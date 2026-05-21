// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Options for <see cref="OpenAIRealtimeAgent"/>.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class OpenAIRealtimeAgentOptions
{
    /// <summary>Gets or sets a human-readable agent name.</summary>
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
