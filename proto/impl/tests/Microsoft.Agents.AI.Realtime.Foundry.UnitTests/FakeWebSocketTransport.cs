// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.Foundry;

namespace Microsoft.Agents.AI.Realtime.Foundry.UnitTests;

[Experimental("MEAIREALTIME001")]
internal sealed class FakeWebSocketTransport : IWebSocketTransport
{
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly ConcurrentQueue<string> _sent = new();
    private int _connected;
    private int _disposed;

    public Uri? ConnectedEndpoint { get; private set; }

    public bool IsDisposed => Volatile.Read(ref this._disposed) != 0;

    public IReadOnlyCollection<string> SentFrames => this._sent;

    public ValueTask EnqueueInbound(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return this._inbound.Writer.WriteAsync(frame);
    }

    public void CompleteInbound() => this._inbound.Writer.TryComplete();

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Interlocked.Exchange(ref this._connected, 1) != 0)
        {
            throw new InvalidOperationException("Already connected.");
        }

        this.ConnectedEndpoint = endpoint;
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();
        this._sent.Enqueue(payload);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await this._inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 0)
        {
            this._inbound.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
