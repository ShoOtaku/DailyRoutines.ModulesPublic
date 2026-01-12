using System;
using DailyRoutines.Abstracts;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.Modules;

public unsafe class AutoNotifyLeveUpdate : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoNotifyLeveUpdateTitle"),
        Description = GetLoc("AutoNotifyLeveUpdateDescription"),
        Category    = ModuleCategories.Notice,
        Author      = ["HSS"]
    };

    private static Config ModuleConfig = null!;

    private static DateTime NextLeveCheck = DateTime.MinValue;
    private static DateTime FinishTime    = DateTime.UtcNow;
    private static int      LastLeve;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        FrameworkManager.Instance().Reg(OnUpdate, throttleMS: 60_000);
    }

    protected override void ConfigUI()
    {
        ImGui.TextUnformatted($"{Lang.Get("AutoNotifyLeveUpdate-NumText")}{LastLeve}");
        ImGui.TextUnformatted($"{Lang.Get("AutoNotifyLeveUpdate-FullTimeText")}{FinishTime.ToLocalTime():g}");
        ImGui.TextUnformatted($"{Lang.Get("AutoNotifyLeveUpdate-UpdateTimeText")}{NextLeveCheck.ToLocalTime():g}");

        if (ImGui.Checkbox(Lang.Get("AutoNotifyLeveUpdate-OnChatMessageConfig"), ref ModuleConfig.OnChatMessage))
            SaveConfig(ModuleConfig);

        ImGui.SetNextItemWidth(200f * GlobalFontScale);

        if (ImGui.SliderInt
            (
                Lang.Get("AutoNotifyLeveUpdate-NotificationThreshold"),
                ref ModuleConfig.NotificationThreshold,
                1,
                100
            ))
        {
            LastLeve = 0;
            SaveConfig(ModuleConfig);
        }
    }

    private static void OnUpdate(IFramework _)
    {
        if (!DService.Instance().ClientState.IsLoggedIn || DService.Instance().ObjectTable.LocalPlayer == null)
            return;

        var nowUTC         = DateTime.UtcNow;
        var leveAllowances = QuestManager.Instance()->NumLeveAllowances;
        if (LastLeve == leveAllowances) return;

        var decreasing = leveAllowances > LastLeve;
        LastLeve      = leveAllowances;
        NextLeveCheck = MathNextTime(nowUTC);
        FinishTime    = MathFinishTime(leveAllowances, nowUTC);

        if (leveAllowances >= ModuleConfig.NotificationThreshold && decreasing)
        {
            var message = $"{Lang.Get("AutoNotifyLeveUpdate-NotificationTitle")}\n"                        +
                          $"{Lang.Get("AutoNotifyLeveUpdate-NumText")}{leveAllowances}\n"                  +
                          $"{Lang.Get("AutoNotifyLeveUpdate-FullTimeText")}{FinishTime.ToLocalTime():g}\n" +
                          $"{Lang.Get("AutoNotifyLeveUpdate-UpdateTimeText")}{NextLeveCheck.ToLocalTime():g}";

            if (ModuleConfig.OnChatMessage)
                Chat(message);
            NotificationInfo(message);
        }
    }

    private static DateTime MathNextTime(DateTime nowUTC) =>
        nowUTC.AddHours(nowUTC.Hour >= 12 ? 24 - nowUTC.Hour : 12 - nowUTC.Hour).Date;

    private static DateTime MathFinishTime(int num, DateTime nowUTC)
    {
        if (num >= 100) return nowUTC;
        var requiredPeriods      = (100 - num + 2) / 3;
        var lastIncrementTimeUTC = new DateTime(nowUTC.Year, nowUTC.Month, nowUTC.Day, nowUTC.Hour >= 12 ? 12 : 0, 0, 0, DateTimeKind.Utc);
        return lastIncrementTimeUTC.AddHours(12 * requiredPeriods);
    }

    protected override void Uninit() =>
        FrameworkManager.Instance().Unreg(OnUpdate);

    private class Config : ModuleConfiguration
    {
        public bool OnChatMessage         = true;
        public int  NotificationThreshold = 97;
    }
}
