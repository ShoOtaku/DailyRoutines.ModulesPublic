using System;
using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public class AutoLeaveDuty : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoLeaveDutyTitle"),
        Description = GetLoc("AutoLeaveDutyDescription"),
        Category    = ModuleCategories.Combat,
    };

    private static Config ModuleConfig = null!;
    
    private static string ContentSearchInput = string.Empty;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        TaskHelper ??= new();

        DService.DutyState.DutyCompleted      += OnDutyComplete;
        DService.ClientState.TerritoryChanged += OnZoneChanged;
    }

    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoLeaveDuty-ForceToLeave")}:");

        ImGui.SameLine();
        if (ImGui.Checkbox("###ForceToLeave", ref ModuleConfig.ForceToLeave))
            SaveConfig(ModuleConfig);

        if (ModuleConfig.ForceToLeave)
        {
            ImGui.SameLine();
            ImGui.TextColored(KnownColor.RoyalBlue.ToVector4(), GetLoc("AutoLeaveDuty-Note"));
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Delay")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        if (ImGui.InputInt("ms###DelayInput", ref ModuleConfig.Delay))
            ModuleConfig.Delay = Math.Max(0, ModuleConfig.Delay);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoLeaveDuty-BlacklistContents")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(250f * GlobalFontScale);
        if (ContentSelectCombo(ref ModuleConfig.BlacklistContents, ref ContentSearchInput))
            SaveConfig(ModuleConfig);

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoLeaveDuty-NoLeaveHighEndDuties")}:");

        ImGui.SameLine();
        if (ImGui.Checkbox("###NoLeaveHighEndDuties", ref ModuleConfig.NoLeaveHighEndDuties))
            SaveConfig(ModuleConfig);

        ImGuiOm.HelpMarker(GetLoc("AutoLeaveDuty-NoLeaveHighEndDutiesHelp"));
    }

    private void OnDutyComplete(object? sender, ushort zone)
    {
        if (ModuleConfig.BlacklistContents.Contains(zone)) return;
        if (ModuleConfig.NoLeaveHighEndDuties &&
            LuminaGetter.Get<ContentFinderCondition>()
                       .FirstOrDefault(x => x.HighEndDuty && x.TerritoryType.RowId == zone).RowId != 0) return;

        if (ModuleConfig.Delay > 0)
            TaskHelper.DelayNext(ModuleConfig.Delay);

        TaskHelper.Enqueue(() =>
        {
            if (!ModuleConfig.ForceToLeave && DService.Condition[ConditionFlag.InCombat]) return false;

            ExecuteCommandManager.ExecuteCommand(ExecuteCommandFlag.LeaveDuty);
            return true;
        });
    }
    
    private void OnZoneChanged(ushort obj) => TaskHelper.Abort();

    protected override void Uninit()
    {
        DService.DutyState.DutyCompleted      -= OnDutyComplete;
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
    }

    private class Config : ModuleConfiguration
    {
        public HashSet<uint> BlacklistContents = [];
        public bool NoLeaveHighEndDuties = true;
        public bool ForceToLeave;
        public int Delay;
    }
}
