using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>面板背景磨砂：把各页那块半透明黑遮罩做成"磨砂玻璃"。
/// 做法 = 在遮罩原位上、往它下面（压暗罩 Veil 与背景之间）塞一张这一块的糊图，糊图裁自 BgBlur 那条全屏 mip 链；
/// 不换素材、不加材质、一个颜色都不染、背景本身一动不动（静态裁剪，只跟着遮罩矩形走）。
/// 必须塞在 Veil 下面：塞在 Veil 上面时底下已经是 0.55×背景，半透明混完越糊越亮；各档 alpha 走与开始界面过渡同一套斜坡。</summary>
public static partial class PortalUI {

    // ================= 常量 =================

    /// <summary>滑条低于这个值 = 整块关掉（0 = 不开磨砂，拉到 1 才有）</summary>
    private const int PanelGlassMinPercent = 1;

    /// <summary>糊图相对遮罩矩形向内缩多少像素的下限。必须缩：遮罩是圆角的，而糊图是一张矩形
    /// （RawImage 没有圆角遮罩），不缩的话四个圆角外侧会露出一牙"没被压暗的亮背景" ——
    /// 弹窗上那圈白边就是这么来的。实际缩多少按每块自己的圆角半径算（见 GlassCropInsetFor），
    /// 这个 4 只够半径 10 那档（10 × 0.293 = 2.93）。</summary>
    private const float GlassCropInset = 4f;

    /// <summary>遮罩轮廓外那条柔和阴影的外扩量（画布像素）：羽化带就铺在轮廓外这一圈里。
    /// 它同时是阴影贴图四周留的余量，贴图边长 / 9-slice border / 这里的数必须同源，不然几何对不上。
    /// 取 4 的倍数（栅格那套口径把带 Pad 的常量都当间距看）。</summary>
    private const float GlassShadowPad = 24f;

    /// <summary>阴影最浓处有多浓（0..1）。阴影跟糊图同一格、都压在压暗罩下面，那层会把整格一起乘一遍，
    /// 所以这里给的就是"相对周围暗几成"：0.45 = 紧贴轮廓那圈暗四成半，再往外柔和收到零。</summary>
    private const float GlassShadowPeak = 0.40f;

    /// <summary>羽化从轮廓外这么多像素才开始：轮廓里侧全透明，一点灰都不许糊进面板本体
    /// （遮罩是半透明的，里面多一层灰一眼就看得出来）。</summary>
    private const float GlassShadowInner = 1f;

    // ================= 状态 =================

    /// <summary>一块"要磨砂的遮罩"，登记一次、之后每帧跟着它走</summary>
    private sealed class GlassPanel {
        public Image Backdrop;                       // 跟谁：读它的 alpha（淡入淡出比例）、显隐、矩形
        public RectTransform Rect;
        /// <summary>这块遮罩的锚画布：弹窗那些自带一块独立画布（糊图要挂进那块画布里），
        /// 面板里那些遮罩这里留空 = 走原来那条"挂在面板画布、排在压暗罩下面"的老路。</summary>
        public Transform RootParent;
        /// <summary>糊图插在谁的下面（同级）。面板那条老路留空 = 插在压暗罩 Veil 下面一档；
        /// 自带画布的那条路（弹窗 / 个人页）用它指定"插在这一层的下面"——不给就落在画布最底，
        /// 而那底下可能还压着一块不透明底板，糊图就白挂了。</summary>
        public Transform InsertBefore;
        /// <summary>父链扫到谁为止（找要乘进去的 CanvasGroup）。面板那条路是面板画布；
        /// 自带画布的那条路是它自己那块画布 —— 不能拿面板画布当终点：面板关着时它是 null，
        /// 那样一个 CanvasGroup 都扫不到，遮罩在淡入淡出、糊图却一直按全不透明显示。</summary>
        public Transform StopAt;
        public float BaseAlpha = 1f;                 // 建的时候那档 alpha，当分母算"淡到几成"
        public readonly RawImage[] Crop = new RawImage[BgBlurMaxLevels];
        public readonly float[] Applied = new float[BgBlurMaxLevels];   // 上次写下去的 alpha（变了才写）
        /// <summary>轮廓外那条柔和阴影（跟糊图同挂一块子画布、压在糊图上面、比遮罩大一圈）。用到才挂</summary>
        public Image Shadow;
        public float AppliedShadow = -1f;            // 上次写下去的浓度（变了才写）
        public float LastX, LastY, LastW, LastH;     // 上次跟到的屏幕矩形（变了才写）
        public bool RectValid;
        public bool GroupsReady;                     // 父链上那几个 CanvasGroup 采一次就够
        public readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
        /// <summary>这块遮罩自己是不是直角矩形。圆角的要往里缩一圈（不然四个圆角外会露出没压暗的亮背景），
        /// 直角的一缩反而会在四边露出一圈没糊的原背景 —— 大厅那几块按钮都是直角，走的就是这条路。</summary>
        public bool Square;
    }

    private static readonly List<GlassPanel> _glassPanels = new List<GlassPanel>();
    /// <summary>各档的目标 alpha。与 _glassCorners 都复用同一个数组：滑条拖动时每帧都在调，每帧 new 一次就是每帧一次 GC</summary>
    private static readonly float[] _glassAlpha = new float[BgBlurMaxLevels];
    private static readonly Vector3[] _glassCorners = new Vector3[4];
    /// <summary>装所有糊图的独立子画布（面板画布一销毁它就成了"假的 null"，下次开面板自动重建）</summary>
    private static RectTransform _glassRoot;
    private static bool _glassLogged;                // "已挂 N 层"那行日志一次面板只打一次

    // ================= 登记 =================

    /// <summary>建遮罩的时候调一句，把这块遮罩登记成"要磨砂的"。
    /// 只登记矩形与 alpha 的出处，糊图那几层等真正用到时才挂（滑条 0 的玩家一个对象都不会多）。
    /// 同一张图重复调只登记一次；页面重建过，旧的那份在下一次 PruneGlassPanels 里自动剔掉。</summary>
    /// <summary>诊断用：把每一块登记进磨砂的遮罩名字+矩形打出来（每 2 秒开 0.2 秒的闸打全量，见 Tick）。</summary>

    /// <summary>裁片几何日志的节流时刻</summary>
    private static float _glassRectDiagAt;
    /// <summary>裁片日志的"开闸"标记：每 2 秒开 0.2 秒，让 Sync 里那一轮把所有块都打全（只打一块会漏掉真凶）</summary>
    private static bool _glassRectDiagOn;

    private static void RegisterGlassPanel(Image backdrop, Transform rootParent = null, Transform insertBefore = null,
                                           bool square = false) {
        try {
            if (backdrop != null) {
                var rt = backdrop.rectTransform;
                Lg.Info("磨砂登记诊断：" + backdrop.name
                    + " 尺寸=" + rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0")
                    + " 屏幕位置=" + rt.position.x.ToString("F0") + "," + rt.position.y.ToString("F0"));
            }
        } catch { }
        try {
            if (backdrop == null) return;
            // 锚画布：面板那条路可以留空（那就用面板画布），弹窗那条路必须给（它有自己的画布、而且弹窗
            // 常常在面板关着的时候出现，用面板画布就没有落脚点）。两条都没有就登记不了，直接放弃。
            if (rootParent == null && (_panelCanvas == null || _panelVeilImg == null)) return;
            for (int i = _glassPanels.Count - 1; i >= 0; i--) {
                var old = _glassPanels[i];
                if (old == null || old.Rect == null || old.Backdrop == null) { _glassPanels.RemoveAt(i); continue; }
                if (old.Backdrop == backdrop) return;         // 已经登记过了
            }
            var g = new GlassPanel();
            g.Backdrop = backdrop;
            g.Rect = backdrop.rectTransform;
            g.RootParent = rootParent;
            g.InsertBefore = insertBefore;
            g.StopAt = rootParent != null ? rootParent
                     : (_panelCanvas != null ? _panelCanvas.transform : null);
            g.BaseAlpha = backdrop.color.a > 0.001f ? backdrop.color.a : 1f;
            g.Square = square;
            _glassPanels.Add(g);
        } catch (System.Exception e) { Lg.Ex("登记面板磨砂失败", e); }
    }

    /// <summary>模糊链要不要为面板磨砂也转起来（TickBgBlur 的第一道闸问它）。
    /// 开始界面那条过渡与面板磨砂共用同一条链，各烘一条就是双份开销。</summary>
    private static bool PanelGlassNeedsChain() {
        return !_bgBlurDead && _glassPanels.Count > 0 && PanelBlur >= PanelGlassMinPercent;
    }

    /// <summary>面板之外还有别的地方要用这条链（进谱面过渡层的那个信息框磨砂底）。
    /// 面板一关、那些遮罩对象就没了，`PanelGlassNeedsChain` 变假、链子停转，
    /// 而过渡层恰恰是在面板关了之后才演的，所以它得自己把需求挂上（见 PortalUI.EnterTransition）。</summary>
    private static bool _glassExtraWant;

    /// <summary>个人页那块磨砂框也要这条链子（它跟过渡层一样是在面板关着的时候演的）。
    /// 与 _glassExtraWant 分开两个开关：过渡层演完把自己的需求撤掉时，不许把个人页这份一起带走。</summary>
    private static bool _glassProfileWant;

    /// <summary>个人页要不要磨砂：只挂需求。糊图源该裁视频纹理还是代码背景，由那一屏自己每帧声明
    /// （见 PortalUI.ProfileScreen：只有视频层真的显形了才让糊图跟着走视频）。</summary>
    private static void SetProfileGlassWant(bool on) {
        _glassProfileWant = on;
    }

    /// <summary>某块遮罩的 alpha 被按那一档系数重新落实过，把磨砂这边的基准跟着改过来。
    /// 库页详情区底色建的时候是 0.75、被压成 0.30 却发生在登记之后（ApplyPanelBackdropAlpha）：
    /// 基准不跟过来，GlassVisibleK 就会把这次永久缩放当成"淡到四成"，那块的磨砂只剩四成。
    /// 契约：谁改了遮罩的 alpha 谁就喊这里一声。</summary>
    private static void RefreshGlassBaseline(Image img) {
        try {
            if (img == null) return;
            for (int i = 0; i < _glassPanels.Count; i++) {
                var g = _glassPanels[i];
                if (g == null || g.Backdrop != img) continue;
                g.BaseAlpha = img.color.a > 0.001f ? img.color.a : 1f;
            }
        } catch { }
    }

    // ================= 每帧 =================

    /// <summary>常驻段每帧调，必须排在 TickBgBlur 之后（链子和各档 RT 得先备好）</summary>
    private static void TickPanelGlass() {
        try {
            if (_glassPanels.Count == 0) return;                   // 面板关着，零开销
            PruneGlassPanels();
            if (_glassPanels.Count == 0) return;
            // 面板关掉之后 _panelCanvas 与压暗罩都没了，但弹窗那份磨砂还活着（它有自己的锚画布），
            // 所以这里不再要求面板存在：每块遮罩能不能出图由它自己的锚画布决定（见 EnsureGlassRootFor）。

            int depth = Mathf.Clamp(_bgBlurDepth, 1, BgBlurMaxLevels);
            float k = Mathf.Clamp01(PanelBlur / 100f);
            bool on = PanelBlur >= PanelGlassMinPercent && !_bgBlurDead && _bgBlurRt[0] != null;
            // 斜坡：跟开始界面那条同一公式，任何时刻最多两层在混、只混相邻档
            int top = -1;
            for (int j = 0; j < BgBlurMaxLevels; j++) {
                _glassAlpha[j] = on ? Mathf.Clamp01(k * depth - j) : 0f;
                if (j < depth && _glassAlpha[j] >= 1f) top = j;     // 最上面那张"已经铺满"的档
            }
            float sw = Mathf.Max(1f, Screen.width), sh = Mathf.Max(1f, Screen.height);
            try {   // 每 2 秒开 0.2 秒的闸：Sync 那一轮会把所有裁片打全
                if (Time.unscaledTime >= _glassRectDiagAt) { _glassRectDiagAt = Time.unscaledTime + 2f; _glassRectDiagOn = true; }
                else if (Time.unscaledTime >= _glassRectDiagAt - 1.8f) _glassRectDiagOn = false;
            } catch { }
            for (int i = _glassPanels.Count - 1; i >= 0; i--) {
                var g = _glassPanels[i];
                if (g == null || g.Rect == null || g.Backdrop == null) { _glassPanels.RemoveAt(i); continue; }
                float vis = GlassVisibleK(g);                       // 0 = 现在看不见（收起 / 淡出中）
                if (!on || vis <= 0.001f) {
                    // 跳过时也要说清是谁、为什么：这一块被跳过时界面上就是"没有磨砂"，
                    // 而登记那行日志只打矩形、打不出"可见度算出来是 0"，排查时看着最费劲。
                    if (_glassRectDiagOn && g.Backdrop != null) {
                        try {
                            Lg.Info("磨砂跳过：" + g.Backdrop.name
                                + " 链开=" + on + " 可见度=" + vis.ToString("F3")
                                + " 基准alpha=" + g.BaseAlpha.ToString("F3")
                                + " 当前alpha=" + g.Backdrop.color.a.ToString("F3"));
                        } catch { }
                    }
                    HideGlassCrops(g);
                    continue;
                }
                EnsureGlassCrops(g, depth);
                EnsureGlassShadow(g);
                if (!SyncGlassRect(g, sw, sh)) { HideGlassCrops(g); continue; }
                SyncGlassShadow(g, vis);
                for (int j = 0; j < BgBlurMaxLevels; j++) {
                    var ri = g.Crop[j];
                    if (ri == null) continue;
                    // 拉满的那几张里只有最上面那张看得见，下面那些是白白几块 overdraw
                    float a = (j < depth) ? _glassAlpha[j] * vis : 0f;
                    bool show = a > BgBlurMinAlpha && j >= top && _bgBlurRt[j] != null;
                    var tex = show ? (Texture)_bgBlurRt[j] : null;
                    if (ri.texture != tex) ri.texture = tex;         // 只有换档/关掉时才写
                    if (Mathf.Abs(g.Applied[j] - a) > 0.002f) {
                        g.Applied[j] = a;
                        // 白色，一个色都不染；只铺八成五 —— 糊图现在是不透明的整块背景（拍平见 NetBg），
                        // 铺满会把遮罩自己那层压暗底色全盖掉，面板浮在背景上就分不出层次了。
                        ri.color = new Color(1f, 1f, 1f, a * 0.85f);
                    }
                    if (ri.enabled != show) ri.enabled = show;
                }
            }
        } catch (System.Exception e) {
            // 出错就整块收干净（背景照旧清晰）——但不自杀：面板每开一次都会重新登记，下次照样能用
            try { Lg.Ex("面板磨砂失败 ->  这一帧不显示（背景保持清晰）", e); } catch { }
            try {
                for (int i = 0; i < _glassPanels.Count; i++) HideGlassCrops(_glassPanels[i]);
            } catch { }
        }
    }

    /// <summary>按"现在这一刻的世界矩形"把已登记的磨砂重算一次（只算几何，不碰 alpha、贴图档位、可见比例）。
    /// 为什么要单开一支：常驻段跑在 Update 里，而弹窗的弹入是协程驱动的 —— 协程在 Update 之后才推进，
    /// 于是常驻段那一次同步看到的是上一帧的缩放。弹窗从过冲收回来那一截（盒子在变小）里，糊图还停在
    /// 上一帧更大的尺寸上，就从盒子四边露出一圈比盒子亮的边，等动画停下（缩放不再变）那圈边又自己消失。
    /// 谁在常驻段之外改了被磨砂的矩形（目前只有弹窗弹入那一支协程），改完立刻调这一支补一次。</summary>
    public static void SyncGlassNow() {
        if (_glassPanels.Count == 0) return;
        try {
            float sw = Mathf.Max(1f, Screen.width), sh = Mathf.Max(1f, Screen.height);
            for (int i = _glassPanels.Count - 1; i >= 0; i--) {
                var g = _glassPanels[i];
                if (g == null || g.Rect == null || g.Backdrop == null) continue;
                if (!SyncGlassRect(g, sw, sh)) continue;
                SyncGlassShadow(g, GlassVisibleK(g));
            }
        } catch (System.Exception e) {
            // 补同步失败只影响这一帧的那圈边，下一次常驻段照旧；日志只报第一次，免得每帧刷屏
            if (!_glassSyncFailLogged) {
                _glassSyncFailLogged = true;
                try { Lg.Ex("磨砂补同步失败（这一帧不补，常驻段下一次照旧）", e); } catch { }
            }
        }
    }

    private static bool _glassSyncFailLogged;

    /// <summary>这块遮罩"现在露几成"（0 = 藏起来了 / 正在淡出）。三样都要算：
    /// ① 自己或祖先被 SetActive(false)；② alpha 被按比例改过（拿建的时候那档当分母）；③ 父链上的 CanvasGroup。
    /// 只走到画布为止：画布自己那个 CanvasGroup（面板 0→1 淡入）本来就会一起乘到糊图上，再乘一次就是乘两遍。</summary>
    private static float GlassVisibleK(GlassPanel g) {
        if (!g.Backdrop.isActiveAndEnabled) { g.RectValid = false; return 0f; }
        // 基准 alpha 为 0 的遮罩（底色本来就是全透明、靠别的东西表达"有没有"）不能按比例判成 0：
        // 那种情况下 BaseAlpha 退回的是 1、当前色仍是 0，除完恒为 0，这一块就永远不出图。
        // 基准为 0 时改为以当前色为准（当前色 0 就是真不可见）。
        float a = g.BaseAlpha > 0.001f
            ? Mathf.Clamp01(g.Backdrop.color.a / g.BaseAlpha)
            : Mathf.Clamp01(g.Backdrop.color.a);
        if (a <= 0.001f) return 0f;
        if (!g.GroupsReady) {
            g.GroupsReady = true;
            g.Groups.Clear();
            Transform t = g.Rect;
            while (t != null && t != g.StopAt) {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null) g.Groups.Add(cg);
                t = t.parent;
            }
        }
        for (int i = 0; i < g.Groups.Count; i++) {
            var cg = g.Groups[i];
            if (cg == null) continue;
            a *= Mathf.Clamp01(cg.alpha);
        }
        return a;
    }

    // ================= 挂糊图那几层 =================

    /// <summary>装所有糊图的独立子画布。往面板画布上直接加 Graphic 会逼整块面板重建一次网格（实测 43ms），
    /// 独立子画布只让重建落在这一小块上。
    /// 绝不设 overrideSorting：子画布默认按层级顺序画在原位，一设就跳到整个面板之上（跟库页 ListRoot 同一套做法）。</summary>
    private static void EnsureGlassRoot() {
        if (_glassRoot != null) return;
        if (_panelCanvas == null || _panelVeilImg == null) return;
        // SetParent 必须紧跟在 new GameObject 的下一行（外部工具按两行窗口判"是不是顶层对象"）
        var go = new GameObject("GlassCrops", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(_panelCanvas.transform, false);
        go.transform.SetSiblingIndex(_panelVeilImg.transform.GetSiblingIndex());   // = Veil 下面一档
        Stretch(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        _glassRoot = go.GetComponent<RectTransform>();
    }

    /// <summary>这一块的糊图该挂到哪块画布下：锚画布为空 = 面板那条老路（挂面板画布、排在压暗罩下面一档）；
    /// 有锚画布 = 弹窗那条路（挂进弹窗自己的画布、排在最底下 —— 盒子是半透明的，透过它看到的正是这一层）。
    /// 同一块画布只建一份，按名字复用。</summary>
    private static RectTransform EnsureGlassRootFor(GlassPanel g) {
        if (g == null) return null;
        if (g.RootParent == null) {
            EnsureGlassRoot();
            return _glassRoot;
        }
        try {
            var found = g.RootParent.Find("GlassCrops");
            if (found != null) return found as RectTransform;
            // SetParent 必须紧跟在 new GameObject 的下一行（外部工具按两行窗口判"是不是顶层对象"）
            var go = new GameObject("GlassCrops", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(g.RootParent, false);
            // 插在指定那一层的下面（同级更靠前 = 更早画）；没指定就落画布最底
            if (g.InsertBefore != null && g.InsertBefore.parent == g.RootParent) {
                go.transform.SetSiblingIndex(g.InsertBefore.GetSiblingIndex());
            } else {
                go.transform.SetSiblingIndex(0);
            }
            Stretch(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            return go.GetComponent<RectTransform>();
        } catch { return null; }
    }

    /// <summary>给这一块把用得着的档挂上（用到才挂，滑条 0 的玩家一个对象都不会多）。
    /// 糊图不吃射线，绝不能截胡上面的按钮/列表；同级顺序 = 挂上来的先后：同一块遮罩里档号大的必须在上面
    /// （斜坡是叠出来的），各块遮罩之间不重叠所以先后无所谓。</summary>
    private static void EnsureGlassCrops(GlassPanel g, int depth) {
        for (int j = 0; j < depth && j < BgBlurMaxLevels; j++) {
            if (g.Crop[j] != null) continue;
            var root = EnsureGlassRootFor(g);
            if (root == null) return;
            var go = new GameObject("GlassCrop" + j, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var ri = go.AddComponent<RawImage>();
            ri.raycastTarget = false;
            ri.texture = null;
            ri.color = new Color(1f, 1f, 1f, 0f);
            ri.enabled = false;
            var rt = ri.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;     // 左下角锚 + 左下角轴心，所以 position 就是左下角
            rt.pivot = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            g.Crop[j] = ri;
            g.Applied[j] = -1f;
            g.RectValid = false;                            // 新层，位置重新跟一遍
            // 新补上来的档要排在阴影下面：阴影永远压在糊图上面，不然它会被后补的糊图盖住
            if (g.Shadow != null) g.Shadow.rectTransform.SetAsLastSibling();
            if (!_glassLogged) {
                _glassLogged = true;
                Lg.Info("面板磨砂：已挂 " + depth + " 层（裁自全屏背景模糊链 · "
                    + "挂在压暗罩下面那层独立子画布里 · 不吃射线 · 滑条 " + PanelBlur + "% 共 "
                    + _glassPanels.Count + " 块遮罩）");
            }
        }
    }

    /// <summary>给这一块挂上轮廓外那条柔和阴影（用到才挂：滑条 0 / 面板关着的玩家一个对象都不会多）。
    /// 挂进糊图那块独立子画布里，绝不另起一块画布：一是这个文件只准两处子画布，
    /// 二是阴影每帧都在跟矩形走，落在面板自己那块画布上会把整块面板的网格重建一遍。</summary>
    private static void EnsureGlassShadow(GlassPanel g) {
        if (g == null || g.Shadow != null) return;
        var root = EnsureGlassRootFor(g);
        if (root == null) return;
        var sp = EnsureGlassShadowSprite(GlassShadowRadiusFor(g.Backdrop));
        if (sp == null) return;
        // SetParent 必须紧跟在 new GameObject 的下一行（外部工具按两行窗口判"是不是顶层对象"）
        var go = new GameObject("GlassShadow", typeof(RectTransform));
        go.transform.SetParent(root, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;                        // 绝不吃射线：它比遮罩还大一圈
        img.sprite = sp;
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0f);            // 白色（黑是贴图自带的）；从全透明起步，浓度每帧推
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;       // 与糊图同一套：左下角锚 + 左下角轴心
        rt.pivot = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.SetAsLastSibling();
        g.Shadow = img;
        g.AppliedShadow = -1f;
        g.RectValid = false;                              // 位置重新跟一遍
    }

    /// <summary>这块遮罩的圆角半径，直接读它贴图的 9-slice 边距（程序化圆角贴图那两族的 border 就是半径，
    /// 与描边框那边同一条口径）。读不到（方角贴图 / 没贴图）就按主圆角那档走。
    /// 半径必须跟被衬的那块遮罩一致：不匹配时轮廓会在圆角处切进面板本体。</summary>
    private static int GlassShadowRadiusFor(Image backdrop) {
        try {
            if (backdrop != null && backdrop.sprite != null) {
                float b = backdrop.sprite.border.x;
                if (b > 0.5f) return Mathf.Clamp(Mathf.RoundToInt(b), 4, 32);
            }
        } catch { }
        return 10;
    }

    /// <summary>这一块糊图要往里缩多少：圆角越大，圆角与矩形之间的斜角空隙越宽（半径 r 的圆角离
    /// 矩形角点 0.2929r），缩得不够就会在四个圆角外露出一牙糊图。半径 10 那档算出来 3，还是用下限 4；
    /// 弹窗那档 22 要缩 7，不缩就在四角上各留一块方形亮斑。</summary>
    private static float GlassCropInsetFor(Image backdrop) {
        float want = GlassShadowRadiusFor(backdrop) * 0.2929f;
        return want > GlassCropInset ? Mathf.Ceil(want) : GlassCropInset;
    }

    /// <summary>阴影贴图按圆角半径分档缓存（实际界面只会用到两三档）。失败也记下来，免得每帧重试刷日志</summary>
    private static readonly Dictionary<int, Sprite> _glassShadowSprites = new Dictionary<int, Sprite>();

    private static Sprite EnsureGlassShadowSprite(int radius) {
        Sprite sp;
        if (_glassShadowSprites.TryGetValue(radius, out sp)) return sp;
        try {
            sp = CreateRoundedShadowSprite(radius, GlassShadowPad, GlassShadowInner, GlassShadowPeak);
        } catch (System.Exception e) {
            Lg.Ex("生成面板阴影贴图失败（这一块不加阴影，其它照旧）", e);
            sp = null;
        }
        _glassShadowSprites[radius] = sp;
        return sp;
    }

    /// <summary>阴影的浓度跟着这块遮罩的可见比例走（淡入淡出时一起淡）。只有变了才写：
    /// 每帧写 Image.color 会把那块子画布弄脏，静止时这里一个字节都不碰。</summary>
    private static void SyncGlassShadow(GlassPanel g, float vis) {
        var sh = g.Shadow;
        if (sh == null) return;
        float a = GlassShadowPeak * Mathf.Clamp01(vis);
        bool show = a > 0.002f;
        if (sh.enabled != show) sh.enabled = show;
        if (Mathf.Abs(g.AppliedShadow - a) > 0.002f) {
            g.AppliedShadow = a;
            sh.color = new Color(1f, 1f, 1f, a);         // 白色不染色，黑是贴图自带的
        }
    }

    /// <summary>把这一块跟到它那块遮罩的屏幕矩形上，返回"能不能显示"（矩形退化 / 已经不可见就是假）。
    /// 只有变了才写：每帧写 RectTransform / uvRect 会把画布弄脏（每帧重建那块网格），静止时这里一个字节都不碰。
    /// 不钳到屏幕内：标签页横滑时会有半块在屏幕外，uvRect 超出 [0,1] 时链子是 Clamp 采样，屏幕里那半块照样是对的（钳了才错）。</summary>
    private static bool SyncGlassRect(GlassPanel g, float sw, float sh) {
        g.Rect.GetWorldCorners(_glassCorners);          // Overlay 画布，世界坐标就是屏幕像素
        float x0 = _glassCorners[0].x, y0 = _glassCorners[0].y;
        float x1 = _glassCorners[2].x, y1 = _glassCorners[2].y;
        if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
        if (y1 < y0) { float t = y0; y0 = y1; y1 = t; }
        // 往里缩一圈：遮罩是圆角的，糊图是矩形，不缩就会在四个圆角外露出没压暗的亮背景
        // （缩多少按这块自己的圆角半径算：圆角越大，斜角空隙越宽）。
        // 直角的那几块（大厅的按钮）反过来，缩了会在四边露出一圈没糊的原背景，所以不缩。
        float inset = g.Square ? 0f : GlassCropInsetFor(g.Backdrop);
        x0 += inset; y0 += inset;
        x1 -= inset; y1 -= inset;
        float w = x1 - x0, h = y1 - y0;
        if (!(w > 1f) || !(h > 1f)) { g.RectValid = false; return false; }   // 收起成 0 宽那种
        bool moved = !g.RectValid
                  || Mathf.Abs(g.LastX - x0) > 0.05f || Mathf.Abs(g.LastY - y0) > 0.05f
                  || Mathf.Abs(g.LastW - w) > 0.05f || Mathf.Abs(g.LastH - h) > 0.05f;
        if (!moved) return true;
        g.RectValid = true;
        g.LastX = x0; g.LastY = y0; g.LastW = w; g.LastH = h;
        // 诊断（每秒最多一条）：把这块磨砂的实际矩形与屏幕尺寸打出来，用来定位"哪一块盖在左边那片上"
        try {
            if (_glassRectDiagOn) {     // 开闸期间：每一块都打（闸门由 Tick 那边每 2 秒开一小会儿）
                Lg.Info("磨砂裁片：" + (g.Backdrop != null ? g.Backdrop.name : "?")
                    + " 屏幕=" + sw.ToString("F0") + "x" + sh.ToString("F0")
                    + " 矩形=" + x0.ToString("F0") + "," + y0.ToString("F0")
                    + " " + w.ToString("F0") + "x" + h.ToString("F0"));
            }
        } catch { }
        var uv = new Rect(x0 / sw, y0 / sh, w / sw, h / sh);
        // 单位换算：上面量到的矩形是"屏幕像素"，而 RectTransform 的 sizeDelta 是"画布单位"——
        // 画布是 ScaleWithScreenSize（scaleFactor = 屏幕宽 / 1920），非 1920 宽的屏幕上直接把像素
        // 写进 sizeDelta，这块糊图会被画成 scaleFactor 倍大小（2560 宽就大三分之一、1280 宽就小三分之一），
        // 只有左下角钉在原位、越往右上偏得越多。背景是代码图（近乎均匀的暗）时看不出来，
        // 换成视频背景后就是"糊图跟背后的视频对不上、像撕裂错位"（洛洛报的分辨率一变就出现）。
        // 位置是世界坐标，不受缩放影响，不用换算。
        float uiScale = 1f;
        try {
            var cv0 = (g.Crop[0] != null) ? g.Crop[0].canvas : (g.Shadow != null ? g.Shadow.canvas : null);
            if (cv0 != null) {
                var root = cv0.rootCanvas;
                if (root != null && root.scaleFactor > 0f) uiScale = root.scaleFactor;
            }
        } catch { }
        for (int j = 0; j < BgBlurMaxLevels; j++) {
            var ri = g.Crop[j];
            if (ri == null) continue;
            var rt = ri.rectTransform;
            rt.sizeDelta = new Vector2(w / uiScale, h / uiScale);
            rt.position = new Vector3(x0, y0, 0f);
            if (ri.uvRect != uv) ri.uvRect = uv;
        }
        var gsh = g.Shadow;
        if (gsh != null) {
            // 阴影拿"没内缩"的那圈世界矩形（x0 / y0 是缩过的，加回来）再往外扩一圈：
            // 糊图往里缩是为了把圆角让出来，阴影正好相反，它要糊到圆角外面去。
            float extra = inset + GlassShadowPad;
            var srt = gsh.rectTransform;
            srt.sizeDelta = new Vector2((w + extra * 2f) / uiScale, (h + extra * 2f) / uiScale);
            srt.position = new Vector3(x0 - extra, y0 - extra, 0f);
        }
        return true;
    }

    /// <summary>把这一块全关掉（看不见 / 滑条 0 / 出错）—— 纹理也放掉，别抓着一张 RT 不放</summary>
    private static void HideGlassCrops(GlassPanel g) {
        if (g == null) return;
        for (int j = 0; j < BgBlurMaxLevels; j++) {
            var ri = g.Crop[j];
            if (ri == null) continue;
            if (ri.enabled) ri.enabled = false;
            if (ri.texture != null) ri.texture = null;
            if (g.Applied[j] != 0f) { g.Applied[j] = 0f; ri.color = new Color(1f, 1f, 1f, 0f); }
        }
        var sh = g.Shadow;
        if (sh != null) {
            if (sh.enabled) sh.enabled = false;
            if (g.AppliedShadow != 0f) { g.AppliedShadow = 0f; sh.color = new Color(1f, 1f, 1f, 0f); }
        }
    }

    /// <summary>剔掉已经销毁的（画布一销毁这些引用就成了"假的 null"）。
    /// 清单清空时把那行日志的"打过了"标记也复位，下次开面板还能看到一行。</summary>
    private static void PruneGlassPanels() {
        for (int i = _glassPanels.Count - 1; i >= 0; i--) {
            var g = _glassPanels[i];
            if (g == null || g.Rect == null || g.Backdrop == null) _glassPanels.RemoveAt(i);
        }
        if (_glassPanels.Count == 0) _glassLogged = false;
    }
}
