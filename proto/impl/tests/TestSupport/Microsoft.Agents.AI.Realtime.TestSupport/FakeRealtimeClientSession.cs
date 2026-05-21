// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.TestSupport;

/// <summary>
/// In-memory <see cref="IRealtimeClientSession"/> used by realtime unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Tests push canned <see cref="RealtimeServerMessage"/> instances into the inbound
/// channel via <see cref="Enqueue"/>; <see cref="GetStreamingResponseAsync"/>
/// drains them in order until <see cref="CompleteInbound"/> is called or the
/// session is disposed. Outbound <see cref="RealtimeClientMessage"/> sends are
/// captured in <see cref="SentMessages"/>.
/// </para>
/// <para>
/// Per ADR-002, <see cref="GetStreamingResponseAsync"/> can only be enumerated once.
/// A second call throws <see cref="InvalidOperationException"/> so tests can verify
/// the single-consumer contract.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class FakeRealtimeClientSession : IRealtimeClientSession
{
    private readonly Channel<RealtimeServerMessage> _inbound = Channel.CreateUnbounded<RealtimeServerMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly ConcurrentQueue<RealtimeClientMessage> _sent = new();
    private int _enumerationStarted;
    private int _disposed;

    /// <summary>Initializes a new instance with optional pre-set options.</summary>
    public FakeRealtimeClientSession(RealtimeSessionOptions? options = null)
    {
        this.Options = options;
    }

    /// <inheritdoc />
    public RealtimeSessionOptions? Options { get; private set; }

    /// <summary>Gets the messages sent by the consumer, in send order.</summary>
    public IReadOnlyCollection<RealtimeClientMessage> SentMessages => this._sent;

    /// <summary>
    /// Gets a value indicating whether <see cref="DisposeAsync"/> has been called.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref this._disposed) != 0;

    /// <summary>Adds <paramref name="message"/> to the inbound queue.</summary>
    public ValueTask Enqueue(RealtimeServerMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this._inbound.Writer.WriteAsync(message, cancellationToken);
    }

    /// <summary>Marks the inbound queue as completed; pending readers see end-of-stream.</summary>
    public void CompleteInbound() => this._inbound.Writer.TryComplete();

    /// <summary>
    /// Sets the session options as if the server had acknowledged a
    /// <see cref="SessionUpdateRealtimeClientMessage"/>.
    /// </summary>
    public void SetOptions(RealtimeSessionOptions? options) => this.Options = options;

    /// <inheritdoc />
    public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        this.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        this._sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref this._enumerationStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(GetStreamingResponseAsync)} can only be enumerated once per session.");
        }

        await foreach (RealtimeServerMessage msg in this._inbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return msg;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 0)
        {
            this._inbound.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (this.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FakeRealtimeClientSession));
        }
    }
}
