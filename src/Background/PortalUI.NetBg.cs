using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>代码绘制的背景（取代原来的静态背景图）。四层：
/// TileFar / TileNear 是无缝平铺的点线网，靠 uvRect 匀速平移产生流动感（远快近慢、方向相反，看着有纵深）；
/// Core 是烘死的密集结构，只做很轻的呼吸缩放与漂移，保证"中间密"始终成立；
/// Vig 是暗角，让四周自然变稀。四层每帧合计只写十来个属性，不产生每帧对象。</summary>
public static partial class PortalUI {

    // ---- 平铺层：一个平铺块在屏幕上占多少像素（越小越细碎）----
    private const float NetFarSpan = 1250f;
    private const float NetNearSpan = 780f;
    private const float NetFarAlpha = 0.85f;
    private const float NetNearAlpha = 0.75f;
    private const float NetCoreAlpha = 1f;
    private const float NetCoreOver = 1.10f;      // 中心层比屏幕大出来的倍数（留呼吸与漂移的余量）
    // ---- 流动：屏幕像素每秒 ----
    private const float NetFarSpeed = 7.5f;
    private const float NetNearSpeed = 12.5f;
    private const float NetFarDirX = 0.86f, NetFarDirY = 0.51f;
    private const float NetNearDirX = -0.78f, NetNearDirY = 0.63f;
    // ---- 呼吸：周期都取十几秒，快一点就变成"闪" ----
    private const float NetFarBreathSec = 13f;
    private const float NetNearBreathSec = 8.5f;
    private const float NetCoreBreathSec = 9.5f;
    private const float NetCoreBreathK = 0.018f;
    private const float NetCoreDriftPx = 14f;
    private const float NetCoreDriftSec = 26f;
    // ---- 动态背景接管时整组淡出 ----
    private const float NetFadeSeconds = 0.55f;
    private const float NetFadeMaxStep = 0.08f;
    // ---- 沿线上流动的光点 ----
    // 排班数学（哪个光点、什么时候、走哪条路径、亮度多少）在 NetBgBake 里做成纯函数：
    // 运行时与预览台共用同一份，谁也不会漂。这里只管把它画出来。
    private const float NetPulseSizeMin = 12f;
    private const float NetPulseSizeMax = 20f;
    private const float NetPulseAlpha = 0.95f;
    private const float NetPulseStretch = 3.0f;   // 沿"走向"拉长几倍：不拉长它跟网络上本来就有的亮点分不出来
    private const float NetPulseTintK = 0.72f;    // 往主题色偏多少：纯白的话跟底图上那些静止的点混在一起
    // ---- 单独呼吸的线 ----
    private const float NetBreatheWidth = 3.4f;   // 线的粗细（画布像素），亮度由 NetBgBake.BreatheStateAt 给
    private const float NetBreatheBase = 0.78f;   // 最亮时的整体亮度（再高就跟底图上那些静止的线抢眼）
    // ---- 光点走线的顺滑与节流 ----
    private const float NetDirWindowPx = 14f;     // 走向按"前后各 14px"平均，转弯时不会一下甩头
    // 每帧都在改的东西越少越省：背景这点动效全是"每秒几十像素"级别的，30Hz 更新肉眼看不出来，
    // 但画布重建与属性写入直接减半（uGUI 里任何一个元素变了，整块画布都要重新合批）。
    private const float NetUpdateMinInterval = 1f / 30f;

    private sealed class NetHost {
        public GameObject Root;
        public CanvasGroup Group;
        public RawImage TileFar, TileNear, Core, Vig;
        public Image[] Pulses;
        public RawImage[] Breathe;
        public float Alpha = 1f;
        public bool Hidden;
        // 这组背景挂在哪块画布上（首次用到时缓存）。画布 enabled=false 只是停止渲染、
        // 不改变 activeInHierarchy，所以"在不在渲染"必须问画布本身，不能用层级激活状态代替。
        public Canvas Canvas;
    }

    private static readonly List<NetHost> _netHosts = new List<NetHost>();
    private static Texture2D _netTexCore, _netTexTileFar, _netTexTileNear, _netTexVig;
    private static Texture2D _netTexFlat;    // 拍平版（专给糊图链，见 NetBgSourceTexture）
    private static Sprite _netGlowSprite;
    private static Texture2D _netLineTex;
    private static Vector2[][] _netPaths;    // 光点路径（画布坐标、相对于屏幕中心）
    private static float[] _netPathLen;
    private static float _netClock;
    private static CanvasGroup _panelNetGroup;   // 面板那一组（补挂视频层时拿它当锚）

    /// <summary>开机调一次：烘四张底图。实测核心 2ms + 两层平铺各 1ms + 暗角 &lt;1ms，
    /// 加上传纹理也在十几毫秒量级，同步做完比开线程省事得多。</summary>
    public static void PrewarmNetBg() {
        try { EnsureNetTextures(); } catch (System.Exception e) {
            _netDead = true;
            try { Lg.Ex("背景底图烘焙失败 ->  退回纯色底", e); } catch { }
        }
    }
    private static bool _netDead;

    /// <summary>四张贴图（懒烘；失败过就不再试，返回 false 时各层保持关闭，屏幕上只剩纯色底）</summary>
    private static bool EnsureNetTextures() {
        if (_netTexCore != null && _netTexTileFar != null && _netTexTileNear != null && _netTexVig != null) return true;
        if (_netDead) return false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        byte[] corePx = NetBgBake.BakeCoreTex();
        _netTexCore = MakeNetTexture("NetTexCore", corePx, NetBgBake.CoreW, NetBgBake.CoreH, false);
        _netTexFlat = MakeNetFlatTexture(corePx);
        _netTexTileFar = MakeNetTexture("NetTexTileFar", NetBgBake.BakeTileFarTex(), NetBgBake.TileSize, NetBgBake.TileSize, true);
        _netTexTileNear = MakeNetTexture("NetTexTileNear", NetBgBake.BakeTileNearTex(), NetBgBake.TileSize, NetBgBake.TileSize, true);
        _netTexVig = MakeNetTexture("NetTexVig", NetBgBake.BakeVignette(), NetBgBake.VigSize, NetBgBake.VigSize, false);
        if (_netTexCore == null || _netTexTileFar == null || _netTexTileNear == null || _netTexVig == null) {
            _netDead = true;
            return false;
        }
        Lg.Info($"[LevelLib] 背景底图已烘好（{sw.ElapsedMilliseconds}ms 四层 "
            + $"{NetBgBake.CoreW}x{NetBgBake.CoreH} + {NetBgBake.TileSize}² ×2 + {NetBgBake.VigSize}²）");
        return true;
    }

    /// <summary>平铺层必须 Repeat，uvRect 的偏移才能跨过 1 绕回去；否则平移出界就露出透明边。</summary>
    private static Texture2D MakeNetTexture(string name, byte[] px, int w, int h, bool repeat) {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, name);
        tex.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.LoadRawTextureData(px);
        tex.Apply(false, true);   // 不生成 mipmap，并放掉 CPU 那份拷贝（四张加起来 5.6MB，之后再不读回）
        return tex;
    }

    // ---------- 挂层 ----------

    /// <summary>在某一屏的父节点下挂一组背景（同一个父节点只挂一组）。
    /// 返回那组的 CanvasGroup：动态背景接手时就淡它一个，不用去动四层各自的 alpha。</summary>
    private static CanvasGroup BuildNetBg(Transform parent) {
        if (parent == null) return null;
        PruneNetHosts();
        for (int i = 0; i < _netHosts.Count; i++) {
            if (_netHosts[i].Root != null && _netHosts[i].Root.transform.parent == parent) return _netHosts[i].Group;
        }
        // 这块背景自带一块子画布：它每帧都在改 uvRect / 透明度 / 光点位置，
        // 直接挂在面板画布下会逼整块面板画布每帧重建（那上面有几百个元素）。
        // 子画布不设 overrideSorting，默认按层级顺序画在原位；祖先上的 CanvasGroup
        //（封面淡出 / 视频接管 / 面板淡入）会自己乘进来，不用手工补。
        var root = new GameObject("NetBg", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(parent, false);
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = false;     // 背景永远不吃射线（开始界面那张背景图身上挂着整屏手势）
        group.interactable = false;
        var host = new NetHost { Root = root, Group = group };
        host.TileFar = AddNetLayer(root.transform, "NetTileFar", _netTexTileFar);
        host.TileNear = AddNetLayer(root.transform, "NetTileNear", _netTexTileNear);
        host.Core = AddNetLayer(root.transform, "NetCore", _netTexCore);
        // 单独呼吸的线（底图里没烘它们）与光点：光点容器挂在核心层里面，
        // 核心层那点呼吸缩放与漂移它自动继承，光点与底图上的点天然对齐
        host.Breathe = AddNetBreatheLines(root.transform);
        if (host.Core != null) host.Pulses = AddNetPulses(host.Core.transform);
        host.Vig = AddNetLayer(root.transform, "NetVig", _netTexVig);
        _netHosts.Add(host);
        // 铁律：视频背景可用、或者有静态背景图时，神经图生下来就是藏着的
        //（建出来只为随时可启用 + 让位状态有落点）。静态图那份不加的话，打开界面会先闪一帧神经图再被图盖掉。
        if (VideoBgUsable() || StaticBgAvailable()) { host.Hidden = true; group.alpha = 0f; root.SetActive(false); }
        ApplyNetHost(host, 0f);
        return group;
    }

    /// <summary>一层全屏 RawImage。贴图还没烘好就先关着：没贴图的 RawImage 会画成一块白，绝不能让它露脸。</summary>
    private static RawImage AddNetLayer(Transform parent, string name, Texture2D tex) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var ri = go.AddComponent<RawImage>();
        ri.raycastTarget = false;
        ri.texture = tex;
        ri.color = new Color(1f, 1f, 1f, 0f);
        ri.enabled = tex != null;
        Stretch(ri.rectTransform, Vector2.zero, Vector2.one);
        return ri;
    }

    /// <summary>光点池：一个零尺寸的容器钉在核心层中心，里面若干张小图按 anchoredPosition 摆位置。
    /// 位置由坐标换算而来（纹理像素 → 屏幕中心坐标系），与底图上那些点共用同一套比例。</summary>
    private static Image[] AddNetPulses(Transform core) {
        try {
            if (!EnsureNetPaths()) return null;
            var holder = new GameObject("NetPulses", typeof(RectTransform));
            holder.transform.SetParent(core, false);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = Vector2.zero;
            hrt.anchoredPosition = Vector2.zero;
            var arr = new Image[NetBgBake.PulseCount];
            var spr = NetGlowSprite();
            for (int i = 0; i < NetBgBake.PulseCount; i++) {
                var go = new GameObject("Pulse" + i, typeof(RectTransform));
                go.transform.SetParent(holder.transform, false);
                var img = go.AddComponent<Image>();
                img.sprite = spr;
                img.type = Image.Type.Simple;
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0f);
                img.enabled = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                float size = Mathf.Lerp(NetPulseSizeMin, NetPulseSizeMax, NetBgBake.Hash01(i * 31 + 7));
                rt.sizeDelta = new Vector2(size * NetPulseStretch, size);   // 拉长成一小段"彗星"，走向由每帧的转角决定
                arr[i] = img;
            }
            return arr;
        } catch (System.Exception e) {
            Lg.Ex("建光点池失败（背景照常，只是没有流动的光点）", e);
            return null;
        }
    }

    /// <summary>单独呼吸的那些线：一条线两个图元（中间那个折点也画），位置与角度建的时候算好就永远不动，
    /// 每帧只改亮度。用 RawImage 而不是 Image：一条线只是一个四顶点方片，不用生成九宫格网格
    ///（一百多条线每帧重建网格是实打实的开销），贴图只在宽度方向有渐变、长度方向是常数 ， 两头不会淡掉。</summary>
    private static RawImage[] AddNetBreatheLines(Transform parent) {
        try {
            if (!EnsureNetPaths()) return null;
            int n = _netBreatheLineCount;
            if (n <= 0) return null;
            var tex = NetLineTexture();
            var arr = new RawImage[n * 2];
            for (int i = 0; i < n; i++) {
                var p0 = _netBreathe[i * 3 + 0];
                var pb = _netBreathe[i * 3 + 1];
                var p1 = _netBreathe[i * 3 + 2];
                arr[i * 2 + 0] = MakeBreatheQuad(parent, tex, p0, pb);
                arr[i * 2 + 1] = MakeBreatheQuad(parent, tex, pb, p1);
            }
            return arr;
        } catch (System.Exception e) {
            Lg.Ex("建呼吸线失败（背景照常，只是这些线不呼吸）", e);
            return null;
        }
    }

    private static RawImage MakeBreatheQuad(Transform parent, Texture2D tex, Vector2 a, Vector2 b) {
        var go = new GameObject("NetLine", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<RawImage>();     // 只要一个方片，别用 Image 去生成九宫格网格
        img.texture = tex;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        Vector2 d = b - a;
        float len = d.magnitude;
        rt.sizeDelta = new Vector2(Mathf.Max(len, 1f), NetBreatheWidth);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        return img;
    }

    /// <summary>一条柔和的细线贴图：横着（长度方向）是常数、竖着（宽度方向）从中间往两边淡。
    /// RawImage 把整张贴图拉满整条线 ， 长度方向只是被拉伸（一个色都不变，两头不会淡），
    /// 宽度方向那 32 个像素给出柔和的边。</summary>
    private static Texture2D NetLineTexture() {
        if (_netLineTex != null) return _netLineTex;
        const int W = 4, N = 32;
        var tex = new Texture2D(W, N, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "netline");
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[W * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++) {
            float v = Mathf.Abs((y - c) / c);
            float core = Mathf.Clamp01(1f - v / 0.42f);
            core = core * core * (3f - 2f * core);
            float halo = Mathf.Clamp01(1f - v);
            halo = halo * halo * 0.34f;
            var col = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(core + halo) * 255f));
            for (int x = 0; x < W; x++) px[y * W + x] = col;
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _netLineTex = tex;
        return _netLineTex;
    }

    // 休眠特性：呼吸线的填充器（EnsureNetBreathe）已删 —— 字段保留默认值，
    // 读取处（AddNetBreatheLines 等）因此永远走 n<=0 分支，不画任何呼吸线（见功能逻辑表）
    private static Vector2[] _netBreathe = new Vector2[0];
    private static int _netBreatheLineCount = 0;

    /// <summary>把烘焙器给的路径读成画布坐标（相对于屏幕中心），顺手把折角抹圆再算累计长度。
    /// 换算与底图同一套比例：纹理 (X,Y) → 画布 ((X/W−0.5)×RefW, (Y/H−0.5)×RefH)，所以光点必然压在线上。
    /// 折角抹圆是因为"光点在拐弯处直角转弯，很不和谐"—— 每个折点换成一小段二次贝塞尔，
    /// 密化成七八个点，运行时那条折线就变成一条顺滑的弧（位置与走向都跟着顺）。</summary>
    private static bool EnsureNetPaths() {
        if (_netPaths != null) return _netPaths.Length > 0;
        try {
            var flat = NetBgBake.BakeCorePaths();
            int n = (int)flat[0];
            if (n <= 0) { _netPaths = new Vector2[0][]; _netPathLen = new float[0]; return false; }
            var paths = new Vector2[n][];
            var lens = new float[n];
            var cum = new float[n][];
            var raw = new List<Vector2>();
            int idx = 1;
            for (int i = 0; i < n; i++) {
                int cnt = (int)flat[idx++];
                raw.Clear();
                for (int k = 0; k < cnt; k++) {
                    float X = flat[idx++], Y = flat[idx++];
                    raw.Add(new Vector2((X / NetBgBake.CoreW - 0.5f) * RefW, (Y / NetBgBake.CoreH - 0.5f) * RefH));
                }
                var pts = raw.ToArray();
                paths[i] = pts;
                var acc = new float[pts.Length];
                for (int k = 1; k < pts.Length; k++) acc[k] = acc[k - 1] + Vector2.Distance(pts[k - 1], pts[k]);
                cum[i] = acc;
                lens[i] = acc[pts.Length - 1];
            }
            _netPaths = paths;
            _netPathLen = lens;
            _netPathCum = cum;
            return true;
        } catch (System.Exception e) {
            _netPaths = new Vector2[0][];
            _netPathLen = new float[0];
            Lg.Ex("读光点路径失败（背景照常，只是没有流动的光点）", e);
            return false;
        }
    }
    private static float[][] _netPathCum;

    /// <summary>一个柔和的白色光点：芯 + 外晕，用多项式而不是高斯（每帧要画二十几个）。</summary>
    private static Sprite NetGlowSprite() {
        if (_netGlowSprite != null) return _netGlowSprite;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "netglow");
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++) {
            for (int x = 0; x < N; x++) {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float core = Mathf.Clamp01(1f - r * 1.6f);
                core = core * core * (3f - 2f * core);
                float halo = Mathf.Clamp01(1f - r * 0.92f);
                halo = halo * halo * 0.38f;
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(core + halo) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _netGlowSprite = PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f)), "netglow");
        return _netGlowSprite;
    }

    /// <summary>沿折线走 d 像素之后的落点与"这一刻的走向"。
    /// 走向取"前后各一个窗口"的平均（`NetDirWindowPx`）：只取相邻两点的话，密化后的圆角上
    /// 每过一段走向仍会跳一下，按窗口平均出来才是连续转弯。</summary>
    private static Vector2 NetPathPosAt(int pathIdx, float d, out Vector2 dir) {
        var pts = _netPaths[pathIdx];
        var acc = _netPathCum[pathIdx];
        int n = pts.Length;
        Vector2 here = PosOnPolyline(pts, acc, n, d);
        Vector2 a = PosOnPolyline(pts, acc, n, Mathf.Max(0f, d - NetDirWindowPx));
        Vector2 b = PosOnPolyline(pts, acc, n, Mathf.Min(acc[n - 1], d + NetDirWindowPx));
        dir = b - a;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        return here;
    }

    private static Vector2 PosOnPolyline(Vector2[] pts, float[] acc, int n, float d) {
        if (n < 2) return n == 1 ? pts[0] : Vector2.zero;
        if (d <= 0f) return pts[0];
        if (d >= acc[n - 1]) return pts[n - 1];
        for (int k = 1; k < n; k++) {
            if (acc[k] < d) continue;
            float seg = acc[k] - acc[k - 1];
            float t = seg > 0.0001f ? (d - acc[k - 1]) / seg : 0f;
            return Vector2.Lerp(pts[k - 1], pts[k], t);
        }
        return pts[n - 1];
    }

    /// <summary>光点那一层：位置、透明度全部由全局时钟 t 现算（无状态、无随机），
    /// 所以任何一屏、任何一帧算出来的都是同一个画面 —— 交叉淡入时不会互相打架。</summary>
    private static void ApplyNetPulses(NetHost h, float t) {
        if (h.Pulses == null || _netPaths == null || _netPaths.Length == 0) return;
        int pathCount = _netPaths.Length;
        // 每帧算一次：光点往主题色偏一点，玩家换了主题色它立刻跟着换（也让它与底图上静止的白点分得开）
        Color tint = Color.Lerp(Color.white, new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f), NetPulseTintK);
        for (int i = 0; i < h.Pulses.Length; i++) {
            var img = h.Pulses[i];
            if (img == null) continue;
            int pathIdx;
            float prog, a;
            if (!NetBgBake.PulseStateAt(i, t, pathCount, out pathIdx, out prog, out a)) {
                if (img.enabled) img.enabled = false;
                continue;
            }
            float len = _netPathLen[pathIdx];
            if (len < 1f) { if (img.enabled) img.enabled = false; continue; }
            Vector2 dir;
            img.rectTransform.anchoredPosition = NetPathPosAt(pathIdx, prog * len, out dir);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            img.rectTransform.localEulerAngles = new Vector3(0f, 0f, ang);
            float want = a * NetPulseAlpha;
            var col = img.color;
            if (Mathf.Abs(col.a - want) > 0.01f || Mathf.Abs(col.r - tint.r) > 0.01f)
                img.color = new Color(tint.r, tint.g, tint.b, want);
            bool on = want > 0.02f;
            if (img.enabled != on) img.enabled = on;
        }
    }

    /// <summary>常驻段每帧调一次（排在 TickVideoBg 之后：视频要不要藏我们由它决定，见 NetBgSetShown）。
    /// 一组都没有时第一行就返回，零开销。整块更新按 30Hz 节流：这点动效都是"每秒几十像素"级别的，
    /// 减半的更新频率肉眼看不出来，但画布重建与属性写入直接减半。</summary>
    private static void TickNetBg() {
        try {
            if (_netDead) return;
            if (_netHosts.Count == 0) return;
            float dt = Time.unscaledDeltaTime;
            _netClock += dt;
            _netUpdateAcc += dt;
            if (_netUpdateAcc < NetUpdateMinInterval) return;
            _netUpdateAcc = 0f;
            if (_netTexCore == null && !EnsureNetTextures()) return;
            NetCostWatch.Restart();
            float t = _netClock;
            // 铁律：视频背景可用（开关开 + 有可播文件）、或者有静态背景图时，神经图绝对不显示 ——
            // 任何"放开"（暂停/停用时把 Hidden 放回 false、层不活跃时的让位豁免）都不许把它亮回来。
            // 放宽会让两套背景叠着闪；有静态图而没视频的那些屏，背景交给静态图。
            bool netOff = VideoBgUsable() || StaticBgAvailable();
            for (int i = _netHosts.Count - 1; i >= 0; i--) {
                var h = _netHosts[i];
                if (h == null || h.Root == null) { _netHosts.RemoveAt(i); continue; }
                float target = (h.Hidden || netOff) ? 0f : 1f;
                // 这一屏现在根本没在渲染（画布被关、或整组被收起）就当场把 alpha 落到目标值，
                // 不走渐变。不落定的话，状态会在"看不见"的那段时间里把 alpha 慢慢爬满，
                // 等画布一开、判据翻回来，它又从 1 慢慢淡出 —— 玩家看到的就是"开面板闪一下神经图"
                // （那会儿视频通常还没 Prepare 完，视频层透明，神经图看得格外清楚）。
                if (h.Canvas == null && h.Group != null) h.Canvas = h.Group.GetComponentInParent<Canvas>();
                bool rendering = h.Root.activeInHierarchy && h.Canvas != null && h.Canvas.enabled;
                if (!rendering) {
                    if (h.Alpha != target) h.Alpha = target;
                    if (h.Group != null && Mathf.Abs(h.Group.alpha - target) > 0.0005f) h.Group.alpha = target;
                    if (h.Root.activeSelf != (target > 0.002f)) h.Root.SetActive(target > 0.002f);
                    continue;
                }
                if (Mathf.Abs(h.Alpha - target) > 0.0005f) {
                    float step = Mathf.Min(NetFadeMaxStep, dt / NetFadeSeconds);
                    h.Alpha = Mathf.MoveTowards(h.Alpha, target, step);
                } else {
                    h.Alpha = target;
                }
                bool on = h.Alpha > 0.002f;
                if (h.Root.activeSelf != on) h.Root.SetActive(on);
                if (h.Group != null && Mathf.Abs(h.Group.alpha - h.Alpha) > 0.0005f) h.Group.alpha = h.Alpha;
                if (!on) continue;                       // 淡干净了就不必再算四层的位置
                if (h.Root != null && !h.Root.activeSelf) continue;   // 整组被关掉（开始界面收起来了）
                ApplyNetHost(h, t);
            }
            ReportNetCost(NetCostWatch.Elapsed.TotalMilliseconds);
        } catch (System.Exception e) {
            _netDead = true;
            try { Lg.Ex("背景动画失败 ->  已关掉（屏幕留纯色底）", e); } catch { }
            DisableNetHosts();
        }
    }

    private static float _netUpdateAcc;
    private static readonly System.Diagnostics.Stopwatch NetCostWatch = new System.Diagnostics.Stopwatch();
    private static double _netCostSum;
    private static int _netCostN;

    /// <summary>自报开销：平时一个字都不打，只有"平均一次更新超过 1.2ms"才每 600 次报一行。
    /// 这样"背景是不是卡的原因"下次翻日志就有数，而不是靠猜。</summary>
    private static void ReportNetCost(double ms) {
        if (ms < 0.0) return;
        _netCostSum += ms;
        _netCostN++;
        if (_netCostN < 600) return;
        double avg = _netCostSum / _netCostN;
        if (avg >= 1.2) {
            int hosts = 0, quads = 0;
            for (int i = 0; i < _netHosts.Count; i++) {
                var h = _netHosts[i];
                if (h == null || h.Root == null || !h.Root.activeSelf) continue;
                hosts++;
                quads += (h.Breathe != null ? h.Breathe.Length : 0) + (h.Pulses != null ? h.Pulses.Length : 0) + 4;
            }
            try {
                Lg.Info($"[LevelLib] 背景开销：每帧更新平均 {avg:F2}ms（30Hz 节流后）"
                    + $" ｜ 在画的 {hosts} 屏 / 图元 {quads} 个");
            } catch { }
        }
        _netCostSum = 0; _netCostN = 0;
    }

    /// <summary>把某一刻的形状写到四层上。t 是全局时钟：同一帧所有屏（开始界面 / 登录界面 / 面板）
    /// 拿到的相位完全一致，交叉淡入时才不会出现"两张背景各动各的"。</summary>
    private static void ApplyNetHost(NetHost h, float t) {
        if (h.TileFar != null && _netTexTileFar != null) {
            if (!h.TileFar.enabled) h.TileFar.enabled = true;
            float u = Mathf.Repeat(t * NetFarSpeed * NetFarDirX / NetFarSpan, 1f);
            float v = Mathf.Repeat(t * NetFarSpeed * NetFarDirY / NetFarSpan, 1f);
            h.TileFar.uvRect = new Rect(u, v, RefW / NetFarSpan, RefH / NetFarSpan);
            SetLayerAlpha(h.TileFar, NetFarAlpha * (0.82f + 0.18f * Mathf.Sin(t * Mathf.PI * 2f / NetFarBreathSec)));
        }
        if (h.TileNear != null && _netTexTileNear != null) {
            if (!h.TileNear.enabled) h.TileNear.enabled = true;
            float u = Mathf.Repeat(t * NetNearSpeed * NetNearDirX / NetNearSpan, 1f);
            float v = Mathf.Repeat(t * NetNearSpeed * NetNearDirY / NetNearSpan, 1f);
            h.TileNear.uvRect = new Rect(u, v, RefW / NetNearSpan, RefH / NetNearSpan);
            SetLayerAlpha(h.TileNear, NetNearAlpha * (0.80f + 0.20f * Mathf.Sin(t * Mathf.PI * 2f / NetNearBreathSec + 1.7f)));
        }
        if (h.Core != null && _netTexCore != null) {
            if (!h.Core.enabled) h.Core.enabled = true;
            float breathe = Mathf.Sin(t * Mathf.PI * 2f / NetCoreBreathSec);
            float k = NetCoreOver * (1f + NetCoreBreathK * breathe);
            h.Core.rectTransform.localScale = new Vector3(k, k, 1f);
            float dx = NetCoreDriftPx * Mathf.Sin(t * Mathf.PI * 2f / NetCoreDriftSec);
            float dy = NetCoreDriftPx * 0.7f * Mathf.Cos(t * Mathf.PI * 2f / (NetCoreDriftSec * 0.83f));
            h.Core.rectTransform.anchoredPosition = new Vector2(dx, dy);
            SetLayerAlpha(h.Core, NetCoreAlpha * (0.88f + 0.12f * Mathf.Sin(t * Mathf.PI * 2f / NetCoreBreathSec + 0.6f)));
        }
        if (h.Vig != null && _netTexVig != null) {
            if (!h.Vig.enabled) h.Vig.enabled = true;
            SetLayerAlpha(h.Vig, 1f);
        }
        ApplyNetBreathe(h, t);
        ApplyNetPulses(h, t);
    }

    private static void SetLayerAlpha(RawImage ri, float a) {
        var c = ri.color;
        if (Mathf.Abs(c.a - a) > 0.002f) ri.color = new Color(1f, 1f, 1f, a);
    }

    /// <summary>每条线各自呼吸明暗（相位与周期都不同）。位置与角度是建的时候算好的，这里只写亮度，
    /// 而且变了才写 —— 一条线的亮度每帧只动百分之几，实际两三个帧才写一次。</summary>
    private static void ApplyNetBreathe(NetHost h, float t) {
        if (h.Breathe == null || h.Breathe.Length == 0) return;
        for (int i = 0; i < _netBreatheLineCount; i++) {
            float a;
            NetBgBake.BreatheStateAt(i, t, out a);
            float want = a * NetBreatheBase;
            for (int k = 0; k < 2; k++) {
                var img = h.Breathe[i * 2 + k];
                if (img == null) continue;
                if (Mathf.Abs(img.color.a - want) > 0.01f) img.color = new Color(1f, 1f, 1f, want);
                if (!img.enabled) img.enabled = true;
            }
        }
    }

    /// <summary>整组开关（开始界面收起来时用）：收起来的那层还在场上但不显示，
    /// 不关掉的话它那几百个图元每帧照样参与渲染与刷新。</summary>
    private static void NetBgSetActive(CanvasGroup group, bool on) {
        if (group == null) return;
        for (int i = 0; i < _netHosts.Count; i++) {
            var h = _netHosts[i];
            if (h == null || h.Group != group) continue;
            if (h.Root != null && h.Root.activeSelf != on) h.Root.SetActive(on);
            return;
        }
    }

    /// <summary>动态背景（视频）接手 / 放手时调。immediate = 挂上去那一刻视频就已经就绪：
    /// 那要当场消失，一帧都不许让两层同时亮着（会闪）。其余情况走淡出。
    /// 铁律：视频背景可用、或者有静态背景图时，"放回来"一律无效 —— 神经图绝对不显示。</summary>
    private static void NetBgSetShown(CanvasGroup group, bool shown, bool immediate) {
        if (group == null) return;
        if (VideoBgUsable() || StaticBgAvailable()) shown = false;
        for (int i = 0; i < _netHosts.Count; i++) {
            var h = _netHosts[i];
            if (h == null || h.Group != group) continue;
            h.Hidden = !shown;
            if (immediate) {
                h.Alpha = shown ? 1f : 0f;
                if (h.Group != null) h.Group.alpha = h.Alpha;
                if (h.Root != null) h.Root.SetActive(shown);
            }
            return;
        }
    }

    /// <summary>视频那几层被销毁、或者静态图掉了的时候把背景放回来（清干净，不留一块黑）。
    /// 铁律：视频背景可用、或者有静态背景图时不放回来 —— 神经图绝对不显示，兜底交给各屏自己的背景层。</summary>
    private static void NetBgShowAll(bool immediate) {
        bool off = VideoBgUsable() || StaticBgAvailable();
        for (int i = 0; i < _netHosts.Count; i++) {
            var h = _netHosts[i];
            if (h == null) continue;
            h.Hidden = off;
            if (immediate) {
                h.Alpha = off ? 0f : 1f;
                if (h.Group != null) h.Group.alpha = h.Alpha;
                if (h.Root != null) h.Root.SetActive(!off);
            }
        }
    }

    private static void PruneNetHosts() {
        for (int i = _netHosts.Count - 1; i >= 0; i--) {
            if (_netHosts[i] == null || _netHosts[i].Root == null) _netHosts.RemoveAt(i);
        }
    }

    private static void DisableNetHosts() {
        for (int i = 0; i < _netHosts.Count; i++) {
            var h = _netHosts[i];
            if (h == null || h.Root == null) continue;
            h.Root.SetActive(false);
        }
    }

    /// <summary>开面板时报一次背景的家底。贴图与大厅拓扑图都是跨面板复用的静态缓存，正常情况下每一项都该说"命中"；
    /// 哪一项显示"待烘"就说明那一次开面板会重新烘焙，而重烘正是玩家会看到的"闪一下"。
    /// 这行是给"还是会闪"那类反馈留的证据（自己翻日志定性，不用玩家描述）。</summary>
    public static void LogNetBgCacheState() {
        try {
            string core = _netTexCore != null ? (_netTexCore.width + "×" + _netTexCore.height) : "无";
            Lg.Info("背景缓存：底图 "
                + (_netTexCore != null && _netTexTileFar != null && _netTexVig != null ? "命中" : "待烘")
                + " ｜ 模糊链 " + _bgBlurDepth + " 档 ｜ 视频层 " + (_bgVideo != null ? "已挂" : "无"));
        } catch { }
    }

    /// <summary>背景模糊与面板磨砂要糊的那张源。动态背景就绪时它们走视频纹理（见 BgBlurSource），
    /// 否则走这张"拍平版"代码背景 —— 它不是屏幕上那四层里任何一张，而是把中心线稿压在整屏底色上
    /// 重新算出来的不透明版（见 MakeNetFlatTexture），糊出来的底最像"我身后的东西"。</summary>
    private static Texture NetBgSourceTexture() {
        return _netTexFlat != null ? (Texture)_netTexFlat : (Texture)_netTexCore;
    }

    /// <summary>把中心那张线稿拍平成"不透明的整块背景"。线稿本身 RGB 恒 255、形状全在 alpha 上
    /// （平均只有 11/255，盖住屏幕不到一成），直接拿去糊的话糊图也是一张几乎透明的图 ——
    /// 磨砂层盖在清晰的背景上等于没盖，看上去"哪儿都没有磨砂"。
    /// 拍平之后糊图是不透明的一整块，磨砂才是"身后那层被糊掉了"的真磨砂。
    /// 两张平铺与暗角合起来只盖住屏幕不到一成，不参与拍平，省一半时间。</summary>
    private static Texture2D MakeNetFlatTexture(byte[] px) {
        try {
            if (px == null) return null;
            int w = NetBgBake.CoreW, h = NetBgBake.CoreH;
            if (px.Length < w * h * 4) return null;
            var flat = new byte[w * h * 4];
            int br = Mathf.Clamp(Mathf.RoundToInt(BgColor.r * 255f), 0, 255);
            int bg = Mathf.Clamp(Mathf.RoundToInt(BgColor.g * 255f), 0, 255);
            int bb = Mathf.Clamp(Mathf.RoundToInt(BgColor.b * 255f), 0, 255);
            for (int i = 0; i + 3 < flat.Length; i += 4) {
                int a = px[i + 3];                            // 线是白的，a 越大越接近白
                flat[i] = (byte)(br + (255 - br) * a / 255);
                flat[i + 1] = (byte)(bg + (255 - bg) * a / 255);
                flat[i + 2] = (byte)(bb + (255 - bb) * a / 255);
                flat[i + 3] = 255;                            // 拍平这一步就是"不许再有透明"
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            PerfDiag.Tag(tex, "NetTexFlat");
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.LoadRawTextureData(flat);
            tex.Apply(false, true);
            return tex;
        } catch (System.Exception e) {
            try { Lg.Ex("代码背景拍平失败，磨砂退回原来的取法", e); } catch { }
            return null;
        }
    }

    // ================= 「闪线」逐帧探针（默认关：mod 目录放 debug_netflash.txt） =================
    // 「打开界面闪一下代码背景」连着报了四轮，而现网日志是按事件打的、没有逐帧行
    //（`【首帧性能】` 只在 ≥12ms 的帧打、`【卡顿】` 只在卡顿帧打）—— 谁也说不清"那一帧屏幕上到底谁在画"。
    // 这个探针就是为它准备的：开面板之后逐帧记 120 行，把四块背景（纯色底 / 面板代码背景 / 视频层 /
    // 开始界面那一套）的显隐与透明度、面板进场闸的状态、模糊链的源一起摆出来，
    // 改前改后两段日志一对照就能看清是哪一帧闪的、以及新口径有没有把那一帧按掉。
    // 面板那块整屏底的 alpha 也记进来（`面板底=`）：它必须 1.000，0.96 时没有视频层盖着，透出来的就是游戏画面。
    // 只在开关文件存在时工作（启动后第一次读就缓存，删掉文件要重启游戏 —— 跟 diag.txt 一个待遇），
    // 默认关、打完自动停；发布包排除名单里有 debug_netflash.txt（见 update_package.ps1）。
    private const int NetFlashProbeFrames = 120;
    private static int _nfLeft;
    private static int _nfOn = -1;      // -1 = 还没读过开关

    private static bool NetFlashProbeOn() {
        if (_nfOn < 0) {
            _nfOn = 0;
            try {
                if (System.IO.File.Exists(System.IO.Path.Combine(Main.ModEntry.Path, "debug_netflash.txt"))) _nfOn = 1;
            } catch { }
        }
        return _nfOn == 1;
    }

    /// <summary>开面板建完背景时武装：从这一帧起逐帧记 NetFlashProbeFrames 帧（之后彻底安静）</summary>
    private static void NetFlashProbeArm() {
        if (!NetFlashProbeOn()) return;
        _nfLeft = NetFlashProbeFrames;
        try {
            Lg.Info("【闪线】探针已武装：开面板后逐帧记 " + NetFlashProbeFrames
                + " 帧（debug_netflash.txt 每帧一行）");
        } catch { }
    }

    /// <summary>某一组代码背景"现在长什么样"：root 开没开 / 整组 alpha / 图元数（图元数用来对"谁在画"）</summary>
    private static string NfHostText(CanvasGroup group) {
        if (group == null) return "未挂";
        for (int i = 0; i < _netHosts.Count; i++) {
            var h = _netHosts[i];
            if (h == null || h.Group != group) continue;
            if (h.Root == null) return "无";
            int quads = 4;
            if (h.Breathe != null) quads += h.Breathe.Length;
            if (h.Pulses != null) quads += h.Pulses.Length;
            return (h.Root.activeSelf ? "on" : "off") + "/" + h.Alpha.ToString("F3") + "/" + quads;
        }
        return "无";
    }

    /// <summary>每帧一处（TickCore 末尾，面板那一段跑完之后）：只在武装之后记，记满自动停</summary>
    private static void NetFlashProbeTick() {
        if (_nfLeft <= 0) return;
        if (!NetFlashProbeOn()) { _nfLeft = 0; return; }
        _nfLeft--;
        try {
            int step = NetFlashProbeFrames - _nfLeft;
            float panelA = (_panelGroup != null) ? _panelGroup.alpha : -1f;
            // 面板那块整屏底的 alpha：它必须 1.000 —— 0.96 时没有视频层盖着，透出来的就是游戏画面
            float plateA = (_panelBgImg != null) ? _panelBgImg.color.a : -1f;
            bool readyOld = VideoReadyToShow();
            float vidA = -1f;
            for (int i = 0; i < _bgVideoLayers.Count; i++) {
                var ri = _bgVideoLayers[i];
                if (ri == null) continue;
                float a = ri.color.a;
                if (a > vidA) vidA = a;
            }
            string src = (_bgBlurSrc == null) ? "无"
                : ((_bgVideo != null && ReferenceEquals(_bgBlurSrc, _bgVideo.texture)) ? "视频" : "代码底");
            string ssLayer = (_ssGo == null) ? "-" : ((_ssParked ? "parked" : "on") + "/"
                + ((_ssGroup != null) ? _ssGroup.alpha.ToString("F2") : "-"));
            Lg.Info("【闪线】帧+" + step
                + " 面板组=" + panelA.ToString("F3")
                + " 面板底=" + plateA.ToString("F3")
                + " 画布=" + ((_panelCanvas != null && _panelCanvas.enabled) ? "on" : "off")
                + " 停渲=" + (_panelRenderStopped ? "T" : "F")
                + " 面板代码背景=" + NfHostText(_panelNetGroup)
                + " 视频层=" + vidA.ToString("F3")
                + " 就绪=" + (readyOld ? "T" : "F")
                + " 有帧=" + (VideoHasFrame() ? "T" : "F")
                + " 暂停过=" + (_videoPaused ? "T" : "F")
                + " 让位等=" + _videoHandoverT.ToString("F3")
                + " 进场闸=" + (VideoHandoverPending() ? "挡" : "放")
                + " 模糊=" + _bgBlurDepth + "档/" + src
                + " 磨砂=" + _glassPanels.Count
                + " 开始层=" + ssLayer
                + " 封面=" + ((_ssCoverGroup != null) ? _ssCoverGroup.alpha.ToString("F2") : "-")
                + " 大厅=" + ((_mainGroup != null) ? _mainGroup.alpha.ToString("F2") : "-")
                + " ｜ 旧口径=" + (readyOld ? "视频层亮/代码背景淡出" : "代码背景亮着（就是这一帧闪的）")
                + (plateA >= 0f && plateA < 0.9995f ? " ｜ 整屏底 " + plateA.ToString("F3") + "（透游戏，必须 1.000）" : ""));
        } catch { }
        if (_nfLeft == 0) {
            try { Lg.Info("【闪线】探针打完（" + NetFlashProbeFrames + " 帧 之后彻底安静）"); } catch { }
        }
    }
}
