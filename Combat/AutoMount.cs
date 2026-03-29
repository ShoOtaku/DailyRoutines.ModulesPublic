using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmenTools.ImGuiOm.Widgets.Combos;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading;
using OmenTools.Threading.TaskHelper;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoMount : ModuleBase
{
    private static Config ModuleConfig = null!;

    private static readonly MountSelectCombo MountSelectCombo = new("Mount");
    private static readonly ZoneSelectCombo  ZoneSelectCombo  = new("Zone");

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoMountTitle"),
        Description = Lang.Get("AutoMountDescription"),
        Category    = ModuleCategory.Combat
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        MountSelectCombo.SelectedID = ModuleConfig.SelectedMount;
        ZoneSelectCombo.SelectedIDs = ModuleConfig.BlacklistZones;

        TaskHelper ??= new TaskHelper { TimeoutMS = 20000 };

        DService.Instance().Condition.ConditionChange    += OnConditionChanged;
        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{LuminaWrapper.GetAddonText(4964)}");

        using (ImRaii.PushIndent())
        {
            ImGui.SetNextItemWidth(300f * GlobalUIScale);

            if (MountSelectCombo.DrawRadio())
            {
                ModuleConfig.SelectedMount = MountSelectCombo.SelectedID;
                ModuleConfig.Save(this);
            }

            ImGui.SameLine();

            if (ImGui.Button($"{FontAwesomeIcon.Eraser.ToIconString()} {Lang.Get("Clear")}"))
            {
                ModuleConfig.SelectedMount = 0;
                ModuleConfig.Save(this);
            }

            if (ModuleConfig.SelectedMount == 0 || !LuminaGetter.TryGetRow(ModuleConfig.SelectedMount, out Mount selectedMount))
            {
                if (ImageHelper.TryGetGameIcon(118, out var texture))
                    ImGuiOm.TextImage(LuminaWrapper.GetGeneralActionName(9), texture.Handle, new(ImGui.GetTextLineHeightWithSpacing()));
            }
            else
            {
                if (ImageHelper.TryGetGameIcon(selectedMount.Icon, out var texture))
                    ImGuiOm.TextImage(selectedMount.Singular.ToString(), texture.Handle, new(ImGui.GetTextLineHeightWithSpacing()));
            }
        }

        ImGui.Spacing();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("BlacklistZones")}");

        using (ImRaii.PushIndent())
        {
            ImGui.SetNextItemWidth(300f * GlobalUIScale);

            if (ZoneSelectCombo.DrawCheckbox())
            {
                ModuleConfig.BlacklistZones = ZoneSelectCombo.SelectedIDs;
                ModuleConfig.Save(this);
            }
        }

        ImGui.Spacing();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("Delay")}");

        using (ImRaii.PushIndent())
        {
            ImGui.SetNextItemWidth(300f * GlobalUIScale);
            if (ImGui.InputInt("ms###AutoMount-Delay", ref ModuleConfig.Delay))
                ModuleConfig.Delay = Math.Max(0, ModuleConfig.Delay);
            if (ImGui.IsItemDeactivatedAfterEdit())
                ModuleConfig.Save(this);
        }

        ImGui.NewLine();

        if (ImGui.Checkbox(Lang.Get("AutoMount-MountWhenZoneChange"), ref ModuleConfig.MountWhenZoneChange))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox(Lang.Get("AutoMount-MountWhenGatherEnd"), ref ModuleConfig.MountWhenGatherEnd))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox(Lang.Get("AutoMount-MountWhenCombatEnd"), ref ModuleConfig.MountWhenCombatEnd))
            ModuleConfig.Save(this);
    }

    private void OnZoneChanged(ushort zone)
    {
        if (!ModuleConfig.MountWhenZoneChange                             ||
            GameState.TerritoryType == 0                                  ||
            ModuleConfig.BlacklistZones.Contains(GameState.TerritoryType) ||
            !CanUseMountCurrentZone())
            return;

        TaskHelper.Abort();
        TaskHelper.Enqueue(UseMount);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (ModuleConfig.BlacklistZones.Contains(GameState.TerritoryType)) return;

        switch (flag)
        {
            case ConditionFlag.Gathering when !value && ModuleConfig.MountWhenGatherEnd:
            case ConditionFlag.InCombat when !value                                 &&
                                             ModuleConfig.MountWhenCombatEnd        &&
                                             !DService.Instance().ClientState.IsPvP &&
                                             (FateManager.Instance()->CurrentFate           == null ||
                                              FateManager.Instance()->CurrentFate->Progress == 100):
                if (!CanUseMountCurrentZone()) return;

                TaskHelper.Abort();
                TaskHelper.DelayNext(500);
                TaskHelper.Enqueue(UseMount);
                break;
        }
    }

    private bool UseMount()
    {
        if (!Throttler.Shared.Throttle("AutoMount-UseMount")) return false;
        if (DService.Instance().Condition.IsBetweenAreas) return false;
        if (AgentMap.Instance()->IsPlayerMoving) return true;
        if (DService.Instance().Condition.IsCasting) return false;
        if (DService.Instance().Condition.IsOnMount) return true;
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 9) != 0) return false;

        if (ModuleConfig.Delay > 0)
            TaskHelper.DelayNext(ModuleConfig.Delay);

        TaskHelper.DelayNext(100);
        TaskHelper.Enqueue
        (() => ModuleConfig.SelectedMount == 0
                   ? UseActionManager.Instance().UseAction(ActionType.GeneralAction, 9)
                   : UseActionManager.Instance().UseAction(ActionType.Mount,         ModuleConfig.SelectedMount)
        );
        return true;
    }

    private static bool CanUseMountCurrentZone() =>
        GameState.TerritoryTypeData is { Mount: true };

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Instance().Condition.ConditionChange    -= OnConditionChanged;
    }

    private class Config : ModuleConfig
    {
        public HashSet<uint> BlacklistZones      = [];
        public int           Delay               = 1000;
        public bool          MountWhenCombatEnd  = true;
        public bool          MountWhenGatherEnd  = true;
        public bool          MountWhenZoneChange = true;

        public uint SelectedMount;
    }
}
