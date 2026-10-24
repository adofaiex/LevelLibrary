using System;
using System.Collections.Generic;

namespace LevelLibrary;

/// <summary>代码绘制的「神经网络拓扑」图样：给大厅左区那块模糊遮罩用。
/// 构图 = 若干簇 + 簇心大节点 + 近邻短连线 + 少量跨簇长连线（小世界网络那种疏密），
/// 节点是点、连线是细线，光点顺着"点 → 点 → 点"的折线流动，最长的那批线交给运行时各自呼吸。
/// 纯算法烘焙、不碰 UnityEngine，产出 RGBA32 裸字节（y = 0 是最下面一行，与 NetBgBake 同一套约定）；
/// 同一颗种子必须烘出同一份字节。
/// 动效的排班直接复用 NetBgBake 的 PulseStateAt / BreatheStateAt —— 与全屏背景那套是同一个手感。</summary>
internal static class NetTopoBake {

    /// <summary>底图尺寸 = 屏幕上那块遮罩的大小（1:1 显示才不糊）</summary>
    public const int W = 896, H = 848;
    /// <summary>四边留的余量：整层做缓慢漂移/呼吸时不会顶到遮罩的圆角上</summary>
    private const float Inset = 30f;

    private const uint SeedTopo = 0x707002u;

    // ---- 构图 ----
    private const int Nodes = 320;             // 节点总数（密一点才像神经网络，稀了就成星座图）
    private const int Clusters = 7;            // 团簇数（只有一个在正中）
    private const int HubCount = 11;           // 大节点
    private const float ClusterSpread = 0.085f;// 簇内散布半径（相对较小边）
    private const float ScatterK = 0.55f;      // 一多半节点撒在全局，整块才铺满
    private const float EdgeFall = 0.34f;      // 靠边变稀的比例（0 = 完全均匀，1 = 边上空掉）
    private const float LinkNearK = 0.085f;    // 先按这个半径找邻居（相对宽度）
    private const float LinkGrow = 1.35f;      // 邻居不够就把半径乘这么大再找
    private const float LinkMaxK = 0.145f;     // 半径上限：超过就不再扩（宁可少连几条）
    private const int LinkMin = 3;             // 每个节点至少连几条（不够就扩半径）
    private const int LinkMax = 4;             // 最多连几条
    private const float BendK = 0.05f;         // 连线的弯曲量（很小，像被轻微拉开）

    // ---- 画 ----
    private const float LineCore = 1.25f;      // 线芯（纹理像素）：太细的线在屏幕上会一节一节地断
    private const float LineGlow = 3.0f;
    private const float LineAmp = 0.36f;       // 亮度压一点：粗了之后再原亮度会糊成一片
    private const float DotR = 2.0f;
    private const float DotAmp = 0.92f;
    private const float HubR = 4.6f;           // 大节点
    private const float HubHalo = 13f;         // 大节点外面那圈很淡的辉光
    private const float HubAmp = 1.0f;

    /// <summary>交给运行时各自呼吸的线：按长度取前这么多条（它们不烘进底图）</summary>
    public const int BreatheLines = 46;

    /// <summary>确定性随机：xorshift32（与 NetBgBake / NetTreeBake 同款）</summary>
    private struct Rng {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0u ? 0x9E3779B9u : seed; }
        public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public float F() { return (Next() >> 8) * (1f / 16777216f); }
        public float Range(float a, float b) { return a + (b - a) * F(); }
        // 近似正态：三个均匀数相加就够（构图不需要更精确的分布）
        public float Gauss() { return (F() + F() + F() - 1.5f) * 1.1547f; }
    }

    private sealed class Graph {
        public float[] X, Y, R;                 // 坐标与半径系数
        public bool[] Hub;
        public int Count;
        public readonly List<int> EA = new List<int>();
        public readonly List<int> EB = new List<int>();
        public readonly List<float> EBend = new List<float>();   // 每条的弯曲量（法线方向）
    }

    private static Graph _graph;
    private static Graph G() {
        if (_graph == null) _graph = Build();
        return _graph;
    }

    private static Graph Build() {
        var rng = new Rng(SeedTopo);
        var g = new Graph { Count = Nodes };
        g.X = new float[Nodes]; g.Y = new float[Nodes]; g.R = new float[Nodes]; g.Hub = new bool[Nodes];
        float cx = W * 0.5f, cy = H * 0.5f;
        float halfW = W * 0.5f - Inset, halfH = H * 0.5f - Inset;
        // 簇心：绕中心一圈 + 正中一个，半径别顶到边
        var ccx = new float[Clusters]; var ccy = new float[Clusters];
        for (int c = 0; c < Clusters; c++) {
            if (c == 0) { ccx[c] = cx + rng.Range(-30f, 30f); ccy[c] = cy + rng.Range(-30f, 30f); continue; }
            float ang = (c - 1) / (float)(Clusters - 1) * 6.2831853f + rng.Range(-0.25f, 0.25f);
            float rr = rng.Range(0.30f, 0.62f);
            ccx[c] = cx + (float)Math.Cos(ang) * halfW * rr;
            ccy[c] = cy + (float)Math.Sin(ang) * halfH * rr;
        }
        float spread = Math.Min(W, H) * ClusterSpread;
        int hubLeft = HubCount;
        for (int i = 0; i < Nodes; i++) {
            float x, y;
            bool scattered = rng.F() < ScatterK;
            if (scattered) {
                // 全局散布：铺满整块，只让靠边的地方稀一点点（边上空掉会看着像"一坨"）
                for (int tries = 0; tries < 24; tries++) {
                    float u = rng.Range(-1f, 1f), v = rng.Range(-1f, 1f);
                    float rad2 = u * u + v * v;
                    if (rad2 > 1f) continue;
                    if (rng.F() < EdgeFall * rad2) continue;
                    x = cx + u * halfW; y = cy + v * halfH;
                    g.X[i] = x; g.Y[i] = y; goto placed;
                }
            }
            int c2 = (int)(rng.F() * Clusters);
            if (c2 >= Clusters) c2 = Clusters - 1;
            x = ccx[c2] + rng.Gauss() * spread;
            y = ccy[c2] + rng.Gauss() * spread;
            g.X[i] = Math.Max(Inset, Math.Min(W - Inset, x));
            g.Y[i] = Math.Max(Inset, Math.Min(H - Inset, y));
        placed:
            g.R[i] = rng.Range(0.75f, 1.35f);
        }
        // 大节点：第 0 簇的第一个节点 + 随机挑几个（挑那种周围邻居多的）
        for (int i = 0; i < Nodes && hubLeft > 0; i++) {
            if (i == 0 || rng.F() < 0.06f) { g.Hub[i] = true; g.R[i] = rng.Range(1.5f, 2.1f); hubLeft--; }
        }
        // 连线：每个节点先按小半径连最近的几个邻居；不够 LinkMin 条就把半径放大再找一轮
        // （自适应是为了让整块连成一张网 —— 固定半径会在稀疏处留下一堆只有一条线的孤点，
        //  那是最像"随便画几笔"的形态）
        for (int i = 0; i < Nodes; i++) {
            var cand = new List<int>();
            for (int j = 0; j < Nodes; j++) if (j != i) cand.Add(j);
            int ii = i;
            cand.Sort((p, q) => Dist2(g, ii, p).CompareTo(Dist2(g, ii, q)));
            float radius = W * LinkNearK;
            float cap = W * LinkMaxK;
            int links = 0;
            for (int round = 0; round < 8; round++) {
                for (int k = 0; k < cand.Count && links < LinkMax; k++) {
                    int j = cand[k];
                    float d = (float)Math.Sqrt(Dist2(g, i, j));
                    if (d > radius) break;
                    if (d < 14f || HasEdge(g, i, j)) continue;
                    AddEdge(g, rng, i, j);
                    links++;
                }
                if (links >= LinkMin || radius >= cap) break;
                radius *= LinkGrow;
                if (radius > cap) radius = cap;
            }
            // （不留"跨簇长连线"：那种一两条横穿画面的直线是"乱"的主要来源）
        }
        return g;
    }

    private static float Dist2(Graph g, int a, int b) {
        float dx = g.X[a] - g.X[b], dy = g.Y[a] - g.Y[b];
        return dx * dx + dy * dy;
    }

    private static bool HasEdge(Graph g, int a, int b) {
        for (int e = 0; e < g.EA.Count; e++) {
            if ((g.EA[e] == a && g.EB[e] == b) || (g.EA[e] == b && g.EB[e] == a)) return true;
        }
        return false;
    }

    private static void AddEdge(Graph g, Rng rng, int a, int b) {
        g.EA.Add(a); g.EB.Add(b);
        g.EBend.Add(rng.Range(-BendK, BendK) * (float)Math.Sqrt(Dist2(g, a, b)));
    }

    /// <summary>连线的控制点（轻微弯曲的二次贝塞尔）</summary>
    private static void Bend(Graph g, int e, out float bx, out float by) {
        float x0 = g.X[g.EA[e]], y0 = g.Y[g.EA[e]], x1 = g.X[g.EB[e]], y1 = g.Y[g.EB[e]];
        float dx = x1 - x0, dy = y1 - y0;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) { bx = x0; by = y0; return; }
        float nx = -dy / len, ny = dx / len;
        bx = (x0 + x1) * 0.5f + nx * g.EBend[e];
        by = (y0 + y1) * 0.5f + ny * g.EBend[e];
    }

    // ================= 哪些线交给运行时呼吸 =================

    private static bool[] _skip;
    private static bool[] Skip() {
        var g = G();
        if (_skip == null) {
            _skip = new bool[g.EA.Count];
            var idx = new List<int>();
            for (int e = 0; e < g.EA.Count; e++) idx.Add(e);
            idx.Sort((p, q) => Dist2(g, g.EA[q], g.EB[q]).CompareTo(Dist2(g, g.EA[p], g.EB[p])));
            int n = Math.Min(BreatheLines, idx.Count);
            for (int k = 0; k < n; k++) _skip[idx[k]] = true;
        }
        return _skip;
    }

    /// <summary>需要单独呼吸的那批线：`{条数, 每条 4 个数 x0,y0,x1,y1}`（含弯曲用的折点由运行时自己插）</summary>
    public static float[] BakeBreatheLines() {
        var g = G();
        var skip = Skip();
        var flat = new List<float>();
        int n = 0;
        for (int e = 0; e < g.EA.Count; e++) {
            if (!skip[e]) continue;
            float bx, by;
            Bend(g, e, out bx, out by);
            flat.Add(g.X[g.EA[e]]); flat.Add(g.Y[g.EA[e]]);
            flat.Add(bx); flat.Add(by);
            flat.Add(g.X[g.EB[e]]); flat.Add(g.Y[g.EB[e]]);
            n++;
        }
        var res = new float[flat.Count + 1];
        res[0] = n;
        flat.CopyTo(res, 1);
        return res;
    }

    // ================= 画 =================

    /// <summary>底图：细线 + 节点（大节点带一圈淡辉光）。最长的那批线不画（运行时单独呼吸）</summary>
    public static byte[] BakeTopo() {
        var g = G();
        var skip = Skip();
        var buf = new float[W * H];
        for (int e = 0; e < g.EA.Count; e++) {
            if (skip[e]) continue;
            float bx, by;
            Bend(g, e, out bx, out by);
            StrokeQuad(buf, g.X[g.EA[e]], g.Y[g.EA[e]], bx, by, g.X[g.EB[e]], g.Y[g.EB[e]]);
        }
        for (int i = 0; i < Nodes; i++) {
            if (g.Hub[i]) {
                Stamp(buf, g.X[i], g.Y[i], HubHalo, HubAmp * 0.10f);
                Stamp(buf, g.X[i], g.Y[i], HubR * 0.5f + LineGlow, HubAmp * 0.30f);
                Stamp(buf, g.X[i], g.Y[i], HubR, HubAmp);
            } else {
                Stamp(buf, g.X[i], g.Y[i], DotR * g.R[i] + LineGlow, DotAmp * 0.26f);
                Stamp(buf, g.X[i], g.Y[i], DotR * g.R[i], DotAmp);
            }
        }
        return ToRgba(buf);
    }

    /// <summary>一条轻弯的连线：二次贝塞尔等分采样，每点盖一颗柔和圆斑</summary>
    private static void StrokeQuad(float[] buf, float x0, float y0, float bx, float by, float x1, float y1) {
        float approx = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 1.25f;
        int steps = Math.Max(4, (int)(approx * 0.6f));
        for (int s = 0; s <= steps; s++) {
            float t = s / (float)steps, mt = 1f - t;
            float x = mt * mt * x0 + 2f * mt * t * bx + t * t * x1;
            float y = mt * mt * y0 + 2f * mt * t * by + t * t * y1;
            Stamp(buf, x, y, LineGlow * 0.5f, LineAmp * 0.22f);
            Stamp(buf, x, y, LineCore * 0.5f, LineAmp);
        }
    }

    private static void Stamp(float[] buf, float cx, float cy, float r, float amp) {
        if (r < 0.3f) r = 0.3f;
        int x0 = Math.Max(0, (int)(cx - r - 2f)), x1 = Math.Min(W - 1, (int)(cx + r + 2f));
        int y0 = Math.Max(0, (int)(cy - r - 2f)), y1 = Math.Min(H - 1, (int)(cy + r + 2f));
        for (int y = y0; y <= y1; y++) {
            float dy = y + 0.5f - cy;
            for (int x = x0; x <= x1; x++) {
                float dx = x + 0.5f - cx;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                float a = Clamp01(r - d + 0.5f) * amp;
                if (a <= 0f) continue;
                int i = y * W + x;
                if (a > buf[i]) buf[i] = a;
            }
        }
    }

    private static byte[] ToRgba(float[] buf) {
        var outb = new byte[W * H * 4];
        for (int i = 0; i < buf.Length; i++) {
            int a = (int)(Clamp01(buf[i]) * 255f + 0.5f);
            int o = i * 4;
            outb[o] = 255; outb[o + 1] = 255; outb[o + 2] = 255; outb[o + 3] = (byte)a;
        }
        return outb;
    }

    private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

    // ================= 光点走的路径 =================

    /// <summary>光点走的路：从一个节点出发，每次挑一个"顺着原来方向"的邻居往下走 3~5 跳，
    /// 折点带上（光点走的才是玩家看到的那条轻弯线）。返回 `{条数, 每条的点数, 然后 x,y 交替…}`。</summary>
    public static float[] BakePaths() {
        var g = G();
        var rng = new Rng(SeedTopo ^ 0x5A5A5A5Au);
        var adj = new List<int>[g.Count];
        for (int i = 0; i < g.Count; i++) adj[i] = new List<int>();
        for (int e = 0; e < g.EA.Count; e++) { adj[g.EA[e]].Add(e); adj[g.EB[e]].Add(e); }
        var flat = new List<float>();
        int made = 0, want = PathTarget;
        for (int tries = 0; tries < want * 12 && made < want; tries++) {
            int start = (int)(rng.F() * g.Count);
            if (adj[start].Count == 0) continue;
            int hops = 3 + (int)(rng.F() * 3f);          // 3~5 跳
            var pts = new List<float>();
            pts.Add(g.X[start]); pts.Add(g.Y[start]);
            int cur = start, prevEdge = -1;
            float dirX = 0f, dirY = 0f;
            for (int step = 0; step < hops; step++) {
                int pick = -1;
                float best = -2f;
                for (int k = 0; k < adj[cur].Count; k++) {
                    int e = adj[cur][k];
                    if (e == prevEdge) continue;
                    int other = (g.EA[e] == cur) ? g.EB[e] : g.EA[e];
                    float vx = g.X[other] - g.X[cur], vy = g.Y[other] - g.Y[cur];
                    float vl = (float)Math.Sqrt(vx * vx + vy * vy);
                    if (vl < 0.001f) continue;
                    vx /= vl; vy /= vl;
                    float dot = (step == 0) ? 0f : (vx * dirX + vy * dirY);
                    if (step > 0 && dot < PathTurnMinDot) continue;   // 回头弯直接不要
                    float score = (step == 0) ? rng.F() : dot + rng.Range(-0.05f, 0.05f);
                    if (score > best) { best = score; pick = e; }
                }
                if (pick < 0) break;
                int nxt = (g.EA[pick] == cur) ? g.EB[pick] : g.EA[pick];
                float bx, by;
                Bend(g, pick, out bx, out by);
                pts.Add(bx); pts.Add(by);
                pts.Add(g.X[nxt]); pts.Add(g.Y[nxt]);
                float dxn = g.X[nxt] - g.X[cur], dyn = g.Y[nxt] - g.Y[cur];
                float dl = (float)Math.Sqrt(dxn * dxn + dyn * dyn);
                if (dl > 0.001f) { dirX = dxn / dl; dirY = dyn / dl; }
                prevEdge = pick; cur = nxt;
            }
            if (pts.Count < 6) continue;
            var rounded = RoundCorners(pts);
            flat.Add(rounded.Count / 2);
            flat.AddRange(rounded);
            made++;
        }
        var res = new float[flat.Count + 1];
        res[0] = made;
        flat.CopyTo(res, 1);
        return res;
    }

    /// <summary>把每个折点换成半径 R 的小圆角（切点之间拉直、圆角处按二次贝塞尔密化成几段）。
    /// 半径封顶到"两侧线段较短那条的 42%"，短边上不会把整段圆角吃掉。
    /// 输入输出都是 x,y 交替的扁平表（纹理像素）。</summary>
    private static List<float> RoundCorners(List<float> pts) {
        int n = pts.Count / 2;
        if (n < 3) return new List<float>(pts);
        var outPts = new List<float>(pts.Count * 2) { pts[0], pts[1] };
        for (int k = 1; k < n - 1; k++) {
            float ax = pts[(k - 1) * 2], ay = pts[(k - 1) * 2 + 1];
            float vx = pts[k * 2], vy = pts[k * 2 + 1];
            float bx = pts[(k + 1) * 2], by = pts[(k + 1) * 2 + 1];
            float d1x = vx - ax, d1y = vy - ay, d2x = bx - vx, d2y = by - vy;
            float l1 = (float)Math.Sqrt(d1x * d1x + d1y * d1y);
            float l2 = (float)Math.Sqrt(d2x * d2x + d2y * d2y);
            if (l1 < 0.01f || l2 < 0.01f) { AddIfFar(outPts, vx, vy); continue; }
            float r = Math.Min(PathCornerRadius, Math.Min(l1, l2) * 0.42f);
            float p0x = vx - d1x / l1 * r, p0y = vy - d1y / l1 * r;
            float p1x = vx + d2x / l2 * r, p1y = vy + d2y / l2 * r;
            AddIfFar(outPts, p0x, p0y);
            for (int q = 1; q < PathCornerSteps; q++) {
                float t2 = q / (float)PathCornerSteps, mt = 1f - t2;
                AddIfFar(outPts, mt * mt * p0x + 2f * mt * t2 * vx + t2 * t2 * p1x,
                         mt * mt * p0y + 2f * mt * t2 * vy + t2 * t2 * p1y);
            }
            AddIfFar(outPts, p1x, p1y);
        }
        AddIfFar(outPts, pts[(n - 1) * 2], pts[(n - 1) * 2 + 1]);
        return outPts;
    }

    /// <summary>加一个点，但和上一个太近就不加</summary>
    private static void AddIfFar(List<float> pts, float x, float y) {
        int m = pts.Count;
        if (m >= 2) {
            float dx = x - pts[m - 2], dy = y - pts[m - 1];
            if (dx * dx + dy * dy < PathMinSegSq) return;
        }
        pts.Add(x); pts.Add(y);
    }

    /// <summary>光点条数（运行时按这个数建小图元）</summary>
    public const int PathTarget = 96;
    // 折角抹圆与转向上限：光点在折点上直角转弯很难看。半径封顶到"两侧线段较短那条的 42%"
    // （短边上抹不出大弧），所以转向本身也得收紧，两条要一起用。
    public const float PathCornerRadius = 13f;
    public const int PathCornerSteps = 6;
    /// <summary>允许的最小转向（点积）：低于它就当回头弯、这一条路径到此为止。
    /// 0 是直角，负数是回头；取 0.5 等于"最多转 60 度"。</summary>
    public const float PathTurnMinDot = 0.50f;
    /// <summary>密化出来的点太近就不收：那种点对形状没有贡献，却会让走向变成噪声</summary>
    private const float PathMinSegSq = 1.44f;      // 1.2 像素的平方
}
