using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe class RealPositionInNaviMap : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("RealPositionInNaviMapTitle"),
        Description = GetLoc("RealPositionInNaviMapDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    private static Config ModuleConfig = null!;
    
    private static TextButtonNode? PositionButton;

    private static int LastX;
    private static int LastY;
    
    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_NaviMap", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize,         "_NaviMap", OnAddon);
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

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PreFinalize:
                PositionButton?.Dispose();
                PositionButton = null;

                if (NaviMap != null)
                {
                    var origTextNode = NaviMap->GetTextNodeById(6);
                    if (origTextNode != null)
                        origTextNode->ToggleVisibility(true);
                }

                LastX = LastY = 0;
                
                break;

            case AddonEvent.PostRequestedUpdate:
                var numberArray = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.AreaMap);
                if (numberArray == null) return;

                // 跳跃的时候始终要更新位置
                if (!DService.Instance().Condition[ConditionFlag.Jumping])
                {
                    if (numberArray->IntArray[0] != LastX)
                        LastX = numberArray->IntArray[0];
                    else if (numberArray->IntArray[1] != LastY)
                        LastY = numberArray->IntArray[1];
                    else
                        return;
                }
                
                if (PositionButton == null)
                {
                    var origTextNode = NaviMap->GetTextNodeById(6);
                    if (origTextNode == null) return;

                    PositionButton = new()
                    {
                        Position  = new(0),
                        Size      = new(130, 18),
                        IsVisible = true,
                        String    = string.Empty,
                        OnClick = () =>
                        {
                            if (DService.Instance().ObjectTable.LocalPlayer is not { } player) return;

                            var agent = AgentMap.Instance();
                            agent->SetFlagMapMarker(GameState.TerritoryType, GameState.Map, player.Position);

                            var result = string.Format
                            (
                                ModuleConfig.CopyFormat,
                                player.Position.X,
                                player.Position.Y,
                                player.Position.Z
                            );

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
                if (DService.Instance().ObjectTable.LocalPlayer is { } localPlayer)
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
