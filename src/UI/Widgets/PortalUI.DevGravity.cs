// 开发者页"重力字母"：点一下掉一个字母/数字，落进高度图里堆起来（复刻 rare-ui 的 gravity-letters）。
// 与源码逐条对齐的数字与规则都写在下面那组常量里，注释里标了源码里的原名。
// 边界：手写 uGUI + 内置管线，零 shader / 零 Material / 零 DOTween / 不用 TMP —— 只改
// RectTransform.anchoredPosition / localScale / localEulerAngles 与 Text.color，物理自己每帧积分。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {

    // ================= 源码常数（一个都不许改，改了就"手感不一样"） =================
    private const float DevGravGravity = 800f;        // gravity：px/s²，每帧 vy += g*dt
    private const int DevGravSize = 28;               // size：字号基数（实际每颗 ×0.8~×1.2）
    private const int DevGravCol = 8;                 // COL：高度图的一格多宽（px）
    private const float DevGravClearance = 24f;       // CLEARANCE：出生点离落点最少留这么高
    private const float DevGravSlope = 0.35f;         // SLOPE：邻格低出自身高度这个比例才滑
    private const float DevGravTilt = 26f;            // TILT：静止倾斜上限（度）
    private const float DevGravBounce = 0.22f;        // BOUNCE：首次触地的恢复系数
    private const float DevGravEager = 0.45f;         // EAGER：倾斜（wind）时的滑落阈值系数
    private const int DevGravTiltOn = 10;             // TILT_ON：设备倾斜多少度触发雪崩（±）
    private const float DevGravFreeSlideKt = 6f;      // 源码回落判据 rebound*rebound > gravity*6
    private const int DevGravTiltMinMs = 250;         // 源码 SHAKE_MS=350 的鼠标近似（鼠标比陀螺仪抖）
    private const float DevGravFontMin = 0.8f;        // fontSize = round(size * rand(0.8, 1.2))
    private const float DevGravFontMax = 1.2f;
    private const float DevGravSpinMax = 4f;          // spin = ±(4 + 10*squareness) 度
    private const float DevGravSpinRate = 50f;        // vr = ±(50 + 130*squareness) 度/秒
    private const float DevGravSpinRateSquare = 130f;
    private const float DevGravSquareCap = 1f;        // squareness = min(1, eh/ew)
    private const float DevGravJitterBase = 2f;       // jitter = ±(2 + 8*squareness) 度
    private const float DevGravJitterSquare = 8f;
    private const float DevGravSwayMax = 12f;         // sway = ±min(12, 落差*0.05)
    private const float DevGravSwayK = 0.05f;
    private const float DevGravOverlapGap = 8f;       // 出生避让：压在还在掉的字头上就再上移 8
    private const float DevGravSlideMinDrop = 2f;     // 重排时"确实往下掉了"的判据（px）
    private const float DevGravRestEps = 1f;          // 重排时判定"悬空"的 1px 容差
    private const float DevGravSweepEps = 1f;         // 源码 restY 里的 h-1：防除零，也当落地容差用
    private const int DevGravSlideIters = 64;         // findRestX 最多扫这么多格
    private const float DevGravSlideStepK = 1f / 3f;  // step = max(COL, round(w/3))
    private const float DevGravSpawnPitch = 0.2f;     // 源码 spawn keyframes 的 scale(0.5) 起点
    private const float DevGravSpawnAlpha = 0.3f;     // 源码 spawn keyframes 的 opacity 起点
    private const float DevGravSquashFrom = 0.82f;    // 源码 landing squash 的 scaleY(0.82)
    private const int DevGravSquashMs = 160;          // squash 动画时长

    // 源码的两段 WAAPI 缓动都是 cubic-bezier(0.215, 0.61, 0.355, 1)；
    // y 分量 x 恰好等于 t（0.61/0.355 是 0.215 的整数倍），解出来就是"匀速"，所以这里按线性做，不另编缓动。
    private const float DevGravSpawnMs = 150f;        // 源码 spawn 动画时长
    private const float DevGravSquashSec = DevGravSquashMs * 0.001f;

    // ================= 运行参数（源码的 props 默认值 + 我们自己的安全阀） =================
    private const int DevGravPoolSize = 120;          // 对象池：预建这么多 Text，循环复用，绝不每次点击 new GameObject
    private const int DevGravMaxGlyphs = 60;          // 源码 maxGlyphs 默认 Infinity；这里钉 60（最老的先淡出腾位，见 DevGravRecycleOne）
    private const int DevGravPourMs = 120;            // POUR_MS：按住不放的泼洒节奏（源码 HOLD_MS=300 起泼）
    private const int DevGravHoldMs = 300;
    private const float DevGravLeaveSec = 0.35f;      // LEAVE_MS=350：超上限那颗用 350ms 淡到 0 再回收
    private const float DevGravFadeStep = 0.10f;      // 单帧步进上限：大卡顿帧不许把淡出一次冲完
    private const int DevGravMaxSubSteps = 4;         // 单帧最多按"正常帧"积分几步，多了就丢帧，字符绝不瞬移

    /// <summary>重力字母的字符集（源码 POOLS.both；type 默认 "letters" 是 A~Z，这里按"字母数字"取并集）</summary>
    private const string DevGravChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>字色：跟制作名单同一支浅灰，只是更淡一点（字体资源零新增，用现成的 _uiFont）</summary>
    private static readonly Color DevGravTextColor = new Color(0.70f, 0.71f, 0.76f, 1f);

    // ---- 单格状态：池里的每一颗都有自己的魂，回收只改 DevGravShown ----
    private enum DevGravSt : byte { Idle = 0, Alive = 1, Leaving = 2 }

    private static Canvas _devGravCanvas;             // 独立子画布（不设 overrideSorting）
    private static RectTransform _devGravRect;        // 铺满开发者页；也是"容器坐标系"的原点（左上角）
    private static GameObject _devGravRoot;
    private static Text[] _devGravText;
    private static RectTransform[] _devGravTr;
    private static float[] _devGravEw, _devGravEh, _devGravW, _devGravH, _devGravDx, _devGravDy;
    private static float[] _devGravX, _devGravY, _devGravX0, _devGravY0;
    private static float[] _devGravTx, _devGravTy, _devGravVy, _devGravSpin, _devGravRot, _devGravVr;
    private static float[] _devGravRestRot, _devGravSway;
    private static float[] _devGravSquare, _devGravSquashK, _devGravLeaveA;
    private static float[] _devGravRevealK;
    private static bool[] _devGravBounced, _devGravDone, _devGravStill, _devGravShown;
    private static byte[] _devGravStArr;
    private static int _devGravPoolMade;              // 已经建出来的 Text 数（懒建，最多 DevGravPoolSize）
    private static int _devGravAlive;                 // 当前活着（Alive + Leaving）几颗
    private static int _devGravCursor;                // 环形游标：下一个用哪一格
    private static int _devGravPhysicsOn;             // 这一帧有谁还需要物理积分

    // ---- 高度图（源码 heightsRef）：每 DevGravCol 像素一格，记"这一格已堆到多高" ----
    private static float[] _devGravHeights = new float[0];
    private static int _devGravHmCols;

    // ---- 重排用的暂存（容量钉在池大小上，绝不扩容、绝不每帧 new List/数组） ----
    private static int[] _devGravOrder = new int[DevGravPoolSize];
    private static int[] _devGravAliveIdx = new int[DevGravPoolSize];

    // ---- 输入侧（源码是 pointerdown；这里是"开发者页这一帧的键鼠"） ----
    private static bool _devGravHidden = true;        // 上一次 Tick 结束时是不是收手状态（用来只打一次日志）
    private static bool _devGravBtnPrev;
    private static bool _devGravCamDown;              // 相机拖动中：一次都不生成
    private static int _devGravPourHold;              // 已经按住多少 ms（-1 = 没按）
    private static int _devGravPourNext;              // 泼洒倒计时（ms）
    private static int _devGravTiltMinMs;             // 距上次倾斜检测多少 ms
    private static int _wind;                        // windRef：-1/0/1
    private static float _devGravTiltAng;             // 倾斜角（度）：A/D 说 ±TILT_ON，有重力感应就用真角度
    private static bool _devGravKeyTilt;              // 这一帧的角度是键盘给的（松手才回中，别跟重力感应打架）

    /// <summary>开发者页的重力字母：切到这一页时预建容器与独立子画布（幂等）</summary>
    private static void BuildDevGravityLetters() {
        if (_devGravRoot != null) return;
        if (_devPage == null) return;
        // 独立子画布：每帧改子物体位置会让 uGUI 标脏重批，放进子画布只影响这一小块，
        // 父画布不每帧重建（跟 PanelGlass 的 GlassCrops、代码背景 NetBg 同一套做法）。
        // 绝不设 overrideSorting：默认按层级顺序画在原位，一设就跳到整个面板之上。
        var root = new GameObject("DevGravity", typeof(RectTransform), typeof(Canvas));
        _devGravCanvas = root.GetComponent<Canvas>();
        root.transform.SetParent(_devPage.transform, false);
        _devGravRoot = root;
        _devGravRect = root.GetComponent<RectTransform>();
        Stretch(_devGravRect, Vector2.zero, Vector2.one);
        _devGravHidden = true;

        _devGravText = new Text[DevGravPoolSize];
        _devGravTr = new RectTransform[DevGravPoolSize];
        _devGravEw = new float[DevGravPoolSize];
        _devGravEh = new float[DevGravPoolSize];
        _devGravW = new float[DevGravPoolSize];
        _devGravH = new float[DevGravPoolSize];
        _devGravDx = new float[DevGravPoolSize];
        _devGravDy = new float[DevGravPoolSize];
        _devGravX = new float[DevGravPoolSize];
        _devGravY = new float[DevGravPoolSize];
        _devGravX0 = new float[DevGravPoolSize];
        _devGravY0 = new float[DevGravPoolSize];
        _devGravTx = new float[DevGravPoolSize];
        _devGravTy = new float[DevGravPoolSize];
        _devGravVy = new float[DevGravPoolSize];
        _devGravSpin = new float[DevGravPoolSize];
        _devGravRot = new float[DevGravPoolSize];
        _devGravVr = new float[DevGravPoolSize];
        _devGravRestRot = new float[DevGravPoolSize];
        _devGravSway = new float[DevGravPoolSize];
        _devGravSquare = new float[DevGravPoolSize];
        _devGravSquashK = new float[DevGravPoolSize];
        _devGravLeaveA = new float[DevGravPoolSize];
        _devGravRevealK = new float[DevGravPoolSize];
        _devGravBounced = new bool[DevGravPoolSize];
        _devGravDone = new bool[DevGravPoolSize];
        _devGravStill = new bool[DevGravPoolSize];
        _devGravShown = new bool[DevGravPoolSize];
        _devGravStArr = new byte[DevGravPoolSize];
        Lg.Info("开发者页重力字母：容器 + 独立子画布已建（池 " + DevGravPoolSize
            + " 颗，上限 " + DevGravMaxGlyphs + " 颗同时在场）");
    }

    /// <summary>开发者页整页拆掉时把引用清空（槽位重建后 BuildDeveloperPage 会重新建）</summary>
    private static void ClearDevGravityRefs() {
        _devGravRoot = null;
        _devGravCanvas = null;
        _devGravRect = null;
        _devGravText = null;
        _devGravTr = null;
        _devGravCreditRects.Clear();
        _devGravPoolMade = 0;
        _devGravAlive = 0;
        _devGravCursor = 0;
        _devGravPhysicsOn = 0;
        _devGravHmCols = 0;
        _devGravPourHold = -1;
        _devGravPourNext = 0;
        _devGravBtnPrev = false;
        _devGravTiltMinMs = 0;
        _wind = 0;
        _devGravTiltAng = 0f;
        _devGravKeyTilt = false;
        _devGravHidden = true;
    }

    /// <summary>这套效果"此刻该不该干活"：不在开发者页 / 面板关了 / 页面被拆 / 遮罩还没淡进来 → 一律收手。
    /// 判据与开发者页遮罩 TickPageVeil 同源（tab 4 + 页面在树上 + 遮罩已淡入），
    /// 所以切走或关面板的下一帧就真的不跑物理、不画任何东西。</summary>
    private static bool DevGravityVisible() {
        if (!_panelOpen || _panelCanvas == null || _topTab != 4) return false;
        if (_devGravRoot == null || _devGravRect == null) return false;
        if (!_devGravRoot.activeInHierarchy) return false;
        return _devVeilCur > 0.01f;
    }

    /// <summary>重力字母每帧驱动（由 TickPanel 调；不在开发者页时内部直接收手）</summary>
    private static void TickDevGravity() {
        if (!DevGravityVisible()) { DevGravityRest(); return; }
        if (_devGravHidden) {
            _devGravHidden = false;
            Lg.Info("开发者页重力字母：开始接管（点一下掉一颗，再按住的会连着掉）");
        }
        int dtMs = (int)(Time.unscaledDeltaTime * 1000f);
        if (dtMs < 0) dtMs = 0;
        if (dtMs > 100) dtMs = 100;      // 卡顿帧最多按 100ms 记（其余交给单帧步进上限去丢）
        // 容器尺寸变了（分辨率/窗口变化）→ 高度图列数跟着变，必须重排一次，否则落点全按旧宽度算
        if (_devGravHmCols != DevGravityHmNeed(_devGravRect.rect.width)) DevGravityRebuild(false);
        // 物理与出生动画一律走不受 timeScale 影响的那一份时间：这一页常常是在游戏暂停时看的
        // （timeScale = 0 时 Time.deltaTime 是 0），用它的话字会永远停在出生那一帧 —— 20% 大小、
        // 30% 透明度，肉眼等于没生成。上面那个 dtMs 本来就是 unscaled 的，跟它同源。
        float dtSec = dtMs * 0.001f;
        DevGravityInput(dtMs);
        DevGravityStep(dtSec);
        DevGravityTickTweens(dtSec);   // 出生缩放/落地压扁：跟物理解耦，躺平后也要把这一下播完
        if (_devGravProbeSlot >= 0) {
            _devGravProbeTimer -= dtSec;
            if (_devGravProbeTimer <= 0f) { DevGravityLogLate(_devGravProbeSlot); _devGravProbeSlot = -1; }
        }
    }

    /// <summary>离开开发者页 / 面板关掉：池里的字全部收走、高度图作废（列数置 0，下次进来必重排）、按键状态复位。
    /// "完全停手"这句就落在这里 —— 之后没有任何物理积分，也没有一次属性写入。</summary>
    private static void DevGravityRest() {
        if (_devGravHidden) return;
        _devGravHidden = true;
        _devGravPourHold = -1;
        _devGravPourNext = 0;
        _devGravBtnPrev = false;
        _wind = 0;
        _devGravTiltAng = 0f;
        int n = _devGravPoolMade;
        for (int i = 0; i < n; i++) {
            _devGravStArr[i] = (byte)DevGravSt.Idle;
            _devGravShown[i] = false;
            if (_devGravRoot != null) _devGravText[i].gameObject.SetActive(false);
        }
        _devGravAlive = 0;
        _devGravPhysicsOn = 0;
        _devGravHmCols = 0;
        Lg.Info("开发者页重力字母：离开开发者页 ->  全部收手（不跑物理、不画东西）");
    }

    // ================= 输入 =================

    /// <summary>这一帧的键鼠。命中"不该抢"的东西（制作名单彩蛋点击区 / 指令输入框 / 任何 uGUI 可交互元素）时
    /// 那一下不生成字符；右键、中键、按住相机拖动（右键或中键）也都不生成。</summary>
    private static void DevGravityInput(int dtMs) {
        bool camDown = UiInput.MouseButton(1) || UiInput.MouseButton(2);
        if (camDown) {
            _devGravCamDown = true;
            _devGravPourHold = -1;       // 拖动中把泼洒也停掉，不然镜头一路都在下雨
            _devGravBtnPrev = false;
                return;
        }
        if (_devGravCamDown) { _devGravCamDown = false; _devGravBtnPrev = UiInput.MouseButton(0); }

        // 倾斜（源码 windRef）：A/D 或 ←/→ 按住一次给一个 ±TILT_ON 的角度；桌面端读不到陀螺仪，
        // Input.acceleration 有数就用真角度（跟源码 deviceorientation 的 gamma 同义）。
        // 判据跟源码一致：|倾斜角| > TILT_ON(10°) 才有方向，方向变了就重排一次（限流到 TILT_MIN_MS）。
        float acc = Input.acceleration.x;
        if (acc > 0.05f || acc < -0.05f) _devGravTiltAng = acc * 90f;
        if (UiInput.Key(KeyCode.LeftArrow) || UiInput.Key(KeyCode.A)) { _devGravTiltAng = -DevGravTiltOn; _devGravKeyTilt = true; }
        else if (UiInput.Key(KeyCode.RightArrow) || UiInput.Key(KeyCode.D)) { _devGravTiltAng = DevGravTiltOn; _devGravKeyTilt = true; }
        else if (_devGravKeyTilt) { _devGravTiltAng = 0f; _devGravKeyTilt = false; }
        int prevWind = _wind;
        _wind = _devGravTiltAng > DevGravTiltOn ? 1 : (_devGravTiltAng < -DevGravTiltOn ? -1 : 0);
        if (_wind != 0 && _wind != prevWind && _devGravTiltMinMs <= 0) {
            _devGravTiltMinMs = DevGravTiltMinMs;      // 跟源码 SHAKE_MS 同一个意思：别每帧重排
            DevGravityRebuild(true);
        }
        if (_devGravTiltMinMs > 0) _devGravTiltMinMs -= dtMs;

        bool down = UiInput.MouseButton(0);
        int downs = 0;
        if (down && !_devGravBtnPrev) downs = 1;
        if (!down) {
            _devGravPourHold = -1;
            _devGravPourNext = 0;
        } else if (downs == 0) {
            if (_devGravPourHold < 0) _devGravPourHold = 0;
            else _devGravPourHold += dtMs;
        }
        _devGravBtnPrev = down;
        if (downs == 0) return;

        Vector2 p;
        if (!DevGravityContainerPoint(UiInput.MousePosition, out p)) {
            DevGravityLogClick(-1f, -1f, 1);
            return;
        }
        // 遮挡判据吃的是屏幕坐标（它内部用的就是 RectangleContainsScreenPoint），
        // 别把上面那个"页内像素坐标"喂进去：两套坐标混用会让判据落在屏幕上另一个点。
        if (DevGravityBlocked(UiInput.MousePosition)) {
            DevGravityLogClick(p.x, p.y, 2);
            return;
        }
        DevGravityLogClick(p.x, p.y, 0);
        DevGravityDrop(p.x, p.y);
    }

    /// <summary>点完之后过 0.5 秒再看一眼那颗字（"生成出来了但看不见"要的是这一刻的状态，
    /// 而不是生成那一帧的）。顺手报一遍容器在父物体里的位置 —— 被压暗罩/别的层盖住也是这类症状。</summary>
    private static int _devGravProbeSlot = -1;
    private static float _devGravProbeTimer;

    private static void DevGravityLogLate(int i) {
        if (i < 0 || i >= _devGravPoolMade) return;
        var t = _devGravText[i];
        var rt = _devGravTr[i];
        if (t == null || rt == null) { Lg.Info("重力字母：半秒后那颗字已经不在了"); return; }
        string order = "";
        if (_devGravRoot != null) {
            var p = _devGravRoot.transform.parent;
            if (p != null) {
                for (int k = 0; k < p.childCount && k < 5; k++) order += (k == 0 ? "" : " < ") + p.GetChild(k).name;
                order += "（本层第 " + _devGravRoot.transform.GetSiblingIndex() + " 个）";
            }
        }
        Lg.Info("重力字母：半秒后 激活=" + t.gameObject.activeSelf
            + " 状态=" + _devGravStArr[i]
            + " 缩放=" + rt.localScale.x.ToString("F2")
            + " alpha=" + t.color.a.ToString("F2")
            + " 世界=" + rt.position.x.ToString("F0") + "," + rt.position.y.ToString("F0")
            + " 画布开=" + (_devGravCanvas != null && _devGravCanvas.enabled)
            + " 覆盖排序=" + (_devGravCanvas != null && _devGravCanvas.overrideSorting)
            + " ｜ 开发者页子物体顺序：" + order);
    }

    /// <summary>点击这条路的点名行（最多几条）：没掉字时一眼看出卡在哪一步
    /// （1 = 点不在容器里 / 2 = 被遮挡判据挡下 / 0 = 真的放了）。
    /// "已生成"那一条会把刚建出来的那颗的状态一起报出来（激活 / 字体 / 透明度 / 位置 / 尺寸 / 父链），
    /// 这样"生成了但看不见"和"根本没生成"一眼就能分开。</summary>
    private static int _devGravClickLog;
    private static void DevGravityLogClick(float x, float y, int why) {
        if (_devGravClickLog >= 6) return;
        _devGravClickLog++;
        string s = why == 0 ? "已生成" : (why == 1 ? "点不在容器里" : "被遮挡判据挡下");
        string extra = "";
        if (why == 0) {
            for (int i = 0; i < _devGravPoolMade; i++) {
                if (_devGravStArr[i] != (byte)DevGravSt.Alive) continue;
                var t = _devGravText[i];
                var rt = _devGravTr[i];
                if (t == null) { extra = " ｜ 但池里那颗 Text 是空的"; break; }
                extra = " ｜ 那颗：激活=" + t.gameObject.activeSelf
                    + " 父链活=" + t.gameObject.activeInHierarchy
                    + " 字体=" + (t.font != null ? t.font.name : "无")
                    + " 字号=" + t.fontSize
                    + " alpha=" + t.color.a.ToString("F2")
                    + " 位置=" + (rt != null ? rt.anchoredPosition.x.ToString("F0") + "," + rt.anchoredPosition.y.ToString("F0") : "?")
                    + " 世界=" + (rt != null ? rt.position.x.ToString("F0") + "," + rt.position.y.ToString("F0") : "?")
                    + " 尺寸=" + (rt != null ? rt.sizeDelta.x.ToString("F0") + "x" + rt.sizeDelta.y.ToString("F0") : "?")
                    + " 容器rect=" + (_devGravRect != null ? _devGravRect.rect.width.ToString("F0") + "x" + _devGravRect.rect.height.ToString("F0") : "?")
                    + " 容器=" + (_devGravRoot != null ? (_devGravRoot.activeInHierarchy ? "活" : "不活") : "无")
                    + " 画布=" + (_devGravCanvas != null ? (_devGravCanvas.enabled ? "开" : "关") : "无");
                break;
            }
        }
        Lg.Info("重力字母：收到一次点击（页内 "
            + x.ToString("F0") + "," + y.ToString("F0") + "）→ " + s
            + " ｜ 时间：timeScale=" + Time.timeScale.ToString("F2")
            + " deltaTime=" + Time.deltaTime.ToString("F3")
            + " unscaled=" + Time.unscaledDeltaTime.ToString("F3") + extra);
    }

    /// <summary>把屏幕坐标换成"开发者页左上角为原点"的像素坐标（跟源码 clientX-rect.left 同一套）。
    /// 左上角矩形用 worldCorners 反推，就自动吃掉了画布的 scaleFactor，不用去猜缩放。
    /// 那个角点数组是缓存字段（GetWorldCorners 要传数组进去，不缓存就每次按一个新数组）。</summary>
    private static readonly Vector3[] _devGravCorners = new Vector3[4];

    private static bool DevGravityContainerPoint(Vector3 mouse, out Vector2 outP) {
        outP = Vector2.zero;
        if (_devGravRect == null) return false;
        _devGravRect.GetWorldCorners(_devGravCorners);   // [0]=左下 [1]=左上 [2]=右上 [3]=右下
        Vector2 tl = RectTransformUtility.WorldToScreenPoint(null, _devGravCorners[1]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(null, _devGravCorners[2]);
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(null, _devGravCorners[0]);
        float w = tr.x - tl.x;
        float h = tl.y - bl.y;
        if (w <= 0.5f || h <= 0.5f) return false;
        float nx = (mouse.x - tl.x) / w;
        float ny = (tl.y - mouse.y) / h;
        if (nx < 0f || nx > 1f || ny < 0f || ny > 1f) return false;
        outP = new Vector2(nx * _devGravRect.rect.width, ny * _devGravRect.rect.height);
        return true;
    }

    /// <summary>这一下该不该被"抢走"：① 制作名单那五行（含彩蛋点击区）；② 指令输入框；③ 界面上任何 uGUI 可交互元素。
    /// ③ 用 EventSystem.RaycastAll 自己投一次（PointerInputModule 的指针数据是 protected，拿不到），
    /// 从最上面那层往下找，命中第一个 Selectable 就算"这一下是人家的"。
    /// 入参是屏幕坐标（下面两条判据走的都是 RectangleContainsScreenPoint）。
    /// 只在鼠标真正按下的那一帧调用（不是每帧），所以这里那一次 List 分配是可以接受的；
    /// 绝不 SetActive、绝不 SetSelectedGameObject、绝不改任何东西。</summary>
    private static readonly List<RaycastResult> _devGravHits = new List<RaycastResult>();

    private static bool DevGravityBlocked(Vector2 screen) {
        if (DevGravityInCreditRows(screen)) return true;    // 制作名单那几行文字（量出来的字框，不是排版框）
        if (DevGravityInInputBox(screen)) return true;      // 指令输入框（bugbug / administrator developer 那套的主人）
        var es = EventSystem.current;
        if (es == null || !es.IsPointerOverGameObject()) return false;
        var ped = new PointerEventData(es);
        ped.position = screen;
        _devGravHits.Clear();
        es.RaycastAll(ped, _devGravHits);
        for (int i = 0; i < _devGravHits.Count; i++) {
            var go = _devGravHits[i].gameObject;
            if (go == null) continue;
            if (go.GetComponent<Selectable>() != null) return true;
        }
        return false;
    }

    // ---- 制作名单那几行文字的字框（BuildDeveloperPage 建的时候登记进来，遮挡判据只认它们 + 输入框） ----
    private static readonly List<RectTransform> _devGravCreditRects = new List<RectTransform>();

    /// <summary>登记文字框时横竖各留的余量（像素）：留一点让"点在字的边缘"也算点在字上。</summary>
    private const float DevGravCreditPadX = 8f, DevGravCreditPadY = 6f;

    /// <summary>指令输入框（设置页「扩展 → 开发者」那一行）的矩形：点在框里不掉字 ——
    /// 那一下必须原样落进输入框（bugbug / administrator developer 那套照旧）。
    /// 只认这个框、不认整行：有意保留"点在那一行左侧空白处照旧掉字"。</summary>
    private static RectTransform _devGravInputRect;

    /// <summary>登记指令输入框（BuildExtraPages 建完输入框后调用）</summary>
    private static void DevGravityRegisterInput(InputField input) {
        if (input == null) return;
        _devGravInputRect = input.GetComponent<RectTransform>();
    }

    /// <summary>点是不是落在指令输入框上（入参是屏幕坐标）</summary>
    private static bool DevGravityInInputBox(Vector2 screen) {
        if (_devGravInputRect == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(_devGravInputRect, screen, null);
    }

    /// <summary>把制作名单某一行登记成"别在这儿掉字"的矩形（BuildDeveloperPage 调用；幂等）。
    /// 登记进来的必须是那行字自己的框（建的时候按 preferredWidth/Height 量出来再缩），
    /// 不是那个用于排版的宽框 —— 排版框比字大得多，按它判会让整页都点不出字。</summary>
    private static void DevGravityRegisterCredit(RectTransform rt) {
        if (rt == null) return;
        for (int i = 0; i < _devGravCreditRects.Count; i++) if (_devGravCreditRects[i] == rt) return;
        _devGravCreditRects.Add(rt);
    }

    /// <summary>点是不是落在某一行文字上（入参是屏幕坐标；RectTransformUtility 处理旋转与缩放，别自己算）</summary>
    private static bool DevGravityInCreditRows(Vector2 screen) {
        for (int i = 0; i < _devGravCreditRects.Count; i++) {
            var rt = _devGravCreditRects[i];
            if (rt == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null)) return true;
        }
        return false;
    }

    // ================= 生成 =================

    /// <summary>点哪里掉哪里（源码 dropAt）：字号 ×0.8~1.2 随机、字符从池里随机取一个</summary>
    private static void DevGravityDrop(float cx, float cy) {
        float fs = Mathf.Round(DevGravSize * DevGravityRand(DevGravFontMin, DevGravFontMax));
        if (fs < 1f) fs = 1f;
        DevGravitySpawn((int)fs, cx, cy);
    }

    /// <summary>按住不放就按 POUR_MS 的节奏连掉，横向 ±8 的抖动（源码 startPour）</summary>
    private static void DevGravityPour(int dtMs) {
        float cx, cy;
        if (!DevGravityLocalMouse(out cx, out cy)) return;
        _devGravPourNext -= dtMs;
        while (_devGravPourNext <= 0) {
            _devGravPourNext += DevGravPourMs;
            DevGravityDrop(cx + DevGravityRand(-8f, 8f), cy);
        }
    }

    /// <summary>鼠标在容器坐标里的位置（不做遮挡判据；泼洒用它更新落点）</summary>
    private static bool DevGravityLocalMouse(out float cx, out float cy) {
        cx = 0f; cy = 0f;
        Vector2 p;
        if (!DevGravityContainerPoint(UiInput.MousePosition, out p)) return false;
        cx = p.x; cy = p.y;
        return true;
    }

    /// <summary>放一颗字：池里挑一格（环形容器；满了就让最老的先淡出腾位），量尺寸、找落点、算出生点</summary>
    private static void DevGravitySpawn(int fontSize, float cx, float cy) {
        if (_devGravPoolMade >= DevGravPoolSize) DevGravityRecycleOne();
        int idx = -1;
        for (int k = 0; k < DevGravPoolSize; k++) {
            int c = _devGravCursor + k;
            if (c >= DevGravPoolSize) c -= DevGravPoolSize;
            if (_devGravStArr[c] != (byte)DevGravSt.Idle) continue;
            idx = c;
            _devGravCursor = c + 1 >= DevGravPoolSize ? 0 : c + 1;
            break;
        }
        if (idx < 0) return;                       // 池真的满了（不该发生：上面刚回收过一颗）
        var t = DevGravityPoolText(idx);
        if (t == null) return;

        // 字符集里随机取一个（不 new 字符串、不拼串）
        int ci = Mathf.Clamp((int)(UnityEngine.Random.value * DevGravChars.Length), 0, DevGravChars.Length - 1);
        t.text = DevGravChars.Substring(ci, 1);     // Substring 每次一颗：不是每帧，可以接受
        t.fontSize = fontSize;
        t.color = new Color(DevGravTextColor.r, DevGravTextColor.g, DevGravTextColor.b, 1f);
        t.gameObject.SetActive(true);

        RectTransform rt = _devGravTr[idx];
        rt.sizeDelta = new Vector2(0f, 0f);
        rt.localPosition = new Vector3(0f, 0f, 0f);
        rt.localScale = new Vector3(1f, 1f, 1f);
        rt.localEulerAngles = Vector3.zero;
        float ew = t.preferredWidth;               // 源码是 offsetWidth：量出来的实际宽高
        float eh = t.preferredHeight;
        if (ew < 0.5f) ew = 1f;
        if (eh < 0.5f) eh = 1f;
        _devGravEw[idx] = ew;
        _devGravEh[idx] = eh;
        _devGravW[idx] = ew;
        _devGravH[idx] = eh;
        _devGravDx[idx] = 0f;
        _devGravDy[idx] = 0f;
        _devGravSquare[idx] = Mathf.Min(DevGravSquareCap, eh / ew);

        // 出生点：默认就是点击处（左上角对齐，减去半宽半高），再夹进容器里（源码 attach 里的两段 clamp）
        float width = _devGravRect.rect.width;
        float height = _devGravRect.rect.height;
        _devGravX[idx] = Mathf.Clamp(cx - ew * 0.5f, 0f, Mathf.Max(width - ew, 0f));
        _devGravY[idx] = cy - eh * 0.5f;
        _devGravX0[idx] = _devGravX[idx];
        _devGravY0[idx] = _devGravY[idx];
        _devGravVy[idx] = 0f;
        _devGravBounced[idx] = false;
        _devGravDone[idx] = false;
        _devGravStill[idx] = false;                // 源码 still 来自 prefers-reduced-motion；我们不做那套，恒为 false
        _devGravSway[idx] = 0f;
        _devGravLeaveA[idx] = 1f;
        _devGravSquashK[idx] = -1f;                // -1 = 没有 squash 动画在跑
        _devGravRevealK[idx] = 0f;
        _devGravStArr[idx] = (byte)DevGravSt.Alive;
        _devGravAlive++;
        _devGravProbeSlot = idx;                  // 半秒后再报一次它的状态（见 DevGravityLogLate）
        _devGravProbeTimer = 0.5f;

        // spin 是"当前转了多少度"、vr 是"每秒转多少度"（源码 Body.spin / Body.vr）
        _devGravRot[idx] = 0f;
        float sq = _devGravSquare[idx];
        _devGravSpin[idx] = 0f;
        _devGravVr[idx] = DevGravityRand(-1f, 1f) * (DevGravSpinRate + DevGravSpinRateSquare * sq);

        int need = DevGravityHmNeed(width);
        if (_devGravHeights.Length < need) _devGravHeights = new float[need];
        _devGravHmCols = need;
        DevGravityRetarget(idx, width, height);
        DevGravitySpawnHeight(idx);
        rt.localScale = new Vector3(DevGravSpawnPitch, DevGravSpawnPitch, 1f);   // spawn 动画起点 scale(0.5)
        DevGravityPaint(idx);
    }

    /// <summary>出生高度（源码 attach 里那段）：先保证离落点至少 CLEARANCE 的空隙，
    /// 再看有没有还在掉的字横向压在这个落点上，压着就再往上顶 h + 8。</summary>
    private static void DevGravitySpawnHeight(int idx) {
        float startY = Mathf.Min(_devGravY[idx], _devGravTy[idx] - DevGravClearance);
        for (int j = 0; j < _devGravPoolMade; j++) {
            if (j == idx || _devGravStArr[j] != (byte)DevGravSt.Alive || _devGravDone[j]) continue;
            bool overlaps = _devGravTx[j] < _devGravTx[idx] + _devGravW[idx]
                            && _devGravTx[idx] < _devGravTx[j] + _devGravW[j];
            if (!overlaps) continue;
            float up = _devGravY[j] - _devGravH[idx] - DevGravOverlapGap;
            if (up < startY) startY = up;
        }
        _devGravY[idx] = startY;
        _devGravY0[idx] = startY;
    }

    /// <summary>回收一颗（超上限时挑最老的那颗走 350ms 淡出；一颗空闲都没有时才真回收）</summary>
    private static void DevGravityRecycleOne() {
        if (_devGravAlive < DevGravMaxGlyphs) return;   // 没满，不用腾
        int pick = -1;
        float best = float.PositiveInfinity;
        for (int j = 0; j < _devGravPoolMade; j++) {
            if (_devGravStArr[j] != (byte)DevGravSt.Alive) continue;
            if (_devGravLeaveA[j] < best) { best = _devGravLeaveA[j]; pick = j; }   // 淡出得最久的 = 最老的
        }
        if (pick < 0) return;
        _devGravStArr[pick] = (byte)DevGravSt.Leaving;
        _devGravLeaveA[pick] = 1f;
        _devGravDone[pick] = true;
        _devGravPhysicsOn++;
    }

    /// <summary>懒建 + 复用：池里的 Text 只建一次，之后只改文字与位置（绝不每次点击 new GameObject）</summary>
    private static Text DevGravityPoolText(int idx) {
        if (_devGravText != null && _devGravText[idx] != null) return _devGravText[idx];
        if (idx > _devGravPoolMade) return null;          // 只能按顺序往外建
        var go = new GameObject("Glyph", typeof(RectTransform));
        go.transform.SetParent(_devGravRect, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);              // 左上角锚点：localPosition 就是"左上角位置"
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);              // 缩放/旋转绕中心（源码 span 是 inline-block）
        rt.sizeDelta = new Vector2(0f, 0f);
        var t = go.AddComponent<Text>();
        t.font = _uiFont;
        t.fontSize = DevGravSize;
        t.color = DevGravTextColor;
        t.alignment = TextAnchor.UpperLeft;              // 锚点=左上，我们自己算出来的就是左上角
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;                         // 关键：绝不吃射线、绝不抢点击/输入框焦点
        t.supportRichText = false;
        go.SetActive(false);
        _devGravText[idx] = t;
        _devGravTr[idx] = rt;
        if (idx + 1 > _devGravPoolMade) _devGravPoolMade = idx + 1;
        return t;
    }

    // ================= 物理 =================

    /// <summary>每帧积分（源码 frame）：vy += g*dt；y += vy*dt；x 按"落体进度"从出生点插到落点；
    /// 旋转由自转混到静止倾角；地面倾斜超过阈值就侧滑；落地那一下按 BOUNCE 反弹一次。</summary>
    private static void DevGravityStep(float dt) {
        if (_devGravPoolMade == 0) return;
        if (_devGravPhysicsOn == 0) return;                 // 都静止了：一帧都不算（省电也省得每帧写属性）
        if (dt <= 0f) return;
        if (dt > 1f / 30f) dt = 1f / 30f;                   // 源码 dt = min(..., 1/30)
        // 单帧步进上限：大卡顿帧最多按这么小的一步积分几次，剩下的丢掉 —— 字符绝不瞬移
        float step = dt;
        int steps = 1;
        if (step > 1f / 120f) {
            steps = Mathf.CeilToInt(step / (1f / 120f));
            if (steps > DevGravMaxSubSteps) steps = DevGravMaxSubSteps;
            step = dt / steps;
        }
        int active = 0;
        for (int s = 0; s < steps; s++) {
            active = 0;
            for (int i = 0; i < _devGravPoolMade; i++) {
                byte st = _devGravStArr[i];
                if (st == (byte)DevGravSt.Leaving) { active += DevGravityFade(i, dt); continue; }
                if (st != (byte)DevGravSt.Alive || _devGravDone[i]) continue;

                _devGravVy[i] += DevGravGravity * step;
                _devGravY[i] += _devGravVy[i] * step;
                _devGravSpin[i] += _devGravVr[i] * step;
                DevGravityDrift(i);

                if (_devGravY[i] >= _devGravTy[i]) {
                    DevGravityLand(i);
                    if (!_devGravDone[i]) active++;
                    continue;
                }
                DevGravityPaint(i);
                active++;
            }
        }
        _devGravPhysicsOn = active;

        // 泼洒（按住不放）：只有活着的时候才有意义
        if (_devGravPourHold >= DevGravHoldMs) {
            DevGravityPour((int)(dt * 1000f));
            _devGravPhysicsOn++;
        }
    }

    /// <summary>横向：x 从出生点按 eased 进度插到落点，再加上 sin(πp)*sway 的摆动（源码那三行）。</summary>
    private static void DevGravityDrift(int i) {
        float total = _devGravTy[i] - _devGravY0[i];
        float p = total > 0f ? Mathf.Min((_devGravY[i] - _devGravY0[i]) / total, 1f) : 1f;
        if (p < 0f) p = 0f;
        _devGravX[i] = _devGravX0[i] + (_devGravTx[i] - _devGravX0[i]) * p * (2f - p)
                       + Mathf.Sin(p * Mathf.PI) * _devGravSway[i];
        float blend = p * p * p;
        _devGravRot[i] = _devGravSpin[i] * (1f - blend) + _devGravRestRot[i] * blend;
    }

    /// <summary>落地：摆正 → 还能蹦就蹦一次（BOUNCE）→ 否则就地躺平（done），带一下 sqush 抖动</summary>
    private static void DevGravityLand(int i) {
        _devGravX[i] = _devGravTx[i];
        _devGravY[i] = _devGravTy[i];
        _devGravRot[i] = _devGravRestRot[i];

        float rebound = _devGravVy[i] * DevGravBounce;
        if (!_devGravBounced[i] && rebound * rebound > DevGravGravity * DevGravFreeSlideKt) {
            _devGravBounced[i] = true;
            _devGravVy[i] = -rebound;
            _devGravX0[i] = _devGravTx[i];
            _devGravY0[i] = _devGravTy[i];
            _devGravSway[i] = 0f;
            _devGravVr[i] = 0f;
            _devGravSpin[i] = _devGravRestRot[i];
            DevGravityPaint(i);
            _devGravSquashK[i] = 0f;      // 源码触地也 squash 一次
            return;                       // 还没完，继续算（active）
        }
        _devGravDone[i] = true;
        DevGravityPaint(i);
        if (!_devGravBounced[i]) _devGravSquashK[i] = 0f;
    }

    /// <summary>超上限那颗的淡出：350ms 线性淡到 0（源码 transition-opacity 300ms + 350ms 后移除），
    /// 淡完立刻回到池里待命。单帧步进上限保证卡顿帧也不会"一下淡完"。</summary>
    private static int DevGravityFade(int i, float dt) {
        float step = dt / DevGravLeaveSec;
        if (step > DevGravFadeStep) step = DevGravFadeStep;
        _devGravLeaveA[i] -= step;
        if (_devGravLeaveA[i] <= 0f) {
            _devGravLeaveA[i] = 0f;
            DevGravityPaint(i);
            _devGravStArr[i] = (byte)DevGravSt.Idle;
            _devGravShown[i] = false;
            _devGravText[i].gameObject.SetActive(false);
            if (_devGravAlive > 0) _devGravAlive--;
            return 0;
        }
        DevGravityPaint(i);
        return 1;
    }

    /// <summary>把一颗的状态写进 Transform / Text.color（这是唯一"画"的地方）。
    /// 只有 anchoredPosition / localScale / localEulerAngles / color 四个属性，没有别的写入。</summary>
    private static void DevGravityPaint(int i) {
        var rt = _devGravTr[i];
        if (rt == null) return;
        _devGravShown[i] = true;
        rt.anchoredPosition = new Vector2(_devGravX[i] + _devGravDx[i], -(_devGravY[i] + _devGravDy[i]));

        float sx = 1f, sy = 1f;
        float rk = _devGravRevealK[i];
        if (rk < 1f) { float k = DevGravSpawnPitch + (1f - DevGravSpawnPitch) * rk; sx *= k; sy *= k; }
        float sk = _devGravSquashK[i];
        if (sk >= 0f && sk < 1f) sy *= DevGravSquashFrom + (1f - DevGravSquashFrom) * sk;
        rt.localScale = new Vector3(sx, sy, 1f);
        rt.localEulerAngles = new Vector3(0f, 0f, _devGravRot[i]);

        float a = _devGravLeaveA[i];
        if (rk < 1f) a *= DevGravSpawnAlpha + (1f - DevGravSpawnAlpha) * rk;
        var t = _devGravText[i];
        t.color = new Color(DevGravTextColor.r, DevGravTextColor.g, DevGravTextColor.b, a);
    }

    /// <summary>squash 与出生缩放的推进（跟物理解耦：躺平之后也还能把这一下播完）。
    /// 源码那两段 WAAPI：出生 scale(0.5)→1 + opacity 0.3→1，150ms；触地 scaleY(0.82)→1，160ms；
    /// 缓动 cubic-bezier(0.215,0.61,0.355,1) 解出来是匀速，所以这里按线性推进，不另编缓动。
    /// uGUI 做不了非等比缩放动画，只能自己每帧写 localScale（这也是"手写 uGUI"那层边界内的最接近做法）。</summary>
    private static void DevGravityTickTweens(float dt) {
        if (_devGravPoolMade == 0 || dt <= 0f) return;
        float revStep = dt / (DevGravSpawnMs * 0.001f);
        float squStep = dt / DevGravSquashSec;
        if (revStep > 0.34f) revStep = 0.34f;   // 单帧步进上限：大卡顿帧不许把动画一次冲完
        if (squStep > 0.34f) squStep = 0.34f;
        for (int i = 0; i < _devGravPoolMade; i++) {
            if (_devGravStArr[i] == (byte)DevGravSt.Idle) continue;
            bool need = false;
            if (_devGravRevealK[i] < 1f) {
                _devGravRevealK[i] += revStep;
                if (_devGravRevealK[i] > 1f) _devGravRevealK[i] = 1f;
                need = true;
            }
            float sk = _devGravSquashK[i];
            if (sk >= 0f && sk < 1f) {
                sk += squStep;
                if (sk > 1f) sk = 1f;
                _devGravSquashK[i] = sk;
                need = true;
            }
            if (need) { DevGravityPaint(i); _devGravPhysicsOn++; }
        }
    }

    // ================= 落点求解（高度图） =================

    /// <summary>高度图要几格：ceil(width / COL)，至少一格（源码 cols）</summary>
    private static int DevGravityHmNeed(float width) {
        int c = Mathf.CeilToInt(width / DevGravCol);
        return c < 1 ? 1 : c;
    }

    /// <summary>这段 x 覆盖到哪几格（源码 spanOf：左右都夹进 [0, cols-1]，越界就往里挤）</summary>
    private static void DevGravitySpanOf(int[] span, float x, float w) {
        int cols = _devGravHmCols;
        int from = Mathf.Max(0, Mathf.FloorToInt(x / DevGravCol));
        int to = Mathf.Min(cols - 1, Mathf.Max(from, Mathf.CeilToInt((x + w) / DevGravCol) - 1));
        span[0] = from;
        span[1] = to;
    }

    /// <summary>这段格子里最高的一摞；越界返回 +∞（源码 windowTop，findRestX 靠它判断"外面没东西"）</summary>
    private static float DevGravityWindowTop(int from, int to) {
        if (from < 0 || to >= _devGravHmCols) return float.PositiveInfinity;
        float top = 0f;
        for (int i = from; i <= to; i++) if (_devGravHeights[i] > top) top = _devGravHeights[i];
        return top;
    }

    private static readonly int[] _devGravSpan = new int[2];

    /// <summary>斜着躺的时候，边缘那点还要再往下一点：挑各格里最小的那个抬升量（源码 restY）</summary>
    private static float DevGravityRestY(float x, float w, float h, float rot, float height) {
        DevGravitySpanOf(_devGravSpan, x, w);
        int from = _devGravSpan[0], to = _devGravSpan[1];
        float tan = Mathf.Tan(Mathf.Abs(rot) * Mathf.Deg2Rad);
        float best = float.PositiveInfinity;
        for (int i = from; i <= to; i++) {
            float center = (i + 0.5f) * DevGravCol - x;
            float cx = Mathf.Clamp(center, 0f, w);
            float edge = Mathf.Min((rot >= 0f ? w - cx : cx) * tan, h - DevGravSweepEps);
            float cand = height - _devGravHeights[i] - h + edge;
            if (cand < best) best = cand;
        }
        return best;
    }

    /// <summary>把这一颗的高度记进高度图（源码 deposit）：各格取最大值</summary>
    private static void DevGravityDeposit(float x, float w, float h, float rot, float y, float height) {
        DevGravitySpanOf(_devGravSpan, x, w);
        int from = _devGravSpan[0], to = _devGravSpan[1];
        float tan = Mathf.Tan(Mathf.Abs(rot) * Mathf.Deg2Rad);
        for (int i = from; i <= to; i++) {
            float center = (i + 0.5f) * DevGravCol - x;
            float cx = Mathf.Clamp(center, 0f, w);
            float edge = Mathf.Min((rot >= 0f ? cx : w - cx) * tan, h - DevGravSweepEps);
            float v = height - y - edge;
            if (v > _devGravHeights[i]) _devGravHeights[i] = v;
        }
    }

    /// <summary>脚底下这块地斜多少度（源码 groundTilt，按左右两半的顶面高度差算）</summary>
    private static float DevGravityGroundTilt(float x, float w) {
        DevGravitySpanOf(_devGravSpan, x, w);
        int from = _devGravSpan[0], to = _devGravSpan[1];
        if (to <= from) return 0f;
        int mid = (int)Mathf.Ceil((from + to) * 0.5f);
        float hl = DevGravityWindowTop(from, mid - 1);
        float hr = DevGravityWindowTop(mid, to);
        if (float.IsPositiveInfinity(hl) || float.IsPositiveInfinity(hr)) return 0f;
        float run = Mathf.Max(((to - from + 1) * 0.5f) * DevGravCol, 1f);
        return Mathf.Atan2(hl - hr, run) * Mathf.Rad2Deg;
    }

    /// <summary>沿地面找该停在哪一格（源码 findRestX）：两边都低过阈值就跟随机出来的 bias 走，
    /// 只有一边低就往那边滑，最多扫 64 次。wind（倾斜）不为 0 时滑落阈值乘 EAGER。</summary>
    private static float DevGravityFindRestX(float x, float ew, float eh, float maxX, int bias, float eager) {
        float cur = Mathf.Min(Mathf.Max(x, 0f), maxX);
        float drop = eh * DevGravSlope * eager;
        int step = Mathf.Max(DevGravCol, Mathf.RoundToInt(ew * DevGravSlideStepK));
        for (int it = 0; it < DevGravSlideIters; it++) {
            DevGravitySpanOf(_devGravSpan, cur, ew);
            int from = _devGravSpan[0], to = _devGravSpan[1];
            int span = to - from + 1;
            float top = DevGravityWindowTop(from, to);
            float dl = top - DevGravityWindowTop(from - span, from - 1);
            float dr = top - DevGravityWindowTop(to + 1, to + span);

            float next = cur;
            if (dl > drop && dr > drop && Mathf.Abs(dl - dr) <= 1f) {
                next = bias < 0 ? Mathf.Max(cur - step, 0f) : Mathf.Min(cur + step, maxX);
            } else if (dl > drop && dl >= dr) {
                next = Mathf.Max(cur - step, 0f);
            } else if (dr > drop) {
                next = Mathf.Min(cur + step, maxX);
            }
            if (next == cur) break;
            cur = next;
        }
        return cur;
    }

    /// <summary>给这一颗重新算落点（源码 retarget）：先找该停的 x，再按地面倾角加一点随机抖动当静止倾角，
    /// 然后按这个倾角算出"占位外框"（旋转后的宽高），最后把落点写进高度图。</summary>
    private static void DevGravityRetarget(int i, float width, float height) {
        int bias = _wind != 0 ? _wind : (UnityEngine.Random.value < 0.5f ? -1 : 1);
        float eager = _wind != 0 ? DevGravEager : 1f;
        float maxX = Mathf.Max(width - _devGravEw[i], 0f);
        float seekX = DevGravityFindRestX(_devGravX[i], _devGravEw[i], _devGravEh[i], maxX, bias, eager);

        float jitter = DevGravityRand(-1f, 1f) * (DevGravJitterBase + DevGravJitterSquare * _devGravSquare[i]);
        float restRot = Mathf.Clamp(DevGravityGroundTilt(seekX, _devGravEw[i]) + jitter, -DevGravTilt, DevGravTilt);
        float rad = Mathf.Abs(restRot) * Mathf.Deg2Rad;
        _devGravRestRot[i] = restRot;
        _devGravW[i] = _devGravEw[i] * Mathf.Cos(rad) + _devGravEh[i] * Mathf.Sin(rad);
        _devGravH[i] = _devGravEw[i] * Mathf.Sin(rad) + _devGravEh[i] * Mathf.Cos(rad);
        _devGravDx[i] = (_devGravW[i] - _devGravEw[i]) * 0.5f;
        _devGravDy[i] = (_devGravH[i] - _devGravEh[i]) * 0.5f;

        float tx = Mathf.Clamp(seekX - _devGravDx[i], 0f, Mathf.Max(width - _devGravW[i], 0f));
        float ty = DevGravityRestY(tx, _devGravW[i], _devGravH[i], restRot, height);
        DevGravityDeposit(tx, _devGravW[i], _devGravH[i], restRot, ty, height);
        _devGravX0[i] = _devGravX[i];
        _devGravY0[i] = Mathf.Min(_devGravY[i], ty);
        _devGravTx[i] = tx;
        _devGravTy[i] = ty;
        float fall = ty - _devGravY0[i];
        _devGravSway[i] = DevGravityRand(-1f, 1f) * Mathf.Min(DevGravSwayMax, fall * DevGravSwayK);
        _devGravBounced[i] = false;
        _devGravDone[i] = false;
    }

    /// <summary>按高度图重排（源码 rebuild）：清空高度图，从下往上（y 大的先）把每一颗重新摆一遍；
    /// 躺好的原地重新登记，悬空/被风推动的重新落一次。wind 不为 0 时悬空的那些会滑下来并重新转起来。</summary>
    private static void DevGravityRebuild(bool slide) {
        if (_devGravRect == null) return;
        float width = _devGravRect.rect.width;
        float height = _devGravRect.rect.height;
        int need = DevGravityHmNeed(width);
        if (_devGravHeights.Length < need) _devGravHeights = new float[need];
        for (int c = 0; c < need; c++) _devGravHeights[c] = 0f;
        _devGravHmCols = need;

        // 先把活着的收进暂存，再按下上顺序排（插入排序：数量 <= 池大小，零分配、无 LINQ）
        int n = 0;
        for (int i = 0; i < _devGravPoolMade; i++) {
            byte st = _devGravStArr[i];
            if (st == (byte)DevGravSt.Idle || st == (byte)DevGravSt.Leaving) continue;
            int k = n;
            while (k > 0 && _devGravY[_devGravOrder[k - 1]] < _devGravY[i]) { _devGravOrder[k] = _devGravOrder[k - 1]; k--; }
            _devGravOrder[k] = i;
            n++;
        }
        for (int k = 0; k < n; k++) {
            int i = _devGravOrder[k];
            _devGravSpin[i] = _devGravRot[i];
            if (!_devGravDone[i]) {
                DevGravityRetarget(i, width, height);
                _devGravPhysicsOn++;
                continue;
            }
            float rest = DevGravityRestY(_devGravX[i], _devGravW[i], _devGravH[i], _devGravRestRot[i], height);
            bool falls = _devGravY[i] < rest - DevGravRestEps;
            if (!falls && slide) {
                int dir = _wind != 0 ? _wind : 1;
                float maxEw = Mathf.Max(width - _devGravEw[i], 0f);
                float probe = DevGravityFindRestX(_devGravX[i], _devGravEw[i], _devGravEh[i], maxEw, dir, DevGravEager);
                float candX = Mathf.Clamp(probe - _devGravDx[i], 0f, Mathf.Max(width - _devGravW[i], 0f));
                float candY = DevGravityRestY(candX, _devGravW[i], _devGravH[i], _devGravRestRot[i], height);
                falls = candY > _devGravY[i] + DevGravSlideMinDrop;
                if (falls) _devGravVr[i] = DevGravityRand(-40f, 40f);   // 源码：滑下去的时候再转起来
            }
            if (falls) {
                _devGravVy[i] = 0f;
                DevGravityRetarget(i, width, height);
                _devGravPhysicsOn++;
            } else {
                DevGravityDeposit(_devGravX[i], _devGravW[i], _devGravH[i], _devGravRestRot[i], _devGravY[i], height);
            }
        }
    }

    /// <summary>[min,max) 的随机数（源码 rand）</summary>
    private static float DevGravityRand(float min, float max) {
        return min + UnityEngine.Random.value * (max - min);
    }
}
