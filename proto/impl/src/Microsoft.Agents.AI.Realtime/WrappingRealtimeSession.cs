// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A trivial <see cref="RealtimeSession"/> that wraps an existing
/// <see cref="IRealtimeClientSession"/>. Useful when a decorator composes a
/// new pipeline at the M.E.AI session level (e.g. function invocation) and
/// needs to surface it as an AF <see cref="RealtimeSession"/>.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal sealed class WrappingRealtimeSession : RealtimeSession
{
    public WrappingRealtimeSession(IRealtimeClientSession innerSession, AgentSessionStateBag? stateBag = null)
        : base(innerSession, stateBag)
    {
    }
}
