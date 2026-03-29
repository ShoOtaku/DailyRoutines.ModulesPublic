using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

// TODO: 合并成单一投影台模块
public class AutoSwitchGlamourJobCategory : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoSwitchGlamourJobCategoryTitle"),
        Description = Lang.Get("AutoSwitchGlamourJobCategoryDescription"),
        Category    = ModuleCategory.UIOptimization,
        Author      = ["ECSS11"]
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init() =>
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreSetup, "MiragePrismPrismBox", OnMiragePrismPrismBox);

    private static unsafe void OnMiragePrismPrismBox(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonMiragePrismPrismBox*)MiragePrismPrismBox;
        if (addon == null) return;

        addon->Param = (int)LocalPlayerState.ClassJob;
    }

    protected override void Uninit() =>
        DService.Instance().AddonLifecycle.UnregisterListener(AddonEvent.PreSetup, "MiragePrismPrismBox");
}
