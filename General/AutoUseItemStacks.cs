using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Dalamud.Game.ClientState.Conditions;

namespace DailyRoutines.Modules;

public unsafe class AutoUseItemStacks : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoUseItemStacksTitle"),
        Description = GetLoc("AutoUseItemStacksDescription"),
        Category    = ModuleCategories.General,
        Author      = ["Cindy-Master"],
    };

    protected override void Init()
    {
        TaskHelper ??= new() { TimeLimitMS = 5_000 };
        DService.ContextMenu.OnMenuOpened += OnContextMenuOpened;
    }

    protected override void ConfigUI() => 
        ConflictKeyText();

    protected override void Uninit() => 
        DService.ContextMenu.OnMenuOpened -= OnContextMenuOpened;

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetInventory targetInventory) return;
        if (OccupiedInEvent) return;

        var itemID = targetInventory.TargetItem?.ItemId ?? 0;
        if (itemID == 0) return;

        if (IsCofferItem(itemID))
            args.AddMenuItem(new OpenAllCoffersMenuItem(this, itemID).Get());
    }

    public void EnqueueOpenAllCoffers(uint itemID)
    {
        if (InterruptByConflictKey(TaskHelper, this)) return;
        if (!TryGetFirstInventoryItem(PlayerInventories, x => x.ItemId == itemID, out _)) return;

        TaskHelper.Enqueue(() => AgentInventoryContext.Instance()->UseItem(itemID));
        TaskHelper.DelayNext(500);
        TaskHelper.Enqueue(() => !DService.Condition[ConditionFlag.Casting]);
        TaskHelper.DelayNext(500);
        TaskHelper.Enqueue(() => EnqueueOpenAllCoffers(itemID));
    }

    private static bool IsCofferItem(uint itemID) => 
        LuminaGetter.GetRow<Item>(itemID) is { StackSize: > 1, ItemAction.RowId: 367 or 388 or 2462 };

    private class OpenAllCoffersMenuItem(AutoUseItemStacks Module, uint ItemID) : MenuItemBase
    {
        public override string Name       { get; protected set; } = GetLoc("AutoUseItemStacks-MenuItem");
        public override string Identifier { get; protected set; } = nameof(AutoUseItemStacks);

        protected override bool WithDRPrefix { get; set; } = true;

        protected override void OnClicked(IMenuItemClickedArgs args) => 
            Module.EnqueueOpenAllCoffers(ItemID);
    }
}
