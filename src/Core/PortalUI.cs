// [split-of] PortalUI.cs lines 1-236
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

/// <summary>谱面库面板（UMM 按钮 / F9 快捷键打开；Esc 退出；字母索引 + 搜索 + 列表 + 重命名/打开位置 + 确认进入编辑器）</summary>
public static partial class PortalUI {
    private static Canvas _panelCanvas;
    private static Canvas _renameCanvas;
    private static bool _panelOpen;
    private static bool _closing;          // 退出动画播放中
    private static float _closeT;
    private const float CloseDuration = 0.3f;
    private static float _prevTimeScale = 1f;   // 面板打开前的 timeScale（打开时冻结游戏时间）
    private static float _savedScrollY;         // 上次关闭时列表滚动位置（重进恢复）

    private static string _search = "";
    private static string _selectedPath;       // 当前选中的谱面文件夹（backup 副本目录）
    private static Text _countText;            // 顶部统计行（谱面总数/通关数）
    private static readonly List<LevelEntry> _display = new();   // 当前列表显示的谱面（滚动定位用）
    private static readonly Dictionary<string, float> _groupOffsets = new();   // 字母标头在列表中的偏移
    private static readonly Dictionary<string, GameObject> _rowByPath = new();   // 谱面路径 -> 行对象（选中局部重绘用）
    private static bool _rowActionGuard;   // 行内子按钮点击守卫（防止冒泡触发行选中）
    private static readonly List<Text> _rainbowTexts = new();   // 满分 100 的彩虹文本（统一驱动，不给每行挂 Update 组件）

    // ---- 选中线框的颜色（列表 tab / 设置页导航通用，改这一处即可整体换色）----
    /// <summary>选中框线颜色：取当前主题色的 RGB + alpha 0.95，随主题色走。
    /// 必须是属性而不是 static readonly：属性在用到时才算，声明位置在 ThemeColor 之前也没问题
    /// （static readonly 会踩静态字段初始化顺序）。</summary>
    public static Color FrameLineColor {
        get { return new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 0.95f); }
    }

    /// <summary>实心选中块的填充色：主题色 RGB + alpha 0.92，随主题色走。
    /// 配深蓝黑文字 HighlightTextColor 才看得清（白字压在浅蓝上会糊）。
    /// 块必须在文字下面（先建块再建按钮，或 SetAsFirstSibling）—— 空心描边压在上面看不出来，实心块会盖住字。
    /// 改属性只能让新建的块对色，已画上去的必须 RegisterFillImage() 登记、由 RepaintThemeImages() 重刷。</summary>
    public static Color HighlightFillColor {
        get { return new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 0.92f); }
    }

    /// <summary>压在实心选中块上的文字颜色（深蓝黑，与字母定位条选中字同色）</summary>
    public static readonly Color HighlightTextColor = new Color(0.06f, 0.10f, 0.18f, 1f);

    // ---- 彩蛋：搜索"月正"/"月正向山倾" → 屏幕中央"月正NB" RGB 闪耀 8 秒 ----
    private static Text _easterText;
    private static float _easterTimer;

    // ---- 作者生日祝福（洛星橙 1月13日）----
    private static Canvas _birthdayCanvas;      // 生日弹窗
    private static bool _birthdayChecking;      // 检测已跑过（防每次 Tick 重复检查）
    private const int BirthdayMonth = 1;
    private const int BirthdayDay = 13;
    private const string BirthdayPrefKey = "LevelLib_BirthdayShownYear";  // 记录已弹过的年份，每年1/13首次弹一次

    // ---- 周报（每周一触发）----
    private static Canvas _weeklyCanvas;        // 周报弹窗
    private static bool _weeklyChecking;        // 检测已跑过
    private const string WeeklyPrefKey = "LevelLib_WeeklyReportShownWeek";  // 记录已弹过的周标识（年+周数，每周一次）
    private static readonly string[] _easterWords = { "月正", "月正向山倾" };

    private static void ShowEasterEgg() {
        if (_easterText == null) return;
        _easterText.text = Lang.Tr("月正NB关注月正谢谢喵喵喵\n感谢月正大人的帮助喵，谢谢喵");
        _easterText.gameObject.SetActive(true);
        _easterTimer = 8f;   // 持续 8 秒；再次搜索重新计时
    }

    // ---- 作者生日祝福（洛星橙 1月13日）----
    /// <summary>该弹生日祝福就弹（每年 1/13 首次打开面板弹一次；mod 目录放 debug_birthday.txt 可强制预览）</summary>
    private static void MaybeShowBirthday() {
        try {
            if (_birthdayChecking) return;
            _birthdayChecking = true;
            // 调试预览：mod 目录有 debug_birthday.txt 就无条件弹
            string debugFile = System.IO.Path.Combine(Main.ModEntry.Path, "debug_birthday.txt");
            if (File.Exists(debugFile)) {
                ShowBirthdayDialog();
                return;
            }
            var now = System.DateTime.Now;
            // 只在生日当天弹
            if (now.Month != BirthdayMonth || now.Day != BirthdayDay) return;
            // 每年首次打开才弹（PlayerPrefs 记年份，跨年重置）
            int shownYear = PlayerPrefs.GetInt(BirthdayPrefKey, -1);
            if (shownYear == now.Year) return;
            PlayerPrefs.SetInt(BirthdayPrefKey, now.Year);
            PlayerPrefs.Save();
            ShowBirthdayDialog();
        } catch { }
    }

    // ---- 周报（每周一触发）----
    /// <summary>上周一零点（周一为一周起点）</summary>
    private static System.DateTime WeekStartMon(System.DateTime d) {
        // DayOfWeek: Sun=0 Mon=1 ... Sat=6；周一偏移 = (day+6)%7
        int off = ((int)d.DayOfWeek + 6) % 7;
        return d.Date.AddDays(-off);
    }

    /// <summary>检测是否该弹周报（每周一首次打开面板弹一次，汇总上周数据；mod 目录放 debug_weekly.txt 强制预览最近7天）</summary>
    private static void MaybeShowWeeklyReport() {
        try {
            if (_weeklyChecking) return;
            _weeklyChecking = true;
            var now = System.DateTime.Now;
            // 只在周一触发
            if (now.DayOfWeek != System.DayOfWeek.Monday) return;
            // 本周一日期作为"这一周"标识（每周一不同，避免重复弹）
            var thisMon = WeekStartMon(now);
            string weekKey = thisMon.ToString("yyyyMMdd");
            // 本周已弹过就不再弹（重启不重复，直到下周一）
            if (PlayerPrefs.GetString(WeeklyPrefKey, "") == weekKey) return;
            PlayerPrefs.SetString(WeeklyPrefKey, weekKey);
            PlayerPrefs.Save();
            ShowWeeklyReportDialog(thisMon, false);
        } catch { }
    }

    /// <summary>Unix 秒（本地时间）</summary>
    private static long ToUnix(System.DateTime d) => new System.DateTimeOffset(d).ToUnixTimeSeconds();

    /// <summary>秒数转可读时长（如 1小时23分 / 45分钟 / 32秒）</summary>
    private static string FormatPlayTime(long sec) {
        if (sec < 0) sec = 0;
        int h = (int)(sec / 3600);
        int m = (int)((sec % 3600) / 60);
        int s = (int)(sec % 60);
        if (h > 0) return Lang.TrFmt("{0}小时{1}分{2}秒", h, m, s);
        if (m > 0) return Lang.TrFmt("{0}分{1}秒", m, s);
        return Lang.TrFmt("{0}秒", s);
    }

    /// <summary>上周通关的最难谱（按 rank 取最高；无则 null）</summary>
    private static LevelEntry FindHardestEntry(long startSec, long endSec) {
        try {
            LevelEntry best = null;
            int bestRank = -1;
            foreach (var e in LevelScanner.Entries) {
                // PeekClearRecords：这里只问"本周有没有通关"（recs.Any 按时间戳筛），
                // 顺序用不上。整个循环每个谱面一次，原写法要为此拷一份 List 再排序。
                var recs = LevelScanner.PeekClearRecords(e.FolderPath);
                if (recs.Count == 0) continue;
                bool clearedLastWeek = recs.Any(r => r.Time >= startSec && r.Time <= endSec);
                if (!clearedLastWeek) continue;
                int rank = RankOf(MarkStore.Get(e.FilePath));
                if (rank > bestRank) { bestRank = rank; best = e; }
            }
            return best;
        } catch { return null; }
    }

    /// <summary>统计上周玩得最多的谱（返回条目 + 次数；无则 entry=null）</summary>
    private static LevelEntry FindMostPlayedEntry(long startSec, long endSec, out int count) {
        count = 0;
        try {
            var weekPlays = LevelScanner.GetWeekPlays(startSec, endSec);   // folder(绝对) -> count
            if (weekPlays.Count == 0) return null;
            var sorted = weekPlays.OrderByDescending(kv => kv.Value).ToList();
            var top = sorted[0];
            count = top.Value;
            return LevelScanner.Entries.FirstOrDefault(x => string.Equals(x.FolderPath, top.Key, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFullPath(x.FolderPath), top.Key, System.StringComparison.OrdinalIgnoreCase));
        } catch { return null; }
    }

    // ---- 周报称号（20 个六字称号，根据本周游玩天数/高难通关/游玩次数给予）----
    // 下标 0 最弱 → 19 最强，按综合活跃度取
    private static readonly string[] WeekTitles = {
        "本周完全摆烂", "咸鱼一条", "懒散开局", "轻度摸鱼", "悠闲玩家",
        "稳中求进", "渐入佳境", "小有所成", "热度上升", "积极向前",
        "手感渐热", "不骄不躁", "持之以恒", "勇攀高峰", "高难猎手",
        "爆肝达人", "全勤战神", "极限征服", "巅峰传说", "传奇引擎"
    };

    /// <summary>根据本周数据给评级下标（0~19），高难权重最大（通关最难谱是核心）</summary>
    private static int WeekTitleIndex(int days, int hard, int plays) {
        float d = Mathf.Clamp(days, 0, 7) / 7f;
        float h = Mathf.Clamp(hard, 0, 8) / 8f;
        float p = Mathf.Clamp(plays, 0, 80) / 80f;
        float score = d * 0.38f + h * 0.42f + p * 0.20f;
        return Mathf.Clamp(Mathf.RoundToInt(score * 19f), 0, 19);
    }

    /// <summary>取本周称号</summary>
    private static string GetWeekTitle(int days, int hard, int plays) {
        int idx = WeekTitleIndex(days, hard, plays);
        if (idx >= 0 && idx < WeekTitles.Length) return WeekTitles[idx];
        return "本周玩家";
    }

    /// <summary>历史严判击破统计：统计所有录入谱面中，有严判(Slot=2)通关记录的数量（P/G/U 各多少）+ 最高严判等级标</summary>
    private static void ComputeHistoryStats(out int pCount, out int gCount, out int uCount, out string highestRank) {
        pCount = 0; gCount = 0; uCount = 0; highestRank = "";
        int bestRankIdx = -1;
        try {
            foreach (var e in LevelScanner.Entries) {
                // 同上：只判"有没有严判记录"，顺序用不上。
                var recs = LevelScanner.PeekClearRecords(e.FolderPath);
                // 有严判(Slot=2)通关记录才算击破
                if (recs.Count == 0 || !recs.Any(r => r.Slot == 2)) continue;
                string mark = MarkStore.Get(e.FilePath) ?? "";
                if (string.IsNullOrEmpty(mark)) continue;
                int rank = RankOf(mark);
                if (rank < 0) continue;
                if (rank <= 19) pCount++;
                else if (rank <= 39) gCount++;
                else uCount++;   // 40-59（Q 图的 rank 也落在这一段，一并算 U）
                if (rank > bestRankIdx) { bestRankIdx = rank; highestRank = mark; }
            }
        } catch { }
    }

    // ---- 虚拟化（行对象池）：只渲染可视区行，滚动时复用 ----
}
