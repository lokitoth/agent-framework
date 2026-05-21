// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI.Realtime.Hosting;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime;

/// <summary>
/// Emits the VoiceLive-shaped SSE event JSON the Python sample hand-codes
/// (<c>vl_sample/hello-world-invocations-voicelive/main.py</c>): each line of
/// the form <c>data: {json}\n\n</c> with event types like
/// <c>output_audio_transcription.delta</c>, <c>output_audio_transcription.done</c>,
/// and a trailing <c>done</c> sentinel.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class VoiceLiveInvocationsEventEncoder : IRealtimeEventEncoder
{
    /// <inheritdoc />
    public int Encode(RealtimeServerMessage message, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);

        JsonObject? body = BuildBody(message);
        if (body is null)
        {
            return 0;
        }

        string payload = "data: " + body.ToJsonString() + "\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        destination.Write(bytes, 0, bytes.Length);
        return bytes.Length;
    }

    private static JsonObject? BuildBody(RealtimeServerMessage message)
    {
        if (message is OutputTextAudioRealtimeServerMessage textAudio)
        {
            if (textAudio.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta && textAudio.Text is { } delta)
            {
                return new JsonObject
                {
                    ["type"] = "output_audio_transcription.delta",
                    ["delta"] = delta,
                };
            }

            if (textAudio.Type == RealtimeServerMessageType.OutputTextDelta && textAudio.Text is { } textDelta)
            {
                return new JsonObject
                {
                    ["type"] = "output_audio_transcription.delta",
                    ["delta"] = textDelta,
                };
            }
        }

        if (message.Type == RealtimeServerMessageType.ResponseDone)
        {
            return new JsonObject
            {
                ["type"] = "done",
            };
        }

        return null;
    }
}
