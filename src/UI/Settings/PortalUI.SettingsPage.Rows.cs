// [split-of] PortalUI.SettingsPage.cs lines 1024-1392 body 17-351

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

/// <summary>PortalUI.SettingsPage.cs 的一部分（同一个 partial 类，从该文件拆分而来）</summary>
public static partial class PortalUI {
    /// <summary>「更新」页最后那块状态文字区的高度（三行 22 号字、行距 1.22 ≈ 80px，留余量取 120）。
    /// 跟建行那一段里的用法是一对，改一个要跟着改另一个。</summary>
    private const float UpdStatusBoxH = 120f;

    /// <summary>按 y 游标顺序摆放一行（位置由游标决定，不依赖布局组）。subIdx 标记它属于哪个子 tab。
    /// 行的可见宽度收窄到 RowW，控制件只贴"行"的右边而不是功能区最右边，标签到控制件的距离才够短。</summary>
    /// <summary>组与组之间的额外留白：语义上成组的行（行为 / 模型 / 声音 / 位置）之间空一档，
    /// 一整块平铺读起来累。默认 0，不传的页面一个像素都不变。</summary>
    private const float RowGroupGap = 28f;

    /// <summary>设置页的一行：记住它属于哪个子 tab、建页时落在哪、上面要空多少。
    /// 光有 GameObject 不够：「某一行被开关带出来 / 收回去」时要按这几项把后面的行重排一遍。</summary>
    private sealed class SetRow {
        public int SubIdx;                 // 属于哪个子 tab
        public GameObject Go;
        public RectTransform Rt;
        public float BuildY;               // 建页时落的 y（重排的基准，只有本页第一行这个值还准）
        public float GapBefore;            // 这一行上面空的组间距
        public float CurY;                 // 现在摆在哪（重排时比它决定要不要动）
        public bool Hidden;                // 被自己的开关藏起来了（不占位置）
        public System.Func<bool> Gate;     // 非空 = 这一行由这个条件决定露不露
    }

    /// <summary>本页行的重排结果（每帧不新建列表，避免 GC）</summary>
    private static readonly List<SetRow> _rowFlow = new List<SetRow>();
    private static readonly List<float> _rowFlowY = new List<float>();

    private static GameObject MakeRowAt(Transform parent, string label, ref float y, int subIdx = 0, float gapBefore = 0f,
                                        System.Func<bool> gate = null) {
        y += gapBefore;                                  // 这一行上面先空出来（组间距）
        var row = new GameObject("Row_" + label + "#" + subIdx, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rt = row.GetComponent<RectTransform>();
        SetAtPx(rt, 0f, y, RowW, RowH);
        _setRows.Add(new SetRow {
            SubIdx = subIdx, Go = row, Rt = rt, BuildY = y, GapBefore = gapBefore, CurY = y, Gate = gate
        });
        y += RowH + SetRowGap;
        var l = MakeText(row.transform, "L", FontRow, Color.white, TextAnchor.MiddleLeft);
        l.text = label;
        var lrt = l.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
        // 标签占左侧、右边留够控制件的位置（这个值就是"标签→控制件"的空隙来源）
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = new Vector2(-RowCtrlZone, 0f);
        return row;
    }

    /// <summary>重算每一行的显隐：所属主 tab + 子 tab 都匹配、并且没被自己的开关藏起来才露。
    /// 返回"有没有哪一行的显隐真的变了"（变了才需要重排 + 让位）。</summary>
    private static bool ApplyRowVisibility() {
        if (_setRows.Count == 0 || _setPageObjs.Count == 0) return false;
        int mt = Mathf.Clamp(_setMainTab, 0, _setPageObjs.Count - 1);
        var page = _setPageObjs[mt];
        bool changed = false;
        for (int i = 0; i < _setRows.Count; i++) {
            var r = _setRows[i];
            if (r == null || r.Go == null) continue;
            if (r.Gate != null) r.Hidden = !r.Gate();
            bool inMain = page != null && r.Go.transform.parent != null && r.Go.transform.parent.gameObject == page;
            bool want = inMain && r.SubIdx == _setSubTab && !r.Hidden;
            if (r.Go.activeSelf != want) { r.Go.SetActive(want); changed = true; }
        }
        return changed;
    }

    /// <summary>把当前页的行按"露出来的行依次往下排"重算一遍 y，位置变化走流式让位。
    /// 基准 = 本页第一行建页时的 y：建页那根 y 游标只有第一行那个值在行增减之后还能信。</summary>
    private static void RelayoutSettingsRows() {
        if (_setRows.Count == 0 || _setPageObjs.Count == 0) return;
        int mt = Mathf.Clamp(_setMainTab, 0, _setPageObjs.Count - 1);
        var page = _setPageObjs[mt];
        if (page == null) return;
        _rowFlow.Clear();
        _rowFlowY.Clear();
        float y = 0f;
        bool haveBase = false;
        int moves = 0;
        for (int i = 0; i < _setRows.Count; i++) {
            var r = _setRows[i];
            if (r == null || r.Go == null || r.Rt == null) continue;
            if (r.Go.transform.parent == null || r.Go.transform.parent.gameObject != page) continue;
            if (!haveBase) { y = r.BuildY; haveBase = true; }
            if (!r.Go.activeSelf) continue;              // 藏起来的行一个像素都不占
            y += r.GapBefore;
            if (Mathf.Abs(r.CurY - y) > 0.01f) moves++;
            _rowFlow.Add(r);
            _rowFlowY.Add(y);
            y += RowH + SetRowGap;
        }
        if (moves == 0) return;
        FlowWindow("设置页行增减", MoSmall);
        for (int i = 0; i < _rowFlow.Count; i++) {
            var r = _rowFlow[i];
            r.CurY = _rowFlowY[i];
            FlowTo(r.Rt, 0f, -_rowFlowY[i]);
        }
    }

    /// <summary>行内右侧开关（开关的位置/尺寸/配色一个都不在这里摆，全在 `MakeSwitchVisual` 一处）。
    /// 落点跟 `RowButton` 同一列齐平（右边缘在行右边缘往内 10px），所以标签区 `RowCtrlZone` 不用改。</summary>
    private static Button SettingsToggleRow(GameObject row, out Button toggleBtn, System.Action onClick) {
        var btn = MakeSwitch(row.transform, "Tg");
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        toggleBtn = btn;
        return btn;
    }

    /// <summary>刷新设置页：主 tab 高亮 + 内容显隐 + 子 tab</summary>
    private static void RefreshSettingsPage() {
        // 边界保护：页面列表为空时直接返回（避免 _setPageObjs[_setMainTab] 越界）
        if (_setPageObjs.Count == 0) return;
        int mt = Mathf.Clamp(_setMainTab, 0, _setPageObjs.Count - 1);
        _setMainTab = mt;
        BuildSubTabsFor(mt);
        for (int i = 0; i < _setPageObjs.Count; i++) {
            if (_setPageObjs[i] != null) _setPageObjs[i].SetActive(i == mt);
        }
        ApplyRowVisibility();                            // 所属主 tab + 子 tab 都匹配才显示
        // 选中项文字用深蓝黑：它压在实心淡蓝块上，白字会糊。
        // extra 那个标签不在这里上色 —— 未选中时归彩虹循环 `TickRainbowTexts` 管，选中时必须压掉彩虹色（亮块上读不清）
        for (int i = 0; i < _setNavLabels.Count; i++) {
            var lb = _setNavLabels[i];
            if (lb == null) continue;
            if (i == mt) lb.color = HighlightTextColor;
            else if (lb != _setExtraNavLabel) lb.color = new Color(1f, 1f, 1f, 0.62f);
        }
        SetNavFrameTo(mt, false);
        // 子 tab 的选中块：重建那种情况在 `BuildSubTabsFor` 里已经直接就位，这里再设一次目标 = 同一个位置、不会动
        SetSubFrameTo(_setSubTab, false);
        // 更新检查状态行：切页时刷一次（真正的实时刷新在 TickSettingsPage 里，每帧都刷）
        RefreshUpdaterStatusLine();
        // 子 tab 文字：选中项现在压在一块实心淡蓝上，所以切成深蓝黑（跟主 tab 的 `HighlightTextColor` 同一个色）
        for (int i = 0; i < _setSubButtons.Count; i++) {
            var lb = _setSubButtons[i] != null ? _setSubButtons[i].GetComponentInChildren<Text>() : null;
            if (lb != null) lb.color = i == _setSubTab ? HighlightTextColor : new Color(1f, 1f, 1f, 0.62f);
        }
        RefreshSettingsToggles();
        RelayoutSettingsRows();                          // 被开关藏起来的行不占位置，露出来的行让位
        if (LogVerbose) Lg.Info($"[LevelLib] 设置页刷新: 主tab={mt} 子tab={_setSubTab} 子tab数={_setSubButtons.Count} 行数={_setRows.Count}");
    }

    /// <summary>这个画布是不是真的还在屏幕上（Esc 分支判断用）。
    /// 光判 `!= null` 不够：引用还在、但对象已经被隐藏（或不是 activeInHierarchy）时，
    /// 分支会吃掉 Esc 却什么都不关 → 玩家连按几下都退不出去。</summary>
    private static bool IsAliveOnScreen(Canvas c) {
        try { return c != null && c.gameObject != null && c.gameObject.activeInHierarchy; } catch { return false; }
    }

    /// <summary>更新状态行（设置 → 工具 → 更新）：不用切 tab 逼它重画。三行 = mod 版本 / 资源包版本 / 状态。
    /// 下载/安装中的措辞跟着当前正在处理的那条线走（`Updater.CurrentJobIsAsset`）。
    /// 两道闸防 GC：0.2 秒节流（进度数字照样够"实时"）、先比动态部分的短指纹，没变就直接返回
    /// —— 以前这里每帧拼整段文字 ≈ 每秒几百 KB 临时字符串，是"设置页开着"时最稳定的一处 GC 来源。</summary>
    private static float _updStatusNextAt;
    private static string _updStatusKey;

    private static void RefreshUpdaterStatusLine() {
        if (!_settingsPageVisible || _updStatusText == null) return;
        float now = Time.unscaledTime;
        if (now < _updStatusNextAt) return;
        _updStatusNextAt = now + 0.2f;

        string line = Updater.Status;
        bool assetJob = Updater.CurrentJobIsAsset;
        string what = assetJob ? "正在更新资源包" : "正在更新 mod";
        // 还没拿到下载直链就是一个字节都没下，别写成"下载 x%"（实测这段能等二十几秒）
        if (Updater.Phase == 2 && Updater.FetchingUrl) line = what + "… 正在连接更新服务器（还没开始下载）";
        else if (Updater.Phase == 2) line = $"{what}… 下载 {Mathf.RoundToInt(Updater.Progress * 100f)}%";
        else if (Updater.Phase == 3) line = $"{what}… 安装 {Mathf.RoundToInt(Updater.Progress * 100f)}%（后台跑，不卡顿）";
        // 短指纹：动态部分没变就不重建整段
        string key = Updater.Phase + "|" + Mathf.RoundToInt(Updater.Progress * 100f) + "|" + line + "|"
                   + Updater.ReadLocalVersion() + "|" + Updater.LocalAssetVersionText;
        if (key == _updStatusKey) return;
        _updStatusKey = key;

        string text = "mod 版本：" + Updater.ReadLocalVersion()
            + "\n资源包版本：" + Updater.LocalAssetVersionText
            + "\n状态：" + line;
        if (text == _updStatusShown) return;
        _updStatusShown = text;
        _updStatusText.text = text;
    }

    /// <summary>已建的开关登记项：按钮 + 取值函数</summary>
    private sealed class ToggleReg {
        public Button Btn;
        public System.Func<bool> Get;
    }

    /// <summary>本页建过的所有开关。每次建开关都通过 BindToggle 自动登记，刷新时统一遍历，
    /// 就不会再出现"漏登记 → 功能生效但按钮文字永远显示开"的坑。</summary>
    private static readonly List<ToggleReg> _toggleRegs = new List<ToggleReg>();

    /// <summary>刷新「屏保休眠时间」按钮上的字（点一下换档之后调用）
    /// —— 它不是开关，进不了开关登记表（那张表只存 bool），所以单独一个刷新函数</summary>
    private static void RefreshSaverIdleButton() {
        if (_saverIdleBtnText != null) _saverIdleBtnText.text = SaverIdleText();
    }

    /// <summary>刷新开关按钮的显示状态：走登记表，新增开关不用再单独往这里加一行</summary>
    private static void RefreshSettingsToggles() {
        for (int i = 0; i < _toggleRegs.Count; i++) {
            var r = _toggleRegs[i];
            if (r == null || r.Btn == null || r.Get == null) continue;   // 已销毁的按钮 Unity 侧 == null，跳过
            bool on;
            try { on = r.Get(); } catch { continue; }
            Init(r.Btn, on);
        }
    }

    /// <summary>开关按钮的"取反"绑定：点击后保存并刷新显示（同时登记，供统一刷新）</summary>
    private static void BindToggle(Button b, System.Func<bool> get, System.Action<bool> setVal) {
        if (b == null) return;
        _toggleRegs.Add(new ToggleReg { Btn = b, Get = get });
        b.onClick.AddListener(() => {
            bool nv = !get();
            setVal(nv);
            AutoFeatures.Save();
            // 顺序是先起步动画、再统一刷新：反过来的话，统一刷新那句会走 `ApplySwitch(..., animate:false)`
            // 把这个开关当场拍到位，刚起步的动画当场结束，还是闪现。
            // （`ApplySwitch` 见到"正在滑"的开关只会改目标、不会拍到位，所以这两句的次序才是关键。）
            AnimateSwitch(b, nv);
            RefreshSettingsToggles();
            // 这个开关可能带出 / 收起它下面那几行（音量、随机发言、游玩时隐藏）：显隐真变了才让位
            if (ApplyRowVisibility()) RelayoutSettingsRows();
        });
    }

    /// <summary>把开关的显示同步到给定值。
    /// 开关是胶囊 + 圆钮，不写「开 / 关」两个字（状态由圆钮在哪一边 + 轨道颜色表达），
    /// 所以这里只有"颜色 + 圆钮位置"两件事，而且都交给 `ApplySwitch` 这一个出口。</summary>
    private static void Init(Button b, bool on) {
        if (b == null) return;
        ApplySwitch(b.GetComponent<Image>(), SwitchKnobOf(b), on);
    }

    /// <summary>开发者页自己的遮罩渐入每帧驱动（设置页那层已删掉，四个 tab 统一用面板的全局遮罩，所以这里只管开发者页）。
    /// tab 编号：0=库 1=成就 2=设置 3=开发者。</summary>
    private static void TickPageVeil() {
        if (_devVeilImg == null) return;
        float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
        float target = (_topTab == 4 && _devPage != null && _devPage.activeInHierarchy) ? DevVeilTarget : 0f;
        _devVeilCur = Mathf.Lerp(_devVeilCur, target, k);
        if (Mathf.Abs(_devVeilCur - target) < 0.004f) _devVeilCur = target;
        _devVeilImg.color = new Color(0f, 0f, 0f, _devVeilCur);
    }

    /// <summary>离开页面时把它的遮罩立即清零（防止回到库页后残留一层重复遮罩）</summary>
    private static void ClearPageVeils(int exceptTab) {
        if (exceptTab != 4 && _devVeilImg != null) {   // 4 = 开发者页（主页插在库前面，编号整体 +1）
            _devVeilCur = 0f;
            _devVeilImg.color = new Color(0f, 0f, 0f, 0f);
        }
    }

    private static void TickHotkeyButtons() {
        if (_hotkeyBtnOpen != null) {
            string want = CaptureTarget == "open" ? "请按键…" : KeyDisplay(OpenLevelKey) + "（点击修改）";
            if (want != _hotkeyShownOpen) {
                _hotkeyShownOpen = want;
                var t = _hotkeyBtnOpen.GetComponentInChildren<Text>();
                if (t != null) t.text = want;
            }
        }
        if (_hotkeyBtnRandom != null) {
            string want = CaptureTarget == "random" ? "请按键…" : KeyDisplay(RandomLevelKey) + "（点击修改）";
            if (want != _hotkeyShownRandom) {
                _hotkeyShownRandom = want;
                var t = _hotkeyBtnRandom.GetComponentInChildren<Text>();
                if (t != null) t.text = want;
            }
        }
    }

    /// <summary>设置页显隐</summary>
    private static void SetSettingsPageVisible(bool on) {
        _settingsPageVisible = on;
        if (_settingsPage == null) return;
        _settingsPage.SetActive(on);
        // Bg / Veil 始终保留在最底层（负责背景图与压暗），库页内容在设置页打开时隐藏，
        // 顶部 tab 栏始终在最上层，设置页固定插在 Veil 之后。
        if (_contentRoot != null) _contentRoot.gameObject.SetActive(!on);
        if (_settingsPage.transform.parent == _panelCanvas.transform) {
            // 不要动这个顺序：设置页必须排在页面带之前，否则整页会被库页盖住。
            // 切到设置页时把页面带隐藏（下面 SetActive(!on) 那行）就是全部手段，不要再改图层。
            // 索引也跟着 Veil 走：写死 2 的话，视频背景那层会把 Veil 挤到 2，
            // 设置页反而被顶到 Veil 上面、被 0.45 黑罩盖住。
            _settingsPage.transform.SetSiblingIndex(BgLayerInsertIndex());
        }
        if (_topTabRoot != null) _topTabRoot.transform.SetAsLastSibling();
        // 切页时一次性校准遮罩（不做每帧检测）
        SyncVeilToTab(false);
        // 不要再把 Veil 提到最上层！它必须紧随背景图，否则会盖住谱面列表/详情面板，把整个界面压暗
        var g0 = _settingsPage.GetComponent<CanvasGroup>();
        if (g0 != null) {
            _settingsPageGroup = g0;   // 存住给 TickSettingsPage 每帧用（见字段注释）
            g0.alpha = 1f;            // 直接设为不透明（淡入只作为可选效果，先保证看得见）
            g0.interactable = true;
            g0.blocksRaycasts = true;
        }
        _setFade = 1f;
        _setFadeTicks = 0;
        if (LogVerbose) Lg.Info($"[LevelLib] 设置页显隐={on} active={_settingsPage.activeInHierarchy} alpha={(g0 != null ? g0.alpha : -1f):F2} 子节点={_settingsPage.transform.childCount}");
    }

    private static void TickSettingsPage() {
        if (!_settingsPageVisible || _settingsPage == null) return;
        _setFadeTicks++;
        // CanvasGroup 建页时挂一次就再没动过（见 BuildSettingsPage），每帧 GetComponent 是白找。
        // 拿不到时报同样的错、同样早退 —— 行为与改前一致。
        var g = _settingsPageGroup;
        if (g == null) { g = _settingsPage.GetComponent<CanvasGroup>(); _settingsPageGroup = g; }
        if (g == null) { ReportError("设置页 CanvasGroup 丢失（设置页可能不可见）"); return; }
        // 淡入只有 0.15 秒（_setFade 到 1 就不再动 alpha），进这段之前不必碰 alpha / 不必找 g。
        if (_setFade < 1f) {
            _setFade = Mathf.Min(1f, _setFade + Time.unscaledDeltaTime / 0.15f);
            g.alpha = _setFade;
            // 兜底：若 3 秒后仍未完全显示，强制设为不透明（避免因异常导致页面永远全透明）
            if (_setFadeTicks == 180 && g.alpha < 0.99f) {
                g.alpha = 1f; _setFade = 1f;
                Lg.Info("设置页淡入兜底：强制 alpha=1");
            }
        }
        // 更新页的进度数字要自己实时动，别逼玩家切界面才刷新
        RefreshUpdaterStatusLine();
    }

    /// <summary>一行「标签 + 开关」（紧凑行；开关长相/落位同样全在 `PortalUI.Switch.cs` 一处）</summary>
    private static GameObject SettingsToggleRow(Transform parent, string label, float rowH, out Button toggleBtn) {
        var row = new GameObject("Row_" + label, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = rowH;
        var l = MakeText(row.transform, "L", FontRow, Color.white, TextAnchor.MiddleLeft);
        l.text = label;
        l.horizontalOverflow = HorizontalWrapMode.Overflow;
        var lrt = l.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = new Vector2(-260f, 0f);
        toggleBtn = MakeSwitch(row.transform, "Tg");
        return row;
    }

    /// <summary>遮罩透明度跟着「背景压暗」滑条走（设置 → 外观，BgVeilPercent 持久化）；
    /// tab 编号在这里其实不影响结果，留参数只是为了以后真要区分时好改。</summary>
    private static float VeilTargetFor(int tab) {
        return Mathf.Clamp(PortalUI.BgVeilPercent, 0, 100) / 100f;
    }

    /// <summary>设置遮罩目标透明度并启动渐变</summary>
    private static void SetVeilTarget(float target, bool instant = false) {
        _veilTarget = Mathf.Clamp01(target);
        if (instant) { _veilCur = _veilTarget; ApplyVeil(); }
        else _veilAnimating = true;
    }

    /// <summary>遮罩渐变每帧驱动</summary>
    private static void TickVeilFade() {
        if (!_veilAnimating || _panelVeilImg == null) return;
        float k = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
        _veilCur = Mathf.Lerp(_veilCur, _veilTarget, k);
        if (Mathf.Abs(_veilCur - _veilTarget) < 0.004f) {
            _veilCur = _veilTarget;
            _veilAnimating = false;   // 到位即停，之后不再有任何开销
            if (LogVerbose) Lg.Info($"[LevelLib] 遮罩渐变结束: 最终值={_veilCur:F2} 顶层tab={_topTab}");
        }
        ApplyVeil();
    }

    /// <summary>按当前顶层 tab 强制校准遮罩（只在切 tab / 切页时调用一次，不做每帧检测）</summary>
    private static void SyncVeilToTab(bool instant) {
        float want = VeilTargetFor(Mathf.Clamp(_topTab, 0, 4));
        _veilTarget = want;
        if (instant) {
            _veilCur = want;
            _veilAnimating = false;
            ApplyVeil();
        } else {
            _veilAnimating = true;
        }
        if (LogVerbose) Lg.Info($"[LevelLib] 遮罩校准: 目标={want:F2} 当前={_veilCur:F2} 顶层tab={_topTab} 立即={instant}");
    }

    /// <summary>销毁设置页（设置是顶层 tab 的页面，不再有独立 Canvas）</summary>
    private static void CloseSettingsPage() {
        if (_settingsPage != null) Object.Destroy(_settingsPage);
        _settingsPage = null;
        _settingsKeys = null;
        _topTabBuilt = -1;   // 允许下次切回来重新构建
        // 必须清空所有静态引用列表：销毁后列表里残留"已销毁对象"，再次打开设置时内容全部失效
        //（表现为"只剩 tab、功能全不见"）
        _setPageObjs.Clear();
        _toggleRegs.Clear();   // 开关登记表也要清（按钮已销毁，留着会越攒越多）
        _setRows.Clear();
        _setNavLabels.Clear();
        _setSubButtons.Clear();
        _setPageRoot = null;
        _setNavCol = null;
        _setBodyRt = null;
        _setNavFrameRt = null;
        // 子 tab 那一族含"当前是哪一组名字"的守卫键：不清的话下次开面板会以为子 tab 行还在，一个都不重建
        _setSubBar = null;
        _setSubSlide.Rt = null;
        _setSubBuiltKey = null;
        // 信息栏拖动那两个"滑条刷新入口"也放掉（它们捕获的控件已经销毁了；下次建设置页会重新登记）
        ClearPlayerBoxSliders();
        // extra 那个导航标签也随页面销毁（它同时挂在 `_rainbowTexts` 里，下一帧会被自动清掉）
        _setExtraNavLabel = null;
        _devVeilImg = null;
        _setMainTab = 0;
        _setSubTab = 0;
        CaptureTarget = null;   // 停止快捷键捕获
        RefreshKeyHintBar();
        Lg.Info("设置页已关闭并清空全部引用（下次打开会重新构建）");
    }

}
