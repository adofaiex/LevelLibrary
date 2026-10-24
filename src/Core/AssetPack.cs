using System;
using System.Collections.Generic;
using System.IO;

namespace LevelLibrary;

/// <summary>资源包（`assets\`）的完整性检查与缺失提示：只对清单里的素材说话（玩家数据 bg_static_custom.* / bg_video.mp4 /
/// avatar_custom.png 与不在清单里的可选素材一律安静），一次会话只说一次，给玩家看的那句话里一个路径都不带。
/// 清单必须与 `assets\` 目录逐个对得上，往资源包里加素材后重新生成一次清单，别手写。</summary>

public static class AssetPack {

    /// <summary>资源包应有的素材（相对 `assets\` 的路径，`/` 分隔，一行一个）</summary>
    private const string ManifestText =
// >>>MANIFEST<<<
        "achievement_icon.png\n" +
        "bg_static_default.jpg\n" +
        "bgm_default.mp3\n" +
        "icons/lock.png\n" +
        "marks/Censored.png\n" +
        "marks/G1.png\n" +
        "marks/G10.png\n" +
        "marks/G11.png\n" +
        "marks/G12.png\n" +
        "marks/G13.png\n" +
        "marks/G14.png\n" +
        "marks/G15.png\n" +
        "marks/G16.png\n" +
        "marks/G17.png\n" +
        "marks/G18.png\n" +
        "marks/G19.png\n" +
        "marks/G2.png\n" +
        "marks/G20.png\n" +
        "marks/G3.png\n" +
        "marks/G4.png\n" +
        "marks/G5.png\n" +
        "marks/G6.png\n" +
        "marks/G7.png\n" +
        "marks/G8.png\n" +
        "marks/G9.png\n" +
        "marks/Impossible.png\n" +
        "marks/normal.png\n" +
        "marks/Omamental.png\n" +
        "marks/P1.png\n" +
        "marks/P10.png\n" +
        "marks/P11.png\n" +
        "marks/P12.png\n" +
        "marks/P13.png\n" +
        "marks/P14.png\n" +
        "marks/P15.png\n" +
        "marks/P16.png\n" +
        "marks/P17.png\n" +
        "marks/P18.png\n" +
        "marks/P19.png\n" +
        "marks/P2.png\n" +
        "marks/P20.png\n" +
        "marks/P3.png\n" +
        "marks/P4.png\n" +
        "marks/P5.png\n" +
        "marks/P6.png\n" +
        "marks/P7.png\n" +
        "marks/P8.png\n" +
        "marks/P9.png\n" +
        "marks/Q0.png\n" +
        "marks/Q1.png\n" +
        "marks/Q2.png\n" +
        "marks/Q3.png\n" +
        "marks/Q4.png\n" +
        "marks/Qq.png\n" +
        "marks/strict.png\n" +
        "marks/T2.png\n" +
        "marks/T2H.png\n" +
        "marks/T3.png\n" +
        "marks/T3H.png\n" +
        "marks/T4.png\n" +
        "marks/U1.png\n" +
        "marks/U10.png\n" +
        "marks/U11.png\n" +
        "marks/U11J.png\n" +
        "marks/U12.png\n" +
        "marks/U13.png\n" +
        "marks/U13J.png\n" +
        "marks/U14.png\n" +
        "marks/U15.png\n" +
        "marks/U15J.png\n" +
        "marks/U16.png\n" +
        "marks/U17.png\n" +
        "marks/U18.png\n" +
        "marks/U19.png\n" +
        "marks/U1J.png\n" +
        "marks/U2.png\n" +
        "marks/U20.png\n" +
        "marks/U3.png\n" +
        "marks/U4.png\n" +
        "marks/U5.png\n" +
        "marks/U5J.png\n" +
        "marks/U6.png\n" +
        "marks/U6J.png\n" +
        "marks/U7.png\n" +
        "marks/U7J.png\n" +
        "marks/U8.png\n" +
        "marks/U9.png\n" +
        "marks/U9J.png\n" +
        "marks/Unranked.png\n" +
        "marks/wide.png\n" +
        "pet/bubble.png\n" +
        "pet/D1.mp3\n" +
        "pet/D1.wav\n" +
        "pet/D2.mp3\n" +
        "pet/D2.wav\n" +
        "pet/DSniang1.png\n" +
        "pet/Ya1.mp3\n" +
        "pet/Ya1.wav\n" +
        "pet/Ya2.mp3\n" +
        "pet/Ya2.wav\n" +
        "portrait_default.png\n" +
        "steam_achieve.wav\n" +
        "trophy_icon.png\n";
// >>>MANIFEST-END<<<

    /// <summary>给玩家看的那句话：一个路径都不带，文案不许改字</summary>
    public static string MissingMessage() {
        return "资源包不完整，请在【设置-工具-更新-检查资源包完整性】检查资源包，并下载完整资源包";
    }

    // ---------- 清单 ----------

    private static HashSet<string> _manifest;

    /// <summary>清单（小写 + `/` 分隔：Windows 上大小写不敏感，统一小写比较）</summary>
    private static HashSet<string> Manifest() {
        if (_manifest != null) return _manifest;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ManifestText.Split('\n')) {
            string s = Norm(line);
            if (s.Length > 0) set.Add(s);
        }
        _manifest = set;
        return _manifest;
    }

    public static int ManifestCount() { return Manifest().Count; }

    /// <summary>路径归一：去掉空白、`\` → `/`、去掉开头的 `assets/`、转小写</summary>
    private static string Norm(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim().Replace('\\', '/');
        if (s.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
        return s.ToLowerInvariant();
    }

    /// <summary>绝对路径 → 清单里的相对路径（不在 mod 的 `assets` 下就返回 null）</summary>
    private static string RelOf(string absPath) {
        try {
            if (string.IsNullOrEmpty(absPath)) return null;
            string root = Path.Combine(Main.ModEntry.Path, "assets");
            string full = Path.GetFullPath(absPath);
            string r = Path.GetFullPath(root);
            if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return null;
            return Norm(full.Substring(r.Length).TrimStart('\\', '/'));
        } catch { return null; }
    }

    // ---------- 缺素材的两条入口 ----------

    /// <summary>按绝对路径报（`LoadAssetSprite` 那条总入口调，一处覆盖图标 / 头像 / 背景等一堆）</summary>
    public static void NoteMissingPath(string absPath) {
        try {
            string rel = RelOf(absPath);
            if (rel == null || rel.Length == 0) return;
            if (!Manifest().Contains(rel)) return;   // 玩家数据 / 可选素材 → 安静
            NoteOnce(rel);
        } catch { }
    }

    /// <summary>按清单里的相对路径报（宠物 / 难度图 / 音效那几处直接知道相对路径）</summary>
    public static void NoteMissing(string rel) {
        try {
            string n = Norm(rel);
            if (n.Length == 0 || !Manifest().Contains(n)) return;
            NoteOnce(n);
        } catch { }
    }

    /// <summary>本次会话第一次缺素材时：一行日志 + 一条提示，之后一律安静</summary>
    private static void NoteOnce(string rel) {
        if (_reported) return;
        _reported = true;
        try {
            // 日志里留一个文件名（作者排查用）；给玩家看的那句里一个路径都没有
            Lg.Info("资源包不完整：缺 " + rel + "（同类缺失不再逐条打印）→ " + MissingMessage());
        } catch { }
        try { PortalUI.ShowToast(MissingMessage(), AssetToastSeconds); } catch { }
    }

    private static bool _reported;
    /// <summary>提示时长：默认轻提示 2.5 秒太短，玩家要照着它去设置里修</summary>
    private const float AssetToastSeconds = 9f;

    /// <summary>本次会话是否已经提示过（给别的模块问一句）</summary>
    public static bool Reported { get { return _reported; } }

    // ---------- 设置页那个按钮 ----------

    /// <summary>「检查资源包完整性」：数一遍缺多少，一个不缺就直接报"完整"、什么都不动。
    /// 有缺的 → 作废本地资源包版本记录（不作废的话 updater 会以为"已经装过这一版"，永远补不齐），
    /// 再顺手起一次更新检查去下完整包，然后返回 MissingMessage()。
    /// 不做任何破坏性动作：素材一个都不删，只是不作数自己那条版本记录。</summary>
    public static string CheckNow() {
        try {
            string root = Path.Combine(Main.ModEntry.Path, "assets");
            int total = 0, miss = 0;
            string firstMissing = null;
            foreach (var rel in Manifest()) {
                total++;
                string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) continue;
                miss++;
                if (firstMissing == null) firstMissing = rel;
            }
            if (miss == 0) {
                Lg.Info($"[LevelLib] 资源包完整性检查：完整（{total} 个素材全在）");
                return $"资源包完整 共 {total} 个素材";
            }
            Lg.Info($"[LevelLib] 资源包完整性检查：不全（共 {total} 个，缺 {miss} 个，第一个缺 {firstMissing}）→ 作废版本记录并重新下载");
            Updater.ForgetLocalAssetVersion();
            try { Updater.CheckNow(true); } catch { }
            return MissingMessage();
        } catch (Exception e) {
            Lg.Ex("资源包完整性检查失败", e);
            return "检查资源包时出错了，请到日志里看详情";
        }
    }
}
