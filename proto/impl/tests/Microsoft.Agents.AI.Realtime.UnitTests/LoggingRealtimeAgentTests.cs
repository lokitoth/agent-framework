// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class LoggingRealtimeAgentTests
{
    [Fact]
    public async Task ConnectAsync_LogsLifecycle_AtDebugLevel()
    {
        RecordingLogger logger = new(LogLevel.Debug);
        TestRealtimeAgent inner = new();
        LoggingRealtimeAgent agent = new(inner, logger);

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Assert.Contains(logger.Entries, e => e.Message.Contains("connecting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.Entries, e => e.Message.Contains("connected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DescribeClientMessage_RedactsAudioBytes_AsLength()
    {
        byte[] payload = new byte[1024];
        InputAudioBufferAppendRealtimeClientMessage msg = new(new DataContent(payload, "audio/pcm"));

        string description = LoggingRealtimeSession.DescribeClientMessage(msg);

        Assert.Equal("InputAudioBufferAppend(Audio(length=1024))", description);
        Assert.DoesNotContain("AAAA", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeServerMessage_RedactsAudioPayload_AsLength()
    {
        OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputAudioDelta)
        {
            Audio = "QUFBQQ==", // 4 bytes base64.
        };

        string description = LoggingRealtimeSession.DescribeServerMessage(msg);

        Assert.Contains("Audio(length=8)", description, StringComparison.Ordinal);
        Assert.DoesNotContain("QUFBQQ", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeServerMessage_NonAudio_LeavesUnchanged()
    {
        ResponseOutputItemRealtimeServerMessage msg = new(RealtimeServerMessageType.ResponseOutputItemDone);
        string description = LoggingRealtimeSession.DescribeServerMessage(msg);
        Assert.Contains("ResponseOutputItem", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_LogsClientMessage_AtTraceLevel()
    {
        RecordingLogger logger = new(LogLevel.Trace);
        TestRealtimeAgent inner = new();
        LoggingRealtimeAgent agent = new(inner, logger);

        await using RealtimeSession session = await agent.ConnectSessionAsync();
        await session.AppendInputAudioAsync(new DataContent(new byte[64], "audio/pcm"));

        Assert.Contains(logger.Entries, e => e.Message.Contains("SendAsync", StringComparison.Ordinal)
            && e.Message.Contains("Audio(length=64)", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveLogger_PrefersExplicitLogger()
    {
        RecordingLogger explicitLogger = new(LogLevel.Debug);
        ILogger resolved = LoggingRealtimeAgent.ResolveLogger(explicitLogger, factory: null);
        Assert.Same(explicitLogger, resolved);
    }

    [Fact]
    public void ResolveLogger_FallsBackToNullLogger()
    {
        ILogger resolved = LoggingRealtimeAgent.ResolveLogger(explicitLogger: null, factory: null);
        Assert.Same(NullLogger.Instance, resolved);
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly LogLevel _minLevel;

        public RecordingLogger(LogLevel minLevel)
        {
            this._minLevel = minLevel;
        }

        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= this._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!this.IsEnabled(logLevel))
            {
                return;
            }

            this.Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
