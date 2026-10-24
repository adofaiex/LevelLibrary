// [split-of] PortalUI.cs lines 3616-3798
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

/// <summary>PortalUI 的随机进谱面 / 键位提示区刷新 / 删除谱面条目 / 自定义备份目录（partial 类的一部分）</summary>
public static partial class PortalUI {
    private static string _pendingRandomOpen;      // 要打开的谱面路径（null = 没有待进入的）
    private static float _pendingRandomOpenT;      // 已经等了多久（兜底用）
    private static int _pendingRandomOpenMinFrames;// 最少先等几帧（给"选中/详情"落定）
    private static float _pendingRandomReadyT = -1f;// 滚动/详情就绪之后又等了多久
    private const float RandomReadSeconds = 0.2f;  // 就绪之后再等这么久才真的进谱面

    /// <summary>等"滑动 + 详情出现"都完成后再真的进关卡。
    /// 条件：① 滚动定位动画跑完（_scrollAnim == false）② 至少等了 minFrames 帧；
    /// 兜底最多等 1.5 秒（动画真卡住也不能把玩家的按键吞掉）。</summary>
    private static void TickRandomOpen() {
        if (string.IsNullOrEmpty(_pendingRandomOpen)) return;
        _pendingRandomOpenT += Time.unscaledDeltaTime;
        if (_pendingRandomOpenMinFrames > 0) { _pendingRandomOpenMinFrames--; return; }
        bool scrollDone = !_scrollAnim;
        if (!scrollDone && _pendingRandomOpenT < 1.5f) return;
        // 就绪判定不变，只是就绪之后再按住 RandomReadSeconds（从就绪的那一帧开始算，用 unscaled）
        if (_pendingRandomReadyT < 0f) _pendingRandomReadyT = 0f;
        else _pendingRandomReadyT += Time.unscaledDeltaTime;
        if (_pendingRandomReadyT < RandomReadSeconds) return;
        string file = _pendingRandomOpen;
        _pendingRandomOpen = null;
        Lg.Info($"[LevelLib] 随机谱面：动画与详情已就绪（等了 {_pendingRandomOpenT:F2}s）→ 播放进入动画");
        StartEnterTransition(file);   // 随机进谱面也走同一条过渡
    }

    /// <summary>上下键：按当前显示列表顺序移动选中</summary>
    private static void MoveSelection(int dir) {
        var pool = _display.Count > 0 ? _display : LevelScanner.Entries;
        if (pool.Count == 0) return;
        int idx = pool.FindIndex(x => x.FolderPath == _selectedPath);
        if (idx < 0) idx = 0;
        else idx += dir;
        if (idx < 0) idx = pool.Count - 1;
        if (idx >= pool.Count) idx = 0;
        var entry = pool[idx];
        _selectedPath = entry.FolderPath;
        RefreshList();            // 重建（新选中高亮）
        ScrollToEntry(entry, true);   // 滚动到视口中间
        RefreshDetailInfo(entry);
    }

    /// <summary>UMM 的按键捕获成功后调用，刷新界面</summary>
    public static void RaiseCaptureDone(KeyCode k) {
        Lg.Info("RaiseCaptureDone k=" + k);
        RefreshKeysArea();
        RefreshKeyHintBar();
        RefreshSettingsToggles();
        ShowToast(Lang.Tr("已设置：") + KeyDisplay(k) + " 喵");
    }

    /// <summary>刷新快捷键区显示（捕获后更新键名）</summary>
    private static void RefreshKeysArea() {
        if (_settingsKeys == null) return;
        foreach (Transform row in _settingsKeys.transform) {
            if (row.name == "Row_open") {
                var t = row.Find("K");
                if (t != null) t.GetComponent<Text>().text = KeyDisplay(OpenLevelKey);
            } else if (row.name == "Row_random") {
                var t = row.Find("K");
                if (t != null) t.GetComponent<Text>().text = KeyDisplay(RandomLevelKey);
            }
        }
    }

    /// <summary>删除一个谱面（列表+记录；deleteFiles 时连本地文件一起删）。
    /// 返回值 = "这一条已经从谱面库里消失"，与玩家看到的列表一致 —— 文件删不掉只记日志、不改返回值。
    /// 每一步各自兜异常：以前整段共用一个 try，里面任何一步失败都会让函数返回 false，
    /// 于是出现"列表里的谱面明明删掉了、提示却写已删除 0 个"。</summary>
    private static bool DeleteLevelEntry(LevelEntry e, bool deleteFiles) {
        if (e == null) return false;
        // 只有我们自己的副本（备份根里那一份）才连文件一起删。没开导入备份时条目直接指向
        // 玩家的原文件夹，那里删掉就真没了，所以那种条目只从列表移除。
        bool wipeFiles = deleteFiles && LevelScanner.IsOwnCopy(e.FolderPath);
        if (deleteFiles && !wipeFiles) {
            Lg.Info($"[LevelLib] 原位置的谱面只从列表移除（没有备份副本，文件没动）: {e.FolderPath}");
        }
        if (wipeFiles) {
            try {
                bool imported = string.Equals(e.FolderPath, Main.ImportDir, System.StringComparison.OrdinalIgnoreCase);
                if (imported) {
                    if (File.Exists(e.FilePath)) File.Delete(e.FilePath);
                    try { if (Directory.Exists(e.FolderPath) && Directory.GetFiles(e.FolderPath).Length == 0) Directory.Delete(e.FolderPath); } catch (System.Exception ex2) { ReportErrorQuiet("删除后清理空目录失败", ex2); }
                } else {
                    if (Directory.Exists(e.FolderPath)) Directory.Delete(e.FolderPath, true);
                }
            } catch (System.Exception ex) {
                Lg.Ex("删除谱面文件失败（列表照常清掉）: " + e.FolderPath, ex);
            }
        }
        // 列表这一条必须真的消失：Remove 走的是引用相等，而扫描期间列表可能被重建过（对象换了实例），
        // 那时按路径再找一遍，免得留下"文件删了、列表里那条还在"的孤儿行。
        if (!LevelScanner.Entries.Remove(e)) {
            var same = LevelScanner.Entries.FirstOrDefault(x =>
                string.Equals(x.FolderPath, e.FolderPath, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.FilePath, e.FilePath, System.StringComparison.OrdinalIgnoreCase));
            if (same != null) LevelScanner.Entries.Remove(same);
        }
        // 各张记录表各自兜：它们清理失败不该影响"这个谱面已经删了"这个事实
        try { MarkStore.Remove(e.FilePath); } catch (System.Exception ex) { ReportErrorQuiet("清理难度标记失败", ex); }
        try { LevelScanner.RemoveTitleOverride(e.FilePath); } catch (System.Exception ex) { ReportErrorQuiet("清理显示名失败", ex); }
        try { LevelScanner.RemoveRecorded(e.FolderPath); } catch (System.Exception ex) { ReportErrorQuiet("清理录入记录失败", ex); }
        if (_selectedPath == e.FilePath) _selectedPath = null;
        return true;   // 列表已经清掉 = 这一条删成功了（文件那半的失败只记日志）
    }

    /// <summary>自定义备份位置：选文件夹，剪切所有备份过去、迁移数据，以后备份都放那里</summary>
    private static void CustomBackupDir() {
        string newDir = ModernFileDialog.OpenFolder("选择备份文件夹");
        if (string.IsNullOrEmpty(newDir)) return;
        string oldBase = LevelScanner.BackupBase;
        // 同一位置直接确认
        if (!string.IsNullOrEmpty(oldBase) && Directory.Exists(oldBase)
            && string.Equals(System.IO.Path.GetFullPath(oldBase), System.IO.Path.GetFullPath(newDir), System.StringComparison.OrdinalIgnoreCase)) {
            ShowToast(Lang.Tr("备份位置未变化喵"));
            return;
        }
        try {
            Directory.CreateDirectory(newDir);
            if (!string.IsNullOrEmpty(oldBase) && Directory.Exists(oldBase)) {
                // 剪切所有备份子目录到新位置（跨盘失败则复制+删除）
                foreach (var sub in Directory.GetDirectories(oldBase)) {
                    string dest = Path.Combine(newDir, Path.GetFileName(sub));
                    try {
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        Directory.Move(sub, dest);
                    } catch {
                        LevelScanner.CopyDirectory(sub, dest);
                        Directory.Delete(sub, true);
                    }
                }
                // 旧备份根空了就删掉
                try {
                    if (Directory.GetFiles(oldBase).Length == 0 && Directory.GetDirectories(oldBase).Length == 0)
                        Directory.Delete(oldBase);
                } catch { }
                // 迁移所有数据记录的路径（recorded/plays/xacc/收藏/标记等）
                LevelScanner.MigrateBackupBase(oldBase, newDir);
            }
            LevelScanner.BackupBase = newDir;
            LevelScanner.CustomBackupPath = newDir;
            AutoFeatures.Save();
            LevelScanner.Refresh(Main.ImportDir);   // 重建条目（指向新备份位置）
            RefreshList();
            ShowToast(Lang.Tr("备份已移动到新位置喵"));
            Lg.Info($"[LevelLib] 备份位置变更: {oldBase} -> {newDir}");
        } catch (System.Exception e) {
            Lg.Ex("移动备份失败", e);
            ShowToast(Lang.Tr("移动备份失败喵"));
        }
    }
}
