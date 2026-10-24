// [split-of] PortalUI.cs lines 3799-4057
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

/// <summary>PortalUI 的设置页几何常量与字段、头像与玩家名字选择。</summary>
public static partial class PortalUI {
    private const float SetLeft = 140f;    // 距屏幕左边
    private const float SetTop = TopNavH;     // 距屏幕顶部（让开右上角那排导航小方块，它占 20~70）
    private const float NavBtnW = 220f;    // 主导航按钮宽
    private const float NavBtnH = 54f;     // 主导航按钮高
    private const float NavGap = 12f;      // 主导航按钮间距
    // 主 tab 区背景遮罩：半透明黑长方形，比导航列宽一圈把它衬起来
    private const float NavBackdropLeft = SetLeft - 40f;
    private const float NavBackdropW = NavBtnW + 80f;
    /// <summary>功能区遮罩比内容区左右各宽出这么多</summary>
    private const float BodyBackdropPad = 32f;
    /// <summary>功能区（右侧内容）左边与宽度。右端按用户标注的红线位置定（屏幕 x≈1780 作右边缘），
    /// 遮罩左边缘 = BodyLeft - BodyBackdropPad = 420，所以 BodyW = 1780 - 420 = 1360，与主 tab 遮罩留 20px 空隙。
    /// 行宽 RowW 保持与遮罩同宽，控制件才贴得到遮罩右边缘。</summary>
    private const float BodyLeft = 450f;
    private const float BodyW = 1360f;
    private const float RowH = 48f;        // 内容行高
    /// <summary>内容行间距。间距只是切分条目，真正的呼吸感来自行高本身。
    /// 「外观」页那组有 14 行：14×RowH + 13×SetRowGap 必须 ≤ 内容容器高 783（= (1080-140) - (92+65)）。
    /// 原来 RowH=54 / gap=8 时是 868，溢出 85px —— 这一页没有滚动机制，
    /// 多出去的那几行会掉到容器外面，玩家看不见也点不到。定稿 48/8 时 776，留 7px 余量。</summary>
    private const float SetRowGap = 8f;   // 内容行间距（见上：受「外观」页 14 行的上限约束）
    /// <summary>行宽 = 功能区遮罩宽（BodyW）：行宽比遮罩窄时，控制件只贴到"行的右边"、离遮罩右边缘还差一截，
    /// 看起来就是"控制件没到右边缘"。</summary>
    private const float RowW = 1360f;
    private const float RowCtrlZone = 330f;

    // ---------- 设置页字体尺寸（集中在这里，方便统一调大调小） ----------
    private const int FontNav = 27;        // 左侧主 tab
    private const int FontSub = 25;        // 子 tab
    private const int FontRow = 27;        // 行标签
    private const int FontBtn = 23;        // 行内按钮/开关
    private const int FontHint = 20;       // 说明小字

    private static RectTransform _setPageRoot;
    private static RectTransform _setNavCol;
    private static RectTransform _setBodyRt;
    private static int _setMainTab;
    private static readonly List<Text> _setNavLabels = new List<Text>();
    private static RectTransform _setNavFrameRt;

    private const float FramePad = 8f;
    private static float _setFrameCurTop, _setFrameTargetTop;
    private static Vector2 _setFrameCurSize, _setFrameTargetSize;

    // ---------- 子 tab 行的"滑动选中块" ----------
    /// <summary>子 tab 按钮的几何（集中在这里，别写死在 BuildSubTabsFor 里）</summary>
    private const float SubBtnW = 150f;
    private const float SubBtnH = 46f;
    private const float SubBtnGap = 16f;
    /// <summary>选中块比按钮外扩多少：横 4 / 纵 2，块 158×50（按钮 150×46），块顶离 SetTop(140) 留 6px。
    /// 曾经是横 6 / 纵 4，块 162×54，块顶只剩 4px 余量 = 顶到界面上边缘，横向再放宽就会重现。
    /// 横向故意不等于主 tab 的 FramePad："同款"指的是同一套机制（实心圆角块 + HomeSlide 滑过去 + 深色字），
    /// 不是外扩数值照抄。</summary>
    private const float SubFramePadX = 4f;
    private const float SubFramePadY = 2f;
    /// <summary>滑动器与主 tab 导航块 / 官谱库那三块同一套 HomeSlide（指数平滑，快速连点从当前位置接着走）</summary>
    private static readonly HomeSlide _setSubSlide = new HomeSlide();
    /// <summary>当前子 tab 行是按哪一组名字建的（拼接串）。
    /// RefreshSettingsPage 每次都会调 BuildSubTabsFor，而点子 tab 也要走它 —— 无条件销毁重建的话，
    /// 选中块每次都被摆回起点、永远滑不起来，所以名字没变就一个对象都不重建。</summary>
    private static string _setSubBuiltKey;

    private static bool _settingsPageVisible;
    private static float _setFade = 1f;
    /// <summary>设置页根节点的 CanvasGroup 缓存。淡入期间每帧要写 alpha，
    /// 原来每帧 GetComponent 一次；组件是建页时挂上就再不动的，这里存住即可。
    /// 万一被销毁，Unity 的 == null 让 TickSettingsPage 退回原来那条查找（并报同样的错）。</summary>
    private static CanvasGroup _settingsPageGroup;
    private static readonly List<GameObject> _setPageObjs = new List<GameObject>();
    private static readonly List<Button> _setSubButtons = new List<Button>();
    private static GameObject _setSubBar;
    private static int _setSubTab;

    // 设置页顶部的三个主 tab 复用全局 tab 栏，不另建一份（另建会出现两组 tab 残影）

    /// <summary>设置页所有行的记录（子 tab 归属 + 建页时落在哪），用于按子 tab 显隐、行增减时重排</summary>
    private static readonly List<SetRow> _setRows = new List<SetRow>();

    /// <summary>开发者页自己那层压暗（切 tab 时 0 → 目标值渐入）。
    /// 设置页那层已删：三个 tab 统一用面板的全局遮罩，两层叠一起会变黑。</summary>
    private static Image _devVeilImg;
    private const float DevVeilTarget = 0.40f;        // 开发者页遮罩深度（比库页的 0.45 略浅）
    private static float _devVeilCur;

    /// <summary>快捷键行按钮（用于显示当前绑定键）</summary>
    private static Button _hotkeyBtnOpen, _hotkeyBtnRandom;

    /// <summary>快捷键按钮当前显示的文字：只在绑定键或捕获状态变化时才更新（避免每帧拼字符串）</summary>
    private static string _hotkeyShownOpen, _hotkeyShownRandom;

    /// <summary>设置页淡入已经走了多少帧（带兜底，防止 tick 异常导致页面永远全透明）</summary>
    private static int _setFadeTicks;

    // ---------- 设置页内容（三级 tab） ----------

    /// <summary>各主 tab 内容容器下的"页"（按建出来的顺序，下标即第几页）</summary>
    private static readonly Dictionary<int, List<GameObject>> _stackPages = new Dictionary<int, List<GameObject>>();

    /// <summary>切换顶层 tab 时的背景遮罩透明度动画（0~1）：库页 0 → 设置 / 开发者页满</summary>
    private static float _veilTarget;      // 目标透明度（0~1）
    private static float _veilCur;         // 当前透明度
    private static bool _veilAnimating;

    /// <summary>选择头像：按短边放大到 1024 见方 → 圆形裁切 → 缩到 256 存成 assets/avatar_custom.png</summary>
    private static void PickAvatar() {
        try {
            string path = ModernFileDialog.OpenFile("选择头像图片", "图片文件", "*.png;*.jpg;*.jpeg;*.bmp");
            if (string.IsNullOrEmpty(path)) return;
            string dst = Path.Combine(Main.ModEntry.Path, "assets", "avatar_custom.png");
            using (var src = new System.Drawing.Bitmap(path))
            using (var canvas = new System.Drawing.Bitmap(1024, 1024, System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                using (var g = System.Drawing.Graphics.FromImage(canvas)) {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    using (var clip = new System.Drawing.Drawing2D.GraphicsPath()) {
                        clip.AddEllipse(0, 0, 1024, 1024);
                        g.SetClip(clip);
                        float scale = System.Math.Max(1024f / src.Width, 1024f / src.Height);
                        int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, (1024 - w) / 2, (1024 - h) / 2, w, h);
                    }
                }
                using (var final = new System.Drawing.Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g2 = System.Drawing.Graphics.FromImage(final)) {
                    g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g2.DrawImage(canvas, 0, 0, 256, 256);
                    final.Save(dst, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            AvatarPath = dst;
            AutoFeatures.Save();
            ApplyAvatarNow();   // 立即刷新面板内 / 界面外两处头像（缓存也要清）
            ShowToast(Lang.Tr("头像已更换喵"));
            CheckPersonaAch();
        } catch (System.Exception e) {
            Lg.Ex("选择头像失败", e);
            ShowToast(Lang.Tr("头像保存失败喵"));
        }
    }

    /// <summary>立即重新加载头像并刷新面板内 / 界面外两处显示
    /// （换头像写的是同一个文件路径，必须清缓存重新解码，否则会一直显示旧头像）</summary>
    private static void ApplyAvatarNow() {
        if (string.IsNullOrEmpty(AvatarPath)) return;
        InvalidateAssetSprite(AvatarPath);
        var spr = LoadAssetSprite(AvatarPath);
        if (spr == null) return;
        System.Action<Image> set = img => {
            if (img == null) return;
            img.sprite = spr;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
        };
        set(_panelAvatarImg);
        set(_outsideAvatarImg);
    }

    /// <summary>修改玩家名字（弹窗输入）</summary>
    private static void PickPlayerName() {
        if (_nameCanvas != null) return;
        _nameCanvas = MakeCanvas("LevelLib_Name", 84500);
        var bg = MakeDialogVeil(_nameCanvas.transform);
        var box = MakeDialogBox(_nameCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(620f, 280f);
        var title = MakeTitle(box.transform, "T", 30, new Color(1f, 0.72f, 0.25f, 1f), TextAnchor.MiddleCenter);
        title.text = Lang.Tr("修改玩家名字");
        title.rectTransform.anchorMin = new Vector2(0f, 0.76f);
        title.rectTransform.anchorMax = new Vector2(1f, 0.96f);
        title.rectTransform.offsetMin = Vector2.zero; title.rectTransform.offsetMax = Vector2.zero;
        var inputBg = MakeImage(box.transform, "InputBg", new Color(0.08f, 0.10f, 0.16f, 1f));
        var ibrt = inputBg.rectTransform;
        ibrt.anchorMin = new Vector2(0.08f, 0.40f); ibrt.anchorMax = new Vector2(0.92f, 0.62f);
        ibrt.offsetMin = Vector2.zero; ibrt.offsetMax = Vector2.zero;
        var it = MakeText(inputBg.transform, "IT", 24, Color.white, TextAnchor.MiddleLeft);
        Stretch(it.rectTransform, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        it.text = "";
        var input = inputBg.gameObject.AddComponent<InputField>();
        input.textComponent = it;
        input.text = PlayerName ?? "";
        var ok = MakeButton(box.transform, "Ok", new Vector2(80f, -80f), new Vector2(140f, 44f), Accent);
        var okT = MakeText(ok.transform, "确定", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => {
            PlayerName = input.text ?? "";
            if (_panelNameText != null) _panelNameText.text = PlayerName;
            AutoFeatures.Save();
            if (_nameCanvas != null) { Object.Destroy(_nameCanvas.gameObject); _nameCanvas = null; }
            ShowToast(Lang.Tr("名字已保存喵"));
            CheckPersonaAch();
        });
        var cancel = MakeButton(box.transform, "Cancel", new Vector2(-80f, -80f), new Vector2(140f, 44f), ItemColor);
        var cT = MakeText(cancel.transform, "取消", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(cT.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => { if (_nameCanvas != null) { Object.Destroy(_nameCanvas.gameObject); _nameCanvas = null; } });
    }

    /// <summary>圆角空心框贴图（九宫格拉伸用），由 CreateRoundedFrameSprite 生成后缓存在这里</summary>
    private static Sprite _roundedFrameSprite;

    // ---------- 文件缺失提示 ----------

    // ---------- UI 工具 ----------

    // ---------- 背景音乐（常驻加载，面板开关时播/停） ----------

    // 节拍静音（把 conductor.bpm 置 0 再还原）那套已整块删除：它和游戏自己的节拍/暂停/输入互相干扰，越修越复杂。
    // 现在改走输入层隔离 —— 面板打开时关掉游戏自己的输入（SetGameInputEnabled），
    // 游戏收不到按键就不会误触发"自动标记"之类，于是根本不需要动 bpm。

    // 诊断用：游戏是不是正在读按键 —— RDInput.cancelPress 等都是属性（内部 GetState 遍历 inputs 看 isActive），
    // 输入隔离生效时，面板开着它们应全部为 false。
}
