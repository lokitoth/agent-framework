// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides an abstract base class for real-time agents that delegate
/// operations to an inner <see cref="RealtimeAgent"/> instance while
/// allowing for extensibility and customization (the decorator pattern).
/// </summary>
/// <remarks>
/// Mirrors <see cref="DelegatingAIAgent"/> for the realtime surface. The
/// default implementation provides transparent pass-through behavior;
/// derived classes can override specific members to layer in logging,
/// telemetry, function invocation, history projection, etc.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public abstract class DelegatingRealtimeAgent : RealtimeAgent
{
    /// <summary>Initializes a new instance of the <see cref="DelegatingRealtimeAgent"/> class.</summary>
    /// <param name="innerAgent">The underlying agent that handles core operations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerAgent"/> is <see langword="null"/>.</exception>
    protected DelegatingRealtimeAgent(RealtimeAgent innerAgent)
    {
        this.InnerAgent = innerAgent ?? throw new ArgumentNullException(nameof(innerAgent));
    }

    /// <summary>Gets the inner agent instance that receives delegated operations.</summary>
    protected RealtimeAgent InnerAgent { get; }

    /// <inheritdoc />
    protected override string? IdCore => this.InnerAgent.Id;

    /// <inheritdoc />
    public override string? Name => this.InnerAgent.Name;

    /// <inheritdoc />
    public override string? Description => this.InnerAgent.Description;

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        // If the key is non-null we don't know what it means; pass through to the inner agent.
        return
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this :
            this.InnerAgent.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    protected override ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
        => this.InnerAgent.ConnectSessionAsync(cancellationToken);
}
