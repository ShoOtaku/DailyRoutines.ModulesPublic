using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoReadOutTalk : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title           = GetLoc("AutoReadOutTalkTitle"),
        Description     = GetLoc("AutoReadOutTalkDescription"),
        Category        = ModuleCategories.General,
        ModulesConflict = ["AutoTalkSkip"]
    };

    private static Config ModuleConfig = null!;

    private delegate void                          ShowBattleTalkDelegate(UIModule* module, CStringPointer name, CStringPointer text, float duration, byte style);
    private static   Hook<ShowBattleTalkDelegate>? ShowBattleTalkHook;

    private delegate void ShowBattleTalkImageDelegate
    (
        UIModule*      module,
        CStringPointer name,
        CStringPointer text,
        float          duration,
        uint           image,
        byte           style,
        int            sound,
        uint           entityID
    );
    private static Hook<ShowBattleTalkImageDelegate>? ShowBattleTalkImageHook;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "Talk", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Talk", OnAddon);
        DService.Instance().AddonLifecycle.RegisterListener(AddonEvent.PreHide,     "Talk", OnAddon);

        ShowBattleTalkHook = UIModule.Instance()->VirtualTable->HookVFuncFromName("ShowBattleTalk", (ShowBattleTalkDelegate)ShowBattleTalkDetour);
        ShowBattleTalkHook.Enable();

        ShowBattleTalkImageHook = UIModule.Instance()->VirtualTable->HookVFuncFromName
            ("ShowBattleTalkImage", (ShowBattleTalkImageDelegate)ShowBattleTalkImageDetour);
        ShowBattleTalkImageHook.Enable();
    }

    protected override void Uninit()
    {
        DService.Instance().AddonLifecycle.UnregisterListener(OnAddon);
        CancelBefore();
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("Format"));

        using (ImRaii.PushIndent())
        {
            ImGui.InputText($"##FormatInput", ref ModuleConfig.Format);
            if (ImGui.IsItemDeactivatedAfterEdit())
                SaveConfig(ModuleConfig);
        }
    }

    private static void ShowBattleTalkDetour(UIModule* module, CStringPointer name, CStringPointer text, float duration, byte style)
    {
        ShowBattleTalkHook.Original(module, name, text, duration, style);

        var speaker = name.HasValue ? name.ExtractText() : string.Empty;
        var line    = text.HasValue ? text.ExtractText() : string.Empty;

        if (string.IsNullOrEmpty(line)) return;

        CancelBefore();
        Speak(string.Format(ModuleConfig.Format, speaker, line));
    }

    private static void ShowBattleTalkImageDetour
    (
        UIModule*      module,
        CStringPointer name,
        CStringPointer text,
        float          duration,
        uint           image,
        byte           style,
        int            sound,
        uint           entityID
    )
    {
        ShowBattleTalkImageHook.Original(module, name, text, duration, image, style, sound, entityID);

        if (sound > -1) return;

        var speaker = name.HasValue ? name.ExtractText() : string.Empty;
        var line    = text.HasValue ? text.ExtractText() : string.Empty;

        if (string.IsNullOrEmpty(line)) return;
        
        CancelBefore();
        Speak(string.Format(ModuleConfig.Format, speaker, line));
    }

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PostRefresh:
                string? line    = null;
                string? speaker = null;

                if (Talk == null) return;

                // 没有实际文本
                if (Talk->AtkValues[0].Type != ValueType.ManagedString || !Talk->AtkValues[0].String.HasValue) return;

                // 没有说话人
                if (Talk->AtkValues[1].Type == ValueType.ManagedString && Talk->AtkValues[1].String.HasValue)
                    speaker = Talk->AtkValues[1].String.ExtractText();

                // 非普通对话
                if (Talk->AtkValues[3].Type != ValueType.UInt || Talk->AtkValues[3].UInt != 0) return;

                line = Talk->AtkValues[0].String.ExtractText();

                if (string.IsNullOrEmpty(line)) return;

                CancelBefore();
                Speak(string.Format(ModuleConfig.Format, speaker, line));
                break;

            case AddonEvent.PreFinalize:
            case AddonEvent.PreHide:
                CancelBefore();
                break;
        }
    }

    private static void CancelBefore() =>
        StopSpeak();

    private class Config : ModuleConfiguration
    {
        public string Format = "{0}: {1}";
    }
}
