using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

public unsafe class AutoWoof : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoWoofTitle"),
        Description = GetLoc("AutoWoofDescription"),
        Category    = ModuleCategories.General,
        Author      = ["逆光"]
    };

    protected override void Init() => FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 1500);

    private static void OnUpdate(IFramework framework)
    {
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;
        if (!DService.Instance().Condition[ConditionFlag.Mounted] || localPlayer.CurrentMount?.RowId != 294) return;
        if (ActionManager.Instance()->GetActionStatus(ActionType.Action, 29463) != 0) return;

        UseActionManager.Instance().UseAction(ActionType.Action, 29463);
    }

    protected override void Uninit() => FrameworkManager.Instance().Unreg(OnUpdate);
}
