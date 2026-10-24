using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI 的「TUF 社区定级 · 批量评级」窗口：进度页（可中断）＋ 三栏结果页。
/// 三栏就是队长要的那三栏：
///   · 自动写入    社区定级可靠 → 已经默默写进 marks.txt 的那些（来源：社区定级）
///   · 需要你确认  多候选 / 艺术家或曲名对不上（NeedsReview）/ 特殊档 —— 列候选让玩家点
///   · 查不到      TUF 没有可用定级 → 已经按本地 AutoRating 兜底写入（来源：本地估算）
/// 窗口是独立画布（91800，高于面板与老的批量进度条），所以关掉面板、换页都不影响它；
/// 关掉它也不影响后台那一轮批量继续跑（协程宿主挂在独立的 GameObject 上）。
/// 判定与写盘全在 TufBatch.cs，本文件只负责画和点。</summary>
public static partial class PortalUI {

    private const int TufBatchSortOrder = 91800;
    private const float TbRowH = 44f;          // 普通列表行高（两行 16 号字够放）
    private const float TbCandH = 48f;         // 候选行高
    private const int TbListCap = 120;         // 一栏最多画多少行（多了会拖慢开窗；完整名单在 tuf_batch.json）

    private static Canvas _tbCanvas;
    private static GameObject _tbProgPage;
    private static GameObject _tbResultPage;
    private static Text _tbTitle, _tbStatus, _tbProgBig, _tbProgDetail;
    private static Image _tbProgFill;
    private static Button _tbCancelBtn;        // 进度页那颗「中断」（自动流程要把它藏起来）
    private static string _tbLastBig = "", _tbLastDetail = "", _tbLastStatus = "";
    private static float _tbLastFill = -1f;
    private static int _tbPendingIdx;

    /// <summary>一栏：整栏 × 列表内容层 × 行排到哪儿了（行由这里手动摆，流式引擎才好接手）</summary>
    private sealed class TbColumn {
        public RectTransform Col;
        public RectTransform Content;
        public float Y;
    }

    private static TbColumn _tbColAuto, _tbColConfirm, _tbColMissing;   // 三栏只建一次，之后原地更新（滚动位置才不会每次重来）
    private static GameObject _tbConfirmFoot;      // 中栏底部那四颗（没有待确认的就藏起来）
    private static RectTransform _tbConfirmBlock;  // 中栏上一条整块（翻页时整块顶出去）
    private static float _tbConfirmH;              // 上一条整块多高
    private const float TbRowGap = 4f;             // 行距（原来是布局组的 spacing 3；栅格要求 4 的倍数）
    private const float TbColPadX = 4f;            // 行左右内缩（原来是布局组的 padding）

    // ================= 打开 / 关闭 =================

    /// <summary>打开窗口（正在跑就显示进度页，跑完/待确认就显示三栏页）</summary>
    public static void TufBatchOpenWindow() {
        try {
            TufBatch.EnsureStateLoaded();   // 换会话/重开游戏后，待确认那份名单要从 tuf_batch.json 补回来
            if (_tbCanvas == null) BuildTufBatchWindow();
            _tbCanvas.gameObject.SetActive(true);
            if (TufBatch.Running) ShowTufBatchProgressPage();
            else ShowTufBatchResultPage();
            RefreshTufBatchProgress(true);
        } catch (Exception e) {
            ReportError("打开 TUF 批量评级窗口失败", e);
        }
    }

    /// <summary>关闭窗口（不打断后台批量；下次点「查看进度」再打开）</summary>
    private static void CloseTufBatchWindow() {
        try { if (_tbCanvas != null) _tbCanvas.gameObject.SetActive(false); } catch { }
    }

    /// <summary>批量层每走一步都会调它（每帧最多几次，开销只有几个字符串比较）</summary>
    internal static void TufBatchNotify() {
        try {
            if (_tbCanvas == null || !_tbCanvas.gameObject.activeSelf) return;
            RefreshTufBatchProgress(false);
            // 自动流程不看三栏（玩家没得选，也不该被问）→ 只留进度，跑完由收尾那段关窗 + 一句提示
            if (TufBatch.AutoMode) return;
            // 跑完了而页面还停在进度页 → 切到三栏
            if (!TufBatch.Running && _tbResultPage != null && !_tbResultPage.activeSelf && TufBatch.Finished) {
                ShowTufBatchResultPage();
            }
        } catch (Exception e) {
            Lg.Ex("TUF 批量评级窗口刷新失败", e);
        }
    }

    // ================= 本地估算（回落）=================

    /// <summary>本地 AutoRating 估算（查不到那一栏的回落）。走的是老批量评级那条一模一样的路：
    /// GetLevelText（带缓存）→ AutoRater.RateChart → IsValidPgu。AutoRating 一个字都没动。</summary>
    internal static bool LocalRateForBatch(string file, out string pgu, out double star) {
        pgu = "";
        star = 0d;
        try {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
            string text = GetLevelText(file);
            if (string.IsNullOrEmpty(text)) return false;
            string p = AutoRater.RateChart(text, out star, 100.0, Path.GetFileName(file));
            if (!IsValidPgu(p)) return false;
            pgu = p;
            return true;
        } catch (Exception e) {
            Lg.Ex("TUF 批量评级：本地估算失败 " + file, e);
            return false;
        }
    }

    /// <summary>批量收尾：列表与详情区刷新（标记图标变了）</summary>
    internal static void RefreshAfterTufBatch() {
        try {
            InvalidateLevelTextCache();
            RefreshList();
            if (_detailEntry != null) RefreshDetailInfo(_detailEntry);
        } catch (Exception e) { Lg.Ex("TUF 批量评级后刷新界面失败", e); }
    }

    /// <summary>自动流程收尾：关掉进度窗，只在屏幕上留一句"评级完了"（玩家全程不用点任何东西）。
    /// 三个数字 = TUF 定下来的 / 本地估算出来的 / 两个都不行没标的。</summary>
    internal static void TufBatchFinishAuto(TufBatchState st) {
        CloseTufBatchWindow();
        int tuf, local, none;
        TufBatchAutoCounts(st, out tuf, out local, out none);
        ShowToast(Lang.TrFmt("评级完了：TUF 定级 {0} 张 · 本地估算 {1} 张 · 没评上 {2} 张", tuf, local, none), 8f);
    }

    /// <summary>自动流程那三个数字（进度页的明细与收尾提示共用这一份算法，别两处各写一遍）</summary>
    internal static void TufBatchAutoCounts(TufBatchState st, out int tuf, out int local, out int none) {
        tuf = 0; local = 0; none = 0;
        if (st == null) return;
        if (st.Auto != null) {
            for (int i = 0; i < st.Auto.Count; i++) {
                var it = st.Auto[i];
                if (it != null && it.Written && it.Source == TufBatch.SourceCommunity) tuf++;
            }
        }
        if (st.Missing != null) {
            for (int i = 0; i < st.Missing.Count; i++) {
                var it = st.Missing[i];
                if (it != null && it.Written && it.Source == TufBatch.SourceLocal) local++;
                else none++;
            }
        }
        // 自动流程不该留下待确认的条目；真留下了也只按"没评上"报，不骗人
        if (st.Pending != null) none += st.Pending.Count;
    }

    // ================= 窗口骨架 =================

    private static void BuildTufBatchWindow() {
        // 画布是新建的：三栏与「上一条」的引用全部作废，下一次会照新画布重建
        _tbColAuto = null; _tbColConfirm = null; _tbColMissing = null;
        _tbConfirmBlock = null; _tbConfirmH = 0f; _tbConfirmFoot = null;
        _tbCanvas = MakeCanvas("LevelLib_TufBatch", TufBatchSortOrder);
        var veil = MakeDialogVeil(_tbCanvas.transform);
        veil.raycastTarget = true;

        var box = MakeDialogBox(_tbCanvas.transform, "Box", PanelColor);
        var brt = box.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(1580f, 880f);

        _tbTitle = MakeTitle(box.transform, "TufTitle", 34, new Color(0.75f, 0.88f, 1f, 1f), TextAnchor.MiddleLeft);
        _tbTitle.fontStyle = FontStyle.Bold;
        _tbTitle.text = Lang.Tr("TUF 社区定级 · 批量评级");
        var trt = _tbTitle.rectTransform;
        trt.anchorMin = new Vector2(0.02f, 0.92f);
        trt.anchorMax = new Vector2(0.70f, 0.99f);
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        _tbStatus = MakeText(box.transform, "TufStatus", 20, new Color(0.72f, 0.74f, 0.80f, 1f), TextAnchor.MiddleRight);
        var srt = _tbStatus.rectTransform;
        srt.anchorMin = new Vector2(0.36f, 0.92f);
        srt.anchorMax = new Vector2(0.945f, 0.99f);
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;

        var close = MakeButton(box.transform, "TufClose", Vector2.zero, new Vector2(46f, 46f), new Color(0.45f, 0.20f, 0.22f, 1f));
        var crt = close.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-18f, -16f);
        // 关闭：图标族的叉（原先是个 × 字体字符，字重粗细全看字号）
        var ct = IconImage(close.transform, "Icon", IconShapes.Kind.Close, IconMd, Color.white);
        var ctRt = ct.rectTransform;
        ctRt.anchorMin = ctRt.anchorMax = new Vector2(0.5f, 0.5f);
        ctRt.pivot = new Vector2(0.5f, 0.5f);
        ctRt.anchoredPosition = Vector2.zero;
        close.onClick.AddListener(() => { DeselectUi(); CloseTufBatchWindow(); });

        // 进度页
        _tbProgPage = new GameObject("ProgPage", typeof(RectTransform));
        _tbProgPage.transform.SetParent(box.transform, false);
        var prt = _tbProgPage.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.03f, 0.10f);
        prt.anchorMax = new Vector2(0.97f, 0.88f);
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        BuildTufBatchProgressPage(_tbProgPage.transform);

        // 三栏页（内容按 State 现建现拆，见 ShowTufBatchResultPage）
        _tbResultPage = new GameObject("ResultPage", typeof(RectTransform));
        _tbResultPage.transform.SetParent(box.transform, false);
        var rrt = _tbResultPage.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.012f, 0.055f);
        rrt.anchorMax = new Vector2(0.988f, 0.91f);
        rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

        _tbProgPage.SetActive(true);
        _tbResultPage.SetActive(false);
    }

    private static void BuildTufBatchProgressPage(Transform parent) {
        _tbProgBig = MakeText(parent, "Big", 30, new Color(1f, 0.88f, 0.55f, 1f), TextAnchor.MiddleCenter);
        _tbProgBig.text = Lang.Tr("准备中…");
        var brt = _tbProgBig.rectTransform;
        brt.anchorMin = new Vector2(0f, 0.72f);
        brt.anchorMax = new Vector2(1f, 0.92f);
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        _tbProgDetail = MakeText(parent, "Detail", 22, Color.white, TextAnchor.MiddleCenter);
        _tbProgDetail.horizontalOverflow = HorizontalWrapMode.Wrap;
        _tbProgDetail.lineSpacing = 1.25f;
        _tbProgDetail.text = "";
        var drt = _tbProgDetail.rectTransform;
        drt.anchorMin = new Vector2(0.04f, 0.46f);
        drt.anchorMax = new Vector2(0.96f, 0.70f);
        drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;

        var bar = MakeImage(parent, "BarBg", new Color(0.08f, 0.09f, 0.13f, 1f));
        var bart = bar.rectTransform;
        bart.anchorMin = new Vector2(0.10f, 0.34f);
        bart.anchorMax = new Vector2(0.90f, 0.42f);
        bart.offsetMin = Vector2.zero; bart.offsetMax = Vector2.zero;
        _tbProgFill = MakeImage(bar.transform, "Fill", new Color(0.30f, 0.62f, 0.45f, 1f));
        var frt = _tbProgFill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        frt.sizeDelta = Vector2.zero;

        var cancel = MakeButton(parent, "Cancel", Vector2.zero, new Vector2(220f, 52f), new Color(0.62f, 0.32f, 0.22f, 1f));
        _tbCancelBtn = cancel;
        var crt = cancel.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.12f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        var ct = MakeText(cancel.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(ct.rectTransform, Vector2.zero, Vector2.one);
        ct.text = Lang.Tr("中断（进度留着，可继续）");
        cancel.onClick.AddListener(() => {
            DeselectUi();
            TufBatch.Cancel();
            ShowToast(Lang.Tr("已中断，进度留着喵（下次点「继续评级」接着来）"), 5f);
        });
    }

    private static void ShowTufBatchProgressPage() {
        if (_tbProgPage != null) _tbProgPage.SetActive(true);
        if (_tbResultPage != null) _tbResultPage.SetActive(false);
        _tbLastBig = null; _tbLastDetail = null; _tbLastFill = -1f;   // 强制刷一次
    }

    /// <summary>进度页的文字与进度条（只在内容变了才写 Text，避免每帧重建布局）</summary>
    private static void RefreshTufBatchProgress(bool force) {
        if (_tbProgPage == null || !_tbProgPage.activeSelf) return;
        var st = TufBatch.State;
        string big, detail;
        float fill;
        bool auto = TufBatch.AutoMode;
        if (TufBatch.Running) {
            int total = st != null ? st.TodoTotal : 0;
            int done = st != null ? st.TodoDone : 0;
            big = total > 0
                ? (auto ? Lang.TrFmt("正在评级：{0} / {1}", done, total) : Lang.TrFmt("正在查 TUF：{0} / {1}", done, total))
                : Lang.Tr("正在扫描谱面库…");
            fill = total > 0 ? Mathf.Clamp01((float)done / total) : 0f;
            detail = auto ? TufBatchDetailLineAuto(st) : TufBatchDetailLine(st);
        } else {
            big = Lang.Tr("批量评级完成（进度已存，可接着确认）");
            fill = 1f;
            detail = auto ? TufBatchDetailLineAuto(st) : TufBatchDetailLine(st);
        }
        // 自动流程不给玩家选择：进度条 + 一句收尾提示就够，「中断」那句按钮收起来
        if (_tbCancelBtn != null && _tbCancelBtn.gameObject.activeSelf == auto) _tbCancelBtn.gameObject.SetActive(!auto);
        if (force || big != _tbLastBig) { _tbProgBig.text = big; _tbLastBig = big; }
        if (force || detail != _tbLastDetail) { _tbProgDetail.text = detail; _tbLastDetail = detail; }
        if (_tbProgFill != null && (force || Mathf.Abs(fill - _tbLastFill) > 0.0005f)) {
            _tbProgFill.rectTransform.anchorMax = new Vector2(fill, 1f);
            _tbLastFill = fill;
        }
        string status = TufBatchStatusLine(st);
        if (force || status != _tbLastStatus) { _tbStatus.text = status; _tbLastStatus = status; }
    }

    private static string TufBatchDetailLine(TufBatchState st) {
        if (st == null) return "";
        return Lang.TrFmt("自动写入 {0} · 待确认 {1} · 查不到 {2}", st.Auto.Count, st.Pending.Count, st.Missing.Count)
             + "\n" + Lang.TrFmt("跳过你已标记的 {0} 张 ｜ 本轮已处理 {1} 条", st.SkippedMarked, st.Queried);
    }

    /// <summary>自动流程的明细：三栏那套（自动写入 / 待确认 / 查不到）对玩家没意义 ——
    /// 待确认这一栏在自动流程里压根不会被用到，所以换成"定级 / 估算 / 没评上"三个数。</summary>
    private static string TufBatchDetailLineAuto(TufBatchState st) {
        int tuf, local, none;
        TufBatchAutoCounts(st, out tuf, out local, out none);
        return Lang.TrFmt("TUF 定级 {0} · 本地估算 {1} · 没评上 {2}", tuf, local, none)
             + "\n" + Lang.TrFmt("跳过你已标记的 {0} 张 ｜ 本轮已处理 {1} 条",
                                  st != null ? st.SkippedMarked : 0, st != null ? st.Queried : 0);
    }

    private static string TufBatchStatusLine(TufBatchState st) {
        if (st == null || st.Scanned <= 0) return "";
        string s = Lang.TrFmt("扫到 {0} 张谱面", st.Scanned);
        if (!string.IsNullOrEmpty(st.BackupFile)) s += " ｜ " + Lang.Tr("写盘前已备份");
        if (st.CharterMatched > 0 || st.CharterEmpty > 0) {
            s += " ｜ " + Lang.TrFmt("谱师回落命中 {0} 张 ｜ 本地谱师为空没走这条路 {1} 张",
                                     st.CharterMatched, st.CharterEmpty);
        }
        return s;
    }

    // ================= 三栏结果页 =================

    private static void ShowTufBatchResultPage() {
        if (_tbResultPage == null) return;
        _tbProgPage.SetActive(false);
        _tbResultPage.SetActive(true);
        var st = TufBatch.State;
        if (st == null) st = new TufBatchState();
        string status = TufBatchStatusLine(st);
        if (status != _tbLastStatus) { _tbStatus.text = status; _tbLastStatus = status; }
        if (_tbColAuto == null) TbBuildResultShell();     // 三栏与底部那两颗只建一次
        TbSetColumnHeader(_tbColAuto, Lang.TrFmt("自动写入 {0}", st.Auto.Count));
        TbBuildInfoList(_tbColAuto, st.Auto, true, Lang.Tr("这一轮没有能自动写入的谱面"));
        TbSetColumnHeader(_tbColConfirm, Lang.TrFmt("需要你确认 {0}", st.Pending.Count));
        TbBuildConfirmColumn(_tbColConfirm, st);
        TbSetColumnHeader(_tbColMissing, Lang.TrFmt("查不到 {0}", st.Missing.Count));
        TbBuildInfoList(_tbColMissing, st.Missing, false, Lang.Tr("这一轮没有查不到的谱面"));
    }

    /// <summary>结果页的壳：三栏 + 底部两颗（只建一次，之后只换栏里的行）</summary>
    private static void TbBuildResultShell() {
        _tbColAuto = TbMakeColumn(_tbResultPage.transform, "ColAuto", 0.000f, 0.305f, "自动写入", new Color(0.55f, 0.85f, 0.62f, 1f),
            Lang.Tr("TUF 定级可靠，已经写进 marks.txt（来源：社区定级）"));
        _tbColConfirm = TbMakeColumn(_tbResultPage.transform, "ColConfirm", 0.340f, 0.660f, "需要你确认", new Color(1f, 0.82f, 0.42f, 1f),
            Lang.Tr("多候选 / 艺术家或曲名对不上 / 特殊档 —— 点一条候选就写入"));
        _tbColMissing = TbMakeColumn(_tbResultPage.transform, "ColMissing", 0.695f, 1.000f, "查不到", new Color(0.72f, 0.78f, 0.90f, 1f),
            Lang.Tr("TUF 没有可用定级 —— 已按本地 AutoRating 兜底（来源：本地估算）"));
        TbBuildConfirmFooter(_tbColConfirm);

        // 底部按钮：分别钉在页面左右两端（中间那栏的按钮占的是页面中线附近，不会打架）
        var rescan = MakeButton(_tbResultPage.transform, "Rescan", Vector2.zero, new Vector2(250f, 40f), new Color(0.25f, 0.45f, 0.7f, 1f));
        var rrt2 = rescan.GetComponent<RectTransform>();
        rrt2.anchorMin = rrt2.anchorMax = new Vector2(0f, 0f);
        rrt2.pivot = new Vector2(0f, 0f);
        rrt2.anchoredPosition = new Vector2(8f, 8f);
        var rt2 = MakeText(rescan.transform, "T", 20, Color.white, TextAnchor.MiddleCenter);
        Stretch(rt2.rectTransform, Vector2.zero, Vector2.one);
        rt2.text = Lang.Tr("重新扫一遍（清空查询记录）");
        rescan.onClick.AddListener(() => {
            DeselectUi();
            if (TufBatch.Running) { ShowToast(Lang.Tr("这一轮还在跑喵"), 4f); return; }
            TufBatch.ClearSessionAndRestart();
        });

        var close2 = MakeButton(_tbResultPage.transform, "Close2", Vector2.zero, new Vector2(180f, 40f), new Color(0.30f, 0.34f, 0.42f, 1f));
        var crt2 = close2.GetComponent<RectTransform>();
        crt2.anchorMin = crt2.anchorMax = new Vector2(1f, 0f);
        crt2.pivot = new Vector2(1f, 0f);
        crt2.anchoredPosition = new Vector2(-8f, 8f);
        var ct2 = MakeText(close2.transform, "T", 20, Color.white, TextAnchor.MiddleCenter);
        Stretch(ct2.rectTransform, Vector2.zero, Vector2.one);
        ct2.text = Lang.Tr("关闭窗口");
        close2.onClick.AddListener(() => { DeselectUi(); CloseTufBatchWindow(); });
    }

    /// <summary>中栏底部那四颗（翻页 / 跳过 / 本地兜底）：只做一次，点的时候现取"当前这一条"</summary>
    private static void TbBuildConfirmFooter(TbColumn c) {
        _tbConfirmFoot = new GameObject("Foot", typeof(RectTransform));
        _tbConfirmFoot.transform.SetParent(c.Col, false);
        Stretch(_tbConfirmFoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        var foot = _tbConfirmFoot.transform;

        var prev = MakeButton(foot, "Prev", Vector2.zero, new Vector2(54f, 38f), new Color(0.25f, 0.30f, 0.42f, 1f));
        TbAnchorFooter(prev, 0f, 6f);
        // 翻页箭头也用图标族（原先 ◀ ▶ 是字体字符；贴图走 IconSprite 的缓存）
        var pt = IconImage(prev.transform, "Icon", IconShapes.Kind.ChevronLeft, IconMd, Color.white);
        var ptRt = pt.rectTransform;
        ptRt.anchorMin = ptRt.anchorMax = new Vector2(0.5f, 0.5f);
        ptRt.pivot = new Vector2(0.5f, 0.5f);
        ptRt.anchoredPosition = Vector2.zero;
        prev.onClick.AddListener(() => { DeselectUi(); _tbPendingIdx--; ShowTufBatchResultPage(); });

        var next = MakeButton(foot, "Next", Vector2.zero, new Vector2(54f, 38f), new Color(0.25f, 0.30f, 0.42f, 1f));
        TbAnchorFooter(next, 62f, 6f);
        var nt = IconImage(next.transform, "Icon", IconShapes.Kind.ChevronRight, IconMd, Color.white);
        var ntRt = nt.rectTransform;
        ntRt.anchorMin = ntRt.anchorMax = new Vector2(0.5f, 0.5f);
        ntRt.pivot = new Vector2(0.5f, 0.5f);
        ntRt.anchoredPosition = Vector2.zero;
        next.onClick.AddListener(() => { DeselectUi(); _tbPendingIdx++; ShowTufBatchResultPage(); });

        var skip = MakeButton(foot, "Skip", Vector2.zero, new Vector2(86f, 38f), new Color(0.24f, 0.28f, 0.38f, 1f));
        TbAnchorFooter(skip, 124f, 6f);
        var st2 = MakeText(skip.transform, "T", 17, Color.white, TextAnchor.MiddleCenter);
        Stretch(st2.rectTransform, Vector2.zero, Vector2.one);
        st2.text = Lang.Tr("跳过");
        skip.onClick.AddListener(() => {
            DeselectUi();
            _tbPendingIdx++;
            ShowToast(Lang.Tr("这条先留着，下次点「继续评级」还能看见喵"), 4f);
            ShowTufBatchResultPage();
        });

        var local = MakeButton(foot, "Local", Vector2.zero, new Vector2(196f, 38f), new Color(0.36f, 0.30f, 0.52f, 1f));
        TbAnchorFooter(local, 218f, 6f);
        var lt = MakeText(local.transform, "T", 17, Color.white, TextAnchor.MiddleCenter);
        Stretch(lt.rectTransform, Vector2.zero, Vector2.one);
        lt.text = Lang.Tr("这条用本地估算兜底");
        local.onClick.AddListener(() => {
            DeselectUi();
            var cur = TbCurrentPending();
            if (cur == null) return;
            string msg;
            if (TufBatch.SkipToLocal(cur, out msg)) { ShowToast(msg, 5f); }
            else { ShowToast(msg, 6f); }
            ShowTufBatchResultPage();
        });
    }

    /// <summary>当前这一条待确认（越界会夹回范围；一条都没有就返回 null）</summary>
    private static TufBatchItem TbCurrentPending() {
        var st = TufBatch.State;
        if (st == null || st.Pending == null || st.Pending.Count == 0) return null;
        if (_tbPendingIdx >= st.Pending.Count) _tbPendingIdx = 0;
        if (_tbPendingIdx < 0) _tbPendingIdx = st.Pending.Count - 1;
        return st.Pending[_tbPendingIdx];
    }

    /// <summary>建一栏：标题 + 说明 + 列表区（行由 TbAddTextRow 手动摆下来，不用布局组 —— 布局组每帧会盖掉行自己的位置）</summary>
    private static TbColumn TbMakeColumn(Transform parent, string name, float x0, float x1, string title, Color titleColor, string caption) {
        var col = new GameObject(name, typeof(RectTransform));
        col.transform.SetParent(parent, false);
        var crt = col.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(x0, 0f);
        crt.anchorMax = new Vector2(x1, 1f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;

        var t = MakeText(col.transform, "T", 30, titleColor, TextAnchor.MiddleLeft);
        t.fontStyle = FontStyle.Bold;
        t.text = Lang.Tr(title);
        var trt = t.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.955f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var c = MakeText(col.transform, "C", 17, new Color(0.66f, 0.68f, 0.74f, 1f), TextAnchor.UpperLeft);
        c.horizontalOverflow = HorizontalWrapMode.Wrap;
        c.text = caption;
        var crt2 = c.rectTransform;
        crt2.anchorMin = new Vector2(0f, 0.885f);
        crt2.anchorMax = new Vector2(1f, 0.955f);
        crt2.offsetMin = Vector2.zero; crt2.offsetMax = Vector2.zero;

        var list = MakeImage(col.transform, "List", new Color(0.05f, 0.06f, 0.10f, 0.55f));
        var lrt = list.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0.075f);
        lrt.anchorMax = new Vector2(1f, 0.877f);
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        list.gameObject.AddComponent<RectMask2D>();
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 42f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = lrt;
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(list.transform, false);
        var ctrt = content.GetComponent<RectTransform>();
        ctrt.anchorMin = new Vector2(0f, 1f);
        ctrt.anchorMax = new Vector2(1f, 1f);
        ctrt.pivot = new Vector2(0.5f, 1f);
        ctrt.sizeDelta = Vector2.zero;
        scroll.content = ctrt;
        return new TbColumn { Col = col.GetComponent<RectTransform>(), Content = ctrt, Y = TbColPadX };
    }

    private static void TbSetColumnHeader(TbColumn c, string text) {
        if (c == null || c.Col == null) return;
        var t = c.Col.Find("T");
        if (t == null) return;
        var txt = t.GetComponent<Text>();
        if (txt != null) txt.text = text;
    }

    /// <summary>把这一栏的行全清掉（栏本身与列表区的滚动位置都留着）</summary>
    private static void TbClearContent(TbColumn c) {
        if (c == null || c.Content == null) return;
        for (int i = c.Content.childCount - 1; i >= 0; i--) {
            UnityEngine.Object.Destroy(c.Content.GetChild(i).gameObject);
        }
        c.Y = TbColPadX;
    }

    /// <summary>收口：内容层按行累加的高度定高（原来是 ContentSizeFitter 自己算）</summary>
    private static void TbFinishColumn(TbColumn c) {
        if (c == null || c.Content == null) return;
        c.Content.sizeDelta = new Vector2(0f, Mathf.Max(c.Y, 1f));
    }

    /// <summary>普通信息行：曲名 + 档位 + 来源（+ 没写成的原因）</summary>
    private static void TbBuildInfoList(TbColumn c, List<TufBatchItem> items, bool autoColumn, string emptyHint) {
        TbClearContent(c);
        if (c == null || c.Content == null) return;
        if (items == null || items.Count == 0) {
            TbAddTextRow(c, emptyHint, new Color(0.6f, 0.62f, 0.68f, 1f), 20, TbRowH);
            TbFinishColumn(c);
            return;
        }
        int cap = Mathf.Min(items.Count, TbListCap);
        for (int i = 0; i < cap; i++) {
            var it = items[i];
            string tier = string.IsNullOrEmpty(it.Tier) ? Lang.Tr("未评级") : it.Tier;
            string src = string.IsNullOrEmpty(it.SourceLabel) ? "" : ("· " + Lang.Tr(it.SourceLabel));
            if (autoColumn) src = src.Length > 0 ? (src + "（TUF）") : "";
            else if (it.Source == TufBatch.SourceLocal) src = src + "（AutoRating）";
            string head = (i + 1) + ". " + (string.IsNullOrEmpty(it.Title) ? Path.GetFileName(it.File) : it.Title);
            string line = head + "   " + tier + (src.Length > 0 ? ("  " + src) : "");
            if (!it.Written && !string.IsNullOrEmpty(it.Reason)) line += "   (" + it.Reason + ")";
            Color col2 = autoColumn ? new Color(0.92f, 0.95f, 1f, 1f)
                       : (it.Written ? new Color(0.92f, 0.95f, 1f, 1f) : new Color(0.95f, 0.75f, 0.60f, 1f));
            TbAddTextRow(c, line, col2, 16, TbRowH);
        }
        if (items.Count > cap) {
            TbAddTextRow(c, Lang.TrFmt("……还有 {0} 条（名单存在 mod\\tuf_batch.json）", items.Count - cap),
                new Color(0.6f, 0.62f, 0.68f, 1f), 16, TbRowH);
        }
        TbFinishColumn(c);
    }

    private static RectTransform TbAddTextRow(TbColumn c, string line, Color color, int fontSize, float height) {
        var row = MakeImage(c.Content, "Row", new Color(1f, 1f, 1f, 0.03f));
        var rrt = row.rectTransform;
        rrt.anchorMin = new Vector2(0f, 1f);
        rrt.anchorMax = new Vector2(1f, 1f);
        rrt.pivot = new Vector2(0.5f, 1f);
        rrt.sizeDelta = new Vector2(-TbColPadX * 2f, height);
        rrt.anchoredPosition = new Vector2(0f, -c.Y);
        row.raycastTarget = false;
        var t = MakeText(row.transform, "T", fontSize, color, TextAnchor.MiddleLeft);
        // 栏宽就 470 上下，长曲名必须换行而不是溢出到隔壁栏去
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.lineSpacing = 1.05f;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 0f); trt.offsetMax = new Vector2(-6f, 0f);
        t.text = line;
        c.Y += height + TbRowGap;
        return rrt;
    }

    /// <summary>「需要你确认」那一栏：一条一条翻，候选列出来给玩家点。
    /// 翻页 / 确认会换一整条：上一条整块往上顶出列表区（遮罩把它切掉），新的从下面顶上来。</summary>
    private static void TbBuildConfirmColumn(TbColumn c, TufBatchState st) {
        if (c == null || c.Content == null) return;
        float oldH = _tbConfirmH;
        if (_tbConfirmBlock != null) {
            FlowTo(_tbConfirmBlock, 0f, oldH);                                 // 整块往上顶出去
            UnityEngine.Object.Destroy(_tbConfirmBlock.gameObject, MoPage);    // 让位看完它自己消失
            _tbConfirmBlock = null;
        }
        if (oldH > 0f) FlowWindow("TUF 换一条", MoSmall);

        var block = new GameObject("Block", typeof(RectTransform));
        block.transform.SetParent(c.Content, false);
        var brt = block.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -oldH);     // 新的这一条先落在上一条下面，再顶上来
        FlowTo(brt, 0f, 0f);
        _tbConfirmBlock = brt;

        var inner = new TbColumn { Col = c.Col, Content = brt, Y = TbColPadX };
        var pending = st.Pending;
        if (pending == null || pending.Count == 0) {
            if (_tbConfirmFoot != null) _tbConfirmFoot.SetActive(false);
            TbAddTextRow(inner, Lang.Tr("没有需要你确认的谱面喵"), new Color(0.6f, 0.62f, 0.68f, 1f), 20, TbRowH);
            TbFinishColumn(inner);
            _tbConfirmH = inner.Y;
            return;
        }
        if (_tbConfirmFoot != null) _tbConfirmFoot.SetActive(true);
        var item = TbCurrentPending();      // 越界的页码会在里面夹回范围
        if (item == null) { TbFinishColumn(inner); _tbConfirmH = inner.Y; return; }

        // 翻页 + 当前是哪一条
        TbAddTextRow(inner, Lang.TrFmt("第 {0} / {1} 条：{2}", _tbPendingIdx + 1, pending.Count,
            string.IsNullOrEmpty(item.Title) ? Path.GetFileName(item.File) : item.Title),
            new Color(1f, 0.92f, 0.72f, 1f), 21, 34f);
        TbAddTextRow(inner, string.IsNullOrEmpty(item.Reason) ? "TUF 有多种可能，请你认一下" : item.Reason,
            new Color(0.86f, 0.80f, 0.66f, 1f), 18, 46f);
        TbAddTextRow(inner, Lang.TrFmt("TUF 已经看到 {0} 条候选（点哪一条就按哪一条写）", item.Candidates.Count),
            new Color(0.66f, 0.68f, 0.74f, 1f), 18, 30f);

        // 候选行（手摆在块里，块一动整块跟着动）
        int shown = Mathf.Min(item.Candidates.Count, 40);
        for (int i = 0; i < shown; i++) {
            var cand = item.Candidates[i];
            string tier = string.IsNullOrEmpty(cand.DifficultyName) ? "?" : cand.DifficultyName;
            bool writable = TufBatch.CanWriteTier(tier);
            string bpm = cand.HasBpm ? ("BPM " + cand.Bpm.ToString("0.##", CultureInfo.InvariantCulture)) : "BPM --";
            string tiles = cand.HasTileCount ? (cand.TileCount + " 格") : "格数 --";
            string line = tier + "  " + (cand.Song ?? "") + " · " + (string.IsNullOrEmpty(cand.Artist) ? "艺术家未知" : cand.Artist)
                        + " · 谱师 " + (string.IsNullOrEmpty(cand.Creator) ? "未知" : cand.Creator)
                        + " · " + bpm + " · " + tiles + (writable ? "" : Lang.Tr("（特殊档·按规矩不写）"));
            Color bg = writable ? new Color(0.20f, 0.34f, 0.26f, 0.95f) : new Color(0.26f, 0.22f, 0.24f, 0.85f);
            var btn = MakeButton(inner.Content, "Cand" + i, Vector2.zero, new Vector2(-TbColPadX * 2f, TbCandH), bg);
            var brt2 = btn.GetComponent<RectTransform>();
            brt2.anchorMin = new Vector2(0f, 1f);
            brt2.anchorMax = new Vector2(1f, 1f);
            brt2.pivot = new Vector2(0.5f, 1f);
            brt2.sizeDelta = new Vector2(-TbColPadX * 2f, TbCandH);
            brt2.anchoredPosition = new Vector2(0f, -inner.Y);
            inner.Y += TbCandH + TbRowGap;
            var t = MakeText(btn.transform, "T", 17, writable ? Color.white : new Color(0.78f, 0.72f, 0.72f, 1f), TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1.05f;
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 0f); trt.offsetMax = new Vector2(-6f, 0f);
            t.text = line;
            var captured = cand;
            var capturedItem = item;
            btn.onClick.AddListener(() => {
                DeselectUi();
                string msg;
                if (TufBatch.ConfirmCandidate(capturedItem, captured, out msg)) {
                    ShowToast(msg, 5f);
                    ShowTufBatchResultPage();
                } else {
                    ShowToast(msg, 6f);
                }
            });
        }
        if (item.Candidates.Count > shown) {
            TbAddTextRow(inner, Lang.TrFmt("……还有 {0} 条候选没列出来", item.Candidates.Count - shown),
                new Color(0.6f, 0.62f, 0.68f, 1f), 18, 30f);
        }
        TbFinishColumn(inner);
        _tbConfirmH = inner.Y;
    }

    /// <summary>把按钮钉在栏底（y 从栏底往上 4px，x 从左边缘往右 off）</summary>
    private static void TbAnchorFooter(Button btn, float off, float bottom) {
        var rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(off, bottom);
    }
}
