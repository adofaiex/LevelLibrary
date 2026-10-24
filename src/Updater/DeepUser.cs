using System;
using System.IO;
using UnityEngine;

namespace LevelLibrary;

/// <summary>「深度用户」成就的计数：连续 1000 次用谱面库打开谱面，期间不能用游戏自己的打开通道。
/// 判定依靠两端的对账：唯一的进关入口 PortalUI.ConfirmOpen 留"这次是我们打开的"标记（记路径 + 时间戳，30 秒有效窗口），
/// AutoFeatures 的 OnEditorStart / OnOpenLevel 是"编辑器真的打开了某个谱面"的检测点，在那里跟标记对账 ——
/// 路径对得上 = 谱面库打开的（连续数 +1），对不上 = 游戏自己打开的（连续数归零）。
/// 只算 .adofai 自制谱，官方关卡不算（谱面库本来也不管官方关卡）。</summary>
public static class DeepUser {

    public const string AchId = "ach_deepuser";
    public const string AchName = "深度用户";
    public const int AchLevel = 5;          // 连续 1000 次极难，与「牢大」「掌中宇宙」同级
    public const int Target = 1000;

    private const float PendingWindow = 30f;   // "我们打开的"标记有效窗口（秒）
    private const float DedupSeconds = 5f;     // 同一路径多久之内不重复计数（两个检测点会各报一次）

    /// <summary>连续用谱面库打开的次数（落盘在 settings.txt 的 DeepUserStreak）</summary>
    public static int Streak;

    private static string _pendingPath;        // 我们刚请求打开的谱面
    private static float _pendingAt;
    private static string _lastCountedPath;    // 去重用
    private static float _lastCountedAt;

    /// <summary>入口①：谱面库要打开一个谱面了（ConfirmOpen 调）→ 留标记</summary>
    public static void NoteLibOpen(string path) {
        try {
            if (string.IsNullOrEmpty(path)) return;
            _pendingPath = path;
            _pendingAt = Time.unscaledTime;
        } catch { }
    }

    /// <summary>入口②：编辑器真的打开了一个谱面（AutoFeatures 的两个补丁调）→ 跟标记对账，决定连续数 +1 还是归零</summary>
    public static void NoteEditorOpened(string path) {
        try {
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase)) return;   // 官方关卡不算
            string full = SafeFull(path);
            float now = Time.unscaledTime;

            bool ours = !string.IsNullOrEmpty(_pendingPath)
                        && (now - _pendingAt) <= PendingWindow
                        && string.Equals(SafeFull(_pendingPath), full, StringComparison.OrdinalIgnoreCase);
            _pendingPath = null;    // 标记只用一次

            if (ours) {
                // 两个检测点（OpenLevel / EditorStart）可能对同一次打开各报一次 → 去重
                if (!string.IsNullOrEmpty(_lastCountedPath)
                    && string.Equals(_lastCountedPath, full, StringComparison.OrdinalIgnoreCase)
                    && (now - _lastCountedAt) <= DedupSeconds) return;
                _lastCountedPath = full; _lastCountedAt = now;

                Streak++;
                bool justDone = false;
                if (Streak >= Target && !Achievements.IsUnlocked(AchId)) {
                    justDone = Achievements.TryUnlock(AchId);
                }
                Save();
                Lg.Info($"[LevelLib] 深度用户：谱面库打开 +1 → 连续 {Streak}/{Target}"
                    + (justDone ? " ★ 达成！" : ""));
            } else {
                // 游戏自己的打开通道（双击 .adofai / 编辑器菜单 / 工坊）→ 连续数归零
                if (Streak != 0) {
                    Lg.Info($"[LevelLib] 深度用户：检测到游戏自己打开谱面（{Path.GetFileName(full)}）"
                        + $" → 连续数从 {Streak} 归零");
                    Streak = 0;
                    Save();
                }
            }
        } catch (Exception e) { Lg.Ex("深度用户计数失败", e); }
    }

    /// <summary>给 AutoFeatures.OnEditorStart 用的安全版：那边 ADOBase.levelPath 可能为空，也可能路径还没就绪</summary>
    public static void NoteEditorOpenedSafe() {
        try {
            string p = AutoFeatures.GetEditorLevelPath();
            if (!string.IsNullOrEmpty(p)) NoteEditorOpened(p);
        } catch { }
    }

    private static string SafeFull(string p) {
        try { return Path.GetFullPath(p); } catch { return p ?? ""; }
    }

    /// <summary>落盘（跟别的设置走同一份 settings.txt）</summary>
    public static void Save() {
        try { AutoFeatures.Save(); } catch { }
    }

    /// <summary>管理员用：复位（写进指令）</summary>
    public static void Reset() {
        Streak = 0; _pendingPath = null; _lastCountedPath = null;
        Save();
    }
}
