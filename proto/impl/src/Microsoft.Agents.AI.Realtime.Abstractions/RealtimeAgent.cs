// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides the base abstraction for real-time agents that hold an open
/// bidirectional connection with a model (e.g. Azure AI Foundry VoiceLive,
/// OpenAI Realtime, Gemini Live).
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="AIAgent"/>, which is request/response shaped,
/// <see cref="RealtimeAgent"/> exposes <see cref="ConnectSessionAsync"/> as
/// its primary entry point. The returned <see cref="RealtimeSession"/> is
/// <strong>already connected</strong> — there is no separate <c>ConnectAsync</c>
/// step on the session itself (per <c>session.md</c> §4.1).
/// </para>
/// <para>
/// Configuration lives on the agent, not per-connect call. Subclasses accept
/// provider-specific options (instructions, voice, audio formats, VAD, tools)
/// at construction time and apply them when establishing the underlying
/// <see cref="Microsoft.Extensions.AI.IRealtimeClientSession"/>. The
/// <see cref="ConnectSessionAsync"/> signature accepts only a
/// <see cref="CancellationToken"/>.
/// </para>
/// <para>
/// <strong>Security:</strong> as with <see cref="AIAgent"/>, a
/// <see cref="RealtimeAgent"/> orchestrates data across trust boundaries.
/// Audio and text streamed in either direction may carry user PII or
/// prompt-injection attempts; treat model output as untrusted.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public abstract class RealtimeAgent
{
    private static readonly AsyncLocal<RealtimeAgentRunContext?> s_currentContext = new();

    /// <summary>
    /// Gets the unique identifier for this agent instance.
    /// </summary>
    /// <remarks>
    /// For in-memory agents this defaults to a randomly-generated id. Provider
    /// implementations may override <see cref="IdCore"/> to surface a
    /// service-assigned identifier.
    /// </remarks>
    public string Id { get => this.IdCore ?? field; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets a custom identifier for the agent, which can be overridden by derived classes.
    /// </summary>
    /// <value>
    /// A string representing the agent's identifier, or <see langword="null"/> if the
    /// default randomly-generated identifier should be used.
    /// </value>
    protected virtual string? IdCore => null;

    /// <summary>Gets the human-readable name of the agent.</summary>
    public virtual string? Name { get; }

    /// <summary>Gets a description of the agent's purpose, capabilities, or behavior.</summary>
    public virtual string? Description { get; }

    /// <summary>
    /// Gets or sets the <see cref="RealtimeAgentRunContext"/> for the current realtime run.
    /// </summary>
    /// <remarks>
    /// Flows across async calls. Implementations that wrap or decorate a
    /// <see cref="RealtimeAgent"/> can use this to discover the active agent and
    /// session without taking them as parameters.
    /// </remarks>
    public static RealtimeAgentRunContext? CurrentRunContext
    {
        get => s_currentContext.Value;
        protected set => s_currentContext.Value = value;
    }

    /// <summary>
    /// Opens a new <see cref="RealtimeSession"/> against the provider. The
    /// returned session is already connected; callers may immediately call
    /// <see cref="RealtimeSession.SendAsync"/> or
    /// <see cref="RealtimeSession.GetStreamingResponseAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <returns>A live <see cref="RealtimeSession"/>.</returns>
    public ValueTask<RealtimeSession> ConnectSessionAsync(CancellationToken cancellationToken = default)
        => this.ConnectSessionCoreAsync(cancellationToken);

    /// <summary>When overridden in a derived class, performs the provider-specific connection handshake.</summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <returns>A live <see cref="RealtimeSession"/>.</returns>
    protected abstract ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken);

    /// <summary>Asks the <see cref="RealtimeAgent"/> for an object of the specified type.</summary>
    /// <param name="serviceType">The type of object being requested.</param>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found object, otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Mirrors <see cref="AIAgent.GetService(System.Type, object?)"/>. Typical uses include
    /// retrieving <see cref="RealtimeAgentMetadata"/> or the underlying
    /// <see cref="Microsoft.Extensions.AI.IRealtimeClient"/>.
    /// </remarks>
    public virtual object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
              ? this
              : null;
    }

    /// <summary>Asks the <see cref="RealtimeAgent"/> for an object of type <typeparamref name="TService"/>.</summary>
    /// <typeparam name="TService">The type of the object to be retrieved.</typeparam>
    /// <param name="serviceKey">An optional key that can be used to help identify the target service.</param>
    /// <returns>The found object, otherwise <see langword="null"/>.</returns>
    public TService? GetService<TService>(object? serviceKey = null)
        => this.GetService(typeof(TService), serviceKey) is TService service ? service : default;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay =>
        this.Name is { } name ? $"Id = {this.Id}, Name = {name}" : $"Id = {this.Id}";
}
