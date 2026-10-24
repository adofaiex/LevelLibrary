// [split-of] PortalUI.cs lines 1696-1826
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

/// <summary>PortalUI 的底部滚动信息栏（正在播放条），以及 LibChromeVisible 这条显隐规则。</summary>
public static partial class PortalUI {
    private static void BuildNowPlayingBar(Transform parent, float left, float top, float w, float h) {
        if (_panelCanvas == null || _nowBox != null) return;
        var box = MakeImage(parent, "NowPlaying", new Color(0.18f, 0.15f, 0.28f, 0.55f));   // 同 ItemColor
        box.sprite = RoundedSprite;
        box.type = Image.Type.Sliced;
        var br = box.rectTransform;
        SetRectPx(br, left, top, w, h);
        _nowBox = box;

        // 文字区：RectMask2D 裁剪框，防止长文本溢出
        var maskGo = new GameObject("NowMask", typeof(RectTransform));
        maskGo.transform.SetParent(box.transform, false);
        var mrt = maskGo.GetComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = new Vector2(14f, 4f);
        mrt.offsetMax = new Vector2(-14f, -4f);
        maskGo.AddComponent<RectMask2D>();
        _nowMaskRt = mrt;

        // 滚动文字：水平溢出，靠移动做 marquee
        var text = MakeText(maskGo.transform, "NowText", 24, Color.white, TextAnchor.MiddleLeft);
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(0f, 1f);   // 左锚竖拉伸：anchoredPosition.x 可自由移动做 marquee
        trt.pivot = new Vector2(0f, 0.5f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        trt.sizeDelta = new Vector2(0f, 0f);
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        _nowText = text;
        _nowTextRt = trt;
        _nowScrollTimer = 0f;
        _nowSrcMode = -1;   // 强制下次 Tick 重算内容
        // 按设置初始化显隐；未选中 / 动画期间不显示（跟滑条、搜索框同一规则）
        _nowBox.gameObject.SetActive(NowBarShow && LibChromeVisible());
    }

    /// <summary>库页左侧外壳（难度滑条 / 搜索框 / 滚动信息栏）是否显示：三者共用这一个判断。
    /// 未选中（详情区收起、列表拉满）时全藏起来，选中时动画快停稳（_libExpand < 0.22）才一起出现。
    /// 信息栏显隐的唯一权威是 TickNowPlaying / RefreshNowBar 每帧的 SetActive，只改 ApplyLibExpand 会被下一帧点回来，
    /// 所以改这条规则时 ApplyLibExpand 的搜索框 + 滑条、TickNowPlaying、RefreshNowBar 三处必须一起改。</summary>
    private static bool LibChromeVisible() { return _libExpand < 0.22f; }

    /// <summary>每帧驱动正在播放条（Tick 里调用）：更新歌名、marquee 滚动、按设置显示 / 隐藏。</summary>
    private static void TickNowPlaying() {
        if (_nowText == null || _nowMaskRt == null) return;
        // 信息栏显示开关：关 = 整条隐藏（SetActive 连同子节点一起控制）；
        // 未选中谱面时与难度滑条 / 搜索框一起藏（见 LibChromeVisible）
        bool show = NowBarShow && _nowBox.gameObject != null && LibChromeVisible();
        if (_nowBox != null && _nowBox.gameObject.activeSelf != show) _nowBox.gameObject.SetActive(show);
        if (!show) return;

        // 取内容：NowBarMode 1 = 滚动歌名，2 = 自定义文本；
        // 只在来源字符串真的变了时才拼串，避免每帧分配新字符串
        string song = (_musicPlayer != null) ? _musicPlayer.SongName : "";
        // 语言也要进缓存键：拼串里的固定段是「当前语言」的译文，只按歌名判断的话
        // 切了语言这一条还停在旧语言（来源没变，不会重拼）。
        if (song != _nowSrcSong || NowBarMode != _nowSrcMode || NowBarCustomText != _nowSrcCustom
            || Lang.Current != _nowSrcLang) {
            _nowSrcSong = song; _nowSrcMode = NowBarMode; _nowSrcCustom = NowBarCustomText;
            _nowSrcLang = Lang.Current;
            // 固定段单独过 Tr：整串（前缀 + 歌名）在词条表里查不到，拼完再翻是翻不动的
            _nowDisplay = NowBarMode == 2
                ? (string.IsNullOrEmpty(NowBarCustomText) ? Lang.Tr("（无内容）") : NowBarCustomText)
                : Lang.Tr("正在播放：") + (string.IsNullOrEmpty(song) ? Lang.Tr("（无）") : song);
        }
        string display = _nowDisplay;
        // 内容变化 → 重置滚动
        if (display != _nowText.text) {
            _nowText.text = display;
            _nowScrollTimer = 0f;
            // 文字宽度只在内容变化时算一次，缓存到 _nowTextWidth
            float textW = Mathf.Max(1f, _nowText.preferredWidth);
            _nowTextWidth = textW;
            // 必须给文字一个明确宽度，否则 RectMask2D 对宽 0 的溢出文字裁切异常
            _nowTextRt.sizeDelta = new Vector2(Mathf.Max(80f, textW), 0f);
        }
        if (string.IsNullOrEmpty(display)) return;
        // marquee：装得下就静止居中，装不下才滚（这条栏很宽，短文本静止更好读）。
        float tw = _nowTextWidth;   // 用缓存文字宽
        float mw = Mathf.Max(1f, _nowMaskRt.rect.width);   // 用可见区宽（不是整框，两边各有 14px 内边距）
        var pos = _nowTextRt.anchoredPosition;
        if (tw <= mw) {
            _nowScrollTimer = 0f;
            pos.x = (mw - tw) * 0.5f;   // 居中静止
            _nowTextRt.anchoredPosition = pos;
            return;
        }
        float speed = 40f;                        // px/秒
        _nowScrollTimer += Time.unscaledDeltaTime * speed;
        float x = mw - _nowScrollTimer;           // 从右侧进入
        // 滚到完全出左侧 → 跳回右侧继续
        if (x < -tw) {
            _nowScrollTimer -= (tw + mw);
            x = mw - _nowScrollTimer;
        }
        pos.x = x;
        _nowTextRt.anchoredPosition = pos;
    }

    // 正在播放条的内容缓存（避免每帧拼字符串）
    private static string _nowSrcSong, _nowSrcCustom, _nowDisplay;
    private static int _nowSrcMode = -1;
    /// <summary>拼串时用的语言：切语言要重拼，见 TickNowPlaying 的缓存判据。</summary>
    private static string _nowSrcLang;

    /// <summary>信息栏设置变更后立即生效：开关 / 内容切换时调用。</summary>
    public static void RefreshNowBar() {
        if (_nowBox == null) return;
        // 关 = 隐藏整条，开 = 显示（SetActive 连同子节点）；未选中 / 动画期间仍保持隐藏（见 LibChromeVisible）
        _nowBox.gameObject.SetActive(NowBarShow && LibChromeVisible());
        // 内容变化由下次 Tick 因内容不同重新排版
    }

    private static float _searchDirtyTimer;                  // 搜索输入防抖计时（>0 时 Tick 里倒计时重建）
    private static bool _filterNoMark = true;              // 显示无等级标记的谱面（默认开）
    // ---------- 成就系统（列表在顶层「成就」tab，见 PortalUI.Achievements.cs） ----------
    /// <summary>5 级成就名字（缓慢 RGB 渐变）；成就页自己驱动卡片池的颜色，这份列表只作兜底。</summary>
    private static readonly List<Text> _achRainbowTexts = new List<Text>();
    // ---------- 顶层 tab（库 / 成就 / 设置 / 开发者） ----------
    /// <summary>顶层 tab 容器：整条页面带，固定宽度 = 页数 × 屏宽，只做横向平移。</summary>
}
