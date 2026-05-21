// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating <see cref="RealtimeSession"/> that projects inbound server
/// messages onto the <see cref="RealtimeSession.History"/> collection per
/// ADR-004. Conversation items completed via
/// <see cref="ResponseOutputItemRealtimeServerMessage"/> with
/// <see cref="RealtimeServerMessageType.ResponseOutputItemDone"/> are appended
/// to the history; items completed via
/// <see cref="RealtimeServerMessageType.ConversationItemDone"/> are also
/// captured for providers that surface user-side items separately.
/// </summary>
/// <remarks>
/// Interruption events (<see cref="InterruptedRealtimeServerMessage"/>) are
/// passed through but do not mutate the history; consumers can pattern-match
/// on the in-stream event to scrub local audio buffers.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public class HistoryProjectingRealtimeSession : DelegatingRealtimeSession
{
    /// <summary>Initializes a new instance of the <see cref="HistoryProjectingRealtimeSession"/> class.</summary>
    /// <param name="innerRealtimeSession">The wrapped session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerRealtimeSession"/> is <see langword="null"/>.</exception>
    public HistoryProjectingRealtimeSession(RealtimeSession innerRealtimeSession)
        : base(innerRealtimeSession)
    {
    }

    /// <inheritdoc/>
    public override IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => this.ProjectAsync(base.GetStreamingResponseAsync(cancellationToken), cancellationToken);

    private async IAsyncEnumerable<RealtimeServerMessage> ProjectAsync(
        IAsyncEnumerable<RealtimeServerMessage> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (RealtimeServerMessage message in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            this.OnServerMessage(message);
            yield return message;
        }
    }

    /// <summary>
    /// Called for each inbound server message. Default behavior appends
    /// "done" conversation items to <see cref="RealtimeSession.History"/>.
    /// </summary>
    /// <param name="message">The incoming server message.</param>
    protected virtual void OnServerMessage(RealtimeServerMessage message)
    {
        if (message is ResponseOutputItemRealtimeServerMessage outputItem
            && outputItem.Type == RealtimeServerMessageType.ResponseOutputItemDone
            && outputItem.Item is { } responseItem)
        {
            this.AddHistoryItem(responseItem);
            return;
        }

        if (message.Type == RealtimeServerMessageType.ConversationItemDone
            && TryExtractItem(message) is { } conversationItem)
        {
            this.AddHistoryItem(conversationItem);
        }
    }

    private static RealtimeConversationItem? TryExtractItem(RealtimeServerMessage message)
        => message switch
        {
            ResponseOutputItemRealtimeServerMessage outputItem => outputItem.Item,
            _ => null,
        };
}
