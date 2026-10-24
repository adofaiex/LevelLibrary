using System;
using System.Collections.Generic;

namespace LevelLibrary;

/// <summary>代码绘制的「神经拓扑」底图：纯算法烘焙，不碰 UnityEngine，产出 RGBA32 原始像素字节。
/// 像素按 Unity 的约定自下而上排列（y = 0 是最下面一行），调用方用 Texture2D.LoadRawTextureData 直接吃。
/// 同一颗种子必须烘出同一份字节，预览台与验证台都靠这条不变式。</summary>
internal static class NetBgBake {

    // ---- 三种图样的尺寸（改手感连同下面的节点/线宽一起动）----
    public const int CoreW = 1280, CoreH = 720;   // 中心密集的主结构
    public const int TileSize = 448;              // 无缝平铺的那两层
    public const int VigSize = 256;               // 四周压暗的暗角

    // ---- 两颗种子与各层的浓淡：预览台和运行时都从这里取，别再各写一份 ----
    private const uint SeedCore = 0x5EED01u;
    private const uint SeedTileFar = 0x5EED02u;
    private const uint SeedTileNear = 0x5EED03u;
    private const float TileFarClose = 0.155f;   // 远层：连得更远、更稀
    private const float TileFarDim = 0.52f;
    private const float TileNearClose = 0.098f;  // 近层：连得近、更细碎
    private const float VignetteAlpha = 0.68f;

    // 密度律是"按椭圆往里收"的：横竖用同一个归一化半径，可屏幕是 16:9，
    // 于是左右两边比上下先变稀（量出来横向两个中段只有纵向的 0.56 倍）。把横向的口径单独放宽，
    // 两边才铺得开；纵向不动，中间密边上疏的层次照旧。暗角同理要横向撑宽，
    // 不然左右两条边比上下更早黑掉（不加宽时两边压出来的黑度一模一样）。
    private const float CoreWidenX = 1.34f;
    private const float VigWidenX = 1.28f;

    private const int CoreNodes = 270;
    private const int CoreHubs = 16;        // 正中心先钉一圈"核心神经元"，免得随机采样在中间留个洞
    private const int TileNodes = 58;
    private const float LineCore = 1.05f;   // 线芯宽（纹理像素）
    private const float LineGlow = 3.4f;
    private const float DotCore = 2.4f;
    private const float DotGlow = 7.2f;
    private const float EdgeAmp = 0.46f;    // 线的亮度上限
    private const float NodeAmp = 1.0f;     // 点的亮度上限
    private const float CoreHubAmp = 0.10f; // 正中心那团很淡的辉光（给"中间密"加一层纵深）

    /// <summary>确定性随机：xorshift32，跨运行/跨机器同一颗种子给出同一串数</summary>
    private struct Rng {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0u ? 0x9E3779B9u : seed; }
        public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        public float F() { return (Next() >> 8) * (1f / 16777216f); }
        public float Range(float a, float b) { return a + (b - a) * F(); }
    }

    private sealed class Graph {
        public float[] X, Y, S;      // 节点坐标与大小系数
        public int Count;
        public readonly List<int> EA = new List<int>();
        public readonly List<int> EB = new List<int>();
    }

    // ================= 三种图样 =================

    /// <summary>中心那层的图缓存在这儿：底图与"光点走的路径"必须来自同一张图，
    /// 各自 BuildGraph 一次虽然种子相同也会一样，但缓存住能保证以后改参数时不会只改一边。</summary>
    private static Graph _coreGraph;
    private static Graph CoreGraph() {
        if (_coreGraph == null) _coreGraph = BuildGraph(new Rng(SeedCore), CoreW, CoreH, CoreNodes, true, 105f, 3);
        return _coreGraph;
    }

    /// <summary>中心密集的主结构：极坐标上按半径拒绝采样，越靠边节点越少，连线也更短更暗。
    /// 最长的那一批线不烘进底图，改由运行时单独画（见 BakeBreatheLines）——
    /// 它们要各自呼吸明暗，烘成一张图就只能整层一起亮暗了。</summary>
    public static byte[] BakeCore(uint seed) {
        var g = CoreGraph();
        var skip = BreatheEdgeFlags(g);
        var buf = new float[CoreW * CoreH];
        DrawEdges(buf, CoreW, CoreH, g, false, 0.9f, skip);
        DrawNodes(buf, CoreW, CoreH, g, false);
        AddCentreHaze(buf, CoreW, CoreH);
        return ToRgba(buf, CoreW, CoreH);
    }

    /// <summary>无缝平铺的一层：节点与连线都按 3×3 副本画，平移接缝处不会断线。
    /// close = 连线的最大距离系数（小的那层看起来更细碎）。</summary>
    public static byte[] BakeTile(uint seed, float close, float dim) {
        var rng = new Rng(seed);
        var g = BuildGraph(rng, TileSize, TileSize, TileNodes, false, TileSize * close, 3);
        var buf = new float[TileSize * TileSize];
        DrawEdges(buf, TileSize, TileSize, g, true, dim);
        DrawNodes(buf, TileSize, TileSize, g, true);
        if (dim < 1f) Dim(buf, dim);
        return ToRgba(buf, TileSize, TileSize);
    }

    /// <summary>暗角：正中间透明、四周渐黑（方图拉满整屏后自然成椭圆，横向再撑宽一点，
    /// 免得左右两条边比上下更早黑掉）。RGB 必须是黑：这张图靠 alpha 去"压暗别人"，白 RGB 会变成往四周抹白。</summary>
    public static byte[] BakeVignette() {
        var buf = new float[VigSize * VigSize];
        float c = (VigSize - 1) * 0.5f;
        for (int y = 0; y < VigSize; y++) {
            for (int x = 0; x < VigSize; x++) {
                float dx = (x - c) / c / VigWidenX, dy = (y - c) / c;
                float r = (float)Math.Sqrt(dx * dx + dy * dy);
                float t = Clamp01((r - 0.34f) / 0.78f);
                buf[y * VigSize + x] = t * t * (3f - 2f * t) * VignetteAlpha;
            }
        }
        return ToRgba(buf, VigSize, VigSize, false);
    }

    // ---- 会单独呼吸的线（不烘进底图，运行时一条两个图元）----

    public const int BreatheLineCount = 80;     // 挑最长的这些条：长线在画面上最显眼，呼吸起来看得出
    public const float BreatheSecMin = 5.5f;    // 每条线自己的呼吸周期（互不相同才叫"不要一起明暗"）
    public const float BreatheSecMax = 9.5f;
    public const float BreatheAlphaMin = 0.22f; // 最暗时留多少（全灭会让网络看起来在闪断）
    public const float BreatheAlphaMax = 1.0f;

    /// <summary>哪几条边交给运行时单独画：按长度取最长的前 N 条（确定性，同一张图每次一样）。</summary>
    private static bool[] BreatheEdgeFlags(Graph g) {
        var skip = new bool[g.EA.Count];
        var idx = new List<int>();
        for (int e = 0; e < g.EA.Count; e++) idx.Add(e);
        idx.Sort((x, y) => EdgeLen(g, y).CompareTo(EdgeLen(g, x)));
        int n = Math.Min(BreatheLineCount, idx.Count);
        for (int k = 0; k < n; k++) skip[idx[k]] = true;
        return skip;
    }

    private static float EdgeLen(Graph g, int e) {
        int a = g.EA[e], b = g.EB[e];
        float dx = g.X[b] - g.X[a], dy = g.Y[b] - g.Y[a];
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>要单独呼吸的那些线（纹理像素，y 自下而上）：每条 `x0,y0, 折点x,折点y, x1,y1`。
    /// 与底图共用同一个折点算法，所以线画在哪儿、光点就走在哪儿。</summary>
    public static float[] BakeBreatheLines() {
        var g = CoreGraph();
        var skip = BreatheEdgeFlags(g);
        var flat = new List<float>();
        int made = 0;
        for (int e = 0; e < g.EA.Count; e++) {
            if (!skip[e]) continue;
            int a = g.EA[e], b = g.EB[e];
            float cx, cy;
            EdgeBend(g, e, out cx, out cy);
            flat.Add(g.X[a]); flat.Add(g.Y[a]);
            flat.Add(cx); flat.Add(cy);
            flat.Add(g.X[b]); flat.Add(g.Y[b]);
            made++;
        }
        var res = new float[flat.Count + 1];
        res[0] = made;
        flat.CopyTo(res, 1);
        return res;
    }

    /// <summary>每条线的呼吸相位与周期（从边的下标哈希算出来，不用运行时随机）：
    /// 相位散开、周期也各不相同，才不会所有线一起亮暗。</summary>
    public static void BreatheStateAt(int lineIdx, float t, out float alpha) {
        float phase = Hash01(lineIdx * 71 + 13) * 6.2831853f;
        float period = BreatheSecMin + (BreatheSecMax - BreatheSecMin) * Hash01(lineIdx * 29 + 5);
        float s = 0.5f + 0.5f * (float)Math.Sin(t * 6.2831853f / period + phase);
        alpha = BreatheAlphaMin + (BreatheAlphaMax - BreatheAlphaMin) * s;
    }

    // ---- 四层的统一入口（运行时与预览台都调这四个，参数只此一份）----

    public static byte[] BakeCoreTex() { return BakeCore(SeedCore); }
    public static byte[] BakeTileFarTex() { return BakeTile(SeedTileFar, TileFarClose, TileFarDim); }
    public static byte[] BakeTileNearTex() { return BakeTile(SeedTileNear, TileNearClose, 1f); }

    // ---- 光点走的路径（同一张图，见 CoreGraph）----

    public const int PathCount = 120;       // 备多少条路径给光点挑（运行时按需循环取用）
    public const int PathMaxSteps = 4;      // 一条路径最多串几段边
    private const float PathMinLenK = 0.12f;   // 太短的路径不要：光点还没流起来就到头了
    // 折角抹圆：光点在折点上直角转弯很难看，但不能把它挪离线（那样就飘到空白处了），
    // 所以半径封顶到"两侧线段较短那条的 42%"——短边上抹不出大弧，那里只能靠转向本身别太急。
    public const float PathCornerRadius = 11f;
    public const int PathCornerSteps = 6;       // 每个折角密化成几段（密一点走向才连续）
    /// <summary>允许的最小转向（点积）：低于它就当"回头弯"、这一条走到底为止。
    /// 0 是直角，负数是回头；取 0.5 等于"最多转 60 度"。
    /// 收这么紧是因为折角抹圆在短边上只能抹出一小段弧，转向越接近直角、量出来的转角就越接近它本身
    /// （放到 0.05 时量出来能到 73 度，只剩两度余量）。</summary>
    private const float NetBakeTurnMinDot = 0.50f;

    // ---- 光点的排班（纯函数，运行时与预览台共用这一份）----
    public const int PulseCount = 44;            // 同时在场上限（每个光点在自己那条路径上轮流上路）
    public const float PulseLife = 10.0f;      // 一个光点走完一条路径用几秒（再快就显不出缓速了）
    public const float PulseDelayMin = 1.0f;    // 走完歇多久再上路（参差不齐才像"生成、消失"）
    public const float PulseDelayMax = 6.5f;
    public const float PulseFadeK = 0.30f;      // 生命周期头尾各占多少比例在淡入 / 淡出

    /// <summary>确定性哈希到 [0,1)：光点的相位、间隔都从序号算出来，不用运行时随机 ——
    /// 这样任何一屏、任何一帧算出来的都是同一个画面，交叉淡入时才不会互相打架。</summary>
    public static float Hash01(int v) {
        unchecked {
            uint x = (uint)v * 2654435761u;
            x ^= x >> 15; x *= 2246822519u;
            x ^= x >> 13;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>第 i 个光点在 t 时刻的状态。返回 false = 这一刻它还没生成或已经消失；
    /// 否则给出该走哪条路径（每次上路换下一条）、沿线走了多少（0~1）与亮度系数（头尾淡入淡出，0~1）。</summary>
    public static bool PulseStateAt(int i, float t, int pathCount, out int pathIdx, out float prog, out float alpha) {
        pathIdx = 0; prog = 0f; alpha = 0f;
        if (pathCount <= 0) return false;
        float phase = Hash01(i * 17 + 3) * 7f;
        float delay = PulseDelayMin + (PulseDelayMax - PulseDelayMin) * Hash01(i * 53 + 11);
        float period = PulseLife + delay;
        float abs = t + phase;
        int cycle = (int)(abs / period);
        float tt = abs - cycle * period;
        if (tt >= PulseLife) return false;
        pathIdx = (i + cycle) % pathCount;
        prog = tt / PulseLife;
        float a = Math.Min(Clamp01(prog / PulseFadeK), Clamp01((1f - prog) / PulseFadeK));
        alpha = a * a * (3f - 2f * a);
        return true;
    }

    /// <summary>在一张图上从随机节点出发、顺着同方向一直走，把每一段的折点都带上，光点走的才是玩家真看到的那条弯线。
    /// 返回 `{条数, 每条的点数, 然后 x,y 交替…}`（纹理像素坐标，y 自下而上，与底图同一套）。</summary>
    public static float[] BakeCorePaths() {
        var g = CoreGraph();
        var rng = new Rng(SeedCore ^ 0x5A5A5A5Au);
        var flat = new List<float>();
        var adj = new List<int>[g.Count];
        for (int i = 0; i < g.Count; i++) adj[i] = new List<int>();
        for (int e = 0; e < g.EA.Count; e++) { adj[g.EA[e]].Add(e); adj[g.EB[e]].Add(e); }
        int made = 0;
        for (int tries = 0; tries < PathCount * 8 && made < PathCount; tries++) {
            int start = (int)(rng.F() * g.Count);
            if (adj[start].Count == 0) continue;
            var pts = new List<float>();
            pts.Add(g.X[start]); pts.Add(g.Y[start]);
            int cur = start, prevEdge = -1;
            float dirX = 0f, dirY = 0f, totalLen = 0f;
            for (int step = 0; step < PathMaxSteps; step++) {
                int pick = -1;
                float bestScore = -2f;
                for (int k = 0; k < adj[cur].Count; k++) {
                    int e = adj[cur][k];
                    if (e == prevEdge) continue;
                    int other = (g.EA[e] == cur) ? g.EB[e] : g.EA[e];
                    float vx = g.X[other] - g.X[cur], vy = g.Y[other] - g.Y[cur];
                    float vl = (float)Math.Sqrt(vx * vx + vy * vy);
                    if (vl < 0.001f) continue;
                    vx /= vl; vy /= vl;
                    float dot = (step == 0) ? 0f : (vx * dirX + vy * dirY);
                    // 回头弯直接不要：没得选的时候掉头，光点会在那儿原地折返（量出来是 180 度）
                    // ， 这一条路径就在这儿结束，剩下的交给 PathMinLenK 那道长度筛子。
                    if (step > 0 && dot < NetBakeTurnMinDot) continue;
                    // 第一步随便挑，之后尽量"顺着原来的方向继续走"（点积越大越是直着往前）
                    float score = (step == 0) ? rng.F() : dot;
                    if (score > bestScore) { bestScore = score; pick = e; }
                }
                if (pick < 0) break;
                int nxt = (g.EA[pick] == cur) ? g.EB[pick] : g.EA[pick];
                float bx, by;
                EdgeBend(g, pick, out bx, out by);
                pts.Add(bx); pts.Add(by);
                pts.Add(g.X[nxt]); pts.Add(g.Y[nxt]);
                float dxn = g.X[nxt] - g.X[cur], dyn = g.Y[nxt] - g.Y[cur];
                float dl = (float)Math.Sqrt(dxn * dxn + dyn * dyn);
                if (dl > 0.001f) { dirX = dxn / dl; dirY = dyn / dl; }
                totalLen += dl;
                prevEdge = pick; cur = nxt;
            }
            if (pts.Count < 6 || totalLen < CoreW * PathMinLenK) continue;
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

    /// <summary>把每个折点换成半径 R 的小圆角（切点之间拉直，圆角处按二次贝塞尔密化成几段）。
    /// 半径封顶到"两侧线段较短那条的 42%"，短边上不会把整段圆角吃掉。
    /// 输入输出都是 x,y 交替的扁平表（纹理像素），与其它路径数据同一套约定。</summary>
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
                float t = q / (float)PathCornerSteps, mt = 1f - t;
                AddIfFar(outPts, mt * mt * p0x + 2f * mt * t * vx + t * t * p1x,
                         mt * mt * p0y + 2f * mt * t * vy + t * t * p1y);
            }
            AddIfFar(outPts, p1x, p1y);
        }
        AddIfFar(outPts, pts[(n - 1) * 2], pts[(n - 1) * 2 + 1]);
        return outPts;
    }

    /// <summary>加一个点，但和上一个太近就不加：密化出来的点可以挨到 0.2 像素，那种点对形状没有贡献，
    /// 却会让"走向"变成噪声（相邻两点的方向抖得厉害，运行时按窗口平均方向时也会被它带偏）。
    /// 最小间距 1.2 纹理像素 ≈ 1.8 画布像素，比光点小一个数量级，看不出来。</summary>
    private static void AddIfFar(List<float> pts, float x, float y) {
        int m = pts.Count;
        if (m >= 2) {
            float dx = x - pts[m - 2], dy = y - pts[m - 1];
            if (dx * dx + dy * dy < NetBakeMinSegSq) return;
        }
        pts.Add(x); pts.Add(y);
    }
    private const float NetBakeMinSegSq = 1.44f;   // 1.2 像素的平方

    // ================= 生成 =================

    private static Graph BuildGraph(Rng rng, int w, int h, int want, bool radial, float maxD, int k) {
        var g = new Graph();
        g.X = new float[want + CoreHubs + 4];
        g.Y = new float[want + CoreHubs + 4];
        g.S = new float[want + CoreHubs + 4];
        if (radial) SeedCoreCluster(g, rng, w, h);
        int guard = want * 60;
        while (g.Count < want + (radial ? CoreHubs + 1 : 0) && guard-- > 0) {
            float x = rng.Range(-0.03f * w, 1.03f * w);
            float y = rng.Range(-0.03f * h, 1.03f * h);
            if (radial) {
                float dx = (x - w * 0.5f) / (w * 0.5f * CoreWidenX), dy = (y - h * 0.5f) / (h * 0.5f);
                float r = (float)Math.Sqrt(dx * dx + dy * dy);
                float p = 1f - 0.90f * (float)Math.Pow(Math.Min(1.15f, r), 1.5f);
                if (rng.F() > Math.Max(0.05f, p)) continue;
            }
            int i = g.Count++;
            g.X[i] = x; g.Y[i] = y;
            g.S[i] = rng.F() < 0.12f ? rng.Range(1.5f, 2.1f) : rng.Range(0.75f, 1.15f);
        }
        Link(g, rng, maxD);
        return g;
    }

    /// <summary>中心先钉一圈核心神经元：一个枢纽加黄金角铺开的十几个，保证"中间密"不靠运气。
    /// 纯随机采样在这些点上会留洞（第一版正中间就是空的）。</summary>
    private static void SeedCoreCluster(Graph g, Rng rng, int w, int h) {
        int hub = g.Count++;
        g.X[hub] = w * 0.5f; g.Y[hub] = h * 0.5f; g.S[hub] = 2.2f;
        float rr = Math.Min(w, h) * 0.19f;
        for (int i = 0; i < CoreHubs; i++) {
            float ang = i * 2.399963f;
            float rad = (float)Math.Sqrt((i + 0.5f) / CoreHubs) * rr;
            int n = g.Count++;
            g.X[n] = g.X[hub] + (float)Math.Cos(ang) * rad;
            g.Y[n] = g.Y[hub] + (float)Math.Sin(ang) * rad;
            g.S[n] = rng.F() < 0.4f ? rng.Range(1.25f, 1.7f) : rng.Range(0.85f, 1.15f);
        }
    }

    /// <summary>连边：多数节点只连 2 根（枝状/链状），少数"胞体"节点连 6 根（树突放射状）——
    /// 这才是神经的样子；人人连 3 根会到处闭合成三角，整张图变成一张网格。
    /// 前两条边还不许和已有的边共用邻居（同样是为了避开三角），再随机撒少量长边当轴突。</summary>
    private static void Link(Graph g, Rng rng, float maxD) {
        for (int i = 0; i < g.Count; i++) {
            int want = g.S[i] >= 1.45f ? 6 : 2;
            int made = 0, guard = 40;
            while (made < want && guard-- > 0) {
                int best = -1; float bestD = maxD * maxD;
                for (int j = 0; j < g.Count; j++) {
                    if (j == i || Linked(g, i, j)) continue;
                    if (made < 2 && SharesNeighbour(g, i, j)) continue;
                    float dx = g.X[j] - g.X[i], dy = g.Y[j] - g.Y[i];
                    float d2 = dx * dx + dy * dy;
                    if (d2 < bestD) { bestD = d2; best = j; }
                }
                if (best < 0) break;
                g.EA.Add(i); g.EB.Add(best); made++;
            }
            if (rng.F() < 0.07f) {
                int j = (int)(rng.F() * g.Count);
                if (j != i && !Linked(g, i, j)) { g.EA.Add(i); g.EB.Add(j); }
            }
        }
    }

    private static bool Linked(Graph g, int a, int b) {
        for (int e = 0; e < g.EA.Count; e++) {
            int x = g.EA[e], y = g.EB[e];
            if ((x == a && y == b) || (x == b && y == a)) return true;
        }
        return false;
    }

    /// <summary>i 与 j 之间已经有"共同邻居"了吗（有的话再连一条就闭合出一个三角）</summary>
    private static bool SharesNeighbour(Graph g, int i, int j) {
        for (int e = 0; e < g.EA.Count; e++) {
            int x = g.EA[e], y = g.EB[e];
            if (x == i) { if (Linked(g, y, j)) return true; }
            else if (y == i) { if (Linked(g, x, j)) return true; }
        }
        return false;
    }

    // ================= 光栅化 =================

    private static void DrawEdges(float[] buf, int w, int h, Graph g, bool wrap, float amp, bool[] skip = null) {
        for (int e = 0; e < g.EA.Count; e++) {
            if (skip != null && e < skip.Length && skip[e]) continue;   // 这一条交给运行时单独画
            int a = g.EA[e], b = g.EB[e];
            float x0 = g.X[a], y0 = g.Y[a], x1 = g.X[b], y1 = g.Y[b];
            float dx = x1 - x0, dy = y1 - y0;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            // 越长的边越暗：近邻连得很亮、跨区的那几根只留一点痕迹
            float k = Math.Max(0.14f, 1f - len / (w * 0.30f));
            float A = EdgeAmp * amp * k;
            // 折一道：全是直连的图看着像网格，稍微弯一点才像树突
            float cx, cy;
            EdgeBend(g, e, out cx, out cy);
            for (int oy = -1; oy <= 1; oy++) {
                for (int ox = -1; ox <= 1; ox++) {
                    if (!wrap && (ox != 0 || oy != 0)) continue;
                    float oxp = ox * w, oyp = oy * h;
                    DrawSeg(buf, w, h, x0 + oxp, y0 + oyp, cx + oxp, cy + oyp, A);
                    DrawSeg(buf, w, h, cx + oxp, cy + oyp, x1 + oxp, y1 + oyp, A * 0.94f);
                }
            }
        }
    }

    /// <summary>第 e 条边画出来的折点（中点沿法线偏一小段）。画线与"光点走的路径"都用它，
    /// 这样光点一定落在画出来的那条弯线上，而不是落在两端连成的直线上。</summary>
    private static void EdgeBend(Graph g, int e, out float cx, out float cy) {
        int a = g.EA[e], b = g.EB[e];
        float x0 = g.X[a], y0 = g.Y[a], x1 = g.X[b], y1 = g.Y[b];
        float dx = x1 - x0, dy = y1 - y0;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        float px = -dy, py = dx;
        float pl = (float)Math.Sqrt(px * px + py * py);
        float bend = 0f;
        if (pl > 0.001f) bend = (Hash01(a * 7919 + b * 104729) - 0.5f) * 0.22f * len / pl;
        cx = (x0 + x1) * 0.5f + px * bend;
        cy = (y0 + y1) * 0.5f + py * bend;
    }

    private static void DrawSeg(float[] buf, int w, int h, float x0, float y0, float x1, float y1, float amp) {
        float pad = LineGlow + 1f;
        int ix0 = (int)Math.Floor(Math.Min(x0, x1) - pad), ix1 = (int)Math.Ceiling(Math.Max(x0, x1) + pad);
        int iy0 = (int)Math.Floor(Math.Min(y0, y1) - pad), iy1 = (int)Math.Ceiling(Math.Max(y0, y1) + pad);
        if (ix1 < 0 || iy1 < 0 || ix0 >= w || iy0 >= h) return;
        if (ix0 < 0) ix0 = 0; if (iy0 < 0) iy0 = 0;
        if (ix1 > w - 1) ix1 = w - 1; if (iy1 > h - 1) iy1 = h - 1;
        float vx = x1 - x0, vy = y1 - y0;
        float vv = vx * vx + vy * vy;
        if (vv < 0.0001f) return;
        for (int y = iy0; y <= iy1; y++) {
            float py = y + 0.5f;
            int row = y * w;
            for (int x = ix0; x <= ix1; x++) {
                float px = x + 0.5f;
                float t = ((px - x0) * vx + (py - y0) * vy) / vv;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                float ddx = px - (x0 + t * vx), ddy = py - (y0 + t * vy);
                float d = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                float a = Falloff(d, LineCore, LineGlow);
                if (a > 0f) buf[row + x] += a * amp;
            }
        }
    }

    private static void DrawNodes(float[] buf, int w, int h, Graph g, bool wrap) {
        for (int i = 0; i < g.Count; i++) {
            float x = g.X[i], y = g.Y[i], s = g.S[i];
            for (int oy = -1; oy <= 1; oy++) {
                for (int ox = -1; ox <= 1; ox++) {
                    if (!wrap && (ox != 0 || oy != 0)) continue;
                    DrawDot(buf, w, h, x + ox * w, y + oy * h, s);
                }
            }
        }
    }

    private static void DrawDot(float[] buf, int w, int h, float cx, float cy, float s) {
        float rc = DotCore * s, rg = DotGlow * s;
        int ix0 = (int)Math.Floor(cx - rg - 1f), ix1 = (int)Math.Ceiling(cx + rg + 1f);
        int iy0 = (int)Math.Floor(cy - rg - 1f), iy1 = (int)Math.Ceiling(cy + rg + 1f);
        if (ix1 < 0 || iy1 < 0 || ix0 >= w || iy0 >= h) return;
        if (ix0 < 0) ix0 = 0; if (iy0 < 0) iy0 = 0;
        if (ix1 > w - 1) ix1 = w - 1; if (iy1 > h - 1) iy1 = h - 1;
        for (int y = iy0; y <= iy1; y++) {
            float dy = y + 0.5f - cy;
            int row = y * w;
            for (int x = ix0; x <= ix1; x++) {
                float dx = x + 0.5f - cx;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                float a = Falloff(d, rc, rg);
                if (a > 0f) buf[row + x] += a * NodeAmp;
            }
        }
    }

    /// <summary>芯 + 晕两层：芯给实体感、晕给"发光"感。用多项式而不是 exp，几百万像素下省一半时间。</summary>
    private static float Falloff(float d, float core, float glow) {
        float a = 0f;
        if (d < core) { float t = 1f - d / core; a += t * t * (3f - 2f * t); }
        if (d < glow) { float t = 1f - d / glow; a += t * t * 0.32f; }
        return a;
    }

    /// <summary>正中心一团很淡的辉光，让"中间密"不只是点密，还有一层纵深</summary>
    private static void AddCentreHaze(float[] buf, int w, int h) {
        float cx = w * 0.5f, cy = h * 0.5f, rr = Math.Min(w, h) * 0.62f;
        for (int y = 0; y < h; y++) {
            float dy = (y + 0.5f - cy) / rr;
            int row = y * w;
            for (int x = 0; x < w; x++) {
                float dx = (x + 0.5f - cx) / rr;
                float d2 = dx * dx + dy * dy;
                if (d2 > 1f) continue;
                float t = 1f - d2;
                buf[row + x] += t * t * CoreHubAmp;
            }
        }
    }

    private static void Dim(float[] buf, float k) {
        for (int i = 0; i < buf.Length; i++) buf[i] *= k;
    }

    private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

    /// <summary>累积缓冲 → RGBA32：加色叠加的地方要夹到 1；点线是白色（white=true），暗角是黑色。
    /// alpha 承载强度，所以整层亮度都靠 RawImage 的 color 乘出来。</summary>
    private static byte[] ToRgba(float[] buf, int w, int h, bool white = true) {
        var bytes = new byte[w * h * 4];
        byte rgb = white ? (byte)255 : (byte)0;
        for (int i = 0; i < buf.Length; i++) {
            int a = (int)(Clamp01(buf[i]) * 255f + 0.5f);
            int o = i * 4;
            bytes[o] = rgb; bytes[o + 1] = rgb; bytes[o + 2] = rgb; bytes[o + 3] = (byte)a;
        }
        return bytes;
    }
}
