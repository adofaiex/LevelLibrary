// [split-of] Achievements.cs lines 701-1206 body 13-478

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace LevelLibrary;

/// <summary>Achievements.cs 的一部分（拆分自 Achievements.cs，同一个 partial 类）</summary>
public static partial class Achievements {
    // ---------- 一次性迁移：无瑕成就追溯重算 ----------
    // 旧无瑕只看判定分布，"从最后一格起玩、只打一格"也能拿到；旧记录又没有检查点字段（全为 0），
    // 无法从历史里分辨真假无瑕。改法：启动时先取消全部无瑕，再用通关记录重建
    // （有严判通关记录 且 判定全完美 且 Checkpoints==0 且没开自动播放/不会失败）。
    // 靠状态文件保证只跑一次，否则每次启动都会把玩家新拿到的无瑕清掉。
    private static string _flawlessMigrationPath;
    /// <summary>迁移标记的版本，必须跟着迁移规则一起升：文件内容与它不等 → 是更早（不完整）的迁移落下的标记，要按新规则重跑。</summary>
    private const string FlawlessMigrationTag = "flawless_recompute_v2";

    /// <summary>静默解锁：写进解锁表但不弹提示、也不加进 _unseen（迁移重发用，玩家本来就有，弹提示像刷屏）。</summary>
    private static bool UnlockSilently(string id) {
        if (string.IsNullOrEmpty(id) || _unlocked.ContainsKey(id)) return false;
        if (GetDef(id) == null) return false;
        _unlocked[id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // 成就页那一行的等级/经验/解锁数是缓存的（每帧重算要扫 6 遍全表）。
        // 解锁表一改它就得作废 —— 在「写表的这一层」失效，一处盖住全部调用点，
        // 比在 20 多个 TryUnlock 调用处各写一遍可靠得多（漏一个就显示旧数据）。
        PortalUI.InvalidateAchBarValues();
        return true;
    }

    /// <summary>这条通关记录算不算"真无瑕"（与触发条件完全同一套判据）。</summary>
    private static bool RecordIsFlawless(LevelScanner.ClearRecord r) {
        if (r == null) return false;
        if (r.Checkpoints != 0) return false;      // 用了检查点/从半途起玩
        if (r.Autoplay || r.NoFail) return false;  // 开过辅助
        return r.Xacc >= 99.5f && r.Perfect > 0
            && r.Early == 0 && r.TooFast == 0 && r.Fast == 0 && r.Slow == 0 && r.TooSlow == 0;
    }

    /// <summary>一次性迁移：取消全部无瑕成就 → 按通关记录重算真无瑕并静默重发，靠状态文件标记只跑一次。</summary>
    public static void RunFlawlessMigrationIfNeeded() {
        try {
            if (string.IsNullOrEmpty(_flawlessMigrationPath)) return;
            // 标记内容 = 当前迁移版本 才算跑过；被更早版本的迁移写过 → 按新规则再跑一次
            try {
                if (File.Exists(_flawlessMigrationPath)) {
                    string had = File.ReadAllText(_flawlessMigrationPath);
                    if (had != null && had.TrimStart().StartsWith(FlawlessMigrationTag, StringComparison.Ordinal)) return;
                    Lg.Info($"[LevelLib] 无瑕重算：发现更早版本的迁移标记，按 v2 规则重跑一次"
                        + $"（旧标记首行：{(had ?? "").Split('\n')[0].Trim()}）");
                }
            } catch { }
            // 安全闸：谱面库为空时绝不能撤成就 —— 会撤掉却没有任何谱面可重建，等于真的清空。
            // 因此先尝试扫一次，还是空就放弃本次且不落标记，等扫描完成后再跑。
            try {
                if (LevelScanner.Entries == null || LevelScanner.Entries.Count == 0) {
                    try { LevelScanner.Refresh(); } catch { }
                    if (LevelScanner.Entries == null || LevelScanner.Entries.Count == 0) {
                        Lg.Info("无瑕重算：谱面库还是空的，本次跳过（不落标记，等扫描完成后再跑）");
                        return;
                    }
                }
            } catch (Exception ex) { Lg.Ex("无瑕重算：预扫描失败，本次跳过", ex); return; }
            // 先落标记，避免迁移中途异常导致反复清空
            try { File.WriteAllText(_flawlessMigrationPath, FlawlessMigrationTag + Environment.NewLine + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); } catch { }

            // 取消：无瑕类 + 世一开 + P/G/U 首通类（它们同样是"标记 + 通关记录"推出来的，
            // 于是"从最后一格起玩"也能骗到；里程碑本身没销毁，因为它的凭据就是标记，而标记仍在）
            var toRemove = new List<string>();
            foreach (var id in _unlocked.Keys) {
                if (id == null) continue;
                if (id.StartsWith("ach_flawless_") || id == "ach_shi"
                    || id == "ach_p10" || id == "ach_galactic" || id == "ach_universal") toRemove.Add(id);
            }
            foreach (var id in toRemove) {
                _unlocked.Remove(id);
                _unseen.Remove(id);      // 已查看状态一并清掉，免得残留
            }
            PortalUI.InvalidateAchBarValues();   // 撤了成就 → 等级/经验/解锁数都变了
            Lg.Info($"[LevelLib] 无瑕重算：已取消 {toRemove.Count} 个无瑕/世一开/PGU 首通成就，开始按通关记录重建");

            // 遍历谱面：按"标记 + 通关记录"重建
            int regranted = 0, charts = 0, flags = 0;
            bool isP10 = false, isGalactic = false, isUniversal = false;
            var entries = LevelScanner.Entries;
            if (entries != null) {
                foreach (var e in entries) {
                    try {
                        string mark = MarkStore.Get(e.FilePath);
                        if (string.IsNullOrEmpty(mark) || mark.Length < 2) continue;
                        char head = mark[0];
                        if (head != 'P' && head != 'G' && head != 'U') continue;
                        int n = 0; bool num = true;
                        for (int i = 1; i < mark.Length; i++) {
                            if (mark[i] < '0' || mark[i] > '9') { num = false; break; }
                            n = n * 10 + (mark[i] - '0');
                        }
                        if (!num || n < 1 || n > 20) continue;

                        bool anyClear = false, strictClear = false, flawless = false;
                        // 用 PeekClearRecords：下面只对每条做存在性判断（有没有 / 有没有严判 / 有没有无瑕），
                        // 三个标志都要看完整个列表，谁都不需要"谁高谁在前"。GetClearRecords 会为这次
                        // 遍历拷一整份 List 再委托排序，纯属白付。
                        foreach (var r in LevelScanner.PeekClearRecords(e.FolderPath)) {
                            anyClear = true;                                   // 有任何通关记录（含检查点起玩）
                            if (r.Slot == 2) strictClear = true;               // 严判通关
                            if (RecordIsFlawless(r)) flawless = true;          // 真无瑕
                        }
                        // P/G/U 首通类：跟原来的判定一致（G/U 只要有任何通关记录，P10 要严判记录）
                        if (head == 'G' && anyClear) isGalactic = true;
                        if (head == 'U' && anyClear) isUniversal = true;
                        if (head == 'P' && n >= 10 && strictClear) isP10 = true;

                        if (!flawless) continue;
                        charts++;
                        if (UnlockSilently("ach_flawless_" + head + n)) regranted++;
                        if (head == 'U' && n >= 15) UnlockSilently("ach_shi");
                    } catch (Exception ex) { Lg.Ex("无瑕重算：单个谱面处理失败", ex); }
                }
            }
            if (isP10) { UnlockSilently("ach_p10"); flags++; }
            if (isGalactic) { UnlockSilently("ach_galactic"); flags++; }
            if (isUniversal) { UnlockSilently("ach_universal"); flags++; }
            Save();
            CheckCollect();   // 无瑕被清可能影响"全收集"，重算一遍
            Lg.Info($"[LevelLib] 无瑕重算完成：{charts} 个谱面有真无瑕记录，重发 {regranted} 个无瑕"
                + $"，P/G/U 首通类重发 {flags} 个（P10={isP10} Galactic={isGalactic} Universal={isUniversal}）"
                + $"（其余旧记录已取消；本次只执行一次）");
        } catch (Exception e) {
            Lg.Ex("无瑕重算失败", e);
        }
    }

    /// <summary>标记 → (P/G/U, 等级) 解析；不是"字母 + 数字 1..20"就返回 false</summary>
    private static bool ParseMarkTier(string mark, out char head, out int n) {
        head = '\0'; n = 0;
        if (string.IsNullOrEmpty(mark) || mark.Length < 2) return false;
        char h = mark[0];
        if (h != 'P' && h != 'G' && h != 'U') return false;
        int v = 0;
        for (int i = 1; i < mark.Length; i++) {
            if (mark[i] < '0' || mark[i] > '9') return false;
            v = v * 10 + (mark[i] - '0');
        }
        if (v < 1 || v > 20) return false;
        head = h; n = v;
        return true;
    }

    /// <summary>这个谱面有没有"严判通关"记录（记录里的 Slot==2 就是严判那一档）</summary>
    private static bool HasStrictClear(string folderPath) {
        try {
            // 用 PeekClearRecords 而不是 GetClearRecords：本方法只问"有没有一条 Slot==2"，
            // 命中第一行就 return，剩下的记录根本不看 —— 而 GetClearRecords 会为这次提问
            // 拷一整份 List 再委托排序。方法名就写着 Has，顺序也用不上。
            foreach (var r in LevelScanner.PeekClearRecords(folderPath)) if (r.Slot == 2) return true;
        } catch (Exception) { }
        return false;
    }

    /// <summary>重新推导 P/G/U 1-20 的里程碑成就：谱面当前标记解析成 P/G/U + n（1..20）且该谱面有严判通关记录 → 算解锁，少任何一条就撤销。
    /// 只动 ach_milestone_*，不碰无瑕/全收集/其它成就（那些是做到了就永久算的历史记录）。
    /// silent=true 时新解锁不弹提示（开机归一化用）。返回解锁 + 撤销的总条数，0 = 状态本来就对。</summary>
    public static int RebuildMilestones(bool silent) {
        var want = new Dictionary<string, string>();   // id → 谱面名（详情里显示用）
        try {
            var entries = LevelScanner.Entries;
            if (entries != null) {
                foreach (var e in entries) {
                    if (e == null) continue;
                    char head; int n;
                    if (!ParseMarkTier(MarkStore.Get(e.FilePath), out head, out n)) continue;
                    if (!HasStrictClear(e.FolderPath)) continue;   // 没有严判通关记录 → 不算
                    string cn = e.Title;
                    if (string.IsNullOrEmpty(cn) && !string.IsNullOrEmpty(e.FolderPath))
                        cn = new System.IO.DirectoryInfo(e.FolderPath).Name;
                    want["ach_milestone_" + head + n] = cn;
                }
            }
        } catch (Exception ex) { Lg.Ex("里程碑重算扫描失败", ex); }

        int changed = 0;
        bool chartChanged = false;
        // ① 该有的：解锁（缺谱面名时补上）
        foreach (var kv in want) {
            if (!_unlocked.ContainsKey(kv.Key)) {
                if (silent) { if (UnlockSilently(kv.Key)) changed++; }
                else if (TryUnlock(kv.Key)) changed++;
            }
            // 只改内存、攒到循环外统一 Save（原来每条不一致就整文件重写一次）
            if (GetMilestoneChart(kv.Key) != kv.Value && TrySetMilestoneChart(kv.Key, kv.Value)) chartChanged = true;
        }
        // ② 不该有的：撤销（只撤里程碑这一类）
        var toRemove = new List<string>();
        foreach (var id in _unlocked.Keys)
            if (id != null && id.StartsWith("ach_milestone_") && !want.ContainsKey(id)) toRemove.Add(id);
        foreach (var id in toRemove) {
            _unlocked.Remove(id);
            _unseen.Remove(id);
            PortalUI.InvalidateAchBarValues();   // 同上：撤销里程碑也要让成就页那行缓存作废
            _milestoneChart.Remove(id);   // 谱面名一起清，别留孤儿（顺带省一次 Save）
            changed++;
        }
        // 里程碑谱面名改过也要落盘：原来是在循环里逐条 Save，这里并到末尾一次。
        // 注意不改 changed 的计数 —— 返回值与日志口径保持原样（它只数解锁/撤销）。
        if (changed > 0 || chartChanged) {
            Save();
            if (changed > 0) {
                Lg.Info($"[LevelLib] 里程碑重算：解锁/撤销共 {changed} 条，"
                    + $"当前有效里程碑 {want.Count} 个{(silent ? "（静默：不弹提示）" : "")}");
                // 撤销不会触发 OnUnlocked → 这里主动通知界面刷新（成就页/详情都是缓存式的）
                try { PortalUI.RefreshAchievementsAfterChange(); } catch (Exception) { }
            }
        }
        return changed;
    }

    /// <summary>扫描谱面库解锁标记类成就：标判 P17→新手村大门；严判 P17→未来的开始；G 级（宽/标/严判）→Galactic；U 级→Universal；P10+ 需严判通关记录</summary>
    public static void CheckVillageMark() {
        // 一次性迁移：旧版"从最后一格也能拿无瑕"的追溯清理与重建（见 RunFlawlessMigrationIfNeeded）
        RunFlawlessMigrationIfNeeded();
        CheckProgression();   // 顺带检查累计/列表/通关类成就（防抖内置）
        CheckPersist();       // 补发：玩很多次仍未通关（坚持系，含历史数据）
        // 里程碑（严判击破 P/G/U1-20）走 RebuildMilestones 推导，顺带撤销已不成立的 → 下面循环里不再单独解锁里程碑
        RebuildMilestones(false);
        try {
            var entries = LevelScanner.Entries;
            if (entries == null) return;
            foreach (var e in entries) {
                string mark = MarkStore.Get(e.FilePath);
                if (mark == null || mark.Length < 2) continue;
                string judge = MarkStore.GetJudge(e.FilePath);
                bool any = judge == "宽判" || judge == "标判" || judge == "严判";
                bool pJudged = judge == "标判" || judge == "严判";
                if (mark == "P17") {
                    if (judge == "标判") TryUnlock("ach_village");
                    else if (judge == "严判") TryUnlock("ach_start");
                    // 不 continue：P17 也属 P10+，继续走下方 P 分支检查严判通关
                }
                char head = mark[0];
                bool num = true;
                int n = 0;
                for (int i = 1; i < mark.Length; i++) { if (mark[i] < '0' || mark[i] > '9') { num = false; break; } n = n * 10 + (mark[i] - '0'); }
                // 里程碑不在这里"见到就解锁"：它跟当前标记 + 严判通关记录强绑定，改等级后旧里程碑会留成脏数据
                if (!num || !any) continue;
                if (head == 'G') TryUnlock("ach_galactic");
                else if (head == 'U') TryUnlock("ach_universal");
                else if (head == 'P') {
                    if (n >= 10) {
                        // P10+：需严判通关记录（Slot==2）
                        try {
                            foreach (var r in LevelScanner.PeekClearRecords(e.FolderPath)) {
                                if (r.Slot == 2) { TryUnlock("ach_p10"); break; }
                            }
                        } catch (System.Exception) { }
                    }
                }
                // 无瑕成就：Xacc=100 且判定只有完美，60 个细分成败由谱面等级决定（P5 谱面无瑕 → P5完美无瑕）。
                // 四条限制与里程碑一致：从第一格开始（Checkpoints==0）、没开自动播放、没开不会失败。
                // 自动播放/不会失败必须从记录里读，不能靠"有记录"间接保证 —— 写入端那道拦（NotRealRun）
                // 依赖可被玩家关掉的 RecordRestrictions 开关。
                if (head == 'G' || head == 'U' || head == 'P') {
                    try {
                        foreach (var r in LevelScanner.PeekClearRecords(e.FolderPath)) {
                            if (r.Checkpoints != 0) continue;   // 用了检查点/从半途起玩 → 不算无瑕
                            if (r.Autoplay || r.NoFail) continue;   // 自动播放 / 不会失败 → 不算无瑕
                            if (r.Xacc >= 99.5f && r.Perfect > 0 && r.Early == 0 && r.TooFast == 0 && r.Fast == 0 && r.Slow == 0 && r.TooSlow == 0) {
                                TryUnlock("ach_flawless_" + head + n);       // 该等级的无瑕成就
                                if (head == 'U' && n >= 15) TryUnlock("ach_shi");   // 世一开：U15+ 完美无瑕
                                break;
                            }
                        }
                    } catch (System.Exception) { }
                }
            }
            CheckCollect();   // 里程碑全部集齐后补发全收集成就
        } catch (System.Exception ex) { Lg.Ex("扫描标记成就失败", ex); }
    }

    /// <summary>全收集成就：P1-20 / G1-20 / U1-20 里程碑全部解锁时分别触发</summary>
    public static void CheckCollect() {
        try {
            if (AllMilestones("P")) TryUnlock("ach_pcollect");
            if (AllMilestones("G")) TryUnlock("ach_gcollect");
            if (AllMilestones("U")) TryUnlock("ach_ucollect");
        } catch (System.Exception ex) { Lg.Ex("全收集成就检查失败", ex); }
    }

    private static bool AllMilestones(string t) {
        for (int n = 1; n <= 20; n++) {
            if (!_unlocked.ContainsKey("ach_milestone_" + t + n)) return false;
        }
        return true;
    }

    private static float _lastProgCheck = -99f;
    private static bool _persistScanned;   // 坚持系扫描：每局游戏只扫一次（防列表频繁遍历影响性能）
    /// <summary>检查累计型成就：Thousand/100days/地球年/100Hours（防抖：3秒内不重复全查）</summary>
    public static void CheckProgression() {
        float now = UnityEngine.Time.unscaledTime;
        if (now - _lastProgCheck < 3f) return;   // 防抖：游玩记录频繁，避免重复读 playlog
        _lastProgCheck = now;
        try {
            long plays = LevelScanner.GetTotalPlayCount();
            if (plays >= 1000) TryUnlock("ach_thousand");
            if (plays >= 10000) TryUnlock("ach_wan");
            if (plays >= 100000) TryUnlock("ach_heng");
            int days = LevelScanner.GetLoginDayCount();
            if (days >= 100) TryUnlock("ach_100days");
            if (days >= 365) TryUnlock("ach_earthyear");
            if (LevelScanner.GetTotalPlayTime() >= 360000L) TryUnlock("ach_100hours");   // 100小时=360000秒
            int listN = LevelScanner.Entries != null ? LevelScanner.Entries.Count : 0;
            if (listN >= 20) TryUnlock("ach_space");
            if (listN >= 200) TryUnlock("ach_matrix");
            if (listN >= 2000) TryUnlock("ach_puni");
            int cleared = LevelScanner.GetClearedCount();
            if (cleared >= 1) TryUnlock("ach_firstclear");
            if (cleared >= 10) TryUnlock("ach_noob");
            if (cleared >= 100) TryUnlock("ach_master");
            if (cleared >= 1000) TryUnlock("ach_dragon");
        } catch (System.Exception ex) { Lg.Ex("累计成就检查失败", ex); }
    }

    /// <summary>扫描已有谱面：玩很多次仍未通关（坚持/耐心/休息/炼成坚持蛊）——用于补发历史数据（OpenPanel 启动时调用）</summary>
    public static void CheckPersist() {
        if (_persistScanned) return;   // 每局游戏只扫一次
        var entries = LevelScanner.Entries;
        if (entries == null) return;   // 列表未加载则本次不作数，下次再扫
        _persistScanned = true;
        try {
            foreach (var e in entries) {
                int pc = LevelScanner.GetPlays(e.FolderPath);
                if (pc <= 100) continue;
                if (LevelScanner.HasClearRecords(e.FolderPath)) continue;   // 已通关跳过
                if (pc > 100) TryUnlock("ach_persist");
                if (pc > 500) TryUnlock("ach_patient");
                if (pc > 1000) TryUnlock("ach_rest");
                if (pc > 2000) TryUnlock("ach_gu");
            }
        } catch (System.Exception ex) { Lg.Ex("成就坚持扫描失败", ex); }
    }

    /// <summary>尝试解锁：已解锁返回 false；新解锁则记录 + 触发回调 + 返回 true</summary>
    public static bool TryUnlock(string id) {
        if (_unlocked.ContainsKey(id)) return false;
        var d = GetDef(id);
        if (d == null) return false;
        _unlocked[id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        PortalUI.InvalidateAchBarValues();   // 同 UnlockSilently：写表即作废成就页那行缓存
        _unseen.Add(id);
        Save();
        try { OnUnlocked?.Invoke(d); } catch (Exception e) { Lg.Ex("成就提示异常", e); }
        return true;
    }

    public static AchievementDef GetDef(string id) {
        for (int i = 0; i < Defs.Count; i++) if (Defs[i].Id == id) return Defs[i];
        return null;   // 找不到返回 null（AchievementDef 现在是 class）
    }

    public static string GetUnlockTimeText(string id) {
        if (!_unlocked.TryGetValue(id, out long t)) return "";
        var dt = DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime;
        return dt.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>记录里程碑触发谱面名（超出20字截断）</summary>
    public static void SetMilestoneChart(string id, string chartName) {
        if (!TrySetMilestoneChart(id, chartName)) return;
        Save();
    }

    /// <summary>写里程碑谱面名但不落盘；返回"值真的变了"（调用方攒一批后统一 Save 一次）。
    /// 存在是因为 RebuildMilestones 会在一个循环里调它几十次，每次 Save 都是整份 achievements.txt
    /// 全量重写 —— 那是通关那一帧最集中的写盘放大。外层本来就有一次合并 Save，不该在这里再存。</summary>
    private static bool TrySetMilestoneChart(string id, string chartName) {
        if (string.IsNullOrEmpty(id) || chartName == null) return false;
        if (chartName.Length > 20) chartName = chartName.Substring(0, 20);
        if (_milestoneChart.TryGetValue(id, out var old) && old == chartName) return false;
        _milestoneChart[id] = chartName;
        return true;
    }

    public static string GetMilestoneChart(string id) {
        return _milestoneChart.TryGetValue(id, out var n) ? n : "";
    }

    /// <summary>成就触发条件文案：里程碑显示触发谱面名（无记录则回退 Desc）</summary>
    public static string GetTriggerText(AchievementDef d) {
        if (d.Id.StartsWith("ach_milestone_")) {
            string name = GetMilestoneChart(d.Id);
            if (name.Length > 20) name = name.Substring(0, 20);
            return name.Length > 0 ? name : d.Desc;
        }
        return d.Desc;
    }

    /// <summary>成就名颜色：1白 2绿 3蓝 4红 5彩色(亮粉)</summary>
    public static Color GetLevelColor(int level) {
        switch (level) {
            case 2: return new Color(0.4f, 1f, 0.5f, 1f);      // 绿
            case 3: return new Color(0.35f, 0.7f, 1f, 1f);     // 蓝
            case 4: return new Color(1f, 0.35f, 0.35f, 1f);    // 红
            case 5: return new Color(1f, 0.4f, 0.9f, 1f);      // 彩色(亮粉)
            default: return new Color(1f, 1f, 1f, 1f);         // 白
        }
    }

    // ---------- 成就经验 / 成就等级 ----------
    // 每条成就按它自己的等级给经验：1级=10、2级=20、3级=50、4级=100、5级=400，只统计已解锁的，每条算一次。
    // 等级门槛是累计经验：0 / 200 / 800 / 4600 / 10600。
    // 每日成就不计经验：它们每天刷新、不写进永久解锁表（achievements.txt），计进去会让等级每天上下跳。

    /// <summary>某个等级的成就给多少经验</summary>
    public static int ExpOfLevel(int level) {
        switch (level) {
            case 1: return 10;
            case 2: return 20;
            case 3: return 50;
            case 4: return 100;
            case 5: return 400;
            default: return 0;
        }
    }

    /// <summary>官方成就的经验表（当前没有调用者：官方成就不折算经验，见 TotalExp。
    /// 留着是为了以后要改成"官方成就只给象征性经验"时不必重推数值）。按世界分级的固定值，下标 = 官方成就等级-1。
    /// 不能用 ExpOfLevel(等级)：那是 10/20/50/100/400，197 个一叠会直接把玩家顶到满级。要调数值只动这一行。</summary>
    private static readonly int[] OfficialExpByLevel = { 1, 2, 5, 3, 5 };

    /// <summary>官方成就的经验（下标越界就取端点，等级读错也不会给爆经验）</summary>
    public static int OfficialExpOf(int level) {
        return OfficialExpByLevel[Mathf.Clamp(level - 1, 0, OfficialExpByLevel.Length - 1)];
    }

    /// <summary>成就等级门槛（累计经验），下标 = 等级-1：0 / 200 / 800 / 4600 / 10600。
    /// 经验本身没变（还是各成就的等级经验之和），门槛翻倍后已解锁很多成就的玩家等级会掉 1~2 级，这是预期结果，别改成"保留原等级"。</summary>
    private static readonly int[] LevelExp = { 0, 200, 800, 4600, 10600 };
    public const int MaxAchievementLevel = 5;

    /// <summary>所有已解锁成就的经验总和（不含每日）。
    /// 成就页每帧调用，所以这里绝对不能调 ApplyTriggerDisplay —— 它内部是 GetDef() 线性查找，160 条 × 160 次字符串比较每帧白烧 CPU。</summary>
    public static int TotalExp() {
        int sum = 0;
        try {
            for (int i = 0; i < Defs.Count; i++) {
                var d = Defs[i];
                if (d == null || d.IsDaily) continue;      // 每日不计
                if (!_unlocked.ContainsKey(d.Id)) continue;
                // 官方成就（ach_official_，194 个）不折算经验：它们数量太多，一装就顶满级，
                // 把升级曲线压平。成就本身照旧留在列表里、解锁状态与卡片显示都不受影响。
                if (d.Id.StartsWith(AchOfficialPrefix)) continue;
                int lv = d.Level;
                long tc = GetTriggerCount(d.Id);
                if (tc > 0) lv = RepeatLevel(d.Id, tc);    // 可重复成就：等级随次数变（两套规则在里面分派）
                sum += ExpOfLevel(lv);
            }
        } catch (Exception e) { Lg.Ex("统计成就经验失败", e); }
        return sum;
    }

    /// <summary>等级进度（给那条进度条用）：
    /// level=当前等级；exp=总经验；floorExp=当前等级门槛；nextExp=下一级门槛（满级时与 floorExp 相等 → 进度算满）。</summary>
    public static void LevelProgress(out int level, out int exp, out int floorExp, out int nextExp) {
        exp = TotalExp();
        int lv = 1;
        for (int i = 0; i < LevelExp.Length; i++) if (exp >= LevelExp[i]) lv = i + 1;
        lv = Mathf.Clamp(lv, 1, MaxAchievementLevel);
        level = lv;
        floorExp = LevelExp[lv - 1];
        nextExp = lv >= MaxAchievementLevel ? LevelExp[MaxAchievementLevel - 1] : LevelExp[lv];
    }

    /// <summary>进度条填充比例 0~1（满级 = 1）</summary>
    public static float LevelFill() {
        int lv, exp, floor, next;
        LevelProgress(out lv, out exp, out floor, out next);
        if (lv >= MaxAchievementLevel || next <= floor) return 1f;
        return Mathf.Clamp01((float)(exp - floor) / (next - floor));
    }

    /// <summary>已解锁数 / 总数（给分类 tab 上的"12/120"用；不含每日）</summary>
    public static void CountUnlocked(AchCategory cat, out int done, out int total) {
        done = 0; total = 0;
        for (int i = 0; i < Defs.Count; i++) {
            var d = Defs[i];
            if (d == null || d.IsDaily) continue;
            if (d.Cat != cat) continue;
            total++;
            if (IsUnlocked(d.Id)) done++;
        }
    }

    /// <summary>「恭喜你：PP-1」那种"可重复成就"的等级区间（给卡片上的升级进度条用）。
    /// 区间与 PP1Level 保持一致：1-10 / 11-50 / 51-100 / 101-200 / 201-500（500 以上停在 5 级）。</summary>
    public static void RepeatBand(long count, out long lo, out long hi, out bool maxed) {
        maxed = false;
        if (count <= 10) { lo = 1; hi = 10; }
        else if (count <= 50) { lo = 11; hi = 50; }
        else if (count <= 100) { lo = 51; hi = 100; }
        else if (count <= 200) { lo = 101; hi = 200; }
        else if (count <= 500) { lo = 201; hi = 500; }
        else { lo = 201; hi = 500; maxed = true; }
    }
}
