using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI
{
    // 图标族：全项目只有这三个尺寸档，形状全部来自 IconShapes（24 格设计网格 + 2 格描边）。
    // 规律：一排里并排的用 Md，独立成键的用 Lg，塞在角上/行内的用 Sm。
    public const int IconSm = 16;
    public const int IconMd = 20;
    public const int IconLg = 24;

    // 收藏星两态色：原先三处各写一遍字面量（详情区 / 列表行 / 选择关卡弹窗），收到这里一份。
    public static readonly Color IconFavOn = new Color(1f, 0.85f, 0.2f, 1f);
    public static readonly Color IconFavOff = new Color(0.7f, 0.7f, 0.7f, 0.6f);

    private static readonly Dictionary<int, Sprite> _iconSprites = new Dictionary<int, Sprite>();

    /// <summary>取一张图标贴图（按 种类+尺寸 缓存）。贴图边长就是屏幕显示像素，1:1 铺不缩放，
    /// 所以任何尺寸档都清晰；TufBatch 结果页每次重建，缓存是必须的。</summary>
    public static Sprite IconSprite(IconShapes.Kind kind, int sizePx) {
        int key = (int)kind * 1000 + sizePx;
        Sprite cached;
        if (_iconSprites.TryGetValue(key, out cached) && cached != null) return cached;
        try {
            var tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false);
            var buf = new byte[sizePx * sizePx * 4];
            IconShapes.Bake(kind, sizePx, buf);
            // Bake 给的是"第 0 行在图像顶部"，而原始贴图数据是"第 0 行在底部"，上传前整行翻一次。
            int stride = sizePx * 4;
            var flipped = new byte[buf.Length];
            for (int y = 0; y < sizePx; y++)
                System.Buffer.BlockCopy(buf, y * stride, flipped, (sizePx - 1 - y) * stride, stride);
            tex.LoadRawTextureData(flipped);
            tex.Apply(false, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var spr = Sprite.Create(tex, new Rect(0f, 0f, sizePx, sizePx), new Vector2(0.5f, 0.5f), 100f);
            _iconSprites[key] = spr;
            return spr;
        } catch { return null; }
    }

    /// <summary>把图标画到已有图元上。烘不出来时<b>把图元关掉</b>而不是留空：
    /// uGUI 的 Image 在 sprite 为 null 时画的是一块实心白方块，不是"什么都不画"（默认头像白块栽过一次）。
    /// 同时强制 Simple：`MakeImage` 默认给的是九宫格圆角图，套在小图标上会把边角拉花。</summary>
    public static void ApplyIcon(Image img, IconShapes.Kind kind, int sizePx, Color tint) {
        if (img == null) return;
        var spr = IconSprite(kind, sizePx);
        img.sprite = spr;
        img.type = Image.Type.Simple;
        img.color = spr == null ? new Color(0f, 0f, 0f, 0f) : tint;
        img.enabled = spr != null;
    }

    /// <summary>建一个图标图元（族内唯一入口）。位置由调用方按各自的锚点摆，这里只管尺寸。</summary>
    public static Image IconImage(Transform parent, string name, IconShapes.Kind kind, int sizePx, Color tint) {
        var img = MakeImage(parent, name, tint);
        img.rectTransform.sizeDelta = new Vector2(sizePx, sizePx);
        img.raycastTarget = false;
        ApplyIcon(img, kind, sizePx, tint);
        return img;
    }

    /// <summary>收藏星两态：实心金 = 已收藏，线框灰 = 未收藏。三处落点（详情区 / 列表行 / 选择关卡弹窗）
    /// 共用这一对判定；状态色只由这里写，所以按钮自己的颜色过渡必须关掉，否则两套会写同一个图元。</summary>
    public static void SetFavIcon(Image img, bool fav, int sizePx) {
        ApplyIcon(img, fav ? IconShapes.Kind.StarFilled : IconShapes.Kind.Star, sizePx, fav ? IconFavOn : IconFavOff);
    }
}
