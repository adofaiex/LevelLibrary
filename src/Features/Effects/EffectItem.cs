using System;
using System.Collections.Generic;
using System.Globalization;

namespace LevelLibrary.Effects;

/// <summary>去特效项的种类。这个值决定它在 level.json 里怎么改、界面上怎么画。</summary>
public enum EffectKind {
    /// <summary>纯开关：勾上就把对应的键从 settings 里删掉（轨道贴图、背景图、背景视频等）。</summary>
    Off,
    /// <summary>开关 + 一个文本值（颜色、样式名、动画名……）：勾上就把 settings[name] 改成那个值。</summary>
    Text,
    /// <summary>开关 + 一个整数（命中音量、粒度……）。</summary>
    Int,
    /// <summary>开关 + 一个小数（缩放、旋转、辉光强度……）。</summary>
    Double,
    /// <summary>开关 + 一个布尔（脉冲、贴边方块……）。</summary>
    Bool,
    /// <summary>开关 + 一对坐标（位置 X/Y）。</summary>
    Position,
}

/// <summary>去特效的三大类：谱面设置 / 装饰物 / 事件。左侧主 tab 用它。</summary>
public enum EffectCategory {
    /// <summary>谱面设置（改 songPreviewSettings 的值）。</summary>
    Level,
    /// <summary>装饰物（删 decorations 里匹配的事件）。</summary>
    Decoration,
    /// <summary>事件（删 actions 里匹配的事件）。</summary>
    Event,
}

/// <summary>一个去特效项。68 个开关全都是它。</summary>
public sealed class EffectItem {
    /// <summary>稳定的英文键，与 ychfx 保持一致（预设、设置文件都按它索引）。</summary>
    public string Key;
    /// <summary>界面上显示的名字。</summary>
    public string Label;
    public EffectKind Kind;
    public EffectCategory Category;
    /// <summary>子分类的键（谱面设置里的"音乐 / 轨道 / 背景 / 镜头 / 杂项"，事件里的"听觉 / 星球 / 轨道…"）。</summary>
    public string GroupKey;
    public string GroupLabel;

    /// <summary>事件类专用：要删的事件名（AddText / PlaySound / Flash ……）。为空表示整条规则靠 Filter 判。</summary>
    public string EventName;
    /// <summary>事件类专用：附加过滤条件（如只删 objectType 是 Planet 的）。返回 true = 命中（即要删）。</summary>
    public System.Func<IDictionary<string, object>, bool> Filter;

    /// <summary>默认值（未勾选时用它写回去）。</summary>
    public string TextValue = "";
    public int IntValue;
    public double DoubleValue;
    public bool BoolValue;
    public double XValue, YValue;

    /// <summary>这个开关当前是否打开。</summary>
    public bool On;
    /// <summary>值是否被改过（值型项在界面里手动改过就置true；预设只管 On，不管这个）。</summary>
    public bool ValueTouched;

    /// <summary>写进 settings 时用的键。事件/装饰物类没有（它们是删除，不是改写）。</summary>
    public string SettingKey => Key;

    public EffectItem Clone() {
        return (EffectItem)MemberwiseClone();
    }
}

/// <summary>去特效的总开关状态：68 项的开关与值。</summary>
public sealed class EffectState {
    private readonly Dictionary<string, EffectItem> _items = new Dictionary<string, EffectItem>();

    public IReadOnlyCollection<EffectItem> Items => _items.Values;

    public void Add(EffectItem it) { _items[it.Key] = it; }

    public EffectItem Get(string key) {
        EffectItem it;
        return _items.TryGetValue(key, out it) ? it : null;
    }

    /// <summary>有没有任何一项开着（界面上"单独去特效"那个开关要靠它判断要不要生效）。</summary>
    public bool AnyOn {
        get {
            foreach (var kv in _items) if (kv.Value.On) return true;
            return false;
        }
    }

    public EffectState Clone() {
        var s = new EffectState();
        foreach (var kv in _items) s.Add(kv.Value.Clone());
        return s;
    }

    /// <summary>把预设叠加进来：预设里开了的项置开；预设里没开的项一律不碰。
    /// 这是用户要的行为——"只看开了的开关，关了的不覆盖"。</summary>
    public void ApplyPreset(IEnumerable<string> enabledKeys) {
        if (enabledKeys == null) return;
        foreach (var k in enabledKeys) {
            var it = Get(k);
            if (it != null) it.On = true;
        }
    }

    /// <summary>预设全选时用它：只把预设列出的项打开，其余保持原样。</summary>
    public void SyncPresetCheck(IEnumerable<string> enabledKeys) {
        var set = new HashSet<string>(enabledKeys ?? new string[0]);
        foreach (var kv in _items) {
            // 只同步"这一套预设该管的项"；不在预设范围内的项由别的预设或手动管
            if (set.Contains(kv.Key)) kv.Value.On = true;
        }
    }
}