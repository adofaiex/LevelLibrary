using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：大厅立绘（背景之上、界面之下的一张图）。
/// 三件事凑成这个功能：
///  ① 一层图：挂在大厅的中性层（ArkRoot，不挂逐顶点透视，所以立绘不会被倾斜），位置 / 大小 / 角度按落盘的值摆；
///  ② 一个圆钮：排在「隐藏 UI」那枚圆钮右边，点开用流式布局向下展开一个小界面（选图 + 四条滑条）；
///  ③ 跟着大厅走：视差位移每帧抄左区那一层（不吃它的透视角），左圆钮冻结视差时立绘一起停。
/// 落盘：settings.txt 的 ArkPortrait* 五个键（路径 / 角度 / X / Y / 大小），改完立刻存。
/// 图两份：默认那张随资源包分发（portrait_default.png），玩家自己选的存 portrait_custom.png（玩家数据、不进资源包）。</summary>
public static partial class PortalUI {

    /// <summary>玩家自选立绘的路径（settings: ArkPortraitPath，空 = 用随包默认那份）</summary>
    public static string PortraitPath;
    /// <summary>旋转角度（settings: ArkPortraitRot，0~360 度）</summary>
    public static float PortraitRot;
    /// <summary>左上角 X（settings: ArkPortraitX，1920 设计坐标；允许拖出屏一点）。默认落在中间偏左那块空白</summary>
    public static float PortraitX = PortraitDefX;
    /// <summary>左上角 Y（settings: ArkPortraitY）</summary>
    public static float PortraitY = PortraitDefY;
    /// <summary>高度（settings: ArkPortraitSize；宽度按图比例算）</summary>
    public static float PortraitSize = PortraitDefSize;

    private const string PortraitDefaultName = "portrait_default.png";
    private const string PortraitCustomName = "portrait_custom.png";

    /// <summary>初始位置与大小：直接取作者实调出来的这一组（他用滑条摆好之后要求设为默认：
    /// X=298 / Y=264 / 高 642，落在大厅中间偏左、上下都留得住，新闻与 VOICE 那几块压不到脸）。
    /// 换句话说这一版不再自己猜位置了 —— 猜了三轮（604 → 111 → 48 → 0）都被他叫停。</summary>
    public const float PortraitDefX = 298f;
    public const float PortraitDefY = 264f;
    public const float PortraitDefSize = 642f;

    private const float PortraitSizeMin = 120f, PortraitSizeMax = 2160f;   // 上限是旧值（1080）的两倍
    private const float PortraitXMin = -200f, PortraitXMax = 2040f;
    private const float PortraitYMin = -200f, PortraitYMax = 1240f;
    /// <summary>存图时最大边：立绘原图常常几千像素，整张留着每次进游戏都要重新解码一大张</summary>
    /// 与 PortraitSizeMax 同步翻倍（原 1440）：不然滑条拉到 2160 也是拿 1440 的图放大，白糊
    private const float PortraitTexMaxSide = 2880f;

    // ---- 运行时 ----
    private static Image _portraitImg;
    private static RectTransform _portraitBtnRt;         // 那颗圆钮（小界面靠它量"下方"到底在哪）
    private static Transform _portraitPanelHost;         // 小界面挂在哪（大厅画布顶层，所以压在任何大厅 UI 之上）
    private static Sprite _portraitSprite;
    private static string _portraitSpritePath;
    private static GameObject _portraitPanelGo;
    private static RectTransform _portraitPanelRt;
    private static CanvasGroup _portraitPanelCg;
    private static System.Action<float> _portraitApplyRot, _portraitApplyX, _portraitApplyY, _portraitApplySize;
    private static bool _portraitPanelOn;        // 目标态：开 / 收
    private static float _portraitPanelA;        // 当前透明度（收放动画自己推）
    private const float PortraitPanelFadeSec = 0.18f;
    private static bool _portraitShiftLogged;
    private static float _portraitShiftX;          // 视差横向位移（并进 anchoredPosition，不另开写入者）

    private static string PortraitDefaultPath() {
        try { return Path.Combine(Main.ModEntry.Path, "assets", PortraitDefaultName); } catch { return null; }
    }

    private static string PortraitCustomPath() {
        try { return Path.Combine(Main.ModEntry.Path, "assets", PortraitCustomName); } catch { return null; }
    }

    /// <summary>此刻该显示哪张图：自选优先，其次随包默认，都没有返回 null（这一屏就不画立绘）。</summary>
    private static string PortraitEffectivePath() {
        try {
            if (!string.IsNullOrEmpty(PortraitPath) && File.Exists(PortraitPath)) return PortraitPath;
            string d = PortraitDefaultPath();
            if (!string.IsNullOrEmpty(d) && File.Exists(d)) return d;
            return null;
        } catch { return null; }
    }

    /// <summary>当前该显示的那张图（按路径缓存；换图走 InvalidateArkPortrait 重读）</summary>
    private static Sprite PortraitSprite() {
        try {
            string p = PortraitEffectivePath();
            if (string.IsNullOrEmpty(p)) { InvalidateArkPortrait(); return null; }
            if (_portraitSprite != null && _portraitSpritePath == p) return _portraitSprite;
            InvalidateArkPortrait();
            var spr = LoadAssetSprite(p);
            if (spr == null) return null;
            _portraitSprite = spr;
            _portraitSpritePath = p;
            return spr;
        } catch { return null; }
    }

    /// <summary>换过图之后调：作废缓存，下次按新文件重读。</summary>
    private static void InvalidateArkPortrait() {
        try { InvalidateAssetSprite(PortraitCustomPath()); InvalidateAssetSprite(PortraitDefaultPath()); } catch { }
        _portraitSprite = null;
        _portraitSpritePath = null;
    }

    // ---------- 立绘那层图 ----------

    /// <summary>建立绘层：挂在大厅的中性层（ArkRoot）——它在两块透视层之下，所以背景被它压住、大厅按键又在它之上；
    /// 中性层不挂逐顶点透视，所以立绘不会被倾斜。它不吃射线（纯展示，别挡按键）。</summary>
    private static void BuildArkPortrait(Transform parent) {
        try {
            if (parent == null) return;
            var go = new GameObject("ArkPortrait", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 1f);
            _portraitImg = img;
            ApplyArkPortrait();
        } catch (System.Exception e) {
            Lg.Ex("建立绘层失败（大厅照旧）", e);
        }
    }

    /// <summary>把落盘的位置 / 大小 / 角度与图写到那层上（建完、换图、拖滑条都走这里）。
    /// 位置写在 anchoredPosition（设计坐标、左上角原点），视差位移写在 localPosition（两个通道互不打架）。</summary>
    private static void ApplyArkPortrait() {
        try {
            if (_portraitImg == null) return;
            var rt = _portraitImg.rectTransform;
            var spr = PortraitSprite();
            if (spr == null || spr.texture == null) {
                // 没有图（资源包没下完 / 玩家删了文件）：整层收掉，不能留一张空白方片在那儿
                if (_portraitImg.enabled) _portraitImg.enabled = false;
                return;
            }
            if (_portraitImg.sprite != spr) _portraitImg.sprite = spr;
            if (!_portraitImg.enabled) _portraitImg.enabled = true;
            float h = Mathf.Clamp(PortraitSize, PortraitSizeMin, PortraitSizeMax);
            float w = h * (spr.texture.width / Mathf.Max(1f, spr.texture.height));
            float x = Mathf.Clamp(PortraitX, PortraitXMin, PortraitXMax);
            float y = Mathf.Clamp(PortraitY, PortraitYMin, PortraitYMax);
            // 轴心放中间：角度是绕图心转的，轴心在左上角的话一转就飘出去
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            // 位置只有这一处写：滑条的 X / Y 与视差位移在这里合成同一个 anchoredPosition
            rt.anchoredPosition = new Vector2(x + w * 0.5f + _portraitShiftX, -(y + h * 0.5f));
            if (Mathf.Abs(rt.localEulerAngles.z - PortraitRot) > 0.01f)
                rt.localEulerAngles = new Vector3(0f, 0f, PortraitRot);
        } catch (System.Exception e) {
            Lg.Ex("应用立绘失败", e);
        }
    }

    /// <summary>视差位移：抄左区那一层的横向位移，立绘因此与大厅平行移动，但不继承它的倾斜。
    /// 冻结视差时 TickArkParallax 早退，这里也就跟着停 —— 与按键、新闻同一套手感。</summary>
    private static void ApplyArkPortraitShift(float shift) {
        try {
            // 位移只记下来、并进 ApplyArkPortrait 一起写进 anchoredPosition。
            // 早先这里每帧直接写 localPosition —— 而 localPosition 与 anchoredPosition 是同一个位置的
            // 两种表达：每帧被它盖一次，滑条刚写进去的 X / Y 当场就被抹掉（只有角度与大小能动，就是这么来的）。
            if (Mathf.Approximately(_portraitShiftX, shift)) return;
            _portraitShiftX = shift;
            ApplyArkPortrait();
            // 小界面不走这条：它在开面板那一刻按"圆钮的实测位置"落位，之后每帧量差值跟随
            // （见 TickArkPortraitFollow —— 圆钮在透视层里会被投影推开，光抄层位移对不齐）
            if (!_portraitShiftLogged) {
                _portraitShiftLogged = true;
                Lg.Info("立绘：跟着大厅做平行移动（横向位移抄左区那一层，自己不吃倾斜）");
            }
        } catch { }
    }

    // ---------- 圆钮 + 小界面 ----------

    /// <summary>那枚圆钮本体（建在左区那排圆钮右边）。点它开合小界面。</summary>
    private static GameObject BuildArkPortraitButton(Transform far) {
        try {
            var plate = MakeArkRoundButtonV(far, "ArkRoundPortrait",
                                           ArkRoundX + 2f * (ArkRoundSize + ArkRoundGap), ArkRoundY, ArkIconPortrait);
            _portraitBtnRt = plate.rectTransform;
            RegisterArkInteract(plate);
            RegisterArkClick(plate, ToggleArkPortraitPanel);
            return plate.gameObject;
        } catch (System.Exception e) {
            Lg.Ex("建立绘圆钮失败（大厅照旧）", e);
            return null;
        }
    }

    private const float PortraitPanelW = 360f;
    private const float PortraitPanelH = 236f;
    private const float PortraitRowH = 40f;
    /// <summary>收起来时的落位：比展开位往上挪一截（配合透明度，视觉上从圆钮那边"长出来"）。</summary>
    private static Vector2 PortraitPanelShut() {
        return PortraitPanelTarget() + new Vector2(0f, 34f);
    }

    /// <summary>量圆钮在屏幕上真正被画出来的位置（换算到中性层局部坐标）：out = 圆心，halfH = 半高。
    /// 为什么必须自己算投影：`GetWorldCorners` 给的是没投影的矩形（逐顶点投影发生在渲染时的
    /// ModifyMesh 里），直接拿它当屏幕位置，面板就会落到 x≈1200 那一侧去（用户两次报"跑到右侧"）。
    /// 这里把 ModifyMesh 的公式原样抄一遍算四角，再走 TransformPoint / InverseTransformPoint 换算，
    /// 于是量到的就是渲染器画出来的那个点。量不到（圆钮/透视还没建）退回估算值。</summary>
    private static Vector2 MeasurePortraitBtn(Transform host, out float halfH) {
        halfH = ArkRoundSize * 0.5f;
        Vector2 fallback = new Vector2(ArkRoundX + 2f * (ArkRoundSize + ArkRoundGap) + ArkRoundSize * 0.5f,
                                       ArkRoundSize * 0.5f);
        try {
            var rt = _portraitBtnRt;
            var layer = _arkLayerFar;
            if (rt == null || layer == null || host == null) return fallback;
            ArkPerspective persp = FirstArkPerspective(_arkPerspFar);
            float yaw = persp != null ? persp.Yaw : 0f;
            float pitch = persp != null ? persp.Pitch : 0f;
            Vector2 origin = persp != null ? persp.Origin : Vector2.zero;
            float dist = persp != null ? persp.Dist : 480f;

            Vector3 pv = layer.InverseTransformPoint(rt.TransformPoint(Vector3.zero));
            Vector2 pivot = new Vector2(pv.x, pv.y);
            var rect = rt.rect;
            Vector2 rp = rt.pivot;
            float cy = Mathf.Cos(yaw * Mathf.Deg2Rad), sinY = Mathf.Sin(yaw * Mathf.Deg2Rad);
            float cp = Mathf.Cos(pitch * Mathf.Deg2Rad), sinP = Mathf.Sin(pitch * Mathf.Deg2Rad);
            float d0 = Mathf.Max(1f, dist);
            float ox = origin.x, oy = origin.y;

            float minY = float.MaxValue, maxY = float.MinValue, sumX = 0f, sumY = 0f;
            for (int c = 0; c < 4; c++) {
                float cx = (c & 1) == 0 ? rect.xMin : rect.xMax;
                float cyy = (c & 2) == 0 ? rect.yMin : rect.yMax;
                Vector2 v = new Vector2(cx - rp.x, cyy - rp.y);            // 顶点坐标：相对轴心
                float rx = pivot.x + v.x - ox;
                float ry = pivot.y + v.y - oy;
                float x1 = rx * cy;
                float z1 = -rx * sinY;
                float y2 = ry * cp + z1 * sinP;
                float z2 = -ry * sinP + z1 * cp;
                float s = d0 / Mathf.Max(1f, d0 - z2);
                // 与 ModifyMesh 同一句：投影后的位置再减去轴心（顶点坐标本身就是相对轴心的，别再叠一次 v）
                Vector2 local = new Vector2(x1 * s + ox - pivot.x, y2 * s + oy - pivot.y);
                Vector3 inHost = host.InverseTransformPoint(rt.TransformPoint(local));
                sumX += inHost.x; sumY += inHost.y;
                if (inHost.y < minY) minY = inHost.y;
                if (inHost.y > maxY) maxY = inHost.y;
            }
            halfH = (maxY - minY) * 0.5f;
            return new Vector2(sumX * 0.25f, sumY * 0.25f);
        } catch (System.Exception e) {
            Lg.Ex("量立绘圆钮位置失败（面板退回估算落位）", e);
            return fallback;
        }
    }

    private static ArkPerspective FirstArkPerspective(ArkPerspective[] arr) {
        if (arr == null) return null;
        for (int i = 0; i < arr.Length; i++) if (arr[i] != null) return arr[i];
        return null;
    }

    /// <summary>展开落位：圆钮正下方，中线对齐、面板上沿落在按钮下沿之下。
    /// 这一步必须做坐标系换算：量到的点是在 host（面板所在那一层）的局部坐标里 —— 原点与 y 方向跟
    /// 面板自己的 anchoredPosition 不同（层是轴心居中 + y 向上，锚点是左上角原点 + y 向下），
    /// 直接拿局部坐标当锚点坐标用，面板会飞到屏幕另一侧。
    /// 换算：锚点归一(0,1) 在父局部坐标里的位置 = (锚点 − 轴心) ⊙ 尺寸，实测点减去它就是 anchoredPosition。
    /// 最后把横向夹回屏内，免得跟着视差推着跑时滑到最右侧那一列里去。</summary>
    private static Vector2 PortraitPanelTarget() {
        var parent = _portraitPanelHost != null ? _portraitPanelHost as RectTransform : null;
        if (parent == null) return new Vector2(0f, -(ArkRoundY + ArkRoundSize));
        float halfH;
        Vector2 p = MeasurePortraitBtn(parent, out halfH);
        Vector2 anchorLocal = new Vector2(-parent.rect.size.x * 0.5f, parent.rect.size.y * 0.5f);
        Vector2 topLeft = p - anchorLocal;                     // 锚点若正好落在圆钮圆心，anchoredPosition 就是它
        float x = topLeft.x - PortraitPanelW * 0.5f;
        float y = topLeft.y - halfH - 10f;                     // 往下 = 更负
        x = Mathf.Clamp(x, 24f, Mathf.Max(24f, parent.rect.size.x - PortraitPanelW - 24f));
        return new Vector2(x, y);
    }

    /// 面板开着的时候不再每帧跟着圆钮挪：落位只在开的那一刻按实测位置算一次。
    /// 早先每帧重算目标，看起来是"跟着按钮走"，实际后果是鼠标往滑条那边一动、面板也动 ——
    /// 指针底下的轨道跟着滑走，点击命中与玩家看到的位置对不上，拖动更是抓不住。
    /// 圆钮自己随视差漂移的那点距离（±几十像素）远小于这个代价，认了。

    /// <summary>建那个小界面（进大厅时建好但收着；坐标交给流式布局引擎，开合只登记目标位置）。
    /// 传进来的必须是没有透视的那一层：用户点名它要「不平行不斜角、直接长方形」，
    /// 而左区/右区挂着逐顶点透视，建在那里面整块板会被斜着画；
    /// 同时它要压在所有大厅 UI 之上（用户报过"被挡住"），所以挂的是大厅画布本身（ArkRoot / ArkFar / ArkNear 之后）。</summary>
    private static void BuildArkPortraitPanel(Transform host) {
        try {
            if (host == null) return;
            _portraitPanelHost = host;
            Vector2 openPos = PortraitPanelTarget();
            // 底板必须自己摆、不能用 MakeArkRect：那个函数按大厅的 CSS 口径把位置与尺寸都乘 ArkPx(1.2)，
            // 而面板内部的内容是按原始像素摆的 —— 混用两套单位会让底板比内容大一圈、
            // 内容挤在左上角，开合时流式布局再按原始坐标覆写位置又会跳一下。
            var plate = MakeImage(host, "ArkPortraitPanel", HexA(0x101018, 214));
            plate.sprite = SoftPlateSprite(0f);
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = true;
            SetAtPx(plate.rectTransform, openPos.x, openPos.y, PortraitPanelW, PortraitPanelH);
            RegisterArkInteract(plate);
            var rt = plate.rectTransform;
            _portraitPanelRt = rt;
            _portraitPanelGo = plate.gameObject;
            _portraitPanelCg = plate.gameObject.GetComponent<CanvasGroup>() ?? plate.gameObject.AddComponent<CanvasGroup>();
            _portraitPanelCg.alpha = 0f;
            _portraitPanelGo.SetActive(false);

            // 第 1 行：选图
            var pick = MakeImage(plate.transform, "Pick", ArkButtonDarkBg());
            pick.sprite = SoftPlateSprite(0f);
            pick.type = Image.Type.Sliced;
            pick.raycastTarget = true;
            SetAtPx(pick.rectTransform, 106f, 12f, PortraitPanelW - 118f, 30f);
            RegisterArkInteract(pick);
            var pt = MakeText(pick.transform, "T", ArkFsNormal, ArkTitleLight(), TextAnchor.MiddleCenter);
            SetAtPx(pt.rectTransform, 0f, 0f, PortraitPanelW - 104f, 30f);
            pt.text = Lang.Tr("选择立绘图片");
            RegisterArkClick(pick, PickArkPortrait);

            // 第 2~5 行：旋转角度 / X 位置 / Y 位置 / 大小（四条同款滑条）
            float y0 = 12f + PortraitRowH;
            _portraitApplyRot = MakeArkPortraitSlider(plate.transform, Lang.Tr("旋转角度"), y0, 0f, 360f, PortraitRot, "°",
                                  v => { PortraitRot = v; SavePortrait(); });
            _portraitApplyX = MakeArkPortraitSlider(plate.transform, Lang.Tr("X 位置"), y0 + PortraitRowH,
                                  PortraitXMin, PortraitXMax, PortraitX, "",
                                  v => { PortraitX = v; SavePortrait(); });
            _portraitApplyY = MakeArkPortraitSlider(plate.transform, Lang.Tr("Y 位置"), y0 + PortraitRowH * 2f,
                                  PortraitYMin, PortraitYMax, PortraitY, "",
                                  v => { PortraitY = v; SavePortrait(); });
            _portraitApplySize = MakeArkPortraitSlider(plate.transform, Lang.Tr("立绘大小"), y0 + PortraitRowH * 3f,
                                  PortraitSizeMin, PortraitSizeMax, PortraitSize, "px",
                                  v => { PortraitSize = v; SavePortrait(); });
        } catch (System.Exception e) {
            Lg.Ex("建立绘小界面失败（大厅照旧）", e);
        }
    }

    /// <summary>一条滑条：左侧标签 + 中间轨道 + 右侧数值，样式与设置页那批同款（小圆角轨道 + 主题色滑块）。
    /// 返回「把某个值刷进这条滑条」的入口（换图 / 回读设置之后用它把四条都对上）。</summary>
    private static System.Action<float> MakeArkPortraitSlider(Transform parent, string label, float top,
                                                              float min, float max, float value, string suffix,
                                                              System.Action<float> onSet) {
        const float LabelW = 84f;
        const float TrackW = 190f, TrackH = 10f, HandleW = 26f, HandleH = 26f;
        const float PadL = 14f;                      // 左边距：内容不贴板边
        float trackX = PadL + LabelW + 8f;
        float valX = trackX + TrackW + 6f;
        float valW = PortraitPanelW - valX - 12f;

        var lab = MakeText(parent, "L", ArkFsSmall, Color.white, TextAnchor.MiddleLeft);
        SetAtPx(lab.rectTransform, 0f, top, LabelW, 26f);
        lab.text = label;

        var track = MakeImage(parent, "Track", new Color(0f, 0f, 0f, 0.34f));
        track.sprite = SmallRoundSprite;
        track.type = Image.Type.Sliced;
        track.raycastTarget = false;          // 只负责看，命中交给下面那块更大的透明区
        var trt = track.rectTransform;
        SetAtPx(trt, trackX, top + (26f - TrackH) * 0.5f, TrackW, TrackH);

        // 命中区：滑块高 26、轨道只有 10，事件挂在轨道上的话只有压在轨道那一线才拖得动。
        // 这块透明区把整条滑块（连同它露在轨道上下的部分）整个包住，点哪都能开始拖。
        var hit = MakeImage(parent, "Hit", new Color(0f, 0f, 0f, 0f));
        hit.raycastTarget = true;
        var hrt2 = hit.rectTransform;
        SetAtPx(hrt2, trackX - 8f, top, TrackW + 16f, 26f);

        var handle = MakeImage(track.transform, "Handle",
                               new Color(HighlightFillColor.r, HighlightFillColor.g, HighlightFillColor.b, 0.55f));
        RegisterFillImage(handle);
        handle.sprite = SmallRoundSprite;
        handle.type = Image.Type.Sliced;
        handle.raycastTarget = false;
        var hrt = handle.GetComponent<RectTransform>();
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
        hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = new Vector2(HandleW, HandleH);

        var valT = MakeText(parent, "V", ArkFsSmall, Color.white, TextAnchor.MiddleRight);
        SetAtPx(valT.rectTransform, valX, top, valW, 26f);
        valT.raycastTarget = false;

        float travel = Mathf.Max(1f, (TrackW - HandleW) * 0.5f);
        System.Action<float> apply = v => {
            if (hrt == null || valT == null) return;
            float k = Mathf.InverseLerp(min, max, v);
            hrt.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, k), 0f);
            string s = Mathf.RoundToInt(v) + suffix;
            if (valT.text != s) valT.text = s;
        };
        apply(Mathf.Clamp(value, min, max));

        UnityEngine.Events.UnityAction<BaseEventData> onDrag = bed => {
            var ed = bed as PointerEventData;
            if (ed == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(trt, ed.position, ed.pressEventCamera, out Vector2 local)) return;
            // 归一化必须按轨道自己的 rect 来：它的轴心在左上角（SetAtPx），
            // local.x 是"离左边缘多远"（0~TrackW），早先按 ±TrackW/2 算等于点了左半边永远取下限、
            // 右半边永远取上限 —— 滑条等于只有两个值。
            var rr = trt.rect;
            float k = Mathf.Clamp01(Mathf.InverseLerp(rr.xMin, rr.xMax, local.x));
            float v = Mathf.Clamp(Mathf.Round(min + k * (max - min)), min, max);
            apply(v);
            ApplyArkPortrait();          // 立绘本身立刻跟手（角度 / 位置 / 大小三样都在这里落到那层上）
            try { onSet(v); } catch { }
        };
        var et = hit.gameObject.AddComponent<EventTrigger>();
        AddTrigger(et, EventTriggerType.PointerDown, onDrag);
        AddTrigger(et, EventTriggerType.Drag, onDrag);
        return apply;
    }

    /// <summary>四条滑条的值刷新（换图 / 落盘回读之后调一次，让文字与滑块对上）</summary>
    private static void RefreshArkPortraitSliders() {
        try {
            if (_portraitApplyRot != null) _portraitApplyRot(PortraitRot);
            if (_portraitApplyX != null) _portraitApplyX(PortraitX);
            if (_portraitApplyY != null) _portraitApplyY(PortraitY);
            if (_portraitApplySize != null) _portraitApplySize(PortraitSize);
        } catch { }
    }

    /// <summary>开面板那一刻打一行几何诊断：量到的圆钮位置、落位目标、面板最终矩形、四条轨道。
    /// 面板与圆钮分属不同坐标系（一个是渲染投影后的位置、一个是画布锚点坐标），
    /// 这一行是"位置对不上"时唯一能定量说话的证据，别删。</summary>
    private static void LogPortraitPanelGeom(Vector2 to) {
        try {
            float hh;
            Vector2 btn = MeasurePortraitBtn(_portraitPanelHost, out hh);
            var prt = _portraitPanelRt;
            var corners = new Vector3[4];
            prt.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++) {
                minX = Mathf.Min(minX, corners[i].x); maxX = Mathf.Max(maxX, corners[i].x);
                minY = Mathf.Min(minY, corners[i].y); maxY = Mathf.Max(maxY, corners[i].y);
            }
            Lg.Info("立绘小界面几何：圆钮实测(层局部)=" + btn.ToString("F0")
                + " 半高=" + hh.ToString("F0") + " | 目标 anchoredPos=" + to.ToString("F0")
                + " | 面板锚点=" + prt.anchoredPosition.ToString("F0")
                + " 世界矩形 x " + minX.ToString("F0") + "~" + maxX.ToString("F0")
                + " y " + minY.ToString("F0") + " ~" + maxY.ToString("F0")
                + " | 画布=" + (_portraitPanelHost != null && _portraitPanelHost is RectTransform
                    ? ((RectTransform)_portraitPanelHost).rect.size.ToString("F0") : "?")
                + " 锚点=" + (prt.anchorMin.ToString("F0")) + " 轴心=" + prt.pivot.ToString("F0"));
        } catch (System.Exception e) {
            Lg.Ex("立绘小界面几何诊断失败", e);
        }
    }

    /// <summary>开合小界面：位置走流式布局（往下展开），透明度自己推。
    /// 开之前先开一个让位窗口 —— 引擎只在窗口里让登记的坐标变化滑过去，平时一律直接落位。</summary>
    private static void ToggleArkPortraitPanel() {
        try {
            if (_portraitPanelRt == null || _portraitPanelCg == null) return;
            _portraitPanelOn = !_portraitPanelOn;
            Vector2 to = _portraitPanelOn ? PortraitPanelTarget() : PortraitPanelShut();
            if (_portraitPanelOn && _portraitPanelGo != null && !_portraitPanelGo.activeSelf)
                _portraitPanelGo.SetActive(true);
            FlowWindow("立绘小界面", PortraitPanelFadeSec + 0.05f);
            FlowTo(_portraitPanelRt, to.x, to.y);
            if (_portraitPanelOn) LogPortraitPanelGeom(to);
            Lg.Info("立绘：小界面" + (_portraitPanelOn ? "展开" : "收起")
                + "（位置走流式布局引擎）");
        } catch (System.Exception e) {
            Lg.Ex("开合立绘小界面失败", e);
        }
    }

    /// <summary>常驻段：小界面透明度 + 立绘换图自愈（图片解码失败 / 被删了就收掉那层）。</summary>
    private static void TickArkPortrait() {
        try {
            if (_portraitPanelCg != null) {
                float target = _portraitPanelOn ? 1f : 0f;
                float dt = Time.unscaledDeltaTime;
                if (!Mathf.Approximately(_portraitPanelA, target)) {
                    _portraitPanelA = Mathf.MoveTowards(_portraitPanelA, target, dt / PortraitPanelFadeSec);
                    _portraitPanelCg.alpha = _portraitPanelA;
                }
                if (!_portraitPanelOn && _portraitPanelA <= 0.001f && _portraitPanelGo != null && _portraitPanelGo.activeSelf)
                    _portraitPanelGo.SetActive(false);
                if (_portraitPanelGo != null) _portraitPanelCg.blocksRaycasts = _portraitPanelA > 0.5f;
            }
            if (_portraitImg != null && !_portraitImg.enabled && PortraitEffectivePath() != null)
                ApplyArkPortrait();       // 图是刚到盘上的（资源包下完 / 刚选完）：补建一次
        } catch { }
    }

    /// <summary>「选择立绘图片」：把玩家选的图缩到不超过 1440 那条边、存进 assets\portrait_custom.png，立刻生效。
    /// 解码走 System.Drawing（Bitmap 构造函数当场验文件，坏图抛异常、不落盘）；先写临时文件再换名。</summary>
    private static void PickArkPortrait() {
        try {
            string path = ModernFileDialog.OpenFile("选择立绘图片", "图片文件", "*.png;*.jpg;*.jpeg;*.bmp");
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) { ShowToast(Lang.Tr("这个图片文件不存在喵")); return; }
            string dst = PortraitCustomPath();
            if (string.IsNullOrEmpty(dst)) { ShowToast(Lang.Tr("立绘保存失败喵")); return; }
            string tmp = dst + ".tmp";
            int sw, sh, ow, oh;
            using (var src = new System.Drawing.Bitmap(path)) {
                sw = src.Width; sh = src.Height;
                float k = Mathf.Min(1f, PortraitTexMaxSide / Mathf.Max(sw, sh));
                ow = Mathf.Max(1, Mathf.RoundToInt(sw * k));
                oh = Mathf.Max(1, Mathf.RoundToInt(sh * k));
                using (var final = new System.Drawing.Bitmap(ow, oh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = System.Drawing.Graphics.FromImage(final)) {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, ow, oh);
                    final.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            File.Copy(tmp, dst, true);
            try { File.Delete(tmp); } catch { }
            PortraitPath = dst;
            SavePortrait();
            InvalidateArkPortrait();
            ApplyArkPortrait();
            RefreshArkPortraitSliders();
            Lg.Info("立绘：已更换（源 " + sw + "×" + sh + " → 存 " + ow + "×" + oh + "）");
            ShowToast(Lang.Tr("立绘已更换喵"));
        } catch (System.Exception e) {
            Lg.Ex("更换立绘失败", e);
            ShowToast(Lang.Tr("立绘图片读取失败喵"));
        }
    }

    /// <summary>立绘的五个值改完立刻落盘（滑条每拖一下都调，所以这里只写这五个键）。</summary>
    private static void SavePortrait() {
        try {
            if (PortraitRot < 0f) PortraitRot = 0f;
            if (PortraitRot > 360f) PortraitRot = 360f;
            PortraitSize = Mathf.Clamp(PortraitSize, PortraitSizeMin, PortraitSizeMax);
            PortraitX = Mathf.Clamp(PortraitX, PortraitXMin, PortraitXMax);
            PortraitY = Mathf.Clamp(PortraitY, PortraitYMin, PortraitYMax);
            ApplyArkPortrait();
            AutoFeatures.Save();
        } catch (System.Exception e) {
            Lg.Ex("落盘立绘设置失败", e);
        }
    }
}
