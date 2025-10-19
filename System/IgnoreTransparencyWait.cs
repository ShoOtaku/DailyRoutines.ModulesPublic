using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace DailyRoutines.ModulesPublic;

public unsafe class IgnoreTransparencyWait : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("IgnoreTransparencyWaitTitle"),
        Description = GetLoc("IgnoreTransparencyWaitDescription"),
        Category    = ModuleCategories.System
    };
    
    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private delegate nint EventSceneScriptDelegate(EventSceneModuleImplBase* scene);

    private static readonly CompSig WaitForBaseSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B D9 48 8B 49 ?? E8 ?? ?? ?? ?? 48 8B 35");

    private static Hook<EventSceneScriptDelegate>? WaitForTransparencyHook;
    private static Hook<EventSceneScriptDelegate>? WaitForMoveHook;
    private static Hook<EventSceneScriptDelegate>? WaitForPathMoveHook;

    protected override void Init()
    {
        var baseAddress = WaitForBaseSig.ScanText();

        WaitForTransparencyHook ??=
            DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "WaitForTransparency"), EventSceneScriptDetour);
        WaitForTransparencyHook.Enable();
        
        WaitForMoveHook ??= DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "WaitForMove"), EventSceneScriptDetour);
        WaitForMoveHook.Enable();

        WaitForPathMoveHook ??=
            DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "WaitForPathMove"), EventSceneScriptDetour);
        WaitForPathMoveHook.Enable();
    }
    
    private static nint EventSceneScriptDetour(EventSceneModuleImplBase* scene) => 1;
}
