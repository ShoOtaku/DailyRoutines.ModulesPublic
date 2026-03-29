using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class NoAutoClosePartyFinder : ModuleBase
{
    private static readonly CompSig                            LookingForGroupHideSig = new("48 89 5C 24 ?? 57 48 83 EC 20 83 A1 ?? ?? ?? ?? ??");
    private static          Hook<LookingForGroupHideDelegate>? LookingForGroupHideHook;

    private static DateTime LastPartyMemberChangeTime;
    private static DateTime LastViewTime;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("NoAutoClosePartyFinderTitle"),
        Description = Lang.Get("NoAutoClosePartyFinderDescription"),
        Category    = ModuleCategory.Recruitment,
        Author      = ["Nyy", "YLCHEN"]
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init()
    {
        LookingForGroupHideHook = LookingForGroupHideSig.GetHook<LookingForGroupHideDelegate>(LookingForGroupHideDetour);
        LookingForGroupHideHook.Enable();

        LogMessageManager.Instance().RegPre(OnPreReceiveMessage);
    }

    private static void OnPreReceiveMessage(ref bool isPrevented, ref uint logMessageID, ref LogMessageQueueItem values)
    {
        if (logMessageID != 947) return;

        isPrevented = true;

        LastPartyMemberChangeTime = StandardTimeManager.Instance().UTCNow.AddSeconds(1);
        if (LookingForGroupDetail->IsAddonAndNodesReady())
            LastViewTime = StandardTimeManager.Instance().UTCNow.AddSeconds(1);
    }

    private static void LookingForGroupHideDetour(AgentLookingForGroup* agent)
    {
        if (StandardTimeManager.Instance().UTCNow < LastPartyMemberChangeTime)
        {
            if (StandardTimeManager.Instance().UTCNow < LastViewTime)
            {
                if (LookingForGroupDetail->IsAddonAndNodesReady())
                    LookingForGroupDetail->Close(true);

                DService.Instance().Framework.RunOnTick(() => agent->OpenListing(agent->LastViewedListing.ListingId), TimeSpan.FromMilliseconds(100));
            }

            return;
        }

        LookingForGroupHideHook.Original(agent);
    }

    protected override void Uninit() =>
        LogMessageManager.Instance().Unreg(OnPreReceiveMessage);

    private delegate void LookingForGroupHideDelegate(AgentLookingForGroup* agent);
}
