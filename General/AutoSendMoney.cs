using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Timers;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoSendMoney : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSendMoneyTitle"),
        Description = GetLoc("AutoSendMoneyDescription"),
        Category    = ModuleCategories.General,
        Author      = ["status102"]
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private const uint MaximumGilPerTimes = 1_000_000;
    
    private static int RandomDelay => 
        Random.Shared.Next(ModuleConfig.Delay1, ModuleConfig.Delay2);
    
    private static Timer?  SelectPlayerTimer;
    private static Config? ModuleConfig;

    private static          int[]                  MoneyButton    = [];
    private static readonly List<Member>           MemberList     = [];
    private static readonly Dictionary<uint, long> EditPlan       = [];
    private static          Dictionary<uint, long> TradePlan      = [];
    private static readonly bool[]                 PreCheckStatus = new bool[2];
    private static          float                  NameLength = -1;

    private static bool IsRunning;         // 是否正在运行
    private static bool IsTrading;         // 是否正在交易
    private static uint LastTradeEntityID; // 上次/当前交易对象

    private static double PlanAll;
    private static long   CurrentChange;
    private static uint   CurrentMoney; // 交易框中自己的钱

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeLimitMS = 5_000 };

        MoneyButton = [-ModuleConfig.Step2, -ModuleConfig.Step1, ModuleConfig.Step1, ModuleConfig.Step2];

        TradeRequestHook = TradeRequestSig.GetHook<TradeRequestDelegate>(TradeRequestDetour);
        TradeRequestHook.Enable();
        
        TradeStatusUpdateHook = TradeStatusUpdateSig.GetHook<TradeStatusUpdateDelegate>(TradeStatusDetour);
        TradeStatusUpdateHook.Enable();

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Trade", OnTrade);

        SelectPlayerTimer         ??= new(1_000) { AutoReset = true };
        SelectPlayerTimer.Elapsed +=  AutoRequestTradeTick;
    }

    protected override void ConfigUI()
    {
        if (NameLength < 0)
            NameLength = ImGui.CalcTextSize(GetLoc("All")).X;
        
        DrawSetting();

        using (ImRaii.PushId("All"))     
            DrawOverallSetting(); 
        
        foreach (var p in MemberList)
        {
            using (ImRaii.PushId(p.EntityID.ToString()))     
                DrawPersonalSetting(p);
        }
    }

    protected override void Uninit()
    {
        SelectPlayerTimer?.Dispose();
        SelectPlayerTimer = null;

        DService.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Trade", OnTrade);
    }

    #region UI

    private void DrawSetting()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Settings")}:");

        ImGui.SameLine();
        ImGui.Text($"{GetLoc("AutoSendMoney-Step", 1)}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50f * GlobalFontScale);
        ImGui.InputInt("###Step1Input", ref ModuleConfig.Step1, flags: ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            MoneyButton = [-ModuleConfig.Step2, -ModuleConfig.Step1, ModuleConfig.Step1, ModuleConfig.Step2];
            SaveConfig(ModuleConfig);
        }

        ImGui.SameLine();
        ImGui.Text($"{GetLoc("AutoSendMoney-Step", 2)}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###Step2Input", ref ModuleConfig.Step2, flags: ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            MoneyButton = [-ModuleConfig.Step2, -ModuleConfig.Step1, ModuleConfig.Step1, ModuleConfig.Step2];
            SaveConfig(ModuleConfig);
        }

        ImGui.SameLine();
        ImGui.Text($"{GetLoc("AutoSendMoney-DelayLowerLimit")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###DelayUpperLimitInput", ref ModuleConfig.Delay1, flags: ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGui.Text($"{GetLoc("AutoSendMoney-DelayUpperLimit")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###DelayLowerLimitInput", ref ModuleConfig.Delay2, flags: ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.Spacing();

        using (ImRaii.Disabled(IsRunning))
        {
            if (ImGui.Button(GetLoc("Start")))
                Start();
        }

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("Stop")))
            Stop();

        ImGui.SameLine();
        ImGui.TextDisabled("|");

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("AutoSendMoney-UpdatePartyList")))
            UpdateTeamList();

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("AutoSendMoney-AddTarget")))
            AddTargetPlayer();
    }

    private static void DrawOverallSetting()
    {
        using var group   = ImRaii.Group();
        var       hasPlan = EditPlan.Count > 0;
        if (ImGui.Checkbox("##AllHasPlan", ref hasPlan))
        {
            if (hasPlan)
            {
                foreach (var p in MemberList.Where(p => !EditPlan.ContainsKey(p.EntityID)))
                    EditPlan.Add(p.EntityID, (int)(PlanAll * 10000));
            }
            else
                EditPlan.Clear();
        }

        ImGui.SameLine();
        ImGui.Text(GetLoc("All"));

        using var disabled = ImRaii.Disabled(IsRunning);

        ImGui.SameLine(NameLength + 60);

        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputDouble($"{GetLoc("Wan")}##AllMoney", ref PlanAll, 0, 0, "%.1lf", ImGuiInputTextFlags.CharsDecimal);
        if (ImGui.IsItemDeactivatedAfterEdit())
            EditPlan.Keys.ToList().ForEach(key => EditPlan[key] = (long)(PlanAll * 10000));

        CurrentChange = 0;
        foreach (var num in MoneyButton)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(15f * GlobalFontScale);
            var display = $"{(num < 0 ? string.Empty : '+')}{num}";
            if (ImGui.Button($"{display}##All"))
                CurrentChange = num * 1_0000;
        }

        if (CurrentChange != 0)
        {
            PlanAll += CurrentChange / 10000;
            MemberList.ForEach(p =>
            {
                if (!EditPlan.TryAdd(p.EntityID, CurrentChange))
                    EditPlan[p.EntityID] += CurrentChange;
            });
        }

        ImGui.SameLine();
        if (ImGui.Button($"{GetLoc("Reset")}###ResetAll"))
        {
            PlanAll = 0;
            foreach (var key in EditPlan.Keys)
                EditPlan[key] = 0;
        }
    }

    private static void DrawPersonalSetting(Member p)
    {
        using var group   = ImRaii.Group();
        var       hasPlan = EditPlan.ContainsKey(p.EntityID);

        using (ImRaii.Disabled(IsRunning))
        {
            if (ImGui.Checkbox($"##{p.FullName}-CheckBox", ref hasPlan))
            {
                if (hasPlan)
                    EditPlan.Add(p.EntityID, (int)(PlanAll * 10000));
                else
                    EditPlan.Remove(p.EntityID);
            }
        }

        if (p.GroupIndex >= 0)
        {
            ImGui.SameLine();
            ImGui.Text($"{(char)('A' + p.GroupIndex)}-");
        }

        ImGui.SameLine();
        ImGui.Text(p.FullName);

        ImGui.SameLine(NameLength + 60);
        if (!hasPlan)
            return;

        if (IsRunning)
            ImGui.Text(GetLoc("AutoSendMoney-Count", TradePlan.GetValueOrDefault(p.EntityID, 0)));
        else
        {
            ImGui.SetNextItemWidth(80f * GlobalFontScale);
            var value = EditPlan.TryGetValue(p.EntityID, out var valueToken) ? valueToken / 10000.0 : 0;
            ImGui.InputDouble($"{GetLoc("Wan")}##{p.EntityID}-Money", ref value, 0, 0, "%.1lf", ImGuiInputTextFlags.CharsDecimal);
            if (ImGui.IsItemDeactivatedAfterEdit())
                EditPlan[p.EntityID] = (int)(value * 10000);

            CurrentChange = 0;
            MoneyButton.ForEach(num =>
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(15f * GlobalFontScale);
                var display = $"{(num < 0 ? string.Empty : '+')}{num}";
                if (ImGui.Button($"{display}##Single_{p.EntityID}"))
                    CurrentChange = num * 1_0000;
            });

            if (CurrentChange != 0)
                EditPlan[p.EntityID] = (int)(value * 10000) + CurrentChange;

            ImGui.SameLine();
            if (ImGui.Button($"{GetLoc("Reset")}###ResetSingle-{p.EntityID}"))
                EditPlan[p.EntityID] = 0;
        }
    }

    private void Start()
    {
        IsRunning = true;
        TradePlan = [.. EditPlan.Where(i => i.Value > 0)];
        SelectPlayerTimer.Start();
        AutoRequestTradeTick(null, null);
    }

    private void Stop()
    {
        IsRunning = false;
        SelectPlayerTimer.Stop();
        TaskHelper?.Abort();
        TradePlan.Clear();
        LastTradeEntityID = 0;
    }

    public static void UpdateTeamList()
    {
        MemberList.Clear();
        var cwProxy = InfoProxyCrossRealm.Instance();
        if (cwProxy->IsCrossRealm)
        {
            var myGroup = InfoProxyCrossRealm.GetMemberByEntityId((uint)Control.GetLocalPlayer()->GetGameObjectId())->GroupIndex;
            AddMembersFromCRGroup(cwProxy->CrossRealmGroups[myGroup], myGroup);
            for (var i = 0; i < cwProxy->CrossRealmGroups.Length; i++)
            {
                if (i == myGroup)
                    continue;

                AddMembersFromCRGroup(cwProxy->CrossRealmGroups[i], i);
            }
        }
        else
        {
            var pAgentHUD = AgentHUD.Instance();
            for (var i = 0; i < pAgentHUD->PartyMemberCount; ++i)
            {
                var charData        = pAgentHUD->PartyMembers[i];
                var partyMemberName = SeString.Parse(charData.Name.Value).TextValue;

                AddTeamMember(charData.EntityId, partyMemberName, charData.Object->HomeWorld);
            }
        }

        // 从交易计划中移除不在小队中的玩家
        EditPlan.Where(p => MemberList.All(t => t.EntityID != p.Key)).ForEach(p => EditPlan.Remove(p.Key));
        foreach (var item in MemberList)
            EditPlan.TryAdd(item.EntityID, 0);

        NameLength = MemberList.Select(p => ImGui.CalcTextSize(p.FullName).X)
                               .Append(ImGui.CalcTextSize(GetLoc("All")).X).Max();
    }

    private static void AddMembersFromCRGroup(CrossRealmGroup crossRealmGroup, int groupIndex)
    {
        for (var i = 0; i < crossRealmGroup.GroupMemberCount; i++)
        {
            var groupMember = crossRealmGroup.GroupMembers[i];
            AddTeamMember(groupMember.EntityId, SeString.Parse(groupMember.Name).TextValue, (ushort)groupMember.HomeWorld, groupIndex);
        }
    }

    private static void AddTeamMember(uint entityID, string fullName, ushort worldID, int groupIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return;
        if (!PresetSheet.Worlds.TryGetValue(worldID, out var world))
            return;

        /*
        var splitName = fullName.Split(' ');
        if (splitName.Length != 2)
        {
            return;
        }*/

        MemberList.Add(new() { EntityID = entityID, FirstName = fullName, World = world.Name.ExtractText(), GroupIndex = groupIndex });
    }

    private static void AddTargetPlayer()
    {
        var target = TargetSystem.Instance()->GetTargetObject();
        if (target is not null &&
            DService.ObjectTable.SearchByEntityID(target->EntityId) is ICharacter { ObjectKind: ObjectKind.Player } player)
        {
            if (MemberList.Any(p => p.EntityID == player.EntityID))
                return;

            MemberList.Add(new(player));
            EditPlan.TryAdd(player.EntityID, 0);
            NameLength = MemberList.Select(p => ImGui.CalcTextSize(p.FullName).X)
                                   .Append(ImGui.CalcTextSize(GetLoc("All")).X).Max();
        }
    }

    #endregion

    #region Trade

    private static void OnTradeBegin(uint entityID)
    {
        CurrentMoney      = 0;
        IsTrading         = true;
        LastTradeEntityID = entityID;
    }

    /// <summary>
    ///     显示交易窗口后设置金额
    /// </summary>
    private void OnTrade(AddonEvent type, AddonArgs args)
    {
        if (!IsRunning)
            return;

        SelectPlayerTimer.Stop();
        TaskHelper?.Abort();

        if (!TradePlan.TryGetValue(LastTradeEntityID, out var value))
        {
            TaskHelper?.DelayNext(RandomDelay);
            TaskHelper?.Enqueue(AddonTradeCancel);
        }
        else
        {
            TaskHelper?.DelayNext(RandomDelay);
            TaskHelper?.Enqueue(() => SetGil(value >= MaximumGilPerTimes ? MaximumGilPerTimes : (uint)value));
            TaskHelper?.DelayNext(RandomDelay);
            TaskHelper?.Enqueue(ConfirmPreCheck);
        }
    }

    public void OnTradePreCheckChanged(uint objectID, bool confirm)
    {
        if (!IsRunning)
            return;

        if (objectID == LocalPlayerState.EntityID)
            PreCheckStatus[0] = confirm;
        else if (objectID == LastTradeEntityID)
            PreCheckStatus[1] = confirm;

        if (!TradePlan.TryGetValue(LastTradeEntityID, out var value))
        {
            TaskHelper?.DelayNext(RandomDelay);
            TaskHelper?.Enqueue(AddonTradeCancel);
            return;
        }

        if (CurrentMoney <= value && !PreCheckStatus[0] && PreCheckStatus[1])
        {
            TaskHelper?.DelayNext(RandomDelay);
            TaskHelper?.Enqueue(ConfirmPreCheck);
        }
    }

    public void OnTradeFinalChecked()
    {
        if (!IsRunning)
            return;

        if (TradePlan.TryGetValue(LastTradeEntityID, out var value))
        {
            if (CurrentMoney <= value)
            {
                TaskHelper?.DelayNext(RandomDelay);
                TaskHelper?.Enqueue(() => AddonTradeFinalCheck());
            }
        }
    }

    public void OnTradeCancelled()
    {
        IsTrading = false;
        if (!IsRunning)
            return;

        TaskHelper?.Abort();
        SelectPlayerTimer.Start();
    }

    public void OnTradeFinished()
    {
        IsTrading = false;
        if (!IsRunning)
            return;

        if (!TradePlan.ContainsKey(LastTradeEntityID))
            Warning(GetLoc("AutoSendMoney-NoPlan"));
        else
        {
            TradePlan[LastTradeEntityID] -= CurrentMoney;
            if (TradePlan[LastTradeEntityID] <= 0)
            {
                TradePlan.Remove(LastTradeEntityID);
                EditPlan.Remove(LastTradeEntityID);
            }
        }

        SelectPlayerTimer.Start();
        if (TradePlan.Count == 0)
            Stop();
    }

    private static void RequestTrade(uint entityID, GameObject* gameObjectAddress)
    {
        TargetSystem.Instance()->Target = gameObjectAddress;
        TradeRequestDetour(InventoryManager.Instance(), entityID);
    }

    private static void SetGil(uint money)
    {
        InventoryManager.Instance()->SetTradeGilAmount(money);
        CurrentMoney = money;
    }

    private static void ConfirmPreCheck()
    {
        if (!PreCheckStatus[0])
        {
            AddonTradePreCheck();
            PreCheckStatus[0] = true;
        }
    }

    /// <summary>
    ///     遍历交易计划发起交易申请Tick
    /// </summary>
    private void AutoRequestTradeTick(object? sender, ElapsedEventArgs? e)
    {
        if (!IsRunning || TradePlan.Count == 0)
        {
            Stop();
            return;
        }

        if (IsTrading)
            return;

        if (LastTradeEntityID != 0 && TradePlan.ContainsKey(LastTradeEntityID))
        {
            var target = DService.ObjectTable.SearchByEntityID(LastTradeEntityID);
            if (target is null || !IsDistanceEnough(target.Position))
                return;

            TaskHelper?.Enqueue(() => RequestTrade(LastTradeEntityID, (GameObject*)target.Address));
        }

        TradePlan.Keys.ToList().ForEach(entityID =>
        {
            var target = DService.ObjectTable.SearchByEntityID(entityID);
            if (target is null || !IsDistanceEnough(target.Position))
                return;

            TaskHelper?.DelayNext(RandomDelay);
            TaskHelper?.Enqueue(() => RequestTrade(entityID, (GameObject*)target.Address));
        });
    }

    private static bool IsDistanceEnough(Vector3 pos2)
    {
        var pos = DService.ObjectTable.LocalPlayer.Position;
        return Math.Pow(pos.X - pos2.X, 2) + Math.Pow(pos.Z - pos2.Z, 2) < 16;
    }

    #endregion

    #region AddonTrade

    /// <summary>
    ///     主动取消交易
    /// </summary>
    private static void AddonTradeCancel()
    {
        var unitBase = GetAddonByName("Trade");
        if (unitBase is not null)
            Callback(unitBase, true, 1, 0);
    }

    /// <summary>
    ///     第一次确认
    /// </summary>
    private static void AddonTradePreCheck()
    {
        var unitBase = GetAddonByName("Trade");
        if (unitBase is not null)
            Callback(unitBase, true, 0, 0);
    }

    private static void AddonTradeFinalCheck(bool confirm = true)
    {
        var unitBase = SelectYesno;
        if (unitBase is not null)
            Callback(unitBase, true, confirm ? 0 : 1);
    }

    #endregion

    #region Hook

    private static readonly CompSig                     TradeRequestSig = new("48 89 6C 24 ?? 56 57 41 56 48 83 EC ?? 48 8B E9 44 8B F2 48 8D 0D");
    public delegate         nint                        TradeRequestDelegate(InventoryManager* manager, uint entityID);
    public static           Hook<TradeRequestDelegate>? TradeRequestHook;
    
    private static readonly CompSig TradeStatusUpdateSig =
        new("E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 4C 8B C2 8B D1 48 8D 0D ?? ?? ?? ?? E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 48 8D 0D");
    public delegate nint                             TradeStatusUpdateDelegate(InventoryManager* manager, nint entityID, nint a3);
    public static   Hook<TradeStatusUpdateDelegate>? TradeStatusUpdateHook;
    
    private static nint TradeRequestDetour(InventoryManager* manager, uint entityID)
    {
        var ret = TradeRequestHook.Original(manager, entityID);
        
        if (ret == 0)
            OnTradeBegin(entityID);
        
        return ret;
    }

    private nint TradeStatusDetour(InventoryManager* manager, nint entityID, nint a3)
    {
        switch (Marshal.ReadByte(a3 + 4))
        {
            case 1:
                // 别人交易你
                IsTrading = true;
                OnTradeBegin((uint)Marshal.ReadInt32(a3 + 40));
                break;
            case 16:
                // 交易状态更新
                switch (Marshal.ReadByte(a3 + 5))
                {
                    case 3:
                        OnTradePreCheckChanged((uint)Marshal.ReadInt32(a3 + 40), false);
                        break;
                    case 4:
                    case 5:
                        // 先确认条件的一边会产生一个a=4，两边都确认后发两个a=5
                        // 最终确认先确认的产生一个a=6，两边都确认后发两个a=1
                        OnTradePreCheckChanged((uint)Marshal.ReadInt32(a3 + 40), true);
                        break;
                }

                break;
            case 5:
                OnTradeFinalChecked();
                break;
            case 7:
                OnTradeCancelled();
                break;
            case 17:
                OnTradeFinished();
                break;
        }

        return TradeStatusUpdateHook.Original(manager, entityID, a3);
    }

    #endregion

    private class Config : ModuleConfiguration
    {
        public int Step1  = 50;
        public int Step2  = 100;
        public int Delay1 = 200;
        public int Delay2 = 500;
    }

    private class Member
    {
        public uint   EntityID;
        public string FirstName  = null!;
        public string World      = null!;
        public int    GroupIndex = -1;

        public Member() { }

        public Member(ICharacter gameObject)
        {
            EntityID  = gameObject.EntityID;
            FirstName = gameObject.Name.TextValue;
            
            var worldID = ((Character*)gameObject.Address)->HomeWorld;
            World = LuminaGetter.GetRow<World>(worldID)?.Name.ExtractText() ?? "???";
        }

        public string FullName => 
            $"{FirstName}@{World}";
    }
}
