using System.Timers;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using OmenTools.Interop.Windows.Models;
using OmenTools.OmenService;
using OmenTools.Threading;
using Timer = System.Timers.Timer;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoNoviceNetwork : ModuleBase
{
    private static Config ModuleConfig = null!;

    private static Timer? AfkTimer;

    private static int  TryTimes;
    private static bool IsJoined;
    private static bool IsMentor;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoNoviceNetworkTitle"),
        Description = Lang.Get("AutoNoviceNetworkDescription"),
        Category    = ModuleCategory.General
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        TaskHelper ??= new() { TimeoutMS = 5_000 };

        AfkTimer           ??= new(10_000);
        AfkTimer.Elapsed   +=  OnAfkStateCheck;
        AfkTimer.AutoReset =   true;
        AfkTimer.Enabled   =   true;
    }

    protected override void ConfigUI()
    {
        if (Throttler.Shared.Throttle("AutoNoviceNetwork-UpdateInfo", 1000))
        {
            IsMentor = PlayerState.Instance()->IsMentor();
            IsJoined = IsInNoviceNetwork();
        }

        ImGui.TextUnformatted($"{Lang.Get("AutoNoviceNetwork-JoinState")}:");

        ImGui.SameLine();
        ImGui.TextColored
        (
            IsJoined ? ImGuiColors.HealerGreen : ImGuiColors.DPSRed,
            IsJoined ? "√" : "×"
        );

        ImGui.TextUnformatted($"{Lang.Get("AutoNoviceNetwork-AttemptedTimes")}:");

        ImGui.SameLine();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{TryTimes}");

        ImGui.NewLine();

        using (ImRaii.Disabled(TaskHelper.IsBusy || !IsMentor))
        {
            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Play, Lang.Get("Start")))
            {
                TryTimes = 0;
                TaskHelper.Enqueue(EnqueueARound);
            }
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Stop, Lang.Get("Stop")))
            TaskHelper.Abort();

        ImGui.NewLine();

        if (ImGui.Checkbox(Lang.Get("AutoNoviceNetwork-TryJoinWhenInactive"), ref ModuleConfig.IsTryJoinWhenInactive))
            ModuleConfig.Save(this);

        ImGuiOm.HelpMarker(Lang.Get("AutoNoviceNetwork-TryJoinWhenInactiveHelp"), 20f * GlobalUIScale);
    }

    private void EnqueueARound()
    {
        if (!(IsMentor = PlayerState.Instance()->IsMentor())) return;

        TaskHelper.Enqueue
        (() =>
            {
                if (PlayerState.Instance()->IsPlayerStateFlagSet(PlayerStateFlag.IsNoviceNetworkAutoJoinEnabled)) return;
                ChatManager.Instance().SendMessage("/beginnerchannel on");
            }
        );

        TaskHelper.Enqueue(TryJoin);

        TaskHelper.DelayNext(250);
        TaskHelper.Enqueue(() => TryTimes++);

        TaskHelper.Enqueue
        (() =>
            {
                if (IsInNoviceNetwork())
                {
                    TaskHelper.Abort();
                    return;
                }

                EnqueueARound();
            }
        );
    }

    private static void TryJoin() =>
        InfoProxyNoviceNetwork.Instance()->SendJoinRequest();

    private static bool IsInNoviceNetwork()
    {
        var infoProxy = InfoModule.Instance()->GetInfoProxyById(InfoProxyId.NoviceNetwork);
        return ((int)infoProxy[1].VirtualTable & 1) != 0;
    }

    private void OnAfkStateCheck(object? sender, ElapsedEventArgs e)
    {
        if (!(IsMentor = PlayerState.Instance()->IsMentor())) return;

        IsJoined = IsInNoviceNetwork();
        if (IsJoined) return;

        if (!ModuleConfig.IsTryJoinWhenInactive         || TaskHelper.IsBusy) return;
        if (DService.Instance().Condition.IsBoundByDuty || DService.Instance().Condition.IsOccupiedInEvent) return;

        if (LastInputInfo.GetIdleTimeTick() > 10_000 || Framework.Instance()->WindowInactive)
            TryJoin();
    }

    protected override void Uninit()
    {
        AfkTimer?.Stop();
        if (AfkTimer != null)
            AfkTimer.Elapsed -= OnAfkStateCheck;
        AfkTimer?.Dispose();
        AfkTimer = null;

        TryTimes = 0;
    }

    private class Config : ModuleConfig
    {
        public bool IsTryJoinWhenInactive;
    }
}
