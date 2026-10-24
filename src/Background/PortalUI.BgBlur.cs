using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>开始界面 → 登录界面 过渡的背景渐进模糊：滑动过程中背景模糊度渐涨，滑回来渐退。
/// 做法 = mip 链（逐级 1/2 降采样 + 双线性放大回整屏；必须逐级 1/2，一步降到底漏采样出的是噪点）。
/// 模糊度只有一个写入口 SetBgBlur，由 SsPaintCover 按 k = _ssBehind ? 1−封面透明度 : 0 驱动。
/// 不抓屏、无 shader；档数由 BgBlurMaxTexelPx 红线和屏幕分辨率现算，实机锯齿的根源是纹素周期不是糊量。</summary>
public static partial class PortalUI {

 // ================= 常量 =================

 /// <summary>糊图档数的容量上限（第 j 档 = 源的 1/2^(j+1)）；实际用几档由 BgBlurMaxTexelPx 红线现算（_bgBlurDepth）。</summary>
    private const int BgBlurMaxLevels = 4;

/// <summary>最深那一档的一个纹素在屏幕上最多占几个像素 = 12，这是判锯齿的唯一红线。
    /// 我们这套是"缩成低分辨率 + 双线性放大"：残留结构周期 = 一个纹素占几个屏幕像素，周期 4~8 舒服、16 以上就锯。
    /// 所以"不够糊"和"锯齿"不是一对矛盾 —— 缩得越小周期必然越粗，光调糊量无解；钉屏幕像素而不是钉档数，
    /// 才能不受"玩家的源多清晰"影响。
    /// 【从 8 放宽到 12】视频背景默认开启后糊的源变成 960×408 的视频纹理，
    /// 按 8 算第 2 档纹素刚好卡在边界（1920/240 = 8，判据是"超过才停"）→ 只剩 1 档，
    /// 各处遮罩看着像"模糊没了"（源本来就是视频、细节少，1 档的糊量根本看不出来）。
    /// 12 是"仍远小于 16 那条锯齿线"的前提下能把档数要回来的最大档 —— 视频源正好拿到 2 档。</summary>
    private const float BgBlurMaxTexelPx = 12f;

 /// <summary>每一档额外做几轮平滑（放大 2 倍再缩回一半，等价于对这张小图做一次 [1,6,1]/8，能削浅折痕）。
 /// 加它而不是再缩一档：再缩会把纹素周期变粗又锯，而多几轮平滑只涨糊量、周期一点不变。
 /// 0 = 不平滑（折痕回来）。想再糊只动这一个数，别动上面那条周期红线。</summary>
    private const int BgBlurSmoothPasses = 3;

 /// <summary>从第几档开始平滑（第 0 档纹素才 4 像素、折痕看不见，不值得花那两笔 Blit）</summary>
    private const int BgBlurSmoothFrom = 1;

 /// <summary>低于这个就不显示（省掉一次全屏 overdraw）</summary>
    private const float BgBlurMinAlpha = 0.002f;

 // ================= 状态 =================

    private static readonly RenderTexture[] _bgBlurRt = new RenderTexture[BgBlurMaxLevels];
    private static readonly int[] _bgBlurRtW = new int[BgBlurMaxLevels];
    private static readonly int[] _bgBlurRtH = new int[BgBlurMaxLevels];
 /// <summary>平滑用的临时 RT（"放大 2 倍"那一站，只有一张、各档轮流用）</summary>
    private static RenderTexture _bgBlurScratch;
    private static int _bgBlurScratchW, _bgBlurScratchH;
    private static Texture _bgBlurSrc;          // 链子是按哪张源烘的（换了就重烘）
    private static bool _bgBlurStale = true;    // 链子需要重烘一遍
    private static float _bgBlurK;              // 0 = 清晰 / 1 = 最糊
 /// <summary>整层还在渐入时"挂着"的那个值（见 SetBgBlur 里那段"不闪"的说明）</summary>
    private static float _bgBlurPending;
    private static bool _bgBlurHasPending;
    private static bool _bgBlurDead;            // 出过一次错 永久关掉（背景保持清晰）
    private static bool _bgBlurLogged;          // 源那行日志只打一次（换源再打）
 /// <summary>这一次实际用了几档，由 BgBlurMaxTexelPx 红线现算（见 BuildBgBlurChain）。
 /// 模糊度的斜坡、日志、循环全都读它，别拿容量上限当档数用。</summary>
    private static int _bgBlurDepth = 1;

 /// <summary>一屏的糊图各档（挂在那屏背景图上面一层）；认 Parent 去重，同一个父节点下只挂一组。</summary>
    private sealed class BgBlurHost {
        public Transform Parent;
        public readonly RawImage[] Img = new RawImage[BgBlurMaxLevels];
    }
    private static readonly List<BgBlurHost> _bgBlurHosts = new List<BgBlurHost>();

 /// <summary>各档的目标 alpha，复用同一个数组：ApplyBgBlurAlphas 每次拖动都在调，每帧 new float[] 就是每帧一次 GC。</summary>
    private static readonly float[] _bgBlurAlpha = new float[BgBlurMaxLevels];

 // ================= 挂层 =================

 /// <summary>某一屏建好"纯色底 + 代码背景 + 视频层"之后调它一次：紧跟 AttachVideoBg 之后、建遮罩之前
 /// （顺序 纯色底 → 代码背景 → BgVideo → 糊图各档 → Veil）。anchor 传代码背景那一组的根节点，
 /// 糊图就压在整个背景之上。糊图必须在遮罩下面：Veil 是那层 0.45 黑，糊图压在它上面会比别处亮一截。
 /// 同一个父节点已挂过就什么都不做；_bgBlurDead 时直接返回。</summary>
    private static void StartBgBlurLayers(Transform anchor) {
        try {
            if (_bgBlurDead || anchor == null) return;
            var parent = anchor.parent;
            if (parent == null) return;
            PruneBgBlurHosts();
            for (int i = 0; i < _bgBlurHosts.Count; i++) {
                if (_bgBlurHosts[i].Parent == parent) return;   // 这一屏已经挂过了
            }
            var host = new BgBlurHost();
            host.Parent = parent;
            int at = anchor.GetSiblingIndex() + 1;              // 紧贴背景上面一档
            for (int j = 0; j < BgBlurMaxLevels; j++) {
    // 顺序不能动：SetParent 必须紧跟 new GameObject 的下一行
                var go = new GameObject("BgBlur" + j, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.transform.SetSiblingIndex(at + j);
                var ri = go.AddComponent<RawImage>();
                ri.raycastTarget = false;                       // 永远不吃射线（开始界面那张背景图身上挂着整屏手势）
                if (_bgBlurRt[j] != null) ri.texture = _bgBlurRt[j];
                ri.color = new Color(1f, 1f, 1f, 0f);
                ri.enabled = false;
                Stretch(ri.rectTransform, Vector2.zero, Vector2.one);
                host.Img[j] = ri;
            }
            _bgBlurHosts.Add(host);
            _bgBlurStale = true;                                // 下一帧把链子准备好
            ApplyBgBlurAlphas();                                // 先按当前 k 摆好（新挂上来的一律全透明）
        } catch (System.Exception e) {
            _bgBlurDead = true;
            try { Lg.Ex("背景模糊挂层失败 ->  已关掉（背景保持清晰）", e); } catch { }
        }
    }

 /// <summary>当前最糊那一档的纹理（糊图链还没建起来时是 null）。给"整屏无颜色的模糊遮罩"用
 /// （个人页那层：整屏铺最糊那一档、自己不染色，看着就是背景被糊掉）。</summary>
    public static Texture BgBlurDeepestTexture() {
        int j = Mathf.Clamp(_bgBlurDepth - 1, 0, BgBlurMaxLevels - 1);
        return _bgBlurRt[j];
    }

 /// <summary>糊的源现在是不是视频纹理（诊断用：视频没显形时糊的是代码背景，那本来就是暗的）</summary>
    public static bool BgBlurSourceIsVideo() {
        bool isVideo;
        BgBlurSource(out isVideo);
        return isVideo;
    }

 /// <summary>当前建起来的糊图档数（诊断用）</summary>
    public static int BgBlurDepthNow() { return _bgBlurDepth; }

 /// <summary>唯一一处写模糊度的地方。k：0 = 背景清晰 / 1 = 最糊。
 /// 全项目只有 SsPaintCover 调它，靠 k = _ssBehind ? 1 − 封面透明度 : 0 这一条不变式盖住拖动、滑出、
 /// 从登录界面滑回来、收起来、重新摆出来全部路径 —— 另加一个"模糊进度"状态迟早会漏喂某一条。
 /// _ssBehind == false（底下不是登录界面、封面不让位）时 k 恒 0：那时后面露出来的是谱面库，糊背景没意义。</summary>
    private static void SetBgBlur(float k) {
        float nk = Mathf.Clamp01(k);
  // 整层还没铺满（画布组 alpha < 1）时模糊度先挂着不动：屏保从 alpha 0 渐入时若当场把 k 归零，
  // 底下还看得见的登录界面会一帧之内从糊变清晰 = 闪现。等 FlushBgBlur（TickStartScreen 渐入收尾）
  // 落下那一刻封面已完全不透明，归零是隐形的。反方向同理：屏保盖在糊着的登录界面上时整段渐入一直糊。
        if (_ssGroup != null && _ssGroup.alpha < 1f) {
            _bgBlurPending = nk;
            _bgBlurHasPending = true;
            return;
        }
        ApplyBgBlur(nk);
    }

 /// <summary>渐入铺满那一刻把"挂着"的那个值落下去（此时封面完全不透明 归零是隐形的）</summary>
    private static void FlushBgBlur() {
        if (!_bgBlurHasPending) return;
        _bgBlurHasPending = false;
        ApplyBgBlur(_bgBlurPending);
    }

 /// <summary>强行归零，绕过"整层没铺满就先挂着"那道闸：给这一层马上要被销毁时用（DropStartScreenLayer），
 /// 不真的归零的话下次冒出来的开始界面会顶着一个"挂着 1"的旧值糊一屏。</summary>
    private static void ResetBgBlur() {
        _bgBlurHasPending = false;
        _bgBlurPending = 0f;
        ApplyBgBlur(0f);
    }

 /// <summary>真写状态的那一层（"挂着"那道闸在 SetBgBlur 里，别绕过它）</summary>
    private static void ApplyBgBlur(float k) {
        if (Mathf.Abs(k - _bgBlurK) < 0.0005f) return;
        _bgBlurK = k;
        if (k > BgBlurMinAlpha) _bgBlurStale = true;   // 从"清晰"回到"要糊" 链子得是新的
        ApplyBgBlurAlphas();
    }

 /// <summary>把当前 k 画到所有已挂的层上（唯一一处写那几档 alpha 的地方）。
 /// a_j = clamp(k × 档数 − j, 0, 1) 任何时刻最多两档在 0~1 之间（相邻两档，不是双重曝光）。
 /// 再顺手剔掉"被上面那档完全盖住"的层：k = 1 时每档都是 alpha 1，但只有最上面那档看得见，
 /// 底下几档是白白的整屏 overdraw 实际最多只画 2 张（top = 最高的那张已满档，比它还低的全部关掉）。</summary>
    private static void ApplyBgBlurAlphas() {
        try {
            float k = Mathf.Clamp01(_bgBlurK);
            float[] a = _bgBlurAlpha;
            int top = -1;
            for (int j = 0; j < BgBlurMaxLevels; j++) {
    // 必须用实际档数（_bgBlurDepth）而不是容量上限：用上限的话，只建了 2 档却按 4 档铺斜坡，
    // 全程只走到 1/4 的进度就满 永远糊不到底。
    // 斜坡是 k × 档数（不是 k × (档数 − 0.5)）：−0.5 那版终点是两档各占一半，两张模糊度不同的图
    // 叠在一起 = 重影（同一条边一锐一糊）看着就像锯齿，而且互相稀释也不够糊。终点必须是单档铺满。
                a[j] = Mathf.Clamp01(k * _bgBlurDepth - j);
                if (a[j] >= 1f) top = j;
            }
            for (int i = _bgBlurHosts.Count - 1; i >= 0; i--) {
                var h = _bgBlurHosts[i];
                if (h == null) { _bgBlurHosts.RemoveAt(i); continue; }
                for (int j = 0; j < BgBlurMaxLevels; j++) {
                    var ri = h.Img[j];
                    if (ri == null) continue;
                    var c = ri.color;
                    if (Mathf.Abs(c.a - a[j]) > 0.0005f) ri.color = new Color(1f, 1f, 1f, a[j]);
                    bool on = a[j] > BgBlurMinAlpha && j >= top && _bgBlurRt[j] != null;
                    if (ri.enabled != on) ri.enabled = on;
                }
            }
        } catch { }
    }

 /// <summary>把已经销毁的层剔掉：画布一销毁这些引用就成了"假的 null"。</summary>
    private static void PruneBgBlurHosts() {
        for (int i = _bgBlurHosts.Count - 1; i >= 0; i--) {
            var h = _bgBlurHosts[i];
            if (h == null || h.Parent == null || h.Img[0] == null) _bgBlurHosts.RemoveAt(i);
        }
    }

 /// <summary>彻底关掉时把层收干净（绝不留一块糊图挂在屏幕上）</summary>
    private static void DisableBgBlurLayers() {
        _bgBlurK = 0f;
        for (int i = _bgBlurHosts.Count - 1; i >= 0; i--) {
            var h = _bgBlurHosts[i];
            if (h == null) { _bgBlurHosts.RemoveAt(i); continue; }
            for (int j = 0; j < BgBlurMaxLevels; j++) {
                var ri = h.Img[j];
                if (ri == null) continue;
                ri.color = new Color(1f, 1f, 1f, 0f);
                ri.enabled = false;
            }
        }
    }

 // ================= 烘链子 =================

 /// <summary>糊图源强制走代码背景（个人页那种"只挂代码背景、不挂视频"的一屏用它）。
 /// 视频就绪时下面默认挑视频纹理，而那一屏根本没有视频层，裁出来的糊图会是另一幅画面。</summary>
    /// <summary>要糊的那张源：跟背景层级同一条优先级 —— 视频 → 静态图 → 代码背景。
 /// 糊图是全局一张图（各屏磨砂裁的是同一批 RT），所以判据只能是全局事实，不能由某一屏的旗子改：
 /// 登录界面那颗「强制走代码背景」的旗子曾经每帧按"我这屏的视频没显形"改全局，
 /// 于是面板开着视频、磨砂却糊着代码背景 = 神经图隔着糊图压在视频上。
 /// 「有静态图或视频时神经图绝不显示」这条铁律对糊图一样成立：视频可用但纹理还没到（准备那几十帧）
 /// 时不许退回代码背景，糊一张纯色（= 那几帧屏幕上真正显示的东西），等纹理到了自动换成视频。</summary>
    private static Texture BgBlurSource(out bool isVideo) {
        bool videoWanted = VideoBgUsable();     // 视频这一条路通着（开关开 + 有可播文件）
        // 只在此刻纹理真的有效时才拿视频当源：拿到过帧的锁存不能当判据 ——
        // 视频预热期纹理会短暂为 null，那时把它交给糊图链，13 块磨砂就会裁出一块空白的圆角矩形。
        if (videoWanted && _bgVideo != null && _bgVideo.texture != null) {
            isVideo = true;
            return _bgVideo.texture;
        }
        isVideo = false;
        // 视频准备中：屏幕上此刻是静态图（没静态图就是纯色底），糊它或糊纯色，
        // 绝不能糊代码背景 —— 那等于把不许显示的神经图透过磨砂露出来。
        if (videoWanted) return Texture2D.blackTexture;
        if (StaticBgVisible()) {
            var spr = StaticBgSprite();
            if (spr != null && spr.texture != null) return spr.texture;
        }
        // 真的什么都没有（视频关着、静态图也没有）才糊代码背景：它此刻就是屏幕上显示的那一层。
        return NetBgSourceTexture();
    }

 /// <summary>常驻段每帧调，顺序上必须排在 TickVideoBg 之后（先让它把视频层的状态/纹理弄好，见 TickCore）。
 /// 这条链子有两个用户：开始界面→登录界面那条过渡（_bgBlurK）和面板磨砂（PanelGlassNeedsChain，
 /// 它自己的斜坡在 TickPanelGlass 里） 两条都不要时第一行就 return（面板关着时磨砂那条零开销）。</summary>
    private static void TickBgBlur() {
        try {
            if (_bgBlurDead) return;
            bool glass = PanelGlassNeedsChain() || ArkGlassNeedsChain() || _glassExtraWant || _glassProfileWant;
            if (_bgBlurK <= BgBlurMinAlpha && !glass) return;   // 两条需求都没有 零开销
            if (_bgBlurHosts.Count == 0 && !glass) return;      // 既没挂层、也没人要磨砂
            bool isVideo;
            var src = BgBlurSource(out isVideo);
            if (src == null) return;
            if (src != _bgBlurSrc) { _bgBlurSrc = src; _bgBlurStale = true; _bgBlurLogged = false; }
   // 视频背景每帧重烘，不许降频：糊图隔几帧才换一次就是几 fps 的台阶，旁边没磨砂的地方还在正常帧率，
   // 糊图块里外一动一静，一眼就看出在顿。稳态重烘不到 1.5ms（贵的是第一次建 RT 那一笔）。
            if (isVideo) _bgBlurStale = true;             // 视频每帧在变 每帧重烘
            if (!_bgBlurStale) return;
            _bgBlurStale = false;
            BuildBgBlurChain(src, isVideo);
        } catch (System.Exception e) {
            _bgBlurDead = true;
            try { Lg.Ex("背景模糊失败 ->  已关掉（背景保持清晰）", e); } catch { }
            DisableBgBlurLayers();
        }
    }

 /// <summary>把源逐级 1/2 烘进若干张 RT。档数不固定，由 BgBlurMaxTexelPx（最深那档的纹素最多占屏幕几个像素）
 /// 现算：一直往下缩，缩到再缩一格纹素就超过 8 屏幕像素就停（第 0 档无条件保留，不然等于没效果）。
 /// 钉纹素而不是钉档数的意义：源越清晰、同样档数越安全，换个 4K 静图会自动多给一档。</summary>
    private static void BuildBgBlurChain(Texture src, bool isVideo) {
        int sw = Mathf.Max(8, src.width), shh = Mathf.Max(8, src.height);
  // ① 先按红线算出这次用几档（纯算术，不建任何东西）
        int depth = 0, bw = sw, bh = shh;
        int depthByLine = 0, lw = sw, lh = shh;
        for (int j = 0; j < BgBlurMaxLevels; j++) {
            int nw = Mathf.Max(4, bw / 2), nh = Mathf.Max(4, bh / 2);
            int texel = Mathf.Max(Mathf.RoundToInt((float)Screen.width / Mathf.Max(1, nw)),
                                  Mathf.RoundToInt((float)Screen.height / Mathf.Max(1, nh)));
            if (j > 0 && texel > BgBlurMaxTexelPx) break;      // 第 0 档无条件要（否则等于没效果）
            bw = nw; bh = nh; depth = j + 1;
            lw = nw; lh = nh; depthByLine = j + 1;             // 多记一份：补档后要重算它的纹素
        }
  // ② 补到至少 2 档 —— 只剩 1 档时糊量小到看不出来（视频背景默认开启后就是这样），
  // 各处遮罩会像"模糊丢了"。但补完必须重新核纹素：源小到补出来那一档已经超红线时
  //（极小源 + 大屏，例如 320×180 铺 1920 屏）宁可退回红线给的档数 —— 糊量小只是"不够糊"，
  // 超红线是"明显锯齿"，后者更糟。
        if (depth < 2 && depthByLine >= 1) {
            int nw = Mathf.Max(4, lw / 2), nh = Mathf.Max(4, lh / 2);
            int texel = Mathf.Max(Mathf.RoundToInt((float)Screen.width / Mathf.Max(1, nw)),
                                  Mathf.RoundToInt((float)Screen.height / Mathf.Max(1, nh)));
            if (texel <= BgBlurMaxTexelPx) depth = 2;         // 补档后仍在红线内才要
            else Lg.Info("背景模糊：源太小（" + sw + "×" + shh
                + "），补到 2 档纹素会涨到 " + texel + " 像素（超过红线 " + BgBlurMaxTexelPx
                + "）→ 保持红线给的 " + depthByLine + " 档（宁可糊得轻一点，也不要锯）");
        }
        _bgBlurDepth = Mathf.Clamp(Mathf.Max(depth, 1), 1, BgBlurMaxLevels);
  // ② 按这个档数建 RT 并逐级烘（只烘用得着的那些；多出来的槽留空 那几张 RawImage 一直是关的）
        bool rebuilt = false;
        bw = sw; bh = shh;
        for (int j = 0; j < _bgBlurDepth; j++) {
            bw = Mathf.Max(4, bw / 2); bh = Mathf.Max(4, bh / 2);   // 逐级 1/2（一步降到底会出噪点）
            if (_bgBlurRt[j] != null && _bgBlurRtW[j] == bw && _bgBlurRtH[j] == bh) continue;
            if (_bgBlurRt[j] != null) { try { _bgBlurRt[j].Release(); Object.Destroy(_bgBlurRt[j]); } catch { } }
            var rt = new RenderTexture(bw, bh, 0);
            rt.name = "LevelLib_BgBlur" + j;
            rt.filterMode = FilterMode.Bilinear;      // 放回整屏靠的就是它
            rt.wrapMode = TextureWrapMode.Clamp;      // 边缘不许重复采样
            rt.useMipMap = false;
            rt.autoGenerateMips = false;
            try { rt.Create(); } catch { }
   // RT 建不出来（极端环境 / 显存不够）必须当场认输：不判的话 Blit 变成空操作、RT 里是未初始化内容，
   // 而它照样会被 RawImage 画出来 屏幕上是一块雪花/纯色，比"没效果"糟得多。
            if (!rt.IsCreated()) {
                try { Object.Destroy(rt); } catch { }
                throw new System.Exception("RenderTexture 建不出来（" + bw + "×" + bh + "）");
            }
            _bgBlurRt[j] = rt; _bgBlurRtW[j] = bw; _bgBlurRtH[j] = bh;
            rebuilt = true;
        }
        if (rebuilt) RebindBgBlurTextures();
        Texture cur = src;
        for (int j = 0; j < _bgBlurDepth; j++) {
            if (_bgBlurRt[j] == null) break;
            Graphics.Blit(cur, _bgBlurRt[j]);          // 无材质 = 引擎内置那个拷贝材质，不需要自定义 shader
            cur = _bgBlurRt[j];
   // 平滑必须刚烘好这一档就做：放到循环外统一做的话，后面几档是从"没平滑过的上一档"降下来的，折痕照样传下去
            for (int s = 0; s < BgBlurSmoothPasses && j >= BgBlurSmoothFrom; s++) SmoothBgBlurLevel(j);
        }
  // 链子刚建出来时那几张 RawImage 还是没有纹理、强制关着的状态 当场按当前 k 重摆一次
  // （不然要等下一帧 SetBgBlur 才显形 = 拖动第一帧糊不上）
        if (rebuilt) ApplyBgBlurAlphas();
        if (!_bgBlurLogged) {
            _bgBlurLogged = true;
   // 尺寸串必须按档数循环拼：写死成 _bgBlurRtW[0]/[1]/[2]/[3] 的话档数一改就下标越界，
   // 而 BuildBgBlurChain 外面裹着 try 表现成"背景模糊彻底关掉"、背景永远清晰，极难联想到是日志越界
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < _bgBlurDepth; j++) {
                if (_bgBlurRt[j] == null) continue;
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(_bgBlurRtW[j]).Append("×").Append(_bgBlurRtH[j]);
            }
            int deepest = Mathf.Max(1, _bgBlurRtW[_bgBlurDepth - 1]);
            Lg.Info("背景模糊：mip 链已建 源=" + (isVideo ? "视频背景" : "背景图")
                + " " + sw + "×" + shh + " → " + sb.ToString()
                + "（逐级 1/2 放回整屏即模糊 · " + _bgBlurDepth + " 档 最深 1/" + (1 << _bgBlurDepth)
                + " ->  最深那档一个纹素占屏幕 "
                + Mathf.Max(1, Mathf.RoundToInt((float)Screen.width / deepest))
                + " 像素 红线 " + BgBlurMaxTexelPx + "）");
        }
    }

 /// <summary>给某一档做一轮平滑：放大 2 倍 → 再缩回原来那么大（GPU 上就是两笔 Graphics.Blit，走同一张 scratch RT）。
 /// "放大再缩回"等价于对这张小图做一次 [1,6,1]/8 卷积，相邻纹素变平缓 削掉折痕尖峰（双线性放大是分段线性的，
 /// 斜边被放大成一串直线段就是锯齿）。
 /// scratch 只有一张、按最大那一档的 2 倍建，每档做完就还回来复用；目标尺寸必须是这一档的 2 倍 ——
 /// 写成固定尺寸就变成"放大 N 倍再缩回"，核的形状变了（糊的量也跟着变）。</summary>
    private static void SmoothBgBlurLevel(int j) {
        if (j < 0 || j >= BgBlurMaxLevels) return;
        var rt = _bgBlurRt[j];
        if (rt == null) return;
        int w = Mathf.Max(4, _bgBlurRtW[j] * 2), h = Mathf.Max(4, _bgBlurRtH[j] * 2);
        if (_bgBlurScratch == null || _bgBlurScratchW != w || _bgBlurScratchH != h) {
            if (_bgBlurScratch != null) { try { _bgBlurScratch.Release(); Object.Destroy(_bgBlurScratch); } catch { } }
            var srt = new RenderTexture(w, h, 0);
            srt.name = "LevelLib_BgBlurScratch";
            srt.filterMode = FilterMode.Bilinear;
            srt.wrapMode = TextureWrapMode.Clamp;
            srt.useMipMap = false;
            srt.autoGenerateMips = false;
            try { srt.Create(); } catch { }
            if (!srt.IsCreated()) {
                try { Object.Destroy(srt); } catch { }
                throw new System.Exception("模糊平滑用的 scratch RT 建不出来（" + w + "×" + h + "）");
            }
            _bgBlurScratch = srt; _bgBlurScratchW = w; _bgBlurScratchH = h;
        }
        Graphics.Blit(rt, _bgBlurScratch);       // 放大 2 倍（双线性）
        Graphics.Blit(_bgBlurScratch, rt);       // 再缩回来（2×2 盒，等价于一次 [1,6,1]/8 平滑）
    }

 /// <summary>RT 被重建过 各档重新贴到各屏的 RawImage 上</summary>
    private static void RebindBgBlurTextures() {
        for (int i = _bgBlurHosts.Count - 1; i >= 0; i--) {
            var h = _bgBlurHosts[i];
            if (h == null) { _bgBlurHosts.RemoveAt(i); continue; }
            for (int j = 0; j < BgBlurMaxLevels; j++) {
                var ri = h.Img[j];
                if (ri == null) continue;
                if (ri.texture != _bgBlurRt[j]) ri.texture = _bgBlurRt[j];
            }
        }
    }
}
