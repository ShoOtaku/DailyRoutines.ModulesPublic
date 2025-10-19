using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Common.Lua;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoSnipeQuest : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSnipeQuestTitle"),
        Description = GetLoc("AutoSnipeQuestDescription"),
        Category    = ModuleCategories.System,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static readonly CompSig EnqueueSnipeTaskSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F9 48 8D 4C 24 ??");
    private delegate        ulong                          EnqueueSnipeTaskDelegate(EventSceneModuleImplBase* scene, lua_State* state);
    private static          Hook<EnqueueSnipeTaskDelegate> EnqueueSnipeTaskHook;

    protected override void Init()
    {
        EnqueueSnipeTaskHook ??= EnqueueSnipeTaskSig.GetHook<EnqueueSnipeTaskDelegate>(EnqueueSnipeTaskDetour);
        EnqueueSnipeTaskHook.Enable();
    }

    private static ulong EnqueueSnipeTaskDetour(EventSceneModuleImplBase* scene, lua_State* state)
    {
        var value = state->top;
        value->tt = 3;
        value->value.n = 1;
        state->top += 1;
        return 1;
    }
}
