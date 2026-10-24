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

/// <summary>列表项悬停/选中变色。行底色只有这一个写手：进出与按下事件只改标志位，
/// 目标色一律由 TargetColor 现算一次。行是对象池复用的（同一根行会被换去显示别的谱面），
/// 若把颜色写死在事件里，刷新那一份就会把悬停色盖掉、而指针没动不会再补一次进出事件，
/// 表现就是鼠标底下那一行忽亮忽灭。</summary>
public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                           IPointerDownHandler, IPointerUpHandler {
    public Color Normal = Color.white;
    public bool IsSelected;
    /// <summary>这一行是「刚导入还没点开过」的谱面：常态用提示色亮一下。
    /// 它只是提示 —— 指针一压上来就让位给悬停色（见 Apply），玩家看过这一眼就够了。</summary>
    public bool IsNew;
    private Graphic _img;      // 只取自身那张图，认 Graphic 而不是 Image（行底将来换成非 Image 图元也能用）；
                               // 挂在行根、图在子物体时，变色会落到行根的透明命中图上
    private bool _hovered;     // 指针是不是正压在这一行上
    private bool _pressed;     // 左键是不是正按在这一行上（按下反馈也归这里，行按钮关掉了自带的染色）

    private static readonly Color HoverColor = new Color(0.28f, 0.23f, 0.42f, 1f);
    private static readonly Color SelectedColor = new Color(1f, 0.55f, 0.75f, 1f);
    /// <summary>「刚导入」的提示色：比选中色淡一档并偏冷，与选中态一眼能分开
    ///（它表示"新来的"，不是"你现在选着它"）</summary>
    public static readonly Color NewColor = new Color(0.45f, 0.95f, 0.75f, 0.55f);
    private const float PressMul = 0.86f;    // 按下时整体压暗的比例（与大厅那几块同一个数）

    // ---------- 颜色过渡（淡入淡出）----------
    // 真实的状态变化（选中 / 悬停 / 按下）走 Apply：显示色往目标色指数平滑地滑 ——
    // 选中粉的进出于是是淡入淡出，吸附落定选中 / 点选切换那一帧不再"咔一下变粉"。
    // 对象池换人 / 初建 / 移出视口复位走 ApplyInstant：直接落位 ——
    // 换人那一帧如果也滑，高速滚动时上一任的选中粉会作为残影滑进这一任
    //（uGUI 自带 ColorTint 当年就是因为这个被关掉的，见 CreatePool 里的注释）。
    // Update 只在"还有距离没走完"那一小段干活，静止时一个字节都不碰。
    private Color _shown;         // 当前正在显示的颜色
    private bool _shownValid;     // 有没有"从哪来"（首次没有，直接落位）
    private bool _fading;         // 过渡进行中（Update 的开关）
    private const float FadeK = 12f;        // 指数平滑系数（≈0.19s 走完九成，与界面小件让位那档手感一致）
    private const float FadeSnap = 0.004f;  // 每通道差小于它就落位收工（指数平滑自己到不了 exact）

    /// <summary>按当前状态算目标色；优先级：选中 &gt; 悬停 &gt; 新导入提示 &gt; 常态，按下再压一层。
    /// 「提示色让位给悬停」是用户点名要的：鼠标压上去就换成悬停色，移开才回到提示色</summary>
    private Color TargetColor() {
        Color c;
        if (IsSelected) c = SelectedColor;
        else if (_hovered) c = HoverColor;
        else if (IsNew) c = NewColor;
        else c = Normal;
        if (_pressed) c = new Color(c.r * PressMul, c.g * PressMul, c.b * PressMul, c.a);
        return c;
    }

    /// <summary>状态变了之后调（外部改完 IsSelected / IsNew / Normal 用它）：显示色往新目标淡入淡出地滑</summary>
    public void Apply() {
        if (_img == null) _img = GetComponent<Graphic>();
        if (_img == null) return;
        if (!_shownValid) { ApplyInstant(); return; }   // 首次没有"从哪来"，直接落位
        _fading = true;                                  // 推进在 Update 里（见上）
    }

    /// <summary>直接落位（池换人 / 初建 / 复位用）：不滑、立刻就是目标色</summary>
    public void ApplyInstant() {
        if (_img == null) _img = GetComponent<Graphic>();
        if (_img == null) return;
        _shown = TargetColor();
        _shownValid = true;
        _fading = false;
        _img.color = _shown;
    }

    /// <summary>过渡推进。常驻 Update 不许抛异常：全程兜住，出错就收工（下一帧静止不动了）</summary>
    private void Update() {
        if (!_fading) return;
        try {
            if (_img == null) { _fading = false; return; }
            Color t = TargetColor();   // 目标每帧现算：过渡途中状态又变了（选中还没淡完又悬停）也不偏航
            float k = 1f - Mathf.Exp(-FadeK * Time.unscaledDeltaTime);
            _shown = Color.Lerp(_shown, t, k);
            if (Mathf.Abs(_shown.r - t.r) < FadeSnap && Mathf.Abs(_shown.g - t.g) < FadeSnap
                && Mathf.Abs(_shown.b - t.b) < FadeSnap && Mathf.Abs(_shown.a - t.a) < FadeSnap) {
                _shown = t;
                _fading = false;
            }
            _img.color = _shown;
        } catch { _fading = false; }
    }

    /// <summary>行被移出视口 / 换人时调：指针离开与松手的事件在「行被挪走」那一帧不一定会补发，
    /// 不清掉的话这行滚回来时会自带悬停色、或者一直是压暗的。</summary>
    public void ClearPointerState() {
        if (!_hovered && !_pressed) return;
        _hovered = false;
        _pressed = false;
        ApplyInstant();   // 复位场景：直接落位，不留过渡
    }

    public void OnPointerEnter(PointerEventData eventData) {
        _hovered = true;
        Apply();
    }

    public void OnPointerExit(PointerEventData eventData) {
        _hovered = false;
        _pressed = false;
        Apply();
    }

    public void OnPointerDown(PointerEventData eventData) {
        _pressed = true;
        Apply();
    }

    public void OnPointerUp(PointerEventData eventData) {
        _pressed = false;
        Apply();
    }
}
