// [split-of] PortalUI.cs lines 1326-1517
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

/// <summary>PortalUI partial 模块：开发者页（制作名单 / 点击彩蛋 / 键盘彩蛋）与详情区空状态</summary>
public static partial class PortalUI {
    private static readonly Color DevCreditsGray = new Color(0.70f, 0.71f, 0.76f, 1f);   // 浅灰（全部文字）
    private const float DevCreditCenterX = 960f;   // 页面水平中心（顶部标签栏也是按 1920 居中）
    /// <summary>开发者名字悬停时的亮黄（"闪亮黄色发光"），描边就是那圈光晕</summary>
    private static readonly Color DevNameYellow = new Color(1f, 0.92f, 0.28f, 1f);
    /// <summary>开发者名与主页（点名字跳转）。URL 为空 = 只做"悬停发黄光"不跳转（云落落 就是这种）。</summary>
    private static readonly string[] DevNameWords = { "系机人喵", "洛星橙", "母鸡先生", "一片烂海苔", "月正向山倾", "云落落" };
    private static readonly string[] DevNameUrls = {
        "https://space.bilibili.com/3546732782685084",
        "https://space.bilibili.com/3493132696029515",
        "https://space.bilibili.com/514415540",   // 母鸡先生
        "https://space.bilibili.com/1593532869",
        "https://space.bilibili.com/2010107273",
        "",                                        // 云落落：不跳转
    };

    /// <summary>这一行里哪些词是可点的开发者名（按 DevNameWords 顺序现切，切不到就跳过）。</summary>
    private static void DevCreditNames(string lineText, int size, float centerY, float centerX, float totalW) {
        for (int i = 0; i < DevNameWords.Length && i < DevNameUrls.Length; i++) {
            string word = DevNameWords[i];
            int wi = lineText.IndexOf(word, System.StringComparison.Ordinal);
            if (wi < 0) continue;                       // 这一行里没有他（比如 Coding 行没有母鸡先生）
            DevCreditNameHit(lineText, size, centerY, centerX, totalW, wi, word, DevNameUrls[i]);
        }
    }

    /// <summary>一个可点开发者名：先铺一层亮黄副本（带描边光晕）盖在原字上，常态 alpha 0 看不见；
    /// 它上面再压一块透明命中区挂 Button，悬停用 ColorTint 把副本的 alpha 抬起来 —— 于是
    /// 只有名字那三个字变黄发光，"&" 与其余文字不动，而且不用每帧轮询。</summary>
    private static void DevCreditNameHit(string lineText, int size, float centerY, float centerX,
                                         float totalW, int wordIndex, string word, string url) {
        float lineLeft = centerX - Mathf.Max(1f, totalW) * 0.5f;
        float preW = MeasureTextWidthAt(lineText.Substring(0, wordIndex), size);
        float wordW = Mathf.Max(40f, MeasureTextWidthAt(word, size));

        // 悬停要的是"闪闪发光"：加粗 + 鲜艳黄 + 一圈白边。用白边而不是黄边 —— 白边能把底下原来的
        // 灰字整个盖住，就不会出现"两层字叠着"的观感；边宽只给 2px，不往相邻字上糊。
        var glow = MakeText(_devPage.transform, "DevName", size, DevNameYellow, TextAnchor.MiddleCenter);
        glow.text = word;
        glow.fontStyle = FontStyle.Bold;
        glow.raycastTarget = false;
        glow.horizontalOverflow = HorizontalWrapMode.Overflow;
        glow.verticalOverflow = VerticalWrapMode.Overflow;
        // 悬停态就是"加粗 + 鲜艳黄"两层效果，不再描边（作者看过带白边的版本，说描边不要了）。
        // 所以这里不挂 Outline/Shadow：字就是干净的一层亮黄粗体。
        SetRectPx(glow.rectTransform, lineLeft + preW, centerY - 110f, wordW, 220f);

        var hit = MakeImage(_devPage.transform, "DevNameHit", new Color(0f, 0f, 0f, 0f));
        hit.raycastTarget = true;
        SetRectPx(hit.rectTransform, lineLeft + preW - 4f, centerY - 34f, wordW + 8f, 68f);
        var btn = hit.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.targetGraphic = glow;                  // 提亮打在黄字副本上
        var cb = btn.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0f);      // 常态：副本透明 = 平时看到的是原来的灰字
        cb.highlightedColor = Color.white;               // 悬停：副本显形 = 这三个字变黄发光
        cb.pressedColor = new Color(1f, 1f, 1f, 0.75f);
        cb.selectedColor = new Color(1f, 1f, 1f, 0f);
        cb.disabledColor = new Color(1f, 1f, 1f, 0f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        // 提到最上层：这一行的"彩蛋点击区"建在本函数之后，不抬层就会被它盖住（母鸡先生就悬停不亮、点不开）
        try { hit.transform.SetAsLastSibling(); } catch { }
        string u = url, w = word;
        btn.onClick.AddListener(() => {
            DeselectUi();
            try {
                if (!string.IsNullOrEmpty(u)) Application.OpenURL(u);
                else Lg.Info("开发者页：「" + w + "」没配主页链接（只做了悬停高亮）");
            } catch (System.Exception e) { ReportErrorQuiet("打开开发者主页失败", e); }
            Lg.Info("开发者页：点了「" + w + "」" + (string.IsNullOrEmpty(u) ? "" : " → " + u));
            // 母鸡先生 还兼着「咯咯哒」彩蛋的计数（原点击区在它下面、被本层盖住，计数挪到这里补上）
            try { if (w == DevEggClickWord) Achievements.RegisterClick(Achievements.AchPopCat, 6); } catch { }
        });
    }

    /// <summary>Explore 那行里可点的词（点击彩蛋）。提成常量是因为它同时被
    /// Explore 行的文案和透明点击区的横向定位用到，两处各写一遍字符串就会改一处漏一处。</summary>
    private const string DevEggClickWord = "母鸡先生";

    /// <summary>开发者页：一张干净的"制作名单"</summary>
    private static void BuildDeveloperPage() {
        if (_devPage != null) return;
        // 槽位号必须和 tab 编号一致（库里占 0、成就 1、设置页视觉上占 2），否则切到这里页面停在屏幕左边看不见。
        var page = PageSlot(4);
        if (page == null) return;
        _devPage = page.gameObject;
        _devPage.name = "DevPage";
        var dpRt = _devPage.GetComponent<RectTransform>();
        dpRt.anchorMin = Vector2.zero;
        dpRt.anchorMax = Vector2.one;
        dpRt.offsetMin = Vector2.zero;
        dpRt.offsetMax = Vector2.zero;
        // 重力字母（点一下掉一颗字母/数字）的容器 + 独立子画布先建出来。建在这里是因为
        // DevCreditLine 要把每行的矩形登记给它当"别在这儿掉字"的判据（那五行文字与彩蛋点击区不许被抢）。
        BuildDevGravityLetters();

        // 背景：整页一层流体球（照搬 fluid-orb，见 PortalUI.DevFluidBg.cs）—— 它自带"不做圆边"的那版贴图，
        // 铺满整页不会像头像那颗球那样被圆形 alpha 裁成一个大椭圆；铺在最底下，压暗罩与制作名单都盖在它身上。
        MountDevFluidBg(dpRt);
        // 背景遮罩：保留（切到这个 tab 时从深到浅渐入，比硬切舒服）
        var devBg = MakeImage(_devPage.transform, "DevBg", new Color(0f, 0f, 0f, 0.34f));
        Stretch(devBg.rectTransform, Vector2.zero, Vector2.one);
        devBg.raycastTarget = false;
        _devVeilImg = devBg;
        RegisterGlassPanel(devBg);   // 磨砂背景：渐入时糊图跟着淡
        // 重力字母那块子画布刚从"DevBg 之前"建出来，会被这层 0.34 的黑压暗罩在里面；
        // 挪到它后面一层：字母画在压暗层之上、文字之下（文字还是盖住落下来的字）。
        // 用压暗罩自己的序号算，不要写死 1 —— 底下多铺一层背景就会差一位。
        if (_devGravRoot != null) _devGravRoot.transform.SetSiblingIndex(devBg.transform.GetSiblingIndex() + 1);

        DevCreditLine("TEAM: FAC", 28, DevCreditsGray, 330f, DevCreditCenterX + 330f, false);
        DevCreditLine("LevelLibrary", 190, DevCreditsGray, 418f, DevCreditCenterX, true, null, null, KaitiFont(), true);   // 楷体 + 投影
        // 只有标题下面那三行整体下移（由 DevCreditsDown 管）：标题与 TEAM:FAC 不动，
        // 版本号留在 y=985。
        DevCreditLine("powered by ADOFAI Tools", 32, DevCreditsGray, 528f + DevCreditsDown, DevCreditCenterX, false);
        DevCreditLine("Coding：洛星橙&云落落&系机人喵", 34, DevCreditsGray, 616f + DevCreditsDown, DevCreditCenterX, false);
        // 这一行可点的词改成「母鸡先生」但彩蛋保留：成就 id 仍是 AchPopCat，已点次数继续算
        DevCreditLine("Explore：母鸡先生&月正向山倾&一片烂海苔", 34, DevCreditsGray, 704f + DevCreditsDown, DevCreditCenterX, false,
                      DevEggClickWord, () => Achievements.RegisterClick(Achievements.AchPopCat, 6));

        // 版本号不进 DevCreditsDown，留在 y=985
        var ver = MakeText(_devPage.transform, "DevVersion", 24, new Color(0.70f, 0.71f, 0.76f, 0.72f), TextAnchor.MiddleCenter);
        SetRectPx(ver.rectTransform, 0f, 985f, 1920f, 40f);
        try { ver.text = Lang.Tr("版本 ") + Updater.ReadLocalVersion(); }   // 完整版（含 .0046），不是 UMM 的 4 段
        catch { ver.text = Lang.Tr("版本 ?"); }
    }

    /// <summary>标题下面那三行（powered by / Coding / Explore）的统一下移量；标题与版本号各自不动</summary>
    private const float DevCreditsDown = 40f;

    /// <summary>制作名单里的一行：整屏宽矩形 + 居中（centerX 是横向中心），字号/字体变了也不会跑偏。
    /// clickWord 非空时给这行挂一块只盖住那一个词的透明点击区（前缀宽度同字号现量、从 text 里现切），
    /// 点它才回调 onClick。</summary>
    private static void DevCreditLine(string text, int size, Color col, float centerY, float centerX, bool bold,
                                      string clickWord = null, System.Action onClick = null, Font font = null,
                                      bool shadow = false) {
        var t = MakeText(_devPage.transform, "DevCredit", size, col, TextAnchor.MiddleCenter);
        const float w = 1500f, h = 220f;   // 够宽够高：最长那行（Explore）不换行，190 号标题也不会被裁
        SetRectPx(t.rectTransform, centerX - w * 0.5f, centerY - h * 0.5f, w, h);
        t.text = text;
        if (font != null) {
            t.font = font;                  // 楷体；font 为 null 时保持游戏字体
            // 向 FontCheck 登记"这一处故意用楷体"，只登记不改行为
            RegisterKaitiIntended(t);
        }
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (bold) t.fontStyle = FontStyle.Bold;
        if (shadow) AddTitleShadow(t);   // 大标题那行才要投影（它没有 Outline，走组件这条路）
        // 登记给重力字母的矩形只盖住这行字本身（现量的 preferred 尺寸 + 一点余量），不是上面那个
        // 1500×220 的排版框：排版框几乎铺满整页，按它判会让整页都点不出字来。
        // 缩框之后文字仍然居中在同一处（对齐是 MiddleCenter、两个方向都允许溢出）。
        float hitW = Mathf.Max(1f, t.preferredWidth) + DevGravCreditPadX * 2f;
        float hitH = Mathf.Max(1f, t.preferredHeight) + DevGravCreditPadY * 2f;
        SetRectPx(t.rectTransform, centerX - hitW * 0.5f, centerY - hitH * 0.5f, hitW, hitH);
        DevGravityRegisterCredit(t.rectTransform);

        // 名字命中区放在最后建：这一行可能还有"彩蛋点击区"（母鸡先生），后建的会盖住先建的 ——
        // 之前把名字建在彩蛋区之前，结果母鸡先生被盖住、悬停不发光（作者报的"母鸡先生还是不显示"）。
        float lineW = t.preferredWidth;
        if (string.IsNullOrEmpty(clickWord) || onClick == null) {
            DevCreditNames(text, size, centerY, centerX, lineW);
            return;
        }
        int wi = text.IndexOf(clickWord, System.StringComparison.Ordinal);
        if (wi < 0) {          // 词不在这一行里（改文案改错了）→ 只留一行日志，名字区还是要建
            Lg.Info("开发者页点击词不在这一行里：" + clickWord + " ← " + text);
            DevCreditNames(text, size, centerY, centerX, t.preferredWidth);
            return;
        }
        float totalW = Mathf.Max(1f, t.preferredWidth);
        float lineLeft = centerX - totalW * 0.5f;
        float preW = MeasureTextWidthAt(text.Substring(0, wi), size);      // 前缀宽度现量
        float wordW = Mathf.Max(40f, MeasureTextWidthAt(clickWord, size));
        var hit = MakeImage(_devPage.transform, "DevCreditHit", new Color(0f, 0f, 0f, 0f));
        hit.raycastTarget = true;
        SetRectPx(hit.rectTransform, lineLeft + preW - 6f, centerY - 34f, wordW + 12f, 68f);
        var btn = hit.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = hit;
        var cb = onClick;
        var word = clickWord;
        btn.onClick.AddListener(() => {
            try { cb(); }
            catch (System.Exception e) { Lg.Ex("开发者页点击「" + word + "」失败", e); }
        });
        // 名字区最后建：这一行的彩蛋点击区刚建完，名字命中区压在它之上，母鸡先生才悬停得亮、点得开
        DevCreditNames(text, size, centerY, centerX, totalW);
    }

    /// <summary>用同字号量一段文字的宽度（临时建一个 Text，量完立刻关掉）；给点击区算横向位置用</summary>
    private static float MeasureTextWidthAt(string s, int size) {
        var go = new GameObject("DevMeasure", typeof(RectTransform));
        go.transform.SetParent(_devPage.transform, false);
        var tx = MakeText(go.transform, "T", size, Color.white, TextAnchor.MiddleLeft);
        tx.text = s;
        tx.horizontalOverflow = HorizontalWrapMode.Overflow;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        tx.raycastTarget = false;
        float w = tx.preferredWidth;
        go.SetActive(false);          // 立刻藏掉，避免它在 (0,0) 闪一帧
        Object.Destroy(go);
        return w;
    }

    /// <summary>开发者页的键盘彩蛋：敲 "bugbug" 解锁成就。只在开发者页里听（切走清掉暗号缓冲）。
    /// 两路来源必须错开：InputString 一帧可能给好几个字符，它一个可用字符都没给时才用 A~Z 扫描补一帧
    /// （同时喂会把同一个按键记成两个字符，暗号永远拼不出来）。
    /// 面板开着时游戏读不到这些键，打字不会误触游戏操作。</summary>
    private static void TickDevEaster() {
        if (_topTab != 4) { Achievements.ResetCcbTyping(); return; }
        bool unlocked;
        if (Achievements.FeedCcbTyping(UiInput.InputString, out unlocked)) return;
        for (int i = 0; i < 26; i++) {
            if (!UiInput.KeyDown((KeyCode)((int)KeyCode.A + i))) continue;
            Achievements.FeedCcbTyping(((char)('a' + i)).ToString(), out unlocked);
        }
    }

    /// <summary>切换详情区空状态：未选中 → 详情区收起（列表拉满），选中 → 详情区展开。
    /// 这里只设目标，真正的位移由 TickLibExpand 花 0.22 秒做动画。</summary>
    private static void ApplyDetailEmptyState() {
        bool hasSelected = _selectedPath != null;
        _libExpandTarget = hasSelected ? 0f : 1f;   // 未选中=1（列表拉满）、选中=0（详情区展开）
        ApplyLibExpand(_libExpand);                 // 按当前进度立刻摆一次（构建期就是终态，不会突然跳）
        if (_detailFixedZone != null) _detailFixedZone.SetActive(hasSelected);
        if (_detailTabRow != null) _detailTabRow.SetActive(hasSelected);
        if (_detailEnterBtn != null) _detailEnterBtn.gameObject.SetActive(hasSelected);
        // 详情区那两个按钮已搬进右键菜单，这里不再管它们的显隐
        if (_detailContent != null) _detailContent.gameObject.SetActive(hasSelected);
        if (hasSelected) RefreshDetailTabContent();
    }

    // 详情区 tab 的白框（选中指示滑块），颜色与字母定位条 / 设置页导航共用 FrameLineColor 那款淡蓝

    private static RectTransform _detailTabFrameRt;
    private static Vector2 _detailFrameCur, _detailFrameTarget;
    private const float DetailFramePadX = 4f, DetailFramePadY = 2f;

    private static Vector2 _detailFrameTargetSize = new Vector2(120f, 56f);

    private static Vector2 _detailFrameSizeCur = new Vector2(120f, 56f);

    /// <summary>给空 tab 内容区加占位提示（幂等：已有就不重复加）</summary>
    private static void EnsureDetailPlaceholder(GameObject area, string text) {
        if (area.transform.Find("Ph") != null) return;
        var ph = MakeText(area.transform, "Ph", 22, new Color(0.6f, 0.6f, 0.7f, 1f), TextAnchor.MiddleCenter);
        ph.text = text;
        var prt = ph.rectTransform;
        prt.anchorMin = new Vector2(0f, 0.4f); prt.anchorMax = new Vector2(1f, 0.6f);
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
    }

}
