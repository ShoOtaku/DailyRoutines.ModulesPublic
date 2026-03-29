using System.Runtime.InteropServices;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Interop.Game.Models;
using OmenTools.Interop.Game.Models.Packets.Upstream;
using OmenTools.OmenService;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace DailyRoutines.ModulesPublic;

public unsafe class CompanyCreditExchangeMore : ModuleBase
{
    private static readonly CompSig AddonFreeCompanyCreditShopRefreshSig = new("41 56 41 57 48 83 EC ?? 0F B6 81 ?? ?? ?? ?? 4D 8B F8");
    private static          Hook<AddonFreeCompanyCreditShopRefreshDelegate> AddonFreeCompanyCreditShopRefreshHook;

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("CompanyCreditExchangeMoreTitle"),
        Description = Lang.Get("CompanyCreditExchangeMoreDescription"),
        Category    = ModuleCategory.System
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        AddonFreeCompanyCreditShopRefreshHook = AddonFreeCompanyCreditShopRefreshSig.GetHook<AddonFreeCompanyCreditShopRefreshDelegate>(AddonRefreshDetour);
        AddonFreeCompanyCreditShopRefreshHook.Enable();

        GamePacketManager.Instance().RegPreSendPacket(OnPreSendPacket);
    }

    private static bool AddonRefreshDetour(AtkUnitBase* addon, uint atkValueCount, AtkValue* atkValues)
    {
        if (addon == null) return false;

        var orig = AddonFreeCompanyCreditShopRefreshHook.Original(addon, atkValueCount, atkValues);

        if (!ModuleConfig.OnlyActiveInWorkshop || HousingManager.Instance()->WorkshopTerritory != null)
        {
            for (var i = 110; i < 130; i++)
            {
                if (addon->AtkValues[i].Type != ValueType.Int) continue;
                addon->AtkValues[i].Int = 255;
            }
        }

        return orig;
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("CompanyCreditExchangeMore-OnlyActiveInWorkshop"), ref ModuleConfig.OnlyActiveInWorkshop))
            ModuleConfig.Save(this);
    }

    private static void OnPreSendPacket(ref bool isPrevented, int opcode, ref nint packet, ref bool isPrioritize)
    {
        if (opcode != UpstreamOpcode.HandOverItemOpcode) return;
        if (ModuleConfig.OnlyActiveInWorkshop && HousingManager.Instance()->WorkshopTerritory == null) return;
        if (FreeCompanyCreditShop == null) return;

        var data = (HandOverItemPacket*)packet;
        if (data->Param0 < 99) return;

        data->Param0 = 255;
    }

    protected override void Uninit() =>
        GamePacketManager.Instance().Unreg(OnPreSendPacket);

    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool AddonFreeCompanyCreditShopRefreshDelegate(AtkUnitBase* addon, uint atkValueCount, AtkValue* atkValues);

    private class Config : ModuleConfig
    {
        public bool OnlyActiveInWorkshop = true;
    }
}
