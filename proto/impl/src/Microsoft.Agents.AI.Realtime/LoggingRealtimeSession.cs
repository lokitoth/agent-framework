// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating <see cref="RealtimeSession"/> that logs send and stream
/// operations. Audio buffer payloads are redacted as <c>Audio(length=N)</c>.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class LoggingRealtimeSession : DelegatingRealtimeSession
{
    private readonly ILogger _logger;

    public LoggingRealtimeSession(RealtimeSession innerRealtimeSession, ILogger logger)
        : base(innerRealtimeSession)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (this._logger.IsEnabled(LogLevel.Trace))
        {
            this._logger.LogTrace("Realtime session SendAsync: {Description}", DescribeClientMessage(message));
        }
        else if (this._logger.IsEnabled(LogLevel.Debug))
        {
            this._logger.LogDebug("Realtime session SendAsync: {MessageType}", message.GetType().Name);
        }

        return base.SendAsync(message, cancellationToken);
    }

    public override IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(CancellationToken cancellationToken = default)
        => this.EnumerateAndLogAsync(base.GetStreamingResponseAsync(cancellationToken), cancellationToken);

    private async IAsyncEnumerable<RealtimeServerMessage> EnumerateAndLogAsync(
        IAsyncEnumerable<RealtimeServerMessage> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (RealtimeServerMessage message in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (this._logger.IsEnabled(LogLevel.Trace))
            {
                this._logger.LogTrace("Realtime session received: {Description}", DescribeServerMessage(message));
            }
            else if (this._logger.IsEnabled(LogLevel.Debug))
            {
                this._logger.LogDebug("Realtime session received: {MessageType} ({TypeKind})", message.GetType().Name, message.Type);
            }

            yield return message;
        }
    }

    internal static string DescribeClientMessage(RealtimeClientMessage message)
    {
        if (message is InputAudioBufferAppendRealtimeClientMessage audio)
        {
            int byteLength = audio.Content?.Data.Length ?? 0;
            return $"InputAudioBufferAppend(Audio(length={byteLength}))";
        }

        return message.GetType().Name;
    }

    internal static string DescribeServerMessage(RealtimeServerMessage message)
    {
        if (message is OutputTextAudioRealtimeServerMessage outputAudio
            && outputAudio.Audio is { Length: > 0 })
        {
            return $"{message.GetType().Name}({message.Type}; Audio(length={outputAudio.Audio.Length}))";
        }

        return $"{message.GetType().Name}({message.Type})";
    }
}
