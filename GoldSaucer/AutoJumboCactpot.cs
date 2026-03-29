using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using OmenTools.Interop.Game.AddonEvent;

namespace DailyRoutines.ModulesPublic;

public class AutoJumboCactpot : ModuleBase
{
    private static readonly Dictionary<Mode, string> NumberModeLoc = new()
    {
        [Mode.Random] = Lang.Get("AutoJumboCactpot-Random"),
        [Mode.Fixed]  = Lang.Get("AutoJumboCactpot-Fixed")
    };

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoJumboCactpotTitle"),
        Description = Lang.Get("AutoJumboCactpotDescription"),
        Category    = ModuleCategory.GoldSaucer
    };


    protected override unsafe void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        TaskHelper ??= new() { TimeoutMS = 5_000 };

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "LotteryWeeklyInput", OnAddon);
        if (LotteryWeeklyInput->IsAddonAndNodesReady())
            OnAddon(AddonEvent.PostSetup, null);
    }

    protected override void ConfigUI()
    {
        ImGui.SetNextItemWidth(100f * GlobalUIScale);

        using (var combo = ImRaii.Combo($"{Lang.Get("AutoJumboCactpot-NumberMode")}", NumberModeLoc.GetValueOrDefault(ModuleConfig.NumberMode, string.Empty)))
        {
            if (combo)
            {
                foreach (var modePair in NumberModeLoc)
                {
                    if (ImGui.Selectable(modePair.Value, modePair.Key == ModuleConfig.NumberMode))
                    {
                        ModuleConfig.NumberMode = modePair.Key;
                        ModuleConfig.Save(this);
                    }
                }
            }
        }

        if (ModuleConfig.NumberMode == Mode.Fixed)
        {
            ImGui.SameLine();

            ImGui.SetNextItemWidth(100f * GlobalUIScale);
            ImGui.InputInt(Lang.Get("AutoJumboCactpot-FixedNumber"), ref ModuleConfig.FixedNumber);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                ModuleConfig.FixedNumber = Math.Clamp(ModuleConfig.FixedNumber, 0, 9999);
                ModuleConfig.Save(this);
            }
        }
    }

    private unsafe void OnAddon(AddonEvent type, AddonArgs args)
    {
        TaskHelper.Abort();

        TaskHelper.Enqueue
        (() =>
            {
                if (!DService.Instance().Condition.IsOccupiedInEvent)
                {
                    TaskHelper.Abort();
                    return true;
                }

                if (!LotteryWeeklyInput->IsAddonAndNodesReady()) return false;

                var number = ModuleConfig.NumberMode switch
                {
                    Mode.Random => Random.Shared.Next(0, 9999),
                    Mode.Fixed  => Math.Clamp(ModuleConfig.FixedNumber, 0, 9999),
                    _           => 0
                };

                LotteryWeeklyInput->Callback(number);
                return true;
            }
        );

        TaskHelper.Enqueue
        (() =>
            {
                AddonSelectYesnoEvent.ClickYes();
                return false;
            }
        );
    }

    protected override void Uninit() =>
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);

    private class Config : ModuleConfig
    {
        public int  FixedNumber = 1;
        public Mode NumberMode  = Mode.Random;
    }

    private enum Mode
    {
        Random,
        Fixed
    }
}
