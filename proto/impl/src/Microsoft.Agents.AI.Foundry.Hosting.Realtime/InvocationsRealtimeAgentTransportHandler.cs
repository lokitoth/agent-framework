// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime;

/// <summary>
/// <see cref="RealtimeAgentTransportHandler"/> for the Azure AI Foundry
/// Invocations protocol (POST <c>/invocations</c> + SSE response stream),
/// per plan §5.2.
/// </summary>
/// <remarks>
/// The handler is constructed with an <see cref="IInvocationsRequestSink"/>
/// abstraction so unit tests can drive the request/response without standing
/// up a real HTTP server. ASP.NET Core integration is wired by
/// <c>MapFoundryRealtime</c> (also in this package).
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class InvocationsRealtimeAgentTransportHandler
{
    private readonly IRealtimeEventEncoder _encoder;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance.</summary>
    public InvocationsRealtimeAgentTransportHandler(IRealtimeEventEncoder encoder, ILoggerFactory? loggerFactory = null)
    {
        this._encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<InvocationsRealtimeAgentTransportHandler>();
    }

    /// <summary>
    /// Runs the Invocations transport: open a session against
    /// <paramref name="agent"/>, pump inbound client messages from
    /// <paramref name="sink"/>, and pump outbound server events back to
    /// <paramref name="sink"/> as SSE frames.
    /// </summary>
    public async Task RunAsync(RealtimeAgent agent, IInvocationsRequestSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(sink);

        HostedRealtimeSessionContext sessionContext = new()
        {
            IsolationKey = sink.SessionId,
            CallerIdentity = sink.InvocationId,
        };

        await using RealtimeSession session = await agent.ConnectSessionAsync(cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation(
            "Invocations realtime session opened (agent={AgentId}, session={SessionId}).",
            agent.Id, sessionContext.IsolationKey);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task inbound = PumpInboundAsync(session, sink, linked.Token);
        Task outbound = this.PumpOutboundAsync(session, sink, linked.Token);

        await Task.WhenAny(inbound, outbound).ConfigureAwait(false);
        linked.Cancel();

        try
        {
            await Task.WhenAll(inbound, outbound).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PumpInboundAsync(RealtimeSession session, IInvocationsRequestSink sink, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            RealtimeClientMessage? msg = await sink.ReadNextClientMessageAsync(cancellationToken).ConfigureAwait(false);
            if (msg is null)
            {
                return;
            }

            await session.SendAsync(msg, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PumpOutboundAsync(RealtimeSession session, IInvocationsRequestSink sink, CancellationToken cancellationToken)
    {
        await foreach (RealtimeServerMessage msg in session.GetStreamingResponseAsync(cancellationToken).ConfigureAwait(false))
        {
            using MemoryStream buffer = new();
            int written = this._encoder.Encode(msg, buffer);
            if (written <= 0)
            {
                continue;
            }

            await sink.WriteSseFrameAsync(buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }
}
