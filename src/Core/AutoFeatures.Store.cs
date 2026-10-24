// [split-of] AutoFeatures.cs lines 707-1248 body 14-546

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LevelLibrary;

/// <summary>AutoFeatures.cs 的一部分（同一个 partial 类）：通关/死亡回调、判定与记录写入、自动录入谱面</summary>
public static partial class AutoFeatures {
    /// <summary>这一局是不是"干净通关"（分版本判）。读不到判定桶就放行。
    /// 返回 false 时 why 里是给日志/诊断看的理由。</summary>
    public static bool CleanClearRecords(int floors, out string why) {
        return CleanClearRecordsFor(ReadMarginBuckets(), floors, out why);
    }

    /// <summary>纯判据（不读游戏，读游戏那步在 CleanClearRecords 里）</summary>
    public static bool CleanClearRecordsFor(int[] b, int floors, out string why) {
        why = "";
        if (b == null || b.Length < 7 || floors <= 0) { why = "读不到判定桶或格子数，放行"; return true; }
        bool is34 = b.Length >= 16;
        int okTile = 0;                     // 允许的"格子判定"之和
        if (is34) {
            for (int i = 0; i <= 8; i++) okTile += b[i];        // 提前/稍快/-完美/x完美/完美+/稍慢/太慢
            if (b.Length > 12) okTile += b[12];                 // 自动格
            // 不许有：9 太快(Multipress) / 10 错过(FailMiss) / 11 过载 / 13 按太快(OverPress)
            // 9 存疑：点名允许的表里有「太快」，而记录行里那一列装的其实是 VeryEarly(桶1) 暂按"没点名 = 不许"处理
            var bad = new System.Collections.Generic.List<string>();
            for (int i = 9; i < b.Length; i++) {
                if (i == 12) continue;                          // 自动格是好的
                if (b[i] > 0) bad.Add(MarginName(i) + "=" + b[i]);
            }
            if (bad.Count > 0) { why = "有不允许的判定：" + string.Join(" ", bad.ToArray()); return false; }
        } else {
            for (int i = 0; i <= 6; i++) okTile += b[i];        // 提前/稍快/完美/稍慢/太慢
            if (b.Length > 10) okTile += b[10];                 // 自动格
            // 不许有：7 太快(Multipress) / 8 错过(FailMiss) / 9 过载 / 11 按太快(OverPress)
            var bad = new System.Collections.Generic.List<string>();
            for (int i = 7; i < b.Length; i++) {
                if (i == 10) continue;                          // 自动格
                if (b[i] > 0) bad.Add(MarginName(i) + "=" + b[i]);
            }
            if (bad.Count > 0) { why = "有不允许的判定：" + string.Join(" ", bad.ToArray()); return false; }
        }
        // 方向：只拦"少"。多出来的 = 空敲/重复按，照记
        if (okTile < floors - 1) {
            why = "判定和=" + okTile + " < 格子数-1=" + (floors - 1) + "（有格子没产生判定 = 漏格）";
            return false;
        }
        int extra = okTile - (floors - 1);
        why = "判定和=" + okTile + " ≥ 格子数-1=" + (floors - 1)
            + (extra > 0 ? "（多出 " + extra + " 个空敲/重复按 照记）" : " 刚好");
        return true;
    }

    /// <summary>读一次判定桶（纯读、不改状态）。读不到返回 null</summary>
    public static int[] ReadMarginBuckets() {
        try { return ADOBase.controller?.playerOne?.marginTracker?.hitMarginsCount; } catch { return null; }
    }

    /// <summary>当前格子数（官方关卡那条线用；读不到返回 0）</summary>
    public static int CurrentFloorCount() {
        try { var lmf = scrLevelMaker.instance; if (lmf != null && lmf.listFloors != null) return lmf.listFloors.Count; } catch { }
        return 0;
    }

    /// <summary>本局是否开过自动播放（官方关卡那条线要问它，别各自记一份）</summary>
    public static bool AutoplayUsedNow() {
        try { return _autoplayUsed || RDC.auto; } catch { return _autoplayUsed; }
    }

    /// <summary>把最近这一局的判定原样写进 judgments_dump.txt，供人工看"出了哪些判定、各多少次"。
    /// 这个文件不进发布包（已在打包排除名单里）。</summary>
    private static void DumpJudgments(LevelEntry entry) {
        try {
            var b = ReadMarginBuckets();
            string dump = Path.Combine(Main.ModEntry.Path, "judgments_dump.txt");
            var sb = new System.Text.StringBuilder();
            sb.Append("==== ").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ")
              .Append(entry != null ? entry.Title : "(未知谱面)").Append(" ====\n");
            sb.Append("谱面文件夹：").Append(entry != null ? entry.FolderPath : "?").Append('\n');
            sb.Append("格子数=").Append(CurrentFloorCount())
              .Append("  判定模式=").Append(GCS.difficulty.ToString())
              .Append("  自动播放=").Append(_lastAutoplay ? "开" : "关")
              .Append("  不会失败=").Append(_lastNoFail ? "开" : "关")
              .Append("  检查点=").Append(_lastCheckpoints)
              .Append("  死亡(控制器/追踪器)=").Append(_lastDeathsCtrl).Append('/').Append(_lastDeathsMt).Append('\n');
            if (b == null) { sb.Append("（读不到判定桶）\n\n"); }
            else {
                sb.Append("判定桶（下标=名称=次数）：\n");
                for (int i = 0; i < b.Length; i++) {
                    sb.Append("  [").Append(i.ToString().PadLeft(2)).Append("] ").Append(MarginName(i).PadRight(22))
                      .Append(" = ").Append(b[i]).Append('\n');
                }
                int sum = 0; for (int i = 0; i < b.Length; i++) sum += b[i];
                sb.Append("合计 = ").Append(sum).Append('\n');
                sb.Append("（版本线判定：").Append(IsGame34() ? "3.4（判定表 16 项）" : "3.3.1（判定表 12 项）").Append("）\n");
            }
            sb.Append('\n');
            File.AppendAllText(dump, sb.ToString());
            Lg.Info("已把这一局的判定写进 judgments_dump.txt（" 
                + (entry != null ? entry.Title : "?") + "）");
        } catch (System.Exception e) {
            try { Lg.Ex("写判定诊断文件失败", e); } catch { }
        }
    }

    /// <summary>通关回调（带已知 winTime，避免同一帧重复调用 GetWinTime）</summary>
    public static void OnBeatLevel(float win) {
        try {
            // winTime 去重：BeatLevel patch 和轮询都会触发，同一通关只处理一次
            if (win != 0f && win == _lastWinTime) return;
            _lastWinTime = win;
            // 肥鱼那句必须放在 winTime 去重之后、任何准入判据之前：
            // 自动播放 / 不会失败 / 用了检查点 / 判定表没过的局照样说，官方关卡通关也会说
            PortalUI.PetSayOnClear();
            // 不会失败模式 / 自动播放的通关一律不算通关记录（判据在 validClear 里，不再是开关相关的 NotRealRun）

            string path = GetCurrentLevelPath();
            if (LogOnBeatLevel) Lg.Info($"[LevelLib] OnBeatLevel: path={path} win={win} isLevelEditor={ADOBase.isLevelEditor}");
            // 官方关卡（没有 .adofai 文件路径）：我们自己记一条通关记录
            try { OfficialLevels.OnCleared(); } catch { }
            if (string.IsNullOrEmpty(path)) { Lg.Info("OnBeatLevel: 无有效路径，直接返回"); return; }
            // 按文件夹匹配（打开 main 还是其他 .adofai 都算同一个谱面）
            string folder = Path.GetDirectoryName(path);
            var entry = LevelScanner.Entries.FirstOrDefault(e => string.Equals(e.FolderPath, folder, System.StringComparison.OrdinalIgnoreCase));
            Lg.Info($"[LevelLib] OnBeatLevel: folder={folder} 匹配entry={(entry == null ? "(空)" : entry.Title)}");
            if (entry == null && !string.IsNullOrEmpty(folder)) {
                // 按 ogg 名再找一次（双击打开时路径是原位置，列表条目是 backup 副本）
                string oggBase = LevelScanner.SuggestTitle(folder);
                entry = LevelScanner.Entries.FirstOrDefault(e =>
                    string.Equals(e.OggName, oggBase, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Title, oggBase, System.StringComparison.OrdinalIgnoreCase));
            }
            if (entry == null && !string.IsNullOrEmpty(folder)) {
                // 按文件夹名匹配（backup\5-ex 与 D:\task\5-ex 尾段都是 5-ex）
                string dirName = Path.GetFileName(folder.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(dirName)) {
                    entry = LevelScanner.Entries.FirstOrDefault(e =>
                        string.Equals(Path.GetFileName(e.FolderPath.TrimEnd('\\', '/')), dirName, System.StringComparison.OrdinalIgnoreCase));
                }
                Lg.Info($"[LevelLib] OnBeatLevel: 文件夹名匹配 dirName={dirName} 结果={(entry == null ? "(空)" : entry.Title)}");
            }
            if (entry == null) {
                // 不在列表：非 mod 谱面；自动录入开着才录入（录入后也可标记）
                if (AutoRecord) {
                    entry = AutoRecordLevel(path);
                    if (entry == null) return;
                } else {
                    return;
                }
            }

            // 检查点数（只读、只记，不参与判断）
            int cpsCtrl = 0, cpsGame = 0;
            try { cpsCtrl = scrController.checkpointsUsed; } catch { }
            try { var sgv = UnityEngine.Object.FindFirstObjectByType<scnGame>(); if (sgv != null) cpsGame = sgv.checkpointsUsed; } catch { }
            // 通关处理只做两件事：① 把这一局的判定原样写进 judgments_dump.txt 给人看 ② 照常记录（准入判据另算）
            // 唯一保留的一条准入是自动播放：早年明确要求"自动播放不记录"，从没改过
            _lastFloors = CurrentFloorCount();
            BindBucketIndexes();          // 先按下标表（3.3.1 / 3.4 两套，别用写死的数字）
            _lastBuckets = ReadMarginBuckets();
            _lastAutoHits = 0;
            if (_lastBuckets != null && _lastBuckets.Length > AutoIdx) _lastAutoHits = _lastBuckets[AutoIdx];
            _lastCheckpoints = cpsCtrl;
            try { _lastDeathsCtrl = Mathf.Max(0, scrController.deaths); } catch { _lastDeathsCtrl = -1; }
            try {
                var mtD = ADOBase.controller?.playerOne?.marginTracker;
                _lastDeathsMt = mtD != null ? mtD.GetDeaths() : -1;
            } catch { _lastDeathsMt = -1; }
            _lastAutoplay = _autoplayUsed;
            _lastNoFail = _noFailUsed;
            DumpJudgments(entry);      // 给人看的那份

            bool validClear = !_autoplayUsed;   // 自动播放一律不记录
            // 判定表按版本判：出现"不许的判定"就不记录，且允许的判定之和必须恰好 = 格子数-1（漏格自然拦掉）
            string cleanWhy = "开了自动播放 不记录";
            if (validClear && !CleanClearRecords(_lastFloors, out cleanWhy)) {
                validClear = false;
                Lg.Info($"[LevelLib] 不记录（{cleanWhy}）{entry.Title}");
            } else if (validClear) {
                Lg.Info($"[LevelLib] 通关判定通过（{cleanWhy}）{entry.Title}");
            }
            int tileJudge = -1, nonTileJudge = 0;
            if (_lastBuckets != null && _lastBuckets.Length >= 7) {
                int t = 0;
                if (_idx34) { for (int i = 0; i <= 8; i++) t += _lastBuckets[i]; t += _lastAutoHits; }   // 3.4：0..8 都是格子判定
                else { for (int i = 0; i <= 6; i++) t += _lastBuckets[i]; t += _lastAutoHits; }         // 3.3.1：0..6
                int nt = 0;
                for (int i = 0; i < _lastBuckets.Length; i++) {
                    if (i == _iAuto) continue;                     // 自动格不算"非格"
                    if (_idx34) { if (i >= 9) nt += _lastBuckets[i]; }   // 3.4 的非格从 9 起（太快/错过/过载/按太快…）
                    else { if (i >= 7) nt += _lastBuckets[i]; }          // 3.3.1 从 7 起
                }
                tileJudge = t; nonTileJudge = nt;
            }
            Lg.Info($"[LevelLib] 通关读数（只读不算）：格子={_lastFloors} 判定和={tileJudge}"
                + $"（自动格{_lastAutoHits}）非格判定={nonTileJudge} 检查点={cpsCtrl}/{cpsGame}"
                + $" 死亡={_lastDeathsCtrl}/{_lastDeathsMt} 自动播放={_lastAutoplay} 不会失败={_lastNoFail}"
                + $" → 判定明细见 judgments_dump.txt");

            // 完成度：自动播放的局不给，其余一律给
            if (NotRealRun()) {
                Lg.Info($"[LevelLib] 自动播放的局：不改完成度 ({entry.Title})");
            } else {
                try { LevelScanner.SetCompletion100(entry.FolderPath); } catch { }
            }

            // 记录 X 精准度（按判定难度分槽：0宽判 1标判 2严判，各自只升不降）+ 通关记录
            // 不记录的理由只剩两条：自动播放 + 判定表没过（漏格 / 出现点名不许的判定）；
            // "不会失败 / 检查点 / 死亡次数"都不再参与判断
            try {
                var mm = ADOBase.controller != null ? ADOBase.controller.mistakesManager : null;
                float xacc = mm != null ? mm.percentXAcc * 100f : 0f;
                Lg.Info($"[LevelLib] 记录段: mm={(mm==null?"(空)":"有")} xacc={xacc:F2} autoplayUsed={_autoplayUsed} noFailUsed={_noFailUsed} validClear={validClear} RecordRestrictions={RecordRestrictions} AutoRecord={AutoRecord}");
                if (mm != null && mm.percentXAcc > 0f) {
                    if (!NotRealRun() && validClear) {
                        int slot = GCS.difficulty == Difficulty.Lenient ? 0 : GCS.difficulty == Difficulty.Strict ? 2 : 1;
                        // 检查点这一列现在恒为 0（"用了检查点就不记"那条判据已经拆掉，列还留着兼容老数据）
                        // 副作用：AddClearRecord 里"检查点=0 就触发今日成就"那条现在每次通关都会走（一天只发一次）
                        int checkpoints = 0;
                        // 纯判定：只有完美(Perfect)和稍快/稍慢(EarlyPerfect/LatePerfect)，无太早/太晚/太快/错过
                        bool pure = true;
                        bool fullPerfect = true;   // 全完美：只有 Perfect，连稍快稍慢都没有（无瑕标识）
                        try {
                            var tracker = ADOBase.controller?.playerOne?.marginTracker;
                            if (tracker != null && tracker.hitMarginsCount != null && tracker.hitMarginsCount.Length >= 9) {
                                var tb = tracker.hitMarginsCount;
                                // 下标一律走 BindBucketIndexes，别再写死数字：3.4 的枚举插进了
                                // PerfectMinus/XPerfect/PerfectPlus，下标整体后移，写死就会把完美记到稍慢
                                // 不算"纯"：有 提前(0/1) / 太慢(5+6 或 7+8) / 太快(Multipress) / 错过(FailMiss)
                                if (Bk(tb, 0) > 0 || Bk(tb, _iEarly1) > 0
                                    || Bk(tb, _iTooSlow1) > 0 || Bk(tb, _iTooSlow2) > 0
                                    || Bk(tb, _iTooFast) > 0 || Bk(tb, _iTooFast + 1) > 0) {
                                    pure = false;
                                    fullPerfect = false;
                                }
                                // 全完美：稍快 / 稍慢 有任何都算有瑕（3.4 的 -完美/x完美/完美+ 仍是完美）
                                if (Bk(tb, _iFast) > 0 || Bk(tb, _iSlow) > 0) {
                                    fullPerfect = false;
                                }
                                if (PerfSum(tb) <= 0) fullPerfect = false;   // 至少要有"完美"（3.4 是三个桶之和）
                            } else {
                                fullPerfect = false;
                            }
                        } catch { }
                        // X-acc：检查点也算，照常记录
                        LevelScanner.UpdateXacc(entry.FolderPath, slot, mm.percentXAcc * 100f, pure, fullPerfect);
                        // 通关记录（判定统计，一条一条无限加）：提前/太快/稍快/完美/稍慢/太慢；检查点也记录
                        try {
                            var tracker2 = ADOBase.controller?.playerOne?.marginTracker;
                            Lg.Info($"[LevelLib] 通关记录段: tracker2={(tracker2==null?"(空)":"有")} hitCount={(tracker2?.hitMarginsCount?.Length ?? -1)} entry.FolderPath={entry.FolderPath} checkpoints={checkpoints}");
                            if (tracker2 != null && tracker2.hitMarginsCount != null && tracker2.hitMarginsCount.Length >= 9) {
                                var rb = tracker2.hitMarginsCount;
                                // 这里的 6 个下标全部走分版本的 BindBucketIndexes，别写死数字
                                // （记错的后果就是"打的完美被记到稍慢去"：3.4 的枚举插进
                                // PerfectMinus/XPerfect/PerfectPlus 下标整体后移）
                                // 参数顺序照 AddClearRecord 的签名：
                                // folderPath, xacc, early, tooFast, fast, perfect, slow, tooSlow, slot, checkpoints,
                                // autoplay, noFail, pitch, auto
                                LevelScanner.AddClearRecord(
                                    entry.FolderPath,                          // folderPath
                                    mm.percentXAcc * 100f,                     // xacc
                                    Bk(rb, 0),                                 // early = 提前(TooEarly)
                                    Bk(rb, _iEarly1),                          // tooFast = 很快(VeryEarly)
                                    Bk(rb, _iFast),                            // fast = 稍快(EarlyPerfect)
                                    PerfSum(rb),                               // perfect = 完美(3.4 三个桶之和)
                                    Bk(rb, _iSlow),                            // slow = 稍慢(LatePerfect)
                                    Bk(rb, _iTooSlow1) + Bk(rb, _iTooSlow2),   // tooSlow = 太慢(VeryLate+TooLate)
                                    slot,                                      // slot
                                    checkpoints,                               // checkpoints
                                    _autoplayUsed,                             // autoplay
                                    _noFailUsed,                               // noFail
                                    ReadClearPitch(),                          // pitch
                                    _lastAutoHits);                            // auto（自动格）
                                // 记录文件那 6 列的含义是老约定，别擅自改（免得新老数据对不上）：
                                // 提前 = TooEarly；"太快"那一列实际装的是 VeryEarly(很早)；
                                // 太慢 = VeryLate+TooLate；"太快"(Multipress) 与"错过"(FailMiss) 在记录文件里没有列
                                Lg.Info($"[LevelLib] 通关记录已写入（{(_idx34 ? "3.4" : "3.3.1")} 下标表 太快(Multipress)={Bk(rb, _iTooFast)} 未入库）: {entry.FolderPath}"
                                    + $" xacc={mm.percentXAcc*100f:F2} slot={slot} 音高={ReadClearPitch()}（{(ReadClearPitch() / 100f):F1}x）"
                                    + $" 自动格={_lastAutoHits} 判定和={tileJudge} 格子={_lastFloors}");
                                // 每日成就：今日打谱大学习后从第一格开始通关才触发，判据与通关记录同一套（validClear）
                                if (validClear) { try { Achievements.TriggerFinish(); } catch { } }
                                // 成就「恭喜你：PP-1」：除完美外只有一个稍快或稍慢就触发（可重复，次数决定等级）
                                try {
                                    int earlyP = Bk(rb, _iFast);   // 稍快
                                    int lateP  = Bk(rb, _iSlow);   // 稍慢
                                    if (earlyP + lateP == 1) {
                                        Achievements.TriggerRepeat(Achievements.AchPP1);
                                    }
                                } catch (System.Exception pe) { Lg.Ex("PP-1 成就触发失败", pe); }
                            }
                            // 成就「兴酣落笔摇五岳」：一个谱面游玩次数为 1 时通关，且这一局没死过。
                            // 用 NoteEntryPlays 那张快照判，别改成 LevelScanner.GetPlays(...) == 1：
                            // 计数在本局已经涨过（打开 / 编辑器就绪 / 通关），那样永远判不出来。
                            try {
                                if (_freshEntry && _freshDeaths == 0
                                    && string.Equals(_freshFolder, entry.FolderPath, System.StringComparison.OrdinalIgnoreCase)) {
                                    Achievements.TriggerRepeat(Achievements.AchXinghan);
                                    _freshFolder = null;    // 一个谱面只认一次（同一局再通也不重复 +1）
                                    _freshEntry = false;
                                    Lg.Info($"[LevelLib] 第一次玩就通关 → 成就「兴酣落笔摇五岳」+1（{entry.Title}）");
                                }
                            } catch (System.Exception exr) { Lg.Ex("「兴酣落笔摇五岳」触发失败", exr); }
                            // 通关算 1 次游玩
                            LevelScanner.RecordPlay(entry.FolderPath);
                        } catch (System.Exception ce) { Lg.Ex("写通关记录失败", ce); }
                    } else {
                        // 不记录（但这一局仍然算一次游玩）；日志打真正的理由 cleanWhy，
                        // 别写死"开了自动播放"——被判据拦下来时那样会同自相矛盾
                        Lg.Info($"[LevelLib] 不记录 X 精准度/通关记录（{cleanWhy}）格子={_lastFloors} 判定和={tileJudge}）{entry.Title}");
                        try { LevelScanner.RecordPlay(entry.FolderPath); } catch { }
                    }
                }
            } catch (System.Exception xe) {
                Lg.Ex("记录X精准度失败", xe);
            }

            // 自动标记（开关开启时）：按玩家选择的判定模式打判定标记；只升不降（低级不覆盖高级）
            if (AutoMark && !NotRealRun() && validClear) {
                string judge;
                switch (GCS.difficulty) {
                    case Difficulty.Lenient: judge = "宽判"; break;
                    case Difficulty.Strict: judge = "严判"; break;
                    default: judge = "标判"; break;
                }
                string existing = MarkStore.GetJudge(entry.FilePath);
                if (string.IsNullOrEmpty(existing) || JudgeRank(judge) >= JudgeRank(existing)) {
                    MarkStore.SetJudge(entry.FilePath, judge);
                    Lg.Info($"[LevelLib] 自动标记: {entry.Title} -> {judge} (难度={GCS.difficulty})");
                } else {
                    Lg.Info($"[LevelLib] 自动标记跳过(低级不覆盖高级): {entry.Title} 已有{existing} 新{judge}");
                }
            } else if (NotRealRun()) {
                Lg.Info($"[LevelLib] 自动播放的局：不标记判定 ({entry.Title})");
            }

            // 通关后：主标记为空就弹标记界面让玩家选择（自动标记打的判定标记不算"已标过"）
            // 非有效通关不弹（用了检查点还弹"通关了来标记吧"很奇怪）
            if (validClear && string.IsNullOrEmpty(MarkStore.Get(entry.FilePath))) {
                Lg.Info($"[LevelLib] 通关无主标记，弹标记界面: {entry.Title}");
                PortalUI.ShowMarkDialog(entry);
            }
        } catch (Exception e) {
            Lg.Ex("自动标记失败", e);
        }
        _autoplayUsed = false;   // 本局处理完，重置（下一局重新检测）
        _noFailUsed = false;     // 不会失败标记同理重置
    }

    /// <summary>游玩死亡回调（scrController.OnPlayerDied postfix）：列表里的谱面每次死亡游玩次数 +1 + 重置自动播放标记</summary>
    public static void OnPlayerDied() {
        try { OfficialLevels.OnPlayerDied(); } catch { }   // 官方关卡：寄录图（我们自己统计）
        _autoplayUsed = false;   // 死亡重开 = 新的一局：重置自动播放标记
        _noFailUsed = false;     // 同理重置不会失败标记
        try {
            var ctrl = scrController.instance;
            if (ctrl == null || ctrl.noFail) return;   // No Fail 模式不死亡不计
            string path = GetCurrentLevelPath();
            if (string.IsNullOrEmpty(path)) return;
            string folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder)) return;
            // 只统计列表里的谱面（按文件夹匹配）
            bool inList = LevelScanner.Entries.Any(e => string.Equals(e.FolderPath, folder, System.StringComparison.OrdinalIgnoreCase));
            if (!inList) return;
            LevelScanner.RecordPlay(folder);   // 立即写盘（带时间戳）
            // 成就「兴酣落笔摇五岳」：这一局死过就不再是"第一次玩就通关"（死一次计数就涨上去了）
            if (string.Equals(_freshFolder, folder, System.StringComparison.OrdinalIgnoreCase)) _freshDeaths++;
            // 死点热力图：只统计主关卡（sub1/sub2 等教程关不计入）
            try {
                string fileName = Path.GetFileName(path);
                bool isTutorial = fileName.StartsWith("sub", System.StringComparison.OrdinalIgnoreCase);
                if (!isTutorial) {
                    // 关卡数据源与下面 99% 成就那段保持同一口径：先取 ADOBase.lm，
                    // 取不到再退回 scrLevelMaker.instance（两者是同一实例）。
                    // 原来这里只读 scrLevelMaker.instance，于是取不到就整段静默跳过，
                    // 外层还包着空的catch，连日志都没有，玩家看到的是「这个谱没寄录图」，
                    // 实际是数据源取不到。全库实测 0 个谱卡在 listFloors.Count > 1 上，
                    // 所以漏记的原因就是这条：某些谱（尤其官方谱 / 自制小谱）走的是另一个源。
                    var lmf = ADOBase.lm != null ? ADOBase.lm : scrLevelMaker.instance;
                    // 死亡瞬间 currFloor 可能已经被清掉，那时整段会静默跳过（又漏记一谱）。
                    // 所以优先用 ctrl.currentSeqID —— 它是 controller 自己的字段，
                    // 不依赖地板对象还在不在（下面 99% 那段也是这么算的，注释里已记过
                    // "currFloor.seqID 与 currentSeqID 未必同源"）。
                    // ctrl 本身为 null 时不能碰 currentSeqID，所以先判 ctrl。
                    int tile = -1;
                    if (ctrl != null) {
                        try { tile = ctrl.currentSeqID; } catch { }
                        if (tile < 0 && ctrl.currFloor != null) {
                            try { tile = ctrl.currFloor.seqID; } catch { }
                        }
                    }
                    if (ctrl != null && tile >= 0 && lmf != null && lmf.listFloors != null && lmf.listFloors.Count > 1) {
                        LevelScanner.RecordDeath(folder, tile, lmf.listFloors.Count);
                    } else {
                        Lg.Info($"[LevelLib] 寄录图跳过：{folder}（lm={lmf != null} tile={tile}）");
                    }
                }
            } catch (System.Exception e) {
                Lg.Ex("记寄录图失败", e);
            }
            Lg.Info($"[LevelLib] 死亡+1: {folder}");
            // 可重复成就「啊嘞，怎么死在这了」：死在完成度 99% 的地方就 +1 次。
            // 完成度用游戏自己的算法（与 scrController.percentComplete 一致）：
            // pct = (currentSeqID + 1) / listFloors.Count，再四舍五入到整数百分比
            // 判据是 pct >= 99 而不是恰好 99：最后一格算出来是 100%（1198/1204 = 99.5% 就进位了），
            // 而"死在最后几格"正是玩家口里的"死在 99"，判"恰好 99"这种最典型的死法永远命中不了。
            // 同一条判据也管着肥鱼那句与成就提示。
            try {
                // 用游戏自己的数据源（ADOBase.controller.currentSeqID / ADOBase.lm.listFloors.Count），
                // lm 取不到时退回 scrLevelMaker.instance，两者是同一个实例
                var lm99 = ADOBase.lm != null ? ADOBase.lm : scrLevelMaker.instance;
                if (ctrl != null && lm99 != null && lm99.listFloors != null && lm99.listFloors.Count > 1) {
                    int tile = ctrl.currentSeqID + 1;
                    int total = lm99.listFloors.Count;
                    int pct = Mathf.RoundToInt((float)tile / total * 100f);
                    // 日志里顺带报"数到第几个地板对象"：currFloor.seqID 与 currentSeqID 未必同源，两个数对不上时好分辨
                    int floorSeq = -1;
                    try { if (ctrl.currFloor != null) floorSeq = ctrl.currFloor.seqID; } catch { }
                    Lg.Info($"[LevelLib] 死亡位置：完成度 {pct}%（第 {tile} / {total} 格"
                        + (floorSeq >= 0 && floorSeq != ctrl.currentSeqID
                           ? $"｜ currFloor.seqID={floorSeq} 与 currentSeqID={ctrl.currentSeqID} 不一致" : "")
                        + "）");
                    if (pct >= 99) {
                        // 这道闸要求"从第一格开始"：scrController.checkpointsUsed 只表示这一局跑的过程中
                        // 用掉了几次检查点，从最后一格开局且一次没死时它也是 0 必须看开局采样。
                        // 从 98% 打到 99% 也会弹的根因：开局没采到（RunStartSeqLatch <= 0）时
                        //   旧判法 `!notFirstTile` 恒真，只要没用检查点就触发，起点判定整个落空。
                        //   现在要求必须采到、且采到的起点就是第 0 格，这一局也没用过检查点才算；
                        //   没采到宁可放过这一局（下一局重新进谱面时会重采），绝不把"从半路开始"记成"从第一格"。
                        int cpsNow = CheckpointsUsedNow();
                        bool sniffed = RunStartSeqLatch > 0f;
                        bool notFirstTile = sniffed && RunStartSeq > 0;
                        if (sniffed && RunStartSeq <= 0 && cpsNow <= 0) {
                            Lg.Info($"[LevelLib] 完成度 {pct}% 且从第一格开始 → 触发可重复成就「啊嘞，怎么死在这了」"
                                + $"（开局采样={(sniffed ? "有" : "无")} 起始格={RunStartSeq + 1} 本局检查点={cpsNow}）");
                            Achievements.TriggerRepeat(Achievements.AchDie99);
                            // 肥鱼那句就挂在这一处 99% 判据里（百分比算法全项目只此一份，另起一套算法会让
                            // 两处漂开、出现"成就弹了、鱼不说话"），且与成就共用同一个条件：两件事不许一个有一个没有
                            try { PortalUI.PetSayOnDie99(); } catch { }
                        } else {
                            string why = (notFirstTile ? $"这一局是从第 {RunStartSeq + 1} 格开始的" : "")
                                + (notFirstTile && cpsNow > 0 ? "、" : "")
                                + (cpsNow > 0 ? $"这一局用了检查点 {cpsNow} 次" : "");
                            if (!sniffed) why += "（这一局没采到起始格 ->  只按检查点判）";
                            Lg.Info($"[LevelLib] 完成度 {pct}% 但不算「从第一格开始」："
                                + why + " ->  成就不触发、肥鱼也不说");
                        }
                    }
                }
            } catch (System.Exception e99) { Lg.Ex("99% 死亡成就触发失败", e99); }
        } catch (Exception e) {
            Lg.Ex("死亡计数失败", e);
        }
    }

    /// <summary>这一局跑的过程中用掉了几次检查点。
    /// 判断"是不是从第一格开始"不能只用它：从最后一格开局、一次没死时它也是 0，
    /// 那条判据要看开局采样 RunStartSeq + RunStartSeqLatch，这个只当补充。
    /// 也不要裸用 scrController.startedFromCheckpoint：它是粘性的（关卡加载时按 seqID>0
    /// 或编辑器里遗留的 GCS.checkpointNum 赋值），从头打一局也可能为 true，要它就得在开局那一刻锁存。
    /// scnGame 那个实例缓存一下（死亡可能很频繁），换场景后 Unity 会让它变成 fake-null，于是自动重新找。</summary>
    private static scnGame _scnGameForCps;
    private static int CheckpointsUsedNow() {
        int c = 0;
        try { var cc = GetController(); if (cc != null) c = scrController.checkpointsUsed; } catch { }
        if (c > 0) return c;
        try {
            if (_scnGameForCps == null) _scnGameForCps = UnityEngine.Object.FindFirstObjectByType<scnGame>();
            if (_scnGameForCps != null) c = _scnGameForCps.checkpointsUsed;
        } catch { _scnGameForCps = null; }
        return c;
    }

    /// <summary>当前谱面路径（编辑器打开的 .adofai 文件路径；非文件路径返回 null）</summary>
    private static string GetCurrentLevelPath() {
        try {
            string p = ADOBase.levelPath;
            if (string.IsNullOrEmpty(p)) return null;
            // 只处理文件路径（编辑器打开的谱面；内置谱面不是 .adofai 文件路径）
            if (!p.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetFullPath(p);
        } catch {
            return null;
        }
    }

    /// <summary>把原路径解析成列表条目对应的 FolderPath（backup 副本），匹配不到就返回原目录</summary>
    private static string ResolveListFolder(string filePath) {
        try {
            if (string.IsNullOrEmpty(filePath)) return null;
            string folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder)) return null;
            // 1. 精确 folder 匹配
            var e = LevelScanner.Entries.FirstOrDefault(x => string.Equals(x.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
            if (e != null) return e.FolderPath;
            // 2. ogg 名匹配
            string oggBase = LevelScanner.SuggestTitle(folder);
            e = LevelScanner.Entries.FirstOrDefault(x =>
                string.Equals(x.OggName, oggBase, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Title, oggBase, StringComparison.OrdinalIgnoreCase));
            if (e != null) return e.FolderPath;
            // 3. 文件夹名匹配（backup\5-ex 与 D:\task\5-ex 尾段一致）
            string dirName = Path.GetFileName(folder.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(dirName)) {
                e = LevelScanner.Entries.FirstOrDefault(x =>
                    string.Equals(Path.GetFileName(x.FolderPath.TrimEnd('\\', '/')), dirName, StringComparison.OrdinalIgnoreCase));
                if (e != null) return e.FolderPath;
            }
            return folder;
        } catch { return null; }
    }

    /// <summary>录入谱面到列表（复制进 backup，指向副本）</summary>
    private static LevelEntry AutoRecordLevel(string path) {
        try {
            string folder = Path.GetDirectoryName(path);
            string dirName = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(dirName)) dirName = Path.GetFileNameWithoutExtension(path);
            // 已在列表（按原文件夹路径/ogg 名）就不录
            // oggName 只算一次：SuggestTitle 底下是 Directory.GetFiles，把它写在 Any 的谓词里
            // 会每个条目求值一次（谱面越多卡得越狠）。谓词与 e 无关的那半边先算出来。
            string suggestName = LevelScanner.SuggestTitle(folder);
            if (LevelScanner.Entries.Any(e => string.Equals(e.FolderPath, folder, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.OggName, suggestName, System.StringComparison.OrdinalIgnoreCase))) {
                Lg.Info($"[LevelLib] 自动录入跳过（已在列表）: {dirName}");
                return null;
            }
            // 不再弹确认框、也不需要确认：直接录入，成功后走"临时成就"那条路跳一个提示
            return CommitAutoRecord(path, folder, dirName);
        } catch (Exception e) {
            Lg.Ex("自动录入失败", e);
            return null;
        }
    }

    /// <summary>真正录入（无确认直接调；确认弹窗点"确定"时调）：复制进 backup + 写盘 + 加列表；关=直接指向原位置。
    /// 备份看的是 `AutoImportBackup`（自动导入自己那条开关），不是 `BackupOnImport`（手动导入那条）——
    /// 两者是两件事，别把手动导入的开关接到这条路上。</summary>
    public static LevelEntry CommitAutoRecord(string path, string folder, string dirName) {
        try {
            string backupDir;
            if (AutoImportBackup) {
                // 复制进 backup，列表指向副本
                backupDir = LevelScanner.EnsureBackup(folder, dirName);
                if (backupDir == null) return null;
            } else {
                backupDir = folder;   // 不备份：直接用原位置加入列表
            }
            // 谱面重复检查：同一个文件夹已在列表就提示，不重复录入
            if (LevelScanner.Entries.Any(e => string.Equals(e.FolderPath, backupDir, System.StringComparison.OrdinalIgnoreCase))) {
                Lg.Info($"[LevelLib] 自动录入跳过（重复）: {backupDir}");
                return null;
            }
            LevelScanner.AddRecorded(backupDir);   // 立即写盘（强杀进程也不丢）
            string title = LevelScanner.SuggestTitle(backupDir);
            string newPath = Path.Combine(backupDir, Path.GetFileName(path));
            var entry = new LevelEntry { Title = title, OggName = title, FolderPath = backupDir, FilePath = newPath };
            LevelScanner.Entries.Add(entry);
            LevelScanner.SortEntries();
            Lg.Info($"[LevelLib] 自动录入: {title}");
            PortalUI.OnLibraryChanged();
            // 走"临时成就"那条路跳一个提示（复用成就提示的窗口样式+音效，但不是真成就：
            // 不写 achievements.txt、不进成就面板、不解锁任何东西）
            PortalUI.ShowTempAchHint("已录入谱面：" + title);
            return entry;
        } catch (Exception e) {
            Lg.Ex("自动录入失败", e);
            return null;
        }
    }
}
