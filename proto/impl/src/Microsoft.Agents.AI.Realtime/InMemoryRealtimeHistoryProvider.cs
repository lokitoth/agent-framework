// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// In-memory history provider for realtime sessions. Mirrors the role of
/// <c>InMemoryChatHistoryProvider</c> for the request/response surface.
/// </summary>
/// <remarks>
/// <para>
/// Each session-id is associated with an ordered list of
/// <see cref="RealtimeConversationItem"/> instances. This proto provider is
/// process-local; it has no durability or eviction beyond explicit
/// <see cref="Clear(string)"/> calls.
/// </para>
/// <para>
/// Per ADR-004 the projection logic itself lives in
/// <see cref="HistoryProjectingRealtimeSession"/>; this type is the in-memory
/// store consumed by consumers who want to retain history beyond a single
/// session instance.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class InMemoryRealtimeHistoryProvider
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<RealtimeConversationItem>> _store = [];

    /// <summary>Appends <paramref name="item"/> to the conversation identified by <paramref name="conversationId"/>.</summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="item">The item to append.</param>
    /// <exception cref="ArgumentException"><paramref name="conversationId"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public void Append(string conversationId, RealtimeConversationItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(item);

        lock (this._lock)
        {
            if (!this._store.TryGetValue(conversationId, out List<RealtimeConversationItem>? list))
            {
                list = [];
                this._store[conversationId] = list;
            }

            list.Add(item);
        }
    }

    /// <summary>Returns a snapshot of the history for <paramref name="conversationId"/>.</summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <returns>A snapshot list; empty if the conversation is unknown.</returns>
    /// <exception cref="ArgumentException"><paramref name="conversationId"/> is null or whitespace.</exception>
    public IReadOnlyList<RealtimeConversationItem> GetHistory(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        lock (this._lock)
        {
            return this._store.TryGetValue(conversationId, out List<RealtimeConversationItem>? list)
                  ? [.. list]
                  : [];
        }
    }

    /// <summary>Removes the history for <paramref name="conversationId"/>, if any.</summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <returns><see langword="true"/> if a history was present and removed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="conversationId"/> is null or whitespace.</exception>
    public bool Clear(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        lock (this._lock)
        {
            return this._store.Remove(conversationId);
        }
    }
}
