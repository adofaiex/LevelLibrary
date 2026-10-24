using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LevelLibrary;

/// <summary>谱面标记存储（marks.txt：每行 "FilePath\t主标记\t判定标记"）+ 标记图标加载缓存</summary>
public static class MarkStore {
    private static readonly Dictionary<string, string> _marks = new();  // FilePath -> 等级标（P/G/U/Q，单选，决定等级）
    private static readonly Dictionary<string, string> _specials = new();  // FilePath -> 特殊标（最多2个，|分隔；并列显示不影响等级）
    private static readonly Dictionary<string, string> _judges = new();  // FilePath -> 判定标记（宽判/标判/严判）
    private static readonly Dictionary<string, Sprite> _icons = new();  // 标记名 -> Sprite
    private static readonly HashSet<string> _autoRated = new();  // 自动评级标过的 FilePath（auto_rated.txt）
    private static string _autoPath;
    private static string _path;

    /// <summary>判定标记简称 -> 图标文件名（宽松判定/标准判定/严格判定；英文名防 zip 中文名编码损坏）</summary>
    public static readonly Dictionary<string, string> JudgeFile = new() {
        { "宽判", "wide" },
        { "标判", "normal" },
        { "严判", "strict" },
    };

    /// <summary>图标文件名 -> 判定简称（宽松判定 -> 宽判）；不是判定图返回 null</summary>
    public static string JudgeShort(string fileName) {
        foreach (var kv in JudgeFile) {
            if (kv.Value == fileName) return kv.Key;
        }
        return null;
    }

    /// <summary>初始化（读 marks.txt）</summary>
    public static void Load(string modPath) {
        _path = modPath;
        _marks.Clear();
        _specials.Clear();
        _judges.Clear();
        _iconsMissing.Clear();   // 重新加载时清负缓存（素材目录可能已变化）
        _autoPath = Path.Combine(modPath, "auto_rated.txt");
        _autoRated.Clear();
        try {
            // 自动评级记录
            if (File.Exists(_autoPath)) {
                foreach (var line in File.ReadAllLines(_autoPath)) {
                    string t = line.Trim();
                    if (t.Length > 0) _autoRated.Add(LevelScanner.ToAbs(t));
                }
            }
        } catch (System.Exception e) { Lg.Ex("加载自动评级记录失败", e); }
        try {
            string file = Path.Combine(modPath, "marks.txt");
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadAllLines(file)) {
                var parts = line.Split('\t');
                if (parts.Length >= 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0) {
                    string fp = LevelScanner.ToAbs(parts[0].Trim());
                    string mark = parts[1].Trim();
                    // 旧格式兼容：只有 2 列时特殊图存在主标记列，读进来要挪到 _specials（不影响等级）
                    if (TabOf(mark) == "特殊" && parts.Length < 4) {
                        _specials[fp] = mark;
                    } else {
                        _marks[fp] = mark;
                    }
                    if (parts.Length >= 3 && parts[2].Trim().Length > 0) _judges[fp] = parts[2].Trim();
                    if (parts.Length >= 4 && parts[3].Trim().Length > 0) _specials[fp] = parts[3].Trim();
                }
            }
        } catch (System.Exception e) {
            // 必须报错：静默失败会让所有标记凭空消失且毫无提示
            PortalUI.ReportError("读取标记数据失败（标记可能显示为未标记，请检查 marks.txt 是否被占用或损坏）", e);
        }
    }

    /// <summary>保存 marks.txt</summary>
    public static void Save() {
        if (string.IsNullOrEmpty(_path)) {
            Lg.Info("警告：marks.txt 路径未初始化，标记未保存！");
            return;
        }
        try {
            var lines = new List<string>();
            foreach (var kv in _marks) {
                _judges.TryGetValue(kv.Key, out var j);
                _specials.TryGetValue(kv.Key, out var s);
                lines.Add(LevelScanner.ToRel(kv.Key) + "\t" + kv.Value + "\t" + (j ?? "") + "\t" + (s ?? ""));
            }
            foreach (var kv in _judges) {
                if (!_marks.ContainsKey(kv.Key)) {
                    _specials.TryGetValue(kv.Key, out var s);
                    lines.Add(LevelScanner.ToRel(kv.Key) + "\t\t" + kv.Value + "\t" + (s ?? ""));
                }
            }
            foreach (var kv in _specials) {
                if (!_marks.ContainsKey(kv.Key) && !_judges.ContainsKey(kv.Key))
                    lines.Add(LevelScanner.ToRel(kv.Key) + "\t\t\t" + kv.Value);
            }
            File.WriteAllLines(Path.Combine(_path, "marks.txt"), lines);
        } catch (System.Exception e) {
            PortalUI.ReportError("保存标记失败（标记数据可能丢失）", e);
        }
    }

    public static string Get(string filePath) => filePath != null && _marks.TryGetValue(LevelScanner.ToAbs(filePath), out var m) ? m : null;

    public static string GetJudge(string filePath) => filePath != null && _judges.TryGetValue(LevelScanner.ToAbs(filePath), out var j) ? j : null;

    /// <summary>等级序号：P1~P20=0~19、G1~G20=20~39、U1~U20=40~59，无等级 / 非等级标返回 -1。
    /// 全项目唯一的「标记 → 等级」算法，PortalUI.RankOf 与 LevelScanner.SortEntries 都转发到这里。</summary>
    public static int RankOf(string mark) {
        if (string.IsNullOrEmpty(mark)) return -1;
        char c = mark[0];
        int baseIdx;
        if (c == 'P' || c == 'p') baseIdx = 0;
        else if (c == 'G' || c == 'g') baseIdx = 20;
        else if (c == 'U' || c == 'u') baseIdx = 40;
        else if (c == 'Q' || c == 'q') {
            // Q 图算 U 级：Q0~Q4 → 40~44，Qq → 45（与 U1~U6 同序）
            if (string.Equals(mark, "Qq", System.StringComparison.OrdinalIgnoreCase)) return 45;
            int qi = 1;
            while (qi < mark.Length && !char.IsDigit(mark[qi])) qi++;
            int qnum = 0;
            while (qi < mark.Length && char.IsDigit(mark[qi])) { qnum = qnum * 10 + (mark[qi] - '0'); qi++; }
            if (qnum < 0 || qnum > 20) return -1;
            return 40 + qnum;
        }
        else return -1;
        int i = 1;
        while (i < mark.Length && !char.IsDigit(mark[i])) i++;
        int num = 0;
        while (i < mark.Length && char.IsDigit(mark[i])) {
            num = num * 10 + (mark[i] - '0');
            i++;
        }
        if (num < 1 || num > 20) return -1;
        return baseIdx + num - 1;
    }

    /// <summary>读取特殊标（"sp1|sp2"，用 | 分隔，最多2个；无返回 null）</summary>
    public static string GetSpecials(string filePath) => filePath != null && _specials.TryGetValue(LevelScanner.ToAbs(filePath), out var s) ? s : null;

    /// <summary>设置特殊标（"sp1|sp2"，|分隔；空清空）</summary>
    public static void SetSpecials(string filePath, string specials) {
        filePath = LevelScanner.ToAbs(filePath);   // 防御：key 统一绝对路径
        if (string.IsNullOrEmpty(filePath)) return;
        if (string.IsNullOrEmpty(specials)) _specials.Remove(filePath);
        else _specials[filePath] = specials;
        Save();
    }

    /// <summary>路径迁移：备份位置变更后把标记记录的旧路径前缀换成新前缀</summary>
    public static void MigratePath(string oldBase, string newBase) {
        if (string.IsNullOrEmpty(oldBase) || string.IsNullOrEmpty(newBase)) return;
        var newMarks = new Dictionary<string, string>();
        foreach (var kv in _marks) newMarks[LevelScanner.Repath(kv.Key, oldBase, newBase)] = kv.Value;
        var newSpecials = new Dictionary<string, string>();
        foreach (var kv in _specials) newSpecials[LevelScanner.Repath(kv.Key, oldBase, newBase)] = kv.Value;
        var newJudges = new Dictionary<string, string>();
        foreach (var kv in _judges) newJudges[LevelScanner.Repath(kv.Key, oldBase, newBase)] = kv.Value;
        _marks.Clear();
        foreach (var kv in newMarks) _marks[kv.Key] = kv.Value;
        _specials.Clear();
        foreach (var kv in newSpecials) _specials[kv.Key] = kv.Value;
        _judges.Clear();
        foreach (var kv in newJudges) _judges[kv.Key] = kv.Value;
        Save();
    }

    public static void Set(string filePath, string mark) {
        filePath = LevelScanner.ToAbs(filePath);   // 防御：key 统一绝对路径
        if (string.IsNullOrEmpty(filePath)) return;
        if (string.IsNullOrEmpty(mark)) _marks.Remove(filePath);
        else _marks[filePath] = mark;
        Save();
    }

    /// <summary>记录该谱面是自动评级标的（写入 auto_rated.txt）</summary>
    public static void MarkAutoRated(string filePath) {
        filePath = LevelScanner.ToAbs(filePath);
        if (string.IsNullOrEmpty(filePath)) return;
        if (_autoRated.Add(filePath)) SaveAutoRated();
    }

    private static void SaveAutoRated() {
        if (string.IsNullOrEmpty(_autoPath)) return;
        try {
            var lines = new List<string>();
            foreach (var fp in _autoRated) lines.Add(LevelScanner.ToRel(fp));
            File.WriteAllLines(_autoPath, lines);
        } catch (System.Exception e) { Lg.Ex("保存自动评级记录失败", e); }
    }

    /// <summary>设置判定标记（三选一：传空清除）</summary>
    public static void SetJudge(string filePath, string judge) {
        filePath = LevelScanner.ToAbs(filePath);   // 防御：key 统一绝对路径
        if (string.IsNullOrEmpty(filePath)) return;
        if (string.IsNullOrEmpty(judge)) _judges.Remove(filePath);
        else _judges[filePath] = judge;
        Save();
    }

    /// <summary>路径变化（重命名/移动后）时迁移记录</summary>
    public static void Move(string oldPath, string newPath) {
        if (oldPath == null || newPath == null || oldPath == newPath) return;
        if (_marks.TryGetValue(oldPath, out var m)) {
            _marks.Remove(oldPath);
            _marks[newPath] = m;
        }
        if (_specials.TryGetValue(oldPath, out var s)) {
            _specials.Remove(oldPath);
            _specials[newPath] = s;
        }
        if (_judges.TryGetValue(oldPath, out var j)) {
            _judges.Remove(oldPath);
            _judges[newPath] = j;
        }
        Save();
    }

    public static void Remove(string filePath) {
        // 复核后确认：这里不需要 ToAbs 归一。写入口 Set 与本方法拿的是同一个
        // LevelEntry.FilePath（由 Directory.GetFiles / PickMainFile 产生，恒为绝对路径），
        // 而 ToAbs 对已 rooted 的路径原样返回。加上它只是多一次 IsPathRooted 判断，
        // 不改变任何结果 —— 保留原样，避免"以为修了其实没修"的假改动。
        bool changed = false;
        if (filePath != null) {
            changed |= _marks.Remove(filePath);
            changed |= _specials.Remove(filePath);
            changed |= _judges.Remove(filePath);
        }
        if (changed) Save();
    }

    /// <summary>迁移标记记录：原位置路径 -> backup 副本路径（切换备份驱动后旧标记不丢）</summary>
    public static void Migrate() {
        if (string.IsNullOrEmpty(_path)) return;
        var newMarks = new Dictionary<string, string>();
        var newSpecials = new Dictionary<string, string>();
        var newJudges = new Dictionary<string, string>();
        bool changed = false;
        foreach (var kv in _marks) {
            string bp = LevelScanner.ToBackupPath(kv.Key);
            if (bp != null && System.IO.File.Exists(bp)) {
                newMarks[bp] = kv.Value;
                changed = true;
            } else {
                newMarks[kv.Key] = kv.Value;
            }
        }
        foreach (var kv in _specials) {
            string bp = LevelScanner.ToBackupPath(kv.Key);
            if (bp != null && System.IO.File.Exists(bp)) {
                newSpecials[bp] = kv.Value;
                changed = true;
            } else {
                newSpecials[kv.Key] = kv.Value;
            }
        }
        foreach (var kv in _judges) {
            string bp = LevelScanner.ToBackupPath(kv.Key);
            if (bp != null && System.IO.File.Exists(bp)) {
                newJudges[bp] = kv.Value;
                changed = true;
            } else {
                newJudges[kv.Key] = kv.Value;
            }
        }
        if (changed) {
            _marks.Clear();
            _specials.Clear();
            _judges.Clear();
            foreach (var kv in newMarks) _marks[kv.Key] = kv.Value;
            foreach (var kv in newSpecials) _specials[kv.Key] = kv.Value;
            foreach (var kv in newJudges) _judges[kv.Key] = kv.Value;
            Save();
        }
    }

    /// <summary>按规则分 tab：Planetary(P)/Galactic(G)/Universal(U)/UJ＆Q（含 J 的 U 图 + Q 图）/特殊</summary>
    public static string TabOf(string mark) {
        if (mark.Length == 0) return "特殊";
        char c = mark[0];
        if (c == 'P' || c == 'p') return "P";
        if (c == 'G' || c == 'g') return "G";
        if (c == 'U' || c == 'u') {
            // U 开头的图：名字里含 J（如 U1J/U11J）归 UJ＆Q 组
            if (mark.IndexOf('J') >= 0 || mark.IndexOf('j') >= 0) return "UJQ";
            return "U";
        }
        if (c == 'Q' || c == 'q') return "UJQ";  // Q 图（Q0-Q4/Qq）归 UJ＆Q
        return "特殊";
    }

    /// <summary>加载标记图标（缓存；不存在返回 null）。
    /// 找不到的标记也记进 _iconsMissing：否则滚动列表时每行都会打一次磁盘。</summary>
    private static readonly HashSet<string> _iconsMissing = new();
    public static Sprite LoadIcon(string mark) {
        if (string.IsNullOrEmpty(mark)) return null;
        if (_icons.TryGetValue(mark, out var cached)) return cached;
        if (_iconsMissing.Contains(mark)) return null;  // 找过且不存在 → 直接返回，不再碰磁盘
        try {
            string file = Path.Combine(_path, "assets", "marks", mark + ".png");
            if (!File.Exists(file)) {
                _iconsMissing.Add(mark);
                // 难度图是资源包素材，缺了就是资源包不全 —— 交给 AssetPack 汇总报（一次会话一句），
                // 不要在这里每个标记打一行日志
                AssetPack.NoteMissing("marks/" + mark + ".png");
                return null;
            }
            using (var ms = new MemoryStream(File.ReadAllBytes(file)))
            using (var bmp = new System.Drawing.Bitmap(ms)) {
                int w = bmp.Width, h = bmp.Height;
                var rect = new System.Drawing.Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try {
                    var bytes = new byte[data.Stride * h];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    var pixels = new Color32[w * h];
                    for (int y = 0; y < h; y++) {
                        int srcRow = y * data.Stride;
                        int dstRow = (h - 1 - y) * w;
                        for (int x = 0; x < w; x++) {
                            int si = srcRow + x * 4;
                            pixels[dstRow + x] = new Color32(bytes[si + 2], bytes[si + 1], bytes[si], bytes[si + 3]);
                        }
                    }
                    // 素材已是按显示尺寸做的 128x128 抗锯齿圆图：不开 mipmap ——
                    // mipmap 链与双线性合用会把图标边缘和透明区混出脏边；无 mipmap + 双线性即可。
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    PerfDiag.Tag(tex, "mark:" + mark);  // 归因标签：LL_ 前缀 = 我们建的，有缓存所以数量有上限
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    var spr = PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f)), "mark:" + mark);
                    _icons[mark] = spr;
                    return spr;
                } finally {
                    bmp.UnlockBits(data);
                }
            }
        } catch {
            _iconsMissing.Add(mark);
            return null;
        }
    }

    /// <summary>列出某个 tab 的标记名（按素材目录文件名 + 规则分组，排除非图片）</summary>
    public static List<string> ListTab(string tab) {
        var result = new List<string>();
        try {
            string dir = Path.Combine(_path, "assets", "marks");
            if (!Directory.Exists(dir)) return result;
            foreach (var f in Directory.GetFiles(dir, "*.png")) {
                string name = Path.GetFileNameWithoutExtension(f);
                if (TabOf(name) == tab) result.Add(name);
            }
        } catch { }
        // 排序：数字在前按数字，字母按字典序；UJQ 组特殊：Q0-4 → Qq → U*J 数字序
        if (tab == "UJQ") {
            result.Sort((a, b) => {
                bool aQ = a.StartsWith("Q", System.StringComparison.OrdinalIgnoreCase);
                bool bQ = b.StartsWith("Q", System.StringComparison.OrdinalIgnoreCase);
                if (aQ != bQ) return aQ ? -1 : 1;   // Q 图在前
                if (aQ) {
                    // Q0-4 按数字，Qq 最后
                    if (string.Equals(a, "Qq", System.StringComparison.OrdinalIgnoreCase)) return 1;
                    if (string.Equals(b, "Qq", System.StringComparison.OrdinalIgnoreCase)) return -1;
                    return ExtractNum(a).CompareTo(ExtractNum(b));
                }
                return ExtractNum(a).CompareTo(ExtractNum(b));   // U*J 按数字
            });
        } else {
            result.Sort((a, b) => {
                int na = ExtractNum(a), nb = ExtractNum(b);
                if (na != nb) return na.CompareTo(nb);
                return string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
            });
        }
        return result;
    }

    private static int ExtractNum(string s) {
        int start = 1;   // 跳过首字母
        while (start < s.Length && !char.IsDigit(s[start])) start++;
        int end = start;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        if (end > start && int.TryParse(s.Substring(start, end - start), out int v)) return v;
        return int.MaxValue;
    }
}
