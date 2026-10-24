// [split-of] PortalUI.cs lines 2867-3175
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

/// <summary>PortalUI 的玩家信息框（面板内那份 + 界面外那份，共用同一套布局与位置偏移）</summary>
public static partial class PortalUI {
    private static void TickPlayerBox() {
        try {
            // ShowPlayerBox = 总开关（关了哪都不显示）；ShowPlayerBoxOutside = 面板不在时也显示；
            // HidePlayerBoxInGame 时只销毁画布，位置/头像/名字等设置一律不碰 —— 离开游玩后这里会自己重建。
            if (!ShowPlayerBox || !ShowPlayerBoxOutside
                || (HidePlayerBoxInGame && IsLevelPlaying())) {
                if (_playerBoxCanvas != null) { Object.Destroy(_playerBoxCanvas.gameObject); _playerBoxCanvas = null; _outsideBoxRt = null; _outsideAvatarImg = null; _outsideNameText = null; _outsideStatText = null; _outsideBadgeImg = null; }
                return;
            }
            if (_playerBoxCanvas == null) BuildOutsidePlayerBox();
            // 只在变化时更新（避免每帧触发 Text 网格重建 / RectTransform 脏标记）。
            // 位置要同步两样：锚点（X/Y 滑条）和 anchoredPosition（PlayerBoxOffset 那条贴顶让位的偏移），
            // 只同步锚点的话，Y 被拖过 0.99 之后这一份的偏移还停在建画布那一刻，跟面板内那份对不上。
            if (_outsideBoxRt != null) {
                var wantAnchor = new Vector2(PlayerBoxX, PlayerBoxY);
                if (_outsideBoxRt.anchorMin != wantAnchor || _outsideBoxRt.anchorMax != wantAnchor) {
                    _outsideBoxRt.anchorMin = _outsideBoxRt.anchorMax = wantAnchor;
                }
                var wantPos = PlayerBoxOffset();
                if (_outsideBoxRt.anchoredPosition != wantPos) _outsideBoxRt.anchoredPosition = wantPos;
            }
            // 名字 / 时长 / 游玩次数 / 严判等级图（内部自带"值没变就不写"的判断）。
            // 0.2 秒节流：RefreshPlayerBoxInfo 会跑统计求和 + 读 playlog，时长要实时涨但不能每帧都跑。
            if (Time.unscaledTime >= _outsideRefreshNextAt) {
                _outsideRefreshNextAt = Time.unscaledTime + 0.2f;
                RefreshPlayerBoxInfo(_outsideNameText, _outsideStatText, _outsideBadgeImg);
            }
            if (_outsideAvatarImg != null && _panelAvatarImg != null && _outsideAvatarImg.sprite != _panelAvatarImg.sprite) {
                _outsideAvatarImg.sprite = _panelAvatarImg.sprite;
                _outsideAvatarImg.type = Image.Type.Simple;
                _outsideAvatarImg.preserveAspect = true;
            }
        } catch (System.Exception e) {
            ReportErrorQuiet("界面外玩家信息框更新失败", e);
        }
    }

    /// <summary>严判最高通关等级：直接读"里程碑成就"里最高档的那个（不遍历谱面和记录）—— 里程碑成就在严判通关
    /// 对应等级谱面时自动解锁（见 Achievements.CheckVillageMark），所以最高档里程碑 = 严判最高通关等级。
    /// 口径：U(宇宙级) &gt; G(星系级) &gt; P(行星级)，同级里取序号大的；都没有就返回 null。
    /// 开销：最多 60 次字典查找，无磁盘/无遍历，随便每帧调。</summary>
    private static string HighestStrictClearMark() {
        try {
            foreach (var head in new[] { 'U', 'G', 'P' }) {
                for (int n = 20; n >= 1; n--) {
                    if (Achievements.IsUnlocked("ach_milestone_" + head + n)) return head + n.ToString();
                }
            }
        } catch (System.Exception ex) {
            ReportErrorQuiet("读取严判最高里程碑失败", ex);
        }
        return null;
    }

    /// <summary>每秒刷新一次面板内信息栏（文字/徽章都要读统计数据，不适合每帧调）</summary>
    private static float _playerBoxInfoNextAt;
    /// <summary>界面外信息栏的刷新节流（0.2 秒；时长要实时涨，但不能每帧都跑统计）</summary>
    private static float _outsideRefreshNextAt;

    /// <summary>信息栏的位置偏移（面板内 / 界面外两份共用这一个，别再各写一套）：贴边留白 -20，
    /// PlayerBoxY &gt;= 0.99 贴到画布顶时改成 -150，给顶部的 tab 栏让位。</summary>
    private static Vector2 PlayerBoxOffset() {
        return PlayerBoxOffsetFor(PlayerBoxY);
    }

    /// <summary>同一条贴顶让位的带参版本：拖动时要拿"候选 Y"去算，不能读还没写回去的全局值。
    /// 全项目只有这一处判断 0.99，别在别处再写一遍。</summary>
    private static Vector2 PlayerBoxOffsetFor(float y) {
        return new Vector2(-24f, y >= 0.99f ? -150f : -20f);
    }

    /// <summary>把当前的 X/Y + 那条贴顶让位的偏移一起写到某个信息栏的 RectTransform 上。
    /// X/Y 滑条的两个回调都调它 —— 原来只写锚点，跨过 0.99 时偏移不会跟。</summary>
    private static void ApplyPlayerBoxPosToRt(RectTransform rt) {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(PlayerBoxX, PlayerBoxY);
        rt.anchoredPosition = PlayerBoxOffset();
    }

    /// <summary>面板内信息栏的显隐每帧对齐：总开关关 → 哪都不显示；「界面外显示」开 → 面板内这份藏起来
    /// （交给界面外那一份，不然两块叠在一起）；关 → 面板内这份显示出来。
    /// 必须每帧对齐：开关随时能点，而面板不是每次点开关都重建，只在建面板那一刻看一次就会永远错下去。
    /// 只改 activeSelf，不碰位置/头像/名字/设置。</summary>
    private static void SyncPanelPlayerBox() {
        if (_playerBoxRt == null) return;
        bool want = ShowPlayerBox && !ShowPlayerBoxOutside;
        if (_playerBoxRt.gameObject.activeSelf != want) {
            _playerBoxRt.gameObject.SetActive(want);
            Lg.Info($"[LevelLib] 面板内信息栏 {(want ? "显示" : "隐藏")}（总开关={ShowPlayerBox} 界面外显示={ShowPlayerBoxOutside}）");
        }
    }

    private static void TickPanelPlayerBox() {
        if (_playerBoxRt == null) return;
        SyncPanelPlayerBox();   // 每帧对齐显隐，不受下面那个 1 秒节流影响（开关一点就跟着变）
        if (Time.unscaledTime < _playerBoxInfoNextAt) return;
        _playerBoxInfoNextAt = Time.unscaledTime + 1f;
        RefreshPlayerBoxInfo(_panelNameText, _panelStatText, _panelBadgeImg);
    }

    /// <summary>玩家信息栏里两行数据的文本：游玩时长（小时:分:秒）+ 累计游玩次数。
    /// 时长口径 = `PlayTime.TotalSeconds()`：启动时把 Steam 那个数读一次当基准，之后由我们自己的实时统计往上加
    /// （Steam 只在退出游戏时才写盘，反复读它数字永远不动）。读不到 Steam 就整个用我们自己的，不显示 0。
    /// 只有这一行变了 —— 100 小时成就照旧按纯我们自己的数据判（拿 Steam 那份去喂 = 玩家一装就白送一堆成就）。</summary>
    private static void StatTexts(out string hoursText, out string playsText) {
        hoursText = Lang.TrFmt("游玩时长：{0}", PlayTime.TotalText());
        // 次数必须走 LibraryTotalPlays（只累加库里现存谱面），跟库页顶部那行同一套算法；
        // plays.txt 里留着已从库里删掉/移走的旧目录，两边会差出一截。
        playsText = Lang.TrFmt("累计游玩次数：{0}", LibraryTotalPlays().ToString("N0"));
    }

    /// <summary>当前谱面库的总游玩次数：只累加库里现存的谱面（与库页顶部统计行同一套算法）</summary>
    private static long LibraryTotalPlays() {
        long sum = 0;
        try {
            foreach (var e in LevelScanner.Entries) sum += LevelScanner.GetPlays(e.FolderPath);
        } catch { }
        return sum;
    }

    /// <summary>把玩家信息栏的文字和等级图刷新到最新（名字 / 时长 / 游玩次数 / 严判最高等级图）</summary>
    private static void RefreshPlayerBoxInfo(Text nameT, Text statT, Image badgeImg) {
        try {
            string wantName = PlayerName ?? "";
            if (nameT != null && nameT.text != wantName) nameT.text = wantName;
            StatTexts(out string h, out string p);
            if (statT != null) {
                string want = h + "\n" + p;
                if (statT.text != want) statT.text = want;
            }
            if (badgeImg != null) {
                string mark = HighestStrictClearMark();
                if (string.IsNullOrEmpty(mark)) {
                    if (badgeImg.enabled) badgeImg.enabled = false;
                } else {
                    // 每次重新取 sprite（换标记后能立刻反映），MarkStore 内部有图标缓存，开销很小
                    var spr = MarkStore.LoadIcon(mark);
                    if (spr != null) {
                        if (badgeImg.sprite != spr) { badgeImg.sprite = spr; badgeImg.type = Image.Type.Simple; badgeImg.preserveAspect = true; }
                        if (!badgeImg.enabled) badgeImg.enabled = true;
                    } else if (badgeImg.enabled) badgeImg.enabled = false;
                }
            }
            // 成就等级奖杯（右侧竖着居中的那枚）：两份用同一套写法刷新，谁搭出来了就刷谁
            ApplyPlayerBoxAchBadge(_panelAchBadgeImg);
            ApplyPlayerBoxAchBadge(_outsideAchBadgeImg);
        } catch (System.Exception e) {
            ReportErrorQuiet("刷新玩家信息栏失败", e);
        }
    }

    /// <summary>在信息栏里搭建"头像 + 名字 + 两行数据 + 严判等级图"这套内容（面板内 / 界面外共用同一套布局）。
    /// 布局：头像 72x72 在左；名字在右上方（字号 22）；下面两行数据；头像右下角盖一个等级图。</summary>
    private static void BuildPlayerBoxContent(Transform box, out Image avatarImg, out Text nameT, out Text statT, out Image badgeImg) {
        // 头像框（左，72x72）：只当"定位容器 + 尺寸基准"，底色透明 —— 玩家头像本身是圆形透明 PNG，
        // 方框的四个角会从圆外面露出来（badge 的位置仍以它为基准）。
        var avatarMask = MakeImage(box, "AvatarMask", new Color(0.22f, 0.24f, 0.32f, 0f));
        avatarMask.sprite = RoundedSprite;
        avatarMask.type = Image.Type.Sliced;
        avatarMask.raycastTarget = false;
        var amrt = avatarMask.rectTransform;
        amrt.anchorMin = amrt.anchorMax = new Vector2(0f, 0.5f);
        amrt.pivot = new Vector2(0f, 0.5f);
        amrt.sizeDelta = new Vector2(72f, 72f);
        amrt.anchoredPosition = new Vector2(16f, 0f);
        avatarImg = MakeImage(avatarMask.transform, "Avatar", Color.white);
        avatarImg.raycastTarget = false;
        Stretch(avatarImg.rectTransform, Vector2.zero, Vector2.one);

        // 严判最高通关等级图：盖在头像右下角（超出头像边界一点，像徽章）
        badgeImg = MakeImage(avatarMask.transform, "JudgeBadge", Color.white);
        badgeImg.raycastTarget = false;
        var brt = badgeImg.rectTransform;
        brt.anchorMin = new Vector2(1f, 0f);
        brt.anchorMax = new Vector2(1f, 0f);
        brt.pivot = new Vector2(1f, 0f);
        brt.sizeDelta = new Vector2(34f, 34f);
        brt.anchoredPosition = new Vector2(7f, -7f);
        badgeImg.enabled = false;   // 没有严判通关记录就不显示

        // 名字（右上，字号 22）
        nameT = MakeText(box, "Name", 22, Color.white, TextAnchor.MiddleLeft);
        nameT.fontStyle = FontStyle.Bold;
        var nrt = nameT.rectTransform;
        nrt.anchorMin = new Vector2(0f, 0.62f);
        nrt.anchorMax = new Vector2(1f, 0.96f);
        nrt.offsetMin = new Vector2(100f, 0f);
        // 右边让开 52px 给右上角那枚成就等级奖杯（奖杯 34 + 6 间距）：名字再长也不会钻到奖杯底下
        nrt.offsetMax = new Vector2(-52f, 0f);

        // 两行数据（名字下方）：字号 22，框高 110，所以文字区 0.02~0.64（约 68px）才放得下两行，
        // 再往上就顶到名字。
        statT = MakeText(box, "Stats", 22, new Color(0.78f, 0.82f, 0.9f, 0.95f), TextAnchor.UpperLeft);
        statT.horizontalOverflow = HorizontalWrapMode.Overflow;
        statT.verticalOverflow = VerticalWrapMode.Overflow;
        var srt = statT.rectTransform;
        srt.anchorMin = new Vector2(0f, 0.02f);
        srt.anchorMax = new Vector2(1f, 0.64f);
        srt.offsetMin = new Vector2(100f, 0f);
        srt.offsetMax = new Vector2(-10f, 0f);
    }

    private static void BuildOutsidePlayerBox() {
        _playerBoxCanvas = MakeCanvas("LevelLib_PlayerBox", 90000);
        // 框跟面板内那份共用同一个颜色常量 PlayerBoxColor。
        // 这一份不登记磨砂：它后面是游戏自己的画面，拿不到那一帧。
        var infoBox = MakeImage(_playerBoxCanvas.transform, "PlayerBox", PlayerBoxColor);
        infoBox.sprite = RoundedSprite;
        infoBox.type = Image.Type.Sliced;
        var infoOutline = infoBox.gameObject.AddComponent<Outline>();
        infoOutline.effectColor = new Color(1f, 1f, 1f, 0.45f);
        infoOutline.effectDistance = new Vector2(2f, -2f);
        var ibrt = infoBox.rectTransform;
        ibrt.anchorMin = ibrt.anchorMax = new Vector2(PlayerBoxX, PlayerBoxY);
        ibrt.pivot = new Vector2(1f, 1f);
        ibrt.sizeDelta = PlayerBoxSize;
        // 贴顶时下移，给顶部的「库/设置/开发者」tab 栏让位 —— 走共用的 PlayerBoxOffset()，别在这里内联同一条偏移。
        ibrt.anchoredPosition = PlayerBoxOffset();
        _outsideBoxRt = ibrt;

        // 内容（与面板内共用同一套布局）
        BuildPlayerBoxContent(infoBox.transform, out _outsideAvatarImg, out _outsideNameText, out _outsideStatText, out _outsideBadgeImg);
        // 成就等级奖杯（右上角那枚，与面板内那份共用同一套写法）
        _outsideAchBadgeImg = MakePlayerBoxAchBadge(infoBox.transform);
        // 头像走共用写法 ApplyAvatarTo：各写一套的话两条路都可能拿到 null，留下一块白的。
        ApplyAvatarTo(_outsideAvatarImg);
        RefreshPlayerBoxInfo(_outsideNameText, _outsideStatText, _outsideBadgeImg);
        // 全部不挡点击
        foreach (var img in infoBox.GetComponentsInChildren<Image>(true)) img.raycastTarget = false;
    }

    private static void TickPickNote() {
        if (!_pickNoteDirty) return;
        _pickNoteSaveTimer -= Time.unscaledDeltaTime;
        if (_pickNoteSaveTimer <= 0f) {
            _pickNoteDirty = false;
            if (_pickNoteFile != null && _pickNoteInput != null) {
                LevelScanner.SetNote(_pickNoteFile, _pickNoteInput.text);
            }
        }
    }

    // ---------- 删除文件确认（选择关卡文件弹窗里的删除按钮） ----------

    private static void ShowPickDeleteConfirm(string file) {
        if (_pickConfirmCanvas != null) return;
        _pickDeleteFile = file;
        _pickConfirmCanvas = MakeCanvas("LevelLib_PickDel", 83500);
        var bg = MakeDialogVeil(_pickConfirmCanvas.transform);

        var box = MakeDialogBox(_pickConfirmCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(560f, 240f);

        var title = MakeTitle(box.transform, "删除文件", 30, new Color(1f, 0.4f, 0.4f, 1f), TextAnchor.MiddleCenter);
        title.rectTransform.anchorMin = new Vector2(0f, 0.72f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = Vector2.zero;
        title.rectTransform.offsetMax = Vector2.zero;

        var msg = MakeText(box.transform, "Msg", 22, Color.white, TextAnchor.MiddleCenter);
        msg.text = Lang.TrFmt("确定删除 {0} 吗？", Path.GetFileName(file));
        msg.rectTransform.anchorMin = new Vector2(0.05f, 0.35f);
        msg.rectTransform.anchorMax = new Vector2(0.95f, 0.68f);
        msg.rectTransform.offsetMin = Vector2.zero;
        msg.rectTransform.offsetMax = Vector2.zero;

        var ok = MakeButton(box.transform, "Ok", new Vector2(100f, -82f), new Vector2(180f, 44f), new Color(0.6f, 0.22f, 0.22f, 1f));
        var okText = MakeText(ok.transform, "确定删除", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => ConfirmPickDelete());

        var cancel = MakeButton(box.transform, "Cancel", new Vector2(-100f, -82f), new Vector2(140f, 44f), ItemColor);
        var cancelText = MakeText(cancel.transform, "取消", 24, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => ClosePickDeleteConfirm());
    }

    private static void ConfirmPickDelete() {
        try {
            if (!string.IsNullOrEmpty(_pickDeleteFile) && File.Exists(_pickDeleteFile)) {
                // 只删我们自己的副本里的文件：没开导入备份时这里就是玩家的原文件夹，
                // 没有备份就不动它（和删除谱面、重复检验同一条规则）。
                if (!LevelScanner.IsOwnCopy(Path.GetDirectoryName(_pickDeleteFile))) {
                    Lg.Info($"[LevelLib] 原位置的文件没有删除: {_pickDeleteFile}");
                    ShowToast(Lang.Tr("这个谱面在原来的位置，文件没有删"));
                } else {
                    File.Delete(_pickDeleteFile);
                    Lg.Info($"[LevelLib] 已删除文件: {_pickDeleteFile}");
                }
            }
        } catch (System.Exception e) {
            Lg.Ex("删除文件失败", e);
        }
        ClosePickDeleteConfirm();
        // 刷新选择弹窗列表（该文件移除）
        if (_pickCanvas != null) {
            var entry = _pickEntry;
            CloseLevelPickDialog();
            if (entry != null) ShowLevelPickDialog(entry);
        }
    }

}
