// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class OpenTelemetryRealtimeAgentTests
{
    [Fact]
    public async Task ConnectAsync_EmitsConnectActivity()
    {
        string sourceName = $"otel-test-{Guid.NewGuid():N}";
        using ActivityCollector collector = new(sourceName);

        TestRealtimeAgent inner = new(name: "agent-x", id: "id-x");
        OpenTelemetryRealtimeAgent agent = new(inner, sourceName);

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Activity? connect = collector.Activities.SingleOrDefault(a => a.OperationName == "connect");
        Assert.NotNull(connect);
        Assert.Equal(ActivityKind.Client, connect!.Kind);
        Assert.Equal("connect", connect.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("id-x", connect.GetTagItem("gen_ai.agent.id"));
        Assert.Equal("agent-x", connect.GetTagItem("gen_ai.agent.name"));
        Assert.Equal(ActivityStatusCode.Ok, connect.Status);
    }

    [Fact]
    public async Task SendAndStream_EmitsSendAndStreamActivities()
    {
        string sourceName = $"otel-test-{Guid.NewGuid():N}";
        using ActivityCollector collector = new(sourceName);

        TestRealtimeAgent inner = new();
        OpenTelemetryRealtimeAgent agent = new(inner, sourceName);

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        FakeRealtimeClientSession fakeSession = (FakeRealtimeClientSession)inner.Client.CreatedSessions.Single();
        await fakeSession.Enqueue(new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone));
        fakeSession.CompleteInbound();

        await session.RequestResponseAsync();

        int count = 0;
        await foreach (RealtimeServerMessage _ in session.GetStreamingResponseAsync())
        {
            count++;
        }

        Assert.Equal(1, count);

        Activity? send = collector.Activities.SingleOrDefault(a => a.OperationName == "send");
        Assert.NotNull(send);
        Assert.Equal("CreateResponseRealtimeClientMessage", send!.GetTagItem("realtime.message.type"));

        Activity? stream = collector.Activities.SingleOrDefault(a => a.OperationName == "stream");
        Assert.NotNull(stream);
        Assert.Equal(1L, stream!.GetTagItem("realtime.messages.count"));
    }

    [Fact]
    public void DefaultSourceName_IsPublic()
    {
        Assert.Equal("Microsoft.Agents.AI.Realtime", OpenTelemetryRealtimeAgent.DefaultSourceName);
    }

    private sealed class ActivityCollector : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCollector(string sourceName)
        {
            this._listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => this.Activities.Add(activity),
            };
            ActivitySource.AddActivityListener(this._listener);
        }

        public ConcurrentBag<Activity> Activities { get; } = [];

        public void Dispose() => this._listener.Dispose();
    }
}
