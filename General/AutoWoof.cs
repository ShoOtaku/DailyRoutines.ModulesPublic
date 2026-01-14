using System.Collections.Frozen;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoWoof : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoWoofTitle"),
        Description = GetLoc("AutoWoofDescription"),
        Category    = ModuleCategories.General,
        Author      = ["逆光"]
    };

    private static readonly FrozenSet<ConditionFlag> InvalidConditions = [ConditionFlag.InFlight, ConditionFlag.Diving];

    protected override void Init()
    {
        TaskHelper ??= new();

        DService.Instance().Condition.ConditionChange += OnConditionChanged;
        UseActionManager.Instance().RegPostUseActionLocation(OnPostUseAction);
    }

    protected override void Uninit()
    {
        DService.Instance().Condition.ConditionChange -= OnConditionChanged;
        UseActionManager.Instance().Unreg(OnPostUseAction);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (InvalidConditions.Contains(flag))
        {
            if (value)
                TaskHelper.Abort();
            else
            {
                if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer || localPlayer.CurrentMount?.RowId != 294) return;
                TaskHelper.Enqueue(UseAction);
            }
        }

        if (flag == ConditionFlag.Mounted)
        {
            if (value)
            {
                if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer || localPlayer.CurrentMount?.RowId != 294) return;
                TaskHelper.Enqueue(UseAction);
            }
            else
                TaskHelper.Abort();
        }

    }

    private void OnPostUseAction(bool result, ActionType actionType, uint actionID, ulong targetID, Vector3 location, uint extraParam, byte a7)
    {
        if (actionType != ActionType.Action || actionID != 29463) return;
        TaskHelper.Enqueue(UseAction);
    }

    private static bool UseAction()
    {
        if (ActionManager.Instance()->GetActionStatus(ActionType.Action, 29463) != 0) return false;

        ActionManager.Instance()->UseAction(ActionType.Action, 29463);
        return true;
    }
}
