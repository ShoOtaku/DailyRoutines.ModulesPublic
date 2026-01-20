using System;
using System.Collections.Generic;
using System.Text;

namespace DailyRoutines.ModulesPublic;

public partial class AutoReplyChatBot
{
    private static void UpdateGameContextInWorldBook()
    {
        if (ModuleConfig is not { EnableGameContext: true }) return;

        var contextParts = new List<string>();

        foreach (var contextType in Enum.GetValues<GameContextType>())
        {
            if (ModuleConfig.GameContextSettings.TryGetValue(contextType, out var enabled) && enabled)
            {
                var value = GameContextValueMap[contextType]();
                contextParts.Add($"{contextType}:{value}");
            }
        }

        var context = string.Join(", ", contextParts);
        ModuleConfig.WorldBookEntry["GameContext"] = context;
    }

    private static class WorldBookManager
    {
        public static List<KeyValuePair<string, string>> FindRelevantEntries(string userMessage, Dictionary<string, string> entries)
        {
            if (string.IsNullOrWhiteSpace(userMessage) || entries is not { Count: > 0 })
                return [];

            var matches = new List<KeyValuePair<string, string>>();
            var message = userMessage.ToLowerInvariant();

            foreach (var entry in entries)
            {
                if (entry.Key == "GameContext"                                               ||
                    entry.Key.Contains(message, StringComparison.InvariantCultureIgnoreCase) ||
                    message.Contains(entry.Key, StringComparison.InvariantCultureIgnoreCase))
                    matches.Add(entry);
            }

            return matches;
        }

        public static string BuildWorldBookContext(List<KeyValuePair<string, string>> matches, int maxLength)
        {
            var context = new StringBuilder();
            context.AppendLine("[WorldBookInfo]");

            var currentLength = 0;

            foreach (var match in matches)
            {
                var content = match.Value.Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                var entryText     = $"[{match.Key}]: {content}";
                var contentLength = entryText.Length;
                if (currentLength + contentLength > maxLength) break;

                context.AppendLine(entryText);
                currentLength += contentLength;
            }

            return context.ToString();
        }
    }
}
