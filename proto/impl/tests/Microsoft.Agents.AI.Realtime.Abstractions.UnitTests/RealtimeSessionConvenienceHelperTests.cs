// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

[Experimental("MEAIREALTIME001")]
public class RealtimeSessionConvenienceHelperTests
{
    private static StubRealtimeSession CreateSession(out FakeRealtimeClientSession inner)
    {
        inner = new FakeRealtimeClientSession();
        return new StubRealtimeSession(inner);
    }

    [Fact]
    public async Task AppendInputAudioAsync_SendsAppendMessage()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);
        DataContent audio = new DataContent(new byte[] { 1, 2, 3 }, mediaType: "audio/pcm");

        await session.AppendInputAudioAsync(audio);

        InputAudioBufferAppendRealtimeClientMessage sent = Assert.IsType<InputAudioBufferAppendRealtimeClientMessage>(Assert.Single(inner.SentMessages));
        Assert.Same(audio, sent.Content);
    }

    [Fact]
    public async Task AppendInputAudioAsync_Throws_OnNullAudio()
    {
        StubRealtimeSession session = CreateSession(out _);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await session.AppendInputAudioAsync(null!));
    }

    [Fact]
    public async Task CommitInputAudioAsync_SendsCommitMessage()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);

        await session.CommitInputAudioAsync();

        Assert.IsType<InputAudioBufferCommitRealtimeClientMessage>(Assert.Single(inner.SentMessages));
    }

    [Fact]
    public async Task SendMessageAsync_WrapsItemInCreateConversationItemMessage()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);
        RealtimeConversationItem item = new RealtimeConversationItem(
            new AIContent[] { new TextContent("hello") },
            id: "u1",
            role: ChatRole.User);

        await session.SendMessageAsync(item);

        CreateConversationItemRealtimeClientMessage sent = Assert.IsType<CreateConversationItemRealtimeClientMessage>(Assert.Single(inner.SentMessages));
        Assert.Same(item, sent.Item);
    }

    [Fact]
    public async Task SendMessageAsync_Throws_OnNullItem()
    {
        StubRealtimeSession session = CreateSession(out _);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await session.SendMessageAsync(null!));
    }

    [Fact]
    public async Task RequestResponseAsync_SendsCreateResponseMessage()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);

        await session.RequestResponseAsync();

        Assert.IsType<CreateResponseRealtimeClientMessage>(Assert.Single(inner.SentMessages));
    }

    [Fact]
    public async Task CancelResponseAsync_SendsAfCancelMarker()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);

        await session.CancelResponseAsync();

        // Per the diary deviation note: AF defines one cancel marker that
        // providers translate. The abstractions layer just sends it as-is.
        Assert.IsType<CancelResponseRealtimeClientMessage>(Assert.Single(inner.SentMessages));
    }
}
