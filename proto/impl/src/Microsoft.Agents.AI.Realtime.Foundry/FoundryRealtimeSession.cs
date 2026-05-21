// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// <see cref="RealtimeSession"/> for Azure AI Foundry VoiceLive.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class FoundryRealtimeSession : RealtimeSession
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="innerSession">The wrapped client session.</param>
    public FoundryRealtimeSession(IRealtimeClientSession innerSession)
        : base(innerSession)
    {
    }
}
