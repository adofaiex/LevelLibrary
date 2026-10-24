using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：个人页
/// 深底（背景图 + 遮罩）居中一列：圆头像 → 用户名（不加框）→ 登录按钮。
/// 流程：开始界面滑走（库内首屏）→ 本页 → 点登录 → 按钮渐隐 + 逐字显示「欢迎回来，身份，用户名」→ 停一下 → 整页渐隐 → 复用启动页那套把列表/主页/顶部 tab 淡回来。
/// 「登录」= 确认本地昵称，不联网；身份为空时那行只写「欢迎回来，用户名」。
/// 屏保滑走不走本页（屏保就是"关掉"）；两类入口的差别由 `_pfFromLib` 决定。</summary>
public static partial class PortalUI {

    private static GameObject _pfGo;
    private static CanvasGroup _pfGroup;
  /// <summary>这一屏的代码背景那一组（视频层要插在它上面，见 PortalUI.VideoBg.AttachVideoBg）</summary>
    private static CanvasGroup _pfNetGroup;
  /// <summary>这一屏的视频层（没挂上 = null；预热那次可能还挂不上，显示时补挂）</summary>
    private static RawImage _pfVideoLayer;
    private static int _pfVideoTries;
  /// <summary>整屏无颜色的模糊遮罩（每帧跟着糊图链最糊那一档换纹理；链没起来就是空的一层）</summary>
    private static RawImage _pfBlurVeil;
    private static Image _pfDarkVeil;      // 整屏压暗遮罩（盖层，不参与内容渐入）
  /// <summary>遮罩那条点名最多打三条（见 TickProfileScreen）</summary>
    private static int _pfVeilLogs;
    private static Text _pfName, _pfWelcome;
    private static RectTransform _pfLoginRt;
    private static CanvasGroup _pfLoginGroup;
    private static CanvasGroup _pfContentGroup;   // 只让"内容"渐入的那一层（背景/遮罩不跟着淡）
    private static bool _pfSigned;              // 已经点过登录 → 按钮渐隐、换成欢迎那行
    private static bool _pfFromLib;             // true=库内首屏（收尾回主界面）false=屏保（保留原位置）
    private static int _pfPhase;                // -3=拖动渐显中 -2=自己渐入 0=等点登录 1=欢迎停留 2=整页渐隐 3=收尾
    private static float _pfT;

    private const float PfFadeSeconds = 0.5f;   // 按钮渐隐 / 整页渐隐
  // 本页自己的渐入：仍然是在滑出一开始就建好、自己渐入，所以不会变回"等 1 秒"
    private const float PfInSeconds = 0.45f;
    private const float PfHoldSeconds = 0.9f;   // 「欢迎回来…」停多久
    private const float PfAvatarSize = 180f;    // 头像直径
  // 名字那行必须走中心锚点（画布中心 + PfNameY），跟头像/登录按钮/欢迎语同一套坐标。
  // 画布高不是恒 1080（16:10→1200、4:3→1440）：按"距画布顶"定位的话，画布高 > 1160 之后名字会爬上去压到头像上。
    private const float PfNameW = 800f;
    private const float PfNameH = 80f;
    private const float PfNameY = 20f;      // 名字那行的中心（画布中心上方多少）
  // 与头像的关系：头像中心 +150、半径 90 底边 +60；名字框顶边 = PfNameY + PfNameH/2 = +60 正好贴着
    private const float PfLoginY = -110f;       // 登录按钮中心（画布中心下方多少）
  // 欢迎那行与登录按钮共用同一个锚点 + 同一个 PfLoginY：只有一个 y 值，以后不会再飘
    private const float PfWelcomeW = 1100f;     // 欢迎那行文本框宽（"欢迎回来，身份，名字"够用）
    private const float PfWelcomeH = 60f;
  // 头像 + 名字 + 登录按钮 外面那块磨砂长方框（屏幕居中）。纵向要框住的范围：
  // 头像顶边 +240（中心 150 + 半径 90）到 登录按钮底边 -144（中心 -110 - 高 68/2），
  // 取内容中心 +48、上下各留 232，于是高 560（余量：上方 72、下方 88）。
  // 宽 900 / 高 560 ≈ 1.607（接近黄金比），比登录按钮（260）与名字那行（800）都宽出一圈。
  /// <summary>个人页的整屏压暗遮罩深浅：跟面板那层 0.55 的模态压暗分开 ——
  /// 这一屏只是"背景压暗"，压到 0.45 就够（再深头像那圈主题色描边会糊掉）。</summary>
    private static readonly Color ProfileVeilColor = new Color(0f, 0f, 0f, 0.45f);
    private const float PfCharSeconds = 0.07f;  // 打字机速度：每个字 0.07s
  // 打字机上限：等它打完再停 PfHoldSeconds，但最多等这么久，免得名字+身份长到离谱时把交接拖死
    private const float PfTypeMaxWaitSeconds = 4f;

    private static string _pfWelcomeFull = ""; // 欢迎那行的全文（逐字揭示用）
    private static int _pfShownChars;          // 已经显示了几个字

  // ---------- 登录界面的背景放大 ----------
  // 揭幕段里背景跟着封面一起放大（两边是同一张底、同一个缩放 = 交叉淡入无缝），封面让完之后
  // 放大"留"在登录界面上 —— 一直保持到点登录、整页渐隐去主界面那一刻才平滑缩回原大。
  // 缩的是四块整屏背景（纯色底[含静态图子节点] / 代码背景组[含糊图层与视频层] / 糊图罩 / 压暗罩）：
  // 全是 Stretch 铺满、中心 pivot，放大只会往外长、四边不会露缝；内容层不在里面，头像/名字/按钮不受影响。
    private const float PfZoomMax = 1.12f;
    private const float PfZoomFollow = 3.5f;   // 进主界面时的回缩速率（指数平滑，1/s）
    private static float _pfZoom = 1f;         // 当前缩放（唯一写入点 SsApplyProfileZoom）
    private static bool _pfZoomRestore;        // 登录交接、整页渐隐那一刻置起
    private static RectTransform _pfBgRt;      // 纯色底（静态背景图是它的子节点，跟着一起缩）

  /// <summary>登录界面背景缩放的唯一写入点：z = 目标缩放（1 = 原大）。
  /// 揭幕段由 SsApplyScrub 按登录界面的渐显进度喂；登录期间保持不动；进主界面的渐隐里缩回 1。
  /// 变化超过 0.001 就记一行（带调用方标签）：揭幕是平滑推进，每帧增量只有千分之几，
  /// 门槛粗了会一条都不打（误判成"从没放大过"），所以这里必须细。</summary>
    private static void SsApplyProfileZoom(float z, string who) {
        float old = _pfZoom;
        _pfZoom = Mathf.Clamp(z, 1f, PfZoomMax);
        if (Mathf.Abs(_pfZoom - old) > 0.001f && _pfZoomLogs < 80) {
            _pfZoomLogs++;
            Lg.Info("登录页背景缩放 " + old.ToString("F2") + "→" + _pfZoom.ToString("F2")
                + "（" + who + "）");
        }
        var sc = new Vector3(_pfZoom, _pfZoom, 1f);
        if (_pfBgRt != null) _pfBgRt.localScale = sc;
        if (_pfNetGroup != null) _pfNetGroup.transform.localScale = sc;
        if (_pfBlurVeil != null) _pfBlurVeil.rectTransform.localScale = sc;
        if (_pfDarkVeil != null) _pfDarkVeil.rectTransform.localScale = sc;
    }
    private static int _pfZoomLogs;

  // ---------- 登录按钮的键盘快捷与悬停提示 ----------
  /// <summary>空格 / 回车 = 点「登录」。悬停提示是按钮底下那行很淡的小字，鼠标停在按钮上才浮出来。</summary>
    private const float PfHintAlpha = 0.55f;   // 提示的目标透明度（刻意压低：别太显眼）
    private static CanvasGroup _pfLoginHint;   // 提示那行的组（只淡它自己，不碰按钮）
    private static float _pfHintA;             // 当前透明度（指数平滑过去的）

  // ---------- 从登录界面回开始界面（仅 Esc 复用这段回程动画；下滑手势已摘除） ----------
  /// <summary>往回拖到阈值时，登录界面淡到还剩多少（剩下的留给"滑回去"那 0.55 秒补完）。</summary>
    private const float PfBackFadeK = 0.8f;
    private static int _pfBackPhase;           // 0=空闲 2=回开始界面(补完) 3=弹回（1=拖着 与 0 的鼠标起手一起摘了）
    private static float _pfBackT, _pfBackRel;
  /// <summary>拖动期间给登录按钮设的忽略窗口：拖完松手那一下手指正好还在按钮上时，uGUI 会把它当一次点击。
  /// （拖动期间把它按掉；普通"点一下"根本不进拖动，不受影响。）</summary>
    private static float _pfBackIgnoreLoginUntil = -1f;

  /// <summary>开始界面"把手交给个人页"（滑出一开始就调，库内首屏与屏保两条路都走它）。
  /// `fromLib` 决定收尾时去哪：库内首屏 → 主界面；屏保 → 保持原位置。</summary>
    private static void ProfileHandOffFromStart(bool fromLib) {
        _pfFromLib = fromLib;
    // 三个卡点（上滑出现登录界面 / 点登录 / 登录后进主界面）之间隔着好几秒，
    // 而子阶段计时窗口只在开面板、建开始界面时武装 120 帧 不重新武装就采不到。
        StartPanelProfiling();
        var gcPfA = TakeGc();
        var swPfShow = System.Diagnostics.Stopwatch.StartNew();
        bool needBuild = !(_pfRefs != null && _pfRefs.Go != null);
        ProfileShowAfterStart();
        var gcPfB = TakeGc();
        Lg.Info($"[LevelLib] 【卡点】上滑显示个人页耗时 = {swPfShow.Elapsed.TotalMilliseconds:F1} ms"
            + $"（须现建={(needBuild ? "是" : "否 用的是预热好的")}）"
            + $" ｜ {GcDeltaText(gcPfA, gcPfB)} ｜ {FrameContextText()}");
    }

  // ---------- "拖着渐渐显示"那条路（由开始界面驱动）----------
  // 三个阶段各有各的主人，别互相插嘴：
  // 拖动中 / 滑回中 —— 开始界面按距离喂透明度（`ProfileScrubSet`）；滑出那 0.55 秒仍由它补到 100%
  // 滑出收尾 —— 就地交给个人页自己（`ProfileScrubCommit` → `_pfPhase = 0`），它那 0.45 秒的渐入一次都不跑（= 两个动画不重叠）

  /// <summary>开始界面按下那一刻调用：把这一页准备好（激活 + 摆名字/头像/状态），返回 true 表示这次由拖动驱动它的透明度。
  /// 必须排在"按下"而不是"第一次拖动"：激活整页要几毫秒（头像还要读盘），放在拖动那条路上会卡一下、跟手就不顺。</summary>
    private static bool ProfileScrubBegin() {
        try {
            var gcA = TakeGc();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool needBuild = !(_pfRefs != null && _pfRefs.Go != null);
            ProfileShowAfterStart(true);                 // true = scrub（别起自己的渐入）
            if (_pfGo == null) return false;             // 建不出来就退回老路（宁可没新效果，不能露底）
            Lg.Info($"[LevelLib] 【卡点】拖动起手准备个人页 = {sw.Elapsed.TotalMilliseconds:F1} ms"
                + $"（须现建={(needBuild ? "是" : "否 用的是预热好的")}）"
                + $" ｜ {GcDeltaText(gcA, TakeGc())} ｜ {FrameContextText()}");
            return true;
        } catch (System.Exception e) {
            Lg.Ex("拖动起手准备个人页失败（退回原来的交接方式）", e);
            return false;
        }
    }

  /// <summary>Esc：从登录界面退回开始界面（复用"从上往下滑"那条回程动画，不另起一套）。
  /// 只在"等点登录"那一档、且底下真的压着开始界面时才接 —— 别的情形交给别处的 Esc 分支。</summary>
    private static bool TryCloseProfileByEsc() {
        try {
            if (_pfGo == null || _pfPhase != 0) return false;
            if (!_ssParked && !_ssBehind) return false;
            if (_pfBackPhase != 0) return false;            // 已经在回程里了，让它走完
            _pfBackRel = 0f; _pfBackT = 0f; _pfBackPhase = 2;
            if (_pfContentGroup != null) _pfContentGroup.blocksRaycasts = false;
            Lg.Info("Esc：登录界面 ->  退回开始界面（复用下滑那条动画）");
            return true;
        } catch { return false; }
    }

  /// <summary>从登录界面回开始界面的回程动画。相位：2=滑回（补完）/ 3=弹回（Esc 中途松不开不存在，3 目前走不到但保留对称兜底）。
  /// 入口只有 Esc（TryCloseProfileByEsc 直接把相位拨到 2）：往下滑回开始界面的手势已摘除，
  /// 登录界面的出口只剩 Esc（回开始界面）与「登录」按钮（进主界面）。</summary>
    private static void TickProfileDragBack(float dt) {
        try {
            if (_pfGo == null || _pfPhase != 0) { _pfBackPhase = 0; return; }
            if (_pfBackPhase == 2) {          // 滑回开始界面（把最后一段补完，同一套缓动）
                _pfBackT += dt;
                float k = Mathf.Clamp01(_pfBackT / SsOutSeconds);
        // 把 `dt` 交进去 这一趟的位置与那三样透明度一起过速度帽
                SsPaintReturn(Mathf.Lerp(_pfBackRel, 1f, k * k), dt);   // easeInQuad（跟"滑出去"同一套）
        // 收尾判据是"真的到位了"而不是"时间到"：没到 1 就调 `SsFinishReturnToStart`，
        // 它内部那句把开始界面位置归零会把界面"跳"下去（时限兜底见 SsSlideHardSeconds）
                if (_ssReturnK >= 0.9995f || _pfBackT >= SsSlideHardSeconds) {
                    SsPaintReturn(1f);                                  // 精确落位（dt=0 不掐）
                    _pfBackPhase = 0; SsFinishReturnToStart();
                }
                return;
            }
            if (_pfBackPhase == 3) {          // 弹回登录界面
                _pfBackT += dt;
                float k = Mathf.Clamp01(_pfBackT / SsOutSeconds);
                float e = 1f - (1f - k) * (1f - k);                   // easeOutQuad
                SsPaintReturn(Mathf.Lerp(_pfBackRel, 0f, e), dt);     // 同上：这一条也过速度帽
        // 收尾同样按"真的回到 0"判
                if (_ssReturnK <= 0.0005f || _pfBackT >= SsSlideHardSeconds) {
                    _pfBackPhase = 0; _pfBackT = 0f;
                    SsPaintReturn(0f);                                // 收到"还在登录界面"那一档
                    if (_pfContentGroup != null) _pfContentGroup.blocksRaycasts = true;
                }
            }
        } catch (System.Exception e) {
            Lg.Ex("个人页回开始界面收尾失败（放回原样）", e);
            _pfBackPhase = 0;
            if (_pfContentGroup != null) _pfContentGroup.blocksRaycasts = true;
        }
    }

  /// <summary>"回开始界面"走完了：开始界面摆回完整站着，我们接管的那一页收起来（`SsFinishReturnToStart` 回头调它）。</summary>
    private static void ProfileBackFinish(bool hideProfile) {
        try {
            if (hideProfile) { ProfileScrubAbort(); }
            else {
        // 屏保那条路：那一页本来就在显示 不收它，只是被封面盖回去了，恢复成全亮
                if (_pfContentGroup != null) { _pfContentGroup.alpha = 1f; _pfContentGroup.blocksRaycasts = true; }
                if (_pfGroup != null) { _pfGroup.alpha = 1f; _pfGroup.blocksRaycasts = true; }
            }
            _pfBackPhase = 0; _pfBackT = 0f;
            _pfZoomRestore = false; SsApplyProfileZoom(1f, "回开始界面收尾");   // 封面要全亮盖回来：底下的缩放顺手归位（被盖着，看不到跳）
        } catch (System.Exception e) { Lg.Ex("回开始界面收尾失败", e); }
    }

  /// <summary>按拖动进度喂"内容层"的透明度（0 = 全透明 / 1 = 全亮）。
  /// 只认我们接管的那一页（`_pfPhase == -3` 去程拖动中，或 `_ssScrub`）；
  /// 别的场合一个字都不改——屏保盖在一个已经显示着的个人页上时，绝不许把它按暗。</summary>
    private static void ProfileScrubSet(float a) {
        if (_pfContentGroup == null) return;
        if (_pfPhase != -3 && !_ssScrub) return;
        try { _pfContentGroup.alpha = Mathf.Clamp01(a); } catch { }
    }

  /// <summary>滑出收尾：登录界面已经全亮，就地交给它自己（`_pfPhase = 0` = 等点登录）。
  /// 这一步是"两个动画不重叠"的命门：不置 0 的话 `TickProfileScreen` 会从 0 再渐入一遍。</summary>
    private static void ProfileScrubCommit() {
        try {
            if (_pfPhase != -3) return;
            if (_pfContentGroup != null) { _pfContentGroup.alpha = 1f; _pfContentGroup.blocksRaycasts = true; }
            if (_pfGroup != null) { _pfGroup.alpha = 1f; _pfGroup.blocksRaycasts = true; }
            _pfPhase = 0; _pfT = 0f;
            Lg.Info("个人页：拖动渐显已收尾（透明度由拖动喂满 ->  它自己那 0.45 秒渐入不跑 不重叠）");
        } catch (System.Exception e) { Lg.Ex("拖动渐显收尾失败", e); }
    }

  /// <summary>滑回原位 藏回预热箱（渐隐由开始界面按距离做完，这里只负责收摊）。
  /// 绝不能走 `FinishProfileScreen`：那会把开始界面那层一起销毁、还会交接下一屏。
  /// 认两个相位：`-3`（去程拖动中滑回）和 `0`（从登录界面往下滑回）。</summary>
    private static void ProfileScrubAbort(string why = null) {
        try {
            if (_pfPhase != -3 && _pfPhase != 0) return;
            var r = _pfRefs;
            if (r != null) {
                if (r.ContentGroup != null) { r.ContentGroup.alpha = 0f; r.ContentGroup.blocksRaycasts = false; }
                if (r.Go != null) r.Go.SetActive(false);
            }
            _pfGo = null; _pfGroup = null; _pfContentGroup = null; _pfLoginGroup = null; _pfDarkVeil = null;
            _pfName = null; _pfWelcome = null; _pfLoginRt = null; _pfLoginHint = null;
            _pfPhase = -1; _pfT = 0f;
            _pfZoomRestore = false; SsApplyProfileZoom(1f, "藏回预热箱");   // 藏回预热箱时缩放归位（页已经不可见，看不到跳）
            Lg.Info("个人页：" + (why ?? "上滑没到阈值")
                + " ->  已收起（藏回预热箱 下次直接显示）");
        } catch (System.Exception e) { Lg.Ex("拖动渐显回退失败", e); }
    }

  /// <summary>滑出收尾时"交归属"（谁负责后面那屏）——页面本身早就准备好了，一个字都不用重摆。</summary>
    private static void ProfileScrubHandOff(bool fromLib) {
        _pfFromLib = fromLib;
        if (_pfPhase != 0) ProfileScrubCommit();     // 兜底（正常路径在相位 2 结束时已经 commit 过）
    }

  /// <summary>屏保那条路特有的一种情形：个人页本来就在屏幕上（屏保是盖在它上面的）。
  /// 拖动期间只让封面动，收尾时也不许重摆——重摆 = `_pfPhase` 回到 -2 = 那 0.45 秒渐入又跑一遍，
  /// 屏幕上就是"已经全亮的登录界面突然暗掉再亮一次"。
  /// 另外不许改 `_pfFromLib`：它决定登录完之后去哪一屏，而"屏保盖在已显示的个人页上"时那一页不是屏保建出来的，
  /// 它归更早那次上滑管，那次已经把归属定好了。改成 false 会让登录后不切主界面、停在面板当时那一页（设置页）。
  /// （屏保滑走"保留原本的位置"是由不隐藏这一页来满足的，不靠这个标记。）</summary>
    private static void ProfileKeepShowing(bool fromLib) {
        if (_pfPhase == -3) ProfileScrubCommit();     // 万一被当成 scrub 接管过 补一次收尾
        if (_pfGroup != null) { _pfGroup.alpha = 1f; _pfGroup.blocksRaycasts = true; }
        Lg.Info("个人页：本来就在屏幕上 ->  保持原样交接（不重摆、不重新渐入"
            + " 归属不变（仍是 " + (_pfFromLib ? "登录后进主界面" : "原地不动") + "）"
            + (fromLib ? "｜本次入口=库内首屏" : "｜本次入口=屏保") + "）");
    }

  /// <summary>预热箱：整页建好之后引用都存在这里（藏起来的）。
  /// 外面所有 `_pfGo != null` 的判据（"被挡着吗" / 防叠层 / 打字机）一律照旧生效，
  /// 所以藏起来时要把活动字段清空、显示时再挂回去，别让"藏着的页面"被当成"开着的页面"。</summary>
    private sealed class ProfileRefs {
        public GameObject Go;
        public CanvasGroup Group, ContentGroup, LoginGroup;
        public Text Name, Welcome;
        public RectTransform LoginRt;
        public Image Avatar;
        public CanvasGroup Hint;
    }
    private static ProfileRefs _pfRefs;

  /// <summary>开始界面滑走后进本页。`scrub` = 这次上滑是拖着渐渐显示的 别起自己的渐入
  /// （透明度由开始界面按拖动距离驱动，见 `ProfileScrubSet`；两个渐入都跑就是"两个动画重叠"）。</summary>
    private static void ProfileShowAfterStart(bool scrub = false) {
        try {
            EnsureProfileBuilt();          // 没预热过（屏保直接从主菜单冒出来那条路）才现建
            var r = _pfRefs;
            if (r == null || r.Go == null) return;
      // 把预热箱里的引用挂回活动字段（外面的判据照旧）
            _pfGo = r.Go; _pfGroup = r.Group; _pfContentGroup = r.ContentGroup;
            _pfLoginGroup = r.LoginGroup; _pfName = r.Name; _pfWelcome = r.Welcome; _pfLoginRt = r.LoginRt;
            _pfLoginHint = r.Hint; _pfHintA = 0f;
            if (_pfLoginHint != null) _pfLoginHint.alpha = 0f;   // 提示每趟从隐着起步（悬停才浮）
            _pfGo.SetActive(true);

      // 有视频背景时，登录界面自己的神经图立刻隐藏（immediate，一帧都不亮）：
      // 暂停期间视频系统把 Hidden 放开过（Hidden=false、alpha 爬回 1），上滑一露出它就是亮的，
      // 要等视频判据翻回来才 0.55 秒淡出 —— 用户看到的就是"上滑亮神经图"。
      // 视频层没挂上的（重试中）不藏：那一屏的兜底就是神经图。
            if (VideoBgUsable() && _pfVideoLayer != null && _pfNetGroup != null)
                NetBgSetShown(_pfNetGroup, false, true);

      // 内容每次都重摆：预热是"提前建好对象"，不是"把内容冻住"（玩家可能刚在设置里改过名字/头像）
            if (_pfName != null) {
                _pfName.text = string.IsNullOrEmpty(PlayerName) ? DefaultPlayerName : PlayerName;
                _pfName.fontStyle = FontStyle.Bold;
            }
      // 头像统一走共用写法：自定义 → 包里默认 → 淡色圆占位（只认自定义的话，没设过的人看不到默认头像）
            ApplyAvatarTo(r.Avatar);
      // 五处状态归零（每次上滑都要跟第一次一模一样）。
      // `scrub` 那条路落在 -3 = "等开始界面拖动来喂透明度"，`TickProfileScreen` 见到它一个动作都不做，0.45 秒的渐入一次都不会跑。
            _pfSigned = false; _pfPhase = scrub ? -3 : -2; _pfT = 0f; _pfHandedOff = false;
            _pfWelcomeFull = ""; _pfShownChars = 0;
            _pfZoomRestore = false; SsApplyProfileZoom(1f, "摆出归位");   // 每次摆出来从原大起步（上一趟的缩放不带走）
            if (_pfGroup != null) { _pfGroup.alpha = 1f; _pfGroup.blocksRaycasts = true; }
            if (_pfContentGroup != null) { _pfContentGroup.alpha = 0f; _pfContentGroup.blocksRaycasts = false; }
            if (_pfLoginGroup != null) _pfLoginGroup.alpha = 1f;
            if (_pfWelcome != null) {
                _pfWelcome.text = "";
                var wc2 = _pfWelcome.color;
                _pfWelcome.color = new Color(wc2.r, wc2.g, wc2.b, 0f);
            }
        } catch (System.Exception e) {
            Lg.Ex("个人页显示失败（直接进库）", e);
            FinishProfileScreen();
        }
    }

  /// <summary>整页建一次（预热调它，没预热过时显示那条路兜底也调它）。
  /// 保险丝：万一这一页是在游戏字体还没就绪的时候建出来的，那些 `Text` 的 `font` 会是 null、一个字都不显示
  /// 建之前先问一句字体在不在，建完再验一遍，不对就丢掉重来（几毫秒的事）。</summary>
    private static void EnsureProfileBuilt() {
        if (_pfRefs != null && _pfRefs.Go != null) {
            bool fontOk = true;
            try { fontOk = _pfRefs.Name == null || _pfRefs.Name.font != null; } catch { fontOk = false; }
            if (fontOk) return;
            Lg.Info("个人页那批文字是「没有字体」时建的 ->  丢掉重建");
            try { UnityEngine.Object.Destroy(_pfRefs.Go); } catch { }
            _pfRefs = null;
        }
        if (_uiFont == null) _uiFont = FindUiFont();     // 建之前把字体问清楚（不然又是 font=null）
        BuildProfileScreen(new StepWatch("个人页(现建)"));
    }

    private static void BuildProfileScreen(StepWatch sw) {
        try {
            if (_pfGo != null) return;                        // 防叠层（同开始界面那条规矩）
            var cv = MakeCanvas("LevelLib_Profile", 93000);
            _pfGo = cv.gameObject;
      // 背景图 + 遮罩立刻不透明，渐入只作用在内容层上：封面销毁那一刻整页还是 alpha=0 的话
      // 两块全屏层都不在场，后面的界面会露出来（闪一下）。封面与这一屏本来就同色，衔接无缝。
            _pfGroup = _pfGo.AddComponent<CanvasGroup>();
            _pfGroup.alpha = 1f;
            _pfGroup.blocksRaycasts = true;
            if (sw != null) sw.Mark("建画布");

      // 深底：纯色底 + 代码背景 + 半透明黑遮罩，文字/描边走主题色（不再用白底）
            var bg = MakeImage(cv.transform, "Bg", BgColor);
            Stretch(bg.rectTransform, Vector2.zero, Vector2.one);
            bg.raycastTarget = true;
            _pfBgRt = bg.rectTransform;   // 背景缩放要写它（见 SsApplyProfileZoom）
      // 静态背景图：垫在纯色底上面、代码背景下面（两层互斥：有图时神经图不显示，见 TickNetBg 那条铁律）
            AttachStaticBg(cv.transform, bg.transform);
      // 代码背景 + 动态背景：个人页也挂视频层（玩家在设置里选了视频时才生效）。
      // 视频没开 / 文件不在时 AttachVideoBg 什么都不做，这一屏照旧是代码背景。
      // 顺序：纯色底 → 代码背景 → 视频层 → 遮罩（后面那句 veil 建得比它晚，自然压在它上面）。
            var net = BuildNetBg(cv.transform);
            _pfNetGroup = net;
            _pfVideoLayer = AttachVideoBg(net);
            if (_pfVideoLayer == null) _pfVideoTries = 2;      // 这一轮建的时候挂不上，后面重看时再补挂几次
      // 这一屏必须挂同一套糊图层：上滑时封面要从 1 淡到 0，两边糊得一样多才不会在中间几帧
      // 出现"清晰的糊图叠糊的糊图"那种一眼看得出在切的效果
            StartBgBlurLayers(net != null ? net.transform : bg.transform);
      // 整屏无颜色的模糊遮罩（不是面板那层 0.45 黑）：铺"最糊那一档"、自己不染色，
      // 看着就是背景被糊掉一层。建在糊图各档之后、内容层之前 —— 内容仍然清晰。
      // 浓度只给七成：糊图现在是不透明的（拍平过），全铺等于把背景整个换成一张糊图，
      // 白线结构全糊成一片雾 —— 那就是他说的"灰白背景、不是开始界面同款"。
      // 留三成原样，结构还在、又一眼看得出糊了，这才叫"同款背景 + 全背景磨砂"。
            var veil = new GameObject("BlurVeil", typeof(RectTransform));
            veil.transform.SetParent(cv.transform, false);
            var vri = veil.AddComponent<RawImage>();
            vri.raycastTarget = false;
            vri.color = new Color(1f, 1f, 1f, 0.70f);
            Stretch(vri.rectTransform, Vector2.zero, Vector2.one);
            _pfBlurVeil = vri;
      // 整屏压暗遮罩（加回）：模糊那层只管"糊"，压暗要另有一层才压得住亮度；
      // 它跟模糊层一样属于"盖层"，不参与内容渐入（盖层恒不透明）。
            var dark = MakeImage(cv.transform, "Veil", ProfileVeilColor);
            dark.raycastTarget = false;
            Stretch(dark.rectTransform, Vector2.zero, Vector2.one);
            _pfDarkVeil = dark;
            if (sw != null) sw.Mark("背景图+模糊遮罩+压暗");

      // 内容层（只装"会渐入"的东西，背景/遮罩留在外面永远不透明）
            var content = new GameObject("ProfileContent", typeof(RectTransform));
            content.transform.SetParent(cv.transform, false);
            var crt = content.GetComponent<RectTransform>();
            Stretch(crt, Vector2.zero, Vector2.one);
            _pfContentGroup = content.AddComponent<CanvasGroup>();
            _pfContentGroup.alpha = 0f;              // 内容从 0 渐入
            _pfContentGroup.blocksRaycasts = false;  // 渐入期间不接点击（防止"还没看清就点登录"）
            if (sw != null) sw.Mark("内容层");


      // ① 头像：设了就用玩家的头像，没设就画一个淡色圆占位（不能空着）
            var avatar = MakeImage(content.transform, "Avatar", Color.white);
            var art = avatar.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(PfAvatarSize, PfAvatarSize);
            art.anchoredPosition = new Vector2(0f, 150f);
            avatar.raycastTarget = false;
      // 项目里没有现成的"画圆"函数 用圆角 sprite 把半径给到贴图一半；显示统一走 `ApplyAvatarTo`
            ApplyAvatarTo(avatar);
            if (sw != null) sw.Mark("头像");

      // ② 名字：不要加框（就是一行字，没有任何底板/边框），深底上用白字
            _pfName = MakeTitle(content.transform, "Name", 52, Color.white, TextAnchor.MiddleCenter);
      // 别改回 `SetRectPx(...)`：它按"距画布顶"定位，画布一高就跟头像错位（见上面 PfNameY 那段）
            var nrt = _pfName.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.sizeDelta = new Vector2(PfNameW, PfNameH);
            nrt.anchoredPosition = new Vector2(0f, PfNameY);
            _pfName.text = string.IsNullOrEmpty(PlayerName) ? DefaultPlayerName : PlayerName;
            _pfName.fontStyle = FontStyle.Bold;
            _pfName.raycastTarget = false;
            if (sw != null) sw.Mark("名字");

      // ③ 登录按钮：圆角底 + 半透明深色（深底 + 主题色细描边 = 一眼看得出是个按钮）
      // 不能用 `Outline`：它是把整块图形按 effectColor 复制 4 份、各偏 2px，
      // 对实心矩形来说那 4 份几乎完全重叠（而且用的是主题色 alpha 0.9），等于在按钮底下铺一层 0.9 的主题色板，
      // 看着就是一块不透明的实心按钮——跟"描边"完全不是一个东西。
            var login = MakeButton(content.transform, "Login", Vector2.zero, new Vector2(260f, 68f), new Color(0f, 0f, 0f, 0.45f));
            var loginImg = login.GetComponent<Image>();
            if (loginImg != null) {
                loginImg.sprite = RoundedSprite;             // 圆角底（跟全站按钮/遮罩一个形状）
                loginImg.type = Image.Type.Sliced;           // 九宫格：拉大时圆角不变形
            }
            _pfLoginRt = login.GetComponent<RectTransform>();
            _pfLoginRt.anchorMin = _pfLoginRt.anchorMax = new Vector2(0.5f, 0.5f);
            _pfLoginRt.pivot = new Vector2(0.5f, 0.5f);
            _pfLoginRt.anchoredPosition = new Vector2(0f, PfLoginY);
            _pfLoginGroup = login.gameObject.AddComponent<CanvasGroup>();
      // 主题色细描边做成子物体（这样按钮渐隐时它跟着一起淡）+ 外扩 2px（跟 `MakeMaskFrame` 同一套）
            var rim = MakeImage(login.transform, "Rim", Accent);
            var rimSpr = EnsureMaskFrameSprite();
            if (rimSpr != null) { rim.sprite = rimSpr; rim.type = Image.Type.Sliced; }
            rim.raycastTarget = false;                       // 吃点击的仍是按钮本体
            RegisterThemeImage(rim);                         // 主题色一变，这圈边跟着重画
            Stretch(rim.rectTransform, new Vector2(-2f, -2f), new Vector2(2f, 2f));
            var lt = MakeText(login.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            Stretch(lt.rectTransform, Vector2.zero, Vector2.one);
            lt.text = Lang.Tr("登录");
            lt.raycastTarget = false;
            if (sw != null) sw.Mark("登录按钮");
      // 点下去这一下也装秒表（怀疑是"欢迎回来…"那行 46 号 Bold 的首次字形生成：动态字体图集按字号重建）
            login.onClick.AddListener(() => {
                var gcLoginA = TakeGc();
                var swLogin = System.Diagnostics.Stopwatch.StartNew();
                ProfileDoLogin();
                var gcLoginB = TakeGc();
                Lg.Info($"[LevelLib] 【卡点】点登录处理耗时 = {swLogin.Elapsed.TotalMilliseconds:F1} ms"
                    + $" ｜ {GcDeltaText(gcLoginA, gcLoginB)} ｜ {FrameContextText()}");
            });

      // 「欢迎回来，身份，用户名」——先透明，点完登录才渐显；落在登录按钮的位置上（按钮渐隐、原地出现这句话）
            _pfWelcome = MakeTitle(content.transform, "Welcome", 46, new Color(AccentBase.r, AccentBase.g, AccentBase.b, 1f), TextAnchor.MiddleCenter);
            _pfWelcome.fontStyle = FontStyle.Bold;
      // 跟登录按钮同一个锚点（屏幕中心）+ 同一个 PfLoginY
            var wrt = _pfWelcome.rectTransform;
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(PfWelcomeW, PfWelcomeH);
            wrt.anchoredPosition = new Vector2(0f, PfLoginY);
            _pfWelcome.horizontalOverflow = HorizontalWrapMode.Overflow;
            _pfWelcome.text = "";
            var wc = _pfWelcome.color; _pfWelcome.color = new Color(wc.r, wc.g, wc.b, 0f);
            _pfWelcome.raycastTarget = false;
            if (sw != null) sw.Mark("欢迎语");

      // 悬停提示：鼠标停在登录按钮上才浮出来的一行小字（目标透明度 PfHintAlpha，刻意压得很淡）。
      // 平时 alpha=0、不吃射线 —— 按键监听不在这儿（在 Tick 里），这行纯装饰。
            var hint = MakeText(content.transform, "LoginHint", 20, Color.white, TextAnchor.MiddleCenter);
            hint.text = Lang.Tr("空格 / 回车也可登录");
            hint.raycastTarget = false;
            var hrt = hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(420f, 30f);
            hrt.anchoredPosition = new Vector2(0f, PfLoginY - 64f);   // 按钮底边（-144）下面隔一点
            _pfLoginHint = hint.gameObject.AddComponent<CanvasGroup>();
            _pfLoginHint.alpha = 0f; _pfLoginHint.blocksRaycasts = false;

            _pfSigned = false; _pfPhase = -2; _pfT = 0f;   // -2 = 正在渐入，之后才轮到"等点登录"(0)
            _pfHandedOff = false;                            // 每次新建个人页都允许交接一次
            Lg.Info($"[LevelLib] 个人页（示例3）：名字={_pfName.text} 头像={(avatar.sprite != null ? "有图（自定义或包里的默认）" : "占位圆")}");
      // 建完立刻收进预热箱并藏起来。藏起来用 `SetActive(false)`（画布整个不激活）：
      // 找画布时默认不含未激活对象，所以藏着的个人页不会被开始界面那道"上面有没有更高的层"的闸门误判成"压着它"。
            _pfRefs = new ProfileRefs {
                Go = _pfGo, Group = _pfGroup, ContentGroup = _pfContentGroup, LoginGroup = _pfLoginGroup,
                Name = _pfName, Welcome = _pfWelcome, LoginRt = _pfLoginRt, Avatar = avatar, Hint = _pfLoginHint
            };
            try { _pfGo.SetActive(false); } catch { }
            _pfGo = null; _pfGroup = null; _pfDarkVeil = null; _pfName = null; _pfWelcome = null; _pfLoginRt = null; _pfLoginHint = null;
            _pfLoginGroup = null; _pfContentGroup = null;
            Lg.Info("个人页已建好并收起（预热：上滑那一下直接显示 不再现建）");
        } catch (System.Exception e) {
            Lg.Ex("个人页构建失败（直接进库）", e);
            FinishProfileScreen();
        }
    }

  /// <summary>头像用的"圆"：圆角 sprite 半径 = 贴图一半 → 视觉上就是圆（缓存一次）</summary>
    private static Sprite _circleSprite;
    private static Sprite EnsureCircleSprite() {
        if (_circleSprite != null) return _circleSprite;
        try { _circleSprite = CreateRoundedSprite(32, 16f); } catch { _circleSprite = RoundedSprite; }
        return _circleSprite;
    }

  /// <summary>点「登录」：本地资料确认，按钮渐隐 → 变「欢迎回来，身份，用户名」（身份没有就不显示）。</summary>
    private static void ProfileDoLogin() {
        try {
            if (_pfSigned || _pfGo == null) return;
      // 拖动"下滑回开始界面"期间给登录按钮留的忽略窗口：uGUI 是"按下 + 在原物体上松开 = 点击"，
      // 从按钮上往下拖、松手时手指还在按钮上会误触发一次登录；普通"点一下"根本不进拖动，不受影响。
            if (Time.unscaledTime < _pfBackIgnoreLoginUntil) {
                Lg.Info("个人页：这次点击落在「下滑返回」手势里 ->  忽略（不当成登录）");
                return;
            }
            _pfSigned = true;
      // 登录 = 本地昵称，不联网（这里没有任何网络请求）。
      // 欢迎那行：身份没有就不显示 只写「欢迎回来，用户名」。
            string uname = string.IsNullOrEmpty(PlayerName) ? DefaultPlayerName : PlayerName;
      // 名字叫 Ziyu3 的人点登录 → 解锁「Ziyu3 BEST bug hunter」并把身份强制写成它（大小写不敏感 + 去首尾空格）。
      // 必须排在下面拼欢迎语之前，这样那行就会是「欢迎回来，BEST bug hunter，Ziyu3」，当场看得见效果。
            try {
                string who = (PlayerName ?? "").Trim();
                if (string.Equals(who, "Ziyu3", System.StringComparison.OrdinalIgnoreCase)) {
                    bool first = Achievements.TryUnlock(Achievements.AchZiyu3);
                    Achievements.ApplyForcedTitle();
                    Lg.Info($"[LevelLib] 登录玩家名 = Ziyu3 → {(first ? "解锁「Ziyu3 BEST bug hunter」" : "这个成就早已解锁")}"
                        + $" 身份={PortalUI.PlayerTitle}");
                }
            } catch (System.Exception ez) { Lg.Ex("Ziyu3 成就触发失败", ez); }
            string title = (PlayerTitle ?? "").Trim();
            _pfWelcome.text = title.Length > 0 ? (Lang.Tr("欢迎回来，") + title + "，" + uname) : (Lang.Tr("欢迎回来，") + uname);
            _pfPhase = 1; _pfT = 0f;
      // 交接放在这儿（欢迎那一阶段的最开始），不是等渐隐才开始：
      // 此刻个人页还完全不透明，大厅正好在我们背后把 1 秒渐入走完，等个人页开始渐隐时它早就全亮，
      // 中间不会露出面板背景。放晚了（渐隐那一下才交接）就会出现"个人页淡完、大厅才亮四成"的
      // 那半秒 = 玩家看到的"一瞬间才显示主界面"（`HandOffToNextScreen` 里那句兜底保留，它是幂等的）。
            HandOffToNextScreen();
      // 欢迎那行一个字一个字显示：记全文，逐字揭示（见 TickProfileScreen）
            _pfWelcomeFull = _pfWelcome.text;
            _pfShownChars = 0;
            _pfWelcome.text = "";
            Lg.Info($"[LevelLib] 个人页：登录（本地）→ 「{_pfWelcomeFull}」（逐字显示）");
            if (_pfLoginRt != null && _pfLoginGroup != null) _pfLoginGroup.alpha = 1f;
        } catch (System.Exception e) { Lg.Ex("个人页登录失败", e); }
    }

    private static void TickProfileScreen() {
        if (_pfGo == null) return;
    // 磨砂需求跟着"这一页在不在演"走：面板关着的时候链子本来会停转，这块框就没糊图了
    // （跟过渡层那份需求同一套，见 PortalUI.PanelGlass 的 _glassProfileWant）。
        SetProfileGlassWant(_pfGo.activeInHierarchy);
    // 这一屏现在也挂视频层（登录界面要显示视频背景）：启动预热那次可能还挂不上（视频那时还没就绪），
    // 显示时再补挂几次；糊图源跟着"视频层真显形了没有"走 —— 视频还没出来时裁视频纹理就是另一幅画面。
        if (_pfGo.activeInHierarchy && _pfVideoLayer == null && _pfVideoTries < 3) {
            _pfVideoTries++;
            _pfVideoLayer = AttachVideoBg(_pfNetGroup);
        }
// 这一屏是常驻的、建得很早，所以不能沿用"让位到底"那一档：不写这一句它会一直停在让位状态，
    // 视频还没就绪时看着像整屏没有背景（原注释记的就是这件事）。
    // 但也不能无条件显示 —— 视频就绪后视频层要接管，每帧按回"显示"就和
    // TickVideoBg 里"视频接管时收起代码背景"的那句对着干：一帧收一帧放 = 肉眼看到神经图闪一下
    //（实测日志：「alpha=0.00 代码背景=1.00」与「alpha=1.00 代码背景=1.00」交替出现）。
    // 这里不再自己判，一律问 CodeBgShouldYield（见 PortalUI.VideoBg 里它的注释：
    // 让位这件事原来有两个写入者，任何一方自己判断都会打架 —— 这就是闪的根因）。
    // 口径只有一条：这一层的视频这一帧显形了就让位，没显形就显示（它是这一屏唯一的背景）。
   bool pfVideoShowing = CodeBgShouldYield(_pfVideoLayer);
        if (_pfNetGroup != null && !pfVideoShowing) NetBgSetShown(_pfNetGroup, true, false);
    // 糊图源不再由这一屏的旗子指定：它是全局一张图，判据只有一条"屏幕上此刻显示的是哪一层"
    // （见 BgBlurSource）。以前这里按"我这屏的视频没显形"强制全局走代码背景，
    // 面板开着视频时磨砂就糊着神经图压在视频上（用户报的就是这个）。
    // 整屏模糊遮罩：纹理来自糊图链（链由 _glassProfileWant 转着），最糊那一档一变就换。
    // 拿不到纹理（链没建起来）就整层关掉，宁可没有遮罩也不能留一块白的。
        if (_pfBlurVeil != null) {
            var bt = BgBlurDeepestTexture();
            if (bt == null) { if (_pfBlurVeil.enabled) _pfBlurVeil.enabled = false; }
            else {
                if (!_pfBlurVeil.enabled) _pfBlurVeil.enabled = true;
                if (_pfBlurVeil.texture != bt) _pfBlurVeil.texture = bt;
            }
      // 点名（最多三条）：先分清是"遮罩没拿到糊图"还是"糊的源本身就是暗的"
      // （视频没显形时糊的是代码背景，那本来就是黑的）。
            if (_pfVeilLogs < 3 && _pfGo.activeInHierarchy) {
                _pfVeilLogs++;
                Lg.Info("个人页模糊遮罩：层开=" + _pfBlurVeil.enabled
                    + " 纹理=" + (bt != null ? "有" : "无")
                    + " 视频层alpha=" + (_pfVideoLayer != null ? _pfVideoLayer.color.a.ToString("F2") : "没这层")
                    + " 糊的源=" + (BgBlurSourceIsVideo() ? "视频" : "代码背景")
                    + " 档数=" + BgBlurDepthNow()
                    + " ｜ 代码背景组=" + (_pfNetGroup != null ? (_pfNetGroup.gameObject.activeSelf ? "开" : "关") + "/" + _pfNetGroup.alpha.ToString("F2") : "没这组")
                    + " 压暗罩=" + (_pfDarkVeil != null ? _pfDarkVeil.color.a.ToString("F2") : "没这层"));
            }
        }
        try {
      // 分段标记：这一整个 Tick 会被记到"个人页"头上，而它自己只做 alpha + Substring 这点事，标记用来定位是哪一小步
            float dt = Mathf.Min(Time.unscaledDeltaTime, UiFadeMaxStep);
      // 进主界面的背景回缩：登录交接（相位 2 起点）置起，整页渐隐的同时背景平滑缩回原大 ——
      // 渐隐盖着缩放，玩家看到的是"背景缓缓回到原大、页面淡去进主界面"
            if (_pfZoomRestore && _pfZoom > 1.0001f) {
                float kz = 1f - Mathf.Exp(-PfZoomFollow * dt);
                SsApplyProfileZoom(SsApproach(_pfZoom, 1f, kz), "进主界面回缩");
            }
      // "从上往下滑回开始界面"那个手势必须排在相位判断之前：它接的正是"等点登录"这一档，
      // 而后面的相位分支里 `_pfPhase == 0` 是直接 return 的，放到后面就永远跑不到。
            TickProfileDragBack(dt);
      // 空格 / 回车 = 点「登录」：只在"等点登录"（0）这一档接，回程动画里不接。
      // 开始界面的同款按键（进登录界面）在它自己相位 0 里消化，落到这儿时开始界面已经交棒，两边撞不上。
            if (_pfPhase == 0 && !_pfSigned && _pfBackPhase == 0
                && (UiInput.KeyDown(KeyCode.Space) || UiInput.KeyDown(KeyCode.Return) || UiInput.KeyDown(KeyCode.KeypadEnter))) {
                Lg.Info("个人页：空格/回车 ->  视作点「登录」");
                ProfileDoLogin();
            }
      // 悬停提示：鼠标停在登录按钮上才往 PfHintAlpha 浮，离开/已登录/回程里淡回 0（指数平滑）。
      // 悬停判定用矩形包含（这套 UGUI 的 EventSystem 没挂 pointerCurrentRaycast 可查）：画布是 Overlay，
      // 相机传 null，ScreenPointToLocalPointInRectangle 自己会把画布 scale 因子算进去。
            if (_pfLoginHint != null) {
                float target = 0f;
                if (_pfPhase == 0 && !_pfSigned && _pfBackPhase == 0 && _pfLoginRt != null) {
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _pfLoginRt, (Vector2)UiInput.MousePosition, null, out var lp)) {
                        var rct = _pfLoginRt.rect;
                        if (lp.x >= rct.xMin && lp.x <= rct.xMax && lp.y >= rct.yMin && lp.y <= rct.yMax)
                            target = PfHintAlpha;
                    }
                }
                _pfHintA += (target - _pfHintA) * (1f - Mathf.Exp(-9f * dt));
                _pfLoginHint.alpha = _pfHintA;
            }
            if (_pfPhase == -3) {
        // 开始界面已经不在了（被场景加载销毁 / 别处收掉了） 这一页没人喂透明度了，
        // 绝不能把玩家留在一张"内容全透明"的空背景上
                if (_ssGo == null) ProfileScrubAbort();
                return;
            }
            if (_pfPhase == -2) {                 // 渐入：smoothstep，越看越亮
                _pfT += dt;
                float ki = Mathf.Clamp01(_pfT / PfInSeconds);
        // 淡的是内容层，不是整页（整页淡会在封面销毁那一瞬间露底）
                if (_pfContentGroup != null) _pfContentGroup.alpha = ki * ki * (3f - 2f * ki);
                if (ki >= 1f) {
                    if (_pfContentGroup != null) { _pfContentGroup.alpha = 1f; _pfContentGroup.blocksRaycasts = true; }
                    if (_pfGroup != null) { _pfGroup.alpha = 1f; _pfGroup.blocksRaycasts = true; }
                    DropStartScreenLayer();      // ← 个人页铺满了 → 这时才销毁开始界面那层
                    _pfPhase = 0; _pfT = 0f;
                }
                TickPhaseMark("  ·个人页-渐入/收封面");
                return;
            }
            if (_pfPhase == 0) return;
            _pfT += dt;
            if (_pfPhase == 1) {
        // 按钮渐隐 + 欢迎那行同时渐显（两件事一起做，视觉上就是"按钮变成了那行字"）
                float k = Mathf.Clamp01(_pfT / PfFadeSeconds);
                if (_pfLoginGroup != null) _pfLoginGroup.alpha = 1f - k;
                if (_pfWelcome != null) { var c = _pfWelcome.color; _pfWelcome.color = new Color(c.r, c.g, c.b, k); }
                TickPhaseMark("  ·个人页-按钮淡出");
        // 一个字一个字显示：按时间揭示，每个字 PfCharSeconds
                int total = string.IsNullOrEmpty(_pfWelcomeFull) ? 0 : _pfWelcomeFull.Length;
                if (_pfWelcome != null && total > 0) {
                    int want = Mathf.Clamp(Mathf.FloorToInt((_pfT - PfFadeSeconds * 0.5f) / PfCharSeconds) + 1, 0, total);
                    if (want != _pfShownChars) { _pfShownChars = want; _pfWelcome.text = _pfWelcomeFull.Substring(0, want); }
                }
                TickPhaseMark("  ·个人页-打字机");
        // 先算"最后一个字什么时候出现"，打完再停 PfHoldSeconds（写死固定时长的话，
        // 那一行超过 17 个字符就永远打不完 = 身份被截断）；上限 PfTypeMaxWaitSeconds，短句下限仍是原来的 1.4s。
                float typeDone = Mathf.Min(PfFadeSeconds * 0.5f + Mathf.Max(0, total - 1) * PfCharSeconds,
                                           PfTypeMaxWaitSeconds);
                float keep = Mathf.Max(PfFadeSeconds + PfHoldSeconds, typeDone + PfHoldSeconds);
                if (k >= 1f && _pfT >= keep) {
                    _pfPhase = 2; _pfT = 0f;
                    _pfZoomRestore = true;   // 字体播完、开始进主界面：背景从这里开始平滑缩回原大
                    HandOffToNextScreen();   // 兜底：正常路径在登录那一刻就交接过了（这里靠 _pfHandedOff 幂等）
                }
                return;
            }
            if (_pfPhase == 2) {
                float k = Mathf.Clamp01(_pfT / PfFadeSeconds);
                if (_pfGroup != null) _pfGroup.alpha = 1f - k;
                if (k >= 1f) _pfPhase = 3;
                return;
            }
            if (_pfPhase == 3) FinishProfileScreen();
        } catch (System.Exception e) {
            Lg.Ex("个人页 tick 异常（直接收尾）", e);
            FinishProfileScreen();
        }
    }

  /// <summary>把后面那一屏准备好（登录那一刻就调，个人页还完全不透明的时候）。
  /// 时机是关键：个人页的欢迎阶段有一秒多，大厅正好在我们背后把渐入走完；等个人页开始渐隐时它已经全亮，
  /// 全程不露背景。放晚了（等到渐隐那一刻）个人页淡完大厅才亮四成，玩家看到的就是"一瞬间才显示主界面"。
  /// 只交接一次（`_pfHandedOff`）：交接里会把渐入计时归零，重复交接 = 大厅又被拉回 0 = 又闪一次。
  /// 与"个人页渐变"同一个道理：先摆状态、再做可见动作，顺序不能反。</summary>
    private static void HandOffToNextScreen() {
        if (_pfHandedOff) return;
        _pfHandedOff = true;
        try {
      // 只有"库内首屏"这条路才切主界面；屏保那条路原地不动（回到原来的 tab/选中）
            if (_pfFromLib) {
                if (!_mainScreenEnteredThisSession) _mainScreenEnteredThisSession = true;
                if (_panelOpen) ShowMainScreenInstant();   // 瞬移页面带 + 武装主界面渐入
            }
        } catch { }
    // 列表 / 字母条 / 顶部 tab / 主页棋盘 一起淡回（复用旧启动页那套，别自己发明）
        try { _splashFading = true; _splashFadeT = 0f; } catch { }
    }

    private static bool _pfHandedOff;   // 这次登录是否已经交接过后一屏

    private static void FinishProfileScreen() {
        _pfPhase = -1;
    // 不 Destroy，留着下次直接显示（销毁 = 下次上滑还得整页重建一遍 = 又卡一次）
        try { if (_pfGo != null) _pfGo.SetActive(false); } catch { }
        _pfGo = null; _pfGroup = null; _pfName = null; _pfWelcome = null; _pfLoginRt = null; _pfLoginGroup = null; _pfContentGroup = null; _pfLoginHint = null;
        _pfWelcomeFull = ""; _pfShownChars = 0;   // 打字机状态清掉，下次登录重新逐字
        _pfZoomRestore = false;                   // 缩放状态收摊（页面已隐藏，下一次显示时在摆出处归 1）
        DropStartScreenLayer();                   // 兜底：开始界面那层不该再留着
    // "切主界面 + 淡回"已经在个人页开始渐隐那一帧做过了（见 `HandOffToNextScreen`），这里只是异常路径的幂等兜底
        if (!_pfHandedOff) HandOffToNextScreen();
        Lg.Info("个人页：已渐隐收起（后一屏已在渐隐开始时交接 整页留着不销毁 下次直接显示）");
    }
}
