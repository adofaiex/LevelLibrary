using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：顶部导航排（"圆角胶囊 + 液桥"那套）。
/// 形状与让位逻辑完全照 shadcn registry 里那份 Gooey Nav 的源码来：
/// · 五个入口拼成一整条圆角胶囊（每块四角都圆）；
/// · 缝 = 第 i 块之前那条空隙（第 0 条与第 N 条恒开，整条胶囊的两端永远是圆的）；
///   被选中那一块的前后两条缝让开 NavGooGap，其余缝贴死（NavGooClosed 为负值，相邻两块叠 1px、不留发丝缝）；
/// · 让位走流式布局：缝宽一动画，后面的块与标签整体跟着挪（滑块不会横穿别人的标签）；
/// · 缝刚拉开/快合上的那一小段里连着一条"液桥"（上下两条凹曲线收成细腰的形状），拉满即断；
/// · 跟随是指数平滑（对应它那个 damped spring，不过冲）。
/// 没有 shader / 没有滤镜，液桥是一张预烘的形状图，按空隙宽度淡出来近似"腰越来越细"。
/// 它是面板画布直属子物体、不随页面带走；停在大厅（没有对应入口）时整条胶囊是一块整的、都不选中。</summary>
public static partial class PortalUI {

    /// <summary>五个入口的两字标签与槽位号。完整名字（官谱库 / 谱面库 / …）留在大厅的大按钮上，
    /// 这里只求一眼扫得完；槽位号与 TopTabName / PageSlot 是同一套编号。</summary>
    private static readonly string[] NavLabels = { "官谱", "谱面", "成就", "设置", "开发" };
    private static readonly int[] NavSlots = { 0, 1, 2, 3, 4 };

    /// <summary>缝的数量 = 块数 + 1（第 0 条在最左边、第 N 条在最右边，两条恒开）</summary>
    private const int NavSeams = 6;
    /// <summary>被选中那块前后两条缝让开多少（画布像素）：让出来的这一圈就是液桥活动的地方</summary>
    private const float NavGooGap = 20f;
    /// <summary>贴死时相邻两块重叠多少（负的空隙）：不留一条头发丝似的缝</summary>
    private const float NavGooClosed = -1f;
    /// <summary>空隙拉开到这个比例之前液桥还在（再拉宽腰就细到看不见了）</summary>
    private const float NavGooNeckK = 0.62f;
    /// <summary>让位的跟随速度（指数平滑，1/s）：约 0.3 秒到位，无过冲</summary>
    private const float NavGooFollow = 9f;
    /// <summary>胶囊的高度</summary>
    private const float NavBarH = 50f;

    private static RectTransform _navRoot;
    private static readonly RectTransform[] _navRects = new RectTransform[5];   // 命中区（跟着流式布局走）
    private static readonly Image[] _navHit = new Image[5];
    private static readonly Text[] _navLabel = new Text[5];
    /// <summary>胶囊的三块：没选中时用 Solo；有选中时左组一块 + 选中那块 + 右组一块</summary>
    private static Image _navSolo, _navGroupL, _navTile, _navGroupR;
    /// <summary>两条液桥（选中块前后各一条）</summary>
    private static Image _navNeckL, _navNeckR;
    /// <summary>每条缝当前的宽度（下标 = 第 i 块之前那条；第 0 条恒为 0）</summary>
    private static readonly float[] _navGap = new float[NavSeams];
    /// <summary>五块当前的 x（流式布局算出来的；标签与命中区都按它摆）</summary>
    private static readonly float[] _navX = new float[5];
    private static bool _navInited;

    /// <summary>建顶部导航排。挂在面板画布上（不随页面带走），顺带把 `_topTabRoot` / `_tabBarRt`
    /// 指到它 —— 那两处引用一个是"设置页打开时把它提到最上层"，一个是"信息栏的层级参照 + 启动页淡入组"。</summary>
    private static void BuildTopNav(Transform parent) {
        var go = new GameObject("TopNav", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        _navRoot = go.GetComponent<RectTransform>();
        _topTabRoot = go;
        _tabBarRt = _navRoot;
        // 整条画布宽、只要 TopNavH 高：纯粹为了"里面的坐标就等于画布坐标"，本身没有图、不吃射线
        SetAtPx(_navRoot, 0f, 0f, RefW, TopNavH);
        EnsureNavSprites();

        float rowW = NavLabels.Length * TopNavBtnW;
        float rowLeft = RefW - TopNavRight - rowW;
        // 画的顺序：整条胶囊 → 左组 → 选中块 → 右组 → 两条液桥；标签最后建，永远压在色块上面
        _navSolo = MakeNavPiece("NavBarSolo", rowLeft);
        _navGroupL = MakeNavPiece("NavBarL", rowLeft);
        _navTile = MakeNavPiece("NavTile", rowLeft);
        _navGroupR = MakeNavPiece("NavBarR", rowLeft);
        _navNeckL = MakeNavNeck("NavNeckL");
        _navNeckR = MakeNavNeck("NavNeckR");

        for (int i = 0; i < NavLabels.Length; i++) {
            int idx = i;
            float x = rowLeft + i * TopNavBtnW;

            var hit = MakeImage(_navRoot, "NavHit" + i, new Color(1f, 1f, 1f, 0f));
            hit.raycastTarget = true;
            SetAtPx(hit.rectTransform, x, TopNavTop, TopNavBtnW, NavBarH);
            _navRects[i] = hit.rectTransform;
            _navHit[i] = hit;

            var label = MakeText(_navRoot, "NavLabel" + i, 26, NavIdleText, TextAnchor.MiddleCenter);
            label.text = Lang.Tr(NavLabels[i]);
            SetAtPx(label.rectTransform, x, TopNavTop, TopNavBtnW, NavBarH);
            _navLabel[i] = label;

            var btn = hit.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = label;      // 提亮打在文字上：胶囊与选中块的配色由 RefreshTopNav 统一写
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = Color.white;   // 悬停自己写色（LayoutTopNav 里那行），乘算在这几个浅色字上看不出来
            cb.pressedColor = SlabPressMul;
            cb.selectedColor = Color.white;
            cb.disabledColor = Color.white;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.10f;
            btn.colors = cb;
            btn.onClick.AddListener(() => { DeselectUi(); SwitchTopTab(idx); });
        }
        _navInited = false;    // 首帧直接就位，别从"全贴死"荡过去
        RefreshTopNav();
    }

    private static Image MakeNavPiece(string name, float x) {
        var img = MakeImage(_navRoot, name, SlabFill);
        img.sprite = NavPill != null ? NavPill : RoundedSprite;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        SetAtPx(img.rectTransform, x, TopNavTop, 0f, NavBarH);
        return img;
    }

    private static Image MakeNavNeck(string name) {
        var img = MakeImage(_navRoot, name, SlabFill);
        img.sprite = NavNeck != null ? NavNeck : RoundedSprite;
        img.type = Image.Type.Simple;      // 形状就是这张图本身，别九宫格切
        img.raycastTarget = false;
        img.enabled = false;
        SetAtPx(img.rectTransform, 0f, TopNavTop, 0f, NavBarH);
        return img;
    }

    /// <summary>导航排的刷新：推进让位动画、按流式布局摆块与标签、写选中态颜色。
    /// 切页时、以及切页动画的每一帧都会调（TickTopTabs 末尾那一次），所以动画就放在这里，不必再挂一个 Tick。</summary>
    private static void RefreshTopNav() {
        if (!NavBuilt) return;   // 面板没建（关着）时直接早退：换主题色那条路也会调到这里

        // 停在大厅时整条导航藏起来（用户要求：切换按键只在别的界面显示）。
        // 大厅是整屏页、自带完整的页面观感，右上角再挂一条胶囊会压在它的日期行上。
        bool navOn = _topTab != MainScreenSlot;
        if (_navRoot.gameObject.activeSelf != navOn) _navRoot.gameObject.SetActive(navOn);
        if (!navOn) return;     // 藏着就不用再摆块与标签了

        // 目标：当前停在哪一页，哪一块脱出来；停在大厅（没有对应入口）就是"没有选中"
        int target = -1;
        for (int i = 0; i < NavSlots.Length; i++) if (_topTab == NavSlots[i]) { target = i; break; }

        if (!_navInited) {
            for (int s = 0; s < NavSeams; s++) _navGap[s] = WantGap(s, target);
            _navInited = true;
        } else {
            float k = 1f - Mathf.Exp(-NavGooFollow * Time.unscaledDeltaTime);
            if (k < 0f) k = 0f;
            if (k > 1f) k = 1f;
            for (int s = 0; s < NavSeams; s++) {
                float want = WantGap(s, target);
                _navGap[s] += (want - _navGap[s]) * k;
                if (Mathf.Abs(want - _navGap[s]) < 0.01f) _navGap[s] = want;
            }
        }
        LayoutTopNav(target);
    }

    /// <summary>第 s 条缝（= 第 s 块之前那条）该有多宽。与那套 Gooey Nav 的 open()/gap() 逐条对齐：
    /// 最左与最右两条恒开（胶囊两端永远圆）、被选中那块前后两条开、其余贴死；
    /// 另外"第 0 块之前那条缝"在那边固定给 0（左边没有块挨着它）。</summary>
    private static float WantGap(int seam, int active) {
        if (seam <= 0) return 0f;
        bool open = (seam == NavLabels.Length) || (active >= 0 && (seam - 1 == active || seam == active));
        return open ? NavGooGap : NavGooClosed;
    }

    /// <summary>流式布局 + 上色。整条胶囊右对齐：总宽 = 五块 + 各条缝（缝可以为负 = 重叠 1px）。
    /// 每处都"变了才写"（这个函数每帧都会被调到）。</summary>
    private static void LayoutTopNav(int active) {
        float slotW = TopNavBtnW;
        float total = NavLabels.Length * slotW;
        for (int s = 1; s < NavSeams; s++) total += _navGap[s];
        float x = RefW - TopNavRight - total;
        for (int i = 0; i < NavLabels.Length; i++) {
            _navX[i] = x;
            x += slotW;
            if (i + 1 < NavSeams) x += _navGap[i + 1];   // 第 i+1 条缝 = 第 i+1 块之前那条
        }

        // 标签与命中区跟着块走（让位时邻居一起挪，滑块才不会横穿别人的标签）
        bool blocked = false;
        try { blocked = PanelInputBlocked(); } catch { }
        for (int i = 0; i < NavLabels.Length; i++) {
            var label = _navLabel[i];
            if (label != null) {
                var rt = label.rectTransform;
                var want = new Vector2(_navX[i], -TopNavTop);
                if (rt.anchoredPosition != want) rt.anchoredPosition = want;
                bool sel = (active == i);
                // 悬停：选中那一格不参与（它已经是主题色实心块 + 深色字，再提亮就看不清了）
                bool hot = !sel && !blocked && MouseInRect(_navRects[i]);
                var wantCol = sel ? HighlightTextColor : (hot ? NavHoverText : NavIdleText);
                if (label.color != wantCol) label.color = wantCol;
            }
            var hit = _navRects[i];
            if (hit != null) {
                var want = new Vector2(_navX[i], -TopNavTop);
                if (hit.anchoredPosition != want) hit.anchoredPosition = want;
            }
        }

        // 色块：没选中就是整条胶囊一块；有选中就是左组 + 选中块 + 右组（四角都圆，朝缝隙那一侧也圆）
        if (active < 0) {
            // 一整条只铺到"最后一块的右沿"为止。最右那条缝恒开、是留给选中块让位的空档：
            // 它要算进流式布局的总宽（整排从哪里起由它定），但不能算进胶囊本身 ——
            // 算进去的话没选中时右端会比选中时多伸出去一截，切页那一瞬间看着像猛地缩了一下。
            SetNavPiece(_navSolo, _navX[0], _navX[NavLabels.Length - 1] + slotW - _navX[0]);
            SetNavPiece(_navGroupL, _navX[0], 0f);
            SetNavPiece(_navTile, _navX[0], 0f);
            SetNavPiece(_navGroupR, _navX[0], 0f);
            SetNavNeck(_navNeckL, 0f, 0f);
            SetNavNeck(_navNeckR, 0f, 0f);
        } else {
            float tileL = _navX[active];
            float tileR = tileL + slotW;
            float gapL = _navGap[active];                  // 选中块之前那条
            float gapR = _navGap[active + 1];              // 选中块之后那条（下标最大 5，永远在范围内）
            float openL = Mathf.Max(0f, gapL), openR = Mathf.Max(0f, gapR);
            SetNavPiece(_navSolo, _navX[0], 0f);
            SetNavPiece(_navGroupL, _navX[0], Mathf.Max(0f, tileL - openL - _navX[0]));
            SetNavPiece(_navTile, tileL, slotW);
            SetNavPiece(_navGroupR, tileR + openR,
                        Mathf.Max(0f, _navX[NavLabels.Length - 1] + slotW - (tileR + openR)));
            SetNavNeck(_navNeckL, tileL - openL, gapL);
            SetNavNeck(_navNeckR, tileR, gapR);
        }

        // 胶囊底与选中块的颜色（换主题色时跟着变）
        var barCol = SlabFill;
        if (_navSolo != null && _navSolo.color != barCol) _navSolo.color = barCol;
        if (_navGroupL != null && _navGroupL.color != barCol) _navGroupL.color = barCol;
        if (_navGroupR != null && _navGroupR.color != barCol) _navGroupR.color = barCol;
        var tileCol = HighlightFillColor;
        if (_navTile != null && _navTile.color != tileCol) _navTile.color = tileCol;
    }

    /// <summary>一条液桥：宽度就是当前空隙（可以为负 = 贴死，那时不画）。空隙越大腰越细，用淡出来近似
    /// （一张预烘的形状图没有可变腰），超过可见窗口整个关掉。</summary>
    private static void SetNavNeck(Image neck, float x, float width) {
        if (neck == null) return;
        bool on = width > 0.05f && width <= NavGooGap * NavGooNeckK;
        if (neck.enabled != on) neck.enabled = on;
        if (!on) return;
        var rt = neck.rectTransform;
        var wantPos = new Vector2(x, -TopNavTop);
        if (rt.anchoredPosition != wantPos) rt.anchoredPosition = wantPos;
        var wantSize = new Vector2(width, NavBarH);
        if (rt.sizeDelta != wantSize) rt.sizeDelta = wantSize;
        float a = 1f - Mathf.Clamp01(width / (NavGooGap * NavGooNeckK));
        var want = new Color(SlabFill.r, SlabFill.g, SlabFill.b, SlabFill.a * a);
        if (neck.color != want) neck.color = want;
    }

    private static void SetNavPiece(Image piece, float x, float w) {
        if (piece == null) return;
        bool on = w > 0.5f;
        if (piece.enabled != on) piece.enabled = on;
        if (!on) return;
        var rt = piece.rectTransform;
        var wantPos = new Vector2(x, -TopNavTop);
        if (rt.anchoredPosition != wantPos) rt.anchoredPosition = wantPos;
        var wantSize = new Vector2(w, NavBarH);
        if (rt.sizeDelta != wantSize) rt.sizeDelta = wantSize;
    }

    /// <summary>导航排建过没有（自检用；没建时一切刷新都要早退）</summary>
    private static bool NavBuilt { get { return _navRoot != null && _navHit[0] != null; } }

    /// <summary>面板销毁时清引用：不清的话下次开面板会去刷已经销毁的对象（这一族的老账）。</summary>
    private static void ClearNavRefs() {
        _navRoot = null;
        _navSolo = _navGroupL = _navTile = _navGroupR = null;
        _navNeckL = _navNeckR = null;
        _navInited = false;
        for (int i = 0; i < _navHit.Length; i++) {
            _navHit[i] = null;
            _navLabel[i] = null;
            _navRects[i] = null;
            _navX[i] = 0f;
        }
        for (int s = 0; s < NavSeams; s++) _navGap[s] = 0f;
    }
}
