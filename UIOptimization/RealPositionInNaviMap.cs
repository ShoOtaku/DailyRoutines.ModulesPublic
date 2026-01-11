using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public class RealPositionInNaviMap : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("RealPositionInNaviMapTitle"),
        Description = GetLoc("RealPositionInNaviMapDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    private static Config ModuleConfig = null!;
    
    private static TextButtonNode? PositionButton;
    
    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostDraw,    "_NaviMap", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_NaviMap", OnAddon);
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("RealPositionInNaviMap-CopyFormat"));
        ImGuiOm.HelpMarker(GetLoc("RealPositionInNaviMap-CopyFormatHelp"), 20f * GlobalFontScale);

        ImGui.InputText("###CopyFormat", ref ModuleConfig.CopyFormat, 256);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);
    }

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        OnAddon(AddonEvent.PreFinalize, null);
    }

    private static unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PreFinalize:
                PositionButton?.Dispose();
                PositionButton = null;
                break;
            case AddonEvent.PostDraw:
                if (NaviMap == null) return;

                if (PositionButton == null)
                {
                    var origTextNode = NaviMap->GetTextNodeById(6);
                    if (origTextNode == null) return;
                    
                    PositionButton = new()
                    {
                        Position  = new(0),
                        Size      = new(130, 18),
                        IsVisible = true,
                        SeString  = string.Empty,
                        OnClick = () =>
                        {
                            if (DService.Instance().ObjectTable.LocalPlayer is not { } player) return;

                            var agent = AgentMap.Instance();
                            agent->SetFlagMapMarker(GameState.TerritoryType, GameState.Map, player.Position);

                            var result = string.Format(ModuleConfig.CopyFormat,
                                                       player.Position.X,
                                                       player.Position.Y,
                                                       player.Position.Z);
                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                ImGui.SetClipboardText(result);
                                NotificationSuccess($"{GetLoc("CopiedToClipboard")}: {result}");
                            }
                        }
                    };

                    if (DService.Instance().ObjectTable.LocalPlayer is { } localPlayer)
                        PositionButton.String = $"X:{localPlayer.Position.X:F1} Y:{localPlayer.Position.Y:F1} Z:{localPlayer.Position.Z:F1}";

                    PositionButton.BackgroundNode.IsVisible = false;

                    PositionButton.LabelNode.TextFlags        = TextFlags.Glare;
                    PositionButton.LabelNode.TextColor        = origTextNode->Color.ToVector4();
                    PositionButton.LabelNode.TextOutlineColor = origTextNode->EdgeColor.ToVector4();

                    PositionButton.AttachNode(NaviMap->GetNodeById(5));

                    origTextNode->ToggleVisibility(false);
                }

            {
                if (LocalPlayerState.Instance().IsMoving && DService.Instance().ObjectTable.LocalPlayer is { } localPlayer)
                    PositionButton.String = $"X:{localPlayer.Position.X:F1} Y:{localPlayer.Position.Y:F1} Z:{localPlayer.Position.Z:F1}";
            }

                break;
        }
    }

    private class Config : ModuleConfiguration
    {
        public string CopyFormat = @"X:{0:F1} Y:{1:F1} Z:{2:F1}";
    }
}
