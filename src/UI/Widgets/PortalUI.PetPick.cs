// 大肥鱼「模型」那一行的下拉选单：点一下在按钮下面展开一整列模型，选一项就收起来。
// 展开是"面板从一条缝向下铺开、每一行等面板盖到它那儿再落进来"：行露面的时刻由面板铺到哪儿决定，
// 面板还没盖住的行透明度还是 0，不需要额外挂遮罩去裁。每个导入的模型后面挂一个删除键。
// 导入模型之后整列重建（RefreshPetPickIfOpen）；删掉一个模型之后不重建 —— 删除键的动画播完才轮到画面：
// 那一行当场溶掉、它下面的行整体往上流一格、面板同步矮一行（见 TickPetPickCollapse 与 SettlePetPickRow）。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelLibrary;

public static partial class PortalUI {
    private static Canvas _petPickCanvas;
    private static RectTransform _petPickRoot;      // 面板本体（圆角底 + 一列行）
    private static RectTransform _petPickAnchor;    // 触发它的那一行按钮（重建时还要用）
    private static RectTransform PetPickAnchor { get { return _petPickAnchor; } }
    private const float PetPickRowH = 48f;          // 一行的高度（删除键按行高撑满，跟通关记录一样）
    private const float PetPickPadY = 8f;           // 面板上下内边距
    private const float PetPickW = 300f;            // 面板宽（比行按钮宽，能放下名字 + 删除键）
    private const int PetPickSortOrder = 89600;     // 压在右键菜单(89500)之上、玩家框(90000)之下

    // ---------- 展开与收起的编排 ----------
 /// <summary>面板从一条缝铺到满高用的时间（走设计尺里"小过渡"那一档）。</summary>
    private const float PetPickOpenSeconds = MoSmall;
 /// <summary>收起比铺开快：整块淡掉就销毁，不做几何收缩 —— 收缩会让行挂到面板外面去。</summary>
    private const float PetPickCloseSeconds = 0.10f;
 /// <summary>一行自己落下来用的时间。</summary>
    private const float PetPickRowSeconds = 0.14f;
 /// <summary>删掉一行之后合拢用的时间（走设计尺"小过渡"那一档：跟铺开同一个手感）。</summary>
    private const float PetPickCollapseSeconds = MoSmall;
 /// <summary>每行从上方多少像素处落下来（起点比终点高，像是从按钮底下抽出来的）。</summary>
    private const float PetPickRowRise = 14f;
 /// <summary>行露面的时刻 = 面板铺过它下沿的时刻乘这个系数（小于 1 就是"确定盖住了才动"）。</summary>
    private const float PetPickCoverK = 0.9f;
 /// <summary>铺开时面板底色的起点（倍数）：磨砂读的就是这个透明度，会跟着一起淡进来。</summary>
    private const float PetPickDimFrom = 0.65f;
    private const float PetPickRowX = 6f;                       // 行相对面板左右各内缩这么多
    private const float PetPickRowInnerH = PetPickRowH - 4f;    // 行之间留 4 像素缝

 /// <summary>一行的动画状态：位置从"落下前"推到最终值，透明度挂在它自己的 CanvasGroup 上。
 /// 用 CanvasGroup 而不是逐个改颜色：行里的名字、删除键各自还有自己的悬停色，乘一层透明度不会打架。</summary>
    private sealed class PetPickRow {
        public RectTransform Rt;
        public CanvasGroup Group;
        public Button Btn;     // 合拢重绑索引时要用（点它会切到第几个模型）
        public Image Bg;       // 选中行的实心块（重刷常态色 / 选中态）
        public Text Label;     // 名字（选中行用深色字）
        public float Y;        // 最终位置（从面板顶往下量）
        public float Start;    // 这一行开始落的时刻（秒）
    }

    private static readonly List<PetPickRow> _petPickRows = new List<PetPickRow>(12);
    private static Image _petPickPanel;
    private static Color _petPickPanelBase;
    private static float _petPickPanelH;
    private static float _petPickT;          // 铺开已经过了多久
    private static bool _petPickOpenDone;    // 铺完了：之后每帧一行都不写
    private static bool _petPickClosing;
    private static float _petPickCloseT;
    private static bool _petPickCollapsing;   // 删掉一行之后正在合拢
    private static int _petPickCollapseIdx = -1;
    private static float _petPickCollapseT;
    private static float _petPickPanelHFrom;  // 合拢前的高度

 /// <summary>打开下拉（再点一次就收起来）。锚在那一行右侧按钮的下面。</summary>
    private static void TogglePetPick(RectTransform anchor) {
        if (_petPickCanvas != null) { ClosePetPick(); return; }
        BuildPetPick(anchor);
    }

 /// <summary>导入 / 删除模型之后刷新：开着就重建，关着只更新那一行的名字。
 /// 重建走"当场销毁"，不留收起动画 —— 不然新旧两列会在屏幕上重叠一下。</summary>
    private static void RefreshPetPickIfOpen() {
        bool wasOpen = _petPickCanvas != null;
        RectTransform anchor = wasOpen ? PetPickAnchor : null;
        if (wasOpen) ClosePetPick(true);
        RefreshPetModelText();
        if (wasOpen && anchor != null) BuildPetPick(anchor);   // 开着就按新表重建（锚点还在那一行上）
    }

 /// <summary>收起下拉（点外面、选完、按 Esc 都走这里）：淡一下再销毁。</summary>
    private static void ClosePetPick() { ClosePetPick(false); }

 /// <summary>instant = true 时当场销毁（重建列表用）。正在收的时候再点就等它收完，不叠一次。</summary>
    private static void ClosePetPick(bool instant) {
        if (_petPickCanvas == null) return;
        if (instant) { DestroyPetPickNow(); return; }
        if (_petPickClosing) return;
        _petPickClosing = true;
        _petPickCloseT = 0f;
    }

    private static void DestroyPetPickNow() {
        if (_petPickCanvas != null) Object.Destroy(_petPickCanvas.gameObject);
        _petPickAnchor = null;
        _petPickCanvas = null;
        _petPickRoot = null;
        _petPickPanel = null;
        _petPickPanelBase = Color.white;
        _petPickPanelH = 0f;
        _petPickT = 0f;
        _petPickOpenDone = false;
        _petPickClosing = false;
        _petPickCloseT = 0f;
        _petPickCollapsing = false;
        _petPickCollapseIdx = -1;
        _petPickCollapseT = 0f;
        _petPickPanelHFrom = 0f;
        _petPickRows.Clear();
    }

 /// <summary>建下拉：一块整屏透明层（点它就收）+ 面板本体（一列行）。
 /// 建出来的样子是"面板高 0、行都停在起点上方"，由 TickPetPick 每帧铺开。</summary>
    private static void BuildPetPick(RectTransform anchor) {
        _petPickAnchor = anchor;
        _petPickCanvas = MakeCanvas("LevelLib_PetPick", PetPickSortOrder);

  // 点外面收起来：整屏透明图，盖住下面所有东西的射线
        var block = MakeImage(_petPickCanvas.transform, "Block", new Color(0f, 0f, 0f, 0f));
        block.raycastTarget = true;
        Stretch(block.rectTransform, Vector2.zero, Vector2.one);
        var blockBtn = block.gameObject.AddComponent<Button>();
        blockBtn.transition = Selectable.Transition.None;
        blockBtn.targetGraphic = block;
        blockBtn.onClick.AddListener(ClosePetPick);

        int n = PetModels.Count;
        float h = n * PetPickRowH + PetPickPadY * 2f;
        var panel = MakeImage(_petPickCanvas.transform, "Panel", PanelColor);
        panel.sprite = RoundedSprite; panel.type = Image.Type.Sliced;
        panel.raycastTarget = true;
        _petPickRoot = panel.rectTransform;
        SetRectPxV(_petPickRoot, PetPickXFor(anchor), PetPickTopFor(anchor, h), PetPickW, 0f);
        _petPickRoot.anchorMin = new Vector2(0f, 1f);
        _petPickRoot.anchorMax = new Vector2(0f, 1f);
        _petPickRoot.pivot = new Vector2(0f, 1f);
        _petPickRoot.sizeDelta = new Vector2(PetPickW, 0f);     // 高度从 0 起，每帧铺到 h

        _petPickPanel = panel;
        _petPickPanelBase = PanelColor;
        _petPickPanelH = h;
        _petPickT = 0f;
        _petPickOpenDone = false;
        _petPickClosing = false;
        _petPickCloseT = 0f;
        _petPickRows.Clear();
        SetPetPickPanelAlpha(_petPickPanelBase.a * PetPickDimFrom);
        RegisterGlassPanel(panel);
        Lg.Info("大肥鱼模型下拉：铺开 " + n + " 行");

        for (int i = 0; i < n; i++) {
            int idx = i;
            var def = PetModels[i];
            bool cur = idx == ResolvePetModel();
            float rowY = PetPickPadY + i * PetPickRowH;
            var rowBg = MakeImage(panel.transform, "Row" + i, cur ? HighlightFillColor : new Color(1f, 1f, 1f, 0.06f));
            rowBg.sprite = RoundedSprite; rowBg.type = Image.Type.Sliced;
            rowBg.raycastTarget = true;
            SetRectPx(rowBg.rectTransform, PetPickRowX, rowY - PetPickRowRise, PetPickW - 2f * PetPickRowX, PetPickRowInnerH);
            var rowBtn = rowBg.gameObject.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            rowBtn.targetGraphic = rowBg;
            rowBtn.onClick.AddListener(() => PetPickChoose(idx));
            RegisterUiTarget(rowBtn, rowBg);               // 行底补上悬停/按下（走全局那一层，不再自己写颜色）
            var label = MakeText(rowBg.transform, "N", 22, cur ? HighlightTextColor : Color.white, TextAnchor.MiddleLeft);
            label.text = def.Name;                        // 名字必须显式赋：物体名叫 "N" 不代表文字是 "N"
            SetRectPx(label.rectTransform, 14f, 0f, PetPickW - 90f, PetPickRowInnerH);
            AttachUiAccent(rowBtn, label);

   // 这一行什么时候露面：面板铺到它下沿之后（h 为 0 的极端情况就让它立刻露面兜底）
            var group = rowBg.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;                  // 还没露面的行不许点（别让点空了的手感发生）
            _petPickRows.Add(new PetPickRow {
                Rt = rowBg.rectTransform, Group = group, Btn = rowBtn, Bg = rowBg, Label = label, Y = rowY,
                Start = h > 0f ? PetPickOpenSeconds * ((rowY + PetPickRowH) / h) * PetPickCoverK : 0f,
            });

   // 0 号是内建的大肥鱼：不给删除键（删了就没兜底形象了）
            if (i == 0 || string.IsNullOrEmpty(def.Source)) continue;
            string id = def.Id, name = def.Name;
            MakeRecordDeleteButton(rowBg.transform, "Del", "pet:" + id, () => DeletePetModel(id, name), () => SettlePetPickRow(idx));
        }
    }

 /// <summary>每帧（常驻段调）：把下拉从"一条缝"铺成一整列。铺完就一行都不写地早退。
 /// 行的露面时刻取决于面板铺到哪儿，所以面板还没盖住的行透明度还是 0。</summary>
    private static void TickPetPick() {
        if (_petPickRoot == null) {
            if (_petPickRows.Count > 0) _petPickRows.Clear();
            _petPickClosing = false;
            return;
        }
        float dt = Time.unscaledDeltaTime;
        if (_petPickClosing) { TickPetPickClose(dt); return; }
        if (_petPickCollapsing) { TickPetPickCollapse(dt); return; }
        if (_petPickOpenDone) return;
        _petPickT += dt;

        float k = Mathf.Clamp01(_petPickT / PetPickOpenSeconds);
        float e = 1f - (1f - k) * (1f - k) * (1f - k);      // 先快后慢：铺开那一下才利落
        _petPickRoot.sizeDelta = new Vector2(PetPickW, _petPickPanelH * e);
        SetPetPickPanelAlpha(_petPickPanelBase.a * Mathf.Lerp(PetPickDimFrom, 1f, e));
        SyncGlassNow();                                    // 矩形这一帧刚改，磨砂要同帧重算（否则会慢一帧、边上露出旧形状）

        bool done = true;
        for (int i = 0; i < _petPickRows.Count; i++) {
            var row = _petPickRows[i];
            if (row == null || row.Rt == null || row.Group == null) continue;
            float rk = Mathf.Clamp01((_petPickT - row.Start) / PetPickRowSeconds);
            if (rk < 1f) done = false;
            float re = 1f - (1f - rk) * (1f - rk) * (1f - rk);
            SetRectPx(row.Rt, PetPickRowX, row.Y - PetPickRowRise * (1f - re), PetPickW - 2f * PetPickRowX, PetPickRowInnerH);
            row.Group.alpha = re;
            row.Group.blocksRaycasts = rk >= 1f;
        }
        if (!done) return;
        _petPickOpenDone = true;
        _petPickRoot.sizeDelta = new Vector2(PetPickW, _petPickPanelH);   // 收口：不留浮点误差
        SetPetPickPanelAlpha(_petPickPanelBase.a);
    }

 /// <summary>选一个模型：切过去 + 落盘 + 收起下拉。闭包里抓的 idx 是"这一行现在排第几"，
 /// 合拢之后会被重绑（见 PetPickRebindRows），别把它当成建行时那个序号。</summary>
    private static void PetPickChoose(int idx) {
        if (idx < 0 || idx >= PetModels.Count) return;
        PetSwitchModel(idx);
        AutoFeatures.Save();
        RefreshPetModelText();
        ClosePetPick();
        Lg.Info("大肥鱼：模型 → " + PetModels[ResolvePetModel()].Name);
    }

 /// <summary>删除键那条链路走完之后来这里（挂在它的 OnSettled 上）：把那一行当场收掉。
 /// 数据早就删干净了（DeletePetModel 在"确认"那一下就跑完），这里只动画面 ——
 /// 删除键自己的对勾 / 桶身动画要先播完（RecDelHoldDeleted），那之前收行会显得没反馈。
 /// 前提是"刚好少了一行"：行数对不上（导入过 / 正在铺开 / 正在收）就退回整列重建。</summary>
    private static void SettlePetPickRow(int idx) {
        if (_petPickRoot == null) return;                        // 下拉已经关了：下次打开按新表重建
        if (_petPickClosing || _petPickCollapsing || !_petPickOpenDone) { RefreshPetPickIfOpen(); return; }
        if (idx < 0 || idx >= _petPickRows.Count) { RefreshPetPickIfOpen(); return; }
        if (_petPickRows.Count != PetModels.Count + 1) { RefreshPetPickIfOpen(); return; }
        StartPetPickCollapse(idx);
    }

 /// <summary>起合拢：先改掉"下面那些行该落到哪"（每行减一格），合拢的每一帧只负责把画面推过去。</summary>
    private static void StartPetPickCollapse(int idx) {
        _petPickCollapsing = true;
        _petPickCollapseIdx = idx;
        _petPickCollapseT = 0f;
        _petPickPanelHFrom = _petPickPanelH;
        _petPickPanelH = Mathf.Max(0f, _petPickPanelH - PetPickRowH);
        for (int i = 0; i < _petPickRows.Count; i++) {
            var row = _petPickRows[i];
            if (row == null) continue;
            if (i > idx) row.Y -= PetPickRowH;                   // 它上面少了一行，落点整体上移一格
            if (i == idx && row.Group != null) {
                row.Group.blocksRaycasts = false;                // 正在溶掉的那一行不再吃射线
                row.Group.interactable = false;
            }
        }
        Lg.Info("大肥鱼模型下拉：删掉第 " + idx + " 行，合拢一格");
    }

 /// <summary>合拢的一帧：被删的那行溶掉（透明度 + 往上抽一点），它下面的行往上流一格，面板同步变矮一行。</summary>
    private static void TickPetPickCollapse(float dt) {
        _petPickCollapseT += dt;
        float k = Mathf.Clamp01(_petPickCollapseT / PetPickCollapseSeconds);
        float e = EaseOut3(k);
        _petPickRoot.sizeDelta = new Vector2(PetPickW, Mathf.Lerp(_petPickPanelHFrom, _petPickPanelH, e));
        SyncGlassNow();                                          // 矩形这一帧刚改，磨砂要同帧重算（否则慢一帧、边上露出旧形状）
        for (int i = 0; i < _petPickRows.Count; i++) {
            var row = _petPickRows[i];
            if (row == null || row.Rt == null) continue;
            if (i == _petPickCollapseIdx) {
                if (row.Group != null) row.Group.alpha = 1f - e;
                SetRectPx(row.Rt, PetPickRowX, row.Y - PetPickRowRise * e, PetPickW - 2f * PetPickRowX, PetPickRowInnerH);
                continue;
            }
            float y = i > _petPickCollapseIdx ? row.Y + PetPickRowH * (1f - e) : row.Y;   // 下面的行往上流一格
            SetRectPx(row.Rt, PetPickRowX, y, PetPickW - 2f * PetPickRowX, PetPickRowInnerH);
        }
        if (k < 1f) return;
        var dead = _petPickRows[_petPickCollapseIdx];
        if (dead != null && dead.Rt != null) Object.Destroy(dead.Rt.gameObject);
        _petPickRows.RemoveAt(_petPickCollapseIdx);
        _petPickCollapsing = false;
        _petPickCollapseIdx = -1;
        _petPickRoot.sizeDelta = new Vector2(PetPickW, _petPickPanelH);   // 收口：不留浮点误差
        PetPickRebindRows();
        SyncGlassNow();
        Lg.Info("大肥鱼模型下拉：合拢完成，还剩 " + _petPickRows.Count + " 行");
    }

 /// <summary>合拢之后把每一行重绑一遍：行对象留着（删除键的登记、悬停的弹簧都还在），
 /// 但"我是第几个模型"整体往上挪了一格 —— 不重绑就会点到错位的模型（删第 2 个之后，点第 3 行会切到第 4 个）。
 /// 顺带把选中那行的底色重刷：当前模型被删掉时会退回 0 号大肥鱼，选中块要跟着回第 0 行。</summary>
    private static void PetPickRebindRows() {
        int cur = ResolvePetModel();
        for (int i = 0; i < _petPickRows.Count; i++) {
            var row = _petPickRows[i];
            if (row == null || row.Btn == null) continue;
            int idx = i;
            row.Btn.onClick.RemoveAllListeners();
            row.Btn.onClick.AddListener(() => PetPickChoose(idx));
            PetPickPaintRow(row, idx == cur);
        }
    }

 /// <summary>刷一行的选中 / 常态配色：选中 = 主题色实心块 + 深色字（跟导航排、设置页同一套语言）。</summary>
    private static void PetPickPaintRow(PetPickRow row, bool cur) {
        if (row == null) return;
        if (row.Bg != null) row.Bg.color = cur ? HighlightFillColor : new Color(1f, 1f, 1f, 0.06f);
        if (row.Label != null) row.Label.color = cur ? HighlightTextColor : Color.white;
        // 底色是业务改的：常态色直接告诉交互态层，别让它读当前颜色（收行那一下鼠标正停在那一行上，读到的是悬停色）
        SetUiBaseFill(row.Btn, row.Bg != null ? row.Bg.color : Color.white);
        RefreshUiTargetBase(row.Btn, false);          // 文字那一半按新字色重记
    }

 /// <summary>收起那一下：面板与每行一起淡掉，位置不动（行就不会跑到面板外面去），淡完销毁。</summary>
    private static void TickPetPickClose(float dt) {
        _petPickCloseT += dt;
        float c = Mathf.Clamp01(_petPickCloseT / PetPickCloseSeconds);
        SetPetPickPanelAlpha(_petPickPanelBase.a * (1f - c));
        for (int i = 0; i < _petPickRows.Count; i++) {
            var row = _petPickRows[i];
            if (row == null || row.Group == null) continue;
            row.Group.alpha = Mathf.Min(row.Group.alpha, 1f - c);   // 取小的：正在落进来的行不会先跳亮再灭
        }
        if (c >= 1f) DestroyPetPickNow();
    }

 /// <summary>写面板底色的透明度。磨砂拿这张图的 alpha 当"现在露几成"，所以淡出淡入都得走这里。</summary>
    private static void SetPetPickPanelAlpha(float a) {
        if (_petPickPanel == null) return;
        _petPickPanel.color = new Color(_petPickPanelBase.r, _petPickPanelBase.g, _petPickPanelBase.b, Mathf.Clamp01(a));
    }

 /// <summary>下拉面板的左边：右边缘与那一行按钮的右边缘对齐（下拉的常规做法），
 /// 再按画布右边缘夹一次 —— 之前是按按钮左边缘往右铺，右边多出 120 像素戳到界面外。</summary>
    private static float PetPickXFor(RectTransform anchor) {
        try {
            var c = _petPickCanvas.GetComponent<RectTransform>();
            Vector3[] cw = new Vector3[4];
            anchor.GetWorldCorners(cw);
            Vector3 leftBottom = c.InverseTransformPoint(cw[0]);    // 按钮左下角
            Vector3 rightTop = c.InverseTransformPoint(cw[2]);      // 按钮右上角
            float right = rightTop.x + c.rect.width * 0.5f;         // 画布本地 → 左上原点坐标
            float x = right - PetPickW;                             // 右边缘对齐
            return Mathf.Clamp(x, 24f, RefW - PetPickW - 24f);
        } catch { return 1200f; }
    }

    private static float PetPickTopFor(RectTransform anchor, float h) {
        try {
            var c = _petPickCanvas.GetComponent<RectTransform>();
            Vector3[] cw = new Vector3[4];
            anchor.GetWorldCorners(cw);
            Vector3 local = c.InverseTransformPoint(cw[3]);   // 行按钮左上角
            float top = c.rect.height * 0.5f - local.y;       // 画布本地 → 从画布顶往下数
            return Mathf.Clamp(top + 6f, 8f, Mathf.Max(8f, CanvasH() - h - 8f));
        } catch { return 400f; }
    }

 /// <summary>删一个导入的模型：先备份再删（玩家数据，删了不可逆）+ 重载模型表 +
 /// 删的正好是当前选的那个就退回大肥鱼 + 落盘 + 刷新那一行与下拉。
 /// 备份失败一律不删（跟导入备份同一条规矩）。</summary>
    private static void DeletePetModel(string id, string name) {
        try {
            string root = PetModelsFolder;
            if (string.IsNullOrEmpty(root)) return;
            string dir = System.IO.Path.Combine(root, id);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) {
                Lg.Info("删模型：目录不在 " + id);
                return;
            }
            string backup = System.IO.Path.Combine(Main.ModEntry.Path, "backup", "pet_models_removed",
                id + "_" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            try {
                System.IO.Directory.CreateDirectory(backup);
                foreach (var f in System.IO.Directory.GetFiles(dir)) {
                    System.IO.File.Copy(f, System.IO.Path.Combine(backup, System.IO.Path.GetFileName(f)), true);
                }
            } catch (System.Exception e) {
                Lg.Info("删模型：备份失败，已跳过（" + e.GetType().Name + "）");
                ShowToast(Lang.Tr("备份没成功，没敢删这个模型喵"));
                return;
            }
            System.IO.Directory.Delete(dir, true);
            Lg.Info("删模型：" + name + "（" + id + "）已备份到 " + backup);
            bool wasCurrent = ResolvePetModel() < PetModels.Count && PetModels[ResolvePetModel()].Id == id;
            ReloadPetModels();
            if (wasCurrent) { PetSwitchModel(0); AutoFeatures.Save(); }
            RefreshPetModelText();
            // 画面不在这里动手：删除键还要把对勾 / 桶身那套动画播完，收行交给它的 OnSettled（= SettlePetPickRow）。
            // 在这里整列重建的话，玩家看到的是"行还没反馈完，整片先闪一下"。
            Lg.Info("删模型：数据已删，等删除键播完再收行");
        } catch (System.Exception e) {
            Lg.Info("删模型失败：" + e.GetType().Name + " " + e.Message);
            ShowToast(Lang.Tr("删模型失败了，看日志喵"));
        }
    }
}
