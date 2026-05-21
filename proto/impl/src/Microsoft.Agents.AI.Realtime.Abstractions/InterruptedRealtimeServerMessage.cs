// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents a real-time server message indicating that the provider observed
/// an interruption of an in-flight response — typically because the caller
/// began speaking ("barge-in") or another participant cleared the output buffer.
/// </summary>
/// <remarks>
/// <para>
/// This is the single AF-defined inbound event called out by
/// <c>normalized-events.md</c> §6 G1: providers vary in how they signal
/// interruption (Foundry VoiceLive emits <c>output_audio_buffer.cleared</c>;
/// OpenAI Realtime emits <c>input_audio_buffer.speech_started</c> mid-response;
/// Gemini Live uses <c>generationComplete</c> with an interrupted reason).
/// AF normalizes all of these to <see cref="InterruptedRealtimeServerMessage"/>
/// so consumers can pattern-match on a single type.
/// </para>
/// <para>
/// The base <see cref="RealtimeServerMessage.Type"/> is set to the well-known
/// value <see cref="InterruptedType"/>; the AF-side history projection in
/// <c>Microsoft.Agents.AI.Realtime</c> uses an <c>is</c>-check (per ADR-005),
/// so consumers that need a type-based dispatcher can rely on either path.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public class InterruptedRealtimeServerMessage : RealtimeServerMessage
{
    /// <summary>
    /// Gets the well-known <see cref="RealtimeServerMessageType"/> assigned to
    /// instances of <see cref="InterruptedRealtimeServerMessage"/>.
    /// </summary>
    public static RealtimeServerMessageType InterruptedType { get; } = new("Interrupted");

    /// <summary>Initializes a new instance of the <see cref="InterruptedRealtimeServerMessage"/> class.</summary>
    public InterruptedRealtimeServerMessage()
    {
        this.Type = InterruptedType;
    }

    /// <summary>
    /// Gets or sets the identifier of the response that was interrupted, when known.
    /// </summary>
    /// <remarks>
    /// Providers that report a response id in the underlying wire event (e.g. OpenAI
    /// Realtime's <c>response.id</c>) populate this property so that consumers can
    /// correlate the interruption with a specific in-flight response.
    /// </remarks>
    public string? InterruptedResponseId { get; set; }

    /// <summary>
    /// Gets or sets the byte offset into the audio output buffer at which the
    /// interruption was observed, when known.
    /// </summary>
    /// <remarks>
    /// Used by clients to trim already-buffered audio when the playback layer
    /// supports sample-accurate truncation.
    /// </remarks>
    public long? OutputAudioOffsetInBytes { get; set; }
}
