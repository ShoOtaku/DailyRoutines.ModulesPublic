using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using OmenTools.Interop.Game;

namespace DailyRoutines.ModulesPublic;

public class SameAethernetTeleport : ModuleBase
{
    private static readonly MemoryPatch Patch0 = new("75 ?? 48 8B 49 ?? 48 8B 01 FF 50 ?? 48 8B C8 BA ?? ?? ?? ?? 48 83 C4 ?? 5E 5D", [0xEB]);
    private static readonly MemoryPatch Patch1 = new("75 ?? 48 8B 4E ?? 48 8B 01 FF 50 ?? 48 8B C8 BA ?? ?? ?? ?? E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 80 7D", [0xEB]);

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("SameAethernetTeleportTitle"),
        Description = Lang.Get("SameAethernetTeleportDescription"),
        Category    = ModuleCategory.System
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true, AllDefaultEnabled = true };

    protected override void Init()
    {
        Patch0.Enable();
        Patch1.Enable();
    }

    protected override void Uninit()
    {
        Patch0.Disable();
        Patch1.Disable();
    }
}
