using System.Numerics;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using OmenTools.ImGuiOm.Widgets.Combos;
using OmenTools.Info.Game.Data;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using OmenTools.Threading;
using OmenTools.Threading.TaskHelper;

namespace DailyRoutines.ModulesPublic;

public class AutoCheckFoodUsage : ModuleBase
{
    public delegate nint CountdownInitDelegate(nint a1, nint a2);

    private const int FOOD_USAGE_COOLDOWN_SECONDS = 10;

    private static readonly CompSig                      CountdownInitSig = new("48 89 5C 24 10 57 48 83 EC 40 48 8B DA 48 8B F9 48 8B 49 08");
    private static          Hook<CountdownInitDelegate>? CountdownInitHook;

    private static Config ModuleConfig = null!;

    private static readonly JobSelectCombo JobSelectCombo = new("Job");

    private static uint   SelectedItem;
    private static string SelectItemSearch = string.Empty;
    private static bool   SelectItemIsHQ   = true;
    private static string ZoneSearch       = string.Empty;
    private static string ConditionSearch  = string.Empty;

    private static Vector2 CheckboxSize = ScaledVector2(20f);

    private static readonly DateTime LastFoodUsageTime = DateTime.MinValue;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoCheckFoodUsageTitle"),
        Description = Lang.Get("AutoCheckFoodUsageDescription"),
        Category    = ModuleCategory.Combat
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();
        foreach (var checkPoint in Enum.GetValues<FoodCheckpoint>())
            ModuleConfig.EnabledCheckpoints.TryAdd(checkPoint, false);

        TaskHelper ??= new TaskHelper { TimeoutMS = 60_000 };

        CountdownInitHook ??= CountdownInitSig.GetHook<CountdownInitDelegate>(CountdownInitDetour);
        CountdownInitHook.Enable();

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        DService.Instance().Condition.ConditionChange    += OnConditionChanged;
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoCheckFoodUsage-Checkpoint")}:");

        using (ImRaii.PushIndent())
        {
            ImGuiOm.ScaledDummy(2f);

            foreach (var checkPoint in Enum.GetValues<FoodCheckpoint>())
            {
                ImGui.SameLine();
                var state = ModuleConfig.EnabledCheckpoints[checkPoint];

                if (ImGui.Checkbox(checkPoint.ToString(), ref state))
                {
                    ModuleConfig.EnabledCheckpoints[checkPoint] = state;
                    ModuleConfig.Save(this);
                }
            }

            if (ModuleConfig.EnabledCheckpoints[FoodCheckpoint.条件变更时])
            {
                using (ImRaii.PushIndent())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored
                    (
                        KnownColor.LightSkyBlue.ToVector4(),
                        $"{Lang.Get("AutoCheckFoodUsage-WhenConditionBegin")}:"
                    );

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalUIScale);

                    using (var combo = ImRaii.Combo
                           (
                               "###ConditionBeginCombo",
                               Lang.Get("AutoCheckFoodUsage-SelectedAmount", ModuleConfig.ConditionStart.Count),
                               ImGuiComboFlags.HeightLarge
                           ))
                    {
                        if (combo)
                        {
                            if (ImGui.IsWindowAppearing())
                                ConditionSearch = string.Empty;

                            ImGui.SetNextItemWidth(-1f);
                            ImGui.InputTextWithHint
                            (
                                "###ConditionBeginSearch",
                                Lang.Get("PleaseSearch"),
                                ref ConditionSearch,
                                128
                            );
                            ImGui.Separator();

                            foreach (var conditionFlag in Enum.GetValues<ConditionFlag>())
                            {
                                if (conditionFlag is ConditionFlag.None or ConditionFlag.NormalConditions) continue;
                                var conditionName = conditionFlag.ToString();
                                if (!string.IsNullOrWhiteSpace(ConditionSearch) &&
                                    !conditionName.Contains(ConditionSearch, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (ImGui.Selectable
                                    (
                                        $"{conditionName}###{conditionFlag}_Begin",
                                        ModuleConfig.ConditionStart.Contains(conditionFlag)
                                    ))
                                {
                                    if (!ModuleConfig.ConditionStart.Remove(conditionFlag))
                                        ModuleConfig.ConditionStart.Add(conditionFlag);
                                    ModuleConfig.Save(this);
                                }
                            }
                        }
                    }

                    ImGui.AlignTextToFramePadding();
                    ImGui.TextColored
                    (
                        KnownColor.LightSkyBlue.ToVector4(),
                        $"{Lang.Get("AutoCheckFoodUsage-WhenConditionEnd")}:"
                    );

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalUIScale);

                    using (var combo = ImRaii.Combo
                           (
                               "###ConditionEndCombo",
                               Lang.Get("AutoCheckFoodUsage-SelectedAmount", ModuleConfig.ConditionEnd.Count),
                               ImGuiComboFlags.HeightLarge
                           ))
                    {
                        if (combo)
                        {
                            if (ImGui.IsWindowAppearing())
                                ConditionSearch = string.Empty;

                            ImGui.SetNextItemWidth(-1f);
                            ImGui.InputTextWithHint("###ConditionEndSearch", Lang.Get("PleaseSearch"), ref ConditionSearch, 128);

                            ImGui.Separator();

                            foreach (var conditionFlag in Enum.GetValues<ConditionFlag>())
                            {
                                if (conditionFlag is ConditionFlag.None or ConditionFlag.NormalConditions) continue;

                                var conditionName = conditionFlag.ToString();
                                if (!string.IsNullOrWhiteSpace(ConditionSearch) &&
                                    !conditionName.Contains(ConditionSearch, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (ImGui.Selectable($"{conditionName}###{conditionFlag}_End", ModuleConfig.ConditionEnd.Contains(conditionFlag)))
                                {
                                    if (!ModuleConfig.ConditionEnd.Remove(conditionFlag))
                                        ModuleConfig.ConditionEnd.Add(conditionFlag);
                                    ModuleConfig.Save(this);
                                }
                            }
                        }
                    }
                }
            }

            ImGuiOm.ScaledDummy(2f);
        }

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("Settings")}:");

        using (ImRaii.PushIndent())
        {
            ImGui.Dummy(Vector2.One);

            ImGui.SetNextItemWidth(50f * GlobalUIScale);
            ImGui.InputInt(Lang.Get("AutoCheckFoodUsage-RefreshThreshold"), ref ModuleConfig.RefreshThreshold);
            if (ImGui.IsItemDeactivatedAfterEdit())
                ModuleConfig.Save(this);

            ImGui.SameLine();
            if (ImGui.Checkbox(Lang.Get("SendChat"), ref ModuleConfig.SendChat))
                ModuleConfig.Save(this);

            ImGuiOm.HelpMarker(Lang.Get("AutoCheckFoodUsage-RefreshThresholdHelp"));
        }

        var       tableSize = (ImGui.GetContentRegionAvail() - ScaledVector2(100f)) with { Y = 0 };
        using var table     = ImRaii.Table("FoodPreset", 4, ImGuiTableFlags.Borders, tableSize);
        if (!table) return;
        ImGui.TableSetupColumn("添加", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
        ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None,       30);
        ImGui.TableSetupColumn("地区", ImGuiTableColumnFlags.None,       30);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None,       30);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableNextColumn();
        if (ImGuiOm.ButtonIconSelectable("AddNewPreset", FontAwesomeIcon.Plus))
            ImGui.OpenPopup("AddNewPresetPopup");

        using (var popup = ImRaii.Popup("AddNewPresetPopup"))
        {
            if (popup)
            {
                ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoCheckFoodUsage-AddNewPreset")}:");

                using (ImRaii.PushIndent())
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted($"{Lang.Get("Food")}:");

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalUIScale);
                    ImGuiOm.SingleSelectCombo
                    (
                        "FoodSelectCombo",
                        Sheets.Food,
                        ref SelectedItem,
                        ref SelectItemSearch,
                        x => $"{x.Name.ToString()} ({x.RowId})",
                        [new("物品", ImGuiTableColumnFlags.WidthStretch, 0)],
                        [
                            x => () =>
                            {
                                var icon = ImageHelper.GetGameIcon(x.Icon, SelectItemIsHQ);

                                if (ImGuiOm.SelectableImageWithText
                                    (
                                        icon.Handle,
                                        ScaledVector2(20f),
                                        x.Name.ToString(),
                                        x.RowId == SelectedItem,
                                        ImGuiSelectableFlags.DontClosePopups
                                    ))
                                    SelectedItem = SelectedItem == x.RowId ? 0 : x.RowId;
                            }
                        ],
                        [x => x.Name.ToString(), x => x.RowId.ToString()],
                        true
                    );

                    ImGui.SameLine();
                    ImGui.Checkbox("HQ", ref SelectItemIsHQ);

                    ImGui.SameLine();

                    using (ImRaii.Disabled(SelectedItem == 0))
                    {
                        if (ImGui.Button(Lang.Get("Add")))
                        {
                            var preset = new FoodUsagePreset(SelectedItem, SelectItemIsHQ);

                            ModuleConfig.Presets.Add(preset);
                            ModuleConfig.Save(this);
                        }
                    }
                }
            }
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(Lang.Get("Food"));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(Lang.Get("AutoCheckFoodUsage-ZoneRestrictions"));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(Lang.Get("AutoCheckFoodUsage-JobRestrictions"));

        for (var i = 0; i < ModuleConfig.Presets.Count; i++)
        {
            var       preset = ModuleConfig.Presets[i];
            using var id     = ImRaii.PushId($"{preset.ItemID}_{preset.IsHQ}_{i}");

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isEnabled = preset.Enabled;

            if (ImGui.Checkbox("", ref isEnabled))
            {
                preset.Enabled = isEnabled;
                ModuleConfig.Save(this);
            }

            CheckboxSize = ImGui.GetItemRectSize();

            ImGui.TableNextColumn();
            ImGui.Selectable
            (
                $"{LuminaGetter.GetRow<Item>(preset.ItemID)!.Value.Name.ToString()} {(preset.IsHQ ? "(HQ)" : "")}"
            );

            using (var context = ImRaii.ContextPopupItem("PresetContextMenu"))
            {
                if (context)
                {
                    if (i != 0 && ImGui.MenuItem(FontAwesomeIcon.AngleDoubleUp.ToIconString()))
                    {
                        (ModuleConfig.Presets[i], ModuleConfig.Presets[i - 1]) = (ModuleConfig.Presets[i - 1], ModuleConfig.Presets[i]);
                        ModuleConfig.Save(this);
                    }

                    if (i != ModuleConfig.Presets.Count - 1 && ImGui.MenuItem(FontAwesomeIcon.AngleDoubleDown.ToIconString()))
                    {
                        (ModuleConfig.Presets[i], ModuleConfig.Presets[i + 1]) = (ModuleConfig.Presets[i + 1], ModuleConfig.Presets[i]);
                        ModuleConfig.Save(this);
                    }

                    ImGui.Separator();

                    if (ImGui.MenuItem($"{Lang.Get("AutoCheckFoodUsage-ChangeTo")} {(preset.IsHQ ? "NQ" : "HQ")}"))
                    {
                        preset.IsHQ ^= true;
                        ModuleConfig.Save(this);
                    }

                    if (ImGui.MenuItem(Lang.Get("Delete")))
                    {
                        ModuleConfig.Presets.Remove(preset);
                        ModuleConfig.Save(this);
                        break; // 删除后跳出循环，防止修改集合时出错
                    }
                }
            }

            ImGui.TableNextColumn();
            var zones = preset.Zones;
            ImGui.SetNextItemWidth(-1f);

            using (ImRaii.PushId("ZonesSelectCombo"))
            {
                if (ImGuiOm.MultiSelectCombo
                    (
                        "ZoneSelectCombo",
                        Sheets.Zones,
                        ref zones,
                        ref ZoneSearch,
                        [
                            new("区域", ImGuiTableColumnFlags.WidthStretch, 0),
                            new("副本", ImGuiTableColumnFlags.WidthStretch, 0)
                        ],
                        [
                            x => () =>
                            {
                                if (ImGui.Selectable
                                    (
                                        $"{x.ExtractPlaceName()}##{x.RowId}",
                                        zones.Contains(x.RowId),
                                        ImGuiSelectableFlags.SpanAllColumns |
                                        ImGuiSelectableFlags.DontClosePopups
                                    ))
                                {
                                    if (!zones.Remove(x.RowId))
                                    {
                                        zones.Add(x.RowId);
                                        ModuleConfig.Save(this);
                                    }
                                }
                            },
                            x => () =>
                            {
                                var contentName = x.ContentFinderCondition.Value.Name.ToString() ?? "";
                                ImGui.TextUnformatted(contentName);
                            }
                        ],
                        [
                            x => x.ExtractPlaceName(),
                            x => x.ContentFinderCondition.Value.Name.ToString() ?? ""
                        ],
                        true
                    ))
                {
                    preset.Zones = zones;
                    ModuleConfig.Save(this);
                }

                ImGuiOm.TooltipHover(Lang.Get("AutoCheckFoodUsage-NoZoneSelectHelp"));
            }

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            JobSelectCombo.SelectedIDs = preset.ClassJobs.ToHashSet();

            if (JobSelectCombo.DrawCheckbox())
            {
                preset.ClassJobs = JobSelectCombo.SelectedIDs.ToHashSet();
                ModuleConfig.Save(this);
            }
        }
    }

    private bool EnqueueFoodRefresh()
    {
        if (!IsValidState()) return false;

        if (!IsCooldownElapsed())
        {
            TaskHelper.Abort();
            return true;
        }

        var validPresets = GetValidPresets();

        if (validPresets.Count == 0)
        {
            TaskHelper.Abort();
            return true;
        }

        if (TryGetWellFedParam(out var itemFood, out var remainingTime))
        {
            var existedStatus = validPresets.FirstOrDefault(x => ToFoodRowID(x.ItemID) == itemFood);

            if (existedStatus != null && !ShouldRefreshFood(remainingTime))
            {
                TaskHelper.Abort();
                return true;
            }
        }

        var finalPreset = validPresets.FirstOrDefault();
        TaskHelper.Enqueue(() => TakeFood(finalPreset));
        return true;
    }

    private bool TakeFood(FoodUsagePreset preset) => TakeFood(preset.ItemID, preset.IsHQ);

    private bool TakeFood(uint itemID, bool isHQ)
    {
        if (!Throttler.Shared.Throttle("AutoCheckFoodUsage-TakeFood", 1000)) return false;
        if (!IsValidState()) return false;

        TaskHelper.Enqueue(() => TakeFoodInternal(itemID, isHQ));
        return true;
    }

    private bool TakeFoodInternal(uint itemID, bool isHQ)
    {
        TaskHelper.Abort();
        if (TryGetWellFedParam(out var itemFoodId, out var remainingTime) &&
            itemFoodId                 == ToFoodRowID(itemID)             &&
            remainingTime.TotalMinutes >= 25)
            return true;

        UseActionManager.Instance().UseActionLocation(ActionType.Item, isHQ ? itemID + 100_0000 : itemID, 0xE0000000, default, 0xFFFF);

        TaskHelper.DelayNext(3_000);
        TaskHelper.Enqueue(() => CheckFoodState(itemID, isHQ));
        return true;
    }

    private bool CheckFoodState(uint itemID, bool isHQ)
    {
        TaskHelper.Abort();

        if (TryGetWellFedParam(out var itemFoodId, out var remainingTime) &&
            itemFoodId                 == ToFoodRowID(itemID)             &&
            remainingTime.TotalMinutes >= 25)
        {
            NotifyHelper.Instance().Chat(Lang.GetSe("AutoCheckFoodUsage-NoticeMessage", new SeStringBuilder().AddItemLink(itemID, isHQ)));
            return true;
        }

        TaskHelper.DelayNext(3000);
        TaskHelper.Enqueue(() => TakeFoodInternal(itemID, isHQ));
        return false;
    }

    private static unsafe bool TryGetWellFedParam(out uint itemFoodRowID, out TimeSpan remainingTime)
    {
        itemFoodRowID = 0;
        remainingTime = TimeSpan.Zero;

        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return false;

        var statusManager = localPlayer.ToStruct()->StatusManager;
        var statusIndex   = statusManager.GetStatusIndex(48);
        if (statusIndex == -1) return false;

        var status = statusManager.Status[statusIndex];
        itemFoodRowID = (uint)status.Param % 10_000;
        remainingTime = TimeSpan.FromSeconds(status.RemainingTime);
        return true;
    }

    private nint CountdownInitDetour(nint a1, nint a2)
    {
        var original = CountdownInitHook.Original(a1, a2);

        if (ModuleConfig.EnabledCheckpoints[FoodCheckpoint.倒计时开始时] && !GameMain.IsInPvPArea())
        {
            TaskHelper.Abort();
            TaskHelper.Enqueue(EnqueueFoodRefresh);
        }

        return original;
    }

    private void OnZoneChanged(ushort zone)
    {
        if (!ModuleConfig.EnabledCheckpoints[FoodCheckpoint.区域切换时] || GameMain.IsInPvPArea()) return;

        TaskHelper.Abort();
        TaskHelper.Enqueue(EnqueueFoodRefresh);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (!ModuleConfig.EnabledCheckpoints[FoodCheckpoint.条件变更时] ||
            GameMain.IsInPvPArea()                                 ||
            (!value || !ModuleConfig.ConditionStart.Contains(flag)) &&
            (value  || !ModuleConfig.ConditionEnd.Contains(flag)))
            return;

        TaskHelper.Abort();
        TaskHelper.Enqueue(EnqueueFoodRefresh);
    }

    protected override void Uninit()
    {
        DService.Instance().Condition.ConditionChange    -= OnConditionChanged;
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
    }

    private static unsafe bool IsValidState() =>
        !DService.Instance().Condition.IsBetweenAreas       &&
        !DService.Instance().Condition.IsOccupiedInEvent    &&
        !DService.Instance().Condition.IsCasting            &&
        DService.Instance().ObjectTable.LocalPlayer != null &&
        UIModule.IsScreenReady()                            &&
        ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 2) == 0;

    private static bool IsCooldownElapsed() =>
        (StandardTimeManager.Instance().Now - LastFoodUsageTime).TotalSeconds >= FOOD_USAGE_COOLDOWN_SECONDS;

    private static uint ToFoodRowID(uint id) =>
        LuminaGetter.GetRow<ItemFood>(LuminaGetter.GetRowOrDefault<Item>(id).ItemAction.Value.Data[1])?.RowId ?? 0;

    private static unsafe List<FoodUsagePreset> GetValidPresets()
    {
        var instance = InventoryManager.Instance();
        var zone     = GameState.TerritoryType;
        if (instance == null || zone == 0) return [];

        return ModuleConfig.Presets
                           .Where
                           (x => x.Enabled                                      &&
                                 (x.Zones.Count == 0 || x.Zones.Contains(zone)) &&
                                 (x.ClassJobs.Count == 0 ||
                                  x.ClassJobs.Contains(DService.Instance().ObjectTable.LocalPlayer.ClassJob.RowId)) &&
                                 instance->GetInventoryItemCount(x.ItemID, x.IsHQ) > 0
                           )
                           .OrderByDescending(x => x.Zones.Contains(zone))
                           .ToList();
    }

    private static bool ShouldRefreshFood(TimeSpan remainingTime) =>
        remainingTime <= TimeSpan.FromSeconds(ModuleConfig.RefreshThreshold) &&
        remainingTime <= TimeSpan.FromMinutes(55);

    public class FoodUsagePreset : IEquatable<FoodUsagePreset>
    {
        public FoodUsagePreset() { }

        public FoodUsagePreset(uint itemID) => ItemID = itemID;

        public FoodUsagePreset(uint itemID, bool isHQ) : this(itemID) => IsHQ = isHQ;

        public uint          ItemID    { get; set; }
        public bool          IsHQ      { get; set; } = true;
        public HashSet<uint> Zones     { get; set; } = [];
        public HashSet<uint> ClassJobs { get; set; } = [];
        public bool          Enabled   { get; set; } = true;

        public bool Equals(FoodUsagePreset? other)
            => other != null && ItemID == other.ItemID && IsHQ == other.IsHQ;

        public override bool Equals(object? obj)
            => Equals(obj as FoodUsagePreset);

        public override int GetHashCode()
            => HashCode.Combine(ItemID, IsHQ);

        public static bool operator ==(FoodUsagePreset? left, FoodUsagePreset? right) =>
            EqualityComparer<FoodUsagePreset>.Default.Equals(left, right);

        public static bool operator !=(FoodUsagePreset? left, FoodUsagePreset? right)
            => !(left == right);
    }

    private class Config : ModuleConfig
    {
        public HashSet<ConditionFlag>           ConditionEnd       = [];
        public HashSet<ConditionFlag>           ConditionStart     = [];
        public Dictionary<FoodCheckpoint, bool> EnabledCheckpoints = [];
        public List<FoodUsagePreset>            Presets            = [];
        public int                              RefreshThreshold   = 600; // 秒
        public bool                             SendChat           = true;
    }

    private enum FoodCheckpoint
    {
        区域切换时,
        倒计时开始时,
        条件变更时
    }
}
