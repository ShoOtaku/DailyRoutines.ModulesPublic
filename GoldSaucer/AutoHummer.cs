using System;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DailyRoutines.ModulesPublic;

public class AutoHummer : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCTSTitle"),
        Description = GetLoc("AutoCTSDescription"),
        Category    = ModuleCategories.GoldSaucer,
    };

    protected override void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 10000 };
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Hummer", OnAddonSetup);
    }

    protected override void ConfigUI() => ConflictKeyText();

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (InterruptByConflictKey(TaskHelper, this)) return;

        TaskHelper.Enqueue(WaitSelectStringAddon);
        TaskHelper.Enqueue(ClickGameButton);
    }

    private bool? WaitSelectStringAddon()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        return ClickSelectString(0);
    }

    private unsafe bool? ClickGameButton()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;

        if (!IsAddonAndNodesReady(Hummer))
            return false;

        var button = Hummer->GetComponentButtonById(29);
        if (button == null || !button->IsEnabled) return false;

        Hummer->IsVisible = false;

        Callback(Hummer, true, 11, 3, 0);

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
        var machine =
            machineTarget.Name.TextValue.Contains(LuminaGetter.GetRow<EObjName>(2005035)!.Value.Singular.ExtractText(),
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
