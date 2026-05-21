// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class FunctionInvocationRealtimeAgentTests
{
    [Fact]
    public async Task ConnectSessionAsync_WrapsSession_WhenInnerProvidesClientSession()
    {
        TestRealtimeAgent inner = new();
        FunctionInvocationRealtimeAgent agent = new(inner, NullLoggerFactory.Instance);

        await using RealtimeSession session = await agent.ConnectSessionAsync();

        Assert.Single(inner.Client.CreatedSessions);
        Assert.NotNull(session.GetService<IRealtimeClientSession>());
    }

    [Fact]
    public async Task BuilderUseFunctionInvocation_AppliesDecorator()
    {
        TestRealtimeAgent inner = new();
        RealtimeAgent built = new RealtimeAgentBuilder(inner)
            .UseFunctionInvocation()
            .Build();

        Assert.IsType<FunctionInvocationRealtimeAgent>(built);

        await using RealtimeSession session = await built.ConnectSessionAsync();
        Assert.NotNull(session);
    }
}
