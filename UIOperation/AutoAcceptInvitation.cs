using System.Text;
using System.Text.RegularExpressions;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Components;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Lumina;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoAcceptInvitation : ModuleBase
{
    private static Config ModuleConfig = null!;

    private static string PlayerNameInput = string.Empty;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoAcceptInvitationTitle"),
        Description = Lang.Get("AutoAcceptInvitationDescription"),
        Category    = ModuleCategory.UIOperation,
        Author      = ["Fragile"]
    };

    private static string Pattern { get; } = BuildPattern(LuminaGetter.GetRow<Addon>(120).GetValueOrDefault().Text.ToDalamudString().Payloads);

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnSelectYesno);
    }

    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{Lang.Get("Mode")}:");

        ImGui.SameLine();
        if (ImGuiComponents.ToggleButton("ModeSwitch", ref ModuleConfig.Mode))
            ModuleConfig.Save(this);

        ImGui.SameLine();
        ImGui.TextUnformatted(Lang.Get(ModuleConfig.Mode ? "Whitelist" : "Blacklist"));

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{LuminaWrapper.GetAddonText(9818)}:");

        using var indent = ImRaii.PushIndent();

        ImGui.SetNextItemWidth(200f * GlobalUIScale);
        ImGui.InputText("##NewPlayerInput", ref PlayerNameInput, 128);
        ImGuiOm.TooltipHover(Lang.Get("AutoAcceptInvitationTitle-PlayerNameInputHelp"));

        ImGui.SameLine();

        using (ImRaii.Disabled
               (
                   string.IsNullOrWhiteSpace(PlayerNameInput) ||
                   (ModuleConfig.Mode ? ModuleConfig.Whitelist : ModuleConfig.Blacklist).Contains(PlayerNameInput)
               ))
        {
            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Plus, Lang.Get("Add")))
            {
                if (!string.IsNullOrWhiteSpace(PlayerNameInput) &&
                    (ModuleConfig.Mode ? ModuleConfig.Whitelist : ModuleConfig.Blacklist).Add(PlayerNameInput))
                {
                    ModuleConfig.Save(this);
                    PlayerNameInput = string.Empty;
                }
            }
        }

        var playersToRemove = new List<string>();

        foreach (var player in ModuleConfig.Mode ? ModuleConfig.Whitelist : ModuleConfig.Blacklist)
        {
            using var id = ImRaii.PushId($"{player}");

            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.TrashAlt, Lang.Get("Delete")))
                playersToRemove.Add(player);

            ImGui.SameLine();
            ImGui.Bullet();

            ImGui.SameLine(0, 8f * GlobalUIScale);
            ImGui.TextUnformatted($"{player}");
        }

        if (playersToRemove.Count > 0)
        {
            playersToRemove.ForEach(x => (ModuleConfig.Mode ? ModuleConfig.Whitelist : ModuleConfig.Blacklist).Remove(x));
            ModuleConfig.Save(this);
        }
    }

    private static void OnSelectYesno(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonSelectYesno*)SelectYesno;
        if (addon == null || DService.Instance().PartyList.Length > 1) return;

        var text = addon->PromptText->NodeText.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var playerName = ExtractPlayerName(text);
        if (string.IsNullOrWhiteSpace(playerName)) return;
        if (ModuleConfig.Mode  && !ModuleConfig.Whitelist.Contains(playerName) ||
            !ModuleConfig.Mode && ModuleConfig.Blacklist.Contains(playerName))
            return;

        AddonSelectYesnoEvent.ClickYes();
    }

    private static string ExtractPlayerName(string inputText) =>
        Regex.Match(inputText, Pattern) is { Success: true, Groups.Count: > 1 } match ? match.Groups[1].Value : string.Empty;

    private static string BuildPattern(List<Payload> payloads)
    {
        var pattern = new StringBuilder();

        foreach (var payload in payloads)
        {
            if (payload is TextPayload textPayload)
                pattern.Append(Regex.Escape(textPayload.Text));
            else
                pattern.Append("(.*?)");
        }

        return pattern.ToString();
    }

    protected override void Uninit() =>
        DService.Instance().AddonLifecycle.UnregisterListener(OnSelectYesno);

    private class Config : ModuleConfig
    {
        public HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase);

        // true - 白名单, false - 黑名单
        public bool Mode = true;

        public HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase);
    }
}
