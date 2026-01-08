using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public unsafe class ThePraetoriumHelper : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("ThePraetoriumHelperTitle"),
        Description = GetLoc("ThePraetoriumHelperDescription"),
        Category    = ModuleCategories.Assist,
        Author      = ["逆光"]
    };

    protected override void Init()
    {
        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    private static void OnZoneChanged(ushort zoneID)
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        if (GameState.TerritoryType != 1044) return;
        
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 1000);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (!Throttler.Throttle("ThePraetoriumHelper-OnUpdate", 1_000)) return;
        if (GameState.TerritoryType != 1044)
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }
        
        if (!DService.Instance().Condition[ConditionFlag.Mounted] || DService.Instance().ObjectTable.LocalPlayer == null ||
            ActionManager.Instance()->GetActionStatus(ActionType.Action, 1128)             != 0)
            return;

        var target = GetMostCanTargetObjects();
        if (target == null) return;
        
        UseActionManager.Instance().UseActionLocation(ActionType.Action, 1128, location: target.Position);
    }

    private static IGameObject? GetMostCanTargetObjects()
    {
        var allTargets = DService.Instance().ObjectTable.Where(o => o.IsTargetable && ActionManager.CanUseActionOnTarget(7, o.ToStruct())).ToList();
        if (allTargets.Count <= 0) return null;

        IGameObject? preObjects = null;
        var preObjectsAoECount = 0;
        foreach (var b in allTargets)
        {
            if (Vector3.DistanceSquared(DService.Instance().ObjectTable.LocalPlayer.Position, b.Position) - b.HitboxRadius > 900) continue;
            
            var aoeCount = GetTargetAoECount(b, allTargets);
            if (aoeCount > preObjectsAoECount)
            {
                preObjectsAoECount = aoeCount;
                preObjects = b;
            }
        }
        
        return preObjects;
    }
    private static int GetTargetAoECount(IGameObject target, IEnumerable<IGameObject> AllTarget)
    {
        var count = 0;
        foreach (var b in AllTarget)
        {
            if (Vector3.DistanceSquared(target.Position, b.Position) - b.HitboxRadius <= 36)
                count++;
        }
        
        return count;
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        FrameworkManager.Instance().Unreg(OnUpdate);
    }
}
