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

/// <summary>PortalUI partial 模块：Guide（由 PortalUI.cs 拆分而来，仍属同一个 partial class）
/// 调试工具：在详情区 / 列表 / 搜索框 / 左侧那一行的边界上画 3px 空心参考线，肉眼核对对齐。
/// mod 目录放 debug_guides.txt 就自动画，删掉即恢复。</summary>
public static partial class PortalUI {

    /// <summary>在详情区 / 列表 / 搜索框 / 左侧那一行的边界上画 3px 空心参考线（只在 mod 目录放 debug_guides.txt 时画）。</summary>
    private static void AddLayoutGuides() {
        try {
            if (_panelCanvas == null) return;
            var root = _panelCanvas.transform;
            const float T = 3f;   // 线宽
            System.Action<Transform, string, float, float, float, float, Color> box =
                (parent, gname, ax0, ay0, ax1, ay1, col) => {
                    // 四条边都靠「锚到对应边界 + 固定像素尺寸」才画得出真细线：先按 (px,py) 单点锚定铺一段矩形，
                    // 再用 offsetMin/offsetMax 把它朝内侧拉成一条线（负的 offsetMax 就是把纵向拉出 -T）。
                    System.Action<string, float, float, float, float> edge =
                        (ename, px, py, w, h) => {
                            var e = MakeImage(parent, ename, col);
                            e.sprite = null;
                            e.type = Image.Type.Simple;
                            e.raycastTarget = false;
                            var r = e.rectTransform;
                            r.anchorMin = r.anchorMax = new Vector2(px, py);
                            r.pivot = new Vector2(0f, 0f);
                            r.sizeDelta = new Vector2(w, h);
                            r.anchoredPosition = Vector2.zero;
                        };
                    edge(gname + "_Bottom", ax0, ay0, 0f, T);   // 底边：横向拉伸，高 3
                    edge(gname + "_Top", ax0, ay1, 0f, T);      // 顶边：横向拉伸，高 3
                    edge(gname + "_Left", ax0, ay0, T, 0f);     // 左边：纵向拉伸，宽 3
                    edge(gname + "_Right", ax1, ay0, T, 0f);    // 右边：纵向拉伸，宽 3
                    // 底/顶边横向拉伸（左锚 ax0、右锚 ax1）；左/右边纵向拉伸（下锚 ay0、上锚 ay1）
                    var b = parent.Find(gname + "_Bottom"); if (b != null) { var r = b.GetComponent<RectTransform>(); r.anchorMin = new Vector2(ax0, ay0); r.anchorMax = new Vector2(ax1, ay0); r.offsetMin = new Vector2(0f, 0f); r.offsetMax = new Vector2(0f, T); }
                    var t = parent.Find(gname + "_Top"); if (t != null) { var r = t.GetComponent<RectTransform>(); r.anchorMin = new Vector2(ax0, ay1); r.anchorMax = new Vector2(ax1, ay1); r.offsetMin = new Vector2(0f, -T); r.offsetMax = new Vector2(0f, 0f); }
                    var l = parent.Find(gname + "_Left"); if (l != null) { var r = l.GetComponent<RectTransform>(); r.anchorMin = new Vector2(ax0, ay0); r.anchorMax = new Vector2(ax0, ay1); r.offsetMin = new Vector2(0f, 0f); r.offsetMax = new Vector2(T, 0f); }
                    var rr = parent.Find(gname + "_Right"); if (rr != null) { var r = rr.GetComponent<RectTransform>(); r.anchorMin = new Vector2(ax1, ay0); r.anchorMax = new Vector2(ax1, ay1); r.offsetMin = new Vector2(-T, 0f); r.offsetMax = new Vector2(0f, 0f); }
                };
            // 详情区（红）：像素定位，与 PanelTop/PanelH 一致
            box(root, "Guide_Detail", 62f / RefW, 1f - (PanelTop + PanelH) / RefH, (62f + 806f) / RefW, 1f - PanelTop / RefH, new Color(1f, 0.25f, 0.25f, 0.9f));
            // 列表（青）：左 / 上边界跟着"未选中拉满"动，按当前状态画（`_libExpand` 0=选中，1=未选中）
            float gLeft = Mathf.Lerp(SearchBoxX, LibFullLeft, _libExpand);
            float gTop = Mathf.Lerp(ListTop, ListTopFull, _libExpand);
            box(root, "Guide_List", gLeft / RefW, 1f - PanelBottom / RefH, ListRightEdge / RefW, 1f - gTop / RefH, new Color(0.25f, 0.95f, 1f, 0.9f));
            // 搜索框（黄）：在右侧列表上方，与列表同左边界，右边界固定
            box(root, "Guide_Search", gLeft / RefW, 1f - (SearchBoxTop + SearchBoxH) / RefH,
                SearchRightEdge / RefW, 1f - SearchBoxTop / RefH, new Color(1f, 0.9f, 0.2f, 0.9f));
            // 左侧那一行：滚动信息栏 + 难度滑条（橙）
            box(root, "Guide_TopRow", 62f / RefW, 1f - (SearchRowTop + SearchRowH) / RefH,
                (62f + 806f) / RefW, 1f - SearchRowTop / RefH, new Color(1f, 0.6f, 0.2f, 0.9f));
        } catch (System.Exception e) { Lg.Ex("画布局参考线失败", e); }
    }

}
