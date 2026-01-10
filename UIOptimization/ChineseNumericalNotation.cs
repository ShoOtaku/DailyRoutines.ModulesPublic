using DailyRoutines.Abstracts;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class ChineseNumericalNotation : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("ChineseNumericalNotationTitle"),
        Description = GetLoc("ChineseNumericalNotationDescription"),
        Category    = ModuleCategories.UIOptimization
    };

    public override ModulePermission Permission { get; } = new() { CNDefaultEnabled = true, TCDefaultEnabled = true };

    // 千分位转万分位
    private static readonly MemoryPatch AtkTextNodeSetNumberCommaPatch = new(
        "B8 ?? ?? ?? ?? F7 E1 D1 EA 8D 04 52 2B C8 83 F9 ?? 75 ?? 41 0F B6 D0 48 8D 8F",
        [
            // mov eax, 0AAAAAAABh
            0x83, 0xE1, 0x03, // and ecx, 3
            0x90, 0x90,       // nop, nop
            // all nop
            0x90, 0x90, 0x90, 0x90, 0x90,
            0x90, 0x90, 0x90, 0x90
        ]);
    
    private static readonly CompSig                     FormatNumberSig = new("E8 ?? ?? ?? ?? 44 3B F7");
    private delegate        Utf8String*                 FormatNumberDelegate(Utf8String* outNumberString, int number, int baseNumber, int mode, void* seperator);
    private static          Hook<FormatNumberDelegate>? FormatNumberHook;

    private static readonly CompSig AtkCounterNodeSetNumberSig =
        new("40 53 48 83 EC ?? 48 8B C2 48 8B D9 48 85 C0");
    private delegate void AtkCounterNodeSetNumberDelegate(AtkCounterNode* node, CStringPointer number);
    private static Hook<AtkCounterNodeSetNumberDelegate>? AtkCounterNodeSetNumberHook;

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        AtkTextNodeSetNumberCommaPatch.Enable();

        AtkCounterNodeSetNumberHook ??= AtkCounterNodeSetNumberSig.GetHook<AtkCounterNodeSetNumberDelegate>(AtkCounterNodeSetNumberDetour);
        AtkCounterNodeSetNumberHook.Enable();
        
        FormatNumberHook ??= FormatNumberSig.GetHook<FormatNumberDelegate>(FormatNumberDetour);
        FormatNumberHook.Enable();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("ChineseNumericalNotation-NoChineseUnit"), ref ModuleConfig.NoChineseUnit))
            SaveConfig(ModuleConfig);

        if (!ModuleConfig.NoChineseUnit)
        {
            if (ImGui.Checkbox(GetLoc("Dye"), ref ModuleConfig.ColoringUnit))
                SaveConfig(ModuleConfig);

            if (ModuleConfig.ColoringUnit)
            {
                using (ImRaii.Group())
                {
                    if (!LuminaGetter.TryGetRow<UIColor>(ModuleConfig.ColorMinus, out var minusColorRow))
                    {
                        ModuleConfig.ColorMinus = 17;
                        ModuleConfig.Save(this);
                        return;
                    }

                    ImGui.ColorButton("###ColorButtonMinus", minusColorRow.ToVector4());

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalFontScale);
                    if (ImGui.InputUShort(GetLoc("ChineseNumericalNotation-ColorMinus"), ref ModuleConfig.ColorMinus, 1, 1))
                        SaveConfig(ModuleConfig);
                }
                
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                
                ImGui.SameLine();
                using (ImRaii.Group())
                {
                    if (!LuminaGetter.TryGetRow<UIColor>(ModuleConfig.ColorUnit, out var unitColorRow))
                    {
                        ModuleConfig.ColorUnit = 17;
                        ModuleConfig.Save(this);
                        return;
                    }

                    ImGui.ColorButton("###ColorButtonUnit", unitColorRow.ToVector4());

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200f * GlobalFontScale);
                    if (ImGui.InputUShort(GetLoc("ChineseNumericalNotation-ColorUnit"), ref ModuleConfig.ColorUnit, 1, 1))
                        SaveConfig(ModuleConfig);
                }

                var sheet = LuminaGetter.Get<UIColor>();
                using (var node = ImRaii.TreeNode(GetLoc("ChineseNumericalNotation-ColorTable")))
                {
                    if (node)
                    {
                        using var table = ImRaii.Table("###ColorTable", 6);
                        if (!table) return;
                        
                        var counter = 0;
                        foreach (var row in sheet)
                        {
                            if (row.RowId == 0) continue;
                            if (row.Dark  == 0) continue;

                            if (counter % 5 == 0) 
                                ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                                    
                            counter++;

                            using (ImRaii.Group())
                            {
                                ImGui.ColorButton($"###ColorButtonTable{row.RowId}", row.ToVector4());
                                        
                                ImGui.SameLine();
                                ImGui.TextUnformatted($"{row.RowId}");
                            }
                        }
                    }
                }
            }
        }
    }

    protected override void Uninit() => 
        AtkTextNodeSetNumberCommaPatch.Dispose();

    private static Utf8String* FormatNumberDetour(Utf8String* outNumberString, int number, int baseNumber, int mode, void* seperator)
    {
        var ret = FormatNumberHook.Original(outNumberString, number, baseNumber, mode, seperator);
        
        if (baseNumber % 10 == 0)
        {
            switch (mode)
            {
                // 千分位分隔
                case 1:
                {
                    var minusColor = ModuleConfig.ColoringUnit ? ModuleConfig.ColorMinus : (ushort?)null;
                    var unitColor  = ModuleConfig.ColoringUnit ? ModuleConfig.ColorUnit : (ushort?)null;

                    var formatted = !ModuleConfig.NoChineseUnit
                                        ? number.ToChineseSeString(minusColor, unitColor)
                                        : number.ToMyriadString();

                    outNumberString->SetString(formatted.ToDalamudString().EncodeWithNullTerminator());
                    return outNumberString;
                }
                case 2 or 3 or 4 or 5:
                    break;
                // 纯数字
                default:
                {
                    var formatted = number.ToMyriadString();
                    
                    outNumberString->SetString(new SeString(new TextPayload(formatted)).EncodeWithNullTerminator());
                    return outNumberString;
                }
            }
        }

        return ret;
    }

    private static void AtkCounterNodeSetNumberDetour(AtkCounterNode* node, CStringPointer number)
    {
        if (!ModuleConfig.NoChineseUnit           &&
            number.HasValue                       &&
            number.ExtractText() is var textValue &&
            textValue.IsAnyChinese())
        {
            node->SetText(textValue.FromChineseString<int>().ToMyriadString());
            node->UpdateWidth();
            return;
        }

        AtkCounterNodeSetNumberHook.Original(node, number);
    }

    private class Config : ModuleConfiguration
    {
        public bool   NoChineseUnit;
        public bool   ColoringUnit;
        public ushort ColorUnit  = 25;
        public ushort ColorMinus = 17;
    }
}
