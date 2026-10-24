using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：库页「排序方式」按钮。
/// 两种排序：字母分组（原样）/ 按标记难度（P1→U20，未标记最后，分组标头一个都不显示，列表平铺）。“ 收藏”照旧置顶，跟本模块无关。
/// 切到难度排序时字母条整条淡出，列表 + 滚动条整块右移补满，两者间距仍是原来那 31px；点击立刻落盘，以后进入游戏沿用。
/// 本模块是按钮本体、排序状态与落盘、切换过渡、以及字母条淡入淡出的唯一写入点；列表顺序和标头在 LevelScanner.SortEntries 与 RefreshList 里，不在这儿。</summary>
public static partial class PortalUI {

    // ================= 几何常量（全部按 1080 参考分辨率）=================

    /// <summary>按钮：跟搜索框同一行（上边 166 / 高 44），左端离搜索框右边界 8px，右端跟列表右边界对齐
    /// —— 两端各对齐一条已有的线，看着才是“列表上方的一个键”</summary>
    private const float SortBtnGapL = 8f;
    private const float SortBtnLeft = SearchRightEdge + SortBtnGapL;
    private const float SortBtnRight = ListRightEdge;
    private const float SortBtnW = SortBtnRight - SortBtnLeft;
    private const float SortBtnTop = SearchBoxTop;
    private const float SortBtnH = SearchBoxH;

    /// <summary>难度排序时滚动条的新 x：右端正好落在字母条原来的右边界上（视觉上就是“字母条让出来的那块，列表整块搬过去了”）</summary>
    private const float ScrollbarXSorted = LetterColX + LetterColW - ScrollbarW;
    /// <summary>难度排序时列表右边界：跟滚动条保持原来那 31px 间距（`ScrollbarX - ListRightEdge`）</summary>
    private const float LibRightEdgeSorted = ScrollbarXSorted - (ScrollbarX - ListRightEdge);

    /// <summary>过渡时长：跟“换序”那一段严格对齐（淡出 0.08 + 淡入 0.22），两边一起收尾才不会看着生硬</summary>
    private const float SortModeDur = 0.30f;
    /// <summary>单帧步进上限：抖一下两三百毫秒的帧不许把过渡一步冲完，那又成了“闪现”（同 `UiFadeMaxStep`）</summary>
    private const float SortModeMaxStep = 0.05f;
    /// <summary>换序：先淡出（旧顺序看不见了）→ 在看不见的时候重排 → 再淡入（新顺序）</summary>
    private const float SortSwapOutSeconds = 0.08f;
    private const float SortSwapInSeconds = 0.22f;

    // ---- 收藏区与"按难度排的那一大段"之间的隔断 ----
    // 标头文字是 `TextAnchor.MiddleCenter`，天然就在“中间”，不用另外摆位
    /// <summary>隔断：标头上方留白</summary>
    private const float SortSepGapTop = 32f;
    /// <summary>隔断：标头下方留白 —— 比上方少 16，不是随手写的：标头文字框 `offsetMin.y = 0 / offsetMax.y = 16`，
    /// 所以那 48px 的行里字的中心落在距行顶 16px 处（不是行中心）；拿它当整段隔断的视觉中心，下面就得少留 16。
    /// 推导：G1 + 16 = (G1 + H + G2) / 2，即 G2 = G1 − 16，与行高无关</summary>
    private const float SortSepGapBottom = SortSepGapTop - 16f;
    /// <summary>隔断上的字（三语表里有）</summary>
    private const string SortSepLabel = "列表";

    // ================= 状态 =================

    /// <summary>排序方式过渡进度：0 = 字母分组（原样）/ 1 = 按标记难度。
    /// 建面板时直接就位（按落盘的值），不许从 0 飞进去 —— 那样每次开面板都会看见列表抖一下</summary>
    private static float _sortK, _sortKTarget;
    private static Button _sortBtn;
    private static GameObject _sortBtnGo;
    private static RectTransform _sortBtnRt;   // 按钮矩形（右端每帧跟列表右边界对齐）
    private static Image _sortBtnImg;
    private static Text _sortBtnText;
    /// <summary>字母条的 CanvasGroup（淡入淡出的唯一载体，见 `ApplyLetterColAlpha`）</summary>
    private static CanvasGroup _letterColGroup;
    /// <summary>字母条那层的“渐入系数”（启动页/屏保那套“列表淡回”用的；默认 1 = 已经显示着）</summary>
    private static float _letterFadeK = 1f;
    /// <summary>列表内容那层的 CanvasGroup —— 换序时只淡它，不动底板/滚动条。
    /// 不能淡整块 `_libListRootGo`：启动页那套“列表淡回”已经占了它，两个写入点会打架</summary>
    private static CanvasGroup _sortSwapGroup;
    /// <summary>换序状态机：0=空闲 / 1=淡出中 / 2=淡入中（`_sortSwapT` 是对应那一段的计时）</summary>
    private static int _sortSwapPhase;
    private static float _sortSwapT;

    // ================= 唯一的写入点 =================

    /// <summary>排序方式过渡进度（0~1，smoothstep 缓动）—— 布局取值统一用它</summary>
    private static float LibSortK() {
        float e = Mathf.Clamp01(_sortK);
        return e * e * (3f - 2f * e);
    }

    /// <summary>列表当前右边界（字母分组 1736 → 难度排序 1832，中间是过渡值）</summary>
    private static float LibSortRightEdge() { return Mathf.Lerp(ListRightEdge, LibRightEdgeSorted, LibSortK()); }

    /// <summary>列表滚动条当前 x（1767 → 1863）—— 必须跟列表用同一份进度，各算各的就会错开</summary>
    private static float LibSortScrollbarX() { return Mathf.Lerp(ScrollbarX, ScrollbarXSorted, LibSortK()); }

    /// <summary>字母条透明度 = 渐入系数 × (1 − 排序过渡进度)。
    /// 这里必须是唯一写入点：启动页那套“列表/字母条/顶栏一起淡回”也写这个 CanvasGroup，两处各写一半，谁后写谁赢 ——
    /// 会出现“难度排序开着，屏保回来字母条又冒出来”（所以那边改调 `SetLetterColFadeK`）</summary>
    private static void ApplyLetterColAlpha() {
        var g = _letterColGroup;
        if (g == null) return;
        float a = Mathf.Clamp01(_letterFadeK * (1f - LibSortK()));
        if (g.alpha != a) g.alpha = a;
        bool hot = a > 0.5f;   // 淡出过半就交回射线：隐藏的字母条一个点击都不许接，免得点到看不见的按钮
        if (g.blocksRaycasts != hot) g.blocksRaycasts = hot;
        if (g.interactable != hot) g.interactable = hot;
    }

    /// <summary>启动页/屏保那边“列表那一片淡回”的入口：它只管渐入系数，排序那部分在 `ApplyLetterColAlpha` 里合</summary>
    private static void SetLetterColFadeK(float k) {
        _letterFadeK = Mathf.Clamp01(k);
        ApplyLetterColAlpha();
    }

    /// <summary>按钮矩形：左端固定，右端每帧跟列表右边界对齐。
    /// 宽度不是常量，一律 `LibSortRightEdge() - SortBtnLeft`（跟列表同一份进度，转过去的时候一起长）</summary>
    private static void ApplySortButtonRect() {
        if (_sortBtnRt == null) return;
        SetRectPx(_sortBtnRt, SortBtnLeft, SortBtnTop, LibSortRightEdge() - SortBtnLeft, SortBtnH);
    }

    /// <summary>换序时淡的那一层 = 列表内容（建列表时登记，见 `AttachSortSwapGroup`）。alpha 只在这儿写</summary>
    private static void ApplySortSwapAlpha() {
        var g = _sortSwapGroup;
        if (g == null) return;
        float a = 1f;
        if (_sortSwapPhase == 1) a = 1f - Mathf.Clamp01(_sortSwapT / SortSwapOutSeconds);
        else if (_sortSwapPhase == 2) a = Mathf.Clamp01(_sortSwapT / SortSwapInSeconds);
        if (g.alpha != a) g.alpha = a;
        bool hot = a > 0.5f;   // 半透明期间不吃点击，否则会点中“已经看不见的那一行”
        if (g.blocksRaycasts != hot) g.blocksRaycasts = hot;
    }

    // ================= 建 =================

    /// <summary>建“排序方式”按钮（挂在库页根下，跟搜索框同一层，都在页面上方的静态区）。
    /// 调用点必须排在字母条/列表之前：它会把 `_sortK` 直接摆到落盘的那个值，
    /// 晚于它们建的话第一帧的列表宽度还是老值，会看见列表弹一下</summary>
    private static void BuildSortModeButton(Transform libRoot) {
        // 直接就位（不开动画）：`SortByDifficulty` 是启动时从 settings.txt 读进来的
        _sortK = _sortKTarget = SortByDifficulty ? 1f : 0f;
        _letterFadeK = 1f;   // 面板刚建出来时字母条是“已经在显示”的那一档（启动页随后会把它设成 0 再淡回）
        // 复用详情区那个“左上角锚定按钮”的辅助函数（`MakeButton` 是中心锚定的，混用会跑到面板外）
        var btn = HomeButton((RectTransform)libRoot.transform, "SortMode", SortBtnLeft, SortBtnTop, SortBtnW, SortBtnH,
                             ItemColor, SortLabel(), 24, out _sortBtnText);
        btn.onClick.AddListener(ToggleSortMode);
        _sortBtn = btn;
        _sortBtnGo = btn.gameObject;
        _sortBtnRt = btn.GetComponent<RectTransform>();
        _sortBtnImg = btn.GetComponent<Image>();
        _sortBtnImg.sprite = RoundedSprite;
        _sortBtnImg.type = Image.Type.Sliced;
        ApplySortButtonRect();   // 右端立刻跟列表右边界对齐（落盘是“难度”时它一开始就该是长的）
        // 初始显隐跟搜索框同一条规则（未选中谱面时那一行整体藏着，见 LibChromeVisible）
        _sortBtnGo.SetActive(LibChromeVisible());
        PaintSortButton();
    }

    /// <summary>字母条建好之后立刻登记（它才是“淡入淡出”的载体，见 `ApplyLetterColAlpha`）</summary>
    private static void AttachLetterColGroup(GameObject letterCol) {
        if (letterCol == null) { _letterColGroup = null; return; }
        _letterColGroup = letterCol.GetComponent<CanvasGroup>();
        if (_letterColGroup == null) _letterColGroup = letterCol.AddComponent<CanvasGroup>();
        ApplyLetterColAlpha();   // 建的时候就要摆对（排序方式可能是“难度”，那它一开始就该是透明的）
    }

    /// <summary>列表内容那层登记（换序时淡的就是它，底板和滚动条不动）</summary>
    private static void AttachSortSwapGroup(GameObject content) {
        if (content == null) { _sortSwapGroup = null; return; }
        _sortSwapGroup = content.GetComponent<CanvasGroup>();
        if (_sortSwapGroup == null) _sortSwapGroup = content.AddComponent<CanvasGroup>();
        _sortSwapPhase = 0;
        _sortSwapT = 0f;
        ApplySortSwapAlpha();   // 新面板一定是“看得见”的状态
    }

    // ================= 按钮外观 =================

    /// <summary>按钮上的字 = 当前排序方式，不是“点了会变成什么”（那样按钮上写的字和眼前看到的列表对不上）。
    /// 难度排序时底色换成主题色 + 深色字（跟“选中行/字母蓝块”同一套语言），一眼看出现在是哪一种</summary>
    private static string SortLabel() { return SortByDifficulty ? "难度排序" : "字母排序"; }

    /// <summary>难度排序时底色换主题色 + 文字换深色（跟"选中行/字母蓝块"同一套语言），一眼看出现在是哪一种。
    /// 两样都必须走 SetUiSelectFill / 先改文字再刷底色：交互态层每帧从自己记的常态色重算这张图，
    /// 直接写 Image.color 会被它顶掉 —— 鼠标移开那一帧原本的颜色就没了（看着像变透明）。</summary>
    private static void PaintSortButton() {
        if (_sortBtnText != null) {
            _sortBtnText.text = Lang.Tr(SortLabel());
            _sortBtnText.color = SortByDifficulty ? HighlightTextColor : Color.white;
        }
        SetUiSelectFill(_sortBtn, SortByDifficulty ? Accent : ItemColor, _sortBtnText);
    }

    // ================= 点击 =================

    /// <summary>点一下换一种：立刻落盘 + 起过渡 + 起“换序”那一段。
    /// 这里不直接调 `RefreshList()` —— 那会“直接闪、有点生硬”；改成淡出 → 在看不见的时候重排 → 淡入（见 `TickSortMode`）</summary>
    private static void ToggleSortMode() {
        SortByDifficulty = !SortByDifficulty;
        AutoFeatures.Save();          // 立刻写进 settings.txt（不等关面板、不等退出游戏）
        _sortKTarget = SortByDifficulty ? 1f : 0f;
        PaintSortButton();
        // 换序状态机：空闲 → 起一段淡出；正在淡入时又点，就退回淡出重来一遍（顺序必须真的换过来）
        if (_sortSwapPhase == 0 || _sortSwapPhase == 2) {
            _sortSwapPhase = 1;
            _sortSwapT = 0f;
        }
        // 淡出中再点不用重启：收尾那一步会按当时的开关重排，连点两下结果也是对的
        Lg.Info($"[LevelLib] 排序方式：{(SortByDifficulty ? "按标记难度（P1→U20，未标记最后）" : "字母分组")}"
            + $" 已落盘 列表 {ListRightEdge:F0}→{LibRightEdgeSorted:F0}（字母条淡出 滚动条 {ScrollbarX:F0}→{ScrollbarXSorted:F0}）");
    }

    /// <summary>把列表钉回最上面（排序方式一换，老滚动位置就没意义了）</summary>
    private static void ScrollLibToTop() {
        try {
            var scroll = _libScroll;
            if (scroll == null) return;
            if (scroll.content != null) scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, 0f);
            scroll.verticalNormalizedPosition = 1f;
            _listDirty = true;      // 下一帧按新位置重画可见行
        } catch { }
    }

    // ================= 每帧 =================

    /// <summary>过渡 + 换序推进（库页面板段）。都停下时每帧只花两次浮点比较。
    /// 摆布局复用 `ApplyLibExpand`，不另写一份 —— 列表/滚动条/字母条/按钮四块矩形只许有一个写入点</summary>
    private static void TickSortMode() {
        float dt = Mathf.Min(Time.unscaledDeltaTime, SortModeMaxStep);   // 单帧步进上限（两段都用）
        bool moving = _sortK != _sortKTarget;
        if (moving) {
            _sortK = Mathf.MoveTowards(_sortK, _sortKTarget, dt / SortModeDur);
            ApplyLibExpand(_libExpand);   // 取用 LibSortRightEdge / LibSortScrollbarX 顺带把字母条透明度也刷了
        }
        // ---- 换序那一段：淡出 → 重排 → 淡入 ----
        if (_sortSwapPhase != 0) {
            _sortSwapT += dt;
            if (_sortSwapPhase == 1 && _sortSwapT >= SortSwapOutSeconds) {
                // 看不见的那一帧才动数据：库的顺序全局重排 + 重建逻辑行 + 回到最上面
                LevelScanner.SortEntries();   // 读的是当前开关 连点两下也不会排错
                RefreshList();
                ScrollLibToTop();
                _sortSwapPhase = 2;
                _sortSwapT = 0f;
            } else if (_sortSwapPhase == 2 && _sortSwapT >= SortSwapInSeconds) {
                _sortSwapPhase = 0;
                _sortSwapT = 0f;
            }
            ApplySortSwapAlpha();
        } else if (!moving) {
            ApplyLetterColAlpha();   // 都停了也要保证“字母条透明度 = 渐入系数 ×(1−进度)”这条不变量
        }
    }

    /// <summary>按钮显隐跟着“搜索框那一行”走（未选中谱面时整行都藏着，见 `LibChromeVisible`）——
    /// 由 `ApplyLibExpand` 每帧对齐（它本来就在管搜索框/滑条/信息栏那几件的显隐）</summary>
    private static void SyncSortButtonVisible() {
        if (_sortBtnGo == null) return;
        bool show = LibChromeVisible();
        if (_sortBtnGo.activeSelf != show) _sortBtnGo.SetActive(show);
    }

    /// <summary>面板销毁时清引用（面板画布整棵都没了，留着就是假 null）</summary>
    private static void ClearSortModeRefs() {
        _sortBtn = null;
        _sortBtnGo = null;
        _sortBtnRt = null;
        _sortBtnImg = null;
        _sortBtnText = null;
        _letterColGroup = null;
        _letterFadeK = 1f;
        _sortSwapGroup = null;   // 内容那层随画布销毁
        _sortSwapPhase = 0;      // 换序状态归零（下次开面板一定是“看得见”）
        _sortSwapT = 0f;
        // `_sortK` 不归零 —— 下次开面板由 `BuildSortModeButton` 按落盘值直接就位
    }
}
