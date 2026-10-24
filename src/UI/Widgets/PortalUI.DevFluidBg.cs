// 开发者页那层铺满整页的流体背景：和默认头像那颗球同一套数学（见 PortalUI.FluidOrb.cs），差别只有一处 ——
// 不做圆形 alpha 边缘：球靠那个圆边才收成圆形，照它铺满整页会被裁出一个大椭圆，把整屏背景挖掉一块，
// 所以背景这版 alpha 全满，整屏拉伸上去就是一整片缓慢流动的蓝。单开一个文件而不是塞回 FluidOrb.cs：
// 那边的"唯一一张贴图、唯一一个 Sprite"是治白方块与闪帧留下的，别为了这一层把它搅浑。
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {
    private const int DevFluidSize = 160;      // 铺满整屏的贴图边长（这个场很平滑，拉伸上去看不出格）
    private static Texture2D _devFluidTex;
    private static Sprite _devFluidSprite;
    private static readonly Color32[] _devFluidPx = new Color32[DevFluidSize * DevFluidSize];
    private static float _devFluidT;           // 自己的时钟（跟头像那颗球各走各的，谁也不推谁的相位）
    private static Image _devFluidOwner;       // 唯一的显示者：开发者页那层背景

    /// <summary>铺满整页的那张背景贴图。拿不到就返回 null（调用方宁可不铺，也不铺一块白上去）。</summary>
    private static Sprite DevFluidSprite() {
        if (_devFluidSprite != null) return _devFluidSprite;
        try {
            _devFluidTex = new Texture2D(DevFluidSize, DevFluidSize, TextureFormat.RGBA32, false);
            _devFluidTex.filterMode = FilterMode.Bilinear;
            _devFluidTex.wrapMode = TextureWrapMode.Clamp;
            BuildFluidPixels(_devFluidPx, DevFluidSize, false, _devFluidT * FluidTimeK);
            _devFluidTex.SetPixels32(_devFluidPx);
            _devFluidTex.Apply(false, false);
            _devFluidSprite = Sprite.Create(_devFluidTex, new Rect(0, 0, DevFluidSize, DevFluidSize),
                                            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Vector4.zero);
            return _devFluidSprite;
        } catch (System.Exception e) {
            _devFluidSprite = null;
            _devFluidTex = null;
            try { Lg.Ex("开发者页流体背景创建失败（这一页退回整屏代码背景）", e); } catch { }
            return null;
        }
    }

    /// <summary>把流体背景铺到开发者页最底下（后面的压暗罩与制作名单都盖在它身上）。
    /// 只建一个物体（自带 Image），不再套一层壳：套壳会多占一个兄弟序号，把重力字母挤到压暗罩下面。</summary>
    private static void MountDevFluidBg(Transform pageRoot) {
        if (_devFluidOwner != null || pageRoot == null) return;
        var spr = DevFluidSprite();
        if (spr == null) return;
        var img = MakeImage(pageRoot, "DevFluidBg", Color.white);
        img.raycastTarget = false;           // 背景不吃射线（整页的点按留给上面那些东西）
        img.sprite = spr;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
        Stretch(img.rectTransform, Vector2.zero, Vector2.one);
        img.transform.SetSiblingIndex(0);
        _devFluidOwner = img;
        try {
            Lg.Info("开发者页背景：流体球已铺满整页（" + DevFluidSize + "² 贴图每帧重算）");
        } catch { }
    }

    /// <summary>常驻段每帧调：只有这一页在显示时才重算贴图（不在这一页时第一行就返回，零开销）。</summary>
    private static void TickDevFluidBg() {
        if (_devFluidTex == null || _devFluidSprite == null || _devFluidOwner == null) return;
        var go = _devFluidOwner.gameObject;
        if (go == null || !go.activeInHierarchy) return;
        float dt = Time.unscaledDeltaTime;
        if (dt > FluidMaxStep) dt = FluidMaxStep;
        if (dt < 0f) dt = 0f;
        _devFluidT += dt;
        // 与头像那颗球同一个节流口径（见 FluidOrb.cs 的 FluidFps 注释）：160² 像素每帧重算 + 100KB 上传。
        _devFluidAcc += dt;
        if (_devFluidAcc < 1f / FluidFps) return;
        _devFluidAcc -= 1f / FluidFps;   // 减去阈值而不是清零：留住余量（口径见 FluidOrb.cs）
        BuildFluidPixels(_devFluidPx, DevFluidSize, false, _devFluidT * FluidTimeK);
        _devFluidTex.SetPixels32(_devFluidPx);
        _devFluidTex.Apply(false, false);
    }
}
