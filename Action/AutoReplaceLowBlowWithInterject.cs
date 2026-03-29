using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoReplaceLowBlowWithInterject : ModuleBase
{
    private const uint LowBlowAction = 7540;

    private static readonly CompSig                           IsActionReplaceableSig = new("E8 ?? ?? ?? ?? 84 C0 74 68 8B D3");
    private static          Hook<IsActionReplaceableDelegate> IsActionReplaceableHook;

    private static readonly CompSig                           GetAdjustedActionIDSig = new("E8 ?? ?? ?? ?? 89 03 8B 03");
    private static          Hook<GetAdjustedActionIDDelegate> GetAdjustedActionIDHook;

    private static readonly CompSig                        GetIconIDForSlotSig = new("E8 ?? ?? ?? ?? 85 C0 89 83 ?? ?? ?? ?? 0F 94 C0");
    private static          Hook<GetIconIDForSlotDelegate> GetIconIDForSlotHook;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoReplaceLowBlowWithInterjectTitle"),
        Description = Lang.Get("AutoReplaceLowBlowWithInterjectDescription"),
        Category    = ModuleCategory.Action
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init()
    {
        IsActionReplaceableHook ??= IsActionReplaceableSig.GetHook<IsActionReplaceableDelegate>(IsActionReplaceableDetour);
        IsActionReplaceableHook.Enable();

        GetAdjustedActionIDHook ??= GetAdjustedActionIDSig.GetHook<GetAdjustedActionIDDelegate>(GetAdjustedActionIDDetour);
        GetAdjustedActionIDHook.Enable();

        GetIconIDForSlotHook ??= GetIconIDForSlotSig.GetHook<GetIconIDForSlotDelegate>(GetIconIDForSlotDetour);
        GetIconIDForSlotHook.Enable();
    }

    private static bool IsActionReplaceableDetour(uint actionID) =>
        actionID == LowBlowAction || IsActionReplaceableHook.Original(actionID);

    private static uint GetAdjustedActionIDDetour(ActionManager* manager, uint actionID) =>
        actionID == LowBlowAction && IsReplaceNeeded() ? 7538 : GetAdjustedActionIDHook.Original(manager, actionID);

    private static uint GetIconIDForSlotDetour
    (
        RaptureHotbarModule.HotbarSlot*    slot,
        RaptureHotbarModule.HotbarSlotType type,
        uint                               actionID
    ) =>
        type == RaptureHotbarModule.HotbarSlotType.Action && actionID == LowBlowAction && IsReplaceNeeded()
            ? 808
            : GetIconIDForSlotHook.Original(slot, type, actionID);

    private static bool IsReplaceNeeded() =>
        ActionManager.Instance()->IsActionOffCooldown(ActionType.Action, 7538) &&
        TargetManager.Target is IBattleChara { IsCastInterruptible: true };

    private delegate bool IsActionReplaceableDelegate(uint actionID);

    private delegate uint GetAdjustedActionIDDelegate(ActionManager* manager, uint actionID);

    private delegate uint GetIconIDForSlotDelegate(RaptureHotbarModule.HotbarSlot* slot, RaptureHotbarModule.HotbarSlotType type, uint actionID);
}
