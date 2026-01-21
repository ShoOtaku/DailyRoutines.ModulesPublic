using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace DailyRoutines.ModulesPublic;

public class AutoOpenNeedGreed : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title           = GetLoc("AutoOpenNeedGreedTitle"),
        Description     = GetLoc("AutoOpenNeedGreedDescription"),
        Category        = ModuleCategories.UIOperation,
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    protected override void Init() =>
        LogMessageManager.Instance().RegPost(OnPost);
    
    protected override void Uninit() =>
        LogMessageManager.Instance().Unreg(OnPost);

    private static unsafe void OnPost(uint logMessageID, LogMessageQueueItem item)
    {
        if (logMessageID != 5194) return;

        NeedGreed->Callback(0, 0U);
    }
}
