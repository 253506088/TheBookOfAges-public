using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Modding;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断报告写入器：把诊断信息独立写成桌面压缩包。
/// </summary>
public sealed class GmDiagnosticReportWriter
{
    private const long RelatedModContextBytes = 256 * 1024;
    private readonly string _modId;
    private readonly string _userDataDirectory;
    private readonly GmDiagnosticNameService _nameService;
    private readonly GmDiagnosticInfoCollector _infoCollector;
    private readonly GmDiagnosticModCollector _modCollector;
    private readonly GmDiagnosticLogCollector _logCollector;
    private readonly GmDiagnosticOutputPathService _outputPathService;
    private readonly GmDiagnosticRelatedModResolver _relatedModResolver = new();

    /// <summary>
    /// 创建诊断报告写入器。
    /// </summary>
    public GmDiagnosticReportWriter(
        string modId,
        string userDataDirectory,
        GmDiagnosticNameService nameService,
        GmDiagnosticInfoCollector infoCollector,
        GmDiagnosticModCollector modCollector,
        GmDiagnosticLogCollector logCollector)
    {
        _modId = modId;
        _userDataDirectory = userDataDirectory;
        _nameService = nameService;
        _infoCollector = infoCollector;
        _modCollector = modCollector;
        _logCollector = logCollector;
        _outputPathService = new GmDiagnosticOutputPathService(userDataDirectory, nameService);
    }

    /// <summary>
    /// 写出诊断压缩包，并返回生成路径。
    /// </summary>
    public string WriteReport(GmDiagnosticEvent diagnosticEvent)
    {
        IReadOnlyList<GmDiagnosticRelatedModInfo> relatedMods = ResolveRelatedMods(diagnosticEvent);
        GmDiagnosticOutputDirectory outputDirectory = _outputPathService.ResolveOutputDirectory();
        if (outputDirectory.Kind == GmDiagnosticOutputDirectoryKind.Disabled || string.IsNullOrWhiteSpace(outputDirectory.Path))
        {
            throw new IOException("未找到可写的诊断导出目录。");
        }

        try
        {
            return WriteReportToDirectory(outputDirectory.Path, diagnosticEvent, relatedMods, outputDirectory.Kind);
        }
        catch when (outputDirectory.Kind == GmDiagnosticOutputDirectoryKind.Desktop)
        {
            GmDiagnosticOutputDirectory fallbackDirectory = ResolveUserDataOutputDirectory();
            return WriteReportToDirectory(fallbackDirectory.Path, diagnosticEvent, relatedMods, fallbackDirectory.Kind);
        }
    }

    /// <summary>
    /// 写出诊断压缩包到指定目录。
    /// </summary>
    private string WriteReportToDirectory(
        string outputDirectory,
        GmDiagnosticEvent diagnosticEvent,
        IReadOnlyList<GmDiagnosticRelatedModInfo> relatedMods,
        GmDiagnosticOutputDirectoryKind outputDirectoryKind)
    {
        string archivePath = CreateUniqueArchivePath(outputDirectory, diagnosticEvent.CreatedAt);

        try
        {
            using FileStream outputStream = new(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using ZipArchive archive = new(outputStream, ZipArchiveMode.Create);
            AddTextEntry(archive, _nameService.GetReadmeEntryName(), BuildReadmeText(relatedMods));
            AddTextEntry(archive, _nameService.GetSummaryEntryName(), BuildSummaryText(diagnosticEvent, relatedMods, outputDirectoryKind, outputDirectory));
            AddTextEntry(archive, _nameService.GetSystemInfoEntryName(), _infoCollector.BuildInfoText(_modId, diagnosticEvent));
            AddTextEntry(archive, _nameService.GetModListEntryName(), _modCollector.BuildModListText());
            _logCollector.AddLogsToArchive(archive);
            AddReleaseInfoFile(archive);
        }
        catch
        {
            TryDeletePartialArchive(archivePath);
            throw;
        }

        return archivePath;
    }

    /// <summary>
    /// 解析本次诊断事件疑似相关的 Mod。
    /// </summary>
    private IReadOnlyList<GmDiagnosticRelatedModInfo> ResolveRelatedMods(GmDiagnosticEvent diagnosticEvent)
    {
        try
        {
            string recentLogContext = _logCollector.ReadRecentLogContext(RelatedModContextBytes);
            return _relatedModResolver.Resolve(diagnosticEvent, recentLogContext);
        }
        catch
        {
            return Array.Empty<GmDiagnosticRelatedModInfo>();
        }
    }

    /// <summary>
    /// 解析用户数据目录下的兜底输出目录。
    /// </summary>
    private GmDiagnosticOutputDirectory ResolveUserDataOutputDirectory()
    {
        string fallbackDirectory = Path.Combine(_userDataDirectory, "TheBookOfAges", "diagnostics", "reports");
        try
        {
            Directory.CreateDirectory(fallbackDirectory);
            return new GmDiagnosticOutputDirectory(fallbackDirectory, GmDiagnosticOutputDirectoryKind.UserData);
        }
        catch (Exception ex)
        {
            throw new IOException("用户数据诊断导出目录不可写。", ex);
        }
    }

    /// <summary>
    /// 删除写入失败时留下的半截压缩包。
    /// </summary>
    private static void TryDeletePartialArchive(string archivePath)
    {
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch
        {
            // 删除失败只会留下一个坏包，不能因此影响游戏运行。
        }
    }

    /// <summary>
    /// 创建不重复的压缩包路径。
    /// </summary>
    private string CreateUniqueArchivePath(string outputDirectory, DateTimeOffset createdAt)
    {
        string fileName = _nameService.BuildArchiveFileName(createdAt);
        string archivePath = Path.Combine(outputDirectory, fileName);
        if (!File.Exists(archivePath))
        {
            return archivePath;
        }

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        for (int index = 1; index < 100; index++)
        {
            archivePath = Path.Combine(outputDirectory, $"{nameWithoutExtension}-{index}.zip");
            if (!File.Exists(archivePath))
            {
                return archivePath;
            }
        }

        return Path.Combine(outputDirectory, $"{nameWithoutExtension}-{Guid.NewGuid():N}.zip");
    }

    /// <summary>
    /// 构建 README 文本。
    /// </summary>
    private string BuildReadmeText(IReadOnlyList<GmDiagnosticRelatedModInfo> relatedMods)
    {
        string minimumLevel = GmModConfig.GetDiagnosticExportMinimumLevelName();
        string modDisplayName = GetModDisplayName();
        string relatedModText = BuildRelatedModText(_nameService.IsChineseGameLanguage(), relatedMods);
        if (_nameService.IsChineseGameLanguage())
        {
            return $"""
                    这是《杀戮尖塔 2》Mod 运行异常诊断包。

                    本诊断包由“{modDisplayName}”Mod 自动生成。
                    如果你不希望自动导出，可以进入游戏的 Mod 设置，找到“{modDisplayName}” -> “异常诊断导出”，关闭“自动导出 Mod 异常诊断包”。
                    自动导出的触发等级也可以在同一设置页调整，默认是 Error，当前配置是 {minimumLevel}。

                    {relatedModText}

                    里面包含：
                    1. 异常摘要
                    2. 游戏与系统信息
                    3. 当前检测到的 Mod 列表和版本
                    4. 游戏日志尾部

                    这个诊断包只保存在本机，默认不会上传到远端。
                    如果需要反馈问题，可以把这个 zip 发给 Mod 作者或维护者。
                    """;
        }

        return $"""
                This is a Slay the Spire 2 mod diagnostic package.

                This diagnostic package was generated by the "{modDisplayName}" mod.
                You can disable automatic export in the in-game Mod Settings: {modDisplayName} -> Diagnostic Export -> Auto-export mod diagnostic packages.
                The export trigger level can also be adjusted there. The default is Error, and the current setting is {minimumLevel}.

                {relatedModText}

                It contains:
                1. Diagnostic summary
                2. Game and system info
                3. Detected mod list and versions
                4. Recent game logs

                This package is generated locally and is not uploaded automatically.
                You can send this zip to the mod author or maintainer when reporting an issue.
                """;
    }

    /// <summary>
    /// 构建 README 和摘要里的疑似相关 Mod 文本。
    /// </summary>
    private static string BuildRelatedModText(bool chinese, IReadOnlyList<GmDiagnosticRelatedModInfo> relatedMods)
    {
        StringBuilder builder = new();
        if (relatedMods.Count == 0)
        {
            builder.AppendLine(chinese ? "疑似相关 Mod：未能可靠判断。" : "Suspected related mod: unable to determine reliably.");
            builder.AppendLine(chinese
                ? "注意：这是根据日志文本自动推断的结果，不等于最终责任判断。"
                : "Note: this is an automatic guess from log text, not a final blame assignment.");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine(chinese ? "疑似相关 Mod：" : "Suspected related mod:");
        foreach (GmDiagnosticRelatedModInfo relatedMod in relatedMods)
        {
            if (chinese)
            {
                builder.AppendLine($"- {relatedMod.DisplayName}（ID: {relatedMod.ModId}，版本: {relatedMod.Version}，置信度: {FormatConfidence(true, relatedMod.Confidence)}）");
                builder.AppendLine($"  判断依据：{FormatReason(true, relatedMod.Reason)}");
                builder.AppendLine($"  证据片段：{relatedMod.Evidence}");
            }
            else
            {
                builder.AppendLine($"- {relatedMod.DisplayName} (ID: {relatedMod.ModId}, Version: {relatedMod.Version}, Confidence: {FormatConfidence(false, relatedMod.Confidence)})");
                builder.AppendLine($"  Reason: {FormatReason(false, relatedMod.Reason)}");
                builder.AppendLine($"  Evidence: {relatedMod.Evidence}");
            }
        }

        builder.AppendLine(chinese
            ? "注意：这是根据日志文本自动推断的结果，不等于最终责任判断。"
            : "Note: this is an automatic guess from log text, not a final blame assignment.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// 根据语言格式化相关 Mod 置信度。
    /// </summary>
    private static string FormatConfidence(bool chinese, GmDiagnosticRelatedModConfidence confidence)
    {
        if (!chinese)
        {
            return confidence.ToString();
        }

        return confidence switch
        {
            GmDiagnosticRelatedModConfidence.High => "高",
            GmDiagnosticRelatedModConfidence.Medium => "中",
            _ => "低",
        };
    }

    /// <summary>
    /// 根据语言格式化相关 Mod 判断依据。
    /// </summary>
    private static string FormatReason(bool chinese, string reason)
    {
        if (chinese)
        {
            return reason;
        }

        return reason switch
        {
            "异常内容中出现 Mod 路径。" => "The error text contains this mod path.",
            "异常内容中出现 Mod 目录名。" => "The error text contains this mod directory name.",
            "异常内容中出现 Mod DLL 文件名。" => "The error text contains this mod DLL file name.",
            "异常内容中出现 Mod ID。" => "The error text contains this mod ID.",
            "异常内容中出现 Mod 展示名。" => "The error text contains this mod display name.",
            "异常堆栈中出现疑似 Mod 命名空间。" => "The stack trace contains a namespace that looks related to this mod.",
            "触发消息中出现 Mod 日志前缀。" => "The trigger message contains this mod log prefix.",
            "触发消息中出现 Mod 路径。" => "The trigger message contains this mod path.",
            "触发消息中出现 Mod 目录名。" => "The trigger message contains this mod directory name.",
            "触发消息中出现 Mod DLL 文件名。" => "The trigger message contains this mod DLL file name.",
            "触发消息中出现 Mod ID。" => "The trigger message contains this mod ID.",
            "触发消息中出现 Mod 展示名。" => "The trigger message contains this mod display name.",
            "触发消息堆栈中出现 Mod 命名空间。" => "The trigger stack trace contains this mod namespace.",
            "最近日志上下文中出现 Mod 路径。" => "The recent log context contains this mod path.",
            "最近日志上下文中出现 Mod 目录名。" => "The recent log context contains this mod directory name.",
            "最近日志上下文中出现 Mod DLL 文件名。" => "The recent log context contains this mod DLL file name.",
            "最近日志上下文中出现 Mod ID。" => "The recent log context contains this mod ID.",
            "最近日志上下文中出现 Mod 展示名。" => "The recent log context contains this mod display name.",
            "最近日志上下文堆栈中出现 Mod 命名空间。" => "The recent log context contains this mod namespace.",
            _ => reason,
        };
    }

    /// <summary>
    /// 获取当前 Mod 展示名，优先使用运行时 Mod 清单里的 name，其次读取本地 json 清单。
    /// </summary>
    private string GetModDisplayName()
    {
        return TryGetRuntimeManifestName()
               ?? TryGetLocalManifestName()
               ?? _modId;
    }

    /// <summary>
    /// 从游戏运行时 ModManager 中读取当前 Mod 的展示名。
    /// </summary>
    private string? TryGetRuntimeManifestName()
    {
        try
        {
            foreach (Mod mod in ModManager.Mods)
            {
                ModManifest? manifest = mod.manifest;
                if (string.Equals(manifest?.id, _modId, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeManifestName(manifest?.name);
                }
            }
        }
        catch
        {
            // ModManager 尚未就绪时，继续尝试读取本地 json 清单。
        }

        return null;
    }

    /// <summary>
    /// 从本地 Mod 清单 json 中读取展示名。
    /// </summary>
    private string? TryGetLocalManifestName()
    {
        foreach (string candidatePath in GetManifestCandidates())
        {
            try
            {
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(candidatePath));
                if (document.RootElement.TryGetProperty("name", out JsonElement nameElement))
                {
                    string? name = NormalizeManifestName(nameElement.GetString());
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // 单个候选清单读取失败时继续尝试其它路径。
            }
        }

        return null;
    }

    /// <summary>
    /// 获取当前 Mod 清单 json 的候选路径。
    /// </summary>
    private IEnumerable<string> GetManifestCandidates()
    {
        string manifestFileName = $"{_modId}.json";
        string? assemblyDirectory = Path.GetDirectoryName(typeof(GmDiagnosticReportWriter).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            yield return Path.Combine(assemblyDirectory, manifestFileName);
            yield return Path.Combine(assemblyDirectory, _modId, manifestFileName);
        }

        yield return Path.Combine(AppContext.BaseDirectory, _modId, manifestFileName);
        yield return Path.Combine(AppContext.BaseDirectory, manifestFileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), _modId, manifestFileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), manifestFileName);
    }

    /// <summary>
    /// 清理清单名称，空白名称按读取失败处理。
    /// </summary>
    private static string? NormalizeManifestName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>
    /// 构建异常摘要文本。
    /// </summary>
    private string BuildSummaryText(
        GmDiagnosticEvent diagnosticEvent,
        IReadOnlyList<GmDiagnosticRelatedModInfo> relatedMods,
        GmDiagnosticOutputDirectoryKind outputDirectoryKind,
        string outputDirectory)
    {
        StringBuilder builder = new();
        bool chinese = _nameService.IsChineseGameLanguage();
        builder.AppendLine(chinese ? "=== 异常摘要 ===" : "=== Diagnostic Summary ===");
        builder.AppendLine($"{Label(chinese, "生成时间", "Generated At")}: {DateTimeOffset.Now:O}");
        builder.AppendLine($"{Label(chinese, "触发时间", "Triggered At")}: {diagnosticEvent.CreatedAt:O}");
        builder.AppendLine($"{Label(chinese, "触发类型", "Trigger Kind")}: {diagnosticEvent.Kind}");
        builder.AppendLine($"{Label(chinese, "触发来源", "Trigger Source")}: {diagnosticEvent.Source}");
        builder.AppendLine($"{Label(chinese, "导出目录类型", "Output Directory Kind")}: {FormatOutputDirectoryKind(chinese, outputDirectoryKind)}");
        builder.AppendLine($"{Label(chinese, "导出目录", "Output Directory")}: {outputDirectory}");
        if (diagnosticEvent.LogLevel.HasValue)
        {
            builder.AppendLine($"{Label(chinese, "日志级别", "Log Level")}: {diagnosticEvent.LogLevel.Value}");
        }

        builder.AppendLine();
        builder.AppendLine(BuildRelatedModText(chinese, relatedMods));
        builder.AppendLine();
        builder.AppendLine(chinese ? "触发消息：" : "Trigger Message:");
        builder.AppendLine(diagnosticEvent.Message);
        return GmDiagnosticTextSanitizer.Sanitize(builder.ToString());
    }

    /// <summary>
    /// 根据语言格式化导出目录类型。
    /// </summary>
    private static string FormatOutputDirectoryKind(bool chinese, GmDiagnosticOutputDirectoryKind kind)
    {
        if (!chinese)
        {
            return kind.ToString();
        }

        return kind switch
        {
            GmDiagnosticOutputDirectoryKind.Desktop => "桌面目录",
            GmDiagnosticOutputDirectoryKind.UserData => "用户数据目录",
            _ => "不可用",
        };
    }

    /// <summary>
    /// 尝试把 release_info.json 放进压缩包。
    /// </summary>
    private static void AddReleaseInfoFile(ZipArchive archive)
    {
        foreach (string candidatePath in GetReleaseInfoCandidates())
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            ZipArchiveEntry entry = archive.CreateEntry("release_info.json");
            using FileStream inputStream = new(candidatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using Stream outputStream = entry.Open();
            inputStream.CopyTo(outputStream);
            return;
        }
    }

    /// <summary>
    /// 获取 release_info.json 的候选路径。
    /// </summary>
    private static IEnumerable<string> GetReleaseInfoCandidates()
    {
        string? processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            yield return Path.Combine(processDirectory, "release_info.json");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "release_info.json");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "release_info.json");
    }

    /// <summary>
    /// 在压缩包里追加文本文件。
    /// </summary>
    private static void AddTextEntry(ZipArchive archive, string entryName, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName.Replace('\\', '/'));
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }

    /// <summary>
    /// 根据语言选择标签。
    /// </summary>
    private static string Label(bool chinese, string zhs, string eng)
    {
        return chinese ? zhs : eng;
    }
}
