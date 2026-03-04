using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace DailyRoutines.ModulesPublic;

public class AutoRespawnTeleport : DailyModuleBase
{
    private const int RetryIntervalMs = 500;
    private const int MaxRetryCount = 20;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRespawnTeleportTitle"),
        Description = GetLoc("AutoRespawnTeleportDescription"),
        Category    = ModuleCategories.Combat
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static Config ModuleConfig = null!;

    private bool ArmedByDeathTransition;
    private bool LastBetweenAreas;
    private int RetryCount;
    private bool HasDeathPosition;
    private Vector3 DeathPosition;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        TaskHelper ??= new() { TimeoutMS = 60_000 };

        LastBetweenAreas = IsBetweenAreas();
        DService.Instance().Framework.Update += OnFrameworkUpdate;
    }

    protected override void Uninit()
    {
        DService.Instance().Framework.Update -= OnFrameworkUpdate;
        TaskHelper?.Abort();
        ResetRuntimeState(IsBetweenAreas());
    }

    protected override void ConfigUI()
    {
        if (ImGui.RadioButton(GetLoc("AutoRespawnTeleport-ModeFixed"),
                ModuleConfig.TeleportMode == RespawnTeleportMode.FixedCoordinate))
        {
            ModuleConfig.TeleportMode = RespawnTeleportMode.FixedCoordinate;
            SaveConfig(ModuleConfig);
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(GetLoc("AutoRespawnTeleport-ModeDeath"),
                ModuleConfig.TeleportMode == RespawnTeleportMode.DeathCoordinate))
        {
            ModuleConfig.TeleportMode = RespawnTeleportMode.DeathCoordinate;
            SaveConfig(ModuleConfig);
        }

        ImGui.Spacing();

        if (ModuleConfig.TeleportMode == RespawnTeleportMode.FixedCoordinate)
        {
            var target = ModuleConfig.TargetCoordinate;
            ImGui.SetNextItemWidth(200f * GlobalFontScale);
            if (ImGui.InputFloat3(GetLoc("AutoRespawnTeleport-TargetCoordinate"), ref target, format: "%.2f"))
            {
                ModuleConfig.TargetCoordinate = target;
                SaveConfig(ModuleConfig);
            }

            ImGui.SameLine();
            if (ImGui.Button(GetLoc("AutoRespawnTeleport-UseCurrentPosition")) &&
                DService.Instance().ObjectTable.LocalPlayer is { } localPlayer)
            {
                ModuleConfig.TargetCoordinate = localPlayer.Position;
                SaveConfig(ModuleConfig);
            }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var betweenAreas = IsBetweenAreas();
        var isUnconscious = DService.Instance().Condition[ConditionFlag.Unconscious];

        if (betweenAreas && isUnconscious)
        {
            if (!ArmedByDeathTransition)
                CaptureDeathPosition();
            ArmedByDeathTransition = true;
        }

        if (ArmedByDeathTransition && LastBetweenAreas && !betweenAreas)
        {
            ArmedByDeathTransition = false;
            BeginTeleportLoop();
        }

        LastBetweenAreas = betweenAreas;
    }

    private void BeginTeleportLoop()
    {
        TaskHelper!.Abort();
        RetryCount = 0;
        TaskHelper.Enqueue(TryTeleportTask);
    }

    private bool TryTeleportTask()
    {
        if (!TryResolveTargetCoordinate(out var target)) return true;

        if (DService.Instance().ObjectTable.LocalPlayer == null || MovementManager.IsManagerBusy)
            return RetryLater();

        MovementManager.TPSmart_InZone(target);
        if (MovementManager.IsManagerBusy)
            return true;

        return RetryLater();
    }

    private bool RetryLater()
    {
        RetryCount++;
        if (RetryCount >= MaxRetryCount) return true;

        TaskHelper!.DelayNext(RetryIntervalMs);
        TaskHelper.Enqueue(TryTeleportTask);
        return true;
    }

    private bool TryResolveTargetCoordinate(out Vector3 target)
    {
        if (ModuleConfig.TeleportMode == RespawnTeleportMode.DeathCoordinate)
        {
            if (HasDeathPosition)
            {
                target = DeathPosition;
                return true;
            }

            target = Vector3.Zero;
            return false;
        }

        target = ModuleConfig.TargetCoordinate;
        return target != Vector3.Zero;
    }

    private void CaptureDeathPosition()
    {
        if (DService.Instance().ObjectTable.LocalPlayer is not { } player)
        {
            HasDeathPosition = false;
            return;
        }

        DeathPosition = player.Position;
        HasDeathPosition = true;
    }

    private void ResetRuntimeState(bool betweenAreas)
    {
        ArmedByDeathTransition = false;
        RetryCount = 0;
        HasDeathPosition = false;
        LastBetweenAreas = betweenAreas;
    }

    private static bool IsBetweenAreas()
    {
        var condition = DService.Instance().Condition;
        return condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];
    }

    private class Config : ModuleConfiguration
    {
        public RespawnTeleportMode TeleportMode = RespawnTeleportMode.FixedCoordinate;
        public Vector3 TargetCoordinate = Vector3.Zero;
    }

    private enum RespawnTeleportMode
    {
        FixedCoordinate = 0,
        DeathCoordinate = 1
    }
}
