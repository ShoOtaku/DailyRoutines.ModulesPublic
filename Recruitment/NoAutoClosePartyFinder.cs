using System;
using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace DailyRoutines.ModulesPublic;

public unsafe class NoAutoClosePartyFinder : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("NoAutoClosePartyFinderTitle"),
        Description = GetLoc("NoAutoClosePartyFinderDescription"),
        Category    = ModuleCategories.Recruitment,
        Author      = ["Nyy", "YLCHEN"]
    };
    
    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private delegate        void                               LookingForGroupHideDelegate(AgentLookingForGroup* agent);
    private static readonly CompSig                            LookingForGroupHideSig = new("48 89 5C 24 ?? 57 48 83 EC 20 83 A1 ?? ?? ?? ?? ??");
    private static          Hook<LookingForGroupHideDelegate>? LookingForGroupHideHook;

    private static DateTime LastPartyMemberChangeTime;
    private static DateTime LastViewTime;

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
        
        LastPartyMemberChangeTime = DateTime.UtcNow.AddSeconds(1);
        if (LookingForGroupDetail->IsAddonAndNodesReady())
            LastViewTime = DateTime.UtcNow.AddSeconds(1);
    }

    private static void LookingForGroupHideDetour(AgentLookingForGroup* agent)
    {
        if (DateTime.UtcNow < LastPartyMemberChangeTime)
        {
            if (DateTime.UtcNow < LastViewTime)
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
}
