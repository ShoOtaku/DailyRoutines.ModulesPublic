using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.ModulesPublic;

public class AutoCuffACur : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCuffACurTitle"),
        Description = GetLoc("AutoCuffACurDescription"),
        Category    = ModuleCategories.GoldSaucer,
    };

    protected override void Init()
    {
        TaskHelper ??= new();
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "PunchingMachine", OnAddonSetup);
    }

    protected override void ConfigUI()
    {
        ConflictKeyText();
        
        ImGui.NewLine();
        
        using (ImRaii.Disabled(GameState.TerritoryType != 144 || TaskHelper.IsBusy || OccupiedInEvent))
        {
            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Play, GetLoc("Start")))
                EnqueueNewRound();
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Stop, GetLoc("Stop")))
        {
            TaskHelper.Abort();
            new EventCompletePackt(2359300, 14).Send();
        }
    }

    private unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (InterruptByConflictKey(TaskHelper, this)) return;

        var currentMGP = 0;
        
        TaskHelper.Abort();
        TaskHelper.Enqueue(WaitSelectStringAddon);
        TaskHelper.Enqueue(() =>
        {
            UpdateSelectStringInfo(GetLoc("AutoCuffACur-StartingRound"));
            
            currentMGP = InventoryManager.Instance()->GetInventoryItemCount(29);
            new EventActionPacket(2359300, 17235982).Send();
        });
        TaskHelper.Enqueue(() => InventoryManager.Instance()->GetInventoryItemCount(29) != currentMGP);
        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(() =>
        {
            new EventActionPacket(2359300, 17301518, 3).Send();
            UpdateSelectStringInfo($"{GetLoc("AutoCuffACur-WaitingForResult")}......");
        });
        TaskHelper.DelayNext(3000);
        TaskHelper.Enqueue(() => new EventCompletePackt(2359300, 14).Send());
        TaskHelper.Enqueue(EnqueueNewRound);
    }

    private static unsafe bool? WaitSelectStringAddon() =>
        IsAddonAndNodesReady(SelectString) && IsAddonAndNodesReady(PunchingMachine);

    private bool? EnqueueNewRound()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        if (OccupiedInEvent) return false;
        
        new EventStartPackt(LocalPlayerState.EntityID, 2359300).Send();
        return true;
    }
    
    private static unsafe void UpdateSelectStringInfo(string info)
    {
        if (!IsAddonAndNodesReady(SelectString) || !IsAddonAndNodesReady(PunchingMachine)) return;

        var list = SelectString->GetComponentListById(3);
        var text = SelectString->GetTextNodeById(2);
        if (list == null || text == null) return;
        
        list->OwnerNode->ToggleVisibility(false);
        list->SetEnabledState(false);

        text->FontSize      = 18;
        text->AlignmentType = AlignmentType.Center;

        var builder = new SeStringBuilder();
        builder.AddUiForeground(28);
        builder.AddText($"[{GetLoc("AutoCuffACurTitle")}]");
        builder.AddUiForegroundOff();
        builder.Add(NewLinePayload.Payload);
        builder.AddText(info);
        
        text->SetText(builder.Encode());
        text->SetPositionFloat(20, 60);
    }

    protected override unsafe void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddonSetup);
        
        if (IsAddonAndNodesReady(PunchingMachine))
            new EventCompletePackt(2359300, 14).Send();
    }
}
