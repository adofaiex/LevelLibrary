using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// LevelLibrary 的字号标尺：界面文字只许用这几档（外加展示级特例）。
// 起因是量出来全项目 UI 字号有 26 种，同一个层级的文字在不同页面各写各的（22/24/26 混用）。
// 这里的数是"设计尺"，不是"用户设置"——列表字号、成就名字号那几项仍是玩家自己在设置里调的，
// 它们的默认值允许不在本表内。
// 设置页那条「界面字体大小」滑条改的是另一件事：它在设计尺之上再乘一个全局比例，
// 调用方照旧只写设计尺上的数，实际像素由 UiFontPx 算。
namespace LevelLibrary;

public static partial class PortalUI {
    /// <summary>说明性小字 / 单位 / 角标</summary>
    public const int FsCaption = 14;
    /// <summary>次要信息：元数据、副标题、卡片第二行</summary>
    public const int FsSmall = 17;
    /// <summary>小标签：统计条说明、热力图轴标</summary>
    public const int FsLabel = 20;
    /// <summary>正文：列表文字、设置项标签、按钮文字（用得最多的一档）</summary>
    public const int FsBody = 22;
    /// <summary>行标题：卡片名、面板里的小标题</summary>
    public const int FsRowTitle = 24;
    /// <summary>段标题：分区标题、详情区标题</summary>
    public const int FsSection = 26;
    /// <summary>页面标题：各页最上面那一行 / 弹窗标题</summary>
    public const int FsPageTitle = 30;
    /// <summary>大标题：详情区主标题、弹窗主标题</summary>
    public const int FsHeading = 34;
    /// <summary>展示级（单词标 / 开始界面大字 / 生日屏）：不受标尺约束，建的时候自己定</summary>
    public const int FsDisplayFloor = 40;

    /// <summary>「界面字体大小」= 设计尺乘一个全局比例（1.00 = 原样）。范围按百分比放在设置页那条滑条上。
    /// 上限不放太宽：各处的矩形框都是像素常量，字放大而框不放大，放太大会挤成一团。</summary>
    public const int UiFontPercentMin = 85;
    public const int UiFontPercentMax = 130;

    // 登记表：MakeText 建出来的每一段文字都记一笔"它在设计尺上是几号"，改比例时按它统一刷新。
    // 页面每次打开都会重建，旧的 Text 被销毁后条目留着没用，所以刷新时顺手清掉。
    private static readonly Dictionary<Text, int> _uiFontTexts = new Dictionary<Text, int>();

    /// <summary>设计尺上的字号换算成实际像素（四舍五入，至少 8 —— 再小就看不见了）。
    /// 必须四舍五入：22 乘 1.1 = 24.2，截断会退回 24 这一档，看着像"滑条没生效"。</summary>
    public static int UiFontPx(int size) {
        return Mathf.Max(8, Mathf.RoundToInt(size * UiFontScale));
    }

    /// <summary>登记一段由 MakeText 建出来的文字：记下它设计尺上的字号，改比例时才刷得到它。</summary>
    public static void RegisterUiFontText(Text t, int designSize) {
        if (t == null) return;
        _uiFontTexts[t] = designSize;
    }

    /// <summary>忘掉一段文字：字号由别处（气泡按框自适应那种）自己算的，别让全局比例去动它。</summary>
    public static void ForgetUiFontText(Text t) {
        if (t == null) return;
        _uiFontTexts.Remove(t);
    }

    /// <summary>把当前比例刷到所有登记过的文字上（设置页拖动滑条时即时生效）。
    /// 成就卡那两行是按用户设置直接写 fontSize 的，不在这张表里，所以另外叫一次。</summary>
    public static void ApplyUiFontScale() {
        List<Text> dead = null;
        foreach (var kv in _uiFontTexts) {
            Text t = kv.Key;
            if (t == null) {
                if (dead == null) dead = new List<Text>();
                dead.Add(t);
                continue;
            }
            int px = UiFontPx(kv.Value);
            if (t.fontSize != px) t.fontSize = px;
        }
        if (dead != null) {
            for (int i = 0; i < dead.Count; i++) _uiFontTexts.Remove(dead[i]);
        }
        ApplyAchFontSizes();
    }
}
