// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class AnonymousDelegatingRealtimeAgentTests
{
    [Fact]
    public void Ctor_NullArguments_Throw()
    {
        TestRealtimeAgent inner = new();
        Assert.Throws<ArgumentNullException>(() => new AnonymousDelegatingRealtimeAgent(null!, (_, _) => default));
        Assert.Throws<ArgumentNullException>(() => new AnonymousDelegatingRealtimeAgent(inner, null!));
    }

    [Fact]
    public async Task ConnectSessionAsync_InvokesDelegate_WithInnerAgent()
    {
        TestRealtimeAgent inner = new();
        RealtimeAgent? receivedInner = null;
        AnonymousDelegatingRealtimeAgent agent = new(
            inner,
            async (a, ct) =>
            {
                receivedInner = a;
                return await a.ConnectSessionAsync(ct).ConfigureAwait(false);
            });

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Assert.Same(inner, receivedInner);
        Assert.Single(inner.Client.CreatedSessions);
    }

    [Fact]
    public async Task BuilderUseFunc_WiresUpAnonymousDelegating()
    {
        TestRealtimeAgent inner = new();
        int invocations = 0;

        RealtimeAgent built = new RealtimeAgentBuilder(inner)
            .Use(async (a, ct) =>
            {
                invocations++;
                return await a.ConnectSessionAsync(ct).ConfigureAwait(false);
            })
            .Build();

        Assert.IsType<AnonymousDelegatingRealtimeAgent>(built);

        await using RealtimeSession session = await built.ConnectSessionAsync();
        Assert.Equal(1, invocations);
    }
}
