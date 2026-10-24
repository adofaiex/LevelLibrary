using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>主题色选择：设置行右边一排色块，点一下立刻全局重画并落盘。
/// 全项目唯一写 ThemeColor 的地方（原来是「从自定义背景图提取主色」，那条已随静态背景功能一起删掉）。
/// 色块的底色是调色板本身，绝不能登记进 _fillImages，否则换主题色时它们会被自己刷成同一个颜色。</summary>
public static partial class PortalUI {

    private const float ThemeSwatchSize = 40f;
    private const float ThemeSwatchGap = 12f;
    private const float ThemeRingPad = 4f;        // 选中那圈白框比色块大出来的半径
    private const float ThemeRightMargin = 12f;   // 与滑条同一条右边界

    /// <summary>九档主题色（第一个 = 淡蓝；默认档是「红」那一格，见 Core.cs 的 DefaultThemeColor ——
    /// 没在设置里亲手选过色的人用它，选中框按"最近档"会亮在红上）。九档都取在同一明度 / 饱和度档上，
    /// 深浅与默认色相当，换到哪一档都不会让压在浅色遮罩上的深色字看不清。</summary>
    private static readonly Color[] ThemePalette = {
        new Color(0.62f, 0.84f, 1.00f, 1f),   // 淡蓝
        new Color(0.55f, 0.90f, 0.90f, 1f),   // 青
        new Color(0.60f, 0.90f, 0.66f, 1f),   // 绿
        new Color(0.90f, 0.88f, 0.56f, 1f),   // 黄
        new Color(1.00f, 0.74f, 0.48f, 1f),   // 橙
        new Color(1.00f, 0.60f, 0.58f, 1f),   // 红（默认档）
        new Color(1.00f, 0.66f, 0.86f, 1f),   // 粉
        new Color(0.80f, 0.66f, 1.00f, 1f),   // 紫
        new Color(0.62f, 0.70f, 1.00f, 1f),   // 蓝紫
    };

    private static readonly Image[] _themeSwatches = new Image[9];   // 设置页每重建一次就整组重填
    private static readonly Image[] _themeRings = new Image[9];

    /// <summary>建「主题色」那一行右边的色块（调用方用 MakeRowAt 起行）</summary>
    private static void BuildThemeRow(GameObject row) {
        try {
            if (row == null) return;
            int n = ThemePalette.Length;
            for (int i = 0; i < n; i++) {
                float x = -(ThemeRightMargin + ThemeSwatchSize * 0.5f + (n - 1 - i) * (ThemeSwatchSize + ThemeSwatchGap));
                var ring = MakeImage(row.transform, "ThemeRing" + i, new Color(1f, 1f, 1f, 0f));
                ring.sprite = GetRoundedFrameSprite();
                ring.type = Image.Type.Sliced;
                ring.raycastTarget = false;
                var rrt = ring.rectTransform;
                rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f);
                rrt.pivot = new Vector2(0.5f, 0.5f);
                rrt.sizeDelta = new Vector2(ThemeSwatchSize + ThemeRingPad * 2f, ThemeSwatchSize + ThemeRingPad * 2f);
                rrt.anchoredPosition = new Vector2(x, 0f);

                var sw = MakeImage(row.transform, "ThemeSwatch" + i, ThemePalette[i]);
                sw.sprite = SmallRoundSprite;
                sw.type = Image.Type.Sliced;
                var srt = sw.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
                srt.pivot = new Vector2(0.5f, 0.5f);
                srt.sizeDelta = new Vector2(ThemeSwatchSize, ThemeSwatchSize);
                srt.anchoredPosition = new Vector2(x, 0f);

                var btn = sw.gameObject.AddComponent<Button>();
                btn.targetGraphic = sw;
                var cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);   // 提亮靠乘法，色相不变
                cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
                cb.selectedColor = Color.white;
                cb.fadeDuration = 0.08f;
                btn.colors = cb;
                var pal = ThemePalette[i];
                btn.onClick.AddListener(() => {
                    try { ApplyThemeColor(pal); } catch (System.Exception e) { Lg.Ex("换主题色失败", e); }
                });
                _themeRings[i] = ring;
                _themeSwatches[i] = sw;
            }
            PaintThemeSwatches();
        } catch (System.Exception e) { Lg.Ex("建主题色行失败", e); }
    }

    /// <summary>换主题色的唯一入口：写值 → 全局重画 → 落盘 → 刷新选中框。
    /// alpha 沿用默认主题色那一档，透明度由遮罩系数管，跟选哪个色无关。</summary>
    private static void ApplyThemeColor(Color c) {
        ThemeColor = new Color(c.r, c.g, c.b, DefaultThemeColor.a);
        ThemePicked = true;   // 从此按文件走，不再被"没选过就回默认蓝"那次迁移覆盖
        RepaintThemeImages();
        AutoFeatures.Save();
        PaintThemeSwatches();
        Achievements.TryUnlock("ach_myself");
        Lg.Info($"[LevelLib] 主题色已换：RGB({ThemeColor.r:F2}, {ThemeColor.g:F2}, {ThemeColor.b:F2})");
    }

    /// <summary>选中框打在"离当前色最近的那一档"上。
    /// 用最近而不是全等：老玩家 settings.txt 里那套从背景图提取出来的颜色跟九档都不完全相等，
    /// 全等的话九档一个都不亮、看着像没选中。</summary>
    private static void PaintThemeSwatches() {
        try {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < ThemePalette.Length; i++) {
                var p = ThemePalette[i];
                float dr = p.r - ThemeColor.r, dg = p.g - ThemeColor.g, db = p.b - ThemeColor.b;
                float d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = i; }
            }
            for (int i = 0; i < _themeRings.Length; i++) {
                var ring = _themeRings[i];
                if (ring == null) continue;
                ring.color = new Color(1f, 1f, 1f, i == best ? 0.95f : 0f);
            }
        } catch { }
    }
}
