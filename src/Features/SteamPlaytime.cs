using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LevelLibrary;

/// <summary>游玩时长：同步 Steam 的本地账本（Steam 根目录下 userdata\账号ID\config\localconfig.vdf）。
/// apps 段里 Playtime 的单位是分钟；已安装清单 appmanifest_*.acf 里没有这个数，且只有经 Steam 客户端启动过才记账。
/// 口径：读到就用 Steam 的、读不到安静退回我们自己的统计；不叠加「本次会话已玩多久」，只有个人信息栏那一行改用它
/// （100 小时成就与记录页的历史时长照旧按我们自己的数据算）。只读不写盘，且不引用 UnityEngine。</summary>
public static class SteamPlaytime {

    /// <summary>ADOFAI 的 Steam appid（商店页地址里那个数）。</summary>
    public const uint AppId = 977950;

    /// <summary>复查间隔（毫秒）：Steam 写那个文件是分钟级的，60 秒足够。</summary>
    private const int RescanMs = 60000;

    private static long _minutes;      // 0 = 没读到（界面靠它决定退不退回我们自己）
    private static string _hitPath;    // 命中的那个 localconfig.vdf（诊断用）
    private static string _hitUser;    // 命中的账号 ID（诊断用）
    private static long _hitStamp;     // 那次读到的文件时间戳（变了才重新解析）
    private static bool _loadedOnce;   // 开机那次 Load 跑过了（哪怕读到 0，也算"这个数已经知道"）
    private static int _nextCheckMs;   // 下一次复查（Environment.TickCount，不碰 Unity）
    private static bool _loggedOnce;   // 开机那行日志只打一次

    /// <summary>读到了吗（0 分钟也算「读到了」，那是新账号没玩过的正常值；只有没找到文件 / 没找到那段才算没读到）</summary>
    public static bool Found { get { return _hitPath != null; } }
    /// <summary>开机那次 Load 跑过没有。给"只同步一次"那种用法看：没跑过就别急着拿 0 当基准。</summary>
    public static bool Loaded { get { return _loadedOnce; } }
    /// <summary>Steam 记的分钟数（没读到 = 0）</summary>
    public static long Minutes { get { return _minutes; } }
    /// <summary>给界面用的秒数（没读到 = 0，调用方自己退回我们的统计）</summary>
    public static long TotalSeconds { get { return _minutes * 60L; } }
    public static string HitPath { get { return _hitPath; } }

    // ---------- 开机读一次 ----------

    /// <summary>开机调一次（Main.OnToggle 里，紧跟在开机预热后面）；读不到就安静地退回我们的统计。</summary>
    public static void Load() {
        try {
            Refresh(true);
        } catch (Exception e) {
            try { Lg.Ex("读 Steam 游玩时长失败（这行将退回我们自己的统计）", e); } catch { }
        } finally {
            _loadedOnce = true;    // 读失败也算"问过了"：调用方不必一直等它
        }
    }

    /// <summary>常驻段每帧调（内部 60 秒节流，平时只有一次整数比较）。</summary>
    public static void Tick() {
        try {
            if (unchecked(Environment.TickCount - _nextCheckMs) < 0) return;
            Refresh(false);
        } catch { }
    }

    /// <summary>真的去读（force = 开机那一次，一定会打一条日志）</summary>
    private static void Refresh(bool force) {
        _nextCheckMs = unchecked(Environment.TickCount + RescanMs);
        // 已经有命中文件时只比时间戳：没变就什么都不做（这就是「平时零开销」）
        if (_hitPath != null && File.Exists(_hitPath)) {
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(_hitPath).Ticks; } catch { }
            if (stamp == _hitStamp) return;
            long m;
            string user;
            if (TryReadOne(_hitPath, out m, out user)) {
                if (m != _minutes) {
                    Lg.Info("Steam 游玩时长已更新：" + Text(m)
                        + "（" + m + " 分钟 上次 " + _minutes + " 分钟）");
                }
                _minutes = m; _hitUser = user; _hitStamp = stamp;
                return;
            }
        }
        // 还没命中（或文件没了 / 读不动）时全量找一遍
        long best = -1; string bestPath = null, bestUser = null;
        ScanCandidates(CandidateRoots(), ref best, ref bestPath, ref bestUser);
        // 兜底扫盘：注册表没有 / Steam 搬过家 / 便携版 / 游戏装在第二库而 Steam 在别处时才走到这里；
        // 只在没命中时跑，正常机器一次都不扫。
        if (bestPath == null) {
            var fb = FallbackRoots();
            if (fb.Count > 0) {
                ScanCandidates(fb, ref best, ref bestPath, ref bestUser);
                Lg.Info("Steam 游玩时长：注册表与游戏目录都没找到 -> "
                    + "扫盘兜底（看了 " + fb.Count + " 个位置）："
                    + (bestPath != null ? bestPath : "还是没找到 ->  退回我们自己的统计"));
            }
        }
        if (bestPath == null) {
            if (force || !_loggedOnce) {
                _loggedOnce = true;
                Lg.Info("Steam 游玩时长：没找到账本 ->  个人信息栏那一行"
                    + "照旧按我们自己的统计显示（试过的根目录：" + JoinRoots() + "）");
            }
            return;
        }
        if (best > _minutes || force || _minutes == 0) {
            Lg.Info("Steam 游玩时长：" + Text(best) + "（" + best + " 分钟"
                + " 账号 " + bestUser + " ｜ " + bestPath + "）-> 个人信息栏那一行改用它");
        }
        _minutes = best; _hitPath = bestPath; _hitUser = bestUser;
        try { _hitStamp = File.GetLastWriteTimeUtc(bestPath).Ticks; } catch { _hitStamp = 0; }
        _loggedOnce = true;
    }

    private static string Text(long minutes) {
        return (minutes / 60) + " 小时 " + (minutes % 60) + " 分";
    }

    private static string JoinRoots() {
        try {
            var sb = new System.Text.StringBuilder();
            foreach (var r in CandidateRoots()) { if (sb.Length > 0) sb.Append(" ｜ "); sb.Append(r); }
            return sb.Length > 0 ? sb.ToString() : "（一个候选都没有）";
        } catch { return "(?)"; }
    }

    // ---------- 找 Steam 根目录 ----------

    /// <summary>在一批候选根目录里逐个扫、取最大的那个（用 ref 是为了把注册表与扫盘两趟合起来比）。</summary>
    private static void ScanCandidates(List<string> roots, ref long best, ref string bestPath, ref string bestUser) {
        if (roots == null) return;
        foreach (var root in roots) {
            string path, user; long m;
            try {
                if (ScanRoot(root, out m, out path, out user) && m > best) { best = m; bestPath = path; bestUser = user; }
            } catch { }
        }
    }

    /// <summary>候选 Steam 根目录：注册表三条 + 从游戏目录反推一条，去重后逐个试。</summary>
    public static List<string> CandidateRoots() {
        var list = new List<string>();
        AddReg(list, @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath");
        AddReg(list, @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddReg(list, @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath");
        // 从我们自己的目录反推：往上走到名字叫 steamapps 的那一级，它的父目录就是这一份库所在的根。
        // Steam 允许把库放在别的盘，而 userdata 一般在装 Steam 的那个根里，所以注册表那几条也要试。
        // 反推单独抽成 RootFromModPath（纯函数，可以直接喂字符串）；Main.ModEntry 在无引擎环境里是 null。
        try {
            string mod = Main.ModEntry != null ? Main.ModEntry.Path : null;
            string derived = RootFromModPath(mod);
            if (!string.IsNullOrEmpty(derived)) list.Add(derived);
        } catch { }
        var outp = new List<string>();
        foreach (var s in list) {
            if (string.IsNullOrEmpty(s)) continue;
            string n = s.Replace('/', '\\').TrimEnd('\\');
            if (n.Length == 0) continue;
            bool dup = false;
            foreach (var o in outp) if (string.Equals(o, n, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
            if (!dup) outp.Add(n);
        }
        return outp;
    }

    private static void AddReg(List<string> list, string key, string value) {
        try {
            object v = Microsoft.Win32.Registry.GetValue(key, value, null);
            if (v is string s && s.Length > 0) list.Add(s);
        } catch { }
    }

    /// <summary>从我们自己的 mod 目录反推这一份库所在的根（纯函数，直接喂字符串）。
    /// 形状：库根\steamapps\common\游戏\Mods\LevelLibrary，走到名叫 steamapps 的那一级、取它的父目录；
    /// 推不出来返回 null（安静退回我们自己的统计）。上限 8 层，免得在奇怪的深目录里一路走到盘根。</summary>
    public static string RootFromModPath(string modPath) {
        try {
            if (string.IsNullOrEmpty(modPath)) return null;
            var d = new DirectoryInfo(modPath.TrimEnd('\\', '/'));
            for (int i = 0; i < 8 && d != null; i++) {
                if (string.Equals(d.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) {
                    return d.Parent != null ? d.Parent.FullName : null;
                }
                d = d.Parent;
            }
        } catch { }
        return null;
    }

    // ---------- 兜底：扫盘找 Steam 根 ----------

    /// <summary>兜底找法：固定盘上的常见位置 + 每盘顶层一级目录里找「长得像 Steam 根」的目录，只在注册表与从
    /// 游戏目录反推都没命中时才跑（覆盖便携版 Steam / 搬过家 / 注册表读不到的机器）。只认 DriveType.Fixed
    /// + IsReady —— 光驱 / U 盘 / 断线的网络盘上做目录检查会卡好几秒；顶层只扫一级、每盘最多看 200 个目录。</summary>
    public static List<string> FallbackRoots() {
        var list = new List<string>();
        // 常见位置（覆盖多数「自定义安装但没注册表」的机器）
        string[] wellKnown = { "Steam", "SteamLibrary", @"Games\Steam", @"Games\SteamLibrary",
                               @"Program Files (x86)\Steam", @"Program Files\Steam" };
        try {
            foreach (var d in DriveInfo.GetDrives()) {
                try {
                    if (d.DriveType != DriveType.Fixed) continue;   // 网络盘 / 光驱 / U 盘一律不碰
                    if (!d.IsReady) continue;
                    string root = d.RootDirectory.FullName;         // 形如 "D:\" 根目录
                    foreach (var rel in wellKnown) {
                        string c = Path.Combine(root, rel);
                        if (LooksLikeSteamRoot(c)) list.Add(c);
                    }
                    string[] subs;
                    try { subs = Directory.GetDirectories(root); } catch { continue; }
                    int n = 0;
                    foreach (var s in subs) {
                        if (++n > 200) break;                       // 上限：别在几万个目录的盘上磨蹭
                        if (LooksLikeSteamRoot(s)) list.Add(s);
                    }
                } catch { }
            }
        } catch { }
        return list;
    }

    /// <summary>这个目录「长得像 Steam 根」吗（纯函数）：有 userdata\ 、或 steam.exe 、或 config\loginusers.vdf 。
    /// 光「目录存在」不算 —— 否则每个空目录都会被当成候选、白跑一堆解析。</summary>
    public static bool LooksLikeSteamRoot(string dir) {
        try {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            if (Directory.Exists(Path.Combine(dir, "userdata"))) return true;
            if (File.Exists(Path.Combine(dir, "steam.exe"))) return true;
            return File.Exists(Path.Combine(dir, "config", "loginusers.vdf"));
        } catch { return false; }
    }

    // ---------- 在一份库根里找（纯函数） ----------

    /// <summary>把 库根\userdata\*\config\localconfig.vdf 逐个解析，取最大的那个 appid 时长。
    /// 多账号时取最大 —— 取「最近玩过的」会让新账号把数字打回个位数。不打日志，日志由调用方 Refresh 打。</summary>
    public static bool ScanRoot(string root, out long minutes, out string path, out string user) {
        minutes = 0; path = null; user = null;
        try {
            if (string.IsNullOrEmpty(root)) return false;
            string ud = Path.Combine(root, "userdata");
            if (!Directory.Exists(ud)) return false;
            foreach (var acc in Directory.GetDirectories(ud)) {
                string f = Path.Combine(acc, "config", "localconfig.vdf");
                if (!File.Exists(f)) continue;
                long m; string u;
                if (!TryReadOne(f, out m, out u)) continue;
                if (path == null || m > minutes) { minutes = m; path = f; user = u; }
            }
        } catch { }
        return path != null;
    }

    private static bool TryReadOne(string file, out long minutes, out string user) {
        minutes = 0; user = null;
        try {
            user = new DirectoryInfo(Path.GetDirectoryName(Path.GetDirectoryName(file))).Name;
            string text = File.ReadAllText(file);
            long m = ParseVdf(text, AppId);
            if (m < 0) return false;
            minutes = m;
            return true;
        } catch { return false; }
    }

    // ---------- 解析（纯函数 · 零 IO） ----------

    /// <summary>从 localconfig.vdf 的文本里取指定 appid 的 Playtime（分钟）；返回 -1 = 没找到那个 appid
    /// （与「找到了、值是 0」区分开 —— 新账号就是后者）。只认「整行只有这一个键」的 appid 行：
    /// Software 段里那句「键 + 一串十六进制值」的 blob 若不跳过，就会把它当成块、从此再也找不到 Playtime。</summary>
    public static long ParseVdf(string text, uint appId) {
        if (string.IsNullOrEmpty(text)) return -1;
        string id = appId.ToString(CultureInfo.InvariantCulture);
        int depth = 0, appDepth = -1;
        long found = -1;
        foreach (var raw in text.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;
            // 括号行：Valve 的写法是独占一行，这里仍按「整行里出现就算」处理（更耐活）
            if (line[0] == '{') { depth++; continue; }
            if (line[0] == '}') {
                depth--;
                if (appDepth >= 0 && depth <= appDepth) appDepth = -1;   // 出了 appid 那个块就不再往里看
                continue;
            }
            int n = 0;
            string k1 = null, k2 = null;
            foreach (var tok in Tokens(line)) { n++; if (n == 1) k1 = tok; else if (n == 2) { k2 = tok; break; } }
            if (n == 0) continue;
            if (appDepth >= 0) {
                // 只在 appid 块的直接子级里找 Playtime（再深一层是它自己的子块，不看）
                if (depth == appDepth + 1 && n == 2 && k1 == "Playtime") {
                    long v;
                    if (long.TryParse(k2, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= 0) {
                        if (v > found) found = v;
                    }
                }
                continue;
            }
            if (n == 1 && k1 == id) { appDepth = depth; if (found < 0) found = 0; }
        }
        return found;
    }

    /// <summary>把一行里的引号串依次取出来（"a" "b" 取成 a, b）</summary>
    private static IEnumerable<string> Tokens(string line) {
        int i = 0;
        while (i < line.Length) {
            if (line[i] != '"') { i++; continue; }
            int j = line.IndexOf('"', i + 1);
            if (j < 0) yield break;
            yield return line.Substring(i + 1, j - i - 1);
            i = j + 1;
        }
    }
}
