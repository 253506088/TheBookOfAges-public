using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode;

//You're recommended but not required to keep all your code in this package and all your assets in the TheBookOfAges folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "TheBookOfAges"; //At the moment, this is used only for the Logger and harmony names.

    public static GmLogger Logger { get; } = new(ModId);

    /// <summary>
    /// 已订阅 RunStarted 标志位，防止热重载 / 多次 Initialize 导致重复订阅。
    /// </summary>
    private static bool _hasSubscribedRunStarted;

    /// <summary>
    /// 等待 RunManager.Instance 出现的最大尝试次数，配合下面的间隔决定总等待时长。
    /// </summary>
    private const int RunManagerHookMaxAttempts = 60;

    /// <summary>
    /// 等待 RunManager.Instance 出现的尝试间隔毫秒数；60 次 * 500ms = 30 秒兜底。
    /// </summary>
    private const int RunManagerHookIntervalMs = 500;
    private const string GmResourcePackFileName = "TheBookOfAges.pck";
    private const string GmResourceProbePath = "res://TheBookOfAges/ui/gm_icons/遗物自助.png";
    private static bool _hasTriedLoadGmResourcePack;

    public static void Initialize()
    {
        Harmony harmony = new(ModId);

        harmony.PatchAll();

        GmModConfig.Register(ModId);
        Logger.ReloadConfig();
        GmDiagnosticMonitorService.Start(ModId);
        TryLoadGmResourcePack();

        // 启动持久化订阅；走 fire-and-forget 是为了不阻塞 Mod 初始化，订阅成功与否都不影响其它功能。
        _ = TryHookRunStartedAsync();

        Logger.Info("TheBookOfAges / GM Console initialized. GM 管理页面已初始化。");
    }

    /// <summary>
    /// 异步等待 RunManager.Instance 出现并订阅 RunStarted；如果 Mod 加载时已经在 Run 中，主动触发一次加载。
    /// </summary>
    private static async Task TryHookRunStartedAsync()
    {
        for (int attempt = 0; attempt < RunManagerHookMaxAttempts; attempt++)
        {
            try
            {
                if (RunManager.Instance != null)
                {
                    if (!_hasSubscribedRunStarted)
                    {
                        RunManager.Instance.RunStarted += OnRunStarted;
                        _hasSubscribedRunStarted = true;
                        Logger.Info($"岁月史书 RunStarted 订阅成功（尝试次数={attempt + 1}）。");

                        // Mod 热重载或晚加载场景：订阅时玩家可能已经在 Run 中，主动触发一次让持久化状态生效。
                        TryTriggerInitialLoad();
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"岁月史书 RunStarted 订阅尝试异常：第 {attempt + 1} 次，异常类型={ex.GetType().Name}，异常={ex.Message}。");
            }

            await Task.Delay(RunManagerHookIntervalMs);
        }

        Logger.Info($"岁月史书 RunStarted 订阅放弃：尝试 {RunManagerHookMaxAttempts} 次后 RunManager 仍为空。");
    }

    /// <summary>
    /// 在 Mod 初始化阶段主动挂载相邻的 PCK，避免运行时读取不到页签图标等 UI 资源。
    /// </summary>
    private static void TryLoadGmResourcePack()
    {
        if (_hasTriedLoadGmResourcePack)
        {
            return;
        }

        _hasTriedLoadGmResourcePack = true;

        try
        {
            if (ResourceLoader.Exists(GmResourceProbePath))
            {
                Logger.Info($"GM 资源包探测成功：已可直接访问 {GmResourceProbePath}。");
                return;
            }

            string? packPath = FindGmResourcePackPath();
            if (string.IsNullOrEmpty(packPath))
            {
                Logger.Info($"GM 资源包挂载跳过：未找到 {GmResourcePackFileName}。");
                return;
            }

            bool loaded = ProjectSettings.LoadResourcePack(packPath.Replace('\\', '/'));
            bool resourceReady = ResourceLoader.Exists(GmResourceProbePath);
            Logger.Info($"GM 资源包挂载结果：文件={packPath}，LoadResourcePack={loaded}，资源可见={resourceReady}。");
        }
        catch (Exception ex)
        {
            Logger.Info($"GM 资源包挂载异常：异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 按运行时常见目录顺序查找与当前 DLL 相邻的 PCK 文件。
    /// </summary>
    private static string? FindGmResourcePackPath()
    {
        foreach (string path in GetGmResourcePackSearchPaths())
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 生成 GM 资源包的候选路径，兼容游戏实际运行目录与开发调试目录。
    /// </summary>
    private static IEnumerable<string> GetGmResourcePackSearchPaths()
    {
        string? assemblyDirectory = Path.GetDirectoryName(typeof(MainFile).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            yield return Path.Combine(assemblyDirectory, GmResourcePackFileName);
            yield return Path.Combine(assemblyDirectory, ModId, GmResourcePackFileName);
        }

        yield return Path.Combine(AppContext.BaseDirectory, ModId, GmResourcePackFileName);
        yield return Path.Combine(AppContext.BaseDirectory, GmResourcePackFileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), ModId, GmResourcePackFileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), GmResourcePackFileName);
    }

    /// <summary>
    /// 订阅完成时如果玩家已经在 Run 中，主动模拟一次 RunStarted 让持久化状态立刻生效。
    /// </summary>
    private static void TryTriggerInitialLoad()
    {
        try
        {
            RunState? state = GmRunContextService.GetCurrentRunState();
            if (state == null)
            {
                return;
            }

            Logger.Info("岁月史书订阅时玩家已在 Run 中，主动模拟一次 RunStarted 加载。");
            OnRunStarted(state);
        }
        catch (Exception ex)
        {
            Logger.Info($"岁月史书订阅时初始加载异常：异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// RunStarted 回调：读磁盘 → 写回 ChronicleService 内存 → 调用 ApplyAfterLoadAsync 让 GodMode 等 toggle 类对齐到游戏内。
    /// async void 用于 RunManager 事件签名（Action&lt;RunState&gt;）。
    /// </summary>
    private static async void OnRunStarted(RunState runState)
    {
        try
        {
            if (GmRunContextService.IsMultiplayer())
            {
                ChronicleService.Instance.ResetToDefaults();
                Logger.Info("岁月史书 RunStarted 处理：检测到多人模式，已重置运行时状态并跳过持久化加载。");
                return;
            }

            ChronicleSavedState? state = ChronicleStateRepository.TryLoad();
            if (state == null)
            {
                Logger.Info("岁月史书 RunStarted 处理：持久化文件不存在或加载失败，保持默认运行时状态。");
                return;
            }

            ChronicleService.Instance.LoadFrom(state);
            await ChronicleService.Instance.ApplyAfterLoadAsync();
        }
        catch (Exception ex)
        {
            Logger.Info($"岁月史书 RunStarted 处理异常：异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
    }
}
