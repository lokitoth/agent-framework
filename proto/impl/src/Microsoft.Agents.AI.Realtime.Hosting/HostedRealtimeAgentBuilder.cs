// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Realtime.Hosting;

/// <summary>Fluent builder for a hosted realtime agent registration.</summary>
[Experimental("MEAIREALTIME001")]
public interface IHostedRealtimeAgentBuilder
{
    /// <summary>The agent registration key.</summary>
    string Name { get; }

    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }
}

/// <inheritdoc />
[Experimental("MEAIREALTIME001")]
internal sealed class HostedRealtimeAgentBuilder : IHostedRealtimeAgentBuilder
{
    public HostedRealtimeAgentBuilder(string name, IServiceCollection services)
    {
        this.Name = name ?? throw new ArgumentNullException(nameof(name));
        this.Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name { get; }

    public IServiceCollection Services { get; }
}
