using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using Control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoChangeCharacterRaceSex : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoChangeCharacterRaceSexTitle"),
        Description = Lang.Get("AutoChangeCharacterRaceSexDescription"),
        Category    = ModuleCategory.System
    };

    private const byte MIN_RACE    = 1;
    private const byte MAX_RACE    = 8;
    private const byte MALE_SEX    = 0;
    private const byte FEMALE_SEX  = 1;
    private const int  SEX_COUNT   = 2;
    private const int  LOOKUP_SIZE = (MAX_RACE - MIN_RACE + 1) * SEX_COUNT;

    private static readonly CompSig                       UpdateDrawDataSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 56 57 41 56 48 83 EC 40 45 33 F6 48 8D 59 7A");
    private delegate        byte                          UpdateDrawDataDelegate(DrawDataContainer* data);
    private static          Hook<UpdateDrawDataDelegate>? UpdateDrawDataHook;

    private static Config ModuleConfig = null!;

    private static RaceSexLookup ActiveLookup = RaceSexLookup.Empty;

    private static byte PendingSourceRace = MIN_RACE;
    private static byte PendingSourceSex  = MALE_SEX;
    private static byte PendingTargetRace = MIN_RACE;
    private static byte PendingTargetSex  = FEMALE_SEX;

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        RebuildLookup();

        UpdateDrawDataHook ??= UpdateDrawDataSig.GetHook<UpdateDrawDataDelegate>(UpdateDrawDataDetour);
        UpdateDrawDataHook.Enable();

        if (GameState.IsLoggedIn)
            RerenderAllPlayers();
    }

    protected override void Uninit() =>
        ActiveLookup = RaceSexLookup.Empty;

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(Lang.Get("AutoChangeCharacterRaceSex-SkipLocalPlayer"), ref ModuleConfig.IsSkipLocalPlayer))
            SaveConfigAndRefresh();
        ImGuiOm.HelpMarker(Lang.Get("AutoChangeCharacterRaceSex-SkipLocalPlayer-Help"));

        ImGui.NewLine();

        using (var editorTable = ImRaii.Table("###AutoChangeCharacterRaceSexEditor", 2, ImGuiTableFlags.SizingStretchProp))
        {
            if (editorTable)
            {
                ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 120f * GlobalUIScale);
                ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Lang.Get("AutoChangeCharacterRaceSex-Source"));
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(320f * GlobalUIScale);
                DrawRaceSexCombo("###AutoChangeCharacterRaceSexSource", ref PendingSourceRace, ref PendingSourceSex);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Lang.Get("AutoChangeCharacterRaceSex-Target"));
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(320f * GlobalUIScale);
                DrawRaceSexCombo("###AutoChangeCharacterRaceSexTarget", ref PendingTargetRace, ref PendingTargetSex);
            }
        }

        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Plus, Lang.Get("AutoChangeCharacterRaceSex-AddOrUpdate")))
            AddOrUpdatePendingRule();
        ImGuiOm.HelpMarker(Lang.Get("AutoChangeCharacterRaceSex-AddOrUpdateHelp"));

        ImGui.SameLine(0, 10f * GlobalUIScale);

        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.SyncAlt, Lang.Get("Refresh")))
        {
            RebuildLookup();
            RerenderAllPlayers();
        }

        ImGui.Spacing();

        if (ModuleConfig.Mappings.Count == 0)
            return;

        using (var ruleTable = ImRaii.Table
               (
                   "###AutoChangeCharacterRaceSexRules",
                   4,
                   ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                   new(0f, 240f * GlobalUIScale)
               ))
        {
            if (ruleTable)
            {
                ImGui.TableSetupColumn($"##{Lang.Get("Enabled")}",                    ImGuiTableColumnFlags.WidthFixed,   ImGui.GetTextLineHeightWithSpacing());
                ImGui.TableSetupColumn(Lang.Get("AutoChangeCharacterRaceSex-Source"), ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn(Lang.Get("AutoChangeCharacterRaceSex-Target"), ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn($"##{Lang.Get("Operation")}",                  ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableHeadersRow();

                for (var i = 0; i < ModuleConfig.Mappings.Count; i++)
                {
                    var mapping = ModuleConfig.Mappings[i];

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    var enabled = mapping.Enabled;

                    if (ImGui.Checkbox($"###AutoChangeCharacterRaceSexEnabled{i}", ref enabled))
                    {
                        mapping.Enabled = enabled;
                        SaveConfigAndRefresh();
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(GetRaceSexDisplayName(mapping.SourceRace, mapping.SourceSex));

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(GetRaceSexDisplayName(mapping.TargetRace, mapping.TargetSex));

                    ImGui.TableNextColumn();

                    using (ImRaii.PushId($"AutoChangeCharacterRaceSexDelete{i}"))
                    {
                        if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.TrashAlt, Lang.Get("Delete")))
                        {
                            ModuleConfig.Mappings.RemoveAt(i);
                            SaveConfigAndRefresh();
                            break;
                        }
                    }
                }
            }
        }
    }

    private static byte UpdateDrawDataDetour(DrawDataContainer* data)
    {
        if (data              == null ||
            data->OwnerObject == null)
            return UpdateDrawDataHook.Original(data);

        var lookup = Volatile.Read(ref ActiveLookup);
        if (!lookup.HasAnyMapping)
            return UpdateDrawDataHook.Original(data);

        var sourceRace = data->CustomizeData.Race;
        var sourceSex  = data->CustomizeData.Sex;

        if (!TryGetTarget(lookup, sourceRace, sourceSex, out var targetRace, out var targetSex) ||
            targetRace == sourceRace && targetSex == sourceSex)
            return UpdateDrawDataHook.Original(data);

        var localPlayer               = Control.GetLocalPlayer();
        var restoreLocalCustomizeData = ModuleConfig.IsSkipLocalPlayer && localPlayer != null;

        Unsafe.SkipInit(out CustomizeData localCustomizeData);
        if (restoreLocalCustomizeData)
            localCustomizeData = localPlayer->DrawData.CustomizeData;

        data->CustomizeData.Race = targetRace;
        data->CustomizeData.Sex  = targetSex;
        data->CustomizeData.Normalize(&data->CustomizeData);

        var result = UpdateDrawDataHook.Original(data);

        if (restoreLocalCustomizeData)
            localPlayer->DrawData.CustomizeData = localCustomizeData;

        return result;
    }

    private void AddOrUpdatePendingRule()
    {
        var existingIndex = FindRuleIndex(PendingSourceRace, PendingSourceSex);

        if (existingIndex >= 0)
        {
            var existing = ModuleConfig.Mappings[existingIndex];
            existing.TargetRace = PendingTargetRace;
            existing.TargetSex  = PendingTargetSex;
            existing.Enabled    = true;
        }
        else
        {
            ModuleConfig.Mappings.Add
            (
                new()
                {
                    Enabled    = true,
                    SourceRace = PendingSourceRace,
                    SourceSex  = PendingSourceSex,
                    TargetRace = PendingTargetRace,
                    TargetSex  = PendingTargetSex
                }
            );
        }

        SaveConfigAndRefresh();
    }

    private static int FindRuleIndex(byte sourceRace, byte sourceSex)
    {
        var mappings = CollectionsMarshal.AsSpan(ModuleConfig.Mappings);

        for (var i = 0; i < mappings.Length; i++)
        {
            ref var mapping = ref mappings[i];
            if (mapping.SourceRace == sourceRace && mapping.SourceSex == sourceSex)
                return i;
        }

        return -1;
    }

    private void SaveConfigAndRefresh()
    {
        RebuildLookup();
        ModuleConfig.Save(this);
        RerenderAllPlayers();
    }

    private static void RebuildLookup()
    {
        var nextLookup = new RaceSexLookup();
        var mappings   = CollectionsMarshal.AsSpan(ModuleConfig.Mappings);

        foreach (ref readonly var mapping in mappings)
        {
            if (!mapping.Enabled) continue;

            var sourceIndex = TryGetLookupIndex(mapping.SourceRace, mapping.SourceSex);
            if (sourceIndex < 0) continue;

            if (!IsValidRace(mapping.TargetRace) || !IsValidSex(mapping.TargetSex))
                continue;

            nextLookup.ActiveMask                 |= (ushort)(1 << sourceIndex);
            nextLookup.PackedTargets[sourceIndex] =  PackRaceSex(mapping.TargetRace, mapping.TargetSex);
        }

        Volatile.Write(ref ActiveLookup, nextLookup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetTarget(RaceSexLookup lookup, byte sourceRace, byte sourceSex, out byte targetRace, out byte targetSex)
    {
        var index = TryGetLookupIndex(sourceRace, sourceSex);

        if (index >= 0 && (lookup.ActiveMask & 1 << index) != 0)
            return TryUnpackRaceSex(lookup.PackedTargets[index], out targetRace, out targetSex);

        targetRace = 0;
        targetSex  = 0;
        return false;
    }

    private static bool DrawRaceSexCombo(string id, ref byte race, ref byte sex)
    {
        var changed = false;

        using var combo = ImRaii.Combo(id, GetRaceSexDisplayName(race, sex));
        if (!combo)
            return false;

        for (var currentRace = MIN_RACE; currentRace <= MAX_RACE; currentRace++)
        for (var currentSex = MALE_SEX; currentSex <= FEMALE_SEX; currentSex++)
        {
            var isSelected = currentRace == race && currentSex == sex;
            var label      = $"{GetRaceSexDisplayName(currentRace, currentSex)}###{id}_{currentRace}_{currentSex}";

            if (!ImGui.Selectable(label, isSelected))
                continue;

            race    = currentRace;
            sex     = currentSex;
            changed = true;
        }

        return changed;
    }

    private static string GetRaceSexDisplayName(byte race, byte sex)
    {
        var raceName = GetRaceName(race, sex);
        return $"{raceName} / {GetSexName(sex)}";
    }

    private static string GetRaceName(byte race, byte sex)
    {
        if (!LuminaGetter.TryGetRow(race, out Race raceRow))
            return Lang.Get("Unknown");

        return sex == FEMALE_SEX
                   ? raceRow.Feminine.ToString()  ?? Lang.Get("Unknown")
                   : raceRow.Masculine.ToString() ?? Lang.Get("Unknown");
    }

    private static string GetSexName(byte sex) =>
        sex == FEMALE_SEX
            ? LuminaWrapper.GetAddonText(15609)
            : LuminaWrapper.GetAddonText(15608);

    private static void RerenderAllPlayers()
    {
        foreach (var obj in DService.Instance().ObjectTable.OfType<ICharacter>())
        {
            var player = obj.ToStruct();
            if (player == null || !player->IsReadyToDraw()) continue;

            player->DisableDraw();
            player->EnableDraw();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TryGetLookupIndex(byte race, byte sex)
    {
        var raceOffset = (uint)(race - MIN_RACE);

        if (raceOffset > MAX_RACE - MIN_RACE || sex > FEMALE_SEX)
            return -1;

        return (int)(raceOffset << 1 | sex);
    }

    private static bool IsValidRace(byte race) =>
        race is >= MIN_RACE and <= MAX_RACE;

    private static bool IsValidSex(byte sex) =>
        sex is MALE_SEX or FEMALE_SEX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PackRaceSex(byte race, byte sex) =>
        (byte)(race | sex << 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryUnpackRaceSex(byte packed, out byte race, out byte sex)
    {
        if (packed == 0)
        {
            race = 0;
            sex  = 0;
            return false;
        }

        race = (byte)(packed & 0x0F);
        sex  = (byte)(packed >> 4);
        return true;
    }

    private sealed class Config : ModuleConfig
    {
        public List<RaceSexMapping> Mappings = [];

        public bool IsSkipLocalPlayer = true;
    }

    private sealed class RaceSexMapping
    {
        public bool Enabled = true;

        public byte SourceRace = MIN_RACE;

        public byte SourceSex = MALE_SEX;

        public byte TargetRace = MIN_RACE;

        public byte TargetSex = FEMALE_SEX;
    }

    private sealed class RaceSexLookup
    {
        public static readonly RaceSexLookup Empty = new();

        public ushort            ActiveMask;
        public PackedTargetTable PackedTargets;

        public bool HasAnyMapping
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ActiveMask != 0;
        }
    }

    [InlineArray(LOOKUP_SIZE)]
    private struct PackedTargetTable
    {
        private byte element0;
    }
}
