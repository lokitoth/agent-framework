// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides a base <see cref="RealtimeSession"/> that forwards operations to
/// an inner <see cref="RealtimeSession"/>. Decorators (logging, telemetry,
/// history projection) derive from this class.
/// </summary>
/// <remarks>
/// The base <see cref="RealtimeSession.InnerSession"/> slot is initialized
/// from the wrapped session's <see cref="IRealtimeClientSession"/>
/// (resolved via <see cref="RealtimeSession.GetService{TService}(object?)"/>),
/// but all wire-level operations are routed through <see cref="InnerRealtimeSession"/>
/// so AF-side projection state (history, ConversationId) on the wrapped session is
/// preserved.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public abstract class DelegatingRealtimeSession : RealtimeSession
{
    /// <summary>Initializes a new instance of the <see cref="DelegatingRealtimeSession"/> class.</summary>
    /// <param name="innerRealtimeSession">The wrapped <see cref="RealtimeSession"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerRealtimeSession"/> is <see langword="null"/>.</exception>
    protected DelegatingRealtimeSession(RealtimeSession innerRealtimeSession)
        : base(
            (innerRealtimeSession ?? throw new ArgumentNullException(nameof(innerRealtimeSession)))
                .GetService<IRealtimeClientSession>()
                ?? throw new ArgumentException(
                    "Inner RealtimeSession does not expose an IRealtimeClientSession.",
                    nameof(innerRealtimeSession)),
            innerRealtimeSession.StateBag)
    {
        this.InnerRealtimeSession = innerRealtimeSession;
    }

    /// <summary>Gets the wrapped <see cref="RealtimeSession"/>.</summary>
    protected RealtimeSession InnerRealtimeSession { get; }

    /// <inheritdoc/>
    public override RealtimeSessionOptions? Options => this.InnerRealtimeSession.Options;

    /// <inheritdoc/>
    public override string? ConversationId => this.InnerRealtimeSession.ConversationId;

    /// <inheritdoc/>
    public override Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this.InnerRealtimeSession.SendAsync(message, cancellationToken);
    }

    /// <inheritdoc/>
    public override IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => this.InnerRealtimeSession.GetStreamingResponseAsync(cancellationToken);

    /// <inheritdoc/>
    public override ValueTask DisposeAsync() => this.InnerRealtimeSession.DisposeAsync();

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return this.InnerRealtimeSession.GetService(serviceType, serviceKey);
    }
}
