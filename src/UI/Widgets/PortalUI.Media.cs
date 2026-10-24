// [split-of] PortalUI.cs lines 2633-2866
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

/// <summary>PortalUI 的音频时长解析与谱面字段缓存：wav/mp3/ogg 时长、adofai 顶层字段、角度数。</summary>
public static partial class PortalUI {
    private static string StripRichText(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        try {
            return System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Trim();
        } catch { return s; }
    }

    /// <summary>读音频时长（秒）：ogg 用 granule 精确算，wav 用 RIFF 头，mp3 按码率估算；失败返回 0</summary>
    private static double ReadAudioDuration(string file) {
        try {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".ogg") return OggDuration(file);
            if (ext == ".wav") return WavDuration(file);
            if (ext == ".mp3") return Mp3Duration(file);
        } catch { }
        return 0d;
    }

    /// <summary>WAV 时长：扫 chunk 找 fmt/data（兼容 JUNK 等填充块），公式 = data 大小 / (采样率×通道×位深/8)</summary>
    private static double WavDuration(string file) {
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
            byte[] h = new byte[12];
            if (fs.Read(h, 0, 12) < 12) return 0d;
            if (h[0] != 0x52 || h[1] != 0x49 || h[2] != 0x46 || h[3] != 0x46) return 0d;   // RIFF
            long len = fs.Length;
            long pos = 12;
            long dataSize = -1;
            int rate = 0, ch = 0, bits = 0;
            while (pos + 8 <= len) {
                fs.Seek(pos, SeekOrigin.Begin);
                byte[] c = new byte[8];
                if (fs.Read(c, 0, 8) < 8) break;
                int cid = c[0] | (c[1] << 8) | (c[2] << 16) | (c[3] << 24);
                int csize = System.BitConverter.ToInt32(c, 4);
                if (cid == 0x20746D66) {   // "fmt "
                    int fl = System.Math.Min(csize, 40);
                    byte[] fmt = new byte[fl];
                    fs.Seek(pos + 8, SeekOrigin.Begin);
                    fs.Read(fmt, 0, fl);
                    if (fl >= 8) {
                        ch = System.BitConverter.ToInt16(fmt, 2);
                        rate = System.BitConverter.ToInt32(fmt, 4);
                    }
                    if (fl >= 16) bits = System.BitConverter.ToInt16(fmt, 14);   // 标准 16 字节 fmt：位深在 offset 14
                } else if (cid == 0x61746164) {   // "data"
                    dataSize = csize;
                }
                pos += 8 + csize + (csize & 1);   // chunk 对齐到偶数
            }
            if (dataSize > 0 && rate > 0 && ch > 0 && bits > 0) return (double)dataSize / (rate * ch * (bits / 8d));
        }
        return 0d;
    }

    /// <summary>MP3 时长：用第一个帧头的码率估算（文件大小 × 8 / 码率），只认 MPEG1 Layer3</summary>
    private static double Mp3Duration(string file) {
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
            byte[] h = new byte[4];
            fs.Read(h, 0, 4);
            if (h[0] == 0xFF && (h[1] & 0xE0) == 0xE0) {
                int version = (h[1] >> 3) & 3;
                int layer = (h[1] >> 1) & 3;
                int bitrateIdx = (h[2] >> 4) & 15;
                int srIdx = (h[2] >> 2) & 3;
                int[] br = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };   // MPEG1 L3
                int[] sr = { 44100, 48000, 32000, 0 };
                if (version == 3 && layer == 1 && bitrateIdx > 0 && bitrateIdx < 15 && srIdx < 3) {
                    double kbps = br[bitrateIdx];
                    return fs.Length * 8d / (kbps * 1000d);
                }
            }
        }
        return 0d;
    }

    /// <summary>OGG 时长：开头 Vorbis 识别头拿采样率，末尾最后页头的 granule position 精确算（冰与火之舞只支持 ogg）</summary>
    private static double OggDuration(string file) {
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {   // 游戏正在播放也能读
            int sampleRate = 44100;
            byte[] head = new byte[8192];
            int headLen = fs.Read(head, 0, head.Length);
            // Vorbis 识别头：0x01 'v' 'o' 'r' 'b' 'i' 's'(7) + version(4) + channels(1) + rate(4 LE) → rate 在 +12
            for (int i = 0; i + 15 < headLen; i++) {
                if (head[i] == 0x01 && head[i + 1] == 0x76 && head[i + 2] == 0x6f && head[i + 3] == 0x72
                    && head[i + 4] == 0x62 && head[i + 5] == 0x69 && head[i + 6] == 0x73) {
                    int r = System.BitConverter.ToInt32(head, i + 12);
                    if (r > 0) sampleRate = r;
                    break;
                }
            }
            // 末尾 64KB 内从后往前找最后一个 "OggS" 页头，读 granule position（offset 6，8 字节 LE）
            long len = fs.Length;
            int tailLen = (int)System.Math.Min(65536, len);
            byte[] tail = new byte[tailLen];
            fs.Seek(len - tailLen, SeekOrigin.Begin);
            fs.Read(tail, 0, tailLen);
            long granule = -1;
            for (int i = tailLen - 4; i >= 0; i--) {
                if (tail[i] == 0x4f && tail[i + 1] == 0x67 && tail[i + 2] == 0x67 && tail[i + 3] == 0x53) {   // OggS
                    granule = System.BitConverter.ToInt64(tail, i + 6);
                    break;
                }
            }
            if (granule >= 0 && sampleRate > 0) return (double)granule / sampleRate;
        }
        return 0d;
    }

    /// <summary>文件存在性（缓存版）：首次 File.Exists 后缓存，滚动时不再反复磁盘查询</summary>
    private static bool CachedFileExists(string file) {
        try {
            if (string.IsNullOrEmpty(file)) return false;
            if (_existsCache.TryGetValue(file, out var cached)) return cached;
            bool exists = File.Exists(file);
            _existsCache[file] = exists;
            return exists;
        } catch { return false; }
    }

    /// <summary>读 artist（缓存版）：首次读盘解析，之后命中缓存，滚动列表不再每行读文件</summary>
    private static string GetCachedArtist(string file) {
        try {
            if (string.IsNullOrEmpty(file)) return null;
            if (_artistCache.TryGetValue(file, out var cached)) return cached;
            string artist = ReadAdofaiField(file, "artist");
            _artistCache[file] = artist;   // null 也缓存（避免反复读盘）
            return artist;
        } catch { return null; }
    }

    /// <summary>adofai 文件文本缓存：谱面详情/列表要反复解析同一文件（artist/bpm/angleData/duration）。
    /// 按路径缓存文本，并用"代次 + 条数上限"控制失效与内存。</summary>
    private const int LevelTextCacheMax = 24;
    private static readonly Dictionary<string, string> _levelTextCache = new();
    private static long _levelCacheGen;
    private static readonly Dictionary<string, long> _levelCacheGenOf = new();

    /// <summary>取谱面文件文本（带缓存；文件已删返回 null）</summary>
    private static string GetLevelText(string file) {
        if (string.IsNullOrEmpty(file)) return null;
        if (_levelTextCache.TryGetValue(file, out var cached)) {
            // 只有"代次记录存在且与当前一致"的缓存才算有效：缓存被清后两边状态不一致时绝不能返回旧内容
            if (_levelCacheGenOf.TryGetValue(file, out var g) && g == _levelCacheGen) return cached;
            _levelTextCache.Remove(file);
            _levelCacheGenOf.Remove(file);
        }
        try {
            if (!File.Exists(file)) return null;
            string text = File.ReadAllText(file);
            if (_levelTextCache.Count >= LevelTextCacheMax) {
                _levelTextCache.Clear();
                _levelCacheGenOf.Clear();
            }
            _levelTextCache[file] = text;
            _levelCacheGenOf[file] = _levelCacheGen;
            return text;
        } catch { return null; }
    }

    /// <summary>清空谱面文本缓存（列表刷新时调用；用代次标记，未再访问的条目下次自动淘汰）</summary>
    private static void InvalidateLevelTextCache() {
        _levelCacheGen++;
        if (_levelTextCache.Count > 0) { _levelTextCache.Clear(); _levelCacheGenOf.Clear(); }
    }

    private static string ReadAdofaiField(string file, string field) {
        try {
            string text = GetLevelText(file);
            if (text == null) return null;
            string key = "\"" + field + "\"";
            int idx = text.IndexOf(key, System.StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = text.IndexOf(':', idx + key.Length);
            if (colon < 0) return null;
            int s = colon + 1;
            while (s < text.Length && (text[s] == ' ' || text[s] == '\t' || text[s] == '\r' || text[s] == '\n')) s++;
            if (s >= text.Length) return null;
            if (text[s] == '"') {
                int e = text.IndexOf('"', s + 1);
                if (e < 0) return null;
                return text.Substring(s + 1, e - s - 1);
            }
            int e2 = s;
            while (e2 < text.Length && (char.IsDigit(text[e2]) || text[e2] == '.' || text[e2] == '-')) e2++;
            return e2 > s ? text.Substring(s, e2 - s) : null;
        } catch { return null; }
    }

    /// <summary>数格子数：优先 angleData（角度数组按逗号数），没有就用 pathData（方向字符串长度）</summary>
    private static int CountAngleData(string file) {
        try {
            string text = GetLevelText(file);
            if (text == null) return 0;
            int idx = text.IndexOf("\"angleData\"", System.StringComparison.Ordinal);
            if (idx >= 0) {
                int open = text.IndexOf('[', idx);
                int close = open >= 0 ? text.IndexOf(']', open) : -1;
                if (open >= 0 && close > open + 1) {
                    string arr = text.Substring(open + 1, close - open - 1);
                    if (!string.IsNullOrWhiteSpace(arr)) return arr.Split(',').Length;
                }
            }
            // 新版/部分谱面用 pathData：方向字符串长度 = 格子数
            int pi = text.IndexOf("\"pathData\"", System.StringComparison.Ordinal);
            if (pi >= 0) {
                int q = text.IndexOf('"', pi + 10);   // 值开始引号
                if (q >= 0) {
                    int q2 = text.IndexOf('"', q + 1);   // 值结束引号
                    if (q2 > q) return q2 - q - 1;
                }
            }
        } catch { }
        return 0;
    }

    /// <summary>清掉游戏 UI 的焦点（`StandaloneInputModule.Process` 之前调用）。
    /// 咱 mod 自己界面（根节点名 `LevelLib_*`）的焦点一律保留，打字不受影响。</summary>
    public static void ClearGameFocus() {
        if (_panelCanvas == null) return;
        try {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) {
                var sel = es.currentSelectedGameObject;
                // 咱 mod 的任何界面都保留焦点，只清游戏 UI 的
                if (!sel.transform.root.name.StartsWith("LevelLib_", System.StringComparison.OrdinalIgnoreCase)) {
                    es.SetSelectedGameObject(null);
                }
            }
        } catch { }
    }

    /// <summary>界面外玩家信息框：开关控制创建/销毁，每帧同步位置/名字/头像</summary>
}
