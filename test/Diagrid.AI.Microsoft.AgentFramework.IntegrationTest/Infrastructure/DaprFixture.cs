// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Dapr.Testcontainers.Common;
using Dapr.Testcontainers.Common.Options;
using Dapr.Testcontainers.Harnesses;
using Dapr.StateManagement;
using Dapr.Workflow;
using Diagrid.AI.Microsoft.AgentFramework.Abstractions;
using Diagrid.AI.Microsoft.AgentFramework.Conversations;
using Diagrid.AI.Microsoft.AgentFramework.Hosting;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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

    /// <summary>
    /// <see cref="DaprWorkflowClient"/> for scheduling and querying custom workflows directly.
    /// </summary>
    public DaprWorkflowClient WorkflowClient { get; private set; } = null!;

    /// <summary>
    /// <see cref="IDaprAgentContextAccessor"/> singleton — exposes the ambient context set
    /// by <c>InvokeAgentActivity</c> while an agent is executing.
    /// </summary>
    public IDaprAgentContextAccessor ContextAccessor { get; private set; } = null!;

    /// <summary>
    /// Tracks the number of times the <c>process_input</c> tool has been invoked by
    /// <c>ToolInvocationAgent</c> during the test run.
    /// </summary>
    public ToolInvocationTracker ToolTracker { get; } = new();

    /// <summary>
    /// Records the number of <see cref="Microsoft.Extensions.AI.ChatMessage"/> objects seen by
    /// <c>HistoryAgent</c> on each LLM call. Used by session tests to verify that conversation
    /// history from prior turns is injected into subsequent calls.
    /// </summary>
    public MessageCountRecorder HistoryRecorder { get; } = new();

    /// <summary>
    /// Records OpenTelemetry baggage visible during LLM and tool execution.
    /// </summary>
    public TelemetryBaggageRecorder TelemetryBaggageRecorder { get; } = new();

    /// <summary>
    /// Records chat options.
    /// </summary>
    public ChatOptionsRecorder OptionsRecorder { get; } = new();

    /// <summary>
    /// The Redis state store component the harness writes; it also backs the conversation store.
    /// </summary>
    public const string StateStoreName = Dapr.Testcontainers.Constants.DaprComponentNames.StateManagementComponentName;

    /// <summary>
    /// The key prefix the test app's conversation store uses.
    /// </summary>
    public const string ConversationKeyPrefix = "it-conversation:";

    /// <summary>
    /// The test app's conversation store (<c>WithConversationStore</c>).
    /// </summary>
    public IConversationStore ConversationStore { get; private set; } = null!;

    /// <summary>
    /// The test app's Dapr state client, for checks against the state store itself.
    /// </summary>
    public DaprStateManagementClient StateClient { get; private set; } = null!;

    /// <summary>An agent with its own ChatOptions and a WithReasoning effort, recording what reaches its chat client.</summary>
    public const string ReasoningOptionsProbeAgent = "ReasoningOptionsProbeAgent";

    /// <summary>
    /// The binary store component name used when <see cref="BinaryStorePayloads"/> is on.
    /// </summary>
    public const string BinaryStoreName = "agent-payloads";

    /// <summary>
    /// When <c>DAPR_BINARY_STORE_PAYLOADS=1</c>, the sidecar gets an in-memory binary store and the
    /// app offloads <em>every</em> workflow payload to it, so the whole suite runs against payload
    /// references instead of inline payloads. Needs a 1.19 or newer sidecar.
    /// </summary>
    public static bool BinaryStorePayloads { get; } =
        Environment.GetEnvironmentVariable("DAPR_BINARY_STORE_PAYLOADS") == "1";

    // ── IAsyncLifetime ────────────────────────────────────────────────────────

    public async ValueTask InitializeAsync()
    {
        var componentsDir = TestDirectoryManager.CreateTestDirectory("agent-components");
        var daprConfigPath = Path.Combine(componentsDir, "otel-tracing-config.yaml");
        await File.WriteAllTextAsync(
            daprConfigPath,
            """
            apiVersion: dapr.io/v1alpha1
            kind: Configuration
            metadata:
              name: otel-tracing
            spec:
              tracing:
                samplingRate: "1"
            """);

        if (BinaryStorePayloads)
        {
            await File.WriteAllTextAsync(
                Path.Combine(componentsDir, "binarystore.yaml"),
                $"""
                apiVersion: dapr.io/v1alpha1
                kind: Component
                metadata:
                  name: {BinaryStoreName}
                spec:
                  type: binarystore.in-memory
                  version: v1
                  metadata: []
                """);
        }

        // 1. Shared Dapr infrastructure: Docker network, Placement, Scheduler, and Redis
        //    (actor state store — required for Dapr Workflow execution).
        _environment = await DaprTestEnvironment.CreateWithPooledNetworkAsync(needsActorState: true);
        await _environment.StartAsync();

        // 2. WorkflowHarness writes the Redis state-store component YAML and starts daprd,
        //    wiring it to the shared Placement and Scheduler services.
        //    We pass startApp: null and manage the test app ourselves so we can wire up
        //    AddDaprAgents() after the sidecar ports are known.
        _harness = new DaprHarnessBuilder(componentsDir)
            .WithOptions(new DaprRuntimeOptions(Environment.GetEnvironmentVariable("DAPR_RUNTIME_VERSION") ?? "1.19.0"))
            .WithEnvironment(_environment)
            .BuildWorkflow();
        SetDaprConfigFilePath(_harness, "/components/otel-tracing-config.yaml");
        await _harness.InitializeAsync();

        // 3. Point the test process at the running sidecar so AddDaprAgents picks up the
        //    correct ports when building the WebApplication below.
        //    Both variables are read by the Dapr .NET SDK at client construction time.
        Environment.SetEnvironmentVariable("DAPR_HTTP_PORT", _harness.DaprHttpPort.ToString());
        Environment.SetEnvironmentVariable("DAPR_GRPC_PORT", _harness.DaprGrpcPort.ToString());

        // 4. Build and start the minimal test application on the port the sidecar already
        //    knows about (BaseHarness assigned it via PortUtilities.GetAvailablePort()).
        _app = BuildTestApp(_harness.AppPort, ToolTracker, HistoryRecorder, TelemetryBaggageRecorder, OptionsRecorder);
        await _app.StartAsync();

        Invoker         = _app.Services.GetRequiredService<IDaprAgentInvoker>();
        WorkflowClient  = _app.Services.GetRequiredService<DaprWorkflowClient>();
        ContextAccessor = _app.Services.GetRequiredService<IDaprAgentContextAccessor>();
        ConversationStore = _app.Services.GetRequiredService<IConversationStore>();
        StateClient = _app.Services.GetRequiredService<DaprStateManagementClient>();
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
    private static WebApplication BuildTestApp(
        int appPort,
        ToolInvocationTracker toolTracker,
        MessageCountRecorder historyRecorder,
        TelemetryBaggageRecorder telemetryBaggageRecorder,
        ChatOptionsRecorder optionsRecorder)
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

        var agents = builder.Services
            .AddDaprAgents(
                opt =>
                {
                    // Register source-generated serialization context for typed responses.
                    opt.AddContext(() => IntegrationTestJsonContext.Default);
                },
                reg =>
                {
                    // Custom workflows for WorkflowContextExtensions integration tests.
                    reg.RegisterWorkflow<EchoOrchestrationWorkflow>();
                    reg.RegisterWorkflow<DetachedEchoOrchestrationWorkflow>();
                    reg.RegisterWorkflow<ReasoningHistoryOrchestrationWorkflow>();
                    reg.RegisterWorkflow<ConversationReasoningOrchestrationWorkflow>();
                    reg.RegisterWorkflow<ConversationRetryOrchestrationWorkflow>();
                    reg.RegisterWorkflow<DeleteConversationOrchestrationWorkflow>();
                    reg.RegisterWorkflow<ConcurrentConversationOrchestrationWorkflow>();
                    reg.RegisterWorkflow<CompactingConversationOrchestrationWorkflow>();
                    reg.RegisterWorkflow<CapitalOrchestrationWorkflow>();
                    reg.RegisterWorkflow<KeyedOrchestrationWorkflow>();
                })
            // --- Agents for basic invocation tests ---
            .WithAgent(_ => new TextResponseMockChatClient("Hello from EchoAgent!")
                .AsAIAgent(instructions: "Echo agent", name: "EchoAgent"))
            // --- Agent for typed-deserialization tests (returns valid CapitalAnswer JSON) ---
            .WithAgent(_ => new TextResponseMockChatClient("""{"answer":"Paris","confidence":0.99}""")
                .AsAIAgent(instructions: "Capital agent", name: "CapitalAgent"))
            // --- Additional agents for multiple-agent tests ---
            .WithAgent(_ => new TextResponseMockChatClient("Hello!")
                .AsAIAgent(instructions: "Greeting agent", name: "GreetingAgent"))
            .WithAgent(_ => new TextResponseMockChatClient("Goodbye!")
                .AsAIAgent(instructions: "Farewell agent", name: "FarewellAgent"))
            .WithAgent(_ => new TextResponseMockChatClient("options-probe-response", optionsRecorder)
                .AsAIAgent(instructions: "Options probe agent", name: "OptionsProbeAgent"))
            // --- Reasoning options probe: its own ChatOptions plus a WithReasoning effort (below) ---
            .WithAgent(_ => new TextResponseMockChatClient("reasoning-probe-response", optionsRecorder)
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = ReasoningOptionsProbeAgent,
                    ChatOptions = new ChatOptions { Instructions = "Reasoning options probe agent", Temperature = 0.4f }
                }))
            .WithReasoning(ReasoningOptionsProbeAgent, ReasoningEffort.High)
            // --- Conversation store, in the workflow's own Redis state store ---
            .WithConversationStore(StateStoreName, options => options.KeyPrefix = ConversationKeyPrefix)
            // --- Compacting agent: summarizes its conversations once they pass a small trigger ---
            .WithAgent(_ => new CompactionMockChatClient()
                .AsAIAgent(instructions: "Compacting agent", name: CompactionMockChatClient.AgentName))
            .WithConversationCompaction(CompactionMockChatClient.AgentName, options =>
            {
                options.TriggerTokens = 50;
                options.KeepRecentTurns = 1;
            })
            // --- Keyed agents: different chat-client keys (mirrors KeyedAgentInvokerDemo) ---
            .WithAgent("chat-key-alpha",
                _ => new TextResponseMockChatClient("Alpha response")
                    .AsAIAgent(instructions: "Alpha agent", name: "AlphaAgent"))
            .WithAgent("chat-key-beta",
                _ => new TextResponseMockChatClient("Beta response")
                    .AsAIAgent(instructions: "Beta agent", name: "BetaAgent"))
            // --- Context accessor test agent: uses a tool to read the ambient context ---
            .WithAgent(sp =>
            {
                var accessor = sp.GetRequiredService<IDaprAgentContextAccessor>();
                var getContextTool = AIFunctionFactory.Create(
                    () =>
                    {
                        var instanceId = accessor.Current?.CurrentWorkflowInstanceId ?? "null";
                        return $"instanceId:{instanceId}";
                    },
                    name: ContextMockChatClient.ToolName,
                    description: "Gets the current workflow context information.");

                return new ContextMockChatClient()
                    .AsAIAgent(
                        instructions: "Context agent",
                        name: "ContextAgent",
                        tools: [getContextTool]);
            })
            // --- Tool invocation test agent: uses ToolCallMockChatClient + a real AIFunction tool ---
            .WithAgent(_ =>
            {
                var processInputTool = AIFunctionFactory.Create(
                    (string input) =>
                    {
                        toolTracker.RecordInvocation();
                        return $"processed:{input}";
                    },
                    name: ToolCallMockChatClient.ToolName,
                    description: "Processes the given input value and returns a result string.");

                return new ToolCallMockChatClient()
                    .AsAIAgent(
                        instructions: "You are a test agent that calls tools.",
                        name: "ToolInvocationAgent",
                        tools: [processInputTool]);
            })
            // --- Reasoning test agent: returns reasoning and reports which earlier reasoning it received ---
            .WithAgent(_ => new ReasoningMockChatClient()
                .AsAIAgent(
                    instructions: "Reasoning agent",
                    name: ReasoningMockChatClient.AgentName,
                    tools: [AIFunctionFactory.Create(() => "noted", ReasoningMockChatClient.ToolName, "Takes a note.")]))
            .WithAgent(_ => new FailOnceChatClient()
                .AsAIAgent(
                    instructions: "Reasoning agent",
                    name: FailOnceChatClient.AgentName,
                    tools: [AIFunctionFactory.Create(() => "noted", ReasoningMockChatClient.ToolName, "Takes a note.")]))
            // --- Session history test agent: records the message count seen on each LLM call ---
            .WithAgent(_ => new MessageCountMockChatClient(historyRecorder)
                .AsAIAgent(instructions: "History tracking agent", name: "HistoryAgent"))
            // --- Telemetry baggage test agent: records Activity.Current baggage from the LLM and tool activities ---
            .WithAgent(_ =>
            {
                var recordBaggageTool = AIFunctionFactory.Create(
                    () =>
                    {
                        telemetryBaggageRecorder.RecordTool(TelemetryBaggageMockChatClient.GetCurrentBaggage());
                        return "recorded";
                    },
                    name: TelemetryBaggageMockChatClient.ToolName,
                    description: "Records the current OpenTelemetry baggage.");

                return new TelemetryBaggageMockChatClient(telemetryBaggageRecorder)
                    .AsAIAgent(
                        instructions: "Telemetry baggage agent",
                        name: TelemetryBaggageMockChatClient.AgentName,
                        tools: [recordBaggageTool]);
            });

        if (BinaryStorePayloads)
        {
            // A null threshold offloads every payload, however small.
            agents.WithBinaryStorePayloads(BinaryStoreName, options => options.OffloadThreshold = null);
        }

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

    private static void SetDaprConfigFilePath(BaseHarness harness, string configFilePath)
    {
        var property = typeof(BaseHarness).GetProperty(
            "DaprConfigFilePath",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        if (property is null || property.SetMethod is null)
        {
            throw new InvalidOperationException(
                "Dapr.Testcontainers no longer exposes the DaprConfigFilePath harness hook required by these tests.");
        }

        property.SetValue(harness, configFilePath);
    }
}
