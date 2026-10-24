// [split-of] PortalUI.SettingsPage.cs lines 1-1023 body 18-966

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

/// <summary>设置页的构建分片：整页骨架（两块遮罩 + 左侧导航 + 内容容器 + 子 tab 行）
/// 与五个主 tab 里的全部设置行（行由 MakeRowAt 手算坐标顺序摆放，不用布局组）。</summary>
public static partial class PortalUI {

    /// <summary>设置页主 tab 与子 tab 的悬停底色（平时全透明）：只给一层很淡的膜，文字不碰。</summary>
    private const float SetTabHoverFillA = 0.16f;

    private static void TickRainbowTexts() {
        if (_rainbowTexts.Count == 0) return;
        float hue = (Time.unscaledTime * 0.12f) % 1f;   // 慢速渐变（约 8 秒一圈）
    // 色相变化极小（<0.002）时跳过：避免每帧算 HSV + 写 100 多个 Text 颜色
        if (_rainbowHueLast >= 0f && Mathf.Abs(hue - _rainbowHueLast) < 0.002f) return;
        _rainbowHueLast = hue;
        Color c = Color.HSVToRGB(hue, 0.85f, 1f);
        for (int i = _rainbowTexts.Count - 1; i >= 0; i--) {
            var t = _rainbowTexts[i];
            if (t == null) { _rainbowTexts.RemoveAt(i); continue; }
      // 设置页 extra 那个标签选中时不许彩虹：它压在实心淡蓝块上，彩色字会糊
      // （选中态的颜色由 RefreshSettingsPage 给）
            if (t == _setExtraNavLabel && _settingsPageVisible && _setMainTab == ExtraTabIndex) continue;
            t.color = c;
        }
    }

  /// <summary>全局输入与面板开关：热键、Esc 分层关闭、编辑器载入、每日成就（始终运行，不依赖面板状态）</summary>
    private static void TickAlwaysHotkeys() {
        int strayBypass = UiInput.SanityResetBypass();   // 安全网：Bypass 计数不该跨帧残留
        if (strayBypass != 0 && Main.ModEntry.Logger != null) {
            Lg.Info($"[LevelLib] 【输入map】Bypass 计数残留 {strayBypass}，已归零（Prefix/Finalizer 没配对）");
        }
        TickEasterEgg();   // 彩蛋"月正NB"计时
        TickVitals1s();        // 每秒体征（定位卡球：bpm / timeScale / paused 到底哪个坏）
        TickStateChangeProbe();   // 状态变化探针：暂停/时间缩放一变就逐帧记录
        TickDeferredInputEnable();   // 关面板后延迟几帧把游戏输入还回去
        EnsureGameInputOffWhilePanelOpen();   // 面板开着时游戏输入必须是关的（常驻段兜底，见 PortalUI.Input.cs）
        TickHealthSnapshot();   // 面板关闭时的体征快照（bpm/timeScale 异常才写日志）
    // 面板开着时每帧清一次游戏异步输入的按键积压（不依赖 AsyncInputManager.Update 是否在跑）：
    // 这些按键是在"游戏读不到输入"期间攒下的，恢复输入时必须丢掉，
    // 否则关面板那一帧游戏会把整段积压按旧时间戳一口气模拟掉 → 球被拉到错误角度（卡球）。
        if (_panelOpen) ClearGameInputBacklog();
        TickInputMapProbe();   // 输入 map 自检：面板开着时应当"UI map 读得到 / 游戏读不到"

    // 调试：mod 目录放 debug_error.txt 时，F9 可手动触发一次报错弹窗（用于验证报错界面）
        TickDebugErrorKey();

    // 快捷键开关（可在 UMM 设置页更换）；游玩谱面时禁用（判据见 ControllerStatePatch 的 HookedState + 场景判断）。
    // 别改成"用 patch 接住 Input.GetKeyDown"：别的 mod 也会 patch 它并返回真值，接住会被反复触发 → 面板自己不停开关。
        if (UiInput.KeyDown(Hotkey)) {
            bool playing = IsLevelPlaying();
            if (!playing) TogglePanel();
        }
    // Esc：整条分层关闭链在 PortalUI.EscClose.cs 里（从最上面那层往下找第一个还开着的窗口，
    // 关掉谁就记一行日志）。这里只留调用，加新弹窗时去那边排队，不要在别处再补一份判据
    // —— 两处都吃同一个 Esc 的话，一次按下会收掉两层。
        TickEscCloseChain();

    // 等待编辑器就绪（含兜底重试）。正常路径：进入前设了 scnEditor.levelToOpenOnLoad，scnEditor.Start 会读它。
    // 判据必须按目标谱面比：只看 ADOBase.levelPath 与 customLevel.levelMaker 是否非空时，
    // 编辑器开着一个空白关也算就绪 → 兜底永不触发，玩家停在空白编辑器。
        if (_editorLoading) {
      // 场景名只取一次，下面两处都要用（缓存分键 + 就绪判据）。
      // 原来这里每帧既全场搜一次 scnEditor、又单独取一次场景名，等于同一件事做两遍。
            string sceneName = null;
            try { sceneName = SceneManager.GetActiveScene().name; } catch { }
      // 缓存命中条件：场景名一致「且」上次那个没被销毁。
      // Unity 的 == 会把"已销毁的托管包装"判成 null，所以对象被场景卸载掉时这里会自然失效，
      // 不需要额外监听场景事件（那样反而要处理"事件在监听器之前就触发"的竞态）。
            if (!string.Equals(_editorCachedScene, sceneName, System.StringComparison.Ordinal)) {
                _editorCached = null;
                _editorCachedScene = sceneName ?? "";
            }
            if (_editorCached == null) _editorCached = Object.FindFirstObjectByType<scnEditor>();
            var editor = _editorCached;
            string want = _expectedOpenPath;
      // 确认"编辑器已载入"不能拿我们自己刚设进去的请求当证据：GetEditorLevelPath 的兜底会读出
      // scnEditor.levelToOpenOnLoad（那是请求，不是结果），而确认那段又把它清成 null
      // 新场景里的编辑器没有谱面可开 = 空关卡。所以确认用的路径不许带预设，且必须真的已经在 scnEditor 场景里。
            string cur = null;
            try { cur = AutoFeatures.GetEditorLevelPath(false); } catch { }
            if (string.IsNullOrEmpty(cur)) { try { cur = ADOBase.levelPath; } catch { } }
            // 场景判据：等价于 SceneManager.GetActiveScene().name == "scnEditor"，
            // 只是场景名这一帧已经取过（上面给缓存分键用的那次），不必再调一次会分配字符串的原生调用。
            // 取不到场景名就算不在编辑器场景（跟原来一样保守，不能因为取名失败就当成了）
            bool inEditorScene = false;
            try { inEditorScene = sceneName == "scnEditor"; } catch { }
            bool matches = inEditorScene && !string.IsNullOrEmpty(want) && !string.IsNullOrEmpty(cur)
                           && SameLevelPath(cur, want);
            if (matches) {
                Lg.Info($"[LevelLib] 编辑器载入确认: {cur}");
                try { scnEditor.levelToOpenOnLoad = null; } catch { }
                _editorLoading = false;
                _pendingOpenPath = null;
                _expectedOpenPath = null;
                _editorReloadTries = 0;
            } else if (editor != null) {
                _editorWaitTimer -= Time.deltaTime;
                if (_editorWaitTimer <= 0f) {
          // 到点还没载入目标谱面 → 自己开一次（可重试）
                    _editorWaitTimer = 1.5f;
                    _editorReloadTries++;
                    string target = !string.IsNullOrEmpty(_pendingOpenPath) ? _pendingOpenPath : want;
                    bool canTry = !string.IsNullOrEmpty(target) && File.Exists(target);
                    if (canTry) {
                        try {
                            editor.OpenLevel(target);
                            Lg.Info($"[LevelLib] 编辑器载入谱面（兜底第 {_editorReloadTries} 次）: {target}"
                                + $" 当前levelPath={cur}");
                        } catch (System.Exception oe) {
                            Lg.Ex("兜底 OpenLevel 失败", oe);
                        }
                    } else {
                        Lg.Info($"[LevelLib] 编辑器兜底放弃：目标文件不存在或路径为空 ({target})");
                        _editorReloadTries = EditorReloadMaxTries;
                    }
                    if (_editorReloadTries >= EditorReloadMaxTries) {
                        Lg.Info($"[LevelLib] 编辑器载入放弃：试了 {_editorReloadTries} 次仍未载入 {want}"
                            + $"（当前levelPath={cur}）—— 停在编辑器里，等玩家自己开谱面");
            // 别让玩家对着空编辑器发愣：明确提示没打开
                        try { ShowToast(Lang.Tr("这个谱面没能自动打开（编辑器里是空的），用编辑器的打开功能再试一次喵")); } catch { }
                        _editorLoading = false;
                        _pendingOpenPath = null;
                        _expectedOpenPath = null;
                        _editorReloadTries = 0;
                    }
                }
            }
        }
    // 每日成就：打卡！——打开编辑器5秒后触发（每天一次）
        if (_achDailyCheckinWait > 0f) {
            _achDailyCheckinWait -= Time.unscaledDeltaTime;
            if (_achDailyCheckinWait <= 0f) {
                _achDailyCheckinWait = -1f;
                Achievements.TriggerDaily("ach_daily_checkin", "打卡！");
            }
        }
    }

  /// <summary>设置页两块遮罩的一圈描边：颜色 = 主题色（alpha 走 Accent 那套面板透明度缩放），比遮罩本体四周各大 1px。
  /// 描边 sprite 的圆角半径必须跟遮罩本体一致（两族的 9-slice border 就是 radius，不匹配时描边会切进遮罩）：
  /// 设置页遮罩用半径 10，主页那三块大遮罩传 HomeMaskRadius = 22；别把这里的默认值改掉。</summary>
    private static void MakeMaskFrame(Transform parent, string name, float left, float top, float w, float h,
                                      float radius = MaskFrameRadius) {
        var rim = MakeImage(parent, name, Accent);      // 主题色
        var frame = EnsureMaskFrameSprite(radius);
        if (frame != null) {
            rim.sprite = frame;
            rim.type = Image.Type.Sliced;               // 九宫格：拉伸时线宽不变
        }
        rim.raycastTarget = false;
        RegisterThemeImage(rim);   // 主题色一变，这圈描边跟着重画
        SetAtPx(rim.rectTransform, left - 2f, top - 2f, w + 4f, h + 4f);
    }

  /// <summary>遮罩描边的默认圆角半径，与 RoundedSprite 一致</summary>
    private const float MaskFrameRadius = 10f;

  /// <summary>把 MakeMaskFrame 画好的那圈描边改成竖直拉伸（底边跟着画布走）。
  /// 外扩量必须跟 MakeMaskFrame 一致：左/上各 −2、宽 +4、底边距也少 2。</summary>
    private static void AnchorMaskFrameV(Transform parent, string name, float left, float top, float w, float bottomMargin) {
        try {
            var t = parent != null ? parent.Find(name) : null;
            if (t != null) SetRectPxV(t.GetComponent<RectTransform>(), left - 2f, top - 2f, w + 4f, bottomMargin - 2f);
        } catch (System.Exception e) { Lg.Ex("描边框拉伸失败（保持原样）", e); }
    }

  /// <summary>遮罩描边的 sprite，按半径分两档缓存：默认 32/10（设置页遮罩、卡片那圈），大档 64/22（主页大遮罩）。
  /// 贴图尺寸必须跟半径同步放大：这两族 sprite 的 border 就是半径，单独改分辨率会撑坏形状。</summary>
    private static Sprite EnsureMaskFrameSprite(float radius = MaskFrameRadius) {
        if (radius > MaskFrameRadius + 0.01f) {
            if (_maskFrameSpriteBig == null) {
                try { _maskFrameSpriteBig = CreateRoundedFrameSprite(64, Mathf.RoundToInt(radius), 3.5f); }
                catch (System.Exception e) {
                    Lg.Ex("大圆角描边 sprite 生成失败（用通用描边兜底）", e);
                    _maskFrameSpriteBig = GetRoundedFrameSprite();
                }
            }
            return _maskFrameSpriteBig;
        }
        if (_maskFrameSprite != null) return _maskFrameSprite;
        try {
            _maskFrameSprite = CreateRoundedFrameSprite(32, Mathf.RoundToInt(MaskFrameRadius), 3.5f);   // 3.5 = 带宽 + 1 → 实际约 2.5px
        } catch (System.Exception e) {
            Lg.Ex("遮罩描边 sprite 生成失败（用通用描边兜底）", e);
            _maskFrameSprite = GetRoundedFrameSprite();
        }
        return _maskFrameSprite;
    }

    private static Sprite _maskFrameSprite;
    private static Sprite _maskFrameSpriteBig;

  /// <summary>设置页整屏页面：挂在面板画布下，独立开关，不参与页面带滑动</summary>
    private static void BuildSettingsPage() {
        if (_settingsPage != null) return;
        try {
            _settingsPage = new GameObject("SettingsPageV2", typeof(RectTransform));
            _settingsPage.transform.SetParent(_panelCanvas.transform, false);
            var prt = _settingsPage.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            var grp = _settingsPage.AddComponent<CanvasGroup>();
            grp.alpha = 1f;

      // 设置页不建自己那层压暗：三个 tab 统一用面板那层全局遮罩，亮度一致，也不会两层叠一起变黑。
      // 画布子节点顺序（从下到上）：Bg →（有视频背景时多一层 BgVideo）→ Veil → SettingsPageV2 → TabContentRoot → TopTabBar。
      // 设置页要插在 Veil 之后、库页之前：既不被背景挡住，也不盖住顶部 tab。
      // 下标必须跟着 Veil 走（Veil 的下标 + 1），不许写死数字：有视频背景时 Veil 的下标会变，
      // 写死 2 会把 Veil 顶到设置页上面，整页被压暗罩盖住（表现是"颜色非常淡"）。
            _settingsPage.transform.SetSiblingIndex(BgLayerInsertIndex());

      // 基准点：设一次，全页复用
            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(_settingsPage.transform, false);
            _setPageRoot = root.GetComponent<RectTransform>();
            _setPageRoot.anchorMin = new Vector2(0f, 1f);
            _setPageRoot.anchorMax = new Vector2(0f, 1f);
            _setPageRoot.pivot = new Vector2(0f, 1f);
            _setPageRoot.anchoredPosition = Vector2.zero;   // 画布左上角 == 屏幕 (0,0)
      // 根节点高度跟着画布（拉伸锚点）：写死 RefH 的话画布更高时整页装不下，两块遮罩会伸出去。
            SetRectPxV(_setPageRoot, 0f, 0f, RefW, 0f);

      // ---------- 主 tab 区背景遮罩 ----------
      // 半透明黑长方形，从主 tab 栏顶部铺到接近界面下边缘；先建 = 画在最底层，不挡任何控件。
      // 只有平坦的 0.45 黑 + 一圈主题色描边（见 MakeMaskFrame），不要玻璃那套模糊/亮光/颗粒。
            var navBg = MakeImage(_setPageRoot, "NavBackdrop", new Color(0f, 0f, 0f, 0.45f));
        RegisterAlphaBackdrop(navBg);   // 跟着面板透明度那一档固定系数
        RegisterGlassPanel(navBg);      // 左边那块遮罩参与面板背景模糊
            navBg.sprite = RoundedSprite;
            navBg.type = Image.Type.Sliced;
            navBg.raycastTarget = false;
      // 两块遮罩的底边是"距画布底 BandBottomMargin"（拉伸锚点）：1080 上等于原来的 (RefH-40) - SetTop，
      // 画布更高时自己长到底。
            SetRectPxV(navBg.rectTransform, NavBackdropLeft, SetTop, NavBackdropW, BandBottomMargin);
            MakeMaskFrame(_setPageRoot, "NavBackdrop_Frame", NavBackdropLeft, SetTop, NavBackdropW,
                          (RefH - 40f) - SetTop);
            AnchorMaskFrameV(_setPageRoot, "NavBackdrop_Frame", NavBackdropLeft, SetTop, NavBackdropW, BandBottomMargin);

      // ---------- 主导航（左侧竖排）：按坐标摆，不用布局组（在这个 UI 环境下不可靠） ----------
            var navCol = new GameObject("NavCol", typeof(RectTransform));
            navCol.transform.SetParent(_setPageRoot, false);
            _setNavCol = navCol.GetComponent<RectTransform>();
            SetAtPx(_setNavCol, SetLeft, SetTop + 60f, NavBtnW, NavBtnH * 5 + NavGap * 4);

      // 选中块必须画在按钮下面（实心块压上去会盖住按钮文字），所以先建块、再建按钮。
            var frame = MakeImage(navCol.transform, "NavFrame", HighlightFillColor);
            frame.raycastTarget = false;
            frame.sprite = RoundedSprite;
            frame.type = Image.Type.Sliced;
            RegisterFillImage(frame);   // 换主题色时要重刷（设置内 tab 选择块）
            _setNavFrameRt = frame.rectTransform;
            _setNavFrameRt.anchorMin = _setNavFrameRt.anchorMax = new Vector2(0f, 1f);
            _setNavFrameRt.pivot = new Vector2(0f, 1f);
            SetNavFrameTo(_setMainTab, true);

      // 五个主 tab：常规（启动 / 导入）/ 外观（背景与界面）/ 操作（快捷键）/ 工具（检验与数据）/ 扩展。
      // 第 5 项 extra 的文字走缓慢 RGB 渐变（复用成就 5 级名字那套色相循环），内容见 PortalUI.Extra.cs。
        string[] navNames = { "去特效", "常规", "外观", "操作", "工具", "扩展" };
            for (int i = 0; i < navNames.Length; i++) {
                int mi = i;
                var btn = MakeFixedTab(navCol.transform, "Nav" + i, navNames[i], 0f, i * (NavBtnH + NavGap), NavBtnW, NavBtnH, FontNav);
                btn.onClick.AddListener(() => {
                    if (_setMainTab != mi) _setSubTab = 0;   // 换主 tab 时子 tab 回到第一个
                    _setMainTab = mi;
                    RefreshSettingsPage();
                    Lg.Info($"[LevelLib] 设置主tab点击 → {navNames[mi]}");
                });
            }
      // extra 那个标签交给彩虹循环（_setNavLabels 按建造顺序追加，所以取最后一项；
      // 插了「去特效」主 tab 后共 6 项，写死 [4] 会拿到"工具"的标签 —— 彩虹就涂错行（用户报过））
            if (_setNavLabels.Count > 0) {
                _setExtraNavLabel = _setNavLabels[_setNavLabels.Count - 1];
                if (_setExtraNavLabel != null && !_rainbowTexts.Contains(_setExtraNavLabel)) _rainbowTexts.Add(_setExtraNavLabel);
            }

      // ---------- 功能区（右侧内容）背景遮罩 ----------
      // 跟主 tab 那层同款半透明黑：先把右侧整片衬起来，再往上摆子 tab 和行。
            var bodyBg = MakeImage(_setPageRoot, "BodyBackdrop", new Color(0f, 0f, 0f, 0.45f));
        RegisterAlphaBackdrop(bodyBg);   // 同左边那块，走面板透明度那一档
        RegisterGlassPanel(bodyBg);      // 右边那块遮罩参与面板背景模糊
            bodyBg.sprite = RoundedSprite;
            bodyBg.type = Image.Type.Sliced;
            bodyBg.raycastTarget = false;
            SetRectPxV(bodyBg.rectTransform, BodyLeft - BodyBackdropPad, SetTop,
                    BodyW + BodyBackdropPad * 2f, BandBottomMargin);
            MakeMaskFrame(_setPageRoot, "BodyBackdrop_Frame", BodyLeft - BodyBackdropPad, SetTop,
                          BodyW + BodyBackdropPad * 2f, (RefH - 40f) - SetTop);
            AnchorMaskFrameV(_setPageRoot, "BodyBackdrop_Frame", BodyLeft - BodyBackdropPad, SetTop,
                             BodyW + BodyBackdropPad * 2f, BandBottomMargin);

      // ---------- 内容容器（右侧） ----------
      // 行由 MakeRowAt 手算坐标顺序摆放，不用布局组；
      // SetTop+65 与子 tab 行的 SetTop+13 是一对，要调就一起调，否则两者之间的相对间距会变。
            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(_setPageRoot, false);
            _setBodyRt = body.GetComponent<RectTransform>();
            SetAtPx(_setBodyRt, BodyLeft, SetTop + 65f, BodyW, RefH - SetTop - 140f);

      // ---------- 子 tab 行（内容区顶部）：手算坐标横排，不用布局组（保证可点） ----------
            var subBar = new GameObject("SubTabRow", typeof(RectTransform));
            subBar.transform.SetParent(_setPageRoot, false);
            _setSubBar = subBar;
            var sbrt = subBar.GetComponent<RectTransform>();
            SetAtPx(sbrt, BodyLeft, SetTop + 13f, BodyW, 44f);

            BuildSettingsPagesInto(body.transform);
            BuildSubTabsFor(_setMainTab);
            BuildSettingsTopTabs();
            RefreshSettingsPage();
            Lg.Info("【新设置页】已构建: 基准左上角=" + SetLeft + "," + SetTop + " 导航列=" + (SetLeft) + " 内容区左=" + (SetLeft + 240f));
        } catch (System.Exception e) {
            Lg.Ex("【新设置页】构建失败", e);
        }
    }

  /// <summary>固定尺寸的 tab 按钮：在父容器里按给定偏移摆放（不用布局组，位置一定准）</summary>
    private static Button MakeFixedTab(Transform parent, string name, string label,
                                       float left, float top, float w, float h, int fontSize) {
        var img = MakeImage(parent, name, new Color(1f, 1f, 1f, 0f));
        img.sprite = null; img.type = Image.Type.Simple; img.raycastTarget = true;
        SetAtPx(img.rectTransform, left, top, w, h);
        var t = MakeText(img.transform, "L", fontSize, Color.white, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, Vector2.zero, Vector2.one);
        t.text = label;
        t.raycastTarget = false;
        _setNavLabels.Add(t);
        var btn = img.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        RegisterUiTarget(btn, img);
        SetUiHoverFill(btn, SetTabHoverFillA);   // 透明底：悬停只给底色一层淡膜
        return btn;
    }

  /// <summary>把导航选中块移到第 index 个按钮上（instant=true 立即就位，否则交给 TickNavFrame 平滑滑动）</summary>
    private static void SetNavFrameTo(int index, bool instant) {
        if (_setNavFrameRt == null) return;
        _setFrameTargetTop = index * (NavBtnH + NavGap) - FramePad;
        _setFrameTargetSize = new Vector2(NavBtnW + FramePad * 2f, NavBtnH + FramePad * 2f);
        if (instant) {
            _setFrameCurTop = _setFrameTargetTop;
            _setFrameCurSize = _setFrameTargetSize;
            _setNavFrameRt.anchoredPosition = new Vector2(-FramePad, -_setFrameCurTop);
            _setNavFrameRt.sizeDelta = _setFrameCurSize;
        }
    }

  /// <summary>导航选中块滑动动画（每帧驱动，指数平滑；快速连点从当前位置接着走）</summary>
    private static void TickNavFrame() {
        if (_setNavFrameRt == null || _settingsPage == null) return;
        float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        _setFrameCurTop = Mathf.Lerp(_setFrameCurTop, _setFrameTargetTop, k);
        _setFrameCurSize = Vector2.Lerp(_setFrameCurSize, _setFrameTargetSize, k);
        if (Mathf.Abs(_setFrameCurTop - _setFrameTargetTop) < 0.2f) _setFrameCurTop = _setFrameTargetTop;
        _setNavFrameRt.anchoredPosition = new Vector2(-FramePad, -_setFrameCurTop);
        _setNavFrameRt.sizeDelta = _setFrameCurSize;
    }

    private static void BuildSettingsTopTabs() {
    // 不自建第二组 tab：全局 TopTabBar 由 SetSettingsPageVisible 统一显隐，
    // 它挂在画布上、不随页面带滑动，在设置页里始终可见可点。
    }

  /// <summary>各级 tab 的子 tab 定义（名字数组；null 表示这一级没有子 tab）</summary>
    private static string[] SubTabNames(int mainTab) {
        switch (mainTab) {
            case 0: return new[] { "全局" };
            case 1: return new[] { "启动", "导入与存储" };
            case 2: return new[] { "个人信息", "背景与外观", "滚动栏" };
            case 3: return new[] { "快捷键", "诊断" };
            case 4: return new[] { "谱面检验", "数据管理", "更新" };
      // extra：两个子 tab 的顺序必须与 PortalUI.Extra.cs 里的 ExtraSubDev / ExtraSubFish 一致
      // （那两页的行按子 tab 下标分组显隐，顺序一错就是整页空白）
            case ExtraTabIndex:
                return new[] { "开发者", "大肥鱼" };
            default: return null;   // 没有子 tab
        }
    }

  /// <summary>按主 tab 重建子 tab 行（只在选中项越界时才修正，不重置 _setSubTab）。
  /// 选中态与主 tab 同款：一块实心淡蓝（HighlightFillColor）滑过去 + 选中文字切 HighlightTextColor。
  /// 块必须先建（画在按钮下面，否则盖住文字）；滑动器与主 tab / 官谱库那几块共用 HomeSlide。</summary>
    private static void BuildSubTabsFor(int mainTab) {
        if (_setSubBar == null) return;
        var names = SubTabNames(mainTab);
        string key = (names == null) ? "" : string.Join("|", names);
    // 同一组名字就一个对象都不重建：点子 tab 也会走到这里，重建会把选中块摆回起点 = 永远滑不起来。
    // 所以判据要带上"块还在不在"（_setSubSlide.Rt），只比 key 不够（对象销毁后 key 可能仍然相等）。
        if (key == _setSubBuiltKey && _setSubSlide.Rt != null) return;
        _setSubBuiltKey = key;
        for (int i = _setSubBar.transform.childCount - 1; i >= 0; i--) Object.Destroy(_setSubBar.transform.GetChild(i).gameObject);
        _setSubButtons.Clear();
        _setSubSlide.Rt = null;
        if (names == null || names.Length == 0) { _setSubBar.SetActive(false); return; }
        _setSubBar.SetActive(true);
    // 只在越界时修正，不覆盖用户的选择
        if (_setSubTab < 0 || _setSubTab >= names.Length) _setSubTab = 0;

        var frame = MakeImage(_setSubBar.transform, "SubFrame", HighlightFillColor);
        RegisterFillImage(frame);   // 换主题色要重刷（跟主 tab 的 NavFrame 一样）
        frame.raycastTarget = false;
        frame.sprite = RoundedSprite;
        frame.type = Image.Type.Sliced;
        var frt = frame.rectTransform;
        frt.anchorMin = frt.anchorMax = new Vector2(0f, 1f);
        frt.pivot = new Vector2(0f, 1f);
    // Destroy 要到帧末才真的生效，光靠"先建"只能保证新建的按钮排在块后面；
    // 显式钉到第一个子物体更稳（uGUI 同级子物体越靠后越在上面，块必须在所有按钮下面）。
        frame.transform.SetAsFirstSibling();
        _setSubSlide.Rt = frt;

        for (int i = 0; i < names.Length; i++) {
            int si = i;
            var img = MakeImage(_setSubBar.transform, "Sub" + i, new Color(1f, 1f, 1f, 0f));
            img.sprite = null; img.type = Image.Type.Simple; img.raycastTarget = true;
      // 手算坐标横排（不用布局组），几何走常量
            SetAtPx(img.rectTransform, i * (SubBtnW + SubBtnGap), 0f, SubBtnW, SubBtnH);
            var t = MakeText(img.transform, "L", FontSub, Color.white, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            t.text = names[i];
            t.raycastTarget = false;
            var btn = img.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            RegisterUiTarget(btn, img);
            SetUiHoverFill(btn, SetTabHoverFillA);   // 同上
            btn.onClick.AddListener(() => {
                _setSubTab = si;
                RefreshSettingsPage();
                Lg.Info($"[LevelLib] 设置子tab点击 → {names[si]}");
            });
            _setSubButtons.Add(btn);
        }
        SetSubFrameTo(_setSubTab, true);   // 刚重建 → 直接就位，别从上一组的位置滑过来
    }

  /// <summary>把子 tab 选中块对准第 index 个按钮（instant=true 直接就位）。
  /// 按钮按 宽 SubBtnW + 间距 SubBtnGap 横排，所以块 = 按钮尺寸外扩 SubFramePadX / SubFramePadY。</summary>
    private static void SetSubFrameTo(int index, bool instant) {
        if (_setSubSlide.Rt == null) return;
        _setSubSlide.Set(
            new Vector2(index * (SubBtnW + SubBtnGap) - SubFramePadX, SubFramePadY),
            new Vector2(SubBtnW + SubFramePadX * 2f, SubBtnH + SubFramePadY * 2f));
        if (instant) _setSubSlide.Snap();
    }

  /// <summary>子 tab 选中块的滑动（每帧驱动，手感跟主 tab 的导航块一致）</summary>
    private static void TickSubFrame() {
        if (_settingsPage == null) return;
        _setSubSlide.Tick();
    }

  // ---------- 滚动栏（列表上方那条滚动文本）设置 ----------

  /// <summary>显示内容模式按钮（0=歌曲名 1=自定义信息）</summary>
    private static readonly Button[] _nowModeBtns = new Button[2];

  /// <summary>切换滚动栏内容来源（1=歌名 2=自定义文本），立即生效并写盘</summary>
    private static void ApplyNowBarMode(int mode) {
        NowBarMode = mode;
        RefreshNowBarModeButtons();
        RefreshNowBar();
        AutoFeatures.Save();
    }

  /// <summary>两个模式按钮的高亮（选中的用主色）</summary>
    private static void RefreshNowBarModeButtons() {
        for (int i = 0; i < _nowModeBtns.Length; i++) {
            var b = _nowModeBtns[i];
            if (b == null) continue;
            SetUiSelectFill(b, ((i + 1) == NowBarMode) ? Accent : ItemColor);
        }
    }

  /// <summary>行内右侧的自定义信息输入框（改了就写盘，滚动栏立刻跟着变）</summary>
    private static void BuildNowBarInputRow(GameObject row) {
        var box = MakeImage(row.transform, "Input", new Color(0.10f, 0.12f, 0.18f, 0.85f));
        box.sprite = SmallRoundSprite;
        box.type = Image.Type.Sliced;
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(420f, 40f);
        brt.anchoredPosition = new Vector2(-6f, 0f);

        var txt = MakeText(box.transform, "T", FontBtn, Color.white, TextAnchor.MiddleLeft);
        var trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Truncate;
        txt.supportRichText = false;

        var ph = MakeText(box.transform, "PH", FontBtn, new Color(0.55f, 0.55f, 0.62f, 1f), TextAnchor.MiddleLeft);
        var prt = ph.rectTransform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(10f, 0f); prt.offsetMax = new Vector2(-10f, 0f);
        ph.text = Lang.Tr("在这里输入要滚动显示的文字…");

        var input = box.gameObject.AddComponent<InputField>();
        input.targetGraphic = box;
        input.textComponent = txt;
        input.placeholder = ph;
        input.lineType = InputField.LineType.SingleLine;
        input.text = NowBarCustomText ?? "";
        input.onValueChanged.AddListener(v => {
            NowBarCustomText = v;
            RefreshNowBar();     // 立刻反映到滚动栏
            AutoFeatures.Save();
        });
    }

  /// <summary>通用行内输入框：外观跟滚动栏那个一样，可给不同行复用（更新检查的 API Key / slug 等）</summary>
    private static InputField BuildTextInputRow(GameObject row, string placeholder, string initValue, float width,
                                                System.Action<string> onChanged, bool password = false) {
        var box = MakeImage(row.transform, "Input", new Color(0.10f, 0.12f, 0.18f, 0.85f));
        box.sprite = SmallRoundSprite;
        box.type = Image.Type.Sliced;
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(width, 40f);
        brt.anchoredPosition = new Vector2(-6f, 0f);

        var txt = MakeText(box.transform, "T", FontBtn, Color.white, TextAnchor.MiddleLeft);
        var trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Truncate;
        txt.supportRichText = false;

        var ph = MakeText(box.transform, "PH", FontBtn, new Color(0.55f, 0.55f, 0.62f, 1f), TextAnchor.MiddleLeft);
        var prt = ph.rectTransform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(10f, 0f); prt.offsetMax = new Vector2(-10f, 0f);
        ph.text = placeholder;

        var input = box.gameObject.AddComponent<InputField>();
        input.targetGraphic = box;
        input.textComponent = txt;
        input.placeholder = ph;
        input.lineType = InputField.LineType.SingleLine;
        if (password) input.contentType = InputField.ContentType.Password;
        input.text = initValue ?? "";
        input.onValueChanged.AddListener(v => {
            try { onChanged(v); } catch (System.Exception e) { Lg.Ex("设置输入处理失败", e); }
        });
        return input;
    }

  /// <summary>行内右侧按钮</summary>
    private static Button RowButton(GameObject row, string text, Color col, System.Action act) {        var btn = MakeButton(row.transform, "B", Vector2.zero, new Vector2(180f, 38f), col);
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-100f, 0f);
        var t = MakeText(btn.transform, "T", FontBtn, Color.white, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, Vector2.zero, Vector2.one);
        t.text = text;
        if (act != null) btn.onClick.AddListener(() => act());
        return btn;
    }

  /// <summary>行内右侧滑条，自绘而不用 Unity 的 Slider：UpdateVisuals() 会把滑块锚点强制改掉，实际尺寸变成"父 rect + sizeDelta"。
  /// 做法同谱面列表的难度滑条（轨道定宽定高、滑块按 anchoredPosition 定位、拖动走 EventTrigger），数值文字后建画在滑块之上。
  /// 返回值 = 刷新这条滑条数值的入口（玩家拖信息栏后刷 X/Y 滑条用）；委托里必须先判控件还在不在，否则常驻段调用会 MissingReferenceException。</summary>
    private static System.Action<float> RowSlider(GameObject row, float min, float max, float cur, string suffix, System.Action<float> onSet) {
        const float TrackW = 480f, TrackH = 40f;    // 轨道
        const float Pad = 0f;                       // 左右留白为 0 → 滑块能贴到轨道两端
        const float HandleW = 36f, HandleH = TrackH;   // 滑块高度 = 轨道高度，完全包住

        var track = MakeImage(row.transform, "SliderBg", new Color(0f, 0f, 0f, 0.34f));
        track.sprite = SmallRoundSprite;
        track.type = Image.Type.Sliced;
        track.raycastTarget = true;                 // 整条轨道都能点/拖
        var trt = track.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(TrackW, TrackH);
        trt.anchoredPosition = new Vector2(-TrackW * 0.5f - 12f, 0f);   // 贴行右边缘，右边留 12

    // 滑块自己摆位（不用 Slider 的 handleRect）；半透明压在轨道上更柔和
        var handle = MakeImage(track.transform, "Handle",
            new Color(HighlightFillColor.r, HighlightFillColor.g, HighlightFillColor.b, 0.55f));
        RegisterFillImage(handle);   // 换主题色要重刷；alpha 0.55 是有意的
        handle.sprite = SmallRoundSprite;
        handle.type = Image.Type.Sliced;
        handle.raycastTarget = false;
        var hrt = handle.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
        hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = new Vector2(HandleW, HandleH);

    // 数值文字（最后建 = 画在滑块之上，永不被挡）
        var valT = MakeText(track.transform, "Val", 20, Color.white, TextAnchor.MiddleCenter);
        Stretch(valT.rectTransform, Vector2.zero, Vector2.zero);
        valT.raycastTarget = false;

    // 滑块中心的活动范围 = ±(轨道宽 − 滑块宽)/2 − Pad，这样左右边缘正好贴到轨道两端（Pad=0 时）
        float travel = Mathf.Max(1f, (TrackW - HandleW) * 0.5f - Pad);
        System.Action<float> apply = v => {
            if (hrt == null || valT == null) return;   // 行已销毁（面板关了）→ 安静退出
            float k = Mathf.InverseLerp(min, max, v);
            hrt.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, k), 0f);
            string s = Mathf.RoundToInt(v) + suffix;
            if (valT.text != s) valT.text = s;         // 值没变就不重写（拖信息栏时这条每帧都会被调）
        };
        apply(Mathf.Clamp(cur, min, max));

    // 拖动/点击：把屏幕坐标换算成轨道内的横向比例（跟难度滑条同一套做法）
        UnityEngine.Events.UnityAction<BaseEventData> onDrag = bed => {
            var ed = bed as PointerEventData;
            if (ed == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trt, ed.position, ed.pressEventCamera, out Vector2 local)) return;
            float half = TrackW * 0.5f - Pad;
            float k = Mathf.Clamp01((local.x + half) / (half * 2f));
            float v = min + k * (max - min);
            v = Mathf.Round(v);                       // 整数档
            apply(v);
            try { onSet(v); } catch { }
        };
        var et = track.gameObject.AddComponent<EventTrigger>();
        AddTrigger(et, EventTriggerType.PointerDown, onDrag);
        AddTrigger(et, EventTriggerType.Drag, onDrag);

    // 报一次真实几何，确认这次自绘的尺寸真的生效了
        if (!_sliderGeomLogged) {
            _sliderGeomLogged = true;
            Canvas.ForceUpdateCanvases();
            Lg.Info($"[LevelLib] 滑条几何(自绘): 行='{row.name}' 轨道rect={trt.rect.size.ToString("F0")}"
                + $" | 滑块rect={hrt.rect.size.ToString("F0")} 行程={(-travel):F0}~{travel:F0}");
        }
        return apply;
    }

  /// <summary>滑条几何只报一次（排查用）</summary>
    private static bool _sliderGeomLogged;

  /// <summary>建五个主 tab 的内容页（每页 = 若干子页，按子 tab 显隐；行全部手算坐标顺序摆放）</summary>
    private static void BuildSettingsPagesInto(Transform body) {
        _toggleRegs.Clear();   // 重建页面时先清登记表（本方法会重新建出全部开关）
    // 6 页了：第 0 页是新加的「去特效」（用户要求放第一个），原来五页整体 +1。
        // 页面下标与子 tab 的对应关系全靠 SubTabNames 的 case，改一处必须同步另一处。
        for (int i = 0; i < 6; i++) {
            var p = new GameObject("Page" + i, typeof(RectTransform));
            p.transform.SetParent(body, false);
            var rt = p.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _setPageObjs.Add(p);
        }

    // ---------- 主 tab 0：去特效（全局）→ 子 tab [0]全局 ----------
    // 全局去特效是"兜底"：某张谱面自己配过去特效了就只听那张谱面的，没配才用全局。
    // 默认关闭（用户要求），所以这里只有一个总开关 + 四套预设 + 详细入口。
        float fy = 0f;
        BuildFxGlobalPage(_setPageObjs[0].transform, ref fy);

    // ---------- 主 tab 1：常规 → 子 tab [0]启动 / [1]导入与存储 ----------
    // 「使用 LevelLibrary 接管进入」：原来的两个开关（进关卡编辑器 / 进谱面库）并成了这一个。
    // 打开后的行为见 AutoFeatures.TickLaunchAction（进编辑器，编辑器就绪后再开面板）
    // 与 AutoFeatures.TickUmmWindow（压住 UMM 每次开机弹的模组列表窗口）。
        float y = 0f;
        var b0 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("使用 LevelLibrary 接管进入"), ref y, 0);
        SettingsToggleRow(b0, out _launchTakeoverToggle, null);
        BindToggle(_launchTakeoverToggle, () => AutoFeatures.LaunchTakeover, v => AutoFeatures.LaunchTakeover = v);
    // 说明行：不是开关，右边什么都不放，只为把"接管之后会发生什么"写清楚
    // （设置页的行只有一行标签、没有第二行小字的机制，所以单独占一行）
        var b0n = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("开机自动进关卡编辑器并打开谱面库，同时隐去 UMM 的模组列表弹窗"), ref y, 0);
        var b0nt = b0n.GetComponentInChildren<Text>();
        if (b0nt != null) b0nt.color = new Color(1f, 1f, 1f, WtSub);
    // 屏保（详见 PortalUI.Splash.cs）：「开始界面作为屏保」= 谱面库内空闲 30 秒后渐变进开始界面，长按回库；
    // 「屏保全局显示」= 不在打谱/编辑器时，面板开没开都会弹，面板关着时长按只关这一页。
        var sv0 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("开始界面作为屏保"), ref y, 0);
        SettingsToggleRow(sv0, out _saverLibToggle, null);
        BindToggle(_saverLibToggle, () => SaverInLib, v => SaverInLib = v);
    // 「屏保休眠时间」：点一下换下一档（30 秒 / 1 分钟 / 5 分钟 / 10 分钟，循环，见 CycleSaverIdle）；
    // 位置必须在「开始界面作为屏保」正下方。
        var sv1 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("屏保休眠时间"), ref y, 0);
        var idleBtn = RowButton(sv1, SaverIdleText(), ItemColor, () => {
            CycleSaverIdle();
            AutoFeatures.Save();          // 跟开关一样立刻落盘
            RefreshSaverIdleButton();
        });
        _saverIdleBtnText = idleBtn != null ? idleBtn.GetComponentInChildren<Text>() : null;
        var sv2 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("屏保全局显示"), ref y, 0);
        SettingsToggleRow(sv2, out _saverGlobalToggle, null);
        BindToggle(_saverGlobalToggle, () => SaverGlobal, v => SaverGlobal = v);

        y = 0f;
        var s0 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("批量导入谱面"), ref y, 1);
        RowButton(s0, Lang.Tr("选择文件夹导入"), Accent, StartBatchImportFromUi);
    // 导入入口在这里两行（批量 / 单个）。空库那块也有同样的两个按钮：两处必须调同一个函数，别各写一份。
var s0b = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("导入单个谱面"), ref y, 1);
   RowButton(s0b, Lang.Tr("选择文件导入"), Accent, StartSingleImportFromUi);
        // 「单个导入也接受 .zip」：开着时文件对话框同时显示 .adofai 与 .zip，选到压缩包会先解压再照常导入
        // （解压目标在系统临时目录，导入完删临时目录，压缩包本身不动）。默认开。
        var s0bb = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("单个导入接受压缩包"), ref y, 1);
        SettingsToggleRow(s0bb, out _singleImportZipsToggle, null);
        BindToggle(_singleImportZipsToggle, () => SingleImportZips, v => SingleImportZips = v);
    // 自动导入：打开一个新谱面（编辑器 / 双击 adofai 文件）时自动把它录进谱面库，默认开。
    // 开关值就是 AutoFeatures.AutoRecord —— 原来这个功能一直存在但界面上没有开关。
        var s0c = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("自动导入"), ref y, 1);
        SettingsToggleRow(s0c, out _autoImportToggle, null);
        BindToggle(_autoImportToggle, () => AutoFeatures.AutoRecord, v => AutoFeatures.AutoRecord = v);
    // 「自动导入后备份」：只作用于上面那条自动录入的路（打开一个新谱面时自动录进库），
    // 与下面那个「谱面导入后备份」（导入弹窗里手动导入那批）是两件事，别接同一个开关。默认关。
        var s0d = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("自动导入后备份"), ref y, 1);
        SettingsToggleRow(s0d, out _autoImportBackupToggle, null);
        BindToggle(_autoImportBackupToggle, () => AutoFeatures.AutoImportBackup, v => AutoFeatures.AutoImportBackup = v);
        var s1 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("谱面导入后备份"), ref y, 1);
        SettingsToggleRow(s1, out _backupImportToggle, null);
        BindToggle(_backupImportToggle, () => AutoFeatures.BackupOnImport, v => AutoFeatures.BackupOnImport = v);
        var s2 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("自动标记"), ref y, 1);
        SettingsToggleRow(s2, out _markToggle, null);
        BindToggle(_markToggle, () => AutoFeatures.AutoMark, v => AutoFeatures.AutoMark = v);
        var s3 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("自定义备份位置"), ref y, 1);
        RowButton(s3, Lang.Tr("选择文件夹"), Accent, () => CustomBackupDir());
        var s4 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("打开备份位置"), ref y, 1);
        RowButton(s4, Lang.Tr("打开"), ItemColor, () => {
            try {
                string dir = LevelScanner.BackupBase;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { ShowToast(Lang.Tr("备份目录不存在喵")); return; }
                Process.Start("explorer.exe", "\"" + dir + "\"");
            } catch (System.Exception ex) { Lg.Ex("打开备份位置失败", ex); }
        });
    // 卡死出口：筛选条件是持久落盘的，筛空又没选中谱面时界面会卡住（重启也救不回来）。
    // 主界面的出口是列表里「没有匹配的谱面 · 点这里清空筛选条件」那一行，这里再放一个永远点得到的兜底。
        var s5 = MakeRowAt(_setPageObjs[1].transform, Lang.Tr("清空谱面筛选"), ref y, 1);
        RowButton(s5, Lang.Tr("清空"), ItemColor, () => ClearAllFilters());

    // ---------- 主 tab 2：外观 → 子 tab [0]个人信息 / [1]背景与外观 / [2]滚动栏 ----------
        y = 0f;
    // 「选择头像」「玩家名字」两行排在最前
        var p1 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("选择头像"), ref y, 0);
        RowButton(p1, Lang.Tr("选择图片"), Accent, () => PickAvatar());
        var p2 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("玩家名字"), ref y, 0);
        RowButton(p2, Lang.Tr("修改"), Accent, () => PickPlayerName());
    // 「身份」那行输入已删，但 PlayerTitle 字段与 settings.txt 的键都保留：个人页欢迎语仍读它
    // （空则只写「欢迎回来，用户名」）。
    // 总开关：关了之后面板内 / 界面外都不显示个人信息栏
        var p0 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("显示个人信息栏"), ref y, 0);
        SettingsToggleRow(p0, out _playerBoxToggle, null);
        BindToggle(_playerBoxToggle, () => ShowPlayerBox, v => { ShowPlayerBox = v; TickPlayerBox(); SyncPanelPlayerBox(); });
        var p3 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("界面外显示"), ref y, 0);
        SettingsToggleRow(p3, out _outsideToggle, null);
    // 点完立刻同步面板内那份，让"点完就变"不依赖下一帧（TickPanelPlayerBox 每帧也会兜住）
        BindToggle(_outsideToggle, () => ShowPlayerBoxOutside, v => { ShowPlayerBoxOutside = v; TickPlayerBox(); SyncPanelPlayerBox(); });
    // 「游玩时隐藏」：判据 IsLevelPlaying()（与"游玩时禁用快捷键"同一套）。
    // 挨着「界面外显示」放，管的正是界面外那份在打谱时露不露；默认关（不初始化 = false）。
        var p3b = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("游玩时隐藏"), ref y, 0);
        Button hideInGameBtn = null;
        SettingsToggleRow(p3b, out hideInGameBtn, null);
        BindToggle(hideInGameBtn, () => HidePlayerBoxInGame, v => { HidePlayerBoxInGame = v; TickPlayerBox(); });
        var p4 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("X 位置"), ref y, 0);
    // 存下 RowSlider 返回的刷新入口：玩家在界面里直接拖信息栏后，这两条滑条要跟着刷到新位置
        _pbXSlider = RowSlider(p4, -20f, 120f, PlayerBoxX * 100f, "", v2 => {
            PlayerBoxX = v2 / 100f;
            ApplyPlayerBoxPosToRt(_playerBoxRt);   // 锚点 + 偏移一起同步（贴顶那条要跟着 Y 走）
            ApplyPlayerBoxPosToRt(_outsideBoxRt);  // 界面外那份也一起（它每帧会自查，这里让它更跟手）
            AutoFeatures.Save();
        });
        var p5 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("Y 位置"), ref y, 0);
        _pbYSlider = RowSlider(p5, -20f, 120f, PlayerBoxY * 100f, "", v2 => {
            PlayerBoxY = v2 / 100f;
            ApplyPlayerBoxPosToRt(_playerBoxRt);
            ApplyPlayerBoxPosToRt(_outsideBoxRt);
            AutoFeatures.Save();
        });
        y = 0f;
        var b1 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("主题色"), ref y, 1);
        BuildThemeRow(b1);
    // 「背景压暗」滑条：背景（纯色底/神经图/静态图/视频）之上那层黑遮罩的透明度，
    // 三处取值（面板遮罩 / 各页 VeilTargetFor / 进场过渡遮罩）都从 BgVeilPercent 来，拖动即时生效。
        var bVeil = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("背景压暗"), ref y, 1);
        RowSlider(bVeil, 0f, 100f, PortalUI.BgVeilPercent, "%", v2 => {
            PortalUI.BgVeilPercent = Mathf.RoundToInt(v2);
            SetVeilTarget(Mathf.Clamp(PortalUI.BgVeilPercent, 0, 100) / 100f, true);
            AutoFeatures.Save();
        });
    // 「静态背景」两行：没有动态背景（视频不可用）时自动显示的整屏底图 —— 不加开关，
    // 有图就显示、图没了才回落代码背景（神经图）。默认那张随资源包分发；
    // 「选择图片」自己选一张（存 assets\bg_static_custom.png）；「恢复默认背景」删掉自选那份回到默认图。
        var bs1 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("静态背景"), ref y, 1);
        RowButton(bs1, Lang.Tr("选择图片"), Accent, () => PickStaticBackground());
        var bs2 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("恢复默认背景"), ref y, 1);
        RowButton(bs2, Lang.Tr("恢复默认背景"), ItemColor, () => RestoreDefaultStaticBackground());
    // 「视频背景」+「选择视频」两行放在静态背景下面；MakeRowAt 自己推进 y，不用手工补空档。
        var bv = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("视频背景"), ref y, 1);
        SettingsToggleRow(bv, out _videoBgToggle, null);
        BindToggle(_videoBgToggle, () => VideoBgOn, v => {
            VideoBgOn = v;
            AutoFeatures.Save();
            ApplyVideoBackground();      // 立刻生效（没挂层的界面下次建背景图时会自己挂）
            // 本地没有可播文件（自己的没设 + 默认那份没下过）时提示去哪拿：
            if (v && VideoBgEffectivePath() == null)
                ShowToast(Lang.Tr("还没有可用的视频文件喵（点下面的「下载默认视频背景」，或用「选择视频文件」自己选一个）"));
        });
        var bvp = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("选择视频"), ref y, 1);
        _videoPickBtn = RowButton(bvp, Lang.Tr("选择视频文件"), Accent, () => PickVideoBackground());
    // 压缩要几十秒，所以按钮文字要能变成「压缩中… N%」（TickVideoBg 在主线程刷，见 RefreshVideoPickRow）
        try { _videoPickLabel = _videoPickBtn != null ? _videoPickBtn.GetComponentInChildren<Text>() : null; } catch { }
    // 「默认视频背景」：本地有默认那份（下过、或机器上还留着）就显示「恢复默认视频背景」、
    // 点一下切回它；没有就显示「下载默认视频背景」、从平台把旧资源包里的视频取回来再切过去。
    // 文案在恢复 / 下载之间自动翻，下载中显示进度并禁点（见 RefreshDefaultVideoRow）。
        var bvd = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("默认视频背景"), ref y, 1);
        _defaultVideoBtn = RowButton(bvd, DefaultVideoButtonText(), ItemColor, OnDefaultVideoButton);
    // 「下载默认视频背景」八个字在 180 宽的标准按钮里放不下：这一颗单独加宽，右缘仍与别行按钮对齐
        try {
            var drt = _defaultVideoBtn.GetComponent<RectTransform>();
            drt.sizeDelta = new Vector2(DefaultVideoBtnW, 38f);
            drt.anchoredPosition = new Vector2(-DefaultVideoBtnW * 0.5f - 10f, 0f);
        } catch { }
    // 要能变成「下载中… N%」，拿住按钮上的文字组件
        try { _defaultVideoLabel = _defaultVideoBtn != null ? _defaultVideoBtn.GetComponentInChildren<Text>() : null; } catch { }
    // 「设置背景音乐」两行，放在音量上面：选本地的 MP3/OGG 直接当面板背景音乐 ——
    // 无论原来有没有设、是不是来自某个谱面，选了就整体换成这一首；「取消背景音乐」清掉当前那首。
        var bBgm = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("设置背景音乐"), ref y, 1);
        RowButton(bBgm, Lang.Tr("选择音乐文件"), Accent, () => PickCustomBgm());
        var bBgmC = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("取消背景音乐"), ref y, 1);
        RowButton(bBgmC, Lang.Tr("取消背景音乐"), ItemColor, () => ClearCustomBgm());
        var b2 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("背景音乐音量"), ref y, 1);
        RowSlider(b2, 0f, 150f, MusicVolume, "%", v2 => {
            MusicVolume = Mathf.RoundToInt(v2);
            if (_musicPlayer != null && _musicPlayer.Source != null) _musicPlayer.Source.volume = MusicVolume / 100f;
            AutoFeatures.Save();
        });
        var bFs = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("界面字体大小"), ref y, 1);
        RowSlider(bFs, UiFontPercentMin, UiFontPercentMax, UiFontScale * 100f, "%", v2 => {
            UiFontScale = Mathf.Clamp(v2 / 100f, UiFontPercentMin / 100f, UiFontPercentMax / 100f);
            ApplyUiFontScale();
            AutoFeatures.Save();
        });
        var b3 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("谱面列表字体大小"), ref y, 1);
        RowSlider(b3, 20f, 44f, ListFontSize, "", v2 => {
            ListFontSize = Mathf.RoundToInt(v2);
            foreach (var pp in _poolRows) { if (pp != null && pp.Label != null) pp.Label.fontSize = UiFontPx(ListFontSize); }
            AutoFeatures.Save();
        });
    // 成就名称 / 详细信息的字体大小两行放这里，紧挨着「谱面列表字体大小」（同一族，都是界面字号）
        var bAch1 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("成就名称字体大小"), ref y, 1);
        RowSlider(bAch1, 18f, 34f, AchNameFontSize, "", v2 => {
            AchNameFontSize = Mathf.RoundToInt(v2);
            ApplyAchFontSizes();
            AutoFeatures.Save();
        });
        var bAch2 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("成就详细信息字体大小"), ref y, 1);
        RowSlider(bAch2, 14f, 22f, AchDetailFontSize, "", v2 => {
            AchDetailFontSize = Mathf.RoundToInt(v2);
            ApplyAchFontSizes();
            AutoFeatures.Save();
        });
    // 「面板背景模糊」那一行已删：面板磨砂固定满值（PortalUI.PanelBlur 是常量 100）。
    // 它改的是各页遮罩那一块背景糊到什么程度（实现见 PortalUI.PanelGlass.cs），不再是遮罩深浅。

    // ---------- 主 tab 1 → 子 tab 2：滚动栏 ----------
        y = 0f;
        var n0 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("显示滚动栏"), ref y, 2);
        SettingsToggleRow(n0, out _nowShowToggle, null);
        BindToggle(_nowShowToggle, () => NowBarShow, v2 => {
            NowBarShow = v2;
            RefreshNowBar();
            AutoFeatures.Save();
        });
    // 显示内容：两个按钮二选一（点了立刻生效）
        var n1 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("显示内容"), ref y, 2);
        var modeSong = RowButton(n1, Lang.Tr("歌曲名"), ItemColor, () => ApplyNowBarMode(1));
        var modeCustom = RowButton(n1, Lang.Tr("自定义信息"), ItemColor, () => ApplyNowBarMode(2));
    // 两个按钮并排摆（RowButton 默认贴右边，这里把第二个往左挪一格）
        var msRt = modeSong.GetComponent<RectTransform>();
        var mcRt = modeCustom.GetComponent<RectTransform>();
        if (msRt != null && mcRt != null) {
            msRt.anchoredPosition = new Vector2(-100f, 0f);
            msRt.sizeDelta = new Vector2(180f, 38f);
            mcRt.anchoredPosition = new Vector2(-290f, 0f);
            mcRt.sizeDelta = new Vector2(180f, 38f);
        }
        _nowModeBtns[0] = modeSong;
        _nowModeBtns[1] = modeCustom;
        RefreshNowBarModeButtons();
    // 自定义信息输入框（只在"自定义信息"模式下有用，但一直显示，切过去就能直接编辑）
        var n2 = MakeRowAt(_setPageObjs[2].transform, Lang.Tr("自定义信息"), ref y, 2);
        BuildNowBarInputRow(n2);

    // ---------- 主 tab 3：操作（快捷键）→ 子 tab [0]快捷键 / [1]诊断 ----------
        y = 0f;
        var k1 = MakeRowAt(_setPageObjs[3].transform, Lang.Tr("打开选中谱面"), ref y, 0);
        _hotkeyBtnOpen = RowButton(k1, KeyDisplay(OpenLevelKey) + Lang.Tr("（点击修改）"), Accent, () => {
            CaptureTarget = "open";
            ShowToast(Lang.Tr("请按一个键…"));
        });
        var k2 = MakeRowAt(_setPageObjs[3].transform, Lang.Tr("随机谱面并打开"), ref y, 0);
        _hotkeyBtnRandom = RowButton(k2, KeyDisplay(RandomLevelKey) + Lang.Tr("（点击修改）"), Accent, () => {
            CaptureTarget = "random";
            ShowToast(Lang.Tr("请按一个键…"));
        });
        var k3 = MakeRowAt(_setPageObjs[3].transform, Lang.Tr("向上切换 / 向下切换"), ref y, 0);
        RowButton(k3, Lang.Tr("↑ / ↓（固定）"), ItemColor, () => ShowToast("上下切换固定为 ↑ / ↓，不可修改"));
    // ---------- 诊断（排错用）：详细日志 / 报错弹窗自测 / 字体检查 ----------
        y = 0f;   // 下面几条属于「诊断」子 tab → y 从头开始
        var d1 = MakeRowAt(_setPageObjs[3].transform, Lang.Tr("记录详细日志"), ref y, 1);
        SettingsToggleRow(d1, out _verboseLogToggle, null);
        BindToggle(_verboseLogToggle, () => LogVerbose, v => LogVerbose = v);
    // 「测试报错弹窗」那一行已删（自测入口不再放给玩家）；报错界面本身仍在，见 PortalUI.ErrorDialog。
    // 字体检查放在诊断这一页（跟前面几条同族，都是排错用的）；逻辑在 PortalUI.FontCheck.cs：
    // 查通用字体是否变楷体 / 我们自己的字有没有混进楷体 / 有没有字体为空的字，能修的当场修、报告进日志。
        var d3 = MakeRowAt(_setPageObjs[3].transform, Lang.Tr("检查界面字体"), ref y, 1);
        RowButton(d3, Lang.Tr("开始检查"), new Color(0.30f, 0.48f, 0.40f, 1f), () => {
            try { ShowToast(CheckAndFixUiFont()); }
            catch (System.Exception ex) { ReportError("字体检查失败", ex); }
        });
    // ---------- 主 tab 4：工具 → 子 tab [0]谱面检验 / [1]数据管理 / [2]更新 ----------
        y = 0f;
        var v1 = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("重复谱面检验"), ref y, 0);
    // 必须先走确认窗（ShowVerifyConfirm）再检验：直接 StartVerify 会跳过确认流程、状态错乱
        RowButton(v1, Lang.Tr("开始检验"), Accent, () => ShowVerifyConfirm());
    // 说明文字单独占一行（不带行标签）
        y += 12f;
        var v2r = MakeRowAt(_setPageObjs[4].transform, "", ref y, 0);
        v2r.GetComponent<RectTransform>().sizeDelta = new Vector2(_setBodyRt.rect.width, 76f);
        y += 76f - RowH;   // 说明行占 76
        var v2lbl = v2r.transform.Find("L");
        if (v2lbl != null) Object.Destroy(v2lbl.gameObject);   // 删掉行标签
        var vt = MakeText(v2r.transform, "Hint", FontHint, new Color(0.62f, 0.62f, 0.68f, 1f), TextAnchor.UpperLeft);
        vt.text = Lang.Tr("比对每个谱面 adofai 文件的内容（哈希），内容完全一致的视为重复。\n检验完成后会自动删除重复谱面（保留主谱面，目录名带「副本/copy」的优先被删）。");
        vt.horizontalOverflow = HorizontalWrapMode.Wrap;
        vt.lineSpacing = 1.25f;
        vt.rectTransform.anchorMin = Vector2.zero; vt.rectTransform.anchorMax = Vector2.one;
        vt.rectTransform.offsetMin = Vector2.zero; vt.rectTransform.offsetMax = Vector2.zero;
        y = 0f;
        var m1 = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("社区评级"), ref y, 1);
    // 批量评级入口 = TUF 社区定级（社区优先；查不到才回落本地 AutoRating）。
    // 老那条「全部本地 AutoRating」的批量路径（StartBatchRating）保留没删 —— 单谱面的
    // 右键「自动评级」和它的口径是同一个 AutoRater，AutoRating 不许删。
        RowButton(m1, TufBatch.StartButtonLabel(), new Color(0.30f, 0.48f, 0.40f, 1f), () => TufBatch.StartOrResume());
        var m2 = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("导出手动标记数据"), ref y, 1);
        RowButton(m2, Lang.Tr("导出"), new Color(0.20f, 0.35f, 0.55f, 1f), () => StartExportMarks());
    // 「异常通关记录」只手动触发：点开先报数（建议清理 N 条 / 建议保留观察 M 条），确认后才删，
    // 删前自动备份 records.txt。口径与理由见 LevelScanner.Stats.cs 的旧记录清理一节。
        var mc = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("异常通关记录"), ref y, 1);
        RowButton(mc, Lang.Tr("检查并清理"), new Color(0.62f, 0.42f, 0.20f, 1f), () => ShowRecordsCheckConfirm());
        var mz = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("删除所有谱面"), ref y, 1);
        RowButton(mz, Lang.Tr("删除全部"), new Color(0.65f, 0.2f, 0.2f, 1f), () => ShowResetAllConfirm());

    // ---------- 主 tab 3 → 子 tab 2 更新：自动检查 + 自动下载安装 ----------
    // 注意：API Key 是内置的，界面里不出现、也不让用户填（所有用户共用）。
    // 「自动检查更新」那个开关已删：更新检查是强制的（Updater.AutoCheck 恒为 true），
    // 否则玩家关掉之后既不检查、开始界面的更新闸门也不拦，出了问题无从排查。
        y = 0f;
        var u1 = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("检查更新"), ref y, 2);
        RowButton(u1, Lang.Tr("立即检查"), Accent, () => Updater.CheckNow(true));

        var u2 = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("打开 Mod 页面"), ref y, 2);
        RowButton(u2, Lang.Tr("打开网页"), new Color(0.25f, 0.5f, 0.35f, 1f), () => {
            try { Application.OpenURL("https://www.adofaitools.top/#/mods?mod=mod-levellibrary&resourceType=MOD"); }
            catch (System.Exception ex) { Lg.Ex("打开 mod 页面失败", ex); }
        });

    // 资源包完整性检查：查出缺多少，缺了就作废版本记录并自己起一次更新检查（下完整包）
        var u2b = MakeRowAt(_setPageObjs[4].transform, Lang.Tr("检查资源包完整性"), ref y, 2);
        RowButton(u2b, Lang.Tr("开始检查"), new Color(0.30f, 0.48f, 0.40f, 1f), () => {
            ShowToast(AssetPack.CheckNow(), 9f);
        });

    // 状态行（每帧刷新）
        var u3 = MakeRowAt(_setPageObjs[4].transform, "", ref y, 2);
    // 高度 = UpdStatusBoxH，跟状态行只有几行字相称（不跟着收会在页面底部空一大片）
        u3.GetComponent<RectTransform>().sizeDelta = new Vector2(_setBodyRt.rect.width, UpdStatusBoxH);
        y += UpdStatusBoxH - RowH;
        var u3lbl = u3.transform.Find("L");
        if (u3lbl != null) Object.Destroy(u3lbl.gameObject);
        _updStatusText = MakeText(u3.transform, "S", 22, new Color(0.85f, 0.9f, 1f, 1f), TextAnchor.UpperLeft);
        _updStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _updStatusText.lineSpacing = 1.22f;
        var u3rt = _updStatusText.rectTransform;
        u3rt.anchorMin = Vector2.zero; u3rt.anchorMax = Vector2.one;
        u3rt.offsetMin = Vector2.zero; u3rt.offsetMax = Vector2.zero;
        _updStatusText.text = Lang.Tr("mod 版本：") + Updater.ReadLocalVersion()
            + Lang.Tr("\n资源包版本：") + Updater.LocalAssetVersionText
            + Lang.Tr("\n状态：") + Updater.Status;
        _updStatusShown = _updStatusText.text;   // 记下来，每帧刷新时只在真变了才重写

    // ---------- 主 tab 5：extra（内容见 PortalUI.Extra.cs） ----------
        BuildExtraPages(_setPageObjs[ExtraTabIndex].transform);
    }

}
