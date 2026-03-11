// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Microsoft.Extensions.Logging;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

internal static partial class MarkdownCodeFenceHelper
{
    private const string MarkdownTickMarks = "```";

    internal static string StripCodeFenceIfPresent(string text, ILogger logger)
    {
        if (text.StartsWith(MarkdownTickMarks, StringComparison.Ordinal))
        {
            LogContainsMarkdownTicks(logger);

            var firstNewLineIndex = text.IndexOf('\n');
            if (firstNewLineIndex >= 0)
            {
                text = text[(firstNewLineIndex + 1)..];
                var lastBacktickIndex = text.LastIndexOf(MarkdownTickMarks, StringComparison.Ordinal);
                if (lastBacktickIndex >= 0)
                {
                    text = text[..lastBacktickIndex];
                }
            }
        }

        return text;
    }

    internal static string ExtractJsonPayload(string text, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var trimmed = text.Trim();
        trimmed = StripCodeFenceIfPresent(trimmed, logger).Trim();

        var start = FindFirstJsonStart(trimmed);
        if (start < 0)
        {
            return trimmed;
        }

        var end = FindMatchingJsonEnd(trimmed, start);
        if (end < 0)
        {
            return trimmed;
        }

        if (start > 0 || end < trimmed.Length - 1)
        {
            LogExtractedJsonPayload(logger);
        }

        return trimmed.Substring(start, end - start + 1);
    }

    private static int FindFirstJsonStart(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '{' || ch == '[')
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMatchingJsonEnd(string text, int startIndex)
    {
        var stack = new Stack<char>();
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{' || ch == '[')
            {
                stack.Push(ch);
                continue;
            }

            if (ch == '}' || ch == ']')
            {
                if (stack.Count == 0)
                {
                    return -1;
                }

                var open = stack.Pop();
                if ((open == '{' && ch != '}') || (open == '[' && ch != ']'))
                {
                    return -1;
                }

                if (stack.Count == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    [LoggerMessage(LogLevel.Information,
        "The text from the agent contains markdown ticks that will be removed before deserializing the value")]
    private static partial void LogContainsMarkdownTicks(ILogger logger);

    [LoggerMessage(LogLevel.Information,
        "The agent response contained additional text; extracted the first JSON payload for deserialization")]
    private static partial void LogExtractedJsonPayload(ILogger logger);
}
