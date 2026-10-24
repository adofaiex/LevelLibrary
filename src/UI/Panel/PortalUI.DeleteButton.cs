using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>通关记录条尾的删除键（照 shadcn 生态 rare-ui 的 delete-button 移植）。
/// 源组件是「触发键 + 就地确认面板」：点垃圾桶则容器变宽、桶盖绕铰链转开、桶壁上沿下移、
/// 确认面板与两个圆键错峰进场；点对勾 = 删、点叉或再点一次垃圾桶 = 取消（桶身抖一下）。
/// 尺寸、时长、缓动、错峰、弹簧全是常量：改大小只动 RecDelH，改手感只动时长那一组。
/// 源组件在网页里从触发键往右展开，这里按键在行尾，所以整体镜像成往左展开，
/// 面板与指示三角换到另一侧，时间与曲线一个没改。逐帧只动 RectTransform / 颜色 / CanvasGroup。</summary>
public static partial class PortalUI {

    // ---------- 尺寸（源组件高 48，整套数字都按它的比例推出来）----------
    /// <summary>触发键边长 = 一行通关记录的高度（源组件在 48 高的一行里就是占满的）。</summary>
    private const float RecDelH = 40f;
    /// <summary>展开出来的确认面板宽（源 PANEL = 84 = 1.75 x 48）。</summary>
    private const float RecDelPanelW = RecDelH * 84f / 48f;
    /// <summary>圆角（源 rounded-2xl = 16 = 48 的三分之一）。</summary>
    private const float RecDelRadius = RecDelH * 16f / 48f;
    /// <summary>垃圾桶图标边长（源 svg 20 x 20，viewBox 24）。</summary>
    private const float RecDelIconSize = RecDelH * 20f / 48f;
    /// <summary>面板里两个圆键的直径（源 h-7 w-7 = 28）。</summary>
    private const float RecDelCircleD = RecDelH * 28f / 48f;
    /// <summary>圆键里的图标边长（源 svg 14 x 14，同一个 viewBox 24）。</summary>
    private const float RecDelCircleIcon = RecDelH * 14f / 48f;
    /// <summary>两个圆键之间（源 gap-2 = 8）。</summary>
    private const float RecDelGap = RecDelH * 8f / 48f;
    /// <summary>面板进场时相对终点偏出去的距离（源 x: -6；镜像之后是正的）。</summary>
    private const float RecDelSlide = RecDelH * 6f / 48f;
    /// <summary>面板边上那个指示三角（源 w-1.5 / h-2.5 = 6 x 10）。</summary>
    private const float RecDelCaretW = RecDelH * 6f / 48f, RecDelCaretH = RecDelH * 10f / 48f;
    /// <summary>按键右缘与行右缘的间距。</summary>
    private const float RecDelMargin = 12f;
    /// <summary>行尾给删除键让出来的横向比例 = (展开 132 + 间距 12 + 空隙 4) / 详情区记录条宽 721。
    /// X 准确度那一栏的右边界按它退，展开态才不会压到百分数上。</summary>
    private const float RecDelReserveK = 0.175f;

    // ---------- 时长与缓动（源组件的 transition 表）----------
    private const float RecDelWidthSec = 0.62f;     // 容器宽度 48 → 132
    private const float RecDelLidSec = 0.60f;       // 桶盖绕铰链转开 35 度
    private const float RecDelWallSec = 0.56f;      // 桶壁上沿 6 → 13.5
    private const float RecDelInSec = 0.44f;        // 面板进场
    private const float RecDelInDelay = 0.14f;      // 面板比宽度晚一拍
    private const float RecDelStagger = 0.07f;      // 两个圆键之间的错峰
    private const float RecDelOutSec = 0.30f;       // 面板退场（比进场快，无延迟）
    private const float RecDelTapSec = 0.20f;       // 触发键按下的缩放
    private const float RecDelSwapSec = 0.22f;      // 图标互换（垃圾桶和对勾）
    private const float RecDelSettleSec = 0.45f;    // 取消时桶身抖一下 / 对勾把路径描出来
    private const float RecDelColorSec = 0.20f;     // 圆键底色的悬停过渡（CSS transition-colors）
    private const float RecDelHoldDeleted = 1.40f;  // 删掉之后对勾停留多久
    private const float RecDelHoldKept = 0.60f;     // 取消之后停留多久
    /// <summary>单帧步进上限：抖一下几百毫秒的帧不许把整段动画一步冲完，那又成了闪现
    /// （与 UiFadeMaxStep / SwitchMoveMaxStep 同一个道理）。</summary>
    private const float RecDelMaxStep = 0.05f;

    /// <summary>源组件的主缓动 EASE = cubic-bezier(0.32, 0.72, 0, 1)。</summary>
    private const float RecDelEaseX1 = 0.32f, RecDelEaseY1 = 0.72f, RecDelEaseX2 = 0f, RecDelEaseY2 = 1f;
    /// <summary>盖子的 EASE_LID = cubic-bezier(0.34, 1.1, 0.64, 1)：y1 大于 1，转过去会过冲一下再回来。</summary>
    private const float RecDelLidX1 = 0.34f, RecDelLidY1 = 1.1f, RecDelLidX2 = 0.64f, RecDelLidY2 = 1f;
    /// <summary>CSS 的 transition-colors 默认缓动 ease。</summary>
    private const float RecDelColX1 = 0.25f, RecDelColY1 = 0.1f, RecDelColX2 = 0.25f, RecDelColY2 = 1f;

    /// <summary>圆键的弹簧（源 PRESS：stiffness 520 / damping 18 / mass 0.5）。</summary>
    private const float RecDelSpringK = 520f, RecDelSpringC = 18f, RecDelSpringM = 0.5f;
    /// <summary>圆键悬停 / 按下的缩放（源 whileHover 1.03 / whileTap 0.84）。</summary>
    private const float RecDelCircleHover = 1.03f, RecDelCirclePress = 0.84f;
    /// <summary>触发键按下的缩放（源 whileTap 0.94，走 0.2 秒补间而不是弹簧）。</summary>
    private const float RecDelTriggerPress = 0.94f;

    // ---------- 配色（源组件 dark: 那一档，十六进制照抄）----------
    private static readonly Color RecDelSurface = new Color32(0x26, 0x26, 0x26, 0xFF);   // dark:bg-[#262626]
    private static readonly Color RecDelRecess = new Color32(0x1B, 0x1B, 0x1B, 0xFF);    // dark:bg-[#1B1B1B]
    private static readonly Color RecDelGlyph = new Color32(0x9B, 0x9A, 0xA7, 0xFF);     // dark:text-[#9B9AA7]
    private static readonly Color RecDelHoverBg = new Color32(0x2C, 0x2C, 0x2C, 0xFF);   // dark:hover:bg-[#2C2C2C]
    private static readonly Color RecDelAccent = new Color32(0xFF, 0x5F, 0x2E, 0xFF);    // ACCENT #FF5F2E

    // ---------- 图标几何（源 SVG 的 viewBox 24，y 朝下）----------
    /// <summary>桶壁上沿：常态 6、展开后 13.5（源的 WALL_TOP / WALL_TOP_OPEN）。</summary>
    private const float RecDelVbWallTop = 6f, RecDelVbWallTopOpen = 13.5f;
    /// <summary>桶底两条竖壁的转角起始高度（源的 WALL_BASE = 20）。</summary>
    private const float RecDelVbBase = 20f;
    /// <summary>桶身左右壁的中心线。</summary>
    private const float RecDelVbWallL = 5f, RecDelVbWallR = 19f;
    /// <summary>桶底横线与转角半径。</summary>
    private const float RecDelVbFoot = 22f, RecDelVbCornerR = 2f;
    /// <summary>盖子横杆 M3 6 h18；提手 x 8~16、顶 2、转角半径 2。</summary>
    private const float RecDelVbLidY = 6f, RecDelVbLidX0 = 3f, RecDelVbLidX1 = 21f;
    private const float RecDelVbHandleX0 = 8f, RecDelVbHandleX1 = 16f, RecDelVbHandleTop = 2f, RecDelVbHandleR = 2f;
    /// <summary>盖子转开的角度：源是 rotate(-35)（y 朝下），Unity 的 y 朝上，所以取正、转出来是同一个方向。</summary>
    private const float RecDelLidOpenDeg = 35f;
    /// <summary>三条描边宽度（源 strokeWidth 2 / 2.5 / 3.5）。</summary>
    private const float RecDelVbBinStroke = 2f, RecDelVbCheckStroke = 2.5f, RecDelVbCircleStroke = 3.5f;
    /// <summary>对勾折线 M4 12.5 9.5 18 20 7：两段的长度（源 pathLength 就是这么分的）。</summary>
    private const float RecDelVbCheckTotal = 22.985f, RecDelVbCheckFirst = 7.778f;
    private const float RecDelCheckSplit = RecDelVbCheckFirst / RecDelVbCheckTotal;
    /// <summary>对勾两段的固定角度（Unity 的 y 朝上）。</summary>
    private const float RecDelCheckDegA = -45f, RecDelCheckDegB = 46.33f;
    /// <summary>贴图按 4 倍烘、再用双线性缩回 1:1（小图标靠重采样抗锯齿，同 SwitchKnobSprite 那条）。</summary>
    private const int RecDelSS = 4;

    // ---------- 贴图（一次性烘好，逐帧只动 RectTransform）----------
    private static Sprite _recDelBody, _recDelLid, _recDelCapsule, _recDelCross, _recDelRound, _recDelCircle, _recDelCaret;

    /// <summary>描边图元：线段，或圆弧（圆心 + 半径 + 起止角，角度按 viewBox 的 y 朝下算）。</summary>
    private struct RecDelSeg {
        public float X1, Y1, X2, Y2, R, A0, A1;
        public bool Arc;
        public static RecDelSeg Line(float x1, float y1, float x2, float y2) {
            return new RecDelSeg { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Arc = false };
        }
        public static RecDelSeg ArcAt(float cx, float cy, float r, float a0, float a1) {
            return new RecDelSeg { X1 = cx, Y1 = cy, R = r, A0 = a0, A1 = a1, Arc = true };
        }
    }

    /// <summary>点到线段的距离。</summary>
    private static float RecDelLineDist(float x1, float y1, float x2, float y2, float px, float py) {
        float dx = x2 - x1, dy = y2 - y1;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0f ? Mathf.Clamp01(((px - x1) * dx + (py - y1) * dy) / len2) : 0f;
        float qx = x1 + dx * t - px, qy = y1 + dy * t - py;
        return Mathf.Sqrt(qx * qx + qy * qy);
    }

    /// <summary>点到某段图元的距离（圆弧落在角度范围外时退回两端点）。</summary>
    private static float RecDelSegDist(RecDelSeg s, float px, float py) {
        if (!s.Arc) return RecDelLineDist(s.X1, s.Y1, s.X2, s.Y2, px, py);
        float ax = px - s.X1, ay = py - s.Y1;
        float d = Mathf.Sqrt(ax * ax + ay * ay);
        float ang = Mathf.Atan2(ay, ax) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;
        if (ang >= s.A0 && ang <= s.A1) return Mathf.Abs(d - s.R);
        float e0x = s.X1 + s.R * Mathf.Cos(s.A0 * Mathf.Deg2Rad), e0y = s.Y1 + s.R * Mathf.Sin(s.A0 * Mathf.Deg2Rad);
        float e1x = s.X1 + s.R * Mathf.Cos(s.A1 * Mathf.Deg2Rad), e1y = s.Y1 + s.R * Mathf.Sin(s.A1 * Mathf.Deg2Rad);
        return Mathf.Min(RecDelLineDist(e0x, e0y, e0x, e0y, px, py), RecDelLineDist(e1x, e1y, e1x, e1y, px, py));
    }

    /// <summary>把一组「线段 / 圆弧」按线宽描成贴图（按覆盖率抗锯齿，再按 RecDelSS 倍烘）。
    /// scale 把 viewBox 单位换算成画布像素，于是贴图的原生尺寸就等于 1:1 的显示尺寸，
    /// 九宫格的边距也不会跟着缩放跑偏。</summary>
    private static Sprite RecDelBake(string tag, float scale, float vbX, float vbY, float vbW, float vbH,
                                     float stroke, RecDelSeg[] segs, Vector4 borderVb) {
        int w = Mathf.Max(4, Mathf.RoundToInt(vbW * scale * RecDelSS));
        int h = Mathf.Max(4, Mathf.RoundToInt(vbH * scale * RecDelSS));
        float per = scale * RecDelSS;                 // 一个贴图像素对应多少 viewBox 单位
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, tag);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[w * h];
        float half = stroke * 0.5f;
        for (int y = 0; y < h; y++) {
            float vy = vbY + (vbH - (y + 0.5f) / per);   // 贴图自下而上：y = 0 是 viewBox 的下边
            for (int x = 0; x < w; x++) {
                float vx = vbX + (x + 0.5f) / per;
                float d = float.MaxValue;
                for (int i = 0; i < segs.Length; i++) {
                    float dd = RecDelSegDist(segs[i], vx, vy);
                    if (dd < d) d = dd;
                }
                float a = Mathf.Clamp01((half - d) * per + 0.5f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        var border = new Vector4(borderVb.x * scale * RecDelSS, borderVb.y * scale * RecDelSS,
                                 borderVb.z * scale * RecDelSS, borderVb.w * scale * RecDelSS);
        return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
            100f * RecDelSS, 0, SpriteMeshType.FullRect, border), tag);
    }

    /// <summary>桶身：两条侧壁 + 桶底（含两个转角圆弧）。上沿不封口，所以上边距是 0：
    /// 九宫格纵向拉伸时桶底与转角保持原样，只有两条竖壁那一段在伸缩，
    /// 正好等于源组件里桶壁上沿从 6 落到 13.5 的那段动画。</summary>
    private static Sprite RecDelBodySprite {
        get {
            if (_recDelBody == null) {
                float k = RecDelIconSize / 24f;
                var segs = new RecDelSeg[] {
                    RecDelSeg.Line(RecDelVbWallR, RecDelVbWallTop, RecDelVbWallR, RecDelVbBase),
                    RecDelSeg.ArcAt(RecDelVbWallR - RecDelVbCornerR, RecDelVbBase, RecDelVbCornerR, 0f, 90f),
                    RecDelSeg.Line(RecDelVbWallR - RecDelVbCornerR, RecDelVbFoot, RecDelVbWallL + RecDelVbCornerR, RecDelVbFoot),
                    RecDelSeg.ArcAt(RecDelVbWallL + RecDelVbCornerR, RecDelVbBase, RecDelVbCornerR, 90f, 180f),
                    RecDelSeg.Line(RecDelVbWallL, RecDelVbBase, RecDelVbWallL, RecDelVbWallTop),
                };
                _recDelBody = RecDelBake("recdelbody", k, 4f, RecDelVbWallTop, 16f, RecDelVbFoot + 1f - RecDelVbWallTop,
                    RecDelVbBinStroke, segs, new Vector4(4f, 3f, 4f, 0f));
            }
            return _recDelBody;
        }
    }

    /// <summary>盖子：横杆 + 提手，整块绕铰链 (3, 6) 转。</summary>
    private static Sprite RecDelLidSprite {
        get {
            if (_recDelLid == null) {
                float k = RecDelIconSize / 24f;
                float hy = RecDelVbLidY - RecDelVbHandleR;
                var segs = new RecDelSeg[] {
                    RecDelSeg.Line(RecDelVbLidX0, RecDelVbLidY, RecDelVbLidX1, RecDelVbLidY),
                    RecDelSeg.Line(RecDelVbHandleX0, RecDelVbLidY, RecDelVbHandleX0, hy),
                    RecDelSeg.ArcAt(RecDelVbHandleX0 + RecDelVbHandleR, hy, RecDelVbHandleR, 180f, 270f),
                    RecDelSeg.Line(RecDelVbHandleX0 + RecDelVbHandleR, RecDelVbHandleTop, RecDelVbHandleX1 - RecDelVbHandleR, RecDelVbHandleTop),
                    RecDelSeg.ArcAt(RecDelVbHandleX1 - RecDelVbHandleR, hy, RecDelVbHandleR, 270f, 360f),
                    RecDelSeg.Line(RecDelVbHandleX1, hy, RecDelVbHandleX1, RecDelVbLidY),
                };
                _recDelLid = RecDelBake("recdellid", k, 2f, RecDelVbHandleTop - 1f, 20f, 6f,
                    RecDelVbBinStroke, segs, Vector4.zero);
            }
            return _recDelLid;
        }
    }

    /// <summary>一段两端圆头的横杆：对勾就是两段拼出来的，要按进度横向拉伸，所以左右留圆头边距。</summary>
    private static Sprite RecDelCapsuleSprite {
        get {
            if (_recDelCapsule == null) {
                float k = RecDelIconSize / 24f;
                var segs = new RecDelSeg[] { RecDelSeg.Line(1.25f, 1.25f, 14.75f, 1.25f) };
                _recDelCapsule = RecDelBake("recdelcaps", k, 0f, 0f, 16f, 2.5f,
                    RecDelVbCheckStroke, segs, new Vector4(1.25f, 0f, 1.25f, 0f));
            }
            return _recDelCapsule;
        }
    }

    /// <summary>圆键里的叉（源 M6 6 18 18 M18 6 6 18）。</summary>
    private static Sprite RecDelCrossSprite {
        get {
            if (_recDelCross == null) {
                float kc = RecDelCircleIcon / 24f;
                var segs = new RecDelSeg[] {
                    RecDelSeg.Line(6f, 6f, 18f, 18f),
                    RecDelSeg.Line(18f, 6f, 6f, 18f),
                };
                _recDelCross = RecDelBake("recdelcross", kc, 4.25f, 4.25f, 15.5f, 15.5f,
                    RecDelVbCircleStroke, segs, Vector4.zero);
            }
            return _recDelCross;
        }
    }

    /// <summary>面板边上那个尖朝按键的三角（源组件的 clip-path 形状，烘成一张贴图）。</summary>
    private static Sprite RecDelCaretSprite {
        get {
            if (_recDelCaret == null) {
                int w = Mathf.Max(4, Mathf.RoundToInt(RecDelCaretW * RecDelSS));
                int h = Mathf.Max(4, Mathf.RoundToInt(RecDelCaretH * RecDelSS));
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                PerfDiag.Tag(tex, "recdelcaret");
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++) {
                    float v = (y + 0.5f) / h;                      // 0 在下、1 在上
                    float t = 1f - Mathf.Abs(v * 2f - 1f);         // 中间最宽
                    for (int x = 0; x < w; x++) {
                        float u = (x + 0.5f) / w;
                        float a = Mathf.Clamp01((t - u) * w + 0.5f);   // 尖朝左，右边是底边
                        px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                _recDelCaret = PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, w, h),
                    new Vector2(0.5f, 0.5f), 100f * RecDelSS), "recdelcaret");
            }
            return _recDelCaret;
        }
    }

    /// <summary>触发键与面板的圆角底（九宫格）。</summary>
    private static Sprite RecDelRoundSprite {
        get {
            if (_recDelRound == null) _recDelRound = CreateRoundedSprite(64, RecDelRadius);
            return _recDelRound;
        }
    }

    /// <summary>圆键的整圆底（Simple 缩到目标直径，靠双线性采样抗锯齿）。</summary>
    private static Sprite RecDelCircleSprite {
        get {
            if (_recDelCircle == null) _recDelCircle = CreateRoundedSprite(64, 32f);
            return _recDelCircle;
        }
    }

    // ---------- 补间与弹簧 ----------

    /// <summary>一段「从 A 到 B」的补间；Lid = 走盖子那条会过冲的缓动，其余走主缓动。
    /// Delay 用来做面板的 0.14 秒延迟与两个圆键的错峰。</summary>
    private struct RecDelTw {
        public float From, To, T, Dur, Delay;
        public bool Lid, On;
        public void Start(float from, float to, float dur, bool lid, float delay) {
            From = from; To = to; Dur = dur; Lid = lid; Delay = delay; T = 0f; On = true;
        }
        /// <summary>推进一帧；返回真表示值变了（没在跑就一次也不写）。</summary>
        public bool Advance(float dt) {
            if (!On) return false;
            if (Delay > 0f) {
                Delay -= dt;
                if (Delay > 0f) return false;
                T = -Delay;          // 延迟用剩的那一点补给已用时长，错峰不会被整帧拉长
                Delay = 0f;
            }
            T += dt;
            if (T >= Dur) { T = Dur; On = false; }
            return true;
        }
        public float Value {
            get {
                float t = Dur > 0f ? Mathf.Clamp01(T / Dur) : 1f;
                float e = Lid ? RecDelBezier(RecDelLidX1, RecDelLidY1, RecDelLidX2, RecDelLidY2, t)
                              : RecDelBezier(RecDelEaseX1, RecDelEaseY1, RecDelEaseX2, RecDelEaseY2, t);
                return Mathf.LerpUnclamped(From, To, e);
            }
        }
    }

    /// <summary>圆键缩放的弹簧（源 PRESS：stiffness 520 / damping 18 / mass 0.5），定步长子步推进。</summary>
    private struct RecDelSpring {
        public float X, V, Target;
        public bool On;
        public void Aim(float target) { Target = target; On = true; }
        public bool Advance(float dt) {
            if (!On) return false;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / n;
            for (int i = 0; i < n; i++) {
                float a = (-RecDelSpringK * (X - Target) - RecDelSpringC * V) / RecDelSpringM;
                V += a * h;
                X += V * h;
            }
            if (Mathf.Abs(X - Target) < 0.0006f && Mathf.Abs(V) < 0.006f) { X = Target; V = 0f; On = false; }
            return true;
        }
    }

    /// <summary>三次贝塞尔缓动：按 x 反解参数再取 y（与源组件的 CSS cubic-bezier 同一套），牛顿迭代六次、不分配。</summary>
    private static float RecDelBezier(float x1, float y1, float x2, float y2, float x) {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        float s = x;
        for (int i = 0; i < 6; i++) {
            float err = RecDelBezierAxis(x1, x2, s) - x;
            if (Mathf.Abs(err) < 0.0005f) break;
            float d = RecDelBezierSlope(x1, x2, s);
            if (Mathf.Abs(d) < 0.0001f) break;
            s = Mathf.Clamp01(s - err / d);
        }
        return RecDelBezierAxis(y1, y2, s);
    }
    private static float RecDelBezierAxis(float a1, float a2, float t) {
        float mt = 1f - t;
        return 3f * mt * mt * t * a1 + 3f * mt * t * t * a2 + t * t * t;
    }
    private static float RecDelBezierSlope(float a1, float a2, float t) {
        float mt = 1f - t;
        return 3f * mt * mt * a1 + 6f * mt * t * (a2 - a1) + 3f * t * t * (1f - a2);
    }

    // ---------- 一个删除键的全部状态 ----------

    private sealed class RecDelBtn {
        public GameObject Root;
        public RectTransform RootRt, IconRt, BinRt, CheckRt, BodyRt, LidRt, PanelRt, TriggerRt;
        public CanvasGroup BinCg, CheckCg, PanelCg;
        public CanvasGroup[] CircleCg;
        public RectTransform[] BarRt, CircleRt;
        public Image[] CircleImg;
        public Button[] CircleBtn;
        public RecDelTw Width, Lid, Wall, PanelX, PanelA, Draw, Settle, Tap;
        public RecDelTw[] CircleA, CircleScale;
        public RecDelSpring[] CircleS;
        public float[] CircleBgT;
        public bool[] CircleHover;
        public int SwapPhase, SwapTarget;                  // 1 = 旧图标退场，2 = 新图标进场
        public float SwapT, BinScale = 1f, CheckScale = 1f, BinSettle = 1f;
        public bool Open, Interactable;
        public int Status;                                 // 0 常态 / 1 已删 / 2 已留
        public float HoldT;
        public System.Action OnConfirm, OnSettled;
        public string SettleKey;                           // 这个键属于哪一份列表（同键的刷新请求合一）
    }

    private static readonly List<RecDelBtn> _recDelBtns = new List<RecDelBtn>();

    // 刷新请求的"同键合一"队列。一次删除会让整份列表重建，而被重建掉的旧按键要到下一帧
    // 才会被发现（Object.Destroy 是帧末生效）—— 每个旧按键各报一次重建的话，同一帧里
    // 就会重建 N 次、每次又造出 N 个新按键，下一帧待报告的翻倍、帧时间指数上涨
    // （玩家日志实测 62ms → 564 → 2265 → 8515 → 53100ms）。所以按"重建区域"合流：
    // 同一帧里同一个键只重建一次。键由调用方给（详情区 / 关卡选择弹窗各是一份列表）。
    private static readonly List<string> _recDelSettleKeys = new List<string>();
    private static readonly List<System.Action> _recDelSettleActs = new List<System.Action>();
    private static readonly Color RecDelClear = new Color(0f, 0f, 0f, 0f);

    // 自己轮询的鼠标状态（这些行收不到 uGUI 事件：谱面那块背景会把射线吃掉，
    // 全项目对列表里的交互一律是"自己读鼠标 + MouseInRect"，见 TickGlobalWheel 的说明）。
    private static bool _recDelDown, _recDelPressed, _recDelBlocked;
    private static int _recDelHitLog;

    // ---------- 建 ----------

    /// <summary>在一条通关记录条的末尾放一个删除键：按键贴行的右缘，确认面板往左展开。
    /// onConfirm 在按下对勾那一刻跑（源组件就是这么调的，数据立刻落盘）；
    /// onSettled 在对勾停留结束、列表该重建的时候跑（那 1.4 秒不播完的话，整行动画会被当场销毁、一眼都看不到）。
    /// settleKey 说明"这一次刷新是哪一份列表"：同一帧里同一个键只会真的重建一次（见 _recDelSettleKeys 的说明）。</summary>
    private static void MakeRecordDeleteButton(Transform parent, string name, string settleKey, System.Action onConfirm, System.Action onSettled) {
        var b = new RecDelBtn { OnConfirm = onConfirm, OnSettled = onSettled, SettleKey = settleKey, Interactable = true };
        float k = RecDelIconSize / 24f;
        float kc = RecDelCircleIcon / 24f;

        // 容器：宽度 48 → 132 的补间。右缘钉在行右缘（镜像之后右缘不动、往左长）
        var root = MakeImage(parent, name, RecDelSurface);
        root.sprite = RecDelRoundSprite;
        root.type = Image.Type.Sliced;
        root.raycastTarget = false;
        var lift = root.gameObject.AddComponent<Shadow>();
        lift.effectColor = new Color(0f, 0f, 0f, 0.35f);   // 源组件两层投影里的第一层（uGUI 没有模糊，第二层略）
        lift.effectDistance = new Vector2(0f, -1f);
        var rrt = root.rectTransform;
        rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f);
        rrt.pivot = new Vector2(1f, 0.5f);
        rrt.sizeDelta = new Vector2(RecDelH, RecDelH);
        rrt.anchoredPosition = new Vector2(-RecDelMargin, 0f);
        b.Root = root.gameObject;
        b.RootRt = rrt;

        // 确认面板：钉在容器左缘（源的 inset-y-0 right-0 镜像过来），整块跟着容器一起长
        var panel = MakeImage(root.transform, "Panel", RecDelRecess);
        panel.sprite = RecDelRoundSprite;
        panel.type = Image.Type.Sliced;
        panel.raycastTarget = false;
        b.PanelRt = panel.rectTransform;
        b.PanelRt.anchorMin = b.PanelRt.anchorMax = new Vector2(0f, 0.5f);
        b.PanelRt.pivot = new Vector2(0f, 0.5f);
        b.PanelRt.sizeDelta = new Vector2(RecDelPanelW, RecDelH);
        b.PanelRt.anchoredPosition = new Vector2(RecDelSlide, 0f);
        b.PanelCg = panel.gameObject.AddComponent<CanvasGroup>();
        b.PanelCg.alpha = 0f;          // CanvasGroup 默认是 1：不清零的话展开时那 0.14 秒延迟里面板会先亮一下
        panel.gameObject.SetActive(false);

        // 指示三角：贴面板靠按键那一侧的外面，尖朝按键（源组件的 clip-path 三角，镜像后翻过来）
        var caret = MakeImage(panel.transform, "Caret", RecDelRecess);
        caret.sprite = RecDelCaretSprite;
        caret.type = Image.Type.Simple;
        caret.raycastTarget = false;
        var crt = caret.rectTransform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(RecDelCaretW, RecDelCaretH);
        crt.anchoredPosition = new Vector2(-RecDelCaretW * 0.5f, 0f);   // 一半露在面板外
        crt.localScale = new Vector3(-1f, 1f, 1f);

        // 触发键：钉在容器右缘（源组件是容器最左边那个 48 x 48），面板在它左边
        var trigger = MakeButton(root.transform, "Trigger", Vector2.zero, new Vector2(RecDelH, RecDelH), RecDelClear);
        trigger.transition = Selectable.Transition.None;
        var trrt = trigger.GetComponent<RectTransform>();
        trrt.anchorMin = trrt.anchorMax = new Vector2(1f, 0.5f);
        trrt.pivot = new Vector2(1f, 0.5f);
        trrt.anchoredPosition = Vector2.zero;
        trrt.SetAsLastSibling();                           // 源组件那句 z-10：按键盖在面板上面
        b.TriggerRt = trrt;

        var icon = new GameObject("Icon", typeof(RectTransform));
        icon.transform.SetParent(trigger.transform, false);
        b.IconRt = icon.GetComponent<RectTransform>();
        b.IconRt.anchorMin = b.IconRt.anchorMax = new Vector2(0.5f, 0.5f);
        b.IconRt.pivot = new Vector2(0.5f, 0.5f);
        b.IconRt.sizeDelta = new Vector2(RecDelIconSize, RecDelIconSize);
        b.IconRt.anchoredPosition = Vector2.zero;

        // 垃圾桶：桶身（九宫格纵向拉伸 = 上沿下移）+ 盖子（只转它）
        var bin = new GameObject("Bin", typeof(RectTransform));
        bin.transform.SetParent(icon.transform, false);
        b.BinRt = bin.GetComponent<RectTransform>();
        b.BinRt.anchorMin = b.BinRt.anchorMax = new Vector2(0.5f, 0.5f);
        b.BinRt.pivot = new Vector2(0.5f, 0.5f);
        b.BinRt.sizeDelta = Vector2.zero;
        b.BinCg = bin.AddComponent<CanvasGroup>();

        var body = MakeImage(bin.transform, "Body", RecDelGlyph);
        body.sprite = RecDelBodySprite;
        body.type = Image.Type.Sliced;
        body.raycastTarget = false;
        b.BodyRt = body.rectTransform;
        b.BodyRt.anchorMin = b.BodyRt.anchorMax = new Vector2(0.5f, 0.5f);
        b.BodyRt.pivot = new Vector2(0.5f, 0f);
        b.BodyRt.sizeDelta = new Vector2(16f * k, (RecDelVbFoot + 1f - RecDelVbWallTop) * k);
        b.BodyRt.anchoredPosition = new Vector2(0f, (12f - (RecDelVbFoot + 1f)) * k);

        var lid = new GameObject("Lid", typeof(RectTransform));
        lid.transform.SetParent(bin.transform, false);
        b.LidRt = lid.GetComponent<RectTransform>();
        b.LidRt.anchorMin = b.LidRt.anchorMax = new Vector2(0.5f, 0.5f);
        b.LidRt.pivot = new Vector2(0.5f, 0.5f);
        b.LidRt.sizeDelta = Vector2.zero;
        b.LidRt.anchoredPosition = new Vector2((RecDelVbLidX0 - 12f) * k, (12f - RecDelVbLidY) * k);
        var lidArt = MakeImage(lid.transform, "Art", RecDelGlyph);
        lidArt.sprite = RecDelLidSprite;
        lidArt.type = Image.Type.Simple;
        lidArt.raycastTarget = false;
        var lart = lidArt.rectTransform;
        lart.anchorMin = lart.anchorMax = new Vector2(0.5f, 0.5f);
        lart.pivot = new Vector2(0.5f, 0.5f);
        lart.sizeDelta = new Vector2(20f * k, 6f * k);
        lart.anchoredPosition = new Vector2(9f * k, 2f * k);

        // 对勾：两段圆头横杆，按进度一段段长出来（源的 pathLength 0 → 1）
        var check = new GameObject("Check", typeof(RectTransform));
        check.transform.SetParent(icon.transform, false);
        b.CheckRt = check.GetComponent<RectTransform>();
        b.CheckRt.anchorMin = b.CheckRt.anchorMax = new Vector2(0.5f, 0.5f);
        b.CheckRt.pivot = new Vector2(0.5f, 0.5f);
        b.CheckRt.sizeDelta = Vector2.zero;
        b.CheckCg = check.AddComponent<CanvasGroup>();
        b.CheckCg.alpha = 0f;
        b.BarRt = new RectTransform[2];
        MakeCheckBars(check.transform, k, RecDelAccent, b.BarRt);
        b.BarRt[0].gameObject.SetActive(false);
        b.BarRt[1].gameObject.SetActive(false);
        check.SetActive(false);

        // 面板里两个圆键。源组件的子元素顺序是先确认后取消，错峰顺序照它；
        // 位置镜像之后对勾挨着触发键（都在右边），叉在左边
        b.CircleRt = new RectTransform[2];
        b.CircleCg = new CanvasGroup[2];
        b.CircleImg = new Image[2];
        b.CircleBtn = new Button[2];
        b.CircleS = new RecDelSpring[2];
        b.CircleA = new RecDelTw[2];
        b.CircleScale = new RecDelTw[2];
        b.CircleBgT = new float[2];
        b.CircleHover = new bool[2];
        float half = RecDelPanelW * 0.5f;
        float slot = (RecDelPanelW - (RecDelCircleD * 2f + RecDelGap)) * 0.5f;
        string[] names = { "Confirm", "Cancel" };
        for (int i = 0; i < 2; i++) {
            b.CircleS[i].X = 1f;
            b.CircleS[i].Target = 1f;
            var c = MakeButton(panel.transform, names[i], Vector2.zero, new Vector2(RecDelCircleD, RecDelCircleD), RecDelSurface);
            c.transition = Selectable.Transition.None;
            var cimg = c.GetComponent<Image>();
            cimg.sprite = RecDelCircleSprite;
            cimg.type = Image.Type.Simple;
            var crt2 = c.GetComponent<RectTransform>();
            crt2.anchorMin = crt2.anchorMax = new Vector2(0.5f, 0.5f);
            crt2.pivot = new Vector2(0.5f, 0.5f);
            crt2.anchoredPosition = new Vector2((i == 0 ? slot + RecDelCircleD * 1.5f + RecDelGap
                                                       : slot + RecDelCircleD * 0.5f) - half, 0f);
            b.CircleRt[i] = crt2;
            b.CircleImg[i] = cimg;
            b.CircleBtn[i] = c;
            b.CircleCg[i] = c.gameObject.AddComponent<CanvasGroup>();
            b.CircleCg[i].alpha = 0f;  // 同上：两个圆键在面板那 0.14 秒延迟里也不许先亮出来
            if (i == 0) MakeCheckBars(c.transform, kc, RecDelAccent, null);
            else {
                var cross = MakeImage(c.transform, "X", RecDelGlyph);
                cross.sprite = RecDelCrossSprite;
                cross.type = Image.Type.Simple;
                cross.raycastTarget = false;
                var xrt = cross.rectTransform;
                xrt.anchorMin = xrt.anchorMax = new Vector2(0.5f, 0.5f);
                xrt.pivot = new Vector2(0.5f, 0.5f);
                xrt.sizeDelta = new Vector2(15.5f * kc, 15.5f * kc);
                xrt.anchoredPosition = Vector2.zero;
            }
        }

        // 建出来就是常态：一个补间都没挂，所以刷新列表时不会满屏一起飘
        b.Width.From = b.Width.To = RecDelH;
        b.Wall.From = b.Wall.To = RecDelVbWallTop;
        b.Lid.From = b.Lid.To = 0f;
        b.Tap.From = b.Tap.To = 1f;
        b.PanelA.From = b.PanelA.To = 0f;
        b.PanelX.From = b.PanelX.To = RecDelSlide;
        _recDelBtns.Add(b);
    }

    /// <summary>对勾的两段：起点与角度固定，只有长度按进度长（into 为 null 就是画一把定长的）。</summary>
    private static void MakeCheckBars(Transform parent, float k, Color color, RectTransform[] into) {
        float[] startX = { -8f, -2.5f }, startY = { -0.5f, -6f };
        float[] deg = { RecDelCheckDegA, RecDelCheckDegB };
        float[] len = { RecDelVbCheckFirst, RecDelVbCheckTotal - RecDelVbCheckFirst };
        for (int i = 0; i < 2; i++) {
            var bar = MakeImage(parent, "Bar" + i, color);
            bar.sprite = RecDelCapsuleSprite;
            bar.type = Image.Type.Sliced;
            bar.raycastTarget = false;
            var rt = bar.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(len[i] * k, RecDelVbCheckStroke * k);
            rt.anchoredPosition = new Vector2(startX[i] * k, startY[i] * k);
            rt.localRotation = Quaternion.Euler(0f, 0f, deg[i]);
            if (into != null) into[i] = rt;
        }
    }

    // ---------- 状态机 ----------

    /// <summary>展开：宽度、盖子、桶壁、面板与两个圆键一起动（延迟与错峰照源组件）。</summary>
    private static void RecDelOpen(RecDelBtn b) {
        b.Open = true;
        b.Width.Start(b.Width.Value, RecDelH + RecDelPanelW, RecDelWidthSec, false, 0f);
        b.Lid.Start(b.Lid.Value, RecDelLidOpenDeg, RecDelLidSec, true, 0f);
        b.Wall.Start(b.Wall.Value, RecDelVbWallTopOpen, RecDelWallSec, false, 0f);
        b.PanelRt.gameObject.SetActive(true);
        b.PanelX.Start(RecDelSlide, 0f, RecDelInSec, false, RecDelInDelay);
        b.PanelA.Start(0f, 1f, RecDelInSec, false, RecDelInDelay);
        for (int i = 0; i < 2; i++) {
            b.CircleA[i].Start(0f, 1f, RecDelInSec, false, RecDelInDelay + RecDelStagger * i);
            b.CircleScale[i].Start(0.9f, 1f, RecDelInSec, false, RecDelInDelay + RecDelStagger * i);
            b.CircleS[i].X = 1f; b.CircleS[i].V = 0f; b.CircleS[i].Aim(1f);
            b.CircleRt[i].localScale = Vector3.one * 0.9f;
        }
    }

    /// <summary>收起来并进入结果态：next = 1 删掉 / 2 取消。源组件在这一步就调回调，这里也是。</summary>
    private static void RecDelResolve(RecDelBtn b, int next) {
        if (!b.Interactable || b.Status != 0) return;
        b.Open = false;
        b.Status = next;
        b.HoldT = 0f;
        b.Width.Start(b.Width.Value, RecDelH, RecDelWidthSec, false, 0f);
        b.Lid.Start(b.Lid.Value, 0f, RecDelLidSec, true, 0f);
        b.Wall.Start(b.Wall.Value, RecDelVbWallTop, RecDelWallSec, false, 0f);
        b.PanelX.Start(b.PanelX.Value, RecDelSlide, RecDelOutSec, false, 0f);
        b.PanelA.Start(b.PanelA.Value, 0f, RecDelOutSec, false, 0f);
        for (int i = 0; i < 2; i++) {
            b.CircleA[i].Start(b.CircleA[i].Value, 0f, RecDelOutSec, false, 0f);
            b.CircleScale[i].Start(b.CircleScale[i].Value, 0.9f, RecDelOutSec, false, 0f);
        }
        if (next == 1) {
            b.SwapTarget = 1;
            b.SwapPhase = 1;
            b.SwapT = 0f;
            if (b.OnConfirm != null) {
                try { b.OnConfirm(); } catch (System.Exception e) { ReportError("删除通关记录失败", e); }
            }
        } else {
            b.Settle.Start(1f, 1f, RecDelSettleSec, false, 0f);   // 只借它的计时，形状见 RecDelSettleValue
        }
    }

    /// <summary>取消时桶身抖一下：源组件是关键帧 [1, 0.86, 1]，缓动作用在整段上。</summary>
    private static float RecDelSettleValue(RecDelBtn b) {
        float t = b.Settle.Dur > 0f ? Mathf.Clamp01(b.Settle.T / b.Settle.Dur) : 1f;
        float e = RecDelBezier(RecDelEaseX1, RecDelEaseY1, RecDelEaseX2, RecDelEaseY2, t);
        return e < 0.5f ? Mathf.Lerp(1f, 0.86f, e * 2f) : Mathf.Lerp(0.86f, 1f, (e - 0.5f) * 2f);
    }

    /// <summary>桶身随上沿伸缩：下沿永远钉在桶底。</summary>
    private static void RecDelPaintBody(RecDelBtn b) {
        float k = RecDelIconSize / 24f;
        float h = (RecDelVbFoot + 1f - b.Wall.Value) * k;
        if (Mathf.Abs(b.BodyRt.sizeDelta.y - h) > 0.0005f) b.BodyRt.sizeDelta = new Vector2(16f * k, h);
    }

    /// <summary>对勾按进度长出来：第一段先走到头、第二段再接着走（与源的 pathLength 同一条折线）。</summary>
    private static void RecDelPaintCheck(RecDelBtn b, float p) {
        float k = RecDelIconSize / 24f;
        float firstLen = Mathf.Clamp01(p / RecDelCheckSplit) * RecDelVbCheckFirst * k;
        float secondLen = Mathf.Clamp01((p - RecDelCheckSplit) / (1f - RecDelCheckSplit)) * (RecDelVbCheckTotal - RecDelVbCheckFirst) * k;
        if (b.BarRt[0].gameObject.activeSelf != (firstLen > 0.01f)) b.BarRt[0].gameObject.SetActive(firstLen > 0.01f);
        if (b.BarRt[1].gameObject.activeSelf != (secondLen > 0.01f)) b.BarRt[1].gameObject.SetActive(secondLen > 0.01f);
        if (firstLen > 0.01f) b.BarRt[0].sizeDelta = new Vector2(firstLen, RecDelVbCheckStroke * k);
        if (secondLen > 0.01f) b.BarRt[1].sizeDelta = new Vector2(secondLen, RecDelVbCheckStroke * k);
    }

    // ---------- 每帧推进 ----------

    /// <summary>自己读鼠标处理这一次的点击与悬停：触发键展开/收起、面板里两个圆键确认与取消、
    /// 触发键的按压缩放、圆键的悬停底色与按压。
    /// 不走 uGUI 的 Button/EventTrigger —— 这些行在列表里收不到事件（同 TickGlobalWheel 那条理由），
    /// 表现就是"按键画得出来、点了一点反应都没有"。命中判定用 MouseInRect（真实屏幕矩形，不用射线）。
    /// 只在这一帧真的按下时做一次决策，其余每帧只做常数级的悬停判断。</summary>
    private static void RecDelPollInput(RecDelBtn b) {
        if (b.Root == null) return;
        bool down = _recDelDown && !_recDelBlocked;
        bool overTrigger = b.Interactable && MouseInRect(b.TriggerRt);
        // 按下那一刻被上层挡着也报一句（最多几条）：不然"点了没反应"永远只能靠猜
        if (_recDelPressed && _recDelBlocked && _recDelHitLog < 8) {
            _recDelHitLog++;
            Lg.Info("通关记录删除键：按下被上层挡住（第 " + _recDelHitLog + " 次）");
        }
        if (_recDelPressed && !_recDelBlocked) {
            if (overTrigger) {
                DeselectUi();
                if (_recDelHitLog < 4) {
                    _recDelHitLog++;
                    Lg.Info("通关记录删除键：命中（第 " + _recDelHitLog + " 次）"
                        + "｜展开=" + (b.Open ? 1 : 0));
                }
                if (b.Open) RecDelResolve(b, 2);           // 展开着再点一次触发键 = 取消
                else { b.Status = 0; RecDelOpen(b); }
            } else if (b.Open && b.PanelRt != null && b.PanelRt.gameObject.activeSelf) {
                for (int i = 0; i < 2; i++) {
                    if (!MouseInRect(b.CircleRt[i])) continue;
                    DeselectUi();
                    RecDelResolve(b, i == 0 ? 1 : 2);
                    break;
                }
            }
        }
        if (!b.Interactable) return;
        // 触发键按下时缩一下（源组件的 whileTap scale 0.94）
        float tapWant = (overTrigger && down) ? RecDelTriggerPress : 1f;
        if (Mathf.Abs(b.Tap.To - tapWant) > 0.0005f) b.Tap.Start(b.Tap.Value, tapWant, RecDelTapSec, false, 0f);
        // 圆键：悬停放大 + 按下更小；面板没展开时一律归位
        bool panelLive = b.Open && b.PanelRt != null && b.PanelRt.gameObject.activeSelf;
        for (int i = 0; i < 2; i++) {
            bool hover = panelLive && MouseInRect(b.CircleRt[i]);
            b.CircleHover[i] = hover;
            b.CircleS[i].Aim(hover ? (down ? RecDelCirclePress : RecDelCircleHover) : 1f);
        }
    }

    /// <summary>把一个"删完了、列表该重建"的请求收进队列：同一个键同一帧只留一次。
    /// 只收队、不当场重建 —— 当场重建会把这份列表的其它旧按键全变成"已经没了"，
    /// 那些按键下一帧又各报一次，帧时间指数上涨（见 _recDelSettleKeys 的说明）。</summary>
    private static void RecDelQueueSettle(RecDelBtn b) {
        var done = b.OnSettled;
        b.OnSettled = null;
        if (done == null) return;
        string key = b.SettleKey ?? "";
        for (int i = 0; i < _recDelSettleKeys.Count; i++) if (_recDelSettleKeys[i] == key) return;
        _recDelSettleKeys.Add(key);
        _recDelSettleActs.Add(done);
    }

    /// <summary>通关记录删除键的动画。挂常驻段：关卡选择弹窗是独立画布，面板关着时它照样开着
    /// （同 TickSwitchMoves 那条理由）。没活时只花一次 Count 判断。</summary>
    private static void TickRecordDeleteButtons() {
        if (_recDelBtns.Count == 0) { _recDelDown = false; _recDelPressed = false; return; }
        float dt = Mathf.Min(Time.unscaledDeltaTime, RecDelMaxStep);
        // 每帧先把这一次的鼠标读一份（按下那一刻算"这一下"）
        _recDelBlocked = true;
        try { _recDelBlocked = PanelInputBlocked(); } catch { }
        bool down = !_recDelBlocked && UiInput.MouseButton(0);
        _recDelPressed = down && !_recDelDown;
        _recDelDown = down;
        for (int i = _recDelBtns.Count - 1; i >= 0; i--) {
            var b = _recDelBtns[i];
            if (b.Root == null) {
                // 行被重建了。只有"真的确认删过"的那一条才需要补一次刷新（数据早删了，只差重建）；
                // 别的旧按键是被这次重建顺手弄没的，它们再各报一次就会同帧重建 N 次。
                _recDelBtns.RemoveAt(i);
                if (b.Status == 1) RecDelQueueSettle(b);
                continue;
            }
            try { StepRecordDeleteButton(b, dt); } catch (System.Exception e) { TickFail("通关记录删除键", e); }
        }
        // 整圈走完再统一重建：同一帧里同一份列表至多重建一次
        if (_recDelSettleActs.Count > 0) {
            for (int i = 0; i < _recDelSettleActs.Count; i++) {
                var done = _recDelSettleActs[i];
                try { done(); } catch (System.Exception e) { ReportError("删除通关记录后刷新列表失败", e); }
            }
            _recDelSettleActs.Clear();
            _recDelSettleKeys.Clear();
        }
    }

    private static void StepRecordDeleteButton(RecDelBtn b, float dt) {
        RecDelPollInput(b);
        // 容器宽度（右缘钉住，往左长）
        if (b.Width.Advance(dt)) {
            float w = b.Width.Value;
            if (Mathf.Abs(b.RootRt.sizeDelta.x - w) > 0.0005f) b.RootRt.sizeDelta = new Vector2(w, RecDelH);
        }
        // 盖子转角（走的是会过冲的那条缓动）
        if (b.Lid.Advance(dt)) b.LidRt.localRotation = Quaternion.Euler(0f, 0f, b.Lid.Value);
        // 桶壁上沿
        if (b.Wall.Advance(dt)) RecDelPaintBody(b);
        // 面板：位移 + 透明度（退场走完就整块关掉，跟源组件卸载面板一个意思）
        bool panelMoved = b.PanelX.Advance(dt);
        bool panelFaded = b.PanelA.Advance(dt);
        if (panelMoved || panelFaded) {
            b.PanelRt.anchoredPosition = new Vector2(b.PanelX.Value, 0f);
            b.PanelCg.alpha = b.PanelA.Value;
        }
        if (!b.Open && !b.PanelA.On && b.PanelRt.gameObject.activeSelf && b.PanelA.Value <= 0.001f) {
            b.PanelRt.gameObject.SetActive(false);
        }
        // 两个圆键：透明度 + 错峰缩放 + 悬停 / 按下的弹簧 + 底色过渡
        for (int i = 0; i < 2; i++) {
            if (b.CircleA[i].Advance(dt)) b.CircleCg[i].alpha = b.CircleA[i].Value;
            float want = b.CircleHover[i] ? RecDelCircleHover : 1f;
            if (b.CircleS[i].Target != want) b.CircleS[i].Aim(want);
            bool scaleMoved = b.CircleScale[i].Advance(dt);
            bool springMoved = b.CircleS[i].Advance(dt);
            if (scaleMoved || springMoved) {
                b.CircleRt[i].localScale = Vector3.one * (b.CircleScale[i].Value * b.CircleS[i].X);
            }
            float wantBg = b.CircleHover[i] ? 1f : 0f;
            if (Mathf.Abs(b.CircleBgT[i] - wantBg) > 0.0005f) {
                b.CircleBgT[i] = Mathf.MoveTowards(b.CircleBgT[i], wantBg, dt / RecDelColorSec);
                float e = RecDelBezier(RecDelColX1, RecDelColY1, RecDelColX2, RecDelColY2, b.CircleBgT[i]);
                b.CircleImg[i].color = Color.Lerp(RecDelSurface, RecDelHoverBg, e);
            }
        }
        // 触发键按下缩放
        if (b.Tap.Advance(dt)) b.IconRt.localScale = Vector3.one * b.Tap.Value;
        // 图标互换（源的 AnimatePresence mode="wait"：旧的先退完，新的才进；两边都是 0.6 → 1 的缩放）
        float binScale = 1f, checkScale = 1f;
        if (b.SwapPhase != 0) {
            b.SwapT += dt;
            float t = Mathf.Clamp01(b.SwapT / RecDelSwapSec);
            float e = RecDelBezier(RecDelEaseX1, RecDelEaseY1, RecDelEaseX2, RecDelEaseY2, t);
            float sc = b.SwapPhase == 1 ? Mathf.LerpUnclamped(1f, 0.6f, e) : Mathf.LerpUnclamped(0.6f, 1f, e);
            bool touchingBin = (b.SwapTarget == 1) == (b.SwapPhase == 1);
            if (touchingBin) binScale = sc; else checkScale = sc;
            var oldCg = b.SwapTarget == 1 ? b.BinCg : b.CheckCg;
            var newCg = b.SwapTarget == 1 ? b.CheckCg : b.BinCg;
            if (b.SwapPhase == 1) {
                oldCg.alpha = 1f - e;
                if (t >= 1f) {
                    oldCg.alpha = 0f;
                    oldCg.gameObject.SetActive(false);
                    newCg.gameObject.SetActive(true);
                    newCg.alpha = 0f;
                    b.SwapPhase = 2;
                    b.SwapT = 0f;
                    if (b.SwapTarget == 1) b.Draw.Start(0f, 1f, RecDelSettleSec, false, 0f);
                }
            } else {
                newCg.alpha = e;
                if (t >= 1f) { newCg.alpha = 1f; b.SwapPhase = 0; }
            }
        }
        if (b.BinScale != binScale) { b.BinScale = binScale; b.BinRt.localScale = Vector3.one * binScale; }
        if (b.CheckScale != checkScale) { b.CheckScale = checkScale; b.CheckRt.localScale = Vector3.one * checkScale; }
        // 桶身抖动（取消）：1 → 0.86 → 1，与上面的互换缩放相乘
        if (b.Settle.On) {
            b.Settle.Advance(dt);
            float sv = b.Settle.On ? RecDelSettleValue(b) : 1f;
            if (Mathf.Abs(b.BinSettle - sv) > 0.0005f) {
                b.BinSettle = sv;
                b.BinRt.localScale = Vector3.one * (sv * b.BinScale);
            }
        }
        if (b.Draw.Advance(dt)) RecDelPaintCheck(b, b.Draw.Value);
        // 结果态的停留与收尾
        if (b.Status != 0 && !b.Open) {
            b.HoldT += dt;
            if (b.HoldT >= (b.Status == 1 ? RecDelHoldDeleted : RecDelHoldKept)) {
                b.HoldT = 0f;
                if (b.Status == 1) {
                    // 删掉这条：对勾放完了，按键不再响应，列表等这一帧的队列统一重建
                    b.Interactable = false;
                    RecDelQueueSettle(b);
                } else {
                    b.Status = 0;
                    b.SwapTarget = 0;
                    b.SwapPhase = 1;
                    b.SwapT = 0f;
                    if (b.Draw.Value > 0f) b.Draw.Start(b.Draw.Value, 0f, RecDelSwapSec, false, 0f);
                }
            }
        }
        // 展开着的时候 Esc 等于点叉 —— 那一条判据在 PortalUI.EscClose.cs 的 Esc 链里。
        // 这里别再读一次：Input.GetKeyDown 同一帧谁读都是 true，两处都动手会一次收两层。
    }
}
