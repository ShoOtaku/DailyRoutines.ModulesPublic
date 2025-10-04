using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCombineItem : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = "自动合并物品",
        Description = "自动合并背包中可以叠加的物品",
        Category    = ModuleCategories.General,
        Author      = ["XSZYYS"]
    };

    private static Config ModuleConfig = null!;

    private static bool IsCombining;
    private static DateTime LastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private static readonly InventoryType[] InventoryTypes =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    ];

    protected override void Init()
    {
        TaskHelper   = new() { TimeLimitMS = 10_000 };
        ModuleConfig = LoadConfig<Config>() ?? new();

        FrameworkManager.Reg(OnUpdate, throttleMS: 1000);
    }
    
    protected override void Uninit()
    {
        FrameworkManager.Unreg(OnUpdate);

        IsCombining   = false;
        LastCheckTime = DateTime.MinValue;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!ModuleConfig.EnableAuto || IsCombining) return;
        if (DateTime.Now - LastCheckTime < CheckInterval) return;
        if (ModuleConfig.OnlyNotInDuty && BoundByDuty) return;

        LastCheckTime = DateTime.Now;
        TryCombineItems();
    }

    protected override void ConfigUI()
    {
        var moduleConfigEnableAuto = ModuleConfig.EnableAuto;
        if (ImGui.Checkbox("启用自动合并", ref moduleConfigEnableAuto))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGuiOm.HelpMarker("当背包物品变化时，自动检测并合并可叠加的物品");

        var moduleConfigOnlyNotInDuty = ModuleConfig.OnlyNotInDuty;
        if (ImGui.Checkbox("仅在非副本状态下执行", ref moduleConfigOnlyNotInDuty))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGuiOm.HelpMarker("开启后，只在非副本状态下自动合并物品，避免影响副本内的操作");

        if (ImGui.Button("立即合并"))
            TryCombineItems();
    }

    private void TryCombineItems()
    {
        if (IsCombining || TaskHelper.IsBusy) return;

        var itemsToCombine = FindItemsToCombine();
        if (itemsToCombine.Count == 0) return;

        IsCombining = true;
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
            if (!container->IsLoaded) continue;

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

                // 使用 ItemId 和 Flags 组合作为唯一标识，区分 HQ 和普通物品
                var itemKey = ((ulong)itemID << 32) | (ulong)flags;

                if (!itemSlots.TryGetValue(itemKey, out var slots))
                {
                    slots = [];
                    itemSlots[itemKey] = slots;
                }

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
        TaskHelper.Abort();

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
        if (manager == null) return true;

        var agentInventory = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
        if (agentInventory == null) return false;

        var sourceContainer = manager->GetInventoryContainer(source.InventoryType);
        var targetContainer = manager->GetInventoryContainer(target.InventoryType);

        if (sourceContainer == null || !sourceContainer->IsLoaded ||
            targetContainer == null || !targetContainer->IsLoaded)
            return false;

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

        return false;
    }
    
    private class Config : ModuleConfiguration
    {
        public bool EnableAuto       { get; set; } = true;
        public bool OnlyNotInDuty    { get; set; } = true;
    }

    private class SlotInfo
    {
        public InventoryType InventoryType { get; init; }
        public int           SlotIndex     { get; init; }
        public uint          Quantity      { get; init; }
        public uint          MaxStackSize  { get; init; }
    }
}
