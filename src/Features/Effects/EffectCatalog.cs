using System.Collections.Generic;

namespace LevelLibrary.Effects;

/// <summary>去特效的全部 70 个开关（清单）。分类与分组照 ychfx 的界面结构搬过来。
/// 分组键：谱面设置 = music / track / background / camera / misc；
/// 装饰物 = deco；事件 = auditory / planet / track / deco / visual / effect / misc。</summary>
public static class EffectCatalog {
    // 分组的中文名（界面上写的小分类标签）
    public const string GMusic = "音乐";
    public const string GTrack = "轨道";
    public const string GBackground = "背景";
    public const string GCamera = "镜头";
    public const string GMisc = "杂项";
    public const string GDeco = "装饰物";
    public const string GAuditory = "听觉事件";
    public const string GPlanet = "星球事件";
    public const string GEventTrack = "轨道事件";
    public const string GEventDeco = "装饰事件";
    public const string GVisual = "视觉杂项";
    public const string GEffect = "特效事件";
    public const string GEventMisc = "其他事件";

    /// <summary>分类的显示名与它包含的分组（左侧主 tab / 上方子 tab）。</summary>
    public static readonly string[] CategoryNames = { "谱面设置", "装饰物", "事件" };

    public static string[] GroupsOf(EffectCategory cat) {
        switch (cat) {
            case EffectCategory.Level:
                return new[] { GMusic, GTrack, GBackground, GCamera, GMisc };
            case EffectCategory.Decoration:
                return new[] { GDeco };
            default:
                return new[] { GAuditory, GPlanet, GEventTrack, GEventDeco, GVisual, GEffect, GEventMisc };
        }
    }

    private static void Off(EffectState s, string key, string label, EffectCategory cat, string group) {
        s.Add(new EffectItem { Key = key, Label = label, Kind = EffectKind.Off, Category = cat, GroupKey = group, GroupLabel = group });
    }

    private static void Txt(EffectState s, string key, string label, EffectCategory cat, string group, string def) {
        s.Add(new EffectItem { Key = key, Label = label, Kind = EffectKind.Text, Category = cat, GroupKey = group, GroupLabel = group, TextValue = def });
    }

    private static void Num(EffectState s, string key, string label, EffectCategory cat, string group, int def) {
        s.Add(new EffectItem { Key = key, Label = label, Kind = EffectKind.Int, Category = cat, GroupKey = group, GroupLabel = group, IntValue = def });
    }

    private static void Dbl(EffectState s, string key, string label, EffectCategory cat, string group, double def) {
        s.Add(new EffectItem { Key = key, Label = label, Kind = EffectKind.Double, Category = cat, GroupKey = group, GroupLabel = group, DoubleValue = def });
    }

    private static void Bl(EffectState s, string key, string label, EffectCategory cat, string group, bool def) {
        s.Add(new EffectItem { Key = key, Label = label, Kind = EffectKind.Bool, Category = cat, GroupKey = group, GroupLabel = group, BoolValue = def });
    }

    private static void Pos(EffectState s, string key, string label, EffectCategory cat, string group) {
        s.Add(new EffectItem { Key = key, Label = label, Kind = EffectKind.Position, Category = cat, GroupKey = group, GroupLabel = group });
    }

    /// <summary>事件类：删掉 matching 的事件。</summary>
    private static void Evt(EffectState s, string key, string label, EffectCategory cat, string group,
                             string eventName, System.Func<IDictionary<string, object>, bool> filter = null) {
        s.Add(new EffectItem {
            Key = key, Label = label, Kind = EffectKind.Off, Category = cat,
            GroupKey = group, GroupLabel = group, EventName = eventName, Filter = filter,
        });
    }

    private static bool IsStr(IDictionary<string, object> d, string key, string val) {
        object o;
        return d.TryGetValue(key, out o) && o is string sv && sv == val;
    }
    private static bool IsNotStr(IDictionary<string, object> d, string key, string val) {
        object o;
        return !(d.TryGetValue(key, out o) && o is string sv && sv == val);
    }

    /// <summary>造一份完整的 70 项。</summary>
    public static EffectState Build() {
        var s = new EffectState();

        // ============ 一、谱面设置（改 settings 的值）============
        var L = EffectCategory.Level;
        // 音乐
        Txt(s, "hitsound", "打拍声", L, GMusic, "Kick");
        Num(s, "hitsoundVolume", "打拍声音量", L, GMusic, 100);
        // 轨道
        Off(s, "trackTexture", "轨道纹理", L, GTrack);
        Txt(s, "trackColorType", "轨道颜色类型", L, GTrack, "Single");
        Txt(s, "trackStyle", "轨道风格", L, GTrack, "Standard");
        Txt(s, "trackColor", "轨道主色调", L, GTrack, "DEBB7B");
        Txt(s, "secondaryTrackColor", "轨道副色调", L, GTrack, "FFFFFF");
        Dbl(s, "trackColorAnimDuration", "色彩动画间隔", L, GTrack, 2);
        Dbl(s, "trackGlowIntensity", "辉度", L, GTrack, 100);
        Txt(s, "trackAnimation", "轨道出现动画", L, GTrack, "None");
        Dbl(s, "beatsAhead", "动画前节拍", L, GTrack, 3);
        Txt(s, "trackDisappearAnimation", "轨道消失动画", L, GTrack, "None");
        Dbl(s, "beatsBehind", "动画后节拍", L, GTrack, 4);
        // 背景
        Off(s, "bgImage", "背景图片", L, GBackground);
        Txt(s, "backgroundColor", "背景颜色", L, GBackground, "000000");
        Bl(s, "showDefaultBGTile", "自动方块", L, GBackground, true);
        Bl(s, "showDefaultBGIfNoImage", "无背景图时显示自动方块", L, GBackground, true);
        Txt(s, "defaultBGTileColor", "自动方块颜色", L, GBackground, "101121");
        Txt(s, "defaultBGShapeType", "教程背景图形", L, GBackground, "Disabled");
        Txt(s, "defaultBGShapeColor", "教程背景图形颜色", L, GBackground, "FFFFFF");
        // 镜头
        Txt(s, "relativeTo", "关联到", L, GCamera, "Player");
        Pos(s, "position", "位置", L, GCamera);
        Dbl(s, "rotation", "角度", L, GCamera, 0);
        Dbl(s, "zoom", "缩放", L, GCamera, 100);
        Bl(s, "pulseOnFloor", "触发方块时摄像头脉动", L, GCamera, true);
        // 杂项
        Off(s, "bgVideo", "视频背景", L, GMisc);
        Bl(s, "floorIconOutlines", "方块图标描边", L, GMisc, false);
        Bl(s, "stickToFloors", "黏性方块", L, GMisc, false);
        Txt(s, "planetEase", "星球轨道缓速", L, GMisc, "None");
        Num(s, "planetEaseParts", "轨道缓速部分", L, GMisc, 1);
        Txt(s, "planetEasePartBehavior", "缓速部分行为", L, GMisc, "Independent");
        Txt(s, "defaultTextColor", "默认文本颜色", L, GMisc, "FFFFFF");
        Txt(s, "defaultTextShadowColor", "默认文本阴影颜色", L, GMisc, "000000");
        Txt(s, "congratsText", "“庆贺！”文本", L, GMisc, "congrats!!");
        Txt(s, "perfectText", "“完美无瑕！”文本", L, GMisc, "PERFECT!!");

        // ============ 二、装饰物（删 decorations 里匹配的事件）============
        var D = EffectCategory.Decoration;
        Evt(s, "killerDecorations", "死亡图像装饰", D, GDeco, "AddDecoration", d => IsStr(d, "hitbox", "Kill"));
        Evt(s, "otherDecorations", "其他图像装饰", D, GDeco, "AddDecoration", d => IsNotStr(d, "hitbox", "Kill"));
        Evt(s, "addText", "文本装饰", D, GDeco, "AddText");
        Evt(s, "addObjectFloor", "地板对象装饰", D, GDeco, "AddObject", d => IsNotStr(d, "objectType", "Planet"));
        Evt(s, "addObjectPlanet", "星球对象装饰", D, GDeco, "AddObject", d => IsStr(d, "objectType", "Planet"));
        Evt(s, "addParticle", "粒子发射器", D, GDeco, "AddParticle");

        // ============ 三、事件（删 actions 里匹配的事件）============
        var A = EffectCategory.Event;
        // 听觉
        Evt(s, "setHitsound", "设置打拍音", A, GAuditory, "SetHitsound");
        Evt(s, "playSound", "播放音效", A, GAuditory, "PlaySound");
        Evt(s, "setHoldSound", "设置长按音效", A, GAuditory, "SetHoldSound");
        // 星球
        Evt(s, "setPlanetRotation", "设置星球轨道", A, GPlanet, "SetPlanetRotation");
        Evt(s, "scalePlanets", "缩放行星", A, GPlanet, "ScalePlanets");
        Evt(s, "scaleRadius", "星球半径大小", A, GPlanet, "ScaleRadius");
        // 轨道
        Evt(s, "hide", "隐藏判定/地板图标", A, GEventTrack, "Hide");
        Evt(s, "moveTrack", "移动轨道", A, GEventTrack, "MoveTrack");
        Evt(s, "positionTrack", "位置轨道", A, GEventTrack, "PositionTrack");
        Evt(s, "colorTrack", "设置轨道颜色", A, GEventTrack, "ColorTrack");
        Evt(s, "animateTrack", "设置轨道动画", A, GEventTrack, "AnimateTrack");
        Evt(s, "recolorTrack", "重新设置轨道颜色", A, GEventTrack, "RecolorTrack");
        Evt(s, "checkpoint", "检查点", A, GEventTrack, "Checkpoint");
        Evt(s, "autoPlayTiles", "自动播放格子", A, GEventTrack, "AutoPlayTiles");
        // 装饰
        Evt(s, "moveDecorations", "移动装饰", A, GEventDeco, "MoveDecorations");
        Evt(s, "setText", "设置文本", A, GEventDeco, "SetText");
        Evt(s, "emitParticle", "发射粒子", A, GEventDeco, "EmitParticle");
        Evt(s, "setParticle", "设置粒子", A, GEventDeco, "SetParticle");
        Evt(s, "setObject", "设置对象", A, GEventDeco, "SetObject");
        Evt(s, "setDefaultText", "设置默认文本", A, GEventDeco, "SetDefaultText");
        // 视觉杂项
        Evt(s, "moveCamera", "移动摄像头", A, GVisual, "MoveCamera");
        Evt(s, "customBackground", "设置背景", A, GVisual, "CustomBackground");
        // 特效
        Evt(s, "flash", "闪光", A, GEffect, "Flash");
        Evt(s, "setFilter", "预设滤镜", A, GEffect, "SetFilter");
        Evt(s, "setFilterAdvanced", "预设高级滤镜", A, GEffect, "SetFilterAdvanced");
        Evt(s, "hallOfMirrors", "镜厅", A, GEffect, "HallOfMirrors");
        Evt(s, "shakeScreen", "振屏", A, GEffect, "ShakeScreen");
        Evt(s, "bloom", "绽放", A, GEffect, "Bloom");
        Evt(s, "screenTile", "平铺", A, GEffect, "ScreenTile");
        Evt(s, "screenScroll", "卷屏", A, GEffect, "ScreenScroll");
        Evt(s, "setFrameRate", "设置帧率", A, GEffect, "SetFrameRate");
        // 其他
        Evt(s, "editorComment", "编辑器附注", A, GEventMisc, "EditorComment");
        Evt(s, "bookmark", "书签", A, GEventMisc, "Bookmark");

        return s;
    }

    /// <summary>内置预设各自管哪些开关（按 ychfx 里各预设的实际内容，已去掉重叠项）。
    /// 预设只记"开了哪些"，具体值由界面上另设 —— 这是用户要求的行为。</summary>
    public static readonly string[][] BuiltinPresets = {
        // 1. 去除装饰物相关
        new[] { "killerDecorations", "otherDecorations", "addText", "addObjectFloor", "addObjectPlanet",
                "addParticle", "moveDecorations", "setText", "emitParticle", "setParticle", "setObject",
                "setDefaultText" },
        // 2. 去除摄像头相关
        new[] { "relativeTo", "position", "rotation", "zoom", "pulseOnFloor", "moveCamera", "customBackground" },
        // 3. 去除滤镜相关
        new[] { "flash", "setFilter", "setFilterAdvanced", "hallOfMirrors", "shakeScreen",
                "bloom", "screenTile", "screenScroll", "setFrameRate" },
        // 4. 去除轨道相关（内容与 ychfx 里用户自制的同名预设一致，只删轨道事件不动设置）
        new[] { "moveTrack", "positionTrack", "colorTrack", "animateTrack", "recolorTrack" },
    };

    public static readonly string[] BuiltinPresetNames = { "去除装饰物相关", "去除摄像头相关", "去除滤镜相关", "去除轨道相关" };
}