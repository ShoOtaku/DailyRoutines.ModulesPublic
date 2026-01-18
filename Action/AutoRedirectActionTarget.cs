using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using Action = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoRedirectActionTarget : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoRedirectActionTargetTitle"),
        Description = GetLoc("AutoRedirectActionTargetDescription"),
        Category    = ModuleCategories.Action
    };

    private static Config ModuleConfig = null!;
    
    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        UseActionManager.Instance().RegPreUseActionLocation(OnPreUseAction);
    }

    protected override void Uninit() =>
        UseActionManager.Instance().Unreg(OnPreUseAction);

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox(GetLoc("AutoRedirectActionTarget-RedirectEnemyAction"), ref ModuleConfig.TargetEnemyAction))
            SaveConfig(ModuleConfig);
        
        if (ImGui.Checkbox(GetLoc("AutoRedirectActionTarget-RedirectMemberAction"), ref ModuleConfig.TargetMemberAction))
            SaveConfig(ModuleConfig);
    }

    private static void OnPreUseAction
    (
        ref bool       isPrevented,
        ref ActionType type,
        ref uint       actionID,
        ref ulong      targetID,
        ref Vector3    location,
        ref uint       extraParam,
        ref byte       a7
    )
    {
        if (type != ActionType.Action) return;
        if (!LuminaGetter.TryGetRow(actionID, out Action actionRow)) return;
        
        if (actionRow.TargetArea) return;
        
        switch (actionRow.CanTargetHostile)
        {
            case true when !ModuleConfig.TargetEnemyAction:
            case false when !ModuleConfig.TargetMemberAction:
                return;
        }
        
        var gameObject = targetID == 0xE0000000 ? null : CharacterManager.Instance()->LookupBattleCharaByEntityId((uint)targetID);
        if (gameObject == null || !ActionManager.CanUseActionOnTarget(actionID, (GameObject*)gameObject))
        {
            var targetToSelect = GetAvailableTarget(actionID, actionRow.CanTargetHostile);
            if (targetToSelect != null)
            {
                targetID = targetToSelect->EntityId;
                if (TargetSystem.Instance()->Target == null)
                    TargetSystem.Instance()->SetHardTarget((GameObject*)targetToSelect);
            }
        }
    }

    private static BattleChara* GetAvailableTarget(uint actionID, bool isTargetEnemy)
    {
        var localPosition = LocalPlayerState.Object.Position;
        var actionRange = MathF.Pow(ActionManager.GetActionRange(actionID), 2);

        var previousTarget = TargetSystem.Instance()->PreviousTarget;
        if (previousTarget != null                                       &&
            ActionManager.CanUseActionOnTarget(actionID, previousTarget) &&
            Vector3.DistanceSquared(localPosition, previousTarget->Position) <= actionRange)
            return (BattleChara*)previousTarget;

        if (isTargetEnemy)
        {
            var array = EnemyListNumberArray.Instance();
            if (array->EnemyCount == 0) return null;

            for (var i = 0; i < array->EnemyCount; i++)
            {
                var enemyData = array->Enemies[i];
                if ((uint)enemyData.EntityId is 0 or 0xE0000000 || !enemyData.ActiveInList || enemyData.RemainingHPPercent == 0) continue;

                var obj = CharacterManager.Instance()->LookupBattleCharaByEntityId((uint)enemyData.EntityId);
                if (obj == null) continue;
                
                if (ActionManager.CanUseActionOnTarget(actionID, (GameObject*)obj) &&
                    Vector3.DistanceSquared(localPosition, obj->Position) <= actionRange)
                    return obj;
            }
        }
        else
        {
            var agent = AgentHUD.Instance();
            if (agent->PartyMemberCount > 1)
            {
                foreach (var partyMember in agent->PartyMembers)
                {
                    if (partyMember.ContentId == 0 || partyMember.Object == null) continue;
                    if (ActionManager.CanUseActionOnTarget(actionID, (GameObject*)partyMember.Object) &&
                        Vector3.DistanceSquared(localPosition, partyMember.Object->Position) <= actionRange)
                        return partyMember.Object;
                }
            }
            
            for (var i = 0; i < 200; i++)
            {
                var obj = CharacterManager.Instance()->BattleCharas[i].Value;
                if (obj == null) continue;
                
                if (ActionManager.CanUseActionOnTarget(actionID, (GameObject*)obj) &&
                    Vector3.DistanceSquared(localPosition, obj->Position) <= actionRange)
                    return obj;
            }
        }
        
        return null;
    }

    private class Config : ModuleConfiguration
    {
        public bool TargetEnemyAction = true;
        public bool TargetMemberAction;
    }
}
