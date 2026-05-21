// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Realtime.Hosting;

/// <summary>Service-collection extensions for hosted realtime agents.</summary>
[Experimental("MEAIREALTIME001")]
public static class RealtimeAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="RealtimeAgent"/> under the given key, wrapped in
    /// a <see cref="HostedRealtimeAgent"/>. Parallels <c>AddAIAgent</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The agent registration key.</param>
    /// <param name="agentFactory">Factory invoked to construct the underlying realtime agent.</param>
    /// <param name="lifetime">DI lifetime (defaults to <see cref="ServiceLifetime.Singleton"/>).</param>
    /// <returns>A fluent builder for further configuration.</returns>
    public static IHostedRealtimeAgentBuilder AddRealtimeAgent(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, RealtimeAgent> agentFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(agentFactory);

        services.Add(new ServiceDescriptor(typeof(RealtimeAgent), name, (sp, _) =>
        {
            RealtimeAgent inner = agentFactory(sp);
            HostedRealtimeSessionContext context = new();
            return new HostedRealtimeAgent(inner, context);
        }, lifetime));

        return new HostedRealtimeAgentBuilder(name, services);
    }
}
