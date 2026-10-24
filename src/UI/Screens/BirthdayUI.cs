using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>生日彩蛋界面：每年 1 月 13 日（作者生日）启动 MOD 时弹出祝福窗（橙色主题喵）</summary>
public static class BirthdayUI {
    private static Canvas _canvas;
    private static bool _checked;   // 本次会话已检查过日期（不管是不是生日，只查一次）
    private static bool _busy;      // 弹窗已创建（防重复）

    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.65f);
    private static readonly Color CardColor = new Color(0.14f, 0.10f, 0.20f, 0.98f);   // 深紫黑卡片（跟谱面库同调）
    private static readonly Color TitleColor = new Color(1f, 0.62f, 0.15f, 1f);        // 橙
    private static readonly Color BodyColor = new Color(1f, 0.92f, 0.78f, 1f);         // 暖白
    private static readonly Color BtnColor = new Color(0.95f, 0.50f, 0.10f, 1f);       // 橙按钮

    private const string Title = "当当当当！！";
    private const string Body = "今天是该MOD的作者洛星橙的生日噢，\n可以去qq群里或者b站给他一个祝福嘛，\n虽然他正在备战高考，不一定能看见，\n但还是很感谢你哦";

    /// <summary>MOD 启动时调用：1 月 13 日则弹出生日祝福（游玩中不打扰）</summary>
    public static void Check() {
        if (_checked) return;
        _checked = true;
        try {
            DateTime now = DateTime.Now;
            if (now.Month != 1 || now.Day != 13) return;
            if (SceneManager.GetActiveScene().name == "scnGame") return;   // 正在游玩就不弹
            Show();
        } catch (Exception e) {
            Lg.Info("生日检查异常: " + e);
        }
    }

    /// <summary>每帧占位，暂未接任何逻辑</summary>
    public static void Tick() { }

    private static void Show() {
        if (_busy) return;
        _busy = true;
        try {
            EnsureEventSystem();
            // 调 PortalUI.MakeCanvas 而不是本文件自己抄一份：它内部会 TrackOurCanvas 登记进 _ourCanvases，
            // 漏登记会让开始界面 SsScanHigherLayer 不认这座画布、PrewarmFontGlyphs 漏这页的字。
            _canvas = PortalUI.MakeCanvas("LevelLib_Birthday", 95000);
            UnityEngine.Object.DontDestroyOnLoad(_canvas.gameObject);

            // 全屏遮罩（点空白处关闭）
            var dim = MakeImage(_canvas.transform, "Dim", DimColor);
            Stretch(dim.rectTransform, Vector2.zero, Vector2.zero);
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(Close);

            // 中央卡片
            var card = MakeImage(_canvas.transform, "Card", CardColor);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(820f, 460f);

            // 标题（橙色大字），投影跟其它主标题同一份（这个类独立，调 PortalUI 那支公开的）
            var title = MakeText(card.transform, "Title", 72, TitleColor, TextAnchor.MiddleCenter);
            PortalUI.AddTitleShadow(title);
            Stretch(title.rectTransform, new Vector2(40f, 280f), new Vector2(-40f, -30f));
            title.text = Title;

            // 正文
            var body = MakeText(card.transform, "Body", 30, BodyColor, TextAnchor.MiddleCenter);
            Stretch(body.rectTransform, new Vector2(50f, 30f), new Vector2(-50f, -150f));
            body.text = Body;

            // 关闭按钮
            var btn = MakeButton(card.transform, "OkBtn", new Vector2(0f, -160f), new Vector2(220f, 62f), BtnColor);
            btn.onClick.AddListener(Close);
            var btnText = MakeText(btn.transform, "OkText", 30, Color.white, TextAnchor.MiddleCenter);
            Stretch(btnText.rectTransform, Vector2.zero, Vector2.zero);
            btnText.text = Lang.Tr("知道啦！");

            Lg.Info("今天（1月13日）是作者生日，已弹出祝福界面");
        } catch (Exception e) {
            Lg.Info("生日界面创建失败: " + e);
            _busy = false;
        }
    }

    /// <summary>庆祝窗口是否还开着（Esc 链靠它决定这一层要不要收）。</summary>
    public static bool IsOpen { get { return _canvas != null && _canvas.gameObject != null; } }

    public static void Close() {
        if (_canvas == null) return;
        UnityEngine.Object.Destroy(_canvas.gameObject);
        _canvas = null;
        _busy = false;
    }

    private static void EnsureEventSystem() {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("LevelLib_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static Image MakeImage(Transform parent, string name, Color color) {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static Button MakeButton(Transform parent, string name, Vector2 pos, Vector2 size, Color color) {
        var img = MakeImage(parent, name, color);
        img.rectTransform.anchoredPosition = pos;
        img.rectTransform.sizeDelta = size;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    private static Text MakeText(Transform parent, string name, int size, Color color, TextAnchor align) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = UiFont;
        text.fontSize = PortalUI.UiFontPx(size);
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        PortalUI.RegisterUiFontText(text, size);   // 记下设计尺字号：玩家改「界面字体大小」时按它重刷
        return text;
    }

    private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax) {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static Font _uiFont;
    private static Font UiFont {
        get {
            // 统一走 PortalUI 的字体解析（优先自定义字体，没装则回退游戏字体）
            if (_uiFont == null) _uiFont = PortalUI.UiFontForOthers();
            return _uiFont;
        }
    }
}
