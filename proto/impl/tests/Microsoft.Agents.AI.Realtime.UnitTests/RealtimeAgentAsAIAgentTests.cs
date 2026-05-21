// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class RealtimeAgentAsAIAgentTests
{
    [Fact]
    public void Ctor_NullAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RealtimeAgentAsAIAgent(null!));
    }

    [Fact]
    public void IdNameDescription_ProxyToInnerAgent()
    {
        TestRealtimeAgent inner = new(name: "n", id: "id-7");
        RealtimeAgentAsAIAgent bridge = new(inner);

        Assert.Equal("id-7", bridge.Id);
        Assert.Equal("n", bridge.Name);
        Assert.Same(inner, bridge.RealtimeAgent);
    }

    [Fact]
    public async Task SessionApis_Throw_NotSupported()
    {
        RealtimeAgentAsAIAgent bridge = new(new TestRealtimeAgent());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await bridge.CreateSessionAsync());
    }

    [Fact]
    public async Task RunAsync_DrainsTextDeltas_UntilResponseDone()
    {
        TestRealtimeAgent inner = new(name: "echo");
        inner.Client.SessionFactory = options =>
        {
            FakeRealtimeClientSession session = new(options);
            _ = session.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { Text = "Hel" });
            _ = session.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { Text = "lo!" });
            _ = session.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta) { Audio = "QUJD" });
            _ = session.Enqueue(new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseDone));
            return session;
        };

        RealtimeAgentAsAIAgent bridge = new(inner);
        AgentResponse response = await bridge.RunAsync("hi");

        Assert.Equal("Hello!", response.Text);
        Assert.Equal(bridge.Id, response.AgentId);

        Assert.NotNull(response.AdditionalProperties);
        Assert.True(response.AdditionalProperties!.TryGetValue(RealtimeAgentAsAIAgent.AudioAdditionalPropertyKey, out object? audioObj));
        IList<string> audio = Assert.IsType<List<string>>(audioObj);
        Assert.Single(audio);
        Assert.Equal("QUJD", audio[0]);

        FakeRealtimeClientSession created = (FakeRealtimeClientSession)inner.Client.CreatedSessions.Single();
        Assert.Equal(2, created.SentMessages.Count);
        Assert.Contains(created.SentMessages, m => m is CreateConversationItemRealtimeClientMessage);
        Assert.Contains(created.SentMessages, m => m is CreateResponseRealtimeClientMessage);
    }

    [Fact]
    public async Task RunStreamingAsync_YieldsTextDeltas()
    {
        TestRealtimeAgent inner = new();
        inner.Client.SessionFactory = options =>
        {
            FakeRealtimeClientSession session = new(options);
            _ = session.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { Text = "A" });
            _ = session.Enqueue(new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { Text = "B" });
            _ = session.Enqueue(new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseDone));
            return session;
        };

        RealtimeAgentAsAIAgent bridge = new(inner);

        List<string> chunks = [];
        await foreach (AgentResponseUpdate update in bridge.RunStreamingAsync("go"))
        {
            chunks.Add(update.Text ?? string.Empty);
        }

        Assert.Equal(["A", "B"], chunks);
    }
}
