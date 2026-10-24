// [split-of] PortalUI.cs lines 3176-3393
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

/// <summary>PortalUI partial 模块：难度标记网格、双击应用标记、自定义背景选择。</summary>
public static partial class PortalUI {
    private static void ClosePickDeleteConfirm() {
        if (_pickConfirmCanvas != null) Object.Destroy(_pickConfirmCanvas.gameObject);
        _pickConfirmCanvas = null;
        _pickDeleteFile = null;
    }

    // ---------- 标记选择 ----------

    private static readonly string[] MarkTabNames = { "Planetary", "Galactic", "Universal", "UJ＆Q", "特殊" };

    /// <summary>标记格子的「双击快速标记」识别状态：Button 只有单击事件，这里自己按「同一格 + 两次点击间隔够短」判。
    /// 身份用标记名而不是引擎对象 ID —— 每次点击都会 RefreshMarkGrid 重建格子，对象 ID 每次都变，靠它判不出「同一格」。</summary>
    private static string _markDblLast = null;
    private static float _markDblLastTime = -1f;
    private const float MarkDblClickSec = 0.35f;

    /// <summary>这一下是不是同一格的第二次点击；命中即消耗状态（连点三下只算一次双击）。</summary>
    private static bool MarkDblClickHit(string key) {
        float now = Time.unscaledTime;
        bool hit = _markDblLast == key && _markDblLastTime > 0f && (now - _markDblLastTime) <= MarkDblClickSec;
        if (hit) {
            _markDblLast = null;          // 消耗掉：连点三下只算一次双击
            _markDblLastTime = -1f;
        } else {
            _markDblLast = key;
            _markDblLastTime = now;
        }
        return hit;
    }

    /// <summary>双击快速标记：直接定选这一格（不做「再点取消」的切换）+ 立刻应用 + 关闭界面。</summary>
    private static void ApplyMarkByDoubleClick(bool isSpecialTab, bool isJudge, string mark) {
        if (isJudge) _pendingJudge = mark;
        else if (isSpecialTab) _pendingSpecials = mark;
        else _pendingMark = mark;
        Lg.Info($"[LevelLib] 标记界面：双击快速标记 {mark}"
            + (isJudge ? "（判定标）" : isSpecialTab ? "（特殊标）" : "（等级标）"));
        ApplyPendingMarkAndClose();
    }

    private static void RefreshMarkGrid(Image box) {
        // tab 高亮
        for (int i = 0; i < _markTabButtons.Count; i++) {
            // 走 SetUiSelectFill：交互态层每帧从它自己记的常态色重算这张图，
            // 直接写 Image.color 会被顶掉（鼠标一动，选中高亮就没了）
            SetUiSelectFill(_markTabButtons[i], i == _markTab ? Accent : ItemColor);
        }
        foreach (var go in _markItems) Object.DestroyImmediate(go);   // 立即销毁，防同帧重建旧格子残留（图标出现两遍）
        _markItems.Clear();
        var scroll = box.GetComponentInChildren<ScrollRect>();
        if (scroll == null) {
            Lg.Info("标记网格：找不到 ScrollRect");
            return;
        }
        var content = scroll.content;
        // ListTab 认的是分组键（P/G/U/特殊），不是显示名（P 级/G 级/U 级/特殊）
        string tab = _markTab == 0 ? "P" : _markTab == 1 ? "G" : _markTab == 2 ? "U" : _markTab == 3 ? "UJQ" : "特殊";
        var marks = MarkStore.ListTab(tab);
        string current = _pendingMark;            // 高亮用暂存值
        string currentJudge = _pendingJudge;
        int okIcon = 0;
        Lg.Info($"[LevelLib] 标记网格: tab={tab} 数量={marks.Count}");
        foreach (var mark in marks) {
            // 判定标（宽判/标判/严判）不给手动选：只由通关自动标记，网格里跳过不显示
            if (MarkStore.JudgeShort(mark) != null) continue;
            var cell = MakeButton(content, "Cell_" + mark, Vector2.zero, new Vector2(100f, 100f), ItemColor);
            cell.transform.SetParent(content, false);
            var crt = cell.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(100f, 100f);
            var icon = MakeImage(cell.transform, "Icon", Color.white);
            var irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0.05f, 0.05f);
            irt.anchorMax = new Vector2(0.95f, 0.95f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var spr = MarkStore.LoadIcon(mark);
            if (spr != null) {
                icon.sprite = spr;
                icon.type = Image.Type.Simple;
                icon.preserveAspect = true;   // 保持宽高比，不超出框
                okIcon++;
            } else {
                Lg.Info($"[LevelLib] 标记图标加载失败: {mark}");
            }
            string judgeShort = MarkStore.JudgeShort(mark);
            // 特殊 tab 多选（最多 2 个，存 _pendingSpecials）；等级 tab 单选（存 _pendingMark）；判定三选一单独处理
            bool isSpecialTab = tab == "特殊";
            bool isSelected;
            string m = mark;
            if (judgeShort != null) {
                isSelected = currentJudge == judgeShort;
            } else if (isSpecialTab) {
                isSelected = _pendingSpecials != null && _pendingSpecials.Split('|').Contains(m);
            } else {
                isSelected = current == m;
            }
            if (isSelected) SetUiSelectFill(cell, Accent);   // 当前标记高亮（走登记入口，否则悬停一进来高亮就被顶掉）
            cell.onClick.AddListener(() => {
                if (MarkDblClickHit(m)) {
                    ApplyMarkByDoubleClick(isSpecialTab, judgeShort != null, m);
                    return;
                }
                // 只改暂存，不写文件不刷列表（点确定才应用，避免卡顿）
                if (judgeShort != null) {
                    _pendingJudge = (_pendingJudge == judgeShort) ? null : judgeShort;
                } else if (isSpecialTab) {
                    // 特殊标多选，最多 2 个；再点已选的取消
                    var list = new List<string>();
                    if (!string.IsNullOrEmpty(_pendingSpecials) && _pendingSpecials.Length > 0) {
                        foreach (var s in _pendingSpecials.Split('|')) if (s.Length > 0) list.Add(s);
                    }
                    if (list.Contains(m)) list.Remove(m);
                    else if (list.Count < 2) list.Add(m);
                    _pendingSpecials = list.Count > 0 ? string.Join("|", list) : null;
                } else {
                    _pendingMark = (_pendingMark == m) ? null : m;
                }
                RefreshMarkGrid(box);
            });
            _markItems.Add(cell.gameObject);
        }
        Lg.Info($"[LevelLib] 标记网格完成: 成功图标 {okIcon}/{marks.Count}");
    }

    /// <summary>成就：个性（头像+名字都设置完毕才解锁）</summary>
    private static void CheckPersonaAch() {
        if (!string.IsNullOrEmpty(AvatarPath) && !string.IsNullOrEmpty(PlayerName))
            Achievements.TryUnlock("ach_persona");
    }

}
