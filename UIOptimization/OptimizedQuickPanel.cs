using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace DailyRoutines.ModulesPublic;

public unsafe class OptimizedQuickPanel : DailyModuleBase
{
    private static readonly TextCommand QuickPanelLine = LuminaGetter.GetRowOrDefault<TextCommand>(50);
    
    protected override void Init()
    {
        ChatManager.RegPreExecuteCommandInner(OnPreExecuteCommandInner);
    }

    protected override void Uninit()
    {
        ChatManager.Unreg(OnPreExecuteCommandInner);
    }

    private static void OnPreExecuteCommandInner(ref bool isPrevented, ref ReadOnlySeString message)
    {
        var messageText = message.ToString();
        if (!messageText.StartsWith('/')) return;
        if (messageText.Split(' ') is not { Length: 2 } prasedCommand                                                      ||
            (prasedCommand[0] != QuickPanelLine.Command.ToString() && prasedCommand[0] != QuickPanelLine.Alias.ToString()) ||
            !int.TryParse(prasedCommand[1], out var index)                                                                 ||
            index - 1 < 0                                                                                                  ||
            index     > 4)
            return;
        
        AgentQuickPanel.Instance()->OpenPanel((uint)(index - 1), showFirstTimeHelp: false);
        isPrevented = true;
    }
}
