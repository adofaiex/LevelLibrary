// LevelLibrary 的形状与动效尺：圆角只用这几档，交互动效只用这几个时长。
// 起因是量出来圆角有 6/8/10/13/16/18/32 七种散值、动效时长有 56 个不同取值，
// 同一个层级的控件在不同页面各写各的。这里只收"通用件"的口径 ——
// 个别动画（进谱面过渡、个人页渐隐这类整屏演出）允许有自己的时长，但要提成它自己的常量。
namespace LevelLibrary;

public static partial class PortalUI {
    // ---------- 圆角 ----------
    /// <summary>小控件：搜索框、开关轨道、色块</summary>
    public const float RadSmall = 8f;
    /// <summary>卡片与块：列表行、成就卡、大厅那几块、磨砂面板</summary>
    public const float RadCard = 10f;
    /// <summary>胶囊：导航排整条、区域胶囊、标签</summary>
    public const float RadPill = 14f;
    /// <summary>大面板：设置页 / 成就页 / 官谱库那几块整屏底板</summary>
    public const float RadPanel = 22f;

    // ---------- 交互动效时长 ----------
    /// <summary>悬停：指针移到可点件上（微交互，快）</summary>
    public const float MoHover = 0.12f;
    /// <summary>按下：按住那一下的回弹（比悬停更快，手感才脆）</summary>
    public const float MoPress = 0.06f;
    /// <summary>小过渡：开关滑动、单个块的显隐</summary>
    public const float MoSmall = 0.18f;
    /// <summary>页面过渡：切页、列表换序、弹窗弹入</summary>
    public const float MoPage = 0.30f;
    /// <summary>大过渡：整屏演出（进谱面过渡、个人页渐隐这类）</summary>
    public const float MoLarge = 0.45f;

    // ---------- 视觉权重（块底的深浅）----------
    /// <summary>实心：选中的块、强调条（alpha 0.92）</summary>
    public const float WtSolid = 0.92f;
    /// <summary>面板底：各页底板、详情区、弹窗盒子（alpha 0.85）</summary>
    public const float WtPanel = 0.85f;
    /// <summary>次级底：弹窗里的小块、列表视图（alpha 0.75）</summary>
    public const float WtSub = 0.75f;
    /// <summary>更淡的底：安静的分区、次要容器（alpha 0.55）</summary>
    public const float WtQuiet = 0.55f;
    /// <summary>安静的分区底：开发者页 / 榜单那种"再淡一档"的底（alpha 0.34）</summary>
    public const float WtQuietDeep = 0.34f;
    /// <summary>压暗罩：模态遮罩、全屏提示（alpha 0.45）</summary>
    public const float WtVeil = 0.45f;
    /// <summary>不透明底：整屏纯色底（alpha 1，只有 BgColor 这一档）</summary>
    public const float WtOpaque = 1f;

    // ---------- 密度（行高 / 卡片 / 组间距）----------
    /// <summary>设置页一行的高度</summary>
    public const float RhRow = 54f;
    /// <summary>谱面库列表一行的高度（比设置页松，列表要一条一条读）</summary>
    public const float RhListRow = 76f;
    /// <summary>成就卡的尺寸</summary>
    public const float CardW = 320f, CardH = 190f;
    /// <summary>组与组之间的默认间距（同一组内用 RhRow 的自然贴合）</summary>
    public const float GapGroup = 24f;

    // ---------- 焦点态与视觉效果的分工（口径，不是数值）----------
    // 焦点态（"当前在哪"）默认只有一种表达：主题色实心块 + 深色字（导航排、子 tab、列表行、区域列都走这套）。
    // 例外只有一个：主题色色块自己的选中环用白色描边（`ThemePick`）—— 它自己就是主题色，用主题色环会看不见。
    // 视觉效果各管一件事，不许叠：压暗罩管"模态/压暗"；磨砂管"材质"（登记过的块由 PanelGlass 插糊图）；
    // 描边管"边界"（外扩 2px 的空心框）；主题色管"状态"（选中/悬停由 Accent 或其派生色表达）。
}
