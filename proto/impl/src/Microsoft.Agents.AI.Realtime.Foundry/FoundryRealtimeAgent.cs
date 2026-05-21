// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.Foundry;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A <see cref="RealtimeAgent"/> backed by Azure AI Foundry's VoiceLive
/// service. Conversation mode only this phase (per plan §4.3).
/// </summary>
/// <remarks>
/// <para>
/// Production code constructs an internal
/// <see cref="IWebSocketTransport"/>; unit tests inject a fake via the
/// internals-visible-to test constructor.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class FoundryRealtimeAgent : RealtimeAgent
{
    private readonly FoundryRealtimeAgentOptions _options;
    private readonly Func<CancellationToken, Task<IWebSocketTransport>> _transportFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoundryRealtimeAgent"/>
    /// class.
    /// </summary>
    /// <param name="options">Agent options including endpoint and auth.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Neither <see cref="FoundryRealtimeAgentOptions.Credential"/> nor <see cref="FoundryRealtimeAgentOptions.ApiKey"/> is set.</exception>
    public FoundryRealtimeAgent(FoundryRealtimeAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Credential is null && string.IsNullOrEmpty(options.ApiKey))
        {
            throw new ArgumentException("Either Credential or ApiKey must be provided.", nameof(options));
        }

        this._options = options;
        this._transportFactory = static _ => throw new NotSupportedException(
            "The production WebSocket transport is not implemented in this prototype phase. " +
            "Use the internal test constructor with an IWebSocketTransport factory.");
    }

    internal FoundryRealtimeAgent(FoundryRealtimeAgentOptions options, Func<CancellationToken, Task<IWebSocketTransport>> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transportFactory);
        this._options = options;
        this._transportFactory = transportFactory;
    }

    /// <inheritdoc />
    public override string? Name => this._options.Name;

    /// <inheritdoc />
    public override string? Description => this._options.Description;

    /// <inheritdoc />
    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        IWebSocketTransport transport = await this._transportFactory(cancellationToken).ConfigureAwait(false);
        try
        {
            await transport.ConnectAsync(this._options.Endpoint, cancellationToken).ConfigureAwait(false);
            FoundryRealtimeClientSession clientSession = new(transport, this._options.SessionOptions);

            if (this._options.SessionOptions is not null)
            {
                await clientSession.SendAsync(new SessionUpdateRealtimeClientMessage(this._options.SessionOptions), cancellationToken).ConfigureAwait(false);
            }

            return new FoundryRealtimeSession(clientSession);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
