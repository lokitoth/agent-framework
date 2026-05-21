// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides metadata describing the capabilities of a <see cref="RealtimeAgent"/>.
/// </summary>
/// <remarks>
/// Realtime providers expose this via <see cref="RealtimeAgent.GetService(System.Type, object?)"/>
/// so that hosts, decorators, and telemetry layers can introspect the agent
/// without coupling to a particular provider implementation.
/// </remarks>
[Experimental("MEAIREALTIME001")]
[DebuggerDisplay("ProviderName = {ProviderName}, ModelId = {ModelId}")]
public sealed class RealtimeAgentMetadata
{
    /// <summary>Initializes a new instance of the <see cref="RealtimeAgentMetadata"/> class.</summary>
    /// <param name="providerName">The name of the provider that backs the agent (e.g. <c>"foundry.voicelive"</c>, <c>"openai.realtime"</c>).</param>
    /// <param name="modelId">The provider-specific model identifier, when known.</param>
    /// <param name="supportedModalities">The set of modalities the agent supports.</param>
    /// <param name="supportsInterruption">Whether the provider can emit <see cref="InterruptedRealtimeServerMessage"/> mid-response.</param>
    /// <param name="supportsVideo">Whether the provider accepts or emits video frames.</param>
    public RealtimeAgentMetadata(
        string? providerName = null,
        string? modelId = null,
        RealtimeModality supportedModalities = RealtimeModality.Text | RealtimeModality.Audio,
        bool supportsInterruption = false,
        bool supportsVideo = false)
    {
        this.ProviderName = providerName;
        this.ModelId = modelId;
        this.SupportedModalities = supportedModalities;
        this.SupportsInterruption = supportsInterruption;
        this.SupportsVideo = supportsVideo;
    }

    /// <summary>Gets the name of the provider, when known.</summary>
    public string? ProviderName { get; }

    /// <summary>Gets the model identifier, when known.</summary>
    public string? ModelId { get; }

    /// <summary>Gets the modalities the agent supports.</summary>
    public RealtimeModality SupportedModalities { get; }

    /// <summary>Gets a value indicating whether the agent surfaces interruption events.</summary>
    public bool SupportsInterruption { get; }

    /// <summary>Gets a value indicating whether the agent supports video modality.</summary>
    public bool SupportsVideo { get; }
}
