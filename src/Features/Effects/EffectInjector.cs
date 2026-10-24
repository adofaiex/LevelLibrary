using System;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace LevelLibrary.Effects;

/// <summary>去特效的执行端：在游戏加载谱面时，按开关改写 level.json 的 settings / actions / decorations。
/// 注入点与 ychfx 一致（LevelData.LoadLevel 期间拦 RDFile.ReadAllText）——
/// 改的是游戏内加载谱面时的效果，不只是预览。</summary>
public static class EffectInjector {
    /// <summary>当前加载的谱面该用哪份设置。为 null 表示这次不改写。</summary>
    public static EffectState Pending;

    /// <summary>是否正在 LevelData.LoadLevel 期间。只有这时候读到的 json 才是"正要加载的那个谱面"，
    /// 否则字体、皮肤等别的 json 也会被改一遍。</summary>
    public static bool InLoadLevel;

    /// <summary>上一次有没有真的改写过（编辑器存盘前要拿它提示「要保存吗」）。</summary>
    public static bool LastModified { get; set; }

    /// <summary>把一份设置应用到 level.json 上。改过就返回新文本，没改返回 null。</summary>
    public static string ApplyToLevelJson(string json, EffectState st) {
        if (string.IsNullOrEmpty(json) || st == null) return null;
        try {
            var root = JObject.Parse(json);
            bool touched = false;

            var settings = root["settings"] as JObject;
            if (settings != null) {
                foreach (var it in st.Items) {
                    if (it.Category != EffectCategory.Level || !it.On) continue;
                    touched |= ApplySetting(settings, it);
                }
            }

            var actions = root["actions"] as JArray;
            if (actions != null) touched |= RemoveEvents(actions, st, EffectCategory.Event);
            var decorations = root["decorations"] as JArray;
            if (decorations != null) touched |= RemoveEvents(decorations, st, EffectCategory.Decoration);

            if (!touched) return null;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        } catch {
            // 解析不了就放弃改写 —— 宁可不去特效，也不能把谱面弄坏
            return null;
        }
    }

    private static bool ApplySetting(JObject settings, EffectItem it) {
        try {
            if (it.Kind == EffectKind.Off) {
                // 纯开关项：把那个键删掉，游戏就退回默认表现（轨道贴图、背景图、背景视频…）
                if (settings[it.SettingKey] == null) return false;
                settings.Remove(it.SettingKey);
                return true;
            }
            switch (it.Kind) {
                case EffectKind.Text:
                    settings[it.SettingKey] = it.TextValue ?? "";
                    return true;
                case EffectKind.Int:
                    settings[it.SettingKey] = it.IntValue;
                    return true;
                case EffectKind.Double:
                    settings[it.SettingKey] = float.Parse(it.DoubleValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    return true;
                case EffectKind.Bool:
                    settings[it.SettingKey] = it.BoolValue;
                    return true;
                case EffectKind.Position: {
                    var a = new JArray();
                    a.Add(float.Parse(it.XValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                    a.Add(float.Parse(it.YValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                    settings[it.SettingKey] = a;
                    return true;
                }
            }
            return false;
        } catch { return false; }
    }

    private static bool RemoveEvents(JArray arr, EffectState st, EffectCategory cat) {
        bool any = false;
        foreach (var it in st.Items) {
            if (it.Category != cat || !it.On) continue;
            any |= RemoveMatching(arr, it);
        }
        return any;
    }

    private static bool RemoveMatching(JArray arr, EffectItem it) {
        int removed = 0;
        for (int i = arr.Count - 1; i >= 0; i--) {
            var o = arr[i] as JObject;
            if (o == null) continue;
            if (Matches(it, ToDict(o))) { arr.RemoveAt(i); removed++; }
        }
        return removed > 0;
    }

    private static bool Matches(EffectItem it, IDictionary<string, object> d) {
        if (!it.On) return false;
        if (!string.IsNullOrEmpty(it.EventName)) {
            object v;
            if (!d.TryGetValue("eventType", out v)) return false;
            if (!(v is string sv) || sv != it.EventName) return false;
        }
        if (it.Filter != null) {
            try { return it.Filter(d); } catch { return false; }
        }
        return true;
    }

    private static Dictionary<string, object> ToDict(JObject o) {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var p in o.Properties()) {
            object v = p.Value;
            var jv = p.Value as JValue;
            if (jv != null) {
                switch (jv.Type) {
                    case JTokenType.Integer: v = (long)jv.Value; break;
                    case JTokenType.Float: v = (double)jv.Value; break;
                    case JTokenType.Boolean: v = (bool)jv.Value; break;
                    case JTokenType.Null: v = null; break;
                    default: v = jv.Value; break;
                }
            }
            d[p.Name] = v;
        }
        return d;
    }
}

/// <summary>注入补丁的挂载点。挂在游戏自己的类型上 —— 与 ychfx 用同样的两个点。
/// 这两个类不带 [HarmonyPatch] 特性：项目从不做 PatchAll（一个补丁找不到目标就整体抛异常），
/// 全部补丁由 Main 里逐个 PatchSafe 手动挂，名字也照那条路的命名习惯叫。</summary>
public static class EffectInjectorHooks {

    /// <summary>LevelData.LoadLevel 的 Prefix / Finalizer。开窗用 Prefix（先于任何读取），
    /// 关窗用 Finalizer（抛异常时也会关，否则 InLoadLevel 永远为真、后面所有 json 都会被改）。</summary>
    public static class FxLoadLevelHook {
        public static void Prefix() { EffectInjector.InLoadLevel = true; EffectInjector.LastModified = false; }
        public static System.Exception Finalizer(System.Exception __exception) {
            EffectInjector.InLoadLevel = false;
            return __exception;
        }
    }

    /// <summary>RDFile.ReadAllText 的 Postfix：改写返回文本（必须写回 __result，否则改了等于没改）。</summary>
    public static class FxReadAllTextHook {
        public static void Postfix(ref string __result) {
            var st = EffectInjector.Pending;
            if (st == null || !EffectInjector.InLoadLevel) return;
            try {
                var changed = EffectInjector.ApplyToLevelJson(__result, st);
                if (changed != null) {
                    __result = changed;
                    EffectInjector.LastModified = true;
                    Lg.Info("去特效：已改写 level.json");
                }
            } catch { }
        }
    }

    /// <summary>scnEditor.Start 的 Prefix：无论谁开谱面（mod 库 / 双击文件 / 游戏内打开），
    /// 编辑器场景起来之前都按"即将打开的那张谱面"重算一次注入设置 —— 没配单谱面的回落到全局，
    /// 配过的用单谱面的，互相不串台。之前只有 mod 库那一条路发布 Pending：
    /// 其余路径沿用上一次的值（没配过的谱面吃不到全局、上一张的单谱面配置还会串台）。
    /// 只在确有"待打开谱面"时才动 Pending，空编辑器不碰它，免得把刚设好的值冲掉。</summary>
    public static class FxEditorStartHook {
        public static void Prefix() {
            try {
                string p = scnEditor.levelToOpenOnLoad;
                if (string.IsNullOrEmpty(p)) return;
                string full = System.IO.Path.GetFullPath(p);
                EffectInjector.Pending = EffectStore.ResolveFor(LevelScanner.ToRel(System.IO.Path.GetDirectoryName(full)));
            } catch { }
        }
    }
}