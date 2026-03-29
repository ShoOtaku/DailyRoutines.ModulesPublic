using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Interop.Game.Models;
using OmenTools.Interop.Windows.Helpers;
using OmenTools.OmenService;
using OmenTools.Threading;

namespace DailyRoutines.ModulesPublic;

public class AutoQTE : ModuleBase
{
    private static readonly CompSig                         IsInputIDPressedSig = new("E9 ?? ?? ?? ?? 83 7F 44 02");
    private static          Hook<IsInputIDPressedDelegate>? IsInputIDPressedHook;

    private static readonly string[] QTETypes = ["_QTEKeep", "_QTEMash", "_QTEKeepTime", "_QTEButton"];

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoQTETitle"),
        Description = Lang.Get("AutoQTEDescription"),
        Category    = ModuleCategory.Combat
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override unsafe void Init()
    {
        IsInputIDPressedHook ??= IsInputIDPressedSig.GetHook<IsInputIDPressedDelegate>(IsInputIDPressedDetour);
        IsInputIDPressedHook.Enable();

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostDraw, QTETypes, OnQTEAddon);
    }

    private static unsafe byte IsInputIDPressedDetour(void* data, InputId id)
    {
        var orig = IsInputIDPressedHook.Original(data, id);
        if (GameState.ContentFinderCondition == 0)
            return orig;

        if (!Throttler.Shared.Check("AutoQTE-QTE"))
            return 0;

        return orig;
    }

    private static unsafe void OnQTEAddon(AddonEvent type, AddonArgs args)
    {
        Throttler.Shared.Throttle("AutoQTE-QTE", 1_000, true);
        KeyEmulationHelper.SendKeypress(Keys.Space);
        AtkStage.Instance()->ClearFocus();
    }

    protected override void Uninit() =>
        DService.Instance().AddonLifecycle.UnregisterListener(OnQTEAddon);

    private unsafe delegate byte IsInputIDPressedDelegate(void* data, InputId id);
}
