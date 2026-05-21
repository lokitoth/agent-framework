// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Bridges a <see cref="RealtimeAgent"/> to the request/response shaped
/// <see cref="AIAgent"/> surface. Each invocation opens a fresh
/// <see cref="RealtimeSession"/>, forwards the input messages, requests a
/// response, drains the stream while collecting text deltas, and surfaces
/// captured audio chunks on <see cref="AgentResponse.AdditionalProperties"/>
/// under <see cref="AudioAdditionalPropertyKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// Transcript-only per implementation-plan.md §4.2: audio is not played back,
/// only collected. The realtime <c>AgentSession</c> argument is currently
/// ignored — multi-turn realtime sessions are out of scope for this phase
/// and are tracked as a follow-up.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class RealtimeAgentAsAIAgent : AIAgent
{
    /// <summary>The key used for <see cref="AgentResponse.AdditionalProperties"/> audio capture.</summary>
    public const string AudioAdditionalPropertyKey = "realtime.audio";

    private readonly RealtimeAgent _realtimeAgent;

    /// <summary>Initializes a new instance of the <see cref="RealtimeAgentAsAIAgent"/> class.</summary>
    /// <param name="realtimeAgent">The realtime agent to bridge.</param>
    /// <exception cref="ArgumentNullException"><paramref name="realtimeAgent"/> is <see langword="null"/>.</exception>
    public RealtimeAgentAsAIAgent(RealtimeAgent realtimeAgent)
    {
        this._realtimeAgent = realtimeAgent ?? throw new ArgumentNullException(nameof(realtimeAgent));
    }

    /// <inheritdoc/>
    protected override string? IdCore => this._realtimeAgent.Id;

    /// <inheritdoc/>
    public override string? Name => this._realtimeAgent.Name;

    /// <inheritdoc/>
    public override string? Description => this._realtimeAgent.Description;

    /// <summary>Gets the wrapped <see cref="RealtimeAgent"/>.</summary>
    public RealtimeAgent RealtimeAgent => this._realtimeAgent;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null && serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return this._realtimeAgent.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{nameof(RealtimeAgentAsAIAgent)} bridges a request/response surface over an ephemeral realtime "
            + "session and does not expose multi-turn AgentSession state. Use the underlying RealtimeAgent's "
            + "ConnectSessionAsync for streaming sessions.");

    /// <inheritdoc/>
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{nameof(RealtimeAgentAsAIAgent)} does not support AgentSession serialization.");

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{nameof(RealtimeAgentAsAIAgent)} does not support AgentSession deserialization.");

    /// <inheritdoc/>
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        RealtimeSession realtime = await this._realtimeAgent.ConnectSessionAsync(cancellationToken).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable _ = realtime.ConfigureAwait(false);

        await SendChatMessagesAsync(realtime, messages, cancellationToken).ConfigureAwait(false);
        await realtime.RequestResponseAsync(cancellationToken).ConfigureAwait(false);

        StringBuilder textBuilder = new();
        List<string> audioChunks = [];

        await foreach (RealtimeServerMessage message in realtime.GetStreamingResponseAsync(cancellationToken).ConfigureAwait(false))
        {
            ProcessServerMessage(message, textBuilder, audioChunks);
            if (message.Type == RealtimeServerMessageType.ResponseDone)
            {
                break;
            }
        }

        ChatMessage responseMessage = new(ChatRole.Assistant, textBuilder.ToString());
        AgentResponse response = new(responseMessage)
        {
            AgentId = this.Id,
        };

        if (audioChunks.Count > 0)
        {
            (response.AdditionalProperties ??= []).Add(AudioAdditionalPropertyKey, audioChunks);
        }

        return response;
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        RealtimeSession realtime = await this._realtimeAgent.ConnectSessionAsync(cancellationToken).ConfigureAwait(false);
        await using ConfiguredAsyncDisposable _ = realtime.ConfigureAwait(false);

        await SendChatMessagesAsync(realtime, messages, cancellationToken).ConfigureAwait(false);
        await realtime.RequestResponseAsync(cancellationToken).ConfigureAwait(false);

        await foreach (RealtimeServerMessage message in realtime.GetStreamingResponseAsync(cancellationToken).ConfigureAwait(false))
        {
            AgentResponseUpdate? update = TryProjectToUpdate(message, this.Id);
            if (update is not null)
            {
                yield return update;
            }

            if (message.Type == RealtimeServerMessageType.ResponseDone)
            {
                break;
            }
        }
    }

    private static async Task SendChatMessagesAsync(RealtimeSession realtime, IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        foreach (ChatMessage message in messages)
        {
            if (message.Contents is null || message.Contents.Count == 0)
            {
                continue;
            }

            RealtimeConversationItem item = new([.. message.Contents], role: message.Role);
            await realtime.SendMessageAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ProcessServerMessage(RealtimeServerMessage message, StringBuilder text, List<string> audio)
    {
        if (message is OutputTextAudioRealtimeServerMessage textAudio)
        {
            if (textAudio.Type == RealtimeServerMessageType.OutputTextDelta
                || textAudio.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
            {
                if (textAudio.Text is { Length: > 0 } chunk)
                {
                    _ = text.Append(chunk);
                }
            }
            else if (textAudio.Type == RealtimeServerMessageType.OutputAudioDelta
                && textAudio.Audio is { Length: > 0 } audioChunk)
            {
                audio.Add(audioChunk);
            }
        }
    }

    private static AgentResponseUpdate? TryProjectToUpdate(RealtimeServerMessage message, string agentId)
    {
        if (message is OutputTextAudioRealtimeServerMessage textAudio)
        {
            if ((textAudio.Type == RealtimeServerMessageType.OutputTextDelta
                || textAudio.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta)
                && textAudio.Text is { Length: > 0 } chunk)
            {
                return new AgentResponseUpdate(ChatRole.Assistant, chunk) { AgentId = agentId };
            }
        }

        return null;
    }
}
