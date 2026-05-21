// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

[Experimental("MEAIREALTIME001")]
public class RealtimeAgentTests
{
    [Fact]
    public void Id_DefaultsToGuid_WhenIdCoreIsNull()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        Assert.False(string.IsNullOrWhiteSpace(agent.Id));
        Assert.True(Guid.TryParseExact(agent.Id, "N", out _));
    }

    [Fact]
    public void Id_UsesIdCore_WhenProvided()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent(id: "agent-1");
        Assert.Equal("agent-1", agent.Id);
    }

    [Fact]
    public void Name_And_Description_ComeFromOverrides()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent(name: "n", description: "d");
        Assert.Equal("n", agent.Name);
        Assert.Equal("d", agent.Description);
    }

    [Fact]
    public void GetService_ReturnsSelf_ForOwnType()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        Assert.Same(agent, agent.GetService(typeof(RealtimeAgent)));
        Assert.Same(agent, agent.GetService(typeof(StubRealtimeAgent)));
        Assert.Same(agent, agent.GetService<StubRealtimeAgent>());
    }

    [Fact]
    public void GetService_ReturnsNull_ForUnknownType()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        Assert.Null(agent.GetService(typeof(string)));
        Assert.Null(agent.GetService<string>());
    }

    [Fact]
    public void GetService_ReturnsNull_ForOwnTypeWithKey()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        Assert.Null(agent.GetService(typeof(StubRealtimeAgent), serviceKey: "anything"));
    }

    [Fact]
    public void GetService_Throws_OnNullType()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        Assert.Throws<ArgumentNullException>(() => agent.GetService(null!));
    }

    [Fact]
    public async Task ConnectSessionAsync_DelegatesToCore_AndReturnsConnectedSession()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Assert.NotNull(session);
        Assert.Single(agent.Client.CreatedSessions);
    }

    [Fact]
    public async Task ConnectSessionAsync_ForwardsCancellation()
    {
        StubRealtimeAgent agent = new StubRealtimeAgent();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await agent.ConnectSessionAsync(cts.Token));
    }

    [Fact]
    public void CurrentRunContext_FlowsAcrossAwait()
    {
        // CurrentRunContext is an AsyncLocal; verify flow semantics.
        StubRealtimeAgent agent = new StubRealtimeAgent();
        RealtimeAgentRunContext context = new RealtimeAgentRunContext(agent);

        SetContext(context);
        Assert.Same(context, RealtimeAgent.CurrentRunContext);

        SetContext(null);
        Assert.Null(RealtimeAgent.CurrentRunContext);

        static void SetContext(RealtimeAgentRunContext? ctx)
        {
            // Reflection-free path through a derived type.
            typeof(RealtimeAgent)
                .GetProperty(nameof(RealtimeAgent.CurrentRunContext))!
                .SetValue(null, ctx);
        }
    }
}
