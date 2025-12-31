using DailyRoutines.Abstracts;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoDisplayDutyReadyLeftTime : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoDisplayDutyReadyLeftTimeTitle"),
        Description = GetLoc("AutoDisplayDutyReadyLeftTimeDescription"),
        Category    = ModuleCategories.Combat,
        PreviewImageURL = ["https://gh.atmoomen.top/raw.githubusercontent.com/AtmoOmen/StaticAssets/main/DailyRoutines/image/AutoDisplayDutyReadyLeftTime-UI.png"]
    };
    
    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };

    private static CountdownTimer? Timer;

    protected override void Init() =>
        DService.Condition.ConditionChange += OnConditionChanged;

    private static void OnConditionChanged(ConditionFlag flag, bool value)
    {
        if (flag != ConditionFlag.WaitingForDuty) return;
        
        Timer?.Stop();
        Timer?.Dispose();
        Timer = null;

        if (value)
        {
            OnCountdownRunning(null, 45);
            
            Timer = new(45);
            Timer.Start();
            Timer.TimeChanged += OnCountdownRunning;
        }
    }

    private static void OnCountdownRunning(object? sender, int second)
    {
        if (!IsAddonAndNodesReady(ContentsFinderReady)) return;
        
        var textNode = ContentsFinderReady->GetTextNodeById(3);
        if (textNode == null) return;

        var builder = new SeStringBuilder();
        builder.AddText($"{LuminaWrapper.GetAddonText(2780)} ")
               .AddUiForeground(32)
               .AddText($"[{DService.SeStringEvaluator.EvaluateFromAddon(9169, [second])}]")
               .AddUiForegroundOff();
        
        textNode->SetText(builder.Build().EncodeWithNullTerminator());
    }

    protected override void Uninit()
    {
        DService.Condition.ConditionChange -= OnConditionChanged;
        OnConditionChanged(ConditionFlag.WaitingForDuty, false);
    }
}
