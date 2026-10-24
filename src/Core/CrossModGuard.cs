using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace LevelLibrary;

/// <summary>跨 mod 冲突防御：把非主线程的 Unity 场景查找调用搬到主线程去执行。
/// 别的 mod（如 Together）会在线程池线程里调主线程专属的 FindObjectsByType / FindFirstObjectByType，
/// 这类调用平时侥幸能过，撞上场景加载/卸载（对象表正在销毁）就是 native 崩，崩的不是咱们的代码。
/// 做法是在引擎入口挂 Prefix：非主线程的调用请主线程（Tick，每帧跑）代查，把真实结果还回去；
/// 主线程正忙时最多等 WaitMs，超时返回空结果（那种线程本来也不该查场景对象），绝不碰对象表。
/// 主线程自己的调用只比一次线程 ID，不受影响。日志里的 `【跨mod防御】` 行 = 又有 mod 在非主线程乱查。</summary>
internal static class CrossModGuard {

    /// <summary>主线程等待超时（毫秒）。超时不是错误：说明主线程正忙（加载场景 / 解码音乐），
    /// 返回空结果比冒险去碰对象表安全得多。</summary>
    private const int WaitMs = 250;

    /// <summary>待处理请求上限：超过就直接返回空，防某人在循环里狂查把队列撑爆。</summary>
    private const int MaxPending = 64;

    private static int _mainThreadId;
    private static int _logCount;
    private const int MaxLog = 12;

    private sealed class Req {
        public MethodInfo M;
        public object[] Args;
        public object Result;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
    }

    private static readonly object _lock = new object();
    // 用 List 手动当 FIFO（不能用 Queue<T>：游戏自带的 System.dll 是裁剪过的，编译时找不到它的类型转发）
    private static readonly List<Req> _queue = new List<Req>();

    /// <summary>挂补丁：UnityEngine.Object 上"按 Type 查"的 Find*（泛型版内部会调它们，不用单独挂）
    /// + UnityEngine.GameObject 上按名字/标签查的 Find*。</summary>
    public static void Install(HarmonyLib.Harmony harmony) {
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        if (harmony == null) { Lg.Info("跨mod防御：没拿到 Harmony 实例，跳过"); return; }
        int ok = 0;
        var names = new List<string>();
        // Object：FindObjectsByType / FindFirstObjectByType / FindAnyObjectByType / FindObjectsOfType / FindObjectOfType…
        ok += PatchFamily(harmony, typeof(UnityEngine.Object), m => {
            var ps = m.GetParameters();
            return ps.Length > 0 && ps[0].ParameterType == typeof(Type);
        }, names);
        // GameObject：Find / FindWithTag / FindGameObjectWithTag / FindGameObjectsWithTag
        ok += PatchFamily(harmony, typeof(UnityEngine.GameObject), m => true, names);

        Lg.Info($"[LevelLib] 跨mod防御：已挂 {ok} 个 Unity 查找补丁（非主线程调用会转交主线程执行，超时才返回空）"
            + (names.Count > 0 ? " —— " + string.Join("、", names.ToArray()) : ""));
    }

    /// <summary>把某个类型上所有 Find* 静态方法（跳过泛型定义）逐个挂上 Prefix，返回成功数</summary>
    private static int PatchFamily(HarmonyLib.Harmony harmony, Type host, Func<MethodInfo, bool> accept, List<string> names) {
        int ok = 0, skipped = 0;
        var skippedNames = new List<string>();
        MethodInfo[] methods;
        try { methods = host.GetMethods(BindingFlags.Public | BindingFlags.Static); }
        catch (Exception e) { Lg.Ex("跨mod防御：枚举方法失败 " + host.Name, e); return 0; }

        foreach (var m in methods) {
            try {
                if (m.IsGenericMethodDefinition) continue;              // 泛型版内部会调非泛型版
                if (!m.Name.StartsWith("Find", StringComparison.Ordinal)) continue;
                if (!accept(m)) continue;
                // 引擎里带 [FreeFunction] 的 extern 方法没有 IL 体（如 Object.FindObjectsOfType(Type,bool)、
                // FindObjectsByType(Type,FindObjectsInactive,FindObjectsSortMode)、FindObjectsOfTypeIncludingAssets(Type)），
                // 硬挂会在 JIT 时报 InvalidProgramException，直接跳过；对应的托管重载都挂上了，泛型版内部会调它们，覆盖面不受影响。
                if (!Main.HasPatchableBody(m)) {
                    skipped++;
                    if (skippedNames.Count < 8) skippedNames.Add(host.Name + "." + m.Name + "/" + m.GetParameters().Length);
                    continue;
                }
                // 返回 void 的重载（如 GameObject.FindGameObjectsWithTag(string, List<GameObject>)）不能用带 __result 的
                // prefix：Harmony 会报 "Cannot get result from void method"，得换 PrefixVoid。
                bool isVoid = m.ReturnType == typeof(void);
                harmony.Patch(m, prefix: new HarmonyLib.HarmonyMethod(typeof(CrossModGuard), isVoid ? nameof(PrefixVoid) : nameof(Prefix)));
                ok++;
                names.Add(host.Name + "." + m.Name + "/" + m.GetParameters().Length);
            } catch (Exception e) {
                // 只记 Message：Harmony 的失败原因在 Message 里，堆栈对排查没帮助却会写很长
                Lg.Info("跨mod防御：挂补丁失败 " + host.Name + "." + m.Name + " —— " + e.GetType().Name + ": " + e.Message);
            }
        }
        if (skipped > 0) {
            Lg.Info($"跨mod防御：{host.Name} 上 {skipped} 个 Find* 是引擎 extern（没有 IL 体）已跳过"
                + (skippedNames.Count > 0 ? " —— " + string.Join("、", skippedNames.ToArray()) : "")
                + "（对应的托管重载都在，泛型版内部会调它们）");
        }
        return ok;
    }

    /// <summary>Harmony Prefix：非主线程调用 → 转交主线程；主线程调用 → 原样执行。</summary>
    public static bool Prefix(MethodBase __originalMethod, object[] __args, ref object __result) {
        if (_mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == _mainThreadId) return true;   // 主线程：不干预
        var mi = __originalMethod as MethodInfo;
        object res = MarshalToMain(mi, __args);
        if (res == null) {
            // 超时/队列满/主线程代查失败 → 自己造"空结果"：元素类型必须对，泛型包装的 (T[]) 转换才不会抛
            var t = (__args != null && __args.Length > 0) ? __args[0] as Type : null;
            var rt = mi != null ? mi.ReturnType : null;
            if (rt != null && rt.IsArray) {
                var et = rt.GetElementType() ?? t ?? typeof(UnityEngine.Object);
                try { res = Array.CreateInstance(et, 0); } catch { res = null; }
            }
        }
        __result = res;
        if (Interlocked.Increment(ref _logCount) <= MaxLog) {
            var t = (__args != null && __args.Length > 0) ? __args[0] : null;
            Lg.Info("【跨mod防御】拦下非主线程的 " + (mi != null ? mi.Name : "Find*")
                + "(" + (t != null ? t.ToString() : "?") + ") → 已由主线程代查（没让那条线程碰 Unity 对象表）"
                + CallerTag());
        }
        return false;   // 不执行原方法
    }

    /// <summary>给返回 void 的 Find* 重载用的 Prefix（结果填进传入的列表）。
    /// 这类方法挂不上带 `__result` 的 prefix（Harmony 会报 "Cannot get result from void method"），
    /// 所以单独来一个：非主线程调用一样转交主线程那一份、超时就跳过原方法（调用方拿到空列表，与返回空数组语义一致）。</summary>
    public static bool PrefixVoid(MethodBase __originalMethod, object[] __args) {
        if (_mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == _mainThreadId) return true;
        MarshalToMain(__originalMethod as MethodInfo, __args);
        if (Interlocked.Increment(ref _logCount) <= MaxLog) {
            Lg.Info("【跨mod防御】拦下非主线程的 " + (__originalMethod != null ? __originalMethod.Name : "Find*")
                + "（void 重载）→ 已由主线程代查（没让那条线程碰 Unity 对象表）" + CallerTag());
        }
        return false;
    }

    /// <summary>日志尾巴：是谁在非主线程乱查 —— 调用方的类型 + 程序集名，跳过我们自己的栈帧、Harmony 包装
    /// 与引擎/系统帧，落到第一个外来的 mod。只在真要写日志那一刻才取（StackTrace 不便宜）。</summary>
    private static string CallerTag() {
        try {
            var st = new System.Diagnostics.StackTrace(false);
            for (int i = 0; i < st.FrameCount && i < 12; i++) {
                var fr = st.GetFrame(i);
                if (fr == null) continue;
                var m = fr.GetMethod();
                if (m == null || m.DeclaringType == null) continue;
                string asm = "";
                try { asm = m.DeclaringType.Assembly.GetName().Name; } catch { }
                if (asm == "LevelLibrary" || asm.Length == 0) continue;
                if (asm.StartsWith("0Harmony") || asm.StartsWith("HarmonyLib")) continue;
                if (asm.StartsWith("UnityEngine") || asm.StartsWith("Unity.")) continue;
                if (asm == "mscorlib" || asm.StartsWith("System")) continue;
                return "  ← 调用方：" + m.DeclaringType.FullName + " @ " + asm;
            }
        } catch { }
        return "  ← 调用方：认不出来（栈里只有引擎/Harmony 帧）";
    }

    /// <summary>非主线程侧：把请求交给主线程，等结果（超时/排队过多 → 返回 null，调用方会用空结果兜底）</summary>
    private static object MarshalToMain(MethodInfo mi, object[] args) {
        if (mi == null) return null;
        var req = new Req { M = mi, Args = args };
        lock (_lock) {
            if (_queue.Count >= MaxPending) return null;
            _queue.Add(req);
        }
        try {
            if (!req.Done.Wait(WaitMs)) return null;
            return req.Result;
        } catch { return null; } finally { try { req.Done.Dispose(); } catch { } }
    }

    /// <summary>主线程侧：每帧把待办查完（在 Tick 里调用，面板开没开都要跑）</summary>
    public static void DrainOnMainThread() {
        if (_mainThreadId == 0) return;
        while (true) {
            Req r;
            lock (_lock) {
                if (_queue.Count == 0) return;
                r = _queue[0];
                _queue.RemoveAt(0);
            }
            try {
                // 这里 Invoke 的是被 Harmony 补丁包过的方法，会再回到 Prefix；当前是主线程，Prefix 直接放行不会递归
                r.Result = r.M.Invoke(null, r.Args);
            } catch (Exception e) {
                r.Result = null;
                if (Interlocked.Increment(ref _logCount) <= MaxLog)
                    Lg.Info("【跨mod防御】主线程代查 " + r.M.Name + " 失败：" + e.GetType().Name);
            }
            try { r.Done.Set(); } catch { }
        }
    }

}
