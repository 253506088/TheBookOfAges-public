using System.Text.RegularExpressions;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断文本清理器：尽量隐藏用户目录名，降低日志外发时的隐私暴露。
/// </summary>
public static class GmDiagnosticTextSanitizer
{
    /// <summary>
    /// 清理诊断文本中的常见用户路径。
    /// </summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string sanitized = text;
        sanitized = Regex.Replace(sanitized, @"C:\\Users\\[^\\\r\n]+", @"C:\Users\<user>", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"/Users/[^/\r\n\s]+", "/Users/<user>", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"/home/[^/\r\n\s]+", "/home/<user>", RegexOptions.IgnoreCase);

        string userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            sanitized = sanitized.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }
}
