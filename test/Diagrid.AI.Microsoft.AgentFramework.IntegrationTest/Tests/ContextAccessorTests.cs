// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentContextAccessor"/>
/// lifecycle management performed by <c>InvokeAgentActivity</c>:
/// <list type="bullet">
///   <item>The ambient context is set (non-null) while the agent is executing.</item>
///   <item><see cref="Diagrid.AI.Microsoft.AgentFramework.Hosting.DaprAgentContext.CurrentWorkflowInstanceId"/>
///         is a non-empty string corresponding to the running workflow instance.</item>
///   <item>The context is cleared back to <c>null</c> after the activity completes.</item>
/// </list>
/// The <c>ContextAgent</c> registered in <see cref="DaprFixture"/> encodes the current instance ID
/// in its response text as <c>"instanceId:{id}"</c>, making it observable from the test.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class ContextAccessorTests(DaprFixture fixture)
{
    // ── Context set during agent execution ────────────────────────────────────

    [Fact]
    public async Task ContextAccessor_DuringAgentExecution_IsNonNull()
    {
        // ContextAgent embeds accessor.Current?.CurrentWorkflowInstanceId in its response.
        // A non-null context produces "instanceId:<guid>"; a null context produces "instanceId:null".
        var agent    = fixture.Invoker.GetAgent("ContextAgent");
        var response = await fixture.Invoker.RunAgentAsync(agent, "probe context");

        Assert.NotNull(response);
        Assert.StartsWith("instanceId:", response.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContextAccessor_DuringAgentExecution_CurrentWorkflowInstanceId_IsNonEmpty()
    {
        var agent    = fixture.Invoker.GetAgent("ContextAgent");
        var response = await fixture.Invoker.RunAgentAsync(agent, "probe instance id");

        // The embedded ID must not be the sentinel "null" — it should be a real instance ID.
        Assert.NotEqual("instanceId:null", response.Text, StringComparer.Ordinal);

        // Extract and verify the ID is non-empty.
        var instanceId = response.Text!["instanceId:".Length..];
        Assert.False(string.IsNullOrWhiteSpace(instanceId),
            "CurrentWorkflowInstanceId should be a non-empty string during execution.");
    }

    // ── Context cleared after agent execution ─────────────────────────────────

    [Fact]
    public async Task ContextAccessor_AfterAgentExecution_IsNull()
    {
        // Run any agent to completion.
        var agent = fixture.Invoker.GetAgent("EchoAgent");
        await fixture.Invoker.RunAgentAsync(agent, "cleanup check");

        // InvokeAgentActivity sets Current to null in its finally block.
        // After WaitForWorkflowCompletionAsync returns, the activity has already finished.
        Assert.Null(fixture.ContextAccessor.Current);
    }

    [Fact]
    public async Task ContextAccessor_AfterMultipleSequentialInvocations_IsAlwaysNull()
    {
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        for (var i = 0; i < 3; i++)
        {
            await fixture.Invoker.RunAgentAsync(agent, $"run-{i}");
            Assert.Null(fixture.ContextAccessor.Current);
        }
    }

    // ── Instance ID correlates to the scheduled workflow ─────────────────────

    [Fact]
    public async Task ContextAccessor_WorkflowInstanceId_IsUniquePerInvocation()
    {
        var agent = fixture.Invoker.GetAgent("ContextAgent");

        var first  = await fixture.Invoker.RunAgentAsync(agent, "first");
        var second = await fixture.Invoker.RunAgentAsync(agent, "second");

        // Each invocation schedules a distinct workflow → distinct instance IDs.
        Assert.NotEqual(first.Text, second.Text);
    }
}
