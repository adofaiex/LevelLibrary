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

/// <summary>重复谱面检验：后台线程按 adofai 文件哈希找重复，主线程 Tick 里应用删除。</summary>
public static partial class PortalUI {

    /// <summary>启动重复谱面检验</summary>
    private static void StartVerify() { StartVerify(false); }

    /// <summary>启动重复谱面检验；auto=true = 由批量导入完成后自动触发</summary>
    private static void StartVerify(bool auto) {
        if (_verifyRunning) { ShowToast(Lang.Tr("检验进行中喵")); return; }
        _verifyGen++;   // 新代际：作废可能残留的旧线程结果
        int gen = _verifyGen;
        _verifyRunning = true;
        _verifyCancel = false;
        _verifyApplyPending = false;
        _verifyResults = null;
        _verifyAuto = auto;
        ShowVerifyProgress();
        if (auto) ShowToast(Lang.Tr("批量导入完成，正在自动检验重复谱面…"));
        Lg.Info($"[LevelLib] 开始重复检验: 谱面数={LevelScanner.Entries.Count} 代际={gen} 自动={auto}");
        Main.RunBackground("VerifyDuplicates", () => VerifyLoop(gen));
    }

    /// <summary>后台线程：按 adofai 哈希找重复；代际变了或取消就直接停，结果只提交给主线程</summary>
    private static void VerifyLoop(int gen) {
        try {
            var entries = new List<LevelEntry>(LevelScanner.Entries);
            // 主谱面（目录名不含"副本/copy"）排前面：同哈希多份时保留主谱面，副本才标重复
            entries.Sort((a, b) => (IsCopyDir(a.FolderPath) ? 1 : 0) - (IsCopyDir(b.FolderPath) ? 1 : 0));
            // 逐个谱面算 adofai 文件的 SHA256：同哈希 = 同一份谱面（无视文件名/文件夹名/文件数）
            var hashFirst = new Dictionary<string, LevelEntry>();   // 哈希 → 第一个拥有它的谱面
            var dupes = new List<LevelEntry>();
            var dupSet = new HashSet<LevelEntry>();
            for (int i = 0; i < entries.Count; i++) {
                if (_verifyGen != gen || _verifyCancel) return;   // 取消 / 代际失效：直接停
                var e = entries[i];
                try {
                    if (Directory.Exists(e.FolderPath)) {
                        foreach (var h in AdofaiHashes(e.FolderPath)) {
                            if (!hashFirst.TryGetValue(h, out var first)) hashFirst[h] = e;
                            else if (first != e && dupSet.Add(e)) dupes.Add(e);   // 哈希已有人 → 当前是重复（保留先出现的）
                        }
                    }
                } catch { }
                _verifyProgress = (float)(i + 1) / System.Math.Max(1, entries.Count);
            }
            if (_verifyGen != gen || _verifyCancel) return;
            // 代际仍有效才提交结果（旧线程的结果丢弃）
            if (_verifyGen == gen) {
                _verifyResults = dupes;
                _verifyApplyPending = true;   // 交给主线程 Tick 应用删除
            }
        } catch (System.Exception e) {
            Lg.Ex("重复检验失败", e);
            _verifyRunning = false;   // 异常：让主线程兜底关窗
        }
    }

    /// <summary>主线程 Tick 驱动：更新进度，扫描完成后应用删除</summary>
    private static void TickVerify() {
        if (!_verifyRunning) {
            // 后台已退出（取消 / 异常）→ 收尾关窗。
            // 这条自动链也到此为止：别把"检验完还要评级"的记号留到下一次手动检验去。
            _batchAutoRatePending = false;
            if (_verifyProgressWin != null) {
                CloseVerifyProgress();
                ShowToast(Lang.Tr("检验已停止喵"));
            }
            return;
        }
        // 后台扫描中：更新进度条
        if (_verifyFill != null) _verifyFill.fillAmount = _verifyProgress;
        if (_verifyInfo != null) _verifyInfo.text = (int)(_verifyProgress * 100f) + "%";
        // 扫描完成 → 关进度窗，直接删除重复谱面（不弹选择窗）
        if (_verifyApplyPending && _verifyResults != null) {
            _verifyApplyPending = false;
            _verifyRunning = false;
            CloseVerifyProgress();
            int n = _verifyResults.Count;
            Lg.Info($"[LevelLib] 检验完成: 重复 {n} 个 → 直接删除（不弹选择窗）");
            if (n == 0) {
                ShowToast(_verifyAuto ? "重复检验完成：没有发现重复谱面喵" : "没有发现重复谱面喵");
                MaybeStartAutoRate();     // 自动链的第三段：不管有没有重复，检验走完就接着评级
                return;
            }
            int deleted = 0, fail = 0, kept = 0;
            int before = LevelScanner.Entries.Count;
            foreach (var e in _verifyResults) {
                // 只有我们自己的副本才连文件一起删：没开导入备份的条目指向玩家原文件夹，
                // 那种"重复"只从列表移除，磁盘上那份一个字都不动。
                bool own = LevelScanner.IsOwnCopy(e.FolderPath);
                if (DeleteLevelEntry(e, own)) {
                    deleted++;
                    if (!own) kept++;
                    Lg.Info($"[LevelLib] 重复谱面已删除: {e.Title}（{e.FolderPath}）"
                        + (own ? "" : " ← 原位置，文件保留"));
                } else fail++;
            }
            // 兜底校正：提示里的数字必须等于"玩家看到列表少了几个"。
            // 删除函数的返回值曾经会因为记录表清理失败而变成 false，于是列表空了、提示还是 0。
            int gone = before - LevelScanner.Entries.Count;
            if (gone > deleted) {
                int fixUp = gone - deleted;      // 返回值说失败、列表其实少了 → 按列表算
                deleted = gone;
                fail = System.Math.Max(0, fail - fixUp);
            }
            RefreshList();
            LevelScanner.SaveRecorded();
            string msg = $"重复检验完成：已删除 {deleted} 个重复谱面";
            if (kept > 0) msg += $"，另有 {kept} 个在原位置（只从列表移除，文件没动）";
            if (fail > 0) msg += $"，失败 {fail} 个（文件可能被占用）";
            ShowToast(msg);
            Lg.Info($"[LevelLib] 重复谱面已删除: 成功 {deleted} 失败 {fail} 保留文件 {kept}");
            _verifyResults = null;
            MaybeStartAutoRate();         // 自动链的第三段：检验走完就接着评级
        }
    }

}
