namespace LevelLibrary;

/// <summary>日志统一出口，替代散落各处的 ModEntry.Logger 直调。
/// 三件事收在这里：① "[LevelLib] " 前缀不用每条手写；② Warn/Err 分级标头（UMM 的 ModLogger
/// 只有 Log / LogException 两档，级别靠文本标头区分）；③ 详细日志统一走 Verbose，由
/// settings.txt 的 LogVerbose 开关控制，各处不再自己 if。</summary>
/// <summary>用法：Lg.Info / Lg.Warn / Lg.Err / Lg.Verbose（一行一条），catch 里用 Lg.Ex（写
/// LogException，带异常与堆栈）。消息面向排障：一行说清"哪个环节、发生了什么、关键数值"，
/// 不写解释性长句与装饰符号。ModEntry 未就绪或 logger 抛异常时安静丢弃——日志永远不能反过来砸了功能。</summary>
public static class Lg {
    private const string Prefix = "[LevelLib] ";

    private static UnityModManagerNet.UnityModManager.ModEntry.ModLogger Out {
        get { return Main.ModEntry != null ? Main.ModEntry.Logger : null; }
    }

    public static void Info(string msg) {
        var l = Out;
        if (l == null || string.IsNullOrEmpty(msg)) return;
        try { l.Log(Prefix + msg); } catch { }
    }

    public static void Warn(string msg) {
        var l = Out;
        if (l == null || string.IsNullOrEmpty(msg)) return;
        try { l.Log(Prefix + "[警告] " + msg); } catch { }
    }

    public static void Err(string msg) {
        var l = Out;
        if (l == null || string.IsNullOrEmpty(msg)) return;
        try { l.Log(Prefix + "[错误] " + msg); } catch { }
    }

    /// <summary>详细日志：默认不出，settings.txt LogVerbose=true 才写（高频/诊断类用它）</summary>
    public static void Verbose(string msg) {
        if (!PortalUI.LogVerbose) return;
        Info(msg);
    }

    /// <summary>异常专用（走 UMM 的 LogException，带堆栈）。what 写清"在干什么时出的错"。
    /// 退出阶段的 catch 里不要用它：那时 logger 可能已经收摊，Main.RunBackground 的
    /// ThreadAbort 分支就是 empty-catch，别改。</summary>
    public static void Ex(string what, System.Exception e) {
        var l = Out;
        if (l == null) return;
        try { l.LogException(Prefix + what, e); } catch { }
    }
}
