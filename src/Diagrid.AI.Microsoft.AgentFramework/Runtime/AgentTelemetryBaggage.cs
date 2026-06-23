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

using System.Diagnostics;

namespace Diagrid.AI.Microsoft.AgentFramework.Runtime;

/// <summary>
/// Helpers and key names for OpenTelemetry baggage used by Dapr agent workflow activities.
/// </summary>
public static class AgentTelemetryBaggage
{
    /// <summary>
    /// Baggage key containing the agent name.
    /// </summary>
    public const string AgentNameKey = "agent.name";

    /// <summary>
    /// Baggage key containing the chat client key for keyed agent registrations.
    /// </summary>
    public const string AgentChatClientKey = "agent.chat_client_key";

    /// <summary>
    /// Baggage key containing the current agent operation.
    /// </summary>
    public const string AgentOperationKey = "agent.operation";

    /// <summary>
    /// Baggage key containing the tool name for tool activity invocations.
    /// </summary>
    public const string ToolNameKey = "tool.name";

    /// <summary>
    /// Baggage key containing the tool call ID for tool activity invocations.
    /// </summary>
    public const string ToolCallIdKey = "tool.call_id";

    internal const string WorkflowOperation = "workflow";
    internal const string LlmOperation = "llm";
    internal const string ToolOperation = "tool";

    internal static void SetAgent(
        string agentName,
        string? chatClientKey,
        string operation,
        IReadOnlyDictionary<string, string?>? customBaggage = null)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        current.SetBaggage(AgentNameKey, agentName);
        current.SetBaggage(AgentOperationKey, operation);

        if (!string.IsNullOrWhiteSpace(chatClientKey))
        {
            current.SetBaggage(AgentChatClientKey, chatClientKey);
        }

        SetCustom(current, customBaggage);
    }

    internal static void SetTool(
        string agentName,
        string functionName,
        string callId,
        IReadOnlyDictionary<string, string?>? customBaggage = null)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        current.SetBaggage(AgentNameKey, agentName);
        current.SetBaggage(AgentOperationKey, ToolOperation);
        current.SetBaggage(ToolNameKey, functionName);
        current.SetBaggage(ToolCallIdKey, callId);
        SetCustom(current, customBaggage);
    }

    /// <summary>
    /// Applies custom baggage values to the current activity, if one exists.
    /// </summary>
    /// <param name="baggage">Custom baggage values to add to <see cref="Activity.Current"/>.</param>
    public static void SetCustom(IReadOnlyDictionary<string, string?>? baggage)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        SetCustom(current, baggage);
    }

    internal static Dictionary<string, string?>? Copy(IReadOnlyDictionary<string, string?>? baggage) =>
        baggage is { Count: > 0 }
            ? new Dictionary<string, string?>(baggage, StringComparer.Ordinal)
            : null;

    private static void SetCustom(Activity activity, IReadOnlyDictionary<string, string?>? baggage)
    {
        if (baggage is null)
        {
            return;
        }

        foreach (var item in baggage)
        {
            activity.SetBaggage(item.Key, item.Value);
        }
    }
}
