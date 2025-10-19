using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoSpearfish : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSpearfishTitle"),
        Description = GetLoc("AutoSpearfishDescription"),
        Category    = ModuleCategories.UIOperation
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private const ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.NoDecoration       |
                                                 ImGuiWindowFlags.AlwaysAutoResize   |
                                                 ImGuiWindowFlags.NoFocusOnAppearing |
                                                 ImGuiWindowFlags.NoNavFocus;

    private static Config            ModuleConfig = null!;
    private static SpotConfig        CurrentConfig => GetCurrentSpotConfig();
    private static SpearfishRenderer Renderer = null!;

    private static readonly Dictionary<SpearfishSize, string> SizeLoc = new()
    {
        [SpearfishSize.All]     = LuminaWrapper.GetAddonText(14853),
        [SpearfishSize.Small]   = LuminaWrapper.GetAddonText(3843),
        [SpearfishSize.Average] = LuminaWrapper.GetAddonText(3844),
        [SpearfishSize.Large]   = LuminaWrapper.GetAddonText(3845),
        [SpearfishSize.Unknown] = LuminaWrapper.GetAddonText(369)
    };
    
    // ItemID - 可以恢复的基础 GP
    private static readonly Dictionary<uint, uint> CordialItems = new()
    {
        [12669] = 400,
        [6141]  = 300,
        [16911] = 150
    };

    private static readonly Dictionary<uint, List<SpearfishSpot>> AllSpots;
    private static          List<SpearfishSpot>                   CurrentSpots = [];
    private static          SpearfishSpot?                        CurrentSpot;

    static AutoSpearfish()
    {
        AllSpots = SpearfishSpot.Generate()
                                .GroupBy(x => x.Zone)
                                .ToDictionary(x => x.Key, x => x.ToList());
    }

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new();

        Overlay       ??= new(this);
        Overlay.Flags =   WindowFlags;

        Renderer = new SpearfishRenderer();

        DService.ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged((ushort)GameState.TerritoryType);

        DService.Condition.ConditionChange += OnConditionChanged;
    }

    private static void OnZoneChanged(ushort zone)
    {
        if (!AllSpots.TryGetValue(GameState.TerritoryType, out var spots)) return;

        CurrentSpots = spots ?? [];
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoSpearfish-UseCordial"), ref ModuleConfig.UseCordial))
            ModuleConfig.Save(this);
        
        ImGui.NewLine();
        
        using var tabBar = ImRaii.TabBar("###AutoSpearfishConfigTabs");
        if (!tabBar) return;

        using (var item = ImRaii.TabItem(GetLoc("AutoSpearfish-AllSpots")))
        {
            if (item)
            {
                // 已配置渔场列表
                RenderSpotList();

                ImGui.NewLine();

                // 所有渔场浏览器
                RenderAllSpotsBrowser();
            }
        }
        
        using (var item = ImRaii.TabItem(GetLoc("DefaultConfig")))
        {
            if (item)
            {
                ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("AutoSpearfish-DefaultConfigHelp"));
                
                ImGui.NewLine();
                
                RenderConfigEditor(ModuleConfig.DefaultConfig);
            }
        }
    }

    private void RenderSpotList()
    {
        using var table = ImRaii.Table("###SpotConfigsTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table) return;
        
        ImGui.TableSetupColumn(LuminaWrapper.GetGatheringPointName(21), ImGuiTableColumnFlags.WidthStretch, 20); // 渔场
        ImGui.TableSetupColumn(LuminaWrapper.GetAddonText(13613),       ImGuiTableColumnFlags.WidthStretch, 50); // 能钓到的鱼
        ImGui.TableSetupColumn(GetLoc("Operation"),                     ImGuiTableColumnFlags.WidthStretch, 20); // 操作
        ImGui.TableHeadersRow();

        List<uint> spotsToRemove = [];

        foreach (var spotConfig in ModuleConfig.SpotConfigs)
        {
            var spotInfo = AllSpots.SelectMany(x => x.Value).FirstOrDefault(s => s.ID == spotConfig.Key);
            if (spotInfo == null) continue;
            
            using var id = ImRaii.PushId($"既有渔场{spotConfig.Key}");
            
            ImGui.TableNextRow();
            
            // 渔场
            ImGui.TableNextColumn();
            ImGui.Text($"{spotInfo.GetData().PlaceName.Value.Name.ExtractText()} [\uE06A {spotInfo.Level}]\n\n" +
                       $"({spotInfo.GetZoneData().ExtractPlaceName()})");

            // 鱼种
            ImGui.TableNextColumn();
            RenderFishList(spotInfo.Fishes);

            // 操作按钮
            ImGui.TableNextColumn();

            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Edit, GetLoc("Edit")))
                ImGui.OpenPopup($"编辑渔场{spotConfig.Key}配置");

            // 编辑弹窗
            using (var popup = ImRaii.Popup($"编辑渔场{spotConfig.Key}配置"))
            {
                if (popup)
                    RenderConfigEditor(spotConfig.Value);
            }

            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.LocationArrow, GetLoc("Locate")))
            {
                AgentMap.Instance()->SetFlagMapMarker(spotInfo.Zone, spotInfo.GetZoneData().Map.RowId, spotInfo.Center.ToVector3(0));
                AgentMap.Instance()->OpenMap(spotInfo.GetZoneData().Map.RowId, spotInfo.Zone, spotInfo.Name);
            }
            
            ImGui.SameLine();
            if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.TrashAlt, GetLoc("Delete")))
                spotsToRemove.Add(spotConfig.Key);
        }

        foreach (var spotID in spotsToRemove)
        {
            ModuleConfig.SpotConfigs.Remove(spotID);
            ModuleConfig.Save(this);
        }
    }

    private void RenderAllSpotsBrowser()
    {
        using var table = ImRaii.Table("AllSpotsBrowser", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                                       new Vector2(-1, 400 * ImGuiHelpers.GlobalScale));
        if (!table) return;

        ImGui.TableSetupColumn(LuminaWrapper.GetGatheringPointName(21), ImGuiTableColumnFlags.WidthStretch, 20); // 渔场
        ImGui.TableSetupColumn(LuminaWrapper.GetAddonText(13613),       ImGuiTableColumnFlags.WidthStretch, 50); // 能钓到的鱼
        ImGui.TableSetupColumn(GetLoc("Operation"),                     ImGuiTableColumnFlags.WidthStretch, 20); // 操作
        ImGui.TableHeadersRow();

        // 按区域分组显示
        foreach (var zoneGroup in AllSpots.OrderBy(x => x.Key))
        {
            var zoneName     = LuminaWrapper.GetZonePlaceName(zoneGroup.Key);
            var anySpotsLeft = false;

            foreach (var spot in zoneGroup.Value)
            {
                if (!ModuleConfig.SpotConfigs.ContainsKey(spot.ID))
                {
                    anySpotsLeft = true;
                    break;
                }
            }

            if (!anySpotsLeft) continue;

            // 区域标题行
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            using (FontManager.UIFont120.Push())
            {
                ScaledDummy(1f, 8f);
                ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), zoneName);
            }

            // 显示该区域的所有未配置渔场
            foreach (var spot in zoneGroup.Value.OrderBy(x => x.Name))
            {
                if (ModuleConfig.SpotConfigs.ContainsKey(spot.ID))
                    continue;
                
                using var id = ImRaii.PushId($"全部渔场{spot.ID}");

                ImGui.TableNextRow();

                // 渔场
                ImGui.TableNextColumn();
                ImGui.Text($"{spot.GetData().PlaceName.Value.Name.ExtractText()} [\uE06A {spot.Level}]\n\n" +
                           $"({spot.GetZoneData().ExtractPlaceName()})");

                // 鱼种
                ImGui.TableNextColumn();
                RenderFishList(spot.Fishes);

                // 操作按钮
                ImGui.TableNextColumn();
                if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.Plus, GetLoc("Add")))
                {
                    ModuleConfig.SpotConfigs[spot.ID] = SpotConfig.CopyFromDefault();
                    ModuleConfig.Save(this);
                }

                ImGui.SameLine();
                if (ImGuiOm.ButtonIconWithText(FontAwesomeIcon.LocationArrow, GetLoc("Locate")))
                {
                    AgentMap.Instance()->SetFlagMapMarker(spot.Zone, spot.GetZoneData().Map.RowId, spot.Center.ToVector3(0));
                    AgentMap.Instance()->OpenMap(spot.GetZoneData().Map.RowId, spot.Zone, spot.Name);
                }
            }
        }
    }

    private static void RenderFishList(List<uint> fishes)
    {
        if (fishes.Count == 0) return;

        var itemSpacing      = ImGui.GetStyle().ItemSpacing.X;
        var isFirstItem      = true;
        var currentLineWidth = 0f;
        
        var isInTable   = ImGui.TableGetColumnCount() > 0;
        var columnWidth = isInTable ? ImGui.GetColumnWidth() : ImGui.GetContentRegionAvail().X;
        
        var cellPadding    = ImGui.GetStyle().CellPadding.X * 2;
        var availableWidth = columnWidth - (isInTable ? cellPadding : 0);

        foreach (var fishId in fishes)
        {
            if (!LuminaGetter.TryGetRow<SpearfishingItem>(fishId, out var row)) continue;

            var item = row.Item.Value;
            if (string.IsNullOrWhiteSpace(item.Name.ExtractText())) continue;

            var fishName = item.Name.ExtractText();
            var textWidth = ImGui.CalcTextSize(fishName).X;
            var itemWidth = textWidth;
            
            var iconWidth = 0f;
            if (DService.Texture.TryGetFromGameIcon(new(item.Icon), out var texture))
            {
                iconWidth =  ImGui.GetTextLineHeight() + (4f * GlobalFontScale);
                itemWidth += iconWidth;
            }
            
            if (!isFirstItem)
            {
                if (currentLineWidth + itemWidth + itemSpacing > availableWidth)
                {
                    isFirstItem = true;
                    currentLineWidth = 0f;
                }
                else
                {
                    ImGui.SameLine(0, itemSpacing);
                    currentLineWidth += itemSpacing;
                }
            }
            
            if (iconWidth > 0)
                ImGuiOm.TextImage(fishName, texture.GetWrapOrEmpty().Handle, new(ImGui.GetTextLineHeight()));
            else
                ImGui.Text(fishName);
            
            currentLineWidth += itemWidth;
            isFirstItem = false;
        }
    }

    private void RenderConfigEditor(SpotConfig config)
    {
        // 尺寸
        var currentSize = (int)config.Size;
        var allSizes = Enum.GetValues<SpearfishSize>()
                           .Where(x => x != SpearfishSize.Unknown)
                           .Select(x => SizeLoc.GetValueOrDefault(x, LuminaWrapper.GetAddonText(369)))
                           .ToArray();
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo($"{LuminaWrapper.GetAddonText(6814)}##Size", ref currentSize, allSizes, allSizes.Length))
        {
            config.Size = (SpearfishSize)currentSize;
            ModuleConfig.Save(this);
        }

        // 速度
        var currentSpeed = (int)config.Speed;
        var allSpeeds = Enum.GetValues<SpearfishSpeed>()
                            .Where(x => x != SpearfishSpeed.Unknown)
                            .Select(x => x == SpearfishSpeed.All ? LuminaWrapper.GetAddonText(14853) : $"{(int)x}")
                            .ToArray();
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo($"{LuminaWrapper.GetAddonText(3153)}##Speed", ref currentSpeed, allSpeeds, allSpeeds.Length))
        {
            config.Speed = (SpearfishSpeed)currentSpeed;
            ModuleConfig.Save(this);
        }

        // 判定框大小
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt($"{GetLoc("AutoSpearfish-HitboxSize")}##HitboxSize", ref config.HitboxSize))
        {
            config.HitboxSize = Math.Clamp(config.HitboxSize, 0, 300);
            ModuleConfig.Save(this);
        }

        ImGui.NewLine();

        // 技能设置
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("Action"));
        
        if (ImGui.Checkbox(LuminaWrapper.GetActionName(7909), ref config.NatureBounty)) // 嘉惠
            ModuleConfig.Save(this);

        ImGui.SameLine();
        ImGui.TextDisabled("|");

        ImGui.SameLine();
        if (ImGui.Checkbox(LuminaWrapper.GetActionName(26804), ref config.ThaliaksFavor)) // 沙利亚克的恩宠
            ModuleConfig.Save(this);
    }

    protected override void OverlayUI()
    {
        var addon = (SpearfishWindow*)SpearFishing;
        if (!IsAddonAndNodesReady((AtkUnitBase*)addon)) return;

        Renderer.UIScale    = addon->AtkUnitBase.Scale;
        Renderer.UIPosition = new Vector2(addon->AtkUnitBase.X, addon->AtkUnitBase.Y);
        Renderer.UISize = new Vector2(addon->AtkUnitBase.WindowNode->AtkResNode.Width  * addon->AtkUnitBase.Scale,
                                      addon->AtkUnitBase.WindowNode->AtkResNode.Height * addon->AtkUnitBase.Scale);

        ImGui.SetWindowPos(new(addon->AtkUnitBase.X + 5, addon->AtkUnitBase.Y - ImGui.GetWindowSize().Y));

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return;

        // 绘制设置UI
        Renderer.DrawSettings();
        Renderer.DrawSpotInfo();

        // 始终优先用沙利亚克的恩宠
        SkillManager.TryUseThaliaksFavor();

        // 处理鱼类
        ProcessFish(addon, addon->Fish1, addon->Fish1Node);
        ProcessFish(addon, addon->Fish2, addon->Fish2Node);
        ProcessFish(addon, addon->Fish3, addon->Fish3Node);
    }
    
    private static void ProcessFish(SpearfishWindow* addon, SpearfishWindow.Info info, AtkResNode* node)
    {
        if (!info.Available) return;

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return;

        var drawList = ImGui.GetForegroundDrawList();

        // 绘制命中框
        Renderer.DrawGigHitbox(addon);

        // 计算鱼的碰撞箱
        var fishHitbox = Renderer.CalculateFishHitbox(node, info.InverseDirection);

        // 绘制鱼的碰撞箱
        Renderer.DrawFishHitbox(addon, drawList, fishHitbox, info);

        if (SpearfishLogic.IsFishMeetDemands(info))
            SkillManager.TryUseNatureBounty();

        // 判断是否应该捕获鱼
        var centerX = Renderer.UISize.X / 2;
        if (SpearfishLogic.ShouldCatchFish(info, fishHitbox, centerX, CurrentConfig.HitboxSize))
            SpearfishLogic.CatchFish();
    }
    
    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.Gathering || !DService.Condition[ConditionFlag.Diving] || LocalPlayerState.ClassJob != 18)
            return;

        CurrentSpot    = value ? CurrentSpots.Where(x => x.IsInside()).OrderBy(x => x.GetDistanceSquared()).FirstOrDefault() : null;
        Overlay.IsOpen = value;

        if (!value)
        {
            TaskHelper.Abort();
            TaskHelper.Enqueue(() => !OccupiedInEvent);
            TaskHelper.Enqueue(() =>
            {
                if (!TryGetAnyCordialToUse(out var item, out var isHQ)) return;
                var cordial = item.Value;
                
                AgentInventoryContext.Instance()->UseItem((uint)(cordial.ItemId + (isHQ ? 100_0000 : 0)),
                                                          cordial.GetInventoryType(),
                                                          (uint)cordial.Slot);
            });
        }
    }
    
    private static bool TryGetAnyCordialToUse([NotNullWhen(true)] out InventoryItem? item, out bool isHQ)
    {
        item = null;
        isHQ = false;

        if (!ModuleConfig.UseCordial) 
            return false;

        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null) return false;

        var gp    = localPlayer->GatheringPoints;
        var maxGP = localPlayer->MaxGatheringPoints;
        if (gp + 50 >= maxGP) return false;

        foreach (var (cordial, baseGP) in CordialItems)
        {
            if (!ActionManager.Instance()->IsActionOffCooldown(ActionType.Item, cordial)) continue;
            if (TryGetInventoryItems(PlayerInventories, x => x.ItemId % 100_0000 == cordial, out var validItems))
            {
                validItems = validItems
                             .OrderByDescending(x => x.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality))
                             .ToList();

                var firstItem = validItems.First();
                var isItemHQ  = firstItem.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);

                // 除了高级强心剂都要加 50
                var gpToRecover = cordial == 12699 ? baseGP : isItemHQ ? baseGP + 50 : baseGP;
                if (gpToRecover + 50 + gp >= maxGP) continue;

                item = firstItem;
                isHQ = isItemHQ;
                return true;
            }
        }

        return false;
    }
    
    private static SpotConfig GetCurrentSpotConfig()
    {
        if (CurrentSpot != null && ModuleConfig.SpotConfigs.TryGetValue(CurrentSpot.ID, out var config))
            return config;
        
        return ModuleConfig.DefaultConfig;
    }

    protected override void Uninit()
    {
        DService.ClientState.TerritoryChanged -= OnZoneChanged;
        DService.Condition.ConditionChange    -= OnConditionChanged;
    }

    private class SpearfishRenderer
    {
        public Vector2     UIPosition    { get; set; } = Vector2.Zero;
        public Vector2     UISize        { get; set; } = Vector2.Zero;
        public float       UIScale       { get; set; } = 1;
        public SpotConfig? EditingConfig { get; set; }
        
        public void DrawSettings()
        {
            if (ImGuiOm.ButtonIcon("OpenSettings", FontAwesomeIcon.Cog))
            {
                EditingConfig = ModuleConfig.SpotConfigs.GetOrAdd(CurrentSpot.ID, _ => SpotConfig.CopyFromDefault());
                ImGui.OpenPopup("编辑配置");
            }
            
            using (var popup = ImRaii.Popup("编辑配置"))
            {
                if (popup)
                {
                    if (EditingConfig != null) 
                        ModuleManager.GetModule<AutoSpearfish>().RenderConfigEditor(EditingConfig);
                }
            }
            
            ImGui.SameLine();
            ImGui.Text($"{LuminaWrapper.GetAddonText(6814)}: {SizeLoc.GetValueOrDefault(CurrentConfig.Size, LuminaWrapper.GetAddonText(369))}"); // 尺寸

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            // 速度
            ImGui.SameLine();
            ImGui.Text($"{LuminaWrapper.GetAddonText(3153)}: " +
                       $"{(CurrentConfig.Speed == SpearfishSpeed.All ? LuminaWrapper.GetAddonText(14853) : $"{(int)CurrentConfig.Speed}")}");

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            ImGui.Text($"{GetLoc("AutoSpearfish-HitboxSize")}: {CurrentConfig.HitboxSize}");

            // 显示技能状态
            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            if (CurrentConfig.NatureBounty)
                ImGui.TextColored(KnownColor.GreenYellow.ToVector4(), LuminaWrapper.GetActionName(7909)); // 嘉惠
            else
                ImGui.TextDisabled(LuminaWrapper.GetActionName(7909));

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            if (CurrentConfig.ThaliaksFavor)
                ImGui.TextColored(KnownColor.GreenYellow.ToVector4(), LuminaWrapper.GetActionName(26804)); // 沙利亚克的恩宠
            else
                ImGui.TextDisabled(LuminaWrapper.GetActionName(26804));
        }

        public void DrawSpotInfo()
        {
            if (CurrentSpot == null) return;

            if (ImGui.Begin("FishSpotInfo", WindowFlags))
            {
                ImGui.SetWindowPos(UIPosition - new Vector2(ImGui.GetWindowWidth(), 0));

                using (FontManager.UIFont140.Push())
                    ImGui.Text($"{CurrentSpot.Name}");
                
                ScaledDummy(1f, 5f);
                
                foreach (var fish in CurrentSpot.Fishes)
                {
                    if (!LuminaGetter.TryGetRow<SpearfishingItem>(fish, out var row)) continue;

                    var item = row.Item.Value;
                    if (string.IsNullOrWhiteSpace(item.Name.ExtractText())) continue;
                    if (!DService.Texture.TryGetFromGameIcon(new(item.Icon), out var texture)) continue;

                    ImGuiOm.TextImage($"{item.Name.ExtractText()} ({row.FishingRecordType.Value.Addon.Value.Text.ExtractText()})",
                                      texture.GetWrapOrEmpty().Handle, new(ImGui.GetTextLineHeight()));
                }

                ImGui.End();
            }
        }

        public void DrawGigHitbox(SpearfishWindow* addon)
        {
            var drawList = ImGui.GetWindowDrawList();
            if (!IsAddonAndNodesReady((AtkUnitBase*)addon)) return;

            var space = CurrentConfig.HitboxSize;

            var startX  = UISize.X                 / 2;
            var centerY = addon->FishLines->Y      * UIScale;
            var endY    = addon->FishLines->Height * UIScale;

            var lineStart = UIPosition + new Vector2(startX - space, centerY);
            var lineEnd   = lineStart  + new Vector2(0,              endY);
            drawList.AddLine(lineStart, lineEnd, KnownColor.Gold.ToVector4().ToUInt(), 2 * ImGuiHelpers.GlobalScale);

            lineStart = UIPosition + new Vector2(startX + space, centerY);
            lineEnd   = lineStart  + new Vector2(0,              endY);
            drawList.AddLine(lineStart, lineEnd, KnownColor.Gold.ToVector4().ToUInt(), 2 * ImGuiHelpers.GlobalScale);
        }

        public void DrawFishHitbox(SpearfishWindow* addon, ImDrawListPtr drawList, float fishHitbox, SpearfishWindow.Info info)
        {
            if (!IsAddonAndNodesReady(&addon->AtkUnitBase)) return;

            var lineStart = UIPosition + new Vector2(fishHitbox, addon->FishLines->Y      * UIScale);
            var lineEnd   = lineStart  + new Vector2(0,          addon->FishLines->Height * UIScale);

            drawList.AddLine(lineStart, lineEnd, KnownColor.Goldenrod.ToVector4().ToUInt(), 1 * ImGuiHelpers.GlobalScale);
            ImGuiOm.TextOutlined(lineStart, KnownColor.Orange.ToVector4().ToUInt(),
                                 $"{LuminaWrapper.GetAddonText(6814)}: {SizeLoc.GetValueOrDefault(info.Size, LuminaWrapper.GetAddonText(369))} / " +
                                 $"{LuminaWrapper.GetAddonText(3153)}: {(int)info.Speed}",
                                 drawList: drawList);
        }

        public float CalculateFishHitbox(AtkResNode* node, bool inverseDirection)
        {
            if (inverseDirection)
                return (node->X * UIScale) + (node->Width * node->ScaleX * UIScale * 0.5f) + (5f * UIScale);
            return (node->X * UIScale) + (node->Width * node->ScaleX * UIScale * 0.4f) - (5f * UIScale);
        }
    }

    private static class SpearfishLogic
    {
        public static bool IsFishMeetDemands(SpearfishWindow.Info fishInfo)
        {
            // 正在出叉
            if (DService.Condition[ConditionFlag.ExecutingGatheringAction])
                return false;

            // 速度过滤
            if (CurrentConfig.Speed != SpearfishSpeed.All && CurrentConfig.Speed != fishInfo.Speed)
                return false;

            // 尺寸过滤
            if (CurrentConfig.Size != SpearfishSize.All && CurrentConfig.Size != fishInfo.Size)
                return false;

            return true;
        }

        public static bool ShouldCatchFish(SpearfishWindow.Info fishInfo, float fishHitbox, float centerX, int hitboxSize)
        {
            if (!IsFishMeetDemands(fishInfo))
                return false;

            // 位置判断
            return fishHitbox >= centerX - hitboxSize && fishHitbox <= centerX + hitboxSize;
        }

        public static void CatchFish()
        {
            Throttler.Throttle("AutoSpearfish-CatchFish");
            UseActionManager.UseAction(ActionType.Action, 7632);
        }
    }

    private static class SkillManager
    {
        public static bool TryUseNatureBounty()
        {
            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null) return false;

            if (!CurrentConfig.NatureBounty)
                return false;

            if (localPlayer->GatheringPoints <= 100)
                return false;

            if (localPlayer->StatusManager.HasStatus(1171))
                return false;

            if (!Throttler.Check("AutoSpearfish-NatureBounty") || !Throttler.Check("AutoSpearfish-CatchFish"))
                return false;

            Throttler.Throttle("AutoSpearfish-NatureBounty", 1_000);
            UseActionManager.UseAction(ActionType.Action, 7909);
            return true;
        }

        public static bool TryUseThaliaksFavor()
        {
            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null) return false;

            if (!CurrentConfig.ThaliaksFavor)
                return false;

            var statusIndex = localPlayer->StatusManager.GetStatusIndex(2778);
            if (statusIndex == -1)
                return false;

            if (localPlayer->MaxGatheringPoints - localPlayer->GatheringPoints < 150)
                return false;

            if (localPlayer->StatusManager.Status[statusIndex].Param < 3)
                return false;

            if (!Throttler.Check("AutoSpearfish-ThaliaksFavor") || !Throttler.Check("AutoSpearfish-CatchFish"))
                return false;

            Throttler.Throttle("AutoSpearfish-ThaliaksFavor", 1_000);
            UseActionManager.UseAction(ActionType.Action, 26804);
            return true;
        }

        public static bool TryUseCordial()
        {
            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null) return false;

            if (!ModuleConfig.UseCordial)
                return false;
            
            if (!TryGetAnyCordialToUse(out var item, out var isHQ))
                return false;

            var cordial = item.Value;
            
            // 避免溢出强心剂
            if (localPlayer->GatheringPoints + CordialItems.GetValueOrDefault(cordial.ItemId, 0U) + 20U >= localPlayer->MaxGatheringPoints)
                return false;

            if (!Throttler.Check("AutoSpearfish-Cordial"))
                return false;

            Throttler.Throttle("AutoSpearfish-Cordial", 1_000);
            AgentInventoryContext.Instance()->UseItem((uint)(cordial.ItemId + (isHQ ? 100_0000 : 0)),
                                                      cordial.GetInventoryType(),
                                                      (uint)cordial.Slot);
            return true;
        }
    }

    private class Config : ModuleConfiguration
    {
        public bool UseCordial = true;
        
        // 默认配置
        public SpotConfig DefaultConfig = new();

        // 渔场特定配置，键为渔场ID
        public Dictionary<uint, SpotConfig> SpotConfigs = new();
    }

    // 单个渔场的配置
    private class SpotConfig
    {
        public SpearfishSize  Size          = SpearfishSize.All;
        public SpearfishSpeed Speed         = SpearfishSpeed.All;
        public bool           CatchAll      = true;
        public int            HitboxSize    = 25;
        public bool           NatureBounty  = true;
        public bool           ThaliaksFavor = true;
        
        public static SpotConfig CopyFromDefault() => new()
        {
            Size          = ModuleConfig.DefaultConfig.Size,
            Speed         = ModuleConfig.DefaultConfig.Speed,
            CatchAll      = ModuleConfig.DefaultConfig.CatchAll,
            HitboxSize    = ModuleConfig.DefaultConfig.HitboxSize,
            NatureBounty  = ModuleConfig.DefaultConfig.NatureBounty,
            ThaliaksFavor = ModuleConfig.DefaultConfig.ThaliaksFavor
        };
    }

    private enum SpearfishSize : byte
    {
        All     = 0,
        Small   = 1,
        Average = 2,
        Large   = 3,
        Unknown = 255
    }

    private enum SpearfishSpeed : ushort
    {
        All           = 0,
        SuperSlow     = 100,
        ExtremelySlow = 150,
        VerySlow      = 200,
        Slow          = 250,
        Average       = 300,
        Fast          = 350,
        VeryFast      = 400,
        ExtremelyFast = 450,
        SuperFast     = 500,
        HyperFast     = 550,
        LynFast       = 600,
        Unknown       = 65535
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct SpearfishWindow
    {
        [FieldOffset(0)]
        public AtkUnitBase AtkUnitBase;

        [StructLayout(LayoutKind.Explicit)]
        public struct Info
        {
            [FieldOffset(8)]
            public bool Available;

            [FieldOffset(16)]
            public bool InverseDirection;

            [FieldOffset(17)]
            public bool GuaranteedLarge;

            [FieldOffset(18)]
            public SpearfishSize Size;

            [FieldOffset(20)]
            public SpearfishSpeed Speed;
        }

        [FieldOffset(0x294)]
        public Info Fish1;

        [FieldOffset(0x2B0)]
        public Info Fish2;

        [FieldOffset(0x2CC)]
        public Info Fish3;


        public AtkResNode* FishLines
            => AtkUnitBase.UldManager.NodeList[3];

        public AtkResNode* Fish1Node
            => AtkUnitBase.UldManager.NodeList[15];

        public AtkResNode* Fish2Node
            => AtkUnitBase.UldManager.NodeList[16];

        public AtkResNode* Fish3Node
            => AtkUnitBase.UldManager.NodeList[17];

        public AtkComponentGaugeBar* GaugeBar
            => (AtkComponentGaugeBar*)AtkUnitBase.UldManager.NodeList[35];
    }

    private class SpearfishSpot : IEquatable<SpearfishSpot>
    {
        public uint       ID     { get; set; }
        public string     Name   { get; set; } = string.Empty;
        public ushort     Level  { get; set; }
        public uint       Zone   { get; set; }
        public Vector2    Center { get; set; } = Vector2.Zero; // 解析好了
        public float      Radius { get; set; }
        public List<uint> Fishes { get; set; } = [];

        public SpearfishingNotebook GetData() =>
            LuminaGetter.GetRow<SpearfishingNotebook>(ID).GetValueOrDefault();
        
        public TerritoryType GetZoneData() =>
            LuminaGetter.GetRow<TerritoryType>(Zone).GetValueOrDefault();
        
        public bool IsInside()
        {
            if (GameState.TerritoryType != Zone) return false;

            var distance2D = GetDistanceSquared();
            if (distance2D == -1f) return false;

            return distance2D <= Radius * Radius;
        }

        public float GetDistanceSquared()
        {
            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null) return -1f;

            var pos2D = localPlayer->Position.ToVector2();
            return Vector2.DistanceSquared(pos2D, Center);
        }

        public static List<SpearfishSpot> Generate()
        {
            var data = LuminaGetter.Get<SpearfishingNotebook>()
                                   .Where(x => x.TerritoryType.RowId > 0 && x.GatheringPointBase.RowId > 0);

            var result = new List<SpearfishSpot>();
            foreach (var spot in data)
            {
                var id     = spot.RowId; // 使用数据行ID作为渔场ID
                var level  = spot.GatheringLevel;
                var radius = spot.Radius + 50;
                var name   = spot.PlaceName.Value.Name.ExtractText();
                var fishes = spot.GatheringPointBase.Value.Item.Select(x => x.RowId).Where(x => x > 0).ToList();
                var center = TextureToWorld(new(spot.X, spot.Y), spot.TerritoryType.Value.Map.Value);
                var zone   = spot.TerritoryType.RowId;

                result.Add(new()
                {
                    ID     = id,
                    Name   = name,
                    Zone   = zone,
                    Level  = level,
                    Center = center,
                    Radius = radius,
                    Fishes = fishes
                });
            }

            return result;
        }

        public override bool Equals(object? obj) =>
            Equals(obj as SpearfishSpot);

        public override int GetHashCode() =>
            (int)ID;

        public static bool operator ==(SpearfishSpot? left, SpearfishSpot? right) =>
            Equals(left, right);

        public static bool operator !=(SpearfishSpot? left, SpearfishSpot? right) =>
            !Equals(left, right);

        public bool Equals(SpearfishSpot? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ID == other.ID;
        }
    }
}
