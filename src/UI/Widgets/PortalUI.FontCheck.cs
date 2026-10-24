using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>界面字体自检 + 自修复：设置 → 操作 → 诊断 →「检查界面字体」；报告写日志 + 落一份 `font_check.txt`，
/// 返回一行结论给屏幕。查三件事：① 通用字体本身是 null 或成了楷体；② 我们自己画的 Text 混进楷体；
/// ③ 我们自己画的 Text font == null（一个字都不显示）。
/// 为什么有玩家明明坏了、点它却说"一切正常"：原来的自检只有上面三条判据，留了三个盲区。
/// 盲区一，它只看现在 —— "全世界变楷体"这种毛病常常在玩家去点按钮之前就自愈了（页面重建、字体重新解析，
/// 那一刻场上的每一个字都已经是好的），所以现在在建字那一瞬间就拦一道（GuardFontOnCreate），坏过就计数、就当场落一份报告文件。
/// 盲区二，它不查这张字体画不画得出中文 —— 退回内置 Arial 时中文全是空白或方块，可是字体非空、也不是楷体，
/// 于是旧自检报"正常"，现在多一条中文覆盖探针（FontProbeChars）。
/// 盲区三，它不查字体是哪来的 —— 别的 mod 往字体池里塞一张、或游戏换了原生字体，旧自检也看不出来，
/// 现在报告里带"字体分布"（我们的字用哪几张 / 游戏与别人的字用哪几张）和"跟第一次捡到的那张对不对得上"。
/// 盲区四，判"是不是内置兜底"原来只看来源对象名里有没有 Arial —— 玩家机器上原生 UI 吃的是内置字体，
/// 但那个对象叫 OfficialNoFail，于是自检对着满屏楷体报"一切正常"。现在改成看字体自己的名字
/// （Unity 6 的内置字体已经改名叫 LegacyRuntime），来源名只当一条辅助判据。
/// 楷体的唯一来源必须走 FindUiFont：一旦捡到我们自己建的楷体标题，全局通用字体都会被带成楷体，之后每个界面元素都是楷体。
/// 刻意用楷体的两处（开始界面大标题、开发者页制作名单大标题）建的时候登记进 _kaitiIntended，自检按这张表放过，别当毛病修掉。
/// 修复动作只换 Text.font，不碰字号/颜色/布局；换完必须重烘字形图集（图集按 字体×字号×字形 分组），否则下一个新字会在玩家正看的那一帧现生成，卡一下。</summary>
public static partial class PortalUI {

    /// <summary>刻意用楷体的那几处字，自检时放过</summary>
    private static readonly List<Text> _kaitiIntended = new List<Text>();

    /// <summary>上一次字体自检是不是"一切正常、一处都没改"（开机自检用它决定要不要在屏幕上说一句）。</summary>
    public static bool LastFontCheckOk { get; private set; }

    /// <summary>中文覆盖探针：这些字我们界面上天天出现（库/谱面/成就/设置/开发者/关卡/记录），
    /// 一张字体连它们都画不出来，玩家看到的就一定是空白或方块。</summary>
    private const string FontProbeChars = "谱面库成就设置开发者关卡记录";

    /// <summary>建字期拦截计数：空字体 / 楷体 / 退回内置 Arial / 显示不出中文 / 跟第一次捡到的那张对不上。自检报告里会带出来。</summary>
    private static int _fontGuardNull, _fontGuardKaiti, _fontGuardArial, _fontGuardNoCjk, _fontGuardOther;
    private static bool _fontGuardLogged, _fontGuardResolved, _fontGuardDumped;

    /// <summary>登记一处“刻意用楷体”的字；建的时候调，只登记，不改变任何行为</summary>
    private static void RegisterKaitiIntended(Text t) {
        try { if (t != null && !_kaitiIntended.Contains(t)) _kaitiIntended.Add(t); } catch { }
    }

    /// <summary>这个 Text 是不是“刻意用楷体”的那几处之一（顺手清掉已销毁的）</summary>
    private static bool IsKaitiIntended(Text t) {
        for (int i = _kaitiIntended.Count - 1; i >= 0; i--) {
            var x = _kaitiIntended[i];
            if (x == null) { _kaitiIntended.RemoveAt(i); continue; }
            if (x == t) return true;
        }
        return false;
    }

    /// <summary>这个字体是不是楷体那一族，两条判据任一成立就算：① 就是我们自己建的那份楷体对象（最准）；
    /// ② 字体名里带 kai 或 楷（别的来源的楷体也认得出来）。</summary>
    private static bool IsKaitiFont(Font f) {
        if (f == null) return false;
        try {
            var k = KaitiFont();
            if (k != null && f == k) return true;
            string n = f.name;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("kai", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("楷", System.StringComparison.Ordinal) >= 0;
        } catch { return false; }
    }

    /// <summary>通用字体是不是"内置兜底"（老版本叫 Arial、Unity 6 里叫 LegacyRuntime）：它没有中文字形，
    /// 界面上中文要么空白或方块，要么靠系统回退 —— 回退成哪张字由玩家的机器决定（有的机器就是楷体）。
    /// 判据必须同时看来源对象名与字体名：出过事的机器来源是 `OfficialNoFail`（对象名里没有 Arial），
    /// 而它身上挂的正是 LegacyRuntime —— 只看来源名的话这一类永远报"正常"。</summary>
    private static bool UiFontIsBuiltinFallback() {
        try {
            if (!string.IsNullOrEmpty(_uiFontSource)
                && _uiFontSource.IndexOf("Arial", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return IsBuiltinFallbackFont(_uiFont);
        } catch { return false; }
    }

    /// <summary>现在这张通用字体是不是"不是游戏原版的那张"（系统字体、或被字体 mod 换进来的）。
    /// 这类字体能显示中文，但它们的动态字形图集与游戏原版那套假设对不上 ——
    /// 大标题的投影（uGUI 的 Shadow，靠字形图集再画一遍顶点）会缺字/被截断；
    /// 而内置 Arial 连中文都没有，中文会按系统默认回退（有的机器就是楷体）。
    /// 早先的自检只判"空 / 楷体 / 内置兜底"三类，这类全放过去了，出过一次满屏楷体。
    /// 判据复用挑选那套：没有 fontNames 的运行时字体（系统字体、字体 mod 注入的）都算。</summary>
    private static bool UiFontNotNative() {
        try {
            if (_uiFont == null) return false;          // 空是另一类（① 里单独报）
            if (IsBuiltinFallbackFont(_uiFont)) return false;   // 内置兜底由 UiFontIsBuiltinFallback 报
            return IsRuntimeCreatedFont(_uiFont);
        } catch { return false; }
    }

    /// <summary>现在这张通用字体，跟第一次从游戏原生 UI 里捡到的那张对不上（对象不同、名字也不同）。
    /// 正常情况下一局里 _uiFont 只解析一次、之后不会变；对不上就说明中间被换过（别的 mod 或游戏换了字体）。</summary>
    private static bool UiFontIsStranger(Font f) {
        try {
            if (f == null || _uiFontNative == null || f == _uiFontNative) return false;
            string a = f.name, b = _uiFontNative.name;
            if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        } catch { return false; }
    }

    /// <summary>这张字体画不画得出我们界面里的中文（HasCharacter 问不出来时当它没事，宁可漏报不误报）。</summary>
    private static bool FontCoversChinese(Font f, out char missing) {
        missing = '\0';
        try {
            if (f == null) { missing = '?'; return false; }
            for (int i = 0; i < FontProbeChars.Length; i++) {
                bool has;
                try { has = f.HasCharacter(FontProbeChars[i]); } catch { return true; }
                if (!has) { missing = FontProbeChars[i]; return false; }
            }
            return true;
        } catch { return true; }
    }

    /// <summary>字体名（拿不到就给个占位，日志用）</summary>
    private static string FontName(Font f) {
        try { if (f == null) return "(null)"; return string.IsNullOrEmpty(f.name) ? "(无名)" : f.name; }
        catch { return "(?)"; }
    }

    /// <summary>建字那一瞬间查一遍字体（MakeText 里调）。玩家去点自检按钮时，这一屏往往已经被重建过、
    /// 字体也重新解析过，光看"现在"什么都看不到，所以坏过就要留下痕迹：计数 + 一条日志 + 一份文件。</summary>
    private static void GuardFontOnCreate(Text text) {
        try {
            if (text == null) return;
            Font f = _uiFont;
            bool covers = FontCoversChinese(f, out _);             // 探针：这张字画不画得出界面里的中文
            bool hardBad = (f == null) || IsKaitiFont(f) || !covers;   // 空 / 楷体 / 显示不出中文：硬错，能当场救
            if (f == null) _fontGuardNull++;
            else if (IsKaitiFont(f)) _fontGuardKaiti++;
            else if (UiFontIsBuiltinFallback()) _fontGuardArial++;
            else if (!covers) _fontGuardNoCjk++;
            else if (UiFontIsStranger(f)) _fontGuardOther++;
            else return;                                           // 好的，什么都不做

            if (!_fontGuardLogged) {
                _fontGuardLogged = true;
                Lg.Info(" 建字时通用字体就不对：" + FontName(f)
                    + "（取自：" + (string.IsNullOrEmpty(_uiFontSource) ? "?" : _uiFontSource) + "）"
                    + " ｜ 第一次出现在 " + PathOf(text));
            }
            if (hardBad && !_fontGuardResolved) {
                // 当场重新解析一次，别让玩家对着一屏坏字（自检按钮里那套重解析是同一个动作）
                _fontGuardResolved = true;
                _uiFont = null;
                _uiFont = FindUiFont();
                Lg.Info("建字时重新解析通用字体 → " + FontName(_uiFont)
                    + "（取自：" + (string.IsNullOrEmpty(_uiFontSource) ? "?" : _uiFontSource) + "）");
            }
            if (!_fontGuardDumped) { _fontGuardDumped = true; DumpFontReport("建字时发现问题"); }
        } catch { }
    }

    /// <summary>把一份自检报告落到 mod 目录里的 `font_check.txt`：玩家不用去翻 Player.log，
    /// 直接把这个文件发过来就行；顺带把"建字期拦截"的计数也写进去，那是自检按钮看不见的那段时间的证据。</summary>
    private static void DumpFontReport(string why) {
        try {
            string dir = Main.ModEntry.Path;
            if (string.IsNullOrEmpty(dir)) return;
            string file = Path.Combine(dir, "font_check.txt");
            var sb = new StringBuilder();
            sb.Append("LevelLibrary 字体自检报告（").Append(why).Append("）\n");
            sb.Append("时间：").Append(System.DateTime.Now.ToString()).Append('\n');
            sb.Append("通用字体：").Append(FontName(_uiFont))
              .Append("（取自：").Append(string.IsNullOrEmpty(_uiFontSource) ? "?" : _uiFontSource).Append("）\n");
            sb.Append("第一次从游戏 UI 捡到的字体：").Append(FontName(_uiFontNative))
              .Append("（取自：").Append(string.IsNullOrEmpty(_uiFontNativeSource) ? "?" : _uiFontNativeSource).Append("）\n");
            sb.Append("建字期拦截：空字体 ").Append(_fontGuardNull)
              .Append(" ｜ 楷体 ").Append(_fontGuardKaiti)
              .Append(" ｜ 内置兜底 ").Append(_fontGuardArial)
              .Append(" ｜ 显示不出中文 ").Append(_fontGuardNoCjk)
              .Append(" ｜ 跟前面对不上 ").Append(_fontGuardOther).Append('\n');
            if (_uiFontNoCjk != null)
                sb.Append("游戏原生 UI 里第一张显示不出中文的字体：").Append(FontName(_uiFontNoCjk))
                  .Append("（取自：").Append(string.IsNullOrEmpty(_uiFontNoCjkSource) ? "?" : _uiFontNoCjkSource).Append("）\n");
            sb.Append("提示：把这份文件连 Player.log 一起发给作者（报告只记字体，日志里有具体是哪个界面元素）\n");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Lg.Info("字体自检报告已写到 " + file);
        } catch (System.Exception e) {
            try { Lg.Ex("写字体自检报告失败", e); } catch { }
        }
    }

    /// <summary>设置 → 操作 → 诊断 →「检查界面字体」：能当场修的修掉，完整报告进日志，返回一行给屏幕看的结论</summary>
    public static string CheckAndFixUiFont() {
        var sb = new StringBuilder();
        int fixedCount = 0;
      // 本次自检有没有"重新挑过通用字体"：挑过的话要把场上所有字都刷成新字体，
      // 而不只是修那几个出错的 —— 否则玩家看到的是"字体换了但大部分字没换"的半吊子界面。
        bool reloaded = false;
        try {
            sb.Append("【字体自检】开始 —— 报告如下\n");

            // ---------- ① 通用字体本身 ----------
            Font bad = _uiFont;
      // 「坏」有四类：空、变成了楷体、内置兜底（没中文字形）、以及"不是游戏原版的那张"。
      // 最后一类是补的：系统字体（雅黑等）与字体 mod 换进来的字体都能显示中文，
      // 早先只判前三类，这类"能用但图集对不上"的就全放过去了，界面满屏楷体/投影缺字，
      // 而自检还报"一切正常"（真出过这一次）。
            bool uiFontWasBad = (bad == null) || IsKaitiFont(bad) || UiFontIsBuiltinFallback()
                             || UiFontNotNative();
            sb.Append("① 通用字体：").Append(FontName(_uiFont))
              .Append("（取自：").Append(string.IsNullOrEmpty(_uiFontSource) ? "?" : _uiFontSource).Append("）");
            if (uiFontWasBad) sb.Append("  ").Append(_uiFont == null ? "是空的" : IsKaitiFont(bad) ? "变成了楷体！" : "不是游戏原版的那张（能用，但投影可能不完美）");
            sb.Append('\n');
            if (!string.IsNullOrEmpty(_uiFontFromOurs))
                sb.Append("    记录：这次还在我们自己的「").Append(_uiFontFromOurs).Append("」身上看到过字体（诊断用）\n");
            if (UiFontIsBuiltinFallback())
                sb.Append("   它是内置兜底（老版本叫 Arial、Unity 6 里叫 LegacyRuntime）—— 它没有中文字形：")
                  .Append("界面上中文要么空白或方块，要么靠系统回退，回退成哪张字由这台机器决定（有的机器就是楷体）\n");

            if (uiFontWasBad) {
                // 清掉上一步记下的坏字体，整个重新解析一遍
                _uiFont = null;
                var again = FindUiFont();
                _uiFont = again;
                bool stillBad = (again == null) || IsKaitiFont(again) || UiFontNotNative();
                sb.Append("   ->  重新解析：").Append(FontName(again))
                  .Append("（取自：").Append(string.IsNullOrEmpty(_uiFontSource) ? "?" : _uiFontSource).Append("）")
                  .Append(stillBad ? "  还是不对（这台机器上装过字体 mod，原版字体被换掉了，只能用它换进来的那张）" : "  修好了").Append('\n');
      // 换过字体之后，场上每一处字都要刷成新字体（下面 ② 只修"错的"，
      // 而刚换完时它们全都是"旧的"）。这里先记一笔，由 ② 那段统一刷。
                if (!stillBad && again != null) reloaded = true;
            }
            if (_uiFont == null || IsKaitiFont(_uiFont)) {
                // 兜底再试一次：局内第一次没捡到、现在游戏 UI 已经建出来了，这一次通常能捡到
                _uiFont = null;
                var retry = FindUiFont();
                _uiFont = retry;
                sb.Append("   ->  再解析一次（游戏 UI 这会儿应该建好了）：").Append(FontName(retry))
                  .Append("（取自：").Append(string.IsNullOrEmpty(_uiFontSource) ? "?" : _uiFontSource).Append("）").Append('\n');
            }
            char missing = '\0';
            bool covers = FontCoversChinese(_uiFont, out missing);
            sb.Append("   · 中文覆盖：").Append(covers ? " 探针里的字它都画得出来" : (" 画不出「" + missing + "」，界面上中文会缺字"))
              .Append("（探针 ").Append(FontProbeChars).Append("）\n");
            if (_uiFontNative != null)
                sb.Append("   · 跟第一次捡到的对得上吗：")
                  .Append(UiFontIsStranger(_uiFont) ? " 对不上（中间被换过）" : " 一致")
                  .Append("（第一次：").Append(FontName(_uiFontNative))
                  .Append("，取自 ").Append(string.IsNullOrEmpty(_uiFontNativeSource) ? "?" : _uiFontNativeSource).Append("）\n");

            // ---------- ② 我们自己的每一处字 ----------
            var texts = Resources.FindObjectsOfTypeAll<Text>();
            int ours = 0, kaitiWrong = 0, nullFont = 0, fixedToUi = 0, keptKaiti = 0, strange = 0;
            int reloadedFixed = 0;   // 重新挑过通用字体后，被刷成新字体的处数
            var samples = new StringBuilder();
            var census = new Dictionary<string, int[]>();
            var strangers = new List<string>();
            for (int i = 0; i < texts.Length; i++) {
                var t = texts[i];
                try {
                    if (t == null || !IsOurUi(t)) continue;     // 只查我们自己画的界面
                    ours++;
                    string fn = FontName(t.font);
                    int[] cell;
                    if (!census.TryGetValue(fn, out cell)) { cell = new int[2]; census[fn] = cell; }
                    cell[0]++;
                    if (IsKaitiIntended(t)) { keptKaiti++; continue; }   // 故意用楷体的两处 放过
      // 刚重新挑过通用字体：场上这些字用的都是上一张（旧字体），逐个刷成新字体。
      // 不刷的话就是"字体换了、大部分字没换"的半吊子界面（满屏楷体那次就是这样）。
                    if (reloaded && t.font != _uiFont) { t.font = _uiFont; reloadedFixed++; }
                    bool isNull = (t.font == null);
                    bool isKaiti = !isNull && IsKaitiFont(t.font);
                    // 字体本身没错、但跟通用字体不是同一张：这一处来路不明（别的 mod 塞的，或者字体池被换过）
                    if (!isNull && !isKaiti && t.font != _uiFont) {
                        strange++;
                        if (strangers.Count < 8) strangers.Add(PathOf(t) + "（" + fn + "）");
                    }
                    if (!isNull && !isKaiti) continue;          // 正常
                    if (isNull) nullFont++; else kaitiWrong++;
                    if (samples.Length < 400)
                        samples.Append("   · ").Append(PathOf(t)).Append("：字号 ").Append(t.fontSize)
                               .Append(" 字体=").Append(FontName(t.font)).Append(isNull ? "（空）" : "（楷体）").Append('\n');
                    // 修：换成通用字体（拿不到就跳过这一处，别写个 null 上去）
                    if (_uiFont != null) { t.font = _uiFont; fixedToUi++; }
                } catch { }
            }
            sb.Append("② 我们自己画的 Text 共 ").Append(ours).Append(" 个")
              .Append("｜楷体（故意的）").Append(keptKaiti).Append(" 处")
              .Append("｜字体为空 ").Append(nullFont).Append(" 处")
              .Append("｜混进楷体 ").Append(kaitiWrong).Append(" 处")
              .Append("｜来路不明 ").Append(strange).Append(" 处\n");
            if (samples.Length > 0) sb.Append(samples);
            fixedCount += fixedToUi + reloadedFixed;
            if (reloadedFixed > 0) sb.Append("   ->  重新挑过通用字体，已把 ").Append(reloadedFixed).Append(" 处刷成新字体\n");
            if (fixedToUi > 0) sb.Append("   ->  已把 ").Append(fixedToUi).Append(" 处改回通用字体\n");
            if (strangers.Count > 0) {
                sb.Append("   这些字的字体不是通用字体（来路不明，多半是别的 mod 塞进来的）：\n");
                for (int i = 0; i < strangers.Count; i++) sb.Append("      · ").Append(strangers[i]).Append('\n');
            }

            // ---------- ②-b 字体分布：分清"只有我们坏了"还是"全都在坏" ----------
            sb.Append("②-b 字体分布（我们的字）：\n");
            int shown = 0;
            foreach (var kv in census) {
                if (shown++ >= 6) { sb.Append("   · …\n"); break; }
                sb.Append("   · ").Append(kv.Key).Append(" × ").Append(kv.Value[0]).Append('\n');
            }
            var outer = new Dictionary<string, int>();
            int outerTotal = 0;
            for (int i = 0; i < texts.Length; i++) {
                var o = texts[i];
                try {
                    if (o == null || o.font == null || IsOurUi(o)) continue;    // 游戏自己的 UI 与别的 mod 的
                    outerTotal++;
                    string fn = FontName(o.font);
                    int c;
                    outer[fn] = outer.TryGetValue(fn, out c) ? c + 1 : 1;
                } catch { }
            }
            sb.Append("   （游戏与别人的字共 ").Append(outerTotal).Append(" 个，字体：");
            int shownOuter = 0;
            foreach (var kv in outer) {
                if (shownOuter++ >= 4) { sb.Append("、…"); break; }
                if (shownOuter > 1) sb.Append("、");
                sb.Append(kv.Key).Append(" × ").Append(kv.Value);
            }
            sb.Append("）\n");
            sb.Append("   ->  我们用的那张在游戏那边也出现，就是全局问题（游戏或别的 mod）；")
              .Append("游戏那边压根没有我们这张，问题就出在我们自己捡字体的那一步\n");

            // ---------- ②-c 建字期拦截：自检按钮看不见的那段时间 ----------
            int guardTotal = _fontGuardNull + _fontGuardKaiti + _fontGuardArial + _fontGuardNoCjk + _fontGuardOther;
            sb.Append("②-c 建字期拦截（建字那一瞬间就不对的次数，自检按钮看不到这一段）：")
              .Append("空 ").Append(_fontGuardNull)
              .Append(" ｜ 楷体 ").Append(_fontGuardKaiti)
              .Append(" ｜ 内置兜底 ").Append(_fontGuardArial)
              .Append(" ｜ 显示不出中文 ").Append(_fontGuardNoCjk)
              .Append(" ｜ 跟前面对不上 ").Append(_fontGuardOther).Append('\n');
            if (guardTotal > 0)
                sb.Append("   有 ").Append(guardTotal).Append(" 次建出来的字一开始就是错的")
                  .Append("（现在屏幕上可能是好的，说明它中途自愈了，这一条就是那时候留下的证据）\n");
            else
                sb.Append("   这一局从建第一个字起就没出过问题\n");

            // ---------- ③ 收尾：重烘字形 + 结论 ----------
            if (fixedCount > 0 || reloadedFixed > 0 || uiFontWasBad) {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                PrewarmFontGlyphs(out int glyphs, out int groups);
                sb.Append("③ 换了字体 ->  字形图集重烘了一轮 字形 ").Append(glyphs).Append(" 个 / ")
                  .Append(groups).Append(" 组（").Append(sw.ElapsedMilliseconds).Append("ms）\n");
            } else {
                sb.Append("③ 没有改动 ->  字形图集不用重烘\n");
            }

            bool ok = !uiFontWasBad && nullFont == 0 && kaitiWrong == 0
                      && covers && strange == 0 && guardTotal == 0 && !UiFontIsBuiltinFallback();
            if (fixedToUi > 0 || reloadedFixed > 0 || uiFontWasBad) ok = (_uiFont != null) && !IsKaitiFont(_uiFont) && !UiFontNotNative();
      // 结论分三档：「原版字体被 mod 换过」既不是"正常"（确实和我们预期不同）也不是"坏了"
      // （界面显示得好好的），单独一档如实说清楚 —— 玩家自己没法解决，别让他以为要找作者。
            bool envOnly = UiFontNotNative() && covers && nullFont == 0 && kaitiWrong == 0
                        && strange == 0 && guardTotal == 0 && _uiFont != null && !IsKaitiFont(_uiFont);
            if (envOnly) ok = true;
            // "一切正常" = 通用字体本来就好、而且一处都不用改；改过就说明这台机器有毛病，值得在屏幕上说一句
            LastFontCheckOk = ok && fixedCount == 0 && !uiFontWasBad;
            sb.Append("结论：").Append(envOnly ? "界面正常，但游戏原版字体被别的 mod 换过了（不影响使用）"
                : ok ? "正常" : "仍有问题（把这份报告发给作者）").Append('\n');
            sb.Append("【字体自检】结束");

            string report = sb.ToString();
            Lg.Info(report);
            DumpFontReport("自检按钮");
            if (ok) return fixedCount > 0 || uiFontWasBad
                ? ("字体检查：发现问题，已修复 " + fixedCount + " 处（报告见日志）")
                : "字体检查：一切正常";
      // 「不是原版字体」这一条要单独说：它不是"坏了"（那张字体照样能显示中文、界面看着是对的），
      // 而是"游戏原版字体被别的 mod 换掉了，我们只好用换进来的那张"。
      // 玩家自己没法解决（得卸掉那个字体 mod），所以文案要说清是环境问题，别写成"仍有问题"。
      if (uiFontWasBad && _uiFont != null && !IsKaitiFont(_uiFont) && covers && nullFont == 0
            && kaitiWrong == 0 && strange == 0 && guardTotal == 0) {
            return "字体检查：界面字体正常，但游戏原版字体被别的 mod 换过了（不影响使用，报告见日志）";
        }
            string why =
                (nullFont + kaitiWrong) > 0 ? ("有 " + (nullFont + kaitiWrong) + " 处字体不对且没修好")
                : (!covers ? ("通用字体画不出中文（缺「" + missing + "」）")
                : (strange > 0 ? ("有 " + strange + " 处用了来路不明的字体")
                : (guardTotal > 0 ? ("建字时有 " + guardTotal + " 次字体是错的，现在已自愈，证据在报告里")
                : (UiFontNotNative() ? ("游戏原版字体被别的 mod 换过了，界面字体是替换后的那张（能用，报告见日志）")
                : "通用字体是内置 Arial 兜底，画不出中文"))));
            return "字体检查：发现问题（" + why + "；mod 目录里的 font_check.txt 发作者）";
        } catch (System.Exception e) {
            LastFontCheckOk = false;
            Lg.Ex("字体自检失败", e);
            return "字体检查：出错（报告见日志）";
        }
    }

    /// <summary>给报告用：这个 Text 挂在哪儿（对象名 + 往上两层路径，超 60 字截断）</summary>
    private static string PathOf(Text t) {
        try {
            string path = t.gameObject != null ? t.gameObject.name : "?";
            var p = t.transform != null ? t.transform.parent : null;
            for (int i = 0; i < 2 && p != null; i++, p = p.parent) path = p.name + "/" + path;
            return path.Length > 60 ? path.Substring(0, 60) : path;
        } catch { return "?"; }
    }
}
