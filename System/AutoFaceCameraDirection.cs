using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Components;
using FFXIVClientStructs.FFXIV.Client.Game;
using Camera = FFXIVClientStructs.FFXIV.Client.Game.Camera;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoFaceCameraDirection : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title            = GetLoc("AutoFaceCameraDirectionTitle"),
        Description      = GetLoc("AutoFaceCameraDirectionDescription"),
        Category         = ModuleCategories.System,
        ModulesRecommend = ["DisableGroundActionAutoFace", "IgnoreActionTargetBlocked"]
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private const string COMMAND = "/pdrface";

    private static readonly CompSig                            CameraUpdateRotationSig = new("40 53 48 81 EC ?? ?? ?? ?? 8B 81 ?? ?? ?? ?? 48 8B D9 44 0F 29 54 24");
    private delegate        void                               CameraUpdateRotationDelegate(Camera* camera);
    private static          Hook<CameraUpdateRotationDelegate> CameraUpdateRotationHook;

    private static Config ModuleConfig = null!;

    private static float LocalPlayerRotationInput;

    private static Camera* CacheCamera;

    private static bool  LockOn;
    private static float LockOnRotation;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();

        CameraUpdateRotationHook ??= CameraUpdateRotationSig.GetHook<CameraUpdateRotationDelegate>(CameraUpdateRotationDetour);
        CameraUpdateRotationHook.Enable();

        UseActionManager.Instance().RegPostUseActionLocation(OnPostUseAction);

        CommandManager.AddCommand(COMMAND, new(OnCommand) { HelpMessage = GetLoc("AutoFaceCameraDirection-CommandHelp", COMMAND) });
    }

    protected override void Uninit()
    {
        UseActionManager.Instance().Unreg(OnPostUseAction);

        CommandManager.RemoveCommand(COMMAND);
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("Command")}");

        using (ImRaii.PushIndent())
        {
            ImGui.TextUnformatted($"{COMMAND} → {GetLoc("AutoFaceCameraDirection-CommandHelp", COMMAND)}");

            ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"<{GetLoc("Type")}>");

            using (ImRaii.PushIndent())
            {
                ImGui.TextUnformatted($"ground ({GetLoc("AutoFaceCameraDirection-GroundDirection")})");

                using (ImRaii.PushIndent())
                    ImGui.TextUnformatted($"({GroundValuesString})");

                ImGui.TextUnformatted($"chara ({GetLoc("AutoFaceCameraDirection-CharacterRotation")})");

                ImGui.TextUnformatted($"camera ({GetLoc("AutoFaceCameraDirection-CameraRotation")})");
            }
        }

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("WorkMode")}");

        ImGui.SameLine();
        if (ImGuiComponents.ToggleButton("WorkMode", ref ModuleConfig.WorkMode))
            SaveConfig(ModuleConfig);

        using (ImRaii.PushIndent())
            ImGui.TextWrapped($"{GetLoc($"AutoFaceCameraDirection-WorkMode{ModuleConfig.WorkMode}")}");

        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoFaceCameraDirection-GroundDirection")}");

        using (ImRaii.PushIndent())
        {
            foreach (var kvp in WorldDirectionToNormalizedDirection)
            {
                if (ImGui.Button($"{kvp.Key}##WorldDirectionToNormalizedDirection"))
                    localPlayer.ToStruct()->SetRotation(WorldDirHToCharaRotation(kvp.Value));
                ImGui.SameLine();
            }

            ImGui.NewLine();
        }

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{GetLoc("AutoFaceCameraDirection-CharacterRotation")}");

        using (ImRaii.ItemWidth(200f * GlobalFontScale))
        using (ImRaii.PushIndent())
        {
            ImGui.InputFloat($"{GetLoc("Settings")}##SetCharaRotation", ref LocalPlayerRotationInput, format: "%.2f");
            if (ImGui.IsItemDeactivatedAfterEdit())
                localPlayer.ToStruct()->SetRotation(LocalPlayerRotationInput);

            var currentRotation = localPlayer.Rotation;
            ImGui.InputFloat($"{GetLoc("Current")}##CurrentCharaRotation", ref currentRotation, format: "%.2f", flags: ImGuiInputTextFlags.ReadOnly);
        }

        if (CacheCamera == null) return;

        ImGui.TextColored
        (
            KnownColor.LightSkyBlue.ToVector4(),
            $"{GetLoc("AutoFaceCameraDirection-CameraRotation")} → " +
            $"{GetLoc("AutoFaceCameraDirection-CharacterRotation")}:"
        );

        using (ImRaii.PushIndent())
            ImGui.TextUnformatted($"{CacheCamera->DirH:F2} → {CameraDirHToCharaRotation(CacheCamera->DirH):F2}");
    }

    private static void OnCommand(string command, string args)
    {
        args = args.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(args))
        {
            NotifyCommandError();
            return;
        }

        var arguments = args.Split(' ');

        if (arguments.Length is not (1 or 2) || DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer)
        {
            NotifyCommandError();
            return;
        }

        var typeRaw  = arguments[0];
        var valueRaw = arguments.Length == 2 ? arguments[1] : string.Empty;

        switch (typeRaw)
        {
            case "ground":
                if (!WorldDirectionToNormalizedDirection.TryGetValue(valueRaw, out var dirGround))
                {
                    NotifyCommandError();
                    return;
                }

                LockOnRotation = WorldDirHToCharaRotation(dirGround);
                LockOn         = true;

                localPlayer.ToStruct()->SetRotation(LockOnRotation);
                break;
            case "chara":
                if (!float.TryParse(valueRaw, out var rotation))
                {
                    NotifyCommandError();
                    return;
                }

                LockOnRotation = rotation;
                LockOn         = true;
                break;
            case "camera":
                if (!float.TryParse(valueRaw, out var dirCamera))
                {
                    NotifyCommandError();
                    return;
                }

                LockOnRotation = CameraDirHToCharaRotation(dirCamera);
                LockOn         = true;

                localPlayer.ToStruct()->SetRotation(LockOnRotation);
                break;
            case "off":
                LockOn         = false;
                LockOnRotation = 0;
                break;
            default:
                NotifyCommandError();
                return;
        }

        if (!LockOn) return;

        localPlayer.ToStruct()->SetRotation(LockOnRotation);

        if (BoundByDuty)
            PositionUpdateInstancePacket.Send(LockOnRotation, localPlayer.Position);
        else
            new PositionUpdatePacket(LockOnRotation, localPlayer.Position).Send();

        return;

        void NotifyCommandError() =>
            NotificationError(GetLoc("Commands-InvalidArgs", command, args));
    }

    private static void OnPostUseAction
    (
        bool       result,
        ActionType actionType,
        uint       actionID,
        ulong      targetID,
        Vector3    location,
        uint       extraParam,
        byte       a7
    )
    {
        if (!result) return;

        Throttler.Remove("AutoFaceCameraDirection-UpdateRotationInstance");
        Throttler.Remove("AutoFaceCameraDirection-UpdateRotation");

        if (CacheCamera != null)
            CameraUpdateRotationDetour(CacheCamera);
    }

    private static void CameraUpdateRotationDetour(Camera* camera)
    {
        CameraUpdateRotationHook.Original(camera);
        CacheCamera = camera;

        if (MovementManager.IsManagerBusy ||
            OccupiedInEvent               ||
            DService.Instance().ObjectTable.LocalPlayer is not { CurrentHp: > 0 } localPlayer)
            return;

        switch (ModuleConfig.WorkMode)
        {
            case false when !IsConflictKeyPressed():
            case true when IsConflictKeyPressed():

                // 不能发包, 发了直接断读条
                if (localPlayer.IsCasting) break;

                var currentRotation = MathF.Round(localPlayer.Rotation,                                              2);
                var targetRotation  = MathF.Round(LockOn ? LockOnRotation : CameraDirHToCharaRotation(camera->DirH), 4);

                var isRotationChanged = IsRotationChanged(currentRotation, targetRotation);
                var currentMoveState  = MovementManager.CurrentZoneMoveState;

                localPlayer.ToStruct()->SetRotation(targetRotation);

                if (GameState.ContentFinderCondition != 0)
                {
                    if (!isRotationChanged && !DService.Instance().Condition[ConditionFlag.InCombat]) break;

                    var moveType = (PositionUpdateInstancePacket.MoveType)(currentMoveState * 0x10000);
                    new PositionUpdateInstancePacket(targetRotation, localPlayer.Position, moveType).Send();
                }
                else
                {
                    if (!isRotationChanged && !DService.Instance().Condition[ConditionFlag.InCombat]) break;
                    if (!Throttler.Throttle("AutoFaceCameraDirection-UpdateRotation", 20)) break;

                    var moveType = (PositionUpdatePacket.MoveType)(currentMoveState * 0x10000);
                    new PositionUpdatePacket(targetRotation, localPlayer.Position, moveType).Send();
                }

                break;
        }
    }

    private class Config : ModuleConfiguration
    {
        // true - 按下打断热键才让人物面向与摄像机一致
        // false - 按下打断热键则不保持人物面向与摄像机一致
        public bool WorkMode;
    }

    [IPCProvider("DailyRoutines.Modules.AutoFaceCameraDirection.SetWorkMode")]
    public static void SetWorkModeIPC(bool workMode) => ModuleConfig.WorkMode = workMode;

    [IPCProvider("DailyRoutines.Modules.AutoFaceCameraDirection.CancelLockOn")]
    public static void CancelLockOnIPC()
    {
        LockOn         = false;
        LockOnRotation = 0;
    }

    [IPCProvider("DailyRoutines.Modules.AutoFaceCameraDirection.LockOnGround")]
    public static bool LockOnGroundIPC(string rotation)
    {
        if (!WorldDirectionToNormalizedDirection.TryGetValue(rotation, out var dirGround))
            return false;

        LockOnRotation = WorldDirHToCharaRotation(dirGround);
        LockOn         = true;

        return true;
    }

    [IPCProvider("DailyRoutines.Modules.AutoFaceCameraDirection.LockOnChara")]
    public static void LockOnCharaIPC(float rotation)
    {
        LockOnRotation = rotation;
        LockOn         = true;
    }

    [IPCProvider("DailyRoutines.Modules.AutoFaceCameraDirection.LockOnCamera")]
    public static void LockOnCameraIPC(float rotation)
    {
        LockOnRotation = CameraDirHToCharaRotation(rotation);
        LockOn         = true;
    }

    private static readonly FrozenDictionary<string, Vector2> WorldDirectionToNormalizedDirection = new Dictionary<string, Vector2>
    {
        ["south"]     = new(0, 1),
        ["north"]     = new(0, -1),
        ["west"]      = new(-1, 0),
        ["east"]      = new(1, 0),
        ["northeast"] = new(0.707f, -0.707f),
        ["southeast"] = new(0.707f, 0.707f),
        ["northwest"] = new(-0.707f, -0.707f),
        ["southwest"] = new(-0.707f, 0.707f)
    }.ToFrozenDictionary();

    private static readonly string GroundValuesString = string.Join(" / ", WorldDirectionToNormalizedDirection.Keys);
}
