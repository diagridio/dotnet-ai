// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using Grpc.Core;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates cancellation-token propagation through the
/// <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentInvoker"/> pipeline.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class CancellationTests(DaprFixture fixture)
{
    // ── Pre-cancelled token ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_WithAlreadyCancelledToken_ThrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var agent = fixture.Invoker.GetAgent("EchoAgent");

        var ex = await Record.ExceptionAsync(
            () => fixture.Invoker.RunAgentAsync(agent, "msg", cancellationToken: cts.Token));

        // The Dapr gRPC client surfaces cancellation as either OperationCanceledException
        // or RpcException with StatusCode.Cancelled — both are acceptable.
        Assert.NotNull(ex);
        Assert.True(IsCancellation(ex),
            $"Expected cancellation but got {ex.GetType().Name}: {ex.Message}");
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithAlreadyCancelledToken_ThrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var agent = fixture.Invoker.GetAgent("CapitalAgent");

        var ex = await Record.ExceptionAsync(
            () => fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
                agent, message: "capital?", cancellationToken: cts.Token));

        Assert.NotNull(ex);
        Assert.True(IsCancellation(ex),
            $"Expected cancellation but got {ex.GetType().Name}: {ex.Message}");
    }

    private static bool IsCancellation(Exception ex) =>
        ex is OperationCanceledException ||
        (ex is RpcException rpc && rpc.StatusCode == StatusCode.Cancelled);

    // ── Fresh (non-cancelled) token ───────────────────────────────────────────

    [Fact]
    public async Task RunAgentAsync_WithFreshCancellationToken_CompletesNormally()
    {
        using var cts     = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var       agent   = fixture.Invoker.GetAgent("EchoAgent");
        var       response = await fixture.Invoker.RunAgentAsync(
            agent, message: "token not cancelled", cancellationToken: cts.Token);

        Assert.NotNull(response);
        Assert.Equal("Hello from EchoAgent!", response.Text);
    }

    [Fact]
    public async Task RunAgentAsync_ByName_WithFreshCancellationToken_CompletesNormally()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var response = await fixture.Invoker.RunAgentAsync(
            "GreetingAgent", message: "fresh token", cancellationToken: cts.Token);

        Assert.Equal("Hello!", response.Text);
    }
}
