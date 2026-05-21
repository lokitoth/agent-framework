// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

[Experimental("MEAIREALTIME001")]
public class DelegatingRealtimeAgentTests
{
    private sealed class PassThroughDecorator : DelegatingRealtimeAgent
    {
        public PassThroughDecorator(RealtimeAgent inner) : base(inner) { }
    }

    [Fact]
    public void Ctor_Throws_OnNullInner()
        => Assert.Throws<ArgumentNullException>(() => new PassThroughDecorator(null!));

    [Fact]
    public void Id_Name_Description_ForwardToInner()
    {
        StubRealtimeAgent inner = new StubRealtimeAgent(name: "inner", description: "desc", id: "x1");
        PassThroughDecorator decorator = new PassThroughDecorator(inner);

        Assert.Equal("x1", decorator.Id);
        Assert.Equal("inner", decorator.Name);
        Assert.Equal("desc", decorator.Description);
    }

    [Fact]
    public void GetService_ReturnsSelf_ForOwnType_ThenDelegates()
    {
        StubRealtimeAgent inner = new StubRealtimeAgent();
        PassThroughDecorator decorator = new PassThroughDecorator(inner);

        Assert.Same(decorator, decorator.GetService(typeof(PassThroughDecorator)));
        Assert.Same(decorator, decorator.GetService(typeof(DelegatingRealtimeAgent)));

        // Asking for the inner concrete type resolves through the inner agent.
        Assert.Same(inner, decorator.GetService(typeof(StubRealtimeAgent)));
    }

    [Fact]
    public void GetService_DelegatesWhenKeyProvided()
    {
        StubRealtimeAgent inner = new StubRealtimeAgent();
        PassThroughDecorator decorator = new PassThroughDecorator(inner);

        // With a key, we don't claim the type even if it would match self;
        // forwarding gives the inner a chance to satisfy.
        Assert.Null(decorator.GetService(typeof(PassThroughDecorator), serviceKey: "key"));
    }

    [Fact]
    public async Task ConnectSessionAsync_DelegatesToInner()
    {
        StubRealtimeAgent inner = new StubRealtimeAgent();
        PassThroughDecorator decorator = new PassThroughDecorator(inner);

        await using RealtimeSession session = await decorator.ConnectSessionAsync();

        Assert.NotNull(session);
        Assert.Single(inner.Client.CreatedSessions);
    }

    [Fact]
    public async Task ConnectSessionAsync_ForwardsCancellation()
    {
        StubRealtimeAgent inner = new StubRealtimeAgent();
        PassThroughDecorator decorator = new PassThroughDecorator(inner);
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await decorator.ConnectSessionAsync(cts.Token));
    }
}
