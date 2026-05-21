// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// An in-memory audio pipe that an application can use to push captured
/// microphone bytes toward a realtime session. The pipe is decoupled from the
/// session so callers can buffer and back-pressure independently.
/// </summary>
/// <remarks>
/// <para>
/// The pipe is single-producer, single-consumer; concurrent writers are not
/// supported. Producers call <see cref="WriteAsync"/> with PCM (or provider-
/// native) byte chunks; consumers call <see cref="ReadAsync"/> or
/// <see cref="PumpToAsync"/> to forward chunks to a <see cref="RealtimeSession"/>.
/// </para>
/// <para>
/// The pipe is bounded by default to avoid unbounded buffering when a slow
/// consumer cannot keep up; <see cref="Complete"/> closes the writer side.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class RealtimeAudioPipe : IDisposable
{
    private readonly Channel<DataContent> _channel;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="RealtimeAudioPipe"/> class.</summary>
    /// <param name="capacity">The maximum number of buffered chunks. Defaults to 32.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public RealtimeAudioPipe(int capacity = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this._channel = Channel.CreateBounded<DataContent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>Gets the writer-side <see cref="RealtimeAudioWriter"/>.</summary>
    public RealtimeAudioWriter Writer => new(this);

    /// <summary>Writes an audio chunk to the pipe.</summary>
    /// <param name="chunk">The audio chunk to write.</param>
    /// <param name="cancellationToken">A token to cancel the wait when the pipe is full.</param>
    /// <returns>A task that completes when the chunk has been accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chunk"/> is <see langword="null"/>.</exception>
    public ValueTask WriteAsync(DataContent chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return this._channel.Writer.WriteAsync(chunk, cancellationToken);
    }

    /// <summary>Reads the next audio chunk; blocks until one is available or the pipe completes.</summary>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The next chunk, or <see langword="null"/> when the pipe is complete and drained.</returns>
    public async ValueTask<DataContent?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (await this._channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return this._channel.Reader.TryRead(out DataContent? chunk) ? chunk : null;
        }

        return null;
    }

    /// <summary>
    /// Pumps audio chunks from this pipe into <paramref name="session"/> by
    /// repeatedly calling <see cref="RealtimeSession.AppendInputAudioAsync"/>.
    /// Returns when the pipe is completed and drained or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="session">The destination session.</param>
    /// <param name="cancellationToken">A token to stop the pump.</param>
    /// <returns>A task that completes when the pump finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public async Task PumpToAsync(RealtimeSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await foreach (DataContent chunk in this._channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await session.AppendInputAudioAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Marks the writer side complete; readers drain remaining chunks.</summary>
    public void Complete() => this._channel.Writer.TryComplete();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 0)
        {
            this._channel.Writer.TryComplete();
        }
    }
}

/// <summary>
/// A lightweight writer view over a <see cref="RealtimeAudioPipe"/>.
/// </summary>
[Experimental("MEAIREALTIME001")]
public readonly struct RealtimeAudioWriter
{
    private readonly RealtimeAudioPipe _pipe;

    internal RealtimeAudioWriter(RealtimeAudioPipe pipe)
    {
        this._pipe = pipe;
    }

    /// <summary>Writes an audio chunk to the underlying pipe.</summary>
    /// <param name="chunk">The audio chunk to write.</param>
    /// <param name="cancellationToken">A token to cancel the wait when the pipe is full.</param>
    /// <returns>A task that completes when the chunk has been accepted.</returns>
    public ValueTask WriteAsync(DataContent chunk, CancellationToken cancellationToken = default)
        => this._pipe.WriteAsync(chunk, cancellationToken);

    /// <summary>Marks the writer side complete.</summary>
    public void Complete() => this._pipe.Complete();
}
