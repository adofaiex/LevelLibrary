using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：主界面的配色令牌与小工具。
/// 颜色全部从示意图那份源码的 @theme / ocean.css 抄过来（值与那里的 CSS 变量一一对应），
/// 不引入新的色板 —— 项目自己的主题色仍然通过 ThemeColor 参与（竖条、光点、强调色）。</summary>
public static partial class PortalUI {

    // ---- 示意图 @theme（index.css）与 ocean.css 里的原值 ----
    private static Color ArkTitleLight()   { return Hex(0xDDDDDD); }        // --color-title-light
    private static Color ArkTitleDark()    { return Hex(0xB9B9B9); }        // --color-title-dark
    private static Color ArkTitleVeryDark(){ return Hex(0x777777); }        // --color-title-very-dark
    private static Color ArkSubtitleLight(){ return Hsl(245, 1f, 0.80f); } // --color-subtitle-light
    private static Color ArkSubtitleMid()  { return Hsl(245, 1f, 0.70f); } // --color-subtitle-mid

    /// <summary>乳白（白里带一点黄的暖白）——用户点名的两处用它：右侧"Welcome back"所在的统计板、
    /// 谱面库/官谱库/成就/开发者四枚小键，以及"叠加层"那一条的底色（采购中心 + 招募栏两块拼成一条）。
    /// 深色字配它才看得清。透明度按用户两次反馈调过：太白太实 → 170，再"透明一些" → 140。</summary>
    private static Color ArkCream()        { return HexA(0xFFF6DF, 140); }

    /// <summary>叠加层那一条的底色（用户点的半透明黑）：采购中心 + 招募栏两块拼成一条用的就是它。
    /// 用户点名"特别是黑色要更透"，透明度比乳白/紫更低一档。</summary>
    private static Color ArkOverlayBg()    { return HexA(0x000000, 100); }

    /// <summary>VOICE 框（交流群那条）专用的乳白：比通用 ArkCream 更不透明（用户点名"不透明一些"）。</summary>
    private static Color ArkCreamSolid()   { return HexA(0xFFF6DF, 190); }

    private static Color ArkVoiceBg()      { return HexA(0x999999, 204); }// --color-voice-bg
    private static Color ArkVoiceText()    { return Hex(0x333333); }        // --color-voice-text
    private static Color ArkNewsBg()       { return Hex(0xBB0000); }        // --color-news-bg
    private static Color ArkNewsScroll()   { return Hex(0xFF9900); }        // --color-news-scrollbar-bg
    private static Color ArkItemsBg()      { return HexA(0x222222, 170); } // --color-items-bg（0xaa）
    private static Color ArkCurrentBg()    { return Hex(0x222222); }        // --color-current-wrapper
    private static Color ArkSanityLight()  { return HexA(0x888888, 119); } // --color-sanity-light
    private static Color ArkSanityDark()   { return Hex(0x323232); }        // --color-sanity-dark

    private static Color ArkButtonDarkBg()   { return HexA(0x333344, 170); }   // --color-button-dark
    private static Color ArkButtonLightBg()  { return HexA(0x4A40BF, 140); }   // --color-button-light（叠加层移交给子按钮后，透明度跟随本轮统一调低）
    private static Color ArkBorderDark()     { return Hex(0x6052FF); }         // --color-border-dark（小按钮左端竖条）
    private static Color ArkButtonFriendsBg(){ return HexA(0x111115, 187); }   // --color-button-friends
    private static Color ArkButtonArchiveBg(){ return HexA(0x222228, 187); }   // --color-button-archive
    private static Color ArkButtonDepotBg()  { return HexA(0x222228, 187); }   // --color-button-depot
    /// <summary>行尾占位块的底色（模板 --color-placeholder-bg: #000000aa）：编队行最右侧那块装饰空块。</summary>
    private static Color ArkPlaceholderBg()  { return HexA(0x000000, 170); }

    /// <summary>示意图里的"主题色竖条"：用项目自己的 ThemeColor（跟着九档走），不写死蓝紫。</summary>
    private static Color ArkAccent() {
        var c = ThemeColor;
      return new Color(c.r, c.g, c.b, 0.95f);
    }

    // ---- 颜色工具 ----
    private static Color Hex(int rgb) {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    private static Color HexA(int rgb, int a) {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a / 255f);
    }

    /// <summary>HSL 到 Color（示意图的副标题色是 hsl(245,100%,60%) 那种写法）。</summary>
    private static Color Hsl(float hDeg, float s, float l) {
        float h = Mathf.Repeat(hDeg / 360f, 1f);
        if (s <= 0f) return new Color(l, l, l, 1f);
        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
    float p = 2f * l - q;
    float r = HueToRgb(p, q, h + 1f / 3f);
        float g = HueToRgb(p, q, h);
 float b = HueToRgb(p, q, h - 1f / 3f);
        return new Color(r, g, b, 1f);
    }

    private static float HueToRgb(float p, float q, float t) {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
  if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
 return p;
    }

    // ================= 图标按钮 =================



    /// <summary>一块正圆底板（圆钮专用）：整圆贴图 + Image.Simple。
    /// 不能改用 MakeArkPlate(radius=24)：CreateRoundedSprite 的半径超过半边长时 SDF 的
    /// Clamp(min&gt;max) 会算出畸形形状（用户报的"圆钮不是圆、是不规则形状"就是这个）。</summary>
    private static Image MakeArkDisc(Transform parent, string name, float left, float top, float size, Color col) {
        var img = MakeImage(parent, name, col);
        img.sprite = RoundDiscSprite;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        SetAtPx(img.rectTransform, left * ArkPx, top * ArkPx, size * ArkPx, size * ArkPx);
        return img;
    }


    /// <summary>左区版圆钮：位置过 ARK_VSCALE_L；圆盘用整圆贴图（同上，别退回 MakeArkPlate）。</summary>
    private static Image MakeArkRoundButtonV(Transform parent, string name, float x, float y, int icon) {
        float cy = ArkVYL(y + ArkRoundSize * 0.5f);
        var plate = MakeArkDisc(parent, name, x, cy - ArkRoundSize * 0.5f, ArkRoundSize, Hex(0x303030));
        MakeArkIcon(plate.transform, "Icon", ArkRoundSize * 0.5f, ArkRoundSize * 0.5f, ArkRoundSize - 8f, icon);
        var btn = plate.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = plate;
        return plate;
    }

    /// <summary>按图标中心摆一枚图标按钮（左区专用，中心 y 已由调用方过 ARK_VSCALE_L）。</summary>
    private static Image MakeArkIconButtonC(Transform parent, string name, float cx, float cy, float size, int icon) {
        var img = MakeArkIcon(parent, name, cx, cy, size, icon);
        var btn = img.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        return img;
    }

    /// <summary>左区版图标：中心 y 过 ARK_VSCALE_L，尺寸保持原值（图标是方的）。</summary>
    private static Image MakeArkIconV(Transform parent, string name, float cx, float cy, float size, int kind) {
        return MakeArkIcon(parent, name, cx, ArkVYL(cy), size, kind);
    }



    // ---- 图标 kind 常量（补齐 ArkView 用到的那几个）----
}
