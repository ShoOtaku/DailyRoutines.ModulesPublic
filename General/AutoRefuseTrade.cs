using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools.Dalamud.Helpers;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using OmenTools.Threading;
using AgentShowDelegate = OmenTools.Interop.Game.Models.Native.AgentShowDelegate;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoRefuseTrade : ModuleBase
{
    private static Hook<AgentShowDelegate>? AgentTradeShowHook;

    private static readonly CompSig                     TradeRequestSig = new("48 89 6C 24 ?? 56 57 41 56 48 83 EC ?? 48 8B E9 44 8B F2");
    private static          Hook<TradeRequestDelegate>? TradeRequestHook;

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoRefuseTradeTitle"),
        Description = Lang.Get("AutoRefuseTradeDescription"),
        Category    = ModuleCategory.General
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        AgentTradeShowHook ??= DService.Instance().Hook.HookFromAddress<AgentShowDelegate>
        (
            AgentModule.Instance()->GetAgentByInternalId(AgentId.Trade)->VirtualTable->GetVFuncByName("Show"),
            AgentTradeShowDetour
        );
        AgentTradeShowHook.Enable();

        TradeRequestHook ??= DService.Instance().Hook.HookFromAddress<TradeRequestDelegate>
        (
            DalamudReflector.GetMemberFuncByName(typeof(InventoryManager.MemberFunctionPointers), "SendTradeRequest"),
            TradeRequestDetour
        );
        TradeRequestHook.Enable();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("SendChat"), ref ModuleConfig.SendChat))
            ModuleConfig.Save(this);

        ImGui.SameLine();
        if (ImGui.Checkbox(Lang.Get("SendNotification"), ref ModuleConfig.SendNotification))
            ModuleConfig.Save(this);

        ImGui.TextUnformatted(Lang.Get("AutoRefuseTrade-ExtraCommands"));
        ImGui.InputTextMultiline("###ExtraCommandsInput", ref ModuleConfig.ExtraCommands, 1024, ScaledVector2(300f, 120f));
        ImGuiOm.TooltipHover(ModuleConfig.ExtraCommands);

        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);
    }

    private static int TradeRequestDetour(InventoryManager* instance, uint entityID)
    {
        Throttler.Shared.Throttle("AutoRefuseTrade-Show", 3_000, true);
        return TradeRequestHook.Original(instance, entityID);
    }

    private static void AgentTradeShowDetour(AgentInterface* agent)
    {
        // 没有 Block => 五秒内没有发起交易的请求
        if (Throttler.Shared.Check("AutoRefuseTrade-Show"))
        {
            InventoryManager.Instance()->RefuseTrade();
            NotifyTradeCancel();
            return;
        }

        AgentTradeShowHook.Original(agent);
    }

    private static void NotifyTradeCancel()
    {
        var message = Lang.Get("AutoRefuseTrade-Notification");

        if (ModuleConfig.SendNotification)
        {
            NotifyHelper.NotificationInfo(message);
            NotifyHelper.Speak(message);
        }

        if (ModuleConfig.SendChat)
            NotifyHelper.Chat($"{message}\n    ({Lang.Get("Time")}: {StandardTimeManager.Instance().Now.ToShortTimeString()})");

        if (!string.IsNullOrWhiteSpace(ModuleConfig.ExtraCommands))
        {
            foreach (var command in ModuleConfig.ExtraCommands.Split('\n'))
                ChatManager.Instance().SendMessage(command);
        }
    }

    private delegate int TradeRequestDelegate(InventoryManager* instance, uint entityID);

    private class Config : ModuleConfig
    {
        public string ExtraCommands    = string.Empty;
        public bool   SendChat         = true;
        public bool   SendNotification = true;
    }
}
