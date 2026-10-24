using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LevelLibrary.Effects;

namespace LevelLibrary;

/// <summary>去特效的绘制：详情区那个 tab 的内容，以及「详细去特效」全屏界面。
/// 全部构件（开关 / 按钮 / 文本 / 滚动区）都复用面板既有的那套，样式与设置页一致。</summary>
public static partial class PortalUI {

    // ---- 尺寸常量（几何都在这里定）----
    private const float FxRowH = 44f;
    /// <summary>全屏界面上方的小子 tab 尺寸（仿设置页那套）。宽度按英文译文最长的
    /// 「Track Events」留：7 个子 tab 一行 = 7×150 + 6×8 = 1098，仍在 1200 的条里。</summary>
    private const float FxSubBtnW = 150f, FxSubBtnH = 34f, FxSubBtnGap = 8f;
    /// <summary>全屏界面左侧主 tab 的尺寸。宽度按英文「Level Settings」留（132 会把字挤出去）。</summary>
    private const float FxMainBtnW = 152f, FxMainBtnH = 46f, FxMainBtnGap = 8f;
    /// <summary>详情区/设置页那排预设按钮的尺寸与间距。宽度按详情区的可用宽（806 - 2×24 = 758）反推：
    /// 四套内置预设要一排放得下（4×176 + 3×10 + 行内边距 4 = 738），再宽就会探出详情区（用户报过）。
    /// 176 是给英文「Remove Decorations」留的，中文四套在这个宽度下更宽裕。</summary>
    private const float FxPresetBtnW = 176f, FxPresetBtnH = 36f, FxPresetGap = 10f;
    /// <summary>预设行容器宽度还没算出来时的兜底可用宽度。</summary>

    // ================= 详情区 tab 的内容 =================

    /// <summary>把详情区「去特效」页刷新一遍（切到这一页时、开关改动后都调）。</summary>
    private static void RefreshFxTabContent() {
        if (_fxArea == null) return;
        for (int i = _fxArea.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(_fxArea.transform.GetChild(i).gameObject);
        if (_fxState == null) FxBeginEditLevel(_fxPendingLevel);
        FxDrawSummary(_fxArea.transform);
    }

    /// <summary>详情区那一页：单独去特效开关 + 四套预设（多选高亮）+ 详细按钮。</summary>
    private static void FxDrawSummary(Transform parent) {
        var inner = new GameObject("FxInner", typeof(RectTransform), typeof(VerticalLayoutGroup));
        inner.transform.SetParent(parent, false);
        var vg = inner.GetComponent<VerticalLayoutGroup>();
        vg.spacing = 14f;
        vg.childForceExpandHeight = false;
        vg.childForceExpandWidth = true;
        var irt = inner.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0f, 1f); irt.anchorMax = new Vector2(1f, 1f);
        irt.pivot = new Vector2(0.5f, 1f);
        irt.sizeDelta = new Vector2(0f, 280f);

        // ① 单独去特效（总开关）
        var enRow = FxMakeRow(inner.transform, "单独去特效");
        var enBtn = MakeSwitch(enRow.transform, "Sw");
        PlaceRight(enBtn.GetComponent<RectTransform>());
        ApplySwitch(enBtn.GetComponent<Image>(), SwitchKnobOf(enBtn), _fxEnabled);
        enBtn.onClick.AddListener(() => {
            _fxEnabled = !_fxEnabled;
            ApplySwitch(enBtn.GetComponent<Image>(), SwitchKnobOf(enBtn), _fxEnabled);
            FxCommit();
            FxRefreshPresetButtons();
        });

        // ② 预设（可多选，叠加生效）—— 标签一行、按钮一行（布局组排，不怕首帧量不到宽度）
        _fxDetailPresetRow = FxMakePresetRow(inner.transform, "预设（可多选，叠加生效）").GetComponent<RectTransform>();
        FxRefreshPresetButtons();

        // ③ 详细去特效
        var btnRow = FxMakeRow(inner.transform, "");
        var detail = MakeButton(btnRow.transform, "Detail", Vector2.zero, new Vector2(220f, 40f), new Color(0.30f, 0.42f, 0.72f, 1f));
        PlaceRight(detail.GetComponent<RectTransform>());
        var dt = MakeText(detail.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
        dt.text = Lang.Tr("详细去特效");
        Stretch(dt.rectTransform, Vector2.zero, Vector2.one);
        detail.onClick.AddListener(() => {
            // 打开前强制重载当前谱面的配置：预设/开关的每次改动都即时 FxCommit 落盘了，重载无损，
            // 但能保证 _fxState 与编辑目标严格配套（切过谱面后旧状态不会跟进来）。
            FxBeginEditLevel(_fxPendingLevel);
            FxOpenScreen();
        });

        if (_fxState != null && !_fxState.AnyOn) {
            // 提示语按正文档 0.72：原来写的 0.62 落在 0.55~0.70 的空档上，既不是占位符档也不是正文档
            var tip = MakeText(inner.transform, "Tip", 22, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleLeft);
            tip.text = Lang.Tr("当前没有开启任何去特效项");
            var trt = tip.rectTransform;
            // 交给布局组排（上边距 10），不写死 anchoredPosition —— 预设按钮换行后行数会变，
            // 写死的 y 会跟实际位置错开。
            var tle = tip.gameObject.AddComponent<LayoutElement>();
            tle.preferredHeight = 30f; tle.minHeight = 30f;
        }
    }

    /// <summary>一个标签行（标签在左、内容贴在右）。返回这一行。</summary>
    private static GameObject FxMakeRow(Transform parent, string label) {
        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        // 行高写进 LayoutElement：垂直布局组不认子物体的 sizeDelta，只认 LayoutElement。
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = FxRowH; le.minHeight = FxRowH;
        if (!string.IsNullOrEmpty(label)) {
            var t = MakeText(row.transform, "L", 24, Color.white, TextAnchor.MiddleLeft);
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(0.62f, 1f);
            trt.offsetMin = new Vector2(4f, 0f); trt.offsetMax = Vector2.zero;
            t.text = label;
        }
        return row;
    }

    /// <summary>把控件贴到这一行的右端（竖直居中）。</summary>
    private static void PlaceRight(RectTransform rt) {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>重建预设那一行的按钮（勾中的高亮）。按钮交给行内的 HorizontalLayoutGroup 排 ——
    /// 之前手摆坐标要读容器宽度，首次构建那一帧宽度还没算出来，整排按钮一个都不显示（用户报过）。</summary>
    private static void FxRefreshPresetButtons() {
        var row = FxActivePresetRow;
        if (row == null) return;
        for (int i = row.childCount - 1; i >= 0; i--) Object.Destroy(row.GetChild(i).gameObject);
        var names = new List<string>();
        for (int i = 0; i < EffectPresets.BuiltinCount; i++) names.Add(EffectPresets.BuiltinName(i));
        foreach (var kv in EffectPresets.UserPresets) names.Add(kv.Key);
        for (int i = 0; i < names.Count; i++) {
            string nm = names[i];
            bool hi = FxPresetHighlighted(nm);
            var b = MakeButton(row, "P" + i, Vector2.zero, new Vector2(FxPresetBtnW, FxPresetBtnH),
                               hi ? new Color(0.36f, 0.72f, 0.45f, 1f) : ItemColor);
            // 字号走标尺的 20（原来是 21，不在八档上；同文件其余按钮文字都是 20）
            var t = MakeText(b.transform, "T", 20, Color.white, TextAnchor.MiddleCenter);
            t.text = nm;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            string captured = nm;
            b.onClick.AddListener(() => { FxTogglePreset(captured); FxRefreshPresetButtons(); });
        }
    }

    /// <summary>预设区的两行结构（详情区版）：标签独占一行，按钮单独一行交给 HorizontalLayoutGroup。
    /// 返回按钮行（挂进 FxDetailPresetRow）。设置页的行是 SetAtPx 绝对定位体系，走 MakeRowAt + FxAttachPresetLayout。</summary>
    private static GameObject FxMakePresetRow(Transform parent, string label) {
        FxMakeRow(parent, label);
        var btnRow = FxMakeRow(parent, "");
        FxAttachPresetLayout(btnRow);
        return btnRow;
    }

    /// <summary>给一行挂上"按钮横排"的布局组。详情区与设置页的行容器体系不同（一个竖排布局、一个绝对定位），
    /// 但按钮行内部的排法是同一套。</summary>
    private static void FxAttachPresetLayout(GameObject btnRow) {
        var hg = btnRow.AddComponent<HorizontalLayoutGroup>();
        hg.spacing = FxPresetGap;
        hg.childAlignment = TextAnchor.MiddleLeft;
        hg.childControlWidth = false; hg.childControlHeight = false;
        hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;
        hg.padding = new RectOffset(4, 0, 0, 0);
    }

    // ================= 全屏详细界面 =================

    /// <summary>打开「详细去特效」全屏界面。</summary>
    private static void FxOpenScreen() {
        if (_fxScreen != null) return;
        if (_fxState == null) FxBeginEditLevel(_fxPendingLevel);
        var canvas = MakeCanvas("LevelLib_Effects", 82100);
        _fxScreen = canvas.gameObject;
        _fxScreenCanvas = canvas;      // Esc 关闭链按 Canvas 引用认窗口（见 PortalUI.EscClose）
        FxBuildScreen(canvas.transform);
        _fxScreenBuiltKey = "";      // 强制下次重建子 tab
        FxRefreshScreen(false);
    }

    private static void FxCloseScreen() {
        if (_fxScreen == null) return;
        // 收窗走与入场同一条轨迹的反向（往下滑 + 盒子/遮罩/整组一起淡出），放完再真销毁。
        // 先把引用摘下来并立刻清状态：Esc 与关闭键都不会再进来第二次（_fxScreen 已空 = 直接 return）。
        var root = _fxScreen;
        var box = _fxScreenBox;
        var veil = _fxScreenVeil;
        _fxScreen = null; _fxScreenCanvas = null;
        _fxScreenBox = null; _fxScreenVeil = null;
        _fxMainBar = null; _fxSubBar = null; _fxContentInner = null; _fxContentGroup = null;
        var grp = box != null ? box.GetComponent<CanvasGroup>() : null;
        if (box == null || grp == null) { Object.Destroy(root); return; }
        FloatAnimator.FloatOut(box.rectTransform, box, veil, grp, DialogInRise, DialogInSeconds,
            () => { try { Object.Destroy(root); } catch { } });
    }

    private static void FxBuildScreen(Transform root) {
        var veil = MakeDialogVeil(root);
        _fxScreenVeil = veil;                    // 收窗淡出要用（见 FxCloseScreen）

        // 盒子走 MakeDialogBox：与其他弹窗同一套（磨砂自动挂、入场自带"从下方浮上来 + 淡入"，
        // 之前手写 MakeImage 是一帧硬切，也没接磨砂链）。
        var box = MakeDialogBox(root, "Box", new Color(0.07f, 0.07f, 0.10f, 0.985f));
        _fxScreenBox = box;
        var brt = box.rectTransform;
        brt.anchorMin = new Vector2(0.06f, 0.05f); brt.anchorMax = new Vector2(0.94f, 0.95f);
        brt.offsetMin = brt.offsetMax = Vector2.zero;

        var title = MakeText(box.transform, "Title", 30, Color.white, TextAnchor.MiddleLeft);
        title.text = Lang.Tr("详细去特效");
        // SetAtPx 的 top 语义是"距父级顶部多少"（内部自己取负）：传负数会反过来往上挪出界。
        // 之前传 -18，标题半截悬在盒子顶边外面（用户截图）。
        SetAtPx(title.rectTransform, 26f, 18f, 400f, 40f);

        var close = MakeButton(box.transform, "Close", Vector2.zero, new Vector2(96f, 40f), new Color(0.55f, 0.22f, 0.22f, 1f));
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-20f, -18f);
        crt.sizeDelta = new Vector2(96f, 40f);
        var ct = MakeText(close.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
        ct.text = Lang.Tr("关闭");
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one);
        close.onClick.AddListener(() => { FxCommit(); FxCloseScreen(); });

        // 左侧主 tab（同上：top 传正值 = 距盒子顶部 70，之前传 -70 整条 tab 被顶出屏幕外）
        _fxMainBar = MakeImage(box.transform, "MainBar", new Color(1f, 1f, 1f, 0f));
        var mrt = _fxMainBar.rectTransform;
        mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 1f);
        mrt.pivot = new Vector2(0f, 1f);
        SetAtPx(mrt, 20f, 70f, FxMainBtnW, 3f * (FxMainBtnH + FxMainBtnGap));

        // 上方小子 tab
        _fxSubBar = MakeImage(box.transform, "SubBar", new Color(1f, 1f, 1f, 0f));
        var srt2 = _fxSubBar.rectTransform;
        srt2.anchorMin = srt2.anchorMax = new Vector2(0f, 1f);
        srt2.pivot = new Vector2(0f, 1f);
        SetAtPx(srt2, 20f + FxMainBtnW + 14f, 70f, 1200f, FxSubBtnH);

        // 内容滚动区（外壳 = 可视区，内容另挂一层）
        var shell = MakeImage(box.transform, "Content", new Color(1f, 1f, 1f, 0f));
        var shrt = shell.rectTransform;
        shrt.anchorMin = new Vector2(0f, 0f); shrt.anchorMax = new Vector2(1f, 1f);
        shrt.offsetMin = new Vector2(20f + FxMainBtnW + 14f, 20f);
        shrt.offsetMax = new Vector2(-20f, -70f - FxSubBtnH - 10f);
        shell.gameObject.AddComponent<RectMask2D>();
        var scroll = shell.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = shrt;

        var content = new GameObject("Inner", typeof(RectTransform));
        content.transform.SetParent(shell.transform, false);
        _fxContentInner = content.GetComponent<RectTransform>();
        _fxContentInner.anchorMin = new Vector2(0f, 1f); _fxContentInner.anchorMax = new Vector2(1f, 1f);
        _fxContentInner.pivot = new Vector2(0.5f, 1f);
        _fxContentInner.sizeDelta = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f; vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        // 切主/子 tab 时内容淡入用（重建是瞬变，淡一层才不生硬）
        _fxContentGroup = content.AddComponent<CanvasGroup>();
        scroll.content = _fxContentInner;
    }

    /// <summary>全屏界面刷新：主 tab 高亮 + 子 tab 重建 + 内容重画。
    /// fade = 这次刷新要不要给内容来一层淡入（切 tab 传 true；开窗那次整体已有浮入动画，传 false）。</summary>
    private static void FxRefreshScreen(bool fade) {
        if (_fxScreen == null || _fxMainBar == null || _fxSubBar == null || _fxContentInner == null) return;
        if (_fxState == null) return;

        // 左侧主 tab
        for (int i = _fxMainBar.transform.childCount - 1; i >= 0; i--) Object.Destroy(_fxMainBar.transform.GetChild(i).gameObject);
        for (int i = 0; i < EffectCatalog.CategoryNames.Length; i++) {
            int mi = i;
            bool sel = i == _fxMainTab;
            var b = MakeButton(_fxMainBar.transform, "M" + i, Vector2.zero, new Vector2(FxMainBtnW, FxMainBtnH),
                               sel ? new Color(0.30f, 0.45f, 0.78f, 1f) : ItemColor);
            SetAtPx(b.GetComponent<RectTransform>(), 0f, i * (FxMainBtnH + FxMainBtnGap), FxMainBtnW, FxMainBtnH);
            var t = MakeText(b.transform, "T", 22, Color.white, TextAnchor.MiddleCenter);
            t.text = EffectCatalog.CategoryNames[i];
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            b.onClick.AddListener(() => { _fxMainTab = mi; _fxSubGroup = 0; _fxScreenBuiltKey = ""; FxRefreshScreen(true); });
        }

        // 上方小子 tab。重建判据要含 _fxSubGroup：只按分类集合作 key 的话，切子 tab 不重建、
        // 选中高亮停在旧的那颗上（用户报"切换 tab 选中提示没切换"）。按钮就几颗，重建没有代价。
        var cat = (EffectCategory)_fxMainTab;
        var groups = EffectCatalog.GroupsOf(cat);
        if (_fxSubGroup >= groups.Length) _fxSubGroup = 0;
        string key = _fxMainTab + "|" + _fxSubGroup + "|" + string.Join("|", groups);
        if (key != _fxScreenBuiltKey) {
            _fxScreenBuiltKey = key;
            for (int i = _fxSubBar.transform.childCount - 1; i >= 0; i--) Object.Destroy(_fxSubBar.transform.GetChild(i).gameObject);
            for (int i = 0; i < groups.Length; i++) {
                int gi = i;
                bool sel = i == _fxSubGroup;
                var b = MakeButton(_fxSubBar.transform, "S" + i, Vector2.zero, new Vector2(FxSubBtnW, FxSubBtnH),
                                   sel ? new Color(0.30f, 0.45f, 0.78f, 1f) : ItemColor);
                SetAtPx(b.GetComponent<RectTransform>(), i * (FxSubBtnW + FxSubBtnGap), 0f, FxSubBtnW, FxSubBtnH);
                var t = MakeText(b.transform, "T", 20, Color.white, TextAnchor.MiddleCenter);
                t.text = groups[i];
                Stretch(t.rectTransform, Vector2.zero, Vector2.one);
                b.onClick.AddListener(() => { _fxSubGroup = gi; FxRefreshScreen(true); });
            }
        }

        // 内容：当前主分类 + 子分类下的开关
        for (int i = _fxContentInner.childCount - 1; i >= 0; i--) Object.Destroy(_fxContentInner.GetChild(i).gameObject);
        string want = groups.Length > 0 ? groups[Mathf.Clamp(_fxSubGroup, 0, groups.Length - 1)] : "";
        foreach (var it in _fxState.Items) {
            if (it.Category != cat) continue;
            if (want.Length > 0 && it.GroupKey != want) continue;
            FxDrawEffectRow(_fxContentInner, it);
        }
        // 切 tab 的过渡：内容淡入一层（快速连点时 FadeIn 的版本号会打断上一次，不会叠影）
        if (fade && _fxContentGroup != null) FloatAnimator.FadeIn(_fxContentGroup, 0.16f);
    }

    /// <summary>一个去特效开关行（标签 + 开关；带值的再加一个可点的取值框）。</summary>
    private static void FxDrawEffectRow(Transform parent, EffectItem it) {
        var row = new GameObject("E_" + it.Key, typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var rle = row.GetComponent<LayoutElement>();
        rle.preferredHeight = FxRowH;
        rle.minHeight = FxRowH;

        var t = MakeText(row.transform, "L", 22, Color.white, TextAnchor.MiddleLeft);
        var trt = t.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(0.5f, 1f);
        trt.offsetMin = new Vector2(4f, 0f); trt.offsetMax = Vector2.zero;
        t.text = it.Label;

        // 带值的项：标签右边一个可点的取值框（点一下轮换候选值，不引输入法）
        if (it.Kind != EffectKind.Off) {
            var inp = MakeButton(row.transform, "In", Vector2.zero, new Vector2(190f, 30f), new Color(1f, 1f, 1f, 0.10f));
            var irt = inp.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(190f, 30f);
            var vt = MakeText(inp.transform, "T", 20, Color.white, TextAnchor.MiddleCenter);
            vt.text = FxValueText(it);
            Stretch(vt.rectTransform, Vector2.zero, Vector2.one);
            string vkey = it.Key;
            inp.onClick.AddListener(() => {
                FxCycleValue(vkey);
                var tt = inp.transform.GetComponentInChildren<Text>();
                if (tt != null && _fxState != null) tt.text = FxValueText(_fxState.Get(vkey));
                FxOnSwitchToggled(vkey);     // 改了预设相关的值 → 那套预设自动取消勾选
            });
        }

        var sw = MakeSwitch(row.transform, "Sw");
        PlaceRight(sw.GetComponent<RectTransform>());
        ApplySwitch(sw.GetComponent<Image>(), SwitchKnobOf(sw), it.On);
        string sk = it.Key;
        sw.onClick.AddListener(() => {
            if (_fxState == null) return;
            var cur = _fxState.Get(sk);
            if (cur == null) return;
            cur.On = !cur.On;
            ApplySwitch(sw.GetComponent<Image>(), SwitchKnobOf(sw), cur.On);
            FxOnSwitchToggled(sk);
            if (FxActivePresetRow != null) FxRefreshPresetButtons();   // 预设高亮跟着变
        });
    }

    private static string FxValueText(EffectItem it) {
        if (it == null) return "";
        switch (it.Kind) {
            case EffectKind.Text: return it.TextValue ?? "";
            case EffectKind.Int: return it.IntValue.ToString();
            case EffectKind.Double: return it.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case EffectKind.Bool: return it.BoolValue ? "开" : "关";
            case EffectKind.Position: return it.XValue.ToString("0.#") + "," + it.YValue.ToString("0.#");
            default: return "";
        }
    }

    /// <summary>点取值框：值型项在几个常用值之间轮换。</summary>
    private static void FxCycleValue(string key) {
        var it = _fxState != null ? _fxState.Get(key) : null;
        if (it == null) return;
        switch (it.Kind) {
            case EffectKind.Text: {
                var opts = FxTextOptions(it.Key);
                if (opts.Length > 0) {
                    int cur = System.Array.IndexOf(opts, it.TextValue);
                    it.TextValue = opts[(cur + 1) % opts.Length];
                }
                break;
            }
            case EffectKind.Bool:
                it.BoolValue = !it.BoolValue;
                break;
            case EffectKind.Double:
                it.DoubleValue = it.DoubleValue >= 400 ? 0 : it.DoubleValue * 2;
                break;
            case EffectKind.Int:
                it.IntValue = it.IntValue >= 200 ? 0 : it.IntValue * 2;
                break;
        }
        it.ValueTouched = true;
    }

    /// <summary>文本型项的候选值。</summary>
    private static string[] FxTextOptions(string key) {
        switch (key) {
            case "trackColorType": return new[] { "Single", "ByBeat", "ByFloor" };
            case "trackStyle": return new[] { "Standard", "Big", "Cubic", "Inverted" };
            case "trackColor":
            case "secondaryTrackColor":
            case "backgroundColor":
            case "defaultBGTileColor":
            case "defaultBGShapeColor":
            case "defaultTextColor":
            case "defaultTextShadowColor":
                return new[] { "000000", "222222", "404040", "808080", "FFFFFF", "DEBB7B", "7BDEBB" };
            case "trackAnimation":
            case "trackDisappearAnimation": return new[] { "None", "Pulse", "Telegraph", "Wave" };
            case "defaultBGShapeType": return new[] { "Disabled", "Floor", "Outline" };
            case "relativeTo": return new[] { "Player", "Planet", "Center", "Top", "Bottom" };
            case "planetEase": return new[] { "None", "Start", "Free", "Out" };
            case "planetEasePartBehavior": return new[] { "Independent", "Linked", "Shared" };
            case "hitsound": return new[] { "Kick", "Snare", "Hat", "Clap" };
            default: return new string[0];
        }
    }
}
