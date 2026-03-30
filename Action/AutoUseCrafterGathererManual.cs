using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoUseCrafterGathererManual : ModuleBase
{
    private static readonly HashSet<uint> Gatherers =
        LuminaGetter.Get<ClassJob>()
                    .Where(x => x.ClassJobCategory.RowId == 32)
                    .Select(x => x.RowId)
                    .ToHashSet();

    private static readonly HashSet<uint> Crafters =
        LuminaGetter.Get<ClassJob>()
                    .Where(x => x.ClassJobCategory.RowId == 33)
                    .Select(x => x.RowId)
                    .ToHashSet();

    private static readonly uint[] GathererManuals = [26553, 12668, 4635, 4633];
    private static readonly uint[] CrafterManuals  = [26554, 12667, 4634, 4632];

    private static readonly HashSet<ConditionFlag> ValidConditions =
    [
        ConditionFlag.Crafting, ConditionFlag.Gathering, ConditionFlag.Mounted
    ];

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoUseCrafterGathererManualTitle"),
        Description = Lang.Get("AutoUseCrafterGathererManualDescription"),
        Category    = ModuleCategory.General,
        Author      = ["Shiyuvi", "AtmoOmen"]
    };

    protected override void Init()
    {
        ModuleConfig =   Config.Load(this) ?? new();
        TaskHelper   ??= new() { TimeoutMS = 15_000 };

        DService.Instance().Condition.ConditionChange    += OnConditionChanged;
        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        DService.Instance().ClientState.ClassJobChanged  += OnClassJobChanged;
        DService.Instance().ClientState.LevelChanged     += OnLevelChanged;

        EnqueueCheck();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("SendNotification"), ref ModuleConfig.SendNotification))
            ModuleConfig.Save(this);
    }

    protected override void Uninit()
    {
        DService.Instance().Condition.ConditionChange    -= OnConditionChanged;
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Instance().ClientState.ClassJobChanged  -= OnClassJobChanged;
        DService.Instance().ClientState.LevelChanged     -= OnLevelChanged;
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (value || !ValidConditions.Contains(flag)) return;
        EnqueueCheck();
    }

    private void OnZoneChanged(ushort zone) =>
        EnqueueCheck();

    private void OnLevelChanged(uint classJobID, uint level) =>
        EnqueueCheck();

    private void OnClassJobChanged(uint classJobID) =>
        EnqueueCheck();

    private void EnqueueCheck()
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue
        (() =>
            {
                if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return false;
                if (localPlayer.Level >= PlayerState.Instance()->MaxLevel) return true;
                if (DService.Instance().Condition.IsBetweenAreas    ||
                    DService.Instance().Condition.IsOccupiedInEvent ||
                    DService.Instance().Condition.IsCasting         ||
                    !UIModule.IsScreenReady()                       ||
                    ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 2) != 0)
                    return false;

                var isGatherer = Gatherers.Contains(localPlayer.ClassJob.RowId);
                var isCrafter  = Crafters.Contains(localPlayer.ClassJob.RowId);
                if (!isGatherer && !isCrafter) return true;

                var statusManager = localPlayer.ToStruct()->StatusManager;
                var statusIndex   = statusManager.GetStatusIndex(isGatherer ? 46U : 45U);
                if (statusIndex != -1) return true;

                var itemID = 0U;
                if (isGatherer && TryGetFirstValidItem(GathererManuals, out var gathererManual))
                    itemID = gathererManual;
                if (isCrafter && TryGetFirstValidItem(CrafterManuals, out var crafterManual))
                    itemID = crafterManual;
                if (itemID == 0 || !LuminaGetter.TryGetRow<Item>(itemID, out var itemRow)) return true;

                UseActionManager.Instance().UseActionLocation(ActionType.Item, itemID, 0xE0000000, default, 0xFFFF);
                if (ModuleConfig.SendNotification)
                    NotifyHelper.Instance().NotificationInfo(Lang.Get("AutoUseCrafterGathererManual-Notification", itemRow.Name.ToString()));
                return true;
            }
        );
    }

    private static bool TryGetFirstValidItem(IEnumerable<uint> items, out uint itemID)
    {
        itemID = 0;

        var manager = InventoryManager.Instance();
        if (manager == null) return false;

        foreach (var item in items)
        {
            var count = manager->GetInventoryItemCount(item) + manager->GetInventoryItemCount(item, true);
            if (count == 0) continue;

            itemID = item;
            return true;
        }

        return false;
    }

    private class Config : ModuleConfig
    {
        public bool SendNotification = true;
    }
}
