using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：静态背景。
/// 口径：没有动态背景（视频还没接手）时显示静态图；有静态图或者视频时神经图绝不显示。
/// 图两份：默认那张随资源包分发（assets\bg_static_default.jpg），玩家自己选的存 assets\bg_static_custom.png
/// （玩家数据、不进资源包；换图与恢复默认都只动这一份）。自定义优先于默认；两份都不在（资源包没下完、
/// 也没自己选过）才回落到代码背景（神经图）。每一屏建背景时挂一层全屏 Image，显隐由 TickStaticBg 每帧决定：
/// 静态图不是动画，没有播放器、没有就绪概念，贴图走常驻图片缓存（LoadAssetSprite），换图时作废缓存重读。
/// 解码与编码都走 System.Drawing 那条托管通道：Unity 这份 profile 里 ImageConversion 只有 ReadOnlySpan 重载，编译不过。</summary>
public static partial class PortalUI {

    /// <summary>默认静态背景的文件名（随资源包分发）。</summary>
    private const string StaticDefaultName = "bg_static_default.jpg";

    /// <summary>玩家自己选的静态背景文件名。刻意不复用旧版静态背景用过的 bg_custom.png：
    /// 老玩家的安装目录里可能还躺着当时选的图（旧功能删掉之后没人清），沿用旧名等于把那些老图突然复活。</summary>
    private const string StaticCustomName = "bg_static_custom.png";

    /// <summary>淡入 / 淡出时长与单帧步长上限（与代码背景那边的观感同一档）。</summary>
    private const float StaticBgFadeSeconds = 0.45f;
    private const float StaticBgFadeMaxStep = 0.1f;

    /// <summary>玩家自选图缩放后的最大边长（超过就等比缩到框内）：8K 大图存原样的话，
    /// 每次进游戏都要重新解码一大张，白白拖慢加载。</summary>
    private const int StaticCustomMaxW = 1920;
    private const int StaticCustomMaxH = 1080;

    private static readonly List<Image> _bgStaticLayers = new List<Image>();
    private static bool _staticWasAvailable;                // 上一帧"有没有静态图"（掉没了要把神经图放回来）

    /// <summary>默认静态背景的完整路径。拿不到 mod 目录返回 null。</summary>
    private static string StaticDefaultPath() {
        try { return Path.Combine(Main.ModEntry.Path, "assets", StaticDefaultName); } catch { return null; }
    }

    /// <summary>玩家自选静态背景的完整路径。</summary>
    private static string StaticCustomPath() {
        try { return Path.Combine(Main.ModEntry.Path, "assets", StaticCustomName); } catch { return null; }
    }

    /// <summary>此刻该用哪个文件：自定义优先，其次默认，都没有返回 null（回落代码背景）。</summary>
    private static string StaticBgEffectivePath() {
        try {
            string c = StaticCustomPath();
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
            string d = StaticDefaultPath();
            if (!string.IsNullOrEmpty(d) && File.Exists(d)) return d;
            return null;
        } catch { return null; }
    }

    /// <summary>有没有静态背景可用（文件在）。神经图让位判据与糊图取源都读它：
    /// 「有静态背景或者视频背景时神经图绝不显示」里的"有静态背景"就是它。</summary>
    private static bool StaticBgAvailable() {
        try { return StaticBgEffectivePath() != null; } catch { return false; }
    }

    /// <summary>静态背景此刻该显示吗：有图，且动态背景还没接手。
    /// 没有动态背景（视频不可用）时当然显示；视频可用但还没稳定接管的那一段也让静态图顶着 ——
    /// 播放器重载前后会就绪与未就绪抖动，跟着它收放就是闪，所以让位判据用视频那边带迟滞的
    /// "已稳定接管"信号，而不是就绪本身；观感是静态图淡入视频。</summary>
    private static bool StaticBgVisible() {
        try { return StaticBgAvailable() && (!VideoBgUsable() || !_videoShowingStable); } catch { return false; }
    }

    /// <summary>当前该显示的那张图。走常驻图片缓存（失败也会缓存 null，不会每帧重试打磁盘）。</summary>
    private static Sprite StaticBgSprite() {
        try {
            string p = StaticBgEffectivePath();
            return string.IsNullOrEmpty(p) ? null : LoadAssetSprite(p);
        } catch { return null; }
    }

    /// <summary>换过图（重新选 / 恢复默认 / 刚下载完）后调：作废两份文件的图片缓存，之后按新文件重读。</summary>
    private static void StaticBgInvalidate() {
        try {
            InvalidateAssetSprite(StaticCustomPath());
            InvalidateAssetSprite(StaticDefaultPath());
        } catch { }
    }

    /// <summary>某一屏建好纯色底之后调它一次：挂一层全屏静态图（恒建，显隐由 TickStaticBg 决定）。
    /// insertAfter 是垫在谁上面（那一屏的纯色底）；它在代码背景之下，两层互斥，谁先谁后只影响极端交错帧的观感。
    /// 建出来先按当前状态摆好：该显示时立刻贴图、完全不透明（新开的屏不该从纯色淡入这一拍），其余先透明关着。
    /// 返回那一层，调用方在 park / 排序时需要它。</summary>
    private static Image AttachStaticBg(Transform parent, Transform insertAfter) {
        try {
            if (parent == null) return null;
            var go = new GameObject("BgStatic", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (insertAfter != null && insertAfter.parent == parent)
                go.transform.SetSiblingIndex(insertAfter.GetSiblingIndex() + 1);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.enabled = false;               // 没图 / 没轮到它时别渲染整屏方片（与视频层同一条性能惯例）
            Stretch(img.rectTransform, Vector2.zero, Vector2.one);
            _bgStaticLayers.Add(img);
            if (StaticBgVisible()) {
                var spr = StaticBgSprite();
                if (spr != null) {
                    img.sprite = spr;
                    img.color = new Color(1f, 1f, 1f, 1f);
                    img.enabled = true;
                }
            }
            return img;
        } catch (System.Exception e) {
            Lg.Ex("挂静态背景层失败（背景照旧）", e);
            return null;
        }
    }

    /// <summary>常驻段每帧调一次。排在 TickVideoBg 与 TickNetBg 之后（视频状态先定下来）、TickBgBlur 之前
    /// （糊图取源读到的就是这一帧的结论）。一层都没挂时第一行就返回，零开销。</summary>
    private static void TickStaticBg() {
        try {
            if (_bgStaticLayers.Count == 0) return;
            // 静态图掉没了（恢复默认、而默认图也不在）：神经图得放回来，否则这一屏没有背景。
            // 只在"上一帧有、这一帧没了"且视频也不可用时做一次；其余情况神经图的去留由 TickNetBg 管。
            bool avail = StaticBgAvailable();
            if (_staticWasAvailable && !avail && !VideoBgUsable()) NetBgShowAll(false);
            _staticWasAvailable = avail;

            bool want = StaticBgVisible();
            Sprite spr = null;
            bool show = false;
            if (want) { spr = StaticBgSprite(); show = spr != null; }
            float dt = Time.unscaledDeltaTime;
            for (int i = _bgStaticLayers.Count - 1; i >= 0; i--) {
                var ri = _bgStaticLayers[i];
                if (ri == null) { _bgStaticLayers.RemoveAt(i); continue; }
                float target = show ? 1f : 0f;
                bool rendering = ri.gameObject.activeInHierarchy && ri.canvas != null && ri.canvas.enabled;
                if (!rendering) {
                    // 这一屏没在画（画布被关 / 整组被收起）：当场落定、不走渐变，免得看不见的那段时间里
                    // 透明度自己爬来爬去；也不动 activeSelf，收起来的层由 park / unpark 自己开关。
                    if (Mathf.Abs(ri.color.a - target) > 0.0005f) ri.color = new Color(1f, 1f, 1f, target);
                    continue;
                }
                float a = ri.color.a;
                if (Mathf.Abs(a - target) > 0.0005f) {
                    float step = Mathf.Min(StaticBgFadeMaxStep, dt / StaticBgFadeSeconds);
                    a = Mathf.MoveTowards(a, target, step);
                } else {
                    a = target;
                }
                if (show && a > 0.002f && ri.sprite != spr) ri.sprite = spr;
                if (ri.enabled != (a > 0.002f)) ri.enabled = a > 0.002f;
                if (Mathf.Abs(ri.color.a - a) > 0.0005f) ri.color = new Color(1f, 1f, 1f, a);
            }
        } catch (System.Exception e) {
            Lg.Ex("静态背景刷新失败（背景照旧，不再重试）", e);
            _bgStaticLayers.Clear();
        }
    }

    // ---------- 设置页两条入口 ----------

    /// <summary>「选择图片」：把玩家选的图缩到不超过 1920×1080、存成 assets\bg_static_custom.png。
    /// 解码与缩放走 System.Drawing（Bitmap 构造函数会当场验文件，坏图抛异常、不落盘），
    /// 先写临时文件再换名：直接写目标路径的话，写到一半失败会把上一张自选图毁掉。</summary>
    private static void PickStaticBackground() {
        try {
            string path = ModernFileDialog.OpenFile("选择背景图片", "图片文件", "*.png;*.jpg;*.jpeg;*.bmp");
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) { ShowToast(Lang.Tr("这个图片文件不存在喵")); return; }
            string dst = StaticCustomPath();
            if (string.IsNullOrEmpty(dst)) { ShowToast(Lang.Tr("背景图片保存失败喵")); return; }
            string tmp = dst + ".tmp";
            int srcW, srcH, outW, outH;
            using (var src = new System.Drawing.Bitmap(path)) {
                srcW = src.Width; srcH = src.Height;
                float k = Mathf.Min(1f, Mathf.Min((float)StaticCustomMaxW / Mathf.Max(1, srcW),
                                                  (float)StaticCustomMaxH / Mathf.Max(1, srcH)));
                outW = Mathf.Max(1, Mathf.RoundToInt(srcW * k));
                outH = Mathf.Max(1, Mathf.RoundToInt(srcH * k));
                using (var final = new System.Drawing.Bitmap(outW, outH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = System.Drawing.Graphics.FromImage(final)) {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, outW, outH);
                    final.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            File.Copy(tmp, dst, true);
            try { File.Delete(tmp); } catch { }
            StaticBgInvalidate();
            Lg.Info("静态背景：已更换（源 " + srcW + "×" + srcH + " → 存 " + outW + "×" + outH + "）");
            ShowToast(Lang.Tr("背景已更换喵"));
        } catch (System.Exception e) {
            Lg.Ex("更换静态背景失败", e);
            ShowToast(Lang.Tr("背景图片加载失败喵"));
        }
    }

    /// <summary>「恢复默认背景」：删掉玩家自己选的那份，回到资源包的默认图。
    /// 默认图也没有（资源包没下完）时自然回落到代码背景，由 TickStaticBg 把神经图放回来。</summary>
    private static void RestoreDefaultStaticBackground() {
        try {
            string custom = StaticCustomPath();
            if (string.IsNullOrEmpty(custom) || !File.Exists(custom)) { ShowToast(Lang.Tr("现在用的就是默认背景喵")); return; }
            File.Delete(custom);
            StaticBgInvalidate();
            Lg.Info("静态背景：已恢复默认（自选那份已删，现在用 "
                + (StaticBgEffectivePath() != null ? "默认图" : "(默认图不在，回落代码背景)") + "）");
            ShowToast(StaticBgAvailable() ? "已恢复默认背景喵" : "自选那份已删掉，资源包下好前先用代码背景顶着");
        } catch (System.Exception e) {
            Lg.Ex("恢复默认背景失败", e);
            ShowToast(Lang.Tr("恢复默认背景失败，看日志喵"));
        }
    }
}
