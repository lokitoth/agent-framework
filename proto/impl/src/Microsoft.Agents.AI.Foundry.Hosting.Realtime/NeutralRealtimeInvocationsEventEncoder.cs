// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI.Realtime.Hosting;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime;

/// <summary>
/// Control comparator encoder that emits a neutral
/// <c>text.delta</c> / <c>text.done</c> / <c>done</c> SSE shape, as called
/// out in plan §5.2.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class NeutralRealtimeInvocationsEventEncoder : IRealtimeEventEncoder
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
        if (message is OutputTextAudioRealtimeServerMessage textAudio
            && (textAudio.Type == RealtimeServerMessageType.OutputTextDelta
                || textAudio.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
            && textAudio.Text is { } delta)
        {
            return new JsonObject
            {
                ["type"] = "text.delta",
                ["delta"] = delta,
            };
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
