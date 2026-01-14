using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Action = Lumina.Excel.Sheets.Action;
using Mount = Lumina.Excel.Sheets.Mount;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoUseMountAction : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoUseMountActionTitle"),
        Description = GetLoc("AutoUseMountActionDescription"),
        Category    = ModuleCategories.General,
        Author      = ["逆光", "Bill"]
    };

    private static Config                 ModuleConfig = null!;
    private static LuminaSearcher<Mount>? MountSearcher;
    
    private static string MountSearchInput = string.Empty;
    
    private static uint SelectedActionID;
    private static uint SelectedMountID;

    private static readonly FrozenSet<ConditionFlag> InvalidConditions = [ConditionFlag.InFlight, ConditionFlag.Diving];

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        TaskHelper ??= new();

        MountSearcher ??= new(LuminaGetter.Get<Mount>()
                                          .Where(x => x.MountAction.RowId > 0)
                                          .Where(x => x.Icon              > 0)
                                          .Where(x => !string.IsNullOrEmpty(x.Singular.ToString()))
                                          .GroupBy(x => x.Singular.ToString())
                                          .Select(x => x.First()),
                              [x => x.Singular.ToString()]);
        
        DService.Instance().Condition.ConditionChange += OnConditionChanged;
        UseActionManager.Instance().RegPostUseActionLocation(OnPostUseAction);

        if (DService.Instance().Condition[ConditionFlag.Mounted])
            OnConditionChanged(ConditionFlag.Mounted, true);
    }

    protected override void Uninit()
    {
        DService.Instance().Condition.ConditionChange -= OnConditionChanged;
        UseActionManager.Instance().Unreg(OnPostUseAction);
        
        TaskHelper?.Abort();
    }

    protected override void ConfigUI()
    {
        using var table = ImRaii.Table("MountActionsTable", 3);
        if (!table) return;

        // 设置列
        ImGui.TableSetupColumn("坐骑", ImGuiTableColumnFlags.None, 200);
        ImGui.TableSetupColumn("动作", ImGuiTableColumnFlags.None, 200);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.None, 80);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        
        ImGui.TableNextColumn();
        if (ImGuiOm.SelectableIconCentered("AddNewAction", FontAwesomeIcon.Plus))
            ImGui.OpenPopup("AddNewActionPopup");

        using (var popup = ImRaii.Popup("AddNewActionPopup"))
        {
            if (popup)
            {
                ImGui.SetNextItemWidth(250f * GlobalFontScale);
                using (var combo = ImRaii.Combo($"{LuminaWrapper.GetAddonText(4964)}##MountSelectCombo",
                                                SelectedMountID > 0 && LuminaGetter.TryGetRow(SelectedMountID, out Mount selectedMount)
                                                    ? $"{selectedMount.Singular.ToString()}"
                                                    : string.Empty,
                                                ImGuiComboFlags.HeightLarge))
                {
                    if (combo)
                    {
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.InputTextWithHint("###MountSearchInput", GetLoc("PleaseSearch"), ref MountSearchInput, 128))
                            MountSearcher?.Search(MountSearchInput);

                        if (MountSearcher != null)
                        {
                            foreach (var mount in MountSearcher.SearchResult)
                            {
                                if (!ImageHelper.TryGetGameIcon(mount.Icon, out var textureWrap)) continue;

                                if (ImGuiOm.SelectableImageWithText(textureWrap.Handle,
                                                                    new(ImGui.GetTextLineHeightWithSpacing()),
                                                                    $"{mount.Singular.ToString()}",
                                                                    mount.RowId == SelectedMountID))
                                    SelectedMountID = mount.RowId;
                            }
                        }
                    }
                }

                if (SelectedMountID > 0                                              &&
                    LuminaGetter.TryGetRow(SelectedMountID, out Mount mountSelected) &&
                    mountSelected.MountAction.ValueNullable is { Action: { Count: > 0 } actions })
                {
                    ImGui.SetNextItemWidth(250f * GlobalFontScale);
                    using var combo = ImRaii.Combo($"{GetLoc("Action")}###ActionSelectCombo",
                                                   LuminaWrapper.GetActionName(SelectedActionID),
                                                   ImGuiComboFlags.None);
                    if (combo)
                    {
                        foreach (var action in actions)
                        {
                            if (action.RowId == 0) continue;
                            if (!ImageHelper.TryGetGameIcon(action.Value.Icon, out var textureWrap)) continue;

                            if (ImGuiOm.SelectableImageWithText(textureWrap.Handle, new(ImGui.GetTextLineHeightWithSpacing()), $"{action.Value.Name}",
                                                                action.RowId == SelectedActionID))
                                SelectedActionID = action.RowId;
                        }
                    }
                }

                ImGui.Spacing();
                using (ImRaii.Disabled(SelectedMountID == 0 || SelectedActionID == 0))
                {
                    if (ImGui.Button(GetLoc("Add")))
                    {
                        var newAction = new MountAction(SelectedMountID, SelectedActionID);
                        if (ModuleConfig.MountActions.TryAdd(newAction.MountID, newAction))
                            ModuleConfig.Save(this);

                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }

        // 显示已配置的动作列表
        foreach (var kv in ModuleConfig.MountActions)
        {
            var action = kv.Value;
            ImGui.TableNextRow();

            // 坐骑ID和特性
            ImGui.TableNextColumn();
            if (LuminaGetter.TryGetRow<Mount>(action.MountID, out var mountRow) && ImageHelper.TryGetGameIcon(mountRow.Icon, out var mountIcon))
                ImGuiOm.TextImage($"{mountRow.Singular.ToString()}", mountIcon.Handle, new(ImGui.GetTextLineHeightWithSpacing()));

            // 动作ID
            ImGui.TableNextColumn();
            if (LuminaGetter.TryGetRow<Action>(action.ActionID, out var actionRow) && ImageHelper.TryGetGameIcon(actionRow.Icon, out var actionIcon))
                ImGuiOm.TextImage($"{actionRow.Name.ToString()}", actionIcon.Handle, new(ImGui.GetTextLineHeightWithSpacing()));

            // 删除按钮
            ImGui.TableNextColumn();
            if (ImGuiOm.ButtonIcon($"{action.MountID}_Delete", FontAwesomeIcon.TrashAlt))
            {
                ModuleConfig.MountActions.Remove(action.MountID);
                ModuleConfig.Save(this);
            }
        }
    }
    
    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (InvalidConditions.Contains(flag))
        {
            if (value)
                TaskHelper.Abort();
            else
            {
                if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer || 
                    !ModuleConfig.MountActions.ContainsKey(localPlayer.CurrentMount?.RowId ?? 0)) return;
                TaskHelper.Enqueue(UseAction);
            }
        }

        if (flag == ConditionFlag.Mounted)
        {
            if (value)
            {
                if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer || 
                    !ModuleConfig.MountActions.ContainsKey(localPlayer.CurrentMount?.RowId ?? 0)) return;
                TaskHelper.Enqueue(UseAction);
            }
            else
                TaskHelper.Abort();
        }
    }

    private void OnPostUseAction(bool result, ActionType actionType, uint actionID, ulong targetID, Vector3 location, uint extraParam, byte a7)
    {
        if (actionType != ActionType.Action) return;
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;

        var mountID = localPlayer.CurrentMount?.RowId ?? 0;
        if (!ModuleConfig.MountActions.TryGetValue(mountID, out var action) || action.ActionID != actionID) return;
        
        TaskHelper.Enqueue(UseAction);
    }
    
    private static bool UseAction()
    {
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return true;

        var mountID = localPlayer.CurrentMount?.RowId ?? 0;
        if (!ModuleConfig.MountActions.TryGetValue(mountID, out var action)) return true;

        if (ActionManager.Instance()->GetActionStatus(ActionType.Action, action.ActionID) != 0) return false;

        ActionManager.Instance()->UseAction(ActionType.Action, action.ActionID);
        return true;
    }

    private class Config : ModuleConfiguration
    {
        public Dictionary<uint, MountAction> MountActions { get; set; } = new();
    }

    private class MountAction : IEquatable<MountAction>
    {
        public uint MountID  { get; set; }
        public uint ActionID { get; set; }

        public MountAction() { }

        public MountAction(uint mountID, uint actionID)
        {
            MountID  = mountID;
            ActionID = actionID;
        }

        public bool Equals(MountAction? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return MountID == other.MountID;
        }

        public override bool Equals(object? obj) =>
            obj is MountAction other && Equals(other);

        public override int GetHashCode() =>
            (int)MountID;
    }
}
