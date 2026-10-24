using UnityEngine;

namespace LevelLibrary;

/// <summary>玩家信息栏的拖动（面板内 / 界面外两份框共用同一套逻辑）。
/// 自己读鼠标而不走 uGUI 拖拽：界面外那份挂在独立画布上、所有 Image 的 raycastTarget 都关了，天生收不到拖拽事件。
/// Tick 必须挂常驻段 —— 界面外那份在面板关着的时候也显示着，挂 TickPanel 就拖不动。
/// 写法：零瞬移 —— 按下后框一动不动，从手真正动起来那一刻起，框跟着鼠标平移同样的距离（1:1 跟手）。
/// 靠的是开始拖那一帧记下的"抓取偏移"（见 `_pbGrab`）：鼠标在哪、框就相对它平移多远，绝不把框吸到鼠标点上。</summary>
public static partial class PortalUI {
    private static bool _pbPressed;          // 已按在框上（还没开始移动）
    private static bool _pbDragging;         // 正在拖
    /// <summary>按下那一刻的鼠标位置（`UiInput.MousePosition` 是 Vector3）：只有真的移动了才进入拖动。
    /// 这一条替代了原来的"长按 0.3 秒"——它管的是"点一下别把框挪走"（大肥鱼那边也是只有拖动事件才会改位置），
    /// 但不等任何时间：手一动就当场跟手。阈值取 Unity 拖拽那套量级（几个像素），1px 的手抖不该把框弹到鼠标底下。</summary>
    private static Vector3 _pbPressPos;
    /// <summary>手要移动多少像素才算"要拖了"（取代原来那 0.3 秒的长按等待）</summary>
    private const float PbDragStartPx = 4f;
    /// <summary>抓取偏移：开始拖那一帧记下"鼠标 − 框 pivot"（父局部坐标），拖动全程沿用它。
    /// 于是框的位移与鼠标的位移完全相等，鼠标不会把框吸到某个点上（零瞬移）。
    /// 必须在这一刻取、不能在按下那一刻取：按下到真正开始拖之间手可能已经移动了几十像素，
    /// 用按下时的偏移就会在开始拖那一帧把这些距离一次性补跳出去。</summary>
    private static Vector2 _pbGrab;
    /// <summary>正在被拖的那一份（面板内 / 界面外，同一时刻只有一份显示）</summary>
    private static RectTransform _pbDragRt;

    /// <summary>设置页「个人信息」里 X / Y 两条位置滑条的刷新入口（拖动中实时跟、松手后再对齐一次）。
    /// 面板关掉之后它们指向的控件已销毁，RowSlider 的 apply 自己判空早退，这里直接调就行。</summary>
    private static System.Action<float> _pbXSlider, _pbYSlider;

    /// <summary>面板关掉时放掉两个滑条引用（下一次建设置页会重新登记）</summary>
    private static void ClearPlayerBoxSliders() {
        _pbXSlider = null;
        _pbYSlider = null;
    }

    /// <summary>把设置页那两条位置滑条刷成当前值（控件没了就什么也不做）</summary>
    private static void RefreshPlayerBoxPosSliders() {
        try {
            if (_pbXSlider != null) _pbXSlider(PlayerBoxX * 100f);
            if (_pbYSlider != null) _pbYSlider(PlayerBoxY * 100f);
        } catch { }
    }

    /// <summary>当前能拖的那一份框（两份互斥显示）；
    /// 总开关关着 / 「游玩时隐藏」生效着时一个都不给拖。</summary>
    private static RectTransform PlayerBoxDragTarget() {
        if (!ShowPlayerBox) return null;
        if (HidePlayerBoxInGame && IsLevelPlaying()) return null;
        if (ShowPlayerBoxOutside) {
            if (_outsideBoxRt == null) return null;
            return _outsideBoxRt.gameObject.activeInHierarchy ? _outsideBoxRt : null;
        }
        if (_playerBoxRt == null) return null;
        return _playerBoxRt.gameObject.activeInHierarchy ? _playerBoxRt : null;
    }

    /// <summary>框所在画布用的相机（Overlay 画布返回 null）</summary>
    private static Camera PlayerBoxCamera(RectTransform rt) {
        try {
            var cv = rt.GetComponentInParent<Canvas>();
            if (cv == null) return null;
            return cv.renderMode == RenderMode.ScreenSpaceOverlay ? null : cv.worldCamera;
        } catch { return null; }
    }

    /// <summary>框的 pivot 在父局部坐标里的位置 = 锚点位置 + anchoredPosition。</summary>
    private static Vector2 PlayerBoxPivotLocal(RectTransform rt, RectTransform parent) {
        var pr = parent.rect;
        var anchor = new Vector2(pr.xMin + rt.anchorMin.x * pr.width,
                                 pr.yMin + rt.anchorMin.y * pr.height);
        return anchor + rt.anchoredPosition;
    }

    /// <summary>记下"鼠标相对框 pivot 的偏移"（开始拖那一帧记一次，之后全程用它）</summary>
    private static void CachePlayerBoxGrab(RectTransform rt) {
        var parent = rt != null ? rt.parent as RectTransform : null;
        if (parent == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, UiInput.MousePosition, PlayerBoxCamera(rt), out Vector2 m)) return;
        _pbGrab = m - PlayerBoxPivotLocal(rt, parent);
    }

    private static void ResetPlayerBoxDrag() {
        _pbPressed = false;
        _pbDragging = false;
        _pbDragRt = null;
    }

    /// <summary>逐帧驱动（常驻段 —— 面板关着也要能拖界面外那一份）</summary>
    private static void TickPlayerBoxDrag() {
        try {
            // 松手 / 鼠标已经不在按着：无论什么原因（切场景、卡帧、框被销毁）都要能自己走出来。
            // 这条兜底必须做和正常松手完全一样的收尾 —— 少一步（比如转惯性那一步）就是新 bug（同 TickGlobalDrag）。
            if ((_pbPressed || _pbDragging) && !UiInput.MouseButton(0)) { EndPlayerBoxDrag(); return; }

            if (!_pbPressed) {
                if (PanelInputBlocked()) return;             // 上面盖着全屏层（开始界面 / 个人页 / 过渡层）
                if (!UiInput.MouseButtonDown(0)) return;
                var rt = PlayerBoxDragTarget();
                if (rt == null || !MouseInRect(rt)) return;
                _pbPressed = true;
                _pbDragging = false;
                _pbDragRt = rt;
                _pbPressPos = UiInput.MousePosition;
                return;
            }

            // 按下之后手一动就进拖动（没有等待、没有长按）：这一帧直接落到下面当场跟手。
            if (!_pbDragging) {
                if (_pbDragRt == null) { ResetPlayerBoxDrag(); return; }
                var mp = UiInput.MousePosition;
                if (Mathf.Abs(mp.x - _pbPressPos.x) < PbDragStartPx
                    && Mathf.Abs(mp.y - _pbPressPos.y) < PbDragStartPx) return;   // 只是点了一下、手没动 → 别把框挪走
                _pbDragging = true;
                CachePlayerBoxGrab(_pbDragRt);   // 抓取偏移在这一刻取：此刻起 1:1 跟手，而在此之前框一动不动
                // 长按那套已经删了，但"让列表那一路的拖拽让位"仍然需要：按下那一帧 TickGlobalDrag
                // 可能已经把自己置成「在拖列表」，两边同时跟手就会打架。
                if (_dragActive) { _dragActive = false; _dragVel = 0f; }
                Lg.Info("玩家信息栏：手一动就开始拖（照大肥鱼那套：鼠标在哪框在哪）");
            }

            if (_pbDragging) ApplyPlayerBoxDrag();
        } catch (System.Exception e) {
            ReportErrorQuiet("玩家信息栏拖动失败", e);
            ResetPlayerBoxDrag();
        }
    }

    /// <summary>把鼠标位置变成框的新位置（大肥鱼那套：不存抓取偏移，鼠标点就是框的 pivot）。
    /// ① 先把整块框夹在画布内（夹的是框，不是锚点）；
    /// ② 反算锚点 = 框 pivot 位置 − 该档 offset。offset 里带「贴顶让位」的膝盖（Y ≥ 0.99 时再减 150），
    /// 跨过 0.99 那一瞬间档位就变了，所以同一个式子迭代两次，否则框会跳 130px。</summary>
    private static void ApplyPlayerBoxDrag() {
        var rt = _pbDragRt;
        if (rt == null) { ResetPlayerBoxDrag(); return; }
        var parent = rt.parent as RectTransform;
        if (parent == null) { ResetPlayerBoxDrag(); return; }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, UiInput.MousePosition, PlayerBoxCamera(rt), out Vector2 m)) return;

        var pr = parent.rect;
        float w = PlayerBoxSize.x, h = PlayerBoxSize.y;
        if (pr.width < w + 1f || pr.height < h + 1f) return;   // 画布还没量出来（第一帧），这一帧不拖

        // 框跟着鼠标平移同样的距离：鼠标位移多少框就走多少，绝不把框吸到鼠标那个点上。
        // 偏移是开始拖那一帧记的（见 CachePlayerBoxGrab），所以这里减掉它即可。
        Vector2 want = m - _pbGrab;
        want.x = Mathf.Clamp(want.x, pr.xMin + w, pr.xMax);    // pivot 在右上角，框占 [x−w, x]
        want.y = Mathf.Clamp(want.y, pr.yMin + h, pr.yMax);    // 框占 [y−h, y]
        float nx = PlayerBoxX, ny = PlayerBoxY;
        for (int i = 0; i < 2; i++) {
            Vector2 off = PlayerBoxOffsetFor(ny);
            nx = (want.x - off.x - pr.xMin) / Mathf.Max(1f, pr.width);
            ny = (want.y - off.y - pr.yMin) / Mathf.Max(1f, pr.height);
        }
        PlayerBoxX = nx;
        PlayerBoxY = ny;
        ApplyPlayerBoxPosToRt(rt);
        // 另一份（现在藏着的 / 或面板里那份）也一起摆好，不然切换显隐时它会用旧位置闪一下
        ApplyPlayerBoxPosToRt(_playerBoxRt != rt ? _playerBoxRt : null);
        ApplyPlayerBoxPosToRt(_outsideBoxRt != rt ? _outsideBoxRt : null);
        RefreshPlayerBoxPosSliders();   // 设置页那两条滑条实时跟着走
    }

    /// <summary>松手（含上面那条兜底路径）：只有这一步写盘，另加对齐两条滑条与两份框。
    /// 没真的拖过（只是按了一下 / 手没动）就什么都不做，绝不无谓写盘。</summary>
    private static void EndPlayerBoxDrag() {
        bool wasDrag = _pbDragging;
        ResetPlayerBoxDrag();
        if (!wasDrag) return;
        try { AutoFeatures.Save(); } catch (System.Exception e) { ReportErrorQuiet("玩家信息栏位置写盘失败", e); }
        ApplyPlayerBoxPosToRt(_playerBoxRt);
        ApplyPlayerBoxPosToRt(_outsideBoxRt);
        RefreshPlayerBoxPosSliders();
        Lg.Info($"[LevelLib] 玩家信息栏拖动结束：({PlayerBoxX:F3}, {PlayerBoxY:F3})（已落盘）");
    }
}
