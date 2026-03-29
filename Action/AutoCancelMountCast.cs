using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.Info.Game.Enums;
using OmenTools.OmenService;
using OmenTools.Threading;

namespace DailyRoutines.ModulesPublic;

public class AutoCancelMountCast : ModuleBase
{
    private static Config ModuleConfig = null!;

    private static CancellationTokenSource? CancelWhenMoveCancelSource;

    private static bool IsOnMountCasting;

    public override ModuleInfo Info { get; } = new()
    {
        Title            = Lang.Get("AutoCancelMountCastTitle"),
        Description      = Lang.Get("AutoCancelMountCastDescription"),
        Category         = ModuleCategory.Action,
        Author           = ["Bill"],
        ModulesRecommend = ["BetterMountRoulette"]
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        DService.Instance().Condition.ConditionChange += OnConditionChanged;
        UseActionManager.Instance().RegPreUseAction(OnPreUseAction);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("AutoCancelMountCast-CancelWhenUseAction"), ref ModuleConfig.CancelWhenUsection))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox(Lang.Get("AutoCancelMountCast-CancelWhenMove"), ref ModuleConfig.CancelWhenMove))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox(Lang.Get("AutoCancelMountCast-CancelWhenJump"), ref ModuleConfig.CancelWhenJump))
            ModuleConfig.Save(this);
    }

    protected override void Uninit()
    {
        DService.Instance().Condition.ConditionChange -= OnConditionChanged;
        UseActionManager.Instance().Unreg(OnPreUseAction);

        OnConditionChanged(ConditionFlag.Casting, false);
    }

    private static void OnConditionChanged(ConditionFlag flag, bool value)
    {
        switch (flag)
        {
            case ConditionFlag.Casting:
                switch (value)
                {
                    case true:
                        if (DService.Instance().ObjectTable.LocalPlayer is { } localPlayer &&
                            (localPlayer.CastActionType == ActionType.Mount ||
                             localPlayer is { CastActionType: ActionType.GeneralAction, CastActionID: 9 }))
                        {
                            IsOnMountCasting = true;

                            CancelWhenMoveCancelSource = new();
                            DService.Instance().Framework.RunOnTick
                            (
                                async () =>
                                {
                                    while (ModuleConfig.CancelWhenMove && IsOnMountCasting && !CancelWhenMoveCancelSource.IsCancellationRequested)
                                    {
                                        if (LocalPlayerState.Instance().IsMoving)
                                            ExecuteCancelCast();

                                        await Task.Delay(10, CancelWhenMoveCancelSource.Token);
                                    }
                                },
                                cancellationToken: CancelWhenMoveCancelSource.Token
                            ).ContinueWith(t => t.Dispose());
                        }

                        break;
                    case false:
                        IsOnMountCasting = false;

                        CancelWhenMoveCancelSource?.Cancel();
                        CancelWhenMoveCancelSource?.Dispose();
                        CancelWhenMoveCancelSource = null;
                        break;
                }

                break;
            case ConditionFlag.Jumping:
                if (!ModuleConfig.CancelWhenJump || !value) return;

                ExecuteCancelCast();
                break;
        }
    }

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
        if (!ModuleConfig.CancelWhenUsection || !IsOnMountCasting) return;

        ExecuteCancelCast();
    }

    private static void ExecuteCancelCast()
    {
        if (Throttler.Shared.Throttle("CancelMountCast-CancelCast", 100))
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.CancelCast);
    }

    private class Config : ModuleConfig
    {
        public bool CancelWhenJump;
        public bool CancelWhenMove;
        public bool CancelWhenUsection = true;
    }
}
