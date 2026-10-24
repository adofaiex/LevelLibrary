using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>进入谱面的过渡动画：三条入口（双击行 / 详情区「进入关卡编辑器」/ 随机谱面）都走 StartEnterTransition。
/// 过渡层是独立画布 + DontDestroyOnLoad（进谱面会换场景），sortingOrder 82000：面板之上、宠物之下。
/// 框外渐隐靠一层全屏不透明盖层，框内内容重建一份从右侧滑入；那只肥鱼只是过渡层自己画的同款图，不动玩家设置。
/// 时序：渐入 + 右移 0.45s → ConfirmOpen 真进关卡 → 等「加载卡顿结束」→ 整层淡出 0.4s → 销毁。</summary>
public static partial class PortalUI {

    private static GameObject _etGo;            // 过渡层根（独立画布）
    private static CanvasGroup _etGroup;        // 整层透明度：只用于淡出，渐入不用它
    /// <summary>只装框内内容的透明层，渐入淡它；盖层不参与，否则会露底。</summary>
    private static CanvasGroup _etContentGroup;
    /// <summary>代码背景那一组的淡入层：背景是独立一层，内容层淡不到它，没有这层的话背景是一帧就砸出来。</summary>
    private static CanvasGroup _etBgFade;
    /// <summary>信息框那块磨砂底（裁自全屏背景模糊链），见 TickEtGlass。</summary>
    private static RawImage _etGlass;
    private static readonly Vector3[] _etGlassCorners = new Vector3[4];
    /// <summary>这一轮那句台词是否已经交给真宠物说过（进场后第一帧才交给它，那时宠物已经显示出来了）。</summary>
    private static bool _etSayArmed;
    private static RectTransform _etBlockRt;    // 框内那块（要往右移）
    // 装饰物加载进度条（摆在画布下方，见 TickEtLoadBar）
    private static RectTransform _etLoadBarRt;  // 整块（底槽 + 填充 + 文字都在它下面）
    private static Image _etLoadFill;           // 主题色填充（左对齐，宽度 = 进度）
    /// <summary>填充块的基准 alpha（建层时从 color 读一次）：呼吸要按比例改它，写死 1 会让条比别处亮一档。</summary>
    private static float _etLoadFillA = 1f;
    private static Text _etLoadText;            // 「正在加载装饰物 X/Y」
    private static Image _etDiffImg;            // 难度图（P/G/U/Q，素材 assets\marks\<mark>.png）
    /// <summary>难度标记（P17 / G2 / U5J…），必须在 BuildEnterTransition 之前解析好 ——
    /// 难度图是建层时就画的，建完再设那一层永远看不到它（顺序铁律见 StartEnterTransition）。</summary>
    private static string _etMark;
    private static string _etFile;              // 动画结束后要打开的谱面
    private static int _etPhase;                // 0=渐入并右移 1=已进关卡等加载 2=淡出 3=收尾
    private static float _etT, _etWait, _etFade;
    /// <summary>这一层的画布高度（建层时从 Content 自己的 rect 读一次）。
    /// 不能用 Screen.height：画布是 ScaleWithScreenSize 缩放的，两者不一定相等。</summary>
    private static float _etCanvasH;

    private const float EtInSeconds = 0.45f;    // 渐入 + 右移
    private const float EtOutSeconds = 0.40f;   // 加载完的淡出
    // 这两个数只是兜底：正常路径永远是「IsLoading 变 false 就淡出」。
    // 必须比 Iridium 装饰物加载联动的极端兜底长，否则硬超时会抢在它前面放行 = 界面提前消失。
    private const float EtMaxWait = 620.0f;     // 等加载的兜底上限（正常由「加载完」提前放行）
    private const float EtHardLimit = 660.0f;   // 整段过渡的硬上限：真卡死时强制收尾救玩家
    private static float _etLife;              // 这段过渡已经活了多久（秒）
    // 进度条贴在画布下方：150 是距画布底的距离，不是距屏幕底（见 _etCanvasH）
    private const float EtLoadBarW = 900f;
    private const float EtLoadBarH = 28f;
    private const float EtLoadBarBottom = 150f;
    private const int EtLoadTextFont = 30;
    /// <summary>还在加载时进度文字后面那串点的轮播间隔（秒）。
    /// 队列归零后 Iridium 还要收尾几十秒、数字不再变，条满格 + 文字定格看着就是卡住了。</summary>
    private const float EtLoadDotSeconds = 0.4f;
    /// <summary>收尾段填充块的呼吸深度（0 = 不呼吸），满格但还在忙的第二个信号。</summary>
    private const float EtLoadTailPulse = 0.35f;
    /// <summary>呼吸速度（弧度 / 秒）：6 rad/s ≈ 1 秒一轮，看得清又不闪。</summary>
    private const float EtLoadTailPulseSpeed = 6f;
    /// <summary>难度图的边长（设计稿红圈直径 108，取 96 留点余量）。</summary>
    private const float EtDiffSize = 96f;
    /// <summary>砖块就绪后给 Iridium 的起跑时间：它的 IsLoading 是砖块就绪之后几行才置 true 的，
    /// 那一刻就去问答案永远是「没在加载」→ 立刻淡出、进度条一次都不亮。</summary>
    private const float EtIridiumGrace = 0.3f;
    private static float _etDecoGrace;   // 这一轮的起跑计时
    private const float EtBlockFromX = 150f;    // 框内内容起点
    /// <summary>框内内容右移的终点 = 画布正中（原来停在右侧 920，信息块偏右；现在整块居中）。
    /// 由画布参考宽与块宽推出来，改块宽自动跟着居中，不许写成数字。</summary>
    private const float EtBlockToX = (RefW - EtBlockW) * 0.5f;      // 终点（居中）
    private const float EtBlockW = 760f;        // 文本块宽度
    private const float EtBlockH = 460f;        // 文本块高度
    /// <summary>文本内容的实际高度（标题 0..110、元信息 118..328、成绩 340..400）。</summary>
    /// <summary>信息框那层磨砂底的透明度（糊图 + 下面那层半透明黑 = 磨砂遮罩）。</summary>
    private const float EtGlassAlpha = 0.75f;

    // ---------- 全屏盖层与右侧信息区遮罩 ----------
    /// <summary>全屏压暗层的透明度，与面板压暗层同一个值（见 PanelBuild 的 veil）：两层一致，过渡前后的明暗才接得上。</summary>
    // 进场遮罩与面板压暗层同源：跟着「背景压暗」滑条走（写死 0.45 的时代滑条调了它也不动）
    private static float EtVeilAlpha => Mathf.Clamp(PortalUI.BgVeilPercent, 0, 100) / 100f;
    // 信息区那块独立遮罩框：框住「要右移的那一整块内容」，是 block 的子物体所以跟着一起右移。
    // 本地原点 = block 的左侧中点（anchor / pivot 都是左中），纵向要减掉 EtBlockH/2。
    private const float EtInfoPadX = 20f;       // 框内左右留白（框比文字两边各宽这么多）
    /// <summary>框内上下留白：框高 = EtBlockH + 2×它。基准必须用 EtBlockH 而不是 EtTextContentH ——
    /// 文字本身恰好占满 block 的 460 高，用 EtTextContentH 算出来比 block 还矮，标题顶和成绩底会各戳出去。</summary>
    private const float EtInfoPadY = 24f;       // 框比内容高出来的部分 = 2 × 这个
    private const float EtInfoFrameRadius = 22f;   // 圆角与主页那三块大遮罩同款（HomeMaskRadius）

    /// <summary>三条入口都调它：先播过渡，动画放完再真的进关卡。</summary>
    private static void StartEnterTransition(string file) {
        try {
            // 这是「一次进入」的明确信号（三条入口都会走这里），音乐名播报据此复位（见 NoteLevelEntered）。
            // 必须排在最前面：下面有「没面板就直接 ConfirmOpen」的早退，放后面会漏掉那条路。
            AutoFeatures.NoteLevelEntered("开始进入过渡");
            if (string.IsNullOrEmpty(file)) { ConfirmOpen(file); return; }
            // 没面板（比如从别处触发）就别硬播，直接进
            if (!_panelOpen || _panelCanvas == null || _etGo != null) { ConfirmOpen(file); return; }
            var entry = LevelScanner.Entries != null
                ? LevelScanner.Entries.Find(x => string.Equals(x.FolderPath, _selectedPath, System.StringComparison.OrdinalIgnoreCase))
                : null;
            string title = entry != null && !string.IsNullOrEmpty(entry.Title) ? entry.Title : System.IO.Path.GetFileNameWithoutExtension(file);
            string meta = _detailMeta != null ? _detailMeta.text : "";
            string score = _detailScore != null ? _detailScore.text : "";
            // 顺序铁律：_etFile / _etMark 必须在 BuildEnterTransition 之前设好 ——
            // 难度图是建层时就画的，建完再设那一层永远看不到它。
            _etFile = file;
            // 难度标记先问要打开的那个文件，没有再问条目的主文件：玩家在选择关卡里挑了别的 .adofai 时，
            // 等级标是打在海报那个主文件上的，列表行用的就是 entry.FilePath，两处口径要一致。
            _etMark = null;
            try {
                _etMark = MarkStore.Get(file);
                if (string.IsNullOrEmpty(_etMark) && entry != null) _etMark = MarkStore.Get(entry.FilePath);
            } catch { }
            BuildEnterTransition(title, meta, score);
            _etPhase = 0;
            _etT = 0f;
            _etLife = 0f;
            // 真宠物一根手指都不碰：位置、大小、吸附全按玩家自己的设置，过渡层只负责让它显示出来。
            Lg.Info($"[LevelLib] 进入过渡：{title}（框内内容往中间移，动画结束后进入）");
        } catch (System.Exception e) {
            _etFile = null; _etMark = null;
            Lg.Ex("进入过渡失败（直接进入）", e);
            // 半路抛的时候层可能已经建了一半（_etGo 已设）：这里必须真收摊，
            // 否则那个半成品会一直挂着（_glassExtraWant 不复位就是背景模糊链空转）。
            FinishEnterTransition();
            ConfirmOpen(file);
        }
    }

    private static void BuildEnterTransition(string title, string meta, string score) {
        var cv = MakeCanvas("LevelLib_EnterTransition", 82000);   // 面板之上、宠物之下
        UnityEngine.Object.DontDestroyOnLoad(cv.gameObject);      // 必须跨场景活着（进谱面会换场景）
        _etGo = cv.gameObject;
        _etSayArmed = false;
        _glassExtraWant = true;      // 信息框那块磨砂底要链子转起来（面板关着，磨砂那条需求本来就断了）
        _etGroup = _etGo.AddComponent<CanvasGroup>();
        // 整层不从 0 渐入：盖层必须一上来就不透明；渐入交给下面的 _etContentGroup（只淡内容），
        // 淡出仍然淡整层（那时要露出游戏画面）。
        _etGroup.alpha = 1f;
        _etGroup.blocksRaycasts = false;                          // 过渡期间别再吃点击

        // ① 全屏底 + 压暗层：框选区域之外的内容全部渐隐，但不是一块死色 —— 跟面板 / 屏保同一套三层：
        // 纯色底 → 代码背景 → 压暗层。
        // 底必须不透明：代码背景满屏覆盖，它自己就把面板的列表 / 顶部 tab / 详情区全盖住。
        var back = MakeImage(cv.transform, "CoverSolid", new Color(BgColor.r, BgColor.g, BgColor.b, 1f));
        back.sprite = null;                       // 整屏不要圆角
        back.type = Image.Type.Simple;
        Stretch(back.rectTransform, Vector2.zero, Vector2.one);
        back.raycastTarget = false;

        // 进谱面是"登录之后"的世界，所以这里跟着玩家的动态背景走（就绪时上面那组代码背景会淡出）。
        // 代码背景装进自己的淡入层：它跟内容层是两块独立的 CanvasGroup，渐入要分别淡。
        var bgFade = new GameObject("BgFade", typeof(RectTransform));
        bgFade.transform.SetParent(cv.transform, false);
        Stretch(bgFade.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        _etBgFade = bgFade.AddComponent<CanvasGroup>();
        _etBgFade.alpha = 0f;      // 由相位 0 的渐入推到 1（底下的纯色底一上来就是不透明的，不会露后面的面板）
   // 静态背景图也装进这个淡入层：它和代码背景是互斥的两块，一起渐入才不会露馅
        AttachStaticBg(bgFade.transform, null);
        var net = BuildNetBg(bgFade.transform);
        AttachVideoBg(net);

        // 覆盖全背景的遮罩（值与面板压暗层同源，见 EtVeilAlpha）
        var veil = MakeImage(cv.transform, "CoverVeil", new Color(0f, 0f, 0f, EtVeilAlpha));
        veil.sprite = null;
        veil.type = Image.Type.Simple;
        Stretch(veil.rectTransform, Vector2.zero, Vector2.one);
        veil.raycastTarget = false;

        // 盖层恒不透明、只淡内容：淡整层的话渐入那 0.45 秒里盖层只有一半不透明，后面的界面会透出来。
        // 内容全部挂到 Content 下面，由 _etContentGroup 控制透明度。
        // Content 必须撑满画布，否则子物体的锚点基准会变（位置全错）。
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(cv.transform, false);
        Stretch(content.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        _etContentGroup = content.AddComponent<CanvasGroup>();
        _etContentGroup.alpha = 0f;

        // 这一层所有纵向定位都按画布自己的高度推，不拿 Screen.height / 2 当画布中心：
        // 画布是 ScaleWithScreenSize（参考 1920×1080，见 MakeCanvas），高度未必等于屏幕高，
        // 两者不等时越靠画布上边缘的东西越容易被推出屏幕。Content 是 Stretch 撑满的，rect 就是真实尺寸。
        _etCanvasH = Screen.height;         // 兜底：万一读不到就用屏幕高
        try {
            var crt = content.GetComponent<RectTransform>();
            if (crt != null && crt.rect.height > 1f) _etCanvasH = crt.rect.height;
        } catch { }
        Lg.Info($"[LevelLib] 进入过渡：画布高 {_etCanvasH:F0} ｜ 屏幕高 {Screen.height}（气泡纵向按前者摆）");

        // ①-c 装饰物加载进度条：Iridium 的分帧装饰物加载是场景进去之后才开始的，过渡层要留到
        // 「装饰物加载完」再淡出，这段时间给玩家看进度。挂在 Content 下（跟着内容渐入）而不是 block 下 ——
        // block 要往右移、进度条得待在原地。数据源 IridiumLink；纵向按 _etCanvasH 摆，不按 Screen.height。
        {
            var bar = new GameObject("LoadBar", typeof(RectTransform));
            bar.transform.SetParent(content.transform, false);
            _etLoadBarRt = bar.GetComponent<RectTransform>();
            _etLoadBarRt.anchorMin = _etLoadBarRt.anchorMax = new Vector2(0.5f, 0f);
            _etLoadBarRt.pivot = new Vector2(0.5f, 0f);
            _etLoadBarRt.sizeDelta = new Vector2(EtLoadBarW, EtLoadBarH);
            _etLoadBarRt.anchoredPosition = new Vector2(0f, EtLoadBarBottom);

            var slot = MakeImage(bar.transform, "Slot", new Color(0f, 0f, 0f, 0.55f));
            Stretch(slot.rectTransform, Vector2.zero, Vector2.one);
            slot.raycastTarget = false;

            var fill = MakeImage(bar.transform, "Fill", Accent);
            var frt = fill.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.sizeDelta = new Vector2(0f, EtLoadBarH - 8f);
            frt.anchoredPosition = new Vector2(4f, 0f);
            fill.raycastTarget = false;
            _etLoadFill = fill;
            _etLoadFillA = fill.color.a;   // 基准 alpha（= 主题色 × 面板透明度）；呼吸要按比例来，别写死 1

            _etLoadText = MakeText(bar.transform, "LoadText", EtLoadTextFont,
                                   new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleCenter);
            var trt = _etLoadText.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(900f, EtLoadTextFont + 18f);
            trt.anchoredPosition = new Vector2(0f, EtLoadBarH + 14f);
            _etLoadText.raycastTarget = false;
            _etLoadText.horizontalOverflow = HorizontalWrapMode.Overflow;

            bar.SetActive(false);   // 一开始不显示，只有 Iridium 真的在加载装饰物时才亮
        }

        // ② 框内内容：标题 + 元信息 + 成绩（成绩在下方，跟详情区新版式一致）
        var block = new GameObject("Block", typeof(RectTransform));
        block.transform.SetParent(content.transform, false);
        _etBlockRt = block.GetComponent<RectTransform>();
        _etBlockRt.anchorMin = _etBlockRt.anchorMax = new Vector2(0f, 0.5f);
        _etBlockRt.pivot = new Vector2(0f, 0.5f);
        _etBlockRt.sizeDelta = new Vector2(EtBlockW, EtBlockH);
        _etBlockRt.anchoredPosition = new Vector2(EtBlockFromX, 0f);

        // ②-b 难度图，位置取自设计稿红圈（中心屏幕 (1314, 698)、直径 108）；换算到 block 局部时
        // block 左边缘在 EtBlockToX(920)、顶边在屏幕 310（anchor / pivot 都是左中、半高 230），圈心 = (394, 388)。
        // 必须挂在 block 下，才会跟着框内内容一起右移；素材 = assets\marks\<mark>.png（128×128 抗锯齿圆图）。
        {
            var diff = MakeImage(block.transform, "DiffIcon", Color.white);
            var drt = diff.rectTransform;
            SetAtPx(drt, 394f - EtDiffSize * 0.5f, 388f - EtDiffSize * 0.5f, EtDiffSize, EtDiffSize);
            diff.raycastTarget = false;
            diff.preserveAspect = true;      // 圆图别拉成椭圆
            _etDiffImg = diff;
            try {
                // 读 _etMark（建层前已解析好、含 entry.FilePath 兜底），不读 _etFile
                string mark = _etMark;
                if (string.IsNullOrEmpty(mark)) {
                    diff.gameObject.SetActive(false);
                    Lg.Info("进入过渡：这个谱面没有难度标记 → 难度图不显示");
                } else {
                    // 用 MarkStore.LoadIcon 而不是自己 LoadAssetSprite：列表行画难度图用的就是它
                    // （同一套 128×128 圆图 + 双线性 + 负缓存），另起一套会同一个标记两处长得不一样。
                    var dspr = MarkStore.LoadIcon(mark);
                    if (dspr != null) {
                        diff.sprite = dspr;
                        Lg.Info("进入过渡：难度图 " + mark + "");
                    } else {
                        diff.gameObject.SetActive(false);
                        // 难度图是资源包里的素材，缺了交给 AssetPack 报一次（不带路径）—— 别在这里每进一次关卡就打一行路径。
                        AssetPack.NoteMissing("marks/" + mark + ".png");
                    }
                }
            } catch (System.Exception de) {
                diff.gameObject.SetActive(false);
                Lg.Ex("进入过渡：难度图加载失败（不显示）", de);
            }
        }
        // ②-c 信息区的遮罩框：框住要往中间移的那一整块（标题 + 元信息 + 成绩），尺寸 / 位置全部从文字几何推。
        // 做法与主页 / 设置页那几块同一套：半透明黑底（HomeBackdrop）+ 主题色圆角描边（MakeMaskFrame）。
        // 必须是 block 的子物体，否则框不动、内容滑出框外。
        // 圆角只能用 22（主页那档）：EnsureMaskFrameSprite 里它那档 sprite 按半径 22 铸的，混用半径角会切进遮罩。
        float infoW = EtBlockW + EtInfoPadX * 2f;
        // 框高按 EtBlockH（460）算而不是文字内容高（400）：用 EtTextContentH + 2×EtInfoPadY 会比 block 矮，
        // 标题顶与成绩底各戳出去。框高 = EtBlockH + 2×EtInfoPadY，边框绝不会切到字。
        float infoH = EtBlockH + EtInfoPadY * 2f;
        // 本地原点在 block 左侧中点 → 纵向中心要往下挪 EtBlockH/2 才是「距 block 顶多少」
        float infoTopInBlock = EtBlockH * 0.5f - infoH * 0.5f;   // = −EtInfoPadY（推导式：改一个另一个自己跟）
        // 信息框的磨砂底：裁自全屏背景模糊链最糊那一档（uvRect 裁到框的屏幕矩形），
        // 压在 InfoBackdrop 下面 —— 糊图 + 半透明黑 = 面板那 14 块遮罩同款的"模糊遮罩"。
        // 必须建在 InfoBackdrop 之前：uGUI 按同级子物体顺序画，后建的盖在上面。
        {
            var glassGo = new GameObject("InfoGlass", typeof(RectTransform));
            glassGo.transform.SetParent(block.transform, false);
            _etGlass = glassGo.AddComponent<RawImage>();
            _etGlass.raycastTarget = false;
            _etGlass.color = new Color(1f, 1f, 1f, EtGlassAlpha);
            _etGlass.texture = null;
            _etGlass.enabled = false;                 // 链子还没烘好之前一个像素都不画
            SetAtPx(_etGlass.rectTransform, -EtInfoPadX, infoTopInBlock, infoW, infoH);
        }
        var infoBg = MakeImage(block.transform, "InfoBackdrop", HomeBackdrop);
        SetAtPx(infoBg.rectTransform, -EtInfoPadX, infoTopInBlock, infoW, infoH);
        infoBg.raycastTarget = false;
        MakeMaskFrame(block.transform, "InfoFrame", -EtInfoPadX, infoTopInBlock, infoW, infoH, EtInfoFrameRadius);
        // 难度图（②-b）建在这两块遮罩之前，而 uGUI 按同级子物体的顺序画，后建的遮罩正好盖住它。
        // 提层必须写在这里：写在图那边没用，那时遮罩还没建。
        if (_etDiffImg != null) _etDiffImg.transform.SetAsLastSibling();

        var t1 = MakeTitle(block.transform, "T", 46, Color.white, TextAnchor.UpperLeft);
        SetRectPx(t1.rectTransform, 0f, 0f, 760f, 110f);
        t1.horizontalOverflow = HorizontalWrapMode.Wrap;
        t1.verticalOverflow = VerticalWrapMode.Truncate;
        t1.resizeTextForBestFit = true; t1.resizeTextMinSize = 24; t1.resizeTextMaxSize = 46;
        t1.fontStyle = FontStyle.Bold;
        t1.text = title;
        t1.raycastTarget = false;

        var t2 = MakeText(block.transform, "M", 30, new Color(1f, 1f, 1f, 0.92f), TextAnchor.UpperLeft);
        SetRectPx(t2.rectTransform, 0f, 118f, 760f, 210f);
        t2.horizontalOverflow = HorizontalWrapMode.Wrap;
        t2.verticalOverflow = VerticalWrapMode.Truncate;
        t2.resizeTextForBestFit = true; t2.resizeTextMinSize = 18; t2.resizeTextMaxSize = 28;
        t2.text = meta;
        t2.raycastTarget = false;

        // 这一屏不再自己画一张宠物图：直接用玩家自己那只真宠物（PetWanted 在过渡期间也放行），
        // 位置就是玩家设置里的位置，一个像素都不挪。台词交给它自己的气泡说（见 TickEnterTransition 开头那段）。

        // ③ 成绩行：框内内容的下方
        var t3 = MakeText(block.transform, "S", 34, new Color(1f, 0.9f, 0.6f, 1f), TextAnchor.UpperLeft);
        SetRectPx(t3.rectTransform, 0f, 340f, 760f, 60f);
        t3.horizontalOverflow = HorizontalWrapMode.Overflow;
        t3.fontStyle = FontStyle.Bold;
        t3.text = score;
        t3.raycastTarget = false;
    }

    /// <summary>装饰物加载进度条。显示「正在加载装饰物 X/Y」+ 主题色进度条（X/Y 由 IridiumLink 从待处理队列算）。
    /// 读不到总数就退化成「正在加载装饰物…」+ 空槽，至少让玩家知道在忙，不会假装满。
    /// 探不到 Iridium / 没在加载时整块隐藏；它不在 block 下，不会被框内内容右移带走。</summary>
    private static void TickEtLoadBar() {
        if (_etLoadBarRt == null) return;
        bool show = false;
        // 显示条件是「探到了 Iridium 可用」而不是 Loading：Loading 要等砖块就绪之后才为真，
        // 那样进度条前几秒是隐身的。没装 Iridium 的人 Available=false → 整块不显示，零痕迹。
        try { show = IridiumLink.Enabled && IridiumLink.Available; } catch { }
        if (_etLoadBarRt.gameObject.activeSelf != show) _etLoadBarRt.gameObject.SetActive(show);
        if (!show) return;
        try {
            bool loading = IridiumLink.Loading;
            // 优先用 Iridium 自己那行文字（IridiumLink.HasLabel，与玩家左上角看到的是同一个数）：队列
            // （还剩多少没造）会先归零，而它的文字是「已造 / 总数」，队列空了之后还在涨、总数也会变大。
            // 读不到文字才退回队列那套（换语言 / 换 UI 实现都不拦路）。
            bool hasLabel = IridiumLink.HasLabel;
            int cur = hasLabel ? IridiumLink.LabelCurrent : IridiumLink.Current;
            int tot = hasLabel ? IridiumLink.LabelTotal : IridiumLink.Total;
            bool counts = hasLabel ? tot > 0 : IridiumLink.HasCounts;
            float ratio = (counts && tot > 0) ? Mathf.Clamp01(cur / (float)tot) : 0f;
            // 收尾段 = 数字都到位了但 IsLoading 还撑着（贴图预压缩 + 图集 / 批处理收尾，实测多撑 10~30 秒）。
            // 这段不撒谎，只补「活着」的信号：填充保持满格但轻微呼吸 + 文字轮播点。
            bool tail = loading && (!counts || cur >= tot);
            if (_etLoadFill != null) {
                float w = (EtLoadBarW - 8f) * ratio;
                _etLoadFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, w), EtLoadBarH - 8f);
                float a = _etLoadFillA;
                if (tail && EtLoadTailPulse > 0f) {
                    float ph = Mathf.Sin(Time.unscaledTime * EtLoadTailPulseSpeed) * 0.5f + 0.5f;   // 0..1
                    a = _etLoadFillA * (1f - EtLoadTailPulse * ph);
                }
                if (Mathf.Abs(_etLoadFill.color.a - a) > 0.01f) {
                    var c = Accent;
                    _etLoadFill.color = new Color(c.r, c.g, c.b, a);
                }
            }
            if (_etLoadText != null) {
                // 轮播点 . → .. → ...（只在还在加载时出现，停了就没了）
                string dots = loading ? new string('.', 1 + (int)(Time.unscaledTime / EtLoadDotSeconds) % 3) : "";
                string t = counts ? $"正在加载装饰物 {cur}/{tot}{dots}"
                                  : $"正在加载装饰物{dots}";
                if (_etLoadText.text != t) _etLoadText.text = t;
            }
        } catch { }
    }

    /// <summary>信息框那块磨砂底：把背景模糊链最糊那一档按"框的屏幕矩形"裁一块贴上去。
    /// 跟面板那 14 块遮罩同一套做法（uvRect 静态裁剪），只是这里只裁一块、只取最糊那一档。
    /// 链子还没烘好 / 拿不到纹理就整块不画（下面那层半透明黑照旧撑着，不会留白）。</summary>
    private static void TickEtGlass() {
        var ri = _etGlass;
        if (ri == null) return;
        try {
            int d = Mathf.Clamp(_bgBlurDepth - 1, 0, BgBlurMaxLevels - 1);
            var tex = _bgBlurRt[d];
            if (ri.texture != tex) ri.texture = tex;
            if (tex == null) { if (ri.enabled) ri.enabled = false; return; }
            if (!ri.enabled) ri.enabled = true;
            float sw = Mathf.Max(1f, Screen.width), sh = Mathf.Max(1f, Screen.height);
            ri.rectTransform.GetWorldCorners(_etGlassCorners);
            float x0 = _etGlassCorners[0].x, y0 = _etGlassCorners[0].y;
            float x1 = _etGlassCorners[2].x, y1 = _etGlassCorners[2].y;
            if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { float t = y0; y0 = y1; y1 = t; }
            var uv = new Rect(x0 / sw, y0 / sh, (x1 - x0) / sw, (y1 - y0) / sh);
            if (ri.uvRect != uv) ri.uvRect = uv;      // 块在往右移，位置每帧都在变，这里必须跟
        } catch { }
    }

    /// <summary>每帧推进。必须放在常驻段 —— 这时面板已经关了。</summary>
    private static void TickEnterTransition() {
        if (_etGo == null) return;
        try {
            float dt = Time.unscaledDeltaTime;
            // 进场第一帧把"好游戏"那句交给玩家自己那只真宠物（这一刻它已经被 TickPet 放出来了）：
            // 用的是它自己的气泡与排版，过渡层不再自己画气泡。吐不出来（宠物关着）就算了，不影响进场。
            if (!_etSayArmed) {
                _etSayArmed = true;
                try { PetSayLine(PetPickSayLine(PetModels[PetModelForTransition()]), "进入过渡"); } catch { }
            }
            TickEtGlass();     // 信息框那块磨砂底（裁自背景模糊链）
            // 硬超时：整个过渡最多活 EtHardLimit 秒，到点无论走到哪一步都强制收尾 ——
            // 宁可少播一点动画，也绝不能把玩家扣在过渡层里。
            _etLife += dt;
            if (_etLife >= EtHardLimit) {
                Lg.Info($"[LevelLib] 进入过渡：硬超时 {EtHardLimit:F0}s（相位 {_etPhase}）→ 强制收尾");
                if (_etFile != null) { string ff = _etFile; _etFile = null; try { ConfirmOpen(ff); } catch { } }
                FinishEnterTransition();
                return;
            }
            TickEtLoadBar();   // 装饰物加载进度条（显示在画布下方）
            switch (_etPhase) {
                case 0: {   // 渐入 + 右移
                    _etT += dt;
                    float k = Mathf.Clamp01(_etT / EtInSeconds);
                    float e = 1f - (1f - k) * (1f - k);            // easeOutQuad
                    // 渐入淡的是内容层不是整层：整层含盖层，淡它会露底。
                    // 代码背景是第二块独立层，跟着同一个 k 一起淡（背景 + 内容一起渐入）。
                    if (_etContentGroup != null) _etContentGroup.alpha = k;
                    if (_etBgFade != null) _etBgFade.alpha = k;
                    if (_etBlockRt != null)
                        _etBlockRt.anchoredPosition = new Vector2(Mathf.Lerp(EtBlockFromX, EtBlockToX, e), 0f);
                    if (k >= 1f) {
                        _etPhase = 1; _etWait = 0f;
                        _etDecoGrace = 0f;   // 起跑计时从这一刻开始
                        string f = _etFile; _etFile = null;
                        Lg.Info("进入过渡：动画结束 → 开始进入谱面");
                        if (!string.IsNullOrEmpty(f)) ConfirmOpen(f);   // 真正进关卡
                    }
                    break;
                }
                case 1: {   // 等「砖块就绪 + 装饰物加载完」
                    _etWait += dt;
                    bool floors = false;
                    try {
                        var lm = scrLevelMaker.instance;
                        floors = lm != null && lm.listFloors != null && lm.listFloors.Count > 0;
                    } catch { }
                    // 砖块就绪不等于能看了：Iridium 的装饰物分帧加载在 scnGame 之后才一帧造一批，只判砖块会
                    // 让玩家看到「界面没了、装饰物还在冒」，所以还要等它报「装饰物加载完」（探不到时恒为 true）。
                    // 光等它报不够：IsLoading 在砖块就绪之后几行才置 true，那一刻去问永远答「没在加载」，
                    // 立刻淡出、进度条一次都不亮。故砖块就绪后先给一段起跑时间（EtIridiumGrace）等它起跑。
                    bool deco = true;
                    try {
                        if (!floors) {
                            _etDecoGrace = 0f;                       // 砖块还没好 → 起跑计时重来
                        } else if (!IridiumLink.Enabled || !IridiumLink.Available) {
                            deco = true;                             // 没联动 / 探不到 → 不等
                        } else if (IridiumLink.Loading) {
                            deco = IridiumLink.DecorationsReady();   // 已经在造装饰物 → 正常等它
                        } else if (_etDecoGrace < EtIridiumGrace) {
                            _etDecoGrace += dt;
                            deco = false;                            // 先别放行，看它会不会开始
                        }
                    } catch { deco = true; }
                    bool ready = floors && deco;
                    if (ready || _etWait >= EtMaxWait) {
                        _etPhase = 2; _etFade = 0f;
                        Lg.Info($"[LevelLib] 进入过渡：加载就绪（等 {_etWait:F2}s，"
                            + $"砖块={floors} 装饰物={deco} 起跑计时={_etDecoGrace:F2}s"
                            + (IridiumLink.Available ? $" Iridium进度={IridiumLink.Current}/{IridiumLink.Total}" : "")
                            + "）→ 淡出");
                    }
                    break;
                }
                case 2: {   // 慢慢消失
                    _etFade += dt;
                    float k = Mathf.Clamp01(_etFade / EtOutSeconds);
                    if (_etGroup != null) _etGroup.alpha = 1f - k;
                    if (k >= 1f) _etPhase = 3;
                    break;
                }
                case 3: {   // 收尾
                    FinishEnterTransition();
                    break;
                }
            }
        } catch (System.Exception e) {
            Lg.Ex("进入过渡 tick 异常（直接收尾）", e);
            FinishEnterTransition();
        }
    }

    /// <summary>进入过渡还在演吗（= 还没完全进入）。音乐名播报要等它收掉才说：
    /// 过渡层盖着的时候宠物整个画布是藏着的（见 PetWanted），那一刻说的话会被过期插播丢掉（见 PetSayGraceSeconds）。</summary>
    public static bool EnterTransitionActive() { return _etGo != null; }

    private static void FinishEnterTransition() {
        _etPhase = -1;
        try { if (_etGo != null) UnityEngine.Object.Destroy(_etGo); } catch { }
        _etGo = null; _etGroup = null; _etContentGroup = null; _etBlockRt = null; _etFile = null; _etLife = 0f;
        _etBgFade = null; _etGlass = null; _etSayArmed = false;
        _glassExtraWant = false;   // 过渡层没了：磨砂那条需求交回给面板自己
        _etMark = null;   // 难度标记也跟着清，下一轮进层前重新解析
        _etLoadBarRt = null; _etLoadFill = null; _etLoadText = null;   // 进度条引用一起清
        // 宠物从头到尾没被动过，这里不需要复原（留个空调用只会让人以为动过）
        Lg.Info("进入过渡：结束（过渡层已销毁、宠物已复原）");
    }
}
