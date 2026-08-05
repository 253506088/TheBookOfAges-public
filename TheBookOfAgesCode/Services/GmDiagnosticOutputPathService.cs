namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断输出目录服务：按平台选择桌面或用户数据目录，移动端不尝试写桌面。
/// </summary>
public sealed class GmDiagnosticOutputPathService
{
    private readonly string _userDataDirectory;
    private readonly GmDiagnosticNameService _nameService;

    /// <summary>
    /// 创建诊断输出目录服务。
    /// </summary>
    public GmDiagnosticOutputPathService(string userDataDirectory, GmDiagnosticNameService nameService)
    {
        _userDataDirectory = userDataDirectory;
        _nameService = nameService;
    }

    /// <summary>
    /// 解析当前可用的输出目录；桌面平台优先桌面，移动端和未知平台直接使用用户数据目录。
    /// </summary>
    public GmDiagnosticOutputDirectory ResolveOutputDirectory()
    {
        if (CanUseDesktopDirectory())
        {
            string? desktopDirectory = TryGetDesktopDirectory();
            if (!string.IsNullOrWhiteSpace(desktopDirectory))
            {
                string outputDirectory = Path.Combine(desktopDirectory, _nameService.GetOutputDirectoryName());
                if (TryEnsureDirectory(outputDirectory))
                {
                    return new GmDiagnosticOutputDirectory(outputDirectory, GmDiagnosticOutputDirectoryKind.Desktop);
                }
            }
        }

        string fallbackDirectory = Path.Combine(_userDataDirectory, "TheBookOfAges", "diagnostics", "reports");
        if (TryEnsureDirectory(fallbackDirectory))
        {
            return new GmDiagnosticOutputDirectory(fallbackDirectory, GmDiagnosticOutputDirectoryKind.UserData);
        }

        return new GmDiagnosticOutputDirectory(string.Empty, GmDiagnosticOutputDirectoryKind.Disabled);
    }

    /// <summary>
    /// 判断当前平台是否适合尝试桌面目录。
    /// </summary>
    private static bool CanUseDesktopDirectory()
    {
        if (OperatingSystem.IsAndroid()
            || OperatingSystem.IsIOS()
            || OperatingSystem.IsTvOS()
            || OperatingSystem.IsBrowser())
        {
            return false;
        }

        return OperatingSystem.IsWindows()
               || OperatingSystem.IsMacOS()
               || OperatingSystem.IsLinux()
               || OperatingSystem.IsFreeBSD();
    }

    /// <summary>
    /// 获取系统桌面目录；取不到时返回空。
    /// </summary>
    private static string? TryGetDesktopDirectory()
    {
        string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktopDirectory))
        {
            return desktopDirectory;
        }

        desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return string.IsNullOrWhiteSpace(desktopDirectory) ? null : desktopDirectory;
    }

    /// <summary>
    /// 尝试创建目录；失败返回 false，不能影响游戏运行。
    /// </summary>
    private static bool TryEnsureDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// GM 诊断输出目录类型。
/// </summary>
public enum GmDiagnosticOutputDirectoryKind
{
    /// <summary>
    /// 使用桌面专用目录。
    /// </summary>
    Desktop,

    /// <summary>
    /// 使用游戏用户数据目录。
    /// </summary>
    UserData,

    /// <summary>
    /// 没有可写目录，本次导出禁用。
    /// </summary>
    Disabled,
}

/// <summary>
/// GM 诊断输出目录解析结果。
/// </summary>
public readonly record struct GmDiagnosticOutputDirectory(string Path, GmDiagnosticOutputDirectoryKind Kind);
