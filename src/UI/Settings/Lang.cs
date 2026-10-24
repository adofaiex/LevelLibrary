using System.Collections.Generic;
using UnityEngine;

namespace LevelLibrary;

/// <summary>多语言：界面文字统一走 Tr()。
/// 以中文原文为 key（源码里照旧写中文，改动最小也不会漏翻），查不到译文就原样返回中文。
/// 语言存 settings.txt 的 Language 键（zh / en / ko），默认中文。</summary>
public static class Lang {
    /// <summary>LangData 是纯注册表，只靠静态构造器跑，而全项目没有别处引用它，
    /// 所以必须在这里显式触发一次：少了这一行 _en/_ko 永远为空，Tr() 全部原样返回中文。
    /// 顺序安全：静态字段初始化器排在 cctor 最前；反向重入时 CLR 对正在初始化中的类型直接放行。</summary>
    static Lang() {
        LangData.EnsureRegistered();
    }

    /// <summary>当前语言代码：zh / en / ko。</summary>
    public static string Current = "zh";

    /// <summary>可选语言：Codes 与 Names 同下标一一对应。</summary>
    public static readonly string[] Codes = { "zh", "en", "ko" };
    public static readonly string[] Names = { "中文", "English", "한국어" };

    private static readonly Dictionary<string, string> _zh = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> _en = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> _ko = new Dictionary<string, string>();

    /// <summary>取当前语言的译文字典；中文返回 null（中文即原文，不查表），调用方必须处理 null。</summary>
    private static Dictionary<string, string> Table() {
        if (Current == "en") return _en;
        if (Current == "ko") return _ko;
        return null;
    }

    /// <summary>翻译界面字符串；查不到词条或译文为空时原样返回中文。</summary>
    public static string Tr(string zh) {
        if (string.IsNullOrEmpty(zh)) return zh;
        var t = Table();
        if (t == null) return zh;
        return t.TryGetValue(zh, out var v) && !string.IsNullOrEmpty(v) ? v : zh;
    }

    /// <summary>某语言的词条数（设置页用它显示「已翻译 N 条」）。</summary>
    public static int Count(string code) {
        if (code == "en") return _en.Count;
        if (code == "ko") return _ko.Count;
        return _zh.Count;
    }

    /// <summary>注册词条，由各语言文件在静态构造里调用。</summary>
    public static void Add(string zh, string en, string ko) {
        if (string.IsNullOrEmpty(zh)) return;
        if (!string.IsNullOrEmpty(en)) _en[zh] = en;
        if (!string.IsNullOrEmpty(ko)) _ko[zh] = ko;
        _zh[zh] = zh;
    }

    /// <summary>反查：把正显示着的译文换回中文原文（查不到原样返回）。
    /// 必须 _en / _ko 两张表都查 —— 调用时机正是 Current 已切成新语言、而屏幕上还是旧译文，
    /// 只查当前语言那张表就永远换不掉。用途见 TextTranslatePatch.RefreshUiTexts。</summary>
    public static string ToZh(string cur) {
        if (string.IsNullOrEmpty(cur)) return cur;
        foreach (var kv in _en) if (kv.Value == cur) return kv.Key;
        foreach (var kv in _ko) if (kv.Value == cur) return kv.Key;
        return cur;
    }

    /// <summary>带占位符的模板翻译：拼出来的整串查不到词条，所以把整句连占位符写进词条表，翻完再 string.Format。
    /// 动态内容（数字 / 谱面名 / 玩家名）只当参数，不参与翻译。</summary>
    public static string TrFmt(string zhTemplate, params object[] args) {
        string tpl = Tr(zhTemplate);
        if (args == null || args.Length == 0) return tpl;
        try { return string.Format(tpl, args); } catch { return tpl; }
    }

    /// <summary>语言代码是否在 Codes 里。</summary>
    public static bool IsValid(string code) {
        for (int i = 0; i < Codes.Length; i++) if (Codes[i] == code) return true;
        return false;
    }
}
