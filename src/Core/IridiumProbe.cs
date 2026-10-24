using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LevelLibrary;

/// <summary>Iridium 联动的探针（只读、只打日志）：把 Iridium 里跟"装饰物分帧加载"沾边的静态成员抓出来，
/// 每 0.5 秒采样一次、值变了才打，用于确认它的加载进度到底能从外部读到哪个字段。
/// 默认关闭：mod 目录放一个 debug_iridium.txt 才开（跟 PerfDiag 同一套路，不给玩家留噪音）。
/// 每一步都单独 try/catch：读别人的静态字段会触发它的静态构造，有的成员一读就抛，一个挂掉不能连坐整轮采样。</summary>
public static class IridiumProbe {
    private const string FlagFile = "debug_iridium.txt";
    /// <summary>采样间隔。0.1 秒太密：单次采样实测最贵 51.5ms，一秒里有一半时间在做反射；
    /// 0.5 秒既看得见"在涨"（装饰物加载好几百毫秒），又把开销砍到 1/5。</summary>
    private const float SampleSeconds = 0.5f;
    /// <summary>单次采样的耗时上限（毫秒），超了就自己关掉。
    /// 探针读的是别的 mod 的属性，而属性 getter 可以跑任意代码（Iridium 的 BatchedCount 那种一读就扫全表），
    /// 我们自己"看起来很便宜"的反射读实际可能一次几十毫秒，必须自带保险丝。</summary>
    private const double SampleMaxMs = 5.0;
    /// <summary>探针自己偷帧到超限就彻底停手（本局不再采，要再开就重启游戏）</summary>
    private static bool _autoOff;

    private static bool _checked, _on, _scanned;
    private static float _next;
    private static float _nextScan;
    private static int _miss;
    private static readonly List<MemberInfo> _watch = new List<MemberInfo>();
    private static readonly Dictionary<string, string> _last = new Dictionary<string, string>();
    private static readonly HashSet<string> _warned = new HashSet<string>();

    /// <summary>探针是否开启（读一次盘，之后走内存）</summary>
    public static bool Enabled { get { return On(); } }

    private static bool On() {
        if (_checked) return _on;
        _checked = true;
        try { _on = File.Exists(Path.Combine(Main.ModEntry.Path, FlagFile)); } catch { }
        if (_on) Lg.Info("【Iridium探针】已开启（删掉 mod 目录里的 " + FlagFile + " 就关闭）");
        return _on;
    }


    /// <summary>常驻段每帧调（跟 PerfDiag.Tick 一样）：进谱面时面板是关的，挂 TickPanel 就采不到</summary>
    public static void Tick() {
        if (!On()) return;
        if (_autoOff) return;                 // 自己太贵，已经停手了
        float now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + SampleSeconds;
        try {
            // 找不到 Iridium 时必须继续重试：游戏启动早期它还没加载完，
            // 写成"扫一次就再也不扫"就会永远扫不到，所以没找到就每 2 秒重试，只有真的找到了才收手
            if (!_scanned) {
                if (now < _nextScan) return;
                _nextScan = now + 2f;
                _miss++;
                if (!Dump()) {
                    // 别每 2 秒刷一行：第一次 + 之后每 15 次（约 30 秒）报一次
                    if (_miss == 1 || _miss % 15 == 0)
                        Lg.Info($"【Iridium探针】还没等到 Iridium 加载（第 {_miss} 次尝试 每 2 秒重试）");
                    return;
                }
                _scanned = true;
            }
            Sample();
        } catch (Exception e) {
            Lg.Info("【Iridium探针】本轮异常：" + e.GetType().Name + " " + e.Message);
        }
    }

    // ================= 一、先把它有什么东西打出来（找到才收手）=================

    /// <summary>扫一遍并打印。返回 false = 还没找到 Iridium（下次再来），别一次性写死</summary>
    private static bool Dump() {
        var asms = new List<Assembly>();
        try {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
                string n = null;
                try { n = a.GetName().Name; } catch { }
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("Iridium", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Iris", StringComparison.OrdinalIgnoreCase) >= 0) asms.Add(a);
            }
        } catch { }

        if (asms.Count == 0) return false;

        Lg.Info($"【Iridium探针】找到 {asms.Count} 个相关程序集 开始列类型（关键词：Loading / Decoration / Batch / Progress）");
        var interesting = new List<Type>();
        int totalTypes = 0;
        foreach (var a in asms) {
            Type[] ts = null;
            try { ts = a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { ts = ex.Types; }
            catch (Exception e) { Lg.Info("【Iridium探针】" + a.GetName().Name + " 类型读不出来：" + e.GetType().Name); }
            if (ts == null) continue;
            string an;
            try { an = a.GetName().Name; } catch { an = "?"; }
            int cnt = 0;
            foreach (var t in ts) {
                if (t == null) continue;
                cnt++;
                if (IsInteresting(t)) interesting.Add(t);
            }
            totalTypes += cnt;
            Lg.Info($"  · 程序集 {an}：{cnt} 个类型");
        }
        Lg.Info($"【Iridium探针】一共 {totalTypes} 个类型，其中与加载/装饰物/进度沾边的 {interesting.Count} 个");

        // 类型清单（只列名字，便于在外面认人）
        var names = new List<string>();
        foreach (var t in interesting) {
            try { names.Add(t.FullName); } catch { }
        }
        names.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var n in names) sb.Append("\n    ").Append(n);
        Lg.Info("【Iridium探针】沾边的类型清单：" + sb);

        // 收集要盯的静态成员
        foreach (var t in interesting) CollectStatics(t, true);
        // 再广撒一层：所有类型里名字像"进度/计数"的静态成员
        foreach (var a in asms) {
            Type[] ts = null;
            try { ts = a.GetTypes(); } catch (ReflectionTypeLoadException ex) { ts = ex.Types; } catch { }
            if (ts == null) continue;
            foreach (var t in ts) {
                if (t == null) continue;
                CollectStatics(t, false);
            }
        }
        Lg.Info($"【Iridium探针】要盯的静态成员共 {_watch.Count} 个（每 {SampleSeconds} 秒采样一次、只在值变化时打印）");
        if (_watch.Count == 0)
            Lg.Info("【Iridium探针】一个可读的静态成员都没有 —— 说明它的进度大概存在实例上（迭代器/补丁实例）");

        // 就算读不到值，也要把成员名单打出来：靠名字才能判断它的进度到底叫什么、藏在哪个类里
        Lg.Info("【Iridium探针】关键类型的成员名单（含非公开 名字就是线索）：");
        int shownTypes = 0;
        foreach (var t in interesting) {
            string tn;
            try { tn = t.FullName; } catch { continue; }
            if (tn.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) < 0 &&
                tn.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) < 0 &&
                tn.IndexOf("Spread", StringComparison.OrdinalIgnoreCase) < 0 &&
                tn.IndexOf("Decoration", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (shownTypes >= 25) { Lg.Info("  ◆ …（成员名单到 25 个类型为止 免得刷爆日志）"); break; }
            shownTypes++;
            var sb2 = new StringBuilder();
            sb2.Append("  ◆ ").Append(tn).Append(" :");
            try {
                const BindingFlags AF = BindingFlags.Public | BindingFlags.NonPublic
                                      | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                int n = 0;
                foreach (var f in t.GetFields(AF)) { if (n++ > 60) break; sb2.Append(' ').Append(f.Name); }
                foreach (var p in t.GetProperties(AF)) { if (n++ > 60) break; sb2.Append(" [P]").Append(p.Name); }
                foreach (var m in t.GetMethods(AF)) {
                    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_")) continue;
                    if (n++ > 60) break;
                    sb2.Append(" [M]").Append(m.Name);
                }
            } catch { sb2.Append("（成员读不出来）"); }
            Lg.Info(sb2.ToString());
        }
        return true;
    }

    /// <summary>这个类型值不值得关注（按名字关键词）</summary>
    private static bool IsInteresting(Type t) {
        try {
            string n = t.FullName;
            if (string.IsNullOrEmpty(n)) return false;
            if (n.IndexOf("Iridium", StringComparison.Ordinal) < 0 &&
                n.IndexOf("Iris", StringComparison.Ordinal) < 0) return false;
            return HasKey(n);
        } catch { return false; }
    }

    private static readonly string[] Keys = {
        "Loading", "Load", "Decoration", "Batch", "Progress", "Spread", "Pending", "Coroutine"
    };

    private static bool HasKey(string s) {
        for (int i = 0; i < Keys.Length; i++)
            if (s.IndexOf(Keys[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>数字类静态成员才盯（进度/计数就是这些）；onlyKeyed = 只收名字带关键词的。
    /// 集合类型也要收：它的待处理队列（FrameSpreadDecorationLoadingPatch._pendingDecorations）是个 List，
    /// 而"还剩多少个"正是进度条的分子分母，被 Numberish 过滤器挡掉就什么都看不到。</summary>
    private static void CollectStatics(Type t, bool onlyKeyed) {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        try {
            foreach (var f in t.GetFields(F)) {
                if (!Numberish(f.FieldType) && !IsCollection(f.FieldType)) continue;
                if (!HasKey(f.Name)) continue;
                Add(f, t.FullName + "." + f.Name);
            }
        } catch { }
        try {
            foreach (var p in t.GetProperties(F)) {
                if (!p.CanRead) continue;
                if (p.GetIndexParameters().Length != 0) continue;
                if (!Numberish(p.PropertyType) && !IsCollection(p.PropertyType)) continue;
                if (!HasKey(p.Name)) continue;
                Add(p, t.FullName + "." + p.Name);
            }
        } catch { }
    }

    private static void Add(MemberInfo m, string id) {
        for (int i = 0; i < _watch.Count; i++) {
            try { if (_watch[i].Name == m.Name && _watch[i].DeclaringType == m.DeclaringType) return; } catch { }
        }
        _watch.Add(m);
        _last[id] = null;
    }

    private static bool Numberish(Type t) {
        if (t == null) return false;
        return t == typeof(int) || t == typeof(float) || t == typeof(bool) || t == typeof(double)
            || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(string)
            || t == typeof(uint) || t == typeof(ushort);
    }

    private static bool IsCollection(Type t) {
        try { return t != null && t != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(t); }
        catch { return false; }
    }

    /// <summary>把一个值描述成一行（集合就报它的个数，待处理队列的"还剩多少个"就是这么读出来的）</summary>
    private static string Describe(object v) {
        try {
            if (v == null) return "null";
            if (v is float f) return f.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            if (v is string s) return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
            if (v is System.Collections.ICollection col) return "Count=" + col.Count;
            // 非 ICollection 的 IEnumerable 不逐个枚举：那可能是别的 mod 的惰性序列 / 生成器，
            // 枚举一次就是让它干一整轮活（限个数只挡死循环，挡不住"很贵"），所以只报类型名
            if (v is System.Collections.IEnumerable) return "(" + v.GetType().Name + ")";
            return v.ToString();
        } catch (Exception e) { return "（读值失败 " + e.GetType().Name + "）"; }
    }

    // ================= 二、每 0.5 秒采样一次（只打变化）=================

    private static void Sample() {
        // 给这一次采样计时，超限就自己关掉（见 SampleMaxMs）
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        int changed = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < _watch.Count; i++) {
            var m = _watch[i];
            string id;
            try { id = m.DeclaringType.FullName + "." + m.Name; } catch { continue; }
            string val;
            try {
                object v = (m is FieldInfo fi) ? fi.GetValue(null) : ((PropertyInfo)m).GetValue(null, null);
                val = Describe(v);
            } catch (Exception e) {
                // 一读就抛的成员只报一次（静态构造炸了之类的），不刷屏
                if (_warned.Add(id)) Lg.Info("【Iridium探针】读不了 " + id + "：" + e.GetType().Name);
                continue;
            }
            string old;
            if (_last.TryGetValue(id, out old) && old == val) continue;
            _last[id] = val;
            sb.Append("\n    ").Append(id).Append(" = ").Append(val);
            changed++;
        }
        if (changed > 0) Lg.Info($"【Iridium探针】变化 {changed} 项：" + sb);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
        if (ms > SampleMaxMs) {
            _autoOff = true;
            Lg.Info($"【Iridium探针】 单次采样花了 {ms:F1}ms（上限 {SampleMaxMs:F0}ms）"
                + "-> 探针自动关闭（它读的是别的 mod 的属性，getter 里可能有重活）"
                + " —— 要再开就重启游戏（顺便说明：这个开关本来就不该长期开着）");
        }
    }
}
