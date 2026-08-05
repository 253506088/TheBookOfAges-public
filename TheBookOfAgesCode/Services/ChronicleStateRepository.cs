using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 岁月史书页签持久化状态仓库；负责按当前 profile + 单/多人模式定位 JSON 文件并做读、写、删。
/// 文件落到游戏自己的 saves 目录下（和 current_run.save 同目录），实现"游戏存档没了 Mod 配置也没了"。
/// </summary>
public static class ChronicleStateRepository
{
    /// <summary>
    /// 单人模式下的 Mod 配置文件名；带 the_book_of_ages 前缀方便玩家或维护者一眼认出归属。
    /// </summary>
    public const string SingleplayerFileName = "the_book_of_ages_chronicle.json";

    /// <summary>
    /// 多人模式下的 Mod 配置文件名；和单人分开存，避免互相覆盖。
    /// </summary>
    public const string MultiplayerFileName = "the_book_of_ages_chronicle_mp.json";

    /// <summary>
    /// 游戏自带 saves 目录名；与 UserDataPathProvider.SavesDir 常量一致，复制一份避免运行时反射开销。
    /// </summary>
    private const string SavesDirName = "saves";

    /// <summary>
    /// 原子写时的临时文件后缀，先写 .tmp 再 rename，避免半写文件导致下次加载失败。
    /// </summary>
    private const string TempFileSuffix = ".tmp";

    /// <summary>
    /// JSON 序列化选项；缩进格式便于玩家或维护者直接打开查看，属性大小写不敏感方便兼容历史字段名。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 计算当前 profile + 单/多人模式对应的 Mod 配置文件完整 user:// 路径。
    /// SaveManager 未初始化（玩家还没选 profile）时返回 false，避免访问 CurrentProfileId 抛异常。
    /// </summary>
    public static bool TryGetCurrentFilePath(out string filePath)
    {
        return TryGetFilePath(GmRunContextService.IsMultiplayer(), out filePath);
    }

    /// <summary>
    /// 计算指定模式（多人或单人）的 Mod 配置文件完整 user:// 路径。
    /// 拆出来是为了让 Run 结束 Patch 能精确清掉对应模式的文件，不连带删另一个。
    /// </summary>
    public static bool TryGetFilePath(bool multiplayer, out string filePath)
    {
        filePath = string.Empty;
        try
        {
            if (SaveManager.Instance == null)
            {
                MainFile.Logger.Info("岁月史书持久化跳过：SaveManager 实例为空，可能 Mod 加载时机过早。");
                return false;
            }

            int profileId = SaveManager.Instance.CurrentProfileId;
            string savesDir = UserDataPathProvider.GetProfileScopedPath(profileId, SavesDirName);
            string fileName = multiplayer ? MultiplayerFileName : SingleplayerFileName;
            filePath = $"{savesDir}/{fileName}";
            return true;
        }
        catch (Exception ex)
        {
            // CurrentProfileId 在 InitProfileId 调用前访问会抛 InvalidOperationException；这是正常时机问题，不视为错误。
            MainFile.Logger.Info($"岁月史书持久化计算路径失败：多人={multiplayer}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 读取当前 profile + 单/多人模式对应的 JSON；文件不存在或解析失败时返回 null，调用方按默认值兜底。
    /// </summary>
    public static ChronicleSavedState? TryLoad()
    {
        if (!TryGetCurrentFilePath(out string filePath))
        {
            return null;
        }

        if (!Godot.FileAccess.FileExists(filePath))
        {
            MainFile.Logger.Info($"岁月史书持久化加载跳过：文件不存在，路径={filePath}。");
            return null;
        }

        try
        {
            string jsonText = ReadAllTextWithGodot(filePath);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                MainFile.Logger.Info($"岁月史书持久化加载跳过：文件内容为空，路径={filePath}。");
                return null;
            }

            ChronicleSavedState? state = JsonSerializer.Deserialize<ChronicleSavedState>(jsonText, JsonOptions);
            if (state == null)
            {
                MainFile.Logger.Info($"岁月史书持久化加载跳过：反序列化结果为空，路径={filePath}。");
                return null;
            }

            MainFile.Logger.Info($"岁月史书持久化加载成功：路径={filePath}，version={state.Version}，锁定数量={state.LockedValues.Count}。");
            return state;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书持久化加载失败：路径={filePath}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
            return null;
        }
    }

    /// <summary>
    /// 把当前状态写入对应 profile + 单/多人模式的 JSON；先写 .tmp 再 rename 保证半写不丢。
    /// </summary>
    public static bool TrySave(ChronicleSavedState state)
    {
        if (!TryGetCurrentFilePath(out string filePath))
        {
            return false;
        }

        string tempPath = filePath + TempFileSuffix;
        try
        {
            EnsureDirectoryFor(filePath);
            string jsonText = JsonSerializer.Serialize(state, JsonOptions);
            if (!WriteAllTextWithGodot(tempPath, jsonText))
            {
                MainFile.Logger.Info($"岁月史书持久化保存失败：临时文件写入失败，路径={tempPath}。");
                return false;
            }

            // 目标文件已存在时 RenameAbsolute 可能因平台行为不一致而失败；先删旧文件再 rename 最稳。
            if (Godot.FileAccess.FileExists(filePath))
            {
                Godot.DirAccess.RemoveAbsolute(filePath);
            }

            Godot.Error renameError = Godot.DirAccess.RenameAbsolute(tempPath, filePath);
            if (renameError != Godot.Error.Ok)
            {
                MainFile.Logger.Info($"岁月史书持久化保存失败：临时文件改名失败，error={renameError}，临时={tempPath}，目标={filePath}。");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书持久化保存异常：路径={filePath}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 删除指定模式（多人或单人）的 Mod 配置文件；供 Run 结束 Patch 在游戏存档被删时调用。
    /// </summary>
    public static bool TryDelete(bool multiplayer)
    {
        if (!TryGetFilePath(multiplayer, out string filePath))
        {
            return false;
        }

        try
        {
            if (!Godot.FileAccess.FileExists(filePath))
            {
                return true;
            }

            Godot.Error error = Godot.DirAccess.RemoveAbsolute(filePath);
            if (error != Godot.Error.Ok)
            {
                MainFile.Logger.Info($"岁月史书持久化删除失败：error={error}，路径={filePath}。");
                return false;
            }

            MainFile.Logger.Info($"岁月史书持久化删除成功：路径={filePath}。");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书持久化删除异常：路径={filePath}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 用 Godot.FileAccess 读出全部文本；和 System.IO 走的不是同一套底层，但能正确处理 user:// 沙盒路径。
    /// </summary>
    private static string ReadAllTextWithGodot(string filePath)
    {
        using Godot.FileAccess file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Godot.Error openError = Godot.FileAccess.GetOpenError();
            throw new IOException($"Godot.FileAccess.Open 读取失败：error={openError}");
        }

        return file.GetAsText();
    }

    /// <summary>
    /// 用 Godot.FileAccess 把文本写入指定路径；失败时返回 false，调用方按需记录日志。
    /// </summary>
    private static bool WriteAllTextWithGodot(string filePath, string content)
    {
        using Godot.FileAccess file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Godot.Error openError = Godot.FileAccess.GetOpenError();
            MainFile.Logger.Info($"岁月史书持久化写文件失败：error={openError}，路径={filePath}。");
            return false;
        }

        file.StoreString(content);
        return true;
    }

    /// <summary>
    /// 确保目标文件所在目录已存在；Godot 的 MakeDirRecursiveAbsolute 已能在目录存在时无声返回。
    /// </summary>
    private static void EnsureDirectoryFor(string filePath)
    {
        int lastSlash = filePath.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            return;
        }

        string dirPath = filePath[..lastSlash];
        Godot.Error error = Godot.DirAccess.MakeDirRecursiveAbsolute(dirPath);
        if (error != Godot.Error.Ok && error != Godot.Error.AlreadyExists)
        {
            MainFile.Logger.Info($"岁月史书持久化创建目录返回非 OK 错误：error={error}，路径={dirPath}。");
        }
    }
}
