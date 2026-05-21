// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI.Realtime.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime;

/// <summary>
/// Service-collection extensions for Azure AI Foundry realtime hosting,
/// per plan §5.2.
/// </summary>
[Experimental("MEAIREALTIME001")]
public static class FoundryRealtimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Foundry realtime hosting primitives: the
    /// VoiceLive-shaped <see cref="IRealtimeEventEncoder"/> as the default,
    /// and the <see cref="InvocationsRealtimeAgentTransportHandler"/>
    /// transport.
    /// </summary>
    public static IServiceCollection AddFoundryRealtime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRealtimeEventEncoder, VoiceLiveInvocationsEventEncoder>();
        services.AddSingleton<InvocationsRealtimeAgentTransportHandler>();
        return services;
    }
}
