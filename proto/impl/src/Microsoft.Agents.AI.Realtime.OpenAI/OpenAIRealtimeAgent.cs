// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A <see cref="RealtimeAgent"/> backed by the OpenAI Realtime API via
/// Microsoft.Extensions.AI's <see cref="IRealtimeClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// This package composes M.E.AI.OpenAI's <c>OpenAIRealtimeClient</c> directly
/// (per plan §4.4): the underlying client implements <see cref="IRealtimeClient"/>
/// and applies <see cref="OpenAIRealtimeAgentOptions.SessionOptions"/> as the
/// initial <c>session.update</c>.
/// </para>
/// <para>
/// The transport (WebSocket) is owned by the OpenAI SDK and is not pluggable
/// via <c>IWebSocketTransport</c> in this phase. Unit tests therefore exercise
/// our wrapper around an arbitrary <see cref="IRealtimeClient"/>, not against
/// the live OpenAI service.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class OpenAIRealtimeAgent : RealtimeAgent
{
    private readonly IRealtimeClient _client;
    private readonly OpenAIRealtimeAgentOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIRealtimeAgent"/>
    /// class.
    /// </summary>
    /// <param name="client">The underlying realtime client.</param>
    /// <param name="options">Optional agent options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public OpenAIRealtimeAgent(IRealtimeClient client, OpenAIRealtimeAgentOptions? options = null)
    {
        this._client = client ?? throw new ArgumentNullException(nameof(client));
        this._options = options ?? new OpenAIRealtimeAgentOptions();
    }

    /// <inheritdoc />
    public override string? Name => this._options.Name;

    /// <inheritdoc />
    public override string? Description => this._options.Description;

    /// <inheritdoc />
    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        IRealtimeClientSession clientSession = await this._client.CreateSessionAsync(this._options.SessionOptions, cancellationToken).ConfigureAwait(false);
        return new OpenAIRealtimeSession(clientSession);
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null)
        {
            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType.IsInstanceOfType(this._client))
            {
                return this._client;
            }

            return this._client.GetService(serviceType, serviceKey);
        }

        return null;
    }
}
