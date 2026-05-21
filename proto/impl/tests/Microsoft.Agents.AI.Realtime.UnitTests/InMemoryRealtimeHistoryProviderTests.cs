// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class InMemoryRealtimeHistoryProviderTests
{
    [Fact]
    public void GetHistory_UnknownId_ReturnsEmpty()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        Assert.Empty(provider.GetHistory("nope"));
    }

    [Fact]
    public void Append_NullItem_Throws()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        Assert.Throws<ArgumentNullException>(() => provider.Append("a", null!));
    }

    [Fact]
    public void Append_NullOrWhitespaceId_Throws()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        RealtimeConversationItem item = new([new TextContent("x")]);
        Assert.Throws<ArgumentException>(() => provider.Append("", item));
        Assert.Throws<ArgumentException>(() => provider.Append("   ", item));
    }

    [Fact]
    public void Append_Then_GetHistory_ReturnsItemsInOrder()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        RealtimeConversationItem a = new([new TextContent("a")]);
        RealtimeConversationItem b = new([new TextContent("b")]);
        provider.Append("conv-1", a);
        provider.Append("conv-1", b);

        System.Collections.Generic.IReadOnlyList<RealtimeConversationItem> history = provider.GetHistory("conv-1");
        Assert.Equal(2, history.Count);
        Assert.Same(a, history[0]);
        Assert.Same(b, history[1]);
    }

    [Fact]
    public void GetHistory_ReturnsSnapshot_NotLiveView()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        provider.Append("conv-1", new RealtimeConversationItem([new TextContent("a")]));

        System.Collections.Generic.IReadOnlyList<RealtimeConversationItem> first = provider.GetHistory("conv-1");
        provider.Append("conv-1", new RealtimeConversationItem([new TextContent("b")]));

        Assert.Single(first);
        Assert.Equal(2, provider.GetHistory("conv-1").Count);
    }

    [Fact]
    public void Clear_RemovesHistory()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        provider.Append("conv-1", new RealtimeConversationItem([new TextContent("x")]));
        Assert.True(provider.Clear("conv-1"));
        Assert.Empty(provider.GetHistory("conv-1"));
    }

    [Fact]
    public void Clear_UnknownId_ReturnsFalse()
    {
        InMemoryRealtimeHistoryProvider provider = new();
        Assert.False(provider.Clear("never-existed"));
    }
}
