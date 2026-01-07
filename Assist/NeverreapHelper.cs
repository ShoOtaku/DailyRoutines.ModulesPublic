using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DailyRoutines.ModulesPublic;

public class NeverreapHelper : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("NeverreapHelperTitle"),
        Description = GetLoc("NeverreapHelperDescription"),
        Category    = ModuleCategories.Assist
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true, AllDefaultEnabled = true };

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeoutMS = 30_000 };
        
        DService.ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OnlyValidWhenSolo"), ref ModuleConfig.ValidWhenSolo))
            SaveConfig(ModuleConfig);
    }

    private unsafe void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();
        
        if (GameState.TerritoryType != 420) return;
        
        TaskHelper.Enqueue(() =>
        {
            if (DService.ObjectTable.LocalPlayer is not { } localPlayer) return false;
            if (BetweenAreas || !UIModule.IsScreenReady()) return false;
            if (ModuleConfig.ValidWhenSolo && (DService.PartyList.Length > 1 || PlayersManager.PlayersAroundCount > 0))
            {
                TaskHelper.Abort();
                return true;
            }
            if (!EventFramework.Instance()->IsEventIDNearby(1638407)) return false;

            new EventStartPackt(localPlayer.EntityID, 1638407).Send();
            return true;
        });
    }

    protected override void Uninit() => 
        DService.ClientState.TerritoryChanged -= OnZoneChanged;

    private class Config : ModuleConfiguration
    {
        public bool ValidWhenSolo = true;
    }
}
