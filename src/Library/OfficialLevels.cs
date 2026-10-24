using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LevelLibrary;

/// <summary>官方关卡的数据与统计（主页棋盘用）。
/// 结构：一个世界里 1-1、1-2… 的数字关都归到该世界的最终关 W-X，棋盘每个世界只放一个图标（W-X），数字关进详情区当子关卡列。
/// 统计写在 official_stats.txt（按关卡 id 存，键带 official:// 前缀）。不能混进 records.txt / deathmap.txt：那两套按谱面文件夹路径存。
/// 通关记录的判据跟谱面库一致：用了检查点的局不记、自动播放的局不记。</summary>
internal static class OfficialLevels {

    /// <summary>我们自己的官方关卡统计文件（已写进 update_package.ps1 的 $excludeRoot，不进发布包）</summary>
    public const string StatsFileName = "official_stats.txt";
    /// <summary>官方关卡的统计键前缀（跟谱面文件夹路径区分开）</summary>
    public const string KeyPrefix = "official://";

    // ================= 世界 / 关卡表 =================

    public sealed class WorldInfo {
        public string Key;              // 世界号，如 "1" / "XF"
        public int Index;               // WorldData.index（读存档要用）
        public string Island;           // Main / Xtra / Crown / MuseDash / CR2024 / Vega
        public string FinalId;          // 最终关 id，如 "1-X"
        public string FinalName;        // 最终关名（本地化）
        public readonly List<string> SubIds = new List<string>();   // 1-1 … 1-6 + 1-X
        public int Difficulty;
        public float Trial;
        public bool HasCheckpoints;
        public bool IsDLC;
    }

    private static List<WorldInfo> _worlds;

    /// <summary>主页要显示的世界（排除 Taro 和 DeadWorlds，按 index 排序；结果缓存）。
    /// 数据源必须是 WorldData.dict 全集而不是 allWorlds（后者已替我们滤掉 doNotBuild / notRealWorld，会漏掉要显示的世界）；
    /// 筛选靠"这个世界的 X 关在本地化表里存在吗"。世界表里没有、只在文本表里有关卡的世界（如 TN-X）不会出现在棋盘上。</summary>
    public static List<WorldInfo> Worlds() {
        if (_worlds != null) return _worlds;
        var list = new List<WorldInfo>();
        try {
            foreach (var kv in WorldData.dict) {
                string key = kv.Key;
                WorldData w = kv.Value;
                if (w == null) continue;
                if (w.island == WorldData.IslandType.Taro) continue;   // Taro 不算官方关卡
                if (DeadWorlds.Contains(key)) continue;
                if (!Exists(key + "-X")) continue;                     // 连最终关都没有 → 不是世界
                var info = new WorldInfo {
                    Key = key, Index = w.index, Island = w.island.ToString(),
                    FinalId = key + "-X", Difficulty = w.difficulty,
                    Trial = w.trialAim, HasCheckpoints = w.hasCheckpoints, IsDLC = w.isDLC
                };
                FillSubIds(info);
                info.FinalName = NameOf(info.FinalId);
                list.Add(info);
            }
        } catch (Exception e) { Log("列世界失败: " + e.GetType().Name + " " + e.Message); }
        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        _worlds = list;
        Log("官方关卡棋盘：" + list.Count + " 个世界（排除 Taro + 废关 TN/XP；含 TP）");
        return list;
    }

    /// <summary>废关世界：整个不显示。以后还有废关就往这里加（只改这一处）。</summary>
    private static readonly System.Collections.Generic.HashSet<string> DeadWorlds =
        new System.Collections.Generic.HashSet<string> { "XP", "TN" };

    /// <summary>子关卡：1、2、3… 一直试到本地化表里没有为止，最后再补一个 X。
    /// 不能靠 levelCount 硬算（世界 6 写 5、实际有 6-1..6-5 + 6-X，硬算会少一关）。</summary>
    private static void FillSubIds(WorldInfo info) {
        try {
            for (int n = 1; n <= 40; n++) {
                string id = info.Key + "-" + n;
                if (!Exists(id)) break;
                info.SubIds.Add(id);
            }
            if (Exists(info.FinalId)) info.SubIds.Add(info.FinalId);
            else if (info.SubIds.Count == 0) info.SubIds.Add(info.FinalId);   // 兜底：至少有一个 X
        } catch { }
    }

    /// <summary>这个关卡 id 在游戏里存在吗（本地化表里有 id.title 就算存在）</summary>
    public static bool Exists(string id) {
        try {
            bool ok;
            ADOBase.GetLocalizedLevelNameWithCheck(id, out ok);
            return ok;
        } catch { return false; }
    }

    /// <summary>关卡显示名（去掉 "1-1 " 前缀，只留名字；取不到就返回 id）</summary>
    public static string NameOf(string id) {
        try {
            string full = ADOBase.GetLocalizedLevelName(id);
            if (!string.IsNullOrEmpty(full)) {
                int sp = full.IndexOf(' ');
                if (sp > 0 && sp + 1 < full.Length) return full.Substring(sp + 1);
                return full;
            }
        } catch { }
        return id;
    }

    /// <summary>统计键（存我们自己的 official_stats.txt 用）</summary>
    public static string Key(string id) { return KeyPrefix + id; }

    // ================= 当前是不是在官方关卡里 =================

    /// <summary>当前正在玩的官方关卡 id；不在官方关卡里返回 null。
    /// 官方关卡分两支：派 scnGame 的看 GCS.internalLevelName，独立场景的（1-X、Taro 那种）场景名本身就是关卡 id。
    /// 不用 isInternalLevel —— 它对独立场景是 false。</summary>
    public static string CurrentId() {
        try {
            string inner = GCS.internalLevelName;
            if (!string.IsNullOrEmpty(inner)) return inner;
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene) || scene.IndexOf('-') < 0) return null;
            string world = scene.Substring(0, scene.IndexOf('-'));
            if (WorldData.dict != null && WorldData.dict.ContainsKey(world)) return scene;   // 形如 1-X / T1-1
            return null;
        } catch { return null; }
    }

    // ================= 进入关卡 =================

    /// <summary>从我们的界面进入官方关卡：先关面板（还原输入与 timeScale），再调游戏自己的 EnterLevel。
    /// 用 EnterLevel 是因为它内部会把 scnGame + internalLevelName 和独立场景两支都处理好，别自己拼 GCS 字段。</summary>
    public static bool Enter(string id) {
        if (string.IsNullOrEmpty(id)) return false;
        try {
            var ctrl = ADOBase.controller;
            if (ctrl == null) { PortalUI.ShowToast(Lang.Tr("现在还不能进关卡（游戏还没就绪）")); return false; }
            Log("进入官方关卡：" + id);
            try { PortalUI.ClosePanel(); } catch { }        // 面板关掉、游戏输入与 timeScale 还原
            ctrl.EnterLevel(id);
            return true;
        } catch (Exception e) {
            Log("进入官方关卡失败 " + id + ": " + e.GetType().Name + " " + e.Message);
            PortalUI.ShowToast(Lang.Tr("进入关卡失败，看日志喵"));
            return false;
        }
    }

    // ================= 我们自己的统计（游玩次数 / 通关记录 / 寄录图）=================

    private sealed class LevelStat {
        public int Plays;
        public int[] Deaths = new int[DeathBuckets];   // 按完成度 1% 一格
        public readonly List<Clear> Clears = new List<Clear>();
    }

    public sealed class Clear {
        public long Time;        // Unix 秒
        public int Slot = -1;    // 0 宽 / 1 标 / 2 严
        public int Percent;      // 通关时完成度（官方关卡都是 100）
        public int Checkpoints;  // 用了检查点的局不记（这里恒 0，留着兼容）
    }

    public const int DeathBuckets = 100;
    private static readonly Dictionary<string, LevelStat> _stats = new Dictionary<string, LevelStat>();
    private static string _statsPath;
    private static bool _loaded;
    private static string _lastTrackingId;        // 当前这一局在统计哪个关卡（进入时 +1 游玩次数一次）
    private static bool _clearedThisRun;          // 本局已经记过通关了（防同一局重复记）

    private static LevelStat Stat(string id) {
        Load();
        LevelStat s;
        if (!_stats.TryGetValue(id, out s)) { s = new LevelStat(); _stats[id] = s; }
        return s;
    }

    private static void Load() {
        if (_loaded) return;
        _loaded = true;
        try {
            _statsPath = Path.Combine(Main.ModEntry.Path, StatsFileName);
            if (!File.Exists(_statsPath)) return;
            var lines = File.ReadAllLines(_statsPath);
            for (int i = 0; i < lines.Length; i++) {
                string ln = lines[i];
                if (string.IsNullOrEmpty(ln) || ln[0] == '#') continue;
                var f = ln.Split('\t');
                if (f.Length >= 4 && f[0] == "P") {
                    var s = Stat2(f[1]);
                    int plays;
                    if (int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out plays)) s.Plays = plays;
                    var parts = f[3].Split(',');
                    for (int b = 0; b < s.Deaths.Length && b < parts.Length; b++) {
                        int v;
                        if (int.TryParse(parts[b], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) s.Deaths[b] = v;
                    }
                } else if (f.Length >= 5 && f[0] == "C") {
                    var s = Stat2(f[1]);
                    var c = new Clear();
                    long t; int sl, pc, cp;
                    long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out t);
                    int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sl);
                    int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out pc);
                    int.TryParse(f.Length > 5 ? f[5] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out cp);
                    c.Time = t; c.Slot = sl; c.Percent = pc; c.Checkpoints = cp;
                    s.Clears.Add(c);
                }
            }
            Log("官方关卡统计已读：" + _stats.Count + " 个关卡");
        } catch (Exception e) { Log("读官方关卡统计失败: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>读档时用（不触发生成条目）</summary>
    private static LevelStat Stat2(string id) {
        LevelStat s;
        if (!_stats.TryGetValue(id, out s)) { s = new LevelStat(); _stats[id] = s; }
        return s;
    }

    private static void Save() {
        try {
            if (string.IsNullOrEmpty(_statsPath)) _statsPath = Path.Combine(Main.ModEntry.Path, StatsFileName);
            var sb = new StringBuilder();
            sb.AppendLine("# LevelLibrary 官方关卡统计（洛洛要求的：通关记录与寄录图由我们自己统计）");
            sb.AppendLine("# P\\t关卡id\\t游玩次数\\t死亡分布(100 格，每格 1% 完成度)");
            sb.AppendLine("# C\\t关卡id\\tUnix秒\\t判定0宽1标2严\\t完成度\\t检查点");
            foreach (var kv in _stats) {
                var s = kv.Value;
                sb.Append("P\t").Append(kv.Key).Append('\t').Append(s.Plays).Append('\t');
                for (int b = 0; b < s.Deaths.Length; b++) {
                    if (b > 0) sb.Append(',');
                    sb.Append(s.Deaths[b]);
                }
                sb.AppendLine();
                for (int i = 0; i < s.Clears.Count; i++) {
                    var c = s.Clears[i];
                    sb.Append("C\t").Append(kv.Key).Append('\t').Append(c.Time).Append('\t')
                      .Append(c.Slot).Append('\t').Append(c.Percent).Append('\t').Append(c.Checkpoints).AppendLine();
                }
            }
            File.WriteAllText(_statsPath, sb.ToString(), Encoding.UTF8);
        } catch (Exception e) { Log("写官方关卡统计失败: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>某个官谱的统计（给界面读）：游玩次数 / 死亡分布 / 通关记录。
    /// 返回的是内部数组与列表本身（不复制），调用方不要改。</summary>
    public static void Read(string id, out int plays, out int[] deaths, out List<Clear> clears) {
        var s = Stat(id);
        plays = s.Plays;
        deaths = s.Deaths;
        clears = s.Clears;
    }

    /// <summary>只读地查一个官谱的统计：没有这条记录就返回 false，什么都不建。
    /// 界面一次性遍历全部世界时必须走这条 —— `Read` 内部是 `Stat`（查不到就顺手 new 一个空条目塞进去），
    /// 拿它遍历 41 个世界就会给 official_stats.txt 多出 41 行全 0 的记录（文件是每次 Save 全量重写的）。
    /// 参数拿不到时按「全 0 / 无记录」填，调用方不用判 null。</summary>
    public static bool TryRead(string id, out int plays, out int[] deaths, out int clears) {
        Load();
        LevelStat s;
        if (string.IsNullOrEmpty(id) || !_stats.TryGetValue(id, out s)) {
            plays = 0; deaths = null; clears = 0;
            return false;
        }
        plays = s.Plays;
        deaths = s.Deaths;
        clears = s.Clears.Count;
        return true;
    }

    /// <summary>每帧都要调（面板开没开都要跑）：进了新的官方关卡就记一次游玩次数</summary>
    public static void Tick() {
        try {
            string id = CurrentId();
            if (id == null) { _lastTrackingId = null; _clearedThisRun = false; return; }
            if (id == _lastTrackingId) return;
            _lastTrackingId = id;
            _clearedThisRun = false;
            var s = Stat(id);
            s.Plays++;
            Save();
            Log("官方关卡开始： " + id + "（游玩次数 " + s.Plays + "）");
        } catch (Exception e) { Log("官方关卡 Tick 失败: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>死亡回调（AutoFeatures.OnPlayerDied 里调）：按死亡时完成度 1% 一格记一次寄录图</summary>
    public static void OnPlayerDied() {
        try {
            string id = CurrentId();
            if (id == null) return;
            var lm = ADOBase.lm != null ? ADOBase.lm : scrLevelMaker.instance;
            var ctrl = ADOBase.controller;
            if (lm == null || ctrl == null || lm.listFloors == null || lm.listFloors.Count <= 1) return;
            int tile = ctrl.currentSeqID + 1;
            int total = lm.listFloors.Count;
            int pct = Mathf.Clamp(Mathf.RoundToInt((float)tile / total * 100f), 1, 100);
            var s = Stat(id);
            s.Deaths[pct - 1]++;
            Save();
        } catch (Exception e) { Log("官方关卡死亡统计失败: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>通关回调（AutoFeatures 的通关路径里调）：记一条通关记录。
    /// 判据只有两条：用了检查点的不记、自动播放的不记，另外还要过 AutoFeatures.CleanClearRecords（与谱面库同一条线）。
    /// 判定槽位取通关时的 GCS.difficulty（0 宽 / 1 标 / 2 严）。</summary>
    public static void OnCleared() {
        try {
            string id = CurrentId();
            if (id == null || _clearedThisRun) return;
            // 官方成就：任意通关一次就算（含用检查点的）—— 所以必须排在下面"用了检查点就 return"之前，
            // 那个 return 只管通关记录，两回事别混。
            try { Achievements.TryUnlock(Achievements.OfficialAchId(id)); } catch { }
            int cps = 0;
            try { cps = scrController.checkpointsUsed; } catch { }
            if (cps > 0) { Log("官方关卡通关带检查点 → 不记记录: " + id); _clearedThisRun = true; return; }
            if (AutoFeatures.AutoplayUsedNow()) {
                Log("官方关卡通关开了自动播放 → 不记记录: " + id);
                _clearedThisRun = true;
                return;
            }
            // 跟谱面库那条线同一套判据
            string why2;
            if (!AutoFeatures.CleanClearRecords(AutoFeatures.CurrentFloorCount(), out why2)) {
                Log("官方关卡通关判定不通过（" + why2 + "）→ 不记记录: " + id);
                _clearedThisRun = true;
                return;
            }
            int slot = -1;
            try {
                slot = GCS.difficulty == Difficulty.Lenient ? 0 : GCS.difficulty == Difficulty.Strict ? 2 : 1;
            } catch { }
            var s = Stat(id);
            s.Clears.Add(new Clear {
                Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Slot = slot, Percent = 100, Checkpoints = 0
            });
            if (s.Clears.Count > 200) s.Clears.RemoveAt(0);   // 上限：别让文件无限长
            Save();
            _clearedThisRun = true;
            Log("官方关卡通关记录 +1: " + id + "（判定=" + slot + "）");
        } catch (Exception e) { Log("官方关卡通关统计失败: " + e.GetType().Name + " " + e.Message); }
    }

    private static void Log(string msg) {
        try { Lg.Info("[官方关卡] " + msg); } catch { }
    }
}
