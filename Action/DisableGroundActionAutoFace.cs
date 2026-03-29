using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using OmenTools.Interop.Game;

namespace DailyRoutines.ModulesPublic;

public class DisableGroundActionAutoFace : ModuleBase
{
    private static readonly MemoryPatch GroundActionAutoFacePatch =
        new("74 ?? 48 8D 8E ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 75 ?? 48 8B 55", [0xEB]);

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("DisableGroundActionAutoFaceTitle"),
        Description = Lang.Get("DisableGroundActionAutoFaceDescription"),
        Category    = ModuleCategory.Action
    };

    protected override void Init() =>
        GroundActionAutoFacePatch.Set(true);

    protected override void Uninit() =>
        GroundActionAutoFacePatch.Dispose();
}
