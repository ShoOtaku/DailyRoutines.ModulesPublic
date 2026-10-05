using System.Collections.Frozen;
using System.Numerics;
using DailyRoutines.Common.Info.Models;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using DailyRoutines.Manager;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading;
using Control = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoCountPlayers : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoCountPlayersTitle"),
        Description = Lang.Get("AutoCountPlayersDescription"),
        Category    = ModuleCategory.General
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private static bool IsPlayerSearchLocation =>
        IsContentSearchZone || IsPlayerSearchZone;

    private static bool IsContentSearchZone =>
        ContentMemberListValidZones.Contains(GameState.TerritoryIntendedUse);

    private static bool IsPlayerSearchZone =>
        GameState.TerritoryType          > 0  &&
        GameState.ContentFinderCondition == 0 &&
        Sheets.PlayerSearchPlaceNames.ContainsKey(GameState.TerritoryTypeData.PlaceNameZone.RowId);

    private Hook<InfoProxyContentMember.Delegates.EndRequest>? InfoProxyContentMemberEndRequestHook;
    private Hook<InfoProxySearch.Delegates.EndRequest>?        InfoProxySearchEndRequestHook;

    private Config        config = null!;
    private IDtrBarEntry? entry;

    private readonly Dictionary<uint, byte[]>              jobIcons          = [];
    private readonly Dictionary<uint, PlayerTargetingInfo> lastTargetingData = [];

    private string searchInput     = string.Empty;
    private string searchZoneInput = string.Empty;

    protected override void Init()
    {
        config = Config.Load(this) ?? new();

        entry       ??= IDtrBar.Instance().Get("DailyRoutines-AutoCountPlayers");
        entry.Shown =   true;
        entry.Text  =   $"{Lang.Get("AutoCountPlayers-PlayersAroundCount")}: 0";
        entry.OnClick = _ =>
        {
            EnsureOverlay();
            Overlay.IsOpen ^= true;
        };

        WindowManager.Instance().PostDraw += OnDraw;

        PlayersManager.Instance().ReceivePlayersAround      += OnReceivePlayers;
        PlayersManager.Instance().ReceivePlayersTargetingMe += OnPlayersTargetingMeUpdate;

        InfoProxyContentMemberEndRequestHook = InfoProxyContentMember.Instance()->VirtualTable->HookVFuncFromName
        (
            "EndRequest",
            (InfoProxyContentMember.Delegates.EndRequest)InfoProxyContentMemberRequestDetour
        );
        InfoProxyContentMemberEndRequestHook.Enable();

        InfoProxySearchEndRequestHook = InfoProxySearch.Instance()->VirtualTable->HookVFuncFromName
        (
            "EndRequest",
            (InfoProxySearch.Delegates.EndRequest)InfoProxySearchRequestDetour
        );
        InfoProxySearchEndRequestHook.Enable();

        LogMessageManager.Instance().RegPre(OnLogMessage);
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 1_000);
        OnUpdate(IFramework.Instance());

        GameState.Instance().WarpComplete += OnWarpCompleted;
    }

    protected override void Uninit()
    {
        GameState.Instance().WarpComplete -= OnWarpCompleted;

        FrameworkManager.Instance().Unreg(OnUpdate);
        LogMessageManager.Instance().Unreg(OnLogMessage);

        WindowManager.Instance().PostDraw                   -= OnDraw;
        PlayersManager.Instance().ReceivePlayersAround      -= OnReceivePlayers;
        PlayersManager.Instance().ReceivePlayersTargetingMe -= OnPlayersTargetingMeUpdate;

        foreach (var info in lastTargetingData.Values)
        {
            var duration = DateTime.Now - info.TargetingStartTime;
            config.TargetingHistories.Add
            (
                new()
                {
                    Name        = info.Player.Name,
                    HomeWorldID = info.Player.HomeWorld.RowId,
                    JobID       = info.Player.ClassJob.RowId,
                    StartTime   = info.TargetingStartTime,
                    Duration    = duration,
                    ZoneID      = GameState.TerritoryType
                }
            );
        }

        if (lastTargetingData.Count > 0)
        {
            lastTargetingData.Clear();
            if (config.TargetingHistories.Count > 100)
                config.TargetingHistories.RemoveRange(0, config.TargetingHistories.Count - 100);
            config.Save(this);
        }

        entry?.Remove();
        entry = null;
    }

    protected override void ConfigUI()
    {
        ImGui.SetNextItemWidth(120f * GlobalUIScale);
        if (ImGui.InputFloat(Lang.Get("Scale"), ref config.ScaleFactor, 0, 0, "%.1f"))
            config.ScaleFactor = Math.Max(0.1f, config.ScaleFactor);
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save(this);

        ImGui.NewLine();

        if (ImGui.Checkbox(Lang.Get("AutoCountPlayers-DisplayLineWhenTargetingMe"), ref config.DisplayLineWhenTargetingMe))
            config.Save(this);

        if (ImGui.Checkbox(Lang.Get("AutoCountPlayers-FilterFriend"), ref config.FilterFriend))
            config.Save(this);
    }

    protected override void OverlayUI()
    {
        using var tabBar = ImRaii.TabBar("##Tab");
        if (!tabBar) return;

        DrawPlayersAroundTab();

        if (IsPlayerSearchLocation)
            DrawPlayersInZoneTab();

        DrawTargetingHistoryTab();
    }

    private void DrawPlayersAroundTab()
    {
        using var item = ImRaii.TabItem(Lang.Get("AutoCountPlayers-PlayersAround"));
        if (!item) return;

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###Search", ref searchInput, 128);

        if (ICondition.Instance().IsBetweenAreas) return;

        using var child = ImRaii.Child("列表", ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing, true);
        if (!child) return;

        var players = PlayersManager.Instance().PlayersAround
                                    .Where(player => MatchesSearch(player.Name, searchInput))
                                    .ToList();

        DrawSectionHeader(Lang.Get("AutoCountPlayers-PlayersAround"), players.Count);

        var hoveredID = 0ul;

        foreach (var player in players)
        {
            using var id = ImRaii.PushId($"{player.GameObjectID}");

            using var rented = new RentedSeStringBuilder();

            var playerInfo = rented.Append(player.Name)
                                   .AppendIcon((uint)BitmapFontIcon.CrossWorld)
                                   .Append(player.HomeWorld.Value.Name.ToString())
                                   .ToReadOnlySeString();

            if (!DrawPlayerRow(player.ClassJob.Value.GetIcon(), playerInfo, clickable: true))
                continue;

            hoveredID = player.GameObjectID;

            ImGui.SetTooltip(Lang.Get("Locate"));

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                NotifyPlayerPosition(player);
        }

        DrawPlayersAroundLines(players, hoveredID);
    }

    private void DrawPlayersAroundLines
    (
        List<IPlayerCharacter> players,
        ulong                  hoveredID
    )
    {
        if (players.Count == 0) return;

        var gameGUI  = IGameGui.Instance();
        var viewport = ImGui.GetMainViewport();

        if (!gameGUI.WorldToScreen(LocalPlayerState.Object.Position, out var localScreenPos, out _))
            localScreenPos = viewport.Pos + viewport.Size with { X = viewport.Size.X * 0.5f };

        foreach (var player in players)
        {
            if (hoveredID != 0 && player.GameObjectID != hoveredID) continue;

            gameGUI.WorldToScreen(player.Position, out var screenPos, out var isInView);

            var linePositions = GetLinePositions
            (
                screenPos,
                isInView,
                viewport.Pos,
                viewport.Size,
                OFFSCREEN_MARKER_INSET  * GlobalUIScale,
                OFFSCREEN_LINE_OVERFLOW * GlobalUIScale
            );

            DrawLine(localScreenPos, linePositions.LineEnd, linePositions.Marker, player, isInView);
        }
    }

    private void DrawPlayersInZoneTab()
    {
        using var item = ImRaii.TabItem(Lang.Get("AutoCountPlayers-PlayersInZone"));
        if (!item) return;

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("###Search", ref searchZoneInput, 128);

        if (ICondition.Instance().IsBetweenAreas) return;

        using var child = ImRaii.Child("列表", ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing, true);
        if (!child) return;

        var info = IsPlayerSearchZone ?
                       (InfoProxyCommonList*)InfoProxySearch.Instance() :
                       (InfoProxyCommonList*)InfoProxyContentMember.Instance();

        foreach (var (dataCenterID, players) in GroupZonePlayersByDataCenter(info, searchZoneInput))
        {
            DrawSectionHeader(LuminaWrapper.GetDataCenterName(dataCenterID), players.Count);

            foreach (var player in players)
            {
                using var id = ImRaii.PushId($"{player.ContentId}");

                using var rented = new RentedSeStringBuilder();

                var playerInfo = rented.Append(player.NameString)
                                       .AppendIcon((uint)BitmapFontIcon.CrossWorld)
                                       .Append(LuminaWrapper.GetWorldName(player.HomeWorld))
                                       .ToReadOnlySeString();

                DrawPlayerRow(LuminaWrapper.GetJobIcon(player.Job), playerInfo);
            }

            ImGui.Spacing();
        }
    }

    private void DrawTargetingHistoryTab()
    {
        using var item = ImRaii.TabItem(Lang.Get("AutoCountPlayers-TargetedHistory"));
        if (!item) return;

        using var child = ImRaii.Child("列表", ImGui.GetContentRegionAvail() - ImGui.GetStyle().ItemSpacing, true);
        if (!child) return;

        DrawSectionHeader(Lang.Get("AutoCountPlayers-TargetedHistory"), config.TargetingHistories.Count);

        foreach (var record in config.TargetingHistories.AsEnumerable().Reverse())
        {
            using var id = ImRaii.PushId($"{record.StartTime.Ticks}-{record.Name}");

            using var rented = new RentedSeStringBuilder();

            rented.Append(record.Name)
                  .AppendIcon((uint)BitmapFontIcon.CrossWorld)
                  .Append(LuminaWrapper.GetWorldName(record.HomeWorldID));

            DrawPlayerRow
            (
                LuminaGetter.GetRowOrDefault<ClassJob>(record.JobID).GetIcon(),
                rented.ToReadOnlySeString(),
                $"[{record.Duration:mm\\:ss}]",
                KnownColor.Orange,
                $"{LuminaWrapper.GetZonePlaceName(record.ZoneID)}  {record.StartTime:MM/dd HH:mm}"
            );
        }
    }

    private static bool DrawPlayerRow
    (
        uint             jobIconID,
        ReadOnlySeString playerInfo,
        string?          trailingText  = null,
        KnownColor       trailingColor = KnownColor.Gray,
        string?          subText       = null,
        bool             clickable     = false
    )
    {
        var style        = ImGui.GetStyle();
        var lineHeight   = ImGui.GetTextLineHeight();
        var indent       = PLAYER_ROW_INDENT * GlobalUIScale;
        var rowStart     = ImGui.GetCursorScreenPos();
        var rowAvail     = ImGui.GetContentRegionAvail().X;
        var contentStart = rowStart with { X = rowStart.X + indent };
        var rowHeight    = lineHeight + (style.FramePadding.Y * 2);
        var textPos      = contentStart with { Y = contentStart.Y + style.FramePadding.Y };
        var subSpacing   = PLAYER_ROW_SUB_SPACING * GlobalUIScale;
        var hovered      = false;

        if (!string.IsNullOrEmpty(subText))
            rowHeight += lineHeight + subSpacing;

        ImGui.SetCursorScreenPos(contentStart);

        if (clickable)
        {
            ImGui.Selectable("##Row", false, ImGuiSelectableFlags.None, new Vector2(rowAvail - indent, rowHeight));

            hovered = ImGui.IsItemHovered();
        }
        else
            ImGui.Dummy(new Vector2(rowAvail - indent, rowHeight));

        var rowEnd = ImGui.GetCursorScreenPos() with { X = rowStart.X };

        ImGui.SetCursorScreenPos(textPos);

        if (ITextureProvider.Instance().TryGetFromGameIcon(jobIconID, out var texture))
        {
            ImGui.Image(texture.GetWrapOrEmpty().Handle, new Vector2(lineHeight));

            ImGui.SameLine(0f, PLAYER_ROW_ICON_SPACING * GlobalUIScale);
        }

        var textStartX = ImGui.GetCursorScreenPos().X;

        ImGuiHelpers.SeStringWrapped(playerInfo);

        if (!string.IsNullOrEmpty(subText))
        {
            ImGui.SetCursorScreenPos(new Vector2(textStartX, textPos.Y + lineHeight + subSpacing));

            ImGui.TextColored(PlayerRowSubTextColor.ToVector4(), subText);
        }

        if (!string.IsNullOrEmpty(trailingText))
        {
            var trailingX = rowStart.X + rowAvail - ImGui.CalcTextSize(trailingText).X - style.FramePadding.X;

            ImGui.SetCursorScreenPos(textPos with { X = trailingX });

            ImGui.TextColored(trailingColor.ToVector4(), trailingText);
        }

        ImGui.SetCursorScreenPos(rowEnd);

        return hovered;
    }

    private static void DrawSectionHeader
    (
        string title,
        int    count
    )
    {
        var drawList = ImGui.GetWindowDrawList();
        var paddingX = SECTION_HEADER_PADDING_HORIZONTAL * GlobalUIScale;
        var paddingY = SECTION_HEADER_PADDING_VERTICAL   * GlobalUIScale;
        var startPos = ImGui.GetCursorScreenPos();
        var width    = ImGui.GetContentRegionAvail().X;
        var height   = ImGui.GetTextLineHeight() + (paddingY * 2);
        var textPos  = startPos                  + new Vector2(paddingX, paddingY);

        drawList.AddText(textPos, SectionHeaderTextColor, title);

        var countText = count.ToString();

        drawList.AddText
        (
            textPos with { X = startPos.X + width - paddingX - ImGui.CalcTextSize(countText).X },
            SectionHeaderCountColor,
            countText
        );

        var lineY     = startPos.Y + height;
        var lineStart = startPos with { Y = lineY };
        var lineEnd   = lineStart with { X = startPos.X + width };

        drawList.AddLine(lineStart, lineEnd, SectionHeaderLineColor, SECTION_HEADER_LINE_THICKNESS * GlobalUIScale);

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void NotifyPlayerPosition
    (
        IPlayerCharacter player
    )
    {
        using var rented = new RentedSeStringBuilder();

        rented.AppendIcon((uint)player.ClassJob.Value.ToBitmapFontIcon())
              .Append(ReadOnlySeString.CreatePlayer(player.Name, player.HomeWorld.RowId));

        var playerLink = ReadOnlySeString.CreatePlayerLink
        (
            player.Name,
            player.HomeWorld.RowId,
            rented.ToReadOnlySeString()
        );

        NotifyHelper.Instance().Chat
        (
            Lang.GetSe
            (
                "AutoCountPlayers-Message-DetailedPosition",
                new Dictionary<string, object>
                {
                    ["playerLink"] = playerLink,
                    ["mapLink"]    = ReadOnlySeString.CreateMapLink(player.Position)
                }
            ),
            false
        );
    }

    private static bool MatchesSearch
    (
        string name,
        string searchText
    ) =>
        string.IsNullOrWhiteSpace(searchText) || name.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    #region 事件

    private static void OnWarpCompleted
    (
        WarpType warpType
    ) =>
        Throttler.Shared.Remove("AutoCountPlayers.Zone");


    private void OnDraw()
    {
        if (!config.DisplayLineWhenTargetingMe || PlayersManager.Instance().PlayersTargetingMe.Count == 0) return;

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
                    ImGui.SetWindowPos(nodeState.Center - (ImGui.GetWindowSize() * 0.75f));

                    using (FontManager.Instance().UIFont140.Push())
                    using (ImRaii.Group())
                    {
                        ImGuiHelpers.SeStringWrapped(new SeStringBuilder().AddIcon(BitmapFontIcon.Warning).Encode());

                        ImGui.SameLine();
                        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (1.2f * GlobalUIScale));
                        ImGuiOm.TextOutlined
                            (KnownColor.Orange.ToUInt(), $"{PlayersManager.Instance().PlayersTargetingMe.Count}", KnownColor.SaddleBrown.ToUInt());

                        if (GameState.ContentFinderCondition == 0)
                        {
                            using (FontManager.Instance().UIFont80.Push())
                            {
                                var text = Lang.Get("AutoCountPlayers-Notification-SomeoneTargetingMe");
                                ImGuiOm.TextOutlined
                                (
                                    ImGui.GetCursorScreenPos() - new Vector2(ImGui.CalcTextSize(text).X * 0.3f, 0),
                                    KnownColor.Orange.ToUInt(),
                                    $"({text})",
                                    KnownColor.SaddleBrown.ToUInt()
                                );
                            }
                        }
                    }

                    ImGui.End();
                }
            }
        }

        var gameGUI  = IGameGui.Instance();
        var viewport = ImGui.GetMainViewport();

        if (!gameGUI.WorldToScreen(localPlayer->Position, out var localScreenPos, out _))
            localScreenPos = viewport.Pos + new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y);

        foreach (var playerInfo in PlayersManager.Instance().PlayersTargetingMe)
        {
            gameGUI.WorldToScreen(playerInfo.Player.Position, out var screenPos, out var isInView);

            var linePositions = GetLinePositions
            (
                screenPos,
                isInView,
                viewport.Pos,
                viewport.Size,
                OFFSCREEN_MARKER_INSET  * GlobalUIScale,
                OFFSCREEN_LINE_OVERFLOW * GlobalUIScale
            );

            DrawLine
            (
                localScreenPos,
                linePositions.LineEnd,
                linePositions.Marker,
                playerInfo.Player,
                isInView,
                true,
                $" [{TimeSpan.FromSeconds(playerInfo.TargetingDurationSeconds)}]"
            );
        }
    }

    private static void OnLogMessage
    (
        ref bool                isPrevented,
        ref uint                logMessageID,
        ref LogMessageQueueItem item
    )
    {
        if (logMessageID != 81) return;
        isPrevented = true;
    }

    private static void OnUpdate
    (
        IFramework framework
    )
    {
        if (!IsPlayerSearchLocation) return;

        if (!UIModule.IsScreenReady() ||
            !Throttler.Shared.Throttle("AutoCountPlayers.Zone", 60_000))
            return;

        if (IsContentSearchZone)
        {
            if (InfoProxyContentMember.Instance() == null ||
                AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentMemberList)->IsAgentActive())
                return;

            AgentId.ContentMemberList.SendEvent(0, 1);
        }
        else if (IsPlayerSearchZone)
        {
            var searchInstance = InfoProxySearch.Instance();
            if (searchInstance == null ||
                AgentModule.Instance()->GetAgentByInternalId(AgentId.Search)->IsAgentActive())
                return;

            searchInstance->JobMask          = 0xFFFFFFFFFFFFFFFF; // all
            searchInstance->LevelMin         = 1;
            searchInstance->LevelMax         = 255;
            searchInstance->GrandCompanyMask = 0xFF;
            searchInstance->LanguageMask     = 0xFF;
            searchInstance->OnlineStatusMask = 0x800000000000;
            searchInstance->LocationIDs[0]   = (ushort)GameState.TerritoryTypeData.PlaceNameZone.RowId;
            searchInstance->LocationCount    = 1;
            for (var i = 0; i < searchInstance->Name.Length; i++)
                searchInstance->Name[i] = 0;

            searchInstance->RequestData();
        }
        else
            FrameworkManager.Instance().Unreg(OnUpdate);
    }

    private void OnReceivePlayers
    (
        IReadOnlyList<IPlayerCharacter> characters
    )
    {
        if (entry == null) return;

        if (IsPlayerSearchLocation)
            entry.Shown = true;
        else
            entry.Shown = !ICondition.Instance()[ConditionFlag.InCombat] || GameState.IsInPVPArea;

        if (!entry.Shown)
        {
            EnsureOverlay();
            Overlay.IsOpen = false;
            return;
        }

        entry.Text = $"{Lang.Get("AutoCountPlayers-PlayersAroundCount")}: {PlayersManager.Instance().PlayersAroundCount}" +
                     (PlayersManager.Instance().PlayersTargetingMe.Count == 0 ?
                          string.Empty :
                          $" ({PlayersManager.Instance().PlayersTargetingMe.Count})");

        // 特殊场景探索
        if (IsContentSearchZone)
        {
            entry.Text.Append
            (
                $" / {Lang.Get("AutoCountPlayers-PlayersZoneCount")}: " +
                $"{InfoProxyContentMember.Instance()->EntryCount}"
            );
        }
        else if (IsPlayerSearchZone)
        {
            var count = InfoProxySearch.Instance()->CharDataSpan
                        .ToArray()
                        .Count(x => x.Job > 0 && x.Location == GameState.TerritoryType);
            entry.Text.Append
            (
                $" / {Lang.Get("AutoCountPlayers-PlayersZoneCount")}: " +
                $"{count}"
            );
        }

        if (characters.Count == 0)
        {
            entry.Tooltip = string.Empty;
            return;
        }

        var tooltip = new SeStringBuilder();

        if (PlayersManager.Instance().PlayersTargetingMe.Count > 0)
        {
            tooltip.AddUiForeground(32)
                   .AddText($"{Lang.Get("AutoCountPlayers-PlayersTargetingMe")}")
                   .AddUiForegroundOff()
                   .Add(NewLinePayload.Payload);

            PlayersManager.Instance().PlayersTargetingMe.ForEach
            (info =>
                 tooltip
                     .AddIcon(info.Player.ClassJob.Value.ToBitmapFontIcon())
                     .AddText($"{info.Player.Name}")
                     .AddIcon(BitmapFontIcon.CrossWorld)
                     .AddText($"{info.Player.HomeWorld.Value.Name}")
                     .Add(NewLinePayload.Payload)
            );
        }

        tooltip.AddUiForeground(32)
               .AddText($"{Lang.Get("AutoCountPlayers-PlayersAroundInfo")}")
               .AddUiForegroundOff()
               .Add(NewLinePayload.Payload);

        characters.ForEach
        (info => tooltip
                 .AddIcon(info.ClassJob.Value.ToBitmapFontIcon())
                 .AddText($"{info.Name}")
                 .AddIcon(BitmapFontIcon.CrossWorld)
                 .AddText($"{info.HomeWorld.Value.Name}")
                 .Add(NewLinePayload.Payload)
        );

        var message = tooltip.Build();
        if (message.Payloads.Last() is NewLinePayload)
            message.Payloads.RemoveAt(message.Payloads.Count - 1);

        entry.Tooltip = message;
    }

    private void OnPlayersTargetingMeUpdate
    (
        IReadOnlyList<PlayerTargetingInfo> targetingPlayersInfo
    )
    {
        var currentIDs     = targetingPlayersInfo.Select(x => x.Player.EntityID).ToHashSet();
        var endedTargeting = lastTargetingData.Where(x => !currentIDs.Contains(x.Key)).ToList();

        if (endedTargeting.Count > 0)
        {
            foreach (var (key, info) in endedTargeting)
            {
                var duration = DateTime.Now - info.TargetingStartTime;

                config.TargetingHistories.Add
                (
                    new()
                    {
                        Name        = info.Player.Name,
                        HomeWorldID = info.Player.HomeWorld.RowId,
                        JobID       = info.Player.ClassJob.RowId,
                        StartTime   = info.TargetingStartTime,
                        Duration    = duration,
                        ZoneID      = GameState.TerritoryType
                    }
                );

                lastTargetingData.Remove(key);
            }

            if (config.TargetingHistories.Count > 100)
                config.TargetingHistories.RemoveRange(0, config.TargetingHistories.Count - 100);

            config.Save(this);
        }

        foreach (var info in targetingPlayersInfo)
        {
            if (info.Player.ClassJob.RowId == 0) continue;
            lastTargetingData[info.Player.EntityID] = info;
        }

        if (targetingPlayersInfo.Count > 0 &&
            (GameState.ContentFinderCondition == 0 || IPartyList.Instance().Length < 2))
        {
            var newTargetingPlayers = targetingPlayersInfo.Where(info => info.IsNew)
                                                          .ToList();

            if (newTargetingPlayers.Any
                (info => Throttler.Shared.Throttle
                         (
                             $"AutoCountPlayers-Player-{info.Player.EntityID}",
                             30_000
                         ) &&
                         !info.Player.ToStruct()->IsFriend
                ))
            {
                NotifyHelper.Instance().TrayWarning
                (
                    Lang.Get
                    (
                        "AutoCountPlayers-Notification-Message",
                        new Dictionary<string, object>
                        {
                            ["playerCount"] = targetingPlayersInfo.Count
                        }
                    )
                );

                var messageTitle = Lang.Get
                (
                    "AutoCountPlayers-Message",
                    new Dictionary<string, object>
                    {
                        ["playerCount"] = targetingPlayersInfo.Count
                    }
                );

                using var rented       = new RentedSeStringBuilder();
                using var playerRented = new RentedSeStringBuilder();

                rented.Append(messageTitle);

                foreach (var player in targetingPlayersInfo)
                {
                    playerRented.AppendIcon((uint)player.Player.ClassJob.Value.ToBitmapFontIcon())
                                .Append(ReadOnlySeString.CreatePlayer(player.Player.Name, player.Player.HomeWorld.RowId));

                    var playerLink = ReadOnlySeString.CreatePlayerLink
                    (
                        player.Player.Name,
                        player.Player.HomeWorld.RowId,
                        playerRented.ToReadOnlySeString()
                    );
                    playerRented.Clear();

                    rented.AppendNewLine()
                          .Append("  ")
                          .Append(playerLink);
                }

                NotifyHelper.Instance().Chat(rented.ToReadOnlySeString(), false);
            }
        }
    }

    private void InfoProxyContentMemberRequestDetour
    (
        InfoProxyContentMember* proxy
    )
    {
        InfoProxyContentMemberEndRequestHook.Original(proxy);
        OnReceivePlayers(PlayersManager.Instance().PlayersAround);
    }

    private void InfoProxySearchRequestDetour
    (
        InfoProxySearch* proxy
    )
    {
        InfoProxySearchEndRequestHook.Original(proxy);
        OnReceivePlayers(PlayersManager.Instance().PlayersAround);
    }

    #endregion

    private static List<(uint DataCenterID, List<InfoProxyCommonList.CharacterData> Players)> GroupZonePlayersByDataCenter
    (
        InfoProxyCommonList* info,
        string               searchText
    )
    {
        var groups = new Dictionary<uint, List<InfoProxyCommonList.CharacterData>>();

        foreach (var player in info->CharDataSpan)
        {
            if (IsPlayerSearchZone                     && player.Location != GameState.TerritoryType) continue;
            if (!string.IsNullOrWhiteSpace(searchText) && !player.NameString.Contains(searchText)) continue;

            var dataCenterID = LuminaWrapper.GetWorldDC(player.HomeWorld);

            if (!groups.TryGetValue(dataCenterID, out var players))
            {
                players              = [];
                groups[dataCenterID] = players;
            }

            players.Add(player);
        }

        return
        [
            .. groups.OrderBy(x => x.Key)
                     .Select
                     (x => (x.Key,
                               x.Value.OrderBy(player => player.HomeWorld)
                                .ThenBy(player => player.NameString)
                                .ToList())
                     )
        ];
    }

    private void DrawLine
    (
        Vector2    startPos,
        Vector2    lineEndPos,
        Vector2    markerPos,
        ICharacter chara,
        bool       isMarkerVisible,
        bool       isAlert   = false,
        string?    extraInfo = null
    )
    {
        var drawList = ImGui.GetForegroundDrawList();
        var lineColor = isAlert ?
                            AlertLineColor :
                            InfoLineColor;
        var labelColor = isAlert ?
                             AlertLabelColor :
                             InfoLabelColor;
        var startRadius  = START_MARKER_RADIUS  * GlobalUIScale;
        var markerRadius = TARGET_MARKER_RADIUS * GlobalUIScale;

        drawList.AddLine(startPos, lineEndPos, LineOutlineColor, LINE_OUTLINE_THICKNESS * GlobalUIScale);
        drawList.AddLine(startPos, lineEndPos, lineColor,        LINE_THICKNESS         * GlobalUIScale);

        drawList.AddCircleFilled(startPos, startRadius + (START_MARKER_OUTLINE_SIZE * GlobalUIScale), LineOutlineColor);
        drawList.AddCircleFilled(startPos, startRadius,                                               lineColor);

        if (isMarkerVisible)
        {
            drawList.AddCircleFilled(markerPos, markerRadius + (TARGET_MARKER_OUTLINE_SIZE * GlobalUIScale), LineOutlineColor);
            drawList.AddCircle(markerPos, markerRadius, lineColor, TARGET_MARKER_SEGMENTS, TARGET_MARKER_THICKNESS * GlobalUIScale);
            drawList.AddCircleFilled(markerPos, TARGET_MARKER_CORE_RADIUS                                          * GlobalUIScale, MarkerCoreColor);
        }

        var viewportCenter = ImGui.GetMainViewport().GetCenter();
        var labelPivot = new Vector2
        (
            markerPos.X >= viewportCenter.X ?
                1f :
                0f,
            markerPos.Y >= viewportCenter.Y ?
                1f :
                0f
        );

        ImGui.SetNextWindowPos(markerPos, ImGuiCond.Always, labelPivot);

        if (ImGui.Begin($"AutoCountPlayers-{chara.EntityID}", WINDOW_FLAGS))
        {
            using (ImRaii.Group())
            {
                ImGuiOm.ScaledDummy(12f);

                var icon = jobIcons.GetOrAdd
                (
                    chara.ClassJob.RowId,
                    _ => new SeStringBuilder().AddIcon(chara.ClassJob.Value.ToBitmapFontIcon()).Encode()
                );
                ImGui.SameLine();
                ImGuiHelpers.SeStringWrapped(icon);

                ImGui.SameLine();
                ImGuiOm.TextOutlined(labelColor, $"{chara.Name}" + (extraInfo ?? string.Empty));
            }

            ImGui.End();
        }
    }

    private static (Vector2 LineEnd, Vector2 Marker) GetLinePositions
    (
        Vector2 projectedPosition,
        bool    isInView,
        Vector2 viewportPosition,
        Vector2 viewportSize,
        float   markerInset,
        float   lineOverflow
    )
    {
        if (isInView) return (projectedPosition, projectedPosition);

        var viewportCenter = viewportPosition  + (viewportSize * 0.5f);
        var direction      = projectedPosition - viewportCenter;

        if (!float.IsFinite(direction.X) ||
            !float.IsFinite(direction.Y) ||
            direction.LengthSquared() < MIN_DIRECTION_LENGTH_SQUARED)
            direction = Vector2.UnitY;

        var halfWidth  = Math.Max(viewportSize.X * 0.5f, 1f);
        var halfHeight = Math.Max(viewportSize.Y * 0.5f, 1f);
        var horizontalT = MathF.Abs(direction.X) > float.Epsilon ?
                              halfWidth / MathF.Abs(direction.X) :
                              float.MaxValue;
        var verticalT = MathF.Abs(direction.Y) > float.Epsilon ?
                            halfHeight / MathF.Abs(direction.Y) :
                            float.MaxValue;
        var edgePosition        = viewportCenter + (direction * MathF.Min(horizontalT, verticalT));
        var normalizedDirection = Vector2.Normalize(direction);

        return
            (
                edgePosition + (normalizedDirection * lineOverflow),
                edgePosition - (normalizedDirection * markerInset)
            );
    }

    private void EnsureOverlay()
    {
        if (Overlay != null) return;

        Overlay            =  new(this);
        Overlay.Flags      &= ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags      &= ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.WindowName =  $"{Lang.Get("AutoCountPlayers-PlayersAroundInfo")}###AutoCountPlayers-Overlay";
    }

    private class Config : ModuleConfig
    {
        public bool DisplayLineWhenTargetingMe = true;

        public bool  FilterFriend;
        public float ScaleFactor = 1;

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

    #region 常量

    private const ImGuiWindowFlags WINDOW_FLAGS =
        ImGuiWindowFlags.NoScrollbar           |
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
        ImGuiWindowFlags.NoInputs              |
        ImGuiWindowFlags.NoSavedSettings;

    private const float LINE_THICKNESS               = 3f;
    private const float LINE_OUTLINE_THICKNESS       = 5f;
    private const float START_MARKER_RADIUS          = 2.25f;
    private const float START_MARKER_OUTLINE_SIZE    = 1.25f;
    private const float TARGET_MARKER_RADIUS         = 4f;
    private const float TARGET_MARKER_OUTLINE_SIZE   = 1.5f;
    private const float TARGET_MARKER_THICKNESS      = 1.5f;
    private const float TARGET_MARKER_CORE_RADIUS    = 1.25f;
    private const int   TARGET_MARKER_SEGMENTS       = 16;
    private const float OFFSCREEN_MARKER_INSET       = 14f;
    private const float OFFSCREEN_LINE_OVERFLOW      = 32f;
    private const float MIN_DIRECTION_LENGTH_SQUARED = 0.001f;

    private const float SECTION_HEADER_PADDING_HORIZONTAL = 6f;
    private const float SECTION_HEADER_PADDING_VERTICAL   = 4f;
    private const float SECTION_HEADER_LINE_THICKNESS     = 1f;
    private const float PLAYER_ROW_INDENT                 = 12f;
    private const float PLAYER_ROW_ICON_SPACING           = 4f;
    private const float PLAYER_ROW_SUB_SPACING            = 2f;

    private const float INFO_LINE_OPACITY    = 0.78f;
    private const float ALERT_LINE_OPACITY   = 0.88f;
    private const float INFO_LABEL_OPACITY   = 0.96f;
    private const float ALERT_LABEL_OPACITY  = 0.98f;
    private const float LINE_OUTLINE_OPACITY = 0.4f;
    private const float MARKER_CORE_OPACITY  = 0.88f;

    private const float SECTION_HEADER_TEXT_OPACITY  = 0.78f;
    private const float SECTION_HEADER_COUNT_OPACITY = 0.45f;
    private const float SECTION_HEADER_LINE_OPACITY  = 0.08f;
    private const float PLAYER_ROW_SUB_OPACITY       = 0.68f;

    private static readonly uint InfoLineColor =
        (KnownColor.DeepSkyBlue.ToVector4() with { W = INFO_LINE_OPACITY }).ToUInt();

    private static readonly uint AlertLineColor =
        (KnownColor.IndianRed.ToVector4() with { W = ALERT_LINE_OPACITY }).ToUInt();

    private static readonly uint InfoLabelColor =
        (KnownColor.LightSkyBlue.ToVector4() with { W = INFO_LABEL_OPACITY }).ToUInt();

    private static readonly uint AlertLabelColor =
        (KnownColor.LightCoral.ToVector4() with { W = ALERT_LABEL_OPACITY }).ToUInt();

    private static readonly uint LineOutlineColor =
        (KnownColor.Black.ToVector4() with { W = LINE_OUTLINE_OPACITY }).ToUInt();

    private static readonly uint MarkerCoreColor =
        (KnownColor.WhiteSmoke.ToVector4() with { W = MARKER_CORE_OPACITY }).ToUInt();

    private static readonly uint SectionHeaderTextColor =
        (KnownColor.LightGray.ToVector4() with { W = SECTION_HEADER_TEXT_OPACITY }).ToUInt();

    private static readonly uint SectionHeaderCountColor =
        (KnownColor.Gray.ToVector4() with { W = SECTION_HEADER_COUNT_OPACITY }).ToUInt();

    private static readonly uint SectionHeaderLineColor =
        (KnownColor.White.ToVector4() with { W = SECTION_HEADER_LINE_OPACITY }).ToUInt();

    private static readonly uint PlayerRowSubTextColor =
        (KnownColor.Gray.ToVector4() with { W = PLAYER_ROW_SUB_OPACITY }).ToUInt();

    private static readonly FrozenSet<TerritoryIntendedUse> ContentMemberListValidZones =
    [
        TerritoryIntendedUse.OccultCrescent,
        TerritoryIntendedUse.Bozja,
        TerritoryIntendedUse.Eureka
    ];

    #endregion
}
