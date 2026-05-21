// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Realtime.TestSupport;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

/// <summary>
/// A minimal concrete <see cref="RealtimeAgent"/> used by the abstractions test suite.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class StubRealtimeAgent : RealtimeAgent
{
    private readonly FakeRealtimeClient _client;
    private readonly RealtimeSessionOptions? _options;

    public StubRealtimeAgent(
        FakeRealtimeClient? client = null,
        RealtimeSessionOptions? options = null,
        string? name = null,
        string? description = null,
        string? id = null)
    {
        this._client = client ?? new FakeRealtimeClient();
        this._options = options;
        this.NameValue = name;
        this.DescriptionValue = description;
        this.IdOverride = id;
    }

    public string? NameValue { get; }

    public string? DescriptionValue { get; }

    public string? IdOverride { get; }

    public FakeRealtimeClient Client => this._client;

    public RealtimeAgentMetadata? MetadataValue { get; init; }

    public override string? Name => this.NameValue;

    public override string? Description => this.DescriptionValue;

    protected override string? IdCore => this.IdOverride;

    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        IRealtimeClientSession inner = await this._client.CreateSessionAsync(this._options, cancellationToken).ConfigureAwait(false);
        return new StubRealtimeSession(inner);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null && this.MetadataValue is not null && serviceType == typeof(RealtimeAgentMetadata))
        {
            return this.MetadataValue;
        }

        return base.GetService(serviceType, serviceKey);
    }
}

/// <summary>A trivial concrete <see cref="RealtimeSession"/>.</summary>
[Experimental("MEAIREALTIME001")]
internal sealed class StubRealtimeSession : RealtimeSession
{
    public StubRealtimeSession(IRealtimeClientSession inner)
        : base(inner)
    {
    }

    public void AddItem(RealtimeConversationItem item) => this.AddHistoryItem(item);

    public void ReplaceItem(int index, RealtimeConversationItem item) => this.ReplaceHistoryItem(index, item);

    public void Clear() => this.ClearHistory();
}
