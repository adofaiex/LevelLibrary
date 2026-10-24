using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：视频背景。选好视频之后要取消代码背景显示（不是叠在上面）、按 40MB 上限自动压码率、不带音轨。
/// 用 `VideoPlayer` + `RenderMode.APIOnly`，`player.texture` 直接交给 RawImage —— 这个项目一直零 RenderTexture，能不引入就不引入。
/// 压缩用游戏自带的 `<游戏目录>\ffmpeg\ffmpeg.exe`，但那份 ffmpeg 是 `--disable-gpl` 编的、没有 libx264：
/// 优先 `libopenh264`（纯软件，输出最标准；限制 width×height ≤ 9437184），兜底 `h264_mf`
/// （Windows Media Foundation，显卡编码器，5088×2160 也能跑——但各家显卡出的流质量参差，
/// 玩家反馈"导入视频后花屏"集中在它身上，所以只当 libopenh264 装不下时才用）；
/// `h264_nvenc` / `h264_qsv` 实测都失败，不进候选。码率按"40MB ÷ 时长"算（只算视频轨），压完必须验一遍能不能解码。
/// 产物落在 `<mod>\assets\bg_video.mp4`（玩家数据、不进发布包）。
/// 先写临时文件、验过了再挪到位：直接写目标路径的话，压失败时上一版的老文件还躺在那儿，`File.Exists(目标)` 会假装成功。
/// 【多个界面共用一个播放器】面板与进谱面过渡层各有一块代码背景，`AttachVideoBg` 把视频层插在它上面一层，
/// 就绪后让那块背景整组淡出（开始界面与登录界面永远不挂视频层，见各自建背景的地方）；
/// 同一个 `VideoPlayer` 喂多个 RawImage，它们指向同一张 `player.texture`，只解码一路。
/// 同一时刻只有一个界面可见，所以"没人挂层就 Pause"这条能省下后台解码。
public static partial class PortalUI {

 /// <summary>视频背景开关（settings: VideoBgOn，默认关）—— 视频改成按需开启：
 /// 没开、或者本地没有可播文件时，背景由静态图顶着（静态图也没有才轮到代码背景）。
 /// 内置默认视频不再随资源包分发，本地没有那条就用设置里的「下载默认视频背景」从平台取。</summary>
    public static bool VideoBgOn = false;
 /// <summary>玩家自己压的视频（settings: VideoBgPath，默认空）—— 指的是压缩后的产物。
 /// 为空 / 文件不在时走内置默认视频，见 VideoBgEffectivePath。</summary>
    public static string VideoBgPath;
 /// <summary>内置默认视频的文件名。这个文件单独分发（从平台的旧资源包里取，见 Updater 那条下载线），
 /// 拿到之后放在 mod 的 assets 下；玩家没设过视频时直接用它，不经过压缩。</summary>
    private const string VideoDefaultName = "bg_video_default.mp4";

 /// <summary>一档压缩参数。导入视频时玩家自己选一档，四档从"原画质"排到"低"（低档就是原来那套默认值）。
 /// 四样参数各管一件事：分辨率与帧率决定解码压力（背景视频隔着 0.45 压暗层看，越小越不跟游戏抢 GPU），
 /// 码率天花板决定短片的清晰度上限，大小上限决定长片能被压到多小 ——
 /// 目标码率仍由"大小上限 ÷ 时长"算出来，再被码率天花板夹一道，两个约束一个都不少。
 /// Key 是落盘用的稳定标识：settings.txt 里存它而不是下标，将来加档 / 调顺序不会把玩家的选择串到别的档上。</summary>
    private sealed class VideoQuality {
        public string Key;
        public string Label;
        public string Note;
        public int MaxW, MaxH;      // 分辨率上限（0 = 不缩放，保持源尺寸）
        public double MaxFps;       // 帧率上限（0 = 不动源帧率）
        public int MaxKbps;         // 码率天花板
        public long MaxBytes;       // 产物大小上限
    }

 /// <summary>四档压缩参数。最低档就是改之前那套写死的默认值，原样保留，别在这一档上做任何"顺手调整"。</summary>
    private static readonly VideoQuality[] VideoQualities = {
        new VideoQuality { Key = "source", Label = "原画质", Note = "不缩不降帧，最清晰，但加载慢、低配可能卡",
                           MaxW = 0, MaxH = 0, MaxFps = 0, MaxKbps = 40000, MaxBytes = 500L * 1024L * 1024L },
        new VideoQuality { Key = "high", Label = "高", Note = "1080p 以内 · 30fps",
                           MaxW = 1920, MaxH = 1080, MaxFps = 30.0, MaxKbps = 12000, MaxBytes = 150L * 1024L * 1024L },
        new VideoQuality { Key = "mid", Label = "中", Note = "720p 以内 · 30fps",
                           MaxW = 1280, MaxH = 720, MaxFps = 30.0, MaxKbps = 6000, MaxBytes = 80L * 1024L * 1024L },
        new VideoQuality { Key = "low", Label = "低", Note = "540p 以内 · 24fps（默认，最省）",
                           MaxW = 960, MaxH = 540, MaxFps = 24.0, MaxKbps = 4000, MaxBytes = 40L * 1024L * 1024L },
    };
 /// <summary>默认挡位（下标指向"低"）：settings.txt 里没有这一项、或者存了不认识的 Key 时用它。</summary>
    private static readonly VideoQuality VideoQualityDefault = VideoQualities[VideoQualities.Length - 1];
 /// <summary>玩家上次选的挡位（settings: VideoQualityKey）。导入时对话框默认标它。</summary>
    private static VideoQuality _videoQuality = VideoQualityDefault;
 /// <summary>正在压缩的那一次用的挡位。后台线程读它，所以必须在起线程之前定下来。</summary>
    private static VideoQuality _videoJobQuality = VideoQualityDefault;
 /// <summary>挡位标识（落盘用）。做成属性而不是一个独立的字符串字段：读设置与点按钮两条路都只写这一个名字，
 /// 取的时候永远从当前挡位现算，不会出现"存的是 A、用的是 B"这种两个来源不同步的情况。
 /// 赋值时顺手解析：认得的 Key 直接用，认不得的（老文件 / 手改坏了）回落到默认档。</summary>
    public static string VideoQualityKey {
        get { return _videoQuality != null ? _videoQuality.Key : ""; }
        set { _videoQuality = FindVideoQuality(value); }
    }

 /// <summary>按落盘的标识找挡位；找不到（空 / 老版本文件 / 手改坏了）就回落到默认档。
 /// 读设置与点按钮都用它，两处各写一遍的话迟早有一处忘了回落。</summary>
    private static VideoQuality FindVideoQuality(string key) {
        if (!string.IsNullOrEmpty(key)) {
            for (int i = 0; i < VideoQualities.Length; i++)
                if (VideoQualities[i].Key == key) return VideoQualities[i];
        }
        return VideoQualityDefault;
    }
    private const int VideoFfmpegMaxSeconds = 600;             // 压缩最多等 10 分钟（到点强杀，别把一局卡死）
 /// <summary>视频最多等多久（有界面在看、却一直不就绪，6 秒后打一行日志说明"先看代码背景"）。
 /// 视频正常时用不到它（Prepare 一般 0.2~1.5 秒）。</summary>
    private const float VideoColdFallbackSeconds = 6f;
 /// <summary>"播放器没就绪"之后隔多久补问一次 Prepare（见 PrepareVideoIfStale 与 TickVideoBg 的自愈段）。
 /// 正常的 Prepare 要 0.2~1.5 秒，取 2 秒：既不会把正在跑的那一次问废，也不用让玩家对着代码背景等好几秒。</summary>
    private const float VideoReprepareSeconds = 2f;
 /// <summary>「打开界面这段时间先别让代码背景亮」的窗口（秒）：面板建背景那一帧就把代码背景收起来，
 /// 视频在这个窗口内自己接上（重开面板实测 1 帧）；等超了就认输、把它淡回来 ——
 /// 宁可不显示也不要闪，但也不能一辈子不显示（视频真起不来时纯色底一直陪着玩家）。
 /// 必须 &gt; 0 且 &lt; VideoYieldMaxSeconds：比"让位到底"那个上限还长就没意义了。</summary>
    private const float PanelBgWaitSeconds = 0.35f;
 /// <summary>「让位到底」最多让多久（秒）：视频这条路通着（开关开 + 文件在）却一直没就绪 ——
 /// 到这个点就把代码背景淡回来（淡入不是闪入），别让玩家一直对着一块纯色底。
 /// 取 1.5 秒：正常的重开面板 1~3 帧、最冷的第一次 Prepare 实测也就 0.2 秒上下，1.5 秒是最坏情况的十倍；
 /// 视频真坏了（解码起不来）也就忍这 1.5 秒。比 VideoColdFallbackSeconds（6 秒那句日志）短：先让位、后认输。</summary>
    private const float VideoYieldMaxSeconds = 1.5f;
 /// <summary>「已接管视频」这个状态要连续成立多久才算数（秒）。播放器在换源/scene 切换/卡顿自愈前后
 /// 会短暂地就绪↔未就绪抖动；每次抖动都让代码背景跟着收-放一次，而淡出是 0.55 秒的渐变，
 /// 肉眼就看见"神经图和视频同屏"。两个方向都用这个值做迟滞：就绪要连着 hold 这么久才收代码背景，
 /// 丢失也要连着 hold 这么久才放回来。取 0.6 秒：跨过 0.55 秒那一次淡出全程，两次切换就不会交叠成同屏。</summary>
    private const float VideoHandoverHold = 0.6f;
    /// <summary>迟滞后的"视频已稳定接管"（代码背景据此收起/放回），以及两个方向的计时。
    /// _videoShowingHoldT &lt; 0 表示"已经在稳定态"（此刻又抖了一下也不重新计时）。</summary>
    private static bool _videoShowingStable;
    private static float _videoShowingHoldT;
    private static float _videoLostHoldT;

    private static VideoPlayer _bgVideo;                        // 唯一的播放器（DontDestroyOnLoad）
    private static string _bgVideoLoadedPath;                   // 当前装进去的是哪个文件（换了才重新 Prepare）
    private static volatile bool _videoUiRefreshPending;        // 后台压完视频 → 主线程刷设置页
    private static string _videoTmpPath;                        // 压缩时的临时产物（验过了才挪到正式位置）
 // 压缩期间界面必须"看得见在做事"：一个大视频要压几十秒，界面一动不动会被当成没反应、直接关游戏
    private static volatile bool _videoBusy;                    // 正在压缩（跨线程读）
    private static volatile int _videoPercent;                  // 0~99（按临时文件大小算）
    private static long _videoTargetBytes;                      // 这次的目标大小（算进度用）
    private static Button _videoPickBtn;                        // 「选择视频文件」那个按钮（主线程改字）
    private static Text _videoPickLabel;
    private static Button _defaultVideoBtn;                     // 「默认视频背景」那个按钮（恢复 / 下载两个形态）
    private static Text _defaultVideoLabel;
 /// <summary>「默认视频背景」按钮的宽度：标准 180 放不下「下载默认视频背景」八个字，这一颗单独加宽
 /// （右缘仍与别行按钮对齐，见设置页那一处摆位）。</summary>
    private const float DefaultVideoBtnW = 236f;
    private static bool _videoWasReady;                         // 上帧"就绪了没"（只为打状态变化的那行日志）
    private static float _videoNotReadyT;                       // "还没就绪"持续了多久（每 2 秒打一行）
    private static int _videoNotReadyLogs;
    private static float _videoFrameT;                          // "真的播了多少帧 + 游戏自己多快"的采样计时
    private static int _videoFrameLogs;
    private static int _videoPerfFrames;                        // 这一段里游戏渲染了多少帧
    private static float _videoPerfWorst;                       // 这一段里最差的一帧有多久
    private static long _videoLastFrame;
    private static float _videoReprepareT;                      // "播放器丢了就绪状态"的自愈计时
    private static int _videoReprepareLogs;
 /// <summary>播放器是被我们自己 Pause 掉的（没人看，见 PauseVideoBackground）。
 /// 从这种暂停里回来不能只 Play() 续播：它会停在最后一帧、只把"播放中"报成真，
 /// 画面上就是一块不动的视频，而"就绪"那条判据完全看不出来（见 TickVideoBg 的恢复段）。</summary>
    private static bool _videoPaused;
 /// <summary>上一次向播放器要 Prepare 的时刻（unscaledTime），用来分辨"刚问过、还在准备"与
 /// "就绪状态丢了、这一屏要等好久"：后者当场补一次（见 PrepareVideoIfStale）。</summary>
    private static float _videoPrepareAt;
    private static long _videoWatchFrame = -1;                  // 卡死看门狗：上次看到的帧号
 /// <summary>代码背景与动态背景的交接：只认"视频真的就绪了"这一个判据 ——
 /// 没就绪就一直看代码背景（它本来就是我们自己的兜底），不闪、也不会空屏。</summary>
 // ---------- 压缩线程 → 主线程的交接口 ----------
 // 压缩跑在独立线程上，而 ShowToast / VideoPlayer / GameObject 这些都只能在主线程碰：
 // 后台线程里调它们要么抛异常（没人接就是"这次操作悄悄失败"），要么直接踩 uGUI 的渲染状态。
 // 更要命的是"换文件"这一步：播放器正播着 <mod>\assets\bg_video.mp4，压缩线程去 Delete + Move
 // 覆盖这个文件 = 解码器（WMF）手里的文件被抽走，读到半截数据就 native 崩。
 // 所以后台只准做三件事：写临时文件、改下面这几个标量、打日志；换文件与提示一律回主线程做。
    private static volatile bool _videoJobDone;                 // 后台收工（成功 / 失败都置）
    private static volatile bool _videoJobOk;                   // 成功 = 临时文件已就绪，等主线程换上去
    private static volatile string _videoJobMsg;                // 给玩家看的一句话（主线程弹）
    private static string _videoJobArg;                         // 成功时 = 临时文件路径
    private static int _videoSwapStage;                         // 0 空闲 / 1 已停播放器、等文件句柄释放后换文件
    private static int _videoSwapFrames;                        // 停播放器之后等了几帧
    private static int _videoSwapTries;                         // 换文件重试次数
    private static string _videoSwapTmp, _videoSwapMsg;

    private static float _videoWaitT;                           // 累计"有界面在看、但视频还没就绪"的时间
 /// <summary>「代码背景让位」已经等了多久（见 PanelBgWaitSeconds）：从"视频能用、却还没就绪"记起，
 /// 到认输为止。面板进场那道闸与"要不要把代码背景放回来"读的都是它。</summary>
    private static float _videoHandoverT;
    private static int _videoFallbackLogs;
    private static float _videoStallT;                          // 帧号连续多久没动
    private static int _videoStallFrames;                       // 这段时间里我们渲染了多少帧
                // （用它区分"视频自己死了"和"游戏整个卡住了"，见看门狗那段）
    private static int _videoStallLogs;
 // 自愈的闸门：卡死看门狗按"卡住期间我们自己渲染了多少帧"来分辨"视频自己死了"和"游戏整个卡住"
 //（游戏真卡住时我们也出不了几帧），两次强制重启之间至少隔 15 秒，免得在一坨卡顿里反复重来；
 // "没就绪就重新 Prepare"只认播放器自己的就绪标志，不掺"游戏忙不忙" —— 要看视频的那几屏
 //（开面板 / 进谱面过渡）本来就是最卡的几秒，再等"游戏闲下来"等于整段都只剩代码背景。
    private static float _videoRestartCd;                        // 两次强制重启之间的冷却
    private static readonly List<RawImage> _bgVideoLayers = new List<RawImage>();        // 各界面上的视频层
    private static readonly List<CanvasGroup> _bgVideoHidden = new List<CanvasGroup>();  // 与之配对的"要淡出的代码背景组"
 /// <summary>与上表一一对应：这一屏建背景时要求「代码背景当场让位」（面板那一路）。
 /// 开始界面 / 登录界面不挂视频层；进谱面过渡层自己有淡入层盖着那几帧，两者都不带这个标记、口径一个字不变。</summary>
    private static readonly List<bool> _bgVideoYield = new List<bool>();

 /// <summary>产物文件名（放在 mod 的 assets 下，跟 bg_custom.png 一个待遇）</summary>
    private static string VideoOutPath() {
        try { return Path.Combine(Main.ModEntry.Path, "assets", "bg_video.mp4"); } catch { return null; }
    }

 /// <summary>内置默认视频的完整路径（&lt;mod&gt;\assets\bg_video_default.mp4，随资源包分发）。拿不到 mod 目录返回 null。</summary>
    private static string VideoDefaultPath() {
        try { return Path.Combine(Main.ModEntry.Path, "assets", VideoDefaultName); } catch { return null; }
    }

 /// <summary>此刻该播哪个文件：玩家自己压的优先（路径在 + 文件在），没有就回内置默认视频；
 /// 两个都不在返回 null（代码背景兜底）。播放、挂层判据、设置页提示都读它，别再直接比 VideoBgPath。</summary>
    private static string VideoBgEffectivePath() {
        try {
            if (!string.IsNullOrEmpty(VideoBgPath) && File.Exists(VideoBgPath)) return VideoBgPath;
            string def = VideoDefaultPath();
            if (!string.IsNullOrEmpty(def) && File.Exists(def)) return def;
            return null;
        } catch { return null; }
    }

 /// <summary>视频这条路通不通（开关开着 + 有可播的文件：自己的或内置默认的）</summary>
    private static bool VideoBgUsable() {
        try { return VideoBgOn && VideoBgEffectivePath() != null; } catch { return false; }
    }


    private static bool VideoHasFrame() {
        try { return _bgVideo != null && _bgVideo.isPrepared && _bgVideo.texture != null; } catch { return false; }
    }

 /// <summary>"视频真的接管了"（= 有帧 + 在播）：视频层当场显形、代码背景按老路淡出。交接只有这一个判据。</summary>
    private static bool VideoReadyToShow() {
        return VideoHasFrame() && !_videoPaused;
    }

 /// <summary>"这个播放器手里拿到过有效帧"的锁存：拿到过一次就不再退回"没帧"。
 /// 糊图取源读它 —— 不然预热期里 `isPrepared && texture != null` 偶尔翻假，糊图就会在
 /// "视频帧"与"代码背景"之间逐帧来回跳（表现是头像背后的整块背景在闪 ）。
 /// 只有重新 prepare / 换文件 / 停止才清零（VideoFrameLatchReset）。</summary>

 /// <summary>「动态背景还在路上」：视频能用（开关开 + 文件在）、有屏在看，却还没就绪、也还没等超窗口。
 /// 面板进场那道闸读它 —— 背景没着落就先别让面板亮出来（见 PortalUI.PanelBuild.Shell.cs 的 TickPanelFade）。
 /// 就绪 / 视频不可用 / 没有屏要它 / 窗口到点，一律为假（到点必须放行，否则视频真起不来时面板一直不显示）。</summary>
    private static bool VideoHandoverPending() {
        try {
            if (!VideoBgUsable()) return false;
            if (_bgVideoLayers.Count == 0) return false;
            if (VideoReadyToShow()) return false;
            return _videoHandoverT < PanelBgWaitSeconds;
        } catch { return false; }
    }


 // ---------- 界面侧：挂视频层 ----------

/// <summary>某一层视频当前"该不该收起代码背景"——全项目唯一的权威判据。
/// 口径（洛洛定的，改了好几轮才定下来）：视频背景开着就永远不画神经图。
/// 为什么是"开着"而不是"就绪了"：早先判的是"这一帧视频层真的显形了才让位"，
    /// 而播放器从 Prepare 到就绪要几十帧，那段时间代码背景已经亮在屏幕上了
    ///（实测日志：`alpha=0.00 代码背景=1.00` 紧跟着 `alpha=1.00 代码背景=1.00`），
    /// 这就是"打开界面神经图闪一下"。改成不等就绪就没这个窗口了。
/// 之前还栽过一次：让位这件事有两个写入者（`TickVideoBg` 每帧收放 +
/// `ProfileScreen` 每帧无条件按回显示），对着干就是一帧收一帧放。
/// 现在两边都问这一个出口，交接迟滞只管"淡入淡出多快"，不再管"谁负责决定"。
/// 真正让代码背景回来的只有三种情况：视频不可用（开关关 / 文件不在）、
    /// 这一层已被收起不渲染、这一层压根还没挂上（`ri == null`）。
    /// 最后那种最容易漏：判据若只看"视频可用"就会把代码背景收起来，而那一屏没有视频层可显示 = 黑屏
    ///（离线穷举时真抓到了这个漏洞）。凡是问"某一层该不该让位"的，层不存在就一律不让位。
    /// 那段"视频还没出来"的空窗由每一屏自己的纯色底兜（每屏都有一层 Bg），观感是背景淡入视频。
    /// </summary>
    /// <param name="ri">这一层的 RawImage；传 null 表示"这一屏还没挂上视频层"，返回 false。</param>
public static bool CodeBgShouldYield(RawImage ri) {
   try {
       // 这一层压根没挂上（ri 为空），所以 让位不得：那一屏没有视频层可显示，收起代码背景就是黑屏。
   // 这条最容易漏，离线穷举时真抓到过一次。
       if (ri == null) return false;
     if (!ri.gameObject.activeInHierarchy) return false;   // 层被收起来：不渲染，不能把代码背景扣住
     // 口径（洛洛定的）：视频背景开着就一帧都不画神经图 —— 不等"播放器就绪"、不看 alpha。
     // 之前等"显形了再让位"恰恰是闪的根源：播放器要在这几十帧里 Prepare，
     // 那段时间代码背景已经亮在屏幕上了（实测日志 `alpha=0.00 代码背景=1.00`）。
  // 那一小段"什么都还没出来"的空窗，用这一屏自己的纯色底兜（每屏都有一层 Bg），
        // 观感就是背景从纯色淡入视频，不会看到网格图。
  // 真正让代码背景回来的只有两种情况：视频不可用（没开 / 文件不在），或这一层根本没挂/已被收起。
      return VideoBgUsable();
  } catch { return false; }
    }

 /// <summary>某个界面建好代码背景之后调它一次（参数就是那一组的 CanvasGroup）。
 /// 视频没开 / 文件不在，就什么都不做（代码背景照旧显示，不会留空框、不会黑屏）；
 /// 视频可用，就在代码背景上面插一层 RawImage（先透明，等播放器准备好了再显形 + 让下面那组淡出）。
 /// 返回挂上去的那一层（没挂上 = null）：调用方要据此判断"这一屏现在有没有视频"，
 /// 例如决定糊图该裁视频纹理还是代码背景。`_bgVideoLayers` 每帧会清掉已销毁的层，
 /// 所以调用方拿着这个引用要按 == null 判活（Unity 对已销毁对象重载了它）。</summary>
    public static RawImage AttachVideoBg(CanvasGroup netGroup) { return AttachVideoBg(netGroup, false); }

 /// <summary>同上，外加 yieldCodeBg = 「这一屏的代码背景建出来就当场让位」。
 /// 只有面板带上它：它一帧都不许露出代码背景（洛洛报的「打开界面闪一下神经网络」就是它趁建背景那一帧冒出来的）。
 /// 进谱面过渡层不带：那一屏的背景本来就是代码背景，而且它自己有淡入层盖着开头那几帧（见 EnterTransition）。</summary>
    public static RawImage AttachVideoBg(CanvasGroup netGroup, bool yieldCodeBg) {
        try {
            if (netGroup == null) return null;
            if (!VideoBgUsable()) return null;
            var parent = netGroup.transform.parent;
            if (parent == null) return null;
            var go = new GameObject("BgVideo", typeof(RectTransform));
            go.transform.SetParent(parent, false);
   // 插在代码背景上面一层：它不吃射线（代码背景也不吃），谁上谁下只影响淡出那 0.55 秒的观感，
   // 压在模版上能让"代码背景淡掉"这个过程完整可见。
            go.transform.SetSiblingIndex(netGroup.transform.GetSiblingIndex() + 1);
            var ri = go.AddComponent<RawImage>();
            ri.raycastTarget = false;
            ri.color = new Color(1f, 1f, 1f, 0f);      // 准备好之前先透明
            Stretch(ri.rectTransform, Vector2.zero, Vector2.one);
            _bgVideoLayers.Add(ri);
            _bgVideoHidden.Add(netGroup);
            _bgVideoYield.Add(yieldCodeBg);
   // 挂上去的这一刻就按播放器当前状态摆好：就绪了当场显形 + 当场让代码背景消失（一帧都不许两层同时亮）
            ApplyVideoLayerNow(ri, netGroup, yieldCodeBg);
            Lg.Info("视频背景层已挂到 " + parent.name
                + (ri.color.a >= 1f ? "（视频已在就绪状态 ，所以 立刻显形 + 代码背景当场收起，不闪）"
                   : yieldCodeBg ? "（等视频就绪；代码背景当场让位，一帧都不画）" : "（等视频就绪）"));
            return ri;
        } catch (System.Exception e) {
            Lg.Ex("挂视频背景层失败（代码背景照旧）", e);
            return null;
        }
    }

 /// <summary>刚挂上去那一刻就把这一层摆成"现在该有的样子"。
 /// ① 播放器已经就绪（它是常驻的，界面关掉时只 Pause 不 Stop，所以第二次开面板时状态还在）
 /// 就把纹理当场贴上 + 当场让代码背景消失，一帧都不闪；
 /// ② 还没就绪就让代码背景亮着 —— 它是兜底，等到真就绪了再交接（见 TickVideoBg）；
 /// ③ yieldCodeBg（面板那一路）：只要视频这条路是通的，代码背景当场让位（immediate，一帧都不画）。
 /// 洛洛报的「打开界面闪一下神经网络」就是它趁这一帧冒出来的：那一刻播放器被我们 Pause 过
 /// （旧判据里带着 !_videoPaused，判不出"就绪"），于是按兜底口径把代码背景亮了一帧，下一帧视频接上、它被切走。
 /// 让位之后由 TickVideoBg 每帧盯着：真就绪 / 等超 VideoYieldMaxSeconds 才让它淡回来。
 /// 「没被我们暂停过」仍然只用来决定视频层显不显形：暂停过的播放器手里是最后一帧的旧画面，
 /// 当场显形就是把一块不动的视频摆给玩家看 —— 那几帧视频层还是透明的，代码背景同样不许顶上，
 /// 面板自己也没亮起来（见 TickPanelFade 的闸），玩家看到的只是"按了快捷键、一下后面板渐入"。</summary>
    private static void ApplyVideoLayerNow(RawImage ri, CanvasGroup net, bool yieldCodeBg) {
        if (ri == null) return;
     // 这一层现在真的在显示吗：被 park 收起来（SetActive(false)）或被整块画布关掉时都不算。
     // 少了这一条就会出"神经图与视频同屏"：判据只看播放器（VideoReadyToShow 读的是播放器），
     // 播放器有帧就照样把代码背景收掉，而这一层的 RawImage 根本不在渲染：两层判定脱钩。
        bool layerVisible = ri.gameObject.activeInHierarchy;
        bool hasFrame = VideoHasFrame();
        bool ready = hasFrame && !_videoPaused;
        if (!hasFrame) PrepareVideoIfStale();     // 就绪状态丢了就当场补问一次（见该函数）
        if (hasFrame) {
            ri.texture = _bgVideo.texture;
            ri.color = new Color(1f, 1f, 1f, (ready && layerVisible) ? 1f : 0f);
            _videoWaitT = 0f;
        }
     // 视频层现在不显示，代码背景就必须回来（收着它就等于这一屏没有背景）。
     // 面板那支要"一帧都不许两层同时亮"的保证，所以仍然 immediate。
        if (yieldCodeBg) {
            NetBgSetShown(net, !(VideoBgUsable() && layerVisible), true);
            return;
        }
// 非面板那一路（个人页 / 开始界面封面 / 进谱面过渡层）：判据与每帧那条完全一致
     //（`CodeBgShouldYield`，口径=视频开着就不画神经图）。以前这一路判的是"会不会马上显形"，
     // 而播放器 Prepare 要几十帧 —— 那段时间神经图已经亮在屏幕上了（实测日志 `alpha=0.00 代码背景=1.00`），
    // 也就是"打开界面神经图闪一下"。现在不等就绪：视频可用就当场让位，
     // 那一小段空窗由这一屏自己的纯色底兜（每屏都有一层 Bg）。
        if (!layerVisible) { NetBgSetShown(net, true, false); return; }   // 层被收起：显示代码背景（不黑屏）
        NetBgSetShown(net, !CodeBgShouldYield(ri), true);
    }

 /// <summary>一层界面要用视频、而播放器"早就该就绪却没就绪"时补一次 Prepare。
 /// 换场景与面板重建都会让它悄悄丢掉准备好状态，而 TickVideoBg 那条自愈要等好几秒，
 /// 那几秒里这一屏只能看代码背景（进谱面过渡整段都落在里面）。
 /// 刚问过的（不到 VideoReprepareSeconds）不重复问：正常的 Prepare 本身就要时间，重复问是白折腾。</summary>
    private static void PrepareVideoIfStale() {
        try {
            if (_bgVideo == null || _bgVideo.isPrepared) return;
   // 还没为这个文件问过（第一次挂层 / 刚换过文件）：交给 TickVideoBg 那一条去问
            string want = VideoBgEffectivePath();
            if (want == null || _bgVideoLoadedPath != want) return;
            if (Time.unscaledTime - _videoPrepareAt < VideoReprepareSeconds) return;
            _videoPrepareAt = Time.unscaledTime;
            _bgVideo.Prepare();
            if (_videoReprepareLogs < 6) {
                _videoReprepareLogs++;
                Lg.Info("视频背景：挂层时播放器还没就绪 ，所以 当场重新 Prepare"
                    + "（等自愈要好几秒，那几秒这一屏只有代码背景）");
            }
        } catch { }
    }

 /// <summary>把已经销毁的层清掉（画布一销毁这些引用就成了"假的 null"）。
 /// 层没了要把配对的代码背景放回来，否则那一屏的背景会一直淡着（看着就是一块黑）。</summary>
    private static void PruneVideoLayers() {
        for (int i = _bgVideoLayers.Count - 1; i >= 0; i--) {
            var ri = _bgVideoLayers[i];
            var net = (i < _bgVideoHidden.Count) ? _bgVideoHidden[i] : null;
            if (ri == null) {
                if (net != null) NetBgSetShown(net, true, false);
                _bgVideoLayers.RemoveAt(i);
                if (i < _bgVideoHidden.Count) _bgVideoHidden.RemoveAt(i);
                if (i < _bgVideoYield.Count) _bgVideoYield.RemoveAt(i);   // 三张表必须一起动（下标一一对应）
            }
        }
    }

 /// <summary>设置页那行要能看出"正在压缩"。
 /// 只在主线程调（TickVideoBg 里）—— 后台压缩线程只改 `_videoBusy / _videoPercent` 两个标量。</summary>
    private static void RefreshVideoPickRow() {
        try {
            if (_videoPickLabel != null) {
                string want = _videoBusy
                    ? ("压缩中… " + Mathf.Clamp(_videoPercent, 0, 99) + "%")
                    : "选择视频文件";
                if (_videoPickLabel.text != want) _videoPickLabel.text = want;
            }
            if (_videoPickBtn != null && _videoPickBtn.interactable == _videoBusy)
                _videoPickBtn.interactable = !_videoBusy;
        } catch { }
    }

 /// <summary>「默认视频背景」按钮此刻该写什么字：本地有默认那份就「恢复」，没有就「下载」。
 /// 下载会从平台把它取回来放到 assets 下，之后这里自然翻成「恢复」。</summary>
    private static string DefaultVideoButtonText() {
        try { return File.Exists(VideoDefaultPath()) ? "恢复默认视频背景" : "下载默认视频背景"; } catch { return "下载默认视频背景"; }
    }

 /// <summary>「默认视频背景」那一行每帧刷新（TickVideoBg 的主线程段调）：
 /// 按本地状态在「恢复 / 下载」之间换文案；下载中显示进度并禁点（防重复触发）。</summary>
    private static void RefreshDefaultVideoRow() {
        try {
            if (_defaultVideoLabel == null && _defaultVideoBtn != null)
                _defaultVideoLabel = _defaultVideoBtn.GetComponentInChildren<Text>();
            if (_defaultVideoLabel != null) {
                string want = Updater.DownloadingDefaultVideo
                    ? ("下载中… " + Mathf.Clamp((int)(Updater.Progress * 100f), 0, 100) + "%")
                    : DefaultVideoButtonText();
                if (_defaultVideoLabel.text != want) _defaultVideoLabel.text = want;
            }
            if (_defaultVideoBtn != null) {
                bool busy = Updater.DownloadingDefaultVideo;
                if (_defaultVideoBtn.interactable == busy) _defaultVideoBtn.interactable = !busy;
            }
        } catch { }
    }

 /// <summary>「默认视频背景」按钮的落点：有文件就切回内置默认那份；没有就去平台把 0012 资源包里的视频取回来
 /// （下载完成由 Updater 收尾回调 OnDefaultVideoDownloaded，切到刚下好的这份）。</summary>
    private static void OnDefaultVideoButton() {
        try {
            if (File.Exists(VideoDefaultPath())) { RestoreDefaultVideoBackground(); return; }
            Updater.DownloadDefaultVideo();
        } catch (System.Exception e) { Lg.Ex("默认视频背景按钮失败", e); }
    }

 /// <summary>默认视频下载完成时调（Updater 主线程收尾那条进来）：把视频背景切到刚下好的这份。
 /// 玩家自己那份产物（bg_video.mp4，在本 mod 的 assets 下、不属于用户的原始素材）跟「恢复默认视频」
 /// 一个待遇：切走就删。开关一并打开，落盘保存。</summary>
    internal static void OnDefaultVideoDownloaded() {
        try {
            string old = VideoBgPath;
            VideoBgPath = "";
            VideoBgOn = true;
            _bgVideoLoadedPath = null;      // 逼播放器丢掉"手里装的是哪个文件"，下一帧按新路径重载
            _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
            AutoFeatures.Save();
            try { if (!string.IsNullOrEmpty(old) && File.Exists(old)) File.Delete(old); } catch { }
            ApplyVideoBackground();
            // 开关是程序改的、不是玩家点的：设置页那颗开关要当场跟上（它一般只在点击时刷）
            try { RefreshSettingsToggles(); } catch { }
            Lg.Info("默认视频背景：已切换为默认视频背景（原文件已删="
                + (!File.Exists(old)) + "，现在播 " + (VideoBgEffectivePath() ?? "(无)") + "）");
            ShowToast(Lang.Tr("默认视频背景已下载，已切换为默认视频背景"));
        } catch (System.Exception e) {
            Lg.Ex("下载完成后切换默认视频失败", e);
            ShowToast(Lang.Tr("默认视频背景下载完成，但切换失败，看日志喵"));
        }
    }

 /// <summary>视频是在面板已经开着的时候才选好的，而视频层只在"界面建背景那一下"挂，
 /// 所以这里每帧自查一次：面板那组代码背景还在、而它上面没有视频层，就补挂（幂等，很便宜）。</summary>
    private static void EnsurePanelVideoLayer() {
        try {
            if (_panelNetGroup == null) return;
            var parent = _panelNetGroup.transform.parent;
            if (parent == null) return;
            for (int i = 0; i < _bgVideoLayers.Count; i++) {
                var ri = _bgVideoLayers[i];
                if (ri != null && ri.transform.parent == parent) return;   // 已经挂过了
            }
            Lg.Info("面板已经在开着 ，所以 现场补挂一层视频背景");
            AttachVideoBg(_panelNetGroup);
        } catch { }
    }

 /// <summary>面板背景那几层的顺序：纯色底 → 代码背景 → 视频 → 遮罩（越靠后越上层）。
 /// 面板那边建遮罩时会 `veil.SetSiblingIndex(1)`，会把先建的几层顶乱，所以建齐之后统一排一次
 /// （只动这几层，页面内容都是后面建的，天然在上）。</summary>
    private static void OrderPanelBgLayers(Image bgImg, Image veil) {
        try {
            if (bgImg == null || veil == null) return;
            var parent = bgImg.transform.parent;
            if (parent == null) return;
            int i = 0;
            bgImg.transform.SetSiblingIndex(i++);
            if (_panelStaticLayer != null && _panelStaticLayer.transform.parent == parent)
                _panelStaticLayer.transform.SetSiblingIndex(i++);
            if (_panelNetGroup != null && _panelNetGroup.transform.parent == parent)
                _panelNetGroup.transform.SetSiblingIndex(i++);
            for (int k = 0; k < _bgVideoLayers.Count; k++) {
                var ri = _bgVideoLayers[k];
                if (ri != null && ri.transform.parent == parent) ri.transform.SetSiblingIndex(i++);
            }
            veil.transform.SetSiblingIndex(i);
        } catch { }
    }

 /// <summary>面板那层静态图（建背景时挂、排序时用；关面板随画布销毁）</summary>
    private static Image _panelStaticLayer;

 /// <summary>"我要排在背景那几层之后"的东西，下标一律用这个函数算。
 /// 背景那几层是：纯色底 → 代码背景 → （有视频时的 BgVideo）→ Veil，数量会变，绝不许写死 ——
 /// 写了就会把 Veil 挤到"我"上面，整页被 0.45 黑罩盖住。</summary>
    public static int BgLayerInsertIndex() {
        try {
            if (_panelVeilImg != null) return _panelVeilImg.transform.GetSiblingIndex() + 1;
        } catch { }
        return 4;   // 拿不到就退回"纯色底 + 代码背景 + 视频 + 遮罩"之后
    }

 /// <summary>播放器：只有一个（DontDestroyOnLoad，换场景不被带走）。
 /// 宿主名字必须是 `BgVideoPlayer` —— 它登记在"常驻根节点"白名单里（不挂画布下面、跨场景活着），改了名字审计会报红。</summary>
    private static void EnsureVideoPlayer() {
        if (_bgVideo != null) return;
        var host = new GameObject("BgVideoPlayer");
        UnityEngine.Object.DontDestroyOnLoad(host);
        _bgVideo = host.AddComponent<VideoPlayer>();
        _bgVideo.playOnAwake = false;
        _bgVideo.isLooping = true;
        _bgVideo.audioOutputMode = VideoAudioOutputMode.None;   // 不要音轨（也省解码）
        _bgVideo.renderMode = VideoRenderMode.APIOnly;          // 纹理交给 RawImage，不自己建 RT
        _bgVideo.skipOnDrop = true;                             // 掉帧就跳，别越积越多
        _bgVideo.playbackSpeed = 1f;
  // `timeReference` 默认是 `AudioClock`，而我们不要音轨（`audioOutputMode = None`），音频时钟可能一直不走，
  // 表现为播放器自称在播、帧号却一动不动（"概率性卡住"），而且"没就绪就重 Prepare"的自愈够不着它。
  // 改成 `Freerun`：按真实时间推进，跟音频无关，也不受游戏 timescale 影响。
        try { _bgVideo.timeReference = VideoTimeReference.Freerun; } catch { }
  // 播放器自己的报错要打出来，不然"一直没就绪"就只能靠猜
        try {
            _bgVideo.errorReceived += (vp, msg) => {
                try { Lg.Info("视频背景：播放器报错 → " + msg); } catch { }
            };
        } catch { }
    }

 /// <summary>每帧（常驻段调，面板关着也可能有人看：开始界面/屏保/进谱面过渡）。
 /// 没人挂层就 Pause；有层就 Prepare/Play 一遍 + 刷新纹理。</summary>
    /// <summary>同一时刻只让"当前显示的那块画布"上的视频层亮着。别的屏（开始界面 / 个人页 / 进谱面过渡）
    /// 留下的层如果还活着，两层视频叠一起就是"视频背景错位/重影"（日志里 视频层=2 就是这么来的）。
    /// 不能只在挂层时一刀切：别的屏后挂层时会把正在看的那层也关掉（表现是"视频整个没了"）。
    /// 所以按"它挂在哪块画布、那块画布还显示着吗"逐帧判，值没变不写。</summary>
    /// <summary>诊断节流：每秒最多打一条"视频层点名"。</summary>
    private static float _videoLayerDiagAt;

    private static void EnforceSingleVideoLayer() {
        try {
            if (Time.unscaledTime >= _videoLayerDiagAt) {
                _videoLayerDiagAt = Time.unscaledTime + 2f;
                for (int i = 0; i < _bgVideoLayers.Count; i++) {
                    var d = _bgVideoLayers[i];
                    if (d == null) continue;
                    var dcv = d.GetComponentInParent<Canvas>();
                    Lg.Info("视频层点名#" + i + "：父=" + (d.transform.parent != null ? d.transform.parent.name : "-")
                        + " 画布=" + (dcv != null ? dcv.name : "-")
                        + " 画布启用=" + (dcv != null ? (dcv.isActiveAndEnabled ? "是" : "否") : "-")
                        + " 自身启用=" + (d.gameObject.activeSelf ? "是" : "否")
                        + " alpha=" + d.color.a.ToString("F2"));
                }
            }
        } catch { }
        for (int i = _bgVideoLayers.Count - 1; i >= 0; i--) {
            var l = _bgVideoLayers[i];
            if (l == null) { _bgVideoLayers.RemoveAt(i); continue; }
            // 画布找不到（层级里根本没有 Canvas，比如预热层挂的宿主已经被拆）也算"不在显示"：
            // 没有画布的 RawImage 本来就画不出来，留着只会一直活跃在层名单里——
            // 实机日志里那个「父=LevelLib_Profile 画布=- 自身启用=是」的僵尸层就是这么来的。
            var cv = l.GetComponentInParent<Canvas>();
            bool want = (cv != null) && cv.isActiveAndEnabled;
            if (l.gameObject.activeSelf != want) l.gameObject.SetActive(want);
        }
    }

    public static void TickVideoBg() {
        try { EnforceSingleVideoLayer(); } catch { }
        try {
            TickVideoJob();                         // 压缩线程的交接口（弹提示 / 换文件，只在主线程做）
            if (_videoUiRefreshPending) {           // 后台压完视频，这里在主线程刷设置页开关
                _videoUiRefreshPending = false;
                try { RefreshSettingsToggles(); } catch { }
            }
            RefreshVideoPickRow();                  // 「压缩中… N%」/ 忙时禁点（主线程）
            RefreshDefaultVideoRow();               // 「默认视频背景」行的文案与进度（恢复 / 下载两个形态）
   // 阶段标记：只在采样窗口内记（平时零开销），卡顿时能看出是哪一小段
            TickPhaseMark("  ·视频-设置行");
            PruneVideoLayers();
            if (VideoBgUsable()) EnsurePanelVideoLayer();   // 面板已经开着、而视频是刚选好的，补挂一层
            TickPhaseMark("  ·视频-层管理");
   // 「代码背景让位」的窗口计时（唯一一处写它）：视频能用、有屏在看、却还没就绪就往上记，
   // 到 PanelBgWaitSeconds 为止 —— 面板进场那道闸读的就是它（到点必须放行，不能一直不显示）。
   // 就绪 / 视频不可用 / 没有屏要它 一律归零 —— 下一次开面板从干净状态重新算。
            if (!VideoBgUsable() || _bgVideoLayers.Count == 0 || VideoReadyToShow()) _videoHandoverT = 0f;
            else _videoHandoverT = Mathf.Min(PanelBgWaitSeconds,
                _videoHandoverT + Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep));
            if (!VideoBgUsable()) {                 // 开关关了 / 文件没了，彻底停 + 恢复背景图
                StopVideoBackground();
                return;
            }
            if (_bgVideoLayers.Count == 0) {        // 没有界面在显示，先暂停，别让解码器空转
                PauseVideoBackground();
                return;
            }
   // 还有一层"画布没销毁但被隐藏了"的可能，别让它继续解码，打谱时掉帧最亏
            bool anyVisible = false;
            for (int i = 0; i < _bgVideoLayers.Count; i++) {
                var l = _bgVideoLayers[i];
                if (l != null && l.gameObject.activeInHierarchy) { anyVisible = true; break; }
            }
            if (!anyVisible) { PauseVideoBackground(); return; }
            EnsureVideoPlayer();
            if (_bgVideo == null) return;
   // 从"没人看"的暂停里回来（见 PauseVideoBackground）：不能只靠下面那句 Play() 续播 ——
   // 它会停在最后一帧、只把"播放中"报成真，画面上就是一块不动的视频，而"就绪"那条判据看不出来。
   // 所以恢复一律整台重来：清掉"装的是哪个文件"，紧接着那一段就会重新 Prepare；
   // 重建那零点几秒由代码背景顶着（没就绪就显示代码背景是既有口径）。
            if (_videoPaused) {
                _videoPaused = false;
                try { _bgVideo.Stop(); } catch { }
                _bgVideoLoadedPath = null;
                // 交接迟滞的状态也要清：要重新 Prepare 了，别带着上一段的"已接管"继续判
                _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
                _videoWasReady = false;
                if (_videoReprepareLogs < 6) {
                    _videoReprepareLogs++;
                    Lg.Info("视频背景：从暂停里回来 ，所以 重新 Prepare"
                        + "（只 Play() 会停在最后一帧、只报播放中）");
                }
            }
            // 换文件期间（已停播放器、正等文件句柄释放）绝不许重载。
            // 这一段 _bgVideoLoadedPath 是被故意清空的，而"该播的文件"还指着旧的那份 ——
            // 让重载逻辑跑起来会拿旧文件重新 Prepare 一遍，两个坏处一起发生：
            // 一是播放器又把文件占住，换文件那边一直换不动（重试 20 次之后报"换不上去"）；
            // 二是它顺手把 _bgVideoLoadedPath 设回那个旧路径，而换完文件之后新旧路径本来就相同
            //（玩家产物永远落在同一个 assets\bg_video.mp4），于是"路径不同才重载"这个条件
            // 再也不成立 —— 新视频永远等不到那次重载。表现就是：导入压完不切换，
            // 玩家手动关一次再开一次视频背景才换过来。
   // 播放对象走「有效路径」：玩家自己压的优先，没有就回内置默认视频（见 VideoBgEffectivePath）
            string wantPath = VideoBgEffectivePath();
            if (_videoSwapStage == 0 && wantPath != null && _bgVideoLoadedPath != wantPath) {
                try { _bgVideo.url = new System.Uri(wantPath).AbsoluteUri; }   // 本地文件用 file:// URI
                catch { _bgVideo.url = wantPath; }
    // 量一下 `Prepare()` 本身：它跑在主线程上、也要 WMF 初始化，卡顿要能分清是不是这一带的账
                var swPrep = System.Diagnostics.Stopwatch.StartNew();
                _bgVideo.Prepare();
                long prepMs = swPrep.ElapsedMilliseconds;
                _bgVideoLoadedPath = wantPath;
                _videoPrepareAt = Time.unscaledTime;
                if (prepMs >= 30) {
                    Lg.Info($"[LevelLib] 【卡顿】视频 Prepare() 耗时 = {prepMs}ms（主线程）");
                }
                Lg.Info("视频背景：开始准备 " + wantPath
                    + (wantPath == VideoBgPath ? "" : "（内置默认视频：玩家没设过）"));
            }
            if (_bgVideo.isPrepared && !_bgVideo.isPlaying) _bgVideo.Play();
   // 播放器在某些时机（换场景 / 面板重建 / 长时间没被看）会悄悄丢掉"已准备好"，
   // 而上面那句"路径没变就不重新 Prepare"会让视频从此定格在最后一帧，所以只要"有界面在看 + 还没就绪"就重问一次。
            if (_videoRestartCd > 0f) _videoRestartCd = Mathf.Max(0f, _videoRestartCd - Time.unscaledDeltaTime);
            if (!_bgVideo.isPrepared) {
                _videoReprepareT += Time.unscaledDeltaTime;
                if (_videoReprepareT >= VideoReprepareSeconds) {
                    _videoReprepareT = 0f;
                    if (_videoReprepareLogs < 6) {
                        _videoReprepareLogs++;
                        Lg.Info("视频背景：播放器丢了就绪状态 ，所以 重新 Prepare");
                    }
                    try { _bgVideo.Prepare(); _bgVideoLoadedPath = wantPath; _videoPrepareAt = Time.unscaledTime; } catch { }
                }
            } else { _videoReprepareT = 0f; }
   // 卡死看门狗：`就绪=True 播放中=True` 但帧号不动。
   // 判据是"我们一直在出帧、视频却不动"：卡住期间数我们渲染了多少帧，够 20 帧（≈15fps 以上在跑）
   // 且卡了 ≥2.5 秒才认定是视频自己死了；游戏真卡住时我们自己也没出几帧，不会误判。
            if (_bgVideo.isPrepared && _bgVideo.texture != null) {
                long f = _bgVideo.frame;
                if (f == _videoWatchFrame) {
                    _videoStallT += Time.unscaledDeltaTime;
                    _videoStallFrames++;
                    if (_videoStallT >= 2.5f && _videoStallFrames >= 20 && _videoRestartCd <= 0f) {
                        _videoStallT = 0f;
                        _videoStallFrames = 0;
                        _videoRestartCd = 15f;                     // 冷却：别在"一坨卡顿"里反复重来
                        if (_videoStallLogs < 8) {
                            _videoStallLogs++;
                            Lg.Info($"[LevelLib] 视频背景：帧号卡住（停在第 {f} 帧已 {_videoStallT:F1}s"
                                + $" / 这期间我们渲染了 {_videoStallFrames} 帧 ，所以 不是游戏卡、是视频自己死了）"
                                + $" ，所以 强制重来一次（就绪={_bgVideo.isPrepared} 播放中={_bgVideo.isPlaying}）");
                        }
                        try { _bgVideo.Stop(); } catch { }
                        _bgVideoLoadedPath = null;   // 逼下一帧重新 Prepare
                        // 交接迟滞的状态也要清：要重新 Prepare 了，别带着上一段的"已接管"继续判
                        _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
                        _videoWasReady = false;
                    }
                } else { _videoWatchFrame = f; _videoStallT = 0f; _videoStallFrames = 0; }
            } else { _videoStallT = 0f; _videoStallFrames = 0; _videoWatchFrame = -1; }
            TickPhaseMark("  ·视频-播放器");
            var tex = _bgVideo.texture;
            bool ready = _bgVideo.isPrepared && tex != null;
   // 状态一变就打一行（只在变化时打，不刷屏）
            if (ready != _videoWasReady) {
                _videoWasReady = ready;
                Lg.Info("视频背景：" + (ready
                    ? $"就绪 纹理 {tex.width}×{tex.height} 播放中={_bgVideo.isPlaying} 层数={_bgVideoLayers.Count}"
                    : "未就绪（还在 Prepare / 或纹理没了）"));
            }
   // "一直不就绪"要能查，每 2 秒打一行状态（最多 10 行，开局那两秒就能定性）
            if (!ready) {
                _videoNotReadyT += Time.unscaledDeltaTime;
                if (_videoNotReadyT >= 2f) {
                    _videoNotReadyT = 0f;
                    if (_videoNotReadyLogs < 10) {
                        _videoNotReadyLogs++;
                        Lg.Info($"[LevelLib] 视频背景：还没就绪 ⟂ isPrepared={_bgVideo.isPrepared}"
                            + $" isPlaying={_bgVideo.isPlaying} frame={_bgVideo.frame}/{_bgVideo.frameCount}"
                            + $" 纹理={(tex != null ? tex.width + "×" + tex.height : "(空)")}"
                            + $" url={_bgVideo.url}");
                    }
                }
            } else {
                _videoNotReadyT = 0f;
    // 光数"视频播了多少帧"查不出卡顿：解码在别的线程上，游戏画面掉到 5fps 视频帧号照样一秒 +24。
    // 所以同时记游戏自己每帧花了多久：2 秒一段报平均 fps + 最差那一帧，
    // 这样能分清是游戏整体在掉帧（抢资源）还是只有视频不动（解码停）。
                _videoFrameT += Time.unscaledDeltaTime;
                _videoPerfFrames++;
                if (Time.unscaledDeltaTime > _videoPerfWorst) _videoPerfWorst = Time.unscaledDeltaTime;
                if (_videoFrameT >= 2f) {
                    float avg = _videoFrameT / Mathf.Max(1, _videoPerfFrames);
                    long now = _bgVideo.frame;
                    long d = now - _videoLastFrame;
                    _videoLastFrame = now;
                    if (_videoFrameLogs < 10) {
                        _videoFrameLogs++;
                        string scene = "?";
                        try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                        Lg.Info($"[LevelLib] 视频背景：游戏 {1f / Mathf.Max(0.0001f, avg):F1}fps"
                            + $"（最差一帧 {_videoPerfWorst * 1000f:F0}ms）｜ 视频 2 秒 {d} 帧 ｜ 场景={scene}"
                            + $" 层={_bgVideoLayers.Count} 就绪={ready} 播放中={_bgVideo.isPlaying}");
                    }
                    _videoFrameT = 0f; _videoPerfFrames = 0; _videoPerfWorst = 0f;
                }
            }
            TickPhaseMark("  ·视频-状态采样");
   // 交接判据只有一个：视频真的就绪了没有。
   // 没就绪就一直看代码背景（它本来就是我们的兜底），等久了只打一行日志说明现状。
            if (ready) _videoWaitT = 0f;
            else {
                _videoWaitT += Time.unscaledDeltaTime;
                if (_videoWaitT >= VideoColdFallbackSeconds && _videoFallbackLogs < 3) {
                    _videoFallbackLogs++;
                    Lg.Info($"[LevelLib] 视频背景：等了 {VideoColdFallbackSeconds:F0} 秒还没就绪 ，所以 "
                        + "继续显示代码背景（不空屏 就绪了它会自动淡出）");
                }
            }
   // 就绪了才让代码背景淡出；没就绪一直亮着（这就是"没闪一下"的根：不需要提前藏）
   // 交接迟滞：播放器在"重载前后"会短暂地就绪↔未就绪抖动（换源/scene 切换/卡顿自愈都会），
   // 每次抖动都让代码背景跟着收-放一次 —— 淡出是 0.55 秒的渐变，肉眼就看见两层同屏。
   // 所以"是否已接管"要求稳定一小段时间：就绪要连着 hold 才算数，丢失要连着 hold 才算真丢。
            bool nowShowing = false;
            for (int i = 0; i < _bgVideoLayers.Count; i++) {
                var ri = _bgVideoLayers[i];
                if (ri == null) continue;
                if (ri.texture != tex) ri.texture = tex;
     // 这一层现在真的在显示吗：被 park 收起来（SetActive(false)）或整块画布关掉时都不算。
     // 必须与 ready 相与 —— 只看播放器的话，被收起来的那层照样把代码背景收起，
     // 摆回来时两层一起亮就是"神经图和视频同屏"。
                bool layerOn = ri.gameObject.activeInHierarchy;
                bool showThis = ready && layerOn;
                if (showThis) nowShowing = true;
                float a = showThis ? 1f : 0f;
                var c = ri.color;
                if (Mathf.Abs(c.a - a) > 0.001f) ri.color = new Color(1f, 1f, 1f, a);
   // 不在显示的层连组件一起关掉：全屏透明 quad 照样整屏走一遍片元（填充率 × 层数），
   // 实测三块同时渲染时游戏掉到 25fps（见"视频背景：游戏 2x fps"那族日志），关掉一块就回 60。
                bool wantOn = a > 0.002f;
                if (ri.enabled != wantOn) ri.enabled = wantOn;
                var net = (i < _bgVideoHidden.Count) ? _bgVideoHidden[i] : null;
   // 代码背景的去留：一律问唯一的权威判据 CodeBgShouldYield（口径=视频开着就不画神经图）。
    // 之前这里按 `yieldNet` 分成两路（面板有 1.5 秒"让位到底"的宽限、其余屏等就绪才收），
       // 分支本身就是闪的来源之一：播放器还没就绪的那几十帧里，两路的结论不一样。
    // 现在统一成一句话，层被收起来时它自己会返回 false（那一屏显示代码背景，不会黑屏）。
     if (net != null) NetBgSetShown(net, !CodeBgShouldYield(ri), false);
            }
      // 迟滞的计时与状态切换：两个方向都要连续 hold 这么久才算数（见上面那段说明）
            if (nowShowing) {
                if (_videoShowingHoldT < 0f) _videoShowingHoldT = 0f;      // 已在稳定态：hold 计时不用
                else { _videoShowingHoldT += Time.unscaledDeltaTime; if (_videoShowingHoldT >= VideoHandoverHold) _videoShowingStable = true; }
            } else {
                if (_videoShowingStable) {
                    if (_videoLostHoldT <= 0f) _videoLostHoldT = VideoHandoverHold;
                    else { _videoLostHoldT -= Time.unscaledDeltaTime; if (_videoLostHoldT <= 0f) _videoShowingStable = false; }
                } else _videoLostHoldT = 0f;
            }
            TickPhaseMark("  ·视频-刷层");
        } catch { }
    }

 /// <summary>设置里改开关 / 选完视频之后立刻生效（不用等下一帧）</summary>
    private static void ApplyVideoBackground() {
        try {
            if (!VideoBgUsable()) { StopVideoBackground(); return; }
            TickVideoBg();
        } catch (System.Exception e) { Lg.Ex("应用视频背景失败", e); }
    }

 /// <summary>没界面在看就暂停。不 Stop：Stop 会把它变回"没准备好"，下次开界面又要等。</summary>
    private static void PauseVideoBackground() {
        try {
   // 旗子只在"真的把播放器按停过"的时候立：它决定下次有人看时是整台重建还是 Play() 续播，
   // 而"播放器本来就没在播"（第一次开界面 / 刚 Stop 过）不该走重建那一条
            if (_bgVideo != null && _bgVideo.isPlaying) { _bgVideo.Pause(); _videoPaused = true; }
            for (int i = 0; i < _bgVideoLayers.Count; i++) {
                if (_bgVideoLayers[i] != null) _bgVideoLayers[i].color = new Color(1f, 1f, 1f, 0f);
                if (i < _bgVideoHidden.Count && _bgVideoHidden[i] != null) NetBgSetShown(_bgVideoHidden[i], true, false);
            }
        } catch { }
    }

 /// <summary>关掉视频背景 / 文件换了，彻底停 + 把代码背景放回来</summary>
    private static void StopVideoBackground() {
        try {
            if (_bgVideo != null && (_bgVideo.isPlaying || _bgVideo.isPrepared)) _bgVideo.Stop();
            _bgVideoLoadedPath = null;
            _videoPaused = false;     // 已经整台停了：下一次起来走的是正常的准备那一条，不再是"从暂停里恢复"
   // 停掉之后"等待"也要复位，否则下次开视频还带着上次的计时
            _videoWaitT = 0f;
   // 交接迟滞的状态同样复位：整台都停了，"已稳定接管"不该还挂着（否则下一段开头会误判成一直在接管）
            _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
            for (int i = 0; i < _bgVideoLayers.Count; i++) {
                if (_bgVideoLayers[i] != null) _bgVideoLayers[i].color = new Color(1f, 1f, 1f, 0f);
                if (i < _bgVideoHidden.Count && _bgVideoHidden[i] != null) NetBgSetShown(_bgVideoHidden[i], true, false);
            }
        } catch { }
    }

 // ---------- 选择 + 压缩 ----------

 /// <summary>设置里点「选择视频文件」：选文件 → 挑压缩挡位 → 压到挡位允许的大小 → 验证可播放 → 落盘并立即生效</summary>
    private static void PickVideoBackground() {
        try {
            string path = ModernFileDialog.OpenFile("选择视频背景", "视频文件", "*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.webm");
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) { ShowToast(Lang.Tr("这个视频文件不存在喵")); return; }
            if (_videoBusy) { ShowToast(Lang.Tr("上一次还在压缩，等它完事喵")); return; }
            if (FindFfmpeg() == null) { ShowToast(Lang.Tr("找不到游戏自带的 ffmpeg，没法压缩喵")); return; }
            // 选好文件之后先让玩家挑挡位：四档的产物能差十倍（原画质档对一段短片可以是几百 MB），
            // 替他定死不如让他自己决定。挡位定下来才进后台压缩（压缩参数在起线程前就要定死）。
            ShowVideoQualityPick(path);
        } catch (System.Exception e) { Lg.Ex("选择视频背景失败", e); }
    }

 /// <summary>「恢复默认视频背景」：丢掉玩家自己选的那份（连文件一起删），切回内置的默认视频、并把开关打开。
 /// 视频默认关之后这一句才完整：没设过自选、开关还关着的玩家点它，就是要"用默认那份"。
 /// 真已经在用着默认那份（开关开着 + 没自选）时只告诉一声，不做多余动作。
 /// 与"一次性调整"那条规则不冲突：那条只在"从没选过"的人身上动一次开关，这里是玩家自己点的。</summary>
    private static void RestoreDefaultVideoBackground() {
        try {
            if (!File.Exists(VideoDefaultPath())) {
                ShowToast(Lang.Tr("本地还没有默认视频（用上面的「下载默认视频背景」取一份）"));
                return;
            }
      // 已经在用默认视频（开关开着 + 没设过自选）：没什么可恢复的，别去删玩家的文件
            if (VideoBgOn && (string.IsNullOrEmpty(VideoBgPath) || VideoBgPath == VideoDefaultPath())) {
                ShowToast(Lang.Tr("现在用的就是内置默认视频喵"));
                return;
            }
            string old = VideoBgPath;
            VideoBgPath = "";
            VideoBgOn = true;
      // 逼播放器丢掉"手里装的是哪个文件"：否则下一帧的"路径没变就不重载"会让它继续放旧文件
            _bgVideoLoadedPath = null;
            // 交接迟滞的状态也要清：要重新 Prepare 了，别带着上一段的"已接管"继续判
            _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
            AutoFeatures.Save();
      // 删掉玩家那份产物（它就在 mod 目录的 assets 下，不属于用户的原始素材）
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            ApplyVideoBackground();
      // 开关可能是程序打开的：设置页那颗开关当场跟上
            try { RefreshSettingsToggles(); } catch { }
            Lg.Info("视频背景：已切回内置默认视频（原文件已删="
                + (!File.Exists(old)) + "，现在播 " + (VideoBgEffectivePath() ?? "(无)") + "）");
            ShowToast(string.IsNullOrEmpty(old) || !File.Exists(old)
                ? "已切回内置默认视频"
                : "已切回内置默认视频，原先那份已删掉");
        } catch (System.Exception e) {
            Lg.Ex("恢复默认视频背景失败", e);
            ShowToast(Lang.Tr("恢复默认视频失败，看日志喵"));
        }
    }

 /// <summary>压缩挡位选择框的画布。必须是字段而不是局部变量：Esc 关闭链要按引用去关它
 /// （链里每一档都是 `EscCloseCanvas(canvas, ref canvas)`，拿不到引用的窗口按 Esc 关不掉）。</summary>
    private static Canvas _videoQualityWin;

 /// <summary>压缩挡位选择：把四档列出来，玩家点哪一档就用哪一档压。
 /// 点某一档 = 记住它 + 开始压缩；点取消 = 什么都不做（也不换视频，原来那个照旧）。
 /// 当前记着的档位在按钮上标一个「当前」，方便玩家知道上次用的是哪一档。</summary>
    private static void ShowVideoQualityPick(string path) {
        try {
            _videoQualityWin = MakeCanvas("LevelLib_VideoQuality", 83700);
            var bg = MakeDialogVeil(_videoQualityWin.transform);
            var box = MakeDialogBox(_videoQualityWin.transform, "Box", PanelColor);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(620f, 440f);

            var t = MakeTitle(box.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            t.text = Lang.Tr("选择压缩挡位");
            t.rectTransform.anchorMin = new Vector2(0f, 0.86f);
            t.rectTransform.anchorMax = new Vector2(1f, 0.98f);
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;

            var hint = MakeText(box.transform, "Hint", 20, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleCenter);
            hint.text = Lang.Tr("档位越高越清晰，也越吃解码性能与磁盘");
            hint.rectTransform.anchorMin = new Vector2(0f, 0.78f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0.87f);
            hint.rectTransform.offsetMin = Vector2.zero;
            hint.rectTransform.offsetMax = Vector2.zero;

            // 四档自上而下从"原画质"排到"低"，与参数表的顺序一致
            for (int i = 0; i < VideoQualities.Length; i++) {
                var q = VideoQualities[i];
                bool cur = (q == _videoQuality);
                float y = 120f - i * 74f;
                var btn = MakeButton(box.transform, "Q" + q.Key, new Vector2(0f, y), new Vector2(520f, 62f),
                                     cur ? Accent : ItemColor);
                var name = MakeText(btn.transform, "N", 24, Color.white, TextAnchor.MiddleLeft);
                name.text = cur ? (Lang.Tr(q.Label) + Lang.Tr("（当前）")) : Lang.Tr(q.Label);
                name.rectTransform.anchorMin = new Vector2(0f, 0f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.offsetMin = new Vector2(22f, 26f);    // 上半：名字
                name.rectTransform.offsetMax = new Vector2(-22f, 0f);
                var note = MakeText(btn.transform, "D", 17, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleLeft);
                note.text = Lang.Tr(q.Note);
                note.rectTransform.anchorMin = new Vector2(0f, 0f);
                note.rectTransform.anchorMax = new Vector2(1f, 1f);
                note.rectTransform.offsetMin = new Vector2(22f, 0f);     // 下半：参数说明
                note.rectTransform.offsetMax = new Vector2(-22f, -34f);
                var pick = q;
                btn.onClick.AddListener(() => {
                    _videoQuality = pick;
                    VideoQualityKey = pick.Key;
                    AutoFeatures.Save();    // 玩家的选择当场落盘，下次导入默认标它
                    CloseVideoQualityPick();
                    StartVideoCompress(path, pick);
                });
            }

            var cancel = MakeButton(box.transform, "Cancel", new Vector2(0f, -178f), new Vector2(180f, 44f), ItemColor);
            var cancelT = MakeText(cancel.transform, "CancelT", 22, Color.white, TextAnchor.MiddleCenter);
            cancelT.text = Lang.Tr("取消");
            Stretch(cancelT.rectTransform, Vector2.zero, Vector2.one);
            cancel.onClick.AddListener(CloseVideoQualityPick);
        } catch (System.Exception e) {
            Lg.Ex("视频压缩挡位选择失败", e);
            StartVideoCompress(path, _videoQuality);   // 对话框没建起来也别把这次导入卡死
        }
    }

 /// <summary>收掉挡位选择框。点档位、点取消、按 Esc 三条路都走它 ——
 /// 引用不清的话下一个 Esc 会去关一个已经销毁的画布（链里那句 IsAliveOnScreen 会兜住，但不该靠兜）。</summary>
    private static void CloseVideoQualityPick() {
        try {
            if (_videoQualityWin != null) Object.Destroy(_videoQualityWin.gameObject);
        } catch { }
        _videoQualityWin = null;
    }

 /// <summary>按选定的挡位开始后台压缩。写临时文件、验过了才交给主线程换上去。</summary>
    private static void StartVideoCompress(string path, VideoQuality q) {
        try {
            _videoJobQuality = q ?? VideoQualityDefault;   // 起线程前定死：后台线程只读它
            _videoBusy = true;      // 界面那行会显示「压缩中… N%」（主线程 Tick 里刷）
            _videoPercent = 0;
            ShowToast(Lang.TrFmt("正在压缩视频（{0}档，长视频要一两分钟）喵", Lang.Tr(_videoJobQuality.Label)));
            Lg.Info("视频背景：准备压缩 " + path + "（挡位=" + _videoJobQuality.Key + "）");
            Main.RunBackground("VideoBgCompress", () => {
                string outPath = VideoOutPath();
                try {
                    if (string.IsNullOrEmpty(outPath)) { VideoJobFail("拿不到 mod 目录，没法保存视频喵"); return; }
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    var q2 = _videoJobQuality;
                    int srcW = 0, srcH = 0; double srcFps = 0;
                    double sec = FfmpegProbe(path, out srcW, out srcH, out srcFps);
                    if (sec <= 0.5) { VideoJobFail("读不出这个视频的时长，换一个试试喵"); return; }
     // 目标码率 = 本档大小上限 × 8 ÷ 时长 × 0.90；余量留 10% 是因为 h264_mf 会略微超目标码率
     // （12 秒样本按 40MB 算会出来 40.34MB，刚好越过那条线）
                    long kbps = (long)System.Math.Max(200.0, (q2.MaxBytes * 8.0 / sec * 0.90) / 1000.0);
     // 再压一道码率天花板：短片子按大小上限算出来的码率高得离谱（10 秒 → 30Mbps）
                    if (kbps > q2.MaxKbps) kbps = q2.MaxKbps;
                    _videoTargetBytes = (long)(kbps * 1000.0 / 8.0 * sec);      // 算进度用
                    _videoPercent = 0;
                    int tw, th; bool sc;
                    FitTargetSize(srcW, srcH, q2, out tw, out th, out sc);
                    Lg.Info($"[LevelLib] 视频背景：源 {srcW}×{srcH} {srcFps:F0}fps 时长 {sec:F1}s"
                        + $" → 输出 {(sc ? tw + "×" + th : "原尺寸")}"
                        + (q2.MaxFps > 0.0 && srcFps > q2.MaxFps + 0.5 ? " " + (int)q2.MaxFps + "fps" : "")
                        + $" 码率 {kbps}kbps（挡位 {q2.Key}，预计产物 {_videoTargetBytes / 1024 / 1024.0:F1}MB）");
     // 压缩写临时文件：直接写目标的话，压失败时老文件还在，
     // `File.Exists(目标)` 会假装成功、还会把老文件当成新产物
                    _videoTmpPath = outPath + ".new.mp4";
                    try { if (File.Exists(_videoTmpPath)) File.Delete(_videoTmpPath); } catch { }
                    string used = null;
                    for (int e = 0; e < 2; e++) {
                        // 先试 libopenh264（纯软件、输出最标准）：h264_mf 走的是各机器的显卡编码器，
                        // NVIDIA/AMD/Intel 出的流各有脾气——玩家反馈"导入视频后花屏"集中在它身上。
                        // 它只在源尺寸超出 libopenh264 的 width×height ≤ 9437184 上限时才接手（原画质档不缩放的那种）。
                        string encName = (e == 0) ? "libopenh264" : "h264_mf";
                        string args = BuildEncodeArgs(path, _videoTmpPath, kbps, e, srcW, srcH, srcFps, q2);
                        Lg.Info("视频背景：试编码器 " + encName);
                        string log = RunFfmpeg(args);
                        long size = 0;
                        try { if (File.Exists(_videoTmpPath)) size = new FileInfo(_videoTmpPath).Length; } catch { }
                        if (size <= 0) {
                            Lg.Info($"[LevelLib] 视频背景：{encName} 没出片 尾部日志: " + Tail(log, 400));
                            continue;
                        }
                        if (!VerifyPlayable(_videoTmpPath)) {
                            Lg.Info($"[LevelLib] 视频背景：{encName} 压出来的解不开 尾部日志: " + Tail(log, 400));
                            continue;
                        }
                        used = encName;
                        Lg.Info($"[LevelLib] 视频背景：{encName} 成功 产物 {size / 1024 / 1024.0:F1}MB");
                        break;
                    }
                    if (used == null) {
                        VideoJobFail("视频压缩失败喵（日志里有 ffmpeg 的报错）");
                        return;
                    }
     // 验过了就交给主线程换上去：这一步不能在后台做（播放器正开着这个文件，见交接口那段说明）
                    long tmpSize = new FileInfo(_videoTmpPath).Length;
                    Lg.Info($"[LevelLib] 视频背景：压缩完成 {Path.GetFileName(path)} 时长 {sec:F1}s"
                        + $" → 挡位 {q2.Key} → 码率 {kbps}kbps → {used} → 临时产物 {tmpSize / 1024 / 1024.0:F1}MB 可解码"
                        + "（换文件交给主线程）");
                    VideoJobOk(_videoTmpPath, $"视频背景已就绪（{q2.Label}档 · {tmpSize / 1024 / 1024.0:F1}MB）喵");
                }
    // 游戏退出时后台线程会被中止，这里静默吞掉：退出过程中 logger 可能已经收摊，
    // 在 catch 里再抛一个就是致命的
                catch (System.Threading.ThreadAbortException) { try { System.Threading.Thread.ResetAbort(); } catch { } }
                catch (System.Exception e) {
                    Lg.Ex("视频背景处理失败", e);
                    VideoJobFail("视频处理出错了喵（见日志）");
                } finally {
     // 临时文件不在这里清：成功时它等着主线程换上去，失败时由主线程 TickVideoJob 清。
     // 没跑到这里的（游戏被关掉）会留一个 `.new.mp4`，下次选视频开头会删掉它。
     // 打包时它也在排除名单里，绝不能把一位玩家的视频发给所有人。
                    _videoBusy = false;
                    _videoPercent = 0;
                }
            });
        } catch (System.Exception e) { Lg.Ex("启动视频压缩失败", e); }
    }

 /// <summary>压缩线程用：记一句给玩家的提示。绝不在这里碰 UI（主线程 TickVideoJob 里弹）。</summary>
    private static void VideoJobFail(string msg) {
        _videoJobMsg = msg;
        _videoJobArg = null;
        _videoJobOk = false;
        _videoJobDone = true;
    }

 /// <summary>压缩线程用：临时文件已就绪，等主线程停播放器再换上去。</summary>
    private static void VideoJobOk(string tmp, string msg) {
        _videoJobMsg = msg;
        _videoJobArg = tmp;
        _videoJobOk = true;
        _videoJobDone = true;
    }

 /// <summary>压缩线程的交接口（主线程、每帧）：弹提示、落盘、换文件。
 /// 换文件必须分两步：① 先停播放器 —— 它手里正开着要替换的那个文件，后台直接 Delete+Move 会让
 /// 解码器（WMF）读到半截数据、native 崩游戏；② 隔两帧等句柄真的关掉再 Delete+Move，
 /// 失败就每 0.25 秒重试（杀毒 / 资源管理器预览都会短时间占住文件），最多试 20 次。</summary>
    private static void TickVideoJob() {
        if (_videoJobDone) {
            _videoJobDone = false;
            bool ok = _videoJobOk; _videoJobOk = false;
            string tmp = _videoJobArg; _videoJobArg = null;
            string msg = _videoJobMsg; _videoJobMsg = null;
            if (!ok) {
    // 失败不再关开关：现在有内置默认视频兜底，「开着」永远有东西可放 ——
    // 这次失败的只是"换一份新的"，继续用原来能用的那份（玩家的旧视频或默认视频）。
                _videoUiRefreshPending = true;
                try { if (!string.IsNullOrEmpty(tmp) && File.Exists(tmp)) File.Delete(tmp); } catch { }
                if (!string.IsNullOrEmpty(msg)) ShowToast(msg);
            } else {
                _videoSwapTmp = tmp;
                _videoSwapMsg = msg;
                _videoSwapTries = 0;
                _videoSwapFrames = 0;
                _videoSwapStage = 1;
                try { if (_bgVideo != null) { _bgVideo.Stop(); _bgVideo.url = ""; } } catch { }
                _bgVideoLoadedPath = null;
                // 交接迟滞的状态也要清：要重新 Prepare 了，别带着上一段的"已接管"继续判
                _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
                _videoPaused = false;      // 为了换文件而停的：换完按新文件重新准备，不是"从暂停里恢复"
                Lg.Info("视频背景：已停播放器、放开文件句柄（隔两帧再换文件）");
            }
        }
        if (_videoSwapStage != 1) return;
        _videoSwapFrames++;
        if (_videoSwapFrames < 2) return;
        if ((_videoSwapFrames - 2) % 15 != 0) return;      // 每 15 帧（约 0.25 秒）试一次
        _videoSwapTries++;
        string outPath = VideoOutPath();
        if (string.IsNullOrEmpty(outPath)) { VideoSwapFinish(false, "拿不到 mod 目录，没法保存视频喵"); return; }
        try {
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(_videoSwapTmp, outPath);
            VideoSwapFinish(true, null);
        } catch (System.Exception e) {
            if (_videoSwapTries < 20) return;              // 文件被短暂占用：下一轮再试
            Lg.Ex("视频背景：换文件失败（已重试 20 次）", e);
            VideoSwapFinish(false, "视频换不上去喵（文件被占用，重启游戏再试一次）");
        }
    }

 /// <summary>换文件收尾：成功就把路径切过去并存盘，失败就清掉临时文件（开关不动：有默认视频兜底）。</summary>
    private static void VideoSwapFinish(bool ok, string failMsg) {
        string tmp = _videoSwapTmp;
        _videoSwapTmp = null; _videoSwapStage = 0; _videoSwapFrames = 0; _videoSwapTries = 0;
        string msg = _videoSwapMsg; _videoSwapMsg = null;
        if (!ok) {
            try { if (!string.IsNullOrEmpty(tmp) && File.Exists(tmp)) File.Delete(tmp); } catch { }
    // 同 TickVideoJob：失败不关开关（有默认视频兜底，失败的只是"换"不是"没有"）
            _videoUiRefreshPending = true;
            if (!string.IsNullOrEmpty(failMsg)) ShowToast(failMsg);
            return;
        }
        string outPath = VideoOutPath();
        long size = 0;
        try { size = new FileInfo(outPath).Length; } catch { }
        VideoBgPath = outPath;
        VideoBgOn = true;
        // 换完再清一次"装的是哪个文件"：产物路径新旧相同，不清就分不出"播放器里那份还是旧的"。
        // 上面那一段换文件期间已经禁止重载了，这里是双保险 —— 下一次 TickVideoBg 一定会重新 Prepare。
        _bgVideoLoadedPath = null;
            // 交接迟滞的状态也要清：要重新 Prepare 了，别带着上一段的"已接管"继续判
            _videoShowingStable = false; _videoShowingHoldT = 0f; _videoLostHoldT = 0f;
        AutoFeatures.Save();
        _videoUiRefreshPending = true;
        Lg.Info($"[LevelLib] 视频背景已就绪：{outPath} 共 {size / 1024 / 1024.0:F1}MB"
            + "（播放器已停，下一帧按新文件重新 Prepare）");
        ShowToast(string.IsNullOrEmpty(msg) ? "视频背景已就绪喵" : msg);
    }

 /// <summary>编码参数（编码器按顺序试，见类注释里那份可用性结论）。
 /// 两个编码器都先缩到 ≤1920×1080：4K 纹理每帧都要传给 GPU（一张 33MB），集成显卡直接跪
 /// —— 实测 3840×2160 / 50fps 的片子会"播放很不稳定，一直在颤抖"；缩到 1080p 是 1/4 的像素量，
 /// 同一个 40MB 预算下画质反而更好，而且天然满足 libopenh264 的 width×height ≤ 9437184。</summary>
    private static string BuildEncodeArgs(string inPath, string outPath, long kbps, int e, int srcW, int srcH, double srcFps, VideoQuality q) {
        int tw, th; bool needScale;
        FitTargetSize(srcW, srcH, q, out tw, out th, out needScale);
        string vf = needScale ? (" -vf scale=" + tw + ":" + th) : "";
  // 帧率也压一道（源片高于这一档的上限才加；上限为 0 的档不动帧率）
        string rf = (q != null && q.MaxFps > 0.0 && srcFps > q.MaxFps + 0.5) ? (" -r " + ((int)q.MaxFps)) : "";
        string head = "-y -i " + Q(inPath) + vf + rf;
        if (e == 0) {
            return head + " -c:v libopenh264 -b:v " + kbps + "k -pix_fmt yuv420p -an -movflags +faststart " + Q(outPath);
        }
        return head + " -c:v h264_mf -b:v " + kbps + "k -pix_fmt yuv420p -an -movflags +faststart " + Q(outPath);
    }

 /// <summary>目标分辨率：只缩不放 —— 等比缩进挡位给定的上限（已经更小的保持原样）。
 /// 原画质档的上限是 0，表示不缩放，直接返回源尺寸。
 /// 尺寸在 C# 里算好、不用 ffmpeg 的表达式（命令行里的引号/逗号很容易被它自己的解析器吃掉）。</summary>
    private static void FitTargetSize(int srcW, int srcH, VideoQuality q, out int w, out int h, out bool needScale) {
        w = srcW; h = srcH; needScale = false;
        if (srcW <= 0 || srcH <= 0) return;
        if (q == null || q.MaxW <= 0 || q.MaxH <= 0) return;    // 原画质档：不缩
        double k = System.Math.Min((double)q.MaxW / srcW, (double)q.MaxH / srcH);
        if (k >= 1.0) {
            // 已经比目标尺寸小，不缩放；但奇数尺寸（裁过的片段很常见）必须取偶，
            // 取偶之后尺寸变了就要真的挂 scale，光改数不挂滤镜等于没改。
            EvenOut(ref w, ref h);
            if (w != srcW || h != srcH) needScale = true;
            return;
        }
        w = ((int)(srcW * k)) & ~1;                 // 偶数（H.264 要求）
        h = ((int)(srcH * k)) & ~1;
        EvenOut(ref w, ref h);
        if (w < 2) w = 2;
        if (h < 2) h = 2;
        needScale = true;
    }

    /// <summary>H.264 4:2:0 的宽高必须都是偶数：奇数尺寸源（裁过的片段很常见）硬编出来的流
    /// 有的解码器能容错、有的（Media Foundation）直接解出花屏。只缩不放的"原画质档"也要取偶。</summary>
    private static void EvenOut(ref int w, ref int h) {
        if ((w & 1) != 0) w -= 1;
        if ((h & 1) != 0) h -= 1;
        if (w < 2) w = 2;
        if (h < 2) h = 2;
    }

 /// <summary>命令行参数里的路径要加引号（有空格的路径不加就废）</summary>
    private static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "") + "\""; }

 /// <summary>日志只留最后一段（ffmpeg 的报错在末尾，别把整屏刷满）</summary>
    private static string Tail(string s, int n) {
        if (string.IsNullOrEmpty(s)) return "(空)";
        s = s.Replace("\r", "").Replace("\n", " | ");
        return s.Length <= n ? s : ("…" + s.Substring(s.Length - n));
    }

 /// <summary>找游戏自带的 ffmpeg（`<游戏目录>\ffmpeg\ffmpeg.exe`，玩家机器上一定有）</summary>
    private static string FindFfmpeg() {
        try {
            string gameDir = Directory.GetParent(Application.dataPath)?.FullName;   // ...\A Dance of Fire and Ice\
            if (string.IsNullOrEmpty(gameDir)) return null;
            string p = Path.Combine(gameDir, "ffmpeg", "ffmpeg.exe");
            return File.Exists(p) ? p : null;
        } catch { return null; }
    }

 /// <summary>跑一次 ffmpeg（stderr 全收回来 —— 它什么都往 stderr 写）。
 /// 长视频要跑几分钟，每 15 秒打一行"还在压 + 已写出多少 MB"；上限 10 分钟，到点强杀。
 /// stderr 必须用独立线程读：管道缓冲区只有 4KB 左右，不读会互相等死。
 ///（也别用 Task.Run：项目规定后台活一律独立线程，别跟 Together 抢线程池。）</summary>
    private static string RunFfmpeg(string args) {
        try {
            string exe = FindFfmpeg();
            if (exe == null) return "(找不到游戏自带的 ffmpeg)";
            var psi = new ProcessStartInfo(exe, args) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var p = Process.Start(psi)) {
                string err = null;
                var reader = new System.Threading.Thread(() => {
                    try { err = p.StandardError.ReadToEnd(); } catch { }
                });
                reader.IsBackground = true;
                reader.Name = "LevelLib_ffmpegErr";
                reader.Start();
                int waited = 0;
                while (!p.WaitForExit(5000)) {
                    waited += 5;
                    long now = 0;
                    try { if (!string.IsNullOrEmpty(_videoTmpPath) && File.Exists(_videoTmpPath)) now = new FileInfo(_videoTmpPath).Length; } catch { }
     // 进度：临时文件大小 ÷ 目标大小（不去解析 ffmpeg 输出，它默认不吐进度）
                    if (_videoTargetBytes > 0 && now > 0) {
                        int pct = (int)System.Math.Min(99L, now * 100L / _videoTargetBytes);
                        if (pct > _videoPercent) _videoPercent = pct;
                    }
                    if (waited % 15 == 0) {
                        Lg.Info("视频背景：压缩中… " + waited + "s"
                            + (now > 0 ? $"，已写出 {now / 1024 / 1024.0:F1}MB（{_videoPercent}%）" : ""));
                    }
                    if (waited >= VideoFfmpegMaxSeconds) {
                        Lg.Info("视频背景：压了 " + VideoFfmpegMaxSeconds + " 秒还没完 ，所以 掐掉（换个短一点的视频试试）");
                        try { p.Kill(); } catch { }
                        break;
                    }
                }
                try { reader.Join(3000); } catch { }
                return err ?? "";
            }
        } catch (System.Exception e) { return "(ffmpeg 调用异常: " + e.GetType().Name + ")"; }
    }

 /// <summary>从 ffmpeg 的 banner 里读时长 + 分辨率 + 帧率（`Duration: 00:00:12.34` / `5082x2160` / `50 fps`）。
 /// 没有 ffprobe 也能拿到 —— 那份 ffmpeg 是 `--disable-ffprobe` 编的。</summary>
    private static double FfmpegProbe(string path, out int w, out int h, out double fps) {
        w = 0; h = 0; fps = 0;
        try {
            string outText = RunFfmpeg("-i " + Q(path));
            var mres = System.Text.RegularExpressions.Regex.Match(outText, @"(\d{2,5})x(\d{2,5})");
            if (mres.Success) {
                int.TryParse(mres.Groups[1].Value, out w);
                int.TryParse(mres.Groups[2].Value, out h);
            }
            var mfps = System.Text.RegularExpressions.Regex.Match(outText, @"([\d.]+)\s*fps");
            if (mfps.Success) {
                double.TryParse(mfps.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out fps);
            }
            int i = outText.IndexOf("Duration:", System.StringComparison.Ordinal);
            if (i < 0) return 0;
            string seg = outText.Substring(i + 9, System.Math.Min(13, outText.Length - i - 9)).Trim().TrimEnd(',');
            var parts = seg.Split(':');
            if (parts.Length < 3) return 0;
            double hh, mm, ss;
            if (!double.TryParse(parts[0], out hh) || !double.TryParse(parts[1], out mm)
                || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ss)) return 0;
            return hh * 3600 + mm * 60 + ss;
        } catch { return 0; }
    }

 /// <summary>确认视频可以播放：让 ffmpeg 完整解一遍，`-v error` 什么都不打 = 解得出。
 /// 只看"文件存在"是不够的，半截文件照样存在。</summary>
    private static bool VerifyPlayable(string path) {
        try {
            string err = RunFfmpeg("-v error -i " + Q(path) + " -f null -");
            if (err.IndexOf("找不到游戏自带的 ffmpeg", System.StringComparison.Ordinal) >= 0) return true;   // 没法验 → 放行
            return string.IsNullOrEmpty(err.Trim());
        } catch { return false; }
    }
}
