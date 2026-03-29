using System.Numerics;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public class AutoUseEarthsReply : ModuleBase
{
    private const uint RiddleOfEarthAction = 29482; // 金刚极意
    private const uint EarthsReplyAction   = 29483; // 金刚转轮
    private const uint SprintStatus        = 1342;  // 冲刺
    private const uint GuardStatus         = 3054;  // 防御

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoUseEarthsReplyTitle"),
        Description = Lang.Get("AutoUseEarthsReplyDescription"),
        Category    = ModuleCategory.Action,
        Author      = ["ToxicStar"]
    };

    protected override void Init()
    {
        ModuleConfig =   Config.Load(this) ?? new();
        TaskHelper   ??= new() { TimeoutMS = 8_000 };

        UseActionManager.Instance().RegPostUseActionLocation(OnUseAction);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("AutoUseEarthsReply-UseWhenGuard"), ref ModuleConfig.UseWhenSprint))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox(Lang.Get("AutoUseEarthsReply-UseWhenSprint"), ref ModuleConfig.UseWhenGuard))
            ModuleConfig.Save(this);
    }

    private void OnUseAction(bool result, ActionType actionType, uint actionID, ulong targetID, Vector3 location, uint extraParam, byte a7)
    {
        if (actionType != ActionType.Action || actionID != RiddleOfEarthAction || !result) return;

        TaskHelper.Abort();
        TaskHelper.DelayNext(8_000, $"Delay_UseAction{EarthsReplyAction}", 1);
        TaskHelper.Enqueue
        (
            () =>
            {
                if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;

                if (!ModuleConfig.UseWhenSprint && localPlayer.StatusList.HasStatus(SprintStatus)) return;
                if (!ModuleConfig.UseWhenGuard  && localPlayer.StatusList.HasStatus(GuardStatus)) return;

                UseActionManager.Instance().UseActionLocation(ActionType.Action, EarthsReplyAction);
            },
            $"UseAction_{EarthsReplyAction}",
            500,
            weight: 1
        );
    }

    protected override void Uninit() =>
        UseActionManager.Instance().Unreg(OnUseAction);

    public class Config : ModuleConfig
    {
        public bool UseWhenGuard;
        public bool UseWhenSprint;
    }
}
