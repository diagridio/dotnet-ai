// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Workflow;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates <see cref="Diagrid.AI.Microsoft.AgentFramework.Runtime.WorkflowContextExtensions"/>:
/// invoking agents and deserializing typed responses from <em>within</em> a Dapr Workflow, rather
/// than via <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentInvoker"/>.
/// Each test schedules a custom workflow (registered in <see cref="DaprFixture"/>) and waits for
/// its completion via <see cref="DaprWorkflowClient"/>.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class WorkflowContextExtensionsTests(DaprFixture fixture)
{
    // ── context.GetAgent + context.RunAgentAsync ─────────────────────────────

    [Fact]
    public async Task RunAgentAsync_FromWorkflowContext_ReturnsExpectedResponse()
    {
        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(EchoOrchestrationWorkflow),
            input: "invoke from workflow");

        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        var result = state.ReadOutputAs<string>();
        Assert.Equal("Hello from EchoAgent!", result);
    }

    [Fact]
    public async Task GetAgent_FromWorkflowContext_ResolvesCorrectAgent()
    {
        // Two independent workflow instances ensure agent resolution works for different names.
        var echoId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(EchoOrchestrationWorkflow),
            input: "echo check");

        var echoState = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(echoId);
        Assert.Equal(WorkflowRuntimeStatus.Completed, echoState.RuntimeStatus);
        Assert.Equal("Hello from EchoAgent!", echoState.ReadOutputAs<string>());
    }

    [Fact]
    public async Task RunAgentAsync_FromWorkflowContext_MultipleInvocations_AllSucceed()
    {
        // Schedule several workflow instances concurrently to validate parallelism.
        var tasks = Enumerable.Range(0, 3).Select(async i =>
        {
            var id    = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
                name:  nameof(EchoOrchestrationWorkflow),
                input: $"concurrent-workflow-{i}");
            var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(id);
            return state;
        });

        var states = await Task.WhenAll(tasks);

        Assert.All(states, s =>
        {
            Assert.Equal(WorkflowRuntimeStatus.Completed, s.RuntimeStatus);
            Assert.Equal("Hello from EchoAgent!", s.ReadOutputAs<string>());
        });
    }

    // ── context.RunAgentAndDeserializeAsync<T> ────────────────────────────────

    [Fact]
    public async Task RunAgentAndDeserializeAsync_FromWorkflowContext_ReturnsTypedObject()
    {
        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(CapitalOrchestrationWorkflow),
            input: "What is the capital of France?");

        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        var result = state.ReadOutputAs<CapitalAnswer>();
        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
        Assert.Equal(0.99, result.Confidence, precision: 5);
    }

    // ── context.GetAgent with chat-client key ────────────────────────────────

    [Fact]
    public async Task GetAgent_FromWorkflowContext_WithChatClientKey_ResolvesKeyedAgent()
    {
        var instanceId = await fixture.WorkflowClient.ScheduleNewWorkflowAsync(
            name:  nameof(KeyedOrchestrationWorkflow),
            input: "keyed message from workflow");

        var state = await fixture.WorkflowClient.WaitForWorkflowCompletionAsync(instanceId);

        Assert.Equal(WorkflowRuntimeStatus.Completed, state.RuntimeStatus);
        Assert.Equal("Alpha response", state.ReadOutputAs<string>());
    }
}
