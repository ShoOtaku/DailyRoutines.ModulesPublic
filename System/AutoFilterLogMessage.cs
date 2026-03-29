using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using DailyRoutines.Extensions;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools.ImGuiOm.Widgets.Combos;
using OmenTools.OmenService;

namespace DailyRoutines.ModulesPublic;

public class AutoFilterLogMessage : ModuleBase
{
    private static Config          ModuleConfig = null!;
    private static LogMessageCombo Combo        = null!;

    private static readonly HashSet<uint> SeenLogMessages = [];

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoFilterLogMessageTitle"),
        Description = Lang.Get("AutoFilterLogMessageDescription"),
        Category    = ModuleCategory.System
    };

    protected override void Init()
    {
        ModuleConfig = Config.Load(this) ?? new();

        Combo             ??= new("LogMessage");
        Combo.SelectedIDs =   ModuleConfig.FilteredLogMessages;

        LogMessageManager.Instance().RegPre(OnLogMessage);
    }

    protected override void ConfigUI()
    {
        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), Lang.Get("AutoFilterLogMessage-MessageToFilter"));

        using (ImRaii.PushIndent())
        {
            if (Combo.DrawCheckbox())
            {
                ModuleConfig.FilteredLogMessages = Combo.SelectedIDs;
                ModuleConfig.Save(this);
            }
        }

        ImGui.NewLine();

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), Lang.Get("Mode"));

        using (ImRaii.PushIndent())
        {
            foreach (var filterMode in Enum.GetValues<FilterMode>())
            {
                if (ImGui.RadioButton(Lang.Get($"AutoFilterLogMessage-Mode-{filterMode}"), ModuleConfig.Mode == filterMode))
                {
                    ModuleConfig.Mode = filterMode;
                    ModuleConfig.Save(this);
                }
            }
        }
    }

    private static void OnLogMessage(ref bool isPrevented, ref uint logMessageID, ref LogMessageQueueItem item)
    {
        if (!ModuleConfig.FilteredLogMessages.Contains(logMessageID)) return;

        switch (ModuleConfig.Mode)
        {
            case FilterMode.Always:
                isPrevented = true;
                break;

            case FilterMode.PassFirst:
                if (SeenLogMessages.Add(logMessageID)) return;

                isPrevented = true;
                break;
        }
    }

    private class Config : ModuleConfig
    {
        public HashSet<uint> FilteredLogMessages = [];
        public FilterMode    Mode                = FilterMode.PassFirst;
    }

    private enum FilterMode
    {
        Always,
        PassFirst
    }
}
