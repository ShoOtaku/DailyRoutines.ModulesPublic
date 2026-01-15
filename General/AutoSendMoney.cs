using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
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
    
    private const uint MAXIMUM_GIL_PER_TRADE = 1_000_000;

    private static readonly CompSig TradeRequestSig = new("48 89 6C 24 ?? 56 57 41 56 48 83 EC ?? 48 8B E9 44 8B F2 48 8D 0D");

    private static readonly CompSig TradeStatusUpdateSig = new
    (
        "E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 4C 8B C2 8B D1 48 8D 0D ?? ?? ?? ?? E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 48 8D 0D"
    );

    private static Config ModuleConfig = null!;
    private static int[]  MoneyButtons = [];

    private static readonly List<Member>           Members  = [];
    private static readonly Dictionary<uint, long> EditPlan = [];

    private static float  NameLength = -1;
    private static double PlanAll;
    private static long   CurrentChange;

    private static SendMoneyRuntime? Runtime;

    private static bool IsRunning => Runtime != null;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        ApplyConfigChanges();
        TaskHelper ??= new() { TimeoutMS = 5_000 };
    }

    protected override void Uninit() =>
        Stop();
    
    #region UI

    protected override void ConfigUI()
    {
        if (NameLength < 0)
            NameLength = ImGui.CalcTextSize(GetLoc("All")).X;

        DrawSettings();

        using (ImRaii.PushId("All"))
            DrawGlobalPlan();

        foreach (var p in Members)
        {
            using (ImRaii.PushId(p.EntityID.ToString()))
                DrawMemberPlan(p);
        }
    }

    private void DrawSettings()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Settings")}:");

        ImGui.SameLine();
        ImGui.TextUnformatted($"{GetLoc("AutoSendMoney-Step", 1)}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50f * GlobalFontScale);
        ImGui.InputInt("###Step1Input", ref ModuleConfig.Step1, flags: ImGuiInputTextFlags.CharsDecimal);

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ApplyConfigChanges();
            ModuleConfig.Save(this);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"{GetLoc("AutoSendMoney-Step", 2)}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###Step2Input", ref ModuleConfig.Step2, flags: ImGuiInputTextFlags.CharsDecimal);

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ApplyConfigChanges();
            ModuleConfig.Save(this);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"{GetLoc("AutoSendMoney-DelayLowerLimit")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###DelayLowerLimitInput", ref ModuleConfig.Delay1, flags: ImGuiInputTextFlags.CharsDecimal);

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ApplyConfigChanges();
            ModuleConfig.Save(this);
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"{GetLoc("AutoSendMoney-DelayUpperLimit")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(50 * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("###DelayUpperLimitInput", ref ModuleConfig.Delay2, flags: ImGuiInputTextFlags.CharsDecimal);

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            ApplyConfigChanges();
            ModuleConfig.Save(this);
        }

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
            RefreshMembers();

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("AutoSendMoney-AddTarget")))
            AddCurrentTarget();
    }

    private static void DrawGlobalPlan()
    {
        using var group   = ImRaii.Group();
        var       hasPlan = EditPlan.Count > 0;

        if (ImGui.Checkbox("##AllHasPlan", ref hasPlan))
        {
            if (hasPlan)
            {
                foreach (var p in Members)
                {
                    if (EditPlan.ContainsKey(p.EntityID)) continue;
                    EditPlan.Add(p.EntityID, (long)(PlanAll * 10000));
                }
            }
            else
                EditPlan.Clear();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(GetLoc("All"));

        using var disabled = ImRaii.Disabled(IsRunning);

        ImGui.SameLine(NameLength + 60);

        ImGui.SetNextItemWidth(80f * GlobalFontScale);
        ImGui.InputDouble($"{GetLoc("Wan")}##AllMoney", ref PlanAll, 0, 0, "%.1lf", ImGuiInputTextFlags.CharsDecimal);

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var keys = EditPlan.Keys.ToArray();
            foreach (var key in keys)
                EditPlan[key] = (long)(PlanAll * 10000);
        }

        CurrentChange = 0;

        foreach (var num in MoneyButtons)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(15f * GlobalFontScale);
            var display = $"{(num < 0 ? string.Empty : '+')}{num}";
            if (ImGui.Button($"{display}##All"))
                CurrentChange = num * 1_0000;
        }

        if (CurrentChange != 0)
        {
            PlanAll += CurrentChange / 10000.0;

            foreach (var p in Members)
            {
                if (!EditPlan.TryAdd(p.EntityID, CurrentChange))
                    EditPlan[p.EntityID] += CurrentChange;
            }
        }

        ImGui.SameLine();

        if (ImGui.Button($"{GetLoc("Reset")}###ResetAll"))
        {
            PlanAll = 0;

            var keys = EditPlan.Keys.ToArray();
            foreach (var key in keys)
                EditPlan[key] = 0;
        }
    }

    private static void DrawMemberPlan(Member p)
    {
        using var group   = ImRaii.Group();
        var       hasPlan = EditPlan.ContainsKey(p.EntityID);

        using (ImRaii.Disabled(IsRunning))
        {
            if (ImGui.Checkbox($"##{p.FullName}-CheckBox", ref hasPlan))
            {
                if (hasPlan)
                    EditPlan.Add(p.EntityID, (long)(PlanAll * 10000));
                else
                    EditPlan.Remove(p.EntityID);
            }
        }

        if (p.GroupIndex >= 0)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"{(char)('A' + p.GroupIndex)}-");
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(p.FullName);

        ImGui.SameLine(NameLength + 60);
        if (!hasPlan)
            return;

        if (IsRunning)
            ImGui.TextUnformatted(GetLoc("AutoSendMoney-Count", Runtime?.GetRemaining(p.EntityID) ?? 0));
        else
        {
            ImGui.SetNextItemWidth(80f * GlobalFontScale);
            var value = EditPlan.TryGetValue(p.EntityID, out var valueToken) ? valueToken / 10000.0 : 0;
            ImGui.InputDouble($"{GetLoc("Wan")}##{p.EntityID}-Money", ref value, 0, 0, "%.1lf", ImGuiInputTextFlags.CharsDecimal);
            if (ImGui.IsItemDeactivatedAfterEdit())
                EditPlan[p.EntityID] = (long)(value * 10000);

            CurrentChange = 0;

            foreach (var num in MoneyButtons)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(15f * GlobalFontScale);
                var display = $"{(num < 0 ? string.Empty : '+')}{num}";
                if (ImGui.Button($"{display}##Single_{p.EntityID}"))
                    CurrentChange = num * 1_0000;
            }

            if (CurrentChange != 0)
                EditPlan[p.EntityID] = (long)(value * 10000) + CurrentChange;

            ImGui.SameLine();
            if (ImGui.Button($"{GetLoc("Reset")}###ResetSingle-{p.EntityID}"))
                EditPlan[p.EntityID] = 0;
        }
    }

    public static void RefreshMembers()
    {
        Members.Clear();
        var cwProxy = InfoProxyCrossRealm.Instance();

        if (cwProxy->IsCrossRealm)
        {
            var myGroup = InfoProxyCrossRealm.GetMemberByEntityId((uint)Control.GetLocalPlayer()->GetGameObjectId())->GroupIndex;
            AddCrossRealmGroupMembers(cwProxy->CrossRealmGroups[myGroup], myGroup);

            for (var i = 0; i < cwProxy->CrossRealmGroups.Length; i++)
            {
                if (i == myGroup)
                    continue;

                AddCrossRealmGroupMembers(cwProxy->CrossRealmGroups[i], i);
            }
        }
        else
        {
            var pAgentHUD = AgentHUD.Instance();

            for (var i = 0; i < pAgentHUD->PartyMemberCount; ++i)
            {
                var charData        = pAgentHUD->PartyMembers[i];
                var partyMemberName = SeString.Parse(charData.Name.Value).TextValue;

                AddMember(charData.EntityId, partyMemberName, charData.Object->HomeWorld);
            }
        }

        var removedKeys = EditPlan.Keys.Where(k => Members.All(m => m.EntityID != k)).ToArray();
        foreach (var key in removedKeys)
            EditPlan.Remove(key);

        foreach (var item in Members)
            EditPlan.TryAdd(item.EntityID, 0);

        NameLength = Members.Select(p => ImGui.CalcTextSize(p.FullName).X)
                            .Append(ImGui.CalcTextSize(GetLoc("All")).X)
                            .Max();
    }

    private static void AddCrossRealmGroupMembers(CrossRealmGroup crossRealmGroup, int groupIndex)
    {
        for (var i = 0; i < crossRealmGroup.GroupMemberCount; i++)
        {
            var groupMember = crossRealmGroup.GroupMembers[i];
            AddMember(groupMember.EntityId, SeString.Parse(groupMember.Name).TextValue, (ushort)groupMember.HomeWorld, groupIndex);
        }
    }

    private static void AddMember(uint entityID, string fullName, ushort worldID, int groupIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return;
        if (!PresetSheet.Worlds.TryGetValue(worldID, out var world))
            return;

        Members.Add(new() { EntityID = entityID, FirstName = fullName, World = world.Name.ToString(), GroupIndex = groupIndex });
    }

    private static void AddCurrentTarget()
    {
        var target = TargetSystem.Instance()->GetTargetObject();

        if (target is not null &&
            DService.Instance().ObjectTable.SearchByEntityID(target->EntityId) is ICharacter { ObjectKind: ObjectKind.Player } player)
        {
            if (Members.Any(p => p.EntityID == player.EntityID))
                return;

            Members.Add(new(player));
            EditPlan.TryAdd(player.EntityID, 0);
            NameLength = Members.Select(p => ImGui.CalcTextSize(p.FullName).X)
                                .Append(ImGui.CalcTextSize(GetLoc("All")).X)
                                .Max();
        }
    }

    #endregion

    #region 交易流程

    private void Start()
    {
        if (Runtime != null) return;
        ApplyConfigChanges();
        Runtime = new SendMoneyRuntime(this);
    }

    private static void Stop()
    {
        Runtime?.Dispose();
        Runtime = null;
    }

    #endregion

    #region 工具

    private static bool IsWithinTradeDistance(Vector3 pos2)
    {
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer)
            return false;

        var delta      = localPlayer.Position - pos2;
        var distanceSq = delta.X * delta.X    + delta.Z * delta.Z;
        return distanceSq < 16;
    }
    
    private static int GetRandomDelayMs()
    {
        var min = Math.Max(0, ModuleConfig.Delay1);
        var max = Math.Max(0, ModuleConfig.Delay2);
        if (max <= min) return min;
        return Random.Shared.Next(min, max);
    }

    private static void ApplyConfigChanges()
    {
        ModuleConfig.Step1 = Math.Abs(ModuleConfig.Step1);
        ModuleConfig.Step2 = Math.Abs(ModuleConfig.Step2);

        if (ModuleConfig.Step1 == 0) ModuleConfig.Step1 = 50;
        if (ModuleConfig.Step2 == 0) ModuleConfig.Step2 = 100;
        if (ModuleConfig.Step2 < ModuleConfig.Step1)
            (ModuleConfig.Step1, ModuleConfig.Step2) = (ModuleConfig.Step2, ModuleConfig.Step1);

        ModuleConfig.Delay1 = Math.Max(0, ModuleConfig.Delay1);
        ModuleConfig.Delay2 = Math.Max(0, ModuleConfig.Delay2);
        if (ModuleConfig.Delay2 < ModuleConfig.Delay1)
            (ModuleConfig.Delay1, ModuleConfig.Delay2) = (ModuleConfig.Delay2, ModuleConfig.Delay1);
        if (ModuleConfig.Delay2 == ModuleConfig.Delay1)
            ModuleConfig.Delay2 = ModuleConfig.Delay1 + 1;

        MoneyButtons =
        [
            -ModuleConfig.Step2,
            -ModuleConfig.Step1,
            ModuleConfig.Step1,
            ModuleConfig.Step2
        ];
    }

    #endregion

    private class Config : ModuleConfiguration
    {
        public int Delay1 = 200;
        public int Delay2 = 500;
        public int Step1  = 50;
        public int Step2  = 100;
    }

    private class Member
    {
        public uint   EntityID;
        public string FirstName  = null!;
        public int    GroupIndex = -1;
        public string World      = null!;

        public Member() { }

        public Member(ICharacter gameObject)
        {
            EntityID  = gameObject.EntityID;
            FirstName = gameObject.Name.TextValue;

            var worldID = gameObject.ToBCStruct()->HomeWorld;
            World = LuminaWrapper.GetWorldName(worldID) ?? "???";
        }

        public string FullName =>
            $"{FirstName}@{World}";
    }

    private readonly record struct PreCheckState
    (
        bool SelfConfirmed,
        bool OtherConfirmed
    );

    private sealed class SendMoneyRuntime : IDisposable
    {
        private readonly AutoSendMoney          owner;
        private readonly HashSet<uint>          pendingTradeRequests = [];
        private readonly Dictionary<uint, long> tradePlan;
        private          PreCheckState          checkState;
        private          uint                   currentMoney;
        private          bool                   isDisposed;

        private bool                             isTrading;
        private uint                             lastTradeEntityID;
        private Hook<TradeRequestDelegate>?      tradeRequestHook;
        private Hook<TradeStatusUpdateDelegate>? tradeStatusUpdateHook;

        public SendMoneyRuntime(AutoSendMoney owner)
        {
            this.owner = owner;
            tradePlan  = [.. EditPlan.Where(i => i.Value > 0)];

            tradeRequestHook = TradeRequestSig.GetHook<TradeRequestDelegate>(OnTradeRequest);
            tradeRequestHook.Enable();

            tradeStatusUpdateHook = TradeStatusUpdateSig.GetHook<TradeStatusUpdateDelegate>(OnTradeStatusUpdate);
            tradeStatusUpdateHook.Enable();

            DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Trade", OnTradeAddonSetup);
            FrameworkManager.Instance().Reg(OnFrameworkTick, 1_000);

            TryQueueNextTrade();
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            FrameworkManager.Instance().Unreg(OnFrameworkTick);
            DService.Instance().AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Trade", OnTradeAddonSetup);

            try
            {
                tradeRequestHook?.Disable();
            }
            catch
            {
                // ignored
            }

            try
            {
                tradeStatusUpdateHook?.Disable();
            }
            catch
            {
                // ignored
            }

            tradeRequestHook?.Dispose();
            tradeRequestHook = null;

            tradeStatusUpdateHook?.Dispose();
            tradeStatusUpdateHook = null;

            owner.TaskHelper?.Abort();
        }

        public long GetRemaining(uint entityID) =>
            tradePlan.GetValueOrDefault(entityID, 0);

        private void OnTradeAddonSetup(AddonEvent type, AddonArgs args)
        {
            owner.TaskHelper?.Abort();
            pendingTradeRequests.Clear();

            if (!tradePlan.TryGetValue(lastTradeEntityID, out var value))
            {
                owner.TaskHelper?.DelayNext(GetRandomDelayMs());
                owner.TaskHelper?.Enqueue(CancelTradeAddon, nameof(CancelTradeAddon));
            }
            else
            {
                owner.TaskHelper?.DelayNext(GetRandomDelayMs());
                owner.TaskHelper?.Enqueue(() => SetTradeGil((uint)Math.Min(value, MAXIMUM_GIL_PER_TRADE)), nameof(SetTradeGil));
                owner.TaskHelper?.DelayNext(GetRandomDelayMs());
                owner.TaskHelper?.Enqueue(ConfirmPreCheck, nameof(ConfirmPreCheck));
            }
        }

        private nint OnTradeRequest(InventoryManager* manager, uint entityID)
        {
            if (tradeRequestHook == null) return 0;
            var ret = tradeRequestHook.Original(manager, entityID);

            if (ret == 0)
                BeginTrade(entityID);
            else
                pendingTradeRequests.Remove(entityID);

            return ret;
        }

        private nint OnTradeStatusUpdate(InventoryManager* manager, nint entityID, nint a3)
        {
            var eventType = Marshal.ReadByte(a3 + 4);

            switch (eventType)
            {
                case 1:
                    BeginTrade((uint)Marshal.ReadInt32(a3 + 40));
                    break;
                case 16:
                    var updateType = Marshal.ReadByte(a3 + 5);

                    switch (updateType)
                    {
                        case 3:
                            UpdatePreCheck((uint)Marshal.ReadInt32(a3 + 40), false);
                            break;
                        case 4:
                        case 5:
                            UpdatePreCheck((uint)Marshal.ReadInt32(a3 + 40), true);
                            break;
                    }

                    break;
                case 5:
                    ConfirmFinal();
                    break;
                case 7:
                    CancelTrade();
                    break;
                case 17:
                    CompleteTrade();
                    break;
            }

            return tradeStatusUpdateHook == null ? 0 : tradeStatusUpdateHook.Original(manager, entityID, a3);
        }

        private void BeginTrade(uint entityID)
        {
            currentMoney      = 0;
            checkState        = default;
            isTrading         = true;
            lastTradeEntityID = entityID;
            pendingTradeRequests.Remove(entityID);
        }

        private void UpdatePreCheck(uint objectID, bool confirm)
        {
            if (objectID == LocalPlayerState.EntityID)
                checkState = checkState with { SelfConfirmed = confirm };
            else if (objectID == lastTradeEntityID)
                checkState = checkState with { OtherConfirmed = confirm };

            if (!tradePlan.TryGetValue(lastTradeEntityID, out var value))
            {
                owner.TaskHelper?.DelayNext(GetRandomDelayMs());
                owner.TaskHelper?.Enqueue(CancelTradeAddon, nameof(CancelTradeAddon));
                return;
            }

            if (currentMoney <= value && checkState is { SelfConfirmed: false, OtherConfirmed: true })
            {
                owner.TaskHelper?.DelayNext(GetRandomDelayMs());
                owner.TaskHelper?.Enqueue(ConfirmPreCheck, nameof(ConfirmPreCheck));
            }
        }

        private void ConfirmFinal()
        {
            if (tradePlan.TryGetValue(lastTradeEntityID, out var value) && currentMoney <= value)
            {
                owner.TaskHelper?.DelayNext(GetRandomDelayMs());
                owner.TaskHelper?.Enqueue(() => FinalCheckTradeAddon(), nameof(FinalCheckTradeAddon));
            }
        }

        private void CancelTrade()
        {
            isTrading = false;
            pendingTradeRequests.Clear();
            checkState = default;

            owner.TaskHelper?.Abort();
        }

        private void CompleteTrade()
        {
            isTrading = false;
            pendingTradeRequests.Clear();
            checkState = default;

            if (!tradePlan.ContainsKey(lastTradeEntityID))
                Warning(GetLoc("AutoSendMoney-NoPlan"));
            else
            {
                tradePlan[lastTradeEntityID] -= currentMoney;

                if (tradePlan[lastTradeEntityID] <= 0)
                {
                    tradePlan.Remove(lastTradeEntityID);
                    EditPlan.Remove(lastTradeEntityID);
                }
            }

            if (tradePlan.Count == 0)
                StopSelf();
        }

        private void IssueTradeRequest(uint entityID, GameObject* gameObjectAddress)
        {
            TargetSystem.Instance()->Target = gameObjectAddress;
            OnTradeRequest(InventoryManager.Instance(), entityID);
        }

        private void SetTradeGil(uint money)
        {
            InventoryManager.Instance()->SetTradeGilAmount(money);
            currentMoney = money;
        }

        private void ConfirmPreCheck()
        {
            if (!checkState.SelfConfirmed)
            {
                PreCheckTradeAddon();
                checkState = checkState with { SelfConfirmed = true };
            }
        }

        private void OnFrameworkTick(IFramework framework) =>
            TryQueueNextTrade();

        private void TryQueueNextTrade()
        {
            if (tradePlan.Count == 0)
            {
                StopSelf();
                return;
            }

            var taskHelper = owner.TaskHelper;
            if (taskHelper == null) return;

            if (isTrading || taskHelper.IsBusy)
                return;

            if (TrySelectTarget(out var entityID, out var targetAddress))
            {
                if (!pendingTradeRequests.Add(entityID))
                    return;

                taskHelper.DelayNext(GetRandomDelayMs(), $"{nameof(AutoSendMoney)}-IssueTradeRequest");
                taskHelper.Enqueue(() => IssueTradeRequest(entityID, (GameObject*)targetAddress), $"{nameof(IssueTradeRequest)}({entityID})");
            }
        }

        private void StopSelf()
        {
            Runtime = null;
            Dispose();
        }

        private bool TrySelectTarget(out uint entityID, out nint address)
        {
            entityID = 0;
            address  = 0;

            if (lastTradeEntityID != 0 && tradePlan.ContainsKey(lastTradeEntityID) && !pendingTradeRequests.Contains(lastTradeEntityID))
            {
                var target = DService.Instance().ObjectTable.SearchByEntityID(lastTradeEntityID);

                if (target != null && IsWithinTradeDistance(target.Position))
                {
                    entityID = lastTradeEntityID;
                    address  = target.Address;
                    return true;
                }
            }

            foreach (var id in tradePlan.Keys)
            {
                if (pendingTradeRequests.Contains(id)) continue;

                var target = DService.Instance().ObjectTable.SearchByEntityID(id);
                if (target == null) continue;

                if (!IsWithinTradeDistance(target.Position)) continue;

                entityID = id;
                address  = target.Address;
                return true;
            }

            return false;
        }

        private static void CancelTradeAddon()
        {
            if (Trade == null) return;
            Trade->Callback(1, 0);
        }

        private static void PreCheckTradeAddon()
        {
            if (Trade == null) return;
            Trade->Callback(0, 0);
        }

        private static void FinalCheckTradeAddon(bool confirm = true)
        {
            if (SelectYesno == null) return;
            SelectYesno->Callback(confirm ? 0 : 1);
        }

        private delegate nint TradeRequestDelegate(InventoryManager* manager, uint entityID);

        private delegate nint TradeStatusUpdateDelegate(InventoryManager* manager, nint entityID, nint a3);
    }
}
