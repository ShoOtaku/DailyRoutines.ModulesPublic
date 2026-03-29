using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace DailyRoutines.ModulesPublic;

public unsafe class IgnoreWindowMinSizeLimit : ModuleBase
{
    private static int OriginalMinWidth  = 1024;
    private static int OriginalMinHeight = 720;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("IgnoreWindowMinSizeLimitTitle"),
        Description = Lang.Get("IgnoreWindowMinSizeLimitDescription"),
        Category    = ModuleCategory.System,
        Author      = ["Siren"]
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init()
    {
        OriginalMinWidth  = GameWindow.Instance()->MinWidth;
        OriginalMinHeight = GameWindow.Instance()->MinHeight;

        GameWindow.Instance()->MinHeight = 1;
        GameWindow.Instance()->MinWidth  = 1;
    }

    protected override void Uninit()
    {
        if (!IsEnabled) return;

        GameWindow.Instance()->MinWidth  = OriginalMinWidth;
        GameWindow.Instance()->MinHeight = OriginalMinHeight;
    }
}
