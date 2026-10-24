// 玩家信息栏右侧那枚「成就等级奖杯」：跟成就页顶栏那枚是同一个东西（同一份洗白剪影 + 同一套等级配色），
// 只是搬到信息栏右侧那块空白里，竖着居中齐平框体中线。面板内那份与界面外那份共用本文件：
// MakePlayerBoxAchBadge 在各自的搭建点各建一枚，ApplyPlayerBoxAchBadge 由 RefreshPlayerBoxInfo 统一刷新
//（两份的定时刷新都会走到它）。刻意与成就页一致 —— 复用 AchBadgeSprite / AchBadgeColor，
// 不新建贴图、不另写一套等级算法，也不给奖杯加任何点击行为。
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {
    private static Image _panelAchBadgeImg;     // 面板内那份信息栏上的奖杯
    private static Image _outsideAchBadgeImg;   // 界面外那份信息栏上的奖杯

    /// <summary>在信息栏右侧建一枚成就等级奖杯（34x34，竖着居中，齐着框体中线的右边那块空白；
    /// 名字的文字框已经为它让开了右边 52px）。素材拿不到就让 Image 停用 —— 宁可不显示，
    /// 也不留一块没有图案的方块。</summary>
    private static Image MakePlayerBoxAchBadge(Transform box) {
        var img = MakeImage(box, "BoxAchBadge", AchBadgeColor(1));
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.raycastTarget = false;   // 面板内那份要吃右键菜单，别在奖杯这儿被挡下来
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(34f, 34f);
        rt.anchoredPosition = new Vector2(-12f, 0f);
        img.sprite = AchBadgeSprite;
        if (img.sprite == null) img.enabled = false;
        return img;
    }

    /// <summary>把奖杯刷成当前成就等级的样子（1 白 2 黄 3 红 4 橙 5 金，与成就页顶栏同一套）。
    /// 值没变就不写；素材这会儿还没加载出来就先停用，下一次刷新（面板内 1 秒 / 界面外 0.2 秒）会自己接上。</summary>
    private static void ApplyPlayerBoxAchBadge(Image img) {
        if (img == null) return;   // 那一份还没搭出来 / 已经销毁
        try {
            var spr = AchBadgeSprite;
            if (spr == null) { if (img.enabled) img.enabled = false; return; }
            Achievements.LevelProgress(out int lv, out _, out _, out _);
            if (img.sprite != spr) { img.sprite = spr; img.type = Image.Type.Simple; img.preserveAspect = true; }
            var want = AchBadgeColor(lv);
            if (img.color != want) img.color = want;
            if (!img.enabled) img.enabled = true;
        } catch (System.Exception e) {
            ReportErrorQuiet("刷新信息栏奖杯失败", e);
        }
    }
}
