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

using Diagrid.AI.Microsoft.AgentFramework.Runtime;

namespace RouterDemo.Models;

public sealed class AgentCatalog
{
    public AgentDescriptor Router { get; } = new(
        Name: AgentIds.RouterName,
        Purpose: "Select the best specialist agent based on the user request and the available agent prompts.",
        Prompt: "You are a routing agent. Select the best specialist agent for the request. " +
                "Only choose from the available agent list and return JSON only.",
        OutputSchema: "{\"agentName\":\"string\",\"reason\":\"string\",\"confidence\":0.0}",
        ConversationComponentName: AgentIds.TinyComponent,
        OutputKind: AgentOutputKind.Classification);

    public AgentDescriptor RouterWorkflow { get; } = new(
        Name: AgentIds.RouterWorkflowName,
        Purpose: "Run the router workflow internally and return the consolidated routing result.",
        Prompt: "You execute the internal router workflow and return the final routing result as JSON only.",
        OutputSchema: "{\"status\":\"string\",\"agentName\":\"string\",\"modelComponent\":\"string\",\"expectedSchema\":\"string\",\"result\":{},\"routerReason\":\"string\",\"attempts\":{\"router\":0,\"coordinator\":0,\"agent\":0},\"error\":\"string\"}",
        ConversationComponentName: AgentIds.TinyComponent,
        OutputKind: AgentOutputKind.Classification);

    public AgentDescriptor Coordinator { get; } = new(
        Name: AgentIds.CoordinatorName,
        Purpose: "Craft a precise, schema-focused prompt for the selected specialist agent.",
        Prompt: "You are a coordinator agent. Given the target agent, its prompt, and the expected schema, " +
                "produce a strict message that will yield valid JSON. Return JSON only.",
        OutputSchema: "{\"targetAgent\":\"string\",\"message\":\"string\",\"expectedSchema\":\"string\",\"retryNotes\":\"string\"}",
        ConversationComponentName: AgentIds.TinyComponent,
        OutputKind: AgentOutputKind.Classification);

    public IReadOnlyList<AgentDescriptor> RoutableAgents { get; } =
    [
        new AgentDescriptor(
            Name: AgentIds.SummaryName,
            Purpose: "Summarize requests into a short executive summary and bullet list.",
            Prompt: "You summarize user requests into a concise summary with 2-4 bullets. " +
                    "Return JSON only with fields summary, bullets, and confidence.",
            OutputSchema: "{\"summary\":\"string\",\"bullets\":[\"string\"],\"confidence\":0.0}",
            ConversationComponentName: AgentIds.GemmaComponent,
            OutputKind: AgentOutputKind.Summary),
        new AgentDescriptor(
            Name: AgentIds.ClassificationName,
            Purpose: "Classify requests into a primary category with tags.",
            Prompt: "You classify requests into a single category and supporting tags. " +
                    "Return JSON only with fields category, tags, and confidence.",
            OutputSchema: "{\"category\":\"string\",\"tags\":[\"string\"],\"confidence\":0.0}",
            ConversationComponentName: AgentIds.QwenComponent,
            OutputKind: AgentOutputKind.Classification),
        new AgentDescriptor(
            Name: AgentIds.PlanName,
            Purpose: "Provide a short action plan with steps and risks.",
            Prompt: "You create a short action plan with 3-5 steps and note key risks. " +
                    "Return JSON only with fields steps, risks, and confidence.",
            OutputSchema: "{\"steps\":[\"string\"],\"risks\":\"string\",\"confidence\":0.0}",
            ConversationComponentName: AgentIds.TinyComponent,
            OutputKind: AgentOutputKind.Plan)
    ];

    public IReadOnlyList<AgentDescriptor> GetRoutableAgents(AgentRegistry registry)
    {
        var registered = new HashSet<string>(registry.RegisteredNames, StringComparer.OrdinalIgnoreCase);
        return RoutableAgents.Where(agent => registered.Contains(agent.Name)).ToArray();
    }
}
