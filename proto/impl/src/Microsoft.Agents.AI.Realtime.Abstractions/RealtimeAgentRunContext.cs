// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides context for an in-flight realtime agent connection — the sibling of
/// <see cref="AgentRunContext"/> for the synchronous request/response surface.
/// </summary>
/// <remarks>
/// Stored in an <see cref="System.Threading.AsyncLocal{T}"/> on
/// <see cref="RealtimeAgent.CurrentRunContext"/> so that nested abstractions
/// (decorators, tool invocations, telemetry layers) can discover the active
/// agent and session without having them threaded through their APIs.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class RealtimeAgentRunContext
{
    /// <summary>Initializes a new instance of the <see cref="RealtimeAgentRunContext"/> class.</summary>
    /// <param name="agent">The agent that owns the active connection.</param>
    /// <param name="session">The active session, when one has been established.</param>
    public RealtimeAgentRunContext(RealtimeAgent agent, RealtimeSession? session = null)
    {
        this.Agent = agent ?? throw new ArgumentNullException(nameof(agent));
        this.Session = session;
    }

    /// <summary>Gets the <see cref="RealtimeAgent"/> associated with the current run.</summary>
    public RealtimeAgent Agent { get; }

    /// <summary>Gets the <see cref="RealtimeSession"/> associated with the current run, when one has been opened.</summary>
    public RealtimeSession? Session { get; internal set; }
}
