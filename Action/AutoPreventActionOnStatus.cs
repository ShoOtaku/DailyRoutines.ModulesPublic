using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

// 模块主类
public unsafe class PreventActionOnStatus : DailyModuleBase
{
    // 模块信息，会显示在DR插件的UI中
    public override ModuleInfo Info { get; } = new()
    {
        Title = "状态技能过滤器",
        Description = "当玩家拥有指定的状态且剩余时间小于阈值时，阻止释放非白名单内的任何技能。",
        Category = ModuleCategories.Action,
    };

    // 模块配置
    private static Config ModuleConfig = null!;

    // 用于UI输入的临时变量
    private static int statusIdInput = 0;
    private static int actionIdInput = 0;

    // 初始化模块
    protected override void Init()
    {
        // 加载配置，如果不存在则创建一个新的
        ModuleConfig = LoadConfig<Config>() ?? new Config();
        // 注册 `OnPreUseAction` 事件钩子，这是模块的核心
        UseActionManager.RegPreUseAction(OnPreUseAction);
    }

    // 绘制模块的配置界面
    protected override void ConfigUI()
    {
        // ----- 主开关 -----
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.1f, 1.0f), "启用模块:");
        ImGui.SameLine();
        if (ImGui.Checkbox("##EnableModule", ref ModuleConfig.Enabled))
            SaveConfig(ModuleConfig);

        ImGui.Separator();

        // ----- 时间阈值设置 -----
        ImGui.AlignTextToFramePadding();
        ImGui.Text("当状态剩余时间小于 (秒):");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat("##TimeThreshold", ref ModuleConfig.TimeThreshold, 0.1f, 1.0f, "%.1f"))
        {
            // 确保时间不为负
            ModuleConfig.TimeThreshold = Math.Max(0.0f, ModuleConfig.TimeThreshold);
            SaveConfig(ModuleConfig);
        }
        ImGuiComponents.HelpMarker("只有当检测到的状态剩余时间低于这个值时，才会阻止技能释放。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ----- 状态ID列表管理 -----
        ImGui.Text("需要检测的状态ID列表:");
        // 添加新状态ID的输入框和按钮
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##StatusIdInput", ref statusIdInput, 0);
        ImGui.SameLine();
        if (ImGui.Button("添加状态ID"))
        {
            // 避免添加0或重复的ID
            if (statusIdInput > 0 && !ModuleConfig.StatusIDs.Contains((uint)statusIdInput))
            {
                ModuleConfig.StatusIDs.Add((uint)statusIdInput);
                SaveConfig(ModuleConfig);
            }
            statusIdInput = 0; // 清空输入框
        }
        
        // 显示已添加的状态ID列表
        if (ImGui.BeginChild("StatusList", new Vector2(0, 150), true))
        {
            for (var i = ModuleConfig.StatusIDs.Count - 1; i >= 0; i--)
            {
                var statusId = ModuleConfig.StatusIDs[i];
                var statusText = PresetSheet.Statuses.TryGetValue(statusId, out var statusInfo)
                                     ? $"{statusInfo.Name.ExtractText()} ({statusId})"
                                     : $"未知状态 ({statusId})";

                if (ImGui.Button($"移除##{statusId}-{i}"))
                {
                    ModuleConfig.StatusIDs.RemoveAt(i);
                    SaveConfig(ModuleConfig);
                    continue; // 避免在修改列表后继续操作
                }
                ImGui.SameLine();
                ImGui.Text(statusText);
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // ----- 技能白名单管理 -----
        ImGui.Text("技能白名单 (这些技能不会被阻止):");
        // 添加新技能ID的输入框和按钮
        ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("##ActionIdInput", ref actionIdInput, 0);
        ImGui.SameLine();
        if (ImGui.Button("添加技能ID"))
        {
            if (actionIdInput > 0 && !ModuleConfig.WhitelistedActions.Contains((uint)actionIdInput))
            {
                ModuleConfig.WhitelistedActions.Add((uint)actionIdInput);
                SaveConfig(ModuleConfig);
            }
            actionIdInput = 0; // 清空输入框
        }
        
        // 显示已添加的技能ID列表
        if (ImGui.BeginChild("ActionWhitelist", new Vector2(0, 150), true))
        {
            for (var i = ModuleConfig.WhitelistedActions.Count - 1; i >= 0; i--)
            {
                var actionId = ModuleConfig.WhitelistedActions[i];
                var actionData = LuminaGetter.GetRow<Lumina.Excel.Sheets.Action>(actionId);

                var actionText = actionData?.Name.ExtractText()
                                     ? $"{actionData.Name.ExtractText()} ({actionId})"
                                     : $"未知技能 ({actionId})";
                
                if (ImGui.Button($"移除##{actionId}-{i}"))
                {
                    ModuleConfig.WhitelistedActions.RemoveAt(i);
                    SaveConfig(ModuleConfig);
                    continue;
                }
                ImGui.SameLine();
                ImGui.Text(actionText);
            }
        }
        ImGui.EndChild();
    }

    // 在每次使用技能前都会被调用的核心逻辑
    private static void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteID)
    {
        // 1. 检查模块是否启用，如果未启用，则直接返回
        if (!ModuleConfig.Enabled) return;

        // 2. 仅处理玩家的技能（ActionType.Action）
        if (actionType != ActionType.Action) return;

        // 3. 检查技能是否在白名单中，如果在，则直接返回，不阻止
        var adjustedActionID = ActionManager.Instance()->GetAdjustedActionId(actionID);
        if (ModuleConfig.WhitelistedActions.Contains(adjustedActionID)) return;

        // 4. 获取本地玩家指针
        var player = Control.GetLocalPlayer();
        if (player == null) return;

        // 5. 遍历配置中需要检测的状态ID列表
        foreach (var statusId in ModuleConfig.StatusIDs)
        {
            var statusManager = &player->StatusManager;
            // 查找玩家身上是否有这个状态
            var statusIndex = statusManager->GetStatusIndex(statusId);

            // 如果找到了状态 (statusIndex != -1)
            if (statusIndex != -1)
            {
                // 获取状态的具体信息
                var status = statusManager->Status[statusIndex];
                // 检查状态的剩余时间是否小于我们设置的阈值
                if (status.RemainingTime < ModuleConfig.TimeThreshold)
                {
                    // 条件满足，阻止技能释放
                    isPrevented = true;
                    
                    // (可选) 发送一个通知告诉用户为什么技能被阻止了
                    var actionData = LuminaGetter.GetRow<Lumina.Excel.Sheets.Action>(adjustedActionID);
                    var actionName = actionData?.Name.ExtractText() ?? $"技能 {adjustedActionID}";
                    var statusData = LuminaGetter.GetRow<Lumina.Excel.Sheets.Status>(statusId);
                    var statusName = statusData?.Name.ExtractText() ?? $"状态 {statusId}";
                    
                    if (Throttler.Throttle($"PreventActionOnStatus-Notification", 1_000))
                        NotificationInfo($"因 {statusName} 剩余时间过短，已阻止释放 {actionName}");
                    
                    // 只要找到一个满足条件的状态，就立刻阻止并返回，不再检查其他状态
                    return;
                }
            }
        }
    }

    // 卸载模块时调用，用于清理资源
    protected override void Uninit()
    {
        // 注销事件钩子，非常重要！
        UseActionManager.Unreg(OnPreUseAction);
    }

    // 模块的配置类，用于存储用户设置
    // 模块的配置类，用于存储用户设置
    private class Config : ModuleConfiguration
    {
        // [修正] 将属性改为公共字段
        // 模块总开关
        public bool Enabled = false;
        
        // 需要检测的状态ID列表
        public List<uint> StatusIDs { get; set; } = [];

        // 技能白名单
        public List<uint> WhitelistedActions { get; set; } = [];

        // [修正] 将属性改为公共字段
        // 触发阻止的时间阈值
        public float TimeThreshold = 3.0f; // 默认3秒
    }
}
