using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LevelLibrary.Effects;

namespace LevelLibrary;

/// <summary>去特效界面的公共部分：68 个开关的绘制、预设按钮、以及与状态的同步。
/// 库页详情区那个 tab 与设置页那个全局 tab 都用这里，两处共用同一套画法。</summary>
public static partial class PortalUI {

    // ================= 状态 =================

    /// <summary>当前在编辑的那份设置（详情区 = 单谱面，设置页 = 全局）。</summary>
    private static EffectState _fxState;
    /// <summary>「单独去特效」总开关。</summary>
    private static bool _fxEnabled;
    /// <summary>当前勾选的预设名（可多选）。</summary>
    private static readonly List<string> _fxSelectedPresets = new List<string>();
    /// <summary>编辑中的谱面文件夹相对路径；空 = 编辑全局。</summary>
    private static string _fxEditingLevel;

    /// <summary>全屏详细界面（详情区那个「详细去特效」按钮打开的）。</summary>
    private static GameObject _fxScreen;
    /// <summary>同一个界面的 Canvas 引用，给 Esc 关闭链认窗口用。</summary>
    private static Canvas _fxScreenCanvas;
    /// <summary>收窗淡出要用的盒子与遮罩（建窗时存下；收窗动画期间 _fxScreen 已置空，靠它们撑到动画放完）。</summary>
    private static Image _fxScreenBox, _fxScreenVeil;
    /// <summary>当前选中谱面（详情区待编辑的对象）；空 = 没选中。</summary>
    private static string _fxPendingLevel;
    /// <summary>详情区「去特效」页的容器与那一行预设按钮的挂点。</summary>
    private static GameObject _fxArea;
    /// <summary>预设按钮行的挂点。详情区与设置页各有一个 —— 之前只有一个字段，
    /// 两处互相覆盖：在设置页改开关会刷到详情区那一行，而自己的行是空的（用户报过设置页空白）。</summary>
    private static RectTransform _fxDetailPresetRow, _fxGlobalPresetRow;

    /// <summary>当前该刷新哪一处的预设按钮（详情区 or 设置页）。</summary>
    private static bool FxEditingGlobal { get { return string.IsNullOrEmpty(_fxEditingLevel); } }

    /// <summary>当前编辑目标对应的那一行预设按钮。</summary>
    private static RectTransform FxActivePresetRow {
        get { return FxEditingGlobal ? _fxGlobalPresetRow : _fxDetailPresetRow; }
    }
    /// <summary>全屏界面的几个挂点。</summary>
    private static Image _fxMainBar, _fxSubBar;
    private static RectTransform _fxContentInner;
    /// <summary>内容区的透明度组：切 tab 时内容淡入用。</summary>
    private static CanvasGroup _fxContentGroup;
    private static string _fxScreenBuiltKey = "";
    /// <summary>全屏界面当前选中的主分类 / 子分类。</summary>
    private static int _fxMainTab, _fxSubGroup;
    /// <summary>设置页那页只建一次（重建页面时才重置）。</summary>
    private static bool _fxGlobalInit;
    private static Transform _fxGlobalRoot;
    /// <summary>设置页那个总开关，刷新时用它把状态画回去。</summary>
    private static UnityEngine.UI.Button _fxGlobalSwitch;

    // ================= 建/取状态 =================

    /// <summary>开始编辑某张谱面的去特效。relPath 为空 = 详情区还没选中谱面 ——
    /// 这时绝不是"编辑全局"：全局是设置页那一处专属的编辑目标。
    /// 之前两种空态混用一个入口，详情区未选谱面时 FxCommit 会把勾选写进全局
    /// （实机上 effects_global.json 被写成了开 + 两套预设的并集，用户报"全局默认是开的"）。</summary>
    private static void FxBeginEditLevel(string relPath) {
        _fxEditingLevel = relPath ?? "";
        _fxEditingGlobalSession = false;     // 详情区这一路永远没有写全局的资格
        var prof = string.IsNullOrEmpty(_fxEditingLevel) ? null : EffectStore.GetLevel(_fxEditingLevel);
        _fxEnabled = prof != null && prof.Enabled;
        _fxState = EffectStore.BuildState(prof);
        FxBackfillSelectedPresets();
        _fxMainTab = 0;
        _fxSubGroup = 0;
    }

    /// <summary>开始编辑全局（设置页那个 tab 专属）。全局是唯一允许空编辑目标的入口，
    /// 用 _fxEditingGlobalSession 把"允许写全局"钉死在这一路，别的路空目标一律不落盘。</summary>
    private static void FxBeginEditGlobal() {
        _fxEditingLevel = "";
        _fxEditingGlobalSession = true;
        var prof = EffectStore.Global;
        _fxEnabled = prof != null && prof.Enabled;
        _fxState = EffectStore.BuildState(prof);
        FxBackfillSelectedPresets();
        _fxMainTab = 0;
        _fxSubGroup = 0;
    }

    /// <summary>打开已有配置时，把"它已经全开"的内置预设回填成勾选态（界面与数据一致）。</summary>
    private static void FxBackfillSelectedPresets() {
        _fxSelectedPresets.Clear();
        if (_fxState == null) return;
        for (int i = 0; i < EffectPresets.BuiltinCount; i++) {
            var n = EffectPresets.BuiltinName(i);
            if (EffectPresets.IsPresetAllOn(_fxState, n)) _fxSelectedPresets.Add(n);
        }
    }

    /// <summary>是否处于"设置页正在编辑全局"的会话里。详情区那一路永远置 false。</summary>
    private static bool _fxEditingGlobalSession;

    /// <summary>把编辑目标强行切到全局，并把界面刷新成全局那份（设置页每次进 tab 都调）。</summary>
    private static void FxSyncGlobalView() {
        if (_fxGlobalPresetRow == null) return;
        if (!FxEditingGlobal || !_fxEditingGlobalSession) FxBeginEditGlobal();
        FxRefreshPresetButtons();
        if (_fxGlobalSwitch != null && _fxState != null) {
            ApplySwitch(_fxGlobalSwitch.GetComponent<Image>(), SwitchKnobOf(_fxGlobalSwitch), _fxEnabled);
        }
    }

    /// <summary>把界面上的改动收回去落盘。空编辑目标且不在全局会话里 = 没有目标，不落盘 ——
    /// 没处写的改动宁可丢了，也不能冒充全局（那就是"全局自己开了"的来源）。</summary>
    private static void FxCommit() {
        if (_fxState == null) return;
        if (string.IsNullOrEmpty(_fxEditingLevel)) {
            if (!_fxEditingGlobalSession) return;
            var g = EffectStore.ToProfile(_fxState, _fxEnabled);
            EffectStore.Global = g;
            EffectStore.SaveGlobal();
            EffectInjector.Pending = (_fxEnabled && g.Enabled && _fxState.AnyOn) ? _fxState : null;
            return;
        }
        var prof = EffectStore.ToProfile(_fxState, _fxEnabled);
        EffectStore.SetLevel(_fxEditingLevel, prof);
        FxPublishToInjector();
    }

    /// <summary>把编辑中的状态交给注入端（下次加载谱面时按它改写）。</summary>
    private static void FxPublishToInjector() {
        var st = _fxState;
        if (st == null) { EffectInjector.Pending = null; return; }
        bool on = _fxEnabled && st.AnyOn;
        // 无论开关开没开都要发：开着就发状态，关着必须显式发 null ——
        // 之前只在 on 时赋值，于是"关掉去特效"对已经排好的 Pending 完全无效（用户报过设了没反应）。
        EffectInjector.Pending = on ? st : null;
    }

    // ================= 预设勾选（多选、叠加、互不干涉） =================

    /// <summary>点预设：开→把它那批打开；关→只关它自己那批。
    /// 其余各套都取并集效果，所以打开时是叠加上去的。</summary>
    private static void FxTogglePreset(string name) {
        if (_fxState == null || string.IsNullOrEmpty(name)) return;
        if (_fxSelectedPresets.Contains(name)) {
            _fxSelectedPresets.Remove(name);
            EffectPresets.RemoveOne(_fxState, name);
        } else {
            _fxSelectedPresets.Add(name);
            var keys = EffectPresets.KeysOf(name);
            _fxState.ApplyPreset(keys);     // 只开它列的那些，其余不碰
        }
        FxCommit();
    }

    /// <summary>手动改某个开关：把"管着这一项的预设"从勾选列表里摘掉（用户要求的行为）。</summary>
    private static void FxOnSwitchToggled(string key) {
        if (_fxState == null) return;
        var it = _fxState.Get(key);
        if (it == null) return;
        if (_fxSelectedPresets.Count > 0) {
            var kept = EffectPresets.DropPresetsTouching(_fxSelectedPresets, key);
            _fxSelectedPresets.Clear();
            _fxSelectedPresets.AddRange(kept);
        }
        FxCommit();
    }

    private static bool FxPresetHighlighted(string name) {
        return _fxSelectedPresets.Contains(name);
    }

    /// <summary>设置页第 0 页（全局去特效）的内容：总开关 + 四套预设 + 详细入口。
    /// 与详情区那页共用同一份状态逻辑（FxTogglePreset / FxOnSwitchToggled / FxCommit），
    /// 只是这里的编辑目标是 EffectStore.Global 而不是某张谱面。</summary>
    private static void BuildFxGlobalPage(Transform parent, ref float y) {
        // 设置页每次关闭都会把整棵页面销毁、下次打开重建。_fxGlobalInit 只代表"上一次构建过"，
        // 旧引用已经全部失效 —— 这时必须整页重造，否则页面上什么都没有（用户报过两次"设置里去特效空白"）。
        if (_fxGlobalInit && _fxGlobalRoot == null) {
            _fxGlobalInit = false;
            _fxGlobalRoot = null;
            _fxGlobalSwitch = null;
            _fxGlobalPresetRow = null;
        }
        if (_fxGlobalInit) { FxSyncGlobalView(); return; }
        _fxGlobalInit = true;
        FxBeginEditGlobal();          // 空 = 编辑全局
        _fxGlobalRoot = parent;

        var enRow = MakeRowAt(parent, Lang.Tr("全局去特效（默认关闭）"), ref y, 0);
        var enBtn = MakeSwitch(enRow.transform, "Sw");
        _fxGlobalSwitch = enBtn;
        ApplySwitch(enBtn.GetComponent<Image>(), SwitchKnobOf(enBtn), _fxEnabled);
        enBtn.onClick.AddListener(() => {
            // 这一页的每次改动都必须先切回全局目标：详情区选过谱面后 _fxEditingLevel 是那张谱面，
            // 不切回来的话全局开关会被写进谱面（用户报过"改了没记录"）。
            if (!FxEditingGlobal) FxBeginEditGlobal();
            _fxEnabled = !_fxEnabled;
            ApplySwitch(enBtn.GetComponent<Image>(), SwitchKnobOf(enBtn), _fxEnabled);
            FxCommit();
            FxRefreshPresetButtons();
        });

        // 预设区同样两行：标签一行、按钮一行。设置页的行必须走 MakeRowAt（SetAtPx 绝对定位）——
        // 之前错用了详情区那套裸行（锚点默认父级中心），按钮整排飞到页面右下（用户截图）。
        MakeRowAt(parent, Lang.Tr("预设（可多选，叠加生效）"), ref y, 0, 14f);
        var preBtnRow = MakeRowAt(parent, "", ref y, 0, 0f);
        FxAttachPresetLayout(preBtnRow);
        _fxGlobalPresetRow = preBtnRow.GetComponent<RectTransform>();
        FxRefreshPresetButtons();

        var btnRow = MakeRowAt(parent, Lang.Tr("详细去特效"), ref y, 0, 14f);
        var detail = RowButton(btnRow, Lang.Tr("打开详细设置"), ItemColor, () => { FxBeginEditGlobal(); FxOpenScreen(); });   // 打开前切回全局会话，详细窗改的才是全局
    }

}
