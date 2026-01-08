using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.Command;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Threading;

namespace DailyRoutines.Modules;

public class FastResetAllSDEnmity : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = GetLoc("FastResetAllSDEnmityTitle"),
        Description = GetLoc("FastResetAllSDEnmityDescription"),
        Category = ModuleCategories.Combat,
    };

    private static CancellationTokenSource? CancelSource;
    
    private const string Command = "resetallsd";

    protected override void Init()
    {
        CancelSource ??= new();

        ExecuteCommandManager.Instance().RegPre(OnResetStrikingDummies);
        CommandManager.AddSubCommand(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = GetLoc("FastResetAllSDEnmity-CommandHelp"),
        });
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Command")}:");

        ImGui.SameLine();
        ImGui.TextUnformatted($"/pdr {Command} → {GetLoc("FastResetAllSDEnmity-CommandHelp")}");
    }

    private static void OnCommand(string command, string arguments) => ResetAllStrikingDummies();

    public static void OnResetStrikingDummies(
        ref bool isPrevented, ref ExecuteCommandFlag command, ref uint param1, ref uint param2, ref uint param3, ref uint param4)
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
