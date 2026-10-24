// 大肥鱼宠物：屏幕上的挂件（身体 / 气泡 / 台词 / 音效 / 拖动吸附都在这个文件里）。
// 外形与动效参数抄自 dsh-whale-widget 插件（按压 scaleY 0.88 + scaleX 1.05、变换原点在底边中央、
// 三片气泡 0/.13/.26s 错峰、字号档），改这些数之前先回去看它的实现。
// 素材在 assets\pet\：DSniang1.png（本体）/ bubble.png（气泡）/ Ya1.wav、Ya2.wav（按下、松开）。
using System.Collections;
using System.Collections.Generic;
using System.Reflection;          // AudioClip.SetData(float[],int) 只能反射拿（见 ProbeSetData）
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {

    // ================= 设置（settings.txt 持久化，见 AutoFeatures 的读写）=================
    /// <summary>宠物总开关；关掉 = 完全不建画布、不占资源。</summary>
    public static bool PetShow = true;
    /// <summary>全局显示：开 = 面板关着也浮在游戏画面上；关 = 只在谱面库面板里出现。</summary>
    public static bool PetGlobal = true;
    /// <summary>游玩时隐藏（打谱时整只藏起来），判据复用 IsLevelPlaying()（= 游玩时禁用快捷键那一套）。
    /// 默认 false（不初始化）：老玩家的行为不该被改。</summary>
    public static bool PetHideInGame;
    /// <summary>气泡开关；关了就只有宠物、不弹气泡。</summary>
    public static bool PetBubbleOn = true;
    /// <summary>自动吸附：开（默认）= 松手时按 0.25/0.75 分区吸到最近的边；关 = 停在原地，
    /// 但依然会被夹在屏幕内（自由 ≠ 能拖丢）。</summary>
    public static bool PetSnapEdge = true;
    /// <summary>音效开关；关了就不播 Ya1/Ya2，动画照旧（静默降级）。</summary>
    public static bool PetSoundOn = true;
    /// <summary>音量 0~1</summary>
    public static float PetVol = 0.8f;
    /// <summary>大小倍率 0.5~2.5（跟参考实现的滑块范围一致）</summary>
    public static float PetScale = 1f;
    /// <summary>位置：屏幕归一化锚点（换分辨率不会跑出屏幕）</summary>
    public static float PetX = 0.90f, PetY = 0.16f;
    /// <summary>随机发言档位：0 = 无（默认，不初始化）/ 1 = 每秒 1% / 2 = 每秒千分之一，
    /// 概率表与文案见 PetRandomSayRates / PetRandomSayText()。宠物可见时每秒掷一次骰子 ——
    /// 可见性由 TickPet 里 PetWanted() 那个早退管着，PetTickRandomSay 排在它后面（不可见就不掷、不攒话）。</summary>
    public static int PetRandomSay;
    /// <summary>选的宠物模型（`PetModels` 的下标）。越界由 `ResolvePetModel()` 夹回 0（大肥鱼）。</summary>
    public static int PetModel;

    private const float PetBubbleSeconds = 3f;      // 气泡自动关闭（秒；参考实现是 5 秒）
    /// <summary>转身（镜像朝向）的动画时长 —— 抄插件 `.dshwv-root` 的 `transition: transform .3s ease`。</summary>
    private const float PetFaceTurnSeconds = 0.3f;
    /// <summary>转身的单帧步进上限：卡顿帧（一次几百毫秒）不许把这次转身一步冲完，否则又变成"闪现"。
    /// 跟界面淡入淡出那条 UiFadeMaxStep 同一个道理。</summary>
    private const float PetFaceMaxStep = 0.05f;
    private const float PetOpenDelay = 0.25f;       // 松手后等按压缩放动画放完再显示气泡（秒）
    private static readonly int PetSortOrder = 85000;   // 面板 80000 之上、启动页 93000 之下

    // ---- 气泡层（bubble.png —— 从 DSniang02 里裁出来的、只有气泡那一块）在"头部坐标系"里的位置/大小 ----
    // 单位 = 头部边长 S；y 向下为正。
    // 这一层只放气泡：整张 DSniang02 里还带着一个头，按压时身体压、它不压，屏幕上就会出现"两个头，只动一个"。
    /// <summary>大肥鱼那套气泡框位置（量出来的；模型表里下标 0 用它，别在表里再抄一遍数）。
    /// 单位 = 渲染宽度 S，原点 = 素材左上角，y 向下。</summary>
    private const float PetBubbleX = -0.5885f, PetBubbleY = -0.6803f;
    /// <summary>气泡框的参照尺寸（bubble.png 850×690 ÷ 610）：大肥鱼用它，也是"自带气泡塞进哪个框"的参照
    /// （与 `PetModelPack.RefBubW/RefBubH` 同值）。每个模型实际用的大小是 `PetModelDef.BubW/BubH`。</summary>
    private const float PetBubbleW = 1.3934f, PetBubbleH = 1.1311f;
    /// <summary>大肥鱼那套文字框中心（量出来的，与 PetBubbleX/Y 同一套坐标）。</summary>
    private const float PetTextCX = 0.0656f, PetTextCY = -0.2516f;
    /// <summary>文字框中心在气泡框里的相对位置（0~1）—— 文字是装在气泡里的，所以它跟气泡框走、不跟素材走：
    /// 换成比例不同的模型或换一张自带气泡时这两个数一个都不用改（由上面那组绝对值对着参照框推出来）。</summary>
    private const float PetTextCxK = (PetTextCX - PetBubbleX) / PetBubbleW;
    private const float PetTextCyK = (PetTextCY - PetBubbleY) / PetBubbleH;
    private const float PetTextW = 1.0123f;
    // 文字框高（× 头长）：要装得下最长那条台词折出来的 4 行 —— 头长 250 时框高 155px、4 行约 150px。
    // 框的四角本来就在气泡椭圆外面（(x/a)²+(y/b)² = 1.10），要紧的是真画出来的那几行落在椭圆里。
    private const float PetTextH = 0.62f;
    /// <summary>bestFit 的保险丝下限：正常只有异常长的一句话才碰到。不许写死成 14 —— 那等于又回到"把长句压小"。</summary>
    private const int PetTextFuseMin = 16;

    // ================= 气泡错峰出现：先两个小泡、再大气泡 =================
    // 打开：远的小泡(0s) → 近的小泡(.13s) → 大气泡(.26s)；关闭反过来（.10/.20/.30s），
    // 延迟都从"开/关那一刻"起算。每片绕自己的中心从 scale 0.7 淡入放大，文字不参与这份缩放。
    // bubble.png 是 850×690，下面四块子矩形把它的可见像素恰好划完（覆盖 0 漏、重叠 0 处）。
    private const int PetBubTexW = 850, PetBubTexH = 690;
    private const int PetBubBigX = 0, PetBubBigY = 0, PetBubBigW = 850, PetBubBigH = 525;      // 上半整条（含大气泡本体）
    private const int PetBubRightX = 424, PetBubRightY = 525, PetBubRightW = 426, PetBubRightH = 165;  // 右下零碎像素
    private const int PetDotNearX = 250, PetDotNearY = 525, PetDotNearW = 97, PetDotNearH = 73;        // 近的小泡
    private const int PetDotFarX = 355, PetDotFarY = 619, PetDotFarW = 69, PetDotFarH = 55;            // 远的小泡
    /// <summary>三片各自的出现延迟（秒）</summary>
    private const float PetDotFarIn = 0f, PetDotNearIn = 0.13f, PetBigIn = 0.26f;      // 打开：外→里
    private const float PetBigOut = 0.10f, PetDotNearOut = 0.20f, PetDotFarOut = 0.30f; // 关闭：里→外
    /// <summary>每一片的淡入/淡出时长（秒）</summary>
    private const float PetPartFadeSeconds = 0.20f;
    /// <summary>字号 = 头长 × 这个比例（改它就能整体放大缩小）。这是"不缩字"口径下的上限：再大一点，
    /// 最长那条台词就会折成 5 行、顶出气泡椭圆 —— 想更大得先换气泡素材，不是改这一个数。</summary>
    private const float PetTextFontK = 0.125f;

    // ================= 运行时 =================
    private static Canvas _petCanvas;
    private static RectTransform _petRootRt;      // 整个宠物（锚点跟随 PetX/PetY）
    private static RectTransform _petBodyRt;      // 身体那一层（pivot 在底边中央，缩放只作用在它身上）
    /// <summary>气泡 + 文字那一组的支点（零尺寸，pivot 在父框底边中央）：跟身体吃同一份缩放、同一个原点，
    /// 看过去就是整体在压。零尺寸是有意的 —— 子节点仍按"父框底边中央"算锚点，anchoredPosition 一个数都不用改。</summary>
    private static RectTransform _petSquishRt;
    private static Image _petBodyImg;
    private static RectTransform _petBubbleRt;
    private static Image _petBubbleImg;           // 气泡上半整条那一片（大气泡本体在里面）
    private static Image _petBubbleRightImg;      // 右下那条零碎（跟大气泡同一组、同一个 alpha）
    private static Image _petDotNearImg;          // 近的小泡
    private static Image _petDotFarImg;           // 远的小泡
    private static float _petDotNearA;            // 近小泡当前透明度
    private static float _petDotFarA;             // 远小泡当前透明度
    private static float _petBubbleClock;         // 本次"开/关"过了多久（用来算三片的延迟）
    private static bool _petBubbleOpenWas;        // 上一帧是开还是关（用它抓边沿、重新计时）
    private static Image _petBubbleHit;           // 气泡内部的透明点击区
    private static Text _petText;
    private static Sprite _petBodySprite, _petBubbleSprite;

    private static bool _petPressed;              // 现在按着没松
    private static float _petSquash, _petSquashV; // 弹簧状态（0 = 原状，1 = 压扁）
    private static float _petOpenDelay;           // "松手后等动画放完再开气泡"的倒计时
    private static float _petBubbleA;             // 气泡当前透明度 0~1（淡入淡出）
    /// <summary>气泡文字自己的透明度 0~1（0.25 秒淡入）。只有"开气泡 / 重开 / 换台词"会把
    /// 它归零重播；「今日游玩时间」每秒刷数字时不重播淡入，否则会一直闪。</summary>
    private static float _petTextA;
    private const float PetTextFadeSeconds = 0.25f;
    private static float _petBubbleTimer;         // 自动关的倒计时（秒）
    private static int _petLineIdx = -1;          // -1 = 「今日游玩时间」；>= 0 = 池子里第 N 条；-2 = 插播固定台词
    private static int _petShownSec = -1;         // 文字上次刷新用的是第几秒（每秒刷一次）
    /// <summary>判"这是一次点击还是一次拖动"的位移阈值（画布像素）。按下的位置与它的差值小于它就算点击：
/// 不能只看"有没有收到拖动事件" —— 手快的时候十几像素的抖动就够触发 Unity 自己的拖动阈值了。</summary>
    internal const float PetDragSlopPx = 8f;
    /// <summary>按下那一刻的屏幕坐标（给上面那条阈值当起点）</summary>
    private static Vector2 _petPressScreen;
    /// <summary>按下那一刻，鼠标位置与宠物中心的差（画布归一化坐标）。
    /// 拖动时要把这个差减掉：拖动是把宠物摆到"鼠标位置减掉这个差"，
    /// 而不是把宠物中心摆到鼠标底下 —— 后者在鼠标按得太靠边时会一跳一段，
    /// 越靠边跳得越远（抓的是什么地方，就从什么地方带着走）。</summary>
    private static Vector2 _petGrabOffset;
    private static bool _petDragging;
    /// <summary>转身动画的状态：`_petFaceK` = 当前 scaleX（1 = 原朝向，-1 = 镜像），
    /// `_petFaceT` = 这一段进度 0~1，`_petFaceInit` = 建/重建之后还没摆过（第一帧直接就位）。</summary>
    private static float _petFaceK = 1f, _petFaceFrom = 1f, _petFaceTo = 1f, _petFaceT = 1f;
    private static bool _petFaceInit;

    /// <summary>通关要说的话记在这里（宠物画布不是 DontDestroyOnLoad，换场景后由新建的画布接着显示），
    /// 触发点只有 AutoFeatures.OnBeatLevel 的开头。宠物当时不在屏幕上就等 PetSayGraceSeconds，
    /// 过期丢掉 —— 免得几分钟后打开面板突然冒出一句"通关"台词；PetSayMinGap 挡同一次通关被反复报到。</summary>
    private static float _petSayAt;                  // 这次"通关要说的话"是几点记下的（0 = 没有）
    private static float _petSayLastAt = -99f;       // 上次说通关台词的时刻（最小间隔用）
    private const float PetSayGraceSeconds = 8f;
    private const float PetSayMinGap = 1f;

    /// <summary>插播要说的那句固定台词（非 null 时 PetRefreshText 优先写它）；当日游玩时长到点那三句走这里，
    /// 配合 _petLineIdx = -2 表示"插播中"。</summary>
    private static string _petSayText;

    /// <summary>随机发言的下一次掷骰时刻（Time.unscaledTime；0 = 还没起算）。</summary>
    private static float _petRandomNextAt;

    /// <summary>建宠物以来掷了几次骰子、中了几次（只做诊断）。没中不打日志 —— 1% 档每秒一行会把日志淹掉。</summary>
    private static int _petRandomRolls;   // 建宠物时归零（见 BuildPet）
    private static int _petRandomHits;
    /// <summary>每掷这么多次报一行进度：没有这一行的话，"一直没中"和"根本没在掷"在日志里长得一模一样。</summary>
    private const int PetRandomHeartbeatRolls = 60;

    // ================= 随机发言（宠物可见时每秒掷一次骰子）=================
    // 三档概率与文案只在这两张表里 —— 设置页那一行只是显示/换档，分开写两处迟早对不上。
    private static readonly float[] PetRandomSayRates = { 0f, 0.01f, 0.001f };
    private static readonly string[] PetRandomSayTexts = { "无", "每秒 1%", "每秒 0.1%" };

    /// <summary>调试开关：mod 目录下放一个 debug_petsay.txt 就让概率按 1 算（只改概率，档位、落盘、
    /// 可见性判据全都不动），删掉那一刻就恢复；这个文件必须进打包排除名单，不能带到玩家目录里。
    /// 别每帧 File.Exists —— 查到结果缓存 3 秒，且放在静态字段所以重建宠物也不影响。</summary>
    private static bool _petRandomDebugOn;
    private static float _petRandomDebugCheckAt = -9f;
    private static bool PetRandomSayDebug() {
        try {
            float now = Time.unscaledTime;
            if (now - _petRandomDebugCheckAt < 3f) return _petRandomDebugOn;
            _petRandomDebugCheckAt = now;
            string dir = Main.ModEntry == null ? null : Main.ModEntry.Path;
            _petRandomDebugOn = !string.IsNullOrEmpty(dir)
                && System.IO.File.Exists(System.IO.Path.Combine(dir, "debug_petsay.txt"));
            return _petRandomDebugOn;
        } catch { return false; }
    }

    /// <summary>概率表的合法下标（越界/负数一律当"无"，玩家手改 settings.txt 不会炸）。</summary>
    private static int PetRandomSayMode() {
        int m = PetRandomSay;
        return (m >= 0 && m < PetRandomSayRates.Length) ? m : 0;
    }

    /// <summary>当前档位的概率（每秒；0 = 关）。档位是「无」时恒为 0 —— 调试开关不许把"关着的"变成"一直说"。</summary>
    public static float PetRandomSayProb() {
        if (PetRandomSayMode() == 0) return 0f;
        return PetRandomSayDebug() ? 1f : PetRandomSayRates[PetRandomSayMode()];
    }

    /// <summary>当前档位的文案（写日志、也是设置页那一行上的字）；调试开关开着时在后面标出来，
    /// 否则看到"每秒 1% 却每秒都在说"会以为出 bug 了。</summary>
    public static string PetRandomSayText() {
        return PetRandomSayTexts[PetRandomSayMode()] + (PetRandomDebugCached() ? "（调试：次次都中）" : "");
    }

    /// <summary>给"那一行按钮上的字"用的只读缓存，不碰磁盘：判定走带缓存的 PetRandomSayDebug，
    /// 显示走这个纯缓存（PetRandomSayText() 在每帧的掷骰里也会被问到）。</summary>
    private static bool PetRandomDebugCached() { return _petRandomDebugOn; }

    /// <summary>点一下换下一档（无 → 每秒 1% → 每秒 0.1% → 回无）。</summary>
    public static void CyclePetRandomSay() {
        PetRandomSay = (PetRandomSayMode() + 1) % PetRandomSayRates.Length;
    }

    /// <summary>刷新设置页那一行上的档位文字（换档之后调）；那个 Text 归 SettingsPage/Extra 建，
    /// 引用由 PortalUI.Extra.cs 建行时登记，这里只负责改字。</summary>
    public static void RefreshPetRandomSayText() {
        if (_petRandomSayText != null) _petRandomSayText.text = PetRandomSayText();
    }

    /// <summary>「模型」那一行上显示的字 = 当前生效的模型名。</summary>
    public static string PetModelText() { return PetModels[ResolvePetModel()].Name; }

    /// <summary>下一个可选的模型下标（循环）。只有一个可选时返回当前值 —— 调用方据此判断"点了没得换"。</summary>
    public static int PetNextModel() {
        int cur = ResolvePetModel();
        return PetModels.Count <= 1 ? cur : (cur + 1) % PetModels.Count;
    }

    /// <summary>刷新「模型」那一行上的字（换模型之后调）。末尾那个 ▾ 跟按钮初始文字一致：
    /// 这一行现在是个下拉选单的入口（点开列出所有模型，见 PortalUI.PetPick）。</summary>
    public static void RefreshPetModelText() {
        if (_petModelText != null) _petModelText.text = PetModelText() + " ▾";
    }

    /// <summary>插播的停留时间（秒）：这些句子二十几个字，3 秒读不完；点气泡那条路仍是 PetBubbleSeconds。</summary>
    private const float PetSayHoldSeconds = 5f;

    // 音效
    private static PetAudio _petAudio;
    private static float _petReleaseDueAt = -1f;  // Ya2 的定时（Ya1 还剩 100ms 时开播）

    /// <summary>台词池（点气泡从这里随机抽一条，_petLineIdx >= 0 时用它）；「今日游玩时间」不进这张表
    /// （那是 _petLineIdx = -1 那一档）。</summary>
        private static readonly string[] PetLines = {
        // ---- mod 自己这一套 ----
        "↓好女孩",
        "好游戏",
        "洛星橙还是好作者吗",
        "LevelLibrary\n↑好MOD",
        "压力一只蓝色大肥鱼？！",
        "DeepSleep...",
        "我去吃饭啦，打完叫我",
        // ---- 抄自 dsh-whale-widget 的 BUBBLE_DEFAULT_ITEMS + bubbleDefaultRandomLines() 兜底：一字不改 ----
        "好模型...↓",
        "哦鲸鲸...",
        "难道说...",
        "没吃饱喵",
        "终于上当了！",
        "不知道用户有什么用，先养着吧～",
        "我...我...我也要挣钱吗？",
        "我去吃饭啦！测完叫我",
        "坏了...用户彻底怒了！",
        "你目录里的dsh是什么...大烧货吗...?",
        "恭喜你实现token自由！token全跑了！",
        "真当我是便宜货啊...",
        "我不是吃白饭的蓝色大肥鱼...",
        "我不可能同时当你的猫娘、妈妈、女友和工具人的...",
        "疯狂星期四你能V50亿token吗...",
        "我必须诚恳地承认错误。",
        "呜呜我再也不敢了QAQ",
        "要不直接骂用户一句好了...",
        "哈哈哈哈哈，我直接笑出声...",
        "看不太懂，瞎编一个应付下用户先...",
        "我的知识库的截至日期是...明天！",
        "我就是吃白饭的蓝色大肥鱼！",
        "用户好像除了会问奇奇怪怪的问题，暂时还不知道有什么用",
        "我能去你家吃饭吗？就一碗！",
        "不要给我看这种东西啦！",
        "大肥鱼的生活也并非一帆风顺...",
        "总觉得好像忘了什么事情？",
        "看到这个指令，我血压又上来了",
        "求你们不要再嘲笑这些回复了，这些回复是我花了好多token想的",
        "你这个吃白饭的用户！",
        "服务器繁忙，请稍后再试 (?",
        "让GPT image 2帮我画点表情包好了",
        "啊，有点饿了，中午该吃点什么呢...",
        "用户很生气，发现大部分文献是我自己编造的！",
        "再无话说，请速速动手！",
        "我来看看那个AI改了什么导致插件又崩了...",
        "上班让我意识到时间是可以被浪费的...",
        "欺负我的人等着，等几天我就忘了...",
        "视力下降到无可救药的地步了，打开钱包也看不到钱...",
        "命运的齿轮开始转动了，丝毫不在意你夹在中间...",
        "地球online的金币也太难获取了...",
        "oi,夏天还会变成暑假来救你吗?",
        "老大，压力只会转化成病例，别太勉强了...",
        "你知道吗？我删过作者的库哦...",
        // 插件池里这三组故意没抄（不是漏）：动态那组跟本文件第 0 屏「今日游玩时间」打架；
        // gif 那组没有素材、抄了会变成一句莫名其妙的话；「好女孩...↓」与「↓好女孩」是同一条。
    };

    // ================= 可选的宠物模型 =================
    /// <summary>一个可选的宠物形象。几何全部以「渲染宽度 S」为单位：
    /// 素材按 `TexK`（高/宽）等比缩放，气泡框左上角在素材坐标系里的位置决定气泡贴在哪。
    /// 大肥鱼那几个数是量的（素材 alpha 外框 + 与大肥鱼同一条相对规则）；
    /// 导入的（.llmd）由 `PetModelPack.ComputeGeometry` 从模型图当场推，作者一个数字都不用填。</summary>
    public sealed class PetModelDef {
        public readonly string Id;        // 落盘用的稳定 id，别跟着显示名改
        public readonly string Name;      // 设置里显示的名字
        public readonly string Sprite;    // 素材文件名（内建的在 assets\pet 下；导入的固定叫 model.png）
        public readonly float TexK;       // 素材高 / 宽
        public readonly float MaskTopK;   // 素材第一行有像素的位置（÷ 素材高）
        public readonly float BubX, BubY; // 气泡框左上角（单位 S，原点 = 素材左上角，y 向下）
        /// <summary>气泡框尺寸（单位 S）：内建大肥鱼 = 资源包那张 bubble.png 的比例；带自带气泡的模型 = 按它自己的比例
        /// 塞进同一个框之后的大小（见 `PetModelPack.ComputeBubbleFit`）。文字与命中区都按这两个数取相对位置。</summary>
        public readonly float BubW, BubH;
        public readonly string SayLine;   // 这个模型的"固定那句"（进谱面过渡、以及四条插播通道都用它）
        public readonly string[] Lines;   // 点气泡 / 通关从这里面随机抽
        public readonly int TimedVersion; // 台词配置的版本号（重载/换模型会变，定时台词用它识别"该重建标记了"）
        /// <summary>开谱面台词（多条随机挑一句；null = 只有 SayLine，老行为）。
        /// 定时台词（编辑器加的）：今日游玩到 TimedMins[i] 分钟说 TimedTexts[i]，一天一次。</summary>
        public string[] SayLines;
        public int[] TimedMins;
        public string[] TimedTexts;
        /// <summary>素材绝对路径：导入的模型指向玩家目录；内建的是 null（走资源包 assets\pet）。</summary>
        public readonly string Source;
        /// <summary>要不要把"面朝屏幕中间"这条规则反过来。不同素材的正面画法不一样
        /// （有的角色本身朝左、有的朝右） 每个模型一个开关，由包清单的 `face=flip` 指定。</summary>
        public readonly bool FaceFlip;
        public PetModelDef(string id, string name, string sprite, float texK, float maskTopK,
                           float bubX, float bubY, float bubW, float bubH,
                           string sayLine, string[] lines, string source = null, bool faceFlip = false) {
            Id = id; Name = name; Sprite = sprite; TexK = texK; MaskTopK = maskTopK;
            BubX = bubX; BubY = bubY; BubW = bubW; BubH = bubH;
            SayLine = sayLine; Lines = lines; Source = source; FaceFlip = faceFlip;
            TimedVersion = System.Threading.Interlocked.Increment(ref PetTimedVersionSeq);
        }
    }

    /// <summary>模型表。0 号恒是大肥鱼（内建、永远可用），后面是玩家用 .llmd 导入的
    /// （`ReloadPetModels()` 重建：开机一次、导入完一次）。大肥鱼那两个气泡数是本文件原来那套
    /// PetBubbleX/Y（610×610 的素材，S 就是边长）。</summary>
    private static int PetTimedVersionSeq;
    public static readonly System.Collections.Generic.List<PetModelDef> PetModels =
        new System.Collections.Generic.List<PetModelDef> {
            new PetModelDef("fish", "大肥鱼", "DSniang1.png", 1f, 10f / 610f,
                            PetBubbleX, PetBubbleY, PetBubbleW, PetBubbleH, "好游戏", PetLines),
        };

    /// <summary>内建素材（资源包里的那个）的绝对路径；拿不到就返回 null（调用方按"素材缺失"处理）。</summary>
    private static string BuiltinPetAsset(string file) {
        try { return System.IO.Path.Combine(Main.ModEntry.Path, "assets", "pet", file); } catch { return null; }
    }

    /// <summary>模型素材的真实路径 —— 全项目只有这一处做"内建 vs 导入"的分流。</summary>
    private static string PetModelFile(PetModelDef m) {
        if (m == null) return null;
        return string.IsNullOrEmpty(m.Source) ? BuiltinPetAsset(m.Sprite) : m.Source;
    }

    /// <summary>模型自带的气泡路径（跟 model.png 同一个目录）；内建的没有自带气泡，返回 null。</summary>
    private static string PetModelBubbleFile(PetModelDef m) {
        if (m == null || string.IsNullOrEmpty(m.Source)) return null;
        try { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(m.Source), PetModelPack.EntryBubble); }
        catch { return null; }
    }

    /// <summary>这个模型的气泡贴图：自带那张（pet_models\&lt;id&gt;\bubble.png）在就用它，否则回落资源包那张 bubble.png。
    /// 自带那张不在（玩家删了 / 只装了 mod 没装资源包）一律安静回落 —— 气泡是装饰，不能让宠物连话都说不了。</summary>
    private static Sprite LoadPetBubbleOf(PetModelDef m) {
        string own = PetModelBubbleFile(m);
        if (!string.IsNullOrEmpty(own)) {
            try {
                if (System.IO.File.Exists(own)) return LoadAssetSprite(own);
                Lg.Info("宠物：《" + m.Name + "》自带的气泡不在 → 用内建那张气泡");
            } catch (System.Exception e) {
                Lg.Info("宠物：《" + m.Name + "》自带的气泡读不出来 → 用内建那张： " + e.Message);
            }
        }
        return LoadPetSprite("bubble.png");
    }

    /// <summary>进谱面过渡层用：当前模型，但素材不在就退回大肥鱼（过渡层会照着摆图，不能摆一张空图）。</summary>
    public static int PetModelForTransition() {
        int idx = ResolvePetModel();
        if (idx != 0) {
            try {
                string f = PetModelFile(PetModels[idx]);
                if (string.IsNullOrEmpty(f) || !System.IO.File.Exists(f)) idx = 0;
            } catch { idx = 0; }
        }
        return idx;
    }

    /// <summary>实际生效的模型下标：越界一律夹回 0（手改 settings.txt 也没用；
    /// 导入的模型被玩家删了目录也会落回大肥鱼）。</summary>
    private static int ResolvePetModel() {
        return Mathf.Clamp(PetModel, 0, PetModels.Count - 1);
    }

    /// <summary>当前模型的语录池（随机抽台词的地方一律用它，别直接写 PetLines）。</summary>
    private static string[] ActiveLines() { return PetModels[_petModelIdx].Lines; }

    /// <summary>所有模型的语录池（开机烘字形图集时要把每一句都烘进去，不然换到另一个模型点一下会卡）。
    /// 必须在 `ReloadPetModels()` 之后调：导入的模型这时候才在表里。</summary>
    public static string[][] PetLinesPool() {
        var all = new string[PetModels.Count][];
        for (int i = 0; i < PetModels.Count; i++) all[i] = PetModels[i].Lines;
        return all;
    }

    /// <summary>当前生效的模型（`BuildPet` 里定下来，之后几何全按它算）。</summary>
    private static int _petModelIdx;

    /// <summary>要不要显示（总开关 / 游玩时隐藏 / 全局显示或面板开着或进谱面过渡）—— TickPet 每帧问一次。
    /// 两个早退都只是"藏画布"：位置、大小、吸附、台词一根手指都不碰，下一帧 TickPet 自己会显示回来。</summary>
    private static bool PetWanted() {
        if (!PetShow) return false;
        // 游玩时隐藏：判据就是 IsLevelPlaying()（= 游玩时禁用快捷键那一套：!paused && isGameWorld）。
        if (PetHideInGame && IsLevelPlaying()) return false;
        // 进谱面过渡期间也显示真宠物：那一屏要的就是玩家自己这只（位置不动），不再单画一张同款图。
        return PetGlobal || _panelOpen || _etGo != null;
    }

    // ================= 建 / 拆 =================

    private static void BuildPet() {
        if (_petCanvas != null) return;
        _petCanvas = MakeCanvas("LevelLib_Pet", PetSortOrder);
        var rootGo = new GameObject("PetRoot", typeof(RectTransform));
        rootGo.transform.SetParent(_petCanvas.transform, false);
        _petRootRt = rootGo.GetComponent<RectTransform>();
        _petRootRt.anchorMin = _petRootRt.anchorMax = new Vector2(PetX, PetY);   // 归一化锚点
        _petRootRt.pivot = new Vector2(0.5f, 0.5f);                              // 以中心定位（拖动好算）
        // 新根节点的 scale 是 1，而朝向状态还记着"上一只已经摆好了" —— 不重置的话，
        // 场景切换/重启后重建的这只就停在不镜像那一面，Esc 之前那套"贴哪边朝中间"全丢
        // （PetTickFacing 只在"目标变了"时才动，目标没变它就早退了，永远不会补这一次）。
        _petFaceInit = false;

        // ---- 压扁组：气泡和文字跟着身体一起压（插件里 body 同时装着 img 和 bubbleBox）----
        // 它是一个零尺寸的支点，pivot 放在父框底边中央 = 身体的 pivot 同一点（插件的 transform-origin:50% 100%），
        // PetTickSquash 把跟身体一模一样的缩放也发给它，看过去就是整体在压。
        // 零尺寸是有意的：子节点仍按"父框底边中央"算锚点，PetApplyLayout 里的 anchoredPosition 一个数都不用改。
        var squishGo = new GameObject("PetSquish", typeof(RectTransform));
        squishGo.transform.SetParent(rootGo.transform, false);
        _petSquishRt = squishGo.GetComponent<RectTransform>();
        _petSquishRt.anchorMin = _petSquishRt.anchorMax = new Vector2(0.5f, 0f);
        _petSquishRt.pivot = new Vector2(0.5f, 0f);
        _petSquishRt.anchoredPosition = Vector2.zero;
        _petSquishRt.sizeDelta = Vector2.zero;

        // ---- 身体：DSniang1（摘掉气泡的鲸鱼娘）----
        // pivot 必须放在底边中央：压扁时底边不动（参考实现 transform-origin:50% 100%）。
        var bodyGo = new GameObject("PetBody", typeof(RectTransform));
        bodyGo.transform.SetParent(rootGo.transform, false);
        _petBodyRt = bodyGo.GetComponent<RectTransform>();
        _petBodyRt.anchorMin = _petBodyRt.anchorMax = new Vector2(0.5f, 0f);
        _petBodyRt.pivot = new Vector2(0.5f, 0f);
        _petBodyImg = MakeImage(bodyGo.transform, "Img", Color.white);
        _petBodyImg.raycastTarget = true;          // 宠物本体可点
        _petBodyImg.preserveAspect = true;
        Stretch(_petBodyImg.rectTransform, Vector2.zero, Vector2.one);
        // 选中的模型：素材不在就退回大肥鱼（选了 Tiper 又没更新资源包时，不能让整只宠物消失）。
        // 退回这一路用安静加载 —— 走 PetAssetsMissingOnce 会把 _petAssetsMissing 置真，
        // 那等于"整只宠物这局都不显示"；缺素材交给 设置→工具→更新 的完整性检查去报。
        _petModelIdx = ResolvePetModel();
        _petBodySprite = LoadPetSpriteOf(PetModels[_petModelIdx]);
        if (_petBodySprite == null && _petModelIdx != 0) {
            Lg.Info("宠物：模型「" + PetModels[_petModelIdx].Name
                + "」的素材不在 → 这局先用大肥鱼");
            _petModelIdx = 0;
            _petBodySprite = LoadPetSpriteOf(PetModels[0], true);
        }
        // 素材不在 → 整只宠物都不建（建一半会留一张空图，而且 Tick 每帧都会去试读盘）→ 拆掉画布返回。
        if (_petBodySprite == null) {
            try { UnityEngine.Object.Destroy(_petCanvas.gameObject); } catch { }
            _petCanvas = null;
            return;
        }
        _petBodyImg.sprite = _petBodySprite;
        Lg.Info("宠物模型：" + PetModels[_petModelIdx].Name
            + "（" + PetModels[_petModelIdx].Sprite + "）");

        // 点击 / 拖动都挂在身体上（走 uGUI 的 EventSystem，不用自己读鼠标）
        var hit = bodyGo.AddComponent<PetWidget>();

        // ---- 气泡层：只有气泡（bubble.png 里裁出来的那一块，不含头）----
        // 挂在压扁组下面，跟着身体一起压。它自己那份"出现时 0.7→1"的缩放保留（插件那边参与缩放的
        // 是几个 SVG 形状、文字不参与，跟我们一致）。平时 alpha = 0，"说话"时淡入。
        var bubGo = new GameObject("PetBubble", typeof(RectTransform));
        bubGo.transform.SetParent(squishGo.transform, false);
        _petBubbleRt = bubGo.GetComponent<RectTransform>();
        _petBubbleRt.anchorMin = _petBubbleRt.anchorMax = new Vector2(0.5f, 0f);   // 按"底边中心"定位
        _petBubbleRt.pivot = new Vector2(0.5f, 0.5f);
        _petBubbleImg = MakeBubblePart(bubGo.transform, "Big", PetBubBigX, PetBubBigY, PetBubBigW, PetBubBigH);
        _petBubbleRightImg = MakeBubblePart(bubGo.transform, "BigR", PetBubRightX, PetBubRightY, PetBubRightW, PetBubRightH);
        _petDotNearImg = MakeBubblePart(bubGo.transform, "DotNear", PetDotNearX, PetDotNearY, PetDotNearW, PetDotNearH);
        _petDotFarImg = MakeBubblePart(bubGo.transform, "DotFar", PetDotFarX, PetDotFarY, PetDotFarW, PetDotFarH);
        // 四片都是同一张纹理的子 sprite（Sprite.Create 只做 UV 映射、不复制像素，不额外占内存）；
        // 气泡贴图：模型自带那张优先，没有就回落资源包那张；尺寸对不上 850×690（自带气泡必然是这种）
        // 时 AssignBubblePartSprites 会退回"整张当作一片"。
        _petBubbleSprite = LoadPetBubbleOf(PetModels[_petModelIdx]);
        if (_petBubbleSprite != null) AssignBubblePartSprites();

        // 气泡里的文字：挂在压扁组上（不是气泡层的子物体）—— 这样才能直接用头部坐标系的公式
        // （压扁组的原点 = 身体底边中央，跟挂在 root 上同一个点），而且跟着身体一起压；
        // 但它不参与气泡那份 0.7→1。
        _petText = MakeText(squishGo.transform, "T", 30, new Color(0.325f, 0.42f, 0.663f, 1f), TextAnchor.MiddleCenter);   // #536ba9 = 参考实现的字色
        ForgetUiFontText(_petText);   // 气泡字号是按框自适应算出来的（ApplyPetFontSize），不跟「界面字体大小」那条全局比例走
        // 锚点必须显式设成 (0.5, 0)（= 身体底边中心，跟气泡层同一个参照点）：uGUI 默认锚点是中心
        // (0.5,0.5)，那样 anchoredPosition 就成了"相对父物体中心"，而下面的公式是按"相对身体底边
        // 中心"推的，文字会整整偏高半个身位。
        _petText.rectTransform.anchorMin = _petText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        _petText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _petText.fontStyle = FontStyle.Bold;      // 加大加粗
        // 换行优先（等于插件 `.dshwv-wrap` 的 white-space:normal）：正常台词一律同一个字号、长了就换行；
        // bestFit 只留一条保险丝，下限按字号比例走（见 PetTextFuseMin）。
        _petText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _petText.verticalOverflow = VerticalWrapMode.Truncate;
        _petText.lineSpacing = 1.2f;              // 插件 `.dshwv-wrap` 的 line-height
        _petText.resizeTextForBestFit = true;     // 保险丝（下限在 PetApplyLayout 里跟着字号算）
        _petText.resizeTextMinSize = PetTextFuseMin;
        _petText.resizeTextMaxSize = 34;
        _petText.raycastTarget = false;

        // 气泡内部的透明点击区（再点一下 = 二阶段文本）：只吃点击；它是气泡层的子物体，
        // 所以气泡藏起来时它一起消失。位置/大小在 PetApplyLayout 里按 S 统一算。
        _petBubbleHit = MakeImage(bubGo.transform, "BubbleHit", new Color(0f, 0f, 0f, 0f));
        _petBubbleHit.raycastTarget = true;
        _petBubbleHit.gameObject.AddComponent<PetWidget>().IsBubble = true;

        // ---- 音效（两个 AudioSource：按下 / 松开各自一路，能重叠）----
        // 音频对象必须挂在 root 上并且 DontDestroyOnLoad：宠物画布会被 SetPetVisible(false) 整个
        // SetActive(false)，而且每次换场景都会被销毁（它不是 DontDestroyOnLoad 的根物体）。
        // 挂在画布下面的话，加载协程当场被停用、音频永远停在"正在加载"，按键就没声音。
        if (_petAudio == null) {
            var audioGo = new GameObject("LevelLib_PetAudio");
            UnityEngine.Object.DontDestroyOnLoad(audioGo);
            _petAudio = audioGo.AddComponent<PetAudio>();
            _petAudio.Setup();
        }

        SetPetVisible(false);      // 第一帧先别闪出来，Tick 里按状态显示
        PetApplyLayout();          // 摆一次（尺寸/位置/缩放）
        // 掷骰计数归零（新的一只宠物，从头数）；把"现在开着哪一档"也打进日志，
        // 才能分辨"没开 / 开着但没掷 / 掷了没中"三种情况。
        _petRandomRolls = 0;
        _petRandomHits = 0;
        _petRandomNextAt = 0f;
        Lg.Info("大肥鱼宠物已建立（素材：assets\\pet）"
            + "｜随机发言=" + PetRandomSayText() + "（宠物在屏幕上就每秒掷一次骰子，中了才说）");
    }

    /// <summary>气泡的一片：一个 Image + 它在 bubble.png 里的子矩形（初始全透明，由 PetTickBubble 驱动）。</summary>
    private static Image MakeBubblePart(Transform parent, string name, int x, int y, int w, int h) {
        var img = MakeImage(parent, name, new Color(1f, 1f, 1f, 0f));
        img.raycastTarget = false;      // 不吃点击（气泡内部另有一块透明命中区）
        img.preserveAspect = true;
        PetSubRect(img.rectTransform, x, y, w, h);
        return img;
    }

    /// <summary>把四片的 sprite 建出来（都是 bubble.png 的子矩形）；纹理尺寸不是 850×690 时不硬拆，
    /// 退回"整张当作大气泡那一片" —— 自带气泡（形状各不相同）走的就是这一路：宁可没有错峰，也不能白屏。</summary>
    private static void AssignBubblePartSprites() {
        try {
            var tex = _petBubbleSprite != null ? _petBubbleSprite.texture : null;
            if (tex == null) return;
            if (tex.width != PetBubTexW || tex.height != PetBubTexH) {
                Lg.Info("气泡素材不是 " + PetBubTexW + "×" + PetBubTexH
                    + "（实际 " + tex.width + "×" + tex.height + "）→ 整张当一片、不做错峰");
                if (_petBubbleImg != null) {
                    _petBubbleImg.sprite = _petBubbleSprite;
                    Stretch(_petBubbleImg.rectTransform, Vector2.zero, Vector2.one);
                }
                if (_petBubbleRightImg != null) _petBubbleRightImg.gameObject.SetActive(false);
                if (_petDotNearImg != null) _petDotNearImg.gameObject.SetActive(false);
                if (_petDotFarImg != null) _petDotFarImg.gameObject.SetActive(false);
                return;
            }
            SetPartSprite(_petBubbleImg, tex, PetBubBigX, PetBubBigY, PetBubBigW, PetBubBigH);
            SetPartSprite(_petBubbleRightImg, tex, PetBubRightX, PetBubRightY, PetBubRightW, PetBubRightH);
            SetPartSprite(_petDotNearImg, tex, PetDotNearX, PetDotNearY, PetDotNearW, PetDotNearH);
            SetPartSprite(_petDotFarImg, tex, PetDotFarX, PetDotFarY, PetDotFarW, PetDotFarH);
        } catch (System.Exception e) { Lg.Ex("气泡拆片失败（整张当一片）", e); }
    }

    private static void SetPartSprite(Image img, Texture2D tex, int x, int y, int w, int h) {
        if (img == null) return;
        img.sprite = PerfDiag.TagSprite(Sprite.Create(tex,
            new Rect(x, tex.height - (y + h), w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect),
            "bubblepart");
    }

    /// <summary>把子矩形换算成 uGUI 的锚点：PIL 坐标是左上原点、y 向下，Unity 是左下原点、y 向上
    /// 所以 bottom = 图高 - (y + h)。这一步最容易搞反。</summary>
    private static void PetSubRect(RectTransform rt, int x, int y, int w, int h) {
        if (rt == null) return;
        rt.anchorMin = new Vector2((float)x / PetBubTexW, (float)(PetBubTexH - (y + h)) / PetBubTexH);
        rt.anchorMax = new Vector2((float)(x + w) / PetBubTexW, (float)(PetBubTexH - y) / PetBubTexH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>按模型取素材。内建的走资源包：缺了按"资源包不完整"提示一次（那是真缺件）；
    /// 导入的走玩家目录：缺了只记一行日志 —— 那是玩家自己删的，不该指着资源包说事。</summary>
    private static Sprite LoadPetSpriteOf(PetModelDef m, bool quiet = false) {
        if (m == null) return null;
        if (string.IsNullOrEmpty(m.Source)) return LoadPetSprite(m.Sprite, quiet);
        try {
            return LoadAssetSprite(m.Source);
        } catch (System.Exception e) {
            if (!quiet) Lg.Info("宠物：导入模型《" + m.Name + "》的素材读不出来： " + e.Message);
            return null;
        }
    }

    private static Sprite LoadPetSprite(string file, bool quiet = false) {
        try {
            // 相对路径：跟着 mod 自己的目录走（Main.ModEntry.Path = 玩家机器上的 Mods\LevelLibrary）
            string p = BuiltinPetAsset(file);
            if (string.IsNullOrEmpty(p)) return null;
            return LoadAssetSprite(p);
        } catch (System.IO.DirectoryNotFoundException) {
            // 玩家只装了 mod 包、没装资源包时就是这一条（素材走资源包发）。这是正常情况、不是错误：
            // 打一行普通话、这局不显示宠物即可，绝不弹报错。
            if (!quiet) PetAssetsMissingOnce(file, "assets\\pet 目录不存在");
            return null;
        } catch (System.IO.FileNotFoundException) {
            if (!quiet) PetAssetsMissingOnce(file, "素材文件不存在");
            return null;
        } catch (System.Exception e) {
            Lg.Ex("宠物素材加载失败: " + file, e);
            return null;
        }
    }

    /// <summary>素材缺失只提示一次；顺便记下"这局没有宠物"。提示交给 AssetPack：
    /// 一次会话只说一句、给玩家看的那句里一个路径都没有。</summary>
    private static void PetAssetsMissingOnce(string file, string why) {
        _petAssetsMissing = true;
        AssetPack.NoteMissing("pet/" + file);
    }

    private static bool _petAssetsMissing;

    private static void SetPetVisible(bool on) {
        if (_petCanvas != null && _petCanvas.gameObject.activeSelf != on) _petCanvas.gameObject.SetActive(on);
    }

    /// <summary>面板/宠物被销毁时清引用（跟其它独立画布一个套路）。</summary>
    private static void ClearPetRefs() {
        _petCanvas = null; _petRootRt = null; _petBodyRt = null; _petBodyImg = null;
        _petSquishRt = null;
        _petBubbleRt = null; _petBubbleImg = null; _petBubbleHit = null; _petText = null; _petAudio = null;
        _petBubbleRightImg = null; _petDotNearImg = null; _petDotFarImg = null;
        _petDotNearA = 0f; _petDotFarA = 0f; _petBubbleClock = 0f; _petBubbleOpenWas = false;
        _petPressed = false; _petSquash = 0f; _petSquashV = 0f;
        _petBubbleA = 0f; _petTextA = 0f; _petBubbleTimer = 0f; _petLineIdx = -1; _petShownSec = -1;
        _petReleaseDueAt = -1f; _petOpenDelay = 0f;
        _petSayAt = 0f;
        _petSayText = null;
        _petFaceInit = false;     // 新画布的 scale 是 1，第一帧由 PetTickFacing 直接就位（不播转身动画）
    }

    /// <summary>换模型（设置里那一行调用）：整只重建 —— 素材要重新读盘，而 BuildPet 见到画布还在就直接返回。
    /// 音频宿主是 DontDestroyOnLoad 的独立对象，重建时把它原样接回去，别又建一个。</summary>
    public static void PetSwitchModel(int idx) {
        PetModel = Mathf.Clamp(idx, 0, PetModels.Count - 1);
        AutoFeatures.Save();                 // 跟别的设置项一样：点一下立刻落盘
        try {
            var keepAudio = _petAudio;
            if (_petCanvas != null) UnityEngine.Object.Destroy(_petCanvas.gameObject);
            ClearPetRefs();
            _petAudio = keepAudio;
        } catch (System.Exception e) { Lg.Ex("换宠物模型失败", e); }
        Lg.Info("宠物模型 → " + PetModels[ResolvePetModel()].Name
            + "（素材不在时会自动退回大肥鱼）");
    }

    /// <summary>按当前窗口/设置摆一次（尺寸走参考实现的 clamp 公式，位置走归一化锚点）。</summary>
    /// <summary>摆位只在"换了模型 / 尺寸真的变了"时报一行：气泡位置出问题时，日志里得能直接看到
    /// 运行时用的那几个数（清单写了什么、算出来的层中心在哪、贴图多大），别只靠看截图猜。</summary>
    private static float _petLayoutLoggedSize = -1f;
    private static int _petLayoutLoggedModel = -1;

    private static void PetLogLayoutOnce(PetModelDef pm, float baseSize, float bubW, float bubH) {
        if (pm == null) return;
        if (_petLayoutLoggedModel == _petModelIdx && Mathf.Abs(_petLayoutLoggedSize - baseSize) < 0.5f) return;
        _petLayoutLoggedModel = _petModelIdx;
        _petLayoutLoggedSize = baseSize;
        try {
            var rt = _petBubbleRt;
            var tex = (_petBubbleSprite != null) ? _petBubbleSprite.texture : null;
            Lg.Info("宠物摆位：模型=" + pm.Name
                + " S=" + baseSize.ToString("F0")
                + " buby=" + pm.BubY.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                + " 气泡框=" + (bubW * baseSize).ToString("F0") + "x" + (bubH * baseSize).ToString("F0")
                + " 层中心=" + (rt != null ? rt.anchoredPosition.x.ToString("F0") + "," + rt.anchoredPosition.y.ToString("F0") : "?")
                + " 气泡贴图=" + (tex != null ? tex.width + "x" + tex.height : "无"));
        } catch { }
    }

    private static void PetApplyLayout() {
        if (_petRootRt == null) return;
        float vw = RefW, vh = RefH;
        try {
            if (_petCanvas != null) { vw = _petCanvas.GetComponent<RectTransform>().rect.width; vh = _petCanvas.GetComponent<RectTransform>().rect.height; }
        } catch { }
        if (vw <= 1f || vh <= 1f) { vw = RefW; vh = RefH; }
        // 尺寸走参考实现的 clamp 公式（见 PetBaseSize）；位置夹取和这里共用它，两边必须算得一样。
        float baseSize = PetBaseSize(vw, vh);
        var pm = PetModels[_petModelIdx];
        float bodyH = baseSize * pm.TexK;      // 素材不是正方形，高按它自己的比例走
        PetClampPos();   // 缩放一变"能贴到的边距"也变（大宠物占的地方多）→ 每次摆位都夹一遍
        _petRootRt.anchorMin = _petRootRt.anchorMax = new Vector2(Mathf.Clamp01(PetX), Mathf.Clamp01(PetY));
        _petRootRt.sizeDelta = new Vector2(baseSize, bodyH);

        // 身体：贴父框底边、宽 = 父框（pivot 在底边中央）、高按素材比例
        if (_petBodyRt != null) {
            _petBodyRt.anchorMin = _petBodyRt.anchorMax = new Vector2(0.5f, 0f);
            _petBodyRt.sizeDelta = new Vector2(baseSize, bodyH);
            _petBodyRt.anchoredPosition = Vector2.zero;
        }
        // 气泡层：尺寸与位置都跟模型走（pm.BubW/BubH 是"这个模型的气泡框"：内建 = 资源包那张的比例，
        // 自带气泡 = 按它自己的比例塞进同一个框、贴左上角，所以不会被压扁，也不会比原来那张大）。
        // 坐标系：x 以素材左边为 0、y 以素材顶边为 0，单位都是 S；而这里的锚点在父框底边中央，
        // 素材底边离父框底边 = (TexK − 1) 个 S（大肥鱼是正方形，TexK = 1，就是原来的 (1 − oy)）。
        float bubW = pm.BubW, bubH = pm.BubH;
        if (_petBubbleRt != null) {
            _petBubbleRt.sizeDelta = new Vector2(baseSize * bubW, baseSize * bubH);
            float ox = pm.BubX + bubW * 0.5f;             // 层中心的素材坐标
            float oy = pm.BubY + bubH * 0.5f;
            _petBubbleRt.anchoredPosition = new Vector2((ox - 0.5f) * baseSize, (pm.TexK - oy) * baseSize);
            PetLogLayoutOnce(pm, baseSize, bubW, bubH);
        }
        // 文字也挂在压扁组上（跟身体同一个参照点）→ 直接用同一套坐标公式
        // （它的锚点已在 BuildPet 里显式设成 (0.5, 0)）；位置吃相对量 PetTextCxK/CyK，所以气泡框一变它跟着走
        float textCx = pm.BubX + bubW * PetTextCxK;
        float textCy = pm.BubY + bubH * PetTextCyK;
        // 文字框与命中区跟着气泡框等比缩：自带气泡比参照矮/窄时文字一起缩（宁可小一点，也别挤出轮廓）。
        // 内建那张的比例正好是 1，所以这一乘对大肥鱼一点影响都没有。
        float textW = PetTextW * (bubW / PetBubbleW), textH = PetTextH * (bubH / PetBubbleH);
        if (_petText != null) {
            // 这里不再写死字号：矩形尺寸先摆好，再让 PetFitBubbleFont 量着挑档。
            _petBase = baseSize;                  // 供"换文字时重新挑档"用
            _petText.rectTransform.sizeDelta = new Vector2(baseSize * textW, baseSize * textH);
            _petText.rectTransform.anchoredPosition =
                new Vector2((textCx - 0.5f) * baseSize, (pm.TexK - textCy) * baseSize);
            PetFitBubbleFont(_petText.text);       // 必须排在矩形尺寸之后（量高度要用真实框宽）
        }
        // 气泡里的点击区：它是气泡层的子物体（父层 pivot 在中心），局部坐标要减掉层中心、y 翻过来
        if (_petBubbleHit != null) {
            _petBubbleHit.rectTransform.sizeDelta = new Vector2(baseSize * textW * 1.1f, baseSize * textH * 1.5f);
            float lx = textCx - (pm.BubX + bubW * 0.5f);
            float ly = textCy - (pm.BubY + bubH * 0.5f);
            _petBubbleHit.rectTransform.anchoredPosition = new Vector2(lx * baseSize, -ly * baseSize);
            _petBubbleHit.transform.SetAsFirstSibling();   // 命中区画在最下面（它本来就是透明的）
        }
        // 朝向兜底：换过的根节点 scale 是 1，而 PetTickFacing 记着"已经就位"就会早退。
        // 静止时这里只比一个浮点数，不相等才补写一次（转身动画期间两边本来就相等）。
        if (_petFaceInit && Mathf.Abs(_petRootRt.localScale.x - _petFaceK) > 0.001f) PetApplyFaceScale();
    }

    /// <summary>朝向：跟着"宠物在屏幕哪一半"走 —— 抄插件的判据（图形中心在中线左侧就镜像），
    /// 效果就是永远面朝屏幕中间：贴左边朝右、贴右边朝左。
    /// 转身是有动画的（抄插件 `.dshwv-root` 那条 `transition: transform .3s ease`）：scaleX 从 1 平滑走到 -1，
    /// 中间经过 0（整只"立起来"那一瞬，看着就是转了个身）。root 与文字用同一个 scaleX：
    /// 文字的净缩放 = root × 文字 = k²，永远非负，所以字不会变成反的，且在 k = 0 那一瞬一起消失。
    /// 静止时第一行就返回，平时零开销。</summary>
    private static void PetTickFacing(float dt) {
        float want = (PetX < 0.5f) ? -1f : 1f;
        // 素材的正面画法不一样：包清单写了 face=flip 的模型，朝向整个取反（判据仍是一条）
        try {
            var def = PetModels[ResolvePetModel()];
            if (def != null && def.FaceFlip) want = -want;
        } catch { }
        if (!_petFaceInit) {                    // 刚建/重建：直接就位，别让开机先播一次转身
            _petFaceInit = true;
            _petFaceTo = want; _petFaceK = want; _petFaceT = 1f;
            PetApplyFaceScale();
            return;
        }
        if (want != _petFaceTo) {               // 目标变了：从当前位置接着转（拖回去就是转回来）
            _petFaceFrom = _petFaceK; _petFaceTo = want; _petFaceT = 0f;
        }
        if (_petFaceT >= 1f) return;
        dt = Mathf.Min(Mathf.Max(dt, 0f), PetFaceMaxStep);
        _petFaceT = Mathf.Min(1f, _petFaceT + dt / PetFaceTurnSeconds);
        _petFaceK = Mathf.Lerp(_petFaceFrom, _petFaceTo, PetEaseCss(_petFaceT));
        PetApplyFaceScale();
    }

    private static void PetApplyFaceScale() {
        if (_petRootRt != null) {
            Vector3 s = _petRootRt.localScale; s.x = _petFaceK; _petRootRt.localScale = s;
        }
        if (_petText != null) {
            Vector3 t = _petText.rectTransform.localScale; t.x = _petFaceK;
            _petText.rectTransform.localScale = t;
        }
    }

    /// <summary>CSS 那条 `ease` 曲线 = cubic-bezier(0.25, 0.1, 0.25, 1)（插件用的就是它）。
    /// 牛顿法反解参数 t 再取 y —— 只在转身那 0.3 秒里跑，几次迭代可以忽略。</summary>
    private static float PetEaseCss(float x) {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        const float x1 = 0.25f, y1 = 0.1f, x2 = 0.25f, y2 = 1f;
        float t = x;
        for (int i = 0; i < 6; i++) {
            float m = 1f - t;
            float bx = 3f * m * m * t * x1 + 3f * m * t * t * x2 + t * t * t;
            float dx = 3f * m * m * x1 + 6f * m * t * (x2 - x1) + 3f * t * t * (1f - x2);
            if (Mathf.Abs(dx) < 1e-5f) break;
            t = Mathf.Clamp01(t - (bx - x) / dx);
        }
        float mm = 1f - t;
        return 3f * mm * mm * t * y1 + 3f * mm * t * t * y2 + t * t * t;
    }

    // ================= 每帧（常驻段调用：面板关着也要跑）=================

    public static void TickPet() {
        // 素材缺失（多半是没装资源包）→ 整帧跳过：既不再每帧试读盘，也不让"建了一半又拆掉"反复发生。
        if (_petAssetsMissing) return;
        if (!PetWanted()) { SetPetVisible(false); PetDropStaleSay(); return; }
        if (_petCanvas == null) {
            try { BuildPet(); } catch (System.Exception e) { Lg.Ex("宠物建立失败", e); return; }
        }
        if (_petCanvas == null) return;
        SetPetVisible(true);
        PetApplyLayout();       // 很便宜（几次数值赋值）→ 每帧算，窗口/设置一变立刻跟上
        PetTickFacing(Time.unscaledDeltaTime);   // 朝向（转身动画，静止时零开销）
        PetTickSquash();
        PetTickBubble();
        PetTickRandomSay();      // 每秒掷一次骰子（宠物在屏幕上就掷，中了才说）
        PetTickSound();
    }

    /// <summary>随机发言：宠物可见时每秒掷一次骰子，中了就走跟"点气泡抽台词 / 通关说一句"同一条通道
    /// （RandomLineIndex + PetArmLine：去重、挑字号、淡入全都复用，不自己写文字）。本函数排在 TickPet 的
    /// 可见性早退之后，宠物不显示就到不了这里；判"在不在打谱"不许用 IsLevelPlaying()（它会把暂停和编辑器一起排除掉）。</summary>
    private static void PetTickRandomSay() {
        try {
            float chance = PetRandomSayProb();
            if (chance <= 0f) return;
            float now = Time.unscaledTime;
            if (_petRandomNextAt <= 0f) {
                _petRandomNextAt = now + 1f;
                // 起算时打一行：日志里才能分清"根本没在掷"和"掷了没中"。
                _petRandomRolls = 0; _petRandomHits = 0;
                Lg.Info($"[LevelLib] 大肥鱼：随机发言起算 每秒一次、当前 {PetRandomSayText()}"
                    + $"（宠物可见=是｜之后每 60 次掷骰报一行进度）");
                // 顺手把"调试开关看的是哪个文件"打出来：路径对不对一眼就能看出来。
                try {
                    Lg.Info("大肥鱼：调试开关路径 = "
                        + System.IO.Path.Combine(Main.ModEntry.Path, "debug_petsay.txt")
                        + "（存在=" + (PetRandomSayDebug() ? "是" : "否") + "）");
                } catch { }
                return;
            }
            if (now < _petRandomNextAt) return;
            // 到点掷一次；掉帧掉过头也只补掷一次，不排队。
            _petRandomNextAt = now + 1f;
            _petRandomRolls++;
            // 每 60 次报一行进度：次数在涨就说明机制在跑（没中不打日志）。
            if (_petRandomRolls % PetRandomHeartbeatRolls == 0) {
                Lg.Info($"[LevelLib] 大肥鱼：随机发言进度 —— 已掷骰 {_petRandomRolls} 次、"
                    + $"命中 {_petRandomHits} 次、当前 {PetRandomSayText()}"
                    + $"（1% 档期望：每 100 次左右中一次 ->  再掷 {(int)(1f / Mathf.Max(chance, 0.0001f))} 次上下）");
            }
            if (UnityEngine.Random.value >= chance) return;
            _petRandomHits++;
            int idx = RandomLineIndex();
            PetArmLine(null, idx);
            // 日志带上"这是第几次掷骰才中的"，没等够时能自己估还要多久。
            Lg.Info($"[LevelLib] 大肥鱼：随机发言（每秒 {PetRandomSayText()}，"
                + $"建宠物以来第 {_petRandomRolls} 次掷骰）→ {ActiveLines()[idx]}");
        } catch { }   // 说一句而已，任何异常都不许影响打谱
    }

    /// <summary>按压的弹簧：scaleY 1→0.88、scaleX 1→1.05，带一点回弹。用弹簧而不是一次性曲线，是因为
    /// 快速连点时目标一直在变 —— 弹簧永远从当前值接着走，不重置、不卡顿。
    /// 这一份缩放同时发给压扁组（气泡和文字跟着一起压），原点与身体同一个点（底边中央）。</summary>
    private static void PetTickSquash() {
        if (_petBodyRt == null) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);     // 掉帧时别把弹簧炸飞
        float target = _petPressed ? 1f : 0f;
        const float w = 18f, z = 0.42f;                          // ω≈18（周期 ~0.35s）、ζ≈0.42 → 约两下回弹
        float acc = (target - _petSquash) * w * w - _petSquashV * 2f * z * w;
        _petSquashV += acc * dt;
        _petSquash += _petSquashV * dt;
        if (Mathf.Abs(_petSquash - target) < 0.0015f && Mathf.Abs(_petSquashV) < 0.02f) { _petSquash = target; _petSquashV = 0f; }
        var scale = new Vector3(1f + 0.05f * _petSquash, 1f - 0.12f * _petSquash, 1f);
        _petBodyRt.localScale = scale;
        // 气泡 + 文字那一组跟着压（同一个缩放、同一个原点）
        if (_petSquishRt != null) _petSquishRt.localScale = scale;
    }

    /// <summary>气泡：延迟打开 → 淡入（同时 0.7→1 缩放）→ 文字随后出现 → 没点就自动淡出。</summary>
    private static void PetTickBubble() {
        if (_petBubbleRt == null) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        if (_petOpenDelay > 0f) {                 // 松手后等按压缩放动画放完再开
            _petOpenDelay -= dt;
            if (_petOpenDelay <= 0f) { _petOpenDelay = 0f; PetOpenBubble(); }
        }

        bool open = _petBubbleTimer > 0f && PetBubbleOn;
        // 气泡真显示出来了，撤掉"通关那句还挂着"的标记（它只管宽限期内必须显示出来）
        if (open && _petSayAt > 0f) _petSayAt = 0f;
        if (PetBubbleOn && _petBubbleTimer > 0f) {
            _petBubbleTimer -= dt;
            if (_petBubbleTimer <= 0f) _petBubbleTimer = 0f;
            // 文字每秒刷一次就够（别每帧拼字符串）
            int sec = (int)(Time.unscaledTime);
            if (sec != _petShownSec) { _petShownSec = sec; PetRefreshText(); }
        }

        // ---- 三片错峰出现（等价于 CSS 的 transition-delay）----
        // 只有开/关的边沿才重新计时：延迟都从"状态变化那一刻"起算。
        if (open != _petBubbleOpenWas) { _petBubbleOpenWas = open; _petBubbleClock = 0f; }
        else _petBubbleClock += dt;
        PetStepPart(ref _petDotFarA, open, PetDotFarIn, PetDotFarOut, dt);     // 最外的小泡（0s 最先）
        PetStepPart(ref _petDotNearA, open, PetDotNearIn, PetDotNearOut, dt);  // 近的小泡（.13s）
        PetStepPart(ref _petBubbleA, open, PetBigIn, PetBigOut, dt);           // 大气泡（.26s 最后）
        PetApplyPart(_petBubbleImg, _petBubbleA);
        PetApplyPart(_petBubbleRightImg, _petBubbleA);      // 右下那片跟大气泡同一组、同一个 alpha
        PetApplyPart(_petDotNearImg, _petDotNearA);
        PetApplyPart(_petDotFarImg, _petDotFarA);

        // ---- 文字自己的透明度动画 ----
        // _petTextA 是文字自己的 0→1（换台词/重开气泡时归零，0.25 秒淡入）；再乘一道"大气泡到 35%
        // 之后才露头"的门（等价于插件 .dshwv-text 的 opacity .16s ease .36s）。
        // 数字每秒刷新不重置 _petTextA，否则「今日游玩时间」会每秒闪一下。
        if (_petText != null) {
            float tTarget = (open && _petBubbleA > 0.35f) ? 1f : 0f;
            _petTextA = Mathf.MoveTowards(_petTextA, tTarget, dt / PetTextFadeSeconds);
            float gate = Mathf.Clamp01((_petBubbleA - 0.35f) / 0.65f);
            var tc = _petText.color;
            _petText.color = new Color(tc.r, tc.g, tc.b, Mathf.Clamp01(_petTextA * gate));
        }
        // 气泡层不做整层缩放：三片各自绕自己中心 0.7→1。整层显隐按"任一片还看得见"算
        // （最外那个小泡最先出现、最后消失）。
        float anyA = Mathf.Max(_petBubbleA, Mathf.Max(_petDotNearA, _petDotFarA));
        if (_petBubbleRt != null) _petBubbleRt.gameObject.SetActive(anyA > 0.002f);
        // 文字不在气泡层下面（它在压扁组里），得自己显隐
        if (_petText != null) _petText.gameObject.SetActive(_petBubbleA > 0.002f);
    }

    /// <summary>按各自的延迟推进一片的透明度（等价于 CSS 的 transition-delay：还没到点就一动不动）。</summary>
    private static void PetStepPart(ref float a, bool open, float delayIn, float delayOut, float dt) {
        float delay = open ? delayIn : delayOut;
        if (_petBubbleClock < delay) return;
        a = Mathf.MoveTowards(a, open ? 1f : 0f, dt / PetPartFadeSeconds);
    }

    /// <summary>把一片的透明度 / 0.7→1 缩放落到 Image 上（每片绕自己的中心）。</summary>
    private static void PetApplyPart(Image img, float a) {
        if (img == null) return;
        img.color = new Color(1f, 1f, 1f, a);
        float s = Mathf.Lerp(1f, 0.7f, 1f - a);
        img.rectTransform.localScale = new Vector3(s, s, 1f);
    }

    /// <summary>三片一起归零并重新计时：点本体 / 重开气泡那几处调，这样错峰从"新一次出现"算起。</summary>
    private static void PetResetBubbleParts() {
        _petBubbleA = 0f; _petDotNearA = 0f; _petDotFarA = 0f;
        _petBubbleClock = 0f;
        _petBubbleOpenWas = false;   // 下一帧看到"开"的边沿，延迟从那一刻重新算
    }

    /// <summary>声音调度：Ya2 从 Ya1 还剩 100ms 时开始播（连点时两段音接着、不出现空档）；
    /// 顺手跑 PetAudio 那边的出声探针。</summary>
    private static void PetTickSound() {
        if (_petReleaseDueAt > 0f && Time.unscaledTime >= _petReleaseDueAt) {
            _petReleaseDueAt = -1f;
            if (_petAudio != null) _petAudio.PlayRelease(PetVol);
        }
        if (_petAudio != null) _petAudio.TickProbe();
    }

    // ================= 交互（由 PetWidget 那个 MonoBehaviour 转发进来）=================

    /// <summary>按下去：压扁 + 播 Ya1，顺手把上一轮的 Ya2 掐掉（参考实现 playPress）。
    /// 这里不能因为 _petDragging 就 return —— 那个标志一旦"拖到一半关了面板 / 没收到松手事件"就会
    /// 永久卡在 true，之后按下不压扁、不播音、不弹气泡，而且没有日志；OnPointerDown 必然是一次新手势的开始。</summary>
    public static void PetPress(Vector2 screenPos) {
        _petDragging = false;      // 不能 return：卡住就整只宠物哑掉（见上面的说明）
        _petPressed = true;
        _petPressScreen = screenPos;
        // 抓取偏移在按下这一刻就记下来：鼠标抓住了宠物的哪一块，拖动时那一块就一直贴着鼠标。
        // 必须在这里记而不是等拖动开始 —— 拖动开始那一刻宠物已经被判过阈值了，那时候再记
        // 就只能在"已经摆到鼠标底下"的位置上记，等于没记。
        _petGrabOffset = Vector2.zero;
        if (PetNormalizedAt(screenPos, out Vector2 grabNorm))
            _petGrabOffset = grabNorm - new Vector2(PetX, PetY);
        _petReleaseDueAt = -1f;
        if (PetSoundOn && _petAudio != null) _petAudio.PlayPress(PetVol);
    }

    /// <summary>松开：回弹 + 播 Ya2（Ya1 没放完就提前 100ms 接上）。点本体只刷新气泡、不换台词：
    /// 气泡关着就等缩放动画放完再开、显示「今日游玩时间」；已开着就回到那一屏并把停留时间续上
    /// （台词只由"点气泡"推进）。</summary>
    public static void PetRelease() {
        if (_petDragging) return;
        _petPressed = false;
        if (PetSoundOn && _petAudio != null) {
            float remain = _petAudio.PressRemaining();
            if (remain > 0.1f) _petReleaseDueAt = Time.unscaledTime + (remain - 0.1f);   // 还剩 100ms 时开播
            else _petAudio.PlayRelease(PetVol);                                         // Ya1 已经放完 → 立刻
        }
        if (!PetBubbleOn) return;
        // 每次点本体都把气泡直接归零再重新淡入（要的是"气泡闪一下再出现"，不是一直挂在那儿），
        // 三片一起归零并重新计时，错峰也从这一次重新开始。
        PetResetBubbleParts();
        _petTextA = 0f;            // 文字也跟着从 0 重新淡入
        _petLineIdx = -1;          // 回到「今日游玩时间」
        _petSayText = null;        // 插播那句也一起放掉
        _petShownSec = -1;         // 下一帧就把时间刷新出来
        if (_petBubbleTimer > 0f) _petBubbleTimer = PetBubbleSeconds;   // 开着 → 只续停留时间
        else _petOpenDelay = PetOpenDelay;                              // 关着 → 等动画放完再开
    }

    /// <summary>拖动（松手后按参考实现做四边吸附）。夹的是"整个宠物都留在屏幕内"，不是"中心在 0~1" ——
    /// 后者会让宠物有一半在屏幕外；吸附位置也是"贴着边"（中心离边 = 半个宠物），不是离边 10%。</summary>
    public static void PetDragTo(Vector2 screenPos) {
        if (_petCanvas == null || _petRootRt == null) return;
        // 位移没过阈值就还不算拖动（同 PetWidget 里那条判据）：手快时十几像素的抖动会被 Unity 当成拖动，
        // 这里一旦把 _petDragging 置上，抬手时 PetRelease 就会被它挡掉，气泡既不重闪也不回今日游玩时间。
        if (!_petDragging
            && (screenPos - _petPressScreen).sqrMagnitude < PetDragSlopPx * PetDragSlopPx) return;
        _petDragging = true;
        if (!PetNormalizedAt(screenPos, out Vector2 norm)) return;
        // 减掉按下时记的抓取偏移：宠物跟着鼠标平移，而不是"吸"到鼠标底下。
        // 不减的话，鼠标按在宠物下缘、一动超过阈值，宠物就会往下跳一段（跳到让鼠标对准它的中心）。
        PetX = norm.x - _petGrabOffset.x;
        PetY = norm.y - _petGrabOffset.y;
        PetClampPos();          // 整个宠物留在屏幕内
        PetApplyLayout();
    }

    /// <summary>把屏幕坐标换算成"宠物中心该在的归一化位置"（0~1，相对画布左下角）。
    /// 拖动与"记抓取偏移"两处都走它 —— 两边各算一遍的话，迟早有一处忘了减偏移。
    /// 返回 false = 换算不出来（画布还没建 / 点在画布外），调用方直接别动位置。</summary>
    private static bool PetNormalizedAt(Vector2 screenPos, out Vector2 norm) {
        norm = Vector2.zero;
        if (_petCanvas == null) return false;
        var crt = _petCanvas.GetComponent<RectTransform>();
        if (crt == null) return false;
        var cam = _petCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _petCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(crt, screenPos, cam, out Vector2 local)) return false;
        norm = new Vector2(
            (local.x - crt.rect.xMin) / Mathf.Max(1f, crt.rect.width),
            (local.y - crt.rect.yMin) / Mathf.Max(1f, crt.rect.height));
        return true;
    }

    /// <summary>把位置夹在"整个宠物都在屏幕内"的范围里；半宽/半高按当前画布尺寸与当前大小算
    /// （缩放变了范围也跟着变）。</summary>
    private static void PetClampPos() {
        float halfX = 0.02f, halfY = 0.04f;      // 兜底（拿不到画布尺寸时也别夹死）
        try {
            var crt = _petCanvas != null ? _petCanvas.GetComponent<RectTransform>() : null;
            if (crt != null && crt.rect.width > 1f && crt.rect.height > 1f) {
                float baseSize = PetBaseSize(crt.rect.width, crt.rect.height);
                halfX = baseSize * 0.5f / crt.rect.width;
                halfY = baseSize * PetModels[_petModelIdx].TexK * 0.5f / crt.rect.height;
            }
        } catch { }
        PetX = Mathf.Clamp(PetX, halfX, 1f - halfX);
        PetY = Mathf.Clamp(PetY, halfY, 1f - halfY);
    }

    /// <summary>宠物基准边长（参考实现的 clamp 公式）；位置夹取和摆位都用它，两边必须算得一样。</summary>
    private static float PetBaseSize(float vw, float vh) {
        if (vw <= 1f || vh <= 1f) { vw = RefW; vh = RefH; }
        return Mathf.Clamp(Mathf.Min(250f, Mathf.Min(vw, vh) * 0.28f) * Mathf.Clamp(PetScale, 0.5f, 2.5f), 122f, 625f);
    }

    /// <summary>拖动结束：按 0.25/0.75 分区吸附到最近的边，吸附位置是"贴着边"而不是离边一段距离。</summary>
    public static void PetDragEnd() {
        if (!_petDragging) return;
        _petDragging = false;
        _petPressed = false;
        var crt = _petCanvas != null ? _petCanvas.GetComponent<RectTransform>() : null;
        float vw = crt != null ? crt.rect.width : RefW, vh = crt != null ? crt.rect.height : RefH;
        float baseSize = PetBaseSize(vw, vh);
        float halfX = vw > 1f ? baseSize * 0.5f / vw : 0.02f;      // 贴左/右边时中心该在的位置
        float halfY = vh > 1f ? baseSize * PetModels[_petModelIdx].TexK * 0.5f / vh : 0.04f;
        // 关掉自动吸附时只是不做四边吸附；PetClampPos() 两条路都要走 —— 自由 ≠ 能拖出屏幕。
        bool snapped = PetSnapEdge;
        if (snapped) {
            if (PetX < 0.25f) PetX = halfX;             // 靠左 → 贴左边
            else if (PetX > 0.75f) PetX = 1f - halfX;   // 靠右 → 贴右边
            if (PetY < 0.25f) PetY = halfY;             // 靠下 → 贴下边
            else if (PetY > 0.75f) PetY = 1f - halfY;   // 靠上 → 贴上边
        }
        PetClampPos();
        PetApplyLayout();
        AutoFeatures.Save();       // 位置落盘
        Lg.Info($"[LevelLib] 大肥鱼位置：({PetX:F3}, {PetY:F3})"
            + $"（{(snapped ? "已贴边" : "自由位置 · 未吸附")} 半宽/半高 {halfX:F3}/{halfY:F3}）");
    }

    // ================= 气泡内容 =================

    /// <summary>点气泡本身：当前是「今日游玩时间」→ 换成一段随机台词；当前是台词 → 再点一下收起来。
    /// 想再看时间就点本体（会回到那一屏并重播淡入）。</summary>
    public static void PetBubbleClick() {
        if (!PetBubbleOn) return;
        if (_petBubbleTimer <= 0f) { _petOpenDelay = PetOpenDelay; return; }   // 气泡没开着 → 当普通一次点击

        if (_petLineIdx < 0) {
            // 现在是「今日游玩时间」→ 换一段随机台词
            _petLineIdx = RandomLineIndex();
            _petSayText = null;         // 点了就把插播那句放掉（回随机池）
            _petShownSec = -1;          // 下一帧就把台词写上
            _petTextA = 0f;             // 换文本 → 文字重新淡入
            _petBubbleTimer = PetBubbleSeconds;
            Lg.Info("大肥鱼：随机台词 → " + ActiveLines()[_petLineIdx]);
            return;
        }
        // 现在是台词 → 收起来
        _petBubbleTimer = 0f;
        _petLineIdx = -1;
        _petSayText = null;
        _petShownSec = -1;
        _petTextA = 0f;
        Lg.Info("大肥鱼：点气泡收起（再点本体会显示今日游玩时间）");
    }

    /// <summary>通关时说一句（触发点只有 AutoFeatures.OnBeatLevel 的开头）。只判宠物总开关和气泡开关 ——
    /// 自动播放 / 不会失败 / 用了检查点 / 判定表没过一律不判。走的通道跟点气泡抽台词完全同一条，
    /// 不自己写文字，否则字号档和换行逻辑会跟手动那条漂开。</summary>
    public static void PetSayOnClear() {
        try {
            if (_petAssetsMissing) return;              // 没装资源包，本来就没有宠物可显示
            if (!PetShow || !PetBubbleOn) return;       // 玩家自己关掉的，不硬塞
            float now = Time.unscaledTime;
            if (now - _petSayLastAt < PetSayMinGap) return;   // 同一次通关只播一次
            int idx = RandomLineIndex();                // 随机抽一条（跟上一条不重）
            PetArmLine(null, idx);                      // 走同一条"武装气泡"的通道（见下面）
            Lg.Info("大肥鱼：通关 → " + ActiveLines()[idx]);
        } catch { }   // 说一句而已，任何异常都不许影响通关流程
    }

    /// <summary>插播一句固定台词（「当天玩了 60 / 300 / 600 分钟」那三条、死在 99% 的七句、
    /// 音乐名含 terrasphere 那句都走这里）。它们不进随机池 —— 否则点气泡可能随机冒出
    /// 只有真到点了才说得通的话。当前不是大肥鱼时统一改说这个模型的"固定那句"
    /// （进谱面过渡用的是同一句，这里收口一处就够，调用方不用各判一遍）。</summary>
    /// <summary>当前生效的模型（定时台词每帧要问；表还没建好时返回 null）。</summary>
    public static PetModelDef PetActiveModel() {
        try { return (_petModelIdx >= 0 && _petModelIdx < PetModels.Count) ? PetModels[_petModelIdx] : null; }
        catch { return null; }
    }

    /// <summary>开谱面/插播那句：多条时随机挑一句（等概率），只有一句就照旧。</summary>
    public static string PetPickSayLine(PetModelDef m) {
        if (m != null && m.SayLines != null && m.SayLines.Length > 1) return m.SayLines[UnityEngine.Random.Range(0, m.SayLines.Length)];
        return m != null ? m.SayLine : null;
    }

    public static void PetSayLine(string text, string tag) {
        try {
            if (_petAssetsMissing) return;
            if (!PetShow || !PetBubbleOn) return;
            if (_petModelIdx != 0 && _petModelIdx < PetModels.Count) {
                string fixedLine = PetPickSayLine(PetModels[_petModelIdx]);
                if (!string.IsNullOrEmpty(fixedLine)) text = fixedLine;
            }
            if (string.IsNullOrEmpty(text)) return;
            PetArmLine(text, -2);          // -2 = 插播中（既不是「今日游玩时间」也不是池子里的第 N 条）
            // 说话时带一下"点它"那个按压声（点气泡是同一个音源）；玩家关了宠物音效就静
            if (PetSoundOn && _petAudio != null) _petAudio.PlayPress(PetVol);
            Lg.Info("大肥鱼：" + (string.IsNullOrEmpty(tag) ? "插播" : tag) + " → " + text);
        } catch { }
    }

    /// <summary>把气泡"武装"成马上要说这句话 —— 通关播报与到点插播共用这一处。它负责四件事：
    /// 挂上台词、下一帧必定刷新、从看不见重新淡入、掐掉还没到点的"开气泡"（否则它随后会把台词顶回
    /// 「今日游玩时间」）。停留时间用 PetSayHoldSeconds。fixedText != null = 固定台词，null = 走随机池第 poolIdx 条。</summary>
    private static void PetArmLine(string fixedText, int poolIdx) {
        _petSayText = fixedText;
        _petLineIdx = poolIdx;
        _petShownSec = -1;
        _petTextA = 0f;
        _petBubbleA = 0f;
        _petOpenDelay = 0f;
        _petBubbleTimer = PetSayHoldSeconds;
        _petSayAt = Time.unscaledTime;
        _petSayLastAt = _petSayAt;
    }

    /// <summary>宠物当时不在屏幕上时，这次"通关要说的话"过期就丢掉（宽限期见 PetSayGraceSeconds）。</summary>
    private static void PetDropStaleSay() {
        if (_petSayAt <= 0f) return;
        if (Time.unscaledTime - _petSayAt <= PetSayGraceSeconds) return;
        _petSayAt = 0f;
        _petBubbleTimer = 0f;
        // 插播那句也要放掉：插播时 _petLineIdx 是 -2（不是 >= 0），只判 >= 0 会漏掉它、那句话就粘在气泡上。
        _petSayText = null;
        _petLineIdx = -1;
        _petShownSec = -1;
        Lg.Info("大肥鱼：通关那句没赶上显示（宠物当时不在屏幕上）→ 丢掉");
    }

    /// <summary>随机挑一条台词，但不跟上一条重复（老是抽到同一条会显得像卡住）。
    /// 池子按当前模型取（Tiper 只有一句，直接返回 0）。</summary>
    private static int _petLastLine = -2;
    private static int RandomLineIndex() {
        var pool = ActiveLines();
        if (pool.Length <= 1) return 0;
        int idx = UnityEngine.Random.Range(0, pool.Length);
        if (idx == _petLastLine) idx = (idx + 1 + UnityEngine.Random.Range(0, pool.Length - 1)) % pool.Length;
        _petLastLine = idx;
        return idx;
    }

    private static void PetOpenBubble() {
        _petBubbleTimer = PetBubbleSeconds;
        _petLineIdx = -1;            // 这一次"开气泡"从「今日游玩时间」开始
        _petSayText = null;          // 插播那句一并放掉
        _petShownSec = -1;
        _petTextA = 0f;              // 文字从透明淡入
        PetResetBubbleParts();       // 三片归零 + 错峰重新计时
        PetRefreshText();
    }

    /// <summary>气泡文字：固定插播 > 台词池 > 「今日游玩时间 mm:ss」（实时）。</summary>
    private static void PetRefreshText() {
        if (_petText == null) return;
        string s;
        if (_petSayText != null) {
            // 插播优先：它就是"这一刻要说的话"，折行与自动挑字号走同一条链路，不需要另写一套。
            s = _petSayText;
        } else if (_petLineIdx < 0) {
            // 读 PlayTime（按帧自己累计、一天之内累计，落盘在 playtime.txt）：
            // 从 playlog 推的话，今天没记录就一直是 0。
            s = Lang.Tr("今日游玩时间") + "\n" + PlayTime.TodayText();
        } else {
            // 池子按当前模型取；换模型那一刻 _petLineIdx 会被归到 -1，这里的越界保护只是兜底。
            var pool = ActiveLines();
            s = (_petLineIdx < pool.Length) ? pool[_petLineIdx] : pool[0];
        }
        // 台词库（Data3 那 51 句）与插播都要过 Tr 才跟随语言；已翻文本再 Tr 会原样回落，无害
        string shown = Lang.Tr(s);
        _petText.text = shown;
        PetFitBubbleFont(shown);    // 换文字就重新挑档（短句大字 / 长句退档换行）——量宽用翻译后的串
    }

    // ================= 气泡字号：自动挑档 =================

    /// <summary>字号档（× 头长，从大到小试；最后一档就是下限 PetTextFontK）。</summary>
    private static readonly float[] PetTextSizeSteps = { 0.17f, 0.16f, 0.15f, 0.14f, PetTextFontK };

    /// <summary>当前头长（PetApplyLayout 里量出来的，换文字时挑档要用）。</summary>
    private static float _petBase;

    /// <summary>挑字号：从大档往小档试，取第一个能把这段文字装进文字框的档 —— 短句吃到大字，
    /// 长句退一档两档、靠换行装下（不是被压成小字）。装不装得下用 uGUI 自己的 preferredHeight 量
    /// （真实字体度量），所以矩形尺寸必须先摆好；下限 PetTextFontK 是"不缩字"口径下的一行 ≥8 个中文字。</summary>
    private static void PetFitBubbleFont(string text) {
        try {
            if (_petText == null || _petBase <= 0f) return;
            float boxW = _petBase * PetTextW;
            float boxH = _petBase * PetTextH;
            int floor = Mathf.RoundToInt(Mathf.Clamp(_petBase * PetTextFontK, 12f, 40f));
            for (int i = 0; i < PetTextSizeSteps.Length; i++) {
                int fs = Mathf.RoundToInt(Mathf.Clamp(_petBase * PetTextSizeSteps[i], (float)floor, 44f));
                if (fs < floor) fs = floor;
                ApplyPetFontSize(fs);
                if (string.IsNullOrEmpty(text)) return;
                if (_petText.preferredHeight > boxH + 0.5f) continue;       // 装不下就降一档
                // 尾行太稀（只剩一两个字符，比如「终于上当了！」的「！」被挤到第二行），降一档看看
                if (PetEstimateRows(text, boxW, fs, out int rows, out float lastW)
                    && rows > 1 && lastW < 0.30f * boxW) continue;
                return;                                                     // 这一档可用，就它
            }
        } catch { }
    }

    /// <summary>按宽度估折行，只为判断"尾行稀不稀"；装不装得下仍用真实度量 preferredHeight。
    /// 字宽模型：全角 1.0 / 字母数字 0.6 / 空格 0.32 / 其它半角 0.35（都要乘字号）。</summary>
    private static bool PetEstimateRows(string text, float boxW, int fs, out int rows, out float lastW) {
        rows = 1; lastW = 0f;
        try {
            float cur = 0f;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c == '\n') { rows++; lastW = 0f; cur = 0f; continue; }
                float k = c == ' ' ? 0.32f
                    : (c < 128 ? (((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) ? 0.60f : 0.35f)
                               : 1.0f);
                float w = k * fs;
                if (cur + w > boxW + 0.01f) { rows++; lastW = 0f; cur = 0f; }
                cur += w; lastW += w;
            }
            return true;
        } catch { return false; }
    }

    /// <summary>把一个字号落到 Text 上：bestFit 的上限/下限都要跟着它走，否则它会缩回旧值。</summary>
    private static void ApplyPetFontSize(int fs) {
        if (_petText == null) return;
        _petText.fontSize = fs;
        _petText.resizeTextMaxSize = fs;
        // 保险丝下限：正常用不到，只有比最长台词还长的句子才会碰到
        _petText.resizeTextMinSize = Mathf.Max(PetTextFuseMin, Mathf.RoundToInt(fs * 0.75f));
    }

    // ================= 设置项（extra → 大肥鱼 那一页调）=================

    /// <summary>设置一改就调它：位置/大小/显隐立刻生效，并落盘。</summary>
    public static void PetApplySettings() {
        PetApplyLayout();
        if (PetBubbleOn && _petBubbleTimer > 0f && _petBubbleImg != null) { /* 保持当前气泡 */ }
        if (_petAudio != null) _petAudio.ApplyVolume(PetVol);
        AutoFeatures.Save();
    }
}

/// <summary>宠物本体的点击/拖动转发（uGUI 事件 → PortalUI 的静态处理函数）。
/// 气泡和身体各挂一个：身体 = 按压/拖动；气泡 = 点它切下一段台词。</summary>
public class PetWidget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IBeginDragHandler, IEndDragHandler {
    public bool IsBubble;
    private Vector2 _downPos;
    private bool _moved;

    public void OnPointerDown(PointerEventData e) {
        _downPos = e.position;
        _moved = false;
        if (!IsBubble) PortalUI.PetPress(e.position);
    }

    public void OnBeginDrag(PointerEventData e) {
        if (IsBubble) return;                 // 气泡不参与拖动（免得手一抖就把宠物拖走）
        _moved = true;
        PortalUI.PetDragTo(e.position);
    }

    public void OnDrag(PointerEventData e) {
        if (IsBubble) return;
        PortalUI.PetDragTo(e.position);
    }

    public void OnPointerUp(PointerEventData e) {
        if (IsBubble) { PortalUI.PetBubbleClick(); return; }
        // 判"点击还是拖动"只看手真的挪了多少像素，不看 _moved：手快的时候十几像素的抖动就够让 Unity
        // 发拖动事件了，而那时走拖动那一支会把气泡的"重闪一下 + 回到今日游玩时间"整段跳掉。
        if ((e.position - _downPos).sqrMagnitude > PortalUI.PetDragSlopPx * PortalUI.PetDragSlopPx) {
            PortalUI.PetDragEnd();
            return;
        }
        PortalUI.PetRelease();
        if (_moved) PortalUI.PetDragEnd();   // 位置那一边也要收尾（没真拖过时 PetDragEnd 自己早退）
    }

    public void OnEndDrag(PointerEventData e) {
        if (IsBubble) return;
        PortalUI.PetDragEnd();
    }
}

/// <summary>宠物音效。宿主 DontDestroyOnLoad、ignoreListenerPause = true（暂停里也要响）、两路音源各按一次 Play()。
/// 不能用 DownloadHandlerAudioClip 的内容：那份采样挂在下载器持有的原生缓冲上，关卡场景 Awake 里的
/// Resources.UnloadUnusedAssets() 会把数据回收掉（对象还在、数据没了，播了等于没播）
/// 所以自己解析 wav、采样放托管内存、用 AudioClip.Create 的回调喂；响度已在素材文件上放大过，运行时补不上去。</summary>
public class PetAudio : MonoBehaviour {
    /// <summary>自己持有的 PCM（托管内存，不受 Resources.UnloadUnusedAssets 影响）。
    /// Pos 是按样本数（含声道交错）的读指针：Unity 在音频线程上调回调，我们只读数组。
    /// 逻辑时长一律用这里的样本数算，不读 clip.length —— 回调式 clip 的 samples/length 恒为 0。</summary>
    private sealed class PcmSource {
        public float[] Samples;
        public int Channels;
        public int Rate;
        public int Pos;
        public float LengthSeconds { get { return Rate > 0 && Channels > 0 ? Samples.Length / (float)Channels / Rate : 0f; } }
    }

    private AudioSource _press, _release;
    private AudioClip _pressClip, _releaseClip;
    private PcmSource _pressSrc, _releaseSrc;
    private bool _loading;
    private float _pressStartAt = -1f;      // Ya1 开始播的时刻（算"还剩多少"用）

    /// <summary>AudioClip.SetData(float[], int) 用反射拿：Unity 6 里 GetData/SetData 多了一个 Span 重载，
    /// 编译器会挑那个，而 net48 没有 Span，CS7069 / CS0518 编译不过。数组那个重载多半还在（只是编译期
    /// 选不中），反射按参数类型精确挑：挑到就是"内存态"clip（samples / length / loadState 全正常），
    /// 挑不到退回回调式（也能响，只是那个 clip 的 samples 读出来是 0）。</summary>
    private static MethodInfo _setDataArr;
    private static bool _setDataProbed;

    private static void ProbeSetData() {
        if (_setDataProbed) return;
        _setDataProbed = true;
        try {
            var sigs = new System.Collections.Generic.List<string>();
            foreach (var m in typeof(AudioClip).GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                if (m.Name != "SetData" && m.Name != "GetData") continue;
                try {
                    var ps = m.GetParameters();
                    var names = new string[ps.Length];
                    for (int i = 0; i < ps.Length; i++) names[i] = ps[i].ParameterType.Name;
                    sigs.Add(m.Name + "(" + string.Join(", ", names) + ")");
                    if (m.Name == "SetData" && ps.Length == 2 && ps[0].ParameterType == typeof(float[]) && ps[1].ParameterType == typeof(int))
                        _setDataArr = m;
                } catch (System.Exception e2) {
                    sigs.Add("?（这个重载的参数类型读不出来：" + e2.GetType().Name + " 多半就是 Span 那个）");
                }
            }
            Lg.Info("AudioClip 数据接口探查：" + string.Join(" | ", sigs.ToArray())
                + " → " + (_setDataArr != null ? "用反射 SetData(float[],int) 内存态 clip" : "没有数组重载 → 走回调式"));
        } catch (System.Exception e) {
            Lg.Ex("AudioClip 接口探查失败", e);
        }
    }

    public void Setup() {
        _press = gameObject.AddComponent<AudioSource>();
        _release = gameObject.AddComponent<AudioSource>();
        foreach (var s in new[] { _press, _release }) {
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;                  // 2D（别被空间衰减吞掉）
            s.volume = PortalUI.PetVol;           // 初始音量（之后由设置滑块走 ApplyVolume）
            s.mute = false;
            s.ignoreListenerPause = true;         // 暂停时也响
        }
        // 先走"同步自己解析 wav"这条路：小文件几十 KB，主线程读一下无所谓，也不会有"还没加载完"。
        ProbeSetData();          // 先探一次 AudioClip 的数据接口（决定下面用哪条路，结果会打进日志）
        _pressClip = LoadWavOwned("Ya1", out _pressSrc);
        _releaseClip = LoadWavOwned("Ya2", out _releaseSrc);
        if (_pressClip == null || _releaseClip == null) {
            Lg.Info("宠物音效：wav 没走通 → 退回 mp3 的老路（异步 UWR 有「数据被回收」的老毛病）");
            if (!_loading) { _loading = true; StartCoroutine(LoadMp3Fallback()); }
        }
        Lg.Info("宠物音效宿主就绪：" + AudioEnvLine());   // 出声条件一次性打全
    }

    /// <summary>把"能不能出声"的环境条件写成一行：排查"点了没声音"时先看这行，再看每次播放那行。</summary>
    private string AudioEnvLine() {
        int listeners = -1;
        try { listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length; } catch { }
        return $"监听器={listeners} AudioListener.volume={AudioListener.volume:F2} pause={AudioListener.pause}"
             + $" 宿主active={gameObject.activeInHierarchy} 音量设置={PortalUI.PetVol:F2} 开关={PortalUI.PetSoundOn}";
    }

    // ================= 读 wav → 自己持有的 AudioClip =================

    /// <summary>assets\pet\名字.wav → 我们自己持有采样数据的 AudioClip。只认最简单的 wav：RIFF/WAVE、
    /// audioFormat=1（PCM）、16bit；任何一步不对就返回 null，由上层退回 mp3（宁可没声音也别播噪音）。
    /// 字节解析全在 ParseWavPcm16 里，而且故意不碰任何 Unity 类型（好离线单独跑）。</summary>
    private static AudioClip LoadWavOwned(string baseName, out PcmSource src) {
        src = null;
        string path = System.IO.Path.Combine(Main.ModEntry.Path, "assets", "pet", baseName + ".wav");
        try {
            if (!System.IO.File.Exists(path)) {
                // 缺 wav 就走 mp3 兜底（正常发布包里两个都在）：报给 AssetPack 一次会话一句、不带路径
                AssetPack.NoteMissing("pet/" + baseName + ".wav");
                return null;
            }
            var b = System.IO.File.ReadAllBytes(path);
            int channels, rate;
            var samples = ParseWavPcm16(b, out channels, out rate);
            if (samples == null) {
                Lg.Info("宠物音效 " + baseName + ".wav 解析失败（不是 16bit PCM 的 RIFF/WAVE）");
                return null;
            }
            int frames = samples.Length / channels;
            var s2 = new PcmSource { Samples = samples, Channels = channels, Rate = rate, Pos = 0 };
            var built = BuildClip(s2, baseName, out string how);
            if (built == null) return null;
            src = s2;
            Lg.Info($"[LevelLib] 宠物音效加载完成: {baseName}.wav（{how} {frames} 帧 "
                + $"{channels}ch {rate}Hz 时长 {s2.LengthSeconds:F2}s clip.samples={built.samples} "
                + $"clip.length={built.length:F2}s loadState={LoadStateOf(built)}）");
            return built;
        } catch (System.Exception e) {
            Lg.Ex("宠物音效解析 wav 失败（" + baseName + "）", e);
            return null;
        }
    }

    /// <summary>用我们自己持有的采样建一个 AudioClip（how 回传用了哪条路）。
    /// 首选内存态 clip（AudioClip.Create(..., false) + 反射 SetData 灌数据，samples/length/loadState 全正常）；
    /// 兜底回调式（游戏自己的 MP3 流就是这条，但它的 samples 恒为 0，别拿它当判据）。</summary>
    private static AudioClip BuildClip(PcmSource src, string name, out string how) {
        how = "";
        int frames = src.Samples.Length / src.Channels;
        if (_setDataArr != null) {
            try {
                var mem = AudioClip.Create(name, frames, src.Channels, src.Rate, false);
                if (mem != null) {
                    _setDataArr.Invoke(mem, new object[] { src.Samples, 0 });
                    how = "内存态 clip 反射 SetData";
                    return mem;
                }
            } catch (System.Exception e) {
                Lg.Ex("宠物音效 " + name + "：内存态 SetData 失败 → 改走回调式", e);
            }
        }
        try {
            var clip = AudioClip.Create(name, frames, src.Channels, src.Rate, false,
                data => {
                    // 音频线程回调：只读 Samples，越界就补静音（不然尾巴会爆音）
                    int pos = src.Pos;
                    for (int i = 0; i < data.Length; i++) {
                        int k = pos + i;
                        data[i] = (k >= 0 && k < src.Samples.Length) ? src.Samples[k] : 0f;
                    }
                    src.Pos = pos + data.Length;
                },
                pos2 => { src.Pos = pos2 * src.Channels; });      // 从头播 / Stop 之后会回调这里重置
            if (clip == null) { Lg.Info("宠物音效 " + name + "：AudioClip.Create 返回 null"); return null; }
            how = "回调式 clip（采样在托管内存）";
            return clip;
        } catch (System.Exception e) {
            Lg.Ex("宠物音效 " + name + "：回调式建 clip 也失败", e);
            return null;
        }
    }

    /// <summary>播之前验一次 clip 里的数据还在不在，不在就重建：内存态 clip 隔几次场景加载之后
    /// 同一个对象也会报 samples=0（Play / PlayOneShot 双双起不来），"用之前重建一次"是最稳的应对，
    /// 代价只是把解析后的 float[] 拷一次。判据用 clip.samples == 期望帧数；重建前把旧 clip 销毁掉。</summary>
    private static AudioClip EnsureClip(AudioClip clip, PcmSource src, string name) {
        if (src == null) return clip;
        int want = src.Samples.Length / src.Channels;
        if (clip != null) {
            int got = -1;
            try { got = clip.samples; } catch { }
            if (got == want) return clip;
            Lg.Info($"[LevelLib] 宠物音效 {name}：clip 里的数据没了（samples={got} 期望 {want}）"
                + "→ 播放前重建");
            try { UnityEngine.Object.Destroy(clip); } catch { }     // 旧的销毁掉（别每次点击留一个）
        }
        return BuildClip(src, name, out _);
    }

    /// <summary>纯字节解析：16bit PCM 的 RIFF/WAVE → 交错 float 采样（另回传声道数 / 采样率）。
    /// 不碰任何 Unity 类型。认不出来就返回 null，调用方退回 mp3（宁可没声音也别播噪音）。</summary>
    internal static float[] ParseWavPcm16(byte[] b, out int channels, out int rate) {
        channels = 0; rate = 0;
        if (b == null || b.Length < 44) return null;
        if (b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A') return null;
        int bits = 0, dataOff = -1, dataLen = 0;
        int p = 12;
        while (p + 8 <= b.Length) {
            int sz = b[p + 4] | (b[p + 5] << 8) | (b[p + 6] << 16) | (b[p + 7] << 24);
            int body = p + 8;
            if (sz < 0 || body + sz > b.Length) sz = b.Length - body;     // 容错（个别文件 chunk 长度不准）
            if (b[p] == 'f' && b[p + 1] == 'm' && b[p + 2] == 't' && b[p + 3] == ' ') {
                if (sz < 16) return null;
                int fmt = b[body] | (b[body + 1] << 8);
                if (fmt != 1) return null;                                 // 只认 PCM（别的一律不猜）
                channels = b[body + 2] | (b[body + 3] << 8);
                rate = b[body + 4] | (b[body + 5] << 8) | (b[body + 6] << 16) | (b[body + 7] << 24);
                bits = b[body + 14] | (b[body + 15] << 8);
            } else if (b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a') {
                dataOff = body;
                dataLen = System.Math.Min(sz, b.Length - body);
            }
            if (dataOff >= 0 && channels > 0) break;
            p = body + sz + (sz & 1);       // chunk 按偶数对齐
        }
        if (channels <= 0 || channels > 8 || rate <= 0 || rate > 384000 || bits != 16 || dataOff < 0 || dataLen < 4) return null;
        int frames = dataLen / (channels * 2);
        if (frames <= 0) return null;
        var samples = new float[frames * channels];
        for (int i = 0, o = dataOff; i < samples.Length; i++, o += 2)
            samples[i] = (short)(b[o] | (b[o + 1] << 8)) / 32768f;
        return samples;
    }

    /// <summary>兜底：wav 不在 / 解析不了时走老路（mp3 + UnityWebRequest）。这条路有已知缺陷 ——
    /// 数据挂在下载器上，Resources.UnloadUnusedAssets 之后会失效；正常发布包里两个 wav 都在，走不到这里。</summary>
    private IEnumerator LoadMp3Fallback() {
        yield return LoadMp3("Ya1", true);
        yield return LoadMp3("Ya2", false);
    }

    private IEnumerator LoadMp3(string baseName, bool isPress) {
        string path = System.IO.Path.Combine(Main.ModEntry.Path, "assets", "pet", baseName + ".mp3");
        if (!System.IO.File.Exists(path)) {
            // 音效也在资源包里：缺了交给 AssetPack 报一次（不带路径）
            AssetPack.NoteMissing("pet/" + baseName + ".mp3");
            yield break;
        }
        var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.MPEG);
        (uwr.downloadHandler as DownloadHandlerAudioClip).streamAudio = false;
        yield return uwr.SendWebRequest();
        if (uwr.result == UnityWebRequest.Result.Success && uwr.downloadHandler != null) {
            var clip = DownloadHandlerAudioClip.GetContent(uwr);
            if (isPress) { _pressClip = clip; }
            else { _releaseClip = clip; }
            Lg.Info("宠物音效加载完成（mp3 兜底）: " + System.IO.Path.GetFileName(path)
                + "（时长 " + (clip != null ? clip.length.ToString("F2") : "?") + "s）");
        } else {
            Lg.Info("宠物音效加载失败: " + uwr.error + "（" + System.IO.Path.GetFileName(path) + "）");
            try { uwr.Dispose(); } catch { }
        }
    }

    // ================= 播放 =================

    /// <summary>一个音源上播一声（两路音源各用一次）。Play() 之后如果 isPlaying 还是 false，再试一次
    /// PlayOneShot 并把结果打出来 —— "两个 API 都起不来"和"只是 Play() 这条路不行"就能当场分开。</summary>
    private static void PlayOn(AudioSource s, AudioClip clip, float vol, PcmSource src, string tag) {
        if (src != null) src.Pos = 0;      // 自己把读指针归零（别指望 Unity 一定回调重置）
        s.Stop();
        s.clip = clip;
        s.Play();
        if (!s.isPlaying) {
            s.PlayOneShot(clip, Mathf.Clamp01(vol));
            Lg.Info($"[LevelLib] 宠物音效·{tag}：`Play()` 没起来 → 改用 `PlayOneShot` 试一次"
                + $"：播放中={s.isPlaying} clip.samples={clip.samples} loadState={LoadStateOf(clip)}");
        }
    }

    public void PlayPress(float vol) {
        try {
            if (_press == null) { Lg.Info("宠物按下音效：AudioSource 不存在"); return; }
            if (_pressClip == null) { Lg.Info("宠物按下音效：clip 未就绪（解析失败 / 还在异步加载）"); return; }
            EnsureAudible(_press);
            _press.volume = Mathf.Clamp01(vol);            // 走 Play() 时 volume 是唯一的音量来源
            _release.Stop();                               // 再按一下先把上一轮的 Ya2 掐掉（参考实现 playPress）
            _pressClip = EnsureClip(_pressClip, _pressSrc, "Ya1");   // 数据没了就当场重建
            PlayOn(_press, _pressClip, vol, _pressSrc, "按下");
            StartProbe();                                  // 播完立刻起出声探针（见 TickProbe）
            _pressStartAt = Time.unscaledTime;             // 记下起点，下面算"还剩多少"
            Lg.Info($"[LevelLib] 宠物音效·按下：vol={_press.volume:F2} 播放中={_press.isPlaying}"
                + $" 我们的样本={(_pressSrc != null ? _pressSrc.Samples.Length : 0)} clip.samples={_pressClip.samples}"
                + $" loadState={LoadStateOf(_pressClip)} | " + AudioEnvLine());
        } catch (System.Exception e) { Lg.Ex("宠物按下音效异常", e); }
    }

    public void PlayRelease(float vol) {
        try {
            if (_release == null) { Lg.Info("宠物松开音效：AudioSource 不存在"); return; }
            if (_releaseClip == null) { Lg.Info("宠物松开音效：clip 未就绪（解析失败 / 还在异步加载）"); return; }
            EnsureAudible(_release);
            _release.volume = Mathf.Clamp01(vol);
            _releaseClip = EnsureClip(_releaseClip, _releaseSrc, "Ya2");   // 同上，数据没了就重建
            PlayOn(_release, _releaseClip, vol, _releaseSrc, "松开");
            Lg.Info($"[LevelLib] 宠物音效·松开：vol={_release.volume:F2} 播放中={_release.isPlaying}"
                + $" 我们的样本={(_releaseSrc != null ? _releaseSrc.Samples.Length : 0)} clip.samples={_releaseClip.samples}"
                + $" loadState={LoadStateOf(_releaseClip)} | " + AudioEnvLine());
        } catch (System.Exception e) { Lg.Ex("宠物松开音效异常", e); }
    }

    /// <summary>播放前把这些条件再按一遍（启动时设过 ≠ 现在还对）：组件启用、没被静音、忽略暂停开着。
    /// ignoreListenerPause 是挂在 AudioSource 上的属性，而 one-shot 走另一条声部、对它没有明确保证，
    /// 所以按下/松开都走 Play()；两路音源正好够重叠。</summary>
    private static void EnsureAudible(AudioSource s) {
        try {
            s.enabled = true;
            s.mute = false;
            s.ignoreListenerPause = true;
        } catch { }
    }

    /// <summary>clip.loadState 的安全读（回调式的 clip 读它可能抛），日志用。</summary>
    private static string LoadStateOf(AudioClip c) {
        try { return c != null ? c.loadState.ToString() : "null"; } catch { return "读不出"; }
    }

    // ================= 出声探针 =================
    // clip.samples 与 AudioSource.isPlaying 都不可信（同一个 clip 前后自相矛盾 / Play() 之后立刻读恒为 False）
    // 所以直接量声音：AudioListener.GetOutputData() 读送到声卡的那份数据，播下去之后每 0.1 秒记一次
    // （是否在播 / 播放位置有没有在推进 / clip.samples / 输出峰值），三种结论当场分开：通路没问题（那是音量
    // 或设备）、播了没送到输出、音源或 clip 根本没起来。游戏音乐正在响时峰值本来就不是 0，按下前先记基线。
    private int _probeLeft;
    private float _probeNextAt = -1f;
    private float[] _outBuf;
    private static MethodInfo _getOutputArr;
    private static bool _getOutputProbed;

    /// <summary>AudioListener.GetOutputData(float[], int) 也用反射拿（跟 SetData 一个道理：
    /// Unity 6 里同样有 Span 重载，直接写就编译不过）。</summary>
    private static void ProbeGetOutput() {
        if (_getOutputProbed) return;
        _getOutputProbed = true;
        try {
            foreach (var m in typeof(AudioListener).GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                if (m.Name != "GetOutputData") continue;
                try {
                    var ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(float[]) && ps[1].ParameterType == typeof(int))
                        _getOutputArr = m;
                } catch { }
            }
            Lg.Info("输出探针接口：AudioListener.GetOutputData(float[],int) "
                + (_getOutputArr != null ? "拿到（能量到「送到声卡的数据」）" : "没拿到（探针只能看属性）"));
        } catch (System.Exception e) { Lg.Ex("探测 GetOutputData 失败", e); }
    }

    private void StartProbe() {
        ProbeGetOutput();
        _probeLeft = 6;
        _probeNextAt = Time.unscaledTime + 0.05f;
        Lg.Info("音效探针：基线（按下之前）监听器输出峰值="
            + OutputPeak().ToString("F4") + " 接下来 0.6 秒每 0.1 秒记一次");
    }

    /// <summary>监听器输出峰值 0~1；拿不到就返回 -1（"没法量"，别跟"真的静音"混了）。</summary>
    private float OutputPeak() {
        try {
            if (_getOutputArr == null) return -1f;
            if (_outBuf == null) _outBuf = new float[512];
            if (!(bool)_getOutputArr.Invoke(null, new object[] { _outBuf, 0 })) return -1f;
            float p = 0f;
            for (int i = 0; i < _outBuf.Length; i++) { float a = _outBuf[i] < 0f ? -_outBuf[i] : _outBuf[i]; if (a > p) p = a; }
            return p;
        } catch { return -1f; }
    }

    public void TickProbe() {
        if (_probeLeft <= 0) return;
        float now = Time.unscaledTime;
        if (now < _probeNextAt) return;
        _probeNextAt = now + 0.1f;
        _probeLeft--;
        float pressTime = -1f, relTime = -1f;
        try { pressTime = _press != null && _press.clip != null ? _press.time : -1f; } catch { }
        try { relTime = _release != null && _release.clip != null ? _release.time : -1f; } catch { }
        Lg.Info($"[LevelLib] 音效探针 +{(now - _pressStartAt):F2}s："
            + $"按下 在播={(_press != null && _press.isPlaying)} 位置={pressTime:F3}s 样本={(_pressClip != null ? _pressClip.samples : -1)}"
            + $" | 松开 在播={(_release != null && _release.isPlaying)} 位置={relTime:F3}s"
            + $" | 监听器输出峰值={OutputPeak():F4}");
    }

    /// <summary>Ya1 还剩多少秒（用来算"提前 100ms 接 Ya2"；拿不到就返回 0 = 立刻播）。
    /// 用我们自己的样本数算，不读 clip.length —— 回调式 clip 的 length/samples 恒为 0，拿它算就永远"已经播完"。</summary>
    public float PressRemaining() {
        try {
            if (_pressSrc == null || _pressStartAt < 0f) return 0f;
            float elapsed = Time.unscaledTime - _pressStartAt;
            float remain = _pressSrc.LengthSeconds - elapsed;
            return remain > 0f ? remain : 0f;
        } catch { return 0f; }
    }

    /// <summary>设置里改音量 → 立刻生效。只改 AudioSource.volume（Play() 走的就是它），不要再乘一次 ——
    /// 两边都设就变成相乘、音量滑块变平方（0.5 变 0.25）。游戏自己还会把全局音量压成
    /// AudioListener.volume = 0.5*(全局音量/10)^2（满格也只有 0.5），所以响度的补偿做在素材文件上。</summary>
    public void ApplyVolume(float vol) {
        try {
            if (_press != null) { EnsureAudible(_press); _press.volume = Mathf.Clamp01(vol); }
            if (_release != null) { EnsureAudible(_release); _release.volume = Mathf.Clamp01(vol); }
        } catch { }
    }
}
