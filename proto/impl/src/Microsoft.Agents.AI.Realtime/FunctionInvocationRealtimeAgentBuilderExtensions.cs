// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI;

/// <summary>
/// Extensions for attaching automatic tool invocation to a
/// <see cref="RealtimeAgentBuilder"/> pipeline.
/// </summary>
[Experimental("MEAIREALTIME001")]
public static class FunctionInvocationRealtimeAgentBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="FunctionInvocationRealtimeAgent"/> stage to the pipeline.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="loggerFactory">An optional logger factory.</param>
    /// <param name="configure">An optional configuration callback applied to the
    /// underlying <see cref="FunctionInvokingRealtimeClient"/> (e.g. to add
    /// <see cref="FunctionInvokingRealtimeClient.AdditionalTools"/>).</param>
    /// <param name="functionInvocationServices">An optional service provider for tools.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static RealtimeAgentBuilder UseFunctionInvocation(
        this RealtimeAgentBuilder builder,
        ILoggerFactory? loggerFactory = null,
        Action<FunctionInvokingRealtimeClient>? configure = null,
        IServiceProvider? functionInvocationServices = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerAgent, services) =>
        {
            ILoggerFactory? resolvedFactory = loggerFactory ?? services.GetService<ILoggerFactory>();
            IServiceProvider? resolvedServices = functionInvocationServices ?? services;
            return new FunctionInvocationRealtimeAgent(innerAgent, resolvedFactory, configure, resolvedServices);
        });
    }
}
