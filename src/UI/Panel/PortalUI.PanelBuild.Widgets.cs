// [split-of] PortalUI.PanelBuild.cs lines 1221-1622 body 16-349

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
    /// <summary>五个 tab 的槽位编号：0 = 主页（官方关卡棋盘）、1 = 库页、2 = 成就页、3 = 设置页、4 = 开发者页。
    /// 设置页不在页面带里（独立挂在画布上），但视觉上占槽位 3，平移基准 = 带子 x + SettingsSlotIndex × 屏宽；
    /// 改 tab 顺序时这个常量必须跟着改，否则设置页会滑到屏幕外或从右边滑进来半格。</summary>
    private const int SettingsSlotIndex = 3;

    /// <summary>切到第 idx 个槽位：整页左移（缓出）+ 设置页同步平移，点 tab 按钮走这条。
    /// 图层不动 —— 设置页仍在页面带之前，切到设置页时隐藏页面带，避免"设置界面被库页盖住 / 整页变黑"；
    /// 设置页按同一节奏平移（起点 = 带子起点 + 它那一格的偏移，终点 = 带子终点 + 同一个偏移）以保留滑动的连续感。</summary>
    private static void SwitchTopTab(int idx) { SwitchTopTabCore(idx, false); }

    /// <summary>不带滑动动画的切换，只有"开面板时沿用上次的 tab"用它：
    /// 开面板那一刻页面带本该直接就在上次那一格，走动画会让玩家先看到官谱库再哗地滑过去。</summary>
    private static void SwitchTopTabInstant(int idx) { SwitchTopTabCore(idx, true); }

    private static void SwitchTopTabCore(int idx, bool instant) {
        // 上界是 MainScreenSlot：主界面也是一个真槽位
        if (idx < 0 || idx > MainScreenSlot || _contentRoot == null) return;
        HideMainScreen();  // 兼容旧调用（现在是槽位切换，它本身已无副作用）
        // instant：已经在这个 tab 上也要往下走 —— 开面板时页面可能还没建过
        if (idx == _topTab && !instant) return;
        int prev = _topTab;
        _topTab = idx;
        NoteUiState();          // 切 tab 是"玩家主动选的位置" → 当场记下
        // 镜像遮罩立即切换、不做渐变（渐变会闪一下明暗）
        SetVeilTarget(VeilTargetFor(idx));
        SyncVeilToTab(true);
        ClearPageVeils(idx);
        // 底部快捷键提示行只属于库页（挂画布上，不随页面带走），切 tab 时同步显隐
        RefreshKeyHintBar();

        // 图层显隐：设置页显示时隐藏页面带 —— 这是设置页能被看见的保证
        bool wantSettings = (idx == SettingsSlotIndex);
        if (_settingsPage != null) _settingsPage.SetActive(wantSettings);
        if (_contentRoot != null) _contentRoot.gameObject.SetActive(!wantSettings);

        // 设置页平移：坐标基准 = 页面带滑到目标位置时的 x + 它那一格的偏移
        float bandFrom = _contentRoot.anchoredPosition.x;
        float bandTo = -RefW * idx;
        float slotOffset = SettingsSlotIndex * RefW;
        _settingsFromX = bandFrom + slotOffset;           // 设置页开局位置
        _settingsSlideActive = true;

        _topFromX = bandFrom;
        _topToX = bandTo;
        if (instant) {
            // 瞬移：带子直接落到目标格 + 页面当场建好 + 导航排颜色当场刷新，不留任何动画中间态
            _topAnimT = 1f;
            _topAnimDur = 0.34f;
            _pendingTopTab = -1;
            _settingsSlideActive = false;
            _contentRoot.anchoredPosition = new Vector2(bandTo, 0f);
            RebuildPage(idx);
            if (wantSettings) { _settingsSlideActive = true; SlideSettingsPage(1f); _settingsSlideActive = false; }
            RefreshTopNav();
        } else {
            _topAnimT = 0f;
            _topAnimDur = Mathf.Clamp(Mathf.Abs(_topToX - _topFromX) / RefW * 0.34f, 0.16f, 0.34f);
            _pendingTopTab = idx;
            _topAnimDoneLogged = false;
        }
        if (LogVerbose) Lg.Info($"[LevelLib] 切tab {prev}→{idx}：带子 {_topFromX:F0}→{_topToX:F0}，设置页 {_settingsFromX:F0}→{(bandTo + slotOffset):F0}{(instant ? "（瞬移）" : "")}");
    }

    /// <summary>「我正停在设置页、可设置页却没显示」这条自愈只报一次，别每帧刷屏。</summary>
    private static bool _settingsHealLogged;

    /// <summary>顶层 tab 每帧：整页左移（缓出）+ 设置页滑入/滑出 + 下划线平移 + 文字渐隐渐显</summary>
    private static void TickTopTabs() {
        if (_contentRoot == null) return;
        // 兜底：当前就停在设置页、可设置页却没显示（后台建页 / 别的流程顺手改了显隐）对玩家就是一片空白，
        // 他唯一的出路是切到别的页再切回来 —— 不管是谁弄没的，只要"现在这一页就是它"就放回来。
        // 根因已在 RebuildPage 改掉，这里只是第二道保险，且只在"它就是当前页"时生效。
        if (_topTab == SettingsSlotIndex && _settingsPage != null && !_settingsPage.activeSelf) {
            SetSettingsPageVisible(true);
            if (!_settingsHealLogged) {
                _settingsHealLogged = true;
                Lg.Info("【兜底】停在设置页、可设置页却没显示 ->  已当场放回来"
                    + "（后台建页/别的流程顺手改了显隐 根因见 RebuildPage 那一段注释 这是第二道保险）");
            }
        }
        if (_topAnimT < 1f) {
            _topAnimT += Time.unscaledDeltaTime / _topAnimDur;
            if (_topAnimT > 1f) _topAnimT = 1f;
            float e = 1f - Mathf.Pow(1f - _topAnimT, 3f);   // easeOutCubic
            float cx = Mathf.Lerp(_topFromX, _topToX, e);
            _contentRoot.anchoredPosition = new Vector2(cx, 0f);
            // 设置页跟着同一条缓动曲线从右侧滑入（目标不是设置页时滑出到右侧）
            SlideSettingsPage(e);
            if (_pendingTopTab >= 0) {
                RebuildPage(_pendingTopTab);
                _pendingTopTab = -1;
            }
            if (_topAnimT >= 1f) {
                _contentRoot.anchoredPosition = new Vector2(_topToX, 0f);
                SlideSettingsPage(1f);
                FinishTabSwitch();
                if (!_topAnimDoneLogged) {
                    _topAnimDoneLogged = true;
                    if (LogVerbose) Lg.Info($"[LevelLib] 滑动动画结束: 目标X={_topToX:F0} 实际X={_contentRoot.anchoredPosition.x:F0} 当前tab={_topTab}");
                }
            }
        }
        RefreshTopNav();
    }

    /// <summary>动画结束后收尾：图层显隐已经在切换瞬间处理完了，这里只清标志。</summary>
    private static void FinishTabSwitch() {
        _settingsSlideActive = false;
    }

    /// <summary>设置页按进度 e 平移，跟页面带同步：
    /// 开局 = 带子起点 + 它那一格的偏移；结束 = 带子终点 + 同一个偏移。</summary>
    private static void SlideSettingsPage(float e) {
        if (!_settingsSlideActive || _settingsPage == null || !_settingsPage.activeSelf) return;
        var rt = _settingsPage.GetComponent<RectTransform>();
        if (rt == null) return;
        float bandTarget = -RefW * _topTab;                 // 带子（若可见）在动画结束时的位置
        float to = bandTarget + SettingsSlotIndex * RefW;   // 设置页终点 = 带子终点 + 它那一格的偏移
        _settingsSlideCur = Mathf.Lerp(_settingsFromX, to, e);
        var p = rt.anchoredPosition;
        p.x = _settingsSlideCur;
        rt.anchoredPosition = p;
    }


    /// <summary>把"玩家主动选的"（切 tab / 选行 / 取消选中）当场记进内存，只改内存，落盘仍由 AutoFeatures.Save() 统一做。
    /// 必须"当场"记：关面板时若读实时字段，那些字段可能已被别处重置成默认值，会把记着的位置冲掉。</summary>
    private static void NoteUiState() {
        LastTopTab = _topTab;
        LastSelectedPath = _selectedPath;
    }

    /// <summary>把"当前停在哪 / 选中谁 / 滚到哪"记下来并落盘，关面板时调。</summary>
    private static void SaveUiState() {
        try {
            // 这里不再用实时字段覆盖记录 —— 记录由 NoteUiState 在动作时写；
            // 两者不一致时打一行日志，方便下次一眼看出是谁把实时状态改掉的
            if (_topTab != LastTopTab || !string.Equals(_selectedPath ?? "", LastSelectedPath ?? "", System.StringComparison.Ordinal)) {
                Lg.Info($"[LevelLib] 【界面状态】关面板时实时状态 tab={_topTab}/选中="
                    + (string.IsNullOrEmpty(_selectedPath) ? "(无)" : Path.GetFileName(_selectedPath))
                    + " 与记录 tab=" + LastTopTab + "/选中=" + (string.IsNullOrEmpty(LastSelectedPath) ? "(无)" : Path.GetFileName(LastSelectedPath))
                    + " 不一致 → 保留记录值（实时值多半被别处重置过）");
            }
            if (_libScroll != null) LastLibScroll = Mathf.Clamp01(_libScroll.verticalNormalizedPosition);
            AutoFeatures.Save();
            Lg.Info($"[LevelLib] 界面状态已记录：tab={LastTopTab}（{TopTabName(LastTopTab)}）"
                + $" 选中={(string.IsNullOrEmpty(LastSelectedPath) ? "(无)" : Path.GetFileName(LastSelectedPath))}"
                + $" 滚动={LastLibScroll:F3}");
        } catch (System.Exception e) { Lg.Ex("记录界面状态失败", e); }
    }

    /// <summary>开面板最后一步：沿用上次的 tab / 选中谱面 / 滚动位置。
    /// 必须排在顶层 tab 初始状态之后（否则 _topTab = 0 会把它冲掉），并用瞬移切 tab。</summary>
    private static void RestoreUiState() {
        try {
            // 必须先把记录值抄到局部：下面 SwitchTopTabInstant 会走进 SwitchTopTabCore，
            // 它为记"玩家点了 tab"而调 NoteUiState —— 那一刻 _selectedPath 还是空的（选中要等下一帧
            // 详情区建好才落实），会把 LastSelectedPath 当场冲成空，之后再去读就已经没有了。
            string wantSel = LastSelectedPath;
            int want = Mathf.Clamp(LastTopTab, 0, MainScreenSlot);  // 允许记住主界面
            SwitchTopTabInstant(want);
            // 选中的谱面不能在这里恢复：详情区是开面板后下一帧才建的，
            // 在这里调 RefreshDetailInfo 时 _detailTitle 那些引用还全是 null，结果是"行选中了、详情区一片空"。
            // 挂起，等 TickPanel 里详情区建好那一刻再落实（见 ApplyPendingRestoreSelection）。
            _uiRestoreSelectedPath = string.IsNullOrEmpty(wantSel) ? null : wantSel;
            if (LastLibScroll > 0.0005f && LastLibScroll < 0.9995f) {
                _libScrollRestore = Mathf.Clamp01(LastLibScroll);
                _libScrollRestoreFrames = 5;  // 前几帧反复落实（内容是分帧算高度的）
            }
            Lg.Info($"[LevelLib] 界面状态沿用：tab={_topTab}（{TopTabName(_topTab)}）"
                + $" 选中={(string.IsNullOrEmpty(_uiRestoreSelectedPath) ? "(无)" : Path.GetFileName(_uiRestoreSelectedPath))}"
                + $" 滚动={LastLibScroll:F3}");
        } catch (System.Exception e) { Lg.Ex("沿用界面状态失败", e); }
    }

    /// <summary>详情区建好之后落实"上次选中的谱面"（开面板时挂起的那件事）。
    /// 布局一起直接摆到终态、不播 0.22 秒的展开动画：开面板本来就该是上次离开时的样子。</summary>
    private static void ApplyPendingRestoreSelection() {
        string want = _uiRestoreSelectedPath;
        _uiRestoreSelectedPath = null;
        if (string.IsNullOrEmpty(want)) return;
        try {
            var e = LevelScanner.Entries != null
                ? LevelScanner.Entries.FirstOrDefault(x => string.Equals(x.FolderPath, want, System.StringComparison.OrdinalIgnoreCase))
                : null;
            if (e == null) {
                Lg.Info("上次选中的谱面已不在列表里（跳过沿用）: " + want);
                return;
            }
            // 必须先把 _selectedPath 清掉：SelectRow 开头是 if (_selectedPath == folder) return;，
            // 不清就等于直接早退、详情区一次都不重画（这是"内容消失"最直接的那一环）
            _selectedPath = null;
            SelectRow(e);
            _libExpand = _libExpandTarget = 0f;  // 详情区展开的终态（选中态）
            ApplyDetailEmptyState();             // 按终态立刻摆一次（内部会 ApplyLibExpand(0)）
            // 详情区是刚建好的，再强制刷一遍固定区 / 文件列表 / 通关记录 / 热力图（都很便宜）
            RefreshDetailInfo(e);
            RefreshDetailFileList(e);
            if (_detailTab == 0) RefreshPickRecordsDetail(e);
            RefreshHeatIfVisible();
            Lg.Info("已沿用上次选中的谱面：" + e.Title);
        } catch (System.Exception e2) { Lg.Ex("沿用选中谱面失败", e2); }
    }

    /// <summary>交互式关闭面板的唯一正确入口 —— Esc / 快捷键 / UMM 按钮都走它。
    /// 它不直接销毁：只把 _closing 立起来、计时归零，剩下的交给 TickCore 里的退出动画（整块透明度 1 → 0，
    /// CloseDuration = 0.3 秒），走完才真的 ClosePanel()（恢复 timeScale / 游戏输入 / 落盘状态都在那里面）。
    /// 三个不该有动画的出口仍然直接调 ClosePanel()（它们要立刻让路给场景加载）：ConfirmOpen 的两处、
    /// OfficialLevels 进官方关卡、Stop() 卸载 mod。
    /// 重入保护：淡出期间再按 Esc / 快捷键不重新计时，否则连按会把动画一直按回开头，就永远关不掉了。</summary>
    public static void RequestClosePanel() {
        if (_panelCanvas == null) { ClosePanel(); return; }  // 画布其实已经不在了 → 直接收尾（防"以为开着"的状态错位）
        if (_closing) return;                                 // 已经在淡出 → 不重入
        _closing = true;
        _closeT = 0f;
        // 淡出期间别再让玩家点到正在消失的界面：只关射线（blocksRaycasts），不动 interactable
        // （那个会把按钮画成灰色，看着像坏了）。跟进场刚好相反 —— 进场那 0.22 秒是故意让人能点的。
        if (_panelGroup != null) _panelGroup.blocksRaycasts = false;
        // 玩家一按关闭就开始压 BGM 音量：等到 ClosePanel（关闭动画走完）才停的话，
        // 那 0.2~0.5 秒音乐还是满音量在响，听上去就是"按了没反应"。这里先起淡出，
        // ClosePanel 里那次 StopMusic 会因"已在淡出"而不重复触发（见 StopMusic 的守卫）。
        if (_musicPlayer != null) _musicPlayer.StopMusic();
        Lg.Info($"[LevelLib] 关闭面板：开始淡出（{CloseDuration:F2}s 这次是「{PanelOpenSource}」打开的）");
    }

    public static void ClosePanel() {
        _panelGroup = null;      // 画布要销毁了，顺手清掉引用
        _panelFadeT = 1f;
        // 大厅画布是场景根对象、不随面板销毁，关闭淡出把它推到 0 的那一下必须在这里补回来（见 ResetArkAlpha）。
        // 补完必须当场把画布关掉：复位这一下 alpha 已经是 1，而画布还 enabled，
        // 等到下一帧 SyncPanelHiddenUnderCover 才收就晚了 —— 中间那帧会整屏亮起来闪一下。
        // 顺序不能反：先复位再关，否则关掉之后 alpha 还停在 0，下次开面板会是一屏透明。
        // 更不能把这两条排到 DisposeArk 之后：DisposeArk 会先把 _arkCanvas / _mainGroup 清成 null，
        // 排它后面两条全成空操作，画布就以 enabled 状态留在场景里 —— 透明但接着吃全部射线，
        // 表现就是「关掉面板之后屏幕上什么都点不动」（实机点击探针点名过：关闭后命中 ArkCanvas/ArkNear/*）。
        ResetArkAlpha();
        SyncArkCanvasEnabled(false);
        DisposeArk();            // 画布已关掉，这里只负责清引用（大厅视图随面板销毁）
        // 下次开面板要重新确认一遍预热的成果还在不在：预制的东西是普通根物体，换场景（进关卡）
        // 会被 Unity 销毁，那时 _pfRefs.Go 变成"假 null"、EnsureProfileBuilt 会自己重建，
        // 但得先让预热的闸门放开。排队预热也一起清（页面挂在面板画布下，画布没了它们就没了）。
        ResetPreloadState();
        SaveUiState();      // 关之前先把状态记下来（画布还活着才能读滚动位置）
        _closing = false;
        Time.timeScale = _prevTimeScale;   // 恢复游戏时间（回退成原始写法，见 OpenPanel 的注释）
        CloseErrorWindow();                // 报错弹窗现在挂在面板画布下，面板销毁前先收掉（防孤儿）
        CloseRowContextMenu(true);         // 右键菜单随面板一起消失（防残留，立刻收掉）
        ClosePetPick();                    // 大肥鱼模型下拉是独立画布，同样要手动收（防残留）
        CloseSplash();                     // 启动欢迎页（独立画布，不跟着面板销毁 → 手动收）
        if (_musicPlayer != null) _musicPlayer.StopMusic();   // 关面板停咱的背景音乐
        ResumeGameMusic();                 // 恢复游戏自己的 BGM
        DeferGameInputEnable();            // 游戏输入延迟几帧再开（避免同一帧被游戏再读一次）
        CloseSettingsPage();               // 设置页一并清掉（防残留）
        CloseMarkDialog();                 // 标记界面一并关掉（防残留）
        CloseLevelPickDialog();            // 关掉关卡选择弹窗（如果有）
        if (_panelCanvas != null) {
            // 保存浏览进度：滚动位置（搜索/筛选/选中状态保留在字段里）
            try {
                var scroll = _libScroll;
                if (scroll != null) _savedScrollY = scroll.content.anchoredPosition.y;
            } catch { }
            Object.Destroy(_panelCanvas.gameObject);
        }
        _panelCanvas = null;
        _panelOpen = false;
        _selectedPath = null;
        _countText = null;
        // 库页展开动画相关引用（随面板一起销毁）
        _listViewportRt = null;
        _libScrollbarRt = null;   // 列表滚动条（随面板销毁）
        _letterColRt = null;      // 字母定位条（随面板销毁）
        _libListRootGo = null;    // 列表整块（随面板销毁）
        _libSearchBgRt = null;
        _libSearchInput = null;   // 搜索输入框引用（随面板销毁）
        _detailAreaRt = null;
        _detailAreaGo = null;
        _detailGroup = null;
        _detailInner = null;   // 详情区内容层（随面板销毁）
        _rankSliderGo = null;  // 难度滑条（随面板销毁）
        _emptyLibHint = null;
        // 空库那一块（提示 + 两个导入按钮）也随面板销毁
        _emptyLibBox = null;
        _emptyImportOneText = null;
        _emptyImportBatchText = null;
        _libExpand = _libExpandTarget = 1f;   // 下次开面板从"未选中、列表拉满"开始
        // 排序方式那个键 + 字母条的淡入淡出载体（都随面板销毁）
        // 排序方式本身（SortByDifficulty）不清 —— 它落盘了、下次开面板要沿用
        ClearSortModeRefs();
        _uiRestoreSelectedPath = null;  // 挂起的"沿用选中"作废（下次开面板重新挂）
        _libScroll = null;      // 列表滚动区随面板销毁
        _libScrollbar = null;   // 滚动条引用一并作废
        _letterFrameRt = null;  // 字母条选中提示随面板销毁
        _letterBtnRt.Clear();   // 字母按钮 RectTransform 缓存同步清掉（下次开面板重新登记）
        _letterBtnText.Clear(); // 字母文字缓存（改字色用）也清掉
        _letterBtnImg.Clear();  // 按钮底 Image 缓存
        _letterBtnBaseCol.Clear();   // 按钮底原始色（恢复高亮时用）
        _letterTextLayer = null;
        _letterHighlightOn = null;
        _letterFrameInit = false;
        _libRoot = null;
        ClearHomeRefs();   // 主页（官方关卡棋盘）的引用也随面板销毁
        _contentRoot = null;
        _tabBarRt = null;   // 信息栏搬到画布上之后 _libIntro 字段整个删掉了（它只当过那个框的父物体）
        ClearNavRefs();     // 导航排随面板销毁：引用必须清干净，否则下次开面板刷已销毁对象
        _devPage = null;
        ClearDevGravityRefs();   // 重力字母（点击掉落）也随页面销毁：池引用、高度图、按键状态一并清空
        // 面板销毁 = 这一场的"键盘彩蛋"到此为止：暗号缓冲清掉（下次开面板重新从头敲）
        Achievements.ResetCcbTyping();
        ClearAchievementsPageRefs();   // 成就页整棵也随面板销毁：引用必须清干净（否则下次开面板访问已销毁对象会抛异常）
        _rowByPath.Clear();
        _detailFileItems.Clear();
        _detailSpecialIcons.Clear();
        _detailPickedFile = null;
        _keyHintBar = null;   // 面板销毁后清引用
        // 详情区 tab 容器随面板销毁，字段清 null 下次打开重建（防已销毁对象残留）
        _detailHeatArea = null;
        _detailFileArea = null;
        _detailFileContent = null;   // 选择关卡 tab 的滚动内容容器（文件行都挂在它下面）
        _detailFileFolder = null;    // 那份列表是给哪个谱面建的 —— 容器没了它也得放掉，否则重开面板不会重建
        _pickRecordArea = null;
        _pickDataArea = null;
        _heatLines = null;
        _heatDots = null;
        _heatStats = null;
        _heatEmpty = null;
        _heatPeakLabel = null;
        _heatAxisY = null;
        _heatFolder = null;
        // 详情区 tab 白框随面板销毁（引用不清会拿着已销毁对象）
        _detailTabRow = null;
        _detailTabFrameRt = null;
        _detailFrameHasPos = false;
        _detailTabs.Clear();
        // 虚拟化：销毁池行（下次打开重建）
        foreach (var pool in _poolRows) {
            if (pool.Go != null) Object.Destroy(pool.Go);
        }
        _poolRows.Clear();
        _logicalRows.Clear();
        _listDirty = false;
    }

    /// <summary>取某个槽位的页面内容根（设置页/开发者页往里塞内容）</summary>
    private static RectTransform PageSlot(int i) {
        if (i < 0 || i >= PageSlotCount) return null;
        return _pageRects[i];
    }

}
