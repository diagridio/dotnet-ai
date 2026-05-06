// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using System.Runtime.CompilerServices;
using System.Text.Json;
using Diagrid.AI.Microsoft.AgentFramework.Runtime;
using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Test.Runtime;

public sealed class ToolResultCompatibilityChatClientTests
{
    // ── RewriteFunctionResults (the internal static helper) ────────────────

    [Fact]
    public void RewriteFunctionResults_FunctionResultContent_BecomesTextContent()
    {
        var original = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call-1", (object?)"some result")
        ]);

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        var text = Assert.Single(rewritten.Contents.OfType<TextContent>());
        Assert.NotNull(text.Text);
    }

    [Fact]
    public void RewriteFunctionResults_FunctionResultContent_PreservesCallIdInAdditionalProperties()
    {
        var original = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call-abc", (object?)"42")
        ]);

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        var text = Assert.Single(rewritten.Contents.OfType<TextContent>());
        Assert.NotNull(text.AdditionalProperties);
        Assert.True(text.AdditionalProperties.TryGetValue(ToolResultCompatibilityChatClient.ToolCallIdKey, out var storedId));
        Assert.Equal("call-abc", storedId);
    }

    [Fact]
    public void RewriteFunctionResults_MultipleFunctionResults_AllRewritten()
    {
        var original = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call-1", (object?)"result-1"),
            new FunctionResultContent("call-2", (object?)"result-2")
        ]);

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        var texts = rewritten.Contents.OfType<TextContent>().ToList();
        Assert.Equal(2, texts.Count);

        Assert.Equal("call-1", texts[0].AdditionalProperties?[ToolResultCompatibilityChatClient.ToolCallIdKey]);
        Assert.Equal("call-2", texts[1].AdditionalProperties?[ToolResultCompatibilityChatClient.ToolCallIdKey]);
    }

    [Fact]
    public void RewriteFunctionResults_NonFunctionResultContent_PassedThrough()
    {
        var originalText = new TextContent("hello");
        var original = new ChatMessage(ChatRole.Tool,
        [
            originalText,
            new FunctionResultContent("call-1", (object?)"r")
        ]);

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        // The original TextContent instance should survive unchanged.
        Assert.Contains(rewritten.Contents, c => ReferenceEquals(c, originalText));
    }

    [Fact]
    public void RewriteFunctionResults_PreservesRole()
    {
        var original = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call-1", (object?)"r")
        ]);

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        Assert.Equal(ChatRole.Tool, rewritten.Role);
    }

    [Fact]
    public void RewriteFunctionResults_PreservesAuthorName()
    {
        var original = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call-1", (object?)"r")
        ])
        { AuthorName = "my-tool" };

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        Assert.Equal("my-tool", rewritten.AuthorName);
    }

    [Fact]
    public void RewriteFunctionResults_JsonElementResult_UsesRawText()
    {
        var json = JsonSerializer.SerializeToElement(new { value = 42 });
        var original = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call-1", json)
        ]);

        var rewritten = ToolResultCompatibilityChatClient.RewriteFunctionResults(original);

        var text = Assert.Single(rewritten.Contents.OfType<TextContent>());
        // Raw JSON text should be used (not ToString of the JsonElement wrapper).
        Assert.Equal(json.GetRawText(), text.Text);
    }

    // ── GetResponseAsync (full adapter flow) ──────────────────────────────

    [Fact]
    public async Task GetResponseAsync_WithFunctionResultContent_ForwardsRewrittenMessages()
    {
        List<ChatMessage>? capturedMessages = null;

        var inner = new CapturingChatClient(messages =>
        {
            capturedMessages = messages.ToList();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        });

        var adapter = new ToolResultCompatibilityChatClient(inner);

        var input = new List<ChatMessage>
        {
            new(ChatRole.User, "use the tool"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "my_tool")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", (object?)"\"tool-output\"")])
        };

        await adapter.GetResponseAsync(input);

        Assert.NotNull(capturedMessages);
        // Third message should be rewritten: role=Tool, TextContent, no FunctionResultContent.
        var toolMsg = capturedMessages![2];
        Assert.Equal(ChatRole.Tool, toolMsg.Role);
        Assert.DoesNotContain(toolMsg.Contents, c => c is FunctionResultContent);
        var textContent = Assert.Single(toolMsg.Contents.OfType<TextContent>());
        Assert.Equal("call-1", textContent.AdditionalProperties?[ToolResultCompatibilityChatClient.ToolCallIdKey]);
    }

    [Fact]
    public async Task GetResponseAsync_WithoutFunctionResultContent_MessagesPassedThrough()
    {
        List<ChatMessage>? capturedMessages = null;

        var inner = new CapturingChatClient(messages =>
        {
            capturedMessages = messages.ToList();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        });

        var adapter = new ToolResultCompatibilityChatClient(inner);

        var userMsg = new ChatMessage(ChatRole.User, "hello");
        await adapter.GetResponseAsync([userMsg]);

        Assert.NotNull(capturedMessages);
        Assert.Same(userMsg, capturedMessages![0]);
    }

    [Fact]
    public async Task GetResponseAsync_AssistantWithFunctionCallContent_NotRewritten()
    {
        List<ChatMessage>? capturedMessages = null;

        var inner = new CapturingChatClient(messages =>
        {
            capturedMessages = messages.ToList();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        });

        var adapter = new ToolResultCompatibilityChatClient(inner);

        // Only the assistant message with FunctionCallContent — no tool-result message.
        var assistantMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "my_tool")]);

        await adapter.GetResponseAsync([assistantMsg]);

        Assert.NotNull(capturedMessages);
        // Assistant message must NOT be rewritten — it carries FunctionCallContent, not FunctionResultContent.
        Assert.Same(assistantMsg, capturedMessages![0]);
        Assert.Single(capturedMessages![0].Contents.OfType<FunctionCallContent>());
    }

    // ── Helper: CapturingChatClient ────────────────────────────────────────

    private sealed class CapturingChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> handler) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(handler(messages));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
