// Copyright (c) 2026-present Diagrid Inc
//
// Licensed under the Business Source License 1.1 (BSL 1.1).
// You may not use this file except in compliance with the License.
//
// The full license terms, including the Additional Use Grant,
// are available in the LICENSE.md file at the root of this repository.
//
// Change Date: March 1, 2030
// On the Change Date, this software will be available under
// the Apache License, Version 2.0.

using Microsoft.Extensions.AI;

namespace Diagrid.AI.Microsoft.AgentFramework.Abstractions;

/// <summary>
/// Decides whether a tool call that requires human approval — i.e. one whose resolved
/// <see cref="AIFunction"/> is a <see cref="ApprovalRequiredAIFunction"/>,
/// such as an MAF skill script when <c>AgentSkillsProviderOptions.ScriptApproval</c> is enabled —
/// is allowed to run.
/// </summary>
/// <remarks>
/// Implementations run inside a Dapr Workflow activity (<c>ExecuteToolActivity</c>), which may run
/// for an extended period without violating workflow determinism (only the orchestrator function
/// itself must be deterministic and quick — activities may await long-running I/O). This makes it a
/// suitable place to await a real human decision, e.g. by polling a data store or ticketing system
/// that a webhook/UI updates out of band.
/// <para/>
/// No implementation is registered by default that approves calls automatically — register one via
/// <c>services.AddSingleton&lt;IToolApprovalHandler, YourHandler&gt;()</c> <em>before</em> calling
/// <c>AddDaprAgents()</c> to opt in to running approval-required tools. Without one, such calls are
/// denied.
/// </remarks>
public interface IToolApprovalHandler
{
    /// <summary>
    /// Requests approval for the described tool call.
    /// </summary>
    /// <param name="request">Describes the pending tool call.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>The approval decision.</returns>
    Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a tool call that is pending human approval.
/// </summary>
/// <param name="AgentName">The name of the agent that requested the call.</param>
/// <param name="ToolName">The name of the tool/function being called.</param>
/// <param name="CallId">The unique identifier for this call.</param>
/// <param name="ArgumentsJson">JSON-encoded arguments the LLM supplied for the call.</param>
public sealed record ToolApprovalRequest(
    string AgentName,
    string ToolName,
    string CallId,
    string ArgumentsJson);

/// <summary>
/// The outcome of a <see cref="IToolApprovalHandler.RequestApprovalAsync"/> call.
/// </summary>
/// <param name="Approved"><c>true</c> if the tool call is allowed to proceed.</param>
/// <param name="Reason">An optional human-readable reason, surfaced back to the LLM when denied.</param>
public sealed record ToolApprovalDecision(bool Approved, string? Reason = null)
{
    /// <summary>Creates an approving decision.</summary>
    public static ToolApprovalDecision Approve(string? reason = null) => new(true, reason);

    /// <summary>Creates a denying decision.</summary>
    public static ToolApprovalDecision Deny(string? reason = null) => new(false, reason);
}

/// <summary>
/// Safe default <see cref="IToolApprovalHandler"/>: denies every approval-required tool call. Since
/// such tools typically run arbitrary skill-bundled scripts, failing closed until a host explicitly
/// registers a real handler is the responsible default.
/// </summary>
internal sealed class DenyingToolApprovalHandler : IToolApprovalHandler
{
    /// <inheritdoc />
    public Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolApprovalDecision.Deny(
            "No IToolApprovalHandler is registered to approve tool calls. Register one via " +
            "services.AddSingleton<IToolApprovalHandler, YourHandler>() before calling AddDaprAgents() " +
            "to allow this tool to run."));
}
