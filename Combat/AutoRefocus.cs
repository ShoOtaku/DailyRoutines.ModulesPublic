using System.Collections.Generic;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace DailyRoutines.ModulesPublic;

public class AutoRefocus : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRefocusTitle"),
        Description = GetLoc("AutoRefocusDescription"),
        Category    = ModuleCategories.Combat,
    };

    private static ulong FocusTarget = 0xE000_0000;

    protected override void Init()
    {
        FocusTarget = 0xE000_0000;
        
        TargetManager.RegPostSetFocusTarget(OnSetFocusTarget);
        DService.ClientState.TerritoryChanged += OnZoneChange;
        PlayersManager.ReceivePlayersAround   += OnReceivePlayerAround;
    }

    private static void OnReceivePlayerAround(IReadOnlyList<IPlayerCharacter> characters)
    {
        if (GameState.ContentFinderCondition == 0 || FocusTarget == 0xE000_0000 || TargetManager.FocusTarget != null) return;
        TargetManager.SetFocusTargetCallDetour(FocusTarget);
    }

    private static void OnSetFocusTarget(GameObjectId gameObjectID) => 
        FocusTarget = gameObjectID;

    private static void OnZoneChange(ushort zone) =>
        FocusTarget = 0xE000_0000;

    protected override void Uninit()
    {
        PlayersManager.ReceivePlayersAround -= OnReceivePlayerAround;
        DService.ClientState.TerritoryChanged -= OnZoneChange;
        TargetManager.Unreg(OnSetFocusTarget);
    }
}
