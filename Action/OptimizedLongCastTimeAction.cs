using System;
using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

// 移除, 调整为可以自定义
public unsafe class OptimizedLongCastTimeAction : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("OptimizedLongCastTimeActionTitle"),
        Description = GetLoc("OptimizedLongCastTimeActionDescription", CAST_TIME_REDUCTION),
        Category    = ModuleCategories.Action,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static readonly CompSig GetAdjustedCastTimeSig = new("E8 ?? ?? ?? ?? 8B D0 48 8B CD E8 ?? ?? ?? ?? EB 25");
    private delegate        int GetAdjustedCastTimeDelegate(ActionType actionType, uint actionID, bool applyProcess, ActionManager.CastTimeProc* castTimeProc);
    private static          Hook<GetAdjustedCastTimeDelegate>? GetAdjustedCastTimeHook;

    private static readonly CompSig CastInfoUpdateTotalSig =
        new("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B F9 0F 29 74 24 ?? 0F B6 49");
    private delegate uint CastInfoUpdateTotalDelegate(nint data, uint spellActionID, float process, float processTotal);
    private static Hook<CastInfoUpdateTotalDelegate>? CastInfoUpdateTotalHook;

    private static readonly CompSig CastTimeCurrentSig =
        new("F3 44 0F 2C C0 BA ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? F3 44 0F 10 1D");
    private static float* CastTimeCurrent;

    private const float CAST_TIME_REDUCTION = 0.4f;

    protected override void Init()
    {
        GetAdjustedCastTimeHook ??= GetAdjustedCastTimeSig.GetHook<GetAdjustedCastTimeDelegate>(GetAdjustedCastTimeDetour);
        GetAdjustedCastTimeHook.Enable();
        
        CastTimeCurrent = CastTimeCurrentSig.GetStatic<float>(0x12);
        
        CastInfoUpdateTotalHook ??= CastInfoUpdateTotalSig.GetHook<CastInfoUpdateTotalDelegate>(CastInfoUpdateTotalDetour);
        CastInfoUpdateTotalHook.Enable();
    }

    private static int GetAdjustedCastTimeDetour(ActionType actionType, uint actionID, bool applyProcess, ActionManager.CastTimeProc* castTimeProc)
    {
        var orig = GetAdjustedCastTimeHook.Original(actionType, actionID, applyProcess, castTimeProc);

        var recastTime = ActionManager.GetAdjustedRecastTime(actionType, actionID);
        // 咏唱大于复唱
        if (recastTime <= orig) return orig - (int)(CAST_TIME_REDUCTION * 1000);
        
        return orig;
    }

    private static uint CastInfoUpdateTotalDetour(nint data, uint spellActionID, float processTotal, float processStart)
    {
        var actionID   = *(uint*)((byte*)data + 4);
        var actionType = (ActionType)(*((byte*)data + 2));
        
        if (actionID == spellActionID)
        {
            var recastTime = ActionManager.GetAdjustedRecastTime(actionType, actionID);
            if (recastTime <= processTotal * 1000)
            {
                processTotal     = Math.Max(processTotal - CAST_TIME_REDUCTION, 0);
                *CastTimeCurrent = processTotal;
            }
        }
        
        return CastInfoUpdateTotalHook.Original(data, spellActionID, processTotal, processStart);
    }
}
