using System.Text.RegularExpressions;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic.Interface;

public partial class UnlimitedMacroIcon : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("UnlimitedMacroIconTitle"),
        Description = Lang.Get("UnlimitedMacroIconDescription", COMMAND),
        Category    = ModuleCategory.Interface
    };

    public override ModulePermission Permission { get; } = new()
    {
        AllDefaultEnabled = true
    };

    private Hook<RaptureMacroModule.Delegates.TryResolveMacroIcon>? TryResolveMacroIconHook;

    protected override unsafe void Init()
    {
        TryResolveMacroIconHook = IGameInteropProvider.Instance().HookFromMemberFunction
        (
            typeof(RaptureMacroModule.MemberFunctionPointers),
            "TryResolveMacroIcon",
            (RaptureMacroModule.Delegates.TryResolveMacroIcon)TryResolveMacroIconDetour
        );
        TryResolveMacroIconHook.Enable();

        CommandManager.Instance().AddSubCommand(COMMAND, new((_, _) => { }) { ShowInHelp = false });
    }

    protected override void Uninit() =>
        CommandManager.Instance().RemoveSubCommand(COMMAND);

    private unsafe bool TryResolveMacroIconDetour
    (
        RaptureMacroModule*                 module,
        UIModule*                           uiModule,
        RaptureHotbarModule.HotbarSlotType* outType,
        uint*                               outRowID,
        int                                 setID,
        uint                                macroID,
        uint*                               outItemID
    )
    {
        var macro = module->GetMacro((uint)setID, macroID);
        if (macro == null)
            return TryResolveMacroIconHook.Original(module, uiModule, outType, outRowID, setID, macroID, outItemID);

        for (var i = 0; i < 15; i++)
        {
            var match = CommandRegex().Match(macro->Lines[i].ToString());
            if (!match.Success) continue;

            if (uint.TryParse(match.Groups[1].Value, out var iconID) && macro->IconId != iconID)
            {
                macro->SetIcon(iconID);
                module->SetSavePendingFlag(true, (uint)setID);
            }

            return false;
        }

        return TryResolveMacroIconHook.Original(module, uiModule, outType, outRowID, setID, macroID, outItemID);
    }

    #region 常量

    private const string COMMAND = "micon";

    [GeneratedRegex($@"^\s*/pdr\s+{COMMAND}\s+(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CommandRegex();

    #endregion
}
