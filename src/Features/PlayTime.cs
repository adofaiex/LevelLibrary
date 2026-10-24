// 今日游玩时长：自己按帧累计，不依赖 playlog / LevelScanner 的统计（那套要今天有 playlog 记录才有值，打着的时候永远是 0:00）。
// 落盘 `Mods\LevelLibrary\playtime.txt`，一行一天：`yyyy-MM-dd <TAB> 秒数`，只留最近 KeepDays 天；跨天自动翻页。
// 累计满 SaveEverySeconds 秒、或每 SaveEveryWallSeconds 秒兜一次、以及离开游玩场景时各冲一次盘（崩溃最多丢 10 秒）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LevelLibrary;

public static class PlayTime {

    private const string FileName = "playtime.txt";
    private const int KeepDays = 30;
    private const double SaveEverySeconds = 10.0;    // 累计到 10 秒就落一次盘
    private const double SaveEveryWallSeconds = 30.0; // 兜底：每 30 秒也落一次（防止一直不满 10 秒）
    private const double MaxFrameSeconds = 1.0;      // 单帧最多算 1 秒（异常大跳的兜底，正常远低于它）

    /// <summary>历史各天的秒数（键 = `yyyy-MM-dd`）</summary>
    private static readonly Dictionary<string, long> _days = new Dictionary<string, long>();
    /// <summary>本次进程里还没落盘的那部分（属于 <see cref="_liveDay"/> 这一天）</summary>
    private static double _live;
    private static string _liveDay;
    private static bool _loaded;
    private static double _sinceSave;        // 距上次落盘累计了多少秒
    private static float _sinceSaveWall;     // 距上次落盘过了多少真实时间

    /// <summary>本进程里 `AddSeconds` 累计过的秒数，只增不减。
    /// 个人信息栏那行"游玩时长"是「Steam 基准 + 我们自己的实时增量」，而增量原先只取
    /// `LevelScanner.GetTotalPlayTimeLive()` —— 那个值要有"正在进行的游玩记录"才往前走：
    /// `_lastPlayUnixSec` 只在写记录时赋值、从不从盘上读回来，重启后直到第一次死亡或通关之前它一直是 0，
    /// 于是 `GetTotalPlayTimeLive()` 退化成 playlog 那个静态值，增量恒为 0，秒针就冻住了
    /// （基准多半是 60 的整倍数，所以看起来永远停在 00）。
    /// 这个计数器不谈统计口径，只回答"我们这个进程又过了多少秒"，把它当增量的下限就一定能跳秒。</summary>
    private static double _ownMono;
    /// <summary>取基准那一刻 `_ownMono` 的值（增量从 0 起算）</summary>
    private static double _ownMonoAtSync;

    private static string FilePath {
        get {
            try {
                if (Main.ModEntry == null || string.IsNullOrEmpty(Main.ModEntry.Path)) return null;
                return Path.Combine(Main.ModEntry.Path, FileName);
            } catch { return null; }
        }
    }

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>懒加载：第一次用的时候读一次盘；路径拿不到（测试环境）就只走内存。</summary>
    private static void EnsureLoaded() {
        if (_loaded) return;
        _loaded = true;
        _liveDay = Today;
        string p = FilePath;
        if (!string.IsNullOrEmpty(p) && File.Exists(p)) {
            try {
                foreach (var raw in File.ReadAllLines(p)) {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    string day = parts[0].Trim();
                    if (day.Length == 0) continue;
                    if (long.TryParse(parts[1].Trim(), out long sec) && sec >= 0) _days[day] = sec;
                }
            } catch (Exception e) { Lg.Ex("读游玩时长失败", e); }
        }
        // 读盘之后把今天已经越过的播报档一次性静默标成已播：否则重启游戏时会补播 60 分钟那句，
        // 而口径是"到 60 分钟那一刻说"，不是"任何时候发现超过 60 分钟都说"。
        long today = 0;
        _days.TryGetValue(Today, out today);
        SyncMilestonesQuiet(today);
        // 启动留一行日志（一次启动一行），便于区分"还没到点"和"已越过、按设计静默标掉了"。
        try {
            Lg.Info("今日游玩时长：读盘 = " + today + " 秒（" + Format(today) + "）｜"
                + "播报档 已越过的＝" + PassedText(today) + " ->  这些本次进入不会再补播");
        } catch { }
    }

    /// <summary>读盘那一刻已经越过的播报档（60/300/600 分钟，一个都没有就写"无"）</summary>
    private static string PassedText(long sec) {
        var sb = new System.Text.StringBuilder();
        if (sec >= SayMinute60) sb.Append("60 ");
        if (sec >= SayMinute300) sb.Append("300 ");
        if (sec >= SayMinute600) sb.Append("600 ");
        return sb.Length > 0 ? (sb.ToString().Trim() + " 分钟") : "无";
    }

    /// <summary>加一段游玩时长。纯逻辑入口（单元测试直接调它，不碰 Unity）。</summary>
    public static void AddSeconds(double sec) {
        EnsureLoaded();
        if (sec <= 0) return;
        RolloverIfNeeded();
        _live += sec;
        _sinceSave += sec;
        _ownMono += sec;      // 只增不减的实时计数器（理由见字段处那段说明）
    }

    /// <summary>跨天：先把昨天那份冲盘，再把"今天"清成 0（`_live` 只属于一天）。
    /// 播报标记也要一起归零——当日游玩时长每天重算，不清的话第二天就不会再播了。</summary>
    private static void RolloverIfNeeded() {
        string today = Today;
        if (_liveDay == today) return;
        Flush();                 // 先把昨天那份写下去
        _live = 0;
        _liveDay = today;
        ResetMilestones();
    }

    /// <summary>这一刻的今日累计秒数（含还没落盘的那部分，界面拿它就能实时刷）。</summary>
    public static long TodaySeconds() {
        EnsureLoaded();
        long baseSec = 0;
        _days.TryGetValue(Today, out baseSec);
        return baseSec + (long)_live;
    }

    /// <summary>今日时长的显示文本：分:秒（超过 60 分直接显示 61:00 这种，不换算成小时）。</summary>
    public static string TodayText() { return Format(TodaySeconds()); }

    /// <summary>秒 → `分:秒`，分钟不封顶（例如 3661 秒 = `61:01`）。</summary>
    public static string Format(long sec) {
        if (sec < 0) sec = 0;
        return (sec / 60) + ":" + (sec % 60).ToString("00");
    }

    /// <summary>秒 → `小时:分:秒`（小时不补零、分与秒各两位；例如 517 小时 21 分 5 秒 = `517:21:05`）。</summary>
    public static string FormatHms(long sec) {
        if (sec < 0) sec = 0;
        return (sec / 3600) + ":" + ((sec / 60) % 60).ToString("00") + ":" + (sec % 60).ToString("00");
    }

    // ---------- 历史总时长的口径（个人信息栏与周报共用这一个）----------
    // Steam 的账本只在退出游戏时才写盘，所以游戏跑着的时候反复读永远拿到同一个数 ——
    // 光读它的话个人信息栏会一直定格在启动那一刻那一秒。定稿：启动后第一次用到时把 Steam 那个数读一次当基准，
    // 之后长出来的部分由我们自己的实时统计补上。Steam 读不到（非 Steam 版 / 没装 / 权限）就整个用我们自己的。
    private static bool _totalSynced;
    private static long _totalBaseSec;      // 基准秒数
    private static long _totalOwnBaseSec;   // 取基准那一刻我们自己的统计值

    /// <summary>历史游玩总时长（秒）：Steam 基准 + 我们自己的实时增量。</summary>
    public static long TotalSeconds() {
        EnsureLoaded();
        long own = 0;
        try { own = LevelScanner.GetTotalPlayTimeLive(); } catch { }
        if (!_totalSynced) {
            long steam = 0;
            try { steam = SteamPlaytime.TotalSeconds; } catch { }
            // 开机那次 Load 还没跑完就先别锁基准：那会儿读到的一定是 0，一旦锁了、这个会话就再也拿不到 Steam 那个数
            // （正常顺序是先 Load 再有人问，这条只是防"以后有人把它挪到开机第一帧"）。
            bool known = steam > 0;
            if (!known) { try { known = SteamPlaytime.Loaded; } catch { } }
            if (!known) { _totalBaseSec = own; _totalOwnBaseSec = own; _ownMonoAtSync = _ownMono; return own; }
            _totalSynced = true;
            _totalBaseSec = steam > 0 ? steam : own;
            _totalOwnBaseSec = own;
            _ownMonoAtSync = _ownMono;
            try {
                Lg.Info($"[LevelLib] 游玩总时长基准：Steam={(steam > 0 ? (steam / 3600) + " 小时" : "读不到")}"
                    + $" 我们的统计={own} 秒 ->  显示基准={_totalBaseSec} 秒（之后按我们自己的实时统计往上加）");
            } catch { }
        }
        // 增量取两条路的较大值：
        // ① playlog 那条（`own − 基准`）在死亡/通关时跳一下，是"正式统计"；
        // ② `_ownMono` 那条每秒都涨，保证秒针一定在跳。
        // 取大的：正式统计领先时用它，统计暂时停住时也不会把显示定格。
        long dStats = own - _totalOwnBaseSec;
        long dMono = (long)(_ownMono - _ownMonoAtSync);
        long delta = dStats > dMono ? dStats : dMono;
        if (delta < 0) delta = 0;
        return _totalBaseSec + delta;
    }

    /// <summary>历史游玩总时长的显示文本（`小时:分:秒`）</summary>
    public static string TotalText() { return FormatHms(TotalSeconds()); }

    /// <summary>每帧调用（挂常驻段：面板关着、暂停中、在编辑器里都要算）。
    /// 口径是"无论任何情况，都记录时间"——不要加场景/暂停条件：编辑器里 `paused` 是 true、面板里 timeScale 是 0，
    /// 加条件会出现文本不跳秒。</summary>
    public static void Tick() {
        EnsureLoaded();
        double dt = Time.unscaledDeltaTime;          // 真实时间（不受 timeScale=0 影响，暂停时照样走）
        if (dt < 0) return;
        if (dt > MaxFrameSeconds) dt = MaxFrameSeconds;   // 单帧兜底 1 秒（防异常大跳，正常不会触发）
        AddSeconds(dt);
        CheckMilestones();                                // 到点就让大肥鱼说话
        _sinceSaveWall += (float)dt;
        if (_sinceSave >= SaveEverySeconds || _sinceSaveWall >= (float)SaveEveryWallSeconds) Flush();
    }

    // ---------- 当日游玩时长到点 → 大肥鱼立刻说一句 ----------
    // 台词一字不改（= 实际要说的三句）：
    // 60/300/600 分钟：「今天已经玩了60分钟诶，老大我们再这么玩下去不会累死吧」/「…300分钟…不会不太好…………」/「老大我要累死了，10个小时了……咱们休息一会吧」
    // 判据用 `>=` 而不是 `==`：单帧最多只算 MaxFrameSeconds 秒且卡顿后 deltaTime 被夹住，累计值可能跳过整点线，用 `==` 会永远错过。
    // 每条一天只播一次（标记在内存里）：读盘时把今天已越过的档静默标记，跨天时三条全部归零。
    // 播报交给 `PortalUI.PetSayLine`（固定台词通道，不进随机池）；这里不判"是不是在打谱"，到点就说。
    private const long SayMinute60 = 60L * 60L;      // 3600 秒
    private const long SayMinute300 = 300L * 60L;    // 18000 秒
    private const long SayMinute600 = 600L * 60L;    // 36000 秒（10 小时）

    private const string SayText60 = "今天已经玩了60分钟诶，老大我们再这么玩下去不会累死吧";
    private const string SayText300 = "今天已经玩了300分钟诶，老大我们再这么玩下去不会不太好…………";
    private const string SayText600 = "老大我要累死了，10个小时了……咱们休息一会吧";

    private static bool _said60, _said300, _said600;

    /// <summary>把当前秒数已经越过的档静默标成已播（不出声，读盘那一刻用，见 `EnsureLoaded`）。</summary>
    private static void SyncMilestonesQuiet(long sec) {
        if (sec >= SayMinute60) _said60 = true;
        if (sec >= SayMinute300) _said300 = true;
        if (sec >= SayMinute600) _said600 = true;
    }

    /// <summary>跨天：三条标记归零（新的一天重新到点）。模型包定时台词同一天口径，跟着归零。</summary>
    private static void ResetMilestones() { _said60 = false; _said300 = false; _said600 = false; _petTimedVer = -1; }

    /// <summary>每帧检查（在 `Tick` 里、累加之后）：到点就说，每条一天一次。</summary>
    private static void CheckMilestones() {
        if (_said60 && _said300 && _said600) { CheckPetTimedSays(); return; }   // 三条都说过 一次判断就够（平时零开销）
        long sec = TodaySeconds();
        if (!_said60 && sec >= SayMinute60) { _said60 = true; PortalUI.PetSayLine(SayText60, "今日游玩 60 分钟"); }
        if (!_said300 && sec >= SayMinute300) { _said300 = true; PortalUI.PetSayLine(SayText300, "今日游玩 300 分钟"); }
        if (!_said600 && sec >= SayMinute600) { _said600 = true; PortalUI.PetSayLine(SayText600, "今日游玩 600 分钟"); }
        CheckPetTimedSays();
    }

    // ---------- 模型包自带的定时台词（编辑器加的：at<分钟>=台词）----------
    // 判据与上面三条内建的同一套：今日游玩秒数、`>=`、每条一天一次。挂在当前生效的模型上 ——
    // 模型表重载 / 换模型都会换实例，TimedVersion 一变就重建标记并把今天已经越过的档静默标掉（不出声）。
    private static int _petTimedVer = -1;
    private static int[] _petTimedMins;
    private static bool[] _petTimedSaid;

    private static void CheckPetTimedSays() {
        try {
            var pm = PortalUI.PetActiveModel();
            if (pm == null || pm.TimedMins == null || pm.TimedMins.Length == 0) { _petTimedVer = -1; return; }
            if (_petTimedVer != pm.TimedVersion) {
                _petTimedVer = pm.TimedVersion;
                _petTimedMins = pm.TimedMins;
                _petTimedSaid = new bool[pm.TimedMins.Length];
                long sec = TodaySeconds();
                for (int i = 0; i < _petTimedMins.Length; i++)
                    if (sec >= (long)_petTimedMins[i] * 60L) _petTimedSaid[i] = true;   // 开机就过了的档静默标掉
            }
            long now = TodaySeconds();
            for (int i = 0; i < _petTimedMins.Length; i++) {
                if (_petTimedSaid[i] || now < (long)_petTimedMins[i] * 60L) continue;
                _petTimedSaid[i] = true;
                PortalUI.PetSayLine(pm.TimedTexts[i], Lang.TrFmt("今日游玩 {0} 分钟", _petTimedMins[i]));
            }
        } catch { }
    }

    /// <summary>把内存里的累计写进 `playtime.txt`（写失败只记日志，玩家数据不能因此崩）。
    /// 拼 `_days` 的字符串留在主线程（`_days` 只有主线程碰），真正的写盘交给常驻线程，见 `Enqueue`。</summary>
    public static void Flush() {
        EnsureLoaded();
        _sinceSave = 0;
        _sinceSaveWall = 0f;
        string p = FilePath;
        if (string.IsNullOrEmpty(p)) return;      // 没有 mod 目录（测试环境）→ 只留在内存里
        string[] payload = null;
        try {
            if (_live > 0) {
                long cur = 0;
                _days.TryGetValue(_liveDay ?? Today, out cur);
                _days[_liveDay ?? Today] = cur + (long)_live;
                _live = 0;
            }
            // 只留最近 KeepDays 天：排序后砍掉最老的
            var keys = new List<string>(_days.Keys);
            keys.Sort(StringComparer.Ordinal);     // yyyy-MM-dd 的字典序 == 时间序
            while (keys.Count > KeepDays) { _days.Remove(keys[0]); keys.RemoveAt(0); }
            var lines = new List<string>();
            foreach (var day in keys) lines.Add(day + "\t" + _days[day].ToString(CultureInfo.InvariantCulture));
            payload = lines.ToArray();
        } catch (Exception e) { Lg.Ex("写游玩时长失败", e); }
        if (payload == null) return;
        var data = payload;      // 快照给后台线程（它不碰任何 Unity 对象 / 不碰 _days）
        string path = p;
        // 常驻线程（不是每次落盘新建一条：这个进程里起线程要几百毫秒，每 10~30 秒来一次就是可见卡顿）。
        Enqueue(data, path);
    }

    // ---------- 常驻写盘线程（只做文件 I/O，绝不碰 Unity API / 不碰 _days）----------
    private static readonly object _saveLock = new object();
    private static string[] _pendingData;
    private static string _pendingPath;
    private static System.Threading.Thread _saverThread;

    /// <summary>预热时就把写盘线程建出来：第一次落盘只剩"递数据"，不会卡在第一次 `new Thread` + `Start` 上。</summary>
    public static void WarmSaver() {
        try { if (_saverThread == null) StartSaverThread(); } catch { }
    }

    private static void Enqueue(string[] data, string path) {
        try {
            lock (_saveLock) {
                _pendingData = data;        // 只留最新一份（旧的还没写就被新的顶掉 = 反正都是全量快照）
                _pendingPath = path;
                System.Threading.Monitor.Pulse(_saveLock);
            }
            if (_saverThread == null) StartSaverThread();
        } catch (Exception e) { Lg.Ex("递游玩时长给写盘线程失败", e); }
    }

    private static void StartSaverThread() {
        _saverThread = new System.Threading.Thread(() => {
            while (true) {
                string[] data; string path;
                try {
                    lock (_saveLock) {
                        while (_pendingData == null) System.Threading.Monitor.Wait(_saveLock);
                        data = _pendingData; path = _pendingPath;
                        _pendingData = null; _pendingPath = null;
                    }
                } catch (System.Threading.ThreadAbortException) { return; }   // 退出：静默
                catch { continue; }
                try { File.WriteAllLines(path, data); }
                catch (System.Threading.ThreadAbortException) { return; }
                catch (Exception e) { Lg.Ex("写游玩时长失败", e); }
            }
        });
        _saverThread.IsBackground = true;
        _saverThread.Name = "LevelLib_PlayTimeSave";
        _saverThread.Priority = System.Threading.ThreadPriority.BelowNormal;
        _saverThread.Start();
    }
}
