// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Realtime.Hosting;

/// <summary>
/// Wraps an inner <see cref="RealtimeAgent"/> with hosted-side, per-connection
/// metadata. Per plan §5.1 / §3.4, the wrapper carries <strong>no</strong>
/// session store or registry — its only job is to attach the hosted session
/// context to each connect call.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class HostedRealtimeAgent : DelegatingRealtimeAgent
{
    /// <summary>Initializes a new instance.</summary>
    public HostedRealtimeAgent(RealtimeAgent innerAgent, HostedRealtimeSessionContext context)
        : base(innerAgent)
    {
        this.Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Gets the hosted session context attached to this agent.</summary>
    public HostedRealtimeSessionContext Context { get; }

    /// <inheritdoc />
    protected override ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
        => this.InnerAgent.ConnectSessionAsync(cancellationToken);

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && serviceType.IsInstanceOfType(this.Context))
        {
            return this.Context;
        }

        return base.GetService(serviceType, serviceKey);
    }
}
