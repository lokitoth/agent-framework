// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.OpenAI.UnitTests;

[Experimental("MEAIREALTIME001")]
public class OpenAIRealtimeAgentTests
{
    [Fact]
    public void Ctor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenAIRealtimeAgent(null!));
    }

    [Fact]
    public void NameDescription_Default_Null()
    {
        OpenAIRealtimeAgent agent = new(new FakeRealtimeClient());

        Assert.Null(agent.Name);
        Assert.Null(agent.Description);
    }

    [Fact]
    public void NameDescription_Reflect_Options()
    {
        OpenAIRealtimeAgentOptions options = new() { Name = "voice", Description = "test agent" };
        OpenAIRealtimeAgent agent = new(new FakeRealtimeClient(), options);

        Assert.Equal("voice", agent.Name);
        Assert.Equal("test agent", agent.Description);
    }

    [Fact]
    public async Task ConnectSessionAsync_Returns_OpenAIRealtimeSession()
    {
        FakeRealtimeClient client = new();
        OpenAIRealtimeAgent agent = new(client);

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Assert.IsType<OpenAIRealtimeSession>(session);
        Assert.Single(client.CreatedSessions);
    }

    [Fact]
    public async Task ConnectSessionAsync_PassesSessionOptionsToClient()
    {
        RealtimeSessionOptions sessionOptions = new() { Instructions = "be helpful" };
        FakeRealtimeClient client = new();
        OpenAIRealtimeAgent agent = new(client, new OpenAIRealtimeAgentOptions { SessionOptions = sessionOptions });

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        FakeRealtimeClientSession created = client.CreatedSessions.Single();
        Assert.Same(sessionOptions, created.Options);
    }

    [Fact]
    public async Task RoundTrip_Via_Fake_ValidatesPlanSection44Criterion()
    {
        // Plan §4.4 validation criterion: open a session against the
        // FakeRealtimeClient, send a SessionUpdate, receive a server message,
        // and dispose cleanly.
        FakeRealtimeClient client = new();
        RealtimeSessionOptions sessionOptions = new() { Instructions = "round-trip" };
        OpenAIRealtimeAgent agent = new(client, new OpenAIRealtimeAgentOptions { SessionOptions = sessionOptions });

        await using RealtimeSession session = await agent.ConnectSessionAsync();
        FakeRealtimeClientSession fake = client.CreatedSessions.Single();

        await session.SendAsync(new SessionUpdateRealtimeClientMessage(sessionOptions));

        OutputTextAudioRealtimeServerMessage delta = new(RealtimeServerMessageType.OutputTextDelta) { Text = "hi" };
        await fake.Enqueue(delta);
        fake.CompleteInbound();

        List<RealtimeServerMessage> received = new();
        await foreach (RealtimeServerMessage msg in session.GetStreamingResponseAsync())
        {
            received.Add(msg);
        }

        Assert.Single(received);
        Assert.Same(delta, received[0]);
        Assert.Single(fake.SentMessages);
        Assert.IsType<SessionUpdateRealtimeClientMessage>(fake.SentMessages.Single());
    }

    [Fact]
    public void GetService_ReturnsAgent_AndClient()
    {
        FakeRealtimeClient client = new();
        OpenAIRealtimeAgent agent = new(client);

        Assert.Same(agent, agent.GetService(typeof(OpenAIRealtimeAgent)));
        Assert.Same(client, agent.GetService(typeof(IRealtimeClient)));
        Assert.Null(agent.GetService(typeof(string)));
    }

    [Fact]
    public void GetService_WithKey_ReturnsNull()
    {
        FakeRealtimeClient client = new();
        OpenAIRealtimeAgent agent = new(client);

        Assert.Null(agent.GetService(typeof(OpenAIRealtimeAgent), "k"));
    }

    [Fact]
    public void GetService_NullType_Throws()
    {
        OpenAIRealtimeAgent agent = new(new FakeRealtimeClient());

        Assert.Throws<ArgumentNullException>(() => agent.GetService(null!));
    }
}
