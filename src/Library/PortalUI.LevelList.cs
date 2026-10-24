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

/// <summary>PortalUI partial 模块：LevelList（官谱库列表的字母定位条、等级筛选、虚拟化行渲染、行点击）</summary>
public static partial class PortalUI {

    private static void MakeLetterButton(Transform parent, string letter) {
        // 按钮底：形状必须跟选中蓝块完全一样（同一个 RoundedSprite、同样的尺寸），否则两块会错位
        var btn = MakeButton(parent, "Letter_" + letter, Vector2.zero, new Vector2(LetterBtnW, LetterBtnH), ItemColor);
        btn.onClick.AddListener(() => ScrollToLetter(letter));
        // 字母按钮不做任何变色：选中蓝块滑过去时鼠标正好停在按钮上，悬停变色看起来就是"蓝块经过的按钮变黑了"。
        // 所以把 Button 的颜色过渡关掉、把悬停组件拆掉，按钮底永远是那个固定颜色。
        btn.transition = Selectable.Transition.None;
        var hover = btn.GetComponent<ButtonHover>();
        if (hover != null) Object.Destroy(hover);
        _letterBtnRt[letter] = btn.GetComponent<RectTransform>();   // 选中蓝块按它定位
        _letterBtnImg[letter] = btn.GetComponent<Image>();          // 选中时把它的底隐掉
    }

    /// <summary>字母文字层：整列最上层，位置跟对应按钮一致（这样蓝块不会被文字挡、也不挡文字）</summary>
    private static void BuildLetterLabels(Transform letterCol) {
        _letterTextLayer = new GameObject("LetterLabels", typeof(RectTransform));
        _letterTextLayer.transform.SetParent(letterCol, false);
        var lrt = _letterTextLayer.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var le = _letterTextLayer.AddComponent<LayoutElement>();
        le.ignoreLayout = true;   // 不参与字母列的竖排布局
        foreach (var kv in _letterBtnRt) {
            if (kv.Value == null) continue;
            var t = MakeText(_letterTextLayer.transform, "T_" + kv.Key, 22, Color.white, TextAnchor.MiddleCenter);
            t.text = kv.Key;
            t.raycastTarget = false;
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);   // 跟按钮同一参照系
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(LetterBtnW, LetterBtnH);
            trt.anchoredPosition = kv.Value.anchoredPosition;
            _letterBtnText[kv.Key] = t;
        }
    }

    // ---- 字母定位条的选中提示（实心蓝块）----
    // 蓝块跟着"当前正在看的那个字母区"走：算出视口顶部落在哪一组里，再移到对应字母按钮上。
    // 位置用指数平滑追（跟设置页导航一个手感），滚动时看起来是"滑过去"而不是跳。
    private static readonly Dictionary<string, RectTransform> _letterBtnRt = new();
    private static readonly Dictionary<string, Text> _letterBtnText = new();
    private static readonly Dictionary<string, Image> _letterBtnImg = new();
    private static RectTransform _letterFrameRt;
    private static float _letterFrameCurY;      // 当前 y（平滑值）
    private static float _letterFrameTargetY;   // 目标 y
    private static string _letterHighlightOn;   // 当前高亮的是哪个字母（改字色用）
    private static bool _letterFrameInit;       // 第一次直接就位，不做动画
    private static GameObject _letterTextLayer; // 字母文字层（最上层）
    /// <summary>字母条宽度的一半（字母条 110 宽 → 55）。蓝块 x 用它，正好落在按钮列的中心线上。</summary>
    private const float LetterColHalfW = 55f;
    /// <summary>字母按钮尺寸（蓝块跟它一样大）</summary>
    private const float LetterBtnW = 44f;
    private const float LetterBtnH = 26f;
    /// <summary>字母定位条的高亮块色（= 选中块同款，alpha 0.92）。
    /// 必须是属性而不是 static readonly：后者会在类型初始化那一刻把当时的主色抄下来，换主题色后永远停在旧色。</summary>
    private static Color LetterHighlightColor {
        get { return new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 0.92f); }
    }

    private static void BuildLetterFrame(Transform letterCol) {
        var f = MakeImage(letterCol, "LetterFrame", LetterHighlightColor);
        f.raycastTarget = false;
        f.sprite = SmallRoundSprite;         // 小圆角块（半径 6；用全局那张半径 10 的会把这 26 高的块画歪）
        f.type = Image.Type.Sliced;
        var le = f.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;   // 不能让 VerticalLayoutGroup 把它当子项排到最下面
        var rt = f.rectTransform;
        // 锚点必须跟 VerticalLayoutGroup 摆出来的按钮用同一个参照系：布局组给子项设的是"以父节点中心为参照"的
        // anchoredPosition，用 (0.5,0.5) 配 pivot (0.5,0.5) 跟父节点的 110x824 撞在一起会整体偏大半截 统一左上角锚点
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(LetterBtnW, LetterBtnH);
        rt.anchoredPosition = new Vector2(LetterColHalfW, 0f);
        _letterFrameRt = rt;
        _letterFrameInit = false;
        f.gameObject.SetActive(false);
        // 图层：蓝块要画在"按钮底之上、字母之下"。按钮是 LayoutGroup 的子项按顺序画，蓝块 ignoreLayout 且排在最前
        // 会被按钮盖住，所以这里 SetAsLastSibling 排在按钮之后（在它们之上），文字层再排在最后（在最上）；
        // 选中那个按钮自己的底色会被隐掉（SetLetterHighlight），免得深色底压在蓝块上把颜色搞浑。
        f.transform.SetAsLastSibling();
    }

    /// <summary>当前视口顶部落在哪个字母组（返回组名；没有则 null）。" 收藏"在最上面，视口贴顶时属于它。</summary>
    private static string CurrentLetterGroup() {
        var scroll = _libScroll;
        if (scroll == null || scroll.content == null) return null;
        if (_groupOffsets.Count == 0) return null;
        float y = scroll.content.anchoredPosition.y;
        string best = null; float bestPos = float.NegativeInfinity;
        foreach (var kv in _groupOffsets) {
            if (kv.Value <= y && kv.Value > bestPos) { bestPos = kv.Value; best = kv.Key; }
        }
        if (best == null) {
            // 视口滚到了第一个字母标头之前（只可能是在收藏区）→ 用第一组
            float min = float.PositiveInfinity;
            foreach (var kv in _groupOffsets) if (kv.Value < min) { min = kv.Value; best = kv.Key; }
        }
        return best;
    }

    /// <summary>把字母文字层的位置同步到对应按钮上。
    /// 必须每帧做：按钮的位置是 VerticalLayoutGroup 排出来的，面板刚建好时它们还在 (0,0)，
    /// 那时复制坐标会让 27 个字全堆在原点（看到的"字母缩成一团"）。</summary>
    private static void SyncLetterLabels() {
        if (_letterTextLayer == null) return;
        foreach (var kv in _letterBtnText) {
            var t = kv.Value;
            if (t == null) continue;
            if (!_letterBtnRt.TryGetValue(kv.Key, out var brt) || brt == null) continue;
            var want = brt.anchoredPosition;
            var cur = t.rectTransform.anchoredPosition;
            if (cur.x != want.x || cur.y != want.y) t.rectTransform.anchoredPosition = want;
        }
    }

    /// <summary>字母条选中蓝块每帧驱动：跟随当前字母区，指数平滑滑动；
    /// 顺便把当前字母的字色压深一点（蓝块是浅蓝，白字有点糊）</summary>
    private static void TickLetterFrame() {
        var rt = _letterFrameRt;
        if (rt == null) return;
        SyncLetterLabels();   // 文字位置跟着布局走（首帧布局没完成时它们还是 0,0）
        string g = CurrentLetterGroup();
        if (g == null || !_letterBtnRt.TryGetValue(g, out var brt) || brt == null) {
            if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
            SetLetterHighlight(null);
            return;
        }
        if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
        _letterFrameTargetY = brt.anchoredPosition.y;   // VerticalLayoutGroup 给的中心坐标
        if (!_letterFrameInit) {
            _letterFrameInit = true;
            _letterFrameCurY = _letterFrameTargetY;
        } else {
            float k = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            _letterFrameCurY = Mathf.Lerp(_letterFrameCurY, _letterFrameTargetY, k);
            if (Mathf.Abs(_letterFrameCurY - _letterFrameTargetY) < 0.15f) _letterFrameCurY = _letterFrameTargetY;
        }
        // 横向：字母条 110 宽、按钮 44 宽且整列居中 按钮中心在 x=55；写 0（条的最左边）框会跑到滑条那一段上。
        // 纵向参照系跟按钮一致（锚左上），所以 y 用按钮的 y 就行。
        rt.anchoredPosition = new Vector2(LetterColHalfW, _letterFrameCurY);
        SetLetterHighlight(g);
    }

    /// <summary>切换高亮字母：隐掉选中按钮自己的底色（免得深色底压在蓝块上）+ 只改两边字母的字色</summary>
    private static void SetLetterHighlight(string letter) {
        if (_letterHighlightOn == letter) return;
        // 旧的按钮底恢复显示
        if (!string.IsNullOrEmpty(_letterHighlightOn) && _letterBtnImg.TryGetValue(_letterHighlightOn, out var oldImg) && oldImg != null)
            SetLetterBtnBg(_letterHighlightOn, oldImg, true);
        if (!string.IsNullOrEmpty(_letterHighlightOn) && _letterBtnText.TryGetValue(_letterHighlightOn, out var oldT) && oldT != null)
            oldT.color = Color.white;
        _letterHighlightOn = letter;
        // 新的按钮底隐掉（蓝块代替它）
        if (!string.IsNullOrEmpty(letter) && _letterBtnImg.TryGetValue(letter, out var newImg) && newImg != null)
            SetLetterBtnBg(letter, newImg, false);
        if (!string.IsNullOrEmpty(letter) && _letterBtnText.TryGetValue(letter, out var newT) && newT != null)
            newT.color = new Color(0.06f, 0.10f, 0.18f, 1f);   // 深蓝黑，压在浅蓝块上很清楚
    }

    /// <summary>按钮底的显隐。必须记住原色再写回原色：按钮原本可能是被面板透明度缩放过的半透明值，
    /// 恢复时写死 alpha 1.0 就会让它每被蓝块经过一次就更实更暗（看起来像"背景框变黑"）。</summary>
    private static readonly Dictionary<string, Color> _letterBtnBaseCol = new();

    private static void SetLetterBtnBg(string letter, Image img, bool visible) {
        if (img == null) return;
        if (visible) {
            // 恢复成它原本的颜色（首次调用时先把它记下来）
            if (_letterBtnBaseCol.TryGetValue(letter, out var baseCol)) { img.color = baseCol; return; }
            _letterBtnBaseCol[letter] = img.color;
            return;
        }
        if (!_letterBtnBaseCol.ContainsKey(letter)) _letterBtnBaseCol[letter] = img.color;   // 先记住原色再隐藏
        var c = img.color;
        c.a = 0f;
        img.color = c;
    }

    /// <summary>等级区间双滑块（P1~U20），自绘：EventTrigger 手写拖动，不依赖 uGUI Slider</summary>
    private static void MakeRankSlider(Transform parent) {
        // 轨道半透明深色、选中段不填色、数值居中显示；轨道高 22 才放得下居中的数值文字
        var bg = MakeImage(parent, "RankBg", new Color(0f, 0f, 0f, 0.34f));
        bg.sprite = SmallRoundSprite;
        bg.type = Image.Type.Sliced;
        bg.rectTransform.sizeDelta = new Vector2(RankTrackW, 22f);
        // 未选中谱面（详情区收起）时难度滑条也藏起来，跟搜索框 / 滚动信息栏共用 LibChromeVisible()（三处必须一致）。
        // 藏起来不会挤走旁边的"正在播放"信息栏：行里第一个是关键占位 spacer(380)，弹性 spacer 在后面吸收空间。
        _rankSliderGo = bg.gameObject;
        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = RankTrackW;
        le.preferredHeight = 22f;
        le.minHeight = 22f;
        le.minWidth = RankTrackW;   // 轨道宽固定=preferred，HLayout 不能压缩/拉伸（根治首帧宽度漂移）
        le.flexibleWidth = 0f;
        _rankTrackRT = bg.rectTransform;

        // 区间指示条：不用主色填充，改成一个很淡的白条做区间提示
        var range = MakeImage(bg.transform, "Range", new Color(1f, 1f, 1f, 0.14f));
        range.raycastTarget = false;
        range.sprite = SharpSprite;
        _rankRangeImg = range;
        var rrt = range.rectTransform;
        rrt.anchorMin = new Vector2(0f, 0f);
        rrt.anchorMax = new Vector2(0f, 1f);
        rrt.pivot = new Vector2(0f, 0.5f);
        rrt.sizeDelta = Vector2.zero;
        _rankRangeRT = rrt;

        // 左右滑块不用固定图，而是显示"当前选中的那个难度"的标记图（左=左区间难度，右=右区间难度）
        _rankHandleA = MakeRankHandle(bg.transform, "HandleA", true);
        _rankHandleB = MakeRankHandle(bg.transform, "HandleB", false);
        // 不强设 0/59，用持久化的 RankMin/RankMax（筛选保留，首帧即正确位置不闪）
        _rankMin = Mathf.Clamp(RankMin, 0, 59);
        _rankMax = Mathf.Clamp(RankMax, 0, 59);

        // 数值：轨道正中央（P1 ~ U20），画在所有东西之后 = 最上层，不会被两个滑块盖住
        _rankLabel = MakeText(bg.transform, "RankVal", 20, Color.white, TextAnchor.MiddleCenter);
        _rankLabel.raycastTarget = false;
        Stretch(_rankLabel.rectTransform, Vector2.zero, Vector2.zero);

        _rankRangeDirty = true;   // 布局完成后下一帧重算位置（首帧 HLayout 未布局完，宽度会算错导致滑块飞出去）
        // 初始显隐：未选中时藏起来（跟搜索框 / 信息栏同一规则，见 LibChromeVisible）
        _rankSliderGo.SetActive(LibChromeVisible());
        // 立即强制布局一次：让轨道宽度马上是真实值，首帧滑块位置就正确（不闪现）
        try {
            var prt = bg.rectTransform.parent as RectTransform;
            if (prt != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(prt);
        } catch { }
        UpdateRankRange();
    }

    /// <summary>创建一个滑块手柄（手柄本体不可见，里面那张图 = 当前选中难度的标记图）。
    /// 手柄挂 EventTrigger 负责拖动，图标是它的子物体（不接收射线，不会抢拖拽）。</summary>
    private static Image MakeRankHandle(Transform parent, string name, bool isA) {
        // 手柄本体：透明，只做定位 + 拖拽手势的接收者
        var h = MakeImage(parent, name, new Color(1f, 1f, 1f, 0f));
        var rt = h.rectTransform;
        rt.sizeDelta = new Vector2(55f, 55f);
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // 难度图：撑满手柄、保持比例居中
        var icon = MakeImage(h.transform, "Icon", Color.white);
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        Stretch(icon.rectTransform, Vector2.zero, Vector2.zero);
        _rankHandleIcon[isA ? 0 : 1] = icon;
        var et = h.gameObject.AddComponent<EventTrigger>();
        AddTrigger(et, EventTriggerType.PointerDown, _ => { });   // 建立拖拽（必须按下过才能收到 Drag）
        AddTrigger(et, EventTriggerType.Drag, d => OnRankDrag(isA, d));
        AddTrigger(et, EventTriggerType.PointerUp, _ => RefreshList());   // 松手才刷新列表（防卡顿）
        return h;
    }

    /// <summary>左右手柄里那张难度图的 Image（0=左 1=右）</summary>
    private static readonly Image[] _rankHandleIcon = new Image[2];

    /// <summary>把某个等级的标记图换成手柄图标（等级序号 0=P1 … 59=U20）。
    /// 图标走 MarkStore 的缓存，拖动时只是查字典；某一档素材不存在就隐藏图标（只留透明手柄，照样能拖）。</summary>
    private static void ApplyRankHandleIcon(int slot, int idx) {
        var img = (slot >= 0 && slot < _rankHandleIcon.Length) ? _rankHandleIcon[slot] : null;
        if (img == null) return;
        string mark = RankName(idx);
        var spr = MarkStore.LoadIcon(mark);
        if (spr == null) {
            if (img.enabled) img.enabled = false;
            return;
        }
        if (img.sprite != spr) { img.sprite = spr; img.type = Image.Type.Simple; }
        if (!img.enabled) img.enabled = true;
    }

    /// <summary>拖动滑块：按鼠标 x 相对轨道比例换算 0~59</summary>
    private static void OnRankDrag(bool isA, BaseEventData bed) {
        var ed = bed as PointerEventData;
        if (ed == null || _rankTrackRT == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rankTrackRT, ed.position, ed.pressEventCamera, out Vector2 local)) return;
        float w = Mathf.Max(1f, RankTrackW);
        int v = Mathf.RoundToInt(Mathf.Clamp01((local.x + w * 0.5f) / w) * 59f);
        if (isA) {
            _rankMin = v;
            if (v > _rankMax) _rankMax = v;   // A 拖过 B 时 B 跟着走
        } else {
            _rankMax = v;
            if (v < _rankMin) _rankMin = v;   // B 拖过 A 时 A 跟着走
        }
        UpdateRankRange();
        AutoFeatures.Save();   // 筛选持久化（RankMin/RankMax）
    }

    /// <summary>等级序号 → 显示名（0=P1 59=U20）</summary>
    private static string RankName(int idx) {
        if (idx < 0) idx = 0;
        if (idx > 59) idx = 59;
        char c = idx < 20 ? 'P' : idx < 40 ? 'G' : 'U';
        return c + ((idx % 20) + 1).ToString();
    }

    /// <summary>刷新区间高亮条位置/宽度 + 滑块位置 + 当前范围文字（按轨道实际宽度）</summary>
    private static void UpdateRankRange() {
        if (_rankRangeRT == null) return;
        // 轨道宽用固定基准值，不依赖运行时 rect.width（根治首帧布局漂移导致滑块闪现）
        float barW = RankTrackW;
        float minX = barW * (_rankMin / 59f);
        float maxX = barW * (_rankMax / 59f);
        _rankRangeRT.anchoredPosition = new Vector2(minX, 0f);
        _rankRangeRT.sizeDelta = new Vector2(Mathf.Max(0f, maxX - minX), 0f);
        if (_rankHandleA != null) _rankHandleA.rectTransform.anchoredPosition = new Vector2(minX, 0f);
        if (_rankHandleB != null) _rankHandleB.rectTransform.anchoredPosition = new Vector2(maxX, 0f);
        // 手柄里的图跟着当前选中的难度走
        ApplyRankHandleIcon(0, _rankMin);
        ApplyRankHandleIcon(1, _rankMax);
        // 数值跟着左右手柄走（居中的 P1 ~ U20）
        if (_rankLabel != null) _rankLabel.text = RankName(_rankMin) + "  ~  " + RankName(_rankMax);
        // 滑块位置监测日志：只在详细日志下输出
        if (LogVerbose) {
            try {
                float ha = (_rankHandleA != null) ? _rankHandleA.rectTransform.anchoredPosition.x : -1f;
                float hb = (_rankHandleB != null) ? _rankHandleB.rectTransform.anchoredPosition.x : -1f;
                Lg.Info($"[LevelLib][RankSlider] barW={barW:F0} min={_rankMin} max={_rankMax} handleA.x={ha:F0} handleB.x={hb:F0}");
            } catch { }
        }
    }

    /// <summary>一键清空「所有筛选项」：搜索词 + 难度区间。
    /// 必须存在：搜索框和难度滑条跟"有没有选中谱面"共用同一条显隐规则（LibChromeVisible，未选中时全藏），
    /// 而筛选条件是持久的（_search 活到关面板、难度区间还写进 settings.txt）——
    /// "筛选把谱面全筛掉 + 没选中谱面"就闭环了，列表空得没得选、条件也改不了，重启游戏都不一定救得回来。
    /// 出口两处（都不看选中状态）：① 列表里那行「没有匹配的谱面 · 点这里清空筛选条件」② 设置 → 库页的「清空谱面筛选」。
    /// 全项目只有这里会把 _search 置空。</summary>
    private static void ClearAllFilters() {
        _search = "";
        // 搜索框自己的显示也要清：面板重建时是 sInput.text = _search，只清 _search 不改输入框会让人以为没清掉
        if (_libSearchInput != null) _libSearchInput.text = "";   // 这行会触发 onValueChanged、把 _search 再置空一次
        _searchDirtyTimer = 0f;                                   // 防抖计时清零：别在 0.15 秒后再白刷一次列表
        // 难度区间：走公开属性（自带钳制）→ 刷新手柄/范围文字 → 落盘（它是持久化的，不存就白清了）
        RankMin = 0; RankMax = 59;
        UpdateRankRange();
        AutoFeatures.Save();
        RefreshList();
        ShowToast(Lang.Tr("已清空搜索条件与难度筛选"));
    }

    /// <summary>刷新列表：构建逻辑行 + 设置 content 高度 + 下一帧渲染可见行（虚拟化）</summary>
    private static void RefreshList() {
        bool reorder = _poolRows.Count > 0;   // 已有行对象池 = 这是一次重列（建页那一次不算，别整屏乱飞）
        CloseRowContextMenu(true);   // 列表重建，菜单里记着的谱面可能已经不在列表里了（立刻收掉，不等淡出）
        _rainbowTexts.Clear();   // 重建前清掉旧彩虹文本引用
        _listViewportH = 0f;   // 布局可能变化，强制重新布局取视口高
        ListSnapReset();   // 列表重建：居中吸附先收起，等下一次真实滚动输入（见 LevelList.Snap.cs）

        UpdateCountText();   // 统计行实时刷新（谱面总数/通关数）
        RefreshEmptyLibHint();   // 空库提示（一个谱面都没有时才显示）
        _rowByPath.Clear();
        // 刷新列表=数据可能变化（导入/删除/重命名等），清掉文件缓存让状态重新读取
        _existsCache.Clear();
        _artistCache.Clear();
        _authorDisplayCache.Clear();
        InvalidateLevelTextCache();   // 谱面文本缓存同步作废（导入/删除后不能读到旧内容）

        var scroll = _libScroll;
        if (scroll == null) return;
        var content = scroll.content;
        // 去掉自动布局（虚拟化行绝对定位，VLayout/Fitter 会重新排池行导致错位）
        var vLayout = content.GetComponent<VerticalLayoutGroup>();
        if (vLayout != null) Object.DestroyImmediate(vLayout);
        var vFitter = content.GetComponent<ContentSizeFitter>();
        if (vFitter != null) Object.DestroyImmediate(vFitter);

        _logicalRows.Clear();
        _groupOffsets.Clear();
        _display.Clear();
        string search = _search == null ? "" : _search.Trim();
        float acc = 0f;

        // 过滤 + 分成 收藏 / 普通 两组
        var favList = new List<LevelEntry>();
        var normalList = new List<LevelEntry>();
        foreach (var entry in LevelScanner.Entries) {
            // 搜索筛选（忽略大小写）
            if (search.Length > 0 && entry.Title.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            // 等级区间筛选：无等级标记（Q/特殊/完全无标记）由 0 开关控制；有等级按区间过滤
            // 有判定标记（宽标严）的谱面不算"无等级"：始终显示（宽标严从无等级分类里剔除）
            int rank = RankOf(MarkStore.Get(entry.FilePath));
            bool hasJudge = !string.IsNullOrEmpty(MarkStore.GetJudge(entry.FilePath));
            if (rank < 0 && !hasJudge) {
                if (!_filterNoMark) continue;   // 0 关闭 → 隐藏无等级标记谱面
            } else if (rank < 0) {
                // 只有判定标记（宽标严）：不参与区间，也不受 0 开关控制
            } else {
                if (rank < _rankMin || rank > _rankMax) continue;
            }
            // 判定筛选：按钮已删除，不再按判定过滤
            _display.Add(entry);
            if (LevelScanner.IsFavorite(entry.FolderPath)) favList.Add(entry);
            else normalList.Add(entry);
        }
        // 收藏区（整个列表最上面，金色标头）
        if (favList.Count > 0) {
            _logicalRows.Add(new LogicalRow { IsHeader = true, HeaderText = "★ 收藏", OffsetY = acc, Height = HeaderRowH });
            acc += HeaderRowH;
            foreach (var e in favList) {
                _logicalRows.Add(new LogicalRow { Entry = e, OffsetY = acc, Height = ItemRowH });
                acc += ItemRowH;
            }
        }
        // 正常区（字母分组标头）
        // "按标记难度"排序时一个标头都不显示：整条列表平铺，顺序由 LevelScanner.SortEntries 定（P1→U20、未标记最后）。
        // _groupOffsets 在难度模式下是空的，字母蓝块于是自己收起（CurrentLetterGroup 返回 null）。
        bool flat = SortByDifficulty;
        // 收藏区与"按等级排的那一大段"之间要有隔断，中间写「列表」（字母分组那边不用加：
        // 收藏下面紧跟着就是字母标头，本身就是一道分界）
        if (flat && favList.Count > 0 && normalList.Count > 0) {
            acc += SortSepGapTop;
            _logicalRows.Add(new LogicalRow { IsHeader = true, HeaderText = SortSepLabel, OffsetY = acc, Height = HeaderRowH });
            acc += HeaderRowH + SortSepGapBottom;
        }
        string lastKey = null;
        foreach (var entry in normalList) {
            if (!flat) {
                string key = LevelScanner.GroupKey(entry.Title);
                if (key != lastKey) {
                    _groupOffsets[key] = acc;
                    _logicalRows.Add(new LogicalRow { IsHeader = true, HeaderText = key, OffsetY = acc, Height = HeaderRowH });
                    acc += HeaderRowH;
                    lastKey = key;
                }
            }
            _logicalRows.Add(new LogicalRow { Entry = entry, OffsetY = acc, Height = ItemRowH });
            acc += ItemRowH;
        }
        // 空结果分两种，只有"被筛空"这一种才加行：
        // · 库里有谱面但被筛空 → 这一行就是出口：点它清空搜索词 + 难度区间
        // · 库里真的一个谱面都没有 → 不加行，列表中央那行淡字（EmptyLibHint）已经在说"还没有谱面"了
        if (_logicalRows.Count == 0) {
            bool libEmpty = LevelScanner.Entries == null || LevelScanner.Entries.Count == 0;
            if (!libEmpty) {
                _logicalRows.Add(new LogicalRow {
                    IsHeader = true,
                    HeaderText = "没有匹配的谱面 · 点这里清空筛选条件",
                    ClearFiltersOnClick = true,   // 渲染时这行变可点（见 UpdateVisible 的标头分支）
                    OffsetY = 0f,
                    Height = HeaderRowH
                });
                acc = HeaderRowH;
            }
        }

        // content 总高度（虚拟化必须手动设置，滚动范围才正确）
        // 必须强制横向拉伸 anchor，否则宽度变 0、行宽变 -40、功能键全挤左边
        var crtV = content.GetComponent<RectTransform>();
        crtV.anchorMin = new Vector2(0f, 1f);
        crtV.anchorMax = new Vector2(1f, 1f);
        crtV.pivot = new Vector2(0.5f, 1f);
        crtV.sizeDelta = new Vector2(0f, acc);
        Canvas.ForceUpdateCanvases();   // 立即生效，避免滚动范围/行位置读到旧尺寸
        UpdateScrollbarSize();          // 内容高度变了，手柄长度跟着重算

        // 首次打开时创建行对象池
        // 池里若存的是已经被销毁的行（面板被强制销毁 / 场景切换后残留），必须重建：
        // 否则下一帧 TickPanel → UpdateVisible → UpdatePoolRow 会对着已销毁的 RectTransform
        // 调 set_offsetMin，NullReferenceException 会把主循环打崩
        if (_poolRows.Count > 0 && !PoolAlive()) {
            Lg.Info("行对象池里有已销毁的行 → 重建池（防 NRE 打崩主循环）");
            _poolRows.Clear();
            reorder = false;   // 池都重建了，没有"旧位"可谈
        }
        if (_poolRows.Count == 0) CreatePool(content);
        // 作废全部认领：数据 / 排序一变，逻辑行列表整个换过，「这根正显示第几行」的旧值不再可信。
        // 置 -1 之后下一帧的分配会把这些行全部交还再重新认领，
        // 于是每一行都会走一次 UpdatePoolRow 重画内容 —— 不然认领表会以为旧内容还对，跳过重绘。
        foreach (var pool in _poolRows) pool.LogicalIndex = -1;
        // 重列（筛选 / 收藏 / 标记 / 导入删除后的刷新）开一个让位窗口：
        // 还挂在列表上的行从旧位滑到新位，池子换人或位移太远的一律直接落位（引擎里判）
        if (reorder) FlowWindow("谱面库换序", MoPage);
        _listDirty = true;   // 下一帧布局完成后渲染可见行（首帧 viewport 高度为 0 会算出空列表）
    }

    /// <summary>池里的行是不是都还活着（有一个被销毁就返回 false → 要求重建池）。
    /// Unity 的 == null 重载能同时覆盖"真 null"和"对象已销毁"两种情况。</summary>
    private static bool PoolAlive() {
        try {
            for (int i = 0; i < _poolRows.Count; i++) {
                var p = _poolRows[i];
                if (p == null || p.Rt == null || p.Go == null) return false;
            }
            return true;
        } catch { return false; }
    }

    /// <summary>按真实比例刷新滚动条手柄长度：
    /// 手柄高度 = 视口高 / 内容高（钳到 0.06~1），跟滚动范围一一对应，
    /// 所以手柄滑到两头时正好贴住轨道上下边，不会留缝。</summary>
    private static void UpdateScrollbarSize() {
        var sb = _libScrollbar;
        var scroll = _libScroll;
        if (sb == null || scroll == null) return;
        try {
            float vh = scroll.viewport != null ? scroll.viewport.rect.height : 0f;
            float ch = scroll.content != null ? scroll.content.rect.height : 0f;
            if (vh <= 1f || ch <= 1f) return;
            float ratio = Mathf.Clamp(vh / ch, 0.06f, 1f);
            if (Mathf.Abs(sb.size - ratio) > 0.0005f) sb.size = ratio;
        } catch { }
    }

    /// <summary>池行的身份键：条目行用谱面文件路径，标头行用标头文字。
    /// 重列时按它认人 —— 池子是"位置固定、内容轮换"的，不看身份的话根本分不出谁挪到了哪。</summary>
    private static string LibRowKey(LogicalRow lr) {
        if (lr == null) return null;
        if (lr.IsHeader) return "@h:" + (lr.HeaderText == null ? "" : lr.HeaderText);
        return lr.Entry == null ? null : lr.Entry.FilePath;
    }

    /// <summary>更新一个池行显示指定逻辑行（内容/位置/显隐）</summary>
    private static void UpdatePoolRow(PoolRow pool, int logicalIdx) {
        var lr = _logicalRows[logicalIdx];
        // 内容没变且行在可视区（不在视口外）就跳过（滚动复用时省掉整行重绘）
        if (pool.LogicalIndex == logicalIdx && !pool.Offscreen && pool.IsHeaderMode == lr.IsHeader && pool.Go.activeSelf) return;
        string rowKey = LibRowKey(lr);
        pool.Key = rowKey;
        pool.LogicalIndex = logicalIdx;
        pool.Offscreen = false;
        pool.IsHeaderMode = lr.IsHeader;
        // 行高按类型：标头 48 / 条目 80，与 RefreshList 里 acc 累加一致（标头行渲染高 76 会让顶部留视觉空隙）
        // 条目行 Bg 高 ItemRowH-RowGap，行间留 RowGap 视觉间隔（逻辑 acc 仍用 ItemRowH，滚动范围不变）
        float rowH = pool.IsHeaderMode ? HeaderRowH : (ItemRowH - RowGap);
        pool.Rt.offsetMin = new Vector2(20f, -rowH);
        pool.Rt.offsetMax = new Vector2(-20f, 0f);
        // 位置走流式让位（身份 = 谱面路径）：重列时那根行从"这条谱面原来在哪"滑到新位，
        // 窗口没开（滚动/拖动/建页）一律直接落位 —— 见 PortalUI.Flow.cs 的文件头
        FlowKeyTo(rowKey, pool.Rt, 0f, -lr.OffsetY);

        if (lr.IsHeader) {
            pool.ItemObj.SetActive(false);
            pool.HeaderObj.SetActive(true);
            pool.HeaderText.text = lr.HeaderText;
            if (lr.ClearFiltersOnClick) {
                // 「点这里清空筛选条件」那一行用主题淡蓝（跟"可交互"的选中框同色）+ 打开射线；
                // 其余标头（ 收藏 / 字母）保持橙色且不吃点击。MakeText 默认 raycastTarget=false，必须显式打开
                pool.HeaderText.color = FrameLineColor;
                pool.HeaderText.raycastTarget = true;
            } else {
                pool.HeaderText.color = lr.HeaderText == "★ 收藏" ? IconFavOn : new Color(1f, 0.65f, 0.2f, 1f);
                pool.HeaderText.raycastTarget = false;
            }
            pool.Go.SetActive(true);
            return;
        }
        pool.HeaderObj.SetActive(false);
        pool.ItemObj.SetActive(true);
        var e = lr.Entry;
        // 诊断：首屏前 3 行信息 + 行宽（排查功能键错位/名字不显示）
        if (!_vzLoggedOnce && logicalIdx < 3) {
            Lg.Info($"[LevelLib] VZ行{logicalIdx}: header={lr.IsHeader} text={(lr.IsHeader ? lr.HeaderText : (e != null ? e.Title : "NULL"))} goW={pool.Rt.rect.width:F0} itemW={pool.Bg.rectTransform.rect.width:F0} offY={lr.OffsetY:F0}");
            if (logicalIdx == 2) _vzLoggedOnce = true;
        }
        bool missing = !CachedFileExists(e.FilePath);
        bool selected = _selectedPath == e.FolderPath;
        pool.Label.text = e.Title;
        pool.Label.color = missing ? new Color(0.75f, 0.75f, 0.75f, 0.55f) : Color.white;
        // 行底色只由 ButtonHover 写：它按「常态色 / 悬停 / 选中 / 按下」现算一次，
        // 这里再写一遍 Bg.color 就是同一个属性的第二个写手（谁先谁后全看调用顺序）。
        pool.Hover.Normal = missing ? MissColor : ItemColor;
        pool.Hover.IsSelected = selected;
        // 刚导入还没点开过的谱面：常态给一个提示色（鼠标压上去就让位给悬停色，见 ButtonHover.Apply）。
        // 提示色跟"选中"必须能一眼分开，所以走的是另一个字段，不是把 Normal 改掉 ——
        // 改成 Normal 的话悬停一移开就再也回不到提示色了。
        pool.Hover.IsNew = !missing && !selected && LevelScanner.IsNewImport(e.FolderPath);
        pool.Hover.ApplyInstant();   // 池换人：直接落位，别把上一任的选中粉当残影滑进这一任（见 ButtonHover 头注释）

        // 收藏 /
        bool isFav = LevelScanner.IsFavorite(e.FolderPath);
        SetFavIcon(pool.FavImg, isFav, IconMd);
        // 父节点 RectTransform 缓存一次（否则每行都要 GetComponent）
        if (pool.FavIconRt == null) {
            var fp = pool.FavImg.transform.parent;
            if (fp != null) pool.FavIconRt = fp.GetComponent<RectTransform>();
        }
        if (pool.FavIconRt != null) {
            // 星在行尾右侧（锚点=右边缘），位置 -30,25
            var favPos = pool.FavIconRt.anchoredPosition;
            if (favPos.x != -30f || favPos.y != 25f) pool.FavIconRt.anchoredPosition = new Vector2(-30f, 25f);
        }

        // 作者小字（从 .adofai 读 artist，读不到用 OggName）+ 长度限制防溢出
        // 性能：整条显示串（读盘 → 去富文本 → 截断）一起缓存，滚动时不再每行跑正则
        string author;
        if (!_authorDisplayCache.TryGetValue(e.FilePath, out author)) {
            author = GetCachedArtist(e.FilePath);
            if (string.IsNullOrEmpty(author)) author = e.OggName;
            author = StripRichText(author);
            if (!string.IsNullOrEmpty(author) && author.Length > 18) author = author.Substring(0, 18) + "…";
            if (author == null) author = "";
            _authorDisplayCache[e.FilePath] = author;
        }
        pool.Author.text = author;
        pool.Author.color = missing ? new Color(0.6f, 0.6f, 0.6f, 0.5f) : new Color(0.6f, 0.6f, 0.7f, 1f);

        // 曲名前两个图标：等级标（P/G/U/Q）在最前 + 判定标（宽/标/严）在其后，位置在曲名左侧
        if (pool.MarkIcon1 != null) {
            // MarkIcon1 = 等级标（最前）
            string mark = MarkStore.Get(e.FilePath);
            string markIcon = null;
            if (!string.IsNullOrEmpty(mark) && RankOf(mark) >= 0) markIcon = mark;   // 只认有效等级标（P/G/U/Q）
            var mspr = string.IsNullOrEmpty(markIcon) ? null : MarkStore.LoadIcon(markIcon);
            if (mspr != null) {
                pool.MarkIcon1.sprite = mspr;
                pool.MarkIcon1.type = Image.Type.Simple;
                pool.MarkIcon1.preserveAspect = true;
                pool.MarkIcon1.enabled = true;
                // 等级标放大 40x40，中心右移进框内
                pool.MarkIcon1.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            } else {
                pool.MarkIcon1.enabled = false;
            }
        }
        if (pool.MarkIcon2 != null) {
            // MarkIcon2 = 判定标（宽/标/严，通关自动打）：中心放在等级标右下角，重叠一点
            //（等级标 40x40 中心 (24,0)、右下角 (44,-20)；判定标 24x24 中心放那）；在等级标之后创建 = 渲染在上方
            string judge = MarkStore.GetJudge(e.FilePath);
            string judgeIcon = null;
            if (!string.IsNullOrEmpty(judge)) {
                judgeIcon = MarkStore.JudgeFile.TryGetValue(judge, out var jf) ? jf : judge;
            }
            var jspr = string.IsNullOrEmpty(judgeIcon) ? null : MarkStore.LoadIcon(judgeIcon);
            if (jspr != null) {
                pool.MarkIcon2.sprite = jspr;
                pool.MarkIcon2.type = Image.Type.Simple;
                pool.MarkIcon2.preserveAspect = true;
                pool.MarkIcon2.enabled = true;
                pool.MarkIcon2.rectTransform.anchoredPosition = new Vector2(36f, -14f);   // 从 -8 往下6（y 减小=往下）
            } else {
                pool.MarkIcon2.enabled = false;
            }
        }
        pool.Go.SetActive(true);
    }

    /// <summary>滚动/布局完成后更新可见行（虚拟化核心）</summary>
    private static void UpdateVisible() {
        var scroll = _libScroll;
        if (scroll == null || _poolRows.Count == 0) return;
        // 布局完成过就用缓存高度，不再每帧强制布局（ForceUpdateCanvases 很贵，滚动两端频繁触发会卡闪）；
        // 但视口高度可能中途变（窗口尺寸/分辨率一变，拉伸锚点会让视口自己长） 先跟真实高度比一下，对不上就作废重算，
        // 否则就是"列表变高了但最下面那一行没补出来"
        if (_listViewportH > 1f && scroll.viewport != null) {
            float nowH = scroll.viewport.rect.height;
            if (nowH > 1f && Mathf.Abs(nowH - _listViewportH) > 0.5f) {
                Lg.Info($"[LevelLib] 视口高度变了（{_listViewportH:F0} → {nowH:F0}）→ 重新补行");
                _listViewportH = 0f;
            }
        }
        if (_listViewportH <= 1f) {
            Canvas.ForceUpdateCanvases();
            float vh0 = scroll.viewport != null ? scroll.viewport.rect.height : 0f;
            if (vh0 <= 1f) return;   // 布局未完成（首帧）
            _listViewportH = vh0;
        }
        float vh = _listViewportH;
        // 滚动距离取绝对值：ScrollRect 滚动时 y 为负，恢复/跳转代码设的是正，两者都表示滚动了多远
        float visTop = Mathf.Abs(scroll.content.anchoredPosition.y);   // 视口顶部在 content 中的位置
        if (!_scrollTopLogged && scroll.verticalNormalizedPosition >= 0.999f) {
            _scrollTopLogged = true;
            Lg.Info($"[LevelLib] 滚到顶: norm={scroll.verticalNormalizedPosition:F4} contentY={scroll.content.anchoredPosition.y:F1} viewH={vh:F1} contentH={scroll.content.rect.height:F1} 首行offY={(_logicalRows.Count>0?_logicalRows[0].OffsetY:-1):F1}");
        }
        // 二分找第一个可见逻辑行（底边越过视口顶的行）
        int lo = 0, hi = _logicalRows.Count - 1, first = 0;
        while (lo <= hi) {
            int mid = (lo + hi) / 2;
            if (_logicalRows[mid].OffsetY + _logicalRows[mid].Height <= visTop) lo = mid + 1;
            else { first = mid; hi = mid - 1; }
        }
        int need = Mathf.CeilToInt(vh / Mathf.Min(ItemRowH, HeaderRowH)) + 6;   // 按较矮行估算 + 更大缓冲，避免两端池行不足抖动
        if (need > _poolRows.Count) need = _poolRows.Count;
        // 「谁在显示哪个逻辑行」的认领表：下标 = 逻辑行 − first，值 = 池行下标（-1 = 还没人显示）。
        // 只覆盖当前这一屏所需的区间，长度不超过池子大小。
        int last = Mathf.Min(_logicalRows.Count, first + need);
        int span = last - first;
        if (span <= 0) {                       // 列表空 / 滚过头：一根都不该留在屏上
            for (int k = 0; k < _poolRows.Count; k++) ReleasePoolRow(_poolRows[k]);
            return;
        }
        while (_libSlotOf.Count < span) _libSlotOf.Add(-1);
        for (int t = 0; t < span; t++) _libSlotOf[t] = -1;

        // 稳定分配：一根池行承载某个逻辑行之后，就让它一直承载到那个逻辑行滚出视口为止。
        // 早先这里是按池行下标对齐分配（_poolRows[i] 恒等于 first + i），滚动一行时
        // 每一根池行都要往下换一位：池行在内容坐标系里的位置因此整体挪了一整行，
        // 而 EventSystem 的指针进出事件比这里晚一帧才发出来 —— 中间那一帧里，
        // 原本压在指针下的那根已经被挪走、还带着悬停色，顶替它的那根又还没收到进入事件，
        // 于是悬停高亮闪一下、并且跟着上一根跑到下面去。滚动时每跨一行闪一次，
        // 实机看起来就是悬停高亮剧烈闪动。
        // 改成认领制之后，一次滚动只有滚出去的那一根池行需要换人。
        for (int k = 0; k < _poolRows.Count; k++) {
            var p = _poolRows[k];
            if (p.Offscreen) continue;
            int L = p.LogicalIndex;
            if (L >= first && L < last && _libSlotOf[L - first] < 0) _libSlotOf[L - first] = k;
            else ReleasePoolRow(p);          // 越界，或同一个逻辑行已经有主
        }
        // 补缺：区间里还没人显示的逻辑行，各取一根空闲池行
        for (int t = 0; t < span; t++) {
            if (_libSlotOf[t] >= 0) continue;
            int slot = -1;
            for (int k = 0; k < _poolRows.Count; k++) if (_poolRows[k].Offscreen) { slot = k; break; }
            if (slot < 0) break;             // 池子用尽（need 已按池子数钳过，正常到不了）
            UpdatePoolRow(_poolRows[slot], first + t);
        }
    }

    /// <summary>把一根池行移出视口，交还成空闲态。空闲的行保持激活、只把位置挪到很远处
    /// （viewport 有裁剪不会真绘制）：反复 SetActive 会带来整行重绘的闪烁，
    /// 而池子总共就二十四根，全部挂在那里最省事。
    /// 移出去之前必须清掉流式身份记录：那笔记录会在它离开视口之后继续写它的位置，
    /// 于是已经移出去的行又自己滑回列表里来。
    /// 同时清指针状态：行被挪走时指针的进出事件不一定会补发（拖拽惯性滑动时指针根本没动），
    /// 不清的话这根行滚回来时会自带悬停色、或者一直是压暗的。</summary>
    private static void ReleasePoolRow(PoolRow p) {
        if (p == null) return;
        if (!p.Go.activeSelf) p.Go.SetActive(true);
        if (!p.Offscreen) {
            FlowForgetRect(p.Rt);
            p.Key = null;
        }
        p.Offscreen = true;
        p.LogicalIndex = -1;
        p.Rt.anchoredPosition = new Vector2(0f, 100000f);
        p.Hover.ClearPointerState();
    }

    /// <summary>选中某一行（不切换取消版）：双击打开前先选中，保证左侧详情区显示的是这个谱面。
    /// 已经选中就什么都不做（避免"再点一次=取消选中"那种切换行为被带进来）。</summary>
    private static void SelectRow(LevelEntry entry) {
        if (entry == null) return;
        if (!File.Exists(entry.FilePath)) { ShowMissingDialog(entry.FilePath); return; }
        string folder = entry.FolderPath;
        if (_selectedPath == folder) return;   // 已选中
        string oldPath = _selectedPath;
        // 「刚导入」的提示到此为止：玩家点开它就说明已经看到这一行了，摘掉标记（顺带落盘）。
        // 必须在改 _selectedPath 之前清 —— 之后这一行就是选中态、提示色反正不显示，标记却还留着，
        // 下次开面板又会冒出来一个亮着的行。这里不单独重绘：下面 PaintRowByPath(folder) 会画这一行。
        if (LevelScanner.IsNewImport(folder)) LevelScanner.ClearNewImport(folder);
        _selectedPath = folder;
        if (!string.IsNullOrEmpty(oldPath)) PaintRowByPath(oldPath, !File.Exists(oldPath));
        PaintRowByPath(folder, false);
        RefreshDetailInfo(entry);
        RefreshDetailFileList(entry);
        if (_detailTab == 1) RefreshPickRecordsDetail(entry);   // 通关记录页现在是 tab1（前面插了去特效）
        else _pickRecordDirty = true;
        _fxPendingLevel = LevelScanner.ToRel(entry.FolderPath);   // 去特效页跟着换到这张谱面
        // 正停在「去特效」页时切谱面必须立刻重载它的配置 —— 之前只换 _fxPendingLevel 不刷新，
        // 页面上留着上一张谱面的开关状态，点一下别的 tab 回来才变（用户报"一会开一会关"）。
        if (_detailTab == 0 && _fxArea != null && _fxArea.activeSelf) RefreshDetailTabContent();
        // 选中谁也算"玩家主动选的"，当场记（否则记忆里还是旧位置，下次开面板又把它选上）
        NoteUiState();
        // 正停在"寄录图"tab 时，切谱面必须立刻重画热力图：热力图只在面板构建 / 点 tab 时刷新，
        // 从"没有寄录记录的谱面"切到"有记录的谱面"时图上会留着上一张的"还没有寄录，去玩一局吧喵"
        RefreshHeatIfVisible();
    }

    /// <summary>这个文件"像个谱面"吗：存在、够大、里面找得到谱面数据。
    /// ADOFAI 的角度数据有两种写法：新格式 angleData（数字数组）与老格式 pathData
    /// （一个长字符串，每个字符代表一个角度），只认前者会把老谱面全误拦（玩家只会觉得"谱面坏了"）；
    /// 而"两种都没有"的文件一个都没见过。所以要两种字段都认，且必须整份搜：
    /// angleData 通常在文件后部（settings/actions 在前面），只读前 64KB 会漏。</summary>
    private static bool LooksLikeLevelFile(string path, out string why) {
        why = "";
        try {
            if (string.IsNullOrEmpty(path)) { why = "路径是空的"; return false; }
            if (!File.Exists(path)) { why = "文件不在了"; return false; }
            var fi = new FileInfo(path);
            if (fi.Length < 32) { why = "文件太小（" + fi.Length + " 字节）"; return false; }
            // 整份搜（分块）：任一关键字段出现即通过
            const long MaxScan = 8L * 1024 * 1024;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs)) {
                var buf = new char[65536];
                string tail = "";              // 上一块的最后 16 字符，拼到下一块前面（防关键词被切两半）
                long read = 0;
                while (read < MaxScan) {
                    int n = sr.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    read += n;
                    string chunk = tail + new string(buf, 0, n);
                    if (chunk.IndexOf("angleData", System.StringComparison.Ordinal) >= 0
                        || chunk.IndexOf("pathData", System.StringComparison.Ordinal) >= 0) return true;
                    tail = chunk.Length > 16 ? chunk.Substring(chunk.Length - 16) : chunk;
                }
            }
            why = "里面既没有 angleData 也没有 pathData（不是有效谱面）";
            return false;
        } catch (System.Exception e) { why = "读取失败：" + e.GetType().Name; return false; }
    }

    /// <summary>行点击（带计时）：选中/取消选中一次到底花了多久 —— 只在明显卡（≥30ms）时记一行日志，
    /// 正常点击几毫秒不刷日志。用来定位"点几下就感觉卡"到底卡在哪。</summary>
    private static void OnRowClicked(PoolRow pool) {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try {
            OnRowClickedCore(pool);
        } finally {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 30.0)
                Lg.Info($"[LevelLib] 【点击】选中/取消切换 耗时={ms:F0}ms（≥30ms 才记录）");
        }
    }

    /// <summary>行点击：选中切换 / 弹 adofai 选择（虚拟化：运行时取当前逻辑行）</summary>
    private static void OnRowClickedCore(PoolRow pool) {
        // 这一下是拖列表，不是选谱面（判据见 _dragMoved 的注释）。
        // 顺手把另外两个守卫也消费掉：它们都是"一次性"标志，
        // 提前返回如果不消费，它们会留到下一次真正的点击上，把那次点击吃掉。
        if (_dragMoved) {
            _rowActionGuard = false;
            ConsumeContextMenuClick();
            return;
        }
        if (_rowActionGuard) { _rowActionGuard = false; return; }   // 行内按钮点击，跳过选中
        if (ConsumeContextMenuClick()) return;   // 这次点击是"关右键菜单"用的，别再顺手选中别的行
        var entry = CurrentEntry(pool);
        if (entry == null) return;
        if (!File.Exists(entry.FilePath)) {
            ShowMissingDialog(entry.FilePath);
            return;
        }
        string folder = entry.FolderPath;
        // 再点一次已选中的行 = 取消选中
        if (_selectedPath == folder) {
            _selectedPath = null;
            NoteUiState();   // 取消选中也要当场记（否则记的还是旧的，下次开面板又把它选上）
            CloseLevelPickDialog();
            PaintRowByPath(folder, false);
            ApplyDetailEmptyState();   // 切回空状态提示
            return;
        }
        string oldPath = _selectedPath;
        // 双击打开也算「点开过」：跟 SelectRow 同一套，在改 _selectedPath 之前把「新导入」标记摘掉，
        // 否则这条路上打开过的谱面下次开面板还会亮着（那是打开谱面最常用的入口，漏了最明显）。
        if (LevelScanner.IsNewImport(folder)) LevelScanner.ClearNewImport(folder);
        _selectedPath = folder;
        NoteUiState();   // 当场记
        if (!string.IsNullOrEmpty(oldPath)) PaintRowByPath(oldPath, !File.Exists(oldPath));
        PaintRowByPath(folder, false);
        // 刷新左侧详情区（固定区 + 选择关卡 tab 文件列表），默认选中第一个文件
        RefreshDetailInfo(entry);
        RefreshDetailFileList(entry);
        // 同时预热通关记录 tab 内容（选中即填充，切 tab 直接显示）
        if (_detailTab == 1) RefreshPickRecordsDetail(entry);   // 通关记录页现在是 tab1（前面插了去特效）
        else _pickRecordDirty = true;   // 标记待刷新：切到 tab1 时再填充
        _fxPendingLevel = LevelScanner.ToRel(entry.FolderPath);   // 去特效页跟着换到这张谱面
        // 正停在「去特效」页时切谱面必须立刻重载它的配置 —— 之前只换 _fxPendingLevel 不刷新，
        // 页面上留着上一张谱面的开关状态，点一下别的 tab 回来才变（用户报"一会开一会关"）。
        if (_detailTab == 0 && _fxArea != null && _fxArea.activeSelf) RefreshDetailTabContent();
        RefreshHeatIfVisible();         // 同上：停在寄录图 tab 时切谱面要立刻重画
    }

    /// <summary>当前正显示详情区的"寄录图"页时，按当前选中谱面重画热力图（热力图只在面板构建 / 点 tab
    /// 时刷新，切谱面不刷新就会残留上一张的内容）。
    /// 必须先 MakePickHeatArea（幂等）：图还没建过时（例如还停在下"先选择一个谱面喵"占位），
    /// 只调 RefreshHeatMap 会因为 _heatDots == null 直接返回、什么都不画；
    /// 建图那一步会先清空容器，顺带清掉占位文字。</summary>
    private static void RefreshHeatIfVisible() {
        try {
            if (_detailTab != 2 || _detailHeatArea == null || !_detailHeatArea.activeSelf) return;
            if (string.IsNullOrEmpty(_selectedPath)) { HideHeatChart(false); return; }
            MakePickHeatArea(_detailHeatArea);
            RefreshHeatMap(_selectedPath);
        } catch (System.Exception e) { Lg.Ex("切谱面刷新寄录图失败", e); }
    }

    /// <summary>按谱面路径重绘可见行（选中/缺失颜色）；行不在视口则跳过（滚动回来自然正确）</summary>
    private static void PaintRowByPath(string path, bool missing) {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var pool in _poolRows) {
            if (!pool.Go.activeSelf || pool.IsHeaderMode) continue;
            var e = CurrentEntry(pool);
            if (e == null || e.FolderPath != path) continue;
            bool selected = _selectedPath == path;
            pool.Label.color = missing ? new Color(0.75f, 0.75f, 0.75f, 0.55f) : Color.white;
            pool.Hover.Normal = missing ? MissColor : ItemColor;
            pool.Hover.IsSelected = selected;
            pool.Hover.IsNew = !missing && !selected && LevelScanner.IsNewImport(path);
            pool.Hover.Apply();
            return;
        }
    }

    /// <summary>更新统计行：谱面总数 + 通关数（有 X-acc 记录的谱面数）+ 总游玩数</summary>
    private static void UpdateCountText() {
        if (_countText == null) return;
        int total = LevelScanner.Entries.Count;
        int cleared = LevelScanner.CountXaccLevels();   // 通关数 = 有 X-acc 记录的谱面数
        int totalPlays = 0;
        foreach (var e in LevelScanner.Entries) {
            totalPlays += LevelScanner.GetPlays(e.FolderPath);
        }
        _countText.text = Lang.TrFmt("谱面总数：{0}    通关数：{1}    总游玩数：{2}", total, cleared, totalPlays);
    }

    /// <summary>等级序号：P1~P20=0~19，G1~G20=20~39，U1~U20=40~59；无等级/特殊标记返回 -1。
    /// 算法本体在 MarkStore.RankOf（"按标记难度排序"要用同一份），这里只留一行转发，调用方一个都不用改。</summary>
    private static int RankOf(string mark) { return MarkStore.RankOf(mark); }

    // ---- 字母定位的滚动动画 ----
    // 点字母是快速滚过去而不是瞬间跳：easeOutCubic 缓出，距离越远给的时间稍长（但封顶），
    // 用户一滚轮/一拖拽就立刻让位（CancelScrollAnimation）。
    private static bool _scrollAnim;
    private static float _scrollFromY;     // 起点（content.anchoredPosition.y）
    private static float _scrollToY;       // 终点
    private static float _scrollAnimT;     // 已用时间
    private static float _scrollAnimDur;   // 总时长

    /// <summary>取消正在跑的定位动画（用户手动滚动/拖拽时调用）。
    /// 吸附动画"落定后选中"那步也一并取消：动画被打断就等于没落定，不许再补选</summary>
    private static void CancelScrollAnimation() {
        _scrollAnim = false;
        _listSnapSelectOnFinish = false;
    }

    private static void ScrollToLetter(string letter) {
        var scroll = _libScroll;
        if (scroll == null) {
            Lg.Info($"[LevelLib] 跳转失败：找不到 ScrollRect");
            return;
        }
        if (!_groupOffsets.TryGetValue(letter, out float targetY)) {
            Lg.Info($"[LevelLib] 跳转失败：字母 {letter} 没有标头（共 {_groupOffsets.Count} 组）");
            return;
        }
        Canvas.ForceUpdateCanvases();
        float maxY = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
        targetY = Mathf.Clamp(targetY, 0f, maxY);
        Lg.Info($"[LevelLib] 跳转 {letter} -> y={targetY:F0} (max={maxY:F0})");
        StartScrollAnimation(scroll, targetY);
    }

    /// <summary>开始一段"快速滚到目标位置"的动画。
    /// durOverride&lt;0 按距离套字母定位那档公式（0.16~0.40s，干脆利落）；
    /// 居中吸附传自己的时长（更长）与更绵的缓出指数（见 LevelList.Snap.cs）</summary>
    private static void StartScrollAnimation(ScrollRect scroll, float targetY, float durOverride = -1f, float easePow = 3f) {
        if (scroll == null || scroll.content == null) return;
        _scrollFromY = scroll.content.anchoredPosition.y;
        _scrollToY = targetY;
        // 距离决定时长：近的干脆利落(0.16s)，远的也就 0.40s 封顶，不会拖沓
        float dist = Mathf.Abs(_scrollToY - _scrollFromY);
        _scrollAnimDur = durOverride > 0f ? durOverride : Mathf.Clamp(0.16f + dist / 9000f, 0.16f, 0.40f);
        _scrollAnimT = 0f;
        _scrollAnimEasePow = easePow;
        _scrollAnim = true;
        scroll.StopMovement();   // 别让 ScrollRect 自己的惯性插一脚
    }

    /// <summary>这一趟定位动画的缓出指数（TickScrollAnimation 用）：
    /// 3 = easeOutCubic（字母定位那档），5 = 五次缓出（吸附收尾更绵、慢慢停稳）</summary>
    private static float _scrollAnimEasePow = 3f;

    /// <summary>定位动画每帧驱动</summary>
    private static void TickScrollAnimation() {
        if (!_scrollAnim) return;
        var scroll = _libScroll;
        if (scroll == null || scroll.content == null) { _scrollAnim = false; return; }
        _scrollAnimT += Time.unscaledDeltaTime;
        float k = _scrollAnimDur > 0f ? Mathf.Clamp01(_scrollAnimT / _scrollAnimDur) : 1f;
        float e = 1f - Mathf.Pow(1f - k, _scrollAnimEasePow);   // 缓出：字母定位 3 次、吸附 5 次（收尾更绵）
        float y = Mathf.Lerp(_scrollFromY, _scrollToY, e);
        scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, y);
        UpdateVisible();   // 滚动中同步刷新可见行（跟滚轮/拖拽一致）
        if (k >= 1f) {
            // 落点对齐到目标值，避免累积误差留半像素
            scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, _scrollToY);
            _scrollAnim = false;
            ListSnapAfterProgrammaticScroll();   // 程序发起的定位（字母跳转/居中吸附）收工后不再连环吸附，等下一次用户滚动
        }
    }

}
