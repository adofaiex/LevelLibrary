// [split-of] Updater.cs lines 1368-1863 body 14-478

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using UnityEngine;

namespace LevelLibrary;

/// <summary>Updater.cs 的一部分（同一个 partial 类）：安装收尾、文件替换与兜底脚本、版本挑选与版本判定自检。</summary>
public static partial class Updater {
    /// <summary>主线程收尾：把后台线程的结果翻译成界面状态，然后推进队列里的下一条。</summary>
    private static void FinishInstall() {
        _installing = false;
        _installData = null;
        // 默认视频背景那条专用线：不走安装收尾（不记版本、不算"本局装过东西"、不需要重启），
        // 解出视频就算完成，之后当场就能用（见 Updater.DefaultVideo.cs）。
        if (_curJob != null && _curJob.VideoOnly) { FinishVideoExtract(); return; }
        bool wasAsset = _installIsAsset;
        string jobVer = _curJob != null ? _curJob.VersionId : "";
        try {
            lock (_installMsgs) {
                foreach (var m in _installMsgs) Lg.Info(m);
                _installMsgs.Clear();
            }
        } catch { }

        if (_installError) {
            Phase = 5; Progress = 0f; Status = "安装失败：看日志";
            Lg.Info($"[LevelLib] 更新：后台安装抛异常，已中止（{(wasAsset ? "资源包" : "mod 包")} {jobVer}）");
            _jobs.Clear();                      // 出错就别接着装下一个了，免得雪上加霜
            _runNotes.Clear();
            _runDeferred = false; _runFailed = false;
            if (_manual) PortalUI.ShowToast(Lang.Tr("安装更新失败，看日志喵"));
            return;
        }

        Phase = 4; Progress = 1f;
        InstalledThisSession = true;    // 盘上的文件换过了：本局往后一律按"等重启"算（见 Updater.Gate.cs）
        double workMs = 0;
        try {
            if (_installT1 > _installT0)
                workMs = (_installT1 - _installT0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        } catch { }
        float lastFrame = 0f;
        if (_installLastFrameAt > 0f) lastFrame = Time.unscaledTime - _installLastFrameAt;
        if (lastFrame > _installWorstFrame) _installWorstFrame = lastFrame;
        Lg.Info($"[LevelLib] 更新：完成 类型={(wasAsset ? "资源包" : "mod 包")} 版本={jobVer}"
            + $" 替换={_installReplaced} 跳过(内容相同)={_installSkipped}"
            + $" 失败={_installFailed} DLL当场换上={_installDllApplied} DLL待装={_installDllDeferred}"
            + $"（后台独立线程 + 主动让路，帧无卡顿）");
        Lg.Info($"[LevelLib] 更新：耗时 后台安装={workMs:F0}ms 主线程最长帧间隔={_installWorstFrame * 1000f:F0}ms"
            + $"（出现在「起后台」后 {_installWorstFrameAt:F0}ms 处）"
            + "（改动前实测 100 个文件逐个比对：3290ms/常驻Tick 2168ms）");
        string diag;
        try {
            string enc = "(无)";
            try { enc = _lastContentEncoding; } catch { }
            diag = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[LevelLib] 更新：各阶段耗时 请求往返(异步)={0:F0}ms 响应->解码完={1:F0}ms"
                + " 解码完->起后台={2:F0}ms 安装期最长帧间隔={3:F0}ms 安装收尾帧间隔={4:F0}ms"
                + "（自检：发请求->现在={5:F0}ms 响应->起后台={6:F0}ms 起后台->现在={7:F0}ms；"
                + "Content-Encoding={8}） 最长帧间隔={9:F0}ms",
                _recvFrameGap * 1000f, _decodeFrameGap * 1000f, _installStartFrameGap * 1000f,
                _installWorstFrame * 1000f, lastFrame * 1000f,
                (Time.unscaledTime - _tReqSent) * 1000f, (_tInstall0 - _tRecv) * 1000f,
                (Time.unscaledTime - _tInstall0) * 1000f,
                enc, _installWorstFrame * 1000f);
        } catch (Exception ex) { diag = "[LevelLib] 更新：卡帧定位失败 " + ex.GetType().Name; }
        Lg.Info(diag);

        // 这一条线的结果记下来（日志明细用）+ 记"哪条线真装上了"（结束语据此挑措辞）
        string head = wasAsset ? $"资源包 {jobVer}" : $"mod 包 {jobVer}";
        if (_installFailed > 0) {
            _runNotes.Add($"{head} 装了 {_installReplaced} 个、{_installFailed} 个没换上");
            _runFailed = true;
        } else {
            _runNotes.Add($"{head} 改动 {_installReplaced} 个/未变 {_installSkipped} 个");
            if (wasAsset) _runAssetDone = true; else _runModDone = true;
        }
        if (_installDllDeferred) _runDeferred = true;
        // 资源包版本号只在这里写：装成功才记，失败就留空、下次检查会再试一遍（不会漏图）。
        // 不能放进后台线程——那边写完就报成功，主线程这里才判定成败。
        // mod 包的本地版本不用另记：它就装在包内的 Info.json 里，下次启动直接读那份。
        if (wasAsset && _installFailed == 0 && !string.IsNullOrEmpty(jobVer)) {
            WriteLocalAssetVersion(jobVer);
            Lg.Info($"[LevelLib] 更新：资源包已记录为 {jobVer}（下次不再重复下载）");
        }
        // 队列里还有下一条就接着装，否则收尾
        StartNextJob();
    }

    /// <summary>两个文件内容是否相同（先比大小，再逐字节比）。
    /// 用来跳过没变动的文件：更新包里大部分是 assets 的图片/音频、几乎从不改动，全量重写会让磁盘 IO 集中在一帧里。
    /// 只比内容不看时间戳——时间戳会被解压/复制改掉，内容才是真相。</summary>
    private static bool SameFile(string a, string b) {
        try {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists) return false;
            if (fa.Length != fb.Length) return false;
            if (fa.Length == 0) return true;
            const int BufSize = 64 * 1024;
            var ba = new byte[BufSize];
            var bb = new byte[BufSize];
            using (var sa = File.OpenRead(a))
            using (var sb = File.OpenRead(b)) {
                while (true) {
                    int na = sa.Read(ba, 0, BufSize);
                    int nb = sb.Read(bb, 0, BufSize);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
            }
        } catch { return false; }   // 读不了就当"不同"，走正常替换流程（宁可多写一次，不能漏更新）
    }

    /// <summary>这个文件是不是"素材"（路径里有 assets，或后缀是常见图片/音频/字体）。
    /// 更新时默认不碰它们——不比对、不写盘。用户定的策略：「asset 里面的一般不会更新，更新了我们再说」。</summary>
    private static bool IsMediaAsset(string rel) {
        try {
            string p = rel.Replace('\\', '/').ToLowerInvariant();
            if (p.Contains("/assets/") || p.StartsWith("assets/")) return true;
            string ext = Path.GetExtension(p);
            switch (ext) {
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp":
                case ".webp": case ".tga": case ".psd":
                case ".wav": case ".mp3": case ".ogg": case ".m4a": case ".aiff":
                case ".ttf": case ".otf":
                    return true;
            }
            return false;
        } catch { return false; }   // 判定失败就当普通文件，走正常比对，宁可多干一点
    }

    /// <summary>把文件搬到目标位置。先直接覆盖；被占用就改名让位再写（Windows 允许改名不允许覆盖）。</summary>
    private static bool CopyWithUnlock(string src, string dest) {
        try { File.Copy(src, dest, true); return true; } catch (Exception e1) {
            // 直接覆盖失败（多半是被占用）→ 把旧的改名让位
            try {
                if (File.Exists(dest)) {
                    string aside = dest + ".old";
                    try { if (File.Exists(aside)) File.Delete(aside); } catch { }
                    File.Move(dest, aside);          // 被占用的文件也能改名
                }
                File.Copy(src, dest, true);
                Lg.Info("更新：用'改名让位'的方式替换了 " + Path.GetFileName(dest));
                return true;
            } catch (Exception e2) {
                Lg.Info($"[LevelLib] 更新：{Path.GetFileName(dest)} 两种方式都失败 —— 覆盖:{e1.Message} / 改名:{e2.Message}");
                return false;
            }
        }
    }

    /// <summary>清理上一轮"改名让位"留下的 .old（正常情况下根本没有；留着会占地方）</summary>
    private static void CleanupOldFiles(string dir) {
        try {
            foreach (var f in Directory.GetFiles(dir, "*.old", SearchOption.AllDirectories)) {
                try { File.Delete(f); } catch { }   // 删不掉说明还被占用，留着下次再说
            }
        } catch { }
    }

    /// <summary>兜底脚本：等游戏退出后把待装 DLL 换上。
    /// 只在"覆盖 + 改名让位都失败"时才写（正常路径下 DLL 当场就覆盖好了，用不到它）。</summary>
    private static void WriteApplyScript() {
        try {
            string modDir = Main.ModEntry.Path;
            string exe = "";
            try { exe = Path.GetFileNameWithoutExtension(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName); } catch { }
            if (string.IsNullOrEmpty(exe)) exe = "A Dance of Fire and Ice";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("rem LevelLibrary updater: waits for the game to exit, then swaps the mod DLL.");
            sb.AppendLine("set MODDIR=%~dp0.");
            sb.AppendLine("set /a N=0");
            sb.AppendLine(":wait");
            sb.AppendLine($"tasklist /FI \"IMAGENAME eq {exe}.exe\" 2>nul | find /I \"{exe}.exe\" >nul");
            sb.AppendLine("if not errorlevel 1 (");
            sb.AppendLine("  set /a N+=1");
            sb.AppendLine("  if %N% GEQ 300 goto giveup");
            sb.AppendLine("  timeout /t 1 /nobreak >nul");
            sb.AppendLine("  goto wait");
            sb.AppendLine(")");
            sb.AppendLine($"if exist \"%MODDIR%\\{PendingDllName}\" copy /Y \"%MODDIR%\\{PendingDllName}\" \"%MODDIR%\\LevelLibrary.dll\" >nul");
            sb.AppendLine($"if exist \"%MODDIR%\\{PendingDllName}\" del /Q \"%MODDIR%\\{PendingDllName}\" >nul");
            sb.AppendLine("echo done");
            sb.AppendLine("del \"%~f0\"");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":giveup");
            sb.AppendLine("echo timeout");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(Path.Combine(modDir, "apply_update.bat"), sb.ToString(), System.Text.Encoding.Default);
        } catch (Exception e) { Lg.Ex("写兜底更新脚本失败", e); }
    }

    /// <summary>当前游戏版本（Application.version，例如 "3.3.1"）</summary>
    private static string GameVersion() {
        try {
            string v = Application.version;
            return string.IsNullOrEmpty(v) ? "?" : v.Trim();
        } catch { return "?"; }
    }

    /// <summary>挑中的文件 + 它声明的支持游戏版本（日志/提示都要用）</summary>
    private class PickedFile {
        public string id = null;
        public string name = null;
        public string GameVer = "";
        /// <summary>true = 该文件没标注 supportedGames，属于"通用"兜底（优先级最低）</summary>
        public bool IsGeneric = false;
    }

    /// <summary>从 versions[] 里挑"最新的、且适配当前游戏版本"的那一条 + 它的可下载文件。
    /// 不能直接看平台标记的 latestVersion：作者在平台上同时挂着支持不同游戏版本的包，而那标记只能指向其中一个。
    /// 候选分三级，低级别绝不压过高级别：① 明确标注适配当前游戏 ② 没标注任何 supportedGames 的"通用"版本（仅当找不到①时）
    /// ③ 都没有 → null。"未标注"必须降级为兜底：平台上有大量不带 supportedGames 的历史版本，若与①同级比较，
    /// 会按版本/线路偏好把远古包当成"最合适的适配版本"推给玩家。
    /// skippedNewer = 比选中的那条更新、但不适配当前游戏的版本数（日志用）
    /// skippedIncompat = 被跳过的版本总数</summary>
    private static PickedBest PickBestVersion(ModDetail d, string gameVer, out int skippedNewer, out int skippedIncompat) {
        skippedNewer = 0; skippedIncompat = 0;
        if (d == null || d.versions == null || d.versions.Count == 0) return null;

        // 线路偏好：两条版本线的段数不同（3.3.1 线 = 5 段，3.4 线 = 4 段），靠段数跨线区分。
        // 平台上存在历史遗留的 4 段号（早期上传、也标了支持 3.3.1），段数判据会失效——
        // 它和 3.4 的 4 段号逐段比数字时会被认为更大，从而在 3.4 的游戏上错选那个老包。
        // 所以按游戏版本取方向：3.3.1 → 优先段数最多，3.4 → 优先段数最少。
        bool wantMoreSeg = gameVer.Trim().StartsWith("3.3");

        // 三级候选池：explicit=明确标注 / generic=未标注（兜底）
        PickedBest expBest = null, genBest = null;
        foreach (var v in d.versions) {
            if (v == null || string.IsNullOrEmpty(v.versionId)) continue;
            // 资源包（纯四位数字）走 PickBestAssetVersion 那条线，别混进来：
            // 它会以"1 段号"的身份参与上面的段数偏好比较，可能被挑成要装的 mod 版本。
            if (IsAssetVersion(v.versionId)) continue;
            var f = PickCompatibleFile(v, gameVer);          // 这个版本里有没有能用的文件
            if (f == null) { skippedIncompat++; continue; }   // 完全不匹配（标了别的游戏版本）→ 跳过
            var cand = new PickedBest { ver = v, file = f };
            if (f.IsGeneric) {
                // ② 未标注 → 只作为兜底池，且只按版本大小比较（不参与线路偏好）
                if (genBest == null || RemoteIsNewer(v.versionId, genBest.ver.versionId)) genBest = cand;
            } else {
                // ① 明确标注适配当前游戏 → 首选池，按 线路偏好 + 版本大小 比较
                if (expBest == null) { expBest = cand; continue; }
                // 先比"代"：新格式（3.3.1-N）压过老格式（5.2.0.1314.N），顺序不能反——
                // 老格式是 5 段、新格式是 3 段，而 3.3.1 线的段数偏好偏偏喜欢段数多的，
                // 不问代的话会把过渡包挑出来、把真正最新的 3.3.1-N 判掉（玩家就升不到新格式）。
                bool curNew = IsGameDashVersion(expBest.ver.versionId), newIsNew = IsGameDashVersion(v.versionId);
                if (curNew != newIsNew) {
                    if (newIsNew) expBest = cand;
                    continue;
                }
                int curSeg = VersionLine(expBest.ver.versionId), newSeg = VersionLine(v.versionId);
                if (!newIsNew && newSeg != curSeg) {
                    if (wantMoreSeg ? newSeg > curSeg : newSeg < curSeg) expBest = cand;
                } else {
                    // 两层规则，缺一不可：
                    // ① 「版本号里的游戏段」与「文件标注的游戏版本」一致的候选优先。
                    //    这是为跳板包准备的：它版本号写成 3.3.3、文件却标着 3.3.1（上传时版本号写错，
                    //    而文件标注才是玩家能不能用的判据）。这种候选只当"过桥用"——
                    //    它的作用是把带修复的 DLL 送到玩家手上，送到之后就该被正常版本号取代。
                    //    没有这一层，它会和新版本号（序号只差几）反复互相取代，玩家在两个版本号之间荡。
                    // ② 同一层内只比后头那个序号。绝不能整串交给 CompareVersions：
                    //    它按游戏版本逐段比，于是一条游戏段写错的版本（3.3.1 写成 3.3.3）
                    //    就永远压过真正的最新包，玩家每次都被导到同一个包、装完再判"要更新"。
                    bool curOk = VersionGamePartMatchesGameVer(expBest.ver.versionId, gameVer);
                    bool newOk = VersionGamePartMatchesGameVer(v.versionId, gameVer);
                    bool take;
                    if (curOk != newOk) take = newOk;                 // ① 标注与版本号一致的那个优先
                    // ② 同层比序号 —— 只在两边都是新格式（X.Y.Z-N）时成立：
                    //    老格式没有 dash 序号（DashSeqOf 返回 -1），两个老包会被判"序号相同"而选不动，
                    //    于是永远停在先遇到的那一个上。老格式交给 CompareVersions → CompareSameLine。
                    else if (IsGameDashVersion(v.versionId) && IsGameDashVersion(expBest.ver.versionId)
                             && SameGameLine(v.versionId, expBest.ver.versionId))
                        take = DashSeqOf(v.versionId) > DashSeqOf(expBest.ver.versionId);
                    else
                        take = CompareVersions(v.versionId, expBest.ver.versionId) > 0;
                    if (take) expBest = cand;
                }
            }
        }
        var best = expBest ?? genBest;   // ①优先，只有①没有时才用②兜底

        // 统计"比选中的更新、但一个适配文件都没有"的版本数，便于日志解释"为什么没提示更新"
        if (best != null) {
            skippedNewer = 0;
            foreach (var v in d.versions) {
                if (v == null || string.IsNullOrEmpty(v.versionId)) continue;
                if (IsAssetVersion(v.versionId)) continue;      // 资源包不参与 mod 线的统计
                if (!IsGameDashVersion(v.versionId)) continue;
                // 只统计与本条同一条游戏线的版本——别条线的包不能叫"更新的版本"（它根本不适用）
                if (!GameVerMatches(GamePartOf(v.versionId), gameVer)) continue;
                if (RemoteIsNewer(v.versionId, best.ver.versionId) && PickCompatibleFile(v, gameVer) == null) skippedNewer++;
            }
        }
        return best;
    }

    private class PickedBest {
        public ModVersion ver = null;
        public PickedFile file = null;
    }

    /// <summary>在"某一个版本"里挑一个支持当前游戏版本的可下载文件（无适配则返回 null）。
    /// 平台返回的 files[] 里每个文件都带 supportedGames（数组，形如 [{"name":"A Dance of Fire and Ice","version":"3.3.1"}]）。
    /// 规则：① 明确标注匹配当前游戏版本 → 用它；② 完全没标注 → 作为"通用"备用（IsGeneric=true）；③ 标了别的版本 → 跳过。</summary>
    private static PickedFile PickCompatibleFile(ModVersion v, string gameVer) {
        if (v == null || v.files == null || v.files.Count == 0) return null;
        PickedFile generic = null;
        foreach (var f in v.files) {
            if (f == null || string.IsNullOrEmpty(f.id)) continue;
            bool hasInfo = f.supportedGames != null && f.supportedGames.Count > 0;
            // 新格式的版本号自带游戏版本（3.3.1-7）：就算这个包没标 supportedGames，
            // 也能直接按版本号判断它适配哪个游戏，比"未标注兜底"可靠得多（不降级成 generic）。
            if (!hasInfo && IsGameDashVersion(v.versionId)) {
                string gp = GamePartOf(v.versionId);
                if (GameVerMatches(gp, gameVer)) {
                    return new PickedFile { id = f.id, name = f.name, GameVer = gp + "（版本号自带）", IsGeneric = false };
                }
            }
            if (hasInfo && SupportedVerMatches(f, gameVer)) {
                return new PickedFile { id = f.id, name = f.name, GameVer = SupportedVer(f), IsGeneric = false };   // 明确匹配优先
            }
            if (!hasInfo && generic == null) {
                // IsGeneric 让上层能把它降到"最低优先级"，避免把远古的"未标注"包当成适配版本推给玩家
                generic = new PickedFile { id = f.id, name = f.name, GameVer = "(未标注)", IsGeneric = true };
            }
        }
        return generic;
    }

    /// <summary>取文件声明的"支持游戏版本"（supportedGames[].version）。
    /// 平台上 supportedGames 是数组（[{name,version}]）而不是单个对象——写成对象会让 Newtonsoft 解析失败/取不到值，所以这里用 List。
    /// 多个条目时，只要有一个匹配当前游戏版本就算适配。</summary>
    private static string SupportedVer(VersionFile f) {
        try {
            if (f == null || f.supportedGames == null || f.supportedGames.Count == 0) return "";
            var vers = new List<string>();
            foreach (var g in f.supportedGames) {
                if (g == null || string.IsNullOrEmpty(g.version)) continue;
                vers.Add(g.version.Trim());
            }
            return vers.Count > 0 ? string.Join("|", vers.ToArray()) : "";
        } catch { return ""; }
    }

    /// <summary>该文件的 supportedGames 里是否有条目匹配当前游戏版本。
    /// 两级匹配：① 先精确比（作者写全了就按全的算）；② 不中再比 major.minor
    /// （平台上会把 3.4 的包标成 "3.4" 而游戏版本是 "3.4.0"，只精确比较会让 3.4 的用户完全拿不到更新）。
    /// major.minor 不会跨版本误匹配："3.3.1" 与 "3.4.0" 分别是 3.3 / 3.4，永不相等，所以 3.3.1 的包绝不会装到 3.4 上。</summary>
    /// <summary>两个"游戏版本"字符串算不算同一个游戏：精确一致，或 major.minor 一致（"3.3.1" 与 "3.3" / "3.3.0"）——
    /// 跟 supportedGames 那套判据同一个口径。</summary>
    private static bool GameVerMatches(string a, string b) {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        string x = a.Trim(), y = b.Trim();
        if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(MajorMinor(x), MajorMinor(y), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportedVerMatches(VersionFile f, string gameVer) {
        try {
            if (f == null || f.supportedGames == null || string.IsNullOrEmpty(gameVer)) return false;
            string gv = gameVer.Trim();
            foreach (var g in f.supportedGames) {
                if (g == null || string.IsNullOrEmpty(g.version)) continue;
                string sv = g.version.Trim();
                if (string.Equals(sv, gv, StringComparison.OrdinalIgnoreCase)) return true;                          // ① 精确
                if (string.Equals(MajorMinor(sv), MajorMinor(gv), StringComparison.OrdinalIgnoreCase)) return true;   // ② 主次版本
            }
            return false;
        } catch { return false; }
    }

    /// <summary>取 major.minor（"3.4.0" → "3.4"，"3.4" → "3.4"，"3" → "3"）</summary>
    private static string MajorMinor(string v) {
        if (string.IsNullOrEmpty(v)) return "";
        var parts = v.Trim().Split('.');
        if (parts.Length >= 2) return parts[0] + "." + parts[1];
        return parts[0];
    }

    /// <summary>启动时把"要不要更新"那张真值表跑一遍写进日志。
    /// 这条逻辑编译器查不出来：写错了只是"某个玩家的自动更新悄悄不动"，而这里踩的正是这种
    /// （游戏版本那半被比成"相等" 换线换不回来）。真值表能当场看出有没有踩回去。
    /// 全是纯函数（没有网络、没有界面），零副作用，启动一次开销可忽略。</summary>
    public static void RunVersionSelfTest() {
        try {
            var fails = new List<string>();
            int n = 0;
            Action<string, string, string, bool> ck = (remote, local, game, want) => {
                n++;
                bool got = RemoteIsNewer(remote, local, game);
                if (got != want) fails.Add($"{local}+{game}→{remote} 期望{(want ? "要" : "不要")}得到{(got ? "要" : "不要")}");
            };
            // ① 同线：只升不降
            ck("3.3.1-30", "3.3.1-29", "3.3.1", true);
            ck("3.3.1-29", "3.3.1-29", "3.3.1", false);
            ck("3.3.1-9", "3.3.1-30", "3.3.1", false);
            // ② 换游戏版本线：本地不是给当前游戏用的 → 必须换
            ck("3.3.1-29", "3.4-2", "3.3.1", true);
            ck("3.4-2", "3.3.1-30", "3.4.0", true);       // 反向：游戏升到 3.4
            // ③ 反例：序号咬着也要换（老比较器就是在这里栽的：游戏版本那半被比成"相等"）
            ck("3.3.1-29", "3.4-30", "3.3.1", true);
            ck("3.3.1-29", "3.4-29", "3.3.1", true);
            ck("3.3.1-29", "3.4-2", "3.3.1", true);
            // ④ 换线之后不许来回弹（两边都自带游戏版本，major.minor 也对得上）
            ck("3.3.1-29", "3.3.1-29", "3.3.1.0", false); // 游戏读成 3.3.1.0 也算同线
            ck("3.4-2", "3.4-2", "3.4.0", false);
            ck("3.3.1-29", "3.3.1-30", "3.3.1", false);   // 同线内本地更大 → 不许被平台降级
            ck("3.4-2", "3.4-5", "3.4.0", false);
            // ⑤ 老格式（没有游戏版本可判，全交回比大小）
            ck("5.2.0.1314.0059", "5.2.0.1314.0058", "3.3.1", true);
            ck("5.2.0.1314.0058", "5.2.0.1314.0059", "3.3.1", false);
            ck("5.2.0.1314.0059", "5.2.0.1314.0059", "3.3.1", false);
            // ⑥ 新格式压过老格式（装了过渡包的人必须能升到 3.3.1-N）
            ck("3.3.1-1", "5.2.0.1314.0059", "3.3.1", true);
            // ⑦ 同线内序号比数字不比字符串（"3.3.1-10" 比 "3.3.1-9" 大）
            ck("3.3.1-10", "3.3.1-9", "3.3.1", true);
            ck("3.3.1-9", "3.3.1-10", "3.3.1", false);
            // ⑧ 本地版本读不到（"?" / 空）→ 一律当成要更新
            ck("3.3.1-29", "?", "3.3.1", true);
            ck("3.3.1-29", "", "3.3.1", true);

            if (fails.Count == 0) {
                Lg.Info($"[LevelLib] 版本判定自检：{n} 项全过"
                    + "（含「换回旧游戏版本线必须换包」与「跨线不许来回弹」两类关键用例）");
            } else {
                Lg.Info($"[LevelLib] 版本判定自检：{fails.Count}/{n} 项不通过 ✘ —— "
                    + string.Join(" ｜ ", fails.ToArray()));
            }
        } catch (Exception e) {
            try { Lg.Ex("版本判定自检失败", e); } catch { }
        }
    }

    /// <summary>把所有版本的"支持版本"汇总成一行，便于日志排查（多版本共存时看这个）</summary>
    private static string DescribeFiles(ModDetail d) {
        try {
            if (d == null || d.versions == null || d.versions.Count == 0) return "(无版本)";
            var parts = new List<string>();
            int shown = 0;
            foreach (var v in d.versions) {
                if (v == null) continue;
                if (shown++ >= 6) { parts.Add("…"); break; }   // 版本多时只列前 6 个
                var vers = new List<string>();
                if (v.files != null) {
                    foreach (var f in v.files) {
                        if (f == null) continue;
                        string s = SupportedVer(f);
                        vers.Add(string.IsNullOrEmpty(s) ? "(未标注)" : s);
                    }
                }
                parts.Add(v.versionId + "→[" + (vers.Count > 0 ? string.Join(",", vers.ToArray()) : "无文件") + "]");
            }
            return string.Join(" ; ", parts.ToArray());
        } catch { return "(读取失败)"; }
    }

    private static string Trim(string s, int n) {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n) + "…";
    }

    // ---------- 接口返回结构（按 public.json 的 schema 对齐）----------
    // ModLatestVersionResponse 只有 id/versionId/versionType/publishedAt，changelog 与 files 都在 versions[]（ModVersionResponse）里。

    private class ModDetail {
        public string displayName = null;
        public LatestVersion latestVersion = null;
        public List<ModVersion> versions = null;
    }
    private class LatestVersion {
        public string id = null;
        public string versionId = null;
    }
    private class ModVersion {
        public string id = null;
        public string versionId = null;
        public string changelog = null;
        public List<VersionFile> files = null;
    }
    private class VersionFile {
        public string id = null;
        public string name = null;
        /// <summary>该文件支持的游戏，平台返回的是数组：[{"name":"A Dance of Fire and Ice","version":"3.3.1"}]
        /// 用来避免把 3.4 的包装到 3.3.1 上（能加载，但 Harmony 按签名匹配不上 patch，功能静默失效）</summary>
        public List<SupportedGame> supportedGames = null;
    }
    private class SupportedGame {
        public string name = null;
        public string version = null;
    }
    private class DownloadIntent {
        public string url = null;
        public string fileName = null;
    }
}
