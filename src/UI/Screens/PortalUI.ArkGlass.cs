using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：大厅按钮的磨砂层。
/// 为什么不能复用 PortalUI.PanelGlass：那一套是把糊图裁成一块屏幕对齐的矩形，
/// 挂在独立子画布上、按遮罩的世界矩形摆位（SyncGlassRect 用 GetWorldCorners 取的是
/// 未经逐顶点透视的矩形角点）。面板那些遮罩本来就是横平竖直的，所以没问题；
/// 但大厅的按钮要经过 ArkPerspective 的逐顶点针孔投影才会变成斜的，
/// 而糊图那块矩形一动都不动，实机看就是"磨砂是一堆屏幕对齐的大板子、
/// 横跨并压过相邻按钮"（用户报的"右侧各个按键区域重叠、磨砂没跟着按键倾斜"）。
/// 这里改成"糊图作为底板自己的子图元"：它跟底板同尺寸同位置，
/// 于是 AttachArkPerspective 会把它和底板用同一套投影参数一起算，
/// 倾斜完全同步，而且它的顶点就是底板的顶点，永远不会盖到隔壁按钮上。
/// 裁哪一块：仍走全屏背景模糊链（_bgBlurRt），uvRect 按底板当前的世界矩形从整屏纹理上取，
/// 与 PanelGlass 完全同源，所以视觉上还是"贴着背景那一块糊了"。</summary>
public static partial class PortalUI {

    /// <summary>一块按钮的磨砂子图元：底板 + 它自己挂的那层糊图。</summary>
    private sealed class ArkGlassLayer {
        public Image Plate;                 // 跟着谁走（读它的世界矩形与 alpha）
        public RawImage Crop;               // 那块糊图（底板的子节点）
        public float BaseAlpha = 1f;        // 底板建的时候那档 alpha，当分母算"露几成"
        public float BaseR, BaseG, BaseB;   // 底板建的时候那档颜色（隐藏时回它）
        public float LastR = -1f, LastG = -1f, LastB = -1f;   // 上次写进糊图的染色（变了才写）
        public float Applied = -1f;         // 上次写下去的浓度（变了才写）
        public float LastX, LastY, LastW, LastH;
        public bool RectValid;
    }

    private static readonly List<ArkGlassLayer> _arkGlass = new List<ArkGlassLayer>();
    private static readonly Vector3[] _arkGlassCorners = new Vector3[4];
    private static bool _arkGlassLogged;

    /// <summary>给一块大厅底板挂上磨砂层。糊图作为它的子节点（Stretch 铺满），
    /// 所以逐顶点透视会把它和底板一起算，倾斜、缩放、位移全部同步，不用另做几何。
    /// 必须在底板的文字/图标之前建，或者显式 SetAsFirstSibling：
    /// uGUI 同层按先后画，糊图要垫在最下面。</summary>
    private static void RegisterArkGlass(Image plate) {
        if (plate == null) return;
        try {
            for (int i = _arkGlass.Count - 1; i >= 0; i--) {
                var old = _arkGlass[i];
                if (old == null || old.Plate == null || old.Crop == null) { _arkGlass.RemoveAt(i); continue; }
                if (old.Plate == plate) return;                 // 已经登记过了
            }
            // SetParent 必须紧跟在 new GameObject 的下一行（外部工具按两行窗口判"是不是顶层对象"）
            var go = new GameObject("ArkGlassCrop", typeof(RectTransform));
            go.transform.SetParent(plate.transform, false);
            var ri = go.AddComponent<RawImage>();
            ri.raycastTarget = false;                           // 绝不吃射线：它就是块底下的糊图
            ri.texture = null;
            // 糊图按底板自己的颜色染色（TickArkGlass 里写 color）。模板的 backdrop-blur 是
            // "糊图垫在半透明深色底下"，这里把那层深色直接乘进糊图，深色磨砂的样子才对。
            // 早先用白色不染色，亮糊直接盖在底板上，把底板的压暗整层顶掉了（用户报的①）。
            ri.color = new Color(0f, 0f, 0f, 0f);
            ri.enabled = false;
            var rt = ri.rectTransform;
            Stretch(rt, Vector2.zero, Vector2.zero);             // 与底板同尺寸同位置
            rt.SetAsFirstSibling();                              // 垫在底板所有子内容（竖条/文字/图标）下面

            var g = new ArkGlassLayer();
            g.Plate = plate;
            g.Crop = ri;
            g.BaseAlpha = plate.color.a > 0.001f ? plate.color.a : 1f;
            g.BaseR = plate.color.r; g.BaseG = plate.color.g; g.BaseB = plate.color.b;
            _arkGlass.Add(g);

            if (!_arkGlassLogged) {
                _arkGlassLogged = true;
                Lg.Info("大厅磨砂：糊图改为按钮自己的子图元（" 
                    + "与按钮共用同一套逐顶点透视 · 不会再盖到隔壁 · 不与 PanelGlass 抢）");
            }
        } catch (System.Exception e) { Lg.Ex("登记大厅磨砂失败", e); }
    }

    /// <summary>大厅磨砂要不要让背景模糊链转起来。与面板磨砂共用同一条链
    /// （TickBgBlur 的第一道闸会问 PanelGlassNeedsChain，这里把大厅那份需求也挂上去）。</summary>
    private static bool ArkGlassNeedsChain() {
        return _arkGlass.Count > 0 && PanelBlur >= PanelGlassMinPercent;
    }

    /// <summary>这块底板"现在露几成"（0 = 藏起来了 / 大厅没在显示）。与 PanelGlass 同一套口径：
    /// 自己或祖先被 SetActive(false) / alpha 被按比例改过 / 父链上的 CanvasGroup 一起乘。</summary>
    private static float ArkGlassVisibleK(ArkGlassLayer g) {
        if (g.Plate == null || !g.Plate.isActiveAndEnabled) { g.RectValid = false; return 0f; }
        float a = Mathf.Clamp01(g.Plate.color.a / Mathf.Max(0.0001f, g.BaseAlpha));
        if (a <= 0.001f) return 0f;
        Transform t = g.Plate.transform;
        while (t != null) {
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null) a *= Mathf.Clamp01(cg.alpha);
            t = t.parent;
        }
        return a;
    }

    /// <summary>每帧：把每块糊图的 uvRect 跟到底板当前的世界矩形上，并按滑条推浓度。
    /// 只有变了才写 —— 每帧写 RawImage 会把那块画布弄脏（静态时这里一个字节都不碰）。</summary>
    private static void TickArkGlass() {
        if (_arkGlass.Count == 0) return;
        try {
            float sw = Mathf.Max(1f, Screen.width), sh = Mathf.Max(1f, Screen.height);
            int depth = Mathf.Clamp(_bgBlurDepth, 1, BgBlurMaxLevels);
            float k = Mathf.Clamp01(PanelBlur / 100f);
            bool on = PanelBlur >= PanelGlassMinPercent && !_bgBlurDead && _bgBlurRt[0] != null;
            // 与 PanelGlass 同一条斜坡：最上面那张铺满的档
            float a = on ? Mathf.Clamp01(k * depth) : 0f;
            var tex = (on && a > BgBlurMinAlpha) ? (Texture)_bgBlurRt[0] : null;

            for (int i = _arkGlass.Count - 1; i >= 0; i--) {
                var g = _arkGlass[i];
                if (g == null || g.Plate == null || g.Crop == null) { _arkGlass.RemoveAt(i); continue; }
                float vis = ArkGlassVisibleK(g);
                float want = a * vis;
                bool show = tex != null && want > BgBlurMinAlpha;
                var ri = g.Crop;
                if (ri.texture != tex) ri.texture = tex;
                if (ri.enabled != show) ri.enabled = show;
                if (!show) {
                    if (g.Applied != 0f) { g.Applied = 0f; ri.color = new Color(g.BaseR, g.BaseG, g.BaseB, 0f); }
                    continue;
                }
                // 铺八成五：与 PanelGlass 同一个口径，全铺会把底板自己那层半透明底色盖掉
                float ca = want * 0.85f;
                // 染色跟底板"当前"颜色走：悬停提亮改的是底板，糊图不跟着变的话悬停就没观感了
                var pc = g.Plate.color;
                bool caChanged = Mathf.Abs(g.Applied - ca) > 0.002f;
                bool tintChanged = Mathf.Abs(g.LastR - pc.r) > 0.002f || Mathf.Abs(g.LastG - pc.g) > 0.002f
                                || Mathf.Abs(g.LastB - pc.b) > 0.002f;
                if (caChanged || tintChanged) {
                    g.Applied = ca;
                    g.LastR = pc.r; g.LastG = pc.g; g.LastB = pc.b;
                    ri.color = new Color(pc.r, pc.g, pc.b, ca);
                }
                SyncArkGlassRect(g, sw, sh);
            }
        } catch (System.Exception e) {
            if (!_arkGlassFailLogged) {
                _arkGlassFailLogged = true;
                Lg.Ex("大厅磨砂失败（这一帧不显示，背景保持清晰）", e);
            }
        }
    }

    private static bool _arkGlassFailLogged;

    /// <summary>把这块糊图的 uvRect 跟到底板当前的世界矩形上。
    /// 糊图是底板的子节点、已经 Stretch 铺满，所以它的几何不用管（会跟着底板一起被透视）；
    /// 只有"从整屏纹理上取哪一块"要每帧更新，因为底板随鼠标左右位移、也在被透视放大。</summary>
    private static void SyncArkGlassRect(ArkGlassLayer g, float sw, float sh) {
        g.Plate.rectTransform.GetWorldCorners(_arkGlassCorners);   // 里面那层子图元没有额外变换
        float x0 = _arkGlassCorners[0].x, y0 = _arkGlassCorners[0].y;
        float x1 = _arkGlassCorners[2].x, y1 = _arkGlassCorners[2].y;
        if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
        if (y1 < y0) { float t = y0; y0 = y1; y1 = t; }
        float w = x1 - x0, h = y1 - y0;
        if (!(w > 1f) || !(h > 1f)) { g.RectValid = false; return; }
        if (g.RectValid
            && Mathf.Abs(g.LastX - x0) <= 0.5f && Mathf.Abs(g.LastY - y0) <= 0.5f
            && Mathf.Abs(g.LastW - w) <= 0.5f && Mathf.Abs(g.LastH - h) <= 0.5f) return;
        g.RectValid = true;
        g.LastX = x0; g.LastY = y0; g.LastW = w; g.LastH = h;
        // uvRect 超出 [0,1] 时链子是 Clamp 采样（与 PanelGlass 同一口径），屏幕外那半块照样对
        g.Crop.uvRect = new Rect(x0 / sw, y0 / sh, w / sw, h / sh);
    }

    /// <summary>拆大厅时把这一份清单清掉（对象会跟着底板一起销毁，这里只清引用）。</summary>
    private static void ClearArkGlass() {
        _arkGlass.Clear();
        _arkGlassLogged = false;
        _arkGlassFailLogged = false;
    }
}
