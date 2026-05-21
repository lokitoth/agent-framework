// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Hosting.UnitTests;

[Experimental("MEAIREALTIME001")]
internal sealed class TestRealtimeAgent : RealtimeAgent
{
    public TestRealtimeAgent(FakeRealtimeClient? client = null)
    {
        this.Client = client ?? new FakeRealtimeClient();
    }

    public FakeRealtimeClient Client { get; }

    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        IRealtimeClientSession inner = await this.Client.CreateSessionAsync(options: null, cancellationToken).ConfigureAwait(false);
        return new TestRealtimeSession(inner);
    }
}

[Experimental("MEAIREALTIME001")]
internal sealed class TestRealtimeSession : RealtimeSession
{
    public TestRealtimeSession(IRealtimeClientSession inner) : base(inner) { }
}
