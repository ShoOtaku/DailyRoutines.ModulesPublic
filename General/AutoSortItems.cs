using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public class AutoSortItems : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoSortItemsTitle"),
        Description = GetLoc("AutoSortItemsDescription"),
        Category    = ModuleCategories.General,
        Author      = ["那年雪落"]
    };

    private static readonly string[] SortOptions        = [GetLoc("Descending"), GetLoc("Ascending")];
    private static readonly string[] TabOptions         = [GetLoc("AutoSortItems-Splited"), GetLoc("AutoSortItems-Merged")];
    private static readonly string[] SortOptionsCommand = ["des", "asc"];

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig =   LoadConfig<Config>() ?? new();
        TaskHelper   ??= new() { TimeoutMS = 15_000 };

        DService.Instance().ClientState.TerritoryChanged += OnZoneChanged;
        OnZoneChanged(0);
    }

    protected override void ConfigUI()
    {
        if (ImGui.Button(LuminaWrapper.GetAddonText(1389)))
            TaskHelper.Enqueue(CheckCanSort);

        ImGui.NewLine();

        if (ImGui.Checkbox(GetLoc("SendChat"), ref ModuleConfig.SendChat))
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        if (ImGui.Checkbox(GetLoc("SendNotification"), ref ModuleConfig.SendNotification))
            SaveConfig(ModuleConfig);

        ImGui.Spacing();

        var       tableSize = (ImGui.GetContentRegionAvail() * 0.75f) with { Y = 0 };
        using var table     = ImRaii.Table(GetLoc("Sort"), 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, tableSize);
        if (!table) return;

        ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.WidthStretch, 30);
        ImGui.TableSetupColumn("方法", ImGuiTableColumnFlags.WidthStretch, 30);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(LuminaWrapper.GetAddonText(12210));

        var typeText = LuminaGetter.GetRow<Addon>(9448)!.Value.Text.ToString();

        DrawTableRow("兵装库 ID", "ID",            ref ModuleConfig.ArmouryChestID,   SortOptions);
        DrawTableRow("兵装库等级",  GetLoc("Level"), ref ModuleConfig.ArmouryItemLevel, SortOptions);
        DrawTableRow("兵装库类型",  typeText,        ref ModuleConfig.ArmouryCategory,  SortOptions, GetLoc("AutoSortItems-ArmouryCategoryDesc"));

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(LuminaWrapper.GetAddonText(12209));

        DrawTableRow("背包 HQ", "HQ",                            ref ModuleConfig.InventoryHQ,        SortOptions);
        DrawTableRow("背包 ID", "ID",                            ref ModuleConfig.InventoryID,        SortOptions);
        DrawTableRow("背包等级",  GetLoc("Level"),                 ref ModuleConfig.InventoryItemLevel, SortOptions);
        DrawTableRow("背包类型",  typeText,                        ref ModuleConfig.InventoryCategory,  SortOptions, GetLoc("AutoSortItems-InventoryCategoryDesc"));
        DrawTableRow("背包分栏",  GetLoc("AutoSortItems-Splited"), ref ModuleConfig.InventoryTab,       TabOptions,  GetLoc("AutoSortItems-InventoryTabDesc"));
    }

    protected override void Uninit() =>
        DService.Instance().ClientState.TerritoryChanged -= OnZoneChanged;

    private void DrawTableRow(string id, string label, ref int value, string[] options, string note = "")
    {
        using var idPush = ImRaii.PushId($"{label}_{id}");

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);

        if (!string.IsNullOrWhiteSpace(note))
            ImGuiOm.HelpMarker(note);

        var oldValue = value;
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo($"##{label}", ref value, options, options.Length) && value != oldValue)
            SaveConfig(ModuleConfig);
    }

    private void OnZoneChanged(ushort zone)
    {
        TaskHelper.Abort();

        if (GameState.TerritoryType == 0) return;
        TaskHelper.Enqueue(CheckCanSort);
    }

    private bool CheckCanSort()
    {
        if (!GameState.IsLoggedIn || !UIModule.IsScreenReady() || OccupiedInEvent) return false;

        if (!DService.Instance().ClientState.IsClientIdle() || !IsInValidZone())
        {
            TaskHelper.Abort();
            return true;
        }

        TaskHelper.Enqueue(SendSortCommand, "SendSortCommand");
        return true;
    }

    private static bool IsInValidZone() =>
        GameState.Map           != 0 &&
        GameState.TerritoryType != 0 &&
        !GameState.IsInPVPArea       &&
        GameState.ContentFinderCondition == 0;

    private static bool SendSortCommand()
    {
        SendSortCondition("armourychest", "id",        ModuleConfig.ArmouryChestID);
        SendSortCondition("armourychest", "itemlevel", ModuleConfig.ArmouryItemLevel);
        SendSortCondition("armourychest", "category",  ModuleConfig.ArmouryCategory);
        ChatManager.Instance().SendMessage("/itemsort execute armourychest");

        SendSortCondition("inventory", "hq",        ModuleConfig.InventoryHQ);
        SendSortCondition("inventory", "id",        ModuleConfig.InventoryID);
        SendSortCondition("inventory", "itemlevel", ModuleConfig.InventoryItemLevel);
        SendSortCondition("inventory", "category",  ModuleConfig.InventoryCategory);

        if (ModuleConfig.InventoryTab == 0)
            ChatManager.Instance().SendMessage("/itemsort condition inventory tab");

        ChatManager.Instance().SendMessage("/itemsort execute inventory");

        if (ModuleConfig.SendNotification)
            NotificationInfo(GetLoc("AutoSortItems-SortMessage"));
        if (ModuleConfig.SendChat)
            Chat(GetLoc("AutoSortItems-SortMessage"));

        return true;

        void SendSortCondition(string target, string condition, int setting) =>
            ChatManager.Instance().SendMessage($"/itemsort condition {target} {condition} {SortOptionsCommand[setting]}");
    }

    public class Config : ModuleConfiguration
    {
        public int ArmouryChestID;
        public int ArmouryItemLevel;
        public int ArmouryCategory;
        public int InventoryHQ;
        public int InventoryID;
        public int InventoryItemLevel;
        public int InventoryCategory;
        public int InventoryTab;

        public bool SendChat;
        public bool SendNotification = true;
    }
}
