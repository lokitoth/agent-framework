// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime;

/// <summary>
/// Abstracts the inbound HTTP/SSE request and outbound SSE response sink
/// for the Invocations transport. Lifted out of the handler so unit tests
/// can drive the request/response lifecycle directly without standing up
/// a TestServer.
/// </summary>
[Experimental("MEAIREALTIME001")]
public interface IInvocationsRequestSink
{
    /// <summary>The agent_session_id query parameter, if provided.</summary>
    string? SessionId { get; }

    /// <summary>The X-Invocation-Id (or analogous) header, if provided.</summary>
    string? InvocationId { get; }

    /// <summary>
    /// Reads the next client message from the request, or returns
    /// <see langword="null"/> when the request body is exhausted.
    /// </summary>
    Task<RealtimeClientMessage?> ReadNextClientMessageAsync(CancellationToken cancellationToken);

    /// <summary>Writes an SSE frame (already including the trailing blank line) to the response.</summary>
    Task WriteSseFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);

    /// <summary>Marks the response as complete; future writes throw.</summary>
    Task CompleteAsync(CancellationToken cancellationToken);
}
