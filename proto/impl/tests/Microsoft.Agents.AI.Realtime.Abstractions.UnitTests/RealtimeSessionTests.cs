// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

[Experimental("MEAIREALTIME001")]
public class RealtimeSessionTests
{
    private static StubRealtimeSession CreateSession(out FakeRealtimeClientSession inner, RealtimeSessionOptions? options = null)
    {
        inner = new FakeRealtimeClientSession(options);
        return new StubRealtimeSession(inner);
    }

    [Fact]
    public void Ctor_Throws_OnNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new StubRealtimeSession(null!));
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);

        await session.DisposeAsync();
        Assert.True(inner.IsDisposed);

        // Second dispose should not throw.
        await session.DisposeAsync();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_SingleConsumer_SecondCallThrows()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);
        inner.CompleteInbound();

        // First enumeration drains cleanly.
        _ = await RealtimeServerMessageRecorder.DrainAsync(session.GetStreamingResponseAsync());

        // Second enumeration must throw (ADR-002).
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await RealtimeServerMessageRecorder.DrainAsync(session.GetStreamingResponseAsync()));
    }

    [Fact]
    public async Task History_StartsEmpty_AndIsReadOnly()
    {
        StubRealtimeSession session = CreateSession(out _);
        Assert.Empty(session.History);

        // The public surface returns IReadOnlyList — mutation goes through the
        // protected mutator surface (exposed via Stub helpers below).
        RealtimeConversationItem item = new RealtimeConversationItem(System.Array.Empty<AIContent>(), id: "a", role: ChatRole.User);
        session.AddItem(item);
        Assert.Same(item, Assert.Single(session.History));

        RealtimeConversationItem replacement = new RealtimeConversationItem(System.Array.Empty<AIContent>(), id: "a", role: ChatRole.Assistant);
        session.ReplaceItem(0, replacement);
        Assert.Same(replacement, session.History[0]);

        session.Clear();
        Assert.Empty(session.History);

        await session.DisposeAsync();
    }

    [Fact]
    public void Options_ReflectsInnerOptions()
    {
        RealtimeSessionOptions options = new RealtimeSessionOptions { Model = "gpt-4o-realtime" };
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner, options);

        Assert.Same(options, session.Options);

        RealtimeSessionOptions updated = new RealtimeSessionOptions { Model = "gpt-4o-realtime-mini" };
        inner.SetOptions(updated);
        Assert.Same(updated, session.Options);
    }

    [Fact]
    public void ConversationId_DefaultsToNull()
    {
        StubRealtimeSession session = CreateSession(out _);
        Assert.Null(session.ConversationId);
    }

    [Fact]
    public void GetService_ReturnsSelf_ForOwnType()
    {
        StubRealtimeSession session = CreateSession(out _);
        Assert.Same(session, session.GetService(typeof(RealtimeSession)));
        Assert.Same(session, session.GetService<RealtimeSession>());
    }

    [Fact]
    public void GetService_DelegatesToInner_ForUnknownType()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);

        // FakeRealtimeClientSession returns itself for IRealtimeClientSession.
        Assert.Same(inner, session.GetService(typeof(IRealtimeClientSession)));
    }

    [Fact]
    public void StateBag_IsAttached()
    {
        StubRealtimeSession session = CreateSession(out _);
        Assert.NotNull(session.StateBag);
        Assert.Equal(0, session.StateBag.Count);
    }

    [Fact]
    public async Task SendAsync_Throws_OnNull()
    {
        StubRealtimeSession session = CreateSession(out _);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await session.SendAsync(null!));
    }

    [Fact]
    public async Task SendAsync_ForwardsToInner()
    {
        StubRealtimeSession session = CreateSession(out FakeRealtimeClientSession inner);
        InputAudioBufferCommitRealtimeClientMessage msg = new InputAudioBufferCommitRealtimeClientMessage();

        await session.SendAsync(msg);

        Assert.Same(msg, Assert.Single(inner.SentMessages));
    }
}
