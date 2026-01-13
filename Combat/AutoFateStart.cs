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
        Category    = ModuleCategories.Combat
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    protected override void Init()
    {
        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        FrameworkManager.Instance().Unreg(OnUpdate);
    }

    private static void OnZoneChanged(ushort obj)
    {
        FrameworkManager.Instance().Unreg(OnUpdate);

        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.Overworld || GameState.IsInPVPArea)
            return;

        FrameworkManager.Instance().Reg(OnUpdate, 2_000);
    }

    private static void OnUpdate(IFramework _)
    {
        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.Overworld || GameState.IsInPVPArea)
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }

        if (LocalPlayerState.ClassJobData.DohDolJobIndex != -1)
            return;

        var objs = DService.Instance().ObjectTable.SearchObjects
        (obj =>
             obj is IBattleNPC { NamePlateIconID: 60093 } battleNPC                &&
             LuminaGetter.GetRow<Fate>(battleNPC.FateID) is { ClassJobLevel: > 0 } &&
             Throttler.Throttle($"AutoFateStart-Fate-{battleNPC.FateID}", 60_000)
        );

        foreach (var o in objs)
        {
            var battleNPC = o as IBattleNPC;

            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.FateStart, battleNPC.FateID, battleNPC.EntityID);
            Chat(GetLoc("AutoFateStart-StartNotice", LuminaWrapper.GetFateName(battleNPC.FateID), battleNPC.Name.ToString()));
            break;
        }
    }
}
