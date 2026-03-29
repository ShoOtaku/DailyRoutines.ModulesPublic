using DailyRoutines.Common.Info.Abstractions;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using MenuItem = Dalamud.Game.Gui.ContextMenu.MenuItem;

namespace DailyRoutines.ModulesPublic;

public unsafe class PetSizeContextMenu : ModuleBase
{
    private static readonly UpperContainerItem ContainerItem = new();

    public override ModuleInfo Info { get; } = new()
    {
        Title       = Lang.Get("PetSizeContextMenuTitle"),
        Description = Lang.Get("PetSizeContextMenuDescription"),
        Category    = ModuleCategory.Combat
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init() =>
        DService.Instance().ContextMenu.OnMenuOpened += OnMenuOpened;

    private static void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!ContainerItem.IsDisplay(args)) return;
        args.AddMenuItem(ContainerItem.Get());
    }

    protected override void Uninit() =>
        DService.Instance().ContextMenu.OnMenuOpened -= OnMenuOpened;

    private class UpperContainerItem : MenuItemBase
    {
        public override string Name       { get; protected set; } = Lang.Get("PetSizeContextMenu-MenuName");
        public override string Identifier { get; protected set; } = nameof(PetSizeContextMenu);

        protected override bool IsSubmenu { get; set; } = true;

        protected override void OnClicked(IMenuItemClickedArgs args)
            => args.OpenSubmenu(Name, ProcessMenuItems());

        private static List<MenuItem> ProcessMenuItems() =>
        [
            new()
            {
                Name      = $"{Lang.Get("Adjust")}: {LuminaWrapper.GetAddonText(6371)}",
                OnClicked = _ => ChatManager.Instance().SendMessage("/petsize all large")
            },
            new()
            {
                Name      = $"{Lang.Get("Adjust")}: {LuminaWrapper.GetAddonText(6372)}",
                OnClicked = _ => ChatManager.Instance().SendMessage("/petsize all medium")
            },
            new()
            {
                Name      = $"{Lang.Get("Adjust")}: {LuminaWrapper.GetAddonText(6373)}",
                OnClicked = _ => ChatManager.Instance().SendMessage("/petsize all small")
            }
        ];

        public override bool IsDisplay(IMenuOpenedArgs args)
        {
            if (args.Target is not MenuTargetDefault defautTarget) return false;
            if (DService.Instance().ObjectTable.LocalPlayer is not { } localPlayer) return false;

            var pet = CharacterManager.Instance()->LookupPetByOwnerObject(localPlayer.ToStruct());
            if (pet == null || defautTarget.TargetObjectId != pet->GetGameObjectId()) return false;

            return true;
        }
    }
}
