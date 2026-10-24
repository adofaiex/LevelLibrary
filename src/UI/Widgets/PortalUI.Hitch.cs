using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LevelLibrary;

/// <summary>卡顿点名：日志量过"开始界面上滑 / 点登录 / 进主界面"这三处 0.2~2.5 秒的巨卡，
/// 且同一操作两次差 10 倍 —— 说明不是固定建界面开销，而是执行期间被别的东西卡住了。
/// 于是做帧级点名：帧间隔超阈值就写一行（GC 计数增量 / 托管堆 / 场景 / 面板 / 视频状态 / 我们的后台活），
/// 用来一眼分清是 GC、是别的东西占主线程、还是我们自己的某一段。成本每帧 3 次 GC.CollectionCount，只在卡的时候写日志。</summary>
public static partial class PortalUI {

    private const float HitchMinSeconds = 0.10f;      // 帧间隔超过它才算"卡"（100ms ≈ 掉到 10fps 以下）
    private const float HitchMinGapSeconds = 0.35f;   // 同一个卡顿不重复报
    private const int HitchMaxLines = 80;             // 整个会话的卡顿日志上限
    private const int HitchRearmMax = 20;             // 卡顿后"接着逐帧记 120 帧"的次数上限（防刷爆日志）
    private const double OurSlowTickMs = 60.0;        // 我们自己的 Tick 超过它就要说明"慢在哪一段"
    private const int OurSlowTickMaxLines = 60;

    private static int _hitchLines;
    private static float _hitchLastAt = -10f;
    private static int _hitchRearms;
    private static int _hitchGc0, _hitchGc1, _hitchGc2;
    private static long _hitchHeap;
    private static bool _hitchHeapValid;
    private static double _hitchPrevTickMs;   // 上一帧我们 Tick 花了多久（那行报的就是上一帧，对得上才不误导）
    private static long _hitchWallLastTicks;  // 上一次 Tick 的 Stopwatch 刻度（算墙钟帧间隔，见 HitchFrame）

    // ---- 帧内三段拆分：一帧里"时间到底花在哪一段" ----
    // 一帧的顺序是：输入 → Update（我们的 Tick 在里面，记 tickEnd）→ LateUpdate →
    // Application.onBeforeRender（记 br）→ 渲染/呈现 → 下一帧开头 → 又轮到我们的 Tick
    // 于是拆成三段：① 我们 Tick 内；② Tick 之后到渲染前（其余 Update + LateUpdate，别的 mod 的代码常在这段）；
    // ③ 渲染前到下一帧 Tick（渲染 + 呈现 + 输入轮询 + 下一帧开头的 Update）。
    // onBeforeRender 挂在静态事件上，不需要新建任何 GameObject（也就不动"我们名下根对象"那份名单）。
    private static bool _hitchBrHooked;
    private static float _hitchBrAt = -1f;
    private static int _hitchBrFrame = -1;
    private static float _hitchTickEndAt = -1f;

    private static void EnsureBeforeRenderHook() {
        if (_hitchBrHooked) return;
        _hitchBrHooked = true;
        try { Application.onBeforeRender += OnBeforeRenderProbe; } catch { }
    }

    private static void OnBeforeRenderProbe() {
        try { _hitchBrAt = Time.realtimeSinceStartup; _hitchBrFrame = Time.frameCount; } catch { }
    }

    /// <summary>帧末调用（Tick() 的最后一步）。
    /// Time.unscaledDeltaTime 在一帧内恒定，描述的是刚刚跑完的那一帧有多长，
    /// 所以这里报的"帧间隔"和同一行的"本帧Tick"是同一帧，对得上才能下结论。
    /// 另有一条独立的墙钟帧间隔（Stopwatch 自己数两次 Tick 之间的真实时间）：
    /// 日志里出现过"帧间隔=965ms"而三段加起来才 10ms 的自相矛盾，两条都打出来当场就能判该信谁。</summary>
    private static void HitchFrame(double tickMs) {
        try {
            EnsureBeforeRenderHook();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            float dt = Time.unscaledDeltaTime;
            // 墙钟帧间隔：两次 Tick 之间的真实时间（与引擎的 delta 无关）
            long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            double wallMs = _hitchWallLastTicks != 0
                ? (nowTicks - _hitchWallLastTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency : 0;
            _hitchWallLastTicks = nowTicks;
            if (dt >= HitchMinSeconds && _hitchLines < HitchMaxLines
                && Time.unscaledTime - _hitchLastAt >= HitchMinGapSeconds) {
                _hitchLastAt = Time.unscaledTime;
                _hitchLines++;
                long heap = GC.GetTotalMemory(false);
                string heapDelta = _hitchHeapValid ? ((heap - _hitchHeap) / 1024.0).ToString("F0") + "KB" : "?";
                _hitchHeap = heap; _hitchHeapValid = true;
                // 上一帧的三段（这一行报的就是上一帧，所以三段也必须是上一帧的）
                string split = "分段=?";
                if (_hitchBrFrame == Time.frameCount - 1 && _hitchBrAt > 0f && _hitchTickEndAt > 0f) {
                    double mid = (_hitchBrAt - _hitchTickEndAt) * 1000.0;                 // ②Tick 后 → 渲染前
                    double tail = (Time.realtimeSinceStartup - _hitchBrAt) * 1000.0;      // ③渲染前 → 现在
                    split = $"分段=Tick后到渲染前 {mid:F0}ms / 渲染+呈现 {tail:F0}ms";
                }
                Lg.Info($"[LevelLib] 【卡顿】帧号={Time.frameCount} 帧间隔={dt * 1000f:F0}ms"
                    + $" 墙钟={wallMs:F0}ms 本帧Tick={tickMs:F1}ms 上帧Tick={_hitchPrevTickMs:F1}ms {split}"
                    + $" ｜ GC +{g0 - _hitchGc0}/{g1 - _hitchGc1}/{g2 - _hitchGc2}（累计 {g0}/{g1}/{g2}）"
                    + $" ｜ 托管堆={heap / 1048576.0:F1}MB（较上次卡顿 {heapDelta}）"
                    + $" ｜ 场景={HitchSceneName()} 面板={_panelOpen} 视频层={_bgVideoLayers.Count} 视频={HitchVideoText()}"
                    + $" ｜ 我们的后台活={Main.BackgroundRunning} 池行={_poolRows.Count} 逻辑行={_logicalRows.Count}");
                // 卡完之后紧接着逐帧记 120 帧（子阶段窗口），才能看清"卡完还在慢吗、慢在哪一段"
                if (_panelProfFrames <= 0 && _hitchRearms < HitchRearmMax) { _hitchRearms++; StartPanelProfiling(); }
            }
            _hitchGc0 = g0; _hitchGc1 = g1; _hitchGc2 = g2;
            _hitchPrevTickMs = tickMs;
            _hitchTickEndAt = Time.realtimeSinceStartup;   // 本帧基准（下一次 HitchFrame 时它就是"上一帧"）
        } catch { }
    }

    /// <summary>我们自己的 Tick 慢的时候（≥60ms）把本帧各段耗时写出来。
    /// 子阶段标记必须一直记（TickPhaseMark 现在就是一直记的），否则窗口关掉后只看到一个总数、定不了凶手。</summary>
    private static int _ourSlowTickLines;
    private static float _ourSlowTickLastAt = -10f;
    private static void HitchOurSlowTick(double tickMs) {
        try {
            if (tickMs < OurSlowTickMs) return;
            if (_ourSlowTickLines >= OurSlowTickMaxLines) return;
            if (Time.unscaledTime - _ourSlowTickLastAt < 0.3f) return;
            _ourSlowTickLastAt = Time.unscaledTime;
            _ourSlowTickLines++;
            string pt = PhaseText();
            Lg.Info($"[LevelLib] 【卡顿·我们】本帧 Tick={tickMs:F1}ms"
                + (string.IsNullOrEmpty(pt) ? "（没有单段超过 1.5ms ->  要么是很多段一起慢、要么别的线程在抢）"
                                            : "  Tick内: " + pt)
                + $" ｜ 场景={HitchSceneName()} 面板={_panelOpen} tab={_topTab} 视频层={_bgVideoLayers.Count}"
                + $" ｜ 后台活={Main.BackgroundRunning} 池行={_poolRows.Count}");
        } catch { }
    }

    private static string HitchSceneName() {
        try { return SceneManager.GetActiveScene().name; } catch { return "?"; }
    }

    private static string HitchVideoText() {
        try {
            if (_bgVideo == null) return "无";
            return (_bgVideo.isPlaying ? "播放中" : "停") + " 帧号=" + _bgVideo.frame + " 就绪=" + _bgVideo.isPrepared;
        } catch { return "?"; }
    }

    // ---- 卡点：分段秒表，回答"这几百毫秒花在哪一行" ----
    // 只在超过阈值时才写一行（正常是几毫秒一段，一行都不写，不刷屏）
    private const long StepMarkMinMs = 12;

    /// <summary>一处界面构建过程的分段秒表（各 Mark 之间自动算差）</summary>
    private sealed class StepWatch {
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private readonly string _tag;
        private long _last;
        public StepWatch(string tag) { _tag = tag; }
        public void Mark(string name) {
            try {
                long now = _sw.ElapsedMilliseconds;
                long d = now - _last;
                _last = now;
                if (d >= StepMarkMinMs) {
                    Lg.Info($"[LevelLib] 【卡点】{_tag}·{name} = +{d}ms（累计 {now}ms）");
                }
            } catch { }
        }
    }

    // ---- GC / 托管堆快照：证明"这几百毫秒到底是不是 GC 的账" ----
    private struct GcStamp { public int G0, G1, G2; public long Heap; }

    private static GcStamp TakeGc() {
        GcStamp s;
        try {
            s.G0 = GC.CollectionCount(0); s.G1 = GC.CollectionCount(1); s.G2 = GC.CollectionCount(2);
            s.Heap = GC.GetTotalMemory(false);
        } catch { s.G0 = 0; s.G1 = 0; s.G2 = 0; s.Heap = 0; }
        return s;
    }

    /// <summary>两枚快照之间的差异（跟在那三根【卡点】秒表后面）：
    /// GC +0/0/0 = 这一段里没有发生任何 GC 那几百毫秒不是 GC 的账。</summary>
    private static string GcDeltaText(GcStamp a, GcStamp b) {
        return $"GC +{b.G0 - a.G0}/{b.G1 - a.G1}/{b.G2 - a.G2} 堆差={(b.Heap - a.Heap) / 1024.0:F0}KB 堆={b.Heap / 1048576.0:F1}MB";
    }

    /// <summary>调用点的"上下文"：上一帧多长（这几百毫秒是搭在一个本来就卡的帧上，还是一片平顺里的孤峰）</summary>
    private static string FrameContextText() {
        try { return $"上一帧={Time.unscaledDeltaTime * 1000f:F0}ms"; } catch { return "上一帧=?"; }
    }
}
