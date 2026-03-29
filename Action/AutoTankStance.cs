using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public class AutoTankStance : ModuleBase
{
    private static readonly HashSet<uint> InvalidContentTypes = [16, 17, 18, 19, 31, 32, 34, 35];

    private static readonly Dictionary<uint, (uint Action, uint Status)> TankStanceActions = new()
    {
        // 剑术师 / 骑士
        [1]  = (28, 79),
        [19] = (28, 79),
        // 斧术师 / 战士
        [3]  = (48, 91),
        [21] = (48, 91),
        // 暗黑骑士
        [32] = (3629, 743),
        // 绝枪战士
        [37] = (16142, 1833)
    };

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoTankStanceTitle"),
        Description = Lang.Get("AutoTankStanceDescription"),
        Category    = ModuleCategory.Action
    };

    protected override void Init()
    {
        ModuleConfig =   Config.Load(this) ?? new();
        TaskHelper   ??= new() { TimeoutMS = 30_000 };

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        DService.Instance().DutyState.DutyRecommenced    += OnDutyRecommenced;
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("AutoTankStance-OnlyAutoStanceWhenOneTank"), ref ModuleConfig.OnlyAutoStanceWhenOneTank))
            ModuleConfig.Save(this);

        ImGuiOm.HelpMarker(Lang.Get("AutoTankStance-OnlyAutoStanceWhenOneTankHelp"));
    }

    private void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();

        if (!IsValidPVEDuty()) return;

        if (ModuleConfig.OnlyAutoStanceWhenOneTank &&
            GameState.ContentFinderConditionData.ContentMemberType.Value.TanksPerParty != 1)
            return;

        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private static bool CheckCurrentJob()
    {
        if (DService.Instance().Condition.IsBetweenAreas || DService.Instance().Condition.IsOccupiedInEvent || !UIModule.IsScreenReady()) return false;

        if (DService.Instance().ObjectTable.LocalPlayer is not { ClassJob.RowId: var job, IsTargetable: true } || job == 0)
            return false;

        if (!TankStanceActions.TryGetValue(job, out var info)) return true;
        if (LocalPlayerState.HasStatus(info.Status, out _)) return true;

        return UseActionManager.Instance().UseAction(ActionType.Action, info.Action);
    }

    private static bool IsValidPVEDuty() =>
        GameState.ContentFinderCondition != 0     &&
        !GameState.IsInPVPArea                    &&
        !GameState.ContentFinderConditionData.PvP &&
        !InvalidContentTypes.Contains(GameState.ContentFinderConditionData.ContentType.RowId);

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Instance().DutyState.DutyRecommenced    -= OnDutyRecommenced;
    }

    private class Config : ModuleConfig
    {
        public bool OnlyAutoStanceWhenOneTank = true;
    }
}
