using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Hooking;
using OmenTools.Interop.Game.Models;

namespace DailyRoutines.ModulesPublic;

public class AutoBlockSystemNotice : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoBlockSystemNoticeTitle"),
        Description = Lang.Get("AutoBlockSystemNoticeDescription"),
        Category    = ModuleCategory.System
    };

    private static readonly CompSig LoginNoticeSig = new
    (
        "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8D 57 ?? 41 8B CE E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8D 57 ?? 41 8B CE E8 ?? ?? ?? ?? B9"
    );
    private delegate void LoginNoticeHandlerDelegate
    (
        nint dispatcher,
        nint packet
    );
    private Hook<LoginNoticeHandlerDelegate>? LoginNoticeHandlerHook;

    private bool isLoginNotice;

    protected override void Init()
    {
        IChatGui.Instance().ChatMessage += OnChat;

        LoginNoticeHandlerHook ??= LoginNoticeSig.GetHook<LoginNoticeHandlerDelegate>(LoginNoticeHandlerDetour);
        LoginNoticeHandlerHook.Enable();
    }

    protected override void Uninit() =>
        IChatGui.Instance().ChatMessage -= OnChat;

    private void LoginNoticeHandlerDetour
    (
        nint dispatcher,
        nint packet
    )
    {
        isLoginNotice = true;
        LoginNoticeHandlerHook.Original(dispatcher, packet);
        isLoginNotice = false;
    }

    private void OnChat
    (
        IHandleableChatMessage message
    )
    {
        if (!isLoginNotice || message.LogKind != XivChatType.Notice) return;
        message.PreventOriginal();
    }
}
