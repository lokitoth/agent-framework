// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Hosting;

/// <summary>
/// Per-connection context handed to an <see cref="IRealtimeAgentTransport"/>
/// implementation: the inbound stream of client messages, the outbound
/// emitter, and the hosted session metadata.
/// </summary>
[Experimental("MEAIREALTIME001")]
public interface IRealtimeAgentTransportContext
{
    /// <summary>Gets the hosted session context.</summary>
    HostedRealtimeSessionContext Session { get; }

    /// <summary>
    /// Reads the next inbound client message from the transport, or returns
    /// <see langword="null"/> when the peer has closed the connection.
    /// </summary>
    Task<RealtimeClientMessage?> ReceiveClientMessageAsync(CancellationToken cancellationToken);

    /// <summary>Writes an encoded server event back to the peer.</summary>
    Task WriteEventAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

/// <summary>
/// Encodes a stream of <see cref="RealtimeServerMessage"/>s into the wire
/// format the hosted transport expects to emit.
/// </summary>
[Experimental("MEAIREALTIME001")]
public interface IRealtimeEventEncoder
{
    /// <summary>Encodes <paramref name="message"/> into <paramref name="destination"/>.</summary>
    /// <returns>The number of bytes written.</returns>
    int Encode(RealtimeServerMessage message, Stream destination);
}

/// <summary>
/// Transport-side wire handler that pumps a hosted realtime agent's session
/// across a concrete transport (Invocations/SSE, WebSocket, etc.). Concrete
/// transports live in provider hosting packages (e.g.
/// <c>Microsoft.Agents.AI.Foundry.Hosting.Realtime</c>).
/// </summary>
[Experimental("MEAIREALTIME001")]
public interface IRealtimeAgentTransport
{
    /// <summary>
    /// Runs the transport: connect to the agent, pump client messages in and
    /// server events out, and return when the peer closes.
    /// </summary>
    Task RunAsync(RealtimeAgent agent, IRealtimeAgentTransportContext context, CancellationToken cancellationToken);
}
