// [split-of] Updater.cs lines 674-1367 body 14-656

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using UnityEngine;

namespace LevelLibrary;

/// <summary>Updater.cs 的一部分（同一个 partial 类）：版本号比较、版本线路判定、资源包版本记录</summary>
public static partial class Updater {
    /// <summary>这个版本号是不是资源包（纯四位数字，如 `0001`、`0002`）；平台上靠版本号形状区分 mod 包和资源包</summary>
    private static bool IsAssetVersion(string v) {
        if (string.IsNullOrEmpty(v)) return false;
        string s = v.Trim();
        if (s.Length != AssetVersionDigits) return false;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            if (c < '0' || c > '9') return false;
        }
        return true;
    }

    /// <summary>本地已装的资源包版本。返回空串 = 没装过（新玩家，或资源被删了）。
    /// 必须连 assets 目录一起看：记录文件在、但 assets 被手动删过或上次装到一半失败时，要当成“没装”，否则他永远拿不到图。</summary>
    public static string ReadLocalAssetVersion() {
        try {
            string modDir = Main.ModEntry.Path;
            string assetsDir = Path.Combine(modDir, "assets");
            if (!Directory.Exists(assetsDir)) return "";
            bool hasAny = false;
            try {
                foreach (var _ in Directory.EnumerateFileSystemEntries(assetsDir)) { hasAny = true; break; }
            } catch { hasAny = true; }        // 列目录失败就别当成"没装"，免得反复下载
            if (!hasAny) return "";
            string p = Path.Combine(modDir, AssetVerFileName);
            if (!File.Exists(p)) return "";
            string s = File.ReadAllText(p).Trim();
            return IsAssetVersion(s) ? s : "";
        } catch { return ""; }
    }

    /// <summary>把“本地已装资源包版本”作废（写空）：之后下一次检查更新就当成“没装过”，直接下最新的完整包。
    /// 不作废的话 updater 会以为已经装着 0006，那份残缺的包永远补不齐。
    /// 只写我们自己的版本记录，素材一个都不删。</summary>
    public static void ForgetLocalAssetVersion() {
        try {
            WriteLocalAssetVersion("");
            Lg.Info("资源包：本地版本记录已作废（素材不全）→ 下次检查更新会重新下完整包");
        } catch (Exception e) { Lg.Ex("作废资源包版本记录失败", e); }
    }

    /// <summary>记下"资源包已装到哪个版本"。装成功之后才写，写失败只记日志（下次会重装一遍，无害）。</summary>
    private static void WriteLocalAssetVersion(string v) {
        try {
            string p = Path.Combine(Main.ModEntry.Path, AssetVerFileName);
            File.WriteAllText(p, (v ?? "").Trim() + Environment.NewLine, System.Text.Encoding.UTF8);
        } catch (Exception e) { Lg.Ex("更新：写资源包版本记录失败", e); }
        _localAssetVerCache = (v ?? "").Trim();       // 界面立刻显示新版本，不用等下次读盘
        _assetVerLoaded = true;
    }

    /// <summary>资源包版本比较（都是纯四位数字，直接按数值比；解析不出来才退回字符串序）</summary>
    private static int CompareAssetVer(string a, string b) {
        int x, y;
        if (int.TryParse((a ?? "").Trim(), out x) && int.TryParse((b ?? "").Trim(), out y))
            return x == y ? 0 : (x > y ? 1 : -1);
        return string.CompareOrdinal(a ?? "", b ?? "");
    }

    /// <summary>在版本列表里挑最新的资源包（纯四位数字 + 有能下的文件）。
    /// 资源包全是一段，而且不比较游戏版本线：它是图片/音频，3.3.1 和 3.4 通用。
    /// 平台若给文件标了别的游戏版本，PickCompatibleFile 会返回 null，这里自然跳过。</summary>
    private static PickedBest PickBestAssetVersion(ModDetail d, string gameVer) {
        PickedBest best = null;
        if (d == null || d.versions == null) return null;
        foreach (var v in d.versions) {
            if (v == null || !IsAssetVersion(v.versionId)) continue;
            var f = PickCompatibleFile(v, gameVer);
            if (f == null) continue;
            if (best == null || CompareAssetVer(v.versionId, best.ver.versionId) > 0)
                best = new PickedBest { ver = v, file = f };
        }
        return best;
    }

    /// <summary>版本“线路”标识 = 版本号的分段数：3.3.1 线是 `5.2.0.1314.0009`（5 段）、3.4 线是 `5.2.0.0003`（4 段）。
    /// 两条线的号不能互相比大小：逐段比数字会得出“3.3.1 的号永远更新”（它的第三段是 1314，而 3.4 线是 0）。
    /// 所以同线内才比大小；跨线（段数不同）一律视为换线，允许安装。</summary>
    // ================= 新版本号格式：`游戏版本-序号` =================
    // 用 '-' 分隔（不用 '.'）：这样它跟老格式（5.2.0.1314.0059，全点分）形状上就分得开，
    // 于是“这两种格式谁更新”可以用一条明确规则回答，不用去猜数字大小。

    /// <summary>是不是新格式 `游戏版本-序号`（3.3.1-1 / 3.3.1-100 / 3.4-3）：
    /// 左边是“数字点数字”形状（至少两段）、右边是纯数字</summary>
    private static bool IsGameDashVersion(string v) {
        if (string.IsNullOrEmpty(v)) return false;
        int i = v.IndexOf('-');
        if (i <= 0 || i >= v.Length - 1) return false;
        for (int k = i + 1; k < v.Length; k++) { char c = v[k]; if (c < '0' || c > '9') return false; }
        int dots = 0;
        for (int k = 0; k < i; k++) {
            char c = v[k];
            if (c == '.') { dots++; continue; }
            if (c < '0' || c > '9') return false;
        }
        return dots >= 1;
    }

    /// <summary>取游戏版本部分（3.3.1-7 → "3.3.1"）</summary>
    private static string GamePartOf(string v) {
        if (string.IsNullOrEmpty(v)) return "";
        int i = v.IndexOf('-');
        return i > 0 ? v.Substring(0, i) : v;
    }

    /// <summary>取序号部分（3.3.1-7 → 7，拿不到返回 -1）</summary>
    private static long DashSeqOf(string v) {
        if (string.IsNullOrEmpty(v)) return -1;
        int i = v.IndexOf('-');
        long n;
        return (i > 0 && long.TryParse(v.Substring(i + 1), out n)) ? n : -1;
    }

    /// <summary>统一的版本比较，两条格式都走这里，别再各写一份。
    /// 一边新格式、一边老格式 → 新格式更新：过渡期平台上两种都在，老格式最后一个包是过渡包，之后的发布全是新格式；
    /// 不这么定，装了过渡包的人就永远升不到 3.3.1-1。
    /// 都是新格式 → 先比游戏版本（不同就是换线更新，如 3.3.1 → 3.4），再比序号数字（不能比字符串：“3.3.1-10”按字符串比会比“3.3.1-9”小）。
    /// 都是老格式 → 沿用同线比末段数字、跨线逐段比。</summary>
    private static int CompareVersions(string a, string b) {
        bool an = IsGameDashVersion(a), bn = IsGameDashVersion(b);
        if (an != bn) return an ? 1 : -1;
        if (!an) return CompareSameLine(a, b);
        string ag = GamePartOf(a), bg = GamePartOf(b);
        if (!string.Equals(ag, bg, System.StringComparison.Ordinal)) {
            int c = CompareGamePart(ag, bg);      // 这里绝不能用 CompareNumeric，见 CompareGamePart 的说明
            if (c == 0) return string.CompareOrdinal(ag, bg);   // 数字相同但写法不同 → 给个确定结果
            return c;
        }
        long asq = DashSeqOf(a), bsq = DashSeqOf(b);
        if (asq == bsq) return 0;
        return asq > bsq ? 1 : -1;
    }

    /// <summary>比较两串“游戏版本”（3.3.1 / 3.4 那种）：按点分段、缺的段补 0、逐段比数字。
    /// 不能用 CompareNumeric —— 它把串里所有连续数字抽出来逐段比，`3.3.1` 得 `[3,3,1]` 而 `3.4` 只有 `[3,4]`，
    /// 于是两个不同的版本线会被判成相等。
    /// 这条比较只在同一条游戏线内部用于决定升不升级；决定换不换线的是 RemoteIsNewer 里的游戏版本线首判。</summary>
    private static int CompareGamePart(string a, string b) {
        var pa = a == null ? new string[0] : a.Split('.');
        var pb = b == null ? new string[0] : b.Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++) {
            long x, y;
            if (!long.TryParse(i < pa.Length ? pa[i] : "0", out x)) x = 0;
            if (!long.TryParse(i < pb.Length ? pb[i] : "0", out y)) y = 0;
            if (x != y) return x > y ? 1 : -1;
        }
        return 0;
    }

    private static int VersionLine(string v) {
        if (string.IsNullOrEmpty(v)) return 0;
        int n = 0;
        foreach (var p in v.Trim().Split('.')) if (p.Length > 0) n++;
        return n;
    }

    /// <summary>平台版本是否该装到本地。
    /// ① mod 包按游戏版本线分开，本地装的那份不是给当前游戏用的就必须换掉，这跟两个版本号谁大谁小毫无关系；
    /// 只有两边都是新格式（自带游戏版本号）时才做这条首判，老格式号里没有游戏版本，交回比大小。
    /// 判据用 GameVerMatches（精确一致或 major.minor 一致），这样“平台写 3.4、游戏读 3.4.0”也算同线，不会每次误判成要换。
    /// ② 同线才比大小：同线只有平台更大才装（绝不降级），跨线直接视为换线更新、装平台的。
    /// 绝不能只看平台的版本 id 变了 —— 本地比平台新时 id 也不同，那样会把玩家降级。
    /// gameVer 参数：轮询候选版本时不传，真正下“要不要更新”结论时必须传当前游戏版本，否则①这条首判形同虚设。</summary>
    /// <summary>版本号里带的游戏版本，是否与当前游戏版本一致（3.3.1-97 对 3.3.1 为真；
    /// 3.3.3-96 对 3.3.1 为假 —— 那是上传时版本号写错、文件标注却对的那一种）。
    /// 用来把「版本号写错但文件能用」的候选排到正常版本号后面（跳板包就靠它让位）。</summary>
    private static bool VersionGamePartMatchesGameVer(string versionId, string gameVer) {
        if (string.IsNullOrEmpty(versionId) || !IsGameDashVersion(versionId)) return false;
        string g = GamePartOf(versionId);
        if (string.IsNullOrEmpty(g)) return false;
        // 逐段比而不是 major.minor：3.3.3 与 3.3.1 在 major.minor 上相等，会被当成一致 ——
        // 而"一致"正是我们要区分的那件事（跳板包的游戏段多一段，就是靠这个区分出来的）。
        return string.Equals(g.Trim(), (gameVer ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>两个版本号是不是同一条游戏线（3.3.1-91 与 3.3.1-93 同线；3.3.1-91 与 3.4-5 不同线）。
    /// 用 major.minor 比，"3.3.1" 与 "3.3" 也算同线。</summary>
    private static bool SameGameLine(string a, string b) {
        string x = GamePartOf(a), y = GamePartOf(b);
        if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y)) return false;
        return string.Equals(MajorMinor(x), MajorMinor(y), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RemoteIsNewer(string remoteVer, string localVer, string gameVer = null) {
        if (string.IsNullOrEmpty(remoteVer)) return false;
        if (string.IsNullOrEmpty(localVer) || localVer == "?") return true;
        string r = remoteVer.Trim(), l = localVer.Trim();
        if (r == l) return false;
        // ① 本地那份不是给当前游戏用的 → 必须换掉，跟谁大谁小毫无关系。
        // 绝不能加“本地版本号更大就不换”：本地 `3.4-30` 与平台 `3.3.1-29` 一比会变成“不换”，
        // 而 `3.4-30` 本来就不适配 3.3.1；判据只能是“适配不适配当前游戏”。
        // 唯一安全的“不许降级”发生在同一条游戏线内部，那时走下面的 CompareVersions。
        if (!string.IsNullOrEmpty(gameVer) && IsGameDashVersion(r) && IsGameDashVersion(l)) {
            if (!GameVerMatches(GamePartOf(l), gameVer.Trim())) return true;
        }
        // 同一条游戏线（major.minor 一致）只比后头那个序号，跟选版本那一步（PickBestVersion）用同一套规则。
        // 两边规则不一致会出事：本地那份是「3.3.3-95」这种游戏段写错的号、平台最新是「3.3.1-96」时，
        // 选版本会选中 3.3.1-96（序号更大），可这里按游戏段逐段比又判它不比 3.3.3-95 新，于是不更新，
        // 玩家就被夹在中间上不去也下不来。
        // 与选版本那一步（PickBestVersion）用同一套两层规则：
        // ① 平台那个版本号里的游戏段与当前游戏不一致（跳板包：3.3.3-96 标着给 3.3.1 用）时，
        //    本地只要有"版本号与当前游戏一致"的那份就算更新过了 ——
        //    不加这条，玩家拿到跳板包后会一直等那个永远轮不到的正常版本号，停在桥上下不来。
        if (!string.IsNullOrEmpty(gameVer) && IsGameDashVersion(r)
            && !VersionGamePartMatchesGameVer(r, gameVer.Trim())
            && VersionGamePartMatchesGameVer(l, gameVer.Trim())) return false;
        // 同一条游戏线上、且两边都是新格式（X.Y.Z-N）时，只比后头那个序号。
        // 老格式（5.2.0.1314.0058 那种）没有 dash 序号：DashSeqOf 对它返回 -1，
        // 若也走这条分支，两个老版本会被判成序号相同、从而不更新，玩家永远收不到后续的老格式包。
        // 老格式留给下面的 CompareVersions 走 CompareSameLine（前缀相同就比末段数值）。
        if (IsGameDashVersion(r) && IsGameDashVersion(l) && SameGameLine(r, l)) return DashSeqOf(r) > DashSeqOf(l);
        // 跨线才交给统一比较器：新格式恒新，老格式沿用"跨线=换线更新、同线=只升不降"
        return CompareVersions(r, l) > 0;
    }

    /// <summary>把更新说明按版本叠起来（最多 MaxChangelogVersions 个，新的在上），形如 `5.2.0.1314.0017：（日志）` 换行下一条。
    /// 只收与本次选中的版本同一条线的版本 —— 3.3.1 线的玩家不该看到 3.4 线的说明。</summary>
    private static string BuildChangelogHistory(ModDetail d, ModVersion best) {
        try {
            if (d == null || d.versions == null || best == null) return "";
            // 新格式按“游戏版本”分组（3.3.1-1 / 3.3.1-7 同组）；老格式沿用前缀分组
            string lineKey = IsGameDashVersion(best.versionId ?? "")
                ? ("game:" + GamePartOf(best.versionId ?? ""))
                : PrefixOf(best.versionId ?? "");
            var picks = new List<ModVersion>();
            foreach (var v in d.versions) {
                if (v == null || string.IsNullOrEmpty(v.versionId)) continue;
                if (IsAssetVersion(v.versionId)) continue;      // 资源包版本（0001…）不进 mod 的更新说明
                // 同一条线：前缀一致（3.3.1 线是 "5.2.0.1314"；3.4 线是 "5.2.0"）
                string vKey = IsGameDashVersion(v.versionId)
                    ? ("game:" + GamePartOf(v.versionId)) : PrefixOf(v.versionId);
                if (!string.IsNullOrEmpty(lineKey) && vKey != lineKey) continue;
                picks.Add(v);
            }
            if (picks.Count == 0) picks.Add(best);
            // 新的排前面（比数字，不能比字符串 —— "…0010" 会被认为比 "…0009" 小）
            picks.Sort((a, b) => CompareVersions(b.versionId, a.versionId));   // 统一的版本比较器

            var sb = new System.Text.StringBuilder();
            int shown = 0;
            foreach (var v in picks) {
                if (shown >= MaxChangelogVersions) break;
                string note = v.changelog == null ? "" : v.changelog.Trim();
                if (note.Length == 0) note = "（无说明）";
                note = note.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                // 单条太长就截断：否则一条超长日志会把后面 14 个版本全挤掉
                if (note.Length > MaxChangelogPerEntry) note = note.Substring(0, MaxChangelogPerEntry) + "…";
                string block = v.versionId + "：" + note;
                if (shown > 0) {
                    if (sb.Length + 1 + block.Length > MaxChangelogChars) break;
                    sb.Append('\n');
                }
                sb.Append(block);
                shown++;
            }
            return sb.ToString();
        } catch (Exception e) {
            Lg.Ex("拼更新说明历史失败", e);
            return "";
        }
    }

    /// <summary>同一条线内的版本比较（&gt;0 表示 a 更新）。</summary>
    private static int CompareSameLine(string a, string b) {
        string ap = PrefixOf(a), bp = PrefixOf(b);
        if (!string.IsNullOrEmpty(ap) && ap == bp) {
            // 前缀相同 → 只比最后一段的数值（整串比字符串会在末尾位数变化时判错：“…10000”会被认为比“…9999”小）
            long an, bn;
            if (long.TryParse(LastSegment(a), out an) && long.TryParse(LastSegment(b), out bn)) {
                if (an == bn) return 0;
                return an > bn ? 1 : -1;
            }
            return string.CompareOrdinal(a, b);   // 末段不是纯数字时的兜底
        }
        return CompareNumeric(a, b);              // 前缀不同 → 逐段比数字
    }

    /// <summary>取最新版本号里最后一个 '.' 之前的部分当作前缀（5.2.0.1314.0005 → "5.2.0.1314"）</summary>
    private static string PrefixOf(string v) {
        int i = v.LastIndexOf('.');
        return i > 0 ? v.Substring(0, i) : "";
    }

    /// <summary>取版本号最后一段（5.2.0.1314.0005 → "0005"）</summary>
    private static string LastSegment(string v) {
        int i = v.LastIndexOf('.');
        return i >= 0 && i + 1 < v.Length ? v.Substring(i + 1) : v;
    }

    /// <summary>兜底比较：把连续数字抽出来逐段比大小（前缀不一致时才用）</summary>
    private static int CompareNumeric(string a, string b) {
        List<long> pa = Numbers(a), pb = Numbers(b);
        int n = Math.Max(pa.Count, pb.Count);
        for (int i = 0; i < n; i++) {
            long x = i < pa.Count ? pa[i] : 0;
            long y = i < pb.Count ? pb[i] : 0;
            if (x != y) return x > y ? 1 : -1;
        }
        return 0;
    }

    private static List<long> Numbers(string s) {
        var list = new List<long>();
        long cur = 0; bool has = false;
        foreach (char c in s) {
            if (c >= '0' && c <= '9') { cur = cur * 10 + (c - '0'); has = true; }
            else { if (has) { list.Add(cur); cur = 0; has = false; } }
        }
        if (has) list.Add(cur);
        return list;
    }

    /// <summary>拿到详情：比版本号判定有没有更新的版本，有就去取下载直链</summary>
    private static void HandleDetail(string json) {
        if (string.IsNullOrEmpty(json)) {
            Phase = 5; Status = "返回内容为空";
            Lg.Info("更新：详情响应为空");
            return;
        }
        ModDetail d = null;
        try { d = JsonConvert.DeserializeObject<ModDetail>(json); } catch (Exception e) {
            Phase = 5; Status = "返回内容解析失败";
            // 解析失败必须把原文和异常细节记下来，否则完全没法查
            Lg.Info($"[LevelLib] 更新：解析详情失败 {e.GetType().Name}: {e.Message}"
                + $" | 响应长度={json.Length} | 前 300 字={Trim(json, 300)}");
            return;
        }
        if (d == null) {
            Phase = 5; Status = "平台返回里没有版本信息";
            Lg.Info("更新：详情解析结果为空\n" + Trim(json, 400));
            return;
        }
        // 「下载默认视频背景」那条线复用同一个详情请求：只挑目标资源包，不做任何版本比较
        if (_dlvActive) { HandleVideoDetail(d); return; }
        string local = ReadLocalVersion();
        RefreshLocalAssetVersion();
        string gv = GameVersion();

        // ================= 两条更新线，一次请求一起判 =================
        // ① mod 包：不看平台的“最新版本”标记，而是从 versions[] 里挑最新的、且适配当前游戏版本的那一条（这样作者能同时挂 3.3.1 / 3.4 的包）
        // ② 资源包：同一条列表里，版本号是纯四位数字的那些（见 IsAssetVersion）
        // 两条线合起来最多要下载两个文件，下面的队列负责排队；都没事就一个字节都不下。
        var best = PickBestVersion(d, gv, out int skippedNewer, out int skippedIncompat);
        var bestAsset = PickBestAssetVersion(d, gv);
        string remoteVer = (best != null && best.ver != null) ? best.ver.versionId : "";
        string remoteAssetVer = (bestAsset != null && bestAsset.ver != null) ? bestAsset.ver.versionId : "";

        if (best == null && bestAsset == null) {
            Phase = 0;
            DetailLoaded = true;      // 平台是问到了，只是没有适配当前游戏的版本 → 别再显示“正在获取”
            Status = $"平台上没有适配当前游戏 {gv} 的版本";
            Lg.Info($"[LevelLib] 更新：没有任何适配游戏 {gv} 的版本；"
                + $"共 {d.versions?.Count ?? 0} 个版本；各文件支持版本：{DescribeFiles(d)}");
            if (_manual) PortalUI.ShowToast(Status);
            return;
        }

        // 第三个参数是当前游戏版本，不能省 —— 它负责“本地装的是别条线的包，必须换掉”这条首判
        bool modNeed = best != null && best.file != null && RemoteIsNewer(remoteVer, local, gv);
        // 资源包：没装过就直接装最新（新玩家的路径），装过就只升不降
        bool assetNeed = bestAsset != null && bestAsset.file != null
                         && (string.IsNullOrEmpty(LocalAssetVersion)
                             || CompareAssetVer(remoteAssetVer, LocalAssetVersion) > 0);

        if (best != null && best.ver != null) {
            NewChangelog = best.ver.changelog ?? "";
            // 更新说明叠版本显示（最多最近 15 个版本，新的在上）
            ChangelogHistory = BuildChangelogHistory(d, best.ver);
            // 顺手落盘一份（启动页第一眼就能显示，不必等这次请求）+
            // 标记“平台详情拿到过”—— 这句必须在早退之前：mod 已是最新时也会走到这里，
            // 否则欢迎页在请求回来之前就建好了，会看不到更新公告
            DetailLoaded = true;
            SaveCachedChangelog(ChangelogHistory);
        }

        string assetLocalTxt = string.IsNullOrEmpty(LocalAssetVersion) ? "未安装" : LocalAssetVersion;
        if (_lastCmpKey != remoteVer + "|" + local + "|" + remoteAssetVer + "|" + assetLocalTxt) {
            _lastCmpKey = remoteVer + "|" + local + "|" + remoteAssetVer + "|" + assetLocalTxt;
            Lg.Info($"[LevelLib] 更新：版本比较 mod 本地={local} 平台(适配{gv})={remoteVer} → 要更新? {modNeed}"
                + $"；资源包 本地={assetLocalTxt} 平台={remoteAssetVer} → 要更新? {assetNeed}"
                + $"（另有 {skippedNewer} 个更新的 mod 版本不适配当前游戏、{skippedIncompat} 个版本跳过）");
        }

        if (!modNeed && !assetNeed) {
            Phase = 0;
            if (best != null && best.ver != null && !string.IsNullOrEmpty(best.ver.id)) LastSeenVersion = best.ver.id;
            bool sameVer = string.Equals(remoteVer.Trim(), local.Trim(), StringComparison.Ordinal);
            Status = (sameVer ? $"mod 已是最新（{local}）" : $"mod 本地版本比平台新（本地 {local} ／ 平台适配 {gv} 的最新 {remoteVer}），不更新")
                   + $"，资源包已是最新（{assetLocalTxt}）";
            if (_manual) PortalUI.ShowToast(Status);
            return;
        }

        // 排队：先 mod 包（新代码先落盘），再资源包
        _jobs.Clear();
        _runNotes.Clear();
        if (modNeed) {
            NewVersion = remoteVer;
            if (best.ver != null && !string.IsNullOrEmpty(best.ver.id)) LastSeenVersion = best.ver.id;
            _jobs.Add(new UpdateJob { IsAsset = false, FileId = best.file.id, VersionId = remoteVer });
            Lg.Info($"[LevelLib] 更新：发现新的 mod 包 本地={local} 平台={remoteVer}"
                + $" 适配游戏={best.file.GameVer} 当前游戏={gv}"
                + $" 说明长度={(NewChangelog != null ? NewChangelog.Length : 0)}");
        }
        if (assetNeed) {
            NewAssetVersion = remoteAssetVer;
            _jobs.Add(new UpdateJob { IsAsset = true, FileId = bestAsset.file.id, VersionId = remoteAssetVer });
            Lg.Info($"[LevelLib] 更新：发现新的资源包 本地={assetLocalTxt} 平台={remoteAssetVer}"
                + $" 适配游戏={bestAsset.file.GameVer}");
        }
        StartNextJob();
    }

    /// <summary>当前下载/安装的是不是资源包（队列为空时按 false 处理 = 当作 mod 包）</summary>
    private static bool IsCurrentJobAsset() {
        var j = _curJob;
        return j != null && j.IsAsset;
    }

    /// <summary>从队列里取下一个要装的东西开始下载；队列空了就收尾。
    /// 必须排队：一次检查可能同时有新 DLL 和新资源包，而下载/安装同一时刻只能跑一个。</summary>
    private static void StartNextJob() {
        if (_jobs.Count == 0) {
            _curJob = null;
            FinishRun();
            return;
        }
        _curJob = _jobs[0];
        _jobs.RemoveAt(0);
        _pendingFileId = _curJob.FileId;
        retryCount = 0;
        Phase = 2; Progress = 0f;
        Status = _curJob.VideoOnly
            ? $"正在获取默认视频背景下载地址…（{_curJob.VersionId}）"
            : _curJob.IsAsset
            ? $"正在获取资源包下载地址…（{_curJob.VersionId}）"
            : $"正在获取下载地址…（本地 {ReadLocalVersion()} → 平台 {_curJob.VersionId}）";
        Lg.Info($"[LevelLib] 更新：开始下载 "
            + (_curJob.VideoOnly ? "默认视频背景" : _curJob.IsAsset ? "资源包" : "mod 包")
            + $" {_curJob.VersionId}（file={_curJob.FileId}，队列剩余 {_jobs.Count} 个）");
        StartPost($"{BaseUrl}/api/mod-files/{_curJob.FileId}/download-intent", 2);   // 必须 POST
    }

    /// <summary>两条线都跑完了：把这一轮的结果拼成一句话告诉玩家。
    /// 措辞：只装资源包 → `资源包已更新至 0002`；两条都装 → `资源包更新至 0002，mod 更新至 5.2.0.1314.0038`；只装 mod → `mod 已更新至 5.2.0.1314.0038`。
    /// 不弹窗，但用成就样式的提示（ShowTempAchHint）告诉他一声 —— 那只是临时提示，不是真成就：
    /// 不写 achievements.txt、不进成就面板、不解锁任何东西。</summary>
    private static void FinishRun() {
        if (_runNotes.Count == 0) return;
        Phase = 4; Progress = 1f;
        string detail = string.Join("；", _runNotes.ToArray());
        _runNotes.Clear();
        bool problems = _runDeferred || _runFailed;
        string head;
        if (_runAssetDone && _runModDone) head = $"资源包更新至 {NewAssetVersion}，mod 更新至 {NewVersion}";
        else if (_runAssetDone) head = $"资源包已更新至 {NewAssetVersion}";
        else if (_runModDone) head = $"mod 已更新至 {NewVersion}";
        else head = detail;                       // 全失败时的兜底（正常走不到）
        // 把“重启后生效”写成明确的祈使句
        Status = problems ? $"{head} —— 有文件没换上，请重启游戏（重启后会自动补上）"
                          : $"{head} —— 请重启游戏";
        PortalUI.ShowTempAchHint(problems ? $"{head}（有文件没换上，请重启游戏后自动补）" : $"{head}　请重启游戏");
        Lg.Info($"[LevelLib] 更新：本轮结束 —— {Status}（明细：{detail}）");
        _runDeferred = false; _runFailed = false;
        _runModDone = false; _runAssetDone = false;
    }

    /// <summary>拿到下载直链 → 开始下载文件本体</summary>
    private static void HandleIntent(string json) {
        if (string.IsNullOrEmpty(json)) {
            Phase = 5; Status = "下载地址响应为空";
            Lg.Info("更新：download-intent 响应为空");
            if (_dlvActive) FailVideoDl("下载地址响应为空");
            return;
        }
        DownloadIntent r = null;
        try { r = JsonConvert.DeserializeObject<DownloadIntent>(json); } catch (Exception e) {
            Phase = 5; Status = "解析下载地址失败";
            Lg.Info($"[LevelLib] 更新：解析 download-intent 失败 {e.GetType().Name}: {e.Message}"
                + $" | 前 200 字={Trim(json, 200)}");
            if (_dlvActive) FailVideoDl("下载地址解析失败");
            return;
        }
        if (r == null || string.IsNullOrEmpty(r.url)) {
            Phase = 5; Status = "平台没有返回下载地址";
            Lg.Info("更新：download-intent 里没有 url\n" + Trim(json, 300));
            if (_dlvActive) FailVideoDl("平台没有返回下载地址");
            return;
        }
        _pendingDownloadUrl = r.url;
        _pendingFileName = string.IsNullOrEmpty(r.fileName) ? "update.zip" : r.fileName;
        retryCount = 0;                      // 直链到手，进入下载阶段，重试计数归零
        Phase = 2; Progress = 0f;
        Status = IsCurrentJobAsset() ? "正在更新资源包… 下载 0%"
               : (_curJob != null && _curJob.VideoOnly) ? "正在下载默认视频背景… 0%"
               : "正在更新 mod… 下载 0%";
        StartGet(_pendingDownloadUrl, 3);   // 预签名直链：GET，且不带 Authorization 头
    }

    /// <summary>下载完成 → 交给后台线程去解压 + 替换，主线程只等结果（以前同步跑完要卡好几秒）。
    /// 关于 DLL：实测这个游戏运行时并不锁 mod 的 DLL，能直接覆盖；万一某些环境锁了，就把被占用的旧文件改名让位
    /// （Windows 不允许覆盖被占用的文件，但允许改名），再把新文件写上去，旧文件等下次启动清理。两条路都失败才等下次启动。</summary>
    private static void HandleDownloaded(byte[] data) {
        if (data == null || data.Length == 0) {
            Phase = 5; Status = "下载到的文件是空的";
            if (_dlvActive) FailVideoDl("下载到的文件是空的");
            return;
        }
        bool isVideoJob = _curJob != null && _curJob.VideoOnly;
        _installData = data;
        _installIsAsset = IsCurrentJobAsset();   // 决定后台那段"素材默认跳过"要不要生效
        _installing = true;
        _installDone = false;
        _installError = false;
        _installDllApplied = false;
        _installDllDeferred = false;
        _installFailed = 0;
        _installReplaced = 0;
        _installSkipped = 0;
        _installProgress = 0f;
        lock (_installMsgs) _installMsgs.Clear();
        Phase = 3;
        Progress = 0f;
        Status = isVideoJob ? "正在解压默认视频背景…（后台进行，不会卡顿）"
               : _installIsAsset ? "正在更新资源包… 安装中（后台进行，不会卡顿）"
               : "正在更新 mod… 安装中（后台进行，不会卡顿）";
        _installT0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _installT1 = 0;
        _tInstall0 = Time.unscaledTime;
        _installStartFrameGap = Time.unscaledTime - _tDecode;   // "起后台线程"这一帧的帧间隔
        _installWorstFrame = 0f;
        _installLastFrameAt = Time.unscaledTime;
        // 不用 ThreadPool：TogetherBootstrap 这个 mod 把初始化丢到共享线程池上，并在那条线程里调主线程专属的 Unity API，
        // 实测会把游戏打崩（托管堆栈就是它：ThreadPoolWorkQueue:Dispatch → TogetherBootstrap → JobDispatcher）。
        // 往线程池塞任务会跟它抢线程、把那个偶发 bug 撞出来，所以这里开独立线程，不参与共享池的调度。
        try {
            // 默认视频背景那条线走自己的解压线程：只取一个文件、不动版本记录（见 Updater.DefaultVideo.cs）
            var th = new System.Threading.Thread(
                isVideoJob ? (System.Threading.ThreadStart)DefaultVideoWorker : InstallWorker);
            th.IsBackground = true;          // 游戏一退出就跟着结束，别拖着进程
            th.Name = "LevelLibraryUpdater";
            th.Priority = System.Threading.ThreadPriority.BelowNormal;   // 别跟游戏抢 CPU
            th.Start();
        } catch (Exception e) {
            _installing = false; _installData = null;
            Phase = 5; Status = "起后台安装线程失败：" + e.GetType().Name;
            Lg.Ex("更新：起后台安装线程失败", e);
        }
    }

    /// <summary>后台线程：整段安装都在这儿跑，不碰 Unity API 和 UI。</summary>
    private static void InstallWorker() {
        string modDir = Main.ModEntry.Path;
        string stageDir = Path.Combine(modDir, StageDirName);
        string zipPath = Path.Combine(stageDir, _pendingFileName);
        int replaced = 0, failed = 0, skipped = 0;   // skipped = 内容相同、直接跳过的文件数
        bool dllApplied = false;      // DLL 是不是已经换上了
        bool dllDeferred = false;     // 覆盖 + 改名让位都失败（罕见）→ 留到下次启动
        var failedFiles = new List<string>();
        byte[] data = _installData;
        try {
            // ① 暂存目录：先在这儿解压好，避免"解压一半失败"把 mod 目录搞成半个版本
            if (Directory.Exists(stageDir)) { try { Directory.Delete(stageDir, true); } catch { } }
            Directory.CreateDirectory(stageDir);
            File.WriteAllBytes(zipPath, data);
            _installProgress = 0.02f;

            string extractDir = Path.Combine(stageDir, "files");
            Directory.CreateDirectory(extractDir);
            using (var zip = ZipFile.OpenRead(zipPath)) {
                int i = 0, total = zip.Entries.Count;
                foreach (var entry in zip.Entries) {
                    i++;
                    if (i % 10 == 0) _installProgress = 0.02f + 0.33f * ((float)i / Math.Max(1, total));
                    if (string.IsNullOrEmpty(entry.Name)) continue;              // 目录项
                    string rel = entry.FullName.Replace('\\', '/');
                    // 去掉包内第一层目录 —— 只在那层恰好叫 LevelLibrary 时去掉。
                    // 无条件砍第一段时，包内根目录直接是 assets/ 的那种包会把图写进 mod 根目录（图全丢），所以现在按名字判。
                    int slash = rel.IndexOf('/');
                    if (slash > 0 && rel.Substring(0, slash).Equals(PackageRootName, StringComparison.OrdinalIgnoreCase))
                        rel = rel.Substring(slash + 1);                          // 去掉包内第一层 LevelLibrary/
                    if (string.IsNullOrEmpty(rel) || rel.Contains("..")) continue;   // 防目录穿越
                    string dest = Path.Combine(extractDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    string dd = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dd) && !Directory.Exists(dd)) Directory.CreateDirectory(dd);
                    entry.ExtractToFile(dest, true);
                }
            }
            _installProgress = 0.35f;

            // ② 搬进 mod 目录。两条线的规则不一样：
            // - mod 包（IsAsset=false）：图片/音频这类素材默认不更新，既不比对也不写盘 ——
            // 素材几乎从不改动，而“逐个文件读目标盘比对内容”是最贵的一步（占整段安装八成以上时间），
            // 跳过之后 mod 包实际只有 DLL + Info.json 两个文件要处理；目标不存在的新素材照常复制（不然新版本引用的图会缺）。
            // - 资源包（IsAsset=true）：素材就是要装的东西，一律正常比对 + 写盘，跳过就没有意义了。
            bool isAssetPack = _installIsAsset;
            var all = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            int skippedMedia = 0;
            for (int i = 0; i < all.Length; i++) {
                string src = all[i];
                _installProgress = 0.35f + 0.6f * ((float)i / Math.Max(1, all.Length));
                string rel = src.Substring(extractDir.Length).TrimStart(Path.DirectorySeparatorChar);
                string dest = Path.Combine(modDir, rel);
                string destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                bool isDll = rel.EndsWith("LevelLibrary.dll", StringComparison.OrdinalIgnoreCase);

                // 素材且已经存在 → 直接不管它（不比对、不写盘）；资源包不适用
                if (!isAssetPack && IsMediaAsset(rel) && File.Exists(dest)) {
                    skippedMedia++;
                    if (isDll) dllApplied = true;
                    continue;
                }

                // 要复制的文件：先比大小（便宜的快速否决），大小一样再比内容。
                // 只有 DLL / Info.json 这类非素材文件会走到这里。
                if (File.Exists(dest)) {
                    var fi1 = new FileInfo(src);
                    var fi2 = new FileInfo(dest);
                    if (fi1.Length == fi2.Length && SameFile(src, dest)) {
                        skipped++;
                        if (isDll) dllApplied = true;   // DLL 内容也一样 → 本来就是这一版
                        continue;
                    }
                }

                if (CopyWithUnlock(src, dest)) {
                    replaced++;
                    if (isDll) dllApplied = true;
                } else {
                    failed++;
                    failedFiles.Add(rel);
                    InstallLog("[LevelLib] 更新：替换失败 " + rel);
                    if (isDll) {
                        // 罕见路径：DLL 覆盖失败且改名让位也没成功 → 暂存 .new，下次启动换上
                        try {
                            File.Copy(src, Path.Combine(modDir, PendingDllName), true);
                            dllDeferred = true;
                        } catch (Exception ex) { InstallLog("更新：暂存 DLL 失败 " + ex.Message); }
                    }
                }

            }
            InstallLog($"[LevelLib] 更新：{(isAssetPack ? "资源包" : "mod 包")}安装 —— 素材跳过={skippedMedia} 个"
                + $"（{(isAssetPack ? "资源包不跳过素材，这个数应该是 0" : "mod 包默认不碰素材")}）");
            if (isAssetPack) {
                // 排查用：把包内顶层目录记下来，万一资源包的目录结构打错了（比如根目录不是 assets/），一眼就能看出来
                try {
                    var tops = new List<string>();
                    foreach (var f in all) {
                        string r = f.Substring(extractDir.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
                        int sl = r.IndexOf('/');
                        string top = sl > 0 ? r.Substring(0, sl) : "(根目录文件)";
                        if (!tops.Contains(top)) tops.Add(top);
                        if (tops.Count >= 6) break;
                    }
                    InstallLog("[LevelLib] 更新：资源包顶层目录 = " + string.Join(", ", tops.ToArray()) + $"（共 {all.Length} 个文件）");
                } catch { }
            }
            _installProgress = 0.97f;

            // ③ 记录清单（排查用）+ 清掉下载的 zip
            try {
                File.WriteAllText(Path.Combine(stageDir, "installed.txt"),
                    "kind=" + (isAssetPack ? "asset" : "mod") + Environment.NewLine
                    + "platformVersion=" + (_curJob != null ? _curJob.VersionId : NewVersion) + Environment.NewLine
                    + "installedAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine
                    + "filesReplaced=" + replaced + Environment.NewLine
                    + "filesSkipped=" + skipped + Environment.NewLine
                    + "failed=" + failed + Environment.NewLine
                    + (failedFiles.Count > 0 ? "failedFiles=" + string.Join(",", failedFiles) + Environment.NewLine : "")
                    + "dllApplied=" + dllApplied + Environment.NewLine
                    + "dllDeferred=" + dllDeferred + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            } catch { }
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(extractDir, true); } catch { }
            CleanupOldFiles(modDir);

            if (dllDeferred) WriteApplyScript();
            _installReplaced = replaced;
            _installSkipped = skipped;
            _installFailed = failed;
            _installDllApplied = dllApplied;
            _installDllDeferred = dllDeferred;
            _installProgress = 1f;
        } catch (Exception e) {
            _installError = true;
            InstallLog("安装更新失败 " + e.GetType().Name + ": " + e.Message);
        } finally {
            _installData = null;      // 5.4MB 的字节数组早点放掉
            _installT1 = System.Diagnostics.Stopwatch.GetTimestamp();
            _installDone = true;
        }
    }

}
