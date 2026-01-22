using System.Linq;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public class Alphascape3Helper : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("Alphascape3HelperTitle"),
        Description = GetLoc("Alphascape3HelperDescription"),
        Category    = ModuleCategories.Assist
    };

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

    private static void OnZoneChanged(ushort zoneID)
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        if (GameState.TerritoryType != 800) return;
        
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 100);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (GameState.TerritoryType != 800)
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }

        if (DService.Instance().ObjectTable.LocalPlayer is null) return;
        
        var obj = DService.Instance().ObjectTable.FirstOrDefault(x => x is { ObjectKind: ObjectKind.BattleNpc, DataID: 9638 });
        if (obj is not { IsTargetable: true }) return;

        UseActionManager.Instance().UseAction(ActionType.Action, 12911, obj.EntityID);
    }
}
