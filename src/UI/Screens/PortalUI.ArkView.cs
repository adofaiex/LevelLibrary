using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：主界面本体 —— 按桌面那份 ArknightsUI-React-Template
/// 的示意图逐个元素重建（坐标、尺寸、层级全部照抄那份源码里的 Tailwind 类名与数值）。
/// 布局对照（模板 CSS px 到 本项目 1920 基准 px，×1.2）
///     画布 1600x900  到1920x1080（项目基准，见 Toolkit 的 CanvasScaler）
///     左侧 2D 区     到 ArkLeftRoot（不参与 3D 旋转）
///     左侧 3D 区     到 ArkLayerFar（rotateY +6°）
///     右侧 3D 区     到 ArkLayerNear（rotateY −6°）
/// 三层深度（App.tsx:225/237 的 translate-z-10=40px、-20=80px）
///     近层 Z=0   中层 Z=−40   远层 Z=−80
/// 每个元素的名字与它对应示意图里的哪一块写在注释上，方便逐条核对。</summary>
public static partial class PortalUI {

    // ================= 示意图数值（全部读自 src/components 与 App.tsx，勿凭感觉改）=================
    // 一份 换算约定，全文所有布局常量都按它写：
    //   示意图画布 1600x900（App.tsx 最外层 div 写死的 width/height），本项目画布 1920x1080
    //   （Toolkit 的 CanvasScaler 参考分辨率），两向都整除，系数 1.2
    //   常量一律保留示意图的 CSS px 原值（好与那份源码逐条核对），乘以 ArkPx 才是项目画布单位。
    // 还有一条容易踩的：FlexContainer 的 gap 是写在 inline style 里的，className 上的 gap-8 之类
    //   会被它覆盖，真实间距要按 inline 的 gap 值算（默认 gap=6，即 24 CSS px）。
    private const float ArkPx = 1.2f;

    // 左侧四枚图标（App.tsx: FlexContainer mt-8 ml-8 + 四个 w-16）
    private const float ArkIconSize = 64f;        // w-16 = 4rem

    // 等级圆环（Icons.tsx LevelIcon：mt-48 ml-12 h-36 w-36，环内还有 LV 一行）
    private const float ArkRingSize = 144f;          // h-36 w-36 = 9rem
    private const float ArkRingX = 48f;              // ml-12 = 3rem
    private const float ArkRingY = 288f;             // 图标行底 96 + mt-48(192)

    // 用户名与 ID（App.tsx：text-large ml-12 与 text-x-small ml-12，紧接等级环之后）
    private const float ArkNameY = 432f;
    private const float ArkIdY = 482f;

    // VOICE 对话框（App.tsx：BaseButton bg-dialog-bg mt-140 ml-2 h-32 w-122）
    // 左侧 3D 那组是底部对齐的（LargeContainer 的 justify-end）加上底边距 mb-15(60)，
    // 内容总高 = 560(VOICE 的 mt-140) + 128 + 24(gap-6) + 144(新闻行) = 856，
    // 于是整组顶边落在 900 - 60 - 856 = -16，框顶 = -16 + 560 = 544。
    private const float ArkDlgX = 56f;               // 3D 容器左 48(ml-12) + 自身 ml-2(8)
    private const float ArkDlgY = 544f;
    private const float ArkDlgW = 488f;              // w-122 = 30.5rem
    private const float ArkDlgH = 128f;             // h-32 = 8rem
    private const float ArkVoiceTagW = 160f;         // w-40 = 10rem

    // 两枚圆钮与渐变分隔线（App.tsx：absolute mt-112 ml-68、两个 w-12、inline gap=2、分隔线 w-36 h-0.5）
    // 它是 absolute 且祖先里没有 relative，定位基准是整屏那个 absolute inset-0，不是 3D 容器。
    private const float ArkRoundSize = 48f;          // w-12 = 3rem
    private const float ArkRoundGap = 8f;            // inline gap=2
    private const float ArkRoundX = 272f;            // ml-68 = 17rem
    private const float ArkRoundY = 448f;            // mt-112 = 28rem

    // 新闻轮播（Columns.tsx NewsCol：ml-2 h-34 w-72；标签 w-33；banner inline gap = gap*4 = 24）
    private const float ArkNewsW = 288f;             // w-72
    private const float ArkNewsH = 136f;             // h-34
    private const float ArkNewsX = 56f;              // 3D 容器左 48 + 自身 ml-2(8)
    private const float ArkNewsY = 696f;             // 新闻行顶 = -16 + 560 + 128 + 24
    private const float ArkNewsTagW = 132f;          // w-33 = 8.25rem
    private const float ArkNewsGap = 24f;            // banner 之间 gap = 6*4
    private const float ArkNewsPage = ArkNewsW + ArkNewsGap;   // 翻一页的位移

    // 好友 / 档案（Buttons.tsx：h-17 w-48；在新闻右侧、-ml-3 压过来 12px，两枚之间 inline gap=2）
    private const float ArkSideBtnW = 192f;          // w-48 = 12rem
    private const float ArkSideBtnH = 68f;           // h-17 = 4.25rem
    private const float ArkSideX = 356f;             // 新闻右(56+288) + gap 24 - 12
    private const float ArkSideY0 = 696f;            // 与新闻同一行顶
    private const float ArkSideGap = 8f;

    // 右侧六行：容器是 items-end（右对齐）+ justify-center（纵向居中），
    // 行宽各不相同，每行都从"右边界"往回推位置，不能共用一个左边距。
    private const float ArkRightEdge = 1600f;        // 模板贴容器右边（CSS px），换算后即 1920
    // 原 30（内容总高 840、容器 900 居中的推导值）；用户要求时间行往上挪，提到 20。
    // 数量行跟着走（见 ArkDropDatetime 的注释），terminal 及以下的原位由 ArkDropItems 补回。
    private const float ArkYDatetime = 20f;

    // 右区纵向压缩。为什么需要：右区被 -6 度透视放大后，整块纵向总深会从模板的名义 910
    // 涨到约 1120（放大率随离透视原点的距离增加），而画布只有 1080。
    // 模板那边靠 LargeContainer 的 overflow-hidden 把上下裁掉；本项目的画布边缘就是屏幕边缘，
    // 裁掉就是实机看到的"日期顶到屏幕外、任务基建掉出屏幕下沿"。
    // 口径：对右区整体做一次等比纵向缩放（等价 CSS 的 scaleY(k)，origin 取整个右块的顶边），
    // 行高、行间落差、外边距、行内元素的相对位置全部一起乘 k。
    // 为什么不能只压"行间距离"而保留行高：模板里六行的落差之和远小于行高之和
    // （落差合计 16+0+24+24+24 再加末行 40，行高合计 24+48+220+160+140+160 = 752），
    // 只压落差省不出足够的空间 —— 压到系数 0.4，底边仍在 1091，依然出屏；行高才是大头。
    // 只压落差还会拆散 items 与 terminal 那对：模板里 TerminalButton 的 -mt-6(24) 正好抵消
    // gap-6(24)，两块严丝合缝贴着；只压 gap 不压外边距就会压出重叠，再往下每一行都跟着叠。
    // 一起乘 k 则全程保持模板的相对关系，缩放只是把整块变小，不会产生任何新的重叠。
    // 另外：六行之间发生"纵向轻微咬合"是模板透视的固有结果，不是缺陷，不要去修。
    // 拿模板名义布局代入同一套 perspective(480px) rotateY(-6) 量过：日期与资源叠 45、
    // 资源与终端叠 59、采购与任务叠 13，日期顶与任务底还各出屏一次。模板在浏览器里靠
    // LargeContainer 的 overflow-hidden 裁掉出屏部分，剩下的咬合则因为每行底板各自半透明、
    // 边界清楚，呈现的是层次感。本项目画布边缘就是屏幕边缘，所以这里只能把整块压小到
    // 全部落在屏内，咬合本身按模板保留。
    // 系数取 0.865：0.88 用户嫌大、0.85 是最初的基准，取两者之间偏小的一档（用户要求
    // "比这次放大前大一点"）。探针量过 0.9 会出屏，0.865 有足够余量。
    private const float ARK_VSCALE = 0.865f;
    /// <summary>把右区的一个纵向 CSS 长度按 ARK_VSCALE 压（行高、落差、外边距、行内纵向偏移都走它）。
    /// 纵向的换算只准从这里出，不许再直接写 CSS 原值。</summary>
    private static float ArkVS(float cssV) { return cssV * ARK_VSCALE; }

    // 模板里的行间落差（只作推导用；实际摆位一律由下面那组 ArkY* 推出）
    // 这两个值不是模板原值了：用户要求"理智值栏上方的时间行与数量行各往上挪一点"，
    // 同时理智值（terminal）及以下保持原位 —— 所以上面两行挪上去多少，ArkDropItems 就补回多少，
    // 差额全部落在"数量行与理智值之间"的空隙里（那里模板本来就是贴死的，松一点不难看）。
    private const float ArkDropDatetime = 8f;        // 原 16，数量行随之再上移 8
    private const float ArkDropItems = 20f;          // 原 0（模板 gap24 与 -mt-6 正好抵消），现在补时间/数量两行的上移量
    private const float ArkDropTerminal = 24f;
    private const float ArkDropRow1 = 24f;
    private const float ArkDropRow2 = 24f;

    /// <summary>模板第 6 行的 translate-y-10（10 单位 = 40 CSS px）。同样过纵向缩放。
    /// 原 40；采购行两轮加粗后第 6 行被顶出屏幕太多，回调到 24 找回约 14，
    /// 让底部出屏量回到此前接受的水平（约 16px，极端视差）。</summary>
    private const float ArkRow3DownRaw = 24f;
    private static readonly float ArkRow3Down = ArkVS(ArkRow3DownRaw);

    private const float ArkHDatetime = 24f;          // h-6
    private const float ArkWDatetime = 740f;         // w-185
    private const float ArkWItems = 760f;            // w-190
    private const float ArkHItems = 48f;             // h-12
    private const float ArkHTerminal = 220f;         // h-55
    private const float ArkWTerminal = 896f;         // w-220，调大一档（基准 880，用户嫌 912 大）
    private const float ArkHSmall = 160f;            // h-40
    private const float ArkWSmall = 354f;            // w-85，调大一档（基准 340）
    private const float ArkHStore = 192f;            // h-35，按用户要求两轮加粗（140 到 164 再到 192，顶边不动、往下长）
    private const float ArkWStore = 252f;            // w-60，调大一档（基准 240）
    // w-100 在模板里是 400，但里面放的是两枚 w-53(212) 的子按钮，去掉 p-2 两边只剩 384，
    // 装不下 424 —— 模板那边是靠父级 overflow-hidden 把右边裁掉的。本项目画布边缘就是屏幕边缘，
    // 裁掉就是"干员寻访超出去了"，所以这里按"容得下"取宽：p-2 两边 16 + 两枚子按钮。
    // 宽度是横向的，不受纵向缩放影响。子按钮与外框同比例调大（212/220、440/456）。
    private const float ArkWRecurit = 456f;
    private const float ArkWRecruitBtn = 220f;
    private const float ArkHRecruitBtn = 88f;        // h-22
    private const float ArkWDepot = 100f;            // w-24，调大一档（基准 96）
    /// <summary>右区各 flex 行里元素之间的横向间距：FlexContainer 的 inline gap 默认是 6，即 1.5rem。
    /// 横向不受纵向缩放影响，保持 CSS 原值。</summary>
    private const float ArkRowGap = 24f;

    // 右区六行的行顶与行高（都过纵向缩放）。行高写成一族常量，调用点直接引用，
    // 免得某个地方忘了乘系数 —— 那会让那一行比别的高，块内立刻错位。
    private static readonly float ArkYDatetimeTop = ArkYDatetime;
    private static readonly float ArkHDateS = ArkVS(ArkHDatetime);
    private static readonly float ArkHItemsS = ArkVS(ArkHItems);
    private static readonly float ArkHTerminalS = ArkVS(ArkHTerminal);
    private static readonly float ArkHSmallS = ArkVS(ArkHSmall);
    private static readonly float ArkHStoreS = ArkVS(ArkHStore);
    private static readonly float ArkYItems = ArkYDatetimeTop + ArkHDateS + ArkVS(ArkDropDatetime);
    private static readonly float ArkYTerminal = ArkYItems + ArkHItemsS + ArkVS(ArkDropItems);
    private static readonly float ArkYRow1 = ArkYTerminal + ArkHTerminalS + ArkVS(ArkDropTerminal);
    private static readonly float ArkYRow2 = ArkYRow1 + ArkHSmallS + ArkVS(ArkDropRow1);
    private static readonly float ArkYRow3 = ArkYRow2 + ArkHStoreS + ArkVS(ArkDropRow2);

    // 倾斜与位移（模板 ThreeDContainer：perspective(30rem) rotateY(±6deg) rotateX(...) translateX(...)）
    // 这块画布走 Overlay，没有相机可用，所以透视由 ArkPerspective 逐顶点算，观感与模板一致。
    private const float ArkCamZ = 480f;              // perspective(30rem) 的观察距离
    private const float ArkYawLeft = 6f, ArkYawRight = -6f;

    // 左右两区的横向避让量（项目单位，正数往右）。
    // 这是对模板的一处显式偏离：模板里两组内容分别贴容器左缘和右缘，靠 LargeContainer 的
    // overflow-hidden 裁掉溢出的部分；本项目的画布边缘就是屏幕边缘，裁掉就等于元素飞出屏幕，
    // 实机看是"左侧界面超出左侧屏幕、左上角按钮超出屏幕"。
    // 透视会把离原点越远的部分往外推得越多，鼠标移到同侧时还有一份反向平移（tranx）叠加，
    // 所以这里按最坏叠加量留余量（两个方向各留了一点，免得刚好卡在边缘上）。
    // 量过的数：左区最左一块原本到 -46、右区最右一块原本到 +27（都是项目单位）。
    private const float ArkPadFar = 82f * ArkPx;     // 左区右移，让最左缘回到屏内
    // 右区横移：原 -80 是按"最右缘刚好回屏内"取的；用户先后两次要求整块往右挪（-80 → -56 → -32）。
    // 现在的边界口径：普通行右缘贴住/贴到屏幕边缘，探出去的部分（编队行的行尾占位块、
    // 任务行右端、terminal 一点点）按模板本意由屏幕边缘裁掉 —— 不再追求"全部元素都在屏内"。
    private const float ArkPadNear = -32f * ArkPx;   // 右区左移量（比上轮再收 24 = 整块再右移 24）

    // 左区整体下移量（项目单位，正数往下）。为什么也需要它：左上角四枚图标原本 mt-8(32) 起，
    // 而透视在竖直方向也会把离原点远的"往前推"（俯仰角虽然只有 ±0.28 度，但图标离原点很远），
    // 最坏情况下顶边只剩几个项目单位 —— 实机看就是"左上角两个按键太高了超出屏幕了"。
    // 下移不影响左区内部任何相对位置，是整体平移，所以对布局没有副作用。
    private const float ArkPadDown = 40f * ArkPx;

    // 左区整体纵向缩放。左区比右区还要长：内容从图标上缘(32)一直排到新闻窗下缘(832)，跨 800；
    // 被 +6 度透视放大后最坏能到 1093 项目单位，而画布只有 1080。
    // 只靠 ArkPadDown 平移动不了这一件事 —— 往下一挪，图标是好看了，新闻窗下缘又出屏。
    // 所以同样做一次等比纵向缩放（等价 CSS scaleY，origin 取左区内容上缘 ArkIconY0），
    // 纵向的位置与高度一起乘 ARK_VSCALE_L，块内所有相对关系保持不变。
    private const float ARK_VSCALE_L = 0.94f;
    /// <summary>左区内容上缘（纵向缩放的锚点）：四枚图标那行的上缘。</summary>
    private const float ARK_VTOP_L = 32f;            // ArkIconY0
    /// <summary>左区纵向位置按 ARK_VSCALE_L 压（锚在 ARK_VTOP_L）。左区所有 top 都走它。</summary>
    private static float ArkVYL(float cssTop) { return ARK_VTOP_L + (cssTop - ARK_VTOP_L) * ARK_VSCALE_L; }
    /// <summary>左区纵向高度按 ARK_VSCALE_L 压。</summary>
    private static float ArkVHL(float cssH) { return cssH * ARK_VSCALE_L; }

    // 右区整体下移量。右区被 -6 度透视放大后，块内最上面那行（日期）会被往上推，
    // 而整块是纵向居中的，只压纵向间距救不回它 —— 需要整体挪一点。
    // 原 24，用户要求再往下挪，加到 44；后又要求整体再往下一点，加到 56（可接受极端视差下底部探出一点）。
    private const float ArkPadDownRight = 56f * ArkPx;


    // 字号（index.css 的 @theme 是 CSS px，这里已按 ArkPx 换算成整数）
    private const int ArkFsHuge = 115, ArkFsXxLarge = 67, ArkFsXLarge = 48;
    private const int ArkFsLarge = 38, ArkFsNormal = 29, ArkFsSmall = 23, ArkFsXSmall = 19;

    // ================= 摆位（换算的唯一出口）=================

    /// <summary>按示意图的 CSS px 摆位：内部乘 ArkPx 换算到项目画布单位。
    /// 布局代码一律走它，换算只发生在这一个地方 —— 常量保持示意图原值，出问题能逐条对照那份源码。</summary>
    private static void ArkAtPx(RectTransform rt, float leftCss, float topCss, float wCss, float hCss) {
        SetAtPx(rt, leftCss * ArkPx, topCss * ArkPx, wCss * ArkPx, hCss * ArkPx);
    }

    /// <summary>左区摆位：与 ArkAtPx 同一套，但纵向（top 与 h）先过 ARK_VSCALE_L。
    /// 左区整体被纵向缩过，所有左区元素都必须走这里 —— 漏一个就会比邻居高出来，块内立刻错位。
    /// 横向（left/w）不受影响。</summary>
    private static void ArkVAt(RectTransform rt, float leftCss, float topCss, float wCss, float hCss) {
        ArkAtPx(rt, leftCss, ArkVYL(topCss), wCss, ArkVHL(hCss));
    }


    /// <summary>右侧各行"从右边界往回推"的左缘（返回值仍是示意图 CSS px）。
    /// 模板那边是 items-end，每行各自右对齐、行宽又各不相同，共用一个左边距必然错开。</summary>
    private static float ArkRightLeft(float rowWidthCss) {
        return ArkRightEdge - rowWidthCss;
    }

    // ================= 引用 =================
    private static RectTransform _arkRoot, _arkLayerFar, _arkLayerNear;
    private static Camera _arkCam;
    private static Canvas _arkCanvas;
    private static bool _ark3DReady;
    // 两层各自的"逐顶点透视"组件。倾斜不是靠旋转层做到的（那样在 Overlay 下没有透视），
    // 而是由这些组件在图元顶点上算针孔投影，见 PortalUI.ArkPrimitives.cs 的 ArkPerspective。
    private static ArkPerspective[] _arkPerspFar, _arkPerspNear;

    // 左上角那排图标按键已按要求整组删除，_arkTopIcons 一并清掉
    // 这两处早先是 SkewedImage（斜切）；模板里它们本来就是直角矩形，改回普通 Image 后类型要跟着改
    private static readonly Image[] _arkRightPlates = new Image[8];
    private static Text _arkDate, _arkUserName, _arkUserId, _arkDlgText, _arkRationality;
    private static Image _arkDlgPlate;
    private static Image _arkVoiceTag, _arkRingArc;
    private static Text _arkSmallTitles;

    /// <summary>三层：远层（左区）、中层、近层（右区）。示意图的 rotateY 与 translateZ 都作用在它们身上。</summary>
    private static RectTransform MakeArkLayer(Transform parent, string name, float z) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        // 铺满父级：offsetMin/offsetMax 是像素边距（不是归一化比例），四边都给 0 才是刚好铺满。
        // 给 1f 会让矩形比父级各外扩 1 像素，anchoredPosition 也跟着偏到 (0.5, 0.5)。
        Stretch(rt, Vector2.zero, Vector2.zero);
        var lp = rt.localPosition; lp.z = z; rt.localPosition = lp;
        return rt;
    }

    /// <summary>把场景里上一份大厅的相机与画布真拆掉。
    /// 它是根对象，没有父级会替它销毁；而 DisposeArkRefs 只清字段、不碰对象，
    /// 于是每重建一次就会在屏幕上多叠一张画布（旧的没有 Camera 驱动、只剩一团假 null 的残留）。
    /// 只在建大厅这一刻调，不在每帧路径上。</summary>
    private static void PurgeArkRoots() {
        try {
            if (_arkCam != null) Object.Destroy(_arkCam.gameObject);
            if (_arkCanvas != null) Object.Destroy(_arkCanvas.gameObject);
            _arkCam = null; _arkCanvas = null; _ark3DReady = false;
        } catch { }
        try {
            // 引用已经被清过（DisposeArk 那条路）时靠名字把残留找回来：
            // 这两个名字是本文件独占的（没有别的代码建它们），按名字收是安全的。
            var scn = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scn.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                var go = roots[i];
                if (go == null) continue;
                if (go.name == "ArkCam" || go.name == "ArkCanvas") Object.Destroy(go);
            }
        } catch { }
    }

    /// <summary>拆大厅时清掉所有引用。相机与画布是场景根对象，会由 PurgeArkRoots 真拆，这里只清字段。</summary>
    private static void DisposeArkRefs() {
        _arkRoot = null; _arkLayerFar = null; _arkLayerNear = null;
        _arkCam = null; _arkCanvas = null; _ark3DReady = false;
        _arkPerspFar = null; _arkPerspNear = null;
        _mainGroup = null;
        _arkDate = null; _arkUserName = null; _arkUserId = null;
        _arkDlgText = null; _arkRationality = null; _arkDlgPlate = null;
        _arkVoiceTag = null; _arkRingArc = null; _arkSmallTitles = null;
        for (int i = 0; i < _arkRightPlates.Length; i++) _arkRightPlates[i] = null;
        // 悬停那两张表跟着这一屏一起作废：元素都销毁了，留着就是一堆假 null 和过期的基色
        _arkHoverables.Clear();
        _arkBaseColor.Clear();
        _arkHovering = null;
        // 点击登记表同理：键都是这一屏的图元，全销毁了
        _arkClicks.Clear();
        _arkClickArmed = false; _arkClickDownG = null;
        _arkNumP = null; _arkNumG = null; _arkNumU = null;
        _arkClearedNum = null; _arkClearedTotal = null; _arkTotalLabel = null;
        // 隐藏动画的载体与选中提示跟着这一屏走；阶段复位成"正常显示"
        _arkUiCgContent = null; _arkUiCgNav = null; _arkUiCgArk = null;
        _arkUiPhase = 0; _arkUiA = 1f; _arkUiHidden = false;
        _arkActiveTint.Clear(); _arkRoundFreeze = null;
        // 磨砂清单同理：底板对象会随画布一起销毁，清单里留着的都是假 null
        ClearArkGlass();
    }

    // ================= 构建 =================

    /// <summary>建大厅本体。三个层 + 左区元素 + 右区六行，全部坐标照示意图。</summary>
    private static void BuildArkBody(Transform slot) {
        // 这块画布必须是 ScreenSpaceOverlay，不能走相机模式。这是"主界面一直空白、
        // 只在开关面板时闪两下"的根因：
        // 面板自己也是 Overlay，而 Unity 的渲染顺序是「所有 Overlay 画布都排在所有相机之后」——
        // 也就是说 ScreenSpaceCamera 画布无论 sortingOrder 多高，都排不到 Overlay 后面去。
        // 表现就是：面板开着的时候大厅一直被压在下面看不见，只有面板画布在开关途中
        // 短暂消失的那一两帧才闪出来（用户报的"闪两下"）。
        // 全项目 30 多处画布都是 Overlay、靠 sortingOrder 排队，这里也归到同一套里才排得动。
        // 那斜切透视怎么办？不能用相机，就自己算 —— 见下面挂的 ArkPerspective（逐图元针孔投影），
        // 公式与示意图的 perspective(30rem) rotateY(deg) 完全等价，观感一点不少。
        PurgeArkRoots();     // 先拆上一份残留（只清字段的 DisposeArkRefs 拆不掉场景里的对象）

        // 排序比面板画布（80000）高：大厅这一屏要在面板之上
        _arkCanvas = MakeCanvas("ArkCanvas", 81000);
        if (_arkCanvas != null) _ark3DReady = true;

        // 内容挂在自己这块画布下（坐标按 1920x1080 基准，与面板根画布同一套 CanvasScaler 口径）
        var host = _arkCanvas != null ? _arkCanvas.transform : slot;
        _arkRoot = MakeArkLayer(host, "ArkRoot", 0f);
        _arkLayerFar = MakeArkLayer(host, "ArkFar", 0f);
        _arkLayerNear = MakeArkLayer(host, "ArkNear", 0f);

        // 诊断：这几行是「整屏空白」的第一现场 —— 画布有没有 RectTransform、内容区多大、
        // 几层都在不在。空白时先看这几行，别急着怀疑坐标。
        try {
            var crt = _arkCanvas != null ? _arkCanvas.GetComponent<RectTransform>() : null;
            var s0 = _arkLayerNear != null ? _arkLayerNear.anchoredPosition : Vector2.zero;
            var l0 = _arkLayerNear != null ? _arkLayerNear.localEulerAngles : Vector3.zero;
            Lg.Info("大厅画布：mode="
            + (_arkCanvas != null ? _arkCanvas.renderMode.ToString() : "无")
            + " 排序=" + (_arkCanvas != null ? _arkCanvas.sortingOrder.ToString() : "-")
            + " 内容区=" + (crt != null ? crt.rect.size.ToString() : "无RectTransform")
            + " 层=" + (_arkRoot != null ? "3" : "0")
            + " 近层pos=" + s0 + " euler=" + l0);
        } catch { }

        BuildArkLeft(_arkLayerFar);
        BuildArkRight(_arkLayerNear);
        // 立绘层：挂中性层（ArkRoot）——它在两块透视层之下（所以背景被它压住、大厅按键又在它之上），
        // 而中性层不挂逐顶点透视，立绘因此只是平移、不会被倾斜（见 PortalUI.ArkPortrait.cs）。
        BuildArkPortrait(_arkRoot);
        // 立绘的小界面挂在大厅画布本身（排在 ArkRoot / ArkFar / ArkNear 之后，也就是压在大厅所有 UI 之上，
        // 用户报过被挡住）；画布上没有逐顶点透视，所以它仍是直长方形、不吃斜角。
        BuildArkPortraitPanel(_arkCanvas != null ? _arkCanvas.transform : _arkRoot);

        // 元素都建完了才挂透视：这套组件是逐图元的，挂早了漏掉后面建的那些。
        // 只给两片"3D 层"挂 —— ArkRoot 那层是不倾斜的中性层（角度恒 0，挂了也是恒等变换）。
        _arkPerspFar = AttachArkPerspective(_arkLayerFar, ArkYawLeft);
        _arkPerspNear = AttachArkPerspective(_arkLayerNear, ArkYawRight);

        // 历程环"透视之后看起来是正圆"：环离透视原点远，横向放大比纵向多约 10%，
        // 不预缩宽就是一眼可见的横向椭圆（用户点名）。见 ArkPerspective.PreSquashToCircle。
        TrySquashRingToCircle(_arkPerspFar);
    }

    /// <summary>把历程环那两枚图元（淡底环 + 亮进度环）按当前投影预缩宽成正圆。
    /// 按名字找而不是存引用：组件是 AttachArkPerspective 统一挂的，这两个名字在本文件独占。</summary>
    private static void TrySquashRingToCircle(ArkPerspective[] views) {
        try {
            for (int i = 0; views != null && i < views.Length; i++) {
                var v = views[i];
                if (v == null) continue;
                var g = v.GetComponent<Graphic>();
                if (g == null) continue;
                if (g.name == "ArkRing" || g.name == "ArkRingBase") v.PreSquashToCircle();
            }
        } catch { }
    }

        /// <summary>跟着面板画布一起开落。盖层（开始界面 / 个人页 / 进谱面过渡层）盖住面板时
    /// 面板会停渲染（Shell.cs 那条判据），大厅这块是独立画布，必须显式跟着落 ——
    /// 层级比面板高，不跟着落就会「盖层走了、大厅先冒出来」。</summary>
    public static void SyncArkCanvasEnabled(bool on) {
        try {
            if (_arkCanvas != null) _arkCanvas.enabled = on;
        } catch { }
    }

// ================= 左区（示意图左上：四枚图标 + 等级环 + 名字 + VOICE + 圆钮 + 好友档案 + 新闻）=================

    private static void BuildArkLeft(Transform far) {
        // ---- 左上角四枚图标按键（设置/通知/邮件/日历）：按用户要求整组删除，不再构建 ----

        // ---- 主标题 LevelLibrary（用户要求加在左上角空区，就是原四枚图标那一带）----
        // 大号加粗字 + 投影（标题也要 shadow，见 AddArkShadow）。左区整体纵向缩过，走 ArkVAt。
        // 颜色跟随主题色（RGB 取 ThemeColor、alpha 拉满；每秒刷新在 TickArkInfo 里跟着换）。
        // 字体用系统楷体（用户点名）：随包自定义字体三条路都已试死，但系统已装的字体
        // 可以用 OS 字体接口直接建。建不出来就退回界面默认字体，标题照常显示。
        _arkTitle = MakeText(far, "ArkTitle", ArkFsTitle, new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f),
                             TextAnchor.MiddleLeft);
        if (_arkKaiFont == null) {
            try { _arkKaiFont = Font.CreateDynamicFontFromOSFont("KaiTi", 100); } catch { }
        }
        if (_arkKaiFont != null) _arkTitle.font = _arkKaiFont;
        _arkTitle.fontStyle = FontStyle.Bold;
        _arkTitle.text = "LevelLibrary";
        ArkVAt(_arkTitle.rectTransform, 48f, ArkVYL(96f), 1000f, ArkVHL(160f));
        AddArkShadow(_arkTitle, 0.5f);

        // ---- 名字底板：按用户要求删除（历程文字后面不再有灰色块）----

        // ---- 等级圆环（Icons.tsx LevelIcon：圆环 + 中间 120 + 下面 LV）----
        BuildArkLevelRing(far);

        // ---- 用户名与 ID：接在等级环后面往下排，左缘与环对齐（ml-12）。
        // 框宽按文字本身给（模板那边是 flex-start 的 flex item，宽度由内容撑开，不是整行宽）——
        // 给太宽会和右侧那两枚圆钮在判定上压在一起。----
        _arkUserName = MakeText(far, "ArkUserName", ArkFsLarge, ArkTitleLight(), TextAnchor.MiddleLeft);
        ArkVAt(_arkUserName.rectTransform, ArkRingX, ArkNameY, 200f, 46f);
        _arkUserName.text = Lang.Tr("玩家名字");
        _arkUserId = MakeText(far, "ArkUserId", ArkFsXSmall, ArkTitleDark(), TextAnchor.MiddleLeft);
        ArkVAt(_arkUserId.rectTransform, ArkRingX, ArkIdY, 200f, 26f);
        // 玩家随机 ID：每位玩家随机一次、落盘固定（见 AutoFeatures 的 PlayerRandomId）
        _arkUserId.text = "ID: " + (string.IsNullOrEmpty(PlayerRandomId) ? "000000000" : PlayerRandomId);

        // ---- 名字右侧的玩家头像（用户要求；后又要求往左、加大、处理边缘锯齿）----
        // 位置：名字行的右侧、两枚圆钮的左边。尺寸是方的、不走纵向缩放（圆形头像被压扁就成椭圆了），
        // 与左区其它图标同一口径：只把中心过 ArkVYL。
        // 显示：自定义头像走 BakeArkAvatarSprite（圆角软边，直边在透视下会出锯齿）；
        // 没有自定义头像走 ApplyAvatarTo 的原路（动效球本来就是圆的）。
        var av = MakeImage(far, "ArkAvatar", Color.white);
        av.raycastTarget = false;
        ArkAtPx(av.rectTransform, 176f, ArkVYL(417f), 76f, 76f);
        var bakedAvatar = BakeArkAvatarSprite(ResolvePlayerAvatarSprite());
        if (bakedAvatar != null) {
            av.sprite = bakedAvatar;
            av.type = Image.Type.Simple;
            av.preserveAspect = true;
        } else {
            ApplyAvatarTo(av);
        }

        // ---- VOICE 对话框（App.tsx：bg-dialog-bg 的块 + 左上角 VOICE 标签 + 正文 + 右下 ▼）----
        // 模板里这块是普通的 div，没有斜切（App.tsx 的 bg-dialog-bg 那个 div 只带 mt/ml 与圆角外无其它），
        // 早先我给它套了 CornerMode.Left，实机看是"斜的对话框"，这里改回直角。
        // 底色按用户要求换成同款乳白（交流群那一块，比别处更不透明一档），正文与折角换深色；VOICE 标签保持原样当点缀。
        _arkDlgPlate = MakeArkRect(far, "ArkVoice", ArkDlgX, ArkVYL(ArkDlgY), ArkDlgW, ArkVHL(ArkDlgH),
                                   ArkCreamSolid());
        // VOICE 标签：贴着框的左上角，往左上各让 8（App.tsx 的 -mt-2 -ml-2）
        _arkVoiceTag = MakeImage(far, "ArkVoiceTag", ArkVoiceBg());
        _arkVoiceTag.raycastTarget = false;
        ArkVAt(_arkVoiceTag.rectTransform, ArkDlgX - 8f, ArkDlgY - 8f, ArkVoiceTagW, 26f);
        SoftifyArkPlate(_arkVoiceTag, 0f);   // 软边板：透视倾斜下硬边会有锯齿
        var tagTxt = MakeText(_arkVoiceTag.transform, "T", ArkFsXSmall, ArkVoiceText(), TextAnchor.MiddleLeft);
        ArkVAt(tagTxt.rectTransform, 6f, 0f, ArkVoiceTagW - 12f, 26f);
        tagTxt.text = "VOICE";

        _arkDlgText = MakeText(_arkDlgPlate.transform, "T", ArkFsNormal, ArkVoiceText(), TextAnchor.MiddleLeft);
        ArkVAt(_arkDlgText.rectTransform, 24f, 0f, ArkDlgW - 80f, ArkDlgH);
        _arkDlgText.text = Lang.Tr("交流群：959648017");
        var fold = MakeText(_arkDlgPlate.transform, "Fold", ArkFsSmall, ArkVoiceText(), TextAnchor.LowerRight);
        ArkVAt(fold.rectTransform, ArkDlgW - 40f, ArkDlgH - 34f, 24f, 20f);
        fold.text = "▼";

        // ---- 两枚圆钮（功能映射：左钮冻结视差跟随 / 右钮只留视频背景）。两枚都是临时开关，不落盘。
        // 原先圆钮右边那道渐变亮线按用户要求删除，不再构建。----
        var hide = MakeArkRoundButtonV(far, "ArkRoundHide", ArkRoundX, ArkRoundY, ArkIconHide);
        _arkRoundFreeze = hide;
        var chg = MakeArkRoundButtonV(far, "ArkRoundChange",
                                      ArkRoundX + ArkRoundSize + ArkRoundGap, ArkRoundY, ArkIconChange);
        RegisterArkInteract(hide);
        RegisterArkInteract(chg);
        RegisterArkClick(hide, ArkToggleParallaxFreeze);
        RegisterArkClick(chg, ArkToggleUiHide);

        // ---- 立绘：圆钮排在「隐藏 UI」那枚右边（那排的第三格），点开是向下展开的小界面。
        // 只有圆钮进左区（与那排圆钮同层、倾角一致）；小界面挂在画布顶层（见上面 BuildArkPortraitPanel 的说明）。
        BuildArkPortraitButton(far);

        // ---- 好友 / 档案：在新闻轮播的右边并排（App.tsx 里它们和 NewsCol 是同一个 flex 行的两项，
        // 靠 -ml-3 往左压过来 12px）。早先我把这一列放在新闻上面，于是和 VOICE 框叠在一起了。----
        // 功能映射：好友→周报（弹最近 7 天的周报窗，与 debug_weekly 预览同一口径）、
        // 档案→设置（设置接设置页槽位 3）
        BuildArkSideButton(far, "ArkFriends", ArkSideX, ArkSideY0, Lang.Tr("周报"), "weekly", ArkButtonFriendsBg(),
                           () => ShowWeeklyReportDialog(System.DateTime.Now, true), ArkIconCalendar);
        BuildArkSideButton(far, "ArkArchive", ArkSideX, ArkSideY0 + ArkSideBtnH + ArkSideGap,
                           Lang.Tr("设置"), "settings", ArkButtonArchiveBg(), () => SwitchTopTab(3), ArkIconSettings);

        // ---- 新闻轮播（Columns.tsx NewsCol：288x136 窗 + BREAKING NEWS 红标 + 3 段指示条）----
        BuildArkNews(far);
    }

    /// <summary>等级圆环：一道圆环描边 + 中间的等级数字 + 下面的"历程"。
    /// 功能映射：中间数字 = 当前成就等级（1~5，Achievements.LevelProgress）；
    /// 圆环从"死描边"改成真进度——Image 改 Filled(Radial360)，填充量 = Achievements.LevelFill()，
    /// 每秒在 TickArkInfo 里刷新。环是 208 视口 / r=96 / 描边 8，等比缩到 144。
    /// SVG 里的数字在 y=120、"历程"在 y=176（都是 208 视口内的坐标），折算成扁形比例就是下面这两个位置。</summary>
    private static void BuildArkLevelRing(Transform parent) {
        // 淡底环：原来那道低透明度描边，常驻不动
        MakeArkIconV(parent, "ArkRingBase", ArkRingX + ArkRingSize * 0.5f,
                     ArkRingY + ArkRingSize * 0.5f, ArkRingSize, ArkIconLevelRing);
        // 亮进度环：叠在淡底上，Image 改 Filled(Radial360)，从正下方起顺时针随成就进度长满一圈。
        // 没进度时 fillAmount=0 一点不露，进度满就是完整一圈亮环。
        var ring = MakeArkIconV(parent, "ArkRing", ArkRingX + ArkRingSize * 0.5f,
                                ArkRingY + ArkRingSize * 0.5f, ArkRingSize, ArkIconLevelRingFill);
        // 完成弧不用 Image.Filled：Filled 的扇区两端是几何硬边，透视倾斜下会出锯齿；
        // 改成"烘一张弧贴图"（ArkRingArcSprite）：整条弧含两端都走纹理采样，任何缩放下都平滑。
        ring.type = Image.Type.Simple;
        ring.sprite = ArkRingArcSprite(0f);
        // 完成弧颜色跟随主题色（与主标题同一套口径：RGB 取 ThemeColor、alpha 拉满；TickArkInfo 每秒跟着换）
        ring.color = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f);
        _arkRingArc = ring;
        _arkSmallTitles = MakeText(parent, "ArkLevelNum", ArkFsXLarge, Color.white, TextAnchor.MiddleCenter);
        // 等级数字再往上一点（用户要求；-20 → -30）。"历程"跟着同量上移，两者相对间距不变。
        ArkVAt(_arkSmallTitles.rectTransform, ArkRingX, ArkRingY + ArkRingSize * 0.5f - 30f, ArkRingSize, 40f);
        if (_arkKaiFont != null) _arkSmallTitles.font = _arkKaiFont;   // 等级数字用楷体
        _arkSmallTitles.text = "1";
        var lv = MakeText(parent, "ArkLevelLv", ArkFsXSmall, Color.white, TextAnchor.MiddleCenter);
        // "历程"再往上一点（用户要求；-44 → -54）。字体为界面默认（用户撤回了楷体）。
        ArkVAt(lv.rectTransform, ArkRingX, ArkRingY + ArkRingSize - 54f, ArkRingSize, 26f);
        lv.text = Lang.Tr("历程");
    }

    // ================= 右区六行（示意图右侧自上而下）=================
    // 六行是 3D 容器里的 flex-col、带 gap-6(24)：纵向位置由行高累加得出，
    // 横向每行各自右对齐（items-end），行宽见各自的 ArkRightLeft 参数。

    private static void BuildArkRight(Transform near) {
        // 第1 行：时间行（Columns.tsx DatetimeCol：hr + 电池 + 时间 + hr，整行 w-185）
        // 行内所有纵向偏移一律过 ArkVS：整块是被纵向缩放的，不缩就对不齐行高。
        // 用户要求整行往左、往上各挪一点：左移 30，累计上移 56（红框截图定的位：整行贴到屏幕顶边）。
        float dL = ArkRightLeft(ArkWDatetime) - 30f;
        float dY = ArkYDatetimeTop - 56f;
        float dTop = dY + ArkVS(10f);
        var hr1 = MakeImage(near, "ArkHrL", new Color(1f, 1f, 1f, 0.28f));
        hr1.sprite = SharpSprite; hr1.type = Image.Type.Simple; hr1.raycastTarget = false;
        ArkAtPx(hr1.rectTransform, dL, dTop, 80f, ArkVS(4f));                                 // w-20 border-t-4
        MakeArkIcon(near, "ArkBattery", dL + 104f, dY + ArkVS(12f), 32f,
                    ArkIconBattery);                                                          // w-8
        _arkDate = MakeText(near, "ArkDate", ArkFsNormal * 2, ArkTitleLight(), TextAnchor.MiddleLeft);
        if (_arkKaiFont != null) _arkDate.font = _arkKaiFont;   // 时间行用楷体（用户点名）
        ArkAtPx(_arkDate.rectTransform, dL + 128f, dY - 2f, 480f, ArkVS(60f));   // 字号翻倍后框跟着加高加宽
        var hr2 = MakeImage(near, "ArkHrR", new Color(1f, 1f, 1f, 0.28f));
        hr2.sprite = SharpSprite; hr2.type = Image.Type.Simple; hr2.raycastTarget = false;
        ArkAtPx(hr2.rectTransform, dL + 624f, dTop, 116f, ArkVS(4f));          // 右端不变，给大字号让出左段

        // 第 2 行：资源条（ItemsCol：w-190 h-12，整行 -translate-x-8）
        BuildArkItems(near);

        // 第 3 行：terminal（w-220 h-55，整行 translate-x-30，右端故意探出画布）
        BuildArkTerminal(near);

        // 第 4 行：编队 · 干员（两块 SmallButton w-85，右端还有 w-32 占位块带 -mr-16）
        // 行宽 = 85 + 6(gap) + 85 + 6 + 32，占位块的 -mr-16 让容器只按 16 计。
        // 用户要求整行再往左一点（三轮累计 -72，左侧伸出得更明显、衬出与上下两行的台阶差）；
        // 行尾占位块跟着走、右端仍探出屏外。
        float r1L = ArkRightLeft(2f * ArkWSmall + 2f * ArkRowGap + 128f - 64f) - 72f;
        // 功能映射：谱面库 → 库页（槽位 1），官谱库 → 主页（槽位 0，官方谱库那一页）
        // 这两枚按用户要求去掉后方投影（blockShadow=false），成就/开发者两枚保留。
        BuildArkSmallButton(near, "ArkSquads", r1L, ArkYRow1, Lang.Tr("谱面库"), "library",
                            () => SwitchTopTab(1), ArkIconList, blockShadow: false);
        BuildArkSmallButton(near, "ArkOperator", r1L + ArkWSmall + ArkRowGap, ArkYRow1,
                            Lang.Tr("官谱库"), "official", () => SwitchTopTab(0), ArkIconCheck, blockShadow: false);
        // 行尾占位块（模板：bg-placeholder-bg w-32 -mr-16）：一块纯装饰的深色空块，右端伸出屏幕外。
        // 画布边缘就是屏幕边缘，伸出去的部分自然看不到 —— 模板那边同样靠 overflow-hidden 裁掉，
        // 所以这里算出屏是"按模板本意"，不修正（用户点名要求它到屏幕外边去）。
        // 后又按用户要求把长度加长一倍（128 → 256，左端不动往右伸出），接受更多截出屏外。
        var ph = MakeArkRect(near, "ArkRow1Placeholder", r1L + 2f * ArkWSmall + 2f * ArkRowGap, ArkYRow1,
                             256f, ArkHSmallS, ArkPlaceholderBg());
        AddArkBlockShadow(ph, 0.42f);   // 与按钮同一套渐变投影（用户点名：占位块也要有）

        // 第 5 行：采购中心（StoreButton w-60 h-35）+ 招募栏（RecuritCol w-100 h-35）+ w-14 占位
        BuildArkStore(near);
        BuildArkRecurit(near);

        // 第 6 行：任务 · 基建（两块 SmallButton）+ 仓库（DepotButton w-24，自身带 -ml-6）
        // 模板这一行还有 translate-x-5（20）往右推：按模板本意整行右探 20，右端到 1620 ——
        // 与占位块同理，探出去的一小截靠屏幕边缘裁掉（模板那边也是 overflow-hidden 裁的）。
        // 用户点名的"第四行突出"就是它。纵向的 translate-y-10（40）与其它行的一样要过纵向缩放。
        float r3L = ArkRightLeft(2f * ArkWSmall + 2f * ArkRowGap + ArkWDepot - 24f) + 20f;
        float r3Y = ArkYRow3 + ArkRow3Down;
        BuildArkSmallButton(near, "ArkMission", r3L, r3Y, Lang.Tr("成就"), "achieve",
                            () => SwitchTopTab(2), ArkIconStar);
        BuildArkSmallButton(near, "ArkBase", r3L + ArkWSmall + ArkRowGap, r3Y,
                            Lang.Tr("开发者"), "develop", () => SwitchTopTab(4), ArkIconCode);
        BuildArkDepot(near, r3L + 2f * ArkWSmall + 2f * ArkRowGap - 24f, r3Y);
    }

    /// <summary>资源条：功能映射后这里不再是钱/玉/源石，是三格统计 —— P 级通关数 / G 级通关数 / U 级通关数
    /// （口径：有通关记录 且 等级标记的档位头是 P/G/U）。图标是"一个圆里写字母"：程序化空心圆 + 字母 Text。
    /// 整条是一块半透明板（w-190 h-12），行宽 760。模板里 ItemsCol 是 FlexContainer，直角，不斜切。</summary>
    private static void BuildArkItems(Transform parent) {
        float left = ArkRightLeft(ArkWItems) - 32f;
        var plate = MakeArkRect(parent, "ArkItemsPlate", left, ArkYItems, ArkWItems, ArkHItemsS, ArkItemsBg());
        _arkRightPlates[0] = plate;
        // 这行不登记磨砂：模板里 ItemsCol 只有 bg-items-bg，没有 backdrop-blur-xs。
        // 上一版给它挂了糊图，实机上整条被亮糊冲成一片白、数字看不见（用户报"数据显示那行飞走了"）。
        var holder = plate.transform;

        // 纵向：板高是缩过的 ArkHItemsS，行内所有纵向偏移与高度都过 ArkVS，中线取板高一半。
        // 注意这里的坐标全部是相对板（holder）的本地值：SetAtPx 锚在父级左上角，
        // 早先误写成画布绝对值（ArkYItems + ... / left + 8f），叠加板的偏移后整体飞出板外，数字图标全都看不见。
        float yMid = ArkHItemsS * 0.5f;      // 图标与数字都按板内中线摆
        float gap = ArkRowGap / 6f;          // flex 的 gap 是 8(CSS)，不是行间距的 24
        float x = 8f;                        // 板内左缘起，对应模板的 px-2
        string[] heads = { "P", "G", "U" };
        int[] widths = { 190, 140, 140 };
        for (int i = 0; i < 3; i++) {
            MakeArkIcon(holder, "ArkCur" + i, x + 26f, yMid, 52f, ArkIconCircle);
            // 圆心里的档位字母：圆的直径约 40（52 的 77%），字母取一档小字号居中；字母用楷体（用户点名）
            var lt = MakeText(holder, "ArkCurL" + i, ArkFsSmall, Color.white, TextAnchor.MiddleCenter);
            ArkAtPx(lt.rectTransform, x, yMid - ArkVS(14f), 52f, ArkVS(28f));
            if (_arkKaiFont != null) lt.font = _arkKaiFont;
            lt.text = heads[i];
            x += 52f + gap;
            // 数字：四位数补零（TickArkInfo 也按 D4 刷新），颜色按档位 —— P 绿 / G 黄 / U 紫（用户点名）
            Color numCol = i == 0 ? Hex(0x6FD96A) : (i == 1 ? Hex(0xF1C94A) : Hex(0xB07EDE));
            var n = MakeText(holder, "ArkNum" + i, ArkFsXLarge, numCol, TextAnchor.MiddleLeft);
            ArkAtPx(n.rectTransform, x, yMid - ArkVS(20f), widths[i], ArkVS(40f));
            n.text = "0000";
            if (i == 0) _arkNumP = n; else if (i == 1) _arkNumG = n; else _arkNumU = n;
            x += widths[i] + gap;
        }
    }

    /// <summary>terminal 那一块（Buttons.tsx：h-55 w-220，整块 translate-x-30）：
    /// 左边是理智 100 / 理智180 两行，右边是 terminal 字样 + 当前胶囊 + 全部完成。
    /// 模板里这块由 translate-x-30 往右推，右端是故意探出容器让 overflow-hidden 裁掉的；
    /// 本项目画布边缘就是屏幕边缘、裁掉等于飞出屏幕，所以这里不加那 30 的位移，
    /// 让它整块落在右边缘之内（对模板的一处显式偏离，理由与 ArkWRecurit 那处相同）。</summary>
    private static void BuildArkTerminal(Transform parent) {
        // 用户要求这一行再往右一点（两轮累计 +48）：与下面的谱面库行一进一退，把它"衬出来"。
        float left = ArkRightLeft(ArkWTerminal) + 48f;
        // 模板里 TerminalButton 是 BaseButton（直角矩形），斜切是我多加的，这里改回直角
        // 底色按用户要求从深灰换成乳白（Welcome back 所在的整块统计板一起换），底上的字改深色。
        var plate = MakeArkRect(parent, "ArkTerminal", left, ArkYTerminal, ArkWTerminal, ArkHTerminalS,
                                ArkCream());
        _arkRightPlates[1] = plate;
        RegisterArkInteract(plate, false);   // 用户要求：通关数统计这一块不带阴影（悬停/可点保留）
        // 不登记磨砂：糊图是"背景模糊 × 底板色"，底板换成接近白的乳白之后糊图≈背景本身，
        // 整块会像透明的一样（叠加层那两条踩过）；乳白块要的就是实心，直接去掉糊图。

        // 左侧理智盒（bg-sanity-light 装大数字，bg-sanity-dark 装"理智/180"），m-5 的边距。
        // 两块都走 MakeArkRect（软边板）：透视倾斜下硬边会有锯齿。
        var l1 = MakeArkRect(plate.transform, "Sanity", 20f, ArkVS(14f), 232f, ArkVS(112f), ArkSanityLight());   // w-58
        // 通关数字左侧的图案（用户要求；后又要求大一点、往右一点）：圆环勾，与数字同色（底是乳白 → 深色）。
        // 数字是右对齐的：位数多（三、四位）时会往左伸长，届时按实机观感再调两者间距。
        _arkClearedIcon = MakeArkIcon(l1.transform, "Icon", 44f, ArkVS(56f), 60f, ArkIconCheck);
        _arkClearedIcon.color = ArkVoiceText();
        var n1 = MakeText(l1.transform, "T", ArkFsHuge, ArkVoiceText(), TextAnchor.UpperRight);
        // 居中对齐（原来是 UpperRight）：作者要"数字落在区域中央"，靠右对齐+宽度反推容易飘，
        // 直接让 Text 自己在盒里居中更稳。左边界让开勾环那块（图标 44..104）。
        n1.alignment = TextAnchor.MiddleCenter;
        ArkAtPx(n1.rectTransform, 116f, ArkVS(-16f), 232f - 116f, ArkVS(112f));
        if (_arkKaiFont != null) n1.font = _arkKaiFont;   // 通关总数用楷体（用户点名）
        n1.text = "0";
        _arkClearedNum = n1;             // 功能映射：这里统计所有通关过的谱面数
        var l2 = MakeArkRect(plate.transform, "SanityDark", 20f, ArkVS(130f), 232f, ArkVS(76f), ArkSanityDark());
        // 数字与标签拆成两个 Text：数字单独用楷体（用户点名），标签"谱面总数/"保持界面默认字体、
        // 右缘紧贴数字左缘（宽度在建完和每秒刷新时由 LayoutArkTotalLabel 按数字实际宽度重算）。
        _arkRationality = MakeText(l2.transform, "T", ArkFsLarge, Color.white, TextAnchor.MiddleCenter);
        ArkAtPx(_arkRationality.rectTransform, 0f, 0f, 232f - 24f, ArkVS(76f));
        if (_arkKaiFont != null) _arkRationality.font = _arkKaiFont;   // 谱面总数里的数字用楷体
        _arkRationality.text = "0";   // 功能映射：谱面总数（数字部分），TickArkInfo 里刷新
        _arkClearedTotal = _arkRationality;   // 登记给每秒刷新（漏了这句就是永远停在 0/0）
        _arkTotalLabel = MakeText(l2.transform, "TL", ArkFsLarge, Color.white, TextAnchor.MiddleCenter);
        ArkAtPx(_arkTotalLabel.rectTransform, 0f, 0f, 232f - 24f, ArkVS(76f));
        _arkTotalLabel.text = Lang.Tr("谱面总数/");   // 连斜杠一起进词条：切语言时 RefreshUiTexts 才反查得回原文
        LayoutArkTotalLabel();   // 建完当场摆一次，别等下一秒的刷新
        LayoutArkClearedGroup();  // 勾环 + 通关数那一组也当场居中一次

        // 右侧：Welcome back（用户点名楷体；底是乳白，字取深色保证可读）+ 当前胶囊 + 等待指示（整组带 ml-2）
        float rx = 20f + 232f + 8f;
        var tt = MakeText(plate.transform, "T", ArkFsHuge, ArkVoiceText(), TextAnchor.MiddleLeft);
        ArkAtPx(tt.rectTransform, rx, ArkVS(10f), 460f, ArkVS(115f));
        if (_arkKaiFont != null) tt.font = _arkKaiFont;   // 与主标题同一份系统楷体（建不出就退默认字体）
        tt.fontStyle = FontStyle.Bold;
        tt.text = "Welcome back";
        var cur = MakeImage(plate.transform, "Now", ArkCurrentBg());
        cur.raycastTarget = false;
        ArkAtPx(cur.rectTransform, rx, ArkVS(130f), 110f, ArkVS(40f));
        SoftifyArkPlate(cur, 6f);   // 软边圆角板：透视倾斜下硬边会有锯齿
        var ct = MakeText(cur.transform, "T", ArkFsNormal, ArkTitleVeryDark(), TextAnchor.MiddleCenter);
        ArkAtPx(ct.rectTransform, 0f, 0f, 110f, ArkVS(40f));
        ct.text = Lang.Tr("当前");
        var dn = MakeText(plate.transform, "Done", ArkFsNormal, ArkTitleVeryDark(), TextAnchor.MiddleLeft);
        ArkAtPx(dn.rectTransform, rx, ArkVS(178f), 460f, ArkVS(34f));
        dn.text = Lang.Tr("等待指示");
    }

    private static Texture2D _arkAvatarSrcTex;
    private static Sprite _arkAvatarBaked;

    /// <summary>把玩家自定义头像烘成"圆角 + 软边"的方图，给大厅名字旁那枚头像用。
    /// 为什么不直接用原 sprite：照片是方的，直边在透视倾斜下会出锯齿（与板子同源的问题）；
    /// 烘一张圆角软边版本，边缘交给纹理采样。源纹理实例变了（换过头像）才重烘。
    /// 失败（纹理不可读等）返回 null，调用方退回 ApplyAvatarTo 的原路。</summary>
    private static Sprite BakeArkAvatarSprite(Sprite src) {
        try {
            if (src == null || src.texture == null || !src.texture.isReadable) return null;
            if (_arkAvatarSrcTex == src.texture && _arkAvatarBaked != null) return _arkAvatarBaked;
            var st = src.texture;
            Rect rr = src.textureRect;
            int side = Mathf.FloorToInt(Mathf.Min(rr.width, rr.height));
            if (side < 8) return null;
            // 源照片往往几千像素，整张 GetPixels32 太贵：直接读"接近输出尺寸"的那一级 mip
            int lv = 0;
            while (side >> lv > 320 && lv < 6) lv++;
            int bw = Mathf.Max(1, side >> lv);
            var block = st.GetPixels(Mathf.RoundToInt(rr.x) >> lv, Mathf.RoundToInt(rr.y) >> lv, bw, bw, lv);
            int outS = 256;
            var tex = new Texture2D(outS, outS, TextureFormat.RGBA32, true);   // 有 mip：显示约为烘制的 1/3.3，没 mip 缩小采样又会出锯齿
            var px = new Color32[outS * outS];
            float rad = outS * 0.24f;            // 圆角半径
            float aa = 2f;                        // 软边（输出像素）
            for (int y = 0; y < outS; y++) {
                for (int x = 0; x < outS; x++) {
                    float fx = x + 0.5f - outS * 0.5f, fy = y + 0.5f - outS * 0.5f;
                    float qx = Mathf.Abs(fx) - (outS * 0.5f - rad);
                    float qy = Mathf.Abs(fy) - (outS * 0.5f - rad);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;
                    float a = Mathf.Clamp01(0.5f - d / aa);
                    if (a <= 0f) { px[y * outS + x] = new Color32(0, 0, 0, 0); continue; }
                    // 源块与输出同为"行序自下而上"，直接映射、不用翻转
                    int sxp = Mathf.Clamp((int)((x + 0.5f) / outS * bw), 0, bw - 1);
                    int syp = Mathf.Clamp((int)((y + 0.5f) / outS * bw), 0, bw - 1);
                    var c = (Color32)block[syp * bw + sxp];   // GetPixels 回来的是 Color，先收成 Color32
                    px[y * outS + x] = new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(c.a * a));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);   // 生成 mip 链
            var sp = Sprite.Create(tex, new Rect(0, 0, outS, outS), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
            _arkAvatarSrcTex = st;
            _arkAvatarBaked = sp;
            return sp;
        } catch (System.Exception e) {
            Lg.Ex("大厅头像圆角化失败（退回原样显示）", e);
            return null;
        }
    }

    /// <summary>把"勾环图标 + 通关数"当一组在理智盒里横向居中。作者报的"上面的 1 不在区域中央"
    /// 就是它：图标钉在 x=44、数字右对齐到盒右，整组自然偏左。现在按数字实际宽度算组宽、把组心压到盒心。</summary>
    private static void LayoutArkClearedGroup() {
        if (_arkClearedNum == null || _arkClearedIcon == null) return;
        try {
            // 勾环图标不参与重摆：它建的时候钉在固定位置就不管了（作者要"跟随平行、固定位置"）。
            // 这根元素挂的是逐顶点投影，谁去搬它都会跟盒子产生相对滑动，看着就是"不断上下移动位置"。
            // 只把通关数这一行按数字实宽居中（数字位数变化时重算）。
            const float boxW = 232f;
            float numW = _arkClearedNum.preferredWidth / ArkPx;
            float left = Mathf.Max(0f, (boxW - numW) * 0.5f);
            ArkAtPx(_arkClearedNum.rectTransform, left, ArkVS(-16f), numW + 4f, ArkVS(112f));
        } catch { }
    }

    /// <summary>把"谱面总数/"标签摆到数字左边（两者各自右对齐：标签右缘 = 盒宽 − 数字宽 − 间隙）。
    /// 数字位数变化（进位）时重摆；抽出来是因为建完的当场也要摆一次，不能等下一秒的刷新。</summary>
    private static void LayoutArkTotalLabel() {
        if (_arkClearedTotal == null || _arkTotalLabel == null) return;
        try {
            // 作者要求"谱面总数/数字"整组落在盒子的正中央：原来两者各自右对齐，组右缘贴盒右缘、整组偏左。
            // 现在按各自实际宽度算组宽，再把组心压到盒心（数字位数变化时每次现量）。
            const float boxW = 232f, gap = 6f;
            float nw = _arkClearedTotal.preferredWidth / ArkPx;   // 画布单位 → CSS
            float lw = _arkTotalLabel.preferredWidth / ArkPx;
            float left = Mathf.Max(0f, (boxW - (lw + gap + nw)) * 0.5f);
            // 两块各占自己那一格、并且各自在格内居中，拼起来整组必然居中（不再靠右对齐反推宽度）
            ArkAtPx(_arkTotalLabel.rectTransform, left, 0f, lw + 4f, ArkVS(76f));
            ArkAtPx(_arkClearedTotal.rectTransform, left + lw + gap, 0f, nw + 4f, ArkVS(76f));
        } catch { }
    }

    /// <summary>编队 / 干员 / 任务 / 基建 那种小按钮（Buttons.tsx SmallButton：h-40 w-85 items-center pl-2，
    /// 内层是 border-l-8 pl-4 的竖条 + 两行字）。
    /// 这块在模板里是普通矩形，左端是直的 —— 早先我给它套了 CornerMode.Left 的斜切，
    /// 实机看就是"左端该是直的却变成很斜的斜角"，这里改回直边。
    /// 竖条颜色取 --color-border-dark (#6052ff)，不是主题色：模板用的是 border-l-border-dark 这个类。</summary>
    private static void BuildArkSmallButton(Transform parent, string name, float x, float y, string zh, string latin,
                                            System.Action onClick = null, int icon = 0, bool blockShadow = true) {
        // 用户要求：这四枚（谱面库/官谱库/成就/开发者）与统计板同款半透明白色（乳白），字与图标转深色。
        var plate = MakeArkRect(parent, name, x, y, ArkWSmall, ArkHSmallS, ArkCream());
        RegisterArkInteract(plate, blockShadow);   // blockShadow=false：用户点名谱面库/官谱库两枚不要后方投影
        RegisterArkClick(plate, onClick);
        // 不登记磨砂：糊图=背景模糊×底板色，乳白底板会让糊图≈背景本身、整块像透明的（同统计板）
        // 内层块 pl-2(8) 起；竖条 border-l-8 宽 8、高 h-32(128)，在 h-40(160) 里纵向居中
        var bar = MakeImage(plate.transform, "Bar", ArkBorderDark());
        bar.sprite = SharpSprite; bar.type = Image.Type.Simple; bar.raycastTarget = false;
        ArkAtPx(bar.rectTransform, 8f, ArkVS(16f), 8f, ArkVS(128f));
        float tx = 8f + 8f + 16f;                     // pl-2 + border-l-8 + pl-4
        // 右侧图案（用户要求：谱面库/官谱库/成就/开发者这几枚放右侧）：垂直居中、右缘留 22。
        // 是 plate 的子节点，跟着按钮一起被逐顶点透视投影（"跟随平行"）。
        float textW = ArkWSmall - tx - 16f;
        if (icon != 0) {
            const float isz = 64f;
            float icoRight = ArkWSmall - 22f;
            MakeArkIcon(plate.transform, "Icon", icoRight - isz * 0.5f, ArkVS(80f), isz, icon).color = ArkVoiceText();
            textW = icoRight - isz - 8f - tx;         // 文字框让出图标那一带
        }
        var t = MakeText(plate.transform, "T", ArkFsXxLarge, ArkVoiceText(), TextAnchor.MiddleLeft);
        ArkAtPx(t.rectTransform, tx, ArkVS(18f), textW, ArkVS(80f));
        t.text = Lang.Tr(zh);
        var s = MakeText(plate.transform, "S", ArkFsLarge, ArkTitleVeryDark(), TextAnchor.MiddleLeft);
        ArkAtPx(s.rectTransform, tx, ArkVS(88f), textW, ArkVS(46f));
        s.fontStyle = FontStyle.Bold;
        s.text = latin;
    }

    /// <summary>采购中心（StoreButton：h-35 w-60）。模板里它是直角矩形（BaseButton 只有 overflow-hidden，没有 rounded），
    /// 早先我套了 CornerMode.Left 的斜切，实机看是"采购中心被切成两块"，这里改回直角。
    /// 底色按用户要求换成半透明黑（与右边招募栏拼成一条，两块一起换），字换回浅色。</summary>
    private static void BuildArkStore(Transform parent) {
        float left = ArkRightLeft(ArkWStore + ArkWRecurit + 56f);     // 行5 整行 = 240+440+56
        var plate = MakeArkRect(parent, "ArkStore", left, ArkYRow2, ArkWStore, ArkHStoreS, ArkOverlayBg());
        RegisterArkInteract(plate, false);   // 用户点名：这一栏不要后方投影（与右边招募栏拼一条，一起摘）
        // 不登记磨砂（模板带 backdrop-blur-xs）：黑底 + 糊图会把整条糊成一团，纯色半透明黑更干净
        var t = MakeText(plate.transform, "T", ArkFsXLarge, ArkTitleLight(), TextAnchor.MiddleLeft);
        ArkAtPx(t.rectTransform, 16f, ArkVS(22f), ArkWStore - 32f, ArkVS(56f));
        t.text = Lang.Tr("叠加层");
        var s = MakeText(plate.transform, "S", ArkFsLarge, ArkSubtitleLight(), TextAnchor.MiddleLeft);
        ArkAtPx(s.rectTransform, 16f, ArkVS(104f), ArkWStore - 32f, ArkVS(46f));
        s.fontStyle = FontStyle.Bold;
        s.text = "Overlay";
    }

    /// <summary>招募栏（RecuritCol：h-35 w-100，p-2 内边距，上面一条"招募"标题条、下面两枚 w-53 h-22）。
    /// 与采购中心同一行、gap 0，两块拼起来看是一条完整的矩形；所以这里也必须是直角、底色跟采购中心
    /// 一起用半透明黑（拼缝两侧不能两块不同色）。
    /// 宽度用了 ArkWRecurit（比模板的 400 宽）以便容下两枚子按钮，理由见那个常量的注释。</summary>
    private static void BuildArkRecurit(Transform parent) {
        float left = ArkRightLeft(ArkWStore + ArkWRecurit + 56f) + ArkWStore;   // 紧接采购中心右边（gap 0）
        var plate = MakeArkRect(parent, "ArkRecurit", left, ArkYRow2, ArkWRecurit, ArkHStoreS, ArkOverlayBg());
        RegisterArkInteract(plate, false);   // 与采购中心拼一条，投影一起摘
        // 不登记磨砂，理由同采购中心（两块拼一条，底色一致）
        // 标题条：bg-sanity-dark，py-1(4) 上下、pl-3(12) 左，里面一枚图标 + "招募"
        var head = MakeArkRect(plate.transform, "Head", 8f, ArkVS(8f), ArkWRecurit - 16f, ArkVS(32f), ArkSanityDark());
        MakeArkIcon(head.transform, "I", 12f + 12f, ArkVS(16f), ArkVS(24f), ArkIconNotice);  // pl-3 之后的 w-6 图标
        var ht = MakeText(head.transform, "T", ArkFsNormal, ArkTitleVeryDark(), TextAnchor.MiddleLeft);
        ArkAtPx(ht.rectTransform, 12f + 24f + 12f, 0f, ArkWRecurit - 72f, ArkVS(32f));
        ht.text = Lang.Tr("叠加层设置");

        // 功能映射：个人信息栏与肥鱼设定各自跳到设置页里对应的分区
        // （个人信息栏 = 外观主 tab 的"个人信息"子页；肥鱼设定 = 扩展主 tab 的"大肥鱼"子页）。
        // 外观前面插了「去特效」主 tab 后整体后移一位（1 → 2），写死旧下标会跳进"常规"页（用户报过）。
        // 两块都往下挪了一点（44 → 56），按用户要求。
        BuildArkRecruitBtn(plate.transform, 8f, ArkVS(56f), "profile", Lang.Tr("个人信息栏"),
                           () => ArkOpenSettings(2, 0));
        BuildArkRecruitBtn(plate.transform, 8f + ArkWRecruitBtn, ArkVS(56f), "fatfish", Lang.Tr("肥鱼设定"),
                           () => ArkOpenSettings(ExtraTabIndex, ExtraSubFish));
    }

    /// <summary>招募子按钮（RecruitButton：h-22 w-53；上面是拉丁、下面是中文，两行都 text-large）。
    /// 底色用"原本叠加层那条的半透明紫"（用户点名：个人信息栏 / 肥鱼设定两枚换成它）。</summary>
    private static void BuildArkRecruitBtn(Transform parent, float x, float y, string latin, string zh,
                                           System.Action onClick = null) {
        var btn = MakeImage(parent, "B", ArkButtonLightBg());
        btn.raycastTarget = true;
        ArkAtPx(btn.rectTransform, x, y, ArkWRecruitBtn, ArkVS(ArkHRecruitBtn));
        SoftifyArkPlate(btn, 10f);   // 软边圆角板：透视倾斜下硬边会有锯齿
        RegisterArkInteract(btn);
        RegisterArkClick(btn, onClick);
        var s = MakeText(btn.transform, "S", ArkFsLarge, ArkSubtitleLight(), TextAnchor.MiddleCenter);
        ArkAtPx(s.rectTransform, 0f, ArkVS(6f), ArkWRecruitBtn, ArkVS(46f));
        s.fontStyle = FontStyle.Bold;
        s.text = latin;
        var t = MakeText(btn.transform, "T", ArkFsLarge, ArkTitleLight(), TextAnchor.MiddleCenter);
        ArkAtPx(t.rectTransform, 0f, ArkVS(40f), ArkWRecruitBtn, ArkVS(46f));
        t.text = zh;
    }

    /// <summary>随机开始（原仓库位，DepotButton：w-24 的 BaseButton，自身带 -ml-6 往左压，pt-6 pl-1）。
    /// 与编队那一组同为直角矩形，模板里没有斜切。y 由调用方按纵向缩放算好传进来。
    /// 点击后切到谱面库页并随机抽一张谱面直接进入（口径与快捷键随机同一份加权）。</summary>
    private static void BuildArkDepot(Transform parent, float x, float y) {
        var plate = MakeArkRect(parent, "ArkDepot", x, y, ArkWDepot, ArkHSmallS,
                                ArkButtonDepotBg());
        RegisterArkInteract(plate);
        RegisterArkGlass(plate);      // DepotButton 带 backdrop-blur-xs
        RegisterArkClick(plate, ArkRandomStart);
        var t = MakeText(plate.transform, "T", ArkFsXLarge, ArkTitleDark(), TextAnchor.MiddleLeft);
        ArkAtPx(t.rectTransform, 4f, ArkVS(24f), ArkWDepot - 8f, ArkVS(56f));
        t.text = Lang.Tr("随机");
        var s = MakeText(plate.transform, "S", ArkFsLarge, ArkTitleVeryDark(), TextAnchor.MiddleLeft);
        ArkAtPx(s.rectTransform, 4f, ArkVS(78f), ArkWDepot - 8f, ArkVS(46f));
        s.text = "random";
    }

    /// <summary>随机开始：切到谱面库页，再按当日加权随机抽一张谱面、走同一条进入动画直接开。
    /// 池子为空或抽出来的条目没有可用文件时只提示，不进关。</summary>
    private static void ArkRandomStart() {
        var pool = LevelScanner.Entries;
        if (pool == null || pool.Count == 0) { ShowToast(Lang.Tr("还没有扫到可用的谱面喵")); return; }
        var entry = RandomPick.Pick(pool);
        if (entry == null || string.IsNullOrEmpty(entry.FilePath) || !System.IO.File.Exists(entry.FilePath)) {
            ShowToast(Lang.Tr("抽中的谱面读不到文件喵")); return;
        }
        SwitchTopTab(1);   // 用户要求：先切到谱面库，再随机打开
        Lg.Info($"[LevelLib] 随机开始：抽中 {entry.Title}（{entry.FilePath}）");
        StartEnterTransition(entry.FilePath);
    }


        /// <summary>好友 / 档案（Buttons.tsx：h-17 w-48 的 BaseButton，flex-col items-end pr-2，文字右对齐；
    /// 上行拉丁小字、下行中文大字）。模板里是直角矩形，没有斜切。左区元素，纵向过 ARK_VSCALE_L。</summary>
    private static void BuildArkSideButton(Transform parent, string name, float x, float y,
    string zh, string latin, Color bg, System.Action onClick = null, int icon = 0) {
        var plate = MakeArkRect(parent, name, x, ArkVYL(y), ArkSideBtnW, ArkVHL(ArkSideBtnH), bg);
        RegisterArkInteract(plate);
        RegisterArkGlass(plate);      // FriendsButton / ArchiveButton 都带 backdrop-blur-xs
        RegisterArkClick(plate, onClick);
        // 左侧图案（用户要求：周报/设置这两枚放左侧，与右侧对齐的文字错开）：垂直居中、左缘留 12。
        // 是 plate 的子节点，跟着按钮一起被逐顶点透视投影（"跟随平行"）。
        if (icon != 0) {
            const float isz = 44f;
            MakeArkIcon(plate.transform, "Icon", 12f + isz * 0.5f, ArkVHL(ArkSideBtnH * 0.5f), isz, icon).color = ArkTitleLight();
        }
        var s = MakeText(plate.transform, "S", ArkFsNormal, ArkSubtitleMid(), TextAnchor.MiddleRight);
        ArkAtPx(s.rectTransform, 0f, ArkVHL(10f), ArkSideBtnW - 8f, ArkVHL(30f));
        s.text = latin;
        var t = MakeText(plate.transform, "T", ArkFsLarge, ArkTitleLight(), TextAnchor.MiddleRight);
        ArkAtPx(t.rectTransform, 0f, ArkVHL(40f), ArkSideBtnW - 8f, ArkVHL(46f));
        t.text = Lang.Tr(zh);
    }

    // ================= 新闻轮播（Columns.tsx NewsCol） =================
    private static Text _arkTitle;                   // 左上角主标题 LevelLibrary
    private static Font _arkKaiFont;                 // 标题用的系统楷体（建一次复用，跨面板留存）
    // ---- 功能映射的动态读数与交互状态 ----
    private static Text _arkNumP, _arkNumG, _arkNumU;       // 资源行三格：P/G/U 级通关数
    private static Text _arkClearedNum, _arkClearedTotal;   // 理智盒：通关数 / 谱面总数（数字部分）
    private static Image _arkClearedIcon;                  // 理智盒里通关数左边那枚勾环（建完就不动它，见 LayoutArkClearedGroup）
    private static Text _arkTotalLabel;                     // 理智盒："谱面总数/"标签（默认字体，右缘贴数字）
    private static readonly System.Collections.Generic.Dictionary<Graphic, System.Action> _arkClicks
        = new System.Collections.Generic.Dictionary<Graphic, System.Action>();   // 大厅按键的点击登记表
    private static bool _arkClickArmed;                     // 左键按下中（抬起时判位移算不算点击）
    private static Vector2 _arkClickDownAt;                 // 按下时的鼠标位置
    private static Graphic _arkClickDownG;                  // 按下时压在哪一块上
    private static bool _arkParallaxFrozen;                 // 左圆钮：冻结视差跟随（临时，不落盘）
    private static Image _arkRoundFreeze;                   // 左圆钮本体（选中时染主题色）
    private static readonly System.Collections.Generic.HashSet<Graphic> _arkActiveTint
        = new System.Collections.Generic.HashSet<Graphic>();   // 选中态染主题色的按键
    private static int _arkUiPhase;                         // 隐藏态：0 显示 / 1 淡出 / 2 全藏 / 3 淡入
    private static bool _arkUiHidden;                       // = 淡出中或全藏（给保险丝与关面板复位用）
    private static float _arkUiA = 1f;                      // 隐藏动画当前透明度
    private static float _arkUiHiddenAt;                    // 藏起来那一刻（防"点按钮的那下"立刻恢复）
    private static CanvasGroup _arkUiCgContent, _arkUiCgNav, _arkUiCgArk;   // 三份透明度载体
    /// <summary>主标题字号：原 140 用户嫌大，缩到 105（比正文的 ArkFsHuge 115 略小一档）。</summary>
    private const int ArkFsTitle = 105;
    private static RectTransform _arkNewsTrack;      // 三张 banner 横向排的那条
    private static readonly Image[] _arkNewsBanners = new Image[3];   // 三张 banner，翻页时按页显隐
    private static readonly CanvasGroup[] _arkNewsCgs = new CanvasGroup[3];   // 每页的透明度（交叉淡入淡出用）
    private static int _arkNewsIndex;
    private static int _arkNewsNextIndex;            // 淡出阶段记住要切到的那一页
    private static int _arkNewsPhase;                // 0 停驻 / 1 旧页淡出 / 2 新页淡入
    private static float _arkNewsFadeA = 1f;         // 当前淡入淡出的透明度
    private static float _arkNewsNextAt;
    private const float ArkNewsIntervalSec = 4f;     // 每页停驻时长
    private const float ArkNewsFadeSec = 0.3f;       // 单侧淡入/淡出时长
    /// <summary>淡出下限：CanvasGroup.alpha 精确为 0 会把 CanvasRenderer 剔除掉，
    /// 恢复显隐后不重画（要等顶点重建才回来）。留 0.004 肉眼不可见但不进剔除。</summary>
    private const float ArkNewsAlphaFloor = 0.004f;
    private static readonly Image[] _arkNewsDots = new Image[3];

    /// <summary>新闻轮播：288x136 的窗 + BREAKING NEWS 红标 + 三张 banner + 底部三段指示条。
    /// 示意图里是 4 秒自动切、拖拽能翻页、拖拽中暂停自动切（那些口径都照搬）。</summary>
    private static void BuildArkNews(Transform parent) {
        // 窗：一块直角矩形当底。模板里 NewsCol 外面那个 div 是直角的，斜切是我多加的。
        // 左区整体纵向缩过，所以 top/h 走 ArkVAt（window 里的 banner 是它的子节点，不用再过）。
        var win = MakeArkRect(parent, "ArkNews", ArkNewsX, ArkVYL(ArkNewsY), ArkNewsW, ArkVHL(ArkNewsH),
                              ArkItemsBg());
        // 三张 banner 横向排开，但翻页不再滑动：窗外没有裁剪（RectMask2D 与顶点投影冲突，
        // 见下一段注释），滑动过程中出入两侧的字都露在窗外，实机就是"自动更换后位置错误"。
        // 改成交叉淡入淡出：轨道只停在各页整位上，任何时刻都只有整页内容落在窗内。
        // 不用 RectMask2D：遮罩按未投影的矩形算裁剪框，而本项目的顶点会被 ArkPerspective
        // 挪到投影后的位置，两套坐标对不上，裁剪边界会错位，把窗内第一个字切掉半截
        // （实机表现是"新版发布只剩版发布"，用户报"不知名遮罩挡住文字"）。
        // 红标：BREAKING NEWS（bg-news-bg 的实底小条，w-33）
        var tag = MakeImage(parent, "ArkNewsTag", ArkNewsBg());
        tag.raycastTarget = false;
        ArkVAt(tag.rectTransform, ArkNewsX - 8f, ArkNewsY - 24f, ArkNewsTagW, 28f);
        SoftifyArkPlate(tag, 0f);   // 软边板：透视倾斜下硬边会有锯齿
        var tt = MakeText(tag.transform, "T", ArkFsXSmall, Color.white, TextAnchor.MiddleLeft);
        ArkVAt(tt.rectTransform, 6f, 0f, ArkNewsTagW - 12f, 28f);
        tt.fontStyle = FontStyle.Bold;
        tt.text = "BREAKING NEWS";

        // 轨道：铺满整窗。三张 banner 各占一页、页间 gap 24；翻页用交叉淡入淡出，
        // 轨道只停在各页整位上（见 TickArkNews 的注释）。
        // Stretch 的 offsetMin/offsetMax 是像素边距（不是归一化比例），四边都给 0 才是刚好铺满。
        _arkNewsTrack = MakeArkLayer(win.transform, "Track", 0f);
        Stretch(_arkNewsTrack, Vector2.zero, Vector2.zero);
        // 轮播按用户要求不接任何点击/悬停交互，只随时间自动切换。

        string[] heads = { Lang.Tr("新版发布"), Lang.Tr("问题修复"), Lang.Tr("功能新增") };
        string[] subs = { "v3.3.1", "神经图闪现", "单个导入压缩包" };
        for (int i = 0; i < 3; i++) {
            float x = i * ArkNewsPage;
            var b = MakeImage(_arkNewsTrack, "B" + i, new Color(0f, 0f, 0f, 0f));
            b.sprite = SharpSprite; b.type = Image.Type.Simple; b.raycastTarget = false;
            ArkAtPx(b.rectTransform, x, 0f, ArkNewsW, ArkVHL(ArkNewsH));
            _arkNewsBanners[i] = b;
            _arkNewsCgs[i] = b.gameObject.AddComponent<CanvasGroup>();   // 交叉淡入淡出写这里的 alpha
            var h1 = MakeText(b.transform, "H", ArkFsNormal, Color.white, TextAnchor.MiddleLeft);
            ArkAtPx(h1.rectTransform, 16f, ArkVHL(20f), ArkNewsW - 60f, ArkVHL(32f));
            h1.fontStyle = FontStyle.Bold;
            h1.text = heads[i];
            var h2 = MakeText(b.transform, "S", ArkFsSmall, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft);
            ArkAtPx(h2.rectTransform, 16f, ArkVHL(ArkNewsH - 48f), ArkNewsW - 60f, ArkVHL(28f));
            h2.text = subs[i];
        }

        // 底部三段指示条：三段等分整窗宽（18/(1.1*3-0.1) rem 宽、间隔 i*1.1*18/3.2 rem），
        // 当前项用 bg-news-scrollbar-bg
        for (int i = 0; i < 3; i++) {
            var dot = MakeImage(parent, "ArkNewsDot" + i, new Color(1f, 1f, 1f, 0.22f));
            dot.sprite = SharpSprite; dot.type = Image.Type.Simple; dot.raycastTarget = false;
            ArkVAt(dot.rectTransform, ArkNewsX + i * 99f, ArkNewsY + ArkNewsH + 8f, 90f, 6f);
            _arkNewsDots[i] = dot;
        }
        _arkNewsIndex = 0;
        _arkNewsPhase = 0;
        _arkNewsFadeA = 1f;
        _arkNewsNextAt = Time.unscaledTime + ArkNewsIntervalSec;
        PaintArkNewsDots();
    }

    private static void PaintArkNewsDots() {
        for (int i = 0; i < _arkNewsDots.Length; i++) {
            if (_arkNewsDots[i] == null) continue;
            _arkNewsDots[i].color = (i == _arkNewsIndex) ? ArkNewsScroll() : new Color(1f, 1f, 1f, 0.22f);
        }
    }



    /// <summary>轮播推进（用户要求：不能拖不能点，只随时间自动换）。
    /// 切换用"淡出旧页、轨道瞬移、淡入新页"的交叉淡入淡出，不再横向滑动：
    /// 窗外没有裁剪（RectMask2D 与顶点投影冲突，见 BuildArkNews 注释），滑动过程中
    /// 出入两侧的标题字都露在窗外，实机就是"自动更换后位置错误、看着像闪回去"。
    /// 轨道永远只停在各页整位上，任何时刻都只有整页内容落在窗内，位置永远正确。</summary>
    private static void TickArkNews() {
        try {
            if (_arkNewsTrack == null) return;
            float page = ArkNewsPage * ArkPx;      // 翻一页的位移（画布单位）
            // 轨道直接写整位（同值时 Unity 内部短路，不花钱），不做滑动插值
            var ap = _arkNewsTrack.anchoredPosition;
            float wantX = -_arkNewsIndex * page;
            if (Mathf.Abs(ap.x - wantX) > 0.01f) {
                ap.x = wantX; _arkNewsTrack.anchoredPosition = ap;
                // 真根因：RectTransform 挪位置不会让顶点重算，而透视投影是"烘进网格"的 ——
                // 轨道换了位置，网格里的投影还是按旧位置算的，整页内容错位/甩出窗口，
                // 要等鼠标动了 ApplyArkYaw 才 MarkDirty 重建（用户报的"切换后消失、动鼠标恢复"）。
                // 所以每次挪轨道必须当场把远层所有透视组件标脏。
                for (int i = 0; _arkPerspFar != null && i < _arkPerspFar.Length; i++)
                    if (_arkPerspFar[i] != null) _arkPerspFar[i].MarkDirty();
            }
            float dt = Time.unscaledDeltaTime;
            if (_arkNewsPhase == 0) {
                // 停驻：当前页保持全亮，等计时到点进入淡出
                var cg = _arkNewsCgs[_arkNewsIndex];
                if (cg != null && cg.alpha < 1f) cg.alpha = 1f;
                if (Time.unscaledTime >= _arkNewsNextAt) {
                    _arkNewsNextIndex = (_arkNewsIndex + 1) % 3;
                    _arkNewsPhase = 1;
                }
            } else if (_arkNewsPhase == 1) {
                // 淡出旧页（此刻 index 还是旧页）；淡干净后瞬移到新页。
                // 下限不写到 0：CanvasGroup.alpha 恰好为 0 时 CanvasRenderer 会被剔除（cull），
                // 之后把 alpha 写回来、网格又没动的话它就一直不被重画 —— 实机就是"内容消失、
                // 动一下鼠标（顶点重建）才回来"。夹在 0.004 上，肉眼一样看不见，但不进剔除。
                _arkNewsFadeA -= dt / ArkNewsFadeSec;
                if (_arkNewsFadeA <= ArkNewsAlphaFloor) {
                    _arkNewsFadeA = ArkNewsAlphaFloor;
                    _arkNewsIndex = _arkNewsNextIndex;
                    PaintArkNewsDots();
                    _arkNewsPhase = 2;
                }
                var cg = _arkNewsCgs[_arkNewsIndex];
                if (cg != null) cg.alpha = _arkNewsFadeA;
            } else {
                // 淡入新页，走完回停驻
                _arkNewsFadeA += dt / ArkNewsFadeSec;
                if (_arkNewsFadeA >= 1f) {
                    _arkNewsFadeA = 1f;
                    _arkNewsPhase = 0;
                    _arkNewsNextAt = Time.unscaledTime + ArkNewsIntervalSec;
                }
                var cg = _arkNewsCgs[_arkNewsIndex];
                if (cg != null) cg.alpha = _arkNewsFadeA;
            }
            // 显隐改用纯透明度：三张 banner 常驻在层里、谁都不 SetActive。早先"藏起来的整组
            // SetActive(false)、轮到它再打开"，重新激活那一刻重建的网格用的是当时的层位移，
            // 而鼠标不动时 SetTilt 不重建顶点，这块就停在错位/看不见的状态，直到动鼠标才恢复
            //（用户报"切换后会消失错位、动鼠标就恢复"）。常驻 + 只写 alpha 后它永远跟着
            // 界面一起被透视平移，不再有"重新入层"这个动作。非当前页 alpha 恒 0，不参与渲染。
            for (int i = 0; i < _arkNewsBanners.Length; i++) {
                var cg = _arkNewsCgs[i];
                if (cg == null) continue;
                float want = (i == _arkNewsIndex) ? Mathf.Max(_arkNewsFadeA, ArkNewsAlphaFloor) : ArkNewsAlphaFloor;
                if (Mathf.Abs(cg.alpha - want) > 0.002f) cg.alpha = want;
            }
        } catch { }
    }

    // ================= 交互与动效 =================

    private static readonly System.Collections.Generic.List<Graphic> _arkHoverables =
        new System.Collections.Generic.List<Graphic>();
    private static Graphic _arkHovering;
    /// <summary>悬停前的基色备份。"键 = 元素"各存一份，只在第一次变亮时写入。
    /// 为什么不现读现算：SetArkHover 里如果读 g.color 再乘系数，还原那一下乘的是 1，
    /// 等于把"已经变亮的颜色"当成基色写回去 —— 颜色只会一路累积，鼠标来回划几次整块就白了。
    /// 备份只写一次（ContainsKey 守卫），之后还原一律写备份值，这才是一个状态一个写入者。</summary>
    private static readonly System.Collections.Generic.Dictionary<Graphic, Color> _arkBaseColor =
        new System.Collections.Generic.Dictionary<Graphic, Color>();

    /// <summary>登记一个能点的元素：悬停提亮 + 按压压暗（示意图里 BaseButton 的 after:bg-black/20 与那层高光）。
    /// 静态列表，不每帧分配。顺带给背景边缘加投影（用户要求所有按键块带 shadow）：
    /// 这里是所有按键块的唯一汇合点，阴影挂在这里一处就全覆盖。
    /// 阴影用渐变精灵图（与库界面面板那套同一张 CreateRoundedShadowSprite），不是 BaseMeshEffect
    /// 那种把矩形顶点复制一份向下偏移的 Shadow 组件——那种在 Image 上就是一条黑边贴在下面，
    /// 不像真正的阴影。这里阴影画在按键的同父级、按键之前（SetAsFirstSibling = 画在底下），
    /// 跟着按键的 RectTransform 走，也跟着 ArkPerspective 一起投影。</summary>
    private static void RegisterArkInteract(Graphic g, bool blockShadow = true) {
        if (g == null) return;
        try {
            g.raycastTarget = true;
            if (blockShadow) AddArkBlockShadow(g, 0.62f);
            if (!_arkHoverables.Contains(g)) _arkHoverables.Add(g);
        } catch { }
    }

    /// <summary>大厅按键块的渐变阴影贴图缓存（按圆角半径分桶）。
    /// 不用面板那套 CreateRoundedShadowSprite：那个内圈 alpha=0、在 d=inner 处用 6% 带宽急升到 peak，
    /// 这条硬过渡经 ArkPerspective 投影后就是多边形折线（锯齿）。这里自己生成：中心填满 peak
    /// （按键盖住它），四边平滑收零、任何位置都没有 alpha 突变 → 透视不出锯齿。</summary>
    private static readonly System.Collections.Generic.Dictionary<int, Sprite> _arkShadowSprites
        = new System.Collections.Generic.Dictionary<int, Sprite>();
    private const float ArkShadowPad = 20f;

    private static Sprite EnsureArkShadowSprite(int radius) {
        Sprite sp;
        if (_arkShadowSprites.TryGetValue(radius, out sp)) return sp;
        try { sp = CreateArkShadowSprite(radius, ArkShadowPad, 0.55f); }
        catch (System.Exception e) { Lg.Ex("生成大厅阴影贴图失败（这块不加阴影，其它照旧）", e); sp = null; }
        _arkShadowSprites[radius] = sp;
        return sp;
    }

    /// <summary>生成一张"中心不透明、四边平滑收零"的阴影贴图。
    /// 内圈没有硬过渡（中心 alpha=peak，渐变只在外圈带）→ 9-slice 经透视投影后不出锯齿。</summary>
    private static Sprite CreateArkShadowSprite(int radius, float margin, float peak) {
        int r = Mathf.Clamp(Mathf.RoundToInt(radius), 4, 32);
        int pad = Mathf.Max(8, Mathf.RoundToInt(margin));
        int border = r + pad;
        const int Mid = 4;
        int size = border * 2 + Mid;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "arkshadow" + size);
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[size * size];
        float half = size * 0.5f;
        float hw = half - pad;
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float fx = x + 0.5f - half, fy = y + 0.5f - half;
                float qx = Mathf.Abs(fx) - (hw - r);
                float qy = Mathf.Abs(fy) - (hw - r);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float a;
                if (d <= 0f) {
                    a = peak;   // 中心填满：按键盖住它，只露出外圈渐变
                } else {
                    float t = Mathf.Clamp01(d / pad);
                    a = peak * (1f - t) * (1f - t);   // 二次收零、无突变
                }
                px[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return PerfDiag.TagSprite(Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border)), "arkshadow" + size);
    }

    /// <summary>给一个按键块加投影边：比按键大一圈的渐变阴影图，四边向外淡出。
    /// 中心填满 peak（按键盖住），渐变只在外圈带、没有硬内边 → 透视投影后不出锯齿。</summary>
    private static void AddArkBlockShadow(Graphic g, float alpha) {
        if (g == null) return;
        try {
            var sp = EnsureArkShadowSprite(4);
            if (sp == null) return;
            var parent = g.transform.parent;
            if (parent == null) return;
            var go = new GameObject(g.name + "Shadow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = sp;
            img.type = Image.Type.Sliced;   // 9-slice：中心纯色、渐变在边框带、与按键大小无关
            img.color = new Color(0f, 0f, 0f, 1f);   // 贴图像素 alpha 直接是显示浓度
            var grt = g.rectTransform;
            var irt = img.rectTransform;
            irt.anchorMin = grt.anchorMin;
            irt.anchorMax = grt.anchorMax;
            irt.pivot = grt.pivot;
            irt.anchoredPosition = grt.anchoredPosition;   // 同中心：四边对称各扩 pad
            irt.sizeDelta = grt.sizeDelta + new Vector2(2f * ArkShadowPad, 2f * ArkShadowPad);
            irt.SetAsFirstSibling();   // 画在按键底下
        } catch { }
    }

    /// <summary>给文字加投影（Shadow 组件）。文字投影走 BaseMeshEffect 才对——它把字形顶点复制
    /// 一份再偏移，渐变阴影图对 Text 不适用（Text 没有固定矩形轮廓）。偏移向下 8、alpha 0.6。</summary>
    private static void AddArkShadow(Graphic g, float alpha) {
        if (g == null) return;
        try {
            var sh = g.GetComponent<Shadow>();
            if (sh == null) sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, alpha);
            sh.effectDistance = new Vector2(0f, -8f);
            sh.useGraphicAlpha = true;
        } catch { }
    }

    /// <summary>登记一块大厅按键的点击动作。点击不用 uGUI 的 Button 事件链（大厅是自绘悬停，
    /// 再挂一套 EventSystem 判定等于两套输入对着抢），沿用悬停同一套屏幕矩形命中：
    /// 按下记下压在哪块上，抬起时还压在同一块、且位移小于 8px 才算点击 —— 拖动不触发。
    /// 没注册动作的块（叠加层、仓库这些暂时没有对应页面的）照旧只悬停不响应。</summary>
    private static void RegisterArkClick(Graphic g, System.Action act) {
        if (g == null || act == null) return;
        _arkClicks[g] = act;
    }

    private static Graphic ArkClickHit(Vector2 m) {
        foreach (var kv in _arkClicks) {
            var g = kv.Key;
            if (g == null || !g.isActiveAndEnabled) continue;
            if (ArkRectContains(g.rectTransform, m)) return g;
        }
        return null;
    }

    private static void TickArkClick() {
        try {
            // 隐藏态：任何一次鼠标按下都恢复（刚藏起的 0.3 秒内不算，防"点按钮的那一下"自己把自己放出来）
            if (_arkUiHidden) {
                if (Input.GetMouseButtonDown(0) && Time.unscaledTime > _arkUiHiddenAt + 0.3f
                    && !ArkPointerBlocked(UiInput.MousePosition)) ArkSetUiHidden(false);
                return;
            }
            var m = UiInput.MousePosition;
            // 弹窗遮挡判据：指针最顶上的射线结果如果不是大厅自己这块，说明有更高层的 UI
            //（周报/生日/确认窗之类的弹窗）压在上面 —— 此时点的是弹窗，大厅这里必须装聋。
            // 大厅自己的可点块也会出现在射线结果里，所以"最顶块是大厅的可点块"就不算被挡。
            bool blocked = ArkPointerBlocked(m);
            if (Input.GetMouseButtonDown(0) && !_arkClickArmed) {
                _arkClickArmed = true;
                _arkClickDownAt = m;
                _arkClickDownG = blocked ? null : ArkClickHit(m);
            } else if (Input.GetMouseButtonUp(0) && _arkClickArmed) {
                _arkClickArmed = false;
                var g = blocked ? null : ArkClickHit(m);
                if (g != null && g == _arkClickDownG && (new Vector2(m.x, m.y) - _arkClickDownAt).sqrMagnitude < 64f) {
                    System.Action act;
                    if (_arkClicks.TryGetValue(g, out act) && act != null) act();
                }
                _arkClickDownG = null;
            }
        } catch { }
    }

    /// <summary>指针最顶上的射线命中是不是别人的（比大厅更高层的 UI）。大厅自身没登记点击的
    /// 悬停块也算"自己的"—— 它们反正不触发动作，不值得为它们挡住弹窗的按钮。</summary>
    private static bool ArkPointerBlocked(Vector2 m) {
        try {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = m };
            var results = new System.Collections.Generic.List<RaycastResult>();
            es.RaycastAll(ped, results);
            for (int i = 0; i < results.Count; i++) {
                var g = results[i].gameObject.GetComponent<Graphic>();
                if (g == null) continue;
                if (_arkClicks.ContainsKey(g)) return false;   // 最顶上就是大厅自己的可点块
                if (_arkHoverables.Contains(g)) continue;      // 大厅的悬停块，继续往下找
                return true;                                   // 别人的 UI 压在最顶上
            }
            return false;
        } catch { return false; }
    }

    /// <summary>左圆钮：冻结/恢复视差跟随。只翻一个运行时标志，TickArk3D 看到它就直接不动
    /// （角度、层位移全都停在冻结那一刻），不写盘，面板重开也保持。
    /// 冻结中给按钮本身染主题色当"选中"提示（SetArkHover 看到它在选中表里就写主题色，悬停不再抢）。</summary>
    private static void ArkToggleParallaxFreeze() {
        _arkParallaxFrozen = !_arkParallaxFrozen;
        if (_arkRoundFreeze != null) {
            if (_arkParallaxFrozen) _arkActiveTint.Add(_arkRoundFreeze);
            else _arkActiveTint.Remove(_arkRoundFreeze);
            SetArkHover(_arkRoundFreeze, false);   // 当场把颜色落到目标值（选中=主题色 / 取消=基色）
        }
        Lg.Info("大厅视差" + (_arkParallaxFrozen ? "已冻结" : "已恢复"));
    }

    /// <summary>右圆钮：把大厅与页面内容全部淡出、只留背景（有视频就是视频，没有就是代码背景）；
    /// 再点任意处淡入恢复。两段都是渐变，不瞬隐瞬现。只翻运行时标志，不落盘。
    /// 背景那层（视频/代码背景）挂在与页面内容不同的组上，藏内容不动背景。</summary>
    private static void ArkToggleUiHide() { ArkSetUiHidden(_arkUiPhase == 0 || _arkUiPhase == 3); }

    /// <summary>隐藏态的四个阶段：0 正常显示 / 1 淡出中 / 2 全藏 / 3 淡入中。_arkUiA 是当前透明度。</summary>
    private const float ArkUiFadeSec = 0.3f;

    private static void ArkSetUiHidden(bool on) {
        EnsureArkUiGroups();
        if (on) {
            if (_arkUiPhase == 1 || _arkUiPhase == 2) return;
            _arkUiPhase = 1;
            _arkUiHiddenAt = Time.unscaledTime;
            Lg.Info("大厅淡出隐藏（点任意处淡入恢复）");
        } else {
            // 阶段 2 时内容组是收起来的，淡入前必须先放回来（透明度从 0 爬）
            if (_arkUiPhase == 2) {
                try {
                    if (_contentRoot != null && !_contentRoot.gameObject.activeSelf) _contentRoot.gameObject.SetActive(true);
                    if (_topTabRoot != null && !_topTabRoot.gameObject.activeSelf) _topTabRoot.gameObject.SetActive(true);
                } catch { }
            }
            if (_arkUiPhase == 1 || _arkUiPhase == 2) {
                _arkUiPhase = 3;
                Lg.Info("大厅淡入恢复");
            }
        }
        // 面板已经关掉的话没人再跑动画了，透明度必须当场落回 1，
        // 否则下次开面板内容组还在（active）却 alpha=0，整屏看不见。
        if (!_panelOpen) {
            _arkUiPhase = 0; _arkUiA = 1f; _arkUiHidden = false;
            try {
                if (_arkUiCgContent != null) { _arkUiCgContent.alpha = 1f; _arkUiCgContent.blocksRaycasts = true; }
                if (_arkUiCgNav != null) { _arkUiCgNav.alpha = 1f; _arkUiCgNav.blocksRaycasts = true; }
                if (_arkUiCgArk != null) _arkUiCgArk.alpha = 1f;
            } catch { }
            return;
        }
        _arkUiHidden = (_arkUiPhase == 1 || _arkUiPhase == 2);
    }

    /// <summary>懒建三份 CanvasGroup：页面内容、顶部导航、大厅画布根。透明度统一写 _arkUiA。</summary>
    private static void EnsureArkUiGroups() {
        try {
            if (_arkUiCgContent == null && _contentRoot != null)
                _arkUiCgContent = _contentRoot.gameObject.GetComponent<CanvasGroup>()
                                  ?? _contentRoot.gameObject.AddComponent<CanvasGroup>();
            if (_arkUiCgNav == null && _topTabRoot != null)
                _arkUiCgNav = _topTabRoot.GetComponent<CanvasGroup>() ?? _topTabRoot.AddComponent<CanvasGroup>();
            if (_arkUiCgArk == null && _arkCanvas != null)
                _arkUiCgArk = _arkCanvas.gameObject.GetComponent<CanvasGroup>()
                              ?? _arkCanvas.gameObject.AddComponent<CanvasGroup>();
        } catch { }
    }

    /// <summary>隐藏态动画：淡出走完把内容/导航收起来（真省渲染），淡入先放回来再爬透明度。
    /// 大厅画布整段都开着（靠 CanvasGroup.alpha 淡），Shell 那边不再因为隐藏态去关它。</summary>
    private static void TickArkHide() {
        if (_arkUiPhase == 0) return;
        float dt = Time.unscaledDeltaTime;
        if (_arkUiPhase == 1) {
            _arkUiA -= dt / ArkUiFadeSec;
            if (_arkUiA <= 0f) {
                _arkUiA = 0f;
                _arkUiPhase = 2;
                try {
                    if (_contentRoot != null) _contentRoot.gameObject.SetActive(false);
                    if (_topTabRoot != null) _topTabRoot.gameObject.SetActive(false);
                } catch { }
            }
        } else if (_arkUiPhase == 3) {
            _arkUiA += dt / ArkUiFadeSec;
            if (_arkUiA >= 1f) { _arkUiA = 1f; _arkUiPhase = 0; }
        } else {
            return;   // 阶段 2：保持全藏，透明度不再动
        }
        if (_arkUiCgContent != null) { _arkUiCgContent.alpha = _arkUiA; _arkUiCgContent.blocksRaycasts = _arkUiA > 0.5f; }
        if (_arkUiCgNav != null) { _arkUiCgNav.alpha = _arkUiA; _arkUiCgNav.blocksRaycasts = _arkUiA > 0.5f; }
        if (_arkUiCgArk != null) _arkUiCgArk.alpha = _arkUiA;
        _arkUiHidden = (_arkUiPhase == 1 || _arkUiPhase == 2);
    }

    /// <summary>大厅按键跳设置页的指定分区：先落主/子 tab 的目标值再切页。
    /// 设置页跨切页是"留着只切显隐"的，切过去并不会重刷选中 tab —— 所以切完必须
    /// 当场补一次 RefreshSettingsPage，否则永远停在上次离开时的那个 tab。</summary>
    private static void ArkOpenSettings(int mainTab, int subTab) {
        // 上界用 ExtraTabIndex（当前最后一个主 tab）：插了「去特效」后共 6 页，写死 4 会把
        // 扩展(5) 夹成工具(4) —— 肥鱼设定那枚按键就是这么跳错的（用户报过）。
        _setMainTab = Mathf.Clamp(mainTab, 0, ExtraTabIndex);
        _setSubTab = Mathf.Max(0, subTab);
        SwitchTopTab(SettingsSlotIndex);
        try {
            if (_settingsPage != null && _settingsPage.activeSelf) RefreshSettingsPage();
        } catch { }
    }

    // ---- 功能映射的统计口径（与库页统计行同一套来源）----

    /// <summary>通关数：有通关记录的谱面数（LevelScanner.CountXaccLevels，库页同款）。</summary>
    private static int ArkClearedCount() {
        try { return LevelScanner.CountXaccLevels(); } catch { return 0; }
    }

    /// <summary>总谱面数：扫描条目总数（库页统计行同款）。</summary>
    private static int ArkTotalCount() {
        try { return LevelScanner.Entries.Count; } catch { return 0; }
    }

    /// <summary>P/G/U 某一档的通关数：有通关记录、且等级标记档位头是这一级的谱面数。
    /// 标记取 MarkStore（手动标 + 自动评级写的那份），头字母 P/G/U 各算各的。</summary>
    private static int ArkPguCount(char head) {
        int n = 0;
        try {
            var es = LevelScanner.Entries;
            for (int i = 0; i < es.Count; i++) {
                var e = es[i];
                if (e == null || string.IsNullOrEmpty(e.FilePath)) continue;
                if (!LevelScanner.HasClearRecords(e.FolderPath)) continue;
                string m = MarkStore.Get(e.FilePath);
                if (!string.IsNullOrEmpty(m) && m[0] == head) n++;
            }
        } catch { }
        return n;
    }

    /// <summary>每帧：悬停态（鼠标在哪就提亮哪一块）+ 3D 跟随 + 读数 + 磨砂。</summary>
    private static void TickArk() {
        TickArkDiag();          // 自检要排在"建过没有"的判断之前：没建出来的那一次才是最需要它的
        if (!_arkBuilt) return;
        TickArkFuse();
        TickArkHover();
        TickArkClick();         // 大厅按键的点击（屏幕矩形命中 + 位移抑制，与悬停同一套判法）
        TickArkHide();          // 右圆钮的隐藏/恢复淡出淡入
        TickArkPortrait();      // 立绘：换图自愈 + 小界面收放（位置那一半归流式布局引擎）
        TickArk3D();
        TickArkGlass();
        TickArkNews();          // 轮播：翻页计时、track 滑动、banner 按位置显隐（漏了它 banner 会全亮在窗外）
        TickArkInfo();
    }

    /// <summary>保险丝：停在大厅、面板也开着、而且没有被盖层盖住的时候，
    /// 大厅这块画布必须是亮的、整屏渐入必须已经走完。不满足就当场掰回来。
    /// 为什么要它：「初次进来空白、Esc 关掉再打开才出现」这类问题的判据链是"状态变化驱动"的
    /// （enabled 只在盖层进出时写、渐入只在武装之后才推进），
    /// 而真正出问题的那条路径很可能压根没走到那次变化，被动等判据就是永远等不到。
    /// 这里只补"该亮却没亮"，不去动"该灭"的判断，所以不会在盖层底下把大厅提前放出来。
    /// 每次修正都打日志：真出了问题，这一行就是第一现场。</summary>
    private static void TickArkFuse() {
        if (!_panelOpen || _topTab != MainScreenSlot) return;
        if (_arkUiHidden) return;   // 右圆钮的隐藏态：大厅画布就是该灭的，保险丝别把它掰回来
        if (_closing) return;   // 关闭淡出期间让路：淡出每帧把 alpha 往 0 写，保险丝看到"没在渐入却不到 1"
                                // 会把它掰回 1，两条路对着写 = 淡出永远看不见（用户报"退出不跟着淡出"的根因）。
        if (_panelRenderStopped) return;              // 有盖层盖着，这时候不该亮
        try {
            if (_arkCanvas != null && !_arkCanvas.enabled) {
                _arkCanvas.enabled = true;
                Lg.Info("大厅保险丝：停在大厅却没在渲染，已把它打开（enabled 本应是 true，说明上面某条判据漏了一次）");
            }
            if (_mainGroup != null && !_mainFading && _mainGroup.alpha < 0.999f) {
                _mainGroup.alpha = 1f;
                Lg.Info("大厅保险丝：渐入停在了 "
                    + _mainGroup.alpha.ToString("F2") + " 没走完，已直接补满");
            }
        } catch { }
    }

    /// <summary>可见性自检：进来之后每秒打一行，把决定"看不看得见"的量全列出来。
    /// 「初次进来空白」这种问题光看代码推不出来 —— 有六七个量各自都可能让它不可见，
    /// 逐个猜要跑很多轮；一次打全，跑一趟就能定位到是哪一环。打满 ArkDiagTimes 次就闭嘴。</summary>
    private const int ArkDiagTimes = 10;
    private static int _arkDiagLeft = ArkDiagTimes;
    private static float _arkDiagNextAt;

    private static void TickArkDiag() {
        if (_arkDiagLeft <= 0) return;
        if (!_panelOpen || _topTab != MainScreenSlot) return;
        if (Time.unscaledTime < _arkDiagNextAt) return;
        _arkDiagNextAt = Time.unscaledTime + 1f;
        _arkDiagLeft--;
        try {
            var crt = _arkCanvas != null ? _arkCanvas.GetComponent<RectTransform>() : null;
            Lg.Info("大厅自检："
                + "画布enabled=" + (_arkCanvas != null ? _arkCanvas.enabled.ToString() : "无")
                + " mode=" + (_arkCanvas != null ? _arkCanvas.renderMode.ToString() : "无")
                + " 画布尺寸=" + (crt != null ? crt.rect.size.ToString() : "-")
                + " 层=" + (_arkLayerNear != null && _arkLayerNear.gameObject.activeInHierarchy ? "在" : "无")
                + " 面板enabled=" + (_panelCanvas != null ? _panelCanvas.enabled.ToString() : "无")
                + " 渐入alpha=" + (_mainGroup != null ? _mainGroup.alpha.ToString("F2") : "无")
                + " 渐入中=" + _mainFading
                + " 停渲染=" + _panelRenderStopped
                + " tab=" + _topTab
                + " 近层pos=" + (_arkLayerNear != null ? _arkLayerNear.anchoredPosition.ToString() : "-")
                + " euler=" + (_arkLayerNear != null ? _arkLayerNear.localEulerAngles.ToString() : "-"));
        } catch { }
    }

    /// <summary>悬停：示意图里没有自定义 hover 色，只有一个很轻的提亮（我按"轻"来做，别抢主体）。
    /// 静止不动时也要判（指针静止在按钮上时它该一直亮着），但值没变就不写颜色。
    /// 提亮的算法是"基色乘系数"，基色取第一次变亮前的那一份备份，见 _arkBaseColor。</summary>
    private static void TickArkHover() {
        try {
            Graphic hit = null;
            var m = UiInput.MousePosition;
            for (int i = _arkHoverables.Count - 1; i >= 0; i--) {
                var g = _arkHoverables[i];
                if (g == null || !g.isActiveAndEnabled) { _arkHoverables.RemoveAt(i); continue; }
                if (!ArkRectContains(g.rectTransform, m)) continue;
                hit = g; break;
            }
            if (hit == _arkHovering) return;
            SetArkHover(_arkHovering, false);
            SetArkHover(hit, true);
            _arkHovering = hit;
        } catch { }
    }

    private static void SetArkHover(Graphic g, bool on) {
        if (g == null) return;
        try {
            // 选中态按键（视差冻结中）永远染主题色：悬停的提亮/还原都不许覆盖这个提示
            if (_arkActiveTint.Contains(g)) { g.color = ArkAccent(); return; }
            Color baseCol;
            if (!_arkBaseColor.TryGetValue(g, out baseCol)) {
                // 第一次碰它：此刻的颜色就是基色，记下来再也不要覆盖（否则第二帧就把亮色当基色了）
                baseCol = g.color;
                _arkBaseColor[g] = baseCol;
            }
            if (!on) {
                g.color = baseCol;      // 还原一律写备份值，不做计算
                return;
            }
            const float k = 1.12f;
            g.color = new Color(Mathf.Min(1f, baseCol.r * k), Mathf.Min(1f, baseCol.g * k),
                                Mathf.Min(1f, baseCol.b * k), baseCol.a);
        } catch { }
    }


    /// <summary>点是否落在这一块内。画布是 Overlay 模式，第三个参数必须传 null：
    /// 传相机会按相机投影去算，而 Overlay 的屏幕坐标与相机无关。
    /// 早先这里还试过把鼠标屏幕坐标除以 10 当世界坐标再 WorldToScreenPoint 那种凑法，是错的，已删。</summary>
    private static bool ArkRectContains(RectTransform rt, Vector2 screenPos) {
        if (rt == null) return false;
        try { return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null); }
        catch { return false; }
    }

    /// <summary>3D 跟随：左右两组一正一负朝中间合拢（App.tsx:154/216）。
    /// 幅度必须照模板原值算，不能凭手感放大 —— 模板里鼠标归一化后的 x 是 [-0.5, 0.5]，
    /// 左组 roty = 6 + x * 2 只会在 [5, 7] 度之间摆（基准 6 度、摆幅 1 度），
    /// 右组 roty = x * 2 - 6 同理。早先这里写成 nx * 2 * 20 是 ±20 度，整整放大二十倍，
    /// 于是左区最左边那几块被投影推到了屏幕外面（用户报的"左侧界面超出左侧屏幕"）。
    /// tranx = -x * 2 的单位是 rem，也就是 -x * 32 CSS 像素，换算到项目单位再乘 ArkPx。
    /// 角度取整到 0.05 度，否则静止时画面每帧微抖。</summary>
    private static void TickArk3D() {
        try {
            if (!_ark3DReady) return;
            if (_arkParallaxFrozen) return;   // 左圆钮冻结：角度与层位移都停在冻结那一刻，鼠标怎么动都不跟
            var m = UiInput.MousePosition;
            float nx = Mathf.Clamp(m.x / Mathf.Max(1f, Screen.width) - 0.5f, -0.5f, 0.5f);
            float ny = Mathf.Clamp(m.y / Mathf.Max(1f, Screen.height) - 0.5f, -0.5f, 0.5f);
            float yaw = Mathf.Round(nx * 2f * 20f) / 20f;          // 摆幅 1 度，量化 0.05 度
            float pitch = Mathf.Round((-ny / 1.8f) * 20f) / 20f;   // rotx = y / 1.8，量级零点几度
            float shift = Mathf.Round(-nx * 32f * ArkPx);          // tranx 的 rem 换成项目单位
            ApplyArkYaw(_arkLayerFar, _arkPerspFar, ArkYawLeft + yaw, pitch, shift, ArkPadFar, ArkPadDown);
            ApplyArkYaw(_arkLayerNear, _arkPerspNear, ArkYawRight + yaw, pitch, shift, ArkPadNear, ArkPadDownRight);
            // 立绘吃同一个横向位移：与大厅平行移动，但它自己那层不吃透视，所以不被倾斜
            ApplyArkPortraitShift(shift);
        } catch { }
    }

    /// <summary>把这一层的倾斜与位移落实下去。
    /// 位移仍然写层的 localPosition（正交投影下就是平移，与以前一样）；
    /// 倾斜则交给逐顶点透视组件 —— 层自己不再旋转，两个一起用就是双重变换。
    /// baseShift 是这一层恒定的横向避让量（见 ArkPadFar / ArkPadNear），单位与层坐标一致、正数往右。
    /// baseDown 是恒定的纵向避让量（见 ArkPadDown / ArkPadDownRight），约定正数往下 ——
    /// 但 localPosition.y 是"上为正"，所以写进去要取负。
    /// 这一条踩过：早先直接写 +baseDown，于是"往下挪"变成了往上挪，
    /// 左上角那几枚图标被顶得更高、实机看就是"左上角按键还在屏幕外面"（越挪越出屏）。
    /// 五个量都没变时这里什么都不做，所以每帧调用不花钱。</summary>
    private static void ApplyArkYaw(RectTransform layer, ArkPerspective[] views, float yaw, float pitch,
                                    float shift, float baseShift, float baseDown) {
        shift += baseShift;
        if (layer == null) return;
        float wantY = -baseDown;                 // 约定"正数往下"，换到 localPosition.y（上为正）要取负
        var lp = layer.localPosition;
        if (!Mathf.Approximately(lp.x, shift) || !Mathf.Approximately(lp.y, wantY)) {
            lp.x = shift;
            lp.y = wantY;
            layer.localPosition = lp;
            // 层一挪，每个图元在层里的位置就变了，投影结果得跟着重算
            for (int i = 0; views != null && i < views.Length; i++)
                if (views[i] != null) views[i].MarkDirty();
        }
        if (views == null) return;
        for (int i = 0; i < views.Length; i++) {
            if (views[i] != null) views[i].SetTilt(yaw, pitch);
        }
    }

    /// <summary>读数：时间与玩家名，每秒一次（与项目里其它页面同一个节流口径）。</summary>
    private static float _arkInfoNextAt;
    private static void TickArkInfo() {
        try {
            if (Time.unscaledTime < _arkInfoNextAt) return;
            _arkInfoNextAt = Time.unscaledTime + 1f;
            if (_arkDate != null) {
                _arkDate.text = System.DateTime.Now.ToString("yyyy/MM/dd  HH:mm");
            }
            // 主标题颜色跟随主题色：在设置页换完主题回大厅，最多一秒内换过来
            // （颜色没变不写 —— Text 赋值会触发网格重建，跟上面读数同一套判据）。
            if (_arkTitle != null) {
                var wantTitle = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f);
                if (_arkTitle.color != wantTitle) _arkTitle.color = wantTitle;
            }
            if (_arkUserName != null) {
                string want = string.IsNullOrEmpty(PlayerName) ? DefaultPlayerName : PlayerName;
                if (_arkUserName.text != want) _arkUserName.text = want;
            }
            // ---- 功能映射的动态读数：每秒对一次，值没变不写（Text 赋值会触发网格重建）----
            if (_arkNumP != null) {
                int p = ArkPguCount('P');
                if (_arkNumP.text != p.ToString("D4")) _arkNumP.text = p.ToString("D4");
            }
            if (_arkNumG != null) {
                int g = ArkPguCount('G');
                if (_arkNumG.text != g.ToString("D4")) _arkNumG.text = g.ToString("D4");
            }
            if (_arkNumU != null) {
                int u = ArkPguCount('U');
                if (_arkNumU.text != u.ToString("D4")) _arkNumU.text = u.ToString("D4");
            }
            if (_arkClearedNum != null) {
                int cleared = ArkClearedCount();
                if (_arkClearedNum.text != cleared.ToString()) _arkClearedNum.text = cleared.ToString();
            }
            if (_arkClearedTotal != null) {
                // 显示口径：数字单独一段（楷体），左边"谱面总数/"是另一段（界面默认字体）
                string want = ArkTotalCount().ToString();
                if (_arkClearedTotal.text != want) _arkClearedTotal.text = want;
                LayoutArkTotalLabel();   // 位数变了标签右缘要跟着摆
                LayoutArkClearedGroup();  // 通关数位数变了整组要重新居中
            }
            if (_arkRingArc != null) {
                try {
                    // 完成弧换成弧贴图（见 ArkRingArcSprite）：1% 步进，变了才烘
                    float fill = Achievements.LevelFill();
                    var sp = ArkRingArcSprite(fill);
                    if (sp != null && _arkRingArc.sprite != sp) _arkRingArc.sprite = sp;
                    // 完成弧颜色跟随主题色：在设置里换完主题回大厅，最多一秒内换过来
                    var wantArc = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f);
                    if (_arkRingArc.color != wantArc) _arkRingArc.color = wantArc;
                } catch { }
            }
            if (_arkSmallTitles != null) {
                try {
                    int lv, exp, floor, next;
                    Achievements.LevelProgress(out lv, out exp, out floor, out next);
                    if (_arkSmallTitles.text != lv.ToString()) _arkSmallTitles.text = lv.ToString();
                } catch { }
            }
            if (_arkDlgText != null) {
                // 对话框的正文按示意图走占位文案；接真实助手语音是后面的事
            }
        } catch { }
    }
}
