using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：启动欢迎页（Splash）—— 本局第一次打开谱面库时弹一次，也当屏保用
/// 挂在独立画布（sortingOrder 93000，高于面板 80000）：面板里的谱面列表自带嵌套画布，
/// 当面板画布的子物体会被嵌套画布盖住。整屏挡板 raycastTarget = true，任意鼠标键按下即关，
/// 顺带挡住下面的面板，不会误点到列表/按钮；关闭时整块画布销毁（CloseSplash），面板关闭时也一并销毁。
/// 公告内容支持文件覆盖：mod 目录下有 announcement.txt 就用它（每行一条，最多 20 行），没有就显示默认的「感谢使用」。</summary>
public static partial class PortalUI {

    private static Canvas _splashCanvas;
    private static Text _splashHint;         // 「长按任意处进入游戏」（闪烁）
    private static Text _splashTitle;        // 大标题（颜色呼吸）
    // 两个栏的正文 Text 必须留引用：平台数据是开局后异步拉的，而欢迎页在它回来之前就建好了。
    // 所以建页时先显示"缓存/占位"，之后每帧比对一次，数据到了就把正文换掉（欢迎页一局只弹一次，
    // "下次打开这一页"永远不会发生）。
    private static Text _splashChangelogText;   // 左栏「更新日志」的正文
    private static string _splashChangelogShown;  // 当前已经画进那一栏的文本（没变就不碰）
    private static string _splashChangelogSrc;    // 算这段文本时用的数据指纹（见 RefreshSplashBoards）
    private static float _splashT;
    private static bool _splashShown;        // 本局游戏只弹一次
    // 建页那一帧的点击必须忽略：玩家"打开面板的那一下点击"在面板建好之后才被 uGUI 处理
    // （mod 的 Tick 早于 EventSystem），刚建出来的整屏挡板正好接住这一下 → 欢迎页瞬间被关掉，
    // 看着就像"没显示过"。所以那一帧忽略点击，另外再留 0.25 秒宽限防手快连点。
    private static int _splashFrame = -1;
    private static float _splashIgnoreUntil;
    private const float SplashInputGrace = 0.25f;

    // ---------- 长按进入（约 0.75 秒，长按时鼠标周围出现淡灰圆、被主题色填满） ----------
    // 用长按而不是"点任意处关闭"：后者会被别的东西顺手点掉（进游戏时 UMM 的 mod 列表弹窗一关，
    // 那一下点击就把欢迎页同时关了）。
    private static RectTransform _splashHoldRt;   // 长按指示器（跟着鼠标走）
    private static Image _splashHoldFill;         // 主题色填充（Radial360 扇形）
    private static Sprite _splashCircle;          // 圆形贴图（圆角半径 = 半径 → 圆）
    private static bool _splashHolding;
    private static float _splashHoldT;
    private const float SplashHoldSeconds = 0.75f;  // 长按时长（秒）
    private const float SplashRingSize = 37.5f;     // 灰色圆直径
    private const float SplashFillSize = 32.5f;     // 主题色填充直径（比灰圆小，外面留一圈灰边）

    // ---------- 列表淡入（还没长按进入时谱面列表透明度 0，进入之后再 0→100） ----------
    private static CanvasGroup _splashListGroup;    // 谱面列表整块（行/滚动条都在它下面）
    // 字母条的透明度只有 ApplyLetterColAlpha 一个写入点：它要和"排序方式"那一份同时写同一个 CanvasGroup
    // 的 alpha，谁后写谁赢（见 PortalUI.SortMode.cs）。
    private static CanvasGroup _splashTabGroup;     // 顶部五个主 tab（含下划线，它是 tab 栏的子物体）
    private static CanvasGroup _splashHomeGroup;    // 主页棋盘（不透明会从启动页后面透出来）
    private static bool _splashFading;              // 已进入 → 正在把列表淡回来
    private static float _splashFadeT;
    // 个人页淡掉的同时，库界面/主页/tab 淡回来；1.0 秒
    private const float SplashFadeSeconds = 1.0f;

    private const int SplashSortOrder = 93000;      // 高于面板(80000)、低于报错弹窗(99000)
    private const float SplashBlinkHz = 0.45f;      // 闪烁频率（次/秒）

    // ---------- 屏保 ----------
    // 两个开关是独立的两道门，缺一不可地按场景分派：
    // · 面板开着（在谱面库里）→ 只认「开始界面作为屏保」SaverInLib
    // · 面板关着（在游戏里） → 必须 SaverInLib && SaverGlobal 两个都开（SaverGlobal 是"要不要跟着到库外去弹"的附加层）
    // · 「屏保全局显示」不包含库内之外的含义：只开全局、不开第一个，在库里空闲照样不弹
    // · 只有 scnGame（真的在打谱，含暂停/死亡/结算）算忙；编辑器 scnEditor 不算忙（判据是"没在打谱子"），
    // 加一条"编辑器也算忙"会让屏保永远不弹
    // · 屏保必须渐变入（整页挂 CanvasGroup 0→1 走 0.5 秒）；开机首屏是直接出现的，不淡入
    // 默认值只在 settings.txt 里还没有这一项时生效（读设置那段是"有这个键才赋值"），
    // 所以老玩家的文件里一律按文件走，改默认值不会替他们把开关翻过来。
    public static bool SaverInLib = true;           // 设置项：开始界面作为屏保（谱面库内）—— 默认开
    public static bool SaverGlobal;                 // 设置项：屏保全局显示（库外也弹）—— 默认关
    // 休眠时间：四个档位循环点选，单位统一存秒
    public static int SaverIdleSetting = SaverIdleDefault;   // 当前休眠时间（秒）
    private const int SaverIdleDefault = 30;        // 默认 30 秒
    /// <summary>四个档位（秒）—— 顺序就是按钮循环的顺序</summary>
    private static readonly int[] SaverIdleOptions = { 30, 60, 300, 600 };
    private const float SaverFadeInSeconds = 0.5f;  // 渐变入时长
    /// <summary>当前生效的空闲阈值（秒 → float；非法值一律回落到默认 30 秒）</summary>
    private static float SaverIdleTarget {
        get {
            int s = SaverIdleSetting;
            if (s != 30 && s != 60 && s != 300 && s != 600) s = SaverIdleDefault;
            return s;
        }
    }

    /// <summary>休眠时间的显示文案（设置页按钮上的字）</summary>
    public static string SaverIdleText() {
        switch (SaverIdleTarget) {
            case 30: return "30 秒";
            case 60: return "1 分钟";
            case 300: return "5 分钟";
            case 600: return "10 分钟";
            default: return (int)SaverIdleTarget + " 秒";
        }
    }

    /// <summary>点一下换下一档（30 秒 → 1 分钟 → 5 分钟 → 10 分钟 → 回到 30 秒）</summary>
    public static void CycleSaverIdle() {
        int i = 0;
        for (int k = 0; k < SaverIdleOptions.Length; k++) {
            if (SaverIdleOptions[k] == SaverIdleTarget) { i = k; break; }
        }
        SaverIdleSetting = SaverIdleOptions[(i + 1) % SaverIdleOptions.Length];
    }
    private static float _saverIdleT;               // 已空闲时长
    private static Vector2 _saverLastMouse;         // 上一帧鼠标位置（判"有没有动"）
    private static bool _saverMouseInit;
    private static bool _saverActive;               // 屏保正显示着（长按离开后清掉）
    private static float _saverDiagT;               // 诊断行节流（每 5 秒一行）
    private static int _saverDiagLines;             // 诊断行数上限
    private static bool _saverFadePending;          // 下一次 BuildSplash 要不要淡入
    private static float _saverFadeT;
    private static CanvasGroup _splashGroup;        // 整页淡入用（挂在启动页画布上）

    // ---------- 标题「颜色呼吸」 ----------
    // 只动颜色、不动 alpha（alpha 变了就成了"闪烁"，跟下面提示条撞了）；在主题色家族里来回：偏深的蓝 ↔ 偏亮的淡蓝。
    // 频率 0.056 次/秒，一个来回约 17.9 秒，比提示条闪烁(0.45)慢得多。
    private const float SplashTitleBreathHz = 0.056f;
    private const float SplashTitleDeepScale = 0.55f;  // 深端：主题色 ×0.55
    private const float SplashTitleBrightMix = 0.50f;  // 亮端：主题色往白色插值 50%
    /// <summary>深端颜色。用属性而不是静态字段：静态字段初始化顺序跨 partial 文件不保证，
    /// 可能先于 AccentBase 初始化拿到全 0；属性是"用的时候才算"。</summary>
    private static Color SplashTitleDeep {
        get { return new Color(AccentBase.r * SplashTitleDeepScale,
                               AccentBase.g * SplashTitleDeepScale,
                               AccentBase.b * SplashTitleDeepScale, 1f); }
    }
    private static Color SplashTitleBright {
        get { return Color.Lerp(new Color(AccentBase.r, AccentBase.g, AccentBase.b, 1f), Color.white, SplashTitleBrightMix); }
    }

    private const string SplashDefaultNotice = "感谢使用";
    /// <summary>全部文字统一用淡灰色（跟开发者页同一支灰）——
    /// 0.90 那种"浅灰"在深色底上看着就是白，这一支才看得出灰。</summary>
    private static readonly Color SplashTextGray = new Color(0.70f, 0.71f, 0.76f, 1f);

    /// <summary>面板构建完成后调用：本局第一次开面板才真正建出来</summary>
    public static void ShowSplashIfFirstTime() {
        // 新的开始界面（StartScreenIfFirstTime）完全替换旧的启动页/屏保；调用点与函数名都保留。
        if (UseNewStartScreen) { StartScreenIfFirstTime(); _splashShown = true; return; }
        if (_splashShown) return;
        if (_panelCanvas == null) return;
        // 屏保正显示着的时候不能再建一层：旧那张会变成没人再引用得到的孤儿画布，
        // CloseSplash 只销毁当前这张，玩家看到的就是"一层关不掉的界面"（它还会继续接 PointerUp）。
        if (_splashCanvas != null) {
            Lg.Info("启动页：屏保已经在屏幕上 → 跳过这次首屏（不叠第二层）");
            _splashShown = true;
            return;
        }
        _splashShown = true;
        try {
            BuildSplash(false, false);   // 首屏：不带背景（面板自己的背景透出来）
            HideLibraryForSplash();   // 谱面列表先透明（进入后再淡回来）
        } catch (Exception e) { Lg.Ex("启动页构建失败", e); }
    }

    /// <summary>把"库界面的那几块"（谱面列表整块 + 字母条 + 顶部主 tab + 主页棋盘）透明度设为 0。
    /// 用 CanvasGroup 一次性管整棵子树的透明度，不用去改每一行每一个图；tab 下划线是 tab 栏的子物体，一起被带着淡入。
    /// 只动这些，不动面板背景 —— 背景要透出来。</summary>
    private static void HideLibraryForSplash() {
        _splashListGroup = EnsureGroup(_libListRootGo);
        _splashTabGroup = EnsureGroup(_tabBarRt != null ? _tabBarRt.gameObject : null);
        _splashHomeGroup = EnsureGroup(_homeRoot);
        if (_splashListGroup != null) _splashListGroup.alpha = 0f;
        // 字母条不能直接写 alpha —— 它还要乘上"排序方式"那一份（切成按标记难度时它本来就该是透明的）
        SetLetterColFadeK(0f);
        if (_splashTabGroup != null) _splashTabGroup.alpha = 0f;
        if (_splashHomeGroup != null) _splashHomeGroup.alpha = 0f;
        Lg.Info("启动页：谱面列表 + 主页棋盘 + 顶部 tab 已设为透明（进入后一起淡入）");
    }

    private static CanvasGroup EnsureGroup(GameObject go) {
        if (go == null) return null;
        var g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        return g;
    }

    /// <summary>建出整页欢迎界面（独立画布）。
    /// `saver` = 这一页是屏保弹的；`inLibrary` = 屏保是在谱面库里弹的。
    /// 库内弹 / 开机首屏都不加背景：面板自己的背景（含自定义背景图）透出来更好看；
    /// 库外（全局屏保）要加一层整屏深色背景，不然标题和两栏会直接飘在游戏的关卡选择/编辑器画面上。</summary>
    private static void BuildSplash(bool saver, bool inLibrary) {
        // 防叠层：建新的之前先把旧的销毁掉 —— 孤儿画布（没人再引用）是关不掉的。
        if (_splashCanvas != null) {
            Lg.Info("启动页：发现已有一层 → 先销毁旧的（防止叠层关不掉）");
            try { UnityEngine.Object.Destroy(_splashCanvas.gameObject); } catch { }
            _splashCanvas = null;
            _splashHint = null;
            _splashTitle = null;
            _splashChangelogText = null;
            _splashChangelogShown = null;
            _splashChangelogSrc = null;
            _splashHoldRt = null;
            _splashHoldFill = null;
        }

        _splashCanvas = MakeCanvas("LevelLib_Splash", SplashSortOrder);
        var root = _splashCanvas.transform;

        // 整页挂在 CanvasGroup 上做"渐变入"（开机欢迎页不淡入：alpha 直接 1）
        _splashGroup = _splashCanvas.gameObject.AddComponent<CanvasGroup>();
        _saverFadeT = 0f;
        _splashGroup.alpha = _saverFadePending ? 0f : 1f;
        _saverFadePending = false;

        // 库外屏保：整屏背景（在挡板之前建 → 排在层级更下面，内容都在它上面）
        // 跟面板背景同一套：纯色底 + 自定义背景图 + 压暗层
        if (saver && !inLibrary) {
            var back = MakeImage(root, "SplashBgSolid", new Color(BgColor.r, BgColor.g, BgColor.b, 1f));
            back.sprite = null;                 // 整屏不要圆角
            back.type = Image.Type.Simple;
            back.raycastTarget = false;         // 射线交给下面那张透明挡板
            Stretch(back.rectTransform, Vector2.zero, Vector2.one);

            // 背景一律代码绘制（这一页现在不构建，但保留可编译）
            BuildNetBg(root);

            // 压暗层：跟面板一致（0.45 黑）—— 亮背景图上浅灰字会糊，压暗一下才看得清
            var sveil = MakeImage(root, "SplashVeil", new Color(0f, 0f, 0f, 0.45f));
            sveil.sprite = null;
            sveil.type = Image.Type.Simple;
            sveil.raycastTarget = false;
            Stretch(sveil.rectTransform, Vector2.zero, Vector2.one);
        }

        // 整屏挡板：完全透明 —— alpha=0 的 Image 依然会接收射线，所以"点任意处关闭"照旧有效，
        // 底下的面板背景/自定义背景图原样透出来。
        var bg = MakeImage(root, "SplashBg", new Color(0f, 0f, 0f, 0f));
        bg.sprite = null;                 // 整屏不要圆角
        bg.type = Image.Type.Simple;
        bg.raycastTarget = true;
        Stretch(bg.rectTransform, Vector2.zero, Vector2.one);
        var trigger = bg.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerDown, data => {
            // 宽限期：建出来的那一帧 + 前 0.25 秒内的按下一律忽略，
            // 否则"打开面板/关掉 UMM 弹窗的那一下"就会把欢迎页当场弄没
            if (Time.frameCount == _splashFrame || Time.unscaledTime < _splashIgnoreUntil) {
                Lg.Info("启动页忽略了一次过早的按下（防误触）");
                return;
            }
            var pe = data as PointerEventData;
            if (pe != null && pe.button != PointerEventData.InputButton.Left
                && pe.button != PointerEventData.InputButton.Right
                && pe.button != PointerEventData.InputButton.Middle) return;
            // 开始长按：在鼠标处显示灰圆 + 主题色填充从 0 开始涨
            _splashHolding = true;
            _splashHoldT = 0f;
            ShowHoldRing(UiInput.MousePosition);
        });
        AddTrigger(trigger, EventTriggerType.PointerUp, _ => {
            // 提前松手 → 取消（圆环消失、进度归零）
            if (!_splashHolding) return;
            _splashHolding = false;
            _splashHoldT = 0f;
            HideHoldRing();
            Lg.Info("启动页长按中断（松手了）");
        });
        _splashFrame = Time.frameCount;
        _splashIgnoreUntil = Time.unscaledTime + SplashInputGrace;

        // 竖向三段，互不重叠：标题中心 y=140、日期行中心 y=274、次行中心 y=338、两栏顶 400、提示中心 y=895。

        // 标题：字号 190，颜色 = 主题色 AccentBase（不经过 WithPanelAlpha，免得被面板透明度缩一次）
        var titleColor = new Color(AccentBase.r, AccentBase.g, AccentBase.b, 1f);
        var title = MakeTitle(root, "SplashTitle", 190, titleColor, TextAnchor.MiddleCenter);
        SetRectPx(title.rectTransform, 0f, 40f, 1920f, 200f);       // 矩形中心 y=140
        title.text = "LevelLibrary";
        title.fontStyle = FontStyle.Bold;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        title.raycastTarget = false;
        _splashTitle = title;      // 颜色呼吸要在 TickSplash 里每帧改颜色 → 留个引用

        // 日期行 + 星期/干支次行：排版按标题可见高度（190 号拉丁大写约 133）推出来的 ——
        // 标题 73~207、日期行 251~297、次行 320~356，次行底下到两栏顶留 44px，跟"标题→日期行"的 44px 一样。
        // 日期取的是建页那一刻的时间：启动页只活几秒，不需要跟着秒走（也就省了每帧刷新）。
        var dateLine = MakeText(root, "SplashDate", 46, SplashTextGray, TextAnchor.MiddleCenter);
        SetRectPx(dateLine.rectTransform, 0f, 244f, 1920f, 60f);     // 矩形中心 y=274
        dateLine.text = DateTime.Now.ToString("yyyy年M月d日 HH:mm");
        dateLine.horizontalOverflow = HorizontalWrapMode.Overflow;
        dateLine.raycastTarget = false;

        var subLine = MakeText(root, "SplashSub", 36, SplashTextGray, TextAnchor.MiddleCenter);
        SetRectPx(subLine.rectTransform, 0f, 312f, 1920f, 52f);      // 矩形中心 y=338
        subLine.text = SplashWeekdayText() + " · " + GanzhiYearText(DateTime.Now.Year);
        subLine.horizontalOverflow = HorizontalWrapMode.Overflow;
        subLine.raycastTarget = false;

        // 两栏：两块同尺寸 530×420、间距 40 → 总宽 1100，左边距 (1920-1100)/2 = 410 → 整组中心正好 960。
        MakeSplashBoard(root, 410f, "更新日志", DefaultChangelog(), true);
        MakeSplashBoard(root, 980f, "公告", ReadSplashText("announcement.txt", SplashDefaultNotice), false);
        // 玩家正盯着这一页，就别再等"开局 10 秒"那个定时器了 → 把自动检查提前到 1 秒后（只在还没查过时生效）
        try { Updater.HurryCheck(1f); } catch { }

        // 「长按任意处进入游戏」（最下方，闪烁）
        _splashHint = MakeText(root, "SplashHint", 34, SplashTextGray, TextAnchor.MiddleCenter);
        SetRectPx(_splashHint.rectTransform, 0f, 860f, 1920f, 70f);  // 矩形中心 y=895
        _splashHint.text = saver ? (inLibrary ? "长按任意处回到谱面库" : "长按任意处关闭屏保")
                                 : "长按任意处进入游戏";
        _splashHint.horizontalOverflow = HorizontalWrapMode.Overflow;
        _splashHint.raycastTarget = false;
        _splashT = 0f;

        Lg.Info("启动页已显示（本局第一次打开谱面库）");
    }

    /// <summary>启动页的一栏（半透明面板 + 标题 + 分隔线 + 正文）。两栏共用这一个函数，
    /// 保证尺寸/间距/字号完全一致。`isChangelog=true` 时把正文 Text 存下来 —— 平台数据晚到时要就地换掉正文。</summary>
    private static void MakeSplashBoard(Transform root, float left, string titleText, string body, bool isChangelog) {
        const float w = 530f, h = 420f, topY = 400f;
        var board = MakeImage(root, "SplashBoard_" + titleText, new Color(0.10f, 0.08f, 0.16f, 0.55f));
        SetRectPx(board.rectTransform, left, topY, w, h);
        board.raycastTarget = false;

        var bTitle = MakeTitle(board.transform, "NoticeTitle", 34, SplashTextGray, TextAnchor.MiddleCenter);
        SetRectPx(bTitle.rectTransform, 0f, 14f, w, 56f);
        bTitle.text = titleText;
        bTitle.fontStyle = FontStyle.Bold;
        bTitle.raycastTarget = false;

        var line = MakeImage(board.transform, "NoticeLine", new Color(1f, 1f, 1f, 0.14f));
        line.sprite = SharpSprite; line.type = Image.Type.Simple; line.raycastTarget = false;
        SetRectPx(line.rectTransform, 24f, 76f, w - 48f, 2f);

        var bodyText = MakeText(board.transform, "Body", 24, SplashTextGray, TextAnchor.UpperLeft);
        SetRectPx(bodyText.rectTransform, 28f, 96f, w - 56f, SplashBoardBodyH);   // 正文区 300 高
        bodyText.text = body;
        bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        bodyText.verticalOverflow = VerticalWrapMode.Truncate;
        bodyText.lineSpacing = 1.25f;
        bodyText.raycastTarget = false;
        string fitted = FitSplashBody(bodyText, body, SplashBoardBodyH);   // 量着裁（见函数注释）

        if (isChangelog) {
            _splashChangelogText = bodyText;
            _splashChangelogShown = fitted;    // 建页时画的就是这一份（TickSplash 拿它比对）
        }
    }

    /// <summary>"更新日志"那栏正文区的高度（h - 120，栏总高 420）</summary>
    private const float SplashBoardBodyH = 300f;

    /// <summary>把正文裁到"真装得下"为止 —— 用 Unity 自己量的 preferredHeight（它算的才是真换行高度）。
    /// 别按 `\n` 的条数裁剪：一条更新说明可能有 180 字，24 号字在 474 宽的栏里会自己折成 6~10 行，
    /// 按"条数"裁完照样溢出，最后被 Truncate 从半行处切掉。先整行整行地减，
    /// 减到只剩一行还放不下时才按字符砍（只在内容变的时候调用，不是每帧）。</summary>
    private static string FitSplashBody(Text t, string text, float maxH) {
        if (t == null || string.IsNullOrEmpty(text)) return text;
        try {
            var lines = text.Replace("\r", "").Split('\n');
            for (int n = lines.Length; n >= 1; n--) {
                string cand = n == lines.Length ? string.Join("\n", lines) : string.Join("\n", lines, 0, n) + "\n…";
                t.text = cand;
                if (t.preferredHeight <= maxH) return cand;
            }
            string s = text.Replace("\r", "");
            while (s.Length > 8) {                       // 单条日志本身就超长 → 按字符砍
                s = s.Substring(0, s.Length - Mathf.Max(4, s.Length / 10));
                t.text = s + "…";
                if (t.preferredHeight <= maxH) return t.text;
            }
        } catch { }
        return t.text;
    }

    /// <summary>「更新日志」栏的就地刷新：平台数据是异步来的，晚了就补上。由 TickSplash 每帧调用。
    /// 必须先比数据指纹、再算文本 —— DefaultChangelog() 里会读盘（changelog.txt 覆盖文件），
    /// 每帧都算一遍就等于每帧 stat/读一次文件。指纹 = 平台说明 + 是否拿到过详情，没变就直接返回。
    /// 只换那一栏的正文文本，不重建页面 —— 重建会把标题呼吸/长按进度/淡入全打乱。</summary>
    private static void RefreshSplashBoards() {
        if (_splashChangelogText == null) return;
        string src = Updater.ChangelogHistory + "|" + Updater.DetailLoaded;
        if (src == _splashChangelogSrc) return;
        _splashChangelogSrc = src;
        // 量着裁（FitSplashBody 会把最终文本写进 Text，所以下面只比对、不重写）
        string fitted = FitSplashBody(_splashChangelogText, DefaultChangelog(), SplashBoardBodyH);
        if (fitted == _splashChangelogShown) return;
        _splashChangelogShown = fitted;
    }

    /// <summary>更新日志栏内容：当前 mod 版本 + 平台给的更新说明。取值优先级：
    /// ① mod 目录下的 changelog.txt（作者自己写的覆盖文件，最高优先）
    /// ② 这次会话从平台拉到的 Updater.ChangelogHistory
    /// ③ 上次留下的落盘缓存 Updater.CachedChangelog —— 给"欢迎页比平台数据先出现"兜底
    /// ④ 都没有 → 占位：还在拉就说"正在获取"，确实没有就说"平台暂时没有更新说明"
    /// （不能写"下次打开这一页就有了" —— 欢迎页一局只弹一次，那句承诺兑现不了）</summary>
    private static string DefaultChangelog() {
        string ver = "?";
        try { ver = Updater.ReadLocalVersion(); } catch { }
        string local = ReadSplashText("changelog.txt", null);
        if (!string.IsNullOrEmpty(local)) return "当前版本 " + ver + "\n\n" + local;
        string hist = "";
        try { hist = Updater.ChangelogHistory; } catch { }
        if (string.IsNullOrEmpty(hist)) {
            try { hist = Updater.LoadCachedChangelog(); } catch { }     // 上次那份，先顶上来
        }
        if (!string.IsNullOrEmpty(hist)) return "当前版本 " + ver + "\n\n" + ClipLines(hist, 11);
        bool pending = true;
        try { pending = !Updater.DetailLoaded; } catch { }
        return "当前版本 " + ver + "\n\n"
             + (pending ? "（正在获取更新公告…）" : "（平台暂时没有更新说明）");
    }

    /// <summary>星期几（星期日 / 一 / … / 六）：DateTime.DayOfWeek 里 Sunday=0、Saturday=6，
    /// 跟这个数组的下标正好一一对上，不用做任何偏移。</summary>
    private static string SplashWeekdayText() {
        string[] wk = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        int i = (int)DateTime.Now.DayOfWeek;
        if (i < 0 || i > 6) i = 0;
        return wk[i];
    }

    /// <summary>天干地支纪年（2026 → 丙午年）：1984 年是甲子年，所以 (年 - 1984) 对 60 取模，
    /// 天干取 mod 10、地支取 mod 12（负数年份也安全：先 +60 再取模）。
    /// 这是按公历年算的常见写法：农历要到春节才换年，所以除夕前后会跟老黄历差一位。</summary>
    private static string GanzhiYearText(int year) {
        string[] gan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
        string[] zhi = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        int n = ((year - 1984) % 60 + 60) % 60;
        return gan[n % 10] + zhi[n % 12] + "年";
    }

    /// <summary>把多行文本裁到最多 n 行（更新日志可能很长，面板放不下）</summary>
    private static string ClipLines(string text, int maxLines) {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Replace("\r", "").Split('\n');
        if (lines.Length <= maxLines) return text.Replace("\r", "");
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < maxLines; i++) { if (i > 0) sb.Append('\n'); sb.Append(lines[i]); }
        sb.Append("\n…");
        return sb.ToString();
    }

    /// <summary>读启动页某个栏的文本文件（mod 目录下，每行一条，最多 20 行）。
    /// `fallback == null` 表示"没有文件就返回 null"（调用方自己决定用默认内容）；否则返回 fallback。</summary>
    private static string ReadSplashText(string fileName, string fallback) {
        try {
            string p = Path.Combine(Main.ModEntry.Path, fileName);
            if (File.Exists(p)) {
                var lines = File.ReadAllLines(p);
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (var raw in lines) {
                    string s = raw.Replace("\r", "").TrimEnd();
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(s);
                    if (++n >= 20) break;   // 最多 20 行，多了也放不下
                }
                string text = sb.ToString().Trim();
                if (text.Length > 0) return text;
            }
        } catch (Exception e) { Lg.Ex("读取启动页文本失败: " + fileName, e); }
        return fallback;
    }

    // ---------- 屏保驱动 ----------
    // 必须由 TickCore 的"常驻段"每帧调用（面板关着也要跑，不能塞进 TickPanel）——
    // 全局屏保恰恰是"面板没开"的时候才用得上。
    public static void TickScreenSaver() {
        // 屏幕上已经有启动页/屏保了 → 不计时（也绝不叠第二层）
        if (_splashCanvas != null) return;
        if (!SaverInLib && !SaverGlobal) { _saverIdleT = 0f; return; }

        bool inLibrary = (_panelCanvas != null);
        bool busy = SaverBusy();
        bool act = SaverActivity();
        SaverDiag(inLibrary, busy, act);   // 每 5 秒一行诊断（排查"怎么不弹"全靠它）
        if (_splashCanvas != null) { _saverIdleT = 0f; return; }       // 屏幕上已经有启动页/屏保 → 不叠第二层
        // 两个开关按场景分派（语义见文件上方那段）：面板开着只认 SaverInLib；
        // 面板关着必须两个都开 —— 只看"全局显示"那一个的话，
        // SaverInLib=false + SaverGlobal=true 的玩家在库外照样被弹屏保。
        if (inLibrary) {
            if (!SaverInLib) { _saverIdleT = 0f; return; }
        } else {
            if (!SaverInLib || !SaverGlobal) { _saverIdleT = 0f; return; }
        }
        if (busy) { _saverIdleT = 0f; return; }                         // 在打谱/编辑器里 → 永远不弹（并且清零）
        if (act) { _saverIdleT = 0f; return; }                          // 有操作 → 计时归零

        _saverIdleT += Time.unscaledDeltaTime;
        if (_saverIdleT < SaverIdleTarget) return;
        StartScreenSaver(inLibrary);
    }

    /// <summary>现在"忙不忙"（忙就不弹屏保）。两条判据取或：
    /// 一、场景名是 scnGame —— 官方关卡 / 主游戏那边就在这个场景里跑。
    /// 二、`IsLevelRunning()` —— 关卡控制器没暂停，或者状态机还停在跑谱的状态上，自制谱走这一条。
    /// 为什么必须补第二条：自制谱是在 scnEditor 里玩的，只看场景名分辨不出
    /// 「在打谱」和「在编辑器里闲着」；玩家开着自动播放时手不用碰键鼠、
    /// 空闲计时一路攒满，屏保就盖到正在跑的谱子上了。正常打谱时键鼠一直在动、
    /// 计时攒不起来，所以这个口子早先没暴露。
    /// 补的这条判据不能只写「控制器在不在」或「isGameWorld」：实测日志里编辑器闲置时
    /// 控制器还在、isGameWorld 恒真、paused 也是真，拿它们判会把编辑器闲置也算成在玩，
    /// 屏保就永远不弹了（单用 isGameWorld 踩过同一个坑，那个字段的反编译默认值就是 true）。
    /// 也不能只写「状态机离开 Start」：补丁挂在 ChangeState 上，
    /// 而某些退出路径下游戏是直接改状态字段的，补丁记不到 ——
    /// 实测日志里玩家已经退回编辑器闲着，补丁记的值还停在 PlayerControl。
    /// 所以那一条是"没暂停"与"状态机非 Start"取或，具体取舍写在 IsLevelRunning 的注释里。
    /// 也不用 IsLevelPlaying：它多一条「游戏世界已建立」，
    /// 而屏保要求暂停也算在打谱（不然玩家一暂停、屏保盖上来，回来看到的就是一页欢迎界面）。
    /// 读不到场景时返回 true（宁可不弹，也别在打谱时盖住玩家）。</summary>
    private static bool SaverBusy() {
        // 进入谱面的过渡层在跑（入场 / 等砖块 / 等装饰物加载 / 淡出）→ 一律算忙：
        // 这段时间面板已经关了、场景还是 scnEditor，光看场景名拦不住，开「屏保全局显示」的人会被盖住
        // （屏保画布 93000 压在过渡层之上）。这不是把 scnEditor 算成忙 —— 编辑器里闲置照旧弹。
        if (_etGo != null) return true;
        try {
            string scn = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scn == "scnGame") return true;
        } catch { return true; }
        return IsLevelRunning();
    }

    /// <summary>屏保诊断：每 5 秒往日志写一行"现在各个闸门的状态"，行数有上限（写完就闭嘴）。
    /// 屏保"不弹"可能是被任何一个闸门挡住，光看日志里"没有弹出记录"分不清是谁。</summary>
    private static void SaverDiag(bool inLibrary, bool busy, bool act) {
        if (!SaverInLib && !SaverGlobal) return;
        if (_saverDiagLines >= 30) return;
        _saverDiagT += Time.unscaledDeltaTime;
        if (_saverDiagT < 5f) return;
        _saverDiagT = 0f;
        _saverDiagLines++;
        try {
            string scn = "?";
            bool isGame = false, gw = false, paused = false, ctrlAlive = false;
            try { scn = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            try { isGame = ADOBase.isScnGame; } catch { }
            try { var c = GetCachedConductor(); gw = (c != null && c.isGameWorld); } catch { }
            try { var ctrl = AutoFeatures.GetCachedController(); ctrlAlive = ctrl != null; paused = (ctrl != null && ctrl.paused); } catch { }
            // 把关卡状态的两个来源并排打出来：当前状态是直接读状态机的真值，
            // 补丁状态是 ChangeState 补丁记的历史值。两者不一致就说明游戏走了
            // 不经 ChangeState 的退出路径，判据必须信前者。
            // 控制器/isGameWorld/paused 三项也留着：它们看不出在不在跑（编辑器闲置时三项分别是
            // 在/true/true），写在这里是为了跟状态机对照，别拿它们当判据。
            Lg.Info("屏保诊断：场景=" + scn + " isScnGame=" + isGame
                + " isGameWorld=" + gw + " paused=" + paused
                + " 控制器=" + (ctrlAlive ? "在" : "无")
                + " 当前状态=" + (CurrentStateName() ?? "?") + " 补丁状态=" + HookedState
                + " 关卡内=" + IsLevelRunning()
                + " 面板=" + (inLibrary ? "开" : "关")
                + " 进入过渡=" + (_etGo != null ? "是" : "否")
                + " 空闲=" + _saverIdleT.ToString("F1") + "s busy=" + busy + " 有操作=" + act
                + " 阈值=" + (int)SaverIdleTarget + "s");
        } catch { }
    }

    /// <summary>这一帧玩家有没有操作（鼠标动过 2px 以上 / 按过任何键或鼠标键）。
    /// 走 UiInput（跟界面其它地方一样读真实输入，直接读 UnityEngine.Input 会被 UI map 的闸门挡掉）；
    /// 读不到输入就当"有操作"，宁可不弹。</summary>
    private static bool SaverActivity() {
        try {
            Vector2 m = UiInput.MousePosition;
            if (!_saverMouseInit) { _saverMouseInit = true; _saverLastMouse = m; return true; }
            if ((m - _saverLastMouse).sqrMagnitude > 4f) { _saverLastMouse = m; return true; }   // 动了 2px 以上
            if (UiInput.AnyKeyDown) return true;
        } catch { return true; }      // 读不到输入就当"有操作"（宁可不弹）
        return false;
    }

    /// <summary>弹出屏保：建出这一页（渐变入）+（库内时）把谱面列表/字母条/顶部 tab/主页棋盘隐掉。</summary>
    private static void StartScreenSaver(bool inLibrary) {
        _saverIdleT = 0f;
        _saverActive = true;
        _saverFadePending = true;     // 让 BuildSplash 把整页 alpha 从 0 开始涨
        try {
            if (UseNewStartScreen) {
                // 新屏保自己渐入（见 StartScreenAsSaver），旧的 _saverFadePending 用不上了 → 清掉，
                // 不清的话它会一直挂着 true，万一以后建了启动页会莫名从 0 淡入。
                _saverFadePending = false;
                StartScreenAsSaver();
                _saverActive = true; _saverIdleT = 0f; return;
            }
            BuildSplash(true, inLibrary);            // 旧屏保（回退时用）
            if (inLibrary) HideLibraryForSplash();   // 库里弹的：列表先透明，长按离开时再淡回来
            Lg.Info("屏保：无操作 " + (int)SaverIdleTarget + " 秒 → 渐变入开始界面（"
                + (inLibrary ? "谱面库内" : "全局") + "，长按离开）");
        } catch (Exception e) {
            _saverActive = false;
            _saverFadePending = false;
            Lg.Ex("屏保构建失败", e);
        }
    }

    /// <summary>闪烁 + 长按进度 + 列表淡入 驱动（由 TickCore 常驻段每帧调用；全部结束后零开销）</summary>
    private static void TickSplash() {
        // ---------- 列表/顶部 tab 淡入：进入之后 0 → 100 ----------
        if (_splashFading) {
            // 单帧步进钳住：这一段正好紧跟在"面板刚建完"那一帧之后，大 dt 会一步走完
            _splashFadeT += Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);
            float a = Mathf.Clamp01(_splashFadeT / SplashFadeSeconds);
            if (_splashListGroup != null) _splashListGroup.alpha = a;
            // 字母条走 SetLetterColFadeK（它内部会乘上排序方式那一份，见 SortMode 那支）
            SetLetterColFadeK(a);
            if (_splashTabGroup != null) _splashTabGroup.alpha = a;
            if (_splashHomeGroup != null) _splashHomeGroup.alpha = a;   // 主页棋盘一起淡入
            if (a >= 1f) {   // 结束：把引用清掉（下次开面板重新登记）
                _splashFading = false;
                _splashFadeT = 0f;
                _splashListGroup = null;
                _splashTabGroup = null;
                _splashHomeGroup = null;
            }
        }
        if (_splashCanvas == null || _splashHint == null) return;

        // 平台数据晚到 → 就地补进「更新日志」那一栏
        RefreshSplashBoards();

        // ---------- 屏保「渐变入」：整页 alpha 0 → 1 ----------
        if (_splashGroup != null && _splashGroup.alpha < 1f) {
            _saverFadeT += Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);   // 同上，单帧步进钳住
            _splashGroup.alpha = Mathf.Clamp01(_saverFadeT / SaverFadeInSeconds);
        }

        _splashT += Time.unscaledDeltaTime;
        // 平滑呼吸式闪烁：0.25 ～ 1.0 之间来回
        float k = 0.5f + 0.5f * Mathf.Sin(_splashT * Mathf.PI * 2f * SplashBlinkHz);
        var c = _splashHint.color;
        c.a = 0.25f + 0.75f * k;
        _splashHint.color = c;

        // ---------- 标题颜色呼吸：深蓝 ↔ 亮淡蓝 来回（alpha 恒定 1，只呼吸"色"不呼吸"亮暗"） ----------
        if (_splashTitle != null) {
            float bk = 0.5f + 0.5f * Mathf.Sin(_splashT * Mathf.PI * 2f * SplashTitleBreathHz);
            _splashTitle.color = Color.Lerp(SplashTitleDeep, SplashTitleBright, bk);
        }

        // 长按中：圆环跟着鼠标、主题色填充按 SplashHoldSeconds 涨满 → 满了就进入
        if (!_splashHolding) return;
        _splashHoldT += Time.unscaledDeltaTime;
        if (_splashHoldFill != null) _splashHoldFill.fillAmount = Mathf.Clamp01(_splashHoldT / SplashHoldSeconds);
        MoveHoldRingToMouse();
        if (_splashHoldT >= SplashHoldSeconds) {
            _splashHolding = false;
            CloseSplash("长按完成");
        }
    }

    /// <summary>在鼠标位置显示长按圆环（淡灰圆 + 主题色扇形填充，填充从 0 开始）</summary>
    private static void ShowHoldRing(Vector2 mouseScreenPos) {
        if (_splashCanvas == null) return;
        if (_splashCircle == null) _splashCircle = CreateRoundedSprite(64, 32f);   // 半径=半边长 → 正圆
        if (_splashHoldRt == null) {
            var go = new GameObject("SplashHold", typeof(RectTransform));
            go.transform.SetParent(_splashCanvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);   // 画布中心为原点 → 可以直接用"局部点"
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(SplashRingSize, SplashRingSize);
            _splashHoldRt = rt;

            // 淡灰色圆底
            var ring = MakeImage(rt, "HoldRing", SplashTextGray);
            ring.sprite = _splashCircle;
            ring.type = Image.Type.Simple;
            ring.raycastTarget = false;
            ring.color = new Color(SplashTextGray.r, SplashTextGray.g, SplashTextGray.b, 0.35f);
            Stretch(ring.rectTransform, Vector2.zero, Vector2.zero);

            // 主题色填充（Radial360 扇形，从正上方开始顺时针填满）
            var fill = MakeImage(rt, "HoldFill", Accent);
            fill.sprite = _splashCircle;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            fill.color = new Color(Accent.r, Accent.g, Accent.b, 1f);   // 主题色本体（Accent 自带 0.65 透明度 → 这里拉满）
            var frt = fill.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.sizeDelta = new Vector2(SplashFillSize, SplashFillSize);
            frt.anchoredPosition = Vector2.zero;
            _splashHoldFill = fill;
        }
        _splashHoldRt.gameObject.SetActive(true);
        MoveHoldRingToMouse(mouseScreenPos);
    }

    /// <summary>把长按圆环挪到鼠标处（屏幕坐标 → 画布局部坐标；Overlay 画布 cam 传 null）</summary>
    private static void MoveHoldRingToMouse(Vector2? screenPos = null) {
        if (_splashCanvas == null || _splashHoldRt == null) return;
        var canvasRt = _splashCanvas.GetComponent<RectTransform>();
        if (canvasRt == null) return;
        Vector2 sp = screenPos.HasValue ? screenPos.Value : (Vector2)UiInput.MousePosition;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out Vector2 lp))
            _splashHoldRt.anchoredPosition = lp;
    }

    private static void HideHoldRing() {
        if (_splashHoldFill != null) _splashHoldFill.fillAmount = 0f;
        if (_splashHoldRt != null) _splashHoldRt.gameObject.SetActive(false);
    }

    /// <summary>关闭启动页（点任意处 / 关面板都会走这里）：整块画布销毁 + 引用清空。
    /// `why` 会写进日志 —— 以后再看"欢迎页一闪而过"就知道是谁关的。</summary>
    public static void CloseSplash(string why = "未标注") {
        bool wasSaver = _saverActive;   // 屏保弹出来的那一页 → 日志里写"屏保已离开"，跟开机首屏区分开
        if (_splashCanvas != null) {
            try { UnityEngine.Object.Destroy(_splashCanvas.gameObject); } catch { }
            Lg.Info("" + (wasSaver ? "屏保已离开" : "启动页已关闭") + "（原因：" + why + "）");
        }
        _splashCanvas = null;
        _splashHint = null;
        _splashTitle = null;       // 标题随画布一起销毁
        _splashChangelogText = null;   // 更新日志正文随画布一起销毁（下次建页重新登记）
        _splashChangelogShown = null;
        _splashChangelogSrc = null;
        _splashHoldRt = null;      // 长按圆环随画布一起销毁
        _splashHoldFill = null;
        _splashHolding = false;
        _splashHoldT = 0f;
        // 离开屏保：状态清干净 + 空闲计时归零（长按这一下本身就是"操作"，
        // 归零能保证"离开之后马上又弹出来"不会发生）
        _splashGroup = null;
        _saverActive = false;
        _saverFadePending = false;
        _saverFadeT = 0f;
        _saverIdleT = 0f;
        // 关闭后把谱面列表从 0 淡回 1。只有"列表确实被隐藏过"才启动淡入 ——
        // 关面板时调用这里时列表已经被销毁，CanvasGroup 会变成 Unity 的 fake-null，TickSplash 里的判断自然跳过。
        if (_splashListGroup != null || _splashTabGroup != null || _splashHomeGroup != null) {
            _splashFading = true;
            _splashFadeT = 0f;
        }
    }
}
