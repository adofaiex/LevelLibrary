// [split-of] PortalUI.cs lines 237-592
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

/// <summary>PortalUI 的数据类与全部界面字段：列表池化 / 各类弹窗 / 详情区 / 批量导入 / 标记 / 信息栏。</summary>
public static partial class PortalUI {
    private sealed class LogicalRow {
        public bool IsHeader;          // 标头行
        public string HeaderText;      // 标头文字（收藏 / 字母）
        /// <summary>这一行是不是「清空筛选条件」的出口：只有「库里有谱面、但被筛空」那一行才为 true，
        /// 渲染时把它的文字变成可点。</summary>
        public bool ClearFiltersOnClick;
        public LevelEntry Entry;       // 谱面条目（Item 行）
        public float OffsetY;          // 距 content 顶部的 y 偏移
        public float Height;           // 行高（HeaderRowH / ItemRowH）
    }

    private sealed class PoolRow {
        public GameObject Go;
        public RectTransform Rt;
        /// <summary>这一根当前代表谁（条目行 = 谱面路径 / 标头行 = 标头文字）。
        /// 流式换序按它认人；池子把同一根换给别人时也要靠它把旧身份的位置记录清掉。</summary>
        public string Key;
        public int LogicalIndex = -1;          // 当前显示的逻辑行（-1 = 未分配）
        public bool Offscreen;                 // 已被移到视口外（滚回时需强制重排）
        public GameObject HeaderObj;           // 标头模式
        public Text HeaderText;
        public GameObject ItemObj;             // 条目模式
        public Image Bg;
        public Text Label;
        public Image FavImg;                   // 收藏星（图标族烘的贴图，不是字体字符）
        public RectTransform FavIconRt;        // 收藏图标容器（缓存，避免每行 GetComponent）
        public Image MarkIcon1, MarkIcon2;
        public Text Author;                     // 作者小字（曲名下方）
        public Button ItemBtn;
        public ButtonHover Hover;
        public bool IsHeaderMode;
    }

    private static readonly List<LogicalRow> _logicalRows = new();
    private static readonly List<PoolRow> _poolRows = new();
    /// <summary>当前这一屏「哪个逻辑行由哪根池行显示」的认领表：下标 = 逻辑行下标 − 可视区首行，值 = 池行下标（-1 = 还没人显示）。
    /// 每帧重建，长度不超过池子大小。它让「已经显示着某个逻辑行的那根池行」下一帧继续显示同一行，
    /// 于是滚动时只有滚出视口的那一根会换人 —— 换人越少，指针悬停状态错位的机会就越少。</summary>
    private static readonly List<int> _libSlotOf = new();
    /// <summary>列表行对象池当前行数（PerfDiag 用；池固定 24 行，涨了就是异常）。</summary>
    public static int PoolRowCount { get { return _poolRows.Count; } }
    private static bool _listDirty;            // 下一帧更新可见行（首帧布局未完成 viewport 高度为 0）
    private static bool _vzLoggedOnce;         // 诊断：首屏行信息只 Log 一次
    private static bool _scrollTopLogged;      // 诊断：滚到顶只 Log 一次
    private static float _listViewportH;       // 列表可视区高度
    private const float HeaderRowH = 56f;      // 标头行高（分组标头，比条目行矮一档但要有呼吸）
    private const float ItemRowH = 96f;        // 条目行高（曲名 + 作者 + 标记图标；行内元素按比例锚定，跟着一起长高）
    private const float RowGap = 12f;          // 谱面行之间的视觉间隔（Bg 高 ItemRowH-RowGap，逻辑高度不变）

    private static LevelEntry _renameEntry;    // 正在重命名的谱面
    private static string _renameText = "";

    private static LevelEntry _deleteEntry;    // 正在确认删除的谱面
    private static bool _deleteFiles = true;   // 删除时是否同时删本地文件
    private static Canvas _deleteCanvas;

    private static Canvas _missingCanvas;      // 文件缺失提示

    // 设置页字段（_settingsPage 等）定义在 PortalUI.Pages.cs
    private static Button _markToggle;

    private static Canvas _toastCanvas;        // 轻提示（谱面重复等）
    private static Text _toastText;
    private static float _toastTimer;

    // ---------- 批量导入（扫描文件夹） ----------
    private static Canvas _batchCanvas;
    private static Image _batchFill;
    private static Text _batchText;
    private static RectTransform _batchBoxRt;   // 进度框底板（最近几行挂在它下面）
    private static readonly List<string> _batchFolders = new();
    private static int _batchIndex;
    private static int _batchTotal;
    private static bool _batchRunning;                                         
    private static bool _batchScanDone;                                        
    private static bool _batchConfirmShown;   // 扫描完成确认框是否已弹
    private static bool _batchWaitingConfirm; // 等待确认框结果（确认前不导入）
    private static Canvas _batchConfirmCanvas; // 批量导入确认框
    private static string _batchRoot;          // 本次扫描根目录（确认框显示用）

    private static Canvas _pickCanvas;         // 选择关卡文件弹窗（文件夹里所有 .adofai）
    private static LevelEntry _pickEntry;      // 弹窗对应的谱面条目
    private static string _pickFolder;
    private static readonly List<string> _pickFiles = new();
    private static string _pickedFile;         // 弹窗里玩家选中的 .adofai
    private static readonly List<GameObject> _pickItems = new();
    private static Text _pickInfoText;         // 选择弹窗左侧：数据滚动区文本（长度/作者/BPM/格子数）
    private static Text _pickCompletionText;   // 选择弹窗左侧：完成度（难度行右侧，不随数据滚动）
    private static Text _pickNameText;         // 选择弹窗左侧：音乐名称（固定）
    private static Image _pickMarkIcon;        // 选择弹窗左侧：难度标记图片
    private static InputField _pickNoteInput;   // 选择弹窗左侧：备注输入框
    private static string _pickNoteFile;        // 当前备注对应的文件
    private static bool _pickNoteDirty;          // 备注有未落盘的输入
    private static float _pickNoteSaveTimer;     // 防抖计时
    // ---------- 「关卡选择弹窗」专属容器（_pickWin* 前缀）----------
    // 绝不能与详情面板的字段共用：共用会让 CloseLevelPickDialog 把详情面板的字段置 null，
    // 详情面板重建出第二套内容容器，新旧两套同时显示（「选择关卡」里叠着「通关记录」的内容）。
    private static GameObject _pickInfoArea;
    private static GameObject _pickWinHeatArea;
    private static GameObject _pickWinDataArea;
    private static GameObject _pickWinRecordArea;
    private static GameObject _detailHeatArea;                // 详情区 tab2 寄录图容器
    private static GameObject _pickDataArea;                  // 详情区 tab3 数据管理容器
    private static GameObject _pickRecordArea;                // 详情区 tab0 通关记录容器
    private static GameObject _recListShell;                  // 详情区通关记录列表的壳（只建一次：行要留在里面让位与退场）
    private static RectTransform _recListContentRt;           // 壳里的内容盒（行位置全手工算，见 PortalUI.RecordList）
    private static int _detailTab;               // 主界面左侧详情区当前 tab：0=通关记录 1=选择关卡 2=寄录图 3=数据管理
    private static RectTransform _detailContent; // 详情区 tab 内容容器
    private static Image _detailMarkIcon;        // 详情区难度图标
    private static Text _detailTitle;            // 详情区曲名
    private static readonly List<Image> _detailSpecialIcons = new();   // 详情区等级图标右边的特殊图标（最多2个）
    private static Image _detailFavImg;          // 详情区收藏星（图标族烘的贴图）
    private static Button _detailFavBtn;         // 详情区收藏星按钮（可点击切换收藏）
    private static LevelEntry _detailEntry;      // 详情区当前显示的谱面（供收藏星点击等用）
    private static Text _detailMeta;             // 详情区谱师·BPM·时长
    private static readonly List<Button> _detailTabs = new();   // 详情区 tab 按钮
    private static GameObject _detailFileArea;   // 选择关卡 tab 内容区
    private static string _detailPickedFile;     // 选择关卡 tab 里选中的 .adofai
    private static readonly List<GameObject> _detailFileItems = new();   // 选择关卡 tab 文件行
    private static Transform _detailFileContent;   // 选择关卡 tab 滚动列表的内容容器（行都挂这儿）
    private static string _detailFileFolder;       // 这份文件列表是给哪个谱面文件夹建的（换谱面就重建）
    private static Text _detailScore;            // 详情区通关数|完成度
    private static Image _batchProgBar;          // 批量评级进度条背景
    private static Image _batchProgFill;         // 批量评级进度填充
    private static Text _batchProgText;          // 批量评级进度文字
    private static string _bgmCustomPath;        // 自定义背景音乐路径（null=用默认）
    /// <summary>自定义背景音乐路径（settings.txt 持久化；空 = 用 assets\bgm_default.mp3）。</summary>
    public static string BgmPath = "";
    /// <summary>背景音乐来源谱面文件夹（只有这个谱面的按钮显示「取消」，其他显示「设为背景音乐」）。</summary>
    public static string BgmSource = "";

    // ---------- “正在播放”信息栏设置 ----------
    public static bool NowBarShow = true;          // 信息栏显示开关（关=整体隐藏）
    public static int NowBarMode = 1;              // 信息栏内容（1=滚动歌名 2=自定义文本滚动）
    public static string NowBarCustomText = "";    // 自定义滚动文本

    // 详情区的备注框已删；数据层 notes.txt / GetNote / SetNote 仍在，「选择关卡」弹窗那个备注框还在用。
    private static bool _pickRecordDirty;         // 选中新谱面但当前 tab 非通关记录 → 切到 tab0 时刷新
    private static GameObject _detailFixedZone;    // 详情区固定区（选中后显示）
    private static GameObject _detailTabRow;       // 详情区 tab 行（选中后显示）
    private static Button _detailEnterBtn;         // 详情区进入编辑器按钮（选中后显示）
    private static Button _pickTabInfo, _pickTabHeat, _pickTabData, _pickTabRecord;   // 左侧 tab 按钮
    private static int _pickTab;                 // 0=谱面信息 1=通关记录 2=寄录图 3=数据管理
    private static ScrollRect _recordScroll;     // 通关记录列表
    private static Text _recordEmpty;            // 通关记录空提示
    private static Image[] _heatLines;           // 热力图折线段（10 条）
    private static Image[] _heatDots;            // 热力图数据点（11 个：0%,10%..100%）
    /// <summary>上升动画里当前各点坐标（TickHeatGrow 每帧算给点用、给线算中点用）。
    /// 原来是每帧 new Vector2[11] —— 数组是纯中间量、每次内容都被整份覆写，提到静态复用即可。</summary>
    private static readonly Vector2[] _heatCur = new Vector2[11];
    private static Text _heatStats, _heatEmpty;  // 热力图统计/空提示
    private static Text _heatPeakLabel;          // 峰值标注（最难点上方）
    private static Text[] _heatAxisY;            // Y 轴刻度（刷新时更新为真实次数）
    private static string _heatFolder;           // 当前热力图对应的文件夹
    private static Canvas _dataConfirmWin;        // 数据清空确认窗
    private static Canvas _pickConfirmCanvas;  // 删除文件确认弹窗
    private static string _pickDeleteFile;     // 待删除的 .adofai

    // ---------- 主界面背景音乐 ----------
    private static GameObject _musicGo;
    private static MusicPlayer _musicPlayer;
    private static readonly List<AudioSource> _pausedSources = new();   // 打开面板时暂停的游戏音源
    private static bool _bgmWanted;            // 面板开着的时候，咱的背景音乐应该是"响着"的
    /// <summary>关面板时的 BGM 淡出：>= 0 = 淡出进行中（已进行的秒数），-1 = 没在淡出。
    /// 关面板不再瞬停 —— 先把音量线性压到 0 再 Stop，不然音乐"啪"一下没了很突兀。</summary>
    private static float _bgmFadeT = -1f;
    private static float _bgmFadeFrom = 1f;     // 淡出起始音量（结束时还原，下次播不受影响）
    private const float BgmFadeSeconds = 0.6f;

    /// <summary>玩家随机 ID（大厅 "ID: xxx" 那一行）。每位玩家第一次进游戏时随机生成一次、
    /// 当场落盘（settings.txt 的 PlayerRandomId），之后固定不变。9 位数字、前导补零。</summary>
    public static string PlayerRandomId;
    private static float _bgmWatchdogAt;       // 下一次音乐健康检查的时间
    // ---------- 面板关闭时的体征快照（日志里能直接看出是 bpm 还是 timeScale 的锅） ----------
    private static string _lastHealthState = "";
    private static float _healthLogAt;         // 限速：最多每 2 秒写一行

    public static int MusicVolume = 100;   // 背景音乐音量 0-150（%）
    /// <summary>背景压暗遮罩的透明度 0-100（%）。面板里背景（纯色底/神经图/静态图/视频）之上那层黑的 alpha，
    /// VeilTargetFor / 开面板建遮罩 / 进场过渡遮罩三处都从这一个值取（改一处三处同步）。</summary>
    public static int BgVeilPercent = 25;
    public static int ListFontSize = 32;   // 谱面列表字体大小 20-44（个性化设置）
    /// <summary>谱面列表的排序方式：false = 字母分组（默认，收藏置顶 + # + A-Z）；
    /// true = 按标记难度（P1→P20→G1→G20→U1→U20，未标记的放最下面，Q 图落进 U 档）。
    /// 点一下按钮就立刻落盘（settings.txt 的 SortByDifficulty）；它是全局唯一的一份顺序，库列表 / 关卡选择弹窗 / 随机进谱面共用。</summary>
    public static bool SortByDifficulty;
    // 范围：成就名称 18~34（卡片名字框高 46，再大就压到下面的分隔线）、
    // 成就详细信息 14~22（多行区高 87，22 时三行约 63px，更长的那几条靠 bestFit 自动缩）。
    public static int AchNameFontSize = 24;
    public static int AchDetailFontSize = 18;
    /// <summary>界面字体大小的全局比例（1 = 原样，可调 0.85~1.30）：设计尺上的字号都乘它，
    /// 实际像素由 UiFontPx 算；设置页那条滑条改的就是它。老玩家读不到这个键时按 1 算。</summary>
    public static float UiFontScale = 1f;
    /// <summary>面板背景模糊 0-100（0 = 关、100 = 最糊）：各页遮罩的深浅都按它走（= PanelAlphaFloor 那一档，
    /// 它取代了原来的「面板背景透明度」，那个字段已删）。实现全在 PortalUI.PanelGlass.cs。
    /// 数值喂给与开始界面过渡同一个斜坡（档数由那条 8 像素红线算出）：0 = 全关，100 = 最深那档铺满。</summary>
    /// <summary>面板磨砂强度（0~100）。固定满值、没有滑条 —— 设置里那一行已删，
    /// settings.txt 里的 PanelBlur 也不再读：磨砂要么全开、要么看着像没生效，留个滑条只是多一个能调坏的地方。</summary>
    public const int PanelBlur = 100;

    /// <summary>把那一档固定的系数应用到"详情区底色"和"谱面列表底色"（开面板 / 建页时调用）</summary>
    private static void ApplyPanelBackdropAlpha() {
        float k = PanelBackdropK();
        if (_detailAreaImg != null) {
            var b = _detailAreaImgBase;
            _detailAreaImg.color = new Color(b.r, b.g, b.b, b.a * k);
            RefreshGlassBaseline(_detailAreaImg);   // 磨砂的基准必须跟着改，不改的话那块只剩四成糊（见 RefreshGlassBaseline）
        }
        if (_listViewportImg != null) {
            var b = _listViewportImgBase;
            _listViewportImg.color = new Color(b.r, b.g, b.b, b.a * k);
            RefreshGlassBaseline(_listViewportImg);
        }
        // 这两页的遮罩各建各的（主页 5 块 + 设置页 2 块 + 成就页若干），建的时候登记进来，
        // 这里统一按同一个 k 缩 alpha —— 只有一个 k 的算法，绝不各页各算一份。
        for (int i = _alphaBackdrops.Count - 1; i >= 0; i--) {
            var img = _alphaBackdrops[i];
            if (img == null) { _alphaBackdrops.RemoveAt(i); _alphaBackdropBase.RemoveAt(i); continue; }   // 页面重建过就清掉
            var b = _alphaBackdropBase[i];
            img.color = new Color(b.r, b.g, b.b, b.a * k);
            RefreshGlassBaseline(img);   // 和上面两块一样：磨砂的基准必须跟着改。漏了这一句，这块的磨砂只剩四成（见 RefreshGlassBaseline）
        }
    }

    /// <summary>各块遮罩底色固定的那一档系数（= 透明度滑条 100% 那档，现在恒定）。
    /// 别再让它跟着任何滑条走 —— 那条滑条已经换成「面板背景模糊」了。</summary>
    private static float PanelBackdropK() {
        return PanelAlphaFloor;
    }

    /// <summary>登记一块要跟着面板遮罩系数走的遮罩底色（建遮罩的时候调）。
    /// 页面每次重建都会重新登记，旧的那些（已销毁）在下一次 Apply 时自动剔除。
    /// 它只管深浅 —— 「要不要磨砂」是另一件事、另一句 RegisterGlassPanel。</summary>
    private static void RegisterAlphaBackdrop(Image img) {
        try {
            if (img == null || _alphaBackdrops.Contains(img)) return;
            _alphaBackdrops.Add(img);
            _alphaBackdropBase.Add(img.color);          // 记原始 alpha（每次都是拿它乘 k，不叠加）
            float k = PanelBackdropK();
            img.color = new Color(img.color.r, img.color.g, img.color.b, img.color.a * k);
        } catch (System.Exception e) { Lg.Ex("登记遮罩透明度失败", e); }
    }

    private static readonly List<Image> _alphaBackdrops = new List<Image>();
    private static readonly List<Color> _alphaBackdropBase = new List<Color>();

    /// <summary>玩家头像路径（settings.txt 持久化；为空画淡灰圆）</summary>
    public static string AvatarPath;
    /// <summary>主题色是不是玩家自己在设置里选过（settings.txt 的 ThemePicked）。没选过就是"从背景图提取"那套
    /// 老颜色，启动时回落到默认蓝；选过一次之后一直按文件走。</summary>
    public static bool ThemePicked;
    /// <summary>玩家名字（settings.txt 持久化；显示在头像旁）；未设置过时用 DefaultPlayerName</summary>
    public static string PlayerName;
    /// <summary>身份（个人页「欢迎回来，身份，用户名」中间那个；空 = 不显示）。
    /// 登录是本地昵称、不联网，所以身份也是本地手填；settings.txt 的 PlayerTitle。</summary>
    public static string PlayerTitle;
    /// <summary>默认玩家名字（首次安装、settings.txt 里没有名字时用）</summary>
    public const string DefaultPlayerName = "User";
    /// <summary>界面外显示：打开之后玩家信息框在任何界面都显示（含 mod 面板里，此时面板内那份会自己藏起来，
    /// 免得同一块框出现两次）；默认开，老玩家的旧值由一次性覆盖补上。</summary>
    public static bool ShowPlayerBoxOutside = true;
    /// <summary>个人信息栏总开关：关掉之后面板内 / 界面外都不显示（settings.txt 持久化）</summary>
    public static bool ShowPlayerBox = true;
    /// <summary>「游玩时隐藏」：打谱时把（界面外那份）个人信息栏藏起来；默认关，老玩家不该被改行为。
    /// 判据复用 IsLevelPlaying()，也就是「游玩时禁用快捷键」那一套：!paused && isGameWorld。</summary>
    public static bool HidePlayerBoxInGame;
    /// <summary>玩家信息框 X 位置（0-1 屏幕比例，默认 0.2 = 左侧）。</summary>
    public static float PlayerBoxX = 0.2f;
    /// <summary>玩家信息框 Y 位置（0-1 屏幕比例，默认 1.12 = 贴顶再往上一档）。</summary>
    public static float PlayerBoxY = 1.12f;
    /// <summary>面板遮罩（固定 0.45 黑罩）。</summary>
    private static Image _panelVeilImg;
    /// <summary>导入命名规则选择框 Canvas。</summary>
    private static Canvas _importNameCanvas;
    /// <summary>更新检查（设置 → 工具 → 更新）的控件引用。</summary>
    private static Button _autoImportToggle;
    /// <summary>设置页「单个导入也接受压缩包」那一行的开关（要留引用，否则重建时旧的要被销毁掉）。</summary>
    private static Button _singleImportZipsToggle;
    /// <summary>「自动导入后备份」开关（只管"打开一个新谱面被自动录进库"那条路，别跟 `_backupImportToggle` 混）</summary>
    private static Button _autoImportBackupToggle;
    private static Text _updStatusText;
    /// <summary>更新状态行"上一次真的写进去的文字"：用来避免每帧重复赋值触发 Text 重建，白费性能</summary>
    private static string _updStatusShown = "";

    /// <summary>改名弹窗（LevelLib_Name 画布）。</summary>
    private static Canvas _nameCanvas;

    // ---------- 设置页三级 tab 体系 ----------

    private static Button _launchTakeoverToggle;           // 开机接管进入开关（原来的两个开机开关并成的这一个）
    private static Button _saverLibToggle;                 // 屏保开关（开始界面作为屏保，谱面库内）
    private static Button _saverGlobalToggle;              // 屏保开关（屏保全局显示）
    private static Text _saverIdleBtnText;                 // 屏保休眠时间按钮上的文字（点一下换档要刷新）

    private static LevelEntry _markEntry;      // 正在设置标记的谱面
    private static Canvas _markCanvas;
    private static int _markTab;               // 标记 tab：0=P 1=G 2=U 3=UJQ 4=特殊
    private static string _pendingMark;        // 暂存的主标记（点确定才应用）
    private static string _pendingJudge;       // 暂存的判定标记
    private static string _pendingSpecials;    // 暂存的特殊标（"sp1|sp2"，最多2个；点确定才应用）
    private static readonly List<GameObject> _markItems = new();
    private static readonly List<Button> _markTabButtons = new();   // tab 按钮（刷新高亮）

    // 主界面筛选：等级区间（双滑块）+ 判定三选一
    /// <summary>轨道基准宽（固定值，防首帧布局漂移；与轨道 LayoutElement.preferredWidth 一致）。</summary>
    private const float RankTrackW = 360f;

    /// <summary>等级区间下限（0=P1、59=U20，默认全选；持久化到 settings.txt）。</summary>
    public static int RankMin { get => _rankMin; set { _rankMin = Mathf.Clamp(value, 0, 59); if (_rankMin > _rankMax) _rankMax = _rankMin; } }
    /// <summary>等级区间上限</summary>
    public static int RankMax { get => _rankMax; set { _rankMax = Mathf.Clamp(value, 0, 59); if (_rankMax < _rankMin) _rankMin = _rankMax; } }
    private static int _rankMin;
    private static int _rankMax = 59;

    // “现在播放”滚动条（右上角显示正在播放的歌名，支持滚动）
    private static Image _nowBox;                                // 框背景
    private static RectTransform _nowTextRt;                     // 滚动文字容器
    private static Text _nowText;                                // 滚动文字（歌名）
    private static RectTransform _nowMaskRt;                     // 裁剪区（RectMask2D）
    private static float _nowScrollTimer;                        // 滚动进度
    private static float _nowTextWidth;                          // 缓存文字宽（歌名变化时算，避免每帧 preferredWidth）
    private static RectTransform _rankTrackRT;              // 轨道 RectTransform（拖动手势用）
    private static Image _rankHandleA, _rankHandleB;         // 左右滑块（自绘，EventTrigger 拖动）
    private static Image _rankRangeImg;               // 区间高亮条 Image（首帧隐藏用）
    private static RectTransform _rankRangeRT;               // 区间高亮条
    private static Text _rankLabel;                          // 难度滑条的数值文字（居中显示在轨道上）
    private static bool _rankRangeDirty;                     // 布局完成后需重算滑块位置（首帧宽度未定）

    private static string _pendingOpenPath;    // 等待编辑器载入的谱面
    /// <summary>进编辑器前记下目标谱面，用来判断编辑器有没有真的把我们的谱面读起来。
    /// 只看 ADOBase.levelPath / customLevel.levelMaker 非空是不够的：编辑器开着空白关时 levelMaker 也可能非空，
    /// 于是误判「已就绪」、清掉 _editorLoading，兜底再也不触发、停在空白编辑器。</summary>
    private static string _expectedOpenPath;
    private static int _editorReloadTries;     // 兜底重试次数（每次间隔 1.5s，最多 4 次）
    private const int EditorReloadMaxTries = 4;
    private static float _editorWaitTimer;
    private static bool _editorLoading;

    /// <summary>等待期的 scnEditor 缓存（按场景名分键）。
    /// 等待期每帧要拿编辑器做兜底重开，而 FindFirstObjectByType 是「全场遍历」——
    /// 等待期最长 EditorReloadMaxTries(4) × 1.5s ≈ 6 秒，按 60fps 算是几百次全场搜索。
    /// 场景一切换旧引用就失效（Unity 假 null），所以拿场景名当 key：名字对不上就重新搜一次。
    /// 这里存的是"上一次搜到的"，判空交给调用处的 Unity == null（被销毁时它为真）。</summary>
    private static scnEditor _editorCached;
    private static string _editorCachedScene = "";
    private static Font _uiFont;

    /// <summary>谱面库快捷键（默认 左Alt，可在 UMM 设置页更换）</summary>
    public static KeyCode Hotkey = KeyCode.LeftAlt;
    /// <summary>快捷键：打开选中谱面（None = 未设置）。</summary>
    public static KeyCode OpenLevelKey = KeyCode.None;
    /// <summary>批量导入时「同时导入压缩包」（settings.txt 的 BatchImportZips）。
    /// 开着的话：扫描时把目录树里的 *.zip 逐个解压到系统临时目录、把含 .adofai 的目录一起导入，之后删掉临时目录，压缩包本身不删。</summary>
    public static bool BatchImportZips;

    /// <summary>单个导入时「也接受 .zip 压缩包」（settings.txt 的 SingleImportZips）。
    /// 与批量那套同口径：解压到系统临时目录（绝不写进玩家的谱面目录树），导入后删临时目录，压缩包本身不删。
    /// 默认开 —— 玩家从网上拿到的谱面大多是压缩包，让他在文件对话框里翻文件夹再一个个导很别扭。</summary>
    public static bool SingleImportZips = true;

    /// <summary>快捷键：随机谱面并打开（None = 未设置）。</summary>
    public static KeyCode RandomLevelKey = KeyCode.None;
    /// <summary>当前要捕获的键："open" / "random" / null；由 UMM OnGUI 读取。</summary>
    public static string CaptureTarget;
    private static KeyCode _lastOpenKey;      // 诊断：记录上次绑定的打开键
    private static KeyCode _lastRandomKey;    // 诊断：记录上次绑定的随机键
    private static RectTransform _keyHintBar;   // 底部快捷键提示：一排「说明 + 键帽」的小件
    /// <summary>热键冲突黑名单：这些键被 ADOFAI 占用，设置后无法触发；存 settings.txt，可扩充。</summary>
    public static readonly HashSet<KeyCode> ConflictedKeys = new HashSet<KeyCode>();

}
