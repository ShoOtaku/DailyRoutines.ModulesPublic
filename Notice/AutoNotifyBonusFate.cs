using System.Collections.Generic;
using System.Linq;
using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public class AutoNotifyBonusFate : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoNotifyBonusFateTitle"),
        Description = GetLoc("AutoNotifyBonusFateDescription"),
        Category    = ModuleCategories.Notice,
        Author      = ["Due"]
    };

    private static readonly HashSet<uint> ValidTerritories =
        LuminaGetter.Get<TerritoryType>()
                    .Where(x => x.TerritoryIntendedUse.RowId == 1)
                    .Where(x => x.ExVersion.Value.RowId      >= 2)
                    .Select(x => x.RowId)
                    .ToHashSet();

    private static Config ModuleConfig = null!;

    private static readonly HashSet<ushort> NotifiedFates = [];

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new();

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        ExecuteCommandManager.Instance().Unreg(OnPostExecuteCommand);

        NotifiedFates.Clear();
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
            SaveConfig(ModuleConfig);

        if (ImGui.Checkbox(GetLoc("SendTTS"), ref ModuleConfig.SendTTS))
            SaveConfig(ModuleConfig);

        ImGui.NewLine();

        if (ImGui.Checkbox(GetLoc("OpenMap"), ref ModuleConfig.AutoOpenMap))
            SaveConfig(ModuleConfig);
    }

    private void OnZoneChanged(ushort zone)
    {
        ExecuteCommandManager.Instance().Unreg(OnPostExecuteCommand);
        TaskHelper.Abort();
        NotifiedFates.Clear();

        if (!ValidTerritories.Contains(GameState.TerritoryType)) return;

        ExecuteCommandManager.Instance().RegPost(OnPostExecuteCommand);
    }

    private void OnPostExecuteCommand(ExecuteCommandFlag command, uint param1, uint param2, uint param3, uint param4)
    {
        if (command != ExecuteCommandFlag.FateLoad) return;

        TaskHelper.Abort();
        TaskHelper.DelayNext(200);
        TaskHelper.Enqueue(UpdateAndNotify);
    }

    private static void UpdateAndNotify()
    {
        if (!ValidTerritories.Contains(GameState.TerritoryType) || GameState.Map == 0) return;

        var fateTable = DService.Instance().Fate;

        if (fateTable.Length == 0)
        {
            if (NotifiedFates.Count > 0) NotifiedFates.Clear();
            return;
        }

        var currentBonusFates = new HashSet<ushort>();

        foreach (var fate in fateTable)
        {
            if (!fate.HasBonus) continue;

            currentBonusFates.Add(fate.FateId);

            if (NotifiedFates.Add(fate.FateId))
                NotifyFate(fate);
        }

        if (NotifiedFates.Count > 0)
            NotifiedFates.IntersectWith(currentBonusFates);
    }

    private static unsafe void NotifyFate(IFate fate)
    {
        var mapPos = WorldToMap(fate.Position.ToVector2(), GameState.MapData);

        var chatMessage = GetSLoc
        (
            "AutoNotifyBonusFate-Chat",
            fate.Name.ToString(),
            fate.Progress,
            SeString.CreateMapLink(GameState.TerritoryType, GameState.Map, mapPos.X, mapPos.Y)
        );
        var notificationMessage = GetLoc("AutoNotifyBonusFate-Notification", fate.Name.ToString(), fate.Progress);

        if (ModuleConfig.SendChat)
            Chat(chatMessage);
        if (ModuleConfig.SendNotification)
            NotificationInfo(notificationMessage);
        if (ModuleConfig.SendTTS)
            Speak(notificationMessage);

        if (ModuleConfig.AutoOpenMap)
        {
            var instance = AgentMap.Instance();
            if (instance == null) return;

            var currentZoneMapID = instance->CurrentMapId;
            instance->SelectedMapId = currentZoneMapID;

            if (!instance->IsAgentActive())
                instance->Show();

            instance->SetFlagMapMarker(GameState.TerritoryType, currentZoneMapID, fate.Position);
            instance->OpenMap(currentZoneMapID, GameState.TerritoryType, fate.Name.ToString());
        }
    }

    private class Config : ModuleConfiguration
    {
        public bool SendChat;
        public bool SendNotification = true;
        public bool SendTTS          = true;
        public bool AutoOpenMap      = true;
    }
}
