using System.Numerics;
using DailyRoutines.Abstracts;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoDeleteLetters : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoDeleteLettersTitle"),
        Description = GetLoc("AutoDeleteLettersDescription"),
        Category    = ModuleCategories.UIOperation,
    };

    protected override void Init()
    {
        TaskHelper ??= new();
        Overlay    ??= new(this);

        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "SelectYesno", AlwaysYes);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "LetterList",  OnAddonLetterList);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LetterList",  OnAddonLetterList);

        if (LetterList != null) 
            OnAddonLetterList(AddonEvent.PostSetup, null);
    }

    protected override void OverlayUI()
    {
        var addon = LetterList;
        if (addon == null) return;

        var pos = new Vector2(addon->GetX() - ImGui.GetWindowSize().X, addon->GetY() + 6);
        ImGui.SetWindowPos(pos);

        ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("AutoDeleteLettersTitle"));
        
        using (ImRaii.Disabled(TaskHelper.IsBusy))
        {
            if (ImGui.Button(GetLoc("Start"))) 
                TaskHelper.Enqueue(RightClickLetter);
        }
        
        ImGui.SameLine();
        if (ImGui.Button(GetLoc("Stop"))) 
            TaskHelper.Abort();
    }

    public static void RightClickLetter()
    {
        var addon = LetterList;
        if (!IsAddonAndNodesReady(addon)) return ;

        var infoProxy = InfoProxyLetter.Instance();
        for (var index = 0; index < infoProxy->Letters.Length; index++)
        {
            SendEvent(AgentId.LetterList, 0, 0, index, 0, 1); // 第二个 0 是索引
            SendEvent(AgentId.LetterList, 4, 0);
        }
    }

    private void AlwaysYes(AddonEvent type, AddonArgs args)
    {
        if (!TaskHelper.IsBusy) return;
        ClickSelectYesnoYes();
    }

    private void OnAddonLetterList(AddonEvent type, AddonArgs? _) =>
        Overlay.IsOpen = type switch
        {
            AddonEvent.PostSetup   => true,
            AddonEvent.PreFinalize => false,
            _                      => Overlay.IsOpen,
        };

    protected override void Uninit()
    {
        DService.AddonLifecycle.UnregisterListener(OnAddonLetterList);
        DService.AddonLifecycle.UnregisterListener(AlwaysYes);
    }
}
