using System;

namespace LevelLibrary;

/// <summary>图标形状层：全部在 24 格的"设计网格"上量，与最终贴图多大无关。
/// 刻意不引用 UnityEngine —— 桌面预览工具（tmp_api\icon_preview）编译同一份源码出 PNG，
/// 所以"预览看到的"和"游戏里烘的"一定是同一套数学，不会两边各写一遍走形。</summary>
public static class IconShapes
{
    /// <summary>设计网格边长（格单位）。所有形状坐标都在 0..Grid 里量。</summary>
    public const int Grid = 24;

    /// <summary>全族唯一描边宽度（格单位）：16 / 20 / 24 三个尺寸档共用，不随尺寸加粗。</summary>
    public const float Stroke = 2.0f;

    /// <summary>光学方框：任何形状离网格边至少留 2 格空气，一排图标看起来才一样大。</summary>
    public const float Safe = 2.0f;

    /// <summary>软边宽度（格单位）。1 格 ≈ 贴图 1 像素，所以这相当于半像素羽化，16 像素档也不会出现硬锯齿。</summary>
    private const float Feather = 1.2f;

    public enum Kind { None, Star, StarFilled, Close, ChevronLeft, ChevronRight, Pencil, Flag, Folder, Trash }

    /// <summary>格坐标 (gx, gy) 处的覆盖率 0..1（gy 向下）。软边已经含在里面，调用方不要再做叠加平均。</summary>
    public static float Coverage(Kind kind, float gx, float gy)
    {
        switch (kind)
        {
            case Kind.Star:
                return Soft(StarEdge(gx, gy) - Stroke * 0.5f);
            case Kind.StarFilled:
                return Soft(StarInside(gx, gy) ? -StarEdge(gx, gy) : StarEdge(gx, gy));
            case Kind.Close:
                return Soft(Math.Min(
                    Seg(gx, gy, 6.5f, 6.5f, 17.5f, 17.5f),
                    Seg(gx, gy, 17.5f, 6.5f, 6.5f, 17.5f)) - Stroke * 0.5f);
            case Kind.ChevronLeft:
                return Soft(Math.Min(
                    Seg(gx, gy, 13.8f, 5.5f, 8.0f, 12.0f),
                    Seg(gx, gy, 8.0f, 12.0f, 13.8f, 18.5f)) - Stroke * 0.5f);
            case Kind.ChevronRight:
                return Soft(Math.Min(
                    Seg(gx, gy, 10.2f, 5.5f, 16.0f, 12.0f),
                    Seg(gx, gy, 16.0f, 12.0f, 10.2f, 18.5f)) - Stroke * 0.5f);
            case Kind.Pencil:
                return Soft(PolyEdge(gx, gy, Pencil, true) - Stroke * 0.5f);
            case Kind.Flag:
                return Soft(Math.Min(
                    PolyEdge(gx, gy, FlagPole, false),
                    PolyEdge(gx, gy, FlagTri, true)) - Stroke * 0.5f);
            case Kind.Folder:
                return Soft(PolyEdge(gx, gy, Folder, true) - Stroke * 0.5f);
            case Kind.Trash:
                return Soft(Math.Min(
                    Math.Min(PolyEdge(gx, gy, TrashBody, false), PolyEdge(gx, gy, TrashLid, false)),
                    Math.Min(PolyEdge(gx, gy, TrashHandle, false), PolyEdge(gx, gy, TrashLidVert, false))) - Stroke * 0.5f);
            default:
                return 0f;
        }
    }

    // 右键菜单那一排用的四个形状（笔 / 旗 / 文件夹 / 垃圾桶），与星、叉、箭头同一条描边。
    private static readonly float[] Pencil = { 15.8f, 6.0f, 18.0f, 8.2f, 9.4f, 16.8f, 6.0f, 18.0f, 7.2f, 14.6f };
    private static readonly float[] FlagPole = { 7.5f, 4.6f, 7.5f, 19.4f };
    private static readonly float[] FlagTri = { 7.5f, 5.0f, 17.0f, 8.2f, 7.5f, 11.4f };
    private static readonly float[] Folder = { 5.5f, 7.5f, 10.0f, 7.5f, 11.5f, 9.8f, 18.5f, 9.8f, 18.5f, 17.5f, 5.5f, 17.5f };
    private static readonly float[] TrashLid = { 6.5f, 7.0f, 17.5f, 7.0f };
    private static readonly float[] TrashHandle = { 10.2f, 4.6f, 13.8f, 4.6f };
    private static readonly float[] TrashLidVert = { 10.2f, 4.6f, 10.2f, 7.0f, 13.8f, 7.0f, 13.8f, 4.6f };
    private static readonly float[] TrashBody = { 8.6f, 7.0f, 9.4f, 18.6f, 14.6f, 18.6f, 15.4f, 7.0f };

    /// <summary>折线（closed = 首尾相连）到点的最短距离。点数组是 x,y,x,y… 排的。</summary>
    private static float PolyEdge(float gx, float gy, float[] pts, bool closed)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 3 < pts.Length; i += 2)
        {
            float d = Seg(gx, gy, pts[i], pts[i + 1], pts[i + 2], pts[i + 3]);
            if (d < best) best = d;
        }
        if (closed && pts.Length >= 6)
        {
            float d = Seg(gx, gy, pts[pts.Length - 2], pts[pts.Length - 1], pts[0], pts[1]);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>烘一张方形贴图：白色 + alpha = 覆盖率（颜色一律由调用点染色，贴图本身不带颜色）。
    /// size = 贴图边长，也是"屏幕上的显示像素"（1:1 铺，不缩放）。第 0 行是图像顶部（PNG 顺序）。</summary>
    public static void Bake(Kind kind, int size, byte[] rgba)
    {
        if (rgba == null || rgba.Length < size * size * 4) throw new ArgumentException("缓冲太小", "rgba");
        float step = (float)Grid / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 2×2 子采样：小尺寸档也能保住细线不断
                float a = 0f;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                        a += Coverage(kind, (x + 0.25f + sx * 0.5f) * step, (y + 0.25f + sy * 0.5f) * step);
                a *= 0.25f;
                int i = (y * size + x) * 4;
                rgba[i] = 255;
                rgba[i + 1] = 255;
                rgba[i + 2] = 255;
                rgba[i + 3] = (byte)(Clamp01(a) * 255f + 0.5f);
            }
        }
    }

    /// <summary>描边类：d = 到形状骨架的距离；超出半个线宽的部分开始向外淡出。</summary>
    private static float Soft(float d) { return Clamp01(0.5f - d / Feather); }

    private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

    /// <summary>点到线段的距离。</summary>
    private static float Seg(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float wx = px - ax, wy = py - ay;
        float len2 = vx * vx + vy * vy;
        float t = len2 <= 0.0001f ? 0f : Clamp01((wx * vx + wy * vy) / len2);
        float dx = wx - vx * t, dy = wy - vy * t;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    // 五角星：中心 (12,12)，外接半径 9、内接半径 3.9（比经典的 0.382 比例略瘦一点：
    // 线框星在 20 像素档中间那个孔要看得出来，太瘦又会显得针一样）。十个顶点从正上方起
    // 每 36 度交替取外/内，内容跨度 18 格，四周正好留 3 格空气。
    private const float StarOuter = 9.0f;
    private const float StarInner = 3.9f;

    private static void StarVertex(int i, out float x, out float y)
    {
        double ang = -Math.PI / 2.0 + i * Math.PI / 5.0;
        float r = (i % 2 == 0) ? StarOuter : StarInner;
        x = 12f + r * (float)Math.Cos(ang);
        y = 12f + r * (float)Math.Sin(ang);
    }

    /// <summary>到星形轮廓的最短距离（不管在里在外）。</summary>
    private static float StarEdge(float gx, float gy)
    {
        float best = float.MaxValue;
        for (int i = 0; i < 10; i++)
        {
            float ax, ay, bx, by;
            StarVertex(i, out ax, out ay);
            StarVertex((i + 1) % 10, out bx, out by);
            float d = Seg(gx, gy, ax, ay, bx, by);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>奇偶规则的在形内判定（射线法），给实心星用。</summary>
    private static bool StarInside(float gx, float gy)
    {
        bool inside = false;
        for (int i = 0, j = 9; i < 10; j = i++)
        {
            float ax, ay, bx, by;
            StarVertex(i, out ax, out ay);
            StarVertex(j, out bx, out by);
            if ((ay > gy) != (by > gy) && gx < (bx - ax) * (gy - ay) / (by - ay) + ax) inside = !inside;
        }
        return inside;
    }
}
