using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoRepair : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRepairTitle"),
        Description = GetLoc("AutoRepairDescription"),
        Category    = ModuleCategories.General,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    public bool IsBusy => TaskHelper?.IsBusy ?? false;

    private static readonly HashSet<ConditionFlag> ValidConditions =
    [
        ConditionFlag.InCombat, 
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.Gathering, 
        ConditionFlag.Crafting
    ];

    private static readonly HashSet<ExecuteCommandFlag> ValidRepairFlags =
    [
        ExecuteCommandFlag.RepairItemNPC,
        ExecuteCommandFlag.RepairAllItemsNPC,
        ExecuteCommandFlag.RepairEquippedItemsNPC,
        
        ExecuteCommandFlag.RepairItem,
        ExecuteCommandFlag.RepairAllItems,
        ExecuteCommandFlag.RepairEquippedItems
    ];

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig ??= LoadConfig<Config>() ?? new();
        TaskHelper ??= new();

        ExecuteCommandManager.RegPost(OnExecuteCommand);
        
        DService.ClientState.TerritoryChanged += OnZoneChanged;
        DService.Condition.ConditionChange    += OnConditionChanged;
        DService.DutyState.DutyRecommenced    += OnDutyRecommenced;
    }
    
    protected override void ConfigUI()
    {
        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        ImGui.InputFloat(GetLoc("AutoRepair-RepairThreshold"), ref ModuleConfig.RepairThreshold, 0, 0, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);
        
        ImGui.NewLine();
        
        if (ImGui.Checkbox(GetLoc("AutoRepair-AllowNPCRepair"), ref ModuleConfig.AllowNPCRepair))
            SaveConfig(ModuleConfig);
        ImGuiOm.HelpMarker(GetLoc("AutoRepair-AllowNPCRepairHelp"), 100f * GlobalFontScale);
        
        if (ModuleConfig.AllowNPCRepair)
        {
            if (ImGui.Checkbox(GetLoc("AutoRepair-PrioritizeNPCRepair"), ref ModuleConfig.PrioritizeNPCRepair))
                SaveConfig(ModuleConfig);
            ImGuiOm.HelpMarker(GetLoc("AutoRepair-PrioritizeNPCRepairHelp"), 100f * GlobalFontScale);
        }
    }

    public void EnqueueRepair()
    {
        if (TaskHelper.IsBusy                      ||
            DService.ClientState.IsPvPExcludingDen ||
            DService.ObjectTable.LocalPlayer is not { CurrentHp: > 0 })
            return;

        var playerState      = PlayerState.Instance();
        var inventoryManager = InventoryManager.Instance();

        if (playerState == null || inventoryManager == null) return;
        
        // 没有需要修理的装备
        if (!TryGetInventoryItems([InventoryType.EquippedItems],
                                 x => x.Condition < ModuleConfig.RepairThreshold * 300f, out var items))
            return;

        // 优先委托 NPC 修理
        if (ModuleConfig is { AllowNPCRepair: true, PrioritizeNPCRepair: true } && IsEventIDNearby(720915))
        {
            TaskHelper.Abort();
            TaskHelper.Enqueue(() => IsAbleToRepair());
            TaskHelper.Enqueue(() => NotificationInfo(GetLoc("AutoRepair-RepairNotice"), GetLoc("AutoRepairTitle")));
            TaskHelper.Enqueue(() => new EventStartPackt(LocalPlayerState.EntityID, 720915).Send());
            TaskHelper.Enqueue(() => IsAddonAndNodesReady(Repair));
            TaskHelper.Enqueue(() => ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.RepairEquippedItemsNPC, 1000));
            TaskHelper.Enqueue(() =>
            {
                if (!IsAddonAndNodesReady(Repair)) return;
                Repair->Close(true);
            });
            
            return;
        }

        List<uint> itemsUnableToRepair = [];
        var repairDMs = LuminaGetter.Get<ItemRepairResource>()
                                   .Where(x => x.Item.RowId != 0)
                                   .ToDictionary(x => x.RowId,
                                                 x => inventoryManager->GetInventoryItemCount(x.Item.RowId));
        
        var isDMInsufficient = false;
        foreach (var itemToRepair in items)
        {
            if (!LuminaGetter.TryGetRow<Item>(itemToRepair.ItemId, out var data)) continue;
            
            var repairJob   = data.ClassJobRepair.RowId;
            var repairLevel = Math.Max(1, Math.Max(0, data.LevelEquip - 10));
            var repairDM    = data.ItemRepair.RowId;

            var firstDM = repairDMs.OrderBy(x => x.Key).FirstOrDefault(x => x.Key >= repairDM && x.Value - 1 >= 0).Key;
            // 可以自己修 + 暗物质数量足够
            if (LocalPlayerState.GetClassJobLevel(repairJob) >= repairLevel && firstDM != 0)
            {
                repairDMs[firstDM]--;
                continue;
            }
            
            if (firstDM is 0)
                isDMInsufficient = true;
            
            itemsUnableToRepair.Add(itemToRepair.ItemId);
        }

        TaskHelper.Abort();
        
        // 还是有能自己修的装备的
        if (items.Count > itemsUnableToRepair.Count)
        {
            TaskHelper.Enqueue(() => IsAbleToRepair(), "等待可以维修状态");
            TaskHelper.Enqueue(() => NotificationInfo(GetLoc("AutoRepair-RepairNotice"), GetLoc("AutoRepairTitle")), "发送开始维修通知");
            
            // 没有暗物质不足的情况
            if (!isDMInsufficient)
                TaskHelper.Enqueue(() => RepairManager.Instance()->RepairEquipped(false), "发送一键全修");
            else
            {
                var itemsSelfRepair = items.ToList();
                itemsSelfRepair.RemoveAll(x => itemsUnableToRepair.Contains(x.ItemId));
                foreach (var item in itemsSelfRepair)
                {
                    TaskHelper.Enqueue(() => RepairManager.Instance()->RepairItem(item.Container, (ushort)item.Slot, false), $"修理: {LuminaWrapper.GetItemName(item.ItemId)}");
                    TaskHelper.DelayNext(3_000);
                }
            }

            TaskHelper.DelayNext(5_00);
        }

        // 附近存在修理工
        if (ModuleConfig.AllowNPCRepair && itemsUnableToRepair.Count > 0 && IsEventIDNearby(720915))
        {
            TaskHelper.Enqueue(() => IsAbleToRepair());
            TaskHelper.Enqueue(() => NotificationInfo(GetLoc("AutoRepair-RepairNotice"), GetLoc("AutoRepairTitle")));
            TaskHelper.Enqueue(() => new EventStartPackt(LocalPlayerState.EntityID, 720915).Send());
            TaskHelper.Enqueue(() => IsAddonAndNodesReady(Repair));
            TaskHelper.Enqueue(() => ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.RepairEquippedItemsNPC, 1000));
            TaskHelper.Enqueue(() =>
            {
                if (!IsAddonAndNodesReady(Repair)) return;
                Repair->Close(true);
            });
        }
    }

    private static bool IsAbleToRepair() =>
        IsScreenReady()            &&
        !OccupiedInEvent           &&
        !GameState.IsInPVPInstance &&
        !IsOnMount                 &&
        !IsCasting                 &&
        ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) == 0;
    
    #region 事件

    private void OnDutyRecommenced(object? sender, ushort e) =>
        EnqueueRepair();

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (value || !ValidConditions.Contains(flag)) return;
        EnqueueRepair();
    }

    private void OnZoneChanged(ushort zoneID) =>
        EnqueueRepair();
    
    private static void OnExecuteCommand(ExecuteCommandFlag command, uint param1, uint param2, uint param3, uint param4)
    {
        if (!ValidRepairFlags.Contains(command)) return;
        
        ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.InventoryRefresh);
    }

    #endregion

    protected override void Uninit()
    {
        ExecuteCommandManager.Unreg(OnExecuteCommand);
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Condition.ConditionChange -= OnConditionChanged;
        DService.DutyState.DutyRecommenced -= OnDutyRecommenced;
    }

    private class Config : ModuleConfiguration
    {
        public float RepairThreshold    = 20;
        public bool  AllowNPCRepair     = true;
        public bool  PrioritizeNPCRepair;
    }
    
    [IPCProvider("DailyRoutines.Modules.AutoRepair.IsBusy")]
    public bool IsBusyIPC => IsBusy;
    
    [IPCProvider("DailyRoutines.Modules.AutoRepair.IsNeedToRepair")]
    public bool IsNeedToRepairIPC => TryGetInventoryItems([InventoryType.EquippedItems],
                                                       x => x.Condition < ModuleConfig.RepairThreshold * 300f, out _);
    
    [IPCProvider("DailyRoutines.Modules.AutoRepair.IsAbleToRepair")]
    public bool IsAbleToRepairIPC => IsAbleToRepair();

    [IPCProvider("DailyRoutines.Modules.AutoRepair.EnqueueRepair")]
    public void EnqueueRepairIPC() => EnqueueRepair();
}
