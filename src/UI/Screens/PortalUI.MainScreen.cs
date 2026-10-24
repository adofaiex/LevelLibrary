using UnityEngine;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：主界面（大厅）—— 入口与生命周期。
/// 本文件只管"这一屏什么时候建、什么时候拆、Esc 怎么退"，一个元素都不摆。
/// 界面本体在 PortalUI.ArkView.cs（按桌面那份 ArknightsUI-React-Template 的示意图重建）。
/// 它仍是页面带上的一个独立槽位（MainScreenSlot），不是盖在别的页上的覆盖层，切页时整条带走。
/// Esc 链：任何页面 Esc 退回大厅，大厅再 Esc 才关掉 mod。</summary>
public static partial class PortalUI {

    /// <summary>现在是不是停在大厅（Esc 链靠它决定"退到大厅"还是"关掉 mod"）</summary>
    public static bool MainScreenShowing { get { return _topTab == MainScreenSlot; } }

    /// <summary>大厅建过了没有（没建过就不进每帧逻辑，避免空引用）</summary>
    private static bool _arkBuilt;

    public static bool ArkBuilt { get { return _arkBuilt; } }

    /// <summary>切到大厅：就是切到那个槽位（动画/遮罩/滑动全走现成的）</summary>
    public static void ShowMainScreen() {
        try {
            if (!_panelOpen || _contentRoot == null) return;
            if (_topTab != MainScreenSlot) SwitchTopTab(MainScreenSlot);
            else EnsureArkBuilt();   // 已经在大厅就别重建（重建会闪一下又重头渐入）
            Lg.Info("主界面（大厅）：已切过去");
        } catch (System.Exception e) { Lg.Ex("显示主界面失败", e); }
    }

    /// <summary>不带滑动动画地切到大厅。个人页渐隐完那一下要用它：那一刻先把带子瞬移过来、再淡回来，
    /// 否则玩家先看到别的页的内容闪一下。</summary>
    public static void ShowMainScreenInstant() {
        try {
            if (!_panelOpen || _contentRoot == null) return;
            if (_topTab != MainScreenSlot) SwitchTopTabInstant(MainScreenSlot);
            else EnsureArkBuilt();
            ArmArkFade();
        } catch (System.Exception e) { Lg.Ex("瞬移显示主界面失败", e); }
    }

    /// <summary>兼容旧调用：大厅是个槽位，离开它等于切到别的槽位，这里什么都不用做</summary>
    public static void HideMainScreen() { }

    /// <summary>本局是否已经自动进过主界面（每次开游戏只自动进一次，之后靠 Esc）</summary>
    private static bool _mainScreenEnteredThisSession;

    // 整屏渐入：登录之后那一屏慢慢亮起来。时长比个人页渐隐长，两段交叉着来才顺。
    private const float ArkFadeSeconds = 1.0f;
    private static CanvasGroup _mainGroup;
    private static bool _mainFading;
    private static float _mainFadeT;

    /// <summary>武装渐入（首帧从 0 起）。只武装不重建：重建会让整屏闪没再从头渐入一遍。</summary>
    private static void ArmArkFade() {
        try {
            if (_mainGroup == null) return;
            _mainGroup.alpha = 0f;
            _mainFadeT = 0f;
      _mainFading = true;
   } catch { }
    }

    /// <summary>每帧推渐入。单帧步进要钳住，跟面板的进出场同一个口径（见 UiFadeMaxStep）。</summary>
    private static void TickArkFade() {
        try {
            if (!_mainFading || _mainGroup == null) return;
            _mainFadeT += Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);
float tc = Mathf.Clamp01(_mainFadeT / ArkFadeSeconds);
            float ease = tc * tc * (3f - 2f * tc);   // smoothstep
            _mainGroup.alpha = ease;
            if (tc >= 1f) { _mainFading = false; _mainGroup.alpha = 1f; }
  } catch { }
    }

    /// <summary>关面板时把大厅也按同一条曲线推淡。
    /// 为什么要它：大厅是独立画布（排序 81000），不受面板 CanvasGroup 影响，
    /// 关面板时面板自己淡 1 到 0、大厅却还是全亮，等 ClosePanel() 落下去那一刻整块被 enabled = false 收掉 ——
    /// 表现就是"内容直接消失，很突兀"。
    /// 这里不另起一套计时：progress 直接用面板那条 0 到 1 的进度，两者天然同步，
    /// 调的时候由 TickCore 的退出动画段每帧喂（走完才 ClosePanel，所以淡出一定能推完）。
    /// 单帧步进不用再钳：progress 本身已经是钳过的量。</summary>
    private static void TickArkCloseFade(float progress) {
        try {
            if (_mainGroup == null) return;
            _mainFading = false;                       // 关闭淡出期间不再让渐入那条路插进来写 alpha
            _mainGroup.alpha = 1f - Mathf.Clamp01(progress);
        } catch { }
    }

    /// <summary>收摊时把大厅的透明度复位成 1。
    /// 关闭淡出会把 alpha 推到 0，而大厅画布是场景根对象、不随面板销毁，下次开面板时
    /// 如果不重建就走不到 ArmArkFade（ShowMainScreen 在已经停在大厅时只 EnsureArkBuilt），
    /// alpha 会一直停在 0 —— 那下次进来就是一屏全透明的空白。
    /// 这里只在收摊那一刻复位，不管当前是不是正在淡出：收摊就是要收干净。</summary>
    private static void ResetArkAlpha() {
        try {
            if (_mainGroup != null) _mainGroup.alpha = 1f;
        } catch { }
        _mainFading = false;
        _mainFadeT = 1f;
    }

    /// <summary>确保大厅建过；建过一次就别重建（重建会闪）。</summary>
    public static void EnsureArkBuilt() {
        // 画布没了就算没建过：相机与画布都是场景根对象，换场景（进关卡）会被 Unity 销毁，
        // 只认 _arkBuilt 的话下次进来就是"以为建过、其实什么都没有"的永久空白。
        if (_arkBuilt && _arkCanvas != null) return;
        BuildArkView();
    }

    /// <summary>拆大厅：先判断"这一局有没有建过"，没建过直接返回（早退路径上 ClearHubRefs 会空引用）。
    /// 内部那些引用由 PortalUI.ArkView.cs 的 DisposeArkRefs 自己清。</summary>
    public static void DisposeArk() {
        if (!_arkBuilt) return;
        // 清引用之前先把画布关掉：DisposeArk 一跑 _arkCanvas 就是 null，外面再也关不掉它，
        // 画布会以 enabled 状态留在场景里（透明但接着吃射线，实机踩过「关面板后点不动」）。
        // 在这里兜住，调用方就不用记「先关画布再 Dispose」的顺序了。
        try { if (_arkCanvas != null) _arkCanvas.enabled = false; } catch { }
        DisposeArkRefs();
        _arkBuilt = false;
    }

    /// <summary>大厅内容挂在槽位上，跟其它页一样。空壳：只把槽位清空并武装渐入。</summary>
    private static void BuildArkView() {
        try {
            var slot = PageSlot(MainScreenSlot);
            if (slot == null) { Lg.Info("主界面：槽位拿不到，放弃"); return; }
            for (int i = slot.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(slot.GetChild(i).gameObject);
            DisposeArkRefs();

            BuildArkBody(slot);
            _arkBuilt = true;

            // 渐入必须挂在"大厅内容真正所在的那块画布"上，不是槽位上：
            // 内容建在独立的 ArkCanvas 下，挂在槽位上的 CanvasGroup 管不到它（等于没有渐入）。
            // 建完当场武装，所有进入路径（登录交接 / 普通切页 / Esc 退回）共用这一处。
            var host = _arkCanvas != null ? _arkCanvas.gameObject : slot.gameObject;
            var cg = host.GetComponent<CanvasGroup>();
            if (cg == null) cg = host.AddComponent<CanvasGroup>();
            _mainGroup = cg;
            ArmArkFade();
            Lg.Info("主界面已建（按示意图重建 · 渐入已武装）");
        } catch (System.Exception e) {
            Lg.Ex("建主界面失败", e);
        }
    }
}