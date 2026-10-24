using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>界面文字翻译拦截层：给 uGUI `Text.text` 的 setter 挂 Harmony prefix，
/// 赋值进 setter 之前把字符串过一遍翻译表（查不到原样不动）。界面文字散落在几百处，逐处包 Tr() 容易漏。
/// 必须是 prefix：postfix 在 setter 跑完之后才执行，那时参数早已写进 m_Text 字段，改 ref value 只改局部参数、静默失效。
/// 作用范围是全局所有 uGUI `Text`（表以中文原文为 key，命中面很小）；中文环境下 Tr() 原样返回，这一层等于不存在。</summary>
public static class TextTranslatePatch {
    /// <summary>这些字符串里出现了就跳过（富文本标签 / 格式化占位符）</summary>
    private static bool ShouldSkip(string s) {
        if (string.IsNullOrEmpty(s)) return true;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            if (c == '<' || c == '>') return true;   // 富文本
            if (c == '{') return true;               // 格式化占位符
        }
        return false;
    }

    /// <summary>Harmony prefix：在 setter 把参数写进字段之前换成译文。别改回 postfix（改参数不回写字段，静默失效）</summary>
    internal static void Prefix(ref string value) {
        if (Lang.Current == "zh") return;            // 中文：原样
        if (ShouldSkip(value)) return;
        value = Lang.Tr(value);
    }

    /// <summary>把已经建好、正显示着旧语言的界面文字按新的 `Lang.Current` 重刷一遍。
    /// Prefix 只拦赋值那一下，所以光改 `Lang.Current` 只对之后新建的文字生效，已上屏的和开机预热建好的几页会停在旧语言。
    /// 做法：每条文字先用 `Lang.ToZh` 反查回中文原文再赋回去，赋值走 setter（prefix 在中间），自动按新语言翻一遍，幂等。
    /// 只扫我们自己 `LevelLib_` 前缀的画布，绝不碰游戏自己的界面；只在玩家点语言按钮那一下调，不是每帧的东西。
    /// （UMM 自己的设置界面是 IMGUI，不在任何画布里，靠每帧重算。）</summary>
    internal static int RefreshUiTexts() {
        int n = 0;
        try {
            // 成就页那行文本做了"内容没变就不重写"的缓存（每帧 60 次 Text 赋值会反复重建网格）。
            // 但它的缓存键是「翻译后的」字符串，语言切换后值没变、缓存却会把新语言挡在外面。
            // 这里就是"全部已上屏文字按新语言重刷"的唯一入口，在这一层作废最不容易漏。
            PortalUI.InvalidateAchBarValues();
            // 走画布登记表：切换语言是玩家操作那一刻的事，那一下的全场景搜索是实打实的手感开销
            var texts = new List<Text>();
            PortalUI.CollectOurGraphics(texts);
            for (int k = 0; k < texts.Count; k++) {
                var t = texts[k];
                if (t == null) continue;
                string cur = t.text;
                if (string.IsNullOrEmpty(cur)) continue;
                if (ShouldSkip(cur)) continue;      // 富文本 / 格式化串一律不碰（跟 Prefix 同一条判据）
                string zh = Lang.ToZh(cur);
                if (zh == cur && Lang.Current == "zh") continue;   // 中文环境下没什么可刷的
                t.text = zh;                        // 走 setter 按新语言翻一遍
                n++;
            }
        } catch (System.Exception e) {
            try { Main.ModEntry.Logger.LogException("界面文字重刷失败（保持原样）", e); } catch { }
        }
        return n;
    }
}
