// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides a builder for creating pipelines of <see cref="RealtimeAgent"/>s.
/// </summary>
/// <remarks>
/// Mirrors <see cref="AIAgentBuilder"/>. Factories registered via <see cref="Use(Func{RealtimeAgent, RealtimeAgent})"/>
/// (or its <see cref="IServiceProvider"/>-aware overload) are applied in reverse order
/// so that the first <c>Use</c> call wraps the outermost layer.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class RealtimeAgentBuilder
{
    private readonly Func<IServiceProvider, RealtimeAgent> _innerAgentFactory;
    private List<Func<RealtimeAgent, IServiceProvider, RealtimeAgent>>? _agentFactories;

    /// <summary>Initializes a new instance of the <see cref="RealtimeAgentBuilder"/> class.</summary>
    /// <param name="innerAgent">The inner <see cref="RealtimeAgent"/> that represents the underlying provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerAgent"/> is <see langword="null"/>.</exception>
    public RealtimeAgentBuilder(RealtimeAgent innerAgent)
    {
        ArgumentNullException.ThrowIfNull(innerAgent);
        this._innerAgentFactory = _ => innerAgent;
    }

    /// <summary>Initializes a new instance of the <see cref="RealtimeAgentBuilder"/> class.</summary>
    /// <param name="innerAgentFactory">A callback that produces the inner <see cref="RealtimeAgent"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerAgentFactory"/> is <see langword="null"/>.</exception>
    public RealtimeAgentBuilder(Func<IServiceProvider, RealtimeAgent> innerAgentFactory)
    {
        ArgumentNullException.ThrowIfNull(innerAgentFactory);
        this._innerAgentFactory = innerAgentFactory;
    }

    /// <summary>Builds the <see cref="RealtimeAgent"/> pipeline.</summary>
    /// <param name="services">An optional service provider for resolving dependencies; an empty provider is used when <see langword="null"/>.</param>
    /// <returns>The composed <see cref="RealtimeAgent"/>.</returns>
    public RealtimeAgent Build(IServiceProvider? services = null)
    {
        services ??= EmptyServiceProvider.Instance;
        RealtimeAgent agent = this._innerAgentFactory(services);

        if (this._agentFactories is not null)
        {
            for (int i = this._agentFactories.Count - 1; i >= 0; i--)
            {
                agent = this._agentFactories[i](agent, services)
                    ?? throw new InvalidOperationException(
                        $"The {nameof(RealtimeAgentBuilder)} entry at index {i} returned null. " +
                        $"Ensure that the callbacks passed to {nameof(Use)} return non-null {nameof(RealtimeAgent)} instances.");
            }
        }

        return agent;
    }

    /// <summary>Adds a factory for an intermediate agent.</summary>
    /// <param name="agentFactory">The agent factory function.</param>
    /// <returns>The current <see cref="RealtimeAgentBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agentFactory"/> is <see langword="null"/>.</exception>
    public RealtimeAgentBuilder Use(Func<RealtimeAgent, RealtimeAgent> agentFactory)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        return this.Use((innerAgent, _) => agentFactory(innerAgent));
    }

    /// <summary>Adds a factory for an intermediate agent that can resolve services.</summary>
    /// <param name="agentFactory">The agent factory function.</param>
    /// <returns>The current <see cref="RealtimeAgentBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agentFactory"/> is <see langword="null"/>.</exception>
    public RealtimeAgentBuilder Use(Func<RealtimeAgent, IServiceProvider, RealtimeAgent> agentFactory)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        (this._agentFactories ??= []).Add(agentFactory);
        return this;
    }

    private sealed class EmptyServiceProvider : IServiceProvider, IKeyedServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;

        public object? GetKeyedService(Type serviceType, object? serviceKey) => null;

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
            => throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
    }
}
