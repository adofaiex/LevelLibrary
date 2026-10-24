using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：HeatMap（由 PortalUI.cs 拆分而来，仍属同一个 partial class）</summary>
public static partial class PortalUI {

    /// <summary>寄录图区：matplotlib 风格折线图（网格线 / 轴标签 / 峰值标注）。
    /// 图表挂在传进来的 area 下（详情区传 _detailHeatArea，旧弹窗传 _pickHeatArea），不要改回共用字段。</summary>
    private static void MakePickHeatArea(GameObject area) {
        if (area == null || _heatDots != null) return;
        // 重建前必须清空容器旧子节点：旧弹窗关闭只清 _heatDots，图表会残留，重建就叠一层
        for (int i = area.transform.childCount - 1; i >= 0; i--) {
            Object.Destroy(area.transform.GetChild(i).gameObject);
        }
        var chartBg = MakeImage(area.transform, "Chart", new Color(0f, 0f, 0f, 0f));
        chartBg.rectTransform.anchorMin = new Vector2(0.06f, 0.08f);
        chartBg.rectTransform.anchorMax = new Vector2(0.94f, 0.94f);
        chartBg.rectTransform.offsetMin = Vector2.zero; chartBg.rectTransform.offsetMax = Vector2.zero;
        // 水平网格线（5 条：0/25/50/75/100% 高度）
        for (int i = 0; i < 5; i++) {
            var hg = MakeImage(chartBg.transform, "GridH" + i, new Color(1f, 1f, 1f, 0.22f));
            hg.raycastTarget = false;
            var hrt = hg.rectTransform;
            hrt.anchorMin = new Vector2(0.02f, 0.08f + i * 0.21f);
            hrt.anchorMax = new Vector2(0.98f, 0.08f + i * 0.21f);
            hrt.sizeDelta = new Vector2(0f, 1f);
        }
        // 垂直网格线（5 条：0/25/50/75/100% 位置）
        for (int i = 0; i < 5; i++) {
            var vg = MakeImage(chartBg.transform, "GridV" + i, new Color(1f, 1f, 1f, 0.22f));
            vg.raycastTarget = false;
            var vrt = vg.rectTransform;
            vrt.anchorMin = new Vector2(0.04f + i * 0.23f, 0.08f);
            vrt.anchorMax = new Vector2(0.04f + i * 0.23f, 0.92f);
            vrt.sizeDelta = new Vector2(1f, 0f);
        }
        // Y 轴标签（左，刷新时更新为真实次数）
        _heatAxisY = new Text[5];
        for (int i = 0; i < 5; i++) {
            var yt = MakeText(chartBg.transform, "AxisY" + i, 14, new Color(0.65f, 0.65f, 0.65f, 1f), TextAnchor.MiddleRight);
            yt.text = "0";
            var yrt = yt.rectTransform;
            yrt.anchorMin = yrt.anchorMax = new Vector2(0f, 0.08f + i * 0.21f);
            yrt.pivot = new Vector2(1f, 0.5f);
            yrt.anchoredPosition = new Vector2(-6f, 0f);
            _heatAxisY[i] = yt;
        }
        // X 轴标签（底：0%/50%/100%）
        for (int i = 0; i < 3; i++) {
            var xt = MakeText(chartBg.transform, "AxisX" + i, 14, new Color(0.65f, 0.65f, 0.65f, 1f), TextAnchor.UpperCenter);
            xt.text = (i * 50) + "%";
            var xrt = xt.rectTransform;
            xrt.anchorMin = xrt.anchorMax = new Vector2(0.04f + i * 0.46f, 0f);
            xrt.pivot = new Vector2(0.5f, 1f);
            xrt.anchoredPosition = new Vector2(0f, -6f);
        }
        // 折线段（10 条）
        _heatLines = new Image[10];
        for (int i = 0; i < 10; i++) {
            var line = MakeImage(chartBg.transform, "Line" + i, new Color(1f, 0.5f, 0.2f, 0.95f));
            line.raycastTarget = false;
            var lrt = line.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(2f, 3f);
            _heatLines[i] = line;
        }
        // 数据点（11 个，每 10% 一个）
        _heatDots = new Image[11];
        for (int i = 0; i <= 10; i++) {
            var dot = MakeImage(chartBg.transform, "Dot" + i, new Color(1f, 0.85f, 0.3f, 1f));
            dot.raycastTarget = false;
            var drt = dot.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 0f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(7f, 7f);
            _heatDots[i] = dot;
        }
        // 峰值标注（最难点上方小标签）
        _heatPeakLabel = MakeText(chartBg.transform, "Peak", 14, new Color(1f, 0.5f, 0.3f, 1f), TextAnchor.LowerCenter);
        _heatPeakLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        _heatPeakLabel.gameObject.SetActive(false);
        _heatStats = MakeText(area.transform, "Stats", 20, Color.white, TextAnchor.UpperLeft);
        var srt = _heatStats.rectTransform;
        srt.anchorMin = new Vector2(0.06f, 0.02f); srt.anchorMax = new Vector2(0.94f, 0.10f);
        srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
        _heatStats.lineSpacing = 1.4f;
        _heatEmpty = MakeText(area.transform, "Empty", 24, new Color(0.6f, 0.6f, 0.6f, 1f), TextAnchor.MiddleCenter);
        _heatEmpty.text = Lang.Tr("还没有寄录，去玩一局吧喵");
        var ert = _heatEmpty.rectTransform;
        ert.anchorMin = new Vector2(0.06f, 0.12f); ert.anchorMax = new Vector2(0.94f, 0.78f);
        ert.offsetMin = Vector2.zero; ert.offsetMax = Vector2.zero;
        _heatEmpty.gameObject.SetActive(false);
        RefreshHeatMap(_selectedPath);
    }

    private static void TickHeatGrow() {
        if (!_heatGrowActive || _heatDots == null || _heatDotTargetY == null) return;
        // 空状态显示时绝不能让动画把点线重新点亮：从有寄录的谱面切到没寄录的谱面时，
        // 上一张图的点线会被动画复活并永久留下，和空提示叠在一起
        if (_heatEmpty != null && _heatEmpty.gameObject.activeSelf) { _heatGrowActive = false; return; }
        _heatGrowT += Time.unscaledDeltaTime / 0.55f;
        if (_heatGrowT >= 1f) { _heatGrowT = 1f; _heatGrowActive = false; }
        // OutCubic 缓出，收尾自然
        float e = 1f - Mathf.Pow(1f - _heatGrowT, 3f);
        var cur = _heatCur;   // 静态复用，别每帧 new Vector2[11]（见字段注释）
        for (int i = 0; i <= 10; i++) {
            if (_heatDots[i] == null) continue;
            float x = _heatDotTargetX[i];
            float y = Mathf.Lerp(_heatChartBaseY, _heatDotTargetY[i], e);
            cur[i] = new Vector2(x, y);
            _heatDots[i].rectTransform.anchoredPosition = cur[i];
            float sc = Mathf.Lerp(0.25f, 1f, e);
            float baseSize = (_heatDotBaseSize != null && i < _heatDotBaseSize.Length) ? _heatDotBaseSize[i] : 7f;
            _heatDots[i].rectTransform.sizeDelta = new Vector2(baseSize * sc, baseSize * sc);
        }
        if (_heatLines != null) {
            for (int i = 0; i < 10; i++) {
                if (_heatLines[i] == null) continue;
                Vector2 a = cur[i], b = cur[i + 1];
                Vector2 mid = (a + b) * 0.5f;
                float dx = b.x - a.x, dy = b.y - a.y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                var lrt = _heatLines[i].rectTransform;
                lrt.anchoredPosition = mid;
                lrt.sizeDelta = new Vector2(len, 3f);
                lrt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
                _heatLines[i].gameObject.SetActive(true);
            }
        }
        // 点/线只在这里做视觉提升，颜色由 RefreshHeatMap 设好，不要在这里改
    }

    /// <summary>隐藏整套图表（点 / 线 / 峰值 / 统计文字），可顺带显示"还没有寄录"那行字。
    /// 必须连上升动画一起停：否则 TickHeatGrow 每帧会重新 SetActive(true)，把上一张图的点线复活并永久留下。</summary>
    private static void HideHeatChart(bool showEmptyText) {
        _heatGrowActive = false;
        if (_heatDots == null) return;  // 图还没建过 → 没东西可隐藏
        if (_heatLines != null)
            for (int i = 0; i < _heatLines.Length; i++) if (_heatLines[i] != null) _heatLines[i].gameObject.SetActive(false);
        for (int i = 0; i < _heatDots.Length; i++) if (_heatDots[i] != null) _heatDots[i].gameObject.SetActive(false);
        if (_heatPeakLabel != null) _heatPeakLabel.gameObject.SetActive(false);
        if (_heatStats != null) _heatStats.text = "";
        if (_heatEmpty != null) _heatEmpty.gameObject.SetActive(showEmptyText);
    }

    private static void RefreshHeatMap(string folder) {
        _heatFolder = folder;
        if (_heatDots == null) return;
        var map = LevelScanner.GetDeathMap(folder);
        int total = 0, max = 0, worst = -1;
        if (map != null) {
            for (int i = 0; i < 10; i++) { total += map[i]; if (map[i] > max) { max = map[i]; worst = i; } }
        }
        float chartW = 300f, chartH = 140f;
        float cw = 300f, chh = 140f;
        try {
            Canvas.ForceUpdateCanvases();
            var cb = _heatDots[0].transform.parent.GetComponent<RectTransform>();
            if (cb.rect.width > 20f) { cw = cb.rect.width; chh = cb.rect.height; }
        } catch { }
        // 折线坐标与网格对齐：x 0.04-0.96，y 0.08-0.92（跟网格线/轴标签同一坐标系）
        float x0 = 0.04f * cw, x1 = 0.96f * cw;
        float y0 = 0.08f * chh, y1 = 0.92f * chh;
        chartW = x1 - x0;
        chartH = y1 - y0;
        if (map == null || total == 0) {
            // 必须走 HideHeatChart：直接 SetActive(false) 的对象会被上升动画每帧重新点亮
            HideHeatChart(true);
            if (_heatAxisY != null) for (int i = 0; i < 5; i++) _heatAxisY[i].text = "0";
            return;
        }
        // 有数据：先收起空状态，再启动上升动画
        _heatEmpty.gameObject.SetActive(false);
        // Y 轴最大值：向上取整到 10 的倍数（最小 10）——不超过 10 时顶值 10，超过变 20、30…
        int yMax = Mathf.Max(10, ((max + 9) / 10) * 10);
        if (_heatAxisY != null) {
            for (int i = 0; i < 5; i++) {
                _heatAxisY[i].text = Mathf.RoundToInt(yMax * i / 4f).ToString();
            }
        }
        // 11 个点：x = 0%,10%..100%；点 i(0-9)=段 i，点 10=段 9（高度按 yMax 比例，坐标对齐网格）
        var pts = new Vector2[11];
        for (int i = 0; i <= 10; i++) {
            int seg = i > 9 ? 9 : i;
            float ratio = (float)map[seg] / yMax;
            float x = x0 + i / 10f * chartW;
            float y = y0 + ratio * chartH;
            pts[i] = new Vector2(x, y);
            var drt = _heatDots[i].rectTransform;
            drt.anchoredPosition = pts[i];
            drt.gameObject.SetActive(true);
            drt.sizeDelta = new Vector2(map[seg] > 0 ? 7f : 3f, map[seg] > 0 ? 7f : 3f);
            _heatDots[i].color = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.15f, 0.15f), ratio);
        }
        // 记录目标位置并启动"从 0 升上去"的动画（点与连线一起）
        if (_heatDotTargetX == null || _heatDotTargetX.Length != 11) {
            _heatDotTargetX = new float[11];
            _heatDotTargetY = new float[11];
            _heatDotBaseSize = new float[11];
        }
        for (int i = 0; i <= 10; i++) {
            _heatDotTargetX[i] = pts[i].x;
            _heatDotTargetY[i] = pts[i].y;
            int seg0 = i > 9 ? 9 : i;
            _heatDotBaseSize[i] = map[seg0] > 0 ? 7f : 3f;  // 基准尺寸：动画期间按它缩放
        }
        _heatChartBaseY = y0;
        _heatGrowT = 0f;
        _heatGrowActive = true;
        _heatChartH = chartH;
        // 连接线段（10 条）
        for (int i = 0; i < 10; i++) {
            var lrt = _heatLines[i].rectTransform;
            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 mid = (a + b) * 0.5f;
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            lrt.anchoredPosition = mid;
            lrt.sizeDelta = new Vector2(len, 3f);
            lrt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
            int sa = i > 9 ? 9 : i, sb = (i + 1) > 9 ? 9 : (i + 1);
            float r = (float)(map[sa] + map[sb]) / (2f * yMax);
            _heatLines[i].color = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.15f, 0.15f), r);
            _heatLines[i].gameObject.SetActive(true);
        }
        // 峰值标注（最难点上方）
        if (worst >= 0 && _heatPeakLabel != null) {
            _heatPeakLabel.text = Lang.TrFmt("峰值 {0}% ~ {1}% · {2} 次", worst * 10, (worst + 1) * 10, map[worst]);
            _heatPeakLabel.rectTransform.anchoredPosition = pts[worst] + new Vector2(0f, 16f);
            _heatPeakLabel.gameObject.SetActive(true);
        } else if (_heatPeakLabel != null) {
            _heatPeakLabel.gameObject.SetActive(false);
        }
        _heatStats.text = Lang.Tr("总寄次数：") + total
            + (worst >= 0 ? Lang.TrFmt("\n最难点：{0}% ~ {1}% 处（{2} 次）", worst * 10, (worst + 1) * 10, map[worst]) : "");
    }

    /// <summary>目录名是否带“副本/copy”标记（Windows 复制产生的）</summary>
    private static bool IsCopyDir(string folder) {
        if (string.IsNullOrEmpty(folder)) return false;
        string n = System.IO.Path.GetFileName(folder);
        return n.IndexOf("副本", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("copy", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>取文件夹内所有 .adofai 文件的 SHA256 哈希（内容一样 → 哈希一样）</summary>
    private static List<string> AdofaiHashes(string folder) {
        var list = new List<string>();
        try {
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                foreach (var f in Directory.GetFiles(folder, "*.adofai")) {
                    try {
                        byte[] data = File.ReadAllBytes(f);
                        byte[] hash = sha.ComputeHash(data);
                        list.Add(System.BitConverter.ToString(hash).Replace("-", ""));
                    } catch { }
                }
            }
        } catch { }
        return list;
    }

}
