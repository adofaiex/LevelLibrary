// 设置页「扩展」主 tab：两个子 tab —— 「开发者」（指令输入框）与「大肥鱼」
// （宠物本体的开关/位置/大小/音效，实现见 `PortalUI.Pet.cs`）。
// 行全部走设置页那套 `MakeRowAt` + `_setRows` 显隐。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;      // Text / InputField

namespace LevelLibrary;

public static partial class PortalUI {

    /// <summary>extra 在设置页主导航里的下标（第 5 项；加它时导航列高度也要跟着加一格）</summary>
    public const int ExtraTabIndex = 5;   // 前面插了「去特效」主 tab，整体后移一位
    /// <summary>子 tab 下标（跟 `SubTabNames(ExtraTabIndex)` 的顺序一一对应）</summary>
    public const int ExtraSubDev = 0;      // 开发者
    public const int ExtraSubFish = 1;     // 大肥鱼

    /// <summary>extra 那个主导航标签（未选中时归彩虹循环 `TickRainbowTexts` 管、
    /// 选中时压成深蓝黑 —— 两处都有针对它的例外，见 PortalUI.SettingsPage.cs）</summary>
    private static Text _setExtraNavLabel;

    // ---------- 行引用 ----------
    private static InputField _extraCmdInput;

    /// <summary>建 extra 页（由 `BuildSettingsPagesInto` 最后调用，传进来的就是 `_setPageObjs[ExtraTabIndex]`）。
    /// 每行的分组号 = 该行所属子 tab 的下标，必须与 `SubTabNames(ExtraTabIndex)` 的顺序一致，
    /// 否则这一页会整页空白（不是只少这一行）。</summary>
    private static void BuildExtraPages(Transform page) {
        float y = 0f;

        // ---------- 子 tab 0：开发者 ----------
        var cmdRow = MakeRowAt(page, Lang.Tr("指令输入"), ref y, ExtraSubDev);
        _extraCmdInput = BuildTextInputRow(cmdRow, "输入指令后回车…", "", 620f, null);
        // 这个输入框登记给开发者页的重力字母当"别在这儿掉字"的判据：
        // 点它那一下必须原样落进输入框（bugbug / administrator developer 那套照旧），不许被掉落抢走。
        DevGravityRegisterInput(_extraCmdInput);
        if (_extraCmdInput != null) {
            _extraCmdInput.onEndEdit.AddListener(v => {
                string cmd = (v ?? "").Trim();
                if (cmd.Length == 0) return;
                _extraCmdInput.text = "";          // 提交后清空（命令框的常规行为）
                RunExtraCommand(cmd);
            });
        }

        // ---------- 子 tab 1：大肥鱼 ----------
        y = 0f;
        var f0 = MakeRowAt(page, Lang.Tr("显示宠物"), ref y, ExtraSubFish);
        Button showBtn = null;
        SettingsToggleRow(f0, out showBtn, null);
        BindToggle(showBtn, () => PetShow, v => { PetShow = v; PetApplySettings(); });

        var f1 = MakeRowAt(page, Lang.Tr("全局显示"), ref y, ExtraSubFish);
        Button globalBtn = null;
        SettingsToggleRow(f1, out globalBtn, null);
        BindToggle(globalBtn, () => PetGlobal, v => { PetGlobal = v; PetApplySettings(); });

        // 游玩时隐藏：判据用 `IsLevelPlaying()`（跟"游玩时禁用快捷键"同一套）；
        // 放在「全局显示」下面，这两条管的都是"什么时候露出来"。
        // 宠物不显示的时候，「游玩时隐藏」没有任何意义 —— 这一行跟着显示开关露 / 收（收藏页行增减同一套让位）
        var f1b = MakeRowAt(page, Lang.Tr("游玩时隐藏"), ref y, ExtraSubFish, 0f, () => PetShow);
        Button petHideBtn = null;
        SettingsToggleRow(f1b, out petHideBtn, null);
        BindToggle(petHideBtn, () => PetHideInGame, v => { PetHideInGame = v; PetApplySettings(); });

        var f2 = MakeRowAt(page, Lang.Tr("显示气泡"), ref y, ExtraSubFish);
        Button bubbleBtn = null;
        SettingsToggleRow(f2, out bubbleBtn, null);
        BindToggle(bubbleBtn, () => PetBubbleOn, v => { PetBubbleOn = v; PetApplySettings(); });

        // 模型：点一下换下一个（跟「随机发言」同一个套路）。只有大肥鱼一个可选时 PetNextModel
        // 返回当前值，点了只留一行日志、不换文字 —— 想加模型走下面那行「导入模型」。
        var fm = MakeRowAt(page, Lang.Tr("模型"), ref y, ExtraSubFish, RowGroupGap);
        var modelBtn = RowButton(fm, PetModelText() + " ▾", ItemColor, null);
        if (modelBtn != null) {
            var mrt = modelBtn.GetComponent<RectTransform>();
            modelBtn.onClick.AddListener(() => TogglePetPick(mrt));   // 点一下在按钮下面展开模型列表
        }
        _petModelText = modelBtn != null ? modelBtn.GetComponentInChildren<Text>() : null;

        // 导入模型：选一个 .llmd（zip：模型图 + 台词）→ 当场算几何 → 落进 pet_models\ → 加进上面那行可切。
        var fm2 = MakeRowAt(page, Lang.Tr("导入模型"), ref y, ExtraSubFish);
        RowButton(fm2, Lang.Tr("选择模型文件"), ItemColor, PickPetModelPack);

        var f5 = MakeRowAt(page, Lang.Tr("宠物大小"), ref y, ExtraSubFish, RowGroupGap);
        RowSlider(f5, 50f, 250f, PetScale * 100f, "%", v => { PetScale = v / 100f; PetApplyLayout(); AutoFeatures.Save(); });

        var f6 = MakeRowAt(page, Lang.Tr("音效"), ref y, ExtraSubFish, RowGroupGap);
        Button soundBtn = null;
        SettingsToggleRow(f6, out soundBtn, null);
        BindToggle(soundBtn, () => PetSoundOn, v => { PetSoundOn = v; PetApplySettings(); });

        var f7 = MakeRowAt(page, Lang.Tr("音量"), ref y, ExtraSubFish, 0f, () => PetSoundOn);
        RowSlider(f7, 0f, 100f, PetVol * 100f, "%", v => { PetVol = v / 100f; PetApplySettings(); });

        // 随机发言：点一下换一档的三态按钮（跟「屏保休眠时间」同一个套路），档位文案 = 无 / 每秒 1% / 每秒 0.1%。
        // 点一下立刻落盘；换档之后必须自己刷一次文字，那条行的控件归设置页管，不刷就还是旧档位的字。
        var f7c = MakeRowAt(page, Lang.Tr("随机发言"), ref y, ExtraSubFish, 0f, () => PetBubbleOn);
        var randBtn = RowButton(f7c, PetRandomSayText(), ItemColor, () => {
            CyclePetRandomSay();
            AutoFeatures.Save();          // 跟开关一样立刻落盘
            RefreshPetRandomSayText();
            Lg.Info("大肥鱼：随机发言 → " + PetRandomSayText());
        });
        _petRandomSayText = randBtn != null ? randBtn.GetComponentInChildren<Text>() : null;

        var f7b = MakeRowAt(page, Lang.Tr("自动吸附"), ref y, ExtraSubFish, RowGroupGap);
        Button snapBtn = null;
        SettingsToggleRow(f7b, out snapBtn, null);
        BindToggle(snapBtn, () => PetSnapEdge, v => { PetSnapEdge = v; AutoFeatures.Save(); });

        var f8 = MakeRowAt(page, Lang.Tr("重置位置与大小"), ref y, ExtraSubFish);
        RowButton(f8, Lang.Tr("重置"), ItemColor, () => {
            PetX = 0.90f; PetY = 0.16f; PetScale = 1f;
            PetApplySettings();
            RefreshSettingsPage();     // 大小那条滑条的滑块跟着回到新值
            Lg.Info("大肥鱼：位置/大小已重置");
        });
    }

    /// <summary>「模型」那一行的副标签（显示当前模型名，点一下换下一个已解锁的）。</summary>
    private static Text _petModelText;

    /// <summary>「随机发言」那一行的副标签（显示当前档位，点一下换下一档）。
    /// 跟 `_saverIdleBtnText` 同一个套路：行标签（左）＋ 控制件（右）；点按钮循环 无 → 每秒 1% → 每秒 0.1% → 无。
    /// 换档之后要自己把文字刷一遍 —— 环里那圈白点是 `ButtonHover`，它缓存的颜色取自那一刻的文字颜色。</summary>
    private static Text _petRandomSayText;

    // ---------- 死在完成度 99% 的地方 → 大肥鱼说一句 ----------
    // 七句都一字不改（省略号、「（？）」都是玩家原话，不要"顺手补全"成句号）。
    // 这七句按顺序一条一条说，不抽随机：随机的话连着两次可能同一句，而这组句子本来就短、"又说了一遍"会很明显。
    // 每死一次往后走一条、走完绕回开头（计数器只在内存里，不落盘）。这七句不进随机池，走 `PetSayLine` 的固定插播通道，
    // 不然点气泡会随机冒出「弱弱……」。
    // 触发点用 `AutoFeatures.OnPlayerDied` 里既有的那一处 99% 判据（跟成就「啊嘞，怎么死在这了」共用，
    // 百分比算法全项目只有一份，绝不另写一套——两处算法一漂开，就会出现"成就弹了、鱼不说话"）。
    private static readonly string[] PetDie99Lines = {
        "杂鱼…………",
        "笨蛋……",
        "弱弱……",
        "怎么会这样呢？",
        "不如我来打……",
        "下次一定（？）",
        "加油捏",
    };

    /// <summary>下次说第几条（每死一次 +1，只在内存里）</summary>
    private static int _petDie99Idx;

    /// <summary>死在 99% 时喊一句（触发点在 `AutoFeatures.OnPlayerDied` 的 99% 分支里）。
    /// 越界保护要有 —— 以后有人往表里加/删句子，游标不会指到表外。</summary>
    public static void PetSayOnDie99() {
        if (PetDie99Lines.Length == 0) return;
        int i = _petDie99Idx % PetDie99Lines.Length;
        _petDie99Idx = (i + 1) % PetDie99Lines.Length;
        PetSayLine(PetDie99Lines[i], "死在 99%");
    }

    /// <summary>执行一条指令。入口先搭好：输入框 + 回车 + 日志 + Toast，后面往里加 case 就行。</summary>
    private static void RunExtraCommand(string cmd) {
        Lg.Info("extra 指令：" + cmd);
        switch (cmd.ToLowerInvariant()) {
            case "help": case "?":
                ShowToast(Lang.Tr("已支持：help / clear-save …（其他指令待定义）"));
                return;
            case "clear-save":          // 自检用：把宠物位置/解锁状态复位（不动玩家数据）
                PetX = 0.90f; PetY = 0.16f; PetScale = 1f;
                PetApplySettings();
                ShowToast(Lang.Tr("宠物位置已复位"));
                return;
            // 管理员指令：给自己一个身份 —— 个人页「欢迎回来，身份，用户名」中间那个（settings: PlayerTitle，
            // 手填输入栏已删掉，现在只由指令设置）。
            // 身份被成就锁定时（拿到「Ziyu3 BEST bug hunter」）指令也改不动它，见 Achievements.ForcedPlayerTitle。
            case "administrator developer":
                SetTitleByCommand("开发者");
                return;
            case "administrator explorer":
                SetTitleByCommand("探索者");
                return;
            default:
                ShowToast(Lang.Tr("未识别的指令：") + cmd);
                return;
        }
    }

    /// <summary>管理员指令设身份（被成就锁定时拒绝覆盖，并把锁定值如实回显）</summary>
    private static void SetTitleByCommand(string want) {
        string locked = Achievements.ForcedPlayerTitle();
        if (!string.IsNullOrEmpty(locked)) {
            PlayerTitle = locked;              // 保险：万一被别处改过，这里也扳回来
            AutoFeatures.Save();
            ShowToast(Lang.Tr("身份已被成就锁定：") + locked);
            Lg.Info($"[LevelLib] 管理员指令想设「{want}」→ 被成就锁挡下，身份保持 {locked}");
            return;
        }
        PlayerTitle = want;
        AutoFeatures.Save();
        ShowToast(Lang.Tr("身份已设为：") + want);
        Lg.Info($"[LevelLib] 管理员指令：身份 → {want}");
    }
}
