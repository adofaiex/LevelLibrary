using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;
using LevelLibrary.Effects;

namespace LevelLibrary;

/// <summary>谱面库 mod 入口：UMM 生命周期回调 + 全部 Harmony 补丁的挂载点</summary>
public static class Main {
    public static UnityModManager.ModEntry ModEntry;
    private static bool _running;
    private static HarmonyLib.Harmony _harmony;
    private static float _gameEnterTimer;   // 进入游玩后的计时（延迟检测 levelPath）
    private static bool _gameChecked;
    private static float _editorEnterTimer; // 进入编辑器后的计时（延迟检测 levelPath）
    private static float _editorPollTimer;   // 编辑器内轮询计时（0.5s 一次）

    public static bool Load(UnityModManager.ModEntry modEntry) {
        ModEntry = modEntry;
        modEntry.OnToggle = OnToggle;
        modEntry.OnUpdate = OnUpdate;
        modEntry.OnGUI = OnGUI;
        modEntry.OnSaveGUI = Save;
        // 退出前把攒着的数据同步落盘（死亡记录、完成度、游玩次数平时都是后台合并写的，
        // 退出时那条线程等不到了，脏数据不能丢）
        modEntry.OnSessionStop += OnSessionStop;
        return true;
    }

    /// <summary>游戏退出 / mod 卸载：把攒在内存里的数据同步落盘（后台线程等不到收尾）</summary>
    private static void OnSessionStop(UnityModManager.ModEntry entry) {
        try { LevelScanner.SaveAllNow(); }
        catch (System.Exception e) { try { Lg.Ex("退出前落盘失败", e); } catch { } }
    }

    /// <summary>开一条独立后台线程跑活儿（不参与共享线程池）。
    /// 绝不用 Task.Run / ThreadPool：TogetherBootstrap（别的 mod）把自己的初始化丢进共享线程池，
    /// 并在那条线程里调主线程专属的 Unity API（FindObjectsByType），实测直接把游戏打崩；
    /// 我们往池子里塞任务就等于跟它抢线程、把这个偶发 bug 更容易撞出来。
    /// 传进来的委托里不能碰 Unity API / UI，只能做文件、网络、字符串这类活。</summary>
    /// <summary>现在有几条我们的后台线在跑（卡顿那行会带上它，用来回答"是不是我们自己的后台活在抢 CPU"）。
    /// 只在起线程 / 线程结束那两下加一减一。</summary>
    public static int BackgroundRunning;

    public static void RunBackground(string name, System.Action work) {
        if (work == null) return;
        try {
            var th = new System.Threading.Thread(() => {
                System.Threading.Interlocked.Increment(ref BackgroundRunning);
                try { work(); }
                // 游戏正常退出时会 abort 后台线程，而 ThreadAbortException 会在 catch 块结束时自动重新抛出
                // 于是变成"托管层未处理异常"，Unity 当场写崩溃报告。所以这里静默吞掉，只 ResetAbort。
                // 这个 catch 里不许做任何"可能出错"的事，连日志都不能打：退出过程中 UMM 的 logger /
                // 文件句柄可能已经收摊，在 catch 里再抛一个异常一样致命。
                catch (System.Threading.ThreadAbortException) {
                    try { System.Threading.Thread.ResetAbort(); } catch { }
                }
                catch (System.Exception e) { Lg.Ex("后台线程出错: " + name, e); }
                // 这条后台活收工了（无论正常结束还是抛异常都走到这里）
                System.Threading.Interlocked.Decrement(ref BackgroundRunning);
            });
            th.IsBackground = true;    // 游戏退出就跟着结束，别拖住进程
            th.Name = "LevelLib_" + name;
            th.Priority = System.Threading.ThreadPriority.BelowNormal;   // 别跟游戏抢 CPU
            th.Start();
        } catch (System.Exception e) {
            Lg.Ex("起后台线程失败: " + name, e);
        }
    }

    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value) {
        _running = value;
        if (value) {
            // 先设路径基准，再加载数据（相对路径换算必须最先就绪）
            LevelScanner.ModRoot = ModEntry.Path;   // 数据文件相对路径换算基准
            LevelScanner.RecordedPath = System.IO.Path.Combine(ModEntry.Path, "recorded.txt");   // 自动录入记录
            LevelScanner.NewMarksPath = System.IO.Path.Combine(ModEntry.Path, "newmarks.txt");   // 新导入谱面的提示标记
            Updater.ApplyPendingUpdate();   // 兜底：上一轮"文件真被占用"而没换上的 .new DLL，趁刚启动换上（正常路径下无事可做）
            // 再加载持久化数据（含自定义备份位置），最后设 BackupBase + 扫描
            LevelScanner.LoadTitles(ModEntry.Path);   // 加载显示名覆盖
            MarkStore.Load(ModEntry.Path);            // 加载标记记录
            AutoFeatures.Load(ModEntry.Path);         // 加载自动功能设置
            // 宠物模型表：必须排在 PrewarmWithGameLoad 之前 —— 字形预热要按 PetLinesPool() 把每个模型
            // （含玩家导入的那些）的台词都烘进图集，晚一步就会在换模型的第一次点击时卡一下。
            PortalUI.ReloadPetModels();       // 大肥鱼 + pet_models\ 下导入的模型
            // 开机这一刻（游戏还在读盘、主菜单还没出来）把跨场景能活下来的全做掉：
            // 个人页整页（独立画布 + DontDestroyOnLoad）、字形图集、程序化贴图、写盘线程（见 PortalUI.PrewarmWithGameLoad）。
            // 面板里那几页做不了 —— 它们挂在"开面板才建"的面板画布下。
            PortalUI.PrewarmWithGameLoad();
            // 同步读（不是丢后台）：异步的话第一次显示会先是我们自己的数、过一会儿再跳过去；
            // 读不到（非 Steam 版 / 没装 Steam）就安静地退回我们自己的统计（见 SteamPlaytime.cs）。
            SteamPlaytime.Load();
            PortalUI.LoadConflictKeys();              // 加载热键冲突黑名单（v4.0.106）
            LevelScanner.LoadRecorded();
            LevelScanner.LoadNewMarks();   // 「刚导入还没点开过」的提示标记（列表行拿它决定给不给提示色）
            EffectPresets.Load(ModEntry.Path);   // 去特效：内置四套是代码里的，玩家自建的从 effects.txt 读
            EffectStore.Load(ModEntry.Path);    // 去特效：全局一份 + 每张谱面一份
            LevelScanner.LoadPlays(ModEntry.Path);   // 游玩次数
            Achievements.Load(ModEntry.Path);   // 加载成就（v4.18）
            LevelScanner.LoadLoginDays(ModEntry.Path);   // 登录天数
            LevelScanner.RecordTodayLogin();   // 记今天登录
            Achievements.CheckProgression();   // 累计成就检查（Thousand/天数/时长）
            Achievements.OnUnlocked += PortalUI.OnAchUnlocked;   // 解锁弹提示
            LevelScanner.LoadFavorites(ModEntry.Path);   // 收藏
            // 随机谱面的"今日已抽过"记录（跨天自动刷新，见 RandomPick.Rollover）
            try { RandomPick.Load(ModEntry.Path); } catch (System.Exception erp) { Lg.Ex("加载随机谱面今日记录失败", erp); }
            LevelScanner.LoadNotes(ModEntry.Path);        // 备注
            LevelScanner.LoadDeathMap(ModEntry.Path);     // 死点热力图
            LevelScanner.LoadProgress(ModEntry.Path);     // 完成度
            LevelScanner.LoadRecords(ModEntry.Path);      // 通关记录
            // 清理异常通关记录只在 设置 → 工具 → 数据管理 里手动点（点开先报数、确认后才删、删前自动备份，
            // 见 LevelScanner.PruneLegacyClearRecords）。项目红线：任何"删/清/覆盖玩家数据"的功能都不许再挂回启动流程。
            LevelScanner.LoadXaccs(ModEntry.Path);   // X 精准度记录
            // 备份根目录：自定义位置优先，否则默认 mod\backup
            LevelScanner.BackupBase = string.IsNullOrEmpty(LevelScanner.CustomBackupPath)
                ? System.IO.Path.Combine(ModEntry.Path, "backup")
                : LevelScanner.CustomBackupPath;
            LevelScanner.Refresh(ImportDir);          // 加载谱面库（玩家导入）
            // 迁移旧数据：标记/显示名记录的路径从原位置切到 backup 副本
            MarkStore.Migrate();
            LevelScanner.MigrateTitles();
            PortalUI.OnLibraryChanged();
            // 里程碑成就强绑定：启动时先按"当前标记 + 严判通关记录"归一化一遍（静默，不弹提示）——
            // 标记改过等级、谱面/记录被删过的情况，这里就会把不再成立的里程碑撤销。
            try { Achievements.RebuildMilestones(true); } catch (System.Exception em) { Lg.Ex("启动时里程碑归一化失败", em); }
            // 自动标记：patch 通关回调
            _harmony = new HarmonyLib.Harmony("com.levellibrary");
            // 不用 PatchAll()：PatchAll 只要有一个补丁的目标方法找不到就整体抛异常，会让整个 mod 加载失败
            // （玩家看到的是"mod 没生效"）。这里逐个挂、逐个 try：某个方法在某版本里改名/消失时，
            // 只是那一个功能失效，其他一切照常。
            PatchSafe(MethodLenient(typeof(scrController), "OnPlayerDied",
                    "scrPlayer", "System.Boolean", "System.Boolean", "System.String", "System.Boolean"),
                "scrController.OnPlayerDied",
                postfix: new HarmonyLib.HarmonyMethod(typeof(PlayerDiedPatch), "Postfix"));
            PatchSafe(HarmonyLib.AccessTools.Method(typeof(scrController), "BeatLevel"), "scrController.BeatLevel",
                postfix: new HarmonyLib.HarmonyMethod(typeof(AutoMarkPatch), "Postfix"));
            PatchSafe(HarmonyLib.AccessTools.Method(typeof(scnEditor), "OpenLevel", new System.Type[] { typeof(string) }), "scnEditor.OpenLevel",
                postfix: new HarmonyLib.HarmonyMethod(typeof(OpenLevelPatch), "Postfix"));
            PatchSafe(HarmonyLib.AccessTools.Method(typeof(scnEditor), "Start"), "scnEditor.Start",
                prefix: new HarmonyLib.HarmonyMethod(typeof(EffectInjectorHooks.FxEditorStartHook), "Prefix"),
                postfix: new HarmonyLib.HarmonyMethod(typeof(EditorStartPatch), "Postfix"));
            // 去特效的注入点。两个补丁在 EffectInjector.cs 里是 [HarmonyPatch] 特性写法，
            // 但本项目从不做 PatchAll（见上面那段注释：一个补丁找不到目标就整体抛异常、整个 mod 加载失败），
            // 特性不会自己生效 —— 必须在这里逐个 PatchSafe 手动挂，否则去特效永远是"配了但不生效"。
            // LoadLevel 用 Prefix 开窗 + Finalizer 关窗（抛异常时也关）；ReadAllText 用 Postfix 改写返回文本。
            int fxOk = 0;
            var ldType = HarmonyLib.AccessTools.TypeByName("LevelData");
            System.Reflection.MethodBase ldLoad = null;
            if (ldType != null) { try { ldLoad = HarmonyLib.AccessTools.Method(ldType, "LoadLevel"); } catch { } }
            fxOk += PatchSafe(ldLoad, "LevelData.LoadLevel",
                prefix: new HarmonyLib.HarmonyMethod(typeof(EffectInjectorHooks.FxLoadLevelHook), "Prefix"),
                postfix: null,
                finalizer: new HarmonyLib.HarmonyMethod(typeof(EffectInjectorHooks.FxLoadLevelHook), "Finalizer")) ? 1 : 0;
            try {
                // RDFile 的命名空间在两个游戏版本间不一致（ADOFAI / 别的），按短名找最稳；
                // 找不到就只跳过这一个功能，其余照常（项目红线：不让一个补丁把整个 mod 搞挂）。
                var rdType = HarmonyLib.AccessTools.TypeByName("RDFile");
                System.Reflection.MethodBase rdRead = null;
                if (rdType != null) {
                    try { rdRead = HarmonyLib.AccessTools.Method(rdType, "ReadAllText"); } catch { }
                }
                if (rdRead != null) {
                    _harmony.Patch(rdRead, postfix: new HarmonyLib.HarmonyMethod(typeof(EffectInjectorHooks.FxReadAllTextHook), "Postfix"));
                    fxOk++;
                } else {
                    Lg.Info("去特效跳过（这个版本里找不到方法）: RDFile.ReadAllText");
                }
            } catch (System.Exception efx) {
                Lg.Info($"[LevelLib] 补丁失败: RDFile.ReadAllText —— {efx.GetType().Name}: {efx.Message}");
            }
            Lg.Info($"[LevelLib] 去特效注入已挂 {fxOk}/2（LevelData.LoadLevel / RDFile.ReadAllText）");
            // uGUI 的输入模块：整套事件处理跑在 UI map（Bypass）里，否则面板自己收不到键鼠。
            // Prefix 开、Finalizer 关，配对出现（Finalizer 保证抛异常时也会关掉）。
            try {
                var sipProcess = HarmonyLib.AccessTools.Method(typeof(UnityEngine.EventSystems.StandaloneInputModule), "Process");
                if (sipProcess != null) {
                    _harmony.Patch(sipProcess,
                        prefix: new HarmonyLib.HarmonyMethod(typeof(StandaloneProcessGuardPatch), "Prefix"),
                        finalizer: new HarmonyLib.HarmonyMethod(typeof(StandaloneProcessGuardPatch), "Finalizer"));
                }
            } catch (System.Exception e0) { Lg.Ex("挂 StandaloneInputModule 输入 map 失败", e0); }
            try {
                var esUpdate = HarmonyLib.AccessTools.Method(typeof(UnityEngine.EventSystems.EventSystem), "Update");
                if (esUpdate != null) {
                    _harmony.Patch(esUpdate,
                        prefix: new HarmonyLib.HarmonyMethod(typeof(EventSystemUiMapPatch), "Prefix"),
                        finalizer: new HarmonyLib.HarmonyMethod(typeof(EventSystemUiMapPatch), "Finalizer"));
                    Lg.Info("EventSystem.Update 已进入 UI map（uGUI 输入不受闸门影响）");
                }
            } catch (System.Exception e1) { Lg.Ex("挂 EventSystem 输入 map 失败", e1); }
            // 输入闸门：游戏读操作类按键的唯一出口（原理见 AutoFeatures.cs 里那段长注释）。
            // 面板打开时让它们回答"没按键"，游戏就收不到我们的按键；
            // 它们跟 UnityEngine.Input 无交集，所以我们的热键 / Esc / uGUI 完全不受影响。
            int gateOk = 0;
            gateOk += PatchSafe(MethodLenient(typeof(RDInput), "GetState", "InputAction", "ButtonState"), "RDInput.GetState",
                prefix: new HarmonyLib.HarmonyMethod(typeof(RdInputGetStateGate), "Prefix")) ? 1 : 0;
            gateOk += PatchSafe(MethodLenient(typeof(RDInput), "GetMain", "ButtonState"), "RDInput.GetMain",
                prefix: new HarmonyLib.HarmonyMethod(typeof(RdInputGetMainGate), "Prefix")) ? 1 : 0;
            gateOk += PatchSafe(MethodLenient(typeof(RDInput), "GetStateKeys", "ButtonState"), "RDInput.GetStateKeys",
                prefix: new HarmonyLib.HarmonyMethod(typeof(RdInputGetStateKeysGate), "Prefix")) ? 1 : 0;
            gateOk += PatchSafe(HarmonyLib.AccessTools.Method(typeof(AsyncInputManager), "Update"), "AsyncInputManager.Update",
                prefix: new HarmonyLib.HarmonyMethod(typeof(AsyncInputManagerGate), "Prefix")) ? 1 : 0;
            Lg.Info($"[LevelLib] 输入闸门已挂 {gateOk}/4（GetState / GetMain / GetStateKeys / AsyncInputManager.Update）");
            // ---------- 两套 input map 的切换点 ----------
            // 挂在这里的原因：游戏的键盘 map 是硬编码 KeyCode + 50 多处直接读 UnityEngine.Input，
            // 所以引擎的 UnityEngine.Input 才是键盘唯一的总入口（详见 InputMap.cs 顶部的说明）。
            int mapOk = RegisterInputMapGate();
            Lg.Info($"[LevelLib] 输入 map 闸门已挂 {mapOk}/16（UI map 生效时游戏读不到键鼠）");
            if (mapOk < 16) {
                Lg.Info("输入 map 闸门缺口 " + (16 - mapOk) + "/16 —— 挂不上的都是引擎 extern"
                    + "（Input.anyKey / anyKeyDown / GetMouseButton(Down/Up)，反编译确认是 public static extern，没有 IL 体）："
                    + "键盘 GetKey* 系、虚拟轴、鼠标坐标/滚轮都在闸门内；这几个只能靠 RDInput 那层 + 面板全屏遮挡兜底");
            }
            // 跨 mod 冲突防御：Together（多人 mod）在共享线程池线程上反射加载自己的核心，并在那条线程里调
            // Unity 主线程专属的 FindObjectsByType → 撞上场景卸载就 native 崩游戏。这里把它那类调用转交主线程执行，
            // 超时才返回空，两个 mod 才能共存（详见 CrossModGuard.cs）。
            try { CrossModGuard.Install(_harmony); }
            catch (System.Exception eG) { Lg.Ex("跨mod防御：安装失败", eG); }
            // 界面文字翻译层：拦 UnityEngine.UI.Text.text 的赋值（中文环境下是空操作，零风险）。
            // 必须挂 prefix：setter 方法体只有一句"把参数写进字段"，postfix 在方法体跑完之后才执行，
            // 那时改 ref value 只是改了个局部参数，字段一个字都不会变（看起来像没效果）。
            try {
                var setter = HarmonyLib.AccessTools.PropertySetter(typeof(UnityEngine.UI.Text), "text");
                if (setter != null) {
                    _harmony.Patch(setter, prefix: new HarmonyLib.HarmonyMethod(typeof(TextTranslatePatch), "Prefix"));
                    Lg.Info("界面文字翻译层已挂（Text.text setter · prefix）");
                } else {
                    Lg.Info(" 界面文字翻译层没挂上：找不到 Text.text 的 setter（界面文字不会跟随语言）");
                }
            } catch (System.Exception eT) {
                Lg.Ex("界面文字翻译层挂载失败（界面文字不会跟随语言）", eT);
            }
            // 关卡状态机通关检测（编辑器模式不走 BeatLevel，靠 States.Won 兜底；方法可能随版本变化，找不到就跳过）
            try {
                System.Reflection.MethodInfo chg = null;
                foreach (var mm in typeof(MonsterLove.StateMachine.StateBehaviour).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)) {
                    if (mm.Name == "ChangeState" && !mm.IsGenericMethodDefinition) {
                        var ps = mm.GetParameters();
                        if (ps.Length == 1 && typeof(System.Enum).IsAssignableFrom(ps[0].ParameterType)) { chg = mm; break; }
                    }
                }
                if (chg != null) _harmony.Patch(chg, postfix: new HarmonyLib.HarmonyMethod(typeof(AutoFeatures), nameof(AutoFeatures.OnStateChanged)));
            } catch { }
            // 主菜单（scnCLS）输入冻结：面板打开时冻结所有 Update 系方法（空格/Enter 开始游戏、自动播放等输入检测全停）。
            // 这几个方法不是每个版本都有（3.4 已移除 FixedUpdate / OnGUI），找不到就跳过，补日志方便换版时对账。
            try {
                foreach (var mn in new[] { "Update", "LateUpdate", "FixedUpdate", "OnGUI" }) {
                    var clsM = HarmonyLib.AccessTools.Method(typeof(scnCLS), mn);
                    if (clsM != null) {
                        _harmony.Patch(clsM, prefix: new HarmonyLib.HarmonyMethod(typeof(ClsFreezePatch), nameof(ClsFreezePatch.Prefix)));
                        Lg.Info($"[LevelLib] 主菜单冻结：已挂 scnCLS.{mn}");
                    } else {
                        Lg.Info($"[LevelLib] 主菜单冻结：跳过 scnCLS.{mn}（这个版本里没有它 3.4 已移除 FixedUpdate/OnGUI）");
                    }
                }
            } catch { }
            // 面板打开时只做两件事：Time.timeScale = 0 + SetGameInputEnabled(false)。
            // 不再逐个拦按键（CheckKeyState / RDInput.Main / Input.GetKey* 那套已删）：输入隔离靠 input map / isActive，
            // 而且逐个拦会把热键（E）也拦掉 —— 游戏读不到，我们自己同一个 Input.GetKeyDown 也读不到。
            // 暂停调用探针：只记录、不改变行为（prefix 不返回 false），
            // 用来判断"关面板后游戏自己被暂停"到底是不是按键泄漏进去触发的。
            try {
                var tpM2 = HarmonyLib.AccessTools.Method(typeof(scrController), "TogglePauseGame");
                if (tpM2 != null) {
                    _harmony.Patch(tpM2, prefix: new HarmonyLib.HarmonyMethod(typeof(PauseCallProbe), nameof(PauseCallProbe.Prefix)));
                    Lg.Info("已挂暂停调用探针（只记录不干预）");
                }
            } catch (System.Exception e) { Lg.Ex("挂暂停调用探针失败", e); }
            // 换版自检必须在所有 patch 挂完之后调：它要复核"应挂的 patch 到底挂上了没"，
            // 在挂之前跑会把所有 patch 都报成"未挂"（假警报）。换版最容易坏的就是"签名变了 → 静默没挂"（详见 Verifier.cs）。
            Verifier.RunStartupChecks();
            // 把"要不要更新"那张真值表跑一遍（纯函数，零副作用）—— 游戏版本换回去时靠它保证换线能换回来（见 Updater.CompareGamePart）。
            Updater.RunVersionSelfTest();
            // TUF 社区定级的判定表自检：三条实测硬规矩（回退只在 total==0 触发 / total==1 必须过艺术家校验 /
            // hasMore 一律不自动采用）编译器查不出来，写错了只会"某个谱面被悄悄写错标记"。纯函数、零网络、零副作用。
            TufClient.RunSelfTest();
            // TUF 批量评级的判定表自检（三栏怎么分、特殊档与 Unranked 不写、NeedsReview 一律进「需要你确认」）：
            // 同样是编译器查不出来的逻辑 —— 写错了只会「某个谱面被悄悄写错标记」。纯函数、零网络、零副作用。
            TufBatch.RunSelfTest();
            PortalUI.Start();             // 谱面库面板
            BirthdayUI.Check();            // 生日彩蛋（1月13日弹出祝福界面）
            Lg.Info($"[LevelLib] 已启动，谱面 {LevelScanner.Entries.Count} 个");
            // 把多语言词条数打在启动日志里：只要有一个 0 就说明 LangData 的静态构造器没跑、两张表是空的，换语言一个字都不变。
            Lg.Info($"[LevelLib] 多语言词条：中文 {Lang.Count("zh")} / 英文 {Lang.Count("en")}"
                + $" / 韩文 {Lang.Count("ko")}（当前语言={Lang.Current}）");
            // 网络预热：现在还在加载画面里，把到平台的 TLS 握手提前做掉。
            // 不然这笔一次性开销会砸在"玩家真的点检查更新"那一帧上（实测 745ms 卡帧）。
            Updater.WarmUp();
        } else {
            _harmony?.UnpatchAll("com.levellibrary");
            PortalUI.Stop();
        }
        return true;
    }

    /// <summary>这个方法有没有"可以挂的托管 IL 体"。
    /// Unity 里带 [FreeFunction] / [NativeMethod] 的 extern 成员是引擎原生实现（反编译确认 Input.anyKey、anyKeyDown、
    /// GetMouseButton(int)、Object.FindObjectsByType 等都是 public static extern）。这类方法没有 IL 体，
    /// Harmony 挂上去只会生成空体包装，JIT 时报 InvalidProgramException，白刷一堆异常和堆栈，所以挂之前先判断。</summary>
    internal static bool HasPatchableBody(System.Reflection.MethodBase m) {
        try {
            if (m == null || m.IsAbstract) return false;
            var f = m.GetMethodImplementationFlags();
            if ((f & System.Reflection.MethodImplAttributes.InternalCall) != 0) return false;
            if ((f & System.Reflection.MethodImplAttributes.Native) != 0) return false;
            return m.GetMethodBody() != null;
        } catch { return false; }
    }

    /// <summary>挂一个补丁，失败只记日志、不抛（返回是否成功）。
    /// 替代 Harmony 的 PatchAll()：PatchAll 遇到一个找不到目标方法的补丁会整体抛异常，导致整个 mod 加载失败。
    /// 先判"有没有 IL 体"，没有就安静跳过（引擎 extern 不算错误）；真失败时只记一行原因，
    /// 不打整个堆栈 —— 原因看 Message 就够了，堆栈会在启动时写一大段。</summary>
    private static bool PatchSafe(System.Reflection.MethodBase target, string label,
                                  HarmonyLib.HarmonyMethod prefix = null, HarmonyLib.HarmonyMethod postfix = null,
                                  HarmonyLib.HarmonyMethod finalizer = null) {
        try {
            if (target == null) { Lg.Info($"[LevelLib] 补丁跳过（这个版本里找不到方法）: {label}"); return false; }
            if (!HasPatchableBody(target)) {
                Lg.Info($"[LevelLib] 补丁跳过（引擎 extern，没有 IL 体，Harmony 挂不上）: {label}");
                return false;
            }
            _harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer);
            return true;
        } catch (System.Exception e) {
            Lg.Info($"[LevelLib] 补丁失败: {label} —— {e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>按"方法名 + 参数类型名"找目标方法，找不到再退回只按名字找。
    /// 游戏换版本时这些方法的签名会变（3.4 实测：RDInput.GetStateKeys 多了 ButtonState 参数、
    /// OnPlayerDied 从无参变成 5 个参数），而 AccessTools.Method(t, 名字) 在多重重载下会挑第一个同名方法
    /// 于是挂到错的重载上 = "补丁挂成功了、功能却不生效"。
    /// 参数类型用字符串给、不用 typeof：某个版本才有的类型在另一个版本上编译不过。
    /// 两种都失败才算"这个版本里没有它"，绝不让一个签名变化把 mod 加载搞挂；顺带打一行日志报出挂到的是哪个签名。</summary>
    private static System.Reflection.MethodBase MethodLenient(System.Type t, string name, params string[] argTypeNames) {
        System.Reflection.MethodBase found = null;
        try {
            if (argTypeNames != null && argTypeNames.Length > 0) {
                var args = new System.Type[argTypeNames.Length];
                bool all = true;
                for (int i = 0; i < argTypeNames.Length; i++) {
                    var at = HarmonyLib.AccessTools.TypeByName(argTypeNames[i]);
                    if (at == null) { all = false; break; }
                    args[i] = at;
                }
                if (all) found = HarmonyLib.AccessTools.Method(t, name, args);
            }
        } catch { }
        bool exact = found != null;
        if (found == null) {
            try { found = HarmonyLib.AccessTools.Method(t, name); } catch { }
        }
        if (found != null) {
            try {
                var ps = found.GetParameters();
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType.Name); }
                Lg.Info($"[LevelLib] 目标签名 {t.Name}.{name}({sb}) —— {(exact ? "按参数类型精确命中" : "按名字兜底命中")}");
            } catch { }
        }
        return found;
    }

    /// <summary>把输入 map 闸门挂到 UnityEngine.Input 的每一个键鼠入口上，返回成功数（共 16 个）。
    /// 为什么挂在这里：游戏的键盘 map 是硬编码 KeyCode，另有 50 多处直接读 UnityEngine.Input，
    /// 所以引擎的 UnityEngine.Input 才是键盘唯一的总入口（详见 InputMap.cs 顶部说明）。
    /// 挂不上的 5 个（anyKey / anyKeyDown / GetMouseButton(int) / GetMouseButtonDown(int) / GetMouseButtonUp(int)）
    /// 在 Unity 6000.3 里是 public static extern、没有 IL 体，硬挂会在 JIT 阶段抛 InvalidProgramException；
    /// 它们由 RDInput 那层闸门 + 面板全屏遮挡兜底，启动日志里"输入 map 闸门缺口"那行会明确报出来。
    /// GetTouch / touches / mousePresent 故意不挂：桌面端用不到，挂了反而可能让游戏以为"没有鼠标"。
    /// 挂了也不影响面板：uGUI 的输入模块（StandaloneInputModule.Process）整段跑在 Bypass 里。</summary>
    private static int RegisterInputMapGate() {
        var T = typeof(UnityEngine.Input);
        var HK = new System.Type[] { typeof(KeyCode) };
        var S = new System.Type[] { typeof(string) };
        var I = new System.Type[] { typeof(int) };
        int ok = 0;
        // 键盘
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetKey", HK), "Input.GetKey", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetKeyDown", HK), "Input.GetKeyDown", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetKeyUp", HK), "Input.GetKeyUp", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetKey", S), "Input.GetKey(string)", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetKeyDown", S), "Input.GetKeyDown(string)", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetKeyUp", S), "Input.GetKeyUp(string)", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.PropertyGetter(T, "anyKey"), "Input.anyKey", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.PropertyGetter(T, "anyKeyDown"), "Input.anyKeyDown", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.PropertyGetter(T, "inputString"), "Input.inputString", prefix: HM(nameof(InputMapGate.Str))) ? 1 : 0;
        // 鼠标
        ok += PatchSafe(HarmonyLib.AccessTools.PropertyGetter(T, "mousePosition"), "Input.mousePosition", prefix: HM(nameof(InputMapGate.Vec3))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.PropertyGetter(T, "mouseScrollDelta"), "Input.mouseScrollDelta", prefix: HM(nameof(InputMapGate.Vec2))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetMouseButton", I), "Input.GetMouseButton", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetMouseButtonDown", I), "Input.GetMouseButtonDown", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetMouseButtonUp", I), "Input.GetMouseButtonUp", prefix: HM(nameof(InputMapGate.Bool))) ? 1 : 0;
        // 虚拟轴（游戏相机 / Rewired 平台层会读）
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetAxis", S), "Input.GetAxis", prefix: HM(nameof(InputMapGate.Float))) ? 1 : 0;
        ok += PatchSafe(HarmonyLib.AccessTools.Method(T, "GetAxisRaw", S), "Input.GetAxisRaw", prefix: HM(nameof(InputMapGate.Float))) ? 1 : 0;
        return ok;
    }

    private static HarmonyLib.HarmonyMethod HM(string prefixName) =>
        new HarmonyLib.HarmonyMethod(typeof(InputMapGate), prefixName);

    /// <summary>mod 目录下存放导入谱面的文件夹</summary>
    public static string ImportDir => System.IO.Path.Combine(ModEntry.Path, "imported");

    private static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime) {
        if (!_running) return;
        BirthdayUI.Tick();
        MourningUI.Tick();   // 九一八公祭日：界面灰度滤镜 + 默哀倒计时弹窗（v4.19.261）
        OfficialLevels.Tick();   // 官方关卡：进了新的官方关卡就记一次游玩次数（主页详情区的统计）
        PortalUI.Tick();
        AutoFeatures.TickLaunchAction();   // 开机接管进入：自动进关卡编辑器，就绪后自动打开谱面库（设置里的一个开关）
        AutoFeatures.TickUmmWindow();      // 开机接管进入：把 UMM 每次开机弹出来的模组列表窗口收起来
        LevelScanner.FlushPlays();   // 游玩次数后台写盘（死亡瞬间不卡）
        LevelScanner.FlushDeathProgress();   // 死亡记录 + 完成度后台写盘（同上，死亡瞬间不卡）
        AutoFeatures.PollWin();      // 轮询通关（编辑器测试模式不走 BeatLevel，靠 winTime 兜底）
        // 场景变化检测：进入游玩场景后延迟 0.8s 再自动录入（等 levelPath 就绪）
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "scnGame") {
            _gameEnterTimer += deltaTime;
            if (!_gameChecked && _gameEnterTimer > 0.8f) {
                _gameChecked = true;
                AutoFeatures.OnEnterGameScene();
            }
        } else {
            _gameEnterTimer = 0f;
            _gameChecked = false;
        }
        // 编辑器场景：持续轮询检测谱面路径（每 0.5s 一次，路径就绪即处理，覆盖双击文件/游戏内打开等加载方式）
        if (scene == "scnEditor") {
            _editorEnterTimer += deltaTime;
            if (_editorEnterTimer > 1.5f) {   // 进编辑器 1.5s 后开始轮询
                _editorPollTimer -= deltaTime;
                if (_editorPollTimer <= 0f) {
                    _editorPollTimer = 0.5f;
                    AutoFeatures.OnEditorReady();
                }
            }
        } else {
            _editorEnterTimer = 0f;
            _editorPollTimer = 0f;
        }
        // 音乐名播报：它不看场景名（自制谱是在 scnEditor 里玩的），判据在它自己内部（PortalUI.IsLevelPlaying，全项目唯一那条）。
        AutoFeatures.TickMusicAnnounce();
        AutoFeatures.TickOrcaEgg();      // Orca 彩蛋：第二句（打到地板 73）
        PortalUI.TickBgmFade();          // 关面板的 BGM 淡出（面板关了它也必须继续走）
    }

    private static bool _capturingHotkey;

    private static void OnGUI(UnityModManager.ModEntry modEntry) {
        GUILayout.Label("LevelLibrary Harmony", UmmStyle.Title);
        GUILayout.Space(4);
        // 最上面两个链接：写在同一行里并排显示（获取更新在左、投稿/反馈在右）
        GUILayout.BeginHorizontal();
        RenderLink(Lang.Tr("获取更新：点击前往"), UpdateUrl);
        GUILayout.Space(24);
        RenderLink(Lang.Tr("成就投稿/BUG反馈"), AuthorHomeUrl);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        // 谱面库设置里点"设置"后在此捕获按键（OnGUI 里 Input.GetKeyDown 稳定）
        if (!string.IsNullOrEmpty(PortalUI.CaptureTarget)) {
            GUILayout.Label(Lang.Tr("请按键盘上一个键…"), UmmStyle.Label);
            Lg.Info("OnGUI捕获中 target=" + PortalUI.CaptureTarget);
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode))) {
                int v = (int)k;
                if (v == 0 || v >= 323) continue;   // 跳过 None 和鼠标/摇杆键
                if (UiInput.KeyDown(k)) {
                    Lg.Info("捕获到键 k=" + k + " 原target=" + PortalUI.CaptureTarget);
                    if (PortalUI.ApplyCapturedKey(k)) break;   // 成功则结束，冲突则继续等下一个键
                }
            }
            return;
        }
        if (_capturingHotkey) {
            GUILayout.Label(Lang.Tr("请按键盘上一个键…"), UmmStyle.Label);
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode))) {
                int v = (int)k;
                if (v == 0 || v >= 323) continue;   // 跳过 None 和鼠标/摇杆键
                if (UiInput.KeyDown(k)) {
                    PortalUI.Hotkey = k;
                    PortalUI.SaveHotkey(k);
                    _capturingHotkey = false;
                    break;
                }
            }
        } else {
            // 三个按钮排成一行（用 BeginHorizontal 强制横排，不会各自换行）。
            // 这整个 UMM 设置界面是 IMGUI（GUILayout），而 TextTranslatePatch 只拦 uGUI 的 Text.text 赋值，
            // 所以这里的中文字面量从来没过翻译表，所有展示文字都得自己过一遍 Lang.Tr（IMGUI 每帧重绘，点完下一帧就变）。
            // 宽度也一律用翻译后的字符串去量，否则英文会撑破按钮 / 中文会留一片空。
            string sOpen = Lang.Tr("打开谱面库");
            string sHotkey = Lang.Tr("更换快捷键");
            string sBatch = Lang.Tr("扫描文件夹批量导入");
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = UmmStyle.Blue;
            if (GUILayout.Button(sOpen, UmmStyle.Btn, GUILayout.Width(BtnWidth(sOpen)), GUILayout.Height(38f))) PortalUI.TogglePanelFrom("UMM按钮");
            GUILayout.Space(12);
            GUI.backgroundColor = UmmStyle.Blue;
            if (GUILayout.Button(sHotkey, UmmStyle.Btn, GUILayout.Width(BtnWidth(sHotkey)), GUILayout.Height(38f))) _capturingHotkey = true;
            GUILayout.Space(12);
            GUI.backgroundColor = UmmStyle.Blue;
            if (GUILayout.Button(sBatch, UmmStyle.Btn, GUILayout.Width(BtnWidth(sBatch)), GUILayout.Height(38f))) {
                // 三个导入入口行为要统一：先确认没在导入中才置标志，否则标志会残留、污染下一次导入。
                if (PortalUI.IsBatchRunning) PortalUI.ShowToast(Lang.Tr("批量导入正在进行中…"));
                else { PortalUI.RequestAutoVerifyAfterBatch = true; PortalUI.BatchImportLevels(); }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            GUILayout.Label(Lang.Tr("让我们说 / Language / 언어"), UmmStyle.Label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Lang.Codes.Length; i++) {
                string code = Lang.Codes[i];
                bool on = Lang.Current == code;
                string label = (on ? "● " : "○ ") + Lang.Names[i];
                GUI.backgroundColor = on ? UmmStyle.Blue : UmmStyle.BlueDim;   // 选中=主题蓝，未选中=暗一档的同色
                if (GUILayout.Button(label, UmmStyle.Btn, GUILayout.Width(BtnWidth(label)), GUILayout.Height(34f))) {
                    if (Lang.Current != code) {
                        Lang.Current = code;
                        AutoFeatures.Save();
                        // 光改 Lang.Current 不够：TextTranslatePatch 只拦"赋值"那一下，已经写好的 Text 不会自己回头看，
                        // 所以得把已经摆在我们界面上的文字重刷一遍（面板里预建好的那几页尤其明显）。
                        int n = TextTranslatePatch.RefreshUiTexts();
                        ShowLangToast();
                        try { Lg.Info($"[LevelLib] 语言切换 → {code}（界面文字重刷 {n} 条）"); } catch { }
                    }
                }
                GUILayout.Space(8);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>在日志里记一条"当前语言已切换"（界面文字由 TextTranslatePatch.RefreshUiTexts 就地重刷）</summary>
    private static void ShowLangToast() {
        string msg = Lang.Current == "en" ? "Language set to English"
                   : Lang.Current == "ko" ? "언어가 한국어로 설정되었습니다"
                   : "语言已设置为中文";
        try { Lg.Info("" + msg); } catch { }
    }

    /// <summary>按钮宽度 = 文字宽度 + 左右各 6 像素留白（样式本身已有 12 像素内边距）。
    /// 必须显式给宽度：GUILayout.Button 默认会横向占满整个设置界面，看起来很长；
    /// 留白也不能给太多，否则三个按钮排一行会超出面板宽度。</summary>
    private static float BtnWidth(string text) {
        var style = UmmStyle.Btn;
        float w = style != null ? style.CalcSize(new GUIContent(text)).x : text.Length * 14f;
        return w + 12f;
    }

    /// <summary>作者 B 站主页</summary>
    private const string AuthorHomeUrl = "https://space.bilibili.com/3493132696029515";
    /// <summary>mod 更新/下载页</summary>
    private const string UpdateUrl = "https://www.adofaitools.top/#/mods?mod=mod-levellibrary&resourceType=MOD";

    private static GUIStyle _linkStyle;

    /// <summary>画一个"链接"：下划线文字做成按钮，点击用系统浏览器打开。
    /// 字号 20，文字在当前主题色的两档淡蓝之间缓慢呼吸渐变（约 8 秒一个来回），鼠标悬停时变白提亮。
    /// 必须用 GUILayout.Width 限宽 —— GUILayout.Button 默认会横向占满整行，
    /// 那样第二个链接永远会被挤到下一行，限宽之后两个才会真正并排。
    /// 用的是 UMM 设置界面的 IMGUI（GUILayout），不是游戏里的 uGUI。</summary>
    private static void RenderLink(string text, string url) {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(url)) return;
        if (_linkStyle == null) {
            _linkStyle = new GUIStyle(GUI.skin.label) { richText = true };
            _linkStyle.fontSize = 20;
            _linkStyle.alignment = TextAnchor.MiddleLeft;
            _linkStyle.padding = new RectOffset(0, 0, 3, 3);
        }
        // 淡蓝色缓慢呼吸渐变：在两种淡蓝之间来回过渡（约 8 秒一个来回），不刺眼
        // 两档都跟着当前主题色（亮端 = 主题色，深端 = 主题色按 0.61 / 0.81 / 0.95 压暗）
        float k = (Mathf.Sin(Time.unscaledTime * 0.4f) + 1f) * 0.5f;
        var tc = PortalUI.ThemeColor;
        Color rc = Color.Lerp(new Color(tc.r, tc.g, tc.b, 1f),
                              new Color(tc.r * 0.61f, tc.g * 0.81f, tc.b * 0.95f, 1f), k);
        _linkStyle.normal.textColor = rc;
        _linkStyle.hover.textColor = Color.white;
        // 宽度用 GUIStyle.CalcSize 实测（比按字数估算准），再留点余量。
        // 宽度必须显式给：GUILayout.Button 默认会横向占满整行，那样第二个链接永远被挤到下一行。
        var content = new GUIContent($"<u>{text}</u>");
        float w = _linkStyle.CalcSize(content).x + 18f;
        if (GUILayout.Button(content, _linkStyle, GUILayout.Width(w), GUILayout.ExpandWidth(false))) {
            try {
                Application.OpenURL(url);
            } catch (System.Exception e) {
                Lg.Ex("打开链接失败: " + url, e);
            }
        }
    }

    private static void Save(UnityModManager.ModEntry modEntry) { }
}
