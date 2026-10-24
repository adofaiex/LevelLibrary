// Esc 的分层关闭链：一次按下只关掉最上面那一层，关掉了谁全都写进日志。
// 顺序 = 画布排序值从高到低（报错弹窗 99000 在最上面），面板/开始界面那些"底层"排在最后。
// 两条硬规矩：一、每个分支先确认窗口真的还在屏幕上（IsAliveOnScreen）—— 只判 != null 时，
// 引用还在但窗口已隐藏的分支会吃掉 Esc 却什么都不关，玩家看着就是"按 Esc 没反应"；
// 二、关一个就立刻 return，别让一次按下收掉两层。
using UnityEngine;

namespace LevelLibrary;

public static partial class PortalUI {

    /// <summary>行内"删除通关记录"那排小按钮的确认：展开着的时候 Esc 等于点叉。
    /// 全项目只在这里读 Esc 关窗口（别处那几处判据都并进这条链了）—— 一处读、一处动手，
    /// 才不会出现同一帧里两层一起收掉的情况。</summary>
    private static bool EscCloseRecDelConfirm() {
        bool any = false;
        for (int i = _recDelBtns.Count - 1; i >= 0; i--) {
            var b = _recDelBtns[i];
            if (b != null && b.Open) { RecDelResolve(b, 2); any = true; }
        }
        return any;
    }

    /// <summary>每帧一次（挂在常驻段 TickAlwaysHotkeys 里，跟面板开不开无关）。</summary>
    private static void TickEscCloseChain() {
        if (!UiInput.KeyDown(KeyCode.Escape)) return;
        string who = null;
        try {
            who = EscCloseTopmost();
        } catch (System.Exception e) {
            Lg.Ex("Esc 关闭链出错", e);
        }
        try {
            Lg.Info("Esc：处理=" + (who ?? "无（界面已经关着了）")
                + $"（面板开={_panelOpen} 顶层tab={_topTab} 右键菜单={_ctxOpen}）");
        } catch { }
    }

    /// <summary>从最上面那一层往下找第一个还在屏幕上的窗口，关掉它并报出名字（没有就返回 null）。</summary>
    private static string EscCloseTopmost() {
        // ---- 99000 报错弹窗：优先级最高，别的界面都被它压着 ----
        if (TryCloseErrorByEsc()) return "报错弹窗";

        // ---- 96xxx / 95000：报告类弹窗（自动弹出的那几张）----
        if (MourningUI.IsOpen) { MourningUI.CloseDialog(); return "悼念弹窗"; }
        if (EscCloseCanvas(_weeklyCanvas, ref _weeklyCanvas)) return "周报弹窗";
        if (BirthdayUI.IsOpen) { BirthdayUI.Close(); return "生日庆祝弹窗"; }
        if (EscCloseCanvas(_birthdayCanvas, ref _birthdayCanvas)) return "生日报告弹窗";

        // ---- 91500 批量进度条（只收窗口，后台批量继续跑）----
        if (EscCloseCanvas(_batchProgCanvas, ref _batchProgCanvas)) return "批量进度窗";

        // ---- 90600 两个"危险操作"确认窗（清除记录 / 重置全部）----
        if (IsAliveOnScreen(_recCleanCanvas)) { CloseRecordsCleanConfirm(); return "清除记录确认"; }
        if (IsAliveOnScreen(_resetAllCanvas)) { CloseResetAllConfirm(); return "重置全部确认"; }

        // ---- 90500 导入命名框（Esc = 取消，跟按"取消"一样把待办动作丢掉）----
        if (IsAliveOnScreen(_importNameCanvas)) {
            _pendingImportAction = null;
            EscCloseCanvas(_importNameCanvas, ref _importNameCanvas);
            return "导入命名框";
        }

        // ---- 90000 批量导入确认（Esc = 取消这次批量导入）----
        if (IsAliveOnScreen(_batchConfirmCanvas)) {
            EscCloseCanvas(_batchConfirmCanvas, ref _batchConfirmCanvas);
            _batchWaitingConfirm = false;
            _batchRunning = false;
            CloseBatchProgress();
            ShowToast(Lang.Tr("已取消批量导入"));
            return "批量导入确认";
        }

        // ---- TUF 批量评级窗口（关窗不打断后台批量）----
        if (IsAliveOnScreen(_tbCanvas)) { CloseTufBatchWindow(); return "TUF 批量评级窗"; }

        // ---- 89600 大肥鱼模型下拉 ----
        if (IsAliveOnScreen(_petPickCanvas)) { ClosePetPick(); return "宠物模型下拉"; }

        // ---- 89500 谱面行右键菜单 ----
        if (_ctxOpen) { CloseRowContextMenu(); return "谱面右键菜单"; }

        // ---- 89000 批量导入窗口 ----
        if (EscCloseCanvas(_batchCanvas, ref _batchCanvas)) return "批量导入窗";

        // ---- 87000 成就解锁提示（它只是隐藏，画布留着复用）----
        if (_achHintActive && _achHintCanvas != null) {
            _achHintActive = false;
            _achHintCanvas.gameObject.SetActive(false);
            return "成就解锁提示";
        }

        // ---- 86600 / 86500 谱面校验的两个窗 ----
        if (IsAliveOnScreen(_verifyProgressWin)) { CloseVerifyProgress(); return "校验进度窗"; }
        if (IsAliveOnScreen(_verifyConfirmWin)) { CloseVerifyConfirm(); return "校验确认窗"; }

        // ---- 85000 / 84500 标记弹窗、改名弹窗 ----
        if (IsAliveOnScreen(_markCanvas)) { CloseMarkDialog(); return "标记弹窗"; }
        if (IsAliveOnScreen(_nameCanvas)) {
            EscCloseCanvas(_nameCanvas, ref _nameCanvas);
            return "改名弹窗";
        }

        // ---- 84000 谱面文件缺失提示 ----
        if (EscCloseCanvas(_missingCanvas, ref _missingCanvas)) return "谱面缺失提示";

        // ---- 83700 / 83600 / 83500 视频压缩挡位、数据清空确认、删除文件确认 ----
        // 挡位框排在三者最上面：它是"选完才动手"的那一层，按 Esc 等于取消这次导入
        if (EscCloseCanvas(_videoQualityWin, ref _videoQualityWin)) return "视频压缩挡位";
        if (EscCloseCanvas(_dataConfirmWin, ref _dataConfirmWin)) return "数据清空确认";
        if (IsAliveOnScreen(_pickConfirmCanvas)) { ClosePickDeleteConfirm(); return "删除文件确认"; }

        // ---- 83000 选谱面弹窗 / 删除谱面确认 ----
        if (IsAliveOnScreen(_pickCanvas)) {
            _selectedPath = null;
            CloseLevelPickDialog();
            if (_panelOpen) RefreshList();   // 刷新行高亮
            return "选择谱面弹窗";
        }
        if (IsAliveOnScreen(_deleteCanvas)) { CloseDeleteDialog(); return "删除谱面确认"; }

        // ---- 82100 详细去特效（Esc = 点「关闭」，先落盘再收窗）----
        if (IsAliveOnScreen(_fxScreenCanvas)) {
            FxCommit();
            FxCloseScreen();
            return "详细去特效窗";
        }

        // ---- 82000 重命名弹窗（只改 mod 内显示名）----
        if (IsAliveOnScreen(_renameCanvas)) { CloseRenameDialog(); return "重命名弹窗"; }

        // ---- 行内删除确认（通关记录那排小按钮，不是独立窗口，排在所有弹窗之后）----
        if (EscCloseRecDelConfirm()) return "行内删除记录确认";

        // ---- 面板里的两样浮层 ----
        if (TryCloseProfileByEsc()) return "登录界面：退回开始界面";

        // ---- 开始界面：连同背后的面板一起收掉，别退到面板上 ----
        if (DismissStartScreenByEsc()) {
            if (_panelOpen) RequestClosePanel();
            return "开始界面：收起" + (_panelOpen ? "（面板一起退）" : "");
        }

        // ---- 面板本身：先退回主界面，在主界面再按 Esc 才关掉 mod ----
        if (_panelOpen && _topTab != MainScreenSlot) {
            ShowMainScreen(); return "退回主界面";
        }
        if (_panelOpen) {
            // 走 RequestClosePanel 让界面淡出；直接调 ClosePanel 是瞬间销毁、没有动画。
            RequestClosePanel(); return "退出谱面库（已在主界面，开始淡出）";
        }
        return null;
    }

    /// <summary>"这个窗口还在屏幕上就关掉它"的统一写法：销毁 + 把字段置空，返回有没有关过。</summary>
    private static bool EscCloseCanvas(Canvas c, ref Canvas slot) {
        if (!IsAliveOnScreen(c)) return false;
        try { Object.Destroy(c.gameObject); } catch { }
        slot = null;
        return true;
    }
}
