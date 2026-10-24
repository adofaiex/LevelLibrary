// LevelLibrary 自动评级引擎：复刻 AutoRating 的 globalRating→PGU 判定
// 纯 C# 解析 .adofai JSON，算星级难度并映射为 P/G/U 标记，不依赖游戏内对象 / MathNet。
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LevelLibrary;

public class ARTile {
    public int LinearFloor;
    public int OriginalFloor;
    public double OriginalAngle;
    public double PauseDuration;
    public double MarginScale = 100.0;
    public bool IsTwirl;
    public bool IsRealBpmChange;
    public int NewPlanetCount;
    public double PlanetBpm;
    public double RealBpm;
    public double TileTime;
    public double HitWindowP;
    public double HitWindowM;
    public bool IsMidspin() => Math.Abs(OriginalAngle - 999.0) < 0.001;
}

public class ARLevel {
    public List<ARTile> Tiles = new List<ARTile>();
    public long TotalLength;
    public double StartBpm;
}

/// <summary>自动评级：解析谱面 JSON → 星级难度 → P/G/U 标记</summary>
public static class AutoRater {
    /// <summary>入口：读 .adofai 文本，算全局星级难度，返回 PGU 标记（如 "P5"/"G12"/"U15"；超范围返回 ""）。
    /// debugName 传谱面文件名，出错时日志里能看出是哪个文件。</summary>
    public static string RateChart(string jsonText, out double starRating, double pitch = 100.0, string debugName = null) {
        starRating = 0;
        try {
            var level = ConvertJsonToLinear(jsonText, pitch);
            return ProcessRatings(level, out starRating);
        } catch (System.Exception e) {
            // 不能吞异常：界面上只会说"暂不符合自动评级"，看不出原因，所以把异常类型、消息、文件名写进日志
            Lg.Info($"[LevelLib] 自动评级失败{(string.IsNullOrEmpty(debugName) ? "" : "（" + debugName + "）")}"
                + ": " + e.GetType().Name + ": " + e.Message);
            return "";
        }
    }

    // ---------- 解析 .adofai JSON 为线性铺面块 ----------
    private static readonly Dictionary<char, Tuple<double, bool>> PathMeta = new Dictionary<char, Tuple<double, bool>> {
        { 'R', Tuple.Create(0.0, false) }, { 'p', Tuple.Create(15.0, false) }, { 'J', Tuple.Create(30.0, false) },
        { 'E', Tuple.Create(45.0, false) }, { 'T', Tuple.Create(60.0, false) }, { 'o', Tuple.Create(75.0, false) },
        { 'U', Tuple.Create(90.0, false) }, { 'q', Tuple.Create(105.0, false) }, { 'G', Tuple.Create(120.0, false) },
        { 'Q', Tuple.Create(135.0, false) }, { 'H', Tuple.Create(150.0, false) }, { 'W', Tuple.Create(165.0, false) },
        { 'L', Tuple.Create(180.0, false) }, { 'x', Tuple.Create(195.0, false) }, { 'N', Tuple.Create(210.0, false) },
        { 'Z', Tuple.Create(225.0, false) }, { 'F', Tuple.Create(240.0, false) }, { 'V', Tuple.Create(255.0, false) },
        { 'D', Tuple.Create(270.0, false) }, { 'Y', Tuple.Create(285.0, false) }, { 'B', Tuple.Create(300.0, false) },
        { 'C', Tuple.Create(315.0, false) }, { 'M', Tuple.Create(330.0, false) }, { 'A', Tuple.Create(345.0, false) },
        { '5', Tuple.Create(108.0, true) }, { '6', Tuple.Create(252.0, true) }, { '7', Tuple.Create(128.57142857142858, true) },
        { '8', Tuple.Create(231.42857142857142, true) }, { 't', Tuple.Create(60.0, true) }, { 'h', Tuple.Create(120.0, true) },
        { 'j', Tuple.Create(240.0, true) }, { 'y', Tuple.Create(300.0, true) }, { '!', Tuple.Create(999.0, true) },
    };

    private static ARLevel ConvertJsonToLinear(string jsonText, double pitch) {
        var val = JObject.Parse(jsonText);
        // bpm 容错：老谱面在 settings.bpm，但有些文件没有 settings（或被精简过），取不到就退回默认 100，
        // 只要不抛异常，后面的星级计算仍能给出参考值。
        double bpm = 100.0;
        var settings = val["settings"] as JObject;
        if (settings != null && settings["bpm"] != null) {
            double.TryParse(settings["bpm"].ToString(), out bpm);
            if (bpm <= 0.0) bpm = 100.0;
        } else {
            Lg.Info("自动评级：谱面没有 settings.bpm，按 100 处理");
        }
        var list = new List<ARTile> { new ARTile { LinearFloor = 0, OriginalFloor = 0, OriginalAngle = 0.0 } };

        if (val["angleData"] != null) {
            var val2 = (JArray)val["angleData"];
            for (int i = 0; i < val2.Count; i++) {
                double a = val2[i].Value<double>();
                list.Add(new ARTile { LinearFloor = i + 1, OriginalFloor = i + 1, PauseDuration = 0.0, OriginalAngle = Math.Abs(a - 999.0) < 0.001 ? 999.0 : WrapTo180(a) });
            }
        } else {
            string text = val["pathData"].ToString();
            double cur = 0.0;
            for (int j = 0; j < text.Length; j++) {
                var tuple = PathMeta[text[j]];
                if (Math.Abs(tuple.Item1 - 999.0) < 0.001) {
                    list.Add(new ARTile { LinearFloor = j + 1, OriginalFloor = j + 1, OriginalAngle = 999.0 });
                } else if (tuple.Item2) {
                    cur = GeneralizeAngle(cur + 180.0 - tuple.Item1);
                    list.Add(new ARTile { LinearFloor = j + 1, OriginalFloor = j + 1, OriginalAngle = WrapTo180(cur) });
                } else {
                    cur = tuple.Item1;
                    list.Add(new ARTile { LinearFloor = j + 1, OriginalFloor = j + 1, OriginalAngle = WrapTo180(cur) });
                }
            }
        }

        // actions 容错：没有变速/停顿的谱面不会有 actions 字段，直接当数组用会抛异常
        var actions = val["actions"] as JArray;
        if (actions == null) actions = new JArray();
        double curBpm = bpm;
        double marginScale = 100.0;
        for (int k = 0; k < actions.Count; k++) {
            var act = (JObject)actions[k];
            string ev = act["eventType"]?.ToString();
            int floor = act["floor"].Value<int>();
            if (list.Count <= floor) continue;
            var tile = list[floor];
            if (ev == "Twirl") tile.IsTwirl = true;
            if (ev == "SetSpeed") {
                if (act["speedType"] == null) {
                    curBpm = tile.PlanetBpm = double.Parse(act["beatsPerMinute"].ToString());
                } else if (act["speedType"].ToString() == "Bpm") {
                    curBpm = tile.PlanetBpm = double.Parse(act["beatsPerMinute"].ToString());
                } else {
                    curBpm = tile.PlanetBpm = curBpm * double.Parse(act["bpmMultiplier"].ToString());
                }
            }
            if (ev == "ScaleMargin" && act["scale"] != null) marginScale = act["scale"].Value<double>();
            tile.MarginScale = marginScale;
            if (ev == "Pause") tile.PauseDuration = act["duration"].Value<double>();
            if (ev == "MultiPlanet") {
                string ptext = act["planets"]?.ToString() ?? "";
                if (ptext.Length > 0) {
                    if (char.IsDigit(ptext[0])) tile.NewPlanetCount = int.Parse(ptext);
                    else tile.NewPlanetCount = ptext == "ThreePlanets" ? 3 : 2;
                }
            }
            if (ev == "Hold") tile.PauseDuration = act["duration"].Value<int>() * 2;
            if (ev == "FreeRoam") tile.PauseDuration = act["duration"].Value<double>() - 1.0;
        }

        // midspin 处理
        for (int l = 1; l < list.Count; l++) {
            list[l].LinearFloor = l;
            if (!list[l].IsMidspin()) continue;
            var mid = list[l];
            var prev = list[l - 1];
            prev.IsTwirl = !prev.IsTwirl;
            if (mid.IsTwirl) prev.IsTwirl = !prev.IsTwirl;
            if (mid.PlanetBpm != 0.0) prev.PlanetBpm = mid.PlanetBpm;
            double baseAng = WrapTo180(prev.OriginalAngle + 90.0);
            for (int m = l + 1; m < list.Count; m++) {
                var t = list[m];
                if (!t.IsMidspin()) {
                    double d = (baseAng - t.OriginalAngle) * 2.0;
                    t.OriginalAngle = WrapTo180(t.OriginalAngle + d);
                }
            }
            list.RemoveAt(l);
            l--;
        }
        // twirl 处理
        for (int n = 1; n < list.Count; n++) {
            var t5 = list[n];
            if (!t5.IsTwirl) continue;
            for (int num9 = n + 1; num9 < list.Count; num9++) {
                var t6 = list[num9];
                if (!t6.IsMidspin()) {
                    double d = (t5.OriginalAngle - t6.OriginalAngle) * 2.0;
                    t6.OriginalAngle = WrapTo180(t6.OriginalAngle + d);
                }
            }
        }
        // planet 全局角度
        bool flag = false;
        int num11 = 0;
        for (int num12 = 1; num12 < list.Count; num12++) {
            var t7 = list[num12];
            if (!t7.IsMidspin()) {
                t7.OriginalAngle = WrapTo180(t7.OriginalAngle + (double)(60 * num11));
                if (t7.NewPlanetCount > 0) flag = t7.NewPlanetCount == 3;
                if (flag) num11++;
            }
        }

        var level = new ARLevel();
        var tiles = new List<ARTile> { new ARTile { LinearFloor = 0, OriginalFloor = 0, OriginalAngle = 0.0 } };
        double rbpm = bpm;
        double lastBpm = bpm;
        for (int num15 = 1; num15 < list.Count - 1; num15++) {
            var t8 = list[num15];
            var t9 = list[num15 + 1];
            double angDiff = GetAngleDiff(t8.OriginalAngle, t9.OriginalAngle) + 180.0;
            if (Math.Abs(angDiff) < 0.001) angDiff = 360.0;
            if (t8.PlanetBpm != 0.0) rbpm = t8.PlanetBpm;
            double pitchF = pitch / 100.0;
            double realBpm = rbpm * (180.0 / (angDiff + t8.PauseDuration * 180.0)) * pitchF;
            double hitWindowP = 45.0 / rbpm * 333.0;
            double hitWindowM = 60.0 / rbpm * 333.0;
            var nt = new ARTile {
                LinearFloor = num15, OriginalFloor = t8.OriginalFloor, OriginalAngle = t8.OriginalAngle,
                PlanetBpm = rbpm, RealBpm = realBpm, HitWindowP = hitWindowP, HitWindowM = hitWindowM,
            };
            if (Math.Abs(lastBpm - realBpm) > 0.0001) { lastBpm = realBpm; nt.IsRealBpmChange = true; }
            tiles.Add(nt);
        }
        var lastTile = list[list.Count - 1];
        tiles.Add(new ARTile { LinearFloor = lastTile.LinearFloor, OriginalFloor = lastTile.OriginalFloor, OriginalAngle = lastTile.OriginalAngle, PlanetBpm = rbpm, RealBpm = rbpm });
        level.StartBpm = bpm;
        level.Tiles = tiles;

        double time = 0.0;
        for (int num21 = 1; num21 < tiles.Count; num21++) {
            tiles[num21].TileTime = time;
            time += 60000.0 / tiles[num21].RealBpm;
        }
        level.TotalLength = (long)(tiles[tiles.Count - 1].TileTime / 1000.0);
        return level;
    }

    // ---------- 评级核心（复刻 RaterMain） ----------
    private static string ProcessRatings(ARLevel adofaiLevel, out double globalRating) {
        var tiles = adofaiLevel.Tiles;
        var groups = GroupTiles(tiles);
        var groupsM = GroupTilesM(tiles);
        var speedRatings = new List<double>();
        for (int i = 0; i < groups.Count - 1; i++) speedRatings.Add(CalculateSingleSpeedRating(groups[i], groups[i + 1]));
        var techRatings = new List<double>();
        for (int j = 0; j < groupsM.Count - 1; j++) techRatings.Add(GetTechSingleRating(groupsM[j], groupsM[j + 1]));

        double alpha = 1.0 + Math.Pow(5.0 + CubeRoot(adofaiLevel.TotalLength - 45), 0.75);
        double scale = Math.Min(1.0, 1.0 + 0.02 * (double)(adofaiLevel.TotalLength - 45));
        double speedR = CalculateWeightedAverage(speedRatings, alpha);
        double techR0 = CalculateWeightedAverage(techRatings, alpha);
        double techR = 150.0 * Math.Pow(techR0 - 1.0, 0.75);
        var data = DetectAndPenalizeRepetitions(CalculateRhythmMultipliers(groups, speedRatings), 16);
        double rhythmR = 30.0 * Math.Pow(speedR, 0.25) * (CalculateWeightedAverage(data, alpha) - 1.0);
        double staminaR = Math.Pow(1.5, alpha) / 10.5 * Math.Pow(tiles.LongCount() / (double)adofaiLevel.TotalLength, 0.51);
        var sorted = new List<double> { rhythmR, speedR, staminaR }.OrderByDescending(d => d).ToList();
        double result = scale * (sorted[0] * 0.92 + sorted[1] * 1.0 + sorted[2] * 1.08) / 3.0;
        globalRating = result;
        if (result <= 0.0) return "";
        string PGU = "";
        // 56 段阈值表，由 TUF 真值标定（359 个独立谱面，L1 + 单调约束的精确 DP）。
        // 切点严格递增、档位单调不减，所以「星级越大档位越高」是结构性保证。
        // 只动这一串常数，上面那几个权重（0.92 / 1.0 / 1.08 与四个分项）一个字都不动。
        if (result > 0.0) PGU = "P1";
        if (result > 1.208886272335652) PGU = "P1";
        if (result > 1.2792150409999894) PGU = "P1";
        if (result > 1.2903174686279648) PGU = "P1";
        if (result > 1.2919260058026725) PGU = "P1";
        if (result > 1.2927949039840612) PGU = "P1";
        if (result > 1.298656586827375) PGU = "P1";
        if (result > 1.3278194121580014) PGU = "P1";
        if (result > 1.4059834201832069) PGU = "P2";
        if (result > 1.5228447316328895) PGU = "P2";
        if (result > 1.595874487727031) PGU = "P2";
        if (result > 1.6137148849576484) PGU = "P2";
        if (result > 1.6458757520705616) PGU = "P2";
        if (result > 1.6986776746892716) PGU = "P2";
        if (result > 1.7335363432223574) PGU = "P3";
        if (result > 1.8086028807582406) PGU = "P3";
        if (result > 1.9147831060399683) PGU = "P3";
        if (result > 1.95505734595397) PGU = "P4";
        if (result > 1.9613811431020105) PGU = "P4";
        if (result > 1.9753372909266287) PGU = "P4";
        if (result > 2.0095365909372123) PGU = "P4";
        if (result > 2.0576205830705465) PGU = "P4";
        if (result > 2.094687015329054) PGU = "P4";
        if (result > 2.126090338792179) PGU = "P4";
        if (result > 2.1610919031654943) PGU = "P4";
        if (result > 2.1988405091218457) PGU = "P6";
        if (result > 2.236232461716087) PGU = "P6";
        if (result > 2.333223477507984) PGU = "P6";
        if (result > 2.4575399846335033) PGU = "P6";
        if (result > 2.5213210449024777) PGU = "P6";
        if (result > 2.5481390177735537) PGU = "P6";
        if (result > 2.5665421452122867) PGU = "P6";
        if (result > 2.6222791426879777) PGU = "P6";
        if (result > 2.6801055427759524) PGU = "P6";
        if (result > 3.4218837147606918) PGU = "P11";
        if (result > 4.854699314333277) PGU = "P14";
        if (result > 5.218806681554939) PGU = "P16";
        if (result > 5.401524308946026) PGU = "P17";
        if (result > 5.825859883719584) PGU = "P19";
        if (result > 5.949512329336269) PGU = "P20";
        if (result > 7.141271138903687) PGU = "G3";
        if (result > 7.169341691232454) PGU = "G5";
        if (result > 7.788507313982771) PGU = "G9";
        if (result > 8.516410857997291) PGU = "G19";
        if (result > 9.05861461096831) PGU = "U1";
        if (result > 9.52760672085519) PGU = "U2";
        if (result > 9.620912971961648) PGU = "U3";
        if (result > 9.808260902157055) PGU = "U4";
        if (result > 10.427932618946247) PGU = "U6";
        if (result > 10.981886254903898) PGU = "U7";
        if (result > 11.516002362382483) PGU = "U8";
        if (result > 12.190960889308498) PGU = "U9";
        if (result > 12.221671504955971) PGU = "U11";
        if (result > 12.612832459966917) PGU = "U12";
        if (result > 15.24789869623795) PGU = "U14";
        if (result > 15.663165857944676) PGU = "U15";
        // 超出标定范围的坏数据（时长字段坏掉那种）会算出无意义的量级，一律不评级。
        if (result > 17.5) PGU = "?";
        return PGU;
    }

    public static double CubeRoot(double x) {
        if (x < 0.0) return -Math.Pow(-x, 1.0 / 3.0);
        return Math.Pow(x, 1.0 / 3.0);
    }

    // ---------- 分组 / 各分项 ----------
    private static List<List<ARTile>> GroupTiles(List<ARTile> tiles) {
        // 原算法：i 以步长 = 上一分组长度递增
        var outList = new List<List<ARTile>>();
        int si = 0;
        while (si < tiles.Count) {
            var tile = tiles[si];
            double limit = tile.TileTime + Math.Max(tile.HitWindowP, 35.0);
            var g = new List<ARTile> { tile };
            for (int j = si + 1; j < tiles.Count && tiles[j].TileTime <= limit; j++) g.Add(tiles[j]);
            outList.Add(g);
            si += g.Count;
        }
        return outList;
    }

    private static double CalculateSingleSpeedRating(List<ARTile> group, List<ARTile> nextGroup) {
        if (group.Count == 0 || nextGroup.Count == 0) return 0.0;
        double t0 = group[0].TileTime;
        double t1 = group.Last().TileTime;
        double t2 = nextGroup[0].TileTime;
        double dur = t1 - t0;
        double span = t2 - t0;
        if (span <= 0.0) span = 1000.0;
        double mult = 1.0;
        if (group.Count > 1) {
            double r = dur / span;
            mult = (r >= 0.25 && r < 0.5) ? Math.Pow(r + 0.5, 0.5) : (r >= 0.125 && r < 0.25) ? Math.Pow(r + 0.5, 0.25) : Math.Pow(r + 0.5, 0.1);
        }
        return 520.0 / (span / Math.Sqrt(group.Count)) * mult;
    }

    private static double CalculateWeightedAverage(List<double> data, double alpha) {
        if (data.Count == 0) return 0.0;
        double avg = data.Average();
        double num = 0.0, den = 0.0;
        foreach (double d in data) {
            double w = Math.Pow(d / avg, alpha);
            num += d * w;
            den += w;
        }
        return den != 0.0 ? num / den : 0.0;
    }

    private static List<double> CalculateRhythmMultipliers(List<List<ARTile>> groups, List<double> speedRatings) {
        var list = new List<double>();
        for (int i = 1; i < groups.Count - 1; i++) {
            double bpmMult = GetBpmMultiplier(i < speedRatings.Count ? speedRatings[i] : 0);
            double interval = GetGroupInterval(groups[i - 1], groups[i], groups[i + 1]);
            double item = 1.0;
            if (interval <= 0.95 || interval >= 1.05) item = bpmMult * CalculateExpectedFunctionValue(interval);
            list.Add(item);
        }
        return list;
    }

    private static double CalculateExpectedFunctionValue(double ratio) {
        ratio = Math.Min(1.0, ratio);
        double[] x = { 0.0, 0.125, 0.2, 0.252, 0.28, 0.32, 0.42, 0.5, 0.58, 0.68, 0.72, 0.748, 0.8, 0.875, 1.0 };
        double[] y = { 1.0, 1.065, 1.2, 1.15, 1.25, 1.15, 1.1, 1.0, 1.1, 1.15, 1.25, 1.15, 1.2, 1.065, 1.0 };
        return AkimaInterpolate(x, y, ratio);
    }

    // MathNet CubicSpline.InterpolateAkimaSorted 的复刻（Akima 五点法 + 边界外推）
    private static double AkimaInterpolate(double[] x, double[] y, double xi) {
        int n = x.Length;
        if (n < 2) return y[0];
        if (xi <= x[0]) return y[0] + ((y[1] - y[0]) / (x[1] - x[0])) * (xi - x[0]);
        if (xi >= x[n - 1]) return y[n - 1] + ((y[n - 1] - y[n - 2]) / (x[n - 1] - x[n - 2])) * (xi - x[n - 1]);
        // 实弦率 s_0..s_{n-2}
        double[] s = new double[n - 1];
        for (int i = 0; i < n - 1; i++) s[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
        // 扩展到 s[-2..n]（外推边界）
        double[] S = new double[n + 3];
        int off = 2;
        S[off + 0] = s[0];
        if (n - 1 >= 2) S[off + 1] = s[1];
        for (int i = 0; i < n - 1; i++) S[off + i] = s[i];
        S[off - 1] = 2.0 * s[0] - (n - 1 >= 2 ? s[1] : s[0]);
        S[off - 2] = 2.0 * S[off - 1] - s[0];
        S[off + (n - 1)] = 2.0 * s[n - 2] - (n - 3 >= 0 ? s[n - 3] : s[n - 2]);
        S[off + n] = 2.0 * S[off + (n - 1)] - s[n - 2];
        // 各点斜率 m_i（Akima 五点加权）
        double[] m = new double[n];
        for (int i = 0; i < n; i++) {
            double sm2 = S[off + (i - 2)];
            double sm1 = S[off + (i - 1)];
            double s0 = S[off + i];
            double sp1 = S[off + (i + 1)];
            double w1 = Math.Abs(sp1 - s0);
            double w2 = Math.Abs(sm1 - sm2);
            m[i] = (w1 + w2 < 1e-12) ? (sm1 + s0) / 2.0 : (w1 * sm1 + w2 * s0) / (w1 + w2);
        }
        // 定位区间 + 立方 Hermite
        int idx = Array.BinarySearch(x, xi);
        if (idx >= 0) return y[idx];
        idx = ~idx - 1;
        double h = x[idx + 1] - x[idx];
        double t = (xi - x[idx]) / h;
        double m0 = m[idx], m1 = m[idx + 1];
        double h00 = (1 + 2 * t) * (1 - t) * (1 - t);
        double h10 = t * (1 - t) * (1 - t);
        double h01 = t * t * (3 - 2 * t);
        double h11 = t * t * (t - 1);
        return h00 * y[idx] + h10 * h * m0 + h01 * y[idx + 1] + h11 * h * m1;
    }

    private static List<double> DetectAndPenalizeRepetitions(List<double> sequence, int maxWindowSize) {
        var list = new List<double>(sequence);
        int count = list.Count;
        double num = 1.0;
        for (int i = 2; i <= maxWindowSize && i <= count / 2; i++) {
            for (int j = 0; j <= count - 2 * i; j++) {
                int start = j + i;
                if (CompareGroups(list, j, start, i)) {
                    num *= 0.75;
                    for (int k = j; k < j + i; k++) list[k] = (list[k] - 1.0) * num + 1.0;
                    j += i - 1;
                }
            }
        }
        return list;
    }

    private static bool CompareGroups(List<double> a, int s1, int s2, int w) {
        for (int i = 0; i < w; i++) if (Math.Round(Math.Abs(a[s1 + i] - a[s2 + i]), 2) > 0.01) return false;
        return true;
    }

    private static double GetBpmMultiplier(double rating) {
        if (rating <= 2.5) return 1.0;
        if (rating <= 5.0) return 1.05;
        if (rating <= 8.0) return 1.1;
        if (rating <= 10.0) return 1.2;
        if (rating <= 12.0) return 1.25;
        if (rating <= 15.0) return 1.2;
        if (rating <= 20.0) return 1.15;
        if (rating <= 25.0) return 1.1;
        return 1.05;
    }

    private static List<List<ARTile>> GroupTilesM(List<ARTile> tiles) {
        var outList = new List<List<ARTile>>();
        int num = 0;
        while (num < tiles.Count) {
            var tile = tiles[num];
            if (tile.TileTime == 0.0 && tile.HitWindowM == 0.0) { num++; continue; }
            double limit = tile.TileTime + Math.Max(tile.HitWindowM, 50.0);
            var g = new List<ARTile> { tile };
            int i;
            for (i = num + 1; i < tiles.Count; i++) {
                if (!(tiles[i].TileTime <= limit)) break;
                g.Add(tiles[i]);
            }
            outList.Add(g);
            num = i;
        }
        return outList;
    }

    private static double GetTechSingleRating(List<ARTile> cur, List<ARTile> next) {
        int c = cur.Count, c2 = next.Count;
        int delta = c2 - c;
        double ratio = (double)c2 / (double)c;
        if (delta >= 6 && ratio >= 3.0) return 1.3 + 0.1 * (1.5 * Math.Pow(ratio, 0.5) - 1.0);
        if (delta >= 3 && ratio > 2.0) return 1.0 + 0.1 * (double)(delta - 2);
        return 1.0;
    }

    private static double GetGroupInterval(List<ARTile> prev, List<ARTile> cur, List<ARTile> next) {
        double t0 = prev[0].TileTime, t1 = cur[0].TileTime, t2 = next[0].TileTime;
        double num = t1 - t0;
        if (num <= 0.001) return 1.0;
        return (t2 - t1) / num;
    }

    private static double WrapTo180(double angle) {
        angle %= 360.0;
        if (angle > 180.0) angle = -(360.0 - angle);
        else if (angle < -180.0) angle += 360.0;
        return angle;
    }

    private static double GeneralizeAngle(double angle) {
        return (angle % 360.0 + 360.0) % 360.0;
    }

    private static double GetAngleDiff(double s, double t) {
        double num = s - t;
        if (num > 180.0) num = -(360.0 - num);
        else if (num < -180.0) num += 360.0;
        return WrapTo180(num);
    }
}
