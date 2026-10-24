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

public static partial class PortalUI {

  /// <summary>左侧详情区：固定区（难度 + 曲名 + 谱师/BPM/时长 + 通关数|完成度）、
  /// tab 区（通关记录 / 选择关卡 / 寄录图 / 数据管理）、底部「开始游玩」。</summary>
    private static void BuildDetailArea() {
        if (_panelCanvas == null) return;
        // 分段秒表：这一段实测 200~370ms，是开面板卡顿最大的一块，但日志里只有一个总数。
        // 先量出是哪几步在花钱，别猜着拆（只打印 >= 12ms 的段，日志不会变吵）。
        var swD = new StepWatch("详情区");
    // 详情区会被重建（开面板/刷新），白框随旧 tab 行一起销毁 → 相关引用必须清空，
    // 否则 TickDetailTabFrame 会拿着已销毁的引用、或在旧位置上残留
        _detailTabFrameRt = null;
        _detailFrameHasPos = false;
        _detailFrameSnapLeft = 2;
        _detailGeomLogged = false;
        var parent = _libRoot.transform;  // 挂在库页容器下，切 tab 时才会跟着页面一起滑走
    // 底色块（左侧详情区背景，圆角）
        // 【作者口径】糊只留左边一条小细竖条：整块登记磨砂会糊成一片，右侧还会多出一条
        // "糊 / 不糊"的粗分界线（作者说那条粗条不想要）。所以整块底色调实一点，磨砂只登记左边那条窄带。
        // 底色的基准 alpha 从 0 改成 0.55（与列表视口同档）：原来那个 0 等于这一块完全不压暗，
        // 于是磨砂的基准 alpha 拿不到数、裁片被 GlassVisibleK 按"当前 0 ÷ 基准 1"判成不可见 ——
        // 作者报"详情区还是没有模糊遮罩"就是这个。改成一档真实的暗紫底，磨砂与层次都才成立。
        var darea = MakeImage(parent, "DetailArea", new Color(0.10f, 0.08f, 0.16f, 0.55f));
        darea.sprite = RoundedSprite;
        darea.type = Image.Type.Sliced;
        _detailAreaImg = darea;                       // 那一档固定的遮罩系数要改它
        _detailAreaImgBase = new Color(0.10f, 0.08f, 0.16f, 0.55f);
        // 磨砂登记在 BuildDetailArea 之外（由 PanelBuild.Shell 在 ApplyPanelBackdropAlpha 之后调），
        // 因为基准 alpha 要等那一档刷上去才准 —— 详见那一句的说明。
        // 当年这里挂过一条 24px 的窄磨砂带，作者看到"最左边一条拖影"就撤了：那个拖影的真根因是
        // 裁片把屏幕像素当画布单位写进 sizeDelta（少除一个 rootCanvas.scaleFactor），
        // 分辨率一变就整块错位叠在原背景上。把单位换算补进 SyncGlassRect 之后重影已经没有了，
        // 所以现在整块详情区都能挂磨砂。窄带不再单独挂 —— 整块有了，窄带只会多出一条"糊/不糊"的粗分界线。
        var dart = darea.rectTransform;
    // 底边距画布底 LibBottomMargin（拉伸锚点）：1080 上就是原来的 1052，画布 1200 时会自己长到底
        SetRectPxV(darea.rectTransform, DetailLeftPx, PanelTop, DetailWPx, LibBottomMargin);
    // 定位用的是「左上角单点锚 + 竖直拉伸」，不能再设 offsetMin/Max=0（会把面板压成一个点）。
    // 整块详情区要能"往左收掉"（未选中时把位置让给列表），所以要留着引用：
    // 收起 = 左边界不动、宽度 806 → 0（往左缩），内容同步淡出，收干净后整块关掉省绘制
        _detailAreaRt = dart;
        _detailAreaGo = darea.gameObject;
        _detailGroup = darea.gameObject.AddComponent<CanvasGroup>();

    // 详情区的全部内容挂在 DetailInner 这一层下（宽度固定 806）：收起动画里详情区的宽要跟着列表
    // 左边界一起收，比例锚定的子物体每帧都要重建矩形（几十上百个图形 × 每帧），既费性能、内容还会被
    // 横向挤扁。所以动画期间直接把这一层关掉，只让背景那一张图在收。
        var innerGo = new GameObject("DetailInner", typeof(RectTransform));
        innerGo.transform.SetParent(darea.transform, false);
        var inrt = innerGo.GetComponent<RectTransform>();
        inrt.anchorMin = new Vector2(0f, 0f);
        inrt.anchorMax = new Vector2(0f, 1f);      // 左边固定、高撑满
        inrt.pivot = new Vector2(0f, 0.5f);
        inrt.sizeDelta = new Vector2(DetailWPx, 0f);   // 宽固定：永远等于"完全展开"时的宽度
        inrt.anchoredPosition = Vector2.zero;
        _detailInner = innerGo;
        var inner = innerGo.transform;

    // 固定区（不随 tab 切换）
        var fixedZone = new GameObject("FixedZone", typeof(RectTransform));
        fixedZone.transform.SetParent(inner, false);
        var fzrt = fixedZone.GetComponent<RectTransform>();
    // 顶部固定带：标题 / 信息 / 分数 / tab 四行都按固定像素从上往下排。
    // 原来是按比例锚（0.46~1 再各自取一段），面板一高一矮四行的间距就各变形一次 ——
    // 详情面板的高度是画布高度推出来的（1080 时 878），比例锚等于把字号和间距交给分辨率决定。
        fzrt.anchorMin = new Vector2(0f, 1f);
        fzrt.anchorMax = new Vector2(1f, 1f);
        fzrt.pivot = new Vector2(0.5f, 1f);
        fzrt.offsetMin = Vector2.zero;
        fzrt.offsetMax = Vector2.zero;
        fzrt.sizeDelta = new Vector2(0f, DetailFixH);   // 高固定；宽由左右拉伸决定
        fzrt.anchoredPosition = Vector2.zero;
        _detailFixedZone = fixedZone;
        swD.Mark("底板与固定带");

    // 难度图标 + 曲名 + 收藏星（横排，难度图标在前）
        var titleRow = new GameObject("TitleRow", typeof(RectTransform));
        titleRow.transform.SetParent(fixedZone.transform, false);
        var trrt = titleRow.GetComponent<RectTransform>();
    // 标题行改固定像素：左右跟四个页面共用 DetailPadInner 那条边线（原来是 4% 的比例内缩，
    // 和页面内容的 24px 内边距差 8~10px，切页时左边缘会跳一下）
        SetRectPx(trrt, DetailPadInner, 20f, DetailWPx - DetailPadInner * 2f, 64f);
        _detailMarkIcon = MakeImage(titleRow.transform, "MarkIcon", Color.white);
        _detailMarkIcon.raycastTarget = false;
        var dmrt = _detailMarkIcon.rectTransform;
        dmrt.anchorMin = dmrt.anchorMax = new Vector2(0f, 0.5f);
        dmrt.pivot = new Vector2(0f, 0.5f);
        dmrt.sizeDelta = new Vector2(52f, 52f);
        dmrt.anchoredPosition = Vector2.zero;
        _detailMarkIcon.enabled = false;
        _detailTitle = MakeTitle(titleRow.transform, "Title", 30, Color.white, TextAnchor.MiddleLeft);
        _detailTitle.fontStyle = FontStyle.Bold;
        _detailTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
        var dtrt = _detailTitle.rectTransform;
        dtrt.anchorMin = new Vector2(0f, 0f);
        dtrt.anchorMax = new Vector2(1f, 1f);
        dtrt.offsetMin = new Vector2(62f, 0f);
        dtrt.offsetMax = new Vector2(-46f, 0f);
        // 收藏星：图标族里的五角星（实心/线框两态），不再是字体字符 —— 字符的粗细随字号、形状随游戏字体，
        // 烘出来的贴图才和旁边其它图标共用同一条描边。
        _detailFavImg = IconImage(titleRow.transform, "Fav", IconShapes.Kind.Star, IconLg, IconFavOff);
        var dfrt = _detailFavImg.rectTransform;
        dfrt.anchorMin = dfrt.anchorMax = new Vector2(1f, 0.5f);
        dfrt.pivot = new Vector2(1f, 0.5f);
        dfrt.sizeDelta = new Vector2(34f, 34f);   // 可点区域仍是 34×34：比 24 的图标大一圈，好按
        dfrt.anchoredPosition = Vector2.zero;
    // 收藏星可点击：点击切换收藏 / 取消收藏。
    // IconImage 默认把 raycastTarget 关掉（图标一般不吃点击），这里必须设回 true 才能接收点击
        _detailFavImg.raycastTarget = true;
        var favBtn = _detailFavImg.gameObject.AddComponent<Button>();
        favBtn.targetGraphic = _detailFavImg;
        // 颜色过渡关掉：星的颜色表达的是"收藏了没"，只由 SetFavIcon 一个写手负责
        // （开着的话鼠标一悬停就被 uGUI 染成白色，两套写同一个图元）。
        favBtn.transition = Selectable.Transition.None;
        _detailFavBtn = favBtn;
        favBtn.onClick.AddListener(() => {
            if (_detailEntry != null) {
                bool nowFav = LevelScanner.ToggleFavorite(_detailEntry.FolderPath);
                SetFavIcon(_detailFavImg, nowFav, IconLg);
                RefreshList();   // 列表收藏区同步更新
            }
        });
        swD.Mark("标题行与收藏星");

    // 谱师/BPM/时长+格子（换行显示：曲师一行、BPM一行、时长+格子一行）
    // 字号从 30 收到 22（八档里的"正文"）：这三行是资料，标题与分数才是主角；
    // 行距放宽到 1.28，22 号三行铺在 84 高里不挤。
        _detailMeta = MakeText(fixedZone.transform, "Meta", 22, Color.white, TextAnchor.UpperLeft);
        var metart = _detailMeta.rectTransform;
        SetRectPx(metart, DetailPadInner, 92f, 520f, 84f);
        _detailMeta.horizontalOverflow = HorizontalWrapMode.Wrap;
        _detailMeta.verticalOverflow = VerticalWrapMode.Truncate;
        _detailMeta.lineSpacing = 1.28f;

    // 通关数 | 完成度：紧贴在信息块下方那一行（信息块是 92~176），左边还是同一条边线。
    // 这两个数是这一页最该被看见的东西，所以字号保持 34（八档里的"大标题"）不动。
        _detailScore = MakeText(fixedZone.transform, "Score", 34, new Color(1f, 0.9f, 0.6f, 1f), TextAnchor.UpperLeft);
        _detailScore.fontStyle = FontStyle.Bold;
        var scrt = _detailScore.rectTransform;
        SetRectPx(scrt, DetailPadInner, 184f, DetailWPx - DetailPadInner * 2f, 48f);
        _detailScore.horizontalOverflow = HorizontalWrapMode.Overflow;
        swD.Mark("资料与分数");

    // 「设为背景音乐」「自动评级」两个按钮在谱面行右键菜单里（自动评级在菜单最下面、
    // 且只在没有等级标记时出现），详情区底部只剩「开始游玩」一个按钮。

    // 详情区的备注框已删掉。「选择关卡」弹窗里那个备注输入框仍然在用数据层
    // （notes.txt / GetNote / SetNote），所以数据层必须保留。

    // tab 区（横排：去特效 / 通关记录 / 选择关卡 / 寄录图 / 数据管理）
        var tabRow = new GameObject("TabRow", typeof(RectTransform));
        tabRow.transform.SetParent(inner, false);
        var tabrt = tabRow.GetComponent<RectTransform>();
    // tab 行：分数下面留 16 的空，左右跟标题/内容同一条边线（原来 3% 比例锚 + 70px 偏移，
    // 位置是"凑"出来的，面板一高一矮就飘）
        SetRectPx(tabrt, DetailPadInner, 248f, DetailWPx - DetailPadInner * 2f, 48f);
        _detailTabRow = tabRow;
        var tabLayout = tabRow.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 6f;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = false;
        _detailTabs.Clear();
    // 「去特效」排在第一个（用户要求）；后面四个整体 +1 —— 编号变了，下面所有 _detailTab == N
        // 的判据都要跟着 +1，改漏一处就是"切到某一页显示另一页内容"。
        MakeDetailTab(tabRow.transform, "去特效", 0);
        MakeDetailTab(tabRow.transform, "通关记录", 1);
        MakeDetailTab(tabRow.transform, "选择关卡", 2);
        MakeDetailTab(tabRow.transform, "寄录图", 3);
        MakeDetailTab(tabRow.transform, "数据管理", 4);
        RefreshDetailTabs();
        swD.Mark("tab行");

    // tab 内容容器：从固定带下面 16px 开始，铺到底部按钮上方 DetailContentBottom 那条线。
    // 原来是 10%~42% 的比例锚（1080 下只有 281 高），四页在里面再各自缩一圈，
    // 真正能用到的内容盒只有 758×233 —— 这次把这块直接放到 462 高，四页一起受益。
        var contentGo = new GameObject("DetailContent", typeof(RectTransform));
        contentGo.transform.SetParent(inner, false);
        var dcrt2 = contentGo.GetComponent<RectTransform>();
        dcrt2.anchorMin = new Vector2(0f, 0f);
        dcrt2.anchorMax = new Vector2(1f, 1f);
        dcrt2.offsetMin = new Vector2(0f, DetailContentBottom);
        dcrt2.offsetMax = new Vector2(0f, -DetailContentTop);
        _detailContent = dcrt2;

    // 底部：开始游玩（固定，不随 tab 切换）
    // 改名要同步 Lang.Data.cs 的 key，否则译文对不上（跟子 tab 改名同一个道理）
        var enterBtn = MakeButton(inner, "EnterEditor", new Vector2(0f, -0f), new Vector2(260f, 64f), Accent);
        var ebrt = enterBtn.GetComponent<RectTransform>();
        ebrt.anchorMin = new Vector2(0.5f, 0.0f);
        ebrt.anchorMax = new Vector2(0.5f, 0.0f);
        ebrt.pivot = new Vector2(0.5f, 0.5f);
    // 按钮变高以后只往上长：底边 24 跟原来一致，免得往下压到详情区底
        ebrt.anchoredPosition = new Vector2(0f, 56f);
        ebrt.sizeDelta = new Vector2(260f, 64f);
        var enterText = MakeText(enterBtn.transform, "开始游玩", 30, Color.white, TextAnchor.MiddleCenter);
        Stretch(enterText.rectTransform, Vector2.zero, Vector2.one);
        enterBtn.onClick.AddListener(() => ConfirmEnterEditorDetail());
        _detailEnterBtn = enterBtn;
        swD.Mark("内容容器与开始键");

    // 通关记录内容区（tab0）
    // 「去特效」页的容器（tab 0）。它不用 buildDetailTabContent 那套壳 —— 内容是自己画的摘要页，
        // 直接挂在内容区上、尺寸由 RefreshFxTabContent 里的布局组推。
        if (_fxArea == null) {
            _fxArea = new GameObject("EffectArea", typeof(RectTransform));
            _fxArea.transform.SetParent(_detailContent, false);
            var frt = _fxArea.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 1f);
            frt.offsetMin = new Vector2(DetailPadInner, 0f);
            frt.offsetMax = new Vector2(-DetailPadInner, -DetailContentBottom + 90f);
        }
        if (_pickRecordArea == null) _pickRecordArea = buildDetailTabContent("RecordArea");
    // 选择关卡内容区（tab1）
        if (_detailFileArea == null) _detailFileArea = buildDetailTabContent("FileArea");
    // 寄录图内容区（tab2）—— 独立字段，不复用旧弹窗的 _pickHeatArea（防两处图表重叠）
        if (_detailHeatArea == null) _detailHeatArea = buildDetailTabContent("HeatArea");
    // 数据管理内容区（tab3）
        if (_pickDataArea == null) _pickDataArea = buildDetailTabContent("DataArea");
        swD.Mark("四页容器");
        RefreshDetailTabContent();
        swD.Mark("填当前页内容");

    // 未选中状态：整块详情区收掉、位置让给列表（见 ApplyLibExpand / TickLibExpand）。
    // 导入入口在 mod 设置页的"库"页里（导入单个谱面 / 批量导入谱面）。
    // 初始显隐：选中 → 展开详情区，未选中 → 收起（列表拉满）
        _libExpand = _libExpandTarget = (_selectedPath == null) ? 1f : 0f;
        ApplyLibExpand(_libExpand);
        ApplyDetailEmptyState();
        swD.Mark("收尾摆位");
    }

  /// <summary>详情区四页共用的内容内边距：四个页面都从这一条边线开始排，
  /// 切页时左边缘不会一页一个样。改这一处四页一起动。</summary>
    private const float DetailPadInner = 24f;

  /// <summary>顶部固定带（标题 20~84 / 信息 92~176 / 分数 184~232 / tab 248~296）的总高。
  /// 四行都是固定像素，改这里只影响"内容区从哪开始"。</summary>
    private const float DetailFixH = 296f;

  /// <summary>内容区上边线 = 固定带高 + 16 的空。</summary>
    private const float DetailContentTop = DetailFixH + 16f;

  /// <summary>内容区下边线：底部「开始游玩」按钮 64 高 + 底边距 24 + 留 16 的空。</summary>
    private const float DetailContentBottom = 104f;

    /// <summary>详细信息区四个 tab 的悬停底色（平时全透明）。只给一层很淡的白膜，
    /// 一个字的颜色都不碰：那几个字是跟着选中态变的，碰了就会跟选中态打架。</summary>
    private const float UiTabHoverFillA = 0.16f;

  /// <summary>建一个页容器。外面那层铺满详情区，里面那层按 DetailPadInner 内缩，
  /// 四个页面各自往里面那层建（返回的就是它），页面内部的坐标因此一个都不用改。</summary>
    private static GameObject buildDetailTabContent(string name) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_detailContent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var inner = new GameObject("Inner", typeof(RectTransform));
        inner.transform.SetParent(go.transform, false);
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = new Vector2(DetailPadInner, DetailPadInner);
        irt.offsetMax = new Vector2(-DetailPadInner, -DetailPadInner);
        return inner;
    }

    private static void MakeDetailTab(Transform parent, string label, int idx) {
    // 按钮本身无底色，选中提示由 TickDetailTabFrame 那个"滑动的框"负责
        var btn = MakeButton(parent, "DTab" + idx, Vector2.zero, new Vector2(0f, 46f), new Color(1f, 1f, 1f, 0f));
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, 46f);
        var le = btn.GetComponent<LayoutElement>();
        le.preferredHeight = 46f;
        le.minHeight = 46f;
        var t = MakeText(btn.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
        t.text = label;
        Stretch(t.rectTransform, Vector2.zero, Vector2.one);
        SetUiHoverFill(btn, UiTabHoverFillA);   // 透明底：悬停只在底色上给一层淡膜，文字不动
        btn.onClick.AddListener(() => { _detailTab = idx; RefreshDetailTabs(); RefreshDetailTabContent(); });
        _detailTabs.Add(btn);
    }

  /// <summary>详情区 tab 刷新：文字高亮 + 滑动框移动（按钮本身无底色）</summary>
    private static void RefreshDetailTabs() {
        for (int i = 0; i < _detailTabs.Count; i++) {
            if (_detailTabs[i] == null) continue;
            var img = _detailTabs[i].GetComponent<Image>();
            if (img != null) img.color = new Color(1f, 1f, 1f, 0f);  // 永远无底色
            var lb = _detailTabs[i].GetComponentInChildren<Text>();
      // 选中项用深蓝黑：它压在实心淡蓝滑块上
            if (lb != null) lb.color = i == _detailTab ? HighlightTextColor : new Color(1f, 1f, 1f, 0.62f);
        }
    // 滑动框由 TickDetailTabFrame 每帧驱动（布局完成后才有正确宽度）
        // 这里按选中态改过文字颜色 让交互态层重新记一遍"常态色"，
        // 否则悬停结束会把颜色写回登记那一刻的旧值（在几个 tab 之间移动时看着就是颜色乱翻）。
        for (int i = 0; i < _detailTabs.Count; i++) RefreshUiTargetBase(_detailTabs[i], false);   // 只重记文字

    }

  /// <summary>详情区 tab 行更新后重建白框（布局组算完宽度才有正确尺寸）</summary>
    private static bool _detailFrameHasPos;  // 白框是否已就位过（首次就位不播动画）
    private static bool _detailFrameInitialized;  // 白框是否已经至少就位过一次（未就位前保持隐藏）
    private static int _detailFrameSnapLeft;  // 还需"直接就位"的帧数（吃掉 RectTransform.rect 的滞后）
    private static string _detailFrameLastSig;  // 诊断：上一次的状态指纹（只在变化时打日志）
    private static bool _detailGeomLogged;  // 诊断：几何基准只打一次

    private static void TickDetailTabFrame() {
    // 诊断：只在状态变化时记录（默认安静；设置里打开「记录详细日志」即可复现排查）
        if (LogVerbose) {
            int selD = Mathf.Clamp(_detailTab, 0, Mathf.Max(0, _detailTabs.Count - 1));
            string sig = (_detailTabFrameRt == null ? "none" : _detailTabFrameRt.anchoredPosition.ToString("F1"))
                + "|" + _detailFrameCur.ToString("F1")
                + "|" + _detailFrameTarget.ToString("F1")
                + "|" + _detailFrameSizeCur.ToString("F1")
                + "|" + selD
                + "|" + _detailFrameHasPos
                + "|" + (_detailTabRow != null ? _detailTabRow.activeSelf.ToString() : "?");
            if (sig != _detailFrameLastSig) {
                _detailFrameLastSig = sig;
        // 几何诊断：只在「记录详细日志」打开时输出
                if (LogVerbose) {
                    string frameW = "无", tabW = "无";
                    try {
                        if (_detailTabFrameRt != null) {
                            var fc = new Vector3[4]; _detailTabFrameRt.GetWorldCorners(fc);
                            frameW = $"({fc[0].x:F0},{fc[0].y:F0})-({fc[2].x:F0},{fc[2].y:F0})";
                        }
                        if (selD < _detailTabs.Count && _detailTabs[selD] != null) {
                            var bc = new Vector3[4]; _detailTabs[selD].GetComponent<RectTransform>().GetWorldCorners(bc);
                            tabW = $"({bc[0].x:F0},{bc[0].y:F0})-({bc[2].x:F0},{bc[2].y:F0})";
                        }
                    } catch { }
                    Lg.Info($"[LevelLib] 白框变化: 渲染={(_detailTabFrameRt == null ? "无" : _detailTabFrameRt.anchoredPosition.ToString("F1"))} cur={_detailFrameCur.ToString("F1")} tgt={_detailFrameTarget.ToString("F1")} size={_detailFrameSizeCur.ToString("F1")} sel={selD} 行active={(_detailTabRow != null ? _detailTabRow.activeSelf.ToString() : "?")}");
                    Lg.Info($"[LevelLib]   白框屏幕={frameW}  按钮屏幕={tabW}");
                }
            }
        }
        if (_detailTabFrameRt == null) {
      // 白框还没建：等布局组算好宽度再建（第一帧宽度可能为 0）
            if (_detailTabRow != null && _detailTabRow.transform.childCount > 1) {
                var first = _detailTabRow.transform.GetChild(0) as RectTransform;
                if (first != null && first.rect.width > 5f) {
          // 滑块本身：尺寸、位置、外扩(padding)、动画都保持原样，不动按钮结构。
          // 它是实心块，必须画在按钮下面（下面 SetAsFirstSibling），否则会盖住 tab 文字。
                    var f = MakeImage(_detailTabRow.transform, "DetailTabFrame", HighlightFillColor);
                    RegisterFillImage(f);   // 换主题色时要重刷它（详情区 tab 选中色）
                    f.raycastTarget = false;
                    f.sprite = RoundedSprite;
                    f.type = Image.Type.Sliced;
          // 必须让布局组彻底无视这个滑块：它是 HorizontalLayoutGroup 的子节点，
          // 不加这个标记时布局组每帧都会把它当普通子项重排到最右边（用户看到"闪到右边再回来"）。
                    var le = f.gameObject.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
          // 实心块要垫在按钮下面：它是 ignoreLayout 的，移到第一个不影响按钮排布。
                    f.transform.SetAsFirstSibling();
                    _detailTabFrameRt = f.rectTransform;
          // 这一帧还没算出目标位置，先隐藏；就位后再显示
                    _detailTabFrameRt.gameObject.SetActive(false);
                    _detailFrameInitialized = false;
                    _detailFrameHasPos = false;
                    _detailFrameSnapLeft = 2;     // 连续两帧直接就位，吃掉 rect 滞后
                    if (LogVerbose) Lg.Info("白框变化: 白框刚创建（先隐藏，等就位后显示）");
                }
            }
            if (_detailTabFrameRt == null) return;
        }
    // 每帧刷新目标（tab 行布局完成或窗口变化都能跟上）
        int sel = Mathf.Clamp(_detailTab, 0, Mathf.Max(0, _detailTabs.Count - 1));
        bool targetValid = false;
        if (sel < _detailTabs.Count && _detailTabs[sel] != null) {
            var brt = _detailTabs[sel].GetComponent<RectTransform>();
            if (brt.rect.width > 5f) {
        // 锚点语义不同，直接赋值会差半个行宽：布局组会把子按钮的锚点改成"左上 (0,1)"，
        // 而这个滑块（ignoreLayout）锚点是"中心 (0.5,0.5)" —— 按钮的 anchoredPosition 相对行左缘，
        // 滑块的相对行中心。这里把按钮中心换算成「相对行中心」的偏移再交给滑块。
                var rowRt = _detailTabRow != null ? _detailTabRow.GetComponent<RectTransform>() : null;
                if (rowRt != null) {
                    Vector2 rowSize = rowRt.rect.size;
          // 按钮锚点在父矩形内的参考点 + anchoredPosition = 按钮 pivot 在父矩形内的位置
                    Vector2 btnPivot = new Vector2(
                        (brt.anchorMin.x - 0.5f) * rowSize.x + brt.anchoredPosition.x,
                        (brt.anchorMin.y - 0.5f) * rowSize.y + brt.anchoredPosition.y);
          // 白框锚点为 (0.5,0.5)、尺寸为 size → 其 anchoredPosition 就是"中心相对行中心的偏移"
                    _detailFrameTargetSize = new Vector2(brt.rect.width + DetailFramePadX * 2f, brt.rect.height + DetailFramePadY * 2f);
                    _detailFrameTarget = btnPivot;
                } else {
                    _detailFrameTarget = brt.anchoredPosition;
                    _detailFrameTargetSize = new Vector2(brt.rect.width + DetailFramePadX * 2f, brt.rect.height + DetailFramePadY * 2f);
                }
                targetValid = true;
        // 一次性基准诊断：默认安静，设置里打开「记录详细日志」才输出
                if (LogVerbose && !_detailGeomLogged) {
                    _detailGeomLogged = true;
                    var rr = _detailTabRow != null ? _detailTabRow.GetComponent<RectTransform>() : null;
                    Lg.Info($"[LevelLib] 详情tab几何: 行 rect={(rr != null ? rr.rect.ToString("F1") : "无")} pivot={(rr != null ? rr.pivot.ToString("F2") : "无")} 子数={(_detailTabRow != null ? _detailTabRow.transform.childCount : -1)}");
                    for (int ci = 0; ci < _detailTabs.Count; ci++) {
                        if (_detailTabs[ci] == null) continue;
                        var brr = _detailTabs[ci].GetComponent<RectTransform>();
                        Lg.Info($"[LevelLib]   按钮{ci}: pos={brr.anchoredPosition.ToString("F1")} rect={brr.rect.ToString("F1")} 本地X={brr.localPosition.x:F1}");
                    }
                    if (_detailTabFrameRt != null) {
                        var fr = _detailTabFrameRt;
                        Lg.Info($"[LevelLib]   白框: pos={fr.anchoredPosition.ToString("F1")} size={fr.sizeDelta.ToString("F1")} 本地X={fr.localPosition.x:F1}");
                    }
                }
            }
        }
    // 首次就位（或刚重建过）：直接就位，避免从 tab 行中心滑过去。
    // 必须连续就位两帧而不是一帧：RectTransform.rect 不是同步更新的，布局组算完之后 rect 可能要到
    // 下一帧才正确。只做一帧时第一帧会用着不准的目标，下一帧才发现目标变了、于是开始 lerp ——
    // 看起来就是"闪一下再回来"。
        if (targetValid && _detailFrameSnapLeft > 0) {
            _detailFrameSnapLeft--;
            _detailFrameHasPos = true;
            _detailFrameInitialized = true;
            _detailFrameCur = _detailFrameTarget;
            _detailFrameSizeCur = _detailFrameTargetSize;
            _detailTabFrameRt.anchoredPosition = _detailFrameCur;
            _detailTabFrameRt.sizeDelta = _detailFrameSizeCur;
            if (!_detailTabFrameRt.gameObject.activeSelf) _detailTabFrameRt.gameObject.SetActive(true);
            if (LogVerbose) Lg.Info($"[LevelLib] 白框变化: 直接就位（剩余{_detailFrameSnapLeft}） tab={sel} pos={_detailFrameCur.ToString("F1")} size={_detailFrameSizeCur.ToString("F1")}");
            return;
        }
        if (!targetValid) {
      // 量不到宽度时绝不能"什么都不做"：那样会露出布局组把滑块排到右边的位置。
      // 隐藏它，等下一帧量到宽度再显示。
            if (_detailFrameInitialized && _detailTabFrameRt.gameObject.activeSelf) {
                _detailTabFrameRt.gameObject.SetActive(false);
                if (LogVerbose) Lg.Info("白框变化: 目标无效 → 暂时隐藏（防止露出布局组位置）");
            }
            return;
        }
        float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        _detailFrameCur = Vector2.Lerp(_detailFrameCur, _detailFrameTarget, k);
        _detailFrameSizeCur = Vector2.Lerp(_detailFrameSizeCur, _detailFrameTargetSize, k);
        if ((_detailFrameCur - _detailFrameTarget).sqrMagnitude < 0.04f) _detailFrameCur = _detailFrameTarget;
        _detailTabFrameRt.anchoredPosition = _detailFrameCur;
        _detailTabFrameRt.sizeDelta = _detailFrameSizeCur;
    }

    private static void RefreshDetailTabContent() {
    // 切到「去特效」页时按当前选中的谱面重新载入它的配置（切换谱面后不能留着上一张的）
        if (_fxArea != null) _fxArea.SetActive(_detailTab == 0);
        if (_detailTab == 0 && _fxArea != null) {
            FxBeginEditLevel(_fxPendingLevel);
            RefreshFxTabContent();
        }
        if (_pickRecordArea != null) _pickRecordArea.SetActive(_detailTab == 1);
        if (_detailFileArea != null) _detailFileArea.SetActive(_detailTab == 2);
        if (_detailHeatArea != null) _detailHeatArea.SetActive(_detailTab == 3);  // 详情区独立容器
        if (_pickDataArea != null) _pickDataArea.SetActive(_detailTab == 4);
    // 通关记录 tab：选中谱面后填充记录列表（每次切到都刷新，避免丢失/残留）
        if (_detailTab == 1 && _pickRecordArea != null && _selectedPath != null) {
            var entry = LevelScanner.Entries.FirstOrDefault(x => x.FolderPath == _selectedPath);
            if (entry != null && _pickRecordDirty) { RefreshPickRecordsDetail(entry); _pickRecordDirty = false; }
        }
    // 选择关卡 tab：选中谱面后填充 .adofai 文件列表
        if (_detailTab == 2 && _detailFileArea != null && _detailFileArea.activeSelf && _selectedPath != null) {
            var entry = LevelScanner.Entries.FirstOrDefault(x => x.FolderPath == _selectedPath);
            // 判"要不要重建"不能看 childCount：滚动容器 FileScroll 是常驻子物体（见 EnsureDetailFileScroll），
            // 它一建好 childCount 就再也不会是 0，那个判据等于"只建一次"，换了谱面这页一直显示上一个谱面的文件。
            // 按"这份列表是给哪个谱面建的"来判（跟通关记录页的 _pickRecordDirty 一个思路）。
            if (entry != null && _detailFileFolder != entry.FolderPath) RefreshDetailFileList(entry);
        }
    // 寄录图 tab：构建折线图并加载选中谱面的热力图数据（用详情区独立容器）
        if (_detailTab == 3 && _detailHeatArea != null && _detailHeatArea.activeSelf && _selectedPath != null) {
            MakePickHeatArea(_detailHeatArea);   // 首次切换到 tab2 时构建图表（幂等）
            RefreshHeatMap(_selectedPath);
        } else if (_detailTab == 3 && _detailHeatArea != null && _detailHeatArea.activeSelf) {
      // 没选中谱面：把整套图表隐藏（必须连上升动画一起停，否则点线会被动画复活）+ 显示占位
            HideHeatChart(false);
            EnsureDetailPlaceholder(_detailHeatArea, "← 先选择一个谱面喵");
        }
    // 数据管理 tab：构建清空按钮
        if (_detailTab == 3 && _pickDataArea != null && _pickDataArea.activeSelf) {
      // 不能用 MakePickDataArea()：那个函数只服务关卡选择弹窗的容器，跟详情区的 _pickDataArea
      // 不是同一个东西，用错就会让详情区的"数据管理"一直是空白。这里用详情区自己的容器构建。
            MakeDetailDataArea();
        }
    }

  /// <summary>详情区「数据管理」页：清空通关记录 / 清空游玩次数 / 清空寄录图。
  /// 都只清当前选中的这个谱面，不是全部；三个按钮竖着排。</summary>
    private static void MakeDetailDataArea() {
        if (_pickDataArea == null) return;
        for (int i = _pickDataArea.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(_pickDataArea.transform.GetChild(i).gameObject);
        var box = _pickDataArea.transform;

    // 说明行：三个键都只清当前选中的这个谱面（跟确认框里那句一致），先写在最上面一行
        var cap = MakeText(box, "Cap", 17, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleLeft);
        cap.text = Lang.Tr("只清当前选中的谱面，其它谱面不受影响");
        SetRectPx(cap.rectTransform, 0f, 0f, 560f, 24f);

    // 三个清空按钮：竖排一列，左边跟上面的说明/标题对齐（原来居中一列 + 顶部空 40，
    // 在 758 宽的内容盒里既不对齐也别扭）
        string[] names = { "清空通关记录", "清空游玩次数", "清空寄录图" };
        const float BtnW = 320f, BtnH = 56f, Gap = 16f, StartTop = 36f;
        for (int i = 0; i < names.Length; i++) {
            int kind = i;
            var btn = MakeButton(box, "Clear" + i, Vector2.zero, new Vector2(BtnW, BtnH), new Color(0.55f, 0.22f, 0.22f, 1f));
            var brt = btn.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(0f, -(StartTop + i * (BtnH + Gap)));
            var bt = MakeText(btn.transform, "T", 22, Color.white, TextAnchor.MiddleCenter);
            Stretch(bt.rectTransform, Vector2.zero, Vector2.one);
            bt.text = names[i];
            btn.onClick.AddListener(() => OnDetailClearClicked(kind));
        }
    }

  /// <summary>详情区清空按钮：先确认，再按类型清空当前谱面的数据</summary>
    private static void OnDetailClearClicked(int kind) {
        var e = _detailEntry;
        if (e == null) { ShowToast(Lang.Tr("请先选中一个谱面喵")); return; }
        string title, msg;
        if (kind == 0) { title = "清空通关记录"; msg = $"确定清空「{e.Title}」的通关记录？\n（只清这个谱面，其他谱面不受影响）"; }
        else if (kind == 1) { title = "清空游玩次数"; msg = $"确定清空「{e.Title}」的游玩次数？\n（只清这个谱面，其他谱面不受影响）"; }
        else { title = "清空寄录图"; msg = $"确定清空「{e.Title}」的寄录图和完成度？\n（只清这个谱面，其他谱面不受影响）"; }
        ShowDataClearConfirm(title, msg, () => {
            try {
                if (kind == 0) { LevelScanner.ClearClearRecords(e.FolderPath); ShowToast(Lang.Tr("通关记录已清空喵")); RefreshPickRecordsDetail(e); }
                else if (kind == 1) { LevelScanner.ClearPlaysOf(e.FolderPath); ShowToast(Lang.Tr("游玩次数已清空喵")); }
                else { LevelScanner.ClearDeathMapOf(e.FolderPath); ShowToast(Lang.Tr("寄录图已清空喵")); }
                RefreshList();          // 列表里的标记/完成度可能跟着变
                RefreshDetailInfo(e);   // 详情区数字跟着刷新
            } catch (System.Exception ex) {
                ReportError("清空谱面数据失败", ex);
            }
        });
    }

  /// <summary>通关记录 tab：填充选中谱面的通关记录列表</summary>
    private static void RefreshPickRecordsDetail(LevelEntry entry) {
        if (_pickRecordArea == null) return;
    // 清空旧内容。列表壳（滚动区 + 内容盒）留着：里面的行要用来算让位的起点，
    // 正在退场的那几行也还挂在里面等着淡出，一起删掉就退不了场了。
        for (int i = _pickRecordArea.transform.childCount - 1; i >= 0; i--) {
            var oldChild = _pickRecordArea.transform.GetChild(i);
            if (_recListShell != null && oldChild == _recListShell.transform) continue;
            Object.Destroy(oldChild.gameObject);
        }
    // 判定的颜色图例（顶部说明，固定不滚）
        var head = MakeText(_pickRecordArea.transform, "Head", 22, Color.white, TextAnchor.MiddleLeft);
        head.supportRichText = true;
    // 颜色标签必须原样留着、只把六个词包进 Lang.Tr：整句写死会被含富文本的 TextTranslatePatch
    // 按设计整串跳过，这行就永远是中文；而翻译层只管文字。
        head.text = "<color=#FF4D4D>" + Lang.Tr("提前") + "</color>  <color=#E68A2E>" + Lang.Tr("太快")
                  + "</color>  <color=#FFE64D>" + Lang.Tr("稍快") + "</color>  <color=#66E666>" + Lang.Tr("完美")
                  + "</color>  <color=#FFE64D>" + Lang.Tr("稍慢") + "</color>  <color=#E68A2E>" + Lang.Tr("太慢") + "</color>";
        var hrt = head.rectTransform;
    // 左边跟下方列表对齐：两边都从内容盒自己的左边线（Inner 已经内缩了 DetailPadInner）开始，
    // 所以这里左右都是 0。
        hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.sizeDelta = new Vector2(0f, 28f);
        hrt.anchoredPosition = new Vector2(0f, 0f);
        head.horizontalOverflow = HorizontalWrapMode.Overflow;
    // 记录列表（ScrollRect 可滚动，不超出框）
    // 这一整块只建一次：删掉一条之后剩下的行要从原位滑到新位、被删掉的那一行要能自己淡出，
    // 壳跟着重建的话行会被连带销毁，这两件事都做不成（行也留不住）。
    // 底色全透明：深色底板压在详情区上显得很闷。Image 仍然保留（raycastTarget 要靠它），只是不画颜色。
        if (_recListShell == null) {
            // 壳连同行一起没了（整块画布重建）：身份表作废，别拿死行的旧位置当让位起点
            RecFlowForgetAll("rec:", _recDetailRows);
            var listView = MakeImage(_pickRecordArea.transform, "ListView", new Color(0.06f, 0.08f, 0.13f, 0f));
            var lvrt = listView.rectTransform;
    // 铺满内容盒（左右由 Inner 的 DetailPadInner 决定），顶部给图例留 36，底部贴到内容盒下沿。
    // 原来是 0.02~0.98 / 0.02~0.90 再叠 -45/-25 的 offset —— 四层数字凑出来的位置，
    // 内容盒一变高就再也对不齐；现在只剩"上边距 36"这一个数字。
            lvrt.anchorMin = new Vector2(0f, 0f);
            lvrt.anchorMax = new Vector2(1f, 1f);
            lvrt.offsetMin = new Vector2(0f, 0f); lvrt.offsetMax = new Vector2(0f, -36f);
            listView.gameObject.AddComponent<RectMask2D>();
            var scroll = listView.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = lvrt;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(listView.transform, false);
            _recListContentRt = content.GetComponent<RectTransform>();
            _recListContentRt.anchorMin = new Vector2(0f, 1f); _recListContentRt.anchorMax = new Vector2(1f, 1f);
            _recListContentRt.pivot = new Vector2(0.5f, 1f);
            _recListContentRt.sizeDelta = new Vector2(0f, 0f);
    // 行位置全手工算（RecRowY / RecListHeight）：布局组每帧会把行搬回它自己算的位置，
    // 流式让位写在行上的位置会被它盖掉。
            scroll.content = _recListContentRt;
            _recListShell = listView.gameObject;
        }
        var list = LevelScanner.GetClearRecords(entry.FolderPath);
    // 被删掉的那几条先挑出来淡出退场（要排在销毁旧行之前），剩下的旧行才交给下面销毁换新
        RecFlowRetire("rec:", _recDetailRows, list);
        for (int i = _recListContentRt.childCount - 1; i >= 0; i--) {
            var oldRow = _recListContentRt.GetChild(i);
            if (RecRowIsDying(oldRow)) continue;   // 退场中的行留给退场那段收尾
            Object.Destroy(oldRow.gameObject);
        }
        if (list.Count == 0) {
            var empty = MakeText(_pickRecordArea.transform, "Empty", 22, new Color(0.6f, 0.6f, 0.65f, 1f), TextAnchor.MiddleCenter);
            empty.text = Lang.Tr("还没有通关记录喵");
            empty.fontStyle = FontStyle.Bold;
            var ert = empty.rectTransform;
            ert.anchorMin = new Vector2(0f, 0.45f); ert.anchorMax = new Vector2(1f, 0.55f);
            ert.offsetMin = Vector2.zero; ert.offsetMax = Vector2.zero;
            _recListContentRt.sizeDelta = new Vector2(0f, 0f);
            return;
        }
    // 还留着旧身份才开让位窗口：换谱面时两份名单没有交集，整列直接落位、不做无谓的滑动
        bool animateRecords = RecFlowHasAny(_recDetailRows, list);
        RecFlowHold("rec:", _recDetailRows);
        if (animateRecords) FlowWindow("通关记录删除", MoSmall);
        for (int i = 0; i < list.Count; i++) {
            var r = list[i];
            var row = MakeImage(_recListContentRt, "Rec" + i, i % 2 == 0 ? new Color(0.13f, 0.16f, 0.24f, 0.9f) : new Color(0.1f, 0.12f, 0.19f, 0.9f));
            var rrt = row.rectTransform;
            rrt.anchorMin = new Vector2(0f, 1f); rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(-RecListPadX * 2f, RecDetailRowH);
    // 位置交给流式引擎：见过的身份从旧位滑过来，没见过的直接落位（见 PortalUI.RecordList）
            RecFlowPlace("rec:", _recDetailRows, r.Time, rrt, RecRowY(i, RecDetailRowH));
      // 行首判定模式图
            string judgeFile = null;
            if (r.Slot >= 0) {
                string judgeIcon = r.Slot == 0 ? "宽判" : r.Slot == 2 ? "严判" : "标判";
                judgeFile = MarkStore.JudgeFile.TryGetValue(judgeIcon, out var jf) ? jf : judgeIcon;
            }
            var jIcon = MakeImage(row.transform, "J", Color.white);
            jIcon.raycastTarget = false;
            var jrt = jIcon.rectTransform;
            jrt.anchorMin = jrt.anchorMax = new Vector2(0f, 0.5f);
            jrt.pivot = new Vector2(0f, 0.5f);
            jrt.sizeDelta = new Vector2(28f, 28f);
            jrt.anchoredPosition = new Vector2(6f, 0f);
            var jspr = string.IsNullOrEmpty(judgeFile) ? null : MarkStore.LoadIcon(judgeFile);
            if (jspr != null) { jIcon.sprite = jspr; jIcon.type = Image.Type.Simple; jIcon.preserveAspect = true; jIcon.enabled = true; }
            else jIcon.enabled = false;
      // 判定数量彩色文本：早期/太快/稍快/完美/稍慢/太慢（原版颜色）。
      // 行首显示的是通关倍速（音高 110 → 1.1x），非 1.0x 用橙色标出来更醒目；
      // 老记录没有音高那一列 → 不显示（不编一个假的 1.0x）。
            var t = MakeText(row.transform, "T", 22, Color.white, TextAnchor.MiddleLeft);
            t.supportRichText = true;
            t.text = "#" + (i + 1) + SpeedTag(r)
                + "  <color=#FF4D4D>" + r.Early + "</color>  <color=#E68A2E>" + r.TooFast + "</color>  <color=#FFE64D>" + r.Fast + "</color>  <color=#66E666>" + r.Perfect + "</color>  <color=#FFE64D>" + r.Slow + "</color>  <color=#E68A2E>" + r.TooSlow + "</color>"
                + "  <color=#9AA5B1>" + System.DateTimeOffset.FromUnixTimeSeconds(r.Time).LocalDateTime.ToString("yyyy-MM-dd") + "</color>";
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(0.62f, 1f);
            trt.offsetMin = new Vector2(42f, 0f); trt.offsetMax = new Vector2(0f, 0f);
      // X-acc（行尾；右边界按删除键让出的宽度退回来，展开确认面板时才不会压到百分数上）
            var xaccT = MakeText(row.transform, "X", 22, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleRight);
            xaccT.text = r.Xacc.ToString("F2") + "%";
            xaccT.fontStyle = FontStyle.Bold;
            var xrt = xaccT.rectTransform;
            xrt.anchorMin = new Vector2(0.60f, 0f); xrt.anchorMax = new Vector2(1f - RecDelReserveK, 1f);
            xrt.offsetMin = new Vector2(0f, 0f); xrt.offsetMax = new Vector2(0f, 0f);
      // 行尾删除键（就地确认）。删的是哪一条只认这一行记录自己的时间戳、不认行号：
      // 列表按 X-acc 降序排，行号跟存储顺序无关（LevelScanner.RemoveClearRecord 同一口径）。
            long recTime = r.Time;
            string recFolder = entry.FolderPath;
            MakeRecordDeleteButton(row.transform, "Del", "detail:" + recFolder,
                () => DoDeleteClearRecord(recFolder, recTime),
                () => RefreshRecordsAfterDelete(recFolder));
        }
        _recListContentRt.sizeDelta = new Vector2(0f, RecListHeight(list.Count, RecDetailRowH, RecListPadBottom));
    }

  /// <summary>「选择关卡」页的滚动列表：懒建一次（容器 + 遮罩 + 布局组），之后只换里面的行。
  /// 原来这页直接把行铺在内容盒上、没有遮罩 —— 谱面文件夹里 .adofai 一多就顶出框外，
  /// 还会盖到下面的「开始游玩」按钮上。</summary>
    private static Transform EnsureDetailFileScroll() {
        if (_detailFileContent != null) return _detailFileContent;
        var view = MakeImage(_detailFileArea.transform, "FileScroll", new Color(0.06f, 0.08f, 0.13f, 0f));
        var vrt = view.rectTransform;
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        view.gameObject.AddComponent<RectMask2D>();
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = vrt;
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = new Vector2(0f, 0f);
        var vl = content.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 4f;
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlWidth = true;
        vl.childControlHeight = false;
        vl.padding = new RectOffset(0, 0, 0, 4);
        var cf = content.AddComponent<ContentSizeFitter>();
        cf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = crt;
        _detailFileContent = content.transform;
        return _detailFileContent;
    }

  /// <summary>刷新可选择关卡 tab 的文件列表（选中谱面后）</summary>
    private static void RefreshDetailFileList(LevelEntry entry) {
        if (_detailFileArea == null) return;
        _detailFileFolder = entry.FolderPath;   // 记住这份列表是给哪个谱面建的（包括"没有 .adofai 文件"那种空列表）
        // 清空旧行。行是建在 FileScroll 里面的 Content 下的（见 EnsureDetailFileScroll），
        // 只扫 _detailFileArea 的直接子物体扫不到它们，换一个谱面就把新谱面的 .adofai 叠在上一个谱面后面，
        // 越叠越多；而且在旧行上点一下会把上一个谱面的文件当成选中项。两层都要清。
        if (_detailFileContent != null) {
            for (int i = _detailFileContent.childCount - 1; i >= 0; i--) {
                var old = _detailFileContent.GetChild(i).gameObject;
                old.SetActive(false);   // Destroy 帧末才生效，先藏起来：免得这一帧新旧两批一起参与布局
                Object.Destroy(old);
            }
        }
    // 清空上一轮留下的空提示（滚动容器本身留着）
        for (int i = _detailFileArea.transform.childCount - 1; i >= 0; i--) {
            var ch = _detailFileArea.transform.GetChild(i);
            if (ch.name == "FileScroll") continue;
            Object.Destroy(ch.gameObject);
        }
        _detailFileItems.Clear();
        _pickFiles.Clear();
        try {
            foreach (var f in Directory.GetFiles(entry.FolderPath, "*.adofai")) _pickFiles.Add(f);
        } catch { }
        _pickFiles.Sort((a, b) => {
            int r = PickFileRank(a).CompareTo(PickFileRank(b));
            return r != 0 ? r : string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
        });
        if (_pickFiles.Count == 0) {
            _detailPickedFile = null;
            var none = MakeText(_detailFileArea.transform, "None", 20, new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleCenter);
            none.text = Lang.Tr("没有 .adofai 文件");
            var nrt = none.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0.45f); nrt.anchorMax = new Vector2(1f, 0.55f);
            nrt.offsetMin = Vector2.zero; nrt.offsetMax = Vector2.zero;
            return;
        }
        _detailPickedFile = _pickFiles[0];  // 默认选中第一个
        var content = EnsureDetailFileScroll();
        for (int i = 0; i < _pickFiles.Count; i++) {
            string f = _pickFiles[i];
            int idx = i;
            var row = MakeButton(content, "File" + i, Vector2.zero, new Vector2(0f, 44f), idx == 0 ? Accent : ItemColor);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 44f; le.minHeight = 44f;
            var t = MakeText(row.transform, "T", 20, Color.white, TextAnchor.MiddleLeft);
            t.text = Path.GetFileName(f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(14f, 0f); trt.offsetMax = new Vector2(-14f, 0f);
            string ff = f;
            row.onClick.AddListener(() => {
                _detailPickedFile = ff;
                for (int j = 0; j < _detailFileItems.Count; j++)
                    SetUiSelectFill(_detailFileItems[j].GetComponent<Button>(), j == idx ? Accent : ItemColor);
            });
            _detailFileItems.Add(row.gameObject);
        }
    }

}
