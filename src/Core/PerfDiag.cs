using System;
using UnityEngine;

namespace LevelLibrary;

/// <summary>性能归因诊断：默认关闭，只在 mod 目录下放了 debug_perf.txt（或开了详细日志）时启用。
/// 每 60 秒打一行：全局对象数 + 我们名下的存活数，玩家日志里一眼看出是不是我们涨的。
/// 不常开的原因：Resources.FindObjectsOfTypeAll 要遍历整个对象表，几十万对象时是毫秒级并且分配大数组。
/// 认对象靠 LL_ 前缀；创建计数只增不减，存活数一直涨才是泄漏。</summary>
public static class PerfDiag {
    /// <summary>开关文件名：放在 mod 目录下即启用，删掉即关闭。</summary>
    public const string OnFileName = "debug_perf.txt";
    /// <summary>我们建的纹理 / 精灵统一用这个前缀命名，PerfDiag 靠它数活着的有多少。</summary>
    public const string Prefix = "LL_";

    private static bool _on, _probed;
    private static float _nextAt;
    private const float IntervalSec = 60f;

    // 创建计数：与存活数对照用 —— 创建多而存活不多 = 正常缓存 / 正常回收。
    public static int TexturesMade, SpritesMade;

    /// <summary>给一张我们建的纹理起名；所有创建点都必须调一次（计数也在这里累加）。</summary>
    public static void Tag(Texture2D tex, string what) {
        try { if (tex != null) tex.name = Prefix + what; } catch { }
        TexturesMade++;
    }

    /// <summary>给我们建的 Sprite 起名；Sprite.Create 出来的默认没有名字，数不出来。</summary>
    public static Sprite TagSprite(Sprite spr, string what) {
        if (spr != null) { try { spr.name = Prefix + what; } catch { } }
        SpritesMade++;
        return spr;
    }

    /// <summary>每帧调一次；关着的时候只花一次 bool 判断。</summary>
    public static void Tick() {
        // 每秒探一次开关文件，运行中放上 / 删掉立刻生效。
        if (!_probed || Time.unscaledTime >= _nextAt - IntervalSec + 1f) {
            _probed = true;
            try {
                string p = System.IO.Path.Combine(Main.ModEntry.Path, OnFileName);
                _on = System.IO.File.Exists(p) || PortalUI.LogVerbose;
            } catch { _on = false; }
        }
        if (!_on) return;
        if (_nextAt > 0f && Time.unscaledTime < _nextAt) return;
        _nextAt = Time.unscaledTime + IntervalSec;
        Report();
    }

    /// <summary>立刻取样并打一行（Tick 每 IntervalSec 调一次）。</summary>
    public static void Report() {
        try {
            int texAll = 0, texOurs = 0, sprAll = 0, sprOurs = 0, goAll = 0, clipAll = 0, cvAll = 0, cvOurs = 0, ourUiObjs = 0;
            var texs = Resources.FindObjectsOfTypeAll<Texture2D>();
            if (texs != null) {
                texAll = texs.Length;
                for (int i = 0; i < texs.Length; i++) {
                    var t = texs[i];
                    if (t != null && t.name != null && t.name.StartsWith(Prefix, StringComparison.Ordinal)) texOurs++;
                }
            }
            var sprs = Resources.FindObjectsOfTypeAll<Sprite>();
            if (sprs != null) {
                sprAll = sprs.Length;
                for (int i = 0; i < sprs.Length; i++) {
                    var s = sprs[i];
                    if (s != null && s.name != null && s.name.StartsWith(Prefix, StringComparison.Ordinal)) sprOurs++;
                }
            }
            var gos = Resources.FindObjectsOfTypeAll<GameObject>();
            if (gos != null) goAll = gos.Length;
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            if (clips != null) clipAll = clips.Length;
            // 「我们的界面」按名字认：画布名统一带 LevelLib_ 前缀（MakeCanvas 那套），
            // 再兜上大厅（ArkCanvas）与库列表的嵌套画布（ListRoot）。
            // 别再按 sortingOrder >= 80000 过滤：Canvas.sortingOrder 的值域是 int16，
            // 运行时会折回（80000 实际存成 14464），拿设计值当判据一条都数不到。
            var cvs = Resources.FindObjectsOfTypeAll<Canvas>();
            if (cvs != null) {
                cvAll = cvs.Length;
                for (int i = 0; i < cvs.Length; i++) {
                    var c = cvs[i];
                    if (c == null || c.name == null) continue;
                    bool ours = c.name.StartsWith("LevelLib_", System.StringComparison.Ordinal)
                             || c.name == "ArkCanvas" || c.name == "ListRoot";
                    if (!ours) continue;
                    cvOurs++;
                    try {
                        var trs = c.GetComponentsInChildren<Transform>(true);
                        if (trs != null) ourUiObjs += trs.Length;
                    } catch { }
                }
            }

            long managed = GC.GetTotalMemory(false);
            Lg.Info("【性能归因】"
                + $"全局：GameObject={goAll} Texture={texAll} Sprite={sprAll} AudioClip={clipAll} Canvas={cvAll}"
                + $" ｜我们名下当前存活：纹理={texOurs} 精灵={sprOurs} 画布={cvOurs} 界面内对象={ourUiObjs}"
                + $" ｜累计新建：纹理={TexturesMade} 精灵={SpritesMade} 池行={PortalUI.PoolRowCount}"
                + $" ｜托管堆={managed / 1048576.0:F1}MB 面板={(PortalUI.IsOpen ? "开" : "关")}"
                + "（这一行只涨不降 = 我们泄漏；我们的数不动而全局在涨 = 不是我们）");
        } catch (Exception e) {
            Lg.Info("【性能归因】取样失败: " + e.GetType().Name + " " + e.Message);
        }
    }
}
