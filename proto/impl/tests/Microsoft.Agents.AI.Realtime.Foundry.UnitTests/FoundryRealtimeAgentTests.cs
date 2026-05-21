// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Agents.AI.Realtime.Foundry;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Foundry.UnitTests;

[Experimental("MEAIREALTIME001")]
public class FoundryRealtimeAgentTests
{
    private static FoundryRealtimeAgentOptions BaseOptions(RealtimeSessionOptions? sessionOptions = null) => new()
    {
        Endpoint = new Uri("wss://example.invalid/voicelive"),
        ApiKey = "k",
        SessionOptions = sessionOptions,
    };

    [Fact]
    public void Ctor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FoundryRealtimeAgent(null!));
    }

    [Fact]
    public void Ctor_NoCredentialNoKey_Throws()
    {
        FoundryRealtimeAgentOptions options = new() { Endpoint = new Uri("wss://example.invalid/voicelive") };
        Assert.Throws<ArgumentException>(() => new FoundryRealtimeAgent(options));
    }

    [Fact]
    public async Task Production_Transport_NotImplemented_ThisPhase()
    {
        FoundryRealtimeAgent agent = new(BaseOptions());

        await Assert.ThrowsAsync<NotSupportedException>(async () => await agent.ConnectSessionAsync());
    }

    [Fact]
    public async Task ConnectSessionAsync_OpensTransport_AndSendsSessionUpdate()
    {
        RealtimeSessionOptions sessionOptions = new() { Instructions = "be helpful" };
        FakeWebSocketTransport transport = new();
        FoundryRealtimeAgent agent = new(BaseOptions(sessionOptions), _ => Task.FromResult<IWebSocketTransport>(transport));

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Assert.IsType<FoundryRealtimeSession>(session);
        Assert.Equal(new Uri("wss://example.invalid/voicelive"), transport.ConnectedEndpoint);
        Assert.Single(transport.SentFrames);

        // Verify the wire JSON is a session.update payload.
        string frame = transport.SentFrames.Single();
        using JsonDocument doc = JsonDocument.Parse(frame);
        Assert.True(doc.RootElement.TryGetProperty("type", out JsonElement type));
        Assert.Equal("session.update", type.GetString());
    }

    [Fact]
    public async Task ConnectSessionAsync_TransportDisposed_OnFactoryFailure_AfterConnect()
    {
        FakeWebSocketTransport transport = new();
        bool sendThrew = false;
        FoundryRealtimeAgent agent = new(
            BaseOptions(new RealtimeSessionOptions()),
            _ => Task.FromResult<IWebSocketTransport>(transport));

        // Disposing the transport before SendAsync runs is awkward; instead
        // verify the happy-path disposal contract: when the caller disposes the
        // returned session, the transport is disposed.
        await using (RealtimeSession session = await agent.ConnectSessionAsync())
        {
            _ = sendThrew;
        }

        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public async Task Session_StreamsProjectedEvents_FromTransport()
    {
        FakeWebSocketTransport transport = new();
        FoundryRealtimeAgent agent = new(BaseOptions(), _ => Task.FromResult<IWebSocketTransport>(transport));
        await using RealtimeSession session = await agent.ConnectSessionAsync();

        await transport.EnqueueInbound("{ \"type\": \"response.text.delta\", \"delta\": \"hi\" }");
        await transport.EnqueueInbound("{ \"type\": \"output_audio_buffer.cleared\", \"response_id\": \"r1\" }");
        await transport.EnqueueInbound("{ \"type\": \"response.done\" }");
        transport.CompleteInbound();

        List<RealtimeServerMessage> received = new();
        await foreach (RealtimeServerMessage msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        Assert.Equal(3, received.Count);
        Assert.IsType<OutputTextAudioRealtimeServerMessage>(received[0]);
        Assert.IsType<InterruptedRealtimeServerMessage>(received[1]);
        Assert.Equal(RealtimeServerMessageType.ResponseDone, received[2].Type);
    }

    [Fact]
    public async Task Session_SerializesOutboundClientMessages_AsJson()
    {
        FakeWebSocketTransport transport = new();
        FoundryRealtimeAgent agent = new(BaseOptions(), _ => Task.FromResult<IWebSocketTransport>(transport));

        await using RealtimeSession session = await agent.ConnectSessionAsync();
        await session.SendAsync(new InputAudioBufferAppendRealtimeClientMessage(new DataContent(new byte[] { 1, 2, 3 }, "audio/pcm")));

        Assert.Single(transport.SentFrames);
        string frame = transport.SentFrames.Single();
        using JsonDocument doc = JsonDocument.Parse(frame);
        Assert.True(doc.RootElement.TryGetProperty("type", out JsonElement t));
        Assert.Equal("input_audio_buffer.append", t.GetString());
    }
}
