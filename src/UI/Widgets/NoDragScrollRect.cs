using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>不自己处理拖动的 ScrollRect。列表的拖动由 `PortalUI.TickGlobalDrag` 统一负责
/// （左键在列表矩形内按下就能拖，卡片上和卡片之间的间隙里都能拖）。
/// 组件本身还得是 ScrollRect —— 滚轮、视觉滚动条、视口裁剪、内容范围都靠它，
/// 这里只把三个拖动回调清空，鼠标拖动这条路就只剩我们那一份实现。
/// 不清空的后果实测过：鼠标按在两个谱面卡片之间的间隙上拖动时列表会抽搐。
/// 为什么只有间隙抽：卡片上按下时命中的是行自己的底色图，而行的按钮上挂着事件触发器，
/// 它实现了拖动接口、会在冒泡路上把拖动接走，ScrollRect 因此碰不到；
/// 间隙处命中的是视口底图，它就在 ScrollRect 这个物体上，没有谁拦得住，
/// 于是 ScrollRect 也来按自己那套位移算法写内容位置，和 TickGlobalDrag 你一下我一下，
/// 内容位置在两个值之间来回跳，看起来就是抽搐。</summary>
public class NoDragScrollRect : ScrollRect {
    public override void OnBeginDrag(PointerEventData eventData) { }
    public override void OnDrag(PointerEventData eventData) { }
    public override void OnEndDrag(PointerEventData eventData) { }
}
