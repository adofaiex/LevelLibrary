using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LevelLibrary;

/// <summary>一个谱面条目：曲名 + 原始 ogg 名 + 文件夹 + 主 .adofai 文件</summary>
public class LevelEntry {
    public string Title;        // 曲名（可能被显示名覆盖）
    public string OggName;      // 原始 ogg 名（重命名不影响，自动录入去重用）
    public string FolderPath;   // 谱面文件夹
    public string FilePath;     // 主 .adofai 文件
}

/// <summary>谱面库：全由玩家导入（手动添加 / 批量导入 / 自动录入），不自动扫描任何目录</summary>
public static partial class LevelScanner {
    /// <summary>备份根目录（列表谱面全部放这里，打开也走副本）</summary>
    public static string BackupBase;

    /// <summary>自定义备份位置（settings.txt 持久化；空 = 默认 mod\backup）</summary>
    public static string CustomBackupPath;

    /// <summary>mod 根目录（数据文件相对路径换算用，换电脑也能通用）</summary>
    public static string ModRoot;

    /// <summary>自动录入的谱面记录文件（recorded.txt，重启后恢复列表）</summary>
    public static string RecordedPath;

    /// <summary>自动录入的备份目录列表（持久化）</summary>
    public static readonly List<string> RecordedFolders = new();

    /// <summary>「刚导入」的谱面列表文件（newmarks.txt）：导入成功时记一笔，列表里给一个提示色。
    /// 与 recorded.txt 同一套做法（内存存绝对路径、落盘转相对、每个写入函数立即写盘）</summary>
    public static string NewMarksPath;
    /// <summary>尚未被玩家点开过的「新导入」谱面目录（持久化）。判据用文件夹路径，跟选中态一致</summary>
    public static readonly List<string> NewImportFolders = new();

    /// <summary>游玩次数记录（plays.txt：文件夹路径\t次数）</summary>
    private static readonly Dictionary<string, int> _plays = new();
    private static string _playsPath;
    private static bool _playsDirty;   // 有未落盘的计数
    /// <summary>游玩次数此刻有一条后台写盘任务在跑（1=在跑），语义同 _deathMapWriting</summary>
    private static int _playsWriting;

    /// <summary>游玩日志（playlog.txt：文件夹路径\tUnix时间戳，每行一次游玩；用于周报"本周玩得最多"统计）</summary>
    private static string _playLogPath;

    /// <summary>一次读入 playlog，返回所有 (时间戳)（按时间升序）。带静态缓存 + RecordPlay 失效，避免重复读盘。</summary>
    private static List<long> _playLogCache;
    private static bool _playLogCacheValid;

    /// <summary>累计登录天数（login_days.txt 记录的自然日数）</summary>
    private static HashSet<string> _loginDays = new HashSet<string>();
    private static string _loginPath;

    /// <summary>收藏（favorites.txt：文件夹路径，收藏的谱面置顶）</summary>
    private static readonly HashSet<string> _favorites = new();
    private static string _favoritesPath;

    /// <summary>X 精准度记录（xacc.txt：文件夹路径\t宽判\t标判\t严判\t纯判定，各自只升不降；纯判定=只有完美/稍快/稍慢）</summary>
    private static readonly Dictionary<string, float[]> _xaccs = new();
    private static string _xaccPath;

    public static readonly List<LevelEntry> Entries = new();

    /// <summary>显示名覆盖表：FilePath -> 用户重命名的显示名（重命名只改 mod 内名字，不动文件夹）</summary>
    private static readonly Dictionary<string, string> _titleOverrides = new();
    private static string _titlePath;

    /// <summary>加载游玩次数（plays.txt）</summary>
    public static void LoadPlays(string modPath) {
        _plays.Clear();
        _playsPath = System.IO.Path.Combine(modPath, "plays.txt");
        _playLogPath = System.IO.Path.Combine(modPath, "playlog.txt");   // 周报游玩日志
        _playLogCacheValid = false;
        _playLogCache = null;
        if (!File.Exists(_playsPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_playsPath)) {
                int idx = line.IndexOf('\t');
                if (idx <= 0) continue;
                if (int.TryParse(line.Substring(idx + 1).Trim(), out int c)) _plays[ToAbs(line.Substring(0, idx))] = c;
            }
        } catch (System.Exception e) {
            Lg.Ex("加载游玩次数失败", e);
        }
    }

    /// <summary>保存游玩次数（立即写盘，防强杀丢数据）</summary>
    public static void SavePlays() {
        if (string.IsNullOrEmpty(_playsPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _plays) lines.Add(ToRel(kv.Key) + "\t" + kv.Value);
            File.WriteAllLines(_playsPath, lines);
        } catch (System.Exception e) {
            PortalUI.ReportErrorQuiet("保存游玩次数失败", e);
        }
    }

    /// <summary>备注（notes.txt：文件路径\t备注，弹窗里自由输入）</summary>
    private static readonly Dictionary<string, string> _notes = new();
    private static string _notesPath;

    /// <summary>死点热力图（deathmap.txt：文件夹路径\t段号(0-49)\t次数）</summary>
    private static readonly Dictionary<string, int[]> _deathMap = new();
    private static string _deathMapPath;
    /// <summary>寄录图有未落盘的计数（死亡时置位，写盘由 FlushDeathMap 后台合并执行）</summary>
    private static bool _deathMapDirty;
    /// <summary>寄录图此刻有一条后台写盘任务在跑（1=在跑）。
    /// 与脏标记配对使用：脏标记说「有数据没落盘」，在飞标记说「已经有人在写了」——
    /// 两个都要有，不然要么每帧重复起线程，要么退出时兜底以为没脏数据而跳过（丢数据）</summary>
    private static int _deathMapWriting;
    /// <summary>完成度（progress.txt：文件夹路径\t最远死亡百分比 0-100，精确到格子；通关无检查点=100 覆盖）</summary>
    private static readonly Dictionary<string, int> _progress = new();
    private static string _progressPath;
    /// <summary>完成度有未落盘的数值（死亡时置位，写盘由 FlushDeathProgress 后台合并执行）</summary>
    private static bool _progressDirty;
    /// <summary>完成度此刻有一条后台写盘任务在跑（1=在跑），语义同 _deathMapWriting</summary>
    private static int _progressWriting;

    /// <summary>一次通关记录（records.txt：文件夹路径\t时间戳\txacc\t提前\t太快\t稍快\t完美\t稍慢\t太慢\t判定模式0宽1标2严\t检查点数\t自动播放\t不会失败\t音高\t自动格数）</summary>
    public class ClearRecord {
        public float Xacc;
        public int Early;      // 提前（TooEarly+VeryEarly）
        public int TooFast;    // 太快（Multipress 双押）
        public int Fast;       // 稍快（EarlyPerfect）
        public int Perfect;    // 完美
        public int Slow;       // 稍慢（LatePerfect）
        public int TooSlow;    // 太慢（VeryLate+TooLate）
        public long Time;      // Unix 秒
        public int Slot = -1;  // 判定模式：0=宽判 1=标判 2=严判；-1=旧记录未知
        /// <summary>检查点数。用了检查点的局不再记录，所以新记录这一列恒为 0；保留列是为了兼容老数据。</summary>
        public int Checkpoints;
        /// <summary>这一局开过自动播放（无瑕这类"真实成绩"成就绝不允许）。</summary>
        public bool Autoplay;
        /// <summary>这一局处于"不会失败"模式（同上）。</summary>
        public bool NoFail;
        /// <summary>通关时的音高（百分数，110 = 1.1 倍速）。0 = 旧记录没这一列（界面上不显示倍速）。
        /// 来源：`conductor.song.pitch`（通关那一刻的倍率 ×100），取不到就退而取谱面设置里的 pitch。</summary>
        public int Pitch;
        /// <summary>自动格（HitMargin.Auto）的数量。它算一次判定但不进那 6 个计数桶，
        /// 所以"格子数校验"要把它加上：6 桶之和 + 自动格 = 格子数。</summary>
        public int Auto;
    }

    private static readonly Dictionary<string, List<ClearRecord>> _records = new();
    private static string _recordsPath;
}
