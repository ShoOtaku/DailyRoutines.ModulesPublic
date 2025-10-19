using System;
using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class PlayerTargetInfoExpand : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("PlayerTargetInfoExpandTitle"),
        Description = GetLoc("PlayerTargetInfoExpandDescription"),
        Category = ModuleCategories.UIOptimization,
        ModulesConflict = ["LiveAnonymousMode"]
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static readonly List<Payload> Payloads =
    [
        new("/Name/", "名称", c => c.Name.TextValue),
        new("/Job/", "职业",
            c => c.ClassJob.ValueNullable?.Name.ExtractText() ?? LuminaGetter.GetRow<ClassJob>(0)!.Value.Name.ExtractText()),
        new("/Level/", "等级", c => c.Level.ToString()),
        new("/FCTag/", "部队", c => c.CompanyTag.TextValue),
        new("/OnlineStatus/", "在线状态",
            c => string.IsNullOrWhiteSpace(c.OnlineStatus.ValueNullable?.Name.ExtractText())
                     ? LuminaGetter.GetRow<OnlineStatus>(47)!.Value.Name.ExtractText()
                     : c.OnlineStatus.ValueNullable?.Name.ExtractText()),
        new("/Mount/", "坐骑", c => LuminaGetter.GetRow<Mount>(c.ToStruct()->Mount.MountId)!.Value.Singular.ExtractText()),
        new("/HomeWorld/", "原始服务器", c => LuminaGetter.GetRow<World>(c.ToStruct()->HomeWorld)!.Value.Name.ExtractText()),
        new("/Emote/", "情感动作",
            c => LuminaGetter.GetRow<Emote>(c.ToStruct()->EmoteController.EmoteId)!.Value.Name.ExtractText()),
        new("/TargetsTarget/", "目标的目标", c => c.TargetObject?.Name.TextValue ?? ""),
        new("/ShieldValue/", "盾值 (百分比)", c => c.ShieldPercentage.ToString()),
        new("/CurrentHP/", "当前生命值", c => c.CurrentHp.ToString()),
        new("/MaxHP/", "最大生命值", c => c.MaxHp.ToString()),
        new("/CurrentMP/", "当前魔力", c => c.CurrentMp.ToString()),
        new("/MaxMP/", "最大魔力", c => c.MaxMp.ToString()),
        new("/MaxCP/", "最大制作力", c => c.MaxCp.ToString()),
        new("/CurrentCP/", "当前制作力", c => c.CurrentCp.ToString()),
        new("/MaxCP/", "最大制作力", c => c.MaxCp.ToString()),
        new("/CurrentGP/", "当前采集力", c => c.CurrentGp.ToString()),
        new("/MaxGP/", "最大采集力", c => c.MaxGp.ToString())
    ];

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_TargetInfo", UpdateTargetInfo);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_TargetInfoMainTarget",
                                                UpdateTargetInfoMainTarget);

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_FocusTargetInfo", UpdateFocusTargetInfo);
    }

    protected override void ConfigUI()
    {
        ImGui.BeginGroup();
        var tableSize = new Vector2(ImGui.GetContentRegionAvail().X / 2, 0);
        DrawInputAndPreviewText(Lang.Get("Target"), ref ModuleConfig.TargetPattern);
        DrawInputAndPreviewText(Lang.Get("PlayerTargetInfoExpand-TargetsTarget"),
                                ref ModuleConfig.TargetsTargetPattern);

        DrawInputAndPreviewText(Lang.Get("PlayerTargetInfoExpand-FocusTarget"),
                                ref ModuleConfig.FocusTargetPattern);
        ImGui.EndGroup();

        ImGui.SameLine();
        if (ImGui.BeginTable("PayloadDisplay", 2, ImGuiTableFlags.Borders, tableSize / 1.5f))
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableNextColumn();
            ImGui.Text(Lang.Get("PlayerTargetInfoExpand-AvailablePayload"));
            ImGui.TableNextColumn();
            ImGui.Text(Lang.Get("Description"));

            foreach (var payload in Payloads)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(payload.Placeholder);
                ImGui.TableNextColumn();
                ImGui.Text(payload.Description);
            }

            ImGui.EndTable();
        }

        return;

        void DrawInputAndPreviewText(string categoryTitle, ref string config)
        {
            if (ImGui.BeginTable(categoryTitle, 2, ImGuiTableFlags.BordersOuter, tableSize))
            {
                ImGui.TableSetupColumn("###Category", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("真得要六个字").X);
                ImGui.TableSetupColumn("###Content", ImGuiTableColumnFlags.None, 50);

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{categoryTitle}:");

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputText($"###{categoryTitle}", ref config, 64))
                    SaveConfig(ModuleConfig);

                if (DService.ObjectTable.LocalPlayer != null && DService.ObjectTable.LocalPlayer is ICharacter chara)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text($"{Lang.Get("Example")}:");

                    ImGui.TableNextColumn();
                    ImGui.Text(ReplacePatterns(config, Payloads, chara));
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
        }
    }

    private static void UpdateTargetInfo(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible) return;

        // 目标
        var target = DService.Targets.Target;
        var node0 = addon->GetTextNodeById(16);
        if (node0 != null && target is ICharacter { ObjectKind: ObjectKind.Player } chara0)
            node0->SetText(ReplacePatterns(ModuleConfig.TargetPattern, Payloads, chara0));

        // 目标的目标
        var targetsTarget = DService.Targets.Target?.TargetObject;
        var node1 = addon->GetTextNodeById(7);
        if (node1 != null && targetsTarget is ICharacter { ObjectKind: ObjectKind.Player } chara1)
            node1->SetText(ReplacePatterns(ModuleConfig.TargetsTargetPattern, Payloads, chara1));
    }

    private static void UpdateTargetInfoMainTarget(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible) return;

        // 目标
        var target = DService.Targets.Target;
        var node0 = addon->GetTextNodeById(10);
        if (node0 != null && target is ICharacter { ObjectKind: ObjectKind.Player } chara0)
            node0->SetText(ReplacePatterns(ModuleConfig.TargetPattern, Payloads, chara0));

        // 目标的目标
        var targetsTarget = DService.Targets.Target?.TargetObject;
        var node1 = addon->GetTextNodeById(7);
        if (node1 != null && targetsTarget is ICharacter { ObjectKind: ObjectKind.Player } chara1)
            node1->SetText(ReplacePatterns(ModuleConfig.TargetsTargetPattern, Payloads, chara1));
    }

    private static void UpdateFocusTargetInfo(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible) return;

        // 焦点目标
        var target = DService.Targets.FocusTarget;
        var node0 = addon->GetTextNodeById(10);
        if (node0 != null && target is ICharacter { ObjectKind: ObjectKind.Player } chara0)
            node0->SetText(ReplacePatterns(ModuleConfig.FocusTargetPattern, Payloads, chara0));
    }

    private static string ReplacePatterns(string input, IEnumerable<Payload> payloads, ICharacter chara)
    {
        foreach (var payload in payloads)
            input = input.Replace(payload.Placeholder, payload.ValueFunc(chara));

        return input;
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(UpdateTargetInfo);
        DService.AddonLifecycle.UnregisterListener(UpdateTargetInfoMainTarget);
        DService.AddonLifecycle.UnregisterListener(UpdateFocusTargetInfo);
    }

    private class Payload(string placeholder, string description, Func<ICharacter, string> valueFunc)
    {
        public string                  Placeholder { get; } = placeholder;
        public string                  Description { get; } = description;
        public Func<ICharacter, string> ValueFunc   { get; } = valueFunc;
    }

    private class Config : ModuleConfiguration
    {
        public string FocusTargetPattern = "/Level/级 /Name/";
        public string TargetPattern = "/Name/ [/Job/] «/FCTag/»";
        public string TargetsTargetPattern = "/Name/";
    }
}
