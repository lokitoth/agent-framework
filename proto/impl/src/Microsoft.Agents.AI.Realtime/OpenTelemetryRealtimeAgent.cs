// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating <see cref="RealtimeAgent"/> that emits OpenTelemetry activity
/// spans for the connect operation and wraps the returned session so that
/// per-send and per-receive operations attach to a parent session span.
/// </summary>
/// <remarks>
/// <para>
/// This decorator <em>does not</em> wire up exporters or specify a
/// <see cref="System.Diagnostics.ActivityListener"/>. Hosts are expected to
/// configure their own listeners against the supplied <see cref="ActivitySource"/>
/// or against the default source name <see cref="DefaultSourceName"/>.
/// </para>
/// <para>
/// Per implementation-plan.md §4.2 / review §S1 this is a small stub --
/// span/meter names are populated but no histograms or counters are emitted.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class OpenTelemetryRealtimeAgent : DelegatingRealtimeAgent
{
    /// <summary>The default <see cref="ActivitySource"/> name used by this decorator.</summary>
    public const string DefaultSourceName = RealtimeOpenTelemetryConsts.DefaultSourceName;

    private readonly ActivitySource _activitySource;
    private readonly string? _sourceName;

    /// <summary>Initializes a new instance of the <see cref="OpenTelemetryRealtimeAgent"/> class.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="sourceName">An optional override for the activity source name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerAgent"/> is <see langword="null"/>.</exception>
    public OpenTelemetryRealtimeAgent(RealtimeAgent innerAgent, string? sourceName = null)
        : base(innerAgent)
    {
        this._sourceName = sourceName ?? DefaultSourceName;
        this._activitySource = new ActivitySource(this._sourceName);
    }

    /// <summary>Gets the <see cref="ActivitySource"/> this decorator emits to.</summary>
    public ActivitySource ActivitySource => this._activitySource;

    /// <inheritdoc/>
    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        using Activity? activity = this._activitySource.StartActivity(
            RealtimeOpenTelemetryConsts.OperationConnect,
            ActivityKind.Client);

        if (activity is not null)
        {
            _ = activity.SetTag(RealtimeOpenTelemetryConsts.GenAI.OperationName, RealtimeOpenTelemetryConsts.OperationConnect);
            _ = activity.SetTag(RealtimeOpenTelemetryConsts.GenAI.AgentId, this.Id);
            if (this.Name is { } name)
            {
                _ = activity.SetTag(RealtimeOpenTelemetryConsts.GenAI.AgentName, name);
            }
        }

        try
        {
            RealtimeSession session = await base.ConnectSessionCoreAsync(cancellationToken).ConfigureAwait(false);
            _ = activity?.SetStatus(ActivityStatusCode.Ok);
            return new OpenTelemetryRealtimeSession(session, this._activitySource);
        }
        catch (Exception ex)
        {
            _ = activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
