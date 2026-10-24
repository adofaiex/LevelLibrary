using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LevelLibrary.Effects;

/// <summary>一个谱面的去特效配置（存哪些开关开了、那些值是多少）。</summary>
public sealed class EffectProfile {
    /// <summary>单独去特效的总开关（默认关）。</summary>
    public bool Enabled;
    /// <summary>开了哪些开关的键。</summary>
    public List<string> OnKeys = new List<string>();
    /// <summary>被手动改过值的项：键 → 值的 json 片段。</summary>
    public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>去特效的落盘：全局一份、每个谱面一份。
/// 用户定的优先级：单谱面配过就只听单谱面的，没配过才用全局。</summary>
public static class EffectStore {
    private const string GlobalFile = "effects_global.json";
    private const string PerLevelDir = "effects_levels";

    /// <summary>全局去特效（设置页那个，默认关闭）。</summary>
    public static EffectProfile Global = new EffectProfile();
    /// <summary>按谱面文件夹路径索引：key 用绝对路径的 ToRel（跟别的数据文件同一套，谱面目录搬家不丢）。</summary>
    private static readonly Dictionary<string, EffectProfile> _perLevel =
        new Dictionary<string, EffectProfile>(StringComparer.OrdinalIgnoreCase);

    private static string _globalPath;
    private static string _levelDir;

    public static IReadOnlyDictionary<string, EffectProfile> PerLevel => _perLevel;

    public static void Load(string modPath) {
        _globalPath = Path.Combine(modPath, GlobalFile);
        _levelDir = Path.Combine(modPath, PerLevelDir);
        Global = ReadProfile(_globalPath) ?? new EffectProfile();
        _perLevel.Clear();
        try {
            if (!Directory.Exists(_levelDir)) return;
            PurgeLegacyLayout(_levelDir);
            // 必须递归：单谱面配置的键是 relPath（形如 目录/子目录/谱面），文件名由键编码而来（见 EncodeKey），
            // relPath 里的斜杠会变成子目录。之前用非递归 GetFiles + 取文件名当键，写进去读不回来。
            foreach (var f in Directory.GetFiles(_levelDir, "*.json", SearchOption.AllDirectories)) {
                try {
                    var p = ReadProfile(f);
                    if (p == null) continue;
                    string key = DecodeKey(Path.GetFileNameWithoutExtension(f));
                    if (!string.IsNullOrEmpty(key)) _perLevel[key] = p;
                } catch { }
            }
        } catch { }
    }

    /// <summary>旧版把 relPath 直接拼成路径，写进了子目录里、键又取的是文件名，读出来全是废数据。
    /// 这里把这些子目录整个清掉：编码前的键一定带分隔符，落在子目录里的文件必然是旧版留下的。</summary>
    private static void PurgeLegacyLayout(string dir) {
        try {
            foreach (var sub in Directory.GetDirectories(dir)) {
                try { Directory.Delete(sub, true); } catch { }
            }
        } catch { }
    }

    /// <summary>谱面键（relPath，含分隔符）→ 落盘用的文件名：斜杠与盘符冒号换成不会出现在文件名里的字符，
    /// 并保证可逆（读回来要还原成同一个键，否则谱面换个层级就读不到自己的配置）。</summary>
    private static string EncodeKey(string rel) {
        // 先把 % 编码掉，否则后面替换出来的 %5C / %3A 会被再次当成 % 处理，解不回去。
        return rel.Replace("%", "%25").Replace("\\", "%5C").Replace(":", "%3A");
    }

    private static string DecodeKey(string file) {
        return file.Replace("%25", "%").Replace("%3A", ":").Replace("%5C", "\\");
    }

    private static EffectProfile ReadProfile(string path) {
        try {
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<EffectProfile>(File.ReadAllText(path));
        } catch { return null; }
    }

    private static void WriteProfile(string path, EffectProfile p) {
        try {
            var d = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
            File.WriteAllText(path, JsonConvert.SerializeObject(p, Formatting.Indented), System.Text.Encoding.UTF8);
        } catch (Exception e) {
            Lg.Ex("保存去特效配置失败", e);
        }
    }

    public static void SaveGlobal() {
        if (string.IsNullOrEmpty(_globalPath)) return;
        WriteProfile(_globalPath, Global);
    }

    public static void SaveLevel(string relPath) {
        if (string.IsNullOrEmpty(_levelDir) || string.IsNullOrEmpty(relPath)) return;
        EffectProfile p;
        if (!_perLevel.TryGetValue(relPath, out p)) return;
        WriteProfile(Path.Combine(_levelDir, EncodeKey(relPath) + ".json"), p);
    }

    public static EffectProfile GetLevel(string relPath) {
        EffectProfile p;
        if (!string.IsNullOrEmpty(relPath) && _perLevel.TryGetValue(relPath, out p)) return p;
        return null;
    }

    public static void SetLevel(string relPath, EffectProfile p) {
        if (string.IsNullOrEmpty(relPath) || p == null) return;
        _perLevel[relPath] = p;
        SaveLevel(relPath);
    }

    /// <summary>取这张谱面该用的配置：配过就只听它的，没配过才用全局。
    /// 返回值是"已应用好开关"的 EffectState，交给注入端。</summary>
    public static EffectState ResolveFor(string relPath) {
        var prof = GetLevel(relPath);
        if (prof == null || !prof.Enabled) {
            // 这张谱面没配过（或配了但没开单独去特效）→ 用全局
            if (Global != null && Global.Enabled) return BuildState(Global);
            return null;
        }
        return BuildState(prof);
    }

    /// <summary>按一份配置把 68 项铺开成可用的状态。</summary>
    public static EffectState BuildState(EffectProfile p) {
        var st = EffectCatalog.Build();
        if (p == null) return st;
        var on = new HashSet<string>(p.OnKeys ?? new List<string>(), StringComparer.Ordinal);
        foreach (var it in st.Items) if (on.Contains(it.Key)) it.On = true;
        foreach (var kv in p.Values) {
            var it = st.Get(kv.Key);
            if (it == null) continue;
            try {
                switch (it.Kind) {
                    case EffectKind.Text: it.TextValue = kv.Value ?? ""; break;
                    case EffectKind.Int: int.TryParse(kv.Value, out it.IntValue); break;
                    case EffectKind.Double: double.TryParse(kv.Value, out it.DoubleValue); break;
                    case EffectKind.Bool: bool.TryParse(kv.Value, out it.BoolValue); break;
                    case EffectKind.Position: {
                        // 位置存成 "x,y"
                        var parts = (kv.Value ?? "").Split(',');
                        if (parts.Length == 2) {
                            double.TryParse(parts[0], out it.XValue);
                            double.TryParse(parts[1], out it.YValue);
                        }
                        break;
                    }
                }
                it.ValueTouched = true;
            } catch { }
        }
        return st;
    }

    /// <summary>把界面上的状态收回一份配置（只有"开了的"和"改过值的"会写进去）。</summary>
    public static EffectProfile ToProfile(EffectState st, bool enabled) {
        var p = new EffectProfile { Enabled = enabled };
        foreach (var it in st.Items) {
            if (it.On) p.OnKeys.Add(it.Key);
            if (!it.ValueTouched) continue;
            switch (it.Kind) {
                case EffectKind.Text: p.Values[it.Key] = it.TextValue ?? ""; break;
                case EffectKind.Int: p.Values[it.Key] = it.IntValue.ToString(); break;
                case EffectKind.Double: p.Values[it.Key] = it.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case EffectKind.Bool: p.Values[it.Key] = it.BoolValue ? "true" : "false"; break;
                case EffectKind.Position: p.Values[it.Key] = it.XValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                                                 it.YValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
            }
        }
        return p;
    }
}