// 默认头像的流体球：照搬 swamimalode67/rare-ui 的 fluid-orb（registry 的 components/ui/fluid-orb.tsx）。
// 原版结构很简：一块画布 + 一个铺满的方片 + 一个每帧用 u_time 求值的片元着色器 —— 没有多帧、没有两层、
// 没有交换、没有缓存。这里按同样的结构移植：唯一一张常驻贴图、唯一一个 Sprite、每帧按同一套数学重算并上传，
// 头像图元自己就是那颗球（贴图的 alpha 已经做了圆形边缘），因此没有第二层、也没有"某层拿不到贴图"这回事。
// 同一套数学还有第二处使用者：开发者页那层铺满整页的背景（见 PortalUI.DevFluidBg.cs，差别只有圆边与否）。
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {
    private const int FluidSize = 128;         // 贴图边长（原版按 size*dpr 超采样；我们显示 74~109，128 够）
    private const int FluidGridN = 25;         // fbm 求值的粗网格边长
    private const float FluidTimeK = 0.22f;    // 原版：t = u_time * 0.22

    /// <summary>流体贴图的重算+上传节流：最快每 1/FluidFps 秒做一次。
    /// 这一族每帧要跑 size² 次像素循环（头像 128²=16384、开发者页 160²=25600）再 SetPixels32+Apply
    /// （每次往 GPU 传 64KB / 100KB）。而这个场本来就极平滑（fbm 在 25² 粗网格上求值后双线性插值），
    /// 60fps 与 30fps 肉眼几乎看不出差别，开销却直接减半。
    /// 时钟照旧每帧累加真实 dt（不是按帧数 +1/30），所以动画速度不变，只是丢掉中间帧 —— 不会越跑越慢。</summary>
    private const float FluidFps = 30f;
    /// <summary>攒到该重算的时长（秒）。攒够就减去一个周期、继续攒，不跟 Time.unscaledTime 比差值：
    /// 那个差值是 float 逐帧相减，精度损耗会让标称 30fps 实际只跑到 24 左右</summary>
    private static float _fluidAcc;
    private static float _devFluidAcc;          // 开发者页背景的（两者独立节流，互不影响）
    private static readonly Color FluidColor = new Color(0x1A / 255f, 0x73 / 255f, 0xF2 / 255f, 1f);  // 原版默认 #1A73F2

    private static Texture2D _fluidTex;
    private static Sprite _fluidSprite;
    private static readonly Color32[] _fluidPx = new Color32[FluidSize * FluidSize];
    private static readonly float[] _fluidF = new float[FluidGridN * FluidGridN];
    private static float _fluidT;              // 播放时钟（秒，累加，单帧钳住）
    private static readonly System.Collections.Generic.List<Image> _fluidOwners = new System.Collections.Generic.List<Image>(4);

    /// <summary>那颗球的贴图（全局唯一一张）。拿不到就返回 null，调用方退回淡灰圆。</summary>
    private static Sprite FluidOrbSprite() {
        if (_fluidSprite != null) return _fluidSprite;
        try {
            _fluidTex = new Texture2D(FluidSize, FluidSize, TextureFormat.RGBA32, false);
            _fluidTex.filterMode = FilterMode.Bilinear;
            _fluidTex.wrapMode = TextureWrapMode.Clamp;
            BuildFluidPixels(_fluidPx, FluidSize, true, _fluidT * FluidTimeK);
            _fluidTex.SetPixels32(_fluidPx);
            _fluidTex.Apply(false, false);
            _fluidSprite = Sprite.Create(_fluidTex, new Rect(0, 0, FluidSize, FluidSize), new Vector2(0.5f, 0.5f),
                                         100f, 0, SpriteMeshType.FullRect, Vector4.zero);
            return _fluidSprite;
        } catch (System.Exception e) {
            _fluidSprite = null;
            _fluidTex = null;
            try { Lg.Ex("默认头像动效创建失败（退回淡灰圆）", e); } catch { }
            return null;
        }
    }

    /// <summary>登记一个"正在显示这颗球"的图元（每帧只更新贴图，不碰任何图元属性）。</summary>
    private static void RegisterFluidOwner(Image img) {
        if (img == null) return;
        for (int i = 0; i < _fluidOwners.Count; i++) if (_fluidOwners[i] == img) return;
        _fluidOwners.Add(img);
    }

    /// <summary>常驻段每帧调：有人正显示着它才重算。没有显示者时第一行返回（零开销）。</summary>
    private static void TickFluidOrb() {
        bool any = false;
        for (int i = _fluidOwners.Count - 1; i >= 0; i--) {
            var im = _fluidOwners[i];
            if (im == null) { _fluidOwners.RemoveAt(i); continue; }
            if (im.gameObject != null && im.gameObject.activeInHierarchy) any = true;
        }
        if (!any || _fluidTex == null || _fluidSprite == null) return;
        float dt = Time.unscaledDeltaTime;
        if (dt > FluidMaxStep) dt = FluidMaxStep;
        if (dt < 0f) dt = 0f;
        _fluidT += dt;
        // 节流到 FluidFps：时钟照常每帧累加（速度不变），只是没到点就不重算、不上传。
        // 判据用"攒够多少秒"而不是跟 Time.unscaledTime 比差值：那个差值是 float，
        // 帧久了会掉到实际帧率的三分之二（60fps 标称 30 却只跑 24 左右），
        // 攒时间再比是同一份精度、只累一次，没有逐帧相减的误差
        _fluidAcc += dt;
        if (_fluidAcc < 1f / FluidFps) return;
        _fluidAcc -= 1f / FluidFps;   // 减去阈值而不是清零：留住余量，帧率与标称不一致时也能对上（比如 144fps 上正好每 5 帧一次）
        BuildFluidPixels(_fluidPx, FluidSize, true, _fluidT * FluidTimeK);
        _fluidTex.SetPixels32(_fluidPx);
        _fluidTex.Apply(false, false);
    }

    private const float FluidMaxStep = 0.05f;   // 卡顿帧不许把动画一步冲过去

    /// <summary>把一个像素算成颜色：逐行照抄原版片元着色器（uv 以左下为原点，与 gl_FragCoord 一致）。
    /// 参数化是因为还有第二处使用者（开发者页那层铺满整页的背景，见 PortalUI.DevFluidBg.cs）：
    /// 同一套数学、同一张粗网格，差别只有贴图边长与"要不要圆边"。dst 的边长必须等于 size。</summary>
    private static void BuildFluidPixels(Color32[] dst, int size, bool circleEdge, float t) {
        // vec2 drift = vec2(sin(t) + 0.6*sin(t*1.7+1.3), cos(t*0.8) + 0.6*cos(t*1.3+2.1));
        float dx = Mathf.Sin(t) + 0.6f * Mathf.Sin(t * 1.7f + 1.3f);
        float dy = Mathf.Cos(t * 0.8f) + 0.6f * Mathf.Cos(t * 1.3f + 2.1f);

        // 粗网格上求 fbm 那几个场：它们都只随 uv 平滑变化，插值到整幅贴图看不出差别。
        for (int gy = 0; gy < FluidGridN; gy++) {
            float vy = (gy + 0.5f) / FluidGridN;
            for (int gx = 0; gx < FluidGridN; gx++) {
                float vx = (gx + 0.5f) / FluidGridN;
                float px = vx * 1.8f + dx * 0.7f;
                float py = vy * 1.0f + dy * 0.7f;
                // vec2 q = vec2(fbm(p + drift), fbm(p + vec2(3.2,1.5) - drift));
                float qx = Fbm(px + dx, py + dy);
                float qy = Fbm(px + 3.2f - dx, py + 1.5f - dy);
                // float f = fbm(p + 1.2 * q);
                _fluidF[gy * FluidGridN + gx] = Fbm(px + 1.2f * qx, py + 1.2f * qy);
            }
        }

        var white = new Vector3(0.99f, 1f, 1f);
        var dark = new Vector3(FluidColor.r, FluidColor.g, FluidColor.b);
        var light = new Vector3(white.x + (dark.x - white.x) * 0.5f,
                                white.y + (dark.y - white.y) * 0.5f,
                                white.z + (dark.z - white.z) * 0.5f);   // mix(white, u_color, 0.5)

        for (int y = 0; y < size; y++) {
            float uvy = (y + 0.5f) / size;
            // 粗网格插值（双线性）
            float fy = uvy * FluidGridN - 0.5f;
            int y0 = Mathf.FloorToInt(fy);
            float ty = fy - y0;
            if (y0 < 0) { y0 = 0; ty = 0f; } else if (y0 >= FluidGridN - 1) { y0 = FluidGridN - 2; ty = 1f; }
            float g = Mathf.Clamp01(1f - uvy);                       // clamp(1 - uv.y, 0, 1)
            float anchor = Smooth(0f, 0.3f, uvy);                    // smoothstep(0, 0.3, uv.y)
            int row = y * size;
            for (int x = 0; x < size; x++) {
                float uvx = (x + 0.5f) / size;
                float fx = uvx * FluidGridN - 0.5f;
                int x0 = Mathf.FloorToInt(fx);
                float tx = fx - x0;
                if (x0 < 0) { x0 = 0; tx = 0f; } else if (x0 >= FluidGridN - 1) { x0 = FluidGridN - 2; tx = 1f; }
                float f00 = _fluidF[y0 * FluidGridN + x0];
                float f10 = _fluidF[y0 * FluidGridN + x0 + 1];
                float f01 = _fluidF[(y0 + 1) * FluidGridN + x0];
                float f11 = _fluidF[(y0 + 1) * FluidGridN + x0 + 1];
                float f = Mathf.Lerp(Mathf.Lerp(f00, f10, tx), Mathf.Lerp(f01, f11, tx), ty);

                // float shade = clamp(g + (f - 0.5) * 0.8 * anchor, 0, 1);
                float shade = Mathf.Clamp01(g + (f - 0.5f) * 0.8f * anchor);
                // col = white; col = mix(col, light, smoothstep(0.28,0.52,shade)); col = mix(col, dark, smoothstep(0.58,0.88,shade));
                float k1 = Smooth(0.28f, 0.52f, shade);
                float k2 = Smooth(0.58f, 0.88f, shade);
                float r = Mathf.Lerp(Mathf.Lerp(white.x, light.x, k1), dark.x, k2);
                float gg = Mathf.Lerp(Mathf.Lerp(white.y, light.y, k1), dark.y, k2);
                float b = Mathf.Lerp(Mathf.Lerp(white.z, light.z, k1), dark.z, k2);
                // float edge = smoothstep(0.5, 0.49, distance(uv, vec2(0.5)));
                float ddx = uvx - 0.5f, ddy = uvy - 0.5f;
                float dist = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                float edge = Smooth(0.5f, 0.49f, dist);              // 注意这一对是"反着的"，和原版一致
                // 注意这里不预乘：原版着色器写的是 col * edge（预乘，浏览器要的），
                // 而 uGUI 的 UI 着色器自己会做 rgb *= a —— 预乘了会被再乘一次、圆边发暗发硬（看着就是锯齿）。
                // 背景那一版不做圆边（circleEdge=false）：alpha 全满，整屏铺开才不会被裁成一个椭圆。
                dst[row + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(gg * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                    circleEdge ? (byte)Mathf.Clamp(Mathf.RoundToInt(edge * 255f), 0, 255) : (byte)255);
            }
        }
    }

    /// <summary>GLSL 的 smoothstep（参数可以反着给，反着就是"从 1 降到 0"）。</summary>
    private static float Smooth(float e0, float e1, float x) {
        float k = (e1 - e0) == 0f ? 0f : Mathf.Clamp01((x - e0) / (e1 - e0));
        return k * k * (3f - 2f * k);
    }

    /// <summary>原版的 float hash(vec2 p) = fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123)</summary>
    private static float Hash(float x, float y) {
        float s = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453123f;
        return s - Mathf.Floor(s);
    }

    /// <summary>原版的 noise：四个角按 smoothstep 权重双线性混。</summary>
    private static float Noise(float x, float y) {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy), b = Hash(ix + 1f, iy), c = Hash(ix, iy + 1f), d = Hash(ix + 1f, iy + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    /// <summary>原版的 fbm：三层，振幅 0.6 / 0.3 / 0.15，坐标每层乘 2。</summary>
    private static float Fbm(float x, float y) {
        float v = 0f, a = 0.6f;
        for (int i = 0; i < 3; i++) {
            v += a * Noise(x, y);
            x *= 2f; y *= 2f; a *= 0.5f;
        }
        return v;
    }
}
