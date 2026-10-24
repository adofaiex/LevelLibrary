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

/// <summary>PortalUI partial 模块：ContextMenu（谱面列表行的右键菜单）
/// 在谱面上点右键弹出竖排菜单（形式参考 Windows 桌面）：点菜单以外任意位置关闭，滚轮会先收起菜单再滚列表。</summary>
public static partial class PortalUI {

    private static GameObject _ctxGo;         // 菜单整块（含点空白关闭用的全屏遮挡）
    private static bool _ctxOpen;
    private static bool _ctxPointerDown;      // 本次鼠标按下是否已被菜单消费（避免关菜单顺手选中行）
    private static bool _ctxCloseRequest;     // EventTrigger 回调里请求关闭，攒到下一帧统一处理

    // 铺开 / 收起这一段（与模型下拉同一套编排：面板从零高往下长，选项在面板铺过它下沿的那一刻落进来）
    private sealed class CtxRow {
        public RectTransform Rt;
        public CanvasGroup Group;
        public Button Btn;
        public Vector2 Base;      // 落点：摆位时算好的最终 anchoredPosition，动画一律以它为准
        public float Top;         // 这一行在菜单里的上边距（算露面时刻用）
        public float Start;       // 露面时刻（秒）
        public bool Done;         // 已经落到精确位置
    }
    private static readonly List<CtxRow> _ctxRows = new List<CtxRow>(8);
    private static RectTransform _ctxBox;
    private static Image _ctxPanel;           // 菜单底板那张图（它的 alpha 就是整块的透明度）
    private static Image _ctxBlocker;         // 点空白关闭用的全屏遮挡层
    private static Color _ctxPanelBase;
    private static float _ctxMenuH;           // 最终高度
    private static float _ctxFinalY;          // 最终 anchoredPosition.y：上沿钉在这一点，只往下长
    private static float _ctxT;
    private static float _ctxCloseT;
    private static bool _ctxOpenDone;
    private static bool _ctxClosing;
    private const float CtxOpenSeconds = MoSmall;   // 开口时长（设计尺小过渡那档）
    private const float CtxCloseSeconds = 0.10f;    // 收起比铺开快：关是"让开"，不该让人等
    private const float CtxRowSeconds = 0.14f;      // 单行落位时长
    private const float CtxRowRise = 10f;           // 每行从上方多少像素落下来
    private const float CtxCoverK = 0.9f;           // 露面时刻 = 面板铺过它下沿的那一刻 × 这个系数
    private const float CtxDimFrom = 0.6f;          // 刚铺开时整块只有最终透明度的六成

    // 菜单尺寸
    private const float CtxOptionH = 44f;
    private const float CtxGap = 4f;
    private const float CtxPad = 8f;
    private const float CtxMinW = 170f;       // 内容区最小宽
    private const float CtxMaxW = 420f;       // 内容区最大宽
    private const float CtxOptionFont = 23;
    private const float CtxOptInset = 8f;     // 选项按键相对内容区再往右让出的像素（视觉更居中）
    private const float CtxIconCol = 32f;     // 图标列宽：20 的图标 + 12 的空隙（也正好是 4 的倍数，见 check_grid）
    // 位置微调量（屏幕像素，正数 = 再往右 / 往下挪）：菜单左上角落在鼠标点的哪儿是个观感量，量不出来，
    // 只能手填。CtxCalibY = 基准下移 85 − 1.5 个按键高（48×1.5 = 72），按实际观感调定。
    private const float CtxCalibX = 20f;
    private const float CtxCalibY = 13f;

    /// <summary>在谱面上点右键 → 打开右键菜单（pos = 鼠标屏幕坐标）</summary>
    private static void OpenRowContextMenu(LevelEntry entry, Vector2 pos) {
        if (entry == null) return;
        if (_panelCanvas == null) return;
        CloseRowContextMenu(true);
        try {
            WrapBuild(BuildRowContextMenu, entry, pos);
        } catch (System.Exception ex) {
            CloseRowContextMenu(true);
            ReportError("打开右键菜单失败", ex);
        }
    }

    /// <summary>在个人信息栏上点右键 → 打开右键菜单（关闭显示 / 更换头像 / 更换名字）</summary>
    private static void OpenPlayerBoxContextMenu(Vector2 pos) {
        if (_panelCanvas == null) return;
        CloseRowContextMenu(true);
        try {
            WrapBuild(BuildPlayerBoxContextMenu, null, pos);
        } catch (System.Exception ex) {
            CloseRowContextMenu(true);
            ReportError("打开个人信息栏菜单失败", ex);
        }
    }

    /// <summary>建菜单的统一入口（出错兜底在调用方的 try/catch 里）。</summary>
    private static void WrapBuild(System.Action<LevelEntry, Vector2> build, LevelEntry entry, Vector2 pos) {
        build(entry, pos);
    }

    private static void BuildRowContextMenu(LevelEntry entry, Vector2 pos) {
        var items = new List<MenuAction> {
            new MenuAction("重命名", RowBtnBlue, IconShapes.Kind.Pencil, () => ShowRenameDialog(entry)),
            new MenuAction("标记", RowBtnGreen, IconShapes.Kind.Flag, () => ShowMarkDialog(entry)),
            new MenuAction("打开位置", RowBtnOrange, IconShapes.Kind.Folder, () => OpenInExplorer(entry)),
            new MenuAction("删除", RowBtnRed, IconShapes.Kind.Trash, () => ShowDeleteDialog(entry)),
        };
        bool isBgm = IsBgmSourceOf(entry);
        items.Add(new MenuAction(isBgm ? "取消背景音乐" : "设置背景音乐", RowBtnBlue, () => ToggleLevelBgmFor(entry)));
        items.Add(new MenuAction("导出 zip 至桌面", RowBtnPurple, () => ExportLevelZipToDesktop(entry)));
        if (string.IsNullOrEmpty(MarkStore.Get(entry.FilePath)))
            items.Add(new MenuAction("自动评级", RowBtnGreen, () => AutoRateEntry(entry)));
        BuildContextMenuCore(pos, items.ToArray());
        // 文件缺失也照样给菜单（打开位置/删除仍然有用），但先提醒一句
        if (!File.Exists(entry.FilePath)) ShowToast(Lang.Tr("这个谱面的文件好像不见了…菜单里的操作可能失败"));
    }

    private static void BuildPlayerBoxContextMenu(LevelEntry _, Vector2 pos) {
        BuildContextMenuCore(pos, new MenuAction[] {
            new MenuAction("关闭显示", RowBtnRed, () => {
                // 个人信息栏总开关关掉（面板内 / 界面外都不再显示）
                ShowPlayerBox = false;
                if (_playerBoxRt != null) _playerBoxRt.gameObject.SetActive(false);
                if (_outsideToggle != null) RefreshSettingsToggles();
                TickPlayerBox();
                AutoFeatures.Save();
                ShowToast(Lang.Tr("已关闭个人信息栏显示（可在设置里重新打开）"));
            }),
            new MenuAction("更换头像", RowBtnBlue, () => PickAvatar()),
            new MenuAction("更换名字", RowBtnGreen, () => PickPlayerName()),
        });
    }

    /// <summary>删掉一条通关记录（按时间戳匹配，立即写盘）。
    /// 只动数据与里程碑、不重建列表：行尾那个删除键要先把对勾停留的那一段放完，
    /// 列表重建交给 RefreshRecordsAfterDelete（行被销毁时会补上，见 TickRecordDeleteButtons）。</summary>
    private static void DoDeleteClearRecord(string folder, long time) {
        try {
            LevelScanner.RemoveClearRecord(folder, time);
            ShowToast(Lang.Tr("已删除该条记录喵"));
            // 里程碑成就跟「当前标记 + 该谱面有严判通关记录」强绑定：删掉记录后当场重算，
            // 免得留下一条没有记录支撑的成就（重算只撤销，不弹解锁以外的提示）。
            try { Achievements.RebuildMilestones(false); } catch (System.Exception) { }
        } catch (System.Exception ex) {
            ReportError("删除通关记录失败", ex);
        }
    }

    /// <summary>重建正在显示的通关记录列表：详情区那份（tab0）与关卡选择弹窗那份。
    /// 弹窗那份只在它正显示同一个谱面时才重建，否则会把别的谱面的记录填进它里面。</summary>
    private static void RefreshRecordsAfterDelete(string folder) {
        try {
            var e = LevelScanner.Entries.FirstOrDefault(x => x.FolderPath == folder);
            if (e != null && _pickRecordArea != null && _pickRecordArea.activeSelf) RefreshPickRecordsDetail(e);
            if (_recordScroll != null && _pickFolder == folder) RefreshPickRecords(folder);
        } catch (System.Exception ex) {
            ReportError("删除通关记录后刷新列表失败", ex);
        }
    }

    /// <summary>把整个谱面文件夹打包成 zip 放到桌面。
    /// 重名不覆盖（自动加 (2)(3)…）；打包的是谱面自己的文件夹（ogg / adofai / 贴图要一起走，少一个谱面就打不开）；
    /// 出错必须弹「错误」框 —— 这种「要把文件拿到手」的操作不许静默失败。</summary>
    private static void ExportLevelZipToDesktop(LevelEntry entry) {
        if (entry == null) return;
        try {
            string folder = entry.FolderPath;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) {
                ReportError("导出 zip：这个谱面的文件夹找不到了 " + folder);
                return;
            }
            string desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) { ReportError("导出 zip：找不到桌面目录"); return; }
            string baseName = SanitizeFileName(string.IsNullOrEmpty(entry.Title) ? Path.GetFileName(folder) : entry.Title);
            if (string.IsNullOrEmpty(baseName)) baseName = "level";
            string dest = Path.Combine(desk, baseName + ".zip");
            for (int i = 2; File.Exists(dest) && i < 200; i++) dest = Path.Combine(desk, baseName + " (" + i + ").zip");
            Lg.Info($"[LevelLib] 导出 zip：{folder} → {dest}");
            if (File.Exists(dest)) File.Delete(dest);   // 走到这里说明 (1..199) 都被占了 → 覆盖最后一个（极罕见）
            System.IO.Compression.ZipFile.CreateFromDirectory(folder, dest,
                System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            ShowToast(Lang.Tr("已导出到桌面：") + Path.GetFileName(dest));
        } catch (System.Exception e) {
            Lg.Ex("导出 zip 失败", e);
            ReportError("导出 zip 失败", e);
        }
    }

    /// <summary>文件名里不能有的字符换成下划线（谱面名字里常有 / : ? 之类）</summary>
    private static string SanitizeFileName(string s) {
        if (string.IsNullOrEmpty(s)) return "";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) sb.Append(System.Array.IndexOf(bad, c) >= 0 ? '_' : c);
        return sb.ToString().Trim().TrimEnd('.');      // Windows 上结尾的点会让文件建不出来
    }

    /// <summary>右键菜单里的一项。IconShape = None 的项不带图标，但文字仍然对齐到同一条左边界。</summary>
    private struct MenuAction {
        public string Label;
        public Color Color;
        public System.Action Act;
        public IconShapes.Kind IconShape;
        public MenuAction(string label, Color color, System.Action act) {
            Label = label; Color = color; Act = act; IconShape = IconShapes.Kind.None;
        }
        public MenuAction(string label, Color color, IconShapes.Kind iconShape, System.Action act) {
            Label = label; Color = color; Act = act; IconShape = iconShape;
        }
    }

    /// <summary>右键菜单通用实现：谱面列表、个人信息栏、通关记录共用这一套外观与定位。
    /// 菜单放在一个独立画布上，全屏透明遮挡层自己挡掉下面所有输入（点菜单以外任何地方都落到它身上 → 关闭）。</summary>
    private static void BuildContextMenuCore(Vector2 pos, MenuAction[] items) {
        if (items == null || items.Length == 0) return;
        var overlay = MakeCanvas("LevelLib_CtxMenu", 89500);   // 低于报错弹窗(99000)、高于设置页
        var blocker = MakeImage(overlay.transform, "CtxBlocker", new Color(0f, 0f, 0f, 0f));
        Stretch(blocker.rectTransform, Vector2.zero, Vector2.one);
        blocker.raycastTarget = true;
        // 先挂上引用：万一后面哪一步抛异常，CloseRowContextMenu 才能把整层销毁掉
        _ctxGo = overlay.gameObject;
        var blockerTrigger = blocker.gameObject.AddComponent<EventTrigger>();
        AddTrigger(blockerTrigger, EventTriggerType.PointerDown, _ => RequestCloseRowContextMenu());

        // 名字不能叫 "Box"：那个名字会自动挂上弹窗那支"从缩小弹回正常"的回弹动画，右键菜单不吃那一套，
        // 它走自己的铺开（见 TickRowContextMenu）。边框也去掉了：这块板靠底色和阴影立着就够了。
        var box = MakeImage(blocker.transform, "CtxBox", PanelColor);
        box.raycastTarget = true;
        box.rectTransform.SetAsLastSibling();   // 菜单本体在遮挡层之上
        var boxRt = box.rectTransform;
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);

        _ctxOpen = true;
        _ctxPointerDown = false;
        _ctxBlocker = blocker;
        _ctxBox = boxRt;
        _ctxPanel = box;
        _ctxPanelBase = PanelColor;
        _ctxRows.Clear();
        _ctxT = 0f;
        _ctxCloseT = 0f;
        _ctxOpenDone = false;
        _ctxClosing = false;

        // 菜单里只要有一项带图标，全部文字就左对齐到同一条竖线（让出 CtxIconCol 宽的图标列）：
        // 带图标和不带图标的项排在同一列上，不会变成"一列居中、一列靠左"。
        bool anyIcon = false;
        for (int i = 0; i < items.Length; i++) if (items[i].IconShape != IconShapes.Kind.None) anyIcon = true;
        float iconCol = anyIcon ? CtxIconCol : 0f;

        // 先建按钮，量出最宽的一项再定菜单宽（不用布局组：布局组会重写锚点，这里全程手动定位）
        var texts = new Text[items.Length];
        var btns = new Button[items.Length];
        float optTextW = 0f;
        for (int i = 0; i < items.Length; i++) {
            var b = MakeButton(box.transform, "Ctx_" + items[i].Label, Vector2.zero,
                new Vector2(CtxMinW, CtxOptionH), items[i].Color);
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            if (items[i].IconShape != IconShapes.Kind.None) {
                var ic = IconImage(b.transform, "Icon", items[i].IconShape, IconMd, new Color(1f, 1f, 1f, 0.85f));
                var icrt = ic.rectTransform;
                icrt.anchorMin = icrt.anchorMax = new Vector2(0f, 0.5f);
                icrt.pivot = new Vector2(0f, 0.5f);
                icrt.anchoredPosition = Vector2.zero;
            }
            // 文字铺满按钮 + 居中：宽度交给锚点（不再写死），按钮内缩 CtxOptInset 是左右对称的，
            // 所以字居中 = 在整个菜单里居中，菜单多宽都成立。带图标那排改成左对齐 + 让出图标列。
            var t = MakeText(b.transform, "T", (int)CtxOptionFont, Color.white,
                anyIcon ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, new Vector2(iconCol, 0f), Vector2.zero);
            t.text = items[i].Label;
            texts[i] = t;
            btns[i] = b;
            int idx = i;
            b.onClick.AddListener(() => {
                _ctxPointerDown = true;   // 本次按下已被菜单消费：别让关菜单顺手选中别的行
                CloseRowContextMenu();
                items[idx].Act?.Invoke();
            });
        }

        Canvas.ForceUpdateCanvases();
        for (int i = 0; i < texts.Length; i++) optTextW = Mathf.Max(optTextW, texts[i].preferredWidth);
        float contentW = Mathf.Clamp(Mathf.Max(CtxMinW, optTextW + 44f + iconCol), CtxMinW, CtxMaxW);

        // 选项竖排（手动定位，间距 CtxGap）：菜单内左右各留 CtxPad，按钮左边缘 = CtxPad + CtxOptInset，
        // 于是按钮两边各空 16px、与菜单框同心。只写 CtxOptInset 会让右边多空 2×CtxPad、按钮整体偏左 8px。
        float y = CtxPad;
        for (int i = 0; i < btns.Length; i++) {
            var brt = btns[i].GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(contentW - CtxOptInset * 2f, CtxOptionH);
            brt.anchoredPosition = new Vector2(CtxPad + CtxOptInset, -y);
            // 铺开期间这一行还没露面：整行透明度交给 CanvasGroup（不逐帧改颜色，免得跟行里的图标和文字打架）。
            // interactable 要一起关掉 —— 交互态那层是按矩形找悬停目标的、不看透明度，开着的话没露面的行也会亮。
            var group = brt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            btns[i].interactable = false;
            _ctxRows.Add(new CtxRow {
                Rt = brt,
                Group = group,
                Btn = btns[i],
                Base = brt.anchoredPosition,
                Top = y,
            });
            y += CtxOptionH + CtxGap;
        }

        float menuW = contentW + CtxPad * 2f;
        float menuH = btns.Length * (CtxOptionH + CtxGap) - CtxGap + CtxPad * 2f;
        boxRt.sizeDelta = new Vector2(menuW, menuH);

        // 定位：菜单左上角对准鼠标点，往右下展开；右边 / 下边放不下就翻到鼠标另一侧，再整体夹进可视区
        var parentRt = blocker.rectTransform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, pos, null, out Vector2 lp)) {
            Rect pr = parentRt.rect;   // 菜单父节点的矩形（局部坐标下的真实可视范围）
            float halfW = pr.width * 0.5f, halfH = pr.height * 0.5f;
            float left = lp.x + halfW;                 // = 鼠标 x（距父节点左边的像素）
            float top = halfH - lp.y;                  // = 鼠标距父节点上边的像素
            if (left + menuW > pr.width) left -= menuW;      // 右边放不下 → 往左翻
            if (top + menuH > pr.height) top -= menuH;        // 下边放不下 → 往上翻
            left = Mathf.Clamp(left, 2f, Mathf.Max(2f, pr.width - menuW - 2f));
            top = Mathf.Clamp(top, 2f, Mathf.Max(2f, pr.height - menuH - 2f));
            // 左上角(距父边像素) → 居中轴心的 anchoredPosition，再叠加手工微调量
            boxRt.anchoredPosition = new Vector2(left + menuW * 0.5f - halfW + CtxCalibX / CtxCanvasScale,
                                                 halfH - top - menuH * 0.5f - CtxCalibY / CtxCanvasScale);
        }

        // 落点是按最终尺寸算的（见上），这里把面板收成零高再往下铺：轴心在正中，所以 Tick 里要补半个
        // 高差才能让上沿钉住不动。整块的透明度也从六成起步，铺到位再回到面板原色。
        _ctxMenuH = menuH;
        _ctxFinalY = boxRt.anchoredPosition.y;
        boxRt.sizeDelta = new Vector2(menuW, 0f);
        box.color = new Color(_ctxPanelBase.r, _ctxPanelBase.g, _ctxPanelBase.b, _ctxPanelBase.a * CtxDimFrom);
        // 每一行的露面时刻 = 面板铺过它下沿的那一刻（跟模型下拉同一条口径，行数变了不用改任何数）
        for (int i = 0; i < _ctxRows.Count; i++) {
            var r = _ctxRows[i];
            r.Start = CtxOpenSeconds * ((r.Top + CtxOptionH) / menuH) * CtxCoverK;
        }
    }

    /// <summary>菜单逐帧驱动：铺开的那一小段编排 + 点空白关闭（Esc 由 PortalUI.EscClose.cs 那条链统一收）。
    /// 铺开结束后每帧只剩两次判断就返回，常驻段不背这块开销。</summary>
    private static void TickRowContextMenu() {
        // 请求关闭可能在 EventTrigger 回调里积压（那时不能直接销毁 UI 对象），这里统一处理
        if (_ctxCloseRequest) { _ctxCloseRequest = false; CloseRowContextMenu(); }
        if (_ctxGo == null) { CloseRowContextMenu(); return; }   // 面板被销毁等外部原因
        if (_ctxClosing) { TickCtxFadeOut(Time.unscaledDeltaTime); return; }
        if (!_ctxOpen || _ctxOpenDone) return;

        _ctxT += Time.unscaledDeltaTime;
        float k = _ctxT / CtxOpenSeconds;
        if (k > 1f) k = 1f;
        float e = 1f - (1f - k) * (1f - k) * (1f - k);   // 三次缓出：先快后慢，收尾停得住
        float h = _ctxMenuH * e;
        _ctxPanel.color = new Color(_ctxPanelBase.r, _ctxPanelBase.g, _ctxPanelBase.b,
            _ctxPanelBase.a * Mathf.Lerp(CtxDimFrom, 1f, e));
        _ctxBox.sizeDelta = new Vector2(_ctxBox.sizeDelta.x, h);
        // 轴心在正中：补半个高差才能让上沿钉在原地、只往下长
        _ctxBox.anchoredPosition = new Vector2(_ctxBox.anchoredPosition.x, _ctxFinalY + (_ctxMenuH - h) * 0.5f);

        bool landed = true;
        for (int i = _ctxRows.Count - 1; i >= 0; i--) {
            var r = _ctxRows[i];
            if (r.Rt == null) { _ctxRows.RemoveAt(i); continue; }
            float rk = (_ctxT - r.Start) / CtxRowSeconds;
            if (rk <= 0f) { landed = false; continue; }
            if (rk >= 1f) {
                if (!r.Done) {
                    r.Done = true;
                    r.Rt.anchoredPosition = r.Base;   // 落到精确落点，不留浮点误差
                    r.Group.alpha = 1f;
                    r.Group.blocksRaycasts = true;
                    if (r.Btn != null) r.Btn.interactable = true;
                }
                continue;
            }
            landed = false;
            float re = 1f - (1f - rk) * (1f - rk) * (1f - rk);
            r.Rt.anchoredPosition = new Vector2(r.Base.x, r.Base.y + CtxRowRise * (1f - re));
            r.Group.alpha = re;
        }

        if (k >= 1f && landed) {
            _ctxOpenDone = true;
            _ctxBox.sizeDelta = new Vector2(_ctxBox.sizeDelta.x, _ctxMenuH);
            _ctxBox.anchoredPosition = new Vector2(_ctxBox.anchoredPosition.x, _ctxFinalY);
            _ctxPanel.color = _ctxPanelBase;
        }
    }

    /// <summary>画布缩放系数（1920x1080 下为 1；换算"画布像素 ↔ 屏幕像素"用）</summary>
    private static float CtxCanvasScale {
        get { return (_panelCanvas != null && _panelCanvas.scaleFactor > 0f) ? _panelCanvas.scaleFactor : 1f; }
    }

    /// <summary>请求关闭右键菜单（EventTrigger 回调里不能直接销毁，攒到下一帧 Tick 处理）</summary>
    private static void RequestCloseRowContextMenu() {
        _ctxPointerDown = true;   // 这次按下算被菜单吃掉，别再触发别的选中
        _ctxCloseRequest = true;
    }

    /// <summary>本次鼠标按下是否已被菜单消费（库页其它点击处理用；读取即清除）</summary>
    private static bool ConsumeContextMenuClick() {
        if (!_ctxPointerDown) return false;
        _ctxPointerDown = false;
        return true;
    }

    /// <summary>关闭右键菜单：整层 Canvas 一起销毁。
    /// 这里完全不碰 ScrollRect —— 菜单层的存在本身就是输入遮挡，列表的滚轮和拖拽永远是好的。
    /// 真的收掉之前先原位淡一下：菜单没有裁剪层，一缩几何选项就甩到框外面去。</summary>
    public static void CloseRowContextMenu() {
        CloseRowContextMenu(false);
    }

    /// <summary>instant = 立刻销毁（开新菜单前清场，免得新旧两块叠在一起）</summary>
    private static void CloseRowContextMenu(bool instant) {
        _ctxOpen = false;
        _ctxCloseRequest = false;
        if (_ctxGo == null) { _ctxClosing = false; return; }
        if (instant) { DestroyCtxNow(); return; }
        if (_ctxClosing) return;   // 已经在收了，别把淡出重新计时
        _ctxClosing = true;
        _ctxCloseT = 0f;
        // 淡出这一小段别再吃点击：玩家点了别处，那一下就该作用到别处去
        if (_ctxBlocker != null) _ctxBlocker.raycastTarget = false;
        if (_ctxPanel != null) _ctxPanel.raycastTarget = false;
        for (int i = 0; i < _ctxRows.Count; i++) {
            var r = _ctxRows[i];
            if (r.Group != null) r.Group.blocksRaycasts = false;
            if (r.Btn != null) r.Btn.interactable = false;
        }
    }

    /// <summary>收起这一段：整块原位淡掉（面板与每行一起），淡完销毁整层</summary>
    private static void TickCtxFadeOut(float dt) {
        _ctxCloseT += dt;
        float c = _ctxCloseT / CtxCloseSeconds;
        if (c > 1f) c = 1f;
        if (_ctxPanel != null) {
            _ctxPanel.color = new Color(_ctxPanelBase.r, _ctxPanelBase.g, _ctxPanelBase.b, _ctxPanelBase.a * (1f - c));
        }
        for (int i = 0; i < _ctxRows.Count; i++) {
            var r = _ctxRows[i];
            if (r.Group != null) r.Group.alpha = Mathf.Min(r.Group.alpha, 1f - c);
        }
        if (c >= 1f) DestroyCtxNow();
    }

    /// <summary>唯一销毁处：整层画布一起走（菜单、遮挡层、选项都在上面）</summary>
    private static void DestroyCtxNow() {
        _ctxClosing = false;
        _ctxOpenDone = false;
        _ctxCloseT = 0f;
        _ctxBox = null;
        _ctxPanel = null;
        _ctxBlocker = null;
        _ctxRows.Clear();
        if (_ctxGo != null) {
            var go = _ctxGo;
            _ctxGo = null;
            Object.Destroy(go);
        }
    }

    /// <summary>鼠标是否落在某个 RectTransform 的真实屏幕矩形内（不经过射线检测）。
    /// 用 RectTransformUtility 一次判完：它内部走的就是同一套世界坐标换算，但不分配数组
    /// —— 这个函数在滚轮 / 拖拽 / 成就左栏 / 大厅悬停里都是每帧调的，原先那个 new Vector3[4] 是白扔的垃圾。</summary>
    private static bool MouseInRect(RectTransform rt) {
        if (rt == null) return false;
        try { return RectTransformUtility.RectangleContainsScreenPoint(rt, UiInput.MousePosition, null); }
        catch { return false; }
    }

    /// <summary>这一层现在有没有被上方的全屏层盖住 —— 所有绕开 uGUI 事件系统、自己读鼠标的逻辑
    /// （全局滚轮 / 全局拖拽 / 成就左栏）都得先过这个闸门，否则那一下会落到玩家根本碰不到的层上。
    /// 判据取「层对象还在不在」（销毁时才置 null）：滑出中 / 渐隐中、面板淡出中也算挡着。
    private static bool PanelInputBlocked() {
        if (_ssGo != null) return true;         // 开始界面（含滑出中）
        if (_pfGo != null) return true;         // 个人页（含渐隐中）
        if (_etGo != null) return true;         // 进入过渡层
        if (_splashCanvas != null) return true;  // 启动页 / 屏保
        // 面板正在淡出时也算「挡着」：滚轮 / 拖拽 / 左栏点击不该落到一个正在消失的界面上。
        // TickGlobalDrag 的「松手兜底」排在这个闸门之前，所以门一关不会把甩手惯性一起吃掉。
        if (_closing) return true;
        return false;
    }

    /// <summary>全局滚轮：每帧直接读 UiInput.MouseScrollDelta，只要鼠标在列表可视范围内就滚。
    /// 不走事件系统 —— 谱面那块背景图会吃掉滚轮事件（事件冒泡、射线过滤两条路都没治好），
    /// 表现就是「鼠标压在谱面上滚不动、只有空隙能滚」。</summary>
    private static void TickGlobalWheel() {
        // 上层盖着时滚轮不该落到列表上（谱面列表在封面后面照样收得到滚轮）
        if (PanelInputBlocked()) return;
        if (_libScroll == null) return;
        var sr = _libScroll;
        if (!sr.vertical || sr.content == null) return;
        float d = UiInput.MouseScrollDelta.y;
        if (Mathf.Abs(d) < 0.0001f) return;
        // 菜单开着时不禁滚（那样滚轮会完全没反应），改成跟 Windows 一样：一滚先把右键菜单收掉，再照常滚列表。
        if (_ctxOpen) {
            _ctxPointerDown = true;
            CloseRowContextMenu();
        }
        var vp = sr.viewport;
        if (vp == null) return;
        if (!MouseInRect(vp)) return;
        float vpH = vp.rect.height;
        float contentH = sr.content.rect.height;
        if (vpH <= 0f || contentH <= vpH) return;   // 内容不比视口高，没得滚
        // 一格（|delta|≈1）滑 scrollSensitivity 像素；符号与原生的 ScrollRect 一致：
        // 滚轮往前（delta.y>0）→ 看上面 → content 的 y 减小
        float step = -Mathf.Sign(d) * sr.scrollSensitivity * Mathf.Max(0.25f, Mathf.Abs(d));
        float minY = 0f, maxY = contentH - vpH;   // content 轴心在上 → y ∈ [0, 内容-视口]
        CancelScrollAnimation();   // 用户自己滚了 → 让位给字母定位动画
        float before = sr.content.anchoredPosition.y;
        sr.StopMovement();
        float target = Mathf.Clamp(before + step, minY, maxY);
        sr.content.anchoredPosition = new Vector2(sr.content.anchoredPosition.x, target);
        MarkListSnapInput();   // 真实的用户滚动输入：武装居中吸附（见 LevelList.Snap.cs）
    }

    // ---------- 全局拖拽 ----------
    private static bool _dragActive;
    private static Vector2 _dragLastMouse;
    private static float _dragVel;      // 拖拽时的指针速度（像素/秒），松手时转成惯性
    private static float _inertiaVel;   // 松手后的惯性速度（像素/秒，正=内容继续向下移动）
    private const float DragFriction = 4f;   // 惯性衰减系数（越小滑得越远；4.0 ≈ 松手后再滑一屏多）
    /// <summary>本次按下期间指针是否移动超过阈值。松手时特意不清，留给点击处理判
    /// 「这一下是拖列表还是选谱面」，下一次按下才清零。
    /// 为什么需要它：鼠标拖动列表时，按下的那一行在松开时可能还是指针底下那一行
    /// （行的位置跟着内容连续移动，指针下的物体身份不变），
    /// 于是行按钮照常收到一次点击，玩家只是想把列表往下拉一段，谱面却被选中了。
    /// 判据不能靠「按下和松开是不是同一个物体」—— 那件事在本项目里不稳定，
    /// 列表滚动时指针下的物体有可能换人、也有可能不换，靠它等于靠运气。
    /// 也不能在松手分支里清这个标志：松手分支和点击回调谁先跑取决于本帧
    /// EventSystem 与我们的 Tick 的先后，清了就有一半概率判不出来。</summary>
    private static bool _dragMoved;
    /// <summary>判成"拖动"的累计位移（像素）。小于它的移动仍算点击，手抖几像素不会吃掉选中。</summary>
    private const float DragClickSlop = 6f;
    private static float _dragAccum;    // 本次按下期间累计的位移（按绝对值累加）

    /// <summary>全局拖拽：跟滚轮同一个思路 —— 不再依赖 uGUI 的事件系统（原来「鼠标压在谱面上拖不动、
    /// 只有空隙能拖」，同样是那块背景图接住了按下事件）。左键在列表范围内按下就开始拖，惯性自己按速度指数衰减算。</summary>
    private static void TickGlobalDrag() {
        // 松手那一下没被我们看见（切场景 / 卡帧 / 上层盖住）时 _dragActive 会一直挂着 true，之后每帧都以为还在拖
        // → 偶发「列表自己动」。兜底必须做和松手分支完全相同的事（含把 _dragVel 转成 _inertiaVel）：
        // 只清状态会让下面的 MouseButtonUp 分支再也进不去，拖拽惯性整个消失。
        try {
            if (_dragActive && !UiInput.MouseButton(0)) {
                if (Mathf.Abs(_dragVel) > 20f) _inertiaVel = _dragVel;   // 甩手 → 交给惯性继续滑
                _dragActive = false;
                _dragVel = 0f;
            }
        } catch { }
        // 上层盖着时一律不碰列表（真凶见 PanelInputBlocked）
        if (PanelInputBlocked()) return;
        if (_libScroll == null) return;
        var sr = _libScroll;
        if (!sr.vertical || sr.content == null) return;
        var vp = sr.viewport;
        if (vp == null) return;

        if (UiInput.MouseButtonDown(0)) {
            _inertiaVel = 0f;   // 重新按下 → 停掉上一段惯性
            _dragMoved = false; // 新的一次按下：拖动判定重新起算
            _dragAccum = 0f;
            if (MouseInRect(vp) && !_ctxOpen) {
                CancelScrollAnimation();   // 用户接手拖拽 → 立刻停掉字母定位动画（v4.19.231）
                _dragActive = true;
                _dragLastMouse = UiInput.MousePosition;
                _dragVel = 0f;
                sr.StopMovement();
            }
        }

        if (_dragActive && UiInput.MouseButton(0)) {
            float vpH = vp.rect.height;
            float contentH = sr.content.rect.height;
            if (vpH <= 0f || contentH <= vpH) { _dragActive = false; return; }
            Vector2 now = UiInput.MousePosition;
            float dy = now.y - _dragLastMouse.y;
            _dragLastMouse = now;
            if (Mathf.Abs(dy) > 0.001f) {
                _dragAccum += Mathf.Abs(dy);
                if (_dragAccum > DragClickSlop) _dragMoved = true;   // 超过阈值就算拖动过，待会儿那次点击要忽略
                float before = sr.content.anchoredPosition.y;
                float target = Mathf.Clamp(before + dy, 0f, contentH - vpH);
                sr.content.anchoredPosition = new Vector2(sr.content.anchoredPosition.x, target);
                float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                _dragVel = Mathf.Lerp(_dragVel, dy / dt, 0.35f);   // 记录指针速度（像素/秒）
                MarkListSnapInput();   // 真实的用户滚动输入：武装居中吸附（见 LevelList.Snap.cs）
            }
            return;
        }

        if (_dragActive && UiInput.MouseButtonUp(0)) {
            _dragActive = false;
            if (Mathf.Abs(_dragVel) > 20f) _inertiaVel = _dragVel;   // 甩手 → 交给惯性继续滑
            _dragVel = 0f;
        }

        // 惯性衰减（松手后继续滑一小段，跟原生手感一致）
        if (Mathf.Abs(_inertiaVel) <= 1f) { _inertiaVel = 0f; return; }
        if (_ctxOpen) { _inertiaVel = 0f; return; }
        float vh = vp.rect.height;
        float ch = sr.content.rect.height;
        if (vh <= 0f || ch <= vh) { _inertiaVel = 0f; return; }
        float dtI = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        float move = _inertiaVel * dtI;
        float b2 = sr.content.anchoredPosition.y;
        float t2 = Mathf.Clamp(b2 + move, 0f, ch - vh);
        sr.content.anchoredPosition = new Vector2(sr.content.anchoredPosition.x, t2);
        if (Mathf.Abs(t2 - b2) < 0.01f) { _inertiaVel = 0f; return; }   // 撞到边界就停
        _inertiaVel *= Mathf.Exp(-DragFriction * dtI);                   // 指数衰减
    }

}
