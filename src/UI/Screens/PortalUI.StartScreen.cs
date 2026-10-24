using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：开始界面 / 待机界面（同一个界面兼任屏保）。
/// 整屏封面（背景图 + 遮罩，视频背景就绪时由视频层顶掉背景图）+ 标题/两行小字/「上滑进入」。
/// 按下记起点、上移跟手，松手按屏高 1/4 分流：超过则滑出（库内首屏 = 进入谱面库，屏保 = 关掉），否则滑回。
/// 滑出过程里封面按拖动进度让位、登录界面（个人页内容层）按同一进度渐显。</summary>
public static partial class PortalUI {

  /// <summary>开始/待机界面用新版。改 false 就退回旧的呼吸标题那套（用开关分流而不是删掉旧代码）</summary>
    private static readonly bool UseNewStartScreen = true;

    private static GameObject _ssGo;          // 整屏画布根
    private static CanvasGroup _ssGroup;      // 整层透明度（只用于渐入：拖动/滑出/滑回期间不碰它）
  /// <summary>整层渐入（0 → 1）：首屏与屏保两个入口都要（见 ShowStartScreen）</summary>
    private static bool _ssFadeIn;
    private static float _ssFadeInT;
    private static RectTransform _ssRootRt;   // 要往上滑的那块（标题/两行小字/提示都在里面）
    private static Text _ssTitle, _ssHint;
    private static Text _ssTitleShadow;        // 标题的投影（另铺的一层深色文字，透明度跟标题同一个系数）
    private static Text _ssSub1, _ssSub2;      // 标题下面那两行小字（All in ONE / Coding by FAC）
  // 更新提示那一块：整块挂一个 CanvasGroup，透明度与标题/提示同一套（见 SsPaintTexts），
  // 闸门没开着时整块 alpha 归零而不是销毁 —— 建一次就一直在，避免开关时重建画布
    private static CanvasGroup _ssUpdGroup;
    private static Text _ssUpdText;
    private static GameObject _ssUpdBar;       // 步进轨道本体（没有步进语义时整块关掉）
    private static readonly Image[] _ssSteps = new Image[Updater.GateStepCount];
    private static readonly Image[] _ssStepFills = new Image[Updater.GateStepCount];
    private static bool _ssUpdBarOn;           // 轨道当前是开着的吗（只在变的时候写）
    private static int _ssStepDone = -1;       // 上次画到第几格
    private static int _ssStepFillPx = -1;     // 上次那一格的充能宽度（像素）
  /// <summary>每颗药丸当前的宽度（像素）：目标宽度一换就指数靠拢 —— 换格时上一根缩回、
  /// 下一根长出来、其余的点被柔和推开，硬切是看不到"推开"的</summary>
    private static readonly float[] _ssStepW = new float[Updater.GateStepCount];
  /// <summary>每颗药丸当前的底色（0 = 待走、1 = 已走）：跟着淡过去，不硬切</summary>
    private static readonly float[] _ssStepK = new float[Updater.GateStepCount];
  /// <summary>显示用的格号与它在屏幕上停了多久。真实格号可能在几十毫秒里连跳好几格
  /// （网络失败换线就是一瞬间的事），给它一个最小停留时间：一格一格地走，眼睛才跟得上。</summary>
    private static int _ssStepShow = -1;
  /// <summary>显示那一格当前的填充（0~1）：真实格号走在前头时它会平滑补到 1，满了才让位</summary>
    private static float _ssFillShown;
    private static float _ssStepHoldT;
    private static int _ssGateLogs;            // 被闸门挡下的次数（日志限量）
    private static bool _ssShown;             // 本局是否已经弹过（首屏只弹一次）
    private static bool _ssFromLib;           // true=库内首屏（滑走=进入库）false=屏保（滑走=关掉）

    private static bool _ssDragging;
    private static float _ssDownY, _ssOffset; // 按下时的屏幕 y / 当前上移量（像素）
    private static float _ssBreathT;          // 「上滑进入」呼吸的时间累加
    private static float _ssFadeK = 1f;       // 拖动淡出系数（0~1）：呼吸乘在它上面，两者不能互相覆盖
    private static float _ssEnterK = 1f;      // 分拍入场进度（0~1）：每次摆好那层从 0 涨到 1
    private static int _ssPhase;              // 0=停着 1=跟手 2=滑出 3=滑回 4=收尾
  // 更新提示块与「上滑进入」各自的平滑量（0~1），都朝 Updater 那边的目标指数跟随。
  // 分开两个：闸门挡着时「上滑进入」要收掉、而更新提示正是要显示的那一块，两者方向相反。
    private static float _ssUpdK, _ssGateK = 1f;
    private static bool _ssGateNoticeInit;    // 第一次计算时直接落到目标，不让启动页露一下过渡
  // 甩动判定用的三个值：手指最近一次采样到多快（px/s）、上次采样的距离与时刻
    private static float _ssVel, _ssVelUp, _ssVelT;
    private static float _ssT;

    private const float SsRootTop = 450f;     // 内容块用中心锚点：块的顶边就在屏幕中心上方 SsRootTop
  /// <summary>开始界面画布排序号：必须高于个人页（93000）—— 屏保那条路是"个人页已经在了、30 秒后才建开始界面"，
  /// 同号时开始界面会被压在下面。同时仍要低于 95000（生日/周报）与 99000（报错弹窗）。</summary>
    private const int SsSortOrder = 93100;
    private const float SsTitleTop = 130f;    // 标题框顶（相对内容块顶；改这里 = 标题上下移动）
    private const int SsTitleFont = 180;
    private const float SsHintTop = 860f;     // 「上滑进入」框顶（放在页面底端）
    private const int SsHintFont = 38;
  // 标题下那两行小字：字号只有标题的 1/5 上下
  //（框高 = 字号 + 16、两行之间留 8px；摆位按框算，按字算两行会互相压）
    private const float SsSub1Top = 372f, SsSub2Top = 456f;
    private const int SsSub1Font = 60, SsSub2Font = 52;
    private const float SsBreathPeriod = 1.8f;// 「上滑进入」呼吸一轮的秒数
    private const float SsBreathMin = 0.30f;  // 呼吸最淡到多少（再淡就看不见了）

  // ---------- 更新提示（那行字 + 一条进度条）----------
  // 位置在「上滑进入」上面一点：提示块顶 780、块高 68（行 0~46、进度条 56~68），提示那行框顶 860
    private const float SsUpdTop = 730f;
    private const float SsUpdBh = 124f;
    private const int SsUpdFont = 40;
    private const float SsUpdBarTop = 66f;
  // 步进轨道：一条圆角"轨道"里排十颗小药丸 —— 走过的亮、当前那颗拉长成一条会充能的进度条、没到的灰。
  // 尺寸比例照那套 StepPlayer 的 RATIO：dot = 轨道高 × 0.115、bar = dot × 8.2、gap = 轨道高 × 0.18。
    private const float SsStepTrackW = 640f, SsStepTrackH = 56f;
    private const float SsStepDot = 8f;
    private const float SsStepGap = 8f;
    private const float SsStepBarW = 66f;
  /// <summary>宽度的跟随速度（指数平滑，1/s）。源码那边是 spring(duration 0.42, bounce 0.14)，
  /// 折成指数平滑约等于每秒 7：换格时上一根缩回、下一根长出来、其余的点被柔和推开。
  /// 颜色另外按 0.3 秒淡（对应源码那个 transition-colors duration-300）。</summary>
    private const float SsStepFollow = 7f;
    private const float SsStepColorFollow = 10f;
  /// <summary>每一格最少在屏幕上停多久（秒）：太快的换格看不出"跑满再让位"</summary>
    private const float SsStepMinDwell = 0.30f;
  /// <summary>补齐那一格的跟随速度（指数平滑，1/s）：约 0.25 秒补满</summary>
    private const float SsStepFillFollow = 12f;
  /// <summary>更新提示块与「上滑进入」收放时的跟随速率（指数平滑，1/s）：约半秒走完大半。
  /// 结论出来的那一下不再让它们瞬间跳变 —— 原本更新提示是整块 alpha 直接归零、
  /// 「上滑进入」是从压暗值直接弹回全亮，两头都很硬。</summary>
    private const float SsGateNoticeFollow = 6f;
  /// <summary>被闸门挡下时最多记几条日志（连点几十下别刷屏）</summary>
    private const int SsGateLogMax = 6;
  // 滑出（相位2）/ 滑回（相位3）/ 收尾（相位5）三个相位的基准时长。
  // 原 0.55；用户要求上滑再快一点，收到 0.45（与速度帽一起收才有效果，见 SsSlideMinSeconds）。
    private const float SsOutSeconds = 0.45f;
    private const float SsMaxRise = 260f;     // 拖到上限时内容上移多少（进度与淡出都按它算比例）
  // 跟手比例写成 0.5 时手指上移 300px 内容只走 150px，手感就是"只能拖动一半"
    private const float SsFollowK = 1f;       // 内容跟手比例（1 = 严格跟手，不许再写 0.5）
  // 手指最多能拖多少（× 屏幕高）：必须小于滑出动画的目标距离（SsMaxRise × 4），
  // 否则"已经拖得比目标还高"时松手，内容会往回退一下（看着像抖）
    private const float SsDragMaxK = 0.6f;

  // ---------- 松手之后自己走的那一段：统一速度帽 ----------
  // 这条动画是"追着目标走"的写法（Lerp(现在, 目标, 系数)），一帧能吃掉剩余距离的一大块，
  // 所以速度正比于剩余距离、越接近收尾越快（实测去程峰值 4169 px/s，回程在落地那一帧还有 2395）。
  // 定稿：一帧最多走 SsSlideMaxPxPerSec × dt 像素（唯一实现在 SsClampSlideStep）。
  // 掐的只是位置：手指拖着的那一段一个字都不掐（那是 1:1 跟手）。
  /// <summary>整段（SsMaxRise × 4 = 1040px）最少用多少秒走完 —— 速度帽由它推出（别单独改速度帽）。
  /// 0.8 → 0.6 → 0.5（本轮）：帽 ≈ 2080 px/s，配合 SsOutSeconds 收到 0.45，整段滑完约半秒。
  /// 下限约束：必须比 SsOutSeconds(0.45) 大，否则等于没掐；上限也不能太大（1.5 秒往上会像卡住了）。
  /// 上滑滑出 / 从登录界面下滑的回程（_ssReturnK）/ 空格·回车那条路 共用这一个帽，改这里三个一起变快。</summary>
  private const float SsSlideMinSeconds = 0.5f;
  /// <summary>速度帽（px/秒）：从 SsSlideMinSeconds 推出来，别单独改它</summary>
    private const float SsSlideMaxPxPerSec = SsMaxRise * 4f / SsSlideMinSeconds;
  /// <summary>兜底时限 = 原时长 + 整段按帽速走完的时间：速度帽会让动画比原时长稍晚收尾，
  /// 相位收尾拿它当"最晚什么时候一定结束"，任何情形都不可能超。</summary>
    private const float SsSlideHardSeconds = SsOutSeconds + SsMaxRise * 4f / SsSlideMaxPxPerSec;
  /// <summary>甩动判定（屏幕像素/秒）：松手时手指速度到这条线，就算拖动距离没到屏高 1/4 也照滑出。
  /// 起因是玩家报的"长按快速上滑：登录界面出现一下又闪回去" —— 快速甩动时拖动阶段能一帧把画面推到全透明，
  /// 但判据只看手指走了多远，甩得快而幅度小的那几下就被判成"不够"，于是又滑回去。
  /// 900 是量出来的：慢慢拖大约 200~600 px/s，甩一下 1500 往上。</summary>
    private const float SsFlingPxPerSec = 900f;

  // ---------- 两段式过渡：先滑内容，后揭幕 ----------
  // 拖动与滑出（相位 1/2）只动开始界面自己的内容（标题/两行小字/提示上移渐隐）：
  // 封面保持完全不透明、登录界面一个像素都不露、不放大也不糊 —— 底下那页直到揭幕前都碰不到。
  // 滑出走完进入揭幕段（相位 6）：封面让位（1→0）、背景模糊跟着涨、封面同步放大到 SsZoomMax、
  // 登录界面（个人页内容层）0→1 渐显 —— 四件事走同一条进度，看着就是"滑完之后背景放大着糊过去、登录界面浮现"。
  // 个人页背后那张底与封面是同一张背景图 + 同一档 0.45 遮罩，交叉让位时背景本身看不出跳变。
  /// <summary>揭幕段时长：封面让位 / 模糊 / 放大 / 登录界面渐显共用这一条进度。</summary>
    private const float SsRevealSeconds = 0.6f;
    private static bool _ssScrub;             // 这次拖动由我们驱动个人页的透明度（见 SsScrubBegin）
    private static bool _ssPrepared;          // 本局是否已经"空闲时把个人页备好"（只做一次）
    private static bool _ssParked;            // 上滑交接后收起来留着（封面 0 / 不吃射线）：留着的这层就是"往下滑回开始界面"要淡回来的东西
    private static bool _ssBehind;            // 封面底下是"登录界面"（可以让开）—— 拿不准就不让
    private static CanvasGroup _ssCoverGroup; // 封面容器（Bg / 代码背景 / Veil 的统一透明度，见 SsApplyScrub）
    private static CanvasGroup _ssCoverNet;   // 封面里那块代码背景（收起来时整组关掉，见 ParkStartScreenLayer）
    private static Image _ssCoverStatic;      // 封面里的静态背景图层（收起来时一起关掉，见 ParkStartScreenLayer）
    private static RawImage _ssCoverVideo;    // 封面里的视频层（视频可用时挂；收起来时一起停，见 ParkStartScreenLayer）
    private static Image _ssVeilImg;          // 封面里那层遮罩（SsPaintCoverVeil 的反补偿要写它；真正的让位走容器）
    private static Color _ssVeilBase;   // 遮罩图的基准色（拖动时只改 alpha，RGB 不动）

  // ---------- 过渡时的背景放大 ----------
  // 上滑进登录界面的整段过渡里，封面容器（纯色底/代码背景/视频/糊图/遮罩那一整块）随让位进度一起放大，
  // 看着就是"背景在往里推"。过渡收尾、层收起来那一刻起再平滑缩回 1 —— 那时封面已经全透明，回缩是隐形的，
  // 下一次开始界面摆回来时缩放已经落回原大（回缩没走完就被摆回来的情形在 UnparkStartScreen 里直接归位）。
    private const float SsZoomMax = 1.12f;    // 过渡终点的缩放（封面完全让开那一下）
    private const float SsZoomFollow = 4f;    // 收尾回缩速率（指数平滑，1/s）：约 0.8 秒落回原大
    private static float _ssZoom = 1f;        // 封面当前的缩放（唯一写入点 SsApplyCoverZoom）
    private static bool _ssZoomRestore;       // 过渡结束、开始往回缩（置起后每帧缩回 1）

  /// <summary>封面缩放的唯一写入点：z = 目标缩放（1 = 原大）。拖动/滑出阶段由 SsApplyScrub 按让位进度喂，
  /// 收尾之后由 TickStartScreen 里的回缩段平滑落回 1。</summary>
    private static void SsApplyCoverZoom(float z) {
        _ssZoom = Mathf.Clamp(z, 1f, SsZoomMax);
        if (_ssCoverGroup != null) _ssCoverGroup.transform.localScale = new Vector3(_ssZoom, _ssZoom, 1f);
    }
  /// <summary>"从登录界面滑回开始界面"这一趟的实际进度（0 = 还在登录界面 / 1 = 回到开始界面）。
  /// 单独存它是因为速度帽会让"实际"落后于"想"，而位置与那三样透明度都必须按实际值走 ——
  /// 只掐位置的话就成了"字都淡进来了却还悬在屏幕上面"。</summary>
    private static float _ssReturnK;

  /// <summary>库内首屏：本局第一次开面板时弹（旧的启动页就是挂在这里的）。建出来先全透明，再整层渐入</summary>
    public static void StartScreenIfFirstTime() {
        if (_ssShown) return;
        _ssShown = true;
    // 这一屏要把本局的更新检查盯完才放人（提示与禁上滑都读它，见 Updater.Gate）。
    // 排在建层之前：第一帧画出来时那行提示就已经在了
        Updater.ArmUpdateGate();
        ShowStartScreen(true);
    }

  /// <summary>屏保触发（空闲到时间）也弹它：屏保模式自己渐入，时长复用旧屏保的 SaverFadeInSeconds。
  /// 至此两个入口（首屏 / 屏保）都渐入，所以"是否渐入"不再是参数，直接写死在建层里。</summary>
    public static void StartScreenAsSaver() { ShowStartScreen(false); }

  /// <summary>把"收起来留着的"开始界面重新摆成完整站着的样子（屏保那条路会用：
  /// 个人页还开着、30 秒到点，屏保要重新盖上来），摆完照旧整层渐入（复用 SaverFadeInSeconds）</summary>
    private static void UnparkStartScreen(bool fromLib) {
        _ssParked = false;
        // 有视频背景或静态背景图时神经图都不参与：视频层刚回来还没就绪的那几帧它亮着，就是"闪神经图"。
        // 这时封面由 静态图 / 纯色底 + 遮罩 + 视频层兜底（视频没就绪时静态图顶着，不空屏）。
        if (!VideoBgUsable() && !StaticBgAvailable()) NetBgSetActive(_ssCoverNet, true);
        if (_ssCoverStatic != null) _ssCoverStatic.gameObject.SetActive(true);
        if (_ssCoverVideo != null) _ssCoverVideo.gameObject.SetActive(true);   // 视频层同理（从暂停里回来会自动整台重来）
        _ssFromLib = fromLib;
        _ssPhase = 0; _ssT = 0f; _ssOffset = 0f; _ssDragging = false;
        _ssBreathT = 0f;
        _ssScrub = false; _ssBehind = false; _ssPrepared = false;
        _ssZoomRestore = false; SsApplyCoverZoom(1f);   // 回缩没走完就被摆回来时直接归位（封面还没亮，看不到跳）
        if (_ssRootRt != null) _ssRootRt.anchoredPosition = Vector2.zero;
        if (_ssBgImg != null) _ssBgImg.raycastTarget = true;      // 重新接手势
        if (_ssHint != null) _ssHint.text = fromLib ? "上滑进入" : "上滑关闭";
    // "整层还没铺满"这件事必须先摆好，再画封面：SsPaintCover 里有"整层还在渐入，背景模糊先挂着别落"
    // 那条判据，排反了的话封面一建出来就把模糊归零，底下那页会当着玩家的面一帧变清晰
        if (_ssGroup != null) { _ssGroup.alpha = 0f; _ssGroup.blocksRaycasts = true; }
        _ssFadeIn = true; _ssFadeInT = 0f;
        SsPaintCover(1f);
        _ssEnterK = 0f;   // 分拍入场从这里开始涨
        SsPaintTexts(1f);
        if (fromLib) { try { HideLibraryForSplash(); } catch { } }
        StartPanelProfiling();
        Lg.Info("开始界面：从「收起来」重新摆出来（不重建 整层渐入 "
            + SaverFadeInSeconds.ToString("F2") + "s）");
    }

    private static void ShowStartScreen(bool fromLib) {
        try {
      // 已经弹着就别叠第二层（"一层关不掉的界面"那个老坑）。
      // "收起来留着的"那一种要复用：屏保可能正好在"个人页还开着"的时候到点，
      // 把它重新摆回"完整站着"再渐入（不重建 = 省掉那次全场扫描，见 ParkStartScreenLayer）
            if (_ssGo != null) {
                if (_ssParked) UnparkStartScreen(fromLib);
                return;
            }
      // 排序号必须高于个人页（理由见 SsSortOrder）。首屏那条路不受影响：
      // 滑出时先建个人页（alpha=0）再销毁封面，中间没有可见重叠。
            var cv = MakeCanvas("LevelLib_StartScreen", SsSortOrder);
            _ssGo = cv.gameObject;
      // "别人家的全屏层"在这一刻扫一次就够（原来它挂在"每次按下鼠标"那条路上，3 万对象的场景里一下 250ms）
            SsPrimeForeignOverlay();
            _ssFromLib = fromLib;
            _ssGroup = _ssGo.AddComponent<CanvasGroup>();
      // 建出来先全透明，之后在常驻段 Tick 里 0 → 1 涨起来（首屏与屏保都要，见 ShowStartScreen）
            _ssGroup.alpha = 0f;
            _ssFadeIn = true;
            _ssFadeInT = 0f;
            _ssGroup.blocksRaycasts = true;

      // "封面"单独装一个容器，拖动时整块让位（见 SsApplyScrub）。
      // 不能只淡"背景图 + 遮罩"两张：开着视频背景时背景图会被 enabled=false 关掉、改由视频层接管，
      // 那时真正挡着登录界面的是视频层。Bg / BgVideo / Veil 三样都放进容器，淡容器 = 三样一起让位。
            var cover = new GameObject("Cover", typeof(RectTransform));
            cover.transform.SetParent(cv.transform, false);
            Stretch(cover.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            _ssCoverGroup = cover.AddComponent<CanvasGroup>();
      // 这里也走 SsPaintCover：_ssCoverGroup.alpha 全项目只有它一处写，直接赋值会绕过
      // "背景模糊跟着封面走"那条不变式。a = 1（封面完整站着）时模糊度恒为 0，与 _ssBehind 此刻是什么无关
            SsPaintCover(1f);

      // 跟面板/个人页同一套视觉：纯色底 + 代码背景 + 半透明黑遮罩，标题与提示走主题色（不再用白底）
            var bg = MakeImage(cover.transform, "Bg", BgColor);
            Stretch(bg.rectTransform, Vector2.zero, Vector2.one);
            _ssBgRt = bg.rectTransform;   // 探针拿它算"整屏背景在屏幕坐标里占哪儿"
            _ssBgImg = bg;
      // 静态背景图：垫在纯色底上面、代码背景下面（两层互斥：有图时神经图不显示，见 TickNetBg 那条铁律）
            _ssCoverStatic = AttachStaticBg(cover.transform, bg.transform);
      // 开始界面（屏保）也挂视频层（原设计是这里只有代码背景）：内置默认视频上线后，
      // 没设过视频的玩家默认也看得到动态背景，开始界面是第一眼，跟着一起上
      //（就绪后代码背景淡出，没就绪照旧看代码背景）。
      // 挂层顺序有讲究：先建代码背景、再建糊图层、最后才挂视频层 —— AttachVideoBg 插在
      // "代码背景 +1"，所以视频会落在代码背景之上、糊图层之下（糊图得压在视频上面，
      // 糊的时候才是整张背景一起糊）；糊图链的源本来就会优先取视频纹理（见 BgBlur），无需额外接线。
            var net = BuildNetBg(cover.transform);
            _ssCoverNet = net;
      // 背景渐进模糊的糊图层：压在整个代码背景之上、遮罩之前
      //（顺序 纯色底 → 代码背景 → 视频层 → 糊图 0~N → Veil；糊图压在 Veil 上面的话会比别处亮一截）
            StartBgBlurLayers(net != null ? net.transform : bg.transform);
            _ssCoverVideo = AttachVideoBg(net);   // 视频不可用时返回 null（这一屏照旧只有代码背景）
            var ssVeil = MakeImage(cover.transform, "Veil", new Color(0f, 0f, 0f, 0.45f));
            Stretch(ssVeil.rectTransform, Vector2.zero, Vector2.one);
            ssVeil.raycastTarget = false;
            _ssVeilImg = ssVeil;                        // 封面里的遮罩层（SsPaintCoverVeil 反补偿要写它）
      // 两张图的基准色在这里存一份（拖动时只改 alpha，RGB 一个字都不会被改花）
            _ssVeilBase = ssVeil.color;
            _ssScrub = false; _ssBehind = false;        // 每个新的开始界面都从"不让位"开始
            _ssPrepared = false;                        // 空闲准备也重来一次
            bg.raycastTarget = true;                   // 整屏接手势（下面是面板也无所谓 —— 它被这层盖住了）
            var trig = bg.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trig, EventTriggerType.PointerDown, e => SsDown(e));
            AddTrigger(trig, EventTriggerType.Drag, e => SsDrag(e));
            AddTrigger(trig, EventTriggerType.PointerUp, e => SsUp(e));

            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(cv.transform, false);
            _ssRootRt = root.GetComponent<RectTransform>();
            _ssRootRt.anchorMin = _ssRootRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ssRootRt.pivot = new Vector2(0.5f, 0.5f);
      // 内容块高度 = 2×SsRootTop，它的顶边正好落在"屏幕中心上方 SsRootTop"
      //（标题/提示都用 SetRectPx 从块顶往下数，所以块必须够高、顶边位置必须明确）
            _ssRootRt.sizeDelta = new Vector2(1800f, SsRootTop * 2f);
            _ssRootRt.anchoredPosition = Vector2.zero;

            var ssTheme = new Color(AccentBase.r, AccentBase.g, AccentBase.b, 1f);   // 主题色
            _ssTitle = MakeText(root.transform, "Title", SsTitleFont, ssTheme, TextAnchor.MiddleCenter);
            SetRectPx(_ssTitle.rectTransform, 0f, SsTitleTop, 1800f, SsTitleFont + 40f);
            _ssTitle.text = "LevelLibrary";
            _ssTitle.fontStyle = FontStyle.Bold;
      // ①楷体：拿系统已装的楷体（KaitiFont() 内部先跟已装字体名对一遍），拿不到就保持游戏字体
      // （宁可不像楷体，也不能没字）
            var ssKaiti = KaitiFont();
      // 顺手登记"这里故意用楷体"：字体自检靠这张表放过这一处，别当毛病修掉（见 RegisterKaitiIntended）
            if (ssKaiti != null) { _ssTitle.font = ssKaiti; RegisterKaitiIntended(_ssTitle); }
      // ②加粗：楷体没有粗体面，fontStyle = Bold 在动态字体上基本不会变粗，
      // 所以再叠一层同色描边把笔画加厚一档（视觉上就是加粗了，颜色不变）
            var ssBold = _ssTitle.gameObject.AddComponent<Outline>();
            ssBold.effectColor = new Color(ssTheme.r, ssTheme.g, ssTheme.b, 0.9f);
            ssBold.effectDistance = new Vector2(3f, -3f);
            _ssTitle.raycastTarget = false;
      // ③投影：标题那层同色描边会把 uGUI 的 Shadow 组件也描一遍（投影就被描没了），
      // 所以投影另铺一层同字号、深色的文字，排在标题前一位压在它底下；
      // 透明度由 SsPaintTexts 跟标题同一个系数喂（拖动时两处一起淡，不会脱节）
            _ssTitleShadow = MakeText(root.transform, "TitleShadow", SsTitleFont,
                                      new Color(0f, 0f, 0f, TitleShadowAlpha), TextAnchor.MiddleCenter);
            float tsd = TitleShadowPx(SsTitleFont);   // 错位量跟组件那条路同一个算法
            SetRectPx(_ssTitleShadow.rectTransform, tsd, SsTitleTop + tsd, 1800f, SsTitleFont + 40f);
            _ssTitleShadow.text = "LevelLibrary";
            _ssTitleShadow.fontStyle = FontStyle.Bold;
            if (ssKaiti != null) { _ssTitleShadow.font = ssKaiti; RegisterKaitiIntended(_ssTitleShadow); }
            _ssTitleShadow.rectTransform.SetSiblingIndex(_ssTitle.rectTransform.GetSiblingIndex());

      // 标题下那两行小字：同一套主题色、透明度略低一档（"保持和谐"）
            var ssSub1 = MakeText(root.transform, "Sub1", SsSub1Font,
                                  new Color(ssTheme.r, ssTheme.g, ssTheme.b, 0.85f), TextAnchor.MiddleCenter);
            SetRectPx(ssSub1.rectTransform, 0f, SsSub1Top, 1800f, SsSub1Font + 16f);
            ssSub1.text = "All in ONE";
            ssSub1.raycastTarget = false;
            _ssSub1 = ssSub1;
            var ssSub2 = MakeText(root.transform, "Sub2", SsSub2Font,
                                  new Color(ssTheme.r, ssTheme.g, ssTheme.b, 0.70f), TextAnchor.MiddleCenter);
            SetRectPx(ssSub2.rectTransform, 0f, SsSub2Top, 1800f, SsSub2Font + 16f);
            ssSub2.text = "Coding by FAC";
            ssSub2.raycastTarget = false;
            _ssSub2 = ssSub2;

            _ssHint = MakeText(root.transform, "Hint", SsHintFont, ssTheme, TextAnchor.MiddleCenter);
            SetRectPx(_ssHint.rectTransform, 0f, SsHintTop, 1800f, SsHintFont + 20f);
            _ssHint.text = fromLib ? "上滑进入" : "上滑关闭";
            _ssHint.raycastTarget = false;

      // 更新提示：一行字 + 一条进度条，装在同一个 CanvasGroup 里（整块一起显隐）。
      // 那行字与主题色同源、透明度略低一档；进度条底槽是白 14% —— 与「上滑进入」一样都吃 SsPaintTexts 的淡出
            var upd = new GameObject("Update", typeof(RectTransform));
            upd.transform.SetParent(root.transform, false);
            SetRectPx(upd.GetComponent<RectTransform>(), 0f, SsUpdTop, 1800f, SsUpdBh);
            _ssUpdGroup = upd.AddComponent<CanvasGroup>();
            _ssUpdGroup.alpha = 0f;
            _ssUpdText = MakeText(upd.transform, "UpdText", SsUpdFont,
                                  new Color(ssTheme.r, ssTheme.g, ssTheme.b, 0.90f), TextAnchor.MiddleCenter);
            SetRectPx(_ssUpdText.rectTransform, 0f, 0f, 1800f, SsUpdFont + 16f);
            _ssUpdText.text = "";
            _ssUpdText.raycastTarget = false;
            var stepTheme = new Color(AccentBase.r, AccentBase.g, AccentBase.b, 0.95f);
            const float barLeft = (1800f - SsStepTrackW) * 0.5f;
            _ssUpdBar = new GameObject("UpdSteps", typeof(RectTransform));
            _ssUpdBar.transform.SetParent(upd.transform, false);
            SetRectPx(_ssUpdBar.GetComponent<RectTransform>(), barLeft, SsUpdBarTop, SsStepTrackW, SsStepTrackH);
            var track = MakeImage(_ssUpdBar.transform, "Track", new Color(1f, 1f, 1f, 0.10f));
            track.sprite = RoundedSprite;
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;
            Stretch(track.rectTransform, Vector2.zero, Vector2.one);
            for (int i = 0; i < Updater.GateStepCount; i++) {
                var step = MakeImage(_ssUpdBar.transform, "Step" + i, new Color(1f, 1f, 1f, 0.22f));
                step.sprite = StepPill != null ? StepPill : SharpSprite;   // 8 像素高、圆角 4 = 整颗圆头
                step.type = Image.Type.Sliced;
                step.raycastTarget = false;
                SetRectPx(step.rectTransform, 0f, (SsStepTrackH - SsStepDot) * 0.5f, SsStepDot, SsStepDot);
        // 充能条：只有"当前那一格"才打开（走过的是整颗实心亮，没到的只是灰）
                var fill = MakeImage(step.transform, "Fill", stepTheme);
                fill.sprite = StepPill != null ? StepPill : SharpSprite;
                fill.type = Image.Type.Sliced;
                fill.raycastTarget = false;
                var frt = fill.rectTransform;
                frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
                frt.pivot = new Vector2(0f, 0.5f);
                frt.anchoredPosition = Vector2.zero;
                frt.sizeDelta = new Vector2(0f, SsStepDot);
                fill.enabled = false;
                _ssSteps[i] = step;
                _ssStepFills[i] = fill;
            }
            _ssUpdBarOn = false;
            _ssStepDone = -1;
            _ssStepFillPx = -1;
            _ssUpdBar.SetActive(false);
            SsTickUpdateNotice();   // 建完当场填一次：不然第一帧那行字还是空的

            _ssPhase = 0; _ssOffset = 0f; _ssDragging = false; _ssT = 0f;
            _ssBreathT = 0f; _ssFadeK = 1f;      // 呼吸从最亮开始，拖动淡出系数复位
      // 库内首屏：列表/主页/顶部 tab 先透明，进入后一起淡入（沿用旧启动页那套）
            if (fromLib) { try { HideLibraryForSplash(); } catch { } }
      // 从这里重新武装一次采样窗口：默认窗口是"开面板之后的 120 帧"，而屏保那条路面板本来就是关的，
      // 在这里起算正好覆盖"在开始界面拖几下"那几秒（两种入口都覆盖得到）
            StartPanelProfiling();
            Lg.Info("开始界面（新）：背景图+遮罩 · 标题楷体/加大/" + (ssKaiti != null ? "已用系统楷体" : "系统无楷体→游戏字体")
                + " · 「" + (fromLib ? "上滑进入" : "上滑关闭") + "」带呼吸（" + (fromLib ? "库内首屏" : "屏保")
                + " · 整层渐入 " + SaverFadeInSeconds.ToString("F2") + "s）");
        } catch (System.Exception e) {
            Lg.Ex("开始界面构建失败", e);
            _ssGo = null;
        }
    }

  // ---------- 手势 ----------

  // EventTrigger 回调的参数类型是 BaseEventData（写 PointerEventData 编译不过），进来先自己转一次
    private static void SsDown(BaseEventData d) {
        var e = d as PointerEventData;
        if (e == null) return;
        _ssEvtSawAny = true;                                     // EventSystem 这条路是通的，兜底不插手
        SsProbeLog("收到 PointerDown y=" + e.position.y);      // 滑动探针（最多几条）
        SsDownAt(e.position.y);
    }

  /// <summary>拖动起手，唯一一处：两条输入路径（EventTrigger 那条 + 兜底那条）都必须走它，
  /// 同一个状态写两份迟早会漂。顺带把个人页准备好 —— 放在"按下"而不是"第一次拖动"：
  /// 按下去那一刻做（激活整页 + 摆名字/头像）只换来一次几毫秒停顿，而拖动那条路上必须是干净的帧。</summary>
    private static void SsDownAt(float y) {
    // 更新还没跑完就先别起手。故意挡在"按下"这一下而不是松手那一下：跟手滑到一半再被弹回去，
    // 就是玩家最反感的"亮一下又闪回去"。屏保那一路（_ssFromLib 为假）不挡 —— 那一下是关屏保，不是进界面
        if (_ssFromLib && Updater.GateBlocked) {
            if (_ssGateLogs < SsGateLogMax) {
                _ssGateLogs++;
                Lg.Info("开始界面：更新还没跑完，这一下不接（" + Updater.GateText
                    + "）｜第 " + _ssGateLogs + " 条");
            }
            return;
        }
        _ssDragging = true; _ssPhase = 1;
        _ssDownY = y;
        _ssOffset = 0f;
        _ssVel = 0f; _ssVelUp = 0f; _ssVelT = Time.unscaledTime;   // 甩动判定的起点
        SsScrubBegin();
    }

  /// <summary>准备好"上滑时渐显出来的那一页"：个人页还没在屏幕上，就现在把它激活、并由拖动驱动它的透明度（_ssScrub）；
  /// 已经在屏幕上了（屏保正好盖在一个显示着的个人页上），就一个字都不许碰它，只让封面退开、让它自己露出来。
  /// 建不出来或拿不准时 _ssBehind 保持 false = 封面不让位（宁可没有新效果，也不能把后面的库界面露出来）。</summary>
    private static void SsScrubBegin() {
        if (_ssBehind) return;                       // 这一趟已经定过了
        if (_pfGo != null) { _ssBehind = true; return; }   // 个人页已经在显示，只让封面、不接管
        if (ProfileScrubBegin()) { _ssBehind = true; _ssScrub = true; }
    }

    private static void SsDrag(BaseEventData d) {
        var e = d as PointerEventData;
        if (e == null || !_ssDragging || _ssGo == null) return;
        float up = Mathf.Max(0f, e.position.y - _ssDownY);     // 只认"往上"：往下拖没有效果
        SsDragTo(up);
        if (_ssProbeDrag < 3) { _ssProbeDrag++; SsProbeLog($"收到 Drag 上移 {up:F0}px"); }
    }

  /// <summary>拖动中唯一的出口：把"手指相对按下点上移了多少像素"变成内容位移。
  /// 两条输入路径都必须走这里（同一个几何写两份迟早会漂），钳位也只在这里做一次。
  /// 位置与"到没到阈值"两个口径都按手指的真实距离算（跟手不能有比例），
  /// 只有画出来这一步过速度帽：甩得再快画面每帧也只走 SsSlideMaxPxPerSec × dt，
  /// 一帧冲到全透明那种"飞出去"就是这么来的。慢了随手就追平（want 是绝对位置，不是增量）。</summary>
    private static void SsDragTo(float up) {
        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;
        float dtt = now - _ssVelT;
        if (dtt > 0.0001f) {
            float v = Mathf.Max(0f, (up - _ssVelUp) / dtt);
            _ssVel = Mathf.Lerp(_ssVel, v, 0.45f);   // 平滑：单帧抖动不该把它带跑
            _ssVelT = now;
            _ssVelUp = up;
        }
        float want = Mathf.Clamp(up, 0f, Screen.height * SsDragMaxK);
        _ssOffset = dt > 0f ? SsClampSlideStep(_ssOffset, want, 1f, dt) : want;
        SsApply();
    }

    private static void SsUp(BaseEventData d) {
        var e = d as PointerEventData;
        if (e == null || !_ssDragging) return;
        _ssDragging = false;
        float up = Mathf.Max(0f, e.position.y - _ssDownY);
        SsProbeLog($"收到 PointerUp 上移 {up:F0}px（阈值 {Screen.height * 0.25f:F0}）");
        SsReleaseAt(up);
    }

  /// <summary>松手（两条输入路径共用）：按屏高 1/4 分流 —— 超过则滑出、否则滑回；
  /// 甩得够快（SsFlingPxPerSec）也算"够远"，两者任一成立就滑出。
  /// 进滑出之前把封面/登录界面重置成"完全没揭幕"那一档：拖动全程都不碰这两样
  ///（内容只滑不揭），这里落一遍是保险 —— 万一上一趟留了半截状态，揭幕段会从干净值起步。</summary>
    private static void SsReleaseAt(float up) {
        SsApplyScrub(0f, 1f);
        bool far = up > Screen.height * 0.25f;
        bool fling = _ssVel >= SsFlingPxPerSec;
        SsProbeLog($"松手判定：上移 {up:F0}px（阈值 {Screen.height * 0.25f:F0}）速度 {_ssVel:F0}px/s"
            + " ->  " + ((far || fling) ? "滑出" : "滑回") + $"（距离够={far} 甩动={fling}）");
        if (far || fling) { _ssPhase = 2; _ssT = 0f; }
        else { _ssPhase = 3; _ssT = 0f; }
    }

  /// <summary>空格 / 回车 = 直接滑入登录界面（跟"拖到阈值再松手"走同一条：相位 2 会把剩下的一段自己补完）。
  /// 这里只把两个"从哪接着走"的值摆成"还没拖动"那一档，其余状态一个字都不碰。</summary>
    private static void SsEnterByKey() {
    // 与上滑同一道闸：更新没跑完时键盘也别放人（两条入口必须一个待遇）
        if (_ssFromLib && Updater.GateBlocked) {
            if (_ssGateLogs < SsGateLogMax) {
                _ssGateLogs++;
                Lg.Info("开始界面：更新还没跑完，空格/回车同样不接（" + Updater.GateText
                    + "）｜第 " + _ssGateLogs + " 条");
            }
            return;
        }
        SsScrubBegin();
        SsApplyScrub(0f, 1f);   // 键盘那条路同样从"完全没揭幕"起步
        _ssPhase = 2; _ssT = 0f;
        Lg.Info("开始界面：空格/回车 ->  直接滑入登录界面");
    }

  /// <summary>Esc：把开始界面整层收掉（相位 5 淡出，不交接个人页 —— 那条路会把人带去登录界面）。
  /// 面板还开着的话由调用方接着走面板退出，于是一次 Esc 把两层一起收干净。
  /// 只在"停着 + 没在渐入"时接：拖动中/滑出滑回中按 Esc 会让两套动画打架。</summary>
    private static bool DismissStartScreenByEsc() {
        try {
            if (_ssGo == null || _ssParked || _ssFadeIn || _ssPhase != 0) return false;
      // 空闲时准备好的个人页要收回预热箱，否则它留着"活动引用"会被别处当成"个人页开着"
            if (_ssScrub) { ProfileScrubAbort("Esc 收起开始界面"); _ssScrub = false; }
            _ssBehind = false;
            _ssPhase = 5; _ssT = 0f;
      // 收掉之后再把面板打开还要弹它：首屏那个"本局只弹一次"的闩在这里松开
      //（不放的话 Esc 之后重新开面板就直接落到主界面，玩家会觉得那一层再也出不来了）
            _ssShown = false;
            try { _saverIdleT = 0f; _saverActive = false; } catch { }   // 屏保那条路：别刚收掉就又弹出来
            Lg.Info("Esc：收起开始界面（整层淡出，不交接个人页）");
            return true;
        } catch { return false; }
    }

  // ---------- 滑动探针："什么东西挡住了我的输入" ----------
  // 开始界面开着时，左键一按就 EventSystem.RaycastAll 打前 4 个命中项 + 深度
  //（命中列表为空 = 没有任何目标接得住，手势永远不会触发），三个回调各自也打一行。
  // 只在开始界面开着时打、最多 16 条（平时完全零开销，也不刷屏）。
    private static int _ssProbeCount, _ssProbeDrag;
    private static bool _ssEvtSawAny;          // EventSystem 那条路到底通不通（SsDown 收到过就置真）
    private static bool _ssRaycasterLogged;    // 射线器清单只列一次
    private static RectTransform _ssBgRt;      // 整屏背景图（探针用）
    private static Image _ssBgImg;
    private static void SsProbeLog(string s) {
        try {
            if (_ssProbeCount >= 16) return;
            _ssProbeCount++;
            Lg.Info("【滑动探针】" + s + $"（第 {_ssProbeCount} 条）");
        } catch { }
    }

  /// <summary>常驻段每帧调（开始界面开着才动，很便宜）</summary>
    private static void TickStartScreenProbe() {
        try {
            if (_ssGo == null || _ssProbeCount >= 16) return;
            if (_ssParked) return;      // 收起来的那层不接输入，探针也别在这儿刷日志
            if (!UiInput.MouseButtonDown(0)) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { SsProbeLog("左键按下，但场景里没有 EventSystem（谁都不收输入）"); return; }
      // 第一次点击时把场上所有 GraphicRaycaster 列一遍（命中 0 个时要能看出是哪一环断的：
      // 没有射线器？被禁用了？画布没开？屏幕尺寸对不上？）
            if (!_ssRaycasterLogged) {
                _ssRaycasterLogged = true;
        // 用 FindObjectsByType：FindObjectsOfType 在 Unity 6000 里已过时（CS0618），项目要求 0 警告
                var rcs = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.GraphicRaycaster>(
                    UnityEngine.FindObjectsSortMode.None);
                var sb = new System.Text.StringBuilder();
                sb.Append("GraphicRaycaster 共 ").Append(rcs.Length).Append(" 个：");
                for (int i = 0; i < rcs.Length && i < 12; i++) {
                    var rc = rcs[i];
                    var cvv = rc.GetComponent<Canvas>();
                    sb.Append("\n   · ").Append(cvv != null ? cvv.name : rc.gameObject.name)
                      .Append(" 排序=").Append(cvv != null ? cvv.sortingOrder : -1)
                      .Append(" 射线器enabled=").Append(rc.enabled)
                      .Append(" 画布enabled=").Append(cvv != null && cvv.enabled)
                      .Append(" 物体active=").Append(rc.gameObject.activeInHierarchy);
                }
                sb.Append("\n   屏幕=").Append(Screen.width).Append("×").Append(Screen.height)
                  .Append(" EventSystem=").Append(es != null ? es.gameObject.name : "(无)");
        // 再补一条：整屏背景图在屏幕坐标里到底占哪儿 —— 命中 0 个只有两种可能：
        // ① 它压根没铺满屏（布局坏）② 铺满了但射线器不认（uGUI 坏），把四个角打出来就能分开
                try {
                    if (_ssBgRt != null) {
                        var c4 = new Vector3[4];
                        _ssBgRt.GetWorldCorners(c4);
                        var cvv = _ssBgRt.GetComponentInParent<Canvas>();
                        var cam = cvv != null ? cvv.worldCamera : null;
                        var p0 = RectTransformUtility.WorldToScreenPoint(cam, c4[0]);
                        var p2 = RectTransformUtility.WorldToScreenPoint(cam, c4[2]);
                        sb.Append("\n   开始界面背景图屏幕矩形=(").Append(p0.x.ToString("F0")).Append(",").Append(p0.y.ToString("F0"))
                          .Append(")-(").Append(p2.x.ToString("F0")).Append(",").Append(p2.y.ToString("F0")).Append(")")
                          .Append(" 尺寸=").Append(_ssBgRt.rect.width.ToString("F0")).Append("×").Append(_ssBgRt.rect.height.ToString("F0"))
                          .Append(" 画布scale=").Append(cvv != null ? cvv.scaleFactor.ToString("F2") : "?")
                          .Append(" raycastTarget=").Append(_ssBgImg != null && _ssBgImg.raycastTarget);
                    } else sb.Append("\n   （拿不到背景图引用）");
                } catch (System.Exception e2) { sb.Append("\n   (背景矩形取失败 ").Append(e2.GetType().Name).Append(")"); }
                Lg.Info("【滑动探针】" + sb.ToString());
            }
            var ped = new PointerEventData(es) { position = UiInput.MousePosition };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(ped, hits);
            string names = "";
            for (int i = 0; i < hits.Count && i < 4; i++) {
                var tr = hits[i].gameObject != null ? hits[i].gameObject.transform : null;
                string path = tr != null ? tr.name : "?";
                for (var p = tr != null ? tr.parent : null; p != null && path.Length < 60; p = p.parent) path = p.name + "/" + path;
                names += $" ← {path}(深度={hits[i].depth})";
            }
            SsProbeLog($"左键按下 at {ped.position}，射线命中 {hits.Count} 个{names}");
        } catch (System.Exception e) { Lg.Ex("滑动探针异常", e); }
    }

  // ---------- EventSystem 那条路断了时的兜底输入 ----------
  // 直接读鼠标（走 UiInput，不受 UI 地图闸门影响）。只在 EventTrigger 一次都没响过时才启用 ——
  // 它要是通的就别抢，免得两套同时跑。也要问"上面压着更高的层没有"，
  // 但不能用 PanelInputBlocked()：它把"开始界面自己在"也算成被挡着（见 SsInputYields）。
    private static bool _ssFallbackDrag;

  /// <summary>兜底输入是否让开：真的是按层号比 —— 只有"比我高"的层在时才让开
  ///（个人页 93000 / 过渡层 82000 / 旧启动页 93000 / 面板 80000 都在我下面，不算）。
  /// 不能拿"个人页在不在"当"我是不是被压着"：开始界面 93100 就在最上面，那样写正好把最上面这层的输入关掉。
  /// 只在按下那一下问，已经拖起来的手势要能走完（半路冒出新层也别把拖动掐断）。</summary>
    private static float _ssYieldCacheT = -1f;      // 上次算"上面有没有更高的层"的时刻（unscaled）
    private static bool _ssYieldCacheVal;
    private static int _ssYieldWhyLogs;             // 让开原因最多记几条（排查"滑不动"用）
    private static bool SsInputYields() {
        try {
            if (_closing) {
        // _closing 是给"面板那边的东西"用的闸门，一旦它卡在 true（例如画布被场景加载销毁、
        // 走不到 ClosePanel）开始界面就会永远让开，所以这里必须记一条
                if (_ssYieldWhyLogs < 8) {
                    _ssYieldWhyLogs++;
                    Lg.Info("【滑动探针】兜底让开 原因=`_closing`（面板正在关闭）"
                        + " 面板开=" + _panelOpen + " 画布=" + (_panelCanvas != null));
                }
                return true;
            }
      // FindObjectsByType<Canvas> 是全场景搜索、每次都分配一个数组（日志里那句"开始界面输入=35.4ms"就是它），
      // 而"上面有没有比我更高的层"在半秒之内不会变，所以结果缓存 0.5 秒
            float now = Time.unscaledTime;
            if (_ssYieldCacheT >= 0f && now - _ssYieldCacheT < 0.5f) return _ssYieldCacheVal;
            _ssYieldCacheT = now;
            string why;
            _ssYieldCacheVal = SsScanHigherLayer(out why);
      // 让开的时候把原因记一条（最多 8 条）：下次一看日志就知道该修哪儿，不用猜
            if (_ssYieldCacheVal && _ssYieldWhyLogs < 8) {
                _ssYieldWhyLogs++;
                Lg.Info("【滑动探针】兜底让开 原因=" + why
                    + "（上面有更高的层 / 面板正在关闭 这一下不会被接下）");
            }
            return _ssYieldCacheVal;
        } catch { }
        return false;
    }

  /// <summary>问"有没有比我高、且够大的 overlay 画布"：只查我们自己的画布登记表 _ourCanvases，
  /// 别人家的全屏层用"开始界面刚建好那一刻"扫一次的结果（见 SsPrimeForeignOverlay）。
  /// 全场景搜索不能每次点击都做：3 万对象的场景里一次就是 250ms。</summary>
    private static bool SsScanHigherLayer(out string why) {
        why = "";
        try {
      // ① 我们自己的层（便宜，覆盖面板 / 个人页 / 过渡层 / 生日 / 周报 / 报错弹窗…全部）
            for (int i = _ourCanvases.Count - 1; i >= 0; i--) {
                var c = _ourCanvases[i];
                if (c == null) { _ourCanvases.RemoveAt(i); continue; }   // 画布没了就顺手清
                if (HigherThanMe(c)) { why = "我们自己的画布 " + c.name + "（排序=" + c.sortingOrder + "）比我高"; return true; }
            }
      // ② 别人家的全屏层：开始界面建好那一刻扫过一次的结果（见 SsPrimeForeignOverlay）
            if (_ssForeignOverlay) why = "别人家的全屏层（`SsPrimeForeignOverlay` 那一刻扫到的）";
            return _ssForeignOverlay;
        } catch { }
        return false;
    }

  /// <summary>这块画布是不是"压在开始界面上面、而且够大"</summary>
    private static bool HigherThanMe(Canvas c) {
        try {
            if (c == null || !c.isActiveAndEnabled) return false;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay) return false;
   // Canvas.sortingOrder 的实际值域是 int16：设计值 >= 32768 运行时会折回（本项目面板 80000 实际存成 14464、
   // 开始界面 93100 存成 27564，实机日志的"画布点名"可以直接看到）。比较必须在同一个值域里做，
   // 否则外面每一块画布都会被误判成"在我下面"。
            int selfOrder = SsSortOrder > 32767 ? SsSortOrder - 65536 : SsSortOrder;
            if (c.sortingOrder <= selfOrder) return false;            // 在我下面 / 就是我，不挡
      // 够大才算"盖在屏幕上"（角上的小窗不该把滑动吃掉）；画布单位是 RefW，不是像素比
            var rt = c.GetComponent<RectTransform>();
            if (rt == null || Mathf.Max(rt.rect.width, rt.rect.height) < RefW * 0.4f) return false;
            return true;
        } catch { return false; }
    }

  /// <summary>别人家的全屏层（名字不带 LevelLib_ 前缀的）：只在开始界面刚建好那一刻扫一次。
  /// 挂在"每次按下鼠标"那条路上时，3 万对象的场景里一次就是 250ms。</summary>
    private static bool _ssForeignOverlay;
    private static void SsPrimeForeignOverlay() {
        try {
            _ssForeignOverlay = false;
            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) {
                var c = all[i];
                if (c == null || c.name == null) continue;
                if (c.name.StartsWith("LevelLib_", System.StringComparison.Ordinal)) continue;   // 我们自己的走登记表
                if (HigherThanMe(c)) { _ssForeignOverlay = true; return; }
            }
        } catch { }
    }

    private static void TickStartScreenInput() {
        try {
            if (_ssGo == null || _ssEvtSawAny) return;        // EventSystem 那条路是通的，不插手
      // 收起来留着的那层一个字节都不许接输入：它上滑交接之后还活着（就为了"往下滑回来"能淡回来），
      // 而这块兜底直接读鼠标、不受射线影响，不挡住的话在登录界面上按下、开始界面也会同时跟手
            if (_ssParked) return;
      // 不能用 PanelInputBlocked()：它把"开始界面自己在"也算成"被挡着"，那条判据是给面板那边的东西用的
            var mp = UiInput.MousePosition;
            if (UiInput.MouseButtonDown(0)) {
                if (SsInputYields()) return;                  // 只在按下那一下问"上面有没有更高的层"
                _ssFallbackDrag = true;
                SsDownAt(mp.y);                               // 跟 EventTrigger 那条走同一处（含"准备个人页"）
                SsProbeLog("兜底：按下 y=" + mp.y);
            } else if (_ssFallbackDrag && UiInput.MouseButton(0)) {
                SsDragTo(Mathf.Max(0f, mp.y - _ssDownY));
            } else if (_ssFallbackDrag) {
                _ssFallbackDrag = false;
                float up = Mathf.Max(0f, mp.y - _ssDownY);
                SsProbeLog($"兜底：松开 上移 {up:F0}px（阈值 {Screen.height * 0.25f:F0}）");
                SsReleaseAt(up);
            }
        } catch { }
    }

  /// <summary>松手之后自己走的每一段位移都要过这里（全项目唯一一处速度帽）：
  /// cur / want 是同一把尺子上的两个值（量位置都用 px、量进度都用 0~1），span = 这条动画整段跨多少像素
  ///（量位置传 1），所以不管用哪把尺子，速度上限都还是同一个 SsSlideMaxPxPerSec。
  /// dt ≤ 0 = 手指正在拖，原样返回、一个字都不掐；用 MoveTowards 是因为差值 ≤ 上限时它正好落在目标上
  ///（收尾那一下必须真的到位，差一点就会在下一帧被"归零"那句拉过去 = 跳一下）。</summary>
    private static float SsClampSlideStep(float cur, float want, float span, float dt) {
        if (dt <= 0f) return want;
        float maxStep = SsSlideMaxPxPerSec * dt / Mathf.Max(1f, span);
        return Mathf.MoveTowards(cur, want, maxStep);
    }

  /// <summary>把"当前上移量"画出来：标题/提示上移 + 变淡（同一条比例，两者节奏一致）。
  /// 只动开始界面自己的内容 —— 封面与登录界面在这里一个字都不碰（两段式：揭幕是相位 6 的事）。
  /// 两条输入路径 + 滑回动画都走这里。</summary>
    private static void SsApply() {
        if (_ssRootRt == null) return;
        float k = Mathf.Clamp01(_ssOffset / Mathf.Max(1f, Screen.height * 0.25f));   // 0~1 = 到阈值的进度
        _ssRootRt.anchoredPosition = new Vector2(0f, _ssOffset * SsFollowK);
    // 拖到阈值时还剩一半不透明度（原来的 0.85 会"一下就没了"）
        _ssFadeK = 1f - k * 0.5f;
        SsPaintTexts(_ssFadeK);                                                      // 标题/副标题/提示（唯一一处写）
    }

  /// <summary>唯一一处写开始界面那几行字透明度的地方（f = 淡出系数：1 = 完整站着 / 0 = 全透明）：
  /// 四条字各乘自己的档（标题 1 / 副标题 0.85 / 0.70），提示那行再乘呼吸曲线 —— 两个系数每次都从源头重算
  ///（读"上一帧的 alpha"的话，拖动与呼吸会互相覆盖、越乘越小）。上滑（SsApply）/ 滑出（相位 2）/ 从登录界面滑回来（SsPaintReturn）三条路都只调它。</summary>
  /// <summary>分拍入场：相邻两拍之间的进度间隔（四拍共占 3×Gap）</summary>
    private const float SsStageGap = 0.14f;



    private static void SsPaintTexts(float f) {
        _ssFadeK = Mathf.Clamp01(f);
    // 两个维度相乘：拖动淡出（_ssFadeK）× 分拍入场（_ssEnterK 折成四拍）。
    // 只有入场那一秒里四拍是错开的；拖上去的时候 _ssEnterK 已经是 1，四个乘数全为 1，
    // 于是"标题随拖动变淡"那条一个像素都没变。
        float k0 = SsStage(_ssEnterK, 0), k1 = SsStage(_ssEnterK, 1);
        float k2 = SsStage(_ssEnterK, 2), k3 = SsStage(_ssEnterK, 3);
        if (_ssTitle != null) { var c = _ssTitle.color; _ssTitle.color = new Color(c.r, c.g, c.b, _ssFadeK * k0); }
        if (_ssTitleShadow != null) { var c = _ssTitleShadow.color; _ssTitleShadow.color = new Color(c.r, c.g, c.b, _ssFadeK * k0 * TitleShadowAlpha); }
        if (_ssSub1 != null) { var c = _ssSub1.color; _ssSub1.color = new Color(c.r, c.g, c.b, _ssFadeK * k1 * 0.85f); }
        if (_ssSub2 != null) { var c = _ssSub2.color; _ssSub2.color = new Color(c.r, c.g, c.b, _ssFadeK * k2 * 0.70f); }
    // 更新提示整块（那行字 + 进度条）跟整层一起淡；闸门没开着时整块归零，
    // 免得收起/交接之后还留一行"检查更新中"压在别的东西上
    // 门提示这一块与「上滑进入」各自朝目标平滑，不在一帧里跳变。
    // 第一次算的时候直接落到目标：启动页刚摆出来时门可能早就武装好了，
    // 让这两个量从初值慢慢走过去的话，玩家会先看到一行亮着的「上滑进入」再看着它暗下去。
        if (!_ssGateNoticeInit) {
            _ssGateNoticeInit = true;
            _ssUpdK = Updater.GateVisible ? 1f : 0f;
            _ssGateK = Updater.GateBlocked ? 0f : 1f;
        } else {
            float dtN = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float kN = 1f - Mathf.Exp(-SsGateNoticeFollow * dtN);
            _ssUpdK = SsApproach(_ssUpdK, Updater.GateVisible ? 1f : 0f, kN);
            _ssGateK = SsApproach(_ssGateK, Updater.GateBlocked ? 0f : 1f, kN);
        }
    // 更新提示整块（那行字 + 进度条）：跟着整层淡，同时自己再淡一层 ——
    // 门收摊时是渐隐而不是瞬间消失
        if (_ssUpdGroup != null) _ssUpdGroup.alpha = _ssFadeK * _ssUpdK;
        if (_ssHint == null) return;
        float wave = (Mathf.Sin(_ssBreathT * Mathf.PI * 2f / SsBreathPeriod) + 1f) * 0.5f;   // 0~1（呼吸曲线）
        float breath = Mathf.Lerp(SsBreathMin, 1f, wave);
    // 更新没跑完时把「上滑进入」整行收掉：这一下点了不会进去，让它彻底看不见 ——
    // 早先只是压暗到三成，实机反馈是"看得见但按不动"，比不收更让人困惑
        float gateK = _ssGateK;
    // 本局装上了更新（这一屏挂着「请重启游戏」）时整行立刻收掉，不等平滑
        if (_ssFromLib && Updater.GateMustRestart) gateK = 0f;
        var h = _ssHint.color;
        _ssHint.color = new Color(h.r, h.g, h.b, _ssFadeK * k3 * 0.9f * breath * gateK);
    }

  /// <summary>指数跟随一步：cur 朝 want 走 k 的比例，靠得足够近就直接落到位。
  /// 不落位的话它永远差最后一点点，每帧都会去写一次控件（白重绘）。
  /// 名字避开 SsFollowK —— 那个常量是"内容跟手比例"，两件事不是一回事。</summary>
    private static float SsApproach(float cur, float want, float k) {
        float v = cur + (want - cur) * k;
        if (Mathf.Abs(want - v) < 0.002f) v = want;
        return v;
    }

  /// <summary>分拍入场的第 slot 拍：把总进度 0~1 换算成这一拍自己的 0~1。
  /// 间隔与拍数写死在这儿，改节奏只动 SsStageGap 一个数。</summary>
    private static float SsStage(float k, int slot) {
        return Mathf.Clamp01((k - SsStageGap * slot) / (1f - SsStageGap * 3f));
    }

  /// <summary>封面的透明度（唯一一处写：上滑让位 / 滑出补完 / 从登录界面滑回来都走它），
  /// 顺带把背景模糊度也定在这儿 —— 模糊度 = _ssBehind ? 1 − 封面透明度 : 0。
  /// 两件事必须同一个写入点：分开喂的话迟早有一条路只喂了一个（不然会出现"糊到底了、界面才滑一半"）。
  /// 另外顺带补偿封面那层遮罩，理由与公式见 SsPaintCoverVeil。</summary>
    private static void SsPaintCover(float a) {
        float ca = Mathf.Clamp01(a);
        if (_ssCoverGroup != null) _ssCoverGroup.alpha = ca;
        SsPaintCoverVeil(ca);
        SetBgBlur(_ssBehind ? 1f - ca : 0f);
    }

  /// <summary>封面遮罩的反补偿（唯一一处写它的地方）：uGUI 的 CanvasGroup.alpha 是逐个元素乘的，
  /// 封面淡出时那层 0.45 遮罩只按 v0×α 生效，而下面的视频/糊图是按 α 铺到"已经压暗过的个人页底"上的，
  /// 于是遮罩写死 v0 时合成亮度会拱成一条抛物线（两端 0.55V，中间 0.599V，亮 9%）。
  /// 解 (1−v·α)·[v0+(1−v0)·α] = v0 得 v(α) = v0 / ((1−v0) + v0·α)：α=1 仍是 v0（静止时画面不变），α→0 趋近 0.818，永不超过 1。</summary>
    private static void SsPaintCoverVeil(float ca) {
        if (_ssVeilImg == null) return;
        float v0 = _ssVeilBase.a;                       // 建层时存下来的那档（0.45）
        if (v0 <= 0.0001f) return;
        float den = (1f - v0) + v0 * ca;
        float v = (den > 0.0001f) ? Mathf.Clamp01(v0 / den) : v0;
        var c = _ssVeilImg.color;
        if (Mathf.Abs(c.a - v) > 0.0005f)
            _ssVeilImg.color = new Color(_ssVeilBase.r, _ssVeilBase.g, _ssVeilBase.b, v);
    }

  /// <summary>唯一一处写"封面让位 + 登录界面渐显"两个透明度的地方。contentA = 个人页"内容层"的透明度（0~1），
  /// coverA = 封面的（1 = 完全挡着）。不接管个人页时（_ssScrub == false）绝不碰它，只让封面。
  /// 只在揭幕段（相位 6）与各处的"重置成没揭幕"里调：拖动/滑出阶段绝不喂它。
  /// 封面淡的是容器（Bg / BgVideo / Veil 三层一起）：只淡背景图的话，开着视频背景时那一层还挡着，等于什么都没淡。
  /// 底下不是登录界面（_ssBehind == false）就一个字都不让（宁可没新效果，也不能露底）。</summary>
    private static void SsApplyScrub(float contentA, float coverA) {
        if (_ssGo == null) return;
        SsPaintCover(_ssBehind ? coverA : 1f);
        ProfileScrubSet(contentA);
    // 背景放大是两份一起：封面随让位放大、登录界面的背景随渐显放大 —— 两边是同一张底、同一个缩放值
    //（都从 contentA/coverA 这同一条进度推出来），交叉让位时看不出接缝。揭幕走完，封面那份在收起来的层里
    // 隐形缩回原大；登录界面那份"留"在页面上，直到点登录进主界面才由个人页自己缩回（见 PfZoomMax 那段）。
        if (_ssBehind && !_ssZoomRestore)
            SsApplyCoverZoom(1f + (1f - Mathf.Clamp01(coverA)) * (SsZoomMax - 1f));
    // 与封面同一个缩放值（同一条进度推 1→PfZoomMax，交叉让位无缝）。别直接喂 contentA 再 Clamp(下限 1)：
    // contentA 是 0~1，被下限一夹恒等于 1 = 登录页背景永远放大不起来（实机踩过，日志里零条缩放记录）
        SsApplyProfileZoom(1f + Mathf.Clamp01(contentA) * (PfZoomMax - 1f), "揭幕");
    }

  /// <summary>"从登录界面往下滑回开始界面"走完了，两边各归各位：开始界面摆回完整站着（封面 1 / 字 1 / 重新接手势），
  /// 登录界面里我们接管的那一页收起来（藏回预热箱）；屏保那条路本来就显示着的那页不收，只是被封面盖回去了。</summary>
    private static void SsFinishReturnToStart() {
        if (_ssGo == null) return;
        _ssParked = false;
        // 有视频背景或静态背景图时神经图都不参与（同 UnparkStartScreen 的口径）：
        // 重新点亮它的话，视频层重启没就绪的那段时间里神经图看得清清楚楚。
        if (!VideoBgUsable() && !StaticBgAvailable()) NetBgSetActive(_ssCoverNet, true);
        if (_ssCoverStatic != null) _ssCoverStatic.gameObject.SetActive(true);
        if (_ssCoverVideo != null) _ssCoverVideo.gameObject.SetActive(true);   // 视频层同理
        _ssPhase = 0; _ssT = 0f; _ssOffset = 0f; _ssDragging = false; _ssBreathT = 0f;
        if (_ssRootRt != null) _ssRootRt.anchoredPosition = Vector2.zero;
        if (_ssBgImg != null) _ssBgImg.raycastTarget = true;
        SsPaintCover(1f);
        _ssZoomRestore = false; SsApplyCoverZoom(1f);   // 回程封面要全亮：缩放必须归位，别顶着放大值亮回来
        // 回程不做"整层渐入"：上一句把封面定成不透明，这一句又要求整层立刻可见，四拍入场进度没地方可涨
        // —— 直接写 1（四个乘数全为 1，正是 SsPaintTexts 里写死的设计前提"拖上去时 _ssEnterK 已经是 1"）。
        // 这里若留成 0：k0..k3 全是零，那几行字会被每帧重画刷成全透明，
        // 于是从登录界面滑回开始界面后文本全没了（标题、两行副标题、底部提示都不显示）。
        // 别改成在这里补 `_ssFadeIn = true;`：整层刚被定成 1，渐入反而会让整屏先黑一下再亮。
        _ssEnterK = 1f;
        SsPaintTexts(1f);
        bool owned = _ssScrub;
        ProfileBackFinish(owned);
        _ssScrub = false; _ssBehind = false; _ssPrepared = false;
        Lg.Info("开始界面：从登录界面往下滑回来了（封面与那几行字都淡回来了 登录界面"
            + (owned ? "已收起 再上滑还能进" : "保持显示（屏保那条路）") + "）");
    }

  /// <summary>"从上往下滑回开始界面"用的一整套透明度：封面涨回来、开始界面那几行字淡回来、登录界面淡下去，
  /// 三件事都按同一个 k 走（k = 0 还在登录界面 / 1 回到开始界面）。开始界面那层上滑之后没有销毁、留着，
  /// 所以这里有东西可以淡回来，也不用重建（重建要扫全场 250ms）。个人页的内容透明度也交给它。</summary>
  /// <remarks>dt > 0 = "松手之后自己走的那一段"，要过速度帽；dt ≤ 0（默认）= 手指正在拖，1:1 跟手、一个字都不掐。
  /// 掐的是进度而不是"只掐位置"：位置和那三样透明度都从 k 现算，只掐位置就成了"字淡进来了、人还悬在上面"。
  /// 过帽会让实际进度落后于"想"，实际值存在 _ssReturnK 里，收尾判据也读它（见 TickProfileDragBack）。</remarks>
    private static void SsPaintReturn(float k, float dt = 0f) {
        _ssReturnK = SsClampSlideStep(_ssReturnK, Mathf.Clamp01(k), SsMaxRise * 4f, dt);
        k = _ssReturnK;
        SsPaintCover(_ssBehind ? k : 1f);     // 没确认底下是登录界面就不让它退回来（防露底）
        SsPaintTexts(k);
    // 位置也要跟着 k 一起回来：滑出那 0.55 秒把整块内容推到了 SsMaxRise × 4（约 1040px），
    // 只把透明度淡回来的话那些字还在屏幕上方外面（"淡了个寂寞"），一直到最后收尾那句 anchoredPosition = 0
    // 才把它搬回原位 = 瞬间闪出来。k = 0 时它在滑出去留下的那个高度、k = 1 时回到原点，与去程完全对称。
    // 这里的 k 已经过了速度帽（见 _ssReturnK），所以位置与那三样透明度天然同步，不会"字先到、人后到"。
        if (_ssRootRt != null) _ssRootRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(SsMaxRise * 4f, 0f, k));
        ProfileScrubSet(1f - k * PfBackFadeK);
        if (_ssGroup != null) _ssGroup.alpha = 1f;   // 从"收起来"往回走时整层必须是完全不透明的
    }

  /// <summary>步进轨道用的主题色（每帧画"走过的那几格"要用，取的是当前主题色）</summary>
    private static Color StepThemeColor() {
        return new Color(AccentBase.r, AccentBase.g, AccentBase.b, 0.95f);
    }

  /// <summary>更新提示那一行与进度条。每帧都来读，但只有值真的变了才写控件：
  /// 字符串比对自己不分配，宽度差半像素以内也不写 —— 写一次矩形就要重画这一块。</summary>
    private static void SsTickUpdateNotice() {
        if (_ssUpdText == null) return;
        string t = Updater.GateText;
    // 更新那一行还空着的时候，把开机字体自检的结论顶上来显示（有问题才有值，见 Updater.RunStartupFontCheck）。
    // 字体坏掉是"整页楷体 / 一个字都不显示"这种一眼可见的毛病，玩家得当场看见，不能只躺在日志里。
        if (string.IsNullOrEmpty(t)) t = Updater.GateFontNote;
        if (!string.Equals(_ssUpdText.text, t, System.StringComparison.Ordinal)) _ssUpdText.text = t ?? "";
        int done; float fill;
        bool on = Updater.GateStepProgress(out done, out fill);
        if (on != _ssUpdBarOn && _ssUpdBar != null) { _ssUpdBarOn = on; _ssUpdBar.SetActive(on); }
        if (!on || _ssSteps[0] == null) return;
    // 显示格号与显示填充：真实格号走在前头时，先把当前这格补满（平滑，不硬跳），
    // 满了才让位给下一格 —— "进度满了换下一个"，中间不会闪。
        float dtRaw = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float kFill = 1f - Mathf.Exp(-SsStepFillFollow * dtRaw);
        if (_ssStepShow < 0) { _ssStepShow = done; _ssFillShown = fill; _ssStepHoldT = 0f; }
        if (done > _ssStepShow) {
            _ssStepHoldT += dtRaw;
            _ssFillShown += (1f - _ssFillShown) * kFill;
            if (_ssStepHoldT >= SsStepMinDwell && 1f - _ssFillShown < 0.02f) {
                _ssStepShow++;
                _ssStepHoldT = 0f;
        // 让位那一刻：真实进度已经追平就从它的真实值起，否则这一格从头开始
                _ssFillShown = (_ssStepShow >= done) ? fill : 0f;
            }
        } else if (done < _ssStepShow) {
            _ssStepShow = done; _ssFillShown = fill; _ssStepHoldT = 0f;
        } else {
      // 追上了：照真实进度（"这一格跑了多少"本身在涨）
            _ssFillShown = fill;
            _ssStepHoldT = 0f;
        }
        done = _ssStepShow;
        fill = Mathf.Clamp01(_ssFillShown);

    // 宽度与颜色都做过渡：目标 = 当前那格是长条（bar）、其余是点（dot）
        float dt2 = dtRaw;
        float k = 1f - Mathf.Exp(-SsStepFollow * dt2);
        float kc = 1f - Mathf.Exp(-SsStepColorFollow * dt2);
        if (k < 0f) k = 0f;
        if (k > 1f) k = 1f;
        bool moving = false;
        for (int i = 0; i < Updater.GateStepCount; i++) {
            float wantW = (i == done) ? SsStepBarW : SsStepDot;
            if (_ssStepW[i] <= 0f) _ssStepW[i] = wantW;             // 第一次直接就位，别从 0 长出来
            _ssStepW[i] += (wantW - _ssStepW[i]) * k;
            if (Mathf.Abs(wantW - _ssStepW[i]) < 0.15f) _ssStepW[i] = wantW;
            else moving = true;
            float wantK = (i < done) ? 1f : 0f;
            if (_ssStepK[i] != wantK) {
                _ssStepK[i] += (wantK - _ssStepK[i]) * kc;
                if (Mathf.Abs(wantK - _ssStepK[i]) < 0.01f) _ssStepK[i] = wantK;
                else moving = true;
            }
        }
    // 没在动、进度也没变 ， 一个矩形都不写（这函数每帧都会被调到）
        int nowFill = Mathf.RoundToInt(SsStepBarW * Mathf.Clamp01(fill));
        if (!moving && done == _ssStepDone && nowFill == _ssStepFillPx) return;
    // 一行十颗、整行居中：当前那颗一长出来，两边的小点就被推开（宽度在动，位置也跟着动）
        float total = (Updater.GateStepCount - 1) * SsStepGap;
        for (int i = 0; i < Updater.GateStepCount; i++) total += _ssStepW[i];
        float x = (SsStepTrackW - total) * 0.5f;
        for (int i = 0; i < Updater.GateStepCount; i++) {
            var step = _ssSteps[i];
            var fill2 = _ssStepFills[i];
            if (step == null) continue;
            float w = _ssStepW[i];
            SetRectPx(step.rectTransform, x, (SsStepTrackH - SsStepDot) * 0.5f, w, SsStepDot);
            var want = Color.Lerp(new Color(1f, 1f, 1f, 0.22f), StepThemeColor(), _ssStepK[i]);
            if (step.color != want) step.color = want;
            if (fill2 != null) {
        // 充能条 = 这一根当前的宽度 × 这一格的进度：跟着宽度一起长，看不出接缝
                float fillW = (i == done) ? w * Mathf.Clamp01(fill) : 0f;
                bool onFill = fillW > 0.5f;
                if (fill2.enabled != onFill) fill2.enabled = onFill;
                if (onFill) {
                    var frt2 = fill2.rectTransform;
                    if (Mathf.Abs(frt2.sizeDelta.x - fillW) > 0.5f)
                        frt2.sizeDelta = new Vector2(fillW, SsStepDot);
                }
            }
            x += w + SsStepGap;
        }
        _ssStepDone = done;
        _ssStepFillPx = nowFill;
    }

  /// <summary>每帧（挂在常驻段：屏保时面板是关着的）</summary>
    private static void TickStartScreen() {
        if (_ssGo == null) return;
        try {
      // dt 必须钳住单帧步进：首屏是在开面板那一帧建出来的、而那一帧极卡（日志里峰值 787ms），
      // 不钳的话下一帧 dt=0.787s 会让渐入一帧走完 = 还是"闪出来"（见 UiFadeMaxStep）
            float dt = Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);
      // 背景缩放的收尾回缩：过渡收尾（层收起来）之后每帧朝 1 缩，封面全透明所以是隐形的；
      // 收起来的那层还在场上，这条每帧都跑得到，等下次摆回来时缩放已经落回原大
            if (_ssZoomRestore && _ssZoom > 1.0001f) {
                float kz = 1f - Mathf.Exp(-SsZoomFollow * dt);
                SsApplyCoverZoom(SsApproach(_ssZoom, 1f, kz));
            }
      // 渐入：整层 0 → 1，时长复用旧屏保的 SaverFadeInSeconds（首屏 / 屏保都走这里）
            if (_ssFadeIn) {
                _ssFadeInT += dt;
                float fa = Mathf.Clamp01(_ssFadeInT / SaverFadeInSeconds);
                if (_ssGroup != null) _ssGroup.alpha = fa;
        // 分拍入场就用这一条进度：标题 → 第一行 → 第二行 → 上滑提示依次亮起来
                _ssEnterK = fa;
                if (fa >= 1f) {
                    _ssFadeIn = false;
          // 整层铺满了，把渐入期间"挂着"的模糊度落下去
          //（铺满之后封面完全不透明，这一刻归零是隐形的，见 SetBgBlur）
                    FlushBgBlur();
                }
            }
            if (!_ssParked) SsTickUpdateNotice();
      // 「上滑进入」呼吸：停着/跟手时都在呼吸（滑出/滑回/收尾阶段不呼吸）
            if (_ssPhase == 0 || _ssPhase == 1) { _ssBreathT += dt; SsPaintTexts(_ssFadeK); }
      // 空格 / 回车也能进（与"上滑到阈值"同一条路）：只在停着、没在拖、整层已经铺满时接
            if (_ssPhase == 0 && !_ssParked && !_ssDragging && !_ssFadeIn
                && (UiInput.KeyDown(KeyCode.Space) || UiInput.KeyDown(KeyCode.Return) || UiInput.KeyDown(KeyCode.KeypadEnter))) {
                SsEnterByKey();
            }
      // 空闲时先把个人页备好：激活整页 + 摆名字/头像要几毫秒（头像第一次还要读盘解码），
      // 放在"按下"那一刻做会有一下停顿，所以只在游戏不忙的帧做（跟排队预热同一套判据，见 TickPreload）：
      // 淡入已走完 + 这一帧 ≤50ms + 玩家手没按着。失败也不管，真拖的时候 SsScrubBegin 还会再试一遍。
            if (!_ssPrepared && !_ssFadeIn && _ssPhase == 0
                && Time.unscaledDeltaTime <= 0.05f && !UiInput.MouseButton(0)) {
                _ssPrepared = true;
                SsScrubBegin();
            }
            if (_ssPhase == 2) {          // 滑出：只把开始界面内容推走，封面与登录界面都按兵不动（揭幕留给相位 6）
                _ssT += dt;
                float k = Mathf.Clamp01(_ssT / SsOutSeconds);
                float e = k * k;                                   // easeInQuad（越走越快，像"甩出去"）
        // 同一条缓动 + 速度帽（实测峰值 4169 px/s）。这一条只掐位置。
                if (_ssRootRt != null) {
                    float to = SsMaxRise * 4f;
                    float cur = _ssRootRt.anchoredPosition.y;
                    float want = Mathf.Lerp(cur, to, k * 0.5f + e * 0.5f);
                    _ssRootRt.anchoredPosition = new Vector2(0f, SsClampSlideStep(cur, want, 1f, dt));
                }
        // 封面（背景图 + 遮罩）全程保持不透明、登录界面全程不露：屏幕上一直是"封面那层同色背景 +
        // 内容滑走"，不放大、不糊、底下那页一个像素都碰不到
                _ssFadeK = Mathf.Clamp01(1f - k);
                SsPaintTexts(_ssFadeK);
                if (k >= 1f) {
                    _ssT = 0f;
                    _ssPhase = 6;   // 滑完才揭幕：放大 + 模糊 + 登录界面渐显
                }
                return;
            }
            if (_ssPhase == 6) {          // 揭幕：封面让位 + 背景放大 + 渐糊 + 登录界面渐显（四件事同一条进度）
                _ssT += dt;
                float k = Mathf.Clamp01(_ssT / SsRevealSeconds);
                float e = 1f - (1f - k) * (1f - k);                // easeOutQuad（开始快，收尾轻）
        // SsApplyScrub 一口喂齐：封面 1→0（模糊与放大都挂在封面让位上，见那边的注释）、内容层 0→1
                SsApplyScrub(e, 1f - e);
                if (k >= 1f) {
          // 到这里登录界面已经全亮、封面已经完全让开，就地把它交给个人页自己（_pfPhase = 0 = 等点登录），
          // 它那 0.45 秒的渐入动画一次都不会跑，两个动画绝不重叠
                    if (_ssScrub) ProfileScrubCommit();
                    else if (_ssBehind) { if (_ssBgImg != null) _ssBgImg.raycastTarget = false; }
                    _ssPhase = 4;
                }
                return;
            }
            if (_ssPhase == 3) {          // 滑回原位
                _ssT += dt;
                float k = Mathf.Clamp01(_ssT / SsOutSeconds);
                float e = 1f - (1f - k) * (1f - k);                // easeOutQuad
        // 这一趟的进度就是 _ssOffset 自己，掐它 = 位置与"跟着位置走的那几样透明度"一起慢（天然同拍）
                _ssOffset = SsClampSlideStep(_ssOffset, Mathf.Lerp(_ssOffset, 0f, e), 1f, dt);
                SsApply();                                          // 顺带把"渐显"按距离收回去（"滑回去就渐隐"）
        // 收尾判据是"真的到位了"，不是"时间到"：有速度帽之后 0.55 秒到了不等于已经回到 0，
        // 而下面那句 _ssOffset = 0f 是硬归零 —— 不等到位就收，屏幕上会看到那一下跳过去。
        // （也留时限兜底，见 SsSlideHardSeconds）
                if (Mathf.Abs(_ssOffset) <= 0.5f || _ssT >= SsSlideHardSeconds) {
                    _ssOffset = 0f; SsApply(); _ssPhase = 0;
          // 滑回原位 = 登录界面已经收干净（透明度 0），把它藏回预热箱：
          // 留着"活动引用"会被别处当成"个人页开着"（例如右键菜单那道闸门）
                    if (_ssScrub) { ProfileScrubAbort(); _ssScrub = false; }
          // _ssBehind 也要复位：它决定"封面让不让位" —— 不复位的话下一次按下会跳过 SsScrubBegin
          //（以为已经定过了），而那时个人页已经藏回去了，封面让开、底下却没人 = 露底
                    _ssBehind = false;
                }
                return;
            }
            if (_ssPhase == 4) FinishStartScreen();
            if (_ssPhase == 5) {          // Esc 收起：整层淡出，不交接个人页（相位 2 那条会交出去）
                _ssT += dt;
                float k5 = Mathf.Clamp01(_ssT / SsOutSeconds);
                if (_ssGroup != null) _ssGroup.alpha = 1f - k5;
                if (k5 >= 1f) { _ssPhase = -1; DropStartScreenLayer(); }
                return;
            }
        } catch (System.Exception e) {
            Lg.Ex("开始界面 tick 异常（直接收尾）", e);
            FinishStartScreen();
        }
    }

  /// <summary>真正销毁开始界面那层：只在"后面那一层已经铺满"之后调（个人页渐入完成 / 个人页收工），否则会露底。
  /// 上滑交接那一步不再调它，改成"收起来留着"（见 ParkStartScreenLayer）—— 要能"往下滑回开始界面"，
  /// 而且重建那层要扫全场找别人家的全屏层（3 万对象下 250ms）。真正销毁它的地方是 FinishProfileScreen。</summary>
    private static void DropStartScreenLayer() {
        if (_ssGo == null) return;
    // 调用点已经固定成"滑出动画走完之后"（见 FinishStartScreen），这里不用再挂起
        try { Object.Destroy(_ssGo); } catch { }
        _ssGo = null; _ssGroup = null; _ssRootRt = null; _ssTitle = null; _ssHint = null;
        _ssSub1 = null; _ssSub2 = null;
        _ssTitleShadow = null;
    // 更新提示那一块跟着这一层一起没了：引用与"进度条开着"的缓存一起清，
    // 下一个开始界面重建时从干净状态开始（不清的话新层会以为进度条已经开着）
        _ssUpdGroup = null; _ssUpdText = null; _ssUpdBar = null;
        for (int i = 0; i < _ssSteps.Length; i++) { _ssSteps[i] = null; _ssStepFills[i] = null; }
        _ssUpdBarOn = false; _ssStepDone = -1; _ssStepFillPx = -1; _ssGateLogs = 0;
        for (int i = 0; i < _ssStepW.Length; i++) { _ssStepW[i] = 0f; _ssStepK[i] = 0f; }   // 下次重建从"直接就位"开始
        _ssStepShow = -1; _ssStepHoldT = 0f; _ssFillShown = 0f;
    // 门提示的两个平滑量也作废：下一层摆出来时按当时的闸门状态直接落位，
    // 不从上一层的残留值滑过去（那会让新层先亮一下再暗）
        _ssGateNoticeInit = false; _ssUpdK = 0f;
    // 这一趟的"渐显"状态到此为止，下一个开始界面从干净状态开始
        _ssVeilImg = null; _ssCoverGroup = null; _ssScrub = false; _ssBehind = false;
        _ssPrepared = false; _ssParked = false;
        _ssZoom = 1f; _ssZoomRestore = false;   // 缩放状态跟层一起销毁，下一层从原大开始
    // "划回来"那条路的实际进度也归零（跟上面两个一起收，别留半截状态）。
    // 归 1 还是归 0 都不影响正确性（手指一拖就是 dt=0 直接赋值）；归 0 = "这一趟还没往回走"，语义最干净。
        _ssReturnK = 0f;
    // 开始界面这一层收了，背景模糊也归零（模糊是"这一趟过渡"的状态；不归零的话个人页收起后再冒屏保，
    // 背景一出来就是糊的）。走 ResetBgBlur 而不是 SetBgBlur(0f)：后者在"整层还没铺满"时会先挂着，而这里马上要销毁层。
        ResetBgBlur();
    }

  /// <summary>上滑交接完之后把这一层收起来留着（不销毁）：封面 → 0、那几行字 → 0、背景图不吃射线
  ///（否则它会一直挡着登录界面的点击）。_ssParked = true 时它还在场上，但不显示、不挡输入。
  /// 留着是因为往下滑回来时要"同款渐隐"、得有东西能淡回来，重建那层又要扫全场（3 万对象下 250ms）；
  /// 销毁它的地方是 FinishProfileScreen（个人页收工，那里本来就在调 DropStartScreenLayer）。</summary>
    private static void ParkStartScreenLayer() {
        if (_ssGo == null) return;
        _ssParked = true;
        _ssZoomRestore = true;   // 过渡到此收尾：背景缩放从这里开始平滑缩回 1（封面已全透明，回缩是隐形的）
        if (_ssBgImg != null) _ssBgImg.raycastTarget = false;
        SsPaintCover(0f);
        SsPaintTexts(0f);
    // 整块背景（四层 + 呼吸线 + 光点，几百个图元）一起关掉：留着虽然看不见，
    // 但每帧照样参与渲染与刷新。摆回来时在 UnparkStartScreen 里打开。
        NetBgSetActive(_ssCoverNet, false);
    // 静态图也停掉：同一个道理 —— 留着看不见，但整屏方片每帧照样走一遍渲染。
        if (_ssCoverStatic != null) _ssCoverStatic.gameObject.SetActive(false);
    // 视频层也停掉：同一个道理 —— 留着看不见，但播放器会一直解码。
    // TickVideoBg 里那条"所有层都被隐藏就暂停"的自查会接住它（见那里 anyVisible 那段）。
        if (_ssCoverVideo != null) _ssCoverVideo.gameObject.SetActive(false);
    }

    private static void FinishStartScreen() {
        bool fromLib = _ssFromLib;
        _ssPhase = -1;
    // 顺序：先建个人页（此刻 alpha=0、完全看不见），再销毁封面（背景本来就同色，看不到跳变），
    // 之后个人页那 0→1 的渐入才是玩家看得见的动画。反过来先销毁开始界面、下一句才建个人页的话，
    // 中间那一帧两块全屏层都不在，后面的面板/库界面会露出来闪一下。
    // 走了拖动渐显的话个人页早就激活且全亮了，这里只把归属交出去（_pfFromLib），绝不重新摆一遍
    //（重新摆 = _pfPhase 回到 -2 = 它那 0.45 秒的渐入又跑一遍 = 两个动画重叠）。
        if (_ssScrub) ProfileScrubHandOff(fromLib);
        else if (_ssBehind) ProfileKeepShowing(fromLib);   // 屏保盖在已经显示着的个人页上，一个字都不重摆
        else ProfileHandOffFromStart(fromLib);
    // 留着不销毁：要在登录界面"往下滑回开始界面"时能淡回来，
    // 收起来（封面 0 / 字 0 / 不吃射线）比销毁再重建省得多。真正的销毁在 FinishProfileScreen。
        ParkStartScreenLayer();
        Lg.Info("开始界面：已滑出收起（" + (fromLib ? "进入谱面库" : "屏保关闭") + "）");
    // 库内首屏滑走 = 进入谱面库（列表/主页/顶部 tab 的淡回在个人页那边），
    // 这里只剩"屏保那条路要把空闲计时归零"这一件事
        if (!fromLib) {
      // 屏保滑走也进个人页：空闲计时归零，个人页走完再把库界面淡回来
            try { _saverIdleT = 0f; _saverActive = false; } catch { }
        }
    }
}
