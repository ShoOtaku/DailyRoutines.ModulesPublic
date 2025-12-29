using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Config;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using OmenTools.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DailyRoutines.Modules;

public unsafe class AutoBattleEffectsChanger : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("AutoBattleEffectsChangerTitle"),
        Description = GetLoc("AutoBattleEffectsChangerDescription"),
        Category = ModuleCategories.System,
        Author = ["Siren"]
    };

    private static EffectSettings? lastAppliedSettings = null;
    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        FrameworkManager.Reg(OnUpdate, throttleMS:2_000);
    }

    protected override void Uninit()
    {
        FrameworkManager.Unreg(OnUpdate);
    }

    protected override void ConfigUI()
    {
        ImGui.TextWrapped(GetLoc("AutoBattleEffectsChanger-ConfigIntro"));
        ImGui.Separator();

        using var tabbar = ImRaii.TabBar("AutoBattleEffectsChangerTabBar", ImGuiTabBarFlags.Reorderable);
        if (!tabbar) return;

        using (var overWorldTab = ImRaii.TabItem(GetLoc("AutoBattleEffectsChanger-TabOverworld")))
        {
            if (overWorldTab) 
                DrawOverworldSettings();
        }

        using (var dutyTab = ImRaii.TabItem(GetLoc("AutoBattleEffectsChanger-TabDuty")))
        {
            if (dutyTab) 
                DrawDutySettings();
        }
    }

    private void DrawOverworldSettings()
    {
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-OverworldHeader"));

        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-Limit1Label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * GlobalFontScale);
        ImGui.InputInt("##Limit1", ref ModuleConfig.AroundCountLimit1);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.AlignTextToFramePadding();
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-Limit2Label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * GlobalFontScale);
        ImGui.InputInt("##Limit2", ref ModuleConfig.AroundCountLimit2);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.Separator();

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-ConditionLow", ModuleConfig.AroundCountLimit1), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("Tier1", ModuleConfig.OverworldLow);

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-ConditionMid", ModuleConfig.AroundCountLimit1, ModuleConfig.AroundCountLimit2), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("Tier2", ModuleConfig.OverworldMid);

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-ConditionHigh", ModuleConfig.AroundCountLimit2), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("Tier3", ModuleConfig.OverworldHigh);
    }

    private void DrawDutySettings()
    {
        ImGui.Text(GetLoc("AutoBattleEffectsChanger-DutyHeader"));
        ImGui.Spacing();

        if (ImGui.CollapsingHeader(GetLoc("AutoBattleEffectsChanger-DutyDefaultHeader"), ImGuiTreeNodeFlags.DefaultOpen))
            DrawEffectSettingRow("DutyDefault", ModuleConfig.DefaultDutySettings);

        ImGui.Separator();

        var contentTypes = LuminaGetter.Get<ContentType>();

        foreach (var contentType in contentTypes)
        {
            var name = contentType.Name.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            var id = contentType.RowId;

            ModuleConfig.DutySpecificSettings.TryAdd(id, new EffectSettings());

            var settings = ModuleConfig.DutySpecificSettings[id];

            var icon = DService.Texture.GetFromGameIcon(new GameIconLookup(contentType.Icon));
            if (icon != null)
            {
                ImGui.Image(icon.GetWrapOrEmpty().Handle, new Vector2(24, 24) * GlobalFontScale);
                ImGui.SameLine();
            }

            using (var node = ImRaii.TreeNode($"{name} (ID: {id})##Type{id}"))
            {
                if (node)
                {
                    ImGui.Checkbox(GetLoc("AutoBattleEffectsChanger-EnableSpecific"), ref settings.Enabled);
                    if (ImGui.IsItemDeactivatedAfterEdit())
                        SaveConfig(ModuleConfig);

                    if (settings.Enabled)
                        DrawEffectSettingRow($"Duty_{id}", settings);
                    else
                        ImGui.TextDisabled(GetLoc("AutoBattleEffectsChanger-UseDefaultNotice"));
                }
            }
        }
    }

    private void DrawEffectSettingRow(string idPrefix, EffectSettings settings)
    {
        using var id = ImRaii.PushId(idPrefix);
        using var table = ImRaii.Table("EffectTable", 4, ImGuiTableFlags.BordersInnerV);
        if (!table) return;

        ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColSelf"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColParty"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColOther"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(GetLoc("AutoBattleEffectsChanger-ColEnemy"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        DrawCombo("##Self", ref settings.Self);

        ImGui.TableNextColumn();
        DrawCombo("##Party", ref settings.Party);

        ImGui.TableNextColumn();
        DrawCombo("##Other", ref settings.Other);

        ImGui.TableNextColumn();
        DrawCombo("##Enemy", ref settings.Enemy);
    }

    private static readonly string[] EffectOptionKeys =
    [
        "AutoBattleEffectsChanger-OptionAll",
        "AutoBattleEffectsChanger-OptionLimited",
        "AutoBattleEffectsChanger-OptionNone"
    ];

    private void DrawCombo(string label, ref uint value)
    {
        var current = Math.Clamp(value, 0, (uint)EffectOptionKeys.Length - 1);
        var previewText = GetLoc(EffectOptionKeys[current]);

        ImGui.SetNextItemWidth(-1);

        using var combo = ImRaii.Combo($"##{label}", previewText);
        if (combo)
        {
            for (var i = 0; i < EffectOptionKeys.Length; i++)
            {
                var optionText = GetLoc(EffectOptionKeys[i]);
                var isSelected = current == i;

                if (ImGui.Selectable(optionText, isSelected))
                {
                    value = (uint)i;
                    SaveConfig(ModuleConfig);
                }
            }
        }
    }

    protected void OnUpdate(IFramework framework)
    {
        EffectSettings? targetSettings = null;
        var instance = GameState.ContentFinderConditionData;
        if (instance.RowId != 0)
        {
            if (LuminaGetter.TryGetRow<ContentFinderCondition>(instance.RowId, out var content))
            {
                var typeId = content.ContentType.Value.RowId;

                if (ModuleConfig.DutySpecificSettings.TryGetValue(typeId, out var specificConfig) && specificConfig.Enabled)
                    targetSettings = specificConfig;
            }

            targetSettings ??= ModuleConfig.DefaultDutySettings;
        }
        else
        {
            var playerCount = PlayersManager.PlayersAroundCount;

            if (playerCount < ModuleConfig.AroundCountLimit1)
                targetSettings = ModuleConfig.OverworldLow;
            else if (playerCount < ModuleConfig.AroundCountLimit2)
                targetSettings = ModuleConfig.OverworldMid;
            else
                targetSettings = ModuleConfig.OverworldHigh;
        }

        ApplySettings(targetSettings);
    }

    private void ApplySettings(EffectSettings? settings)
    {
        if (settings == null) return;

        if (lastAppliedSettings != null && settings.Equals(lastAppliedSettings))
            return;

        try
        {
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectSelf.ToString(), (uint)settings.Self);
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectParty.ToString(), (uint)settings.Party);
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectOther.ToString(), (uint)settings.Other);
            DService.GameConfig.UiConfig.Set(UiConfigOption.BattleEffectPvPEnemyPc.ToString(), (uint)settings.Enemy);

            lastAppliedSettings = settings.Clone();
        }
        catch (Exception ex)
        {
            Error("设置特效水平发生异常：",ex);
        }
    }


    public class EffectSettings : IEquatable<EffectSettings>
    {
        public bool Enabled = false;
        public uint Self = 0;
        public uint Party = 1;
        public uint Other = 2;
        public uint Enemy = 0;

        public EffectSettings Clone()
        {
            return (EffectSettings)this.MemberwiseClone();
        }

        public bool Equals(EffectSettings? other)
        {
            if (other is null) return false;
            return Self == other.Self &&
                   Party == other.Party &&
                   Other == other.Other &&
                   Enemy == other.Enemy;
        }
    }

    public class Config : ModuleConfiguration
    {
        public int AroundCountLimit1 = 20;
        public int AroundCountLimit2 = 40;

        public EffectSettings OverworldLow = new() { Self = 0, Party = 0, Other = 0, Enemy = 0 };
        public EffectSettings OverworldMid = new() { Self = 0, Party = 1, Other = 1, Enemy = 0 };
        public EffectSettings OverworldHigh = new() { Self = 0, Party = 1, Other = 2, Enemy = 0 };

        public EffectSettings DefaultDutySettings = new() { Self = 0, Party = 0, Other = 1, Enemy = 0 };

        public Dictionary<uint, EffectSettings> DutySpecificSettings = new();
    }
}
