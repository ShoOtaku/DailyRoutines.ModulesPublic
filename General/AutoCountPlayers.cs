using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCountPlayers : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoCountPlayersTitle"),
        Description = GetLoc("AutoCountPlayersDescription"),
        Category    = ModuleCategories.General
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private const ImGuiWindowFlags WINDOW_FLAGS = ImGuiWindowFlags.NoScrollbar           |
                                                  ImGuiWindowFlags.AlwaysAutoResize      |
                                                  ImGuiWindowFlags.NoTitleBar            |
                                                  ImGuiWindowFlags.NoBackground          |
                                                  ImGuiWindowFlags.NoBringToFrontOnFocus |
                                                  ImGuiWindowFlags.NoFocusOnAppearing    |
                                                  ImGuiWindowFlags.NoNavFocus            |
                                                  ImGuiWindowFlags.NoDocking             |
                                                  ImGuiWindowFlags.NoMove                |
                                                  ImGuiWindowFlags.NoResize              |
                                                  ImGuiWindowFlags.NoScrollWithMouse     |
                                                  ImGuiWindowFlags.NoInputs;

    private static readonly uint LineColorBlue = KnownColor.LightSkyBlue.ToUInt();
    private static readonly uint LineColorRed  = KnownColor.Red.ToUInt();
    private static readonly uint DotColor      = KnownColor.RoyalBlue.ToUInt();

    private delegate void InfoProxy24EndRequestDelegate(InfoProxy24* instance);
    private static Hook<InfoProxy24EndRequestDelegate> InfoProxy24EndRequestHook;

    private static Config        ModuleConfig = null!;
    private static IDtrBarEntry? Entry;

    private static readonly Dictionary<uint, byte[]> JobIcons = [];
    private static readonly Dictionary<uint, PlayerTargetingInfo> LastTargetingData = [];

    private static string SearchInput = string.Empty;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        Overlay            ??= new(this);
        Overlay.Flags      &=  ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags      &=  ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.WindowName =   $"{GetLoc("AutoCountPlayers-PlayersAroundInfo")}###AutoCountPlayers-Overlay";

        Entry         ??= DService.Instance().DTRBar.Get("DailyRoutines-AutoCountPlayers");
        Entry.Shown   =   true;
        Entry.Text    =   $"{GetLoc("AutoCountPlayers-PlayersAroundCount")}: 0";
        Entry.OnClick +=  _ => Overlay.IsOpen ^= true;

        WindowManager.Draw += OnDraw;

        PlayersManager.ReceivePlayersAround      += OnReceivePlayers;
        PlayersManager.ReceivePlayersTargetingMe += OnPlayersTargetingMeUpdate;

        var instance = (InfoProxy24*)InfoModule.Instance();
        InfoProxy24EndRequestHook ??= instance->VirtualTable->HookVFuncFromName
        (
            "EndRequest",
            (InfoProxy24EndRequestDelegate)InfoProxy24EndRequestDetour
        );
        InfoProxy24EndRequestHook.Enable();

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        FrameworkManager.Instance().Reg(OnUpdate, 10_000);
        OnUpdate(DService.Instance().Framework);
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;

        FrameworkManager.Instance().Unreg(OnUpdate);

        WindowManager.Draw                       -= OnDraw;
        PlayersManager.ReceivePlayersAround      -= OnReceivePlayers;
        PlayersManager.ReceivePlayersTargetingMe -= OnPlayersTargetingMeUpdate;

        foreach (var info in LastTargetingData.Values)
        {
            var duration = DateTime.Now - info.TargetingStartTime;
            ModuleConfig.TargetingHistories.Add
            (
                new()
                {
                    Name        = info.Player.Name.ToString(),
                    HomeWorldID = info.Player.HomeWorld.RowId,
                    JobID       = info.Player.ClassJob.RowId,
                    StartTime   = info.TargetingStartTime,
                    Duration    = duration,
                    ZoneID      = GameState.TerritoryType
                }
            );
        }
        
        if (LastTargetingData.Count > 0)
        {
            LastTargetingData.Clear();
            if (ModuleConfig.TargetingHistories.Count > 100)
                ModuleConfig.TargetingHistories.RemoveRange(0, ModuleConfig.TargetingHistories.Count - 100);
            ModuleConfig.Save(this);
        }

        Entry?.Remove();
        Entry = null;
    }

    protected override void ConfigUI()
    {
        ImGui.SetNextItemWidth(120f * GlobalFontScale);
        if (ImGui.InputFloat(GetLoc("Scale"), ref ModuleConfig.ScaleFactor, 0, 0, "%.1f"))
            ModuleConfig.ScaleFactor = Math.Max(0.1f, ModuleConfig.ScaleFactor);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ModuleConfig.Save(this);

        ImGui.NewLine();

        if (ImGui.Checkbox(GetLoc("AutoCountPlayers-DisplayLineWhenTargetingMe"), ref ModuleConfig.DisplayLineWhenTargetingMe))
            ModuleConfig.Save(this);

        if (ModuleConfig.DisplayLineWhenTargetingMe)
        {
            using (ImRaii.PushIndent())
            {
                if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
                    ModuleConfig.Save(this);

                if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
                    ModuleConfig.Save(this);

                if (ImGui.Checkbox(GetLoc("SendTTS"), ref ModuleConfig.SendTTS))
                    ModuleConfig.Save(this);

                if (ModuleConfig.SendNotification || ModuleConfig.SendTTS)
                {
                    using (ImRaii.PushIndent())
                    {
                        if (ImGui.Checkbox(GetLoc("AutoCountPlayers-FilterFriend"), ref ModuleConfig.FilterFriend))
                            ModuleConfig.Save(this);
                    }
                }
            }
        }

        
    }

    protected override void OverlayUI()
    {
        using var tabBar = ImRaii.TabBar("##Tab");
        if (!tabBar) return;

        using (var item = ImRaii.TabItem(GetLoc("AutoCountPlayers-PlayersAround")))
        {
            if (item)
            {
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText("###Search", ref SearchInput, 128);

                if (BetweenAreas) return;

                using var child = ImRaii.Child("列表", ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing, true);
                if (!child) return;

                foreach (var playerAround in PlayersManager.PlayersAround)
                {
                    using var id = ImRaii.PushId($"{playerAround.GameObjectID}");
                    
                    if (!string.IsNullOrWhiteSpace(SearchInput) && !playerAround.Name.ToString().Contains(SearchInput)) continue;

                    if (ImGuiOm.ButtonIcon("定位", FontAwesomeIcon.Flag, GetLoc("Locate")))
                    {
                        var mapPos = WorldToMap(playerAround.Position.ToVector2(), GameState.MapData);
                        var message = new SeStringBuilder()
                                      .Add
                                      (
                                          new PlayerPayload
                                          (
                                              playerAround.Name.TextValue,
                                              playerAround.ToStruct()->HomeWorld
                                          )
                                      )
                                      .Append(" (")
                                      .AddIcon(playerAround.ClassJob.Value.ToBitmapFontIcon())
                                      .Append($" {playerAround.ClassJob.Value.Name})")
                                      .Add(new NewLinePayload())
                                      .Append("     ")
                                      .Append(SeString.CreateMapLink(GameState.TerritoryType, GameState.Map, mapPos.X, mapPos.Y))
                                      .Build();
                        Chat(message);
                    }

                    if (DService.Instance().GameGUI.WorldToScreen(playerAround.Position,            out var screenPos) &&
                        DService.Instance().GameGUI.WorldToScreen(LocalPlayerState.Object.Position, out var localScreenPos))
                    {
                        if (!ImGui.IsAnyItemHovered() || ImGui.IsItemHovered())
                            DrawLine(localScreenPos, screenPos, playerAround);
                    }


                    ImGui.SameLine();
                    ImGui.TextUnformatted($"{playerAround.Name} ({playerAround.ClassJob.Value.Name})");
                }
            }
        }
        
        using (var item = ImRaii.TabItem(GetLoc("AutoCountPlayers-TargetedHistory")))
        {
            if (item)
            {
                foreach (var record in ModuleConfig.TargetingHistories.AsEnumerable().Reverse())
                {
                    ImGui.TextDisabled($"{record.StartTime:MM/dd HH:mm:ss}");
                        
                    ImGui.SameLine();
                    var jobIcon = JobIcons.GetOrAdd
                    (
                        record.JobID,
                        _ => new SeStringBuilder().AddIcon(record.JobID.ToLuminaRowRef<ClassJob>().Value.ToBitmapFontIcon()).Encode()
                    );
                    ImGuiHelpers.SeStringWrapped(jobIcon);

                    ImGui.SameLine();
                    ImGui.TextUnformatted($"{record.Name}@{LuminaWrapper.GetWorldName(record.HomeWorldID)}");
                        
                    ImGui.SameLine();
                    ImGui.TextColored(KnownColor.Orange.ToVector4(), $"[{record.Duration:mm\\:ss}]");
                    
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({LuminaWrapper.GetZonePlaceName(record.ZoneID)})");
                }
            }
        }
    }

    private static void OnDraw()
    {
        if (!ModuleConfig.DisplayLineWhenTargetingMe || PlayersManager.PlayersTargetingMe.Count == 0) return;

        if (!GameState.IsForeground) return;

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return;

        if (NamePlate->IsAddonAndNodesReady())
        {
            var node = NamePlate->GetNodeById(1);

            if (node != null)
            {
                var nodeState = node->GetNodeState();

                if (ImGui.Begin($"AutoCountPlayers-{localPlayer->EntityId}", WINDOW_FLAGS))
                {
                    ImGui.SetWindowPos(nodeState.Center - ImGui.GetWindowSize() * 0.75f);

                    using (FontManager.Instance().UIFont140.Push())
                    using (ImRaii.Group())
                    {
                        ImGuiHelpers.SeStringWrapped(new SeStringBuilder().AddIcon(BitmapFontIcon.Warning).Encode());

                        ImGui.SameLine();
                        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 1.2f * GlobalFontScale);
                        ImGuiOm.TextOutlined(KnownColor.Orange.ToVector4(), $"{PlayersManager.PlayersTargetingMe.Count}", KnownColor.SaddleBrown.ToVector4());

                        if (GameState.ContentFinderCondition == 0)
                        {
                            using (FontManager.Instance().UIFont80.Push())
                            {
                                var text = GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe");
                                ImGuiOm.TextOutlined
                                (
                                    ImGui.GetCursorScreenPos() - new Vector2(ImGui.CalcTextSize(text).X * 0.3f, 0),
                                    KnownColor.Orange.ToVector4().ToUInt(),
                                    $"({text})",
                                    KnownColor.SaddleBrown.ToVector4().ToUInt()
                                );
                            }
                        }
                    }

                    ImGui.End();
                }
            }
        }

        var currentWindowSize = ImGui.GetMainViewport().Size;
        if (!DService.Instance().GameGUI.WorldToScreen(localPlayer->Position, out var localScreenPos))
            localScreenPos = currentWindowSize with { X = currentWindowSize.X / 2 };

        foreach (var playerInfo in PlayersManager.PlayersTargetingMe)
        {
            if (DService.Instance().GameGUI.WorldToScreen(playerInfo.Player.Position, out var screenPos))
                DrawLine(localScreenPos, screenPos, playerInfo.Player, LineColorRed, $" [{TimeSpan.FromSeconds(playerInfo.TargetingDurationSeconds)}]");
        }
    }

    private static void OnZoneChanged(ushort obj)
    {
        FrameworkManager.Instance().Unreg(OnUpdate);

        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.OccultCrescent ||
            AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentMemberList)->IsAgentActive())
            return;

        FrameworkManager.Instance().Reg(OnUpdate, 30_000);
    }

    private static void OnUpdate(IFramework framework)
    {
        if (GameState.TerritoryIntendedUse != TerritoryIntendedUse.OccultCrescent ||
            AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentMemberList)->IsAgentActive())
        {
            FrameworkManager.Instance().Unreg(OnUpdate);
            return;
        }

        var proxy = (InfoProxy24*)InfoModule.Instance()->GetInfoProxyById((InfoProxyId)24);
        if (proxy == null) return;

        AgentId.ContentMemberList.SendEvent(0, 1);
    }

    private void OnReceivePlayers(IReadOnlyList<IPlayerCharacter> characters)
    {
        if (Entry == null) return;

        // 新月岛
        if (GameState.TerritoryIntendedUse == TerritoryIntendedUse.OccultCrescent)
            Entry.Shown = true;
        else
            Entry.Shown = !DService.Instance().Condition[ConditionFlag.InCombat] || GameState.IsInPVPArea;

        if (!Entry.Shown)
        {
            Overlay.IsOpen = false;
            return;
        }

        Entry.Text = $"{GetLoc("AutoCountPlayers-PlayersAroundCount")}: {PlayersManager.PlayersAroundCount}" +
                     (PlayersManager.PlayersTargetingMe.Count == 0 ? string.Empty : $" ({PlayersManager.PlayersTargetingMe.Count})");

        // 新月岛
        if (GameState.TerritoryIntendedUse == TerritoryIntendedUse.OccultCrescent)
        {
            Entry.Text.Append
            (
                $" / {GetLoc("AutoCountPlayers-PlayersZoneCount")}: " +
                $"{((InfoProxy24*)InfoModule.Instance()->GetInfoProxyById((InfoProxyId)24))->EntryCount}"
            );
        }

        if (characters.Count == 0)
        {
            Entry.Tooltip = string.Empty;
            return;
        }

        var tooltip = new SeStringBuilder();

        if (PlayersManager.PlayersTargetingMe.Count > 0)
        {
            tooltip.AddUiForeground(32)
                   .AddText($"{GetLoc("AutoCountPlayers-PlayersTargetingMe")}")
                   .AddUiForegroundOff()
                   .Add(NewLinePayload.Payload);

            PlayersManager.PlayersTargetingMe.ForEach
            (info =>
                 tooltip.AddText($"{info.Player.Name} (")
                        .AddIcon(info.Player.ClassJob.Value.ToBitmapFontIcon())
                        .AddText($"{info.Player.ClassJob.Value.Name.ToString()})")
                        .Add(NewLinePayload.Payload)
            );
        }

        tooltip.AddUiForeground(32)
               .AddText($"{GetLoc("AutoCountPlayers-PlayersAroundInfo")}")
               .AddUiForegroundOff()
               .Add(NewLinePayload.Payload);

        characters.ForEach
        (info => tooltip.AddText($"{info.Name} (")
                        .AddIcon(info.ClassJob.Value.ToBitmapFontIcon())
                        .AddText($"{info.ClassJob.Value.Name.ToString()})")
                        .Add(NewLinePayload.Payload)
        );

        var message = tooltip.Build();
        if (message.Payloads.Last() is NewLinePayload)
            message.Payloads.RemoveAt(message.Payloads.Count - 1);

        Entry.Tooltip = message;
    }

    private void OnPlayersTargetingMeUpdate(IReadOnlyList<PlayerTargetingInfo> targetingPlayersInfo)
    {
        var currentIds     = targetingPlayersInfo.Select(x => x.Player.EntityID).ToHashSet();
        var endedTargeting = LastTargetingData.Where(x => !currentIds.Contains(x.Key)).ToList();

        if (endedTargeting.Count > 0)
        {
            foreach (var (key, info) in endedTargeting)
            {
                var duration = DateTime.Now - info.TargetingStartTime;

                ModuleConfig.TargetingHistories.Add
                (
                    new()
                    {
                        Name        = info.Player.Name.ToString(),
                        HomeWorldID = info.Player.HomeWorld.RowId,
                        JobID       = info.Player.ClassJob.RowId,
                        StartTime   = info.TargetingStartTime,
                        Duration    = duration,
                        ZoneID      = GameState.TerritoryType
                    }
                );

                LastTargetingData.Remove(key);
            }
            
            if (ModuleConfig.TargetingHistories.Count > 100)
                ModuleConfig.TargetingHistories.RemoveRange(0, ModuleConfig.TargetingHistories.Count - 100);
            
            ModuleConfig.Save(this);
        }

        foreach (var info in targetingPlayersInfo)
            LastTargetingData[info.Player.EntityID] = info;
        
        if (targetingPlayersInfo.Count > 0 &&
            (GameState.ContentFinderCondition == 0 || DService.Instance().PartyList.Length < 2))
        {
            var newTargetingPlayers = targetingPlayersInfo.Where(info => info.IsNew).ToList();
            if (newTargetingPlayers.Any(info => Throttler.Throttle($"AutoCountPlayers-Player-{info.Player.EntityID}", 30_000)))
            {
                if (ModuleConfig.SendTTS)
                {
                    if (!ModuleConfig.FilterFriend || targetingPlayersInfo.All(x => !x.Player.ToStruct()->IsFriend))
                        Speak(GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe"));
                }

                if (ModuleConfig.SendNotification)
                {
                    if (!ModuleConfig.FilterFriend || targetingPlayersInfo.All(x => !x.Player.ToStruct()->IsFriend))
                        NotificationWarning(GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe"));
                }
                
                if (ModuleConfig.SendChat)
                {
                    var builder = new SeStringBuilder();

                    builder.Append($"{GetLoc("AutoCountPlayers-Notification-SomeoneTargetingMe")}:");
                    builder.Add(new NewLinePayload());

                    foreach (var info in targetingPlayersInfo)
                    {
                        builder.Add(new PlayerPayload(info.Player.Name.ToString(), info.Player.HomeWorld.RowId))
                               .Append(" (")
                               .AddIcon(info.Player.ClassJob.Value.ToBitmapFontIcon())
                               .Append($" {info.Player.ClassJob.Value.Name})");
                        builder.Add(new NewLinePayload());
                    }

                    var message = builder.Build();
                    if (message.Payloads.Last() is NewLinePayload)
                        message.Payloads.RemoveAt(message.Payloads.Count - 1);

                    Chat(builder.Build());
                }
            }
        }
    }

    private void InfoProxy24EndRequestDetour(InfoProxy24* proxy)
    {
        InfoProxy24EndRequestHook.Original(proxy);
        OnReceivePlayers(PlayersManager.PlayersAround);
    }

    private static void DrawLine(Vector2 startPos, Vector2 endPos, ICharacter chara, uint lineColor = 0, string? extraInfo = null)
    {
        lineColor = lineColor == 0 ? LineColorBlue : lineColor;

        var drawList = ImGui.GetForegroundDrawList();

        drawList.AddLine(startPos, endPos, lineColor, 8f);
        drawList.AddCircleFilled(startPos, 12f, DotColor);
        drawList.AddCircleFilled(endPos,   12f, DotColor);

        ImGui.SetNextWindowPos(endPos);

        if (ImGui.Begin($"AutoCountPlayers-{chara.EntityID}", WINDOW_FLAGS))
        {
            using (ImRaii.Group())
            {
                ScaledDummy(12f);

                var icon = JobIcons.GetOrAdd
                (
                    chara.ClassJob.RowId,
                    _ => new SeStringBuilder().AddIcon(chara.ClassJob.Value.ToBitmapFontIcon()).Encode()
                );
                ImGui.SameLine();
                ImGuiHelpers.SeStringWrapped(icon);

                ImGui.SameLine();
                ImGuiOm.TextOutlined(KnownColor.Orange.ToVector4(), $"{chara.Name}" + (extraInfo ?? string.Empty));
            }

            ImGui.End();
        }
    }

    private class Config : ModuleConfiguration
    {
        public float ScaleFactor                = 1;

        public bool DisplayLineWhenTargetingMe = true;

        public bool SendChat         = true;
        public bool SendNotification = true;
        public bool SendTTS          = true;

        public bool FilterFriend;

        public List<TargetingRecord> TargetingHistories = [];
    }

    private class TargetingRecord
    {
        public string   Name        { get; set; } = string.Empty;
        public uint     HomeWorldID { get; set; }
        public uint     JobID       { get; set; }
        public uint     ZoneID      { get; set; }
        public DateTime StartTime   { get; set; }
        public TimeSpan Duration    { get; set; }
    }
}
