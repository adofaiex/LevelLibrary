using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LevelLibrary;

/// <summary>LevelScanner 的统计分片：游玩日志（playlog / plays）、游玩时长与次数、
/// 完成度（progress）、通关记录（records）的读写，以及旧通关记录的手动清理。</summary>
public static partial class LevelScanner {

    /// <summary>记录一次游玩（追加到 playlog.txt，异步写不卡主线程；同时更新累计次数）</summary>
    public static void RecordPlay(string folderPath) {
        AddPlay(folderPath);
        _lastPlayUnixSec = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();   // 实时时长用（内存）
        if (string.IsNullOrEmpty(folderPath) || string.IsNullOrEmpty(_playLogPath)) return;
        string line = ToRel(folderPath) + "\t" + _lastPlayUnixSec;
        _playLogCacheValid = false;   // 有新增游玩，使缓存失效
        Main.RunBackground("RecordPlay", () => {
            try { File.AppendAllText(_playLogPath, line + "\n"); }
            catch (System.Exception e) { Lg.Ex("写游玩日志失败", e); }
        });
    }

    /// <summary>取某时间区间内各谱面的游玩次数（周报"本周玩得最多"）：返回 folder->{playCount}</summary>
    public static Dictionary<string, int> GetWeekPlays(long startSec, long endSec) {
        var result = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(_playLogPath) || !File.Exists(_playLogPath)) return result;
        try {
            foreach (var line in File.ReadAllLines(_playLogPath)) {
                int idx = line.LastIndexOf('\t');
                if (idx <= 0) continue;
                if (!long.TryParse(line.Substring(idx + 1), out long t)) continue;
                if (t < startSec || t > endSec) continue;
                string key = ToAbs(line.Substring(0, idx).Trim());
                if (key.Length == 0) continue;
                result[key] = result.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        } catch (System.Exception e) { Lg.Ex("读取游玩日志失败", e); }
        return result;
    }

    /// <summary>一次读入 playlog，返回所有 (时间戳)（按时间升序）。带静态缓存 + RecordPlay 失效，避免重复读盘。</summary>
    public static List<long> GetPlayLogTimes() {
        if (_playLogCacheValid && _playLogCache != null) return _playLogCache;
        var times = new List<long>();
        if (string.IsNullOrEmpty(_playLogPath) || !File.Exists(_playLogPath)) {
            _playLogCache = null;
            _playLogCacheValid = true;
            return times;
        }
        try {
            foreach (var line in File.ReadAllLines(_playLogPath)) {
                int idx = line.LastIndexOf('\t');
                if (idx <= 0) continue;
                if (!long.TryParse(line.Substring(idx + 1), out long t)) continue;
                times.Add(t);
            }
        } catch (System.Exception e) { Lg.Ex("读取游玩日志失败", e); }
        times.Sort();
        _playLogCache = times;
        _playLogCacheValid = true;
        return times;
    }

    /// <summary>本周游玩总次数（该区间内所有 playlog 记录总数），已 >0 次筛选</summary>
    public static int GetWeekPlayCount(long startSec, long endSec) {
        int total = 0;
        foreach (var t in GetPlayLogTimes()) if (t >= startSec && t <= endSec) total++;
        return total;
    }

    /// <summary>本周游玩总时长（秒）：时间区间内 playlog 记录按时间排序，相邻间隔< gapSec 视为连续游玩并累加；区间内首尾间隔也计入。</summary>
    public static long GetWeekPlayTime(long startSec, long endSec) {
        var times = new List<long>();
        foreach (var t in GetPlayLogTimes()) if (t >= startSec && t <= endSec) times.Add(t);
        if (times.Count < 2) return 0;
        const long gapSec = 30 * 60;   // 相邻间隔超过 30 分钟断开（挂机不算）
        long total = 0;
        for (int i = 1; i < times.Count; i++) {
            long gap = times[i] - times[i - 1];
            if (gap > 0 && gap <= gapSec) total += gap;
        }
        return total;
    }

    /// <summary>历史游玩总时长（秒）：读全部 playlog（不限区间），相邻间隔<30分钟算连续累加</summary>
    public static long GetTotalPlayTime() {
        var times = GetPlayLogTimes();
        if (times.Count < 2) return 0;
        const long gapSec = 30 * 60;
        long total = 0;
        for (int i = 1; i < times.Count; i++) {
            long gap = times[i] - times[i - 1];
            if (gap > 0 && gap <= gapSec) total += gap;
        }
        return total;
    }

    /// <summary>最后一次记游玩的时间戳（Unix 秒，0=还没有记录）。内存里记着，避免刚写完文件读不到。</summary>
    private static long _lastPlayUnixSec;

    /// <summary>游玩时长（秒），含"这一局还在进行中"的那段：
    /// 基础值只在死亡/通关时跳一下，这里再补上"距上一条记录已过多久"（超过 30 分钟不算）。</summary>
    public static long GetTotalPlayTimeLive() {
        long baseSec = GetTotalPlayTime();
        if (_lastPlayUnixSec <= 0) return baseSec;
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long live = now - _lastPlayUnixSec;
        const long gapSec = 30 * 60;
        if (live <= 0 || live > gapSec) return baseSec;
        return baseSec + live;
    }

    /// <summary>总游玩次数：所有谱面累计游玩次数之和（plays.txt 持久累计）</summary>
    public static long GetTotalPlayCount() {
        long sum = 0;
        foreach (var kv in _plays) sum += kv.Value;
        return sum;
    }

    /// <summary>已通关（有通关记录）的谱面数量：按文件夹去重</summary>
    public static int GetClearedCount() {
        return _records.Count;
    }

    public static void LoadLoginDays(string modPath) {
        _loginPath = System.IO.Path.Combine(modPath, "login_days.txt");
        _loginDays.Clear();
        try {
            if (System.IO.File.Exists(_loginPath)) {
                foreach (var line in System.IO.File.ReadAllLines(_loginPath)) {
                    var s = line.Trim();
                    if (s.Length == 8) _loginDays.Add(s);
                }
            }
        } catch (System.Exception e) { Lg.Ex("加载登录天数失败", e); }
    }

    public static int GetLoginDayCount() => _loginDays.Count;
    /// <summary>记录今天已登录（启动时调用；同一天不重复记）</summary>
    public static void RecordTodayLogin() {
        string today = System.DateTime.Now.ToString("yyyyMMdd");
        if (_loginDays.Add(today)) {
            try { System.IO.File.AppendAllText(_loginPath, today + "\n"); } catch (System.Exception) { }
        }
    }

    /// <summary>本周有几天玩过（周一到周日 0-7）：统计区间内 playlog 记录覆盖的自然日数</summary>
    public static int GetWeekPlayDays(long startSec, long endSec) {
        var days = new HashSet<int>();
        foreach (var t in GetPlayLogTimes()) {
            if (t < startSec || t > endSec) continue;
            // 本地日期（机器时区）：用 Unix 秒换算当日 0 点
            var dt = System.DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.Date;
            days.Add(dt.Year * 1000 + dt.DayOfYear);
        }
        return days.Count;
    }

    /// <summary>本周通关的高难度谱面数（G15~U20）：遍历 entries，统计本周有通关记录且 RankOf(等级标) 在 G15(34)~U20(59) 区间的谱面数量</summary>
    public static int GetWeekHardClears(long startSec, long endSec, System.Func<string,int> rankOf) {
        int count = 0;
        try {
            foreach (var e in Entries) {
                // G15 的 RankOf = 20 + 14 = 34；U20 = 59
                int rank = rankOf != null ? rankOf(MarkStore.Get(e.FilePath)) : -1;
                if (rank < 34 || rank > 59) continue;   // 只算 G15~U20
                // 同样用 PeekClearRecords：这里只按时间戳筛"这段时间内有没有通关"，
                // 顺序完全用不上，而 GetClearRecords 要为每个候选谱面拷一份 List 再排序。
                foreach (var r in PeekClearRecords(e.FolderPath))
                    if (r.Time >= startSec && r.Time <= endSec) { count++; break; }
            }
        } catch { }
        return count;
    }

    /// <summary>读取游玩次数（按文件夹路径）</summary>
    public static int GetPlays(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return 0;
        return _plays.TryGetValue(folderPath, out int c) ? c : 0;
    }

    /// <summary>游玩次数 +1（只改内存，写盘由 FlushPlays 后台合并执行，避免死亡瞬间主线程 IO 卡顿）</summary>
    public static void AddPlay(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return;
        _plays[folderPath] = GetPlays(folderPath) + 1;
        _playsDirty = true;
        // 成就：小牢/中牢/牢大（单个谱面游玩次数）
        int pc = GetPlays(folderPath);
        try {
            if (pc >= 500) Achievements.TryUnlock("ach_500");
            if (pc >= 1000) Achievements.TryUnlock("ach_zhonglao");
            if (pc >= 10000) Achievements.TryUnlock("ach_laoda");
        } catch (System.Exception e) { Lg.Ex("成就牢判定失败", e); }
        // 成就：玩了很多次仍未通关（坚持/耐心/休息/炼成坚持蛊）
        try {
            if (!HasClearRecords(folderPath)) {   // 仍未通关
                if (pc > 100) Achievements.TryUnlock("ach_persist");
                if (pc > 500) Achievements.TryUnlock("ach_patient");
                if (pc > 1000) Achievements.TryUnlock("ach_rest");
                if (pc > 2000) Achievements.TryUnlock("ach_gu");
            }
        } catch (System.Exception e) { Lg.Ex("成就坚持判定失败", e); }
        // 累计成就：总次数/时长（Thousand/100Hours 等）
        try { Achievements.CheckProgression(); } catch (System.Exception) { }
    }

    /// <summary>把所有攒着的数据同步落盘（游戏退出前调一次；后台线程来不及跑完，脏数据不能丢）。
    /// 游玩次数平时也是后台写的，同样在这里兜底</summary>
    /// <remarks>
    /// 判据是「脏 <em>或</em> 在飞」，不是只看脏标记。
    /// 只看脏标记会漏掉这一种情况：脏标记已在提交后台任务时清掉、那条任务正在写，
    /// 此时游戏退出。若按「不脏」跳过，那次死亡就永远丢了 —— 这正是实测到的那个现象。
    /// 在飞时这里会同步再写一遍：与后台线程并发写同一个文件，最坏是内容相同，
    /// 但比丢数据好得多（后台线程写的是同一份内存快照的完整结果）。
    /// </remarks>
    public static void SaveAllNow() {
        if (_deathMapDirty || System.Threading.Volatile.Read(ref _deathMapWriting) != 0) SaveDeathMap();
        if (_progressDirty || System.Threading.Volatile.Read(ref _progressWriting) != 0) SaveProgress();
        if (_playsDirty || System.Threading.Volatile.Read(ref _playsWriting) != 0) { _playsDirty = false; SavePlays(); }
    }

    /// <summary>游玩次数后台写盘（主线程每帧调用，脏了才写；写盘在后台线程，不卡游戏）。
    /// 在飞标记的用意与 FlushDeathProgress 那段完全一样，见那里的说明：
    /// 提交后台任务时就清脏的话，任务没跑成就没人兜底，最后一次游玩次数会丢</summary>
    public static void FlushPlays() {
        if (!_playsDirty || _playsWriting != 0) return;
        var snapshot = new Dictionary<string, int>(_plays);
        string path = _playsPath;
        if (string.IsNullOrEmpty(path)) return;
        _playsWriting = 1;
        _playsDirty = false;   // 快照已带走；写盘期间的新计数会重新置脏，下一帧补写
        Main.RunBackground("SavePlays", () => {
            try {
                var lines = new List<string>();
                foreach (var kv in snapshot) lines.Add(kv.Key + "\t" + kv.Value);
                File.WriteAllLines(path, lines);
            } catch (System.Exception e) {
                Lg.Ex("保存游玩次数失败(后台)", e);
            }
            System.Threading.Interlocked.Exchange(ref _playsWriting, 0);
        });
    }

    /// <summary>加载完成度（最远死亡百分比）</summary>
    public static void LoadProgress(string modPath) {
        _progress.Clear();
        _progressPath = System.IO.Path.Combine(modPath, "progress.txt");
        if (!File.Exists(_progressPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_progressPath)) {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                string key = ToAbs(parts[0].Trim());
                if (key.Length == 0) continue;
                if (!int.TryParse(parts[1].Trim(), out int pct)) continue;
                if (pct < 0) pct = 0; if (pct > 100) pct = 100;
                _progress[key] = pct;
            }
        } catch (System.Exception e) {
            Lg.Ex("加载完成度失败", e);
        }
    }

    public static void SaveProgress() {
        if (string.IsNullOrEmpty(_progressPath)) return;
        _progressDirty = false;
        try {
            var lines = new List<string>();
            foreach (var kv in _progress) lines.Add(ToRel(kv.Key) + "\t" + kv.Value);
            File.WriteAllLines(_progressPath, lines);
        } catch (System.Exception e) {
            Lg.Ex("保存完成度失败", e);
        }
    }

    /// <summary>后台写盘（主线程每帧调用，脏了才写；写盘在后台线程，不卡游戏）。
    /// 死亡一次要改两张表，逐张同步落盘会在死亡瞬间卡主线程，所以都攒到这里合并写。
    /// 写的是快照不是活表：后台线程扫字典时主线程可能正在改，扫到一半改掉会抛异常</summary>
    /// <remarks>
    /// 这里有两个标记，各管一件事，少一个都会丢数据：
    /// <para>_deathMapDirty —— 内存里有还没落盘的计数。</para>
    /// <para>_deathMapWriting —— 此刻有一条后台写盘任务在跑（防每帧重复起线程）。</para>
    /// <para>原来的写法是在提交后台任务<em>之前</em>就把脏标记清掉，那有致命后果：
    /// 后台线程还没跑到 WriteAllLines，脏标记已经是 false 了。此时若游戏退出，
    /// SaveAllNow 看到的全是「不脏」，兜底直接跳过 —— 内存里明明改过、却永远不落盘。
    /// 实测症状：plays.txt 一直更新（RecordPlay 是立即写的，不走这里），
    /// 而 deathmap.txt / progress.txt 停在某个很久以前的时刻；且「有游玩记录但完全没寄录图」
    /// 的谱清一色只玩过一次（第一次死亡就没落盘）。
    /// <para>现在改成「在飞标记」：提交时清脏（在飞标记负责挡住重复提交），
    /// 写完才放掉在飞标记。写盘期间若主线程又置脏（又死了一次），那次置脏会保留，
    /// 下一帧再 flush 一遍，不丢。</para>
    /// </remarks>
    public static void FlushDeathProgress() {
        if (_deathMapDirty && _deathMapWriting == 0) {
            // 深拷贝必须「遍历源、写目标」——在同一个字典上边遍历边赋值会抛
            // InvalidOperationException（.NET 的字典枚举器带版本号，写入即算修改）。
            // 这个坑踩过一次：OnUpdate 每帧都抛这异常，后台线程压根没提交出去，
            // 寄录图和完成度就永远停在 00:30，而 plays.txt 因为走立即写盘所以照常更新。
            var snap = new Dictionary<string, int[]>(_deathMap.Count);
            foreach (var kv in _deathMap) snap[kv.Key] = (int[])kv.Value.Clone();
            string path = _deathMapPath;
            if (!string.IsNullOrEmpty(path)) {
                _deathMapWriting = 1;
                _deathMapDirty = false;   // 快照已带走；写盘期间的新死亡会重新置脏，下一帧补写
                Main.RunBackground("SaveDeathMap", () => {
                    try { WriteDeathMap(path, snap); }
                    catch (System.Exception e) {
                        Lg.Ex("保存死点热力图失败(后台)", e);
                    }
                    // 放在后台线程里收尾：写完（含失败）才允许下一次提交。
                    // 这里不碰 _deathMapDirty —— 写盘期间主线程置的脏必须留给下一帧。
                    System.Threading.Interlocked.Exchange(ref _deathMapWriting, 0);
                });
            }
        }
        if (_progressDirty && _progressWriting == 0) {
            var snap = new Dictionary<string, int>(_progress);
            string ppath = _progressPath;
            if (!string.IsNullOrEmpty(ppath)) {
                _progressWriting = 1;
                _progressDirty = false;
                Main.RunBackground("SaveProgress", () => {
                    try {
                        var lines = new List<string>();
                        foreach (var kv in snap) lines.Add(ToRel(kv.Key) + "\t" + kv.Value);
                        File.WriteAllLines(ppath, lines);
                    } catch (System.Exception e) {
                        Lg.Ex("保存完成度失败(后台)", e);
                    }
                    System.Threading.Interlocked.Exchange(ref _progressWriting, 0);
                });
            }
        }
    }

    /// <summary>通关无检查点 → 完成度 100%（高覆盖低；写盘）</summary>
    public static void SetCompletion100(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return;
        string key = ToAbs(folderPath);
        if (!_progress.TryGetValue(key, out int old) || old < 100) {
            _progress[key] = 100;
            SaveProgress();
        }
    }

    /// <summary>完成度：通关无检查点=100%；否则精确死亡位置（每 1 分）；都没记录=0%（不用老段估算）</summary>
    public static int GetCompletion(string folderPath) {
        try {
            string key = ToAbs(folderPath);
            // 通关且无检查点 → 100%（最高，覆盖一切）
            if (_records.TryGetValue(key, out var recs)) {
                foreach (var r in recs) if (r.Checkpoints <= 0) return 100;
            }
            // 精确死亡位置（0-100 每 1 一档）；没记录就 0%（不用死亡段估算）
            if (_progress.TryGetValue(key, out int pct)) return pct;
            return 0;
        } catch { }
        return 0;
    }

    public static void LoadRecords(string modPath) {
        _records.Clear();
        _recordsPath = System.IO.Path.Combine(modPath, "records.txt");
        if (!File.Exists(_recordsPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_recordsPath)) {
                var parts = line.Split('\t');
                if (parts.Length < 8) continue;
                string key = ToAbs(parts[0].Trim());
                if (key.Length == 0) continue;
                var r = new ClearRecord();
                if (!long.TryParse(parts[1].Trim(), out r.Time)) continue;
                if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r.Xacc)) continue;
                int.TryParse(parts[3].Trim(), out r.Early);
                int.TryParse(parts[4].Trim(), out r.TooFast);
                int.TryParse(parts[5].Trim(), out r.Fast);
                int.TryParse(parts[6].Trim(), out r.Perfect);
                int.TryParse(parts[7].Trim(), out r.Slow);
                if (parts.Length >= 9) int.TryParse(parts[8].Trim(), out r.TooSlow);
                if (parts.Length >= 10) int.TryParse(parts[9].Trim(), out r.Slot);
                if (parts.Length >= 11) int.TryParse(parts[10].Trim(), out r.Checkpoints);
                // 旧记录没有这两列 → 保持 false（不追溯作废玩家以前拿到的无瑕）
                if (parts.Length >= 12) r.Autoplay = parts[11].Trim() == "1";
                if (parts.Length >= 13) r.NoFail = parts[12].Trim() == "1";
                // 老记录没这两列 → Pitch=0（界面不显示倍速）、Auto=0（校验时按 0 处理）
                if (parts.Length >= 14) int.TryParse(parts[13].Trim(), out r.Pitch);
                if (parts.Length >= 15) int.TryParse(parts[14].Trim(), out r.Auto);
                if (!_records.TryGetValue(key, out var list)) { list = new List<ClearRecord>(); _records[key] = list; }
                list.Add(r);
            }
        } catch (System.Exception e) {
            Lg.Ex("加载通关记录失败", e);
        }
    }

    // ---------- 旧记录清理 ----------
    // 空敲（没有砖块时按下的那一下）记进"太早/很早"桶、不死人 —— 六个判定桶之和比判定数多出来的正是空敲，
    // 属于正常通关，绝不能按"多"删。只认四类没有争议的：开过自动播放 / 开过不会失败 / 用了检查点 / xacc ≤ 0。
    // 判定数"少一点点"同样可能是空敲或自动格口径差异，一律放行；只有少得离谱（≥ JudgeGapSuspect）才挑出来给人看。

    /// <summary>判定数差到这个数才算"离谱到值得看一眼"（空敲造成的偏差远小于它）</summary>
    private const int JudgeGapSuspect = 100;
    /// <summary>判定数缓存，按文件夹；读不出来记 -1</summary>
    private static readonly Dictionary<string, int> _judgeCache = new Dictionary<string, int>();

    /// <summary>数一个谱面文件夹的判定数（= 段数）。读不出来返回 -1，调用方据此放行。</summary>
    private static int JudgeCountOf(string absFolder) {
        if (string.IsNullOrEmpty(absFolder) || !Directory.Exists(absFolder)) return -1;
        if (_judgeCache.TryGetValue(absFolder, out int cached)) return cached;
        int result = -1;
        try {
            string file = null;
            string main = Path.Combine(absFolder, "main.adofai");
            if (File.Exists(main)) file = main;
            else {
                var cand = Directory.GetFiles(absFolder, "*.adofai");
                if (cand != null && cand.Length > 0) file = cand[0];
            }
            if (file != null) {
                // 只数 angleData / pathData 数组里的元素个数（不整份解析 JSON：有些谱面上百 MB）
                string text = File.ReadAllText(file);
                int segs = CountJsonArrayItems(text, "\"angleData\"");
                if (segs < 0) segs = CountJsonArrayItems(text, "\"pathData\"");
                if (segs > 0) result = segs;   // 判定数 = 段数；格子数 = 段数 + 1
            }
        } catch { result = -1; }
        _judgeCache[absFolder] = result;
        return result;
    }

    /// <summary>数 "key": [ ... ] 里的元素个数（数逗号即可，元素是数字还是 "R"/"L" 都一样）；
    /// 找不到这个 key 返回 -1、数组为空返回 0。
    /// 只统计当前这一层的逗号：元素里可能还有嵌套数组，把内层逗号算进来就会多数。</summary>
    private static int CountJsonArrayItems(string text, string key) {
        int i = text.IndexOf(key, System.StringComparison.Ordinal);
        if (i < 0) return -1;
        int lb = text.IndexOf('[', i);
        if (lb < 0) return -1;
        bool any = false, closed = false;
        int depth = 0, n = 0;
        for (int k = lb; k < text.Length; k++) {
            char c = text[k];
            if (c == '[' || c == '{') depth++;
            else if (c == ']' || c == '}') { depth--; if (depth <= 0) { closed = true; break; } }
            else if (c == ',' && depth == 1) n++;
            else if (depth == 1 && !char.IsWhiteSpace(c)) any = true;
        }
        if (!closed) return -1;                       // 扫到文件尾都没闭合 → 当成读不出来
        return any ? n + 1 : 0;
    }

    /// <summary>一条记录为什么"建议清理"（null = 留着）；只认四类无争议的，空敲不算。</summary>
    private static string RecordRejectReason(string key, ClearRecord r) {
        if (r == null) return null;
        if (r.Autoplay) return "开过自动播放";
        if (r.NoFail) return "开过不会失败";
        if (r.Checkpoints > 0) return "用了检查点 " + r.Checkpoints;
        if (r.Xacc <= 0f) return "xacc=0（空壳数据）";
        return null;
    }

    /// <summary>只读：数有多少条建议清理，给确认框报数用；不修改任何数据。</summary>
    public static int CountRejectableRecords(out int keep) {
        keep = 0;
        int bad = 0;
        try {
            foreach (var kv in _records) {
                if (kv.Value == null) continue;
                foreach (var r in kv.Value) {
                    if (RecordRejectReason(kv.Key, r) == null) keep++; else bad++;
                }
            }
        } catch { }
        return bad;
    }

    /// <summary>只读：数有多少条"判定数少得离谱"（只报数，不删）。</summary>
    public static int CountSuspectGapRecords() {
        int n = 0;
        try {
            foreach (var kv in _records) {
                if (kv.Value == null) continue;
                int need = JudgeCountOf(kv.Key);
                if (need < 0) continue;
                foreach (var r in kv.Value) {
                    int got = r.Early + r.TooFast + r.Fast + r.Perfect + r.Slow + r.TooSlow + r.Auto;
                    if (need - got >= JudgeGapSuspect) n++;
                }
            }
        } catch { }
        return n;
    }

    /// <summary>清掉不符合现行规范的旧通关记录：只由设置页那个按钮调，绝不在启动流程里跑。
    /// 删前先备份 records.txt，备份失败就中止；返回删掉的条数（-1 = 没做 / 出错）。</summary>
    public static int PruneLegacyClearRecords() {
        try {
            if (string.IsNullOrEmpty(_recordsPath) || !File.Exists(_recordsPath)) {
                Lg.Info("旧记录清理：没有 records.txt，什么都没做");
                return -1;
            }
            // 先备份：没有备份不许删
            string bak = null;
            try {
                bak = Path.Combine(Path.GetDirectoryName(_recordsPath) ?? ".",
                    "records_backup_" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.Copy(_recordsPath, bak, true);
            } catch (System.Exception be) {
                Lg.Info("旧记录清理中止：备份失败（" + be.GetType().Name + " " + be.Message + "）—— 没有备份不许删");
                return -1;
            }

            int removed = 0, kept = 0;
            var reasons = new Dictionary<string, int>();
            var keys = new List<string>(_records.Keys);
            foreach (var key in keys) {
                if (!_records.TryGetValue(key, out var list) || list == null) continue;
                var keepList = new List<ClearRecord>();
                foreach (var r in list) {
                    string why = RecordRejectReason(key, r);
                    if (why == null) { kept++; keepList.Add(r); }
                    else {
                        removed++;
                        if (!reasons.ContainsKey(why)) reasons[why] = 0;
                        reasons[why]++;
                    }
                }
                if (keepList.Count == 0) _records.Remove(key);
                else _records[key] = keepList;
            }
            if (removed > 0) SaveRecords();
            var sb = new System.Text.StringBuilder($"旧通关记录清理（手动触发）：删掉 {removed} 条、保留 {kept} 条"
                + $"；备份=" + Path.GetFileName(bak ?? "(无)"));
            foreach (var kv in reasons) sb.Append("\n   · ").Append(kv.Key).Append(" → ").Append(kv.Value).Append(" 条");
            sb.Append("\n    空敲（太早/很早 多出来的那几个）不算脏 一律保留");
            Lg.Info(sb.ToString());
            return removed;
        } catch (System.Exception e) {
            Lg.Info("清理旧通关记录失败: " + e.GetType().Name + " " + e.Message);
            return -1;
        }
    }

    /// <summary>清理逻辑专用日志出口：任何情况下都不许把异常抛出去（记录与返回值的正确性优先于日志）。</summary>

    // ---------- 旧记录清理 结束 ----------

    public static void SaveRecords() {
        if (string.IsNullOrEmpty(_recordsPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _records) {
                foreach (var r in kv.Value) {
                    lines.Add(ToRel(kv.Key) + "\t" + r.Time + "\t" + r.Xacc.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                        + "\t" + r.Early + "\t" + r.TooFast + "\t" + r.Fast + "\t" + r.Perfect + "\t" + r.Slow + "\t" + r.TooSlow + "\t" + r.Slot + "\t" + r.Checkpoints
                        + "\t" + (r.Autoplay ? "1" : "0") + "\t" + (r.NoFail ? "1" : "0")
                        + "\t" + r.Pitch + "\t" + r.Auto);
                }
            }
            File.WriteAllLines(_recordsPath, lines);
        } catch (System.Exception e) {
            Lg.Ex("保存通关记录失败", e);
        }
    }

    /// <summary>加一条通关记录（立即写盘；同一次通关只记一条）。
    /// autoplay/noFail 存下来供"无瑕"这类要求真实成绩的成就判定，不能只靠写入端拦（RecordRestrictions 可关）。</summary>
    public static void AddClearRecord(string folderPath, float xacc, int early, int tooFast, int fast, int perfect, int slow, int tooSlow, int slot, int checkpoints,
                                      bool autoplay = false, bool noFail = false, int pitch = 0, int auto = 0) {
        if (string.IsNullOrEmpty(folderPath) || xacc <= 0f) return;
        string key = ToAbs(folderPath);
        if (!_records.TryGetValue(key, out var list)) { list = new List<ClearRecord>(); _records[key] = list; }
        list.Add(new ClearRecord {
            Xacc = xacc, Early = early, TooFast = tooFast, Fast = fast,
            Perfect = perfect, Slow = slow, TooSlow = tooSlow, Slot = slot, Checkpoints = checkpoints,
            Autoplay = autoplay, NoFail = noFail, Pitch = pitch, Auto = auto,
            Time = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        SaveRecords();
        // 成就：通关可能触发 P10+（严判）等扫描类成就
        try { Achievements.CheckVillageMark(); } catch (System.Exception e) { Lg.Ex("通关后成就扫描失败", e); }
        // 每日成就：今日打谱大学习——每日从第一格（未用检查点）通关第一个谱子时触发（每天一次）
        if (checkpoints == 0) {
            try { Achievements.TriggerDaily("ach_daily_study", "今日打谱大学习"); } catch (System.Exception e) { Lg.Ex("每日成就触发失败", e); }
        }
    }

    /// <summary>删除一条通关记录（按时间戳匹配；立即写盘）</summary>
    public static void RemoveClearRecord(string folderPath, long time) {
        if (string.IsNullOrEmpty(folderPath)) return;
        string key = ToAbs(folderPath);
        if (!_records.TryGetValue(key, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--) {
            if (list[i].Time == time) { list.RemoveAt(i); break; }
        }
        SaveRecords();
    }

    /// <summary>清空某个谱面的通关记录（详情区"数据管理"用；只影响这一个谱面，立即写盘）</summary>
    public static void ClearClearRecords(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return;
        string key = ToAbs(folderPath);
        if (!_records.Remove(key)) return;
        SaveRecords();
    }

    /// <summary>清空某个谱面的游玩次数（只影响这一个谱面；立即写盘）</summary>
    public static void ClearPlaysOf(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return;
        string key = ToAbs(folderPath);
        if (!_plays.Remove(key)) return;
        _playsDirty = false;
        SavePlays();
    }

    /// <summary>取谱面通关记录（xacc 降序，谁高谁在前；无数据返回空列表）</summary>
    public static List<ClearRecord> GetClearRecords(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return new List<ClearRecord>();
        if (!_records.TryGetValue(ToAbs(folderPath), out var list)) return new List<ClearRecord>();
        var sorted = new List<ClearRecord>(list);
        sorted.Sort((a, b) => b.Xacc.CompareTo(a.Xacc));
        return sorted;
    }

    /// <summary>这个谱面有没有通关记录（只判有无）。不拷贝、不排序、不分配 ——
    /// 只想问一句"通没通关"时别用 GetClearRecords(...).Count == 0，那条路会为每个 bool
    /// 付一次整表拷贝 + 排序，而它在每次死亡 / 每次进编辑器 / 每次通关都会走到。</summary>
    public static bool HasClearRecords(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return false;
        // 必须用 TryGetValue，不能写 _records[key]：字典索引器在键不存在时抛
        // KeyNotFoundException（不是返回 null）。而"没通关过的谱面"恰好就是键不存在，
        // 这是本方法最常见的入口 —— 用索引器等于每次死亡都对没打过的新谱面炸一次。
        if (!_records.TryGetValue(ToAbs(folderPath), out var list)) return false;
        return list != null && list.Count > 0;
    }

    /// <summary>只读遍历这个谱面的通关记录，不拷贝、不排序（顺序 = 落盘顺序）。
    /// 要按 xacc 降序展示才用 GetClearRecords；像"里面有没有严判记录"这种扫描用这个，
    /// 省掉每个谱面一次的整表拷贝 + 委托排序。</summary>
    public static List<ClearRecord> PeekClearRecords(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return EmptyRecords;
        if (!_records.TryGetValue(ToAbs(folderPath), out var list) || list == null || list.Count == 0) return EmptyRecords;
        return list;
    }

    /// <summary>共享的空列表：给"只读遍历"兜底用，省得每次 new 一个空 List。</summary>
    private static readonly List<ClearRecord> EmptyRecords = new List<ClearRecord>();

}
