using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DailyRoutines.Helpers;
using DailyRoutines.Managers;
using Dalamud.Game.Text;
using Dalamud.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DailyRoutines.ModulesPublic;

public partial class AutoReplyChatBot
{
    private static readonly Dictionary<APIProvider, IChatBackend> Backends = new()
    {
        [APIProvider.OpenAI] = new OpenAIBackend(),
        [APIProvider.Ollama] = new OllamaBackend(),
    };

    private static async Task GenerateAndReplyAsync(string name, string world, XivChatType originalType)
    {
        var target = $"{name}@{world}";
        var reply  = string.Empty;

        SetCooldown();

        try
        {
            reply = await GenerateReplyAsync(ModuleConfig, target, CancellationToken.None);
        }
        catch (Exception ex)
        {
            NotificationError(GetLoc("AutoReplyChatBot-ErrorTitle"));
            Error($"{GetLoc("AutoReplyChatBot-ErrorTitle")}:", ex);

            reply = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(reply))
            return;

        switch (originalType)
        {
            case XivChatType.TellIncoming:
                ChatManager.Instance().SendMessage($"/tell {target} {reply}");
                break;
            case XivChatType.Party:
                ChatManager.Instance().SendMessage($"/p {reply}");
                break;
            case XivChatType.FreeCompany:
                ChatManager.Instance().SendMessage($"/fc {reply}");
                break;
            case XivChatType.Ls1:
                ChatManager.Instance().SendMessage($"/l1 {reply}");
                break;
            case XivChatType.Ls2:
                ChatManager.Instance().SendMessage($"/l2 {reply}");
                break;
            case XivChatType.Ls3:
                ChatManager.Instance().SendMessage($"/l3 {reply}");
                break;
            case XivChatType.Ls4:
                ChatManager.Instance().SendMessage($"/l4 {reply}");
                break;
            case XivChatType.Ls5:
                ChatManager.Instance().SendMessage($"/l5 {reply}");
                break;
            case XivChatType.Ls6:
                ChatManager.Instance().SendMessage($"/l6 {reply}");
                break;
            case XivChatType.Ls7:
                ChatManager.Instance().SendMessage($"/l7 {reply}");
                break;
            case XivChatType.Ls8:
                ChatManager.Instance().SendMessage($"/l8 {reply}");
                break;
            case XivChatType.CrossLinkShell1:
                ChatManager.Instance().SendMessage($"/cwlinkshell1 {reply}");
                break;
            case XivChatType.CrossLinkShell2:
                ChatManager.Instance().SendMessage($"/cwlinkshell2 {reply}");
                break;
            case XivChatType.CrossLinkShell3:
                ChatManager.Instance().SendMessage($"/cwlinkshell3 {reply}");
                break;
            case XivChatType.CrossLinkShell4:
                ChatManager.Instance().SendMessage($"/cwlinkshell4 {reply}");
                break;
            case XivChatType.CrossLinkShell5:
                ChatManager.Instance().SendMessage($"/cwlinkshell5 {reply}");
                break;
            case XivChatType.CrossLinkShell6:
                ChatManager.Instance().SendMessage($"/cwlinkshell6 {reply}");
                break;
            case XivChatType.CrossLinkShell7:
                ChatManager.Instance().SendMessage($"/cwlinkshell7 {reply}");
                break;
            case XivChatType.CrossLinkShell8:
                ChatManager.Instance().SendMessage($"/cwlinkshell8 {reply}");
                break;
            case XivChatType.Say:
                ChatManager.Instance().SendMessage($"/say {reply}");
                break;
            case XivChatType.Yell:
                ChatManager.Instance().SendMessage($"/yell {reply}");
                break;
            case XivChatType.Shout:
                ChatManager.Instance().SendMessage($"/shout {reply}");
                break;
            default:
                ChatManager.Instance().SendMessage($"/tell {target} {reply}");
                break;
        }

        NotificationInfo(reply, $"{GetLoc("AutoReplyChatBot-AutoRepliedTo")}{target}");
        AppendHistory(target, "assistant", reply);
    }

    private static async Task<string?> GenerateReplyAsync(Config cfg, string historyKey, CancellationToken ct)
    {
        UpdateGameContextInWorldBook();

        if (cfg.APIKey.IsNullOrWhitespace() || cfg.BaseURL.IsNullOrWhitespace() || cfg.Model.IsNullOrWhitespace())
            return null;

        var hist = ModuleConfig.Histories.TryGetValue(historyKey, out var list) ? list.ToList() : [];
        if (hist.Count == 0)
            return null;

        if (cfg.EnableContextLimit)
        {
            var userMessagesCount = hist.Count(x => x.Role == "user");
            if (userMessagesCount > cfg.MaxContextMessages)
                return null;
        }

        var userMessage = hist.LastOrDefault(x => x.Role == "user").Text;
        if (string.IsNullOrWhiteSpace(userMessage)) return null;

        if (cfg.EnableFilter && !string.IsNullOrWhiteSpace(cfg.FilterModel))
        {
            var filteredMessage = await FilterMessageAsync(cfg, userMessage, ct);
            switch (filteredMessage)
            {
                case null:
                    return null;
            }

            if (filteredMessage != userMessage)
            {
                if (ModuleConfig.Histories.TryGetValue(historyKey, out var originalList))
                {
                    for (var i = originalList.Count - 1; i >= 0; i--)
                    {
                        if (originalList[i].Role == "user")
                        {
                            originalList[i] = new ChatMessage(originalList[i].Role, filteredMessage, originalList[i].Timestamp, originalList[i].Name);
                            break;
                        }
                    }
                }

                for (var i = hist.Count - 1; i >= 0; i--)
                {
                    if (hist[i].Role == "user")
                    {
                        hist[i] = new ChatMessage(hist[i].Role, filteredMessage, hist[i].Timestamp, hist[i].Name);
                        break;
                    }
                }
            }
        }

        var url = Backends[cfg.Provider].BuildURL(cfg.BaseURL);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.APIKey);

        if (cfg.SelectedPromptIndex < 0 || cfg.SelectedPromptIndex >= cfg.SystemPrompts.Count)
            cfg.SelectedPromptIndex = 0;
        var currentPrompt = cfg.SystemPrompts[cfg.SelectedPromptIndex];
        var sys = string.IsNullOrWhiteSpace(currentPrompt.Content)
                      ? DEFAULT_SYSTEM_PROMPT
                      : currentPrompt.Content;

        var worldBookContext = string.Empty;
        if (cfg is { EnableWorldBook: true, WorldBookEntry.Count: > 0 })
        {
            var lastUserMessage = hist.LastOrDefault(x => x.Role == "user").Text;
            if (!string.IsNullOrWhiteSpace(lastUserMessage))
            {
                var relevantEntries = WorldBookManager.FindRelevantEntries(lastUserMessage, cfg.WorldBookEntry);
                worldBookContext = WorldBookManager.BuildWorldBookContext(relevantEntries, cfg.MaxWorldBookContext);
            }
        }

        var messages = new List<object>
        {
            new { role = "system", content = sys }
        };

        if (!string.IsNullOrWhiteSpace(worldBookContext))
            messages.Add(new { role = "system", content = worldBookContext });

        var messagesToSend = hist;
        if (cfg is { EnableContextLimit: true, MaxContextMessages: > 0 })
        {
            // 每轮对话包含用户和 AI 消息
            var totalMessagesToTake = Math.Min(cfg.MaxContextMessages * 2, hist.Count);
            messagesToSend = hist.Skip(Math.Max(0, hist.Count - totalMessagesToTake)).ToList();
        }

        foreach (var message in messagesToSend)
            messages.Add(new { role = message.Role, content = message.Text });

        var body = Backends[cfg.Provider].BuildRequestBody(messages, cfg.Model, cfg.MaxTokens, cfg.Temperature);

        var json = JsonConvert.SerializeObject(body);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await HTTPClientHelper.Get().SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var jsonResponse = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var jObj         = JObject.Parse(jsonResponse);

        var final = Backends[cfg.Provider].ParseContent(jObj);

        return final.StartsWith("[ATTACK") ? string.Empty : final;
    }

    private static async Task<string?> FilterMessageAsync(Config cfg, string userMessage, CancellationToken ct)
    {
        if (cfg.APIKey.IsNullOrWhitespace() || cfg.BaseURL.IsNullOrWhitespace() || cfg.FilterModel.IsNullOrWhitespace())
            return userMessage;

        var url = Backends[cfg.Provider].BuildURL(cfg.BaseURL);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.APIKey);

        // 无记忆
        var messages = new List<object>
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(cfg.FilterPrompt) ? FILTER_SYSTEM_PROMPT : cfg.FilterPrompt },
            new { role = "user", content   = userMessage }
        };

        var body = Backends[cfg.Provider].BuildRequestBody(messages, cfg.Model, 512, 0.0f); // 过滤器不需要太多token & 极低温度，确保严格按照规则执行

        var json = JsonConvert.SerializeObject(body);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await HTTPClientHelper.Get().SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var jsonResponse = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var jObj = JObject.Parse(jsonResponse);

            var content = Backends[cfg.Provider].ParseContent(jObj);

            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch (Exception ex)
        {
            Error($"过滤失败: {ex.Message}");
            return null;
        }
    }

    private interface IChatBackend
    {
        string BuildURL(string baseURL);

        /// <summary>
        /// 组装完整的请求体，包括 messages / model 以及 provider 特定参数。
        /// </summary>
        /// <param name="messages">聊天消息数组</param>
        /// <param name="model">model name</param>
        /// <param name="maxTokens">最大 token</param>
        /// <param name="temperature">采样温度</param>
        /// <returns>序列化前的请求体字典</returns>
        Dictionary<string, object> BuildRequestBody(List<object> messages, string model, int maxTokens, float temperature);

        /// <summary>
        /// 从后端返回的 JSON 字符串中解析出最终的回复文本。
        /// </summary>
        /// <param name="jsonObject">
        /// 已经解析好的 <see cref="JObject"/>，对应完整的接口响应。
        /// </param>
        /// <returns>
        /// - 如果成功，返回 <c>string</c> 类型的对话回复内容。<br/>
        /// - 如果失败（没有 content 字段或结构不符），返回 <c>null</c>。
        /// </returns>
        string? ParseContent(JObject jsonObject);
    }

    private class OpenAIBackend : IChatBackend
    {
        public string BuildURL(string baseURL) => baseURL.TrimEnd('/') + "/chat/completions";

        public Dictionary<string, object> BuildRequestBody(List<object> messages, string model, int maxTokens, float temperature)
        {
            var body = new Dictionary<string, object>
            {
                ["messages"] = messages,
                ["model"]    = model,
                ["max_tokens"] = maxTokens,
                ["temperature"] = temperature
            };
            return body;
        }

        public string? ParseContent(JObject jsonObject)
        {
            var msg = jsonObject["choices"] is JArray { Count: > 0 } choices ? choices[0]["message"] : null;
            return msg?["content"]?.Value<string>();
        }
    }

    private class OllamaBackend : IChatBackend
    {
        public string BuildURL(string baseURL) => baseURL.TrimEnd('/') + "/chat";

        public Dictionary<string, object> BuildRequestBody(List<object> messages, string model, int maxTokens, float temperature)
        {
            var body = new Dictionary<string, object>
            {
                ["messages"] = messages,
                ["model"]    = model,
                ["stream"]   = false,
                ["think"]    = false,
                ["options"]  = new Dictionary<string, object>
                {
                    ["num_predict"] = maxTokens,
                    ["temperature"] = temperature
                }
            };
            return body;
        }

        public string? ParseContent(JObject jsonObject)
        {
            var messageToken = jsonObject["message"];
            return messageToken?["content"]?.Value<string>();
        }
    }
}
