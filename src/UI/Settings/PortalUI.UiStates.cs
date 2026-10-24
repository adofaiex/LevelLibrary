// LevelLibrary 的交互态层：把"能点的东西必须有看得见的反馈"做成一处实现。
// 走 MakeButton / HomeButton 建出来的按钮自动登记；每帧只做两件事：找出鼠标下那一个、
// 把颜色推向目标值（悬停提亮 / 按下压暗 / 禁用降透明度，都从按钮自己的底色推）。
// 刻意不用 uGUI 的 OnPointerEnter/Exit：切页把它藏起来时不会补发 exit，会停在亮着那一态。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {
 /// <summary>一个可交互目标的常态色与当前过渡进度（进度只在这几个数上动，逐帧不分配）</summary>
    private sealed class UiTarget {
        public Button Btn;
        public Image Fill;          // 变色改它
        public Graphic Accent;      // 按钮里的图标 / 文字（幽灵按钮就只有它可见）
        public Color BaseFill;      // 常态底色
        public Color BaseAccent;    // 常态图标 / 文字色
        public float K;             // 悬停过渡 0..1
        public float Press;         // 按下过渡 0..1
        /// <summary>悬停时底色要到的透明度（0 = 按老规矩提亮底色）。
        /// 给底色全透明的按钮用：它们看得见的变化只能靠一层很淡的膜，文字不能碰。</summary>
        public float HoverFillA;
    }

    private static readonly List<UiTarget> _uiTargets = new List<UiTarget>(160);
    private static UiTarget _uiHot;
    private static bool _uiHoverLogged;   // 首次命中报一次，用来判定这一层有没有跑起来

 /// <summary>悬停 / 按下的过渡时长：走设计尺（改手感改 PortalUI.Design.cs 那两个数）</summary>
    private const float UiHoverSeconds = MoHover;
    private const float UiPressSeconds = MoPress;
 /// <summary>悬停时底色提亮多少（加到 RGB 上，并给 alpha 也加一点）——比 uGUI 那套乘算看得清</summary>
    private const float UiHoverLift = 0.13f, UiHoverAlphaLift = 0.10f;
 /// <summary>按下时整体压暗的比例（跟大厅那些块同一个数）</summary>
    private const float UiPressMul = 0.86f;
 /// <summary>禁用时的整体透明度</summary>
    private const float UiDisabledAlpha = 0.45f;

 /// <summary>登记一个按钮（由共用的建按钮入口调，别在业务里散着调）。
 /// fill 传按钮自己那张底图，accent 传它里面的图标/文字（幽灵按钮只有这个可见，可空）。</summary>
    private static void RegisterUiTarget(Button btn, Image fill, Graphic accent = null) {
        if (btn == null || fill == null) return;
        for (int i = 0; i < _uiTargets.Count; i++) if (_uiTargets[i].Btn == btn) return;   // 已经登记过
        var t = new UiTarget {
            Btn = btn, Fill = fill, Accent = accent,
            BaseFill = fill.color,
            BaseAccent = accent != null ? accent.color : Color.white,
        };
        _uiTargets.Add(t);
    }


 /// <summary>给已经登记过的按钮补挂"悬停时一起提亮的那个图形"（一般是它里面的文字或图标）。
 /// 透明底的按钮只提亮底色是看不见的（0 → 0.10 的一点白），要提亮的其实是那行字。</summary>
    private static void AttachUiAccent(Button btn, Graphic g) {
        if (btn == null || g == null) return;
        for (int i = 0; i < _uiTargets.Count; i++) {
            var t = _uiTargets[i];
            if (t.Btn != btn) continue;
            t.Accent = g;
            t.BaseAccent = g.color;
            return;
        }
    }

    /// <summary>给某类按钮指定悬停时底色要到的透明度（透明底按钮专用）。
    /// 只改这一个目标自己的悬停行为，不动提亮那套默认值，也不再碰它的文字。</summary>
    private static void SetUiHoverFill(Button btn, float alpha) {
        if (btn == null) return;
        for (int i = 0; i < _uiTargets.Count; i++) {
            if (_uiTargets[i].Btn != btn) continue;
            _uiTargets[i].HoverFillA = alpha;
            _uiTargets[i].Accent = null;
            return;
        }
    }

 /// <summary>底色/图标色被业务改过之后重新记一次常态色（换主题色、换选中态时调）。
 /// 不调的话"常态"还是旧的，悬停结束会弹回一个过时的颜色。</summary>
    private static void RefreshUiTargetBase(Button btn, bool fillToo = true) {
        if (btn == null) return;
        for (int i = 0; i < _uiTargets.Count; i++) {
            var t = _uiTargets[i];
            if (t.Btn != btn) continue;
            // fillToo = false 时只重记文字与图标那一半：底色那一半此刻可能正被悬停改着，
            // 记下来会把悬停中的颜色当成常态（刷新那一刻鼠标常正停在刚点的那个 tab 上）。
            if (fillToo) t.BaseFill = t.Fill != null ? t.Fill.color : t.BaseFill;
            t.BaseAccent = t.Accent != null ? t.Accent.color : t.BaseAccent;
            return;
        }
    }

 /// <summary>业务自己改了底色之后，直接把"常态色"告诉交互态层。
 /// 比 RefreshUiTargetBase 更准：鼠标正停在那个按钮上时，读"当前颜色"读到的是悬停色
 ///（大肥鱼下拉收行那一下正好会这样：鼠标还停在刚确认的那一行上）。</summary>
    private static void SetUiBaseFill(Button btn, Color c) {
        if (btn == null) return;
        for (int i = 0; i < _uiTargets.Count; i++) {
            if (_uiTargets[i].Btn != btn) continue;
            _uiTargets[i].BaseFill = c;
            return;
        }
    }

    /// <summary>业务改按钮底色（选中态高亮那一套）的唯一入口：写常态色 + 立刻按当前过渡进度刷一次。
    /// 为什么不能直接写 Image.color：交互态层每帧都从自己记的 BaseFill 重算并覆写这张图，
    /// 直接写等于跟它抢同一个颜色 —— 鼠标一动（K 变化触发重算）业务写的那个色就被旧常态色顶掉。
    /// 表现出来就是：点一下选中色闪一下就没了、鼠标移开原本的颜色变回透明（ItemColor 只有 0.55 透明度）。
    /// text 非空时连按钮里的文字/图标色一起当常态色记下（HomeButton 登记过 accent 的那些）。
    /// 没登记过的按钮退回直接写图元色，不当哑巴。</summary>
    private static void SetUiSelectFill(Button btn, Color c, Graphic text = null) {
        if (btn == null) return;
        for (int i = 0; i < _uiTargets.Count; i++) {
            var t = _uiTargets[i];
            if (t.Btn != btn) continue;
            t.BaseFill = c;
            if (text != null) t.BaseAccent = text.color;
            ApplyUiTarget(t);       // 立刻按当前 K / Press 重算：否则这一帧还是旧颜色，要等下一次状态变化
            return;
        }
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = c;
    }

 /// <summary>每帧（常驻段调）：找出鼠标下那一个按钮，其余的全部退回常态。
 /// 没有登记过任何目标时第一行就返回（常数开销）。</summary>
    private static void TickUiStates() {
        if (_uiTargets.Count == 0) return;
        var mouse = UiInput.MousePosition;
        bool down = UiInput.MouseButton(0);
        bool blocked = PanelInputBlocked();
        UiTarget hot = null;
        for (int i = _uiTargets.Count - 1; i >= 0; i--) {
            var t = _uiTargets[i];
            if (t == null || t.Btn == null) { _uiTargets.RemoveAt(i); continue; }   // 页面重建过，顺手清掉
            if (!t.Btn.gameObject.activeInHierarchy || !t.Btn.interactable) continue;
            if (t.Fill == null || t.Fill.gameObject == null) { _uiTargets.RemoveAt(i); continue; }
        }
        if (!blocked) {
            for (int i = 0; i < _uiTargets.Count; i++) {
                var t = _uiTargets[i];
                if (t.Btn == null || !t.Btn.gameObject.activeInHierarchy || !t.Btn.interactable) continue;
                var rt = t.Btn.transform as RectTransform;
                if (rt == null) continue;
                if (!MouseInRect(rt)) continue;
                hot = t;                     // 后建的盖在上面：取最后一个命中的
            }
        }
        if (hot != _uiHot) {
            _uiHot = hot;
            if (hot != null && !_uiHoverLogged) {
                _uiHoverLogged = true;
                Lg.Info("交互态层：首次悬停命中「" + hot.Btn.gameObject.name + "」");
            }
        }
        float hoverStep = Time.unscaledDeltaTime / UiHoverSeconds;
        float pressStep = Time.unscaledDeltaTime / UiPressSeconds;
        for (int i = 0; i < _uiTargets.Count; i++) {
            var t = _uiTargets[i];
            if (t.Btn == null || t.Fill == null) continue;
            bool on = t == _uiHot;
            bool held = on && down;
            float wk = on ? 1f : 0f;
            float wp = held ? 1f : 0f;
   // 已经到位且没有需要写的东西就跳过（绝大多数帧走这条）
            if (Mathf.Abs(t.K - wk) < 0.001f && Mathf.Abs(t.Press - wp) < 0.001f) continue;
            t.K = Mathf.MoveTowards(t.K, wk, hoverStep);
            t.Press = Mathf.MoveTowards(t.Press, wp, pressStep);
            ApplyUiTarget(t);
        }
    }

 /// <summary>把一个目标当前该有的颜色算出来写下去（唯一写颜色的地方）。</summary>
    private static void ApplyUiTarget(UiTarget t) {
        bool usable = t.Btn != null && t.Btn.interactable;
        float dis = usable ? 1f : UiDisabledAlpha;
        float lift = UiHoverLift * t.K;
        float mul = Mathf.Lerp(1f, UiPressMul, t.Press);
        var c = t.BaseFill;
        var fill = new Color(
            Mathf.Clamp01((c.r + lift) * mul),
            Mathf.Clamp01((c.g + lift) * mul),
            Mathf.Clamp01((c.b + lift) * mul),
            t.HoverFillA > 0f
                    ? Mathf.Clamp01(Mathf.Lerp(c.a, t.HoverFillA, t.K) * dis)
                    : Mathf.Clamp01((c.a + UiHoverAlphaLift * t.K) * dis));
        if (t.Fill != null) t.Fill.color = fill;
        if (t.Accent != null) {
            var a = t.BaseAccent;
   // 图标/文字只吃"提亮 + 禁用"，不跟着压暗（字跟着压暗会糊）
            var ac = new Color(
                Mathf.Clamp01(a.r + lift * 1.6f),
                Mathf.Clamp01(a.g + lift * 1.6f),
                Mathf.Clamp01(a.b + lift * 1.6f),
                Mathf.Clamp01(a.a * dis));
            t.Accent.color = ac;
        }
    }

}
