using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoClaimItemIgnoringMismatchJobAndLevel : DailyModuleBase
{
    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };
    
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoClaimItemIgnoringMismatchJobAndLevelTitle"),
        Description = GetLoc("AutoClaimItemIgnoringMismatchJobAndLevelDescription"),
        Category    = ModuleCategories.UIOperation
    };

    protected override void Init()
    {
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnAddon);
        if (SelectYesno->IsAddonAndNodesReady()) 
            OnAddon(AddonEvent.PostSetup, null);
    }

    private static void OnAddon(AddonEvent type, AddonArgs? args)
    {
        if (!SelectYesno->IsAddonAndNodesReady()) return;
        
        ClickSelectYesnoYes
        ([
            LuminaWrapper.GetAddonText(1962), 
            LuminaWrapper.GetAddonText(2436), 
            LuminaWrapper.GetAddonText(11502), 
            LuminaWrapper.GetAddonText(11508)
        ]);
    }

    protected override void Uninit() => DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
}
