using System.Collections.Generic;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Infos;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Action = Lumina.Excel.Sheets.Action;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoActionAlignCamera : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = GetLoc("AutoActionAlignCameraTitle"),
        Description = GetLoc("AutoActionAlignCameraDescription"),
        Category    = ModuleCategories.Action,
    };

    public override ModulePermission Permission { get; } = new() { NeedAuth = true };

    private static Config ModuleConfig = null!;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new() { ActionReversed = [94, 29494, 24402] };
        
        UseActionManager.RegPreUseActionLocation(OnPreUseAction);
    }

    protected override void ConfigUI()
    {
        using var table = ImRaii.Table("ActionEnabled", 3, ImGuiTableFlags.BordersInnerH, (ImGui.GetContentRegionAvail() / 1.75f).WithY(0));
        if (!table) return;

        ImGui.TableSetupColumn("选框", ImGuiTableColumnFlags.WidthFixed, ImGui.GetTextLineHeightWithSpacing());
        ImGui.TableSetupColumn("技能", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("反转", ImGuiTableColumnFlags.WidthFixed, ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.X);

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableNextColumn();

        ImGui.TableNextColumn();
        ImGuiOm.Text(GetLoc("Action"));

        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (4f * GlobalFontScale));
        ImGuiOm.Text(FontAwesomeIcon.Undo.ToIconString());
        ImGuiOm.TooltipHover(GetLoc("AutoActionAlignCamera-ReverseDirection"));

        foreach (var actionPair in ModuleConfig.ActionEnabled)
        {
            if (!LuminaGetter.TryGetRow<Action>(actionPair.Key, out var data)) continue;
            
            var actionIcon = DService.Texture.GetFromGameIcon(new(data.Icon)).GetWrapOrDefault();
            if (actionIcon == null) continue;

            using var id = ImRaii.PushId($"{actionPair.Key}");
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isEnabled = actionPair.Value;
            if (ImGui.Checkbox($"###{actionPair.Key}", ref isEnabled))
            {
                ModuleConfig.ActionEnabled[actionPair.Key] = isEnabled;
                ModuleConfig.Save(this);
            }
            
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (4f * GlobalFontScale));
            ImGui.Image(actionIcon.Handle, new(ImGui.GetTextLineHeight()));
            
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (2f * GlobalFontScale));
            ImGui.Text($"{data.Name.ExtractText()}");

            ImGui.TableNextColumn();
            var isReversed = ModuleConfig.ActionReversed.Contains(actionPair.Key);
            if (ImGui.Checkbox($"###{actionPair.Key}-IsReversed", ref isReversed))
            {
                if (!ModuleConfig.ActionReversed.Remove(actionPair.Key))
                    ModuleConfig.ActionReversed.Add(actionPair.Key);
                ModuleConfig.Save(this);
            }
        }
    }

    private static void OnPreUseAction(
        ref bool       isPrevented,
        ref ActionType type,
        ref uint       actionID,
        ref ulong      targetID,
        ref Vector3    location,
        ref uint       extraParam,
        ref byte       a7)
    {
        if (type != ActionType.Action) return;

        var adjustedID = ActionManager.Instance()->GetAdjustedActionId(actionID);
        if (!ModuleConfig.ActionEnabled.TryGetValue(adjustedID, out var enabled) || !enabled) return;

        if (DService.ObjectTable.LocalPlayer is not { } localPlayer) return;

        var transformedRotation = CameraDirHToCharaRotation(((CameraEx*)CameraManager.Instance()->Camera)->DirH);
        if (ModuleConfig.ActionReversed.Contains(adjustedID))
            transformedRotation = CharaRotationSymmetricTransform(transformedRotation);

        if (BoundByDuty)
            PositionUpdateInstancePacket.Send(transformedRotation, localPlayer.Position);
        else
            PositionUpdatePacket.Send(transformedRotation, localPlayer.Position);
        
        localPlayer.ToStruct()->SetRotation(transformedRotation);
    }

    private class Config : ModuleConfiguration
    {
        public HashSet<uint> ActionReversed = [];

        public Dictionary<uint, bool> ActionEnabled = new()
        {
            // 回避跳跃
            [94] = true,
            // 回避跳跃 (PVP)
            [29494] = true,
            // 地狱入境
            [24401] = true,
            // 地狱入境 (PVP)
            [29550] = true,
            // 地狱出境
            [24402] = true,
            // 速涂
            [34684] = true,
            // 速涂 (PVP)
            [39210] = true,
            // 前冲步
            [16010] = true,
            // 前冲步 (PVP)
            [29430] = true,
            // 火焰喷射器
            [7418] = true,
            // 以太变移
            [37008] = true,
            // 武装戍卫
            [7385] = true,
            // 本轮 (PVP)
            [41506] = true,

            // 水流吐息
            [11390] = true,
            // 5级石化
            [11414] = true,
            // 拍掌
            [11403] = true,
            // 鼻息
            [11383] = true,
            // 诡异视线
            [11399] = true,
            // 臭气
            [11388] = true,
            // 喷墨
            [11422] = true,
            // 山崩
            [11428] = true,
            // 冰雪乱舞
            [11430] = true,
            // 万变水波
            [18296] = true,
            // 狂风暴雪
            [18297] = true,
            // 寒光
            [18299] = true,
            // 穿甲散弹
            [18323] = true,
            // 水脉诅咒
            [23283] = true,
            // 鬼宿脚
            [23288] = true,
            [23289] = true,
            // 魔法吐息
            [34567] = true,
            // 红宝石电圈
            [34571] = true,
            // 魔之符文
            [34572] = true,
            // 启示录
            [34581] = true,
        };
    }
}
