using System;
using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Lumina.Excel.Sheets;
using Status = Lumina.Excel.Sheets.Status;

namespace DailyRoutines.ModulesPublic;

public unsafe class CustomStatusPrevent : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("CustomStatusPrevent-Title"),
        Description = GetLoc("CustomStatusPrevent-Description"),
        Category    = ModuleCategories.Action,
    };

    private static Config ModuleConfig = null!;
    private static int    NewStatusID;

    public enum DetectType
    {
        Self,
        Target
    }

    public class CustomStatusEntry
    {
        public bool       IsEnabled { get; set; } = true;
        public DetectType Target    { get; set; } = DetectType.Target;
        public uint       StatusID  { get; init; }
        public float      Threshold { get; set; } = 3.5f;
    }

    private class Config : ModuleConfiguration
    {
        public List<CustomStatusEntry> StatusEntries { get; set; } = [];
    }

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        UseActionManager.RegPreUseAction(OnPreUseAction);
    }

    protected override void Uninit() =>
        UseActionManager.Unreg(OnPreUseAction);

    private static void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteID)
    {
        if (actionType != ActionType.Action) return;
        if (ModuleConfig.StatusEntries.Count == 0) return;

        foreach (var entry in ModuleConfig.StatusEntries)
        {
            if (!entry.IsEnabled) continue;

            var actor = entry.Target switch
            {
                DetectType.Self => Control.GetLocalPlayer(),
                DetectType.Target => DService.Targets.Target is IBattleChara chara ? chara.ToStruct() : null,
                _ => null
            };

            if (actor == null) continue;

            if (HasStatus(&actor->StatusManager, entry.StatusID, entry.Threshold))
            {
                isPrevented = true;
                return;
            }
        }
    }

    private static bool HasStatus(StatusManager* statusManager, uint statusID, float threshold)
    {
        var statusIndex = statusManager->GetStatusIndex(statusID);
        return statusIndex != -1 && statusManager->Status[statusIndex].RemainingTime < threshold;
    }

    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{GetLoc("StatusID")}:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputInt("##NewStatusId", ref NewStatusID);

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("AddNewRule")))
        {
            if (NewStatusID > 0 && GetStatusName((uint)NewStatusID) != null)
            {
                ModuleConfig.StatusEntries.Add(new CustomStatusEntry { StatusID = (uint)NewStatusID });
                SaveConfig(ModuleConfig);
                NewStatusID = 0;
            }
        }
        ImGuiOm.HelpMarker(GetLoc("AddRuleHelp"));

        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        using var table = ImRaii.Table("###CustomStatusTable", 7, tableFlags);
        if (!table) return;

        ImGui.TableSetupColumn(GetLoc("Enabled"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2);
        ImGui.TableSetupColumn(GetLoc("DetectTarget"), ImGuiTableColumnFlags.WidthFixed, 100f * GlobalFontScale);
        ImGui.TableSetupColumn(GetLoc("Icon"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2);
        ImGui.TableSetupColumn(GetLoc("StatusID"), ImGuiTableColumnFlags.WidthFixed, 100f * GlobalFontScale);
        ImGui.TableSetupColumn(GetLoc("StatusName"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(GetLoc("RemainingTimeLessThan"), ImGuiTableColumnFlags.WidthFixed, 120f * GlobalFontScale);
        ImGui.TableSetupColumn(GetLoc("Operations"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2);

        ImGui.TableHeadersRow();

        var detectTypeNames = new[] { GetLoc("Self"), GetLoc("Target") };
        int? indexToRemove = null;
        for (var i = 0; i < ModuleConfig.StatusEntries.Count; i++)
        {
            var entry = ModuleConfig.StatusEntries[i];
            if (DrawStatusEntryRow(entry, i, detectTypeNames))
                indexToRemove = i;
        }

        if (indexToRemove.HasValue)
        {
            ModuleConfig.StatusEntries.RemoveAt(indexToRemove.Value);
            SaveConfig(ModuleConfig);
        }
    }

    private bool DrawStatusEntryRow(CustomStatusEntry entry, int index, string[] detectTypeNames)
    {
        ImGui.PushID(index);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var isEnabled = entry.IsEnabled;
        if (ImGui.Checkbox("##IsEnabled", ref isEnabled))
        {
            entry.IsEnabled = isEnabled;
            SaveConfig(ModuleConfig);
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var currentTarget = (int)entry.Target;
        if (ImGui.Combo("##Target", ref currentTarget, detectTypeNames, detectTypeNames.Length))
        {
            entry.Target = (DetectType)currentTarget;
            SaveConfig(ModuleConfig);
        }

        var hasStatus = LuminaGetter.TryGetRow<Status>(entry.StatusID, out var status);

        ImGui.TableNextColumn();
        if (hasStatus)
        {
            var icon = ImageHelper.GetGameIcon(status.Icon);
            if (icon != null)
                ImGui.Image(icon.Handle, new Vector2(ImGui.GetFrameHeight()));
        }

        ImGui.TableNextColumn();
        ImGui.Text($"{entry.StatusID}");

        ImGui.TableNextColumn();
        ImGui.Text(hasStatus ? status.Name.ExtractText() : GetLoc("InvalidOrNotFound"));

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var threshold = entry.Threshold;
        ImGui.InputFloat("##Threshold", ref threshold, 0.1f, 0.5f, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            entry.Threshold = Math.Max(0, threshold);
            SaveConfig(ModuleConfig);
        }

        ImGui.TableNextColumn();
        var shouldRemove = ImGui.Button(GetLoc("Remove"));

        ImGui.PopID();
        return shouldRemove;
    }

    private static string? GetStatusName(uint statusID) =>
        LuminaGetter.TryGetRow<Status>(statusID, out var status) ? status.Name.ExtractText() : null;
}
