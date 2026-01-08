using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoUseEventItem : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoUseEventItemTitle"),
        Description = GetLoc("AutoUseEventItemDescription"),
        Category    = ModuleCategories.General,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static readonly string[] InventoryEventAddons =
    [
        "InventoryEventGrid", "InventoryEventGrid0", "InventoryEventGrid0E",
    ];

    private static readonly Dictionary<uint, HashSet<uint>> QuestRowIDToEventItems =
        LuminaGetter.Get<EventItem>()
                    .Where(x => x.Quest.ValueNullable  != null && x.Action.ValueNullable != null)
                    .GroupBy(item => item.Quest.RowId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.RowId).ToHashSet());

    private static readonly HashSet<uint> InvalidLogMessageID = [7732, 563];

    protected override void Init()
    {
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 100);
        LogMessageManager.Instance().RegPre(OnPreReceiveMessage);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (InventoryEventAddons.All(x =>
            {
                var addon = GetAddonByName(x);
                return addon == null || !addon->IsVisible;
            }))
            return;
        
        OnAddonInventoryEvent();
    }
    
    private static void OnPreReceiveMessage(ref bool isPrevented, ref uint logMessageID, ref Span<LogMessageParam> values)
    {
        if (!InvalidLogMessageID.Contains(logMessageID)) return;
        
        isPrevented  = true;
        logMessageID = 0;
    }

    private static void OnAddonInventoryEvent()
    {
        if (!Throttler.Throttle("AutoUseEventItem", 100)) return;
        if (IsCasting || Request != null || !IsAnyQuestNearby(out var questRowID)) return;

        IGameObject gameObj;
        if (TargetManager.Target != null) 
            gameObj = TargetManager.Target;
        else 
            IsAnyMTQNearby(out gameObj);
        
        if (!QuestRowIDToEventItems.TryGetValue(questRowID, out var eventItemList)) return;

        if (DService.Instance().Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            Marshal.WriteByte(DService.Instance().Condition.Address + 32, 0);
            return;
        }

        var filterItems = FilterEItemsByInventory(eventItemList);
        foreach (var eItem in filterItems)
        {
            if (IsCasting) return;
            UseActionManager.Instance().UseActionLocation(ActionType.EventItem, eItem, gameObj.GameObjectID);
        }
    }

    private static bool IsAnyQuestNearby(out uint questRowID)
    {
        questRowID = 0;
        var localPos = DService.Instance().ObjectTable.LocalPlayer.Position;

        var validMarkers = AgentHUD.Instance()->MapMarkers
                           .AsSpan().ToArray()
                           .Where(marker => 
                           {
                               if (marker.TooltipString == null || (nint)marker.TooltipString <= 0) return false;
                               var markerName = marker.TooltipString->ToString();
                               if (string.IsNullOrWhiteSpace(markerName)) return false;

                               var distance = Vector3.Distance(localPos, marker.Position);
                               return marker.Radius <= 1 ? distance <= 5 : distance <= marker.Radius;
                           })
                           .Select(marker => marker.TooltipString->ToString())
                           .ToHashSet();

        var nearbyQuest = QuestManager.Instance()->NormalQuests.ToArray()
                          .Where(quest => quest.QuestId != 0 && !quest.IsHidden)
                          .Select(quest =>
                          {
                              var rowID = quest.QuestId + 65536U;
                              var questName = LuminaGetter.GetRow<Quest>(rowID)?.Name.ToDalamudString().TextValue ?? string.Empty;
                              return (rowID, questName);
                          })
                          .FirstOrDefault(quest => validMarkers.Contains(quest.questName));

        if (nearbyQuest != default)
        {
            questRowID = nearbyQuest.rowID;
            return true;
        }

        return false;
    }

    private static bool IsAnyMTQNearby(out IGameObject gameObj)
    {
        var gameObjInternal = DService.Instance().ObjectTable.FirstOrDefault(obj =>
        {
            if (!obj.IsValid() || !obj.IsTargetable || obj.IsDead)
                return false;

            var gameObjStruct = obj.ToStruct();
            if (gameObjStruct == null || gameObjStruct->RenderFlags != 0)
                return false;

            return QuestIcons.IsQuest(gameObjStruct->NamePlateIconId) ||
                   (obj.ObjectKind == ObjectKind.EventObj && gameObjStruct->TargetStatus == 15);
        });

        gameObj = gameObjInternal;
        return gameObj != null;
    }

    private static List<uint> FilterEItemsByInventory(HashSet<uint> validEItems)
    {
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.KeyItems);
        var result    = new List<uint>();
        
        if (container == null || !container->IsLoaded) return result;
        
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;
            if (!validEItems.Contains(slot->ItemId)) continue;

            result.Add(slot->ItemId);
        }

        return result;
    }

    protected override void Uninit()
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        LogMessageManager.Instance().Unreg(OnPreReceiveMessage);
    }

    [IPCProvider("DailyRoutines.Modules.AutoUseEventItem.UseEventItem")]
    private static void UseEventItem() => OnAddonInventoryEvent();
}
