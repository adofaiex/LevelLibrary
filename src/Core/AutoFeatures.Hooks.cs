// [split-of] AutoFeatures.cs lines 1-706 body 15-798

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LevelLibrary;

/// <summary>AutoFeatures 的设置读写 / 自动录入 / 音乐名播报 / 开局采样 / 通关判定（同一个 partial 类的一部分）。
/// 设置从 settings.txt 逐行按 key 解析，读不到的 key 保持程序里的默认值。</summary>
public static partial class AutoFeatures {
    /// <summary>自动录入：编辑器游玩谱面时，列表里没有就录入</summary>
    public static bool AutoRecord;

    /// <summary>自动标记：通关后按精准度自动打判定标记（需先开自动录入）</summary>
    public static bool AutoMark;

    /// <summary>录入限制：开=自动播放/不会失败(NoFail) 不记录成绩；关=都记录（默认开）</summary>
    public static bool RecordRestrictions = true;

    /// <summary>谱面导入后备份：开=复制进 backup；关=只加入列表、指向原位置。默认关 —— 导入一大批会把磁盘写满，
    /// 而磁盘满了之后别的功能（如换背景的备份）也会跟着坏。老玩家的 settings.txt 里可能已写死 true，
    /// 所以真正的兜底是导入前的剩余空间检查（`PortalUI.BatchImport.cs` 的 `CheckBackupSpace`）。</summary>
    public static bool BackupOnImport;

    /// <summary>「自动导入」那条路自己的备份开关（默认关）。它和 `BackupOnImport` 是两件事：
    /// 后者管导入弹窗里手动导入的那一批，这一条只管打开一个新谱面、它被自动录进库的那一次。
    /// 默认关的理由同 `BackupOnImport`：备份占磁盘，磁盘写满会连带弄坏别的功能（如换背景的备份）。</summary>
    public static bool AutoImportBackup;

    /// <summary>批量导入后删除原谱面文件夹（破坏性，默认关）—— 三条安全网：
    /// ① 勾上它自动强制打开「导入后备份」（不备份还删 = 删掉玩家唯一的副本，红线）；
    /// ② 执行时只有"备份真的成功了"（`backupDir != 原目录`）才删，光信开关不够；
    /// ③ 危险目录一律不删：盘符根 / mod 目录 / 备份根 / 游戏目录 / 系统目录（见 `SafeToDeleteSource`）。</summary>
    public static bool DeleteSourceOnImport;

    // ================= 开机接管进入（设置里"常规"子 tab 启动的那一个开关）=================
    // 原先是两个开关（进关卡编辑器 / 进谱面库，两个可以全选）；按他的要求并成一个
    // 「使用 LevelLibrary 接管进入」。打开时：进关卡编辑器，编辑器就绪后再打开谱面库面板，
    // 同时把 UMM 每次开机弹出来的模组列表窗口压住（TickUmmWindow）。
    /// <summary>打开游戏后由 LevelLibrary 接管进入：自动进关卡编辑器，就绪后再自动打开谱面库面板</summary>
    public static bool LaunchTakeover;         // 开机接管进入（默认开：第一次读到设置时覆盖一次，之后按文件走）
    public static bool LaunchTakeoverOnboard;  // 那次一次性覆盖已经做过了（落盘之后不再覆盖）
    /// <summary>个人信息栏显示 / 大肥鱼全局显示"默认开启"的一次性覆盖已经做过了（落盘之后不再覆盖）</summary>
    public static bool ShowPlayerBoxOnboard;
    /// <summary>视频背景"默认关闭"的一次性调整已经做过了（落盘之后不再覆盖）</summary>
    public static bool VideoBgOffOnboard;

    /// <summary>开机接管的逐帧驱动（Main.OnUpdate 里调用）。启动头一两秒游戏还在初始化，切场景/开面板
    /// 都可能出问题，所以只在第一次进到 scnLevelSelect 之后再等 1.5 秒动手，整个游戏只做一次；
    /// 动手时两步走：① 切编辑器场景 → ② 等编辑器就绪（2 秒）再开面板。</summary>
    private static float _launchWait = -1f;
    private static bool _launchDone;
    private static bool _launchPanelAfterEditor;   // 阶段②：已经进了编辑器，还要不要再开面板
    private static float _launchWait2;
    public static void TickLaunchAction() {
        // ---- 阶段②：已经切到编辑器场景了，等它建好再开面板 ----
        if (_launchPanelAfterEditor) {
            try {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "scnEditor") return;
                _launchWait2 -= Time.unscaledDeltaTime;
                if (_launchWait2 > 0f) return;
                _launchPanelAfterEditor = false;
                Lg.Info("开机接管进入：编辑器已就绪，接下来打开谱面库面板");
                PortalUI.OpenPanelFrom("开机自动进入（编辑器就绪后）");
            } catch (Exception e) {
                _launchPanelAfterEditor = false;
                Lg.Ex("开机接管进入（阶段②）失败", e);
            }
            return;
        }
        if (_launchDone) return;
        if (!LaunchTakeover) { _launchDone = true; return; }
        try {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != "scnLevelSelect") return;   // 还在启动/加载场景，继续等
            if (_launchWait < 0f) {
                _launchWait = 1.5f;
                Lg.Info("开机接管进入：已到关卡选择场景，等 1.5 秒后切编辑器");
                return;
            }
            _launchWait -= Time.unscaledDeltaTime;
            if (_launchWait > 0f) return;
            _launchDone = true;
            Lg.Info("开机接管进入：切到关卡编辑器（scnEditor），就绪后再打开谱面库面板");
            UnityEngine.SceneManagement.SceneManager.LoadScene("scnEditor");
            _launchPanelAfterEditor = true;
            _launchWait2 = 2f;
        } catch (Exception e) {
            _launchDone = true;   // 失败就这一次，别每帧重试刷日志
            Lg.Ex("开机接管进入失败", e);
        }
    }

    // ================= 开机接管：把 UMM 的模组列表窗口收起来 =================
    // UMM 每次进游戏都会弹它的模组列表窗口（开关在它自己的 Params.xml 里），判断发生在
    // UI.Load() 之后的 FirstLaunch()，比我们的 Main.Load 晚；那个窗口又只在 OnGUI 里画，
    // 而 OnUpdate 在 OnGUI 之前，所以在这里看到它开着就收起来，玩家一帧都看不到。
    // 刻意不写 UMM 的配置：关掉接管，UMM 就照旧弹（它的设置页里那个开关我们一个字都不动）。
    private static float _ummWatch = 20f;      // 只盯开机头 20 秒（万一接管动作没走完，也不会一直跟它较劲）
    private static bool _ummClosed;            // 开机那一次已经收掉了
    private static bool _ummCloseLogged;
    public static void TickUmmWindow() {
        if (!LaunchTakeover) return;
        if (_ummClosed) return;                                // 只收开机那一次，之后玩家自己按 RightAlt 打开的不再动
        if (_launchDone && !_launchPanelAfterEditor) return;   // 接管动作已经做完了，不再管它
        if (_ummWatch <= 0f) return;
        _ummWatch -= Time.unscaledDeltaTime;
        try {
            var ui = UnityModManagerNet.UnityModManager.UI.Instance;
            if (ui == null || !ui.Opened) return;
            ui.ToggleWindow(false);
            _ummClosed = true;
            if (!_ummCloseLogged) {
                _ummCloseLogged = true;
                Lg.Info("开机接管进入：UMM 的模组列表窗口已收起（想自己看它，按 RightAlt 还能打开）");
            }
        } catch (Exception e) {
            _ummWatch = 0f;   // 失败就这一次，别每帧刷日志
            Lg.Info("收 UMM 模组列表窗口失败（不影响接管进入）：" + e.Message);
        }
    }

    private static string _settingsPath;
    private static string _lastCheckedPath;   // 上次处理过的谱面路径（同一谱面只处理一次）
    private static string _lastSeenPath;      // 上次轮询读到的路径（变化时打日志）
    private static float _lastWinTime;        // 上次处理过的通关时刻（winTime 去重）
    private static System.Reflection.FieldInfo _winTimeField;   // 缓存反射字段（每帧读不重复查找）
    private static bool _autoplayUsed;         // 本局是否开启过自动播放（RDC.auto；开启过则通关不记录 X-acc）
    private static bool _noFailUsed;           // 本局是否处于"不会失败"模式（scrController.noFail；开了就不记录成绩）

    /// <summary>加载设置（settings.txt）。没有设置文件也要走完 ——
    /// 末尾的默认值处理（默认名字）必须执行，否则全新安装会显示空白名字（v4.19.205 修的）。</summary>
    public static void Load(string modPath) {
        _settingsPath = Path.Combine(modPath, "settings.txt");
        AutoRecord = true;   // 默认开启
        AutoMark = true;
        try {
            if (File.Exists(_settingsPath)) {
                foreach (var line in File.ReadAllLines(_settingsPath)) {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                string k = line.Substring(0, idx).Trim();
                bool v = line.Substring(idx + 1).Trim() == "true";
                if (k == "AutoRecord") AutoRecord = v;
                else if (k == "AutoMark") AutoMark = v;
                else if (k == "RecordRestrictions") RecordRestrictions = v;
                else if (k == "BackupOnImport") BackupOnImport = v;
                else if (k == "AutoImportBackup") AutoImportBackup = v;
                else if (k == "LaunchTakeover") LaunchTakeover = v;
                // 一次性覆盖的标记（只写 true）：老用户更新上来会被强制打开一遍，玩家自己关掉之后就不管了
                else if (k == "LaunchTakeoverOnboard") LaunchTakeoverOnboard = v;
                // 个人信息栏显示 / 大肥鱼全局显示"默认开启"的一次性覆盖标记（同上一套：老用户更新上来强制打开一遍）
                else if (k == "ShowPlayerBoxOnboard") ShowPlayerBoxOnboard = v;
                // 视频背景"默认关闭"的一次性调整标记（同上一套：只写 true）
                else if (k == "VideoBgOffOnboard") VideoBgOffOnboard = v;
                // 老设置：原先那两个开关（进编辑器 / 进谱面库）已经并成一个开关了，
                // 读到任意一个 true 就当成"接管进入"打开；落盘时只写新 key，老行下一次保存就没了。
                else if (k == "LaunchToEditor") LaunchTakeover |= v;
                else if (k == "LaunchToLibrary") LaunchTakeover |= v;
                // 屏保两个开关：SaverInLib=开始界面作为屏保 / SaverGlobal=屏保全局显示
                else if (k == "SaverInLib") PortalUI.SaverInLib = v;
                else if (k == "SaverGlobal") PortalUI.SaverGlobal = v;
                // 视频背景：开关 + 产物路径（路径指的是压缩后的 `assets\bg_video.mp4`）
                else if (k == "VideoBgOn") PortalUI.VideoBgOn = v;
                else if (k == "VideoBgPath") PortalUI.VideoBgPath = line.Substring(idx + 1).Trim();
                // 大厅立绘：自选图路径（空 = 随包默认那张）+ 角度 / X / Y / 大小（1920 设计坐标）
                else if (k == "ArkPortraitPath") PortalUI.PortraitPath = line.Substring(idx + 1).Trim();
                else if (k == "ArkPortraitRot" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float apr)) PortalUI.PortraitRot = Mathf.Repeat(apr, 360f);
                else if (k == "ArkPortraitX" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float apx)) PortalUI.PortraitX = apx;
                else if (k == "ArkPortraitY" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float apy)) PortalUI.PortraitY = apy;
                else if (k == "ArkPortraitSize" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float aps)) PortalUI.PortraitSize = Mathf.Clamp(aps, 120f, 1080f);
                // 视频压缩挡位（导入时玩家自己选的；认不得的标识由 VideoQualityKey 的 setter 回落到默认档）
                else if (k == "VideoQualityKey") PortalUI.VideoQualityKey = line.Substring(idx + 1).Trim();
                // 屏保休眠时间，单位秒（30 / 60 / 300 / 600 四档；非法值由 SaverIdleTarget 回落到 30）
                else if (k == "SaverIdle" && int.TryParse(line.Substring(idx + 1).Trim(), out int sid)) PortalUI.SaverIdleSetting = sid;
                else if (k == "MusicVolume" && int.TryParse(line.Substring(idx + 1).Trim(), out int mv)) PortalUI.MusicVolume = mv;
                else if (k == "BgVeilPercent" && int.TryParse(line.Substring(idx + 1).Trim(), out int bvp)) PortalUI.BgVeilPercent = Mathf.Clamp(bvp, 0, 100);
                else if (k == "ListFontSize" && int.TryParse(line.Substring(idx + 1).Trim(), out int lfs)) PortalUI.ListFontSize = lfs;
                // 谱面列表排序方式（false=字母分组 / true=按标记难度）。
                // 必须在 `LevelScanner.Refresh()` 之前读到（扫描里就会排一次），晚读到第一屏还是字母序。
                else if (k == "SortByDifficulty") PortalUI.SortByDifficulty = v;
                // 成就名称 / 详细信息的字号（外观 → 背景与外观）
                else if (k == "AchNameFontSize" && int.TryParse(line.Substring(idx + 1).Trim(), out int anf)) PortalUI.AchNameFontSize = anf;
                else if (k == "AchDetailFontSize" && int.TryParse(line.Substring(idx + 1).Trim(), out int adf)) PortalUI.AchDetailFontSize = adf;
                else if (k == "UiFontScale" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ufs)) PortalUI.UiFontScale = Mathf.Clamp(ufs, PortalUI.UiFontPercentMin / 100f, PortalUI.UiFontPercentMax / 100f);
                // 面板磨砂固定满值：`PanelBlur=` 与更早的 `PanelTransparency=` 都不再读，
                // 各页遮罩深浅冻在 PanelAlphaFloor 那一档（见 PortalUI.PanelBlur）。
                // 界面状态沿用（上次停在哪 / 选中谁 / 滚到哪）
                else if (k == "BatchImportZips") PortalUI.BatchImportZips = v;
     else if (k == "SingleImportZips") PortalUI.SingleImportZips = v;
                else if (k == "LastTopTab" && int.TryParse(line.Substring(idx + 1).Trim(), out int ltt)) PortalUI.LastTopTab = Mathf.Clamp(ltt, 0, PortalUI.MainScreenSlot);
                // 选中谁不跨局读回：`LastSelectedPath` 是进程内存字段，同一局之内关面板写、开面板读，
                // 重启 = 新进程 = 天然干净。文件里那一行照写，只是启动时不读（以后要跨局沿用改这一行）。
                else if (k == "LastSelectedPath") { /* 故意不读 */ }
                // 滚动位置同样不跨局读回，只在一局之内沿用：重启后列表从最上面开始。
                // 文件里那一行照写，只是启动时不读。
                else if (k == "LastLibScroll") { /* 故意不读 */ }
                // 旧版写过的 CustomBgPath 不再读：静态背景整条功能已删，背景一律由代码绘制
                // 主题色是不是玩家自己在设置里选过（见下面那次迁移）：老文件里没有这一行
                else if (k == "ThemePicked") PortalUI.ThemePicked = v;
                // 主题色 "r,g,b" 三位小数、invariant；解析坏了就保持默认
                else if (k == "ThemeColor") {
                    var tc = line.Substring(idx + 1).Trim().Split(',');
                    if (tc.Length >= 3) {
                        float cr, cg, cb;
                        if (float.TryParse(tc[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cr)
                            && float.TryParse(tc[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cg)
                            && float.TryParse(tc[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cb))
                            PortalUI.ThemeColor = new Color(Mathf.Clamp01(cr), Mathf.Clamp01(cg), Mathf.Clamp01(cb), PortalUI.DefaultThemeColor.a);
                    }
                }
                else if (k == "BgmPath") PortalUI.BgmPath = line.Substring(idx + 1).Trim();
                else if (k == "BgmSource") PortalUI.BgmSource = line.Substring(idx + 1).Trim();
                else if (k == "OpenLevelKey" && System.Enum.TryParse(line.Substring(idx + 1).Trim(), out KeyCode olk)) PortalUI.OpenLevelKey = olk;
                else if (k == "RandomLevelKey" && System.Enum.TryParse(line.Substring(idx + 1).Trim(), out KeyCode rlk)) PortalUI.RandomLevelKey = rlk;
                else if (k == "AvatarPath") PortalUI.AvatarPath = line.Substring(idx + 1).Trim();
                else if (k == "PlayerTitle") PortalUI.PlayerTitle = line.Substring(idx + 1).Trim();   // 玩家身份称号
                else if (k == "PlayerName") PortalUI.PlayerName = line.Substring(idx + 1).Trim();
                else if (k == "PlayerRandomId") PortalUI.PlayerRandomId = line.Substring(idx + 1).Trim();   // 大厅 "ID: xxx" 那行
                else if (k == "ShowPlayerBoxOutside") PortalUI.ShowPlayerBoxOutside = v;
                else if (k == "ShowPlayerBox") PortalUI.ShowPlayerBox = v;
                // 游玩时隐藏（个人信息栏 / 大肥鱼，默认关）
                else if (k == "HidePlayerBoxInGame") PortalUI.HidePlayerBoxInGame = v;
                else if (k == "PetHideInGame") PortalUI.PetHideInGame = v;
                else if (k == "PlayerBoxX" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pbx)) PortalUI.PlayerBoxX = pbx;
                else if (k == "PlayerBoxY" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pby)) PortalUI.PlayerBoxY = pby;
                // ---- 大肥鱼宠物：位置用归一化锚点，跟个人信息栏同一套 ----
                else if (k == "PetShow") PortalUI.PetShow = v;
                else if (k == "PetGlobal") PortalUI.PetGlobal = v;
                else if (k == "PetSnapEdge") PortalUI.PetSnapEdge = v;   // 自动吸附
                // 深度用户：连续用谱面库打开的次数（跨局保留，本来就是累计成就）
                else if (k == "DeepUserStreak" && int.TryParse(line.Substring(idx + 1).Trim(), out int dus))
                    DeepUser.Streak = Mathf.Max(0, dus);
                else if (k == "PetBubbleOn") PortalUI.PetBubbleOn = v;
                else if (k == "PetSoundOn") PortalUI.PetSoundOn = v;
                // 大肥鱼「随机发言」三档（0 无 / 1 每秒 1% / 2 每秒千分之一）
                else if (k == "PetRandomSay" && int.TryParse(line.Substring(idx + 1).Trim(), out int prs))
                    PortalUI.PetRandomSay = prs;
                // 宠物模型：选的是哪一个（下标；导入的模型排在 1 号往后，越界由 ResolvePetModel 夹回大肥鱼）
                else if (k == "PetModel" && int.TryParse(line.Substring(idx + 1).Trim(), out int pmd))
                    PortalUI.PetModel = pmd;
                else if (k == "PetVol" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pv)) PortalUI.PetVol = pv;
                else if (k == "PetScale" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float psc)) PortalUI.PetScale = psc;
                else if (k == "PetX" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float px)) PortalUI.PetX = px;
                else if (k == "PetY" && float.TryParse(line.Substring(idx + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float py)) PortalUI.PetY = py;
                else if (k == "CustomBackupPath") LevelScanner.CustomBackupPath = line.Substring(idx + 1).Trim();
                else if (k == "RankMin" && int.TryParse(line.Substring(idx + 1).Trim(), out int rmn)) { PortalUI.RankMin = rmn; }
                else if (k == "RankMax" && int.TryParse(line.Substring(idx + 1).Trim(), out int rmx)) { PortalUI.RankMax = rmx; }
                else if (k == "NowBarShow") PortalUI.NowBarShow = v;
                else if (k == "NowBarMode" && int.TryParse(line.Substring(idx + 1).Trim(), out int nbm)) PortalUI.NowBarMode = nbm;
                else if (k == "NowBarCustomText") PortalUI.NowBarCustomText = line.Substring(idx + 1).Trim();
                else if (k == "LogVerbose") PortalUI.LogVerbose = v;
                else if (k == "LogOnBeatLevel") LogOnBeatLevel = v;
                // UpdAutoCheck 这个键不再读：自动检查更新是强制的（Updater.AutoCheck 恒为 true），
                // 老 settings.txt 里写着 false 也一律忽略，界面上那个开关已经删掉。
                else if (k == "UpdLastSeen") Updater.LastSeenVersion = line.Substring(idx + 1).Trim();
                else if (k == "Language" && Lang.IsValid(line.Substring(idx + 1).Trim())) Lang.Current = line.Substring(idx + 1).Trim();
                }
            }
        } catch (System.Exception e) {
            // 读取失败不弹窗（启动阶段弹窗会挡住玩家），只留日志
            PortalUI.ReportErrorQuiet("读取设置失败（将使用默认设置）", e);
        }
        // 没设过名字 → 用默认名
        if (string.IsNullOrEmpty(PortalUI.PlayerName)) PortalUI.PlayerName = PortalUI.DefaultPlayerName;
        // 随机 ID：每位玩家只生成一次（文件里没有这一行 / 读到空值），生成后立刻落盘、之后固定不变。
        // 用 System.Random：全 mod 取 Unity 随机数的只有 RandomPick 一处（那条约定管的是抽谱面）。
        if (string.IsNullOrEmpty(PortalUI.PlayerRandomId)) {
            PortalUI.PlayerRandomId = new System.Random().Next(0, 1000000000).ToString("D9");
            Save();
        }
        // 主题色一次性迁移：老文件里那套颜色是从"自定义背景图"提取出来的，而静态背景整条删了，
        // 所以没在设置里亲手选过色的人一律回默认色（选过一次之后 ThemePicked 就落盘，从此按文件走）
        if (!PortalUI.ThemePicked) PortalUI.ThemeColor = PortalUI.DefaultThemeColor;
        // 接管进入的一次性覆盖：这份设置第一次被读到（老用户更新上来 / 全新安装）时强制打开一遍，只此一次。
        // 立刻落盘 `LaunchTakeoverOnboard=true`，从此以文件为准 —— 玩家自己关掉，以后每次更新都不再覆盖。
        if (!LaunchTakeoverOnboard) {
            LaunchTakeover = true;
            LaunchTakeoverOnboard = true;
            Save();
        }
        // 个人信息栏两个开关的一次性覆盖：总开关与「界面外显示」本来就默认开（Fields.cs 里声明为 true），
        // 但老玩家的 settings.txt 里可能存着过去关掉的 false —— 更新上来强制打开一遍，也只此一次。
        // 落盘 `ShowPlayerBoxOnboard=true` 之后以文件为准：玩家自己再关掉，以后每次更新都不覆盖。
        // 打开「界面外显示」= 信息栏在任何界面都显示（面板里那份会自己藏起来，同一块框不会出现两次）。
        if (!ShowPlayerBoxOnboard) {
            PortalUI.ShowPlayerBox = true;
            PortalUI.ShowPlayerBoxOutside = true;
            ShowPlayerBoxOnboard = true;
            Save();
        }
        // 视频背景的一次性调整：默认背景换成静态图之后，视频改成按需开启。
        // 把"没设过自己视频"的玩家统一关掉一次，只此一次 —— 让默认观感回到静态背景；
        // 落盘 `VideoBgOffOnboard=true` 之后以文件为准：玩家自己再打开，以后每次更新都不覆盖。
        // 自己设过视频（路径还在）的人不动：那种情况下的开多半是故意留着。
        if (!VideoBgOffOnboard) {
            if (string.IsNullOrEmpty(PortalUI.VideoBgPath) || !File.Exists(PortalUI.VideoBgPath)) PortalUI.VideoBgOn = false;
            VideoBgOffOnboard = true;
            Save();
        }
        // 启动诊断：记一行名字/头像/语言的最终值，排查"初始名字是空的"只能靠它
        try {
            Lg.Info($"[LevelLib] 启动信息: 设置文件={(!string.IsNullOrEmpty(_settingsPath) && File.Exists(_settingsPath) ? "有" : "无")}"
                + $" 名字=[{PortalUI.PlayerName ?? "(null)"}] 头像=[{(string.IsNullOrEmpty(PortalUI.AvatarPath) ? "(未设置)" : PortalUI.AvatarPath)}]"
                + $" 语言={Lang.Current} 外部显示={PortalUI.ShowPlayerBoxOutside} 总开关={PortalUI.ShowPlayerBox}");
        } catch { }
    }

    /// <summary>保存设置（settings.txt）</summary>
    public static void Save() {
        if (string.IsNullOrEmpty(_settingsPath)) return;
        try {
            File.WriteAllLines(_settingsPath, new[] {
                "AutoRecord=" + (AutoRecord ? "true" : "false"),
                "AutoMark=" + (AutoMark ? "true" : "false"),
                "RecordRestrictions=" + (RecordRestrictions ? "true" : "false"),
                "BackupOnImport=" + (BackupOnImport ? "true" : "false"),
                "AutoImportBackup=" + (AutoImportBackup ? "true" : "false"),
                "LaunchTakeover=" + (LaunchTakeover ? "true" : "false"),
                // 一次性覆盖的标记：只写 true —— 有这一行就代表"已经覆盖过"，跟开关当前的值无关
                "LaunchTakeoverOnboard=true",
                // 个人信息栏两个开关"默认开启"的一次性覆盖标记（同样只写 true，跟开关当前值无关）
                "ShowPlayerBoxOnboard=true",
                // 视频背景"默认关闭"的一次性调整标记（同样只写 true，跟开关当前值无关）
                "VideoBgOffOnboard=true",
                "SaverInLib=" + (PortalUI.SaverInLib ? "true" : "false"),
                "SaverGlobal=" + (PortalUI.SaverGlobal ? "true" : "false"),
                "SaverIdle=" + PortalUI.SaverIdleSetting,
                // 视频背景：开关 + 产物路径
                "VideoBgOn=" + (PortalUI.VideoBgOn ? "true" : "false"),
                "VideoBgPath=" + (string.IsNullOrEmpty(PortalUI.VideoBgPath) ? "" : PortalUI.VideoBgPath),
                // 大厅立绘：自选图路径 + 角度 / X / Y / 大小（空路径 = 随包默认那张）
                "ArkPortraitPath=" + (string.IsNullOrEmpty(PortalUI.PortraitPath) ? "" : PortalUI.PortraitPath),
                "ArkPortraitRot=" + PortalUI.PortraitRot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "ArkPortraitX=" + PortalUI.PortraitX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "ArkPortraitY=" + PortalUI.PortraitY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "ArkPortraitSize=" + PortalUI.PortraitSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // 视频压缩挡位：下次导入视频时对话框默认标它
                "VideoQualityKey=" + PortalUI.VideoQualityKey,
                "MusicVolume=" + PortalUI.MusicVolume,
                "BgVeilPercent=" + PortalUI.BgVeilPercent,
                "ListFontSize=" + PortalUI.ListFontSize,
                "UiFontScale=" + PortalUI.UiFontScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // 谱面列表排序方式（点一下立刻落盘，下次进游戏沿用）
                "SortByDifficulty=" + (PortalUI.SortByDifficulty ? "true" : "false"),
                // 成就字号两个设置
                "AchNameFontSize=" + PortalUI.AchNameFontSize,
                "AchDetailFontSize=" + PortalUI.AchDetailFontSize,
                "BatchImportZips=" + (PortalUI.BatchImportZips ? "true" : "false"),
     "SingleImportZips=" + (PortalUI.SingleImportZips ? "true" : "false"),
                "LastTopTab=" + PortalUI.LastTopTab,
                "LastSelectedPath=" + (string.IsNullOrEmpty(PortalUI.LastSelectedPath) ? "" : PortalUI.LastSelectedPath),
                "LastLibScroll=" + PortalUI.LastLibScroll.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Language=" + Lang.Current,
                "ThemePicked=" + (PortalUI.ThemePicked ? "true" : "false"),
                "ThemeColor=" + PortalUI.ThemeColor.r.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                             + PortalUI.ThemeColor.g.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                             + PortalUI.ThemeColor.b.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "BgmPath=" + (string.IsNullOrEmpty(PortalUI.BgmPath) ? "" : PortalUI.BgmPath),
                "BgmSource=" + (string.IsNullOrEmpty(PortalUI.BgmSource) ? "" : PortalUI.BgmSource),
                "OpenLevelKey=" + PortalUI.OpenLevelKey,
                "RandomLevelKey=" + PortalUI.RandomLevelKey,
                "AvatarPath=" + (string.IsNullOrEmpty(PortalUI.AvatarPath) ? "" : PortalUI.AvatarPath),
                "PlayerName=" + (string.IsNullOrEmpty(PortalUI.PlayerName) ? "" : PortalUI.PlayerName),
                "PlayerRandomId=" + (string.IsNullOrEmpty(PortalUI.PlayerRandomId) ? "" : PortalUI.PlayerRandomId),
                "PlayerTitle=" + (string.IsNullOrEmpty(PortalUI.PlayerTitle) ? "" : PortalUI.PlayerTitle),
                "ShowPlayerBoxOutside=" + (PortalUI.ShowPlayerBoxOutside ? "true" : "false"),
                "ShowPlayerBox=" + (PortalUI.ShowPlayerBox ? "true" : "false"),
                "HidePlayerBoxInGame=" + (PortalUI.HidePlayerBoxInGame ? "true" : "false"),
                "PetHideInGame=" + (PortalUI.PetHideInGame ? "true" : "false"),
                "PlayerBoxX=" + PortalUI.PlayerBoxX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "PlayerBoxY=" + PortalUI.PlayerBoxY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // 大肥鱼宠物
                "PetShow=" + (PortalUI.PetShow ? "true" : "false"),
                "PetGlobal=" + (PortalUI.PetGlobal ? "true" : "false"),
                "PetSnapEdge=" + (PortalUI.PetSnapEdge ? "true" : "false"),
                "DeepUserStreak=" + DeepUser.Streak,        // 深度用户连续数
                "PetBubbleOn=" + (PortalUI.PetBubbleOn ? "true" : "false"),
                "PetSoundOn=" + (PortalUI.PetSoundOn ? "true" : "false"),
                // 大肥鱼随机发言档位（0 无 / 1 每秒 1% / 2 每秒千分之一）
                "PetRandomSay=" + PortalUI.PetRandomSay,
                "PetModel=" + PortalUI.PetModel,
                "PetVol=" + PortalUI.PetVol.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "PetScale=" + PortalUI.PetScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "PetX=" + PortalUI.PetX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "PetY=" + PortalUI.PetY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "CustomBackupPath=" + (string.IsNullOrEmpty(LevelScanner.CustomBackupPath) ? "" : LevelScanner.CustomBackupPath),
                "RankMin=" + PortalUI.RankMin,
                "RankMax=" + PortalUI.RankMax,
                "NowBarShow=" + (PortalUI.NowBarShow ? "true" : "false"),
                "NowBarMode=" + PortalUI.NowBarMode,
                "NowBarCustomText=" + (string.IsNullOrEmpty(PortalUI.NowBarCustomText) ? "" : PortalUI.NowBarCustomText),
                // 明文存 API Key：单机本地配置文件，不落盘就没法持久化
                // UpdAutoCheck 不再写：自动检查更新是强制的，设置页那个开关已删（见 Updater.AutoCheck）
                "UpdLastSeen=" + (string.IsNullOrEmpty(Updater.LastSeenVersion) ? "" : Updater.LastSeenVersion),
                // 诊断开关：排错时要跨重启保留，否则复现前又得重开一遍
                "LogVerbose=" + (PortalUI.LogVerbose ? "true" : "false"),
                "LogOnBeatLevel=" + (LogOnBeatLevel ? "true" : "false"),
            });
        } catch (System.Exception e) {
            // 不能空 catch：写盘失败无声，玩家会以为设置已保存
            PortalUI.ReportError("保存设置失败（本次修改可能不会被记住）", e);
        }
    }

    /// <summary>进入游玩场景（scnGame）：打开算 1 次游玩 + 自动录入检测（备用逻辑，编辑器 OpenLevel 也会触发）+ 重置本局标记</summary>
    public static void OnEnterGameScene() {
        _autoplayUsed = false;   // 新的一局：重置自动播放标记
        _noFailUsed = false;     // 新的一局：重置不会失败标记
        try {
            string path = GetCurrentLevelPath();
            if (string.IsNullOrEmpty(path)) return;
            // 打开算 1 次游玩（用列表条目路径，确保对上）
            string listFolder = ResolveListFolder(path);
            if (!string.IsNullOrEmpty(listFolder)) {
                // 成就「兴酣落笔摇五岳」：这里也是"一次进入"的检测点之一（外部打开走这条路）。
                // force=false 表示上一次记过就不覆盖，从谱面库进来的快照必须保住
                NoteEntryPlays(listFolder, false);
                LevelScanner.RecordPlay(listFolder);
                Lg.Info($"[LevelLib] 打开算游玩+1: {listFolder}");
            }
            if (!AutoRecord) return;
            AutoRecordIfMissing(path);
        } catch (Exception e) {
            Lg.Ex("自动录入检测失败", e);
        }
    }

    // ================= 音乐名播报：音乐名含 terrasphere 就说「好音乐」（不分大小写）=================
    // 三个来源，任何一个命中就算：① `LevelData.song` ② `LevelData.songFilename` ③ 正在播的 clip 名。
    // 不能"取第一个非空的来判" —— 有的谱面只在一处带那个词，取非空的那个会漏。
    // "完全进入后" = 在打这一关、并且我们的进入过渡层已经收掉：过渡期间宠物整个画布是藏着的，
    // 早说会被"过期插播"丢掉；而进关卡要等砖块 + Iridium 装饰物分帧加载（能拖几十秒）。
    // 「在打这一关」一律用 `PortalUI.IsLevelPlaying()`，不许比场景名 —— 自制谱是在 `scnEditor` 里玩的。
    // 一次进入只判一次：复位只认明确的"一次进入"信号 `NoteLevelEntered()`（谱面库的进入过渡 /
    // 编辑器 OpenLevel / 编辑器启动，三处入口各调一次），不能复位在"没在玩"上 —— 暂停也算没在玩。
    private static bool _musicSaid;          // 本次进入是否已判过
    private static float _musicDeadLine;     // 本次进入的"等关卡数据就绪"窗口
    private static float _musicEtSeenAt;     // 最近一次"过渡层还在演"的时刻
    private static bool _musicEtSeen;        // 本进程里是否见过进入过渡
    // 单独用一个 bool 而不是"时刻 > 0"：时刻恰好为 0 时那种判法会翻车
    private const float MusicProbeSeconds = 6f;
    /// <summary>过渡层收掉之后的宽限窗口：这期间就算游戏还在起跑倒计时里也照说。
    /// 过渡层收掉那一刻游戏可能还在倒计时（paused=True），只等 `!paused` 会晚 1~2 秒；
    /// 我们那个过渡层本来就是"砖块 + 装饰物都就绪"才收的，所以它一收掉就算"完全进入"。</summary>
    private const float MusicAfterTransitionSeconds = 3f;
    private const string MusicWord = "terrasphere";

    /// <summary>「一次进入」的复位点（三个入口都调）：清掉"这一关已经说过"的标记，
    /// 同时让开局位置重新采一次。只在真的记过的时候打日志，免得每次开谱面都刷一行。</summary>
    public static void NoteLevelEntered(string why) {
        // "这一局从哪一格开始"重新采一次，见 `TickStartPosSnapshot`
        _runStartSeqLatch = 0f;
        _runStartSeq = -1;
        _runStartSeqSrc = null;
        _runStartDelay = -1f;
        _orcaActive = false; _orcaSaidOpen = _orcaSaidSpeed = _orcaSaidClear = false;
        _orcaDiagLogged = false;   // 每次进谱面重新打一条判定采样，方便定位
        try {
            if (!_musicSaid && _musicDeadLine <= 0f && !_musicEtSeen) return;
            _musicSaid = false;
            _musicDeadLine = 0f;
            _musicEtSeen = false;
            _musicEtSeenAt = 0f;
            Lg.Info("音乐名播报：复位（" + why + "）");
        } catch { }
    }

    // ================= 「这一局从哪一格开始」开局采样 =================
    // 判据用 `scrController.startedFromCheckpoint` + `currentSeqID`，不能用 `checkpointsUsed` ——
    // 后者记的是"这一局跑的过程中用掉了几次检查点"，从第 1200 格开始打且一次没死仍然是 0。
    // 但 `startedFromCheckpoint` 是粘性的（编辑器遗留的 `GCS.checkpointNum` 会骗下一局），
    // 所以定稿是开局那一刻采一次并锁存：`IsLevelPlaying()` 第一次成立时 `Start_Rewind` 已跑完，
    // 那一瞬间的值就是本局起点；由 `NoteLevelEntered` 清掉重采。
    // 还要留一条"采到过"的标记：没采到就不认这一局，宁可放过也别把"从最后一格开始"记成"从第一格"。
    private static float _runStartSeqLatch;   // > 0 = 这一局开局采过了（存时刻）
    private static int _runStartSeq = -1;     // 开局时游戏报的 seqID（-1 = 还没采）
    private static string _runStartSeqSrc;    // 从哪个字段采到的（诊断用）

    /// <summary>这一局开局采到过没有（`> 0` = 采到了，0 = 这一局跑起来了却没采上）</summary>
    public static float RunStartSeqLatch { get { return _runStartSeqLatch; } }
    /// <summary>这一局从第几格开始（`seqID`，0 = 第一格，>0 = 从中间/最后一格开始）。
    /// 必须配 `RunStartSeqLatch > 0` 一起用：没采到时它是 -1，别当成"从第一格"。</summary>
    public static int RunStartSeq { get { return _runStartSeq; } }

    /// <summary>开局延迟采样的计时（-1 = 还没开始计时）。一上来就采会踩"假 0"：
    /// `IsLevelPlaying()` 第一次成立时 `Start_Rewind`（重定位到检查点/指定格）未必已经落完，
    /// 那一刻 `currentSeqID` 读到的还是 0，"从 95% 开始"就被记成"从第一格"。
    /// 所以真开始跑之后还要再等一小段（见 RunStartDelaySeconds）才采；从第 0 格开局不受影响。</summary>
    private static float _runStartDelay = -1f;
    private const float RunStartDelaySeconds = 0.6f;

    private static void TickStartPosSnapshot() {
        if (_runStartSeqLatch > 0f) return;                     // 这一局采过了
        if (!PortalUI.IsLevelPlaying()) { _runStartDelay = -1f; return; }   // 还没真的开始跑，计时也不算
        if (_runStartDelay < 0f) _runStartDelay = 0f;           // 第一次真的在跑 → 开始计时
        _runStartDelay += Time.unscaledDeltaTime;
        if (_runStartDelay < RunStartDelaySeconds) return;      // 等 Start_Rewind 落完（防"假 0"）
        try {
            var c = scrController.instance;
            if (c == null) return;
            bool fromCp = false;
            try { fromCp = c.startedFromCheckpoint; } catch { }
            int seq = -1;
            try { seq = c.currentSeqID; } catch { }
            // 「从第一格开始」⇔ 游戏说不是从检查点开始的 且 开局 seqID 就是 0。
            // `fromCp` 为真但 seqID 读不到时按"不是第一格"算：宁可放过不误记。
            int startTile = (seq > 0 || fromCp) ? (seq > 0 ? seq : 1) : 0;
            _runStartSeqLatch = Time.unscaledTime;              // 采到了（哪怕值是 0）
            _runStartSeq = startTile;
            _runStartSeqSrc = "startedFromCheckpoint=" + (fromCp ? "真" : "假") + " / currentSeqID=" + seq;
            Lg.Info($"[LevelLib] 开局位置采样：从第 {startTile + 1} 格开始"
                + $"（{_runStartSeqSrc}）-> "
                + (startTile > 0 ? "这一局不是从第一格开始" : "这一局从第一格开始"));
        } catch { }
    }

    /// <summary>每帧调（`Main.OnUpdate`）。两条进入判据并存：
    /// ① 真的在打这一关 = `PortalUI.IsLevelPlaying()`（即 `!paused && isGameWorld`，别自己另发明一套）；
    /// ② 我们自己的过渡层收掉后 3 秒内也算"完全进入" —— 那层收掉时游戏可能还在起跑倒计时里（paused=True），
    /// 只等 `!paused` 会晚 1~2 秒。没有过渡层的入口（编辑器按播放 / 双击文件）走 ①。
    /// 别拿 `conductor.isGameWorld` 单用当闸门：它默认就是 true、只置 true 不置 false。</summary>
    public static void TickMusicAnnounce() {
        try {
            TickStartPosSnapshot();     // 顺路采一次"这一局从哪一格开始"（见它自己的说明）
            // ① 过渡层还在演：记下时刻（它收掉之后那一小段就是“完全进入”）并等它
            if (PortalUI.EnterTransitionActive()) { _musicEtSeen = true; _musicEtSeenAt = Time.unscaledTime; return; }
            if (_musicSaid) return;                     // 这一次进入已经判过
            // ② 该不该"开始判"：真的在打这一关，或者刚刚走完我们自己的进入过渡（后者见上）。
            // 一旦开始了就一路走到底：关卡数据晚到时要在窗口里等它，不能半路放弃
            // （`_musicDeadLine > 0` = 本次进入已经在判了）。
            if (_musicDeadLine <= 0f) {
                bool justArrived = _musicEtSeen
                    && Time.unscaledTime - _musicEtSeenAt <= MusicAfterTransitionSeconds;
                if (!justArrived && !PortalUI.IsLevelPlaying()) return;
                _musicDeadLine = Time.unscaledTime + MusicProbeSeconds;
            }
            bool hasAny;
            string diag;
            string name = MatchedMusicName(out hasAny, out diag);
            if (!hasAny) {
                // 关卡数据还没就绪（刚起来那几百毫秒）：窗口内再等几帧；
                // 窗口用完还是空的（官方关卡 / 非自制谱就是这种）：这关不播
                if (Time.unscaledTime < _musicDeadLine) return;
                _musicSaid = true;
                Lg.Info("音乐名播报：读不到任何音乐名（官方关卡 / 非自制谱）→ 不播");
                return;
            }
            _musicSaid = true;      // 判过就记下（不管命不命中），不然每帧都要再读一遍
            if (name == null) {
                // 不命中也要留一行：只在命中时打日志的话，出问题时日志里什么都没有
                Lg.Info("音乐名播报：不含「" + MusicWord + "」（" + diag + "）→ 不说");
                return;
            }
            Lg.Info("音乐名播报：命中 音乐名 = " + name + "（" + diag + "）→ 说「好音乐」");
            PortalUI.PetSayLine("好音乐", "音乐名 " + name);
        } catch { }
    }

    /// <summary>每帧（Main.OnUpdate）跑 Orca 彩蛋的两道判定。
    /// 第一句原本挂在"音乐名播报"里 —— 那个探针只有 6 秒窗口，Orca 谱带大量装饰物、加载常超时，
    /// 探针一放弃就再也不判（表现就是"进图完全没反应"）。改成这里每帧独立判，等多久都等。</summary>
    public static void TickOrcaEgg() {
        try { TickOrcaOpen(); TickOrcaSpeed(); } catch { }
    }

    /// <summary>第一句：真的在打这一关、且确认是那张 Orca 谱（音乐 Orca.ogg + 物料 848）时说一次。</summary>
    private static void TickOrcaOpen() {
        if (_orcaSaidOpen) return;
        if (!PortalUI.IsLevelPlaying()) return;     // 关卡还没真的跑起来（含加载期），继续等
        if (!IsOrcaChart()) return;                 // 不是那张谱（或关卡数据还没就绪），继续等
        TryOrcaOpen();
    }

    // ================= Orca 彩蛋（物量 848 + 音乐 Orca.ogg）=================
    // 三个时点各说一句：进图"这是我家？！" / 打到雪花段第一个变速（全谱第一个 SetSpeed 在地板 73）
    // "这条鱼可真肥，不对，这TM不是我吗！" / 通关"到家咯！"。进图那一刻解锁彩蛋成就。
    // 判据只认这一张谱：音乐文件名 Orca.ogg 且地板数 848（见 OrcaFloors）——别的谱就算同名音乐也不算。
    private const int OrcaFloors = 848;          // 物料：848 块砖（谱面文件 angleData 长度就是 848）
    private const int OrcaMidFloor = 420;        // 第二句的砖号（作者指定）
    private const int OrcaEndFloor = 847;        // 第三句的砖号（作者指定，不是"通关"）
    private static bool _orcaActive;      // 本局是不是那张谱（进图时定，NoteLevelEntered 复位）
    private static bool _orcaSaidOpen, _orcaSaidSpeed, _orcaSaidClear;

    private static bool _orcaDiagLogged;

    /// <summary>这一局是不是 Orca 那张谱：音乐名（songFilename / song / 正在播的 clip 任一）含 orca，
    /// 且砖数 = 848。判据就是作者给的两条：砖数 + 音乐文件名。读不到值时返回 false（宁可不说，也不误说）。</summary>
    private static bool IsOrcaChart() {
        string diag = null;
        try {
            bool nameOk = false;
            string nm = null;
            var lv = ADOBase.customLevel;
            if (lv != null && lv.levelData != null) {
                string a = lv.levelData.song, b = lv.levelData.songFilename;
                nm = (b ?? "") + " / " + (a ?? "");
                if (!string.IsNullOrEmpty(b) && b.IndexOf("orca", StringComparison.OrdinalIgnoreCase) >= 0) nameOk = true;
                if (!string.IsNullOrEmpty(a) && a.IndexOf("orca", StringComparison.OrdinalIgnoreCase) >= 0) nameOk = true;
            }
            int floors = -1;
            try {
                var lm = ADOBase.lm;
                if (lm != null && lm.listFloors != null) floors = lm.listFloors.Count;
            } catch { }
            bool floorsOk = (floors == OrcaFloors || floors == OrcaFloors + 1);
            diag = "音乐=" + nm + "｜砖数=" + floors + (floorsOk ? "(算)" : "(不算)")
                 + "｜名字" + (nameOk ? "含 orca" : "不含 orca");
            if (!_orcaDiagLogged) {
                _orcaDiagLogged = true;
                Lg.Info("Orca 彩蛋：判定采样 → " + diag);
            }
            return nameOk && floorsOk;
        } catch (System.Exception e) {
            if (!_orcaDiagLogged) { _orcaDiagLogged = true; Lg.Ex("Orca 彩蛋：判定出错", e); }
            return false;
        }
    }

    /// <summary>进图那一刻：说第一句 + 解锁彩蛋成就（一次进入只说一次）。</summary>
    private static void TryOrcaOpen() {
        if (_orcaSaidOpen) return;
        _orcaSaidOpen = true;
        _orcaActive = true;
        PortalUI.PetSayLine("这是我家？！", "Orca 进图");
        Lg.Info("Orca 彩蛋：进图 → 「这是我家？！」");
        try { Achievements.TriggerRepeat(Achievements.AchOrca); } catch { }
    }

    /// <summary>每帧跑：到第 420 块砖说第二句、第 847 块砖说第三句（两句各自只说一次）。
    /// 作者的口径是"打到第 N 块砖"，所以用 currentSeqID（0 基）直接比。</summary>
    private static void TickOrcaSpeed() {
        if (!_orcaActive) return;
        if (!PortalUI.IsLevelPlaying()) return;
        int seq = -1;
        try {
            var c = scrController.instance;
            if (c != null) seq = c.currentSeqID;
        } catch { }
        if (seq < 0) return;
        if (!_orcaSaidSpeed && seq >= OrcaMidFloor) {
            _orcaSaidSpeed = true;
            PortalUI.PetSayLine("这条鱼可真肥，不对，这TM不是我吗！", "Orca 第 420 砖");
            Lg.Info("Orca 彩蛋：第 " + OrcaMidFloor + " 块砖 → 「这条鱼可真肥…」");
        }
        if (!_orcaSaidClear && seq >= OrcaEndFloor) {
            _orcaSaidClear = true;
            PortalUI.PetSayLine("到家咯！", "Orca 第 847 砖");
            Lg.Info("Orca 彩蛋：第 " + OrcaEndFloor + " 块砖 → 「到家咯！」");
        }
    }

    /// <summary>读三个音乐名来源（只读，不做判断）：`song` / `songFilename` / 正在播的 clip 名</summary>
    private static void ReadMusicNames(out string a, out string b, out string c) {
        a = null; b = null; c = null;
        try {
            var lv = ADOBase.customLevel;
            if (lv != null && lv.levelData != null) { a = lv.levelData.song; b = lv.levelData.songFilename; }
        } catch { }
        try {
            var cd = ADOBase.conductor;
            if (cd != null && cd.song != null && cd.song.clip != null) c = cd.song.clip.name;
        } catch { }
    }

    /// <summary>三个来源逐个过一遍，返回命中的那一个（都没命中返回 null）。
    /// `hasAny` = 有没有读到任何东西，用来区分"还没读到数据"（要在窗口内重试）和
    /// "有数据但不含那个词"（当场判定不播）。</summary>
    private static string MatchedMusicName(out bool hasAny, out string diag) {
        string a, b, c;
        ReadMusicNames(out a, out b, out c);
        hasAny = !string.IsNullOrEmpty(a) || !string.IsNullOrEmpty(b) || !string.IsNullOrEmpty(c);
        diag = "song='" + (a ?? "") + "'｜songFilename='" + (b ?? "") + "'｜clip='" + (c ?? "") + "'";
        if (HasMusicWord(a)) return a;
        if (HasMusicWord(b)) return b;
        if (HasMusicWord(c)) return c;
        return null;
    }

    /// <summary>不分大小写地包含那个词吗</summary>
    private static bool HasMusicWord(string s) {
        return !string.IsNullOrEmpty(s) && s.IndexOf(MusicWord, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>编辑器打开谱面（scnEditor.OpenLevel postfix）：直接检测录入</summary>
    public static void OnOpenLevel(string filePath) {
        Lg.Info($"[LevelLib] OpenLevel 检测: path={filePath} AutoRecord开关={AutoRecord}");
        // 这也是"一次进入"的信号之一（双击文件 / 游戏内打开都走这里）
        NoteLevelEntered("编辑器打开谱面");
        // 「深度用户」的计数必须无条件跑，不能挂在 AutoRecord 开关下面
        // （否则玩家关掉自动录入就永远数不到 1000 次）
        DeepUser.NoteEditorOpened(filePath);
        if (!AutoRecord) return;
        try {
            if (string.IsNullOrEmpty(filePath)) return;
            if (!filePath.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase)) return;
            string full = Path.GetFullPath(filePath);
            // 自动录入是"直接录入 + 临时成就提示"，没有确认/拒绝，不用清拒绝记录
            AutoRecordIfMissing(full);
        } catch (Exception e) {
            Lg.Ex("自动录入检测失败", e);
        }
    }

    /// <summary>编辑器启动（scnEditor.Start postfix）：覆盖双击文件/游戏内打开等不走 OpenLevel 的路径</summary>
    public static void OnEditorStart() {
        Lg.Info($"[LevelLib] 编辑器启动检测: levelPath={ADOBase.levelPath} AutoRecord开关={AutoRecord}");
        // 同上：编辑器启动 = 一次新的进入
        NoteLevelEntered("编辑器启动");
        // 同上：无条件跑 —— 这个检测点覆盖"双击文件/游戏内打开"那些路，
        // 正是判"是不是游戏自己打开的"的关键位置
        DeepUser.NoteEditorOpenedSafe();
        if (!AutoRecord) return;
        try {
            string path = ADOBase.levelPath;
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase)) return;
            AutoRecordIfMissing(Path.GetFullPath(path));
        } catch (Exception e) {
            Lg.Ex("自动录入检测失败", e);
        }
    }

    /// <summary>取编辑器当前谱面路径（多路：ADOBase.levelPath → customLevel.levelPath → levelToOpenOnLoad）。
    /// 最后那一路（`levelToOpenOnLoad` = 我们刚设进去的预设）只是"请求"不是"结果"，只能给
    /// "现在打算开哪个谱面"的调用方用；判"到底载入了没"必须传 `includePending: false`。</summary>
    public static string GetEditorLevelPath(bool includePending = true) {
        try {
            if (!string.IsNullOrEmpty(ADOBase.levelPath)) return ADOBase.levelPath;
            if (scnEditor.instance != null && scnEditor.instance.customLevel != null && !string.IsNullOrEmpty(scnEditor.instance.customLevel.levelPath)) {
                return scnEditor.instance.customLevel.levelPath;
            }
            if (includePending && !string.IsNullOrEmpty(scnEditor.levelToOpenOnLoad)) return scnEditor.levelToOpenOnLoad;
        } catch { }
        return null;
    }

    /// <summary>编辑器就绪轮询（Main 延迟 1.5s 起调，覆盖所有加载方式）：同一个谱面只处理一次</summary>
    public static void OnEditorReady() {
        if (!AutoRecord) return;
        try {
            string path = GetEditorLevelPath();
            // 路径变化时打日志（空→非空、A→B 都能看到）
            if (path != _lastSeenPath) {
                _lastSeenPath = path;
                Lg.Info($"[LevelLib] 编辑器轮询: path={path}");
            }
            if (string.IsNullOrEmpty(path)) return;   // 还没加载完，等下次轮询
            if (!path.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase)) return;
            string full = Path.GetFullPath(path);
            if (full == _lastCheckedPath) {
                return;     // 同一谱面只处理一次（不刷日志：周期性写盘是帧率波动的嫌疑）
            }
            _lastCheckedPath = full;
            Lg.Info($"[LevelLib] 编辑器就绪检测: path={full}");
            string listFolder = ResolveListFolder(full);   // 用列表条目路径，确保对上
            // 成就「兴酣落笔摇五岳」：外部（游戏自己）打开也认一次进入。
            // force=false 表示从谱面库进来的那一次已经在 ConfirmOpen 记过了，这里不许覆盖
            // （否则"进它之前的次数"会变成 1，永远判不出第一次）。
            if (!string.IsNullOrEmpty(listFolder)) NoteEntryPlays(listFolder, false);
            if (!string.IsNullOrEmpty(listFolder)) LevelScanner.RecordPlay(listFolder);   // 外部打开也计游玩次数
            AutoRecordIfMissing(full);
        } catch (Exception e) {
            Lg.Ex("自动录入检测失败", e);
        }
    }

    /// <summary>确认从谱面库打开时直接调用（咱自己知道路径，不依赖游戏字段）</summary>
    public static void EnsureRecorded(string path) {
        if (!AutoRecord) return;
        try {
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase)) return;
            string full = Path.GetFullPath(path);
            // 谱面库打开是明确操作：绕过"已处理"拦截，重新检测一次
            _lastCheckedPath = null;
            AutoRecordIfMissing(full);
        } catch (Exception e) {
            Lg.Ex("自动录入失败", e);
        }
    }

    /// <summary>按 ogg 文件名（曲名）判断：列表 Title 里没有就录入</summary>
    private static void AutoRecordIfMissing(string path) {
        try {
            string folder = Path.GetDirectoryName(path);
            // 取文件夹内合格的 ogg 名（跟扫描同一套规则），没有就退回 .adofai 文件名
            string oggBase = string.IsNullOrEmpty(folder) ? null : LevelScanner.SuggestTitle(folder);
            if (string.IsNullOrEmpty(oggBase)) oggBase = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(oggBase)) return;
            bool exists = LevelScanner.Entries.Any(e =>
                string.Equals(e.OggName, oggBase, System.StringComparison.OrdinalIgnoreCase) ||   // 原始 ogg 名（重命名后仍能对上）
                string.Equals(e.Title, oggBase, System.StringComparison.OrdinalIgnoreCase));
            if (!exists) AutoRecordLevel(path);
        } catch (Exception e) {
            Lg.Ex("自动录入判断失败", e);
        }
    }

    /// <summary>判定等级：严判 3 > 标判 2 > 宽判 1（自动标记只升不降用）</summary>
    private static int JudgeRank(string j) {
        if (j == "严判") return 3;
        if (j == "标判") return 2;
        if (j == "宽判") return 1;
        return 0;
    }

    /// <summary>缓存的 scrController 引用：`scrController.instance` 是 FindObjectOfType，每帧多次调用很贵。
    /// 只在为空或已销毁时重新获取。</summary>
    private static scrController _ctrlCache;

    /// <summary>通关日志开关（默认关闭：原先每帧打一条带字符串拼接的日志，是游戏内每帧开销）</summary>
    public static bool LogOnBeatLevel;

    private static scrController GetController() {
        try {
            var c = _ctrlCache;
            // Unity 的 == 重载把"已销毁对象"也判为 null，所以这一句同时覆盖两种情况
            if (c != null) return c;
            _ctrlCache = scrController.instance;
            return _ctrlCache;
        } catch {
            _ctrlCache = null;
            return null;
        }
    }

    /// <summary>供其它模块复用的缓存 controller（避免各处重复 FindObjectOfType）</summary>
    public static scrController GetCachedController() { return GetController(); }

    /// <summary>读 scrController.winTime（private 字段，反射并缓存 FieldInfo）。非通关时为 0，
    /// 暂停/重开时也可能被游戏置回 0 → 轮询会把它当成一次新通关，由 `_lastWinTime` 去重。</summary>
    private static float GetWinTime() {
        try {
            var ctrl = GetController();
            if (ctrl == null) return 0f;
            if (_winTimeField == null) {
                _winTimeField = HarmonyLib.AccessTools.Field(typeof(scrController), "winTime");
                if (_winTimeField == null) return 0f;
            }
            var v = _winTimeField.GetValue(ctrl);
            return v is float f ? f : 0f;
        } catch {
            return 0f;
        }
    }

    /// <summary>关卡状态机回调（StateBehaviour.ChangeState postfix）：记录真实状态到 `PortalUI.HookedState`
    /// （快捷键禁用用：退出游玩会 ChangeState(Start)→放行）；Won = 通关（编辑器兜底）</summary>
    public static void OnStateChanged(Enum newState) {
        try {
            if (newState != null) {
                string s = newState.ToString();
                PortalUI.HookedState = s;
                Lg.Info($"[LevelLib] ChangeState: {s}");
                if (s == "Won") { Lg.Info("ChangeState Won → 触发 OnBeatLevel"); OnBeatLevel(); }
            }
        } catch { }
    }

    /// <summary>本局是否属于"不算真实成绩"的模式 —— 只剩自动播放：不会失败不算（开着不失败但打得干净也照记）。
    /// `RecordRestrictions` 打开时，自动播放的局不记录成绩、也不自动标记。</summary>
    private static bool NotRealRun() { return RecordRestrictions && _autoplayUsed; }

    /// <summary>读"通关时的音高"（百分数：110 = 1.1 倍速）。优先取 `conductor.song.pitch` ——
    /// pitch 事件能中途改倍率，要的是"通关那一刻"的；取不到才退回 `levelData.pitch`；
    /// 都没有返回 0（界面上不显示倍速，别编一个 1.0x）。</summary>
    private static int ReadClearPitch() {
        try {
            var c = ADOBase.conductor;
            if (c != null && c.song != null && c.song.pitch > 0f) {
                return (int)System.Math.Round(c.song.pitch * 100f);
            }
        } catch { }
        try {
            var lvl = ADOBase.customLevel;
            if (lvl != null && lvl.levelData != null && lvl.levelData.pitch > 0) return lvl.levelData.pitch;
        } catch { }
        return 0;
    }

    /// <summary>每帧轮询通关状态：本局开过自动播放 / 不会失败就只记标记（不算真实成绩）；
    /// 编辑器测试模式不走 BeatLevel，靠 winTime 兜底。</summary>
    public static void PollWin() {
        // 本局任何时刻开过 RDC.auto 就标记（自动播放的局不记录成绩）
        try { if (RDC.auto) _autoplayUsed = true; } catch { }
        // 本局任何时刻开过 No Fail 就标记（本局死过没死过要靠它判断）
        try { var nf = GetController(); if (nf != null && nf.noFail) _noFailUsed = true; } catch { }
        // winTime 变了就是一次新的通关（用缓存的 controller，避免每帧 FindObjectOfType）
        try {
            float win = GetWinTime();
            if (win != 0f && win != _lastWinTime) OnBeatLevel(win);
        } catch { }
    }

    /// <summary>通关回调（scrController.BeatLevel postfix + 上面的轮询）：自动标记 + 记录成绩</summary>
    public static void OnBeatLevel() { OnBeatLevel(GetWinTime()); }

    // ================= 通关复核：只把这一局的判定原样读出来 =================
    // 准入判断只用"有没有点名的坏判定"和"判定总数够不够"，不再做漏格算术等推导

    /// <summary>最近一次通关的判定读数（只用于写诊断文件，不参与任何准入判断）</summary>
    private static int[] _lastBuckets;
    private static int _lastFloors;
    private static int _lastAutoHits, _lastDeathsCtrl, _lastDeathsMt;
    private static bool _lastAutoplay, _lastNoFail;
    private static int _lastCheckpoints;

    // ================= 成就「兴酣落笔摇五岳」需要的"本局是不是第一次玩" =================
    // 条件 = 一个谱面游玩次数为 1 时通关（第一次玩就过、中途没死过）。
    // 不能直接判 `GetPlays(folder) == 1` —— 计数会在打开 / 编辑器就绪 / 死亡 / 通关各 +1，
    // 真正第一次通关那一刻早就 ≥3 了。改成进谱面那一刻拍快照：进它之前是不是 0 次。
    // 死亡次数用我们自己的死亡钩子数，不信游戏那个累加值。
    private static string _freshFolder;     // 本局进的是哪个谱面（entry 身份）
    private static bool _freshEntry;        // 进它之前游玩次数 == 0（这一局就是"游玩次数 1"那一局）
    private static int _freshDeaths;        // 这一局死了几次

    /// <summary>进入某个谱面时调用（必须在计数 +1 之前）：记下这一局是不是它的"第一次玩"。
    /// force=true 表示玩家明确的一次新进入（从谱面库打开；同一个谱面再进要重新判）；
    /// force=false 是编辑器侧的就绪检测（同一个谱面重复报到时不覆盖前面那份快照）。</summary>
    public static void NoteEntryPlays(string folder, bool force) {
        if (string.IsNullOrEmpty(folder)) return;
        if (!force && string.Equals(_freshFolder, folder, System.StringComparison.OrdinalIgnoreCase)) return;
        _freshFolder = folder;
        _freshDeaths = 0;
        try { _freshEntry = LevelScanner.GetPlays(folder) == 0; }
        catch { _freshEntry = false; }   // 读不到就当"不是第一次"（宁可少给别乱给）
    }
    /// <summary>判定名字兜底表（下标 = HitMargin 枚举值）。3.3.1 是 12 项、3.4 是 16 项，
    /// 所以名字一律走 `MarginName(i)`：先从游戏程序集的 HitMargin 枚举现读，读不到才用这张表。</summary>
    private static readonly string[] MarginNamesFallback = {
        "太早 VeryEarly?", "很早 VeryEarly", "稍快 EarlyPerfect", "完美 Perfect", "稍慢 LatePerfect",
        "太晚 VeryLate", "过晚 TooLate", "太快 Multipress", "失误 FailMiss", "过载 FailOverload",
        "自动格 Auto", "过按 OverPress",
    };
    private static string[] _marginNames;
    private static int _marginNameTried;

    /// <summary>记录列用的桶下标（分版本）。3.4 在枚举里插了 PerfectMinus / XPerfect / PerfectPlus，
    /// 下标整体后移，凡是用桶下标的代码都必须过这里，别直接写数字。
    /// 3.3.1（12 项）：提前=0+1、太快=7(Multipress)、稍快=2、完美=3、稍慢=4、太慢=5+6、自动格=10；
    /// 3.4（16 项）：提前=0+1、太快=9(Multipress)、稍快=2、完美=3+4+5、稍慢=6、太慢=7+8、自动格=12。</summary>
    private static bool _idx34;          // 本次通关读到的桶是不是 3.4 那套（16 项）
    private static int _iEarly1, _iTooFast, _iFast, _iSlow, _iTooSlow1, _iTooSlow2, _iAuto;
    private static int AutoIdx { get { return _iAuto; } }

    /// <summary>按当前判定表长度定下标（每次通关前调一次）</summary>
    private static void BindBucketIndexes() {
        bool is34 = MarginBucketCount() >= 16;
        _idx34 = is34;
        _iEarly1 = 1; _iFast = 2; _iAuto = is34 ? 12 : 10;
        _iTooFast = is34 ? 9 : 7;                       // Multipress，玩家说的"太快"
        _iSlow = is34 ? 6 : 4;
        _iTooSlow1 = is34 ? 7 : 5; _iTooSlow2 = is34 ? 8 : 6;
        // 完美那一列不走"三个下标相加"（3.3.1 三个都指 3 会算成三倍）→ 统一用 PerfSum()
    }

    /// <summary>取某个桶的次数（下标越界返回 0）</summary>
    private static int Bk(int[] b, int i) {
        return (b != null && i >= 0 && i < b.Length) ? b[i] : 0;
    }
    /// <summary>"完美"那一列（3.4 = 桶 3+4+5 之和；3.3.1 = 就是桶 3）</summary>
    private static int PerfSum(int[] b) {
        return _idx34 ? (Bk(b, 3) + Bk(b, 4) + Bk(b, 5)) : Bk(b, 3);
    }

    /// <summary>判定名（下标 = HitMargin 枚举值）：先从游戏枚举里读真名，读不到用兜底表。
    /// 读枚举与打日志必须分成两个 try —— 合在一起时 catch 里再调 Logger，在没有 UMM 的环境
    /// （Logger 为 null）会二次抛异常把整个函数带崩；判据可以是纯的，不该被日志拖死。</summary>
    public static string MarginName(int i) {
        if (_marginNameTried == 0) {
            _marginNameTried = 1;
            try {
                var names = System.Enum.GetNames(typeof(HitMargin));
                if (names != null && names.Length > 0) _marginNames = names;
            } catch (System.Exception e) {
                try { Lg.Ex("读 HitMargin 枚举失败", e); } catch { }
            }
            try {
                Lg.Info("HitMargin 枚举共 " + (_marginNames != null ? _marginNames.Length : 0) + " 项"
                    + (_marginNames != null ? "：" + string.Join(", ", _marginNames) : ""));
            } catch { }
        }
        if (_marginNames != null && i >= 0 && i < _marginNames.Length) return _marginNames[i];
        return i >= 0 && i < MarginNamesFallback.Length ? MarginNamesFallback[i] : ("未知#" + i);
    }

    /// <summary>判定桶长度（读不到返回 0）</summary>
    public static int MarginBucketCount() {
        try { var b = ReadMarginBuckets(); return b != null ? b.Length : 0; } catch { return 0; }
    }

    /// <summary>是不是 3.4 版（判定表 16 项 = 有 PerfectMinus / XPerfect / PerfectPlus 那一套）；
    /// 3.3.1 是 12 项。通关记录规则两版分开写。</summary>
    public static bool IsGame34() { return MarginBucketCount() >= 16; }

    // ================= 通关准入（分版本，按点名的判定表）=================
    // 3.4 的桶→名字：[0]TooEarly+[1]VeryEarly=提前、[2]EarlyPerfect=稍快、[3]PerfectMinus=-完美、
    // [4]XPerfect=x完美、[5]PerfectPlus=完美+、[6]LatePerfect=稍慢、[7]VeryLate+[8]TooLate=太慢、
    // [9]Multipress=太快、[10]FailMiss=错过（不许有）、[11]FailOverload、[13]OverPress=按太快（不许有）、
    // [12]Auto=自动格（算格子判定）。
    // 3.3.1（12 项）：[0]+[1]=提前、[2]稍快、[3]完美、[4]稍慢、[5]+[6]=太慢、[7]Multipress=太快、
    // [8]FailMiss=错过（不许有）、[9]FailOverload、[11]OverPress=按太快（不许有）、[10]Auto=自动格。
    // 判据 = ① 不许出现点名的坏判定 + ② 允许判定之和不少于 格子数-1。
    // 不能写成"恰好等于"：多出来的判定是空敲/重复按（不致命），少了才是漏格。
    // 读不到桶或格子数 → 放行，宁可少拦也不能把玩家的记录全拦掉。

}
