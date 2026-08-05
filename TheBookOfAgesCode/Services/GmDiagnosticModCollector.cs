using System.Text;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断 Mod 清单收集器：从游戏运行时读取所有已检测到的 Mod。
/// </summary>
public sealed class GmDiagnosticModCollector
{
    private readonly GmDiagnosticNameService _nameService;

    /// <summary>
    /// 创建 Mod 清单收集器。
    /// </summary>
    public GmDiagnosticModCollector(GmDiagnosticNameService nameService)
    {
        _nameService = nameService;
    }

    /// <summary>
    /// 构建 Mod 列表文本。
    /// </summary>
    public string BuildModListText()
    {
        StringBuilder builder = new();
        bool chinese = _nameService.IsChineseGameLanguage();
        IReadOnlyList<Mod> mods = TryGetMods();

        builder.AppendLine(chinese ? "=== Mod 列表 ===" : "=== Mod List ===");
        builder.AppendLine($"{Label(chinese, "检测到的 Mod 数量", "Detected Mod Count")}: {mods.Count}");
        builder.AppendLine();

        for (int index = 0; index < mods.Count; index++)
        {
            AppendMod(builder, mods[index], index + 1, chinese);
            builder.AppendLine();
        }

        return GmDiagnosticTextSanitizer.Sanitize(builder.ToString());
    }

    /// <summary>
    /// 追加单个 Mod 的信息。
    /// </summary>
    private static void AppendMod(StringBuilder builder, Mod mod, int index, bool chinese)
    {
        ModManifest? manifest = mod.manifest;
        builder.AppendLine($"[{index}] {manifest?.name ?? Label(chinese, "未知名称", "Unknown Name")}");
        builder.AppendLine($"  {Label(chinese, "ID", "ID")}: {manifest?.id ?? "Unknown"}");
        builder.AppendLine($"  {Label(chinese, "作者", "Author")}: {manifest?.author ?? "Unknown"}");
        builder.AppendLine($"  {Label(chinese, "版本", "Version")}: {manifest?.version ?? "Unknown"}");
        builder.AppendLine($"  {Label(chinese, "加载状态", "Load State")}: {mod.state}");
        builder.AppendLine($"  {Label(chinese, "来源", "Source")}: {mod.modSource}");
        builder.AppendLine($"  {Label(chinese, "包含 DLL", "Has DLL")}: {manifest?.hasDll.ToString() ?? "Unknown"}");
        builder.AppendLine($"  {Label(chinese, "包含 PCK", "Has PCK")}: {manifest?.hasPck.ToString() ?? "Unknown"}");
        builder.AppendLine($"  {Label(chinese, "影响玩法", "Affects Gameplay")}: {manifest?.affectsGameplay.ToString() ?? "Unknown"}");
        builder.AppendLine($"  {Label(chinese, "依赖", "Dependencies")}: {BuildDependenciesText(manifest)}");
        AppendErrors(builder, mod, chinese);
    }

    /// <summary>
    /// 追加 Mod 加载错误。
    /// </summary>
    private static void AppendErrors(StringBuilder builder, Mod mod, bool chinese)
    {
        if (mod.errors == null || mod.errors.Count == 0)
        {
            return;
        }

        builder.AppendLine($"  {Label(chinese, "加载错误", "Load Errors")}:");
        foreach (LocString error in mod.errors)
        {
            builder.AppendLine($"    - {FormatLocString(error)}");
        }
    }

    /// <summary>
    /// 构建依赖列表文本。
    /// </summary>
    private static string BuildDependenciesText(ModManifest? manifest)
    {
        if (manifest?.dependencies == null || manifest.dependencies.Count == 0)
        {
            return "None";
        }

        return string.Join(", ", manifest.dependencies);
    }

    /// <summary>
    /// 安全格式化本地化错误文本。
    /// </summary>
    private static string FormatLocString(LocString locString)
    {
        try
        {
            return locString.GetFormattedText();
        }
        catch
        {
            return $"{locString.LocTable}/{locString.LocEntryKey}";
        }
    }

    /// <summary>
    /// 获取当前 ModManager 中的 Mod 列表。
    /// </summary>
    private static IReadOnlyList<Mod> TryGetMods()
    {
        try
        {
            return ModManager.Mods;
        }
        catch
        {
            return Array.Empty<Mod>();
        }
    }

    /// <summary>
    /// 根据语言选择标签。
    /// </summary>
    private static string Label(bool chinese, string zhs, string eng)
    {
        return chinese ? zhs : eng;
    }
}
