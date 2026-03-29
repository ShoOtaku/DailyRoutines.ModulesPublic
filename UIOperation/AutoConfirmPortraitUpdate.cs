using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public class AutoConfirmPortraitUpdate : ModuleBase
{
    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoConfirmPortraitUpdateTitle"),
        Description = Lang.Get("AutoConfirmPortraitUpdateDescription"),
        Category    = ModuleCategory.UIOperation
    };

    protected override unsafe void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "BannerPreview", OnAddon);
        if (BannerPreview != null)
            OnAddon(AddonEvent.PostSetup, null);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("SendNotification"), ref ModuleConfig.SendNotification))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox(Lang.Get("SendChat"), ref ModuleConfig.SendChat))
            ModuleConfig.Save(this);
    }

    private static unsafe void OnAddon(AddonEvent type, AddonArgs? args)
    {
        BannerPreview->Callback(0);

        if (ModuleConfig.SendNotification)
            NotifyHelper.NotificationSuccess(Lang.Get("AutoConfirmPortraitUpdate-Notification"));
        if (ModuleConfig.SendChat)
            NotifyHelper.Chat(Lang.Get("AutoConfirmPortraitUpdate-Notification"));
    }

    protected override void Uninit() => DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);

    private class Config : ModuleConfig
    {
        public bool SendChat         = true;
        public bool SendNotification = true;
    }
}
