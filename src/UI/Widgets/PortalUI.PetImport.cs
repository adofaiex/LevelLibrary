using System;
using System.IO;
using System.Text;

namespace LevelLibrary;

/// <summary>PortalUI partial 模块：宠物模型包（.llmd）的导入与模型表重载。
/// 格式解析、几何推导、图片量框、落盘文本全在 `PetModelPack.cs`（纯逻辑，验证台拿真包直接跑它）；
/// 这里只做碰引擎的三件事：开文件对话框、把模型表重建一遍、导入完把设置页那一行刷一下。</summary>
public static partial class PortalUI {

    /// <summary>导入模型存放目录（玩家数据：不进资源包、不进 mod 包）。</summary>
    public static string PetModelsFolder {
        get {
            try { return System.IO.Path.Combine(Main.ModEntry.Path, "pet_models"); } catch { return null; }
        }
    }

    /// <summary>重载模型表：0 号恒是大肥鱼，后面按目录名排序追加导入的。
    /// 必须排在 `PrewarmWithGameLoad()` 之前（字形预热要按 PetLinesPool 把每一句都烘进去）。</summary>
    public static void ReloadPetModels() {
        try {
            while (PetModels.Count > 1) PetModels.RemoveAt(PetModels.Count - 1);
            int bad = 0;
            foreach (var d in PetModelPack.ListInstalled(PetModelsFolder)) {
                try {
                    string manifestPath = Path.Combine(d, PetModelPack.EntryManifest);
                    string imagePath = Path.Combine(d, PetModelPack.EntryImage);
                    string linesPath = Path.Combine(d, PetModelPack.EntryLines);
                    if (!File.Exists(manifestPath) || !File.Exists(imagePath) || !File.Exists(linesPath)) { bad++; continue; }
                    PetModelPack.PackInfo info;
                    string err;
                    if (!PetModelPack.TryReadInstalled(File.ReadAllText(manifestPath, Encoding.UTF8),
                                                       Path.GetFileName(d), out info, out err) ||
                        PetModelPack.CheckGeometry(info.Geo) != null) { bad++; continue; }
                    var lines = PetModelPack.ParseLines(File.ReadAllText(linesPath, Encoding.UTF8));
                    if (lines.Length == 0) { bad++; continue; }
                    info.Lines = lines;
                    if (string.IsNullOrEmpty(info.SayLine)) info.SayLine = lines[0];
                    var def = new PetModelDef(info.Id, info.Name, PetModelPack.EntryImage, info.Geo.TexK,
                                              info.Geo.MaskTopK, info.Geo.BubX, info.Geo.BubY,
                                              info.Geo.BubW, info.Geo.BubH,
                                              info.SayLine, lines, imagePath, info.FaceFlip);
                    // 编辑器加的多条开谱面台词与定时台词（老包没有就是 null/空 = 老行为）
                    def.SayLines = info.SayLines;
                    def.TimedMins = info.TimedMins;
                    def.TimedTexts = info.TimedTexts;
                    PetModels.Add(def);
                    // 每个模型读到的几何都报一行：气泡位置出问题时，第一眼看的就是这几个数
                    // （清单里写的是什么、运行时拿到的就得是什么；自带气泡有没有也跟着报）。
                    Lg.Info("宠物模型几何：" + info.Name
                        + " bubx=" + info.Geo.BubX.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                        + " buby=" + info.Geo.BubY.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                        + " bubw=" + info.Geo.BubW.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                        + " bubh=" + info.Geo.BubH.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                        + " texk=" + info.Geo.TexK.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                        + " 自带气泡=" + (info.HasBubble ? "有" : "没有")
                        + " 目录=" + Path.GetFileName(d));
                } catch (System.Exception e) {
                    bad++;
                    Lg.Info("宠物模型读不了： " + e.Message);
                }
            }
            Lg.Info("宠物模型：可用 " + PetModels.Count + " 个（大肥鱼 + 导入 "
                + (PetModels.Count - 1) + " 个" + (bad > 0 ? "，另有 " + bad + " 个目录读不了" : "") + "）");
        } catch (System.Exception e) { Lg.Ex("重载宠物模型失败", e); }
    }

    /// <summary>设置里点「选择模型文件」：选 .llmd → 导入（解包 / 量框 / 算几何 / 落盘）→ 重载 → 切过去。</summary>
    private static void PickPetModelPack() {
        try {
            string path = ModernFileDialog.OpenFile("选择宠物模型包", "宠物模型包", "*" + PetModelPack.Extension);
            if (string.IsNullOrEmpty(path)) return;
            string folder = PetModelsFolder;
            string err;
            var info = PetModelPack.ImportFromFile(path, folder, out err);
            if (info == null) {
                ShowToast(err);
                Lg.Info("宠物模型包导入失败：" + err);
                return;
            }
            // 同一份包重新导入（或换了图）时必须让缓存的贴图失效，否则宠物还用着上一版
            InvalidateAssetSprite(Path.Combine(folder, info.Id, PetModelPack.EntryImage));
            ReloadPetModels();
                RefreshPetPickIfOpen();   // 下拉开着就重建，关着只更新那一行的名字
            int idx = IndexOfPetModel(info.Id);
            if (idx > 0) PetSwitchModel(idx);
            RefreshPetModelText();
            ShowToast(idx > 0 ? "已导入《" + info.Name + "》并切了过去" : "已导入《" + info.Name + "》");
            Lg.Info("宠物模型包导入成功：《" + info.Name + "》（id=" + info.Id
                + "，" + info.Width + "×" + info.Height
                + "，贴图 " + info.Geo.TexK.ToString("0.###") + " / 内容顶 " + info.Geo.MaskTopK.ToString("0.###")
                + " / 气泡 " + info.Geo.BubX.ToString("0.####") + "," + info.Geo.BubY.ToString("0.####")
                + "，台词 " + info.Lines.Length + " 句）");
        } catch (System.Exception e) {
            Lg.Ex("选择宠物模型包失败", e);
            ShowToast(Lang.Tr("模型包读不进来，换一个文件再试"));
        }
    }

    /// <summary>装好的模型里按 id 找下标（找不到返回 -1）。</summary>
    private static int IndexOfPetModel(string id) {
        for (int i = 0; i < PetModels.Count; i++) if (PetModels[i].Id == id) return i;
        return -1;
    }
}
