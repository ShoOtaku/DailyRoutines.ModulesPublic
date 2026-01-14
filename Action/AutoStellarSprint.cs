using DailyRoutines.Abstracts;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace DailyRoutines.ModulesPublic;

public class AutoStellarSprint : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoStellarSprintTitle"),
        Description = GetLoc("AutoStellarSprintDescription"),
        Category    = ModuleCategories.Action,
        Author      = ["Due"]
    };

    private const uint STELLAR_SPRINT = 43357;
    private const uint SPRINT_STATUS  = 4398;

    protected override void Init()
    {
        DService.Instance().ClientState.TerritoryChanged += OnZoneChange;
        OnZoneChange(0);
    }
    
    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChange;
        FrameworkManager.Instance().Unreg(OnUpdate);
        PlayerStatusManager.Instance().Unreg(OnLoseStatus);
    }

    private static void OnZoneChange(ushort zone)
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        PlayerStatusManager.Instance().Unreg(OnLoseStatus);

        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.CosmicExploration) return;
        
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 2_000);
        PlayerStatusManager.Instance().RegLose(OnLoseStatus);
    }

    private static void OnLoseStatus(IBattleChara player, ushort id, ushort param, ushort stackCount, ulong sourceID)
    {
        if (player.EntityID != LocalPlayerState.EntityID) return;

        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.CosmicExploration)
        {
            PlayerStatusManager.Instance().Unreg(OnLoseStatus);
            return;
        }
        
        PlayerStatusManager.Instance().Unreg(OnLoseStatus);
        
        FrameworkManager.Instance().Unreg(OnUpdate);
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 2_000);
    }

    private static void OnUpdate(IFramework _)
    {
        if (BetweenAreas || OccupiedInEvent) return;

        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.CosmicExploration)
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }
        
        if (LocalPlayerState.HasStatus(SPRINT_STATUS, out var _))
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            
            PlayerStatusManager.Instance().Unreg(OnLoseStatus);
            PlayerStatusManager.Instance().RegLose(OnLoseStatus);
            return;
        }
        
        var jobCategory = LuminaGetter.GetRowOrDefault<ClassJob>(LocalPlayerState.ClassJob).ClassJobCategory.RowId;
        if (jobCategory is not (32 or 33)) return;
        
        UseActionManager.Instance().UseAction(ActionType.Action, STELLAR_SPRINT);
    }
}
