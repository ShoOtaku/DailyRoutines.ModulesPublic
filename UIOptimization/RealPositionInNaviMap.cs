using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class RealPositionInNaviMap : ModuleBase
{
    private static Config ModuleConfig = null!;

    private static TextButtonNode? PositionButton;

    private static int LastX;
    private static int LastY;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("RealPositionInNaviMapTitle"),
        Description = Lang.Get("RealPositionInNaviMapDescription"),
        Category    = ModuleCategory.UIOptimization
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_NaviMap", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize,         "_NaviMap", OnAddon);
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), Lang.Get("RealPositionInNaviMap-CopyFormat"));
        ImGuiOm.HelpMarker(Lang.Get("RealPositionInNaviMap-CopyFormatHelp"), 20f * GlobalUIScale);

        ImGui.InputText("###CopyFormat", ref ModuleConfig.CopyFormat, 256);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);
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
                                NotifyHelper.NotificationSuccess($"{Lang.Get("CopiedToClipboard")}: {result}");
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

    private class Config : ModuleConfig
    {
        public string CopyFormat = @"X:{0:F1} Y:{1:F1} Z:{2:F1}";
    }
}
