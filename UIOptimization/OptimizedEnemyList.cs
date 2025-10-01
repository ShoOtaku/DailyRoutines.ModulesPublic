﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe class OptimizedEnemyList : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("OptimizedEnemyListTitle"),
        Description = GetLoc("OptimizedEnemyListDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    private static readonly CompSig                                AgentHudUpdateEnemyListSig = new("40 55 57 41 56 48 81 EC ?? ?? ?? ?? 4C 8B F1");
    private delegate        void                                   AgentHudUpdateEnemyListDelegate(AgentHUD* agent);
    private static          Hook<AgentHudUpdateEnemyListDelegate>? AgentHudUpdateEnemyListHook;
    
    private static Config ModuleConfig = null!;

    private static Dictionary<uint, int> HaterInfo = [];
    
    private static readonly List<(uint ComponentNodeID, TextNode TextNode, NineGridNode BackgroundNode, EnemyCastProgressBarNode CastBarNode)> TextNodes = [];

    private static string CastInfoTargetBlacklistInput = string.Empty;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        AgentHudUpdateEnemyListHook ??= AgentHudUpdateEnemyListSig.GetHook<AgentHudUpdateEnemyListDelegate>(AgentHudUpdateEnemyListDetour);
        AgentHudUpdateEnemyListHook.Enable();
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,          "_EnemyList", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "_EnemyList", OnAddon);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,           "_EnemyList", OnAddon);
    }

    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("Offset"));
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.InputFloat2("###TextOffsetInput", ref ModuleConfig.TextOffset, format: "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.Save(this);
            UpdateTextNodes();
        }
        
        ImGui.Spacing();

        if (ImGui.Checkbox(GetLoc("OptimizedEnemyList-UseCustomColor"), ref ModuleConfig.UseCustomizeTextColor))
        {
            ModuleConfig.Save(this);
            UpdateTextNodes();
        }

        if (ModuleConfig.UseCustomizeTextColor)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Color")}:");
            
            ImGui.SameLine();
            ModuleConfig.TextColor =
                ImGuiComponents.ColorPickerWithPalette(0, "###TextColorInput", ModuleConfig.TextColor);
            
            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("EdgeColor")}:");
            
            ImGui.SameLine();
            ModuleConfig.EdgeColor =
                ImGuiComponents.ColorPickerWithPalette(1, "###EdgeColorInput", ModuleConfig.EdgeColor);
            
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            
            ImGui.SameLine();
            ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("BackgroundColor")}:");
            
            ImGui.SameLine();
            ModuleConfig.BackgroundNodeColor =
                ImGuiComponents.ColorPickerWithPalette(2, "###BackgroundColorInput", ModuleConfig.BackgroundNodeColor);
            
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            
            ImGui.SameLine();
            if (ImGui.Button($"{GetLoc("Save")}"))
            {
                ModuleConfig.Save(this);
                UpdateTextNodes();
            }
            
            ImGui.SameLine();
            if (ImGui.Button($"{GetLoc("Reset")}"))
            {
                ModuleConfig.TextColor           = Vector4.One;
                ModuleConfig.EdgeColor           = new(0, 0.372549f, 1, 1);
                ModuleConfig.BackgroundNodeColor = Vector4.Zero.WithW(1);
                
                ModuleConfig.Save(this);
                UpdateTextNodes();
            }
        }
        
        ImGui.Spacing();

        if (ImGui.Checkbox(GetLoc("OptimizedEnemyList-UseCustomGeneralInfo"), ref ModuleConfig.UseCustomizeText))
        {
            ModuleConfig.Save(this);
            UpdateTextNodes();
        }

        if (ModuleConfig.UseCustomizeText)
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetTextLineHeightWithSpacing());
            ImGui.InputText("###CustomizeTextPatternInput", ref ModuleConfig.CustomizeTextPattern);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.Save(this);
                UpdateTextNodes();
            }
        }
        
        ImGui.Spacing();
        
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("FontSize"));
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f * GlobalFontScale);
        ImGui.InputByte("###FontSize", ref ModuleConfig.FontSize);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ModuleConfig.Save(this);
            UpdateTextNodes();
        }
        
        ImGui.Spacing();
        
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("OptimizedEnemyList-CastInfoDisplayTargetBlacklist"));
        ImGuiOm.HelpMarker(GetLoc("OptimizedEnemyList-CastInfoDisplayTargetBlacklistHelp"));
        
        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Plus, GetLoc("Add")))
            ImGui.OpenPopup("CastInfoTargetBlacklistPopup");

        using (var popup = ImRaii.Popup("CastInfoTargetBlacklistPopup"))
        {
            if (popup)
            {
                ImGui.InputText("###CastInfoTargetBlacklistInput", ref CastInfoTargetBlacklistInput);
                
                ImGui.SameLine();
                using (ImRaii.Disabled(string.IsNullOrWhiteSpace(CastInfoTargetBlacklistInput) || 
                                       ModuleConfig.CastInfoTargetBlacklist.Contains(CastInfoTargetBlacklistInput)))
                {
                    if (ImGui.Button(GetLoc("Confirm")))
                    {
                        ModuleConfig.CastInfoTargetBlacklist.Add(CastInfoTargetBlacklistInput);
                        ModuleConfig.Save(this);
                        
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }

        var counter = 1;
        foreach (var blacklist in ModuleConfig.CastInfoTargetBlacklist)
        {
            using (ImRaii.PushId($"CastInfoTargetBlacklist-{blacklist}"))
            {
                if (ImGui.Button(GetLoc("Delete")))
                {
                    ModuleConfig.CastInfoTargetBlacklist.Remove(blacklist);
                    ModuleConfig.Save(this);
                    
                    break;
                }
                
                ImGui.SameLine();
                ImGui.Text($"{counter}. {blacklist}");

                counter++;
            }
        }
    }

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PostSetup:
                CreateTextNodes();
                break;
            case AddonEvent.PreRequestedUpdate:
                UpdateTextNodes();
                break;
            case AddonEvent.PostDraw:
                if (!Throttler.Throttle("OptimizedEnemyList-AddonPostDraw", 50)) return;
                UpdateTextNodes();
                break;
        }
    }
    
    private static void AgentHudUpdateEnemyListDetour(AgentHUD* agent)
    {
        AgentHudUpdateEnemyListHook.Original(agent);
        UpdateHaterInfo();
    }

    private static void UpdateTextNodes()
    {
        var nodes = TextNodes;
        if (nodes is not { Count: > 0 })
        {
            CreateTextNodes();
            return;
        }
        
        var numberArray = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.EnemyList);
        if (numberArray == null) return;
        
        var castWidth  = stackalloc ushort[1];
        var castHeight = stackalloc ushort[1];

        var isTargetCasting = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.Hud2)->IntArray[69] != -1;
        for (var i = 0; i < nodes.Count; i++)
        {
            var offset = 8 + (i * 6);
            
            var gameObjectID = (ulong)numberArray->IntArray[offset];
            if (gameObjectID is 0 or 0xE0000000) continue;

            var textNode       = nodes[i].TextNode;
            var backgroundNode = nodes[i].BackgroundNode;
            var castBarNode    = nodes[i].CastBarNode;
            
            var gameObj = DService.ObjectTable.SearchById(gameObjectID);
            if (gameObj is not IBattleChara bc || !HaterInfo.TryGetValue(gameObj.EntityId, out var enmity))
            {
                textNode.SeString        = string.Empty;
                backgroundNode.IsVisible = false;
                continue;
            }

            var componentNode = EnemyList->GetComponentNodeById(nodes[i].ComponentNodeID);
            if (componentNode == null)
            {
                CreateTextNodes();
                return;
            }
            
            var castTextNode = componentNode->Component->UldManager.SearchNodeById(4)->GetAsAtkTextNode();
            if (castTextNode == null) continue;

            var targetNameTextNode = componentNode->Component->UldManager.SearchNodeById(6)->GetAsAtkTextNode();
            if (targetNameTextNode == null) continue;
            
            var origCastBarNode         = componentNode->Component->UldManager.SearchNodeById(7);
            var origCastBarProgressNode = componentNode->Component->UldManager.SearchNodeById(8);
            if (origCastBarNode == null || origCastBarProgressNode == null) continue;
            
            targetNameTextNode->GetTextDrawSize(castWidth, castHeight);

            var isCasting = bc.IsCasting || (isTargetCasting && bc.Address == (DService.Targets.Target?.Address ?? nint.Zero));
            if (isCasting)
            {
                origCastBarNode->SetAlpha(0);
                origCastBarProgressNode->SetAlpha(0);
                castTextNode->SetAlpha(0);

                var castBackgroundNode = componentNode->Component->UldManager.SearchNodeById(5);
                if (castBackgroundNode != null) 
                    castBackgroundNode->SetAlpha(0);
                
                castBarNode.IsVisible = true;
                castBarNode.ProgressNode.Width  = 105 * (bc.CurrentCastTime / bc.TotalCastTime);

                if (bc.IsCastInterruptible)
                    castBarNode.AddColor = KnownColor.Red.ToVector4().ToVector3();
                else
                    castBarNode.AddColor = KnownColor.Yellow.ToVector4().ToVector3() / 255f;
            }
            else
            {
                castBarNode.IsVisible = false;
                castBarNode.Progress  = 0f;
            }

            var targetName = SanitizeSeIcon(targetNameTextNode->NodeText.ExtractText());
            
            textNode.TextColor = ModuleConfig.UseCustomizeTextColor
                                      ? ModuleConfig.TextColor
                                      : ConvertByteColorToVector4(castTextNode->TextColor);
            textNode.TextOutlineColor = ModuleConfig.UseCustomizeTextColor
                                     ? ModuleConfig.EdgeColor
                                     : new(0, 0.372549f, 1, 1);
            backgroundNode.Color = ModuleConfig.UseCustomizeTextColor
                                       ? ModuleConfig.BackgroundNodeColor
                                       : ConvertByteColorToVector4(castTextNode->BackgroundColor);
            
            textNode.FontSize = ModuleConfig.FontSize;
            
            if (isCasting && !ModuleConfig.CastInfoTargetBlacklist.Contains(targetName))
            {
                var castTimeLeft = MathF.Max(bc.TotalCastTime - bc.CurrentCastTime, 0f);

                textNode.SeString        = $"{GetCastInfoText(bc.CastActionType, bc.CastActionId)}: " + (castTimeLeft != 0 ? $"{castTimeLeft:F1}" : "\ue07f\ue07b");
                backgroundNode.IsVisible = true;
            }            
            else if (!bc.IsTargetable && bc.CurrentHp == bc.MaxHp)
            {
                textNode.SeString        = string.Empty;
                backgroundNode.IsVisible = false;
            }
            else
            {
                textNode.SeString        = GetGeneralInfoText((float)bc.CurrentHp / bc.MaxHp * 100, enmity);
                backgroundNode.IsVisible = true;
            }
            
            textNode.Position = new(Math.Max(95, *castWidth + 28) + ModuleConfig.TextOffset.X, 4 + ModuleConfig.TextOffset.Y);

            var textSize = textNode.GetTextDrawSize(textNode.SeString);
            
            backgroundNode.Position = new(Math.Max(68, *castWidth + 1) + (isCasting ? 12.5f : 0) + ModuleConfig.TextOffset.X, 6 + ModuleConfig.TextOffset.Y);
            backgroundNode.Size     = new(textSize.X                   + 47                      + (isCasting ? -18 : 0), textSize.Y * 0.7f);
        }
    }

    private static void CreateTextNodes()
    {
        if (EnemyList == null) return;
        if (!TryFindButtonNodes(out var buttonNodesPtr)) return;

        ClearTextNodes();
        
        foreach (var nodePtr in buttonNodesPtr)
        {
            var node = (AtkComponentNode*)nodePtr;
            
            var castTextNode = node->Component->UldManager.SearchNodeById(4)->GetAsAtkTextNode();
            if (castTextNode == null) continue;

            var textNode = new TextNode
            {
                SeString      = string.Empty,
                FontSize      = ModuleConfig.FontSize,
                IsVisible     = true,
                Size          = new(160f, 25f),
                AlignmentType = AlignmentType.TopLeft,
                Position      = new(100, 5),
                TextFlags     = TextFlags.Edge    | TextFlags.Emboss,
                NodeFlags     = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.AnchorTop | NodeFlags.AnchorLeft,
                LineSpacing   = 20,
            };

            // 需要把 STP 显示 UVWH 都除以 2
            var backgroundNode = new SimpleNineGridNode
            {
                TexturePath        = "ui/uld/TextInputA.tex",
                TextureCoordinates = new(24, 0),
                TextureSize        = new(24, 24),
                Size               = new(160, 10),
                IsVisible          = true,
                Color              = KnownColor.Black.ToVector4(),
                Position           = new(75, 6),
                Alpha              = 0.6f,

            };

            var castBarNode = new EnemyCastProgressBarNode
            {
                IsVisible = true,
                Position  = new(85, 13.7f),
                Size      = new(120, 20)
            };

            castBarNode.ProgressNode.Height   -= 12f;
            castBarNode.ProgressNode.Position += new Vector2(7.7f, 6.5f);
            castBarNode.ProgressNode.AddColor =  new(1);
            
            Service.AddonController.AttachNode(backgroundNode, node);
            Service.AddonController.AttachNode(textNode,       node);
            Service.AddonController.AttachNode(castBarNode,    node);
            
            TextNodes.Add(new(node->NodeId, textNode, backgroundNode, castBarNode));
        }
    }

    private static void ClearTextNodes()
    {
        foreach (var (_, textNode, backgroundNode, castBarNode) in TextNodes)
        {
            Service.AddonController.DetachNode(textNode);
            Service.AddonController.DetachNode(backgroundNode);
            Service.AddonController.DetachNode(castBarNode);
        }
        
        TextNodes.Clear();
    }

    private static void UpdateHaterInfo()
    {
        var hater = UIState.Instance()->Hater;
        HaterInfo = hater.Haters
                         .ToArray()
                         .Take(hater.HaterCount)
                         .Where(x => x.EntityId != 0 && x.EntityId != 0xE0000000)
                         .DistinctBy(x => x.EntityId)
                         .ToDictionary(x => x.EntityId, x => x.Enmity);
    }
    
    private static string GetGeneralInfoText(float percentage, int enmity) =>
        ModuleConfig.UseCustomizeText
            ? string.Format(ModuleConfig.CustomizeTextPattern, percentage.ToString("F1"), enmity)
            : $"{LuminaWrapper.GetAddonText(232)}: {percentage:F1}% / {LuminaWrapper.GetAddonText(721)}: {enmity}%";

    private static string GetCastInfoText(ActionType type, uint actionID)
    {
        var result = string.Empty;
        
        switch (type)
        {
            case ActionType.Action:
                result = LuminaWrapper.GetActionName(actionID);
                break;
        }

        if (string.IsNullOrWhiteSpace(result))
            result = LuminaWrapper.GetAddonText(1032);
        
        return result;
    }
    
    private static bool TryFindButtonNodes(out List<nint> nodes)
    {
        nodes = [];
        if (EnemyList == null) return false;
        
        for (var i = 4; i < EnemyList->UldManager.NodeListCount; i++)
        {
            var node = EnemyList->UldManager.NodeList[i];
            if (node == null || (ushort)node->Type != 1001) continue;
            
            var buttonNode = node->GetAsAtkComponentButton();
            if (buttonNode == null) continue;

            nodes.Add((nint)node);
        }

        nodes.Reverse();
        return nodes.Count > 0;
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddon);

        ClearTextNodes();
        
        HaterInfo.Clear();
    }

    private class Config : ModuleConfiguration
    {
        public Vector2 TextOffset = Vector2.Zero;
        
        public bool   UseCustomizeText;
        public string CustomizeTextPattern = @"HP: {0}% / Enmity: {1}%";

        public bool    UseCustomizeTextColor;
        public Vector4 TextColor           = Vector4.One;
        public Vector4 EdgeColor           = new(0, 0.372549f, 1, 1);
        public Vector4 BackgroundNodeColor = Vector4.Zero.WithW(1);

        public byte FontSize = 10;

        public HashSet<string> CastInfoTargetBlacklist = [];
    }
}
