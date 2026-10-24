using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace LevelLibrary;

/// <summary>宠物模型包（.llmd）的格式引擎：解析、几何推导、校验、落盘 —— 全是纯逻辑，不碰 UnityEngine，
/// 所以验证台能直接拿真包喂它（解码图片那一半在 PortalUI.PetImport.cs）。
/// 包就是一个 zip，三个条目：model.png（模型图，必须）、lines.txt（台词，一行一句，必须）、
/// model.txt（清单，可选：name / id / say）。
/// 几何一律从 model.png 推，作者不用填任何数字：高宽比给缩放，alpha 外框给"内容从哪开始"，
/// 气泡框沿用大肥鱼那条相对规则（内容外框左上角 + 大肥鱼那一份偏移），自带气泡再按两张图的占用掩膜做一次净空
/// （压住角色的比例超阈值就整体上抬，见 LiftBubbleOffBody）。</summary>
public static class PetModelPack {

    // ---------- 参照（大肥鱼）：与 PortalUI.Pet.cs 的 PetBubbleX/Y 同源，alpha 外框是 610² 素材量出来的 ----------
    public const int RefWidth = 610;
    public const int RefAlphaLeft = 45, RefAlphaTop = 10;
    public const float RefBubX = -0.5885f, RefBubY = -0.6803f;
    /// <summary>气泡框的参照尺寸（= 资源包那张 bubble.png 850×690 ÷ 610）。自带气泡按「保持比例塞进这个框、贴左上角」算：
    /// 宽取「框宽」与「按比例放进框高」的较小者，高跟着比例走 —— 于是任何比例都不会被压扁，也不会比原来那张更大。</summary>
    public const float RefBubW = 1.3934f, RefBubH = 1.1311f;

    // ---------- 净空（只对自带气泡）：气泡的可见像素不许压在角色身上 ----------
    /// <summary>允许压住的比例上限。按 0 算：自带气泡的"尾巴"往往只占几格，覆盖率看着很小
    /// （甘城那份是 0.18%）但那一小块正落在角色头顶上，一眼就是"贴着"。参照那份（大肥鱼）走的是内建几何、
    /// 根本不进这条函数，所以这里放松到 0 不会把它抬起来。</summary>
    public const float ClearTol = 0f;
    /// <summary>最多往上抬多少（单位 S）。抬到上限还清不干净就维持参照落点：宁可维持现状，也不把气泡甩出去。</summary>
    public const float ClearMaxLift = 0.5f;
    /// <summary>占用栅格沿宽度切多少格（上限 = 图宽，格子越小抬得越准、越费时）。</summary>
    public const int ClearGrid = 512;

    // ---------- 包内条目名（制作工具与导入器共用这一份定义）----------
    public const string EntryImage = "model.png";
    public const string EntryLines = "lines.txt";
    public const string EntryManifest = "model.txt";
    /// <summary>自带气泡（可选，第四条目）：有就用它，没有就用资源包那张 bubble.png。</summary>
    public const string EntryBubble = "bubble.png";
    public const string Extension = ".llmd";
    /// <summary>模型图边长上限：只用来挡"误把一张几千像素的图当模型图"（解码要占内存）</summary>
    public const int MaxImageSide = 4096;
    /// <summary>模型图边长下限：再小就不是素材了（多半是别的文件改了后缀）</summary>
    public const int MinImageSide = 8;

    /// <summary>从素材尺寸与 alpha 外框推出来的几何（单位都是"渲染宽度 S"；BubX/BubY 的 y 向下）</summary>
    public struct Geometry {
        public float TexK;       // 高 / 宽
        public float MaskTopK;   // 内容第一行 / 高（进谱面过渡对齐气泡用）
        public float BubX, BubY; // 气泡框左上角
        public float BubW, BubH; // 气泡框尺寸（自带气泡按比例塞进参照框；没有自带气泡时 = 参照框）
    }

    /// <summary>一个装好的模型（包解析 + 几何算完之后的样子）</summary>
    public sealed class PackInfo {
        /// <summary>清单里写了 `face=flip` 就是真：这个素材的正面朝另一边。</summary>
        public bool FaceFlip;
        public string Name;      // 设置里显示的名字
        public string Id;        // 落盘目录名（稳定，别跟着显示名改）
        public string SayLine;   // 这个模型的"固定那句"（进谱面过渡、四条插播通道）
        /// <summary>开谱面台词（多条，含 SayLine 那句；null/空 = 只有 SayLine，老行为）。
        /// 清单键 say1=…say32=（键名互异所以现有解析层天然支持；编辑器把第一条同时写成 say= 与 say1=，
        /// 老版本 mod 只读 say= 也不坏）。</summary>
        public string[] SayLines;
        /// <summary>定时台词：今日游玩到点（分钟）说一句，一天一次。at<分钟>=台词，编辑器可无限加。</summary>
        public int[] TimedMins;
        public string[] TimedTexts;
        /// <summary>包内清单自带完整气泡几何（bubx/buby/bubw/bubh 四键齐全）：编辑器里作者自己摆好了，
        /// 导入时不再套大肥鱼偏移规则、不再做净空。</summary>
        public bool ExplicitBubble;
        public string[] Lines;   // 台词池（一行一句）
        public Geometry Geo;
        public int Width, Height; // 模型图尺寸（只给日志看）
        /// <summary>这个包带了自带气泡（落盘目录里会有 bubble.png）</summary>
        public bool HasBubble;
    }

    /// <summary>素材尺寸与 alpha 外框 → 几何。BubY 除以的是宽：那套坐标的 y 也以 S（渲染宽度）为单位。</summary>
    public static Geometry ComputeGeometry(int w, int h, int alphaLeft, int alphaTop) {
        var g = new Geometry();
        if (w <= 0 || h <= 0) return g;
        g.TexK = (float)h / w;
        g.MaskTopK = Math.Max(0f, Math.Min(1f, (float)alphaTop / h));
        // 大肥鱼那一份偏移 = 它的气泡框位置 − 它的内容外框左上角（两者同一套单位，所以能直接挪到别的素材上）
        float offX = RefBubX - (float)RefAlphaLeft / RefWidth;
        float offY = RefBubY - (float)RefAlphaTop / RefWidth;
        g.BubX = (float)alphaLeft / w + offX;
        g.BubY = (float)alphaTop / w + offY;
        g.BubW = RefBubW;      // 没有自带气泡时就是资源包那张的框
        g.BubH = RefBubH;
        return g;
    }

    /// <summary>自带气泡的框：保持比例塞进参照框、贴左上角 —— 宽取「框宽」与「按比例放进框高」的较小者，高跟着比例走。
    /// 甘城那张 1786×1173（ar 1.5226）算出来是 1.3934 × 0.9152：宽顶着框、高比框矮（硬拉会把泡泡压扁 19%）。
    /// 尺寸读不出来（0 / 负数）时退回参照框。</summary>
    public static void ComputeBubbleFit(int bubbleW, int bubbleH, out float w, out float h) {
        w = RefBubW;
        h = RefBubH;
        if (bubbleW <= 0 || bubbleH <= 0) return;
        float ar = (float)bubbleW / bubbleH;
        w = Math.Min(RefBubW, RefBubH * ar);
        h = w / ar;
    }

    /// <summary>占用栅格：沿宽度切 ClearGrid 格，行数按宽高比推（格子近似正方）。
    /// 每格存的是「格心那一个像素有没有 alpha」，不是「格里有没有像素」：参照那份的尾巴小圆点只有一像素宽的抗锯齿细边，
    /// 按后者算会把细边放大成一整格，于是参照自己也被抬起来（实测 4.4%，必须 0）。</summary>
    private struct Grid {
        public int W, H;        // 图尺寸
        public int Gx, Gy;      // 网格格数
        public int On;          // 亮着的格数（全透明时是 0）
        public bool[] Cells;    // Gx × Gy，行主序
    }

    private static Grid NewGrid(int w, int h) {
        var g = new Grid {
            W = w, H = h,
            Gx = Math.Max(1, Math.Min(ClearGrid, w)),
            Gy = Math.Max(1, Math.Min(Math.Min(h, ClearGrid * 4), (int)Math.Round((double)ClearGrid * h / w))),
        };
        g.Cells = new bool[g.Gx * g.Gy];
        return g;
    }

    /// <summary>格心在素材像素里的坐标（浮点那份给气泡换元用，整数那份给取像素用）</summary>
    private static float CellF(int i, int n, int size) { return (i + 0.5f) * size / n; }
    private static int CellI(int i, int n, int size) {
        int v = (int)CellF(i, n, size);
        return v < 0 ? 0 : (v > size - 1 ? size - 1 : v);
    }

    /// <summary>量一张图的占用栅格。判据与 MeasureAlphaBox 同一个口径（alpha != 0），两边的几何才对得上。</summary>
    private static string MeasureGrid(byte[] imageBytes, out Grid grid) {
        grid = default(Grid);
        try {
            using (var ms = new MemoryStream(imageBytes))
            using (var bmp = new Bitmap(ms)) {
                int w = bmp.Width, h = bmp.Height;
                if (w < MinImageSide || h < MinImageSide) return "图太小了（每边至少 8 像素）";
                if (w > MaxImageSide || h > MaxImageSide) return "图太大了（每边不超过 4096 像素）";
                var g = NewGrid(w, h);
                var rect = new Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    int stride = data.Stride;
                    var all = new byte[stride * h];
                    Marshal.Copy(data.Scan0, all, 0, all.Length);
                    int n = 0;
                    for (int y = 0; y < g.Gy; y++) {
                        int py = CellI(y, g.Gy, h);
                        int row = py * stride;
                        int crow = y * g.Gx;
                        for (int x = 0; x < g.Gx; x++) {
                            if (all[row + CellI(x, g.Gx, w) * 4 + 3] == 0) continue;   // 32bppArgb 的字节序是 BGRA，alpha 在第 4 个
                            g.Cells[crow + x] = true; n++;
                        }
                    }
                    g.On = n;
                    grid = g;
                } finally { bmp.UnlockBits(data); }
            }
            return null;
        } catch (Exception e) {
            Log("占用栅格读不出来", e);
            return "图读不出来，请用 PNG 格式";
        }
    }

    /// <summary>把自带气泡的占用栅格采到角色那套格子上：角色每一格的格心换算回气泡的像素坐标，取气泡那一张的 alpha。
    /// 两张图因此落在同一套格子里，覆盖率与位移都只在这套格子上算。</summary>
    private static string MeasureGridOn(Grid body, Geometry geo, byte[] bubbleBytes, out Grid grid) {
        grid = default(Grid);
        try {
            using (var ms = new MemoryStream(bubbleBytes))
            using (var bmp = new Bitmap(ms)) {
                int bw = bmp.Width, bh = bmp.Height;
                if (bw < MinImageSide || bh < MinImageSide) return "图太小了（每边至少 8 像素）";
                if (bw > MaxImageSide || bh > MaxImageSide) return "图太大了（每边不超过 4096 像素）";
                var g = NewGrid(body.W, body.H);
                g.Gx = body.Gx; g.Gy = body.Gy; g.Cells = new bool[g.Gx * g.Gy];
                // 气泡框在素材像素里的左上角，以及「气泡一个像素 = 素材几个像素」
                float bx = geo.BubX * body.W, by = geo.BubY * body.W;
                float sx = geo.BubW * body.W / bw, sy = geo.BubH * body.W / bh;
                var rect = new Rectangle(0, 0, bw, bh);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    int stride = data.Stride;
                    var all = new byte[stride * bh];
                    Marshal.Copy(data.Scan0, all, 0, all.Length);
                    int n = 0;
                    for (int y = 0; y < g.Gy; y++) {
                        int v = (int)Math.Round((CellF(y, g.Gy, body.H) - by) / sy);
                        if (v < 0 || v >= bh) continue;
                        int row = v * stride;
                        int crow = y * g.Gx;
                        for (int x = 0; x < g.Gx; x++) {
                            int u = (int)Math.Round((CellF(x, g.Gx, body.W) - bx) / sx);
                            if (u < 0 || u >= bw) continue;
                            if (all[row + u * 4 + 3] == 0) continue;
                            g.Cells[crow + x] = true; n++;
                        }
                    }
                    g.On = n;
                    grid = g;
                } finally { bmp.UnlockBits(data); }
            }
            return null;
        } catch (Exception e) {
            Log("自带气泡的占用栅格读不出来", e);
            return "图读不出来，请用 PNG 格式";
        }
    }

    /// <summary>气泡按当前落点上抬 shift 格之后，压在角色身上的格数（覆盖率 = 它 ÷ 角色格数）。
    /// 位移就是把气泡那一片栅格往下挪 shift 行再按位取与 —— 逐格精确，不引入近似。</summary>
    private static int CoveredCells(Grid body, Grid bub, int shift) {
        int rows = body.Gy - shift;
        if (rows <= 0) return 0;
        int n = 0;
        for (int y = 0; y < rows; y++) {
            int a = y * body.Gx, b = (y + shift) * body.Gx;
            for (int x = 0; x < body.Gx; x++) {
                if (body.Cells[a + x] && bub.Cells[b + x]) n++;
            }
        }
        return n;
    }

    /// <summary>自带气泡的净空：按参照落点摆好之后，用两张图的占用栅格量「气泡压了角色多少」，
    /// 超阈值就把气泡整体往上抬最小格数 —— 只上抬、不左右移（横向那一段与大肥鱼本来就一致，动了反而不一致）。
    /// 返回抬了多少（单位 S，0 = 没抬）。参照那份的尾巴落在角色左边的空档里，覆盖率只有 0.07%，所以恒返回 0，一个像素都不动。
    /// 抬到上限还清不干净也返回 0（维持参照落点，不把气泡甩出去）。
    /// g 必须走 ref —— Geometry 是值类型，按值传改的是副本，抬了等于没抬。</summary>
    public static float LiftBubbleOffBody(ref Geometry g, byte[] bodyBytes, byte[] bubBytes) {
        Grid body, bub;
        if (MeasureGrid(bodyBytes, out body) != null) return 0f;
        if (MeasureGridOn(body, g, bubBytes, out bub) != null) return 0f;
        if (body.On <= 0 || bub.On <= 0) return 0f;
        int allowed = (int)Math.Floor(body.On * ClearTol);
        if (CoveredCells(body, bub, 0) <= allowed) return 0f;
        float cell = (float)body.H / body.Gy;                    // 一格 = 这么多素材像素
        int maxShift = (int)Math.Floor(ClearMaxLift * body.W / cell);
        for (int k = 1; k <= maxShift && k < body.Gy; k++) {
            if (CoveredCells(body, bub, k) <= allowed) {
                float dy = k * cell;
                g.BubY -= dy / body.W;
                return dy / body.W;
            }
        }
        return 0f;
    }

    /// <summary>自带气泡再往左挪：让它可见像素的右沿落在角色可见像素的中线上。
    /// 为什么需要这一步：参照那份（大肥鱼）的气泡画在素材左侧、尾巴朝右下，落点看上去是"角色左上方"；
    /// 而自带气泡常常是满幅的云（甘城那张 alpha 外框就是整张图），同一条"以框左上角为锚"的规则落下来
    /// 会变成"正上方"、尾巴点还搭在头顶上 —— 光往上抬解决不了观感，得连着往左挪。
    /// 只往左挪、有上限（ClearMaxLift）；参照那份走内建几何、根本不进这里。
    /// 返回挪了多少（单位 S，0 = 没挪）。g 必须走 ref。</summary>
    public static float ShiftBubbleLeft(ref Geometry g, byte[] bodyBytes, byte[] bubBytes) {
        Grid body, bub;
        if (MeasureGrid(bodyBytes, out body) != null) return 0f;
        if (MeasureGridOn(body, g, bubBytes, out bub) != null) return 0f;
        if (body.On <= 0 || bub.On <= 0) return 0f;
        int bubRight = -1, bodyLeft = -1, bodyRight = -1;
        for (int y = 0; y < body.Gy; y++) {
            int row = y * body.Gx;
            for (int x = 0; x < body.Gx; x++) {
                if (bub.Cells[row + x] && x > bubRight) bubRight = x;
                if (body.Cells[row + x]) {
                    if (bodyLeft < 0 || x < bodyLeft) bodyLeft = x;
                    if (x > bodyRight) bodyRight = x;
                }
            }
        }
        if (bubRight < 0 || bodyLeft < 0 || bodyRight < bodyLeft) return 0f;
        int bodyMid = (bodyLeft + bodyRight) / 2;
        int cols = bubRight - bodyMid;                       // 往左挪这么多格，气泡的右沿就落在中线上
        if (cols <= 0) return 0f;
        int maxCols = (int)Math.Floor(ClearMaxLift * body.Gx);   // 一个 S = Gx 格
        if (cols > maxCols) cols = maxCols;
        if (cols <= 0) return 0f;
        float dx = cols / (float)body.Gx;                    // 单位 S
        g.BubX -= dx;
        return dx;
    }

    /// <summary>清单/落盘文件都是 `key=value` 一行一条（`#` 开头是注释，键不分大小写）。</summary>
    public static Dictionary<string, string> ParseKeyValues(string text) {
        var map = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(text)) return map;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
            string line = raw.Trim().TrimStart('\ufeff');
            if (line.Length == 0 || line[0] == '#') continue;
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            string k = line.Substring(0, i).Trim().ToLowerInvariant();
            if (k.Length == 0 || map.ContainsKey(k)) continue;
            map[k] = line.Substring(i + 1).Trim();
        }
        return map;
    }

    /// <summary>台词：一行一句，空行丢掉，BOM 去掉。顺序就是随机池的顺序。</summary>
    public static string[] ParseLines(string text) {
        if (string.IsNullOrEmpty(text)) return new string[0];
        var list = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
            string line = raw.Trim().TrimStart('\ufeff');
            if (line.Length == 0) continue;
            list.Add(line);
        }
        return list.ToArray();
    }

    /// <summary>小数一律按不变文化解析：玩家系统是逗号小数点的话，`0.65` 会解析成 65。</summary>
    public static float ParseFloat(string s, float dflt) {
        float v;
        if (!string.IsNullOrEmpty(s) && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
        return dflt;
    }

    /// <summary>id 规整成能当目录名的样子（非法字符换掉、限长）—— 目录名全项目只有这一处生成。</summary>
    public static string SanitizeId(string raw) {
        var sb = new StringBuilder();
        foreach (char c in (raw ?? "").Trim()) {
            bool ok = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
            if (ok) sb.Append(c);
            else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
        }
        string s = sb.ToString().Trim('.', '_', ' ');
        if (s.Length > 32) s = s.Substring(0, 32).Trim('.', '_', ' ');
        return s.Length > 0 ? s : "model";
    }

    /// <summary>清单里读名字 / id / 那句固定台词（缺省：名字用文件名、id 由名字规整、固定台词用第一句）。</summary>
    public static void ReadManifest(string text, string fallbackName, out string name, out string id, out string say) {
        var kv = ParseKeyValues(text);
        string v;
        name = kv.TryGetValue("name", out v) && v.Trim().Length > 0 ? v.Trim() : (fallbackName ?? "").Trim();
        id = kv.TryGetValue("id", out v) && v.Trim().Length > 0 ? v.Trim() : name;
        say = kv.TryGetValue("say", out v) ? v.Trim() : null;
        if (string.IsNullOrEmpty(name)) name = "模型";
    }

    /// <summary>清单里的多条开谱面台词：say= 之外还认 say1=…say32=（键名互异，ParseKeyValues 天然支持）。
    /// say1= 与 say= 同句不算重复条目（编辑器两边都写，这里按值去重）；返回 null = 没有多条。</summary>
    public static string[] CollectSayLines(Dictionary<string, string> kv, string primary) {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(primary)) list.Add(primary.Trim());
        for (int i = 1; i <= 32; i++) {
            string v;
            if (!kv.TryGetValue("say" + i, out v)) continue;
            v = (v ?? "").Trim();
            if (v.Length == 0 || list.Contains(v)) continue;
            list.Add(v);
        }
        return list.Count > 1 ? list.ToArray() : null;
    }

    /// <summary>清单里的定时台词：at<分钟>=台词（如 at60=…、at300=…），键名互异所以可以无限多条。
    /// 分钟不合法（非数字 / ≤0 / 超过 1440）或台词为空的丢弃，按分钟升序。</summary>
    public static void CollectTimedLines(Dictionary<string, string> kv, List<int> mins, List<string> texts) {
        var pairs = new List<KeyValuePair<int, string>>();
        foreach (var e in kv) {
            if (e.Key.Length < 3 || !e.Key.StartsWith("at")) continue;
            int m;
            if (!int.TryParse(e.Key.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) continue;
            if (m <= 0 || m > 1440) continue;
            string v = (e.Value ?? "").Trim();
            if (v.Length == 0) continue;
            pairs.Add(new KeyValuePair<int, string>(m, v));
        }
        pairs.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return a.Key.CompareTo(b.Key); });
        foreach (var p in pairs) { mins.Add(p.Key); texts.Add(p.Value); }
    }

    /// <summary>落盘的清单文本（归一化：几何也写进去，启动时只读这个文件，不再解图片）。</summary>
    public static string BuildManifestText(PackInfo info) {
        var sb = new StringBuilder();
        sb.Append("# 宠物模型包落盘清单（导入时由 mod 生成；手改这里可以改名字与那句固定台词）\n");
        sb.Append("version=1\n");
        sb.Append("name=").Append(info.Name.Replace("\n", " ")).Append('\n');
        sb.Append("id=").Append(info.Id).Append('\n');
        sb.Append("say=").Append((info.SayLine ?? "").Replace("\n", " ")).Append('\n');
        // 多条开谱面台词与定时台词（编辑器加的那些）：say1 与 say 同句不算重复，老版本只读 say= 不受影响
        if (info.SayLines != null && info.SayLines.Length > 1) {
            for (int i = 0; i < info.SayLines.Length && i < 32; i++)
                sb.Append(i == 0 ? "say1=" : "say" + (i + 1) + "=").Append(info.SayLines[i].Replace("\n", " ")).Append('\n');
        }
        if (info.TimedMins != null && info.TimedTexts != null) {
            for (int i = 0; i < info.TimedMins.Length && i < 64; i++)
                sb.Append("at").Append(info.TimedMins[i]).Append('=').Append((info.TimedTexts[i] ?? "").Replace("\n", " ")).Append('\n');
        }
        sb.Append("texk=").Append(info.Geo.TexK.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("masktopk=").Append(info.Geo.MaskTopK.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("bubx=").Append(info.Geo.BubX.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("buby=").Append(info.Geo.BubY.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
        // 气泡框尺寸与"有没有自带气泡"都写进清单：启动时只读这个文件，不再解图片
        sb.Append("bubw=").Append(info.Geo.BubW.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("bubh=").Append(info.Geo.BubH.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("bubble=").Append(info.HasBubble ? "1" : "0").Append('\n');
        sb.Append("lines=").Append(info.Lines == null ? 0 : info.Lines.Length).Append('\n');
        return sb.ToString();
    }

    /// <summary>启动时读一个装好的模型目录：清单齐了才算数（几何缺一项就跳过这个模型，不猜）。</summary>
    public static bool TryReadInstalled(string manifestText, string fallbackName, out PackInfo info, out string error) {
        info = null;
        error = null;
        var kv = ParseKeyValues(manifestText);
        string name, id, say;
        ReadManifest(manifestText, fallbackName, out name, out id, out say);
        string texk, mask, bx, by;
        if (!kv.TryGetValue("texk", out texk) || !kv.TryGetValue("masktopk", out mask) ||
            !kv.TryGetValue("bubx", out bx) || !kv.TryGetValue("buby", out by)) {
            error = "清单里缺少几何（texk / masktopk / bubx / buby）";
            return false;
        }
        var g = new Geometry {
            TexK = ParseFloat(texk, 0f),
            MaskTopK = ParseFloat(mask, 0f),
            BubX = ParseFloat(bx, 0f),
            BubY = ParseFloat(by, 0f),
            BubW = RefBubW,
            BubH = RefBubH,
        };
        // 气泡框尺寸是后加的：老模型（清单里没有这两项）按参照框走，效果与加这一条之前一模一样
        string bw, bh, bb;
        if (kv.TryGetValue("bubw", out bw) && kv.TryGetValue("bubh", out bh)) {
            g.BubW = ParseFloat(bw, RefBubW);
            g.BubH = ParseFloat(bh, RefBubH);
        }
        bool hasBubble = kv.TryGetValue("bubble", out bb) && bb.Trim() == "1";
        string fv;
        bool faceFlip = kv.TryGetValue("face", out fv) && fv.Trim().ToLowerInvariant() == "flip";   // 朝向翻转（可选）
        info = new PackInfo {
            Name = name,
            Id = SanitizeId(id),
            SayLine = say,
            Lines = new string[0],
            Geo = g,
            HasBubble = hasBubble,
        };
        // 多条开谱面台词与定时台词（编辑器加的；老清单没有就是 null/空 = 老行为）
        info.SayLines = CollectSayLines(kv, say);
        var tMins = new List<int>(); var tTexts = new List<string>();
        CollectTimedLines(kv, tMins, tTexts);
        info.TimedMins = tMins.ToArray();
        info.TimedTexts = tTexts.ToArray();
        return true;
    }

    /// <summary>几何是否落在合理区间（不合理的直接拒绝：免得一张怪图把宠物摆到屏幕外）。</summary>
    public static string CheckGeometry(Geometry g) {
        if (g.TexK < 0.25f || g.TexK > 4f) return "模型图的长宽比太极端";
        if (g.MaskTopK < 0f || g.MaskTopK > 0.9f) return "模型图的内容外框读不出来";
        return null;
    }

    /// <summary>读一个 .llmd：返回包内条目的原文（图片给字节，别在这里解码）。自带气泡（bubble.png）缺了不算错，
    /// bubbleBytes 就是 null；包里多出来的条目一律忽略（前向兼容）。
    /// error 是给玩家看的一句话（不带任何路径），成功时返回 null。</summary>
    public static string ReadPack(string path, out byte[] imageBytes, out byte[] bubbleBytes,
                                  out string linesText, out string manifestText) {
        imageBytes = null;
        bubbleBytes = null;
        linesText = null;
        manifestText = null;
        try {
            using (var zip = ZipFile.OpenRead(path)) {
                foreach (var e in zip.Entries) {
                    string n = e.Name.ToLowerInvariant();
                    if (n == EntryImage && imageBytes == null) imageBytes = ReadAll(e);
                    else if (n == EntryLines && linesText == null) linesText = ReadText(e);
                    else if (n == EntryManifest && manifestText == null) manifestText = ReadText(e);
                    else if (n == EntryBubble && bubbleBytes == null) bubbleBytes = ReadAll(e);
                }
            }
        } catch (Exception e) {
            Log("模型包打不开", e);
            return "模型包打不开，请确认选的是 .llmd 文件";
        }
        if (imageBytes == null || imageBytes.Length == 0) return "模型包不完整：缺少 " + EntryImage;
        if (string.IsNullOrEmpty(linesText)) return "模型包不完整：缺少 " + EntryLines;
        return null;
    }

    private static byte[] ReadAll(ZipArchiveEntry e) {
        using (var s = e.Open()) {
            var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    private static string ReadText(ZipArchiveEntry e) {
        using (var s = e.Open())
        using (var r = new StreamReader(s, Encoding.UTF8, true)) return r.ReadToEnd();
    }

    /// <summary>量模型图的 alpha 外框：最左有像素的列、最上有像素的行。
    /// Bitmap 的行序自上而下，所以先命中的那一行就是"内容顶"，不用像纹理那样换算。
    /// 判据是 alpha != 0 —— 与做包时 python 的 getbbox() 同一个口径，两边算出来的几何才对得上。</summary>
    public static string MeasureAlphaBox(byte[] imageBytes, out int w, out int h, out int left, out int top) {
        w = h = left = top = 0;
        try {
            using (var ms = new MemoryStream(imageBytes))
            using (var bmp = new Bitmap(ms)) {
                w = bmp.Width;
                h = bmp.Height;
                if (w < MinImageSide || h < MinImageSide) return "模型图太小了（每边至少 8 像素）";
                if (w > MaxImageSide || h > MaxImageSide) return "模型图太大了（每边不超过 4096 像素）";
                var rect = new Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    int stride = data.Stride;
                    var all = new byte[stride * h];
                    Marshal.Copy(data.Scan0, all, 0, all.Length);
                    int minX = w, minY = -1;
                    for (int y = 0; y < h; y++) {
                        int row = y * stride;
                        for (int x = 0; x < w; x++) {
                            if (all[row + x * 4 + 3] == 0) continue;   // 32bppArgb 的字节序是 BGRA，alpha 在第 4 个
                            if (x < minX) minX = x;
                            if (minY < 0) minY = y;
                        }
                    }
                    if (minY < 0) return "模型图整张都是透明的";
                    left = minX;
                    top = minY;
                } finally { bmp.UnlockBits(data); }
            }
            return null;
        } catch (Exception e) {
            Log("模型图解码失败", e);
            return "模型图读不出来，请用 PNG 格式";
        }
    }

    /// <summary>只量一张图的尺寸（自带气泡用：它不参与 alpha 外框那套几何，但宽高比决定塞进框之后的大小）。</summary>
    public static string MeasureImageSize(byte[] imageBytes, out int w, out int h) {
        w = h = 0;
        try {
            using (var ms = new MemoryStream(imageBytes))
            using (var bmp = new Bitmap(ms)) {
                w = bmp.Width;
                h = bmp.Height;
            }
            if (w < MinImageSide || h < MinImageSide) return "图太小了（每边至少 8 像素）";
            if (w > MaxImageSide || h > MaxImageSide) return "图太大了（每边不超过 4096 像素）";
            return null;
        } catch (Exception e) {
            Log("图片解码失败", e);
            return "图读不出来，请用 PNG 格式";
        }
    }

    /// <summary>整个导入流程：读包 → 量 alpha 外框 → 算几何 → 校验 → 落盘。
    /// 成功返回装好的模型，失败返回 null 并把"给玩家看的那一句话"写进 error（不带任何路径）。</summary>
    public static PackInfo ImportFromFile(string packPath, string modelsDir, out string error) {
        error = null;
        byte[] imageBytes, bubbleBytes;
        string linesText, manifestText;
        string err = ReadPack(packPath, out imageBytes, out bubbleBytes, out linesText, out manifestText);
        if (err != null) { error = err; return null; }
        var lines = ParseLines(linesText);
        if (lines.Length == 0) { error = "模型包里没有台词"; return null; }

        int w, h, alphaLeft, alphaTop;
        err = MeasureAlphaBox(imageBytes, out w, out h, out alphaLeft, out alphaTop);
        if (err != null) { error = err; return null; }

        string name, id, say;
        ReadManifest(manifestText, Path.GetFileNameWithoutExtension(packPath), out name, out id, out say);
        var geo = ComputeGeometry(w, h, alphaLeft, alphaTop);
        err = CheckGeometry(geo);
        if (err != null) { error = err; return null; }

        // 自带气泡：按它自己的宽高比塞进参照框（读不出来就当作没带 —— 气泡是装饰，不该让整包导不进来）
        bool hasBubble = false;
        if (bubbleBytes != null && bubbleBytes.Length > 0) {
            int bw, bh;
            string berr = MeasureImageSize(bubbleBytes, out bw, out bh);
            if (berr != null) {
                Log("自带气泡读不出来（这包按没有自带气泡处理）：" + berr, null);
            } else {
                float fitW, fitH;
                ComputeBubbleFit(bw, bh, out fitW, out fitH);
                geo.BubW = fitW;
                geo.BubH = fitH;
                hasBubble = true;
            }
        }
        // 这条路上还没有解析过清单键（下面才用到 lines / name），这里解析一次（face / 多条台词 / 显式气泡几何）。
        var kv = ParseKeyValues(manifestText);
        string fv2;
        bool faceFlip2 = kv.TryGetValue("face", out fv2) && fv2.Trim().ToLowerInvariant() == "flip";
        // 编辑器调好的气泡摆位：bubx/buby/bubw/bubh 四键齐全就原样用（尺寸必须为正，缺一样或非法都当没写）
        string eBx = null, eBy = null, eBw = null, eBh = null;
        bool explicitBubble = kv.TryGetValue("bubx", out eBx) && kv.TryGetValue("buby", out eBy)
                           && kv.TryGetValue("bubw", out eBw) && kv.TryGetValue("bubh", out eBh)
                           && ParseFloat(eBw, 0f) > 0.01f && ParseFloat(eBh, 0f) > 0.01f;
        // 净空：只有自带气泡才做 —— 栅格要两张图都拿得到；没有自带气泡时用的是资源包那张，几何一个数都不动。
        // 顺序是"先往左挪、再往上抬"：挪到角色左上方的位置之后再抬，抬的量才是这个位置真正需要的。
        // 编辑器写好的显式几何跳过这一切：作者在编辑器里摆好了，导入时一个数都不该再动。
        if (explicitBubble) {
            geo.BubX = ParseFloat(eBx, geo.BubX);
            geo.BubY = ParseFloat(eBy, geo.BubY);
            geo.BubW = ParseFloat(eBw, geo.BubW);
            geo.BubH = ParseFloat(eBh, geo.BubH);
        } else if (hasBubble) {
            float dx = ShiftBubbleLeft(ref geo, imageBytes, bubbleBytes);
            float lift = LiftBubbleOffBody(ref geo, imageBytes, bubbleBytes);
            if (dx > 0f || lift > 0f) {
                Log("自带气泡净空：往左挪 " + dx.ToString("0.#####", CultureInfo.InvariantCulture)
                    + " + 往上抬 " + lift.ToString("0.#####", CultureInfo.InvariantCulture)
                    + " 个渲染宽度（让它像参照那样落在角色左上方、且不压住角色）", null);
            }
        }
        var says = CollectSayLines(kv, say);
        var tMins = new List<int>(); var tTexts = new List<string>();
        CollectTimedLines(kv, tMins, tTexts);
        var info = new PackInfo {
            Name = name,
            Id = SanitizeId(id),
            SayLine = says != null ? says[0] : (string.IsNullOrEmpty(say) ? lines[0] : say),
            SayLines = says,
            TimedMins = tMins.ToArray(),
            TimedTexts = tTexts.ToArray(),
            Lines = lines,
            Geo = geo,
            Width = w,
            Height = h,
            HasBubble = hasBubble,
            ExplicitBubble = explicitBubble,
        };
        if (string.IsNullOrEmpty(modelsDir)) { error = "模型存不进去，请检查 mod 目录能不能写"; return null; }
        if (WriteInstalled(modelsDir, imageBytes, hasBubble ? bubbleBytes : null, info, out err) == null) { error = err; return null; }
        return info;
    }

    /// <summary>把解出来的包写成 <dir> 下一个可用的模型目录（同名 id 覆盖 = 重新导入同一份包）。
    /// 目录名用 id，图片与台词各存一份，清单带上算好的几何；带了气泡就再写一份 bubble.png。
    /// 这里一个字节都不删：新包没带气泡时，清单里 bubble=0 会让启动那一侧照旧用资源包那张（旧文件留着不碍事）。</summary>
    public static string WriteInstalled(string dir, byte[] imageBytes, byte[] bubbleBytes, PackInfo info, out string error) {
        error = null;
        try {
            string target = Path.Combine(dir, info.Id);
            Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target, EntryImage), imageBytes);
            if (bubbleBytes != null && bubbleBytes.Length > 0) {
                File.WriteAllBytes(Path.Combine(target, EntryBubble), bubbleBytes);
            }
            File.WriteAllText(Path.Combine(target, EntryLines), string.Join("\n", info.Lines), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(target, EntryManifest), BuildManifestText(info), new UTF8Encoding(false));
            return target;
        } catch (Exception e) {
            Log("模型落盘失败", e);
            error = "模型存不进去，请检查 mod 目录能不能写";
            return null;
        }
    }

    /// <summary>目录里装好的模型清单（按目录名排序：顺序稳定，模型表的顺序也就稳定）。</summary>
    public static List<string> ListInstalled(string dir) {
        var list = new List<string>();
        try {
            if (!Directory.Exists(dir)) return list;
            var dirs = Directory.GetDirectories(dir);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs) list.Add(d);
        } catch (Exception e) { Log("模型目录列不出来", e); }
        return list;
    }

    /// <summary>日志要吞异常：验证台里 ModEntry 是 null，日志把逻辑带崩过（本项目老账）。</summary>
    private static void Log(string what, Exception e) {
        try { Lg.Info("宠物模型包：" + what + (e != null ? "： " + e.Message : "")); } catch { }
    }
}
