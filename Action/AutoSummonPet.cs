using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DailyRoutines.ModulesPublic;

public class AutoSummonPet : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSummonPetTitle"),
        Description = GetLoc("AutoSummonPetDescription"),
        Category    = ModuleCategories.Action,
    };

    private static readonly Dictionary<uint, uint> SummonActions = new()
    {
        [28] = 17215, // 学者
        [26] = 25798, // 秘术师 / 召唤师
        [27] = 25798,
    };
    
    private static readonly HashSet<uint> InvalidContentTypes = [16, 17, 18, 19, 31, 32, 34, 35];

    protected override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeoutMS = 30_000 };

        DService.ClientState.TerritoryChanged += OnZoneChanged;
        DService.DutyState.DutyRecommenced += OnDutyRecommenced;
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
        
        if (!IsValidPVEDuty()) return;

        TaskHelper.DelayNext(1_000);
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private unsafe bool CheckCurrentJob()
    {
        if (BetweenAreas || !UIModule.IsScreenReady() || DService.Condition[ConditionFlag.Casting] ||
            DService.ObjectTable.LocalPlayer is not { IsTargetable: true } localPlayer) return false;

        if (!SummonActions.TryGetValue(LocalPlayerState.ClassJob, out var actionID))
        {
            TaskHelper.Abort();
            return true;
        }
        
        var state = CharacterManager.Instance()->LookupPetByOwnerObject(localPlayer.ToStruct()) != null;
        if (state)
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Enqueue(() => UseActionManager.UseAction(ActionType.Action, actionID));
        TaskHelper.DelayNext(1_000);
        TaskHelper.Enqueue(CheckCurrentJob);
        return true;
    }

    private static bool IsValidPVEDuty() =>
        !GameState.IsInPVPArea &&
        (GameState.ContentFinderCondition == 0 ||
         !InvalidContentTypes.Contains(GameState.ContentFinderConditionData.ContentType.RowId));

    protected override void Uninit()
    {
        DService.DutyState.DutyRecommenced -= OnDutyRecommenced;
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
    }
}
