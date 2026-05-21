// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating <see cref="RealtimeSession"/> that emits OpenTelemetry
/// activity events for individual send / receive operations on the wrapped
/// session.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class OpenTelemetryRealtimeSession : DelegatingRealtimeSession
{
    private readonly ActivitySource _activitySource;

    public OpenTelemetryRealtimeSession(RealtimeSession innerSession, ActivitySource activitySource)
        : base(innerSession)
    {
        this._activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
    }

    public override Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using Activity? activity = this._activitySource.StartActivity("send", ActivityKind.Client);
        _ = activity?.SetTag("realtime.message.type", message.GetType().Name);

        return base.SendAsync(message, cancellationToken);
    }

    public override IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => this.EnumerateAndTraceAsync(base.GetStreamingResponseAsync(cancellationToken), cancellationToken);

    private async IAsyncEnumerable<RealtimeServerMessage> EnumerateAndTraceAsync(
        IAsyncEnumerable<RealtimeServerMessage> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using Activity? activity = this._activitySource.StartActivity("stream", ActivityKind.Client);
        long messageCount = 0;

        await foreach (RealtimeServerMessage message in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            messageCount++;
            yield return message;
        }

        _ = activity?.SetTag("realtime.messages.count", messageCount);
    }
}
