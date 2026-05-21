// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Foundry;

/// <summary>
/// Encodes <see cref="RealtimeClientMessage"/> instances into the Azure
/// VoiceLive wire JSON format (the OpenAI Realtime shape).
/// </summary>
/// <remarks>
/// MEAI's <see cref="RealtimeClientMessage"/> hierarchy carries no <c>type</c>
/// discriminator on the wire, so each provider must encode it themselves. This
/// encoder maps the AF/MEAI message subtype to its VoiceLive event name and
/// emits a minimal payload sufficient for the proto's unit-test surface.
/// </remarks>
[Experimental("MEAIREALTIME001")]
internal static class FoundryClientMessageEncoder
{
    internal static string Encode(RealtimeClientMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        JsonObject obj = new()
        {
            ["type"] = WireTypeFor(message),
        };

        if (message.MessageId is { } id)
        {
            obj["event_id"] = id;
        }

        switch (message)
        {
            case SessionUpdateRealtimeClientMessage sessionUpdate:
                obj["session"] = JsonSerializer.SerializeToNode(sessionUpdate.Options, sessionUpdate.Options.GetType(), RealtimeAgentJsonUtilities.DefaultOptions);
                break;

            case InputAudioBufferAppendRealtimeClientMessage append:
                obj["audio"] = Convert.ToBase64String(append.Content.Data.Span);
                break;

            case CreateConversationItemRealtimeClientMessage createItem:
                obj["item"] = JsonSerializer.SerializeToNode(createItem.Item, createItem.Item.GetType(), RealtimeAgentJsonUtilities.DefaultOptions);
                break;

            case CreateResponseRealtimeClientMessage createResponse:
                obj["response"] = JsonSerializer.SerializeToNode(createResponse, typeof(CreateResponseRealtimeClientMessage), RealtimeAgentJsonUtilities.DefaultOptions);
                break;

            default:
                break;
        }

        return obj.ToJsonString();
    }

    private static string WireTypeFor(RealtimeClientMessage message) => message switch
    {
        SessionUpdateRealtimeClientMessage => "session.update",
        InputAudioBufferAppendRealtimeClientMessage => "input_audio_buffer.append",
        InputAudioBufferCommitRealtimeClientMessage => "input_audio_buffer.commit",
        CreateConversationItemRealtimeClientMessage => "conversation.item.create",
        CreateResponseRealtimeClientMessage => "response.create",
        CancelResponseRealtimeClientMessage => "response.cancel",
        _ => message.GetType().Name,
    };
}
