using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DailyRoutines.ModulesPublic;

public class AutoMonsterToss : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoMonsterTossTitle"),
        Description = GetLoc("AutoMonsterTossDescription"),
        Category    = ModuleCategories.GoldSaucer,
    };

    protected override void Init()
    {
        TaskHelper ??= new();

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "BasketBall", OnAddonSetup);
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
            new EventCompletePackt(0x240001, 14).Send();
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
            UpdateSelectStringInfo(GetLoc("AutoMonsterToss-StartingGame"));
            
            currentMGP = InventoryManager.Instance()->GetInventoryItemCount(29);
            new EventActionPacket(0x240001, 0x107000E).Send();
        });
        TaskHelper.Enqueue(() => InventoryManager.Instance()->GetInventoryItemCount(29) != currentMGP);
        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(() =>
        {
            new EventActionPacket(0x240001, 0x108000E, 1).Send();
            new EventActionPacket(0x240001, 0x108000E, 1).Send();
            new EventActionPacket(0x240001, 0x108000E, 1).Send();
            new EventActionPacket(0x240001, 0x108000E, 1).Send();
            new EventActionPacket(0x240001, 0x108000E, 1).Send();
        });

        const int maxTime = 25;
        for (var i = 0; i < maxTime; i++)
        {
            var second = i;
            TaskHelper.DelayNext(1000);
            TaskHelper.Enqueue(() => UpdateSelectStringInfo(GetLoc("AutoMonsterToss-WaitingForResult", maxTime - second)));
        }
                
        TaskHelper.Enqueue(() => new EventCompletePackt(0x240001, 14).Send());
        TaskHelper.Enqueue(EnqueueNewRound);
    }
    
    private bool? EnqueueNewRound()
    {
        if (InterruptByConflictKey(TaskHelper, this)) return true;
        if (OccupiedInEvent) return false;
        
        new EventStartPackt(LocalPlayerState.EntityID, 0x240001).Send();
        return true;
    }
    
    private static unsafe bool? WaitSelectStringAddon() =>
        IsAddonAndNodesReady(SelectString) && IsAddonAndNodesReady(BasketBall);
    
    private static unsafe void UpdateSelectStringInfo(string info)
    {
        if (!IsAddonAndNodesReady(SelectString) || !IsAddonAndNodesReady(BasketBall)) return;

        var list = SelectString->GetComponentListById(3);
        var text = SelectString->GetTextNodeById(2);
        if (list == null || text == null) return;
        
        list->OwnerNode->ToggleVisibility(false);
        list->SetEnabledState(false);

        text->FontSize      = 18;
        text->AlignmentType = AlignmentType.Center;

        var builder = new SeStringBuilder();
        builder.AddUiForeground(28);
        builder.AddText($"[{GetLoc("AutoMonsterTossTitle")}]");
        builder.AddUiForegroundOff();
        builder.Add(NewLinePayload.Payload);
        builder.AddText(info);
        
        text->SetText(builder.Encode());
        text->SetPositionFloat(20, 60);
    }

    protected override unsafe void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddonSetup);
        
        if (IsAddonAndNodesReady(BasketBall))
            new EventCompletePackt(0x240001, 14).Send();
    }
}
