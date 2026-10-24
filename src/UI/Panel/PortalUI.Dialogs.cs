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

/// <summary>PortalUI partial 模块：Dialogs（由 PortalUI.cs 拆分而来，仍属同一个 partial class）</summary>
public static partial class PortalUI {

    /// <summary>轻提示：屏幕下方显示几秒后自动消失（谱面重复等）</summary>
    public static void ShowToast(string msg) { ShowToast(msg, ToastSeconds); }

    /// <summary>轻提示（可指定停留时长，给"要玩家照着去操作"的长句子用）。
    /// 框宽按文字的 preferredWidth 加宽并钳在 ToastMinW~ToastMaxW 之间：文字是 MiddleCenter + Overflow 不折行，
    /// 写死宽度会让长句子冲出框外。</summary>
    public static void ShowToast(string msg, float seconds) {
        if (_toastCanvas == null) {
            _toastCanvas = MakeCanvas("LevelLib_Toast", 89000);
            var bg = MakeImage(_toastCanvas.transform, "Bg", new Color(0f, 0f, 0f, 0.75f));
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 0.12f);
            bg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(ToastMinW, 56f);
            _toastBgRt = bg.rectTransform;
            var t = MakeText(bg.transform, "T", 26, new Color(1f, 0.75f, 0.4f, 1f), TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            _toastText = t;
            // 入场：从下面一点浮上来、同时淡入（与弹窗同一套，只是没有遮罩）
            _toastGroup = AnimateFloatIn(bg.rectTransform, bg, null, ToastInRise, ToastInSeconds);
        }
        // 上一条还在收尾时又来一条：直接拉回不透明，别让它继续淡下去
        _toastFade = 0f;
        if (_toastGroup != null) _toastGroup.alpha = 1f;
        _toastText.text = msg;
        if (_toastBgRt != null) {
            float w = Mathf.Clamp(_toastText.preferredWidth + 72f, ToastMinW, ToastMaxW);
            _toastBgRt.sizeDelta = new Vector2(w, _toastBgRt.sizeDelta.y);
        }
        _toastTimer = seconds <= 0f ? ToastSeconds : seconds;
        Lg.Info($"[LevelLib] Toast: {msg}");
    }

    /// <summary>轻提示的默认停留时长 / 宽度区间（长句子自动加宽，但不出屏）</summary>
    private const float ToastSeconds = 2.5f;
    private const float ToastMinW = 480f, ToastMaxW = 1560f;
    /// <summary>轻提示的入场与收起：浮上来的高度 / 时长，以及时间到之后淡掉的那一小段。
    /// 停留时间到了先淡再销毁：浮进来却是一下子没，比不动还别扭。</summary>
    private const float ToastInRise = 18f, ToastInSeconds = 0.22f, ToastOutSeconds = 0.15f;
    private static RectTransform _toastBgRt;
    private static CanvasGroup _toastGroup;
    private static float _toastFade;

    /// <summary>每帧：Toast 计时消失（先淡出再销毁）</summary>
    private static void TickToast() {
        if (_toastCanvas == null) return;
        _toastTimer -= Time.unscaledDeltaTime;
        if (_toastTimer > 0f) return;
        _toastFade += Time.unscaledDeltaTime / ToastOutSeconds;
        if (_toastGroup != null) _toastGroup.alpha = Mathf.Clamp01(1f - _toastFade);
        if (_toastFade < 1f) return;
        Object.Destroy(_toastCanvas.gameObject);
        _toastCanvas = null;
        _toastText = null;
        _toastBgRt = null;
        _toastGroup = null;
        _toastFade = 0f;
    }

    // ---- 导入命名规则选择 ----
    // 点"导入单个谱面 / 批量导入谱面"时，先弹这个框选命名规则，确认后才打开资源管理器。

    /// <summary>命名规则：0=谱面信息命名法（默认，从音频文件名/谱面推断），1=文件夹名称命名法</summary>
    private static int _importNameMode;
    /// <summary>选完命名规则后要执行的动作（null=用户取消了）</summary>
    private static System.Action _pendingImportAction;

    // 导入弹窗的几何：内容（标题带 + 两个命名规则按钮 + 三个开关）整块在"框顶 ~ 底部按钮上沿"之间垂直居中，
    // 上下留白相等。以前功能区压在框下半部分、标题下面空一大片，重心全在下面。
    // 下面的 y 全部由这组常量推出来，改一个数整块跟着动，不许再往控件上写坐标。
    private const float ImpBoxW = 720f, ImpBoxH = 640f;
    private const float ImpTitleTopPad = 24f;      // 标题带距框顶
    private const float ImpTitleBandH = 96f;       // 标题带高（30 号字一行）
    private const float ImpTitleGap = 40f;         // 标题带 → 功能区
    private const float ImpModeW = 300f, ImpModeH = 64f, ImpModeGapX = 20f, ImpModeGapY = 20f;
    private const float ImpSwitchW = 470f, ImpSwitchH = 46f, ImpSwitchGap = 10f;
    private const float ImpBtnW = 200f, ImpBtnH = 56f, ImpBtnBottom = 26f, ImpBtnGapX = 20f;
    /// <summary>功能区总高 = 命名规则一行 + 间距 + 三个开关两两之间两次间距（推导式，别写死）。</summary>
    private const float ImpFuncH = ImpModeH + ImpModeGapY + ImpSwitchH * 3f + ImpSwitchGap * 2f;

    /// <summary>弹出"请选择命名规则"框；选好并确认后调用 onConfirm</summary>
    private static void ShowImportNameDialog(System.Action onConfirm) {
        try {
            if (_importNameCanvas != null) Object.Destroy(_importNameCanvas.gameObject);
            _pendingImportAction = onConfirm;
            var cv = MakeCanvas("LevelLib_ImportName", 90500);   // 高于批量确认(90000)，别被挡
            _importNameCanvas = cv;
            var bg = MakeDialogVeil(cv.transform);
            var box = MakeDialogBox(cv.transform, "Box", PanelColor);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(ImpBoxW, ImpBoxH);
            // 内容整块垂直居中：可用区 = 底部按钮上沿 ~ 标题带顶，内容 = 标题带 + 间隙 + 功能区，
            // 上下留白相等。所有控件的 y 都从这几个式子里来（局部坐标，原点在框左下角）。
            float impBtnTop = ImpBtnBottom + ImpBtnH;
            float impContentH = ImpTitleBandH + ImpTitleGap + ImpFuncH;
            float impContentBottom = impBtnTop + Mathf.Max(0f, (ImpBoxH - ImpTitleTopPad - impBtnTop - impContentH) * 0.5f);
            float impFuncTop = impContentBottom + ImpFuncH;
            float impTitleBottom = impFuncTop + ImpTitleGap;
            float impHalf = ImpBoxH * 0.5f;
            float impModeY = impFuncTop - ImpModeH * 0.5f;
            float impSw1Y = impModeY - ImpModeH * 0.5f - ImpModeGapY - ImpSwitchH * 0.5f;
            float impSw2Y = impSw1Y - ImpSwitchH - ImpSwitchGap;
            float impSw3Y = impSw2Y - ImpSwitchH - ImpSwitchGap;

            var title = MakeTitle(box.transform, "Title", 30, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, new Vector2(30f, impTitleBottom),
                    new Vector2(-30f, -(ImpBoxH - impTitleBottom - ImpTitleBandH)));
            title.text = Lang.Tr("请选择命名规则");

            // 两个规则按钮（选中的用主色高亮）
            var b1 = MakeButton(box.transform, "Mode0", Vector2.zero, new Vector2(ImpModeW, ImpModeH), ItemColor);
            var r1 = b1.GetComponent<RectTransform>();
            r1.anchorMin = r1.anchorMax = new Vector2(0.5f, 0.5f);
            r1.anchoredPosition = new Vector2(-(ImpModeW + ImpModeGapX) * 0.5f, impModeY - impHalf);
            var t1 = MakeText(b1.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(t1.rectTransform, Vector2.zero, Vector2.one);
            t1.text = Lang.Tr("谱面信息命名法");

            var b2 = MakeButton(box.transform, "Mode1", Vector2.zero, new Vector2(ImpModeW, ImpModeH), ItemColor);
            var r2 = b2.GetComponent<RectTransform>();
            r2.anchorMin = r2.anchorMax = new Vector2(0.5f, 0.5f);
            r2.anchoredPosition = new Vector2((ImpModeW + ImpModeGapX) * 0.5f, impModeY - impHalf);
            var t2 = MakeText(b2.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(t2.rectTransform, Vector2.zero, Vector2.one);
            t2.text = Lang.Tr("文件夹名称命名法");

            // 这行解释小字整块删掉：规矩是没说就别往界面加解释文本。
            // 它原本是绝对定位（Stretch + offset，不占纵向游标），所以删掉不影响下面开关的位置。

            // 高亮必须走 SetUiSelectFill（它同时把新色记成交互态层的常态色并立刻刷一次）：
            // 直接写 Image.color 的话，鼠标一动触发重算就被旧常态色顶掉，高亮闪一下就没了。
            System.Action refresh = () => {
                SetUiSelectFill(b1, _importNameMode == 0 ? Accent : ItemColor);
                SetUiSelectFill(b2, _importNameMode == 1 ? Accent : ItemColor);
            };
            b1.onClick.AddListener(() => { _importNameMode = 0; refresh(); });
            b2.onClick.AddListener(() => { _importNameMode = 1; refresh(); });
            refresh();

            // 三个开关整行 = 左侧标签 + 右侧胶囊开关（SwitchRowLabelInset 是给开关让出的宽度）。
            // 行宽 470：最长那条「导入后删原文件夹（不可恢复）」22 号字约 341px，470 − 18(左) − 96(右) = 356 刚好放得下。
            // 行底色不跟着状态变：状态已经由开关自己表达，再染一遍整行太吵。
            var zipBtn = MakeButton(box.transform, "ZipToggle", Vector2.zero, new Vector2(ImpSwitchW, ImpSwitchH), ItemColor);
            var zrt = zipBtn.GetComponent<RectTransform>();
            zrt.anchorMin = zrt.anchorMax = new Vector2(0.5f, 0.5f);
            zrt.anchoredPosition = new Vector2(0f, impSw1Y - impHalf);
            var zt = MakeText(zipBtn.transform, "T", 22, Color.white, TextAnchor.MiddleLeft);
            Stretch(zt.rectTransform, new Vector2(18f, 0f), new Vector2(-SwitchRowLabelInset, 0f));
            zt.text = Lang.Tr("同时导入压缩包（.zip）");
            Image zipKnob;
            var zipTrack = MakeSwitchVisual(zipBtn.transform, "Switch", out zipKnob);   // 两片都不吃射线 点哪儿都算
            System.Action refreshZip = () => ApplySwitch(zipTrack, zipKnob, BatchImportZips);
            // 建好时这一句是即时到位；点出来的变化要滑动（传给 ApplySwitch 的 animate=true）
            zipBtn.onClick.AddListener(() => {
                BatchImportZips = !BatchImportZips;
                AutoFeatures.Save();
                ApplySwitch(zipTrack, zipKnob, BatchImportZips, true);
            });
            refreshZip();

            // 导入后备份：默认关（见 AutoFeatures.BackupOnImport）—— 开着导入一大批会写满磁盘，
            // 之后连"换背景"的备份都写不进去，所以这里给一个当场可改的开关。
            // 摆位与压缩包开关同款（中心锚点，往下 142）：每加一个开关 y 减 56。
            var bkBtn = MakeButton(box.transform, "BackupToggle", Vector2.zero, new Vector2(ImpSwitchW, ImpSwitchH), ItemColor);
            var brt2 = bkBtn.GetComponent<RectTransform>();
            brt2.anchorMin = brt2.anchorMax = new Vector2(0.5f, 0.5f);
            brt2.anchoredPosition = new Vector2(0f, impSw2Y - impHalf);
            var bt2 = MakeText(bkBtn.transform, "T", 22, Color.white, TextAnchor.MiddleLeft);
            Stretch(bt2.rectTransform, new Vector2(18f, 0f), new Vector2(-SwitchRowLabelInset, 0f));
            bt2.text = Lang.Tr("导入后备份谱面（会占用磁盘）");
            Image bkKnob;
            var bkTrack = MakeSwitchVisual(bkBtn.transform, "Switch", out bkKnob);
            System.Action refreshBk = () => ApplySwitch(bkTrack, bkKnob, AutoFeatures.BackupOnImport);
            bkBtn.onClick.AddListener(() => {
                AutoFeatures.BackupOnImport = !AutoFeatures.BackupOnImport;
                AutoFeatures.Save();
                ApplySwitch(bkTrack, bkKnob, AutoFeatures.BackupOnImport, true);
            });
            refreshBk();

            // 导入后删原文件夹：破坏性选项，安全网三条都实现在 AutoFeatures.DeleteSourceOnImport 里。
            // 这里只负责把连带效果摆在明面上：勾上它会强制打开「导入后备份」并让上面那个开关跟着滑。
            // 文案把「不可恢复」写在按钮上，别藏进 hint。位置 y=-198（第三个），与前两个相距 56。
            var delBtn = MakeButton(box.transform, "DeleteSrcToggle", Vector2.zero, new Vector2(ImpSwitchW, ImpSwitchH), ItemColor);
            var drt = delBtn.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(0f, impSw3Y - impHalf);
            var dt = MakeText(delBtn.transform, "T", 22, Color.white, TextAnchor.MiddleLeft);
            Stretch(dt.rectTransform, new Vector2(18f, 0f), new Vector2(-SwitchRowLabelInset, 0f));
            dt.text = Lang.Tr("导入后删原文件夹（不可恢复）");
            Image delKnob;
            var delTrack = MakeSwitchVisual(delBtn.transform, "Switch", out delKnob);
            System.Action refreshDel = () => ApplySwitch(delTrack, delKnob, AutoFeatures.DeleteSourceOnImport);
            delBtn.onClick.AddListener(() => {
                AutoFeatures.DeleteSourceOnImport = !AutoFeatures.DeleteSourceOnImport;
                if (AutoFeatures.DeleteSourceOnImport && !AutoFeatures.BackupOnImport) {
                    AutoFeatures.BackupOnImport = true;   // 安全网①：删之前必须有备份
                    ApplySwitch(bkTrack, bkKnob, true, true);
                }
                AutoFeatures.Save();
                ApplySwitch(delTrack, delKnob, AutoFeatures.DeleteSourceOnImport, true);
            });
            refreshDel();

            var okBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(ImpBtnW, ImpBtnH), new Color(0.2f, 0.7f, 0.3f, 1f));
            var okRt = okBtn.GetComponent<RectTransform>();
            okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = new Vector2(-(ImpBtnW + ImpBtnGapX) * 0.5f, ImpBtnBottom);
            var okT = MakeText(okBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
            okT.text = Lang.Tr("确认");
            okBtn.onClick.AddListener(() => {
                var act = _pendingImportAction;
                _pendingImportAction = null;
                Object.Destroy(cv.gameObject);
                _importNameCanvas = null;
                if (act != null) act();     // 确认后才打开资源管理器
            });

            var noBtn = MakeButton(box.transform, "Cancel", Vector2.zero, new Vector2(ImpBtnW, ImpBtnH), new Color(0.7f, 0.3f, 0.3f, 1f));
            var noRt = noBtn.GetComponent<RectTransform>();
            noRt.anchorMin = noRt.anchorMax = new Vector2(0.5f, 0f);
            noRt.pivot = new Vector2(0.5f, 0f);
            noRt.anchoredPosition = new Vector2((ImpBtnW + ImpBtnGapX) * 0.5f, ImpBtnBottom);
            var noT = MakeText(noBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            Stretch(noT.rectTransform, Vector2.zero, Vector2.one);
            noT.text = Lang.Tr("取消");
            noBtn.onClick.AddListener(() => {
                _pendingImportAction = null;
                Object.Destroy(cv.gameObject);
                _importNameCanvas = null;
            });
        } catch (System.Exception e) {
            _pendingImportAction = null;
            ReportError("打开「选择命名规则」框失败（导入已中止）", e);
        }
    }

    /// <summary>按当前命名规则给谱面起名</summary>
    private static string TitleByMode(string folder) {
        if (_importNameMode == 1) {
            string dn = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(dn)) return dn;   // 文件夹名称命名法
        }
        return LevelScanner.SuggestTitle(folder);       // 默认：谱面信息命名法
    }

    // ---- 删除全部谱面与使用数据（设置 → 检验 → 标记）----

    private static Canvas _resetAllCanvas;
    private static Button _resetOkBtn;
    private static Text _resetOkText;
    private static Text _resetCountText;
    private static float _resetDelay;

    // ---- 异常通关记录清理确认框（设置 → 工具 → 数据管理 → 「检查并清理」）----
    // 先把会删多少条报出来，确认后才动手，真删时自动备份 records.txt。
    private static Canvas _recCleanCanvas;
    private static Button _recCleanOkBtn;
    private static Text _recCleanInfoText;
    private static Text _recCleanOkText;
    private static int _recCleanBad;
    private static int _recCleanSuspect;

    /// <summary>「检查并清理异常通关记录」确认框：只读扫描 + 报数，确认后才真删</summary>
    private static void ShowRecordsCheckConfirm() {
        if (_recCleanCanvas != null) return;
        try {
            LevelScanner.LoadRecords(Main.ModEntry.Path);       // 先读一遍最新的（别处可能改过）
            _recCleanBad = LevelScanner.CountRejectableRecords(out int keep);
            _recCleanSuspect = LevelScanner.CountSuspectGapRecords();

            var cv = MakeCanvas("LevelLib_RecClean", 90600);
            _recCleanCanvas = cv;
            var bg = MakeDialogVeil(cv.transform);
            var box = MakeDialogBox(cv.transform, "Box", PanelColor);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(860f, 460f);

            var title = MakeTitle(box.transform, "Title", 30, new Color(1f, 0.85f, 0.45f, 1f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, new Vector2(30f, 388f), new Vector2(-30f, -20f));
            title.text = Lang.Tr("检查并清理异常通关记录");

            var body = MakeText(box.transform, "Body", 22, new Color(0.92f, 0.92f, 0.96f, 1f), TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.25f;
            Stretch(body.rectTransform, new Vector2(40f, 120f), new Vector2(-40f, -70f));
            // 正文必须逐行包 Lang.Tr：翻译层是按整串查词条的，
            // 先拼成一大串再翻译会一句都翻不到（词条表里存的是单行）。
            body.text = Lang.Tr("只清理这四种没有争议的记录：\n")
                + Lang.Tr("  · 开过自动播放（Autoplay）\n")
                + Lang.Tr("  · 开过不会失败（No Fail）\n")
                + Lang.Tr("  · 用了检查点（旧规则留下的）\n")
                + Lang.Tr("  · 准确度为 0 的空壳数据\n\n")
                + Lang.Tr(" 空敲（没有砖块时按下的那一下，记在「太早/很早」里）不算异常，一律保留。\n")
                + Lang.Tr(" 判定数比谱面少一点点也只是放行 —— 只有少得离谱的才会单独列出来供你自己判断。\n")
                + Lang.Tr("删除前会自动把 records.txt 备份一份，可以回滚。");

            _recCleanInfoText = MakeText(box.transform, "Count", 22, new Color(1f, 0.85f, 0.4f, 1f), TextAnchor.MiddleCenter);
            Stretch(_recCleanInfoText.rectTransform, new Vector2(40f, 74f), new Vector2(-40f, -370f));
            _recCleanInfoText.text = _recCleanBad > 0
                ? Lang.TrFmt("扫到 {0} 条建议清理；另有 {1} 条判定数偏少（保留观察，不删）", _recCleanBad, _recCleanSuspect)
                : Lang.TrFmt("没有需要清理的记录（另有 {0} 条判定数偏少，保留观察）", _recCleanSuspect);

            _recCleanOkBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(300f, 56f), new Color(0.62f, 0.42f, 0.20f, 1f));
            var okRt = _recCleanOkBtn.GetComponent<RectTransform>();
            okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = new Vector2(-160f, 26f);
            _recCleanOkText = MakeText(_recCleanOkBtn.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(_recCleanOkText.rectTransform, Vector2.zero, Vector2.one);
            _recCleanOkText.text = _recCleanBad > 0 ? (Lang.Tr("确认清理 ") + _recCleanBad + Lang.Tr(" 条")) : Lang.Tr("没有要清理的");
            _recCleanOkBtn.interactable = _recCleanBad > 0;
            _recCleanOkBtn.onClick.AddListener(OnRecordsCleanConfirmed);

            var noBtn = MakeButton(box.transform, "Cancel", Vector2.zero, new Vector2(220f, 56f), new Color(0.25f, 0.4f, 0.6f, 1f));
            var noRt = noBtn.GetComponent<RectTransform>();
            noRt.anchorMin = noRt.anchorMax = new Vector2(0.5f, 0f);
            noRt.pivot = new Vector2(0.5f, 0f);
            noRt.anchoredPosition = new Vector2(170f, 26f);
            var noT = MakeText(noBtn.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(noT.rectTransform, Vector2.zero, Vector2.one);
            noT.text = Lang.Tr("取消");
            noBtn.onClick.AddListener(CloseRecordsCleanConfirm);
        } catch (System.Exception e) {
            ReportError("打开「检查并清理异常通关记录」确认框失败", e);
        }
    }

    private static void OnRecordsCleanConfirmed() {
        try {
            int removed = LevelScanner.PruneLegacyClearRecords();
            CloseRecordsCleanConfirm();
            if (removed > 0) ShowToast($"已清理 {removed} 条异常通关记录（records.txt 已自动备份）");
            else if (removed == 0) ShowToast(Lang.Tr("没有需要清理的记录"));
            else ShowToast(Lang.Tr("清理未执行：备份失败（详见日志）"));
        } catch (System.Exception e) {
            CloseRecordsCleanConfirm();
            ReportError("清理异常通关记录失败", e);
        }
    }

    private static void CloseRecordsCleanConfirm() {
        if (_recCleanCanvas != null) Object.Destroy(_recCleanCanvas.gameObject);
        _recCleanCanvas = null;
        _recCleanOkBtn = null;
        _recCleanOkText = null;
        _recCleanInfoText = null;
    }

    /// <summary>弹"删除所有谱面以及使用数据"确认框。确认键 3 秒后才可点（防手滑）。</summary>
    private static void ShowResetAllConfirm() {
        if (_resetAllCanvas != null) return;
        try {
            var cv = MakeCanvas("LevelLib_ResetAll", 90600);
            _resetAllCanvas = cv;
            var bg = MakeDialogVeil(cv.transform);
            var box = MakeDialogBox(cv.transform, "Box", PanelColor);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(860f, 440f);

            var title = MakeTitle(box.transform, "Title", 30, new Color(1f, 0.4f, 0.4f, 1f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, new Vector2(30f, 368f), new Vector2(-30f, -20f));
            title.text = Lang.Tr("删除所有谱面");

            var body = MakeText(box.transform, "Body", 22, new Color(0.92f, 0.92f, 0.96f, 1f), TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.25f;
            Stretch(body.rectTransform, new Vector2(40f, 120f), new Vector2(-40f, -70f));
            body.text = Lang.Tr("这会删除：\n")
                + "  · 备份/导入的全部谱面文件（backup、imported 目录）\n"
                + "  · 谱面列表（列表里所有谱面都会被清空）\n"
                + "  · 这些谱面的数据：通关记录、游玩次数、完成度、寄录图、X 准确度、\n"
                + "     谱面标记、游玩日志、收藏、备注、显示名覆盖\n\n"
                + "保留：成就、登录天数，以及你的个性化设置\n"
                + "（背景、头像、背景音乐、自定义备份位置、热键等都不受影响）。\n\n"
                + "此操作不可恢复！";

            _resetCountText = MakeText(box.transform, "Count", 22, new Color(1f, 0.85f, 0.4f, 1f), TextAnchor.MiddleCenter);
            Stretch(_resetCountText.rectTransform, new Vector2(40f, 84f), new Vector2(-40f, -350f));
            _resetCountText.text = "";

            _resetOkBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(240f, 56f), new Color(0.7f, 0.25f, 0.25f, 1f));
            var okRt = _resetOkBtn.GetComponent<RectTransform>();
            okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = new Vector2(-130f, 26f);
            _resetOkText = MakeText(_resetOkBtn.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(_resetOkText.rectTransform, Vector2.zero, Vector2.one);
            _resetOkBtn.onClick.AddListener(OnResetAllConfirmed);
            SetResetOkEnabled(false);

            var noBtn = MakeButton(box.transform, "Cancel", Vector2.zero, new Vector2(240f, 56f), new Color(0.25f, 0.4f, 0.6f, 1f));
            var noRt = noBtn.GetComponent<RectTransform>();
            noRt.anchorMin = noRt.anchorMax = new Vector2(0.5f, 0f);
            noRt.pivot = new Vector2(0.5f, 0f);
            noRt.anchoredPosition = new Vector2(130f, 26f);
            var noT = MakeText(noBtn.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(noT.rectTransform, Vector2.zero, Vector2.one);
            noT.text = Lang.Tr("取消");
            noBtn.onClick.AddListener(CloseResetAllConfirm);

            _resetDelay = 3f;
        } catch (System.Exception e) {
            ReportError("打开「删除全部数据」确认框失败", e);
        }
    }

    /// <summary>确认键倒计时（每帧由面板 Tick 驱动，3 秒内不可点）</summary>
    private static void TickResetAllConfirm() {
        if (_resetAllCanvas == null) return;
        if (_resetDelay > 0f) {
            _resetDelay -= Time.unscaledDeltaTime;
            int left = Mathf.CeilToInt(Mathf.Max(0f, _resetDelay));
            if (_resetOkText != null) _resetOkText.text = left > 0 ? (Lang.Tr("确认删除（") + left + "）") : Lang.Tr("确认删除");
            if (_resetDelay <= 0f) SetResetOkEnabled(true);
        }
    }

    private static void SetResetOkEnabled(bool on) {
        if (_resetOkBtn == null) return;
        _resetOkBtn.interactable = on;
        if (_resetOkText != null && !on) _resetOkText.text = Lang.TrFmt("确认删除（{0}）", Mathf.CeilToInt(Mathf.Max(0f, _resetDelay)));
        var img = _resetOkBtn.GetComponent<Image>();
        if (img != null) {
            var c = img.color;
            img.color = on ? new Color(c.r, c.g, c.b, 1f) : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.85f);
        }
    }

    private static void CloseResetAllConfirm() {
        if (_resetAllCanvas != null) Object.Destroy(_resetAllCanvas.gameObject);
        _resetAllCanvas = null;
        _resetOkBtn = null;
        _resetOkText = null;
        _resetCountText = null;
        _resetDelay = 0f;
    }

    private static void OnResetAllConfirmed() {
        if (_resetDelay > 0f) return;   // 倒计时没好，忽略
        var runner = new GameObject("__ResetAllHost").AddComponent<ResetAllRunner>();
        runner.Begin();
    }

    /// <summary>后台执行"删除所有谱面"（协程分帧，避免卡住主线程）：只删谱面及其游玩数据，不动个性化设置。
    /// 保留 settings.txt（背景/头像/音乐/备份位置等）、hotkey.txt、成就、登录天数、自定义背景/头像素材。</summary>
    private class ResetAllRunner : MonoBehaviour {
        public void Begin() { StartCoroutine(Run()); }
        private System.Collections.IEnumerator Run() {
            int deleted = 0, failed = 0;
            // ① 谱面目录：整体删掉（backup / imported）
            string[] dirs = { "backup", "imported" };
            foreach (var d in dirs) {
                if (_resetCountText != null) _resetCountText.text = Lang.TrFmt("正在删除 {0} …", d);
                yield return null;
                try {
                    string p = Path.Combine(Main.ModEntry.Path, d);
                    if (Directory.Exists(p)) { Directory.Delete(p, true); deleted++; }
                } catch (System.Exception e) { failed++; Lg.Ex("删除目录失败: " + d, e); }
            }
            // ② 只删"谱面相关"的数据文件（个性化设置与成就/登录天数一律保留）
            // 收藏、备注、标记都是按谱面文件夹路径存的，谱面删了就成孤儿数据，所以一并删除。
            string[] files = {
                "recorded.txt",    // 已导入谱面的列表
                "records.txt",     // 通关记录
                "plays.txt",       // 游玩次数
                "playlog.txt",     // 游玩日志（周报用）
                "deathmap.txt",    // 寄录图（死点热力图）
                "progress.txt",    // 完成度
                "xacc.txt",        // X 准确度
                "marks.txt",       // 谱面标记
                "favorites.txt",   // 收藏
                "notes.txt",       // 备注
                "titles.txt",      // 显示名覆盖（同样是按谱面路径存的）
            };
            for (int i = 0; i < files.Length; i++) {
                if (_resetCountText != null) _resetCountText.text = Lang.TrFmt("正在删除谱面数据 {0}/{1} …", i + 1, files.Length);
                if (i % 4 == 0) yield return null;   // 分帧
                try {
                    string p = Path.Combine(Main.ModEntry.Path, files[i]);
                    if (File.Exists(p)) { File.Delete(p); deleted++; }
                } catch (System.Exception e) { failed++; Lg.Ex("删除文件失败: " + files[i], e); }
            }
            // ③ 内存数据同步清空并重新加载（界面立刻变回空库）
            if (_resetCountText != null) _resetCountText.text = Lang.Tr("正在刷新列表 …");
            yield return null;
            try {
                LevelScanner.Entries.Clear();               // 列表清空
                MarkStore.Load(Main.ModEntry.Path);
                LevelScanner.LoadRecorded();
                LevelScanner.LoadPlays(Main.ModEntry.Path);
                LevelScanner.LoadRecords(Main.ModEntry.Path);
                LevelScanner.LoadDeathMap(Main.ModEntry.Path);
                LevelScanner.LoadProgress(Main.ModEntry.Path);
                LevelScanner.LoadXaccs(Main.ModEntry.Path);
                LevelScanner.SortEntries();
            } catch (System.Exception e) { Lg.Ex("清空内存谱面数据失败", e); }

            CloseResetAllConfirm();
            if (_panelOpen) {
                _selectedPath = null;
                _detailEntry = null;
                CloseLevelPickDialog();
                ApplyDetailEmptyState();
                RefreshList();
            }
            ShowToast($"已删除全部谱面（删除 {deleted} 项，失败 {failed} 项）喵");
            Lg.Info($"[LevelLib] 已执行「删除所有谱面」：删除 {deleted} 项，失败 {failed} 项（个性化设置与收藏/备注/成就已保留）");
            Destroy(gameObject);
        }
    }

    // ---- 更新弹窗已全部移除 ----
    // 更新信息只在 设置 → 检验 → 更新 的状态行里显示，不再弹窗。

    /// <summary>批量导入确认框：扫描到大量文件夹时先确认，防误选盘符根目录导出一大堆</summary>
    private static void ShowBatchConfirmDialog() {
        try {
            if (_batchConfirmCanvas != null) return;
            _batchWaitingConfirm = true;
            var cv = MakeCanvas("LevelLib_BatchConfirm", 90000);   // 高于引导(88000)，别被挡
            _batchConfirmCanvas = cv;
            var bg = MakeDialogVeil(cv.transform);
            var box = MakeDialogBox(cv.transform, "Box", PanelColor);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(680f, 320f);
            var t = MakeText(box.transform, "Msg", 26, Color.white, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, new Vector2(40f, 120f), new Vector2(-40f, -60f));
            t.text = Lang.TrFmt("扫描范围：{0}\n扫描到 {1} 个含谱面的文件夹\n确认要全部导入吗？\n(选错文件夹可点取消)", _batchRoot, _batchFolders.Count)
                + (_batchScanFailed > 0 ? $"\n 有 {_batchScanFailed} 个目录扫描失败（可能被跳过）" : "");
            var okBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(220f, 56f), new Color(0.2f, 0.7f, 0.3f, 1f));
            var okRt = okBtn.GetComponent<RectTransform>();
            okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = new Vector2(-120f, 30f);
            var okT = MakeText(okBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            okT.text = Lang.Tr("确认导入");
            Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
            okBtn.onClick.AddListener(() => { Object.Destroy(cv.gameObject); _batchConfirmCanvas = null; _batchWaitingConfirm = false; });
            var noBtn = MakeButton(box.transform, "Cancel", Vector2.zero, new Vector2(220f, 56f), new Color(0.7f, 0.3f, 0.3f, 1f));
            var noRt = noBtn.GetComponent<RectTransform>();
            noRt.anchorMin = noRt.anchorMax = new Vector2(0.5f, 0f);
            noRt.pivot = new Vector2(0.5f, 0f);
            noRt.anchoredPosition = new Vector2(120f, 30f);
            var noT = MakeText(noBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            noT.text = Lang.Tr("取消");
            Stretch(noT.rectTransform, Vector2.zero, Vector2.one);
            noBtn.onClick.AddListener(() => { Object.Destroy(cv.gameObject); _batchConfirmCanvas = null; _batchWaitingConfirm = false; _batchRunning = false; CloseBatchProgress(); ShowToast(Lang.Tr("已取消批量导入")); });
        } catch (System.Exception e) {
            _batchWaitingConfirm = false;
            ReportError("打开批量导入确认框失败（导入已中止）", e);
        }
    }

    // ---- 成就系统 ----
    /// <summary>成就每帧驱动：解锁提示窗口计时 + 5 级名字渐变。
    /// 成就列表弹窗已删除，列表显示全在顶层「成就」tab 里（见 PortalUI.Achievements.cs）；
    /// 这里只保留面板外的两样：解锁提示小窗、5 级名字彩虹渐变。</summary>
    private static void TickAchievements() {
        // 成就解锁提示的 Esc 收法在 PortalUI.EscClose.cs 那条链里（一处读 Esc，避免同帧收两层）
        // 解锁提示窗口：驱动上滑 + 停留 20 秒后淡出
        TickAchHint();
        // 一轮播完接着弹下一个（同一次通关常会同时解锁好几个成就）
        PumpAchHintQueue();
        // 5级成就名字：缓慢 RGB 渐变（现在由成就页自己的卡片池驱动，这里只兜底旧引用）
        if (_achRainbowTexts.Count > 0) {
            float hue = (Time.unscaledTime * 0.08f) % 1f;   // 慢速（约12秒一圈）
            Color rc = Color.HSVToRGB(hue, 0.85f, 1f);
            for (int i = _achRainbowTexts.Count - 1; i >= 0; i--) {
                var t = _achRainbowTexts[i];
                if (t == null) { _achRainbowTexts.RemoveAt(i); continue; }
                t.color = rc;
            }
        }
    }

    /// <summary>解锁提示每帧：上滑升入 + 停留 20 秒 + 滑出消失（点击可提前收回）</summary>
    private static void TickAchHint() {
        if (!_achHintActive || _achHintCanvas == null) return;
        _achHintT += Time.unscaledDeltaTime;
        // 用建窗时存下的 RectTransform；万一它被销毁了（Unity 假 null）就退回原来那两条查找，
        // 行为与改前完全一致，只是慢一点 —— 不为了性能把"窗没了"变成"崩"。
        var wr = _achHintWinRt;
        if (wr == null) {
            var win = _achHintCanvas.transform.Find("Win");
            if (win == null) return;
            wr = _achHintWinRt = win.GetComponent<RectTransform>();
        }
        // 阶段：0-0.4 上滑升入；0.4-20.4 停留；20.4-20.8 往下收回
        const float holdEnd = AchIn + AchHold;
        float oy;
        if (_achHintT < AchIn) {
            float k = _achHintT / AchIn;
            oy = Mathf.Lerp(-130f, 40f, k);   // 从屏幕外升上来
        } else if (_achHintT < holdEnd) {
            oy = 40f;
        } else if (_achHintT < holdEnd + AchOut) {
            float k = (_achHintT - holdEnd) / AchOut;
            oy = Mathf.Lerp(40f, -160f, k);   // 往下收回
        } else {
            _achHintActive = false;
            _achHintCanvas.gameObject.SetActive(false);
            return;
        }
        wr.anchoredPosition = new Vector2(-30f, oy);
    }

    public static void ShowMarkDialog(LevelEntry entry) {
        if (_markCanvas != null) return;
        _markEntry = entry;
        _pendingMark = MarkStore.Get(entry.FilePath);        // 暂存值，确认时才写回
        _pendingJudge = MarkStore.GetJudge(entry.FilePath);
        _pendingSpecials = MarkStore.GetSpecials(entry.FilePath);
        _markTab = 0;
        _markCanvas = MakeCanvas("LevelLib_Mark", 85000);
        var bg = MakeDialogVeil(_markCanvas.transform);

        var box = MakeDialogBox(_markCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(960f, 640f);

        var title = MakeTitle(box.transform, "选择标记", 34, Color.white, TextAnchor.MiddleCenter);
        title.rectTransform.anchorMin = new Vector2(0f, 0.92f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;

        var nameLabel = MakeText(box.transform, "Name", 24, new Color(1f, 0.65f, 0.2f, 1f), TextAnchor.MiddleCenter);
        nameLabel.text = entry.Title;
        nameLabel.rectTransform.anchorMin = new Vector2(0f, 0.855f);
        nameLabel.rectTransform.anchorMax = new Vector2(1f, 0.915f);
        nameLabel.rectTransform.offsetMin = Vector2.zero;
        nameLabel.rectTransform.offsetMax = Vector2.zero;

        // 清除标记（box 下方外面）：立即清除（写盘）+ 列表立刻刷新，界面不关，可继续选新标记
        var clearBtn = MakeButton(box.transform, Lang.Tr("清除标记"), new Vector2(-160f, -375f), new Vector2(150f, 42f), new Color(0.5f, 0.3f, 0.3f, 1f));
        var clearText = MakeText(clearBtn.transform, "清除标记", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(clearText.rectTransform, Vector2.zero, Vector2.one);
        clearBtn.onClick.AddListener(() => {
            if (_markEntry != null) {
                MarkStore.Set(_markEntry.FilePath, null);
                MarkStore.SetJudge(_markEntry.FilePath, null);
                MarkStore.SetSpecials(_markEntry.FilePath, null);
            }
            _pendingMark = null;   // 暂存一起清掉，避免再点确定时又把旧选择写回去
            _pendingJudge = null;
            _pendingSpecials = null;
            RefreshMarkGrid(box);
            RefreshList();   // 列表图标立刻消失
            // 里程碑跟标记是强绑定，标记没了就要跟着撤销
            try { Achievements.RebuildMilestones(false); } catch (System.Exception) { }
        });

        // 取消（清除标记左边）：放弃暂存选择并关闭界面
        var cancelBtn = MakeButton(box.transform, Lang.Tr("取消"), new Vector2(-320f, -375f), new Vector2(120f, 42f), ItemColor);
        var cancelText = MakeText(cancelBtn.transform, "取消", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancelBtn.onClick.AddListener(() => CloseMarkDialog());

        // 确定（box 下方外面）：应用暂存的选择并关闭
        var okBtn = MakeButton(box.transform, Lang.Tr("确定"), new Vector2(160f, -375f), new Vector2(120f, 42f), Accent);
        var okText = MakeText(okBtn.transform, "确定", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        okBtn.onClick.AddListener(() => ApplyPendingMarkAndClose());

        // 左侧 4 个 tab
        _markTabButtons.Clear();
        for (int i = 0; i < MarkTabNames.Length; i++) {
            int idx = i;
            var tabBtn = MakeButton(box.transform, "Tab" + i, new Vector2(-380f, 180f - i * 56f), new Vector2(150f, 46f), i == 0 ? Accent : ItemColor);
            var tabText = MakeText(tabBtn.transform, "T", 24, Color.white, TextAnchor.MiddleCenter);
            tabText.text = MarkTabNames[i];
            Stretch(tabText.rectTransform, Vector2.zero, Vector2.one);
            tabBtn.onClick.AddListener(() => { _markTab = idx; RefreshMarkGrid(box); });
            _markTabButtons.Add(tabBtn);
        }

        // 网格区域（起点 y 0.09，避开下面的判定按钮）
        var gridView = MakeImage(box.transform, "GridView", ItemColor);
        gridView.rectTransform.anchorMin = new Vector2(0.30f, 0.09f);
        gridView.rectTransform.anchorMax = new Vector2(0.98f, 0.84f);
        gridView.rectTransform.offsetMin = Vector2.zero;
        gridView.rectTransform.offsetMax = Vector2.zero;
        gridView.gameObject.AddComponent<RectMask2D>();
        var gridScroll = gridView.gameObject.AddComponent<ScrollRect>();
        gridScroll.horizontal = false;
        gridScroll.vertical = true;
        gridScroll.scrollSensitivity = 24f;
        gridScroll.movementType = ScrollRect.MovementType.Clamped;
        gridScroll.viewport = gridView.GetComponent<RectTransform>();

        var gridContent = new GameObject("GridContent", typeof(RectTransform));
        gridContent.transform.SetParent(gridView.transform, false);
        var gcrt = gridContent.GetComponent<RectTransform>();
        gcrt.anchorMin = new Vector2(0f, 1f);
        gcrt.anchorMax = new Vector2(1f, 1f);
        gcrt.pivot = new Vector2(0.5f, 1f);
        gcrt.sizeDelta = new Vector2(0f, 0f);
        var grid = gridContent.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(100f, 100f);
        grid.spacing = new Vector2(10f, 10f);
        grid.padding = new RectOffset(10, 10, 10, 10);
        grid.childAlignment = TextAnchor.UpperLeft;
        var gFitter = gridContent.AddComponent<ContentSizeFitter>();
        gFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        gridScroll.content = gcrt;

        // 判定标记：宽判/标判/严判三选一，可与主标记并存
        RefreshMarkGrid(box);
    }

    /// <summary>把当前暂存的选择真正写进去并关闭界面（"确定"和"双击快速标记"共用这一条路）。</summary>
    private static void ApplyPendingMarkAndClose() {
        if (_markEntry != null) {
            MarkStore.Set(_markEntry.FilePath, _pendingMark);
            MarkStore.SetJudge(_markEntry.FilePath, _pendingJudge);
            MarkStore.SetSpecials(_markEntry.FilePath, _pendingSpecials);
            // 标记改变后立刻刷新详情区（难度图标/等级标记/特殊图标），无需切换谱面
            // 注意：必须在 CloseMarkDialog() 前调用，否则 _markEntry 被置 null 会跳过
            RefreshDetailInfo(_markEntry);
        }
        CloseMarkDialog();
        RefreshList();
        // 里程碑跟"当前标记"强绑定：改完标记立刻重算（P16 改成 P10 就撤销 P16、改算 P10；升级则当场解锁并弹提示）
        try { Achievements.RebuildMilestones(false); } catch (System.Exception) { }
    }

    private static void CloseMarkDialog() {
        if (_markCanvas != null) Object.Destroy(_markCanvas.gameObject);
        _markCanvas = null;
        _markEntry = null;
        _pendingMark = null;
        _pendingJudge = null;
        _pendingSpecials = null;
        _markItems.Clear();
    }

    /// <summary>检验确认窗：开始检验？</summary>
    private static void ShowVerifyConfirm() {
        if (_verifyConfirmWin != null) return;
        _verifyConfirmWin = MakeCanvas("LevelLib_VerifyConfirm", 86500);
        var bg = MakeDialogVeil(_verifyConfirmWin.transform);
        var box = MakeDialogBox(_verifyConfirmWin.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(460f, 220f);
        var txt = MakeTitle(box.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
        txt.text = Lang.Tr("开始检验？");
        txt.rectTransform.anchorMin = new Vector2(0f, 0.55f);
        txt.rectTransform.anchorMax = new Vector2(1f, 0.95f);
        txt.rectTransform.offsetMin = Vector2.zero; txt.rectTransform.offsetMax = Vector2.zero;
        var sub = MakeText(box.transform, "Sub", 17, new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleCenter);
        sub.text = Lang.Tr("将自动删除重复谱面（保留主谱面）");
        sub.rectTransform.anchorMin = new Vector2(0f, 0.28f);
        sub.rectTransform.anchorMax = new Vector2(1f, 0.52f);
        sub.rectTransform.offsetMin = Vector2.zero; sub.rectTransform.offsetMax = Vector2.zero;
        var ok = MakeButton(box.transform, "Ok", new Vector2(80f, -75f), new Vector2(140f, 42f), Accent);
        var okText = MakeText(ok.transform, "开始", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => { CloseVerifyConfirm(); StartVerify(); });
        var cancel = MakeButton(box.transform, "Cancel", new Vector2(-80f, -75f), new Vector2(140f, 42f), ItemColor);
        var cancelText = MakeText(cancel.transform, "取消", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => CloseVerifyConfirm());
    }

    private static void CloseVerifyConfirm() {
        if (_verifyConfirmWin != null) { Object.Destroy(_verifyConfirmWin); _verifyConfirmWin = null; }
    }

    /// <summary>进度窗：进度条 + 状态 + 取消</summary>
    private static void ShowVerifyProgress() {
        if (_verifyProgressWin != null) return;
        _verifyProgressWin = MakeCanvas("LevelLib_VerifyProgress", 86600);
        var bg = MakeDialogVeil(_verifyProgressWin.transform);
        var box = MakeDialogBox(_verifyProgressWin.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(520f, 240f);
        var title = MakeText(box.transform, "T", 26, Color.white, TextAnchor.MiddleCenter);
        title.text = Lang.Tr("重复检验中…");
        title.rectTransform.anchorMin = new Vector2(0f, 0.78f);
        title.rectTransform.anchorMax = new Vector2(1f, 0.98f);
        title.rectTransform.offsetMin = Vector2.zero; title.rectTransform.offsetMax = Vector2.zero;
        // 进度条底
        var barBg = MakeImage(box.transform, "BarBg", new Color(0.15f, 0.18f, 0.25f, 1f));
        barBg.rectTransform.anchorMin = barBg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        barBg.rectTransform.sizeDelta = new Vector2(420f, 26f);
        barBg.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        // 进度条填充
        var fill = MakeImage(barBg.transform, "Fill", Accent);
        _verifyFill = fill;
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(1f, 1f);
        frt.offsetMin = new Vector2(2f, 2f); frt.offsetMax = new Vector2(-2f, -2f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0f;
        // 状态文字
        var info = MakeText(box.transform, "Info", 22, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleCenter);
        info.text = "0 / 0";
        info.rectTransform.anchorMin = new Vector2(0f, 0.42f);
        info.rectTransform.anchorMax = new Vector2(1f, 0.60f);
        info.rectTransform.offsetMin = Vector2.zero; info.rectTransform.offsetMax = Vector2.zero;
        _verifyInfo = info;
        // 取消：立即关窗 + 代际作废旧线程（已删的不撤销）
        var cancel = MakeButton(box.transform, "Cancel", new Vector2(0f, -75f), new Vector2(140f, 42f), ItemColor);
        var cancelText = MakeText(cancel.transform, "取消检验", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => {
            _verifyGen++;   // 作废旧线程（它下次检查点时自动退出，结果丢弃）
            _verifyCancel = true;
            _verifyRunning = false;
            CloseVerifyProgress();
            ShowToast(Lang.Tr("检验已取消喵"));
        });
    }

    private static void CloseVerifyProgress() {
        if (_verifyProgressWin != null) { Object.Destroy(_verifyProgressWin); _verifyProgressWin = null; }
        _verifyFill = null;
        _verifyInfo = null;
    }

    private static void ShowMissingDialog(string path) {
        if (_missingCanvas != null) return;
        _missingCanvas = MakeCanvas("LevelLib_Missing", 84000);
        var bg = MakeDialogVeil(_missingCanvas.transform);

        var box = MakeDialogBox(_missingCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(680f, 260f);

        var title = MakeTitle(box.transform, "文件缺失", 34, new Color(1f, 0.7f, 0.3f, 1f), TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        title.rectTransform.anchorMin = new Vector2(0f, 0.78f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;

        var msg = MakeText(box.transform, "Msg", 24, Color.white, TextAnchor.MiddleCenter);
        msg.text = Lang.Tr("谱面文件不存在：\n") + path;
        msg.rectTransform.anchorMin = new Vector2(0.06f, 0.24f);
        msg.rectTransform.anchorMax = new Vector2(0.94f, 0.72f);
        msg.rectTransform.offsetMin = Vector2.zero;
        msg.rectTransform.offsetMax = Vector2.zero;

        var ok = MakeButton(box.transform, Lang.Tr("确定"), new Vector2(0f, -95f), new Vector2(160f, 44f), ItemColor);
        var okText = MakeText(ok.transform, "确定", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => {
            if (_missingCanvas != null) Object.Destroy(_missingCanvas.gameObject);
            _missingCanvas = null;
        });
    }

}
