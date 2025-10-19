using System.Collections.Generic;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoRequestItemSubmit : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRequestItemSubmitTitle"),
        Description = GetLoc("AutoRequestItemSubmitDescription"),
        Category    = ModuleCategories.UIOperation,
    };
    
    private static readonly HashSet<string> HQItemTexts =
    [
        LuminaWrapper.GetAddonText(5450),
        LuminaWrapper.GetAddonText(11514),
        LuminaWrapper.GetAddonText(102434)
    ];

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "Request", OnAddonRequest);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostDraw,    "Request", OnAddonRequest);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Request", OnAddonRequest);
    }

    protected override void ConfigUI()
    {
        ConflictKeyText();
        
        if (ImGui.Checkbox(GetLoc("AutoRequestItemSubmit-SubmitHQItem"), ref ModuleConfig.IsSubmitHQItem))
            SaveConfig(ModuleConfig);
    }

    private static void OnAddonRequest(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PostSetup:
                DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnAddonSelectYesno);
                break;
            case AddonEvent.PostDraw:
                OperateOnRequest();
                break;
            case AddonEvent.PreFinalize:
                DService.AddonLifecycle.UnregisterListener(OnAddonSelectYesno);
                break;
        }
    }
    
    private static void OnAddonSelectYesno(AddonEvent type, AddonArgs args)
    {
        if (!ModuleConfig.IsSubmitHQItem) return;

        var text = ((AddonSelectYesno*)SelectYesno)->PromptText->NodeText.ExtractText();
        if (!HQItemTexts.Contains(text)) return;
        
        ClickSelectYesnoYes();
    }
    
    private static void OperateOnRequest()
    {
        if (IsConflictKeyPressed())
            return;
        
        var addon = (AddonRequest*)Request;
        if (addon == null) return;
        
        if (addon->HandOverButton->IsEnabled)
        {
            addon->HandOverButton->ClickAddonButton(Request);
            return;
        }
        
        var agent = AgentNpcTrade.Instance();
        if (agent == null) return;

        var manager = InventoryManager.Instance();
        if (manager == null) return;
        
        var container = manager->GetInventoryContainer(InventoryType.HandIn);
        if (container == null) return;

        var requestState = UIState.Instance()->NpcTrade.Requests;
        for (var i = 0; i < requestState.Count; i++)
        {
            var slotState   = container->GetInventorySlot(i);
            var itemRequest = requestState.Items[i];
            if (slotState->ItemId == itemRequest.ItemId) continue;

            // 数据没好, 先请求加载
            if (!IsAddonAndNodesReady(ContextIconMenu))
            {
                SendEvent(AgentId.NpcTrade, 0, 2, i, 0, 0);
                return;
            }
            
            var firstItem = agent->SelectedTurnInSlotItemOptionValues[0].Value;
            if (firstItem == null || firstItem->ItemId == 0) return;
            
            SendEvent(AgentId.NpcTrade, 1, 0, 0, firstItem->GetItemId(), 0U, 0);
            return;
        }
    }

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddonRequest);
        DService.AddonLifecycle.UnregisterListener(OnAddonSelectYesno);
    }

    private class Config : ModuleConfiguration
    {
        public bool IsSubmitHQItem = true;
    }
}
