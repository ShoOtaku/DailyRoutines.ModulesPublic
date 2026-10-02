using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading;

namespace DailyRoutines.ModulesPublic.Duty;

public unsafe class AutoCheckItemLevel : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoCheckItemLevelTitle"),
        Description = Lang.Get("AutoCheckItemLevelDescription"),
        Category    = ModuleCategory.Duty
    };

    protected override void Init()
    {
        TaskHelper ??= new() { TimeoutMS = 20_000 };

        IClientState.Instance().TerritoryChanged += OnZoneChanged;
    }

    protected override void Uninit() =>
        IClientState.Instance().TerritoryChanged -= OnZoneChanged;

    private void OnZoneChanged
    (
        uint zone
    )
    {
        TaskHelper.Abort();

        if (GameState.IsInPVPArea                                                                                  ||
            GameState.ContentFinderCondition == 0                                                                  ||
            GameState.ContentFinderConditionData.PvP                                                               ||
            !ValidContentJobCategories.Contains(GameState.ContentFinderConditionData.AcceptClassJobCategory.RowId) ||
            GameState.ContentFinderConditionData.ContentMemberType.Value.MeleesPerParty == 0                       ||
            ICondition.Instance()[ConditionFlag.DutyRecorderPlayback])
            return;

        TaskHelper.Enqueue
        (
            () => !ICondition.Instance().IsBetweenAreas &&
                  IObjectTable.Instance().LocalPlayer != null,
            "等待进入副本"
        );

        TaskHelper.Enqueue(() => CheckMembersItemLevel([LocalPlayerState.EntityID], []));
    }

    private bool CheckMembersItemLevel
    (
        HashSet<ulong>                                        checkedMembers,
        List<(HudPartyMember Member, uint AvgIL, uint MinIL)> pendingMembers
    )
    {
        var agent        = AgentHUD.Instance();
        var agentInspect = AgentInspect.Instance();

        if (agent == null || agentInspect == null || agent->PartyMemberCount <= 1)
        {
            TaskHelper.Abort();
            return true;
        }

        if (ICondition.Instance().IsBetweenAreas) return false;

        if (CharacterInspect != null)
        {
            CharacterInspect->Close(true);
            return false;
        }

        var members = agent->PartyMembers.ToArray();

        foreach (var member in members)
        {
            if (member.EntityId  == 0                         ||
                member.ContentId == 0                         ||
                member.EntityId  == LocalPlayerState.EntityID ||
                !checkedMembers.Add(member.EntityId))
                continue;

            TaskHelper.Enqueue
            (
                () =>
                {
                    if (CharacterInspect != null && agentInspect->CurrentEntityId == member.EntityId) return true;

                    if (Throttler.Shared.Throttle("AutoCheckItemLevel-OpenExamine"))
                    {
                        if (CharacterInspect != null)
                        {
                            CharacterInspect->Close(true);
                            Throttler.Shared.Throttle("AutoCheckItemLevel-OpenExamine", 10, true);
                        }
                        else
                            agentInspect->ExamineCharacter(member.EntityId);
                    }

                    return false;
                },
                "打开检视界面"
            );

            TaskHelper.Enqueue
            (
                () =>
                {
                    if (member.Object == null) return false;
                    if (!InventoryType.Examine.TryGetItems(_ => true, out var list)) return false;

                    while (list.Count < 13)
                        list.Add(new());

                    uint totalIL        = 0U, lowestIL = 9999U;
                    var  itemSlotAmount = 11;

                    for (var i = 0; i < 13; i++)
                    {
                        var slot   = list[i];
                        var itemID = slot.ItemId;

                        if (!LuminaGetter.TryGetRow(itemID, out Item item)) continue;

                        switch (i)
                        {
                            case 0:
                            {
                                var category = item.ClassJobCategory.RowId;
                                if (HaveOffHandJobCategories.Contains(category))
                                    itemSlotAmount++;

                                break;
                            }
                            case 1 when itemSlotAmount != 12:
                            case 5: // 腰带
                                continue;
                        }

                        if (item.LevelItem.RowId < lowestIL)
                            lowestIL = item.LevelItem.RowId;

                        totalIL += item.LevelItem.RowId;
                    }

                    var avgItemLevel = (uint)(totalIL / itemSlotAmount);

                    pendingMembers.Add((member, avgItemLevel, lowestIL));

                    CharacterInspect->Close(true);
                    agentInspect->FetchCharacterDataStatus = 0;
                    agentInspect->FetchSearchCommentStatus = 0;
                    agentInspect->FetchCharacterDataStatus = 0;

                    return true;
                },
                "检查装等"
            );

            var checkedCount = checkedMembers.Count - 1;
            if (checkedCount != 0 && checkedCount % 3 == 0)
                TaskHelper.DelayNext(1000, "等待 1 秒");

            TaskHelper.Enqueue(() => CheckMembersItemLevel(checkedMembers, pendingMembers), "进入新循环");
            return true;
        }

        SendNotifications(pendingMembers);

        TaskHelper.Abort();
        return true;
    }

    private static void SendNotifications
    (
        List<(HudPartyMember Member, uint AvgIL, uint MinIL)> pendingMembers
    )
    {
        var content   = GameState.ContentFinderConditionData;
        var hasOutput = false;

        using var rented       = new RentedSeStringBuilder();
        using var playerRented = new RentedSeStringBuilder();

        foreach (var (partyMember, avgIL, minIL) in pendingMembers)
        {
            if (partyMember.Object == null)
                continue;

            var isAbnormal = partyMember.Object->Level <= content.ClassJobLevelRequired ||
                             minIL                     <= content.ItemLevelRequired;
            if (!isAbnormal)
                continue;

            playerRented.AppendIcon((uint)LuminaGetter.GetRowOrDefault<ClassJob>(partyMember.Object->ClassJob).ToBitmapFontIcon())
                        .Append
                        (
                            ReadOnlySeString.CreatePlayer
                            (
                                partyMember.Object->NameString,
                                partyMember.Object->HomeWorld
                            )
                        );

            var playerLink = ReadOnlySeString.CreatePlayerLink
            (
                partyMember.Object->NameString,
                partyMember.Object->HomeWorld,
                playerRented.ToReadOnlySeString()
            );
            playerRented.Clear();

            var message = Lang.GetSe
            (
                "AutoCheckItemLevel-Notification-Message",
                new Dictionary<string, object>
                {
                    ["level"]  = partyMember.Object->Level,
                    ["player"] = playerLink,
                    ["minIL"]  = minIL,
                    ["avgIL"]  = avgIL
                }
            );

            if (!hasOutput)
            {
                rented.Append(Lang.Get("AutoCheckItemLevel-Notification-Title"))
                      .AppendNewLine();
                hasOutput = true;
            }
            else
                rented.AppendNewLine();

            rented.Append(message);
        }

        if (hasOutput)
            NotifyHelper.Instance().Chat(rented.ToReadOnlySeString());
    }

    #region 常量

    private static readonly uint[] ValidContentJobCategories = [108, 142, 146];
    private static readonly uint[] HaveOffHandJobCategories  = [2, 7, 8, 20];

    #endregion
}
