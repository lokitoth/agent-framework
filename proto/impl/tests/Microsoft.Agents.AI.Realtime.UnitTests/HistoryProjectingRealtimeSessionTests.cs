// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class HistoryProjectingRealtimeSessionTests
{
    [Fact]
    public async Task ResponseOutputItemDone_AppendsItemToHistory()
    {
        TestRealtimeAgent inner = new();
        await using RealtimeSession provider = await inner.ConnectSessionAsync();
        HistoryProjectingRealtimeSession projecting = new(provider);

        FakeRealtimeClientSession fake = (FakeRealtimeClientSession)inner.Client.CreatedSessions.Single();
        RealtimeConversationItem item = new([new TextContent("hello")], id: "item-1", role: ChatRole.Assistant);
        ResponseOutputItemRealtimeServerMessage doneMsg = new(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            Item = item,
        };
        await fake.Enqueue(doneMsg);
        fake.CompleteInbound();

        await foreach (RealtimeServerMessage _ in projecting.GetStreamingResponseAsync())
        {
            // drain
        }

        Assert.Single(projecting.History);
        Assert.Same(item, projecting.History[0]);
    }

    [Fact]
    public async Task ResponseOutputItemAdded_DoesNotAppendToHistory()
    {
        TestRealtimeAgent inner = new();
        await using RealtimeSession provider = await inner.ConnectSessionAsync();
        HistoryProjectingRealtimeSession projecting = new(provider);

        FakeRealtimeClientSession fake = (FakeRealtimeClientSession)inner.Client.CreatedSessions.Single();
        RealtimeConversationItem item = new([new TextContent("partial")], id: "item-1", role: ChatRole.Assistant);
        ResponseOutputItemRealtimeServerMessage addedMsg = new(RealtimeServerMessageType.ResponseOutputItemAdded)
        {
            Item = item,
        };
        await fake.Enqueue(addedMsg);
        fake.CompleteInbound();

        await foreach (RealtimeServerMessage _ in projecting.GetStreamingResponseAsync())
        {
            // drain
        }

        Assert.Empty(projecting.History);
    }

    [Fact]
    public async Task NonItemMessage_PassesThroughWithoutChangingHistory()
    {
        TestRealtimeAgent inner = new();
        await using RealtimeSession provider = await inner.ConnectSessionAsync();
        HistoryProjectingRealtimeSession projecting = new(provider);

        FakeRealtimeClientSession fake = (FakeRealtimeClientSession)inner.Client.CreatedSessions.Single();
        OutputTextAudioRealtimeServerMessage delta = new(RealtimeServerMessageType.OutputTextDelta)
        {
            Text = "hi",
        };
        await fake.Enqueue(delta);
        fake.CompleteInbound();

        int yielded = 0;
        await foreach (RealtimeServerMessage msg in projecting.GetStreamingResponseAsync())
        {
            yielded++;
            Assert.Same(delta, msg);
        }

        Assert.Equal(1, yielded);
        Assert.Empty(projecting.History);
    }
}
