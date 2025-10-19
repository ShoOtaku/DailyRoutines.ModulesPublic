using DailyRoutines.Abstracts;

namespace DailyRoutines.ModulesPublic;

public class DisableGroundActionAutoFace : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("DisableGroundActionAutoFaceTitle"),
        Description = GetLoc("DisableGroundActionAutoFaceDescription"),
        Category    = ModuleCategories.Action,
    };

    private static readonly MemoryPatch GroundActionAutoFacePatch =
        new("74 ?? 48 8D 8E ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 75 ?? 48 8B 55", [0xEB]);

    protected override void Init() => 
        GroundActionAutoFacePatch.Set(true);

    protected override void Uninit() => 
        GroundActionAutoFacePatch.Dispose();
}
