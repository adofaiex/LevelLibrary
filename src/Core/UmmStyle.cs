using UnityEngine;

namespace LevelLibrary;

/// <summary>UMM 设置页圆角样式（IMGUI 自绘圆角纹理 + GUIStyle）</summary>
public static class UmmStyle {
    /// <summary>主题蓝：取自 Harmony logo 的主色（#75A4D2）—— 标题和所有按钮都用它</summary>
    public static readonly Color Blue = new Color(0.459f, 0.643f, 0.824f, 1f);
    /// <summary>主题蓝压暗一档（用于"未选中"的语言按钮，跟选中的形成对比）</summary>
    public static readonly Color BlueDim = new Color(0.26f, 0.36f, 0.47f, 1f);

    private static Texture2D _roundTex;
    private static GUIStyle _btn;
    private static GUIStyle _label;
    private static GUIStyle _title;

    public static GUIStyle Btn => _btn ?? Init();
    public static GUIStyle Label => _label ?? Init();
    public static GUIStyle Title => _title ?? Init();

    private static GUIStyle Init() {
        _roundTex = MakeRoundedTexture(32, 10f);
        _title = new GUIStyle(GUI.skin.label) {
            normal = { textColor = Blue },
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 4, 4),
        };
        _btn = new GUIStyle(GUI.skin.button) {
            normal = { background = _roundTex, textColor = Color.white },
            hover = { background = _roundTex, textColor = Color.white },
            active = { background = _roundTex, textColor = Color.white },
            border = new RectOffset(10, 10, 10, 10),
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            padding = new RectOffset(12, 12, 6, 6),
        };
        _label = new GUIStyle(GUI.skin.label) {
            normal = { textColor = new Color(0.9f, 0.9f, 0.95f, 1f) },
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 4, 4),
        };
        return _btn;
    }

    /// <summary>生成白色圆角纹理（透明角落），配 GUI.backgroundColor 着色</summary>
    private static Texture2D MakeRoundedTexture(int size, float radius) {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        PerfDiag.Tag(tex, "ummskin");   // 只建一次：三个 GUIStyle 的 ?? 守卫共用同一个 Init
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                bool inside = dx * dx + dy * dy <= radius * radius;
                tex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        return tex;
    }
}
