// [split-of] PortalUI.PanelBuild.cs lines 612-1220 body 17-530

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

/// <summary>OpenPanel 与顶层 tab 栏 / 页面带的构建（PortalUI.PanelBuild.cs 的一部分）。</summary>
public static partial class PortalUI {
    public static void OpenPanel() {
        if (_panelCanvas != null) {
            // 画布还在 = 面板本来就开着（或正在淡出）：不再建一遍，但输入状态要当场核对一次 ——
            // 从 UMM 按钮进来时 UMM 自己的窗口在最上层、游戏的 Input 正被它读，这里漏一次就是
            // "进了 mod 界面、input map 却没切换"。
            SetGameInputEnabled(false);
            return;
        }
        _prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;       // 冻结游戏时间：主菜单的行星/镜头/背景全停，输入不乱动
        // 关掉游戏自己的两套输入队列（Rewired 键盘 + 异步键盘）：游戏收不到按键，就不会误触发"自动标记"之类的功能。
        SetGameInputEnabled(false);
        MourningUI.OnPanelOpened();   // 九一八公祭日：当天首次打开主界面弹默哀提示
        _openWatch = System.Diagnostics.Stopwatch.StartNew();   // 从最开头就开始计时（含成就扫描）
        _openLastMs = 0;
        StartPanelProfiling();   // 开始"首帧性能"采样：盯住开面板后的 90 帧（首帧卡顿的藏身处）
        // 每日成就「今天好！！」：只记住"这次开面板要触发"，不在这里弹 ——
        // 现在正是最卡的时候，等 TickPanel 判断出卡顿结束再提示。
        _panelOpenTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _lastPanelFrameTicks = _panelOpenTicks;
        _smoothFrames = 0;
        _achTodayPending = !Achievements.DailyDoneToday("ach_daily_today");
        Achievements.TryUnlock("ach_open");   // 成就：第一次打开主界面
        OpenStep("成就ach_open");
        Achievements.CheckVillageMark();   // 扫描标判/严判P17成就
        OpenStep("成就扫描CheckVillageMark");
        _panelCanvas = MakeCanvas("LevelLib_Panel", 80000);
        // 保底：新画布一律先设为"不渲染"，等这一次开面板把盖层（开始界面 / 屏保 / 个人页）都建好之后，
        // 由末尾那次 SyncPanelHiddenUnderCover() 决定要不要画。默认关着 = 任何一条路径漏判都不会闪；
        // 反过来（默认开着）漏判就会在开面板那一帧露出面板与它的背景，那一下就是玩家报的"闪"。
        _panelCanvas.enabled = false;
        _panelRenderStopped = true;
        // 整块面板一个 CanvasGroup：进来 0→1、出去 1→0
        // 只动 alpha，不碰 blocksRaycasts / interactable → 淡入过程中照样能点
        _panelGroup = _panelCanvas.gameObject.AddComponent<CanvasGroup>();
        _panelGroup.alpha = 0f;
        // 显式写一遍 true：这是新画布上的新组件，万一以后有人改成复用旧引用，
        // 就不会出现"打开是打开了、界面却点不动"这种最难查的状态
        _panelGroup.blocksRaycasts = true;
        _panelFadeT = 0f;
        _panelFadeLogged = false;   // 这一次开面板的诊断日志要重新打一遍（见 TickPanelFade）
        OpenStep("MakeCanvas");
        // 背景三层：纯色底 → 代码绘制的背景 → 半透明黑罩（动态背景就绪时中间那层整组淡掉）
        var bgImg = MakeImage(_panelCanvas.transform, "Bg", BgColor);
        Stretch(bgImg.rectTransform, Vector2.zero, Vector2.one);
        _panelBgImg = bgImg;   // 记录引用：补挂视频层与层级排序都要用它
        // 静态背景图垫在纯色底上面、代码背景下面（两层互斥：有图时神经图不显示，见 TickNetBg 那条铁律）
        _panelStaticLayer = AttachStaticBg(_panelCanvas.transform, bgImg.transform);
        _panelNetGroup = BuildNetBg(_panelCanvas.transform);
        // 视频背景：就绪后代码背景淡出、由它接管（图层顺序在建完遮罩后统一排，见 OrderPanelBgLayers）
        // 要排在 Veil 之前建，否则插入位置会跑到遮罩上面
        // 第二个实参 = 「代码背景建出来就当场让位」：只要视频这条路通着，这一屏一帧都不许露代码背景
        //（洛洛：打开界面开始背景不重建、不显示；真起不来时才由 TickVideoBg 淡回来）
        AttachVideoBg(_panelNetGroup, true);
        var veil = MakeImage(_panelCanvas.transform, "Veil", new Color(0f, 0f, 0f, Mathf.Clamp(PortalUI.BgVeilPercent, 0, 100) / 100f));
        OpenStep("背景+遮罩");
        Stretch(veil.rectTransform, Vector2.zero, Vector2.one);
        veil.raycastTarget = false;
        // 压暗层必须紧跟在背景之后（索引 1）—— 顺序 Bg(0) → Veil(1) → 各页面内容；
        // 否则它会渲染在列表/面板等内容之上，把整个界面压暗
        veil.transform.SetSiblingIndex(1);
        _panelVeilImg = veil;
        // 上一句 SetSiblingIndex(1) 会把先前插进来的几层顶上去，建完遮罩统一排一次：
        // 纯色底(0) → 代码背景(1) → 视频(2) → 遮罩(3)
        OrderPanelBgLayers(bgImg, veil);
        _veilCur = VeilTargetFor(0);
        _veilTarget = _veilCur;
        _veilAnimating = false;
        ApplyVeil();     // 打开面板时应用库页的遮罩值
        if (LogVerbose) Lg.Info($"[LevelLib] 遮罩初始化: 值={_veilCur:F2} 目标={_veilTarget:F2} 图层索引={veil.transform.GetSiblingIndex()}");

        // 三个页面并排、整条带子平移：页 i 的横向槽位固定在 [i × 屏宽, (i+1) × 屏宽]，
        // 所以切 tab 只是平移整条带子，每页自身位置永远不变。
        BuildPageBandAndNav();
        OpenStep("BuildPageBandAndNav");
        var libRoot = PageSlot(1) != null ? PageSlot(1).gameObject : null;   // 库页在槽位 1（0 是主页）
        if (libRoot == null) { Lg.Info("OpenPanel: 库页槽位创建失败"); return; }
        libRoot.name = "LibRoot";
        _libRoot = libRoot;

        // 库页展开状态必须在建任何东西之前定好：难度滑条比搜索框先建，而它的初始显隐要用 LibChromeVisible()
        // （= 未选中时藏起来），晚设会先按旧值显示一帧、闪一下。
        _libExpand = _libExpandTarget = (_selectedPath == null) ? 1f : 0f;

        // 成就入口放在右上角：锚在右上角（0.91 宽 / 0.99 高、轴心右上），跟着画布右边缘走，行宽变化也不会跑；
        // 同时避开右下方那行统计文字（谱面总数/通关数/总游玩数）和右侧字母定位条。

        // 开着「界面外显示」时，面板内这份信息框一开始就不要显示（否则会和界面外那个同时出现、闪一下两个）。
        // 这里只是初始状态，之后由每帧的 SyncPanelPlayerBox() 负责。
        bool suppressPanelBox = ShowPlayerBoxOutside;

        // 右上角：玩家信息框（灰色圆角白边半透明，左头像右名字）
        // 这个框挂在画布上（跟顶栏同一层，不随页面走），并且排在顶栏下面（不压住 tab 按钮）——
        // 挂在库页槽位下的话会跟着页面带一起横移，还会被槽位那层 RectMask2D 裁掉，切页就"锁在库页那个位置"或直接不见
        var infoBox = MakeImage(_panelCanvas.transform, "PlayerBox", PlayerBoxColor);   // 底色两份共用，见 PlayerBoxColor
        // 面板内这份后面就是我们自己的背景图/视频 它是真模糊的（挂进磨砂那张表，见 PortalUI.PanelGlass.cs）。
        // 界面外那份不登记：它后面是游戏自己的画面，我们拿不到那一帧，只能靠半透明。
        RegisterGlassPanel(infoBox);
        // 总开关关掉 → 也不显示（真正的显隐由 SyncPanelPlayerBox 每帧对齐，这里只是别闪一下）
        if (!ShowPlayerBox || suppressPanelBox) infoBox.gameObject.SetActive(false);
        infoBox.sprite = RoundedSprite;
        infoBox.type = Image.Type.Sliced;
        var infoOutline = infoBox.gameObject.AddComponent<Outline>();
        infoOutline.effectColor = new Color(1f, 1f, 1f, 0.45f);
        infoOutline.effectDistance = new Vector2(2f, -2f);
        var ibrt = infoBox.rectTransform;
        ibrt.anchorMin = ibrt.anchorMax = new Vector2(PlayerBoxX, PlayerBoxY);
        ibrt.pivot = new Vector2(1f, 1f);
        ibrt.sizeDelta = PlayerBoxSize;
        // 位置必须跟界面外那份共用同一个函数 PlayerBoxOffset()：界面外那份有"贴顶就下移 150 给 tab 栏让位"
        // 的膝盖，写死坐标会让两份落点不同（PlayerBoxY=1.12 时面板内这份会跑到屏幕外面去）
        ibrt.anchoredPosition = PlayerBoxOffset();
        if (_tabBarRt != null) ibrt.SetSiblingIndex(_tabBarRt.GetSiblingIndex());   // 页面带之上、顶栏之下
        _playerBoxRt = ibrt;
        // 右键菜单：关闭显示 / 更换头像 / 更换名字
        var pbTrigger = infoBox.gameObject.AddComponent<EventTrigger>();
        AddTrigger(pbTrigger, EventTriggerType.PointerClick, data => {
            var pe = data as PointerEventData;
            if (pe == null || pe.button != PointerEventData.InputButton.Right) return;
            OpenPlayerBoxContextMenu(pe.position);
        });
        // 内容（与界面外那份共用同一套布局：头像 + 名字 + 两行数据 + 严判等级图）
        BuildPlayerBoxContent(infoBox.transform, out _panelAvatarImg, out _panelNameText, out _panelStatText, out _panelBadgeImg);
        // 成就等级奖杯（右上角那枚，与界面外那份共用同一套写法）
        _panelAchBadgeImg = MakePlayerBoxAchBadge(infoBox.transform);
        // 头像一律走 ApplyAvatarTo（自定义 → 包里默认 → 淡色圆占位），不会留下"没有 sprite 的 Image"
        ApplyAvatarTo(_panelAvatarImg);
        RefreshPlayerBoxInfo(_panelNameText, _panelStatText, _panelBadgeImg);

        // ===== 左侧一行：滚动信息栏（"正在播放"）+ 难度滑条；搜索框在右侧列表上方 =====
        // 滚动信息栏占左侧这一行，长度让开难度滑条（给布局组留一格 NowBarSlotW）；搜索框左端对齐列表左边界，
        // 右侧那条竖向滚动条是列表滚动条，与"正在播放"的滚动栏不是一回事。
        var searchRow = new GameObject("SearchRow", typeof(RectTransform));
        searchRow.transform.SetParent(libRoot.transform, false);
        var srchRt = searchRow.GetComponent<RectTransform>();
        SetRectPx(srchRt, 62f, SearchRowTop, 806f, SearchRowH);
        var srchLayout = searchRow.AddComponent<HorizontalLayoutGroup>();
        srchLayout.spacing = 8f;
        srchLayout.childAlignment = TextAnchor.MiddleCenter;
        srchLayout.childControlHeight = false;
        srchLayout.childControlWidth = true;   // 宽度按各自的 LayoutElement 来（这样信息栏和滑条各占各的，不会挤在一起）

        // ① 给滚动信息栏占位（它自己是绝对定位的，见下面 BuildNowPlayingBar）。
        // 信息栏会被设置开关整条隐藏（SetActive(false)）：直接把它当布局子物体的话，隐藏时布局会把难度滑条挤到左边去；
        // 用一个永远存在的占位 spacer，开关它时滑条位置就不动。
        var nowSlot = new GameObject("NowBarSlot", typeof(RectTransform));
        nowSlot.transform.SetParent(searchRow.transform, false);
        var nowLe = nowSlot.AddComponent<LayoutElement>();
        nowLe.preferredWidth = NowBarSlotW; nowLe.minWidth = NowBarSlotW;
        nowLe.flexibleWidth = 0f;

        // ② 难度滑条
        var spacerL = new GameObject("SliderSpacerL", typeof(RectTransform));
        spacerL.transform.SetParent(searchRow.transform, false);
        var spL = spacerL.AddComponent<LayoutElement>();
        spL.preferredWidth = 26f;
        MakeRankSlider(searchRow.transform);
        var spacerR = new GameObject("JudgeSpacer", typeof(RectTransform));
        spacerR.transform.SetParent(searchRow.transform, false);
        var spR = spacerR.AddComponent<LayoutElement>();
        spR.flexibleWidth = 1f;

        // ③ 滚动信息栏本体（绝对定位到①占的那一格：行内水平居中 → 行高 52、条高 30 → 上边距 11）
        BuildNowPlayingBar(libRoot.transform, 62f, SearchRowTop + (SearchRowH - NowBarH) * 0.5f, NowBarSlotW, NowBarH);

        // ④ 搜索框：右侧列表上方、左端对齐列表左边界、加长
        // 它跟着列表一起左右动（未选中时左边界也拉到 62），右边界固定不变；展开进度在库页开头就定好了。
        float srchLeft = Mathf.Lerp(SearchBoxX, LibFullLeft, _libExpand);
        var searchBg = MakeImage(libRoot.transform, "SearchBg", ItemColor);
        _libSearchBgRt = searchBg.rectTransform;
        SetRectPx(searchBg.rectTransform, srchLeft, SearchBoxTop, SearchRightEdge - srchLeft, SearchBoxH);
        // 初始显隐：未选中时跟难度滑条/信息栏一起藏起来（三处共用 LibChromeVisible）——
        // 不这么写会先按默认的显示状态亮一帧再被 Tick 藏掉，看着就是"闪一下"
        searchBg.gameObject.SetActive(LibChromeVisible());
        var sInput = searchBg.gameObject.AddComponent<InputField>();
        var sText = MakeText(searchBg.transform, "SText", 24, Color.white, TextAnchor.MiddleLeft);
        Stretch(sText.rectTransform, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        sText.text = "";   // 内容由 InputField 管理
        sInput.textComponent = sText;
        var sPh = MakeText(searchBg.transform, "SPh", 24, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
        Stretch(sPh.rectTransform, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        sPh.text = Lang.Tr("搜索谱面");
        sInput.placeholder = sPh;
        sInput.text = _search;
        _libSearchInput = sInput;   // 清空筛选（ClearAllFilters）要把输入框显示一起重置
        sInput.onValueChanged.AddListener(v => {
            _search = v; _searchDirtyTimer = 0.15f;
            // 彩蛋：搜到暗号屏幕中央闪耀“月正NB”
            if (v == _easterWords[0] || v == _easterWords[1]) ShowEasterEgg();
            // 成就：月正向山倾倒（搜索月正向山倾 / 月正 触发）
            if (!string.IsNullOrEmpty(v) && (v.IndexOf("月正向山倾", System.StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("月正", System.StringComparison.OrdinalIgnoreCase) >= 0))
                Achievements.TryUnlock("ach_moon");
        });

        // 判定筛选那排按钮（宽判/标判/严判/0）已整排删除；无等级标记的谱面照常显示
        RefreshFilterButtons();

        // 搜索框右边那个「切换排序方式」的键：必须建在字母条和列表之前 ——
        // 建立时会把 _sortK 按落盘值直接就位，晚于它们建的话第一帧的列表宽度/滚动条位置还是老值，会看见列表弹一下
        BuildSortModeButton(libRoot.transform);

        // 统计行不再显示（位置让给下面的滚动栏），但 UpdateCountText() 照旧调用：
        // 总数/通关数/总游玩数的计算都还在，只是这个 Text 不激活；想再显示 SetActive(true) 即可。
        var countText = MakeText(libRoot.transform, "Count", 26, new Color(1f, 0.9f, 0.6f, 1f), TextAnchor.MiddleCenter);
        SetRectPx(countText.rectTransform, srchLeft, CountRowTop, ListRightEdge - srchLeft, CountRowH);
        _countText = countText;
        UpdateCountText();
        countText.gameObject.SetActive(false);   // 只隐藏显示，统计照常算

        // 右侧字母定位条（# 在最上，下面 A-Z）——放在列表右侧
        var letterCol = new GameObject("LetterCol", typeof(RectTransform));
        letterCol.transform.SetParent(libRoot.transform, false);
        var lc = letterCol.GetComponent<RectTransform>();
        SetRectPxV(lc, 1782f, ListTop, 110f, LibBottomMargin);
        _letterColRt = lc;   // 未选中时列表上提到 158 → 字母条跟着同顶同高（见 ApplyLibExpand）
        // 切成"按标记难度"时这一整条要渐渐隐藏：淡入淡出的载体在这里登记
        // （ApplyLetterColAlpha 是它唯一的写入点，见 SortMode 那支）
        AttachLetterColGroup(letterCol);
        // 图层顺序靠三个同级容器的建立次序决定：LetterFrameRoot（蓝块） → LetterButtons（按钮） → LetterLabels（字母文字），
        // 蓝块夹在按钮底之下、字母之上；蓝块和按钮放同一层的话它只能整层盖在按钮之上，把按钮底压暗。
        var frameRoot = new GameObject("LetterFrameRoot", typeof(RectTransform));
        frameRoot.transform.SetParent(letterCol.transform, false);
        Stretch(frameRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

        var btnHolder = new GameObject("LetterButtons", typeof(RectTransform));
        btnHolder.transform.SetParent(letterCol.transform, false);
        Stretch(btnHolder.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        var innerLayout = btnHolder.AddComponent<VerticalLayoutGroup>();
        innerLayout.spacing = 2f;
        innerLayout.childAlignment = TextAnchor.MiddleCenter;
        innerLayout.childControlWidth = false;
        innerLayout.childControlHeight = false;

        MakeLetterButton(btnHolder.transform, "#");
        for (int i = 0; i < 26; i++) MakeLetterButton(btnHolder.transform, ((char)('A' + i)).ToString());

        BuildLetterFrame(frameRoot.transform);   // 选中蓝块（第一个容器 → 画在按钮底下）
        BuildLetterLabels(letterCol.transform);  // 字母文字层（最后建 → 最上层，字母不会被蓝块挡）

        // 列表渲染根：独立 Canvas（含 viewport + 滚动条），拖动时只重建列表区域，面板静态元素不重绘
        var listRoot = new GameObject("ListRoot", typeof(RectTransform));
        listRoot.transform.SetParent(libRoot.transform, false);
        var lroot = listRoot.GetComponent<RectTransform>();
        lroot.anchorMin = Vector2.zero;
        lroot.anchorMax = Vector2.one;
        lroot.offsetMin = Vector2.zero;
        lroot.offsetMax = Vector2.zero;
        listRoot.AddComponent<Canvas>();
        listRoot.AddComponent<GraphicRaycaster>();
        _libListRootGo = listRoot;   // 启动页要把"列表整块"（含滚动条）调透明，只动列表、不动背景

        // 左侧详情区占左侧（0.03-0.44），右侧列表占居中（0.46-0.915）
        // 列表（ScrollRect）——移到右侧，右边界收到 0.915 避免和字母条重合
        var viewport = MakeImage(listRoot.transform, "Viewport", ItemColor);
        _listViewportImg = viewport;                  // 那一档固定的遮罩系数要改它
        _listViewportImgBase = ItemColorBase;         // 存"未缩放"的基准色，否则每开一次面板就被多缩一次
        RegisterGlassPanel(viewport);                 // 库区列表后遮罩 要磨砂
        // 右侧列表与左侧详情面板同顶、同高（用像素定位，避免比例算错）；左边界会动（未选中时拉到 62）。
        // 行是横向拉伸到 content 宽的，所以列表一变宽，行内的文字/bg 自动跟着重排，不用重建列表。
        // 右边界不恒定：切成"按标记难度"时整块右移补满，所以取 LibSortRightEdge()
        // （排序按钮建在前面、已经按落盘值就位，这里拿到的就是终值）。
        _listViewportRt = viewport.rectTransform;
        // 拉伸锚点：顶 ListTop 不变、底距画布底 LibBottomMargin 画布变高时视口自己长，不再空一截
        SetRectPxV(viewport.rectTransform, srchLeft, ListTop, LibSortRightEdge() - srchLeft, LibBottomMargin);
        // 定位用的是「左上角单点锚 + 竖直拉伸」，绝不能再设 offsetMin/Max=0（那会把矩形压成一个点，整个列表消失）
        viewport.gameObject.AddComponent<RectMask2D>();
        // 组件用我们自己那份（不处理拖动的 ScrollRect）：鼠标拖动统一归 TickGlobalDrag，
        // 留一份原生的在这里会和它抢着写内容位置，表现为按住间隙拖动时列表抽搐（见 NoDragScrollRect）
        var scroll = viewport.gameObject.AddComponent<NoDragScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 64f;   // 滚轮滑动距离加大
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = viewport.GetComponent<RectTransform>();   // 必须指定，否则跳转时空引用

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = new Vector2(0f, 0f);
        var vLayout = content.AddComponent<VerticalLayoutGroup>();
        vLayout.spacing = 4f;
        vLayout.childAlignment = TextAnchor.UpperCenter;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = false;
        var vFitter = content.AddComponent<ContentSizeFitter>();
        vFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = crt;
        // 换排序方式时"淡出 → 重排 → 淡入"淡的就是这一层（行都在它下面）。
        // 只淡内容，不动底板/滚动条；淡整块 listRoot 是不行的，启动页那套"列表淡回"已经占了它。
        AttachSortSwapGroup(content);

        // 右侧拖动竖条（字母条左边，可拖动快速浏览）；难度排序时它跟着列表整块右移，所以取 LibSortScrollbarX()
        var sbGo = new GameObject("Scrollbar", typeof(RectTransform));
        sbGo.transform.SetParent(listRoot.transform, false);
        var sbrt = sbGo.GetComponent<RectTransform>();
        SetRectPxV(sbrt, LibSortScrollbarX(), ListTop, ScrollbarW, LibBottomMargin);
        _libScrollbarRt = sbrt;   // 滚动条跟列表同顶同高（未选中时一起上提到 158）
        // 轨道底色是独立子物体（视觉容器）：底色画在 Scrollbar 自己身上时，Sliding Area 上下各缩 1px，
        // 手柄滑到两端差 1px 到不了头，轨道底色就从上下漏出细边；底色单独一层跟着手柄缩同一段行程就不会漏。
        var trackImg = MakeImage(sbGo.transform, "Track", new Color(0.2f, 0.17f, 0.3f, 0.5f));
        trackImg.raycastTarget = false;
        Stretch(trackImg.rectTransform, Vector2.zero, Vector2.zero);
        // 手柄的滑动区做成"把手柄本体也一起缩掉"：轨道槽 = 整条 - 手柄高度，
        // 于是手柄在最上时贴住顶端、最下时贴住底端，两端都不会留缝。
        var scrollbar = sbGo.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.transform.SetParent(sbGo.transform, false);
        var sart = slidingArea.GetComponent<RectTransform>();
        sart.anchorMin = Vector2.zero;
        sart.anchorMax = Vector2.one;
        // 四边都不缩：手柄宽度直接等于轨道宽度（左右没有可漏的东西），上下不留 1px 缝
        sart.offsetMin = Vector2.zero;
        sart.offsetMax = Vector2.zero;
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(slidingArea.transform, false);
        var hrt = handleGo.GetComponent<RectTransform>();
        hrt.sizeDelta = new Vector2(0f, 0f);
        var hImg = handleGo.AddComponent<Image>();
        // 手柄用不透明的主题色，不能用主色 Accent：那个色带 0.65 透明度（主色要铺在整行选中这种大面积上），
        // 铺在深色轨道上就发暗。
        hImg.color = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f);
        hImg.sprite = RoundedSprite;
        hImg.type = Image.Type.Sliced;
        scrollbar.handleRect = hrt;
        scrollbar.targetGraphic = hImg;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        // 虚拟化：滚动时只刷新可见行（不复用整行重建，开销极小）
        scroll.onValueChanged.AddListener(_ => UpdateVisible());
        _libScroll = scroll;   // 记住库页列表的滚动区（设置页也有 ScrollRect，不能再靠搜索）
        _libScrollbar = scrollbar;
        // 手柄长度按真实比例（视口/内容）算一次，列表内容变化时再算（UpdateScrollbarSize）
        UpdateScrollbarSize();

        // 空库状态：一行淡字 + 单个/批量导入两个按钮。
        // 显隐只切容器 _emptyLibBox 一处，里面的字和按钮一起进退；容器挂在 listRoot 下（和 viewport / 滚动条同级、
        // 在它们之后）→ 画在列表之上，而且空库时一定没得选中、列表恒为"拉满"态。
        // 整块以原来那行字为中心摆：容器顶 = 老位置 − EmptyBoxUpShift（块高 180：字 60 + 间 20 + 按钮 56 + 余量）；
        // 位置用"当前画布底 − 边距"CanvasH() 算，不写死 PanelBottom。
        float libBottomY = CanvasH() - LibBottomMargin;
        float emptyTop = ListTopFull + (libBottomY - ListTopFull) * 0.40f - EmptyBoxUpShift;
        var emptyBox = new GameObject("EmptyLibBox", typeof(RectTransform));
        emptyBox.transform.SetParent(listRoot.transform, false);
        var ebrt = emptyBox.GetComponent<RectTransform>();
        SetRectPx(ebrt, LibFullLeft, emptyTop, ListRightEdge - LibFullLeft, EmptyBoxH);
        _emptyLibBox = ebrt;

        var emptyHint = MakeText(emptyBox.transform, "EmptyLibHint", 30, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleCenter);
        SetRectPx(emptyHint.rectTransform, 0f, 0f, ListRightEdge - LibFullLeft, 60f);
        emptyHint.text = Lang.Tr("还没有谱面");
        _emptyLibHint = emptyHint;

        // 两个导入按钮：跟设置页那两行调同一套逻辑（StartSingleImportFromUi / StartBatchImportFromUi），不另写一份
        float bw = EmptyBtnW, bh = EmptyBtnH, gap = EmptyBtnGap;
        float total = bw * 2f + gap;
        float bx = (ListRightEdge - LibFullLeft - total) * 0.5f;   // 整块居中
        float by = 60f + 20f;                                      // 字下面 20px
        var eb1 = HomeButton(ebrt, "EmptyImportOne", bx, by, bw, bh, Accent, "单个导入", 26, out _emptyImportOneText);
        eb1.onClick.AddListener(StartSingleImportFromUi);
        var eb2 = HomeButton(ebrt, "EmptyImportBatch", bx + bw + gap, by, bw, bh, Accent, "批量导入", 26, out _emptyImportBatchText);
        eb2.onClick.AddListener(StartBatchImportFromUi);

        RefreshEmptyLibHint();

        // 库页静态元素建完了 → 按当前展开进度整体摆一次（未选中时列表就该在 62/158）：
        // 构建期用的是"选中态"的写死坐标，不摆这一下第一帧会看到列表在 899/222 然后跳过去
        ApplyLibExpand(_libExpand);

        // 彩蛋文本：屏幕中央“月正NB关注月正谢谢喵喵喵”（搜索暗号触发，快速 RGB 闪耀 8 秒）
        var easter = MakeText(libRoot.transform, "Easter", 190, Color.white, TextAnchor.MiddleCenter);
        easter.fontStyle = FontStyle.Bold;
        easter.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        var ert = easter.rectTransform;
        ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
        ert.pivot = new Vector2(0.5f, 0.5f);
        ert.sizeDelta = new Vector2(1300f, 520f);
        ert.anchoredPosition = Vector2.zero;
        easter.gameObject.SetActive(false);
        _easterText = easter;

        // 确认/取消行已移除：选中谱面后改为弹窗选择 .adofai 文件（见 ShowLevelPickDialog）

        _panelOpen = true;
        // 记下开面板那一刻的场景名，看门狗 TickPanelTakeoverWatch 靠它判断"游戏自己进了关卡"
        _panelSceneAtOpen = CurrentSceneName();
        // 同一时刻的"在不在打谱"：用来区分"进来之后才开始玩"（要收摊）和"本来就在玩、玩家自己按 UMM 开的面板"
        // （那是正常用法，一脚都不许碰）
        _panelPlayingAtOpen = IsLevelPlaying();
        // 详情区构建实测要 370ms 左右，是开面板卡顿的最大一块。它只在左侧那一片，晚一帧建完全看不出来：
        // 面板本身立刻出现（背景+列表先可见），详情区紧接着补上。
        _detailBuildPending = true;
        ApplyPanelBackdropAlpha();   // 应用遮罩固定那一档（列表底色；详情区在它建好后应用）
        BuildKeyHintBar();   // 底部快捷键提示行
        MaybeShowBirthday();   // 作者生日祝福（1/13）
        MaybeShowWeeklyReport();   // 周报（周一触发）
        // 背景音乐加载（读+解码整个 mp3）实测要 750ms 左右，玩家看不见这段，晚一帧开始播完全没区别
        _musicStartPending = true;
        OpenStep("音乐延后");
        // 恢复上次浏览位置：先设置滚动位置，再刷新列表（否则池行先按旧位置渲染、下一帧才跳过去，看起来"列表飞一下"）
        var restoreScroll = _libScroll;
        if (restoreScroll != null && restoreScroll.content != null) {
            Canvas.ForceUpdateCanvases();
            float maxY = Mathf.Max(0f, restoreScroll.content.rect.height - restoreScroll.viewport.rect.height);
            restoreScroll.content.anchoredPosition = new Vector2(restoreScroll.content.anchoredPosition.x, Mathf.Clamp(_savedScrollY, 0f, maxY));
            if (LogVerbose) Lg.Info($"[LevelLib] 滚动位置已恢复: savedY={_savedScrollY:F0} maxY={maxY:F0} 实际={restoreScroll.content.anchoredPosition.y:F0}");
        }
        RefreshList();
        // 显式置顶，两种口径都写一遍：ScrollRect 的初始值在不同口径下可能停在底部
        // （normalizedPosition 0 = 底 / content.y 0 = 顶）；只有"本局要沿用上次位置"时才跳过。
        bool keepScroll = false;
        try { keepScroll = _libScrollRestore > 0.0005f && _libScrollRestore < 0.9995f; } catch { }
        if (!keepScroll && _libScroll != null) {
            try {
                _libScroll.verticalNormalizedPosition = 1f;
                if (_libScroll.content != null)
                    _libScroll.content.anchoredPosition = new Vector2(_libScroll.content.anchoredPosition.x, 0f);
                _savedScrollY = 0f;
                // 一次置顶不够：开面板后详情区展开动画(0.22s)会改视口高度，内容高度/钳制位置跟着重算，列表会自己往下走。
                // 数帧也不行 —— 置顶那一帧内容还没长起来（maxY=0），之后内容分帧长高，而 verticalNormalizedPosition
                // 默认 0 = 底部。所以一直钉到玩家自己滚动/拖拽为止（内容长多少帧就钉多少帧）。
                _libPinTop = true;
                _libPinT = 0f;
                _libPinHeld = false;   // 别把上一次的"按住"带进来
            } catch { }
        }
        OpenStep("RefreshList");
        // 主页必须建在 ShowSplashIfFirstTime() 之前：启动页那里会给"列表/主页/tab 栏"挂 CanvasGroup 设 0（透明），
        // 建晚了就漏在启动页后面透出来
        try { BuildHomePage(); _topTabBuilt = 0; } catch (System.Exception eHome) { ReportError("构建主页失败", eHome); }
        // 启动欢迎页：本局第一次打开谱面库时显示；放在最后，面板已经建好，点掉欢迎页看到的就是完整的库界面
        ShowSplashIfFirstTime();
        OpenStep("启动欢迎页");
        // RestoreUiState() 不能放在这里：下面那段"顶层 tab 初始状态"会把 _topTab 硬写成 0（官谱库）、
        // 把带子摆回 x=0，刚刚沿用回来的 tab 当场被冲掉。必须排在那段之后（见函数末尾的 RestoreUiState()）。
        // 另外沿用"上次选中"要等详情区建好（它是开面板后一帧才建的），见 ApplyPendingRestoreSelection。
        // 列表整片空白时记一行"为什么空"，下次一眼定性（筛空本来就有出口：列表里那行"点这里清空筛选条件"）
        try {
            if (_display == null || _display.Count == 0) {
                Lg.Info("【列表空白诊断】条目="
                    + (LevelScanner.Entries != null ? LevelScanner.Entries.Count : -1)
                    + " 显示=" + (_display != null ? _display.Count : -1)
                    + " 顶层tab=" + _topTab + "（筛空有出口：列表里那行可点）");
            }
        } catch { }
        // 本局第一次开面板时先停在主界面（启动页/个人页放完之后露出来的就是它），之后再开面板沿用记住的 tab。
        // 主界面不在这里显：改到"个人页渐隐完"那一刻（见 ProfileScreen.Finish），
        // 这样滑动→登录这一路底下一直是库界面，不会先闪一下主界面。
        // 顶层 tab 初始状态：主页（tab 0）定位在 0（首帧不做滑动）
        // 主页已经在上面（ShowSplashIfFirstTime 之前）建好了：RebuildPage 只在"切 tab 的动画"里被调，
        // 开面板时 _topAnimT 已经是 1 → 谁都不会去建它 → 玩家看到的会是空白主页
        _topTab = 0;
        _topTabBuilt = 0;
        _topAnimT = 1f;
        _topAnimDur = 0.34f;
        _pendingTopTab = -1;
        if (_contentRoot != null) {
            _topToX = _topFromX = 0f;
            _contentRoot.anchoredPosition = Vector2.zero;
        }
        try { BuildHomePage(); _topTabBuilt = 0; } catch (System.Exception eHome) { ReportError("构建主页失败", eHome); }
        RefreshTopNav();
        // RestoreUiState() 是开面板的最后一步：必须排在上面那段"初始停官谱库 + 带子归零"之后，
        // 否则沿用回来的 tab 会被它冲掉
        RestoreUiState();
        // 这里不调用 Canvas.ForceUpdateCanvases()：RefreshList() 内部已经强制更新过一次布局，
        // 紧接着重复调用等于把整棵 UI 树重新布局两遍（开面板那一下的主要开销之一）；
        // 位置对齐由 Tick 每帧驱动，本来就不依赖这里。
        // 调试：mod 目录放 debug_guides.txt 就画出布局参考线（排查对齐问题），删掉文件即恢复
        try {
            if (System.IO.File.Exists(System.IO.Path.Combine(Main.ModEntry.Path, "debug_guides.txt"))) AddLayoutGuides();
        } catch { }
        // 开面板的最后一步：把个人页整页建好藏起来 + 把界面里所有字×字号烘进字形图集。
        // 这几个卡点全是"现建 / 现生成"，卡的是玩家正看着的那一下，所以全挪到这儿来 ——
        // 此刻游戏自己正在解 scnEditor（8~9 秒级的帧），这点开销感觉不到。详见 PortalUI.Preload.cs
        PreloadAllScreens();
        // 同一帧就把"面板要不要画"定下来：盖层（开始界面 / 屏保 / 个人页）已经在场就一帧都不许画。
        // 留到下一帧的 TickCore 再判的话，开面板那一帧会先把面板（连着它的背景与大厅）画出来 —— 那一下就是"闪"。
        SyncPanelHiddenUnderCover();
        // 顺带把"背景是不是又重建了"记一行：贴图与大厅拓扑图都是跨面板复用的静态缓存，这行本该永远说"命中"。
        LogNetBgCacheState();
        NetFlashProbeArm();   // 「闪线」逐帧探针（默认关：mod 目录放 debug_netflash.txt 才开，见 PortalUI.NetBg.cs）
        if (_openWatch != null) {
            // 开面板一次性开销：无条件记一行（这是玩家能直接感觉到的卡顿，值得常驻观测）
            Lg.Info($"[LevelLib] 【开面板】总计 {_openWatch.ElapsedMilliseconds}ms");
            _openWatch = null;
        }
    }

    /// <summary>页面带 + 顶部导航排：建立固定 RefW × PageSlotCount 宽的内容带（每页一格、独立裁剪视口），
    /// 再建右上角那排导航小方块。导航挂在面板画布上（不随页面滑动），页面带只做横向平移。</summary>
    private static void BuildPageBandAndNav() {
        // ===== 内容带：固定宽 RefW × 槽位数，槽位并排（页 i 左边 = i × RefW）=====
        var contentRoot = new GameObject("TabContentRoot", typeof(RectTransform));
        contentRoot.transform.SetParent(_panelCanvas.transform, false);
        _contentRoot = contentRoot.GetComponent<RectTransform>();
        // 页面带宽度 = 槽位宽 × 槽位数（横向要滑动，必须定宽），高度改成铺满画布（竖直拉伸锚点）：
        // 写死 RefH 的话画布更高时整条带不够高，页面里所有"到底"的东西下方都会空出一截
        SetRectPxV(_contentRoot, 0f, 0f, RefW * PageSlotCount, 0f);
        for (int i = 0; i < PageSlotCount; i++) {
            var page = new GameObject("PageSlot" + i, typeof(RectTransform));
            page.transform.SetParent(contentRoot.transform, false);
            // 槽位：0 = 主页（官方关卡棋盘）· 1 = 库页 · 2 = 成就页 ·
            // 3 = 空着（设置页独立挂在画布上，视觉上占这一格）· 4 = 开发者页 · 5 = 主界面（大厅）
            // 槽位高度也跟着画布（拉伸）—— 里面的 PageView / PageRect 本来就是 Stretch
            SetRectPxV(page.GetComponent<RectTransform>(), i * RefW, 0f, RefW, 0f);
            _pageSlots[i] = page.GetComponent<RectTransform>();
        }
        if (_libScroll != null) _libScroll = null;   // 面板重建，旧引用作废

        // 页面视口必须按槽位数（PageSlotCount）建，不能按"导航排有几个按钮"建：
        // 按钮只有 5 个而槽位有 6 个，MainScreenSlot 那个槽位会没有 PageRect，
        // 而 PageSlot(i) 返回的正是 _pageRects[i] —— 主界面拿到 null 就直接放弃（槽位本身与切页都是好的）
        for (int i = 0; i < PageSlotCount; i++) {
            var page = _pageSlots[i];
            var viewport = MakeImage(page.transform, "PageView", new Color(0f, 0f, 0f, 0f));
            viewport.sprite = null; viewport.type = Image.Type.Simple;
            viewport.raycastTarget = false;
            Stretch(viewport.rectTransform, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            var pageRect = new GameObject("PageRect", typeof(RectTransform));
            pageRect.transform.SetParent(viewport.transform, false);
            _pageRects[i] = pageRect.GetComponent<RectTransform>();
            Stretch(_pageRects[i], Vector2.zero, Vector2.one);
        }

        // ===== 顶部导航排（右上角五块斜切小方块，不随页面滑动）=====
        BuildTopNav(_panelCanvas.transform);
    }
}
