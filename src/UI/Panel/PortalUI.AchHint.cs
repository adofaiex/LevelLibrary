// [split-of] PortalUI.cs lines 1518-1695
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

/// <summary>PortalUI 的成就解锁提示条与成就音效播放器。</summary>
public static partial class PortalUI {
    private static Canvas _achHintCanvas;
    private static Text _achHintText;
    /// <summary>提示窗那个 Win 面板的 RectTransform，建窗时存一次。
    /// TickAchHint 每帧要写它的 anchoredPosition（20 秒停留期里每帧都写），
    /// 而原来每帧先 transform.Find("Win") 再 GetComponent&lt;RectTransform>() 找两遍。
    /// 窗是 DontDestroyOnLoad 的一次性对象，"Win" 不会再被换掉，所以建时存住就够。
    /// 万一被外部销毁，Unity 的 == null 会让它自然失效并重新取（见 TickAchHint）。</summary>
    private static RectTransform _achHintWinRt;
    private static float _achHintT;
    private static bool _achHintActive;
    private const float AchIn = 0.4f;    // 上滑升入耗时
    private const float AchHold = 20f;   // 停留时长（含自动更新提示）
    private const float AchOut = 0.4f;   // 往下收回耗时
    private static AchSfxPlayer _achSfx;   // 成就音效播放器
    private static float _achDailyCheckinWait = -1f;   // 打卡！计时（>0计时中，触发后-1）

    /// <summary>右下角从屏幕外升上来一个小窗「解锁成就：xxx」，停留 AchHold 后收回，点一下也能立刻收回</summary>
    private static void ShowAchHint(string name) {
        if (_achHintCanvas == null) {
            _achHintCanvas = MakeCanvas("LevelLib_AchHint", 87000);
            Object.DontDestroyOnLoad(_achHintCanvas.gameObject);   // 全局显示（跨场景不销毁）
            var win = MakeImage(_achHintCanvas.transform, "Win", PanelColor);
            var wr = win.rectTransform;
            _achHintWinRt = wr;   // 存住给每帧的 TickAchHint 用（见字段注释）
            wr.anchorMin = wr.anchorMax = new Vector2(1f, 0f);
            wr.pivot = new Vector2(1f, 0f);
            wr.anchoredPosition = new Vector2(-30f, 40f);
            wr.sizeDelta = new Vector2(460f, 90f);
            // 左侧：游戏图标（冰与火之舞 logo）
            var icon = MakeImage(win.transform, "Icon", Color.white);
            var icrt = icon.rectTransform;
            icrt.anchorMin = icrt.anchorMax = new Vector2(0f, 0.5f);
            icrt.pivot = new Vector2(0f, 0.5f);
            icrt.anchoredPosition = new Vector2(16f, 0f);
            icrt.sizeDelta = new Vector2(48f, 48f);
            var iconSpr = LoadAssetSprite(Path.Combine(Main.ModEntry.Path, "assets", "achievement_icon.png"));
            if (iconSpr != null) { icon.sprite = iconSpr; icon.type = Image.Type.Simple; icon.preserveAspect = true; }
            else icon.enabled = false;
            // 右侧：文字
            _achHintText = MakeText(win.transform, "Txt", 22, new Color(1f, 0.8f, 0.3f, 1f), TextAnchor.MiddleLeft);
            var htrt = _achHintText.rectTransform;
            htrt.anchorMin = new Vector2(0f, 0f); htrt.anchorMax = new Vector2(1f, 1f);
            htrt.offsetMin = new Vector2(76f, 8f); htrt.offsetMax = new Vector2(-14f, -8f);
            var hintClick = win.gameObject.AddComponent<Button>();
            hintClick.transition = Selectable.Transition.None;
            hintClick.targetGraphic = win;
            hintClick.onClick.AddListener(() => { _achHintT = AchIn + AchHold - AchOut; });   // 点一下直接进入收回阶段
        }
        if (_achHintText != null) _achHintText.text = Lang.Tr("解锁成就：") + Lang.Tr(name);
        _achHintT = 0f;
        _achHintActive = true;
        if (_achHintCanvas != null) _achHintCanvas.gameObject.SetActive(true);
    }

    /// <summary>临时成就提示：复用成就提示的窗口样式与音效，但不是真成就 ——
    /// 不写 achievements.txt、不进成就面板、不解锁任何东西，纯粹是一条带成就质感的通知。
    /// 用途：自动更新完成后提示玩家"新版本已自动更新，请重启游戏"。</summary>
    public static void ShowTempAchHint(string text) {
        try {
            PlayAchievementSound();
            ShowAchHintText(text);
        } catch (System.Exception e) { Lg.Ex("临时成就提示失败", e); }
    }

    /// <summary>提示窗口的文本入口：ShowAchHint 只接名字、自己拼"解锁成就："，这里允许传完整文案</summary>
    private static void ShowAchHintText(string fullText) {
        if (_achHintCanvas == null) ShowAchHint("");     // 首次调用先建出窗口（文本会被下面覆盖）
        if (_achHintText != null) _achHintText.text = fullText;
        _achHintT = 0f;
        _achHintActive = true;
        if (_achHintCanvas != null) _achHintCanvas.gameObject.SetActive(true);
    }

    /// <summary>每次成就解锁播放一次音效（assets/steam_achieve.wav，懒加载）</summary>
    private static void PlayAchievementSound() {
        try {
            if (_achSfx == null) {
                var host = new GameObject("__AchSfx");
                Object.DontDestroyOnLoad(host);
                _achSfx = host.AddComponent<AchSfxPlayer>();
                _achSfx.Source = host.AddComponent<AudioSource>();
                _achSfx.Source.playOnAwake = false;
                _achSfx.Source.ignoreListenerPause = true;   // 面板打开/监听暂停时也能响
                _achSfx.Source.volume = 1f;   // 与背景音量无关，独立音量
            }
            _achSfx.Play();
        } catch (System.Exception e) { Lg.Ex("成就音效播放失败", e); }
    }

    /// <summary>成就音效播放器：懒加载 clip，加载完自动补播</summary>
    private class AchSfxPlayer : MonoBehaviour {
        public AudioSource Source;
        private AudioClip _clip;
        private UnityEngine.Networking.UnityWebRequest _uwr;   // 成功那次的请求（不 Dispose 的理由见 LoadClip）
        private bool _loading;
        private bool _pendingPlay;
        public void Play() {
            if (_clip != null) { Source.PlayOneShot(_clip); return; }
            if (!_loading) StartCoroutine(LoadClip());
            _pendingPlay = true;
        }
        private System.Collections.IEnumerator LoadClip() {
            _loading = true;
            string path = System.IO.Path.Combine(Main.ModEntry.Path, "assets", "steam_achieve.wav");
            if (!File.Exists(path)) { Lg.Info("成就音效不存在: " + path); _loading = false; yield break; }
            var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.WAV);
            yield return uwr.SendWebRequest();
            bool ok = (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success) && uwr.downloadHandler != null;
            if (ok) {
                _clip = DownloadHandlerAudioClip.GetContent(uwr);
                _uwr = uwr;      // 成功那次故意留着不 Dispose：AudioClip 是同一次请求解出来的，提前放掉有失效风险
                                 //（一次会话只加载一次，请求不会累积）
                if (_pendingPlay && Source != null && _clip != null) Source.PlayOneShot(_clip);
            } else {
                Lg.Info("成就音效加载失败: " + uwr.error);
                // 失败这条没有 clip 依赖它 → 立刻放掉（失败会被反复重试，不放就是每解锁一次泄漏一个）
                try { uwr.Dispose(); } catch { }
            }
            _pendingPlay = false;
            _loading = false;
        }
    }

    /// <summary>成就状态变了 → 通知成就界面刷新。两处调用：解锁回调 OnAchUnlocked，
    /// 以及里程碑重算的撤销（撤销不触发 OnUnlocked，必须自己调这里）。</summary>
    public static void RefreshAchievementsAfterChange() {
        try {
            RefreshAchList(false);                    // 页面没建过时它自己早退
            if (_topTab == 2) RefreshAchLevelBar();   // 正停在成就页（tab 2）→ 顺带刷等级条/汇总
        } catch (System.Exception e) { Lg.Ex("成就界面刷新失败", e); }
    }

    public static void OnAchUnlocked(AchievementDef d) {
        try {
            if (d == null) return;
            // 成就列表只在"切分类 / 搜索 / 建页"时重建（RefreshAchList），解锁这条路上没人通知它，
            // 所以必须在这里补一次刷新
            RefreshAchievementsAfterChange();
            if (_achHintActive) {
                if (_achHintQueue.Count < 20) _achHintQueue.Add(d);   // 正在播 → 排队
                Lg.Info($"[LevelLib] 成就提示排队：{d.Name}（第 {_achHintQueue.Count} 个待播）");
                return;
            }
            PlayAchievementSound();
            ShowAchHint(d.Name);
        } catch (System.Exception e) { Lg.Ex("成就提示失败", e); }
    }

    /// <summary>成就提示队列：同一次通关可能同时解锁好几个（里程碑 + 无瑕 + P/G/U 首通…），
    /// 正在显示就排队，等这一轮播完（升上来 → 停留 → 收回）再弹下一个。
    /// 不排队的话音效会叠着响、文字只剩最后一个，没播完的音效还会拖到之后开面板时才响出来。</summary>
    private static readonly List<AchievementDef> _achHintQueue = new List<AchievementDef>();

    /// <summary>排队驱动：当前没在显示、队列里还有 → 弹下一个。由 TickAchievements 每帧调用。</summary>
    private static void PumpAchHintQueue() {
        if (_achHintActive || _achHintQueue.Count == 0) return;
        var next = _achHintQueue[0];
        _achHintQueue.RemoveAt(0);
        PlayAchievementSound();
        ShowAchHint(next.Name);
        Lg.Info($"[LevelLib] 成就提示出队播放：{next.Name}（还剩 {_achHintQueue.Count} 个）");
    }

    // 成就页 tab 选中背景 = 等级色压暗：一级纯白会和选中态的白字撞色，压暗后白字才读得出来


    // ---------- 主界面筛选：等级双滑块 + 判定图三选一 ----------

}
