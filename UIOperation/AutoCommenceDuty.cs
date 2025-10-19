using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DailyRoutines.ModulesPublic;

public class AutoCommenceDuty : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCommenceDutyTitle"),
        Description = GetLoc("AutoCommenceDutyDescription"),
        Category    = ModuleCategories.UIOperation,
        Author      = ["Cindy-Master"]
    };

    protected override void Init()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "ContentsFinderConfirm", OnAddonSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreDraw,   "ContentsFinderConfirm", OnAddonSetup);
    }

    private static unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (args.Addon == nint.Zero) return;
        
        var addon = args.Addon.ToAtkUnitBase();
        if (addon->AtkValues[7].UInt != 0)
            return;
        
        ((AddonContentsFinderConfirm*)addon)->CommenceButton->ClickAddonButton(addon);
    }

    protected override void Uninit() => 
        DService.AddonLifecycle.UnregisterListener(OnAddonSetup);
}
