using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using DailyRoutines.Abstracts;
using Dalamud.Plugin.Services;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DailyRoutines.ModulesPublic;

public unsafe class CustomizeGameObject : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("CustomizeGameObjectTitle"),
        Description = GetLoc("CustomizeGameObjectDescription"),
        Category    = ModuleCategories.System,
        Author      = ["HSS"]
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static Config ModuleConfig = null!;

    private static CustomizeType TypeInput  = CustomizeType.Name;
    private static string        NoteInput  = string.Empty;
    private static float         ScaleInput = 1f;
    private static string        ValueInput = string.Empty;
    private static bool          ScaleVFXInput;

    private static CustomizeType TypeEditInput  = CustomizeType.Name;
    private static string        NoteEditInput  = string.Empty;
    private static float         ScaleEditInput = 1f;
    private static string        ValueEditInput = string.Empty;
    private static bool          ScaleVFXEditInput;

    private static Vector2 CheckboxSize = ScaledVector2(20f);

    private static readonly Dictionary<nint, CustomizeHistoryEntry> CustomizeHistory = [];

    private static CancellationTokenSource? CancelSource;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        CancelSource ??= new();

        FrameworkManager.Reg(OnUpdate, true, 1000);
        DService.ClientState.TerritoryChanged += OnZoneChanged;
    }

    protected override void ConfigUI()
    {
        TargetInfoPreviewUI(DService.Targets.Target);

        var       tableSize = (ImGui.GetContentRegionAvail() - ScaledVector2(100f)) with { Y = 0 };
        using var table     = ImRaii.Table("###ConfigTable", 7, ImGuiTableFlags.BordersInner, tableSize);
        if (!table) return;

        ImGui.TableSetupColumn("启用",   ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
        ImGui.TableSetupColumn("备注",   ImGuiTableColumnFlags.None,       20);
        ImGui.TableSetupColumn("模式",   ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("ModelSkeletonID").X);
        ImGui.TableSetupColumn("值",    ImGuiTableColumnFlags.None,       30);
        ImGui.TableSetupColumn("缩放比例", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("99.99").X);
        ImGui.TableSetupColumn("缩放特效", ImGuiTableColumnFlags.WidthFixed, CheckboxSize.X);
        ImGui.TableSetupColumn("操作",   ImGuiTableColumnFlags.WidthFixed, 6 * CheckboxSize.X);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

        ImGui.TableNextColumn();
        if (ImGuiOm.SelectableIconCentered("AddNewPreset", FontAwesomeIcon.Plus))
            ImGui.OpenPopup("AddNewPresetPopup");

        using (var popup = ImRaii.Popup("AddNewPresetPopup"))
        {
            if (popup)
            {
                CustomizePresetEditorUI(ref TypeInput, ref ValueInput, ref ScaleInput, ref ScaleVFXInput, ref NoteInput);

                ImGui.Spacing();

                var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, 24f * GlobalFontScale);
                if (ImGui.Button(GetLoc("Add"), buttonSize))
                {
                    if (ScaleInput > 0 && !string.IsNullOrWhiteSpace(ValueInput))
                    {
                        ModuleConfig.CustomizePresets.Add(
                            new CustomizePreset
                            {
                                Enabled  = true,
                                Scale    = ScaleInput,
                                Type     = TypeInput,
                                Value    = ValueInput,
                                ScaleVFX = ScaleVFXInput,
                                Note     = NoteInput,
                            });

                        SaveConfig(ModuleConfig);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }

        ImGui.TableNextColumn();
        ImGuiOm.Text(GetLoc("Note"));

        ImGui.TableNextColumn();
        ImGuiOm.Text(GetLoc("CustomizeGameObject-CustomizeType"));

        ImGui.TableNextColumn();
        ImGuiOm.Text(GetLoc("Value"));

        ImGui.TableNextColumn();
        ImGuiOm.Text(GetLoc("CustomizeGameObject-Scale"));

        ImGui.TableNextColumn();
        ImGui.Dummy(new(32f));
        ImGuiOm.TooltipHover(GetLoc("CustomizeGameObject-ScaleVFX"));

        ImGui.TableNextColumn();
        ImGuiOm.Text(GetLoc("Operation"));

        var array = ModuleConfig.CustomizePresets.ToArray();
        for (var i = 0; i < ModuleConfig.CustomizePresets.Count; i++)
        {
            var       preset = array[i];
            using var id     = ImRaii.PushId($"Preset_{i}");

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isEnabled = preset.Enabled;
            if (ImGui.Checkbox("###IsEnabled", ref isEnabled))
            {
                ModuleConfig.CustomizePresets[i].Enabled = isEnabled;
                SaveConfig(ModuleConfig);

                RemovePresetHistory(preset);
            }

            CheckboxSize = ImGui.GetItemRectSize();

            ImGui.TableNextColumn();
            ImGuiOm.Text(preset.Note);

            ImGui.TableNextColumn();
            ImGuiOm.Text(preset.Type.ToString());

            ImGui.TableNextColumn();
            ImGuiOm.Text(preset.Value);

            ImGui.TableNextColumn();
            ImGuiOm.Text(preset.Scale.ToString(CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            var isScaleVFX = preset.ScaleVFX;
            if (ImGui.Checkbox("###IsScaleVFX", ref isScaleVFX))
            {
                ModuleConfig.CustomizePresets[i].ScaleVFX = isScaleVFX;
                SaveConfig(ModuleConfig);

                RemovePresetHistory(preset);
            }

            ImGui.TableNextColumn();
            if (ImGuiOm.ButtonIcon($"EditPreset_{i}", FontAwesomeIcon.Edit))
                ImGui.OpenPopup($"EditNewPresetPopup_{i}");

            using (var popup = ImRaii.Popup($"EditNewPresetPopup_{i}"))
            {
                if (popup)
                {
                    if (ImGui.IsWindowAppearing())
                    {
                        TypeEditInput     = preset.Type;
                        NoteEditInput     = preset.Note;
                        ScaleEditInput    = preset.Scale;
                        ValueEditInput    = preset.Value;
                        ScaleVFXEditInput = preset.ScaleVFX;
                    }

                    if (CustomizePresetEditorUI(ref TypeEditInput,     ref ValueEditInput, ref ScaleEditInput,
                                                ref ScaleVFXEditInput, ref NoteEditInput))
                    {
                        ModuleConfig.CustomizePresets[i].Type     = TypeEditInput;
                        ModuleConfig.CustomizePresets[i].Value    = ValueEditInput;
                        ModuleConfig.CustomizePresets[i].Scale    = ScaleEditInput;
                        ModuleConfig.CustomizePresets[i].ScaleVFX = ScaleVFXEditInput;
                        ModuleConfig.CustomizePresets[i].Note     = NoteEditInput;
                        SaveConfig(ModuleConfig);

                        RemovePresetHistory(preset);
                    }
                }
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon($"DeletePreset_{i}", FontAwesomeIcon.TrashAlt, GetLoc("HoldCtrlToDelete")) &&
                ImGui.IsKeyDown(ImGuiKey.LeftCtrl))
            {
                var keysToRemove = CustomizeHistory
                                   .Where(x => x.Value.Preset == preset)
                                   .Select(x => x.Key)
                                   .ToList();

                foreach (var key in keysToRemove)
                {
                    ResetCustomizeFromHistory(key);
                    CustomizeHistory.Remove(key);
                }

                ModuleConfig.CustomizePresets.Remove(preset);
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon($"ExportPreset_{i}", FontAwesomeIcon.FileExport, GetLoc("ExportToClipboard")))
                ExportToClipboard(preset);

            ImGui.SameLine();
            if (ImGuiOm.ButtonIcon($"ImportPreset_{i}", FontAwesomeIcon.FileImport, GetLoc("ImportFromClipboard")))
            {
                var presetImport = ImportFromClipboard<CustomizePreset>();

                if (presetImport != null && !ModuleConfig.CustomizePresets.Contains(presetImport))
                {
                    ModuleConfig.CustomizePresets.Add(presetImport);
                    SaveConfig(ModuleConfig);
                    array = [.. ModuleConfig.CustomizePresets];
                }
            }

        }
    }

    private static bool CustomizePresetEditorUI(
        ref CustomizeType typeInput,     ref string valueInput, ref float scaleInput,
        ref bool          scaleVFXInput, ref string noteInput)
    {
        var state = false;

        using var table = ImRaii.Table("CustomizeTable", 2, ImGuiTableFlags.None);
        if (!table) return false;

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("真得五个字").X);
        ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthFixed, 300f * GlobalFontScale);

        // 类型
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{GetLoc("CustomizeGameObject-CustomizeType")}:");

        ImGui.TableNextColumn();
        if (ImGui.BeginCombo("###CustomizeTypeSelectCombo", typeInput.ToString()))
        {
            foreach (var mode in Enum.GetValues<CustomizeType>())
            {
                if (ImGui.Selectable(mode.ToString(), mode == typeInput))
                {
                    typeInput = mode;
                    state     = true;
                }
            }

            ImGui.EndCombo();
        }

        // 值
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{GetLoc("Value")}:");

        ImGui.TableNextColumn();
        ImGui.InputText("###CustomizeValueInput", ref valueInput, 128);
        if (ImGui.IsItemDeactivatedAfterEdit()) 
            state = true;
        ImGuiOm.TooltipHover(valueInput);

        // 缩放
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{GetLoc("CustomizeGameObject-Scale")}:");

        ImGui.TableNextColumn();
        ImGui.SliderFloat("###CustomizeScaleSilder", ref scaleInput, 0.1f, 10f, "%.1f");
        if (ImGui.IsItemDeactivatedAfterEdit()) 
            state = true;

        ImGui.SameLine();
        if (ImGui.Checkbox(GetLoc("CustomizeGameObject-ScaleVFX"), ref scaleVFXInput)) 
            state = true;

        // 备注
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text($"{GetLoc("Note")}:");

        ImGui.TableNextColumn();
        ImGui.InputText("###CustomizeNoteInput", ref noteInput, 128);
        if (ImGui.IsItemDeactivatedAfterEdit()) 
            state = true;
        ImGuiOm.TooltipHover(noteInput);

        return state;
    }

    private static void TargetInfoPreviewUI(IGameObject? gameObject)
    {
        if (gameObject is not ICharacter chara)
        {
            ImGui.Text(GetLoc("CustomizeGameObject-NoTaretNotice"));
            return;
        }

        var       tableSize = new Vector2(350f * GlobalFontScale, 0f);
        using var table     = ImRaii.Table("TargetInfoPreviewTable", 2, ImGuiTableFlags.BordersInner, tableSize);
        if (!table) return;

        ImGui.TableSetupColumn("Lable", ImGuiTableColumnFlags.WidthFixed,   ImGui.CalcTextSize("--Model Skeleton ID--").X);
        ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthStretch, 50);

        // Target Name
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{GetLoc("Name")}");

        var targetName = chara.Name.ExtractText();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###TargetNamePreview", ref targetName, 128, ImGuiInputTextFlags.ReadOnly);

        // Data ID
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Data ID");

        var targetDataID = chara.DataID.ToString();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###TargetDataIDPreview", ref targetDataID, 128, ImGuiInputTextFlags.ReadOnly);

        // Object ID
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Object ID");

        var targetObjectID = chara.GameObjectID.ToString();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###TargetObjectIDPreview", ref targetObjectID, 128, ImGuiInputTextFlags.ReadOnly);

        // ModelChara ID
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Model Chara ID");

        var targetModelCharaID = chara.ModelCharaID.ToString();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###TargetModelCharaIDPreview", ref targetModelCharaID, 128, ImGuiInputTextFlags.ReadOnly);

        // ModelChara ID
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Model Skeleton ID");

        var targetSkeletonID = chara.ModelSkeletonID.ToString();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###TargetModelSkeletonIDPreview", ref targetSkeletonID, 128, ImGuiInputTextFlags.ReadOnly);

        if (chara is IPlayerCharacter { CurrentMount: not null })
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Mount Object ID");

            var targetMountObjectID = ((ulong)chara.ToStruct()->Mount.MountObject->GetGameObjectId()).ToString();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("###TargetMountObjectIDPreview", ref targetMountObjectID, 128, ImGuiInputTextFlags.ReadOnly);
        }
    }

    private static void OnUpdate(IFramework framework)
    {
        if (ModuleConfig.CustomizePresets.Count == 0 ||
            BetweenAreas                             || DService.ObjectTable.LocalPlayer == null ||
            DService.PI.UiBuilder.CutsceneActive) return;

        foreach (var obj in DService.ObjectTable)
        {
            if (obj.ObjectKind == ObjectKind.Player && string.IsNullOrWhiteSpace(obj.Name.ExtractText())) continue;
            if (obj is not ICharacter chara) continue;
            
            var pTarget     = chara.ToStruct();
            if (!chara.ToStruct()->IsReadyToDraw()) continue;
            
            var targetAddress = (nint)pTarget;

            var name            = chara.Name.ExtractText();
            var dataID          = chara.DataID.ToString();
            var objectID        = chara.GameObjectID.ToString();
            var modelCharaID    = chara.ModelCharaID.ToString();
            var modelSkeletonID = chara.ModelSkeletonID.ToString();

            foreach (var preset in ModuleConfig.CustomizePresets)
            {
                if (!preset.Enabled) continue;

                var isNeedToReScale = preset.Type switch
                {
                    CustomizeType.Name            => name            == preset.Value,
                    CustomizeType.DataID          => dataID          == preset.Value,
                    CustomizeType.ObjectID        => objectID        == preset.Value,
                    CustomizeType.ModelCharaID    => modelCharaID    == preset.Value,
                    CustomizeType.ModelSkeletonID => modelSkeletonID == preset.Value,
                    _                             => false,
                };

                if (isNeedToReScale)
                {
                    if (CustomizeHistory.TryGetValue(targetAddress, out var historyEntry))
                    {
                        if (pTarget->Scale != historyEntry.CurrentScale)
                            CustomizeHistory.Remove(targetAddress);
                    }

                    if (!CustomizeHistory.ContainsKey(targetAddress))
                    {
                        var modifiedScale = pTarget->Scale * preset.Scale;
                        var entry         = new CustomizeHistoryEntry(preset, pTarget->Scale, modifiedScale);
                        if (CustomizeHistory.TryAdd(targetAddress, entry))
                        {
                            pTarget->Scale = modifiedScale;
                            if (preset.ScaleVFX) 
                                pTarget->VfxScale = modifiedScale;
                            if (preset.Type is CustomizeType.ModelCharaID or CustomizeType.ModelSkeletonID)
                                pTarget->CharacterData.ModelScale = modifiedScale;
                        }

                        pTarget->DisableDraw();
                        pTarget->EnableDraw();
                    }
                }
            }
        }
    }

    private static void OnZoneChanged(ushort zone) => CustomizeHistory.Clear();

    private static void RemovePresetHistory(CustomizePreset? preset)
    {
        var keysToRemove = CustomizeHistory
                           .Where(x => x.Value.Preset == preset)
                           .Select(x => x.Key)
                           .ToList();

        foreach (var key in keysToRemove)
        {
            ResetCustomizeFromHistory(key);
            CustomizeHistory.Remove(key);
        }
    }

    private static void ResetCustomizeFromHistory(nint address)
    {
        if (CustomizeHistory.Count == 0) return;

        if (!CustomizeHistory.TryGetValue(address, out var data)) return;

        var gameObj = (GameObjectStruct*)address;
        if (gameObj == null || !gameObj->IsReadyToDraw()) return;

        gameObj->Scale = data.OrigScale;
        gameObj->VfxScale = data.OrigScale;
        gameObj->DisableDraw();
        gameObj->EnableDraw();
    }

    private static void ResetAllCustomizeFromHistory()
    {
        if (CustomizeHistory.Count == 0) return;

        foreach (var (objectPtr, data) in CustomizeHistory)
        {
            var gameObj = (GameObjectStruct*)objectPtr;
            if (gameObj == null || !gameObj->IsReadyToDraw()) continue;

            gameObj->Scale = data.OrigScale;
            gameObj->VfxScale = data.OrigScale;
            gameObj->DisableDraw();
            gameObj->EnableDraw();
        }
    }

    protected override void Uninit()
    {
        FrameworkManager.Unreg(OnUpdate);
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        
        CancelSource?.Cancel();
        CancelSource?.Dispose();
        CancelSource = null;

        if (DService.ObjectTable.LocalPlayer != null)
            ResetAllCustomizeFromHistory();

        CustomizeHistory.Clear();
    }

    private class CustomizePreset : IEquatable<CustomizePreset>
    {
        public string        Note     { get; set; } = string.Empty;
        public CustomizeType Type     { get; set; }
        public string        Value    { get; set; } = string.Empty;
        public float         Scale    { get; set; }
        public bool          ScaleVFX { get; set; }
        public bool          Enabled  { get; set; }

        public bool Equals(CustomizePreset? other)
        {
            if (other == null) return false;

            return Type == other.Type && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            if (obj is CustomizePreset other) return Equals(other);
            return false;
        }

        public override int GetHashCode() => HashCode.Combine(Type, Value);

        public static bool operator ==(CustomizePreset left, CustomizePreset right) => Equals(left, right);

        public static bool operator !=(CustomizePreset left, CustomizePreset right) => !Equals(left, right);
    }

    private sealed record CustomizeHistoryEntry(CustomizePreset Preset, float OrigScale, float CurrentScale);

    private enum CustomizeType
    {
        Name,
        ModelCharaID,
        ModelSkeletonID,
        DataID,
        ObjectID,
    }

    private class Config : ModuleConfiguration
    {
        public List<CustomizePreset> CustomizePresets = [];
    }
}
