// [split-of] PortalUI.cs lines 4265-4690
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

/// <summary>PortalUI 的背景音乐与健康快照；FloatAnimator / MusicPlayer 两个组件（拆分自 PortalUI.cs，同一个 partial 类）</summary>
public static partial class PortalUI {
    private static void PauseGameMusic() {
        _pausedSources.Clear();
        foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) {
            if (s == null) continue;
            if (_musicPlayer != null && s == _musicPlayer.Source) continue;   // 排除咱自己的
            if (s.isPlaying) {
                s.Pause();
                _pausedSources.Add(s);
            }
        }
    }

    /// <summary>恢复之前暂停的游戏音源（从暂停处继续，不重头播）</summary>
    private static void ResumeGameMusic() {
        foreach (var s in _pausedSources) {
            if (s != null) s.UnPause();
        }
        _pausedSources.Clear();
        AudioListener.pause = false;   // 面板期间被游戏暂停过的话，这里一并恢复（否则关面板后整个游戏都没声）
    }

    /// <summary>面板打开期间保证 Unity 的音频监听器不被"暂停"。
    /// 游戏自己的暂停会把 AudioListener.pause 置真，那会让所有音源静音（包括咱的背景音乐），
    /// 界面上看不出来、只觉得"音乐没了"。面板本来就冻结游戏，所以这里直接把它掰回来。</summary>
    private static void KeepAudioListenerUnpaused() {
        if (AudioListener.pause) AudioListener.pause = false;
    }

    /// <summary>关面板的 BGM 淡出（每帧调，必须挂在 Main.OnUpdate 上 ——
    /// TickBgmWatchdog 只在面板开着时被调，面板一关它就停了，淡出永远走不完，
    /// 音乐会一直响、进谱面还跟谱面音乐叠 —— 这正是上一版踩的坑）。
    /// 线性把音量压到 0，到 0 才真正 Stop（并把音量还原，下次播不受影响）。</summary>
    public static void TickBgmFade() {
        if (_bgmFadeT < 0f) return;
        if (_musicPlayer != null && _musicPlayer.Source != null) {
            var fs = _musicPlayer.Source;
            _bgmFadeT += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_bgmFadeT / BgmFadeSeconds);
            fs.volume = _bgmFadeFrom * (1f - k);
            if (k >= 1f) { fs.Stop(); fs.volume = _bgmFadeFrom; _bgmFadeT = -1f; }
        } else { _bgmFadeT = -1f; }
    }

    /// <summary>音乐健康检查：面板开着、曲子也加载好了，却没在响 → 补播。
    /// 每 2 秒才查一次。防的是"音乐被外力掐掉后永远不响"这种情况。</summary>
    private static void TickBgmWatchdog() {
        KeepAudioListenerUnpaused();
        TickBgmFade();
        if (!_bgmWanted) return;
        if (Time.unscaledTime < _bgmWatchdogAt) return;
        _bgmWatchdogAt = Time.unscaledTime + 2f;
        if (_musicPlayer == null || _musicPlayer.Source == null) return;
        var src = _musicPlayer.Source;
        if (src.isPlaying) return;
        if (src.clip == null) return;             // 还在加载，等加载完自己会补播
        src.Play();
    }

    /// <summary>每帧体征采样：每秒一行，只在"面板关着"时写（没放 diag.txt 就不写，发布版安静）。
    /// 目的：卡球发生时日志能直接看出到底是 conductor.bpm 变 0、还是 timeScale 变 0、
    /// 还是游戏自己的暂停被打开了 —— 三者修法完全不同。开销极小：每 60 帧读几个字段写一行。</summary>
    private static void TickVitals1s() {
        if (!DiagOn) return;                        // 没放 diag.txt 就不写（发布版安静）
        try {
            _vitalsFrames++;
            if (_vitalsFrames < 60) return;
            _vitalsFrames = 0;
            if (_panelOpen) return;                 // 面板开着时状态由面板控制，不掺进来
            var c = scrConductor.instance;
            float bpm = c != null ? c.bpm : -1f;
            bool ctrlPaused = false;
            try { var ctrl = AutoFeatures.GetCachedController(); if (ctrl != null) ctrlPaused = ctrl.paused; } catch { }
            string scn = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            // 音乐时钟：球的角度 = ( songposition_minusi − lastHit ) / crotchetAtStart × π × speed，
            // 而 songposition_minusi 由 dspTime 算出来。所以"球不转"最可能是这里停住了（音频时钟不前进），
            // 而不是 bpm / timeScale —— 那两个值正常时球照样可以不动。
            string music = "";
            try {
                if (c != null && c.song != null) {
                    music = $" song.isPlaying={c.song.isPlaying} song.time={c.song.time:F3}"
                          + $" songPos={c.songposition_minusi:F3} crotchet={c.crotchetAtStart:F4}";
                } else music = " song=(空)";
            } catch (System.Exception me) { music = " song读取失败:" + me.GetType().Name; }
            Lg.Info($"[LevelLib] 体征1s: bpm={bpm:F2} timeScale={Time.timeScale:F2}"
                + $" paused={ctrlPaused} 音频暂停={AudioListener.pause} 游戏输入={_gameInputOn}"
                + $" 场景={scn}{music}");
            Lg.Info($"[LevelLib] 体征1s·输入: {InputProbe()}");
        } catch { }
    }
    private static int _vitalsFrames;

    /// <summary>输入 map 自检（只在 DiagOn 且面板开着时跑，每 10 帧一次）：UI map 读到的键，
    /// 用"游戏读法"再读一遍，两者应当永远相反 —— 游戏那边也读到 true 就说明闸门在游戏侧没生效
    /// （补丁没挂上 / 被内联绕过），日志直接标【漏了！】。</summary>
    private static readonly KeyCode[] _probeKeys = {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F,
        KeyCode.Space, KeyCode.Return, KeyCode.Escape, KeyCode.Tab, KeyCode.Delete,
        KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.LeftShift, KeyCode.I, KeyCode.O, KeyCode.N, KeyCode.F9,
    };
    private static int _probeTick;
    private static void TickInputMapProbe() {
        if (!DiagOn || !_panelOpen) return;
        _probeTick++;
        if (_probeTick % 10 != 0) return;        // 每 10 帧查一次：够灵敏，又不刷日志
        var mine = new System.Collections.Generic.List<string>();
        var game = new System.Collections.Generic.List<string>();
        foreach (var k in _probeKeys) {
            if (UiInput.Key(k)) mine.Add(k.ToString());
            if (UnityEngine.Input.GetKey(k)) game.Add(k.ToString());   // 故意不走 UiInput —— 这就是"游戏读法"
        }
        if (mine.Count == 0 && game.Count == 0) return;
        Lg.Info("输入map自检: UImap读到=[" + string.Join(",", mine.ToArray()) + "]"
            + " 游戏读到=[" + string.Join(",", game.ToArray()) + "]"
            + (game.Count > 0 ? "  ← 【漏了！闸门没挡住】" : "  ← 正常"));
    }

    /// <summary>状态变化探针：面板关着时，paused / timeScale / AudioListener.pause 一变就逐帧记一行
    /// （每秒采样会漏掉那一瞬间，抓不到"是谁按了暂停"）。面板开着时状态由面板控制，不记。</summary>
    private static bool _probePaused, _probeAudioPaused;
    private static float _probeTimeScale = -1f;
    private static void TickStateChangeProbe() {
        if (!DiagOn) return;                        // 没放 diag.txt 就不写（发布版安静）
        try {
            if (_panelOpen) return;                     // 面板开着时状态由面板控制
            bool ctrlPaused = false;
            try { var ctrl = AutoFeatures.GetCachedController(); if (ctrl != null) ctrlPaused = ctrl.paused; } catch { }
            bool audioPaused = AudioListener.pause;
            float ts = Time.timeScale;
            bool changed = ctrlPaused != _probePaused || audioPaused != _probeAudioPaused
                        || (ts <= 0.0001f) != (_probeTimeScale <= 0.0001f);
            if (!changed) { _probeTimeScale = ts; return; }
            _probePaused = ctrlPaused; _probeAudioPaused = audioPaused; _probeTimeScale = ts;
            var c = scrConductor.instance;
            bool songPlaying = false;
            try { if (c != null && c.song != null) songPlaying = c.song.isPlaying; } catch { }
            Lg.Info($"[LevelLib] 【状态变化】paused={ctrlPaused} 音频暂停={audioPaused}"
                + $" timeScale={ts:F2} song.isPlaying={songPlaying} 游戏输入={_gameInputOn}"
                + $" 场景={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} 帧={Time.frameCount}");
        } catch { }
    }

    /// <summary>面板关闭状态下的"体征快照"：只在这些值不对劲或刚变化时写一行，
    /// 让"球卡住"这类问题在朋友的日志里能一眼看出是 bpm 还是 timeScale 的锅。</summary>
    private static void TickHealthSnapshot() {
        if (_panelOpen) return;
        if (Time.unscaledTime < _healthLogAt) return;
        string state;
        try {
            var c = scrConductor.instance;
            float bpm = c != null ? c.bpm : -1f;
            // 这里只观察、不改动：曾经会"自愈"式地把 timeScale 恢复成 1，实测会和游戏自己的暂停状态拉锯
            // （我们恢复、游戏又按回 0），反而更容易看到卡顿，已撤掉。
            state = $"bpm={bpm:F2} timeScale={Time.timeScale:F2} 游戏输入={_gameInputOn}"
                  + $" 音频暂停={AudioListener.pause}";
            // timeScale=0 不一定是异常：游戏暂停时本来就该是 0（scrController.TogglePauseGame 里
            // paused / audioPaused / Time.timeScale 三件事绑在一起做），编辑器一进去就是暂停状态。
            // 所以判据用游戏自己的 scrController.paused（只读 getter，没有副作用），不是去猜场景名。
            bool gamePaused = false;
            try { var sc = scrController.instance; gamePaused = sc != null && sc.paused; } catch { }
            state += $" 游戏暂停={gamePaused}";
            // 只在"异常"或"状态发生变化"时才写日志（正常情况完全不打扰）
            bool bad = (c != null && bpm <= 0.0001f) || (Time.timeScale <= 0.0001f && !gamePaused);
            if (!bad && state == _lastHealthState) return;
            _lastHealthState = state;
            _healthLogAt = Time.unscaledTime + 2f;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Lg.Info($"[LevelLib] 体征快照（面板已关）：{state} 场景={scene}"
                + (bad ? "  ← 【异常】" : ""));
        } catch { }
    }

    private static void EnsureMusic() {
        if (_musicGo != null) return;
        _musicGo = new GameObject("LevelLibMusic");
        Object.DontDestroyOnLoad(_musicGo);
        _musicPlayer = _musicGo.AddComponent<MusicPlayer>();
        // 背景音乐来源：玩家指定的那首优先（右键谱面 / 设置页选文件，持久化在 BgmPath）；
        // 没设过就播随资源包分发的默认曲 assets\bgm_default.mp3（生命流）。默认曲不在
        // （资源包还没下完）就保持安静，与"文件不存在"同一条路，不额外报错。
        _bgmCustomPath = string.IsNullOrEmpty(BgmPath) ? null : BgmPath;
        string boot = string.IsNullOrEmpty(BgmPath) ? DefaultBgmPath() : BgmPath;
        if (string.IsNullOrEmpty(BgmPath)) {
            Lg.Info(string.IsNullOrEmpty(boot)
                ? "[LevelLib] 没有设置背景音乐，默认曲（assets\\bgm_default.mp3）也不在 —— 安静"
                : "[LevelLib] 没有设置背景音乐，用随包默认曲: " + boot);
        }
        _musicPlayer.Init(boot);
    }

    /// <summary>随包默认曲的完整路径（assets\bgm_default.mp3，与默认静态背景同一套定位方式）。
    /// 文件不存在返回 null（调用方落到"安静"，与旧版行为一致）。</summary>
    private static string DefaultBgmPath() {
        try {
            string p = Path.Combine(Main.ModEntry.Path, "assets", "bgm_default.mp3");
            return File.Exists(p) ? p : null;
        } catch { return null; }
    }

    /// <summary>找选中谱面的音乐文件：优先 adofai 的 song 字段指向的文件（去富文本、容忍截断名），找不到就扫文件夹取第一个音频</summary>
    private static string FindLevelMusic(LevelEntry entry) {
        try {
            if (entry == null || string.IsNullOrEmpty(entry.FilePath)) {
                Lg.Info("FindLevelMusic: entry 或 FilePath 为空");
                return null;
            }
            string folder = Path.GetDirectoryName(entry.FilePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) {
                Lg.Info("FindLevelMusic: 文件夹不存在 " + (folder ?? "null"));
                return null;
            }
            // 收集所有音频文件（含子目录）
            var audios = new System.Collections.Generic.List<string>();
            try {
                foreach (var af in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)) {
                    string ext = Path.GetExtension(af).ToLowerInvariant();
                    if (ext == ".ogg" || ext == ".wav" || ext == ".mp3") audios.Add(af);
                }
            } catch (System.Exception ex) { Lg.Ex("FindLevelMusic 扫音频失败", ex); }
            if (audios.Count == 0) {
                Lg.Info("FindLevelMusic: 文件夹内无音频 " + folder);
                return null;
            }
            // 优先：adofai 的 song 字段（可能是文件名/相对路径/带富文本标签）
            string song = ReadAdofaiField(entry.FilePath, "song");
            if (!string.IsNullOrEmpty(song)) {
                song = StripRichText(song).Trim();   // 去富文本（如 "</color>" 残留）和空白
                string songName = Path.GetFileName(song);   // 去掉路径前缀
                string songBase = System.IO.Path.GetFileNameWithoutExtension(songName);
                foreach (var af in audios) {
                    string fn = Path.GetFileName(af);
                    string fb = System.IO.Path.GetFileNameWithoutExtension(fn);
                    // 精确名、去扩展名名、或包含匹配（song 名可能被截断）
                    if (string.Equals(fn, songName, System.StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fb, songBase, System.StringComparison.OrdinalIgnoreCase)
                        || fb.IndexOf(songBase, System.StringComparison.OrdinalIgnoreCase) >= 0) {
                        Lg.Info("FindLevelMusic 命中song: " + af + " (song字段=" + song + ")");
                        return af;
                    }
                }
            }
            // 兜底：取第一个音频
            Lg.Info("FindLevelMusic 兜底取第一个音频: " + audios[0] + " (song字段=" + (song ?? "") + ")");
            return audios[0];
        } catch (System.Exception ex) { Lg.Ex("FindLevelMusic 异常", ex); }
        return null;
    }

    /// <summary>这条谱面是不是当前背景音乐的来源（右键菜单用它决定显示"设置"还是"取消"）</summary>
    private static bool IsBgmSourceOf(LevelEntry e) {
        try {
            if (e == null || string.IsNullOrEmpty(_bgmCustomPath) || string.IsNullOrEmpty(e.FilePath)) return false;
            return _bgmCustomPath.StartsWith(Path.GetDirectoryName(e.FilePath), System.StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    /// <summary>设置/取消背景音乐（指定谱面版），入口是行右键菜单</summary>
    private static void ToggleLevelBgmFor(LevelEntry e) {
        try {
            EnsureMusic();
            if (e == null) { ShowToast(Lang.Tr("先选择一个谱面喵")); return; }
            bool curIsSource = IsBgmSourceOf(e);
            if (!curIsSource) {
                // 设为背景音乐（或切换到另一个谱面的音乐：覆盖前面的）
                string music = FindLevelMusic(e);
                if (string.IsNullOrEmpty(music)) { ShowToast(Lang.Tr("这个谱面没有找到音乐文件喵")); return; }
                _bgmCustomPath = music;
                BgmPath = music;   // 持久化：关游戏再开仍是这首
                BgmSource = e.FolderPath;   // 记录来源谱面
                AutoFeatures.Save();
                _musicPlayer.SetMusic(music);
                if (_musicPlayer.Source != null) _musicPlayer.Source.volume = MusicVolume / 100f;
                if (PortalUI.IsOpen) _musicPlayer.PlayMusic();
                ShowToast(Lang.Tr("已设为背景音乐喵"));
            } else {
                // 取消：清掉持久化，落回随包默认曲（默认曲不在就安静，SetMusic(null) 同款早退）
                _bgmCustomPath = null;
                BgmPath = "";
                BgmSource = "";
                AutoFeatures.Save();
                _musicPlayer.SetMusic(DefaultBgmPath());
                ShowToast(Lang.Tr("已取消背景音乐喵"));
            }
        } catch (System.Exception ex) { Lg.Ex("切换背景音乐失败", ex); }
    }

    /// <summary>设置页「设置背景音乐」：选本地的 MP3/OGG 直接当面板背景音乐。
    /// 无论原来有没有设过、是不是来自某个谱面，都会整体换成这一首（同一个 BgmPath，后来者覆盖）。</summary>
    private static void PickCustomBgm() {
        try {
            EnsureMusic();
            string path = ModernFileDialog.OpenFile("选择背景音乐", "音乐文件", "*.mp3;*.ogg");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            _bgmCustomPath = path;
            BgmPath = path;
            BgmSource = "";
            AutoFeatures.Save();
            _musicPlayer.SetMusic(path);
            if (_musicPlayer.Source != null) _musicPlayer.Source.volume = MusicVolume / 100f;
            if (PortalUI.IsOpen) _musicPlayer.PlayMusic();
            ShowToast(Lang.Tr("已设为背景音乐喵"));
        } catch (System.Exception ex) { Lg.Ex("选择背景音乐失败", ex); }
    }

    /// <summary>设置页「取消背景音乐」：清掉当前那首（不管它来自谱面还是本地选择），落回随包默认曲。</summary>
    private static void ClearCustomBgm() {
        try {
            EnsureMusic();
            if (string.IsNullOrEmpty(_bgmCustomPath)) { ShowToast(Lang.Tr("还没有设置背景音乐喵")); return; }
            _bgmCustomPath = null;
            BgmPath = "";
            BgmSource = "";
            AutoFeatures.Save();
            _musicPlayer.SetMusic(DefaultBgmPath());
            ShowToast(Lang.Tr("已取消背景音乐喵"));
        } catch (System.Exception ex) { Lg.Ex("取消背景音乐失败", ex); }
    }

    /// <summary>弹窗与提示的入场动画宿主（MonoBehaviour，跑原生协程，不依赖 DOTween，绝对稳定）</summary>
    private class FloatAnimator : MonoBehaviour {
        /// <summary>浮入：整体从下方 rise 像素浮上来，同时从透明淡到原样（dur 秒，三次缓出、不带缩放也不带过冲）。
        /// plate = 盒子自己那张图，它也要跟着淡：弹窗的磨砂是按"盒子透出几成"算的，
        /// 只淡 CanvasGroup 的话糊图会留在原地，比盒子还亮一截。</summary>
        public static void FloatIn(RectTransform rt, Image plate, Image veil, CanvasGroup group, float rise, float dur) {
            if (rt == null) return;
            var go = new GameObject("__FloatAnim");
            var a = go.AddComponent<FloatAnimator>();
            a._rt = rt;
            a._plate = plate;
            a._veil = veil;
            a._group = group;
            a._rise = rise;
            a._dur = dur;
            a._plateA = plate != null ? plate.color.a : 0f;
            a._veilA = veil != null ? veil.color.a : 0f;
            // 落点要等布局摆完那一帧才读得到（建盒子的时候位置还没定），这一帧先整个压成透明
            a.Apply(0f);
            a.StartCoroutine(a.Float());
        }

        /// <summary>淡入：CanvasGroup 透明度 0→1（dur 秒）。用于开发者详情渐显。带 token：快速切换时旧协程自动停止。</summary>
        public static void FadeIn(CanvasGroup grp, float dur) {
            if (grp == null) return;
            var go = new GameObject("__FadeAnim");
            var a = go.AddComponent<FloatAnimator>();
            a._grp = grp;
            a._dur = dur;
            a._token = ++_lastFadeToken;
            a.StartCoroutine(a.Fade());
        }

        /// <summary>浮出（收起）：与浮入同一条轨迹反向 —— 往下滑 rise、盒子/遮罩/整组一起淡到透明，
        /// 结束时回调 done（真正的销毁由调用方做）。磨砂按"盒子透出几成"算，所以每帧照旧 SyncGlassNow，
        /// 不然收窗那一小段糊图会停在原地亮一截。</summary>
        public static void FloatOut(RectTransform rt, Image plate, Image veil, CanvasGroup group, float rise, float dur, System.Action done) {
            if (rt == null || group == null) { if (done != null) done(); return; }
            var go = new GameObject("__FloatAnimOut");
            var a = go.AddComponent<FloatAnimator>();
            a._rt = rt; a._plate = plate; a._veil = veil; a._group = group;
            a._rise = rise; a._dur = dur; a._done = done;
            a._plateA = plate != null ? plate.color.a : 0f;
            a._veilA = veil != null ? veil.color.a : 0f;
            a.StartCoroutine(a.OutFloat());
        }

        private System.Action _done;

        private System.Collections.IEnumerator OutFloat() {
            Vector2 start = _rt.anchoredPosition;
            float t = 0f;
            while (t < _dur) {
                if (_rt == null) break;                 // 窗在收的半路被销毁（换场景等）：不用回调，别把新窗炸掉
                t += Time.unscaledDeltaTime;            // 同浮入：timeScale 可能是 0，必须 unscaled
                float k = Mathf.Clamp01(t / _dur);
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                _rt.anchoredPosition = new Vector2(start.x, start.y - _rise * e);
                Apply(1f - e);
                PortalUI.SyncGlassNow();
                yield return null;
            }
            if (_rt != null) {
                _rt.anchoredPosition = new Vector2(start.x, start.y - _rise);
                Apply(0f);
                PortalUI.SyncGlassNow();
            }
            if (_done != null) _done();
            Destroy(gameObject);
        }

        private static int _lastFadeToken;   // 淡入版本号：新点击打断旧协程

        private CanvasGroup _grp;
        private float _dur;
        private int _token;
        private RectTransform _rt;
        private Image _plate, _veil;
        private CanvasGroup _group;
        private float _rise, _plateA, _veilA;

        /// <summary>按进度写透明度：盒子、遮罩、以及盒子底下那棵子树的 CanvasGroup。</summary>
        private void Apply(float e) {
            if (_plate != null) { var c = _plate.color; c.a = _plateA * e; _plate.color = c; }
            if (_veil != null) { var c = _veil.color; c.a = _veilA * e; _veil.color = c; }
            if (_group != null) _group.alpha = e;
        }

        private System.Collections.IEnumerator Fade() {
            if (_grp == null) { Destroy(gameObject); yield break; }
            _grp.alpha = 0f;
            float t = 0f;
            while (t < _dur) {
                if (_grp == null) { Destroy(gameObject); yield break; }
                if (_token != _lastFadeToken) { Destroy(gameObject); yield break; }   // 已被更新的淡入取代
                t += Time.unscaledDeltaTime;
                _grp.alpha = Mathf.Clamp01(t / _dur);
                yield return null;
            }
            if (_grp != null) _grp.alpha = 1f;
            Destroy(gameObject);
        }

        private System.Collections.IEnumerator Float() {
            if (_rt == null) { Destroy(gameObject); yield break; }
            yield return null;                       // 等这一帧把布局摆完，才读得到盒子最终落在哪儿
            if (_rt == null) { Destroy(gameObject); yield break; }
            Vector2 target = _rt.anchoredPosition;
            _rt.anchoredPosition = new Vector2(target.x, target.y - _rise);
            // 协程在 Update 之后才推进，常驻段那次磨砂同步看到的是上一帧的矩形：盒子挪了、糊图没挪，
            // 边上会露出一圈亮边。这里每改一次位置就补一次同步。
            PortalUI.SyncGlassNow();
            // 必须用 unscaledDeltaTime：弹窗常冻结游戏（timeScale=0），scaled deltaTime 会死循环
            float t = 0f;
            while (t < _dur) {
                if (_rt == null) { Destroy(gameObject); yield break; }
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / _dur);
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                _rt.anchoredPosition = new Vector2(target.x, target.y - _rise * (1f - e));
                Apply(e);
                PortalUI.SyncGlassNow();
                yield return null;
            }
            if (_rt != null) _rt.anchoredPosition = target;
            Apply(1f);
            PortalUI.SyncGlassNow();
            Destroy(gameObject);
        }
    }

    /// <summary>主界面背景音乐播放器：文件加载一次缓存，循环播放</summary>
    private class MusicPlayer : MonoBehaviour {
        public AudioSource Source;
        private AudioClip _clip;
        /// <summary>当前 clip 对应的那次请求。换曲时必须和旧 clip 一起放掉（见 ReleaseClip）</summary>
        private UnityEngine.Networking.UnityWebRequest _uwr;
        public string _path;   // 当前播放音乐路径（公开是为了"正在播放"显示）

        /// <summary>当前播放的歌名（去掉扩展名），无则空。随包默认曲显示成曲名而不是文件名。</summary>
        public string SongName {
            get {
                if (string.IsNullOrEmpty(_path)) return "";
                try {
                    if (string.Equals(System.IO.Path.GetFileName(_path), "bgm_default.mp3", System.StringComparison.OrdinalIgnoreCase))
                        return "生命流";
                    return System.IO.Path.GetFileNameWithoutExtension(_path);
                } catch { return ""; }
            }
        }

        public void Init(string path) {
            _path = path;
            Source = gameObject.AddComponent<AudioSource>();
            Source.loop = true;
            Source.playOnAwake = false;
            Source.volume = PortalUI.MusicVolume / 100f;   // 应用设置里的音量
            StartCoroutine(LoadClip());
        }

        /// <summary>换曲：停旧曲重新加载新文件。不能只把 _clip = null —— AudioClip 是 Unity 对象，
        /// 光丢引用不会立刻释放（按谱面切歌每切一次就多留一首歌解出来的 PCM），
        /// 所以旧 clip 和旧请求成对走 ReleaseClip，任何时刻只留当前这一首。</summary>
        public void SetMusic(string path) {
            _path = path;
            if (Source != null) {
                Source.Stop();
                Source.clip = null;
            }
            ReleaseClip();
            StartCoroutine(LoadClip());
        }

        /// <summary>释放"当前 clip + 它那次请求"：两个都是 Unity 对象，必须显式销毁/释放。
        /// 故意成对、并且只在这里做 —— 新 clip 的请求要等它被换掉时才放，
        /// 免得提前 Dispose 把正在用的 AudioClip 一起弄失效。</summary>
        private void ReleaseClip() {
            if (_clip != null) { try { UnityEngine.Object.Destroy(_clip); } catch { } _clip = null; }
            if (_uwr != null) { try { _uwr.Dispose(); } catch { } _uwr = null; }
        }

        private static AudioType AudioTypeFor(string path) {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ogg") return AudioType.OGGVORBIS;
            if (ext == ".wav") return AudioType.WAV;
            return AudioType.MPEG;
        }

        private System.Collections.IEnumerator LoadClip() {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) {
                Lg.Info($"[LevelLib] 背景音乐文件不存在: {_path}");
                yield break;
            }
            // 这个 Unity 版本里 GetAudioClip 没有三参数重载（编译报 CS1501），
            // 流式开关只能在下载处理器上打：DownloadHandlerAudioClip.streamAudio = true
            var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + _path.Replace('\\', '/'), AudioTypeFor(_path));
            try {
                var dh = uwr.downloadHandler as DownloadHandlerAudioClip;
                if (dh != null) dh.streamAudio = true;
            } catch { }
            yield return uwr.SendWebRequest();
            if ((uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success) && uwr.downloadHandler != null) {
                // 卡顿点名：日志里"背景音乐加载完成"那一行后面紧跟一个 800+ ms 的巨卡帧，
                // 量下来就是这一步把整曲 mp3 在主线程上解成 PCM（3.2MB 的 mp3 要 1526ms）。
                // 所以改成流式加载，不在主线程上一次吃掉；流式拿不到就回退老的非流式路径（宁可卡一下，不能没声音）。
                var swDecode = System.Diagnostics.Stopwatch.StartNew();
                AudioClip streamed = null;
                try { streamed = DownloadHandlerAudioClip.GetContent(uwr); } catch { streamed = null; }
                if (streamed != null && streamed.loadState != AudioDataLoadState.Failed) {
                    try { if (streamed.loadState != AudioDataLoadState.Loaded) streamed.LoadAudioData(); } catch { }
                    _clip = streamed;
                    _uwr = uwr;      // 留着，换曲时由 ReleaseClip 和旧 clip 一起放掉（提前 Dispose 有让 clip 失效的风险）
                    Source.clip = _clip;
                    Lg.Info($"[LevelLib] 背景音乐加载完成（流式 {swDecode.ElapsedMilliseconds}ms"
                        + $" 状态={_clip.loadState} 不在主线程上整曲解码）: {_path}");
                    // 加载完成时面板还开着就自动补播（避免首次打开时 clip 未就绪导致没声）
                    if (PortalUI.IsOpen && !Source.isPlaying) Source.Play();
                    yield break;
                }
                // 流式这条路没成 → 老路径（非流式：整曲一次解码，会卡一下但那才有声音）
                try { if (streamed != null) UnityEngine.Object.Destroy(streamed); } catch { }
                try { uwr.Dispose(); } catch { }
                Lg.Info("背景音乐：流式加载没成 ->  回退非流式（会卡一下 但有声音）");
                var uwr2 = UnityWebRequestMultimedia.GetAudioClip("file:///" + _path.Replace('\\', '/'), AudioTypeFor(_path));
                yield return uwr2.SendWebRequest();
                if ((uwr2.result != UnityEngine.Networking.UnityWebRequest.Result.Success) || uwr2.downloadHandler == null) {
                    Lg.Info($"[LevelLib] 背景音乐加载失败: {uwr2.error}");
                    try { uwr2.Dispose(); } catch { }
                    yield break;
                }
                uwr = uwr2;
                var swDecode2 = System.Diagnostics.Stopwatch.StartNew();
                _clip = DownloadHandlerAudioClip.GetContent(uwr);
                long decodeMs = swDecode2.ElapsedMilliseconds;
                if (decodeMs >= 50) {
                    Lg.Info($"[LevelLib] 【卡顿】背景音乐解码耗时 = {decodeMs}ms"
                        + $"（{System.IO.Path.GetFileName(_path)} 这一段是主线程上的整曲解码）");
                }
                _uwr = uwr;      // 留着，换曲时由 ReleaseClip 和旧 clip 一起放掉（提前 Dispose 有让 clip 失效的风险）
                Source.clip = _clip;
                Lg.Info($"[LevelLib] 背景音乐加载完成: {_path}");
                // 加载完成时面板还开着就自动补播（避免首次打开时 clip 未就绪导致没声）
                if (PortalUI.IsOpen && !Source.isPlaying) Source.Play();
            } else {
                Lg.Info($"[LevelLib] 背景音乐加载失败: {uwr.error}");
                try { uwr.Dispose(); } catch { }   // 失败这条没有 clip 依赖它 → 立刻放掉（换曲会反复重试，不放就是每次泄漏一个）
            }
        }

        public void PlayMusic() {
            PortalUI._bgmWanted = true;
            // 取消还没走完的淡出：淡出途中玩家又把面板打开了的话，不取消就会被继续压到 0（音乐莫名消失）
            PortalUI._bgmFadeT = -1f;
            if (Source != null) Source.volume = Mathf.Max(Source.volume, Mathf.Clamp01(PortalUI.MusicVolume / 100f));
            if (Source != null && _clip != null && !Source.isPlaying) Source.Play();
        }

        public void StopMusic() {
            PortalUI._bgmWanted = false;
            if (Source == null) return;
            if (!Source.isPlaying) { Source.Stop(); return; }
            if (PortalUI._bgmFadeT >= 0f) return;   // 已经在淡出：别重来一遍（RequestClosePanel 与 ClosePanel 都会调）
            // 关面板不瞬停：交给逐帧淡出（TickBgmWatchdog 最前面那段），0.6s 压到 0 再 Stop
            PortalUI._bgmFadeT = 0f;
            PortalUI._bgmFadeFrom = Source.volume;
        }
    }
}
