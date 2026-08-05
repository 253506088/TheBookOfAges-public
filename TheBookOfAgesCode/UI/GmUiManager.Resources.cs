using Godot;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 资源读取辅助：统一处理 PCK 内资源优先、磁盘文件兜底的图标加载逻辑。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 读取 GM UI 图标；优先从 Godot 资源路径读取，找不到时再回退到磁盘文件。
    /// </summary>
    private static Texture2D? LoadGmUiTexture(string fileName)
    {
        if (GmUiTextureCache.TryGetValue(fileName, out Texture2D? cachedTexture))
        {
            return cachedTexture;
        }

        Texture2D? texture = LoadGmUiTextureFromResource(fileName) ?? LoadGmUiTextureFromFile(fileName);
        GmUiTextureCache[fileName] = texture;
        return texture;
    }

    /// <summary>
    /// 从 PCK 或项目资源树读取 GM UI 图标。
    /// </summary>
    private static Texture2D? LoadGmUiTextureFromResource(string fileName)
    {
        string resourcePath = $"{GmUiIconResourceFolder}/{fileName}";
        if (!ResourceLoader.Exists(resourcePath))
        {
            return null;
        }

        return ResourceLoader.Load<Texture2D>(resourcePath);
    }

    /// <summary>
    /// 从磁盘文件兜底读取 GM UI 图标，兼容未打 PCK 的调试场景。
    /// </summary>
    private static Texture2D? LoadGmUiTextureFromFile(string fileName)
    {
        foreach (string path in GetGmUiTextureSearchPaths(fileName))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            Image image = Image.LoadFromFile(path);
            if (image.IsEmpty())
            {
                continue;
            }

            return ImageTexture.CreateFromImage(image);
        }

        return null;
    }

    /// <summary>
    /// 生成 GM UI 图标的候选磁盘路径，兼容旧的散装图片目录与新的资源目录结构。
    /// </summary>
    private static IEnumerable<string> GetGmUiTextureSearchPaths(string fileName)
    {
        string? assemblyDirectory = Path.GetDirectoryName(typeof(MainFile).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            yield return Path.Combine(assemblyDirectory, "TheBookOfAges", "ui", "gm_icons", fileName);
            yield return Path.Combine(assemblyDirectory, GmButtonIconFolderName, fileName);
        }

        yield return Path.Combine(AppContext.BaseDirectory, "TheBookOfAges", "ui", "gm_icons", fileName);
        yield return Path.Combine(AppContext.BaseDirectory, GmButtonIconFolderName, fileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), "TheBookOfAges", "ui", "gm_icons", fileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), GmButtonIconFolderName, fileName);
    }
}
