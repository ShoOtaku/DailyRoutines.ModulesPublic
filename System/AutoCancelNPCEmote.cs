using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCancelNPCEmote : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCancelNPCEmoteTitle"),
        Description = GetLoc("AutoCancelNPCEmoteDescription"),
        Category    = ModuleCategories.System
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private delegate nint EventSceneScriptDelegate(EventSceneModuleImplBase* scene);

    private static readonly CompSig WaitForBaseSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 48 8B D9 48 8B 49 ?? E8 ?? ?? ?? ?? 48 8B 35");
    
    private static Hook<EventSceneScriptDelegate>? WaitForActionTimelineHook;
    private static Hook<EventSceneScriptDelegate>? WaitForActionTimelineLoadHook;
    private static Hook<EventSceneScriptDelegate>? PlayActionTimelineHook;
    private static Hook<EventSceneScriptDelegate>? PlayEmoteHook;
    private static Hook<EventSceneScriptDelegate>? CancelEmoteHook;
    private static Hook<EventSceneScriptDelegate>? WaitForEmoteHook;
    private static Hook<EventSceneScriptDelegate>? IsEmotingHook;

    protected override void Init()
    {
        var baseAddress = WaitForBaseSig.ScanText();

        WaitForActionTimelineHook ??=
            DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "WaitForActionTimeline"), EventSceneScriptDetour);
        WaitForActionTimelineHook.Enable();

        PlayActionTimelineHook ??=
            DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "PlayActionTimeline"), EventSceneScriptDetour);
        PlayActionTimelineHook.Enable();

        WaitForActionTimelineLoadHook ??=
            DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "WaitForActionTimelineLoad"), EventSceneScriptDetour);
        WaitForActionTimelineLoadHook.Enable();
        
        PlayEmoteHook ??= DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "PlayEmote"), EventSceneScriptDetour);
        PlayEmoteHook.Enable();
        
        CancelEmoteHook ??= DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "CancelEmote"), EventSceneScriptDetour);
        CancelEmoteHook.Enable();
        
        WaitForEmoteHook ??= DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "WaitForEmote"), EventSceneScriptDetour);
        WaitForEmoteHook.Enable();
        
        IsEmotingHook ??= DService.Hook.HookFromAddress<EventSceneScriptDelegate>(GetLuaFunctionByName(baseAddress, "IsEmoting"), EventSceneScriptNoDetour);
        IsEmotingHook.Enable();
    }
    
    private static nint EventSceneScriptDetour(EventSceneModuleImplBase* scene) => 1;
    
    private static nint EventSceneScriptNoDetour(EventSceneModuleImplBase* scene) => 0;
}
