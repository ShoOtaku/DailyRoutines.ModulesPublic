using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Manager;
using Dalamud.Game.Command;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using OmenTools.Info.Game.Enums;
using OmenTools.OmenService;

namespace DailyRoutines.Modules;

public class FastResetAllSDEnmity : ModuleBase
{
    private const string Command = "resetallsd";

    private static CancellationTokenSource? CancelSource;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("FastResetAllSDEnmityTitle"),
        Description = Lang.Get("FastResetAllSDEnmityDescription"),
        Category    = ModuleCategory.Combat
    };

    protected override void Init()
    {
        CancelSource ??= new();

        ExecuteCommandManager.Instance().RegPre(OnResetStrikingDummies);
        CommandManager.AddSubCommand
        (
            Command,
            new CommandInfo(OnCommand)
            {
                HelpMessage = Lang.Get("FastResetAllSDEnmity-CommandHelp")
            }
        );
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("Command")}:");

        ImGui.SameLine();
        ImGui.TextUnformatted($"/pdr {Command} → {Lang.Get("FastResetAllSDEnmity-CommandHelp")}");
    }

    private static void OnCommand(string command, string arguments) => ResetAllStrikingDummies();

    public static void OnResetStrikingDummies
    (
        ref bool               isPrevented,
        ref ExecuteCommandFlag command,
        ref uint               param1,
        ref uint               param2,
        ref uint               param3,
        ref uint               param4
    )
    {
        if (command != ExecuteCommandFlag.ResetStrikingDummy) return;
        isPrevented = true;

        ResetAllStrikingDummies();
    }

    private static void ResetAllStrikingDummies()
    {
        DService.Instance().Framework.RunOnTick(FindAndResetInternal, TimeSpan.Zero,                   0, CancelSource.Token);
        DService.Instance().Framework.RunOnTick(FindAndResetInternal, TimeSpan.FromMilliseconds(500),  0, CancelSource.Token);
        DService.Instance().Framework.RunOnTick(FindAndResetInternal, TimeSpan.FromMilliseconds(1000), 0, CancelSource.Token);
        DService.Instance().Framework.RunOnTick(FindAndResetInternal, TimeSpan.FromMilliseconds(1500), 0, CancelSource.Token);
    }

    private static unsafe void FindAndResetInternal()
    {
        var targets = UIState.Instance()->Hater.Haters;
        foreach (var targetID in targets)
            ExecuteCommandManager.Instance().ExecuteCommand(ExecuteCommandFlag.ResetStrikingDummy, targetID.EntityId);
    }

    protected override void Uninit()
    {
        ExecuteCommandManager.Instance().Unreg(OnResetStrikingDummies);
        CommandManager.RemoveSubCommand(Command);

        CancelSource?.Cancel();
        CancelSource?.Dispose();
        CancelSource = null;
    }
}
