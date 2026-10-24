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

/// <summary>PortalUI partial 模块：LevelPick（关卡选择/删除弹窗、列表行对象池、详情区刷新）</summary>
public static partial class PortalUI {

    private static void ConfirmEnterEditorDetail() {
        // 用当前选中谱面的默认文件进入编辑器（_detailPickedFile 优先）
        if (_selectedPath == null) { ShowToast(Lang.Tr("请先在列表选中一个谱面")); return; }
        var entry = LevelScanner.Entries.FirstOrDefault(x => x.FolderPath == _selectedPath);
        if (entry == null) return;
        var file = _detailPickedFile ?? LevelScanner.PickMainFile(entry.FolderPath);
        if (string.IsNullOrEmpty(file)) { ShowToast(Lang.Tr("没有可打开的 .adofai 文件")); return; }
        // 必须走 StartEnterTransition（动画放完才真的进），不能直接打开
        // "文件不像谱面就拦下来"这道防御挂在这条路上：详情区按钮与弹窗确认都走这里
        // （快捷键那条进关路不查 LooksLikeLevelFile）
        string whyNot = "";
        if (!LooksLikeLevelFile(file, out whyNot)) {
            Lg.Info("详情区开始游玩被拦下（" + whyNot + "）: " + file);
            ShowToast(Lang.TrFmt("这个谱面文件打不开（{0}）喵", whyNot));
            return;
        }
        StartEnterTransition(file);
    }

    /// <summary>创建行对象池：行内组件一次建齐后复用，避免滚动时反复建/拆</summary>
    private static void CreatePool(Transform content) {
        for (int i = 0; i < 24; i++) {
            var pool = new PoolRow();
            pool.Go = new GameObject("PoolRow", typeof(RectTransform));
            pool.Go.transform.SetParent(content, false);
            pool.Rt = pool.Go.GetComponent<RectTransform>();
            // 横向拉伸到 content 宽（两侧各留 20），纵向顶部锚定、高 76
            pool.Rt.anchorMin = new Vector2(0f, 1f);
            pool.Rt.anchorMax = new Vector2(1f, 1f);
            pool.Rt.pivot = new Vector2(0.5f, 1f);
            pool.Rt.offsetMin = new Vector2(20f, -ItemRowH);
            pool.Rt.offsetMax = new Vector2(-20f, 0f);
            pool.Go.SetActive(false);

            // 标头模式
            pool.HeaderObj = new GameObject("Header", typeof(RectTransform));
            pool.HeaderObj.transform.SetParent(pool.Go.transform, false);
            var hrt = pool.HeaderObj.GetComponent<RectTransform>();
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            pool.HeaderText = MakeText(pool.HeaderObj.transform, "H", 34, new Color(1f, 0.65f, 0.2f, 1f), TextAnchor.MiddleCenter);
            pool.HeaderText.fontStyle = FontStyle.Bold;
            var htrt = pool.HeaderText.rectTransform;
            htrt.anchorMin = new Vector2(0f, 0f); htrt.anchorMax = new Vector2(1f, 1f);
            htrt.offsetMin = new Vector2(10f, 0f); htrt.offsetMax = new Vector2(-10f, 16f);
            // 标头行平时只是文字，只有"没有匹配的谱面 · 点这里清空筛选条件"那一行要能点，
            // 所以常驻挂一个 Button，靠 HeaderText.raycastTarget 控制现在吃不吃点击
            // （false 时点击穿过去，标头行不挡列表滚动/点选）。
            // 按钮挂文字自身：文字所在物体有 CanvasRenderer，挂空物体上不保险
            var hesc = pool.HeaderText.gameObject.AddComponent<Button>();
            hesc.transition = Selectable.Transition.None;
            hesc.targetGraphic = pool.HeaderText;
            hesc.onClick.AddListener(() => {
                // 双保险：只有"库里有谱面、但被筛选筛空"这一档才真的清空
                int total = (LevelScanner.Entries == null) ? 0 : LevelScanner.Entries.Count;
                if (total == 0 || _display.Count > 0) return;
                ClearAllFilters();
            });
            pool.HeaderObj.SetActive(false);

            // 条目模式（行主体：用 MakeImage 创建保证 RectTransform 拉伸可靠）
            pool.Bg = MakeImage(pool.Go.transform, "Item", ItemColor);
            pool.Bg.sprite = RoundedSprite;
            pool.Bg.type = Image.Type.Sliced;
            pool.ItemObj = pool.Bg.gameObject;
            var iort = pool.Bg.rectTransform;
            iort.anchorMin = Vector2.zero; iort.anchorMax = Vector2.one;
            iort.offsetMin = Vector2.zero; iort.offsetMax = Vector2.zero;
            // 背景图必须保留（圆角九宫格 + 半透明底色）：它是悬停变色和右键菜单的射线目标。
            // 滚轮与拖拽由 TickGlobalWheel / TickGlobalDrag 全局处理，不依赖这里的射线
            pool.ItemBtn = pool.ItemObj.AddComponent<Button>();
            pool.ItemBtn.targetGraphic = pool.Bg;   // 只作为可交互图元登记（染色已交给 ButtonHover）
            // uGUI 自带的 ColorTint 也写同一个图元：按下压暗、高亮提亮，各带一次 0.1 秒渐变。
            // 行是对象池复用的，拖动滚动时这个染色会跟着"行换人"而闪，必须关掉它；
            // 底部那层底色只留 ButtonHover 一个写手（Toolkit 建按钮时也是同一条规矩）。
            pool.ItemBtn.transition = Selectable.Transition.None;
            pool.Hover = pool.ItemObj.AddComponent<ButtonHover>();
            pool.Hover.Normal = ItemColor;
            pool.Hover.IsSelected = false;
            pool.Hover.ApplyInstant();   // 初建：直接落位（真实的状态变化才走 Apply 淡入淡出）

            // 收藏按钮（行尾右侧）
            var favBtn = MakeButton(pool.ItemObj.transform, "Fav", Vector2.zero, new Vector2(34f, 34f), new Color(0f, 0f, 0f, 0f));
            var favRt = favBtn.GetComponent<RectTransform>();
            // 锚点必须锚右边缘（锚左边的话行宽一变就跑位）：行右边缘在屏幕 x≈1730，
            // 星心落在 x≈1700 中心距右边缘 -30
            favRt.anchorMin = favRt.anchorMax = new Vector2(1f, 0.50f);
            favRt.pivot = new Vector2(0.5f, 0.5f);
            favRt.anchoredPosition = new Vector2(-30f, 25f);
            // 收藏星用图标族的贴图，居中放在 34×34 的透明按钮里（贴图按原尺寸摆，拉伸就糊）
            pool.FavImg = IconImage(favBtn.transform, "FavIcon", IconShapes.Kind.Star, IconMd, IconFavOff);
            var fir = pool.FavImg.rectTransform;
            fir.anchorMin = fir.anchorMax = new Vector2(0.5f, 0.5f);
            fir.pivot = new Vector2(0.5f, 0.5f);
            fir.anchoredPosition = Vector2.zero;
            favBtn.onClick.AddListener(() => {
                _rowActionGuard = true;   // 防止冒泡触发行选中
                var e = CurrentEntry(pool);
                if (e == null) return;
                LevelScanner.ToggleFavorite(e.FolderPath);   // 立即写盘
                LevelScanner.SortEntries();   // 重排
                RefreshList();
            });

            // 曲名（上半部大字，右侧让出图标区）
            pool.Label = MakeText(pool.ItemObj.transform, "Label", ListFontSize, Color.white, TextAnchor.MiddleLeft);
            pool.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lrt = pool.Label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.50f);
            lrt.anchorMax = new Vector2(1f, 0.96f);
            lrt.offsetMin = new Vector2(50f, 0f);   // 给等级标(最前)+判定标让位
            lrt.offsetMax = new Vector2(-24f, 0f);   // 右侧留 24（行尾只剩收藏按钮）

            // 标记图标（最多两个：主标记 + 判定）
            pool.MarkIcon1 = MakeImage(pool.ItemObj.transform, "MarkIcon1", Color.white);
            pool.MarkIcon1.raycastTarget = false;
            pool.MarkIcon1.rectTransform.anchorMin = pool.MarkIcon1.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            pool.MarkIcon1.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            pool.MarkIcon1.rectTransform.sizeDelta = new Vector2(40f, 40f);
            pool.MarkIcon2 = MakeImage(pool.ItemObj.transform, "MarkIcon2", Color.white);
            pool.MarkIcon2.raycastTarget = false;
            pool.MarkIcon2.rectTransform.anchorMin = pool.MarkIcon2.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            pool.MarkIcon2.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            pool.MarkIcon2.rectTransform.sizeDelta = new Vector2(24f, 24f);

            // 作者小字（曲名下方，浅灰）
            pool.Author = MakeText(pool.ItemObj.transform, "Author", 22, new Color(0.72f, 0.72f, 0.82f, 1f), TextAnchor.MiddleLeft);
            pool.Author.horizontalOverflow = HorizontalWrapMode.Overflow;
            var art = pool.Author.rectTransform;
            art.anchorMin = new Vector2(0f, 0.05f);
            art.anchorMax = new Vector2(1f, 0.46f);
            art.offsetMin = new Vector2(50f, 0f);   // 与谱面名对齐（Label x=50）
            art.offsetMax = new Vector2(-24f, 0f);   // 与曲名同宽

            // 这一行只挂左键（选中）与右键（竖排菜单：重命名 / 标记 / 打开位置 / 删除）两件事，
            // 左键双击不再直接进关
            var rowTrigger = pool.ItemBtn.gameObject.AddComponent<EventTrigger>();
            AddTrigger(rowTrigger, EventTriggerType.PointerClick, data => {
                var pe = data as PointerEventData;
                if (pe == null) return;
                if (pe.button == PointerEventData.InputButton.Right) {
                    var eb = CurrentEntry(pool);
                    if (eb == null) return;
                    OpenRowContextMenu(eb, pe.position);
                }
            });

            // 左键单击：选中这一行
            pool.ItemBtn.onClick.AddListener(() => OnRowClicked(pool));

            _poolRows.Add(pool);
        }
    }

    /// <summary>刷新右侧详情区固定区（选中行后调用）：难度图标 + 曲名 + 收藏星 + 谱师/BPM/时长 + 通关|完成度</summary>
    private static void RefreshDetailInfo(LevelEntry entry) {
        if (entry == null) return;
        _detailEntry = entry;   // 存当前谱面（收藏星点击等用）
        ApplyDetailEmptyState();   // 选中后：显示详情内容，隐藏提示
        // "设为背景音乐"按钮已搬进右键菜单：那里按 IsBgmSourceOf(entry) 自己决定显示"设置"还是"取消"，
        // 这里不再同步按钮文字/颜色
        if (_detailTitle != null) _detailTitle.text = StripRichText(entry.Title);
        var mainFile = entry.FilePath;
        // 难度图标
        if (_detailMarkIcon != null) {
            string mark = MarkStore.Get(mainFile);
            if (string.IsNullOrEmpty(mark)) _detailMarkIcon.enabled = false;
            else {
                var spr = MarkStore.LoadIcon(mark);
                if (spr != null) { _detailMarkIcon.sprite = spr; _detailMarkIcon.type = Image.Type.Simple; _detailMarkIcon.preserveAspect = true; _detailMarkIcon.enabled = true; }
                else _detailMarkIcon.enabled = false;
            }
        }
        // 特殊图标（等级图标右边，最多 2 个；曲名往后顶给它们让位）
        if (_detailTitle != null && _detailMarkIcon != null) {
            var parent = _detailMarkIcon.transform.parent;
            foreach (var im in _detailSpecialIcons) if (im != null) Object.DestroyImmediate(im.gameObject);
            _detailSpecialIcons.Clear();
            int specialStartX = 62;   // 等级图标宽52 + 间距10
            int specialStep = 60;      // 特殊图标宽52 + 间距8，与等级图标同大
            int idx = 0;
            var specials = MarkStore.GetSpecials(mainFile);
            if (!string.IsNullOrEmpty(specials)) {
                foreach (var sp in specials.Split('|')) {
                    if (sp.Length == 0 || idx >= 2) break;
                    var spr = MarkStore.LoadIcon(sp);
                    var im = MakeImage(parent, "SpecialIcon" + idx, Color.white);
                    var irt = im.rectTransform;
                    irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                    irt.pivot = new Vector2(0f, 0.5f);
                    irt.sizeDelta = new Vector2(52f, 52f);
                    irt.anchoredPosition = new Vector2(specialStartX + idx * specialStep, 0f);
                    im.raycastTarget = false;
                    if (spr != null) { im.sprite = spr; im.type = Image.Type.Simple; im.preserveAspect = true; im.enabled = true; }
                    else im.enabled = false;
                    _detailSpecialIcons.Add(im);
                    idx++;
                }
            }
            var dtrt2 = _detailTitle.rectTransform;
            dtrt2.offsetMin = new Vector2(62f + idx * specialStep, 0f);   // 特殊图标占位，曲名右移
        }
        // 收藏星
        if (_detailFavImg != null) SetFavIcon(_detailFavImg, LevelScanner.IsFavorite(entry.FolderPath), IconLg);
        // 谱师/BPM/时长+格子（换行显示，曲师名太长截断）
        if (_detailMeta != null) {
            string artist = ReadAdofaiField(mainFile, "artist");
            string bpm = ReadAdofaiField(mainFile, "bpm");
            string dur = ReadLevelDuration(entry);
            string author = string.IsNullOrEmpty(artist) ? entry.OggName : artist;
            author = StripRichText(author);
            if (author.Length > 12) author = author.Substring(0, 12) + "…";
            int tiles = CountAngleData(mainFile);
            string bpmLine = "BPM：" + (string.IsNullOrEmpty(bpm) ? "--" : bpm);
            string tileLine = "格子数：" + (tiles > 0 ? tiles.ToString() : "--");
            string durLine = "音乐时长：" + (dur.Length > 0 ? dur : "--:--");
            _detailMeta.text = Lang.Tr("曲师：") + author + "\n" + bpmLine + "\n" + tileLine + "\n" + durLine;
        }
        // 通关数 | 完成度
        if (_detailScore != null) {
            int plays = LevelScanner.GetPlays(entry.FolderPath);
            int compl = LevelScanner.GetCompletion(entry.FolderPath);
            _detailScore.text = Lang.TrFmt("游玩 {0} 次    完成度 {1}%", plays, compl);
        }
        // 这里不再管备注输入框：「选择关卡」弹窗自己那座不受影响
    }

    /// <summary>删除确认框：框高，以及底部按钮行离框底多少（原来 25 太贴边，往上提到46）。
    /// 三个数（框高 / 离底 / 横向偏移）都提出来，改的时候不会漏掉另一半按钮</summary>
    private const float DeleteBoxH = 300f, DeleteBtnBottom = 46f, DeleteOkDx = 140f;

    private static void ShowDeleteDialog(LevelEntry entry) {
        if (_deleteCanvas != null) return;
        _deleteEntry = entry;
        _deleteFiles = true;
        _deleteCanvas = MakeCanvas("LevelLib_Delete", 83000);
        var bg = MakeDialogVeil(_deleteCanvas.transform);

        var box = MakeDialogBox(_deleteCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(680f, DeleteBoxH);

        var title = MakeTitle(box.transform, "删除谱面", 34, new Color(1f, 0.4f, 0.4f, 1f), TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        title.rectTransform.anchorMin = new Vector2(0f, 0.78f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;

        var msg = MakeText(box.transform, "Msg", 26, Color.white, TextAnchor.MiddleCenter);
        msg.text = Lang.Tr("确定删除谱面？\n") + entry.Title;
        msg.rectTransform.anchorMin = new Vector2(0.06f, 0.45f);
        msg.rectTransform.anchorMax = new Vector2(0.94f, 0.74f);
        msg.rectTransform.offsetMin = Vector2.zero;
        msg.rectTransform.offsetMax = Vector2.zero;

        // 选项：同时删除本地文件。全项目统一的开关外观（左标签 + 右胶囊）。
        // 整行点哪儿都算：开关那两片不吃射线，由这一行的 Button 接
        var toggleGo = new GameObject("DeleteFiles", typeof(RectTransform));
        toggleGo.transform.SetParent(box.transform, false);
        var tglRt = toggleGo.GetComponent<RectTransform>();
        tglRt.anchorMin = new Vector2(0.12f, 0.30f);
        tglRt.anchorMax = new Vector2(0.88f, 0.42f);
        tglRt.offsetMin = Vector2.zero;
        tglRt.offsetMax = Vector2.zero;
        var tglImg = toggleGo.AddComponent<Image>();
        tglImg.color = ItemColor;
        tglImg.sprite = RoundedSprite;
        tglImg.type = Image.Type.Sliced;
        var tglBtn = toggleGo.AddComponent<Button>();
        tglBtn.targetGraphic = tglImg;
        Image tglKnob;
        var tglTrack = MakeSwitchVisual(toggleGo.transform, "Switch", out tglKnob);
        ApplySwitch(tglTrack, tglKnob, _deleteFiles);        // 建的时候即时摆位，不滑
        tglBtn.onClick.AddListener(() => {
            _deleteFiles = !_deleteFiles;
            ApplySwitch(tglTrack, tglKnob, _deleteFiles, true);   // 点出来的是滑过去
        });
        var tglText = MakeText(toggleGo.transform, "同时删除本地文件", 24, Color.white, TextAnchor.MiddleLeft);
        tglText.rectTransform.anchorMin = Vector2.zero;
        tglText.rectTransform.anchorMax = Vector2.one;
        tglText.rectTransform.offsetMin = new Vector2(16f, 0f);
        tglText.rectTransform.offsetMax = new Vector2(-SwitchRowLabelInset, 0f);

        // 框高与按钮行的纵向位置都从下面这两个常量推：原来按钮行离框底只有 25px，
        // 视觉上贴着底边（作者要求往上移一些），改成 DeleteBtnBottom = 46。
        var ok = MakeButton(box.transform, Lang.Tr("确定删除"), new Vector2(DeleteOkDx, -(DeleteBoxH * 0.5f - DeleteBtnBottom)),
                            new Vector2(200f, 44f), new Color(0.6f, 0.22f, 0.22f, 1f));
        var okText = MakeText(ok.transform, "确定删除", 26, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => ConfirmDelete());

        var cancel = MakeButton(box.transform, Lang.Tr("取消"), new Vector2(-DeleteOkDx, -(DeleteBoxH * 0.5f - DeleteBtnBottom)),
                                new Vector2(140f, 44f), ItemColor);
        var cancelText = MakeText(cancel.transform, "取消", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => CloseDeleteDialog());
    }

    private static void ShowLevelPickDialog(LevelEntry entry) {
        if (_pickCanvas != null) return;
        _pickEntry = entry;
        _pickFolder = entry.FolderPath;
        _pickFiles.Clear();
        try {
            foreach (var f in Directory.GetFiles(entry.FolderPath, "*.adofai")) _pickFiles.Add(f);
        } catch { }
        // 排序：main 第一 → level 第二 → 其余中间 → backup（含数字后缀）最后；同类按名字
        _pickFiles.Sort((a, b) => {
            int r = PickFileRank(a).CompareTo(PickFileRank(b));
            return r != 0 ? r : string.Compare(a, b, System.StringComparison.OrdinalIgnoreCase);
        });
        _pickedFile = _pickFiles.Count > 0 ? _pickFiles[0] : null;   // 默认选第一个

        _pickCanvas = MakeCanvas("LevelLib_Pick", 83000);
        var bg = MakeDialogVeil(_pickCanvas.transform);

        var box = MakeDialogBox(_pickCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(1200f, 680f);

        var title = MakeTitle(box.transform, "Title", 34, new Color(1f, 0.65f, 0.2f, 1f), TextAnchor.MiddleCenter);
        title.text = Lang.Tr("选择关卡文件");
        title.rectTransform.anchorMin = new Vector2(0f, 0.92f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;

        var nameLabel = MakeText(box.transform, "Folder", 24, Color.white, TextAnchor.MiddleCenter);
        nameLabel.text = entry.Title;
        nameLabel.rectTransform.anchorMin = new Vector2(0f, 0.84f);
        nameLabel.rectTransform.anchorMax = new Vector2(1f, 0.90f);
        nameLabel.rectTransform.offsetMin = Vector2.zero;
        nameLabel.rectTransform.offsetMax = Vector2.zero;

        // 左侧 tab 列（谱面信息 / 通关记录 / 寄录图 / 数据管理；右侧文件列表固定不随 tab 变）
        _pickTabInfo = MakeButton(box.transform, "TabInfo", Vector2.zero, new Vector2(120f, 52f), Accent);
        var tirt = _pickTabInfo.GetComponent<RectTransform>();
        tirt.anchorMin = new Vector2(0.01f, 0.73f); tirt.anchorMax = new Vector2(0.115f, 0.80f);
        tirt.offsetMin = Vector2.zero; tirt.offsetMax = Vector2.zero;
        var tiText = MakeText(_pickTabInfo.transform, "谱面信息", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(tiText.rectTransform, Vector2.zero, Vector2.one);
        _pickTabHeat = MakeButton(box.transform, "TabHeat", Vector2.zero, new Vector2(120f, 52f), ItemColor);
        var thrt = _pickTabHeat.GetComponent<RectTransform>();
        thrt.anchorMin = new Vector2(0.01f, 0.55f); thrt.anchorMax = new Vector2(0.115f, 0.62f);   // 通关记录下方
        thrt.offsetMin = Vector2.zero; thrt.offsetMax = Vector2.zero;
        var thText = MakeText(_pickTabHeat.transform, "寄录图", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(thText.rectTransform, Vector2.zero, Vector2.one);
        _pickTabInfo.onClick.AddListener(() => { _pickTab = 0; RefreshPickTabs(); });
        _pickTabHeat.onClick.AddListener(() => { _pickTab = 2; RefreshPickTabs(); RefreshHeatMap(_pickFolder); });
        _pickTabRecord = MakeButton(box.transform, "TabRecord", Vector2.zero, new Vector2(120f, 52f), ItemColor);
        var trrt = _pickTabRecord.GetComponent<RectTransform>();
        trrt.anchorMin = new Vector2(0.01f, 0.64f); trrt.anchorMax = new Vector2(0.115f, 0.71f);
        trrt.offsetMin = Vector2.zero; trrt.offsetMax = Vector2.zero;
        var trText = MakeText(_pickTabRecord.transform, "通关记录", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(trText.rectTransform, Vector2.zero, Vector2.one);
        _pickTabRecord.onClick.AddListener(() => { _pickTab = 1; RefreshPickTabs(); RefreshPickRecords(_pickFolder); });
        _pickTabData = MakeButton(box.transform, "TabData", Vector2.zero, new Vector2(120f, 52f), ItemColor);
        var tdrt = _pickTabData.GetComponent<RectTransform>();
        tdrt.anchorMin = new Vector2(0.01f, 0.46f); tdrt.anchorMax = new Vector2(0.115f, 0.53f);   // 四个 tab 高都 0.07、间距 0.02
        tdrt.offsetMin = Vector2.zero; tdrt.offsetMax = Vector2.zero;
        var tdText = MakeText(_pickTabData.transform, "数据管理", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(tdText.rectTransform, Vector2.zero, Vector2.one);
        _pickTabData.onClick.AddListener(() => { _pickTab = 3; RefreshPickTabs(); });
        // 内容区容器（tab 切换显示）。必须用本弹窗自己的字段（_pickWinXxx）：
        // _pickRecordArea 那几个归「详情面板」所有（PortalUI.DetailPanel.cs），一旦共用，
        // CloseLevelPickDialog 会把它们置 null，详情面板就会重建第二套容器、两套同时显示。
        _pickInfoArea = new GameObject("PickInfoArea", typeof(RectTransform));
        _pickInfoArea.transform.SetParent(box.transform, false);
        var ria = _pickInfoArea.GetComponent<RectTransform>();
        ria.anchorMin = new Vector2(0.13f, 0.10f); ria.anchorMax = new Vector2(0.48f, 0.80f);
        ria.offsetMin = Vector2.zero; ria.offsetMax = Vector2.zero;
        _pickWinHeatArea = new GameObject("PickHeatArea", typeof(RectTransform));
        _pickWinHeatArea.transform.SetParent(box.transform, false);
        var rha = _pickWinHeatArea.GetComponent<RectTransform>();
        rha.anchorMin = new Vector2(0.13f, 0.10f); rha.anchorMax = new Vector2(0.48f, 0.80f);
        rha.offsetMin = Vector2.zero; rha.offsetMax = Vector2.zero;
        _pickWinHeatArea.SetActive(false);
        _pickWinRecordArea = new GameObject("PickRecordArea", typeof(RectTransform));
        _pickWinRecordArea.transform.SetParent(box.transform, false);
        var rra = _pickWinRecordArea.GetComponent<RectTransform>();
        rra.anchorMin = new Vector2(0.13f, 0.10f); rra.anchorMax = new Vector2(0.48f, 0.80f);
        rra.offsetMin = Vector2.zero; rra.offsetMax = Vector2.zero;
        _pickWinRecordArea.SetActive(false);
        _pickWinDataArea = new GameObject("PickDataArea", typeof(RectTransform));
        _pickWinDataArea.transform.SetParent(box.transform, false);
        var rda = _pickWinDataArea.GetComponent<RectTransform>();
        rda.anchorMin = new Vector2(0.13f, 0.10f); rda.anchorMax = new Vector2(0.48f, 0.80f);
        rda.offsetMin = Vector2.zero; rda.offsetMax = Vector2.zero;
        _pickWinDataArea.SetActive(false);

        // 谱面信息面板（挂到 tab1 内容区）
        var infoPanel = MakeImage(_pickInfoArea.transform, "InfoPanel", new Color(0.13f, 0.15f, 0.22f, 1f));
        infoPanel.rectTransform.anchorMin = Vector2.zero;
        infoPanel.rectTransform.anchorMax = Vector2.one;
        infoPanel.rectTransform.offsetMin = Vector2.zero; infoPanel.rectTransform.offsetMax = Vector2.zero;
        var infoTitle = MakeTitle(infoPanel.transform, "IT", 30, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleLeft);
        infoTitle.text = Lang.Tr("谱面信息");
        infoTitle.fontStyle = FontStyle.Bold;
        var itrt = infoTitle.rectTransform;
        itrt.anchorMin = new Vector2(0f, 0.86f); itrt.anchorMax = new Vector2(1f, 1f);
        itrt.offsetMin = new Vector2(24f, 0f); itrt.offsetMax = new Vector2(-12f, 0f);
        // 音乐名称（固定，长名称换行不挤数据区）
        _pickNameText = MakeText(infoPanel.transform, "Name", 26, Color.white, TextAnchor.UpperLeft);
        _pickNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _pickNameText.verticalOverflow = VerticalWrapMode.Truncate;
        _pickNameText.supportRichText = false;
        var nrt2 = _pickNameText.rectTransform;
        nrt2.anchorMin = new Vector2(0.04f, 0.60f); nrt2.anchorMax = new Vector2(0.96f, 0.72f);
        nrt2.offsetMin = Vector2.zero; nrt2.offsetMax = Vector2.zero;
        // 数据滚动区（音乐长度/作者/BPM/格子数，可滑动；无背景框）
        var dataView = MakeImage(infoPanel.transform, "DataView", new Color(0f, 0f, 0f, 0f));
        dataView.rectTransform.anchorMin = new Vector2(0.04f, 0.27f);
        dataView.rectTransform.anchorMax = new Vector2(0.96f, 0.61f);
        dataView.rectTransform.offsetMin = Vector2.zero; dataView.rectTransform.offsetMax = Vector2.zero;
        dataView.gameObject.AddComponent<RectMask2D>();
        var dataScroll = dataView.gameObject.AddComponent<ScrollRect>();
        dataScroll.horizontal = false;
        dataScroll.vertical = true;
        dataScroll.scrollSensitivity = 20f;
        dataScroll.movementType = ScrollRect.MovementType.Clamped;   // 不飞出屏幕
        dataScroll.viewport = dataView.GetComponent<RectTransform>();
        var dataContent = new GameObject("DataContent", typeof(RectTransform));
        dataContent.transform.SetParent(dataView.transform, false);
        var dcrt = dataContent.GetComponent<RectTransform>();
        dcrt.anchorMin = new Vector2(0f, 1f); dcrt.anchorMax = new Vector2(1f, 1f);
        dcrt.pivot = new Vector2(0.5f, 1f);
        dcrt.sizeDelta = new Vector2(0f, 0f);
        var dv = dataContent.AddComponent<VerticalLayoutGroup>();
        dv.spacing = 6f;
        dv.childAlignment = TextAnchor.UpperLeft;
        dv.childControlWidth = true;
        dv.childControlHeight = true;
        var df = dataContent.AddComponent<ContentSizeFitter>();
        df.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        dataScroll.content = dcrt;
        dataScroll.verticalNormalizedPosition = 1f;   // 初始显示顶部（数据）
        _pickInfoText = MakeText(dataContent.transform, "Info", 26, Color.white, TextAnchor.UpperLeft);
        _pickInfoText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _pickInfoText.lineSpacing = 1.4f;
        _pickInfoText.supportRichText = false;   // 名称里的 <size>/<color> 代码不解析，原样剥离显示
        // 布局（VLayout childControlWidth/Height=true）自动按内容定宽高，content 高度 = 内容高度，滚动距离正常
        var iLe = _pickInfoText.gameObject.AddComponent<LayoutElement>();
        iLe.flexibleWidth = 1f;
        // 难度行：标签 + 标记图片（没有标记就空着）
        var diffLabel = MakeText(infoPanel.transform, "DiffLabel", 26, Color.white, TextAnchor.MiddleLeft);
        diffLabel.text = Lang.Tr("难度：");
        var dlrt = diffLabel.rectTransform;
        dlrt.anchorMin = new Vector2(0.05f, 0.76f); dlrt.anchorMax = new Vector2(0.35f, 0.84f);
        dlrt.offsetMin = Vector2.zero; dlrt.offsetMax = Vector2.zero;
        _pickMarkIcon = MakeImage(infoPanel.transform, "MarkIcon", Color.white);
        _pickMarkIcon.raycastTarget = false;
        var mirt = _pickMarkIcon.rectTransform;
        mirt.anchorMin = mirt.anchorMax = new Vector2(0.38f, 0.80f);
        mirt.pivot = new Vector2(0f, 0.5f);
        mirt.sizeDelta = new Vector2(44f, 44f);
        mirt.anchoredPosition = new Vector2(-75f, 0f);   // 难度图片相对 0.38 锚点的偏移：左移 100 再右移 25
        _pickMarkIcon.enabled = false;
        // 完成度（难度行右侧，固定不滚动；通关无检查点=100%，否则历史最远死亡段）
        _pickCompletionText = MakeText(infoPanel.transform, "Completion", 26, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleRight);
        _pickCompletionText.text = "";
        _pickCompletionText.fontStyle = FontStyle.Bold;
        var crt2 = _pickCompletionText.rectTransform;
        crt2.anchorMin = new Vector2(0.50f, 0.76f); crt2.anchorMax = new Vector2(0.98f, 0.84f);
        crt2.offsetMin = new Vector2(-130f, 0f); crt2.offsetMax = new Vector2(-130f, 0f);   // 完成度相对 0.50 锚点的偏移：左移 100 再左移 30
        // 备注区：输入框 + 确认按钮（自由输入，点确认立刻保存；无标签，框更大）
        var noteBg = MakeImage(infoPanel.transform, "NoteBg", new Color(0.08f, 0.1f, 0.16f, 1f));
        var nbrt = noteBg.rectTransform;
        nbrt.anchorMin = new Vector2(0.03f, 0.03f); nbrt.anchorMax = new Vector2(0.97f, 0.20f);
        nbrt.offsetMin = Vector2.zero; nbrt.offsetMax = Vector2.zero;
        var noteText = MakeText(noteBg.transform, "NoteText", 24, Color.white, TextAnchor.MiddleLeft);
        noteText.text = "";
        noteText.horizontalOverflow = HorizontalWrapMode.Wrap;
        noteText.verticalOverflow = VerticalWrapMode.Truncate;
        Stretch(noteText.rectTransform, Vector2.zero, Vector2.one);
        _pickNoteInput = noteBg.gameObject.AddComponent<InputField>();
        _pickNoteInput.textComponent = noteText;
        _pickNoteInput.lineType = InputField.LineType.MultiLineNewline;
        _pickNoteInput.onValueChanged.AddListener(_ => { _pickNoteDirty = true; _pickNoteSaveTimer = 0.4f; });   // 输入即时保存（防抖 0.4s）
        var notePlaceholder = MakeText(noteBg.transform, "PH", 22, new Color(0.5f, 0.5f, 0.5f, 1f), TextAnchor.MiddleLeft);
        notePlaceholder.text = Lang.Tr("写点备注…");
        Stretch(notePlaceholder.rectTransform, Vector2.zero, Vector2.one);
        _pickNoteInput.placeholder = notePlaceholder;

        // 文件列表（右侧，固定不随 tab 变）
        var listView = MakeImage(box.transform, "ListView", ItemColor);
        listView.rectTransform.anchorMin = new Vector2(0.52f, 0.10f);
        listView.rectTransform.anchorMax = new Vector2(0.985f, 0.80f);
        listView.rectTransform.offsetMin = Vector2.zero;
        listView.rectTransform.offsetMax = Vector2.zero;
        listView.gameObject.AddComponent<RectMask2D>();
        var pickScroll = listView.gameObject.AddComponent<ScrollRect>();
        pickScroll.horizontal = false;
        pickScroll.vertical = true;
        pickScroll.scrollSensitivity = 48f;
        pickScroll.viewport = listView.GetComponent<RectTransform>();
        var pContent = new GameObject("Content", typeof(RectTransform));
        pContent.transform.SetParent(listView.transform, false);
        var pcrt = pContent.GetComponent<RectTransform>();
        pcrt.anchorMin = new Vector2(0f, 1f);
        pcrt.anchorMax = new Vector2(1f, 1f);
        pcrt.pivot = new Vector2(0.5f, 1f);
        pcrt.sizeDelta = new Vector2(0f, 0f);
        var pv = pContent.AddComponent<VerticalLayoutGroup>();
        pv.spacing = 4f;
        pv.childAlignment = TextAnchor.UpperCenter;
        pv.childControlWidth = true;
        pv.childControlHeight = false;
        var pf = pContent.AddComponent<ContentSizeFitter>();
        pf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        pickScroll.content = pcrt;

        for (int i = 0; i < _pickFiles.Count; i++) {
            string f = _pickFiles[i];
            int idx = i;
            var row = MakeButton(pContent.transform, "Pick_" + i, Vector2.zero, new Vector2(0f, 44f), idx == 0 ? Accent : ItemColor);
            row.transform.SetParent(pContent.transform, false);
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(-24f, 44f);
            var t = MakeText(row.transform, "T", 24, Color.white, TextAnchor.MiddleLeft);
            t.text = Path.GetFileName(f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(16f, 0f);
            trt.offsetMax = new Vector2(-80f, 0f);   // 让出右侧删除按钮
            // 删除按钮（行右侧）：确认后删除该 .adofai 文件
            var delBtn = MakeButton(row.transform, "Del", new Vector2(-40f, 0f), new Vector2(64f, 34f), new Color(0.55f, 0.22f, 0.22f, 1f));
            var drt = delBtn.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            var delText = MakeText(delBtn.transform, "删除", 17, Color.white, TextAnchor.MiddleCenter);
            Stretch(delText.rectTransform, Vector2.zero, Vector2.one);
            string ff = f;
            delBtn.onClick.AddListener(() => ShowPickDeleteConfirm(ff));
            row.onClick.AddListener(() => {
                _pickedFile = f;
                RefreshPickInfo(f);   // 切换文件同步刷新左侧信息
                for (int j = 0; j < _pickItems.Count; j++)
                    SetUiSelectFill(_pickItems[j].GetComponent<Button>(), j == idx ? Accent : ItemColor);
            });
            _pickItems.Add(row.gameObject);
        }
        if (_pickFiles.Count == 0) {
            var none = MakeText(pContent.transform, "None", 24, new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleCenter);
            none.text = Lang.Tr("文件夹里没有 .adofai 文件");
            none.rectTransform.sizeDelta = new Vector2(500f, 44f);
        }

        // 确认进入（默认已选第一个文件）
        var ok = MakeButton(box.transform, "Ok", new Vector2(130f, -305f), new Vector2(220f, 46f), Accent);
        var okText = MakeText(ok.transform, "确认进入编辑器", 26, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => {
            if (string.IsNullOrEmpty(_pickedFile)) return;
            string p = _pickedFile;
            CloseLevelPickDialog();
            ConfirmOpen(p);
        });

        // 取消（保持选中，只关弹窗）
        var cancel = MakeButton(box.transform, "Cancel", new Vector2(-130f, -305f), new Vector2(140f, 46f), ItemColor);
        var cancelText = MakeText(cancel.transform, "取消", 26, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => CloseLevelPickDialog());

        // 初始信息：默认选中的文件
        RefreshPickInfo(_pickedFile);
        RefreshPickTabs();
        MakePickHeatArea(_pickWinHeatArea);   // 寄录图区（_pickTab == 2）
        MakePickRecordArea();   // 通关记录区（_pickTab == 1）
        MakePickDataArea();   // 数据管理区（_pickTab == 3）
    }

    private static void MakePickDataArea() {
        if (_pickWinDataArea == null) return;
        // 幂等：先清空旧子节点再重建，避免切 tab 反复添加导致残留/堆积
        for (int i = _pickWinDataArea.transform.childCount - 1; i >= 0; i--)
            Object.Destroy(_pickWinDataArea.transform.GetChild(i).gameObject);
        var dt = MakeText(_pickWinDataArea.transform, "DT", 26, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleLeft);
        dt.text = Lang.Tr("数据管理");
        dt.fontStyle = FontStyle.Bold;
        var dtrt = dt.rectTransform;
        dtrt.anchorMin = new Vector2(0.05f, 0.88f); dtrt.anchorMax = new Vector2(0.95f, 1f);
        dtrt.offsetMin = Vector2.zero; dtrt.offsetMax = Vector2.zero;
        var desc = MakeText(_pickWinDataArea.transform, "Desc", 20, new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.UpperLeft);
        desc.text = Lang.Tr("清空操作不可恢复，请谨慎喵");
        var drt2 = desc.rectTransform;
        drt2.anchorMin = new Vector2(0.05f, 0.76f); drt2.anchorMax = new Vector2(0.95f, 0.84f);
        drt2.offsetMin = Vector2.zero; drt2.offsetMax = Vector2.zero;
        // 清空游玩记录（游玩次数 + 完成度）
        var btn1 = MakeButton(_pickWinDataArea.transform, "ClearPlays", Vector2.zero, new Vector2(300f, 54f), new Color(0.55f, 0.22f, 0.22f, 1f));
        var b1rt = btn1.GetComponent<RectTransform>();
        b1rt.anchorMin = new Vector2(0.05f, 0.60f); b1rt.anchorMax = new Vector2(0.95f, 0.72f);
        b1rt.offsetMin = Vector2.zero; b1rt.offsetMax = Vector2.zero;
        var b1t = MakeText(btn1.transform, "清空游玩记录", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(b1t.rectTransform, Vector2.zero, Vector2.one);
        btn1.onClick.AddListener(() => ShowDataClearConfirm("清空游玩记录", "确定清空所有谱面的游玩次数和完成度？\n（游玩次数/完成度全部归零，通关记录/寄录图保留）", () => {
            LevelScanner.ClearPlaysAndProgress();
            ShowToast(Lang.Tr("游玩记录已清空喵"));
            RefreshList();
        }));
        // 清空寄录图
        var btn2 = MakeButton(_pickWinDataArea.transform, "ClearHeat", Vector2.zero, new Vector2(300f, 54f), new Color(0.55f, 0.22f, 0.22f, 1f));
        var b2rt = btn2.GetComponent<RectTransform>();
        b2rt.anchorMin = new Vector2(0.05f, 0.44f); b2rt.anchorMax = new Vector2(0.95f, 0.56f);
        b2rt.offsetMin = Vector2.zero; b2rt.offsetMax = Vector2.zero;
        var b2t = MakeText(btn2.transform, "清空寄录图", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(b2t.rectTransform, Vector2.zero, Vector2.one);
        btn2.onClick.AddListener(() => ShowDataClearConfirm("清空寄录图", "确定清空所有谱面的寄录？", () => {
            LevelScanner.ClearDeathMap();
            ShowToast(Lang.Tr("寄录图已清空喵"));
            RefreshHeatMap(_selectedPath);
        }));
    }

    /// <summary>刷新通关记录列表（xacc 降序，谁高谁在上，编序号）</summary>
    private static void RefreshPickRecords(string folder) {
        if (_recordScroll == null || _recordScroll.content == null) return;
        var content = _recordScroll.content;
        var list = LevelScanner.GetClearRecords(folder);
    // 被删掉的那几条先挑出来淡出退场（要排在销毁旧行之前），剩下的旧行才交给下面销毁换新
        RecFlowRetire("pick:", _recPickRows, list);
        for (int i = content.childCount - 1; i >= 0; i--) {
            var oldRow = content.GetChild(i);
            if (RecRowIsDying(oldRow)) continue;   // 退场中的行留给退场那段收尾
            Object.Destroy(oldRow.gameObject);
        }
        if (list.Count == 0) {
            if (_recordEmpty != null) _recordEmpty.gameObject.SetActive(true);
            content.sizeDelta = new Vector2(0f, 0f);
            return;
        }
        if (_recordEmpty != null) _recordEmpty.gameObject.SetActive(false);
    // 还留着旧身份才开让位窗口：换谱面时两份名单没有交集，整列直接落位、不做无谓的滑动
        bool animateRecords = RecFlowHasAny(_recPickRows, list);
        RecFlowHold("pick:", _recPickRows);
        if (animateRecords) FlowWindow("通关记录删除", MoSmall);
        for (int i = 0; i < list.Count; i++) {
            var r = list[i];
            var row = MakeImage(content.transform, "Row" + i, i % 2 == 0 ? new Color(0.13f, 0.16f, 0.24f, 0.9f) : new Color(0.1f, 0.12f, 0.19f, 0.9f));
            var rrt = row.rectTransform;
            rrt.anchorMin = new Vector2(0f, 1f); rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(-RecListPadX * 2f, RecPickRowH);
    // 位置交给流式引擎：见过的身份从旧位滑过来，没见过的直接落位（见 PortalUI.RecordList）
            RecFlowPlace("pick:", _recPickRows, r.Time, rrt, RecRowY(i, RecPickRowH));
            // 行首判定模式图（该次通关用的宽/标/严判）；旧记录 Slot < 0，不显示
            string judgeFile = null;
            if (r.Slot >= 0) {
                string judgeIcon = r.Slot == 0 ? "宽判" : r.Slot == 2 ? "严判" : "标判";
                judgeFile = MarkStore.JudgeFile.TryGetValue(judgeIcon, out var jf) ? jf : judgeIcon;
            }
            var jIcon = MakeImage(row.transform, "J", Color.white);
            jIcon.raycastTarget = false;
            var jrt = jIcon.rectTransform;
            jrt.anchorMin = jrt.anchorMax = new Vector2(0f, 0.5f);
            jrt.pivot = new Vector2(0f, 0.5f);
            jrt.sizeDelta = new Vector2(30f, 30f);
            jrt.anchoredPosition = new Vector2(8f, 0f);
            var jspr = string.IsNullOrEmpty(judgeFile) ? null : MarkStore.LoadIcon(judgeFile);
            if (jspr != null) { jIcon.sprite = jspr; jIcon.type = Image.Type.Simple; jIcon.preserveAspect = true; jIcon.enabled = true; }
            else jIcon.enabled = false;
            var t = MakeText(row.transform, "T", 24, Color.white, TextAnchor.MiddleLeft);
            t.supportRichText = true;
            t.text = "#" + (i + 1) + SpeedTag(r)
                + "  <color=#FF4D4D>" + r.Early + "</color>  <color=#E68A2E>" + r.TooFast + "</color>  <color=#FFE64D>" + r.Fast + "</color>  <color=#66E666>" + r.Perfect + "</color>  <color=#FFE64D>" + r.Slow + "</color>  <color=#E68A2E>" + r.TooSlow + "</color>"
                + "  <color=#9AA5B1>" + System.DateTimeOffset.FromUnixTimeSeconds(r.Time).LocalDateTime.ToString("yyyy-MM-dd") + "</color>";
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(0.72f, 1f);
            trt.offsetMin = new Vector2(48f, 0f); trt.offsetMax = new Vector2(0f, 0f);
            var xaccT = MakeText(row.transform, "X", 20, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleRight);
            xaccT.text = r.Xacc.ToString("F2") + "%";
            xaccT.fontStyle = FontStyle.Bold;
            var xrt = xaccT.rectTransform;
            xrt.anchorMin = new Vector2(0.68f, 0f); xrt.anchorMax = new Vector2(0.86f, 1f);
            xrt.offsetMin = new Vector2(0f, 0f); xrt.offsetMax = new Vector2(-6f, 0f);
            // 行尾删除键：与详情区通关记录那一条是同一个控件（就地确认，动效同一套）
            string recFolder = folder;
            long recTime = r.Time;
            MakeRecordDeleteButton(row.transform, "Del", "pick:" + recFolder,
                () => DoDeleteClearRecord(recFolder, recTime),
                () => RefreshRecordsAfterDelete(recFolder));
        }
        content.sizeDelta = new Vector2(0f, RecListHeight(list.Count, RecPickRowH, 0f));
    }

    private static void RefreshPickInfo(string file) {
        if (_pickInfoText == null) return;
        if (_pickNoteInput != null && _pickNoteFile != null && _pickNoteFile != file && _pickedFile != null) {
            LevelScanner.SetNote(_pickNoteFile, _pickNoteInput.text);   // 切文件前先把上一个文件的备注存下来（防丢）
        }
        _pickNoteFile = file;
        if (_pickNoteInput != null) {
            string note = LevelScanner.GetNote(file);
            _pickNoteInput.text = note ?? "";
        }
        string song = ReadAdofaiField(file, "song");
        if (string.IsNullOrEmpty(song) && _pickEntry != null) song = _pickEntry.Title;   // 没有音乐名就用列表谱面名
        string artist = ReadAdofaiField(file, "artist");
        string bpm = ReadAdofaiField(file, "bpm");
        int tiles = CountAngleData(file);
        // 音乐长度：递归扫文件夹里所有音频（ogg/wav/mp3）取最长的实际时长
        string dur = "";
        try {
            double maxDur = 0d;
            if (!string.IsNullOrEmpty(file)) {
                string folder = Path.GetDirectoryName(file);
                if (Directory.Exists(folder)) {
                    foreach (var af in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)) {
                        string ext = Path.GetExtension(af).ToLowerInvariant();
                        if (ext == ".ogg" || ext == ".wav" || ext == ".mp3") {
                            double d = ReadAudioDuration(af);
                            if (d > maxDur) maxDur = d;
                        }
                    }
                }
            }
            if (maxDur > 0d) {
                int totalSec = (int)System.Math.Round(maxDur);
                dur = (totalSec / 60) + ":" + (totalSec % 60).ToString("00");
            }
        } catch { }
        _pickNameText.text = Lang.Tr("音乐名称：") + (string.IsNullOrEmpty(song) ? "" : StripRichText(song));
        _pickInfoText.text =
            "音乐长度：" + (dur.Length > 0 ? dur : "") + "\n"
            + "谱面作者：" + (string.IsNullOrEmpty(artist) ? "" : StripRichText(artist)) + "\n"
            + "基础 BPM：" + (string.IsNullOrEmpty(bpm) ? "" : bpm) + "\n"
            + "格子数：" + (tiles > 0 ? tiles.ToString() : "");
        if (_pickCompletionText != null) _pickCompletionText.text = Lang.Tr("完成度：") + LevelScanner.GetCompletion(Path.GetDirectoryName(file)) + "%";
        // 难度标记图片（主标记，没有就空着）
        if (_pickMarkIcon != null) {
            string mark = _pickEntry != null ? MarkStore.Get(_pickEntry.FilePath) : null;
            if (string.IsNullOrEmpty(mark)) {
                _pickMarkIcon.enabled = false;
            } else {
                var spr = MarkStore.LoadIcon(mark);
                if (spr != null) {
                    _pickMarkIcon.sprite = spr;
                    _pickMarkIcon.type = Image.Type.Simple;
                    _pickMarkIcon.preserveAspect = true;
                    _pickMarkIcon.enabled = true;
                } else {
                    _pickMarkIcon.enabled = false;
                }
            }
        }
    }

    private static void CloseLevelPickDialog() {
        if (_pickCanvas != null) Object.Destroy(_pickCanvas.gameObject);
        _pickCanvas = null;
        // 关窗前保存当前备注输入（防丢）
        if (_pickNoteInput != null && _pickNoteFile != null) {
            LevelScanner.SetNote(_pickNoteFile, _pickNoteInput.text);
        }
        _pickEntry = null;
        _pickFolder = null;
        _pickFiles.Clear();
        _pickedFile = null;
        _pickItems.Clear();
        _pickNoteInput = null;
        _pickNoteFile = null;
        _pickInfoArea = null;
        _pickWinHeatArea = null;
        _pickWinDataArea = null;
        _pickWinRecordArea = null;
        _recordScroll = null;
        _recordEmpty = null;
        _pickTabInfo = null;
        _pickTabHeat = null;
        _pickTabData = null;
        _pickTabRecord = null;
        _dataConfirmWin = null;
        _heatLines = null;
        _heatDots = null;
        _heatStats = null;
        _heatEmpty = null;
        _heatPeakLabel = null;
        _heatAxisY = null;
        _heatFolder = null;
    }

    // 以下两个方法属于「关卡选择弹窗」，用的是弹窗自己的容器字段
    /// <summary>通关记录区（_pickTab == 1）：每次通关一条、按 xacc 降序、带序号、可滚动</summary>
    private static void MakePickRecordArea() {
        if (_pickWinRecordArea == null || _recordScroll != null) return;
        var rt = MakeText(_pickWinRecordArea.transform, "RT", 26, new Color(1f, 0.85f, 0.5f, 1f), TextAnchor.MiddleLeft);
        rt.text = Lang.Tr("通关记录");
        rt.fontStyle = FontStyle.Bold;
        var rtrt = rt.rectTransform;
        rtrt.anchorMin = new Vector2(0.05f, 0.88f); rtrt.anchorMax = new Vector2(0.95f, 1f);
        rtrt.offsetMin = Vector2.zero; rtrt.offsetMax = Vector2.zero;
        var listView = MakeImage(_pickWinRecordArea.transform, "ListView", new Color(0.06f, 0.08f, 0.13f, 0.92f));
        listView.rectTransform.anchorMin = new Vector2(0.05f, 0.10f);
        listView.rectTransform.anchorMax = new Vector2(0.95f, 0.78f);
        listView.rectTransform.offsetMin = Vector2.zero; listView.rectTransform.offsetMax = Vector2.zero;
        listView.gameObject.AddComponent<RectMask2D>();
        _recordScroll = listView.gameObject.AddComponent<ScrollRect>();
        _recordScroll.horizontal = false;
        _recordScroll.vertical = true;
        _recordScroll.scrollSensitivity = 40f;
        _recordScroll.movementType = ScrollRect.MovementType.Clamped;
        _recordScroll.viewport = listView.GetComponent<RectTransform>();
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(listView.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = new Vector2(0f, 0f);
    // 行位置全手工算（RecRowY / RecListHeight）：布局组每帧会把行搬回它自己算的位置，
    // 流式让位写在行上的位置会被它盖掉。
        _recordScroll.content = crt;
        var head = MakeText(_pickWinRecordArea.transform, "Head", 17, Color.white, TextAnchor.MiddleLeft);
        // 彩色标签留着，六个判定词包进 Lang.Tr（与详情页表头同一口径）
        head.text = "<color=#FF4D4D>" + Lang.Tr("提前") + "</color>  <color=#E68A2E>" + Lang.Tr("太快")
                  + "</color>  <color=#FFE64D>" + Lang.Tr("稍快") + "</color>  <color=#66E666>" + Lang.Tr("完美")
                  + "</color>  <color=#FFE64D>" + Lang.Tr("稍慢") + "</color>  <color=#E68A2E>" + Lang.Tr("太慢") + "</color>"
            + "              X-acc";
        var hrt = head.rectTransform;
        hrt.anchorMin = new Vector2(0.05f, 0.80f); hrt.anchorMax = new Vector2(0.95f, 0.85f);
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
        _recordEmpty = MakeText(_pickWinRecordArea.transform, "Empty", 22, new Color(0.6f, 0.6f, 0.6f, 1f), TextAnchor.MiddleCenter);
        _recordEmpty.text = Lang.Tr("还没有通关记录喵");
        var ert = _recordEmpty.rectTransform;
        ert.anchorMin = new Vector2(0.05f, 0.40f); ert.anchorMax = new Vector2(0.95f, 0.60f);
        ert.offsetMin = Vector2.zero; ert.offsetMax = Vector2.zero;
        _recordEmpty.gameObject.SetActive(false);
        RefreshPickRecords(_pickFolder);
    }

}
