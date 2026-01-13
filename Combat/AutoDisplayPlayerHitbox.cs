using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Overlay;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoDisplayPlayerHitbox : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoDisplayPlayerHitboxTitle"),
        Description = GetLoc("AutoDisplayPlayerHitboxDescription"),
        Category    = ModuleCategories.Combat,
        Author      = ["Due"]
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private static bool IsWeaponUnsheathed() => 
        UIState.Instance()->WeaponState.IsUnsheathed;

    private static Config ModuleConfig = null!;

    private static OverlayController? Controller;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Controller ??= new();
        Controller.CreateNode(() => new PlayerDotImageNode());
    }

    protected override void Uninit()
    {
        Controller?.Dispose();
        Controller = null;
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OnlyInCombat"), ref ModuleConfig.OnlyInCombat))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("OnlyInDuty"), ref ModuleConfig.OnlyInDuty))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("OnlyUnsheathed"), ref ModuleConfig.OnlyUnsheathed))
            SaveConfig(ModuleConfig);

        ImGui.NewLine();
        
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        ImGui.ColorPicker4(GetLoc("Color"), ref ModuleConfig.Color);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);
        
        ImGui.NewLine();
        
        using (ImRaii.ItemWidth(300f * GlobalFontScale))
        {
            if (ImGui.InputFloat(GetLoc("Size"), ref ModuleConfig.Size))
                ModuleConfig.Size = MathF.Max(1, ModuleConfig.Size);
            if (ImGui.IsItemDeactivatedAfterEdit())
                ModuleConfig.Save(this);

            ImGui.InputUInt(GetLoc("Icon"), ref ModuleConfig.IconID);
            if (ImGui.IsItemDeactivatedAfterEdit())
                ModuleConfig.Save(this);

            ImGui.SameLine();
            if (ImGui.Button($"{FontAwesomeIcon.Icons.ToIconString()}"))
                ChatManager.Instance().SendCommand("/xldata icon");
            ImGuiOm.TooltipHover($"{GetLoc("IconBrowser")}\n({GetLoc("IconBrowser-Suggestion")})");
            
            if (ImGui.InputFloat3(GetLoc("Offset"), ref ModuleConfig.Offset, 0.1f, 1f, "%.1f"))
                SaveConfig(ModuleConfig);
        }
    }

    private class PlayerDotImageNode : OverlayNode
    {
        public override OverlayLayer OverlayLayer     => OverlayLayer.Foreground;
        public override bool         HideWithNativeUi => false;

        private readonly IconImageNode imageNode;

        public PlayerDotImageNode()
        {
            imageNode = new IconImageNode
            {
                IconId     = 60952,
                FitTexture = true
            };
            imageNode.AttachNode(this);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();

            imageNode.Size   = Size;
            imageNode.Origin = new Vector2(ModuleConfig.Size / 2.0f);
        }

        protected override void OnUpdate()
        {
            Size = new Vector2(ModuleConfig.Size);

            imageNode.Color  = ModuleConfig.Color;
            imageNode.IconId = ModuleConfig.IconID;

            Timeline?.PlayAnimation(1);

            if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer)
            {
                IsVisible = false;
                return;
            }

            IsVisible = !DService.Instance().Condition[ConditionFlag.Occupied38]                                   &&
                        (!ModuleConfig.OnlyInCombat   || DService.Instance().Condition[ConditionFlag.InCombat])    &&
                        (!ModuleConfig.OnlyInDuty     || DService.Instance().Condition[ConditionFlag.BoundByDuty]) &&
                        (!ModuleConfig.OnlyUnsheathed || IsWeaponUnsheathed());

            if (!IsVisible)
                return;

            var   offset = ModuleConfig.Offset;
            var   angle  = -localPlayer.Rotation;
            float cos    = MathF.Cos(angle), sin = MathF.Sin(angle);

            var rotatedOffset = new Vector3((cos * offset.X) - (sin * offset.Z), offset.Y, (sin * offset.X) + (cos * offset.Z));
            DService.Instance().GameGUI.WorldToScreen(localPlayer.Position + rotatedOffset, out var screenPos);

            Position = screenPos - (imageNode.Size / 2f);
        }
    }

    private class Config : ModuleConfiguration
    {
        public bool OnlyInCombat = true;
        public bool OnlyInDuty   = true;
        public bool OnlyUnsheathed;

        public Vector4 Color  = new(1f, 1f, 1f, 1f);
        public float   Size   = 96f;
        public uint    IconID = 60422;
        public Vector3 Offset = Vector3.Zero;
    }
}
