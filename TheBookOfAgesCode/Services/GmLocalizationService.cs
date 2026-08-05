using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 本地化服务，统一读取游戏语言和解析游戏内置本地化文本。
/// </summary>
public static class GmLocalizationService
{
    /// <summary>
    /// 判断当前游戏语言是否为中文。
    /// </summary>
    public static bool IsChinese => GetGameLanguage().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                                    GetGameLanguage().StartsWith("zhs", StringComparison.OrdinalIgnoreCase) ||
                                    GetGameLanguage().StartsWith("zht", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按当前游戏语言返回中文或英文文本。
    /// </summary>
    public static string Pick(string zh, string en)
    {
        return IsChinese ? zh : en;
    }

    /// <summary>
    /// 解析游戏内置的本地化文本，优先返回格式化结果，失败时再退回原始文本。
    /// </summary>
    public static string? ResolveText(LocString? locString, string logContext)
    {
        if (locString == null || locString.IsEmpty)
        {
            return null;
        }

        try
        {
            return locString.GetFormattedText();
        }
        catch
        {
            try
            {
                return locString.GetRawText();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"{logContext}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 直接读取游戏内置本地化原文，不做 SmartFormat 格式化。
    /// 适合包含运行时占位符（例如 Amount、singleStarIcon）的文本场景。
    /// </summary>
    public static string? ResolveRawText(LocString? locString, string logContext)
    {
        if (locString == null || locString.IsEmpty)
        {
            return null;
        }

        try
        {
            return locString.GetRawText();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{logContext}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 按当前语言返回职业显示名。
    /// 中文环境下把基础职业英文标识转换为中文，英文环境保持原文。
    /// 其他未知职业（例如 Mod 新增职业）保留原名。
    /// </summary>
    public static string LocalizeCharacterName(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return characterName;
        }

        if (!IsChinese)
        {
            return characterName;
        }

        string normalized = characterName.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "ironclad" => "铁甲战士",
            "silent" => "静默猎手",
            "thesilent" => "静默猎手",
            "defect" => "故障机器人",
            "thedefect" => "故障机器人",
            "watcher" => "观者",
            "thewatcher" => "观者",
            _ => normalized,
        };
    }

    /// <summary>
    /// 按当前语言解析遗物池对应的职业名。
    /// 优先从角色模型的本地化标题读取，找不到时再使用兜底职业名。
    /// </summary>
    public static string ResolveCharacterNameByRelicPool(RelicPoolModel? relicPool, string fallbackCharacterName)
    {
        Type? poolType = relicPool?.GetType();
        if (poolType != null)
        {
            CharacterModel? character = ModelDb.AllCharacters.FirstOrDefault(model =>
                model?.RelicPool != null &&
                model.RelicPool.GetType() == poolType);
            string? localizedName = ResolveText(character?.Title, "解析遗物池对应职业名称失败 / Failed to resolve character name by relic pool");
            if (!string.IsNullOrWhiteSpace(localizedName))
            {
                return localizedName.Trim();
            }
        }

        return LocalizeCharacterName(fallbackCharacterName);
    }

    /// <summary>
    /// 按当前语言解析药水池对应的职业名。
    /// 优先从角色模型的本地化标题读取，找不到时再使用兜底职业名。
    /// </summary>
    public static string ResolveCharacterNameByPotionPool(PotionPoolModel? potionPool, string fallbackCharacterName)
    {
        Type? poolType = potionPool?.GetType();
        if (poolType != null)
        {
            CharacterModel? character = ModelDb.AllCharacters.FirstOrDefault(model =>
                model?.PotionPool != null &&
                model.PotionPool.GetType() == poolType);
            string? localizedName = ResolveText(character?.Title, "解析药水池对应职业名称失败 / Failed to resolve character name by potion pool");
            if (!string.IsNullOrWhiteSpace(localizedName))
            {
                return localizedName.Trim();
            }
        }

        return LocalizeCharacterName(fallbackCharacterName);
    }

    /// <summary>
    /// 按当前语言解析卡池对应的职业名。
    /// 优先从角色模型 (CharacterModel.CardPool) 的本地化 Title 读取，找不到时退回 LocalizeCharacterName 兜底。
    /// 用于「卡片自助」页签的职业筛选下拉项与卡片 tooltip 中职业字段的展示。
    /// </summary>
    public static string ResolveCharacterNameByCardPool(CardPoolModel? cardPool, string fallbackCharacterName)
    {
        Type? poolType = cardPool?.GetType();
        if (poolType != null)
        {
            CharacterModel? character = ModelDb.AllCharacters.FirstOrDefault(model =>
                model?.CardPool != null &&
                model.CardPool.GetType() == poolType);
            string? localizedName = ResolveText(character?.Title, "解析卡池对应职业名称失败 / Failed to resolve character name by card pool");
            if (!string.IsNullOrWhiteSpace(localizedName))
            {
                return localizedName.Trim();
            }
        }

        return LocalizeCharacterName(fallbackCharacterName);
    }

    /// <summary>
    /// 读取游戏语言设置，读取失败时再使用 Godot 当前语言。
    /// </summary>
    private static string GetGameLanguage()
    {
        try
        {
            return LocManager.Instance?.Language ?? TranslationServer.GetLocale();
        }
        catch
        {
            return TranslationServer.GetLocale();
        }
    }
}
