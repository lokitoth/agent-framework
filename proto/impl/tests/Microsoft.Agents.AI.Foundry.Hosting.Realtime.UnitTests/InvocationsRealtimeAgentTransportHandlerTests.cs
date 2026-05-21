// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Foundry.Hosting.Realtime;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
internal sealed class TestRealtimeAgent : RealtimeAgent
{
    public TestRealtimeAgent(FakeRealtimeClient? client = null)
    {
        this.Client = client ?? new FakeRealtimeClient();
    }

    public FakeRealtimeClient Client { get; }

    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        IRealtimeClientSession inner = await this.Client.CreateSessionAsync(options: null, cancellationToken).ConfigureAwait(false);
        return new TestRealtimeSession(inner);
    }
}

[Experimental("MEAIREALTIME001")]
internal sealed class TestRealtimeSession : RealtimeSession
{
    public TestRealtimeSession(IRealtimeClientSession inner) : base(inner) { }
}

[Experimental("MEAIREALTIME001")]
internal sealed class FakeInvocationsSink : IInvocationsRequestSink
{
    public string? SessionId { get; set; } = "sess-1";

    public string? InvocationId { get; set; } = "inv-1";

    public Channel<RealtimeClientMessage?> Inbound { get; } = Channel.CreateUnbounded<RealtimeClientMessage?>();

    public ConcurrentQueue<string> SseFrames { get; } = new();

    public bool Completed { get; private set; }

    public async Task<RealtimeClientMessage?> ReadNextClientMessageAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await this.Inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public Task WriteSseFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        this.SseFrames.Enqueue(Encoding.UTF8.GetString(frame.Span));
        return Task.CompletedTask;
    }

    public Task CompleteAsync(CancellationToken cancellationToken)
    {
        this.Completed = true;
        return Task.CompletedTask;
    }
}

[Experimental("MEAIREALTIME001")]
public class InvocationsRealtimeAgentTransportHandlerTests
{
    [Fact]
    public void Ctor_NullEncoder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InvocationsRealtimeAgentTransportHandler(null!));
    }

    [Fact]
    public async Task RunAsync_EncodesServerMessages_AsSseFrames()
    {
        TestRealtimeAgent agent = new();
        FakeInvocationsSink sink = new();
        InvocationsRealtimeAgentTransportHandler handler = new(new VoiceLiveInvocationsEventEncoder());

        Task run = handler.RunAsync(agent, sink, CancellationToken.None);

        await WaitForAsync(() => agent.Client.CreatedSessions.Count == 1);
        FakeRealtimeClientSession session = agent.Client.CreatedSessions.First();

        await session.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioTranscriptionDelta) { Text = "hi" });
        await session.Enqueue(new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone });
        session.CompleteInbound();
        sink.Inbound.Writer.TryComplete();

        await run;

        Assert.True(sink.Completed);
        Assert.Equal(2, sink.SseFrames.Count);
        Assert.Contains(sink.SseFrames, f => f.Contains("output_audio_transcription.delta"));
        Assert.Contains(sink.SseFrames, f => f.Contains("\"type\":\"done\""));
    }

    [Fact]
    public async Task RunAsync_ForwardsInboundClientMessages_ToSession()
    {
        TestRealtimeAgent agent = new();
        FakeInvocationsSink sink = new();
        InvocationsRealtimeAgentTransportHandler handler = new(new VoiceLiveInvocationsEventEncoder());

        Task run = handler.RunAsync(agent, sink, CancellationToken.None);
        await WaitForAsync(() => agent.Client.CreatedSessions.Count == 1);
        FakeRealtimeClientSession session = agent.Client.CreatedSessions.First();

        await sink.Inbound.Writer.WriteAsync(new SessionUpdateRealtimeClientMessage(new RealtimeSessionOptions()));
        await WaitForAsync(() => session.SentMessages.Count == 1);

        sink.Inbound.Writer.TryComplete();
        session.CompleteInbound();
        await run;

        Assert.Single(session.SentMessages);
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        int waited = 0;
        while (!predicate() && waited < timeoutMs)
        {
            await Task.Delay(20).ConfigureAwait(false);
            waited += 20;
        }

        Assert.True(predicate(), "Condition never became true within timeout.");
    }
}

[Experimental("MEAIREALTIME001")]
public class FoundryRealtimeServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFoundryRealtime_RegistersExpectedServices()
    {
        ServiceCollection services = new();
        services.AddFoundryRealtime();
        ServiceProvider sp = services.BuildServiceProvider();

        Assert.IsType<VoiceLiveInvocationsEventEncoder>(sp.GetRequiredService<Microsoft.Agents.AI.Realtime.Hosting.IRealtimeEventEncoder>());
        Assert.NotNull(sp.GetRequiredService<InvocationsRealtimeAgentTransportHandler>());
    }

    [Fact]
    public void AddFoundryRealtime_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Microsoft.Agents.AI.Foundry.Hosting.Realtime.FoundryRealtimeServiceCollectionExtensions.AddFoundryRealtime(null!));
    }
}
