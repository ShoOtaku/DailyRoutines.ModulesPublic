using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoRestoreFurniture : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRestoreFurnitureTitle"),
        Description = GetLoc("AutoRestoreFurnitureDescription"),
        Category    = ModuleCategories.UIOperation,
    };

    protected override void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 10000 };
        Overlay ??= new(this);

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, OnAddon);
        if (HousingGoods != null) 
            OnAddon(AddonEvent.PostSetup, null);
    }

    protected override void OverlayUI()
    {
        var addon = HousingGoods;
        if (addon == null)
        {
            Overlay.IsOpen = false;
            return;
        }

        var pos = new Vector2(addon->GetX() - ImGui.GetWindowSize().X, addon->GetY() + 6);
        ImGui.SetWindowPos(pos);

        using (FontManager.UIFont80.Push())
        {
            var isOutdoor = HousingGoods->AtkValues[9].UInt != 6U;
            
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("AutoRestoreFurnitureTitle"));
            
            ImGui.SameLine();
            ImGui.Text($"({GetLoc(isOutdoor ? "Outdoors" : "Indoors")})");

            ImGui.SameLine();
            ImGui.Spacing();
            
            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Stop, $" {GetLoc("Stop")}"))
                TaskHelper.Abort();

            using (ImRaii.Disabled(TaskHelper.IsBusy))
            {
                if (ImGui.Selectable($"    {GetLoc("AutoRestoreFurniture-PlacedToStoreRoom")}"))
                    EnqueueRestore(isOutdoor ? 25001U : 25003U, isOutdoor ? 25001U : 25010U, !isOutdoor, 65536);

                if (ImGui.Selectable($"    {GetLoc("AutoRestoreFurniture-PlacedToInventory")}"))
                    EnqueueRestore(isOutdoor ? 25001U : 25003U, isOutdoor ? 25001U : 25010U, !isOutdoor);

                if (ImGui.Selectable($"    {GetLoc("AutoRestoreFurniture-StoredToInventory")}"))
                    EnqueueRestore(isOutdoor ? 27000U : 27001U, isOutdoor ? 27000U : 27008U, !isOutdoor);
            }
        }
    }

    private bool? EnqueueRestore(uint startInventory, uint endInventory, bool isIndoor, int extraSlotParam = 0)
    {
        var houseManager = HousingManager.Instance();
        var inventoryManager = InventoryManager.Instance();
        if (houseManager == null || inventoryManager == null ||
            (houseManager->GetCurrentIndoorHouseId() < 0 && houseManager->GetCurrentPlot() < 0))
        {
            TaskHelper.Abort();
            return true;
        }

        var param1 = isIndoor
                         ? *(long*)((nint)houseManager->IndoorTerritory + 38560) >> 32
                         : *(long*)((nint)houseManager->OutdoorTerritory + 38560) >> 32;
        var param2 = isIndoor ? houseManager->IndoorTerritory->HouseId : houseManager->OutdoorTerritory->HouseId;

        for (var i = startInventory; i <= endInventory; i++)
        {
            var type = (InventoryType)i;
            var contaniner = inventoryManager->GetInventoryContainer(type);
            if (contaniner == null) continue;
            for (var d = 0; d < contaniner->Size; d++)
            {
                var slot = contaniner->GetInventorySlot(d);
                if (slot == null || slot->ItemId == 0) continue;

                var inventoryTypeFinal = (int)i;
                var slotFinal = d;

                TaskHelper.Enqueue(() => ExecuteCommandManager.ExecuteCommand(
                                       ExecuteCommandFlag.RestoreFurniture, 
                                       (uint)param1, 
                                       (uint)param2, 
                                       (uint)inventoryTypeFinal, 
                                       (uint)(slotFinal + extraSlotParam)));
                TaskHelper.Enqueue(() => EnqueueRestore(startInventory, endInventory, isIndoor, extraSlotParam));
                return true;
            }
        }

        TaskHelper.Abort();
        return true;
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        var addon = HousingGoods;
        if (addon == null || !IsAddonAndNodesReady(addon)) return;

        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup => true,
            AddonEvent.PreFinalize => true,
            _ => Overlay.IsOpen,
        };

        if (type == AddonEvent.PreFinalize) 
            TaskHelper.Abort();
    }

    protected override void Uninit() => 
        DService.AddonLifecycle.UnregisterListener(OnAddon);
}
