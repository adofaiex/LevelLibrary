using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：主页 tab（官方关卡棋盘）。
/// 结构：顶层 tab 主页 / 库 / 成就 / 设置 / 开发者；页面 = 左侧区域 tab 列 + 右侧关卡列表，点一条 → 列表往右缩让出详情区（几何与库页镜像），点空白处取消选中。
/// 详情区自上而下：子关卡按钮行 → 标题/元信息 → 存档数据（带遮罩）→「通关记录 / 寄录图」两个 tab → 底部「进入关卡」。
/// 所有矩形一律用 SetRectPx 那一族（按左上角锚定）—— MakeButton 是中心锚定的，混用会让按钮跑到面板外；输入走 UiInput，动画不用 DOTween。</summary>
public static partial class PortalUI {

    // ================= 状态 =================

    private static GameObject _homeRoot;
    private static RectTransform _homeViewportRt;     // 列表可视区（带 RectMask2D）
    private static RectTransform _homeContentRt;      // 列表内容（按行排卡片）
    private static GameObject _homeDetailGo;          // 详情区整块
    private static Text _homeTitleText, _homeMetaText;
    private static GameObject _homeClearArea, _homeHeatArea;
    private static Text _homeClearText, _homeHeatHint, _homeHeatAxis;
    private static readonly List<Image> _homeHeatBars = new List<Image>();
    private static readonly List<Image> _homeIconImgs = new List<Image>();          // 下标 = 世界下标
    private static readonly List<RectTransform> _homeCardRts = new List<RectTransform>();
    private static readonly List<CanvasGroup> _homeCardCgs = new List<CanvasGroup>();
    private static readonly List<Vector2> _homeCardGridPos = new List<Vector2>();   // 每张卡在列表里的位
    // ---- 行内右侧那一块统计（方案 B）----
    // 每行三样：完成度百分比文字 / 完成度条（底槽 + 填充）/ 死亡热点小柱（固定根数，降采样填）。
    // 下标一律 = 世界下标，跟 _homeCardRts 对齐；建卡时填、刷新时改尺寸与颜色。
    private static readonly List<Text> _homePctTexts = new List<Text>();
    private static readonly List<Image> _homeProgSlots = new List<Image>();      // 底槽（灰）
    private static readonly List<Image> _homeProgFills = new List<Image>();      // 填充（主题色，宽度 = 完成度）
    /// <summary>每行的完成度比例（0~1）。填充宽度按「当前条宽 × 这个比例」现算，
    /// 不保存像素宽 —— 行宽会在选中时收窄、取消后回宽，存像素宽就会停在收窄时的尺寸上。</summary>
    private static readonly List<float> _homeProgRatios = new List<float>();
    private static readonly List<Image[]> _homeRowBars = new List<Image[]>();     // 每行的小柱数组
    /// <summary>每行那 12 根柱各自的分桶死亡数（选中态要按它重画颜色：高度看不出「这一格到底有没有死过人」）</summary>
    private static readonly List<int[]> _homeRowCounts = new List<int[]>();
    private static readonly List<Button> _homeSubBtns = new List<Button>();
    private static readonly List<Text> _homeSubTexts = new List<Text>();
    private static readonly List<string> _homeSubIds = new List<string>();
    private static readonly List<Button> _homeTabBtns = new List<Button>();         // 详情区里那两个内容 tab
    private static readonly List<Text> _homeTabTexts = new List<Text>();
    private static Text _homeSaveText1, _homeSaveText2;   // 存档数据两栏（左边世界数据、右边子关卡数据）
    private static RectTransform _homeSubRow;

    // ---- 左侧区域 tab 列 ----
    private static GameObject _homeTabCol;                        // 整列（选中后整列淡出）
    private static readonly List<Button> _homeRegionBtns = new List<Button>();
    private static readonly List<Text> _homeRegionNames = new List<Text>();
    private static RectTransform _homeRegionFrameRt;              // 选中那块实心淡蓝（在 tab 之间滑）
    private static Image _homeTabFrame;                          // 详情区内容 tab 的滑动块
    private static Image _homeSubFrame;                          // 详情区子关卡行的滑动块
    private static int _homeRegion;                              // 当前区域 tab（0..HomeRegionCount-1）
    private static ScrollRect _homeScroll;
    private static GameObject _homeGridBar;                       // 列表滚动条（这一区放得下时藏起来）
    private static RectTransform _homeGridBackdropRt, _homeGridFrameRt;
    // 三块"滑动选中块"：跟库 tab 里那套一样要滑过去，不能直接跳
    private static readonly HomeSlide _homeRegionSlide = new HomeSlide();
    private static readonly HomeSlide _homeSubSlide = new HomeSlide();
    private static readonly HomeSlide _homeTabSlide = new HomeSlide();

    /// <summary>通用“滑动选中块”（跟库页详情 tab / 成就页左栏 / 设置页导航同一套做法）：
    /// 指数平滑 `1-exp(-14·dt)`，首次直接就位，快速连点会从当前位置接着走</summary>
    private sealed class HomeSlide {
        public RectTransform Rt;
        public Vector2 Cur, Target, CurSize, TargetSize;
        public bool Init;
        public void Set(Vector2 pos, Vector2 size) { Target = pos; TargetSize = size; }
        public void Snap() { Cur = Target; CurSize = TargetSize; Init = true; Apply(); }
        public void Apply() { if (Rt == null) return; Rt.anchoredPosition = Cur; Rt.sizeDelta = CurSize; }
        public void Tick() {
            if (Rt == null) return;
            if (!Init) { Snap(); return; }
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            Cur = Vector2.Lerp(Cur, Target, k);
            CurSize = Vector2.Lerp(CurSize, TargetSize, k);
            if ((Cur - Target).sqrMagnitude < 0.04f && (CurSize - TargetSize).sqrMagnitude < 0.04f) {
                Cur = Target; CurSize = TargetSize;
            }
            Apply();
        }
    }

    private static int _homeSelected = -1;     // 选中的世界下标（OfficialLevels.Worlds() 的下标）
    /// <summary>详情区开着时藏掉列表行里的寄录图小柱。它表达的是「这个世界的死亡热点」，
    /// 详情区已经有一张完整的寄录图，列表里再铺一排同色小柱只是噪音；取消选中时原样放回来。</summary>
    private static bool _homeRowHeatHidden;
    private static int _homeSubIndex;          // 详情区里选中的子关卡下标
    private static int _homeDetailTab;         // 0 = 通关记录 / 1 = 寄录图
    private static float _homeExpand = 1f;     // 1 = 列表铺满（没选）/ 0 = 详情展开、列表让出左半边
    private static float _homeExpandTarget = 1f;

    // ================= 主页布局几何（左 tab + 右列表）=================
    /// <summary>列表的上下边界：跟左右两块底板的 TopNavH..1040 对齐（设置页/成就页那块面板的高度）</summary>
    private const float HomeTop = TopNavH, HomeBottom = 1040f;
    /// <summary>没选中时列表的左边界：必须让开左栏（底板 100..400），照设置页分栏</summary>
    private const float HomeGridLeft = 420f;
    private const float HomeShrunkLeft = 899f;                  // 选中后列表区左边界（跟库页一致）
    /// <summary>右侧面板（列表容器背景遮罩 + 描边框 + 可视区 + 行）的右边界，三者共用这一个值。
    /// 早先这里是两套：`HomeFullRight = 1736` 给可视区、`HomeBackRight = 1858` 给背景与框线，
    /// 两者差 122 像素 —— 行宽又是从可视区宽度推的，于是卡片右缘永远停在离容器框线 122 像素的地方，
    /// 右边空一大截（实机截图标出来是 362 像素，因为还叠加了留白与滚动条）。
    /// 「面板」是一个视觉整体，三条边必须同宽：背景遮罩、描边框、可视区一律取这个值，
    /// 描边框照旧外扩 2 像素（见 ApplyHomeExpand）。</summary>
    private const float HomeBackRight = 1858f;
    /// <summary>可视区右边界。跟容器背景同值，卡片才能一路铺到面板右缘。
    /// 之前比 HomeBackRight 小 122，是个纯粹的遗留错位。</summary>
    private const float HomeFullRight = HomeBackRight;
    // 左侧区域 tab 列（照设置页那套）：
    // - 底板 140..(1080-HomeRegBackBottom) = 140..1040，跟右侧显示区顶 / 设置页 NavBackdrop 对齐
    // 底板顶必须用自己的常量 HomeRegBackTop，绝不能由按钮起点 HomeRegTop 推导 ——
    // 否则按钮一往下挪，底板顶就跟着掉，遮罩位置整块错位
    // - 按钮起点 = 底板顶再往下半个按键高
    private const float HomeRegBackX = 62f, HomeRegBackW = 300f;
    private const float HomeRegBackTop = TopNavH, HomeRegBackBottom = 40f;
    private const float HomeRegX = 102f, HomeRegW = 220f, HomeRegH = 52f, HomeRegGap = 12f;
    private const float HomeRegTop = HomeRegBackTop + HomeRegH * 0.5f;      // = 166
    // 右侧卡片：卡面 240×154，5 列、行距 24
    private const float HomeIconW = 240f, HomeIconH = 154f;
    private const float HomeCardGapY = 24f;
    private const float HomeGridBarW = 14f;
    /// <summary>列表一律从显示区左上角起排，不要居中（只有三个关卡的区会被推到中间）</summary>
    private const float HomeGridPadX = 24f, HomeGridPadY = 28f;
    // ===== 右侧从“卡片网格”换成“列表” =====
    // 卡片对象本身留着（背景/描边/图标/编号/名字都是现成的），只改排布：5 列网格 → 单列列表
    private const float HomeListRowH = 76f;        // 一行高
    private const float HomeListGapY = 8f;         // 行间距（比网格的 24 紧，列表要“一条一条”的读感）
    private const float HomeListPadL = 16f;        // 行距可视区左缘
    /// <summary>行距可视区右缘的留白。跟左缘同值，行才能在容器里左右对称。
    /// 行宽计算统一走 HomeRowAvailW：早先一处扣 HomeGridPadX(24) 一处扣字面量 16，
    /// 两个数各写各的又都不对应容器的遮罩，合计让卡片右缘离容器框线空出 154 像素。</summary>
    private const float HomeListPadR = 16f;
    private const float HomeListIcon = 48f;        // 行内图标边长
    private const float HomeListNumX = 80f;        // 编号那一栏的 x
    private const float HomeListNumW = 150f;       // 编号栏宽
    private const float HomeListNameX = 236f;      // 名字起始 x
    /// <summary>名字栏必须保有的宽度。统计块从这个名字栏的右边开始摆，
    /// 所以这个名字就是「统计块的左边界」—— 算统计块宽度时要从行宽里减掉它，而不是减名字栏的起点。
    /// 踩过的坑：早先公式写的是 `rowW − HomeListNameX − HomeStatsGap`，
    /// 只减了起点 236、没减名字实际占的宽度，于是统计块左缘落在距行左 240 处，
    /// 而名字栏从 236 开始就占 236..356 —— 统计块整块压在名字文字上，
    /// 实机截图里进度条直接从「凛雪之舞」身上碾过去（条左端 760、名字约 690..768）。
    /// 240 与 236 只差 4 像素，肉眼看代码极难发现，必须把两个边界都算出来才看得见。</summary>
    private const float HomeNameMinW = 220f;
    private const float HomeListNominalW = 1000f;  // 建卡时先按这个名义宽度摆（真实宽度要等 Layout 时才知道）
    // ===== 行内右侧那一块统计（方案 B：徽章 / 完成度 / 死亡热点）=====
    // 整块挂在行的右缘（锚点 (1,0.5)），不跟左边的编号/名字抢位置：
    // 行宽会随选中态在 783↔1262 之间变（列表区左边界 420↔899），右锚的东西一次摆好就不用再动，
    // 只有名字那一栏要按剩余宽度收窄（见 LayoutHomeRowMeta）。
    // 统计块宽度跟着行宽走（HomeStatsW 由 RecalcHomeStatsGeometry 按真实行宽重算），
    // 不用写死：写死 310 的时候，行宽一千四百像素而统计块只占 310，
    // 右边空出一大截（实机截图里进度条右侧全是空的，正是用户拿红箭头指的地方）。
    // 完成度条是唯一能吃掉多余空间的元素 —— 它本来就是比例尺，越长越读得准；
    // 百分比文字和小柱阵列宽度固定，留在块的右端不动。
    private const float HomeStatsPadR = 16f;       // 统计块距行右缘
    /// <summary>统计块的最小总宽。行宽特别窄（选中态 783）时按这个兜底，
    /// 低于它就先让完成度条收窄，再窄才允许整块按比例缩。
    /// 推导式比写死 310 好：改小柱宽度时最低宽度自动跟着变。</summary>
    private const float HomeStatsWMin = HomeStatHeatSpan + HomeStatSegGap + HomeStatPctW + HomeStatSegGap + 64f;
    // ---- 死亡热点小柱的几何（先摆它，因为完成度条宽度是拿它推出来的）----
    // 固定 12 根（100 格死亡分布降采样到 12 根），4 宽 3 缝。阵列总宽由
    // 「柱宽 × 根数 + 缝 ×(根数−1)」推出来，起点再由总宽推出来：
    // 这样「12 根」和「右缘顶到统计块边界」两个约束不会各写一个魔数，改柱宽时自动重排，
    // 也不会像早先那版那样改完缝宽就发现总宽悄悄超出窗口、从右缘溢出去。
    private const int HomeStatHeatBars = 12;
    private const float HomeStatHeatBarW = 4f, HomeStatHeatGap = 3f;
    private const float HomeStatHeatSpan = HomeStatHeatBarW * HomeStatHeatBars + HomeStatHeatGap * (HomeStatHeatBars - 1);
    private const float HomeStatHeatH = 32f;       // 最高那根的高度
    /// <summary>小柱的横向步距（柱宽 + 缝）</summary>
    private const float HomeStatHeatPitch = HomeStatHeatBarW + HomeStatHeatGap;
    /// <summary>小柱基线：最高那根的底边距行中线往下多少像素。取柱高的一半 = 整组上下居中于行
    /// （行高 76、柱高 32，底边落在中线下方 16 像素处）</summary>
    private const float HomeStatHeatBaseline = HomeStatHeatH * 0.5f;
    /// <summary>小柱的圆角半径（柱宽 4，取 1 留 2 像素直段；取 2 就顶到临界、圆头会被压扁）</summary>
    private const float HomeStatHeatBarR = 1f;
    /// <summary>小柱的最小高度。小柱是双向的，两条边都得大于 2×半径+1：
    /// 宽度方向 4 已经大于 3（成立），高度方向就要靠这个下限兜着。
    /// 高度一旦 ≤ 2×border，9-slice 的上下 border 就叠上了，uGUI 只能按比例压扁、圆头挤成畸形的疙瘩。
    /// 「这一段没有死亡」用这么高的短粒表示：比 0 高度看得见，又不占地方。</summary>
    private const float HomeStatHeatBarMinH = 2f * HomeStatHeatBarR + 2f;
    // ---- 三段排布：百分比 | 完成度条 | 死亡热点 ----
    private const float HomeStatPctW = 60f;        // 百分比文字
    /// <summary>三段（百分比 / 完成度条 / 死亡热点）之间的缝。三段用同一个值，
    /// 整块看起来才是同一套呼吸节奏 —— 早先写死「百分比 8、热点 21」两个魔数时，一眼就看出排歪了。
    /// 完成度条宽度是唯一被推导出来的那一段：它是显示比例尺，短几像素不影响读数，
    /// 换来的是改缝宽 / 改小柱宽度时整块自动重排，不必再回头核一遍会不会撞上或溢出。</summary>
    private const float HomeStatSegGap = 16f;
    /// <summary>完成度条的条高。这是第二轮返工的教训：条高决定圆角能被看见多少。
    /// 取 16 而不是更早的 10 —— 10 像素高的条上画 4 像素圆角，上下各 4 像素全是圆弧区、
    /// 直段只剩 2 像素，实机逐像素扫下来两端确实有渐变，但人眼在正常观看距离下
    /// 读出来就是「没有圆角」。判据不是「半径够不够大」，而是「直段留得够不够」：
    /// 直段至少 4 像素才看得出是胶囊而不是圆头。条高 = 2 × 半径 + 直段，16 = 2×6 + 4。</summary>
    private const float HomeStatProgH = 16f;
    /// <summary>完成度条的圆角半径。取 6 —— 正好是条高 16 的一半，整条是一颗饱满的胶囊，
    /// 两端各留 4 像素直段。不再取更大：半径一旦超过条高的一半，
    /// 上下的圆弧区就会在中间那段重叠，uGUI 切片算出来的直段变成 0，圆角会糊成一团。
    /// 底槽与「已完成色」这一次用同一张圆角贴图、同样走九宫格：底槽铺满固定宽度的条身，
    /// 已完成色盖在它身上、宽度按完成度走。两者同形同高，分界处因此是一个圆头，
    /// 而不是切一条直线的色块 —— 早先那版就是直线，被指出「只有底槽有圆角、内部是方的」。
    /// 九宫格的约束落在最窄的那一端：填充宽度 = 完成度 × 条宽，完成度很小时只有几像素，
    /// 两条 border 叠上就会把两端的圆角压掉、退回直角，所以填充另有一个最小宽度。
    /// 凡是套圆角 sliced 的元素，显示宽高都必须严格大于两倍 border 加 1，
    /// 这条约束在下面每个半径常量处都成立。半径一律取整数：算出 4.5 这类小数时
    /// 临界判定会变成「等于」而不成立，白白踩线。</summary>
    private const float HomeStatProgR = 6f;
    /// <summary>「已完成色」的最小宽度。跟条身共用同一张圆角贴图、同样走九宫格，
    /// 所以下限必须大于两倍半径（这里取两倍半径加二，跟小柱最小高度同一个推导口径）：
    /// 宽度一旦掉到两倍半径以下，两条 border 就叠在一起，右端的圆头会被压平。
    /// 代价是完成度不足这把宽度时色条仍按这个宽度显示 —— 精确读数由旁边那串百分比文字给。</summary>
    private const float HomeStatProgFillMinW = 2f * HomeStatProgR + 2f;

    // ---- 统计块的横向几何（运行时，按真实行宽算）----
    // 三段从左到右：百分比 | 完成度条 | 死亡热点。百分比和小柱宽度是固定的，
    // 完成度条吃下所有多余宽度 —— 它本来就是比例尺，越长读数越准。
    // 行宽一千四百像素时条能有四百多像素，行宽一千零几像素（选中态）时自然收窄，
    // 一套公式覆盖两种状态，不用为「宽 / 窄」各写一套几何。
    //   HomeStatsW    统计块总宽 = 行宽 − 名字栏与统计块之间的缝
    //   HomeStatProgW 完成度条宽   = 总宽 − 百分比 − 小柱 − 两道缝
    private static float HomeStatsW = 310f;
    private static float HomeStatProgW = 137f;
    private static float HomeStatHeatX = 229f;      // 小柱阵列起点（统计块局部 x）
    private static float HomeStatProgX = 76f;       // 完成度条起点（统计块局部 x）= 百分比 + 一道缝
    /// <summary>按当前行宽重算统计块的三段横向几何。行宽一变就要调（见 ApplyHomeRowWidth）。
    /// 收窄到极限也保证完成度条至少有 HomeStatProgWMin 像素，不让条被压没。
    /// 顺序反过来过：先把百分比和小柱从总宽里扣掉，剩下的全部给条，不再设上限。
    /// 早先给条设了 420 的上限，结果宽行时多出来的部分变成条与小柱之间一大片空洞
    /// （离线渲染里量到 567 像素），布局看着像断了一截 —— 那才是「右边空着」的另一种形态。
    /// 比例尺本来就该铺满可用宽度，读数也更准。
    /// 统计块总宽 = 行宽 − 右留白 − 两块之间的缝 − 名字栏起点 − 名字栏最小宽。
    /// 最后两项合起来就是「统计块左边界必须让给名字栏的那一段」。只减起点不减宽度，
    /// 统计块就会压到名字文字上（实测条左端 760、名字 690..768，整条盖在名字上）。</summary>
    private static void RecalcHomeStatsGeometry(float rowW) {
        float statsW = Mathf.Max(HomeStatsWMin,
            rowW - HomeStatsPadR - HomeStatsGap - HomeListNameX - HomeNameMinW);
        float progW = statsW - HomeStatPctW - HomeStatHeatSpan - HomeStatSegGap * 2f;
        if (progW < HomeStatProgWMin) progW = HomeStatProgWMin;      // 再窄就保底，块可以略微超出行宽
        HomeStatsW = statsW;
        HomeStatProgW = progW;
        HomeStatProgX = HomeStatPctW + HomeStatSegGap;
        HomeStatHeatX = statsW - HomeStatHeatSpan;
    }
    /// <summary>完成度条宽度的下限。低于它进度就读不出来了（只剩几个像素的色块）。</summary>
    private const float HomeStatProgWMin = 48f;

    private const float HomeBadgeR = 14f;          // 徽章圆角（48 见方，14 刚好是"圆角方块"不是圆）
    private const float HomeBadgeFont = 24f, HomeBadgeFontMin = 13f;
    private const float HomeStatFont = 19f;        // 百分比文字字号
    /// <summary>行内统计块左缘到名字栏之间的缝</summary>
    private const float HomeStatsGap = 20f;
    /// <summary>行内统计的刷新间隔（秒）。完成度和死亡数只有打完一局才会变，1 秒足够；
    /// 每帧刷 41 行 × 14 个图元纯属浪费（还在算 100 格降采样）</summary>
    private const float HomeStatRefreshInterval = 1f;
    private static float _homeStatRefreshTimer;
    // 卡内排版（一整族常量互相推导，改一个另一个跟着变）
    private const float HomeCardPad = 16f;                      // 卡内边距
    private const float HomeCardTextX = 78f;                    // 编号列的左起点（= 边距 + 方图 + 4）
    private const float HomeCardNameY = 80f;                    // 名字区上边（在方图下方）
    private const float HomeCardIdFont = 34f;                   // 编号字号
    private const float HomeCardNameFont = 22f;                 // 名字字号（bestFit 下限 17）
    private const float HomeCardNameFontMin = 17f;
    /// <summary>展开/收起时长。走全站那一档“换页”动效，不再单开一个 0.22 ——
    /// 面板里凡是「整块进 / 整块出」都用 MoPage，列表让位属于同一件事</summary>
    private const float HomeExpandDur = MoPage;

    // ================= 详情区几何 =================
    // 纵向排布（局部坐标，左上角原点，总高 900 —— 跟左右两块底板/右侧显示区同一个 140..1040：
    // 标题 24..70 ｜ 元信息 74..102 ｜ 子关卡小标题 110..132 ｜ 子关卡按钮 134..174
    // 存档遮罩 186..382 ｜ 内容 tab 394..434 ｜ 内容遮罩 446..826（「进入关卡」搬进框内：760..816））
    private const float HD_Left = 62f, HD_Top = HomeTop, HD_W = 806f, HD_Bottom = HomeBottom;
    private const float HD_TitleY = 24f, HD_TitleH = 46f;
    private const float HD_MetaY = 74f, HD_MetaH = 28f;
    private const float HD_SubCapY = 110f, HD_SubCapH = 22f;
    private const float HD_SubY = 134f, HD_SubH = 40f;
    private const float HD_SaveY = 186f, HD_SaveH = 196f;
    private const float HD_TabY = 394f, HD_TabH = 40f;
    private const float HD_BodyY = 446f, HD_BodyH = 380f;
    /// <summary>详情区“内容块”距详情区底的边距 = 900 − 826 = 74。底下那条空带现在只放得下按钮的
    /// 下沿（按钮搬进框内：距底 84 加高 56，顶端在 140），所以内容块自身照旧拉到 74 处。
    /// 别再写死内容块高度 —— 画布一高它就该长，用拉伸锚点</summary>
    private const float HD_BodyMargin = 74f;
    /// <summary>「进入关卡」距详情区底多少：84 让它整个落在内容块（下沿 74）里面，不再吊在框下面</summary>
    private const float HD_EnterBottom = 84f;
    private const float HD_EnterY = 760f, HD_EnterH = 56f;
    /// <summary>内容区（通关记录 / 热力图）距详情区底的边距：按钮顶端在 140，再留 12 的缝就是 152。
    /// 两个内容区的高度都按它拉伸（原来跟着 HD_BodyMargin − 14 走，按钮搬上来后会被压住）</summary>
    private const float HD_ContentBottom = 152f;
    /// <summary>「进入关卡」按钮宽度（不是整块面板宽 806，左右要短一截）</summary>
    private const float HD_EnterW = 320f;
    private const float HD_PadX = 20f;

    // 颜色：主页这一页统一走“半透明黑”，选中态不靠卡面变色，而是主题色描边 + 行底色（跟设置页/成就页一致）
    /// <summary>主页里所有“底板 / 遮罩”统一的半透明黑：详情区底板、存档遮罩、内容遮罩、右侧列表区背景遮罩、以及卡片卡面。
    /// 声明顺序有意义：下面两个卡面色引用它，必须写在它后面 —— 静态字段按书写顺序初始化，
    /// 写在前面会拿到全零的 default(Color)（= 全透明黑）</summary>
    private static readonly Color HomeBackdrop = new Color(0f, 0f, 0f, 0.45f);
    /// <summary>卡片（未选中）的卡面 = 半透明黑，跟上面那块遮罩同一个颜色。
    /// 卡片叠在列表区背景遮罩上面 → 两层 0.45 黑叠起来比周围再深一档，卡面依然读得出来（再加那圈主题色描边）</summary>
    private static readonly Color HomeIconIdle = HomeBackdrop;
    /// <summary>选中态不再用“其余卡片淡出 + 只留这一张居中”那套，改成跟谱子库列表一样：选中行直接用 `Accent`（见 `SelectHomeWorld`），
    /// 所以这里只剩“未选中”一个色；`HomeIconSel` 保留，给“选中色 = Accent”的旧引用兜底</summary>

    /// <summary>主页三块大遮罩（左栏底板 / 右侧列表区 / 详情区底板）的圆角半径，比全站默认的 `RoundedSprite`（半径 10）大一倍。
    /// 描边那圈必须用同一个半径（`MakeMaskFrame(..., HomeMaskRadius)`）：这两族 sprite 的 9-slice border 就是半径本身，
    /// 半径不匹配 → 四个角的弧度对不上、描边会切进遮罩里。
    /// 里面那两块小遮罩（存档 / 内容）不跟着改：外圈 22 + 内圈 10 是刻意的层级差</summary>
    private const float HomeMaskRadius = 22f;
    private static Sprite _homeRoundSprite;
    private static Sprite HomeRoundSprite {
        get {
            // 贴图 64 / 边距 = 半径（跟 CreateRoundedFrameSprite 那张大描边同尺寸同半径）
            if (_homeRoundSprite == null) _homeRoundSprite = CreateRoundedSprite(64, HomeMaskRadius);
            return _homeRoundSprite;
        }
    }

    // ================= 建页 =================

    /// <summary>建主页（切到主页 tab 时建一次；整页挂在 PageSlot(0) 上）</summary>
    private static void BuildHomePage() {
        if (_homeRoot != null) return;
        var slot = PageSlot(0);
        if (slot == null) { Lg.Info("主页：PageSlot(0) 拿不到，放弃"); return; }

        var root = new GameObject("HomePage", typeof(RectTransform));
        root.transform.SetParent(slot, false);
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        _homeRoot = root;

        // ---- 左侧区域 tab 列 + 右侧列表 ----
        BuildHomeRegionColumn(root.transform);
        BuildHomeGridBackdrop(root.transform);
        BuildHomeGrid(root.transform);

        // 41 张卡一次建好（切区域只改显隐），下标 = Worlds() 的下标。
        // 建卡之前先把统计块几何按当前可视区宽度算好：统计块是右锚到行的，
        // 建卡那一刻就把三段的绝对位置写进 RectTransform 了，事后再改宽度不会自动重排。
        if (_homeViewportRt != null) RecalcHomeStatsGeometry(HomeRowAvailW(_homeViewportRt.rect.width));
        var worlds = OfficialLevels.Worlds();
        int n = worlds.Count;
        for (int i = 0; i < n; i++) MakeHomeCard(_homeContentRt, worlds[i], i);

        BuildHomeDetail(root.transform);
        _homeRegion = 0;
        SelectHomeRegion(0, true);
        RefreshHomeRowStats();          // 行内统计（完成度 / 死亡热点）建完行才有意义，刷新一次
        ApplyHomeExpand(_homeExpand, true);
        Lg.Info($"[LevelLib] 主页建好了：{HomeRegionCount} 个区域 tab + {n} 张卡片（成就页式布局：左 tab / 右卡片网格）");
    }

    // ================= 左区域 tab 列 + 右列表 =================

    /// <summary>区域口径：基础关卡放 1-6、进阶关卡放 7-12 与 B、皇冠岛 / Muse Dash / CR2024 各占一个，其余归“愚人节＆彩蛋”。
    /// 没被点名的区域（Xtra / 1J~6J / Vega）也必须各自有归属，不然那批谱子点不到</summary>
    private static readonly string[] HomeRegionNames = { "基础关卡", "进阶关卡", "附加关卡", "皇冠岛", "Muse Dash", "CR2024", "愚人节＆彩蛋" };
    private const int HomeRegionCount = 7;

    /// <summary>世界 key → 区域下标：Main 的 1~6 = 基础、7~12 与 B = 进阶；Xtra/Crown/MuseDash/CR2024 各占一个；
    /// 剩下的（1J~6J / BJ、Vega 的 1EX）= 其他</summary>
    private static int HomeRegionOf(OfficialLevels.WorldInfo w) {
        if (w == null) return HomeRegionCount - 1;
        // TP 也归“其他”：XP 的 island 写的是 Crown，不先拦一下会掉进「皇冠岛」（XP / TN 已作为废关剔除）
        if (w.Key == "TP") return HomeRegionCount - 1;
        switch (w.Island) {
            case "Xtra": return 2;
            case "Crown": return 3;
            case "MuseDash": return 4;
            case "CR2024": return 5;
        }
        if (w.Key == "B") return 1;
        int num;
        if (int.TryParse(w.Key, out num)) {
            if (num >= 1 && num <= 6) return 0;
            if (num >= 7 && num <= 12) return 1;
        }
        return 6;
    }

    /// <summary>建左栏：一列透明按钮（选中由那块实心淡蓝表示，跟成就页同款）</summary>
    private static void BuildHomeRegionColumn(Transform parent) {
        var col = new GameObject("HomeTabCol", typeof(RectTransform));
        col.transform.SetParent(parent, false);
        Stretch(col.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        _homeTabCol = col;
        col.AddComponent<CanvasGroup>();

        // 左栏底板 140..(画布底 - HomeRegBackBottom)，照设置页那套。
        // 高度只能用 HomeRegBackTop 自己算；底边用拉伸锚点而不是写死 1080，画布一高底板才跟着到底
        var bg = MakeImage(col.transform, "HomeTabBackdrop", HomeBackdrop);
        RegisterAlphaBackdrop(bg);   // 跟着遮罩那一档固定系数走
        RegisterGlassPanel(bg);      // 官谱库左侧子 tab 区遮罩 要磨砂
        bg.sprite = HomeRoundSprite; bg.type = Image.Type.Sliced; bg.raycastTarget = false;
        SetRectPxV(bg.rectTransform, HomeRegBackX, HomeRegBackTop, HomeRegBackW, HomeRegBackBottom);
        MakeMaskFrame(col.transform, "HomeTabFrame", HomeRegBackX, HomeRegBackTop, HomeRegBackW,
                      (1080f - HomeRegBackBottom) - HomeRegBackTop, HomeMaskRadius);
        AnchorMaskFrameV(col.transform, "HomeTabFrame", HomeRegBackX, HomeRegBackTop, HomeRegBackW, HomeRegBackBottom);

        // 先建选中块、再建按钮：实心块必须在按钮下面，否则会把文字整片盖住
        var frame = MakeImage(col.transform, "HomeRegionFrame", HighlightFillColor);
        RegisterFillImage(frame);   // 换主题色时要重刷它
        frame.raycastTarget = false;
        frame.sprite = RoundedSprite;
        frame.type = Image.Type.Sliced;
        _homeRegionFrameRt = frame.rectTransform;
        _homeRegionFrameRt.anchorMin = _homeRegionFrameRt.anchorMax = new Vector2(0f, 1f);
        _homeRegionFrameRt.pivot = new Vector2(0f, 1f);
        _homeRegionSlide.Rt = _homeRegionFrameRt;          // 交给“滑动块”统一驱动

        for (int i = 0; i < HomeRegionCount; i++) {
            int idx = i;
            var btn = HomeTabButton(col.transform, "HomeRegion" + i, HomeRegionNames[i],
                                    HomeRegTop + i * (HomeRegH + HomeRegGap));
            btn.onClick.AddListener(() => SelectHomeRegion(idx));
            _homeRegionBtns.Add(btn);
            _homeRegionNames.Add(btn.GetComponentInChildren<Text>());
        }
    }

    /// <summary>左栏一个区域按钮（无底色 + 居中文字；选中态只由那块实心块表示）</summary>
    private static Button HomeTabButton(Transform parent, string name, string label, float top) {
        var btn = MakeButton(parent, name, Vector2.zero, new Vector2(HomeRegW, HomeRegH), new Color(1f, 1f, 1f, 0f));
        var img = btn.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.type = Image.Type.Simple; img.raycastTarget = true; }
        btn.transition = Selectable.Transition.None;      // 不要 hover 变色
        SetRectPx(btn.GetComponent<RectTransform>(), HomeRegX, top, HomeRegW, HomeRegH);
        var nm = MakeText(btn.transform, "N", 30, Color.white, TextAnchor.MiddleCenter);
        Stretch(nm.rectTransform, Vector2.zero, Vector2.zero);
        nm.text = label;
        nm.raycastTarget = false;
        btn.gameObject.AddComponent<CanvasGroup>();       // 整列淡出时按钮也能跟着淡（Raycast 由列的 CG 管）
        return btn;
    }

    /// <summary>右侧显示区的背景遮罩（跟左栏底板同一套视觉：平坦 0.45 黑 + 主题色描边）：
    /// 左边界跟着动画走（没选中 420 / 选中 899），右边界到 `HomeBackRight`（比列表区再宽一截），在列表下面一层</summary>
    private static void BuildHomeGridBackdrop(Transform parent) {
        var bg = MakeImage(parent, "HomeGridBackdrop", HomeBackdrop);
        RegisterAlphaBackdrop(bg);
        RegisterGlassPanel(bg);      // 官谱库右侧主区遮罩 要磨砂
        bg.sprite = HomeRoundSprite; bg.type = Image.Type.Sliced; bg.raycastTarget = false;
        // 底板 / 描边框改成“距画布底 BandBottomMargin”的拉伸锚点（1080 上还是 140..1040，画布 1200 时自己长到 1160）
        SetRectPxV(bg.rectTransform, HomeGridLeft, HomeTop, HomeBackRight - HomeGridLeft, BandBottomMargin);
        _homeGridBackdropRt = bg.rectTransform;
        MakeMaskFrame(parent, "HomeGridFrame", HomeGridLeft, HomeTop, HomeBackRight - HomeGridLeft, HomeBottom - HomeTop, HomeMaskRadius);
        var frmGo = parent.Find("HomeGridFrame");
        _homeGridFrameRt = frmGo != null ? frmGo.GetComponent<RectTransform>() : null;
        // 描边框是“外扩 2px”画的，所以它的底边距也跟着少 2（水平/上边照旧）
        if (_homeGridFrameRt != null)
            SetRectPxV(_homeGridFrameRt, HomeGridLeft - 2f, HomeTop - 2f, (HomeBackRight - HomeGridLeft) + 4f, BandBottomMargin - 2f);
    }

    /// <summary>建右侧列表：可视区（RectMask2D + 点空白取消选中）+ 可滚内容 + 滚动条</summary>
    private static void BuildHomeGrid(Transform parent) {
        var view = MakeImage(parent, "HomeGrid", new Color(0f, 0f, 0f, 0f));
        view.sprite = null; view.type = Image.Type.Simple; view.raycastTarget = true;
        view.gameObject.AddComponent<RectMask2D>();
        _homeViewportRt = view.rectTransform;
        SetRectPxV(_homeViewportRt, HomeGridLeft, HomeTop, HomeFullRight - HomeGridLeft, BandBottomMargin);

        // 点空白处 = 取消选中（卡片自己是 Button，会先吃掉点击）
        var blank = view.gameObject.AddComponent<Button>();
        blank.transition = Selectable.Transition.None;
        blank.targetGraphic = view;
        blank.onClick.AddListener(() => DeselectHomeWorld());

        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 64f;                   // 跟成就页/库页一致
        var content = new GameObject("HomeGridContent", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        _homeContentRt = content.GetComponent<RectTransform>();
        _homeContentRt.anchorMin = new Vector2(0f, 1f);
        _homeContentRt.anchorMax = new Vector2(1f, 1f);
        _homeContentRt.pivot = new Vector2(0.5f, 1f);
        _homeContentRt.anchoredPosition = Vector2.zero;
        scroll.viewport = _homeViewportRt;
        scroll.content = _homeContentRt;
        _homeScroll = scroll;

        // 滚动条（跟成就页同款；这一区放得下时整条藏起来）
        var sbGo = new GameObject("HomeGridBar", typeof(RectTransform));
        sbGo.transform.SetParent(parent, false);
        SetRectPxV(sbGo.GetComponent<RectTransform>(), HomeFullRight - HomeGridBarW, HomeTop,
                   HomeGridBarW, BandBottomMargin);
        var track = MakeImage(sbGo.transform, "Track", new Color(0.2f, 0.17f, 0.3f, 0.5f));
        track.sprite = SmallRoundSprite; track.type = Image.Type.Sliced; track.raycastTarget = false;
        Stretch(track.rectTransform, Vector2.zero, Vector2.zero);
        var bar = sbGo.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        var sliding = new GameObject("Sliding", typeof(RectTransform));
        sliding.transform.SetParent(sbGo.transform, false);
        var slrt = sliding.GetComponent<RectTransform>();
        slrt.anchorMin = Vector2.zero; slrt.anchorMax = Vector2.one;
        slrt.offsetMin = Vector2.zero; slrt.offsetMax = Vector2.zero;
        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(sliding.transform, false);
        var hrt = handle.GetComponent<RectTransform>();
        hrt.sizeDelta = Vector2.zero;
        var hImg = handle.AddComponent<Image>();
        hImg.color = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f);
        // 手柄颜色跟着主题色（每次刷新都重设，不用登记）
        hImg.sprite = SmallRoundSprite; hImg.type = Image.Type.Sliced;
        bar.handleRect = hrt; bar.targetGraphic = hImg;
        scroll.verticalScrollbar = bar;
        // AutoHide：这一区放得下时滚动条自己藏起来（Permanent 会一直挂着一条）
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        _homeGridBar = sbGo;
    }

    /// <summary>切区域：该区域的卡片按单列排好、其余整张隐藏；选中块滑到对应行</summary>
    private static void SelectHomeRegion(int region, bool instant = false) {
        if (region < 0 || region >= HomeRegionCount) return;
        if (instant || _homeRegion != region) DeselectHomeWorld();
        _homeRegion = region;
        if (_homeRegionFrameRt != null) {
            float top = HomeRegTop + region * (HomeRegH + HomeRegGap);
            // 只改“目标”，由 TickHome 里那块滑动块平滑滑过去
            _homeRegionSlide.Set(new Vector2(HomeRegX - 4f, -(top + 4f)), new Vector2(HomeRegW + 8f, HomeRegH - 8f));
            if (instant) _homeRegionSlide.Snap();
        }
        LayoutHomeGrid();
    }

    /// <summary>按当前区域排卡片（单列、从显示区左上角起排；内容高度按行数算）</summary>
    private static void LayoutHomeGrid() {
        try {
            if (_homeContentRt == null || _homeViewportRt == null) return;
            var worlds = OfficialLevels.Worlds();
            float vw = _homeViewportRt.rect.width, vh = _homeViewportRt.rect.height;
            var inRegion = new bool[worlds.Count];
            int count = 0;
            for (int i = 0; i < worlds.Count; i++) {
                if (HomeRegionOf(worlds[i]) == _homeRegion) { inRegion[i] = true; count++; }
            }
            // 单列列表：一行一个世界
            int cols = 1;
            int rows = Mathf.Max(1, count);
            // 行宽跟可视区走（建卡时只知道名义宽度），这里修一次。
            // 滚动条只在真显示时才占位，六个谱面撑不满一屏时不再白扣那 14 像素（见 HomeRowAvailW）
            float availW = HomeRowAvailW(vw);
            // 统计块几何跟着行宽重算（完成度条要铺到行右缘）。这里也调一次：
            // LayoutHomeGrid 自己设行宽、不经过 ApplyHomeRowWidth，漏了它条就还停在上一次的宽度。
            RecalcHomeStatsGeometry(availW);
            float pitchX = availW, pitchY = HomeListRowH + HomeListGapY;
            float gridW = availW;
            // 一律从显示区左上角起排，不要“居中”（只有三张卡的区会被推到中间）
            float left0 = HomeGridPadX;
            float contentH = Mathf.Max(vh, rows * pitchY - HomeCardGapY + HomeGridPadY + 28f);
            _homeContentRt.sizeDelta = new Vector2(0f, contentH);
            int slot = 0;
            if (_homeCardGridPos.Count < worlds.Count) {
                _homeCardGridPos.Clear();
                for (int i = 0; i < worlds.Count; i++) _homeCardGridPos.Add(Vector2.zero);
            }
            for (int i = 0; i < _homeCardRts.Count; i++) {
                var rt = _homeCardRts[i];
                if (rt == null) continue;
                bool on = i < worlds.Count && inRegion[i];
                if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
                if (!on) continue;
                int col = slot % cols, row = slot / cols;
                slot++;
                // 行宽跟可视区走（建卡时只知道名义宽度），这里修一次
                if (!Mathf.Approximately(rt.sizeDelta.x, availW) || !Mathf.Approximately(rt.sizeDelta.y, HomeListRowH)) {
                    rt.sizeDelta = new Vector2(availW, HomeListRowH);
                    var rimRt = rt.Find("Rim") as RectTransform;
                    if (rimRt != null) SetRectPx(rimRt, -2f, -2f, availW + 4f, HomeListRowH + 4f);
                }
                // 名字栏要按「行宽 − 右缘统计块」收窄（统计块右锚着、自己会跟）
                LayoutHomeRowMeta(rt, availW);
                // 左上角锚定（anchor/pivot = (0,1)）→ y 必须取负才是“往下排”，写成 +y 整排会跑到可视区上方去
                var p = new Vector2(left0 + col * pitchX, -(HomeGridPadY + row * pitchY));
                _homeCardGridPos[i] = p;
                rt.anchoredPosition = p;
            }
            if (_homeScroll != null) _homeScroll.verticalNormalizedPosition = 1f;
            if (_homeGridBar != null) _homeGridBar.SetActive(contentH > vh + 1f);
        } catch { }
    }

    /// <summary>一张世界卡片：圆角矩形底 + 岛屿徽章 + 编号 + 名字 + 右缘那组统计。点它 = 选中这个世界</summary>
    private static void MakeHomeCard(RectTransform parent, OfficialLevels.WorldInfo w, int idx) {
        var img = MakeImage(parent, "Card" + idx, HomeIconIdle);
        img.sprite = RoundedSprite;
        img.type = Image.Type.Sliced;
        img.raycastTarget = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);      // 列表按左上角锚定排
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(HomeIconW, HomeIconH);
        var cg = img.gameObject.AddComponent<CanvasGroup>();
        var btn = img.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        int captured = idx;
        btn.onClick.AddListener(() => SelectHomeWorld(captured));
        _homeIconImgs.Add(img);
        _homeCardRts.Add(rt);
        _homeCardCgs.Add(cg);
        _homeCardGridPos.Add(Vector2.zero);

        // 主题色描边（跟设置页遮罩那圈同一个 sprite，让卡片有“卡面”感）
        var rim = MakeImage(img.transform, "Rim", Accent);
        rim.sprite = EnsureMaskFrameSprite();
        rim.type = Image.Type.Sliced;
        rim.raycastTarget = false;
        // 描边跟着行的大小走（建时按名义宽度，Layout 里按真实宽度修）
        SetRectPx(rim.rectTransform, -2f, -2f, HomeListNominalW + 4f, HomeListRowH + 4f);

        // 徽章：按岛屿配色现画的小圆角方块 + 世界序号（原来 41 张卡共用同一张成就图标，一眼看不出谁是谁）
        var pic = MakeImage(img.transform, "IconImage", Color.white);
        pic.raycastTarget = false;
        var badge = HomeBadgeColorOf(w);
        pic.sprite = HomeBadgeSprite;                          // 圆角半径 HomeBadgeR 的方块，48 见方
        pic.type = Image.Type.Sliced;
        pic.color = new Color(badge.r, badge.g, badge.b, 0.22f);   // 底：淡一档的主题/岛屿色
        // 图标：行内左侧、竖直居中
        SetRectPx(pic.rectTransform, HomeListPadL, (HomeListRowH - HomeListIcon) * 0.5f, HomeListIcon, HomeListIcon);

        // 序号压在徽章正中：比徽章底色亮一档，深底浅字 / 浅底深字由 RefreshHomeBadgeText 决定
        var bnum = MakeText(pic.transform, "BadgeNum", (int)HomeBadgeFont, Color.white, TextAnchor.MiddleCenter);
        Stretch(bnum.rectTransform, Vector2.zero, Vector2.zero);
        bnum.resizeTextMinSize = UiFontPx((int)HomeBadgeFontMin);
        bnum.resizeTextMaxSize = UiFontPx((int)HomeBadgeFont);
        bnum.resizeTextForBestFit = true;                       // "CR2024" 这种长世界号缩到塞得下
        bnum.horizontalOverflow = HorizontalWrapMode.Overflow;
        bnum.verticalOverflow = VerticalWrapMode.Truncate;
        bnum.text = w.Key ?? "";

        // 编号（大，主题色）
        var num = MakeText(img.transform, "Title", (int)HomeCardIdFont, AccentBase, TextAnchor.UpperLeft);
        // 编号：图标右边、竖直居中（字号从卡片的 34 缩到 26，列表行不需要那么大）
        SetRectPx(num.rectTransform, HomeListNumX, 22f, HomeListNumW, 32f);
        num.fontSize = UiFontPx(26);
        num.horizontalOverflow = HorizontalWrapMode.Wrap;        // 编号最长 6 字符（CETX-X）≈106 < 148 单行放得下
        num.text = w.FinalId;

        // 名字：编号右边、一行（列表行的读法；宽了就不折行、竖直居中），长名字靠 bestFit 缩一点点兜底
        // 建卡这一刻先给 HomeNameMinW（真实宽度要等 LayoutHomeRowMeta 才知道，
        // 那时统计块总宽才算出来）。这里不用名义宽度 1000 减起点 ——
        // 那等于默认名字栏能伸到 764，而统计块左边界只有 456 左右，先摆宽了会有一帧压字。
        var nm = MakeText(img.transform, "Name", (int)HomeCardNameFont, new Color(1f, 1f, 1f, 0.82f), TextAnchor.UpperLeft);
        SetRectPx(nm.rectTransform, HomeListNameX, 20f, HomeNameMinW, 36f);
        nm.resizeTextMinSize = UiFontPx(16);
        nm.resizeTextMaxSize = UiFontPx((int)HomeCardNameFont);
        nm.horizontalOverflow = HorizontalWrapMode.Wrap;
        nm.verticalOverflow = VerticalWrapMode.Truncate;
        nm.lineSpacing = 0.95f;
        nm.resizeTextForBestFit = true;                          // 长名字自动缩到塞得下（只缩不放）
        nm.resizeTextMinSize = UiFontPx((int)HomeCardNameFontMin);
        nm.resizeTextMaxSize = UiFontPx((int)HomeCardNameFont);
            nm.text = string.IsNullOrEmpty(w.FinalName) ? "" : Lang.Tr(w.FinalName);   // 关卡名在 Lang.Official（游戏本地化表生成）

        BuildHomeRowStats(img.transform, idx);
    }

    // ================= 行内右侧统计（完成度 / 死亡热点）=================

    /// <summary>一行右缘那一块：完成度百分比 + 完成度条 + 死亡热点小柱。
    /// 整块锚在行的右缘（anchor/pivot 都取 (1,0.5)，x = −HomeStatsPadR）：行宽在选中时会从 1262 缩到 783，
    /// 右锚的东西不用跟着重摆一次，只有名字那一栏要按剩下的宽度收窄（见 LayoutHomeRowMeta）。</summary>
    private static void BuildHomeRowStats(Transform parent, int idx) {
        try {
            // 完成度那一组（百分比 + 条）：放在统计块左半边，整组的中线比行中线抬高 4 一点，
            // 右边那组小柱底对齐行底 —— 两组基线不同才分得开谁是谁
            float cy = HomeStatRowCy;
            var pct = MakeText(parent, "Pct", (int)HomeStatFont, new Color(1f, 1f, 1f, 0.70f), TextAnchor.MiddleLeft);
            SetRectRightPx(pct.rectTransform, StatRight(HomeStatPctW), cy, HomeStatPctW, 28f);
            _homePctTexts.Add(pct);

            // 完成度条 = 一颗固定宽的胶囊底槽（灰）+ 盖在它身上的「已完成色」（主题色）。
            // 两张用同一张圆角贴图、同样走九宫格，区别只在颜色和宽度：
            // 底槽铺满 HomeStatProgW 永不缩放，已完成色的宽度按完成度走。
            // 两者同形同高、左缘对齐，所以已完成的那一头是圆头而不是直角，两头也都由同一颗圆角收住。
            // 已完成色与底槽平级（不是底槽的子物体）：两者用的是同一张形状贴图，
            // 谁也不必去裁剪谁，平级最直白；子物体反而要再挂一层裁剪才能得到同样的结果。
            var slot = MakeImage(parent, "ProgSlot", new Color(1f, 1f, 1f, 0.10f));
            slot.sprite = HomeProgBarSprite; slot.type = Image.Type.Sliced; slot.raycastTarget = false;
            SetRectRightPx(slot.rectTransform, StatRight(HomeStatProgX + HomeStatProgW), cy, HomeStatProgW, HomeStatProgH);
            _homeProgSlots.Add(slot);
            // 已完成色：初始宽度给 0，刷新时按完成度给。
            // 左缘与底槽左缘对齐、高度与条高一致，宽度变化时向右生长、左端不动。
            // 走 ApplyHomeProgFill 而不是直接写 RectTransform：宽度 0 在这个入口就被关掉，
            // 建卡到首次刷新之间不会先闪一条宽度 0 的残线。
            var fill = MakeImage(parent, "ProgFill", AccentBase);
            fill.sprite = HomeProgBarSprite; fill.type = Image.Type.Sliced; fill.raycastTarget = false;
            SetRectLeftEdgePx(fill.rectTransform, HomeStatProgX, cy, 0f, HomeStatProgH);
            fill.enabled = false;
            _homeProgFills.Add(fill);

            // 死亡热点小柱：固定 12 根，100 格死亡分布降采样填（每根管 8~9 格）
            var bars = new Image[HomeStatHeatBars];
            for (int i = 0; i < HomeStatHeatBars; i++) {
                var b = MakeImage(parent, "Heat" + i, FrameLineColor);
                b.sprite = HomeHeatBarSprite; b.type = Image.Type.Sliced; b.raycastTarget = false;
                // 从左往右排：最左那根 = 最早那 10% 完成度，最右那根 = 最后 10%（越靠右越接近通关）。
                // 偏移必须用加号：SetRectRightPx 的 fromRight 越小越靠右，
                // 所以「往左走一格」要把 fromRight 调大。写成减号会让整排柱子往右溢出卡片右缘 ——
                // 实机截图标出来第一根就从 x=1706 起（卡右缘 1705），整排掉到卡外，末几根还被容器框切掉。
                // 上一轮我以为「下标直接乘步距」就够，其实还得看这个数是加还是减：
                // 同一套「左端起点 + 按序偏移」的算法在左锚坐标系里成立，
                // 换成右锚坐标系方向就反了。fromRight 与 localX 同向递减（见 StatRight）。
                // 初始高度取保底值，中心按基线减半个柱高摆 —— 与 RefreshHomeRowStats 的算法一致，
                // 建卡那一帧和刷新后不会跳一下（少半像素的错位在选中动画里看得出来）
                SetRectRightPx(b.rectTransform, StatRight(HomeStatHeatX + HomeStatHeatSpan) + i * HomeStatHeatPitch,
                               HomeStatHeatBaseline - HomeStatHeatBarMinH * 0.5f, HomeStatHeatBarW, HomeStatHeatBarMinH);
                bars[i] = b;
            }
            _homeRowBars.Add(bars);
        } catch (System.Exception e) {
            Lg.Ex("建官谱行内统计失败", e);
            // 补齐长度，后面按世界下标取的时候不会越界
            while (_homePctTexts.Count <= idx) _homePctTexts.Add(null);
            while (_homeProgSlots.Count <= idx) _homeProgSlots.Add(null);
            while (_homeProgFills.Count <= idx) _homeProgFills.Add(null);
            while (_homeRowBars.Count <= idx) _homeRowBars.Add(null);
        }
    }

    /// <summary>右锚定位：锚点/轴都取右中，fromRight 是「距右缘多少像素」（往左为正），
    /// centerY 是「距行中线往下多少像素」。用中线而不是上边缘：三样东西（百分比 / 完成度条 / 小柱）
    /// 都是各自居中或底对齐的，拿中线当基准才看得出它们排没排齐。</summary>
    private static void SetRectRightPx(RectTransform rt, float fromRight, float centerY, float wPx, float hPx) {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-fromRight, -centerY);
        rt.sizeDelta = new Vector2(wPx, hPx);
    }

    /// <summary>左缘定位：锚点仍取行的右中（这样行宽变化时跟着走），但 pivot 取左中，
    /// 于是 sizeDelta.x 变大时元素向右生长、左缘纹丝不动。
    /// 完成度的「已完成色」矩形要的就是这个：它表达「从条身左端起填充」，
    /// 进度变化时左端必须钉住。用 SetRectRightPx（pivot 右中）会让它往左长 ——
    /// 方向是反的，进度条会从右往左填。这个坑我踩过一次。
    /// localX 是统计块局部坐标，和 StatRight 配套使用。</summary>
    private static void SetRectLeftEdgePx(RectTransform rt, float localX, float centerY, float wPx, float hPx) {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(-StatRight(localX), -centerY);
        rt.sizeDelta = new Vector2(wPx, hPx);
    }

    /// <summary>行内统计块三样东西的横向位置（距行右缘）。列在一起是因为它们是一条基线上的三段，
    /// 改一处必须三处一起改，分开写在三个调用点迟早会排歪。</summary>
    private static float StatRight(float localX) { return HomeStatsPadR + HomeStatsW - localX; }

    /// <summary>按世界所在区域取徽章色。同一区域共用一个色相，跨区域一眼能分开；
    /// 亮度按区域序号错开一点，同区域内相邻的两个世界（1 和 2）也不会看成同一块。</summary>
    private static Color HomeBadgeColorOf(OfficialLevels.WorldInfo w) {
        Color[] baseCols = {
            new Color(0.42f, 0.72f, 1.00f),   // 0 基础关卡：主题蓝
            new Color(0.40f, 0.86f, 0.72f),   // 1 进阶关卡：青绿
            new Color(0.76f, 0.56f, 1.00f),   // 2 附加关卡：紫
            new Color(1.00f, 0.74f, 0.34f),   // 3 皇冠岛：琥珀
            new Color(1.00f, 0.48f, 0.72f),   // 4 Muse Dash：粉
            new Color(0.52f, 0.92f, 0.52f),   // 5 CR2024：绿
            new Color(0.86f, 0.62f, 0.44f),   // 6 愚人节＆彩蛋：陶土
        };
        int r = HomeRegionOf(w);
        if (r < 0 || r >= baseCols.Length) return AccentBase;
        Color c = baseCols[r];
        // 同区域内按世界下标错开 ±8% 明度：色相一样、亮度不同，一排看下来有节奏也不刺眼
        float k = 1f + ((w != null && w.Index >= 0 ? w.Index : 0) % 3 - 1) * 0.08f;
        return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
    }

    /// <summary>徽章方块用的圆角 sprite（48 见方、半径 HomeBadgeR）：跟全站那张 RoundedSprite 分开，
    /// 那个半径只有 10（RAD_CARD），贴在 48 见方上四角几乎看不出圆</summary>
    private static Sprite _homeBadgeSprite;
    private static Sprite HomeBadgeSprite {
        get {
            if (_homeBadgeSprite == null) _homeBadgeSprite = CreateRoundedSprite(64, (int)HomeBadgeR);
            return _homeBadgeSprite;
        }
    }

    /// <summary>完成度条的圆角 sprite：底槽与「已完成色」共用这一张，整条的形状来源。
    /// 必须单独烘，不能套全站那张 RoundedSprite —— 那张半径 10，而条只有 16 高，
    /// 9-slice 的 border 比整块还大，顶/底边会被吃光、圆角直接画歪（同 SmallRoundSprite 那条注释）。
    /// 半径走 HomeStatProgR，条高 16 算下来正好是一颗饱满的胶囊，显示高 16 大于两倍半径加一。
    /// 底槽铺在固定宽的条身上（永不缩放）；已完成色宽度按完成度走，
    /// 宽度小时靠 HomeStatProgFillMinW 兜住，两条 border 不会叠上。</summary>
    private static Sprite _homeProgBarSprite;
    private static Sprite HomeProgBarSprite {
        get {
            if (_homeProgBarSprite == null) _homeProgBarSprite = CreateRoundedSprite(32, HomeStatProgR);
            return _homeProgBarSprite;
        }
    }

    /// <summary>死亡热点小柱专用：柱宽只有 4，贴图也只取 16 见方（省一次 16×16 的贴图分配）。
    /// 半径 1 是 4 宽能保住形状的上限；贴图边长取 16，大于两倍半径（16 大于 2）。</summary>
    private static Sprite _homeHeatBarSprite;
    private static Sprite HomeHeatBarSprite {
        get {
            if (_homeHeatBarSprite == null) _homeHeatBarSprite = CreateRoundedSprite(16, HomeStatHeatBarR);
            return _homeHeatBarSprite;
        }
    }

    /// <summary>完成度那一组（百分比 + 条 + 已完成色）的中线：距行中线往下 4 像素。
    /// 提到常量是因为建卡、刷新、重排三处都要用同一个值 —— 散着写在三处，
    /// 改一处漏一处就会出现「刷新后跳半格」这种只在某些时序下看得见的错位。</summary>
    private const float HomeStatRowCy = -4f;

    /// <summary>完成度条里那段「已完成色」该有多宽：条宽乘比例。非 0 时保底到一个超过两倍半径的
    /// 宽度（再窄九宫格两条 border 就叠上、右端圆头会被压平），再夹住不让它超过条身 ——
    /// 条身收窄时填充必须跟着收，否则会捅出底槽。
    /// 记 0 的返回 0，由调用方据此关掉 Image（见 ApplyHomeProgFill）：
    /// 宽度 0 的 sliced Image 在有些驱动上不会退化掉、会画出一条一像素的残线，
    /// 显式 enabled=false 比指望「顶点退化不产生面片」可靠。未通关的谱子比例就是 0，
    /// 列表里总有几个，所以这条不能靠运气。</summary>
    private static float HomeProgFillWidth(float ratio) {
        float w = HomeStatProgW * Mathf.Clamp01(ratio);
        if (w > 0f && w < HomeStatProgFillMinW) w = HomeStatProgFillMinW;
        if (w > HomeStatProgW) w = HomeStatProgW;
        return w;
    }

    /// <summary>给一行的「已完成色」设宽度，并同步它的显隐。
    /// 宽度与 enabled 必须一起定：两者都由同一个 w 决定，分开写迟早会出现
    /// 「宽度 0 但还开着」这种只有其中一处被改到的状态。
    /// localX 传条身左缘（未缩进），与底槽左缘同一条竖线。</summary>
    private static void ApplyHomeProgFill(Image fill, float ratio, float localX, float centerY) {
        if (fill == null) return;
        float w = HomeProgFillWidth(ratio);
        fill.enabled = w > 0f;
        if (!fill.enabled) return;
        SetRectLeftEdgePx(fill.rectTransform, localX, centerY, w, HomeStatProgH);
    }

    /// <summary>刷新行内统计：完成度百分比 + 完成度条长度 + 死亡热点小柱高度。
    /// 完成度取游戏自己的存档（`Persistence.GetPercentCompletion`，跟详情区左栏同一个来源）；
    /// 死亡热点取我们自己记的 `official_stats.txt`（按最终关 id，一个世界一格）。</summary>
    private static void RefreshHomeRowStats() {
        try {
            var worlds = OfficialLevels.Worlds();
            for (int i = 0; i < worlds.Count && i < _homeCardRts.Count; i++) {
                var w = worlds[i];
                if (w == null) continue;

                // ---- 完成度 ----
                float pct01 = 0f;
                bool havePct = false;
                if (w.Index >= 0) {
                    try { pct01 = Persistence.GetPercentCompletion(w.Index, false, false); havePct = true; }
                    catch { havePct = false; }
                }
                int pctTxt = Mathf.Clamp(Mathf.RoundToInt(pct01 * 100f), 0, 100);
                if (i < _homePctTexts.Count && _homePctTexts[i] != null)
                    _homePctTexts[i].text = havePct ? pctTxt + "%" : "—";
                // 比例留档：条身宽度会在选中/取消时变，填充必须按「当时的条宽 × 比例」重算，
                // 不能把这一帧算出的像素宽直接存下来（那就是取消选中后条短一截、回不去的来路）
                float ratio = havePct ? Mathf.Clamp01(pct01) : 0f;
                while (_homeProgRatios.Count <= i) _homeProgRatios.Add(0f);
                _homeProgRatios[i] = ratio;
                if (i < _homeProgFills.Count && _homeProgFills[i] != null) {
                    // 已完成色的宽度就是条宽按完成度切一块：底槽与它同形，所以已完成的那一头是圆的。
                    // 比例 0（没通关）时这一块整块关掉，不靠宽度 0 去退化
                    ApplyHomeProgFill(_homeProgFills[i], ratio, HomeStatProgX, HomeStatRowCy);
                }

                // ---- 死亡热点：100 格降采样到 12 根 ----
                if (i >= _homeRowBars.Count) continue;
                var bars = _homeRowBars[i];
                if (bars == null || bars.Length == 0) continue;
                // 游玩次数/通关数这行用不到（那是详情区的活），这里只要死亡分布
                int plays, clears; int[] deaths;
                OfficialLevels.TryRead(w.FinalId, out plays, out deaths, out clears);
                int[] merged = new int[bars.Length];
                int max = 1;
                if (deaths != null) {
                    for (int b = 0; b < bars.Length; b++) {
                        int b0 = b * OfficialLevels.DeathBuckets / bars.Length;
                        int b1 = (b + 1) * OfficialLevels.DeathBuckets / bars.Length;
                        if (b1 <= b0) b1 = b0 + 1;
                        int sum = 0;
                        for (int k = b0; k < b1 && k < deaths.Length; k++) sum += deaths[k];
                        merged[b] = sum;
                        if (sum > max) max = sum;
                    }
                }
                while (_homeRowCounts.Count <= i) _homeRowCounts.Add(null);
                _homeRowCounts[i] = merged;
                for (int b = 0; b < bars.Length; b++) {
                    if (bars[b] == null) continue;
                    var brt = bars[b].rectTransform;
                    // 柱子底边固定落在基线上、往上长：把中心往上挪半个柱高（centerY 往负方向就是上）
                    float h = merged[b] > 0
                        ? Mathf.Max(HomeStatHeatBarMinH, HomeStatHeatH * (merged[b] / (float)max))
                        : HomeStatHeatBarMinH;
                    // 横向位置与建卡时同一套算法（见 BuildHomeRowStats 里那段注释）：
                    // 下标乘步距，用加号 —— fromRight 越大越靠左，往左一格是加不是减
                    SetRectRightPx(brt, StatRight(HomeStatHeatX + HomeStatHeatSpan) + b * HomeStatHeatPitch,
                                   HomeStatHeatBaseline - h * 0.5f, HomeStatHeatBarW, h);
                }
                PaintHomeRowStatsColor(i);       // 颜色统一走这一处（刷新 / 选中态切换都调它）
            }
        } catch (System.Exception e) {
            Lg.Ex("刷官谱行内统计失败", e);
        }
    }

    /// <summary>列表行里的那一排寄录图小柱要不要显示。详情区开着时藏掉（详情区自己有一张完整的寄录图），
    /// 取消选中时放回来。隐藏只动图元自己，不碰驱动 —— 小柱的高度只在刷新时写，
    /// 所以下次刷新不会把它们点回来（与热力图那条「隐藏必须同时停掉每帧驱动」是两回事）。</summary>
    private static void ApplyHomeRowHeatVisible(bool visible) {
        try {
            bool hide = !visible;
            if (_homeRowHeatHidden == hide) return;
            _homeRowHeatHidden = hide;
            for (int i = 0; i < _homeRowBars.Count; i++) {
                var bars = _homeRowBars[i];
                if (bars == null) continue;
                for (int b = 0; b < bars.Length; b++) {
                    if (bars[b] == null) continue;
                    if (bars[b].gameObject.activeSelf != visible) bars[b].gameObject.SetActive(visible);
                }
            }
        } catch { }
    }

    /// <summary>行宽变了之后收窄名字那一栏：右缘的统计块是右锚的、跟着走不用管，
    /// 但名字栏原来一直撑到行右端（`availW − HomeListNameX − 16`），不收的话会钻到统计块底下去。
    /// 宽度取「统计块左边界 − 名字栏起点 − 一道缝」，与 RecalcHomeStatsGeometry 里
    /// 「统计块总宽 = 行宽 − PadR − Gap − NameX − HomeNameMinW」是同一个式子的两头：
    /// 那头定统计块的左边界，这头把剩下的全给名字栏。两边必须同源，
    /// 各算一次的话其中一边改了另一边没改，就会重新出现压字。</summary>
    private static void LayoutHomeRowMeta(RectTransform rt, float availW) {
        try {
            if (rt == null) return;
            float statL = HomeStatsPadR + HomeStatsW + HomeStatsGap;      // 统计块左缘距行左缘
            float nameW = availW - HomeListNameX - statL;
            var nmRt = rt.Find("Name") as RectTransform;
            if (nmRt != null) SetRectPx(nmRt, HomeListNameX, 20f, Mathf.Max(HomeNameMinW, nameW), 36f);
        } catch { }
    }

    // ================= 详情区 =================

    private static void BuildHomeDetail(Transform parent) {
        var go = new GameObject("HomeDetail", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        // 底边跟着画布走（拉伸锚点），不写死 900
        SetRectPxV(rt, HD_Left, HD_Top, HD_W, BandBottomMargin);
        _homeDetailGo = go;
        go.AddComponent<CanvasGroup>();

        // 底板（跟设置页那两块遮罩一套视觉：平坦 0.45 黑 + 主题色描边）
        var bg = MakeImage(rt, "HomeDetailBg", HomeBackdrop);
        RegisterAlphaBackdrop(bg);
        RegisterGlassPanel(bg);      // 点击谱面后详细信息区遮罩 要磨砂
        bg.sprite = HomeRoundSprite; bg.type = Image.Type.Sliced; bg.raycastTarget = false;
        Stretch(bg.rectTransform, Vector2.zero, Vector2.zero);
        MakeMaskFrame(rt, "HomeDetailFrame", 0f, 0f, HD_W, HD_Bottom - HD_Top, HomeMaskRadius);
        AnchorMaskFrameV(rt, "HomeDetailFrame", 0f, 0f, HD_W, BandBottomMargin);

        // 标题 + 元信息
        _homeTitleText = MakeTitle(rt, "HomeTitle", 34, Color.white, TextAnchor.UpperLeft);
        SetRectPx(_homeTitleText.rectTransform, HD_PadX, HD_TitleY, HD_W - HD_PadX * 2f, HD_TitleH);
        _homeMetaText = MakeText(rt, "HomeMeta", 20, new Color(1f, 1f, 1f, 0.70f), TextAnchor.UpperLeft);
        SetRectPx(_homeMetaText.rectTransform, HD_PadX, HD_MetaY, HD_W - HD_PadX * 2f, HD_MetaH);
        // 标题下面一条细分割线（把“标题区”和下面几块分开）
        var sep = MakeImage(rt, "TitleSep", new Color(FrameLineColor.r, FrameLineColor.g, FrameLineColor.b, 0.35f));
        sep.sprite = SharpSprite; sep.type = Image.Type.Simple; sep.raycastTarget = false;
        SetRectPx(sep.rectTransform, HD_PadX, HD_MetaY + HD_MetaH + 3f, HD_W - HD_PadX * 2f, 2f);

        // 子关卡按钮行
        var subCaption = MakeText(rt, "SubCaption", 20, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleLeft);
        SetRectPx(subCaption.rectTransform, HD_PadX, HD_SubCapY, 120f, HD_SubCapH);
        subCaption.text = Lang.Tr("子关卡");
        var subRow = new GameObject("HomeSubRow", typeof(RectTransform));
        subRow.transform.SetParent(rt, false);
        SetRectPx(subRow.GetComponent<RectTransform>(), HD_PadX, HD_SubY, HD_W - HD_PadX * 2f, HD_SubH);
        var layout = subRow.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f; layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false; layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        _homeSubRow = subRow.GetComponent<RectTransform>();

        // 存档数据（一块遮罩 + 两栏文字）
        var saveBg = MakeImage(rt, "SaveBackdrop", HomeBackdrop);
        RegisterAlphaBackdrop(saveBg);
        RegisterGlassPanel(saveBg);      // 详细信息区里的存档块 同一族
        saveBg.sprite = RoundedSprite; saveBg.type = Image.Type.Sliced; saveBg.raycastTarget = false;
        SetRectPx(saveBg.rectTransform, HD_PadX, HD_SaveY, HD_W - HD_PadX * 2f, HD_SaveH);
        MakeMaskFrame(rt, "SaveFrame", HD_PadX, HD_SaveY, HD_W - HD_PadX * 2f, HD_SaveH);
        _homeSaveText1 = MakeText(rt, "SaveL", 22, Color.white, TextAnchor.UpperLeft);
        SetRectPx(_homeSaveText1.rectTransform, HD_PadX + 16f, HD_SaveY + 14f, (HD_W - HD_PadX * 2f) * 0.52f - 20f, HD_SaveH - 28f);
        _homeSaveText2 = MakeText(rt, "SaveR", 22, new Color(1f, 1f, 1f, 0.88f), TextAnchor.UpperLeft);
        SetRectPx(_homeSaveText2.rectTransform, HD_PadX + (HD_W - HD_PadX * 2f) * 0.52f, HD_SaveY + 14f,
                  (HD_W - HD_PadX * 2f) * 0.48f - 16f, HD_SaveH - 28f);

        // 内容 tab：通关记录 / 寄录图
        // 按钮无底色，选中由后面那块会滑动的实心淡蓝块表示（跟库页详情 tab 一样）
        int tabCount = 2;
        _homeTabFrame = MakeImage(rt, "HomeTabFrameSlide", HighlightFillColor);
        RegisterFillImage(_homeTabFrame);   // 换主题色时要重刷它
        _homeTabFrame.raycastTarget = false;
        _homeTabFrame.sprite = RoundedSprite; _homeTabFrame.type = Image.Type.Sliced;
        var tabFrameRt = _homeTabFrame.rectTransform;
        tabFrameRt.anchorMin = tabFrameRt.anchorMax = new Vector2(0f, 1f);
        tabFrameRt.pivot = new Vector2(0f, 1f);
        _homeTabSlide.Rt = tabFrameRt;
        _homeTabSlide.Set(new Vector2(HD_PadX - 4f, -(HD_TabY + 4f)), new Vector2(HomeTabW + 8f, HD_TabH - 8f));
        _homeTabSlide.Snap();
        string[] tabNames = { "通关记录", "寄录图" };
        for (int i = 0; i < tabCount; i++) {
            int idx = i;
            var b = HomeButton(rt, "HomeTab" + i, HD_PadX + i * (HomeTabW + 8f), HD_TabY, HomeTabW, HD_TabH,
                               new Color(1f, 1f, 1f, 0f), tabNames[i], 24, out var t);
            b.onClick.AddListener(() => SelectHomeDetailTab(idx));
            _homeTabBtns.Add(b); _homeTabTexts.Add(t);
        }

        // 内容区（同一块遮罩，两个 tab 共用位置，靠 SetActive 切换）
        var bodyBg = MakeImage(rt, "BodyBackdrop", HomeBackdrop);
        RegisterAlphaBackdrop(bodyBg);
        RegisterGlassPanel(bodyBg);      // 详细信息区的内容块 同一族
        bodyBg.sprite = RoundedSprite; bodyBg.type = Image.Type.Sliced; bodyBg.raycastTarget = false;
        SetRectPxV(bodyBg.rectTransform, HD_PadX, HD_BodyY, HD_W - HD_PadX * 2f, HD_BodyMargin);
        MakeMaskFrame(rt, "BodyFrame", HD_PadX, HD_BodyY, HD_W - HD_PadX * 2f, HD_BodyH);
        AnchorMaskFrameV(rt, "BodyFrame", HD_PadX, HD_BodyY, HD_W - HD_PadX * 2f, HD_BodyMargin);

        var clearGo = new GameObject("HomeClearArea", typeof(RectTransform));
        clearGo.transform.SetParent(rt, false);
        SetRectPxV(clearGo.GetComponent<RectTransform>(), HD_PadX + 16f, HD_BodyY + 14f, HD_W - HD_PadX * 2f - 32f, HD_ContentBottom);
        _homeClearArea = clearGo;
        _homeClearText = MakeText(clearGo.transform, "ClearText", 20, Color.white, TextAnchor.UpperLeft);
        Stretch(_homeClearText.rectTransform, Vector2.zero, Vector2.zero);

        var heatGo = new GameObject("HomeHeatArea", typeof(RectTransform));
        heatGo.transform.SetParent(rt, false);
        SetRectPxV(heatGo.GetComponent<RectTransform>(), HD_PadX + 16f, HD_BodyY + 14f, HD_W - HD_PadX * 2f - 32f, HD_ContentBottom);
        _homeHeatArea = heatGo;
        float heatH = HD_BodyH - 120f, heatW = HD_W - HD_PadX * 2f - 32f;   // 380 − 120 = 260：内容区给按钮让位后剩 288，再留 28 的上下提示位
        int bars = 24;
        float barW = (heatW - (bars - 1) * 5f) / bars;
        for (int i = 0; i < bars; i++) {
            var bar = MakeImage(heatGo.transform, "Bar" + i, FrameLineColor);
            bar.sprite = SharpSprite; bar.type = Image.Type.Simple; bar.raycastTarget = false;
            SetRectPx(bar.rectTransform, i * (barW + 5f), heatH - 24f - 2f, barW, 2f);
            _homeHeatBars.Add(bar);
        }
        var hint = MakeText(heatGo.transform, "HeatHint", 20, new Color(1f, 1f, 1f, 0.72f), TextAnchor.UpperLeft);
        SetRectPx(hint.rectTransform, 0f, 0f, heatW, 30f);          // 顶部一条（不压柱子）
        _homeHeatHint = hint;
        var axis = MakeText(heatGo.transform, "HeatAxis", 17, new Color(1f, 1f, 1f, 0.72f), TextAnchor.LowerLeft);
        SetRectPx(axis.rectTransform, 0f, heatH - 22f, heatW, 22f);  // 底部一条（不压柱子）；真实的底在刷新时按量到的区域高再摆一次
        axis.text = Lang.Tr("完成度 0%   ←   死亡分布   →   100%");
        axis.raycastTarget = false;
        _homeHeatAxis = axis;

        // 底部：进入关卡（必须用 SetRectPx 系的辅助函数：MakeButton 是中心锚定的，混用会跑到面板外；宽度收窄、水平居中）
        var enter = HomeButton(rt, "HomeEnter", (HD_W - HD_EnterW) * 0.5f, HD_EnterY, HD_EnterW, HD_EnterH, Accent, "进入关卡", 28, out var et);
        // 这块按钮本来就贴着详情区底，所以用底部锚定（写死 y 的话画布一高它就浮在中间）
        SetRectPxB(enter.GetComponent<RectTransform>(), (HD_W - HD_EnterW) * 0.5f, HD_EnterBottom, HD_EnterW, HD_EnterH);
        enter.onClick.AddListener(() => {
            string id = CurrentHomeSubId();
            if (!string.IsNullOrEmpty(id)) OfficialLevels.Enter(id);
        });

        go.SetActive(false);
    }

    private const float HomeTabW = 126f;

    /// <summary>详情区里的按钮：一律按左上角锚定（`SetRectPx`）建，避免混用中心锚定跑到面板外</summary>
    private static Button HomeButton(RectTransform parent, string name, float left, float top, float w, float h,
                                     Color color, string label, int fontSize, out Text text) {
        var img = MakeImage(parent, name, color);
        img.sprite = RoundedSprite; img.type = Image.Type.Sliced; img.raycastTarget = true;
        SetRectPx(img.rectTransform, left, top, w, h);
        var btn = img.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        text = MakeText(img.transform, "T", fontSize, Color.white, TextAnchor.MiddleCenter);
        RegisterUiTarget(btn, img, text);      // 三态交给交互态层（见 PortalUI.UiStates）
        Stretch(text.rectTransform, new Vector2(6f, 0f), new Vector2(-6f, 0f));
        text.text = label;
        return btn;
    }

    // ================= 选择 / 刷新 =================

    private static void SelectHomeWorld(int idx) {
        var worlds = OfficialLevels.Worlds();
        if (idx < 0 || idx >= worlds.Count) return;
        if (_homeSelected == idx) { DeselectHomeWorld(); return; }   // 再点一次 = 取消选择（跟库页一致）
        _homeSelected = idx;
        _homeDetailTab = 0;
        _homeExpandTarget = 0f;                       // 左栏淡出、列表区往右缩让出详情区、列表照常显示
        // 详情区自己有一张完整的寄录图，列表行里那排小柱这时候只是噪音，先收起来
        ApplyHomeRowHeatVisible(false);
        if (_homeDetailGo != null) _homeDetailGo.SetActive(true);
        LayoutHomeSubButtons();
        // 不一律选最终关，而是定位到“已通关关卡的下一关”（全通了才落到最终关）
        _homeSubIndex = DefaultHomeSubIndex();
        RefreshHomeDetail();
        // 选中的行高亮：跟库页选中行完全同一个色（`Accent`）—— 列表不再收走，选中只剩“底色”这一种表达
        for (int i = 0; i < _homeIconImgs.Count; i++) {
            if (_homeIconImgs[i] == null) continue;
            _homeIconImgs[i].color = (i == idx) ? Accent : HomeIconIdle;
            PaintHomeRowText(i, i == idx);      // 底色变浅 → 文字切深色
        }
        // 滚动/滚动条照常（列表还是那一条列表，选中不等于把它锁住）
    }

    /// <summary>默认选中哪一个子关卡 —— “已通关关卡的下一关”，全通了才落到最终关。
    /// 取法 = 这一串里第一个没通关的下标：它跟“已通关的下一关”等价，但中间有空洞时不会跳错
    /// （1-1 通了、1-2 没通、1-3 通了 → 应落在 1-2；按“最靠后的已通关 +1”会落到 1-4）。
    /// 判据 = 官谱自己的统计（`official_stats.txt` 里有通关记录就算通了）。</summary>
    private static int DefaultHomeSubIndex() {
        try {
            var ids = _homeSubIds;
            if (ids == null || ids.Count == 0) return 0;
            for (int i = 0; i < ids.Count; i++) {
                if (!IsOfficialCleared(ids[i])) return i;
            }
            return ids.Count - 1;      // 全部通关 → 最终关
        } catch (System.Exception e) {
            Lg.Ex("算官谱默认关卡失败（退回最终关）", e);
            return Mathf.Max(0, _homeSubIds != null ? _homeSubIds.Count - 1 : 0);
        }
    }

    /// <summary>这个官谱通关过没有（读 official_stats.txt 的通关记录）</summary>
    private static bool IsOfficialCleared(string id) {
        try {
            if (string.IsNullOrEmpty(id)) return false;
            OfficialLevels.Read(id, out _, out _, out var clears);
            return clears != null && clears.Count > 0;
        } catch { return false; }
    }

    /// <summary>取消选择：列表区拉回原位、左栏回来、全部卡片重新显出来（点空白处走这里）</summary>
    private static void DeselectHomeWorld() {
        if (_homeSelected < 0) return;
        _homeSelected = -1;
        _homeSubIndex = 0;
        _homeExpandTarget = 1f;      // 列表区重新铺满（详情在动画结束时会被隐藏）
        ApplyHomeRowHeatVisible(true);   // 列表收回原位，行里那排寄录图小柱一起放回来
        for (int i = 0; i < _homeIconImgs.Count; i++) {
            if (_homeIconImgs[i] != null) _homeIconImgs[i].color = HomeIconIdle;
            PaintHomeRowText(i, false);         // 底色回深 → 文字回浅色
        }
        if (_homeScroll != null) _homeScroll.enabled = true;
        // 内容高度/滚动条按“铺满态”复位（此刻可视区还是窄的，行宽留给展开动画每帧同步，见 ApplyHomeExpand 末尾的 ApplyHomeRowWidth）
        LayoutHomeGrid();
    }

    private static void SelectHomeDetailTab(int tab) {
        _homeDetailTab = tab;
        RefreshHomeDetail();
    }
    private static string CurrentHomeSubId() {
        if (_homeSubIndex >= 0 && _homeSubIndex < _homeSubIds.Count) return _homeSubIds[_homeSubIndex];
        return null;
    }

    /// <summary>刷新详情区（子关卡按钮 + 元信息 + 存档数据 + 通关记录 / 寄录图）</summary>
    private static void RefreshHomeDetail() {
        try {
            var worlds = OfficialLevels.Worlds();
            if (_homeSelected < 0 || _homeSelected >= worlds.Count) return;
            var w = worlds[_homeSelected];
            string subId = CurrentHomeSubId();

            if (_homeSubIds.Count != w.SubIds.Count) LayoutHomeSubButtons();
            for (int i = 0; i < _homeSubTexts.Count && i < _homeSubIds.Count; i++) {
                bool sel = (i == _homeSubIndex);
                var img = _homeSubTexts[i].transform.parent.GetComponent<Image>();
                if (img != null) img.color = new Color(1f, 1f, 1f, 0f);   // 按钮无底色，选中靠滑动块
                _homeSubTexts[i].color = sel ? HighlightTextColor : Color.white;
            }

            if (_homeTitleText != null) _homeTitleText.text = w.FinalId + "   " + Lang.Tr(w.FinalName);
            if (_homeMetaText != null) {
                // Index < 0 = 世界表里没有它（TN 这类），没有难度/试炼数据，别显示 0.0 装作有
                _homeMetaText.text = (w.Index >= 0)
                    ? Lang.TrFmt("世界 {0} · {1} · 难度 {2}", w.Key, w.Island, w.Difficulty)
                      + Lang.TrFmt(" · 试炼 ×{0}", w.Trial.ToString("0.0"))
                      + (w.HasCheckpoints ? Lang.Tr(" · 有检查点") : "")
                      + (w.IsDLC ? Lang.Tr(" · DLC") : "")
                    : Lang.TrFmt("世界 {0} · 游戏世界表里没有这一项（只有关卡本体）", w.Key);
            }

            // 左栏：游戏自己的世界数据（完成度和尝试次数）
            if (_homeSaveText1 != null) {
                string s1;
                if (w.Index < 0) {
                    s1 = Lang.Tr("完成度：—\n尝试次数：—\n状态：游戏没给这个世界记数据");   // 没有世界索引
                } else try {
                    float pct = Persistence.GetPercentCompletion(w.Index, false, false) * 100f;
                    int att = Persistence.GetWorldAttempts(w.Index);
                    bool done = Persistence.IsWorldComplete(w.Index);
                    bool perfect = Persistence.IsWorldPerfect(w.Index);
                    s1 = Lang.TrFmt("完成度：{0}%", pct.ToString("0.0")) + "\n"
                       + Lang.TrFmt("尝试次数：{0} 次", att) + "\n"
                       + Lang.Tr("状态：") + Lang.Tr(perfect ? "完美通关" : done ? "已通关" : "未通关");
                } catch (Exception ex) { s1 = Lang.TrFmt("（读游戏存档失败：{0}）", ex.GetType().Name); }
                _homeSaveText1.text = s1;
            }
            // 右栏：准确度 + 我们自己的统计
            if (_homeSaveText2 != null) {
                string s2;
                try {
                    int plays; int[] deaths; List<OfficialLevels.Clear> clears;
                    OfficialLevels.Read(subId, out plays, out deaths, out clears);
                    int strict = 0;
                    for (int i = 0; i < clears.Count; i++) if (clears[i].Slot == 2) strict++;
                    string accLine;
                    if (w.Index >= 0) {
                        float acc = Persistence.GetBestPercentAccuracy(w.Index, false) * 100f;
                        float xacc = Persistence.GetBestPercentXAccuracy(w.Index, false) * 100f;
                        accLine = Lang.TrFmt("最佳准确度：{0}%", acc.ToString("0.0")) + "\n"
                                + Lang.TrFmt("最佳 X 准确度：{0}%", xacc.ToString("0.0")) + "\n";
                    } else {
                        accLine = Lang.Tr("最佳准确度：—") + "\n" + Lang.Tr("最佳 X 准确度：—") + "\n";   // 世界表里没有它 → 没有世界级准确度
                    }
                    s2 = accLine
                       + Lang.TrFmt("本关游玩 {0} 次 · 通关 {1} 次 · 严判 {2} 次", plays, clears.Count, strict);
                } catch (Exception ex) { s2 = Lang.TrFmt("（统计读取失败：{0}）", ex.GetType().Name); }
                _homeSaveText2.text = s2;
            }

            for (int i = 0; i < _homeTabBtns.Count; i++) {
                var img = _homeTabBtns[i].GetComponent<Image>();
                if (img != null) img.color = new Color(1f, 1f, 1f, 0f);     // 按钮无底色，选中靠滑动块
                if (i < _homeTabTexts.Count) _homeTabTexts[i].color = (i == _homeDetailTab) ? HighlightTextColor : Color.white;
            }
            // 内容 tab 的滑动块滑到当前 tab
            _homeTabSlide.Set(new Vector2(HD_PadX + _homeDetailTab * (HomeTabW + 8f) - 4f, -(HD_TabY + 4f)),
                              new Vector2(HomeTabW + 8f, HD_TabH - 8f));
            // 子关卡行的滑动块滑到当前子关卡
            SetHomeSubFrameTarget();
            if (_homeClearArea != null) _homeClearArea.SetActive(_homeDetailTab == 0);
            if (_homeHeatArea != null) _homeHeatArea.SetActive(_homeDetailTab == 1);

            if (_homeDetailTab == 0 && _homeClearText != null) {
                int plays; int[] deaths; List<OfficialLevels.Clear> clears;
                OfficialLevels.Read(subId, out plays, out deaths, out clears);
                int strict = 0;
                for (int i = 0; i < clears.Count; i++) if (clears[i].Slot == 2) strict++;
                var sb = new System.Text.StringBuilder();
                sb.Append(Lang.TrFmt("这一关的游玩次数：{0}    通关 {1} 次   严判 {2} 次", plays, clears.Count, strict)).Append("\n\n");
                if (clears.Count == 0) {
                    sb.Append(Lang.Tr("还没有通关记录。"));
                } else {
                    int from = Mathf.Max(0, clears.Count - 6);
                    for (int i = clears.Count - 1; i >= from; i--) {
                        var c = clears[i];
                        string when;
                        try { when = DateTimeOffset.FromUnixTimeSeconds(c.Time).ToLocalTime().ToString("MM-dd HH:mm"); }
                        catch { when = "?"; }
                        sb.Append(when).Append("    ")
                          .Append(Lang.Tr(c.Slot == 2 ? "严判" : c.Slot == 1 ? "标判" : c.Slot == 0 ? "宽判" : "判定未知"))
                          .Append("    ").Append(Lang.TrFmt("完成度 {0}%", c.Percent)).Append("\n");
                    }
                }
                _homeClearText.text = sb.ToString();
            }

            if (_homeDetailTab == 1) {
                int plays; int[] deaths; List<OfficialLevels.Clear> clears;
                OfficialLevels.Read(subId, out plays, out deaths, out clears);
                int max = 1, total = 0;
                var merged = new int[_homeHeatBars.Count];
                int per = Mathf.Max(1, LevelLibrary.OfficialLevels.DeathBuckets / _homeHeatBars.Count);
                for (int i = 0; i < merged.Length; i++) {
                    for (int k = 0; k < per; k++) {
                        int idx = i * per + k;
                        if (idx < deaths.Length) merged[i] += deaths[idx];
                    }
                    total += merged[i];
                    if (merged[i] > max) max = merged[i];
                }
                // 柱区高度按实际区域量（区域是拉伸的，画布一高柱子就该跟着长）。
                // 注意 `_homeHeatArea` 是 GameObject，要 GetComponent<RectTransform>()
                var heatRt = _homeHeatArea != null ? _homeHeatArea.GetComponent<RectTransform>() : null;
                float heatH2 = (heatRt != null && heatRt.rect.height > 1f) ? heatRt.rect.height : (HD_BodyH - 120f);   // 柱区局部高度
                float areaH = heatH2 - 24f - 30f;                    // 顶部提示条 30 + 底部轴 24 之外都能长
                // 底部那条轴也跟着量到的高度摆 —— 区域高是跟着「进入关卡」的让位拉伸的，
                // 写死 352 那种值一改版面就会掉到区域外面去
                if (_homeHeatAxis != null) SetRectPx(_homeHeatAxis.rectTransform, 0f, heatH2 - 22f, _homeHeatAxis.rectTransform.sizeDelta.x, 22f);
                for (int i = 0; i < _homeHeatBars.Count; i++) {
                    float h = 3f + areaH * (merged[i] / (float)max);
                    var brt = _homeHeatBars[i].rectTransform;
                    SetRectPx(brt, brt.anchoredPosition.x, heatH2 - 24f - h, brt.sizeDelta.x, h);
                    _homeHeatBars[i].color = new Color(FrameLineColor.r, FrameLineColor.g, FrameLineColor.b,
                        merged[i] > 0 ? 0.95f : 0.25f);
                }
                if (_homeHeatHint != null) {
                    _homeHeatHint.text = total > 0 ? Lang.TrFmt("这一关共 {0} 次死亡", total) : Lang.Tr("还没有寄录数据。");
                }
            }
        } catch (Exception e) {
            Lg.Ex("刷新官方关卡详情失败", e);
        }
    }

    /// <summary>把子关卡行的滑动块对准当前选中的按钮（块在行里、左上角锚定；按钮位置由布局组给）。
    /// instant=true 直接就位（刚重建完 / 刚换世界，别让它从上一关滑过来）</summary>
    private static void SetHomeSubFrameTarget(bool instant = false) {
        try {
            if (_homeSubSlide.Rt == null || _homeSubFrame == null) return;
            int i = Mathf.Clamp(_homeSubIndex, 0, Mathf.Max(0, _homeSubBtns.Count - 1));
            if (_homeSubBtns.Count == 0 || _homeSubBtns[i] == null) return;
            var brt = _homeSubBtns[i].GetComponent<RectTransform>();
            if (brt == null) return;
            // 布局组的坐标要等一帧才算准 → 用按钮自己的 anchoredPosition + sizeDelta 反推（左上角锚定）
            float w = brt.rect.width > 1f ? brt.rect.width : brt.sizeDelta.x;
            float h = brt.rect.height > 1f ? brt.rect.height : brt.sizeDelta.y;
            var pos = new Vector2(brt.anchoredPosition.x - 3f, brt.anchoredPosition.y + 3f);
            _homeSubSlide.Set(pos, new Vector2(w + 6f, h - 6f));
            if (instant) _homeSubSlide.Snap();
        } catch { }
    }

    private static void LayoutHomeSubButtons() {
        try {
            var worlds = OfficialLevels.Worlds();
            if (_homeSubRow == null || _homeSelected < 0 || _homeSelected >= worlds.Count) return;
            for (int i = _homeSubRow.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_homeSubRow.GetChild(i).gameObject);
            _homeSubBtns.Clear(); _homeSubTexts.Clear(); _homeSubIds.Clear();

            // 先建滑动块、再建按钮 —— 按钮无底色，选中由这块实心淡蓝滑过去表示；
            // 块必须画在按钮下面，否则盖住文字（跟库页详情 tab / 成就页左栏同一套）
            _homeSubFrame = MakeImage(_homeSubRow, "SubFrameSlide", HighlightFillColor);
            RegisterFillImage(_homeSubFrame);
            _homeSubFrame.raycastTarget = false;
            _homeSubFrame.sprite = RoundedSprite; _homeSubFrame.type = Image.Type.Sliced;
            var subFrameRt = _homeSubFrame.rectTransform;
            subFrameRt.anchorMin = subFrameRt.anchorMax = new Vector2(0f, 1f);
            subFrameRt.pivot = new Vector2(0f, 1f);
            var le = _homeSubFrame.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;                    // 行里有 HorizontalLayoutGroup，别让它排这块
            _homeSubSlide.Rt = subFrameRt;

            var w = worlds[_homeSelected];
            int n = Mathf.Max(1, w.SubIds.Count);
            float totalW = HD_W - HD_PadX * 2f;
            float bw = Mathf.Clamp(totalW / n - 6f, 44f, 110f);
            for (int i = 0; i < w.SubIds.Count; i++) {
                string id = w.SubIds[i];
                int idx = i;
                var b = HomeButton(_homeSubRow, "Sub" + i, 0f, 0f, bw, HD_SubH - 4f, new Color(1f, 1f, 1f, 0f),
                                   id.Substring(id.IndexOf('-') + 1), 20, out var t);
                b.transform.SetParent(_homeSubRow, false);   // HomeButton 建在 rt 下，这里挪回按钮行
                b.onClick.AddListener(() => { _homeSubIndex = idx; RefreshHomeDetail(); });
                _homeSubBtns.Add(b); _homeSubTexts.Add(t); _homeSubIds.Add(id);
            }
            SetHomeSubFrameTarget(true);                   // 摆位（重建后直接就位，别从上一关滑过来）
        } catch (Exception e) { Lg.Ex("铺子关卡按钮失败", e); }
    }

    // ================= 展开动画 / 每帧 =================

    /// <summary>行内统计块上色（完成度条 / 死亡小柱 / 徽章底）。刷新数据后、选中态切换时都走这里，
    /// 别在两处各写一份颜色 —— 选中行底色是浅蓝，统计块不跟着切深就会糊成一片看不清。</summary>
    private static void PaintHomeRowStatsColor(int i) {
        try {
            if (i < 0) return;
            bool sel = i == _homeSelected;
            // 选中行底色 = Accent（浅蓝），上面的东西一律切深；未选中是半透明黑，回到浅色
            var deep = HighlightTextColor;
            if (i < _homeProgSlots.Count && _homeProgSlots[i] != null)
                _homeProgSlots[i].color = sel ? new Color(0f, 0f, 0f, 0.16f) : new Color(1f, 1f, 1f, 0.10f);
            if (i < _homeProgFills.Count && _homeProgFills[i] != null)
                _homeProgFills[i].color = sel ? deep : AccentBase;
            if (i < _homePctTexts.Count && _homePctTexts[i] != null)
                _homePctTexts[i].color = sel ? deep : new Color(1f, 1f, 1f, 0.70f);
            if (i >= _homeRowBars.Count || i >= _homeRowCounts.Count) return;
            var bars = _homeRowBars[i];
            var counts = _homeRowCounts[i];
            if (bars == null || counts == null) return;
            for (int b = 0; b < bars.Length && b < counts.Length; b++) {
                if (bars[b] == null) continue;
                if (counts[b] > 0) {
                    // 有死亡的格子：暖红实心；选中行底色浅，红压上去要加深才看得见
                    bars[b].color = sel ? new Color(0.72f, 0.16f, 0.12f, 1f) : new Color(1f, 0.42f, 0.36f, 0.95f);
                } else {
                    bars[b].color = sel ? new Color(0f, 0f, 0f, 0.18f)
                                        : new Color(FrameLineColor.r, FrameLineColor.g, FrameLineColor.b, 0.18f);
                }
            }
        } catch { }
    }

    /// <summary>选中/取消时重刷行内文字颜色：选中行底色是浅蓝（跟库页选中行同一个色），
    /// 编号/名字都要切深色 —— 编号那栏本来就是主题色 `AccentBase`，压在浅蓝底上几乎看不见；
    /// 未选中行底色是半透明黑 → 回到“编号主题色 + 名字 0.82 白”，跟 `MakeHomeCard` 建的时候一模一样</summary>
    private static void PaintHomeRowText(int i, bool selected) {
        try {
            if (i < 0 || i >= _homeCardRts.Count) return;
            var rt = _homeCardRts[i];
            if (rt == null) return;
            var numRt = rt.Find("Title") as RectTransform;
            var nmRt = rt.Find("Name") as RectTransform;
            var numT = (numRt != null) ? numRt.GetComponent<Text>() : null;
            var nmT = (nmRt != null) ? nmRt.GetComponent<Text>() : null;
            var deep = HighlightTextColor;      // 全站统一的深色（选中态压在浅蓝底上的字都用它）
            if (numT != null) numT.color = selected ? deep : AccentBase;
            if (nmT != null) nmT.color = selected ? deep : new Color(1f, 1f, 1f, 0.82f);
            // 行内右侧那块统计也得跟着切（浅蓝底上的浅色统计会糊掉）
            PaintHomeRowStatsColor(i);
        } catch { }
    }

    /// <summary>一行的可用宽度：可视区宽度扣掉右边真正被占掉的部分。
    /// 滚动条是 AutoHide（内容放得下时整条藏起来），所以它只在真的显示的时候才该扣宽度 ——
    /// 早先这里无条件减 HomeGridBarW，结果六个谱面这种撑不满一屏的情况白扣 14 像素，
    /// 加上另外两个余量，卡片右缘停在 x=1705 而容器框线在 1859，右边空出 154 像素。
    /// 判断交给 ScrollRect 自己（IsActive 会考虑 AutoHide 与内容高度），不自己猜内容够不够。
    /// 左右两侧各留 HomeListPadL / HomeListPadR 的呼吸边，与容器背景遮罩对齐。</summary>
    private static float HomeRowAvailW(float viewportW) {
        float bar = 0f;
        if (_homeScroll != null && _homeScroll.verticalScrollbar != null && _homeScroll.verticalScrollbar.IsActive())
            bar = HomeGridBarW;
        return Mathf.Max(240f, viewportW - bar - HomeListPadL - HomeListPadR);
    }

    /// <summary>统计块三段几何变了之后，把每一行的绝对位置重排一遍。
    /// 为什么必须显式重排：三样东西（百分比 / 条 / 小柱）都是右锚定位，
    /// 位置在建卡那一刻就以像素写进 RectTransform 了；行宽一变、
    /// HomeStatsW / HomeStatProgW / HomeStatHeatX 跟着变，但不重排的话
    /// 条还是旧宽度、旧位置 —— 表现就是「条压在卡片右缘上、右边空一截」。
    /// 选中态与非选中态行宽不同（783↔1262），每次切换都要走这里。</summary>
    private static void RelayoutHomeStatsGeometry() {
        try {
            float cy = HomeStatRowCy;
            for (int i = 0; i < _homeCardRts.Count; i++) {
                var rt = _homeCardRts[i];
                if (rt == null) continue;
                if (i < _homePctTexts.Count && _homePctTexts[i] != null)
                    SetRectRightPx(_homePctTexts[i].rectTransform, StatRight(HomeStatPctW), cy, HomeStatPctW, 28f);
                if (i < _homeProgSlots.Count && _homeProgSlots[i] != null)
                    SetRectRightPx(_homeProgSlots[i].rectTransform, StatRight(HomeStatProgX + HomeStatProgW), cy,
                                   HomeStatProgW, HomeStatProgH);
                if (i < _homeProgFills.Count && _homeProgFills[i] != null) {
                    // 宽度按留档比例重新算，不能沿用上一次的 sizeDelta.x：
                    // 选中时条身收窄、填充跟着变短，取消选中后条身回宽，
                    // 沿用旧像素宽就会停在收窄时那一截上，看着像「掉了一段回不去」。
                    float ratio = i < _homeProgRatios.Count ? _homeProgRatios[i] : 0f;
                    ApplyHomeProgFill(_homeProgFills[i], ratio, HomeStatProgX, cy);
                }
                if (i >= _homeRowBars.Count || _homeRowBars[i] == null) continue;
                var bars = _homeRowBars[i];
                for (int b = 0; b < bars.Length; b++) {
                    if (bars[b] == null) continue;
                    var brt = bars[b].rectTransform;
                    SetRectRightPx(brt, StatRight(HomeStatHeatX + HomeStatHeatSpan) + b * HomeStatHeatPitch,
                                   HomeStatHeatBaseline - brt.sizeDelta.y * 0.5f,
                                   HomeStatHeatBarW, brt.sizeDelta.y);
                }
            }
        } catch { }
    }

    /// <summary>把“行宽”按当前可视区宽度同步一次（选中变窄 / 取消变宽都要跟着）。
    /// 只动行尺寸与行内元素，不动滚动位置、不动内容高度（那两件事归 `LayoutHomeGrid`）——
    /// 所以它可以在展开动画里每帧调，不会把玩家的滚动位置弹回顶部</summary>
    private static void ApplyHomeRowWidth() {
        try {
            if (_homeViewportRt == null) return;
            float vw = _homeViewportRt.rect.width;
            float availW = HomeRowAvailW(vw);
            // 统计块的三段几何跟着行宽走：条要吃掉多出来的宽度，一路铺到行的右缘。
            // 必须在下面设行宽之前算完，重排放在循环外只做一次。
            RecalcHomeStatsGeometry(availW);
            for (int i = 0; i < _homeCardRts.Count; i++) {
                var rt = _homeCardRts[i];
                if (rt == null || !rt.gameObject.activeSelf) continue;
                if (!Mathf.Approximately(rt.sizeDelta.x, availW)) rt.sizeDelta = new Vector2(availW, HomeListRowH);
                var rimRt = rt.Find("Rim") as RectTransform;
                if (rimRt != null) SetRectPx(rimRt, -2f, -2f, availW + 4f, HomeListRowH + 4f);
                // 名字栏跟着收窄（行内右缘那一块统计是右锚的，宽度变了它自己会跟）
                LayoutHomeRowMeta(rt, availW);
            }
            RelayoutHomeStatsGeometry();
        } catch { }
    }

    /// <summary>展开进度 → 几何：k=1 = 左栏在、列表铺满；k=0 = 左栏淡出、列表区左边界拉到 899、详情区占住左半边</summary>
    private static void ApplyHomeExpand(float k, bool instant = false) {
        try {
            k = Mathf.Clamp01(k);
            // ① 列表区几何：左边界 420 ↔ 899 平滑推过去（没选中时让开左栏、选中后让开详情）
            float left = Mathf.Lerp(HomeShrunkLeft, HomeGridLeft, k);
            // 这三条也走拉伸锚点（底边跟着画布，不写死 HomeBottom − HomeTop）
            if (_homeViewportRt != null) SetRectPxV(_homeViewportRt, left, HomeTop, HomeFullRight - left, BandBottomMargin);
            // ①-b 右侧背景遮罩跟着同一条边界走，右端到 HomeBackRight
            if (_homeGridBackdropRt != null)
                SetRectPxV(_homeGridBackdropRt, left, HomeTop, HomeBackRight - left, BandBottomMargin);
            if (_homeGridFrameRt != null)   // 描边框外扩 2px，底边距也少 2
                SetRectPxV(_homeGridFrameRt, left - 2f, HomeTop - 2f, (HomeBackRight - left) + 4f, BandBottomMargin - 2f);
            // ② 左栏整列淡出 + 关掉交互（选中后点不到）
            if (_homeTabCol != null) {
                var tcg = _homeTabCol.GetComponent<CanvasGroup>();
                if (tcg != null) {
                    tcg.alpha = k;
                    tcg.blocksRaycasts = k > 0.5f;
                    tcg.interactable = k > 0.5f;
                }
            }
            // ③ 卡片一张都不淡出、选中那张也不飞走：只让可视区左边界往右缩（① 那条），
            // 列表内容照常排、照常滚，选中只靠行底色表达（跟库页选中行同一个色，见 SelectHomeWorld）。
            // 但左栏仍然要淡出 —— 详情区正好占在它的位置上。
            for (int i = 0; i < _homeCardCgs.Count; i++) {
                var cg = _homeCardCgs[i];
                if (cg == null) continue;
                if (cg.alpha != 1f) cg.alpha = 1f;
                if (!cg.blocksRaycasts) cg.blocksRaycasts = true;
            }
            if (_homeScroll != null) _homeScroll.enabled = true;
            // ③-b 可视区宽度变了 → 行宽/描边/名字栏必须跟着走：`LayoutHomeGrid` 只在切区域和取消选中那一刻调一次，
            // 而取消选中那一刻动画还没跑、可视区还是窄的 → 行按窄宽度摆好后就留在窄宽度上（列表看着短一截）
            ApplyHomeRowWidth();
            // ④ 左侧详情：淡入/淡出（跟列表区同一条时间线）
            if (_homeDetailGo != null) {
                var dcg = _homeDetailGo.GetComponent<CanvasGroup>();
                if (dcg != null) dcg.alpha = Mathf.Clamp01(1f - k);
                bool visible = k < 0.999f;
                if (_homeDetailGo.activeSelf != visible) _homeDetailGo.SetActive(visible);
            }
        } catch { }
    }

    /// <summary>主页每帧（只在主页 tab 时由 TickPanel 调）：推展开动画 + 驱动三块滑动选中块</summary>
    private static void TickHome() {
        try {
            if (_homeRoot == null) return;
            if (Mathf.Abs(_homeExpand - _homeExpandTarget) > 0.001f) {
                _homeExpand = Mathf.MoveTowards(_homeExpand, _homeExpandTarget, Time.unscaledDeltaTime / HomeExpandDur);
                ApplyHomeExpand(_homeExpand);
            }
            // 滑动选中块（跟库 tab 那套一样要滑过去）
            _homeRegionSlide.Tick();
            _homeTabSlide.Tick();
            // 子关卡行的按钮位置是布局组在帧末给的 → 目标必须每帧重算一次，
            // 只在建行时算一次的话会拿到 (0,0)，块就跑到行首去了
            SetHomeSubFrameTarget();
            _homeSubSlide.Tick();
            // 行内统计（完成度 / 死亡热点）要跟着游玩变，但每帧刷 41 行太浪费：
            // 节流到 1 秒一次，且只在有面板开着的时候跑（TickHome 本身就只在主页 tab 时被调）
            _homeStatRefreshTimer -= Time.unscaledDeltaTime;
            if (_homeStatRefreshTimer <= 0f) {
                _homeStatRefreshTimer = HomeStatRefreshInterval;
                RefreshHomeRowStats();
            }
        } catch { }
    }

    /// <summary>面板销毁时清掉主页引用</summary>
    private static void ClearHomeRefs() {
        _homeRoot = null; _homeViewportRt = null; _homeContentRt = null; _homeScroll = null;
        _homeTabCol = null; _homeRegionFrameRt = null; _homeGridBar = null;
        _homeDetailGo = null;
        _homeTitleText = null; _homeMetaText = null; _homeSaveText1 = null; _homeSaveText2 = null;
        _homeClearArea = null; _homeHeatArea = null; _homeClearText = null; _homeHeatHint = null; _homeHeatAxis = null; _homeSubRow = null;
        _homeHeatBars.Clear(); _homeIconImgs.Clear(); _homeCardRts.Clear(); _homeCardCgs.Clear(); _homeCardGridPos.Clear();
        _homePctTexts.Clear(); _homeProgSlots.Clear(); _homeProgFills.Clear(); _homeProgRatios.Clear(); _homeRowBars.Clear(); _homeRowCounts.Clear();
        _homeRowHeatHidden = false;     // 上面那批小柱跟着一起没了，开关必须复位
        _homeStatRefreshTimer = 0f;
        _homeSubBtns.Clear(); _homeSubTexts.Clear(); _homeSubIds.Clear();
        _homeTabBtns.Clear(); _homeTabTexts.Clear();
        _homeRegionBtns.Clear(); _homeRegionNames.Clear();
        _homeSelected = -1; _homeSubIndex = 0; _homeDetailTab = 0; _homeRegion = 0;
        _homeExpand = _homeExpandTarget = 1f;
        // 行内那几张圆角贴图的缓存跟着面板生命周期走，逐张都要清：
        // 漏掉一张的话，面板关过一轮之后那张的 Texture2D 就没人引用了，白占显存
        _homeBadgeSprite = null; _homeProgBarSprite = null; _homeHeatBarSprite = null;
    }
}
