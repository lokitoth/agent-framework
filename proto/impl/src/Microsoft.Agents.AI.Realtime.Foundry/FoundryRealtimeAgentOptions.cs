// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Options for <see cref="FoundryRealtimeAgent"/>. Carries the
/// Foundry-tier connection parameters and the default
/// <see cref="RealtimeSessionOptions"/> applied as the initial
/// <c>session.update</c>.
/// </summary>
/// <remarks>
/// Azure-only knobs (<c>azure_semantic_vad</c>,
/// <c>azure_deep_noise_suppression</c>, HD/custom voices, <c>rate</c>) ride on
/// <see cref="RealtimeSessionOptions.RawRepresentationFactory"/> per
/// <c>session.md</c> §4.4 — they are <strong>not</strong> typed on the AF
/// surface.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class FoundryRealtimeAgentOptions
{
    /// <summary>Gets or sets the VoiceLive endpoint URI.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets or sets the Foundry project name.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Gets or sets the agent name to connect to.</summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// Gets or sets the credential used to authenticate. Either this or
    /// <see cref="ApiKey"/> must be set.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets an API key, if used in place of a
    /// <see cref="TokenCredential"/>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the agent's human-readable name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the agent description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the default <see cref="RealtimeSessionOptions"/> applied to
    /// each new session opened by this agent. When <see langword="null"/>, the
    /// provider defaults are used.
    /// </summary>
    public RealtimeSessionOptions? SessionOptions { get; set; }
}
