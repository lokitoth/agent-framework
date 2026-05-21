// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Foundry;

/// <summary>
/// <see cref="IRealtimeClientSession"/> implementation over the Azure
/// VoiceLive WebSocket wire. Reads JSON text frames and projects them into
/// the AF normalized <see cref="RealtimeServerMessage"/> hierarchy; writes
/// AF <see cref="RealtimeClientMessage"/> instances as JSON text frames.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class FoundryRealtimeClientSession : IRealtimeClientSession
{
    private readonly IWebSocketTransport _transport;
    private int _enumerationStarted;
    private int _disposed;

    internal FoundryRealtimeClientSession(IWebSocketTransport transport, RealtimeSessionOptions? options)
    {
        this._transport = transport;
        this.Options = options;
    }

    public RealtimeSessionOptions? Options { get; }

    public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        this.ThrowIfDisposed();

        string json = FoundryClientMessageEncoder.Encode(message);
        await this._transport.SendTextAsync(json, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref this._enumerationStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(GetStreamingResponseAsync)} can only be enumerated once per session.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? frame = await this._transport.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                yield break;
            }

            RealtimeServerMessage? projected = FoundryEventProjector.Project(frame);
            if (projected is not null)
            {
                yield return projected;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 0)
        {
            await this._transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this._disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(FoundryRealtimeClientSession));
        }
    }
}
