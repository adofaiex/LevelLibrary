using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：斜切板与几何基元（按ArknightsUI 示意图的视觉语言）。
/// 示意图里那些块的斜感有两层来源，这一层是第一层：
///   ·块本身是斜切的（左端一道斜边 / 整体是平行四边形）—— 本文件负责
///   · 整组容器绕Y 轴旋转（透视投影）—— 见 PortalUI.Ark3D.cs
/// 两者叠在一起才是示意图那个观感，缺一个都不对。
/// 为什么不用 skew：uGUI 的 RectTransform 只有平移/旋转/缩放，没有 CSS 那种 skew，
/// 也没有 clip-path。要拿到"这块的左边沿是斜的"，只能自己出网格 ——
/// 所以这里继承 Graphic 重写 OnPopulateMesh，按四个角点画一个四边形。
/// 四种形状都由同一段代码算出（CornerMode）：
///   None        直角（不切）
///   Left        左上左下两点各向外让出 cut（左边一条斜边，右边保持竖直）
///   Parallelogram左右都切、方向相反（整块歪着，是示意图里最常见的那种）
///   TopLeft     只切左上角
/// 附带：`ArkFrame` 是九宫格圆角板（截图里那些半透明底板），
/// `ArkIcon` 把示意图那几个 SVG 图标画成程序化贴图（不引外部文件）。</summary>
public static partial class PortalUI {

    // ===================== 斜切板 =====================

    /// <summary>斜切方式（切哪几个角、往哪边切）</summary>
    public enum CornerMode {
        None,            // 直角
        Left,          // 左边一条斜边（右上右下不动）
        Parallelogram,  // 左右都切、方向相反
        TopLeft,       // 只切左上角
    }

    /// <summary>一块斜切板。四角由 `CornerMode` 与 `Cut` 决定，网格在 OnPopulateMesh 里现算。</summary>
    public class SkewedImage : Graphic {
        [SerializeField] private CornerMode _mode = CornerMode.Left;
        [SerializeField] private float _cut = 0f;         // 斜边在竖直方向上让出的像素

        public CornerMode Mode {
            get { return _mode; }
            set { if (_mode != value) { _mode = value; SetVerticesDirty(); } }
        }
        public float Cut {
            get { return _cut; }
            set { if (!Mathf.Approximately(_cut, value)) { _cut = value; SetVerticesDirty(); } }
        }
        /// <summary>切边斜率：像素/像素。示意图里那种 12px 边走 40px 高，约 0.3。</summary>
        public float SkewRatio {
            get { float h = rectTransform.rect.height; return h > 1f ? _cut / h : 0f; }
            set { Cut = value * rectTransform.rect.height; }
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f) return;

            float c = Mathf.Clamp(_cut, 0f, Mathf.Min(w, h) * 0.9f);
            float x0 = r.xMin, y0 = r.yMin, x1 = r.xMax, y1 = r.yMax;
            Vector2 lt, rt, rb, lb;

            switch (_mode) {
                case CornerMode.Left:
                    // 左边斜：左上向左让出 c，左下向右让回 c（形成一条 / 方向的边）
                    lt = new Vector2(x0 - c, y1);
                    lb = new Vector2(x0 + c, y0);
                    rt = new Vector2(x1, y1);
                    rb = new Vector2(x1, y0);
                    break;
                case CornerMode.Parallelogram:
                    // 上边整体右移、下边左移 => 左右两条边同向斜
                    lt = new Vector2(x0 + c, y1);
                    rt = new Vector2(x1 + c, y1);
                    rb = new Vector2(x1 - c, y0);
                    lb = new Vector2(x0 - c, y0);
                    break;
                case CornerMode.TopLeft:
                    lt = new Vector2(x0 - c, y1);
                    lb = new Vector2(x0, y0);
                    rt = new Vector2(x1, y1);
                    rb = new Vector2(x1, y0);
                    break;
                default:
                    lt = new Vector2(x0, y1); rt = new Vector2(x1, y1);
                    rb = new Vector2(x1, y0); lb = new Vector2(x0, y0);
                    break;
            }

Color32 c32 = color;
  // VertexHelper.AddVert 返回 void（顶点下标从 0 顺排），所以自己数：
      // 四个点依次是 0/1/2/3，三角形按这个下标连
            vh.AddVert(lt, c32, new Vector2(0f, 1f));   // 0 左上
     vh.AddVert(rt, c32, new Vector2(1f, 1f));   // 1 右上
            vh.AddVert(rb, c32, new Vector2(1f, 0f));   // 2 右下
            vh.AddVert(lb, c32, new Vector2(0f, 0f));   // 3 左下
      vh.AddTriangle(0, 1, 2);
   vh.AddTriangle(2, 3, 0);
        }

        /// <summary>斜切板不吃射线的默认值之外的东西：按矩形判即可（斜边那块仍然算点得到，
      /// 与示意图那种"整块都是按钮"的口径一致 —— 边角差几像素不影响手感）。</summary>
        public override bool Raycast(Vector2 sp, Camera eventCamera) {
        if (!canvasRenderer.cull) return false;
            if (eventCamera != null && !IsActive()) return false;
      return GetPixelAdjustedRect().Contains(sp);
        }
    }

    // ===================== 逐顶点透视 =====================

    /// <summary>把"整层绕 Y 轴倾斜"的透视，逐顶点做进每个图元里。
    /// 为什么不直接用相机：大厅这块画布必须是 Overlay。Unity 的渲染顺序是
    /// 「所有 Overlay 画布都排在所有相机之后」，也就是说相机画布无论 sortingOrder 多高，
    /// 都排不到面板那块 Overlay 画布后面去 —— 表现就是大厅一直被面板压着、
    /// 只在面板画布开合途中短暂消失的那两帧闪一下。
    /// 而 Overlay 没有相机做投影，所以透视改由这里逐顶点算：
    /// 公式与 CSS 的 perspective(30rem) rotateY(deg) 等价（同一个针孔投影），
    /// 观察者在 z = +Dist 处，物体在 z = 0 平面。
    /// 顶点是在图元自己的局部坐标里给的（原点在它自己的 pivot），
    /// 所以要先把点抬到层坐标、投影、再落回图元局部坐标 —— 输出会被 uGUI 的
    /// localToWorldMatrix 再变一次，两者必须抵消掉。
    /// 层本身因此不能再旋转（会变成双重变换），倾斜全部由这里负责。</summary>
    public class ArkPerspective : BaseMeshEffect {
        /// <summary>所属的层（透视的参考系，原点在层中心）</summary>
        public RectTransform Layer;
        /// <summary>透视原点，层坐标。等价于 CSS 的 perspective-origin ——
        /// 示意图里 perspective(30rem) 写在那个 3D 容器上，原点就是容器中心（默认 50% 50%）。
        /// 用画布中心当原点会错得很远：右区的内容整体偏在中心右侧，
        /// 那样算出来边缘要放大到 1.26 倍、整块被推出画布外。</summary>
        public Vector2 Origin;
        private float _yaw, _pitch;
        public float Dist = 480f;        // 观察者到画布平面的距离（与示意图的 perspective(30rem) 一致）

        public float Yaw { get { return _yaw; } }
        public float Pitch { get { return _pitch; } }

        /// <summary>写入倾斜角；值真的变了才让 uGUI 重建顶点（鼠标不动时一帧都不重算）。</summary>
        public void SetTilt(float yaw, float pitch) {
            if (Mathf.Approximately(_yaw, yaw) && Mathf.Approximately(_pitch, pitch)) return;
            _yaw = yaw;
            _pitch = pitch;
            MarkDirty();
        }

        /// <summary>把这块的渲染矩形按当前投影预缩宽，让它在屏幕上看起来是正圆。
        /// 为什么需要：针孔投影的横向放大随 x 变化，离透视原点远的图形会被横向拉长（~10% 的椭圆）。
        /// 做法：把矩形四角按同一套投影公式算一遍得出显示宽高比，宽度乘这个比例收窄，
        /// 并按"轴心在矩形里的位置"修正位置保持中心不动；迭代两次收敛（第二次误差 <1%）。</summary>
        public void PreSquashToCircle() {
            if (graphic == null || Layer == null) return;
            var me = graphic.rectTransform;
            if (me == null) return;
            for (int k = 0; k < 2; k++) {
                Vector2 pivot = Layer.InverseTransformPoint(me.TransformPoint(Vector3.zero));
                float cy = Mathf.Cos(_yaw * Mathf.Deg2Rad), sy = Mathf.Sin(_yaw * Mathf.Deg2Rad);
                float cp = Mathf.Cos(_pitch * Mathf.Deg2Rad), sp = Mathf.Sin(_pitch * Mathf.Deg2Rad);
                float d0 = Mathf.Max(1f, Dist);
                float ox = Origin.x, oy = Origin.y;
                var r = me.rect;
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int c = 0; c < 4; c++) {
                    float vx = (c & 1) == 0 ? r.xMin : r.xMax;
                    float vy = (c & 2) == 0 ? r.yMin : r.yMax;
                    float rx = pivot.x + vx - ox;
                    float ry = pivot.y + vy - oy;
                    float x1 = rx * cy;
                    float z1 = -rx * sy;
                    float y2 = ry * cp + z1 * sp;
                    float z2 = -ry * sp + z1 * cp;
                    float s = d0 / Mathf.Max(1f, d0 - z2);
                    float px = x1 * s, py = y2 * s;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
                float w = Mathf.Max(1f, maxX - minX), h = Mathf.Max(1f, maxY - minY);
                float kk = h / w;                          // 宽度要收的比例
                if (Mathf.Abs(kk - 1f) < 0.004f) break;    // 已经够圆
                float oldW = me.sizeDelta.x;
                float newW = Mathf.Max(4f, oldW * kk);
                me.sizeDelta = new Vector2(newW, me.sizeDelta.y);
                var ap = me.anchoredPosition;
                ap.x += (0.5f - me.pivot.x) * (oldW - newW);   // 保持中心不动
                me.anchoredPosition = ap;
            }
            MarkDirty();
        }

        /// <summary>让这一块的顶点重算。层的位移（鼠标反向位移）变化时也要走它 ——
        /// 投影公式里用到了"图元在层里的位置"，层一挪那个位置就变了。</summary>
        public void MarkDirty() {
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh) {
            if (Layer == null || graphic == null) return;
            var me = graphic.rectTransform;
            if (me == null) return;
            int n = vh.currentVertCount;
            if (n <= 0) return;
            // 角度都是 0 时是恒等变换，直接跳过（省掉整层的顶点重算）
            if (Mathf.Approximately(_yaw, 0f) && Mathf.Approximately(_pitch, 0f)) return;

            // 这个图元的 pivot 落在层的哪个位置。层的位移（鼠标反向位移）每帧在变，
            // 所以这一项不能缓存，得跟着算。
            Vector3 pivot = Layer.InverseTransformPoint(me.TransformPoint(Vector3.zero));

            float cy = Mathf.Cos(_yaw * Mathf.Deg2Rad), sy = Mathf.Sin(_yaw * Mathf.Deg2Rad);
            float cp = Mathf.Cos(_pitch * Mathf.Deg2Rad), sp = Mathf.Sin(_pitch * Mathf.Deg2Rad);
            float d0 = Mathf.Max(1f, Dist);
            float ox = Origin.x, oy = Origin.y;

            UIVertex vt = default(UIVertex);
            for (int i = 0; i < n; i++) {
                vh.PopulateUIVertex(ref vt, i);
                Vector3 v = vt.position;
                // 相对透视原点取坐标，投影完再加回去
                float rx = pivot.x + v.x - ox;
                float ry = pivot.y + v.y - oy;
                // 绕 Y 轴（过透视原点的竖直轴）
                float x1 = rx * cy;
                float z1 = -rx * sy;
                // 绕 X 轴
                float y2 = ry * cp + z1 * sp;
                float z2 = -ry * sp + z1 * cp;
                // 针孔投影：观察者在 z = +d0
                float d = d0 - z2;
                if (d < 1f) d = 1f;
                float s = d0 / d;
                vt.position = new Vector3(x1 * s + ox - pivot.x, y2 * s + oy - pivot.y, v.z);
                vh.SetUIVertex(vt, i);
            }
        }
    }

    /// <summary>给这一层下所有图元挂上逐顶点透视（顺便把角度与透视原点写进去）。
    /// 建完之后调一次即可 —— 之后 `SetTilt` 会自己让 uGUI 重建顶点。
    /// 透视原点取"这一层内容的包围盒中心"，与示意图把 perspective 写在容器上同义。</summary>
    private static ArkPerspective[] AttachArkPerspective(RectTransform layer, float yaw) {
        if (layer == null) return null;
        var gs = layer.GetComponentsInChildren<Graphic>(true);
        var list = new System.Collections.Generic.List<ArkPerspective>(gs.Length);
        // 先算内容包围盒（层坐标，原点在层中心）
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < gs.Length; i++) {
            var g = gs[i];
            if (g == null) continue;
            var rt = g.rectTransform;
            if (rt == null) continue;
            Vector3 a = layer.InverseTransformPoint(rt.TransformPoint(new Vector3(rt.rect.xMin, rt.rect.yMin, 0f)));
            Vector3 b = layer.InverseTransformPoint(rt.TransformPoint(new Vector3(rt.rect.xMax, rt.rect.yMax, 0f)));
            if (a.x < minX) minX = a.x;
            if (a.y < minY) minY = a.y;
            if (b.x > maxX) maxX = b.x;
            if (b.y > maxY) maxY = b.y;
        }
        Vector2 origin = new Vector2(0f, 0f);
        if (minX <= maxX && minY <= maxY) origin = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        for (int i = 0; i < gs.Length; i++) {
            var g = gs[i];
            if (g == null) continue;
            var e = g.gameObject.GetComponent<ArkPerspective>();
            if (e == null) e = g.gameObject.AddComponent<ArkPerspective>();
            e.Layer = layer;
            e.Dist = ArkCamZ;
            e.Origin = origin;
            e.SetTilt(yaw, 0f);
            list.Add(e);
        }
        return list.ToArray();
    }

    // ===================== 斜切板的创建入口 =====================

    /// <summary>建一块斜切板。入参是示意图的 CSS px（left/top 是距左上角，cut 是斜边让出的量），
    /// 换算成项目画布单位这一步统一在本层做 —— 调用点一律保持示意图原值，好逐条核对那份源码。</summary>
    private static SkewedImage MakeSkewed(Transform parent, string name, float left, float top,
                                           float w, float h, Color col, CornerMode mode, float cut) {
        var go = new GameObject(name, typeof(RectTransform), typeof(SkewedImage));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<SkewedImage>();
        img.color = col;
        img.raycastTarget = false;
        img.Mode = mode;
        img.Cut = cut * ArkPx;
        SetAtPx(img.rectTransform, left * ArkPx, top * ArkPx, w * ArkPx, h * ArkPx);
        return img;
    }


    // ===================== 圆角板（半透明底板） =====================

    private static Sprite _arkRoundSprite;
    /// <summary>九宫格圆角板用的圆角贴图（半径 10，与项目里那套一致）。
    /// 9-slice 的 border 等于半径，所以贴图边长必须大于两倍半径，否则中间的拉伸段被吃光。</summary>
    private static Sprite ArkRoundSprite {
        get {
            if (_arkRoundSprite == null) _arkRoundSprite = CreateRoundedSprite(24, 10f);
            return _arkRoundSprite;
        }
    }


    /// <summary>一块直角矩形底板。模板的 BaseButton 带 overflow-hidden 但没有 rounded，
    /// 所以它做出来的按钮是直角矩形 —— 小按钮 / 采购中心 / 招募栏这一组都走这里。
    /// 贴图用软边版（SoftPlateSprite）：透视倾斜下几何硬边会有锯齿，软边把抗锯齿交给纹理采样；
    /// 几何按软边量向外扩，50% 等值线落回名义矩形、视觉尺寸不变。</summary>
    private static Image MakeArkRect(Transform parent, string name, float left, float top,
                                     float w, float h, Color col) {
        var img = MakeImage(parent, name, col);
        img.sprite = SoftPlateSprite(0f);
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        float pad = SoftEdgePx / ArkPx;      // 画布单位 → CSS
        SetAtPx(img.rectTransform, (left - pad) * ArkPx, (top - pad) * ArkPx,
                (w + pad * 2f) * ArkPx, (h + pad * 2f) * ArkPx);
        return img;
    }

    /// <summary>把一块已经摆好位的图元换成软边板贴图并向外扩边（50% 等值线回到原位）。
    /// 给那些自己摆位、不走 MakeArkRect 的板子用（理智盒内块 / 招募子钮 / 当前胶囊 / 标签条）。
    /// 前提：rect 是"单点锚父级左上角"（SetAtPx / ArkAtPx 摆的），向外扩 = 左上角往左上各让 SoftEdgePx。</summary>
    private static void SoftifyArkPlate(Image img, float radius) {
        if (img == null) return;
        img.sprite = SoftPlateSprite(radius);
        img.type = Image.Type.Sliced;
        var rt = img.rectTransform;
        var sd = rt.sizeDelta;
        rt.sizeDelta = new Vector2(sd.x + SoftEdgePx * 2f, sd.y + SoftEdgePx * 2f);
        var ap = rt.anchoredPosition;
        ap.x -= SoftEdgePx;
        ap.y += SoftEdgePx;
        rt.anchoredPosition = ap;
    }

    // ===================== 图标（程序化，不引外部文件） =====================

    /// <summary>图标贴图按 (kind, size) 缓存：同一尺寸下多处复用同一张，不重复烘。
    /// 入参 cx/cy 是图标中心的 CSS px，size 是 CSS px 边长。</summary>
    private static Image MakeArkIcon(Transform parent, string name, float cx, float cy, float size, int kind) {
        float s = size * ArkPx;
        var tex = ArkIconTexture(kind, s);
        var img = MakeImage(parent, name, Color.white);
        img.sprite = SpriteFromTexture(tex);
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        img.preserveAspect = true;
        SetAtPx(img.rectTransform, (cx - size * 0.5f) * ArkPx, (cy - size * 0.5f) * ArkPx, s, s);
        return img;
    }

    private static System.Collections.Generic.Dictionary<int, Texture2D> _arkIconTex;

    /// <summary>图标贴图超采样倍数：按 4 倍分辨率烘、显示时由 GPU 缩小采样。
    /// 早先是 1:1 烘，实机上画布缩放（屏幕比 1920 宽）与透视放大会把贴图拉大 → 边缘变成阶梯（锯齿）。
    /// 4 倍超采样后缩小采样等效 4x4 盒式滤波，任何缩放下都平滑；配合 ArkPaint 的覆盖率软边更稳。</summary>
    private const int ArkIconSs = 4;

    /// <summary>图标贴图按 (kind, size) 缓存：同一尺寸下多处复用同一张，不重复烘。
    /// 注意缓存键用的是"1 倍尺寸"，超采样只发生在烘制这一步（BakeArkIcon 拿 3 倍尺寸画）。</summary>
    private static Texture2D ArkIconTexture(int kind, float size) {
        int s = Mathf.Max(16, Mathf.RoundToInt(size));
        if (_arkIconTex == null) _arkIconTex = new System.Collections.Generic.Dictionary<int, Texture2D>();
        int key = kind * 10000 + s;
        Texture2D got;
        if (_arkIconTex.TryGetValue(key, out got) && got != null) return got;
        var tex = BakeArkIcon(kind, s * ArkIconSs);
        _arkIconTex[key] = tex;
        return tex;
    }

    /// <summary>把一个图标烘成贴图。线宽按示意图的 strokeWidth 等比缩放：
    /// 例如 settings.svg 视图 64、描边 4，画成 64px 就是 4px 线。
    /// 开 mipmap：3 倍超采样的贴图在 1 倍显示时靠 mip 链做盒式滤波（只有 bilinear 的话
    /// 采样只取邻近 4 个纹素、等于没缩），边缘才真正平滑。</summary>
    private static Texture2D BakeArkIcon(int kind, int s) {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[s * s];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
        // 白色主色 + 主题色点缀：示意图里图标是白色描边、个别地方带主题色
        var white = new Color32(255, 255, 255, 255);
        var theme = ThemeColorBytes();

        switch (kind) {
            case ArkIconSettings: ArkDrawSettings(px, s, white); break;
    case ArkIconMail:ArkDrawMail(px, s, white); break;
    case ArkIconCalendar: ArkDrawCalendar(px, s, white); break;
      case ArkIconPlus: ArkDrawPlus(px, s, white); break;
            case ArkIconBattery: ArkDrawBattery(px, s, white); break;
    case ArkIconNotice: ArkDrawNotice(px, s, white); break;
            case ArkIconHide: ArkDrawHide(px, s, white); break;
            case ArkIconChange: ArkDrawChange(px, s, white); break;
            case ArkIconLevelRing: ArkDrawLevelRing(px, s, white); break;
            case ArkIconMoney: ArkDrawMoney(px, s, white); break;
            case ArkIconJade: ArkDrawJade(px, s, white); break;
            case ArkIconOriginium: ArkDrawOriginium(px, s, white); break;
            case ArkIconCircle: ArkDrawCircle(px, s, white); break;
            case ArkIconLevelRingFill: ArkDrawLevelRingFill(px, s, white); break;
            case ArkIconList: ArkDrawList(px, s, white); break;
            case ArkIconCheck: ArkDrawCheck(px, s, white); break;
            case ArkIconStar: ArkDrawStar(px, s, white); break;
            case ArkIconCode: ArkDrawCode(px, s, white); break;
            case ArkIconPortrait: ArkDrawPortrait(px, s, white); break;
            default: ArkDrawPlus(px, s, white); break;
        }
        tex.SetPixels32(px);
        tex.Apply(true, false);   // 生成 mip 链（缩小显示时平滑靠它）
        return tex;
    }

    private static Color32 ThemeColorBytes() {
        var c = ThemeColor;
        return new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255f),
    (byte)(Mathf.Clamp01(c.g) * 255f),
      (byte)(Mathf.Clamp01(c.b) * 255f), 255);
    }

    // --- 图标各画法：都是"线段 + 圆头"，线宽取 s/16左右，与示意图的 strokeWidth 同量级 ---
    private static void ArkDrawSettings(Color32[] px, int s, Color32 col) {
        // 设置：三个横条 + 左侧三颗点（示意图的 settings.svg 是三条带滑块的线）
        int lw = Mathf.Max(2, s / 16);
        for (int i = 0; i < 3; i++) {
            int y = s * (28 + i * 22) / 100;
            ArkLine(px, s, s * 12, s * 88, y, y, lw, col);
       int kx = s * (34 + i * 14) / 100;
     ArkDisc(px, s, kx, y, lw * 2, col);
        }
    }

    private static void ArkDrawMail(Color32[] px, int s, Color32 col) {
        int lw = Mathf.Max(2, s / 16);
        int x0 = s * 14, x1 = s * 86, y0 = s * 28, y1 = s * 72;
      ArkLine(px, s, x0, y0, x1, y0, lw, col);
 ArkLine(px, s, x0, y0, x0, y1, lw, col);
        ArkLine(px, s, x1, y0, x1, y1, lw, col);
        ArkLine(px, s, x0, y1, x1, y1, lw, col);
   ArkLine(px, s, x0, y0, s * 50, s * 52, lw, col);   // 信封盖
        ArkLine(px, s, x1, y0, s * 50, s * 52, lw, col);
    }

    private static void ArkDrawCalendar(Color32[] px, int s, Color32 col) {
        int lw = Mathf.Max(2, s / 16);
int x0 = s * 16, x1 = s * 84, y0 = s * 24, y1 = s * 84;
        ArkLine(px, s, x0, y0, x1, y0, lw, col);
        ArkLine(px, s, x0, y0, x0, y1, lw, col);
 ArkLine(px, s, x1, y0, x1, y1, lw, col);
        ArkLine(px, s, x0, y1, x1, y1, lw, col);
 ArkLine(px, s, x0, s * 44, x1, s * 44, lw, col);   // 中间那道分隔
   ArkLine(px, s, s * 32, s * 12, s * 32, s * 26, lw, col);   // 上头两个挂环
        ArkLine(px, s, s * 68, s * 12, s * 68, s * 26, lw, col);
    }

    private static void ArkDrawNotice(Color32[] px, int s, Color32 col) {
      // 通知：一个圆 + 里面三条线（示意图 notice.svg）
        int lw = Mathf.Max(2, s / 16);
        int cx = s / 2, cy = s / 2, r = s * 36 / 100;
        ArkRing(px, s, cx, cy, r, lw, col);
        for (int i = 0; i < 3; i++) {
   int y = cy + s * 10 - i * s * 12;
       ArkLine(px, s, cx - s * 18, y, cx + s * 18, y, lw, col);
        }
    }

    private static void ArkDrawPlus(Color32[] px, int s, Color32 col) {
        int lw = Mathf.Max(3, s / 12);
        int cx = s / 2, cy = s / 2, r = s * 26 / 100;
        ArkLine(px, s, cx - r, cy, cx + r, cy, lw, col);
        ArkLine(px, s, cx, cy - r, cx, cy + r, lw, col);
    }

    private static void ArkDrawBattery(Color32[] px, int s, Color32 col) {
        // 电池：外框 + 右侧小凸起 + 三格电量（示意图的 BatteryIcon，viewBox 1024）
        int lw = Mathf.Max(2, s / 12);
  int x0 = s * 13, x1 = s * 88, y0 = s * 37, y1 = s * 77;
        ArkLine(px, s, x0, y0, x1, y0, lw, col);
  ArkLine(px, s, x0, y1, x1, y1, lw, col);
        ArkLine(px, s, x0, y0, x0, y1, lw, col);
        ArkLine(px, s, x1, y0, x1, y1, lw, col);
        ArkLine(px, s, s * 88, s * 44, s * 97, s * 44, lw, col);   // 右侧凸起
ArkLine(px, s, s * 88, s * 70, s * 97, s * 70, lw, col);
    ArkLine(px, s, s * 97, s * 44, s * 97, s * 70, lw, col);
        for (int i = 0; i < 3; i++) {   // 三格电量（示意图里按电量高低显示，这里全亮）
       int bx = s * (22 + i * 20);
       ArkLine(px, s, bx, s * 45, bx, s * 69, lw, col);
       ArkLine(px, s, bx + s * 12, s * 45, bx + s * 12, s * 69, lw, col);
        }
    }

    // --- 画图元：线段（圆头）、圆盘、圆环 ---
    /// <summary>按覆盖率把一个像素混到画布上（约 2 像素宽的软边）。
    /// 原先图元是"距离达标就整像素写死"，边缘是硬阶梯；改成按覆盖率做 alpha 合成后，
    /// 贴图自带软边，缩小/放大采样都不会出锯齿。同色图形叠加（环上线、线交点）也正确。
    /// 软边宽度取 2 个绘制像素（半宽 1）：烘制空间是 1 倍尺寸的数倍（ArkIconSs），
    /// 过窄的软边（0.5）在缩小采样稍有偏差时就抓不住，会退回锯齿。</summary>
    private static void ArkPaint(Color32[] px, int idx, Color32 col, float cov) {
        float a = (col.a / 255f) * Mathf.Clamp01(cov);
        if (a <= 0.002f) return;
        if (a >= 0.998f) { px[idx] = col; return; }
        Color32 d = px[idx];
        float da = d.a / 255f;
        float oa = a + da * (1f - a);
        float r = (col.r * a + d.r * da * (1f - a)) / oa;
        float g = (col.g * a + d.g * da * (1f - a)) / oa;
        float b = (col.b * a + d.b * da * (1f - a)) / oa;
        px[idx] = new Color32(
            (byte)Mathf.Clamp(r + 0.5f, 0f, 255f),
            (byte)Mathf.Clamp(g + 0.5f, 0f, 255f),
            (byte)Mathf.Clamp(b + 0.5f, 0f, 255f),
            (byte)Mathf.Clamp(oa * 255f + 0.5f, 0f, 255f));
    }

    private static void ArkLine(Color32[] px, int s, float x0, float y0, float x1, float y1,
                                float w, Color32 col) {
        float hw = Mathf.Max(0.5f, w * 0.5f);
        float minX = Mathf.Floor(Mathf.Min(x0, x1) - hw - 1f), maxX = Mathf.Ceil(Mathf.Max(x0, x1) + hw + 1f);
        float minY = Mathf.Floor(Mathf.Min(y0, y1) - hw - 1f), maxY = Mathf.Ceil(Mathf.Max(y0, y1) + hw + 1f);
        float dx = x1 - x0, dy = y1 - y0;
        float len2 = dx * dx + dy * dy;
        if (len2 < 0.0001f) { ArkDisc(px, s, x0, y0, w, col); return; }
        for (int y = (int)minY; y <= (int)maxY; y++) {
            if (y < 0 || y >= s) continue;
            for (int x = (int)minX; x <= (int)maxX; x++) {
                if (x < 0 || x >= s) continue;
                float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / len2);
                float px_ = x0 + dx * t, py = y0 + dy * t;
                float ddx = x - px_, ddy = y - py;
                float dist = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                float cov = hw + 1f - dist;      // 2 像素宽软边（半宽 1）
                if (cov > 0f) ArkPaint(px, y * s + x, col, cov);
            }
        }
    }

    private static void ArkDisc(Color32[] px, int s, float cx, float cy, float r, Color32 col) {
        int ir = Mathf.CeilToInt(r) + 1;
        for (int y = (int)(cy - ir); y <= (int)(cy + ir); y++) {
            if (y < 0 || y >= s) continue;
            for (int x = (int)(cx - ir); x <= (int)(cx + ir); x++) {
                if (x < 0 || x >= s) continue;
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float cov = r + 1f - d;      // 2 像素宽软边
                if (cov > 0f) ArkPaint(px, y * s + x, col, cov);
            }
        }
    }

    private static void ArkRing(Color32[] px, int s, float cx, float cy, float r, float w, Color32 col) {
        float ro = r, ri = Mathf.Max(0f, r - w);
        int ir = Mathf.CeilToInt(r) + 1;
        for (int y = (int)(cy - ir); y <= (int)(cy + ir); y++) {
            if (y < 0 || y >= s) continue;
            for (int x = (int)(cx - ir); x <= (int)(cx + ir); x++) {
                if (x < 0 || x >= s) continue;
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float cov = Mathf.Min(ro + 1f - d, d - ri + 1f);   // 外缘与内缘各 2 像素宽软边
                if (cov > 0f) ArkPaint(px, y * s + x, col, cov);
            }
        }
    }


    private static void ArkDrawHide(Color32[] px, int s, Color32 col) {
        int cx = s / 2, cy = s / 2, r = s * 42 / 100;
        int lw = Mathf.Max(2, s / 20);
        ArkDisc(px, s, cx, cy, r, new Color32(0x30, 0x30, 0x30, 255));
        ArkRing(px, s, cx, cy, r, lw, new Color32(0x99, 0x99, 0x99, 255));
        ArkLine(px, s, cx - r * 0.6f, cy, cx + r * 0.6f, cy, lw, col);
        ArkLine(px, s, cx + r * 0.5f, cy + r * 0.5f, cx - r * 0.5f, cy - r * 0.5f, lw * 2, col);
    }

    private static void ArkDrawChange(Color32[] px, int s, Color32 col) {
        int cx = s / 2, cy = s / 2, r = s * 42 / 100;
        int lw = Mathf.Max(2, s / 20);
        ArkDisc(px, s, cx, cy, r, new Color32(0x30, 0x30, 0x30, 255));
        ArkRing(px, s, cx, cy, r, lw, new Color32(0x99, 0x99, 0x99, 255));
        ArkRing(px, s, cx, cy, r / 2f, lw, col);
        ArkLine(px, s, cx + r * 0.55f, cy - r * 0.55f, cx - r * 0.55f, cy + r * 0.55f, lw * 2, col);
    }

    private static void ArkDrawLevelRing(Color32[] px, int s, Color32 col) {
        int cx = s / 2, cy = s / 2, r = s * 44 / 100;
        // 描边向内加粗（外缘半径不动，线宽 s/10，ArkRing 的内径跟着往里收；用户两轮加粗后定在这个值）
        ArkRing(px, s, cx, cy, r, Mathf.Max(2, s / 10), new Color32(255, 255, 255, 80));
    }

    private static void ArkDrawCircle(Color32[] px, int s, Color32 col) {
        // 一个圆（细描边空心圆）：功能映射后资源行三格的底，字母由调用方用 Text 叠在圆心里。
        int lw = Mathf.Max(2, s / 16);
        ArkRing(px, s, s / 2, s / 2, s * 38 / 100, lw, col);
    }

    private static void ArkDrawList(Color32[] px, int s, Color32 col) {
        // 列表：三行"小点 + 横线"（谱面库按钮用，与设置图标的三条线区分开、偏"目录"感）
        int lw = Mathf.Max(2, s / 16);
        for (int i = 0; i < 3; i++) {
            float y = s * (28 + i * 22) / 100f;
            ArkDisc(px, s, s * 20 / 100f, y, lw, col);
            ArkLine(px, s, s * 34 / 100f, y, s * 84 / 100f, y, lw, col);
        }
    }

    private static void ArkDrawCheck(Color32[] px, int s, Color32 col) {
        // 圆环里一个勾（官谱库按钮用，认证感）
        int lw = Mathf.Max(2, s / 16);
        ArkRing(px, s, s / 2f, s / 2f, s * 37 / 100f, lw, col);
        ArkLine(px, s, s * 34 / 100f, s * 51 / 100f, s * 45 / 100f, s * 63 / 100f, lw, col);
        ArkLine(px, s, s * 45 / 100f, s * 63 / 100f, s * 68 / 100f, s * 37 / 100f, lw, col);
    }

    private static void ArkDrawStar(Color32[] px, int s, Color32 col) {
        // 五角星轮廓（成就按钮用）：外顶 5 个、内顶 5 个，线段依次连成轮廓
        int lw = Mathf.Max(2, s / 16);
        float cx = s * 0.5f, cy = s * 0.53f;
        float ro = s * 0.40f, ri = ro * 0.382f;
        float lx = 0f, ly = 0f;
        for (int i = 0; i <= 10; i++) {
            float ang = (-90f + i * 36f) * Mathf.Deg2Rad;
            float rr = (i % 2 == 0) ? ro : ri;
            float x = cx + Mathf.Cos(ang) * rr;
            float y = cy + Mathf.Sin(ang) * rr;
            if (i > 0) ArkLine(px, s, lx, ly, x, y, lw, col);
            lx = x; ly = y;
        }
    }

    private static void ArkDrawCode(Color32[] px, int s, Color32 col) {
        // 尖括号加斜杠（开发者按钮用）：左 "<"、右 ">"、中间一根斜杠
        int lw = Mathf.Max(2, s / 16);
        ArkLine(px, s, s * 36 / 100f, s * 22 / 100f, s * 20 / 100f, s * 50 / 100f, lw, col);
        ArkLine(px, s, s * 20 / 100f, s * 50 / 100f, s * 36 / 100f, s * 78 / 100f, lw, col);
        ArkLine(px, s, s * 56 / 100f, s * 76 / 100f, s * 44 / 100f, s * 24 / 100f, lw, col);
        ArkLine(px, s, s * 64 / 100f, s * 22 / 100f, s * 80 / 100f, s * 50 / 100f, lw, col);
        ArkLine(px, s, s * 80 / 100f, s * 50 / 100f, s * 64 / 100f, s * 78 / 100f, lw, col);
    }

    /// <summary>立绘按钮：圆里一个长方形（圆钮本身已经是圆盘，这里只画那个框）。
    /// 四条边按线段画，与其它图标同一套"线段 + 圆头"软边待遇（描边是几何硬边会出锯齿）。</summary>
    private static void ArkDrawPortrait(Color32[] px, int s, Color32 col) {
        int lw = Mathf.Max(2, s / 16);
        float x0 = s * 28 / 100f, x1 = s * 72 / 100f;
        float y0 = s * 24 / 100f, y1 = s * 76 / 100f;
        ArkLine(px, s, x0, y0, x1, y0, lw, col);
        ArkLine(px, s, x1, y0, x1, y1, lw, col);
        ArkLine(px, s, x1, y1, x0, y1, lw, col);
        ArkLine(px, s, x0, y1, x0, y0, lw, col);
    }

    private static void ArkDrawLevelRingFill(Color32[] px, int s, Color32 col) {
        // 等级环的"亮进度"层：与 ArkDrawLevelRing 同几何，只是不透明。
        // 注意：完成弧现在实际走 ArkRingArcSprite 的"整弧贴图"路线，这里只作为预留画法保留。
        int cx = s / 2, cy = s / 2, r = s * 44 / 100;
        ArkRing(px, s, cx, cy, r, Mathf.Max(2, s / 10), col);
    }

    // ---- 进度弧贴图（替代 Image.Filled 的完成弧）----
    private static Texture2D _arkArcTex;
    private static Sprite _arkArcSprite;
    private static int _arkArcPct = -1;

    /// <summary>等级环的"完成进度"弧贴图：与淡底环同几何，只画 [0, fill] 扇区、两端各留 ~2 度软边。
    /// 为什么不用 Image.Filled：Filled 的扇区两端是几何硬边（光栅化无 MSAA），透视倾斜下会出锯齿；
    /// 烘成贴图后整条弧（含两端）都走纹理采样，与其它图标同一套软边待遇。
    /// 按 1% 步进缓存、只保留最近一张（进度变化慢；换新时销毁旧纹理，不攒显存）。
    /// 方向与 uGUI Filled(Origin360.Bottom, clockwise) 一致：贴图行序 y=0 在显示下缘，
    /// 所以从正下方（-90 度）起、顺时针（角度递减）扫过 左侧 → 上方 → 右侧。</summary>
    private static Sprite ArkRingArcSprite(float fill) {
        int pct = Mathf.Clamp(Mathf.RoundToInt(fill * 100f), 0, 100);
        if (pct == _arkArcPct && _arkArcSprite != null) return _arkArcSprite;
        try {
            int s = Mathf.Max(64, Mathf.RoundToInt(ArkRingSize * ArkPx)) * ArkIconSs;   // 与图标同口径超采样
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            PerfDiag.Tag(tex, "arc" + s);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[s * s];
            if (pct > 0) {
                float cx = s * 0.5f, cy = s * 0.5f;
                float r = s * 44f / 100f;
                float w = Mathf.Max(2f, s / 10f);
                float ro = r, ri = Mathf.Max(0f, r - w);
                float sweep = pct * 3.6f;     // 已填充角度（度）
                const float fade = 2f;        // 两端软边（度）
                int ir = Mathf.CeilToInt(r) + 2;
                var white = new Color32(255, 255, 255, 255);
                for (int y = 0; y < s; y++) {
                    for (int x = 0; x < s; x++) {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;   // 行序 y=0 = 显示下缘，即 y 向上
                        if (Mathf.Abs(dx) > ir || Mathf.Abs(dy) > ir) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float cov = Mathf.Min(ro + 1f - d, d - ri + 1f);
                        if (cov <= 0f) continue;
                        float th = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        float dt = Mathf.Repeat(-90f - th, 360f);   // 距起点的顺时针角距
                        if (dt >= sweep) continue;
                        float ac = 1f;
                        if (dt < fade) ac = dt / fade;
                        if (sweep - dt < fade) ac = Mathf.Min(ac, (sweep - dt) / fade);
                        if (ac > 0f) ArkPaint(px, y * s + x, white, cov * ac);
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            var old = _arkArcTex;
            var sp = PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect), "arc" + s);
            _arkArcTex = tex;
            _arkArcSprite = sp;
            _arkArcPct = pct;
            if (old != null) Object.Destroy(old);   // 新贴图就位后才销毁旧的
            return sp;
        } catch (System.Exception e) {
            Lg.Ex("生成进度弧贴图失败（这一帧不画完成弧）", e);
            return null;
        }
    }

    private static void ArkDrawMoney(Color32[] px, int s, Color32 col) {
        int cx = s / 2, cy = s / 2, r = s * 38 / 100;
        int lw = Mathf.Max(2, s / 16);
        ArkRing(px, s, cx, cy, r, lw, col);
        int q = s * 14 / 100;
        ArkDisc(px, s, cx - q, cy - q, lw, col);
        ArkDisc(px, s, cx + q, cy - q, lw, col);
ArkDisc(px, s, cx - q, cy + q, lw, col);
        ArkDisc(px, s, cx + q, cy + q, lw, col);
    }

    private static void ArkDrawJade(Color32[] px, int s, Color32 col) {
        int pad = s * 18 / 100;
        int rr = s * 12 / 100;
        for (int y = pad; y < s - pad; y++)
            for (int x = pad; x < s - pad; x++) {
       int dx = Mathf.Max(pad + rr - x, 0, x - (s - pad - rr - 1));
   int dy = Mathf.Max(pad + rr - y, 0, y - (s - pad - rr - 1));
          if (dx * dx + dy * dy <= rr * rr) px[y * s + x] = col;
            }
    }

    private static void ArkDrawOriginium(Color32[] px, int s, Color32 col) {
        int cx = s / 2, cy = s / 2, r = s * 40 / 100;
        for (int y = 0; y < s; y++)
          for (int x = 0; x < s; x++) {
    int dx = Mathf.Abs(x - cx), dy = Mathf.Abs(y - cy);
   if (dx + dy <= r) px[y * s + x] = col;
          }
    }

    private static System.Collections.Generic.Dictionary<Texture2D, Sprite> _arkSprites;
    /// <summary>贴图转 Sprite（带 pivot 居中）。同一张贴图复用同一个 Sprite。</summary>
    private static Sprite SpriteFromTexture(Texture2D tex) {
        if (_arkSprites == null) _arkSprites = new System.Collections.Generic.Dictionary<Texture2D, Sprite>();
        Sprite got;
        if (_arkSprites.TryGetValue(tex, out got) && got != null) return got;
        var sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
      new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _arkSprites[tex] = sp;
        return sp;
    }

    // 图标 kind 常量（避免到处写数字）
    private const int ArkIconSettings = 1;
    private const int ArkIconNotice = 2;
    private const int ArkIconMail = 3;
    private const int ArkIconCalendar = 4;
    private const int ArkIconPlus = 5;
    private const int ArkIconBattery = 6;
    private const int ArkIconHide = 7;
    private const int ArkIconChange = 8;
    private const int ArkIconLevelRing = 9;
    private const int ArkIconMoney = 10;
    private const int ArkIconJade = 11;
    private const int ArkIconOriginium = 12;
    private const int ArkIconCircle = 13;   // 空心圆（P/G/U 三格的底）
    private const int ArkIconLevelRingFill = 14;   // 等级环的亮进度层（全不透明描边）
    private const int ArkIconList = 15;    // 谱面库按钮：三行列表
    private const int ArkIconCheck = 16;   // 官谱库按钮：圆环里一个勾
    private const int ArkIconStar = 17;    // 成就按钮：五角星轮廓
    private const int ArkIconCode = 18;    // 开发者按钮：尖括号加斜杠（代码）
    private const int ArkIconPortrait = 19; // 立绘按钮：一个长方形（圆钮本身是圆的，图标只画框）
}
