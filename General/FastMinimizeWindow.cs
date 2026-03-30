using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using DailyRoutines.Manager;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public unsafe class FastMinimizeWindow : ModuleBase
{
    private static Config ModuleConfig = null!;

    private static NotifyIcon? TrayIcon;

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("FastMinimizeWindowTitle"),
        Description = Lang.Get("FastMinimizeWindowDescription", CommandMini, CommandTray),
        Category    = ModuleCategory.General,
        Author      = ["Rorinnn"]
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        CommandManager.Instance().AddSubCommand(CommandMini, new(OnMinimizeCommand) { HelpMessage = Lang.Get("FastMinimizeWindow-MinimizeToTaskbar") });
        CommandManager.Instance().AddSubCommand(CommandTray, new(OnTrayCommand) { HelpMessage     = Lang.Get("FastMinimizeWindow-MinimizeToTray") });

        WindowManager.Instance().PostDraw += DrawMinimizeButton;

        if (ModuleConfig.AlwaysAddTrayIcon)
            CreateTrayIcon();
    }

    protected override void Uninit()
    {
        WindowManager.Instance().PostDraw -= DrawMinimizeButton;
        CommandManager.Instance().RemoveSubCommand(CommandMini);
        CommandManager.Instance().RemoveSubCommand(CommandTray);

        if (IsEnabled)
        {
            var hwnd = Framework.Instance()->GameWindow->WindowHandle;

            if (hwnd != nint.Zero && !IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SwShow);
                SetForegroundWindow(hwnd);
            }
        }

        DisposeTrayIcon();
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), Lang.Get("Command"));

        using (ImRaii.PushIndent())
        {
            ImGui.TextUnformatted($"/pdr {CommandMini} → {Lang.Get("FastMinimizeWindow-MinimizeToTaskbar")}");
            ImGui.TextUnformatted($"/pdr {CommandTray} → {Lang.Get("FastMinimizeWindow-MinimizeToTray")}");
        }

        ImGui.NewLine();

        if (ImGui.Checkbox(Lang.Get("FastMinimizeWindow-AlwaysAddTrayIcon"), ref ModuleConfig.AlwaysAddTrayIcon))
        {
            if (ModuleConfig.AlwaysAddTrayIcon)
                CreateTrayIcon();
            else
                DisposeTrayIcon();

            ModuleConfig.Save(this);
        }

        ImGuiOm.HelpMarker(Lang.Get("FastMinimizeWindow-AlwaysAddTrayIcon-Help"));

        ImGui.NewLine();

        if (ImGui.Checkbox(Lang.Get("FastMinimizeWindow-DrawButton"), ref ModuleConfig.DrawButton))
            ModuleConfig.Save(this);

        if (ModuleConfig.DrawButton)
        {
            using (ImRaii.ItemWidth(250f * GlobalUIScale))
            using (ImRaii.PushIndent())
            {
                if (ImGui.Checkbox(Lang.Get("FastMinimizeWindow-IsTransparentWhenNotHovered"), ref ModuleConfig.IsTransparentWhenNotHovered))
                    ModuleConfig.Save(this);

                if (ImGui.SliderFloat(Lang.Get("Scale"), ref ModuleConfig.Scale, 0.1f, 2f))
                    ModuleConfig.Scale = Math.Clamp(ModuleConfig.Scale, 0.1f, 2f);
                if (ImGui.IsItemDeactivatedAfterEdit())
                    ModuleConfig.Save(this);

                DrawBehaviorCombo(Lang.Get("FastMinimizeWindow-LeftClickBehavior"),  ref ModuleConfig.LeftClickBehavior);
                DrawBehaviorCombo(Lang.Get("FastMinimizeWindow-RightClickBehavior"), ref ModuleConfig.RightClickBehavior);

                using (var combo = ImRaii.Combo(Lang.Get("Position"), Lang.Get($"{ModuleConfig.Position}")))
                {
                    if (combo)
                    {
                        foreach (var buttonPosition in Enum.GetValues<ButtonPosition>())
                        {
                            if (ImGui.Selectable(Lang.Get($"{buttonPosition}", buttonPosition == ModuleConfig.Position)))
                            {
                                ModuleConfig.Position = buttonPosition;
                                ModuleConfig.Save(this);
                            }
                        }
                    }
                }
            }
        }
    }

    private void DrawBehaviorCombo(string label, ref ClickBehavior behavior)
    {
        using var combo = ImRaii.Combo(label, BehaviorNames.GetValueOrDefault(behavior, LuminaWrapper.GetAddonText(7)));
        if (!combo) return;

        foreach (var (behaviour, name) in BehaviorNames)
        {
            if (ImGui.Selectable(name, behavior == behaviour))
            {
                behavior = behaviour;
                ModuleConfig.Save(this);
            }
        }
    }

    private void DrawMinimizeButton()
    {
        if (!ModuleConfig.DrawButton) return;

        var buttonSize = 20f * ModuleConfig.Scale;
        var windowPos = ModuleConfig.Position switch
        {
            ButtonPosition.TopLeft     => new Vector2(0f,                                            0f),
            ButtonPosition.TopRight    => new Vector2(ImGuiHelpers.MainViewport.Size.X - buttonSize, 0f),
            ButtonPosition.BottomLeft  => new Vector2(0f,                                            ImGuiHelpers.MainViewport.Size.Y - buttonSize),
            ButtonPosition.BottomRight => ImGuiHelpers.MainViewport.Size - new Vector2(buttonSize),
            _                          => Vector2.Zero
        };

        using var style1 = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var style2 = ImRaii.PushStyle(ImGuiStyleVar.WindowMinSize, Vector2.Zero);

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(windowPos);

        if (ImGui.Begin("##DRMinimizeButton", ButtonWindowFlags))
        {
            using var color = ModuleConfig.IsTransparentWhenNotHovered ? ImRaii.PushColor(ImGuiCol.Button, 0u) : null;
            if (ImGui.Button("##MinimizeBtn", new Vector2(buttonSize, buttonSize)))
                HandleClick(ModuleConfig.LeftClickBehavior);

            if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                HandleClick(ModuleConfig.RightClickBehavior);

            ImGui.End();
        }
    }

    private void HandleClick(ClickBehavior behavior)
    {
        switch (behavior)
        {
            case ClickBehavior.MinimizeToTaskbar:
                TryMinimize();
                break;
            case ClickBehavior.MinimizeToTray:
                TryMinimizeToTray();
                break;
        }
    }

    private static void OnMinimizeCommand(string command, string args) => TryMinimize();

    private void OnTrayCommand(string command, string args) => TryMinimizeToTray();

    private static void TryMinimize()
    {
        var hwnd = Framework.Instance()->GameWindow->WindowHandle;
        if (hwnd != nint.Zero)
            ShowWindow(hwnd, SwMinimize);
    }

    private void TryMinimizeToTray()
    {
        var hwnd = Framework.Instance()->GameWindow->WindowHandle;
        if (hwnd == nint.Zero) return;

        if (TrayIcon?.Visible is not true && !CreateTrayIcon())
            return;

        ShowWindow(hwnd, SwHide);
    }

    private static Icon? TryExtractGameIcon()
    {
        var mainModule = Process.GetCurrentProcess().MainModule;
        return mainModule?.FileName is { } fileName
                   ? Icon.ExtractAssociatedIcon(fileName)
                   : null;
    }

    private bool CreateTrayIcon()
    {
        if (TrayIcon?.Visible is true) return true;

        DisposeTrayIcon();

        try
        {
            TrayIcon = new NotifyIcon
            {
                Icon    = TryExtractGameIcon() ?? SystemIcons.Application,
                Text    = Info.Title,
                Visible = true
            };

            TrayIcon.Click += OnTrayIconClick;
            return true;
        }
        catch
        {
            TrayIcon = null;
            return false;
        }
    }

    private static void OnTrayIconClick(object? sender, EventArgs e)
    {
        var hwnd = Framework.Instance()->GameWindow->WindowHandle;
        if (hwnd == nint.Zero) return;

        if (!IsWindowVisible(hwnd))
        {
            ShowWindow(hwnd, SwShow);
            SetForegroundWindow(hwnd);

            if (!ModuleConfig.AlwaysAddTrayIcon)
                DisposeTrayIcon();
        }
        else
        {
            ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }
    }

    private static void DisposeTrayIcon()
    {
        if (TrayIcon is null) return;

        TrayIcon.Click   -= OnTrayIconClick;
        TrayIcon.Visible =  false;
        TrayIcon.Dispose();
        TrayIcon = null;
    }

    private class Config : ModuleConfig
    {
        public bool           AlwaysAddTrayIcon;
        public bool           DrawButton = true;
        public bool           IsTransparentWhenNotHovered;
        public ClickBehavior  LeftClickBehavior  = ClickBehavior.MinimizeToTaskbar;
        public ButtonPosition Position           = ButtonPosition.TopRight;
        public ClickBehavior  RightClickBehavior = ClickBehavior.MinimizeToTray;
        public float          Scale              = 0.5f;
    }

    #region 预定义

    private enum ClickBehavior
    {
        None,
        MinimizeToTaskbar,
        MinimizeToTray
    }

    private enum ButtonPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    #endregion

    #region 数据

    private const string CommandMini = "mini";
    private const string CommandTray = "tray";

    private const int SwMinimize = 6;
    private const int SwHide     = 0;
    private const int SwShow     = 5;
    private const int SwRestore  = 9;

    private const ImGuiWindowFlags ButtonWindowFlags =
        ImGuiWindowFlags.AlwaysAutoResize      |
        ImGuiWindowFlags.NoNavFocus            |
        ImGuiWindowFlags.NoFocusOnAppearing    |
        ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoTitleBar            |
        ImGuiWindowFlags.NoMove                |
        ImGuiWindowFlags.NoBackground          |
        ImGuiWindowFlags.NoScrollbar           |
        ImGuiWindowFlags.NoScrollWithMouse;

    private static readonly Dictionary<ClickBehavior, string> BehaviorNames = new()
    {
        [ClickBehavior.None]              = LuminaWrapper.GetAddonText(7),
        [ClickBehavior.MinimizeToTaskbar] = Lang.Get("FastMinimizeWindow-MinimizeToTaskbar"),
        [ClickBehavior.MinimizeToTray]    = Lang.Get("FastMinimizeWindow-MinimizeToTray")
    };

    #endregion

    #region Win32

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    #endregion
}
