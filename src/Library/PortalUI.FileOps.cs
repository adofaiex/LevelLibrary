// [split-of] PortalUI.cs lines 1938-2632
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LevelLibrary.Effects;

namespace LevelLibrary;

/// <summary>PortalUI 的谱面列表操作：导入 / 重命名 / 删除 / 进关 / 批量评级 / 数据清空确认。</summary>
public static partial class PortalUI {
    private static string _playStateLogged = "";   // 状态变化时才写日志，避免刷屏
    /// <summary>游戏真实的关卡状态名（由 `AutoFeatures` 的 ChangeState 补丁记录）。
    /// 初始 Start（放行）；退出游玩会回到 Start 所以也放行。</summary>
    public static string HookedState = "Start";
    /// <summary>artist 字段缓存（FilePath→artist）：滚动列表每行都读文件找 artist 会卡顿，读一次后缓存</summary>
    private static readonly Dictionary<string, string> _artistCache = new();
    /// <summary>文件存在性缓存（FilePath→bool）：滚动时每行 File.Exists 也是磁盘 IO，只在刷新/开关面板时清</summary>
    private static readonly Dictionary<string, bool> _existsCache = new();
    /// <summary>作者显示串缓存（FilePath→已去富文本并截断的文本）：避免滚动时每行跑正则</summary>
    private static readonly Dictionary<string, string> _authorDisplayCache = new();

    /// <summary>现在是不是在关卡里跑（含暂停 / 死亡 / 结算）。两条判据取或：
    /// 一、关卡控制器没在暂停 —— 没暂停就一定在跑，自动播放也算在跑。
    /// 二、状态机已经离开 Start —— 覆盖"暂停着但人还在关卡里"。
    /// 用途是「在关卡里就别打扰玩家」这一类判据，典型是屏保：暂停时它盖上来，
    /// 玩家回来看到的就是一页欢迎界面。
    /// 为什么不能比场景名：自制谱是在 scnEditor 里玩的，场景名分不出
    /// 「在打谱」和「在编辑器里闲着」。
    /// 为什么不能只看控制器那一组字段（翻游戏日志实测）：编辑器里闲着的时候
    /// 场景=scnEditor isGameWorld=True paused=True —— 控制器还在、isGameWorld 恒真、
    /// paused 也是真，光看它们判不出在不在跑（单用 isGameWorld 踩过同一个坑，
    /// 那个字段的反编译默认值就是 true）。
    /// 为什么不能只看补丁记的 HookedState：补丁挂在 ChangeState 上，
    /// 而某些退出路径下游戏是直接改状态字段的，补丁记不到 ——
    /// 实测日志里玩家已经退回编辑器闲着，HookedState 还停在 PlayerControl。
    /// 编辑器一进去就是暂停态（scrController.TogglePauseGame 里
    /// paused / audioPaused / timeScale 三件事绑在一起做），所以"暂停"这一项不能反过来用：
    /// 编辑器闲置和游玩暂停都是 paused=true，只能靠状态机分。
    /// 与 IsLevelPlaying 的关系：那个要求"没暂停且游戏世界已建立"，
    /// 是给"只在真正跑谱时才生效"的功能用的（禁用快捷键、游玩时隐藏）。</summary>
    public static bool IsLevelRunning() {
        var ctrl = AutoFeatures.GetCachedController();
        if (ctrl == null) return false;          // 控制器都不在，肯定没在跑
        bool paused = false;
        try { paused = ctrl.paused; } catch { }
        if (!paused) return true;                // 没暂停就是正在跑（自动播放走这条）
        string st = CurrentStateName();
        if (string.IsNullOrEmpty(st)) st = HookedState;   // 读不到状态机就退回补丁记的值
        return st != "Start";                    // 暂停中：状态回到 Start 才算回到编辑器闲置
    }

    /// <summary>状态机的当前状态名，直接读状态机自己的字段（真值）。
    /// 优先属性、次取字段、再按名字找一个含 state 的字段 —— 不同版本的 StateBehaviour
    /// 写法不一样，三条路只要有一条通就行。
    /// 反射结果缓存起来：屏保是常驻段每帧调它的，不能每帧反射。
    /// 读不到返回 null（调用方退回补丁记的值）。</summary>
    private static string CurrentStateName() {
        try {
            var ctrl = AutoFeatures.GetCachedController();
            if (ctrl == null) return null;
            if (_stateGetter == null && _stateField == null) {
                var t = typeof(MonsterLove.StateMachine.StateBehaviour);
                _stateGetter = HarmonyLib.AccessTools.PropertyGetter(t, "state");
                if (_stateGetter == null) _stateField = HarmonyLib.AccessTools.Field(t, "state");
                if (_stateField == null && _stateGetter == null) {
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public
                                                 | System.Reflection.BindingFlags.NonPublic
                                                 | System.Reflection.BindingFlags.Instance)) {
                        if (f.Name.IndexOf("state", System.StringComparison.OrdinalIgnoreCase) >= 0) { _stateField = f; break; }
                    }
                }
                Lg.Info("关卡状态读取：" + (_stateGetter != null ? "属性 state"
                    : (_stateField != null ? "字段 " + _stateField.Name + "" : "没找到 退回补丁记录值")));
            }
            object v = null;
            if (_stateGetter != null) v = _stateGetter.Invoke(ctrl, null);
            else if (_stateField != null) v = _stateField.GetValue(ctrl);
            return v == null ? null : v.ToString();
        } catch { return null; }
    }
    private static System.Reflection.MethodInfo _stateGetter;
    private static System.Reflection.FieldInfo _stateField;

    /// <summary>是否正在玩谱面（禁用快捷键 / 游玩时隐藏都用它）：`!scrController.paused && scrConductor.isGameWorld`。
    /// 自制谱是在 `scnEditor` 里玩的，所以判"在不在打这一关"一律用这个，别去比场景名 `scnGame`
    /// （那是官方关卡/主游戏那边）。死亡界面 paused=false 且 isGameWorld=true，同样算在玩。
    /// 需要「暂停也算在关卡里」时用 IsLevelRunning：本函数会把暂停排除掉。</summary>
    public static bool IsLevelPlaying() {
        try {
            // 用 AutoFeatures 里缓存的 controller（scrController.instance 是 FindObjectOfType，很贵）
            var ctrl = AutoFeatures.GetCachedController();
            var cond = GetCachedConductor();
            if (ctrl == null || cond == null) return false;
            bool paused = false;
            try { paused = ctrl.paused; } catch { }
            bool gw = cond.isGameWorld;
            if (LogVerbose) {
                string key = "p" + paused + "gw" + gw;
                if (key != _playStateLogged) {
                    _playStateLogged = key;
                    Lg.Info("IsLevelPlaying paused=" + paused + " isGameWorld=" + gw);
                }
            }
            return !paused && gw;
        } catch { return false; }
    }

    /// <summary>缓存的 scrConductor 引用（同样避免重复 FindObjectOfType）</summary>
    private static scrConductor _conductorCache;
    private static scrConductor GetCachedConductor() {
        try {
            var c = _conductorCache;
            if (c != null) return c;      // Unity 的 == 重载同时覆盖"为空"和"已销毁"
            _conductorCache = scrConductor.instance;
            return _conductorCache;
        } catch {
            _conductorCache = null;
            return null;
        }
    }

    private static string ReadLevelDuration(LevelEntry entry) {
        try {
            string folder = Path.GetDirectoryName(entry.FilePath);
            if (!Directory.Exists(folder)) return "";
            double maxDur = 0d;
            foreach (var af in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)) {
                string ext = Path.GetExtension(af).ToLowerInvariant();
                if (ext == ".ogg" || ext == ".wav" || ext == ".mp3") {
                    double d = ReadAudioDuration(af);
                    if (d > maxDur) maxDur = d;
                }
            }
            if (maxDur > 0d) { int totalSec = (int)System.Math.Round(maxDur); return (totalSec / 60) + ":" + (totalSec % 60).ToString("00"); }
        } catch { }
        return "";
    }

    /// <summary>取池行当前显示的谱面条目（点击时实时查，行被滚动复用了也不会错）</summary>
    private static LevelEntry CurrentEntry(PoolRow pool) {
        int idx = pool.LogicalIndex;
        if (idx < 0 || idx >= _logicalRows.Count) return null;
        return _logicalRows[idx].Entry;
    }

    /// <summary>在曲名前放一个标记小图标（28x28，保持宽高比）</summary>

    /// <summary>随机选一个谱面：锁定高亮 + 滚动到视口中间（不弹选择窗）</summary>

    /// <summary>滚动列表定位到指定谱面（按与 RefreshList 一致的行布局累计计算，含收藏区标头；centered=滚到视口中间）</summary>
    private static void ScrollToEntry(LevelEntry entry, bool centered = false) {
        var scroll = _libScroll;
        if (scroll == null || entry == null) return;
        float acc = 0f;
        bool favHeaderAdded = false;   // 收藏区标头只加一次
        string lastKey = null;
        foreach (var e in _display) {
            if (LevelScanner.IsFavorite(e.FolderPath)) {
                // 收藏区只有“收藏”一个标头，不加字母标头；高度必须用 HeaderRowH（56）——
                // 与 RefreshList 的行高累加同源，写死 48 会让定位永久偏 8px（本函数的老 bug）
                if (!favHeaderAdded) { acc += HeaderRowH; favHeaderAdded = true; }
            } else {
                // 正常区：字母标头
                string key = LevelScanner.GroupKey(e.Title);
                if (key != lastKey) { acc += HeaderRowH; lastKey = key; }
            }
            acc += ItemRowH;   // 必须与 RefreshList 的行高一致，写死数字会定位偏移
            if (ReferenceEquals(e, entry)) {
                Canvas.ForceUpdateCanvases();
                float maxY = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
                float targetY = acc - ItemRowH * 0.5f;
                if (centered && scroll.viewport != null) {
                    targetY -= scroll.viewport.rect.height * 0.5f;   // 滚到视口中间
                }
                targetY = Mathf.Clamp(targetY, 0f, maxY);
                scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, targetY);
                Lg.Info($"[LevelLib] 定位到谱面: {entry.Title} y={targetY:F0}");
                return;
            }
        }
    }

    // ---- 添加谱面 ----

    private static void ImportLevel() {
        // 先选命名规则，确认后才打开资源管理器
        ShowImportNameDialog(() => ImportLevelWithMode());
    }

private static void ImportLevelWithMode() {
        // 文件对话框同时接受谱面与压缩包（开关关时只显示 .adofai）。
        // 多格式必须每个都带 "*."（ShowDialog 靠 Contains("*") 判断是不是已写全的 spec，
        // 写 "adofai;zip" 会被拼成 "*.adofai;zip" —— 第二个没有星号，Windows 认不出来）。
        // 与既有的视频（*.mp4;*.mov…）/ 头像（*.png;*.jpg…）两处同口径。
        string filterExt = SingleImportZips ? "*.adofai;*.zip" : "adofai";
 string filterName = SingleImportZips ? "ADOFAI 谱面或压缩包" : "ADOFAI 谱面";
 string path = ModernFileDialog.OpenFile("选择谱面", filterName, filterExt);
        if (string.IsNullOrEmpty(path)) return;
        // 选到的是压缩包：先解压到系统临时目录，再把里面的谱面目录走下面同一套流程。
     // 解压目标必须在临时目录 —— 放进玩家的谱面目录树会被下一轮扫描扫到，
        // 而且后面"导入完删源目录"会去动玩家的目录（与批量导入 ScanZipsInto 同一口径）。
        if (SingleImportZips && path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase)) {
        ImportLevelFromZip(path);
   return;
        }
try {
   string folder = Path.GetDirectoryName(path);
            ImportLevelFolder(folder, path);
        } catch (System.Exception e) {
     Lg.Ex("导入谱面失败", e);
        }
    }

    /// <summary>压缩包那一路专用：判"落点是不是真的搬到别处去了"。
    /// `EnsureBackup` 有两种"没搬"的情况会退回源目录：BackupBase 为空、或它自己抛异常。
    /// 源目录是临时目录时这两种都要拦住 —— 条目指向一个马上会被删的路径，谱面就打不开了。
    /// 判据用"路径不在临时目录里"而不是比较字符串：两条路径的大小写/尾斜杠可能不同。</summary>
    private static bool LooksPersisted(string dest, string tempSource) {
        try {
            if (string.IsNullOrEmpty(dest)) return false;
            string d = Path.GetFullPath(dest).TrimEnd('\\', '/');
            string s = Path.GetFullPath(tempSource).TrimEnd('\\', '/');
       if (string.Equals(d, s, System.StringComparison.OrdinalIgnoreCase)) return false;
            // 双保险：落点还在系统临时目录里也不算数（那里随时会被清）
         return !d.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/'),
      System.StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    /// <summary>导入一个「装着 .adofai 的目录」：备份 → 查重 → 录入列表 → 定位。
    /// 普通导入与压缩包导入共用这一段（同一套判据，不分叉）。
    /// `srcFileName` 只是主文件找不到时的兜底，正常走 PickMainFile。
    /// `mustPersist` = 这个目录马上就会被删（压缩包那路的临时目录），那就必须先拷到持久位置。</summary>
private static bool ImportLevelFolder(string folder, string srcFileName, bool mustPersist = false) {
     try {
        if (string.IsNullOrEmpty(folder)) {
   Lg.Info("导入失败：拿不到目录");
        return false;
        }
string dirName = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(dirName)) dirName = Path.GetFileNameWithoutExtension(srcFileName);
   // 整个文件夹备份到 backup（和自动录入一致，不丢素材）；备份关 = 直接用原目录。
   // 压缩包那一路的源在系统临时目录里、导入完就会被删，这种情况下不能沿用
   // "备份关就直接用原目录"，否则条目指向的路径下一瞬就没了，谱面打不开
    //（真出过一次：日志里「已导入谱面 → 临时目录路径」紧跟着「已删除解压临时目录」）。
   // 所以这里把 mustPersist 折进判断：必须持久时，备份这一道不能省。
        string backupDir = (AutoFeatures.BackupOnImport || mustPersist)
         ? LevelScanner.EnsureBackup(folder, dirName) : folder;
        // EnsureBackup 在 BackupBase 没初始化时会退回 srcDir（原目录），mustPersist 就落空了，
        // 条目又指回临时目录。mustPersist 时这种"没搬到持久位置"的结果一律当失败，
        // 宁可不导入，也不能记一个打开不了的条目。
        if (mustPersist && !LooksPersisted(backupDir, folder)) {
     Lg.Info("导入失败：没能把谱面复制到持久位置（备份根未初始化？）"
    + "源=" + folder + " 落点=" + backupDir);
    ShowToast(Lang.Tr("导入失败：谱面没能保存到库目录"));
      return false;
        }
            if (backupDir == null) {
 Lg.Info("导入失败：备份副本创建失败");
         return false;
   }
         // 同一个文件夹已在列表里就提示，不重复添加
            if (LevelScanner.Entries.Any(x => string.Equals(x.FolderPath, backupDir, System.StringComparison.OrdinalIgnoreCase))) {
   PortalUI.ShowToast(Lang.Tr("谱面重复：") + dirName);
         Lg.Info($"[LevelLib] 导入跳过（重复）：{backupDir}");
          return false;
            }
        string main = LevelScanner.PickMainFile(backupDir) ?? Path.Combine(backupDir, Path.GetFileName(srcFileName));
     string title = TitleByMode(backupDir);   // 按选定的命名规则起名
            var entry = new LevelEntry {
Title = title,
    OggName = title,
         FolderPath = backupDir,
FilePath = main,
 };
  LevelScanner.AddRecorded(backupDir);   // 立即写盘：重启后仍恢复在列表
            // 记一笔「新导入」：列表里给一个提示色，玩家点开过就摘掉（见 SelectRow）
            LevelScanner.MarkNewImport(backupDir);
            LevelScanner.Entries.Add(entry);
LevelScanner.SortEntries();
  RefreshList();
  ScrollToEntry(entry);   // 定位到新添加的谱面
      Lg.Info($"[LevelLib] 已导入谱面(整文件夹): {backupDir}");
   return true;
        } catch (System.Exception e) {
        Lg.Ex("导入谱面失败", e);
            return false;
        }
    }

    /// <summary>导入一个 .zip：解压到系统临时目录，把里面所有含 .adofai 的目录逐个导入，最后删临时目录。
    /// 压缩包本身不动（删的是玩家的原始素材，不能碰）。
    /// 一个包里通常只有一个谱面，但也有把好几张谱打成一包的，所以按目录逐个导、不假设只有一张。</summary>
    /// <summary>解压一个 zip，文件名的解码按 UTF-8 严格 → GBK 两级兜底。
    /// 为什么要自己解而不用 `ZipFile.ExtractToDirectory`：
    ///   .NET 读zip 时按条目头的 flag bit 11（0x800）决定"文件名是不是 UTF-8"。
 ///   国内大量 zip（尤其是 Windows 资源管理器"压缩为 ZIP 文件"打出来的）没有置这一位，
    ///   .NET 就一律按 CP437 解码，中文目录名全变成乱码（实测过：日志里出现过
    ///   `已导入谱面 → backup\����è`，玩家看到的就是那个名字）。
    ///   字节本身其实是 UTF-8（Python 能正确读出"人是猫"），只是标志位没置。
/// 做法：自己读中央目录拿原始文件名字节（严格 UTF-8，解不出就 GBK），
    /// 数据块交给 .NET 的 DeflateStream（标准算法，与文件名无关）。
    /// destDir 必须是新建的空目录；相对路径做了穿越防护（`..`、盘符、绝对路径一律去掉）。
    /// </summary>
    private static void ExtractZipWithGbkNames(string zipPath, string destDir) {
        byte[] all;
      using (var fs = File.OpenRead(zipPath)) {
    all = new byte[fs.Length];
    int got = 0;
         while (got < all.Length) {
 int n = fs.Read(all, got, all.Length - got);
      if (n <= 0) break;
   got += n;
      }
     }
        // 中央目录末尾的 EOCD（End Of Central Directory），它前面最多 65535+22 字节
        int eocd = -1;
        int limit = System.Math.Max(0, all.Length - 65557);
        for (int i = all.Length - 22; i >= limit; i--) {
     if (all[i] == 0x50 && all[i + 1] == 0x4B && all[i + 2] == 0x05 && all[i + 3] == 0x06) {
     eocd = i;
   break;
  }
        }
        if (eocd < 0) throw new System.IO.InvalidDataException("不是有效的 zip（找不到中央目录）");

        int count = System.BitConverter.ToUInt16(all, eocd + 10);
   long cdOffset = System.BitConverter.ToUInt32(all, eocd + 16);
      int p = (int)cdOffset;

        for (int n = 0; n < count; n++) {
    if (p + 46 > all.Length) break;
            if (!(all[p] == 0x50 && all[p + 1] == 0x4B && all[p + 2] == 0x01 && all[p + 3] == 0x02)) break;
       ushort nameLen = System.BitConverter.ToUInt16(all, p + 28);
            ushort extraLen = System.BitConverter.ToUInt16(all, p + 30);
        ushort cmtLen = System.BitConverter.ToUInt16(all, p + 32);
      // 压缩大小与本地头偏移都从中央目录读：本地头里那两份 size 在"数据描述符"模式下是 0
    //（真 zip 常常如此），只读本地头会一个条目都解不出来。
            long compSize = System.BitConverter.ToUInt32(all, p + 20);
   int localOffset = (int)System.BitConverter.ToUInt32(all, p + 42);
         int nameStart = p + 46;
            if (nameStart + nameLen > all.Length) break;
            string entryName = DecodeZipEntryName(all, nameStart, nameLen);

          bool isDir = entryName.EndsWith("/", System.StringComparison.Ordinal)
          || entryName.EndsWith("\\", System.StringComparison.Ordinal);
          string safeName = MakeSafeRelativePath(entryName);
if (string.IsNullOrEmpty(safeName)) {
    p = nameStart + nameLen + extraLen + cmtLen;
      continue;
      }
        string outPath = Path.Combine(destDir, safeName.Replace('/', Path.DirectorySeparatorChar));
        if (isDir) {
        try { Directory.CreateDirectory(outPath); } catch { }
} else {
        try {
         string dir = Path.GetDirectoryName(outPath);
              if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            ExtractZipEntry(all, localOffset, outPath, compSize);
   } catch { }
            }
    p = nameStart + nameLen + extraLen + cmtLen;
  }
    }

    /// <summary>中央目录里的文件名字节 → 字符串：先严格 UTF-8，解不出（含替换字符）再 GBK，最后宽松 UTF-8 兜底。
    /// 不看 flag bit11：没置位的那些字节本身仍是 UTF-8，只是 .NET 不认。</summary>
    private static string DecodeZipEntryName(byte[] all, int start, int len) {
        if (len <= 0) return "";
      try {
 var strictUtf8 = new System.Text.UTF8Encoding(false, true);   // 非法字节抛异常 = 等价于严格模式
            string s = strictUtf8.GetString(all, start, len);
        if (s.IndexOf('\uFFFD') < 0) return s;       // 解出来了且没有替换字符，说明原文就是 UTF-8
        } catch { }
        try {
     return System.Text.Encoding.GetEncoding(936).GetString(all, start, len);   // 936 = GBK
        } catch { }
        try { return System.Text.Encoding.UTF8.GetString(all, start, len); } catch { }
        return "";
    }

    /// <summary>把 zip 条目的相对路径做成安全的：去掉盘符与根、丢掉 `.` / `..` 段（目录穿越防护）。</summary>
    private static string MakeSafeRelativePath(string entryName) {
        try {
     string s = entryName.Replace('\\', '/').Trim();
    if (s.Length == 0) return "";
            if (s.Length >= 2 && s[1] == ':') s = s.Substring(2);   // 去盘符
    s = s.TrimStart('/');                 // 去根
            var parts = new System.Collections.Generic.List<string>();
            foreach (var seg in s.Split('/')) {
       if (seg == "." || seg == "..") continue;
                if (seg.Length > 0) parts.Add(seg);
       }
         return string.Join("/", parts.ToArray());
        } catch { return ""; }
    }

    /// <summary>按本地文件头定位数据区，用 DeflateStream 解压到目标文件。
    /// `compSize` 由调用方从中央目录传入 —— 本地头里那两份 size 在"数据描述符"模式下是 0。</summary>
    private static void ExtractZipEntry(byte[] all, int localOffset, string outPath, long compSize) {
        if (localOffset + 30 > all.Length) return;
   if (!(all[localOffset] == 0x50 && all[localOffset + 1] == 0x4B
          && all[localOffset + 2] == 0x03 && all[localOffset + 3] == 0x04)) return;
        ushort nameLen = System.BitConverter.ToUInt16(all, localOffset + 26);
        ushort extraLen = System.BitConverter.ToUInt16(all, localOffset + 28);
      int dataStart = localOffset + 30 + nameLen + extraLen;
        ushort method = System.BitConverter.ToUInt16(all, localOffset + 8);
     if (compSize <= 0 || dataStart + compSize > all.Length) return;
        if (method == 0) {
        // Stored：不压缩，直接拷字节
    using (var fs = File.Create(outPath)) fs.Write(all, dataStart, (int)compSize);
          return;
        }
      if (method != 8) return;    // 只支持 Stored(0) 与 Deflate(8)
    var comp = new System.IO.MemoryStream(all, dataStart, (int)compSize, false);
        using (var inf = new System.IO.Compression.DeflateStream(comp, System.IO.Compression.CompressionMode.Decompress))
        using (var fs = File.Create(outPath)) {
            var buf = new byte[16384];
   int got;
            while ((got = inf.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, got);
     }
    }

    private static void ImportLevelFromZip(string zipPath) {
  string tempRoot = null;
  int okCount = 0;
        try {
            string zipName = SanitizeFileName(Path.GetFileNameWithoutExtension(zipPath));
      if (string.IsNullOrEmpty(zipName)) zipName = "zip";
    tempRoot = Path.Combine(Path.GetTempPath(), "LevelLib_1zip_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempRoot);
        // 用自己那套解压（ExtractZipWithGbkNames），不用 ZipFile.ExtractToDirectory：
        // 后者按条目头的 UTF-8 标志位解码文件名，国内大量 zip 没置那一位 ，所以 中文目录名全变乱码。
        ExtractZipWithGbkNames(zipPath, tempRoot);

  // 解压结果里找含 .adofai 的目录：可能是 tempRoot 本身，也可能是子目录（谱面常被打成一层文件夹）
 var dirs = new System.Collections.Generic.List<string>();
       try {
        if (Directory.GetFiles(tempRoot, "*.adofai").Length > 0) dirs.Add(tempRoot);
        foreach (var d in Directory.GetDirectories(tempRoot, "*", SearchOption.AllDirectories)) {
     try {
        if (Directory.GetFiles(d, "*.adofai").Length > 0) dirs.Add(d);
         } catch { }
    }
            } catch { }

   if (dirs.Count == 0) {
                ShowToast(Lang.Tr("这个压缩包里没找到 .adofai 谱面"));
      Lg.Info("单个导入：压缩包里没有 .adofai —— " + zipPath);
 return;
            }

for (int i = 0; i < dirs.Count; i++) {
            try {
       // true = 这个源目录马上就要被删（finally 里删临时目录），必须先拷到 backup，不能直接把条目指向它
  if (ImportLevelFolder(dirs[i], null, true)) okCount++;
 } catch (System.Exception e) {
     Lg.Ex("单个导入：从压缩包导入失败 " + dirs[i], e);
      }
        }
        if (okCount > 0) ShowToast($"已从压缩包导入 {okCount} 个谱面");
      Lg.Info($"[LevelLib] 单个导入：压缩包 {zipPath} → 导入 {okCount}/{dirs.Count} 个");
   } catch (System.Exception e) {
       ShowToast(Lang.Tr("压缩包解压失败"));
            Lg.Ex("单个导入：解压失败 " + zipPath, e);
        } finally {
    // 无论成败都删临时目录（压缩包本身不动 —— 那是玩家的原始素材）
       // finally 里不能 return（CS0157 控制流不能从 finally 子句中离开），所以用块把那句包起来。
  if (!string.IsNullOrEmpty(tempRoot)) {
    try {
        if (Directory.Exists(tempRoot)) { Directory.Delete(tempRoot, true); Lg.Info("单个导入：已删除解压临时目录 " + tempRoot); }
    } catch (System.Exception e) {
           Lg.Ex("单个导入：删除解压临时目录失败 " + tempRoot, e);
      }
   }
        }
    }

    // ---- 重命名 ----

    private static void ShowRenameDialog(LevelEntry entry) {
        if (_renameCanvas != null) return;
        _renameEntry = entry;
        _renameText = entry.Title;
        _renameCanvas = MakeCanvas("LevelLib_Rename", 82000);
        var bg = MakeDialogVeil(_renameCanvas.transform);

        var box = MakeDialogBox(_renameCanvas.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(640f, 220f);

        var label = MakeTitle(box.transform, "Label", 34, Color.white, TextAnchor.MiddleCenter);
        label.rectTransform.anchorMin = new Vector2(0f, 0.72f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.text = Lang.Tr("重命名谱面");

        var inputBg = MakeImage(box.transform, "InputBg", ItemColor);
        inputBg.rectTransform.anchorMin = new Vector2(0.08f, 0.42f);
        inputBg.rectTransform.anchorMax = new Vector2(0.92f, 0.66f);
        inputBg.rectTransform.offsetMin = Vector2.zero;
        inputBg.rectTransform.offsetMax = Vector2.zero;
        var input = inputBg.gameObject.AddComponent<InputField>();
        var inputText = MakeText(inputBg.transform, "InputText", 34, Color.white, TextAnchor.MiddleLeft);
        Stretch(inputText.rectTransform, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        input.textComponent = inputText;
        input.text = _renameText;
        input.onValueChanged.AddListener(v => _renameText = v);

        var ok = MakeButton(box.transform, "Ok", new Vector2(130f, -70f), new Vector2(180f, 44f), Accent);
        var okText = MakeText(ok.transform, "确定", 30, Color.white, TextAnchor.MiddleCenter);
        Stretch(okText.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => ConfirmRename());

        var cancel = MakeButton(box.transform, "Cancel", new Vector2(-130f, -70f), new Vector2(140f, 44f), ItemColor);
        var cancelText = MakeText(cancel.transform, "取消", 26, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelText.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => CloseRenameDialog());
    }

    private static void CloseRenameDialog() {
        if (_renameCanvas != null) Object.Destroy(_renameCanvas.gameObject);
        _renameCanvas = null;
        _renameEntry = null;
    }

    private static void ConfirmRename() {
        if (_renameEntry == null) return;
        string newName = _renameText == null ? "" : _renameText.Trim();
        if (newName.Length == 0) return;
        if (string.Equals(newName, _renameEntry.Title, System.StringComparison.Ordinal)) {
            CloseRenameDialog();
            return;
        }
        // 只改 mod 内显示名，不动文件夹名（titles.txt 持久化，扫描后也不会被覆盖）
        LevelScanner.SetTitleOverride(_renameEntry.FilePath, newName);
        _renameEntry.Title = newName;
        LevelScanner.SortEntries();
        CloseRenameDialog();
        RefreshList();
        Lg.Info($"[LevelLib] 已重命名（仅显示） -> {newName}");
    }

    // ---- 删除 ----

    private static void CloseDeleteDialog() {
        if (_deleteCanvas != null) Object.Destroy(_deleteCanvas.gameObject);
        _deleteCanvas = null;
        _deleteEntry = null;
    }

    private static void ConfirmDelete() {
        if (_deleteEntry == null) return;
        try {
            // 只删我们自己的副本：没开导入备份时条目指向玩家的原文件夹，那里只从列表移除
            bool wipeFiles = _deleteFiles && LevelScanner.IsOwnCopy(_deleteEntry.FolderPath);
            bool keptFiles = _deleteFiles && !wipeFiles;
            if (wipeFiles) {
                bool imported = string.Equals(_deleteEntry.FolderPath, Main.ImportDir, System.StringComparison.OrdinalIgnoreCase);
                if (imported) {
                    // 导入的谱面：删文件（顺手清空目录）
                    if (File.Exists(_deleteEntry.FilePath)) File.Delete(_deleteEntry.FilePath);
                    try { if (Directory.Exists(_deleteEntry.FolderPath) && Directory.GetFiles(_deleteEntry.FolderPath).Length == 0) Directory.Delete(_deleteEntry.FolderPath); } catch (System.Exception e) { ReportErrorQuiet("删除后清理空目录失败", e); }
                } else {
                    // D:\task 的谱面：删整个文件夹
                    if (Directory.Exists(_deleteEntry.FolderPath)) Directory.Delete(_deleteEntry.FolderPath, true);
                }
            }
            LevelScanner.Entries.Remove(_deleteEntry);
            MarkStore.Remove(_deleteEntry.FilePath);
            LevelScanner.RemoveTitleOverride(_deleteEntry.FilePath);
            LevelScanner.RemoveRecorded(_deleteEntry.FolderPath);
            LevelScanner.ClearNewImport(_deleteEntry.FolderPath);   // 别让已删谱面的标记留在 newmarks.txt 里
            // 删掉的正好是当前选中项 → 左侧详情区必须立刻清空。
            // 注意：_selectedPath 存的是"文件夹路径"，但下面几处比较用的是"文件路径"，
            // 只按文件路径判断会漏掉，所以两种都判一次。
            string delFolder = _deleteEntry.FolderPath;
            string delFile = _deleteEntry.FilePath;
            string delTitle = _deleteEntry.Title;
            bool wasSelected = string.Equals(_selectedPath, delFolder, System.StringComparison.OrdinalIgnoreCase)
                            || string.Equals(_selectedPath, delFile, System.StringComparison.OrdinalIgnoreCase);
            CloseDeleteDialog();
            if (wasSelected) {
                _selectedPath = null;
                _detailEntry = null;
                _detailPickedFile = null;
                CloseLevelPickDialog();      // 顺带收掉可能开着的关卡选择弹窗
                ApplyDetailEmptyState();     // 左侧切回"未选中"提示（清掉旧谱面的信息）
                ShowToast(keptFiles ? "已从列表移除：这个谱面没有备份副本，文件留在原位置" : "谱面已删除");
            }
            RefreshList();
            Lg.Info($"[LevelLib] 已删除谱面: {delTitle}（原本选中={wasSelected}）"
                + (keptFiles ? "，原位置文件保留" : ""));
        } catch (System.Exception e) {
            Lg.Ex("删除谱面失败", e);
        }
    }

    // ---- 打开位置 ----

    private static void OpenInExplorer(LevelEntry entry) {
        try {
            Process.Start("explorer.exe", $"/select,\"{entry.FilePath}\"");
        } catch (System.Exception e) {
            Lg.Ex("打开位置失败", e);
        }
    }

    /// <summary>比较两个谱面路径是不是"同一张谱"。游戏读进来后可能被规范化（分隔符/大小写/相对路径），
    /// 所以不要求字符串完全相等：文件名 + 父目录名都对上就算同一张。</summary>
    private static bool SameLevelPath(string a, string b) {
        try {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string na = Path.GetFileName(a), nb = Path.GetFileName(b);
            if (!string.Equals(na, nb, System.StringComparison.OrdinalIgnoreCase)) return false;
            string da = Path.GetFileName(Path.GetDirectoryName(a) ?? "");
            string db = Path.GetFileName(Path.GetDirectoryName(b) ?? "");
            return string.Equals(da, db, System.StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    // ---- 确认进入 ----

    private static void ConfirmOpen(string adofaiPath) {
        if (string.IsNullOrEmpty(adofaiPath)) return;
        if (!File.Exists(adofaiPath)) {
            ShowMissingDialog(adofaiPath);
            return;
        }
        _pendingOpenPath = adofaiPath;
        _achDailyCheckinWait = 5f;   // 每日成就：打卡！打开编辑器5秒后触发
        Achievements.TryUnlock("ach_palm");   // 成就：首次打开谱面进编辑器（掌中宇宙）
        // 成就「兴酣落笔摇五岳」要判“这一局是不是这个谱面的第一次玩”：
        // 必须在 `AddPlay` 之前记 —— 它看的就是“进它之前的游玩次数是不是 0”
        // （打开 / 编辑器就绪 / 通关 后面还会各 +1，都不影响这份快照）
        // force=true：从库里进 = 玩家明确的一次新进入，同一个谱面第二次进来要重新判。
        AutoFeatures.NoteEntryPlays(Path.GetDirectoryName(adofaiPath), true);
        LevelScanner.AddPlay(Path.GetDirectoryName(adofaiPath));   // 游玩次数 +1（立即写盘）
        AutoFeatures.EnsureRecorded(adofaiPath);   // 咱自己知道路径：先确保录入，不依赖游戏字段
        // 去特效：把"这张谱面该用什么设置"交给注入端。必须在一切早退分支之前 ——
        // 之前放在编辑器内切换那段 return 之后，编辑器里连开第二张谱面时 Pending 停在上一次的值，
        // 没配单谱面去特效的谱面既吃不到全局、还可能串到上一张的配置（用户报过）。
        EffectInjector.Pending = EffectStore.ResolveFor(LevelScanner.ToRel(System.IO.Path.GetDirectoryName(adofaiPath)));
        // 已在编辑器场景：直接切换谱面，不重载场景（保留编辑器状态，秒开）
        if (SceneManager.GetActiveScene().name == "scnEditor") {
            var editor = Object.FindFirstObjectByType<scnEditor>();
            if (editor != null && editor.customLevel != null && editor.customLevel.levelMaker != null) {
                editor.OpenLevel(adofaiPath);
                ClosePanel();
                CloseRenameDialog();
                Lg.Info($"[LevelLib] 编辑器内直接切谱面: {adofaiPath}");
                return;
            }
        }
        // 不在编辑器：切场景后载入。
        // 先告诉编辑器“载入时要打开哪个谱面”（scnEditor.levelToOpenOnLoad）：
        // 这样 scnEditor.Start 会直接把谱面读起来，不用“先开空编辑器 → 轮询等它就绪 → 再调 OpenLevel”
        // 那三步（旧做法要等 1.2 秒以上）；剩余耗时全是游戏重建编辑器场景的固有开销。
        // 先把看门狗状态清干净：
        // 上一次“载入放弃”或中途切场景留下的 `_editorLoading=true` / 旧 `_expectedOpenPath`
        // 会让这一次的确认从一开始就是错的（“停在空编辑器里”就是这么来的）。
        _editorLoading = false;
        _expectedOpenPath = null;
        _pendingOpenPath = adofaiPath;      // 先记上：兜底重试时要用
        _editorLoading = true;
        _editorWaitTimer = 3f;   // 收到入成功为止最多等 3 秒（超时就回退成旧的 OpenLevel 调用）
        _expectedOpenPath = adofaiPath;   // 记下目标谱面：判定"是否真的载入了它"
        // 给「深度用户」记一笔“这次是谱面库打开的”：
        // 编辑器真打开时由 AutoFeatures 的两个补丁对账，对不上就说明是游戏自己打开的。
        DeepUser.NoteLibOpen(adofaiPath);
        _editorReloadTries = 0;
    // （Pending 的发布已挪到上面的早退分支之前 —— 这里原来那行删掉了）
        try {
            // 它是静态字段，游戏消费后不一定清 → 先清再设
            scnEditor.levelToOpenOnLoad = null;
            scnEditor.levelToOpenOnLoad = adofaiPath;
            Lg.Info($"[LevelLib] 进入编辑器：预设待打开谱面 {adofaiPath}");
        } catch (System.Exception e) {
            Lg.Ex("预设待打开谱面失败（将回退到旧流程）", e);
        }
        ClosePanel();
        CloseRenameDialog();
        SceneManager.LoadScene("scnEditor");
    }

    // ---- 选择关卡文件（文件夹内所有 .adofai）----

    /// <summary>自动评级指定谱面（无等级标记且落在 P 档才写）。
    /// 入口在行的右键菜单里，菜单只在"没有等级标记"时才显示这一项。</summary>
    private static void AutoRateEntry(LevelEntry e) {
        if (e == null) { ShowToast(Lang.Tr("请先选中一个谱面喵")); return; }
        try {
            if (!string.IsNullOrEmpty(MarkStore.Get(e.FilePath))) { ShowToast(Lang.Tr("已有等级标记喵")); return; }
            string file = LevelScanner.PickMainFile(e.FolderPath);
            if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file)) { ShowToast(Lang.Tr("找不到谱面文件喵")); return; }
            string pgu = AutoRater.RateChart(GetLevelText(file), out double star, 100.0, System.IO.Path.GetFileName(file));
            if (IsValidPgu(pgu)) {
                MarkStore.Set(e.FilePath, pgu);
                MarkStore.MarkAutoRated(e.FilePath);   // 记录自动评级标过
                MarkStore.Save();
                RefreshList();
                if (_detailEntry == e) RefreshDetailInfo(e);   // 选中的就是它 → 顺带刷新详情（标记图标）
                try { Achievements.RebuildMilestones(false); } catch (System.Exception) { }   // 里程碑与标记强绑定
                ShowToast($"已自动评级：{pgu}（star {star:F2}）喵");
            } else {
                ShowToast(Lang.Tr("该谱面暂不符合自动评级喵"));
            }
        } catch (System.Exception ex) { Lg.Ex("自动评级失败", ex); ShowToast("自动评级失败喵"); }
    }

    /// <summary>是否有效的 P/G/U 标记（P/G/U + 数字，排除 "?"）</summary>
    private static bool IsValidPgu(string pgu) {
        if (string.IsNullOrEmpty(pgu) || pgu.Length < 2) return false;
        if (pgu[0] != 'P' && pgu[0] != 'G' && pgu[0] != 'U') return false;
        for (int i = 1; i < pgu.Length; i++) if (!char.IsDigit(pgu[i])) return false;
        return true;
    }

    /// <summary>批量进度条：惰性创建一次。挂在独立 Canvas 上（层级高于面板），
    /// 这样在设置页触发也能看见。</summary>
    private static Canvas _batchProgCanvas;
    private static void EnsureBatchProgUI() {
        if (_batchProgCanvas != null) return;
        try {
            _batchProgCanvas = MakeCanvas("LevelLib_BatchProgress", 91500);
            var cover = MakeDialogVeil(_batchProgCanvas.transform);

            var box = MakeDialogBox(_batchProgCanvas.transform, "Box", new Color(0.13f, 0.15f, 0.22f, 0.97f));
            box.sprite = RoundedSprite;
            box.type = Image.Type.Sliced;
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(640f, 150f);

            _batchProgText = MakeText(box.transform, "T", 26, new Color(1f, 0.85f, 0.45f, 1f), TextAnchor.MiddleCenter);
            _batchProgText.text = Lang.Tr("评级中…");
            var trt = _batchProgText.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.56f);
            trt.anchorMax = new Vector2(1f, 0.96f);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            // 进度条背景 + 填充（不用 Slider 三件套，直接改 anchorMax）
            _batchProgBar = MakeImage(box.transform, "BarBg", new Color(0.08f, 0.09f, 0.13f, 1f));
            var brrt = _batchProgBar.rectTransform;
            brrt.anchorMin = new Vector2(0.08f, 0.22f);
            brrt.anchorMax = new Vector2(0.92f, 0.44f);
            brrt.offsetMin = Vector2.zero; brrt.offsetMax = Vector2.zero;

            _batchProgFill = MakeImage(_batchProgBar.transform, "Fill", new Color(0.30f, 0.62f, 0.45f, 1f));
            var frt = _batchProgFill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            frt.sizeDelta = Vector2.zero;

            _batchProgBar.gameObject.SetActive(false);
            _batchProgText.gameObject.SetActive(false);
            Lg.Info("批量进度条已创建（此前从未接线，属于修复）");
        } catch (System.Exception e) {
            ReportErrorQuiet("创建批量进度条失败", e);
        }
    }

    /// <summary>批量进度条显隐。必须开关整个画布：只关里面的进度条和文字的话，
    /// 画布本身和那个深色底 Box 一直激活，跑完会在屏幕中间留下一个关不掉的深蓝方块。</summary>
    private static void SetBatchProgVisible(bool on) {
        if (_batchProgCanvas != null) _batchProgCanvas.gameObject.SetActive(on);
        if (_batchProgBar != null) _batchProgBar.gameObject.SetActive(on);
        if (_batchProgText != null) _batchProgText.gameObject.SetActive(on);
    }


    /// <summary>批量评级完成提示：只弹 Toast，不弹结果窗口 —— 独立画布做结果窗有层级问题
    /// （sortingOrder 读回来不是设的值），会被面板画布压住，表现成"屏幕中间一块关不掉的蓝色"。</summary>
    private static void ShowBatchResult(List<KeyValuePair<string, string>> marked) {
        // 收尾时显式存一次兜底：MarkStore.Set 本身每次都写，但"跑完到玩家退出"之间
        // 万一有别的写把它盖掉 / 没落盘，这一下能救回来
        try {
            MarkStore.Save();
            Lg.Info("批量评级：已显式保存标记（marks.txt）");
        } catch (System.Exception e) { Lg.Ex("批量评级保存标记失败", e); }
        if (marked == null || marked.Count == 0) {
            ShowToast(Lang.Tr("评级完毕：没有可评级的谱面喵"));
            Lg.Info("批量评级完成：没有可评级的谱面");
        } else {
            ShowToast($"评级完毕：共标记 {marked.Count} 个谱面喵");
            Lg.Info($"[LevelLib] 批量评级完成：标记 {marked.Count} 个谱面");
        }
        // 排查用：列出所有活着的画布，看残留的"蓝色块"是谁
        LogAllCanvases("批量评级结束时");
    }

    /// <summary>排查用：列出当前所有 Canvas（名字/排序/子节点数/是否激活），定位残留画布</summary>
    private static void LogAllCanvases(string when) {
        try {
            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Lg.Info($"[LevelLib] 【画布排查】{when}：共 {all.Length} 个画布");
            for (int i = 0; i < all.Length; i++) {
                var c = all[i];
                if (c == null) continue;
                string n = c.gameObject.name;
                bool mine = n.StartsWith("LevelLib_", System.StringComparison.OrdinalIgnoreCase);
                var img = c.GetComponent<Image>();
                Lg.Info($"[LevelLib] 【画布排查】  {(mine ? "★" : " ")} {n} 排序={c.sortingOrder}"
                    + $" 子节点={c.transform.childCount} 激活={c.gameObject.activeInHierarchy}"
                    + $" 同级序={c.transform.GetSiblingIndex()}/{((c.transform.parent != null) ? c.transform.parent.childCount - 1 : -1)}"
                    + $" 根={((c.transform.root != null) ? c.transform.root.name : "?")}");
            }
        } catch (System.Exception e) {
            Lg.Ex("画布排查失败", e);
        }
    }
    /// <summary>导出手动标记数据：对所有有标记的谱面评难度 star 并导出到桌面</summary>
    private static void StartExportMarks() {
        EnsureBatchProgUI();
        if (_batchProgFill != null) _batchProgFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        if (_batchProgText != null) _batchProgText.text = Lang.Tr("导出中…");
        SetBatchProgVisible(true);
        var host = new GameObject("__ExportMarksHost");
        var runner = host.AddComponent<BatchRateRunner>();
        runner.Mode = "export";
        runner.Begin();
    }

    /// <summary>批量评级协程宿主（每 3 个谱面 yield 一次，保持 UI 响应）</summary>
    private class BatchRateRunner : MonoBehaviour {
        public string Mode = "rate";   // rate=批量评级 export=导出手动标记
        private readonly List<KeyValuePair<string, string>> _marked = new List<KeyValuePair<string, string>>();
        public void Begin() { StartCoroutine(Run()); }
        private System.Collections.IEnumerator Run() {
            if (Mode == "export") {
                var all = LevelScanner.Entries;
                var targets = new List<LevelEntry>();
                if (all != null) {
                    foreach (var e in all) {
                        if (IsValidPgu(MarkStore.Get(e.FilePath))) targets.Add(e);   // 只导出手动标了 P/G/U 的
                    }
                }
                int total = targets.Count;
                int done = 0;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("标记\t难度star");
                if (total > 0) {
                    foreach (var e in targets) {
                        try {
                            string mark = MarkStore.Get(e.FilePath);
                            string file = LevelScanner.PickMainFile(e.FolderPath);
                            if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file)) {
                                string pgu = AutoRater.RateChart(GetLevelText(file), out double star, 100.0, System.IO.Path.GetFileName(file));
                                sb.AppendLine(mark + "\t" + star.ToString("F2"));
                            }
                        } catch { }
                        done++;
                        if (_batchProgFill != null) _batchProgFill.rectTransform.anchorMax = new Vector2(total > 0 ? (float)done / total : 0f, 1f);
                        if (_batchProgText != null) _batchProgText.text = Lang.TrFmt("导出中 {0}/{1}", done, total);
                        if (done % 3 == 0) yield return null;
                    }
                }
                string path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "手动标记数据.txt");
                try { System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8); }
                catch (System.Exception ex) { Lg.Ex("导出手动标记失败", ex); }
                SetBatchProgVisible(false);
                ShowToast(Lang.Tr("已导出到桌面\\手动标记数据.txt 喵"));
            } else {
                var all = LevelScanner.Entries;
                var targets = new List<LevelEntry>();
                if (all != null) {
                    foreach (var e in all) {
                        if (string.IsNullOrEmpty(MarkStore.Get(e.FilePath))) targets.Add(e);   // 只扫无等级标记的
                    }
                }
                int total = targets.Count;
                int done = 0;
                if (total > 0) {
                    foreach (var e in targets) {
                        try {
                            string file = LevelScanner.PickMainFile(e.FolderPath);
                            if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file)) {
                                string pgu = AutoRater.RateChart(GetLevelText(file), out double star, 100.0, System.IO.Path.GetFileName(file));
                                if (IsValidPgu(pgu)) {
                                    MarkStore.Set(e.FilePath, pgu);
                                    MarkStore.MarkAutoRated(e.FilePath);   // 记录"自动评级标过"
                                    string nm = string.IsNullOrEmpty(e.Title) ? new System.IO.DirectoryInfo(e.FolderPath).Name : e.Title;
                                    _marked.Add(new KeyValuePair<string, string>(nm, pgu));
                                }
                            }
                        } catch { }
                        done++;
                        if (_batchProgFill != null) _batchProgFill.rectTransform.anchorMax = new Vector2(total > 0 ? (float)done / total : 0f, 1f);
                        if (_batchProgText != null) _batchProgText.text = Lang.TrFmt("评级中 {0}/{1}", done, total);
                        if (done % 3 == 0) yield return null;
                    }
                }
                MarkStore.Save();
                try { Achievements.RebuildMilestones(false); } catch (System.Exception) { }   // 批量评级后同样重算里程碑
                RefreshList();
                SetBatchProgVisible(false);
                ShowBatchResult(_marked);
            }
            Object.Destroy(gameObject);
        }
    }

    /// <summary>数据清空确认弹窗。`okLabel` 是确认键上的字，默认"确定清空"；
    /// 动作只在点了确认之后跑。</summary>
    private static void ShowDataClearConfirm(string titleText, string msgText, System.Action onOk, string okLabel = "确定清空") {
        if (_dataConfirmWin != null) return;
        _dataConfirmWin = MakeCanvas("LevelLib_DataClear", 83600);
        var bg = MakeDialogVeil(_dataConfirmWin.transform);
        var box = MakeDialogBox(_dataConfirmWin.transform, "Box", PanelColor);
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(520f, 260f);
        var t = MakeTitle(box.transform, "T", 30, new Color(1f, 0.4f, 0.4f, 1f), TextAnchor.MiddleCenter);
        t.text = titleText;
        t.fontStyle = FontStyle.Bold;
        t.rectTransform.anchorMin = new Vector2(0f, 0.72f); t.rectTransform.anchorMax = new Vector2(1f, 0.95f);
        t.rectTransform.offsetMin = Vector2.zero; t.rectTransform.offsetMax = Vector2.zero;
        var m = MakeText(box.transform, "M", 22, Color.white, TextAnchor.MiddleCenter);
        m.text = msgText;
        m.rectTransform.anchorMin = new Vector2(0.05f, 0.30f); m.rectTransform.anchorMax = new Vector2(0.95f, 0.66f);
        m.rectTransform.offsetMin = Vector2.zero; m.rectTransform.offsetMax = Vector2.zero;
        var ok = MakeButton(box.transform, "Ok", new Vector2(80f, -85f), new Vector2(140f, 44f), new Color(0.55f, 0.22f, 0.22f, 1f));
        var okT = MakeText(ok.transform, "OkT", 22, Color.white, TextAnchor.MiddleCenter);
        okT.text = okLabel;
        Stretch(okT.rectTransform, Vector2.zero, Vector2.one);
        ok.onClick.AddListener(() => {
            var act = onOk;
            if (_dataConfirmWin != null) Object.Destroy(_dataConfirmWin.gameObject);
            _dataConfirmWin = null;
            act?.Invoke();
        });
        var cancel = MakeButton(box.transform, "Cancel", new Vector2(-80f, -85f), new Vector2(140f, 44f), ItemColor);
        var cancelT = MakeText(cancel.transform, "取消", 22, Color.white, TextAnchor.MiddleCenter);
        Stretch(cancelT.rectTransform, Vector2.zero, Vector2.one);
        cancel.onClick.AddListener(() => {
            if (_dataConfirmWin != null) Object.Destroy(_dataConfirmWin.gameObject);
            _dataConfirmWin = null;
        });
    }

    /// <summary>切 tab：谱面信息 / 通关记录 / 寄录图 / 数据管理</summary>
    private static void RefreshPickTabs() {
        if (_pickInfoArea != null) _pickInfoArea.SetActive(_pickTab == 0);
        if (_pickWinRecordArea != null) _pickWinRecordArea.SetActive(_pickTab == 1);
        if (_pickWinHeatArea != null) _pickWinHeatArea.SetActive(_pickTab == 2);
        if (_pickWinDataArea != null) _pickWinDataArea.SetActive(_pickTab == 3);
        // 走 SetUiSelectFill（它把新色记成交互态层的常态色并立刻刷一次）：
        // 直接写 Image.color 会被交互态层每帧的重算顶掉，鼠标一动选中高亮就没了
        SetUiSelectFill(_pickTabInfo, _pickTab == 0 ? Accent : ItemColor);
        SetUiSelectFill(_pickTabRecord, _pickTab == 1 ? Accent : ItemColor);
        SetUiSelectFill(_pickTabHeat, _pickTab == 2 ? Accent : ItemColor);
        SetUiSelectFill(_pickTabData, _pickTab == 3 ? Accent : ItemColor);
    }

    private static float[] _heatDotTargetX, _heatDotTargetY, _heatDotBaseSize;
    private static float _heatGrowT = 1f, _heatChartBaseY, _heatChartH = 140f;
    private static bool _heatGrowActive;

    /// <summary>死亡后实时刷新完成度文本（弹窗开着才有意义）</summary>
    public static void RefreshCompletion() {
        try {
            if (_pickCompletionText != null && _pickedFile != null) {
                _pickCompletionText.text = Lang.Tr("完成度：") + LevelScanner.GetCompletion(Path.GetDirectoryName(_pickedFile)) + "%";
            }
        } catch { }
    }

    /// <summary>adofai 文件排序优先级：main 0 > level 1 > 其余 2 > backup(含数字后缀) 3</summary>
    private static int PickFileRank(string f) {
        string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
        if (n.Contains("main")) return 0;
        if (n.Contains("level")) return 1;
        if (n.StartsWith("backup")) return 3;   // backup / backup2 / backup3 …
        return 2;
    }

    /// <summary>剥离富文本标签（&lt;size=..&gt;/&lt;color=..&gt; 等设定大小颜色的代码）</summary>
}
