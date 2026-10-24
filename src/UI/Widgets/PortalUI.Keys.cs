// [split-of] PortalUI.cs lines 3394-3615
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

/// <summary>PortalUI 的快捷键绑定、按键捕获、底部键位提示（拆分自 PortalUI.cs，同一个 partial 类）</summary>
public static partial class PortalUI {
    private static string _buildStep = "未开始";

    // 鸣谢区和开发者区已并入顶层「开发者」tab（见 BuildDeveloperPage），这里不再有旧版悬浮提示与开发者卡牌。

    // ---------- 底部键位提示：一排「说明 + 键帽」的小件 ----------
    // 原来是一整行字符串：键名和说明挤在同一个字号里，说明和键的分界只有"文字颜色一样、中间没东西"；
    // 改键之后整行宽度跳一下，也看不出是哪一段变了。现在每条提示是一个小件、键帽单独一块底，
    // 键名变了由键帽自己变宽，右边的小件滑过去让位（走流式引擎那一套，见 PortalUI.Flow）。
    private sealed class KeyHint {
        public RectTransform Rt;            // 这一条（说明 + 键帽）
        public Text Desc;                   // 说明
        public Image CapBg;                 // 键帽底
        public Text Cap;                    // 键帽上的字
        public System.Func<string> Key;     // 当前绑的键（改键后要重新问）
    }

    private static readonly List<KeyHint> _keyHints = new List<KeyHint>();
    private static string _keyHintShown;    // 上一次摆的那组键名（只有真变了才滑，建的时候本来就摆得好好的）
    private const float KeyHintH = 30f;         // 这一排的高度
    private const float KeyHintGap = 24f;       // 两条提示之间
    private const float KeyCapGap = 8f;         // 说明与键帽之间
    private const float KeyCapPadX = 12f;       // 键帽左右内边距（栅格：间距一律 4 的倍数）
    private const float KeyCapMinW = 34f;       // 键帽最小宽度（"↑"这种单符号也要有个键的样子）
    /// <summary>键位提示里"说明"那一段的颜色（比正文暗一档：它是常驻提示，不该抢内容）</summary>
    private static readonly Color KeyHintTextColor = new Color(0.78f, 0.78f, 0.84f, 1f);

    /// <summary>主界面底部快捷键提示：打开谱面 / 打开随机谱面 / 上下切换</summary>
    private static void BuildKeyHintBar() {
        if (_panelCanvas == null) return;
        var row = new GameObject("KeyHintBar", typeof(RectTransform));
        row.transform.SetParent(_panelCanvas.transform, false);
        var rrt = row.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.5f, 0f);
        rrt.anchorMax = new Vector2(0.5f, 0f);
        rrt.pivot = new Vector2(0.5f, 0f);
        rrt.anchoredPosition = new Vector2(0f, 10f);   // 贴着库页底部按钮行的下沿，别再往上抬
        rrt.sizeDelta = new Vector2(0f, KeyHintH);
        _keyHintBar = rrt;
        _keyHints.Clear();
        _keyHintShown = null;
        AddKeyHint(rrt, Lang.Tr("打开谱面："), () => KeyDisplay(OpenLevelKey));
        AddKeyHint(rrt, Lang.Tr("打开随机谱面："), () => KeyDisplay(RandomLevelKey));
        AddKeyHint(rrt, Lang.Tr("向上切换："), () => "↑");
        AddKeyHint(rrt, Lang.Tr("向下切换："), () => "↓");
        RefreshKeyHintBar();
    }

    /// <summary>加一条「说明 + 键帽」。这里只建件，位置由 LayoutKeyHintRow 一次算好</summary>
    private static void AddKeyHint(RectTransform row, string label, System.Func<string> key) {
        var go = new GameObject("Hint", typeof(RectTransform));
        go.transform.SetParent(row, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        var bg = MakeImage(go.transform, "CapBg", ItemColor);       // 键帽底先建（画在字下面）
        bg.sprite = SmallRoundSprite;                               // 小件那张半径 8 的圆角块
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false;
        var brt = bg.rectTransform;
        brt.anchorMin = new Vector2(0f, 0.5f);
        brt.anchorMax = new Vector2(0f, 0.5f);
        brt.pivot = new Vector2(0f, 0.5f);
        var desc = MakeText(go.transform, "Desc", FsLabel, KeyHintTextColor, TextAnchor.MiddleLeft);
        desc.text = Lang.Tr(label);
        var drt = desc.rectTransform;
        drt.anchorMin = new Vector2(0f, 0f);
        drt.anchorMax = new Vector2(0f, 1f);
        drt.pivot = new Vector2(0f, 0.5f);
        var cap = MakeText(go.transform, "Cap", FsLabel, Color.white, TextAnchor.MiddleCenter);
        var crt = cap.rectTransform;
        crt.anchorMin = new Vector2(0f, 0.5f);
        crt.anchorMax = new Vector2(0f, 0.5f);
        crt.pivot = new Vector2(0f, 0.5f);
        _keyHints.Add(new KeyHint { Rt = rt, Desc = desc, CapBg = bg, Cap = cap, Key = key });
    }

    /// <summary>这一排当前绑的是哪几个键（拼起来当指纹：只有它变了才需要滑）</summary>
    private static string KeyHintKeys() {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _keyHints.Count; i++) {
            var k = _keyHints[i];
            if (k == null) continue;
            if (i > 0) sb.Append('|');
            sb.Append(k.Key != null ? k.Key() : "");
        }
        return sb.ToString();
    }

    /// <summary>按"说明 + 键帽"的实际宽度把这一排从左到右摆开、整排居中；键名变了就滑过去让位</summary>
    private static void LayoutKeyHintRow(bool animate) {
        if (_keyHintBar == null || _keyHints.Count == 0) return;
        float total = 0f;
        for (int i = 0; i < _keyHints.Count; i++) {
            var k = _keyHints[i];
            if (k.Rt == null) continue;
            k.Cap.text = k.Key != null ? k.Key() : "";
            float descW = k.Desc.preferredWidth;
            float capW = Mathf.Max(KeyCapMinW, k.Cap.preferredWidth + KeyCapPadX * 2f);
            k.Desc.rectTransform.sizeDelta = new Vector2(descW, 0f);
            k.Desc.rectTransform.anchoredPosition = Vector2.zero;
            k.CapBg.rectTransform.sizeDelta = new Vector2(capW, KeyHintH - 6f);
            k.CapBg.rectTransform.anchoredPosition = new Vector2(descW + KeyCapGap, 0f);
            k.Cap.rectTransform.sizeDelta = new Vector2(capW, KeyHintH - 6f);
            k.Cap.rectTransform.anchoredPosition = new Vector2(descW + KeyCapGap, 0f);
            k.Rt.sizeDelta = new Vector2(descW + KeyCapGap + capW, KeyHintH);
            total += k.Rt.sizeDelta.x + (i > 0 ? KeyHintGap : 0f);
        }
        if (animate) FlowWindow("键位提示", MoSmall);
        float x = -total * 0.5f;
        for (int i = 0; i < _keyHints.Count; i++) {
            var k = _keyHints[i];
            if (k.Rt == null) continue;
            if (animate) FlowTo(k.Rt, x, 0f);
            else k.Rt.anchoredPosition = new Vector2(x, 0f);
            x += k.Rt.sizeDelta.x + KeyHintGap;
        }
    }

    /// <summary>更新底部快捷键提示（设置里改键后刷新）。
    /// 这条提示讲的是谱面列表的快捷键，所以只在库页显示：它挂在画布上、不随页面带滑动，
    /// 不显式隐藏的话在成就页/设置页/开发者页也会露出来，跟页面内容叠在一起。</summary>
    private static void RefreshKeyHintBar() {
        if (_keyHintBar == null) return;
        string now = KeyHintKeys();
        bool changed = _keyHintShown != null && _keyHintShown != now;   // 第一次摆不滑（那一刻本来就摆得对）
        _keyHintShown = now;
        LayoutKeyHintRow(changed);
        _keyHintBar.gameObject.SetActive(_topTab == 1);   // 底部快捷键提示只属于库页（现在是 tab 1）
    }

    // ---------- 点击探针 ----------
    /// <summary>"官方成就点不到"用的探针：日志已证明四个子 tab 按钮的位置/尺寸/锚点/可交互全对，
    /// 而那一击连兜底带都没进 —— 说明有东西把这一击吃掉了，且不是按钮的同级元素（同级都提到最上层了）。
    /// 面板开着时鼠标左键一按就打印 RaycastAll 的前四个命中项（名字 + 深度），一次点击点名凶手。</summary>
    private static void TickClickProbe() {
        try {
            if (!_panelOpen) return;
            // 必须用 UiInput.*：我们自己的 InputMapGate（InputMap.cs）在 UI map 生效时，
            // 会把"没走 UiInput 的读取"一律回答零（坐标→零向量）。
            if (!UiInput.MouseButtonDown(0)) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { Lg.Info("【点击探针】EventSystem 为空"); return; }
            var ped = new UnityEngine.EventSystems.PointerEventData(es);
            ped.position = UiInput.MousePosition;
            var res = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(ped, res);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < res.Count && i < 4; i++) {
                var g = res[i].gameObject;
                if (i > 0) sb.Append("  <-  ");
                sb.Append(g != null ? g.name : "?");
                sb.Append("(深度=").Append(res[i].depth).Append(")");
            }
            Lg.Info($"[LevelLib] 【点击探针】鼠标 {UiInput.MousePosition.x:F0},{UiInput.MousePosition.y:F0} 命中 {res.Count} 个 → {sb}");
        } catch (System.Exception e) { Lg.Ex("点击探针异常", e); }
    }

    /// <summary>快捷键捕获：游戏内每帧捕获。优先 inputString（可打印字符最灵敏），其次 GetKeyDown 枚举（功能键）。
    /// anyKeyDown 日志用于确认游戏是否聚焦——若恒 false 说明游戏窗口没键盘焦点。</summary>
    private static void TickKeyCapture() {
        if (string.IsNullOrEmpty(CaptureTarget)) return;
        // 通道1：inputString（可打印字符，最灵敏，失焦时也可能空）
        string s = UiInput.InputString;
        if (!string.IsNullOrEmpty(s)) {
            KeyCode mapped = CharToKeyCode(s[0]);
            if (mapped != KeyCode.None) {
                Lg.Info("inputString捕获 char='" + s[0] + "' -> " + mapped + " 原target=" + CaptureTarget);
                if (ApplyCapturedKey(mapped)) return;   // 成功才结束，冲突则继续等下一个键
            }
        }
        // 通道2：GetKeyDown 枚举（功能键/无字符键）
        foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode))) {
            int v = (int)k;
            if (v == 0 || v >= 323) continue;   // 跳过 None 和鼠标/摇杆键
            if (UiInput.KeyDown(k)) {
                Lg.Info("GetKeyDown捕获 k=" + k + " 原target=" + CaptureTarget);
                if (ApplyCapturedKey(k)) return;   // 成功才结束，冲突则继续等下一个键
            }
        }
    }

    /// <summary>初始化热键冲突黑名单（默认占满 ADOFAI 常用键，可被 conflict_keys.txt 覆盖扩充）</summary>
    public static void InitConflictKeys() {
        if (ConflictedKeys.Count > 0) return;   // 已加载过
        string[] defs = new string[] {
            // 方向/暂停/取消
            "UpArrow","DownArrow","LeftArrow","RightArrow","Escape","Q",
            // 确认
            "Space","Return","KeypadEnter","Backspace","Delete","Tab",
            // 游玩操作（Action1 左半键）
            "W","E","R","T","A","S","D","F","G","Z","X","C","V","B","CapsLock",
            // 游玩操作（Action2 右半键）
            "Y","U","I","O","P","H","J","K","L","N","M","Comma","Period","Slash","Semicolon","Quote","LeftBracket","RightBracket","LeftShift","RightShift",
            // 数字/符号
            "Alpha0","Alpha1","Alpha2","Alpha3","Alpha4","Alpha5","Alpha6","Alpha7","Alpha8","Alpha9","BackQuote","AltGr",
            // 特殊/功能
            "Tab","End","Print","SysReq","LeftAlt","RightAlt","LeftWindows","RightWindows","LeftMeta","RightMeta","LeftControl","RightControl",
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"
        };
        foreach (string s in defs) {
            if (System.Enum.TryParse(s, out KeyCode k)) ConflictedKeys.Add(k);
        }
    }

    /// <summary>读黑名单（conflict_keys.txt；文件存在则整体替换默认表，不再并集）</summary>
    public static void LoadConflictKeys() {
        InitConflictKeys();   // 先填默认
        string path = Path.Combine(Main.ModEntry.Path, "conflict_keys.txt");
        if (!File.Exists(path)) return;
        try {
            string val = File.ReadAllText(path).Trim();
            ConflictedKeys.Clear();
            foreach (string s in val.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)) {
                if (System.Enum.TryParse(s.Trim(), out KeyCode k)) ConflictedKeys.Add(k);
            }
        } catch { }
    }

    /// <summary>应用捕获到的键：直接绑定，不再做冲突拒绝（v4.0.107 起 UI map 已让游戏读不到键，任意键都可用）</summary>
    public static bool ApplyCapturedKey(KeyCode k) {
        if (CaptureTarget == "open") { OpenLevelKey = k; }
        else if (CaptureTarget == "random") { RandomLevelKey = k; }
        CaptureTarget = null;
        AutoFeatures.Save();
        RaiseCaptureDone(k);
        return true;
    }

    /// <summary>字符转 KeyCode（inputString 用）</summary>
    private static KeyCode CharToKeyCode(char c) {
        if (c >= 'a' && c <= 'z') return (KeyCode)(KeyCode.A + (c - 'a'));
        if (c >= 'A' && c <= 'Z') return (KeyCode)(KeyCode.A + (c - 'A'));
        if (c >= '0' && c <= '9') return (KeyCode)(KeyCode.Alpha0 + (c - '0'));
        if (c == ' ') return KeyCode.Space;
        if (c == '\n' || c == '\r') return KeyCode.Return;
        if (c == '\t') return KeyCode.Tab;
        if (c == '\b') return KeyCode.Backspace;
        if (c == '\\') return KeyCode.Backslash;
        if (c == '.') return KeyCode.Period;
        if (c == ',') return KeyCode.Comma;
        if (c == '-') return KeyCode.Minus;
        if (c == '=' ) return KeyCode.Equals;
        if (c == '[') return KeyCode.LeftBracket;
        if (c == ']') return KeyCode.RightBracket;
        if (c == ';') return KeyCode.Semicolon;
        if (c == '\'') return KeyCode.Quote;
        if (c == '/') return KeyCode.Slash;
        if (c == '`') return KeyCode.BackQuote;
        return KeyCode.None;
    }

    private static void TickLevelKeys() {
        if (!_panelOpen || IsLevelPlaying()) return;
        if (!string.IsNullOrEmpty(CaptureTarget)) return;   // 捕获中不触发
        // 诊断：绑定值变化时打一次当前快捷键
        if (_lastOpenKey != OpenLevelKey || _lastRandomKey != RandomLevelKey) {
            _lastOpenKey = OpenLevelKey; _lastRandomKey = RandomLevelKey;
            Lg.Info("快捷键当前绑定 OpenLevelKey=" + OpenLevelKey + " RandomLevelKey=" + RandomLevelKey);
        }
        // 打开选中谱面
        if (OpenLevelKey != KeyCode.None && IsHotkeyPressed(OpenLevelKey)) {
            Lg.Info("检测到打开键按下 OpenLevelKey=" + OpenLevelKey);
            var entry = LevelScanner.Entries.FirstOrDefault(x => x.FolderPath == _selectedPath);
            if (entry != null && !string.IsNullOrEmpty(entry.FilePath)) {
                Lg.Info("快捷键打开谱面: " + entry.FilePath);
                // 快捷键进关也必须走进入动画：这条路原来直接调 ConfirmOpen，把过渡整个绕过去了，
                // 而"进入谱面的动画"是所有进关入口都要有的（见 EnterTransition 的文件头）。
                StartEnterTransition(entry.FilePath);
            }
        }
        // 随机谱面：不直接进关，只"选中 + 滑过去"，真正的进入交给 TickRandomOpen() 等动画走完再触发。
        if (RandomLevelKey != KeyCode.None && IsHotkeyPressed(RandomLevelKey)) {
            Lg.Info("检测到随机键按下 RandomLevelKey=" + RandomLevelKey);
            var pool = _display.Count > 0 ? _display : LevelScanner.Entries;
            if (pool.Count > 0) {
                // 按当日随机过的次数加权（抽过的权重降低，从不排除），只有这一处算"随机抽中"——
                // 手动点开/快捷键打开不计数。
                var entry = RandomPick.Pick(pool);
                bool hadSel = !string.IsNullOrEmpty(_selectedPath);
                _selectedPath = entry.FolderPath;
                RefreshList();
                ScrollToEntry(entry, true);          // 快速滑动到视口中间（已有那套指数定位）
                RefreshDetailInfo(entry);            // 详情先填上
                RefreshDetailFileList(entry);
                if (_detailTab == 0) RefreshPickRecordsDetail(entry);
                _pendingRandomOpen = entry.FilePath;
                _pendingRandomOpenT = 0f;
                _pendingRandomReadyT = -1f;   // -1 = 还没到"就绪"，到的那一帧才开始算等待
                _pendingRandomOpenMinFrames = hadSel ? 4 : 10;   // 原本没选中 → 多给几帧让"选中+详情"落定
                Lg.Info($"[LevelLib] 随机谱面：滑过去并选中 {entry.Title}"
                    + $"（原本有没有选中={hadSel}，等动画+详情出现后再进）");
            }
        }
        // 上下键切换选中
        if (UiInput.KeyDown(KeyCode.UpArrow)) MoveSelection(-1);
        if (UiInput.KeyDown(KeyCode.DownArrow)) MoveSelection(1);
    }

    // ---------- 随机谱面的"延迟进入" ----------
}
