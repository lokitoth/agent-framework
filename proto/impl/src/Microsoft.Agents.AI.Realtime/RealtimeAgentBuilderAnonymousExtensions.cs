// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI;

/// <summary>
/// Builder extensions for inline / anonymous middleware.
/// </summary>
[Experimental("MEAIREALTIME001")]
public static class RealtimeAgentBuilderAnonymousExtensions
{
    /// <summary>
    /// Adds an anonymous delegating stage to the pipeline.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="connectFunc">A delegate invoked when a session is requested; receives the inner agent and a cancellation token.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="connectFunc"/> is <see langword="null"/>.</exception>
    public static RealtimeAgentBuilder Use(
        this RealtimeAgentBuilder builder,
        Func<RealtimeAgent, CancellationToken, ValueTask<RealtimeSession>> connectFunc)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectFunc);
        return builder.Use((innerAgent, _) => new AnonymousDelegatingRealtimeAgent(innerAgent, connectFunc));
    }
}
