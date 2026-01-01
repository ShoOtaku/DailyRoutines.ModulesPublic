using System.Collections.Generic;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoPeloton : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoPelotonTitle"),
        Description = GetLoc("AutoPelotonDescription"),
        Category    = ModuleCategories.Action,
        Author      = ["yamiYori"]
    };
    
    // 诗人 机工 舞者
    private static readonly HashSet<uint> ValidClassJobs = [23, 31, 38];

    private const uint PELOTONING_ACTION_ID = 7557;

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        TaskHelper   ??= new();
        ModuleConfig =   LoadConfig<Config>() ?? new();

        LocalPlayerState.PlayerMoveStateChanged += OnMoveStateChanged;
        PlayerStatusManager.RegLoseStatus(OnLoseStatus);
    }

    protected override void Uninit()
    {
        LocalPlayerState.PlayerMoveStateChanged -= OnMoveStateChanged;
        PlayerStatusManager.Unreg(OnLoseStatus);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OnlyInDuty"), ref ModuleConfig.OnlyInDuty))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("AutoPeloton-DisableInWalk"), ref ModuleConfig.DisableInWalk))
            SaveConfig(ModuleConfig);
    }
    
    private void OnLoseStatus(BattleChara* player, ushort id, ushort param, ushort stackCount, ulong sourceID)
    {
        if ((nint)player != LocalPlayerState.Object?.Address) return;
        if (id != 1199 && id != 50) return;
        
        CheckAndUsePeloton();
    }

    private void OnMoveStateChanged(bool isMoving)
    {
        if (!isMoving) return;
        CheckAndUsePeloton();
    }

    private void CheckAndUsePeloton()
    {
        if (ModuleConfig.OnlyInDuty && GameState.ContentFinderCondition == 0) return;
        if (GameState.IsInPVPArea) return;
        if (DService.Condition[ConditionFlag.InCombat]) return;
        if (BetweenAreas || !IsScreenReady() || OccupiedInEvent || DService.ObjectTable.LocalPlayer is not { } localPlayer)
            return;
        if (!ValidClassJobs.Contains(localPlayer.ClassJob.RowId))
            return;
        if (!IsActionUnlocked(PELOTONING_ACTION_ID))
            return;
        if (ModuleConfig.DisableInWalk && LocalPlayerState.IsWalking)
            return;
        
        TaskHelper.Abort();
        TaskHelper.Enqueue(UsePeloton, $"UseAction_{PELOTONING_ACTION_ID}", 5_000, true, 1);
    }

    private bool? UsePeloton()
    {
        if (DService.ObjectTable.LocalPlayer is not { } localPlayer) return false;
        var actionManager = ActionManager.Instance();
        var statusManager = localPlayer.ToStruct()->StatusManager;

        if (actionManager->GetActionStatus(ActionType.Action, PELOTONING_ACTION_ID) != 0) return false;
        if (statusManager.HasStatus(1199) || statusManager.HasStatus(50)) return true;
        if (!LocalPlayerState.IsMoving) return true;

        TaskHelper.Enqueue(() => UseActionManager.UseAction(ActionType.Action, PELOTONING_ACTION_ID),
                           $"UseAction_{PELOTONING_ACTION_ID}",
                           5_000,
                           true,
                           1);
        return true;
    }
    
    private class Config : ModuleConfiguration
    {
        public bool OnlyInDuty    = true;
        public bool DisableInWalk = true;
    }
}
