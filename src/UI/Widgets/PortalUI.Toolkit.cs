using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：Toolkit（由 PortalUI.cs 拆分而来，仍属同一个 partial class）
/// 图片加载与缓存、热键判定、按画布定位的摆位函数、自建画布登记表，以及 Image/Button/Text 三个基础控件工厂。</summary>
public static partial class PortalUI {

    /// <summary>常驻图片缓存（路径 到 Sprite）：头像/图标这类固定素材只解码一次（之前每次开面板都重新读盘 + 解码 + 建纹理）。</summary>
    private static readonly Dictionary<string, Sprite> _spriteCache = new();

    /// <summary>带缓存的图片加载（推荐入口）：同一路径只解码一次。
    /// 读不到时报一次缺失，且只对资源包里的素材出声（玩家数据/可选素材一律安静）。</summary>
    private static Sprite LoadAssetSprite(string path) {
        if (string.IsNullOrEmpty(path)) return null;
        if (_spriteCache.TryGetValue(path, out var cached)) return cached;
        var spr = LoadImageFromPath(path);
        _spriteCache[path] = spr;   // 失败也缓存（null），避免反复尝试打磁盘
        if (spr == null) AssetPack.NoteMissingPath(path);
        return spr;
    }

    /// <summary>清空图片缓存（换自定义头像/背景后调用，强制重新读取）</summary>
    private static void InvalidateAssetSprite(string path) {
        if (!string.IsNullOrEmpty(path)) _spriteCache.Remove(path);
    }

    /// <summary>从任意图片文件加载 Sprite（png/jpg/bmp；翻转行序）。</summary>
    private static Sprite LoadImageFromPath(string path) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var spr = LoadImageManaged(path);
        if (LogVerbose && spr != null) {
            Lg.Info($"[LevelLib] 图片加载 {System.IO.Path.GetFileName(path)}: {sw.ElapsedMilliseconds}ms ({(spr.texture != null ? spr.texture.width + "x" + spr.texture.height : "?")})");
        }
        return spr;
    }

    /// <summary>托管解码通道（本地 Unity profile 里 ImageConversion.LoadImage 只有 ReadOnlySpan 重载、编译不过，所以走不到原生解码）。
    /// 逐像素只做 BGRA到RGBA 换序 + 整块上传，不用 Color32 数组（省一趟拷贝）；纹理格式必须留 RGBA32，
    /// 换 ARGB32 会在 Linear 色彩空间下整体偏色。</summary>
    private static Sprite LoadImageManaged(string path) {
        var swAll = System.Diagnostics.Stopwatch.StartNew();
        try {
            // 大图走"缩图缓存"通道：缓存文件命中就直接读小的，命中不了才解码大图 + 生成缓存
            string cachePath = ImageCachePath(path);
            if (cachePath != null && File.Exists(cachePath)) {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var cached = DecodeToSprite(cachePath);
                long tCache = sw.ElapsedMilliseconds;
                if (cached != null) {
                    // 分段计时：读缓存(含 PNG 解码) vs 总计 —— 用来定位到底哪一段在吃时间
                    Lg.Info($"[LevelLib] 图片加载(缓存) {Path.GetFileName(path)}: 读缓存={tCache}ms 总计={swAll.ElapsedMilliseconds}ms");
                    return cached;
                }
                // 缓存坏了就重来
                try { File.Delete(cachePath); } catch { }
            }

            System.Drawing.Bitmap src = null;
            System.Drawing.Bitmap bmp = null;
            bool ownBmp = false;
            try {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                src = new System.Drawing.Bitmap(new MemoryStream(File.ReadAllBytes(path)));
                bmp = DownscaleIfHuge(src, Screen.width, Screen.height);
                ownBmp = !ReferenceEquals(bmp, src);
                long tDecode = sw.ElapsedMilliseconds;
                var spr = DecodeToSprite(bmp);
                long tConvert = sw.ElapsedMilliseconds;
                // 生成缓存供下次直接使用（失败不影响本次显示）
                if (spr != null && cachePath != null && ownBmp) {
                    try { bmp.Save(cachePath, System.Drawing.Imaging.ImageFormat.Png); } catch { }
                }
                Lg.Info($"[LevelLib] 图片加载(原图) {Path.GetFileName(path)}: 解码+缩图={tDecode}ms 转换={tConvert - tDecode}ms"
                    + $" 存缓存={sw.ElapsedMilliseconds - tConvert}ms 总计={swAll.ElapsedMilliseconds}ms"
                    + $" 尺寸={(bmp != null ? bmp.Width + "x" + bmp.Height : "?")}");
                return spr;
            } finally {
                if (ownBmp && bmp != null) bmp.Dispose();
                if (src != null) src.Dispose();
            }
        } catch (System.Exception e) {
            // 图片解码失败用户是能直接看到的（背景/头像空白），所以要弹窗而不是只记日志
            ReportError("图片加载失败：" + path, e);
        }
        return null;
    }

    /// <summary>解码 Bitmap 文件 到 Sprite</summary>
    private static Sprite DecodeToSprite(string path) {
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var bmp = new System.Drawing.Bitmap(ms)) {
            return DecodeToSprite(bmp);
        }
    }

    /// <summary>解码 Bitmap 到 Sprite（逐像素 BGRA到RGBA + 整块上传；行序要翻）</summary>
    private static Sprite DecodeToSprite(System.Drawing.Bitmap bmp) {
        int w = bmp.Width, h = bmp.Height;
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try {
            int stride = data.Stride;
            var all = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, all, 0, all.Length);
            var body = new byte[w * h * 4];
            for (int y = 0; y < h; y++) {
                int srcRow = y * stride;
                int dstRow = (h - 1 - y) * w * 4;   // Bitmap 行序自上而下 → Unity 纹理自下而上
                for (int x = 0; x < w; x++) {
                    int si = srcRow + x * 4;
                    int di = dstRow + x * 4;
                    body[di] = all[si + 2];       // R
                    body[di + 1] = all[si + 1];   // G
                    body[di + 2] = all[si];       // B
                    body[di + 3] = all[si + 3];   // A
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            PerfDiag.Tag(tex, "img");   // LL_ 前缀 = 我们建的（诊断里按前缀数存活数）
            tex.LoadRawTextureData(body);
            tex.Apply();
            return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f)), "img");
        } finally {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>缩图缓存文件路径（只有"明显大于屏幕"的图才需要缓存；小图返回 null = 不走缓存）</summary>
    private static string ImageCachePath(string path) {
        try {
            using (var fs = File.OpenRead(path)) {
                if (fs.Length < 400 * 1024) return null;   // 小于 400KB 的图解码本来就快，不值得缓存
            }
            string dir = Path.Combine(Main.ModEntry.Path, "imgcache");
            Directory.CreateDirectory(dir);
            // 用"原图路径的哈希 + 尺寸"当文件名：原图换了（大小或时间变了）自然就是新文件
            var fi = new FileInfo(path);
            string key = path.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            int hcode = 17;
            for (int i = 0; i < key.Length; i++) hcode = hcode * 31 + key[i];
            return Path.Combine(dir, "bg_" + (hcode & 0x7FFFFFFF).ToString("x8") + ".png");
        } catch { return null; }
    }

    /// <summary>图片明显大于目标尺寸时等比缩小（高质量插值），否则原样返回。
    /// 只用于"缩放后再逐像素处理"这条路径（背景本来就是整图拉伸，观感不变）。</summary>
    private static System.Drawing.Bitmap DownscaleIfHuge(System.Drawing.Bitmap src, int maxW, int maxH) {
        try {
            if (maxW <= 0 || maxH <= 0) return src;
            if (src.Width <= maxW && src.Height <= maxH) return src;
            float k = Mathf.Min((float)maxW / src.Width, (float)maxH / src.Height);
            int nw = Mathf.Max(1, Mathf.RoundToInt(src.Width * k));
            int nh = Mathf.Max(1, Mathf.RoundToInt(src.Height * k));
            var dst = new System.Drawing.Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(dst)) {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(src, new System.Drawing.Rectangle(0, 0, nw, nh));
            }
            return dst;
        } catch {
            return src;   // 缩放失败就按原图走，不影响功能
        }
    }

    private static void AddTrigger(EventTrigger et, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> cb) {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(cb);
        et.triggers.Add(entry);
    }

    /// <summary>判断绑定键是否被按下：功能键走 GetKeyDown，字符键再拿 inputString 兜底
    /// （IME 下 GetKeyDown(BackQuote) 不响，`~` 这类字符只能靠字符匹配）。</summary>
    private static bool IsHotkeyPressed(KeyCode k) {
        if (UiInput.KeyDown(k)) return true;
        string s = UiInput.InputString;
        if (!string.IsNullOrEmpty(s)) {
            for (int i = 0; i < s.Length; i++) {
                if (CharToKeyCode(s[i]) == k) return true;
            }
        }
        return false;
    }

    /// <summary>键名显示（友好名：UpArrow到↑ 等）</summary>
    private static string KeyDisplay(KeyCode k) {
        if (k == KeyCode.None) return "未设置";
        if (k == KeyCode.UpArrow) return "↑";
        if (k == KeyCode.DownArrow) return "↓";
        if (k == KeyCode.LeftArrow) return "←";
        if (k == KeyCode.RightArrow) return "→";
        if (k == KeyCode.Space) return "空格";
        if (k == KeyCode.Return) return "回车";
        if (k == KeyCode.Escape) return "Esc";
        return k.ToString();
    }

    /// <summary>按画布左上角基准摆放：单点锚 + 固定尺寸，位置一定是"距左上角多少像素"。</summary>
    private static void SetAtPx(RectTransform rt, float left, float top, float w, float h) {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(left, -top);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>当前画布的高（画布单位），拿不到就退回 1080（面板构建最早那几帧画布还没量过）。它不是恒 1080：
    /// 缩放口径是"两轴取小"（见 UiFitScale）——画布单位永远不小于 1920×1080 设计面，
    /// 高 = max(1080, 1080 × 屏幕高 / 屏幕宽)（16:9到1080 / 16:10到1200 / 4:3到1440；
    /// 比 16:9 更宽的屏高恒 1080、宽变宽）。
    /// 所以"距画布顶固定多少像素"的底边在非 16:9 上会空出一截；正解是把该到底的元素做成拉伸锚点（见 `SetRectPxV`），不要每帧去测分辨率。</summary>
    private static float CanvasH() {
        try {
            if (_panelCanvas != null) {
                var rt = _panelCanvas.GetComponent<RectTransform>();
                if (rt != null && rt.rect.height > 1f) return rt.rect.height;
            }
        } catch { }
        return RefH;
    }

    /// <summary>竖直"拉伸"的摆法：上边距 topPx、下边距 bottomPx（0 = 一直铺到父节点下边），水平仍是 leftPx / 宽 wPx。
    /// 画布变高或窗口改尺寸时自己长到底，不需要任何监测代码。
    /// 前提是父节点本身得有"画布那么高"（页面带 / 页槽位已经是拉伸的）；底边边距一律走 `BandBottomMargin` / `LibBottomMargin`，别写绝对 y。</summary>
    private static void SetRectPxV(RectTransform rt, float leftPx, float topPx, float wPx, float bottomPx) {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(leftPx, -topPx);
        rt.sizeDelta = new Vector2(wPx, -(topPx + bottomPx));
    }

    /// <summary>底部对齐的固定尺寸摆法：左边 leftPx、下边距父底 bottomPx、尺寸 w×h。
    /// 给本来就贴着底板下边的东西用；写死绝对 y 的话画布一高它就浮在半空。</summary>
    private static void SetRectPxB(RectTransform rt, float leftPx, float bottomPx, float wPx, float hPx) {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(leftPx, bottomPx);
        rt.sizeDelta = new Vector2(wPx, hPx);
    }

    private static void ApplyVeil() {
        if (_panelVeilImg != null) _panelVeilImg.color = new Color(0f, 0f, 0f, _veilCur);
    }

    private static Sprite GetRoundedFrameSprite() {
        if (_roundedFrameSprite != null) return _roundedFrameSprite;
        // 贴图尺寸不能单独改：border 跟着放大，9-slice 几何一起翻，界面形状直接崩。
        // 第三个参数 = 带宽 + 1（内边缘按 d + bw - 1 判定），所以 3.5 的视觉线宽要传 4.5。
        try {
            _roundedFrameSprite = CreateRoundedFrameSprite(18, 7, 4.5f);
            return _roundedFrameSprite;
        } catch { return null; }
    }

    /// <summary>弹窗与提示的入场：整体从下方浮上来、同时从透明淡到原样（原生协程，不依赖 DOTween，不带缩放也不带回弹）。
    /// 自动补一个 CanvasGroup 管整棵子树的透明度；plate 那张图自己也要跟着走（磨砂按"盒子透出几成"算），
    /// veil 是弹窗那层遮罩（提示没有遮罩就传 null）。返回那个 CanvasGroup，收起时要淡出的话拿它用。</summary>
    private static CanvasGroup AnimateFloatIn(RectTransform rt, Image plate, Image veil, float rise, float dur) {
        if (rt == null) return null;
        try {
            var grp = rt.GetComponent<CanvasGroup>();
            if (grp == null) grp = rt.gameObject.AddComponent<CanvasGroup>();
            FloatAnimator.FloatIn(rt, plate, veil, grp, rise, dur);
            return grp;
        } catch { return null; }
    }

    /// <summary>我们建过的所有 overlay 画布，遍历"我们自己界面元素"的地方一律问它
    /// （层高判断 / 字形预热 / 切语言重刷 / 公祭日灰度），所以谁建画布谁登记（见 `TrackOurCanvas`）——漏一个，上面那几处一起漏认它。</summary>
    private static readonly List<Canvas> _ourCanvases = new List<Canvas>();

    /// <summary>把"我们自己建的 overlay 画布"登记进表（`MakeCanvas` 内部会调它）。
    /// 自建画布必须走 `MakeCanvas`：私下抄一份建画布的代码会漏掉登记，表现是那扇窗开着时开始界面照样响应上滑、字形预热和切语言都漏它。</summary>
    internal static void TrackOurCanvas(Canvas c) {
        if (c == null) return;
        try { if (!_ourCanvases.Contains(c)) _ourCanvases.Add(c); } catch { }
    }

    /// <summary>把"我们自己全部画布里"的某种组件收进 `into`，含未激活的组件（预热过的个人页就是藏着的）。
    /// 判据与原来的"全场景搜 Canvas 再按 `LevelLib_` 前缀筛"一致，但不用在三万对象的场景里付那 250ms 级的搜索。</summary>
    internal static void CollectOurGraphics<T>(List<T> into) where T : Component {
        if (into == null) return;
        for (int i = _ourCanvases.Count - 1; i >= 0; i--) {
            var c = _ourCanvases[i];
            if (c == null) { _ourCanvases.RemoveAt(i); continue; }    // 画布没了就顺手清
            try { c.GetComponentsInChildren(true, into); } catch { }
        }
    }

    internal static Canvas MakeCanvas(string name, int sortOrder, Camera camera = null, float planeDistance = 0f) {
        // RectTransform 必须显式给：overlay 模式 Unity 会自己补一个，
        // 相机模式不会 —— 少了它画布的内容区是 0x0，子元素全画不出来（整屏空白）。
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        var canvas = go.GetComponent<Canvas>();
        if (camera != null) {
            // 相机模式：uGUI 的透视只能由相机做投影（主界面那套斜切/分层就靠它，见 PortalUI.ArkView.cs）。
      // 这个函数是全项目唯一建画布的地方（两处 new GameObject 会被审计判红），所以两种模式合在一起。
   canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
 canvas.planeDistance = planeDistance > 0f ? planeDistance : Mathf.Abs(camera.transform.position.z);
            // 只有相机模式需要显式撑满：overlay 模式 Unity 每帧自己铺满，
            // 写了反而把已有画布（含面板根）的锚点改坏，整块面板跟着错位。
            // 边距是像素，四边都给 0 才是刚好铺满（给 one 会各外扩 1 像素）。
            Stretch(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        } else {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        canvas.sortingOrder = sortOrder;
 TrackOurCanvas(canvas);            // 登记（销毁的画布会在扫描时顺手清掉）
        var scaler = go.GetComponent<CanvasScaler>();
        // 缩放口径 = Expand（两轴取小，与 UiFitScale 同一公式）：画布单位永远不小于 1920×1080 的设计面。
        // 为什么是 Expand 不是 MatchWidthOrHeight：后者按宽度配时，比 16:9 更宽的屏（21:9 带鱼屏 / 超宽窗口）
        // 会把画布压得比 1080 矮（21:9 只剩 810 单位），设计面底部两成内容直接被裁掉 ——
        // 玩家报的「换分辨率之后显示不全」就是它。Expand 取两轴比例的小者：
        // 比 16:9 更高/更窄的屏（16:10、4:3）与旧口径数值完全一致（纵向多出的空间照旧靠
        // 拉伸锚点吃掉）；更宽的屏画布高度恒定 1080、宽度变宽，设计面永远装得下。
        // 缩放系数由 CanvasScaler 每帧自己维护（屏幕尺寸一变下一帧就重算），
        // 不再走 ConstantPixelSize + TickUiScale 手动盖章的那套自管轮子。
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0f;   // Expand 模式下不参与计算，给 0 消歧义
        return canvas;
    }

    /// <summary>全项目统一的画布缩放系数：min(屏幕宽/1920, 屏幕高/1080)。
    /// 保证画布单位里至少有 1920×1080 那么大的一块设计面 —— 任何一边都不小于设计稿，
    /// 界面在任意宽高比下都装得下（多出来的空间由拉伸锚点与背景层吃掉）。
    /// 画布本身已切 ScaleWithScreenSize(Expand)（公式与此处一致、由 CanvasScaler 自维护）；
    /// 这个函数还在被「屏幕像素 ⇆ 画布单位」的手动换算处引用（如 ArkView 的像素系数），语义别动。</summary>
    internal static float UiFitScale() {
        try {
            float sw = Mathf.Max(1f, Screen.width), sh = Mathf.Max(1f, Screen.height);
            float s = Mathf.Min(sw / RefW, sh / RefH);
            if (s > 0.0001f) return s;
        } catch { }
        return 1f;
    }

    private static Image MakeImage(Transform parent, string name, Color color) {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.sprite = RoundedSprite;      // 圆角
        img.type = Image.Type.Sliced;    // 九宫格拉伸保持圆角
        // 入场动画不在这里挂：建盒子这一刻位置还没定，落点要等布局摆完才读得到，
        // 所以由 MakeDialogBox（弹窗）与调用方（提示）在摆好之后自己调 AnimateFloatIn。
        return img;
    }

    private static Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color) {
        var img = MakeImage(parent, name, color);
        img.rectTransform.anchoredPosition = anchoredPos;
        img.rectTransform.sizeDelta = size;
        // 布局组里保持固定尺寸（否则 Horizontal/VerticalLayoutGroup 会把宽度压成 preferred=0）
        var le = img.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = size.x;
        le.preferredHeight = size.y;
        le.minWidth = size.x;      // 强制锁定最小尺寸，布局压不扁（正方形按钮不会被压成长方形）
        le.minHeight = size.y;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        // 过渡交给交互态层一个写手：uGUI 自己那套 ColorTint 也写同一个图元的颜色，
        // 两套叠在一起就是"进去亮一下（uGUI 染的）、随后被交互态层覆盖掉（看着就是消失）"。
        btn.transition = Selectable.Transition.None;
        RegisterUiTarget(btn, img);      // 悬停/按下/禁用三态统一由交互态层驱动（见 PortalUI.UiStates）
        return btn;
    }

    /// <summary>清掉 uGUI 的当前选中对象。按钮点完还留在选中态，而 Enter / 空格会被 EventSystem 当成 Submit
    /// 把那一下重放一遍（切页、开公告），游戏的输入闸门管不到 uGUI 自己的输入模块。只在"按钮点完"那一刻调，
    /// 别去动输入框那种本来就该被选中的。</summary>
    private static void DeselectUi() {
        try {
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
        } catch { }
    }

  /// <summary>大标题的投影：往右下错开几个像素的一层深色副本。错位量跟着字号走
  /// （大标题按同一个比例才像影子，小标题保底 4px）；界面这套 SetRectPx 的 y 是从块顶往下数，
  /// 挂组件的投影 y 向上为正，用的时候取负号。</summary>
    private const float TitleShadowK = 0.05f;
    private const float TitleShadowMin = 4f;
    private const float TitleShadowMax = 12f;
    private const float TitleShadowAlpha = 0.42f;   // 最浓处；再深就不像投影、像描边

  /// <summary>投影的错位量：按字号算。组件那条路与开始界面另铺的那层都从这儿取，
  /// 免得两处各写一个数、改着改着就对不上了。</summary>
    private static float TitleShadowPx(int size) {
        return Mathf.Clamp(Mathf.Round(size * TitleShadowK), TitleShadowMin, TitleShadowMax);
    }

  /// <summary>给一个大标题挂上投影（uGUI 的 Shadow 组件会把"当前所有顶点"再画一遍，
  /// 挂到已经用 Outline 加粗过的标题上时，那层同色描边会把投影也描成主题色、投影就没了，
  /// 所以挂了 Outline 的标题不要走这里，改成另铺一层深色文字当投影）。
  /// 独立成类的界面（生日、默哀）也调这一份，别再各自写一套投影参数。</summary>
    public static void AddTitleShadow(Text title) {
        if (title == null) return;
        float d = TitleShadowPx(title.fontSize);
        var ts = title.gameObject.AddComponent<Shadow>();
        ts.effectColor = new Color(0f, 0f, 0f, TitleShadowAlpha);
        ts.effectDistance = new Vector2(d, -d);
    }

    private static Text MakeText(Transform parent, string name, int size, Color color, TextAnchor align) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = _uiFont;
        GuardFontOnCreate(text);   // 字体自检：建字这一瞬间就查一遍（自检按钮只看得到"现在"，看漏的正是"当时"）
        text.fontSize = UiFontPx(size);   // 设计尺上的数按「界面字体大小」换算成实际像素
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;   // 默认不换行：需要换行的调用方必须自己改成 Wrap
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = Lang.Tr(name);   // 默认是 GameObject 名（调用方不覆盖就有字）；顺便过一遍翻译表
        RegisterUiFontText(text, size);   // 记下设计尺字号：玩家改「界面字体大小」时按它重刷
        return text;
    }

  /// <summary>建一段主标题文字，顺手挂上投影。页面标题、面板标题、弹窗标题都从这儿走，
  /// 不要再各处写一遍"MakeText 加 AddTitleShadow"——漏掉一处就是两套观感。</summary>
    private static Text MakeTitle(Transform parent, string name, int size, Color color, TextAnchor align) {
        var t = MakeText(parent, name, size, color, align);
        AddTitleShadow(t);
        return t;
    }

    private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax) {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

}
