using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace DailyRoutines.ModulesPublic;

public unsafe class SelectableRecruitmentText : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title           = GetLoc("SelectableRecruitmentTextTitle"),
        Description     = GetLoc("SelectableRecruitmentTextDescription"),
        Category        = ModuleCategories.Recruitment,
        PreviewImageURL = ["https://gh.atmoomen.top/raw.githubusercontent.com/AtmoOmen/StaticAssets/main/DailyRoutines/image/SelectableRecruitmentText-UI.png"]
    };
    
    private static TextMultiLineInputNode? RecruitmentTextNode;
    
    protected override void Init()
    {
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreRefresh, "LookingForGroupDetail", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LookingForGroupDetail", OnAddon);
    }
    
    private void OnAddon(AddonEvent type, AddonArgs? args)
    {
        switch (type)
        {
            case AddonEvent.PreFinalize:
                RecruitmentTextNode?.Dispose();
                RecruitmentTextNode = null;
                
                break;
            
            case AddonEvent.PreDraw:
                if (LookingForGroupDetail == null) return;

                var origText = LookingForGroupDetail->GetTextNodeById(20);
                if (origText == null) return;
                
                if (RecruitmentTextNode != null)
                {
                    if (!RecruitmentTextNode.IsFocused && string.IsNullOrEmpty(RecruitmentTextNode.String))
                        RecruitmentTextNode.SeString = new ReadOnlySeStringSpan(AgentLookingForGroup.Instance()->LastViewedListing.Comment).PraseAutoTranslate();

                    return;
                }

                var origButton = LookingForGroupDetail->GetComponentButtonById(18);
                if (origButton == null) return;

                var textNodeContainer = LookingForGroupDetail->GetNodeById(19);
                if (textNodeContainer == null) return;

                origButton->OwnerNode->ToggleVisibility(false);
                origText->ToggleVisibility(false);
                textNodeContainer->ToggleVisibility(false);

                RecruitmentTextNode = new()
                {
                    AutoUpdateHeight = false,
                    Size             = new(520, 60),
                    Position         = new Vector2(origButton->OwnerNode->X, origButton->OwnerNode->Y) - new Vector2(10, 8),
                    ShowLimitText    = false,
                };
                RecruitmentTextNode.TextLimitsNode.DetachNode();
                RecruitmentTextNode.AttachNode(LookingForGroupDetail);

                break;
        }
    }

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        OnAddon(AddonEvent.PreFinalize, null);
    }
}
