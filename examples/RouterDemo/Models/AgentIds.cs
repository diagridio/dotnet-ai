// Copyright (c) 2026-present Diagrid Inc
// 
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
// 
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2029
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

namespace RouterDemo.Models;

public static class AgentIds
{
    public const string RouterWorkflowName = "RouterWorkflowAgent";
    public const string RouterName = "RouterAgent";
    public const string CoordinatorName = "CoordinatorAgent";
    public const string SummaryName = "SummaryAgent";
    public const string ClassificationName = "ClassificationAgent";
    public const string PlanName = "PlanAgent";

    public const string TinyComponent = "conversation-ollama-tiny";
    public const string GemmaComponent = "conversation-ollama-gemma";
    public const string QwenComponent = "conversation-ollama-qwen";
}
