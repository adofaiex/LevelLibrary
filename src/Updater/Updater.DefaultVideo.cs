// Updater 的一部分（同一个 static partial 类）：默认视频背景的按需下载。
// 默认视频不再随资源包分发，「下载默认视频背景」从平台把旧资源包里的那份视频取回来、
// 只解出视频一个文件放进 assets，不装整个包、也不动资源包版本记录。

using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace LevelLibrary;

public static partial class Updater {

    /// <summary>默认视频背景在平台的哪个资源包版本里（这份包里带着视频文件；当前在架的资源包已经不带它了）。</summary>
    private const string DefaultVideoAssetVersion = "0012";

    /// <summary>包内那个视频的文件名（安装路径会剥掉一层 LevelLibrary/，按文件名找就够）。</summary>
    private const string DefaultVideoFileName = "bg_video_default.mp4";

    /// <summary>解出来的文件至少要有这么大才算完整（真实那份约 29MB，这里只是挡"半个文件的空壳"）。</summary>
    private const long DefaultVideoMinBytes = 1L * 1024L * 1024L;

    private static bool _dlvActive;                 // 「下载默认视频背景」这条线在跑（设置行据此显示进度、拦重复点击）
    private static volatile bool _dlvExtractOk;     // 后台线程写完：视频解出来并放到位了没（跨线程读）

    /// <summary>设置页「下载默认视频背景」的入口。更新器只有一个请求槽，同一时刻只跑一条线。</summary>
    public static void DownloadDefaultVideo() {
        try {
            if (_dlvActive) { PortalUI.ShowToast(Lang.Tr("正在下载默认视频背景，稍等喵")); return; }
            if (_req != null || _installing) { PortalUI.ShowToast(Lang.Tr("正在下载或更新中，稍等再试喵")); return; }
            _dlvActive = true;
            _manual = true;
            _allLinesFailed = false;
            NewVersion = ""; NewChangelog = "";
            _pendingDownloadUrl = ""; _pendingFileName = "";
            _jobs.Clear(); _curJob = null; _runNotes.Clear();
            _runDeferred = false; _runFailed = false;
            _runModDone = false; _runAssetDone = false;
            Phase = 1; Progress = 0f;
            Status = "正在获取默认视频背景…";
            Lg.Info("默认视频背景：开始从平台获取（目标资源包 " + DefaultVideoAssetVersion + "）");
            StartGet($"{BaseUrl}/api/mods/{ResourceType}/{Slug}", 1);
        } catch (Exception e) {
            _dlvActive = false;
            Phase = 5; Status = "下载默认视频背景失败";
            Lg.Ex("默认视频背景：启动下载失败", e);
        }
    }

    /// <summary>设置行用：这条线在不在跑。</summary>
    public static bool DownloadingDefaultVideo { get { return _dlvActive; } }

    /// <summary>详情到手时的分支（仅当这条线在跑）：找到目标资源包版本，把它的文件排进下载队列。
    /// 与普通更新不同：不比较版本、不看适配（视频文件是通用的），找到就下。</summary>
    private static void HandleVideoDetail(ModDetail d) {
        ModVersion hit = null;
        if (d != null && d.versions != null) {
            foreach (var v in d.versions) {
                if (v == null || string.IsNullOrEmpty(v.versionId)) continue;
                if (!string.Equals(v.versionId, DefaultVideoAssetVersion, StringComparison.Ordinal)) continue;
                hit = v;
                break;
            }
        }
        if (hit == null || hit.files == null || hit.files.Count == 0) {
            FailVideoDl("平台上没有 " + DefaultVideoAssetVersion + " 资源包");
            return;
        }
        string fid = null;
        foreach (var f in hit.files) {
            if (f != null && !string.IsNullOrEmpty(f.id)) { fid = f.id; break; }
        }
        if (string.IsNullOrEmpty(fid)) { FailVideoDl(DefaultVideoAssetVersion + " 资源包里没有可下载文件"); return; }
        _jobs.Clear();
        _jobs.Add(new UpdateJob { VideoOnly = true, FileId = fid, VersionId = hit.versionId });
        StartNextJob();
    }

    /// <summary>把这条线判定为失败（各路早退共用）：清标志、给一句人话、提示一声。</summary>
    private static void FailVideoDl(string why) {
        _dlvActive = false;
        Phase = 5; Progress = 0f;
        Status = "下载默认视频背景失败：" + why;
        try { Lg.Info("默认视频背景：" + why); } catch { }
        try { PortalUI.ShowToast(Lang.Tr("下载默认视频背景失败，稍后再试喵")); } catch { }
    }

    /// <summary>后台线程：从下载好的压缩包里把视频那一个文件解出来、写到 assets\bg_video_default.mp4。
    /// 不复用资源包安装器：那条会把整个包写盘、还会把「已装 0012」记进资源包版本文件 ——
    /// 而我们只要一个文件，版本记录也必须保持不动（新资源包的更新线不归它管）。</summary>
    private static void DefaultVideoWorker() {
        string modDir = Main.ModEntry.Path;
        string stageDir = Path.Combine(modDir, StageDirName);
        string zipPath = Path.Combine(stageDir, _pendingFileName);
        _dlvExtractOk = false;
        try {
            // 与安装器同一套暂存约定：先把下载到的字节落盘，再从 zip 里取文件
            if (Directory.Exists(stageDir)) { try { Directory.Delete(stageDir, true); } catch { } }
            Directory.CreateDirectory(stageDir);
            File.WriteAllBytes(zipPath, _installData);
            _installProgress = 0.10f;

            string dest = Path.Combine(modDir, "assets", DefaultVideoFileName);
            string destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            string tmp = dest + ".dl";
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

            using (var zip = ZipFile.OpenRead(zipPath)) {
                ZipArchiveEntry entry = null;
                foreach (var e in zip.Entries) {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    if (e.Name.Equals(DefaultVideoFileName, StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
                }
                if (entry == null) throw new Exception("资源包里没有 " + DefaultVideoFileName);
                entry.ExtractToFile(tmp, true);
            }
            _installProgress = 0.75f;

            var fi = new FileInfo(tmp);
            if (!fi.Exists || fi.Length < DefaultVideoMinBytes)
                throw new Exception("解出来的视频不完整（" + (fi.Exists ? fi.Length.ToString() : "0") + " 字节）");
            // 写临时文件、验过了再挪到位：直接写目标路径的话，解压中途失败会留下一份半截的视频，
            // 而"文件在"的判据会让界面显示成下载成功。
            File.Copy(tmp, dest, true);
            try { File.Delete(tmp); } catch { }
            _dlvExtractOk = true;
            InstallLog($"[LevelLib] 默认视频背景：已就位 {fi.Length / 1024 / 1024.0:F1}MB → assets/{DefaultVideoFileName}");
            _installProgress = 1f;
        } catch (Exception e) {
            _installError = true;
            InstallLog("默认视频背景解压失败 " + e.GetType().Name + ": " + e.Message);
            try { File.Delete(zipPath); } catch { }
            try { File.Delete(Path.Combine(modDir, "assets", DefaultVideoFileName + ".dl")); } catch { }
        } finally {
            _installData = null;
            _installT1 = System.Diagnostics.Stopwatch.GetTimestamp();
            _installDone = true;
        }
    }

    /// <summary>主线程收尾（FinishInstall 的这条分支进来）：按结果给界面一句话；
    /// 成功时切到刚下好的默认视频背景。不需要重启（视频文件是运行时读的）。</summary>
    private static void FinishVideoExtract() {
        string ver = _curJob != null ? _curJob.VersionId : "";
        _curJob = null;
        _dlvActive = false;
        try {
            lock (_installMsgs) {
                foreach (var m in _installMsgs) Lg.Info(m);
                _installMsgs.Clear();
            }
        } catch { }
        bool ok = _dlvExtractOk && !_installError;
        if (ok) {
            Phase = 4; Progress = 1f;
            Status = "默认视频背景已下载完成";
            Lg.Info("默认视频背景：下载完成（" + ver + "）");
            try { PortalUI.OnDefaultVideoDownloaded(); } catch (Exception e) {
                Lg.Ex("默认视频背景：切换到它失败", e);
            }
        } else {
            Phase = 5; Progress = 0f;
            Status = "默认视频背景下载失败，看日志";
            try { PortalUI.ShowToast(Lang.Tr("下载默认视频背景失败，稍后再试喵")); } catch { }
        }
    }
}
