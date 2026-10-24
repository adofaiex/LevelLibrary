// [split-of] PortalUI.cs lines 1184-1325
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

/// <summary>PortalUI partial 模块：布局常量（竖直带 / 列表 / 详情区）与顶层 tab 重建。</summary>
public static partial class PortalUI {
    /// <summary>按 1920×1080 参考像素定位：left/top 分别从左 / 上边量，top 向下为正（pivot 取左上角）。</summary>
    private static void SetRectPx(RectTransform rt, float leftPx, float topPx, float wPx, float hPx) {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(leftPx, -topPx);
        rt.sizeDelta = new Vector2(wPx, hPx);
    }

    /// <summary>布局一律用 1920×1080 参考分辨率下的像素坐标（与 CanvasScaler referenceResolution 对齐，不受父容器宽度影响）。</summary>
    private const float RefW = 1920f, RefH = 1080f;
    /// <summary>竖直带（成就页 / 官谱库 / 设置页）距画布底的边距 —— 1080 画布上就是原来的 1040 底边，一个像素都没变。
    /// 底边不许再写成绝对 y：画布高不是恒 1080（见 CanvasH()，16:10 是 1200、4:3 是 1440），写死就会在下方空出一截。
    /// 一律走 SetRectPxV(rt, left, top, w, BandBottomMargin)（锚点拉伸 = 自己长到底）。</summary>
    private const float BandBottomMargin = 40f;
    /// <summary>库页（谱面列表 + 详情面板）距画布底的边距（1080 上就是原来的 1052）。</summary>
    private const float LibBottomMargin = 28f;
    // ---------- 顶部导航排（五块斜切小方块）----------
    // 位置在右上角：四个页面的左上角都已经有栏（库页详情面板 62 起、官谱库区域列 100~400、
    // 成就左栏 100~400、设置导航列 140~380），而玩家信息栏默认也挂在左上（x 30~360、y 20~130）——
    // 导航排放左上会正好压在它身上。右上角是各页唯一共同的空白带（成就页的等级标从 172 才开始）。
    private const float TopNavRight = 26f, TopNavTop = 20f;                       // 距画布右 / 上
    private const float TopNavBtnW = 96f;
    /// <summary>导航排占 20~70（五块 96×50 紧挨着拼成一整条圆角胶囊，整排 480 宽，右边缘对齐 TopNavRight；
    /// 被选中那块左右各让出 NavGooGap，让位时整排总宽会变、右边缘不动 ， 左端跟着动）。
    /// 页面竖直带从 TopNavH 起 —— 原来那条 150 高的文字 tab 栏拆掉以后，各页整体上移 PageTopShift；
    /// 各页的"顶"常量一律写成 TopNavH + 原本的相对偏移，别再写死 140（改一处就全跟着走）。</summary>
    private const float TopNavH = 92f;

    // ---------- 库页纵向分区（1920×1080 参考像素，从上往下排，互不重叠） ----------
    private const float SearchRowTop = TopNavH + 18f, SearchRowH = 52f; // 左侧一行：滚动信息栏 + 等级滑条（紧贴面板正上方）
    // 滚动信息栏（列表上方那条「正在播放」）在左侧这一行、长度让开难度滑条；
    // 搜索框在右侧列表上方、左端对齐列表左边界 899。注意 1767 处那条竖条是列表滚动条，跟「滚动栏」不是一回事。
    private const float SearchBoxX = 899f, SearchBoxTop = TopNavH + 26f, SearchBoxW = 700f, SearchBoxH = 44f;
    private const float NowBarSlotW = 380f, NowBarH = 40f;   // 行高 52，上下各留 6
    private const float PanelTop = TopNavH + 82f;               // 左侧详情面板顶
    private const float ListTop = TopNavH + 82f;                // 右侧谱面列表顶（与面板同顶）
    private const float PanelBottom = 1052f;                    // 面板与列表的底边（靠到屏幕下方）
    private const float PanelH = PanelBottom - PanelTop;        // 878
    private const float CountRowTop = TopNavH + 26f, CountRowH = 44f;   // 游玩统计行（列表正上方）

    // ---------- 库页「未选中：列表拉满 / 选中：详情区展开」动画 ----------
    // 未选中（初始）：详情区宽 806 → 0（左边界 62 不动），列表/搜索框左边界一起从 899 拉到 62，
    // 右边（列表 1736 / 搜索框 1599）全程不动，列表上边同时 222 → 158 往上长高；选中时反过来。
    // 列表/面板底部固定 1052，所以未选中时列表是往上长高，滚动条和字母条跟着一起长。
    private const float LibFullLeft = 62f;                  // 拉满时列表/搜索框的左边界 = 详情区左边界
    private const float ListRightEdge = 1736f;              // 列表右边界（固定不变）
    private const float SearchRightEdge = SearchBoxX + SearchBoxW;   // 搜索框右边界（1599，固定不变）
    private const float DetailLeftPx = 62f, DetailWPx = 806f;   // 详情区左边界与宽度
    private const float ListTopFull = SearchRowTop;         // 未选中时列表上边提到这里（= SearchRowTop）
    private const float ScrollbarX = 1767f, ScrollbarW = 29f;   // 列表竖向滚动条（宽度恒定）
    private const float LetterColX = 1782f, LetterColW = 110f;  // 字母定位条
    private const float LibExpandDur = 0.22f;               // 展开/收起的动画时长（秒）

    // ---------- 空库那一块（第一次装 mod 时给单个导入 / 批量导入两个按钮） ----------
    /// <summary>空库块的整高（提示 60 + 间 20 + 按钮 56 + 余量）</summary>
    private const float EmptyBoxH = 180f;
    /// <summary>整块相对原来那行提示上移 50：块高 180 而内容只占 136，上移 (180−136)/2 + 提示行半高 30 ≈ 52，取 50，
    /// 让整块以原来那行为视觉中心。</summary>
    private const float EmptyBoxUpShift = 50f;
    /// <summary>两个按钮的尺寸与间距（并排居中）</summary>
    private const float EmptyBtnW = 240f, EmptyBtnH = 56f, EmptyBtnGap = 28f;

    // ---------- 导航排的两种状态色 ----------
    // 选中 = 实心主题色块 + 深色字（跟设置页导航块 / 子 tab 选中块同一套语言），未选中 = 斜切暗片 + 冷灰蓝字。
    // 不再有"琥珀 + 下划线"那一套 —— 一条横贯整屏的文字 tab 栏已经拆掉了。
    /// <summary>未选中时的文字色（浅灰蓝；别用纯白）</summary>
    private static readonly Color NavIdleText = new Color(0.84f, 0.86f, 0.92f, 0.88f);
    /// <summary>鼠标压在某一格上时的文字色：纯白 + 不透明。未选中那档原本已经挺亮，靠 uGUI 的乘算提亮
    /// 根本看不出变化（乘出来的是一点亮度），所以悬停由我们自己写色（见 LayoutTopNav 那一行）。</summary>
    private static readonly Color NavHoverText = new Color(1f, 1f, 1f, 1f);

    /// <summary>渲染指定 tab 的页面内容：设置页独立（不参与滑动），其余四页都在页面带里。
    /// tab 编号（另见 SettingsSlotIndex）：0=主页 1=库 2=成就 3=设置 4=开发者。</summary>
    private static void RebuildPage(int tab) {
        try {
            if (tab == SettingsSlotIndex) {
                BuildSettingsPage();
                SetSettingsPageVisible(true);
            } else {
                // 设置页显隐只由「现在停在哪一页」（_topTab）决定，不许由「这一次在建哪一页」（tab）决定 ——
                // 排队预热会在后台建成就/开发者页，原来这里无条件关掉设置页，正停在设置页的玩家就会看到内容整片消失。
                bool onSettings = (_topTab == SettingsSlotIndex);
                SetSettingsPageVisible(onSettings);
                // 建页期间让页面带保持激活：页里的量尺寸/布局组要跟平时一模一样；
                // 设置那一格本来就是空的、槽位视口又是全透明的，这一刻画不出任何东西、盖不住设置页。
                if (_contentRoot != null) _contentRoot.gameObject.SetActive(true);
                // 大厅（独立槽位）：建过了就别重建 —— 重建会把内容销毁重造、树上的光点与视差状态一起归零
                //（`ShowMainScreenInstant` 里也写着"重建会让内容闪没"）。每次从别的页回大厅都会走到这里，
                // 无条件重建 = 白花十几毫秒 + 那几帧的空档。空槽位才建（开面板、面板重建之后）。
                // 重建之外还要武装渐入：早先只有"首次建"和"瞬移显示"两条路会武装，
                // 普通切页进大厅（EnsureArkBuilt 已建过直接返回）从来不武装 = 切进来没有淡入（用户报的）。
                if (tab == MainScreenSlot) { EnsureArkBuilt(); ArmArkFade(); }
                else if (tab == 0) BuildHomePage();             // 主页（官方关卡棋盘）
                else if (tab == 2) BuildAchievementsPage();
                else if (tab == 4) BuildDeveloperPage();
                // tab == 1（库页）在开面板时就建好了，不用重建
                // 建完收尾：玩家还停在设置页就把页面带收回去 —— 它必须压在设置页下面，设置页才看得见
                if (onSettings && _contentRoot != null) _contentRoot.gameObject.SetActive(false);
            }
            _topTabBuilt = tab;
        } catch (System.Exception e) {
            // 页面构建失败：回退到库页，并把原因弹给玩家（否则只会看到"页面是空的"却不知道为什么）
            ReportError($"构建「{TopTabName(tab)}」页面失败，已回退到库页（最后完成的步骤: {_buildStep}）", e);
            _topTab = 1;                                       // 回退到库页
            _topToX = _topFromX = -RefW;
            _topAnimT = 1f;
            _pendingTopTab = -1;
            if (_contentRoot != null) _contentRoot.anchoredPosition = new Vector2(-RefW, 0f);
            _topTabBuilt = 1;
        }
    }

    /// <summary>tab 编号 → 中文名（日志/报错用）</summary>
    private static string TopTabName(int tab) {
        switch (tab) {
            case MainScreenSlot: return "主界面";   // 主界面（有槽位没按钮）
            case 0: return "官谱库";   // 官谱库（原主页）
            case 1: return "库";
            case 2: return "成就";
            case 3: return "设置";
            case 4: return "开发者";
            default: return "库";
        }
    }

}
