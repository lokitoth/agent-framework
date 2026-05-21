// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.TestSupport;

/// <summary>
/// Drains the streaming response of an <see cref="IRealtimeClientSession"/> (or
/// the AF-side <see cref="RealtimeSession"/>) into an in-memory list, with
/// helpers used by realtime unit tests.
/// </summary>
[Experimental("MEAIREALTIME001")]
public static class RealtimeServerMessageRecorder
{
    /// <summary>Drains all messages from <paramref name="source"/> until completion.</summary>
    public static async Task<IReadOnlyList<RealtimeServerMessage>> DrainAsync(
        IAsyncEnumerable<RealtimeServerMessage> source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<RealtimeServerMessage> list = new List<RealtimeServerMessage>();
        await foreach (RealtimeServerMessage msg in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(msg);
        }

        return list;
    }

    /// <summary>Drains <paramref name="source"/> and returns just the <see cref="RealtimeServerMessage.Type"/> values, in order.</summary>
    public static async Task<IReadOnlyList<RealtimeServerMessageType>> DrainTypesAsync(
        IAsyncEnumerable<RealtimeServerMessage> source,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RealtimeServerMessage> drained = await DrainAsync(source, cancellationToken).ConfigureAwait(false);
        return drained.Select(m => m.Type).ToArray();
    }
}

