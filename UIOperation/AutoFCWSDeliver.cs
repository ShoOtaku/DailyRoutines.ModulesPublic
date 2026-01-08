using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Extensions;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoFCWSDeliver : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title               = GetLoc("AutoFCWSDeliverTitle"),
        Description         = GetLoc("AutoFCWSDeliverDescription"),
        Category            = ModuleCategories.UIOperation,
        ModulesPrerequisite = ["AutoRequestItemSubmit", "AutoCutsceneSkip"]
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };
    
    protected override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeoutMS = 30_000 };
        Overlay    ??= new Overlay(this);
        
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "SelectYesno",        OnAddonYesno);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "SelectString",       OnAddonString);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "SubmarinePartsMenu", OnAddonMenu);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SubmarinePartsMenu", OnAddonMenu);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CompanyCraftRecipeNoteBook", OnAddonRecipeNote);
        
        if (SubmarinePartsMenu != null) 
            OnAddonMenu(AddonEvent.PostSetup, null);
    }

    protected override void ConfigUI() => ConflictKeyText();

    protected override void OverlayUI()
    {
        if (SubmarinePartsMenu == null)
        {
            Overlay.IsOpen = false;
            TaskHelper.RemoveQueue(0);
            return;
        }

        var pos = new Vector2(SubmarinePartsMenu->GetX() + 6, SubmarinePartsMenu->GetY() - ImGui.GetWindowSize().Y + 6);

        ImGui.SetWindowPos(pos);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, GetLoc("AutoFCWSDeliverTitle"));

        ImGui.SameLine();
        using (ImRaii.Disabled(TaskHelper.IsBusy))
        {
            if (ImGui.Button(GetLoc("Start")))
                EnqueueSubmit();
        }

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("Stop")))
            TaskHelper.Abort();
    }

    private bool EnqueueSubmit()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        if (!SubmarinePartsMenu->IsAddonAndNodesReady()) return false;
        if (Request->IsAddonAndNodesReady()) return false;

        TaskHelper.Abort();
        
        var validItems = WorkshopCraftItem.Parse(SubmarinePartsMenu);
        if (validItems.Count == 0) return true;
        
        foreach (var item in validItems)
        {
            TaskHelper.Enqueue(() =>
                               {
                                   if (InterruptByConflictKey(TaskHelper, this)) return;
                                   var atkValue = new AtkValue()
                                   {
                                       Type  = (ValueType)(item.ItemID % 500000), 
                                       Int64 = SetHighDword((int)item.ItemCount)
                                   };
                                   AgentId.CompanyCraftMaterial.SendEvent(0, 0, item.Index, item.ItemCount, atkValue);
                               }, $"交纳物品 {item.ItemID}x{item.ItemCount}");
            TaskHelper.DelayNext(5_00, "等待 Request");
            TaskHelper.Enqueue(() =>
            {
                if (InterruptByConflictKey(TaskHelper, this)) return true;
                return !Request->IsAddonAndNodesReady() && !SelectYesno->IsAddonAndNodesReady();
            }, "等待材料上交界面消失");
            break;
        }

        TaskHelper.Enqueue(EnqueueSubmit, "开始新一轮");
        return true;
    }

    private void OnAddonMenu(AddonEvent type, AddonArgs args)
    {
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup   => true,
            AddonEvent.PreFinalize => true,
            _                      => Overlay.IsOpen,
        };

        if (type is AddonEvent.PostSetup)
        {
            if (InterruptByConflictKey(TaskHelper, this)) return;
            TaskHelper.Enqueue(EnqueueSubmit);
        }
    }

    private void OnAddonYesno(AddonEvent type, AddonArgs args)
    {
        if (HousingManager.Instance()->WorkshopTerritory == null) return;
        if (SubmarinePartsMenu == null || !TaskHelper.IsBusy) return;

        var addon = SelectYesno;
        if (addon == null) return;

        var text = addon->AtkValues[0].String.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (InterruptByConflictKey(TaskHelper, this)) return;
        ClickSelectYesnoYes();
    }
    
    private void OnAddonString(AddonEvent type, AddonArgs args)
    {
        if (!Throttler.Throttle("AutoFCWSDeliver-ClickSelectString", 3_000)) return;
        
        var addon = SelectString;
        if (addon == null) return;
        
        if (HousingManager.Instance()->WorkshopTerritory == null) return;
        if (TargetManager.Target is not { ObjectKind: ObjectKind.EventObj, DataID: 2011588 }) return;
        if (InterruptByConflictKey(TaskHelper, this)) return;
        
        TaskHelper.RemoveQueueTasks(0);

        ClickSelectString(0);
        
        TaskHelper.Enqueue(() =>
        {
            if (InterruptByConflictKey(TaskHelper, this)) return true;
            if (DService.Instance().UIBuilder.CutsceneActive || !UIModule.IsScreenReady()) return false;
            if (TargetManager.Target is not { ObjectKind: ObjectKind.EventObj, DataID: 2011588 })
            {
                var target =
                    DService.Instance().ObjectTable.FindNearest(DService.Instance().ObjectTable.LocalPlayer.Position,
                                                     x => x is { ObjectKind: ObjectKind.EventObj, DataID: 2011588 });
                TargetManager.Target = target;
            }

            TargetManager.Target.Interact();
            return SubmarinePartsMenu->IsAddonAndNodesReady() || SelectString->IsAddonAndNodesReady();
        }, "尝试再次交互合建设备", weight: 1);
    }
    
    private void OnAddonRecipeNote(AddonEvent type, AddonArgs args) => TaskHelper.Abort();
    
    public static long SetHighDword(int value) => (long)value << 32;

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddonRecipeNote);
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddonYesno);
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddonString);
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddonMenu);
    }

    private record WorkshopCraftItem(uint ItemID, uint ItemCount, uint Index)
    {
        public static List<WorkshopCraftItem> Parse(AtkUnitBase* addon)
        {
            List<WorkshopCraftItem> result = [];
            
            if (addon == null || addon->NameString != "SubmarinePartsMenu") return result;

            var agent = AgentCompanyCraftMaterial.Instance();
            if (agent == null) return result;

            var manager = InventoryManager.Instance();
            if (manager == null) return result;

            var validItemCount = agent->SupplyItems.ToArray().Count(x => x != 0);
            if (validItemCount == 0) return result;

            for (var i = 0; i < validItemCount; i++)
            {
                // 物品不存在
                var itemID = addon->AtkValues[12 + i].UInt;
                if (itemID == 0) continue; 

                // 这个物品交完了
                var progress = addon->AtkValues[132 + i].UInt;
                if (progress == 1) continue;

                var itemCount      = addon->AtkValues[60 + i].UInt;
                var itemCountOwned = addon->AtkValues[72 + i].UInt;
                
                // 物品不够
                if (itemCountOwned < itemCount) continue;

                result.Add(new(itemID, itemCount, (uint)i));
            }

            return result;
        }
    }
}
