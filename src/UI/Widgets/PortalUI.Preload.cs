using System.Collections.Generic;
using UnityEngine;

namespace LevelLibrary;

/// <summary>界面预热：开面板那一下只建便宜且必要的（个人页整页），其余排队交给 TickPreload 逐帧做。
/// 不能一次性全建：游戏还在装 mod / 解场景时整个进程被按住，同样的活会慢二十倍（实测开面板 16985ms vs 闲时 300~573ms）——
/// 昂贵的不是代码，是把活干在"进程被按住"的时刻。
/// 预热不改行为：预热好的个人页每次显示前都会重新摆名字/头像/五处状态；没走开面板那条路的（屏保直接从主菜单冒出来）靠各处的 Ensure 兜底现建。</summary>
public static partial class PortalUI {

    /// <summary>mod 初始化时就调（游戏还在读盘、主菜单还没出来），把能提前做的全做掉：
    /// 个人页整页 + 跨场景常驻、字形图集（烘进游戏自己的字体）、程序化贴图、楷体与背景图、写盘线程。
    /// 面板里那几页做不了 —— 它们是面板画布的子物体，而画布要开面板时才建，只能等面板建好后排队。
    /// 开工前必须先问到字体：拿不到会退回 Arial（中文全白），那时个人页这次就别建，留到开面板时再建。</summary>
    public static void PrewarmWithGameLoad() {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            // ① 必须先问清字体：这一步漏了的话 _uiFont 还是 null，
            // MakeText 会把 font 设成 null，建出来的字一个字都不显示。
            if (_uiFont == null) _uiFont = FindUiFont();
            bool fontOk = _uiFont != null && (_uiFontSource == null || !_uiFontSource.Contains("退回内置 Arial"));
            if (!fontOk) {
                Lg.Info("【预热】开机时游戏原生字体还没就绪"
                    + "（拿不到就会退回 Arial 中文会全白）-> 个人页这次不建 留到开面板时再建");
                return;
            }
            // ② 个人页画布是独立的，开机就建好并设为跨场景常驻
            // （这样"进关卡 → 回主菜单"之后它也还在，上滑永远是"激活 + 摆状态"）
            EnsureProfileBuilt();
            try {
                if (_pfRefs != null && _pfRefs.Go != null && _pfRefs.Go.transform.parent == null)
                    UnityEngine.Object.DontDestroyOnLoad(_pfRefs.Go);
            } catch { }
            long tProfile = sw.ElapsedMilliseconds;
            // ③ 字形图集：烘进游戏自己的字体里，跨场景不会丢
            int glyphs = 0, groups = 0;
            PrewarmFontGlyphs(out glyphs, out groups);
            long tGlyph = sw.ElapsedMilliseconds;
            // ④ 程序化贴图 + 写盘线程（都是静态字段 / 独立线程，跨场景有效）
            var rounded = RoundedSprite;
            var frame = EnsureMaskFrameSprite();
            var circle = EnsureCircleSprite();
            PlayTime.WarmSaver();
            long tSprites = sw.ElapsedMilliseconds;
            // ⑤ 把"第一次开面板才会付的两笔大头"也提到开机：楷体（建中文动态字体）与背景底图（烘四张 + 上传纹理）。
            // 两样都进静态字段，跨场景有效；开机时游戏自己在读盘，这点开销看不出来。
            var kaiti = KaitiFont();
            PrewarmNetBg();
            // 默认头像动效不需要预热：那张贴图是常驻的、每帧现算（见 PortalUI.FluidOrb.cs）。
            long tNet = sw.ElapsedMilliseconds;
            Lg.Info($"[LevelLib] 【预热】开机随游戏一起加载"
                + $" 个人页已建好并设为跨场景常驻（{tProfile}ms）"
                + $" ｜ 字形 {glyphs} 个 / {groups} 组（+{tGlyph - tProfile}ms）"
                + $" ｜ 贴图就绪（圆角={rounded != null} 描边={frame != null} 圆={circle != null}）"
                + $" ｜ 楷体={(kaiti != null ? "已建好" : "拿不到（退回游戏字体）")}"
                + $" 背景底图已烘好（+{tNet - tSprites}ms）"
                + $" ｜ 写盘线程已建好 ｜ 合计 {sw.ElapsedMilliseconds}ms"
                + "（此刻游戏自己在读盘、主界面还没出来 这点开销看不出来）");
        } catch (System.Exception e) {
            Lg.Ex("开机预热失败（照旧按需构建）", e);
        }
    }

    private static readonly List<int> _preloadQueue = new List<int>();   // 还要建哪几页（2 成就 / 4 开发者 / 3 设置）
    private static bool _preloadPending;      // 队列里还有活
    private static bool _preloadFontsDone;    // 字形图集只烘一次
    private static int _preloadLogs;          // 日志上限，别刷屏
    private static float _preloadUnsteady;    // >0 = 最近两秒出现过卡帧（机器还在加载，先别干重活）
    private static float _preloadIdleT;       // 手停着多久了（要停够 PreloadIdleSeconds 才动手）
    private static Vector2 _preloadLastMouse = new Vector2(-1f, -1f);
    private const float PreloadIdleSeconds = 1.5f;   // 手停这么久才做下一件

    /// <summary>开面板的最后一步调用（见 PortalUI.PanelBuild.Tabs.cs 里「开面板总计」那一行之前）。
    /// 这里只做便宜的那一件，贵的排队交给 TickPreload。</summary>
    private static void PreloadAllScreens() {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            EnsureProfileBuilt();          // 个人页整页：实测几十毫秒（上滑那一下只剩"激活 + 摆状态"）
            long tProfile = sw.ElapsedMilliseconds;
            // 其余排队（顺序：先烘字形，再把三页建出来：成就 → 开发者 → 设置）
            _preloadQueue.Clear();
            _preloadQueue.Add(2);
            _preloadQueue.Add(4);
            _preloadQueue.Add(3);
            _preloadFontsDone = false;
            _preloadPending = true;
            Lg.Info($"[LevelLib] 【预热】个人页已建好收好（{tProfile}ms）"
                + " 其余（字形图集 + 成就/开发者/设置三页）已排队"
                + " 由每帧的 `TickPreload` 只在游戏跑得动的时候一件一件做 不再卡开面板那一下");
        } catch (System.Exception e) {
            Lg.Ex("界面预热失败（功能照旧 只是那几下还会现建）", e);
        }
    }

    /// <summary>常驻段每帧调：只在游戏跑得动的帧上做一件预热活（没活时只花一次 bool 判断）。
    /// 忙判据：Time.unscaledDeltaTime > 0.05f（≈20fps 以下）= 游戏在加载 / 在卡，这一帧不做。
    /// 面板关了或画布没了，预制品已随画布销毁，队列清空（下次开面板重新排）。</summary>
    public static void TickPreload() {
        if (!_preloadPending) return;
        if (!_panelOpen || _panelCanvas == null) { _preloadPending = false; _preloadQueue.Clear(); return; }
        try {
            float dt = Time.unscaledDeltaTime;
            // 忙闸门一：最近两秒内出现过 >60ms 的帧，说明机器还在加载，先别加码
            if (dt > 0.06f) _preloadUnsteady = 2f;
            else _preloadUnsteady = Mathf.Max(0f, _preloadUnsteady - dt);
            if (_preloadUnsteady > 0f) return;
            // 忙闸门二：他正在动鼠标 / 按着键，就不在他眼皮底下干重活。
            // 光看"这一帧快不快"不够：这些页在机器还在装 mod 时要 2 秒多，闲着时只要 113~139ms。
            var mp = UiInput.MousePosition;
            if (UiInput.MouseButton(0)
                || Mathf.Abs(mp.x - _preloadLastMouse.x) > 2f || Mathf.Abs(mp.y - _preloadLastMouse.y) > 2f) {
                _preloadIdleT = 0f;
            } else {
                _preloadIdleT += dt;
            }
            _preloadLastMouse = mp;
            if (_preloadIdleT < PreloadIdleSeconds) return;     // 手停够 1.5 秒才动手
            if (!_preloadFontsDone) {
                _preloadFontsDone = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                PrewarmFontGlyphs(out int glyphs, out int groups);
                if (_preloadLogs < 20) {
                    _preloadLogs++;
                    Lg.Info($"[LevelLib] 【预热】字形图集烘好了 字形 {glyphs} 个 / {groups} 组"
                        + $"（{sw.ElapsedMilliseconds}ms 这一帧是游戏跑得动的帧）");
                }
                return;                                     // 一帧只做一件
            }
            if (_preloadQueue.Count == 0) {
                _preloadPending = false;
                var rounded = RoundedSprite; var frame = EnsureMaskFrameSprite(); var circle = EnsureCircleSprite();
                PlayTime.WarmSaver();
                if (_preloadLogs < 20) {
                    _preloadLogs++;
                    Lg.Info($"[LevelLib] 【预热】全部完成 贴图就绪（圆角={rounded != null}"
                        + $" 描边={frame != null} 圆={circle != null}）｜ 写盘线程已建好"
                        + "（这一轮总耗时分散在很多帧上 ->  随时点成就/设置/开发者都不用再现建）");
                }
                return;
            }
            int tab = _preloadQueue[0];
            _preloadQueue.RemoveAt(0);
            // 护栏：玩家正看着的那一页一律不碰 —— 它切过去那一刻就建好了，后台再建一遍是白干，
            // 而且建页那条路会顺手改显隐/层级，可能动到他眼前这一页（见 Layout.cs 里 RebuildPage 那段）。
            if (tab == _topTab) {
                if (_preloadLogs < 20) {
                    _preloadLogs++;
                    Lg.Info($"[LevelLib] 【预热】「{TopTabName(tab)}」页玩家正看着 ->  跳过"
                        + "（它早就建好了 后台预热绝不许碰玩家眼前这一页）");
                }
                return;                      // 一帧只做一件，下一件等下一次"手停"
            }
            int keepBuilt = _topTabBuilt;
            var swTab = System.Diagnostics.Stopwatch.StartNew();
            RebuildPage(tab);
            // 必须还原：RebuildPage(3) 会顺手 SetSettingsPageVisible(true)，
            // 不还原就会出现"面板明明在主界面，设置页却被摆到最前面"。
            if (tab == SettingsSlotIndex) {
                SetSettingsPageVisible(_topTab == SettingsSlotIndex);
                _topTabBuilt = keepBuilt;
            }
            _preloadIdleT = 0f;     // 干完一件就重新等"手停"
            if (_preloadLogs < 20) {
                _preloadLogs++;
                Lg.Info($"[LevelLib] 【预热】「{TopTabName(tab)}」页已建好"
                    + $"（{swTab.ElapsedMilliseconds}ms 还剩 {_preloadQueue.Count} 页）");
            }
        } catch (System.Exception e) {
            Lg.Ex("排队预热出错（照旧按需构建）", e);
            _preloadQueue.Clear(); _preloadPending = false;
        }
    }

    /// <summary>面板关闭 / 重建时清掉队列（预制品随面板画布一起销毁了）</summary>
    public static void ResetPreloadState() {
        _preloadPending = false;
        _preloadQueue.Clear();
        _preloadFontsDone = false;
    }
}
