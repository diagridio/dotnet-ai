// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;
using Dapr.Workflow.Payloads;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates <c>WithBinaryStorePayloads</c>. Runs only when <see cref="DaprFixture.BinaryStorePayloads"/>
/// is on; in that mode every other test in the suite also runs against offloaded payloads.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class BinaryStorePayloadTests(DaprFixture fixture)
{
    [Fact]
    public async Task AgentRunFromWorkflow_OutputIsStoredAsReference_AndStillReadsAsValue()
    {
        Assert.SkipUnless(DaprFixture.BinaryStorePayloads, "Set DAPR_BINARY_STORE_PAYLOADS=1 to run.");

        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(EchoOrchestrationWorkflow),
            input: "offload me");

        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        Assert.Equal("Hello from EchoAgent!", state.ReadOutputAs<string>());
        Assert.NotNull(state.ReadOutputAs<PayloadReference<string>>());
    }
}
