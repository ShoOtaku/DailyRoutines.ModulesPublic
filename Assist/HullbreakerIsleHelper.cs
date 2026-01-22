using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.ModulesPublic;

public class HullbreakerIsleHelper : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("HullbreakerIsleHelperTitle"),
        Description = GetLoc("HullbreakerIsleHelperDescription"),
        Category    = ModuleCategories.Assist
    };

    private static readonly HashSet<uint> FakeTreasuresID = [2004074, 2004075, 2004076, 2004077, 2004078, 2004079];

    private static List<Vector3> TrapPositions         = [];
    private static List<Vector3> FakeTreasurePositions = [];

    protected override void Init()
    {
        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }
    
    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;

        WindowManager.Draw -= OnDraw;
        FrameworkManager.Instance().Unreg(OnUpdate);
        
        TrapPositions.Clear();
        FakeTreasurePositions.Clear();
    }

    private static void OnZoneChanged(ushort zone)
    {
        WindowManager.Draw -= OnDraw;
        FrameworkManager.Instance().Unreg(OnUpdate);
        TrapPositions.Clear();
        FakeTreasurePositions.Clear();

        if (GameState.TerritoryType != 361) return;

        FrameworkManager.Instance().Reg(OnUpdate, 2_000);
        WindowManager.Draw += OnDraw;
    }

    private static void OnDraw()
    {
        var list = ImGui.GetBackgroundDrawList();

        foreach (var trap in TrapPositions)
        {
            if (!DService.Instance().GameGUI.WorldToScreen(trap, out var screenPos)) continue;
            list.AddText(screenPos, KnownColor.Yellow.ToUInt(), LuminaWrapper.GetEObjName(2000947));
        }

        foreach (var fakeTreasure in FakeTreasurePositions)
        {
            if (!DService.Instance().GameGUI.WorldToScreen(fakeTreasure, out var screenPos)) continue;
            list.AddText(screenPos, KnownColor.Yellow.ToUInt(), LuminaWrapper.GetBNPCName(2896));
        }
    }

    private static unsafe void OnUpdate(IFramework _)
    {
        List<Vector3> trapCollect         = [];
        List<Vector3> fakeTreasureCollect = [];

        // 捕兽夹
        foreach (var trap in DService.Instance().ObjectTable.SearchObjects
                 (
                     x => x.ObjectKind == ObjectKind.BattleNpc &&
                          x is IBattleNPC { NameID: 2891, TargetableStatus: (ObjectTargetableFlags)248 },
                     IObjectTable.CharactersRange
                 ))
        {
            trapCollect.Add(trap.Position);
            trap.ToStruct()->Highlight(ObjectHighlightColor.Yellow);
        }

        // 怪宝箱
        foreach (var treasure in DService.Instance().ObjectTable.SearchObjects
                 (
                     x => x.ObjectKind == ObjectKind.EventObj && FakeTreasuresID.Contains(x.DataID) && x.IsTargetable,
                     IObjectTable.EventRange
                 ))
        {
            fakeTreasureCollect.Add(treasure.Position);
            treasure.ToStruct()->Highlight(ObjectHighlightColor.Yellow);
        }

        TrapPositions         = trapCollect;
        FakeTreasurePositions = fakeTreasureCollect;
    }
}
