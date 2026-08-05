namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// GM 功能枚举，用于多人模式下统一判断某个功能是否允许执行。
/// </summary>
public enum GmFeature
{
    GrantCard,
    RemoveCard,
    UpgradeCard,
    GrantRelic,
    RemoveRelic,
    GrantPotion,
    SetPotionSlots,
    GrantOrb,
    ToggleBlight,
    EnchantCard,
    ApplyPower,
    SpawnMonster,
    TriggerEvent,
    RoomTravel,
    ActTravel,
    Chronicle,
}
