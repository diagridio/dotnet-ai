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

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// A rough token count for history: characters / 4, over every text the model would read.
/// </summary>
internal static class TokenEstimate
{
    /// <summary>The characters counted as one token.</summary>
    internal const int CharsPerToken = 4;

    /// <summary>The estimate for chat messages as they are sent: text, reasoning, tool calls and tool results.</summary>
    internal static long Of(IEnumerable<ChatMessage> messages)
    {
        long chars = 0;
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                chars += content switch
                {
                    TextContent text => text.Text?.Length ?? 0,
                    TextReasoningContent reasoning => (long)(reasoning.Text?.Length ?? 0) + (reasoning.ProtectedData?.Length ?? 0),
                    FunctionCallContent call => call.Name.Length
                        + (call.Arguments is { Count: > 0 } args ? JsonSerializer.Serialize(args).Length : 0),
                    FunctionResultContent result => result.Result switch
                    {
                        null => 0,
                        string s => s.Length,
                        JsonElement e => e.GetRawText().Length,
                        var other => JsonSerializer.Serialize(other).Length,
                    },
                    _ => 0,
                };
            }
        }

        return chars / CharsPerToken;
    }

    internal static long Of(IEnumerable<WorkflowChatMessage> messages)
    {
        long chars = 0;
        foreach (var message in messages)
        {
            chars += message.Content?.Length ?? 0;
            if (message.Reasoning is { } reasoning)
            {
                chars += reasoning.Sum(r => (long)(r.Text?.Length ?? 0) + (r.ProtectedData?.Length ?? 0));
            }

            if (message.FunctionCalls is { } calls)
            {
                chars += calls.Sum(c => (long)c.Name.Length + c.ArgumentsJson.Length);
            }

            if (message.FunctionResults is { } results)
            {
                chars += results.Sum(r => (long)(r.ResultJson?.Length ?? 0));
            }
        }

        return chars / CharsPerToken;
    }
}
