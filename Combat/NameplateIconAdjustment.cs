using System.Numerics;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.ModulesPublic;

public unsafe class NameplateIconAdjustment : ModuleBase
{
    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("NameplateIconAdjustmentTitle"),
        Description = Lang.Get("NameplateIconAdjustmentDescription"),
        Category    = ModuleCategory.Combat,
        Author      = ["Marsh"]
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "NamePlate", OnAddon);
    }

    protected override void ConfigUI()
    {
        if (ImGui.SliderFloat(Lang.Get("Scale"), ref ModuleConfig.Scale, 0f, 2f, "%.2f"))
            ModuleConfig.Save(this);

        if (ImGui.SliderFloat2($"{Lang.Get("IconOffset")}", ref ModuleConfig.Offset, -100f, 100f, "%.1f"))
            ModuleConfig.Save(this);
    }

    private static void OnAddon(AddonEvent type, AddonArgs? args)
    {
        var addon = NamePlate;
        if (!NamePlate->IsAddonAndNodesReady()) return;

        {
            var componentNode = addon->GetComponentNodeById(2);
            if (componentNode == null) return;

            var imageNode = (AtkImageNode*)componentNode->Component->UldManager.SearchNodeById(9);
            if (imageNode == null) return;

            imageNode->SetScale(ModuleConfig.Scale, ModuleConfig.Scale);

            var posX = (1.5f - ModuleConfig.Scale * 0.5f) * 96f + ModuleConfig.Offset.X * ModuleConfig.Scale;
            var posY = 4                                        + ModuleConfig.Offset.Y * ModuleConfig.Scale;
            imageNode->SetPositionFloat(posX, posY);
        }

        for (uint i = 0; i < 49; i++)
        {
            var componentNode = addon->GetComponentNodeById(i + 20001);

            if (componentNode == null) return;

            var imageNode = (AtkImageNode*)componentNode->Component->UldManager.SearchNodeById(9);
            if (imageNode == null) return;

            imageNode->SetScale(ModuleConfig.Scale, ModuleConfig.Scale);

            var posX = (1.5f - ModuleConfig.Scale * 0.5f) * 96f + ModuleConfig.Offset.X * ModuleConfig.Scale;
            var posY = 4                                        + ModuleConfig.Offset.Y * ModuleConfig.Scale;
            imageNode->SetPositionFloat(posX, posY);
        }
    }

    protected override void Uninit() =>
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);

    public class Config : ModuleConfig
    {
        public Vector2 Offset;
        public float   Scale = 1f;
    }
}
