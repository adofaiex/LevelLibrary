// [split-of] Updater.cs lines 1-673 body 14-637

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using UnityEngine;

namespace LevelLibrary;

/// <summary>Updater.cs 的一部分：版本检查 / 多线路重试 / 下载与后台安装。</summary>
public static partial class Updater {
    // ================= 内置配置（用户不可见）=================

    /// <summary>API Key：内置，所有用户共用（作者要求）。DLL 是公开的，所以它事实上是公开的；
    /// 一旦平台侧撤销，这里需要同步更换。</summary>
    private const string BuiltInApiKey = "adof_sk_8BaBGgEdKI7C6-EOj3HtwDH1UxVr4wDCSd6tMPy0LsE";

    /// <summary>多条 API 线路（key）：一条不通就换下一条，全部都不通就停止本次检查。
    /// 平台按 key 限流（每 IP 每分钟 120 次，超了 429），单条 key 被限或撤销会让所有人的自动更新一起哑。
    /// 顺序就是数组顺序，用第一条通的、通了之后一直用它（_keyIndex 只前进不后退），
    /// 日志只打印"第几条线路"、从不打印 key 本身；最后一条是内置老 key 兜底。</summary>
    private static readonly string[] ApiKeys = {
        "adof_sk_yBGjLt6gIqNY3fhu9iQnqlEFphtesr2Qy_UBFf_2J0U",
        "adof_sk_VzGmtRe24WGTQnLBg5l6twlyNGKUpc1HVe9a2j2QH88",
        "adof_sk_JFXr9F-I5nj4YOhCy8Nceu1pu9MLt76EkecxlgGt1uQ",
        "adof_sk_jJtge__dSMmI8MdXums1tjo-JlMGRRk6KwraP4D4ZbQ",
        "adof_sk_5QJ3lWIxTL3BCFYWnvKjjmt5pmKjdBNdjA17v2p9Mgs",
        "adof_sk_vdL2Mj1eSPjWIiHn4sM3MtlllGSumSzEdyzwoWUW31o",
        "adof_sk_EayAcBbGWIv6PiFqA9nKFnFh8604K4BCvDJ_yajCwP0",
        "adof_sk_ThB47CHPusqPSDoyT6U3Gv1KuCY7GCg8oMvcXU4jojs",
        "adof_sk_2_0hD11GJP1R--nWDduiBz5UC2QGDElGz-7otQPmaew",
        BuiltInApiKey,
    };
    /// <summary>现在用第几条线路（0 起，只在失败时 +1、不后退）</summary>
    private static int _keyIndex;
    /// <summary>本次检查里把所有线路都试过了 → 直接收摊，不再走后面的重试</summary>
    private static bool _allLinesFailed;
    /// <summary>记下"刚才那次带 key 的请求"：换线路时要原样重发它（只有内容型请求带 key，预签名直链不带）</summary>
    private static string _lastKeyedUrl = "", _lastKeyedMethod = "GET";
    private static int _lastKeyedKind;

    /// <summary>对外只暴露 key 本身，界面和日志都不打印它</summary>
    private static string Key { get { return ApiKeys[Mathf.Clamp(_keyIndex, 0, ApiKeys.Length - 1)]; } }

    private const string BaseUrl = "https://www.adofaitools.top";
    private const float HttpTimeout = 20f;
    private const string ResourceType = "MOD";
    private const string Slug = "mod-levellibrary";
    /// <summary>暂存目录名（mod 目录下）</summary>
    private const string StageDirName = "_update";
    /// <summary>待替换的 DLL 暂存名。正常路径用不到 —— 游戏运行时不锁 DLL，更新是当场直接覆盖的；
    /// 只有覆盖和"改名让位"都失败（别的进程抓着文件）才留这个 .new，等下次启动换上。</summary>
    private const string PendingDllName = "LevelLibrary.dll.new";

    // ================= 资源包（assets 单独一条更新线）=================

    /// <summary>平台上只有一条版本列表，mod 包和资源包混在一起，靠版本号的形状区分：
    /// mod 包是多段式（带 '.'），资源包是纯四位数字，判定就是"整串恰好 4 个字符且全是数字"。
    /// 必须把资源包从 mod 线里排除：资源包版本只有 1 段，而 mod 线是 5 段或 4 段，
    /// 不排除的话 PickBestVersion 的"段数偏好"会让它参与比较，甚至可能把一个 0001 挑成要装的 mod 版本。</summary>
    private const int AssetVersionDigits = 4;

    /// <summary>本地"已装资源包版本"的记录文件（mod 目录下）。
    /// 文件不存在 = 从没装过资源包 → 新玩家首次进来就会去下最新资源包。</summary>
    private const string AssetVerFileName = "assets_ver.txt";



    /// <summary>更新包内部的根目录名。解压时只有第一层恰好是它才会被剥掉，
    /// 这样"直接把 assets 当根打"的资源包也不会被解错位置。</summary>
    private const string PackageRootName = "LevelLibrary";

    // ================= 设置 =================

    /// <summary>自动检查更新（进游戏后延迟几秒查一次）。强制开启、不给开关 ——
    /// 设置页那一行已删、settings.txt 里的 UpdAutoCheck 也不再读：玩家关掉之后既不检查、
    /// 开始界面的更新闸门也不拦，出问题无从排查。这个字段留着只是让"关闭"那条分支仍在代码里可读。</summary>
    public static bool AutoCheck = true;
    /// <summary>上次见到的平台版本 id（同一个版本不重复提示）</summary>
    public static string LastSeenVersion = "";

    // ================= 状态（界面显示用）=================

    public static string Status = "还没检查过";
    /// <summary>平台最新版本号（用于显示）</summary>
    public static string NewVersion = "";
    public static string NewChangelog = "";
    /// <summary>更新说明按版本叠起来，最多最近几个版本，形如"5.2.0.1314.0017：xxx"。
    /// 只收同一条线（3.3.1 线只显示 .1314.xxxx，3.4 线只显示 .xxxx）的版本。</summary>
    public static string ChangelogHistory = "";
    /// <summary>更新说明最多显示几个版本</summary>
    private const int MaxChangelogVersions = 15;
    /// <summary>更新说明整块的字符上限（界面高度有限，超了保留最新的几条）</summary>
    private const int MaxChangelogChars = 1800;
    /// <summary>单条日志的字符上限：某条写太长会把后面那几条挤掉，所以逐条截断</summary>
    private const int MaxChangelogPerEntry = 180;
    /// <summary>这次会话平台详情到底拿到过没有（拿到过才有 ChangelogHistory，哪怕内容是"（无说明）"）。
    /// 启动页那一栏要能区分"还在拉"和"平台真的没写说明"。</summary>
    public static bool DetailLoaded = false;
    /// <summary>上一次从平台拿到的更新说明（落盘缓存）：启动页第一眼就能显示上一次的公告，不用等这次请求回来。
    /// 这正好治"最新版时拿不到公告"——平台数据永远比欢迎页慢（欢迎页只活几秒）。</summary>
    public static string CachedChangelog = "";
    private const string ChangelogCacheFileName = "changelog_cache.txt";
    private static bool _cacheLoaded;
    public static string LocalVersion = "";
    /// <summary>0=空闲 1=检查版本 2=下载中 3=解压安装中 4=已完成 5=失败</summary>
    public static int Phase = 0;
    /// <summary>本局真的往盘上装过东西（新 DLL 与素材都是开机时加载的，装上就必须重启才算数）。
    /// Phase 会被后面的检查改回 0，这个标记不会；重启之后它自然归零，所以不需要落盘。</summary>
    public static bool InstalledThisSession;
    /// <summary>下载进度 0~1</summary>
    public static float Progress = 0f;

    /// <summary>现在到底是在"下载字节"还是"还没开始下"（Phase=2 但直链还没拿到）。
    /// 这两段在界面上必须分开说：取直链那一段是等服务器（TLS 握手偶发要二十几秒，
    /// 一个字节都没下），说成"正在下载"会让玩家盯着不动的进度条以为卡死。</summary>
    public static bool FetchingUrl { get { return Phase == 2 && string.IsNullOrEmpty(_pendingDownloadUrl); } }

    public static bool Busy { get { return _req != null; } }

    // ================= 运行时 =================

    private static UnityEngine.Networking.UnityWebRequest _req;
    private static bool _manual;
    private static float _autoCheckAt = -1f;
    private static bool _autoChecked;
    private static string _pendingDownloadUrl = "";
    private static string _pendingFileName = "";
    /// <summary>本次要下载的文件 id（download-intent 失败重试时要用它重新申请直链）</summary>
    private static string _pendingFileId = "";

    // ---- 两条更新线的排队（mod 包 + 资源包）----
    /// <summary>一次检查可能同时要装两样东西（新 DLL + 新资源包），而更新器同一时刻只能跑一个请求 /
    /// 一次安装，所以排成队列一个一个来。两条线共用同一个"取直链 → 下载 → 后台安装"流程，
    /// 只是装的时候规则不同（mod 包素材默认跳过；资源包素材就是要装的东西，照常写盘）。</summary>
    private sealed class UpdateJob {
        public bool IsAsset;         // true=资源包（assets）；false=mod 包（DLL + Info.json）
        public bool VideoOnly;       // true=只要那份默认视频背景：解出视频一个文件，不装整个包、不动版本记录
        public string FileId;        // 平台的文件 id
        public string VersionId;     // 平台版本号（写日志、写资源包版本记录用）
    }
    private static readonly List<UpdateJob> _jobs = new List<UpdateJob>();
    /// <summary>正在下载/安装的那一条（null = 队列空）</summary>
    private static UpdateJob _curJob;
    /// <summary>本次检查里每条线的结果，最后拼成一句话给玩家看</summary>
    private static readonly List<string> _runNotes = new List<string>();
    /// <summary>本轮出现过"文件没换上（改名让位也没成功）"／"有文件替换失败"</summary>
    private static bool _runDeferred;
    private static bool _runFailed;
    /// <summary>本次要装的资源包版本（界面/日志用）</summary>
    public static string NewAssetVersion = "";
    /// <summary>本轮真的装上去了的线（给结束语挑措辞用）</summary>
    private static bool _runModDone, _runAssetDone;

    // ---- 本地资源包版本（界面显示用）----
    private static bool _assetVerLoaded;
    private static string _localAssetVerCache = "";

    /// <summary>本地已装的资源包版本（界面显示用）。空串 = 没装过（会显示"未安装"）。
    /// 只在第一次读一次盘，之后走缓存 —— 更新页是每帧刷新的，不能每帧去扫目录读文件；
    /// 装完资源包时 WriteLocalAssetVersion 会立刻刷新缓存。</summary>
    public static string LocalAssetVersion {
        get {
            if (!_assetVerLoaded) RefreshLocalAssetVersion();
            return _localAssetVerCache;
        }
    }

    /// <summary>重新从盘上读一次"本地已装资源包版本"（检查更新前调一次就够）</summary>
    public static void RefreshLocalAssetVersion() {
        try {
            _localAssetVerCache = ReadLocalAssetVersion();
            _assetVerLoaded = true;
        } catch { _localAssetVerCache = ""; _assetVerLoaded = true; }
    }

    /// <summary>给界面用的一行文字：没装过时写"未安装"，省得界面自己判断空串</summary>
    public static string LocalAssetVersionText {
        get { string v = LocalAssetVersion; return string.IsNullOrEmpty(v) ? "未安装" : v; }
    }

    /// <summary>当前正在下载/安装的那条是不是资源包（更新页据此换措辞）</summary>
    public static bool CurrentJobIsAsset { get { return IsCurrentJobAsset(); } }
    /// <summary>后台正在装的那一份是不是资源包（决定素材是"照常写盘"还是"默认跳过"）</summary>
    private static volatile bool _installIsAsset;
    /// <summary>当前"取直链 / 下载"阶段已重试次数</summary>
    private static int retryCount;
    /// <summary>单个阶段最多自动重试几次</summary>
    private const int MaxRetry = 3;
    /// <summary>本次请求发出时刻（用于重试等待时显示已等了多少秒）</summary>
    private static float _reqT0;
    /// <summary>上次记录过的"版本比较"组合，避免同一组版本每帧刷日志</summary>
    private static string _lastCmpKey = "";

    // ---- 后台安装（更新那一下不能卡主线程）----
    // 整段"写zip→解压→逐个比内容→删暂存"以前在 TickRequest 里同步跑，实测卡了 4.6 秒；
    // 其中最贵的是逐个文件读目标盘做内容比对（比数据量本身贵得多）。现在整段丢到后台线程，
    // 主线程只负责收尾 + 提示。下面的 _recvFrameGap / _decodeFrameGap / _installStartFrameGap
    // 是"两个关键时刻之间过了多久"（跨了多少帧的总时长），不是单帧卡顿 —— 量单帧卡顿的是 _installWorstFrame。
    private static volatile bool _installing;
    private static volatile bool _installDone;
    private static volatile bool _installDllApplied;
    private static volatile bool _installDllDeferred;
    private static volatile int _installFailed;
    private static volatile int _installReplaced;
    private static volatile int _installSkipped;
    private static volatile float _installProgress;
    private static volatile bool _installError;
    /// <summary>后台安装起止时刻（Stopwatch 刻度），用来在日志里实测"是否真的没卡"</summary>
    private static long _installT0;
    private static long _installT1;
    /// <summary>主线程看到的"帧间隔最大的那一帧"（只在安装期间统计，单位秒）</summary>
    private static float _installWorstFrame;
    /// <summary>那一帧出现在"起后台线程之后"的第几毫秒 —— 用来判断卡的是安装期还是下载/解码期</summary>
    private static float _installWorstFrameAt;
    private static float _installLastFrameAt = -1f;
    /// <summary>分段计时（秒）：请求到手 / 解码完 / 起后台线程时，用来定位剩余卡顿到底在哪一步</summary>
    private static float _tRecv;
    private static float _tDecode;
    private static float _tInstall0;
    /// <summary>发请求那一刻的帧时刻（配合下面的 *_FrameGap 量各阶段耗时）</summary>
    private static float _tReqSent;
    /// <summary>下面这三个不是"帧间隔"，是"两个关键时刻之间过了多久"（秒）：起止都取 Time.unscaledTime，
    /// 所以它们等于跨了多少帧的总时长（异步请求可能横跨好几秒），跟"某一帧卡了多久"完全是两回事。
    /// 量单帧卡顿的是 _installWorstFrame。</summary>
    private static float _recvFrameGap;
    private static float _decodeFrameGap;
    private static float _installStartFrameGap;
    /// <summary>预热网络连接（TLS/DNS）用的请求，见 WarmUp</summary>
    private static bool _warmDone;
    /// <summary>最近一次响应体的 Content-Encoding（判断平台有没有 gzip，决定解码这步贵不贵）</summary>
    private static string _lastContentEncoding = "(无)";
    private static byte[] _installData;
    private static readonly List<string> _installMsgs = new List<string>();

    /// <summary>后台线程攒下来的日志，回主线程再吐给 UMM（免得线程安全问题）</summary>
    private static void InstallLog(string msg) {
        lock (_installMsgs) {
            if (_installMsgs.Count < 200) _installMsgs.Add(msg);
        }
    }

    /// <summary>读本地版本号（Info.json 的 Version，由打包脚本写入）</summary>
    public static string ReadLocalVersion() {
        if (!string.IsNullOrEmpty(LocalVersion)) return LocalVersion;
        try {
            string p = Path.Combine(Main.ModEntry.Path, "Info.json");
            if (File.Exists(p)) {
                var o = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(p));
                object v;
                if (o != null && o.TryGetValue("Version", out v) && v != null) LocalVersion = v.ToString();
            }
        } catch (Exception e) { Lg.Ex("读取本地版本失败", e); }
        if (string.IsNullOrEmpty(LocalVersion)) LocalVersion = "?";
        return LocalVersion;
    }

    /// <summary>启动时调用（在 UMM 加载 mod 之后）：只处理兜底情况 —— 上一轮更新因为文件真被占用而没换上、
    /// 留下的那个 .new，现在趁刚启动换上。正常路径下这里什么都不做（DLL 更新时就已经直接覆盖写盘了）。</summary>
    public static void ApplyPendingUpdate() {
        try {
            string modDir = Main.ModEntry.Path;
            string pending = Path.Combine(modDir, PendingDllName);
            string target = Path.Combine(modDir, "LevelLibrary.dll");
            if (!File.Exists(pending)) return;
            try {
                File.Copy(pending, target, true);
                File.Delete(pending);
                LocalVersion = "";                       // 版本号可能变了，重新读
                Lg.Info("更新：待装 DLL 已替换成功（下次进游戏就是新版本）");
            } catch (Exception e) {
                Lg.Ex("更新：替换待装 DLL 失败（下次启动再试）", e);
            }
        } catch (Exception e) { Lg.Ex("ApplyPendingUpdate 失败", e); }
    }

    /// <summary>开局自动查一次更新的延迟（秒）。不能更长：启动页一打开就会调 HurryCheck(1f) 把它提前，
    /// 延迟太长会让检查请求出现在「启动页已显示」之后，看起来就像"只有开界面才更新"。
    /// 网络请求本身是异步的、不占帧，预热请求是开局就发的、跟这个不冲突。</summary>
    private const float AutoCheckDelay = 3f;

    /// <summary>每帧驱动（检查时机 + 网络请求 + 后台解压）</summary>
    public static void Tick() {
        if (!AutoCheck || _autoChecked) return;
        if (_installing) return;                                                     // 后台安装中，别再插一脚
        if (_autoCheckAt < 0f) { _autoCheckAt = Time.unscaledTime + AutoCheckDelay; return; }   // 进游戏 3 秒后自己查
        if (Time.unscaledTime < _autoCheckAt) return;
        // 有请求在飞的时候不能先把 _autoChecked 置上：CheckNow 开头遇到 _req != null（多半是开局那次
        // 预热请求还没回来）会直接 return，那样本次会话就永远不会再自动检查，更新说明也就永远是空的。
        // 必须"确认这帧真的发了请求（或者本来就查过了）"才置位。
        if (_req != null) return;                                                    // 预热/上一个请求还没完 → 下一帧再来
        _autoChecked = true;
        CheckNow(false);
    }

    /// <summary>把"开局自动查一次"提前到 delay 秒之后（启动页要看更新公告时调用）。
    /// 只在"还没查过、也没有请求在飞"时生效；已经查过就什么都不做，别把已有结果冲掉。</summary>
    public static void HurryCheck(float delay = 1f) {
        try {
            if (_autoChecked || _installing || _req != null) return;
            float want = Time.unscaledTime + Mathf.Max(0.2f, delay);
            if (_autoCheckAt < 0f || _autoCheckAt > want) _autoCheckAt = want;
        } catch { }
    }

    /// <summary>读"上次拿到的更新说明"缓存（只读一次盘，之后走内存）。
    /// 文件不存在/读失败一律返回空串，绝不让界面因此出错。</summary>
    public static string LoadCachedChangelog() {
        if (_cacheLoaded) return CachedChangelog;
        _cacheLoaded = true;
        try {
            string p = Path.Combine(Main.ModEntry.Path, ChangelogCacheFileName);
            if (File.Exists(p)) CachedChangelog = File.ReadAllText(p).Trim();
        } catch (Exception e) { Lg.Ex("读取更新说明缓存失败", e); }
        return CachedChangelog;
    }

    /// <summary>把这次拿到的更新说明写进缓存（给下一次开游戏的第一眼用）。写失败只记日志。</summary>
    private static void SaveCachedChangelog(string text) {
        if (string.IsNullOrEmpty(text)) return;
        try {
            CachedChangelog = text;
            _cacheLoaded = true;
            File.WriteAllText(Path.Combine(Main.ModEntry.Path, ChangelogCacheFileName), text, System.Text.Encoding.UTF8);
        } catch (Exception e) { Lg.Ex("写更新说明缓存失败", e); }
    }

    /// <summary>开始检查（manual=true 会 Toast 反馈；自动时不打扰玩家）</summary>
    public static void CheckNow(bool manual = true) {
        if (_req != null) { if (manual) PortalUI.ShowToast(Lang.Tr("正在检查更新…")); return; }
        if (_dlvActive) { if (manual) PortalUI.ShowToast(Lang.Tr("正在下载默认视频背景，稍等喵")); return; }
        ReadLocalVersion();
        _manual = manual;
        Phase = 1; Progress = 0f;
        // 每次检查重新允许"换线路"（上一次可能是全都不通才停的）；_keyIndex 不清零：
        // 要是真换了第一条通的，下次就继续用它
        _allLinesFailed = false;
        NewVersion = ""; NewChangelog = ""; ChangelogHistory = "";
        DetailLoaded = false;          // 这次会话还没拿到平台详情（启动页据此区分"在拉"和"没有"）
        NewAssetVersion = "";
        RefreshLocalAssetVersion();
        _pendingDownloadUrl = ""; _pendingFileName = "";
        _jobs.Clear(); _curJob = null; _runNotes.Clear();
        _runDeferred = false; _runFailed = false;
        _runModDone = false; _runAssetDone = false;
        Status = "正在检查更新…（同时检查 mod 包和资源包）";
        StartGet($"{BaseUrl}/api/mods/{ResourceType}/{Slug}", 1);
    }

    /// <summary>换下一条 API 线路；换成功（并且已经把请求重发出去）返回 true。
    /// 没有下一条了 → 置 _allLinesFailed 并返回 false（调用方据此停止）。
    /// 重发用的是刚才那次带 key 的请求（_lastKeyed*）—— 换的只是 key，请求内容一个字不改。
    /// 只前进不后退：通了的那条就一直用，要回第一条得重开游戏。
    /// 换 key 时重试计数归零：每条 key 各自享有完整的 MaxRetry 次（成功键总尝试 = 键数 × MaxRetry）。</summary>
    private static bool TryNextKey() {
        try {
            if (_keyIndex + 1 >= ApiKeys.Length) {
                _allLinesFailed = true;
                return false;
            }
            _keyIndex++;
            _allLinesFailed = false;
            retryCount = 0;      // 新的一条线路 它自己的重试次数从 0 开始
            Lg.Info($"[LevelLib] 更新：第 {_keyIndex} 条线路不通 → 换第 {_keyIndex + 1}/{ApiKeys.Length} 条重试"
                + $"（这条自己有 {MaxRetry} 次重试机会 全部试完才继续往下换）");
            Phase = 1; Progress = 0f;
            Status = $"正在换线路重试（第 {_keyIndex + 1}/{ApiKeys.Length} 条）…";
            if (string.IsNullOrEmpty(_lastKeyedUrl)) return false;   // 没有可重发的目标 → 交给上层
            StartRequest(_lastKeyedUrl, _lastKeyedMethod, _lastKeyedKind);
            return true;
        } catch (Exception e) {
            Lg.Ex("换用下一条 API 线路失败", e);
            return false;
        }
    }

    /// <summary>每帧推进请求（顺带盯着后台安装的进度）</summary>
    public static void TickRequest() {
        // 「下载默认视频背景」那条线的兜底收口：任何一步失败都会把 Phase 打到 5，
        // 与其在十来个失败分支里逐个收线，不如在这里统一判 —— 标志不清掉，按钮会一直停在"下载中"。
        // 主要失败分支都会先给一句明确的提示，这里只负责把剩下的长尾清干净（日志留证）。
        if (_dlvActive && Phase == 5) {
            _dlvActive = false;
            try { Lg.Info("默认视频背景：中途失败 — " + Status); } catch { }
        }
        // 后台安装中：只更新进度条，不碰别的
        if (_installing) {
            // 顺便量一下主线程这几帧的最大间隔 —— 以前这里会记录到 3.2 秒
            if (_installLastFrameAt > 0f) {
                float dt = Time.unscaledTime - _installLastFrameAt;
                if (dt > _installWorstFrame) {
                    _installWorstFrame = dt;
                    // 连"这一帧出现在第几毫秒"一起记下来：只报一个帧间隔说不清是"安装期"还是"下载/解码期"的锅
                    _installWorstFrameAt = (Time.unscaledTime - _tInstall0) * 1000f;
                }
            }
            _installLastFrameAt = Time.unscaledTime;
            if (_installDone) {
                FinishInstall();
            } else {
                Progress = _installProgress;
                Status = (_curJob != null && _curJob.VideoOnly)
                    ? $"正在解压默认视频背景… {(_installProgress * 100f):F0}%（后台进行，不会卡顿）"
                    : $"正在安装更新… {(_installProgress * 100f):F0}%（后台进行，不会卡顿）";
            }
        }
        if (_req == null) return;
        if (!_req.isDone) {
            // 下载/取直链：把进度写到界面上（重试等待时显示已等多少秒，免得看着像卡死）
            if (Phase == 2) {
                Progress = Mathf.Clamp01(_req.downloadProgress);
                if (_req.downloadProgress <= 0f) {
                    float waited = Time.unscaledTime - _reqT0;
                    string what = (_curJob != null && _curJob.VideoOnly) ? "正在下载默认视频背景"
                                : IsCurrentJobAsset() ? "正在获取资源包" : "正在获取 mod 更新";
                    Status = retryCount > 0
                        ? $"重试中（{retryCount}/{MaxRetry}）… 已等待 {waited:F0} 秒"
                        : $"{what}… 已等待 {waited:F0} 秒";
                }
            }
            return;
        }
        var req = _req;
        _req = null;
        long code = req.responseCode;
        _tRecv = Time.unscaledTime;      // 响应到手的时刻（下面靠相邻时刻的差定位各阶段耗时）
        // 这是"发请求→收到响应"的异步往返总时长，不是帧间隔（中间跨了很多帧，UnityWebRequest 不占帧）
        _recvFrameGap = _tRecv - _tReqSent;
        try { _lastContentEncoding = req.GetResponseHeader("Content-Encoding") ?? "(无)"; } catch { }
        try {
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) {
                string err = string.IsNullOrEmpty(req.error) ? ("HTTP " + code) : req.error;
                Phase = 5;
                string body = "";
                // 下载失败时响应体可能是"半个 zip"：把几 MB 二进制交给 downloadHandler.text 让 Unity
                // 当 UTF-8 解码实测烧掉 400ms 以上，所以 kind=3（文件本体）只报错误摘要，绝不碰正文
                try {
                    if (_kind != 3 && req.downloadHandler != null) body = Trim(req.downloadHandler.text, 200);
                } catch { }
                Status = (code == 429) ? "检查失败：请求太频繁（每分钟 120 次上限）"
                     : (code == 401 ? "检查失败：API Key 无效或被撤销"
                     : (code == 403 ? "失败：接口拒绝访问（403）"
                     : ("失败：HTTP " + code)));
                if (_kind == 3) Status = "下载失败：HTTP " + code;
                // 把响应体也记下来（平台的错误说明在 body 里，比如"API Key 无权限访问此接口"）
                if (_kind == 3) {
                    long got = -1;
                    try { if (req.downloadHandler != null && req.downloadHandler.data != null) got = req.downloadHandler.data.Length; } catch { }
                    Lg.Info($"[LevelLib] 更新：下载失败 HTTP {code} 错误={err} 已收到字节={got}"
                        + $" file={_pendingFileName}（不解析二进制正文；下面按需重试）");
                } else {
                    Lg.Info($"[LevelLib] 更新：请求失败 HTTP {code} {err}（kind={_kind} method={req.method}）"
                        + (string.IsNullOrEmpty(body) ? "" : " body=" + body));
                }
                if (_manual) PortalUI.ShowToast(Status);

                // 多线路 + 每个都试一遍，定稿顺序是"先重试、后换线路"：
                // ① 同一条 key 失败 → 先重试它（retryCount < MaxRetry）
                // ② 这条 key 的重试用光了 → 换下一条（TryNextKey 里把 retryCount 归零，每条各自享有完整的 MaxRetry）
                // ③ 所有 key 都试完 → 停，不再拿最后一条死掉的 key 白撞
                // 预签名直链（kind=3）不带 key、不参与换线路，它有自己的重试路；kind=0（预热）失败无所谓。
                if (_kind == 2) {
                    if (retryCount < MaxRetry && !string.IsNullOrEmpty(_pendingFileId)) {
                        retryCount++;
                        Phase = 2; Progress = 0f;
                        Status = $"获取下载地址失败，正在重试（{retryCount}/{MaxRetry}）…";
                        Lg.Info($"[LevelLib] 更新：download-intent 超时/失败 → "
                            + $"第 {_keyIndex + 1}/{ApiKeys.Length} 条线路的第 {retryCount}/{MaxRetry} 次重试"
                            + "（这条试满才换下一条）");
                        StartPost($"{BaseUrl}/api/mod-files/{_pendingFileId}/download-intent", 2);
                        return;
                    }
                    // 这条线路的重试用完了 换下一条（它自己再有 MaxRetry 次）
                    if (TryNextKey()) return;
                    if (_allLinesFailed) {
                        if (_dlvActive) { FailVideoDl("网络不通（下载地址拿不到）"); return; }
                        Status = $"获取下载地址失败：{ApiKeys.Length} 条线路各试了 {MaxRetry} 次都不通（稍后再试）";
                        Lg.Info($"[LevelLib] 更新：{ApiKeys.Length} 条线路 × {MaxRetry} 次全部失败 → 停止本次检查"
                            + "（清空下载队列，下次检查会重新排队）");
                        _jobs.Clear(); _curJob = null; _runNotes.Clear();
                        _runDeferred = false; _runFailed = false;
                        if (_manual) PortalUI.ShowToast(Status);
                        return;
                    }
                } else if (_kind != 3 && _kind != 0) {
                    // 普通的带 key 请求（列表 / 详情）：换线路那条路本来就是"原样重发"
                    if (TryNextKey()) return;
                    if (_allLinesFailed) {
                        if (_dlvActive) { FailVideoDl("网络不通（所有线路都不通）"); return; }
                        Status = "检查更新失败：所有线路都不通（稍后再试）";
                        Lg.Info("更新：所有线路都不通 → 停止本次检查"
                            + "（清空下载队列，下次检查会重新排队）");
                        _jobs.Clear(); _curJob = null; _runNotes.Clear();
                        _runDeferred = false; _runFailed = false;
                        if (_manual) PortalUI.ShowToast(Status);
                        return;
                    }
                }

                // 这里失败基本是网络/网站抽风（平台会把 download-intent 拖过超时，而响应其实已经到了、
                // body 里就是完整的下载直链，整串白扔太亏；下载本体也会偶发返回半个 zip），所以两条路都给重试：
                // ① 文件本体：预签名直链 24 小时内有效 → 拿原链重下，不用重新申请。
                // ② download-intent：重新 POST 一次同一个接口（接口自己会再给一条新直链）。
                // 这里不能顺手清 retryCount：原来有一句 `if (_kind != 2) retryCount = 0;`，
                // 下载（kind=3）失败时也会走到它，计数每次都被推回 1，日志里永远是「第 1 次重试」，
                // 网络不通就无限撞下去（玩家日志里几十次，每次都卡在 curl 超时）。
                // 计数该归零的两处阶段边界本来就有：拿到直链进入下载阶段（Updater.Version.cs）、
                // 换线路（TryNextKey）。
                if (_kind == 3 && retryCount < MaxRetry && !string.IsNullOrEmpty(_pendingDownloadUrl)) {
                    retryCount++;
                    StartGet(_pendingDownloadUrl, 3);
                    Phase = 2; Progress = 0f;
                    Status = $"下载失败，正在重试（{retryCount}/{MaxRetry}）…";
                    Lg.Info($"[LevelLib] 更新：下载失败，开始第 {retryCount} 次重试（重新下载）");
                    return;
                }
                // 重试用尽 → 给个能看懂的提示（而不是干巴巴的 "失败：HTTP 200"）
                if (_kind == 2) Status = "获取下载地址失败：平台响应太慢（已重试 " + MaxRetry + " 次，稍后再试）";
                else if (_kind == 3) Status = "下载失败：已重试 " + MaxRetry + " 次仍失败，稍后再试";
                if (_dlvActive) { FailVideoDl(_kind == 2 ? "平台响应太慢" : "下载重试多次仍失败"); return; }
                // 这一条下载彻底失败了，剩下的（比如资源包）也别接着下：清空队列，下次检查会重新排队；
                // 留着的话下次一进游戏就会莫名其妙继续下载
                _jobs.Clear(); _curJob = null; _runNotes.Clear();
                _runDeferred = false; _runFailed = false;
                return;
            }
            // 只有结果成功才拿内容，并且必须先判空再交给 JsonConvert —— 空串/null 传给 Newtonsoft 会抛
            // ArgumentNullException("value")。另外平台返回是 gzip 压缩的，而 Unity 在"手动设过请求头"之后
            // 就不自动解压了，downloadHandler.text 会变成空串，所以这里不用 .text 而是拿原始字节自己解压。
            string text = null;
            try { if (req.downloadHandler != null) text = DecodeBody(req); } catch (Exception ex) {
                Lg.Ex("更新：解压响应失败", ex);
            }
            _tDecode = Time.unscaledTime;    // 解码（gzip 解压）走完的时刻
            _decodeFrameGap = _tDecode - _tRecv;
            if (_kind == 0) {
                // 预热请求：连接建好了就行，返回的 JSON 直接丢掉（不解析、不提示）
                Lg.Info($"[LevelLib] 更新：网络预热完成 HTTP {code}"
                    + $"（请求往返 {_recvFrameGap * 1000f:F0}ms —— 异步等待，不占帧；"
                    + "预热过以后真请求就不用再付 TLS 握手这笔钱了）");
                return;
            }
            if (_kind == 3) {
                byte[] bytes = null;
                try { if (req.downloadHandler != null) bytes = req.downloadHandler.data; } catch { }
                if (bytes == null || bytes.Length == 0) { Phase = 5; Status = "下载到的文件是空的"; return; }
                HandleDownloaded(bytes);
                return;
            }
            if (string.IsNullOrEmpty(text)) {
                Phase = 5;
                Status = "返回内容为空（HTTP " + code + "）";
                Lg.Info($"[LevelLib] 更新：返回内容为空 kind={_kind} method={req.method} HTTP {code}"
                    + $" 内容类型={(req.GetResponseHeader("Content-Type") ?? "(无)")}"
                    + $" 内容编码={(req.GetResponseHeader("Content-Encoding") ?? "(无)")}"
                    + $" 原始字节数={(req.downloadHandler != null && req.downloadHandler.data != null ? req.downloadHandler.data.Length : -1)}");
                if (_manual) PortalUI.ShowToast(Lang.Tr("接口返回空内容喵"));
                return;
            }
            if (_kind == 1) HandleDetail(text);
            else if (_kind == 2) HandleIntent(text);
        } catch (Exception e) {
            Phase = 5; Status = "处理失败：" + e.GetType().Name;
            Lg.Ex("更新处理失败", e);
        } finally {
            req.Dispose();
        }
    }

    private static int _kind;   // 1=详情 2=下载直链 3=文件本体

    /// <summary>取响应正文并按需解压。
    /// 平台用 gzip 返回，而 Unity 一旦被手动设过请求头就不再自动解压，downloadHandler.text 会是空串 ——
    /// 所以这里直接处理原始字节：先按 Content-Encoding 试 gzip/deflate，再兜底试 deflate、最后当纯文本。</summary>
    private static string DecodeBody(UnityEngine.Networking.UnityWebRequest req) {
        byte[] raw = req.downloadHandler.data;
        if (raw == null || raw.Length == 0) return null;
        string enc = req.GetResponseHeader("Content-Encoding") ?? "";
        enc = enc.Trim().ToLowerInvariant();
        if (enc.Contains("gzip")) {
            string s = TryInflate(raw, true);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        if (enc.Contains("deflate") || enc.Contains("br") || enc.Contains("zstd")) {
            string s = TryInflate(raw, false);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        // 没有/不认识 Content-Encoding：先看前两个字节是不是 gzip 魔数，再试 zlib，最后当纯文本
        if (raw.Length > 2 && raw[0] == 0x1f && raw[1] == 0x8b) {
            string s = TryInflate(raw, true);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        if (raw.Length > 2 && raw[0] == 0x78) {
            string s = TryInflate(raw, false);      // zlib（deflate with header）
            if (!string.IsNullOrEmpty(s)) return s;
        }
        try { return System.Text.Encoding.UTF8.GetString(raw); } catch { return null; }
    }

    /// <summary>gzip / deflate 解压；失败返回 null（由调用方继续尝试别的格式）</summary>
    private static string TryInflate(byte[] raw, bool gzip) {
        try {
            using (var ms = new MemoryStream(raw))
            using (var ds = gzip ? (Stream)new GZipStream(ms, CompressionMode.Decompress)
                                 : new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream()) {
                var buf = new byte[16384];
                int n;
                while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                var bytes = outMs.ToArray();
                if (bytes.Length == 0) return null;
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        } catch { return null; }
    }

    private static void StartGet(string url, int kind) { StartRequest(url, "GET", kind); }
    private static void StartPost(string url, int kind) { StartRequest(url, "POST", kind); }

    /// <summary>开局先把到平台的 HTTPS 连接建立好：第一次发请求那一帧会卡 700ms 以上（其中大部分是
    /// TLS 握手/DNS 在主线程上的同步等待），提前用一个轻量 GET 把连接/握手做掉，后面真检查更新时就不用现握手了；
    /// 顺带完成程序集/网络栈的首次初始化。失败无所谓，静默忽略。</summary>
    public static void WarmUp() {
        if (_warmDone) return;
        if (_req != null || _installing) return;      // 已经有请求在飞了，没必要
        _warmDone = true;
        StartGet($"{BaseUrl}/api/mods/{ResourceType}/{Slug}", 0);   // kind=0 → 结果直接丢掉
        Lg.Info("更新：已发起网络预热请求（消除首次请求的 TLS 握手卡帧）");
    }

    /// <summary>发请求。方法必须写对：download-intent 只认 POST，用 GET 会返回 403 "API Key 无权限访问此接口"。
    /// 另外两个坑：① 用 url + method 构造的 UnityWebRequest 不会自动创建 DownloadHandler，不显式给一个
    /// downloadHandler 就是 null、响应体永远收不到（表现是"HTTP 200 但内容为空"）；
    /// ② 预签名直链不要带 Authorization 头（R2 会报 400 Missing x-amz-content-sha256）。</summary>
    private static void StartRequest(string url, string method, int kind) {
        try {
            _kind = kind;
            _tReqSent = Time.unscaledTime;   // 记下"发请求这一帧"的时刻，回包里用来量卡帧
            _reqT0 = _tReqSent;
            _req = new UnityEngine.Networking.UnityWebRequest(url, method);
            _req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();   // 必须显式给，否则收不到响应体
            // 超时：文件本体给 5 分钟；download-intent 给 45 秒（平台会拖到 20 秒以上，而它其实已经把直链算好了，
            // 只是回得慢 —— 卡在 20 秒会把成功结果当失败扔掉）；其它查询接口维持 HttpTimeout。
            _req.timeout = (int)(kind == 3 ? 300f : (kind == 2 ? 45f : HttpTimeout));
            if (method == "POST") _req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(new byte[0]);
            var h = _req.SetRequestHeader;
            bool presigned = url.IndexOf("X-Amz-Signature", StringComparison.OrdinalIgnoreCase) >= 0
                          || url.IndexOf("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!presigned) {
                // 平台接口要 key；预签名直链是自证的，多带头反而被 S3/R2 拒绝
                _lastKeyedUrl = url; _lastKeyedMethod = method; _lastKeyedKind = kind;   // 换线路要原样重发它
                h("Authorization", "Bearer " + Key);
                h("User-Agent", "LevelLibrary/" + ReadLocalVersion());
            }
            Lg.Info($"[LevelLib] 更新：请求（{kind} {method}）{url}"
                + (presigned ? "" : $" 线路={Mathf.Clamp(_keyIndex, 0, ApiKeys.Length - 1) + 1}/{ApiKeys.Length}"));   // 日志里不含 key
            _req.SendWebRequest();
        } catch (Exception e) {
            Phase = 5; Status = "发起请求失败：" + e.GetType().Name;
            Lg.Ex("发起更新请求失败", e);
        }
    }

    // ================= 资源包：识别 / 本地记录 / 挑最新版 =================

}
