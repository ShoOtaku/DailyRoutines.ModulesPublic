using System;
using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmenTools.Extensions;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoLucidDreaming : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoLucidDreamingTitle"),
        Description = GetLoc("AutoLucidDreamingDescription"),
        Category    = ModuleCategories.Action,
        Author      = ["qingsiweisan"]
    };
    
    private const int    ABILITY_LOCK_TIME_MS    = 600;
    private const float  USE_IN_GCD_WINDOW_START = 60;
    private const float  USE_IN_GCD_WINDOW_END   = 95;
    private const uint   LUCID_DREAMING_ID       = 7562;
    private const ushort TRANSCENDENT_STATUS     = 418;

    private static readonly HashSet<uint> ValidClassJobs = [6, 7, 15, 19, 20, 21, 23, 24, 26, 27, 28, 33, 35, 36, 40];

    private static Config ModuleConfig = null!;
    
    private static DateTime LastLucidDreamingUseTime = DateTime.MinValue;
    private static bool     IsAbilityLocked;

    protected override void Init()
    {
        TaskHelper   ??= new() { TimeoutMS = 30_000 };
        ModuleConfig =   LoadConfig<Config>() ?? new();

        DService.Instance().Condition.ConditionChange += OnConditionChanged;

        CheckAndEnqueue();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OnlyInDuty"), ref ModuleConfig.OnlyInDuty))
        {
            SaveConfig(ModuleConfig);
            CheckAndEnqueue();
        }

        ImGui.SetNextItemWidth(250f * GlobalFontScale);
        if (ImGui.DragInt("##MpThresholdSlider", ref ModuleConfig.MpThreshold, 100f, 3000, 9000, $"{LuminaWrapper.GetAddonText(233)}: %d"))
            SaveConfig(ModuleConfig);
        
        ImGui.NewLine();
        
        if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
            SaveConfig(ModuleConfig);
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
        
        if ((ModuleConfig.OnlyInDuty && GameState.ContentFinderCondition == 0) ||
            GameState.IsInPVPArea                                              ||
            !DService.Instance().Condition[ConditionFlag.InCombat])
            return;

        TaskHelper.Enqueue(MainProcess);
    }

    private void MainProcess()
    {
        TaskHelper.Abort();

        if (!UIModule.IsScreenReady() || OccupiedInEvent)
        {
            TaskHelper.DelayNext(1000);
            TaskHelper.Enqueue(MainProcess);
            return;
        }

        if (!DService.Instance().Condition[ConditionFlag.InCombat]         ||
            !ValidClassJobs.Contains(LocalPlayerState.ClassJob) ||
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
            statusManager.HasStatus(1204)                                           ||
            localPlayer->Mode == CharacterModes.AnimLock                            ||
            localPlayer->IsCasting                                                  ||
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
        TaskHelper.Enqueue(() =>
                           {
                               if (IsAbilityLocked) return false;
            
                               var result = UseActionManager.Instance().UseActionLocation(ActionType.Action, LUCID_DREAMING_ID);
                               if (result)
                               {
                                   LastLucidDreamingUseTime = capturedTime;
                                   if (ModuleConfig.SendNotification && Throttler.Throttle("AutoLucidDreaming-Notification", 10_000))
                                       NotificationInfo(GetLoc("AutoLucidDreaming-Notification", localPlayer->Mana));
                               }
            
                               return result;
                           }, $"UseAction_{LUCID_DREAMING_ID}", 5_000, weight: 1);
        return true;
    }
    
    private class Config : ModuleConfiguration
    {
        public bool OnlyInDuty;
        public int  MpThreshold      = 7000;
        public bool SendNotification = true;
    }
}
