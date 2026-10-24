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

/// <summary>PortalUI 的报错弹窗分片：统一的错误上报接口（ReportError / ReportErrorQuiet / Guard）。
/// 多条报错排队显示，点「确定」看下一条，不会互相覆盖。</summary>
public static partial class PortalUI {

    // ---------- 对外接口 ----------

    /// <summary>上报一个错误：记录日志并弹窗提示（最常用）</summary>
    public static void ReportError(string context, System.Exception ex) {
        LogError(context, ex);
        EnqueueError(BuildErrorText(context, ex));
    }

    /// <summary>上报一个错误（没有异常对象时用）</summary>
    public static void ReportError(string context) {
        Lg.Info($"[LevelLib] [错误] {context}");
        EnqueueError(context);
    }

    /// <summary>只记录日志、不弹窗（高频路径或可自行恢复的情况用）</summary>
    public static void ReportErrorQuiet(string context, System.Exception ex) {
        LogError(context, ex);
    }

    /// <summary>把一段可能抛异常的操作包起来执行：出错则记录+弹窗，返回是否成功</summary>
    public static bool Guard(string context, System.Action act) {
        try { act(); return true; }
        catch (System.Exception ex) { ReportError(context, ex); return false; }
    }

    /// <summary>Guard 的带返回值版本：失败时返回 fallback</summary>
    public static T Guard<T>(string context, System.Func<T> func, T fallback = default) {
        try { return func(); }
        catch (System.Exception ex) { ReportError(context, ex); return fallback; }
    }

    // ---------- 内部实现 ----------

    /// <summary>弹窗里最多同时记录的条数（避免刷屏把内存吃光）</summary>
    private const int ErrorQueueMax = 20;

    private static Canvas _errorCanvas;
    private static Text _errorText;
    private static Text _errorCountText;
    // 用 List 当队列（本项目引用的程序集里没有 Queue<T>）：从头部取、尾部加
    private static readonly List<string> _errorQueue = new();
    private static int _errorShown;      // 主动弹过多少次（用于「本次会话已出现 N 个错误」计数）
    /// <summary>同一条错误在"本轮提示结束前"只入队一次，防止循环里每帧刷屏。
    /// 关闭弹窗时必须整批清空，否则同一条错误以后永远弹不出来。</summary>
    private static readonly HashSet<string> _errorDedup = new();

    /// <summary>拼出人类可读的错误文本</summary>
    private static string BuildErrorText(string context, System.Exception ex) {
        var sb = new System.Text.StringBuilder();
        sb.Append(context);
        if (ex != null) {
            sb.Append('\n').Append(ex.GetType().Name).Append("：").Append(ex.Message);
            // 内层异常往往是真正的根因
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 3) {
                sb.Append("\n  ← ").Append(inner.GetType().Name).Append("：").Append(inner.Message);
                inner = inner.InnerException;
                depth++;
            }
        }
        return sb.ToString();
    }

    private static void LogError(string context, System.Exception ex) {
        try {
            Lg.Info($"[LevelLib] [错误] {context}: {(ex != null ? ex.ToString() : "（无异常信息）")}");
        } catch { }
    }

    /// <summary>入队并（必要时）弹出</summary>
    private static void EnqueueError(string text) {
        if (string.IsNullOrEmpty(text)) return;
        // 去重：同一条内容只提示一次
        if (!_errorDedup.Add(text)) return;
        if (_errorQueue.Count >= ErrorQueueMax) _errorQueue.RemoveAt(0);   // 超上限丢最旧的
        _errorQueue.Add(text);
        _errorShown++;
        ShowNextError();
    }

    /// <summary>显示队首错误（已在显示则不打断）</summary>
    private static void ShowNextError() {
        if (_errorCanvas != null) return;          // 已经有一个在显示
        if (_errorQueue.Count == 0) return;
        string msg = _errorQueue[0];
        _errorQueue.RemoveAt(0);
        try {
            BuildErrorWindow(msg);
        } catch (System.Exception e) {
            // 弹窗本身失败：至少保证日志里有记录
            try { Lg.Ex("构建报错弹窗失败", e); } catch { }
        }
    }

    private static void CloseErrorWindow() {
        if (_errorCanvas != null) { Object.Destroy(_errorCanvas.gameObject); _errorCanvas = null; }
        _errorText = null;
        _errorCountText = null;
        _errorDedup.Clear();
        ShowNextError();
    }

    /// <summary>构建报错窗口（复制按钮方便把错误贴给作者）</summary>
    private static void BuildErrorWindow(string msg) {
        // 不用独立画布 + sortingOrder：那套实测会把排序值变成负数，弹窗被排到所有界面后面。
        // 改成挂在面板画布底下：层级在面板内容之后 = 一定盖在面板和设置页之上，且不依赖排序值。
        var parent = _panelCanvas != null ? _panelCanvas.transform : null;
        _errorCanvas = MakeCanvas("LevelLib_Error", 99000);
        if (parent != null) {
            _errorCanvas.transform.SetParent(parent, false);
            _errorCanvas.overrideSorting = false;   // 跟着父画布排，不再自己定排序
            // 子画布的 RectTransform 必须铺满父画布，否则会缩成一个点
            var ecrt = _errorCanvas.GetComponent<RectTransform>();
            ecrt.anchorMin = Vector2.zero;
            ecrt.anchorMax = Vector2.one;
            ecrt.offsetMin = Vector2.zero;
            ecrt.offsetMax = Vector2.zero;
            ecrt.localScale = Vector3.one;
            ecrt.anchoredPosition = Vector2.zero;
            _errorCanvas.transform.SetAsLastSibling();   // 排在面板所有内容之后（最上层）
        }
        var bg = MakeDialogVeil(_errorCanvas.transform);

        var box = MakeDialogBox(_errorCanvas.transform, "Box", new Color(0.16f, 0.10f, 0.12f, 0.98f));
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(900f, 520f);

        var title = MakeTitle(box.transform, "Title", 34, new Color(1f, 0.45f, 0.45f, 1f), TextAnchor.MiddleCenter);
        title.text = Lang.Tr("出错了喵");
        title.fontStyle = FontStyle.Bold;
        title.rectTransform.anchorMin = new Vector2(0f, 0.90f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = Vector2.zero; title.rectTransform.offsetMax = Vector2.zero;

        // 提示：可复制
        var hint = MakeText(box.transform, "Hint", 20, new Color(0.7f, 0.7f, 0.75f, 1f), TextAnchor.MiddleCenter);
        hint.text = Lang.Tr("下面的文字可以选中复制，反馈问题时贴给作者即可");
        hint.rectTransform.anchorMin = new Vector2(0f, 0.83f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0.90f);
        hint.rectTransform.offsetMin = Vector2.zero; hint.rectTransform.offsetMax = Vector2.zero;

        // 错误正文（可滚动）
        var viewport = MakeImage(box.transform, "Viewport", new Color(0.08f, 0.06f, 0.08f, 1f));
        viewport.rectTransform.anchorMin = new Vector2(0.04f, 0.20f);
        viewport.rectTransform.anchorMax = new Vector2(0.96f, 0.83f);
        viewport.rectTransform.offsetMin = Vector2.zero; viewport.rectTransform.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = viewport.rectTransform;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = crt;

        var body = MakeText(content.transform, "Body", 20, new Color(0.95f, 0.9f, 0.9f, 1f), TextAnchor.UpperLeft);
        body.text = msg;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;
        body.lineSpacing = 1.2f;
        var brt = body.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -8f);
        brt.sizeDelta = new Vector2(-24f, 0f);
        _errorText = body;

        // 队列提示（还有几条没看）
        var cnt = MakeText(box.transform, "Count", 17, new Color(1f, 0.8f, 0.45f, 1f), TextAnchor.MiddleLeft);
        cnt.rectTransform.anchorMin = new Vector2(0.04f, 0.10f);
        cnt.rectTransform.anchorMax = new Vector2(0.55f, 0.19f);
        cnt.rectTransform.offsetMin = Vector2.zero; cnt.rectTransform.offsetMax = Vector2.zero;
        _errorCountText = cnt;
        RefreshErrorCount();

        // 按钮：复制 / 确定
        var copyBtn = MakeButton(box.transform, "Copy", Vector2.zero, new Vector2(160f, 46f), new Color(0.25f, 0.35f, 0.55f, 1f));
        var cpRt = copyBtn.GetComponent<RectTransform>();
        cpRt.anchorMin = cpRt.anchorMax = new Vector2(0.5f, 0.09f);
        cpRt.pivot = new Vector2(0.5f, 0.5f);
        cpRt.anchoredPosition = new Vector2(-95f, 0f);
        var cpT = MakeText(copyBtn.transform, "T", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(cpT.rectTransform, Vector2.zero, Vector2.one);
        cpT.text = Lang.Tr("复制");
        copyBtn.onClick.AddListener(() => {
            try {
                GUIUtility.systemCopyBuffer = _errorText != null ? _errorText.text : msg;
                ShowToast(Lang.Tr("错误信息已复制"));
            } catch (System.Exception e) { ReportErrorQuiet("复制错误信息失败", e); }
        });

        var okBtn = MakeButton(box.transform, "Ok", Vector2.zero, new Vector2(160f, 46f), new Color(0.55f, 0.3f, 0.3f, 1f));
        var okRt = okBtn.GetComponent<RectTransform>();
        okRt.anchorMin = okRt.anchorMax = new Vector2(0.5f, 0.09f);
        okRt.pivot = new Vector2(0.5f, 0.5f);
        okRt.anchoredPosition = new Vector2(95f, 0f);
        var okT = MakeText(okBtn.transform, "T", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
        okT.text = Lang.Tr("确定");
        okBtn.onClick.AddListener(() => CloseErrorWindow());

        // Esc 也能关（复用全局 Esc 处理：见 TickAlwaysHotkeys）
        // 把弹窗实际渲染到的屏幕矩形记进日志：定位"建出来了但看不见"这类问题
        try {
            var rt = _errorCanvas.GetComponent<RectTransform>();
            var cs = new Vector3[4];
            rt.GetWorldCorners(cs);
            Lg.Info($"[LevelLib] [报错弹窗] {(msg.Length > 120 ? msg.Substring(0, 120) + "…" : msg)}"
                + $" | 排序={_errorCanvas.sortingOrder} 覆盖排序={_errorCanvas.overrideSorting}"
                + $" 屏幕矩形=({cs[0].x:F0},{cs[0].y:F0})-({cs[2].x:F0},{cs[2].y:F0})"
                + $" 子节点={_errorCanvas.transform.childCount} 缩放={_errorCanvas.scaleFactor:F2}");
        } catch (System.Exception e) {
            Lg.Ex("报错弹窗自检失败", e);
        }
    }

    private static void RefreshErrorCount() {
        if (_errorCountText == null) return;
        if (_errorQueue.Count > 0) {
            _errorCountText.text = Lang.TrFmt("还有 {0} 条错误（点确定继续查看）", _errorQueue.Count);
        } else {
            _errorCountText.text = _errorShown > 1 ? Lang.TrFmt("本次运行共 {0} 条错误", _errorShown) : "";
        }
    }

    /// <summary>Esc 关闭报错弹窗（供全局 Esc 处理调用），返回是否消费了这次 Esc</summary>
    private static bool TryCloseErrorByEsc() {
        if (_errorCanvas == null) return false;
        CloseErrorWindow();
        return true;
    }

    // ---------- 自测入口 ----------
    // mod 目录放 debug_error.txt，游戏里按 F9 就弹一次测试错误弹窗；正式游玩没有这个文件就没有任何影响。

    private static int _debugErrorFlag = -1;   // -1=还没查过, 0=未启用, 1=已启用

    private static void TickDebugErrorKey() {
        if (_debugErrorFlag == 0) return;
        if (_debugErrorFlag < 0) {
            try {
                string flag = Path.Combine(Main.ModEntry.Path, "debug_error.txt");
                _debugErrorFlag = File.Exists(flag) ? 1 : 0;
            } catch { _debugErrorFlag = 0; }
            if (_debugErrorFlag == 0) return;
        }
        if (!UiInput.KeyDown(KeyCode.F9)) return;
        try {
            // 故意制造一个带内层异常的失败，验证弹窗能把根因也显示出来
            throw new System.InvalidOperationException("这是一条测试错误（按 F9 触发）",
                new System.IO.IOException("内层异常示例：文件被占用"));
        } catch (System.Exception ex) {
            ReportError("报错弹窗自测：如果你看到这个窗口，说明报错接口工作正常", ex);
        }
    }
}
