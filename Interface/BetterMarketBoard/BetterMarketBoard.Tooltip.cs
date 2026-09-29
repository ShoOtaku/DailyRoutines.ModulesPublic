using System.Globalization;
using DailyRoutines.Common.RemoteInteraction.Enums;
using DailyRoutines.Common.RemoteInteraction.Models;
using DailyRoutines.RemoteInteraction.Universalis;
using DailyRoutines.RemoteInteraction.Universalis.Models.Responses;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using TimeAgo;

namespace DailyRoutines.ModulesPublic.Interface;

public partial class BetterMarketBoard
{
    private void OnItemTooltipUpdate
    (
        ItemKind                          kind,
        uint                              itemID,
        ref List<TooltipItemModification> modifications
    )
    {
        if (!config.AppendMarketStatsTooltip)
            return;

        if (kind is ItemKind.Collectible or ItemKind.EventItem)
            return;

        if (itemID == 0                                         ||
            !LuminaGetter.TryGetRow<Item>(itemID, out var item) ||
            item.ItemSearchCategory.RowId == 0                  ||
            GameState.CurrentWorld        == 0)
            return;

        var hqOnly            = kind is ItemKind.Hq && item.CanBeHq;
        var currentRegionName = GetCurrentRegionName();
        var currentWorldName  = LuminaWrapper.GetWorldName(GameState.CurrentWorld);
        var currentWorldScope = string.IsNullOrEmpty(currentWorldName) ?
                                    GameState.CurrentWorld.ToString() :
                                    currentWorldName;
        var requestTargets = string.IsNullOrEmpty(currentRegionName) ?
                                 [currentWorldScope] :
                                 new[] { currentWorldScope, currentRegionName };

        foreach (var scope in requestTargets)
            provider.RequestTooltipAggregatedScope(itemID, scope);

        var currentWorldSnapshot = RemoteUniversalisAggregatedMarket.GetOrRequest([itemID], currentWorldScope);
        var currentRegionSnapshot = string.IsNullOrEmpty(currentRegionName) ?
                                        default :
                                        RemoteUniversalisAggregatedMarket.GetOrRequest([itemID], currentRegionName);
        var marketStats = CreateTooltipMarketStats(itemID, hqOnly, currentWorldSnapshot, currentRegionSnapshot);

        modifications.Add
        (
            new()
            {
                Target = TooltipItemType.Description,
                Type   = TooltipModificationType.Append,
                Text = BuildItemTooltipMarketText
                (
                    marketStats == null,
                    marketStats?.CurrentWorldMinPrice,
                    marketStats?.CurrentWorldTimeText,
                    marketStats?.RegionMinWorldName ?? string.Empty,
                    marketStats?.RegionMinPrice,
                    marketStats?.RegionTimeText,
                    marketStats?.DailySales ?? 0
                )
            }
        );
    }

    private static ReadOnlySeString BuildItemTooltipMarketText
    (
        bool    isRequesting,
        ulong?  currentWorldMinPrice,
        string? currentWorldTimeText,
        string  regionMinWorldName,
        ulong?  regionMinPrice,
        string? regionTimeText,
        float   dailySales
    )
    {
        using var rented  = new RentedSeStringBuilder();
        var       builder = rented.Builder;

        // 标题
        builder.Append($"[{LuminaWrapper.GetAddonText(6556)}]");

        if (isRequesting)
        {
            builder
                .AppendNewLine()
                .PushColorType(32)
                .Append($"    \ue031 {LuminaWrapper.GetAddonText(2717)}")
                .PopColorType();

            return builder.ToReadOnlySeString();
        }

        // 当前服务器最低价
        builder.AppendNewLine()
               .Append($"    {Lang.Get("BetterMarketBoard-Tooltip-CurrentWorld")}")
               .Append(" (")
               .AppendIcon((uint)BitmapFontIcon.CrossWorld)
               .Append($"{GameState.CurrentWorldData.Name}")
               .Append(")");

        builder.Append(": ")
               .PushColorType(32)
               .Append(FormatPrice(currentWorldMinPrice))
               .PopColorType();

        if (!string.IsNullOrEmpty(currentWorldTimeText))
            builder.Append($" ({currentWorldTimeText})");

        // 当前区域最低价格
        builder.AppendNewLine()
               .Append($"    {Lang.Get("BetterMarketBoard-Tooltip-CurrentRegion")}");

        if (!string.IsNullOrEmpty(regionMinWorldName))
        {
            builder.Append(" (")
                   .AppendIcon((uint)BitmapFontIcon.CrossWorld)
                   .Append(regionMinWorldName)
                   .Append(")");
        }

        builder.Append(": ")
               .PushColorType(32)
               .Append(FormatPrice(regionMinPrice))
               .PopColorType();

        if (!string.IsNullOrEmpty(regionTimeText))
            builder.Append($" ({regionTimeText})");

        // 当前区域日销量
        builder.AppendNewLine()
               .Append($"    {Lang.Get("BetterMarketBoard-Tooltip-DailySales")}: ")
               .PushColorType(45)
               .Append(FormatSales(dailySales))
               .PopColorType();

        return builder.ToReadOnlySeString();

        static string FormatPrice
        (
            ulong? price
        ) =>
            price is > 0 ?
                $"{price.Value.ToChineseString()}\ue049" :
                "-";

        static string FormatSales
        (
            float sales
        ) =>
            sales > 0 ?
                ((int)MathF.Ceiling(sales)).ToChineseString() :
                "-";
    }

    private string GetCurrentRegionName() =>
        allWorlds.FirstOrDefault(region => region.Value.Values.Any(dc => dc.ContainsKey(GameState.CurrentWorld))).Key;

    private static TooltipMarketStats? CreateTooltipMarketStats
    (
        uint                                                    itemID,
        bool                                                    hqOnly,
        RemoteSnapshot<UniversalisAggregatedMarketDataResponse> currentWorldSnapshot,
        RemoteSnapshot<UniversalisAggregatedMarketDataResponse> currentRegionSnapshot
    )
    {
        if (!TryGetAggregatedResult(currentWorldSnapshot, itemID, out var currentWorldResult))
            return null;

        if (!TryGetAggregatedResult(currentRegionSnapshot, itemID, out var currentRegionResult))
            return null;

        var currentWorldData     = GetAggregatedMarketScope(currentWorldResult,  hqOnly);
        var currentRegionData    = GetAggregatedMarketScope(currentRegionResult, hqOnly);
        var currentWorldMinPrice = ToPrice(currentWorldData.MinListing.World.Price);
        var currentWorldTimeText = ToTimeAgo(currentWorldData.RecentPurchase.World.Timestamp);
        var regionMinListing     = currentRegionData.MinListing.Region;
        var regionMinPrice       = ToPrice(regionMinListing.Price);
        var regionMinWorldName = regionMinListing.WorldID is > 0 ?
                                     LuminaWrapper.GetWorldName(regionMinListing.WorldID.Value) :
                                     string.Empty;
        var regionTimeText = ToTimeAgo(currentRegionData.RecentPurchase.Region.Timestamp);
        var dailySales     = currentRegionData.DailySaleVelocity.Region.Quantity ?? 0;

        if (currentWorldSnapshot.Status is not (RemoteSnapshotStatus.Ready or RemoteSnapshotStatus.Refreshing) ||
            currentRegionSnapshot.Status is not (RemoteSnapshotStatus.Ready or RemoteSnapshotStatus.Refreshing))
            return null;

        return new(currentWorldMinPrice, currentWorldTimeText, regionMinWorldName, regionMinPrice, regionTimeText, dailySales);

        static ulong? ToPrice
        (
            double? price
        ) =>
            price is > 0 ?
                (ulong)Math.Round(price.Value) :
                null;

        static string? ToTimeAgo
        (
            long? timestamp
        ) =>
            timestamp is > 0 ?
                DateTimeOffset.FromUnixTimeMilliseconds(timestamp.Value).LocalDateTime.TimeAgo() :
                null;

    }

    private static UniversalisAggregatedMarketScope GetAggregatedMarketScope
    (
        UniversalisAggregatedMarketResult result,
        bool                              hqOnly
    ) =>
        hqOnly ?
            result.HQ :
            result.NQ;

    private static bool TryGetAggregatedResult
    (
        RemoteSnapshot<UniversalisAggregatedMarketDataResponse> snapshot,
        uint                                                    itemID,
        out UniversalisAggregatedMarketResult?                  result
    )
    {
        result = null;

        if (!snapshot.HasValue || snapshot.Value == null)
            return false;

        result = snapshot.Value.Results.FirstOrDefault(x => x.ItemID == itemID);
        return result != null;
    }

    private sealed record TooltipMarketStats
    (
        ulong?  CurrentWorldMinPrice,
        string? CurrentWorldTimeText,
        string  RegionMinWorldName,
        ulong?  RegionMinPrice,
        string? RegionTimeText,
        float   DailySales
    );
}
