using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace LevelLibrary;

/// <summary>Iridium 联动：把「装饰物分帧加载」的进度接到这边，让进入过渡层等到装饰物造完再淡出。
/// 根因：过渡层的就绪判定只看砖块，而 Iridium 是进场景之后才开始造装饰物。
/// 安全底线：探不到字段（Available=false）、没确认过字段（_trusted=false）、超时、异常 → 一律放行，
/// 绝不把玩家扣在过渡层里。</summary>
public static class IridiumLink {
    /// <summary>联动总开关（设置页「Iridium 联动」那一行）；关掉等于没有这个功能。</summary>
    public static bool Enabled = true;

    /// <summary>探到了能用的加载字段（false = 没装 Iridium 或字段不认识，此时一律不拦）</summary>
    public static bool Available { get; private set; }
    /// <summary>此刻是否正在加载装饰物</summary>
    public static bool Loading { get; private set; }
    public static int Current { get; private set; }
    public static int Total { get; private set; }
    /// <summary>进度首选源：Iridium 自己那行「加载装饰物 X/Y」文字里的数字，与玩家看到的逐字一致。
    /// 队列口径（_pendingDecorations）只是"本批还剩多少"、会先归零，总数也追不上它；
    /// 读不到这行文字（换语言 / 换 UI 实现 / 找不到组件）就自动退回队列那套，绝不拦路。</summary>
    public static bool HasLabel { get; private set; }
    public static int LabelCurrent { get; private set; }
    public static int LabelTotal { get; private set; }
    /// <summary>这套数值来自哪个字段名（仅日志用）</summary>
    public static string Source { get; private set; }
    public static float Ratio { get { return Total > 0 ? Mathf.Clamp01(Current / (float)Total) : 0f; } }
    /// <summary>是否有总数（Total==0 时进度条只能显示不确定态）</summary>
    public static bool HasCounts { get { return Total > 0; } }

    private const float ScanRetrySeconds = 2f;    // 没找到就每 2 秒重试（Iridium 是异步加载的）
    private const float SampleSeconds = 0.1f;     // 采样间隔（太稀看不出"在动"）
    /// <summary>极端兜底：装饰物进度真卡死时的硬上限（秒），正常加载永远碰不到；
    /// 完全不设的话真卡住会让玩家永久留在进入界面、只能手改 settings 自救。
    /// 600 是按实测大谱面（850 个装饰物等了 138 秒）留的余量，过渡层的 EtMaxWait 620 / EtHardLimit 660 都在它之上。</summary>
    public const float EmergencySeconds = 600f;
    /// <summary>进度数字停滞多久打一行日志；纯诊断，不参与放行。</summary>
    public const float StallSeconds = 3f;

    private static bool _scanned, _trusted, _timeoutLogged;
    private static float _next, _nextScan, _loadingT;
    /// <summary>上一次采样时刻（Time.unscaledTime）：_loadingT 靠两次采样之差累加真实秒数。
    /// 若改成每次固定加 SampleSeconds，掉帧时采样也变稀，"已等"会缩水数倍。</summary>
    private static float _lastSampleT;
    // ---------- 它自己那行进度文字（反射找组件，找到就缓存） ----------
    private static Component _labelComp;
    private static PropertyInfo _labelTextProp;
    private static float _nextLabelScan;
    private static bool _labelLogged;
    private static int _labelTries;
    private static bool _labelGaveUp;
    private const int LabelMaxTries = 90;   // 最多找 90 次 ≈ 90 秒，够它把 UI 建出来
    /// <summary>进度数字不变的累计时长（数字一变就归零）；只做诊断。</summary>
    private static float _stallT;
    private static int _lastCurStall = -1;
    private static bool _stallLogged;
    private static int _miss;
    private static int _lastCur = -1;

    // ---------- 主判据：静态 bool（IsLoading / _isLoading） ----------
    private static PropertyInfo _pLoad;
    private static FieldInfo _fLoad;
    // ---------- 加载标志之二：批量创建（IsBatchCreating / _isBatchCreating） ----------
    private static PropertyInfo _pBatch;
    private static FieldInfo _fBatch;
    // ---------- 进度分子分母：待处理队列（静态集合，Count = 本批还剩多少） ----------
    private static FieldInfo _fPending;
    private static PropertyInfo _pPending;
    /// <summary>分批加载的计数：它的队列是一批批塞的，第一批 500 造完又塞第二批（它自己的显示从 0/500 变成 500/1500）。
    /// pending 上升 = 新的一批：把上一批造完的量累加进 _done，_batchPeak 换成新的峰值。
    /// Total = _done + _batchPeak，Current = _done + 本批已完成；允许 Total 变大、Current 往回缩。</summary>
    private static int _done;
    private static int _batchPeak;
    private static int _lastPendForBatch = -1;
    /// <summary>这一轮已经识别出的批次数（仅日志用）</summary>
    private static int _batchCount;
    /// <summary>定期进度日志的计时</summary>
    private static float _logT;
    private const float ProgressLogSeconds = 10f;
    // ---------- 兜底：成对整数（万一它明确暴露 X/Y） ----------
    private static FieldInfo _fCur, _fTot;
    private static PropertyInfo _pCur, _pTot;


    /// <summary>常驻段每帧调：进谱面时面板是关的，挂在 TickPanel 上就采不到样本。</summary>
    public static void Tick() {
        if (!Enabled) return;
        float now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + SampleSeconds;
        try {
            if (!_scanned) {
                if (now < _nextScan) return;
                _nextScan = now + ScanRetrySeconds;
                _miss++;
                if (!Scan()) {
                    if (_miss == 1 || _miss % 15 == 0)
                        Lg.Info($"【Iridium联动】还没找到加载标志（第 {_miss} 次尝试 每 {ScanRetrySeconds:F0} 秒重试）");
                    return;
                }
                _scanned = true;
            }
            Sample();
        } catch (Exception e) {
            Lg.Info("【Iridium联动】本轮异常：" + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>给过渡层用：装饰物加载完了没。探不到字段 / 没确认过字段 / 超时 / 抛异常 → 一律 true（不拦）。</summary>
    public static bool DecorationsReady() {
        try {
            if (!Enabled || !Available || !_trusted) return true;   // 守不住的东西绝不拦路
            if (!Loading) return true;                              // 它说加载完 → 放行
            if (_loadingT >= EmergencySeconds) {
                if (!_timeoutLogged) {
                    _timeoutLogged = true;
                    Lg.Info($"【Iridium联动】 装饰物进度卡住了 {EmergencySeconds:F0}s 都没走完"
                        + (Total > 0 ? $"（{Current}/{Total}）" : "") + " → 极端兜底放行"
                        + "（正常不该出现 这一行是用来定性的）");
                }
                return true;
            }
            return false;
        } catch { return true; }
    }

    // ---------- 读它自己那行进度文字 ----------

    /// <summary>找并读 Iridium 自己那行「加载装饰物 X/Y」，读到就缓存组件。
    /// 只认场景里的组件（排除 prefab 资源），并且必须排除我们自己那条 LoadText；
    /// 找不到就安静地退回队列口径（换语言 / 换 UI 实现都不该让功能坏掉）。</summary>
    private static void TryReadLabel() {
        try {
            if (_labelComp != null && !_labelComp) { _labelComp = null; _labelTextProp = null; }   // 被销毁了
            if (_labelComp == null) {
                float now = Time.unscaledTime;
                if (now < _nextLabelScan) return;
                _nextLabelScan = now + 1f;                    // 最多每秒找一次 （FindObjects 不便宜 ）
                // 找够次数就放弃：大谱面要等几分钟，每秒遍历一遍所有文字太浪费；
                // 放弃后数字源换成队列那套，等待与放行都不受影响。
                if (_labelTries >= LabelMaxTries) {
                    if (!_labelGaveUp) {
                        _labelGaveUp = true;
                        Lg.Info("【Iridium联动】找不到它自己那行进度文字，进度条改用队列算（换语言/换 UI 都会这样，不影响等待与放行）");
                    }
                    return;
                }
                _labelTries++;
                _labelComp = FindLabelComponent();
                if (_labelComp == null) return;
                _labelTextProp = _labelComp.GetType().GetProperty("text");
                if (_labelTextProp == null) { _labelComp = null; return; }
            }
            string s = null;
            try { s = _labelTextProp.GetValue(_labelComp, null) as string; } catch { }
            int a, b;
            if (!ParsePair(s, out a, out b)) { HasLabel = false; return; }
            LabelCurrent = a; LabelTotal = b; HasLabel = true;
            if (!_labelLogged) {
                _labelLogged = true;
                Lg.Info("【Iridium联动】接上它自己的进度文字（" + _labelComp.GetType().Name + "），当前 " + a + "/" + b + "，进度条以它为准");
            }
        } catch { HasLabel = false; }
    }

    private static Component FindLabelComponent() {
        // 先试具体类型再试基类：后两个是"万一它用的是别的文字组件"的兜底
        foreach (var ty in new[] { FindType("TMPro.TextMeshProUGUI"), FindType("TMPro.TextMeshPro"),
                                   FindType("TMPro.TMP_Text"), typeof(UnityEngine.UI.Text) }) {
            if (ty == null) continue;
            UnityEngine.Object[] arr = null;
            try { arr = Resources.FindObjectsOfTypeAll(ty); } catch { }
            if (arr == null) continue;
            foreach (var o in arr) {
                var c = o as Component;
                if (c == null) continue;
                try {
                    if (!c.gameObject.scene.IsValid()) continue;        // 只要场景里的（排除 prefab 资源）
                    if (c.gameObject.name == "LoadText") continue;      // 我们自己那条
                    var p = c.GetType().GetProperty("text");
                    if (p == null) continue;
                    string s = p.GetValue(c, null) as string;
                    if (!LooksLikeIridiumProgress(s)) continue;
                    return c;
                } catch { }
            }
        }
        return null;
    }

    /// <summary>这串字像不像它的进度行：认「装饰物 / ecoration / 장식」三种关键词，都认不出就不认。</summary>
    private static bool LooksLikeIridiumProgress(string s) {
        if (string.IsNullOrEmpty(s)) return false;
        if (s.StartsWith("正在加载", StringComparison.Ordinal)) return false;   // 我们自己那条
        bool kw = Has(s, "装饰物") || Has(s, "ecoration") || Has(s, "장식");
        if (!kw) return false;
        int a, b;
        return ParsePair(s, out a, out b);
    }

    /// <summary>从一行字里取最后两个整数当 X/Y（「加载装饰物: 666/851」）。
    /// 必须要求串里有 `/`，否则「版本 2.0.1 第 3 项」这种也会被当成 X/Y。</summary>
    private static bool ParsePair(string s, out int a, out int b) {
        a = 0; b = 0;
        if (string.IsNullOrEmpty(s) || s.IndexOf('/') < 0) return false;
        var nums = new List<int>(4);
        int i = 0;
        while (i < s.Length) {
            if (char.IsDigit(s[i])) {
                int j = i;
                while (j < s.Length && char.IsDigit(s[j])) j++;
                int v;
                if (int.TryParse(s.Substring(i, j - i), out v)) nums.Add(v);
                i = j;
            } else i++;
        }
        if (nums.Count < 2) return false;
        a = nums[nums.Count - 2]; b = nums[nums.Count - 1];
        return b > 0 && a >= 0;
    }

    /// <summary>按名字在已加载的程序集里找类型；拿不到返回 null，不抛异常。</summary>
    private static Type FindType(string fullName) {
        try {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
        } catch { }
        return null;
    }

    // ---------- 找字段 ----------

    private static bool Scan() {
        var asms = new List<Assembly>();
        try {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
                string n = null;
                try { n = a.GetName().Name; } catch { }
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("Iridium", StringComparison.OrdinalIgnoreCase) >= 0) asms.Add(a);
            }
        } catch { }
        if (asms.Count == 0) return false;      // Iridium 还没加载完 → 下次再试

        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int scoreLoad = 0, scorePend = 0, scoreBatch = 0, scorePair = 0;

        foreach (var a in asms) {
            Type[] ts = null;
            try { ts = a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { ts = ex.Types; }
            catch { }
            if (ts == null) continue;
            foreach (var t in ts) {
                if (t == null) continue;
                string tn;
                try { tn = t.FullName; } catch { continue; }
                // 类名越像"分帧装饰物加载"那个类，得分越高
                int rel = 0;
                if (Has(tn, "FrameSpread")) rel += 10;
                if (Has(tn, "Loading") && Has(tn, "Decoration")) rel += 8;
                if (Has(tn, "LoadingOptimization")) rel += 4;
                if (Has(tn, "Decoration")) rel += 2;
                if (rel == 0) continue;

                try {
                    foreach (var p in t.GetProperties(F)) {
                        if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                        if (p.PropertyType == typeof(bool) && p.Name.IndexOf("IsLoading", StringComparison.OrdinalIgnoreCase) >= 0) {
                            int sc = rel + 5;
                            if (sc > scoreLoad) { scoreLoad = sc; _pLoad = p; _fLoad = null; }
                        } else if (p.PropertyType == typeof(bool) && Has(p.Name, "BatchCreating")) {
                            int sc = rel + 1;
                            if (sc > scoreBatch) { scoreBatch = sc; _pBatch = p; _fBatch = null; }
                        } else if (IsCollection(p.PropertyType) && Has(p.Name, "pending")) {
                            int sc = rel + 5;
                            if (sc > scorePend) { scorePend = sc; _pPending = p; _fPending = null; }
                        } else if (p.PropertyType == typeof(int) && Any(p.Name, CurKeys)) {
                            if (rel + 2 > scorePair) { _pCur = p; _pTot = null; scorePair = rel + 2; }
                        } else if (p.PropertyType == typeof(int) && Any(p.Name, TotKeys)) {
                            if (scorePair > 0 && _pTot == null) _pTot = p;
                        }
                    }
                    foreach (var f in t.GetFields(F)) {
                        if (f.FieldType == typeof(bool) && f.Name.IndexOf("isLoading", StringComparison.OrdinalIgnoreCase) >= 0) {
                            int sc = rel + 4;   // 私有字段比同名属性低一分（属性更稳）
                            if (sc > scoreLoad) { scoreLoad = sc; _fLoad = f; _pLoad = null; }
                        } else if (f.FieldType == typeof(bool) && Has(f.Name, "BatchCreating")) {
                            int sc = rel;
                            if (sc > scoreBatch) { scoreBatch = sc; _fBatch = f; _pBatch = null; }
                        } else if (IsCollection(f.FieldType) && Has(f.Name, "pending")) {
                            int sc = rel + 5;
                            if (sc > scorePend) { scorePend = sc; _fPending = f; _pPending = null; }
                        } else if (f.FieldType == typeof(int) && Any(f.Name, CurKeys)) {
                            if (rel + 2 > scorePair) { _fCur = f; _fTot = null; scorePair = rel + 2; }
                        } else if (f.FieldType == typeof(int) && Any(f.Name, TotKeys)) {
                            if (scorePair > 0 && _fTot == null) _fTot = f;
                        }
                    }
                } catch { }
            }
        }

        bool hasLoad = _pLoad != null || _fLoad != null;
        bool hasPend = _fPending != null || _pPending != null;
        bool hasPair = (_fCur != null && _fTot != null) || (_pCur != null && _pTot != null);
        if (!hasLoad && !hasBatch() && !hasPair) return false;

        // 名字精确是 IsLoading 的静态 bool → 直接算可信，不等"亲眼见过它变 true"：
        // 它置 true 的时机正好在砖块就绪之后，而我们在同一刻就问 DecorationsReady()，
        // 等见证会永远赶不上第一次进谱面；认错的风险也低（字段恒为 false 时 Loading=false 直接放行）。
        if (hasLoad) {
            _trusted = true;
            Lg.Info("【Iridium联动】加载标志名就是 IsLoading，直接按可信处理（不等先见过 true，那会赶不上第一次进谱面）");
        }

        Available = true;
        var sb = new StringBuilder("【Iridium联动】找到字段：");
        if (hasLoad) sb.Append(" 加载标志=").Append(Nm(_pLoad != null ? (MemberInfo)_pLoad : _fLoad)).Append(",");
        if (hasPend) sb.Append(" 待处理队列=").Append(Nm(_pPending != null ? (MemberInfo)_pPending : _fPending)).Append(",");
        if (hasPair) sb.Append(" 成对整数=").Append(Nm(_fCur != null ? (MemberInfo)_fCur : _pCur))
                                  .Append('/').Append(Nm(_fTot != null ? (MemberInfo)_fTot : _pTot)).Append(",");
        if (hasBatch()) sb.Append(" 另有批量创建标志=").Append(Nm(_pBatch != null ? (MemberInfo)_pBatch : _fBatch));
        Source = (hasLoad ? Nm(_pLoad != null ? (MemberInfo)_pLoad : _fLoad)
                          : (hasPend ? Nm(_pPending != null ? (MemberInfo)_pPending : _fPending) : "pair"));
        Lg.Info(sb.ToString());
        if (!hasPend)
            Lg.Info("【Iridium联动】没找到待处理队列，只能知道「在不在加载」，进度条会显示不确定态");
        return true;
    }

    private static bool hasBatch() { return _pBatch != null || _fBatch != null; }

    // ---------- 采样 ----------

    private static void Sample() {
        if (!Available) return;

        // 采样间隔取两次采样的真实时间差（非固定加 SampleSeconds）：
        // 加载时帧率掉到 2~3 FPS，采样也跟着变稀，固定加会让"已等"和极端兜底都缩水好几倍。
        float nowT = Time.unscaledTime;
        float dtSample = _lastSampleT <= 0f ? SampleSeconds : Mathf.Clamp(nowT - _lastSampleT, 0f, 0.5f);
        _lastSampleT = nowT;

        bool load = ReadBool(_pLoad, _fLoad, false);
        bool batch = ReadBool(_pBatch, _fBatch, false);

        // 待处理队列：Count = 本批还剩多少个没造；-1 = 读不到
        int pend = -1;
        if (_fPending != null) pend = ReadCount(_fPending, false);
        else if (_pPending != null) pend = ReadCount(_pPending, true);

        // 顺序不能反：先判"这一轮加载是不是刚开始"并复位起点，再算进度 ——
        // 反过来的话，同一帧里刚建立的批次状态会被复位清掉，Total 永远是 0、进度条只能显示不确定态。
        bool loading = load || batch || pend > 0;
        bool was = Loading;
        if (loading && !was) {
            _loadingT = 0f; _timeoutLogged = false; _stallLogged = false;
            _done = 0; _batchPeak = 0; _lastPendForBatch = -1;   // 新的一轮 → 分批计数归零
            _batchCount = 0; _logT = 0f;
            Lg.Info("【Iridium联动】这一轮装饰物加载开始（队列起点 "
                + (pend >= 0 ? pend.ToString() : "?") + " 之后每 " + ProgressLogSeconds + "s 报一次进度）");
        }

        if (pend >= 0) {
            // 队列 Count 只是"本批还剩多少"，pending 上升 = 它又扫出了新的一批（0/500 → 500/1500）。
            // 所以分母 = 已完成累计 + 本批峰值、分子 = 已完成累计 + 本批已完成，
            // 允许 Total 变大、Current 往回缩（条跟着变，不能卡住）。
            if (pend > _lastPendForBatch) {
                if (_lastPendForBatch > 0) {
                    _done += (_batchPeak - _lastPendForBatch);   // 上一批造完的
                Lg.Info($"【Iridium联动】又扫出一批装饰物：上一批峰值 {_batchPeak}，累计 {_done}，本批 {pend}，总数 {_done + pend}");
                }
                _batchPeak = pend;
                _batchCount++;
            }
            _lastPendForBatch = pend;
            Total = _done + _batchPeak;
            Current = Total > 0 ? Mathf.Clamp(_done + Mathf.Max(0, _batchPeak - pend), 0, Total) : 0;
        } else {
            // 兜底：成对整数
            int c = ReadInt(_fCur, _pCur, -1);
            int t = ReadInt(_fTot, _pTot, -1);
            if (c >= 0) Current = c;
            if (t >= 0) Total = t;
            if (c >= 0 && _lastCur >= 0 && c > _lastCur) _trusted = true;   // 见过它在涨 → 字段可信
            if (c >= 0) _lastCur = c;
        }

        // 停滞计时：进度数字不再变就累加、一变就归零；不参与放行，
        // 只为"真卡住"那一刻留下证据（配合 StallSeconds 打一行日志）。
        if (Current != _lastCurStall) {
            _lastCurStall = Current; _stallT = 0f;
        } else {
            _stallT += SampleSeconds;
            if (Loading && _stallT >= StallSeconds && !_stallLogged) {
                _stallLogged = true;
                Lg.Info($"【Iridium联动】装饰物进度停在 {Current}/{Total} 已 {StallSeconds:F0}s（继续等，仅留痕）");
            }
        }

        Loading = loading;
        if (loading) {
            TryReadLabel();                       // 读它自己那行文字（数字源优先用它）
            _loadingT += dtSample;                // 真实秒数，不是每采样固定加 0.1
            _logT += dtSample;
            if (_logT >= ProgressLogSeconds) {
                _logT = 0f;
                string src = HasLabel ? $"文字={LabelCurrent}/{LabelTotal}" : "文字读不到 按队列算";
                string queue = pend >= 0 ? $"队列剩 {pend}" : "队列读不到";
                Lg.Info($"【Iridium联动】进度 {Current}/{Total}（{src}，{queue}，已造 {_done}，等了 {_loadingT:F0}s）");
            }
        } else { _loadingT = 0f; _stallT = 0f; _lastCurStall = -1; _lastPendForBatch = -1; HasLabel = false; }
    }

    // ---------- 小工具 ----------

    private static readonly string[] CurKeys = { "current", "loaded", "created", "done", "now", "index", "progress", "count" };
    private static readonly string[] TotKeys = { "total", "all", "max", "target", "sum", "length", "amount", "need" };

    private static bool Has(string s, string k) { return s != null && s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0; }
    private static bool Any(string s, string[] ks) {
        for (int i = 0; i < ks.Length; i++) if (Has(s, ks[i])) return true;
        return false;
    }
    private static bool IsCollection(Type t) {
        try { return t != null && t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t); } catch { return false; }
    }
    private static string Nm(MemberInfo m) {
        try { return m.DeclaringType.FullName + "." + m.Name; } catch { return m.Name; }
    }
    private static bool ReadBool(PropertyInfo p, FieldInfo f, bool dflt) {
        try {
            object v = p != null ? p.GetValue(null, null) : (f != null ? f.GetValue(null) : null);
            if (v == null) return dflt;
            return Convert.ToBoolean(v, System.Globalization.CultureInfo.InvariantCulture);
        } catch { return dflt; }
    }
    private static int ReadInt(FieldInfo f, PropertyInfo p, int dflt) {
        try {
            object v = f != null ? f.GetValue(null) : (p != null ? p.GetValue(null, null) : null);
            if (v == null) return dflt;
            return Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
        } catch { return dflt; }
    }
    private static int ReadCount(MemberInfo m, bool isProp) {
        try {
            object v = isProp ? ((PropertyInfo)m).GetValue(null, null) : ((FieldInfo)m).GetValue(null);
            if (v == null) return -1;
            if (v is ICollection col) return col.Count;
            int n = 0;
            foreach (var _ in (IEnumerable)v) { n++; if (n > 2000000) break; }
            return n;
        } catch { return -1; }
    }
}
