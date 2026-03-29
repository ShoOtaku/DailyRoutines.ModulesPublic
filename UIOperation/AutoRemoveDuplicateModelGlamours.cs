using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;

namespace DailyRoutines.ModulesPublic;

// TODO: 合并成单一投影台模块
public class AutoRemoveDuplicateModelGlamours : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("AutoRemoveDuplicateModelGlamoursTitle"),
        Description = Lang.Get("AutoRemoveDuplicateModelGlamoursDescription"),
        Category    = ModuleCategory.UIOperation
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init() =>
        TaskHelper ??= new();

    protected override void ConfigUI()
    {
        if (ImGui.Button(Lang.Get("Start")))
            Enqueue();

        ImGui.SameLine();
        if (ImGui.Button(Lang.Get("Stop")))
            TaskHelper.Abort();
    }

    private unsafe void Enqueue()
    {
        var instance = MirageManager.Instance();
        if (instance == null) return;

        List<uint>      itemIndexToRemove = [];
        HashSet<uint>   itemIDHash        = [];
        HashSet<string> itemModelHash     = [];

        for (var i = 0U; i < 800; i++)
        {
            var item = instance->PrismBoxItemIds[(int)i];
            if (item == 0) continue;

            var itemID = item % 100_0000;
            if (!LuminaGetter.TryGetRow(itemID, out Item row) ||
                !row.IsGlamorous                              ||
                row.ModelMain               == 0              ||
                row.EquipSlotCategory.RowId == 0)
                continue;

            if (!itemIDHash.Add(itemID) || !itemModelHash.Add($"{row.ModelMain}_{row.EquipSlotCategory.RowId}"))
                itemIndexToRemove.Add(i);
        }

        if (itemIndexToRemove.Count == 0) return;

        itemIndexToRemove.ForEach(x => TaskHelper.Enqueue(() => instance->RestorePrismBoxItem(x)));
        TaskHelper.Enqueue(Enqueue);
    }
}
