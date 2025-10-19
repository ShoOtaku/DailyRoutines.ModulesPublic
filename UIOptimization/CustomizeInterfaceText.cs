using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DailyRoutines.Abstracts;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;

namespace DailyRoutines.ModulesPublic;

public unsafe class CustomizeInterfaceText : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("CustomizeInterfaceTextTitle"),
        Description = GetLoc("CustomizeInterfaceTextDescription"),
        Category    = ModuleCategories.UIOptimization,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static readonly CompSig SetPlayerNamePlateSig = new("48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 44 0F B6 EA");
    private delegate nint SetPlayerNamePlateDelegate(
        nint namePlateObjectPtr,
        bool isPrefixTitle,
        bool displayTitle,
        nint titlePtr,
        nint namePtr,
        nint fcNamePtr,
        nint prefix,
        int  iconID);
    private static Hook<SetPlayerNamePlateDelegate>? SetPlayerNamePlateHook;

    private static readonly CompSig TextNodeSetStringSig = new("E8 ?? ?? ?? ?? 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC 40 55 56 57 48 81 EC");
    private delegate        void TextNodeSetStringDelegate(AtkTextNode* textNode, CStringPointer text);
    private static          Hook<TextNodeSetStringDelegate>? TextNodeSetStringHook;
    
    private static Config ModuleConfig = null!;

    private static string KeyInput   = string.Empty;
    private static string ValueInput = string.Empty;
    private static int    ReplaceModeInput;

    private static string KeyEditInput   = string.Empty;
    private static string ValueEditInput = string.Empty;
    private static int    ReplaceModeEditInput;


    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        TextNodeSetStringHook ??= TextNodeSetStringSig.GetHook<TextNodeSetStringDelegate>(TextNodeSetStringDetour);
        TextNodeSetStringHook.Enable();

        SetPlayerNamePlateHook ??= SetPlayerNamePlateSig.GetHook<SetPlayerNamePlateDelegate>(SetPlayerNamePlayerDetour);
        SetPlayerNamePlateHook.Enable();
    }

    protected override void ConfigUI()
    {
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{GetLoc("Key")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f * GlobalFontScale);
            ImGui.InputText("###KeyInput", ref KeyInput, 96);
            
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{GetLoc("Value")}:");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(300f * GlobalFontScale);
            ImGui.InputText("###ValueInput", ref ValueInput, 96);

            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{GetLoc("CustomizeInterfaceText-ReplaceMode")}:");

            foreach (var replaceMode in Enum.GetValues<ReplaceMode>())
            {
                ImGui.SameLine();
                ImGui.RadioButton(replaceMode.ToString(), ref ReplaceModeInput, (int)replaceMode);
            }
        }

        ImGui.SameLine();
        if (ImGuiOm.ButtonIconWithTextVertical(FontAwesomeIcon.Plus, GetLoc("Add")) && !string.IsNullOrWhiteSpace(KeyInput))
        {
            var pattern = new ReplacePattern(KeyInput, ValueInput, (ReplaceMode)ReplaceModeInput, true);
            if (ReplaceModeEditInput == (int)ReplaceMode.正则)
                pattern.Regex = new Regex(pattern.Key, RegexOptions.Compiled);

            if (!ModuleConfig.ReplacePatterns.Contains(pattern))
            {
                ModuleConfig.ReplacePatterns.Add(pattern);
                KeyInput = ValueInput = string.Empty;

                SaveConfig(ModuleConfig);
            }
        }

        ImGui.NewLine();

        using (var table = ImRaii.Table("###CustomizeInterfaceTextTable", 4, ImGuiTableFlags.Borders))
        {
            if (table)
            {
                ImGui.TableSetupColumn("启用",   ImGuiTableColumnFlags.WidthFixed,   20 * GlobalFontScale);
                ImGui.TableSetupColumn("键",    ImGuiTableColumnFlags.WidthStretch, 50);
                ImGui.TableSetupColumn("值",    ImGuiTableColumnFlags.WidthStretch, 50);
                ImGui.TableSetupColumn("匹配模式", ImGuiTableColumnFlags.WidthStretch, 15);

                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableNextColumn();
                ImGui.Text(string.Empty);
                ImGui.TableNextColumn();
                ImGui.Text(GetLoc("Key"));
                ImGui.TableNextColumn();
                ImGui.Text(GetLoc("Value"));
                ImGui.TableNextColumn();
                ImGui.Text(GetLoc("CustomizeInterfaceText-ReplaceMode"));

                var array = ModuleConfig.ReplacePatterns.ToArray();
                for (var i = 0; i < ModuleConfig.ReplacePatterns.Count; i++)
                {
                    var replacePattern = array[i];
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    var enabled = replacePattern.Enabled;
                    if (ImGui.Checkbox($"###{i}_IsEnabled", ref enabled))
                    {
                        ModuleConfig.ReplacePatterns[i].Enabled = enabled;
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.TableNextColumn();
                    ImGui.Selectable(replacePattern.Key, false, ImGuiSelectableFlags.DontClosePopups);

                    using (var context = ImRaii.ContextPopupItem($"{replacePattern.Key}_KeyEdit"))
                    {
                        if (context)
                        {
                            if (ImGui.IsWindowAppearing())
                                KeyEditInput = replacePattern.Key;

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text($"{GetLoc("Key")}:");

                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(300f * GlobalFontScale);
                            ImGui.InputText("###KeyEditInput", ref KeyEditInput, 96);
                            
                            if (ImGui.IsItemDeactivatedAfterEdit() && !string.IsNullOrWhiteSpace(KeyEditInput))
                            {
                                var pattern = new ReplacePattern(KeyEditInput, "", 0, replacePattern.Enabled);
                                if (!ModuleConfig.ReplacePatterns.Contains(pattern))
                                {
                                    ModuleConfig.ReplacePatterns[i].Key = KeyEditInput;
                                    if (replacePattern.Mode is ReplaceMode.正则)
                                        ModuleConfig.ReplacePatterns[i].Regex = new Regex(KeyEditInput);

                                    SaveConfig(ModuleConfig);
                                }
                            }

                            ImGui.SameLine();
                            if (ImGui.Button(GetLoc("Delete")))
                            {
                                if (ModuleConfig.ReplacePatterns.Remove(replacePattern))
                                    SaveConfig(ModuleConfig);
                            }
                        }
                    }

                    ImGui.TableNextColumn();
                    ImGui.Selectable(replacePattern.Value, false, ImGuiSelectableFlags.DontClosePopups);

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        ValueEditInput = replacePattern.Value;

                    using (var context = ImRaii.ContextPopupItem($"{replacePattern.Key}_ValueEdit"))
                    {
                        if (context)
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text($"{GetLoc("Value")}:");

                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(300f * GlobalFontScale);
                            ImGui.InputText("###ValueEditInput", ref ValueEditInput, 96);
                            
                            if (ImGui.IsItemDeactivatedAfterEdit())
                            {
                                ModuleConfig.ReplacePatterns[i].Value = ValueEditInput;
                                SaveConfig(ModuleConfig);
                            }
                        }
                    }

                    ImGui.TableNextColumn();
                    ImGui.Selectable(replacePattern.Mode.ToString(), false, ImGuiSelectableFlags.DontClosePopups);

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        ReplaceModeEditInput = (int)replacePattern.Mode;

                    using (var context = ImRaii.ContextPopupItem($"{replacePattern.Key}_ModeEdit"))
                    {
                        if (context)
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text($"{GetLoc("CustomizeInterfaceText-ReplaceMode")}:");

                            foreach (var replaceMode in Enum.GetValues<ReplaceMode>())
                            {
                                ImGui.SameLine();
                                ImGui.RadioButton(replaceMode.ToString(), ref ReplaceModeEditInput, (int)replaceMode);

                                if (ImGui.IsItemDeactivatedAfterEdit())
                                {
                                    ModuleConfig.ReplacePatterns[i].Mode = (ReplaceMode)ReplaceModeEditInput;
                                    if ((ReplaceMode)ReplaceModeEditInput is ReplaceMode.正则)
                                        ModuleConfig.ReplacePatterns[i].Regex = new Regex(replacePattern.Key);

                                    SaveConfig(ModuleConfig);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static void TextNodeSetStringDetour(AtkTextNode* textNode, CStringPointer text)
    {
        if (textNode == null || !text.HasValue)
        {
            TextNodeSetStringHook.Original(textNode, text);
            return;
        }

        var origText = SeString.Parse(text.Value);
        if (origText.Payloads.Count == 0)
        {
            TextNodeSetStringHook.Original(textNode, text);
            return;
        }

        if (ApplyTextReplacements(origText, out var modifiedText))
        {
            var pinnedArray = GCHandle.Alloc(modifiedText, GCHandleType.Pinned);
            try
            {
                TextNodeSetStringHook.Original(textNode, new((byte*)pinnedArray.AddrOfPinnedObject()));
            }
            finally
            {
                pinnedArray.Free();
            }
        }
        else
            TextNodeSetStringHook.Original(textNode, text);
    }

    private static nint SetPlayerNamePlayerDetour(
        nint namePlateObjectPtr,
        bool isPrefixTitle,
        bool displayTitle,
        nint titlePtr,
        nint namePtr,
        nint fcNamePtr,
        nint prefix,
        int  iconID)
    {
        using var nameMemory   = ReplaceTextAndAllocate(namePtr);
        using var titleMemory  = ReplaceTextAndAllocate(titlePtr);
        using var fcNameMemory = ReplaceTextAndAllocate(fcNamePtr);

        return SetPlayerNamePlateHook!.Original(
            namePlateObjectPtr, isPrefixTitle, displayTitle,
            titleMemory.Pointer, nameMemory.Pointer, fcNameMemory.Pointer, prefix, iconID);
    }

    private static PinnedMemory ReplaceTextAndAllocate(nint originalTextPtr)
    {
        var origText = MemoryHelper.ReadSeStringNullTerminated(originalTextPtr);
        return ApplyTextReplacements(origText, out var modifiedText)
            ? new PinnedMemory(modifiedText)
            : new PinnedMemory(Array.Empty<byte>());
    }

    private static bool ApplyTextReplacements(SeString origText, out byte[]? modifiedText)
    {
        modifiedText = null;
        var textPayloads = origText.Payloads.OfType<TextPayload>().ToArray();

        var modified = false;
        foreach (var pattern in ModuleConfig.ReplacePatterns)
        {
            if (!pattern.Enabled) continue;

            foreach (var payload in textPayloads)
            {
                var originalText = payload.Text;
                var replacedText = pattern.Mode switch
                {
                    ReplaceMode.部分匹配 => originalText.Contains(pattern.Key, StringComparison.Ordinal)
                        ? originalText.Replace(pattern.Key, pattern.Value)
                        : null,
                    ReplaceMode.完全匹配 => originalText.Equals(pattern.Key, StringComparison.Ordinal)
                        ? pattern.Value
                        : null,
                    ReplaceMode.正则 => pattern.Regex?.Replace(originalText, pattern.Value),
                    _ => null,
                };

                if (replacedText != null)
                {
                    payload.Text = replacedText;
                    modified = true;
                }
            }
        }

        if (modified)
        {
            modifiedText = origText.Encode();
            return true;
        }

        return false;
    }

    private class Config : ModuleConfiguration
    {
        public List<ReplacePattern> ReplacePatterns = [];
    }
    
    private enum ReplaceMode
    {
        部分匹配,
        完全匹配,
        正则,
    }

    private class ReplacePattern : IComparable<ReplacePattern>, IEquatable<ReplacePattern>
    {
        public ReplacePattern() { }

        public ReplacePattern(string key, string value, ReplaceMode mode, bool enabled)
        {
            Key     = key;
            Value   = value;
            Mode    = mode;
            Enabled = enabled;
            
            if (mode == ReplaceMode.正则)
                Regex = new Regex(key, RegexOptions.Compiled);
        }

        public string      Key     { get; set; } = string.Empty;
        public string      Value   { get; set; } = string.Empty;
        public ReplaceMode Mode    { get; set; }
        public bool        Enabled { get; set; }
        public Regex?      Regex   { get; set; }

        public int CompareTo(ReplacePattern? other) =>
            other == null ? 1 : string.Compare(Key, other.Key, StringComparison.Ordinal);

        public bool Equals(ReplacePattern? other) => other != null && Key == other.Key;

        public override bool Equals(object? obj) => Equals(obj as ReplacePattern);

        public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);

        public void Deconstruct(out string key, out string value, out ReplaceMode mode, out bool enabled) =>
            (key, value, mode, enabled) = (Key, Value, Mode, Enabled);
    }
    
    private struct PinnedMemory : IDisposable
    {
        public readonly nint     Pointer;
        private         GCHandle handle;

        public PinnedMemory(IEnumerable array)
        {
            handle  = GCHandle.Alloc(array, GCHandleType.Pinned);
            Pointer = handle.AddrOfPinnedObject();
        }

        public void Dispose() => handle.Free();
    }
}
