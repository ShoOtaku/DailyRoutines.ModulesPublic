using System.Runtime.InteropServices;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using DailyRoutines.Internal;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools.Interop.Game.Models;
using OmenTools.Threading;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoNumericInputMax : ModuleBase
{
    private static readonly CompSig UldUpdateSig =
        new("40 53 48 83 EC ?? 48 8B D9 48 83 C1 ?? E8 ?? ?? ?? ?? 80 BB ?? ?? ?? ?? ?? 74 ?? 48 8B CB");

    private static Hook<UldUpdateDelegate>? UldUpdateHook;

    private static readonly CompSig NumericSetValueSig = new
        ("E8 ?? ?? ?? ?? C7 83 ?? ?? ?? ?? ?? ?? ?? ?? 48 83 C4 ?? 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 8B 91");

    private static NumericSetValueDelegate? NumericSetValue;

    private static readonly HashSet<string> BlacklistAddons =
    [
        "RetainerSell", "ItemSearch", "LookingForGroupSearch", "LookingForGroupCondition", "CountDownSettingDialog",
        "ConfigSystem", "ConfigCharacter"
    ];

    private static readonly Throttler<nint> Throttler = new();
    private static          long            LastInterruptTime;

    private static bool IsBlocked;

    private static Config ModuleConfig = null!;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoNumericInputMaxTitle"),
        Description = Lang.Get("AutoNumericInputMaxDescription"),
        Category    = ModuleCategory.UIOptimization
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    protected override void Init()
    {
        ModuleConfig ??= Config.Load(this) ?? new();

        NumericSetValue ??= Marshal.GetDelegateForFunctionPointer<NumericSetValueDelegate>(NumericSetValueSig.ScanText());

        UldUpdateHook ??= DService.Instance().Hook.HookFromSignature<UldUpdateDelegate>(UldUpdateSig.Get(), UldUpdateDetour);
        UldUpdateHook.Enable();
    }

    protected override void ConfigUI()
    {
        ImGuiOm.ConflictKeyText();
        ImGuiOm.HelpMarker(Lang.Get("AutoNumericInputMax-InterruptHelp"));

        ImGui.Spacing();

        if (ImGui.Checkbox(Lang.Get("AutoNumericInputMax-AdjustMax"), ref ModuleConfig.AdjustMaximumValue))
            ModuleConfig.Save(this);
        ImGuiOm.HelpMarker(Lang.Get("AutoNumericInputMax-AdjustMaxHelp"));

        if (ModuleConfig.AdjustMaximumValue)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100f * GlobalUIScale);
            if (ImGui.InputInt(Lang.Get("AutoNumericInputMax-MaxValue"), ref ModuleConfig.MaxValue))
                ModuleConfig.MaxValue = Math.Clamp(ModuleConfig.MaxValue, 1, 9999);
            if (ImGui.IsItemDeactivatedAfterEdit())
                ModuleConfig.Save(this);
            ImGuiOm.HelpMarker(Lang.Get("AutoNumericInputMax-MaxValueInputHelp"));
        }
    }

    private static nint UldUpdateDetour(AtkComponentNumericInput* component)
    {
        var result = UldUpdateHook.Original(component);

        if (PluginConfig.Instance().ConflictKeyBinding.IsPressed())
            LastInterruptTime = Environment.TickCount64;

        if (Environment.TickCount64 - LastInterruptTime > 10000)
        {
            if (Throttler.Throttle(nint.Zero, 5000))
            {
                var focusedList = RaptureAtkModule.Instance()->AtkUnitManager->FocusedUnitsList.Entries;
                if (focusedList.Length == 0) goto Out;
                var focusedAddons = focusedList
                                    .ToArray()
                                    .Where(x => x != null && x.Value != null && !string.IsNullOrWhiteSpace(x.Value->NameString))
                                    .Select(x => x.Value->NameString);
                IsBlocked = focusedAddons.Any(BlacklistAddons.Contains);
            }

            if (IsBlocked || !Throttler.Throttle((nint)component, 250)) goto Out;

            var max = component->Data.Max;
            if (component->AtkResNode == null) goto Out;
            if (!component->AtkResNode->NodeFlags.HasFlag(NodeFlags.Visible) || max >= 9999)
                goto Out;

            switch (max)
            {
                case 1:
                    goto Out;
                case 99 when ModuleConfig.AdjustMaximumValue:
                    max = ModuleConfig.MaxValue;
                    break;
            }

            component->Data.Max = max;
            NumericSetValue(component, max, true, false);
            component->Value = max;
            component->AtkTextNode->SetNumber(max);
        }

        Out: ;
        return result;
    }

    private delegate nint UldUpdateDelegate(AtkComponentNumericInput* component);

    private delegate void NumericSetValueDelegate(AtkComponentNumericInput* component, int value, bool a3, bool a4);

    private class Config : ModuleConfig
    {
        public bool AdjustMaximumValue = true;
        public int  MaxValue           = 999;
    }
}
