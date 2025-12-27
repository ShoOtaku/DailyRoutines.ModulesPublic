using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
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

    private static string SearchFilterSeed = string.Empty;

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeLimitMS = 10_000 };

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "HousingGardening", OnAddon);
    }

    protected override void ConfigUI()
    {
        using var disabled = ImRaii.Disabled(TaskHelper?.IsBusy ?? true);

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

        if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
            StartPlant();
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();

        ImGui.Spacing();

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (SingleSelectCombo("SeedSelectCombo",
                              PresetSheet.Seeds,
                              ref ModuleConfig.SeedSelected,
                              ref SearchFilterSeed,
                              x => $"{x.Name.ExtractText()} ({x.RowId})",
                              [new(LuminaWrapper.GetAddonText(6412), ImGuiTableColumnFlags.WidthStretch, 0)],
                              [
                                  x => () =>
                                  {
                                      var icon = ImageHelper.GetGameIcon(x.Icon);
                                      if (ImGuiOm.SelectableImageWithText(
                                              icon.Handle,
                                              new(ImGui.GetTextLineHeightWithSpacing()),
                                              x.Name.ExtractText(),
                                              x.RowId == ModuleConfig.SeedSelected,
                                              ImGuiSelectableFlags.DontClosePopups | ImGuiSelectableFlags.SpanAllColumns))
                                          ModuleConfig.SeedSelected = x.RowId;
                                  }
                              ],
                              [x => x.Name.ExtractText(), x => x.RowId.ToString()],
                              true))
            SaveConfig(ModuleConfig);
        
        ImGui.SameLine();
        ImGui.Text(LuminaWrapper.GetAddonText(6412));

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        using (ImRaii.PushId("SoilSelectCombo"))
        {
            if (SingleSelectCombo("SoilSelectCombo",
                                  PresetSheet.Soils, 
                                  ref ModuleConfig.SoilSelected, 
                                  ref SearchFilterSeed,
                                  x => $"{x.Name.ExtractText()} ({x.RowId})",
                                  [new(LuminaWrapper.GetAddonText(6411), ImGuiTableColumnFlags.WidthStretch, 0)],
                                  [
                                      x => () =>
                                      {
                                          var icon = ImageHelper.GetGameIcon(x.Icon);
                                          if (ImGuiOm.SelectableImageWithText(
                                                  icon.Handle, new(ImGui.GetTextLineHeightWithSpacing()),
                                                  x.Name.ExtractText(), x.RowId == ModuleConfig.SeedSelected,
                                                  ImGuiSelectableFlags.DontClosePopups))
                                              ModuleConfig.SoilSelected = x.RowId;
                                      }
                                  ],
                                  [x => x.Name.ExtractText(), x => x.RowId.ToString()],
                                  true))
                SaveConfig(ModuleConfig);
        }
        
        ImGui.SameLine();
        ImGui.Text(LuminaWrapper.GetAddonText(6411));
    }

    private void DrawAutoGather()
    {
        using var id = ImRaii.PushId("AutoGather");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoGather")}");

        using var indent = ImRaii.PushIndent();

        if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
            StartGather();
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();
    }

    private void DrawAutoFertilize()
    {
        using var id = ImRaii.PushId("AutoFertilize");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoFertilize")}");

        using var indent = ImRaii.PushIndent();

        if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
            StartFertilize();
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();
        
        ImGui.Spacing();

        ImGui.SetNextItemWidth(300f * GlobalFontScale);
        if (SingleSelectCombo("FertilizersSelectCombo",
                              PresetSheet.Fertilizers,
                              ref ModuleConfig.FertilizerSelected,
                              ref SearchFilterSeed,
                              x => $"{x.Name.ExtractText()} ({x.RowId})",
                              [new(LuminaWrapper.GetAddonText(6417), ImGuiTableColumnFlags.WidthStretch, 0)],
                              [
                                  x => () =>
                                  {
                                      if (ImGuiOm.SelectableImageWithText(
                                              ImageHelper.GetGameIcon(x.Icon).Handle,
                                              new(ImGui.GetTextLineHeightWithSpacing()),
                                              x.Name.ExtractText(),
                                              x.RowId == ModuleConfig.SeedSelected,
                                              ImGuiSelectableFlags.DontClosePopups | ImGuiSelectableFlags.SpanAllColumns))
                                          ModuleConfig.FertilizerSelected = x.RowId;
                                  }
                              ],
                              [x => x.Name.ExtractText(), x => x.RowId.ToString()],
                              true))
            ModuleConfig.Save(this);
        
        ImGui.SameLine();
        ImGui.Text(LuminaWrapper.GetAddonText(6417));
    }
    
    private void DrawAutoTend()
    {
        using var id = ImRaii.PushId("AutoTend");
        
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoGardensWork-AutoTend")}");

        using var indent = ImRaii.PushIndent();

        if (ImGui.Button($"{FontAwesomeIcon.Play.ToIconString()} {GetLoc("Start")}"))
            StartTend();
        
        ImGui.SameLine();
        if (ImGui.Button($"{FontAwesomeIcon.Stop.ToIconString()} {GetLoc("Stop")}"))
            TaskHelper.Abort();
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (ModuleConfig.SeedSelected == 0 || ModuleConfig.SoilSelected == 0) return;

        if (!TryGetFirstInventoryItem(PlayerInventories, x => x.ItemId == ModuleConfig.SeedSelected, out var seedItem) ||
            !TryGetFirstInventoryItem(PlayerInventories, x => x.ItemId == ModuleConfig.SoilSelected, out var soilItem))
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

    private void StartAction(string entryKeyword, Action extraAction = null)
    {
        if (DService.ObjectTable.LocalPlayer == null) return;

        foreach (var garden in ObtainGardensAround())
        {
            TaskHelper.Enqueue(() => new EventStartPackt(garden, 721047).Send(), $"交互园圃: {garden}");
            TaskHelper.Enqueue(() => ClickEntryByText(entryKeyword),      "点击");
            extraAction?.Invoke();
            TaskHelper.Enqueue(() => !DService.Condition[ConditionFlag.OccupiedInQuestEvent], "等待退出交互状态");
        }
    }

    private void StartGather() => StartAction(LuminaGetter.GetRow<HousingGardeningPlant>(6)!.Value.Text.ExtractText());

    private void StartTend() => StartAction(LuminaGetter.GetRow<HousingGardeningPlant>(4)!.Value.Text.ExtractText());

    private void StartPlant() =>
        StartAction(LuminaGetter.GetRow<HousingGardeningPlant>(2)!.Value.Text.ExtractText(), () => TaskHelper.DelayNext(250));

    private void StartFertilize() =>
        StartAction(LuminaGetter.GetRow<HousingGardeningPlant>(3)!.Value.Text.ExtractText(), () =>
        {
            TaskHelper.Enqueue(CheckFertilizerState);
            TaskHelper.Enqueue(ClickFertilizer);
            TaskHelper.Enqueue(() => !DService.Condition[ConditionFlag.OccupiedInQuestEvent]);
        });


    /// <summary>
    ///     获取距离为 10 以内的园圃
    /// </summary>
    /// <returns>有效园圃的 Object ID</returns>
    private static List<ulong> ObtainGardensAround() =>
        DService.ObjectTable
                .Where(x => x is { ObjectKind: ObjectKind.EventObj, DataID: 2003757 } &&
                            Vector2.DistanceSquared(x.Position.ToVector2(), DService.ObjectTable.LocalPlayer.Position.ToVector2()) <= 100)
                .Select(x => x.GameObjectID).ToList();

    private static bool? CheckFertilizerState()
    {
        if (SelectString != null) return false;

        return Inventory->IsVisible || InventoryLarge->IsVisible || InventoryExpansion->IsVisible ||
               !DService.Condition[ConditionFlag.OccupiedInQuestEvent];
    }

    private bool? ClickFertilizer()
    {
        if (SelectString != null) return false;
        if (!DService.Condition[ConditionFlag.OccupiedInQuestEvent]) return true;
        if (ModuleConfig.FertilizerSelected == 0 ||
            !TryGetFirstInventoryItem(PlayerInventories, x => x.ItemId == ModuleConfig.FertilizerSelected, out var fertilizerItem))
        {
            TaskHelper.Abort();
            return true;
        }

        AgentInventoryContext.Instance()->
            OpenForItemSlot(fertilizerItem->Container,
                            fertilizerItem->Slot,
                            0,
                            AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory)->AddonId);

        TaskHelper.Enqueue(() => ClickContextMenuByText(LuminaGetter.GetRow<HousingGardeningPlant>(3)!.Value.Text.ExtractText()), weight: 2);
        return true;
    }

    private static bool? ClickContextMenuByText(string text)
    {
        if (!DService.Condition[ConditionFlag.OccupiedInQuestEvent]) return true;

        if (!IsAddonAndNodesReady(ContextMenuXIV)) return false;
        if (!TryScanContextMenuText(ContextMenuXIV, text, out var index))
        {
            ContextMenuXIV->FireCloseCallback();
            ContextMenuXIV->Close(true);
            return true;
        }

        Callback(ContextMenuXIV, true, 0, index, 0U, 0, 0);
        return false;
    }

    private static bool? ClickEntryByText(string text)
    {
        if (!IsAddonAndNodesReady(SelectString))
            return false;

        if (!TryScanSelectStringText(SelectString, text,                                                                    out var index))
            TryScanSelectStringText(SelectString,  LuminaGetter.GetRow<HousingGardeningPlant>(1)!.Value.Text.ExtractText(), out index);

        return ClickSelectString(index);
    }

    protected override void Uninit()
    {
        if (ModuleConfig != null)
            SaveConfig(ModuleConfig);

        DService.AddonLifecycle.UnregisterListener(OnAddon);
    }

    private class Config : ModuleConfiguration
    {
        public uint SeedSelected;
        public uint SoilSelected;
        public uint FertilizerSelected;
    }
}
