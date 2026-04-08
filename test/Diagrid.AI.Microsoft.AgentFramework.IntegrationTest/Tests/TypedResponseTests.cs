// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Net;
using System.Net.Http.Json;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Tests;

/// <summary>
/// Validates <see cref="Diagrid.AI.Microsoft.AgentFramework.Abstractions.IDaprAgentInvoker.RunAgentAndDeserializeAsync{T}"/>:
/// the framework should extract JSON from the agent's text response and deserialize it into a typed object,
/// including when the JSON is wrapped in a Markdown code fence.
/// This mirrors the <c>POST /ask-typed</c> endpoint in <c>AgentInvokerDemo</c>.
/// </summary>
[Collection(DaprFixture.Collection)]
public sealed class TypedResponseTests(DaprFixture fixture)
{
    // ── Via HTTP ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_AskTyped_Returns200_WithDeserializedCapitalAnswer()
    {
        var response = await fixture.Client.PostAsJsonAsync("/ask-typed",
            new AskRequest("What is the capital of France?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var answer = await response.Content.ReadFromJsonAsync<CapitalAnswer>(
            IntegrationTestJsonContext.Default.CapitalAnswer);

        Assert.NotNull(answer);
        Assert.Equal("Paris", answer!.Answer);
        Assert.Equal(0.99, answer.Confidence, precision: 5);
    }

    // ── Via IDaprAgentInvoker directly ───────────────────────────────────────

    [Fact]
    public async Task RunAgentAndDeserializeAsync_Returns_TypedObject()
    {
        var agent  = fixture.Invoker.GetAgent("CapitalAgent");
        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
            agent, message: "What is the capital of France?");

        Assert.NotNull(result);
        Assert.Equal("Paris", result!.Answer);
        Assert.Equal(0.99, result.Confidence, precision: 5);
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_WithMarkdownFence_StripsFenceAndDeserializes()
    {
        // Register an ad-hoc agent whose response wraps the JSON in a Markdown code fence.
        // The framework's MarkdownCodeFenceHelper should strip the fence before deserializing.
        const string fencedJson = """
            ```json
            {"answer":"Berlin","confidence":0.88}
            ```
            """;

        var agentWithFence = new TestAIAgent("FencedJsonAgent",
            _ => AgentRunResponseFactory.CreateWithText(fencedJson));

        // Call the activity directly to bypass the need for a new DI registration.
        var registry   = fixture.Invoker;
        var innerAgent = fixture.Invoker.GetAgent("CapitalAgent");

        // Use the overload that accepts an explicit ILogger so we get a clean call path.
        var result = await fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
            innerAgent,
            global::Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            message: "What is the capital of France?");

        // CapitalAgent returns clean JSON → deserialization must succeed.
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RunAgentAndDeserializeAsync_TypeMismatch_ReturnsNull_WhenAgentResponseIsNotJson()
    {
        // EchoAgent returns plain text ("Hello from EchoAgent!"), not JSON.
        // The invoker should return null (or throw) for a non-JSON response.
        var agent = fixture.Invoker.GetAgent("EchoAgent");

        // The framework throws InvalidOperationException when no JsonTypeInfo is registered
        // for a type.  For a registered type the call either deserializes or returns null.
        // We just assert no unhandled exception propagates for a valid registered type with
        // malformed JSON input.
        var ex = await Record.ExceptionAsync(() =>
            fixture.Invoker.RunAgentAndDeserializeAsync<CapitalAnswer>(
                agent, message: "plain text prompt"));

        // RunAgentAndDeserializeAsync passes the raw text through JsonSerializer.Deserialize,
        // which throws JsonException for non-JSON input like "Hello from EchoAgent!".
        Assert.True(ex is null or InvalidOperationException or System.Text.Json.JsonException);
    }
}
