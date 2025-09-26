using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using Status = Lumina.Excel.Sheets.Status;

namespace DailyRoutines.ModulesPublic;

public unsafe class CustomStatusPrevent : DailyModuleBase
{

    public override ModuleInfo Info { get; } = new()
    {
        Title = "CustomStatusPrevent",
        Description = "当自身或目标存在指定的状态时，自动阻止释放技能。",
        Category = ModuleCategories.Action
    };

    // 模块的核心状态应声明为 static
    private static Config ModuleConfig = null!;

    private static int newStatusID;
    private static readonly string[] DetectTypeNames = ["自身", "目标"];
    
    /// <summary>
    /// 定义检测对象的类型
    /// </summary>
    public enum DetectType
    {
        Self,
        Target
    }

    /// <summary>
    /// 用于存储单条自定义状态规则的数据结构
    /// </summary>
    public class CustomStatusEntry
    {
        public bool IsEnabled { get; set; } = true;
        public DetectType Target { get; set; } = DetectType.Target;
        public uint StatusID { get; set; }
        public float Threshold { get; set; } = 3.5f;
    }

    /// <summary>
    /// 模块的配置类，继承自 ModuleConfiguration
    /// </summary>
    private class Config : ModuleConfiguration
    {
        // 用于存储所有自定义规则的列表
        public List<CustomStatusEntry> StatusEntries { get; set; } = [];
    }

    /// <summary>
    /// 模块初始化方法。在模块加载时执行。
    /// </summary>
    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        
        UseActionManager.RegPreUseAction(OnPreUseAction);
    }

    /// <summary>
    /// 模块卸载方法。在模块卸载时执行。
    /// </summary>
    protected override void Uninit()
    {
        UseActionManager.Unreg(OnPreUseAction);
    }

    /// <summary>
    /// 核心逻辑：在每次技能使用前被调用
    /// </summary>
    private static void OnPreUseAction(
        ref bool isPrevented,
        ref ActionType actionType,
        ref uint actionID,
        ref ulong targetID,
        ref uint extraParam,
        ref ActionManager.UseActionMode queueState,
        ref uint comboRouteID)
    {
        // "早返回" 策略：过滤掉非玩家技能的事件
        if (actionType != ActionType.Action) return;
        if (ModuleConfig.StatusEntries.Count == 0) return;

        foreach (var entry in ModuleConfig.StatusEntries)
        {
            if (!entry.IsEnabled) continue;

            BattleChara* actor = null;
            switch (entry.Target)
            {
                case DetectType.Self:
                    actor = Control.GetLocalPlayer();
                    break;
                case DetectType.Target:
                    var targetObj = DService.Targets.Target;
                    // 确保目标是有效的战斗角色
                    if (targetObj is not IBattleChara chara) continue;
                    actor = chara.ToStruct();
                    break;
            }

            if (actor == null) continue;

            if (HasStatus(&actor->StatusManager, entry.StatusID, entry.Threshold))
            {
                isPrevented = true;
                // 找到一个匹配的阻止规则，立即返回
                return;
            }
        }
    }
    
    /// <summary>
    /// 检查指定的状态管理器中是否存在某个状态，并且剩余时间大于阈值
    /// </summary>
    /// <param name="statusManager">目标的状态管理器指针</param>
    /// <param name="statusID">要检查的状态ID</param>
    /// <param name="threshold">时间阈值</param>
    /// <returns>如果状态存在且时间足够，则返回 true</returns>
    private static bool HasStatus(StatusManager* statusManager, uint statusID, float threshold)
    {
        if (statusManager == null) return false;

        var statusIndex = statusManager->GetStatusIndex(statusID);
        if (statusIndex == -1) return false;

        // 规范要求：在 OnPreUseAction 这种高频事件中，核心逻辑应被包裹
        try
        {
            return statusManager->Status[statusIndex].RemainingTime < threshold;
        }
        catch
        {
            // 防止意外的指针错误导致游戏崩溃
            return false;
        }
    }
    
    /// <summary>
    /// 绘制模块的配置界面
    /// </summary>
    protected override void ConfigUI()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("状态 ID:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputInt("##NewStatusId", ref newStatusID);
        
        ImGui.SameLine();
        if (ImGui.Button("添加新规则"))
        {
            if (newStatusID > 0 && GetStatusName((uint)newStatusID) != null)
            {
                ModuleConfig.StatusEntries.Add(new CustomStatusEntry { StatusID = (uint)newStatusID });
                SaveConfig(ModuleConfig);
                newStatusID = 0; // Reset input field
            }
        }
        ImGuiOm.HelpMarker("输入一个有效的状态ID后点击添加。");
        
        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        using var table = ImRaii.Table("###CustomStatusTable", 7, tableFlags);
        if (!table) return;

        // Setup columns
        ImGui.TableSetupColumn("启用", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2);
        ImGui.TableSetupColumn("对象", ImGuiTableColumnFlags.WidthFixed, 100f * GlobalFontScale);
        ImGui.TableSetupColumn("图标", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2);
        ImGui.TableSetupColumn("状态 ID", ImGuiTableColumnFlags.WidthFixed, 100f * GlobalFontScale);
        ImGui.TableSetupColumn("状态名称", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("剩余时间少于(秒)", ImGuiTableColumnFlags.WidthFixed, 120f * GlobalFontScale);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2);
        
        ImGui.TableHeadersRow();

        int? indexToRemove = null;
        for (var i = 0; i < ModuleConfig.StatusEntries.Count; i++)
        {
            var entry = ModuleConfig.StatusEntries[i];
            ImGui.PushID($"CustomStatusEntry_{i}");

            ImGui.TableNextRow();

            // Column 1: Enabled Checkbox
            ImGui.TableNextColumn();
            var isEnabled = entry.IsEnabled;
            if (ImGui.Checkbox("##IsEnabled", ref isEnabled))
            {
                entry.IsEnabled = isEnabled;
                SaveConfig(ModuleConfig);
            }

            // Column 2: Target ComboBox
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var currentTarget = (int)entry.Target;
            if (ImGui.Combo("##Target", ref currentTarget, DetectTypeNames, DetectTypeNames.Length))
            {
                entry.Target = (DetectType)currentTarget;
                SaveConfig(ModuleConfig);
            }

            // Column 3: Status Icon
            ImGui.TableNextColumn();
            var icon = GetStatusIcon(entry.StatusID);
            if (icon != null)
                ImGui.Image(icon.Handle, new Vector2(ImGui.GetFrameHeight()));

            // Column 4: Status ID
            ImGui.TableNextColumn();
            ImGui.Text(entry.StatusID.ToString());

            // Column 5: Status Name
            ImGui.TableNextColumn();
            ImGui.Text(GetStatusName(entry.StatusID) ?? "无效或未找到");

            // Column 6: Threshold Input
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var threshold = entry.Threshold;
            ImGui.InputFloat("##Threshold", ref threshold, 0.1f, 0.5f, "%.1f");

            // 当用户完成编辑后（例如按回车或点击其他地方）
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                // 确保阈值不为负数
                if (threshold < 0) 
                    threshold = 0;

                // 只有当值确实发生改变时，才更新并保存配置
                if (entry.Threshold != threshold)
                {
                    entry.Threshold = threshold;
                    SaveConfig(ModuleConfig);
                }
            }

            // Column 7: Remove Button
            ImGui.TableNextColumn();
            if (ImGui.Button("移除"))
                indexToRemove = i;

            ImGui.PopID();
        }

        if (indexToRemove.HasValue)
        {
            ModuleConfig.StatusEntries.RemoveAt(indexToRemove.Value);
            SaveConfig(ModuleConfig);
        }
    }
    
    private static string? GetStatusName(uint statusID)
    {
        return LuminaGetter.TryGetRow<Status>(statusID, out var status) ? status.Name.ExtractText() : null;
    }

    private static IDalamudTextureWrap? GetStatusIcon(uint statusID)
    {
        if (!LuminaGetter.TryGetRow<Status>(statusID, out var status)) return null;
        return DService.Texture.GetFromGameIcon(new(status.Icon)).GetWrapOrDefault();
    }
}
