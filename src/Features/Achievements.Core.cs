// [split-of] Achievements.cs lines 64-700 body 12-588

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace LevelLibrary;

public static partial class Achievements {
    public static readonly List<AchievementDef> Defs = new List<AchievementDef> {
        new AchievementDef("ach_open",  "打开主界面", 1, "第一次打开主界面", "打开新世界的大门？"),
        new AchievementDef("ach_batch", "批量导入谱面", 2, "第一次使用批量导入谱面功能", "谱间宇宙"),
        new AchievementDef("ach_500",   "小牢", 2, "单个谱面游玩次数达到 500 次", "我一定能击破这个谱子"),
        new AchievementDef("ach_zhonglao","中牢", 3, "单个谱面游玩次数达到 1000 次", "你到底有多喜欢这个谱？"),
        new AchievementDef("ach_laoda", "牢大", 5, "单个谱面游玩次数达到 10000 次", "是什么支持你这么做的？时间吗？"),
        new AchievementDef("ach_palm",  "【掌中宇宙】", 5, "首次打开关卡编辑器打开谱面", "吾掌中，寰宇也"),
        new AchievementDef("ach_moon",  "月正向山倾", 5, "搜索月正向山倾", "月正向山倾"),
        // 彩蛋成就：在开发者页点「母鸡先生」那 6 次解锁（5 级，归"私人"）。
        // 点击彩蛋本身与成就 id（ach_popcat）保持不变，玩家已点过的次数仍有效。
        new AchievementDef("ach_popcat", "咯咯哒", 5, "点击开发者页的「母鸡先生」6 次", "咯咯哒咯咯哒咯咯哒"),
        // 第二个彩蛋：在开发者界面用键盘敲出 "bugbug" → 解锁（5 级，归"私人"）。
        // 注释是两行，必须放得下：成就卡片注释区宽 AchCardW-20、高 33 = 两行，
        // 每行都要窄于卡内宽才不会换行，再多一行就会压到下面的升级进度条。
        new AchievementDef("ach_ccb", "Coding Coding bug", 5, "在开发者界面输入 bugbug", "Coding Coding bug何尝不是ccb呢\n致敬传奇找bug人自语"),
        // Ziyu3 专属：玩家名字为 Ziyu3 的人点击登录进入界面时触发（归"私人"栏，见 CategoryOf）。
        // 名字与注释一字不改（含注释末尾句号）；触发点 PortalUI.ProfileDoLogin（本地昵称，不联网）。
        // 拿到之后身份被强制锁定：ForcedPlayerTitle / ApplyForcedTitle 会把个人页身份写死成它。
        new AchievementDef(AchZiyu3, "Ziyu3 BEST bug hunter", 5,
            "玩家名字为 Ziyu3 的人点击登录进入界面时触发", "你能寻找到任何漏洞，你是BEST hunter。"),
        // 「深度用户」的描述文字是需求原文，一字不改。
        new AchievementDef(DeepUser.AchId, DeepUser.AchName, DeepUser.AchLevel,
            "连续用谱面库打开 1000 次谱面（中途用游戏自己的方式打开会重新计数）",
            "我说了，我会让LevelLibrary越来越好，很感谢你的支持。"),
        // 「兴酣落笔摇五岳」：一个谱面第一次玩就通关（游玩次数为 1 时通关）。
        // 等级恒 4、可多次触发但不升级（见 RepeatLevel / LevelsUp）；归 MOD成就（见 CategoryOf）；注释留空。
        new AchievementDef(AchXinghan, "兴酣落笔摇五岳", 4, "一个谱面游玩次数为1时通关"),
        new AchievementDef("ach_village","新手村的大门", 2, "标判通过一个标记P17的谱面", "恭喜你，走进新手村的大门啦"),
        new AchievementDef("ach_start",  "新手村……未来的开始", 4, "严判标记P17的谱面", "历经万难，终成道果，你还会继续走多远呢？"),
        new AchievementDef("ach_galactic", "Galactic", 3, "宽判或标判或严判通过 G1-20 的谱面", "\"Galactic\"，意为星系的，你的内心能容纳多少光年之外的寂静与回响？"),
        new AchievementDef("ach_p10",    "P10", 2, "严判通关 P10 及以上的谱面", "你已经学会了点击键盘，现在去击破2026吧"),
        // 里程碑成就（严判通关某个谱面）与下面的无瑕成就是两套互相独立的成就。
        new AchievementDef("ach_thousand", "Thousand", 2, "总游玩次数达到 1000", "一千次了…………打什么谱子要用这么多次呢"),
        new AchievementDef("ach_100days","100days", 4, "累计登录天数达到 100 天", "100天了诶，你真的很喜欢这个游戏"),
        new AchievementDef("ach_earthyear","地球年", 5, "累计登录天数达到 365 天", "年年岁岁，岁岁年年"),
        new AchievementDef("ach_100hours","100Hours", 3, "累计游玩时间达到 100 小时", "玩了100小时啦，还会有多少个100H呢？"),
        new AchievementDef("ach_wan",    "【万】", 3, "总游玩次数达到 10000", "你真的很热爱这个游戏"),
        new AchievementDef("ach_heng",   "【持之以恒】", 4, "总游玩次数达到 100000", "十万……许多人不可企及的数字，你做到了"),
        new AchievementDef("ach_space",  "【列表空间】", 2, "列表谱面数达到 20 个", ""),
        new AchievementDef("ach_matrix", "【列表矩阵】", 3, "列表谱面数达到 200 个", ""),
        new AchievementDef("ach_puni",   "【谱面宇宙】", 4, "列表谱面数达到 2000 个", "全即一？"),
        new AchievementDef("ach_firstclear","首次通关谱面", 1, "宽判/标判/严判通关任意谱面", "你迈出了第一步"),
        new AchievementDef("ach_noob",   "打谱新手", 2, "通关 10 个不同谱面", "这10个谱子里都是你喜欢的吗"),
        new AchievementDef("ach_master", "打谱高手", 3, "通关 100 个不同谱子", "100个……你一定练了很久吧……"),
        new AchievementDef("ach_dragon", "龙币", 5, "通关 1000 个不同谱子", "1000个？！大佬教教我…………"),
        new AchievementDef("ach_universal","Universal", 4, "宽判或标判或严判通过 U1-20 的谱面", "\"Universal\"，意为宇宙的，你的技术已经超过这个宇宙百分之99的人了噢"),
        new AchievementDef("ach_persist", "坚持", 1, "单个谱面游玩次数超过 100 后仍未通关", "再坚持一下，说不定就过了呢？"),
        new AchievementDef("ach_patient", "耐心", 2, "单个谱面游玩次数超过 500 后仍未通关", "可能下一把就过了？"),
        new AchievementDef("ach_rest",   "要不……我们休息一会？", 3, "单个谱面游玩次数超过 1000 后仍未通关", "你真的很累了，休息一会吧"),
        new AchievementDef("ach_gu",     "【炼成坚持蛊】", 4, "单个谱面游玩次数超过 2000 后仍未通关", "逆流河上万仙退，失败不敌坚持泪……"),
        new AchievementDef("ach_myself", "我的世界，我自己定义", 1, "第一次自定义主题色", "个性化的旅程开始了"),
        new AchievementDef("ach_persona", "个性", 2, "头像和名字都设置完毕", "这就是我"),
        new AchievementDef("ach_shi",   "世一开", 5, "完美无瑕 U15 及以上的谱面", "开开？！"),
        new AchievementDef("ach_pcollect", "P级全收集", 3, "解锁 P1-20 全部里程碑成就", "你一定是收集癖吧……"),
        new AchievementDef("ach_gcollect", "G级全收集", 4, "解锁 G1-20 全部里程碑成就", "你居然做到了这一步？！"),
        new AchievementDef("ach_ucollect", "U级全收集", 5, "解锁 U1-20 全部里程碑成就", "没关就是开了？"),
        new AchievementDef("ach_daily_checkin", "打卡！", 1, "打开关卡编辑器5秒后触发", "每日打卡", true),
        new AchievementDef("ach_daily_study", "今日打谱大学习", 1, "每日随意判定通关第一个谱子时触发", "每日学习", true),
        new AchievementDef("ach_daily_finish", "完成谱面！", 1, "今日打谱大学习触发后，每次从第一格（未用检查点）通关触发", "", true),
        // 触发条件里的中文括号连同括号内文字已删掉（原来写的是"…主界面时触发（等开界面卡顿结束后才提示）"）
        new AchievementDef("ach_daily_today", "今天好！！", 1, "每日第一次打开谱面库主界面时触发", "今天也要好好的", true),
        // 可重复触发：等级与注释由触发次数决定（Achievements.ApplyTriggerDisplay 每次刷新时写入）
        new AchievementDef("ach_pp1", "恭喜你：PP-1", 1, "每次通关时除完美外只有一个稍快或稍慢时触发（可重复触发）", "触发次数：0"),
        // 可重复模板：死在"完成度 99%"的地方就 +1 次（触发点见 AutoFeatures.OnPlayerDied）。
        // 判据里必须含"没用检查点"（checkpointsUsed <= 0，每局重置的计数器），
        // 但不能把注释里的"（可重复触发）"删掉 —— 它由 ApplyTriggerDisplay 每次刷新时改写。
        new AchievementDef("ach_death99", "啊嘞，怎么死在这了", 1,
            "在完成度 99% 的地方死亡，且这一局必须从第一格开始（没用检查点）时触发（可重复触发）", "触发次数：0"),
        // 彩蛋：Orca（世界首个高鲸法阵，物量 848 / 音乐 Orca.ogg）。进图、打到雪花段第一个变速、通关
        // 三个时点大肥鱼各说一句，进图那一刻解锁。判定见 AutoFeatures.Hooks 的 Orca 彩蛋段。
        new AchievementDef("ach_orca", "这是我家？！", 1,
            "打开 Orca（音乐 Orca.ogg、物量 848）时触发", "回家的路终于找到了"),
    };

    // 两套独立的成就，等级都按标记编号分段：
    // 里程碑（P1~P20 / G1~G20 / U1~U20 首次严判击破）：P1-10=1 P11-20=2 G1-10=2 G11-20=3 U1-10=4 U11-20=5
    // 无瑕（60 个「{等级}完美无瑕！！」，条件是该谱面 Xacc=100 且判定只有完美，稍快/稍慢/太早/太晚都不算）：
    // P1-10=1 P11-20=2 G1-10=3 G11-20=4 U1-20=5
    static Achievements() {
        string[] tiers = { "P", "G", "U" };
        // P 里程碑的 20 个名字与注释是自定义的，只改名字和注释，触发条件与等级不动
        string[] pNames = { "初窥门径","循序渐进","渐入佳境","熟能生巧","驾轻就熟","百炼成钢","行云流水","登堂入室","炉火纯青","技冠群雄","更上一层","披荆斩棘","迎难而上","百折不挠","水滴石穿","破茧成蝶","登峰造极","一骑绝尘","所向披靡","初入星海" };
        string[] pComments = { "原来，这就是开始","一步一步来","好像开始有点感觉了","多练，总会有回报","已经不再陌生","反复淬炼，只为这一刻","指尖已经开始跟上节奏","你已经不是门外汉了","技术逐渐成形","这一关，可没那么简单","山外还有山","前路从来不会平坦","越难，越要试试","再来一次","坚持本身就是一种力量","终于跨过了那道坎","新手村已经留不住你了","身后已经没人跟得上","还有什么能阻挡你？","恭喜，你已经走得很远了" };
        for (int ti = 0; ti < tiers.Length; ti++) {
            string t = tiers[ti];
            for (int n = 1; n <= 20; n++) {
                string m = t + n;
                int lv;
                if (t == "P") lv = n <= 10 ? 1 : 2;
                else if (t == "G") lv = n <= 10 ? 2 : 3;
                else lv = n <= 10 ? 4 : 5;
                string name = Lang.TrFmt("{0}里程碑", m);
                string comment = Lang.TrFmt("{0}里程碑", m);
                if (t == "P") { name = pNames[n - 1]; comment = pComments[n - 1]; }
                Defs.Add(new AchievementDef("ach_milestone_" + m, name, lv, Lang.TrFmt("首次严判击破 {0} 标记的谱子", m), comment));

                // ---- 无瑕成就（60 个，独立于里程碑）----
                // 等级：P: 1-10→1、11-20→2 ｜ G: 1-10→3、11-20→4 ｜ U: 全部→5
                int flv;
                if (t == "P") flv = n <= 10 ? 1 : 2;
                else if (t == "G") flv = n <= 10 ? 3 : 4;
                else flv = 5;
                // 注释按作者要求：P=无瑕之路的开始、G=渐入佳境、U=登峰造极
                string fcomment = t == "P" ? "无瑕之路的开始" : (t == "G" ? "渐入佳境" : "登峰造极");
                // 显示文案只写 "Pure perfect！！"（再长卡片上要占三行）；
                // 判定规则本身没变，仍见 RecordIsFlawless
                Defs.Add(new AchievementDef("ach_flawless_" + m, Lang.TrFmt("{0}完美无瑕！！", m), flv,
                    "Pure perfect！！", fcomment));
            }
        }
    }

    private static readonly Dictionary<string, long> _unlocked = new Dictionary<string, long>();   // id -> unix时间戳
    private static readonly HashSet<string> _unseen = new HashSet<string>();   // 新解锁未查看
    private static readonly Dictionary<string, string> _milestoneChart = new Dictionary<string, string>();   // 里程碑id -> 触发该成就的谱面名
    private static readonly Dictionary<string, string> _dailyDone = new Dictionary<string, string>();   // 每日成就id -> 触发日期(yyyy-MM-dd)
    private static string _dailyPath;
    private static string _path;

    /// <summary>解锁成就会触发（PortalUI 用来弹右下角提示）</summary>
    public static event Action<AchievementDef> OnUnlocked;

    /// <summary>把 197 个官方通关成就加进 Defs，只加一次。
    /// 必须由 Load 调用、用 bool 守一道：静态构造器跑在类型初始化时，世界数据 WorldData.dict 那时候未必就绪。</summary>
    private static bool _officialDefsAdded;
    private static void AddOfficialDefsOnce() {
        if (_officialDefsAdded) return;
        _officialDefsAdded = true;
        try {
            int n = 0;
            foreach (var w in OfficialLevels.Worlds()) {
                if (w == null || w.SubIds == null || w.SubIds.Count == 0) continue;
                foreach (var lid in w.SubIds) {
                    if (string.IsNullOrEmpty(lid)) continue;
                    string id = OfficialAchId(lid);
                    if (GetDef(id) != null) continue;                 // 别重复加
                    string lname = OfficialLevels.NameOf(lid);
                    if (string.IsNullOrEmpty(lname)) lname = lid;
                    // 名字/条件是拼的，必须走 TrFmt 模板：lname 是官方关卡名，
                    // 它自己已在词条表里（Lang.Official），显示时走 prefix 会再翻一次
                    Defs.Add(new AchievementDef(id, Lang.TrFmt("{0} 通关", lname),
                        OfficialAchLevel(w.Key, w.Island, w.IsDLC),
                        Lang.TrFmt("通关官方关卡 {0}", lid),
                        Lang.TrFmt("世界 {0}", string.IsNullOrEmpty(w.FinalId) ? w.Key : w.FinalId)));
                    n++;
                }
            }
            Lg.Info("官方成就已生成：" + n + " 个（每个官谱一个通关成就）");
        } catch (System.Exception e) {
            Lg.Ex("生成官方成就失败", e);
        }
    }

    public static void Load(string modPath) {
        AddOfficialDefsOnce();  // 先把官方成就补进 Defs（放最前面，后面读档才能对上）
        _path = Path.Combine(modPath, "achievements.txt");
        _countPath = Path.Combine(modPath, "ach_counts.txt");   // 可重复成就的触发次数
        _flawlessMigrationPath = Path.Combine(modPath, "ach_migration.txt");   // 一次性迁移标记
        _clickPath = Path.Combine(modPath, "ach_clicks.txt");   // 彩蛋点击计数（咯咯哒）
        LoadClicks();
        _checkinHistLoaded = false;   // 打卡历史天数缓存作废（每次 Load 重新算）
        LoadCounts();
        _unlocked.Clear();
        _unseen.Clear();
        _dailyPath = Path.Combine(modPath, "daily_ach.txt");
        _dailyDone.Clear();
        if (File.Exists(_dailyPath)) {
            try {
                foreach (var line in File.ReadAllLines(_dailyPath)) {
                    var p = line.Split('\t');
                    if (p.Length >= 2 && p[0].Trim().Length > 0) _dailyDone[p[0].Trim()] = p[1].Trim();
                }
            } catch (Exception e) { Lg.Ex("加载每日成就失败", e); }
        }
        if (!File.Exists(_path)) { ApplyForcedTitle(); return; }
        try {
            foreach (var raw in File.ReadAllLines(_path)) {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split('\t');
                if (parts.Length >= 2 && !_unlocked.ContainsKey(parts[0]))
                    _unlocked[parts[0]] = long.Parse(parts[1]);
                if (parts.Length >= 3) _milestoneChart[parts[0]] = parts[2];   // 里程碑：触发谱面名
            }
        } catch (Exception e) {
            Lg.Ex("加载成就失败", e);
        }
        // 读档之后把"身份锁定"归一化一遍：玩家手改 settings.txt 或老版本写过别的身份，这里统一扳回来
        ApplyForcedTitle();
    }

    public static void Save() {
        if (string.IsNullOrEmpty(_path)) return;
        try {
            var sb = new List<string>();
            foreach (var kv in _unlocked) sb.Add(kv.Key + "\t" + kv.Value + (_milestoneChart.TryGetValue(kv.Key, out var cn) ? ("\t" + cn) : ""));
            File.WriteAllLines(_path, sb.ToArray());
        } catch (Exception e) {
            Lg.Ex("保存成就失败", e);
        }
    }

    /// <summary>每日成就触发（每天一次）：弹提示+音效，不写入永久解锁、列表不显示</summary>
    public static bool TriggerDaily(string id, string name) {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_dailyDone.TryGetValue(id, out var d) && d == today) return false;
        _dailyDone[id] = today;
        SaveDaily();
        // 打卡：累计次数 +1（上面已经挡住"今天打过"，所以这里一天只 +1 次）。
        // 复用可重复成就那张次数表（ach_counts.txt），但打卡不是可重复成就 ——
        // 它不写永久解锁、不参与等级/进度条，只借这张表存一个累计数给详情区显示。
        if (id == AchDailyCheckin) {
            long cur = GetTriggerCount(id);
            // 首次记账先补上"历史上开过编辑器但没计数"的那些天（不含今天），再 +1；
            // 不补的话明天第一次记账会从 0+1=1 开始，数字反而变小
            if (cur <= 0) cur = CheckinHistoricalDays();
            _triggerCount[id] = cur + 1;
            SaveCounts();
            Lg.Info("打卡：累计打卡 " + _triggerCount[id] + " 次（写进 " + _countPath + "）");
        }
        var def = GetDef(id);
        if (def == null) def = new AchievementDef(id, name, 1, "", "");
        try { OnUnlocked?.Invoke(def); } catch (Exception e) { Lg.Ex("每日成就提示异常", e); }
        return true;
    }

    /// <summary>每日「打卡！」的成就 id（触发点：设置页那套"编辑器里待够 5 秒"的检测）</summary>
    public const string AchDailyCheckin = "ach_daily_checkin";

    /// <summary>历史"打开谱面进编辑器"的天数（不含今天），给打卡次数补初始值用。
    /// 依据：打卡的触发点就是 ConfirmOpen，它同时会写一行 playlog，
    /// 所以 playlog 里"有记录的自然日" = 那天开过编辑器 = 那天打过卡（计数器上线前的日子只能这样估）。
    /// 排除今天是因为今天的 +1 由计数器自己加，避免重复。结果缓存一次，Load() 时作废。</summary>
    private static long _checkinHist;
    private static bool _checkinHistLoaded;
    private static long CheckinHistoricalDays() {
        if (_checkinHistLoaded) return _checkinHist;
        _checkinHistLoaded = true;
        try {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            var days = new HashSet<string>();
            foreach (var t in LevelScanner.GetPlayLogTimes()) {
                string d = DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("yyyy-MM-dd");
                if (d != today) days.Add(d);
            }
            _checkinHist = days.Count;
        } catch (Exception e) {
            _checkinHist = 0;
            Lg.Ex("统计历史打卡天数失败", e);
        }
        return _checkinHist;
    }

    /// <summary>累计打卡次数。
    /// 没记过账时用历史天数（playlog 里开过编辑器的自然日，不含今天）+ 今天是否已打卡补出来：
    /// 计数器是后来才加的，而"今天这次打卡"可能发生在上线之前（daily_ach.txt 里今天已打过、
    /// ach_counts.txt 里却查不到），当天又不会再触发第二次，否则会显示成 0、卡片上说"还没打过卡"。
    /// 第一次真正记账时也按同一个基数起步，避免"明天从 1 开始"把数字变小。</summary>
    public static long CheckinCount() {
        long n = GetTriggerCount(AchDailyCheckin);
        if (n > 0) return n;
        return CheckinHistoricalDays() + (DailyDoneToday(AchDailyCheckin) ? 1 : 0);
    }

    /// <summary>详情区那行"数据文字"（目前只有打卡次数，别的成就返回空串 = 没有额外数据）。
    /// 单独一个函数是因为打卡详情要显示次数统计，且写在注释位（那一行的"注释"标签已去掉）。
    /// 也用在同一张卡的遮罩状态下：次数是玩家自己的数据、不算剧透，遮罩时照样显示。</summary>
    public static string DetailStatText(string id) {
        if (id != AchDailyCheckin) return "";
        long n = CheckinCount();
        // 这两句会进成就卡片的详细信息区，必须在这里就翻好、走 {0} 模板
        return n > 0 ? Lang.TrFmt("累计打卡 {0} 次", n)
                     : Lang.Tr("还没打过卡：打开关卡编辑器待满 5 秒就算一次");
    }

    // ---------- 彩蛋点击计数（开发者页点字那类）----------
    // 「咯咯哒」：在开发者页点「母鸡先生」那几个字 6 次解锁。
    // 不能借用可重复成就那张次数表（ach_counts.txt）：那边的 ApplyTriggerDisplay 会按次数改写等级和注释，
    // 彩蛋会被写成"触发次数：N（几级）"。所以自己一个小文件（ach_clicks.txt：id \t 次数），只做"够了就解锁"。
    public const string AchPopCat = "ach_popcat";

    private static readonly Dictionary<string, int> _clicks = new Dictionary<string, int>();
    private static string _clickPath;

    /// <summary>彩蛋已点击次数（没记过就是 0）</summary>
    public static int GetClickCount(string id) {
        return (!string.IsNullOrEmpty(id) && _clicks.TryGetValue(id, out var n)) ? n : 0;
    }

    /// <summary>点一次彩蛋元素：计数 +1 → 落盘 → 够次数就解锁，返回新的计数（调用方可用它做提示）。
    /// 每次点击都打一行日志，方便确认"是不是真的点到了"。</summary>
    public static int RegisterClick(string id, int need) {
        if (string.IsNullOrEmpty(id)) return 0;
        int n = GetClickCount(id) + 1;
        _clicks[id] = n;
        SaveClicks();
        Lg.Info("彩蛋点击：" + id + " " + n + " / " + need);
        if (n >= need && !IsUnlocked(id)) {
            Lg.Info("彩蛋达成：" + id + "（共点了 " + n + " 次）→ 解锁成就");
            TryUnlock(id);
        }
        return n;
    }

    private static void LoadClicks() {
        _clicks.Clear();
        if (string.IsNullOrEmpty(_clickPath) || !File.Exists(_clickPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_clickPath)) {
                var p = line.Split('\t');
                if (p.Length >= 2 && p[0].Trim().Length > 0 && int.TryParse(p[1].Trim(), out int n)) _clicks[p[0].Trim()] = n;
            }
        } catch (Exception e) { Lg.Ex("加载彩蛋点击次数失败", e); }
    }

    private static void SaveClicks() {
        if (string.IsNullOrEmpty(_clickPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _clicks) lines.Add(kv.Key + "\t" + kv.Value);
            File.WriteAllLines(_clickPath, lines);
        } catch (Exception e) { Lg.Ex("保存彩蛋点击次数失败", e); }
    }

    // ---------- 彩蛋：键盘输入暗号（开发者界面敲 bugbug）----------
    // 「Coding Coding bug」：在开发者界面用键盘敲出 "bugbug" 就解锁，5 级，跟「月正向山倾」同在"私人"分类。
    // 为什么要一个缓冲：敲进来的是逐帧的零散字符（一帧可能 0 个、也可能好几个），而暗号是一串，
    // 得自己攒一条"最近敲进来的字符"的尾巴，每帧判一次尾巴。
    // 为什么两路来源：中文输入法会把 inputString 吞掉，而 ~ 那类键反倒只有 inputString 拿得到，
    // 所以上层（PortalUI.TickDevEaster）两路都备着：优先 inputString，它这一帧一个可用字符都没给才退回按键扫描。
    public const string AchCCB = "ach_ccb";
    private const string CcbSecret = "bugbug";   // 暗号
    private const int CcbBufMax = 24;            // 缓冲上限（只留最近这些字符：暗号 + 手滑多敲几个也还认得出）
    private static string _ccbBuf = "";

    /// <summary>清掉暗号缓冲。切走开发者页 / 关面板时必须调用，
    /// 否则"上午敲了 bug、下午又敲了 bug"会被拼成暗号，跟"在开发者界面输入"的本意不符。</summary>
    public static void ResetCcbTyping() { _ccbBuf = ""; }

    /// <summary>把这一帧敲进来的字符喂进来。
    /// 返回 = 这一帧有没有收到"可用的字符"（consumed）；unlocked = 这次真的敲出暗号并解锁了。
    /// 两个值必须分开返回：上层要靠 consumed 判断"inputString 是不是被输入法吞了"，
    /// 吞了才换按键扫描再喂一次（合成一个 bool 就分不清"没敲键"和"敲了但没中"）。</summary>
    public static bool FeedCcbTyping(string chars, out bool unlocked) {
        unlocked = false;
        if (string.IsNullOrEmpty(chars)) return false;
        bool consumed = false;
        for (int i = 0; i < chars.Length; i++) {
            char c = chars[i];
            if (c == '\b') {                       // 退格：删掉一个（敲错了自己改回来）
                if (_ccbBuf.Length > 0) _ccbBuf = _ccbBuf.Substring(0, _ccbBuf.Length - 1);
                consumed = true;
                continue;
            }
            if (c == '\n' || c == '\r') { _ccbBuf = ""; consumed = true; continue; }   // 回车 = 重来
            if (!IsAsciiWordChar(c)) continue;     // 控制字符 / 中文输入法的候选字一律丢掉（不算"敲了"）
            consumed = true;
            _ccbBuf += char.ToLowerInvariant(c);   // 大小写不敏感（BUGbug 也认）
            if (_ccbBuf.Length > CcbBufMax) _ccbBuf = _ccbBuf.Substring(_ccbBuf.Length - CcbBufMax);
            if (_ccbBuf.EndsWith(CcbSecret, StringComparison.Ordinal)) {
                _ccbBuf = "";                      // 命中即清空：免得接着敲又被算一次
                unlocked = true;
                break;
            }
        }
        if (unlocked) {
            bool first = TryUnlock(AchCCB);
            Lg.Info("彩蛋暗号：开发者界面敲出 \"" + CcbSecret + "\" → "
                + (first ? "解锁「Coding Coding bug」" : "成就早已解锁，本次不重复"));
        }
        return consumed;
    }

    private static bool IsAsciiWordChar(char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }

    // ---------- 成就锁定的身份（Ziyu3 BEST bug hunter）----------
    // 身份 = 个人页「欢迎回来，身份，用户名」中间那个（settings: PlayerTitle）。
    // 拿到成就之后：当场写进去；每次读档（Load 末尾）归一化一遍；管理员指令也改不动它
    // （Extra 那两个指令会先问 ForcedPlayerTitle）。这就是"强制覆盖锁定"，不是只改一次。
    public const string AchZiyu3 = "ach_ziyu3";
    public const string Ziyu3Title = "BEST bug hunter";

    /// <summary>身份被成就锁定时的值；没锁返回 null。</summary>
    public static string ForcedPlayerTitle() {
        try { return IsUnlocked(AchZiyu3) ? Ziyu3Title : null; } catch { return null; }
    }

    /// <summary>把身份按"成就锁"归一化：锁了就强制写回那个值，没锁什么都不做。</summary>
    public static void ApplyForcedTitle() {
        try {
            string f = ForcedPlayerTitle();
            if (string.IsNullOrEmpty(f)) return;
            if (PortalUI.PlayerTitle == f) return;
            PortalUI.PlayerTitle = f;
            try { AutoFeatures.Save(); } catch { }
            Lg.Info("身份已被成就锁定 → " + f + "");
        } catch (System.Exception e) { Lg.Ex("归一化身份锁定失败", e); }
    }

    private static void SaveDaily() {
        if (string.IsNullOrEmpty(_dailyPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _dailyDone) lines.Add(kv.Key + "\t" + kv.Value);
            File.WriteAllLines(_dailyPath, lines);
        } catch (Exception e) { Lg.Ex("保存每日成就失败", e); }
    }

    /// <summary>今日打谱大学习今天是否已触发</summary>
    public static bool DailyCheckinDoneToday() {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        return _dailyDone.TryGetValue("ach_daily_study", out var d) && d == today;
    }

    /// <summary>某个每日成就今天是否已经触发过（给"今天好！！"这类每日成就判断用）</summary>
    public static bool DailyDoneToday(string id) {
        if (string.IsNullOrEmpty(id)) return false;
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        return _dailyDone.TryGetValue(id, out var d) && d == today;
    }

    /// <summary>完成谱面！（今日打谱大学习已触发后，每次从第一格通关触发；不限制次数，不写入永久解锁）</summary>
    public static void TriggerFinish() {
        if (!DailyCheckinDoneToday()) return;
        var def = GetDef("ach_daily_finish");
        if (def == null) def = new AchievementDef("ach_daily_finish", "完成谱面！", 1, "", "");
        try { OnUnlocked?.Invoke(def); } catch (Exception e) { Lg.Ex("完成谱面提示异常", e); }
    }

    public static bool IsUnlocked(string id) => _unlocked.ContainsKey(id);

    // ---------- 可重复触发的成就（触发次数决定等级和注释）----------
    // 「恭喜你：PP-1」：每次通关时"除完美外只有一个稍快或稍慢"就 +1 次，可以无限重复。
    // 等级由次数决定：1-10=1级 11-50=2级 51-100=3级 101-200=4级 201-500=5级。
    /// <summary>可重复成就 id -> 已触发次数</summary>
    private static readonly Dictionary<string, long> _triggerCount = new Dictionary<string, long>();
    private static string _countPath;

    /// <summary>「恭喜你：PP-1」的成就 id（触发点见 AutoFeatures 通关记录处）</summary>
    public const string AchPP1 = "ach_pp1";

    /// <summary>「啊嘞，怎么死在这了」的成就 id（触发点见 AutoFeatures.OnPlayerDied：死在完成度 99% 处）</summary>
    public const string AchDie99 = "ach_death99";

    /// <summary>「这是我家？！」（Orca 彩蛋）的成就 id（触发点见 AutoFeatures.Hooks 的 Orca 段）</summary>
    public const string AchOrca = "ach_orca";

    /// <summary>「兴酣落笔摇五岳」的成就 id。
    /// 条件（"游玩次数为1"= 这个谱面第一次玩就通关，中途没死过）不能用 GetPlays() == 1 直接判：
    /// 我们的计数在"打开 / 编辑器就绪 / 死亡 / 通关"各 +1（见 ConfirmOpen / OnEditorReady / OnPlayerDied），
    /// 真·第一次通关时它早就 ≥3 了。改成进谱面那一刻记一份"进它之前是不是 0 次"（NoteEntryPlays）。
    /// 等级恒 4 不升级（RepeatLevel 对这个 id 恒返 4，LevelsUp 返回 false）；归 MOD成就；
    /// 可多次触发，每换一个"第一次玩"的谱面各算一次，次数存 ach_counts.txt。
    /// 触发点：AutoFeatures 通关记录段 PP-1 那条旁边，在 validClear 里面，自动播放不算。</summary>
    public const string AchXinghan = "ach_xinghan";

    /// <summary>官方成就的前缀：ach_official_&lt;关卡id&gt;（每个官谱一个通关成就）。
    /// 通关条件：任意通关一次就算（含用检查点的）；等级按世界难度分级。</summary>
    public const string AchOfficialPrefix = "ach_official_";

    /// <summary>官方成就的 id（给解锁处用）。</summary>
    public static string OfficialAchId(string levelId) { return AchOfficialPrefix + levelId; }

    /// <summary>官方成就的等级，按世界难度分级：基础 1 / 进阶·附加 2 / 皇冠岛·愚人节彩蛋 3 /
    /// Muse Dash·CR2024 4 / DLC(Vega) 5。判据用世界号 + 岛名（见 OfficialLevels.WorldInfo），
    /// 分级表只写在这一处，改的时候只动这里。</summary>
    public static int OfficialAchLevel(string worldKey, string island, bool isDlc) {
        if (isDlc) return 5;                                     // DLC 世界（Vega）
        string k = (worldKey ?? "").Trim().ToUpperInvariant();
        string isl = island ?? "";
        if (isl.Equals("MuseDash", System.StringComparison.OrdinalIgnoreCase)
            || isl.Equals("CR2024", System.StringComparison.OrdinalIgnoreCase)) return 4;
        if (isl.Equals("Crown", System.StringComparison.OrdinalIgnoreCase)) return 3;
        // 愚人节＆彩蛋那批（1J~6J / BJ / 1EX / TP / XP / TN）—— 带字母的世界号
        if (k.IndexOf('J') >= 0 || k.StartsWith("EX") || k.StartsWith("TP") || k.StartsWith("XP") || k.StartsWith("TN")) return 3;
        int n = 0;
        if (int.TryParse(k, out n)) {
            if (n >= 1 && n <= 6) return 1;                      // 基础关卡 1~6
            return 2;                                            // 进阶关卡 7~12
        }
        if (isl.Equals("Xtra", System.StringComparison.OrdinalIgnoreCase)) return 2;   // 附加关卡
        return 2;                                                // 兜底：当进阶算
    }

    /// <summary>按触发次数算等级：1-10→1级 11-50→2级 51-100→3级 101-200→4级 201-500→5级（500 以上维持 5 级）</summary>
    public static int PP1Level(long count) {
        if (count <= 0) return 1;
        if (count <= 10) return 1;
        if (count <= 50) return 2;
        if (count <= 100) return 3;
        if (count <= 200) return 4;
        return 5;
    }

    /// <summary>可重复成就的等级只有两套：PP-1 的区间档 + 「兴酣落笔摇五岳」的恒 4 级。</summary>
    public static int RepeatLevel(string id, long count) {
        if (id == AchXinghan) return 4;          // 恒 4 级：次数照加、等级不动
        return PP1Level(count);
    }

    /// <summary>这个可重复成就会不会"按次数升级"？决定卡片上要不要画升级进度条、要不要写"再 N 次升 X 级"。
    /// 「兴酣落笔摇五岳」不升级，别给它画进度条、也别写升级提示。</summary>
    public static bool LevelsUp(string id) {
        return id != AchXinghan;
    }

    /// <summary>下一级还差多少次的提示（已达 5 级返回空）。</summary>
    private static string PP1NextHint(long count) {
        if (count <= 10) return Lang.TrFmt("（再 {0} 次升 {1} 级）", 11 - count, 2);
        if (count <= 50) return Lang.TrFmt("（再 {0} 次升 {1} 级）", 51 - count, 3);
        if (count <= 100) return Lang.TrFmt("（再 {0} 次升 {1} 级）", 101 - count, 4);
        if (count <= 200) return Lang.TrFmt("（再 {0} 次升 {1} 级）", 201 - count, 5);
        return "";
    }

    /// <summary>取某个可重复成就已经触发过多少次。</summary>
    public static long GetTriggerCount(string id) {
        return (!string.IsNullOrEmpty(id) && _triggerCount.TryGetValue(id, out var n)) ? n : 0L;
    }

    /// <summary>把"次数 → 等级/注释"写进 Defs 里那一个实例（成就列表每次刷新都会调用）。
    /// 注释按用户要求写触发次数；顺带带上当前等级和升级还差几次。</summary>
    public static void ApplyTriggerDisplay(string id) {
        var d = GetDef(id);
        if (d == null) return;
        long n = GetTriggerCount(id);
        if (n <= 0) return;
        int lv = RepeatLevel(id, n);
        d.Level = lv;
        // 不升级的那条（「兴酣落笔摇五岳」）不改注释：它本来就不升级，
        // 写"（4 级）（再 X 次升 5 级）"是骗人；次数由卡片自己那行「触发次数 N」显示。
        if (!LevelsUp(id)) return;
        d.Comment = Lang.TrFmt("触发次数：{0}　（{1} 级）", n, lv) + PP1NextHint(n);
    }

    /// <summary>可重复成就触发一次：次数 +1 → 更新等级/注释 → 落盘 → 提示（可重复触发的成就每次都提示）</summary>
    public static bool TriggerRepeat(string id) {
        var d = GetDef(id);
        if (d == null) return false;
        long n = GetTriggerCount(id) + 1;
        _triggerCount[id] = n;
        // 第一次触发记进永久解锁（这样它才会出现在成就列表里），之后只涨次数
        if (!_unlocked.ContainsKey(id)) _unlocked[id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ApplyTriggerDisplay(id);
        SaveCounts();
        Save();
        try { OnUnlocked?.Invoke(d); } catch (Exception e) { Lg.Ex("成就提示异常", e); }
        Lg.Info($"[LevelLib] 成就「{d.Name}」触发第 {n} 次（{d.Level} 级）");
        return true;
    }

    private static void LoadCounts() {
        _triggerCount.Clear();
        if (string.IsNullOrEmpty(_countPath) || !File.Exists(_countPath)) return;
        try {
            foreach (var line in File.ReadAllLines(_countPath)) {
                var p = line.Split('\t');
                if (p.Length >= 2 && p[0].Trim().Length > 0) {
                    long v;
                    if (long.TryParse(p[1].Trim(), out v)) _triggerCount[p[0].Trim()] = v;
                }
            }
        } catch (Exception e) { Lg.Ex("加载成就触发次数失败", e); }
    }

    private static void SaveCounts() {
        if (string.IsNullOrEmpty(_countPath)) return;
        try {
            var lines = new List<string>();
            foreach (var kv in _triggerCount) lines.Add(kv.Key + "\t" + kv.Value);
            File.WriteAllLines(_countPath, lines.ToArray());
        } catch (Exception e) { Lg.Ex("保存成就触发次数失败", e); }
    }

    /// <summary>清空所有成就记录（测试期用，洛洛说"好了"后不再调用）。</summary>
    public static void ResetAll() {
        _unlocked.Clear();
        _unseen.Clear();
        Save();
    }

}
