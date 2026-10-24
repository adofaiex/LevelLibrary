using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LevelLibrary;

/// <summary>两套 input map 与切换点：游戏一套、我们界面一套。
/// 这个游戏键盘的唯一「总入口」就是 UnityEngine.Input —— Rewired 那几张 map 只对摇杆生效（键盘走 RDInputType_Keyboard 里硬编码的 KeyCode），
/// 另有 50 多处直接读 UnityEngine.Input，所以切换点挂在它上面：
/// 游戏 map 生效 → 照常放行；UI map 生效 → 游戏发起的读取一律答「没有输入」，我们的界面走 UiInput.*（见下面的 Gate）。</summary>
public static class InputMap {
    public enum Side { Game, Ui }

    /// <summary>现在生效的是哪一套 map（面板开着 = UI map）。</summary>
    public static Side Active => PortalUI.GameInputAllowed ? Side.Game : Side.Ui;

    /// <summary>UI map 是否生效（= 游戏输入被切掉）。</summary>
    public static bool UiMapActive {
        get {
            try { return !PortalUI.GameInputAllowed; } catch { return false; }
        }
    }
}

/// <summary>UI map 的读法：我们界面自己的键鼠读取全部走这里。
/// 每次读的瞬间把 Gate 放开（Bypass 计数 +1）、读完立刻还原，所以拿到的是真实输入，而游戏那边仍然什么都读不到。
/// 同步调用、单线程，计数是稳的。</summary>
public static class UiInput {
    private static int _bypass;

    /// <summary>当前是否有"我们自己"的读取正在进行（Gate 看到它就放行）。</summary>
    public static bool Bypassing => _bypass > 0;
    public static void BeginBypass() { _bypass++; }
    public static void EndBypass() { if (_bypass > 0) _bypass--; }

    /// <summary>安全网：我们自己的 Update 开头一定要调用它。
    /// 正常情况下此刻不该有 Bypass 没还回去（EventSystem / 输入框都在别的时机跑），所以这里归零；
    /// 万一某次 Prefix/Finalizer 没配对，也不会让闸门一直开着变成永久漏输入（最多漏一帧）。
    /// 返回被迫归零前的深度（非 0 说明真的漏配过，用来记日志）。</summary>
    public static int SanityResetBypass() {
        int was = _bypass;
        _bypass = 0;
        return was;
    }

    public static bool Key(KeyCode k) {
        BeginBypass(); try { return UnityEngine.Input.GetKey(k); } finally { EndBypass(); }
    }
    public static bool KeyDown(KeyCode k) {
        BeginBypass(); try { return UnityEngine.Input.GetKeyDown(k); } finally { EndBypass(); }
    }
    /// <summary>屏保空闲计时用：这一帧有没有按过任何键/鼠标键（Input.anyKeyDown 本身就含鼠标左中右）。
    /// 同样要 Bypass，否则 UI map 生效时读到的永远是 false、空闲计时就废了。</summary>
    public static bool AnyKeyDown {
        get { BeginBypass(); try { return UnityEngine.Input.anyKeyDown; } finally { EndBypass(); } }
    }
    public static string InputString {
        get { BeginBypass(); try { return UnityEngine.Input.inputString; } finally { EndBypass(); } }
    }
    public static Vector3 MousePosition {
        get { BeginBypass(); try { return UnityEngine.Input.mousePosition; } finally { EndBypass(); } }
    }
    public static Vector2 MouseScrollDelta {
        get { BeginBypass(); try { return UnityEngine.Input.mouseScrollDelta; } finally { EndBypass(); } }
    }
    public static bool MouseButton(int b) {
        BeginBypass(); try { return UnityEngine.Input.GetMouseButton(b); } finally { EndBypass(); }
    }
    public static bool MouseButtonDown(int b) {
        BeginBypass(); try { return UnityEngine.Input.GetMouseButtonDown(b); } finally { EndBypass(); }
    }
    public static bool MouseButtonUp(int b) {
        BeginBypass(); try { return UnityEngine.Input.GetMouseButtonUp(b); } finally { EndBypass(); }
    }
}

/// <summary>map 切换闸门：挂在 UnityEngine.Input 的每一个键鼠入口上。
/// UI map 生效时，凡是游戏发起的读取（不是 UiInput 的、也不是 uGUI 输入模块的）一律回答「没有输入」：
/// bool→false、string→空、数值→0、坐标→零向量。任何异常都放行（宁可漏一次，也绝不能把游戏卡死）。
/// 每个 prefix 方法对应一种返回类型，注册时用 HarmonyMethod 显式指认（见 Main.cs 的 RegisterInputMapGate），不用特性标注。</summary>
public static class InputMapGate {
    private static bool Open() {
        try { return UiInput.Bypassing || !InputMap.UiMapActive; } catch { return true; }
    }

    public static bool Bool(ref bool __result) {
        if (Open()) return true;
        __result = false;
        return false;
    }
    public static bool Float(ref float __result) {
        if (Open()) return true;
        __result = 0f;
        return false;
    }
    public static bool Str(ref string __result) {
        if (Open()) return true;
        __result = string.Empty;
        return false;
    }
    public static bool Vec2(ref Vector2 __result) {
        if (Open()) return true;
        __result = Vector2.zero;
        return false;
    }
    public static bool Vec3(ref Vector3 __result) {
        if (Open()) return true;
        __result = Vector3.zero;
        return false;
    }
}

/// <summary>uGUI 输入模块跑在 UI map 里：面板里的按钮、滚动条、输入框、拖拽全靠 EventSystem / StandaloneInputModule 每帧读键鼠，
/// 那一段必须能读到真实输入，否则面板自己就哑了。
/// 两层都挂：EventSystem.Update（总入口）与 StandaloneInputModule.Process（实际读键鼠的地方），嵌套靠 Bypass 计数处理，
/// Finalizer 保证抛异常时也会关。Process 里顺带清掉游戏按钮的焦点（空格/回车不触发游戏按钮）。</summary>
[HarmonyPatch(typeof(UnityEngine.EventSystems.EventSystem), "Update")]
public static class EventSystemUiMapPatch {
    static void Prefix() { UiInput.BeginBypass(); }
    static System.Exception Finalizer(System.Exception __exception) {
        UiInput.EndBypass();
        return __exception;      // 原样返回：不改异常
    }
}

[HarmonyPatch(typeof(UnityEngine.EventSystems.StandaloneInputModule), "Process")]
public static class StandaloneProcessGuardPatch {
    static void Prefix() {
        UiInput.BeginBypass();
        try { PortalUI.ClearGameFocus(); } catch { }
    }
    static System.Exception Finalizer(System.Exception __exception) {
        UiInput.EndBypass();
        return __exception;
    }
}
