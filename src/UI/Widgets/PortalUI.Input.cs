// [split-of] PortalUI.cs lines 4058-4264
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

/// <summary>PortalUI 的输入闸门（面板开着时挡住游戏读键鼠）与诊断探针</summary>
public static partial class PortalUI {
    public static void NoteTogglePause(string from) {
        try {
            Lg.Info($"[LevelLib] 【暂停被调用】来源={from} 游戏输入={_gameInputOn}"
                + $" 面板开={_panelOpen} 场景={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}"
                + $" 帧={Time.frameCount}");
        } catch { }
    }

    /// <summary>游戏是否在"读到按键"（诊断用；输入隔离生效时应全部 false）</summary>
    private static string InputProbe() {
        try {
            return $"cancelPress={RDInput.cancelPress} confirmPress={RDInput.confirmPress}"
                 + $" left={RDInput.leftPress} right={RDInput.rightPress} up={RDInput.upPress} down={RDInput.downPress}";
        } catch (System.Exception e) { return "读取失败:" + e.GetType().Name; }
    }

    // 游戏自己的输入是否开启（面板期间会关掉）
    private static bool _gameInputOn = true;

    /// <summary>游戏现在能不能读到"操作类按键"，面板开着时为 false。
    /// 真正关住输入的是 AutoFeatures.cs 里那几个 prefix 闸门补丁（游戏读按键的唯一出口）；这里只是状态位。
    /// 出任何异常都当"放行"，绝不把游戏卡死。</summary>
    public static bool GameInputAllowed => _gameInputOn;

    /// <summary>开关游戏自己的输入（面板打开时关、关闭时开），两层一起做，缺一不可：
    /// ① 状态位 _gameInputOn —— 输入闸门补丁看它决定放不放行（真正管用的那层）；
    /// ② RDInput 里所有输入类型的 isActive = false —— 让游戏自己的判断一致，并让 scrController.UpdateInput() 提前 return。
    /// 我们界面走自己的 uGUI + UnityEngine.Input，闸门补丁完全不碰它，所以界面永远收得到自己的按键。</summary>
    private static void SetGameInputEnabled(bool on) {
        try {
            if (_gameInputOn == on) return;
            _gameInputOn = on;                    // 先置位：让同一帧稍后跑的闸门补丁立刻看到新状态
            int n = ApplyInputIsActive(on);
            if (!on) ClearGameInputBacklog();      // 关输入的同时清掉异步输入的按键积压
            Lg.Info($"[LevelLib] 游戏输入已{(on ? "开启" : "关闭")}（_isActive {n} 个输入类型）");
        } catch (System.Exception e) { Lg.Ex("切换游戏输入开关失败", e); }
    }

    /// <summary>清空游戏"异步输入"的按键积压：面板期间 isActive 被关掉，而消费队列的 scrController.UpdateInput()
    /// 开头就 return，队列只进不出；恢复输入那一帧游戏会按旧时间戳把整段积压一口气模拟掉，球的角度/位置被拉飞。</summary>
    private static void ClearGameInputBacklog() {
        try {
            AsyncInputManager.ClearKeys();   // 游戏自己的方法：keyQueue + keyMask/keyDownMask/keyUpMask
            // ClearKeys 漏了下面这三个：游戏在 scrController.UpdateInput 里额外清它们，而面板期间那段不会跑
            // 所以开面板那一刻正按着的键会一直留在里面，关面板后游戏以为键还按着（粘键）。
            AsyncInputManager.frameDependentKeyMask.Clear();
            AsyncInputManager.frameDependentKeyDownMask.Clear();
            AsyncInputManager.frameDependentKeyUpMask.Clear();
        } catch { }
    }

    /// <summary>（第二层）把 RDInput 里所有输入类型的 isActive 置位：每种实现开头都是 `if (!isActive) return false;`，
    /// 这一个开关能让游戏自己的判断和我们一致，也能让消费异步输入队列的 scrController.UpdateInput() 提前 return。</summary>
    private static readonly System.Collections.Generic.Dictionary<object, bool> _activeBackup
        = new System.Collections.Generic.Dictionary<object, bool>();

    private static int ApplyInputIsActive(bool on) {
        int n = 0;
        var flag = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                 | System.Reflection.BindingFlags.Public;
        var sflag = flag | System.Reflection.BindingFlags.Static;
        var t = typeof(RDInput);

        System.Action<object> apply = (obj) => {
            if (obj == null) return;
            var ty = obj.GetType();
            while (ty != null) {
                var pi = ty.GetProperty("isActive", flag);
                if (pi != null && pi.CanRead && pi.CanWrite) {
                    if (!on) {
                        // 必须记下原值再恢复：异步键盘构造里就是 _isActive = false，只有玩家选了"异步输入"
                        // 才由 AsyncInputManager.Update 置 true。统一恢复成 true 会凭空打开异步输入，游戏行为就变了。
                        if (!_activeBackup.ContainsKey(obj)) _activeBackup[obj] = (bool)pi.GetValue(obj);
                        pi.SetValue(obj, false);
                    } else {
                        bool orig = true;
                        if (_activeBackup.TryGetValue(obj, out bool saved)) orig = saved;
                        pi.SetValue(obj, orig);
                    }
                    n++; return;
                }
                var fi = ty.GetField("_isActive", flag);   // 兜底：基类 RDInputType 上还有个 protected 字段 _isActive
                if (fi != null) {
                    if (!on) {
                        if (!_activeBackup.ContainsKey(obj)) _activeBackup[obj] = (bool)fi.GetValue(obj);
                        fi.SetValue(obj, false);
                    } else {
                        bool orig = true;
                        if (_activeBackup.TryGetValue(obj, out bool saved2)) orig = saved2;
                        fi.SetValue(obj, orig);
                    }
                    n++; return;
                }
                ty = ty.BaseType;
            }
        };

        var inputsField = t.GetField("inputs", sflag);
        var list = inputsField != null ? inputsField.GetValue(null) as System.Collections.IEnumerable : null;
        if (list != null) foreach (var it in list) apply(it);
        var piField = t.GetField("playerInputs", sflag);
        var piList = piField != null ? piField.GetValue(null) as System.Collections.IEnumerable : null;
        if (piList != null) foreach (var set in piList) {
            var en = set as System.Collections.IEnumerable;
            if (en != null) foreach (var it in en) apply(it);
        }
        // 只遍历 inputs + playerInputs 会漏（实测只数到 8 个，RDInput 上实际有 11 个输入类型实例），
        // 漏掉的那个只要还是 active 游戏就仍读得到按键，所以这里把命名字段和摇杆数组全部点名。
        foreach (var fn in new[] { "keyboardInput", "keyboardLeft", "keyboardRight", "mouseInput",
                                   "asyncKeyboard", "asyncKeyboardLeft", "asyncKeyboardRight" }) {
            var f = t.GetField(fn, sflag);
            if (f != null) apply(f.GetValue(null));
        }
        var jf = t.GetField("joystickInputs", sflag);
        var jarr = jf != null ? jf.GetValue(null) as System.Collections.IEnumerable : null;
        if (jarr != null) foreach (var it in jarr) apply(it);
        if (on) _activeBackup.Clear();     // 恢复完就不需要备份了，下次关闭面板重新采一遍
        return n;
    }

    // ---------- 输入隔离：为什么必须走 RDInput 闸门 ----------
    // 给游戏和我们的界面各做一套 input map 的设想在这个游戏里行不通：映射表只对摇杆生效，键盘完全不经过它。
    // RDInputType_Keyboard 直接读 UnityEngine.Input，RDInputType_AsyncKeyboard 走 AsyncInputManager 掩码，
    // 而 RDInput.SetMapping 只遍历 joystickInputs，所以原来那套反射调 player.controllers.maps.SetAllMapsEnabled
    // 对键盘毫无作用，已整个删除。
    // 真正管用的是关掉游戏读按键的唯一出口 RDInput.GetState / GetMain / GetStateKeys（见 AutoFeatures.cs 的 RdInputGate）：
    // 它们是静态方法，和界面用的 UnityEngine.Input（热键 / Esc / uGUI）毫无交集，
    // 所以"游戏收不到、我们自己照常收得到"能同时成立。

    // ---------- 诊断日志开关 ----------
    /// <summary>mod 目录下存在 diag.txt 时才写"每秒体征 / 状态变化 / 输入探针"这些高频日志，发布版默认安静。</summary>
    private static int _diagChecked = -1;
    public static bool DiagOn {
        get {
            if (_diagChecked < 0) {
                try { _diagChecked = File.Exists(Path.Combine(Main.ModEntry.Path, "diag.txt")) ? 1 : 0; }
                catch { _diagChecked = 0; }
            }
            return _diagChecked == 1;
        }
    }

    /// <summary>关面板后延迟 3 帧再恢复游戏输入：我们关面板是在 mod 的 Update 里，而游戏的 Update 跑在我们后面，
    /// 同一帧里那个 Esc/热键会被游戏再读一次（实测关面板后 1 帧游戏就 TogglePauseGame）。</summary>
    private static int _inputReEnableAfterFrame = -1;
    private static void DeferGameInputEnable() {
        _inputReEnableAfterFrame = Time.frameCount + 3;
    }
    private static void TickDeferredInputEnable() {
        // 兜底自愈：面板已关、也没安排延迟恢复，输入却还是关的（某条路径关掉输入后被异常打断）
        // 直接补上，绝不让玩家遇到"游戏永远收不到按键"。
        if (!_panelOpen && _inputReEnableAfterFrame < 0 && !_gameInputOn) {
            Lg.Info("游戏输入状态不一致（面板已关但输入仍关着），自动恢复");
            SetGameInputEnabled(true);
            return;
        }
        if (_inputReEnableAfterFrame < 0) return;
        if (Time.frameCount < _inputReEnableAfterFrame) return;
        _inputReEnableAfterFrame = -1;
        // 面板在这个延迟窗口里又被打开了（开始界面 / 个人页 / 主界面这类"收一下又马上开"的路径）：
        // 这时绝不能把游戏输入还回去 —— 还回去就是"进了 mod 主界面、input map 却没切换"。
        if (_panelOpen) return;
        SetGameInputEnabled(true);
    }

    /// <summary>面板开着时游戏输入必须是关的（常驻段每帧核对一次，只花一次 bool 比较）。
    /// 必须放在常驻段而不是 TickPanel：面板开着但界面是开始界面 / 个人页 / 主界面那几条路，
    /// TickPanel 也在跑，可游戏里好几处会每帧把 isActive 顶回 true，一次性设置治不住。
    /// 状态漂了就在这里当场纠正并留一行日志（谁打开的面板也一并记上）。</summary>
    private static void EnsureGameInputOffWhilePanelOpen() {
        if (!_panelOpen || !_gameInputOn) return;
        Lg.Info("【输入map兜底】面板开着但游戏输入被打开了 → 再关一次"
            + $"（本次面板由「{PanelOpenSource}」打开）");
        SetGameInputEnabled(false);
    }

    /// <summary>暂停游戏自己的音源（主菜单 BGM 等），关面板时恢复</summary>

    // ================= 点击探针·关闭态：排查「关闭面板后点不动」（诊断用，查完连同调用点一起删） =================
    /// <summary>面板关着时，把鼠标位置的 uGUI 射线命中记进日志：左键按下记一条（每秒最多一条），
    /// 另外每 8 秒按当前位置巡检一条（合计 120 条上限，到顶就闭嘴）。谁在吃点击，命中列表会直接点名
    /// （对象路径 + 画布名 + 画布的运行时排序值）。只读不动：不碰任何界面状态。
    /// （面板开着那份叫 TickClickProbe，在 PortalUI.Keys.cs，两者互斥运行。）</summary>
    private static float _clickProbeAt;
    private static float _clickProbeScanAt;
    private static int _clickProbeLeft = 120;

    private static void TickClickProbeClosed() {
        try {
            if (_panelOpen || _clickProbeLeft <= 0) return;
            bool pressed = UiInput.MouseButtonDown(0);
            float t = Time.unscaledTime;
            if (pressed) { if (t < _clickProbeAt) return; }
            else { if (t < _clickProbeScanAt) return; }
            _clickProbeAt = t + 1f;
            _clickProbeScanAt = t + 8f;
            _clickProbeLeft--;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) {
                Lg.Info("点击探针（面板=关）：EventSystem.current = null —— 界面系统本身没在跑");
                return;
            }
            var ped = new UnityEngine.EventSystems.PointerEventData(es);
            ped.position = UiInput.MousePosition;
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(ped, hits);
            var sb = new System.Text.StringBuilder();
            sb.Append("[LevelLib] 点击探针（面板=关").Append(pressed ? " · 左键按下" : " · 巡检")
              .Append(" · 鼠标=").Append(ped.position.x.ToString("F0")).Append(",").Append(ped.position.y.ToString("F0"))
              .Append("）命中 ").Append(hits.Count).Append(" 个");
            for (int i = 0; i < hits.Count && i < 4; i++) {
                var r = hits[i];
                var go = r.gameObject;
                var cv = go != null ? go.GetComponentInParent<Canvas>() : null;
                sb.Append("  ← ").Append(ClickProbePathOf(go))
                  .Append("［画布=").Append(cv != null ? cv.name : "-")
                  .Append(" 排序=").Append(cv != null ? cv.sortingOrder.ToString() : "-")
                  .Append(cv != null && !cv.isActiveAndEnabled ? " 画布关着" : "")
                  .Append("］");
            }
            if (hits.Count == 0) sb.Append("（没有任何界面接住这一点，点击会直接落到游戏）");
            Lg.Info(sb.ToString());
        } catch (System.Exception e) {
            _clickProbeLeft = 0;
            try { Lg.Ex("点击探针出错（已自行停掉）", e); } catch { }
        }
    }

    /// <summary>对象的层级路径（从根往下，最多 6 层）—— 点击探针点名用</summary>
    private static string ClickProbePathOf(UnityEngine.GameObject go) {
        if (go == null) return "(空对象)";
        var sb = new System.Text.StringBuilder(go.name);
        var t = go.transform.parent;
        for (int i = 0; i < 5 && t != null; i++) { sb.Insert(0, t.name + "/"); t = t.parent; }
        return sb.ToString();
    }
}
