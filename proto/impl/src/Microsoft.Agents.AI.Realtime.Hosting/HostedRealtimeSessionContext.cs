// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Agents.AI.Realtime.Hosting;

/// <summary>
/// Minimal per-connection context for a hosted realtime session — the
/// hosting-shared identity/isolation surface called out in plan §5.1.
/// </summary>
/// <remarks>
/// Header-to-key extraction is provided by callers; no policy is shipped
/// this phase.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class HostedRealtimeSessionContext
{
    /// <summary>Gets or sets the connection's isolation key.</summary>
    public string? IsolationKey { get; set; }

    /// <summary>Gets or sets the connection's caller identity, when known.</summary>
    public string? CallerIdentity { get; set; }
}
