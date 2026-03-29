using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading;
using Control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoLucidDreaming : ModuleBase
{
    private const int    ABILITY_LOCK_TIME_MS    = 600;
    private const float  USE_IN_GCD_WINDOW_START = 60;
    private const float  USE_IN_GCD_WINDOW_END   = 95;
    private const uint   LUCID_DREAMING_ID       = 7562;
    private const ushort TRANSCENDENT_STATUS     = 418;

    private static readonly HashSet<uint> ValidClassJobs = [6, 7, 15, 19, 20, 21, 23, 24, 26, 27, 28, 33, 35, 36, 40];

    private static Config ModuleConfig = null!;

    private static DateTime LastLucidDreamingUseTime = DateTime.MinValue;
    private static bool     IsAbilityLocked;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoLucidDreamingTitle"),
        Description = Lang.Get("AutoLucidDreamingDescription"),
        Category    = ModuleCategory.Action,
        Author      = ["qingsiweisan"]
    };

    protected override void Init()
    {
        TaskHelper   ??= new() { TimeoutMS = 30_000 };
        ModuleConfig =   Config.Load(this) ?? new();

        DService.Instance().Condition.ConditionChange += OnConditionChanged;

        CheckAndEnqueue();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("OnlyInDuty"), ref ModuleConfig.OnlyInDuty))
        {
            ModuleConfig.Save(this);
            CheckAndEnqueue();
        }

        ImGui.SetNextItemWidth(250f * GlobalUIScale);
        if (ImGui.DragInt("##MpThresholdSlider", ref ModuleConfig.MpThreshold, 100f, 3000, 9000, $"{LuminaWrapper.GetAddonText(233)}: %d"))
            ModuleConfig.Save(this);

        ImGui.NewLine();

        if (ImGui.Checkbox(Lang.Get("SendNotification"), ref ModuleConfig.SendNotification))
            ModuleConfig.Save(this);
    }

    protected override void Uninit() =>
        DService.Instance().Condition.ConditionChange -= OnConditionChanged;

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.InCombat) return;

        CheckAndEnqueue();
    }

    private void CheckAndEnqueue()
    {
        TaskHelper.Abort();

        if (ModuleConfig.OnlyInDuty && GameState.ContentFinderCondition == 0 ||
            GameState.IsInPVPArea                                            ||
            !DService.Instance().Condition[ConditionFlag.InCombat])
            return;

        TaskHelper.Enqueue(MainProcess);
    }

    private void MainProcess()
    {
        TaskHelper.Abort();

        if (!UIModule.IsScreenReady() || DService.Instance().Condition.IsOccupiedInEvent)
        {
            TaskHelper.DelayNext(1000);
            TaskHelper.Enqueue(MainProcess);
            return;
        }

        if (!DService.Instance().Condition[ConditionFlag.InCombat] ||
            !ValidClassJobs.Contains(LocalPlayerState.ClassJob)    ||
            !ActionManager.IsActionUnlocked(LUCID_DREAMING_ID))
            return;

        TaskHelper.Enqueue(PreventAbilityUse, "PreventAbilityUse", 5_000, weight: 1);
        TaskHelper.Enqueue(UseLucidDreaming,  "UseLucidDreaming",  5_000, weight: 1);

        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(MainProcess);
    }

    private bool PreventAbilityUse()
    {
        var timeSinceLastUse = (StandardTimeManager.Instance().Now - LastLucidDreamingUseTime).TotalMilliseconds;

        var shouldLock = timeSinceLastUse < ABILITY_LOCK_TIME_MS;
        IsAbilityLocked = shouldLock;

        if (shouldLock)
        {
            var remainingLockTime = ABILITY_LOCK_TIME_MS - (int)timeSinceLastUse;
            TaskHelper.DelayNext(Math.Min(remainingLockTime, 100));
        }

        return true;
    }

    private bool UseLucidDreaming()
    {
        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return false;

        var statusManager    = localPlayer->StatusManager;
        var currentMp        = localPlayer->Mana;
        var timeSinceLastUse = (StandardTimeManager.Instance().Now - LastLucidDreamingUseTime).TotalMilliseconds;

        if (timeSinceLastUse < ABILITY_LOCK_TIME_MS || currentMp >= ModuleConfig.MpThreshold)
            return true;

        // 刚复活的无敌
        if (statusManager.HasStatus(TRANSCENDENT_STATUS))
            return true;

        var actionManager = ActionManager.Instance();
        if (actionManager->GetActionStatus(ActionType.Action, LUCID_DREAMING_ID) != 0 ||
            statusManager.HasStatus(1204)                                             ||
            localPlayer->Mode == CharacterModes.AnimLock                              ||
            localPlayer->IsCasting                                                    ||
            actionManager->AnimationLock > 0)
            return true;

        var gcdRecast = actionManager->GetRecastGroupDetail(58);

        if (gcdRecast->IsActive)
        {
            var gcdTotal   = actionManager->GetRecastTimeForGroup(58);
            var gcdElapsed = gcdRecast->Elapsed;

            var gcdProgressPercent = gcdElapsed / gcdTotal * 100;
            if (gcdProgressPercent is < USE_IN_GCD_WINDOW_START or > USE_IN_GCD_WINDOW_END)
                return true;
        }

        var capturedTime = StandardTimeManager.Instance().Now;
        TaskHelper.Enqueue
        (
            () =>
            {
                if (IsAbilityLocked) return false;

                var result = UseActionManager.Instance().UseActionLocation(ActionType.Action, LUCID_DREAMING_ID);

                if (result)
                {
                    LastLucidDreamingUseTime = capturedTime;
                    if (ModuleConfig.SendNotification && Throttler.Shared.Throttle("AutoLucidDreaming-Notification", 10_000))
                        NotifyHelper.NotificationInfo(Lang.Get("AutoLucidDreaming-Notification", localPlayer->Mana));
                }

                return result;
            },
            $"UseAction_{LUCID_DREAMING_ID}",
            5_000,
            weight: 1
        );
        return true;
    }

    private class Config : ModuleConfig
    {
        public int  MpThreshold = 7000;
        public bool OnlyInDuty;
        public bool SendNotification = true;
    }
}
