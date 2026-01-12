using DailyRoutines.Abstracts;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace DailyRoutines.ModulesPublic;

public class AutoFateStart : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoFateStartTitle"),
        Description = GetLoc("AutoFateStartDescription"),
        Category    = ModuleCategories.Combat,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };
    
    protected override void Init() => 
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 2_000);

    private static void OnUpdate(IFramework _)
    {
        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.Overworld ||
            GameState.IsInPVPArea                                            ||
            LocalPlayerState.ClassJobData.DohDolJobIndex != -1)
            return;
            
        foreach (var obj in DService.Instance().ObjectTable.CharacterManagerObjects)
        {
            if (obj is not IBattleNPC { NamePlateIconID: 60093 } battleNPC                        ||
                LuminaGetter.GetRow<Fate>(battleNPC.FateID) is not { ClassJobLevel: > 0 } fateRow ||
                !Throttler.Throttle($"AutoFateStart-Fate-{battleNPC.FateID}", 60_000))
                continue;
            
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.FateStart, battleNPC.FateID, battleNPC.EntityID);
            Chat(GetLoc("AutoFateStart-StartNotice", fateRow.Name.ToString(), battleNPC.Name.ToString()));
            break;
        }
    }

    protected override void Uninit() =>
        FrameworkManager.Instance().Unreg(OnUpdate);
}
