using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public class AutoManageInterruptAction : ModuleBase
{
    private static readonly HashSet<uint> InterruptActions = [7538, 7551];

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoManageInterruptActionTitle"),
        Description = Lang.Get("AutoManageInterruptActionDescription"),
        Category    = ModuleCategory.Action
    };

    protected override void Init() =>
        UseActionManager.Instance().RegPreUseAction(OnPreUseAction);

    private static void OnPreUseAction
    (
        ref bool                        isPrevented,
        ref ActionType                  actionType,
        ref uint                        actionID,
        ref ulong                       targetID,
        ref uint                        extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint                        comboRouteID
    )
    {
        if (actionType != ActionType.Action || !InterruptActions.Contains(actionID)) return;
        if (TargetManager.Target is IBattleChara { IsCasting: true, IsCastInterruptible: true }) return;

        isPrevented = true;
    }

    protected override void Uninit() =>
        UseActionManager.Instance().Unreg(OnPreUseAction);
}
