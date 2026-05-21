// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents an already-connected real-time session opened by a
/// <see cref="RealtimeAgent"/>. Wraps an underlying
/// <see cref="IRealtimeClientSession"/> from <c>Microsoft.Extensions.AI</c>,
/// exposing the same wire-level send / streaming-response surface plus a
/// small set of AF-side conveniences.
/// </summary>
/// <remarks>
/// <para>
/// Constructed in the connected state; there is no separate <c>ConnectAsync</c>
/// step on the session. The underlying provider has already opened the WebSocket
/// (or other transport) by the time the session is handed back from
/// <see cref="RealtimeAgent.ConnectSessionAsync"/>.
/// </para>
/// <para>
/// <strong>Single-consumer enumeration (ADR-002):</strong>
/// <see cref="GetStreamingResponseAsync"/> may be enumerated at most once per
/// session. Provider implementations should throw <see cref="InvalidOperationException"/>
/// if it is invoked a second time.
/// </para>
/// <para>
/// <strong>History (ADR-004):</strong> the base type exposes a read-only
/// <see cref="History"/> collection and a <c>protected</c> mutator
/// surface. The actual projection from <see cref="RealtimeServerMessage"/> events
/// to <see cref="RealtimeConversationItem"/> entries lives in
/// <c>Microsoft.Agents.AI.Realtime</c> (the core package), not in
/// <c>Abstractions</c>, so the abstractions assembly does not need to know about
/// every <see cref="RealtimeServerMessage"/> subtype. The hosted layer does not
/// own history (no store dependency in this phase).
/// </para>
/// <para>
/// <strong>Persistence:</strong> there is no <c>Serialize</c> /
/// <c>Deserialize</c> pair in this phase. <see cref="ConversationId"/> is a
/// forward-compat slot for providers (notably Gemini) that support resumable
/// sessions.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public abstract class RealtimeSession : IAsyncDisposable
{
    private readonly List<RealtimeConversationItem> _history = new();

    /// <summary>Initializes a new instance of the <see cref="RealtimeSession"/> class.</summary>
    /// <param name="innerSession">The underlying connected <see cref="IRealtimeClientSession"/>.</param>
    /// <param name="stateBag">An optional pre-populated state bag. When <see langword="null"/>, a new empty bag is created.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerSession"/> is <see langword="null"/>.</exception>
    protected RealtimeSession(IRealtimeClientSession innerSession, AgentSessionStateBag? stateBag = null)
    {
        this.InnerSession = innerSession ?? throw new ArgumentNullException(nameof(innerSession));
        this.StateBag = stateBag ?? new AgentSessionStateBag();
    }

    /// <summary>
    /// Gets the inner <see cref="IRealtimeClientSession"/> that this session wraps.
    /// </summary>
    /// <remarks>
    /// Derived classes use this for delegation; consumers should normally interact
    /// with the wrapping <see cref="RealtimeSession"/> directly to benefit from
    /// AF-side projection (e.g. interruption normalization) and convenience helpers.
    /// </remarks>
    protected IRealtimeClientSession InnerSession { get; }

    /// <summary>
    /// Gets the session options reported by the underlying provider session.
    /// </summary>
    public virtual RealtimeSessionOptions? Options => this.InnerSession.Options;

    /// <summary>
    /// Gets the provider-assigned conversation identifier, when the provider supports
    /// resumable sessions (e.g. Gemini Live). Otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Forward-compat slot per <c>session.md</c> §4.2. The proto does not currently
    /// support reconnect-with-replay; consumers should treat a non-null value as a
    /// hint for telemetry only.
    /// </remarks>
    public virtual string? ConversationId => null;

    /// <summary>
    /// Gets any arbitrary state associated with this session. Mirrors
    /// <see cref="AgentSession.StateBag"/>.
    /// </summary>
    public AgentSessionStateBag StateBag { get; }

    /// <summary>
    /// Gets the client-tracked conversation history projected from server messages.
    /// </summary>
    /// <remarks>
    /// The mutator surface (<see cref="AddHistoryItem"/>,
    /// <see cref="ReplaceHistoryItem"/>, <see cref="ClearHistory"/>) is
    /// <c>protected</c>; the actual projection logic lives in
    /// <c>Microsoft.Agents.AI.Realtime</c> per ADR-004.
    /// </remarks>
    public IReadOnlyList<RealtimeConversationItem> History => this._history;

    /// <summary>Appends a conversation item to the projected history.</summary>
    /// <param name="item">The conversation item to append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    protected void AddHistoryItem(RealtimeConversationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (this._history)
        {
            this._history.Add(item);
        }
    }

    /// <summary>Replaces the conversation item at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index of the item to replace.</param>
    /// <param name="item">The replacement item.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    protected void ReplaceHistoryItem(int index, RealtimeConversationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (this._history)
        {
            if ((uint)index >= (uint)this._history.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            this._history[index] = item;
        }
    }

    /// <summary>Removes all entries from the projected history.</summary>
    protected void ClearHistory()
    {
        lock (this._history)
        {
            this._history.Clear();
        }
    }

    /// <summary>
    /// Sends a client message to the session. Forwards to the underlying
    /// <see cref="IRealtimeClientSession.SendAsync"/> by default; providers may
    /// override to intercept AF-side message subtypes such as
    /// <see cref="CancelResponseRealtimeClientMessage"/> and translate them to
    /// provider-specific wire ops.
    /// </summary>
    /// <param name="message">The client message to send.</param>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public virtual Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this.InnerSession.SendAsync(message, cancellationToken);
    }

    /// <summary>
    /// Streams server messages from the session. Forwards to the underlying
    /// <see cref="IRealtimeClientSession.GetStreamingResponseAsync"/> by default.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>The async stream of server messages produced by the provider.</returns>
    /// <remarks>
    /// Per ADR-002, the returned stream is single-consumer; provider
    /// implementations throw <see cref="InvalidOperationException"/> on a second
    /// enumeration attempt.
    /// </remarks>
    public virtual IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => this.InnerSession.GetStreamingResponseAsync(cancellationToken);

    /// <summary>Asks the <see cref="RealtimeSession"/> for an object of the specified type.</summary>
    /// <param name="serviceType">The type of object being requested.</param>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found object, otherwise <see langword="null"/>.</returns>
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return this.InnerSession.GetService(serviceType, serviceKey);
    }

    /// <summary>Asks the <see cref="RealtimeSession"/> for an object of type <typeparamref name="TService"/>.</summary>
    /// <typeparam name="TService">The type of the object to be retrieved.</typeparam>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found object, otherwise <see langword="null"/>.</returns>
    public TService? GetService<TService>(object? serviceKey = null)
        => this.GetService(typeof(TService), serviceKey) is TService service ? service : default;

    /// <summary>
    /// Releases the underlying provider session. Safe to call multiple times.
    /// </summary>
    public virtual ValueTask DisposeAsync() => this.InnerSession.DisposeAsync();

    // ---------------------------------------------------------------------
    // Convenience helpers (non-virtual; implemented via SendAsync). Per
    // implementation-plan.md §4.1 / §3.1 these are ergonomic sugar over the
    // M.E.AI client-message subtypes — they do not introduce a parallel taxonomy.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Appends an audio chunk to the input audio buffer. Sends an
    /// <see cref="InputAudioBufferAppendRealtimeClientMessage"/>.
    /// </summary>
    /// <param name="audio">The audio data chunk to append.</param>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>A task that completes when the chunk has been sent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="audio"/> is <see langword="null"/>.</exception>
    public Task AppendInputAudioAsync(DataContent audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return this.SendAsync(new InputAudioBufferAppendRealtimeClientMessage(audio), cancellationToken);
    }

    /// <summary>
    /// Commits the input audio buffer. Sends an
    /// <see cref="InputAudioBufferCommitRealtimeClientMessage"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>A task that completes when the commit has been sent.</returns>
    public Task CommitInputAudioAsync(CancellationToken cancellationToken = default)
        => this.SendAsync(new InputAudioBufferCommitRealtimeClientMessage(), cancellationToken);

    /// <summary>
    /// Sends a conversation item to the session. Wraps the item in a
    /// <see cref="CreateConversationItemRealtimeClientMessage"/>.
    /// </summary>
    /// <param name="item">The conversation item to send.</param>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>A task that completes when the item has been sent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public Task SendMessageAsync(RealtimeConversationItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return this.SendAsync(new CreateConversationItemRealtimeClientMessage(item), cancellationToken);
    }

    /// <summary>
    /// Requests that the provider generate a new response. Sends a
    /// <see cref="CreateResponseRealtimeClientMessage"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>A task that completes when the request has been sent.</returns>
    public Task RequestResponseAsync(CancellationToken cancellationToken = default)
        => this.SendAsync(new CreateResponseRealtimeClientMessage(), cancellationToken);

    /// <summary>
    /// Requests cancellation of the in-flight response. Sends a
    /// <see cref="CancelResponseRealtimeClientMessage"/>; providers' SendAsync
    /// overrides translate this to the appropriate wire op
    /// (<c>response.cancel</c>, <c>output_audio_buffer.clear</c>, etc.).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>A task that completes when the cancel request has been sent.</returns>
    public Task CancelResponseAsync(CancellationToken cancellationToken = default)
        => this.SendAsync(new CancelResponseRealtimeClientMessage(), cancellationToken);

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
        => this.ConversationId is { } cid
         ? $"ConversationId = {cid}, History = {this._history.Count}"
         : $"History = {this._history.Count}";
}
