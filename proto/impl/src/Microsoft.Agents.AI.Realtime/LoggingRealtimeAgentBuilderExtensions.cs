// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI;

/// <summary>
/// Extension methods for attaching logging middleware to a
/// <see cref="RealtimeAgentBuilder"/> pipeline.
/// </summary>
[Experimental("MEAIREALTIME001")]
public static class LoggingRealtimeAgentBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="LoggingRealtimeAgent"/> stage to the pipeline.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="logger">An optional logger. When omitted, a logger is resolved from <see cref="ILoggerFactory"/> in the service provider.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RealtimeAgentBuilder UseLogging(this RealtimeAgentBuilder builder, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerAgent, services) =>
        {
            ILogger resolved = LoggingRealtimeAgent.ResolveLogger(logger, services.GetService<ILoggerFactory>());
            return new LoggingRealtimeAgent(innerAgent, resolved);
        });
    }
}
