using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：LevelList.Snap（列表居中吸附）
///
/// 滚动停下来之后，把离视口中心最近的一条谱面行动画滑到视口正中间（scroll-snap），
/// 落定后自动选中那一行（左侧详情区跟着换）。
/// 吸附动画走自己的时长与缓出：0.35~0.9 秒按距离给、五次缓出 —— 比字母定位那档
/// （0.16~0.40 秒、三次缓出）更长更绵，是"慢慢滑到、轻轻停稳"，不是"咔一下到位"。
/// 只加这一个行为，其余滚动体系（像素滚轮 / 拖拽惯性 / 字母条定位 / 行点击选中）原样不动。
///
/// 四条铁律（都在代码里落了闸）：
/// ① 唯一写手：吸附动画启动那一刻必须清掉残余惯性并 StopMovement ——
///    惯性在 |速度|&gt;1 时仍每帧写内容位置，不清就是动画和惯性同时写一个属性，来回拉扯（实测抽搐）；
/// ② 程序发起的滚动不吸附：字母条定位（StartScrollAnimation）落点是"标头顶在视口顶"，
///    吸附跟在它后面会把列表再拽半屏，等于把玩家点字母的定位毁掉。所以定位动画收工后
///    必须等下一次真实的用户滚动输入（滚轮 / 拖拽）才重新 armed；列表重建（搜索/排序/导入）同理；
/// ③ 时机：只在"没有动画、没有拖拽、惯性基本停了、距上次输入过了 0.15 秒"时启动，
///    钉顶 / 恢复滚动位置那两段开局流程也不掺和；
/// ④ 落定选中只跟吸附走：动画被用户输入打断（CancelScrollAnimation）或列表重建，
///    "落定后选中"的挂起标志一并作废 —— 只有完整滑到底的那一次才选中。
///    缺文件的谱面不自动选（SelectRow 会弹缺失弹窗，滚过去弹一个窗没法看）。</summary>
public static partial class PortalUI {

    /// <summary>停止输入这么多秒后才吸附（滚轮连击的间隔内不抢方向盘）</summary>
    private const float ListSnapDelay = 0.15f;
    /// <summary>惯性速度低于它（像素/秒）才算"停了"。清零残余这点速度视觉上无感（&lt;1px/帧）</summary>
    private const float ListSnapInertiaIdle = 40f;
    /// <summary>离中心已经这么近（像素）就不动，免得为半像素起一段动画</summary>
    private const float ListSnapMinPx = 2f;
    /// <summary>吸附动画时长：0.35s 起步，距离越长越久，最远 0.9s 封顶（配五次缓出）</summary>
    private const float ListSnapDurMin = 0.35f;
    private const float ListSnapDurMax = 0.9f;
    private const float ListSnapDurPerPx = 1f / 4000f;

    /// <summary>最近一次用户滚动输入（滚轮 / 拖拽）的时刻；负一万年 = 从未输入</summary>
    private static float _listSnapLastInputT = -10000f;
    /// <summary>要不要等下一次真实用户输入才允许吸附。
    /// 列表重建后、字母定位动画收工后置真 —— 那两种落点都不是"滑到一半停下的"，不该被吸附改写</summary>
    private static bool _listSnapWaitUserInput = true;
    /// <summary>吸附动画落定后要不要选中中间那行（动画启动时置真，收工/被打断时消费掉）</summary>
    private static bool _listSnapSelectOnFinish;

    /// <summary>用户滚了一下（滚轮 / 拖拽移动）：重新武装吸附</summary>
    private static void MarkListSnapInput() {
        _listSnapLastInputT = Time.unscaledTime;
        _listSnapWaitUserInput = false;
    }

    /// <summary>列表重建 / 面板新开：吸附先收起，等玩家真的滚过一次再说。
    /// 同时把输入时刻刷到现在，免得重建完 0.15 秒就自己动一下</summary>
    private static void ListSnapReset() {
        _listSnapLastInputT = Time.unscaledTime;
        _listSnapWaitUserInput = true;
        _listSnapSelectOnFinish = false;
    }

    /// <summary>字母定位那类程序动画收工时调：吸附收起（等下一次用户输入）。
    /// 吸附自己的动画收工也会走到这 —— 正好：已经居中了，不需要连环吸附。
    /// 挂着"落定后选中"标志的那一趟（= 吸附）收工时，当场选中正停在中间的那行</summary>
    private static void ListSnapAfterProgrammaticScroll() {
        _listSnapWaitUserInput = true;
        if (_listSnapSelectOnFinish) {
            _listSnapSelectOnFinish = false;
            ListSnapSelectCentered();
        }
    }

    /// <summary>吸附落定：选中正停在视口中间的那行（走 SelectRow，详情区/去特效页/寄录图全线联动）。
    /// 已选中的是空操作；缺文件不选（不弹窗）；找不到真正居中的行（理论不会）也安静返回</summary>
    private static void ListSnapSelectCentered() {
        try {
            var scroll = _libScroll;
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            float vpH = scroll.viewport.rect.height;
            if (vpH <= 1f) return;
            float visTop = Mathf.Abs(scroll.content.anchoredPosition.y);
            float centerY = visTop + vpH * 0.5f;
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _logicalRows.Count; i++) {
                var lr = _logicalRows[i];
                if (lr.IsHeader || lr.Entry == null) continue;
                float d = Mathf.Abs(lr.OffsetY + lr.Height * 0.5f - centerY);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0 || bestD > ItemRowH * 0.5f) return;   // 中间没有条目行（空列表/极端情况）
            var e = _logicalRows[best].Entry;
            if (e != null && _selectedPath != e.FolderPath && File.Exists(e.FilePath)) SelectRow(e);
        } catch (System.Exception ex) { Lg.Ex("吸附落定选中出错", ex); }
    }

    /// <summary>每帧驱动（TickPanel 调，排在 TickScrollAnimation 之后）：条件齐了就把最近的一行滑到正中</summary>
    private static void TickListSnap() {
        try {
            // 只在谱面页（别的页列表藏在页滑动后面，吸附了也看不见）
            if (_topTab != 1) return;
            var scroll = _libScroll;
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            // 忙时不吸附：动画进行中 / 正在拖 / 开局的钉顶与恢复位置流程
            if (_scrollAnim || _dragActive || _libPinTop || _libScrollRestoreFrames > 0) return;
            if (_listSnapWaitUserInput) return;                    // 等真实用户输入（字母定位/重建后）
            if (Mathf.Abs(_inertiaVel) >= ListSnapInertiaIdle) return;   // 惯性还没停
            if (Time.unscaledTime - _listSnapLastInputT < ListSnapDelay) return;
            float vpH = scroll.viewport.rect.height;
            float contentH = scroll.content.rect.height;
            if (vpH <= 1f || contentH <= vpH + 1f) return;          // 没得滚就没什么可吸
            float visTop = Mathf.Abs(scroll.content.anchoredPosition.y);
            float centerY = visTop + vpH * 0.5f;
            // 找离视口中心最近的条目行（标头不吸附 —— 吸到"A"/"★ 收藏"上没有意义）
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _logicalRows.Count; i++) {
                var lr = _logicalRows[i];
                if (lr.IsHeader || lr.Entry == null) continue;
                float d = Mathf.Abs(lr.OffsetY + lr.Height * 0.5f - centerY);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0 || bestD < ListSnapMinPx) return;          // 空列表 / 已经居中
            float targetY = _logicalRows[best].OffsetY + _logicalRows[best].Height * 0.5f - vpH * 0.5f;
            targetY = Mathf.Clamp(targetY, 0f, contentH - vpH);
            float dist = Mathf.Abs(targetY - visTop);
            if (dist < ListSnapMinPx) return;
            // 唯一写手：残余惯性清零 + 停掉 ScrollRect 自带的弹性，位置只归吸附动画写
            _inertiaVel = 0f;
            scroll.StopMovement();
            // 自己的时长与缓出：更长更绵（五次缓出收尾），落定后选中中间那行
            float dur = Mathf.Clamp(ListSnapDurMin + dist * ListSnapDurPerPx, ListSnapDurMin, ListSnapDurMax);
            _listSnapSelectOnFinish = true;
            StartScrollAnimation(scroll, targetY, dur, 5f);
        } catch (System.Exception e) { Lg.Ex("列表居中吸附出错", e); }
    }
}
