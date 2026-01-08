using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.Extensions;

namespace DailyRoutines.ModulesPublic;

public class AutoSoulsow : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSoulsowTitle"),
        Description = GetLoc("AutoSoulsowDescription"),
        Category    = ModuleCategories.Action,
    };
    
    private static readonly HashSet<uint> InvalidContentTypes = [16, 17, 18, 19, 31, 32, 34, 35];

    protected override void Init()
    {
        TaskHelper ??= new() { TimeoutMS = 30_000 };

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        DService.Instance().DutyState.DutyRecommenced    += OnDutyRecommenced;
        DService.Instance().Condition.ConditionChange    += OnConditionChanged;
    }

    // 重新挑战
    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    // 进入副本
    private void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();
        
        if (GameState.ContentFinderCondition == 0) return;

        TaskHelper.Enqueue(CheckCurrentJob);
    }
    
    // 战斗状态
    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag is not ConditionFlag.InCombat) return;
        
        TaskHelper.Abort();
        if (!value) 
            TaskHelper.Enqueue(CheckCurrentJob);
    }

    private bool CheckCurrentJob()
    {
        if (BetweenAreas || !UIModule.IsScreenReady() || OccupiedInEvent) return false;
        if (DService.Instance().Condition[ConditionFlag.InCombat] || LocalPlayerState.ClassJob != 39 || !IsValidPVEDuty())
        {
            TaskHelper.Abort();
            return true;
        }
        
        TaskHelper.Enqueue(UseRelatedActions, "UseRelatedActions", 5_000, weight: 1);
        return true;
    }
    
    private bool UseRelatedActions()
    {
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return false;

        // 播魂种
        if (localPlayer.StatusList.HasStatus(2594) || !ActionManager.IsActionUnlocked(24387))
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Enqueue(() => UseActionManager.Instance().UseAction(ActionType.Action, 24387), $"UseAction_{24387}", 5_000, weight: 1);
        TaskHelper.DelayNext(2_000);
        TaskHelper.Enqueue(CheckCurrentJob, "二次检查", weight: 1);
        return true;
    }

    private static bool IsValidPVEDuty() =>
        !GameState.IsInPVPArea &&
        (GameState.ContentFinderCondition == 0 ||
         !InvalidContentTypes.Contains(GameState.ContentFinderConditionData.ContentType.RowId));

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Instance().DutyState.DutyRecommenced    -= OnDutyRecommenced;
        DService.Instance().Condition.ConditionChange    -= OnConditionChanged;
    }
}
