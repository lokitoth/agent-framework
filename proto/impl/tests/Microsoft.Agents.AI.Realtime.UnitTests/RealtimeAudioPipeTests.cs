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
public class RealtimeAudioPipeTests
{
    [Fact]
    public void Ctor_InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeAudioPipe(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeAudioPipe(-1));
    }

    [Fact]
    public async Task WriteThenRead_ReturnsChunksInOrder()
    {
        using RealtimeAudioPipe pipe = new();
        DataContent a = new(new byte[] { 1 }, "audio/pcm");
        DataContent b = new(new byte[] { 2 }, "audio/pcm");

        await pipe.WriteAsync(a);
        await pipe.WriteAsync(b);
        pipe.Complete();

        DataContent? r1 = await pipe.ReadAsync();
        DataContent? r2 = await pipe.ReadAsync();
        DataContent? r3 = await pipe.ReadAsync();

        Assert.Same(a, r1);
        Assert.Same(b, r2);
        Assert.Null(r3);
    }

    [Fact]
    public async Task PumpToAsync_ForwardsChunks_AsInputAudioBufferAppend()
    {
        TestRealtimeAgent inner = new();
        await using RealtimeSession session = await inner.ConnectSessionAsync();
        FakeRealtimeClientSession fake = (FakeRealtimeClientSession)inner.Client.CreatedSessions.Single();

        using RealtimeAudioPipe pipe = new();
        Task pump = pipe.PumpToAsync(session);

        await pipe.WriteAsync(new DataContent(new byte[] { 10, 20 }, "audio/pcm"));
        await pipe.WriteAsync(new DataContent(new byte[] { 30 }, "audio/pcm"));
        pipe.Complete();

        await pump;

        List<RealtimeClientMessage> sent = fake.SentMessages.ToList();
        Assert.Equal(2, sent.Count);
        InputAudioBufferAppendRealtimeClientMessage first = Assert.IsType<InputAudioBufferAppendRealtimeClientMessage>(sent[0]);
        Assert.Equal(2, first.Content?.Data.Length);
    }

    [Fact]
    public async Task Writer_DelegatesToPipe()
    {
        using RealtimeAudioPipe pipe = new();
        RealtimeAudioWriter writer = pipe.Writer;

        await writer.WriteAsync(new DataContent(new byte[] { 7 }, "audio/pcm"));
        writer.Complete();

        DataContent? read = await pipe.ReadAsync();
        Assert.NotNull(read);
        Assert.Equal(1, read!.Data.Length);
        Assert.Null(await pipe.ReadAsync());
    }

    [Fact]
    public async Task Dispose_CompletesPipe()
    {
        RealtimeAudioPipe pipe = new();
        pipe.Dispose();
        Assert.Null(await pipe.ReadAsync());
    }

    [Fact]
    public async Task WriteAsync_NullChunk_Throws()
    {
        using RealtimeAudioPipe pipe = new();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await pipe.WriteAsync(null!));
    }
}
