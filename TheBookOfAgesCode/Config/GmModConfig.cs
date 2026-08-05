using System.Text.Json;
using BaseLib.Config;
using MegaCrit.Sts2.Core.Logging;

namespace TheBookOfAges.TheBookOfAgesCode.Config;

/// <summary>
/// 诊断包自动导出的最低日志触发等级。
/// </summary>
public enum GmDiagnosticExportLogLevel
{
    /// <summary>
    /// Info、Warn、Error 都会触发导出。
    /// </summary>
    Info,

    /// <summary>
    /// Warn 和 Error 会触发导出。
    /// </summary>
    Warn,

    /// <summary>
    /// 只有 Error 会触发导出。
    /// </summary>
    Error,
}

/// <summary>
/// 岁月史书 GM 控制台的 BaseLib 配置。
/// 负责把原来的本地 json 配置迁移到游戏内 Mod Settings，
/// 并继续给现有业务代码提供统一的读取入口。
/// </summary>
public sealed class GmModConfig : SimpleModConfig
{
    private const string ConfigFileName = "TheBookOfAges.config.json";
    private static readonly object RegistrationLock = new();

    private static bool _isRegistered;
    private static GmModConfig? _registeredConfig;

    /// <summary>
    /// 运行时配置变更事件。
    /// 用于通知现有业务逻辑在游戏内配置页修改后及时同步状态。
    /// </summary>
    public static event Action? RuntimeConfigChanged;

    /// <summary>
    /// 旧版配置文件结构，只用于首次迁移。
    /// </summary>
    private sealed class LegacyConfigData
    {
        /// <summary>
        /// 旧版日志开关。
        /// </summary>
        public bool EnableLog { get; set; } = !true;

        /// <summary>
        /// 旧版卡片预热开关。
        /// </summary>
        public bool EnableCardPreheat { get; set; }
    }

    /// <summary>
    /// 是否输出岁月史书 GM 控制台自己的日志。
    /// 只影响本 Mod，不影响 BaseLib 或其他 Mod。
    /// </summary>
    [ConfigSection("ChronicleModSection")]
    [ConfigHoverTip]
    public static bool EnableLog { get; set; } = true;

    /// <summary>
    /// 是否启用卡片自助页的后台预热。
    /// 只影响本 Mod 的卡片自助体验，不影响其他 Mod。
    /// </summary>
    [ConfigHoverTip]
    public static bool EnableCardPreheat { get; set; }

    /// <summary>
    /// 是否禁止遗物自助重复发放同一遗物。
    /// 关闭时允许重复发放；开启时保持旧逻辑，已有遗物会跳过。
    /// </summary>
    [ConfigHoverTip]
    public static bool PreventDuplicateRelicGrant { get; set; }

    /// <summary>
    /// 是否在检测到游戏或 Mod 运行异常时自动导出诊断包。
    /// PC 默认开启，移动端默认关闭；玩家修改后由 BaseLib 配置持久保存。
    /// </summary>
    [ConfigSection("DiagnosticSection")]
    [ConfigHoverTip]
    public static bool EnableDiagnosticExport { get; set; } = GetDefaultDiagnosticExportEnabled();

    /// <summary>
    /// 自动导出诊断包的最低日志触发等级。
    /// 默认 Error；玩家改成 Warn 时，Warn 和 Error 都会触发。
    /// </summary>
    [ConfigHoverTip]
    public static GmDiagnosticExportLogLevel DiagnosticExportMinimumLevel { get; set; } = GmDiagnosticExportLogLevel.Error;

    /// <summary>
    /// 是否已完成旧配置迁移。
    /// 只参与保存，不在游戏内配置页显示。
    /// </summary>
    [ConfigHideInUI]
    [ConfigIgnoreRestoreDefaults]
    public static bool LegacyConfigMigrated { get; set; }

    /// <summary>
    /// 是否已经按当前平台应用过诊断导出的默认开关。
    /// 用于把旧版本移动端的默认开启迁移为默认关闭，不在游戏内配置页显示。
    /// </summary>
    [ConfigHideInUI]
    [ConfigIgnoreRestoreDefaults]
    public static bool DiagnosticPlatformDefaultApplied { get; set; }

    /// <summary>
    /// 给现有日志代码保留的便捷读取口。
    /// </summary>
    public static bool IsLogEnabled => EnableLog;

    /// <summary>
    /// 给现有卡片预热逻辑保留的便捷读取口。
    /// </summary>
    public static bool IsCardPreheatEnabled => EnableCardPreheat;

    /// <summary>
    /// 给遗物自助逻辑保留的便捷读取口。
    /// </summary>
    public static bool ShouldPreventDuplicateRelicGrant => PreventDuplicateRelicGrant;

    /// <summary>
    /// 给诊断导出模块保留的便捷读取口。
    /// </summary>
    public static bool IsDiagnosticExportEnabled => EnableDiagnosticExport;

    /// <summary>
    /// 给诊断导出模块保留的最低触发等级读取口。
    /// </summary>
    public static GmDiagnosticExportLogLevel MinimumDiagnosticExportLevel => DiagnosticExportMinimumLevel;

    /// <summary>
    /// 判断当前日志等级是否达到玩家配置的诊断导出最低等级。
    /// </summary>
    public static bool ShouldExportDiagnosticForLogLevel(LogLevel level)
    {
        return GetSeverityRank(level) >= GetMinimumDiagnosticExportSeverityRank();
    }

    /// <summary>
    /// 获取诊断导出最低等级显示文本。
    /// </summary>
    public static string GetDiagnosticExportMinimumLevelName()
    {
        return MinimumDiagnosticExportLevel.ToString();
    }

    /// <summary>
    /// 创建并初始化 BaseLib 配置实例。
    /// </summary>
    public GmModConfig()
    {
        ImportLegacyConfigIfNeeded();
        ApplyDiagnosticPlatformDefaultIfNeeded();
    }

    /// <summary>
    /// 把本配置注册到 BaseLib 的 Mod Settings 列表。
    /// </summary>
    public static void Register(string modId)
    {
        lock (RegistrationLock)
        {
            if (_isRegistered)
            {
                return;
            }

            GmModConfig config = new();
            config.ConfigChanged += HandleConfigChanged;
            config.OnConfigReloaded += HandleConfigReloaded;
            ModConfigRegistry.Register(modId, config);
            _registeredConfig = config;
            _isRegistered = true;
        }
    }

    /// <summary>
    /// 重新从 BaseLib 配置文件读取当前配置。
    /// </summary>
    public static void Reload()
    {
        _registeredConfig?.Load();
    }

    /// <summary>
    /// 首次切到 BaseLib 配置时，把旧版同目录 json 配置导入进来。
    /// 迁移完成后只保留新配置为主，不再继续读取旧文件。
    /// </summary>
    private void ImportLegacyConfigIfNeeded()
    {
        if (LegacyConfigMigrated)
        {
            return;
        }

        try
        {
            LegacyConfigData? legacy = TryLoadLegacyConfig();
            if (legacy != null)
            {
                EnableLog = legacy.EnableLog;
                EnableCardPreheat = legacy.EnableCardPreheat;
            }
        }
        catch
        {
            // 迁移失败时保留当前默认值，避免因为旧文件损坏影响新配置启用。
        }

        LegacyConfigMigrated = true;
        Save();
        ConfigReloaded();
    }

    /// <summary>
    /// 首次应用平台默认值：PC 默认开启，移动端默认关闭。
    /// 移动端旧版本若因旧默认值已经开启，会在本次迁移中关闭；之后玩家手动开启会被保留。
    /// </summary>
    private void ApplyDiagnosticPlatformDefaultIfNeeded()
    {
        if (DiagnosticPlatformDefaultApplied)
        {
            return;
        }

        if (IsMobilePlatform())
        {
            EnableDiagnosticExport = false;
        }

        DiagnosticPlatformDefaultApplied = true;
        Save();
        ConfigReloaded();
    }

    /// <summary>
    /// 读取旧版配置文件。
    /// </summary>
    private static LegacyConfigData? TryLoadLegacyConfig()
    {
        string legacyConfigPath = GetLegacyConfigPath();
        if (!File.Exists(legacyConfigPath))
        {
            return null;
        }

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        string json = File.ReadAllText(legacyConfigPath);
        return JsonSerializer.Deserialize<LegacyConfigData>(json, options);
    }

    /// <summary>
    /// 获取旧版配置文件路径。
    /// </summary>
    private static string GetLegacyConfigPath()
    {
        string? assemblyPath = typeof(GmModConfig).Assembly.Location;
        string baseDirectory = string.IsNullOrWhiteSpace(assemblyPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory;

        return Path.Combine(baseDirectory, ConfigFileName);
    }

    /// <summary>
    /// 获取诊断自动导出的平台默认值。
    /// </summary>
    private static bool GetDefaultDiagnosticExportEnabled()
    {
        return !IsMobilePlatform();
    }

    /// <summary>
    /// 判断当前系统是否属于移动端，优先使用 .NET 平台判断，补充读取 Godot 平台名。
    /// </summary>
    private static bool IsMobilePlatform()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS())
        {
            return true;
        }

        string systemName = GetGodotSystemName();
        return systemName.Contains("Android", StringComparison.OrdinalIgnoreCase)
               || systemName.Contains("iOS", StringComparison.OrdinalIgnoreCase)
               || systemName.Contains("iPhoneOS", StringComparison.OrdinalIgnoreCase)
               || systemName.Contains("iPadOS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 安全读取 Godot 平台名；读取失败时返回空字符串。
    /// </summary>
    private static string GetGodotSystemName()
    {
        try
        {
            return Godot.OS.GetName() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 获取玩家配置的最低导出等级排序值。
    /// </summary>
    private static int GetMinimumDiagnosticExportSeverityRank()
    {
        return DiagnosticExportMinimumLevel switch
        {
            GmDiagnosticExportLogLevel.Info => 1,
            GmDiagnosticExportLogLevel.Warn => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// 获取游戏日志等级排序值，值越大代表越严重。
    /// </summary>
    private static int GetSeverityRank(LogLevel level)
    {
        return level switch
        {
            LogLevel.Info => 1,
            LogLevel.Warn => 2,
            LogLevel.Error => 3,
            _ => 0,
        };
    }

    /// <summary>
    /// 处理 BaseLib 配置页里的即时修改。
    /// </summary>
    private static void HandleConfigChanged(object? sender, EventArgs e)
    {
        RuntimeConfigChanged?.Invoke();
    }

    /// <summary>
    /// 处理配置重新加载后的同步通知。
    /// </summary>
    private static void HandleConfigReloaded()
    {
        RuntimeConfigChanged?.Invoke();
    }
}
