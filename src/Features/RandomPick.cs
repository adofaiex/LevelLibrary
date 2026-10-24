using System;
using System.Collections.Generic;
using System.IO;

namespace LevelLibrary;

/// <summary>随机谱面：当日已经抽到过的，降低再次抽中的概率，每天刷新。
/// 加权随机而不是排除：今天没抽过权重 1，抽到过 n 次权重 Decay^n（永远不为 0）。
/// 只统计随机功能抽中的次数；手动点开 / 快捷键 / 谱面库进关都不计数。
/// 池子里一个都没抽过时权重全为 1，行为与均匀随机完全一致。</summary>
public static class RandomPick {

    // 存 Mods\LevelLibrary\random_today.txt（玩家数据，打包时排除）。
    // 首行是日期 yyyy-MM-dd（与 playtime.txt 同口径），其后每行「次数<TAB>相对文件夹」。
    // 用相对路径：备份位置换过也还认得（与 marks / favorites 同款）。
    private const string FileName = "random_today.txt";
    /// <summary>每被抽中一次就再乘一遍：0.35，一次降到 35%，不为 0 所以迟早还会抽到。</summary>
    public const float Decay = 0.35f;
    /// <summary>权重最低封到 Decay^12（≈ 3.4e-6）：再低没意义，也免得连乘把 float 算炸。</summary>
    private const int MaxCount = 12;

    /// <summary>今天各谱面被随机抽中的次数；键 = 相对文件夹路径，大小写不敏感。</summary>
    private static readonly Dictionary<string, int> _count = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>这份计数属于哪一天；空 = 还没读盘。</summary>
    private static string _day = "";
    private static string _path;

    // ---------- 读盘 / 跨天 ----------

    /// <summary>启动时读一次，由 Main.OnToggle 与其它数据文件一起调用。</summary>
    public static void Load(string modPath) {
        try {
            _path = Path.Combine(modPath, FileName);
            _count.Clear();
            _day = "";
            if (!File.Exists(_path)) return;   // 第一次玩，空着即可
            var lines = File.ReadAllLines(_path);
            if (lines.Length == 0) return;
            _day = (lines[0] ?? "").Trim();
            for (int i = 1; i < lines.Length; i++) {
                var parts = lines[i].Split('\t');
                if (parts.Length < 2) continue;
                int n;
                if (!int.TryParse(parts[0].Trim(), out n) || n <= 0) continue;
                string key = LevelScanner.ToRel(parts[1].Trim()).Trim();
                if (key.Length > 0) _count[key] = n;
            }
            Rollover();   // 开局已是新的一天就当场清掉，省得第一抽还按昨天的算
            Lg.Info($"[LevelLib] 随机谱面：今日已抽 {_count.Count} 个谱面（记录日 {_day}）");
        } catch (Exception e) {
            _count.Clear();
            LogEx("随机谱面：读今日记录失败（忽略，按没抽过处理）", e);
        }
    }

    /// <summary>跨天就清空；读盘后与每次抽取前都要调，游戏一直开着跨过零点也算每天刷新。</summary>
    private static void Rollover() {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_day == today) return;
        _count.Clear();
        _day = today;
        Save();
        Lg.Info("随机谱面：新的一天，今日抽取记录已刷新");
    }

    private static void Save() {
        if (string.IsNullOrEmpty(_path)) return;
        try {
            var lines = new List<string>();
            lines.Add(_day);
            foreach (var kv in _count) lines.Add(kv.Value + "\t" + kv.Key);
            File.WriteAllLines(_path, lines.ToArray());
        } catch (Exception e) {
            LogEx("随机谱面：写今日记录失败（不影响这一次抽取）", e);
        }
    }

    /// <summary>日志必须吞异常：离线验证台里 Main.ModEntry 是 null，
    /// 日志自己抛 NRE 会把整段逻辑带崩。</summary>
    private static void LogEx(string msg, Exception e) {
        try { Lg.Ex(msg, e); } catch { }
    }

    // ---------- 权重 ----------

    /// <summary>当天被抽中 pickedToday 次时的权重（0 次 = 1，一次 = 0.35 …）。
    /// 纯函数：不读盘、不碰静态字段。</summary>
    public static float WeightOf(int pickedToday) {
        int n = pickedToday < 0 ? 0 : (pickedToday > MaxCount ? MaxCount : pickedToday);
        return (float)Math.Pow(Decay, n);
    }

    /// <summary>某个谱面今天的抽取次数，没有就是 0。</summary>
    public static int CountOf(string folderPath) {
        try {
            string key = LevelScanner.ToRel(folderPath);
            if (string.IsNullOrEmpty(key)) return 0;
            int n;
            return _count.TryGetValue(key.Trim(), out n) ? n : 0;
        } catch { return 0; }
    }

    // ---------- 抽 ----------

    /// <summary>按当日次数加权抽一个（游戏里走这条）。
    /// 全 mod 只有这里取 Unity 随机数；带 roll 的重载是给离线验证台用的。</summary>
    public static LevelEntry Pick(List<LevelEntry> pool) {
        return Pick(pool, UnityEngine.Random.value);
    }

    /// <summary>同上，但随机数由外面给（roll ∈ [0,1)）：离线验证台在没有 Unity 引擎的控制台里跑，
    /// UnityEngine.Random.value 在无引擎进程里会炸，所以把选下标这一步做成可注入的。</summary>
    public static LevelEntry Pick(List<LevelEntry> pool, float roll) {
        if (pool == null || pool.Count == 0) return null;
        try {
            Rollover();
            var w = new float[pool.Count];
            float total = 0f;
            for (int i = 0; i < pool.Count; i++) {
                w[i] = WeightOf(CountOf(pool[i] != null ? pool[i].FolderPath : null));
                total += w[i];
            }
            int idx = PickIndex(w, total, roll);
            var entry = pool[idx];
            string key = entry != null ? LevelScanner.ToRel(entry.FolderPath) : null;
            int before = 0;
            if (!string.IsNullOrEmpty(key)) {
                key = key.Trim();
                _count.TryGetValue(key, out before);
                _count[key] = before + 1;
                Save();
            }
            int pickedToday = 0;
            foreach (var kv in _count) if (kv.Value > 0) pickedToday++;
            Lg.Info($"[LevelLib] 随机谱面：抽中 {(entry != null ? entry.Title : "(空)")}"
                + $"（今天这个已经抽过 {before} 次 → 本次权重 {WeightOf(before):F3}｜今日共抽过 {pickedToday} 个／池子 {pool.Count}）");
            return entry;
        } catch (Exception e) {
            LogEx("随机谱面：加权抽取失败（退回均匀随机）", e);
            int fallback = (int)(UnityEngine.Mathf.Clamp01(roll) * pool.Count);
            return pool[fallback >= pool.Count ? pool.Count - 1 : fallback];
        }
    }

    /// <summary>按权重挑下标，纯函数：不读盘、不碰静态状态。
    /// roll ∈ [0,1) 乘总权重后落在哪个区间就取哪个（权重越大区间越宽 = 概率越高）。</summary>
    public static int PickIndex(float[] weights, float total, float roll) {
        if (weights == null || weights.Length == 0) return 0;
        if (total <= 0f) {
            // 理论上到不了（权重恒 > 0）；兜底均匀取一个，别让按键没反应
            int n = (int)(Mathf01(roll) * weights.Length);
            return n >= weights.Length ? weights.Length - 1 : n;
        }
        float r = Mathf01(roll) * total;
        float acc = 0f;
        for (int i = 0; i < weights.Length; i++) {
            acc += weights[i];
            if (r < acc) return i;
        }
        return weights.Length - 1;   // 浮点误差兜底
    }

    /// <summary>把 roll 钳到 [0,1)：外面给了 1.0 也不会越界。纯算术、不依赖 Unity。</summary>
    private static float Mathf01(float roll) {
        if (roll < 0f) return 0f;
        if (roll >= 1f) return 0.99999994f;
        return roll;
    }
}
