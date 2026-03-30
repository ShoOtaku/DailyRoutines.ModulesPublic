using System.Numerics;
using System.Runtime.InteropServices;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using DailyRoutines.Internal;
using DailyRoutines.Manager;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools.Dalamud;
using OmenTools.Interop.Game;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class RightClickToMoveMode : ModuleBase
{
    public enum ControlMode
    {
        RightClick,
        LeftRightClick,
        KeyRightClick
    }

    public enum MoveMode
    {
        Game,
        vnavmesh
    }

    private static readonly Dictionary<ControlMode, (string Title, string Desc)> ControlModes = new()
    {
        [ControlMode.RightClick]     = (Lang.Get("RightClickToMoveMode-RightClickMode-Title"), Lang.Get("RightClickToMoveMode-RightClickMode-Desc")),
        [ControlMode.LeftRightClick] = (Lang.Get("RightClickToMoveMode-LeftRightClickMode-Title"), Lang.Get("RightClickToMoveMode-LeftRightClickMode-Desc")),
        [ControlMode.KeyRightClick]  = (Lang.Get("RightClickToMoveMode-KeyRightClickMode-Title"), Lang.Get("RightClickToMoveMode-KeyRightClickMode-Desc"))
    };

    private static readonly CompSig                              GameObjectSetRotationSig = new("40 53 48 83 EC ?? F3 0F 10 81 ?? ?? ?? ?? 48 8B D9 0F 2E C1");
    private static          Hook<GameObjectSetRotationDelegate>? GameObjectSetRotationHook;

    private static volatile bool IsModuleActive;

    private static readonly uint LineColor = KnownColor.LightSkyBlue.ToVector4().ToUInt();
    private static readonly uint DotColor  = KnownColor.RoyalBlue.ToVector4().ToUInt();
    private static readonly uint TextColor = KnownColor.Orange.ToVector4().ToUInt();

    private static Vector3 TargetWorldPos;

    private static Config                   ModuleConfig;
    private static MovementInputController? PathFindHelper;

    private static WindowHook? Hook;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("RightClickToMoveModeTitle"),
        Description = Lang.Get("RightClickToMoveModeDescription"),
        Category    = ModuleCategory.General
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        GameObjectSetRotationHook ??= GameObjectSetRotationSig.GetHook<GameObjectSetRotationDelegate>(GameObjectSetRotationDetour);
        GameObjectSetRotationHook.Enable();

        if (!DService.Instance().PI.IsPluginEnabled(vnavmeshIPC.InternalName))
        {
            ModuleConfig.MoveMode = MoveMode.Game;
            ModuleConfig.Save(this);
        }

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;

        if (IsModuleActive) return;
        IsModuleActive = true;

        try
        {
            PathFindHelper ??= new MovementInputController { Precision = 1f };

            Hook ??= new(Framework.Instance()->GameWindow->WindowHandle, HandleClickResult);

            WindowManager.Instance().PostDraw += OnPosDraw;
        }
        catch
        {
            CleanupResources();
            throw;
        }
    }

    protected override void ConfigUI()
    {
        ImGuiOm.ConflictKeyText();

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("RightClickToMoveMode-MoveMode")}");

        using (ImRaii.PushIndent())
        {
            ImGui.Spacing();

            foreach (var moveMode in Enum.GetValues<MoveMode>())
            {
                using var disabled = ImRaii.Disabled
                (
                    ModuleConfig.MoveMode == moveMode ||
                    moveMode == MoveMode.vnavmesh && !DService.Instance().PI.IsPluginEnabled(vnavmeshIPC.InternalName)
                );

                ImGui.SameLine();

                if (ImGui.RadioButton(moveMode.ToString(), moveMode == ModuleConfig.MoveMode))
                {
                    ModuleConfig.MoveMode = moveMode;
                    ModuleConfig.Save(this);
                }
            }
        }

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), $"{Lang.Get("RightClickToMoveMode-ControlMode")}");

        using (ImRaii.PushIndent())
        {
            ImGui.Spacing();

            foreach (var controlMode in Enum.GetValues<ControlMode>())
            {
                using var disabled = ImRaii.Disabled(controlMode == ModuleConfig.ControlMode);

                ImGui.SameLine();

                if (ImGui.RadioButton(ControlModes[controlMode].Title, controlMode == ModuleConfig.ControlMode))
                {
                    ModuleConfig.ControlMode = controlMode;
                    ModuleConfig.Save(this);
                }
            }

            ImGui.TextUnformatted(ControlModes[ModuleConfig.ControlMode].Desc);

            if (ModuleConfig.ControlMode == ControlMode.KeyRightClick)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted($"{Lang.Get("RightClickToMoveMode-ComboKey")}:");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f * GlobalUIScale);
                using var combo = ImRaii.Combo("###ComboKeyCombo", ModuleConfig.ComboKey.GetFancyName());

                if (combo)
                {
                    var validKeys = DService.Instance().KeyState.GetValidVirtualKeys();

                    foreach (var keyToSelect in validKeys)
                    {
                        using var disabled = ImRaii.Disabled(PluginConfig.Instance().ConflictKeyBinding.Keyboard == keyToSelect);

                        if (ImGui.Selectable(keyToSelect.GetFancyName()))
                        {
                            ModuleConfig.ComboKey = keyToSelect;
                            ModuleConfig.Save(this);
                        }
                    }
                }
            }
        }

        ImGui.NewLine();

        if (ImGui.Checkbox($"{Lang.Get("RightClickToMoveMode-DisplayLineToTarget")}###DisplayLineToTarget", ref ModuleConfig.DisplayLineToTarget))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox($"{Lang.Get("RightClickToMoveMode-NoChangeFaceDirection")}###NoChangeFaceDirection", ref ModuleConfig.NoChangeFaceDirection))
            ModuleConfig.Save(this);

        if (ImGui.Checkbox($"{Lang.Get("RightClickToMoveMode-WASDToInterrupt")}###WASDToInterrupt", ref ModuleConfig.WASDToInterrupt))
            ModuleConfig.Save(this);
    }

    protected override void Uninit() =>
        CleanupResources();

    private static void OnPosDraw()
    {
        if (!IsModuleActive) return;

        if (TargetWorldPos == default) return;
        if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return;

        MovementManager.SetCurrentControlMode(MovementControlMode.Normal);

        var distance = Vector2.DistanceSquared(TargetWorldPos.ToVector2(), localPlayer.Position.ToVector2());
        if (IsInterruptKeysPressed() || distance <= 4f)
            StopPathFind();

        if (!ModuleConfig.DisplayLineToTarget) return;

        if (!DService.Instance().GameGUI.WorldToScreen(TargetWorldPos,       out var screenPos) ||
            !DService.Instance().GameGUI.WorldToScreen(localPlayer.Position, out var localScreenPos))
            return;

        var drawList = ImGui.GetForegroundDrawList();

        drawList.AddLine(localScreenPos, screenPos, LineColor, 8f);
        drawList.AddCircleFilled(localScreenPos, 12f, DotColor);
        drawList.AddCircleFilled(screenPos,      12f, DotColor);

        ImGuiOm.TextOutlined
        (
            screenPos + ScaledVector2(16f),
            TextColor,
            Lang.Get
            (
                "RightClickToMoveMode-TextDisplay",
                $"[{TargetWorldPos.X:F1}, {TargetWorldPos.Y:F1}, {TargetWorldPos.Z:F1}]",
                MathF.Sqrt(distance).ToString("F2")
            )
        );
    }

    private static void OnZoneChanged(ushort obj) =>
        StopPathFind();

    private static void GameObjectSetRotationDetour(nint obj, float value)
    {
        if (ModuleConfig.NoChangeFaceDirection                                                    &&
            obj            == (DService.Instance().ObjectTable.LocalPlayer?.Address ?? nint.Zero) &&
            TargetWorldPos != default)
            return;

        GameObjectSetRotationHook.Original(obj, value);
    }

    private static bool IsInterruptKeysPressed()
    {
        if (PluginConfig.Instance().ConflictKeyBinding.IsPressed()) return true;
        if (ModuleConfig.WASDToInterrupt &&
            (DService.Instance().KeyState[VirtualKey.W] ||
             DService.Instance().KeyState[VirtualKey.A] ||
             DService.Instance().KeyState[VirtualKey.S] ||
             DService.Instance().KeyState[VirtualKey.D]))
            return true;

        return false;
    }

    private static void HandleClickResult()
    {
        if (!IsModuleActive) return;
        if (DService.Instance().ObjectTable.LocalPlayer is null || PathFindHelper == null) return;

        switch (ModuleConfig.ControlMode)
        {
            case ControlMode.RightClick:
                break;
            case ControlMode.LeftRightClick:
                var isLeftButtonPressed = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (!isLeftButtonPressed) return;
                break;
            case ControlMode.KeyRightClick:
                var isKeyPressed = DService.Instance().KeyState[ModuleConfig.ComboKey];
                if (!isKeyPressed) return;
                break;
        }

        if (!DService.Instance().GameGUI.ScreenToWorld(ImGui.GetMousePos(), out var worldPos)) return;

        var finalWorldPos = Vector3.Zero;
        if (DService.Instance().PI.IsPluginEnabled(vnavmeshIPC.InternalName) &&
            vnavmeshIPC.QueryNearestPointOnMesh(worldPos, 3, 10) is { } worldPosByNavmesh)
            finalWorldPos = worldPosByNavmesh;
        else if (MovementManager.TryDetectGroundDownwards(worldPos, out var hitInfo, 1024) ?? false)
            finalWorldPos = hitInfo.Point;
        else
            return;

        StopPathFind();
        TargetWorldPos = finalWorldPos;

        if (AgentMap.Instance()->IsPlayerMoving)
            ChatManager.Instance().SendMessage("/automove off");

        switch (ModuleConfig.MoveMode)
        {
            case MoveMode.Game:
                PathFindHelper.DesiredPosition = finalWorldPos;
                PathFindHelper.Enabled         = true;
                break;
            case MoveMode.vnavmesh:
                if (!DService.Instance().PI.IsPluginEnabled(vnavmeshIPC.InternalName))
                {
                    ModuleConfig.MoveMode = MoveMode.Game;
                    ModuleConfig.Save(ModuleManager.GetModule<RightClickToMoveMode>());
                    return;
                }

                vnavmeshIPC.SetPathfindTolerance(2f);
                vnavmeshIPC.PathfindAndMoveTo
                    (TargetWorldPos, DService.Instance().Condition[ConditionFlag.InFlight] || DService.Instance().Condition[ConditionFlag.Diving]);
                break;
        }
    }

    private static void StopPathFind()
    {
        TargetWorldPos = default;

        if (PathFindHelper != null)
            PathFindHelper.Enabled = false;

        vnavmeshIPC.StopPathfind();
    }

    private static void CleanupResources()
    {
        if (!IsModuleActive) return;

        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;
        WindowManager.Instance().PostDraw                               -= OnPosDraw;

        Hook?.Dispose();

        if (PathFindHelper != null)
        {
            PathFindHelper.Dispose();
            PathFindHelper = null;
        }

        TargetWorldPos = default;
        IsModuleActive = false;
    }

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    private delegate void GameObjectSetRotationDelegate(nint obj, float value);

    private class Config : ModuleConfig
    {
        public VirtualKey  ComboKey            = VirtualKey.SHIFT;
        public ControlMode ControlMode         = ControlMode.RightClick;
        public bool        DisplayLineToTarget = true;
        public MoveMode    MoveMode            = MoveMode.Game;
        public bool        NoChangeFaceDirection;
        public bool        WASDToInterrupt = true;
    }

    public class WindowHook
    {
        private const int WH_MOUSE       = 7;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_ACTIVATE    = 0x0006;

        private static nint           HookID = nint.Zero;
        private static Win32MouseProc MouseProc;

        private static nint       GameWindowHandle;
        private static bool       IsModuleActive = true;
        private static Action     HandleClickCallback;
        private        nint       oldWndProc;
        private        WindowProc windowHookProc;

        public WindowHook(nint windowHandle, Action clickCallback)
        {
            GameWindowHandle    = windowHandle;
            HandleClickCallback = clickCallback;
            MouseProc           = MouseHookCallback;

            windowHookProc = WndProc;
            oldWndProc     = GetWindowLongPtr(GameWindowHandle, -4);
            SetWindowLongPtr(GameWindowHandle, -4, Marshal.GetFunctionPointerForDelegate(windowHookProc));

            StartHook();
        }

        private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            if (msg == WM_ACTIVATE)
            {
                StopHook();
                StartHook();
            }

            return CallWindowProc(oldWndProc, hWnd, msg, wParam, lParam);
        }

        private static void StartHook()
        {
            if (GameWindowHandle == nint.Zero) return;

            var threadID = GetWindowThreadProcessID(GameWindowHandle, out _);
            HookID = SetWindowsHookEx(WH_MOUSE, MouseProc, nint.Zero, threadID);

            if (HookID == nint.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"Failed to set mouse hook, error code: {error}");
            }
        }

        private static void StopHook()
        {
            if (HookID != nint.Zero)
            {
                UnhookWindowsHookEx(HookID);
                HookID = nint.Zero;
            }
        }

        private static nint MouseHookCallback(int nCode, nint wParam, nint lParam)
        {
            if (!IsModuleActive) return CallNextHookEx(nint.Zero, nCode, wParam, lParam);

            if (nCode >= 0 && HookID != nint.Zero)
            {
                var mouseStruct = (MouseHook)Marshal.PtrToStructure(lParam, typeof(MouseHook));

                if ((int)wParam == WM_RBUTTONDOWN && mouseStruct.hwnd == GameWindowHandle)
                {
                    var clientPoint = new Vector2 { X = mouseStruct.pt.X, Y = mouseStruct.pt.Y };
                    ScreenToClient(GameWindowHandle, ref clientPoint);

                    HandleClickCallback();
                }
            }

            return CallNextHookEx(HookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessID(nint hWnd, out uint lpdwProcessID);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
        private static extern nint SetWindowsHookEx(int idHook, Win32MouseProc lpfn, nint hMod, uint dwThreadID);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "UnhookWindowsHookEx")]
        private static extern bool UnhookWindowsHookEx(nint hhk);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "CallNextHookEx")]
        private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "ScreenToClient")]
        private static extern bool ScreenToClient(nint hWnd, ref Vector2 lpPoint);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

        public void Dispose()
        {
            StopHook();
            if (GameWindowHandle != nint.Zero && oldWndProc != nint.Zero)
                SetWindowLongPtr(GameWindowHandle, -4, oldWndProc);
        }

        private delegate nint Win32MouseProc(int nCode, nint wParam, nint lParam);

        private delegate nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseHook
        {
            public Vector2 pt;
            public nint    hwnd;
            public uint    wHitTestCode;
            public nint    dwExtraInfo;
        }
    }
}
