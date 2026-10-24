using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LevelLibrary;

/// <summary>换版自检：启动时把 mod 依赖的游戏内部结构（类 / 方法 / 字段名、签名、patch、反射成员）
/// 体检一遍写进日志 —— 这些名字是字符串，编译器查不出来，找不到只会静默失效或不挂 patch，
/// 必须在 `Main` 里所有 patch 挂完之后调 `RunStartupChecks()`：挂之前跑会把 patch 全报成“未挂”。
/// `Tick()` 由常驻段在进关卡之后再做一次运行期监控。</summary>
public static class Verifier {
    public static int FailedCount;
    public static int TotalCount;
    public static string Summary = "还没检查";

    private static bool _ran;
    private static bool _tickArmed;
    private static float _tickAt = -1f;
    private static readonly List<string> _failed = new List<string>();


    /// <summary>启动自检（必须在所有 patch 挂完之后调，见类注释）</summary>
    public static void RunStartupChecks() {
        try {
            if (_ran) return;
            _ran = true;
            RunInner();
        } catch (Exception e) {
            try { Lg.Ex("自检自身抛异常", e); } catch { }
        }
    }

    private static void RunInner() {
        var sb = new StringBuilder();
        int total = 0, failed = 0;
        _failed.Clear();
        sb.Append("【换版自检】");
        try { sb.Append("Unity ").Append(Application.unityVersion); } catch { }
        try { sb.Append(" 游戏版本=").Append(Application.version); } catch { }
        try { sb.Append(" 场景=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name); } catch { }

        // ① 关键类型
        var types = new[] {
            "scrConductor", "scrController", "scnEditor", "scnGame", "scnCLS", "scnLevelSelect",
            "scrPlayerManager", "RDInputType_Keyboard", "scrFloor", "HitMargin", "ADOBase",
            "AnyKeyCode", "ButtonState", "InputAction", "scrLevelMaker", "scnEditor",
        };
        var missTypes = new List<string>();
        foreach (var tn in types) {
            total++;
            if (AccessTools.TypeByName(tn) == null) { failed++; missTypes.Add(tn); _failed.Add("类型缺失 " + tn); }
        }
        sb.Append(" ｜ 类型 ").Append(types.Length - missTypes.Count).Append('/').Append(types.Length);
        if (missTypes.Count > 0) sb.Append("（缺: ").Append(string.Join(",", missTypes.ToArray())).Append("）");

        // ② 要 patch / 反射的方法：签名按 3.4 写，找不到就是换版断点
        var missM = new List<string>();
        CheckMethod(ref total, ref failed, missM, typeof(RDInput), "GetState", new[] { "InputAction", "ButtonState" });
        CheckMethod(ref total, ref failed, missM, typeof(RDInput), "GetMain", new[] { "ButtonState" });
        CheckMethod(ref total, ref failed, missM, typeof(RDInput), "GetStateKeys", new[] { "ButtonState" });
        CheckMethod(ref total, ref failed, missM, typeof(AsyncInputManager), "Update", new string[0]);
        CheckMethod(ref total, ref failed, missM, typeof(scrController), "OnPlayerDied",
                    new[] { "scrPlayer", "System.Boolean", "System.Boolean", "System.String", "System.Boolean" });
        CheckMethod(ref total, ref failed, missM, typeof(scrController), "BeatLevel", new string[0]);
        CheckMethod(ref total, ref failed, missM, typeof(scrController), "TogglePauseGame", new string[0]);
        CheckMethod(ref total, ref failed, missM, typeof(scnEditor), "OpenLevel", new[] { "System.String" });
        CheckMethod(ref total, ref failed, missM, typeof(scnEditor), "Start", new string[0]);
        CheckMethod(ref total, ref failed, missM, typeof(UnityEngine.UI.Text), "set_text", new[] { "System.String" });
        CheckMethod(ref total, ref failed, missM, typeof(UnityEngine.EventSystems.StandaloneInputModule), "Process", new string[0]);
        CheckMethod(ref total, ref failed, missM, typeof(UnityEngine.EventSystems.EventSystem), "Update", new string[0]);
        // scnCLS 那几个是"有就冻结、没有就跳过"的，3.4 已移除 FixedUpdate/OnGUI，所以只记不报错
        int clsAlive = 0;
        foreach (var mn in new[] { "Update", "LateUpdate", "FixedUpdate", "OnGUI" }) {
            try { if (AccessTools.Method(typeof(scnCLS), mn) != null) clsAlive++; } catch { }
        }
        sb.Append(" ｜ 方法 ").Append(total - missM.Count).Append('/').Append(total)
          .Append("（scnCLS 冻结可用 ").Append(clsAlive).Append("/4）");

        // ③ patch 复核：PatchAll 对"找不到目标"是静默的，签名写错只会悄悄不挂
        var expect = new List<string>();
        expect.Add(VerifyPatch(typeof(RDInput), "GetState", new[] { "InputAction", "ButtonState" }) ? "RDInput.GetState" : null);
        expect.Add(VerifyPatch(typeof(RDInput), "GetMain", new[] { "ButtonState" }) ? "RDInput.GetMain" : null);
        expect.Add(VerifyPatch(typeof(RDInput), "GetStateKeys", new[] { "ButtonState" }) ? "RDInput.GetStateKeys" : null);
        expect.Add(VerifyPatch(typeof(AsyncInputManager), "Update", new string[0]) ? "AsyncInputManager.Update" : null);
        expect.Add(VerifyPatch(typeof(scrController), "OnPlayerDied",
                    new[] { "scrPlayer", "System.Boolean", "System.Boolean", "System.String", "System.Boolean" }) ? "scrController.OnPlayerDied" : null);
        expect.Add(VerifyPatch(typeof(scrController), "BeatLevel", new string[0]) ? "scrController.BeatLevel" : null);
        expect.Add(VerifyPatch(typeof(scnEditor), "OpenLevel", new[] { "System.String" }) ? "scnEditor.OpenLevel" : null);
        expect.Add(VerifyPatch(typeof(scnEditor), "Start", new string[0]) ? "scnEditor.Start" : null);
        expect.Add(VerifyPatch(typeof(UnityEngine.EventSystems.StandaloneInputModule), "Process", new string[0]) ? "StandaloneInputModule.Process" : null);
        expect.Add(VerifyPatch(typeof(UnityEngine.EventSystems.EventSystem), "Update", new string[0]) ? "EventSystem.Update" : null);
        int notPatched = 0;
        foreach (var e in expect) if (e == null) notPatched++;
        total += expect.Count; failed += notPatched;
        sb.Append(" ｜ patch ").Append(expect.Count - notPatched).Append('/').Append(expect.Count);

        // ④ 反射取的成员：这些是静默取值的地方，少了不会崩、只会功能不对
        var missMem = new List<string>();
        CheckMember(ref total, ref failed, missMem, typeof(scrConductor), "bpm");
        CheckMember(ref total, ref failed, missMem, typeof(scnEditor), "customLevel");
        CheckMember(ref total, ref failed, missMem, typeof(ADOBase), "levelPath");
        CheckMember(ref total, ref failed, missMem, typeof(scnGame), "levelPath");
        // `scnGame.customLevel` 在 3.4 改名成 `levelData`：两个都查，有一个在就算过
        total++;
        bool scnGameLevel = false;
        try {
            scnGameLevel = (typeof(scnGame).GetField("levelData", AccessTools.all) != null)
                        || (typeof(scnGame).GetField("customLevel", AccessTools.all) != null);
        } catch { }
        if (!scnGameLevel) { failed++; missMem.Add("scnGame.levelData/customLevel"); _failed.Add("成员缺失 scnGame.levelData/customLevel"); }
        sb.Append(" ｜ 反射成员 ").Append(total - failed).Append('/').Append(total);
        if (missMem.Count > 0) sb.Append("（缺: ").Append(string.Join(",", missMem.ToArray())).Append("）");

        TotalCount = total; FailedCount = failed;
        Summary = failed == 0 ? $"通过（{total} 项）" : $"发现 {failed} 处（共 {total} 项）";
        sb.Append(" ->  ").Append(Summary);
        Lg.Info(sb.ToString());

        // 失败清单 + 专项转储（只有出错时才打，正常一次都不刷）
        if (_failed.Count > 0) {
            var sb2 = new StringBuilder("【换版自检·失败清单】");
            foreach (var f in _failed) sb2.Append("\n   ✘ ").Append(f);
            Lg.Info(sb2.ToString());
            DumpForDiagnosis();
        }
        _tickArmed = true;
        _tickAt = -1f;
    }

    /// <summary>出错时的专项转储：把可疑类型的真实成员列出来，用于判断"字段/方法被移到哪了、改名了"。
    /// 平时不打，免得刷爆日志。</summary>
    private static void DumpForDiagnosis() {
        var sb = new StringBuilder("【换版专项转储】");
        try {
            sb.Append("\n [scnCLS] 名字含 Update/GUI/Key 的方法：");
            foreach (var m in typeof(scnCLS).GetMethods(AccessTools.all)) {
                if (m.DeclaringType == typeof(object)) continue;
                if (m.Name.IndexOf("Update", StringComparison.OrdinalIgnoreCase) < 0
                 && m.Name.IndexOf("GUI", StringComparison.OrdinalIgnoreCase) < 0
                 && m.Name.IndexOf("Key", StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.Append("\n    ").Append(Sig(m));
            }
            sb.Append("\n [RDInputType_Keyboard] 全部方法：");
            foreach (var m in typeof(RDInputType_Keyboard).GetMethods(AccessTools.all)) {
                if (m.DeclaringType == typeof(object)) continue;
                sb.Append("\n    ").Append(Sig(m));
            }
            sb.Append("\n [RDInput] 输入入口：");
            foreach (var m in typeof(RDInput).GetMethods(AccessTools.all)) {
                if (m.DeclaringType == typeof(object)) continue;
                if (m.Name.IndexOf("Get", StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.Append("\n    ").Append(Sig(m));
            }
            sb.Append("\n [scnGame] 字段：");
            foreach (var f in typeof(scnGame).GetFields(AccessTools.all)) {
                if (f.Name.IndexOf("level", StringComparison.OrdinalIgnoreCase) < 0
                 && f.Name.IndexOf("custom", StringComparison.OrdinalIgnoreCase) < 0) continue;
                sb.Append("\n    ").Append(f.FieldType.Name).Append(' ').Append(f.Name);
            }
        } catch (Exception e) { sb.Append("\n (转储异常: ").Append(e.Message).Append(')'); }
        Lg.Info(sb.ToString());
    }

    /// <summary>运行期监控（常驻段调，只做一次）：进关卡之后才有的东西在这里再确认一遍</summary>
    public static void Tick() {
        if (!_tickArmed) return;
        if (_tickAt < 0f) { _tickAt = Time.unscaledTime + 20f; return; }
        if (Time.unscaledTime < _tickAt) return;
        _tickAt = float.MaxValue;
        try {
            var sb = new StringBuilder("【运行期监控】");
            string scn = "?";
            try { scn = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            sb.Append(" 场景=").Append(scn);
            try { var c = scrConductor.instance; sb.Append(" scrConductor=").Append(c == null ? "null" : "有"); } catch { }
            try { var ctl = scrController.instance; sb.Append(" scrController=").Append(ctl == null ? "null" : "有"); } catch { }
            sb.Append(" 谱面=").Append(LevelScanner.Entries != null ? LevelScanner.Entries.Count : -1);
            sb.Append(" 自检=").Append(Summary);
            Lg.Info(sb.ToString());
        } catch (Exception e) {
            try { Lg.Ex("运行期监控失败", e); } catch { }
        }
    }

    // ================= 小工具 =================

    private static MethodInfo Find(Type t, string name, string[] argNames) {
        try {
            if (argNames != null && argNames.Length > 0) {
                var args = new Type[argNames.Length];
                for (int i = 0; i < argNames.Length; i++) {
                    args[i] = AccessTools.TypeByName(argNames[i]);
                    if (args[i] == null) { args = null; break; }
                }
                if (args != null) {
                    var m = AccessTools.Method(t, name, args);
                    if (m != null) return m;
                }
            }
        } catch { }
        try { return AccessTools.Method(t, name); } catch { return null; }
    }

    private static void CheckMethod(ref int total, ref int failed, List<string> miss, Type t, string name, string[] argNames) {
        total++;
        if (Find(t, name, argNames) == null) { failed++; miss.Add(t.Name + "." + name); _failed.Add("方法缺失 " + t.Name + "." + name); }
    }

    private static void CheckMember(ref int total, ref int failed, List<string> miss, Type t, string name) {
        total++;
        bool ok = false;
        try {
            ok = t.GetField(name, AccessTools.all) != null || t.GetProperty(name, AccessTools.all) != null;
        } catch { }
        if (!ok) { failed++; miss.Add(t.Name + "." + name); _failed.Add("成员缺失 " + t.Name + "." + name); }
    }

    /// <summary>复核某个方法到底有没有被 patch 过（挂上了返回 true）：
    /// 能发现"签名写错 → 静默没挂"这类问题，这正是换版后最常见的一种坏法。</summary>
    private static bool VerifyPatch(Type t, string name, string[] argNames) {
        try {
            var m = Find(t, name, argNames);
            if (m == null) return false;
            var info = HarmonyLib.Harmony.GetPatchInfo(m);
            int n = 0;
            if (info != null) n = info.Prefixes.Count + info.Postfixes.Count + info.Transpilers.Count + info.Finalizers.Count;
            return n > 0;
        } catch { return false; }
    }

    private static string Sig(MethodInfo m) {
        var ps = new List<string>();
        foreach (var p in m.GetParameters()) ps.Add(p.ParameterType.Name);
        return m.ReturnType.Name + " " + m.Name + "(" + string.Join(",", ps.ToArray()) + ")";
    }
}
