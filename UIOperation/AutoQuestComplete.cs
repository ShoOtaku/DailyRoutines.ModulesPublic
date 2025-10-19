using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace DailyRoutines.ModulesPublic;

public class AutoQuestComplete : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoQuestCompleteTitle"),
        Description = GetLoc("AutoQuestCompleteDescription"),
        Category    = ModuleCategories.UIOperation,
    };

    protected override void Init()
    {
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "JournalResult", OnAddonJournalResultSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,  "JournalResult", OnAddonJournalResultSetup);
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SatisfactionSupplyResult", OnAddonSatisfactionSupplyResultSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,  "SatisfactionSupplyResult", OnAddonSatisfactionSupplyResultSetup);
    }
    
    private static unsafe void OnAddonJournalResultSetup(AddonEvent type, AddonArgs args)
    {
        var addon = JournalResult;
        if (addon == null) return;

        var itemID = addon->AtkValues[82].UInt;
        if (itemID == 0)
        {
            Callback(addon, true, 0, 0);
            return;
        }
        
        Callback(addon, true, 0, itemID);
    }
    
    private static unsafe void OnAddonSatisfactionSupplyResultSetup(AddonEvent type, AddonArgs args)
    {
        var addon = SatisfactionSupplyResult;
        if (addon == null) return;

        Callback(addon, true, 1);
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddonSatisfactionSupplyResultSetup);
        DService.AddonLifecycle.UnregisterListener(OnAddonJournalResultSetup);
    }
}
