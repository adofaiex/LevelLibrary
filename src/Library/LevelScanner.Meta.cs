using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LevelLibrary;

/// <summary>LevelScanner partial module: Meta</summary>
public static partial class LevelScanner {

    /// <summary>加载显示名覆盖表：每行「路径\t显示名」，路径统一转绝对路径后当 key</summary>
    public static void LoadTitles(string modPath) {
        _titlePath = System.IO.Path.Combine(modPath, "titles.txt");
        _titleOverrides.Clear();
        try {
            if (!System.IO.File.Exists(_titlePath)) return;
            foreach (var line in System.IO.File.ReadAllLines(_titlePath)) {
                int idx = line.IndexOf('\t');
                if (idx > 0) {
                    string fp = line.Substring(0, idx).Trim();
                    string t = line.Substring(idx + 1).Trim();
                    if (fp.Length > 0 && t.Length > 0) _titleOverrides[ToAbs(fp)] = t;
                }
            }
        } catch { }
    }

    private static void SaveTitles() {
        if (string.IsNullOrEmpty(_titlePath)) {
            Lg.Info("警告：titles.txt 路径未初始化，显示名未保存！");
            return;
        }
        try {
            var lines = new List<string>();
            foreach (var kv in _titleOverrides) lines.Add(ToRel(kv.Key) + "\t" + kv.Value);
            System.IO.File.WriteAllLines(_titlePath, lines);
        } catch (System.Exception e) {
            PortalUI.ReportError("保存谱面显示名失败", e);
        }
    }

    /// <summary>设置显示名覆盖（重命名）；title 为空 = 删除该条覆盖</summary>
    public static void SetTitleOverride(string filePath, string title) {
        filePath = ToAbs(filePath);   // 防御：key 统一绝对路径
        if (string.IsNullOrEmpty(filePath)) return;
        if (string.IsNullOrEmpty(title)) _titleOverrides.Remove(filePath);
        else _titleOverrides[filePath] = title;
        SaveTitles();
    }

    /// <summary>删除谱面时清理覆盖记录（key 须为绝对路径，这里不转 ToAbs）
    /// 复核后确认不转是对的：调用点传的 LevelEntry.FilePath 恒为绝对路径（Directory.GetFiles 产出），
    /// 而 ToAbs 对已 rooted 的路径原样返回，转了也不改变结果。</summary>
    public static void RemoveTitleOverride(string filePath) {
        if (filePath != null && _titleOverrides.Remove(filePath)) SaveTitles();
    }

    public static void LoadNotes(string modPath) {
        _notes.Clear();
        _notesPath = System.IO.Path.Combine(modPath, "notes.txt");
        if (!File.Exists(_notesPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_notesPath)) {
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string key = ToAbs(line.Substring(0, tab).Trim());
                if (key.Length == 0) continue;
                _notes[key] = line.Substring(tab + 1);
            }
        } catch (System.Exception e) {
            Lg.Ex("加载备注失败", e);
        }
    }

    public static void SaveNotes() {
        if (string.IsNullOrEmpty(_notesPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _notes) lines.Add(ToRel(kv.Key) + "\t" + kv.Value);
            File.WriteAllLines(_notesPath, lines);
        } catch (System.Exception e) {
            PortalUI.ReportError("保存备注失败（备注可能丢失）", e);
        }
    }

    public static string GetNote(string filePath) {
        if (string.IsNullOrEmpty(filePath)) return null;
        return _notes.TryGetValue(ToAbs(filePath), out var v) ? v : null;
    }

    public static void SetNote(string filePath, string note) {
        if (string.IsNullOrEmpty(filePath)) return;
        _notes[ToAbs(filePath)] = note ?? "";
        SaveNotes();
    }

    /// <summary>加载收藏（favorites.txt，每行一个绝对路径）</summary>
    public static void LoadFavorites(string modPath) {
        _favorites.Clear();
        _favoritesPath = System.IO.Path.Combine(modPath, "favorites.txt");
        if (!File.Exists(_favoritesPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_favoritesPath)) {
                string t = line.Trim();
                if (t.Length > 0) _favorites.Add(ToAbs(t));
            }
        } catch (System.Exception e) {
            Lg.Ex("加载收藏失败", e);
        }
    }

    /// <summary>保存收藏；每次改动都立即写盘</summary>
    public static void SaveFavorites() {
        if (string.IsNullOrEmpty(_favoritesPath)) return;
        try {
            File.WriteAllLines(_favoritesPath, _favorites.Select(ToRel));
        } catch (System.Exception e) {
            Lg.Ex("保存收藏失败", e);
        }
    }

    /// <summary>切换收藏，返回切换后的状态；立即写盘</summary>
    public static bool ToggleFavorite(string folderPath) {
        folderPath = ToAbs(folderPath);
        if (string.IsNullOrEmpty(folderPath)) return false;
        bool now;
        if (_favorites.Remove(folderPath)) now = false;
        else { _favorites.Add(folderPath); now = true; }
        SaveFavorites();
        return now;
    }

    /// <summary>是否收藏</summary>
    public static bool IsFavorite(string folderPath) {
        return !string.IsNullOrEmpty(folderPath) && _favorites.Contains(ToAbs(folderPath));
    }

}
