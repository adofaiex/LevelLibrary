using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace LevelLibrary;

// ============================================================================
// TUF 社区定级 —— 批量评级流程（判定 / 落盘层）
// 与 TufClient 的分工：
//   · TufClient = 只读客户端：读 .adofai 头、排队查 TUF、给判定 TufMatch。它不碰玩家数据。
//   · TufBatch  = 批量流程：扫库 → 一条条查 → 分三栏 → 按规矩写 marks.txt。
// 【三栏】（队长口径，见下）
//   ① 自动写入   主判据「格数 + BPM」两道硬门定下来（TufMatch.AutoRated）且没有 NeedsReview → 写档位名
//   ② 需要你确认 BPM 对不上（NeedsReview）/ 格数一致的候选定不下来 / 池子截断 / 本地格数读不到；
//                特殊档（Censored/Qq/P0/Marathon/Impossible）也放这一栏，但不可点写。
//                谱师回落命中的那档（charter_match）同样只进这一栏 —— TUF 的 creator 常常是
//                上传者或审核者而不是谱师，它不是指纹（格数才是），只够当让玩家点一下的线索。
//   ③ 查不到     TUF 没有可用定级（查不到 / Unranked / 网络失败）→ 本地 AutoRating 兜底
// 【主判据】格数 + BPM（原话：「用谱面的格子数和 bpm 进行比对，这样不会出错」）：
//   · 候选里格数对不上的一律排除（不是扣分）；排除后恰好剩 1 条，自动采用；
//   · 剩多条时用 BPM 精确相等定第二条，定不下就进「需要你确认」；
//   · 定下来但 BPM 不符，记 NeedsReview（版本/倍速可能不同），进确认栏，不否掉；
//   · total==1 降级为辅助：只回 1 条也要过格数硬门，过不了就进确认栏；
//   · 池子截断（hasMore），一律不自动采用；本地格数读不到，硬门不可用，一律不自动采用。
// 【口径变更】artist / 谱师 / 曲名对不上不再拦自动写入 —— 它们已降级为软证据
// （只进 ReviewFlags / SoftFlags，给玩家看理由、给日志对账）。理由：title 单独匹配确实容易错
// （实测 summer dream 对上过另一个艺术家的同名曲），而格数 + BPM 才是真正不会错的指纹。
// 【写盘四条铁规矩】全部收在 WriteMark 一个入口里，别在别处再 MarkStore.Set 一次：
//   ① 只写「主标记为空」的谱面 —— 玩家自己标过的一条都不覆盖
//   ② Censored / Qq / P0 / Marathon / Impossible / Unranked 不写（TierWritable）
//   ③ 只写本机真有档位图标的档位（IconExists）—— 否则会写出一条列表里看不见的标记
//   ④ 写 marks.txt 之前先备份到备份库（先找这台机器上已有的那个库，找不到退回 mod\marks_backup）；
//      备份失败就不写
// 【可中断可续】会话状态写在 mod\tuf_batch.json：已处理过的谱面不重查，待确认的那一栏
// 原样留着，下次点「继续评级」接着来（网络请求是限速的，重跑一遍很贵）。
// 【来源可追溯】每写成一条都记进 mod\tuf_source.txt（相对路径 \t community|local \t 档位名），
// 界面上「社区定级(TUF) / 本地估算(AutoRating)」就是按它显示出来的。
// ============================================================================

/// <summary>批量流程里的一条谱面：落到哪一栏、写成了什么、为什么。</summary>
public class TufBatchItem {
    /// <summary>谱面主文件（绝对路径；marks.txt 的键就是它）</summary>
    public string File = "";
    /// <summary>显示名（列表里那个曲名，没有就用文件名）</summary>
    public string Title = "";
    /// <summary>三栏之一：TufBatch.BucketAuto / BucketConfirm / BucketMissing</summary>
    public string Bucket = "";
    /// <summary>写进 marks.txt 的档位名（社区或本地）</summary>
    public string Tier = "";
    /// <summary>TUF 判定档位名（confirm 栏显示「TUF 说是 X」用）</summary>
    public string TufTier = "";
    public long? TufId;
    public int? DiffId;
    /// <summary>TufMatch.MatchType（对账用）</summary>
    public string MatchType = "";
    /// <summary>本地谱师为空，就没走「谱师回落」这一层（结果页的诊断数字）</summary>
    public bool CharterSkippedEmpty;
    /// <summary>为什么落到这一栏 / 为什么没写</summary>
    public string Reason = "";
    /// <summary>真写进 marks.txt 了没有（没写成的也在列表里，理由写在 Reason）</summary>
    public bool Written;
    /// <summary>来源：TufBatch.SourceCommunity / SourceLocal（只有写成了才有意义）</summary>
    public string Source = "";
    /// <summary>本地 AutoRating 的档位与 star（查不到那一栏）</summary>
    public string LocalTier = "";
    public double LocalStar;
    /// <summary>候选池（已按 score 降序，来自 TufMatch.Candidates）</summary>
    public List<TufCandidate> Candidates = new List<TufCandidate>();

    /// <summary>来源的中文说法（界面直接用）</summary>
    public string SourceLabel {
        get {
            if (Source == TufBatch.SourceCommunity) return "社区定级";
            if (Source == TufBatch.SourceLocal) return "本地估算";
            return "";
        }
    }
}

/// <summary>批量会话状态（持久化到 mod\tuf_batch.json，用于「中断后继续」）</summary>
public class TufBatchState {
    public string startedAt = "";
    public string finishedAt = "";
    /// <summary>这次扫到多少张谱面（递归 backup\ 下的 .adofai，一张谱面取一个主文件）</summary>
    public int Scanned;
    /// <summary>其中因为玩家已经标过而跳过的</summary>
    public int SkippedMarked;
    /// <summary>依次列出被跳过的（相对路径），界面/日志能核对</summary>
    public List<string> Skipped = new List<string>();
    /// <summary>格数硬门一条都没命中、但走了「谱师回落」进确认栏的条数（结果页的诊断数字）</summary>
    public int CharterMatched;
    /// <summary>格数硬门一条都没命中、却因为本地谱师字段为空所以没走「谱师回落」的条数（结果页的诊断数字）</summary>
    public int CharterEmpty;
    /// <summary>这次（含历史续跑）查了多少条</summary>
    public int Queried;
    /// <summary>本轮要查多少条（进度条分母；续跑时不重查已处理过的）</summary>
    public int TodoTotal;
    /// <summary>本轮已经查完多少条（进度条分子）</summary>
    public int TodoDone;
    /// <summary>已处理完的谱面（相对路径）：续跑时不再重查</summary>
    public List<string> Resolved = new List<string>();
    public List<TufBatchItem> Auto = new List<TufBatchItem>();
    public List<TufBatchItem> Pending = new List<TufBatchItem>();
    public List<TufBatchItem> Missing = new List<TufBatchItem>();
    /// <summary>上次备份 marks.txt 落到了哪里（界面/日志显示）</summary>
    public string BackupFile = "";

    /// <summary>还有没干完的活（待确认的条目）—— 设置页用它决定按钮写「开始评级」还是「继续评级」</summary>
    public bool HasWork { get { return Pending != null && Pending.Count > 0; } }
}

/// <summary>TUF 批量评级：扫库 / 判定落栏 / 写 marks.txt（唯一写入口）/ 会话状态。</summary>
public static class TufBatch {

    // ================= 常量 =================

    public const string BucketAuto = "auto";        // 自动写入（社区定级）
    public const string BucketConfirm = "confirm";  // 需要你确认
    public const string BucketMissing = "missing";  // 查不到 → 本地估算兜底

    /// <summary>来源：社区定级（TUF）</summary>
    public const string SourceCommunity = "community";
    /// <summary>来源：本地估算（AutoRating，回落）</summary>
    public const string SourceLocal = "local";

    /// <summary>写 marks.txt 前的备份落点：先找这台机器上已有的「DSH备份库」（这台在 D 盘），
    /// 找不到就退回 mod 自己的 marks_backup —— 备份失败按规矩不写，所以必须有个总能用的落点。</summary>
    public static string BackupLibDir() {
        try {
            foreach (System.IO.DriveInfo d in System.IO.DriveInfo.GetDrives()) {
                if (d == null || !d.IsReady) continue;
                string lib = System.IO.Path.Combine(d.RootDirectory.FullName, "DSH备份库", "LevelLibrary");
                if (System.IO.Directory.Exists(lib)) return lib;
            }
        } catch { }
        string root = ModDir();
        return string.IsNullOrEmpty(root) ? null : System.IO.Path.Combine(root, "marks_backup");
    }

    private const string SessionFile = "tuf_batch.json";
    private const string SourceFile = "tuf_source.txt";

    /// <summary>按规矩永不写入的档位（大小写不敏感）。Unranked = 平台没定级，其余是特殊档。</summary>
    private static readonly string[] NeverWriteTiers = {
        "Unranked", "Censored", "Qq", "P0", "Marathon", "Impossible"
    };

    // ================= 会话状态 =================

    /// <summary>当前会话（界面直接读它渲染）</summary>
    public static TufBatchState State = new TufBatchState();

    /// <summary>正在跑（扫库 / 查询 / 收尾）</summary>
    public static bool Running;
    /// <summary>玩家按了中断</summary>
    public static bool CancelRequested;
    /// <summary>这一轮跑完了（界面切到三栏视图）</summary>
    public static bool Finished;
    /// <summary>这一轮是自动流程（批量导入 → 检验重复 → 评级 的第三段）：不给玩家做选择 ——
    /// 定不下来的条目降级给本地估算，跑完不翻"待确认"那一页，只留一句提示。</summary>
    public static bool AutoMode;

    /// <summary>玩家已经标过的谱面数量（扫库时统计，界面显示）</summary>
    public static int SkippedMarkedCount { get { return State != null ? State.SkippedMarked : 0; } }

    /// <summary>有没有「可以接着做」的活：待确认的一栏非空</summary>
    public static bool HasPending { get { return State != null && State.Pending != null && State.Pending.Count > 0; } }

    // ================= 测试缝（离线自检 / 自检用；正常运行时两个都是 null）=================
    // 光靠"编译过了"验不了写盘规矩（只写空位 / 不覆盖 / 备份失败就不写），要真跑一遍又不能拿洛洛
    // 真实的 marks.txt 和 D:\DSH备份库 去试。所以 mod 目录与备份库目录都留一个可替换的口子，
    // 自检脚本把它指到临时目录，就能把整套写盘路径跑成真的。

    /// <summary>临时替换 mod 目录（null = 用 Main.ModEntry.Path）</summary>
    public static string ModDirOverride;
    /// <summary>临时替换备份库目录（null = 用 BackupLibDir() 找到的那个库）</summary>
    public static string BackupLibOverride;

    /// <summary>当前 mod 目录</summary>
    public static string ModDir() {
        if (!string.IsNullOrEmpty(ModDirOverride)) return ModDirOverride;
        try { return Main.ModEntry != null ? Main.ModEntry.Path : null; } catch { return null; }
    }

    /// <summary>当前备份库目录（写 marks.txt 之前的备份落点）</summary>
    public static string BackupLib() {
        return string.IsNullOrEmpty(BackupLibOverride) ? BackupLibDir() : BackupLibOverride;
    }

    // ================= 扫库 =================

    /// <summary>编辑器滚动备份的文件名（<c>20250817_144742589_BACKUP.adofai</c>）：
    /// 这种目录里上千个时间戳副本不是独立谱面（它们就是父目录那一张的副本），一张都不该评。</summary>
    private static readonly System.Text.RegularExpressions.Regex RollingBackupRe =
        new System.Text.RegularExpressions.Regex(@"^\d{8}_\d+_BACKUP$", System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>这一轮要评的谱面主文件清单 —— 直接取谱面库列表的那份结果（LevelScanner.Entries）。
    /// 为什么不在这里自己找目录：列表读的地方和批量扫的地方一旦是两个出处，就必然漂。
    /// 实测踩过：批量把扫描根拼成 mod\backup\，而玩家重装后那个目录根本不存在
    /// （谱面全在 recorded.txt 指的库根里，可能在 mod 目录之外），
    /// 于是列表里有谱、批量却「扫描 0 个谱面文件夹」—— 一张都评不到。
    /// 列表加载（LevelScanner.Refresh）已经把「哪个文件夹算一张谱面」定好了：
    /// 玩家导入目录 + recorded 恢复 + 备份库兜底，容器与重复都在那边消化掉。
    /// 这里只做两件不改变集合归属的事：去掉盘上已经不存在的、去重、跳过主文件本身是滚动备份的。
    /// folderCount = 列表里带主文件的条目数；skippedRolling = 其中因为是滚动备份被跳过的条数。</summary>
    public static List<string> ScanTargets(out int folderCount, out int skippedRolling) {
        // 列表里的条目可能已经从盘上删了（玩家改过库）—— 不存在的直接不算，别拿它去查网
        var live = new List<LevelEntry>();
        var entries = LevelScanner.Entries;
        if (entries != null) {
            for (int i = 0; i < entries.Count; i++) {
                LevelEntry e = entries[i];
                if (e == null || string.IsNullOrEmpty(e.FilePath)) continue;
                try { if (!File.Exists(e.FilePath)) continue; } catch { continue; }
                live.Add(e);
            }
        }
        return TargetsFromEntries(live, out folderCount, out skippedRolling);
    }

    /// <summary>把「谱面库列表条目」变成「要评的主文件清单」。纯函数（自检直接喂假列表）。
    /// 顺序沿用列表的顺序（列表已经按玩家选的排序排好了，进度条跟着列表走更自然）。</summary>
    public static List<string> TargetsFromEntries(IList<LevelEntry> entries, out int folderCount, out int skippedRolling) {
        var result = new List<string>();
        folderCount = 0;
        skippedRolling = 0;
        if (entries == null) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++) {
            LevelEntry e = entries[i];
            if (e == null || string.IsNullOrEmpty(e.FilePath)) continue;
            folderCount++;
            if (IsRollingBackupFile(e.FilePath)) { skippedRolling++; continue; }
            if (seen.Add(e.FilePath)) result.Add(e.FilePath);
        }
        return result;
    }

    /// <summary>主文件本身就是编辑器滚动备份（形如 20250817_144642582_BACKUP.adofai）——
    /// 一个文件夹里清一色是这种东西时，列表里那条其实不是一张谱面，评它没有意义</summary>
    private static bool IsRollingBackupFile(string file) {
        try { return RollingBackupRe.IsMatch(Path.GetFileNameWithoutExtension(file)); } catch { return false; }
    }

    /// <summary>谱面显示名：优先用列表里那个（玩家改过名的也算），没有就用文件名</summary>
    public static string TitleOf(string file) {
        try {
            var list = LevelScanner.Entries;
            if (list != null) {
                for (int i = 0; i < list.Count; i++) {
                    var e = list[i];
                    if (e != null && string.Equals(e.FilePath, file, StringComparison.OrdinalIgnoreCase)) {
                        if (!string.IsNullOrEmpty(e.Title)) return TufClient.CleanText(e.Title);
                    }
                }
            }
        } catch { }
        try { return TufClient.CleanText(Path.GetFileNameWithoutExtension(file)); } catch { return ""; }
    }

    // ================= 档位能不能写 =================

    /// <summary>档位名是不是「本功能允许写进 marks.txt」的形状。纯函数（不碰磁盘，自检直接调）。</summary>
    public static bool TierWritable(string name) {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.Trim();
        // ① 明确不写的档位
        for (int i = 0; i < NeverWriteTiers.Length; i++) {
            if (string.Equals(n, NeverWriteTiers[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        // ② 只认本机标记体系里的四种档位名：P1~P20 / G1~G20 / U1~U20 / Q0~Q4（外加几个 U*J 老档位）。
        //    平台那边的合集档（"GQ0 (G1~G4)" / "UQ2 (U9~U12)"）、LEGACY 的 "Q2+"、"U15M"、"Gimmick"
        //    在本机没有对应图标，写进去就是一条看不见的标记 —— 一律不认。
        int i2 = 0;
        char c = n[0];
        if (c >= 'a' && c <= 'z') c = (char)(c - 32);
        if (c != 'P' && c != 'G' && c != 'U' && c != 'Q') return false;
        i2 = 1;
        int digits = 0;
        while (i2 < n.Length && n[i2] >= '0' && n[i2] <= '9') { i2++; digits++; }
        if (digits < 1 || digits > 2) return false;
        if (i2 < n.Length) {
            char tail = n[i2];
            if (tail >= 'a' && tail <= 'z') tail = (char)(tail - 32);
            // 只允许末尾一个 J（本机有 U1J/U5J/U6J/U7J/U9J/U11J/U13J/U15J 这几张图）；"U15M" 这类不认
            if (tail != 'J' || i2 != n.Length - 1) return false;
        }
        return true;
    }

    /// <summary>档位图标是否真的存在（写之前必须过这一关）。带负缓存，不走 MarkStore.LoadIcon
    /// —— 那个会往「资源包缺失」清单里记一笔，这里只是"不写"而不是"缺资源"。</summary>
    private static readonly Dictionary<string, bool> _iconCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public static bool IconExists(string tier) {
        if (string.IsNullOrEmpty(tier)) return false;
        bool hit;
        if (_iconCache.TryGetValue(tier, out hit)) return hit;
        bool ok = false;
        try {
            string root = ModDir();
            if (!string.IsNullOrEmpty(root)) {
                ok = File.Exists(Path.Combine(Path.Combine(Path.Combine(root, "assets"), "marks"), tier + ".png"));
            }
        } catch { ok = false; }
        _iconCache[tier] = ok;
        return ok;
    }

    /// <summary>能不能写这个档位：形状对 + 本机有图标</summary>
    public static bool CanWriteTier(string tier) { return TierWritable(tier) && IconExists(tier); }

    // ================= 判定 → 分栏（纯函数）=================

    /// <summary>把一个 TufMatch 判成「进哪一栏」。纯函数：不写盘、不碰界面，自检与离线对账直接调它。</summary>
    public static TufBatchItem Decide(TufMatch m, TufChartInfo info, string title) {
        var it = new TufBatchItem();
        it.File = info != null ? info.Path : "";
        it.Title = string.IsNullOrEmpty(title) ? TitleOf(it.File) : title;
        if (m == null) {
            it.Bucket = BucketMissing;
            it.MatchType = TufClient.MatchQueryFailed;
            it.Reason = "没有判定结果（查询被取消？）";
            return it;
        }
        it.MatchType = m.MatchType;
        it.TufId = m.TufId;
        it.DiffId = m.DiffId;
        it.TufTier = m.DifficultyName ?? "";
        it.Reason = m.Reason ?? "";
        it.CharterSkippedEmpty = m.CharterSkippedEmpty;
        if (m.Candidates != null) it.Candidates = m.Candidates;

        // ① 自动写入：格数 + BPM 两道硬门定下来（AutoRated）＋ 没有 NeedsReview ＋ 档位可写
        if (m.AutoRated && !m.NeedsReview && TierWritable(m.DifficultyName)) {
            it.Bucket = BucketAuto;
            it.Tier = m.DifficultyName;
            it.Reason = "TUF 判定可靠（格数 " + m.LocalTileCount + " 格一致"
                      + (m.MatchType == TufClient.MatchHighScore ? "，BPM 也对上" : "，唯一候选")
                      + "）";
            if (m.SoftFlags) {
                // 署名对不上却照样写：这句是固定话术，一行一句摆在自动写入那一栏里。
                // 理由会随会话落进 mod\tuf_batch.json、结果页每次显示都从那儿来，
                // 所以事后翻结果页就能认出「这一条只凭指纹定的，不是靠署名对上的」。
                it.Reason += "署名对不上，凭格数+BPM 判定（" + FlagsText(m.ReviewFlags) + "）";
            }
            return it;
        }

        // ② 需要你确认：五种情况
        //    · 谱师回落命中（格数硬门一条都没命中，只有 creator 对得上）—— creator 不是指纹
        //      （TUF 的 creator 常常是上传者/审核者），只够让玩家点一下，绝不自动写
        //    · NeedsReview（BPM 对不上）—— 版本/倍速可能不同，交给玩家点，按口径不否掉
        //    · 多候选 ambiguous（格数一致的定不下来 / 池子截断 / 本地格数读不到）
        //    · 特殊档（Censored/Qq/P0/Marathon/Impossible）—— 按规矩不写，但要说清楚 TUF 给了什么
        if (m.MatchType == TufClient.MatchCharterMatch) {
            it.Bucket = BucketConfirm;
            it.Reason = (string.IsNullOrEmpty(it.Reason)
                         ? "谱师相同、格数/ BPM 对不上，需要你确认" : it.Reason);
            return it;
        }
        if (m.NeedsReview) {
            it.Bucket = BucketConfirm;
            it.Reason = (string.IsNullOrEmpty(it.Reason) ? "需要你确认" : it.Reason);
            return it;
        }
        if (m.MatchType == TufClient.MatchAmbiguous) {
            it.Bucket = BucketConfirm;
            if (string.IsNullOrEmpty(it.Reason)) {
                it.Reason = m.TopTieCount.HasValue ? ("最高分并列 " + m.TopTieCount.Value + " 个") : "候选不唯一";
            }
            return it;
        }
        if (m.Auto && m.Rated == true && !string.IsNullOrEmpty(m.DifficultyName) && !TierWritable(m.DifficultyName)) {
            it.Bucket = BucketConfirm;
            it.Reason = "TUF 记为特殊档 " + m.DifficultyName + "（按规矩不写入，你要标就自己标）";
            return it;
        }

        // ③ 查不到 → 本地 AutoRating 兜底
        it.Bucket = BucketMissing;
        if (m.MatchType == TufClient.MatchUnranked) {
            it.Reason = "TUF：Unranked（平台没给这谱面定级）";
        } else if (m.MatchType == TufClient.MatchQueryFailed) {
            it.Reason = "查询失败（网络）：" + (string.IsNullOrEmpty(it.Reason) ? "?" : it.Reason);
        } else if (m.MatchType == TufClient.MatchNoSong) {
            it.Reason = "本地 .adofai 里没有 song 字段，没法查";
        } else if (string.IsNullOrEmpty(it.Reason)) {
            it.Reason = "TUF 查不到（query=" + (m.QueryUsed ?? "") + "）";
        }
        return it;
    }

    /// <summary>把告警列表拼成一句（"a / b"），空列表返回空串</summary>
    private static string FlagsText(List<string> flags) {
        if (flags == null || flags.Count == 0) return "";
        return string.Join(" / ", flags.ToArray());
    }

    // ================= 写盘（唯一入口）=================

    /// <summary>这一轮已经备份过 marks.txt 了没有（一次批量只备份一次，避免每写一条都复制一份）</summary>
    private static bool _backupDone;
    /// <summary>备份落在哪里（写日志/界面用）</summary>
    public static string LastBackupFile = "";

    /// <summary>重置备份标记：新一轮批量开始时调（上一轮的备份不再代表"写之前"的状态）</summary>
    public static void ResetBackupFlag() { _backupDone = false; }

    /// <summary>把 marks.txt / auto_rated.txt 复制到备份库。返回 false = 备份失败（此时不许写盘）。</summary>
    public static bool TryBackupMarks(out string dest, out string error) {
        dest = "";
        error = "";
        string root = ModDir();
        if (string.IsNullOrEmpty(root)) { error = "mod 目录未知"; return false; }
        string marks = Path.Combine(root, "marks.txt");
        string auto = Path.Combine(root, "auto_rated.txt");
        bool any = File.Exists(marks) || File.Exists(auto);
        if (!any) { dest = "(还没有 marks.txt，无需备份)"; return true; }   // 第一次评级：没有可备份的玩家数据
        try {
            string lib = BackupLib();
            Directory.CreateDirectory(lib);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(marks)) {
                dest = Path.Combine(lib, "marks_before_tuf_" + stamp + ".txt");
                File.Copy(marks, dest, true);
            }
            if (File.Exists(auto)) {
                string d2 = Path.Combine(lib, "auto_rated_before_tuf_" + stamp + ".txt");
                File.Copy(auto, d2, true);
            }
            return true;
        } catch (Exception e) {
            error = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    /// <summary>写一条标记。本功能唯一的写入口，四条铁规矩都在这里：
    /// ① 档位必须可写（TierWritable + 图标存在）② 玩家已标过的不覆盖
    /// ③ 写之前必须先备份到备份库（备份失败就不写）④ 顺手记来源。
    /// <paramref name="why"/> 回传没写成的理由（界面直接显示）。</summary>
    public static bool WriteMark(string file, string tier, string source, out string why) {
        why = "";
        if (string.IsNullOrEmpty(file)) { why = "文件路径为空"; return false; }
        if (!TierWritable(tier)) { why = "档位按规矩不写入（" + (tier ?? "") + "）"; return false; }
        if (!IconExists(tier)) { why = "本机没有「" + tier + "」的档位图标，写入会变成看不见的标记"; return false; }
        if (!string.IsNullOrEmpty(MarkStore.Get(file))) { why = "玩家自己已经标过了，不覆盖"; return false; }

        if (!_backupDone) {
            string dest, err;
            if (!TryBackupMarks(out dest, out err)) {
                why = "备份 marks.txt 失败（" + err + "），按规矩不写：" + BackupLib();
                Lg.Info("TUF 批量评级：备份失败，已放弃写入 —— " + why);
                return false;
            }
            _backupDone = true;
            LastBackupFile = dest;
            State.BackupFile = dest;
            Lg.Info("TUF 批量评级：写盘前已备份 marks.txt → " + dest);
        }

        MarkStore.Set(file, tier);
        try { MarkStore.MarkAutoRated(file); } catch { }   // 与老的本地批量评级同口径：记进 auto_rated.txt
        SourceStore.Set(file, source, tier);
        return true;
    }

    /// <summary>本地估算兜底（查不到那一栏）：走 AutoRating，档位必须是合法 P/G/U 才写</summary>
    public static TufBatchItem LocalFallback(TufBatchItem it) {
        if (it == null) return null;
        string pgu = null;
        double star = 0d;
        try {
            if (!PortalUI.LocalRateForBatch(it.File, out pgu, out star)) {
                it.Reason = it.Reason + "｜本地估算也没能给出档位";
                return it;
            }
        } catch (Exception e) {
            it.Reason = it.Reason + "｜本地估算出错：" + e.GetType().Name;
            return it;
        }
        it.LocalTier = pgu;
        it.LocalStar = star;
        string why;
        if (WriteMark(it.File, pgu, SourceLocal, out why)) {
            it.Tier = pgu;
            it.Source = SourceLocal;
            it.Written = true;
            it.Reason = it.Reason + "｜已按本地估算写入 " + pgu + "（star " + star.ToString("F2", CultureInfo.InvariantCulture) + "）";
        } else {
            it.Reason = it.Reason + "｜本地估算 " + pgu + " 没写成：" + why;
        }
        return it;
    }

    // ================= 来源记录（tuf_source.txt）=================

    /// <summary>每张谱面的定级来源（社区 / 本地估算）。界面按它显示来源。</summary>
    public static class SourceStore {
        private class Row { public string source = ""; public string tier = ""; }
        private static Dictionary<string, Row> _rows;
        private static string PathOf() {
            try {
                string root = ModDir();
                return string.IsNullOrEmpty(root) ? null : Path.Combine(root, SourceFile);
            } catch { return null; }
        }
        private static void Ensure() {
            if (_rows != null) return;
            _rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            try {
                string p = PathOf();
                if (p == null || !File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p)) {
                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    string[] parts = t.Split('\t');
                    if (parts.Length < 2 || parts[0].Trim().Length == 0) continue;
                    _rows[LevelScanner.ToAbs(parts[0].Trim())] = new Row {
                        source = parts[1].Trim(),
                        tier = parts.Length > 2 ? parts[2].Trim() : ""
                    };
                }
            } catch (Exception e) { LogException("读取定级来源记录失败", e); }
        }

        /// <summary>来源：community / local；没记录返回 ""</summary>
        public static string Get(string file) {
            Ensure();
            if (string.IsNullOrEmpty(file)) return "";
            Row r;
            return _rows.TryGetValue(LevelScanner.ToAbs(file), out r) ? r.source : "";
        }

        /// <summary>来源 + 档位（"community|P6"）；没记录返回 ""</summary>
        public static string GetDetail(string file) {
            Ensure();
            if (string.IsNullOrEmpty(file)) return "";
            Row r;
            return _rows.TryGetValue(LevelScanner.ToAbs(file), out r) ? (r.source + "|" + r.tier) : "";
        }

        public static void Set(string file, string source, string tier) {
            Ensure();
            if (string.IsNullOrEmpty(file)) return;
            _rows[LevelScanner.ToAbs(file)] = new Row { source = source ?? "", tier = tier ?? "" };
            Save();
        }

        public static void Save() {
            Ensure();
            string p = PathOf();
            if (p == null) return;
            try {
                var lines = new List<string>();
                foreach (KeyValuePair<string, Row> kv in _rows) {
                    if (kv.Value == null || string.IsNullOrEmpty(kv.Value.source)) continue;
                    lines.Add(LevelScanner.ToRel(kv.Key) + "\t" + kv.Value.source + "\t" + kv.Value.tier);
                }
                File.WriteAllLines(p, lines.ToArray());
            } catch (Exception e) { LogException("保存定级来源记录失败", e); }
        }
    }

    // ================= 会话状态（可中断可续）=================

    private static string SessionPath() {
        try {
            string root = ModDir();
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, SessionFile);
        } catch { return null; }
    }

    /// <summary>读上次没做完的会话（文件不在 / 读坏了就当没有，绝不让它挡住批量评级）</summary>
    public static TufBatchState LoadSession() {
        var st = new TufBatchState();
        try {
            string p = SessionPath();
            if (p == null || !File.Exists(p)) return st;
            string json = File.ReadAllText(p);
            var read = JsonConvert.DeserializeObject<TufBatchState>(json);
            if (read != null) {
                if (read.Auto == null) read.Auto = new List<TufBatchItem>();
                if (read.Pending == null) read.Pending = new List<TufBatchItem>();
                if (read.Missing == null) read.Missing = new List<TufBatchItem>();
                if (read.Resolved == null) read.Resolved = new List<string>();
                if (read.Skipped == null) read.Skipped = new List<string>();
                return read;
            }
        } catch (Exception e) {
            Lg.Info("TUF 批量评级：上次的会话记录读不了（" + e.GetType().Name + "），本轮从头开始");
        }
        return st;
    }

    public static void SaveSession() {
        try {
            string p = SessionPath();
            if (p == null) return;
            File.WriteAllText(p, JsonConvert.SerializeObject(State, Formatting.Indented), new System.Text.UTF8Encoding(false));
        } catch (Exception e) { LogException("保存 TUF 批量评级会话失败", e); }
    }

    // ================= 入口 =================

    /// <summary>把上次没做完的会话读进 State（只在还没有任何结果时读，别把正在跑的这一轮盖掉）。
    /// 重开游戏后点「继续评级」/「查看进度」，窗口里就该有上次待确认的那些条目。</summary>
    public static void EnsureStateLoaded() {
        if (Running) return;
        var st = State;
        if (st != null && (st.Pending.Count > 0 || st.Auto.Count > 0 || st.Missing.Count > 0 || st.Scanned > 0)) return;
        State = LoadSession();
    }

    /// <summary>开始 / 继续批量评级。已经有一轮在跑就直接把窗口顶到前面，不开第二轮。</summary>
    public static void StartOrResume() {
        if (Running && _host != null) { PortalUI.TufBatchOpenWindow(); return; }
        if (Running && _host == null) {
            // 宿主没了（早先版本会在切场景时被连场景一起销毁）→ 别把功能锁死在这上面
            Lg.Info("TUF 批量评级：上一轮的宿主已经不存在了（切过场景？），本轮重开");
            Running = false;
        }
        try {
            if (Application.internetReachability == NetworkReachability.NotReachable) {
                PortalUI.ShowToast(Lang.Tr("现在连不上网喵（TUF 是网上查的），先检查一下网络"), 6f);
                return;
            }
        } catch { }   // 取不到网络状态就不拦（别因为一个探测把功能堵死）
        BeginRun(false);
    }

    /// <summary>自动流程入口（批量导入 → 检验重复 → 评级 的第三段）。和 StartOrResume 只差两点：
    /// 断网也照跑（查不了 TUF 就整轮交给本地估算，别让玩家白等一串 8 秒超时），
    /// 跑完也不翻"待确认"那一页 —— 这件事不该让玩家做选择。</summary>
    public static void StartAuto() {
        if (Running) {
            Lg.Info("TUF 批量评级：自动流程跳过（已经有一轮在跑）");
            return;
        }
        BeginRun(true);
    }

    /// <summary>开一轮（自动 / 手动共用）。防重入由上层的两个入口各自负责，这里只管把宿主与状态摆好。</summary>
    private static void BeginRun(bool auto) {
        AutoMode = auto;
        Running = true;
        CancelRequested = false;
        Finished = false;
        ResetBackupFlag();
        PortalUI.TufBatchOpenWindow();
        _host = new GameObject("__TufBatchHost");
        UnityEngine.Object.DontDestroyOnLoad(_host);   // 切场景（进关卡）不许把这一轮连锅端掉
        _host.AddComponent<TufBatchRunner>().Begin();
    }

    /// <summary>批量协程的宿主（在跑时非空；跑完自己清掉）</summary>
    private static GameObject _host;

    /// <summary>中断（进度留下，下次点「继续评级」接着来）</summary>
    public static void Cancel() {
        if (!Running) return;
        CancelRequested = true;
        try { TufClient.AbortAll("玩家中断 TUF 批量评级"); } catch { }
        Lg.Info("TUF 批量评级：玩家中断，已完成 " + State.Queried + " 条（会话已存，可继续）");
    }

    /// <summary>设置页那颗按钮该写什么字：正在跑 = 查看进度，有待确认/没跑完 = 继续评级，否则 = 开始评级</summary>
    public static string StartButtonLabel() {
        if (Running) return Lang.Tr("查看进度");
        if (HasPending) return Lang.Tr("继续评级");
        try {
            string p = SessionPath();
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return Lang.Tr("继续评级");
        } catch { }
        return Lang.Tr("开始评级");
    }

    /// <summary>清空查询记录（tuf_batch.json）后重新扫一遍评级：只丢"查过什么"的记忆，不动任何标记</summary>
    public static void ClearSessionAndRestart() {
        if (Running) { PortalUI.ShowToast(Lang.Tr("这一轮还在跑喵"), 4f); return; }
        try {
            string p = SessionPath();
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p);
        } catch (Exception e) { LogException("清空 TUF 批量评级会话失败", e); }
        State = new TufBatchState();
        Finished = false;
        Lg.Info("TUF 批量评级：已清空查询记录，重新扫描");
        StartOrResume();
    }

    // ================= 批量协程 =================

    /// <summary>批量评级协程宿主：一帧处理一张（读头 → 排队 → 等结果 → 落栏 → 写盘）。</summary>
    private class TufBatchRunner : MonoBehaviour {
        public void Begin() { StartCoroutine(Run()); }

        private IEnumerator Run() {
            var st = LoadSession();
            State = st;
            st.finishedAt = "";
            if (string.IsNullOrEmpty(st.startedAt)) {
                st.startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            // 上一轮还没跑完（程序被强杀）→ 记一笔，本轮接着做
            if (st.Resolved.Count > 0 || st.Auto.Count > 0 || st.Pending.Count > 0 || st.Missing.Count > 0) {
                Lg.Info("TUF 批量评级：接上次的进度 —— 已处理 " + st.Resolved.Count
                    + " 条，待确认 " + st.Pending.Count + " 条");
            }
            PortalUI.TufBatchNotify();
            yield return null;

            // ① 扫库（清单直接来自谱面库列表，见 ScanTargets 的说明）
            int folders, rolling;
            var all = ScanTargets(out folders, out rolling);
            st.Scanned = all.Count;
            st.Skipped.Clear();
            st.SkippedMarked = 0;
            st.CharterMatched = 0;
            st.CharterEmpty = 0;
            Lg.Info("TUF 批量评级：谱面库列表里 " + folders + " 条带主文件（与列表同源），"
                + "取到 " + all.Count + " 张要评；跳过 " + rolling + " 条编辑器滚动备份");

            // ② 分拣：玩家标过的跳过；上次已处理的跳过；没做完的接着做
            var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string r in st.Resolved) { string a = LevelScanner.ToAbs(r); if (!string.IsNullOrEmpty(a)) resolved.Add(a); }
            var todo = new List<string>();
            for (int i = 0; i < all.Count; i++) {
                string f = all[i];
                string mark = MarkStore.Get(f);
                if (!string.IsNullOrEmpty(mark)) {
                    st.Skipped.Add(LevelScanner.ToRel(f));
                    st.SkippedMarked++;
                    DropPendingFor(st, f);      // 玩家自己标了 → 从待确认里撤掉
                    continue;
                }
                if (resolved.Contains(LevelScanner.ToAbs(f))) continue;   // 上次跑过的，不重查
                todo.Add(f);
            }
            // 待确认列表也要过一遍：文件没了 / 玩家已经标了的，撤掉
            for (int i = st.Pending.Count - 1; i >= 0; i--) {
                var it = st.Pending[i];
                if (it == null || string.IsNullOrEmpty(it.File) || !File.Exists(it.File)) { st.Pending.RemoveAt(i); continue; }
                if (!string.IsNullOrEmpty(MarkStore.Get(it.File))) { st.Pending.RemoveAt(i); continue; }
            }
            st.finishedAt = "";
            SaveSession();
            PortalUI.TufBatchNotify();
            yield return null;

            // ③ 逐条查（TufClient 自己排队 + 0.6s 限速；这里一次只挂一单，进度才好显示）
            // 自动流程遇到断网就不查了：每一条都要等满 8 秒超时，一轮下来是几十分钟的白等；
            // 直接按"查不到"处理，让本地估算接手（本地估算不用网）。
            bool offline = false;
            if (AutoMode) {
                try { offline = Application.internetReachability == NetworkReachability.NotReachable; } catch { }
                if (offline) Lg.Info("TUF 批量评级：自动流程，现在连不上网 → 这一轮全部改用本地估算");
            }
            var resolvedList = st.Resolved;
            int total = todo.Count;
            int done = 0;
            st.TodoTotal = total;
            st.TodoDone = 0;
            foreach (string file in todo) {
                if (CancelRequested) break;
                done++;
                TufChartInfo info = null;
                string title = TitleOf(file);
                if (!offline) {
                    try { info = TufClient.ReadChartInfo(file); } catch (Exception e) {
                        LogException("TUF 批量评级：读谱面头失败 " + file, e);
                    }
                }
                TufBatchItem item;
                if (info == null || string.IsNullOrEmpty(info.Path)) info = new TufChartInfo { Path = file };
                TufQuery job = null;
                if (!offline) {
                    try { job = TufClient.Query(info, title); } catch (Exception e) {
                        LogException("TUF 批量评级：排队查询失败 " + file, e);
                    }
                    while (job != null && !job.Done) {
                        if (CancelRequested) { try { TufClient.AbortAll("玩家中断 TUF 批量评级"); } catch { } break; }
                        PortalUI.TufBatchNotify();
                        yield return null;
                    }
                    if (CancelRequested) break;
                }

                // 没网 / 没查成：喂一个"查不到"的判定，后面照样走本地估算那条路
                TufMatch match = job != null
                    ? job.Match
                    : (offline ? new TufMatch { MatchType = TufClient.MatchQueryFailed, Reason = "现在连不上网" } : null);
                item = Decide(match, info, title);
                item.File = file;
                if (string.IsNullOrEmpty(item.Title)) item.Title = Path.GetFileNameWithoutExtension(file);
                if (item.MatchType == TufClient.MatchCharterMatch) st.CharterMatched++;
                if (item.CharterSkippedEmpty) st.CharterEmpty++;

                if (item.Bucket == BucketAuto) {
                    string why;
                    if (WriteMark(file, item.Tier, SourceCommunity, out why)) {
                        item.Source = SourceCommunity;
                        item.Written = true;
                    } else {
                        item.Bucket = BucketConfirm;      // 写不成（图标缺 / 玩家刚标上）→ 交给玩家看
                        item.Reason = item.Reason + "｜没能自动写入：" + why;
                    }
                }
                // 自动流程：待确认那一栏是要玩家点开看的，这条链上玩家不会看到它，所以降级到"查不到"，
                // 交给下面的本地估算；本地估算也给不出档位就既不标也不问（两个都不行就不标）。
                if (item.Bucket == BucketConfirm && AutoMode) {
                    item.Bucket = BucketMissing;
                    item.Reason = (item.Reason ?? "") + "｜自动流程：TUF 定不下来，改用本地估算";
                }
                if (item.Bucket == BucketConfirm) st.Pending.Add(item);
                else if (item.Bucket == BucketMissing) { LocalFallback(item); st.Missing.Add(item); }
                else st.Auto.Add(item);

                st.Queried++;
                st.TodoDone = done;
                resolvedList.Add(LevelScanner.ToRel(file));
                SaveSession();                        // 每条都落一次：断电/强杀也不丢进度
                string tail = item.Written
                    ? ("（写入 " + item.Tier + " · " + item.SourceLabel + "）")
                    : (string.IsNullOrEmpty(item.Reason) ? "" : ("（" + item.Reason + "）"));
                Lg.Info("TUF 批量评级 " + done + "/" + total + "：" + item.Title + " → " + item.Bucket + tail);                PortalUI.TufBatchNotify();
                yield return null;
            }

            // ④ 收尾
            try { MarkStore.Save(); } catch (Exception e) { LogException("批量评级保存标记失败", e); }
            try { Achievements.RebuildMilestones(false); } catch (Exception) { }
            try { PortalUI.RefreshAfterTufBatch(); } catch (Exception e) { LogException("批量评级后刷新列表失败", e); }
            st.finishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Running = false;
            Finished = true;
            _host = null;      // 先清引用：宿主马上就被销毁了
            SaveSession();
            PortalUI.TufBatchNotify();
            bool wasAuto = AutoMode;
            AutoMode = false;  // 这一轮结束了：下次手动点「开始评级」按手动那一套走
            if (wasAuto) {
                // 自动流程：收掉进度窗，只留一句"评级完了"（玩家全程不用点任何东西）
                try { PortalUI.TufBatchFinishAuto(st); } catch (Exception e) { LogException("自动评级收尾失败", e); }
            }
            Lg.Info("TUF 批量评级" + (CancelRequested ? "（已中断）" : "完成")
                + "：自动写入 " + st.Auto.Count + " / 待确认 " + st.Pending.Count + " / 查不到 " + st.Missing.Count
                + "（跳过玩家已标记 " + st.SkippedMarked + "；谱师回落进确认栏 " + st.CharterMatched
                + "，本地谱师为空没走这条路 " + st.CharterEmpty + "）"
                + (string.IsNullOrEmpty(st.BackupFile) ? "" : "；写盘前备份 → " + st.BackupFile));
            UnityEngine.Object.Destroy(gameObject);
        }

        private static void DropPendingFor(TufBatchState st, string file) {
            for (int i = st.Pending.Count - 1; i >= 0; i--) {
                if (st.Pending[i] != null && string.Equals(st.Pending[i].File, file, StringComparison.OrdinalIgnoreCase)) {
                    st.Pending.RemoveAt(i);
                }
            }
        }
    }

    // ================= 玩家在「需要你确认」里点一条候选 =================

    /// <summary>玩家点了一个候选：把该谱面标成这个候选的档位（社区来源）。返回是否成功。</summary>
    public static bool ConfirmCandidate(TufBatchItem item, TufCandidate cand, out string message) {
        message = "";
        if (item == null || cand == null) { message = "条目无效"; return false; }
        string tier = cand.DifficultyName;
        if (string.IsNullOrEmpty(tier)) { message = "这条候选没有档位名（TUF 数据里 difficulty 缺失）"; return false; }
        if (!TierWritable(tier)) { message = "「" + tier + "」是特殊档，按规矩不写入 —— 要标就自己标"; return false; }
        if (!IconExists(tier)) {
            // 档位名合法但没有图标：候选栏里给了「照写」的可能（玩家自己确认过）
            message = "本机没有「" + tier + "」这张档位图，先补资源包再写";
            return false;
        }
        string why;
        if (!WriteMark(item.File, tier, SourceCommunity, out why)) { message = why; return false; }
        item.Bucket = BucketAuto;
        item.Tier = tier;
        item.TufTier = tier;
        item.TufId = cand.Id;
        item.DiffId = cand.DiffId;
        item.Source = SourceCommunity;
        item.Written = true;
        item.Reason = "你确认的：TUF " + tier + "（" + (cand.Creator ?? "") + "）";
        State.Pending.Remove(item);
        State.Auto.Add(item);
        SaveSession();
        message = "已标为 " + tier + "（社区定级）";
        return true;
    }

    /// <summary>玩家点「这条我不选，用本地估算兜底」：把一条待确认的按 AutoRating 落盘</summary>
    public static bool SkipToLocal(TufBatchItem item, out string message) {
        message = "";
        if (item == null) { message = "条目无效"; return false; }
        LocalFallback(item);
        if (item.Written) {
            State.Pending.Remove(item);
            State.Missing.Add(item);
            SaveSession();
            message = "已按本地估算标为 " + item.Tier + "（来源：本地估算）";
            return true;
        }
        message = item.Reason;
        return false;
    }

    // ================= 自检（纯函数、零网络、零副作用）=================

    /// <summary>批量层的判定表自检：全过返回 "PASS n/n"，否则 "FAIL k/n: …"。纯函数，零网络零磁盘写。</summary>
    public static string SelfTestReport() {
        var fails = new List<string>();
        int n = 0;
        Action<string, bool> ck = delegate (string name, bool ok) { n++; if (!ok) fails.Add(name); };

        // ---------- 铁规矩②：特殊档与 Unranked 不写 ----------
        ck("不写：Unranked", !TierWritable("Unranked"));
        ck("不写：Censored", !TierWritable("Censored"));
        ck("不写：Qq", !TierWritable("Qq"));
        ck("不写：P0", !TierWritable("P0"));
        ck("不写：Marathon", !TierWritable("Marathon"));
        ck("不写：Impossible", !TierWritable("Impossible"));
        ck("要写：P1/P20/G1/G20/U1/U20/Q0/Q4", TierWritable("P1") && TierWritable("P20") && TierWritable("G1")
            && TierWritable("G20") && TierWritable("U1") && TierWritable("U20") && TierWritable("Q0") && TierWritable("Q4"));
        ck("要写：U*J 老档位（本机有图）", TierWritable("U1J") && TierWritable("U15J"));
        ck("不写：平台合集档 GQ1 (G5~G8)", !TierWritable("GQ1 (G5~G8)"));
        ck("不写：LEGACY 的 Q2+ / U15M / Gimmick", !TierWritable("Q2+") && !TierWritable("U15M") && !TierWritable("Gimmick"));
        ck("不写：空档位名", !TierWritable("") && !TierWritable(null));

        // ---------- 扫描源：必须跟谱面库列表同源（不许自己拼 backup\ 那种存放谱面的地方）----------
        // 假路径一律不带盘符（源码里不许出现盘符字面量）。
        var fake = new List<LevelEntry>();
        fake.Add(new LevelEntry { FolderPath = "task\\!Wandering", FilePath = "task\\!Wandering\\main.adofai", Title = "a" });
        fake.Add(new LevelEntry { FolderPath = "task\\!Wandering", FilePath = "task\\!Wandering\\main.adofai", Title = "a" });
        fake.Add(new LevelEntry { FolderPath = "task\\5-ex", FilePath = "task\\5-ex\\level.adofai", Title = "b" });
        fake.Add(new LevelEntry { FolderPath = "task\\roll", FilePath = "task\\roll\\20250817_144642582_BACKUP.adofai", Title = "c" });
        fake.Add(new LevelEntry { FolderPath = "task\\empty", FilePath = "", Title = "d" });
        fake.Add(null);
        int fc, sr;
        var tg = TargetsFromEntries(fake, out fc, out sr);
        ck("扫描源：直接吃谱面库列表条目（同一文件重复条目只算一张）", tg.Count == 2 && fc == 4);
        ck("扫描源：主文件本身是编辑器滚动备份的条目跳过", sr == 1
            && !tg.Contains("task\\roll\\20250817_144642582_BACKUP.adofai"));
        ck("扫描源：库根在 mod 目录之外（相对形态 task\\…）照样进清单",
            tg.Contains("task\\!Wandering\\main.adofai") && tg.Contains("task\\5-ex\\level.adofai"));
        ck("扫描源：空条目 / 空主文件不许进清单", !tg.Contains("") && tg.Count == 2);
        ck("扫描源：没有列表（null）时不炸，返回空清单",
            TargetsFromEntries(null, out fc, out sr).Count == 0 && fc == 0 && sr == 0);

        // ---------- 三栏判定表（喂真实响应形状，走 TufClient 的真解析与真判定）----------
        var local = new TufChartInfo {
            Path = "backup/Komorebi/main.adofai", Song = "Komorebi", Artist = "m-takeu",
            Author = "charterGuy", Charter = "charterGuy", FolderName = "Komorebi", Bpm = 103d, HasBpm = true,
            TileCount = 800L, HasTileCount = true, TileSource = "angleData"
        };
        string jAuto = "{\"results\":[{\"id\":1,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":103,\"tilecount\":800,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufBatchItem auto = Decide(TufClient.Classify(local, "", TufClient.ReadLevelsJson(jAuto), "Komorebi"), local, "Komorebi");
        ck("自动写入：格数 + BPM 两道硬门定下来 → auto 栏，档位 P7",
            auto.Bucket == BucketAuto && auto.Tier == "P7");

        // 主判据第一道门：格数对不上的一律排除 → 不自动写
        string jTile = "{\"results\":[{\"id\":2,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":103,\"tilecount\":801,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufMatch mTile = TufClient.Classify(local, "", TufClient.ReadLevelsJson(jTile), "Komorebi");
        TufBatchItem tile = Decide(mTile, local, "Komorebi");
        ck("格数对不上（800 vs 801）→ 不自动写，进「需要你确认」", tile.Bucket == BucketConfirm && !tile.Written);
        ck("格数对不上时不许报成「自动写入」", !mTile.AutoRated && mTile.TileMatchCount == 0);

        // 谱师回落：格数硬门一条都没剩下、但谱师（creator）对得上 → 只进「需要你确认」
        string jCharter = "{\"results\":[{\"id\":11,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":150,\"tilecount\":801,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufMatch mCharter = TufClient.Classify(local, "", TufClient.ReadLevelsJson(jCharter), "Komorebi");
        TufBatchItem charter = Decide(mCharter, local, "Komorebi");
        ck("谱师回落：格数没命中但谱师对上 → 进「需要你确认」，绝不自动写",
            charter.Bucket == BucketConfirm && !charter.Written
            && mCharter.MatchType == TufClient.MatchCharterMatch && !mCharter.AutoRated);
        ck("谱师回落：这一档不带档位名（creator 不是定级证据，等玩家自己点）",
            charter.Tier == "" && charter.TufTier == "" && charter.DiffId == null);
        ck("谱师回落：理由带固定话术（玩家一眼认出这是只凭谱师的那一档）",
            charter.Reason.IndexOf("谱师相同、格数/ BPM 对不上，需要你确认") >= 0);
        ck("格数门命中时不许谱师改判：creator 对得上但格数也对得上 → 仍旧自动写",
            auto.Bucket == BucketAuto && auto.MatchType == TufClient.MatchAuto && !auto.CharterSkippedEmpty);

        var localNoCharter = new TufChartInfo {
            Path = "backup/Komorebi/main.adofai", Song = "Komorebi", Artist = "m-takeu",
            Author = "", Charter = "", FolderName = "Komorebi", Bpm = 103d, HasBpm = true,
            TileCount = 800L, HasTileCount = true, TileSource = "angleData"
        };
        TufMatch mEmptyCharter = TufClient.Classify(localNoCharter, "", TufClient.ReadLevelsJson(jCharter), "Komorebi");
        TufBatchItem emptyCharter = Decide(mEmptyCharter, localNoCharter, "Komorebi");
        ck("谱师回落：本地谱师为空 → 不走这条路（保持原来那一档）并留下诊断标记",
            emptyCharter.Bucket == BucketConfirm && emptyCharter.MatchType == TufClient.MatchAmbiguous
            && emptyCharter.CharterSkippedEmpty && !emptyCharter.Written);

        // 口径变更：artist / 谱师降级为软证据 —— 格数 + BPM 都对上就照写
        string jSoft = "{\"results\":[{\"id\":3,\"song\":\"Komorebi\",\"artist\":\"satie\",\"creator\":\"uploader\","
            + "\"suffix\":null,\"bpm\":103,\"tilecount\":800,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufMatch mSoft = TufClient.Classify(local, "", TufClient.ReadLevelsJson(jSoft), "Komorebi");
        TufBatchItem soft = Decide(mSoft, local, "Komorebi");
        ck("artist/谱师对不上但格数+BPM 都对 → 仍自动写（artist 已降级为软证据）",
            soft.Bucket == BucketAuto && soft.Tier == "P7");
        ck("软证据照样留痕（ReviewFlags 非空）供对着看", mSoft.SoftFlags && !mSoft.NeedsReview);
        ck("署名对不上也照写时，理由里必须带固定话术（只凭指纹写的，玩家事后一眼能认）",
            soft.Reason.IndexOf("署名对不上，凭格数+BPM 判定") >= 0);
        ck("署名对得上的条目不带这句（不给正常条目加噪音）",
            auto.Reason.IndexOf("凭格数+BPM 判定") < 0);

        // 主判据第二道门：格数一致但 BPM 不符 → 记 NeedsReview，进确认栏（不否掉）
        string jBpm = "{\"results\":[{\"id\":4,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":90,\"tilecount\":800,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufMatch mBpm = TufClient.Classify(local, "", TufClient.ReadLevelsJson(jBpm), "Komorebi");
        TufBatchItem bpm = Decide(mBpm, local, "Komorebi");
        ck("格数一致但 BPM 不符 → 进「需要你确认」（不否掉、也不自动写）",
            bpm.Bucket == BucketConfirm && !bpm.Written && mBpm.BpmMismatch);

        // 候选池被截断 → 一律不自动采用（格数一致的候选可能落在第 101 条之后）
        string jTrunc = "{\"results\":[{\"id\":5,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":103,\"tilecount\":800,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}},"
            + "{\"id\":6,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"zz\",\"suffix\":null,\"bpm\":150,"
            + "\"tilecount\":800,\"diffId\":8,\"difficulty\":{\"id\":8,\"name\":\"P8\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":true,\"total\":2}";
        TufBatchItem trunc = Decide(TufClient.Classify(local, "", TufClient.ReadLevelsJson(jTrunc), "Komorebi"), local, "Komorebi");
        ck("候选池截断（hasMore）→ 进「需要你确认」，绝不自动写",
            trunc.Bucket == BucketConfirm && !trunc.Written);

        string jAmb = "{\"results\":[{\"id\":7,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"a\",\"suffix\":null,"
            + "\"bpm\":103,\"tilecount\":800,\"diffId\":7,\"difficulty\":{\"id\":7,\"name\":\"P7\"}},"
            + "{\"id\":8,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"b\",\"suffix\":null,\"bpm\":103,"
            + "\"tilecount\":800,\"diffId\":8,\"difficulty\":{\"id\":8,\"name\":\"P8\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":2}";
        TufBatchItem amb = Decide(TufClient.Classify(local, "", TufClient.ReadLevelsJson(jAmb), "Komorebi"), local, "Komorebi");
        ck("需要你确认：格数一致且 BPM 也并列 → confirm 栏且候选 ≥2", amb.Bucket == BucketConfirm && amb.Candidates.Count >= 2);

        TufBatchItem none = Decide(TufClient.Classify(local, "", TufClient.ReadLevelsJson("{\"results\":[],\"total\":0,\"hasMore\":false}"), "Komorebi"), local, "Komorebi");
        ck("查不到：total==0 → missing 栏（交给本地估算兜底）", none.Bucket == BucketMissing);

        string jUnr = "{\"results\":[{\"id\":9,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":103,\"tilecount\":800,\"diffId\":0,\"difficulty\":{\"id\":0,\"name\":\"Unranked\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufBatchItem unr = Decide(TufClient.Classify(local, "", TufClient.ReadLevelsJson(jUnr), "Komorebi"), local, "Komorebi");
        ck("查不到：Unranked（平台没定级）→ missing 栏且档位为空", unr.Bucket == BucketMissing && unr.Tier == "");

        string jCen = "{\"results\":[{\"id\":10,\"song\":\"Komorebi\",\"artist\":\"m-takeu\",\"creator\":\"charterGuy\","
            + "\"suffix\":null,\"bpm\":103,\"tilecount\":800,\"diffId\":1000,\"difficulty\":{\"id\":1000,\"name\":\"Censored\"}}],"
            + "\"page\":0,\"offset\":0,\"limit\":100,\"hasMore\":false,\"total\":1}";
        TufBatchItem cen = Decide(TufClient.Classify(local, "", TufClient.ReadLevelsJson(jCen), "Komorebi"), local, "Komorebi");
        ck("特殊档 Censored：不自动写、但也不当「查不到」，进 confirm 让人看见",
            cen.Bucket == BucketConfirm && cen.Tier != "Censored" && cen.TufTier == "Censored");

        var failed = new TufMatch { MatchType = TufClient.MatchQueryFailed, Reason = "timeout", QueryUsed = "x" };
        TufBatchItem fail = Decide(failed, local, "Komorebi");
        ck("查不到：网络失败 → missing 栏（本地兜底）", fail.Bucket == BucketMissing && fail.Reason.IndexOf("网络") >= 0);

        TufBatchItem nosong = Decide(new TufMatch { MatchType = TufClient.MatchNoSong }, new TufChartInfo { Path = "x" }, "");
        ck("查不到：本地没有 song 字段 → missing 栏", nosong.Bucket == BucketMissing);

        // ---------- 滚动备份目录的识别（编辑器的 *_BACKUP.adofai 不算谱面）----------
        ck("编辑器滚动备份文件名识别",
            RollingBackupRe.IsMatch("20250817_144742589_BACKUP") && !RollingBackupRe.IsMatch("backup")
            && !RollingBackupRe.IsMatch("20250817_144742589_BACKUP_old"));

        // ---------- 来源口径 ----------
        var src = new TufBatchItem { Source = SourceCommunity };
        var src2 = new TufBatchItem { Source = SourceLocal };
        ck("来源标签：社区定级 / 本地估算", src.SourceLabel == "社区定级" && src2.SourceLabel == "本地估算");

        if (fails.Count == 0) return "PASS " + n + "/" + n;
        return "FAIL " + fails.Count + "/" + n + ": " + string.Join(" ｜ ", fails.ToArray());
    }

    /// <summary>启动时把批量层的判定表跑一遍写进日志（与 TufClient.RunSelfTest 同写法）</summary>
    public static void RunSelfTest() {
        try {
            string r = SelfTestReport();
            if (r.StartsWith("PASS", StringComparison.Ordinal)) {
                Lg.Info("TUF 批量评级自检：" + r + "（含「NeedsReview 一律进需要你确认」「特殊档与 Unranked 不写」"
                    + "「查不到才回落本地估算」三条写盘规矩）");
            } else {
                Lg.Info("TUF 批量评级自检：✘ " + r);
            }
        } catch (Exception e) {
            LogException("TUF 批量评级自检失败", e);
        }
    }


    private static void LogException(string msg, Exception e) {
        try { Lg.Ex("" + msg, e); } catch { }
    }
}
