// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Agents.AI;

/// <summary>
/// Extensions for attaching OpenTelemetry instrumentation to a
/// <see cref="RealtimeAgentBuilder"/> pipeline.
/// </summary>
[Experimental("MEAIREALTIME001")]
public static class OpenTelemetryRealtimeAgentBuilderExtensions
{
    /// <summary>
    /// Wraps the pipeline in an <see cref="OpenTelemetryRealtimeAgent"/>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="sourceName">An optional override for the <see cref="System.Diagnostics.ActivitySource"/> name.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RealtimeAgentBuilder UseOpenTelemetry(this RealtimeAgentBuilder builder, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerAgent, _) => new OpenTelemetryRealtimeAgent(innerAgent, sourceName));
    }
}
