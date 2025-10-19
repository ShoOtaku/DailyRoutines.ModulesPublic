using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class InstantPlaceLocationAction : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("InstantPlaceLocationActionTitle"),
        Description = GetLoc("InstantPlaceLocationActionDescription"),
        Category = ModuleCategories.Action,
    };

    // 黑魔纹, 魔纹步, 回退, 回退 (PVP), 螺旋气流, 螺旋气流 (PVP), 星空构想, 胖胖之墙, 逆行 (PVP)
    private static readonly HashSet<uint> InvalidActions = 
    [
        3573, 7419, 24403, 29551, 25837, 29669, 34675, 39215, 41507
    ];

    protected override void Init() => 
        UseActionManager.RegPreUseAction(OnPreUseAction);

    public static void OnPreUseAction(
        ref bool                        isPrevented,
        ref ActionType                  actionType,
        ref uint                        actionID,
        ref ulong                       targetID,
        ref uint                        extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint                        comboRouteID)
    {
        if (actionType != ActionType.Action) return;

        var adjustedAction = ActionManager.Instance()->GetAdjustedActionId(actionID);
        if (InvalidActions.Contains(adjustedAction)) return;

        var localPlayer = DService.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        if (!LuminaGetter.TryGetRow<Action>(adjustedAction, out var data)) return;
        if (data is not { TargetArea: true }) return;

        if (ActionManager.Instance()->GetActionStatus(actionType, adjustedAction) != 0) return;
        if (!DService.Gui.ScreenToWorld(ImGui.GetMousePos(), out var pos)) return;

        pos = AdjustTargetPosition(localPlayer.Position, pos, data.Range);
        UseActionManager.UseActionLocationCallDetour(ActionType.Action, adjustedAction, 0xE000_0000, pos, extraParam);
        UIGlobals.PlaySoundEffect(24);
        isPrevented = true;
    }

    public static Vector3 AdjustTargetPosition(Vector3 origin, Vector3 target, float maxDistance)
    {
        var originXZ = origin.ToVector2();
        var targetXZ = target.ToVector2();
        var distance = Vector2.DistanceSquared(originXZ, targetXZ);

        if (distance > maxDistance * maxDistance)
        {
            var direction = Vector2.Normalize(targetXZ - originXZ);
            targetXZ = originXZ + (direction * maxDistance);
            return new Vector3(targetXZ.X, target.Y, targetXZ.Y);
        }

        return target;
    }

    protected override void Uninit() => 
        UseActionManager.Unreg(OnPreUseAction);
}
