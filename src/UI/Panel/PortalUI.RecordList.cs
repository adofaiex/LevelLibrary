using System.Collections.Generic;
using UnityEngine;

namespace LevelLibrary;

/// <summary>通关记录列表的流式让位。详情区那一份与关卡选择弹窗那一份共用这套。
/// 删掉一条之后剩下的行从原位滑到新位，被删掉的那几条自己淡出退场，不再整列同帧重排。
/// 让位的身份用记录自己的时间戳：列表按 X 准确度降序排，行号跟存储顺序无关，
/// 跟行尾那个删除键认的是同一个东西。</summary>
public static partial class PortalUI {
    private const float RecRowGap = 4f;            // 行间缝（原来是布局组的 spacing）
    private const float RecDetailRowH = 44f;       // 详情区一行的高度
    private const float RecPickRowH = 46f;         // 关卡选择弹窗一行的高度
    private const float RecListPadX = 0f;          // 行左右内缩（原来是布局组的 padding 左右）
    private const float RecListPadBottom = 4f;     // 详情区内容盒底部内缩（原来是布局组的 padding 下）
    private const float RecDyingRise = 60f;        // 退场中的行每秒往上飘多少像素（跟让位同方向）

    // 两份列表各一张身份表：同一个时间戳在两份列表里是两条不同的账，前缀分开
    private static readonly Dictionary<long, RectTransform> _recDetailRows = new Dictionary<long, RectTransform>();
    private static readonly Dictionary<long, RectTransform> _recPickRows = new Dictionary<long, RectTransform>();

    /// <summary>正在退场的一行：透明推到 0 就销毁。用 CanvasGroup 而不是逐个改颜色 ——
    /// 行里的文字和删除键各自还有自己的悬停色，乘一层透明度不会跟它们打架。</summary>
    private sealed class RecDyingRow {
        public RectTransform Rt;
        public CanvasGroup Group;
        public float T;
    }
    private static readonly List<RecDyingRow> _recDyingRows = new List<RecDyingRow>();

    /// <summary>行在内容盒里的纵向位置（行的轴心在上边）：第 i 行的顶端离内容盒顶 i 格。</summary>
    private static float RecRowY(int i, float rowH) { return -(i * (rowH + RecRowGap)); }

    /// <summary>内容盒该多高。布局组拆掉之后得自己算，滚动条靠它。</summary>
    private static float RecListHeight(int count, float rowH, float padBottom) {
        if (count <= 0) return 0f;
        return count * rowH + (count - 1) * RecRowGap + padBottom;
    }

    /// <summary>把上一版里已经不在名单上的那几条挑出来淡出退场，其余旧行交给重建那段销毁换新。
    /// 要先办这件事再登记旧位：正在退场的行不该再参与这一次让位。</summary>
    private static void RecFlowRetire(string prefix, Dictionary<long, RectTransform> rows, List<LevelScanner.ClearRecord> list) {
        List<long> gone = null;
        foreach (KeyValuePair<long, RectTransform> kv in rows) {
            bool still = false;
            for (int i = 0; i < list.Count; i++) if (list[i].Time == kv.Key) { still = true; break; }
            if (still) continue;
            if (gone == null) gone = new List<long>();
            gone.Add(kv.Key);
        }
        if (gone == null) return;
        for (int i = 0; i < gone.Count; i++) {
            RectTransform rt;
            if (!rows.TryGetValue(gone[i], out rt)) continue;
            rows.Remove(gone[i]);
            if (rt != null) StartRecRowFade(prefix, gone[i], rt);
        }
    }

    /// <summary>这次要显示的那些记录里还有没有上一版登过记的。有才开让位窗口：
    /// 换一个谱面时两份名单没有交集，整列直接落位，不做无谓的滑动。</summary>
    private static bool RecFlowHasAny(Dictionary<long, RectTransform> rows, List<LevelScanner.ClearRecord> list) {
        if (rows.Count == 0) return false;
        for (int i = 0; i < list.Count; i++) if (rows.ContainsKey(list[i].Time)) return true;
        return false;
    }

    /// <summary>重建前把上一版每根行现在站的位置按身份重登一遍，这是让位的起点。
    /// 第一次见到某个身份只会把它原地放好、不会滑，所以必须重登一次当前值。</summary>
    private static void RecFlowHold(string prefix, Dictionary<long, RectTransform> rows) {
        foreach (KeyValuePair<long, RectTransform> kv in rows) {
            RectTransform rt = kv.Value;
            if (rt != null) FlowKeyTo(prefix + kv.Key, rt, 0f, rt.anchoredPosition.y);
        }
        rows.Clear();
    }

    /// <summary>建好一行就登记它的目标位置：见过的身份从旧位滑过来，没见过的直接落位。</summary>
    private static void RecFlowPlace(string prefix, Dictionary<long, RectTransform> rows, long time, RectTransform rt, float y) {
        rows[time] = rt;
        FlowKeyTo(prefix + time, rt, 0f, y);
    }

    /// <summary>被删掉的那一行：挂一层 CanvasGroup 淡出、同时按让位方向轻轻上飘，退完销毁。
    /// 身份也随之作废 —— 同一条时间戳的记录不会再有第二条。</summary>
    private static void StartRecRowFade(string prefix, long time, RectTransform rt) {
        FlowForgetKey(prefix + time);
        CanvasGroup cg = rt.GetComponent<CanvasGroup>();
        if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
        RecDyingRow d = new RecDyingRow();
        d.Rt = rt; d.Group = cg; d.T = 0f;
        _recDyingRows.Add(d);
    }

    /// <summary>这根行是不是正在退场：重建时别把它当旧内容删掉，退场那段负责收尾。</summary>
    private static bool RecRowIsDying(Transform t) {
        for (int i = 0; i < _recDyingRows.Count; i++) if (_recDyingRows[i].Rt == t) return true;
        return false;
    }

    /// <summary>退场推进。挂常驻段：关卡选择弹窗是独立画布，面板关着时它照样可能在退场。</summary>
    private static void TickRecRowFade(float dt) {
        for (int i = _recDyingRows.Count - 1; i >= 0; i--) {
            RecDyingRow d = _recDyingRows[i];
            if (d.Rt == null) { _recDyingRows.RemoveAt(i); continue; }
            d.T += dt;
            float k = MoSmall > 0f ? Mathf.Clamp01(d.T / MoSmall) : 1f;
            if (d.Group != null) d.Group.alpha = 1f - k;
            d.Rt.anchoredPosition = new Vector2(d.Rt.anchoredPosition.x, d.Rt.anchoredPosition.y + RecDyingRise * dt);
            if (k >= 1f) {
                Object.Destroy(d.Rt.gameObject);
                _recDyingRows.RemoveAt(i);
            }
        }
    }

    /// <summary>列表壳连同行一起没了（整块画布重建）时把身份表作废：
    /// 那些身份指向的是已经销毁的矩形，留着会让新行从死行的旧位置滑过来。</summary>
    private static void RecFlowForgetAll(string prefix, Dictionary<long, RectTransform> rows) {
        foreach (KeyValuePair<long, RectTransform> kv in rows) FlowForgetKey(prefix + kv.Key);
        rows.Clear();
    }
}
