using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using OmenTools.Interop.Game.Models;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoSharpenInterfaceText : ModuleBase
{
    private static readonly CompSig                          AtkTextNodeSetTextSig = new("48 85 C9 0F 84 ?? ?? ?? ?? 4C 8B DC 53 56");
    private static          Hook<AtkTextNodeSetTextDelegate> AtkTextNodeSetTextHook;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoSharpenInterfaceTextTitle"),
        Description = Lang.Get("AutoSharpenInterfaceTextDescription"),
        Category    = ModuleCategory.UIOptimization
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init()
    {
        AtkTextNodeSetTextHook ??= AtkTextNodeSetTextSig.GetHook<AtkTextNodeSetTextDelegate>(AtkTextNodeSetTextDetour);
        AtkTextNodeSetTextHook.Enable();
    }

    private static void AtkTextNodeSetTextDetour(AtkTextNode* node, CStringPointer text)
    {
        AtkTextNodeSetTextHook.Original(node, text);

        if (node == null || !text.HasValue) return;

        // NamePlate
        if ((byte)node->TextFlags == 152 && node->AlignmentFontType == 7)
            return;

        var flag = node->TextFlags;

        if (flag.HasFlag((TextFlags)(1 << 12)))
        {
            flag            &= ~(TextFlags)(1 << 12);
            node->TextFlags =  flag;
        }
    }

    private delegate void AtkTextNodeSetTextDelegate(AtkTextNode* node, CStringPointer text);
}
