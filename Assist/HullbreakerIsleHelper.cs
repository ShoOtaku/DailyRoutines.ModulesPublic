using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
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

    private static readonly HashSet<string> TrapNames;
    private static readonly HashSet<string> FakeTreasureNames;

    private static HashSet<Vector3> TrapPositions         = [];
    private static HashSet<Vector3> FakeTreasurePositions = [];

    static HullbreakerIsleHelper()
    {
        TrapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // 捕兽夹
            LuminaGetter.GetRow<EObjName>(2000947)!.Value.Singular.ToString(),
            LuminaGetter.GetRow<EObjName>(2000947)!.Value.Plural.ToString(),
        };

        FakeTreasureNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // 宝箱
            LuminaGetter.GetRow<EObjName>(2002491)!.Value.Singular.ToString(),
            LuminaGetter.GetRow<EObjName>(2002491)!.Value.Plural.ToString()
        };
    }

    protected override void Init()
    {
        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    private static void OnZoneChanged(ushort zone)
    {
        WindowManager.Draw -= OnDraw;
        FrameworkManager.Instance().Unreg(OnUpdate);
        TrapPositions.Clear();
        FakeTreasurePositions.Clear();
        
        if (GameState.TerritoryType != 361) return;
        
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 2000);
        WindowManager.Draw += OnDraw;
    }

    private static void OnDraw()
    {
        foreach (var trap in TrapPositions)
        {
            if (!DService.Instance().Gui.WorldToScreen(trap, out var screenPos)) continue;
            ImGui.GetBackgroundDrawList().AddText(screenPos, KnownColor.Yellow.ToVector4().ToUInt(), TrapNames.First());
        }
        
        foreach (var fakeTreasure in FakeTreasurePositions)
        {
            if (!DService.Instance().Gui.WorldToScreen(fakeTreasure, out var screenPos)) continue;
            ImGui.GetBackgroundDrawList().AddText(screenPos, KnownColor.Yellow.ToVector4().ToUInt(), FakeTreasureNames.First());
        }
    }

    private static unsafe void OnUpdate(IFramework _)
    {
        HashSet<Vector3> trapCollect = [];
        HashSet<Vector3> fakeTreasureCollect = [];
        
        foreach (var obj in DService.Instance().ObjectTable)
        {
            switch (obj.ObjectKind)
            {
                // 捕兽夹
                case ObjectKind.BattleNpc:
                    if (!TrapNames.Contains(obj.Name.ToString())) continue;
                    trapCollect.Add(obj.Position);
                    obj.ToStruct()->Highlight(ObjectHighlightColor.Yellow);
                    break;
                case ObjectKind.EventObj:
                    if (!FakeTreasureNames.Contains(obj.Name.ToString()) || 
                        !obj.IsTargetable) continue;
                    fakeTreasureCollect.Add(obj.Position);
                    obj.ToStruct()->Highlight(ObjectHighlightColor.Yellow);
                    break;
            }
        }
        
        TrapPositions = trapCollect;
        FakeTreasurePositions = fakeTreasureCollect;
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        OnZoneChanged(0);
    }
}
