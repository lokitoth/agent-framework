// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.Hosting;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.Realtime.Hosting.UnitTests;

[Experimental("MEAIREALTIME001")]
public class HostedRealtimeAgentTests
{
    [Fact]
    public void Ctor_NullContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HostedRealtimeAgent(new TestRealtimeAgent(), null!));
    }

    [Fact]
    public async Task ConnectSessionAsync_Delegates_ToInner()
    {
        TestRealtimeAgent inner = new();
        HostedRealtimeAgent hosted = new(inner, new HostedRealtimeSessionContext());

        await using RealtimeSession session = await hosted.ConnectSessionAsync();

        Assert.Single(inner.Client.CreatedSessions);
    }

    [Fact]
    public void GetService_ResolvesContext()
    {
        HostedRealtimeSessionContext ctx = new() { IsolationKey = "k1" };
        HostedRealtimeAgent hosted = new(new TestRealtimeAgent(), ctx);

        Assert.Same(ctx, hosted.GetService(typeof(HostedRealtimeSessionContext)));
    }
}

[Experimental("MEAIREALTIME001")]
public class RealtimeAgentServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRealtimeAgent_RegistersUnderKey()
    {
        ServiceCollection services = new();
        services.AddRealtimeAgent("voice", _ => new TestRealtimeAgent());

        ServiceProvider sp = services.BuildServiceProvider();
        RealtimeAgent resolved = sp.GetRequiredKeyedService<RealtimeAgent>("voice");

        Assert.IsType<HostedRealtimeAgent>(resolved);
    }

    [Fact]
    public void AddRealtimeAgent_NullGuards()
    {
        ServiceCollection services = new();

        Assert.Throws<ArgumentNullException>(() => services.AddRealtimeAgent(null!, _ => new TestRealtimeAgent()));
        Assert.Throws<ArgumentNullException>(() => services.AddRealtimeAgent("k", null!));
    }

    [Fact]
    public void AddRealtimeAgent_EachKey_IsIsolated()
    {
        ServiceCollection services = new();
        services.AddRealtimeAgent("a", _ => new TestRealtimeAgent());
        services.AddRealtimeAgent("b", _ => new TestRealtimeAgent());

        ServiceProvider sp = services.BuildServiceProvider();
        RealtimeAgent a = sp.GetRequiredKeyedService<RealtimeAgent>("a");
        RealtimeAgent b = sp.GetRequiredKeyedService<RealtimeAgent>("b");

        Assert.NotSame(a, b);
    }

    [Fact]
    public void AddRealtimeAgent_ScopedLifetime_RespectsScope()
    {
        ServiceCollection services = new();
        services.AddRealtimeAgent("v", _ => new TestRealtimeAgent(), ServiceLifetime.Scoped);

        ServiceProvider sp = services.BuildServiceProvider();
        using IServiceScope s1 = sp.CreateScope();
        using IServiceScope s2 = sp.CreateScope();
        RealtimeAgent a1 = s1.ServiceProvider.GetRequiredKeyedService<RealtimeAgent>("v");
        RealtimeAgent a2 = s2.ServiceProvider.GetRequiredKeyedService<RealtimeAgent>("v");

        Assert.NotSame(a1, a2);
    }
}

[Experimental("MEAIREALTIME001")]
public class RealtimeAgentTransportHandlerTests
{
    private sealed class CaptureEncoder : IRealtimeEventEncoder
    {
        public List<string> Events { get; } = new();

        public int Encode(RealtimeServerMessage message, Stream destination)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message.Type.Value);
            destination.Write(bytes);
            this.Events.Add(message.Type.Value);
            return bytes.Length;
        }
    }

    private sealed class FakeContext : IRealtimeAgentTransportContext
    {
        public Channel<RealtimeClientMessage?> Inbound { get; } = Channel.CreateUnbounded<RealtimeClientMessage?>();

        public ConcurrentQueue<byte[]> WrittenFrames { get; } = new();

        public HostedRealtimeSessionContext Session { get; } = new();

        public async Task<RealtimeClientMessage?> ReceiveClientMessageAsync(CancellationToken cancellationToken)
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

        public Task WriteEventAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            this.WrittenFrames.Enqueue(payload.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class DefaultHandler : RealtimeAgentTransportHandler
    {
        public DefaultHandler(IRealtimeEventEncoder encoder) : base(encoder) { }
    }

    [Fact]
    public async Task RunAsync_PumpsOutboundEvents_Through_Encoder()
    {
        FakeRealtimeClient client = new();
        TestRealtimeAgent agent = new(client);
        CaptureEncoder encoder = new();
        DefaultHandler handler = new(encoder);
        FakeContext ctx = new();

        Task run = handler.RunAsync(agent, ctx, CancellationToken.None);

        // Wait for the session to be created
        await WaitForAsync(() => client.CreatedSessions.Count == 1);
        FakeRealtimeClientSession session = client.CreatedSessions.First();

        await session.Enqueue(new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone });
        session.CompleteInbound();
        ctx.Inbound.Writer.TryComplete();

        await run;

        Assert.Contains("ResponseDone", encoder.Events);
        Assert.Single(ctx.WrittenFrames);
    }

    [Fact]
    public async Task RunAsync_PumpsInboundClientMessages_Into_Session()
    {
        FakeRealtimeClient client = new();
        TestRealtimeAgent agent = new(client);
        DefaultHandler handler = new(new CaptureEncoder());
        FakeContext ctx = new();

        Task run = handler.RunAsync(agent, ctx, CancellationToken.None);
        await WaitForAsync(() => client.CreatedSessions.Count == 1);
        FakeRealtimeClientSession session = client.CreatedSessions.First();

        SessionUpdateRealtimeClientMessage msg = new(new RealtimeSessionOptions());
        await ctx.Inbound.Writer.WriteAsync(msg);

        await WaitForAsync(() => session.SentMessages.Count == 1);

        ctx.Inbound.Writer.TryComplete();
        session.CompleteInbound();
        await run;

        Assert.Single(session.SentMessages);
    }

    [Fact]
    public async Task RunAsync_CancellationCloses_BothPumps()
    {
        FakeRealtimeClient client = new();
        TestRealtimeAgent agent = new(client);
        DefaultHandler handler = new(new CaptureEncoder());
        FakeContext ctx = new();

        using CancellationTokenSource cts = new();
        Task run = handler.RunAsync(agent, ctx, cts.Token);

        await WaitForAsync(() => client.CreatedSessions.Count == 1);
        cts.Cancel();

        // Either OperationCanceled or graceful completion is acceptable.
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    [Fact]
    public void Ctor_NullEncoder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultHandler(null!));
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
public class HostedRealtimeSessionContextTests
{
    [Fact]
    public void Defaults_AreNull()
    {
        HostedRealtimeSessionContext ctx = new();

        Assert.Null(ctx.IsolationKey);
        Assert.Null(ctx.CallerIdentity);
    }
}
