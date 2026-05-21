// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI.Realtime.Hosting;

/// <summary>
/// Base class for hosted realtime transport handlers. Drives the standard
/// connect / pump / close lifecycle: open a session, fan client messages
/// inbound, fan server events outbound through an
/// <see cref="IRealtimeEventEncoder"/>, and dispose cleanly on cancellation
/// or peer close.
/// </summary>
[Experimental("MEAIREALTIME001")]
public abstract class RealtimeAgentTransportHandler : IRealtimeAgentTransport
{
    private readonly IRealtimeEventEncoder _encoder;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance.</summary>
    protected RealtimeAgentTransportHandler(IRealtimeEventEncoder encoder, ILoggerFactory? loggerFactory = null)
    {
        this._encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(this.GetType());
    }

    /// <inheritdoc />
    public virtual async Task RunAsync(RealtimeAgent agent, IRealtimeAgentTransportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(context);

        await using RealtimeSession session = await agent.ConnectSessionAsync(cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Hosted realtime session opened ({AgentId}).", agent.Id);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task inboundPump = this.PumpInboundAsync(session, context, linked.Token);
        Task outboundPump = this.PumpOutboundAsync(session, context, linked.Token);

        Task first = await Task.WhenAny(inboundPump, outboundPump).ConfigureAwait(false);
        linked.Cancel();

        try
        {
            await Task.WhenAll(inboundPump, outboundPump).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        await first.ConfigureAwait(false);
    }

    private async Task PumpInboundAsync(RealtimeSession session, IRealtimeAgentTransportContext context, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            RealtimeClientMessage? message = await context.ReceiveClientMessageAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return;
            }

            await session.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PumpOutboundAsync(RealtimeSession session, IRealtimeAgentTransportContext context, CancellationToken cancellationToken)
    {
        await foreach (RealtimeServerMessage msg in session.GetStreamingResponseAsync(cancellationToken).ConfigureAwait(false))
        {
            using MemoryStream buffer = new();
            int written = this._encoder.Encode(msg, buffer);
            if (written <= 0)
            {
                continue;
            }

            await context.WriteEventAsync(buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }
}
