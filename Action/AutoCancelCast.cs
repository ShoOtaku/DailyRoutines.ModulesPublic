using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCancelCast : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCancelCastTitle"),
        Description = GetLoc("AutoCancelCastDescription"),
        Category    = ModuleCategories.Action
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private static readonly FrozenSet<ObjectKind> ValidObjectKinds =
    [
        ObjectKind.Player,
        ObjectKind.BattleNpc
    ];

    private static readonly FrozenSet<ConditionFlag> ValidConditions =
    [
        ConditionFlag.Casting
    ];

    private static FrozenSet<uint> TargetAreaActions { get; } =
        LuminaGetter.Get<LuminaAction>()
                    .Where(x => x.TargetArea)
                    .Select(x => x.RowId)
                    .ToFrozenSet();

    protected override void Init() =>
        DService.Instance().Condition.ConditionChange += OnConditionChanged;

    protected override void Uninit()
    {
        DService.Instance().Condition.ConditionChange -= OnConditionChanged;
        FrameworkManager.Instance().Unreg(OnUpdate);
    }

    private static void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (!ValidConditions.Contains(flag)) return;

        if (value)
            FrameworkManager.Instance().Reg(OnUpdate);
        else
            FrameworkManager.Instance().Unreg(OnUpdate);
    }

    private static void OnUpdate(IFramework _)
    {
        if (!IsCasting)
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }

        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;

        if (localPlayer.CastActionType != ActionType.Action      ||
            TargetAreaActions.Contains(localPlayer.CastActionID) ||
            !LuminaGetter.TryGetRow(localPlayer.CastActionID, out LuminaAction actionRow))
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }

        var obj = localPlayer.CastTargetObject;

        if (obj is not IBattleChara battleChara || !ValidObjectKinds.Contains(battleChara.ObjectKind))
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }

        if (!battleChara.IsTargetable)
        {
            ExecuteCancast();
            return;
        }

        if (actionRow.DeadTargetBehaviour == 0 && (battleChara.IsDead || battleChara.CurrentHp == 0))
        {
            ExecuteCancast();
            return;
        }

        if (ActionManager.CanUseActionOnTarget(localPlayer.CastActionID, obj.ToStruct()))
            return;

        ExecuteCancast();

        return;

        void ExecuteCancast()
        {
            if (Throttler.Throttle("AutoCancelCast-CancelCast", 100))
                ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.CancelCast);
        }
    }
}
