using UnityEngine;

namespace LevelLibrary;

/// <summary>Updater 的一部分（同一个 partial 类）：开始界面那条更新闸门。
/// 进游戏后第一次弹出开始界面时，那一屏要把本局的更新检查跑完才放人 —— 检查到第几条线路、下载到几成、
/// 装到几成都显示在那一屏上；判定全部读上面那几个 Phase / Progress 字段，闸门自己只负责"发起第一次检查"与"什么时候放行"。
/// 放行不等于取消更新：网络错误或超时只是把人放进去，更新本身照旧在后台跑完。</summary>
public static partial class Updater {

    /// <summary>这一屏要不要显示更新提示（闸门开着，或者刚出结论、那行字还在展示）</summary>
    public static bool GateVisible { get { return _gateArmed && (!_gateSettled || _gateHoldT > 0f); } }

    /// <summary>这一轮还没跑完：开始界面先别让人进去（上滑与空格回车都会被挡下）</summary>
    public static bool GateBlocked { get { return _gateArmed && !_gateSettled; } }

    /// <summary>本局装上了更新、这一屏正挂着「请重启游戏」：开始界面那行「上滑进入」这时候一个像素都不该留
    ///（点了也进不去，留着只会让人以为还能进）</summary>
    public static bool GateMustRestart { get { return _gateMustRestart; } }

    /// <summary>提示那一行（只在内容真的变了才重建，见 GateRefreshText）</summary>
    public static string GateText { get { return _gateText; } }

    /// <summary>要不要画进度条（只有"字节真的在动"才画）</summary>
    public static bool GateBarOn { get { return _gateBar >= 0f; } }

    /// <summary>步进显示一共几格：与检查更新那几条线路一一对应（"检查更新中（N/10）"里的 10）</summary>
    public const int GateStepCount = 10;

    /// <summary>步进显示用的"走到第几格 + 这一格里走了多少"。
    /// 检查阶段按"试过几条线路"（一共 GateStepCount 条）、下载与安装阶段按字节进度折算。
    /// 返回 false = 这一刻没有步进语义（那条轨道不该画）。</summary>
    public static bool GateStepProgress(out int done, out float fill) {
        done = 0; fill = 0f;
        try {
            // 闸门已经武装、但还没轮到我们发检查：开局那次网络预热请求占着请求槽，这一段没有 Phase 语义。
            // 也必须给出一条在走的轨道 —— 否则这段时间门挡着人、那行字还空着、轨道也不画，
            // 玩家面对的是一屏什么提示都没有的挡板，只能干等（实机就是这么反馈的）。
            // 按"门已开多久 / 一格期望耗时"充能，最多到第一格满，不越过下一格。
            if (_gateArmed && !_gateSettled && !_gateStarted) {
                done = 0;
                fill = Mathf.Clamp01(_gateT / Mathf.Clamp(_gateStepAvg, 0.8f, GateStepSeconds));
                return true;
            }
            if (Phase == 1) {
                // 一格 = 一条线路：格内充能按"这一次尝试的超时"走（20 秒走满）
                int line = Mathf.Clamp(_keyIndex, 0, GateStepCount - 2);
                done = line;
                float denom = Mathf.Clamp(_gateStepAvg, 0.8f, GateStepSeconds);
                fill = Mathf.Clamp01(_gateStepT / denom);
                return true;
            }
            if (Phase == 4) {
                // 已经装完了：十格全亮，看着是"走到底了"
                done = GateStepCount - 1;
                fill = 1f;
                return true;
            }
            if (_installing || Phase == 2 || Phase == 3) {
                // 接着检查已经走过的那几格往下分：每一格都会真的走满，走满才轮到下一格，
                // 而且整数部分只会往前（进度条不倒退）
                int from = Mathf.Clamp(_keyIndex, 0, GateStepCount - 1);
                float p = from + Mathf.Clamp01(Progress) * (GateStepCount - from);
                int idx = Mathf.Clamp((int)p, 0, GateStepCount - 1);
                done = Mathf.Max(idx, from);
                fill = done >= GateStepCount - 1 && Mathf.Clamp01(Progress) >= 0.999f
                     ? 1f : Mathf.Clamp01(p - done);
                return true;
            }
        } catch { }
        return false;
    }

    /// <summary>进度条填充比例 0~1</summary>
    public static float GateProgress { get { return _gateBar > 0f ? _gateBar : 0f; } }

    // ================= 参数 =================

    /// <summary>「没有任何字节在动」最多容忍多少秒。下载与安装正常都是几秒到几十秒的事，
    /// 一直没动静就是网络有问题；到点按网络错误收场并放行（更新不会被掐，只是不再拦着玩家）。</summary>
    private const float GateStallSeconds = 60f;
    /// <summary>检查阶段的"没有动静"容忍时长，比下载阶段短得多：这一阶段本来就没有字节，只有一次版本请求，
    /// 平台连不上时十条线路 × 每条三次重试能把玩家扣很久（实测一直在"检查更新中（N/10）"）。
    /// 到点按网络错误放人，更新照旧在后台跑完 —— 连上之后该提示还会提示。</summary>
    private const float GateCheckStallSeconds = 8f;
    /// <summary>整轮的上限，兜住"某个阶段卡住又不算停滞"的极端情况</summary>
    private const float GateMaxSeconds = 600f;
    /// <summary>装完了那行字再留多久（正常收摊那一路用；装上了更新走"等重启"那条，不适用）</summary>
    /// <summary>网络错误那行字再留多久</summary>
    private const float GateHoldErrorSeconds = 4f;
    /// <summary>进度条要动到这个比例以上才算"字节在动"：取直链那一段的 0 不是停滞</summary>
    private const float GateMovingEpsilon = 0.005f;

    /// <summary>调试开关的文件名（放在 mod 目录下；打包时本来就被排除在外）</summary>
    private const string GateDebugFileName = "debug_updgate.txt";
    /// <summary>调试开关默认多按几秒（文件里写数字可以改）</summary>
    private const float GateDebugDefaultSeconds = 8f;
    /// <summary>调试开关多久查一次盘（不每帧碰磁盘）</summary>
    private const float GateDebugCacheSeconds = 3f;
    /// <summary>充能的分母上限：一条线路最久可能等到请求超时。
    /// 实际用的是"实测每次尝试大概多久"（见 GateStepAvgSeconds），这个值只当兜底上限 ——
    /// 拿超时当分母会让每条线路都只跑到几分之一就被换掉（实测 4 秒换线 , 只跑到五分之一）。</summary>
    private const float GateStepSeconds = 20f;
    /// <summary>实测"一次线路尝试大概多久"的初值（秒）：第一次先按这个估，之后按实测滑动平均</summary>
    private const float GateStepAvgInit = 3.5f;
    /// <summary>检查阶段每一格对应一条线路（界面上那句"检查更新中（N/10）"就是同一个 N）：
    /// 试第 N 条线路时第 N 格变成进度条，跑满（拿到结果或超时）才轮到下一条、下一格。</summary>

    // ================= 状态 =================

    /// <summary>这一屏要不要等更新。本局只武装一次（开始界面第一次弹出来那一下）</summary>
    private static bool _gateArmed;
    /// <summary>已经有结论、放行</summary>
    private static bool _gateSettled;
    /// <summary>本局真的装上了更新：这一屏不再放行，一直挂着「请重启游戏」等玩家重启。
    /// 新 DLL 是开机时加载的、素材也是开机读的，本局里装上的东西不重启根本不算生效 ——
    /// 放进去只会让玩家以为更新没成功。重启之后本地就是新版本，正常检查一遍、没东西可装自然放行。</summary>
    private static bool _gateMustRestart;
    /// <summary>已经确认"有一轮检查在跑"（自己有请求在飞，或者开局那条自动检查正在跑）</summary>
    private static bool _gateStarted;
    /// <summary>已经尝试发过一次检查：发不出去就收场，不许每帧重试</summary>
    private static bool _gateTried;
    /// <summary>从武装起的总时长</summary>
    private static float _gateT;
    /// <summary>当前这一格已经走了多久（换线路就归零），给进度条充能用</summary>
    private static float _gateStepT;
    /// <summary>实测的"一次线路尝试大概多久"（滑动平均）：充能的分母用它，进度才会在换线时正好走满</summary>
    private static float _gateStepAvg = GateStepAvgInit;
    private static int _gateStepSeen = -1;
    /// <summary>累计"没有字节在动"的时长</summary>
    private static float _gateStallT;
    /// <summary>结论那行字还要留多久</summary>
    private static float _gateHoldT;
    private static string _gateText = "";
    /// <summary>进度条比例；小于 0 = 这一轮不画进度条</summary>
    private static float _gateBar = -1f;
    /// <summary>上一次拼提示用的状态指纹：指纹没变就一个字符串都不建</summary>
    private static int _gateKey = -1;

    /// <summary>调试开关要求的"结论出来之后再多按几秒"倒计时（见 GateDebugHoldSeconds）</summary>
    private static float _gateDebugT;
    /// <summary>结论已经出来、正等调试倒计时：这两样把它暂存着，倒计时走完才真正生效</summary>
    private static bool _gateVerdictSet, _gateVerdictErr;
    private static string _gateVerdictWhy = "";
    private static int _gateDebugLeft = -1;
    /// <summary>调试开关查盘的时刻与结果（缓存几秒，不每帧碰磁盘）</summary>
    private static float _gateDebugCheckedAt = -1f, _gateDebugHold;

    /// <summary>开机字体自检的结论（只有真的发现问题时才有值）：开始界面那一行在更新提示空着时显示它。</summary>
    public static string GateFontNote { get; private set; } = "";
    private static bool _gateFontChecked;

    /// <summary>开机字体自检：并进开始界面这一屏的流程里（每次进游戏跑一次，见 ArmUpdateGate）。
    /// 字体坏掉是"整页变楷体 / 一个字都不显示"这类致命外观问题，等玩家自己去设置里点太晚；
    /// 能修的当场修掉、报告进日志，只有真的发现问题时才留一句话给屏幕。</summary>
    private static void RunStartupFontCheck() {
        if (_gateFontChecked) return;
        _gateFontChecked = true;
        try {
            string msg = PortalUI.CheckAndFixUiFont();     // 报告由它自己写进日志
            GateFontNote = PortalUI.LastFontCheckOk ? "" : (msg ?? "");
            if (!string.IsNullOrEmpty(GateFontNote))
                Lg.Info("开机字体自检：发现问题 → " + GateFontNote);
        } catch (System.Exception e) {
            Lg.Ex("开机字体自检失败", e);
        }
    }

    /// <summary>开始界面第一次弹出来时调一次：让这一屏等到本局的更新检查出结论再放人。
    /// 每次进游戏只武装一次，所以这就是"每次重启游戏进入时"的那一次检查。
    /// 顺带把开机字体自检并进这一屏（RunStartupFontCheck）。
    /// 下面 `!AutoCheck` 那条分支现在走不到（自动检查已强制开启），留着只是为了这条判据本身可读。</summary>
    public static void ArmUpdateGate() {
        try {
            if (_gateArmed) return;
            _gateArmed = true;
            _gateSettled = false; _gateStarted = false; _gateTried = false; _gateMustRestart = false;
            _gateT = 0f; _gateStallT = 0f; _gateHoldT = 0f;
        _gateStepT = 0f; _gateStepSeen = -1; _gateStepAvg = GateStepAvgInit;
            _gateText = ""; _gateBar = -1f; _gateKey = -1;
            _gateDebugT = 0f; _gateVerdictSet = false; _gateDebugLeft = -1;
            RunStartupFontCheck();   // 开机字体自检并进这一屏（每次进游戏一次）
            if (!AutoCheck) { _gateSettled = true; return; }
            // 开局那条自动检查已经在跑（或者后台正在装）：直接盯着它，不另发一次请求
            if (_installing || Phase == 1 || Phase == 2 || Phase == 3) { _gateStarted = true; return; }
            // 本局早些时候就已经装完了（Phase 被后面的检查改回 0 也算）：这时候进游戏跑的还是旧 DLL，
            // 与"亲眼看着它装完"一个待遇，直接钉在「请重启游戏」上，不放人也不白跑一轮检查
            if (InstalledThisSession) { GateArmRestartHold(); return; }
            // 其余情况由 GateTick 接手去发这一轮的检查（要等开局的网络预热请求先让出请求槽）
            Lg.Info("更新闸门：开始界面已武装（本局这次检查跑完才放行；"
                + "此时 Phase=" + Phase + " 线路=" + (_keyIndex + 1) + "/" + ApiKeys.Length + "）");
        } catch { }
    }

    /// <summary>每帧推进（常驻段，排在 TickRequest 之后：那一帧的请求结果这一步就看得见）。
    /// 没武装时第一行就返回，零开销。</summary>
    public static void GateTick() {
        try {
            if (!_gateArmed) return;
            if (_gateSettled) {
                // 结论那行字到点就收（收完这一屏就只剩常规内容了）
                if (_gateHoldT > 0f) _gateHoldT -= Time.unscaledDeltaTime;
                return;
            }
            float dt = Time.unscaledDeltaTime;
            _gateT += dt;
        // 这一格的计时：换线路（或从检查切到下载）就归零
        int stepNow = Phase == 1 ? _keyIndex : -2;
        if (stepNow != _gateStepSeen) {
            // 换线路：把刚过去这一次的耗时并进滑动平均，再给新的一格从零计时
            if (_gateStepSeen >= 0 && _gateStepT > 0.2f)
                _gateStepAvg += (_gateStepT - _gateStepAvg) * 0.34f;   // 手写插值：验证台那份 Mathf 桩里没有 Lerp
            _gateStepSeen = stepNow;
            _gateStepT = 0f;
        } else {
            _gateStepT += dt;
        }

            // 调试开关：结论已经出来，但先按着不放，好让"提示 + 挡住上滑"这两件事看得清
            if (_gateVerdictSet) {
                _gateDebugT -= dt;
                int left = Mathf.Max(0, Mathf.CeilToInt(_gateDebugT));
                if (left != _gateDebugLeft && left > 0) {
                    _gateDebugLeft = left;
                    _gateText = (string.IsNullOrEmpty(_gateText) ? "" : _gateText + "　")
                              + "（调试保持 " + left + " 秒后放行）";
                }
                if (_gateDebugT > 0f) return;
                GateSettle(_gateVerdictErr, _gateVerdictWhy);
                return;
            }

            // "字节在动" = 后台安装中，或者下载已经真的开始了；取直链那一段不算（它本来就是零）。
            // 先记时再判分支：预热请求一直不回来、检查发不出去这些"卡在起跑线上"的情形也要能被兜住，
            // 否则闸门会永远关着（玩家进不去，日志里还什么都看不出来）
            bool moving = _installing || (Phase == 2 && Progress > GateMovingEpsilon);
            _gateStallT = moving ? 0f : (_gateStallT + dt);

            if (!_gateStarted) {
                if (Busy || _installing) {
                    // 开局的网络预热请求还在飞（或者后台正在装）：等它让出请求槽，别跟它抢
                } else if (Phase == 1 || Phase == 2 || Phase == 3) {
                    _gateStarted = true;      // 开局那条自动检查正在跑，盯着它就行
                } else if (!_gateTried) {
                    _gateTried = true;
                    // 上一轮是"所有线路都不通"收场的：线路下标只前进不后退，不归零的话这一轮
                    // 一发出去就会又判"没有下一条"，等于没重试
                    if (Phase == 5) _keyIndex = 0;
                    CheckNow(false);
                    if (_req != null) {
                        _autoChecked = true;    // 这一轮就是本局那次检查，别让开局延迟那条再发一次
                        _gateStarted = true;
                        Lg.Info("更新闸门：开始界面自己发起了本局的更新检查");
                    }
                } else {
                    GateSettle(false, "检查请求发不出去");
                    return;
                }
            }

            // 这一句放在"检查有没有发出去"前面：等待发检查那一段同样要显示提示与轨道，
            // 不然那段时间门挡着人、一句话都没有
            GateRefreshText();
            if (_gateStarted) {
                // 三种收摊姿态：0 = 平台没有要更新的东西、4 = 装完了、5 = 失败。
                // 还必须没有请求在飞、也没有后台安装 —— 失败之后紧接着重试的那几帧 Phase 也是 5
                if ((Phase == 0 || Phase == 4 || Phase == 5) && !Busy && !_installing) {
                    if (Phase == 4) { GateArmRestartHold(); return; }   // 装上了：不放行，等重启
                    if (_gateMustRestart) return;                       // 本局装过东西了：后面那两种收摊也不放行
                    if (Phase == 5) GateSettle(true, "检查或下载失败");
                    else GateSettle(false, "平台没有需要更新的东西");
                    return;
                }
            }

            // 装完之后只剩"等玩家重启"这一件事：停滞与整轮上限都不许再把闸门打开
            // （放行只能来自"重开一局、本地已经是新版本"）
            if (_gateMustRestart) { GateRefreshText(); return; }

            // 检查阶段（含"等开局那次预热请求让位"）用短闸：这些阶段本来就没有字节，
            // 平台连不上时十条线路 × 每条三次重试能把玩家扣很久（一直卡在"检查更新中（N/10）"）。
            // 只有真的在下载 / 安装的那几个阶段才用那条松的。
            bool dataPhase = _installing || Phase == 2 || Phase == 3;
            float stallLimit = dataPhase ? GateStallSeconds : GateCheckStallSeconds;
            if (_gateStallT >= stallLimit) {
                GateSettle(true, Phase == 1 ? "检查阶段一直没有数据（平台连不上）" : "长时间没有任何数据");
                return;
            }
            if (_gateT >= GateMaxSeconds) { GateSettle(false, "超过整轮上限"); return; }
        } catch { }
    }

    /// <summary>本局装上了更新：把这一屏钉在「请重启游戏」上，不再放行。
    /// 只置一次；顺带把进度条收掉（那会儿没有字节在动了）。</summary>
    private static void GateArmRestartHold() {
        try {
            if (_gateMustRestart) return;
            _gateMustRestart = true;
            _gateSettled = false;          // 关键：没"出结论"就等于一直挡着
            _gateStarted = true;           // 这一屏不再发检查（从武装那一步直接钉过来时它还是假的）
            _gateStallT = 0f;
            _gateBar = -1f;
            _gateText = Lang.Tr("更新完成，请重启游戏");
            _gateKey = -1;
            Lg.Info("更新闸门：本局装上了更新 ->  不再放行，这一屏挂着"
                + "「请重启游戏」（共等 " + _gateT.ToString("F1") + "s）；重启后本地就是新版本，那时正常放行");
        } catch { }
    }

    /// <summary>把当前状态拼成一行提示与一条进度。值没变就一个字符串都不建
    /// （下载期间百分比每变 1 才重建一次，这一屏每帧都会来读）。</summary>
    private static void GateRefreshText() {
        try {
            // 等重启那行字是钉住的：后来那种"后台自己在跑检查"的相位不许把它盖成「检查更新中（N/10）」
            if (_gateMustRestart) return;
            int line = Mathf.Clamp(_keyIndex, 0, ApiKeys.Length - 1) + 1;
            int pct = Mathf.Clamp((int)(Progress * 100f), 0, 100);
            bool asset = CurrentJobIsAsset;
            // 还没拿到下载直链 = 一个字节都还没下，这一段要单独说一句。说成"正在下载"的话，
            // 玩家只会盯着不动的进度条以为卡死；实测这段能等二十几秒（TLS 握手偶发失败）
            bool fetching = FetchingUrl;
            bool barOn = _installing || (Phase == 2 && Progress > GateMovingEpsilon && !fetching);
            // 把"检查有没有真的发出去"也编进 key：等待发检查那一段（预热请求占着请求槽）与
            // 已经发出去之后可能同为 Phase 0，不区分的话第一句提示会被判成"没变"而不写进去
            int key = Phase * 1000000 + line * 10000 + pct * 10 + (asset ? 1 : 0) + (barOn ? 2 : 0)
                    + (fetching ? 8 : 0)
                    + (_gateStarted ? 4 : 0);
            if (key != _gateKey) {
                _gateKey = key;
                // 这几句要过词条表：整串（含数字）查不到词条，所以连占位符一起写进表里再 TrFmt
                //（数字只当参数，不参与翻译）
                if (Phase == 1) {
                    _gateText = Lang.TrFmt("检查更新中（{0}/{1}）", line, ApiKeys.Length);
                } else if (Phase == 2 && fetching) {
                    _gateText = asset ? Lang.Tr("正在连接更新服务器，还没开始下载")
                                      : Lang.Tr("正在连接更新服务器，还没开始下载");
                } else if (Phase == 2) {
                    _gateText = asset
                        ? (pct > 0 ? Lang.TrFmt("正在下载资源包（{0}%）", pct) : Lang.Tr("正在下载资源包"))
                        : (pct > 0 ? Lang.TrFmt("正在下载 mod 更新（{0}%）", pct) : Lang.Tr("正在下载 mod 更新"));
                } else if (Phase == 3) {
                    _gateText = asset ? Lang.TrFmt("正在安装资源包（{0}%）", pct)
                                      : Lang.TrFmt("正在安装 mod 更新（{0}%）", pct);
                } else if (!_gateStarted) {
                    // 还没轮到我们发检查：这时也要有一句话，不然门挡着人却一个字都没有
                    _gateText = Lang.Tr("正在检查更新");
                }
            }
            _gateBar = barOn ? Mathf.Clamp01(Progress) : -1f;
        } catch { }
    }

    /// <summary>出结论：放行，并按结局留一句提示。
    /// 平台上没有要更新的东西就什么都不留；装上了更新这一条不放行（见 GateArmRestartHold）。</summary>
    private static void GateSettle(bool error, string why) {
        // 装上了更新不许从这条路放行：任何调用方走到这里都一样，直接改道去"等重启"
        // （本局装过就算：Phase 有可能已经被后面那一轮检查改回 0）
        if (!error && (Phase == 4 || InstalledThisSession)) { GateArmRestartHold(); return; }
        // 调试开关：先别落结论，把闸门多按几秒（只延长时间，判定一个字都不改）
        float dbg = GateDebugHoldSeconds();
        if (dbg > 0f && !_gateVerdictSet) {
            _gateVerdictSet = true; _gateVerdictErr = error; _gateVerdictWhy = why;
            _gateDebugT = dbg;
            _gateBar = -1f;
            try {
                Lg.Info("更新闸门调试：结论已出（" + why + "），闸门再按 "
                    + dbg.ToString("F0") + " 秒供观察（mod 目录里的 " + GateDebugFileName + " 删掉就恢复）");
            } catch { }
            return;
        }
        _gateSettled = true;
        _gateStallT = 0f;
        _gateBar = -1f;
        if (error) {
            _gateText = Lang.Tr("网络错误");
            _gateHoldT = GateHoldErrorSeconds;
        } else {
            _gateText = "";
            _gateHoldT = 0f;
        }
        try {
            Lg.Info("更新闸门：放行（" + why + "）；共等 "
                + _gateT.ToString("F1") + "s ｜ 提示="
                + (string.IsNullOrEmpty(_gateText) ? "（不提示）" : _gateText));
        } catch { }
    }

    /// <summary>调试开关要求的额外按住秒数。mod 目录放一个 debug_updgate.txt 就生效，
    /// 文件里写个数字还能改秒数（不写按默认）。结果缓存几秒，不每帧碰磁盘；文件删掉就恢复原样。</summary>
    private static float GateDebugHoldSeconds() {
        try {
            float now = Time.unscaledTime;
            if (_gateDebugCheckedAt >= 0f && now - _gateDebugCheckedAt < GateDebugCacheSeconds)
                return _gateDebugHold;
            _gateDebugCheckedAt = now;
            _gateDebugHold = 0f;
            string p = System.IO.Path.Combine(Main.ModEntry.Path, GateDebugFileName);
            if (!System.IO.File.Exists(p)) return _gateDebugHold;
            _gateDebugHold = GateDebugDefaultSeconds;
            try {
                float want;
                if (float.TryParse(System.IO.File.ReadAllText(p).Trim(), out want) && want > 0f)
                    _gateDebugHold = Mathf.Clamp(want, 1f, 120f);
            } catch { }
        } catch { _gateDebugHold = 0f; }
        return _gateDebugHold;
    }
}
