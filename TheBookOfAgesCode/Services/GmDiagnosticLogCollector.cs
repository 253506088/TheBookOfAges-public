using System.IO.Compression;
using System.Text;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断日志收集器：独立读取游戏日志目录并写入诊断压缩包。
/// </summary>
public sealed class GmDiagnosticLogCollector
{
    private const int ArchivedLogFileCount = 8;
    private const int RecentLogFileCount = 5;
    private const long RecentScanBytes = 1024 * 1024;
    private readonly string _userDataDirectory;
    private readonly long _maxBytesPerLogFile;

    /// <summary>
    /// 创建日志收集器。
    /// </summary>
    public GmDiagnosticLogCollector(string userDataDirectory, long maxBytesPerLogFile)
    {
        _userDataDirectory = userDataDirectory;
        _maxBytesPerLogFile = maxBytesPerLogFile;
    }

    /// <summary>
    /// 获取游戏日志目录。
    /// </summary>
    public string GetLogsDirectory()
    {
        return Path.Combine(_userDataDirectory, "logs");
    }

    /// <summary>
    /// 获取 godot.log 路径。
    /// </summary>
    public string GetPrimaryLogPath()
    {
        return Path.Combine(GetLogsDirectory(), "godot.log");
    }

    /// <summary>
    /// 把日志目录里的日志文件写入诊断压缩包。
    /// </summary>
    public void AddLogsToArchive(ZipArchive archive)
    {
        string logsDirectory = GetLogsDirectory();
        if (!Directory.Exists(logsDirectory))
        {
            CreateTextEntry(archive, "logs/logs_not_found.txt", $"日志目录不存在：{logsDirectory}");
            return;
        }

        IReadOnlyList<string> logFiles = GetRecentLogFiles(logsDirectory, ArchivedLogFileCount);
        if (logFiles.Count == 0)
        {
            CreateTextEntry(archive, "logs/logs_empty.txt", $"日志目录为空或无法读取：{logsDirectory}");
            return;
        }

        foreach (string filePath in logFiles)
        {
            string relativePath = Path.GetRelativePath(logsDirectory, filePath).Replace('\\', '/');
            string entryName = $"logs/{relativePath}";
            AddLogFileTail(archive, filePath, entryName);
        }
    }

    /// <summary>
    /// 扫描最近日志尾部，判断上次异常退出是否有明显错误痕迹。
    /// </summary>
    public GmDiagnosticEvent? TryFindRecentTrigger()
    {
        string logsDirectory = GetLogsDirectory();
        if (!Directory.Exists(logsDirectory))
        {
            return null;
        }

        foreach (string filePath in GetRecentLogFiles(logsDirectory, RecentLogFileCount))
        {
            string text = ReadTailText(filePath, RecentScanBytes);
            foreach (string line in SplitLines(text).Reverse())
            {
                GmDiagnosticEvent? diagnosticEvent = GmDiagnosticTriggerMatcher.TryMatchText(line, $"日志文件扫描：{Path.GetFileName(filePath)}");
                if (diagnosticEvent != null)
                {
                    return diagnosticEvent;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 读取最近日志上下文，供相关 Mod 识别使用。
    /// </summary>
    public string ReadRecentLogContext(long maxBytes)
    {
        string logsDirectory = GetLogsDirectory();
        if (!Directory.Exists(logsDirectory) || maxBytes <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        long remainingBytes = maxBytes;
        foreach (string filePath in GetRecentLogFiles(logsDirectory, RecentLogFileCount))
        {
            if (remainingBytes <= 0)
            {
                break;
            }

            string text = ReadTailText(filePath, Math.Min(remainingBytes, RecentScanBytes));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            builder.AppendLine($"===== {Path.GetFileName(filePath)} =====");
            builder.AppendLine(text);
            remainingBytes -= Encoding.UTF8.GetByteCount(text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 获取最近更新的日志文件；读取失败时返回空列表，避免诊断导出反过来影响游戏。
    /// </summary>
    private static IReadOnlyList<string> GetRecentLogFiles(string logsDirectory, int maxCount)
    {
        try
        {
            return Directory.EnumerateFiles(logsDirectory, "*", SearchOption.AllDirectories)
                .OrderByDescending(SafeGetLastWriteTimeUtc)
                .Take(maxCount)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 安全获取日志修改时间；单个文件异常不能拖垮整个诊断包。
    /// </summary>
    private static DateTime SafeGetLastWriteTimeUtc(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 从 godot.log 读取新增行；首次调用只定位到文件尾部，不读取旧内容。
    /// </summary>
    public IReadOnlyList<string> ReadNewPrimaryLogLines(ref long position)
    {
        string logPath = GetPrimaryLogPath();
        if (!File.Exists(logPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            using FileStream stream = new(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (position < 0)
            {
                position = stream.Length;
                return Array.Empty<string>();
            }

            if (position > stream.Length)
            {
                position = 0;
            }

            if (position == stream.Length)
            {
                return Array.Empty<string>();
            }

            stream.Seek(position, SeekOrigin.Begin);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            string text = reader.ReadToEnd();
            position = stream.Length;
            return SplitLines(text);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 把一个日志文件尾部写入压缩包。
    /// </summary>
    private void AddLogFileTail(ZipArchive archive, string filePath, string entryName)
    {
        string text = ReadTailText(filePath, _maxBytesPerLogFile);
        CreateTextEntry(archive, entryName, GmDiagnosticTextSanitizer.Sanitize(text));
    }

    /// <summary>
    /// 读取文件尾部文本。
    /// </summary>
    private static string ReadTailText(string filePath, long maxBytes)
    {
        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bool truncated = false;
            if (maxBytes > 0 && stream.Length > maxBytes)
            {
                stream.Seek(stream.Length - maxBytes, SeekOrigin.Begin);
                SkipPartialLine(stream);
                truncated = true;
            }

            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string text = reader.ReadToEnd();
            return truncated ? $"[...日志已截断，仅保留最后约 {maxBytes / 1024 / 1024} MB...]\n{text}" : text;
        }
        catch (Exception ex)
        {
            return $"读取日志失败：{filePath}\n异常类型：{ex.GetType().Name}\n异常信息：{ex.Message}";
        }
    }

    /// <summary>
    /// 跳过从文件中间开始读取时可能遇到的半行内容。
    /// </summary>
    private static void SkipPartialLine(Stream stream)
    {
        while (stream.Position < stream.Length)
        {
            int value = stream.ReadByte();
            if (value == -1 || value == '\n')
            {
                break;
            }
        }
    }

    /// <summary>
    /// 按行拆分文本。
    /// </summary>
    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 在压缩包里创建文本文件。
    /// </summary>
    private static void CreateTextEntry(ZipArchive archive, string entryName, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName.Replace('\\', '/'));
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
