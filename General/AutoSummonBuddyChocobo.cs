using System;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoSummonBuddyChocobo : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSummonBuddyChocoboTitle"),
        Description = GetLoc("AutoSummonBuddyChocoboDescription"),
        Category    = ModuleCategories.General,
        Author      = ["Veever"]
    };

    private const uint GYSAHL_GREENS_ITEM_ID = 4868;

    private static Config ModuleConfig = null!;

    private static bool HasNotifiedInCurrentZone;

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new();

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        Cleanup();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoSummonBuddyChocobo-AutoSwitchStance"), ref ModuleConfig.AutoSwitchStance))
            SaveConfig(ModuleConfig);

        if (ModuleConfig.AutoSwitchStance)
        {
            using (ImRaii.PushIndent())
            {
                var isFirst = true;

                foreach (var checkPoint in Enum.GetValues<ChocoboStance>())
                {
                    if (!LuminaGetter.TryGetRow<BuddyAction>((uint)checkPoint, out var buddyAction)) continue;

                    if (!isFirst)
                        ImGui.SameLine();
                    isFirst = false;

                    if (ImGui.RadioButton(buddyAction.Name.ToString(), ModuleConfig.Stance == checkPoint))
                    {
                        ModuleConfig.Stance = checkPoint;
                        SaveConfig(ModuleConfig);
                    }
                }
            }
        }

        ImGui.NewLine();

        if (ImGui.Checkbox(GetLoc("AutoSummonBuddyChocobo-NotBattleJobUsingGys"), ref ModuleConfig.NotBattleJobUsingGysahl))
            SaveConfig(ModuleConfig);

        ImGui.NewLine();

        if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("SendTTS"), ref ModuleConfig.SendTTS))
            SaveConfig(ModuleConfig);
    }

    private void OnZoneChanged(ushort zone)
    {
        Cleanup();

        if (!IsZoneValid()) return;

        LocalPlayerState.Instance().PlayerMoveStateChanged += OnPlayerMoving;
        DService.Instance().Condition.ConditionChange      += OnConditionChanged;
        LogMessageManager.Instance().RegPost(OnLogMessage);
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.Mounted) return;
        
        if (!IsZoneValid())
        {
            Cleanup();
            return;
        }
        
        if (value) return;

        OnPlayerMoving(false);
    }
    
    // 给那种原地挂机但一定要陆行鸟在场的人准备的
    private void OnLogMessage(uint logMessageID, LogMessageQueueItem item)
    {
        if (logMessageID != 1328) return;
        
        if (!IsZoneValid())
        {
            Cleanup();
            return;
        }
        
        OnPlayerMoving(false);
    }

    private void OnPlayerMoving(bool state)
    {
        if (!IsZoneValid())
        {
            Cleanup();
            return;
        }

        if (state)
        {
            TaskHelper.Abort();
            return;
        }

        if (LocalPlayerState.Object is not { IsDead: false } ||
            OccupiedInEvent                                  ||
            IsOnMount)
            return;

        if (!ModuleConfig.NotBattleJobUsingGysahl && LocalPlayerState.ClassJobData.DohDolJobIndex != -1)
            return;

        var companionInfo = UIState.Instance()->Buddy.CompanionInfo;

        if (companionInfo.TimeLeft > 300)
        {
            if (ModuleConfig.AutoSwitchStance && companionInfo.ActiveCommand != (int)ModuleConfig.Stance)
                TaskHelper.Enqueue(() => UseActionManager.Instance().UseAction(ActionType.BuddyAction, (uint)ModuleConfig.Stance));
            return;
        }

        if (LocalPlayerState.GetItemCount(GYSAHL_GREENS_ITEM_ID) <= 3)
        {
            if (HasNotifiedInCurrentZone) return;
            HasNotifiedInCurrentZone = true;

            var notificationMessage = GetLoc("AutoSummonBuddyChocobo-NotificationMessage");
            if (ModuleConfig.SendChat)
                Chat(notificationMessage);
            if (ModuleConfig.SendNotification)
                NotificationInfo(notificationMessage);
            if (ModuleConfig.SendTTS)
                Speak(notificationMessage);

            return;
        }

        TaskHelper.DelayNext(500);
        TaskHelper.Enqueue(() => { UseActionManager.Instance().UseActionLocation(ActionType.Item, GYSAHL_GREENS_ITEM_ID, extraParam: 0xFFFF); }
        );
    }

    private void Cleanup()
    {
        LocalPlayerState.Instance().PlayerMoveStateChanged -= OnPlayerMoving;
        DService.Instance().Condition.ConditionChange      -= OnConditionChanged;
        LogMessageManager.Instance().Unreg(OnLogMessage);

        TaskHelper?.Abort();

        HasNotifiedInCurrentZone = false;
    }

    // 因为在鸟棚里的话没可能隔空取出来, 必定要先回去取出然后再召唤, 期间要切换至少三个区域
    private static bool IsZoneValid() =>
        !GameState.IsInPVPArea                                           &&
        GameState.TerritoryIntendedUse == TerritoryIntendedUse.Overworld &&
        !PlayerState.Instance()->IsPlayerStateFlagSet(PlayerStateFlag.IsBuddyInStable);

    private enum ChocoboStance
    {
        FreeStance     = 0x04,
        DefenderStance = 0x05,
        AttackerStance = 0x06,
        HealerStance   = 0x07
    }

    private class Config : ModuleConfiguration
    {
        public bool SendChat;
        public bool SendNotification = true;
        public bool SendTTS;

        public bool NotBattleJobUsingGysahl;

        public bool          AutoSwitchStance;
        public ChocoboStance Stance = ChocoboStance.FreeStance;
    }
}
