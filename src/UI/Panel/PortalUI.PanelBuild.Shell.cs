// [split-of] PortalUI.PanelBuild.cs lines 1-611 body 17-708

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI.PanelBuild.cs 的一部分：Tick 主循环与常驻段、开面板计时与首帧性能采样、库页展开/收起动画、面板看门狗。</summary>
public static partial class PortalUI {

    // ---------- 开面板分步计时（只在慢的时候输出，用于定位一次性开销） ----------
    private static System.Diagnostics.Stopwatch _openWatch;
    private static long _openLastMs;            // 上一步的累计耗时（用于算增量）
    private static bool _detailBuildPending;   // 详情区延迟到开面板后的下一帧构建
    private static string _uiRestoreSelectedPath;   // 开面板时要沿用的「上次选中」（等详情区建好再落实）
    private static bool _musicStartPending;    // 背景音乐延迟到下一帧开始加载

    /// <summary>开面板分步耗时：同时打「累计」和「本步增量」——增量才是能直接指出哪一步慢的数字。</summary>
    private static void OpenStep(string name) {
        if (_openWatch == null) return;
        long now = _openWatch.ElapsedMilliseconds;
        long delta = now - _openLastMs;
        _openLastMs = now;
        Lg.Info($"[LevelLib] 【开面板】{name}: +{delta}ms（累计 {now}ms）");
    }

    // ---------- 面板打开后的头几帧耗时检测 ----------
    // 体感卡顿常常不是某一次调用慢，而是开面板后紧接着的几帧被延迟构建（详情区 / 列表首帧布局 / 背景图解码）拖住，
    // 所以这里连续采样若干帧，把超过阈值的帧连同 Tick 内部耗时一起打出来。
    private static int _panelProfFrames;         // 还要采样多少帧（0=不采样）
    private static long _prevFrameStartTicks;    // 上一帧开始的时刻（算帧间隔）
    private static long _tickInnerTicks;         // 本帧 Tick 内部已用时间
    private static readonly string[] _phaseName = new string[PanelProfPhaseMax];   // 本帧最贵的几段（名字）
    private static readonly double[] _phaseMs = new double[PanelProfPhaseMax];      // 同上（耗时）
    private static int _tickPhaseCount;          // 本帧已经记了几个阶段（防一行太长）
    private static int _profLogLines;            // 已经打过多少行（防刷爆日志）
    // 采样窗口 = 120 帧（约 2~6 秒，够覆盖「在开始界面拖几下」那几秒）。
    private const int PanelProfFrameCount = 120;
    private const long PanelProfThresholdMs = 12;// 超过这个耗时就报告（约等于掉到 60fps 以下）
    // 单阶段门槛 0.8ms：卡顿常常是「十来个 1~2ms 的小调用凑出来的」，门槛太高就一个都不到线、什么都定不了。
    // 从 1.5 收到 0.8：面板开着时每帧稳定 8~13ms，而 1.5ms 只抓到过「常规Tick」一个笼统段，
    // 定位不到具体是谁在花 —— 收到 0.8 能多抓出几个中间量级的段，一行里最多 12 段不会刷屏。
    private const double PanelProfPhaseMs = 0.8;
    private const int PanelProfPhaseMax = 12;    // 一行里最多记这么多段（够定位，不刷屏）
    private const int PanelProfMaxLines = 200;   // 整个会话的采样行上限（够看又不会撑爆日志）

    // ---------- 每日成就「今天好！！」：等开面板那阵卡顿过去再提示 ----------
    // 开面板那一两秒有卡顿（背景图解码、列表首帧布局、音乐启动），提示窗这时弹会被吞掉。
    // 判据：距离开面板至少 AchSettleMinMs，且连续 AchSettleFrames 帧的帧间隔都低于 AchSmoothMs。
    private static bool _achTodayPending;         // 本次开面板还有没有待触发的每日成就
    private static long _panelOpenTicks;          // 本次开面板的时刻（Stopwatch 刻度）
    private static long _lastPanelFrameTicks;     // 上一帧结尾时刻（算帧间隔）
    private static int _smoothFrames;             // 已经连续多少帧是"平稳帧"
    private const double AchSmoothMs = 20.0;      // 帧间隔低于这个算平稳（约 50fps 以上）
    private const int AchSettleFrames = 12;       // 需要连续这么多平稳帧，约 0.2 秒
    private const double AchSettleMinMs = 700.0;  // 但至少要等开面板后这么久（避开首帧大卡顿）

    private static void StartPanelProfiling() {
        _panelProfFrames = PanelProfFrameCount;
        _prevFrameStartTicks = 0;
        // 这里绝不能再清相位表：它会在「一帧的中途」被调（建个人页 / 主界面 / 开始界面 / 开面板都会调），
        // 清了就会把那一帧前半段记下的相位丢掉，日志里出现「本帧 Tick 很大但没有任何单段超阈值」的自相矛盾行；
        // 每帧开头 TickPhaseBegin() 已经清过一次。
    }

    /// <summary>Tick 内部阶段计时：开始一个阶段（配合 TickPhaseMark）。
    /// 一直记而不只在采样窗口里记（窗口关了的时候慢帧就只剩一个总数、定不了凶手）；标记本身很便宜。
    /// 名额满了按耗时留最大的 12 段 —— 先到先得会把真正吃掉整帧的那一段丢掉。</summary>
    private static void TickPhaseBegin() {
        _tickInnerTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _tickPhaseCount = 0;
    }
    private static void TickPhaseMark(string name) {
        if (_tickInnerTicks == 0) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (now - _tickInnerTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _tickInnerTicks = now;
        if (ms < PanelProfPhaseMs) return;
        if (_tickPhaseCount < PanelProfPhaseMax) {
            int at = _tickPhaseCount++;
            _phaseName[at] = name; _phaseMs[at] = ms;
            return;
        }
        // 名额满了：挤掉当前最小的那一段（直接丢的话丢的往往正是最贵的那一段）
        int worst = 0;
        for (int i = 1; i < _tickPhaseCount; i++) if (_phaseMs[i] < _phaseMs[worst]) worst = i;
        if (ms > _phaseMs[worst]) { _phaseName[worst] = name; _phaseMs[worst] = ms; }
    }

    /// <summary>把本帧记下的几段拼成一行（按耗时从大到小，最贵的排最前）。</summary>
    private static string PhaseText() {
        if (_tickPhaseCount <= 0) return "";
        var order = new int[_tickPhaseCount];
        for (int i = 0; i < _tickPhaseCount; i++) order[i] = i;
        for (int i = 1; i < order.Length; i++) {           // 插入排序（≤12 个，不引 Linq）
            int k = order[i], j = i - 1;
            while (j >= 0 && _phaseMs[order[j]] < _phaseMs[k]) { order[j + 1] = order[j]; j--; }
            order[j + 1] = k;
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < order.Length; i++) {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(_phaseName[order[i]]).Append('=').Append(_phaseMs[order[i]].ToString("F1")).Append("ms");
        }
        return sb.ToString();
    }

    /// <summary>本帧结束：算出帧间隔和 Tick 内部耗时，慢就报告</summary>
    private static void EndPanelProfilingFrame() {
        if (_panelProfFrames <= 0) return;
        _panelProfFrames--;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double frameMs = _prevFrameStartTicks > 0
            ? (now - _prevFrameStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency
            : 0;
        _prevFrameStartTicks = now;
        if (frameMs >= PanelProfThresholdMs && _profLogLines < PanelProfMaxLines) {
            _profLogLines++;
            string pt = PhaseText();
            Lg.Info($"[LevelLib] 【首帧性能】帧间隔 {frameMs:F1}ms"
                + (string.IsNullOrEmpty(pt) ? "（Tick 内无明显单步耗时）" : "  Tick内: " + pt)
                + $"  面板={_panelOpen} 详情区待建={_detailBuildPending} 音乐待启动={_musicStartPending}");
        }
        if (_panelProfFrames == 0 && _profLogLines < PanelProfMaxLines) {
            _profLogLines++;
            Lg.Info($"[LevelLib] 【首帧性能】采样结束（{PanelProfFrameCount} 帧）");
        }
    }
    // ---------- 库页：未选中列表拉满 / 选中详情区展开的动画 ----------

    /// <summary>按进度 e（0 = 详情区展开 / 1 = 详情区收起）摆一次库页布局。
    /// 列表与搜索框只动左边界（右边界取 LibSortRightEdge()），详情区左边界固定、宽度往左收到 0；
    /// 列表上边也动（未选中时提到 158，占住藏起来的搜索框那两行），底边按 LibBottomMargin 拉伸锚定。</summary>
    private static void ApplyLibExpand(float e) {
        float k = e * e * (3f - 2f * e);   // smoothstep：两端慢、中间快
        float left = Mathf.Lerp(SearchBoxX, LibFullLeft, k);
        float top = Mathf.Lerp(ListTop, ListTopFull, k);
        // 右边界 / 滚动条 x 不再是常量：切成「按标记难度」时列表整块右移补满，一律取
        // LibSortRightEdge() / LibSortScrollbarX() —— 这两个函数是唯一的取值点，别在这儿另算一份插值。
        float right = LibSortRightEdge();
        float sbX = LibSortScrollbarX();
        // 底边不是写死的 PanelBottom，而是拉伸锚点 + 距底边距：画布高 1200 时列表 / 滚动条 / 字母条 / 详情面板会自己长到底。
        if (_listViewportRt != null) SetRectPxV(_listViewportRt, left, top, right - left, LibBottomMargin);
        // 滚动条和字母定位条跟列表同高同顶（它们本来就是列表这一块的一部分）
        if (_libScrollbarRt != null) SetRectPxV(_libScrollbarRt, sbX, top, ScrollbarW, LibBottomMargin);
        if (_letterColRt != null) SetRectPxV(_letterColRt, LetterColX, top, LetterColW, LibBottomMargin);
        // 字母条透明度：渐入系数（启动页淡回）×（1 − 排序过渡进度）—— 只有这一个写入点
        ApplyLetterColAlpha();
        if (_libSearchBgRt != null) SetRectPx(_libSearchBgRt, left, SearchBoxTop, SearchRightEdge - left, SearchBoxH);
        // 搜索框未选中时跟难度滑条 / 信息栏一起藏起来，选中后动画快停稳时（k<0.22）才出现；
        // 位置每帧照旧摆好（隐藏时零开销），所以它不会跟着列表滑动。
        // 三处必须用同一个判断 LibChromeVisible()。
        if (_libSearchBgRt != null && _libSearchBgRt.gameObject.activeSelf != LibChromeVisible())
            _libSearchBgRt.gameObject.SetActive(LibChromeVisible());
        // 排序方式那个键跟搜索框同一行，走同一条显隐规则（未选中谱面时整行藏着）
        SyncSortButtonVisible();
        // 按键右端每帧跟列表右边界对齐（列表往右延伸时按键一起变长）
        ApplySortButtonRect();
        if (_countText != null) SetRectPx(_countText.rectTransform, left, CountRowTop, right - left, CountRowH);
        if (_detailAreaRt != null) SetRectPxV(_detailAreaRt, DetailLeftPx, PanelTop, DetailWPx * (1f - k), LibBottomMargin);
        // 内容层在动画期间整层关掉：那几十上百个图形按比例锚定，面板一收窄就要每帧重建矩形、还会被横向挤扁。
        // 这里只让背景那一张图在收（配合下面的淡出），内容快到位时再打开。
        if (_detailInner != null && _detailInner.activeSelf != (k < 0.22f)) _detailInner.SetActive(k < 0.22f);
        // 难度滑条跟详情区内容同一时机显隐，与搜索框 / 滚动信息栏共用 LibChromeVisible()（三处必须一致）。
        // 重新显示时标一下 dirty，让下一帧按真实宽度重算两个滑块的位置。
        if (_rankSliderGo != null && _rankSliderGo.activeSelf != LibChromeVisible()) {
            bool show = LibChromeVisible();
            _rankSliderGo.SetActive(show);
            if (show) _rankRangeDirty = true;
        }
        // 背景淡出（CanvasGroup 影响的只有背景那一张图，开销可忽略）
        if (_detailGroup != null) _detailGroup.alpha = Mathf.Clamp01(1f - k * 4f);
        // 收干净后整块关掉（不绘制、不参与射线）；一开始动就重新打开。
        // 用 activeSelf 判断，避免每帧都调 SetActive（那会每帧触发一次层级变更）
        if (_detailAreaGo != null) {
            bool want = k < 0.999f;
            if (_detailAreaGo.activeSelf != want) _detailAreaGo.SetActive(want);
        }
        // 行是虚拟化的：可视行数按视口高度算、结果被 _listViewportH 缓存住。视口高度一变
        // （未选中时上边提到 158，高了 64px ≈ 一行）不重算就会出现「列表变高了但最下面那行没补出来」。
        _listViewportH = 0f;
        UpdateScrollbarSize();
        if (_libScroll != null) UpdateVisible();
    }

    /// <summary>库页展开/收起动画：只在"没到位"时干活，到位后每帧一次浮点比较就返回。
    /// 顺带采一次样，结束时打一行日志（每次切换一行），用来判断动画本身卡不卡。</summary>
    private static void TickLibExpand() {
        if (_libExpand == _libExpandTarget) {
            if (_animFrames > 0) {
                double total = (System.Diagnostics.Stopwatch.GetTimestamp() - _animStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Lg.Info("【动画】"
                    + (_libExpandTarget > 0.5f ? "收起详情区（列表拉满）" : "展开详情区（列表收回）")
                    + $" 用时={total:F0}ms 帧数={_animFrames} 最长帧={_animMaxFrameMs:F1}ms"
                    + $" 平时最长帧={FrameBaselineMs():F1}ms 行数={_poolRows.Count}"
                    + "（平时帧 = 动画之前最近 30 帧里最慢的一帧，用来判断动画本身有没有加负担）");
                _animFrames = 0; _animMaxFrameMs = 0; _animStartTicks = 0;
            }
            return;
        }
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_animFrames == 0) _animStartTicks = now;
        else {
            double ms = (now - _animLastFrameTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > _animMaxFrameMs) _animMaxFrameMs = ms;
        }
        _animLastFrameTicks = now;
        _animFrames++;
        _libExpand = Mathf.MoveTowards(_libExpand, _libExpandTarget, Time.unscaledDeltaTime / LibExpandDur);
        ApplyLibExpand(_libExpand);
    }

    // ---------- 动画性能采样（每次切换只打一行日志） ----------
    private static long _animStartTicks, _animLastFrameTicks;
    private static int _animFrames;
    private static double _animMaxFrameMs;

    // ---------- 平时帧耗时基线（最近 30 个「没在动画」的帧里最慢的一帧）----------
    // 只看「动画期间最长帧」没法判断那慢帧是咱们造成的还是游戏本来就慢（实测同一批动画前后差到 17ms 到 47ms）。
    private static readonly double[] _frameRing = new double[30];
    private static int _frameRingIdx;
    private static long _frameBaselineLastTicks;
    private static void TickFrameBaseline() {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_frameBaselineLastTicks != 0 && _animFrames == 0) {   // 动画期间不采样，免得把自己的帧算进基线
            double ms = (now - _frameBaselineLastTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _frameRing[_frameRingIdx] = ms;
            _frameRingIdx = (_frameRingIdx + 1) % _frameRing.Length;
        }
        _frameBaselineLastTicks = now;
    }
    private static double FrameBaselineMs() {
        double max = 0;
        for (int i = 0; i < _frameRing.Length; i++) if (_frameRing[i] > max) max = _frameRing[i];
        return max;
    }

    /// <summary>空库那一块的显隐：只有真的一个谱面都没有时才显示（整块容器一起切：提示 + 两个导入按钮）。</summary>
    private static void RefreshEmptyLibHint() {
        bool empty = LevelScanner.Entries == null || LevelScanner.Entries.Count == 0;
        if (_emptyLibBox != null && _emptyLibBox.gameObject.activeSelf != empty) _emptyLibBox.gameObject.SetActive(empty);
        if (_emptyLibHint != null && _emptyLibHint.gameObject.activeSelf != empty) _emptyLibHint.gameObject.SetActive(empty);
        // 两个按钮的字要跟着语言走（建的时候是中文，语言换了 / 重开面板时重刷）
        if (_emptyImportOneText != null) _emptyImportOneText.text = Lang.Tr("单个导入");
        if (_emptyImportBatchText != null) _emptyImportBatchText.text = Lang.Tr("批量导入");
    }

    /// <summary>每帧驱动（由游戏的 Update 调用）
    /// 结构：先跑「与面板无关」的功能（不依赖界面），面板没开就直接跳过后面所有界面逻辑。
     /// 这样面板关闭时每帧只做几次判断，而不是跑二十个 Tick。</summary>
    public static void Tick() {
        // 本帧 Tick 自己花了多久（帧级卡顿那行要用它；两次 Stopwatch 读约几十纳秒）
        long hitchT0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // 帧耗时统计：仅在「记录详细日志」打开时才计时，默认零开销
        if (LogVerbose) {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            TickGuarded();
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            ProfTick((t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        } else {
            TickGuarded();
        }
        // 帧末：帧间隔超阈值就记一行（带 GC 计数增量 / 托管堆 / 视频状态，见 PortalUI.Hitch.cs）
        double hitchMs = (System.Diagnostics.Stopwatch.GetTimestamp() - hitchT0) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
        // Tick 超过 60ms 就把慢在哪一段写出来（只有总数的话定不了凶手）。
        // 必须先报我们自己、再报帧级卡顿：HitchFrame 里「卡顿后接着逐帧记 120 帧」会调
        // StartPanelProfiling()，而它会清空相位表 —— 排在它后面读相位表永远是空的。
        HitchOurSlowTick(hitchMs);
        HitchFrame(hitchMs);
    }

    /// <summary>Tick 主体 + 异常兜底：任何未捕获异常都不该让整个界面静默失效</summary>
    private static void TickGuarded() {
        try {
            TickCore();
        } catch (System.Exception ex) {
            ReportError("游戏内主循环出现异常（界面可能已失效，建议重开一次）", ex);
        }
    }

    /// <summary>帧耗时统计：累计约 1000ms 的 Tick 开销后检查一次，只在偏慢时记录</summary>
    private static double _profSum, _profMax;
    private static int _profCount;
    private static void ProfTick(double ms) {
        _profSum += ms; _profCount++;
        if (ms > _profMax) _profMax = ms;
        if (_profSum < 1000.0) return;
        double avg = _profCount > 0 ? _profSum / _profCount : 0;
        // 阈值：面板打开时 1ms、关闭时 0.3ms；只有超标才打日志（平时完全静默）
        double limit = _panelOpen ? 1.0 : 0.3;
        if (avg > limit || _profMax > limit * 5) {
            Lg.Info($"[LevelLib] 【性能】Tick 平均={avg:F3}ms 峰值={_profMax:F3}ms 采样={_profCount} 面板={_panelOpen} tab={_topTab} 池行={_poolRows.Count} 逻辑行={_logicalRows.Count}");
        }
        _profSum = 0; _profMax = 0; _profCount = 0;
    }

    /// <summary>常驻段某一节抛异常时的统一收尾。常驻段是一条直线（见 TickCore）：任何一处把异常抛出去，
    /// 它后面的二十多个模块这一帧全不跑，面板开着时表现成「整个界面卡死、点不动」，所以各处自己 try/catch 交到这里。
    /// 日志必须节流（只报第 1/2/10 次，之后每 60 秒一次），计时用 Environment.TickCount 而不碰 Unity 的 Time。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, int> _tickFailCount
        = new System.Collections.Generic.Dictionary<string, int>();
    private static readonly System.Collections.Generic.Dictionary<string, int> _tickFailAt
        = new System.Collections.Generic.Dictionary<string, int>();

    private static void TickFail(string who, System.Exception e) {
        int n = 0;
        try {
            _tickFailCount.TryGetValue(who, out n); n++; _tickFailCount[who] = n;
            int last; _tickFailAt.TryGetValue(who, out last);
            int now = System.Environment.TickCount;
            bool shout = n <= 2 || n == 10 || unchecked(now - last) >= 60000;
            if (!shout) return;
            _tickFailAt[who] = now;
        } catch { return; }
        try {
            Lg.Info("常驻段「" + who + "」抛出异常（累计第 " + n
                + " 次" + (n <= 10 ? "" : "，仍在持续，之后每 60 秒报一次") + "）：" + e);
        } catch { }
    }

    private static void TickCore() {
        // 首帧性能：采样窗口内给各阶段计时，同时一直盯着「我们自己的 Tick 慢没慢」。
        TickPhaseBegin();
        // prof 恒为 true（二十来处 if (prof) TickPhaseMark(...) 保留原样）：标记本身几十纳秒，换来的是慢帧一定知道慢在哪一段。
        bool prof = true;

        // ---------- 一、始终需要运行的部分（不依赖面板是否打开） ----------
        // 一步一步的 TickPhaseMark 是定位「卡卡的」用的：整段只有一个名字定不了凶手，拆成十几段就能一次点名。
        // 标记本身极便宜（一次 Stopwatch 读约几十纳秒）。
        CrossModGuard.DrainOnMainThread();   // 跨 mod 防御：替别的 mod（Together）在非主线程发的 Unity 查询跑一趟
        if (prof) TickPhaseMark("·跨mod防御");
        TickKeyCapture();    // 快捷键捕获（游戏内 Tick 捕获，OnGUI 不跑时兜底）
        TickLevelKeys();     // 快捷键监听：打开选中谱面 / 随机谱面 / 上下切换
        if (prof) TickPhaseMark("·热键");
        TickBatchImport();   // 批量导入进度驱动（弹出的是独立窗口）
        if (prof) TickPhaseMark("·批量导入");
        TickPlayerBox();     // 界面外玩家信息框（面板外的常驻显示）
        TickPlayerBoxDrag(); // 玩家信息栏长按拖动（必须常驻：界面外那份在面板关着时也显示着）
        if (prof) TickPhaseMark("·玩家框");
        TickToast();         // 轻提示（独立窗口）
        TickAchievements();  // 成就图标渐变 + 解锁提示（独立窗口）
        if (prof) TickPhaseMark("·提示/成就");
        // 开关圆钮的滑动动画。必须常驻段：弹窗（批量导入进度 / 删除确认）都是独立画布，面板关着时也开着，
        // 挂 TickPanel 的话关掉面板再点开关、圆钮会停在半路。
        // 没活时只花一次 Count == 0 判断。
        TickSwitchMoves();
        if (prof) TickPhaseMark("·开关动画");
        // 通关记录条尾的删除键。必须常驻段：详情区与关卡选择弹窗都在面板里，但收起面板那一帧
        // 动画还要收尾；一个按钮都没建过时第一行就返回。
        TickRecordDeleteButtons();
        if (prof) TickPhaseMark("·记录删除键");
        TickNowPlaying();    // 右上角"正在播放"（面板内的条，但无界面时自身早退）
        if (prof) TickPhaseMark("·正在播放");
        TickPickNote();      // 备注防抖保存（可能刚关面板，仍需落盘）
        if (prof) TickPhaseMark("·备注");
        TickVerify();        // 重复谱面检验（后台线程驱动，可能面板已关）
        if (prof) TickPhaseMark("·谱面校验");
        TickResetAllConfirm();   // 删除全部数据确认框：3 秒倒计时（挂独立画布，面板关了也要继续走）
        if (prof) TickPhaseMark("·删除确认");
        Updater.Tick();          // 更新检查：开局延迟自动查一次
        Updater.TickRequest();   // 更新检查：推进当前网络请求（完成/失败都在这里收尾）
        // 开始界面那条更新闸门：读上一步刚推进完的状态，决定那一屏显示什么、什么时候放人。
        // 必须排在 TickRequest 之后（同一帧的请求结果这一步才看得见）；没武装时第一行就返回。
        Updater.GateTick();
        if (prof) TickPhaseMark("·更新检查");
        TufClient.Tick();        // TUF 社区定级：推进查询队列（严格串行 + 每次间隔 ≥600ms，空闲时只有几个空判断）
        // 这两个必须留在常驻段，不能塞进 TickPanel：TickScreenSaver —— 全局屏保恰恰是「面板没开」时才用得上；
        // TickSplash —— 屏保的渐入 / 提示闪烁 / 长按进度都要在面板关着时继续跑。
        // 两个函数开头都有 null 早退，没有启动页时每帧只花一次判断。
        TickScreenSaver();
        TickSplash();
        TickClickProbeClosed();   // 临时诊断：面板关着时把鼠标处的射线命中记进日志（排查「关闭后点不动」，查完删）
        // 画布缩放不再由常驻段盖章：MakeCanvas 已切 CanvasScaler.ScaleWithScreenSize(Expand)，
        // Unity 每帧自己盯屏幕尺寸并重算缩放系数（原先的 TickUiScale 在新模式下写的 scaleFactor 会被覆盖，已删）。
        if (prof) TickPhaseMark("·屏保/启动页");
        // 这三条必须在常驻段：TickEnterTransition 一开始就 ConfirmOpen（到 ClosePanel），之后面板是关的、
        // 甚至会换场景，放进 TickPanel 就等于动画永远停在第一帧；TickStartScreen / TickProfileScreen 同理（屏保那条路面板本来就是关的）。
        // 三条开头都有 null 早退，没在播时每帧只花一次判断。
        // 下面这几处各自单独罩了一层 try/catch：常驻段是一条直线，任何一处把异常抛出去，它后面的二十多个模块
        // 这一帧全不跑（面板开着时就是「整个界面卡住、按什么都没反应」）。
        // 只罩状态最多、跨场景活得最久的这几处（过渡层 / 开始界面 / 个人页 / 宠物 / 视频 / 两级模糊），日志在 TickFail 里节流。
        try { TickEnterTransition(); } catch (System.Exception e) { TickFail("进入过渡层", e); }
        if (prof) TickPhaseMark("·过渡层");
        try { TickStartScreen(); } catch (System.Exception e) { TickFail("开始界面", e); }
        if (prof) TickPhaseMark("·开始界面");
        try { TickProfileScreen(); } catch (System.Exception e) { TickFail("个人页", e); }
        if (prof) TickPhaseMark("·个人页");
        // 大肥鱼同样必须在常驻段：开着「全局显示」时面板是关着的（塞进 TickPanel 就变成「只有开着面板才看得见」）。
        // 它自己开头有开关判断，关掉宠物时每帧只花一次 bool 判断。
        try { TickPet(); } catch (System.Exception e) { TickFail("大肥鱼", e); }
        if (prof) TickPhaseMark("·大肥鱼");
        // 游玩时长也必须在常驻段：打谱的时候面板一般是关着的（它自己判 scnGame + 非暂停，不在打谱时每帧只花一次场景名比较）。
        PlayTime.Tick();
        if (prof) TickPhaseMark("·游玩时长");
        // Steam 的账本也要在常驻段复查：界面外那份信息栏面板关着也显示，挂 TickPanel 就只在开着面板时才更新。
        // 它自己 60 秒节流，平时就是一次整数比较 + 一次提前 return（见 SteamPlaytime.cs）。
        SteamPlaytime.Tick();
        if (prof) TickPhaseMark("·Steam时长");
        PerfDiag.Tick();     // 性能归因诊断（默认关闭：mod 目录放 debug_perf.txt 才开，每 60 秒一行）
        // Iridium 联动探针（默认关闭：mod 目录放 debug_iridium.txt 才开）。必须在常驻段：
        // 进谱面时面板是关着的，挂 TickPanel 就永远采不到。
        IridiumProbe.Tick();
        if (prof) TickPhaseMark("·Iridium探针");
        // Iridium 联动的取值层（装饰物分帧加载的进度）。同样必须在常驻段：进入过渡层的就绪判定要读它，
        // 而那一刻面板早就关了（过渡一开始就 ConfirmOpen 到 ClosePanel）。
        IridiumLink.Tick();
        if (prof) TickPhaseMark("·Iridium联动");
        // 视频背景也必须在常驻段：开始界面 / 屏保 / 进谱面过渡层都可能在面板关着时显示，
        // 挂 TickPanel 的话视频会在那几屏停住。它自己有「没挂层就 Pause」的判断。
        try { TickVideoBg(); } catch (System.Exception e) { TickFail("视频背景", e); }
        if (prof) TickPhaseMark("·视频背景");
        // 代码绘制的背景：必须排在 TickVideoBg 之后 —— 视频要不要让它淡出是上一步刚定的。
        // 也必须在常驻段（开始界面 / 登录界面 / 进谱面过渡层都可能在面板关着时显示）；
        // 一组都没挂时第一行就 return，零开销。
        try { TickNetBg(); } catch (System.Exception e) { TickFail("代码背景", e); }
        // 静态背景：排在 TickNetBg 之后 —— 图"掉没了"（恢复默认、而默认图也不在）时它负责把神经图放回来；
        // 也必须在常驻段（开始界面 / 登录界面 / 进谱面过渡层都可能在面板关着时显示）。
        // 一层都没挂时第一行就 return，零开销。
        try { TickStaticBg(); } catch (System.Exception e) { TickFail("静态背景", e); }
        try { TickUiStates(); } catch (System.Exception e) { TickFail("交互态", e); }     // 按钮三态：鼠标下那一个亮起来、其余退回常态（见 PortalUI.UiStates）
        // 流式布局（让位）：这一帧登记过目标的矩形从旧位滑到新位（见 PortalUI.Flow）。
        // 必须排在 TickPetPick / TickPanelGlass 之前 —— 磨砂按当帧的矩形算，位置晚一帧就会露旧形状。
        try { TickFlow(); } catch (System.Exception e) { TickFail("流式布局", e); }
        // 通关记录里被删掉的那几行正在淡出退场（见 PortalUI.RecordList）。同样挂常驻段：
        // 关卡选择弹窗是独立画布，面板关着时它照样可能在退场。
        try { TickRecRowFade(Time.unscaledDeltaTime); } catch (System.Exception e) { TickFail("记录行退场", e); }
        // 大肥鱼模型下拉的铺开 / 收起那一小段编排（见 PortalUI.PetPick）。排在这里是因为它要改面板矩形，
        // 得赶在下面 TickPanelGlass 之前改完 —— 磨砂按当帧的矩形算，晚了就会露一帧旧形状。
        try { TickPetPick(); } catch (System.Exception e) { TickFail("模型下拉", e); }
        if (prof) TickPhaseMark("·代码背景");
        // 背景渐进模糊（开始界面 到 登录界面）。必须排在 TickVideoBg 之后：视频那一层的纹理 / 就绪状态
        // 是它这一帧刚摆好的，排在前面就会拿上一帧的纹理去烘（刚起播那几帧可能还是空的）。
        // 也必须在常驻段（开始界面与个人页都可能在面板关着时显示）；模糊度为 0 时第一行就 return。
        try { TickBgBlur(); } catch (System.Exception e) { TickFail("背景模糊", e); }
        if (prof) TickPhaseMark("·背景模糊");
        // 面板磨砂（各页遮罩的模糊滑条）：同样在常驻段、同样排在 TickBgBlur 之后 —— 它裁的那张糊图
        // 就是上一步刚烘好的；面板没开 / 滑条为 0 时第一行就 return。
        try { TickPanelGlass(); } catch (System.Exception e) { TickFail("面板磨砂", e); }
        if (prof) TickPhaseMark("·面板磨砂");
        TickStartScreenProbe();   // 开始界面「滑不动」的滑动探针（最多 16 条，平时零开销）
        TickStartScreenInput();   // 同一个问题的兜底输入：EventSystem 那条路断了也能滑
        if (prof) TickPhaseMark("·开始界面输入");
        // 换版自检：进关卡之后才有的东西（conductor / controller / 谱面数）在这里再确认一遍，
        // 只做一次（启动 20 秒后），见 Verifier.cs。
        Verifier.Tick();
        if (prof) TickPhaseMark("·换版自检");
        // 排队预热：只在游戏跑得动的帧上做一件（字形图集 / 成就 / 开发者 / 设置三页）。
        // 必须常驻段（面板刚开那几秒就派上用场）；没活时只花一次 bool 判断。
        TickPreload();
        if (prof) TickPhaseMark("·预热排队");
        // 默认头像动效（照搬 fluid-orb，见 PortalUI.FluidOrb.cs）：必须常驻段 —— 界面外那份信息栏
        // 面板关着也显示，个人页与大厅玩家卡也各自独立。一处都没挂时第一行就返回；
        // 挂着时每帧只重算那一张贴图并上传，不碰任何图元的 sprite 与 alpha、也不改子物体数量。
        try { TickFluidOrb(); } catch (System.Exception e) { TickFail("默认头像动效", e); }
        if (prof) TickPhaseMark("·默认头像动效");
        // 开发者页那层铺满整页的流体背景（同一个 fluid-orb 数学的"不做圆边"版，见 PortalUI.DevFluidBg.cs）：
        // 也走常驻段 —— 那一页的显示者可能在面板动效之后才建；没挂或不在这一页时第一行就返回。
        try { TickDevFluidBg(); } catch (System.Exception e) { TickFail("开发者页流体背景", e); }
        if (prof) TickPhaseMark("常驻Tick");

        // 看门狗：面板开着时游戏自己进了关卡（换了场景 / 画布被销毁）就收摊。必须在常驻段、
        // 而且排在 _closing 那段淡出之前 —— 那一刻游戏输入还关着，早一帧收摊玩家就早一帧能按得动。
        TickPanelTakeoverWatch();

        // 退出动画要在面板已关之前继续跑：RequestClosePanel() 只立标记 + 计时归零，真正的销毁在这里走完动画后调 ClosePanel()。
        // 只做透明度、不移动画布 —— MakeCanvas 建的是 ScreenSpaceOverlay 根画布，Unity 每帧强制它铺满屏幕，
        // 移动它的 RectTransform 会被引擎覆盖回去。
        if (_closing && _panelCanvas != null) {
            // 单帧步进钳住（跟进场同一套：抖动帧别把 0.3 秒的淡出一步冲完，见 UiFadeMaxStep）
            _closeT += Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);
            float tc = Mathf.Clamp01(_closeT / CloseDuration);
            float ease = tc * tc * (3f - 2f * tc);   // smoothstep（跟进场同一套手感）
            if (_panelGroup != null) _panelGroup.alpha = 1f - ease;   // 整块 1 → 0
            // 大厅那块独立画布跟着同一条曲线落。不跟的话：面板淡没了，大厅还全亮着，
            // 直到 ClosePanel() 把画布 enabled = false 才整块消失 —— 就是用户说的"直接消失，很突兀"。
            // 喂进去的是钳过的进度 tc，不是 ease，免得两条曲线再叠一层。
            TickArkCloseFade(tc);
            if (tc >= 1f) { _closing = false; ClosePanel(); }
        }

        TickAlwaysHotkeys();
        if (prof) TickPhaseMark("热键/退出动画");

        // 隐藏态（右圆钮）不能跨面板关闭存活：面板一关就把内容收起来这件事作废，
        // 否则下次开面板页面内容还是藏着，没人再点一下就永远空屏。
        if (!_panelOpen && _arkUiHidden) ArkSetUiHidden(false);

        // ---------- 二、只有面板打开时才需要跑的部分 ----------
        // 开始界面开着那一段帧间隔中位数 20.6ms（≈48fps）而我们 Tick 只有 1~3ms，时间花在渲染上，
        // 而面板（80000）被开始界面（93100）整块盖住，那张 1920×1080 背景图 + 遮罩 + 整页 UI 全在白画。
        // 所以被完全盖住时把面板画布停掉渲染（个人页一开始淡出就恢复）。
        SyncPanelHiddenUnderCover();
        if (!_panelOpen || _panelCanvas == null) { NetFlashProbeTick(); EndPanelProfilingFrame(); return; }
        TickPanel();
        if (prof) TickPhaseMark("TickPanel");
        NetFlashProbeTick();   // 「闪线」逐帧探针（默认关，见 PortalUI.NetBg.cs）：面板这一帧最终长什么样
        EndPanelProfilingFrame();
    }

    /// <summary>面板被一整层盖住时停止渲染面板（省掉半屏白画的填充率）。盖层 = 开始界面（93100）/ 过渡层（82000）/ 个人页（93000）。
    /// 判据只看"盖层在不在"，不看它渐入到什么程度：开始界面自己有 0.5 秒整层渐入，那半秒里让面板画出来的话，
    /// 玩家会隔着半透明的封面看见面板的背景与大厅，那就是他报的"闪一下"。
    /// 开面板那一下必须在同一帧调一次本函数（见 OpenPanel 末尾与画布创建处），漏掉就会先画一帧再停，同样是闪。
    /// 个人页只在还没开始淡出时算盖层 —— phase ≥ 2 时主界面正在跟它交叉淡入，那一段必须让面板画出来，
    /// 否则「亮起来的时候是空的」。只关渲染：不动对象、不动布局、不动交互判据，盖层一走立刻恢复。</summary>
    private static bool _panelRenderStopped;
    private static void SyncPanelHiddenUnderCover() {
        try {
            if (_panelCanvas == null) {
                _panelRenderStopped = false;
                // 面板都销毁了，大厅那块独立画布必须跟着落。它是场景根对象，不挂在面板下，
                // 没人叫停就会一直在屏幕上画 —— 这正是"关掉 mod 界面之后主界面还留着"。
                SyncArkCanvasEnabled(false);
                return;
            }
            // 「收起来留着的」开始界面（_ssParked，上滑交接后不销毁、留给「往下滑回开始界面」淡回来用）不算盖层：
            // 只看 _ssGo != null 的话那一层永远「在」，面板渲染就被永久停掉 —— 登录之后个人页一淡出，
            // 后面什么都没有、直接露出游戏。它封面全透明、也不吃射线。
            bool covered = (_ssGo != null && !_ssParked) || _etGo != null || (_pfGo != null && _pfPhase <= 0);
            // 恢复渲染这一下 = 整块画布重新建批次（面板里有 24 行池 + 主页 40 张卡 + 顶栏），这记重活不能落在
            // 「登录之后」那一刻，所以判据取 _pfPhase <= 0（个人页渐入结束、等点登录那会儿，重新建批次没人看得出来）。
            if (covered != _panelRenderStopped) {
                _panelRenderStopped = covered;
                _panelCanvas.enabled = !covered;
                Lg.Info(covered
                    ? "[LevelLib] 面板被整层盖住 ->  停止渲染面板（半屏白画不要了）"
                    : "[LevelLib] 盖层走了 ->  面板恢复渲染");
            }
            // 大厅这块每次都要核对，不只是状态变的时候：它是独立画布，可能在"面板被盖着"的那段时间里
            // 才刚建出来（默认 enabled=true），那就等于在被盖着的时候就开始画了。赋值同值时 Unity 内部短路，不花钱。
            // 判据比面板本体多一条"必须停在大厅这一页"：大厅画布也是 Overlay 且排序(81000)比面板(80000)高，
            // 少了这一条，切去设置页/成就页时大厅会盖在它们最上面。
            // （右圆钮的隐藏态不在这里处理：大厅靠画布根上的 CanvasGroup.alpha 淡出淡入，
            //   这里要是把 enabled 关了，淡出动画就被切成瞬隐了。）
            SyncArkCanvasEnabled(!covered && _topTab == MainScreenSlot);
        } catch { }
    }

    /// <summary>面板画布的整体透明度（进界面 0到1、退出 1到0），淡入用 smoothstep，时长 PanelFadeDuration。
    /// 必须用 Time.unscaledDeltaTime：面板打开期间 Time.timeScale = 0，用缩放时间就永远停在 0。</summary>
    private static CanvasGroup _panelGroup;
    private static float _panelFadeT = 1f;
    private const float PanelFadeDuration = 0.22f;
    private static bool _panelFadeLogged;
    /// <summary>开面板那一刻的场景名 —— 看门狗用它判断「面板还开着、但游戏自己换场景进关卡了」。</summary>
    private static string _panelSceneAtOpen;
    /// <summary>开面板那一刻是不是已经在打谱 —— 看门狗靠它区分「进来之后才开始玩」（= 游戏自己进了关卡）
    /// 和「本来就在玩、玩家自己按 UMM 开的面板」（后者不许动）。</summary>
    private static bool _panelPlayingAtOpen;

    /// <summary>当前场景名（读不到返回 null；读不到就不做场景判据，宁可少判一次也别误判）。</summary>
    private static string CurrentSceneName() {
        try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { return null; }
    }

    /// <summary>看门狗：面板开着的时候游戏自己进了关卡 到 立刻收摊 + 放开游戏输入。
    /// 起因：快捷键设成 Tab / 字母时，那一下同时弹了面板、也被游戏读走（左半键盘本来就是 Action1「击打」），
    /// 而我们的关输入在自己的 Tick 里、跟游戏 Update 没有固定先后；换场景又销毁了面板画布而 _panelOpen 还挂着 true，
    /// 自愈判据不触发，游戏输入一直关着。判据三条任一条成立就收摊：场景名变了 / 画布没了 / 开始打谱了。</summary>
    private static void TickPanelTakeoverWatch() {
        if (!_panelOpen) return;
        try {
            bool canvasGone = (_panelCanvas == null);
            string scn = CurrentSceneName();
            bool sceneMoved = !string.IsNullOrEmpty(_panelSceneAtOpen)
                           && !string.IsNullOrEmpty(scn)
                           && scn != _panelSceneAtOpen;
            // ③「进面板的时候还没在打谱，现在开始打了」补场景判据的漏：面板正好在换场景那一帧打开时
            // OpenPanel 记到的就已经是新场景名，判据①②都抓不到。它不会误伤 —— 打谱中按 UMM 按钮开面板
            // 是允许的正常用法，那一刻 _panelPlayingAtOpen 已经是 true，这条永远不成立。
            bool startedPlaying = !_panelPlayingAtOpen && IsLevelPlaying();
            if (!canvasGone && !sceneMoved && !startedPlaying) return;
            Lg.Info("看门狗：面板还开着，但"
                + (canvasGone ? "面板画布已经没了" : (sceneMoved ? "场景已经换了" : "游戏开始打谱了"))
                + $"（开面板时={_panelSceneAtOpen ?? "?"} 现在={scn ?? "?"}"
                + $" 开面板时在打谱={_panelPlayingAtOpen}）"
                + " → 判定「游戏自己进了关卡」 强制收摊 + 放开游戏输入"
                + "（快捷键跟游戏的操作键撞了就会这样 见 PortalUI.PanelBuild.cs 的说明）");
            _panelCanvas = null;     // 多半已经被场景切换销毁了，置空免得 ClosePanel 再去 Destroy 一次
            ClosePanel();            // 真正的收摊：恢复 timeScale / 安排游戏输入恢复 / 清引用
        } catch (System.Exception e) {
            ReportErrorQuiet("面板看门狗异常", e);
        }
    }

    /// <summary>界面渐入的单帧步进上限。渐入都用 Time.unscaledDeltaTime 推进（面板打开时 timeScale = 0），
    /// 而开面板那一帧极卡：下一帧的 unscaledDeltaTime 就是那个巨大间隔，不钳的话一步就顶到 1 = 等于没有渐入。
    /// 只在卡顿帧起作用（正常 60fps 一次都不触发）；钳的是面板 / 开始界面 / 个人页 / 主界面 / 启动页与屏保。</summary>
    public const float UiFadeMaxStep = 0.05f;

    private static void TickPanelFade() {
        if (_panelGroup == null || _panelFadeT >= 1f) return;
        // 正在淡出时别碰 alpha：这一段是「进场 0到1」，而退出动画在 TickCore 里把同一块 alpha 往 0 推，
        // 两边同时写就会打架（正常情况下进场早就走完了，但「刚打开就立刻按 Esc」会撞上）。
        if (_closing) return;
        // 「宁可不显示也不要闪」：动态背景还在路上时先别让整块面板亮出来 —— 这一帧什么都不画。
        // 判据只有一条（见 VideoHandoverPending）：视频能用 + 有屏在看 + 还没就绪 + 还没等超 PanelBgWaitSeconds。
        // 就绪那一帧当场放行 —— 玩家看到的是"按了快捷键、一两帧后面板连着它的动态背景一起淡进来"，
        // 而不是"先亮一块代码背景、下一帧被视频一帧切走"（他连报了四轮的那一下）。
        // 排在这里（写 alpha 之前）是必须的：排到后面这一帧的面板已经画出去了，等于没挡。
        if (VideoHandoverPending()) return;
        // 记一次这一步的真实帧间隔：以后再报「还是没渐入」，这一行直接告出开面板那一帧卡了多少毫秒。
        if (!_panelFadeLogged) {
            _panelFadeLogged = true;
            Lg.Info($"[LevelLib] 面板渐入：起始帧间隔 {Time.unscaledDeltaTime * 1000f:F0}ms"
                + $"（单帧步进钳到 {UiFadeMaxStep * 1000f:F0}ms 总时长 {PanelFadeDuration:F2}s）");
        }
        float pdt = Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);
        _panelFadeT = Mathf.Min(1f, _panelFadeT + pdt / PanelFadeDuration);
        float e = _panelFadeT * _panelFadeT * (3f - 2f * _panelFadeT);   // smoothstep
        _panelGroup.alpha = e;
    }

    /// <summary>每日成就「今天好！！」：每天第一次打开谱面库主界面时触发。
    /// 开面板那一两秒是卡的，提示窗这时弹会被吞掉，所以先算「卡顿结束了没」（距开面板够久 + 连续若干帧帧间隔平稳）。</summary>
    private static void TickAchToday() {
        if (!_achTodayPending) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double sinceOpenMs = (now - _panelOpenTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double frameMs = (now - _lastPanelFrameTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _lastPanelFrameTicks = now;

        if (frameMs > 0 && frameMs <= AchSmoothMs) _smoothFrames++;
        else _smoothFrames = 0;      // 这一帧又卡了，重新数

        if (sinceOpenMs < AchSettleMinMs || _smoothFrames < AchSettleFrames) return;
        _achTodayPending = false;    // 只试一次；真正"今天是否已触发"由 TriggerDaily 内部把关
        bool ok = Achievements.TriggerDaily("ach_daily_today", "今天好！！");
        Lg.Info($"[LevelLib] 每日成就：今天好！！ 触发={ok}"
            + $"（开面板后 {sinceOpenMs:F0}ms、连续平稳 {_smoothFrames} 帧，卡顿已结束）");
    }

    /// <summary>面板相关的逐帧逻辑（仅在面板打开时执行）</summary>
    private static void TickPanel() {
        bool prof = true;    // 同 TickCore：阶段标记一直记（面板段里的「详情区构建」/「列表首帧布局」也都能被抓到）
        TickPanelFade();     // 进界面的淡入（alpha 0→1）
        TickArkFade();   // 主界面整屏渐入（登录之后那一屏慢慢亮起来，见 PortalUI.ArkView.cs）
        TickRandomOpen();    // 随机谱面：等滑动 + 详情就绪再进
        // 进入过渡 / 开始界面 / 个人页这三条已经挪到 TickCore 的常驻段：过渡一开始就 ConfirmOpen 到 ClosePanel，
        // 面板一关 TickCore 就从「面板没开 到 return」走掉了，放在这里它们再也没人推进。
        // 面板开着 到 游戏输入必须是关的：这一帧发现它被重新打开就当场再关一次。
        // 游戏里好几处会每帧把 isActive 顶回 true，AsyncInputManagerGate 拦它，但只要有一处漏挂或时序差了
        // 就会出现「面板开着但游戏还收得到按键」；状态漂了的问题靠一次性设置治不住，每帧核对只花一次 bool 比较。
        EnsureGameInputOffWhilePanelOpen();   // 面板开着时游戏输入必须是关的（兜底在常驻段，这里再来一次）
        TickAchToday();          // 每日成就「今天好！！」：等开面板的卡顿过去再提示
        TickFrameBaseline();     // 平时帧耗时基线（只给「动画」日志做对比用）
        TickClickProbe();        // 点击探针：这一击到底打到了谁（定位「点不动」）
        TickAchLeftClick();      // 左栏读鼠标（不依赖射线，官方成就那条）
        if (prof) TickPhaseMark("面板输入组");
        TickGlobalWheel();       // 全局滚轮：鼠标在列表范围内就滚（绕开事件系统，谱面行会吃掉滚轮事件）
        TickGlobalDrag();        // 全局拖拽：左键在列表内按下即可拖动（同上，绕开事件系统）
        TickScrollAnimation();   // 字母定位：快速滚到目标字母
        TickListSnap();          // 滚动停下后把最近的一行吸附到视口正中（见 LevelList.Snap.cs）
        if (prof) TickPhaseMark("面板滚动组");
        // 钉顶：开面板头十几帧把列表按在顶部（玩家一动就让位）
        if (_libPinTop && _libScroll != null) {
            // 释放条件不能太松：早期一帧杂散的滚轮 / 拖拽事件就会把钉顶放掉，内容一长高 ScrollRect 的
            // 默认值(0=底)又把它带下去，表现就是「差一点到顶」。定稿：只有真的拖拽过才立刻释放，
            // 滚轮要「钉满 0.8 秒之后」才认，本局要沿用位置（_libScrollRestoreFrames>0）时照旧让位。
            _libPinT += Time.unscaledDeltaTime;
            bool dragged = false, wheeled = false, letterJump = false;
            // 上层盖着（开始界面 / 个人页 / 启动页 / 过渡层）时只钉不判：否则玩家在封面上「上滑进入」的那一下
            // 有真实位移，照样会把钉顶放掉，之后内容还在分帧长高、ScrollRect 就把列表带下去。
            bool blocked = false;
            try { blocked = PanelInputBlocked(); } catch { }
            if (!blocked) {
                try {
                    wheeled = UiInput.MouseScrollDelta.y != 0f;
                    // 判据不看 _dragActive —— 它只表示「左键在列表矩形里按下了」，「点一下选中谱面」会把它置真，
                    // 隔着开始界面按一下也会，于是钉顶被「假拖拽」当场放掉。改为：按住、且纵向真的移动了
                    // LibPinDragPx 像素才算「玩家在拖」（点一下不算）。
                    bool held = UiInput.MouseButton(0);
                    float my = UiInput.MousePosition.y;
                    if (held) {
                        if (!_libPinHeld) { _libPinHeld = true; _libPinMouseY = my; }
                        else if (Mathf.Abs(my - _libPinMouseY) > LibPinDragPx) dragged = true;
                    } else { _libPinHeld = false; }
                } catch { }
                // 字母定位动画也算「玩家在动」，要让位 —— 不让位钉顶会每帧把它拽回顶上，变成两边打架。
                try { letterJump = _scrollAnim; } catch { }
            }
            if (dragged || letterJump || (wheeled && _libPinT > 0.8f) || _libScrollRestoreFrames > 0) {
                _libPinTop = false;
                _libPinHeld = false;
                Lg.Info($"[LevelLib] 列表钉顶结束（真实拖拽={dragged} 字母定位={letterJump}"
                    + $" 滚轮={wheeled} 钉了 {_libPinT:F1}s）");
            } else {
                // 每帧把列表按在顶部：内容分帧长高时也不会被 ScrollRect 的默认值(0=底)带下去
                try {
                    _libScroll.verticalNormalizedPosition = 1f;
                    if (_libScroll.content != null)
                        _libScroll.content.anchoredPosition = new Vector2(_libScroll.content.anchoredPosition.x, 0f);
                } catch { }
            }
        }
        if (prof) TickPhaseMark("钉顶");
        // 开面板时恢复上次的列表滚动位置：内容高度是分帧算出来的，所以前几帧反复落实
        if (_libScrollRestoreFrames > 0 && _libScroll != null) {
            _libScrollRestoreFrames--;
            try { _libScroll.verticalNormalizedPosition = _libScrollRestore; } catch { }
            if (_libScrollRestoreFrames == 0) _libScrollRestore = -1f;
        }
        if (prof) TickPhaseMark("恢复滚动位置");
        TickLetterFrame();       // 字母条白框：跟随当前所在字母区
        TickRowContextMenu();   // 右键菜单：点菜单外任意位置关闭
        TickPanelPlayerBox();   // 面板内信息栏：每秒刷新 + 每帧同步显隐（见 SyncPanelPlayerBox）
        // 开发者页的键盘彩蛋（敲 bugbug 到 「Coding Coding bug」）：不在这一页时它自己清缓冲
        TickDevEaster();
        if (_topTab == 2) TickAchievementsPage();   // 成就页：进度条/圆标 + 可视卡片重排（2 = 成就 tab）
        if (_topTab == 0) TickHome();               // 主页：棋盘展开动画 + 拖动惯性 + 滚轮缩放（0 = 主页 tab）
        if (prof) TickPhaseMark("常规Tick");
        // 详情区延迟构建（开面板那一帧只建背景+列表，详情区下一帧补上，避免整帧 370ms 卡顿）
        if (_detailBuildPending) {
            _detailBuildPending = false;
            BuildDetailArea();
            ApplyPanelBackdropAlpha();   // 详情区底色（固定那一档，见 PanelAlphaFloor）
            // 详情区登记磨砂：必须排在 ApplyPanelBackdropAlpha 之后。
            // 登记那一刻会读遮罩当前的 alpha 当基准（BaseAlpha），而详情区建出来时 alpha 是 0
            // （那档固定系数由 ApplyPanelBackdropAlpha 刷上去）—— 提前登记会拿到 0 而退回 1，
            // 于是那一块只剩四成糊，糊图跟遮罩的深浅也对不上（见 RefreshGlassBaseline）。
            RegisterGlassPanel(_detailAreaImg);
            OpenStep("BuildDetailArea(延迟)");
            if (prof) TickPhaseMark("★详情区构建");
        }
        // 详情区建好了才能落实「上次选中的谱面」（开面板那一帧刷详情 = 对着一堆 null 引用刷，见 RestoreUiState）。
        if (_uiRestoreSelectedPath != null) ApplyPendingRestoreSelection();
        // 背景音乐延迟启动（读+解码 mp3 要 750ms 左右，不该算在开面板那一帧里）
        if (_musicStartPending) {
            _musicStartPending = false;
            EnsureMusic();
            PauseGameMusic();                                     // 先停游戏自己的 BGM
            if (_musicPlayer != null) _musicPlayer.PlayMusic();    // 再播咱的背景音乐（循环）
            _bgmWatchdogAt = 0f;                                   // 打开后马上做一次健康检查
            OpenStep("音乐启动(延迟)");
            if (prof) TickPhaseMark("★音乐启动");
        }
        TickBgmWatchdog();   // 音乐健康检查（面板开着却没在响就补播）
        TickTopTabs();   // 顶层：整页左移动画 + 导航排配色
        TickArk();      // 主界面：斜切投影 + 鼠标跟随 + 悬停态 + 读数（每帧/每秒各一趟）
        TickVeilFade();  // 背景遮罩透明度渐变
        TickPageVeil();  // 设置页/开发者页遮罩渐入
        TickDevGravity();   // 开发者页的重力字母（点一下掉一颗）：不在这一页时它自己收手，一帧都不算
        TickHotkeyButtons();   // 快捷键按钮文字刷新
        TickNavFrame();  // 设置页导航白框滑动
        TickSubFrame();  // 设置页子 tab 选中块滑动（跟主 tab 同款）
        TickSettingsPage();   // 设置页（淡入）
        TickDetailTabFrame();   // 详情区 tab 白框移动
        TickLibExpand();        // 库页：未选中→列表拉满 / 选中→详情区展开（0.22 秒动画）
        TickSortMode();         // 排序方式切换的 0.22 秒过渡（列表右边界 + 滚动条 + 字母条淡出）
        TickHeatGrow();         // 寄录图节点上升动画
        // 搜索防抖：输入停止 0.15s 后才重建列表（避免每敲一个字符全量重建 3600+ 组件）
        if (_searchDirtyTimer > 0f) {
            _searchDirtyTimer -= Time.unscaledDeltaTime;
            if (_searchDirtyTimer <= 0f) RefreshList();
        }
        // 滑条首帧布局完成后的位置重算（HLayout 布局完成前轨道宽度算错，滑块会飞出去）
        if (_rankRangeDirty) {
            _rankRangeDirty = false;
            UpdateRankRange();
        }
        // 列表虚拟化：布局完成后渲染可见行（首帧 viewport 高度为 0，必须等布局）
        if (_listDirty) {
            var scroll2 = _libScroll;
            if (scroll2 != null && scroll2.viewport != null && scroll2.viewport.rect.height > 1f) {
                _listDirty = false;
                UpdateVisible();
                if (prof) TickPhaseMark("★列表首帧布局");
            }
            // viewport 还没布局（高度 0）到 保持 dirty，下一帧再试
        }
        TickRainbowTexts();
        if (prof) TickPhaseMark("收尾");
    }

}
