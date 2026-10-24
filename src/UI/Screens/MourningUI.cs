using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>九一八公祭日彩蛋：9 月 18 日当天整个 MOD 界面变黑白（只留黑/白/灰），
/// 当天第一次打开 MOD 主界面时弹一次提示（血红"默哀"标题 + 正文 + CountdownSec 秒进度条，
/// 走完"我已知晓"才可点）。
/// 灰度做法：扫 MOD 所有画布（LevelLib_*）下的 Graphic 把颜色压成灰度并记住原色以便还原，
/// 游戏自己的 UI 一律不碰。</summary>
public static class MourningUI {
    private const string AckFileName = "mourning_ack.txt";   // 记"哪一年已经知晓过了"
    private const float CountdownSec = 8f;                   // 倒计时秒数（与正文里"默哀8秒"那句话对应）

    /// <summary>调试开关：mod 目录里出现这个文件，整个彩蛋（灰度滤镜 + 默哀弹窗）都不生效，
    /// 9/18 当天开发与截图才能看到彩色。
    /// 这个文件绝不能进发布包——混进去所有玩家的彩蛋都会被关掉（打包脚本已排除它）。</summary>
    private const string OffFileName = "debug_nomourning.txt";
    private static int _offChecked = -1;    // -1 还没查过 / 0 没有这个文件 / 1 有（关掉彩蛋）

    /// <summary>彩蛋是否被调试文件关掉了（结果缓存，只查一次盘）</summary>
    private static bool IsDisabledByFile() {
        if (_offChecked < 0) {
            try {
                _offChecked = File.Exists(Path.Combine(Main.ModEntry.Path, OffFileName)) ? 1 : 0;
                if (_offChecked == 1)
                    Lg.Info("九一八彩蛋已被 " + OffFileName + " 关闭（灰度滤镜与默哀弹窗都不生效）");
            } catch { _offChecked = 0; }
        }
        return _offChecked == 1;
    }

    // 文案
    private const string TitleText = "默哀";
    private const string BodyText =
        "今天是{0}九月十八日，是九一八事变【{0}-1931】周年公祭日，\n" +
        "曾经是1931年9月18日日本驻中国东北地区的关东军突然袭击奉天，以武力侵占东北，" +
        "拉开了第二次世界大战东方战场的序幕，\n" +
        "无数中国烈士前仆后继，悍不畏死，守下了我们的国家，\n" +
        "在此，请对他们默哀8秒，致以最诚挚的敬意。";   // 文案里的"8秒"与 CountdownSec 对应

    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.82f);
    private static readonly Color CardColor = new Color(0.08f, 0.07f, 0.09f, 0.98f);
    private static readonly Color BloodRed = new Color(0.72f, 0.05f, 0.05f, 1f);      // 血红标题
    private static readonly Color BodyColor = new Color(0.90f, 0.90f, 0.90f, 1f);
    private static readonly Color BarBgColor = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color BarFillColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private static bool _checkedToday;      // 本会话是否已判断过日期
    private static bool _isMourningDay;     // 今天是不是 9/18
    private static string _ackYear;         // 已知晓的年份（文件里读出来的）
    private static bool _dialogShown;       // 本会话已弹过（防重复）
    private static bool _dialogWanted;      // 主界面打开时请求弹窗
    private static bool _tickHooked;        // 是否已记过"已启用"那行日志（驱动来自 Main.OnUpdate）

    // 弹窗运行时状态
    private static GameObject _canvasGo;
    private static float _elapsed;
    private static Image _barFill;
    private static Button _okBtn;
    private static Text _okText;
    private static bool _countdownDone;

    /// <summary>今天是不是 9 月 18 日（结果缓存，第一次调用时才读盘）。
    /// 调试文件优先：debug_nomourning.txt 存在时直接返回 false，灰度与弹窗都不生效。</summary>
    public static bool IsMourningDay() {
        if (!_checkedToday) {
            _checkedToday = true;
            try {
                if (IsDisabledByFile()) { _isMourningDay = false; return _isMourningDay; }
                var now = DateTime.Now;
                _isMourningDay = (now.Month == 9 && now.Day == 18);
                _ackYear = LoadAckYear();
            } catch (Exception e) {
                Lg.Info("公祭日日期判定失败: " + e);
                _isMourningDay = false;
            }
        }
        return _isMourningDay;
    }

    /// <summary>打开 MOD 主界面时调用：当天且本年度还没"知晓"过 → 请求弹提示（一年只弹一次）</summary>
    public static void OnPanelOpened() {
        if (!IsMourningDay()) return;
        string year = DateTime.Now.Year.ToString();
        if (_ackYear == year) return;   // 今年已知晓过
        _dialogWanted = true;
        LogEnabledOnce();
    }

    /// <summary>每帧：灰度滤镜 + 弹窗驱动（由 Main.OnUpdate 调，本文件不自己挂 OnUpdate）</summary>
    public static void Tick() {
        try {
            if (!IsMourningDay()) return;
            ApplyGrayscale(true);
            if (_dialogWanted && !_dialogShown) {
                _dialogShown = true;
                _dialogWanted = false;
                ShowDialog();
            }
            TickDialog();
        } catch (Exception e) {
            Lg.Info("公祭日界面异常: " + e);
        }
    }

    /// <summary>首次启用时记一行日志（顺带置上"只记一次"的标志）。
    /// 这里绝对不能再往 Main.ModEntry.OnUpdate 上挂 Tick：驱动来自 Main.OnUpdate → `Tick()`，
    /// 挂重了每帧跑两遍，倒计时会双倍速。</summary>
    private static void LogEnabledOnce() {
        if (_tickHooked) return;
        _tickHooked = true;
        try {
            if (Main.ModEntry != null) {
                Lg.Info("九一八公祭日：界面灰度与默哀提示已启用（今天 " + DateTime.Now.ToString("yyyy-MM-dd") + "）");
            }
        } catch (Exception e) {
            Lg.Info("公祭日启用失败: " + e);
        }
    }

    // ---------- 灰度滤镜 ----------

    private sealed class GrayEntry {
        public Graphic G;
        public Color Orig;
        public Color AppliedTo;   // 上一帧我们写进去的值（判断外部有没有改色）
    }
    private static readonly Dictionary<Graphic, GrayEntry> _gray = new();
    private static int _grayTickCounter;

    /// <summary>每 2 帧扫一次盘（新出现的画布/文字才会被抓进来），已登记的老老实实每帧压灰度</summary>
    private static void ApplyGrayscale(bool on) {
        if (_grayTickCounter++ % 2 == 0) ScanCanvases();

        var dead = new List<Graphic>();
        foreach (var kv in _gray) {
            var e = kv.Value;
            if (e.G == null) { dead.Add(e.G); continue; }
            var cur = e.G.color;
            // 外部改过色（与我们上一帧写进去的不同）→ 认它为新原色，还原时才不会回退成旧色
            if (!Approx(cur, e.AppliedTo)) e.Orig = cur;
            var want = on ? GrayOf(e.Orig) : e.Orig;
            if (!Approx(cur, want)) e.G.color = want;
            e.AppliedTo = want;
        }
        for (int i = 0; i < dead.Count; i++) _gray.Remove(dead[i]);
    }

    /// <summary>把新出现的 Graphic 登记进来，供灰度逐帧改写。
    /// 必须走 `PortalUI.CollectOurGraphics` 问登记表，不能全场景搜：这里每 2 帧跑一次
    /// （见 `ApplyGrayscale`），三万对象的场景里每两帧扫全场还会分配大数组。
    /// `IsModGraphic` 的根节点前缀判定留着当保险丝：表外的画布万一混进来也不动。</summary>
    private static readonly List<Graphic> _scanBuf = new List<Graphic>();

    private static void ScanCanvases() {
        try {
            _scanBuf.Clear();
            PortalUI.CollectOurGraphics(_scanBuf);
            for (int i = 0; i < _scanBuf.Count; i++) {
                var g = _scanBuf[i];
                if (g == null) continue;
                if (!IsModGraphic(g.transform)) continue;
                if (_gray.ContainsKey(g)) continue;
                _gray[g] = new GrayEntry { G = g, Orig = g.color, AppliedTo = g.color };
            }
        } catch (Exception e) {
            Lg.Info("灰度扫描失败: " + e);
        }
    }

    /// <summary>往上找根节点，看是不是咱自己的画布（游戏自己的 UI 一律不动）</summary>
    private static bool IsModGraphic(Transform t) {
        while (t != null) {
            string n = t.name;
            if (!string.IsNullOrEmpty(n) && n.StartsWith("LevelLib_", StringComparison.OrdinalIgnoreCase)) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>保留原透明度，只把 RGB 压成同亮度的灰</summary>
    private static Color GrayOf(Color c) {
        float y = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        return new Color(y, y, y, c.a);
    }

    private static bool Approx(Color a, Color b) {
        return Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f
            && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;
    }

    // ---------- 默哀提示弹窗 ----------

    private static void ShowDialog() {
        if (_canvasGo != null) return;
        try {
            EnsureEventSystem();
            // 必须用 PortalUI.MakeCanvas（它内部登记画布表）：自己建画布的话灰度扫不到它自己、
            // 字形预热与切语言重刷也漏它
            var canvas = PortalUI.MakeCanvas("LevelLib_Mourning", 96000);   // 高于生日(95000)与成就提示
            _canvasGo = canvas.gameObject;

            var dim = MakeImage(canvas.transform, "Dim", DimColor);
            Stretch(dim.rectTransform, Vector2.zero, Vector2.zero);

            var card = MakeImage(canvas.transform, "Card", CardColor);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(1180f, 820f);

            // 血红色主标题，投影跟其它主标题同一份（这个类独立，调 PortalUI 那支公开的）
            var title = MakeText(card.transform, "Title", 96, BloodRed, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            PortalUI.AddTitleShadow(title);
            Stretch(title.rectTransform, new Vector2(60f, 660f), new Vector2(-60f, -30f));
            title.text = TitleText;

            // 正文（{0} 代入当前年份）
            int year = DateTime.Now.Year;
            var body = MakeText(card.transform, "Body", 26, BodyColor, TextAnchor.UpperCenter);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.lineSpacing = 1.35f;
            // 卡片 820 高、轴心居中（底边 -410、顶边 +410）——自下而上：进度条 y=150、按钮 y=-330、
            // 正文 70~-290、标题占顶部 140，互不重叠（改任何一块的 y 都要重新核一遍）
            Stretch(body.rectTransform, new Vector2(70f, 70f), new Vector2(-70f, -290f));
            body.text = string.Format(BodyText, year);

            // 倒计时：进度条 + 文字（都在按钮上方）
            var barBg = MakeImage(card.transform, "BarBg", BarBgColor);
            var bbrt = barBg.rectTransform;
            bbrt.anchorMin = new Vector2(0.5f, 0f);
            bbrt.anchorMax = new Vector2(0.5f, 0f);
            bbrt.pivot = new Vector2(0.5f, 0f);
            bbrt.anchoredPosition = new Vector2(0f, 150f);
            bbrt.sizeDelta = new Vector2(880f, 26f);
            _barFill = MakeImage(barBg.transform, "Fill", BarFillColor);
            var frt = _barFill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            frt.sizeDelta = Vector2.zero;

            // 剩余秒数只显示在按钮文案上，进度条下方不再有文字

            // 我已知晓（在进度条下方；倒计时走完前不可点）
            _okBtn = MakeButton(card.transform, "OkBtn", new Vector2(0f, -330f), new Vector2(300f, 64f), BloodRed);
            _okText = MakeText(_okBtn.transform, "T", 30, Color.white, TextAnchor.MiddleCenter);
            Stretch(_okText.rectTransform, Vector2.zero, Vector2.zero);
            _okText.text = Lang.TrFmt("我已知晓（{0}）", (int)CountdownSec);
            SetOkEnabled(false);
            _okBtn.onClick.AddListener(OnAcknowledged);

            _elapsed = 0f;
            _countdownDone = false;
            Lg.Info($"[LevelLib] 九一八公祭日提示已弹出（{year} 年，默哀 {CountdownSec} 秒）");
        } catch (Exception e) {
            Lg.Info("公祭日弹窗创建失败: " + e);
            _canvasGo = null;
        }
    }

    /// <summary>倒计时每帧驱动</summary>
    private static void TickDialog() {
        if (_canvasGo == null || _countdownDone) return;
        _elapsed += Time.unscaledDeltaTime;   // 用 unscaled：面板把 timeScale 冻成 0 了
        float k = Mathf.Clamp01(_elapsed / CountdownSec);
        if (_barFill != null) {
            var a = _barFill.rectTransform.anchorMax;
            a.x = k;
            _barFill.rectTransform.anchorMax = a;
        }
        int left = Mathf.CeilToInt(Mathf.Max(0f, CountdownSec - _elapsed));   // 剩余秒数只用在按钮文案上
        if (_okText != null) _okText.text = k >= 1f ? Lang.Tr("我已知晓") : (Lang.Tr("我已知晓（") + left + "）");
        if (k >= 1f) {
            _countdownDone = true;
            SetOkEnabled(true);
        }
    }

    /// <summary>按钮可用/不可用（不可用时压暗并挡住点击）</summary>
    private static void SetOkEnabled(bool on) {
        if (_okBtn == null) return;
        _okBtn.interactable = on;
        if (_okText != null) _okText.color = on ? Color.white : new Color(0.75f, 0.72f, 0.72f, 0.6f);
        var img = _okBtn.GetComponent<Image>();
        if (img != null) {
            var c = img.color;
            img.color = on ? new Color(c.r, c.g, c.b, 1f) : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.85f);
        }
    }

    /// <summary>点"我已知晓"：记下年份、关掉弹窗（灰度继续留到当天结束）</summary>
    private static void OnAcknowledged() {
        if (!_countdownDone) return;
        string year = DateTime.Now.Year.ToString();
        _ackYear = year;
        SaveAckYear(year);
        CloseDialog();
    }

    /// <summary>悼念弹窗是否还开着（Esc 链靠它决定这一层要不要收）。</summary>
    public static bool IsOpen { get { return _canvasGo != null; } }

    public static void CloseDialog() {
        if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
        _canvasGo = null;
        _barFill = null;
        _okBtn = null;
        _okText = null;
    }

    // ---------- 存档（哪一年已知晓） ----------

    private static string AckPath {
        get { return Path.Combine(Main.ModEntry.Path, AckFileName); }
    }

    private static string LoadAckYear() {
        try {
            if (!File.Exists(AckPath)) return null;
            foreach (var line in File.ReadAllLines(AckPath)) {
                var s = line.Trim();
                if (s.Length == 4) return s;
            }
        } catch (Exception e) {
            Lg.Info("读取公祭日记录失败: " + e);
        }
        return null;
    }

    private static void SaveAckYear(string year) {
        try {
            File.WriteAllText(AckPath, year + "\n");
            Lg.Info("九一八公祭日：" + year + " 年已确认知晓");
        } catch (Exception e) {
            Lg.Info("保存公祭日记录失败: " + e);
        }
    }

    // ---------- 建界面用的小工具（这个类不是 partial PortalUI，自带一套） ----------

    private static Font _uiFont;
    private static Font UiFont {
        get {
            // 统一走 PortalUI 的字体解析（游戏原生 UI 字体，找不到时退回内置 Arial）
            if (_uiFont == null) _uiFont = PortalUI.UiFontForOthers();
            return _uiFont;
        }
    }

    private static void EnsureEventSystem() {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("LevelLib_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // 本文件不再自带 MakeCanvas 副本：一律调 PortalUI.MakeCanvas，它内部会登记画布表

    private static Image MakeImage(Transform parent, string name, Color color) {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static Button MakeButton(Transform parent, string name, Vector2 pos, Vector2 size, Color color) {
        var img = MakeImage(parent, name, color);
        img.rectTransform.anchoredPosition = pos;
        img.rectTransform.sizeDelta = size;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    private static Text MakeText(Transform parent, string name, int size, Color color, TextAnchor align) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = UiFont;
        text.fontSize = PortalUI.UiFontPx(size);
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        PortalUI.RegisterUiFontText(text, size);   // 记下设计尺字号：玩家改「界面字体大小」时按它重刷
        return text;
    }

    private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax) {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}
