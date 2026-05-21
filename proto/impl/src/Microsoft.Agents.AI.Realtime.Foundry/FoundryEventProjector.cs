// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Foundry;

/// <summary>
/// Projects raw Azure VoiceLive JSON events into AF's normalized
/// <see cref="RealtimeServerMessage"/> hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// VoiceLive's event vocabulary (see <c>vl_sample/client/voicelive_client.py</c>)
/// is the OpenAI Realtime shape with Azure extensions; the events we handle
/// here are the headline conversation events plus the interruption signal
/// (<c>output_audio_buffer.cleared</c>) per <c>normalized-events.md</c> §6 G1.
/// </para>
/// <para>
/// Unknown event types fall through to a base
/// <see cref="RealtimeServerMessage"/> carrying the original type string,
/// so consumers that need raw access can still observe them.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
internal static class FoundryEventProjector
{
    internal static RealtimeServerMessage? Project(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty("type", out JsonElement typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string typeName = typeProp.GetString()!;

        switch (typeName)
        {
            case "response.text.delta":
                {
                    OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputTextDelta);
                    if (doc.RootElement.TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.String)
                    {
                        msg.Text = delta.GetString();
                    }

                    return msg;
                }

            case "response.audio_transcript.delta":
                {
                    OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputAudioTranscriptionDelta);
                    if (doc.RootElement.TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.String)
                    {
                        msg.Text = delta.GetString();
                    }

                    return msg;
                }

            case "response.audio.delta":
                {
                    OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputAudioDelta);
                    if (doc.RootElement.TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.String)
                    {
                        msg.Audio = delta.GetString();
                    }

                    return msg;
                }

            case "response.done":
                return new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone };

            case "output_audio_buffer.cleared":
                {
                    InterruptedRealtimeServerMessage msg = new();
                    if (doc.RootElement.TryGetProperty("response_id", out JsonElement rid) && rid.ValueKind == JsonValueKind.String)
                    {
                        msg.InterruptedResponseId = rid.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("audio_end_ms", out JsonElement end) && end.ValueKind == JsonValueKind.Number && end.TryGetInt64(out long ms))
                    {
                        msg.OutputAudioOffsetInBytes = ms;
                    }

                    return msg;
                }

            default:
                return new RealtimeServerMessage { Type = new RealtimeServerMessageType(typeName) };
        }
    }
}
