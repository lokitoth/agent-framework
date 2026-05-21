// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class RealtimeAgentBuilderTests
{
    [Fact]
    public void Ctor_NullInnerAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RealtimeAgentBuilder((RealtimeAgent)null!));
    }

    [Fact]
    public void Ctor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RealtimeAgentBuilder((Func<IServiceProvider, RealtimeAgent>)null!));
    }

    [Fact]
    public void Build_WithoutUse_ReturnsInnerAgent()
    {
        TestRealtimeAgent inner = new();
        RealtimeAgent built = new RealtimeAgentBuilder(inner).Build();
        Assert.Same(inner, built);
    }

    [Fact]
    public void Use_NullFactory_Throws()
    {
        RealtimeAgentBuilder builder = new(new TestRealtimeAgent());
        Assert.Throws<ArgumentNullException>(() => builder.Use((Func<RealtimeAgent, RealtimeAgent>)null!));
        Assert.Throws<ArgumentNullException>(() => builder.Use((Func<RealtimeAgent, IServiceProvider, RealtimeAgent>)null!));
    }

    [Fact]
    public void Build_NullReturningFactory_Throws()
    {
        TestRealtimeAgent inner = new();
        RealtimeAgentBuilder builder = new(inner);
        builder.Use(_ => null!);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("returned null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Use_AppliesFactoriesInReverseOrder_FirstUseIsOutermost()
    {
        TestRealtimeAgent inner = new();
        TrackingDelegatingRealtimeAgent? layerA = null;
        TrackingDelegatingRealtimeAgent? layerB = null;

        RealtimeAgent built = new RealtimeAgentBuilder(inner)
            .Use(agent => layerA = new TrackingDelegatingRealtimeAgent(agent, "A"))
            .Use(agent => layerB = new TrackingDelegatingRealtimeAgent(agent, "B"))
            .Build();

        Assert.NotNull(layerA);
        Assert.NotNull(layerB);
        Assert.Same(layerA, built);
        Assert.Same(layerB, layerA!.InnerAgentForTest);
        Assert.Same(inner, layerB!.InnerAgentForTest);
    }

    [Fact]
    public async Task Build_ConnectsThroughEntireChain()
    {
        TestRealtimeAgent inner = new();
        RealtimeAgent built = new RealtimeAgentBuilder(inner)
            .Use(agent => new TrackingDelegatingRealtimeAgent(agent, "outer"))
            .Build();

        await using RealtimeSession session = await built.ConnectSessionAsync();
        Assert.Single(inner.Client.CreatedSessions);
    }

    [Experimental("MEAIREALTIME001")]
    private sealed class TrackingDelegatingRealtimeAgent : DelegatingRealtimeAgent
    {
        public TrackingDelegatingRealtimeAgent(RealtimeAgent innerAgent, string tag)
            : base(innerAgent)
        {
            this.Tag = tag;
        }

        public string Tag { get; }

        public RealtimeAgent InnerAgentForTest => this.InnerAgent;
    }
}
