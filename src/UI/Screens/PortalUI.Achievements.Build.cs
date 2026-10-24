// [split-of] PortalUI.Achievements.cs lines 1-650 body 12-555

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 分片：成就页的建页、左栏与自检。</summary>
public static partial class PortalUI {

    // ---------- 布局常量（1920×1080 参考分辨率，与设置页/库页同一套坐标） ----------
    // 左栏遮罩 100~400、右区遮罩 420~1820：屏幕左右各留 100，整体才不会看着偏右。
    // 顶边一律写成 TopNavH + 偏移（导航排占 20~70，内容从 92 起）—— 别写死 140。
    private const float AchPanelTop = TopNavH, AchPanelBottom = 1040f;
    private const float AchLeftPanelX = 62f, AchLeftPanelW = 300f;         // 与设置页 NavBackdrop 同款
    private const float AchRightPanelX = 420f, AchRightPanelW = 1400f;

    // 左侧一列（从上到下）：灰色小标题「里程碑」→ P/G/U/官方 四个子 tab →（纯空白分隔）→ MOD成就/私人成就/彩蛋成就；
    // 四个子 tab 各自是「该级的严判击破 + 完美无瑕」；选中一律由一块会滑动的实心淡蓝块表示（子 tab 与分类共用一个块）。
    private const float AchCatX = 112f, AchCatW = 200f, AchCatH = 48f, AchCatGap = 10f;   // 相对 AchLeftPanelX(62) 左右各留 50，居中
    private const float AchMileCaptionTop = TopNavH + 46f, AchMileCaptionH = 26f;   // 灰色小标题「里程碑」
    private const float AchSubTop = TopNavH + 78f;                                  // P/G/U/官方 子 tab 起点
    // 所有左栏位置一律走 AchLeftTop(slot)，别再引入第二套坐标：曾经这里的 AchCatTop 常量跟实际排布漂了 16px，
    // 就是「选中框比文字低」那个 bug 的根源。
    private const float AchCatFramePad = 4f;                                // 选中块比按钮外扩多少

    // 右区内容
    private const float AchSearchX = 450f, AchSearchTop = TopNavH + 30f, AchSearchW = 700f, AchSearchH = 50f;
    private const float AchBarX = 1160f, AchBarTop = TopNavH + 42f, AchBarW = 520f, AchBarH = 26f;
    /// <summary>右侧等级标（成就历程进度条右边的奖杯，按等级染色）的位置与大小：
    /// 46 = 原设计 90 缩一半，纵向与进度条居中对齐（条中心 y=TopNavH+55）。</summary>
    private const float AchBadgeX = 1718f, AchBadgeTop = TopNavH + 32f, AchBadgeSize = 46f;
    private const float AchDivX = 450f, AchDivTop = TopNavH + 118f, AchDivW = 1340f, AchDivH = 2f;
    private const float AchGridX = 450f, AchGridTop = TopNavH + 134f, AchGridW = 1330f, AchGridH = 814f;
    private const float AchScrollX = 1788f, AchScrollW = 14f;
    private const int AchCols = 4;
    // 卡片宽 320（(1330 − 3×16) 再四等分）。高 190 是下限：字体调大 + 无瑕那批条件要三行，再矮就会压到下面的注释行。
    private const float AchCardW = 320f, AchCardH = 190f, AchCardGapX = 16f, AchCardGapY = 14f;
    private const float AchCardPadX = 16f;   // 卡片横向内边距（文字不贴边）
    /// <summary>卡片池大小：一屏 3.4 行 + 上下各一行缓冲 + 一行余量，乘列数</summary>
    private const int AchPoolCards = AchCols * 7;
    private const float AchCardPitch = AchCardH + AchCardGapY;

    /// <summary>注释那行的正常字号。单独提出来是因为它出现在两处：MakeText 和 bestFit 的上限 resizeTextMaxSize ——
    /// 两处各写一个数的话，哪天改字号必然漏一处（要么白缩、要么装不下）。</summary>
    private const int AchNoteFont = 17;
    /// <summary>注释 bestFit 的下限：注释区只有两行高（33px），文案再长也只缩到 12 号，保证不叠到下面的升级进度条上。</summary>
    private const int AchNoteFontMin = 12;

    // ---------- 页面状态 ----------
    private static GameObject _achPageRoot;                 // 整页根（挂在页面带的槽位 2）
    private static AchCategory _achCat = AchCategory.Milestone;
    /// <summary>里程碑类里的分级子 tab：0=P、1=G、2=U、3=官方成就（只有 _achCat == Milestone 时有效）</summary>
    private static int _achMileTier;
    private static string _achQuery = "";
    private static readonly List<Button> _achCatBtns = new List<Button>();
    private static readonly List<Text> _achCatName = new List<Text>();
    /// <summary>P/G/U/官方 四个子 tab 的按钮与文字（和分类按钮分开存，便于分别控制颜色）</summary>
    private static readonly List<Button> _achSubBtns = new List<Button>();
    private static readonly List<Text> _achSubName = new List<Text>();
    private static Text _achMileCaption;                    // 灰色小标题「里程碑」
    private static RectTransform _achCatFrameRt;            // 分类选中的滑动框
    private static float _achFrameCurTop, _achFrameTargetTop, _achFrameCurW, _achFrameTargetW, _achFrameCurH, _achFrameTargetH;
    private static InputField _achSearchInput;
    private static Image _achBarFill;
    private static float _achBarFullW;
    private static Text _achBarText;
    private static Image _achBadgeImg;                     // 右侧等级标：奖杯剪影，按等级染色（1白 2黄 3红 4橙 5金）
    private static ScrollRect _achScroll;
    private static RectTransform _achContent;
    private static GameObject _achEmptyHint;
    private static Text _achSummaryText;
    private static readonly List<AchievementDef> _achList = new List<AchievementDef>();   // 当前分类 + 搜索过滤后的列表
    private static readonly List<AchCardRefs> _achPool = new List<AchCardRefs>();
    private static Sprite _achIconSprite;                   // 奖杯图（懒加载）
    private static Sprite _achCrownSprite;                  // 程序画的皇冠（只在奖杯素材读不到时兜底）
    private static Sprite _achBadgeSprite;                  // 等级标：奖杯的白色剪影（染色用）
    private static bool _achBadgeTried;
    private static Sprite _achRoundSprite;                  // 加强圆角（卡片/遮罩/搜索框）
    private static Sprite _achPillSprite;                    // 胶囊（等级进度条）
    private static Sprite _achLockSprite;                    // 未解锁卡片的锁图
    private static bool _achLockTried;
    private static int _achLastFirst = -1, _achLastLast = -1;   // 上一次渲染的可视区间（没变就不重排版）

    /// <summary>等级条那一行的"值"缓存（成就页每帧调 RefreshAchLevelBar）。
    /// 里面的 LevelProgress / LevelFill / CountUnlocked 每帧要遍历 Defs 全表 6 遍
    /// （LevelFill 内部又调一次 LevelProgress，而 CountUnlocked 按 4 个分类各扫一遍），
    /// 而这些值只在新解锁 / 列表重建时才会变。颜色那一半（进度条填充色、等级标染色）
    /// 跟着面板透明度设置走，必须每帧刷 —— 所以只缓存值，不缓存颜色。</summary>
    private static bool _achBarValValid;
    private static int _achBarLv, _achBarExp, _achBarFloor, _achBarNext;
    private static float _achBarFillPct;
    private static int _achBarDone, _achBarTotal, _achBarShown;
    /// <summary>两个文本件的"上次写进去是什么"：Text 赋值会触发 uGUI 重建网格，
    /// 内容没变就别每帧重写（每帧 60 次无谓的网格重建，累积起来是可观的 GC 与顶点开销）。</summary>
    private static string _achBarTextLast, _achSummaryLast;
    /// <summary>让成就页那一行的值缓存作废，下次调用时重算。
    /// 成就层（Achievements 写解锁表时）与语言层（TextTranslatePatch.RefreshUiTexts）都要调它，
    /// 所以是 public —— 这两处都不在 PortalUI 里，不加前缀访问不到。</summary>
    public static void InvalidateAchBarValues() { _achBarValValid = false; }

    /// <summary>未解锁成就卡里那把锁：原图按 alpha 边界裁掉四周空白、四周留 6% 空气、缩成 159×192 存为 assets\icons\lock.png。
    /// 素材读不到就整张图都不显示（不报错、也不留空框）—— 只更 DLL 没更素材包时会走这条。</summary>
    private static Sprite AchLockSprite {
        get {
            if (_achLockTried) return _achLockSprite;
            _achLockTried = true;
            try { _achLockSprite = LoadAssetSprite(Path.Combine(Main.ModEntry.Path, "assets", "icons", "lock.png")); }
            catch { _achLockSprite = null; }
            return _achLockSprite;
        }
    }

    /// <summary>锁图在卡片里的尺寸（56 的方框 + preserveAspect 按原图比例缩，实际画出来约 46×56）；
    /// 纵向 74 = 详细信息区（58..145，高 87）里的居中位置。</summary>
    private const float AchLockSize = 56f, AchLockTop = 74f;

    /// <summary>一张池化卡片里所有要改的引用（建一次，之后只改内容）</summary>
    private class AchCardRefs {
        public GameObject Go;
        public Image Bg;
        public Image Icon;
        public Text Name;
        public Image Line1;
        public Text Cond;
        public Text Note;
        public Image BarBg;
        public Image BarFill;
        public Image Line2;
        public Text Time;
        public Image Lock;               // 未解锁时详细信息区里那张锁图
        public bool Rainbow;             // 5 级成就：名字每帧 RGB 渐变
        public int BoundIndex = -1;      // 当前绑定的是 _achList 的第几个（-1 = 空）
    }

    /// <summary>加强圆角（默认那张 RoundedSprite 半径只有 10，套在 320 宽的大卡片上几乎看不出圆）：
    /// 单独做一张半径 18 的，贴图 56、9-slice 边距 18。</summary>
    private static Sprite AchRoundSprite {
        get {
            if (_achRoundSprite == null) _achRoundSprite = CreateRoundedSprite(56, RadPanel);
            return _achRoundSprite;
        }
    }

    /// <summary>胶囊形（等级进度条）。半径 13 ≈ 条高的一半，两端就是圆的。</summary>
    private static Sprite AchPillSprite {
        get {
            if (_achPillSprite == null) _achPillSprite = CreateRoundedSprite(32, RadPill);
            return _achPillSprite;
        }
    }

    /// <summary>奖杯图标（就是原来成就按钮用的那张）</summary>
    private static Sprite AchIconSprite {
        get {
            if (_achIconSprite == null) {
                try { _achIconSprite = LoadAssetSprite(Path.Combine(Main.ModEntry.Path, "assets", "trophy_icon.png")); }
                catch { }
            }
            return _achIconSprite;
        }
    }

    /// <summary>右侧等级标用的贴图 = 成就名左边那个奖杯图标的白色剪影。
    /// 必须洗成白色：uGUI 的 Image.color 是跟贴图颜色相乘的，金色原图染「白」出来还是金色、染红会变成暗红褐，五档颜色全不准。
    /// 按 alpha 边界裁掉四周空白（原图 256×256 有一圈留白）；素材读不到时退回程序画的皇冠，保证这个位置不会空着。</summary>
    private static Sprite AchBadgeSprite {
        get {
            if (!_achBadgeTried) {
                _achBadgeTried = true;
                _achBadgeSprite = BuildWhiteSilhouette(AchIconSprite);
                if (_achBadgeSprite == null) _achBadgeSprite = AchCrownSprite;
            }
            return _achBadgeSprite;
        }
    }

    /// <summary>把一张 Sprite 变成纯白剪影（RGB=255、保留 alpha）并按 alpha 边界裁剪。
    /// 我们的图片走自己的托管解码通道（代码里 new Texture2D 建的），所以纹理可读。</summary>
    private static Sprite BuildWhiteSilhouette(Sprite src) {
        try {
            if (src == null || src.texture == null) return null;
            var st = src.texture;
            int w = st.width, h = st.height;
            if (w <= 0 || h <= 0) return null;
            var pix = st.GetPixels32();
            if (pix == null || pix.Length < w * h) return null;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++) {
                int row = y * w;
                for (int x = 0; x < w; x++) {
                    byte a = pix[row + x].a;
                    if (a <= 8) { pix[row + x] = new Color32(255, 255, 255, 0); continue; }
                    pix[row + x] = new Color32(255, 255, 255, a);      // 颜色全抹白，只留形状
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < minX || maxY < minY) return null;                // 整张全透明 → 不可用
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            PerfDiag.Tag(tex, "achsilhouette");   // 性能归因：只建一次（缓存），数量是常数
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(pix);
            tex.Apply();
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(minX, minY, cw, ch), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, Vector4.zero), "achsilhouette");
        } catch (System.Exception e) {
            Lg.Ex("成就页：等级标剪影生成失败（退回程序画的皇冠）", e);
            return null;
        }
    }

    /// <summary>等级标颜色：1 级白 / 2 级黄 / 3 级红 / 4 级橙 / 5 级金，黄橙金特意拉开色相与明度（黄偏亮、橙偏深、金偏暖哑）。
    /// 5 级额外画大 10%（由 Card.cs 按 MaxAchievementLevel 判）。</summary>
    private static Color AchBadgeColor(int level) {
        switch (level) {
            case 2: return new Color(1.00f, 0.93f, 0.16f, 1f);   // 黄
            case 3: return new Color(0.95f, 0.24f, 0.24f, 1f);   // 红
            case 4: return new Color(1.00f, 0.50f, 0.10f, 1f);   // 橙
            case 5: return new Color(0.99f, 0.76f, 0.26f, 1f);   // 金
            default: return new Color(1.00f, 1.00f, 1.00f, 1f);  // 1 级白
        }
    }

    /// <summary>程序生成的皇冠贴图 —— 只在奖杯素材读不到时兜底。
    /// 归一化多边形 + 3×3 超采样 + 射线法算覆盖率；纯白遮罩，靠 Image.color 染色。</summary>
    private static Sprite CreateCrownSprite(int size) {
        // 归一化多边形（x 向右、y 向上）：左下 → 左上角 → 凹口 → 中尖 → 凹口 → 右上角 → 右下
        float[] px = { 0.04f, 0.04f, 0.27f, 0.50f, 0.73f, 0.96f, 0.96f };
        float[] py = { 0.02f, 0.60f, 0.38f, 0.92f, 0.38f, 0.60f, 0.02f };
        int n = px.Length;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "crown");   // 性能归因：兜底皇冠只建一次
        tex.filterMode = FilterMode.Bilinear;
        var pix = new Color32[size * size];
        const int SS = 3;                       // 每个像素 3×3 个子采样
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                int hit = 0;
                for (int sy = 0; sy < SS; sy++) {
                    for (int sx = 0; sx < SS; sx++) {
                        float fx = (x + (sx + 0.5f) / SS) / size;
                        float fy = (y + (sy + 0.5f) / SS) / size;
                        if (PointInPoly(fx, fy, px, py, n)) hit++;
                    }
                }
                byte a = (byte)Mathf.RoundToInt(255f * hit / (SS * SS));
                pix[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pix);
        tex.Apply();
        return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, Vector4.zero), "crown");
    }

    /// <summary>兜底皇冠（懒建）：只在奖杯素材读不到时用</summary>
    private static Sprite AchCrownSprite {
        get {
            if (_achCrownSprite == null) _achCrownSprite = CreateCrownSprite(96);
            return _achCrownSprite;
        }
    }

    /// <summary>射线法判点是否在多边形内（兜底皇冠贴图用）</summary>
    private static bool PointInPoly(float x, float y, float[] px, float[] py, int n) {
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++) {
            if (((py[i] > y) != (py[j] > y)) &&
                (x < (px[j] - px[i]) * (y - py[i]) / (py[j] - py[i]) + px[i]))
                inside = !inside;
        }
        return inside;
    }

    // ---------- 建页 ----------

    /// <summary>建成就页（只在切到成就 tab 时建一次；整页挂在 PageSlot(2) 上，跟库页/开发者页一样在页面带里滑动）。</summary>
    private static void BuildAchievementsPage() {
        if (_achPageRoot != null) return;
        var slot = PageSlot(2);   // 成就页在槽位 2（0 = 主页、1 = 库页）
        if (slot == null) { Lg.Info("成就页：PageSlot(2) 拿不到，放弃"); return; }
        _achPageRoot = slot.gameObject;
        _achBtnRts.Clear();     // 自检用的矩形表随页面重建清空
        _achPageRoot.name = "AchPage";
        // 整页建一次要 3 秒多，装分段秒表：下一次日志直接写出是 4 个建页步骤里的哪一个（别猜）。
        var swAch = new StepWatch("成就页");

        // ① 两层半透明遮罩先建（先建 = 画在最底层，不会挡住任何控件）
        BuildAchBackdrops();
        if (swAch != null) swAch.Mark("遮罩");
        // ② 左分类列（无底色 + 滑动选中框）
        BuildAchCategoryColumn();
        if (swAch != null) swAch.Mark("左栏");
        // ③ 顶部搜索 + 等级进度条 + 等级标 + 汇总
        BuildAchTopBar();
        if (swAch != null) swAch.Mark("顶栏");
        // ④ 卡片网格 + 滚动条
        BuildAchGrid();
        if (swAch != null) swAch.Mark("卡片网格");

        _achCat = AchCategory.Milestone;
        _achQuery = "";
        RefreshAchList(true);
        if (swAch != null) swAch.Mark("刷列表");
    }

    /// <summary>左栏 / 右区两层半透明黑遮罩（跟设置页 NavBackdrop / BodyBackdrop 同款：0.45 黑 + 圆角）</summary>
    private static void BuildAchBackdrops() {
        var left = MakeImage(_achPageRoot.transform, "AchLeftBackdrop", new Color(0f, 0f, 0f, 0.45f));
        left.sprite = AchRoundSprite; left.type = Image.Type.Sliced;
        left.raycastTarget = false;
        // 两块底板走「距画布底 BandBottomMargin」的拉伸锚点：1080 上仍是 140..1040，画布 1200 时自己长到 1160，不留空。
        SetRectPxV(left.rectTransform, AchLeftPanelX, AchPanelTop, AchLeftPanelW, BandBottomMargin);

        // 进「要磨砂的遮罩」那张表，但不进 RegisterAlphaBackdrop 那张表 —— 它一直 0.45 到底、不跟面板透明度系数缩，
        // 这一轮只加磨砂，深浅没动。
        RegisterGlassPanel(left);

        var right = MakeImage(_achPageRoot.transform, "AchRightBackdrop", new Color(0f, 0f, 0f, 0.45f));
        right.sprite = AchRoundSprite; right.type = Image.Type.Sliced;
        right.raycastTarget = false;
        SetRectPxV(right.rectTransform, AchRightPanelX, AchPanelTop, AchRightPanelW, BandBottomMargin);
        RegisterGlassPanel(right);   // 成就页左右两块遮罩都要磨砂
    }

    /// <summary>建左侧一列：「里程碑」小标题 → P/G/U/官方 四个子 tab → 纯空白分隔 → 三个分类按钮（都是无底色按钮）。</summary>
    private static void BuildAchCategoryColumn() {
        // 先建选中块再建按钮：实心块必须画在按钮下面，否则会把文字整片盖住
        var frame = MakeImage(_achPageRoot.transform, "AchCatFrame", HighlightFillColor);
        RegisterFillImage(frame);   // 同一族填充色：换主题色要重刷
        frame.raycastTarget = false;
        frame.sprite = RoundedSprite;
        frame.type = Image.Type.Sliced;
        _achCatFrameRt = frame.rectTransform;
        _achCatFrameRt.anchorMin = _achCatFrameRt.anchorMax = new Vector2(0f, 1f);
        _achCatFrameRt.pivot = new Vector2(0f, 1f);
        SetAchFrameTarget(AchCurrentSlot(), true);

        // ① 灰色小标题「里程碑」：一行小字、不可点，居中跟下面几个 tab 文字对齐
        _achMileCaption = MakeText(_achPageRoot.transform, "AchMileCaption", 22,
                                   new Color(1f, 1f, 1f, 0.42f), TextAnchor.MiddleCenter);
        SetRectPx(_achMileCaption.rectTransform, AchCatX, AchMileCaptionTop, AchCatW, AchMileCaptionH);
        _achMileCaption.text = Lang.Tr("里程碑");
        _achMileCaption.raycastTarget = false;

        // 一张表吃下所有左栏项：前 4 项是里程碑分级（0..3），后 3 项是三个分类。
        // 用「按位置反算」而不是闭包索引：闭包索引和鼠标实际命中可能不一致（点在某按钮上、handler 收到别的编号），
        // 位置决定法跟眼睛永远一致。
        for (int i = 0; i < AchLeftCount; i++) {
            int idx = i;
            // 两组之间留纯空白：第 4 项起整体再让一个 AchLeftGapGroups（不画线、也不加标题）
            float top = AchLeftTop(i);
            var btn = MakeAchTabButton("AchLeft" + i, AchLeftLabel(i), top, 24);
            var img = btn.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;       // 自己的射线自己扛
            btn.interactable = true;
            btn.onClick.AddListener(() => SelectAchLeft(idx));   // 只认行索引，位置反算只写日志
            // 列表按组登记：0..3 归「里程碑分级」、4..6 归「分类」（颜色刷新各管一组）
            try { _achBtnRts.Add(btn.GetComponent<RectTransform>()); } catch { }
            if (i <= 3) {
                _achSubBtns.Add(btn);
                _achSubName.Add(btn.GetComponentInChildren<Text>());
            } else {
                _achCatBtns.Add(btn);
                _achCatName.Add(btn.GetComponentInChildren<Text>());
            }
        }
        // 自检不能建完立刻做：这一刻顶部 tab 还在滑动动画里，页面挂在页面带上，量到的世界坐标是动画途中的位置
        //（真日志里 7 行全报被 Bg 压着，而 x=1901 在 1920 宽屏幕的最右边、左栏其实在 canvas x 150..350）。
        // 改成等动画停稳再量：推迟 0.6 秒，由 TickAchievements 触发。
        _achSelfCheckPending = true;
        _achSelfCheckAt = Time.unscaledTime + 0.6f;
    }

    /// <summary>推迟到页面滑动动画停稳之后的自检（建页时设 0.6 秒，由 TickAchievements 触发）。</summary>
    private static bool _achSelfCheckPending;
    private static float _achSelfCheckAt;

    /// <summary>建一个"无底色 + 居中单行文字"的左栏按钮（选中由那块实心块表示，按钮自己不画底）</summary>
    private static Button MakeAchTabButton(string name, string label, float top, int fontSize) {
        var btn = MakeButton(_achPageRoot.transform, name, Vector2.zero, new Vector2(AchCatW, AchCatH), new Color(1f, 1f, 1f, 0f));
        var img = btn.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.type = Image.Type.Simple; img.raycastTarget = true; }
        btn.transition = Selectable.Transition.None;   // 不要 hover 变色，选中只由那个实心块表示
        SetRectPx(btn.GetComponent<RectTransform>(), AchCatX, top, AchCatW, AchCatH);
        var nm = MakeText(btn.transform, "N", fontSize, Color.white, TextAnchor.MiddleCenter);
        Stretch(nm.rectTransform, Vector2.zero, Vector2.zero);
        nm.text = label;
        nm.raycastTarget = false;
        return btn;
    }

    /// <summary>完全不依赖射线的左栏点击：直接读鼠标（UiInput）＋ MouseInRect 判命中，只在成就页显示、且没被上层盖住时生效。
    /// 不用 ScreenPointToLocalPointInRectangle —— 它在这台画布上静默失败过、整条路没跑。
    /// 命中左栏那一条带子才切项，每次都写一行日志。
    private static void TickAchLeftClick() {
        try {
            if (!_panelOpen || _achPageRoot == null || !_achPageRoot.activeInHierarchy) return;
            // 上层盖着（开始界面 / 个人页 / 启动页 / 过渡层）时别接这一下
            if (PanelInputBlocked()) return;
            if (!UiInput.MouseButtonDown(0)) return;
            int idx = AchLeftIndexFromMouse();
            Lg.Info($"[LevelLib] 【成就】左栏读鼠标（不依赖射线）：命中 {(idx >= 0 ? idx + "「" + AchLeftLabel(idx) + "」" : "不在左栏")}"
                + $" 鼠标={UiInput.MousePosition.x:F0},{UiInput.MousePosition.y:F0}");
            // 这条路的索引本身就是按位置算出来的 → 直接按它执行（口径与 SelectAchLeft 里的行索引不同）
            if (idx >= 0) {
                if (idx <= 3) SelectAchTier(idx);
                else SelectAchCategory((AchCategory)(idx - 3));
            }
        } catch (System.Exception e) { Lg.Ex("左栏读鼠标异常", e); }
    }

    /// <summary>自检：页面建好后对 7 行逐个射线检测，打印「这一格最上面是谁」—— 最上面不是按钮自己就是有东西压着。
    /// 屏幕点走 GetWorldCorners + WorldToScreenPoint（跟 MouseInRect 同一套）；必须在页面滑动动画停稳之后再量。</summary>
    private static void SelfCheckAchLeft() {
        try {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || _achPageRoot == null || _panelCanvas == null) return;
            // 附带：页面带当前的横向偏移（0 = 已经停稳，非 0 = 还在滑）
            float bandX = float.NaN;
            try {
                var prt = _achPageRoot.transform.parent as RectTransform;
                if (prt != null) bandX = prt.anchoredPosition.x;
            } catch { }
            // 屏幕点用 Unity 官方换算（跟 MouseInRect 同一套）：手写 scaleFactor 那把尺子是歪的，
            // 会把点算到屏幕外、报出一堆「被压着」的假警报。
            for (int i = 0; i < _achBtnRts.Count; i++) {
                var rt = _achBtnRts[i];
                if (rt == null) continue;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector2 blc = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                Vector2 trc = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                Vector2 screen = (blc + trc) * 0.5f;
                var ped = new UnityEngine.EventSystems.PointerEventData(es);
                ped.position = screen;
                var res = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                es.RaycastAll(ped, res);
                string top = (res.Count > 0 && res[0].gameObject != null) ? res[0].gameObject.name : "(无)";
                Lg.Info($"[LevelLib] 【成就·自检】第 {i} 行「{AchLeftLabel(i)}」中心屏幕点 {screen.x:F0},{screen.y:F0}"
                    + $" → 最上面是 {top}{(top == rt.gameObject.name ? "（就是自己 可点）" : "（被它压着！）")}"
                    + $" 命中 {res.Count} 个｜页面带偏移 x={(float.IsNaN(bandX) ? "?" : bandX.ToString("F0"))}"
                    + $"（0 附近 = 动画已停稳 非 0 = 量早了）");
            }
        } catch (System.Exception e) { Lg.Ex("左栏自检异常", e); }
    }

    /// <summary>左栏一共 7 项：0..3 = 里程碑分级（含官方成就）、4..6 = 三个分类。
    /// 标签、几何、点击都来自同一张表 / 同一个公式（不可能再串号）。</summary>
    private static readonly System.Collections.Generic.List<RectTransform> _achBtnRts =
        new System.Collections.Generic.List<RectTransform>();   // 自检用：7 行按钮的矩形
    private const int AchLeftCount = 7;
    /// <summary>里程碑子 tab 的个数（0=P 1=G 2=U 3=官方成就）—— 上界一律由它推导，不许再出现写死的数字。
    /// 曾经 Mathf.Clamp(tier, 0, 2) 里的 2 是三个子 tab 时代留下的，子 tab 变 4 个后点官方成就被夹回 U 级。</summary>
    private const int AchTierCount = 4;
    /// <summary>里程碑组与分类组之间的纯空白（不画任何东西，免得压住下一组按钮）。</summary>
    private const float AchLeftGapGroups = 32f;
    private static string AchLeftLabel(int i) {
        switch (i) {
            case 0: return "P级里程碑";
            case 1: return "G级里程碑";
            case 2: return "U级里程碑";
            case 3: return "官方成就";
            case 4: return "MOD成就";
            case 5: return "私人";
            default: return "彩蛋成就";
        }
    }

    /// <summary>左栏点击的唯一入口：i = 行索引（0..6）。
    /// 只认行索引（标签和按钮是同一个循环建出来的），位置反算永远不参与决策、只写日志做对照。</summary>
    private static void SelectAchLeft(int i) {
        try {
            // 上一版拿位置反算当权威，把第 3 行算成 2、于是点官方成就跳去了 U 级；定稿只认行索引。
            // 位置反算只写一行日志做对照：就算以后换算再歪，也只影响日志、不影响点得对不对。
            int byMouse = AchLeftIndexFromMouse();
            int use = Mathf.Clamp(i, 0, AchLeftCount - 1);
            Lg.Info($"[LevelLib] 【成就】左栏点击：行={i}「{AchLeftLabel(i)}」"
                + $" → 采用 行索引 {use}「{AchLeftLabel(use)}」"
                + $"（位置反算对照={(byMouse >= 0 ? byMouse + "「" + AchLeftLabel(byMouse) + "」" : "不在左栏")}"
                + (byMouse >= 0 && byMouse != use ? $"  与行索引差 {byMouse - use} 行 已按行索引执行）" : "）"));
            if (use <= 3) SelectAchTier(use);
            else SelectAchCategory((AchCategory)(use - 3));   // 4→Mod(1) 5→Private(2) 6→Misc(3)
        } catch (System.Exception e) { Lg.Ex("左栏点击异常", e); }
    }

    /// <summary>鼠标当前落在左栏第几项（-1 = 不在左栏那条带子里）。只用于日志与自检，不参与「点的是哪一项」的决策。
    /// 换算走 MouseInRect（GetWorldCorners + WorldToScreenPoint，Unity 官方那套），全项目只留这一套。</summary>
    private static int AchLeftIndexFromMouse() {
        try {
            if (_achBtnRts.Count == 0) return -1;
            Vector2 m = UiInput.MousePosition;
            // 曾经用画布自己的 scaleFactor 手写换算，那把尺子是歪的（7 行全算到屏幕外），所以它一直报「不在任何一行」——
            // 那不是「没点中」，是尺子本身错了。换成 MouseInRect 后正常。
            for (int i = 0; i < _achBtnRts.Count; i++) {
                var rt = _achBtnRts[i];
                if (rt == null) continue;
                if (!MouseInRect(rt)) continue;
                Lg.Info($"[LevelLib] 【成就】左栏矩形判定：命中 {i}「{AchLeftLabel(i)}」鼠标={m.x:F0},{m.y:F0}");
                return i;
            }
            Lg.Info($"[LevelLib] 【成就】左栏矩形判定：不在任何一行（落在行间空隙）鼠标={m.x:F0},{m.y:F0}");
            return -1;
        } catch (System.Exception e) {
            Lg.Ex("左栏矩形判定异常", e);
            return -1;
        }
    }

    /// <summary>左栏第 slot 项的顶边（0..3 = P/G/U/官方 四个子 tab，4..6 = MOD成就 / 私人成就 / 彩蛋成就）。
    /// 建按钮与摆选中块都必须调它：曾经两处各拼一份公式、漂了 16px，现场看起来就是「文字没在框里居中」。
    /// 返回的是顶边（配 pivot=(0,1) 的 SetRectPx / 取负的 anchoredPosition）。</summary>
    private static float AchLeftTop(int slot) {
        return AchSubTop + slot * (AchCatH + AchCatGap) + ((slot >= 4) ? AchLeftGapGroups : 0f);
    }

    /// <summary>左栏可选项的位置表（选中块在 0..6 这 7 个位置之间滑动）；就是 AchLeftTop 的别名，别再各写一套。</summary>
    private static float AchSlotTop(int slot) {
        return AchLeftTop(slot);
    }

    /// <summary>当前选中项对应哪一个位置（AchSlotTop 的入参）</summary>
    private static int AchCurrentSlot() {
        if (_achCat == AchCategory.Milestone) return Mathf.Clamp(_achMileTier, 0, 3);   // 0..3
        int idx = (int)_achCat - 1;                       // Mod=1→0、Private=2→1、Misc=3→2
        return 4 + Mathf.Clamp(idx, 0, 2);                // 分类按钮从槽位 4 开始
    }

    /// <summary>里程碑类里的一条成就属于哪一级（P/G/U）。id 形如 ach_milestone_P7 / ach_flawless_U20。</summary>
    private static char AchTierOf(string id) {
        if (string.IsNullOrEmpty(id)) return '?';
        // 官方成就按前缀认：它们的 id 是 ach_official_<关卡id>，末段是关卡号、不是 P/G/U，
        // 所以不能走下面那套「取最后一个下划线后面那个字母」。
        if (id.StartsWith(Achievements.AchOfficialPrefix)) return 'O';
        int i = id.LastIndexOf('_');
        if (i < 0 || i + 1 >= id.Length) return '?';
        return char.ToUpperInvariant(id[i + 1]);
    }

    /// <summary>子 tab 的级字母：0=P、1=G、2=U、3=O（官方）</summary>
    private static char AchTierLetter() {
        // 第 4 个子 tab 用字母 'O'（Official）：它被 AchTierOf 拿去过滤列表，两边必须一致
        if (_achMileTier == 3) return 'O';
        return "PGU"[Mathf.Clamp(_achMileTier, 0, 2)];
    }

    /// <summary>把选中块移到第 slot 个位置（instant=true 立刻到位，否则交给每帧平滑滑动）</summary>
    private static void SetAchFrameTarget(int slot, bool instant) {
        if (_achCatFrameRt == null) return;
        _achFrameTargetTop = AchSlotTop(slot) - AchCatFramePad;
        _achFrameTargetW = AchCatW + AchCatFramePad * 2f;
        _achFrameTargetH = AchCatH + AchCatFramePad * 2f;
        if (instant) {
            _achFrameCurTop = _achFrameTargetTop;
            _achFrameCurW = _achFrameTargetW;
            _achFrameCurH = _achFrameTargetH;
            ApplyAchFrame();
        }
    }

    private static void ApplyAchFrame() {
        if (_achCatFrameRt == null) return;
        _achCatFrameRt.anchoredPosition = new Vector2(AchCatX - AchCatFramePad, -_achFrameCurTop);
        _achCatFrameRt.sizeDelta = new Vector2(_achFrameCurW, _achFrameCurH);
    }

}
