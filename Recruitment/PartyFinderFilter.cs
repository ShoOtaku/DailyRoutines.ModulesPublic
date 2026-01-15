using DailyRoutines.Abstracts;
using DailyRoutines.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Interface.Components;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DailyRoutines.ModulesPublic;

public class PartyFinderFilter : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("PartyFinderFilterTitle"),
        Description = GetLoc("PartyFinderFilterDescription"),
        Category    = ModuleCategories.Recruitment,
        Author      = ["status102"]
    };
    
    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private static Config ModuleConfig = null!;

    private static int  BatchIndex;
    private static bool IsSecret;
    private static bool IsRaid;
    private static bool ManualMode;
    
    private static readonly HashSet<(ushort, string)> DescriptionSet = [];
    
    protected override unsafe void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        Overlay ??= new Overlay(this);

        DService.Instance().PartyFinder.ReceiveListing += OnReceiveListing;

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "LookingForGroup", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LookingForGroup", OnAddon);
        if (LookingForGroup->IsAddonAndNodesReady())
            OnAddon(AddonEvent.PostSetup, null);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("PartyFinderFilter-FilterDuplicate"), ref ModuleConfig.FilterSameDescription))
            SaveConfig(ModuleConfig);

        ImGui.Spacing();

        DrawHighEndSettings();

        ImGui.Spacing();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("PartyFinderFilter-DescriptionRegexFilter"));

        ImGui.Spacing();

        DrawRegexFilterSettings();
    }

    private void DrawHighEndSettings()
    {
        using var group = ImRaii.Group();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("PartyFinderFilter-HighEndFilter"));

        using var indent = ImRaii.PushIndent();

        if (ImGui.Checkbox(GetLoc("PartyFinderFilter-HighEndFilterSameJob"), ref ModuleConfig.HighEndFilterSameJob))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox($"{GetLoc("PartyFinderFilter-HighEndFilterRoleCount")}", ref ModuleConfig.HighEndFilterRoleCount))
            SaveConfig(ModuleConfig);
        ImGuiOm.HelpMarker(GetLoc("PartyFinderFilter-HighEndFilterRoleCountHelp"), 20f * GlobalFontScale);

        ImGui.SameLine();
        ImGuiComponents.ToggleButton("###IsHighEndRoleCountFilterManualMode", ref ManualMode);

        ImGui.SameLine();
        ImGui.TextUnformatted(GetLoc(ManualMode ? "ManualMode" : "AutoMode"));

        if (!ModuleConfig.HighEndFilterRoleCount)
            return;

        var changed = false;
        
        using var pushIndent = ImRaii.PushIndent();
        using var itemWidth  = ImRaii.ItemWidth(50f * GlobalFontScale);

        using (ImRaii.Group())
        {
            ImGui.InputInt($"{LuminaWrapper.GetAddonText(1082)}", ref ModuleConfig.FilterJobTypeCountData.Tank);
            if (ImGui.IsItemDeactivatedAfterEdit())
                changed = true;
        
            ImGui.InputInt($"{LuminaWrapper.GetAddonText(11300)}", ref ModuleConfig.FilterJobTypeCountData.PureHealer);
            if (ImGui.IsItemDeactivatedAfterEdit())
                changed = true;
        
            ImGui.InputInt($"{LuminaWrapper.GetAddonText(11301)}", ref ModuleConfig.FilterJobTypeCountData.ShieldHealer);
            if (ImGui.IsItemDeactivatedAfterEdit())
                changed = true;
        }

        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.InputInt($"{LuminaWrapper.GetAddonText(1084)}", ref ModuleConfig.FilterJobTypeCountData.Melee);
            if (ImGui.IsItemDeactivatedAfterEdit())
                changed = true;
        
            ImGui.InputInt($"{LuminaWrapper.GetAddonText(1085)}", ref ModuleConfig.FilterJobTypeCountData.PhysicalRanged);
            if (ImGui.IsItemDeactivatedAfterEdit())
                changed = true;
        
            ImGui.InputInt($"{LuminaWrapper.GetAddonText(1086)}", ref ModuleConfig.FilterJobTypeCountData.MagicalRanged);
            if (ImGui.IsItemDeactivatedAfterEdit())
                changed = true;
        }
        
        if (changed)
            SaveConfig(ModuleConfig);
    }

    private void DrawRegexFilterSettings()
    {
        using var indent = ImRaii.PushIndent();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, GetLoc("PartyFinderFilter-AddPreset")))
            ModuleConfig.BlackList.Add(new(true, string.Empty));

        ImGui.SameLine();
        DrawWorkModeSettings();

        var index = 0;
        foreach (var item in ModuleConfig.BlackList.ToList())
        {
            var enableState = item.Key;
            if (ImGui.Checkbox($"##available{index}", ref enableState))
            {
                ModuleConfig.BlackList[index] = new(enableState, item.Value);
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            if (DrawRegexFilterItemText(index, item))
                index++;
        }
    }

    private void DrawWorkModeSettings()
    {
        using var group = ImRaii.Group();

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("WorkMode")}:");

        ImGui.SameLine();
        if (ImGuiComponents.ToggleButton("ModeToggle", ref ModuleConfig.IsWhiteList))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGui.TextUnformatted(ModuleConfig.IsWhiteList ? GetLoc("Whitelist") : GetLoc("Blacklist"));

        ImGui.SameLine();
        ImGuiOm.HelpMarker(GetLoc("PartyFinderFilter-WorkModeHelp"), 20f * GlobalFontScale);
    }

    private bool DrawRegexFilterItemText(int index, KeyValuePair<bool, string> item)
    {
        var value = item.Value;
        ImGui.InputText($"##{index}", ref value, 500);

        if (ImGui.IsItemDeactivatedAfterEdit())
            HandleRegexUpdate(index, item.Key, value);

        ImGui.SameLine();
        if (ImGuiOm.ButtonIcon($"##Delete{index}", FontAwesomeIcon.Trash))
            ModuleConfig.BlackList.RemoveAt(index);
        return true;
    }

    private void HandleRegexUpdate(int index, bool key, string value)
    {
        try
        {
            _ = new Regex(value);
            ModuleConfig.BlackList[index] = new(key, value);
            SaveConfig(ModuleConfig);
        }
        catch (ArgumentException)
        {
            NotificationWarning(GetLoc("PartyFinderFilter-RegexError"));
            ModuleConfig = LoadConfig<Config>() ?? new Config();
        }
    }

    private static void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (BatchIndex != args.BatchNumber)
        {
            IsSecret = listing.SearchArea.HasFlag(SearchAreaFlags.Private);
            IsRaid = listing.Category == DutyCategory.HighEndDuty;
            BatchIndex = args.BatchNumber;
            DescriptionSet.Clear();
        }

        if (IsSecret)
            return;

        args.Visible &= FilterBySameDescription(listing);
        args.Visible &= FilterByRegexList(listing);
        args.Visible &= FilterByHighEndSameJob(listing);
        args.Visible &= FilterByHighEndSameRole(listing);
    }

    private static bool FilterBySameDescription(IPartyFinderListing listing)
    {
        if (!ModuleConfig.FilterSameDescription)
            return true;

        var description = listing.Description.ToString();
        if (string.IsNullOrWhiteSpace(description))
            return true;

        return DescriptionSet.Add((listing.RawDuty, description));
    }

    private static bool FilterByRegexList(IPartyFinderListing listing)
    {
        var description = listing.Description.ToString();
        if (string.IsNullOrEmpty(description))
            return true;

        var isMatch = ModuleConfig.BlackList
                                  .Where(i => i.Key)
                                  .Any(item => Regex.IsMatch(listing.Name.ToString(), item.Value) ||
                                               Regex.IsMatch(description, item.Value));

        return ModuleConfig.IsWhiteList ? isMatch : !isMatch;
    }

    private static bool FilterByHighEndSameJob(IPartyFinderListing listing)
    {
        if (!ModuleConfig.HighEndFilterSameJob) return true;
        if (!IsRaid) return true;

        var job = LocalPlayerState.ClassJobData;
        // 生产职业 / 基础职业
        if (job.DohDolJobIndex == -1 || job.ClassJobParent.RowId == job.RowId)
            return true;

        foreach (var present in listing.JobsPresent)
        {
            if (present.RowId == LocalPlayerState.ClassJob)
                return false;
        }

        return true;
    }

    private static bool FilterByHighEndSameRole(IPartyFinderListing listing)
    {
        if (!ModuleConfig.HighEndFilterRoleCount) return true;
        if (!IsRaid) return true;

        var job = LocalPlayerState.ClassJobData;
        if (ManualMode)
        {
            var filter0 = JobTypeCounter(1, ModuleConfig.FilterJobTypeCountData.Tank,           job);
            var filter1 = JobTypeCounter(2, ModuleConfig.FilterJobTypeCountData.PureHealer,     job);
            var filter2 = JobTypeCounter(6, ModuleConfig.FilterJobTypeCountData.ShieldHealer,   job);
            var filter3 = JobTypeCounter(3, ModuleConfig.FilterJobTypeCountData.Melee,          job);
            var filter4 = JobTypeCounter(4, ModuleConfig.FilterJobTypeCountData.PhysicalRanged, job);
            var filter5 = JobTypeCounter(5, ModuleConfig.FilterJobTypeCountData.MagicalRanged,  job);

            return filter0 && filter1 && filter2 && filter3 && filter4 && filter5;
        }

        return job.JobType switch
        {
            0 => true,
            1 => JobTypeCounter(1, ModuleConfig.FilterJobTypeCountData.Tank,           job),
            2 => JobTypeCounter(2, ModuleConfig.FilterJobTypeCountData.PureHealer,     job),
            3 => JobTypeCounter(3, ModuleConfig.FilterJobTypeCountData.Melee,          job),
            4 => JobTypeCounter(3, ModuleConfig.FilterJobTypeCountData.PhysicalRanged, job),
            5 => JobTypeCounter(3, ModuleConfig.FilterJobTypeCountData.MagicalRanged,  job),
            6 => JobTypeCounter(6, ModuleConfig.FilterJobTypeCountData.ShieldHealer,   job),
            _ => true,
        };

        bool JobTypeCounter(int jobType, int maxCount, ClassJob currentJob)
        {
            if (maxCount == -1)
                return true;

            var count = 0;
            var hasSlot = false;

            var slots       = listing.Slots.ToList();
            var jobsPresent = listing.JobsPresent.ToList();
            foreach (var i in Enumerable.Range(0, 8))
            {
                if (slots.Count <= i || jobsPresent.Count <= i || count >= maxCount)
                    break;

                if (jobsPresent.ElementAt(i).Value.RowId != 0)
                {
                    // 如果该位置已有玩家，检查职业类型
                    if (jobsPresent.ElementAt(i).Value.JobType == jobType)
                        count++;
                }
                else if (!hasSlot) // 有空位后不再检查
                {
                    // 检查空位是否允许当前角色类型
                    if (ManualMode)
                    {
                        // 手动模式：检查所有同类角色是否有空位
                        foreach (var playerJob in LuminaGetter.Get<ClassJob>().Where(j => j.RowId != 0 && j.JobType == jobType))
                        {
                            if (Enum.TryParse<JobFlags>(playerJob.NameEnglish.ToString().Replace(" ", string.Empty), out var flag) &&
                                slots.ElementAt(i)[flag])
                            {
                                hasSlot = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // 自动模式：检查当前职业是否有空位
                        if (Enum.TryParse<JobFlags>(currentJob.NameEnglish.ToString().Replace(" ", string.Empty), out var flag) && slots.ElementAt(i)[flag])
                            hasSlot = true;
                    }
                }
            }

            return count < maxCount && hasSlot;
        }
    }

    private void OnAddon(AddonEvent type, AddonArgs? args) =>
        ToggleOverlayConfig(type == AddonEvent.PostSetup);

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        DService.Instance().PartyFinder.ReceiveListing -= OnReceiveListing;
    }

    private class Config : ModuleConfiguration
    {
        public List<KeyValuePair<bool, string>> BlackList = [];

        public bool IsWhiteList;

        public bool FilterSameDescription = true;
        public bool HighEndFilterSameJob = true;

        public bool HighEndFilterRoleCount = true;
        
        // T2, 血奶1, 盾奶1, 近2, 远1, 法2
        public (int Tank, int PureHealer, int ShieldHealer, int Melee, int PhysicalRanged, int MagicalRanged) FilterJobTypeCountData = (2, 1, 1, 2, 1, 2); 
    }
}
