// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.UnitTests;

/// <summary>
/// Minimal concrete <see cref="RealtimeAgent"/> used as the inner-most agent in
/// builder pipelines for these tests.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class TestRealtimeAgent : RealtimeAgent
{
    private readonly FakeRealtimeClient _client;

    public TestRealtimeAgent(FakeRealtimeClient? client = null, string? name = null, string? id = null)
    {
        this._client = client ?? new FakeRealtimeClient();
        this.NameOverride = name;
        this.IdOverride = id;
    }

    public string? NameOverride { get; }

    public string? IdOverride { get; }

    public FakeRealtimeClient Client => this._client;

    public override string? Name => this.NameOverride;

    protected override string? IdCore => this.IdOverride;

    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        IRealtimeClientSession inner = await this._client.CreateSessionAsync(options: null, cancellationToken).ConfigureAwait(false);
        return new TestRealtimeSession(inner);
    }
}

/// <summary>Trivial <see cref="RealtimeSession"/> wrapping a fake client session.</summary>
[Experimental("MEAIREALTIME001")]
internal sealed class TestRealtimeSession : RealtimeSession
{
    public TestRealtimeSession(IRealtimeClientSession inner)
        : base(inner)
    {
    }
}
