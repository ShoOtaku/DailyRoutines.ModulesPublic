using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using OmenTools;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCombineItem : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCombineItemTitle"),
        Description = GetLoc("AutoCombineItemDescription"),
        Category    = ModuleCategories.General,
        Author      = ["XSZYYS"]
    };

    private static readonly CompSig                        InventoryUpdateSig = new("48 89 5C 24 ?? 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 0F B7 5A");
    private delegate        nint                           InventoryUpdateDelegate(nint a1, nint a2);
    private static          Hook<InventoryUpdateDelegate>? InventoryUpdateHook;
    private static readonly InventoryType[] InventoryTypes =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    ];

    private static Config ModuleConfig = null!;
    private bool IsCombining;
    
    protected override void Init()
    {
        TaskHelper  ??= new() { TimeLimitMS = 10_000 };
        ModuleConfig  = LoadConfig<Config>() ?? new();

        InventoryUpdateHook ??= InventoryUpdateSig.GetHook<InventoryUpdateDelegate>(OnInventoryUpdate);
        InventoryUpdateHook.Enable();
    }

    protected override void Uninit()
    {
        if (InventoryUpdateHook != null)
        {
            InventoryUpdateHook.Dispose();
            InventoryUpdateHook = null;
        }

        IsCombining = false;
        
        TaskHelper?.Abort();
        TaskHelper?.Dispose();
        TaskHelper = null;
    }

    private nint OnInventoryUpdate(nint a1, nint a2)
    {
        try
        {
            if (ModuleConfig.EnableAuto && !IsCombining && !TaskHelper.IsBusy)
            {
                if (ModuleConfig.OnlyNotInDuty && BoundByDuty)
                    return InventoryUpdateHook!.Original(a1, a2);

                IsCombining = true;
                TaskHelper.Enqueue(() =>
                {
                    TryCombineItems();
                    return true;
                });
            }
        }
        catch (Exception ex)
        {
            DService.Log.Error(ex, "合并任务出错");
        }

        return InventoryUpdateHook!.Original(a1, a2);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("Enable"), ref ModuleConfig.EnableAuto))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();

        if (ImGui.Checkbox(GetLoc("AutoCombineItem-OnlyNotInDuty"), ref ModuleConfig.OnlyNotInDuty))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();

        if (ImGui.Button(GetLoc("AutoCombineItem-CombineNow")))
            TryCombineItems();
    }

    private void TryCombineItems()
    {
        var itemsToCombine = FindItemsToCombine();
        if (itemsToCombine.Count == 0)
        {
            IsCombining = false;
            return;
        }

        EnqueueCombineTasks(itemsToCombine);
    }

    private static Dictionary<ulong, List<SlotInfo>> FindItemsToCombine()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return [];

        var itemSlots = new Dictionary<ulong, List<SlotInfo>>();

        foreach (var invType in InventoryTypes)
        {
            var container = manager->GetInventoryContainer(invType);
            if (container == null || !container->IsLoaded) continue;

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;

                var itemID = slot->ItemId;
                var quantity = slot->GetQuantity();
                var flags = slot->Flags;

                if (quantity == 0) continue;

                if (!LuminaGetter.TryGetRow<Item>(itemID, out var item)) continue;
                if (item.StackSize <= 1) continue;

                // 高32位: ItemID, 低32位: Flags (包含 HQ 标记等信息)
                var itemKey = ((ulong)itemID << 32) | (ulong)flags;

                if (!itemSlots.TryGetValue(itemKey, out var slots))
                    itemSlots[itemKey] = slots = [];

                slots.Add(new SlotInfo
                {
                    InventoryType = invType,
                    SlotIndex     = i,
                    Quantity      = quantity,
                    MaxStackSize  = item.StackSize
                });
            }
        }

        return itemSlots.Where(kvp => kvp.Value.Count > 1 &&
                                      kvp.Value.Any(s => s.Quantity < s.MaxStackSize))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private void EnqueueCombineTasks(Dictionary<ulong, List<SlotInfo>> itemsToCombine)
    {
        foreach (var (itemID, slots) in itemsToCombine)
        {
            var sortedSlots = slots.OrderByDescending(s => s.Quantity).ToList();

            for (var i = 0; i < sortedSlots.Count; i++)
            {
                for (var j = i + 1; j < sortedSlots.Count; j++)
                {
                    var targetSlot = sortedSlots[i];
                    var sourceSlot = sortedSlots[j];

                    if (targetSlot.Quantity >= targetSlot.MaxStackSize) break;

                    TaskHelper.Enqueue(() => CombineSlots(sourceSlot, targetSlot));
                    TaskHelper.DelayNext(100);
                }
            }
        }

        TaskHelper.Enqueue(() =>
        {
            IsCombining = false;
            return true;
        });
    }

    private static bool? CombineSlots(SlotInfo source, SlotInfo target)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return false;

        var sourceContainer = manager->GetInventoryContainer(source.InventoryType);
        if (sourceContainer == null || !sourceContainer->IsLoaded) return false;

        var targetContainer = manager->GetInventoryContainer(target.InventoryType);
        if (targetContainer == null || !targetContainer->IsLoaded) return false;

        var sourceSlot = sourceContainer->GetInventorySlot(source.SlotIndex);
        var targetSlot = targetContainer->GetInventorySlot(target.SlotIndex);

        if (sourceSlot == null || sourceSlot->ItemId == 0 ||
            targetSlot == null || targetSlot->ItemId == 0)
            return true;

        // 检查 ItemId 和 Flags 是否都相同，确保 HQ 和普通物品不会合并
        if (sourceSlot->ItemId != targetSlot->ItemId || sourceSlot->Flags != targetSlot->Flags)
            return true;

        if (!LuminaGetter.TryGetRow<Item>(sourceSlot->ItemId, out var item)) return true;

        var targetQuantity = targetSlot->GetQuantity();
        if (targetQuantity >= item.StackSize) return true;
        if (sourceSlot->GetQuantity() == 0) return true;

        manager->MoveItemSlot(source.InventoryType, (ushort)source.SlotIndex,
                             target.InventoryType, (ushort)target.SlotIndex, true);

        return null;
    }

    private class Config : ModuleConfiguration
    {
        public bool EnableAuto = true;
        public bool OnlyNotInDuty = true;
    }

    private record SlotInfo
    {
        public InventoryType InventoryType { get; init; }
        public int           SlotIndex     { get; init; }
        public uint          Quantity      { get; init; }
        public uint          MaxStackSize  { get; init; }
    }
}
