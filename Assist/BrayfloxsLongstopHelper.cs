using DailyRoutines.Abstracts;
using DailyRoutines.Managers;

namespace DailyRoutines.ModulesPublic;

public class BrayfloxsLongstopHelper : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("BrayfloxsLongstopHelperTitle"),
        Description = GetLoc("BrayfloxsLongstopHelperDescription"),
        Category    = ModuleCategories.Assist
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeLimitMS = 30_000 };
        
        DService.ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(DService.ClientState.TerritoryType);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("OnlyValidWhenSolo"), ref ModuleConfig.ValidWhenSolo))
            SaveConfig(ModuleConfig);
    }

    private void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();
        
        if (zone != 1041) return;
        
        TaskHelper.Enqueue(() =>
        {
            if (DService.ObjectTable.LocalPlayer is not { } localPlayer) return false;
            if (BetweenAreas || !IsScreenReady()) return false;
            if (ModuleConfig.ValidWhenSolo && (DService.PartyList.Length > 1 || PlayersManager.PlayersAroundCount > 0))
            {
                TaskHelper.Abort();
                return true;
            }
            if (!IsEventIDNearby(1638401)) return false;

            new EventStartPackt(localPlayer.EntityID, 1638401).Send();
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
