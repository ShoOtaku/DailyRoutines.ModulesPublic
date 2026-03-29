using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Hooking;
using Dalamud.Interface.Components;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using OmenTools.ImGuiOm.Widgets.Combos;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using Action = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoBroadcastActionHitInfo : ModuleBase
{
    private static readonly CompSig                                 ProcessPacketActionEffectSig = new("E8 ?? ?? ?? ?? 48 8B 8D F0 03 00 00");
    private static          Hook<ProcessPacketActionEffectDelegate> ProcessPacketActionEffectHook;

    private static Config ModuleConfig = null!;

    private static readonly ActionSelectCombo WhitelistCombo = new("Whitelist");
    private static readonly ActionSelectCombo BlacklistCombo = new("Blacklist");
    private static readonly ActionSelectCombo SelectedCombo  = new("Selected");

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoBroadcastActionHitInfoTitle"),
        Description = Lang.Get("AutoBroadcastActionHitInfoDescription"),
        Category    = ModuleCategory.Action,
        Author      = ["Xww"]
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        WhitelistCombo.SelectedIDs = ModuleConfig.WhitelistActions;
        BlacklistCombo.SelectedIDs = ModuleConfig.BlacklistActions;

        ProcessPacketActionEffectHook ??= ProcessPacketActionEffectSig.GetHook<ProcessPacketActionEffectDelegate>(ProcessPacketActionEffectDetour);
        ProcessPacketActionEffectHook.Enable();
    }

    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoBroadcastActionHitInfo-DHHint")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * GlobalUIScale);
        ImGui.InputText("###DirectHitMessage", ref ModuleConfig.DirectHitPattern);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoBroadcastActionHitInfo-CHHint")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * GlobalUIScale);
        ImGui.InputText("###CriticalHitMessage", ref ModuleConfig.CriticalHitPattern);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoBroadcastActionHitInfo-DCHHint")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(300f * GlobalUIScale);
        ImGui.InputText("###DirectCriticalHitMessage", ref ModuleConfig.DirectCriticalHitPattern);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);

        ImGuiOm.ScaledDummy(5f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoBroadcastActionHitInfo-UseTTS")}");

        ImGui.SameLine();
        if (ImGui.Checkbox("###UseTTS", ref ModuleConfig.UseTTS))
            ModuleConfig.Save(this);

        ImGuiOm.ScaledDummy(5f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("WorkMode")}:");

        ImGui.SameLine();
        if (ImGuiComponents.ToggleButton("WorkModeButton", ref ModuleConfig.WorkMode))
            ModuleConfig.Save(this);

        ImGui.SameLine();
        ImGui.TextUnformatted(ModuleConfig.WorkMode ? Lang.Get("Whitelist") : Lang.Get("Blacklist"));

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("Action")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f * GlobalUIScale);

        if (ModuleConfig.WorkMode
                ? WhitelistCombo.DrawCheckbox()
                : BlacklistCombo.DrawCheckbox())
        {
            ModuleConfig.BlacklistActions = BlacklistCombo.SelectedIDs;
            ModuleConfig.WhitelistActions = BlacklistCombo.SelectedIDs;

            ModuleConfig.Save(this);
        }

        ImGuiOm.ScaledDummy(5f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("AutoBroadcastActionHitInfo-CustomActionAlias")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(250f * GlobalUIScale);
        using (ImRaii.PushId("AddCustomActionSelect"))
            SelectedCombo.DrawRadio();

        ImGui.SameLine();

        using (ImRaii.Disabled
               (
                   SelectedCombo.SelectedID == 0 ||
                   ModuleConfig.CustomActionName.ContainsKey(SelectedCombo.SelectedID)
               ))
        {
            if (ImGuiOm.ButtonIcon("##新增", FontAwesomeIcon.Plus))
            {
                if (SelectedCombo.SelectedID != 0)
                {
                    ModuleConfig.CustomActionName.TryAdd(SelectedCombo.SelectedID, string.Empty);
                    ModuleConfig.Save(this);
                }
            }
        }

        ImGui.Spacing();

        if (ModuleConfig.CustomActionName.Count < 1) return;

        if (ImGui.CollapsingHeader
            (
                $"{Lang.Get("AutoBroadcastActionHitInfo-CustomActionAliasCount", ModuleConfig.CustomActionName.Count)}###CustomActionsCombo"
            ))
        {
            var counter = 1;

            foreach (var actionNamePair in ModuleConfig.CustomActionName)
            {
                using var id = ImRaii.PushId($"ActionCustomName_{actionNamePair.Key}");

                if (!LuminaGetter.TryGetRow<Action>(actionNamePair.Key, out var data)) continue;
                var actionIcon = DService.Instance().Texture.GetFromGameIcon(new(data.Icon)).GetWrapOrDefault();
                if (actionIcon == null) continue;

                using var group = ImRaii.Group();

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted($"{counter}.");

                ImGui.SameLine();
                ImGui.Image(actionIcon.Handle, new(ImGui.GetTextLineHeightWithSpacing()));

                ImGui.SameLine();
                ImGui.TextUnformatted(data.Name.ToString());

                ImGui.SameLine();

                if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.TrashAlt, Lang.Get("Delete")))
                {
                    ModuleConfig.CustomActionName.Remove(actionNamePair.Key);
                    ModuleConfig.Save(this);
                    continue;
                }

                using (ImRaii.PushIndent())
                {
                    var message = actionNamePair.Value;

                    ImGui.SetNextItemWidth(250f * GlobalUIScale);
                    if (ImGui.InputText("###ActionCustomNameInput", ref message, 64))
                        ModuleConfig.CustomActionName[actionNamePair.Key] = message;
                    if (ImGui.IsItemDeactivatedAfterEdit())
                        ModuleConfig.Save(this);
                }

                counter++;
            }
        }
    }

    private static void ProcessPacketActionEffectDetour
    (
        uint                        sourceID,
        nint                        sourceCharacter,
        nint                        pos,
        ActionEffectHandler.Header* effectHeader,
        ActionEffectHandler.Effect* effectArray,
        ulong*                      effectTrail
    )
    {
        ProcessPacketActionEffectHook.Original(sourceID, sourceCharacter, pos, effectHeader, effectArray, effectTrail);
        Parse(sourceID, effectHeader, effectArray);
    }

    public static void Parse(uint sourceEntityID, ActionEffectHandler.Header* effectHeader, ActionEffectHandler.Effect* effectArray)
    {
        try
        {
            var targets = effectHeader->NumTargets;
            if (targets < 1) return;

            if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;
            if (localPlayer.EntityID != sourceEntityID) return;

            var actionID   = effectHeader->ActionId;
            var actionData = LuminaGetter.GetRow<Action>(actionID);
            if (actionData == null || actionData.Value.ActionCategory.RowId == 1) return; // 自动攻击

            switch (ModuleConfig.WorkMode)
            {
                case false:
                    if (ModuleConfig.BlacklistActions.Contains(actionID)) return;
                    break;
                case true:
                    if (!ModuleConfig.WhitelistActions.Contains(actionID)) return;
                    break;
            }

            var actionName = ModuleConfig.CustomActionName.TryGetValue(actionID, out var customName) &&
                             !string.IsNullOrWhiteSpace(customName)
                                 ? customName
                                 : actionData.Value.Name.ToString();

            var message = effectArray->Param0 switch
            {
                64 => string.Format(ModuleConfig.DirectHitPattern,         actionName),
                32 => string.Format(ModuleConfig.CriticalHitPattern,       actionName),
                96 => string.Format(ModuleConfig.DirectCriticalHitPattern, actionName),
                _  => string.Empty
            };

            if (string.IsNullOrWhiteSpace(message)) return;

            switch (effectArray->Param0)
            {
                case 32 or 64:
                    NotifyHelper.ContentHintBlue(message, TimeSpan.FromSeconds(1));
                    if (ModuleConfig.UseTTS)
                        NotifyHelper.Speak(message);
                    break;
                case 96:
                    NotifyHelper.ContentHintRed(message, TimeSpan.FromSeconds(1));
                    if (ModuleConfig.UseTTS)
                        NotifyHelper.Speak(message);
                    break;
            }
        }
        catch
        {
            // ignored
        }

    }

    private delegate void ProcessPacketActionEffectDelegate
    (
        uint                        sourceID,
        nint                        sourceCharacter,
        nint                        pos,
        ActionEffectHandler.Header* effectHeader,
        ActionEffectHandler.Effect* effectArray,
        ulong*                      effectTrail
    );

    public class Config : ModuleConfig
    {
        public HashSet<uint> BlacklistActions   = [];
        public string        CriticalHitPattern = "技能 {0} 触发了暴击";

        public Dictionary<uint, string> CustomActionName         = [];
        public string                   DirectCriticalHitPattern = "技能 {0} 触发了直暴";

        public string DirectHitPattern = "技能 {0} 触发了直击";

        public bool UseTTS;

        public HashSet<uint> WhitelistActions = [];

        // False - 黑名单, True - 白名单
        public bool WorkMode;
    }
}
