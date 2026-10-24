using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LevelLibrary.Effects;

/// <summary>去特效的预设管理器。
/// 规则（按用户要求定死）：
///  · 四套内置预设固定、不可改名不可删；玩家可以另存自己的预设。
///  · 预设只记「开了哪些开关」，不记具体值 —— 值在界面上另设。
///  · 多选叠加 = 取并集：某套预设里没开的开关完全不干涉当前状态。
///  · 打开某套预设 → 界面里对应的开关跟着打开；关闭某套 → 只关掉它自己那批。
///  · 手动改了某套预设管的那批开关里的任意一个，那套预设就自动取消勾选。</summary>
public static class EffectPresets {
    /// <summary>预设名（固定，不给改名）。</summary>
    public const string PresetName = "effects.txt";

    private static string _path;

    /// <summary>玩家自建预设：名字 → 该预设开了哪些开关的键。</summary>
    private static readonly Dictionary<string, List<string>> _userPresets =
        new Dictionary<string, List<string>>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, List<string>> UserPresets => _userPresets;

    /// <summary>内置预设的键集合（只读视图，界面与合并逻辑共用一份）。</summary>
    public static string[] BuiltinKeys(int idx) {
        if (idx < 0 || idx >= EffectCatalog.BuiltinPresets.Length) return new string[0];
        return EffectCatalog.BuiltinPresets[idx];
    }

    public static int BuiltinCount => EffectCatalog.BuiltinPresets.Length;
    public static string BuiltinName(int idx) =>
        idx >= 0 && idx < EffectCatalog.BuiltinPresetNames.Length ? EffectCatalog.BuiltinPresetNames[idx] : "预设";

    /// <summary>这套预设（内置或自建）开了哪些键。</summary>
    public static string[] KeysOf(string name) {
        if (string.IsNullOrEmpty(name)) return new string[0];
        for (int i = 0; i < EffectCatalog.BuiltinPresetNames.Length; i++)
            if (EffectCatalog.BuiltinPresetNames[i] == name) return EffectCatalog.BuiltinPresets[i];
        List<string> v;
        return _userPresets.TryGetValue(name, out v) ? v.ToArray() : new string[0];
    }

    // ================= 落盘 =================

    public static void Load(string modPath) {
        _path = Path.Combine(modPath, PresetName);
        _userPresets.Clear();
        if (!File.Exists(_path)) return;
        try {
            var obj = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(_path));
            if (obj == null) return;
            foreach (var kv in obj) {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                // 内置预设名不许被文件覆盖（玩家存坏了也不影响那四套）
                bool builtin = false;
                foreach (var n in EffectCatalog.BuiltinPresetNames) if (n == kv.Key) builtin = true;
                if (builtin) continue;
                _userPresets[kv.Key] = new List<string>(kv.Value);
            }
        } catch (Exception e) {
            Lg.Ex("加载去特效预设失败", e);
        }
    }

    public static void Save() {
        if (string.IsNullOrEmpty(_path)) return;
        try {
            File.WriteAllText(_path, JsonConvert.SerializeObject(_userPresets, Formatting.Indented),
                             System.Text.Encoding.UTF8);
        } catch (Exception e) {
            Lg.Ex("保存去特效预设失败", e);
        }
    }

    public static void SavePreset(string name, IEnumerable<string> keys) {
        if (string.IsNullOrEmpty(name)) return;
        _userPresets[name] = new List<string>(keys ?? new string[0]);
        Save();
    }

    public static void DeletePreset(string name) {
        if (string.IsNullOrEmpty(name) || !_userPresets.Remove(name)) return;
        Save();
    }

    public static bool IsBuiltin(string name) {
        foreach (var n in EffectCatalog.BuiltinPresetNames) if (n == name) return true;
        return false;
    }

    // ================= 叠加与同步 =================

    /// <summary>把勾选的所有预设叠加进状态（取并集）。</summary>
    public static void ApplySelected(EffectState st, IEnumerable<string> selected) {
        if (st == null || selected == null) return;
        foreach (var name in selected) {
            var keys = KeysOf(name);
            if (keys.Length > 0) st.ApplyPreset(keys);
        }
    }

    /// <summary>关掉某套预设：只关它自己那批键，别碰别的预设开的。</summary>
    public static void RemoveOne(EffectState st, string presetName) {
        if (st == null) return;
        var keys = new HashSet<string>(KeysOf(presetName), StringComparer.Ordinal);
        foreach (var it in st.Items) {
            if (keys.Contains(it.Key)) it.On = false;
        }
    }

    /// <summary>判断某套预设此刻是否"全开"（界面高亮用）。
    /// 只要它管的键全都开着就算选中 —— 别的预设也开了同一个也算（取并集的结果）。</summary>
    public static bool IsPresetAllOn(EffectState st, string presetName) {
        if (st == null) return false;
        var keys = KeysOf(presetName);
        if (keys.Length == 0) return false;
        foreach (var k in keys) {
            var it = st.Get(k);
            if (it == null || !it.On) return false;
        }
        return true;
    }

    /// <summary>手动改了某个开关之后调用：把"管着这个开关的预设"从勾选列表里摘掉。
    /// 用户要的行为 —— 改了预设相关的去特效，那套预设就不该再显示为选中。</summary>
    public static List<string> DropPresetsTouching(List<string> selected, string changedKey) {
        if (selected == null || string.IsNullOrEmpty(changedKey)) return selected;
        var outList = new List<string>(selected.Count);
        foreach (var name in selected) {
            bool owns = false;
            foreach (var k in KeysOf(name)) if (k == changedKey) { owns = true; break; }
            if (!owns) outList.Add(name);
        }
        return outList;
    }
}
