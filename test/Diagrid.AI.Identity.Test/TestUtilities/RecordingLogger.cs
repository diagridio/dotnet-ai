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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diagrid.AI.Identity.Test.TestUtilities;

/// <summary>
/// An <see cref="ILogger"/> that keeps every record it is handed, rendered exactly as a
/// provider would render it, so a test can assert on what an operator would read.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    private readonly List<LogRecord> _records = [];

    /// <summary>
    /// Gets a snapshot of the records the logger was handed, in order.
    /// </summary>
    internal IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_records)
            {
                return [.. _records];
            }
        }
    }

    /// <summary>
    /// Gets the rendered messages logged at <see cref="LogLevel.Warning"/>, in order.
    /// </summary>
    internal IReadOnlyList<string> Warnings =>
        [.. Records.Where(record => record.Level == LogLevel.Warning).Select(record => record.Message)];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => NullLogger.Instance.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (_records)
        {
            _records.Add(new LogRecord(logLevel, formatter(state, exception), exception));
        }
    }
}

/// <summary>
/// One record as a logging provider would receive it.
/// </summary>
/// <param name="Level">The level the record was logged at.</param>
/// <param name="Message">The rendered message.</param>
/// <param name="Exception">The logged exception, if any.</param>
internal sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Adapts a <see cref="RecordingLogger"/> to the generic logger the container resolves.
/// </summary>
/// <typeparam name="TCategory">The category the consumer asks for.</typeparam>
/// <param name="inner">The logger that keeps the records.</param>
internal sealed class RecordingLogger<TCategory>(RecordingLogger inner) : ILogger<TCategory>
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => inner.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        inner.Log(logLevel, eventId, state, exception, formatter);
}
