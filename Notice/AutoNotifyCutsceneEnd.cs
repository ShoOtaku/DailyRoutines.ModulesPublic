using System;
using System.Diagnostics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoNotifyCutsceneEnd : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoNotifyCutsceneEndTitle"),
        Description = GetLoc("AutoNotifyCutsceneEndDescription"),
        Category    = ModuleCategories.Notice
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private static Config ModuleConfig = null!;

    private static bool       IsDutyEnd;
    private static Stopwatch? Stopwatch;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Stopwatch  ??= new();
        TaskHelper ??= new() { TimeoutMS = 30_000 };

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        DService.Instance().DutyState.DutyCompleted      += OnDutyComplete;
        
        OnZoneChanged(0);
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Instance().DutyState.DutyCompleted      -= OnDutyComplete;

        ClearResources();
        Stopwatch = null;
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("SendTTS"), ref ModuleConfig.SendTTS))
            SaveConfig(ModuleConfig);
    }

    private void OnZoneChanged(ushort zone)
    {
        ClearResources();

        if (GameState.ContentFinderCondition == 0 || GameState.IsInPVPArea) return;

        TaskHelper.Abort();
        TaskHelper.Enqueue
        (
            () =>
            {
                if (BetweenAreas || LocalPlayerState.Object == null) return false;

                if (GroupManager.Instance()->MainGroup.MemberCount < 2)
                {
                    TaskHelper.Abort();
                    return true;
                }

                DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_PartyList", OnAddon);
                return true;
            },
            "检查是否需要开始监控"
        );
    }

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        // 不应该吧
        var agent = AgentHUD.Instance();
        if (agent == null) return;

        // 不在副本内 / PVP / 副本已经结束 / 少于两个真人玩家 → 结束检查
        if (GameState.ContentFinderCondition == 0 ||
            GameState.IsInPVPArea                 ||
            IsDutyEnd                             ||
            GroupManager.Instance()->MainGroup.MemberCount < 2)
        {
            ClearResources();
            return;
        }

        // 本地玩家为空, 暂时不检查
        if (LocalPlayerState.Object == null) return;

        if (DService.Instance().Condition[ConditionFlag.InCombat])
        {
            // 进战时还在检查
            if (Stopwatch.IsRunning)
                CheckStopwatchAndRelay();

            return;
        }

        // 计时器运行中
        if (Stopwatch.IsRunning)
        {
            // 检查是否任一玩家仍在剧情状态
            if (IsAnyPartyMemberWatchingCutscene(agent))
                return;

            CheckStopwatchAndRelay();
        }
        else
        {
            // 居然无一人正在看剧情
            if (!IsAnyPartyMemberWatchingCutscene(agent))
                return;

            Stopwatch.Restart();
        }
    }

    private static void OnDutyComplete(object? sender, ushort zone) =>
        IsDutyEnd = true;

    private static void CheckStopwatchAndRelay()
    {
        if (!Stopwatch.IsRunning || !Throttler.Throttle("AutoNotifyCutsceneEnd-Relay", 1_000)) return;

        var elapsedTime = Stopwatch.Elapsed;
        Stopwatch.Reset();

        // 小于四秒 → 不播报
        if (elapsedTime < TimeSpan.FromSeconds(4)) return;

        var message = $"{GetLoc("AutoNotifyCutsceneEnd-NotificationMessage")}";
        if (ModuleConfig.SendChat)
            Chat($"{message} {GetLoc("AutoNotifyCutsceneEnd-NotificationMessage-WaitSeconds", $"{elapsedTime.TotalSeconds:F0}")}");
        if (ModuleConfig.SendNotification)
            NotificationInfo($"{message} {GetLoc("AutoNotifyCutsceneEnd-NotificationMessage-WaitSeconds", $"{elapsedTime.TotalSeconds:F0}")}");
        if (ModuleConfig.SendTTS)
            Speak(message);
    }

    private static bool IsAnyPartyMemberWatchingCutscene(AgentHUD* agent)
    {
        if (agent == null) return false;
        
        var group = GroupManager.Instance()->MainGroup;
        if (group.MemberCount < 2) return false;

        foreach (var member in agent->PartyMembers)
        {
            if (member.EntityId  == 0 ||
                member.ContentId == 0 ||
                member.Object    == null)
                continue;

            if (!DService.Instance().DutyState.IsDutyStarted &&
                !member.Object->GetIsTargetable())
                return true;

            if (member.Object->OnlineStatus == 15)
                return true;
        }

        return false;
    }

    private void ClearResources()
    {
        TaskHelper?.Abort();
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        Stopwatch?.Reset();
        IsDutyEnd = false;
    }

    private class Config : ModuleConfiguration
    {
        public bool SendChat         = true;
        public bool SendNotification = true;
        public bool SendTTS          = true;
    }
}
