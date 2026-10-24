// [split-of] PortalUI.cs lines 938-1183
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

/// <summary>PortalUI partial 模块：字体（UI 字体探测 / 楷体 / 字形预热）与程序化贴图（圆角、描边）</summary>
public static partial class PortalUI {
    private static float _rainbowHueLast = -1f;

    /// <summary>详细日志开关（默认关闭，打开后每帧都要拼字符串）</summary>
    public static bool LogVerbose;

    // ---------- 字体 ----------

    /// <summary>界面通用字体：只认游戏原生 UI 的字体（自带中文，不用随包字体）。
    /// 三条自定义字体的路都已试死，别再走：裸 .ttf 运行时变不成 Font；AssetBundle 要同版本 Unity
    /// （游戏 6000.3，本机只有 2022.3）；进程内注册随包字体 AddFontResourceEx + FR_PRIVATE，
    /// Unity 的字体枚举不认，CreateDynamicFontFromOSFont 仍返回 null。
    /// 陷阱：场景扫描（`FindObjectsOfTypeAll&lt;Text&gt;`）返回顺序未定义，一旦先扫到我们自己建的
    /// 楷体标题，这个"通用字体"就变成楷体，之后每个走 `MakeText` 的元素都跟着变楷体。
    /// 所以取字体时必须排除自己的画布（`LevelLib_` 前缀，见 `MakeCanvas`）——凡"从场景里捡一个
    /// 对象当通用配置"的写法，都要先问一句捡到的会不会是我自己。</summary>
    private static Font FindUiFont() {
        Font mine = null;
      // 两轮挑选：第一轮只认"游戏打包进来的字体"（带 fontNames 的那种）；
      // 一个都没有（字体 mod 把原生 UI 全换掉了）才进第二轮，把运行时字体也当候选，
      // 但仍然优先挑"不是内置兜底、又能显示中文"的那张。
      // 早先一轮到位：把运行时字体直接排掉，原生 UI 被换过的那台机器会一路退到系统字体，
      // 而系统字体（雅黑等）的字形图集同样对不上原版假设，满屏就成楷体 / 投影缺字。
        for (int round = 0; round < 2; round++) {
            bool allowRuntime = (round == 1);
            foreach (var t in Resources.FindObjectsOfTypeAll<Text>()) {
                try {
                    if (t == null || t.font == null) continue;
                    if (IsOurUi(t)) {
                        if (mine == null) { mine = t.font; _uiFontFromOurs = t.name; }
                        continue;   // 只有游戏自己的 UI 不算"我们建的"
                    }
                    if (_uiFontNative == null) { _uiFontNative = t.font; _uiFontNativeSource = t.name; }
                    // 游戏原生 UI 自己也可能在吃内置兜底（Unity 6 里叫 LegacyRuntime）：它没有中文字形，
                    // 拿它当通用字体的话中文全靠系统回退，回退成哪张字由玩家的机器决定（有的机器就是楷体）。
                    // 所以"是不是原生 UI 在用的"不够，还要它能显示中文才算数。
                    if (IsBuiltinFallbackFont(t.font) || !FontCoversChinese(t.font, out _)) {
                        if (_uiFontNoCjk == null) { _uiFontNoCjk = t.font; _uiFontNoCjkSource = t.name; }
                        continue;
                    }
      // 字体 mod 换过的字体（如 godoMaum）也能显示中文，但它的字形图集与游戏原版那套假设对不上：
      // 大标题的投影（uGUI 的 Shadow，靠字形图集再画一遍顶点）会缺字/截断。
      // 判据看它是不是"没有 fontNames 的运行时字体"：游戏打包进来的都带字体名。
      // 这种字体在第二轮里仍然可用（那时已经没有更原版的候选了，宁可投影不完美也不能满屏楷体）。
                    if (!allowRuntime && IsRuntimeCreatedFont(t.font)) {
                        if (_uiFontReplaced == null) { _uiFontReplaced = t.font; _uiFontReplacedSource = t.name; }
                        continue;
                    }
                    _uiFont = t.font;
                    _uiFontSource = t.name;
                    if (allowRuntime) {
                        Lg.Info("界面字体：原生 UI 的字体全被 mod 换过 ->  "
                            + "在运行时字体里挑一张能显示中文的（" + t.name + "）凑合（投影可能不完美）");
                    }
                    return t.font;
                } catch { }
            }
            // 第一轮一个都没挑到：登记"这台机器装过字体 mod"，第二轮继续
            if (!allowRuntime && _uiFontReplaced != null) {
                Lg.Info("界面字体：原生 UI 在用 " + _uiFontReplacedSource
                    + "（被 mod 换过的那种）-> 放宽到运行时字体里挑");
            }
        }
        // 游戏原生 UI 里没有一张能显示中文的（或者一张原生字体都没捡到，游戏还没建 UI），那就退到系统已装字体
        var osUiFont = OsUiFontForChinese();
        if (osUiFont != null) {
            _uiFontSource = "(游戏原生 UI 里没有能显示中文的 → 用系统已装字体)";
            return osUiFont;
        }
        // 最后才是内置 Arial；绝不拿自己的字体顶上，那正是"全世界都变楷体"的成因
        _uiFontSource = "(游戏原生 UI 里没找到能用的 → 退回内置 Arial)";
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    /// <summary>这张字体是不是"运行时创建出来的"（fontNames 为空）：`CreateDynamicFontFromOSFont`
    /// 建出来的、以及字体 mod 注入的，多半属于这一类；游戏打包进来的内置字体都带字体名。
    /// 这类字体的动态字形图集由各自那份资源重建，我们的投影与烘焙假设（按原版字体估的字形范围）
    /// 与它对不上，大标题投影就会缺字/被截断。所以它不配当"通用字体"，宁可退到系统中文字体。</summary>
    private static bool IsRuntimeCreatedFont(Font f) {
        try {
            if (f == null) return false;
            if (f.dynamic) return true;
            string[] names = f.fontNames;
            return names == null || names.Length == 0;
        } catch { return false; }
    }

    /// <summary>这张字体是不是 Unity 的内置兜底（老版本叫 Arial、Unity 6 里叫 LegacyRuntime）。
    /// 它没有中文字形：界面上中文要么空白或方块，要么靠系统回退 —— 回退成哪张字由玩家的机器决定。
    /// 判据看字体自己的名字，不看它挂在哪个对象上（出过事的机器就是"原生 UI 的对象名 + 内置兜底的字体名"）。</summary>
    private static bool IsBuiltinFallbackFont(Font f) {
        try {
            if (f == null) return false;
            string n = f.name;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("Arial", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("LegacyRuntime", System.StringComparison.OrdinalIgnoreCase) >= 0;
        } catch { return false; }
    }

    /// <summary>系统里已装、且真能显示中文的字体，按优先级取第一张装了的。
    /// 与楷体那条路同一个办法：先跟 `Font.GetOSInstalledFontNames()` 精确比对名字（差一个字
    /// `CreateDynamicFontFromOSFont` 会静默返回 null，那种"字全不见了"最难查），建出来再过一遍中文探针。
    /// 返回 null = 调用方继续往下退（内置 Arial）。</summary>
    private static Font OsUiFontForChinese() {
        try {
            string[] want = { "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI", "SimSun", "宋体",
                              "Noto Sans CJK SC", "Source Han Sans SC", "Malgun Gothic", "Meiryo", "Arial Unicode MS" };
            string[] have = null;
            try { have = Font.GetOSInstalledFontNames(); } catch { }
            if (have == null) return null;
            for (int w = 0; w < want.Length; w++) {
                for (int h = 0; h < have.Length; h++) {
                    if (string.IsNullOrEmpty(have[h])) continue;
                    string name = have[h].Trim();
                    if (!string.Equals(name, want[w], System.StringComparison.OrdinalIgnoreCase)) continue;
                    var f = Font.CreateDynamicFontFromOSFont(name, 24);
                    if (f == null || !FontCoversChinese(f, out _)) continue;
                    Lg.Info("界面字体取自：系统已装字体 " + name
                        + "（游戏原生 UI 里没有能显示中文的）");
                    return f;
                }
            }
        } catch (System.Exception e) {
            Lg.Ex("找系统中文界面字体失败（继续退内置 Arial）", e);
        }
        return null;
    }

    /// <summary>字体是从哪个对象身上捡到的（诊断用）</summary>
    private static string _uiFontSource = "?";
    /// <summary>万一捡到的是我们自己画的字，这里是那个对象名（诊断用）</summary>
    private static string _uiFontFromOurs = "";
    /// <summary>第一次从游戏原生 UI 里捡到的那张字体（自检拿它当"这台机器本该用哪张"的参照），
    /// 以及是从哪个对象身上捡到的。正常一局里只赋值一次；后面 _uiFont 要是跟它对不上，就说明中间被换过。</summary>
    private static Font _uiFontNative;
    private static string _uiFontNativeSource = "";
    /// <summary>游戏原生 UI 里第一张"显示不出中文"的字体（内置兜底，或者过不了中文探针），以及是从哪个
    /// 对象身上捡到的 —— 诊断用：它说明这台机器上原生 UI 吃的是哪种兜底（见 DumpFontReport）。</summary>
    private static Font _uiFontNoCjk;
    private static string _uiFontNoCjkSource = "";
    /// <summary>游戏原生 UI 里第一张"被字体 mod 换过"的字体（能显示中文、但是运行时创建的那种），
    /// 以及是从哪个对象身上捡到的 —— 诊断用：出现它就说明这台机器上装过字体 mod，
    /// 而它的字形图集会让大标题投影缺字（见 IsRuntimeCreatedFont）。</summary>
    private static Font _uiFontReplaced;
    private static string _uiFontReplacedSource = "";

    /// <summary>这个对象是不是我们自己建的 UI（画布名统一带 `LevelLib_` 前缀，见 MakeCanvas）</summary>
    private static bool IsOurUi(Component c) {
        try {
            var tr = c.transform;
            while (tr != null) {
                if (tr.name != null && tr.name.StartsWith("LevelLib_", System.StringComparison.Ordinal)) return true;
                tr = tr.parent;
            }
        } catch { }
        return false;
    }

    /// <summary>给别的界面模块（生日窗 / 公祭日窗）取通用字体；第一次取到就缓存，
    /// 不能再每次全场景扫一遍——那既每次分配大数组，又可能每次抓到不同的对象（楷体 bug 的温床）。</summary>
    public static Font UiFontForOthers() { return _uiFont ?? (_uiFont = FindUiFont()); }

    // ---------- 楷体 ----------
    private static Font _kaitiFont;
    private static bool _kaitiTried;

    /// <summary>楷体：只能用系统已装的字体（裸 .ttf / AssetBundle / AddFontResourceEx 都不行，
    /// `Font.CreateDynamicFontFromOSFont` 对装了的楷体才有效；Windows 上的名字可能是
    /// KaiTi / 楷体 / KaiTi_GB2312 / STKaiti 等，必须先跟 `Font.GetOSInstalledFontNames()` 对一遍，
    /// 差一个字它静默返回 null，那种"字全不见了"最难查）。
    /// 返回 null = 调用方退回游戏自带字体，宁可不像楷体也不能没字。</summary>
    public static Font KaitiFont() {
        if (_kaitiTried) return _kaitiFont;
        _kaitiTried = true;
        try {
            string[] want = { "KaiTi", "楷体", "KaiTi_GB2312", "楷体_GB2312", "STKaiti", "华文楷体", "SimKai" };
            string[] have = null;
            try { have = Font.GetOSInstalledFontNames(); } catch { }
            string pick = null;
            if (have != null) {
                foreach (var w in want) {
                    foreach (var h in have) {
                        if (!string.IsNullOrEmpty(h) && string.Equals(h.Trim(), w, System.StringComparison.OrdinalIgnoreCase)) { pick = h.Trim(); break; }
                    }
                    if (pick != null) break;
                }
            }
            if (pick == null) {
                Lg.Info("系统里没找到楷体（已装字体 " + (have != null ? have.Length : 0)
                    + " 个）→ 开发者页标题继续用游戏字体");
                return null;
            }
            _kaitiFont = Font.CreateDynamicFontFromOSFont(pick, 190);
            Lg.Info("楷体：" + (pick) + " → "
                + (_kaitiFont != null ? "建好了（开发者页标题用它）" : "CreateDynamicFontFromOSFont 返回 null 退回游戏字体"));
        } catch (System.Exception e) {
            Lg.Ex("取楷体失败（退回游戏字体）", e);
            _kaitiFont = null;
        }
        return _kaitiFont;
    }

    // ---- 圆角 ----

    private static Sprite _roundedSprite;
    private static Sprite RoundedSprite {
        get {
            // 贴图尺寸与 9-slice 边距必须保持原样（32 / 10），只让边缘那圈有柔化过渡
            if (_roundedSprite == null) _roundedSprite = CreateRoundedSprite(32, RadCard);
            return _roundedSprite;
        }
    }

    /// <summary>纯白直边 Sprite，给要贴边拉满的细条用（滑条的选中填充区）。
    /// 圆角 Sprite 的 9-slice 边距是 10，条幅只有 6~12 像素高时四角会画歪。</summary>
    private static Sprite _sharpSprite;
    private static Sprite SharpSprite {
        get {
            if (_sharpSprite == null) {
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                PerfDiag.Tag(tex, "sharp");   // 【性能归因】我们的纹理统一起名 LL_*，诊断时能数出"活着的有几张"
                var px = new Color32[64];
                for (int i = 0; i < 64; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px);
                tex.Apply();
                _sharpSprite = PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, Vector4.zero), "sharp");
            }
            return _sharpSprite;
        }
    }

    // ---------- 小圆角块（字母定位条的选中提示用） ----------
    // 全界面那张 RoundedSprite 外半径 10，套在 44x26 的小块上会把顶/底边吃光、9-slice 也画歪，
    // 所以小方块单独用半径 6 的贴图
    private static Sprite _smallRoundSprite;

    /// <summary>小圆角实心块（白色），外半径 6，9-slice 边距 6</summary>
    private static Sprite SmallRoundSprite {
        get {
            if (_smallRoundSprite == null) _smallRoundSprite = CreateRoundedSprite(24, RadSmall);
            return _smallRoundSprite;
        }
    }

    // ---------- 弹窗的大圆角块 ----------
    // 弹窗是整个界面里最大的一块板：圆角跟方板一样用半径 10，四角在 1000 像素宽的板上看着就是直角，
    // 所以他一直觉得"区域遮罩圆角不够"。这档用尺上最高的 RadPanel(22)，比方板那一档圆一倍多。
    // 磨砂阴影半径读的就是贴图的 9-slice 边距，换这张贴图阴影会自己跟上（见 PortalUI.PanelGlass.cs）。
    private static Sprite _dialogRoundSprite;

    /// <summary>弹窗底板与磨砂遮罩用的圆角块（白色），半径走尺上最高的 RadPanel，9-slice 边距同值</summary>
    private static Sprite DialogRoundSprite {
        get {
            if (_dialogRoundSprite == null) _dialogRoundSprite = CreateRoundedSprite(64, RadPanel);
            return _dialogRoundSprite;
        }
    }

    /// <summary>生成白色圆角矩形纹理（带 9-slice border，配合 Image.Sliced 使用）。
    /// 贴图尺寸 / 边距 / 圆角半径（32、10）不可单独动：提高贴图分辨率而 border 不同比例放大，
    /// 9-slice 的几何就会跟着变，界面形状直接崩。贴图边长必须大于两倍半径（中间那条拉伸带会被吃光）。</summary>
    private static Sprite CreateRoundedSprite(int size, float radius) {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "round" + size);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                // 有向距离：<0 在圆角矩形内；边界落在半径处（x=radius 是第一列完全在内、x=radius-1 是最外一列），
                // 所以 (d + 0.5) 就是像素覆盖率：内部整像素 alpha=1，只有边缘那圈是渐变
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                float a = Mathf.Clamp01(0.5f - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)), "round" + size);
    }

    private static Sprite _roundDiscSprite;
    /// <summary>完美正圆盘贴图（白色、边带抗锯齿）：半径 = 半边长，整块就是圆的底板用它 + Image.Simple。
    /// 不能拿 CreateRoundedSprite(24, 24) 顶：半径 24 超过半边 12，SDF 里 Clamp(min>max) 会算出畸形形状
    /// （圆钮报过"不规则形状"就是这个）。</summary>
    private static Sprite RoundDiscSprite {
        get {
            if (_roundDiscSprite == null) _roundDiscSprite = CreateRoundedSprite(64, 32f);
            return _roundDiscSprite;
        }
    }

    /// <summary>软边量（画布单位）：板子轮廓外这条宽度内 alpha 从 1 收到 0。
    /// 2 单位 ≈ 1.7 CSS ≈ 2px@1080p —— 够吃掉透视倾斜的像素阶梯、又不显得模糊。</summary>
    private const float SoftEdgePx = 2f;

    /// <summary>带软边的板子贴图（9-slice，radius=0 是直角板）。为什么要它：大厅图元被逐顶点
    /// 透视倾斜后，几何边缘不再与像素网格对齐，Overlay 画布又没有 MSAA，硬光栅化就是锯齿；
    /// 把"可见边缘"从几何边界挪进贴图 alpha 渐变里，抗锯齿交给纹理采样（与圆钮/阴影同一思路）。
    /// 渐变放在轮廓外面（轮廓内 alpha=1）：相邻两块 gap0 拼板（采购中心+招募栏）的软边互相叠住，
    /// 不会像"往内收"那样在拼缝处裂出背景。调用方要把几何按 SoftEdgePx 向外扩（见 MakeArkRect /
    /// SoftifyArkPlate），50% 等值线才落在名义矩形上、视觉尺寸不变。</summary>
    private static readonly System.Collections.Generic.Dictionary<int, Sprite> _softPlateSprites
        = new System.Collections.Generic.Dictionary<int, Sprite>();
    private static Sprite SoftPlateSprite(float radius) {
        int r = Mathf.Clamp(Mathf.RoundToInt(radius), 0, 32);
        int a = Mathf.Max(1, Mathf.RoundToInt(SoftEdgePx));
        int key = r * 100 + a;
        Sprite sp;
        if (_softPlateSprites.TryGetValue(key, out sp) && sp != null) return sp;
        int border = r + a;
        const int Mid = 4;
        int size = border * 2 + Mid;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "softplate" + size);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[size * size];
        float half = size * 0.5f;
        float hw = half - a;                 // 轮廓半宽：直边离贴图边 a
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float fx = x + 0.5f - half, fy = y + 0.5f - half;
                float qx = Mathf.Abs(fx) - (hw - r);
                float qy = Mathf.Abs(fy) - (hw - r);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float a01 = Mathf.Clamp01(1f - Mathf.Max(0f, d) / a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a01 * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        sp = PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border)), "softplate" + size);
        _softPlateSprites[key] = sp;
        return sp;
    }

    // ---------- 顶部导航那排"圆角胶囊 + 液桥"的贴图 ----------
    // 形状语言来自 shadcn 生态的 Gooey Nav：一排圆角方块拼成一条胶囊，被选中的那一块左右各让出
    // 一段空隙、从群体里脱出来；空隙刚开始拉开时两侧还连着一段"液桥"（上下两条凹曲线收成细腰），
    // 拉满之后就断干净。这里没有 shader / 没有滤镜，所以液桥是一张预烘的形状图 + 按空隙宽度淡出。

    /// <summary>导航胶囊的圆角半径（画布像素）。跟设置页那种 10 的圆角区分开：这排更"软"一点。</summary>
    private const float NavPillRadius = 14f;

    private static Sprite _navPill, _navNeck, _stepPill;

    /// <summary>更新条那一颗小药丸（8 像素高、圆角 4 = 整颗圆头）：方角在放大之后一眼看得出来</summary>
    public static Sprite StepPill { get { EnsureNavSprites(); return _stepPill; } }

    /// <summary>胶囊块：一整条、左组、右组、被选中那块全都用它 —— 每块的四个角都要圆。
    /// 朝缝隙那一侧要是方角，选中中间那一块时两边的断口就是两个直角（一眼看出没做圆角）。</summary>
    private static Sprite NavPill { get { EnsureNavSprites(); return _navPill; } }
    private static Sprite NavNeck { get { EnsureNavSprites(); return _navNeck; } }

    private static void EnsureNavSprites() {
        if (_navPill != null && _navNeck != null) return;
        try {
            if (_navPill == null) _navPill = PerfDiag.TagSprite(CreateRoundedSprite(32, (int)NavPillRadius), "navpill");
            if (_navNeck == null) _navNeck = PerfDiag.TagSprite(CreateNeckSprite(), "navneck");
            if (_stepPill == null) _stepPill = PerfDiag.TagSprite(CreateRoundedSprite(16, 8), "steppill");
        } catch (System.Exception e) {
            Lg.Ex("生成导航胶囊贴图失败（退回圆角方块）", e);
        }
    }

    /// <summary>液桥：上下两条二次贝塞尔在中间收成细腰（与那套 Gooey Nav 的 SVG 路径同形）。
    /// 左右两端高度是整个胶囊高、中间只有腰那么高，所以两边一拉开就像一滴液体被拽细。
    /// 它只画在"被选中那块让出来的空隙"里，宽度由运行时按当前空隙给。</summary>
    private static Sprite CreateNeckSprite() {
        const int W = 64, H = 64;
        float waist = H * 0.34f;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "navneck");
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++) {
            float t = x / (W - 1f), mt = 1f - t;
            // 上边：P0=0、P1=H-waist、P2=0；下边：P0=H、P1=waist、P2=H（y 向下，与贴图无关，形状左右对称）
            float top = 2f * mt * t * (H - waist);
            float bot = mt * mt * H + 2f * mt * t * waist + t * t * H;
            for (int y = 0; y < H; y++) {
                float cy = y + 0.5f;
                float a = Mathf.Clamp01(Mathf.Min(cy - top, bot - cy) + 0.5f);
                px[(H - 1 - y) * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }
    // ---------- 斜切平行四边形（形状生成器，现在没有任何界面调用它） ----------
    // 大厅那几块已改圆角（RoundedSprite 走九宫格）、顶部导航排是圆角胶囊，于是这套只作为
    // 形状回退留在原地：要恢复斜切，把调用点接回 SlabAt 就行；没人调它时一次也不会烘。
    // 形状：顶边与底边水平，左右两条边向右上斜（顶边相对底边右移 skew 像素）。
    // 贴图按显示尺寸现烘（不是九宫格拉伸）：九宫格那版是"256 高的图纵向拉到 170"，
    // 斜边的抗锯齿被重采样一次，边上总有一圈发糊。每个尺寸烘一张，逐像素按覆盖率算，
    // 一比一贴上去，斜边就是全屏最锐的那条线。
    /// <summary>斜切量（画布像素）：与块高无关，永远是这么多（斜率随高度自适应）。</summary>
    /// <summary>左侧那条竖条的宽度（画布像素）：必须小于最小的 skew，否则竖条会越过斜边。</summary>
    private const int SlabBarW = 7;
    /// <summary>斜边的抗锯齿过渡带（像素）：1.0 = 正好一像素，这是覆盖率唯一正确的口径。
    /// 不能为了"看着柔"放宽，放宽之后斜角边缘会整圈发虚（要边缘清楚就靠一比一现烘，不是靠糊）。</summary>
    private const float SlabEdgeRamp = 1.0f;
    /// <summary>玻璃边那圈细线的宽度（像素）：只用来把轮廓钉住，不参与形状</summary>
    private const float SlabRimW = 1.3f;

    /// <summary>烘好的块：body = 整块（白、按覆盖率带 alpha）、bar = 左侧那条竖条、rim = 轮廓那圈细线。
    /// 三者同尺寸、同几何，摆在一起严丝合缝。</summary>
    private sealed class SlabArt {
        public Sprite Body, Bar, Rim;
    }

    private static readonly System.Collections.Generic.Dictionary<long, SlabArt> _slabCache =
        new System.Collections.Generic.Dictionary<long, SlabArt>();

    private static long SlabKey(int w, int h, int skew) { return ((long)w << 40) | ((long)h << 16) | (uint)skew; }


    /// <summary>按显示尺寸烘一张斜切片（逐像素覆盖率）。
    /// 逐行看：这一行的斜边在 x = skew·(1 − y/h) 处（y 从底往上算），于是
    /// 每个像素的覆盖率 = 它到斜边的水平距离除以抗锯齿过渡带、再夹到 0..1。</summary>
    private static SlabArt BakeSlab(int w, int h, int skew) {
        try {
            var texBody = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var texBar = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var texRim = new Texture2D(w, h, TextureFormat.RGBA32, false);
            PerfDiag.Tag(texBody, "slab" + skew + "_" + w + "x" + h);
            PerfDiag.Tag(texBar, "slabbar" + skew + "_" + w + "x" + h);
            PerfDiag.Tag(texRim, "slabrim" + skew + "_" + w + "x" + h);
            texBody.filterMode = FilterMode.Bilinear;
            texBar.filterMode = FilterMode.Bilinear;
            texRim.filterMode = FilterMode.Bilinear;
            texBody.wrapMode = texBar.wrapMode = texRim.wrapMode = TextureWrapMode.Clamp;
            var pxBody = new Color32[w * h];
            var pxBar = new Color32[w * h];
            var pxRim = new Color32[w * h];
            for (int y = 0; y < h; y++) {
                // 贴图自下而上：y = 0 是底边。底边的斜边在 x = 0，顶边在 x = skew。
                float t = h > 1 ? (float)y / (h - 1) : 0f;
                float xl = skew * t;
                float xr = (w - skew) + skew * t;
                float dTop = y, dBot = (h - 1) - y;      // 贴图里 y 向上 , 到上下两条水平边的距离
                if (dTop > dBot) { float tmp = dTop; dTop = dBot; dBot = tmp; }   // dTop = 到最近那条水平边的距离
                for (int x = 0; x < w; x++) {
                    float cx = x + 0.5f;
                    float dSide = Mathf.Min(cx - xl, xr - cx);
                    float a = Mathf.Clamp01(0.5f + dSide / SlabEdgeRamp);
                    int i = y * w + x;
                    pxBody[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    float b = Mathf.Clamp01(0.5f + Mathf.Min(cx - xl, xl + SlabBarW - cx) / SlabEdgeRamp);
                    pxBar[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(b * 255f));
                    float dEdge = Mathf.Min(dSide, dTop);
                    float rv = Mathf.Clamp01((SlabRimW - dEdge) / SlabRimW) * a;
                    pxRim[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(rv * 255f));
                }
            }
            texBody.SetPixels32(pxBody); texBody.Apply();
            texBar.SetPixels32(pxBar); texBar.Apply();
            texRim.SetPixels32(pxRim); texRim.Apply();
            var art = new SlabArt();
            art.Body = Sprite.Create(texBody, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            art.Bar = Sprite.Create(texBar, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            art.Rim = Sprite.Create(texRim, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return art;
        } catch (System.Exception e) {
            Lg.Ex("烘斜切块贴图失败（退回圆角方块）", e);
            return null;
        }
    }

    // ---------- 描边框（白框，9-slice） ----------
    // 尺寸 / 边距 / 线宽保持整数点阵参数（18x18、圆角 7、线宽 2），只把边缘那圈 alpha 从二值
    // 换成覆盖率过渡，缩放时才不是一格格的台阶。
    // 贴图分辨率不能单独动：border 放大 8 倍会让 9-slice 几何跟着翻，界面形状直接崩

    /// <summary>空心圆角矩形描边（白色），9-slice。borderWidth 是描边带宽（浮点，允许 3.5 这种半像素线宽）</summary>
    private static Sprite CreateRoundedFrameSprite(int size, int radius, float borderWidth) {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "frame" + size);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                // 解析有向距离：与点阵判定同一套几何（边界在 radius、radius-1 之间），
                // 只是把"是/否"换成 0..1 的覆盖率
                float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;          // 外轮廓：<0 在内
                float outer = Mathf.Clamp01(0.5f - d);                     // 外边缘柔化
                float inner = Mathf.Clamp01(0.5f - (d + borderWidth - 1f)); // 内边缘柔化（描边带内侧）
                float a = Mathf.Clamp01(outer - inner);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius)), "frame" + size);
    }

    // ---------- 阴影环（黑，9-slice） ----------
    // 圆角矩形轮廓外侧一圈羽化带，轮廓里侧全透明。与描边框那族的区别：那个的轮廓贴着贴图四边、
    // 带宽往里长；这个的轮廓往里让出 margin，羽化带才铺得开，所以贴图还得留一条中间拉伸区
    // （不留的话轮廓的直边在贴图里长度为 0，拉宽时直边会被画成圆弧）。
    // border = 圆角半径 + margin，贴图边长 = border × 2 + 中间那条；三处必须同源，9-slice 的几何才对得上。

    /// <summary>黑色柔和阴影环。radius = 轮廓圆角半径（必须跟被衬的那块遮罩一致）、
    /// margin = 羽化带宽度（也是轮廓相对贴图四边的余量）、inner = 轮廓外留多少像素的空白
    /// （不许糊进面板本体：遮罩是半透明的，里面多一层灰看得出来）、peak = 最浓处的不透明度。
    /// alpha 沿"到轮廓的距离"先快抬到峰值（接触阴影），再平滑收到零。</summary>
    private static Sprite CreateRoundedShadowSprite(float radius, float margin, float inner, float peak) {
        int r = Mathf.Clamp(Mathf.RoundToInt(radius), 4, 32);
        int pad = Mathf.Max(6, Mathf.RoundToInt(margin));
        int border = r + pad;
        const int Mid = 12;                     // 轮廓直边在贴图里的长度（这一段会被 9-slice 拉长）
        int size = border * 2 + Mid;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "shadow" + size);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[size * size];
        float half = size * 0.5f;
        float hw = half - pad;                  // 轮廓半宽：轮廓是半径 r 的圆角矩形，四边离贴图边 pad
        float band = Mathf.Max(0.5f, pad - inner);
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                // 到圆角矩形的有向距离（< 0 在轮廓里）：与 CreateRoundedSprite 同一口径，
                // 只是内缩矩形由 radius 换成 hw - r（轮廓里那圈直边的端点）
                float fx = x + 0.5f - half, fy = y + 0.5f - half;
                float qx = Mathf.Abs(fx) - (hw - r);
                float qy = Mathf.Abs(fy) - (hw - r);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float a = 0f;
                if (d > inner) {
                    float t = Mathf.Clamp01((d - inner) / band);
                    float rise = Mathf.Clamp01(t / 0.06f);                 // 紧贴轮廓那一两像素抬起来
                    rise = rise * rise * (3f - 2f * rise);
                    float fall = 1f - t;
                    a = peak * rise * fall * fall;                         // 再往外按平方收：比 smoothstep 更淡，不会看成一圈描边
                }
                px[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)), "shadow" + size);
    }

    // ---------- 面板 ----------

    // ---------- 顶层 tab（库 / 设置 / 开发者） ----------

    /// <summary>按「距屏幕顶部多少像素」定位一个矩形（上边 topPx、高 hPx、左 leftPx、宽 wPx；参考分辨率 1920x1080）。
    /// 只对父节点撑满整屏的情况有效（如 libRoot / _contentRoot），父节点不满屏时必须自己换算。</summary>

    // ---------- 字形预热（开机时加载） ----------

    /// <summary>把界面里出现过的每个字 × 每个字号一次性烘进字形图集，避免首次进新界面时卡一下
    /// （Unity 的字形是首次用到某个字/字号时同步生成的，那一下正好落在渲染前）。
    /// 只烘我们自己界面里出现过的字（画布名 `LevelLib_` 前缀），不烘整本字典——
    /// 几千个汉字会把图集撑到 2048² 以上，白占显存。</summary>
    private static void PrewarmFontGlyphs(out int glyphCount, out int groupCount) {
        glyphCount = 0; groupCount = 0;
        var map = new Dictionary<Font, Dictionary<long, HashSet<char>>>();
        try {
            // ① 扫我们自己所有画布上的 Text（含未激活的：预热的个人页就是藏着的）——
            // 走 `CollectOurGraphics` 问登记表，不能全场景搜（三万对象场景里 250ms 级，
            // 还跑在开机预热这条路上）
            var texts = new List<Text>();
            PortalUI.CollectOurGraphics(texts);
            for (int k = 0; k < texts.Count; k++) {
                var t = texts[k];
                if (t == null || t.font == null || string.IsNullOrEmpty(t.text)) continue;
                AddFontChars(map, t.font, t.fontSize, t.fontStyle, t.text);
            }
            // ② 之后才出现的字：欢迎那行（打字机，身份有/无两种写法）
            var welcome = _pfRefs != null ? _pfRefs.Welcome : null;
            if (welcome != null && welcome.font != null) {
                string uname = string.IsNullOrEmpty(PlayerName) ? DefaultPlayerName : PlayerName;
                string title = (PlayerTitle ?? "").Trim();
                AddFontChars(map, welcome.font, welcome.fontSize, welcome.fontStyle, "欢迎回来，" + uname);
                AddFontChars(map, welcome.font, welcome.fontSize, welcome.fontStyle, "欢迎回来，" + title + "，" + uname);
            }
            // ③ 大肥鱼全部台词（每次随机换一句，不烘就是"点一下鱼卡一下"）；两个模型的池子都要烘
            if (_petText != null && _petText.font != null) {
                var pools = PetLinesPool();
                for (int m = 0; m < pools.Length; m++) {
                    for (int i = 0; i < pools[m].Length; i++) {
                        AddFontChars(map, _petText.font, _petText.fontSize, _petText.fontStyle, pools[m][i]);
                    }
                }
            }
            // ④ 真正把字要进图集：这一步就是那次"卡一下"本身，只是提前到现在做
            foreach (var kv in map) {
                foreach (var g in kv.Value) {
                    var chars = new char[g.Value.Count];
                    g.Value.CopyTo(chars);
                    int size = (int)(g.Key >> 8);
                    FontStyle style = (FontStyle)(int)(g.Key & 0xFF);
                    try { kv.Key.RequestCharactersInTexture(new string(chars), size, style); } catch { }
                    groupCount++;
                    glyphCount += chars.Length;
                }
            }
            // ⑤ 让图集现在就重建完，别拖到下一帧玩家正看着的时候
            try { Canvas.ForceUpdateCanvases(); } catch { }
        } catch (System.Exception e) {
            Lg.Ex("字形预热失败（不影响功能 只是那三下还会现建）", e);
        }
    }

    /// <summary>把字符串里的字按（字体 × 字号 × 字形）分组收集，供预热用；空白字符不烘</summary>
    private static void AddFontChars(Dictionary<Font, Dictionary<long, HashSet<char>>> map,
                                     Font f, int size, FontStyle style, string s) {
        if (f == null || string.IsNullOrEmpty(s)) return;
        long key = ((long)size << 8) | (long)(int)style;
        if (!map.TryGetValue(f, out var inner)) { inner = new Dictionary<long, HashSet<char>>(); map[f] = inner; }
        if (!inner.TryGetValue(key, out var set)) { set = new HashSet<char>(); inner[key] = set; }
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            if (c == '\n' || c == '\r' || c == ' ' || c == '\t') continue;
            set.Add(c);
        }
    }
}
