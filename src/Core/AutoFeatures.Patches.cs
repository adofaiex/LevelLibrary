// [split-of] AutoFeatures.cs lines 1249-1422 body 12-134

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LevelLibrary;

/// <summary>通关回调 patch</summary>
[HarmonyPatch(typeof(scrController), "OnPlayerDied")]
public static class PlayerDiedPatch {
    static void Postfix() => AutoFeatures.OnPlayerDied();
}

[HarmonyPatch(typeof(scrController), "BeatLevel")]
public static class AutoMarkPatch {
    public static void Postfix() {
        AutoFeatures.OnBeatLevel();
    }
}

/// <summary>编辑器打开谱面 patch（自动录入主逻辑）</summary>
[HarmonyPatch(typeof(scnEditor), "OpenLevel", new System.Type[] { typeof(string) })]
public static class OpenLevelPatch {
    public static void Postfix(string filePath) {
        AutoFeatures.OnOpenLevel(filePath);
    }
}

/// <summary>编辑器启动 patch（覆盖双击打开/游戏内打开谱面的路径）</summary>
[HarmonyPatch(typeof(scnEditor), "Start")]
public static class EditorStartPatch {
    public static void Postfix() {
        AutoFeatures.OnEditorStart();
    }
}

/// <summary>主菜单（scnCLS）输入冻结 prefix：面板打开时返回 false（动态 patch Update/LateUpdate/FixedUpdate/OnGUI）</summary>
public static class ClsFreezePatch {
    private static bool _logged;
    public static bool Prefix() {
        if (PortalUI.IsOpen) {
            if (!_logged) { _logged = true; Lg.Info("诊断: ClsFreezePrefix 生效（面板打开，主菜单 Update 系已冻结）"); }
        } else {
            _logged = false;
        }
        return !PortalUI.IsOpen;
    }
}

// 诊断探针：只记录 TogglePauseGame 被调过，完全不改变行为。
public static class PauseCallProbe {
    public static void Prefix() {
        try { PortalUI.NoteTogglePause("TogglePauseGame"); } catch { }
    }
}

// CancelFreezePatch / PauseGuardPatch 已全部移除。
// 理由：游戏的暂停要同时改 paused / audioPaused / enabled / timeScale / 暂停菜单五处状态，
// 无论吃掉还是事后纠正都只能改一半，会留下"paused=true 没人恢复"或"歌被重置到开头"的半状态。
// 现在只保留一种安静做法：面板打开期间把 conductor.bpm 置 0，关闭时还原，不碰游戏的暂停系统。

// ================= 输入隔离：关掉游戏读按键的出口 =================
// Rewired 那套 input map 对本作没用：RDInput.SetMapping 只遍历摇杆映射，键盘走
// RDInputType_Keyboard 直接读 UnityEngine.Input，一个键都不经过映射表。
// 所以"游戏输入"真正的出口是下面三个静态方法，面板打开时让它们直接回答"没按键"即可。
// 不拦 UnityEngine.Input.GetKey*：那是我们和游戏共用的接口，拦了热键、Esc 自己也读不到。

/// <summary>输入闸门公共判断。任何异常都当"放行" —— 宁可漏一次输入，也绝不能把游戏卡死。</summary>
public static class InputGateCommon {
    private static bool _loggedOnce;
    /// <summary>面板开着（游戏输入被关）时返回 true，调用方应直接返回"没按键"。</summary>
    public static bool Blocked(string from) {
        try {
            if (PortalUI.GameInputAllowed) return false;
            if (!_loggedOnce) {
                _loggedOnce = true;
                Lg.Info($"[LevelLib] 输入闸门已接管：{from} 被挡下（面板开着，游戏收不到按键）");
            }
            return true;
        } catch { return false; }
    }
}

[HarmonyPatch(typeof(RDInput), nameof(RDInput.GetState))]
public static class RdInputGetStateGate {
    public static bool Prefix(ref bool __result) {
        if (!InputGateCommon.Blocked("GetState")) return true;
        __result = false;
        return false;   // 跳过原方法
    }
}

[HarmonyPatch(typeof(RDInput), nameof(RDInput.GetMain))]
public static class RdInputGetMainGate {
    public static bool Prefix(ref int __result) {
        if (!InputGateCommon.Blocked("GetMain")) return true;
        __result = 0;
        return false;
    }
}

[HarmonyPatch(typeof(RDInput), nameof(RDInput.GetStateKeys))]
public static class RdInputGetStateKeysGate {
    /// <summary>`__result` 的类型必须与原方法返回值一字不差（否则 Harmony 挂不上，只会在日志里报一句话）。
    /// 注册那边也要按签名找方法：只写名字会在重载里挑错。</summary>
    public static bool Prefix(ref List<AnyKeyCode> __result) {
        if (!InputGateCommon.Blocked("GetStateKeys")) return true;
        __result = new List<AnyKeyCode>();
        return false;
    }
}

/// <summary>必须挂：AsyncInputManager.Update() 每帧把 keyboardInput.isActive 等强制置回 true，
/// 会盖掉"关掉游戏输入"的设置（还会让 scrController.UpdateInput() 开始消费积压的按键）。
/// 面板期间整体跳过它，并顺手清空按键积压（队列的另一半见 PortalUI.ClearGameInputBacklog）。</summary>
[HarmonyPatch(typeof(AsyncInputManager), "Update")]
public static class AsyncInputManagerGate {
    public static bool Prefix() {
        try {
            if (PortalUI.GameInputAllowed) return true;
            AsyncInputManager.ClearKeys();
            return false;
        } catch { return true; }
    }
}

// 拦单个按键的那套补丁已全部删除（UnityEngine.Input.GetKey* 系、RDInput 主路径、
// scrPlayerManager.AnyValidInputWasTriggered、RDInputType_Keyboard.CheckKeyState）。
// 拦按键既没必要（隔离已由上面的闸门完成），也是"开一次面板后热键关不掉"的原因：
// 热键（如 E）与游戏读的是同一个 Input，一起被拦哑。
