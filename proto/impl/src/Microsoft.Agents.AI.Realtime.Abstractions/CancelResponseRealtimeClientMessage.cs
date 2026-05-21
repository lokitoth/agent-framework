// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents a real-time client message that requests cancellation of an
/// in-flight response (a "barge-in" or explicit interrupt).
/// </summary>
/// <remarks>
/// <para>
/// MEAI 10.5 does not ship a typed cancel-response message — the normalized
/// event map (<c>normalized-events.md</c> §117) lists <c>response.cancel</c>
/// as a <see cref="RealtimeClientMessage.RawRepresentation"/> passthrough.
/// AF adds <see cref="CancelResponseRealtimeClientMessage"/> as a typed
/// seam so that <see cref="RealtimeSession.CancelResponseAsync"/> can remain
/// a non-virtual convenience helper and providers can pattern-match in their
/// <see cref="IRealtimeClientSession.SendAsync"/> override to emit the right
/// wire operation (OpenAI <c>response.cancel</c>, Foundry VoiceLive
/// equivalent, etc.).
/// </para>
/// <para>
/// This is the only AF-defined <see cref="RealtimeClientMessage"/> subtype
/// in the proto. Provider-specific cancel knobs ride on
/// <see cref="RealtimeClientMessage.RawRepresentation"/>.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public class CancelResponseRealtimeClientMessage : RealtimeClientMessage
{
    /// <summary>Initializes a new instance of the <see cref="CancelResponseRealtimeClientMessage"/> class.</summary>
    public CancelResponseRealtimeClientMessage()
    {
    }

    /// <summary>
    /// Gets or sets the identifier of the response to cancel, when the caller
    /// knows it. Providers that need to target a specific response (e.g.
    /// OpenAI Realtime requires the <c>response_id</c>) use this value; when
    /// <see langword="null"/>, the provider cancels the most recent in-flight
    /// response.
    /// </summary>
    public string? ResponseId { get; set; }
}
