using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>批量导入：选文件夹 → 后台扫描（含解压 .zip）→ 确认框 → 每帧导入一个，逐个备份并录入列表。</summary>
public static partial class PortalUI {

    /// <summary>批量导入入口（UMM 设置页按钮 / 详情区按钮）：先弹"选命名规则"框，确认后才打开文件夹选择</summary>
    public static void BatchImportLevels() {
        if (_batchRunning) {
            ShowToast(Lang.Tr("批量导入正在进行中…"));
            return;
        }
        ShowImportNameDialog(() => BatchImportLevelsWithMode());
    }

    /// <summary>设置页那两行与空库那一块的两个按钮共用这两个入口：两处各写一份的话，
    /// 哪天流程改了只会改一处，两个入口的行为就不一样了。</summary>
    private static void StartBatchImportFromUi() {
        try {
            _batchAutoVerifyPending = true;   // 导入完成后自动检验重复谱面
            BatchImportLevels();              // 内部自己会挡「正在导入中」
        } catch (System.Exception ex) { Lg.Ex("批量导入启动失败", ex); }
    }

    /// <summary>单个导入的共用入口（见上）</summary>
    private static void StartSingleImportFromUi() {
        try { ImportLevel(); } catch (System.Exception ex) { Lg.Ex("导入单个谱面启动失败", ex); }
    }

    /// <summary>选完命名规则后真正开始：选文件夹 → 后台扫描 → 逐个导入（进度条）</summary>
    private static void BatchImportLevelsWithMode() {
        if (_batchRunning) {
            ShowToast(Lang.Tr("批量导入正在进行中…"));
            return;
        }
        Lg.Info("批量导入：准备打开文件夹选择…");
        string root;
        try {
            root = PickFolder();
        } catch (System.Exception ex) {
            Lg.Ex("文件夹选择对话框异常", ex);
            return;
        }
        Lg.Info("文件夹选择结果: " + (root ?? "<null/取消>"));
        if (string.IsNullOrEmpty(root)) return;
        _batchRoot = root;
        _batchFolders.Clear();
        _batchIndex = 0;
        _batchTotal = 0;
        _batchRunning = true;
        _batchScanDone = false;
        _batchConfirmShown = false;
        _batchWaitingConfirm = false;
        ShowBatchProgress("正在扫描文件夹…", 0f);
        _batchScanFailed = 0;   // 统计扫描失败的目录数：这些异常若不记账会悄悄漏谱面
        Main.RunBackground("BatchScan", () => {
            try {
                // 根目录本身也要扫（可能有谱面直接放在所选根目录下）
                try {
                    if (Directory.GetFiles(root, "*.adofai").Length > 0) _batchFolders.Add(root);
                } catch (System.Exception e) { _batchScanFailed++; ReportErrorQuiet("批量导入：扫描根目录失败 " + root, e); }
                // 不做深度限制（有人就是想扫全盘），但扫描完始终弹确认框显示范围和数量以防误扫
                System.Collections.Generic.List<string> dirs;
                try {
                    dirs = new System.Collections.Generic.List<string>(Directory.GetDirectories(root, "*", SearchOption.AllDirectories));
                } catch (System.Exception e) {
                    // 顶层遍历失败（权限/路径过长等）：必须让用户知道，否则会以为"扫完了但一个都没有"
                    _batchScanFailed++;
                    ReportError("批量导入：遍历子目录失败，可能有谱面被漏掉\n" + root, e);
                    dirs = new System.Collections.Generic.List<string>();
                }
                foreach (var d in dirs) {
                    string dn = Path.GetFileName(d);
                    // 只精确匹配跳过 backup / BACKUPS：目录名只是"包含"backup 的用户谱面不会被误伤
                    if (dn.Equals("backup", System.StringComparison.OrdinalIgnoreCase)
                        || dn.Equals("BACKUPS", System.StringComparison.OrdinalIgnoreCase)) continue;
                    try {
                        if (Directory.GetFiles(d, "*.adofai").Length > 0) _batchFolders.Add(d);
                    } catch (System.Exception e) { _batchScanFailed++; ReportErrorQuiet("批量导入：扫描子目录失败 " + d, e); }
                }
            } catch (System.Exception e) {
                _batchScanFailed++;
                ReportError("批量导入：扫描阶段出现未预期的错误", e);
            }
            // 压缩包：解压出来的文件夹导入后删掉，压缩包本身不动
            if (BatchImportZips) {
                try { ScanZipsInto(root); } catch (System.Exception ez) {
                    _batchScanFailed++;
                    ReportErrorQuiet("批量导入：解压压缩包阶段出错", ez);
                }
            }
            _batchScanDone = true;
        });
    }

    /// <summary>批量导入扫描阶段失败的目录数（用于在确认框里提醒"有目录没扫到"）</summary>
    private static int _batchScanFailed;

    /// <summary>这次导入里"来自压缩包"的目录：用完要删，所以必须强制备份（见 `TickBatchImport`）</summary>
    private static readonly HashSet<string> _batchZipFolders = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    /// <summary>解压用的临时根目录（收尾时整个删掉；null = 这次没解压）</summary>
    private static string _zipTempRoot;

    /// <summary>把 `root` 目录树里的所有 .zip 解压到系统临时目录，并把其中含 .adofai 的目录加进导入列表。
    /// 解压目标必须放在系统临时目录：放进玩家的谱面目录树会被下一轮扫描扫到，
    /// 而且"导入后删除"会去动玩家的目录。</summary>
    private static void ScanZipsInto(string root) {
        System.Collections.Generic.List<string> zips;
        try { zips = new System.Collections.Generic.List<string>(Directory.GetFiles(root, "*.zip", SearchOption.AllDirectories)); }
        catch (System.Exception e) { ReportErrorQuiet("批量导入：遍历压缩包失败 " + root, e); return; }
        if (zips.Count == 0) { Lg.Info("批量导入：开关开了，但这个目录里没有 .zip"); return; }
        _zipTempRoot = Path.Combine(Path.GetTempPath(), "LevelLib_unzip_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_zipTempRoot);
        int okCount = 0, badCount = 0, found = 0;
        for (int i = 0; i < zips.Count; i++) {
            string z = zips[i];
            string dest = null;
            try {
                string name = SanitizeFileName(Path.GetFileNameWithoutExtension(z));
                if (string.IsNullOrEmpty(name)) name = "zip";
                dest = Path.Combine(_zipTempRoot, (i + 1) + "_" + name);
                Directory.CreateDirectory(dest);
// 同一个 partial class 里那个自定义解压（ExtractZipWithGbkNames）：
      // 不用 ZipFile.ExtractToDirectory 是因为它按 UTF-8 标志位解码文件名，
      // 国内大量 zip 没置那一位 ，所以 中文目录名全变乱码（实测过）。
    ExtractZipWithGbkNames(z, dest);
                okCount++;
            } catch (System.Exception e) {
                badCount++;
                ReportErrorQuiet("批量导入：解压失败（跳过）" + z, e);
                continue;
            }
            // 解压结果里找含 .adofai 的目录：可能是 dest 本身，也可能是子目录（谱面常被打成一层文件夹）
            try {
                if (Directory.GetFiles(dest, "*.adofai").Length > 0) {
                    _batchFolders.Add(dest); _batchZipFolders.Add(dest); found++;
                }
                foreach (var d in Directory.GetDirectories(dest, "*", SearchOption.AllDirectories)) {
                    try {
                        if (Directory.GetFiles(d, "*.adofai").Length > 0) { _batchFolders.Add(d); _batchZipFolders.Add(d); found++; }
                    } catch { }
                }
            } catch { }
        }
        Lg.Info($"[LevelLib] 批量导入：压缩包 {zips.Count} 个（成功解压 {okCount}、失败 {badCount}）"
            + $" → 找到 {found} 个谱面目录 临时目录={_zipTempRoot}");
    }

    /// <summary>导入收尾：删掉解压出来的临时目录（压缩包不动）</summary>
    private static void CleanupZipTemp() {
        if (string.IsNullOrEmpty(_zipTempRoot)) return;
        string t = _zipTempRoot;
        _zipTempRoot = null;
        _batchZipFolders.Clear();
        try {
            if (Directory.Exists(t)) { Directory.Delete(t, true); Lg.Info("批量导入：已删除解压临时目录 " + t + "（压缩包保留）"); }
        } catch (System.Exception e) { Lg.Ex("删除解压临时目录失败: " + t, e); }
    }

    /// <summary>备份前先看磁盘还剩多少（判据：备份盘可用空间 ≥ 这个安全余量，200MB）。
    /// 不预算是因为算"这次要拷多大"要递归遍历源目录（一千个文件夹就是一千次遍历），
    /// 而"盘只剩 200MB"本身就足够作为"别继续了"的理由。
    /// 查不出来（路径怪 / 权限）就当通过：检查本身坏掉不该挡住导入。</summary>
    private const long BackupFreeMargin = 200L * 1024 * 1024;

    /// <summary>这一批里"要备份但备份失败"的个数（不能静默跳过，否则玩家只看到"导入了但列表里没有"）</summary>
    private static int _batchBackupFailed;
    /// <summary>这一批里删掉的原文件夹个数 / 没删掉的个数（删除是破坏性的，必须报清楚）</summary>
    private static int _batchSourceDeleted, _batchSourceDeleteFailed;

    /// <summary>"导入后删除原谱面文件夹"之前的最后一道闸，这些目录一律不删：
    /// 盘符根、一层目录（太浅）、mod 自己的目录 / 备份根 / 游戏目录及它们里面的任何东西、
    /// 系统目录（Windows / Program Files / Program Files (x86)）。
    /// 调用方还会再确认一次"备份真的成功了"（`backupDir != 原目录`），两道闸都过才删；
    /// 任何一条不过就只跳过删除（导入照常完成，不算失败）。查不出来就当不安全 —— 删除不可逆。</summary>
    private static bool SafeToDeleteSource(string dir) {
        try {
            string full = Path.GetFullPath(dir).TrimEnd('\\', '/');
            if (full.Length == 0) return false;
            string root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(full, root.TrimEnd('\\', '/'), System.StringComparison.OrdinalIgnoreCase)) return false;
            if (full.Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries).Length < 2) return false;

            var banned = new System.Collections.Generic.List<string>();
            try { banned.Add(Main.ModEntry.Path); } catch { }
            try { banned.Add(LevelScanner.BackupBase); } catch { }
            try { banned.Add(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."))); } catch { }
            try { banned.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows)); } catch { }
            try { banned.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles)); } catch { }
            try { banned.Add(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86)); } catch { }
            foreach (var b in banned) {
                if (string.IsNullOrEmpty(b)) continue;
                string bb = Path.GetFullPath(b).TrimEnd('\\', '/');
                if (string.Equals(full, bb, System.StringComparison.OrdinalIgnoreCase)) return false;
                if (full.StartsWith(bb + "\\", System.StringComparison.OrdinalIgnoreCase)) return false;   // 在我们自己地盘里
            }
            return true;
        } catch (System.Exception e) {
            Lg.Ex("删除前安全检查失败（按不安全处理 不删）", e);
            return false;
        }
    }

    private static bool CheckBackupSpace(out string why) {
        why = null;
        try {
            string root = Path.GetPathRoot(Path.GetFullPath(LevelScanner.BackupBase));
            var di = new DriveInfo(root);
            long free = di.AvailableFreeSpace;
            if (free < BackupFreeMargin) {
                why = $"备份盘（{root}）空间不足：只剩 {free / 1048576.0:F0}MB"
                    + $"（少于 {BackupFreeMargin / 1048576}MB 就不再往里面备份了）";
                return false;
            }
            return true;
        } catch (System.Exception e) {
            Lg.Ex("备份空间检查失败（按通过处理）", e);
            return true;
        }
    }

    /// <summary>每帧驱动批量导入：扫描完成后逐帧导入一个文件夹，更新进度条</summary>
    private static void TickBatchImport() {
        TickBatchLines();              // 流水行的淡入淡出（跟导入是否还在跑无关）
        if (!_batchRunning) return;
        if (!_batchScanDone) return;   // 还在扫描
        // 扫描完成 → 始终弹确认框（显示扫描范围 + 数量，防止误扫）
        if (!_batchConfirmShown) {
            _batchConfirmShown = true;
            ShowBatchConfirmDialog();
            return;
        }
        // 等待确认（确认框没关就不继续导入）
        if (_batchWaitingConfirm) return;
        if (_batchTotal == 0) _batchTotal = Mathf.Max(1, _batchFolders.Count);
        if (_batchIndex < _batchFolders.Count) {
            string dir = _batchFolders[_batchIndex];
            _batchIndex++;
            // 目录名加父目录前缀，防止不同父目录下的同名文件夹备份冲突（A\song 和 B\song）
            string dirName = Path.GetFileName(dir);
            string parent = Path.GetFileName(Path.GetDirectoryName(dir));
            if (!string.IsNullOrEmpty(parent) && !string.Equals(parent, dirName, System.StringComparison.OrdinalIgnoreCase)) {
                dirName = parent + "_" + dirName;
            }
            try {
                // 来自压缩包的目录一律强制备份：它们在系统临时目录里，而"导入后删除解压文件夹"
                // 会把它们删掉 —— 直接引用原目录的话刚导入的谱面就没了
                bool fromZip = _batchZipFolders.Contains(dir);
                bool wantBackup = AutoFeatures.BackupOnImport || fromZip;
                // 备份前先看盘：不够就中止整批导入（不是跳过这一个，下一个一样会满），
                // 而且只报一次 —— 逐文件夹报错会弹一屏对话框
                if (wantBackup) {
                    string why;
                    if (!CheckBackupSpace(out why)) {
                        _batchRunning = false;
                        CleanupZipTemp();
                        CloseBatchProgress();
                        Lg.Info("批量导入中止：备份盘空间不足 " + why);
                        ReportError("批量导入已中止：" + why
                            + "\n\n可以这样解决：\n"
                            + "· 关掉「谱面导入后备份」（导入弹窗里就有这个开关，或 设置 → 常规 → 导入与存储）\n"
                            + "· 或者把备份位置改到空间更大的盘（同一页的「自定义备份位置」）\n"
                            + "· 也可以先清理备份目录里不再需要的谱面", new System.Exception(why));
                        return;
                    }
                }
                string backupDir = wantBackup ? LevelScanner.EnsureBackup(dir, dirName) : dir;   // 备份关 = 直接用原目录
                if (backupDir == null && wantBackup) {
                    // 备份自己失败了（空间 / 权限 / 文件名非法）：不能静默跳过，
                    // 否则玩家只看到"导入了但列表里没有"；这里计数，收尾时讲明
                    _batchBackupFailed++;
                    Lg.Info("备份失败（已跳过这个谱面）: " + dirName + " ← " + dir);
                }

                // 「导入后删除原谱面文件夹」是破坏性操作，两道闸都过才删：
                // 闸① 备份真的成功了（`backupDir != dir`；备份关着时 backupDir 就是原目录本身，
                // 那时删 = 删掉玩家唯一的副本）；闸② 目录不在禁删名单里。
                // 任何一条不过就只跳过删除，导入本身照常完成（不算失败）。
                if (AutoFeatures.DeleteSourceOnImport && !fromZip && backupDir != null
                    && !string.Equals(backupDir, dir, System.StringComparison.OrdinalIgnoreCase)) {
                    if (SafeToDeleteSource(dir)) {
                        try {
                            Directory.Delete(dir, true);
                            _batchSourceDeleted++;
                            Lg.Info("导入后已删除原文件夹 " + dir);
                        } catch (System.Exception de) {
                            _batchSourceDeleteFailed++;
                            Lg.Ex("删除原文件夹失败（已跳过 不影响导入）" + dir, de);
                        }
                    } else {
                        _batchSourceDeleteFailed++;
                        Lg.Info("原文件夹命中禁删名单，未删除 " + dir);
                    }
                }
                if (backupDir != null) {
                    bool dup = LevelScanner.Entries.Any(x => string.Equals(x.FolderPath, backupDir, System.StringComparison.OrdinalIgnoreCase));
                    if (!dup) {
                        string main = LevelScanner.PickMainFile(backupDir);
                        if (main == null) {
                            var files = Directory.GetFiles(backupDir, "*.adofai");
                            if (files.Length > 0) main = files[0];
                        }
                        string title = TitleByMode(backupDir);   // 按选定的命名规则起名
                        LevelScanner.Entries.Add(new LevelEntry { Title = title, OggName = title, FolderPath = backupDir, FilePath = main });
                        LevelScanner.AddRecorded(backupDir);   // 立即写盘
                        LevelScanner.MarkNewImport(backupDir);   // 「刚导入」提示色：玩家点开过就摘掉
                    }
                }
            } catch (System.OutOfMemoryException ome) {
                // 内存不足单独认出来给一句人话：别让玩家对着 OutOfMemoryException 发呆
                _batchRunning = false;
                CleanupZipTemp();
                CloseBatchProgress();
                Lg.Ex($"批量导入中止：内存不足（{dirName}）", ome);
                ReportError("批量导入已中止：内存不足"
                    + "\n\n· 这次要导入的谱面太多 / 太大了，分几批再来一次\n"
                    + "· 关掉其他占内存的程序也有帮助", ome);
                return;
            } catch (System.Exception e) {
                Lg.Ex($"批量导入失败: {dirName}", e);
            }
            ShowBatchProgress($"正在导入 ({_batchIndex}/{_batchTotal}): {dirName}", (float)_batchIndex / _batchTotal);
            PushBatchLine(dirName);
        } else {
            // 完成：recorded.txt 在导入过程中已逐个写盘；这里只重排列表，不做耗时重扫（防关游戏打断）
            _batchRunning = false;
            CleanupZipTemp();                     // 删解压产生的文件夹（压缩包保留）
            LevelScanner.SaveRecorded();          // 保险再落盘一次
            LevelScanner.SaveNewMarks();          // 同理：新导入标记这一轮积了一堆，合并落一次盘
            LevelScanner.SortEntries();           // 重排（内存已含全部新条目）
            CloseBatchProgress();
            RefreshList();
            string extra = "";
            if (_batchBackupFailed > 0) extra += $"；{_batchBackupFailed} 个没备份成，已跳过";
            if (_batchSourceDeleted > 0) extra += $"；已删除 {_batchSourceDeleted} 个原文件夹";
            if (_batchSourceDeleteFailed > 0) extra += $"；{_batchSourceDeleteFailed} 个原文件夹没删掉（见日志）";
            ShowToast($"批量导入完成：共 {_batchTotal} 个谱面" + (extra.Length > 0 ? extra : "（已保存）"));
            if (_batchBackupFailed > 0)
                Lg.Info($"[LevelLib] 批量导入：有 {_batchBackupFailed} 个谱面备份失败被跳过"
                    + "（多半是磁盘空间或文件名问题 看上面的日志）");
            if (_batchSourceDeleted > 0 || _batchSourceDeleteFailed > 0)
                Lg.Info($"[LevelLib] 批量导入：删原文件夹 {_batchSourceDeleted} 个成功、"
                    + $"{_batchSourceDeleteFailed} 个未删（禁删名单或权限）");
            _batchBackupFailed = 0;
            _batchSourceDeleted = 0;
            _batchSourceDeleteFailed = 0;
            Lg.Info($"[LevelLib] 批量导入完成: {_batchTotal} 个");
            Achievements.TryUnlock("ach_batch");   // 成就：批量导入谱面
            // 导入完成后自动接重复检验（避免"导入了一堆重复谱面却不知道"）
            if (_batchAutoVerifyPending) {
                _batchAutoVerifyPending = false;
                Lg.Info("批量导入完成 → 自动启动重复检验");
                _batchAutoRatePending = true;   // 第三段：评级（一整条链都不用玩家点）
                StartVerify(true);
            }
        }
    }

    /// <summary>重复检验跑完 → 自动接评级（批量导入 → 检验重复 → 评级 这条链的第三段）。
    /// 玩家全程不用做选择：TUF 查得到就用 TUF 的档，查不到就用本地估算，两个都不行就不标。</summary>
    private static void MaybeStartAutoRate() {
        if (!_batchAutoRatePending) return;
        _batchAutoRatePending = false;
        Lg.Info("重复检验完成 → 自动启动谱面评级");
        try { TufBatch.StartAuto(); } catch (System.Exception ex) {
            Lg.Ex("自动评级启动失败", ex);
        }
    }

    // ---------- 进度框里的「刚处理完」几行 ----------
    // 每处理完一个谱面，它的名字从进度条下面冒出来、把上面几行往下顶一格，最下面那行淡出去。
    // 位置交给流式引擎（PortalUI.Flow）：这里只说"谁该在第几格"，一个 anchoredPosition 都不自己写。
    private sealed class BatchLine {
        public Text T;
        public RectTransform Rt;
        public int Slot;            // 现在占第几格（0 = 最上面那格）
        public float Alpha;         // 现在的不透明度（0→1 淡入、推出格子后 1→0 淡出）
        public float Target;
        public bool Dying;
    }

    private static readonly List<BatchLine> _batchLines = new List<BatchLine>();
    private const int BatchLineSlots = 4;      // 露几行（再多框里放不下）
    private const float BatchLineH = 28f;      // 一行高
    private const float BatchLineTop = 78f;    // 第一行距框顶（标题 + 进度条下面）
    private const float BatchLineX = 40f;
    private const float BatchLineW = 560f;     // 640 的框，左右各留 40
    private const float BatchLineFade = MoSmall;
    /// <summary>这几行的字色（比正文浅一档：它们是刚过去的流水，不该比进度数字更响）</summary>
    private static readonly Color BatchLineColor = new Color(0.86f, 0.86f, 0.92f, 1f);

    /// <summary>把"刚处理完的那个谱面"顶进这几行：上面的往下让一格，出格那行淡掉</summary>
    private static void PushBatchLine(string name) {
        if (_batchBoxRt == null || string.IsNullOrEmpty(name)) return;
        for (int i = 0; i < _batchLines.Count; i++) {
            var old = _batchLines[i];
            if (old == null || old.Rt == null || old.Dying) continue;
            old.Slot++;                       // 让位：所有还在格里的行往下挪一格
        }
        var t = MakeText(_batchBoxRt, "Line", FsSmall, BatchLineColor, TextAnchor.MiddleLeft);
        t.text = name;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(BatchLineW, BatchLineH);
        rt.anchoredPosition = new Vector2(BatchLineX, -(BatchLineTop - BatchLineH));   // 先从上一格位置冒头
        _batchLines.Add(new BatchLine { T = t, Rt = rt, Slot = 0, Alpha = 0f, Target = 1f });
        FlowWindow("批量导入", MoSmall);
        ApplyBatchLineSlots();
    }

    /// <summary>按每行现在的格号重算目标位置（走流式让位）；推到格子外面的标记成"淡完就删"</summary>
    private static void ApplyBatchLineSlots() {
        for (int i = 0; i < _batchLines.Count; i++) {
            var ln = _batchLines[i];
            if (ln == null || ln.Rt == null || ln.Dying) continue;
            if (ln.Slot >= BatchLineSlots) {
                ln.Dying = true;
                ln.Target = 0f;
                FlowTo(ln.Rt, BatchLineX, -(BatchLineTop + BatchLineSlots * BatchLineH));
                continue;
            }
            FlowTo(ln.Rt, BatchLineX, -(BatchLineTop + ln.Slot * BatchLineH));
        }
    }

    /// <summary>每帧推一次淡入淡出（位置由流式引擎推，这里只碰颜色里的 alpha）</summary>
    private static void TickBatchLines() {
        if (_batchLines.Count == 0) return;
        float step = Time.unscaledDeltaTime / BatchLineFade;
        for (int i = _batchLines.Count - 1; i >= 0; i--) {
            var ln = _batchLines[i];
            if (ln == null || ln.T == null) { _batchLines.RemoveAt(i); continue; }
            if (Mathf.Abs(ln.Alpha - ln.Target) > 0.001f) {
                ln.Alpha = Mathf.MoveTowards(ln.Alpha, ln.Target, step);
                var c = BatchLineColor;
                c.a = BatchLineColor.a * ln.Alpha;
                ln.T.color = c;
            }
            if (ln.Dying && ln.Alpha <= 0.002f) {
                Object.Destroy(ln.T.gameObject);
                _batchLines.RemoveAt(i);
            }
        }
    }

    /// <summary>批量导入进度界面（进度条 + 刚处理完的几行 + 当前这一条）</summary>
    private static void ShowBatchProgress(string text, float progress) {
        if (_batchCanvas == null) {
            _batchCanvas = MakeCanvas("LevelLib_Batch", 89000);   // 高于引导层(88000)，进度条才可见
            var bg = MakeDialogVeil(_batchCanvas.transform);
            var box = MakeDialogBox(_batchCanvas.transform, "Box", PanelColor);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(640f, 300f);   // 比原来高：下面多了几行流水
            _batchBoxRt = box.rectTransform;
            _batchLines.Clear();
            var title = MakeTitle(box.transform, "Title", 30, new Color(1f, 0.65f, 0.2f, 1f), TextAnchor.MiddleCenter);
            title.text = Lang.Tr("批量导入谱面");
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 0.86f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = Vector2.zero;
            title.rectTransform.offsetMax = Vector2.zero;
            var barBg = MakeImage(box.transform, "BarBg", ItemColor);
            barBg.rectTransform.anchorMin = new Vector2(0.08f, 0.74f);
            barBg.rectTransform.anchorMax = new Vector2(0.92f, 0.80f);
            barBg.rectTransform.offsetMin = Vector2.zero;
            barBg.rectTransform.offsetMax = Vector2.zero;
            var fill = MakeImage(barBg.transform, "Fill", Accent);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            _batchFill = fill;
            var t = MakeText(box.transform, "Text", 24, Color.white, TextAnchor.MiddleCenter);
            t.rectTransform.anchorMin = new Vector2(0.05f, 0.02f);
            t.rectTransform.anchorMax = new Vector2(0.95f, 0.20f);
            t.rectTransform.offsetMin = Vector2.zero;
            t.rectTransform.offsetMax = Vector2.zero;
            _batchText = t;
        }
        _batchText.text = text;
        float barWidth = 0.84f * 640f;
        _batchFill.rectTransform.sizeDelta = new Vector2(barWidth * Mathf.Clamp01(progress), 0f);
    }

    private static void CloseBatchProgress() {
        if (_batchCanvas != null) Object.Destroy(_batchCanvas.gameObject);
        _batchCanvas = null;
        _batchFill = null;
        _batchText = null;
        _batchBoxRt = null;
        _batchLines.Clear();
    }

}
