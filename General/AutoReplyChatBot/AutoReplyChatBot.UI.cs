using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.Text;

namespace DailyRoutines.ModulesPublic;

public partial class AutoReplyChatBot
{
    protected override void ConfigUI()
    {
        var fieldW  = 230f                            * GlobalFontScale;
        var promptH = 200f                            * GlobalFontScale;
        var promptW = ImGui.GetContentRegionAvail().X * 0.9f;

        using var tab = ImRaii.TabBar("###Config", ImGuiTabBarFlags.Reorderable);
        if (!tab) return;

        using (var generalTab = ImRaii.TabItem(GetLoc("General")))
        {
            if (generalTab)
            {
                ImGui.SetNextItemWidth(fieldW);

                using (var combo = ImRaii.Combo
                       (
                           $"{GetLoc("AutoReplyChatBot-ValidChatTypes")}",
                           string.Join(',', ModuleConfig.ValidChatTypes.Select(x => ValidChatTypes.GetValueOrDefault(x, string.Empty))),
                           ImGuiComboFlags.HeightLarge
                       ))
                {
                    if (combo)
                    {
                        foreach (var (chatType, loc) in ValidChatTypes)
                        {
                            if (ImGui.Selectable($"{loc}##{chatType}", ModuleConfig.ValidChatTypes.Contains(chatType), ImGuiSelectableFlags.DontClosePopups))
                            {
                                if (!ModuleConfig.ValidChatTypes.Remove(chatType))
                                    ModuleConfig.ValidChatTypes.Add(chatType);
                                ModuleConfig.Save(this);
                            }
                        }
                    }
                }

                if (ModuleConfig.ValidChatTypes.Contains(XivChatType.TellIncoming) &&
                    ImGui.Checkbox(GetLoc("AutoReplyChatBot-OnlyReplyNonFriendTell"), ref ModuleConfig.OnlyReplyNonFriendTell))
                    SaveConfig(ModuleConfig);

                ImGui.NewLine();

                // 冷却秒
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.SliderInt(GetLoc("AutoReplyChatBot-CooldownSeconds"), ref ModuleConfig.CooldownSeconds, 0, 120))
                    SaveConfig(ModuleConfig);

                ImGui.NewLine();

                // API调用时的历史记录数量限制
                if (ImGui.Checkbox(GetLoc("AutoReplyChatBot-EnableContextLimit"), ref ModuleConfig.EnableContextLimit))
                    SaveConfig(ModuleConfig);
                ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-EnableContextLimit-Help"));

                using (ImRaii.Disabled(!ModuleConfig.EnableContextLimit))
                {
                    ImGui.SetNextItemWidth(fieldW);
                    if (ImGui.SliderInt(GetLoc("AutoReplyChatBot-MaxContextMessages"), ref ModuleConfig.MaxContextMessages, 1, 50))
                        SaveConfig(ModuleConfig);
                }

                ImGui.NewLine();

                // 历史记录存储上限（仅作提示，不删除记录）
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.SliderInt(GetLoc("AutoReplyChatBot-MaxHistory"), ref ModuleConfig.MaxHistory, 1, 50))
                    SaveConfig(ModuleConfig);
                ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-MaxHistory-Help"));

                ImGui.NewLine();

                // 最大令牌数
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.SliderInt(GetLoc("AutoReplyChatBot-MaxTokens"), ref ModuleConfig.MaxTokens, 256, 8192))
                    SaveConfig(ModuleConfig);
                ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-MaxTokens-Help"));

                ImGui.NewLine();

                // 温度参数
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.SliderFloat(GetLoc("AutoReplyChatBot-Temperature"), ref ModuleConfig.Temperature, 0.0f, 2.0f))
                    SaveConfig(ModuleConfig);
                ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-Temperature-Help"));
            }
        }

        using (var apiTab = ImRaii.TabItem("API"))
        {
            if (apiTab)
            {
                // API Select
                ImGui.TextColored(KnownColor.LightSkyBlue.ToVector4(), GetLoc("Type"));

                using (ImRaii.PushIndent())
                {
                    var currentProvider = ModuleConfig.Provider;
                    if (ImGui.RadioButton("OpenAI", currentProvider == APIProvider.OpenAI))
                        ModuleConfig.Provider = APIProvider.OpenAI;

                    ImGui.SameLine();
                    if (ImGui.RadioButton("Ollama", currentProvider == APIProvider.Ollama))
                        ModuleConfig.Provider = APIProvider.Ollama;
                    SaveConfig(ModuleConfig);
                }

                ImGui.NewLine();

                // API Key
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.InputText("API Key", ref ModuleConfig.APIKey, 256))
                    SaveConfig(ModuleConfig);
                ImGuiOm.TooltipHover(ModuleConfig.APIKey);

                // Base Url
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.InputText("Base URL", ref ModuleConfig.BaseURL, 256))
                    SaveConfig(ModuleConfig);

                // Model
                ImGui.SetNextItemWidth(fieldW);
                if (ImGui.InputText(GetLoc("AutoReplyChatBot-Model"), ref ModuleConfig.Model, 128))
                    SaveConfig(ModuleConfig);
            }
        }

        using (var filterTab = ImRaii.TabItem(GetLoc("AutoReplyChatBot-FilterSettings")))
        {
            if (filterTab)
            {
                if (ImGui.Checkbox(GetLoc("AutoReplyChatBot-EnableFilterModel"), ref ModuleConfig.EnableFilter))
                    SaveConfig(ModuleConfig);
                ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-EnableFilterModel-Help"));

                using (ImRaii.Disabled(!ModuleConfig.EnableFilter))
                {
                    ImGui.SetNextItemWidth(fieldW);
                    if (ImGui.InputText($"{GetLoc("AutoReplyChatBot-Model")}##FilterModelInput", ref ModuleConfig.FilterModel, 128))
                        SaveConfig(ModuleConfig);
                    ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-FiterModelChoice-Help"));

                    ImGui.NewLine();

                    ImGui.TextUnformatted(GetLoc("AutoReplyChatBot-FilterSystemPrompt"));

                    ImGui.SameLine();

                    if (ImGui.SmallButton($"{GetLoc("Reset")}##ResetFilterPrompt"))
                    {
                        ModuleConfig.FilterPrompt = FILTER_SYSTEM_PROMPT;
                        SaveConfig(ModuleConfig);
                    }

                    ImGui.InputTextMultiline("##FilterSystemPrompt", ref ModuleConfig.FilterPrompt, 4096, new(promptW, promptH));
                    if (ImGui.IsItemDeactivatedAfterEdit())
                        SaveConfig(ModuleConfig);
                }
            }
        }

        using (var systemPromptTab = ImRaii.TabItem(GetLoc("AutoReplyChatBot-SystemPrompt")))
        {
            if (systemPromptTab)
            {
                if (ModuleConfig.SelectedPromptIndex < 0 ||
                    ModuleConfig.SelectedPromptIndex >= ModuleConfig.SystemPrompts.Count)
                {
                    ModuleConfig.SelectedPromptIndex = 0;
                    SaveConfig(ModuleConfig);
                }

                var selectedPrompt = ModuleConfig.SystemPrompts[ModuleConfig.SelectedPromptIndex];

                ImGui.SetNextItemWidth(fieldW);

                using (var combo = ImRaii.Combo("##PromptSelector", selectedPrompt.Name))
                {
                    if (combo)
                    {
                        for (var i = 0; i < ModuleConfig.SystemPrompts.Count; i++)
                            if (ImGui.Selectable(ModuleConfig.SystemPrompts[i].Name, i == ModuleConfig.SelectedPromptIndex))
                            {
                                ModuleConfig.SelectedPromptIndex = i;
                                SaveConfig(ModuleConfig);
                            }
                    }
                }

                ImGui.SameLine();

                if (ImGui.Button(GetLoc("Add")))
                {
                    var newPromptName = $"Prompt {ModuleConfig.SystemPrompts.Count + 1}";
                    ModuleConfig.SystemPrompts.Add
                    (
                        new()
                        {
                            Name    = newPromptName,
                            Content = string.Empty
                        }
                    );
                    ModuleConfig.SelectedPromptIndex = ModuleConfig.SystemPrompts.Count - 1;
                    SaveConfig(ModuleConfig);
                }

                ImGui.SameLine();

                using (ImRaii.Disabled(ModuleConfig.SelectedPromptIndex == 0))
                {
                    if (ImGui.Button(GetLoc("Delete")))
                    {
                        ModuleConfig.SystemPrompts.RemoveAt(ModuleConfig.SelectedPromptIndex);
                        if (ModuleConfig.SelectedPromptIndex >= ModuleConfig.SystemPrompts.Count)
                            ModuleConfig.SelectedPromptIndex = ModuleConfig.SystemPrompts.Count - 1;

                        SaveConfig(ModuleConfig);
                    }
                }

                if (ModuleConfig.SelectedPromptIndex == 0)
                {
                    ImGui.SameLine();

                    if (ImGui.Button(GetLoc("Reset")))
                    {
                        ModuleConfig.SystemPrompts[0].Content = DEFAULT_SYSTEM_PROMPT;
                        SaveConfig(ModuleConfig);
                    }
                }

                ImGui.NewLine();

                ImGui.SetNextItemWidth(fieldW);

                using (ImRaii.Disabled(ModuleConfig.SelectedPromptIndex == 0))
                {
                    if (ImGui.InputText(GetLoc("Name"), ref selectedPrompt.Name, 128))
                        SaveConfig(ModuleConfig);
                }

                if (ModuleConfig.SelectedPromptIndex == 0)
                {
                    ImGui.SameLine(0, 8f * GlobalFontScale);
                    ImGui.TextDisabled($"({GetLoc("Default")})");
                }

                ImGui.InputTextMultiline("##SystemPrompt", ref selectedPrompt.Content, 4096, new(promptW, promptH));
                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveConfig(ModuleConfig);
            }
        }

        using (var worldBookTab = ImRaii.TabItem(GetLoc("AutoReplyChatBot-WorldBook")))
        {
            if (worldBookTab)
            {
                if (ImGui.Checkbox(GetLoc("AutoReplyChatBot-EnableWorldBook"), ref ModuleConfig.EnableWorldBook))
                    SaveConfig(ModuleConfig);

                if (ModuleConfig.EnableWorldBook)
                {
                    ImGui.SetNextItemWidth(fieldW);
                    if (ImGui.InputInt(GetLoc("AutoReplyChatBot-MaxWorldBookContext"), ref ModuleConfig.MaxWorldBookContext, 256, 2048))
                        ModuleConfig.MaxWorldBookContext = Math.Max(256, ModuleConfig.MaxWorldBookContext);
                    if (ImGui.IsItemDeactivatedAfterEdit())
                        SaveConfig(ModuleConfig);

                    ImGui.NewLine();

                    if (ImGui.Button($"{GetLoc("Add")}##AddWorldBook"))
                    {
                        var newKey = $"Entry {ModuleConfig.WorldBookEntry.Count + 1}";
                        ModuleConfig.WorldBookEntry[newKey] = GetLoc("AutoReplyChatBot-WorldBookEntryContent");
                        SaveConfig(ModuleConfig);
                    }

                    if (ModuleConfig.WorldBookEntry.Count > 0)
                    {
                        ImGui.SameLine();

                        if (ImGui.Button($"{GetLoc("Clear")}##ClearWorldBook"))
                        {
                            ModuleConfig.WorldBookEntry.Clear();
                            SaveConfig(ModuleConfig);
                        }
                    }

                    var counter         = -1;
                    var entriesToRemove = new List<string>();

                    foreach (var entry in ModuleConfig.WorldBookEntry)
                    {
                        if (entry.Key == "GameContext") continue;

                        counter++;

                        using var id = ImRaii.PushId($"WorldBookEntry_{counter}");

                        var key   = entry.Key;
                        var value = entry.Value;

                        if (ImGui.CollapsingHeader($"{key}###Header_{counter}"))
                        {
                            using (ImRaii.PushIndent())
                            {
                                // 词条名
                                ImGui.TextUnformatted(GetLoc("AutoReplyChatBot-WorldBookEntryName"));

                                ImGui.SetNextItemWidth(fieldW);
                                ImGui.InputText($"##Key_{key}", ref key, 128);

                                if (ImGui.IsItemDeactivatedAfterEdit())
                                {
                                    if (!string.IsNullOrWhiteSpace(key) && key != entry.Key)
                                    {
                                        ModuleConfig.WorldBookEntry.Remove(entry.Key);
                                        ModuleConfig.WorldBookEntry[key] = value;
                                        SaveConfig(ModuleConfig);

                                        continue;
                                    }
                                }

                                // 词条释义
                                ImGui.TextUnformatted(GetLoc("AutoReplyChatBot-WorldBookEntryContent"));

                                ImGui.SetNextItemWidth(promptW);
                                ImGui.InputTextMultiline($"##Value_{key}", ref value, 2048, new(promptW, 100 * GlobalFontScale));

                                if (ImGui.IsItemDeactivatedAfterEdit())
                                {
                                    ModuleConfig.WorldBookEntry[entry.Key] = value;
                                    SaveConfig(ModuleConfig);

                                    continue;
                                }

                                if (ImGui.Button(GetLoc("Delete")))
                                    entriesToRemove.Add(entry.Key);
                            }
                        }
                    }

                    foreach (var key in entriesToRemove)
                    {
                        ModuleConfig.WorldBookEntry.Remove(key);
                        SaveConfig(ModuleConfig);
                    }
                }
            }
        }

        using (var testChatTab = ImRaii.TabItem(GetLoc("AutoReplyChatBot-TestChat")))
        {
            if (testChatTab)
                DrawTestChatTab();
        }

        using (var historyTab = ImRaii.TabItem(GetLoc("AutoReplyChatBot-HistoryPreview")))
        {
            if (historyTab)
            {
                var keys = ModuleConfig.Histories.Keys.ToArray();

                var noneLabel   = GetLoc("None");
                var displayKeys = new List<string>(keys.Length + 1) { string.Empty };
                displayKeys.AddRange(keys);

                if (ModuleConfig.HistoryKeyIndex < 0 || ModuleConfig.HistoryKeyIndex >= displayKeys.Count)
                    ModuleConfig.HistoryKeyIndex = 0;

                var currentLabel = ModuleConfig.HistoryKeyIndex == 0 ? noneLabel : displayKeys[ModuleConfig.HistoryKeyIndex];

                ImGui.SetNextItemWidth(fieldW);

                using (var combo = ImRaii.Combo("###UserKey", currentLabel))
                {
                    if (combo)
                    {
                        for (var i = 0; i < displayKeys.Count; i++)
                        {
                            var label    = i == 0 ? noneLabel : displayKeys[i];
                            var selected = i == ModuleConfig.HistoryKeyIndex;

                            if (ImGui.Selectable(label, selected))
                            {
                                ModuleConfig.HistoryKeyIndex = i;
                                SaveConfig(ModuleConfig);
                            }
                        }
                    }
                }

                ImGui.SameLine();

                if (ImGui.Button($"{GetLoc("Clear")}##ClearHistory"))
                {
                    if (ModuleConfig.HistoryKeyIndex > 0)
                    {
                        var currentKey = displayKeys[ModuleConfig.HistoryKeyIndex];
                        ModuleConfig.Histories.Remove(currentKey);
                        SaveConfig(ModuleConfig);
                        SaveConfig(ModuleConfig);
                    }
                }

                if (ModuleConfig.HistoryKeyIndex > 0)
                {
                    var currentKey = displayKeys[ModuleConfig.HistoryKeyIndex];
                    var entries    = ModuleConfig.Histories.TryGetValue(currentKey, out var list) ? list.ToList() : [];

                    using (ImRaii.Child("##HistoryViewer", new(promptW, promptH), true))
                    {
                        var isAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2f;

                        for (var i = 0; i < entries.Count; i++)
                        {
                            var message = entries[i];
                            var isUser  = message.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
                            message.LocalTime ??= message.Timestamp.ToUTCDateTimeFromUnixSeconds().ToLocalTime();
                            var timestamp = message.LocalTime?.ToString("HH:mm:ss") ?? string.Empty;

                            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.90f, 0.85f, 1f, 1f), !isUser))
                            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.85f, 0.90f, 1f, 1f), isUser))
                            {
                                if (ImGui.Selectable($"[{timestamp}] [{message.Name}] {message.Text}"))
                                {
                                    ImGui.SetClipboardText(message.Text);
                                    NotificationSuccess($"{GetLoc("CopiedToClipboard")}: {message.Text}");
                                }

                                using (var context = ImRaii.ContextPopupItem($"{i}"))
                                {
                                    if (context)
                                    {
                                        if (ImGui.MenuItem($"{GetLoc("Delete")}"))
                                        {
                                            try
                                            {
                                                ModuleConfig.Histories[currentKey].RemoveAt(i);
                                                break;
                                            }
                                            catch
                                            {
                                                // ignored
                                            }
                                        }
                                    }
                                }
                            }

                            ImGui.Separator();
                        }

                        if (isAtBottom)
                            ImGui.SetScrollHereY(1f);
                    }
                }
            }
        }

        using (var gameContextTab = ImRaii.TabItem(GetLoc("AutoReplyChatBot-GameContext")))
        {
            if (gameContextTab)
            {
                // 启用游戏上下文
                if (ImGui.Checkbox(GetLoc("AutoReplyChatBot-EnableGameContext"), ref ModuleConfig.EnableGameContext))
                    SaveConfig(ModuleConfig);
                ImGuiOm.HelpMarker(GetLoc("AutoReplyChatBot-EnableGameContext-Help"));

                // 游戏上下文设置
                using (ImRaii.Disabled(!ModuleConfig.EnableGameContext))
                using (ImRaii.PushIndent())
                {
                    foreach (var contextType in Enum.GetValues<GameContextType>())
                    {
                        using var id = ImRaii.PushId($"AutoReplyChatBot-GameContext-{contextType}");

                        var enabled = ModuleConfig.GameContextSettings.GetValueOrDefault(contextType, true);
                        var label   = GameContextLocMap.GetValueOrDefault(contextType, contextType.ToString());

                        if (ImGui.Checkbox($"{label}##{contextType}", ref enabled))
                        {
                            ModuleConfig.GameContextSettings[contextType] = enabled;
                            SaveConfig(ModuleConfig);
                            UpdateGameContextInWorldBook();
                        }
                    }
                }
            }
        }
    }

    private void DrawTestChatTab()
    {
        if (ModuleConfig.TestChatWindows.Count == 0)
        {
            var testGUID = Guid.NewGuid().ToString();
            ModuleConfig.TestChatWindows[testGUID] = new ChatWindow
            {
                ID          = testGUID,
                Name        = "Chat Test",
                Role        = "Tester",
                HistoryGUID = testGUID
            };
            ModuleConfig.CurrentActiveChat = testGUID;
            SaveConfig(ModuleConfig);
        }

        // 聊天窗口标签页
        using (var tabBar = ImRaii.TabBar("ChatTabs"))
        {
            if (tabBar)
            {
                if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Trailing))
                {
                    var newGUID = Guid.NewGuid().ToString();
                    ModuleConfig.TestChatWindows[newGUID] = new ChatWindow
                    {
                        ID          = newGUID,
                        Name        = "New Chat",
                        Role        = "NewUser",
                        HistoryGUID = newGUID
                    };
                    ModuleConfig.CurrentActiveChat = newGUID;
                    SaveConfig(ModuleConfig);
                }

                // 聊天窗口标签
                var chatTabs = ModuleConfig.TestChatWindows.ToList();

                foreach (var (id, window) in chatTabs)
                {
                    var isOpen = true;

                    var flags = ImGuiTabItemFlags.None;
                    if (id == ModuleConfig.CurrentActiveChat)
                        flags |= ImGuiTabItemFlags.SetSelected;

                    using (var tabItem = ImRaii.TabItem($"{window.Name}###{id}", ref isOpen, flags))
                    {
                        if (tabItem)
                        {
                            // ignored
                        }
                    }

                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        ModuleConfig.CurrentActiveChat = id;

                    // 点击关闭按钮
                    if (!isOpen && ModuleConfig.TestChatWindows.Count > 1)
                    {
                        ModuleConfig.TestChatWindows.Remove(id);
                        if (ModuleConfig.CurrentActiveChat == id && ModuleConfig.TestChatWindows.Count > 0)
                            ModuleConfig.CurrentActiveChat = ModuleConfig.TestChatWindows.Keys.First();
                        SaveConfig(ModuleConfig);
                    }
                }
            }
        }

        ImGui.Spacing();

        if (!ModuleConfig.TestChatWindows.TryGetValue(ModuleConfig.CurrentActiveChat, out var currentWindow))
            return;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{GetLoc("AutoReplyChatBot-TestChat-Role")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputText("##CurrentRole", ref currentWindow.Role, 96);
        if (ImGui.IsItemDeactivatedAfterEdit())
            SaveConfig(ModuleConfig);

        ImGui.SameLine();
        ImGui.TextUnformatted($"{GetLoc("Name")}:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        ImGui.InputText("##WindowName", ref currentWindow.Name, 96);

        ImGui.SameLine(0, 10f * GlobalFontScale);

        if (ImGui.Button($"{GetLoc("Clear")}"))
        {
            var historyKey = currentWindow.HistoryKey;
            if (ModuleConfig.Histories.TryGetValue(historyKey, out var historyList))
                historyList.Clear();
        }

        ImGui.Spacing();

        var chatHeight = 300f * GlobalFontScale;
        var chatWidth  = ImGui.GetContentRegionAvail().X - 4 * ImGui.GetStyle().ItemSpacing.X;

        using (var child = ImRaii.Child("##ChatMessages", new(chatWidth, chatHeight - 60f * GlobalFontScale), true))
        {
            var isAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2f;

            if (child)
            {
                var historyKey = currentWindow.HistoryKey;
                var messages   = ModuleConfig.Histories.TryGetValue(historyKey, out var list) ? list.ToList() : [];

                for (var i = 0; i < messages.Count; i++)
                {
                    var message = messages[i];
                    var isUser  = message.Role.Equals("user", StringComparison.OrdinalIgnoreCase);

                    var textSize     = ImGui.CalcTextSize(message.Text) + new Vector2(2 * ImGui.GetStyle().ItemSpacing.X, 4 * ImGui.GetStyle().ItemSpacing.Y);
                    var messageWidth = Math.Min(textSize.X + 20f                        * GlobalFontScale, chatWidth * 0.75f);

                    if (isUser)
                        ImGui.SetCursorPosX(chatWidth - messageWidth - 16f * GlobalFontScale);
                    else
                        ImGui.SetCursorPosX(8f * GlobalFontScale);

                    var bgColor   = isUser ? KnownColor.CadetBlue.ToVector4() : KnownColor.SlateGray.ToVector4();
                    var textColor = isUser ? KnownColor.White.ToVector4() : new(0.9f, 0.9f, 0.9f, 1.0f);

                    using (ImRaii.Group())
                    using (ImRaii.PushColor(ImGuiCol.ChildBg, bgColor))
                    using (ImRaii.PushColor(ImGuiCol.Text, textColor))
                    {
                        using (var msgChild = ImRaii.Child
                               (
                                   $"##Msg_{i}",
                                   textSize with { X = messageWidth },
                                   true,
                                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                               ))
                        {
                            if (msgChild)
                            {
                                ImGui.TextWrapped(message.Text);

                                // 右键菜单
                                using var context = ImRaii.ContextPopupItem($"Context_{i}");

                                if (context)
                                {
                                    if (ImGui.MenuItem($"{GetLoc("Copy")}"))
                                    {
                                        ImGui.SetClipboardText(message.Text);
                                        NotificationSuccess($"{GetLoc("CopiedToClipboard")}");
                                    }

                                    if (ImGui.MenuItem($"{GetLoc("Delete")}"))
                                    {
                                        try
                                        {
                                            if (ModuleConfig.Histories.TryGetValue(historyKey, out var historyList) && i < historyList.Count)
                                            {
                                                historyList.RemoveAt(i);
                                                SaveConfig(ModuleConfig);
                                            }

                                            break;
                                        }
                                        catch
                                        {
                                            // ignored
                                        }
                                    }
                                }
                            }
                        }

                        message.LocalTime ??= message.Timestamp.ToUTCDateTimeFromUnixSeconds().ToLocalTime();
                        var timeStr = message.LocalTime?.ToString("HH:mm:ss") ?? string.Empty;

                        using (FontManager.Instance().UIFont80.Push())
                            ImGui.TextDisabled($"[{timeStr}] {message.Name}");
                    }

                    ScaledDummy(0, 6f);
                }
            }

            if (isAtBottom)
                ImGui.SetScrollHereY(1f);
        }

        ImGui.SetNextItemWidth(chatWidth - ImGui.CalcTextSize(GetLoc("AutoReplyChatBot-Send")).X - 4 * ImGui.GetStyle().ItemSpacing.X);
        ImGui.InputText("##MessageInput", ref currentWindow.InputText, 512, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.SameLine();

        if ((ImGui.Button(GetLoc("AutoReplyChatBot-Send")) || ImGui.IsKeyPressed(ImGuiKey.Enter)) &&
            !string.IsNullOrWhiteSpace(currentWindow.InputText))
        {
            var text       = currentWindow.InputText;
            var historyKey = currentWindow.HistoryKey;

            currentWindow.InputText    = string.Empty;
            currentWindow.IsProcessing = true;

            TaskHelper.Abort();
            TaskHelper.DelayNext(1000, "等待 1 秒收集更多消息");
            TaskHelper.Enqueue(IsCooldownReady);
            TaskHelper.EnqueueAsync
            (async () =>
                {
                    SetCooldown();

                    AppendHistory(historyKey, "user", text, currentWindow.Role);
                    var reply = string.Empty;

                    try
                    {
                        reply = await GenerateReplyAsync(ModuleConfig, historyKey, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        NotificationError(GetLoc("AutoReplyChatBot-ErrorTitle"));
                        Error($"{GetLoc("AutoReplyChatBot-ErrorTitle")}:", ex);
                    }

                    if (!string.IsNullOrWhiteSpace(reply))
                        AppendHistory(historyKey, "assistant", reply);

                    currentWindow.IsProcessing = false;
                }
            );
        }
    }
}
