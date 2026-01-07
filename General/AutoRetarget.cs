using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoRetarget : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRetargetTitle"),
        Description = GetLoc("AutoRetargetDescription"),
        Category    = ModuleCategories.General,
        Author      = ["KirisameVanilla"],
    };
    
    private static Config ModuleConfig = null!;
    
    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeoutMS = 15_000 };
        
        FrameworkManager.Reg(OnUpdate, true, 1000);
    }

    protected override void Uninit() => 
        FrameworkManager.Unreg(OnUpdate);
    
    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoRetarget-PrioritizeForlorn"), ref ModuleConfig.PrioritizeForlorn)) 
            ModuleConfig.Save(this);

        ImGui.InputText(GetLoc("Target"), ref ModuleConfig.DisplayName, 64);
        if (ImGui.Button(GetLoc("AutoRetarget-SetToTarget")) && TargetManager.Target is { } target)
        {
            ModuleConfig.DisplayName = TargetManager.Target is IPlayerCharacter player
                                           ? $"{player.Name}@{player.HomeWorld.Value.Name}"
                                           : $"{target.Name}";
            ModuleConfig.Save(this);
        }
        
        ImGui.SameLine();
        if (ImGui.Button(GetLoc("Clear")))
        {
            ModuleConfig.DisplayName = GetLoc("None");
            ModuleConfig.Save(this);
        }

        if (ImGui.Checkbox(GetLoc("AutoRetarget-UseMarkerTrack"), ref ModuleConfig.MarkerTrack) && !ModuleConfig.MarkerTrack)
            ClearMarkers();
    }

    private void OnUpdate(IFramework framework)
    {
        if (ModuleConfig.DisplayName == GetLoc("None") && !ModuleConfig.PrioritizeForlorn)
        {
            ClearMarkers();
            return;
        }

        List<IGameObject> found = [];
        foreach (var igo in DService.ObjectTable)
        {
            var objName = igo is IPlayerCharacter ipc
                              ? $"{igo.Name}@{ipc.HomeWorld.ValueNullable?.Name}"
                              : igo.Name.ToString();

            if (ModuleConfig.PrioritizeForlorn && igo is IBattleNPC ibn && (ibn.NameID == 6737 || ibn.NameID == 6738))
            {
                found.Insert(0, igo);
                break;
            }

            if (objName != ModuleConfig.DisplayName) continue;
            found.Add(igo);
        }

        if (found.Count != 0)
        {
            var igo = found.First();
            if (igo is IBattleNPC ibn && (ibn.NameID == 6737 || ibn.NameID == 6738)) 
                TargetManager.Target = igo;
            else 
                TargetManager.Target ??= igo;

            if (ModuleConfig.MarkerTrack)
                EnqueuePlaceFieldMarkers(igo.Position);
        }
    }

    private void EnqueuePlaceFieldMarkers(Vector3 targetPos)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(() =>
        {
            var flagPos  = new Vector2(targetPos.X, targetPos.Z);
            var currentY = targetPos.Y;
            var counter  = 0;

            foreach (var fieldMarkerPoint in Enum.GetValues<FieldMarkerPoint>())
            {
                MarkingController.Instance()->PlaceFieldMarkerLocal(fieldMarkerPoint, flagPos.ToVector3(currentY - 2 + (counter * 5)));
                counter++;
            }
        }, name: "放置标点");
    }

    private static void ClearMarkers()
    {
        var instance = MarkingController.Instance();
        if (instance == null) return;

        var array = instance->FieldMarkers.ToArray();
        if (array.Count(x => x.Active) != 8) return;
        if (array.Select(x => x.Position.ToVector2()).ToHashSet().Count == 1)
            Enumerable.Range(0, 8).ForEach(x => MarkingController.Instance()->ClearFieldMarkerLocal((FieldMarkerPoint)x));
    }
    
    private class Config : ModuleConfiguration
    {
        public bool   MarkerTrack;
        public string DisplayName = GetLoc("None");
        public bool   PrioritizeForlorn;
    }
}
