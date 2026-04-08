// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Testcontainers.Common;
using Dapr.Testcontainers.Common.Options;
using Dapr.Testcontainers.Harnesses;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

// ---------------------------------------------------------------------------
// xUnit v3 collection definition
// Tests that use DaprFixture must be decorated with [Collection(DaprFixture.Collection)].
// ---------------------------------------------------------------------------
[CollectionDefinition(DaprFixture.Collection)]
public sealed class DaprCollection : ICollectionFixture<DaprFixture> { }

/// <summary>
/// xUnit collection fixture that:
/// <list type="bullet">
///   <item>Uses <b>Dapr.Testcontainers</b> <see cref="WorkflowHarness"/> to start Redis,
///         Dapr Placement, Dapr Scheduler, and a Dapr sidecar in a shared Docker network.</item>
///   <item>Builds and runs a minimal ASP.NET Core app that mirrors the <c>AgentInvokerDemo</c>
///         example, with <see cref="TestAIAgent"/> instances standing in for real LLM agents.</item>
///   <item>Exposes <see cref="Invoker"/> and <see cref="Client"/> for use in test classes.</item>
/// </list>
/// Requires Docker to be available.
/// </summary>
/// <remarks>
/// Environment variable overrides:
/// <list type="bullet">
///   <item><c>DAPR_RUNTIME_VERSION</c> – Dapr Docker image tag (default: <c>1.17.0</c>).</item>
/// </list>
/// </remarks>
public sealed class DaprFixture : IAsyncLifetime
{
    /// <summary>xUnit collection name shared by all test classes that use this fixture.</summary>
    public const string Collection = "Dapr Integration";

    private DaprTestEnvironment _environment = null!;
    private WorkflowHarness     _harness     = null!;
    private WebApplication      _app         = null!;

    // ── Public surface for tests ─────────────────────────────────────────────

    /// <summary>
    /// <see cref="IDaprAgentInvoker"/> resolved from the test application's DI container.
    /// </summary>
    public IDaprAgentInvoker Invoker { get; private set; } = null!;

    /// <summary>
    /// <see cref="HttpClient"/> pre-configured to call the running test application.
    /// </summary>
    public HttpClient Client { get; private set; } = null!;

    // ── IAsyncLifetime ────────────────────────────────────────────────────────

    public async ValueTask InitializeAsync()
    {
        var componentsDir = TestDirectoryManager.CreateTestDirectory("agent-components");

        // 1. Shared Dapr infrastructure: Docker network, Placement, Scheduler, and Redis
        //    (actor state store — required for Dapr Workflow execution).
        _environment = await DaprTestEnvironment.CreateWithPooledNetworkAsync(needsActorState: true);
        await _environment.StartAsync();

        // 2. WorkflowHarness writes the Redis state-store component YAML and starts daprd,
        //    wiring it to the shared Placement and Scheduler services.
        //    We pass startApp: null and manage the test app ourselves so we can wire up
        //    AddDaprAgents() after the sidecar ports are known.
        _harness = new DaprHarnessBuilder(componentsDir)
            .WithEnvironment(_environment)
            .BuildWorkflow();
        await _harness.InitializeAsync();

        // 3. Point the test process at the running sidecar so AddDaprAgents picks up the
        //    correct ports when building the WebApplication below.
        //    Both variables are read by the Dapr .NET SDK at client construction time.
        Environment.SetEnvironmentVariable("DAPR_HTTP_PORT", _harness.DaprHttpPort.ToString());
        Environment.SetEnvironmentVariable("DAPR_GRPC_PORT", _harness.DaprGrpcPort.ToString());

        // 4. Build and start the minimal test application on the port the sidecar already
        //    knows about (BaseHarness assigned it via PortUtilities.GetAvailablePort()).
        _app = BuildTestApp(_harness.AppPort);
        await _app.StartAsync();

        Invoker = _app.Services.GetRequiredService<IDaprAgentInvoker>();
        Client  = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_harness.AppPort}"),
            Timeout     = TimeSpan.FromSeconds(60),
        };
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        // WorkflowHarness.DisposeAsync stops the Daprd container and cleans up component YAMLs.
        await _harness.DisposeAsync();

        // DaprTestEnvironment.DisposeAsync stops Placement, Scheduler, Redis, and the Docker network.
        await _environment.DisposeAsync();
    }

    // ── Test application ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal ASP.NET Core application that mirrors the structure of the
    /// <c>AgentInvokerDemo</c> example. All agents are <see cref="TestAIAgent"/> instances
    /// that return predetermined responses without calling a real LLM.
    /// </summary>
    private static WebApplication BuildTestApp(int appPort)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" });

        builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

        // Align HTTP JSON options with the IntegrationTestJsonContext (CamelCase naming):
        //  - PropertyNamingPolicy: Results.Ok serializes responses as camelCase so the
        //    test's source-generated TypeInfo (which expects camelCase keys) round-trips.
        //  - PropertyNameCaseInsensitive: request bodies from PostAsJsonAsync arrive as
        //    PascalCase (HttpClient default); case-insensitive binding accepts both forms.
        builder.Services.ConfigureHttpJsonOptions(opts =>
        {
            opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.SerializerOptions.PropertyNameCaseInsensitive = true;
        });

        builder.Services
            .AddDaprAgents(opt =>
            {
                // Register source-generated serialization context for typed responses.
                opt.AddContext(() => IntegrationTestJsonContext.Default);
            })
            // --- Agents for basic invocation tests ---
            .WithAgent(_ => new TestAIAgent("EchoAgent",
                _ => AgentRunResponseFactory.CreateWithText("Hello from EchoAgent!")))
            // --- Agent for typed-deserialization tests (returns valid CapitalAnswer JSON) ---
            .WithAgent(_ => new TestAIAgent("CapitalAgent",
                _ => AgentRunResponseFactory.CreateWithText(
                    """{"answer":"Paris","confidence":0.99}""")))
            // --- Additional agents for multiple-agent tests ---
            .WithAgent(_ => new TestAIAgent("GreetingAgent",
                _ => AgentRunResponseFactory.CreateWithText("Hello!")))
            .WithAgent(_ => new TestAIAgent("FarewellAgent",
                _ => AgentRunResponseFactory.CreateWithText("Goodbye!")))
            // --- Keyed agents: different chat-client keys (mirrors KeyedAgentInvokerDemo) ---
            .WithAgent("chat-key-alpha",
                _ => new TestAIAgent("AlphaAgent",
                    _ => AgentRunResponseFactory.CreateWithText("Alpha response")))
            .WithAgent("chat-key-beta",
                _ => new TestAIAgent("BetaAgent",
                    _ => AgentRunResponseFactory.CreateWithText("Beta response")));

        var app = builder.Build();

        // POST /ask  –  raw text response (mirrors AgentInvokerDemo /ask endpoint)
        app.MapPost("/ask", async (IDaprAgentInvoker invoker, AskRequest req, CancellationToken ct) =>
        {
            var agentName = req.AgentName ?? "EchoAgent";
            var agent     = invoker.GetAgent(agentName);
            var response  = await invoker.RunAgentAsync(agent, req.Prompt, cancellationToken: ct);
            return Results.Ok(new AskResponse(response.Text ?? string.Empty));
        });

        // POST /ask-typed  –  JSON agent response deserialized to CapitalAnswer
        app.MapPost("/ask-typed", async (IDaprAgentInvoker invoker, AskRequest req, CancellationToken ct) =>
        {
            var agent  = invoker.GetAgent("CapitalAgent");
            var result = await invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
                agent, message: req.Prompt, cancellationToken: ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

        return app;
    }
}
