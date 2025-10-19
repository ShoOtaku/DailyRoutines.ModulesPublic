using System;
using DailyRoutines.Abstracts;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using InteropGenerator.Runtime;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class InstantLogout : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("InstantLogoutTitle"),
        Description = GetLoc("InstantLogoutDescription"),
        Category    = ModuleCategories.System,
    };

    private static readonly CompSig                          SystemMenuExecuteSig = new("E8 ?? ?? ?? ?? 48 8B 07 48 8B CF 89 5F ?? FF 50 ?? 84 C0");
    private delegate        nint                             SystemMenuExecuteDelegate(AgentHUD* agentHud, int a2, uint a3, int a4, nint a5);
    private static          Hook<SystemMenuExecuteDelegate>? SystemMenuExecuteHook;

    private static          Hook<AgentShowDelegate>? AgentCloseMessageShowHook;

    private static readonly CompSig                          ProcessSendedChatSig = new("E8 ?? ?? ?? ?? FE 87 ?? ?? ?? ?? C7 87 ?? ?? ?? ?? ?? ?? ?? ??");
    private delegate        byte                             ProcessSendedChatDelegate(nint uiModule, CStringPointer* message, nint a3);
    private static          Hook<ProcessSendedChatDelegate>? ProcessSendedChatHook;

    private static readonly Lazy<TextCommand> LogoutLine   = new(() => LuminaGetter.GetRowOrDefault<TextCommand>(172));
    private static readonly Lazy<TextCommand> ShutdownLine = new(() => LuminaGetter.GetRowOrDefault<TextCommand>(173));
    
    protected override void Init()
    {
        TaskHelper ??= new();

        SystemMenuExecuteHook ??= SystemMenuExecuteSig.GetHook<SystemMenuExecuteDelegate>(SystemMenuExecuteDetour);
        SystemMenuExecuteHook.Enable();

        AgentCloseMessageShowHook ??= DService.Hook.HookFromAddress<AgentShowDelegate>(
            GetVFuncByName(AgentModule.Instance()->GetAgentByInternalId(AgentId.CloseMessage)->VirtualTable, "Show"),
            AgentCloseMessageShowDetour);
        AgentCloseMessageShowHook.Enable();

        ProcessSendedChatHook ??= ProcessSendedChatSig.GetHook<ProcessSendedChatDelegate>(ProcessSendedChatDetour);
        ProcessSendedChatHook.Enable();
    }

    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("InstantLogout-ManualOperation")}:");

        ImGui.SameLine();
        if (ImGui.Button(GetLoc("InstantLogout-Logout"))) 
            Logout(TaskHelper);
        
        ImGui.SameLine();
        if (ImGui.Button(GetLoc("InstantLogout-Shutdown"))) 
            Shutdown(TaskHelper);
    }

    private nint SystemMenuExecuteDetour(AgentHUD* agentHud, int a2, uint a3, int a4, nint a5)
    {
        if (a2 is 1 && a4 is -1)
        {
            switch (a3)
            {
                case 23:
                    Logout(TaskHelper);
                    return 0;
                case 24:
                    Shutdown(TaskHelper);
                    return 0;
            }
        }
        
        return SystemMenuExecuteHook.Original(agentHud, a2, a3, a4, a5);
    }
    
    private void AgentCloseMessageShowDetour(AgentInterface* agent) => 
        Shutdown(TaskHelper);

    private byte ProcessSendedChatDetour(nint uiModule, CStringPointer* message, nint a3)
    {
        var messageDecode = message->ToString();

        if (string.IsNullOrWhiteSpace(messageDecode) || !messageDecode.StartsWith('/'))
            return ProcessSendedChatHook.Original(uiModule, message, a3);

        CheckCommand(messageDecode, LogoutLine.Value,   TaskHelper, Logout);
        CheckCommand(messageDecode, ShutdownLine.Value, TaskHelper,  Shutdown);

        return ProcessSendedChatHook.Original(uiModule, message, a3);
    }

    private static void CheckCommand(string message, TextCommand command, TaskHelper taskHelper, Action<TaskHelper> action)
    {
        if (message == command.Command.ExtractText() || message == command.Alias.ExtractText()) 
            action(taskHelper);
    }

    private static void Logout(TaskHelper _) => 
        RequestDutyNormal(167, DefaultOption);

    private static void Shutdown(TaskHelper taskHelper)
    {
        taskHelper.Enqueue(() => Logout(taskHelper));
        taskHelper.Enqueue(() =>
        {
            if (DService.ClientState.IsLoggedIn) return false;

            ChatHelper.SendMessage("/xlkill");
            return true;
        });
    }
}
