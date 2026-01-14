using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Action = System.Action;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoGardensWork : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title               = GetLoc("AutoGardensWorkTitle"),
        Description         = GetLoc("AutoGardensWorkDescription"),
        Category            = ModuleCategories.General,
        ModulesPrerequisite = ["AutoTalkSkip"]
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };
    
    private static Config ModuleConfig = null!;

    private static string SearchSeed      = string.Empty;
    private static string SearchSoil      = string.Empty;
    private static string SearchFertilize = string.Empty;
    
    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeoutMS = 10_000 };

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "HousingGardening", OnAddon);

        TargetManager.Instance().RegPostSetHardTarget(OnSetHardTarget);
    }

    protected override void ConfigUI()
    {
        DrawAutoPlant();

        ImGui.NewLine();
        
        DrawAutoGather();

        ImGui.NewLine();
        
        DrawAutoFertilize();

        ImGui.NewLine();

        DrawAutoTend();
    }

    private void DrawAutoPlant()
    {
        using var id = ImRaii.PushId("AutoPlant");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoPlant")}");

        using var indent = ImRaii.PushIndent();

        using (ImRaii.Disabled(TaskHelper?.IsBusy ?? true))
        {
            if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
                StartPlant();
        }

        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();

        ImGui.Spacing();

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (SingleSelectCombo("SeedSelectCombo",
                              PresetSheet.Seeds,
                              ref ModuleConfig.SeedSelected,
                              ref SearchSeed,
                              x => $"{x.Name.ToString()} ({x.RowId})",
                              [new(LuminaWrapper.GetAddonText(6412), ImGuiTableColumnFlags.WidthStretch, 0)],
                              [
                                  x => () =>
                                  {
                                      var icon = ImageHelper.GetGameIcon(x.Icon);
                                      if (ImGuiOm.SelectableImageWithText(
                                              icon.Handle,
                                              new(ImGui.GetTextLineHeightWithSpacing()),
                                              x.Name.ToString(),
                                              x.RowId == ModuleConfig.SeedSelected,
                                              ImGuiSelectableFlags.DontClosePopups | ImGuiSelectableFlags.SpanAllColumns))
                                          ModuleConfig.SeedSelected = x.RowId;
                                  }
                              ],
                              [x => x.Name.ToString(), x => x.RowId.ToString()],
                              true))
            SaveConfig(ModuleConfig);
        
        ImGui.SameLine();
        ImGui.TextUnformatted(LuminaWrapper.GetAddonText(6412));

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        using (ImRaii.PushId("SoilSelectCombo"))
        {
            if (SingleSelectCombo("SoilSelectCombo",
                                  PresetSheet.Soils, 
                                  ref ModuleConfig.SoilSelected, 
                                  ref SearchSoil,
                                  x => $"{x.Name.ToString()} ({x.RowId})",
                                  [new(LuminaWrapper.GetAddonText(6411), ImGuiTableColumnFlags.WidthStretch, 0)],
                                  [
                                      x => () =>
                                      {
                                          var icon = ImageHelper.GetGameIcon(x.Icon);
                                          if (ImGuiOm.SelectableImageWithText(
                                                  icon.Handle, new(ImGui.GetTextLineHeightWithSpacing()),
                                                  x.Name.ToString(), x.RowId == ModuleConfig.SeedSelected,
                                                  ImGuiSelectableFlags.DontClosePopups))
                                              ModuleConfig.SoilSelected = x.RowId;
                                      }
                                  ],
                                  [x => x.Name.ToString(), x => x.RowId.ToString()],
                                  true))
                SaveConfig(ModuleConfig);
        }
        
        ImGui.SameLine();
        ImGui.TextUnformatted(LuminaWrapper.GetAddonText(6411));
    }

    private void DrawAutoGather()
    {
        using var id = ImRaii.PushId("AutoGather");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoGather")}");

        using var indent = ImRaii.PushIndent();

        using (ImRaii.Disabled(TaskHelper?.IsBusy ?? true))
        {
            if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
                StartGather();
        }
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();
    }

    private void DrawAutoFertilize()
    {
        using var id = ImRaii.PushId("AutoFertilize");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoFertilize")}");

        using var indent = ImRaii.PushIndent();

        using (ImRaii.Disabled(TaskHelper?.IsBusy ?? true))
        {
            if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
                StartFertilize();
        }
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();
        
        ImGui.Spacing();

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (SingleSelectCombo("FertilizersSelectCombo",
                              PresetSheet.Fertilizers,
                              ref ModuleConfig.FertilizerSelected,
                              ref SearchFertilize,
                              x => $"{x.Name.ToString()} ({x.RowId})",
                              [new(LuminaWrapper.GetAddonText(6417), ImGuiTableColumnFlags.WidthStretch, 0)],
                              [
                                  x => () =>
                                  {
                                      if (ImGuiOm.SelectableImageWithText(
                                              ImageHelper.GetGameIcon(x.Icon).Handle,
                                              new(ImGui.GetTextLineHeightWithSpacing()),
                                              x.Name.ToString(),
                                              x.RowId == ModuleConfig.SeedSelected,
                                              ImGuiSelectableFlags.DontClosePopups | ImGuiSelectableFlags.SpanAllColumns))
                                          ModuleConfig.FertilizerSelected = x.RowId;
                                  }
                              ],
                              [x => x.Name.ToString(), x => x.RowId.ToString()],
                              true))
            ModuleConfig.Save(this);
        
        ImGui.SameLine();
        ImGui.TextUnformatted(LuminaWrapper.GetAddonText(6417));
    }
    
    private void DrawAutoTend()
    {
        using var id = ImRaii.PushId("AutoTend");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoTend")}");

        using var indent = ImRaii.PushIndent();

        using (ImRaii.Disabled(TaskHelper?.IsBusy ?? true))
        {
            if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
                StartTend();
        }
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();
    }

    #region 事件

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (ModuleConfig.SeedSelected == 0 || ModuleConfig.SoilSelected == 0) return;

        if (!PlayerInventories.TryGetFirstItem(x => x.ItemId == ModuleConfig.SeedSelected, out var seedItem) ||
            !PlayerInventories.TryGetFirstItem(x => x.ItemId == ModuleConfig.SoilSelected, out var soilItem))
            return;

        TaskHelper.Enqueue(() =>
        {
            var agent = AgentHousingPlant.Instance();
            if (agent == null) return;

            agent->SelectedItems[0] = new()
            {
                ItemId        = soilItem->ItemId,
                InventoryType = soilItem->Container,
                InventorySlot = (ushort)soilItem->Slot
            };
            agent->SelectedItems[1] = new()
            {
                ItemId        = seedItem->ItemId,
                InventoryType = seedItem->Container,
                InventorySlot = (ushort)seedItem->Slot
            };

            agent->ConfirmSeedAndSoilSelection();
        }, weight: 2);

        TaskHelper.Enqueue(() => ClickSelectYesnoYes(), weight: 2);
    }
    
    private void OnSetHardTarget(bool result, IGameObject? target, bool checkMode, bool a4, int a5)
    {
        var outdoorZone = HousingManager.Instance()->OutdoorTerritory;
        if (outdoorZone == null) return;

        switch (target)
        {
            case null when (OverlayConfig?.IsOpen ?? false):
                ToggleOverlayConfig(false);
                break;
            
            case { ObjectKind: ObjectKind.EventObj, DataID: 2003757 }:
                ToggleOverlayConfig(true);
                break;
            
            default:
                ToggleOverlayConfig(false);
                break;
        }
    }

    #endregion

    #region 流程

    private void StartAction(string entryKeyword, Action extraAction = null)
    {
        if (!IsEnvironmentValid(out var objectIDs)) return;

        TaskHelper.Enqueue(() => TargetManager.Target = null, "移除当前目标");

        foreach (var garden in objectIDs)
        {
            TaskHelper.Enqueue(() => new EventStartPackt(garden, 721047).Send(), $"交互园圃: {garden}");
            TaskHelper.Enqueue(() => ClickGardenEntryByText(entryKeyword),       "点击");
            extraAction?.Invoke();
            TaskHelper.Enqueue(() => !DService.Instance().Condition[ConditionFlag.OccupiedInQuestEvent], "等待退出交互状态");
        }
    }

    private void StartGather() => 
        StartAction(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(6).Text.ToString());

    private void StartTend() => 
        StartAction(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(4).Text.ToString());

    private void StartPlant() =>
        StartAction(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(2).Text.ToString(), () => TaskHelper.DelayNext(250));

    private void StartFertilize() =>
        StartAction(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(3).Text.ToString(), () =>
        {
            TaskHelper.Enqueue(CheckFertilizerState);
            TaskHelper.Enqueue(ClickFertilizer);
            TaskHelper.Enqueue(() => !DService.Instance().Condition[ConditionFlag.OccupiedInQuestEvent]);
        });

    #endregion

    #region 工具

    private static bool IsEnvironmentValid(out List<ulong> objectIDs)
    {
        objectIDs = [];
        
        if (DService.Instance().ObjectTable.LocalPlayer == null) return false;

        var manager = HousingManager.Instance();
        if (manager == null) return false;
        
        // 不在房区里
        var outdoorZone = manager->OutdoorTerritory;
        if (outdoorZone == null) return false;

        var houseID = (ulong)manager->GetCurrentHouseId();
        if (houseID == 0)  return false;
        
        // 在自己有权限的房子院子里
        // 具体怎么个有权限法不想测了
        foreach (var type in Enum.GetValues<EstateType>())
        {
            if (type == EstateType.SharedEstate)
            {
                for (var i = 0; i < 2; i++)
                {
                    var typeHouseID = HousingManager.GetOwnedHouseId(type, i);
                    if (typeHouseID == houseID)
                        goto Out;
                }
            }
            else
            {
                var typeHouseID = HousingManager.GetOwnedHouseId(type);
                if (typeHouseID == houseID)
                    goto Out;
            }
        }
        
        return false;

        Out: ;
        
        // 找一下有没有园圃
        return TryObtainGardensAround(out objectIDs);
    }
    
    private static bool TryObtainGardensAround(out List<ulong> objectIDs)
    {
        objectIDs = [];
        
        var outdoorZone = HousingManager.Instance()->OutdoorTerritory;
        if (outdoorZone == null) return false;
        
        // 找一下有没有园圃
        List<(ulong GameObjectID, Vector3 Position)> gardenCenters = [];
        foreach (var housingObj in outdoorZone->FurnitureStruct.ObjectManager.ObjectArray.Objects)
        {
            if (housingObj == null || housingObj.Value == null) continue;
            if (housingObj.Value->ObjectKind != FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.HousingEventObject) continue;
            if (housingObj.Value->BaseId     != 131128) continue;
            if (LocalPlayerState.DistanceTo3D(housingObj.Value->Position) > 10) continue;
            
            gardenCenters.Add(new(housingObj.Value->GetGameObjectId(), housingObj.Value->Position));
        }
        if (gardenCenters.Count == 0) return false;
        
        // 园圃家具周围绕一圈的那个实际可交互的坑位
        objectIDs = DService.Instance().ObjectTable
                            .Where(x => x is { ObjectKind: ObjectKind.EventObj, DataID: 2003757 } &&
                                        gardenCenters.Any(g => Vector3.DistanceSquared(x.Position, g.Position) <= 25))
                            .Select(x => x.GameObjectID)
                            .ToList();
        return objectIDs.Count > 0;
    }

    private static bool CheckFertilizerState()
    {
        if (SelectString != null) return false;

        return Inventory->IsVisible          ||
               InventoryLarge->IsVisible     ||
               InventoryExpansion->IsVisible ||
               !DService.Instance().Condition[ConditionFlag.OccupiedInQuestEvent];
    }

    private bool ClickFertilizer()
    {
        if (SelectString != null) return false;
        if (!DService.Instance().Condition[ConditionFlag.OccupiedInQuestEvent]) return true;
        if (ModuleConfig.FertilizerSelected == 0 ||
            !PlayerInventories.TryGetFirstItem(x => x.ItemId == ModuleConfig.FertilizerSelected, out var fertilizerItem))
        {
            TaskHelper.Abort();
            return true;
        }

        AgentInventoryContext.Instance()->
            OpenForItemSlot(fertilizerItem->Container,
                            fertilizerItem->Slot,
                            0,
                            AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory)->AddonId);

        TaskHelper.Enqueue(() => ClickContextMenu(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(3).Text.ToString()), weight: 2);
        return true;
    }

    private static bool ClickGardenEntryByText(string text)
    {
        if (!SelectString->IsAddonAndNodesReady())
            return false;

        if (!TryScanSelectStringText(text,                                                                  out var index))
            TryScanSelectStringText(LuminaGetter.GetRowOrDefault<HousingGardeningPlant>(1).Text.ToString(), out index);

        return ClickSelectString(index);
    }

    #endregion

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        TargetManager.Instance().Unreg(OnSetHardTarget);
    }

    private class Config : ModuleConfiguration
    {
        public uint SeedSelected;
        public uint SoilSelected;
        public uint FertilizerSelected;
    }
}
