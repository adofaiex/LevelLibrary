// [split-of] PortalUI.Achievements.cs lines 651-1286 body 12-606

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

/// <summary>PortalUI.Achievements.cs 的一部分（拆分自 PortalUI.Achievements.cs，同一个 partial 类）</summary>
public static partial class PortalUI {
    /// <summary>选中框滑动（每帧指数平滑，快速连点会从当前位置接着走）</summary>
    private static void TickAchFrame() {
        if (_achCatFrameRt == null) return;
        float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        _achFrameCurTop = Mathf.Lerp(_achFrameCurTop, _achFrameTargetTop, k);
        _achFrameCurW = Mathf.Lerp(_achFrameCurW, _achFrameTargetW, k);
        _achFrameCurH = Mathf.Lerp(_achFrameCurH, _achFrameTargetH, k);
        if (Mathf.Abs(_achFrameCurTop - _achFrameTargetTop) < 0.2f) _achFrameCurTop = _achFrameTargetTop;
        if (Mathf.Abs(_achFrameCurW - _achFrameTargetW) < 0.2f) _achFrameCurW = _achFrameTargetW;
        if (Mathf.Abs(_achFrameCurH - _achFrameTargetH) < 0.2f) _achFrameCurH = _achFrameTargetH;
        ApplyAchFrame();
    }

    /// <summary>顶部一条：搜索框（左）+ 成就等级进度条（中）+ 圆形等级标（右）+ 汇总行</summary>
    private static void BuildAchTopBar() {
        // ---- 搜索框 ----
        var searchBg = MakeImage(_achPageRoot.transform, "AchSearchBg", new Color(0.10f, 0.12f, 0.18f, 0.85f));
        searchBg.sprite = AchRoundSprite; searchBg.type = Image.Type.Sliced;
        SetRectPx(searchBg.rectTransform, AchSearchX, AchSearchTop, AchSearchW, AchSearchH);
        _achSearchInput = searchBg.gameObject.AddComponent<InputField>();
        var st = MakeText(searchBg.transform, "T", 24, Color.white, TextAnchor.MiddleLeft);
        Stretch(st.rectTransform, new Vector2(16f, 0f), new Vector2(-16f, 0f));
        st.text = "";
        _achSearchInput.textComponent = st;
        var ph = MakeText(searchBg.transform, "Ph", 24, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform, new Vector2(16f, 0f), new Vector2(-16f, 0f));
        ph.text = Lang.Tr("搜索成就");
        _achSearchInput.placeholder = ph;
        _achSearchInput.text = _achQuery;
        _achSearchInput.onValueChanged.AddListener(v => {
            _achQuery = v ?? "";
            RefreshAchList(true);
        });

        // ---- 等级进度条（圆角胶囊；颜色跟随主题色 Accent，跟左栏滑框/滚动条同一款淡蓝）----
        var barBg = MakeImage(_achPageRoot.transform, "AchBarBg", new Color(1f, 1f, 1f, 0.16f));
        barBg.sprite = AchPillSprite; barBg.type = Image.Type.Sliced; barBg.pixelsPerUnitMultiplier = 1f;
        barBg.raycastTarget = false;
        SetRectPx(barBg.rectTransform, AchBarX, AchBarTop, AchBarW, AchBarH);
        _achBarFullW = AchBarW;
        _achBarFill = MakeImage(barBg.transform, "Fill", AchBarFillColor());
        _achBarFill.sprite = AchPillSprite; _achBarFill.type = Image.Type.Sliced; _achBarFill.pixelsPerUnitMultiplier = 1f;
        _achBarFill.raycastTarget = false;
        var frt = _achBarFill.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(0f, 1f);
        frt.pivot = new Vector2(0f, 0.5f);
        frt.anchoredPosition = Vector2.zero;
        frt.sizeDelta = new Vector2(0f, 0f);
        _achBarText = MakeText(_achPageRoot.transform, "AchBarText", 20, new Color(1f, 1f, 1f, 0.92f), TextAnchor.MiddleLeft);
        SetRectPx(_achBarText.rectTransform, AchBarX + 2f, AchBarTop - 26f, AchBarW, 22f);
        _achBarText.text = "";
        _achBarText.raycastTarget = false;

        // ---- 右侧等级标：成就名左边那个奖杯图标（洗成白色剪影后按等级染色，1白 2黄 3红 4橙 5金）----
        _achBadgeImg = MakeImage(_achPageRoot.transform, "AchLevelBadge", AchBadgeColor(1));
        _achBadgeImg.sprite = AchBadgeSprite;
        _achBadgeImg.type = Image.Type.Simple; _achBadgeImg.preserveAspect = true;
        _achBadgeImg.raycastTarget = false;
        if (_achBadgeImg.sprite == null) _achBadgeImg.enabled = false;   // 万一素材和兜底都拿不到
        SetRectPx(_achBadgeImg.rectTransform, AchBadgeX, AchBadgeTop, AchBadgeSize, AchBadgeSize);

        // ---- 分隔线 ----
        var div = MakeImage(_achPageRoot.transform, "AchDiv", new Color(1f, 1f, 1f, 0.16f));
        div.sprite = SharpSprite; div.type = Image.Type.Simple;
        div.raycastTarget = false;
        SetRectPx(div.rectTransform, AchDivX, AchDivTop, AchDivW, AchDivH);

        // ---- 汇总（搜索框正下方那一行，不要放到进度条那一带，会压在一起）----
        _achSummaryText = MakeText(_achPageRoot.transform, "AchSummary", 20, new Color(1f, 1f, 1f, 0.78f), TextAnchor.MiddleLeft);
        SetRectPx(_achSummaryText.rectTransform, AchSearchX + 2f, TopNavH + 86f, AchSearchW, 24f);
        _achSummaryText.text = "";
        _achSummaryText.raycastTarget = false;
    }

    /// <summary>卡片网格：视口 + 内容 + 池化卡片 + 右侧滚动条</summary>
    private static void BuildAchGrid() {
        var viewport = new GameObject("AchView", typeof(RectTransform));
        viewport.transform.SetParent(_achPageRoot.transform, false);
        var vrt = viewport.GetComponent<RectTransform>();
        SetRectPx(vrt, AchGridX, AchGridTop, AchGridW, AchGridH);
        var vImg = viewport.AddComponent<Image>();
        vImg.color = new Color(0f, 0f, 0f, 0f);     // 完全透明：底下已经有右区遮罩，这里再压一层会变黑
        vImg.raycastTarget = true;                  // 但要能接收射线，空白处滚轮才滚得动
        viewport.AddComponent<RectMask2D>();
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 64f;            // 与谱面列表一致
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        _achContent = content.GetComponent<RectTransform>();
        _achContent.anchorMin = new Vector2(0f, 1f);
        _achContent.anchorMax = new Vector2(1f, 1f);
        _achContent.pivot = new Vector2(0.5f, 1f);
        _achContent.anchoredPosition = Vector2.zero;
        _achContent.sizeDelta = new Vector2(0f, AchGridH);
        scroll.viewport = vrt;
        scroll.content = _achContent;
        _achScroll = scroll;
        scroll.onValueChanged.AddListener(_ => UpdateAchVisible());

        // 滚动条（跟库页同款：轨道底色单独一层，手柄贴住两端）
        var sbGo = new GameObject("AchScrollbar", typeof(RectTransform));
        sbGo.transform.SetParent(_achPageRoot.transform, false);
        var sbrt = sbGo.GetComponent<RectTransform>();
        SetRectPx(sbrt, AchScrollX, AchGridTop, AchScrollW, AchGridH);
        var track = MakeImage(sbGo.transform, "Track", new Color(0.2f, 0.17f, 0.3f, 0.5f));
        track.sprite = SmallRoundSprite; track.type = Image.Type.Sliced;
        track.raycastTarget = false;
        Stretch(track.rectTransform, Vector2.zero, Vector2.zero);
        var bar = sbGo.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        var sliding = new GameObject("Sliding Area", typeof(RectTransform));
        sliding.transform.SetParent(sbGo.transform, false);
        var slrt = sliding.GetComponent<RectTransform>();
        slrt.anchorMin = Vector2.zero; slrt.anchorMax = Vector2.one;
        slrt.offsetMin = Vector2.zero; slrt.offsetMax = Vector2.zero;
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        handleGo.transform.SetParent(sliding.transform, false);
        var hrt = handleGo.GetComponent<RectTransform>();
        hrt.sizeDelta = Vector2.zero;
        var hImg = handleGo.AddComponent<Image>();
        hImg.color = new Color(ThemeColor.r, ThemeColor.g, ThemeColor.b, 1f);
        // 跟着主题色走：这里每次刷新都会重设取值，所以不需要登记进主题色刷新表
        hImg.sprite = SmallRoundSprite; hImg.type = Image.Type.Sliced;
        bar.handleRect = hrt;
        bar.targetGraphic = hImg;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        // 空状态提示（搜索没结果时）
        var empty = MakeText(_achPageRoot.transform, "AchEmpty", 24, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleCenter);
        SetRectPx(empty.rectTransform, AchGridX, AchGridTop + AchGridH * 0.4f, AchGridW, 40f);
        empty.text = Lang.Tr("没有匹配的成就");
        empty.raycastTarget = false;
        _achEmptyHint = empty.gameObject;
        _achEmptyHint.SetActive(false);

        // 卡片池
        for (int i = 0; i < AchPoolCards; i++) _achPool.Add(CreateAchCard(i));
    }

    /// <summary>建一张卡片（所有子对象一次建好，之后只改内容）。
    /// 纵向坐标是算好的，别随手改。卡片高 190，从上往下：图标/名字 6-52 · 分隔线 54 ·
    /// 条件 58-110（18 号，三行要 51.3px）· 注释 112-145（17 号，两行要 32.3px）· 升级进度条 147-160 ·
    /// 分隔线 163 · 达成时间 167-185。整体是靠把每个间距压到 2px 抠出来的，别再往下削。</summary>
    private static AchCardRefs CreateAchCard(int index) {
        var c = new AchCardRefs();
        var go = new GameObject("AchCard" + index, typeof(RectTransform));
        go.transform.SetParent(_achContent, false);
        c.Go = go;
        SetRectPx(go.GetComponent<RectTransform>(), 0f, 0f, AchCardW, AchCardH);
        c.Bg = go.AddComponent<Image>();
        c.Bg.sprite = AchRoundSprite; c.Bg.type = Image.Type.Sliced;

        // 1 区：图标 —— 用原版金色奖杯图，不染色：白色滤镜不改贴图，它那个内阴影和金属感在剪影里会被抹平。
        c.Icon = MakeImage(go.transform, "Icon", Color.white);
        c.Icon.sprite = AchIconSprite;
        c.Icon.type = Image.Type.Simple; c.Icon.preserveAspect = true;
        c.Icon.raycastTarget = false;
        SetRectPx(c.Icon.rectTransform, 10f, 8f, 40f, 40f);

        // 2 区：名称 + 条件 + 注释 + 进度
        // 名字字号可调，见 AchNameFontSize / AchDetailFontSize；右上角原来那个"圆 + 等级数字"的标已删，宽度全给名字。
        c.Name = MakeText(go.transform, "Name", AchNameFontSize, Color.white, TextAnchor.UpperLeft);
        SetRectPx(c.Name.rectTransform, 56f, 6f, AchCardW - 56f - 10f, 46f);
        c.Name.text = "";
        c.Name.fontStyle = FontStyle.Bold;
        c.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
        c.Name.lineSpacing = 0.95f;
        c.Name.raycastTarget = false;

        c.Line1 = MakeImage(go.transform, "L1", new Color(1f, 1f, 1f, 0.14f));
        c.Line1.sprite = SharpSprite; c.Line1.type = Image.Type.Simple; c.Line1.raycastTarget = false;
        SetRectPx(c.Line1.rectTransform, AchCardPadX, 54f, AchCardW - AchCardPadX * 2f, 2f);

        c.Cond = MakeText(go.transform, "Cond", AchDetailFontSize, new Color(0.9f, 0.94f, 1f, 1f), TextAnchor.UpperLeft);
        SetRectPx(c.Cond.rectTransform, AchCardPadX, 58f, AchCardW - AchCardPadX * 2f, 52f);
        c.Cond.horizontalOverflow = HorizontalWrapMode.Wrap;
        c.Cond.lineSpacing = 0.95f;
        // 字号被调大后，条件长的几条会顶到下面的升级进度条，所以给详细信息上 bestFit 当保险丝：
        // 装不下就自动缩，上限 = 用户设的那个字号（正常情况一个字都不缩）。
        c.Cond.resizeTextForBestFit = true;
        c.Cond.resizeTextMinSize = UiFontPx(10);
        c.Cond.resizeTextMaxSize = UiFontPx(AchDetailFontSize);
        c.Cond.raycastTarget = false;

        // 未解锁时把锁图盖在详细信息区（解锁后由 BindAchCard 立刻关掉）。
        // 放在 Cond 之后建 画在文字之上；素材是 159×192，按比例缩进方框不会拉扁。
        c.Lock = MakeImage(go.transform, "Lock", Color.white);
        c.Lock.sprite = AchLockSprite;
        c.Lock.type = Image.Type.Simple;
        c.Lock.preserveAspect = true;
        c.Lock.raycastTarget = false;
        SetRectPx(c.Lock.rectTransform, AchCardW * 0.5f - AchLockSize * 0.5f, AchLockTop, AchLockSize, AchLockSize);
        c.Lock.gameObject.SetActive(false);   // 建出来先关着，绑定那一刻按解锁状态决定

        c.Note = MakeText(go.transform, "Note", AchNoteFont, new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperLeft);
        SetRectPx(c.Note.rectTransform, AchCardPadX, 112f, AchCardW - AchCardPadX * 2f, 33f);   // 按两行留高（Galactic/Universal 那几条注释一行放不下）
        // 这块已经不用了 —— 注释并进了上面那块多行区（Cond）。留着对象只是为了让 AchCardRefs 的字段不用大改，
        // 直接停用（不参与绘制、也不占排序）。
        c.Note.gameObject.SetActive(false);
        c.Note.horizontalOverflow = HorizontalWrapMode.Wrap;
        c.Note.lineSpacing = 0.95f;
        c.Note.raycastTarget = false;
        // 这块的 bestFit 参数已随对象停用而失效，留着只是不为改字段
        c.Note.resizeTextForBestFit = true;
        c.Note.resizeTextMinSize = UiFontPx(AchNoteFontMin);
        c.Note.resizeTextMaxSize = UiFontPx(AchNoteFont);

        // 升级进度条（可重复成就才有）：13 高 + 半径 6 的小圆角
        c.BarBg = MakeImage(go.transform, "BarBg", new Color(1f, 1f, 1f, 0.16f));
        c.BarBg.sprite = SmallRoundSprite; c.BarBg.type = Image.Type.Sliced; c.BarBg.raycastTarget = false;
        SetRectPx(c.BarBg.rectTransform, AchCardPadX, 147f, AchCardW - AchCardPadX * 2f, 13f);
        c.BarFill = MakeImage(c.BarBg.transform, "Fill", new Color(1f, 0.72f, 0.25f, 0.95f));
        c.BarFill.sprite = SmallRoundSprite; c.BarFill.type = Image.Type.Sliced; c.BarFill.raycastTarget = false;
        var bfr = c.BarFill.rectTransform;
        bfr.anchorMin = new Vector2(0f, 0f); bfr.anchorMax = new Vector2(0f, 1f);
        bfr.pivot = new Vector2(0f, 0.5f);
        bfr.anchoredPosition = Vector2.zero;
        bfr.sizeDelta = Vector2.zero;

        // 3 区：达成时间
        c.Line2 = MakeImage(go.transform, "L2", new Color(1f, 1f, 1f, 0.14f));
        c.Line2.sprite = SharpSprite; c.Line2.type = Image.Type.Simple; c.Line2.raycastTarget = false;
        SetRectPx(c.Line2.rectTransform, AchCardPadX, 163f, AchCardW - AchCardPadX * 2f, 2f);
        c.Time = MakeText(go.transform, "Time", 17, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleLeft);
        SetRectPx(c.Time.rectTransform, AchCardPadX, 167f, AchCardW - AchCardPadX * 2f, 18f);
        c.Time.raycastTarget = false;

        go.SetActive(false);
        return c;
    }

    // ---------- 交互 ----------

    /// <summary>切换左侧分类（MOD成就 / 私人成就 / 彩蛋成就；里程碑不走这里，它靠 P/G/U 子 tab 进入）</summary>
    private static void SelectAchCategory(AchCategory cat) {
        if (cat == AchCategory.Milestone) return;      // 防御：里程碑不是分类按钮
        if (_achCat == cat) return;
        _achCat = cat;
        _achQuery = "";                       // 换分类时清空搜索（不然会以为"这一类没成就"）
        if (_achSearchInput != null) _achSearchInput.text = "";
        SetAchFrameTarget(AchCurrentSlot(), false);   // 实心块滑过去
        RefreshAchList(true);
    }

    /// <summary>切换里程碑的分级子 tab（0=P、1=G、2=U、3=官方成就）；点它同时意味着进入里程碑类</summary>
    private static void SelectAchTier(int tier) {
        tier = Mathf.Clamp(tier, 0, AchTierCount - 1);   // 上界跟着子 tab 数量走，别写死数字
        Lg.Info($"[LevelLib] 【成就】SelectAchTier({tier}) 进入：分类={_achCat} 分级={_achMileTier}"
            + $" 列表={_achList.Count} 条（夹紧后 tier={tier} 上界 3 官方成就=3）");
        if (_achCat == AchCategory.Milestone && _achMileTier == tier) {
            RefreshAchList(true);                       // 同一个也强制刷新：点了必须有反应
            Lg.Info($"[LevelLib] 【成就】SelectAchTier({tier}) 同级但强制刷新：列表={_achList.Count} 条 层级字母={AchTierLetter()}");
            return;
        }
        _achCat = AchCategory.Milestone;
        _achMileTier = tier;
        _achQuery = "";
        if (_achSearchInput != null) _achSearchInput.text = "";
        SetAchFrameTarget(AchCurrentSlot(), false);
        RefreshAchList(true);
        // 官方成就点不动那次的排查手段：把"到底走到哪一步"写进日志，点了没反应时一看就知道卡在哪
        Lg.Info($"[LevelLib] 【成就】点子 tab tier={tier} → 分类={_achCat} 列表={_achList.Count} 条"
            + $" 层级字母={(AchTierLetter())} 空提示={(_achEmptyHint != null ? _achEmptyHint.activeSelf : false)}");
    }

    /// <summary>重算"当前分类（含里程碑分级）+ 搜索"的成就列表，回到顶部，重排卡片</summary>
    private static void RefreshAchList(bool resetScroll) {
        // 成就页还没建过（没打开过成就 tab / 面板刚开）→ 什么都不用做：
        // 解锁回调（OnAchUnlocked）会直接调本函数，那时页面可能还不存在，下次 BuildAchievementsPage 会用最新数据重建。
        if (_achContent == null) return;
        _achList.Clear();
        string q = (_achQuery ?? "").Trim();
        bool milestone = _achCat == AchCategory.Milestone;
        char tier = AchTierLetter();
        for (int i = 0; i < Achievements.Defs.Count; i++) {
            var d = Achievements.Defs[i];
            if (d == null) continue;
            if (d.Cat != _achCat) continue;
            // 里程碑类再按 P/G/U 分级拆：P级里程碑 = P 的"严判击破"+"完美无瑕"，G/U 同理
            if (milestone && AchTierOf(d.Id) != tier) continue;
            if (q.Length > 0 && !AchMatch(d, q)) continue;
            // 可重复成就：等级/注释随触发次数变，绑卡片前先按最新次数写进去
            if (!d.IsDaily) Achievements.ApplyTriggerDisplay(d.Id);
            // 每日成就的"数据行"（打卡次数）不在这里写：那种写法依赖"列表被刷新过"，会漏显示，
            // 所以改成在 BindAchCard 里渲染时现算（唯一渲染路径，绝不会漏）。
            _achList.Add(d);
        }
        int rows = Mathf.CeilToInt(_achList.Count / (float)AchCols);
        _achContent.sizeDelta = new Vector2(0f, Mathf.Max(AchGridH, rows * AchCardPitch - (rows > 0 ? AchCardGapY : 0f)));
        if (resetScroll && _achScroll != null) _achScroll.verticalNormalizedPosition = 1f;
        // 列表内容变了 → 强制全量重绑（区间缓存和每张卡的绑定都得作废，
        // 否则"过滤后条数没变但内容换了"会显示成旧卡片）
        for (int i = 0; i < _achPool.Count; i++) _achPool[i].BoundIndex = -1;
        _achLastFirst = -1; _achLastLast = -1;
        RefreshAchCategoryButtons();
        InvalidateAchBarValues();   // 分类/搜索换了 → "当前显示 N 张"与汇总都要重算
        RefreshAchLevelBar();
        UpdateAchVisible();
        if (_achEmptyHint != null) _achEmptyHint.SetActive(_achList.Count == 0);
    }

    /// <summary>搜索匹配：名字 / 触发条件 / 注释 都算</summary>
    private static bool AchMatch(AchievementDef d, string q) {
        if (d.Name != null && d.Name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (d.Desc != null && d.Desc.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        string trig = Achievements.GetTriggerText(d);
        if (trig != null && trig.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (d.Comment != null && d.Comment.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (d.Id != null && d.Id.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>左栏文字颜色：选中项用深蓝黑（压在实心淡蓝块上，白字会糊）；
    /// 里程碑没被选中时，P/G/U 三个子 tab 压暗（表示"这一组现在不在用"）</summary>
    private static void RefreshAchCategoryButtons() {
        bool mileActive = _achCat == AchCategory.Milestone;
        for (int i = 0; i < _achSubName.Count; i++) {
            if (_achSubName[i] == null) continue;
            bool sel = mileActive && i == _achMileTier;
            _achSubName[i].color = sel ? HighlightTextColor
                                : (mileActive ? new Color(1f, 1f, 1f, 0.78f) : new Color(1f, 1f, 1f, 0.42f));
        }
        for (int i = 0; i < _achCatBtns.Count; i++) {
            if (_achCatName[i] == null) continue;
            bool sel = (AchCategory)(i + 1) == _achCat;      // 分类按钮从 Mod 开始，+1 对应枚举值
            _achCatName[i].color = sel ? HighlightTextColor : new Color(1f, 1f, 1f, 0.78f);
        }
        if (_achMileCaption != null) {
            _achMileCaption.color = mileActive ? new Color(1f, 1f, 1f, 0.62f) : new Color(1f, 1f, 1f, 0.34f);
        }
    }

    /// <summary>进度条填充色 = 主题色 Accent（跟左栏选中滑框、滚动条手柄、按钮主色同一款淡蓝）。
    /// 主题色自带 0.65 的 alpha（那是给"整行选中的大面积铺色"调的），进度条是细条，
    /// 压那么透会看不清底下的轨道，所以只取它的 RGB、alpha 抬到 0.92。</summary>
    private static Color AchBarFillColor() {
        var a = Accent;
        return new Color(a.r, a.g, a.b, 0.92f);
    }

    /// <summary>顶部进度条 + 皇冠等级标 + 汇总</summary>
    private static void RefreshAchLevelBar() {
      // 值（等级 / 经验 / 进度 / 汇总）只在新解锁或列表重建时变，没变就复用上次的 ——
      // 每帧重算要遍历 Defs 全表 6 遍，而这一行只有颜色必须每帧跟面板透明度联动。
        if (!_achBarValValid) {
            Achievements.LevelProgress(out _achBarLv, out _achBarExp, out _achBarFloor, out _achBarNext);
            _achBarFillPct = Achievements.LevelFill();   // 内部会再调一次 LevelProgress，只在这一轮里付
            _achBarDone = 0; _achBarTotal = 0;
            for (int c = 0; c < 4; c++) {
                int a, b;
                Achievements.CountUnlocked((AchCategory)c, out a, out b);
                _achBarDone += a; _achBarTotal += b;
            }
            _achBarShown = _achList.Count;
            _achBarValValid = true;
        }
        int lv = _achBarLv, exp = _achBarExp, next = _achBarNext;
        float fill = _achBarFillPct;
        if (_achBarFill != null) {
            // 胶囊精灵的 9-slice 边距是 13，太窄会画歪 → 有进度时至少给 28 宽；0 进度直接不画
            float w = fill <= 0.001f ? 0f : Mathf.Max(28f, _achBarFullW * fill);
            _achBarFill.rectTransform.sizeDelta = new Vector2(w, 0f);
            _achBarFill.gameObject.SetActive(w > 0f);
            _achBarFill.color = AchBarFillColor();     // 跟着面板透明度设置走，每帧重取
        }
        if (_achBarText != null) {
            if (_achBarTextLast == null || _achBarTextLast != lv + "|" + exp + "|" + next + "|" + _achBarShown) {
                _achBarTextLast = lv + "|" + exp + "|" + next + "|" + _achBarShown;
                _achBarText.text = lv >= Achievements.MaxAchievementLevel
                    ? Lang.TrFmt("成就等级 {0}（满级）　总经验 {1}", lv, exp)
                    : "成就等级 " + lv + " → " + (lv + 1) + "　经验 " + exp + " / " + next
                      + "（还差 " + Mathf.Max(0, next - exp) + "）";
            }
        }
        // 等级标按等级染色；满级（5 级）画大 10%，坐标要按增量补回一半才居中
        if (_achBadgeImg != null) {
            _achBadgeImg.color = AchBadgeColor(lv);
            float s = lv >= Achievements.MaxAchievementLevel ? 1.10f : 1f;
            float w = AchBadgeSize * s, h = AchBadgeSize * s;
            var rt = _achBadgeImg.rectTransform;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(AchBadgeX - (w - AchBadgeSize) * 0.5f,
                                              -(AchBadgeTop - (h - AchBadgeSize) * 0.5f));
        }
        if (_achSummaryText != null) {
            string s = Lang.TrFmt("已解锁 {0} / {1}　当前显示 {2} 张", _achBarDone, _achBarTotal, _achBarShown);
            if (_achSummaryLast != s) { _achSummaryLast = s; _achSummaryText.text = s; }
        }
    }

    /// <summary>按滚动位置把可视区（+上下各一行缓冲）的卡片绑上内容，其余隐藏。
    /// 性能关键：无论 120 张还是 4 张，同时活着的卡片最多 AchPoolCards 张。
    /// 每帧会被调用，所以先比"可视区间有没有变"，没变就直接返回 —— 否则每帧给几十张卡重设 anchors/坐标，uGUI 会反复重算布局。</summary>
    private static void UpdateAchVisible() {
        if (_achContent == null || _achPool.Count == 0) return;
        float vh = _achScroll != null && _achScroll.viewport != null ? _achScroll.viewport.rect.height : AchGridH;
        if (vh <= 1f) vh = AchGridH;
        float scrollY = _achContent.anchoredPosition.y;        // 内容轴心在上 → 往下滚时为正
        int firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollY / AchCardPitch) - 1);
        int visRows = Mathf.CeilToInt(vh / AchCardPitch) + 3;   // +3：上下各一行缓冲再多一行
        int firstIdx = firstRow * AchCols;
        int lastIdx = Mathf.Min(_achList.Count - 1, (firstRow + visRows) * AchCols - 1);
        if (firstIdx == _achLastFirst && lastIdx == _achLastLast) return;   // 区间没变，什么都不用做
        _achLastFirst = firstIdx; _achLastLast = lastIdx;

        int used = 0;
        for (int idx = firstIdx; idx <= lastIdx && used < _achPool.Count; idx += AchCols) {
            for (int col = 0; col < AchCols && used < _achPool.Count; col++) {
                int item = idx + col;
                if (item >= _achList.Count) break;
                var card = _achPool[used++];
                if (card.BoundIndex != item) {
                    card.BoundIndex = item;
                    BindAchCard(card, _achList[item]);
                }
                int row = item / AchCols;
                SetRectPx(card.Go.GetComponent<RectTransform>(),
                    col * (AchCardW + AchCardGapX), row * AchCardPitch, AchCardW, AchCardH);
                if (!card.Go.activeSelf) card.Go.SetActive(true);
            }
        }
        for (int i = used; i < _achPool.Count; i++) {
            var card = _achPool[i];
            card.BoundIndex = -1;
            if (card.Go.activeSelf) card.Go.SetActive(false);
        }
    }

    /// <summary>把两个字号设置刷到所有成就卡上（设置页滑条一动就调它）。
    /// 卡片是池化的，光改 fontSize 不够：BoundIndex 置 -1 让下一帧 UpdateAchVisible 全量重绑，文字才会按新字号重排。</summary>
    private static void ApplyAchFontSizes() {
        try {
            for (int i = 0; i < _achPool.Count; i++) {
                var c = _achPool[i];
                if (c == null) continue;
                if (c.Name != null) c.Name.fontSize = UiFontPx(AchNameFontSize);
                if (c.Cond != null) {
                    c.Cond.fontSize = UiFontPx(AchDetailFontSize);
                    c.Cond.resizeTextMaxSize = UiFontPx(AchDetailFontSize);   // bestFit 的上限跟着走
                }
                c.BoundIndex = -1;
            }
            _achLastFirst = -1; _achLastLast = -1;
            UpdateAchVisible();
        } catch (System.Exception e) { Lg.Ex("应用成就字号失败", e); }
    }

    /// <summary>未解锁时的占位文字（作者要求：名字和详细内容都盖成"？？？"，解锁后才显示真内容）</summary>

    /// <summary>把一条成就的内容写进一张卡片</summary>
    private static void BindAchCard(AchCardRefs c, AchievementDef d) {
        bool daily = d.IsDaily;
        bool unlocked = daily ? Achievements.DailyDoneToday(d.Id) : Achievements.IsUnlocked(d.Id);
        var lvColor = Achievements.GetLevelColor(d.Level);

        // 底色：已解锁用等级色淡淡铺一层；未解锁中性灰
        c.Bg.color = unlocked
            ? new Color(lvColor.r * 0.24f, lvColor.g * 0.24f, lvColor.b * 0.24f, 0.92f)
            : new Color(1f, 1f, 1f, 0.10f);
        // 图标：用原版金色奖杯图 —— 已解锁保持原色（白色滤镜 = 不改变贴图），未解锁压暗
        c.Icon.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.20f);

        // 未解锁：只显示名称，下方详细信息整块留空（名字和内容全盖成 ？？？ 的老做法已废）
        if (!unlocked) {
            c.Name.text = Lang.Tr(d.Name);   // 成就名在词条表里（Data2/3/4），显示处必须过 Tr 才跟随语言
            c.Name.color = new Color(1f, 1f, 1f, 0.55f);
            c.Cond.text = "";
            // 例外：打卡的"累计打卡次数"是玩家自己的数据、不算剧透 → 未解锁时照常显示，
            // 否则今天还没打卡时那一行什么都没有，玩家看不到次数。
            string statMasked = Achievements.DetailStatText(d.Id);
            if (!string.IsNullOrEmpty(statMasked)) c.Cond.text = statMasked;
            c.Cond.color = new Color(1f, 1f, 1f, 0.5f);
            c.Note.text = "";        // 这条已并进上面那块多行区
            // 锁图位置：那块区是 58..145（高 87），56 的锁居中放；
            // 打卡那条左边有"累计打卡 N 次"文字，锁要挪到右边，免得压字。
            if (c.Lock != null) {
                bool hasStat = !string.IsNullOrEmpty(c.Cond.text);
                float lx = hasStat ? (AchCardW - 10f - AchLockSize - 8f) : (AchCardW * 0.5f - AchLockSize * 0.5f);
                SetRectPx(c.Lock.rectTransform, lx, AchLockTop, AchLockSize, AchLockSize);
                bool showLock = AchLockSprite != null;    // 素材读不到就不显示（也不留空框）
                if (c.Lock.gameObject.activeSelf != showLock) c.Lock.gameObject.SetActive(showLock);
            }
            c.BarBg.gameObject.SetActive(false);
            c.Time.text = "";        // 未解锁不写"达成时间：？？？"
            c.Time.color = new Color(1f, 1f, 1f, 0.35f);
            c.Rainbow = false;
            return;
        }

        // ---- 已解锁 ----
        // 锁图必须在这里显式关掉：卡片是池化复用的，上一条未解锁的成就把锁打开过，
        // 同一张卡接着绑一条已解锁的成就不关就会留着一把锁。
        if (c.Lock != null && c.Lock.gameObject.activeSelf) c.Lock.gameObject.SetActive(false);
        c.Name.text = Lang.Tr(d.Name);   // 同上：名字必须过 Tr（词条已在表里）
        c.Name.color = lvColor;
        // 下方详细信息一行一条：注释（可多行）→ 等级 → 触发次数（可重复成就才有），
        // 不拼"触发条件：xxx"那种固定格式、也不写触发条件。最多 5 行左右落在 87 高的多行区里，超了靠 bestFit 缩字。
        {
            var condLines = new List<string>();
            string noteForLines = Achievements.DetailStatText(d.Id);
            if (string.IsNullOrEmpty(noteForLines)) noteForLines = d.Comment;
            if (!string.IsNullOrEmpty(noteForLines)) {
                foreach (var ln in noteForLines.Split('\n')) {
                    string one = ln.Trim();
                    // 必须逐行翻：多行文本整串在多语言表里根本不存在，拼完再翻等于一个字都不翻。
                    // 同族的 "等级 {0}" / "触发次数 {0}" 也是拼接出来的，走 TrFmt。
                    if (one.Length > 0) condLines.Add(Lang.Tr(one));
                }
            }
            condLines.Add(Lang.TrFmt("等级 {0}", d.Level));
            long tcLine = Achievements.GetTriggerCount(d.Id);
            if (!daily && tcLine > 0) condLines.Add(Lang.TrFmt("触发次数 {0}", tcLine));
            c.Cond.text = string.Join("\n", condLines.ToArray());
        }
        c.Cond.color = new Color(0.9f, 0.94f, 1f, 1f);

        // 注释直接写内容，不加"注释："前缀；内容为空时也明确写出来，不留白。
        // 数据行优先：打卡那张卡这一行显示累计打卡次数（渲染时现算，见 Achievements.DetailStatText）——
        // 不能只在列表刷新时往 d.Comment 里写，那条路依赖列表被重建，会漏显示。
        string note = Achievements.DetailStatText(d.Id);
        if (string.IsNullOrEmpty(note)) note = d.Comment;
        bool hasNote = !string.IsNullOrEmpty(note);
        c.Note.text = "";   // 注释已并进上面的"逐行"多行区，这条不再用
        c.Note.color = hasNote
            ? new Color(1f, 1f, 1f, 0.62f)
            : new Color(1f, 1f, 1f, 0.38f);

        // 升级进度条：只给"可重复、按次数升级"的成就显示，同一行位置留给"等级/经验"或"升级进度"的文字。
        // 每日成就必须排除（打卡借用同一张次数表，但它不升级、没有进度条）；
        // 判据统一走 LevelsUp(id)（它能滤掉"能多次触发但不升级"的那些），别在这里再手写一遍 id 比较。
        long tc = Achievements.GetTriggerCount(d.Id);
        bool repeat = !daily && tc > 0 && Achievements.LevelsUp(d.Id);
        if (repeat) {
            long lo, hi; bool maxed;
            Achievements.RepeatBand(tc, out lo, out hi, out maxed);
            float f = maxed ? 1f : Mathf.Clamp01((float)(tc - lo + 1) / Mathf.Max(1f, hi - lo + 1));
            c.BarBg.gameObject.SetActive(true);
            c.BarFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, (AchCardW - 24f) * f), 0f);
        } else {
            c.BarBg.gameObject.SetActive(false);
        }

        // 3 区：达成时间
        if (daily) c.Time.text = Lang.Tr("今日状态：") + Lang.Tr(unlocked ? "已完成" : "未完成");
        else {
            string t = Achievements.GetUnlockTimeText(d.Id);
            c.Time.text = string.IsNullOrEmpty(t) ? Lang.Tr("达成时间：未达成") : (Lang.Tr("达成时间：") + t);
        }
        c.Time.color = new Color(0.85f, 1f, 0.85f, 0.85f);

        // 5 级成就名字缓慢 RGB 渐变（每帧在 TickAchievementsPage 里驱动）
        c.Rainbow = d.Level >= 5;
    }

    // ---------- 每帧 ----------

    /// <summary>成就页每帧：选中框滑动 + 进度条/圆标刷新 + 5级名字彩虹 + 可视卡片重排</summary>
    private static void TickAchievementsPage() {
        if (_achPageRoot == null || !_achPageRoot.activeInHierarchy) return;
        // 左栏自检要等页面停稳之后再跑：建完立刻就量会量在动画途中（量出来的 x 全是被 Bg 压着的旧位置）
        if (_achSelfCheckPending && Time.unscaledTime >= _achSelfCheckAt) {
            _achSelfCheckPending = false;
            SelfCheckAchLeft();
        }
        TickAchFrame();
        RefreshAchLevelBar();
        UpdateAchVisible();
        // 5 级成就名字缓慢彩虹
        if (_achPool.Count > 0) {
            float hue = (Time.unscaledTime * 0.08f) % 1f;
            Color rc = Color.HSVToRGB(hue, 0.85f, 1f);
            for (int i = 0; i < _achPool.Count; i++) {
                var c = _achPool[i];
                if (c == null || !c.Go.activeSelf || !c.Rainbow) continue;
                c.Name.color = rc;
            }
        }
    }

    /// <summary>面板销毁时清掉成就页的所有引用（整棵 UI 树连同 PageSlot 一起没了），下次切到成就 tab 时会重新 BuildAchievementsPage。
    /// 别省这一步：不清的话 _achPageRoot 会是一个"已销毁的 Unity 对象"，== null 虽是 true，
    /// 但直接读它的 activeInHierarchy 会抛 MissingReferenceException。</summary>
    private static void ClearAchievementsPageRefs() {
        _achPageRoot = null;
        _achSearchInput = null;
        _achBarFill = null;
        _achBarText = null;
        _achBadgeImg = null;
        _achScroll = null;
        _achContent = null;
        _achEmptyHint = null;
        _achSummaryText = null;
        _achCatFrameRt = null;
        _achMileCaption = null;
        _achCatBtns.Clear();
        _achCatName.Clear();
        _achSubBtns.Clear();
        _achSubName.Clear();
        _achPool.Clear();
        _achList.Clear();
        _achLastFirst = -1; _achLastLast = -1;
    }
}
