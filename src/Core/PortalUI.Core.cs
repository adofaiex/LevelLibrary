// [split-of] PortalUI.cs lines 593-937
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

/// <summary>PortalUI 的配色与主题色、面板透明度档位、面板开关的生命周期。</summary>
public static partial class PortalUI {
    /// <summary>面板 / 开始界面 / 登录界面那三块整屏底的底色。alpha 必须是 1：
    /// 它是所有背景（静态图 / 代码背景 / 视频）共享的最底那一层，而代码背景（见 PortalUI.NetBg.cs）是"强度写在 alpha 里、
    /// RGB 只有纯白或纯黑"的点线图，四层合起来平均只盖住屏幕百分之五 —— 它挡不住任何东西，
    /// 0.96 就等于 4% 的游戏画面一直透出来（有视频层时被视频盖住看不出来，没视频层、开始界面与
    /// 登录界面就直接露出来）。进谱面过渡层与屏保那两块盖层只取这里的 RGB、alpha 自己写死 1，不受影响。</summary>
    private static readonly Color BgColor = new Color(0.06f, 0.05f, 0.10f, 1f);
    private static readonly Color PanelColor = new Color(0.10f, 0.08f, 0.16f, 0.85f);
    /// <summary>弹窗那层全屏遮罩：真压暗 + 挡输入（模态语义：遮罩挡住的点击不该漏到背后的面板上）。
    /// 以前试过"不压暗、只挡输入"，结果是弹窗四周看着比周围亮、一弹出来像遮罩被擦掉了 ——
    /// 那时的问题出在糊图插错了位置（插在遮罩上面就不吃这份压暗），现在糊图插在遮罩下面，
    /// 压暗和磨砂可以同时成立（见 MakeDialogBox）。</summary>
    private static readonly Color DialogVeil = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>给一个弹窗铺上那层全屏遮挡层（统一入口，别在各处再写一遍）。
    /// 铺满整块画布、默认吃射线（弹窗是模态的）。顺手把刚建的这层记下来，
    /// 紧随其后的 MakeDialogBox 会把磨砂糊图插在它下面（见那条的说明）。</summary>
    private static Image MakeDialogVeil(Transform parent, bool blockInput = true) {
        var bg = MakeImage(parent, "Bg", DialogVeil);
        Stretch(bg.rectTransform, Vector2.zero, Vector2.one);
        bg.raycastTarget = blockInput;
        _lastDialogVeil = bg;
        return bg;
    }

    /// <summary>上一个弹窗遮罩（只给"紧接着建盒子"那一句用；用完就清掉，不跨弹窗留着）。</summary>
    private static Image _lastDialogVeil;

    /// <summary>弹窗盒子的不透明度：下面还要透出磨砂，所以不能太实（太实就看不见糊了）。</summary>
    private const float DialogBoxAlpha = 0.72f;

    /// <summary>弹窗入场：整体从下方浮上来这么多像素、用这么久（时长走设计尺"整页"那一档）。
    /// 浮的距离是观感量：太小看不出在动，太大像从屏幕外飞进来。</summary>
    private const float DialogInRise = 26f, DialogInSeconds = MoPage;

    /// <summary>弹窗那个盒子：半透明深色板 + 磨砂。
    /// 糊图必须插在遮罩下面（跟面板里那几块遮罩同一套做法）：插在遮罩上面的话，糊图不吃遮罩那 0.55 的压暗，
    /// 会比周围亮一截 —— 他以前说的「老是出现白色」就是它。所以这里用带 insertBefore 的那个重载。
    /// 注意 MainNotice 那种没有遮罩的盒子：_lastDialogVeil 为空 所以 不挂磨砂（照旧）。</summary>
    private static Image MakeDialogBox(Transform parent, string name, Color color) {
        color.a = Mathf.Min(color.a, DialogBoxAlpha);
        var box = MakeImage(parent, name, color);
        box.sprite = DialogRoundSprite;      // 弹窗比别的板大得多，四角用尺上最高那档才看得出是圆的
        var veil = _lastDialogVeil;
        _lastDialogVeil = null;
        if (veil != null && veil.transform.parent == parent) RegisterGlassPanel(box, parent, veil.transform);
        // 入场放在最后：磨砂要在盒子还是原样不透明的时候登记（它按那一刻的 alpha 当基准），
        // 摆好之后才轮到淡入 + 从下面浮上来
        AnimateFloatIn(box.rectTransform, box, veil, DialogInRise, DialogInSeconds);
        return box;
    }
    private static readonly Color ItemColorBase = new Color(0.18f, 0.15f, 0.28f, 0.55f);
    private static readonly Color ItemHoverBase = new Color(0.28f, 0.23f, 0.42f, 0.75f);
    // 主色（滑条选中段 / 按钮 / 选中态）取 FrameLineColor 的 RGB，alpha 保持 0.65：
    // 主色会铺在「整行选中」这种大面积上，透一点才看得清底下的字和背景。
    /// <summary>默认主题色（暖红，= 主题色九档里的「红」档）。玩家在设置里亲手选过的色存 settings 的
    /// ThemeColor；没选过的人一直用它（见 AutoFeatures.Hooks 里那次 ThemePicked 迁移）。</summary>
    public static readonly Color DefaultThemeColor = new Color(1f, 0.6f, 0.58f, 0.65f);
    /// <summary>当前主题色；唯一写入口是 PortalUI.ThemePick 的 ApplyThemeColor。</summary>
    public static Color ThemeColor = DefaultThemeColor;

    /// <summary>主题色。必须是属性而不是 readonly 字段：读它的地方都是「用的时候才算」。</summary>
    private static Color AccentBase { get { return ThemeColor; } }

    /// <summary>用主题色画的描边（MakeMaskFrame 建的那圈）；换主题色时一起重画，alpha 走面板透明度。</summary>
    private static readonly List<Image> _themeImages = new List<Image>();

    /// <summary>颜色 = 主题色、但 alpha 各用各的那批实心块（选中块 / 滑条滑块）。
    /// 与 _themeImages 分开的原因：描边重刷走 Accent（alpha 走面板透明度），实心块必须保留自己那档 alpha。
    /// 范围：设置页导航块 / 详情区 tab / 官谱库 tab 与区域块 / 子 tab / 成就左栏分类块 / 设置页滑条滑块。</summary>
    private static readonly List<Image> _fillImages = new List<Image>();

    /// <summary>登记一张实心高亮块图（建的时候调）；换主题色时按它自己的 alpha 重刷。</summary>
    private static void RegisterFillImage(Image img) {
        try { if (img != null && !_fillImages.Contains(img)) _fillImages.Add(img); } catch { }
    }

    /// <summary>登记一张颜色 = 主题色的图（建的时候调）。</summary>
    private static void RegisterThemeImage(Image img) {
        try { if (img != null && !_themeImages.Contains(img)) _themeImages.Add(img); } catch { }
    }

    /// <summary>主题色变了：把所有登记过的图重画一遍，并刷新受影响的底色。</summary>
    public static void RepaintThemeImages() {
        try {
            for (int i = _themeImages.Count - 1; i >= 0; i--) {
                var img = _themeImages[i];
                if (img == null) { _themeImages.RemoveAt(i); continue; }   // 页面重建过 → 清掉
                img.color = Accent;                                        // alpha 仍走面板透明度
            }
            // 实心选中块 / 滑条滑块：重刷时保留各自原来的 alpha（0.92 / 0.55 各档不同），不能用 Accent。
            for (int i = _fillImages.Count - 1; i >= 0; i--) {
                var img = _fillImages[i];
                if (img == null) { _fillImages.RemoveAt(i); continue; }
                float a = img.color.a;                                     // 它自己那档 alpha
                img.color = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, a);
            }
            // 斜切块（顶部导航排 + 主界面大厅）的底色与左侧竖条都取主题色：重画一遍
            try { RefreshTopNav(); } catch { }
            RefreshList();          // 列表选中块 / 描边用的是 Accent
            RefreshKeyHintBar();
            // 开关的「开」色 = 主题色，但它不在上面两张表里（开关是控件不是底色，建的时候自己画一次，
            // 见 ApplySwitch），所以换主题色时必须显式让设置页开关重画一遍，否则它们还是旧色。
            try { RefreshSettingsToggles(); } catch { }
            // 「排序方式」键的底色也是主题色（难度排序时用 Accent），与开关同理：换主题色 / 换面板透明度时要重画。
            try { PaintSortButton(); } catch { }
            Lg.Info($"[LevelLib] 主题色已重画：RGB({ThemeColor.r:F2}, {ThemeColor.g:F2}, {ThemeColor.b:F2})"
                + $" 共 {_themeImages.Count} 张图");
        } catch (System.Exception e) { Lg.Ex("重画主题色失败", e); }
    }

    private static readonly Color MissColorBase = new Color(0.18f, 0.15f, 0.28f, 0.30f);   // 缺失谱面行颜色

    /// <summary>面板各块遮罩底色的固定系数 0.40（原来是「面板背景透明度」滑条推到 100 时的量）。
    /// 那条滑条已整块换成「面板背景模糊」（PanelBlur，见 PortalUI.PanelGlass.cs），遮罩深浅就冻在这个数上，
    /// 升级上来的玩家界面一个像素都没变。想再透 / 再实只动这一个数，别在各页各写一份。</summary>
    private const float PanelAlphaFloor = 0.40f;

    /// <summary>把基准色按 PanelAlphaFloor 缩放 alpha；原来跟着透明度滑条走，现在恒定。</summary>
    private static Color WithPanelAlpha(Color baseColor) {
        return new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * PanelAlphaFloor);
    }

    // 列表行配色：基准色 + PanelAlphaFloor 缩放。用属性每次读都现算，改了基准色立刻全局生效
    private static Color ItemColor { get { return WithPanelAlpha(ItemColorBase); } }
    private static Color ItemHover { get { return WithPanelAlpha(ItemHoverBase); } }
    private static Color Accent { get { return WithPanelAlpha(AccentBase); } }

    // ---------- 斜切块的配色（顶部导航排 + 主界面大厅共用一套） ----------
    /// <summary>顶部导航那五片的底色：那里背后是游戏画面（没有糊图垫着），得留够不透明度才不吃背景的杂色。</summary>
    private static readonly Color SlabFill = new Color(0.16f, 0.17f, 0.25f, 0.80f);
    /// <summary>导航里不可点的那种（目前没有，留着给以后同族的底板）</summary>
    private static readonly Color SlabFillQuiet = new Color(0.15f, 0.16f, 0.23f, 0.60f);
    /// <summary>大厅那五块的底色：它们压在一块真模糊的遮罩上，所以自身要很透，
    /// 让底下那层糊图透出来 —— 要的"半透明的模糊背景"就是这个意思（不是各块自己再裁一张糊图：
    /// 每块都是平行四边形，矩形糊图会从斜边外露出来）。</summary>
    private static readonly Color SlabGlassFill = new Color(0.10f, 0.11f, 0.18f, 0.40f);
    /// <summary>玻璃边那圈细线的颜色：很淡的白，只用来把轮廓钉住（块本身半透明，没有它边缘会跟糊图糊在一起）</summary>
    private static readonly Color SlabRimColor = new Color(1f, 1f, 1f, 0.20f);
    /// <summary>不可点的两块（统计条 / 玩家卡）再透一档，免得玩家以为它们能点</summary>
    private static readonly Color SlabGlassQuiet = new Color(0.09f, 0.10f, 0.16f, 0.30f);
    /// <summary>按下时的色彩乘算（小于 1 = 压暗，乘算所以不用另算一个颜色）。
    /// 悬停那档不走这里：乘出来的亮度在这几个贴着遮罩色的块上看不出来，改由 PortalUI.HubHover.cs 与
    /// LayoutTopNav 直接写颜色。</summary>
    private static readonly Color SlabPressMul = new Color(0.86f, 0.86f, 0.86f, 1.00f);
    /// <summary>斜切块上的主标题（冷灰蓝，不用纯白：纯白会跟主题色副标题抢重心）</summary>
    private static readonly Color SlabTitleColor = new Color(0.90f, 0.92f, 0.96f, 1f);
    /// <summary>斜切块左侧那条竖条 / 副标题的颜色：跟随主题色，满亮（方舟那条是实心的）</summary>
    private static Color SlabAccentColor { get { return new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 0.95f); } }
    private static Color MissColor { get { return WithPanelAlpha(MissColorBase); } }
    // 右键菜单配色（行尾文字按钮已删，改为右键菜单，见 PortalUI.ContextMenu.cs）：
    // 重命名 = 蓝 / 标记 = 绿 / 打开 = 橙 / 删除 = 红
    private static readonly Color RowBtnBlue = new Color(0.25f, 0.45f, 0.7f, 0.85f);
    private static readonly Color RowBtnGreen = new Color(0.3f, 0.55f, 0.35f, 0.85f);
    private static readonly Color RowBtnOrange = new Color(0.75f, 0.5f, 0.2f, 0.85f);
    private static readonly Color RowBtnRed = new Color(0.6f, 0.25f, 0.25f, 0.85f);
    /// <summary>右键菜单「导出」项的颜色；与其它四项同一个明度 / 透明度族。</summary>
    private static readonly Color RowBtnPurple = new Color(0.45f, 0.35f, 0.65f, 0.85f);
    private static Image _panelBgImg;   // 面板纯色底（代码背景挂在它上面一层，见 PortalUI.NetBg.cs）
    // 两块底色：基准色单独存一份，免得反复缩放 alpha 越缩越小；它们也是要磨砂的遮罩
    private static Image _detailAreaImg;              // 详情区底色
    private static Color _detailAreaImgBase;
    private static Image _listViewportImg;            // 谱面列表底色
    private static Color _listViewportImgBase;
    // ---------- 「未选中拉满 / 选中展开」动画用的引用与进度 ----------
    private static RectTransform _listViewportRt;     // 列表可视区（左边界跟着详情区一起动）
    private static RectTransform _libScrollbarRt;     // 列表竖向滚动条（高度跟着列表一起变）
    private static RectTransform _letterColRt;        // 字母定位条（同上）
    private static GameObject _libListRootGo;         // 谱面列表整块（含滚动条）：启动页要把它整块调透明
    private static RectTransform _libSearchBgRt;      // 搜索框（同上）
    /// <summary>搜索输入框本体：清空筛选时必须连它的显示文字一起重置，只清 _search 用户会以为没清掉。</summary>
    private static InputField _libSearchInput;
    private static RectTransform _detailAreaRt;       // 详情区整块（收起时宽度→0）
    private static GameObject _detailAreaGo;          // 详情区 GameObject（收干净后整块关掉省绘制）
    private static CanvasGroup _detailGroup;          // 详情区整体淡出/淡入（内容层关掉后只剩背景那一张图）
    private static GameObject _detailInner;           // 详情区内容层（收起动画期间整层关掉：省性能 + 不被挤扁）
    private static GameObject _rankSliderGo;          // 难度滑条（未选中时跟详情区一起藏起来）
    private static Text _emptyLibHint;                // 空库提示（一个谱面都没有时显示）
    /// <summary>空库那一块（提示 + 单个导入 / 批量导入两个按钮）；显隐只切这一个容器。</summary>
    private static RectTransform _emptyLibBox;
    private static Text _emptyImportOneText, _emptyImportBatchText;
    private static float _libExpand, _libExpandTarget;   // 0 = 详情区展开（列表在 899），1 = 收起（列表拉到 62）
    private static Image _panelAvatarImg;   // 右上角玩家头像 Image
    private static Text _panelNameText;     // 右上角玩家名字 Text
    private static RectTransform _playerBoxRt;   // 玩家信息框（位置调整用）
    private static Canvas _playerBoxCanvas;      // 界面外显示的独立 Canvas
    private static Image _outsideAvatarImg;      // 外部框头像
    private static Text _outsideNameText;        // 外部框名字
    private static Text _outsideStatText;        // 外部框两行数据（时长/游玩次数）
    private static Image _outsideBadgeImg;       // 外部框：严判最高等级图
    private static Text _panelStatText;          // 面板内两行数据
    private static Image _panelBadgeImg;         // 面板内：严判最高等级图

    /// <summary>个人信息栏尺寸（含名字下方两行数据）。</summary>
    private static readonly Vector2 PlayerBoxSize = new Vector2(330f, 110f);

    /// <summary>个人信息栏那块半透明玻璃的底色；面板内 Tabs 那份与界面外 PlayerBox 那份共用这一个颜色，
    /// 「界面内外显示一致」就靠它，谁也别再各写一份字面量。alpha 0.55 是折中：面板内那份后面是
    /// 我们自己的背景图 / 视频（能真模糊，见 PortalUI.PanelGlass.cs），界面外那份后面是游戏画面、只能靠半透明。</summary>
    private static readonly Color PlayerBoxColor = new Color(0.13f, 0.15f, 0.22f, 0.55f);

    /// <summary>取玩家头像 sprite：只认玩家自己选的那张，没设过就返回 null（由调用方画淡灰圆）。</summary>
    private static Sprite ResolvePlayerAvatarSprite() {
        try {
            if (!string.IsNullOrEmpty(AvatarPath) && File.Exists(AvatarPath)) {
                var s = LoadAssetSprite(AvatarPath);
                if (s != null) return s;
            }
        } catch { }
        return null;
    }

    /// <summary>没设过头像时那个淡灰实心圆：只在动效还没烘好（或烘失败）时兜底，
    /// 正常情况走 orb（见 PortalUI.FluidOrb.cs）。</summary>
    private static readonly Color AvatarPlaceholderColor = new Color(0.80f, 0.80f, 0.83f, 1f);

    /// <summary>所有显示头像的地方都走这一个函数，它永远不会留下「没有 sprite 的 Image」：
    /// uGUI 对没 sprite 的 Image 会画成白的（玩家说的空白图）。
    /// 顺序：玩家自定义（原样，一个字不改）→ 默认头像动效（复刻 fluid-orb）→ 淡灰实心圆（动效没就绪时）。
    /// 走动效那一支时父物体自己的图要压成全透明：orb 的圆边缘比圆角贴图的柔化边窄，
    /// 父物体留着不透明就会在圆外露一圈灰的。</summary>
    private static void ApplyAvatarTo(Image img) {
        if (img == null) return;
        try {
            var s = ResolvePlayerAvatarSprite();
            if (s != null) {
                img.sprite = s;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;      // 非正方形的自定义头像也不拉伸变形
                img.color = Color.white;
                return;
            }
            // 流体球（照搬 fluid-orb 的结构）：这个图元自己就是那颗球 —— sprite 是全局唯一那张
            // 常驻贴图、颜色纯白（贴图的 alpha 已经做了圆形边缘）。没有第二层，也就没有"某一层拿不到贴图"。
            var orb = FluidOrbSprite();
            if (orb != null) {
                img.sprite = orb;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = Color.white;
                RegisterFluidOwner(img);          // 每帧重算那张贴图（只更新贴图，不碰图元属性）
                return;
            }
            var circle = EnsureCircleSprite();
            if (circle != null) { img.sprite = circle; img.type = Image.Type.Simple; img.preserveAspect = true; }
            img.color = AvatarPlaceholderColor;   // 动效建不起来：淡灰圆顶着
        } catch { }
    }
    private static RectTransform _outsideBoxRt;  // 外部框（位置同步）

    // ---------- 生命周期 ----------

    public static void Start() {
        _uiFont = FindUiFont();
        // 这行日志用来证伪「界面字体取自谁」：FindUiFont 可能捡到我们自己画的字
        // （楷体标题被当成通用字体，之后所有 Text 全用楷体），有它就能一眼看出捡到的是游戏 UI 还是我们的。
        try {
            Lg.Info("界面字体取自：" + _uiFontSource
                + "（字体名 " + (_uiFont != null ? _uiFont.name : "(null)") + "）"
                + (_uiFontFromOurs.Length > 0 ? "  场上也有我们自己的字：" + _uiFontFromOurs + "（已排除 不当通用字体）" : "")
                + (_uiFontReplaced != null ? " ｜  这台机器装过字体 mod：原生 UI 在用 " + _uiFontReplacedSource
                    + "（已排除 那种字体的图集会让大标题投影缺字）" : ""));
        } catch { }
        _panelOpen = false;
        _editorLoading = false;
        LoadHotkey();
    }

    /// <summary>读取快捷键（hotkey.txt）</summary>
    public static void LoadHotkey() {
        try {
            string path = Path.Combine(Main.ModEntry.Path, "hotkey.txt");
            if (File.Exists(path)) {
                var k = (KeyCode)System.Enum.Parse(typeof(KeyCode), File.ReadAllText(path).Trim(), true);
                if ((int)k > 0 && (int)k < 323) Hotkey = k;
            }
        } catch { }
    }

    /// <summary>保存快捷键（hotkey.txt）</summary>
    public static void SaveHotkey(KeyCode k) {
        try {
            File.WriteAllText(Path.Combine(Main.ModEntry.Path, "hotkey.txt"), k.ToString());
        } catch { }
    }

    public static void Stop() {
        ClosePanel();
        CloseRenameDialog();
    }

    /// <summary>通关记录行里的倍速标签：音高 110 → 1.1x。
    /// 老记录没有音高那一列（Pitch = 0）→ 返回空串，不编造一个 1.0x。
    /// 非 1.0x 用橙色突出，1.0x 用灰。</summary>
    private static string SpeedTag(LevelScanner.ClearRecord r) {
        if (r == null || r.Pitch <= 0) return "";
        float mul = r.Pitch / 100f;
        string color = (r.Pitch == 100) ? "#9AA5B1" : "#FFAA55";
        return " <color=" + color + ">" + mul.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "x</color>";
    }

    /// <summary>谱面列表变化（重新扫描后）</summary>
    public static void OnLibraryChanged() {
        if (_panelOpen) RefreshList();
    }

    /// <summary>谱面库面板是否打开（主菜单冻结用）</summary>
    public static bool IsOpen => _panelOpen;

    // 面板打开时把 conductor.bpm 置 0（球停、beat 回调不发），关面板还原：
    // 见 MuteConductorBpm / RestoreConductorBpm。不要恢复「关面板后纠正游戏暂停状态」那套机制，
    // 它只会和游戏自己的暂停系统互相干扰（把状态改一半反而更糟）。

    /// <summary>UMM 设置页按钮 / 快捷键入口</summary>
    public static void TogglePanel() { TogglePanelFrom("快捷键"); }

    /// <summary>不带"切换"语义的打开入口（开机自动进入那两条路），只为把"谁打开的"记进日志。</summary>
    public static void OpenPanelFrom(string who) {
        _panelOpenSource = who;
        OpenPanel();
    }

    /// <summary>记录「这次是谁把面板打开的」；快捷键 / UMM 按钮 / 开机自动打开三个入口都走这里。
    /// 从 UMM 按钮进来有个本质区别：那一刻 UMM 自己的 IMGUI 窗口还在最上层，游戏的 Input 正被 UMM 读，
    /// 所以 TickPanel 里还有「面板开着就保证输入是关的」兜底（EnsureGameInputOffWhilePanelOpen）。</summary>
    public static void TogglePanelFrom(string who) {
        _panelOpenSource = who;
        // 状态一致性守卫：以为开着、其实画布已经没了时，ClosePanel 什么都没做而 OpenPanel 又被
        // 画布非空挡回，快捷键就成了哑的；先按画布是否真的存在纠正状态，再决定开关。
        if (_panelOpen && _panelCanvas == null) {
            _panelOpen = false;
            _closing = false;
            Time.timeScale = _prevTimeScale > 0.0001f ? _prevTimeScale : 1f;
        }
        Lg.Info($"[LevelLib] 【{who}】TogglePanel：面板开={_panelOpen} 画布={(_panelCanvas != null)}"
            + $" timeScale={Time.timeScale:F2} → {(_panelOpen ? "关" : "开")}"
            + $"（当前游戏输入={(GameInputAllowed ? "开" : "关")}）");
        // 关闭这一侧也要淡出：走 RequestClosePanel（它只立 _closing 标记，透明度 1→0 的动画在 TickCore 里跑完才真销毁）。
        if (_panelOpen) RequestClosePanel();
        else OpenPanel();
    }

    /// <summary>这次面板是被谁打开的；只给日志用。</summary>
    private static string _panelOpenSource = "(未知)";
    public static string PanelOpenSource => _panelOpenSource;

    // ---------- 每帧 ----------

    // ---------- 扫描文件夹批量导入 ----------

    /// <summary>选择文件夹（新式资源管理器对话框）</summary>
    private static string PickFolder() {
        return ModernFileDialog.OpenFolder("选择要扫描的文件夹（含 .adofai 的子文件夹都会导入）");
    }

    /// <summary>满分彩虹文本：全局统一刷新（单个循环驱动全部，不占 MonoBehaviour 开销）</summary>
}
