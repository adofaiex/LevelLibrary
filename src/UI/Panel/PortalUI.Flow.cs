// LevelLibrary 的流式布局：一个矩形改位置时不让它瞬移，而是从旧位滑到新位。
// ① 位置只有这里一个写入点（TickFlow 每帧推进）：业务代码只登记目标（FlowTo / FlowKeyTo），
//    不许自己再写 anchoredPosition，否则两个写手会打架（同一个毛病见交互态层文件头）。
// ② 触发是"窗口式"的：只有 FlowWindow 开着的这段时间里登记的坐标变化才会滑；
//    平时（滚动、拖动、换页重建）一律直接落位，否则滚动时每帧目标都在变，整列会像橡皮筋拖在后面。
using System.Collections.Generic;
using UnityEngine;

namespace LevelLibrary;

public static partial class PortalUI {
    /// <summary>流式让位的默认时长：走设计尺 MoSmall（面板内小件让位那一档；大范围重排显式传 MoPage）</summary>
    private const float FlowSeconds = MoSmall;
    /// <summary>位移超过这么多像素就直接落位（换页 / 池子复用 / 滚出去又回来，别拉一条长线）</summary>
    private const float FlowInstantPx = 1200f;
    /// <summary>停稳误差（像素）：剩余距离小到它以内就 snap 到目标并收手，之后一帧都不再写这个矩形</summary>
    private const float FlowSnapPx = 0.5f;
    /// <summary>身份表上限：按身份记的表会随滚动堆旧身份，满了整表清一次（见 FlowTrimKeys）</summary>
    private const int FlowKeyCap = 1024;

    /// <summary>一个被流式接管的矩形：从哪来、到哪去、这一趟走多久</summary>
    private sealed class FlowItem {
        public RectTransform Rt;
        public float FromX, FromY, ToX, ToY;
        public float T;         // 进度 0..1
        public float Dur;       // 这一趟的时长（秒）
        public bool Active;
    }

    /// <summary>按矩形记（同一根 RectTransform 一直代表同一件事时用它）</summary>
    private static readonly Dictionary<int, FlowItem> _flowByRect = new Dictionary<int, FlowItem>();
    /// <summary>按身份记（谱面行 = 谱面路径）：池子把这根 RectTransform 换给了别人，
    /// 也能从"那个谱面原来在哪"滑过来 —— 这是换序看起来是"流"而不是"闪"的关键。</summary>
    private static readonly Dictionary<string, FlowItem> _flowByKey = new Dictionary<string, FlowItem>();
    private static readonly List<int> _flowDeadRect = new List<int>();
    private static readonly List<string> _flowDeadKey = new List<string>();
    /// <summary>登记窗口的截止时刻（unscaledTime）：只有窗口里发生的坐标变化才会滑</summary>
    private static float _flowWindowEnd;
    private static string _flowWhat;         // 本窗口是谁开的（只给那一次日志用）
    private static int _flowMoves;           // 本窗口起了多少次滑动
    private static bool _flowLogged;         // 本窗口的那一次日志报过没有

    /// <summary>开一个让位窗口：接下来 seconds 秒里登记的坐标变化会滑过去，之外一律直接落位。
    /// what = 这次让位是谁（进日志，用来分辨"生效了但看不出来"和"根本没生效"）。</summary>
    private static void FlowWindow(string what, float seconds) {
        float end = Time.unscaledTime + seconds;
        if (end > _flowWindowEnd) _flowWindowEnd = end;
        _flowWhat = what;
        _flowMoves = 0;
        _flowLogged = false;
    }


    /// <summary>登记一个矩形该在哪（默认 MoSmall 的时长）。布局侧的唯一入口。</summary>
    private static void FlowTo(RectTransform rt, float x, float y) { FlowTo(rt, x, y, FlowSeconds); }

    private static void FlowTo(RectTransform rt, float x, float y, float dur) {
        if (rt == null) return;
        int id = rt.GetInstanceID();
        FlowItem it;
        if (!_flowByRect.TryGetValue(id, out it)) {
            it = NewItem(rt, x, y);
            _flowByRect[id] = it;
            Place(rt, x, y);
            return;
        }
        FlowStep(it, rt, x, y, dur);
    }

    /// <summary>按身份登记一个矩形该在哪（谱面行用谱面路径当 key）。</summary>
    private static void FlowKeyTo(string key, RectTransform rt, float x, float y) {
        FlowKeyTo(key, rt, x, y, FlowSeconds);
    }

    private static void FlowKeyTo(string key, RectTransform rt, float x, float y, float dur) {
        if (rt == null) return;
        if (string.IsNullOrEmpty(key)) { FlowTo(rt, x, y, dur); return; }
        FlowItem it;
        if (!_flowByKey.TryGetValue(key, out it)) {
            it = NewItem(rt, x, y);
            _flowByKey[key] = it;
            Place(rt, x, y);
            return;
        }
        FlowStep(it, rt, x, y, dur);
    }


    /// <summary>忘掉一个身份键的旧位（这条谱面被删了 / 这一栏清空了）。</summary>
    private static void FlowForgetKey(string key) {
        if (!string.IsNullOrEmpty(key)) _flowByKey.Remove(key);
    }

    /// <summary>忘掉一根矩形在两张表里的全部记录（它被移出视口 / 这个界面要拆了）。
    /// 身份表里那些"还指着它的旧记录"必须一起清：那笔记录会在它被移出视口之后继续写它的位置，
    /// 于是已经移出去的行又自己滑回列表里来。</summary>
    private static void FlowForgetRect(RectTransform rt) {
        if (rt == null) return;
        _flowByRect.Remove(rt.GetInstanceID());
        _flowDeadKey.Clear();
        foreach (KeyValuePair<string, FlowItem> kv in _flowByKey)
            if (kv.Value.Rt == rt) _flowDeadKey.Add(kv.Key);
        for (int i = 0; i < _flowDeadKey.Count; i++) _flowByKey.Remove(_flowDeadKey[i]);
    }

    /// <summary>身份表满了就整表清一次：先把还在动的落到目标（不能把行冻在半路），再清空。
    /// 清空只损失"下一次重列时这些身份没有旧位可滑"（直接落位），不影响正确性。</summary>
    private static void FlowTrimKeys() {
        if (_flowByKey.Count <= FlowKeyCap) return;
        foreach (KeyValuePair<string, FlowItem> kv in _flowByKey) {
            FlowItem it = kv.Value;
            if (it.Active && it.Rt != null) Place(it.Rt, it.ToX, it.ToY);
            it.Active = false;
        }
        _flowByKey.Clear();
    }

    /// <summary>新建条目：就落在目标上、算作已经停稳（第一次见到它不动，免得建页那一下整屏乱飞）</summary>
    private static FlowItem NewItem(RectTransform rt, float x, float y) {
        FlowItem it = new FlowItem { Rt = rt, FromX = x, FromY = y, ToX = x, ToY = y, T = 1f, Dur = FlowSeconds };
        return it;
    }

    /// <summary>直接落位（新条目 / 认输时都用它）</summary>
    private static void Place(RectTransform rt, float x, float y) {
        rt.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>核心：从"现在渲染在哪"改道到新目标。窗口外、或者位移太大，就直接落位。</summary>
    private static void FlowStep(FlowItem it, RectTransform rt, float x, float y, float dur) {
        float e = EaseOut3(it.T);
        float curX = it.FromX + (it.ToX - it.FromX) * e;
        float curY = it.FromY + (it.ToY - it.FromY) * e;
        // 池子复用：这一根现在代表这个身份了。换手那一帧要当场把它摆到"这个身份现在看起来在哪"，
        // 否则这一帧它按新身份画在原地、下一帧才跳回旧位再滑（一帧的错位闪）。
        // 同一根矩形也得核一遍位置：行是复用的，它这一帧可能刚替另一笔身份站过位，
        // 那时候"Rt 就是我"并不代表我站在 From→To 插值出来的那个点上，早年就靠这句把整个收藏区留在了列表顶端。
        Vector2 now = rt.anchoredPosition;
        if (it.Rt != rt || Mathf.Abs(now.x - curX) > FlowSnapPx || Mathf.Abs(now.y - curY) > FlowSnapPx)
            Place(rt, curX, curY);
        it.Rt = rt;
        if (x == it.ToX && y == it.ToY) return;            // 目标没变：正在动的继续动，别的什么都不做
        bool far = Mathf.Abs(x - it.ToX) > FlowInstantPx || Mathf.Abs(y - it.ToY) > FlowInstantPx;
        if (far || Time.unscaledTime > _flowWindowEnd) {
            it.FromX = it.ToX = x;
            it.FromY = it.ToY = y;
            it.T = 1f;
            it.Active = false;
            Place(rt, x, y);
            return;
        }
        it.FromX = curX;                                   // 从"看起来还在的位置"出发（连续，不跳）
        it.FromY = curY;
        it.ToX = x;
        it.ToY = y;
        it.T = 0f;
        it.Dur = dur <= 0.01f ? FlowSeconds : dur;
        it.Active = true;
        _flowMoves++;
    }

    /// <summary>三次缓出（跟宠物下拉铺开 / 公告墙同一条）</summary>
    private static float EaseOut3(float k) {
        if (k <= 0f) return 0f;
        if (k >= 1f) return 1f;
        float u = 1f - k;
        return 1f - u * u * u;
    }

    /// <summary>常驻段：推进所有正在滑的矩形；停稳那一帧 snap 到目标，静止时一个矩形都不碰。</summary>
    private static void TickFlow() {
        FlowLogOnce();
        FlowTrimKeys();
        if (_flowByRect.Count == 0 && _flowByKey.Count == 0) return;
        float dt = Time.unscaledDeltaTime;
        if (_flowByRect.Count > 0) TickFlowMap(_flowByRect, _flowDeadRect, dt);
        if (_flowByKey.Count > 0) TickFlowMap(_flowByKey, _flowDeadKey, dt);
    }

    /// <summary>一个让位窗口结束后报一行日志：让位了几件。
    /// 有这一行才分得清"滑动生效了但看不出来"和"根本没走这条路径"。</summary>
    private static void FlowLogOnce() {
        if (_flowLogged || _flowWhat == null || Time.unscaledTime <= _flowWindowEnd) return;
        _flowLogged = true;
        if (_flowMoves > 0 && Main.ModEntry != null && Main.ModEntry.Logger != null)
            Lg.Info("流式布局：" + _flowWhat + " 让位 " + _flowMoves + " 件");
    }

    /// <summary>一张表推一帧（两张表的类型不同，写成泛型省一份代码）</summary>
    private static void TickFlowMap<TK>(Dictionary<TK, FlowItem> map, List<TK> dead, float dt) {
        dead.Clear();
        foreach (KeyValuePair<TK, FlowItem> kv in map) {
            FlowItem it = kv.Value;
            if (it.Rt == null) { dead.Add(kv.Key); continue; }   // 那个界面已经拆了
            if (!it.Active) continue;                            // 停稳过的不再写
            it.T += dt / it.Dur;
            float e = EaseOut3(it.T);
            float px = it.FromX + (it.ToX - it.FromX) * e;
            float py = it.FromY + (it.ToY - it.FromY) * e;
            Place(it.Rt, px, py);
            float dx = it.ToX - px, dy = it.ToY - py;
            if (it.T >= 1f || dx * dx + dy * dy <= FlowSnapPx * FlowSnapPx) {
                Place(it.Rt, it.ToX, it.ToY);                    // snap 到目标：停稳误差 = 0
                it.T = 1f;
                it.Active = false;
                it.FromX = it.ToX;
                it.FromY = it.ToY;
            }
        }
        for (int i = 0; i < dead.Count; i++) map.Remove(dead[i]);
    }

}
