// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A <see cref="DelegatingRealtimeAgent"/> whose connect-time behavior is
/// supplied via a delegate. Mirrors <see cref="AnonymousDelegatingAIAgent"/>
/// for the realtime surface and is the type produced by
/// <see cref="RealtimeAgentBuilderAnonymousExtensions.Use(RealtimeAgentBuilder, Func{RealtimeAgent, CancellationToken, ValueTask{RealtimeSession}})"/>.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class AnonymousDelegatingRealtimeAgent : DelegatingRealtimeAgent
{
    private readonly Func<RealtimeAgent, CancellationToken, ValueTask<RealtimeSession>> _connectFunc;

    /// <summary>Initializes a new instance of the <see cref="AnonymousDelegatingRealtimeAgent"/> class.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="connectFunc">A delegate that implements the connect operation; receives the inner agent and the cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerAgent"/> or <paramref name="connectFunc"/> is <see langword="null"/>.</exception>
    public AnonymousDelegatingRealtimeAgent(
        RealtimeAgent innerAgent,
        Func<RealtimeAgent, CancellationToken, ValueTask<RealtimeSession>> connectFunc)
        : base(innerAgent)
    {
        this._connectFunc = connectFunc ?? throw new ArgumentNullException(nameof(connectFunc));
    }

    /// <inheritdoc/>
    protected override ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
        => this._connectFunc(this.InnerAgent, cancellationToken);
}
