// [split-of] Achievements.cs lines 1-63 body 3-53

// LevelLibrary 成就系统：成就定义数据 + 分类推导
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace LevelLibrary;

/// <summary>成就分类（成就页左侧的四个 tab）</summary>
public enum AchCategory { Milestone = 0, Mod = 1, Private = 2, Misc = 3 }

/// <summary>成就定义。必须是 class 不能是 struct：有"可重复触发、等级与注释随触发次数变化"的类型
/// （例如「恭喜你：PP-1」），需要就地修改 Defs 里那一个实例；struct 从 List 取出来是副本，改完写不回去。</summary>
public class AchievementDef {
    public string Id;
    public string Name;
    public int Level;     // 1~5
    public string Desc;
    public string Comment;   // 补充文本（显示在名字下方，不写“注释”二字）
    public bool IsDaily;   // 每日成就：不在列表显示，每天刷新
    public AchCategory Cat;   // 左侧分类 tab（按 id 推导，见 CategoryOf）
    public AchievementDef(string id, string name, int level, string desc, string comment = "", bool isDaily = false) {
        Id = id; Name = name; Level = level; Desc = desc; Comment = comment; IsDaily = isDaily;
        Cat = CategoryOf(id);
    }

    /// <summary>成就分类（成就页左侧四个 tab）。按 id 推导，这样 160 条定义的构造函数都不用改。
    /// 里程碑 = 严判击破 P/G/U 1-20（60）+ 无瑕 P/G/U 1-20（60）+ 官方成就（第 4 个子 tab）；MOD成就 = 用 mod 功能触发的；
    /// 私人 = 彩蛋与个人向 + 4 个每日；Misc = 剩下的积累类（枚举名仍叫 Misc，显示文案是"彩蛋成就"）。</summary>
    public static AchCategory CategoryOf(string id) {
        if (string.IsNullOrEmpty(id)) return AchCategory.Misc;
        if (id.StartsWith("ach_milestone_") || id.StartsWith("ach_flawless_") || id.StartsWith("ach_official_"))
            return AchCategory.Milestone;
        if (id.StartsWith("ach_daily_")) return AchCategory.Private;
        switch (id) {
            case "ach_open": case "ach_batch": case "ach_firstclear": case "ach_myself":
            case "ach_persona": case "ach_palm":
            case "ach_pcollect": case "ach_gcollect": case "ach_ucollect":
            case "ach_xinghan":
                return AchCategory.Mod;
            // 私人：月正向山倾（搜索彩蛋）· 咯咯哒（开发者页点「母鸡先生」6 次）· Coding Coding bug（开发者界面敲 bugbug）
            // · 啊嘞，怎么死在这了（完成度 99% 死亡，可重复）· Ziyu3 BEST bug hunter（名字叫 Ziyu3 的人点登录）
            case "ach_shi": case "ach_pp1": case "ach_moon": case "ach_popcat": case "ach_ccb": case "ach_death99":
            case "ach_ziyu3": case "ach_orca":
                return AchCategory.Private;
            default:
                return AchCategory.Misc;
        }
    }
}
