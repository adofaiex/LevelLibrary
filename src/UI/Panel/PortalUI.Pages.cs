// [split-of] PortalUI.cs lines 1827-1937
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

/// <summary>PortalUI 的页面槽位与顶栏引用、界面状态沿用、列表钉顶、验证任务字段。</summary>
public static partial class PortalUI {
    private static RectTransform _contentRoot;
    /// <summary>页面槽位数（库 / 设置 / 开发者 + 1 个备用）</summary>
    // 槽位 5 = 主界面，它不进顶部 tab 栏，因此没有按键，只能靠 Esc 退回或开场进入。
    private const int PageSlotCount = 6;
    /// <summary>主界面的槽位号（不进 tab 栏）</summary>
    public const int MainScreenSlot = 5;
    /// <summary>并排的页面槽位（页 i 的左边 = i × 1920）</summary>
    private static readonly RectTransform[] _pageSlots = new RectTransform[PageSlotCount];
    /// <summary>每个槽位内部的页面内容根（带裁剪视口）</summary>
    private static readonly RectTransform[] _pageRects = new RectTransform[PageSlotCount];

    /// <summary>库页根容器（谱面库那一套界面）</summary>
    private static GameObject _libRoot;
    /// <summary>设置页根容器（全屏，随切页销毁重建）</summary>
    private static GameObject _settingsPage;
    /// <summary>开发者页根容器（随切页销毁重建）</summary>
    private static GameObject _devPage;
    /// <summary>面板画布上的"顶部那一层"的矩形：现在是右上角那排导航小方块（见 PortalUI.Nav.cs）。
    /// 两处外部引用还挂着它 —— 信息栏建在它下面（不压住导航）与启动页把整层淡出。</summary>
    private static RectTransform _tabBarRt;
    /// <summary>顶部那一层的 GameObject（设置页打开时提到最上层用）</summary>
    private static GameObject _topTabRoot;
    /// <summary>当前顶层 tab（0 主页 / 1 库 / 2 成就 / 3 设置 / 4 开发者；设置页不在页面带里，见 SettingsSlotIndex）</summary>
    private static int _topTab;

    // ---------- 界面状态沿用 ----------
    // 记三个值供重新打开面板时沿用：顶层 tab、选中谱面（记 FolderPath —— 路径稳定，下标会随筛选/排序变）、
    // 列表滚动位置（记归一化值 0~1，换分辨率 / 换字号都不跑偏）。
    public static int LastTopTab;
    public static string LastSelectedPath;
    public static float LastLibScroll;
    /// <summary>待恢复的滚动位置：面板刚建好时 ScrollRect 的内容高度还没算出来，
    /// 只能记下来在 TickPanel 前几帧里落实；-1 = 没有待恢复的。</summary>
    private static float _libPinT;                 // 钉顶已持续多久（滚轮释放要看它）
    private static bool _libPinTop;                // 开面板后"钉顶"中，一直钉到玩家自己滚动为止
    private static bool _libPinHeld;               // 钉顶期间左键是否按住（判"真实拖拽"用）
    private static float _libPinMouseY;            // 按下的那一刻鼠标 y（真实位移 = 现在 - 它）
    private const float LibPinDragPx = 4f;         // 纵向位移超过这个像素数才算玩家真的在拖，点一下不算
    private static float _libScrollRestore = -1f;
    private static int _libScrollRestoreFrames;
    /// <summary>库页列表的 ScrollRect（只在库页槽位下找，不能用 GetComponentInChildren：设置页也有 ScrollRect）</summary>
    private static ScrollRect _libScroll;
    /// <summary>库页列表的滚动条（手柄长度按"视口/内容"实时算）</summary>
    private static Scrollbar _libScrollbar;
    /// <summary>已渲染的 tab（切页重建用）</summary>
    private static int _topTabBuilt = -1;
    /// <summary>顶层 tab 动画：起始位置、目标位置、计时、时长、待处理目标</summary>
    private static float _topFromX, _topToX, _topAnimT = 1f, _topAnimDur = 0.34f;
    private static int _pendingTopTab = -1;
    /// <summary>设置页滑入/滑出动画：设置页不在页面带里，单独让它从右侧滑进来</summary>
    private static bool _settingsSlideActive;
    private static float _settingsFromX, _settingsSlideCur;
    private static bool _topAnimDoneLogged;   // 动画结束日志只打一次

    // ---- 用户自定义背景等 ----
    private static GameObject _settingsKeys;               // 设置弹窗：快捷键区
    private static Button _outsideToggle;                  // 界面外显示开关
    private static Button _playerBoxToggle;                // 个人信息栏总开关
    private static Button _backupImportToggle;             // 谱面导入后备份开关
    private static Button _verboseLogToggle;               // 详细日志开关（诊断用）
    private static Button _nowShowToggle;
    /// <summary>视频背景的开关行（类型必须是 Button：SettingsToggleRow 吐的就是 Button，写 Toggle 编译不过）</summary>
    private static Button _videoBgToggle;

    // ---- 重复谱面检验 ----
    private static volatile bool _verifyRunning;            // 检验进行中（跨线程）
    private static volatile bool _verifyCancel;            // 取消标志（后台线程检查）
    private static volatile float _verifyProgress;         // 0~1 进度（后台线程写）
    private static volatile int _verifyGen;               // 检验代际（取消/重启时作废旧线程结果）
    private static Canvas _verifyConfirmWin, _verifyProgressWin;   // 确认窗 / 进度窗
    private static Image _verifyFill;                      // 进度条填充
    private static Text _verifyInfo;                       // 进度/状态文字
    private static List<LevelEntry> _verifyResults;        // 扫描出的重复谱面（扫描完成后弹列表）
    private static bool _verifyApplyPending;               // 后台扫描完成，主线程弹重复列表
    private static bool _verifyAuto;                       // 本次检验是否由"批量导入完成后"自动触发
    private static bool _batchAutoVerifyPending;           // 批量导入完成后是否自动接重复检验
    private static bool _batchAutoRatePending;             // 重复检验完成后是否自动接评级（同一条链的第三段）
    /// <summary>给 UMM 设置页用：点"扫描文件夹批量导入"时置位，导入完成后自动接重复检验</summary>
    public static bool RequestAutoVerifyAfterBatch {
        get { return _batchAutoVerifyPending; }
        set { _batchAutoVerifyPending = value; }
    }
    /// <summary>给 UMM 设置页用：是否正在批量导入</summary>
    public static bool IsBatchRunning => _batchRunning;

    // ---- 重复谱面列表界面（扫描后弹窗，多选删除）----

    private static void RefreshFilterButtons() { }

    /// <summary>行尾文字按钮（两行布局）：同一行两个按钮，右缘对齐，悬停变色</summary>
}
