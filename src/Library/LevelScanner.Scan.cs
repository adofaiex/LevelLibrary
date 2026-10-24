using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LevelLibrary;

/// <summary>LevelScanner 的扫描分片：外部目录扫描、backup 副本与路径迁移、条目排序。</summary>
public static partial class LevelScanner {

    /// <summary>绝对路径 -> 相对 mod 目录（写数据文件用）；不在 mod 内保持原样。</summary>
    public static string ToRel(string abs) {
        if (string.IsNullOrEmpty(abs) || string.IsNullOrEmpty(ModRoot)) return abs;
        if (abs.StartsWith(ModRoot, System.StringComparison.OrdinalIgnoreCase)) {
            return abs.Substring(ModRoot.Length).TrimStart('\\', '/');
        }
        return abs;
    }

    /// <summary>相对 mod 目录 -> 绝对路径（读数据文件用）；已是绝对路径保持原样。</summary>
    public static string ToAbs(string rel) {
        if (string.IsNullOrEmpty(rel)) return rel;
        if (System.IO.Path.IsPathRooted(rel)) return rel;
        if (string.IsNullOrEmpty(ModRoot)) return rel;
        return System.IO.Path.Combine(ModRoot, rel);
    }

    /// <summary>全量加载：不扫描任何外部目录，条目全部来自玩家导入（导入目录 + recorded + backup 兜底）。</summary>
    public static void Refresh(string extraDir = null) {
        Entries.Clear();
        Lg.Info($"[LevelLib] Refresh: recorded={RecordedFolders.Count} backupBase={BackupBase ?? "null"} exists={(!string.IsNullOrEmpty(BackupBase) && Directory.Exists(BackupBase))} extra={extraDir ?? "null"}");
        // 额外目录（mod 导入的谱面）：每个 .adofai 文件一个条目，文件名当曲名
        if (!string.IsNullOrEmpty(extraDir) && Directory.Exists(extraDir)) {
            foreach (var f in Directory.GetFiles(extraDir, "*.adofai")) {
                Entries.Add(new LevelEntry {
                    Title = Path.GetFileNameWithoutExtension(f),
                    FolderPath = extraDir,
                    FilePath = f,
                });
            }
        }
        // recorded.txt 的自动录入谱面：文件夹唯一即一个条目，不按 ogg 名去重（同名曲在不同文件夹都要保留）
        int recOk = 0;
        foreach (var dir in RecordedFolders) {
            try {
                if (!Directory.Exists(dir)) { Lg.Info($"[LevelLib] recorded 目录不存在: {dir}"); continue; }
                if (Entries.Any(e => string.Equals(e.FolderPath, dir, System.StringComparison.OrdinalIgnoreCase))) continue;
                string backupMain = PickMainFile(dir);
                if (backupMain == null) { Lg.Info($"[LevelLib] recorded 无 adofai: {dir}"); continue; }
                string oggName = SuggestTitle(dir);
                string title = oggName;
                if (_titleOverrides.TryGetValue(backupMain, out var over)) title = over;
                Entries.Add(new LevelEntry {
                    Title = title,
                    OggName = oggName,
                    FolderPath = dir,
                    FilePath = backupMain,
                });
                recOk++;
            } catch (System.Exception rex) { Lg.Ex("recorded 恢复失败", rex); }
        }
        Lg.Info($"[LevelLib] recorded 恢复 {recOk} 个");
        // backup 兜底：backup 下每个目录都恢复成条目，同样不按 ogg 名去重，防同名曲被误跳过
        if (!string.IsNullOrEmpty(BackupBase) && Directory.Exists(BackupBase)) {
            foreach (var dir in Directory.GetDirectories(BackupBase)) {
                try {
                    if (Entries.Any(e => string.Equals(e.FolderPath, dir, System.StringComparison.OrdinalIgnoreCase))) continue;
                    string backupMain = PickMainFile(dir);
                    if (backupMain == null) continue;
                    string oggName = SuggestTitle(dir);
                    string title = oggName;
                    if (_titleOverrides.TryGetValue(backupMain, out var over2)) title = over2;
                    Entries.Add(new LevelEntry {
                        Title = title,
                        OggName = oggName,
                        FolderPath = dir,
                        FilePath = backupMain,
                    });
                } catch { }
            }
        }
        // 分组重排：# 组（数字 / 符号开头）最前，然后 A-Z 组；组内按曲名忽略大小写
        SortEntries();
    }

    /// <summary>确保目录的 backup 副本：整个文件夹同步复制，返回副本目录；失败返回 null。</summary>
    public static string EnsureBackup(string srcDir, string dirName) {
        try {
            if (string.IsNullOrEmpty(BackupBase)) return srcDir;   // 未初始化就退回原目录
            System.IO.Directory.CreateDirectory(BackupBase);
            string dest = System.IO.Path.Combine(BackupBase, SanitizeName(dirName));
            if (System.IO.Directory.Exists(dest)) {
                // 已存在：有 .adofai 就直接用，没有就重拷
                if (System.IO.Directory.GetFiles(dest, "*.adofai").Length > 0) return dest;
                System.IO.Directory.Delete(dest, true);
            }
            CopyDirectory(srcDir, dest);
            return dest;
        } catch (System.Exception e) {
            Lg.Ex("备份副本失败", e);
            return null;
        }
    }

    /// <summary>这个谱面目录是不是我们自己的副本：备份根的第一层子目录，或 mod 目录以内的东西。
    /// 删除谱面时只有自己的副本才连文件一起删 —— 没开导入备份时条目直接指向玩家的原文件夹，
    /// 那里往往是他唯一的一份。</summary>
    public static bool IsOwnCopy(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return false;
        try {
            string full = System.IO.Path.GetFullPath(folderPath).TrimEnd('\\', '/');
            if (full.Length == 0) return false;
            // 副本永远是 BackupBase\名字 这一层，所以只认第一层子目录；备份根自己就是盘符根时
            // 整条不认，免得把整块盘当成我们的副本
            if (!string.IsNullOrEmpty(BackupBase)) {
                string b = System.IO.Path.GetFullPath(BackupBase).TrimEnd('\\', '/');
                if (b.Length > 0 && !b.EndsWith(":")
                    && full.StartsWith(b + "\\", System.StringComparison.OrdinalIgnoreCase)
                    && full.IndexOf('\\', b.Length + 1) < 0) return true;
            }
            string mod = System.IO.Path.GetFullPath(Main.ModEntry.Path).TrimEnd('\\', '/');
            if (mod.Length > 0 && full.StartsWith(mod + "\\", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        } catch (System.Exception e) {
            Lg.Ex("判断谱面目录归属失败", e);
            return false;
        }
    }

    /// <summary>文件名清洗（去掉路径非法字符）</summary>
    private static string SanitizeName(string name) {
        if (string.IsNullOrEmpty(name)) return "level";
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray().Where(c => !invalid.Contains(c)).ToArray();
        string s = new string(chars).Trim();
        return s.Length == 0 ? "level" : s;
    }

    /// <summary>挑一个合格 ogg 名：排除名字里带图片扩展名的，取最长的一个；没有返回 null。</summary>
    private static string PickOggName(string dir) {
        var oggs = Directory.GetFiles(dir, "*.ogg");
        if (oggs.Length == 0) return null;
        var candidates = new List<string>();
        foreach (var o in oggs) {
            string name = Path.GetFileNameWithoutExtension(o);
            if (name.Length < 3) continue;
            if (name.IndexOf(".png", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf(".jpg", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf(".jpeg", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf(".gif", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            candidates.Add(name);
        }
        if (candidates.Count == 0) return null;
        string best = candidates[0];
        foreach (var c in candidates) {
            if (c.Length > best.Length) best = c;
        }
        return best;
    }

    /// <summary>挑主谱面文件：名字含 main 的优先，没有就取字节数最大的；没有 .adofai 返回 null。</summary>
    public static string PickMainFile(string dir) {
        var files = Directory.GetFiles(dir, "*.adofai");
        if (files.Length == 0) return null;
        foreach (var f in files) {
            string name = Path.GetFileNameWithoutExtension(f);
            if (name.IndexOf("main", System.StringComparison.OrdinalIgnoreCase) >= 0) return f;
        }
        string best = files[0];
        long bestLen = -1;
        foreach (var f in files) {
            try {
                long len = new FileInfo(f).Length;
                if (len > bestLen) { bestLen = len; best = f; }
            } catch { }
        }
        return best;
    }

    /// <summary>分组重排：收藏永远置顶（组内字母序），其余按当前排序方式。
    /// 字母分组（默认）= # 组 + A-Z 组，组内按曲名；按标记难度 = P1→P20→G1→G20→U1→U20，未标记的放最下面。
    /// 排序方式是全局唯一的一份（PortalUI.SortByDifficulty）：库列表 / 关卡选择弹窗 / 随机进谱面看到的顺序一致。</summary>
    public static void SortEntries() {
        var favGroup = Entries.Where(e => IsFavorite(e.FolderPath))
            .OrderBy(e => e.Title, System.StringComparer.OrdinalIgnoreCase).ToList();
        var rest = Entries.Where(e => !IsFavorite(e.FolderPath)).ToList();
        Entries.Clear();
        Entries.AddRange(favGroup);
        if (PortalUI.SortByDifficulty) {
            // -1（没等级标 / 特殊标）不许当成最小的 0，必须映射成比所有真实档位都大的数，
            // 未标记的才会落在最下面（Q 图由 MarkStore.RankOf 落进 U 档，与等级筛选一致）。
            var byRank = rest.OrderBy(e => { int r = MarkStore.RankOf(MarkStore.Get(e.FilePath)); return r < 0 ? int.MaxValue : r; })
                .ThenBy(e => e.Title, System.StringComparer.OrdinalIgnoreCase).ToList();
            Entries.AddRange(byRank);
            return;
        }
        var hashGroup = rest.Where(e => GroupKey(e.Title) == "#")
            .OrderBy(e => e.Title, System.StringComparer.OrdinalIgnoreCase).ToList();
        var letterGroup = rest.Where(e => GroupKey(e.Title) != "#")
            .OrderBy(e => GroupKey(e.Title), System.StringComparer.Ordinal)
            .ThenBy(e => e.Title, System.StringComparer.OrdinalIgnoreCase).ToList();
        Entries.AddRange(hashGroup);
        Entries.AddRange(letterGroup);
    }

    /// <summary>曲名首字母分组键：A-Z 返回大写字母，中文返回拼音首字母，数字 / 符号返回 #。</summary>
    public static string GroupKey(string title) {
        if (string.IsNullOrEmpty(title)) return "#";
        char c = title[0];
        if (c >= 'a' && c <= 'z') return ((char)(c - 32)).ToString();
        if (c >= 'A' && c <= 'Z') return c.ToString();
        if (c < 128) return "#";   // 数字/符号
        return PinyinInitial(c);
    }

    /// <summary>汉字转拼音首字母：GB2312 区位码分段（一级汉字按拼音排序）；生僻字或失败返回 #。</summary>
    private static string PinyinInitial(char c) {
        try {
            byte[] bytes = System.Text.Encoding.GetEncoding("GB2312").GetBytes(c.ToString());
            if (bytes.Length != 2) return "#";
            int q = bytes[0] - 0xA0;   // 区
            int w = bytes[1] - 0xA0;   // 位
            int code = q * 100 + w;
            if (code >= 1601 && code < 1637) return "A";
            if (code < 1833) return "B";
            if (code < 2078) return "C";
            if (code < 2274) return "D";
            if (code < 2302) return "E";
            if (code < 2433) return "F";
            if (code < 2594) return "G";
            if (code < 2787) return "H";
            if (code < 3106) return "J";
            if (code < 3212) return "K";
            if (code < 3472) return "L";
            if (code < 3635) return "M";
            if (code < 3722) return "N";
            if (code < 3730) return "O";
            if (code < 3858) return "P";
            if (code < 4027) return "Q";
            if (code < 4086) return "R";
            if (code < 4390) return "S";
            if (code < 4558) return "T";
            if (code < 4684) return "W";
            if (code < 4925) return "X";
            if (code < 5249) return "Y";
            if (code < 5590) return "Z";
            return "#";
        } catch {
            return "#";
        }
    }

    /// <summary>把列表里所有谱面的整个文件夹增量备份到 backup 目录；manifest.txt 记录已备份的源路径。</summary>
    public static void BackupLevels(string backupDir) {
        // 后台线程跑，避免首次全量备份卡住游戏；用独立线程而非共享线程池，见 Main.RunBackground
        Main.RunBackground("BackupLevels", () => {
            try {
                System.IO.Directory.CreateDirectory(backupDir);
                string manifest = System.IO.Path.Combine(backupDir, "manifest.txt");
                var done = new HashSet<string>();
                var lines = new List<string>();
                if (System.IO.File.Exists(manifest)) {
                    foreach (var l in System.IO.File.ReadAllLines(manifest)) {
                        string t = l.Trim();
                        if (t.Length > 0) { done.Add(t); lines.Add(t); }
                    }
                }
                int added = 0;
                foreach (var e in Entries) {
                    try {
                        // imported 谱面本身就在 mod 文件夹内，不重复备份
                        if (string.Equals(e.FolderPath, Main.ImportDir, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!System.IO.Directory.Exists(e.FolderPath)) continue;
                        string full = System.IO.Path.GetFullPath(e.FolderPath);
                        if (done.Contains(full)) continue;
                        // 目标：backup\{序号}_{文件夹名}，整个文件夹一起复制
                        string dest = System.IO.Path.Combine(backupDir, $"{lines.Count + 1}_{System.IO.Path.GetFileName(e.FolderPath)}");
                        int n = 1;
                        while (System.IO.Directory.Exists(dest)) {
                            dest = System.IO.Path.Combine(backupDir, $"{lines.Count + 1}_{System.IO.Path.GetFileName(e.FolderPath)} ({n++})");
                        }
                        CopyDirectory(e.FolderPath, dest);
                        done.Add(full);
                        lines.Add(full);
                        added++;
                    } catch { }
                }
                if (added > 0) System.IO.File.WriteAllLines(manifest, lines);
                if (added > 0) Lg.Info($"[LevelLib] 备份完成：新增 {added} 个谱面文件夹（backup 目录）");
            } catch (System.Exception e) {
                Lg.Ex("备份谱面失败", e);
            }
        });
    }

    /// <summary>给目录建议曲名（合格 ogg 名优先，否则文件夹名）</summary>
    public static string SuggestTitle(string dir) {
        string t = PickOggName(dir);
        if (t == null) t = Path.GetFileName(dir);
        return t;
    }

    /// <summary>路径前缀迁移：oldBase 开头的路径换成 newBase；备份位置变更后所有数据记录同步。</summary>
    public static string Repath(string abs, string oldBase, string newBase) {
        if (string.IsNullOrEmpty(abs) || string.IsNullOrEmpty(oldBase)) return abs;
        if (abs.StartsWith(oldBase, System.StringComparison.OrdinalIgnoreCase))
            return System.IO.Path.Combine(newBase, abs.Substring(oldBase.Length).TrimStart('\\', '/'));
        return abs;
    }

    /// <summary>备份位置变更后的全量数据迁移（recorded/plays/deleted/favorites/xacc/标题覆盖/标记）</summary>
    public static void MigrateBackupBase(string oldBase, string newBase) {
        if (string.IsNullOrEmpty(oldBase) || string.IsNullOrEmpty(newBase)
            || string.Equals(System.IO.Path.GetFullPath(oldBase), System.IO.Path.GetFullPath(newBase), System.StringComparison.OrdinalIgnoreCase)) return;
        // recorded
        for (int i = 0; i < RecordedFolders.Count; i++) {
            RecordedFolders[i] = Repath(RecordedFolders[i], oldBase, newBase);
        }
        SaveRecorded();
        // plays
        var newPlays = new Dictionary<string, int>();
        foreach (var kv in _plays) newPlays[Repath(kv.Key, oldBase, newBase)] = kv.Value;
        _plays.Clear();
        foreach (var kv in newPlays) _plays[kv.Key] = kv.Value;
        SavePlays();
        // favorites
        var newFav = new HashSet<string>();
        foreach (var f in _favorites) newFav.Add(Repath(f, oldBase, newBase));
        _favorites.Clear();
        foreach (var f in newFav) _favorites.Add(f);
        SaveFavorites();
        // xaccs
        var newX = new Dictionary<string, float[]>();
        foreach (var kv in _xaccs) newX[Repath(kv.Key, oldBase, newBase)] = kv.Value;
        _xaccs.Clear();
        foreach (var kv in newX) _xaccs[kv.Key] = kv.Value;
        SaveXaccs();
        // titleOverrides
        var newT = new Dictionary<string, string>();
        foreach (var kv in _titleOverrides) newT[Repath(kv.Key, oldBase, newBase)] = kv.Value;
        _titleOverrides.Clear();
        foreach (var kv in newT) _titleOverrides[kv.Key] = kv.Value;
        SaveTitles();
        // 标记
        MarkStore.MigratePath(oldBase, newBase);
        Lg.Info($"[LevelLib] 备份位置迁移: {oldBase} -> {newBase}");
    }

    /// <summary>迁移显示名覆盖记录：原位置路径 -> backup 副本路径；切换备份驱动后旧数据不丢。</summary>
    public static void MigrateTitles() {
        if (string.IsNullOrEmpty(BackupBase)) return;
        var newMap = new Dictionary<string, string>();
        bool changed = false;
        foreach (var kv in _titleOverrides) {
            string bp = ToBackupPath(kv.Key);
            if (bp != null && File.Exists(bp)) {
                newMap[bp] = kv.Value;
                changed = true;
            } else {
                newMap[kv.Key] = kv.Value;
            }
        }
        if (changed) {
            _titleOverrides.Clear();
            foreach (var kv in newMap) _titleOverrides[kv.Key] = kv.Value;
            SaveTitles();
        }
    }

    /// <summary>原位置路径 -> backup 副本路径（按 文件夹名 / 文件名 规则映射）；不在扫描范围的返回 null。</summary>
    public static string ToBackupPath(string originalPath) {
        try {
            if (string.IsNullOrEmpty(originalPath) || string.IsNullOrEmpty(BackupBase)) return null;
            if (originalPath.StartsWith(BackupBase, System.StringComparison.OrdinalIgnoreCase)) return originalPath;   // 已在备份内
            string dir = Path.GetDirectoryName(originalPath);
            string fileName = Path.GetFileName(originalPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName)) return null;
            string dirName = Path.GetFileName(dir);
            return Path.Combine(BackupBase, SanitizeName(dirName), fileName);
        } catch {
            return null;
        }
    }

    /// <summary>递归复制整个文件夹</summary>
    public static void CopyDirectory(string src, string dst) {
        System.IO.Directory.CreateDirectory(dst);
        foreach (var f in System.IO.Directory.GetFiles(src)) {
            System.IO.File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), true);
        }
        foreach (var d in System.IO.Directory.GetDirectories(src)) {
            CopyDirectory(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
        }
    }

}
