using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LevelLibrary;

/// <summary>LevelScanner 的持久化部分：录入记录、寄录图（死点热力图）、完成度、X 精准度、游玩次数。
/// 内存里一律存绝对路径（ToAbs），写盘时一律转相对路径（ToRel）；每个写入函数都立即落盘。</summary>
public static partial class LevelScanner {

    /// <summary>加载自动录入记录（recorded.txt）</summary>
    public static void LoadRecorded() {
        RecordedFolders.Clear();
        if (string.IsNullOrEmpty(RecordedPath) || !File.Exists(RecordedPath)) return;
        try {
            foreach (var line in File.ReadAllLines(RecordedPath)) {
                string t = line.Trim();
                if (t.Length > 0) RecordedFolders.Add(ToAbs(t));
            }
        } catch (System.Exception e) {
            Lg.Ex("加载录入记录失败", e);
        }
    }

    /// <summary>立即保存录入记录（每次录入后调用，防强杀丢数据）</summary>
    public static void SaveRecorded() {
        if (string.IsNullOrEmpty(RecordedPath)) return;
        try {
            File.WriteAllLines(RecordedPath, RecordedFolders.Select(ToRel));
        } catch (System.Exception e) {
            PortalUI.ReportError("保存录入记录失败（下次启动可能漏掉已录入的谱面）", e);
        }
    }

    /// <summary>记录一个自动录入的备份目录并立即写盘</summary>
    public static void AddRecorded(string backupDir) {
        // 顺手把「新导入」标记也摘掉：自动录入的那一份不是玩家导入的，不该带提示色
        ClearNewImport(backupDir);
        if (string.IsNullOrEmpty(backupDir)) return;
        if (!RecordedFolders.Contains(backupDir, System.StringComparer.OrdinalIgnoreCase)) {
            RecordedFolders.Add(backupDir);
            SaveRecorded();
            Lg.Info($"[LevelLib] 录入记录已保存: {backupDir}");
        }
    }

    /// <summary>新导入标记最多留几条：超了从最老的开始丢（这个标记只是提示，不值得无限攒）</summary>
    private const int NewMarkLimit = 400;

    /// <summary>加载「新导入」标记列表（newmarks.txt）。读不出来就当没有，别拿它挡住启动</summary>
    public static void LoadNewMarks() {
        NewImportFolders.Clear();
        if (string.IsNullOrEmpty(NewMarksPath) || !File.Exists(NewMarksPath)) return;
        try {
            foreach (var line in File.ReadAllLines(NewMarksPath)) {
                string t = line.Trim();
                if (t.Length > 0) NewImportFolders.Add(ToAbs(t));
            }
        } catch (System.Exception e) {
            Lg.Ex("加载新导入标记失败", e);
        }
    }

    /// <summary>立即保存「新导入」标记列表（每次变动后调用，防强杀丢数据）</summary>
    public static void SaveNewMarks() {
        if (string.IsNullOrEmpty(NewMarksPath)) return;
        try {
            File.WriteAllLines(NewMarksPath, NewImportFolders.Select(ToRel));
        } catch (System.Exception e) {
            PortalUI.ReportError("保存新导入标记失败（下次启动可能少几个提示色）", e);
        }
    }

    /// <summary>把一个目录记成「新导入的」（导入成功时调用）并立即写盘。
    /// 上限 400 条：再多就把最老的丢掉 —— 这个标记只是提示，攒到几百条就没意义了，
    /// 不设上限的话 settings 之外还会长出一个无限增长的文件。</summary>
    public static void MarkNewImport(string backupDir) {
        if (string.IsNullOrEmpty(backupDir)) return;
        if (!NewImportFolders.Contains(backupDir, System.StringComparer.OrdinalIgnoreCase)) {
            NewImportFolders.Add(backupDir);
            while (NewImportFolders.Count > NewMarkLimit) NewImportFolders.RemoveAt(0);
            SaveNewMarks();
        }
    }

    /// <summary>这个目录是不是刚导入还没点开过（列表行拿它决定要不要给提示色）</summary>
    public static bool IsNewImport(string folderPath) {
        if (string.IsNullOrEmpty(folderPath) || NewImportFolders.Count == 0) return false;
        for (int i = 0; i < NewImportFolders.Count; i++)
            if (string.Equals(NewImportFolders[i], folderPath, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>清掉一个目录的「新导入」标记（玩家点开过这个谱面时调用：提示已经看到过，不必再亮）</summary>
    public static void ClearNewImport(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return;
        int idx = NewImportFolders.FindIndex(d => string.Equals(d, folderPath, System.StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        NewImportFolders.RemoveAt(idx);
        SaveNewMarks();
    }

    /// <summary>从录入记录移除（删除谱面时调用）并立即写盘</summary>
    public static void RemoveRecorded(string backupDir) {
        if (string.IsNullOrEmpty(backupDir)) return;
        int idx = RecordedFolders.FindIndex(d => string.Equals(d, backupDir, System.StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) {
            RecordedFolders.RemoveAt(idx);
            SaveRecorded();
        }
    }

    // 寄录图：谱面 → 长度 10 的计数数组，下标 = 段号（第 i 段 = 曲子进度 i*10%~(i+1)*10%）
    public static void LoadDeathMap(string modPath) {
        _deathMap.Clear();
        _deathMapPath = System.IO.Path.Combine(modPath, "deathmap.txt");
        if (!File.Exists(_deathMapPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_deathMapPath)) {
                var parts = line.Split('\t');
                if (parts.Length < 3) continue;
                string key = ToAbs(parts[0].Trim());
                if (key.Length == 0) continue;
                if (!int.TryParse(parts[1].Trim(), out int seg)) continue;
                if (!int.TryParse(parts[2].Trim(), out int cnt)) continue;
                if (seg < 0 || seg >= 10) continue;
                if (!_deathMap.TryGetValue(key, out var arr)) { arr = new int[10]; _deathMap[key] = arr; }
                arr[seg] = cnt;
            }
        } catch (System.Exception e) {
            Lg.Ex("加载死点热力图失败", e);
        }
    }

    /// <summary>立刻写盘一次（清空 / 单个删除这类一次性操作用，改完立刻落盘）。
    /// 死亡那种高频写入只置脏标记，落盘由 FlushDeathProgress 后台合并，别用这个</summary>
    public static void SaveDeathMap() {
        if (string.IsNullOrEmpty(_deathMapPath)) return;
        _deathMapDirty = false;
        WriteDeathMap(_deathMapPath, _deathMap);
    }

    /// <summary>真写盘：把整张表转成行再落一次。后台线程与主线程共用。
    /// 相对路径在段循环外算一次：一张谱面最多十段，同一个 key 转十次是白转的</summary>
    private static void WriteDeathMap(string path, Dictionary<string, int[]> src) {
        try {
            var lines = new List<string>();
            foreach (var kv in src) {
                string rel = ToRel(kv.Key);
                for (int i = 0; i < 10; i++) {
                    if (kv.Value[i] > 0) lines.Add(rel + "\t" + i + "\t" + kv.Value[i]);
                }
            }
            File.WriteAllLines(path, lines);
        } catch (System.Exception e) {
            Lg.Ex("保存死点热力图失败", e);
        }
    }

    /// <summary>记录一次死亡：当前格子 tileIdx / 总格子 totalTiles → 归入 10% 段。
    /// 完成度另算，存精确百分比（0~100，四舍五入），只升不降。
    /// 两张表都只改内存、置脏标记，落盘由 FlushDeathProgress 后台合并（死亡瞬间不卡主线程；
    /// 退出前 Main 的收尾会强制同步落盘，见 SaveAllNow）</summary>
    public static void RecordDeath(string folderPath, int tileIdx, int totalTiles) {
        if (string.IsNullOrEmpty(folderPath) || totalTiles <= 0 || tileIdx < 0) return;
        int seg = (int)((float)tileIdx / totalTiles * 10f);
        if (seg < 0) seg = 0;
        if (seg > 9) seg = 9;
        string key = ToAbs(folderPath);
        if (!_deathMap.TryGetValue(key, out var arr)) { arr = new int[10]; _deathMap[key] = arr; }
        arr[seg]++;
        _deathMapDirty = true;
        int pct = (int)((float)tileIdx / totalTiles * 100f + 0.5f);
        if (pct < 0) pct = 0; if (pct > 100) pct = 100;
        if (!_progress.TryGetValue(key, out int old) || pct > old) {
            _progress[key] = pct;
            _progressDirty = true;
            // 弹窗开着就实时刷新完成度文本
            try { PortalUI.RefreshCompletion(); } catch { }
        }
    }

    /// <summary>清空前备份单个文件；备份失败返回 false（调用方必须中止清空，否则数据无法恢复）</summary>
    private static bool BackupBeforeClear(string path, string what) {
        try {
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            return true;
        } catch (System.Exception e) {
            PortalUI.ReportError($"清空{what}前备份失败，已中止清空操作（数据未被修改）", e);
            return false;
        }
    }

    /// <summary>清空全部死点热力图 + 完成度（数据管理）</summary>
    public static void ClearDeathMap() {
        if (!BackupBeforeClear(_deathMapPath, "寄录图数据")) return;
        if (!BackupBeforeClear(_progressPath, "完成度数据")) return;
        _deathMap.Clear();
        SaveDeathMap();
        _progress.Clear();
        SaveProgress();
    }

    /// <summary>清空游玩次数 + 完成度（数据管理；不影响通关记录 / 寄录图）</summary>
    public static void ClearPlaysAndProgress() {
        if (!BackupBeforeClear(_playsPath, "游玩次数")) return;
        if (!BackupBeforeClear(_progressPath, "完成度数据")) return;
        _plays.Clear();
        _playsDirty = false;
        SavePlays();
        _progress.Clear();
        SaveProgress();
    }

    /// <summary>取谱面死点热力图（10 段，每 10% 一段，下标即段号；无数据返回 null）</summary>
    public static int[] GetDeathMap(string folderPath) {
        return _deathMap.TryGetValue(ToAbs(folderPath), out var arr) ? arr : null;
    }

    /// <summary>清空某个谱面的寄录图（死点热力图）+ 完成度，只影响这一个谱面；立即写盘</summary>
    public static void ClearDeathMapOf(string folderPath) {
        if (string.IsNullOrEmpty(folderPath)) return;
        string key = ToAbs(folderPath);
        bool changed = _deathMap.Remove(key);
        if (_progress.Remove(key)) changed = true;
        if (!changed) return;
        SaveDeathMap();
        SaveProgress();
        try { PortalUI.RefreshCompletion(); } catch { }
    }

    /// <summary>加载 X 精准度记录（xacc.txt）。每个谱面一个 float[5]：
    /// 0-2 = 宽/标/严 xacc，3 = 纯判定 flag，4 = 全完美 flag（flag 写 "1"/"0"）</summary>
    public static void LoadXaccs(string modPath) {
        _xaccs.Clear();
        _xaccPath = System.IO.Path.Combine(modPath, "xacc.txt");
        if (!File.Exists(_xaccPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_xaccPath)) {
                var parts = line.Split('\t');
                if (parts.Length < 2 || parts[0].Trim().Length == 0) continue;
                var arr = new float[5];   // 槽位含义见 LoadXaccs
                for (int i = 0; i < 3 && i + 1 < parts.Length; i++) {
                    if (float.TryParse(parts[i + 1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) arr[i] = v;
                }
                if (parts.Length >= 5 && parts[4].Trim() == "1") arr[3] = 1f;
                if (parts.Length >= 6 && parts[5].Trim() == "1") arr[4] = 1f;
                _xaccs[ToAbs(parts[0].Trim())] = arr;
            }
        } catch (System.Exception e) {
            Lg.Ex("加载 X 精准度失败", e);
        }
    }

    /// <summary>保存 X 精准度（立即写盘）</summary>
    public static void SaveXaccs() {
        if (string.IsNullOrEmpty(_xaccPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _xaccs) {
                lines.Add(ToRel(kv.Key) + "\t" + kv.Value[0].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + "\t" + kv.Value[1].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + "\t" + kv.Value[2].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + "\t" + (kv.Value[3] >= 1f ? "1" : "0")
                    + "\t" + (kv.Value[4] >= 1f ? "1" : "0"));
            }
            File.WriteAllLines(_xaccPath, lines);
        } catch (System.Exception e) {
            Lg.Ex("保存 X 精准度失败", e);
        }
    }
    /// <summary>统计有 X 精准度记录的谱面数（任意判定槽位 > 0 算一个）</summary>
    public static int CountXaccLevels() {
        int n = 0;
        foreach (var kv in _xaccs) {
            if (kv.Value[0] > 0f || kv.Value[1] > 0f || kv.Value[2] > 0f) n++;
        }
        return n;
    }

    /// <summary>更新某判定难度的 X 精准度（idx = 0 宽 / 1 标 / 2 严），只升不降，立即写盘。
    /// 两个 flag 同样只升不降：pure = 只有完美/稍快稍慢，fullPerfect = 只有完美。</summary>
    public static void UpdateXacc(string folderPath, int idx, float value, bool pure = false, bool fullPerfect = false) {
        folderPath = ToAbs(folderPath);
        if (string.IsNullOrEmpty(folderPath) || value <= 0f || idx < 0 || idx > 2) return;
        if (!_xaccs.TryGetValue(folderPath, out var arr)) {
            arr = new float[5];   // 槽位含义见 LoadXaccs
            _xaccs[folderPath] = arr;
        }
        if (arr[idx] >= value) return;   // 低不覆盖高
        arr[idx] = value;
        if (pure) arr[3] = 1f;   // 一旦纯过就保持
        if (fullPerfect) arr[4] = 1f;   // 一旦全完美过就保持
        SaveXaccs();
        Lg.Info($"[LevelLib] X精准度记录(槽{idx}): {value:F2}% pure={pure} ({folderPath})");
    }

}
