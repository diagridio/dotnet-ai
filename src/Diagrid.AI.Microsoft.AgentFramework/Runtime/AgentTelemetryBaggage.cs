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

internal static class AgentTelemetryBaggage
{
    internal const string AgentNameKey = "agent.name";
    internal const string AgentChatClientKey = "agent.chat_client_key";
    internal const string AgentOperationKey = "agent.operation";
    internal const string ToolNameKey = "tool.name";
    internal const string ToolCallIdKey = "tool.call_id";

    internal const string WorkflowOperation = "workflow";
    internal const string LlmOperation = "llm";
    internal const string ToolOperation = "tool";

    public static void SetAgent(string agentName, string? chatClientKey, string operation)
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
    }

    public static void SetTool(string agentName, string functionName, string callId)
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
    }
}
