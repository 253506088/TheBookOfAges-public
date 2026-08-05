using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断命名服务：根据游戏语言生成桌面目录名、压缩包文件名和包内文本文件名。
/// </summary>
public sealed class GmDiagnosticNameService
{
    private const string ChineseDirectoryName = "杀戮尖塔2mod运行异常日志";
    private const string EnglishDirectoryName = "Slay the Spire 2 Mod Diagnostic Logs";
    private const string ChineseFilePrefix = "杀戮尖塔2Mod运行异常日志";
    private const string EnglishFilePrefix = "SlayTheSpire2-Mod-Diagnostic-Log";
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// 获取当前语言对应的诊断目录名。
    /// </summary>
    public string GetOutputDirectoryName()
    {
        return IsChineseGameLanguage() ? ChineseDirectoryName : EnglishDirectoryName;
    }

    /// <summary>
    /// 根据当前语言生成诊断包文件名。
    /// </summary>
    public string BuildArchiveFileName(DateTimeOffset now)
    {
        if (IsChineseGameLanguage())
        {
            string chineseTimestamp = now.LocalDateTime.ToString("yyyy年MM月dd日HH时mm分ss秒", CultureInfo.InvariantCulture);
            return $"{ChineseFilePrefix}-{chineseTimestamp}.zip";
        }

        string timestamp = now.LocalDateTime.ToString("MMM-dd-yyyy_hh-mm-ss-tt", EnglishCulture);
        return $"{EnglishFilePrefix}-{timestamp}.zip";
    }

    /// <summary>
    /// 获取 README 文件名。
    /// </summary>
    public string GetReadmeEntryName()
    {
        return IsChineseGameLanguage() ? "README_先看这里.txt" : "README_Read_Me_First.txt";
    }

    /// <summary>
    /// 获取异常摘要文件名。
    /// </summary>
    public string GetSummaryEntryName()
    {
        return IsChineseGameLanguage() ? "异常摘要.txt" : "Diagnostic_Summary.txt";
    }

    /// <summary>
    /// 获取游戏和系统信息文件名。
    /// </summary>
    public string GetSystemInfoEntryName()
    {
        return IsChineseGameLanguage() ? "游戏与系统信息.txt" : "Game_And_System_Info.txt";
    }

    /// <summary>
    /// 获取 Mod 列表文件名。
    /// </summary>
    public string GetModListEntryName()
    {
        return IsChineseGameLanguage() ? "Mod列表.txt" : "Mod_List.txt";
    }

    /// <summary>
    /// 判断当前游戏语言是否是中文。
    /// </summary>
    public bool IsChineseGameLanguage()
    {
        IReadOnlyList<string> gameLanguages = GetGameLanguageCandidates();
        if (gameLanguages.Any(IsChineseLanguageToken))
        {
            return true;
        }

        if (gameLanguages.Count > 0)
        {
            return false;
        }

        return GetFallbackLanguageCandidates().Any(IsChineseLanguageToken);
    }

    /// <summary>
    /// 获取当前游戏语言；优先返回游戏和 Godot 当前语言，失败时才使用系统语言兜底。
    /// </summary>
    public string GetGameLanguage()
    {
        IReadOnlyList<string> gameLanguages = GetGameLanguageCandidates();
        string? chineseGameLanguage = gameLanguages.FirstOrDefault(IsChineseLanguageToken);
        if (!string.IsNullOrWhiteSpace(chineseGameLanguage))
        {
            return chineseGameLanguage;
        }

        string? gameLanguage = gameLanguages.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(gameLanguage))
        {
            return gameLanguage;
        }

        return GetFallbackLanguageCandidates().FirstOrDefault() ?? "eng";
    }

    /// <summary>
    /// 获取诊断语言候选，用于写入诊断信息，方便排查语言误判。
    /// </summary>
    public string GetLanguageCandidatesText()
    {
        List<string> candidates = [];
        AddLanguageCandidate(candidates, TryGetLocManagerLanguage(), "LocManager");
        AddLanguageCandidate(candidates, TryGetGodotLocale(), "Godot");
        AddLanguageCandidate(candidates, CultureInfo.CurrentUICulture.Name, "CurrentUICulture");
        AddLanguageCandidate(candidates, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "CurrentUICultureTwoLetter");
        return candidates.Count == 0 ? "Unknown" : string.Join(", ", candidates);
    }

    /// <summary>
    /// 获取游戏语言候选；只读取游戏本身和 Godot 当前语言，避免系统中文误判英文游戏。
    /// </summary>
    private static IReadOnlyList<string> GetGameLanguageCandidates()
    {
        List<string> candidates = [];
        AddLanguageCandidate(candidates, TryGetLocManagerLanguage());
        AddLanguageCandidate(candidates, TryGetGodotLocale());
        return candidates;
    }

    /// <summary>
    /// 获取系统语言兜底候选；仅在游戏语言完全读取不到时使用。
    /// </summary>
    private static IReadOnlyList<string> GetFallbackLanguageCandidates()
    {
        List<string> candidates = [];
        AddLanguageCandidate(candidates, CultureInfo.CurrentUICulture.Name);
        AddLanguageCandidate(candidates, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        return candidates;
    }

    /// <summary>
    /// 尝试读取游戏本地化管理器的语言。
    /// </summary>
    private static string? TryGetLocManagerLanguage()
    {
        try
        {
            return LocManager.Instance?.Language;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 尝试读取 Godot 当前语言；异常诊断可能在后台线程触发，所以失败时安静兜底。
    /// </summary>
    private static string? TryGetGodotLocale()
    {
        try
        {
            return TranslationServer.GetLocale();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 添加语言候选，自动过滤空白和重复值。
    /// </summary>
    private static void AddLanguageCandidate(List<string> candidates, string? value, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalized = value.Trim();
        if (candidates.Any(candidate => string.Equals(StripLanguageSource(candidate), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(string.IsNullOrWhiteSpace(source) ? normalized : $"{source}={normalized}");
    }

    /// <summary>
    /// 去掉诊断展示中的来源前缀，便于复用去重逻辑。
    /// </summary>
    private static string StripLanguageSource(string value)
    {
        int separatorIndex = value.IndexOf('=');
        return separatorIndex < 0 ? value : value[(separatorIndex + 1)..];
    }

    /// <summary>
    /// 判断语言标识是否表示中文，兼容 zh_CN、zhs、chs、Chinese 等常见写法。
    /// </summary>
    private static bool IsChineseLanguageToken(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        string normalized = StripLanguageSource(language).Trim().Replace('_', '-');
        return normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("zhs", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("zht", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("chs", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("cht", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("chi", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("zho", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("chinese", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("simplified", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("traditional", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("中文", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("简体", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("繁体", StringComparison.OrdinalIgnoreCase);
    }

}
