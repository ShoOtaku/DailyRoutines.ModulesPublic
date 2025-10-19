using System;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.ModulesPublic;

public class AutoUfoCatcher : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoTMPTitle"),
        Description = GetLoc("AutoTMPDescription"),
        Category    = ModuleCategories.GoldSaucer,
    };

    protected override void Init()
    {
        TaskHelper ??= new();
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "UfoCatcher", OnAddonSetup);
    }

    protected override void ConfigUI() => ConflictKeyText();

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (InterruptByConflictKey(TaskHelper, this)) return;
        TaskHelper.Enqueue(WaitSelectStringAddon);
        TaskHelper.Enqueue(ClickGameButton);
    }

    private unsafe bool? WaitSelectStringAddon()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        return TryGetAddonByName<AddonSelectString>("SelectString", out var addon) &&
               IsAddonAndNodesReady(&addon->AtkUnitBase) && ClickSelectString(0);
    }

    private unsafe bool? ClickGameButton()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;

        if (!IsAddonAndNodesReady(UFOCatcher))
            return false;

        var button = UFOCatcher->GetComponentButtonById(2);
        if (button == null || !button->IsEnabled) return false;

        UFOCatcher->IsVisible = false;

        Callback(UFOCatcher, true, 11, 3, 0);

        // 只是纯粹因为游玩动画太长了而已
        TaskHelper.DelayNext(5000);
        TaskHelper.Enqueue(StartAnotherRound);
        return true;
    }

    private unsafe bool? StartAnotherRound()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        if (OccupiedInEvent) return false;
        
        var machineTarget = DService.Targets.PreviousTarget;
        var machine = machineTarget.Name.TextValue.Contains(LuminaGetter.GetRow<EObjName>(2005036)!.Value.Singular.ExtractText(),
                                                            StringComparison.OrdinalIgnoreCase)
                          ? (GameObject*)machineTarget.Address
                          : null;

        if (machine != null)
        {
            TargetSystem.Instance()->InteractWithObject(machine);
            return true;
        }

        return false;
    }

    protected override void Uninit() => 
        DService.AddonLifecycle.UnregisterListener(OnAddonSetup);
}
