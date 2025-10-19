using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Map = Lumina.Excel.Sheets.Map;

namespace DailyRoutines.ModulesPublic;

public unsafe class BetterTeleport : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("BetterTeleportTitle"),
        Description = GetLoc("BetterTeleportDescription"),
        Category    = ModuleCategories.UIOptimization,
        ModulesPrerequisite = ["SameAethernetTeleport"]
    };
    
    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private const string Command = "/pdrtelepo";
    
    // Icon ID - Record
    private static readonly Dictionary<string, List<AetheryteRecord>> Records      = [];
    private static readonly List<AetheryteRecord>                     HouseRecords = [];

    private static readonly SeString HomeChar      = new SeStringBuilder().AddIcon(BitmapFontIcon.OrangeDiamond).Build();
    private static readonly SeString FreeChar      = new SeStringBuilder().AddIcon(BitmapFontIcon.GoldStar).Build();
    private static readonly SeString FavoriteChar  = new SeStringBuilder().AddIcon(BitmapFontIcon.SilverStar).Build();
    private static readonly SeString AetheryteChar = new SeStringBuilder().AddIcon(BitmapFontIcon.Aetheryte).Build();
    private static readonly SeString AethernetChar = new SeStringBuilder().AddIcon(BitmapFontIcon.Aethernet).Build();

    private static readonly HashSet<uint> HouseZones = [339, 340, 341, 641, 979];
    
    private static readonly Dictionary<uint, string> TicketUsageTypes = [];
    
    private static uint TicketUsageType
    {
        get => DService.GameConfig.UiConfig.GetUInt("TelepoTicketUseType");
        set => DService.GameConfig.UiConfig.Set("TelepoTicketUseType", value);
    }

    private static uint TicketUsageGilSetting
    {
        get => DService.GameConfig.UiConfig.GetUInt("TelepoTicketGilSetting");
        set => DService.GameConfig.UiConfig.Set("TelepoTicketGilSetting", value);
    }

    private static Config ModuleConfig = null!;

    private static bool IsRefreshing;
    
    private static string                SearchWord   = string.Empty;
    private static List<AetheryteRecord> SearchResult = [];
    private static List<AetheryteRecord> Favorites    = [];

    private static IEnumerable<AetheryteRecord> AllRecords => 
        Records.Values.SelectMany(x => x).Concat(HouseRecords);

    static BetterTeleport()
    {
        for (var i = 0U; i < 5; i++)
        {
            var addonOffset       = i + 8523U;
            var optionDescription = LuminaWrapper.GetAddonText(addonOffset);
            TicketUsageTypes[i] = optionDescription;
        }
    }

    protected override void Init()
    {
        Overlay            ??= new(this);
        Overlay.Flags      &=  ~ImGuiWindowFlags.AlwaysAutoResize;
        Overlay.Flags      &=  ~ImGuiWindowFlags.NoTitleBar;
        Overlay.Flags      |=  ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        Overlay.WindowName =   $"{LuminaWrapper.GetAddonText(8513)}###BetterTeleportOverlay";

        TaskHelper ??= new() { TimeLimitMS = 60_000 };

        ModuleConfig = LoadConfig<Config>() ?? new();

        DService.ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(DService.ClientState.TerritoryType);

        CommandManager.AddCommand(Command, new(OnCommand) { HelpMessage = GetLoc("BetterTeleport-CommandHelp") });

        UseActionManager.RegPreUseAction(OnPostUseAction);
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Command")}:");
        
        ImGui.SameLine();
        ImGui.TextWrapped($"{Command} {GetLoc("BetterTeleport-CommandHelp")}");
    }

    protected override void OverlayUI()
    {
        switch (IsRefreshing)
        {
            case false when !TaskHelper.IsBusy && Records.Count == 0:
                OnZoneChanged(DService.ClientState.TerritoryType);
                return;
            case true:
                return;
        }

        if (DService.KeyState[VirtualKey.ESCAPE])
        {
            Overlay.IsOpen = false;
            if (SystemMenu != null)
                SystemMenu->Close(true);
        }

        var isSearchEmpty = string.IsNullOrWhiteSpace(SearchWord);
        if (!isSearchEmpty)
        {
            if (ImGuiOm.ButtonIcon("Clear", FontAwesomeIcon.TrashAlt))
            {
                SearchWord   = string.Empty;
                SearchResult = [];
            }
        }
        
        ImGui.SetNextItemWidth(-1f);
        if (!isSearchEmpty)
            ImGui.SameLine();
        if (ImGui.InputTextWithHint("###Search", GetLoc("PleaseSearch"), ref SearchWord, 128))
        {
            SearchResult = !string.IsNullOrWhiteSpace(SearchWord)
                               ? Records.Values
                                        .SelectMany(x => x)
                                        .Where(x => x.ToString()
                                                     .Contains(SearchWord, StringComparison.OrdinalIgnoreCase) ||
                                                    (ModuleConfig.Remarks.TryGetValue(x.RowID, out var remark) && 
                                                     remark.Contains(SearchWord, StringComparison.OrdinalIgnoreCase)))
                                        .ToList()
                               : [];
        }
        
        using var tabBar = ImRaii.TabBar("###AetherytesTabBar", ImGuiTabBarFlags.Reorderable);
        if (!tabBar) return;
        
        var isSettingOn = false;
        
        if (SearchResult.Count > 0 || !isSearchEmpty)
        {
            using var tabItem = ImRaii.TabItem($"{GetLoc("SearchResult")}##TabItem");
            if (!tabItem) return;
            
            var       childSize = ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
            using var child     = ImRaii.Child("###SearchResultChild", childSize, false, ImGuiWindowFlags.NoBackground);
            if (!child) return;
            
            foreach (var aetheryte in SearchResult)
                DrawAetheryte("search", aetheryte);
        }
        else
        {
            if (Favorites.Count > 0)
            {
                using var tabItem = ImRaii.TabItem($"{GetLoc("Favorite")}##TabItem");
                if (tabItem)
                {
                    var       childSize = ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
                    using var child     = ImRaii.Child("###FavoriteChild", childSize, false, ImGuiWindowFlags.NoBackground);
                    if (child)
                    {
                        foreach (var aetheryte in Favorites)
                            DrawAetheryte("favorite", aetheryte);
                    }
                }
            }

            var agentLobby = AgentLobby.Instance();
            if (agentLobby == null) return;
            
            foreach (var (name, aetherytes) in Records)
            {
                using var tabItem = ImRaii.TabItem($"{name}##TabItem");
                if (!tabItem) continue;

                var       childSize = ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
                using var child     = ImRaii.Child($"###{name}Child", childSize, false, ImGuiWindowFlags.NoBackground);
                if (!child) continue;

                var lastName    = string.Empty;
                var lastGroupID = -1;

                var source = name == LuminaWrapper.GetAddonText(832) ? HouseRecords.Concat(aetherytes) : aetherytes;
                foreach (var aetheryte in source)
                {
                    if (!aetheryte.IsUnlocked() && aetheryte.Group != 255) continue;
                    if (aetheryte.Group                   == 254 &&
                        agentLobby->LobbyData.HomeWorldId != agentLobby->LobbyData.CurrentWorldId)
                        continue;

                    if (aetheryte.Group == 0)
                    {
                        if (lastName != aetheryte.RegionName)
                        {
                            using (var tableHeader = ImRaii.Table($"{name}_Table_Header_{aetheryte.Group}", 1, ImGuiTableFlags.None))
                            {
                                if (tableHeader)
                                {
                                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                                    ImGui.TableNextColumn();
                                    
                                    ImGuiOm.TextCentered(aetheryte.RegionName);
                                }
                            }

                            lastName = aetheryte.RegionName;
                        }
                    }
                    else
                    {
                        if (lastGroupID != aetheryte.Group)
                        {
                            using (var tableHeader = ImRaii.Table($"{name}_Table_Header_{aetheryte.Group}", 1, ImGuiTableFlags.None))
                            {
                                if (tableHeader)
                                {
                                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                                    ImGui.TableNextColumn();
                                    
                                    ImGuiOm.TextCentered(aetheryte.RegionName);
                                }
                            }

                            lastGroupID = aetheryte.Group;
                        }
                    }
                    
                    DrawAetheryte(name, aetheryte);

                    ImGui.Spacing();
                }
            }
            
            using (var settingTab = ImRaii.TabItem(FontAwesomeIcon.Cog.ToIconString()))
            {
                if (settingTab)
                {
                    isSettingOn = true;
                
                    ImGui.Text($"{LuminaWrapper.GetAddonText(8522)}");

                    using (var combo = ImRaii.Combo("###TeleportUsageTypeCombo", TicketUsageTypes[TicketUsageType]))
                    {
                        if (combo)
                        {
                            foreach (var kvp in TicketUsageTypes)
                            {
                                if (ImGui.Selectable($"{kvp.Value}", kvp.Key == TicketUsageType))
                                    TicketUsageType = kvp.Key;
                            }
                        }
                    }
                    
                    ImGui.Text($"{LuminaWrapper.GetAddonText(8528)}");

                    var gilSetting = TicketUsageGilSetting;
                    if (ImGui.InputUInt("###GilInput", ref gilSetting))
                        TicketUsageGilSetting = gilSetting;
                    
                    if (ImGui.Checkbox(GetLoc("BetterTeleport-HideAethernetInParty"), ref ModuleConfig.HideAethernetInParty))
                        SaveConfig(ModuleConfig);
                }
                else
                    ImGuiOm.TooltipHover(LuminaWrapper.GetAddonText(8516));
            }
        }
        

        if (!isSettingOn) 
            DrawBottomToolbar();
    }

    private void DrawAetheryte(string name, AetheryteRecord aetheryte)
    {
        if (ModuleConfig.HideAethernetInParty && !aetheryte.IsAetheryte && DService.PartyList.Length > 1)
            return;
        
        var       columnCount = aetheryte.Group > 0 ? 3 : 4;
        using var table       = ImRaii.Table($"{name}_Table_{aetheryte.Group}", columnCount);
        if (!table) return;
        
        ImGui.TableSetupColumn("特殊标记", ImGuiTableColumnFlags.WidthFixed,   ImGui.GetTextLineHeightWithSpacing());
        ImGui.TableSetupColumn("区域",   ImGuiTableColumnFlags.WidthStretch, 30);
        if (columnCount == 4)
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.WidthStretch, 50);
        ImGui.TableSetupColumn("传送费", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("12345\uE049").X);

        ImGui.TableNextRow();

        using var id = ImRaii.PushId($"{aetheryte.RowID}");

        ImGui.TableNextColumn();
        switch (aetheryte.State)
        {
            case AetheryteRecordState.Home:
                ImGuiHelpers.SeStringWrapped(HomeChar.Encode());
                break;
            case AetheryteRecordState.Free:
            case AetheryteRecordState.FreePS:
                ImGuiHelpers.SeStringWrapped(FreeChar.Encode());
                break;
            case AetheryteRecordState.Favorite:
                ImGuiHelpers.SeStringWrapped(FavoriteChar.Encode());
                break;
        }

        if (aetheryte.Group == 0)
        {
            ImGui.TableNextColumn();
            ImGui.Text($"{aetheryte.GetZone().ExtractPlaceName()}");
        }

        var hasRemark = ModuleConfig.Remarks.TryGetValue(aetheryte.RowID, out var remark);
        ImGui.TableNextColumn();
        using (ImRaii.Group())
        {
            if (hasRemark)
            {
                var origCursorPosX = ImGui.GetCursorPosX();
                if (ImGui.Selectable(remark, false, ImGuiSelectableFlags.SpanAllColumns))
                    HandleTeleport(aetheryte);
                
                ImGui.SameLine();
                ImGui.SetCursorPosX(origCursorPosX + ImGui.CalcTextSize(remark).X + (10 * GlobalFontScale));
                ImGui.TextDisabled(aetheryte.Name);
            }
            else
            {
                if (ImGui.Selectable(aetheryte.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                    HandleTeleport(aetheryte);
            }
        }
        
#if DEBUG
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            var localPos = Control.GetLocalPlayer()->Position;
            ImGui.SetClipboardText($"// {aetheryte.Name}\n" +
                                   $"[{aetheryte.RowID}] = new({localPos.X:F2}f, {localPos.Y + 0.1f:F2}f, {localPos.Z:F2}f),");
        }
#endif
        if (!ImGui.IsPopupOpen("AetheryteContextPopup") && ImGui.IsItemHovered())
        {
            var tex = DService.Texture.GetFromGame(aetheryte.GetMap().GetTexturePath());
            
            ImGui.BeginTooltip();
            DrawAetheryteMap(tex, aetheryte);
            ImGui.EndTooltip();
        }

        using (ImRaii.PushId($"{aetheryte.RowID}_ContextPopup"))
        using (var context = ImRaii.ContextPopupItem("AetheryteContextPopup"))
        {
            if (context)
            {
                ImGui.Text($"{aetheryte.Name}");

                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.MenuItem(GetLoc("Favorite"), string.Empty, ModuleConfig.Favorites.Contains(aetheryte.RowID)))
                {
                    if (!ModuleConfig.Favorites.Add(aetheryte.RowID))
                        ModuleConfig.Favorites.Remove(aetheryte.RowID);

                    RefreshFavoritesInfo();
                    SaveConfig(ModuleConfig);
                }
                
                ImGui.Separator();
                ImGui.Spacing();
                
                ImGui.Text(GetLoc("Note"));
                
                var input = hasRemark ? remark : string.Empty; 
                ImGui.SetNextItemWidth(Math.Max(150f * GlobalFontScale, ImGui.CalcTextSize(aetheryte.Name).X));
                if (ImGui.InputText("###Note", ref input, 128))
                {
                    if (string.IsNullOrWhiteSpace(input))
                        ModuleConfig.Remarks.Remove(aetheryte.RowID);
                    else
                        ModuleConfig.Remarks[aetheryte.RowID] = input;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveConfig(ModuleConfig);
                
                ImGui.Separator();
                ImGui.Spacing();
                
                ImGui.Text(GetLoc("Position"));

                var hasPosition = ModuleConfig.Positions.TryGetValue(aetheryte.RowID, out var position);
                using (FontManager.UIFont60.Push())
                    ImGui.Text($"{(hasPosition ? position : aetheryte.Position):F1}");
                
                if (ImGui.MenuItem(GetLoc("BetterTeleport-RedirectedToCurrentPos")))
                {
                    ModuleConfig.Positions[aetheryte.RowID] = Control.GetLocalPlayer()->Position;
                    SaveConfig(ModuleConfig);
                }

                using (ImRaii.Disabled(!ModuleConfig.Positions.ContainsKey(aetheryte.RowID)))
                {
                    if (ImGui.MenuItem($"{GetLoc("Clear")}###DeleteRedirected"))
                    {
                        ModuleConfig.Positions.Remove(aetheryte.RowID);
                        SaveConfig(ModuleConfig);
                    }
                }
            }
        }

        ImGui.TableNextColumn();
        ImGui.Text($"{aetheryte.Cost}\uE049");
    }
    
    private static void DrawAetheryteMap(ISharedImmediateTexture tex, AetheryteRecord aetheryte)
    {
        var warp      = tex.GetWrapOrEmpty();
        var imageSize = ScaledVector2(384f);
        var scale     = imageSize.X / 2048f;
        
        var orig = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(orig);
        ImGui.Image(warp.Handle, imageSize);

        var itemSpacing  = ImGui.GetStyle().ItemSpacing;
        var aetherytePos = ModuleConfig.Positions.TryGetValue(aetheryte.RowID, out var redirected) ? redirected : aetheryte.Position;
        var pos          = WorldToTexture(aetherytePos, aetheryte.GetMap()) * scale;
        var iconPos      = orig + pos;
        
        ImGui.SetCursorScreenPos(iconPos - new Vector2(5 * itemSpacing.X, 3.5f * itemSpacing.Y));
        using (FontManager.UIFont160.Push())
        using (ImRaii.Group())
            ImGuiHelpers.SeStringWrapped(aetheryte.IsAetheryte ? AetheryteChar.Encode() : AethernetChar.Encode());

        var text = ModuleConfig.Remarks.TryGetValue(aetheryte.RowID, out var remark) ? remark : aetheryte.Name;
        
        ImGui.SetCursorScreenPos(iconPos - new Vector2(ImGui.CalcTextSize(text).X / 2, 1.5f * ImGui.GetTextLineHeight()));
        using (ImRaii.Group())
            ImGuiOm.TextOutlined(KnownColor.LightSkyBlue.ToVector4(), text);
    }
    
    private static void DrawBottomToolbar()
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return;
        
        DrawItem(1);
        
        ImGui.SameLine(0, 10f * GlobalFontScale);
        DrawItem(7569);
        return;

        void DrawItem(uint itemID)
        {
            using var group = ImRaii.Group();
            
            if (!LuminaGetter.TryGetRow<Item>(itemID, out var row)) return;
            if (!DService.Texture.TryGetFromGameIcon(new(row.Icon), out var texture)) return;

            ImGui.Image(texture.GetWrapOrEmpty().Handle, new(ImGui.GetTextLineHeightWithSpacing()));
            
            ImGui.SameLine();
            ImGui.Text($"{row.Name}: {manager->GetInventoryItemCount(itemID)}");
        }
    }

    private void HandleTeleport(AetheryteRecord aetheryte)
    {
        if (GameState.ContentFinderCondition != 0) return;
        
        TaskHelper.Abort();

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return;

        var hasRedirect  = ModuleConfig.Positions.TryGetValue(aetheryte.RowID, out var redirected);
        var aetherytePos = hasRedirect ? redirected : aetheryte.Position;
        
        var isSameZone = aetheryte.ZoneID == GameState.TerritoryType;
        var distance2D = !isSameZone 
                             ? 999 
                             : Vector2.DistanceSquared(localPlayer->Position.ToVector2(), aetherytePos.ToVector2());
        if (distance2D <= 900) return;

        var isPosDefault = aetherytePos.Y == 0;
        
        NotificationInfo(GetLoc("BetterTeleport-Notification", aetheryte.Name));
        SearchWord = string.Empty;
        SearchResult.Clear();

        switch (aetheryte.Group)
        {
            // 房区
            case 255:
                Telepo.Instance()->Teleport(aetheryte.RowID, aetheryte.SubIndex);
                return;
            // 天穹街
            case 254:
                TaskHelper.Enqueue(MovementManager.TeleportFirmament, "天穹街");
                TaskHelper.Enqueue(() => DService.ClientState.TerritoryType == 886 && Control.GetLocalPlayer() != null &&
                                         !MovementManager.IsManagerBusy, "等待天穹街");
                TaskHelper.Enqueue(() => MovementManager.TPSmart_InZone(aetherytePos), "区域内TP");
                TaskHelper.Enqueue(() =>
                {
                    if (MovementManager.IsManagerBusy || DService.ObjectTable.LocalPlayer == null) 
                        return false;
                    
                    MovementManager.TPGround();
                    return true;
                }, "TP到地面");
                return;
            // 野外大水晶直接传
            case 0:
                var direction = !isPosDefault
                                    ? new()
                                    : Vector2.Normalize(((Vector3)localPlayer->Position).ToVector2() - aetherytePos.ToVector2());
                var offset    = direction * 10;

                TaskHelper.Enqueue(() => MovementManager.TPSmart_BetweenZone(aetheryte.ZoneID, aetherytePos + offset.ToVector3(0)));
                if (isPosDefault)
                {
                    TaskHelper.Enqueue(() =>
                    {
                        if (MovementManager.IsManagerBusy || BetweenAreas || !IsScreenReady() ||
                            DService.Condition.Any(ConditionFlag.Mounted)) 
                            return false;
                        MovementManager.TPGround();
                        return true;
                    });
                }
                return;
        }

        // 当前在有小水晶的城区
        if (DService.ClientState.TerritoryType == aetheryte.ZoneID && aetheryte.Group != 0)
        {
            Debug("测试城区");
            // 大水晶才要偏移一下
            var offset = new Vector3();
            if (aetheryte.IsAetheryte)
            {
                var direction = !isPosDefault
                                    ? new()
                                    : Vector3.Normalize((Vector3)localPlayer->Position - aetherytePos);
                offset = direction * 10;
            }
            
            TaskHelper.Enqueue(() => MovementManager.TPSmart_InZone(aetherytePos + offset));
            if (isPosDefault)
            {
                TaskHelper.Enqueue(() =>
                {
                    if (MovementManager.IsManagerBusy || BetweenAreas || !IsScreenReady() ||
                        DService.Condition.Any(ConditionFlag.Mounted)) 
                        return false;
                    MovementManager.TPGround();
                    return true;
                });
            }
            return;
        }
        
        // 先获取当前区域任一水晶
        var aetheryteInThisZone = MovementManager.GetNearestAetheryte(Control.GetLocalPlayer()->Position, DService.ClientState.TerritoryType);
        
        // 获取不到水晶 / 不属于同一组水晶 / 附近没有能交互到的水晶 → 直接传
        if ((!isSameZone && aetheryte.Group == 0)        ||
            aetheryteInThisZone       == null            ||
            aetheryteInThisZone.Group != aetheryte.Group ||
            !TryGetNearestEventID(x => x.EventId.ContentId is EventHandlerContent.Aetheryte, _ => true,
                                                       DService.ObjectTable.LocalPlayer.Position, out var eventIDAetheryte))
        {
            // 大水晶直接传
            if (aetheryte.IsAetheryte)
            {
                Telepo.Instance()->Teleport(aetheryte.RowID, aetheryte.SubIndex);
                if (hasRedirect)
                {
                    TaskHelper.Enqueue(() => GameState.TerritoryType == aetheryte.ZoneID && Control.GetLocalPlayer() != null);
                    TaskHelper.Enqueue(() => MovementManager.TPSmart_InZone(aetherytePos));
                }

                return;
            }

            TaskHelper.Enqueue(() => MovementManager.TPSmart_BetweenZone(aetheryte.ZoneID, aetherytePos));
            if (isPosDefault)
            {
                TaskHelper.Enqueue(() =>
                {
                    if (MovementManager.IsManagerBusy || BetweenAreas || !IsScreenReady() ||
                        DService.Condition.Any(ConditionFlag.Mounted))
                        return false;
                    MovementManager.TPGround();
                    return true;
                });
            }

            return;
        }

        TaskHelper.Enqueue(() => !OccupiedInEvent);
        if (!IsAddonAndNodesReady(TelepotTown))
            TaskHelper.Enqueue(() => new EventStartPackt(Control.GetLocalPlayer()->EntityId, eventIDAetheryte).Send());
        TaskHelper.Enqueue(() =>
        {
            ClickSelectString(["都市传送网", "Aethernet", "都市転送網"]);

            var agent = AgentTelepotTown.Instance();
            if (agent == null || !agent->IsAgentActive()) return false;

            SendEvent(AgentId.TelepotTown, 1, 11, (uint)aetheryte.SubIndex);
            SendEvent(AgentId.TelepotTown, 1, 11, (uint)aetheryte.SubIndex);
            return true;
        });
        
        if (hasRedirect)
        {
            TaskHelper.Enqueue(() => GameState.TerritoryType == aetheryte.ZoneID && Control.GetLocalPlayer() != null);
            TaskHelper.Enqueue(() => MovementManager.TPSmart_InZone(aetherytePos));
        }
    }

    #region Data

    private static void RefreshFavoritesInfo()
    {
        if (ModuleConfig.Favorites.Count == 0) return;
        Favorites = ModuleConfig.Favorites
                                .Select(x => AllRecords.FirstOrDefault(d => d.RowID == x))
                                .Where(x => x != null)
                                .OfType<AetheryteRecord>()
                                .OrderBy(x => x.RowID)
                                .ToList();
    }

    private static void RefreshHouseInfo()
    {
        HouseRecords.Clear();
        foreach (var aetheryte in DService.AetheryteList)
        {
            if (!HouseZones.Contains(aetheryte.TerritoryID)) continue;
            if (!LuminaGetter.TryGetRow<Aetheryte>(aetheryte.AetheryteID, out var aetheryteRow)) continue;
            if (!LuminaGetter.TryGetRow<TerritoryType>(aetheryte.TerritoryID, out var row)) continue;

            var shareHouseName = string.Empty;
            if (aetheryte.IsSharedHouse)
            {
                var rawAddonText = LuminaGetter.GetRow<Addon>(6724)!.Value.Text.ToDalamudString();
                rawAddonText.Payloads[3] = new TextPayload(aetheryte.Ward.ToString());
                rawAddonText.Payloads[5] = new TextPayload(aetheryte.Plot.ToString());

                shareHouseName = rawAddonText.ExtractText();
            }

            var name = string.Empty;
            if (aetheryte.IsSharedHouse)
                name = shareHouseName;
            else if (aetheryte.IsApartment)
                name = LuminaWrapper.GetAddonText(6710);
            else
                name = aetheryteRow.PlaceName.Value.Name.ExtractText();

            var record = new AetheryteRecord(aetheryte.AetheryteID, aetheryte.SubIndex, 255, 0, aetheryte.TerritoryID,
                                             row.Map.RowId, true, new(aetheryte.Ward, aetheryte.SubIndex, 0),
                                             $"{aetheryteRow.Territory.Value.ExtractPlaceName()} {name}");

            HouseRecords.Add(record);
        }
    }
    
    // 天穹街
    private static void RefreshHwdInfo()
    {
        var markers = GetZoneMapMarkers(886)
                      .Where(x => x.DataType is 3 or 4)
                      .Select(x => new
                      {
                          Name     = AetheryteRecord.TryParseName(x, out var markerName) ? markerName : string.Empty,
                          Position = TextureToWorld(new(x.X, x.Y), LuminaGetter.GetRow<Map>(574)!.Value).ToVector3(0),
                          Marker   = x
                      })
                      .DistinctBy(x => x.Name);

        byte indexCounter = 0;
        foreach (var marker in markers)
        {
            var record = new AetheryteRecord(70, indexCounter, 254, 1, 886, 574, false, marker.Position, marker.Name);
            
            Records.TryAdd("3.0", []);
            Records["3.0"].Add(record);
            
            indexCounter++;
        }
    }

    #endregion
    
    private void OnPostUseAction(
        ref bool                        isPrevented,
        ref ActionType                  actionType, ref uint actionID,     ref ulong targetID, ref uint extraParam,
        ref ActionManager.UseActionMode queueState, ref uint comboRouteID)
    {
        if (actionType != ActionType.GeneralAction || actionID != 7) 
            return;

        isPrevented = true;
        
        if (GameMain.Instance()->CurrentContentFinderConditionId != 0    || IsRefreshing || BetweenAreas ||
            Control.GetLocalPlayer()                             == null || !IsScreenReady())
            return;

        UIGlobals.PlaySoundEffect(23);
        Overlay.IsOpen ^= true;
    }

    private void OnZoneChanged(ushort zone)
    {
        Overlay.IsOpen = false;
        TaskHelper.RemoveAllTasks(1);

        if (zone == 0 || GameMain.Instance()->CurrentContentFinderConditionId != 0) return;

        TaskHelper.Enqueue(() =>
        {
            try
            {
                IsRefreshing = true;

                var localPlayer = Control.GetLocalPlayer();
                if (localPlayer == null || BetweenAreas) return false;

                var instance = Telepo.Instance();
                if (instance == null) return false;

                var otherName = LuminaWrapper.GetAddonText(832);

                RefreshHouseInfo();

                if (Records.Count == 0)
                {
                    foreach (var aetheryte in MovementManager.Aetherytes)
                    {
                        // 金碟
                        if (aetheryte.Group == 5)
                        {
                            Records.TryAdd(otherName, []);
                            Records[otherName].Add(aetheryte);
                        }
                        else if (aetheryte.Version == 0)
                        {
                            var regionRow  = aetheryte.GetZone().PlaceNameRegion.Value;
                            var regionName = regionRow.RowId is 22 or 23 or 24 ? aetheryte.GetZone().PlaceNameRegion.Value.Name.ExtractText() : otherName;

                            Records.TryAdd(regionName, []);
                            Records[regionName].Add(aetheryte);
                        }
                        else
                        {
                            var versionName = $"{aetheryte.Version + 2}.0";

                            Records.TryAdd(versionName, []);
                            Records[versionName].Add(aetheryte);
                        }
                    }

                    RefreshHwdInfo();
                }

                RefreshFavoritesInfo();

            } 
            finally
            {
                IsRefreshing = false;
            }

            AllRecords.ForEach(x => TaskHelper.Enqueue(x.Update, $"更新 {x.Name} 信息", weight: -3));

            return true;
        }, "初始化信息", weight: 1);
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (string.IsNullOrWhiteSpace(args)) return;

        var result = Records.Values
                            .SelectMany(x => x)
                            .Concat(HouseRecords)
                            .Where(x =>
                            {
                                var name = string.Empty;
                                try
                                {
                                    name = x.ToString();
                                }
                                catch
                                {
                                    // ignored
                                }
                                
                                return name.Contains(args, StringComparison.OrdinalIgnoreCase);
                            })
                            .OrderByDescending(x => x.IsAetheryte)
                            .ThenBy(x => x.Name.Length)
                            .FirstOrDefault();
        
        if (result == null) return;
        
        HandleTeleport(result);
    }

    protected override void Uninit()
    {
        UseActionManager.Unreg(OnPostUseAction);
        CommandManager.RemoveCommand(Command);
        
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
    }
    
    private class Config : ModuleConfiguration
    {
        public HashSet<uint> Favorites = [];

        public Dictionary<uint, string> Remarks = [];
        
        public Dictionary<uint, Vector3> Positions = [];

        public bool HideAethernetInParty = true;
    }
}
