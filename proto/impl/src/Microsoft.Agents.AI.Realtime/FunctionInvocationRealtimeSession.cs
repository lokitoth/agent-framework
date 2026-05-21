// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A <see cref="RealtimeSession"/> implementation that wraps a function-invoking
/// <see cref="IRealtimeClientSession"/> produced by
/// <see cref="FunctionInvokingRealtimeClient"/>, while preserving the AF-side
/// projection state (history, ConversationId, StateBag) of the underlying
/// provider <see cref="RealtimeSession"/>.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class FunctionInvocationRealtimeSession : RealtimeSession
{
    private readonly RealtimeSession _providerSession;

    public FunctionInvocationRealtimeSession(IRealtimeClientSession invokingSession, RealtimeSession providerSession)
        : base(invokingSession, providerSession?.StateBag)
    {
        this._providerSession = providerSession ?? throw new ArgumentNullException(nameof(providerSession));
    }

    public override RealtimeSessionOptions? Options => this._providerSession.Options;

    public override string? ConversationId => this._providerSession.ConversationId;

    public override Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this.InnerSession.SendAsync(message, cancellationToken);
    }

    public override IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => this.InnerSession.GetStreamingResponseAsync(cancellationToken);

    public override async ValueTask DisposeAsync()
    {
        await this.InnerSession.DisposeAsync().ConfigureAwait(false);
        // _providerSession owns the underlying transport which the InnerSession also references;
        // disposing the function-invoking session is sufficient to release wire resources.
    }
}
