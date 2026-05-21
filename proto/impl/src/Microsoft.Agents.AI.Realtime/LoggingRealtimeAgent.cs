// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating <see cref="RealtimeAgent"/> that logs connection lifecycle and
/// wire-level send/receive operations to an <see cref="ILogger"/>.
/// </summary>
/// <remarks>
/// <para>
/// At <see cref="LogLevel.Debug"/> only connection lifecycle and message-type
/// metadata is logged. At <see cref="LogLevel.Trace"/> the contents of
/// individual <see cref="Microsoft.Extensions.AI.RealtimeClientMessage"/> and
/// <see cref="Microsoft.Extensions.AI.RealtimeServerMessage"/> instances are
/// logged. Audio byte payloads are <em>always</em> redacted as
/// <c>Audio(length=N)</c> regardless of level (review §S1).
/// </para>
/// <para>
/// <see cref="LogLevel.Trace"/> is disabled by default and should never be
/// enabled in production environments.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class LoggingRealtimeAgent : DelegatingRealtimeAgent
{
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="LoggingRealtimeAgent"/> class.</summary>
    /// <param name="innerAgent">The underlying <see cref="RealtimeAgent"/>.</param>
    /// <param name="logger">The logger that receives the log entries.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerAgent"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public LoggingRealtimeAgent(RealtimeAgent innerAgent, ILogger logger)
        : base(innerAgent)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        if (this._logger.IsEnabled(LogLevel.Debug))
        {
            this._logger.LogDebug("Realtime agent {AgentId} connecting...", this.Id);
        }

        try
        {
            RealtimeSession session = await base.ConnectSessionCoreAsync(cancellationToken).ConfigureAwait(false);

            if (this._logger.IsEnabled(LogLevel.Debug))
            {
                this._logger.LogDebug("Realtime agent {AgentId} connected.", this.Id);
            }

            return new LoggingRealtimeSession(session, this._logger);
        }
        catch (OperationCanceledException)
        {
            this._logger.LogDebug("Realtime agent {AgentId} connect canceled.", this.Id);
            throw;
        }
        catch (Exception ex) when (LogConnectFailure(ex))
        {
            throw;
        }
    }

    private bool LogConnectFailure(Exception ex)
    {
        this._logger.LogError(ex, "Realtime agent {AgentId} connect failed.", this.Id);
        return false; // Don't catch -- propagate after logging.
    }

    internal static ILogger ResolveLogger(ILogger? explicitLogger, ILoggerFactory? factory)
        => explicitLogger
           ?? factory?.CreateLogger(typeof(LoggingRealtimeAgent))
           ?? NullLogger.Instance;
}
