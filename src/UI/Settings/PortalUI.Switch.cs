using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>全项目唯一的开关外观实现（设置页 / 批量导入弹窗 / 删除弹窗都走这里）。
/// 外形 = 胶囊轨道 + 圆钮：轨宽 = 轨高 × 1.84、钮径 = 轨高 − 2×留白，整体缩放只改 SwitchH，其余按比例推出来。
/// 关 = 深灰轨道 + 白钮在左；开 = 主题色轨道 + 白钮在右。
/// 尺寸、配色、滑动速度都只在这一个文件里；三处界面只负责说「点了一下、目标状态是这个」。</summary>
public static partial class PortalUI {

    // ---------- 尺寸（只改这几个数就能整体变大变小） ----------
    // 只说「大小」时只动 SwitchH —— 宽 / 圆钮 / 留白都是按比例从它推出来的，比例不能动。
    private const float SwitchW = 59f;                              // 轨道宽 = 轨高 × 1.84
    private const float SwitchH = 32f;                              // 轨道高：一行 54 高里放着合适，比 38 的行按钮矮一点
    private const float SwitchPad = 5f;                             // 圆钮到轨道边的留白 = 轨高 × 0.156
    private const float SwitchKnob = SwitchH - SwitchPad * 2f;      // 圆钮直径 = 轨高 − 2×留白（= 22）
    private const float SwitchRightMargin = 10f;                    // 贴行右边缘的留白；与 RowButton 的对齐线一致
    /// <summary>圆钮从左到右的行程 = 轨道宽 − 2×留白 − 圆钮直径。</summary>
    private const float SwitchTravel = SwitchW - SwitchPad * 2f - SwitchKnob;

    // 关态轨道色（设计稿 rgb(49,51,56)），留一点透明让面板背景透出来。
    // 不参与面板透明度滑条：开关是控件，面板拉到最透明时也不该跟着淡到看不见。
    private static readonly Color SwitchOffColor = new Color(0.192f, 0.200f, 0.220f, 0.92f);

    /// <summary>圆钮色（设计稿上的近白 rgb(248,249,250)）；开 / 关都一样。</summary>
    private static readonly Color SwitchKnobColor = new Color(0.973f, 0.976f, 0.980f, 1f);

    // ---------- 贴图（两张都是程序化生成，零素材） ----------
    private static Sprite _switchPillSprite;

    /// <summary>胶囊底：圆角半径 = 轨高 / 2，9-slice 拉宽之后正好是胶囊。
    /// 贴图 64×64、border = 半径，中间可拉伸区 64 − 2×半径（= 32px），四角是原尺寸绘制、不变形。</summary>
    private static Sprite SwitchPillSprite {
        get {
            if (_switchPillSprite == null) _switchPillSprite = CreateRoundedSprite(64, SwitchH * 0.5f);
            return _switchPillSprite;
        }
    }

    private static Sprite _switchKnobSprite;

    /// <summary>圆钮：整圆（半径 = 边长 / 2）。
    /// 这一张必须用 Image.Type.Simple，不能 sliced：半径等于半边长时可拉伸区正好为 0；
    /// 用 Simple 缩到 22px 靠双线性采样自动抗锯齿，比在 22px 上现画一个圆更干净。</summary>
    private static Sprite SwitchKnobSprite {
        get {
            if (_switchKnobSprite == null) _switchKnobSprite = CreateRoundedSprite(64, 32f);
            return _switchKnobSprite;
        }
    }

    // ---------- 画 ----------

    /// <summary>把开关画在进度 k 上：k=0 完全关，k=1 完全开，中间就是滑到一半。
    /// 颜色与圆钮位置只在这里算一份 —— 建的时候、点的时候、动画每帧、重刷，全走它。</summary>
    private static void PaintSwitch(Image track, Image knob, float k) {
        if (track != null) track.color = Color.Lerp(SwitchOffColor, Accent, k);
        if (knob != null) {
            // 圆钮贴左（关）⇄ 贴右（开）：中心 x = 留白 + 半径 + 行程 × k
            float x = SwitchPad + SwitchKnob * 0.5f + SwitchTravel * k;
            knob.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }
    }

    /// <summary>圆钮当前进度（0~1），从坐标反算而不另存状态：动画被打断也能接着走。</summary>
    private static float SwitchKOf(Image knob) {
        if (knob == null) return 0f;
        float x = knob.rectTransform.anchoredPosition.x - (SwitchPad + SwitchKnob * 0.5f);
        return Mathf.Clamp01(x / SwitchTravel);
    }

    // ---------- 滑动动画 ----------

    /// <summary>滑完一趟用多久（手感值，比 iOS 那种 0.25s 稍快）；想更快 / 更慢只改这一个数。</summary>
    private const float SwitchMoveSeconds = MoSmall;

    /// <summary>单帧最多推进多少：卡一下两三百毫秒的帧不许把整段动画一步冲完，否则又变成闪现。
    /// 与面板淡入的 UiFadeMaxStep 同一个道理。</summary>
    private const float SwitchMoveMaxStep = 0.05f;

    /// <summary>正在滑的开关；数量通常 0~1，列表一空就零开销。</summary>
    private sealed class SwitchMove {
        public Image Track;
        public Image Knob;
        public float From, To, T;      // T = 已经滑了多久（秒）
    }

    private static readonly List<SwitchMove> _switchMoves = new List<SwitchMove>();

    /// <summary>这个开关是不是正在滑（按同一个圆钮实例找）。</summary>
    private static SwitchMove FindSwitchMove(Image knob) {
        if (knob == null) return null;
        for (int i = 0; i < _switchMoves.Count; i++) if (_switchMoves[i].Knob == knob) return _switchMoves[i];
        return null;
    }

    /// <summary>画到给定状态；只有玩家点出来的变化才传 animate=true（那才滑过去）。</summary>
    private static void ApplySwitch(Image track, Image knob, bool on, bool animate = false) {
        float want = on ? 1f : 0f;
        var mv = FindSwitchMove(knob);
        if (!animate) {
            // 点击后紧接着的统一刷新会以 animate=false 走这里：正在滑的开关只改目标，
            // 直接拍到位会把刚起步的动画掐掉（又变回闪现）。
            if (mv != null) { mv.From = SwitchKOf(knob); mv.To = want; mv.T = 0f; return; }
            PaintSwitch(track, knob, want);
            return;
        }
        float now = SwitchKOf(knob);
        if (Mathf.Abs(now - want) < 0.002f) {                 // 本来就在目标位置，没什么可滑的
            if (mv != null) _switchMoves.Remove(mv);
            PaintSwitch(track, knob, want);
            return;
        }
        if (mv == null) { mv = new SwitchMove { Knob = knob }; _switchMoves.Add(mv); }
        mv.Track = track;
        mv.From = now;                                        // 从它现在真正所在的位置起步
        mv.To = want;
        mv.T = 0f;
    }

    /// <summary>让某个开关滑到目标状态，只有玩家点出来的状态变化才调它。
    /// 建的时候 / 切页统一刷新 / 换主题色重刷一律即时：那些也滑的话，打开设置页会看见 19 个开关一起飘。</summary>
    private static void AnimateSwitch(Button b, bool on) {
        if (b == null) return;
        ApplySwitch(b.GetComponent<Image>(), SwitchKnobOf(b), on, true);
    }

    /// <summary>推进正在滑的开关，必须挂在 TickCore 常驻段 —— 批量导入进度窗这类弹窗是独立画布，
    /// 面板关着的时候照样开着，挂 TickPanel 就会「关掉面板后圆钮停在半路」；没活时只花一次 Count 判断。</summary>
    private static void TickSwitchMoves() {
        if (_switchMoves.Count == 0) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, SwitchMoveMaxStep);
        for (int i = _switchMoves.Count - 1; i >= 0; i--) {
            var m = _switchMoves[i];
            if (m == null || m.Knob == null) { _switchMoves.RemoveAt(i); continue; }
            m.T += dt;
            float t = Mathf.Clamp01(m.T / SwitchMoveSeconds);
            float e = 1f - (1f - t) * (1f - t) * (1f - t);    // easeOutCubic（起步快、收尾稳）
            PaintSwitch(m.Track, m.Knob, Mathf.Lerp(m.From, m.To, e));
            if (t >= 1f) _switchMoves.RemoveAt(i);
        }
    }

    /// <summary>按名字找开关上的圆钮，不另外维护注册表；只在刷新 / 点击那一下查，不在每帧路径上。</summary>
    private static Image SwitchKnobOf(Button b) {
        if (b == null) return null;
        var t = b.transform.Find("Knob");
        return t != null ? t.GetComponent<Image>() : null;
    }

    /// <summary>只画轨道 + 圆钮，不带交互 —— 给「整行是一个按钮」的弹窗开关用：
    /// 那一行点哪儿都算，所以两片都关掉射线，由外面那层 Button 接。
    /// 默认贴行右边缘、垂直居中；建完自己 ApplySwitch 一次（默认关态）。</summary>
    private static Image MakeSwitchVisual(Transform parent, string name, out Image knob) {
        var track = MakeImage(parent, name, SwitchOffColor);
        track.sprite = SwitchPillSprite;
        track.type = Image.Type.Sliced;
        track.raycastTarget = false;
        var trt = track.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(SwitchW, SwitchH);
        // 锚点 / pivot 都是中心，所以这个值代表中心不是右边缘（右边缘要再让出半个宽）
        trt.anchoredPosition = new Vector2(-(SwitchRightMargin + SwitchW * 0.5f), 0f);

        knob = MakeImage(track.transform, "Knob", SwitchKnobColor);
        knob.sprite = SwitchKnobSprite;
        knob.type = Image.Type.Simple;
        knob.raycastTarget = false;
        var krt = knob.rectTransform;
        krt.anchorMin = krt.anchorMax = new Vector2(0f, 0.5f);   // 圆钮按左边缘定位，画的时候只改 x
        krt.pivot = new Vector2(0.5f, 0.5f);
        krt.sizeDelta = new Vector2(SwitchKnob, SwitchKnob);
        ApplySwitch(track, knob, false);
        return track;
    }

    /// <summary>建一个可点的开关（整块轨道就是一个 Button）；尺寸 / 位置 / 配色都在 MakeSwitchVisual 里，
    /// 这里只补「能点」这一层。建出来就是关态，要画成别的状态用 ApplySwitch / Init(btn, on)。</summary>
    private static Button MakeSwitch(Transform parent, string name) {
        var track = MakeSwitchVisual(parent, name, out _);   // 圆钮建好即关态；单独操作它时用 SwitchKnobOf
        track.raycastTarget = true;                 // 只有可点的那种才吃射线
        var le = track.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = SwitchW; le.preferredHeight = SwitchH;
        le.minWidth = SwitchW; le.minHeight = SwitchH;
        var btn = track.gameObject.AddComponent<Button>();
        btn.targetGraphic = track;
        // 悬浮 / 按下的反馈用亮度（ColorTint 是乘算，白色 = 保持原色）；
        // 不能照抄 MakeButton 那套 ItemHover —— 它带 0.75 的 alpha，会把轨道乘成半透明。
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = colors;
        return btn;
    }

    /// <summary>弹窗里「一行标签 + 右侧开关」的标签右边界 = 开关右边距 + 开关宽 + 16px 空隙（距行右边缘）。
    /// 必须由那几个常量推出来：硬写数字的话尺寸一改就会压到开关上。</summary>
    private const float SwitchRowLabelInset = SwitchW + SwitchRightMargin + 16f;
}
