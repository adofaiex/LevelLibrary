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

/// <summary>PortalUI 的报表类弹窗：复活节彩蛋、作者生日祝福、周报（上周最难谱 / 玩得最多 / 本周数据 / 历史数据 / 称号）。
/// 周报的 canvas 排序值 96000 高于生日 95000，两者都是遮挡一切的全屏罩。</summary>
public static partial class PortalUI {

    private static void TickEasterEgg() {
        if (_easterTimer <= 0f) return;
        _easterTimer -= Time.unscaledDeltaTime;
        if (_easterTimer <= 0f) {
            if (_easterText != null) _easterText.gameObject.SetActive(false);
            return;
        }
        // 快速 RGB 闪耀（1.5 圈/秒）
        if (_easterText != null) {
            float hue = (Time.unscaledTime * 1.5f) % 1f;
            _easterText.color = Color.HSVToRGB(hue, 1f, 1f);
        }
    }

    /// <summary>作者生日祝福弹窗（标题 + 正文 + 关闭按钮）</summary>
    private static void ShowBirthdayDialog() {
        try {
            if (_birthdayCanvas != null) return;
            var cv = MakeCanvas("LevelLib_Birthday", 95000);   // 排序值高于设置/标记等面板
            _birthdayCanvas = cv;
            // 半透明黑罩（点不穿透，挡住下面）
            var bg = MakeImage(cv.transform, "Bg", new Color(0f, 0f, 0f, 0.85f));
            Stretch(bg.rectTransform, Vector2.zero, Vector2.one);

            // 主体盒子
            var box = MakeImage(cv.transform, "Box", PanelColor);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(860f, 560f);
            // 入场与其它弹窗同一套：遮罩和盒子一起从下面浮上来、从透明淡到原样（见 MakeDialogBox 那条的说明）
            AnimateFloatIn(brt, box, bg, DialogInRise, DialogInSeconds);

            // 标题"当当当当！！"（大号暖色，居上）
            var title = MakeTitle(box.transform, "Title", 58, new Color(1f, 0.72f, 0.25f, 1f), TextAnchor.MiddleCenter);
            title.text = Lang.Tr("当当当当！！");
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -36f);
            trt.sizeDelta = new Vector2(820f, 74f);

            // 装饰：标题下一条分隔线（细横条）
            var line = MakeImage(box.transform, "Line", new Color(1f, 0.72f, 0.25f, 0.55f));
            var lrt = line.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -128f);
            lrt.sizeDelta = new Vector2(780f, 3f);

            // 正文（精简为 4 行，居中，不重叠）
            var body = MakeText(box.transform, "Body", 26, Color.white, TextAnchor.MiddleCenter);
            body.text = Lang.Tr("今天是该 MOD 的作者 洛星橙 的生日噢～\n\n") +
                "可以去 QQ 群里或者 B 站\n给他一个祝福嘛？\n\n" +
                "虽然他正在备战高考不一定能看见\n但还是很感谢你哦";
            var brct = body.rectTransform;
            brct.anchorMin = brct.anchorMax = new Vector2(0.5f, 0.5f);
            brct.pivot = new Vector2(0.5f, 0.5f);
            brct.anchoredPosition = new Vector2(0f, 12f);
            brct.sizeDelta = new Vector2(800f, 260f);

            // 关闭按钮“知道了”（底部居中）
            var okBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(240f, 58f), new Color(0.2f, 0.7f, 0.3f, 1f));
            var okRt = okBtn.GetComponent<RectTransform>();
            okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = new Vector2(0f, 40f);
            var okT = MakeText(okBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            okT.text = Lang.Tr("知道了~");
            Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
            okBtn.onClick.AddListener(() => { Object.Destroy(cv.gameObject); _birthdayCanvas = null; });
        } catch { }
    }

    /// <summary>构造周报弹窗（标题 + 上周最难谱 / 玩得最多 + 本周数据 + 称号 + 历史数据 + 底部鼓励）</summary>
    private static void ShowWeeklyReportDialog(System.DateTime thisMon, bool preview) {
        try {
            if (_weeklyCanvas != null) return;
            long startSec, endSec;
            if (preview) {
                // 预览：最近 7 天（now-7d ~ now+1s）
                endSec = ToUnix(System.DateTime.Now.AddSeconds(1));
                startSec = ToUnix(System.DateTime.Now.AddDays(-7));
            } else {
                // 正式：上周 = 本周一往前推 7 天（上周一 ~ 上周日）
                System.DateTime lastMon = thisMon.AddDays(-7);
                System.DateTime lastSun = thisMon.AddSeconds(-1);
                startSec = ToUnix(lastMon);
                endSec = ToUnix(lastSun);
            }

            // 统计
            LevelEntry hardest = FindHardestEntry(startSec, endSec);
            LevelEntry mostPlayed = FindMostPlayedEntry(startSec, endSec, out int mostCount);
            int totalPlays = LevelScanner.GetWeekPlayCount(startSec, endSec);
            long totalTimeSec = LevelScanner.GetWeekPlayTime(startSec, endSec);
            int playDays = LevelScanner.GetWeekPlayDays(startSec, endSec);
            int hardClears = LevelScanner.GetWeekHardClears(startSec, endSec, RankOf);
            string weekTitle = GetWeekTitle(playDays, hardClears, totalPlays);
            // 历史统计（右区）
            ComputeHistoryStats(out int histP, out int histG, out int histU, out string highestRank);
            // 历史时长与个人信息栏同一个口径（启动时同步一次 Steam，之后按我们自己的实时统计往上加），
            // 格式也统一成 小时:分:秒 —— 两处数字必须一样，不然玩家一眼就看出是两套账。
            long histTime = PlayTime.TotalSeconds();
            string histTimeStr = PlayTime.FormatHms(histTime);

            var cv = MakeCanvas("LevelLib_Weekly", 96000);   // 排序值高于生日弹窗
            _weeklyCanvas = cv;
            // 半透明黑罩
            var bg = MakeImage(cv.transform, "Bg", new Color(0f, 0f, 0f, 0.85f));
            Stretch(bg.rectTransform, Vector2.zero, Vector2.one);

            // 主体盒子
            var box = MakeImage(cv.transform, "Box", PanelColor);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(1500f, 900f);
            // 入场与其它弹窗同一套：遮罩和盒子一起从下面浮上来、从透明淡到原样
            AnimateFloatIn(brt, box, bg, DialogInRise, DialogInSeconds);

            // 标题“周报”
            var title = MakeTitle(box.transform, "Title", 58, new Color(1f, 0.72f, 0.25f, 1f), TextAnchor.MiddleCenter);
            title.text = Lang.Tr("周报");
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -36f);
            trt.sizeDelta = new Vector2(1460f, 72f);

            // 分隔线
            var line = MakeImage(box.transform, "Line", new Color(1f, 0.72f, 0.25f, 0.55f));
            var lrt = line.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -128f);
            lrt.sizeDelta = new Vector2(1420f, 3f);

            // 中间竖分隔线（左右分区）：顶部对齐横线(-128)，向下延伸，底部停在 foot 上方
            var vline = MakeImage(box.transform, "VLine", new Color(1f, 0.72f, 0.25f, 0.35f));
            var vlrt = vline.rectTransform;
            vlrt.anchorMin = vlrt.anchorMax = new Vector2(0.5f, 1f);
            vlrt.pivot = new Vector2(0.5f, 1f);
            vlrt.anchoredPosition = new Vector2(0f, -161f);
            vlrt.sizeDelta = new Vector2(3f, 540f);

            // 第一块：本周通关最难谱（竖排：标签 + 内容行带图标）
            BuildReportBlock(box.transform, "本周通关最难谱", hardest, -1f, -380f);
            // 第二块：本周玩得最多的谱
            BuildReportBlock(box.transform, "本周玩得最多的谱", mostPlayed, (float)mostCount, -380f);

            // 统计区：本周游玩次数 / 本周游玩时间（顶部锚，紧贴两块下方）
            string timeStr = FormatPlayTime(totalTimeSec);
            string countStr = Lang.TrFmt("本周游玩次数：{0} 次", totalPlays);
            string timeLine = Lang.TrFmt("本周游玩时间：{0}", timeStr);
            var statTitle = MakeText(box.transform, "StatTitle", 26, new Color(1f, 0.82f, 0.45f, 1f), TextAnchor.UpperLeft);
            statTitle.text = Lang.Tr("本周数据");
            var stRt = statTitle.rectTransform;
            stRt.anchorMin = stRt.anchorMax = new Vector2(0.5f, 1f);
            stRt.pivot = new Vector2(0.5f, 1f);
            stRt.anchoredPosition = new Vector2(-380f, -430f);
            stRt.sizeDelta = new Vector2(680f, 40f);

            var statCount = MakeText(box.transform, "StatCount", 30, Color.white, TextAnchor.UpperLeft);
            statCount.text = countStr;
            var sRt1 = statCount.rectTransform;
            sRt1.anchorMin = sRt1.anchorMax = new Vector2(0.5f, 1f);
            sRt1.pivot = new Vector2(0.5f, 1f);
            sRt1.anchoredPosition = new Vector2(-380f, -478f);
            sRt1.sizeDelta = new Vector2(680f, 40f);

            var statTime = MakeText(box.transform, "StatTime", 30, Color.white, TextAnchor.UpperLeft);
            statTime.text = timeLine;
            var sRt2 = statTime.rectTransform;
            sRt2.anchorMin = sRt2.anchorMax = new Vector2(0.5f, 1f);
            sRt2.pivot = new Vector2(0.5f, 1f);
            sRt2.anchoredPosition = new Vector2(-380f, -526f);
            sRt2.sizeDelta = new Vector2(680f, 40f);

            // 本周称号（金色，界面下方）
            var titleLabel = MakeText(box.transform, "TitleLabel", 26, new Color(1f, 0.82f, 0.45f, 1f), TextAnchor.UpperLeft);
            titleLabel.text = Lang.Tr("本周称号");
            var tlRt = titleLabel.rectTransform;
            tlRt.anchorMin = tlRt.anchorMax = new Vector2(0.5f, 1f);
            tlRt.pivot = new Vector2(0.5f, 1f);
            tlRt.anchoredPosition = new Vector2(-380f, -576f);
            tlRt.sizeDelta = new Vector2(680f, 40f);

            var titleVal = MakeText(box.transform, "TitleVal", 52, new Color(1f, 0.84f, 0.30f, 1f), TextAnchor.MiddleLeft);
            titleVal.text = weekTitle;
            var tvRt = titleVal.rectTransform;
            tvRt.anchorMin = tvRt.anchorMax = new Vector2(0.5f, 1f);
            tvRt.pivot = new Vector2(0.5f, 1f);
            tvRt.anchoredPosition = new Vector2(-380f, -628f);
            tvRt.sizeDelta = new Vector2(680f, 64f);

            // ---- 右区：历史数据 ----
            float histX = 380f;
            var histTitle = MakeTitle(box.transform, "HistTitle", 30, new Color(1f, 0.82f, 0.45f, 1f), TextAnchor.UpperLeft);
            histTitle.text = Lang.Tr("历史数据");
            var hTitleRt = histTitle.rectTransform;
            hTitleRt.anchorMin = hTitleRt.anchorMax = new Vector2(0.5f, 1f);
            hTitleRt.pivot = new Vector2(0.5f, 1f);
            hTitleRt.anchoredPosition = new Vector2(histX, -180f);
            hTitleRt.sizeDelta = new Vector2(660f, 44f);

            string[] histLines = {
                Lang.TrFmt("历史严判最高通关等级：{0}", highestRank),
                Lang.TrFmt("历史击破P级：{0}", histP),
                Lang.TrFmt("历史击破G级：{0}", histG),
                Lang.TrFmt("历史击破U级：{0}", histU),
                Lang.TrFmt("历史游玩时长：{0}", histTimeStr),
            };
            for (int i = 0; i < histLines.Length; i++) {
                var ht = MakeText(box.transform, "Hist" + i, 30, new Color(0.95f, 0.95f, 0.98f, 1f), TextAnchor.UpperLeft);
                ht.text = histLines[i];
                var hrt = ht.rectTransform;
                hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
                hrt.pivot = new Vector2(0.5f, 1f);
                hrt.anchoredPosition = new Vector2(histX, -240f - i * 50f);
                hrt.sizeDelta = new Vector2(660f, 44f);
            }

            // 底部鼓励文字
            var foot = MakeText(box.transform, "Foot", 26, new Color(0.9f, 0.9f, 0.95f, 1f), TextAnchor.MiddleCenter);
            foot.text = Lang.Tr("这一周也很努力啦\n下一周也要开开心心的过呀～");
            var footRt = foot.rectTransform;
            footRt.anchorMin = footRt.anchorMax = new Vector2(0.5f, 0f);
            footRt.pivot = new Vector2(0.5f, 0f);
            footRt.anchoredPosition = new Vector2(0f, 96f);
            footRt.sizeDelta = new Vector2(840f, 116f);

            // 关闭按钮
            var okBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(240f, 58f), new Color(0.2f, 0.7f, 0.3f, 1f));
            var okRt = okBtn.GetComponent<RectTransform>();
            okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = new Vector2(0f, 30f);
            var okT = MakeText(okBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            okT.text = Lang.Tr("知道了~");
            Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
            okBtn.onClick.AddListener(() => { Object.Destroy(cv.gameObject); _weeklyCanvas = null; });
        } catch { }
    }

    /// <summary>竖排一块：标签行（文字）+ 内容行（等级图标 + 判定图标 + 谱名文字 [+次数]）</summary>
    private static void BuildReportBlock(Transform parent, string label, LevelEntry entry, float playCount, float x) {
        // 两个区块的垂直偏移：第 1 块 topY=-160，第 2 块 topY=-300
        bool firstBlock = (label == "本周通关最难谱");
        float topY = firstBlock ? -160f : -300f;

        // ---- 标签行 ----
        var lbl = MakeText(parent, "Label", 30, new Color(1f, 0.82f, 0.45f, 1f), TextAnchor.UpperLeft);
        lbl.text = label;
        var lblRt = lbl.rectTransform;
        lblRt.anchorMin = lblRt.anchorMax = new Vector2(0.5f, 1f);
        lblRt.pivot = new Vector2(0.5f, 1f);
        lblRt.anchoredPosition = new Vector2(x, topY);
        lblRt.sizeDelta = new Vector2(660f, 40f);

        // 没数据：占位一行
        if (entry == null) {
            var empty = MakeText(parent, "Empty", 26, new Color(0.6f, 0.6f, 0.7f, 1f), TextAnchor.UpperLeft);
            empty.text = Lang.Tr("（暂无数据）");
            var emptyRt = empty.rectTransform;
            emptyRt.anchorMin = emptyRt.anchorMax = new Vector2(0.5f, 1f);
            emptyRt.pivot = new Vector2(0.5f, 1f);
            emptyRt.anchoredPosition = new Vector2(x, topY - 50f);
            emptyRt.sizeDelta = new Vector2(660f, 40f);
            return;
        }

        // ---- 内容行 ----------------
        float contentY = topY - 54f;
        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(x, contentY);
        rowRt.sizeDelta = new Vector2(660f, 56f);

        float cursorX = 0f;
        // 等级图标
        string mark = MarkStore.Get(entry.FilePath) ?? "";
        if (!string.IsNullOrEmpty(mark)) {
            var mk = MakeImage(row.transform, "Mark", Color.white);
            mk.sprite = MarkStore.LoadIcon(mark);
            mk.rectTransform.anchorMin = mk.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            mk.rectTransform.pivot = new Vector2(0f, 0.5f);
            mk.rectTransform.anchoredPosition = new Vector2(cursorX, 0f);
            mk.rectTransform.sizeDelta = new Vector2(54f, 54f);
            cursorX += 64f;
        }
        // 判定图标（宽判/标判/严判 → wide/normal/strict.png）
        string judge = MarkStore.GetJudge(entry.FilePath) ?? "";
        if (!string.IsNullOrEmpty(judge) && MarkStore.JudgeFile.TryGetValue(judge, out var judgeFile)) {
            var jk = MakeImage(row.transform, "Judge", Color.white);
            jk.sprite = MarkStore.LoadIcon(judgeFile);
            jk.rectTransform.anchorMin = jk.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            jk.rectTransform.pivot = new Vector2(0f, 0.5f);
            jk.rectTransform.anchoredPosition = new Vector2(cursorX, 0f);
            jk.rectTransform.sizeDelta = new Vector2(54f, 54f);
            cursorX += 64f;
        }
        // 谱名文字（图标右边）
        var nameText = MakeText(row.transform, "Name", 30, Color.white, TextAnchor.MiddleLeft);
        nameText.text = entry.Title + (playCount >= 0f ? Lang.TrFmt("  x{0}次", (int)playCount) : "");
        nameText.rectTransform.anchorMin = nameText.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        nameText.rectTransform.pivot = new Vector2(0f, 0.5f);
        nameText.rectTransform.anchoredPosition = new Vector2(cursorX, 0f);
        nameText.rectTransform.sizeDelta = new Vector2(500f, 56f);
        nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
    }

}
