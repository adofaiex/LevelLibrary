using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LevelLibrary;

// ============================================================================
// TUF 社区难度天梯 —— 只读客户端
// 数据源：TUF（The Universal Forums）公开 API v2，https://api.tuforums.com
// 只碰三个端点，多一个都不许加：
//   · GET /v2/database/difficulties            91 条档位表（id → 档位名），缓存本地
//   · GET /v2/database/levels?query=&limit=100 曲名子串匹配，返回
//                                              {results,page,offset,limit,hasMore,total}
//   · GET /v2/database/levels/<id>/ratings     单谱面评级明细（可选端点，原样回传）
// 【红线】不带 Authorization。我们平台（adofaitools）的 key 只属于 adofaitools，
//         发给第三方等于把自家钥匙塞给别人；这里从头发到尾是匿名请求。
// 【主判据：格数 + BPM 两道硬门】（原话：「用谱面的格子数和 bpm 进行比对，这样不会出错」）
//   ① 候选里格数对不上的一律排除（不是扣分）；
//   ② 排除后恰好剩 1 条，自动采用；剩多条时用 BPM 精确相等定第二条，定不下就进确认栏；
//   ③ 定下来但 BPM 不符，记 NeedsReview（版本/倍速可能不同），进确认栏，不否掉；
//   ④ total == 1 那条老规矩保留但降级为辅助：只回 1 条也要过格数硬门，过不了进确认栏；
//   ⑤ hasMore == true（候选池被截断），一律不自动采用 —— 格数一致的候选可能落在第 101 条之后
//      （实测 query "R" → total=14137，只取回 100）；
//   ⑥ 本地格数读不到，硬门不可用，一律不自动采用。
//   artist / 谱师 / 曲名的打分只作软证据（排序 + 展示 + 对账），不再决定采不采用。
// 【格数口径：实测标定，不是猜的】（详见 TryCountTiles）
//   · 有 angleData：格数 = 条目数 − 值为 999 的条目数
//   · 否则有 pathData：格数 = 字符数 − '!' 的个数
//   （中旋在两种编码里都多占一个槽位，而那个槽位不是格子）
//   标定办法：把 TUF 上那份原始 level.adofai 下下来（18 份，覆盖两种编码、中旋 0~273），
//   用同一套扫描器算、与平台报的 tilecount 逐条比 —— 18/18 完全相等；
//   同一批里「不减去标记」的朴素算法只有 7/18。
// 【另外三条实测硬规矩】（来自 tmp_api\tuf_dryrun.py 的真实跑数）
//   ① query 是曲名子串匹配，不是模糊搜索。所以先用 .adofai 的 song 原值查，
//      仅在 total == 0 时剥掉艺术家前缀 / 括号限定词拿纯标题重试（命中即停）。
//      不这么做会严重失真：本地 "Plum - Skyscape" 字面查返回 0，改查 "Skyscape" 返回 17。
//   ② total == 1 的假阳性（本地 Komorebi/m-takeu 被对上 TUF 的 Komorebi/satie）——
//      现在由格数硬门兜住，不再靠 artist 比对。
//   ③ hasMore == true 一律不自动采用（见主判据⑤）。
// 解析与打分口径照抄 tmp_api\tuf_dryrun.py（read_header / query_fallbacks /
// score_candidate / review_flags / classify），不自己发明 —— 只是打分从「判据」降成「软证据」：
//   +2 候选中 artist 与本地 artist 相同（忽略大小写与空白）
//   +3 候选中 creator 与本地 author 或 levelAuthorName 相同
//   +1 bpm 相同（容差 0.5）
//   +1 候选 suffix 非空且出现在本地文件夹名或显示名里
//   difficulty.name == Unranked 或 diffId == 0 → matched_unranked（不算已评级）
// 【谱师回落】只在格数硬门一条都没剩下时启用（也就是「候选里没有格数一致的」那一档）：
//   在同一次查询的候选里，用 TUF 的 creator 与本地 Charter 比（CharterWords / CharterHit）。
//   命中一律判成 charter_match 进「需要你确认」，绝不自动写入 —— TUF 的 creator 常常是上传者
//   或审核者而不是谱师，它不是指纹（格数才是）；只有「谱师相同、格数或 BPM 对不上」这个弱
//   线索时才该让玩家自己点。命中多条就把命中的候选排在最前面给人挑。
//   本地 Charter 为空时这一层不启用（保持原来那一档），并把这件事计数进结果页的诊断数字。
// 【网络规矩】严格串行：任何时刻只有一个请求在飞；上一次请求结束到下一次发出
// 至少间隔 600ms（与 tuf_dryrun.py 的 _throttle 同一口径）。失败重试 1 次，超时 8s。
// User-Agent: LevelLibrary/<本地版本>。
// 【用法】Tick() 必须由主线程每帧驱动（已挂在 PortalUI 的常驻 Tick 段）：
//     var job = TufClient.Query(TufClient.ReadChartInfo(mainFile), displayName);
//     // ... 若干帧后 ...
//     if (job.Done) { job.Match.MatchType ... }
// ============================================================================

/// <summary>一个本地谱面的头信息（.adofai 的 settings 段）。字段口径与 tuf_dryrun.py 的
/// read_header() 完全一致：Song/Artist/Author 是原值，Song/Artist/Charter 是清洗后的可用值。</summary>
public class TufChartInfo {
    /// <summary>.adofai 绝对路径（只用于日志与后缀匹配）</summary>
    public string Path = "";
    /// <summary>所在文件夹名（suffix 加分要比它）</summary>
    public string FolderName = "";
    /// <summary>.adofai 的 song 原值（可能带 Unity 富文本标签）</summary>
    public string SongRaw = "";
    /// <summary>清洗后的曲名 —— 查询主键</summary>
    public string Song = "";
    /// <summary>.adofai 的 artist 原值</summary>
    public string ArtistRaw = "";
    /// <summary>清洗后的乐曲艺术家（注意：这不是谱师！）</summary>
    public string Artist = "";
    /// <summary>.adofai 的 author —— 谱师字段的历史写法</summary>
    public string Author = "";
    /// <summary>.adofai 的 levelAuthorName —— 谱师字段的新写法</summary>
    public string LevelAuthorName = "";
    /// <summary>谱师：levelAuthorName 优先，回落 author（与 tuf_dryrun.py 的 rec["charter"] 一致）。
    /// 界面上那个「谱面作者」以前显示的是 Artist（= 乐曲艺术家），是错的。</summary>
    public string Charter = "";
    /// <summary>BPM（0 表示没读到）</summary>
    public double Bpm;
    /// <summary>BPM 是否读到了</summary>
    public bool HasBpm;
    /// <summary>本机谱面的格数 —— 口径按 TUF 的 tilecount 逐条实测标定过（见 TryCountTiles）。
    /// 这是「格数 + BPM」主判据里的第一道硬门。</summary>
    public long TileCount;
    /// <summary>格数是否读到了（读不到 = 硬门不可用，绝不自动采用）</summary>
    public bool HasTileCount;
    /// <summary>格数是从哪个字段算出来的（angleData / pathData）—— 日志与标定报告用</summary>
    public string TileSource = "";
}

/// <summary>一个 TUF 候选（对应 tuf_dryrun.py 的 compact_candidate()）</summary>
public class TufCandidate {
    public long? Id;
    public string Song = "";
    public string Artist = "";
    public string Creator = "";
    public string Suffix = "";
    public double Bpm;
    public bool HasBpm;
    public long TileCount;
    public bool HasTileCount;
    public int? DiffId = null;
    /// <summary>档位名（difficulty.name；缺失时用档位表 id→name 补）</summary>
    public string DifficultyName = "";
    public List<string> Aliases = new List<string>();
    /// <summary>打分结果与构成（artist+2 / creator+3 / bpm+1 / suffix+1）</summary>
    public int Score;
    public List<string> ScoreReasons = new List<string>();
}

/// <summary>一次判定的结果（字段口径与 tuf_dryrun.py 的 classify() 返回字典一一对应）</summary>
public class TufMatch {
    /// <summary>not_found / auto / high_score / ambiguous / matched_unranked / query_failed / no_song</summary>
    public string MatchType = "";
    /// <summary>采用的候选的 TUF 谱面 id</summary>
    public long? TufId = null;
    /// <summary>档位名（如 "P6" / "G12" / "Unranked"）</summary>
    public string DifficultyName = "";
    /// <summary>档位 id（0 = Unranked）</summary>
    public int? DiffId = null;
    /// <summary>是否算「已评级」（只有 Unranked / diffId == 0 不算）</summary>
    public bool? Rated = null;
    /// <summary>采用的候选的得分（ambiguous 时是最高分）</summary>
    public int? Score = null;
    public List<string> ScoreReasons = new List<string>();
    /// <summary>采用的候选</summary>
    public TufCandidate Chosen = null;
    /// <summary>全部候选（已按得分降序排好，供「需要你确认」那一栏列出来）</summary>
    public List<TufCandidate> Candidates = new List<TufCandidate>();
    /// <summary>TUF 报的候选总数</summary>
    public int Total;
    /// <summary>本次真正取回的候选条数</summary>
    public int Returned;
    /// <summary>候选池是否完整（total == 取回条数 且 hasMore == false）——不完整就不许自动采用</summary>
    public bool PoolComplete;
    /// <summary>最高分并列的候选个数（ambiguous 时用于提示「并列几个」）</summary>
    public int? TopTieCount = null;
    /// <summary>不自动采用的原因（截断 / 艺术家对不上 / 并列…）</summary>
    public string Reason = "";
    /// <summary>实际发起查询用的串（回退生效时与本地 song 不同）</summary>
    public string QueryUsed = "";
    /// <summary>是否用了「纯标题」回退查询</summary>
    public bool FallbackUsed;
    /// <summary>一致性告警：artist_mismatch / creator_mismatch / title_mismatch</summary>
    public List<string> ReviewFlags = new List<string>();
    /// <summary>clean / note_only（只有谱师对得上，多半其实是同一首）/ needs_review（artist 或曲名对不上）</summary>
    public string FlagClass = "clean";
    /// <summary>定下来了但有要玩家确认的点 —— 目前唯一来源是采用的候选 BPM 与本地不符
    /// （版本/倍速可能不同，按口径不否掉，只让它走「需要你确认」）。
    /// 注意口径变化：artist / 曲名对不上不再置这一位，它们已经降级成软证据（见 SoftFlags）。</summary>
    public bool NeedsReview;
    /// <summary>弱告警（只有谱师对不上）</summary>
    public bool NoteOnly;
    /// <summary>有软告警（artist / 谱师 / 曲名对不上）—— 只作展示与对账，不参与采不采用</summary>
    public bool SoftFlags { get { return ReviewFlags != null && ReviewFlags.Count > 0; } }

    // ---- 主判据（格数 + BPM 两道硬门）的留痕，界面与对账都要看 ----

    /// <summary>格数硬门可用（本地格数读到了）</summary>
    public bool TileGateAvailable;
    /// <summary>本地格数（0 = 没读到）</summary>
    public long LocalTileCount;
    /// <summary>过了格数硬门、进入第二道门比对的候选条数</summary>
    public int TileMatchCount;
    /// <summary>采用的候选 BPM 与本地不一致（不否掉，但要玩家点）</summary>
    public bool BpmMismatch;

    // ---- 谱师回落（只在格数硬门一条都没剩下时启用）的留痕 ----

    /// <summary>本地 Charter 读到了（为空时谱师回落这条路不启用 —— 空值跟谁都不该算命中）</summary>
    public bool CharterGateAvailable;
    /// <summary>谱师回落的命中候选条数（0 = 一条都没命中，保持原来那一档的语义）</summary>
    public int CharterMatchCount;
    /// <summary>格数硬门一条都没命中、却因为本地 Charter 为空而没走谱师回落（结果页的诊断数字）</summary>
    public bool CharterSkippedEmpty;

    /// <summary>自动采用（auto / high_score）</summary>
    public bool Auto { get { return MatchType == TufClient.MatchAuto || MatchType == TufClient.MatchHighScore; } }
    /// <summary>自动采用且 TUF 已评级 —— 对应试算报告里「可自动写入」的那两档</summary>
    public bool AutoRated { get { return Auto && Rated == true; } }
}

/// <summary>一个查询任务。enqueue 之后不用管，完成后 Done == true。</summary>
public class TufQuery {
    /// <summary>本地谱面头信息（评级明细查询时为 null）</summary>
    public TufChartInfo Chart;
    /// <summary>玩家的显示名覆盖（titles.txt），suffix 加分要比它</summary>
    public string DisplayName = "";
    /// <summary>true = 这是 /levels/{id}/ratings 查询（可选端点）</summary>
    public bool IsRatings;
    /// <summary>/levels/{id}/ratings 的 levelId</summary>
    public long LevelId;
    /// <summary>完成标志（成功、失败、取消都会置上）</summary>
    public bool Done;
    /// <summary>查询结果（Done 之后才有值；IsRatings 时恒为 null）</summary>
    public TufMatch Match;
    /// <summary>/levels/{id}/ratings 的原始响应正文（字段随平台演进，这里不做建模、原样回传）</summary>
    public string RatingsJson;
    /// <summary>失败原因（网络 / 解析），成功为 null</summary>
    public string Error;
    /// <summary>已被取消</summary>
    public bool Cancelled;
}

// ---------------------------- JSON 响应模型 ----------------------------

/// <summary>/v2/database/levels 的响应信封：{results,page,offset,limit,hasMore,total}</summary>
public class TufLevelsResponse {
    [JsonProperty("results")] public List<TufLevelRaw> Results;
    [JsonProperty("page")] public int? Page;
    [JsonProperty("offset")] public int? Offset;
    [JsonProperty("limit")] public int? Limit;
    [JsonProperty("hasMore")] public bool HasMore;
    [JsonProperty("total")] public int? Total;
}

/// <summary>一条 TUF 谱面（只声明用得到的字段）</summary>
public class TufLevelRaw {
    [JsonProperty("id")] public long? Id;
    [JsonProperty("song")] public string Song;
    [JsonProperty("artist")] public string Artist;
    /// <summary>谱师名（TUF 顶层 creator；实测常是审核者/上传者，所以打分里只值 +3 不足以单独采信）</summary>
    [JsonProperty("creator")] public string Creator;
    [JsonProperty("suffix")] public string Suffix;
    [JsonProperty("bpm")] public double? Bpm;
    /// <summary>格子数：实测字段名是全小写 tilecount，两种写法都收（与 tuf_dryrun.py 一致）</summary>
    [JsonProperty("tilecount")] public long? TileCountLower;
    [JsonProperty("tileCount")] public long? TileCountUpper;
    [JsonProperty("diffId")] public int? DiffId;
    [JsonProperty("difficulty")] public TufDifficultyRef Difficulty;
    [JsonProperty("aliases")] public List<string> Aliases;
}

/// <summary>候选里的 difficulty 对象（可能整个缺失，也可能只有 id 没有 name）</summary>
public class TufDifficultyRef {
    [JsonProperty("id")] public int? Id;
    [JsonProperty("name")] public string Name;
}

/// <summary>/v2/database/difficulties 的一行</summary>
public class TufDifficultyRow {
    [JsonProperty("id")] public int? Id;
    [JsonProperty("name")] public string Name;
}

/// <summary>TUF 只读客户端（静态，主线程驱动）</summary>
public static class TufClient {

    // ================= 常量 =================

    /// <summary>TUF API 根。只读三个端点，见文件头。</summary>
    public const string ApiBase = "https://api.tuforums.com/v2/database";
    /// <summary>HTTP 超时（秒）—— 与 tuf_dryrun.py 的 8s 对齐</summary>
    private const int HttpTimeout = 8;
    /// <summary>每次查询固定取 100 条（与实测脚本一致）</summary>
    private const int ResultLimit = 100;
    /// <summary>串行请求的最小间隔（秒）：上一次请求结束 → 下一次发出，至少隔这么久</summary>
    public const float MinRequestInterval = 0.6f;
    /// <summary>档位表本地缓存的保鲜期（天）：过期就重新拉一次，没过期直接吃缓存（省一次请求）</summary>
    private const int DifficultyCacheDays = 7;
    private const string DifficultyCacheFile = "tuf_difficulties.json";
    /// <summary>读 .adofai 头的字节上限：要用的字段都在开头几 KB，angleData 可能几 MB，不整份读</summary>
    private const int HeaderBytes = 250000;

    // 判定档位名（与 tuf_dryrun.py 的 verdict["match_type"] 同名，方便两边对账）
    public const string MatchNotFound = "not_found";
    public const string MatchAuto = "auto";
    public const string MatchHighScore = "high_score";
    public const string MatchAmbiguous = "ambiguous";
    /// <summary>格数硬门一条都没命中、但谱师（creator）对上了 —— 语义与 ambiguous 不同：
    /// 这一档是「有线索、但线索不够硬」，一律进「需要你确认」，绝不自动写入。</summary>
    public const string MatchCharterMatch = "charter_match";
    public const string MatchUnranked = "matched_unranked";
    public const string MatchQueryFailed = "query_failed";
    public const string MatchNoSong = "no_song";

    // 请求种类
    private const int KindLevels = 1;
    private const int KindDifficulties = 2;
    private const int KindRatings = 3;

    // ================= 统计（界面/日志用）=================

    /// <summary>累计发出的 HTTP 请求数（含重试）</summary>
    public static int RequestCount;
    /// <summary>累计失败重试次数</summary>
    public static int RetryCount;
    /// <summary>累计最终失败的请求数</summary>
    public static int ErrorCount;

    // ================= 档位表（id → 档位名）=================

    private static readonly Dictionary<int, string> _difficultyNames = new Dictionary<int, string>();
    private static bool _difficultiesEnsured;      // 已经看过本地缓存 / 排过拉取（成功失败都算）
    private static bool _difficultiesPending;      // 排了一个拉取请求

    /// <summary>已知档位数（91 = 平台当前全量）</summary>
    public static int DifficultyCount { get { return _difficultyNames.Count; } }

    // ================= 运行时状态 =================

    // 用 List 手动当 FIFO：本项目引用的 System.dll 是游戏自带那份（裁剪过的），
    // 编译时找不到 Queue<T> 的类型转发（见 CrossModGuard.cs / PortalUI.ErrorDialog.cs 的同款注释）。
    private static readonly List<TufQuery> _queue = new List<TufQuery>();
    private static int _queueHead;                 // 队头下标（已取走的都在它前面）
    private static TufQuery _current;              // 正在处理的那一单（非空 = 别人别插队）
    private static UnityWebRequest _req;           // 正在飞的请求（严格串行：最多一个）
    private static string _reqUrl = "";
    private static int _reqKind;
    private static string _pendingUrl;             // 排好的下一个请求（等间隔够了就发）
    private static int _pendingKind;
    private static int _retryLeft;                 // 这一单请求还能重试几次
    private static float _lastDoneAt = -1f;        // 上一次请求结束的时刻（unscaledTime）

    // 当前 levels 查询的回退状态（只被 _current 用，单线程无并发问题）
    private static List<string> _fallbacks = new List<string>();
    private static int _fallbackIndex = -1;
    private static TufLevelsResponse _primary;     // 主查询（song 原值）的响应
    private static TufLevelsResponse _chosen;      // 第一个 total > 0 的响应（主查询或回退，照 python 口径）
    private static string _usedQuery = "";

    // ================= 对外状态 =================

    /// <summary>还有活没干完（有请求在飞 / 有请求待发 / 有单在处理 / 队列非空）</summary>
    public static bool Busy {
        get { return _req != null || _pendingUrl != null || _current != null || _queueHead < _queue.Count; }
    }
    /// <summary>还没轮到（不含正在处理的那一单）</summary>
    public static int PendingCount { get { return _queue.Count - _queueHead; } }

    /// <summary>取队头。队头攒多了就地压缩一次，避免长批量跑出个只增不减的大 List。</summary>
    private static bool TryDequeue(out TufQuery job) {
        if (_queueHead >= _queue.Count) { job = null; return false; }
        job = _queue[_queueHead];
        _queue[_queueHead] = null;
        _queueHead++;
        if (_queueHead >= 64 && _queueHead * 2 >= _queue.Count) {
            _queue.RemoveRange(0, _queueHead);
            _queueHead = 0;
        }
        return true;
    }

    // ================= 主线程驱动 =================

    /// <summary>每帧调用（主线程）。空闲时只有几个空判断，开销可忽略。</summary>
    public static void Tick() {
        try {
            TickInner();
        } catch (Exception e) {
            // Tick 绝对不许把异常抛出去：它挂在常驻 Tick 段，抛出去会让后面的模块整帧不跑
            LogException("TUF：Tick 异常", e);
            AbortAll("Tick 异常");
        }
    }

    private static void TickInner() {
        // ① 有请求在飞 → 只推进它（严格串行：别的什么都不做）
        if (_req != null) { PollRequest(); return; }

        // ② 排好的请求：间隔够了才发
        if (_pendingUrl != null) {
            if (!IntervalOk()) return;
            StartScheduled();
            return;
        }

        // ③ 这一单还在处理中（它自己会排下一个请求，或者已经收尾）
        if (_current != null) return;

        // ④ 档位表：全局一次性（本地缓存能用就不联网）
        if (_difficultiesPending) {
            _difficultiesPending = false;
            Schedule(ApiBase + "/difficulties", KindDifficulties);
            return;
        }

        // ⑤ 取下一单
        TufQuery job;
        while (TryDequeue(out job)) {
            if (job == null) continue;
            if (job.Cancelled) { job.Done = true; continue; }             // 取消：直接了结，不发请求
            if (job.IsRatings) {
                _current = job;
                Schedule(ApiBase + "/levels/" + job.LevelId.ToString(CultureInfo.InvariantCulture) + "/ratings", KindRatings);
                return;
            }
            if (job.Chart == null || string.IsNullOrEmpty(job.Chart.Song)) {
                job.Match = new TufMatch { MatchType = MatchNoSong, QueryUsed = "" };   // 照 tuf_dryrun.py 的 no_song 档
                job.Done = true;
                continue;
            }
            _current = job;
            _fallbacks = QueryFallbacks(job.Chart.Song);
            _fallbackIndex = -1;
            _primary = null;
            _chosen = null;
            _usedQuery = job.Chart.Song;
            Schedule(LevelsUrl(job.Chart.Song), KindLevels);
            return;
        }
    }

    private static bool IntervalOk() {
        if (_lastDoneAt < 0f) return true;
        // 必须用 unscaledTime：面板打开时游戏是 Time.timeScale = 0，受缩放的时间会冻住
        return (Time.unscaledTime - _lastDoneAt) >= MinRequestInterval;
    }

    /// <summary>排一个"新"请求（重试次数复位）</summary>
    private static void Schedule(string url, int kind) {
        _pendingUrl = url;
        _pendingKind = kind;
        _retryLeft = 1;      // 每个新请求允许失败重试 1 次（与 tuf_dryrun.py 的 get(..., retries=1) 一致）
    }

    /// <summary>排一个"重试"请求（不动重试次数，避免无限重试）</summary>
    private static void ScheduleRetry(string url, int kind) {
        _pendingUrl = url;
        _pendingKind = kind;
    }

    private static string LevelsUrl(string query) {
        return ApiBase + "/levels?query=" + Uri.EscapeDataString(query ?? "") + "&limit=" + ResultLimit;
    }

    /// <summary>User-Agent: LevelLibrary/&lt;版本&gt;</summary>
    private static string UserAgent() {
        string v = "";
        try { v = Updater.ReadLocalVersion(); } catch { }
        if (string.IsNullOrEmpty(v)) v = "?";
        return "LevelLibrary/" + v;
    }

    /// <summary>真正发请求。两个必须记住的坑：
    ///  ① `new UnityWebRequest(url, "GET")` 不会自动创建 DownloadHandler —— 不显式给一个
    ///     `DownloadHandlerBuffer()`，downloadHandler 就是 null，响应体永远收不到（表现是「HTTP 200 但内容为空」）。
    ///  ② 手动设过请求头之后 Unity 不再自动解压，所以正文一律走 DecodeBody 自己解（见下）。
    /// 另外：绝不设 Authorization —— TUF 是第三方。</summary>
    private static void StartScheduled() {
        string url = _pendingUrl;
        int kind = _pendingKind;
        _pendingUrl = null;
        _reqUrl = url;
        _reqKind = kind;
        UnityWebRequest req = null;
        try {
            req = new UnityWebRequest(url, "GET");
            req.downloadHandler = new DownloadHandlerBuffer();   //  必须显式给，否则收不到响应体
            req.timeout = HttpTimeout;
            req.SetRequestHeader("User-Agent", UserAgent());
            req.SetRequestHeader("Accept", "application/json");
            // 这里不设 Authorization：我们平台的 key 只发给 adofaitools，不许出闸到第三方
            _req = req;
            req.SendWebRequest();
        } catch (Exception e) {
            try { if (req != null) req.Dispose(); } catch { }
            _req = null;
            _lastDoneAt = Time.unscaledTime;   // 发起就失败也算一次"请求结束"，间隔照算
            ErrorCount++;
            LogException("TUF：发起请求失败 " + url, e);
            OnRequestFailed(kind, e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>推进在飞的请求（完成/失败都在这里收尾）</summary>
    private static void PollRequest() {
        UnityWebRequest req = _req;
        if (req == null || !req.isDone) return;
        _req = null;
        _lastDoneAt = Time.unscaledTime;   //  间隔从「上一次请求结束」起算（与 tuf_dryrun.py 的 _throttle 同一口径）
        int kind = _reqKind;
        string url = _reqUrl;
        long code = 0;
        bool ok = false;
        string body = null;
        string err = null;
        try {
            code = req.responseCode;
            ok = req.result == UnityWebRequest.Result.Success;
            if (ok) body = DecodeBody(req);
            else err = string.IsNullOrEmpty(req.error) ? ("HTTP " + code) : req.error;
        } catch (Exception e) {
            ok = false;
            err = e.GetType().Name + ": " + e.Message;
        }
        try { req.Dispose(); } catch { }
        RequestCount++;

        if (!ok || string.IsNullOrEmpty(body)) {
            string why = err ?? ("HTTP " + code + "（响应体为空）");
            if (_retryLeft > 0) {
                _retryLeft--;
                RetryCount++;
                Lg.Info("TUF：请求失败，重试 1 次 —— " + why + " ｜ " + url);
                ScheduleRetry(url, kind);
                return;
            }
            ErrorCount++;
            Lg.Info("TUF：请求最终失败 —— " + why + " ｜ " + url);
            OnRequestFailed(kind, why);
            return;
        }
        OnBody(kind, url, body);
    }

    private static void OnBody(int kind, string url, string body) {
        if (kind == KindDifficulties) { HandleDifficulties(body); return; }
        if (kind == KindRatings) {
            if (_current != null) {
                _current.RatingsJson = body;
                _current.Done = true;
                _current = null;
            }
            return;
        }
        HandleLevels(url, body);
    }

    private static void OnRequestFailed(int kind, string why) {
        if (kind == KindRatings && _current != null) {
            _current.Error = why;
            _current.Done = true;
            _current = null;
            return;
        }
        if (kind == KindDifficulties) return;   // 档位表拉不到不影响判定：payload 里的 difficulty.name 照样能用
        if (_current != null) {
            _current.Error = why;
            _current.Match = new TufMatch { MatchType = MatchQueryFailed, Reason = why, QueryUsed = _usedQuery };
            _current.Done = true;
            _current = null;
        }
    }

    /// <summary>关闭一切（取消全部任务 / 丢弃在飞的请求）</summary>
    public static void AbortAll(string reason) {
        TufQuery j;
        while (TryDequeue(out j)) {
            if (j == null) continue;
            j.Cancelled = true;
            j.Done = true;
            if (j.Match == null) j.Match = new TufMatch { MatchType = MatchQueryFailed, Reason = reason ?? "aborted" };
        }
        if (_current != null) {
            _current.Cancelled = true;
            _current.Done = true;
            if (_current.Match == null) _current.Match = new TufMatch { MatchType = MatchQueryFailed, Reason = reason ?? "aborted" };
            _current = null;
        }
        _pendingUrl = null;
        if (_req != null) {
            try { _req.Abort(); } catch { }
            try { _req.Dispose(); } catch { }
            _req = null;
        }
    }

    // ================= 任务入口 =================

    /// <summary>排一个「本地谱面 → TUF 定级」查询。完成后 job.Done 为 true。</summary>
    /// <param name="chart">本地谱面头信息（TufChartInfo）</param>
    /// <param name="displayName">玩家的显示名覆盖（titles.txt），没有就传 null</param>
    public static TufQuery Query(TufChartInfo chart, string displayName = null) {
        var job = new TufQuery { Chart = chart, DisplayName = displayName ?? "" };
        EnsureDifficulties();
        _queue.Add(job);
        return job;
    }

    /// <summary>排一个可选的「/levels/{id}/ratings」查询（原始正文回传，不做建模）</summary>
    public static TufQuery QueryRatings(long levelId) {
        var job = new TufQuery { IsRatings = true, LevelId = levelId };
        _queue.Add(job);
        return job;
    }

    /// <summary>本地档位表能用就直接用；没有/过期才排一次拉取（省一次请求）</summary>
    private static void EnsureDifficulties() {
        if (_difficultiesEnsured) return;
        _difficultiesEnsured = true;
        bool fresh = LoadDifficultyCache();
        if (!fresh) _difficultiesPending = true;
        if (_difficultyNames.Count > 0) {
            Lg.Info("TUF：本地档位表 " + _difficultyNames.Count + " 条（" + (fresh ? "缓存新鲜，不联网" : "缓存过期，后台刷新") + "）");
        }
    }

    // ================= 响应处理：档位表 =================

    private static void HandleDifficulties(string body) {
        try {
            JToken tok = ParseToken(body);
            if (tok == null) return;
            List<TufDifficultyRow> rows = null;
            if (tok.Type == JTokenType.Array) rows = tok.ToObject<List<TufDifficultyRow>>(Serializer);
            else if (tok["results"] != null) rows = tok["results"].ToObject<List<TufDifficultyRow>>(Serializer);
            if (rows == null || rows.Count == 0) { Lg.Info("TUF：档位表响应里没有可用行"); return; }
            _difficultyNames.Clear();
            foreach (TufDifficultyRow r in rows) {
                if (r == null || !r.Id.HasValue) continue;
                _difficultyNames[r.Id.Value] = r.Name ?? "";
            }
            SaveDifficultyCache();
            Lg.Info("TUF：档位表已更新 " + _difficultyNames.Count + " 条");
        } catch (Exception e) {
            LogException("TUF：解析档位表失败", e);
        }
    }

    private static string DifficultyCachePath() {
        try {
            string dir = Main.ModEntry != null ? Main.ModEntry.Path : null;
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, DifficultyCacheFile);
        } catch { return null; }
    }

    /// <summary>读本地档位表缓存。返回是否「新鲜」（新鲜 = 不用再联网）。</summary>
    private static bool LoadDifficultyCache() {
        string path = DifficultyCachePath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try {
            var o = JsonConvert.DeserializeObject<DifficultyCache>(File.ReadAllText(path), JsonSettings);
            if (o == null || o.names == null || o.names.Count == 0) return false;
            _difficultyNames.Clear();
            foreach (KeyValuePair<string, string> kv in o.names) {
                int id;
                if (int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) {
                    _difficultyNames[id] = kv.Value ?? "";
                }
            }
            if (_difficultyNames.Count == 0) return false;
            DateTime at;
            if (!DateTime.TryParse(o.fetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out at)) return false;
            return (DateTime.UtcNow - at.ToUniversalTime()).TotalDays < DifficultyCacheDays;
        } catch (Exception e) {
            Lg.Info("TUF：档位表缓存读不了（" + e.GetType().Name + ": " + e.Message + "），将联网拉取");
            return false;
        }
    }

    private static void SaveDifficultyCache() {
        string path = DifficultyCachePath();
        if (string.IsNullOrEmpty(path)) return;
        try {
            var o = new DifficultyCache { fetchedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
            foreach (KeyValuePair<int, string> kv in _difficultyNames) {
                o.names[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value;
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(o, Formatting.Indented), new UTF8Encoding(false));
        } catch (Exception e) {
            Lg.Info("TUF：档位表缓存写不了（" + e.GetType().Name + "），下次启动会重新拉");
        }
    }

    private class DifficultyCache {
        public string fetchedAt = "";
        public Dictionary<string, string> names = new Dictionary<string, string>();
    }

    // ================= 响应处理：levels（含三条硬规矩）=================

    private static void HandleLevels(string url, string body) {
        TufLevelsResponse resp = ReadLevelsJson(body);
        if (resp == null) {
            // 解析不了当成一次请求失败走重试，别把「解析炸了」记成「查不到」
            if (_retryLeft > 0) {
                _retryLeft--;
                RetryCount++;
                Lg.Info("TUF：levels 响应解析失败，重试 1 次（前 200 字：" + Trim(body, 200) + "）");
                ScheduleRetry(url, KindLevels);
                return;
            }
            ErrorCount++;
            if (_current != null) {
                _current.Error = "响应解析失败";
                _current.Match = new TufMatch { MatchType = MatchQueryFailed, Reason = "响应解析失败", QueryUsed = _usedQuery };
                _current.Done = true;
                _current = null;
            }
            return;
        }

        int total = resp.Total.HasValue ? resp.Total.Value : (resp.Results != null ? resp.Results.Count : 0);
        if (_primary == null) _primary = resp;
        if (total > 0 && _chosen == null) { _chosen = resp; _usedQuery = QueryOfLevelsUrl(url); }

        // 硬规矩①：query 是曲名子串匹配 —— 只在 total == 0 时才拿「纯标题」重试，命中即停
        if (total == 0 && _chosen == null && (_fallbackIndex + 1) < _fallbacks.Count) {
            _fallbackIndex++;
            string alt = _fallbacks[_fallbackIndex];
            Lg.Info("TUF：'" + (_current != null && _current.Chart != null ? _current.Chart.Song : "") + "' 字面查 total=0，改用纯标题重试：'" + alt + "'");
            Schedule(LevelsUrl(alt), KindLevels);
            return;
        }

        TufLevelsResponse payload = _chosen ?? _primary;
        if (_current != null) {
            _current.Match = Classify(_current.Chart, _current.DisplayName, payload, _usedQuery);
            if (_current.Match != null) {
                TufChartInfo c = _current.Chart;
                _current.Match.FallbackUsed = c != null && !string.Equals(_usedQuery, c.Song, StringComparison.Ordinal);
            }
            _current.Done = true;
            _current = null;
        }
    }

    /// <summary>从 URL 里取回 query 参数（日志/对账用）</summary>
    private static string QueryOfLevelsUrl(string url) {
        try {
            int q = url.IndexOf("?query=", StringComparison.Ordinal);
            if (q < 0) return "";
            string rest = url.Substring(q + 7);
            int amp = rest.IndexOf('&');
            if (amp >= 0) rest = rest.Substring(0, amp);
            return Uri.UnescapeDataString(rest);
        } catch { return ""; }
    }

    // ================= 解析：.adofai 头 =================

    private static readonly Regex RichTagRe = new Regex(@"</?[a-zA-Z][^<>]{0,120}>", RegexOptions.Compiled);
    private static readonly Regex SpacesRe = new Regex(@"\s+", RegexOptions.Compiled);
    /// <summary>一次抓齐四个字段（song / artist / author / levelAuthorName），等价于对每个 key 单独 re.search</summary>
    private static readonly Regex HeaderRe = new Regex(
        "\"(song|artist|author|levelAuthorName)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\")", RegexOptions.Compiled);
    private static readonly Regex BpmRe = new Regex("\"bpm\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.Compiled);

    /// <summary>去掉 Unity 富文本标签并折叠空白（.adofai 的 song/artist 里真的会混进 &lt;size=75&gt; 这类标签）</summary>
    public static string CleanText(string value) {
        if (string.IsNullOrEmpty(value)) return "";
        string s = RichTagRe.Replace(value, "");
        return SpacesRe.Replace(s, " ").Trim();
    }

    /// <summary>读一个 .adofai 的头信息（有界读取：只用开头 250KB，angleData 再大也不怕）</summary>
    public static TufChartInfo ReadChartInfo(string adofaiPath, int headBytes = HeaderBytes) {
        var info = new TufChartInfo();
        if (string.IsNullOrEmpty(adofaiPath)) return info;
        info.Path = adofaiPath;
        try { info.FolderName = Path.GetFileName(Path.GetDirectoryName(adofaiPath)); } catch { info.FolderName = ""; }
        try {
            int take = Math.Max(4096, headBytes);
            byte[] buf;
            int read;
            using (var fs = new FileStream(adofaiPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                if (fs.Length < take) take = (int)fs.Length;
                buf = new byte[take];
                read = fs.Read(buf, 0, take);
            }
            if (read <= 0) return info;
            string text = Encoding.UTF8.GetString(buf, 0, read);   // 坏字节自动变 U+FFFD，不抛
            ParseChartText(text, info);
        } catch (Exception e) {
            Lg.Info("TUF：读谱面头失败 " + adofaiPath + " —— " + e.GetType().Name + ": " + e.Message);
        }
        return info;
    }

    /// <summary>把 .adofai 文本的头解析进 info（口径照抄 tuf_dryrun.py 的 read_header()）。
    /// 单独拆出来是为了让调用方复用自己已经缓存的谱面文本，不用重复读盘。</summary>
    public static void ParseChartText(string text, TufChartInfo info) {
        if (info == null || string.IsNullOrEmpty(text)) return;
        int idx = text.IndexOf("\"settings\"", StringComparison.Ordinal);
        string seg = idx >= 0 ? text.Substring(idx, Math.Min(8000, text.Length - idx)) : text;

        Match m = HeaderRe.Match(seg);
        while (m.Success) {
            string key = m.Groups[1].Value;
            string val = UnquoteJsonString(m.Groups[2].Value);
            switch (key) {   // 每个 key 只认第一次出现（与 re.search 一致）
                case "song": if (info.SongRaw.Length == 0) info.SongRaw = val; break;
                case "artist": if (info.ArtistRaw.Length == 0) info.ArtistRaw = val; break;
                case "author": if (info.Author.Length == 0) info.Author = val; break;
                case "levelAuthorName": if (info.LevelAuthorName.Length == 0) info.LevelAuthorName = val; break;
            }
            m = m.NextMatch();
        }
        Match bm = BpmRe.Match(seg);
        if (bm.Success) {
            double d;
            if (double.TryParse(bm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) {
                info.Bpm = d;
                info.HasBpm = true;
            }
        }
        info.Song = CleanText(info.SongRaw);
        info.Artist = CleanText(info.ArtistRaw);
        info.Author = CleanText(info.Author);
        info.LevelAuthorName = CleanText(info.LevelAuthorName);
        // 谱师：levelAuthorName 优先，回落 author（两个历史写法，实测总有一个是空的）
        info.Charter = string.IsNullOrEmpty(info.LevelAuthorName) ? info.Author : info.LevelAuthorName;
        // 本地格数（主判据的第一道硬门；口径见 TryCountTiles）。读不到就保持 HasTileCount = false。
        long tiles;
        string tileSrc;
        if (TryCountTiles(text, out tiles, out tileSrc)) {
            info.TileCount = tiles;
            info.HasTileCount = true;
            info.TileSource = tileSrc;
        }
    }

    // ================= 格数（主判据的第一道硬门；口径见 TryCountTiles 的标定说明）=================

    /// <summary>从 .adofai 文本里数格子数。口径按 TUF 的 tilecount 逐条实测标定，不是猜的：
    ///   · 有 angleData：格数 = 条目数 − 值为 999 的条目数
    ///   · 否则有 pathData：格数 = 字符数 − '!' 的个数
    /// 两条其实是同一件事 —— ADOFAI 的中旋（midspin）在两种编码里都多占一个槽位，
    /// 而那个槽位不是一个格子：angleData 里是一条哨兵值 999，pathData 里是一个 '!' 标记字符。
    /// 【标定过程】把 TUF 上那份原始 level.adofai 下下来（18 份，覆盖 angleData / pathData 两种编码、
    /// 中旋数 0~273），用同一套扫描器算，与平台报的 tilecount 逐条比：18/18 完全相等；
    /// 同一批里「不减去标记」的朴素算法只有 7/18。反向复核：本机 138 个「试算时已对上 TUF」的文件里
    /// 100 个相等、38 个不等 —— 不等的那 38 个正是「本机这份跟 TUF 那份不是同一张谱」
    /// （对照组里 TUF 自己那份 18/18 全中），也就是格数硬门真正要拦下来的那批假匹配。
    /// 返回 false = 文本里没有完整可数的路径数据（截断 / 一个字段都没有）——
    /// 这时硬门不可用，调用方绝不自动采用。</summary>
    public static bool TryCountTiles(string text, out long tiles, out string source) {
        tiles = 0;
        source = "";
        if (string.IsNullOrEmpty(text)) return false;

        int ai = text.IndexOf("\"angleData\"", StringComparison.Ordinal);
        if (ai >= 0) {
            int ob = text.IndexOf('[', ai);
            if (ob < 0) return false;
            int cb = text.IndexOf(']', ob);
            if (cb < 0) return false;                 // 有界读取里没截完 → 不猜
            long n = 0, marks = 0;
            int i = ob + 1;
            while (i < cb) {
                while (i < cb && (text[i] == ',' || text[i] == ' ' || text[i] == '\t' || text[i] == '\r' || text[i] == '\n')) i++;
                int s = i;
                while (i < cb && text[i] != ',') i++;
                int e = i;
                while (e > s && (text[e - 1] == ' ' || text[e - 1] == '\t' || text[e - 1] == '\r' || text[e - 1] == '\n')) e--;
                if (e <= s) continue;
                n++;
                if (IsSentinel999(text, s, e)) marks++;
            }
            if (n <= 0) return false;
            tiles = n - marks;
            source = "angleData";
            return tiles > 0;
        }

        int pi = text.IndexOf("\"pathData\"", StringComparison.Ordinal);
        if (pi >= 0) {
            int q = text.IndexOf('"', pi + 10);
            if (q < 0) return false;
            int q2 = text.IndexOf('"', q + 1);
            if (q2 < 0) return false;
            long marks = 0;
            for (int i = q + 1; i < q2; i++) { if (text[i] == '!') marks++; }
            tiles = (q2 - q - 1) - marks;
            source = "pathData";
            return tiles > 0;
        }
        return false;
    }

    /// <summary>这一项是不是中旋哨兵 999（写成 999 / 999.0 / 999.00 都算）。
    /// 不解析浮点、不分配字符串 —— 批量扫几千个谱面时这一点很值。</summary>
    private static bool IsSentinel999(string text, int s, int e) {
        if (e - s < 3) return false;
        if (text[s] != '9' || text[s + 1] != '9' || text[s + 2] != '9') return false;
        for (int i = s + 3; i < e; i++) {
            char c = text[i];
            if (c == '.') continue;
            if (c == '0') continue;
            return false;
        }
        return true;
    }

    /// <summary>把一个 JSON 字符串字面量（含两边的引号）还原成文本</summary>
    private static string UnquoteJsonString(string literal) {
        if (string.IsNullOrEmpty(literal)) return "";
        try {
            string s = JsonConvert.DeserializeObject<string>(literal);
            return s ?? "";
        } catch {
            return literal.Trim('"');
        }
    }

    // ================= 回退查询串（照抄 query_fallbacks）=================

    /// <summary>正常化比较键：去空白 + 转小写（照抄 norm()）</summary>
    public static string Norm(string value) {
        if (value == null) return "";
        return SpacesRe.Replace(value, "").Trim().ToLowerInvariant();
    }

    private static readonly Regex ArtistPrefixRe = new Regex(@"^\s*(.{1,60}?)\s+[-–—]\s+(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex ParenTailRe = new Regex(@"^(.*?)\s*[\(\[]([^\)\]]{0,40})[\)\]]\s*$", RegexOptions.Compiled);

    /// <summary>total == 0 时的备用查询串：剥掉「艺术家 - 」前缀、剥掉结尾括号限定词。
    /// 两条规则都保守，且绝不返回原串、不返回短于 3 字的串（照抄 query_fallbacks）。</summary>
    public static List<string> QueryFallbacks(string song) {
        var outp = new List<string>();
        if (string.IsNullOrEmpty(song)) return outp;

        // "Artist - Title" / "Artist – Title" → "Title"
        Match m = ArtistPrefixRe.Match(song);
        if (m.Success) {
            string tail = m.Groups[2].Value.Trim();
            if (tail.Length >= 3) outp.Add(tail);
        }
        // 结尾括号限定词："Foo (Bar Ver.)" → "Foo"
        Match m2 = ParenTailRe.Match(song);
        if (m2.Success && m2.Groups[1].Value.Trim().Length >= 4) outp.Add(m2.Groups[1].Value.Trim());

        var seen = new HashSet<string>();
        seen.Add(Norm(song));
        var uniq = new List<string>();
        foreach (string cand in outp) {
            string key = Norm(cand);
            if (key.Length == 0) continue;
            if (seen.Contains(key)) continue;
            if (cand.Length < 3) continue;
            seen.Add(key);
            uniq.Add(cand);
        }
        return uniq;
    }

    // ================= 打分与一致性检查（照抄 score_candidate / review_flags）=================

    private static readonly Regex PunctRe = new Regex(
        @"[\s\-–—_~!！?？.,，。:：;；'""“”‘’()（）\[\]【】{}<>《》/\\|+&*#@$%^=]+", RegexOptions.Compiled);
    private static readonly Regex TitleNoiseRe = new Regex(
        @"[\(\[]\s*(?:ver|version|remake|remix|nerf|nerfed|short|cut|edit|hd|new)[^\)\]]*[\)\]]", RegexOptions.Compiled);
    private static readonly Regex ArtistTokenRe = new Regex(
        @"[()（）\[\]【】,，、/&＆+·・|]|feat\.?|ft\.?|prod\.?", RegexOptions.Compiled);
    private static readonly Regex SplitRe = new Regex(@"\s+", RegexOptions.Compiled);

    /// <summary>曲名比较专用（不是打分用）：去括号限定词 / 标点 / ver.·remake·remix 之类的噪声</summary>
    public static string TitleKey(string value) {
        if (value == null) return "";
        string s = value.ToLowerInvariant();
        s = TitleNoiseRe.Replace(s, " ");
        s = PunctRe.Replace(s, "");
        string[] noise = { "ver", "remake", "remix", "nerfed", "nerf", "short", "cut", "edit", "feat", "ft" };
        foreach (string n in noise) s = s.Replace(n, "");
        return s;
    }

    /// <summary>艺术家比较用的原始小写串（去空白）</summary>
    public static string ArtistKey(string value) {
        if (value == null) return "";
        return SpacesRe.Replace(value, "").ToLowerInvariant();
    }

    /// <summary>艺术家分词，让 "Camellia (かめりあ)" 与 "かめりあ (Camellia)" 比得上</summary>
    public static HashSet<string> ArtistTokens(string value) {
        var set = new HashSet<string>();
        if (value == null) return set;
        string s = ArtistTokenRe.Replace(value.ToLowerInvariant(), " ");
        foreach (string t in SplitRe.Split(s)) {
            if (t.Length > 1) set.Add(t);
        }
        return set;
    }

    /// <summary>两个艺术家串是否可能指同一个人。任一边为空一律返回 true（无从反驳）。
    /// 这个函数就是硬规矩②里的「艺术家校验」。</summary>
    public static bool ArtistsCompatible(string localArtist, string candArtist) {
        string la = ArtistKey(localArtist);
        string ca = ArtistKey(candArtist);
        if (la.Length == 0 || ca.Length == 0) return true;   // nothing to contradict
        if (la == ca) return true;
        if (la.IndexOf(ca, StringComparison.Ordinal) >= 0) return true;
        if (ca.IndexOf(la, StringComparison.Ordinal) >= 0) return true;
        HashSet<string> lt = ArtistTokens(localArtist);
        HashSet<string> ct = ArtistTokens(candArtist);
        foreach (string t in lt) { if (ct.Contains(t)) return true; }
        return false;
    }

    /// <summary>谱师比较用的归一化：去首尾空白、转小写、按分隔符拆成词。
    /// 归一化只有这一处实现 —— 分词与艺术家那边共用 ArtistTokenRe（括号、逗号、顿号、斜杠、
    /// 和号、加号、间隔点、竖线以及 feat. / ft. / prod. 都算分隔符），所以带括号的团队名也拆得开：
    /// NeoMoyelle(Video Creator) 拆成 neomoyelle / video / creator。
    /// 长度 ≤ 1 的词丢掉（一个字母跟谁都像，留着只制造噪声）。
    /// 纯函数：只吃字符串、只吐词集合，不碰网络、磁盘与界面。</summary>
    public static HashSet<string> CharterWords(string value) {
        return ArtistTokens(value);
    }

    /// <summary>谱师算不算命中：两边各拆成词，任一词相等即算命中。任一边为空一律不算命中
    /// （空值不是证据）。这是谱师回落唯一的判据，别在别处再写一份分词比较。</summary>
    public static bool CharterHit(string localCharter, string candCreator) {
        string a = (localCharter ?? "").Trim();
        string b = (candCreator ?? "").Trim();
        if (a.Length == 0 || b.Length == 0) return false;
        HashSet<string> lt = CharterWords(a);
        HashSet<string> ct = CharterWords(b);
        if (lt.Count == 0 || ct.Count == 0) return false;
        foreach (string t in lt) { if (ct.Contains(t)) return true; }
        return false;
    }

    /// <summary>一个候选是否算「已评级」：只有 Unranked / diffId == 0 不算。
    /// 注意 Censored（diffId 1000）/ Qq / P0 / Marathon 这些特殊档，按试算口径算已评级；
    /// 界面上要不要写属于写入策略（那一层自己按档位名过滤）。</summary>
    public static bool IsRanked(string name, int? diffId) {
        if (diffId.HasValue && diffId.Value == 0) return false;
        return (name ?? "").Trim().ToLowerInvariant() != "unranked";
    }

    /// <summary>把一个 TUF 候选压平（照抄 compact_candidate）</summary>
    public static TufCandidate CompactCandidate(TufLevelRaw raw) {
        var c = new TufCandidate();
        if (raw == null) return c;
        c.Id = raw.Id;
        c.Song = raw.Song ?? "";
        c.Artist = raw.Artist ?? "";
        c.Creator = raw.Creator ?? "";
        c.Suffix = raw.Suffix ?? "";
        c.HasBpm = raw.Bpm.HasValue;
        c.Bpm = raw.Bpm.HasValue ? raw.Bpm.Value : 0d;
        long? tc = raw.TileCountLower ?? raw.TileCountUpper;
        c.HasTileCount = tc.HasValue;
        c.TileCount = tc.HasValue ? tc.Value : 0L;

        TufDifficultyRef diff = raw.Difficulty;
        string name = diff != null ? diff.Name : null;
        int? diffId = raw.DiffId;
        if (!diffId.HasValue && diff != null) diffId = diff.Id;
        if (string.IsNullOrEmpty(name) && diffId.HasValue) {
            string mapped;
            if (_difficultyNames.TryGetValue(diffId.Value, out mapped)) name = mapped;
        }
        c.DiffId = diffId;
        c.DifficultyName = name ?? "";
        if (raw.Aliases != null) c.Aliases = raw.Aliases;
        return c;
    }

    /// <summary>给一个候选打分（照抄 score_candidate）：
    /// +2 artist 相同 / +3 creator 与本地 author 或 levelAuthorName 相同 / +1 bpm 相同 / +1 suffix 命中</summary>
    public static int ScoreCandidate(TufCandidate cand, TufChartInfo local, string folderKey, string displayKey) {
        int score = 0;
        if (cand == null || local == null) return 0;
        if (cand.ScoreReasons == null) cand.ScoreReasons = new List<string>();

        string ca = Norm(cand.Artist);
        if (ca.Length > 0 && ca == Norm(local.Artist)) { score += 2; cand.ScoreReasons.Add("artist+2"); }

        string cc = Norm(cand.Creator);
        if (cc.Length > 0 && (cc == Norm(local.Author) || cc == Norm(local.LevelAuthorName))) {
            score += 3; cand.ScoreReasons.Add("creator+3");
        }

        if (cand.HasBpm && local.HasBpm) {
            if (Math.Abs(cand.Bpm - local.Bpm) < 0.51) { score += 1; cand.ScoreReasons.Add("bpm+1"); }
        }

        string sfx = cand.Suffix == null ? "" : cand.Suffix.Trim();
        if (sfx.Length > 0) {
            string sk = Norm(sfx);
            if (sk.Length > 0 && (sk == (folderKey ?? "") || sk == (displayKey ?? ""))) {
                score += 1; cand.ScoreReasons.Add("suffix+1");
            }
        }
        return score;
    }

    /// <summary>一致性告警（照抄 review_flags）。artist / 曲名对不上是强证据，谱师对不上是弱证据
    /// （TUF 的 creator 常常是审核者/上传者，不是谱师）。</summary>
    public static List<string> ReviewFlagsOf(TufChartInfo local, TufCandidate cand) {
        var flags = new List<string>();
        if (local == null || cand == null) return flags;
        string candArtist = (cand.Artist ?? "").Trim();
        string candCreator = (cand.Creator ?? "").Trim();
        string localArtist = (local.Artist ?? "").Trim();
        string localCharter = (local.Charter ?? "").Trim();

        if (localArtist.Length > 0 && candArtist.Length > 0 && !ArtistsCompatible(localArtist, candArtist)) {
            flags.Add("artist_mismatch");
        }
        if (localCharter.Length > 0 && candCreator.Length > 0) {
            string lc = Norm(localCharter);
            string cc = Norm(candCreator);
            if (lc.Length > 0 && cc.Length > 0
                && lc.IndexOf(cc, StringComparison.Ordinal) < 0
                && cc.IndexOf(lc, StringComparison.Ordinal) < 0) {
                HashSet<string> lt = ArtistTokens(localCharter);
                HashSet<string> ct = ArtistTokens(candCreator);
                bool overlap = false;
                foreach (string t in lt) { if (ct.Contains(t)) { overlap = true; break; } }
                if (!overlap) flags.Add("creator_mismatch");
            }
        }
        string localTitle = TitleKey(local.Song);
        string candTitle = TitleKey(cand.Song);
        if (localTitle.Length > 0 && candTitle.Length > 0
            && localTitle.IndexOf(candTitle, StringComparison.Ordinal) < 0
            && candTitle.IndexOf(localTitle, StringComparison.Ordinal) < 0) {
            flags.Add("title_mismatch");
        }
        return flags;
    }

    /// <summary>把告警分成强/弱（照抄 flag_class）</summary>
    public static string FlagClassOf(List<string> flags) {
        if (flags == null) return "clean";
        if (flags.Contains("artist_mismatch") || flags.Contains("title_mismatch")) return "needs_review";
        if (flags.Contains("creator_mismatch")) return "note_only";
        return "clean";
    }

    // ================= 判定表（照抄 classify + 三条硬规矩）=================

    /// <summary>BPM「精确相等」的容差：只为吸收 128 vs 128.0 这类浮点表示误差。
    /// 不是 tuf_dryrun.py 打分里那种 0.5 的模糊匹配 —— 这里是硬门，宁可判不准也不用模糊。</summary>
    public const double BpmEpsilon = 0.01;

    /// <summary>把一个响应判定成一个结果。纯函数（不碰网络、不碰界面），自检直接调它。
    /// 【主判据】格数 + BPM 两道硬门（原话：「用谱面的格子数和 bpm 进行比对，这样不会出错」）
    ///   ① 候选里格数对不上的一律排除（不是扣分）；
    ///   ② 排除后恰好剩 1 条，自动采用 —— 这就是「不会出错」那条路；
    ///   ③ 剩多条时拿 BPM 精确相等定第二条；定不下（0 条或 ≥2 条）就进「需要你确认」，不瞎挑；
    ///   ④ 定下来的候选 BPM 与本地不符，记 NeedsReview（版本/倍速可能不同），仍进确认栏，不否掉；
    ///   ⑤ total==1 那条老规矩保留但降级为辅助：只回 1 条时同样要过格数硬门，过不了就进确认栏；
    ///   ⑥ 候选池被截断（hasMore），一律不自动采用（格数一致的候选可能落在第 101 条之后）；
    ///   ⑦ 本地格数读不到，硬门不可用，一律不自动采用（不拿没核对过的东西去写盘）。
    ///   ⑧ 格数硬门一条都没剩下时，才启用谱师回落（CharterHit）；命中一律判成 charter_match
    ///      进确认栏、绝不自动写入（TUF 的 creator 常常是上传者或审核者而不是谱师，不是指纹）；
    ///      本地 Charter 为空时不走这条路，只把这种情况计数出来给结果页看。
    /// artist / 谱师 / 曲名的打分只作软证据：用来给候选排序、给玩家看的理由、以及日志对账，
    /// 不再决定采不采用（title 单独匹配确实容易错：实测 summer dream 对上过另一个艺术家的同名曲）。
    /// 软告警照样回传（ReviewFlags / SoftFlags），界面看得到「格数对上了但署名不一样」。</summary>
    public static TufMatch Classify(TufChartInfo local, string displayName, TufLevelsResponse payload, string queryUsed) {
        var m = new TufMatch { QueryUsed = queryUsed ?? "" };
        if (local == null) local = new TufChartInfo();

        List<TufLevelRaw> results = payload != null ? payload.Results : null;
        if (results == null) results = new List<TufLevelRaw>();
        int total = payload != null && payload.Total.HasValue ? payload.Total.Value : results.Count;
        bool hasMore = payload != null && payload.HasMore;
        // 候选池完整 = 平台报的 total 与取回条数一致，且没有下一页。
        // total 缺失时一律当"不完整"（宁可不自动采用，也不拿半个池子当全池）。
        bool poolComplete = payload != null && payload.Total.HasValue && payload.Total.Value == results.Count && !hasMore;

        m.Total = total;
        m.Returned = results.Count;
        m.PoolComplete = poolComplete;

        long localTiles = local.HasTileCount ? local.TileCount : 0L;
        bool tileGate = local.HasTileCount && localTiles > 0L;
        m.TileGateAvailable = tileGate;
        m.LocalTileCount = tileGate ? localTiles : 0L;
        // 谱师回落能不能用：本地谱师字段读到了没有（为空就整条路不启用，结果页要看得见这个数）
        string localCharter = (local.Charter ?? "").Trim();
        bool charterGate = localCharter.Length > 0;
        m.CharterGateAvailable = charterGate;

        if (total == 0 || results.Count == 0) { m.MatchType = MatchNotFound; return m; }

        // 软证据打分 + 排序（只影响展示顺序与日志，不影响采不采用）
        string folderKey = Norm(local.FolderName);
        string displayKey = Norm(displayName);
        var scored = new List<TufCandidate>();
        foreach (TufLevelRaw raw in results) {
            TufCandidate c = CompactCandidate(raw);
            c.Score = ScoreCandidate(c, local, folderKey, displayKey);
            scored.Add(c);
        }
        scored.Sort(delegate (TufCandidate a, TufCandidate b) {
            if (a.Score != b.Score) return b.Score - a.Score;
            return (a.Id ?? 0).CompareTo(b.Id ?? 0);
        });
        m.Candidates = scored;

        // ⑥ 候选池被截断时，「格数一致」也说明不了问题 —— 真赢家可能在我们没抓的那一页上
        if (!poolComplete) {
            m.MatchType = MatchAmbiguous;
            m.Score = scored.Count > 0 ? scored[0].Score : (int?)null;
            m.Reason = "candidate pool truncated: total=" + total + " but only " + results.Count
                     + " fetched (hasMore=" + (hasMore ? "True" : "False") + ")";
            m.TopTieCount = CountTopTie(scored, scored.Count > 0 ? scored[0].Score : 0);
            return m;
        }

        // ⑦ 本地格数读不到 → 硬门不可用，不自动采用
        if (!tileGate) {
            m.MatchType = MatchAmbiguous;
            m.Score = scored.Count > 0 ? scored[0].Score : (int?)null;
            m.Reason = "本地格数读不到（.adofai 里没有完整可数的 angleData/pathData）—— 格数硬门不可用，不自动采用";
            return m;
        }

        // ① 格数硬门：对不上的一律排除
        var survivors = new List<TufCandidate>();
        foreach (TufCandidate c in scored) {
            if (c.HasTileCount && c.TileCount == localTiles) survivors.Add(c);
        }
        m.TileMatchCount = survivors.Count;

        if (survivors.Count == 0) {
            // 谱师回落：只在格数硬门一条都没剩下时启用（格数门已经命中时绝不再拿谱师去改判）。
            // 判据是 CharterHit（归一化只有那一处实现），命中一律判成 charter_match 进
            // 「需要你确认」，绝不自动写入 —— TUF 的 creator 常常是上传者或审核者而不是谱师，
            // 它不是指纹（格数才是），只够当「让玩家点一下」的线索。
            var charterHits = new List<TufCandidate>();
            if (charterGate) {
                foreach (TufCandidate c in scored) {
                    if (CharterHit(localCharter, c.Creator)) charterHits.Add(c);
                }
            } else {
                m.CharterSkippedEmpty = true;
            }
            m.CharterMatchCount = charterHits.Count;
            if (charterHits.Count > 0) {
                m.MatchType = MatchCharterMatch;
                m.NeedsReview = true;
                m.Score = charterHits[0].Score;
                m.TopTieCount = charterHits.Count;
                m.Candidates = CharterFirstList(charterHits, scored);
                m.Reason = CharterReasonText(localCharter, localTiles, scored, charterHits);
                return m;
            }
            m.MatchType = MatchAmbiguous;
            m.Score = scored.Count > 0 ? scored[0].Score : (int?)null;
            m.Reason = "候选里没有格数一致的（本地 " + localTiles + " 格；候选 " + DescribeTiles(scored) + "）";
            return m;
        }

        // ② 排除后恰好剩 1 条，自动采用 —— 「不会出错」那条路
        TufCandidate chosen;
        if (survivors.Count == 1) {
            chosen = survivors[0];
            m.MatchType = MatchAuto;
        } else {
            // ③ 第二条硬门：BPM 精确相等
            int exact = 0;
            TufCandidate bpmPick = null;
            foreach (TufCandidate c in survivors) {
                if (c.HasBpm && local.HasBpm && Math.Abs(c.Bpm - local.Bpm) <= BpmEpsilon) { exact++; bpmPick = c; }
            }
            if (exact == 1) {
                chosen = bpmPick;
                m.MatchType = MatchHighScore;   // 名字是历史遗留：现在的含义是「格数 + BPM 两道硬门定下来的」
            } else {
                m.MatchType = MatchAmbiguous;
                m.Score = survivors[0].Score;
                m.TopTieCount = survivors.Count;
                m.Reason = "格数一致的有 " + survivors.Count + " 条，BPM 精确相等"
                         + (exact == 0 ? "的一条都没有（本地 BPM " + BpmText(local.Bpm) + "）"
                                       : "的有 " + exact + " 条")
                         + " —— 定不下来，等你点";
                return m;
            }
        }

        string name = chosen.DifficultyName;
        int? diffId = chosen.DiffId;
        m.TufId = chosen.Id;
        m.DifficultyName = name ?? "";
        m.DiffId = diffId;
        m.Score = chosen.Score;
        m.ScoreReasons = chosen.ScoreReasons ?? new List<string>();
        m.Chosen = chosen;
        m.Rated = IsRanked(name, diffId);

        // 软证据：只记不拦
        m.ReviewFlags = ReviewFlagsOf(local, chosen);
        m.FlagClass = FlagClassOf(m.ReviewFlags);
        m.NoteOnly = m.FlagClass == "note_only";

        // ④ 定下来了但 BPM 不符 → 记 NeedsReview（版本/倍速可能不同），不否掉，交给玩家点
        if (local.HasBpm && chosen.HasBpm && Math.Abs(chosen.Bpm - local.Bpm) > BpmEpsilon) {
            m.BpmMismatch = true;
            m.NeedsReview = true;
            m.Reason = "格数一致（" + localTiles + " 格）但 BPM 不符：本地 " + BpmText(local.Bpm)
                     + " / TUF " + BpmText(chosen.Bpm) + "（版本或倍速可能不同），等你点";
        }

        if (m.Rated != true) m.MatchType = MatchUnranked;   // 对上了但 Unranked —— 不算「能评上」
        return m;
    }

    /// <summary>候选格数的一行摘要（"12、15、…"，最多 maxShown 个，超出接省略号），两边不带括号</summary>
    private static string TileListText(List<TufCandidate> scored, int maxShown) {
        if (scored == null || scored.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        foreach (TufCandidate c in scored) {
            if (shown >= maxShown) { sb.Append("…"); break; }
            if (shown > 0) sb.Append("、");
            sb.Append(c.HasTileCount ? c.TileCount.ToString(CultureInfo.InvariantCulture) : "?");
            shown++;
        }
        return sb.ToString();
    }

    /// <summary>候选格数的一行摘要（「没有候选 / 12 格 / 12、15 格…」），进 Reason 给玩家看</summary>
    private static string DescribeTiles(List<TufCandidate> scored) {
        if (scored == null || scored.Count == 0) return "（无）";
        return TileListText(scored, 4) + " 格）";
    }

    /// <summary>谱师回落命中时给玩家看的理由：固定话术在最前（一眼能认出这是「只凭谱师」那一档），
    /// 后面才是本地格数、本地谱师、候选格数与命中的那几条候选（带 TUF id 与档位名）供人对照。</summary>
    private static string CharterReasonText(string localCharter, long localTiles,
                                            List<TufCandidate> scored, List<TufCandidate> hits) {
        var sb = new StringBuilder();
        sb.Append("谱师相同、格数/ BPM 对不上，需要你确认（本地 ");
        sb.Append(localTiles.ToString(CultureInfo.InvariantCulture));
        sb.Append(" 格 / 谱师「");
        sb.Append(localCharter);
        sb.Append("」；候选 ");
        sb.Append(TileListText(scored, 4));
        sb.Append(" 格；谱师命中 ");
        sb.Append(hits.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append(" 条：");
        int shown = 0;
        foreach (TufCandidate c in hits) {
            if (shown >= 3) { sb.Append("…"); break; }
            if (shown > 0) sb.Append("、");
            sb.Append("#");
            sb.Append(c.Id.HasValue ? c.Id.Value.ToString(CultureInfo.InvariantCulture) : "?");
            if (!string.IsNullOrEmpty(c.DifficultyName)) { sb.Append(" "); sb.Append(c.DifficultyName); }
            shown++;
        }
        sb.Append("）");
        return sb.ToString();
    }

    /// <summary>确认栏列候选的顺序：谱师命中的排在最前面（其余保持原来的打分顺序跟在后面），
    /// 玩家点的时候先看到的那几条就是真正对得上谱师的。</summary>
    private static List<TufCandidate> CharterFirstList(List<TufCandidate> hits, List<TufCandidate> scored) {
        var outp = new List<TufCandidate>();
        foreach (TufCandidate c in hits) outp.Add(c);
        foreach (TufCandidate c in scored) {
            bool already = false;
            foreach (TufCandidate h in hits) {
                if (object.ReferenceEquals(h, c)) { already = true; break; }
            }
            if (!already) outp.Add(c);
        }
        return outp;
    }

    private static string BpmText(double v) {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int CountTopTie(List<TufCandidate> scored, int top) {
        int n = 0;
        foreach (TufCandidate c in scored) { if (c.Score == top) n++; }
        return n;
    }

    // ================= JSON 解析 =================

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        // 单个字段类型不对（比如某天 diffId 变成字符串）不让整份响应报废
        Error = delegate (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args) {
            args.ErrorContext.Handled = true;
        }
    };
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings);

    /// <summary>解析 /v2/database/levels 的响应。容忍"裸数组"这种老/新部署形态（照抄 search_levels 的防御分支）。</summary>
    public static TufLevelsResponse ReadLevelsJson(string json) {
        try {
            JToken tok = ParseToken(json);
            if (tok == null) return null;
            if (tok.Type == JTokenType.Array) {
                List<TufLevelRaw> arr = tok.ToObject<List<TufLevelRaw>>(Serializer) ?? new List<TufLevelRaw>();
                return new TufLevelsResponse { Results = arr, Total = arr.Count, HasMore = false };
            }
            return tok.ToObject<TufLevelsResponse>(Serializer);
        } catch (Exception e) {
            Lg.Info("TUF：解析 levels 响应失败 —— " + e.GetType().Name + ": " + e.Message);
            return null;
        }
    }

    private static JToken ParseToken(string json) {
        if (string.IsNullOrEmpty(json)) return null;
        if (json[0] == '\uFEFF') json = json.Substring(1);   // 有些接口会带 BOM，JToken.Parse 会当场炸
        return JToken.Parse(json);
    }

    // ================= 正文解压 =================

    /// <summary>取响应正文并按需解压（与 Updater 里那套一模一样）。
    /// 为什么不能直接用 downloadHandler.text：手动设过请求头之后 Unity 不再自动解压，
    /// gzip 响应会变成空串（表现是"HTTP 200 但内容为空"）。所以直接处理原始字节。</summary>
    private static string DecodeBody(UnityWebRequest req) {
        byte[] raw = req.downloadHandler != null ? req.downloadHandler.data : null;
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
        if (raw.Length > 2 && raw[0] == 0x1f && raw[1] == 0x8b) {   // gzip 魔数
            string s = TryInflate(raw, true);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        if (raw.Length > 2 && raw[0] == 0x78) {                     // zlib
            string s = TryInflate(raw, false);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        try { return Encoding.UTF8.GetString(raw); } catch { return null; }
    }

    private static string TryInflate(byte[] raw, bool gzip) {
        try {
            using (var ms = new MemoryStream(raw))
            using (Stream ds = gzip ? (Stream)new GZipStream(ms, CompressionMode.Decompress)
                                    : new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream()) {
                var buf = new byte[16384];
                int n;
                while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                byte[] bytes = outMs.ToArray();
                if (bytes.Length == 0) return null;
                return Encoding.UTF8.GetString(bytes);
            }
        } catch { return null; }
    }

    private static string Trim(string s, int n) {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n) + "…";
    }


    private static void LogException(string msg, Exception e) {
        try { Lg.Ex("" + msg, e); } catch { }
    }

    // ================= 自检（纯函数，零副作用，零网络）=================

    /// <summary>自检报告字符串：全过返回 "PASS n/n"，否则返回 "FAIL k/n: 用例名 ｜ 用例名"。
    /// 拆成纯字符串返回是为了能被外部直接调用核对（启动日志那条也用它）。</summary>
    public static string SelfTestReport() {
        var fails = new List<string>();
        int n = 0;
        Action<string, bool> ck = delegate (string name, bool ok) { n++; if (!ok) fails.Add(name); };

        // ---------- 硬规矩①：曲名子串匹配 → 只在 total==0 时用纯标题回退 ----------
        List<string> f1 = QueryFallbacks("Plum - Skyscape");
        ck("回退①：'Plum - Skyscape' 应剥出纯标题 'Skyscape'", f1.Contains("Skyscape"));
        ck("回退①：绝不把原串再查一遍", !f1.Contains("Plum - Skyscape"));
        List<string> f2 = QueryFallbacks("Skyscape (Orgel Ver.)");
        ck("回退②：'Skyscape (Orgel Ver.)' 应剥出 'Skyscape'", f2.Contains("Skyscape"));
        ck("回退：本来就是纯标题时不该产生任何回退串", QueryFallbacks("Skyscape").Count == 0);
        ck("回退：太短的标题不许当回退串（<3 字）", QueryFallbacks("Ab - Cd").Count == 0);

        // ---------- 格数算法（主判据第一道硬门的底座，口径实测标定过）----------
        long tiles; string tileSrc;
        bool tk1 = TryCountTiles("{\"angleData\":[0,90,999,45,180,999.0,270]}", out tiles, out tileSrc)
                   && tiles == 5 && tileSrc == "angleData";
        ck("格数算法：angleData 条目数 − 999 哨兵数（999.0 这种写法也要算）", tk1);
        bool tk2 = TryCountTiles("{\"pathData\":\"RR!5ARR\"}", out tiles, out tileSrc)
                   && tiles == 6 && tileSrc == "pathData";
        ck("格数算法：pathData 字符数 − '!' 标记数（7 字符含 1 个标记 = 6 格）", tk2);
        bool tk3 = !TryCountTiles("{\"settings\":{\"song\":\"x\"}}", out tiles, out tileSrc);
        ck("格数算法：两条路径都读不到 → 硬门不可用（不是算成 0 格）", tk3);

        // ---------- 主判据：格数 + BPM 两道硬门 ----------
        var local = new TufChartInfo {
            Song = "Komorebi", Artist = "m-takeu", Author = "charterGuy",
            LevelAuthorName = "", Charter = "charterGuy", Bpm = 103d, HasBpm = true, FolderName = "Komorebi",
            TileCount = 800L, HasTileCount = true, TileSource = "angleData"
        };

        // ① 格数对不上的一律排除
        TufLevelsResponse tileBad = Envelope(1, false, Raw(1, "Komorebi", "m-takeu", "charterGuy", "P6", 799, 103d, "P6"));
        TufMatch mTileBad = Classify(local, "", tileBad, "Komorebi");
        ck("格数硬门①：格数对不上的候选被排除（本地 800 / 候选 799）",
            !mTileBad.AutoRated && !mTileBad.Auto && mTileBad.TileMatchCount == 0);
        ck("格数硬门①：格数对不上但谱师对得上 → 走谱师回落（仍旧不是自动采用）",
            mTileBad.MatchType == MatchCharterMatch && mTileBad.CharterMatchCount == 1);
        ck("格数硬门①：排除时把本地与候选的格数都写进理由（玩家看得到为什么）",
            mTileBad.Reason.Contains("800") && mTileBad.Reason.Contains("799"));

        // ② 排除后恰好剩 1 条 → 自动采用（「不会出错」那条路）；artist 只是软证据，不拦
        TufLevelsResponse tileOne = Envelope(2, false,
            Raw(2, "Komorebi", "satie", "uploader", "P6", 800, 103d, "P6"),      // 格数+BPM 都对，artist/谱师完全不同
            Raw(3, "Komorebi", "m-takeu", "charterGuy", "P8", 799, 103d, "P8"));
        TufMatch mTileOne = Classify(local, "", tileOne, "Komorebi");
        ck("格数硬门②：排除后恰好剩 1 条 → 自动采用",
            mTileOne.MatchType == MatchAuto && mTileOne.TufId == 2 && mTileOne.TileMatchCount == 1 && mTileOne.AutoRated);
        ck("主判据：artist/谱师对不上不再拦自动采用（已降级为软证据）",
            !mTileOne.NeedsReview && mTileOne.SoftFlags && mTileOne.FlagClass == "needs_review");

        // ③ 剩多条 → BPM 精确相等定第二条
        TufLevelsResponse bpmPick = Envelope(2, false,
            Raw(4, "Komorebi", "m-takeu", "charterGuy", "P6", 800, 90d, "P6"),
            Raw(5, "Komorebi", "m-takeu", "charterGuy", "P8", 800, 103d, "P8"));
        TufMatch mBpmPick = Classify(local, "", bpmPick, "Komorebi");
        ck("BPM 硬门③：格数一致的有 2 条 → 由 BPM 精确相等定下唯一条",
            mBpmPick.MatchType == MatchHighScore && mBpmPick.TufId == 5 && mBpmPick.TileMatchCount == 2
            && !mBpmPick.NeedsReview && mBpmPick.AutoRated);

        TufLevelsResponse bpmTie = Envelope(2, false,
            Raw(6, "Komorebi", "m-takeu", "charterGuy", "P6", 800, 103d, "P6"),
            Raw(7, "Komorebi", "m-takeu", "charterGuy", "P8", 800, 103d, "P8"));
        TufMatch mBpmTie = Classify(local, "", bpmTie, "Komorebi");
        ck("BPM 硬门③：格数一致且 BPM 也并列 → ambiguous（不瞎挑一个）",
            mBpmTie.MatchType == MatchAmbiguous && !mBpmTie.AutoRated && mBpmTie.TileMatchCount == 2);

        TufLevelsResponse bpmNone = Envelope(2, false,
            Raw(8, "Komorebi", "m-takeu", "charterGuy", "P6", 800, 90d, "P6"),
            Raw(9, "Komorebi", "m-takeu", "charterGuy", "P8", 800, 150d, "P8"));
        TufMatch mBpmNone = Classify(local, "", bpmNone, "Komorebi");
        ck("BPM 硬门③：格数一致但一条 BPM 都对不上 → ambiguous（不否掉，等玩家点）",
            mBpmNone.MatchType == MatchAmbiguous && !mBpmNone.AutoRated && mBpmNone.TileMatchCount == 2);

        // ④ 定下来了但 BPM 不符 → 记 NeedsReview（版本/倍速可能不同），不否掉
        TufLevelsResponse bpmBad = Envelope(1, false, Raw(10, "Komorebi", "m-takeu", "charterGuy", "P6", 800, 90d, "P6"));
        TufMatch mBpmBad = Classify(local, "", bpmBad, "Komorebi");
        ck("BPM 硬门④：格数一致但 BPM 不符 → NeedsReview + BpmMismatch（仍进确认栏，不否掉）",
            mBpmBad.MatchType == MatchAuto && mBpmBad.AutoRated && mBpmBad.BpmMismatch && mBpmBad.NeedsReview);

        // ⑤ total==1 降级为辅助：同样要过格数硬门
        TufLevelsResponse singleBad = Envelope(1, false, Raw(11, "Komorebi", "m-takeu", "charterGuy", "P6", 900, 103d, "P6"));
        TufMatch mSingleBad = Classify(local, "", singleBad, "Komorebi");
        ck("total==1 降级为辅助：只回 1 条也要过格数硬门，过不了不自动采用（谱师对得上也只有确认栏）",
            !mSingleBad.AutoRated && !mSingleBad.Auto && mSingleBad.MatchType == MatchCharterMatch);

        // ⑦ 本地格数读不到 → 硬门不可用 → 一律不自动采用
        var noTile = new TufChartInfo {
            Song = "Komorebi", Artist = "m-takeu", Charter = "charterGuy",
            Bpm = 103d, HasBpm = true, FolderName = "Komorebi"
        };
        TufMatch mNoTile = Classify(noTile, "", Envelope(1, false,
            Raw(12, "Komorebi", "m-takeu", "charterGuy", "P6", 800, 103d, "P6")), "Komorebi");
        ck("格数硬门不可用（本地读不到格数）→ 一律不自动采用",
            mNoTile.MatchType == MatchAmbiguous && !mNoTile.AutoRated && !mNoTile.TileGateAvailable);

        // ---------- 谱师回落（只在格数硬门一条都没剩下时启用；一律进确认栏）----------
        ck("谱师归一化：任一词相等就算命中（'Han_rang & kyogo' 与 'kyogo'）",
            CharterHit("Han_rang & kyogo", "kyogo"));
        const string paddedName = "  Team Ciforance ";
        const string plainName = "team ciforance";
        ck("谱师归一化：大小写与首尾空白不算差异", CharterHit(paddedName, plainName));
        ck("谱师归一化：带括号的团队名也拆（'NeoMoyelle(Video Creator)' 与 'Video Creator'）",
            CharterHit("NeoMoyelle(Video Creator)", "Video Creator"));
        const string anotherCharter = "satie";
        ck("谱师归一化：确实不同的两个人不算命中", !CharterHit("m-takeu", anotherCharter));
        ck("谱师归一化：任一边为空一律不算命中（空值不是证据）",
            !CharterHit("", "satie") && !CharterHit("satie", "") && !CharterHit("   ", "satie"));
        ck("谱师归一化：按分隔符拆词（'Pharah & NumbEr07' = 2 个词）", CharterWords("Pharah & NumbEr07").Count == 2);

        var lc = new TufChartInfo {
            Song = "Myths You Forgot", Artist = "PLORALD", Author = "Han_rang & kyogo",
            Charter = "Han_rang & kyogo", FolderName = "Myths You Forgot", Bpm = 103d, HasBpm = true,
            TileCount = 800L, HasTileCount = true, TileSource = "angleData"
        };
        TufLevelsResponse charterOne = Envelope(1, false,
            Raw(21, "Myths You Forgot", "PLORALD", "kyogo", "P6", 810, 150d, "P6"));
        TufMatch mCharter = Classify(lc, "", charterOne, "Myths You Forgot");
        ck("谱师回落：格数一条都没剩下但谱师命中 → charter_match",
            mCharter.MatchType == MatchCharterMatch && mCharter.CharterMatchCount == 1);
        ck("谱师回落：绝不自动采用（AutoRated 假、Auto 假、NeedsReview 真）",
            !mCharter.Auto && !mCharter.AutoRated && mCharter.NeedsReview);
        ck("谱师回落：理由带固定话术「谱师相同、格数/ BPM 对不上，需要你确认」",
            mCharter.Reason.IndexOf("谱师相同、格数/ BPM 对不上，需要你确认") >= 0);
        ck("谱师回落：理由里带上本地谱师与命中的候选 id（玩家能对着看）",
            mCharter.Reason.IndexOf("Han_rang") >= 0 && mCharter.Reason.IndexOf("#21") >= 0);

        TufLevelsResponse charterMany = Envelope(2, false,
            Raw(22, "Myths You Forgot", "a", "kyogo", "P6", 810, 150d, "P6"),
            Raw(23, "Myths You Forgot", "b", "Han_rang", "P7", 820, 150d, "P7"));
        TufMatch mCharterMany = Classify(lc, "", charterMany, "Myths You Forgot");
        ck("谱师回落：命中多条 → 全进确认栏（候选一条不少，命中的排最前）",
            mCharterMany.MatchType == MatchCharterMatch && mCharterMany.CharterMatchCount == 2
            && mCharterMany.Candidates.Count == 2 && mCharterMany.Candidates[0].Id == 22
            && mCharterMany.TopTieCount == 2);

        TufLevelsResponse charterNone = Envelope(2, false,
            Raw(24, "Myths You Forgot", "a", "someoneelse", "P6", 810, 150d, "P6"),
            Raw(25, "Myths You Forgot", "b", "another", "P7", 820, 150d, "P7"));
        TufMatch mCharterNone = Classify(lc, "", charterNone, "Myths You Forgot");
        ck("谱师回落：一条都没命中 → 维持原来那一档（ambiguous）",
            mCharterNone.MatchType == MatchAmbiguous && mCharterNone.CharterMatchCount == 0
            && !mCharterNone.NeedsReview && !mCharterNone.AutoRated);

        var noCharter = new TufChartInfo {
            Song = "Myths You Forgot", Artist = "PLORALD", Author = "", Charter = "",
            FolderName = "Myths You Forgot", Bpm = 103d, HasBpm = true,
            TileCount = 800L, HasTileCount = true, TileSource = "angleData"
        };
        TufMatch mNoCharter = Classify(noCharter, "", charterOne, "Myths You Forgot");
        ck("谱师回落：本地谱师为空 → 这条路不启用（保持 ambiguous 并记下这件事）",
            mNoCharter.MatchType == MatchAmbiguous && !mNoCharter.CharterGateAvailable
            && mNoCharter.CharterSkippedEmpty && !mNoCharter.AutoRated && mNoCharter.CharterMatchCount == 0);

        TufLevelsResponse tileHit = Envelope(1, false,
            Raw(26, "Myths You Forgot", "a", "kyogo", "P6", 800, 103d, "P6"));
        TufMatch mTileHit = Classify(lc, "", tileHit, "Myths You Forgot");
        ck("格数门命中时不许谱师抢答：照样是 auto，不是 charter_match",
            mTileHit.MatchType == MatchAuto && mTileHit.CharterMatchCount == 0 && mTileHit.AutoRated);

        TufMatch mCharterTrunc = Classify(lc, "", Envelope(2, true,
            Raw(27, "Myths You Forgot", "a", "kyogo", "P6", 810, 150d, "P6"),
            Raw(28, "Myths You Forgot", "b", "kyogo", "P7", 820, 150d, "P7")), "Myths You Forgot");
        ck("谱师回落不越过截断那一档：池子不完整仍旧只判 ambiguous",
            mCharterTrunc.MatchType == MatchAmbiguous && mCharterTrunc.CharterMatchCount == 0);

        // ⑥ 候选池截断（hasMore）—— 这条不能松：格数一致的候选可能落在第 101 条之后
        TufLevelsResponse trunc = Envelope(2, true,
            Raw(13, "Komorebi", "m-takeu", "charterGuy", "P6", 800, 103d, "P6"),
            Raw(14, "Komorebi", "other", "zzz", "P8", 800, 150d, "P8"));
        TufMatch mTrunc = Classify(local, "", trunc, "Komorebi");
        ck("候选池截断：hasMore==true → 一律不自动采用（哪怕格数一致）",
            mTrunc.MatchType == MatchAmbiguous && !mTrunc.AutoRated && !mTrunc.PoolComplete);

        // ---------- 判定表其余部分 ----------
        TufMatch mNone = Classify(local, "", new TufLevelsResponse { Results = new List<TufLevelRaw>(), Total = 0, HasMore = false }, "x");
        ck("total==0 → not_found", mNone.MatchType == MatchNotFound);

        TufLevelsResponse unr = Envelope(1, false, Raw(15, "Komorebi", "m-takeu", "charterGuy", "Unranked", 800, 103d, "Unranked", 0));
        TufMatch mUnr = Classify(local, "", unr, "Komorebi");
        ck("Unranked（diffId 0）→ matched_unranked 且不算已评级",
            mUnr.MatchType == MatchUnranked && mUnr.Rated != true && !mUnr.AutoRated);

        ck("Censored 等特殊档按试算口径算「已评级」", IsRanked("Censored", 1000) && IsRanked("Marathon", 7) && !IsRanked("Unranked", 0));

        // ---------- 谱师字段（.adofai 的 author / levelAuthorName）----------
        const string adofai = "{\"settings\":{\"version\":12,\"song\":\"<size=75>Plum - Skyscape</size>\"," +
            "\"artist\":\"<color=#ffd999>Plum</color>\",\"author\":\"OldAuthor\",\"levelAuthorName\":\"NewCharter\"," +
            "\"bpm\":133.5,\"offset\":0},\"angleData\":[0,1,2]}";
        var info = new TufChartInfo();
        ParseChartText(adofai, info);
        ck("谱面头：song 原值保留、清洗值去富文本", info.SongRaw == "<size=75>Plum - Skyscape</size>" && info.Song == "Plum - Skyscape");
        ck("谱面头：artist 是乐曲艺术家（去富文本）", info.Artist == "Plum");
        ck("谱面头：author / levelAuthorName 都读到", info.Author == "OldAuthor" && info.LevelAuthorName == "NewCharter");
        ck("谱师口径：levelAuthorName 优先", info.Charter == "NewCharter");
        ck("谱面头：bpm 读到 133.5", info.HasBpm && Math.Abs(info.Bpm - 133.5d) < 0.0001d);
        ck("谱面头：顺手把本地格数算出来（3 格，angleData）",
            info.HasTileCount && info.TileCount == 3L && info.TileSource == "angleData");

        var info2 = new TufChartInfo();
        ParseChartText("{\"settings\":{\"song\":\"OnlyOld\",\"author\":\"OldOnly\"}}", info2);
        ck("谱师口径：没有 levelAuthorName 时回落 author", info2.Charter == "OldOnly");

        // ---------- 艺术家比较 / 打分（软证据，仍然要算对：排序与日志都靠它）----------
        ck("艺术家比较：括号/词序互换要认得出同一人",
            ArtistsCompatible("Camellia (かめりあ)", "かめりあ (Camellia)"));
        ck("艺术家比较：'Plum' 与 'Plum - Melodic Artist' 算兼容",
            ArtistsCompatible("Plum", "Plum - Melodic Artist"));
        ck("艺术家比较：确实不同的两个人不能算兼容", !ArtistsCompatible("m-takeu", "satie"));

        var cand = new TufCandidate { Song = "Komorebi", Artist = "m-takeu", Creator = "charterGuy", Bpm = 103d, HasBpm = true };
        int score = ScoreCandidate(cand, local, Norm("Komorebi"), "");
        ck("打分：artist(+2) + creator(+3) + bpm(+1) = 6", score == 6);

        var cand2 = new TufCandidate { Song = "Komorebi", Artist = "satie", Creator = "charterGuy", Bpm = 90d, HasBpm = true };
        int score2 = ScoreCandidate(cand2, local, "", "");
        ck("打分：artist 不同 + bpm 差得远 → 只剩 creator 的 3 分", score2 == 3);

        List<string> flags = ReviewFlagsOf(local, new TufCandidate { Song = "Komorebi", Artist = "satie", Creator = "uploader" });
        ck("一致性告警：artist 对不上 → artist_mismatch + needs_review（软证据，只记不拦）",
            flags.Contains("artist_mismatch") && FlagClassOf(flags) == "needs_review");

        // ---------- 判定器不许把"对上了"和"自动写"混为一谈 ----------
        ck("AutoRated 只在格数硬门定下来且已评级时为真",
            mTileOne.AutoRated && mBpmPick.AutoRated && !mTileBad.AutoRated && !mTrunc.AutoRated
            && !mUnr.AutoRated && !mNone.AutoRated && !mNoTile.AutoRated
            && !mCharter.AutoRated && !mCharterMany.AutoRated && !mCharterNone.AutoRated
            && !mNoCharter.AutoRated);

        if (fails.Count == 0) return "PASS " + n + "/" + n;
        return "FAIL " + fails.Count + "/" + n + ": " + string.Join(" ｜ ", fails.ToArray());
    }

    /// <summary>启动时把主判据（格数 + BPM 硬门）与判定表跑一遍写进日志。写法与 `Updater.RunVersionSelfTest()` 一致：
    /// 这些都是编译器查不出来的逻辑（写错了只会"某个谱面被悄悄写错标记"）。
    /// 纯函数、零网络、零副作用，启动一次开销可忽略。</summary>
    public static void RunSelfTest() {
        try {
            string r = SelfTestReport();
            if (r.StartsWith("PASS", StringComparison.Ordinal)) {
                Lg.Info("TUF 判定自检：" + r + "（主判据=格数 + BPM 两道硬门：格数对不上必须排除 / "
                    + "剩 1 条才自动采用 / BPM 不符记 NeedsReview / 截断一律不自动采用 / 回退只在 total==0 触发）");
            } else {
                Lg.Info("TUF 判定自检：✘ " + r);
            }
        } catch (Exception e) {
            LogException("TUF 判定自检失败", e);
        }
    }

    // ---- 自检用的小工厂 ----

    private static TufLevelRaw Raw(long id, string song, string artist, string creator, string suffix,
                                   long tiles, double bpm, string diffName, int? diffId = null) {
        return new TufLevelRaw {
            Id = id, Song = song, Artist = artist, Creator = creator, Suffix = suffix,
            Bpm = bpm, TileCountLower = tiles,
            DiffId = diffId,
            Difficulty = new TufDifficultyRef { Id = diffId, Name = diffName }
        };
    }

    private static TufLevelsResponse Envelope(int total, bool hasMore, params TufLevelRaw[] rows) {
        var r = new TufLevelsResponse {
            Results = new List<TufLevelRaw>(rows),
            Total = total,
            HasMore = hasMore,
            Limit = ResultLimit
        };
        return r;
    }
}
