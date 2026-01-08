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
        
        TargetManager.Instance().RegPostSetFocusTarget(OnSetFocusTarget);
        DService.Instance().ClientState.TerritoryChanged += OnZoneChange;
        PlayersManager.ReceivePlayersAround   += OnReceivePlayerAround;
    }

    private static unsafe void OnReceivePlayerAround(IReadOnlyList<IPlayerCharacter> characters)
    {
        if (GameState.ContentFinderCondition == 0 || FocusTarget == 0xE000_0000 || TargetManager.FocusTarget != null) return;
        TargetManager.ToStruct()->SetFocusTargetByObjectId(FocusTarget);
    }

    private static void OnSetFocusTarget(GameObjectId gameObjectID) => 
        FocusTarget = gameObjectID;

    private static void OnZoneChange(ushort zone) =>
        FocusTarget = 0xE000_0000;

    protected override void Uninit()
    {
        PlayersManager.ReceivePlayersAround -= OnReceivePlayerAround;
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChange;
        TargetManager.Instance().Unreg(OnSetFocusTarget);
    }
}
