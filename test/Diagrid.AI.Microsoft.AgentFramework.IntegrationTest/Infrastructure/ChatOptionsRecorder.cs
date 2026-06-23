// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).

using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.IntegrationTest.Infrastructure;

public sealed class ChatOptionsRecorder
{
    private readonly object _gate = new();
    private ChatOptionsSnapshot? _lastSnapshot;

    public ChatOptionsSnapshot? LastSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _lastSnapshot;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastSnapshot = null;
        }
    }

    public void Record(ChatOptions? options)
    {
        lock (_gate)
        {
            _lastSnapshot = new ChatOptionsSnapshot(
                HasOptions: options is not null,
                AllowBackgroundResponses: options?.AllowBackgroundResponses,
                ResponseFormat: options?.ResponseFormat,
                ToolCount: options?.Tools?.Count ?? 0);
        }
    }
}

public sealed record ChatOptionsSnapshot(
    bool HasOptions,
    bool? AllowBackgroundResponses,
    ChatResponseFormat? ResponseFormat,
    int ToolCount);
