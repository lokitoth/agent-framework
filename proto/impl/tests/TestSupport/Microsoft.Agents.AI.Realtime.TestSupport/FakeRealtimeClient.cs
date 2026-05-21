// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.TestSupport;

/// <summary>
/// In-memory <see cref="IRealtimeClient"/> used by realtime unit tests.
/// </summary>
/// <remarks>
/// Each call to <see cref="CreateSessionAsync"/> hands out a new
/// <see cref="FakeRealtimeClientSession"/> and records it in <see cref="CreatedSessions"/>.
/// Test code can either set <see cref="SessionFactory"/> before calling, or
/// reach the returned sessions via <see cref="CreatedSessions"/> after the
/// call completes.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class FakeRealtimeClient : IRealtimeClient
{
    private readonly ConcurrentQueue<FakeRealtimeClientSession> _sessions = new();
    private int _disposed;

    /// <summary>
    /// Gets or sets a callback that produces a session for a given
    /// <see cref="RealtimeSessionOptions"/> instance. When <see langword="null"/>,
    /// a default <see cref="FakeRealtimeClientSession"/> is created.
    /// </summary>
    public Func<RealtimeSessionOptions?, FakeRealtimeClientSession>? SessionFactory { get; set; }

    /// <summary>Gets all sessions handed out by this client, in creation order.</summary>
    public IReadOnlyCollection<FakeRealtimeClientSession> CreatedSessions => this._sessions;

    /// <summary>Gets a value indicating whether <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed => Volatile.Read(ref this._disposed) != 0;

    /// <inheritdoc />
    public Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        FakeRealtimeClientSession session = this.SessionFactory?.Invoke(options) ?? new FakeRealtimeClientSession(options);
        this._sessions.Enqueue(session);
        return Task.FromResult<IRealtimeClientSession>(session);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref this._disposed, 1);
    }

    private void ThrowIfDisposed()
    {
        if (this.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FakeRealtimeClient));
        }
    }
}
