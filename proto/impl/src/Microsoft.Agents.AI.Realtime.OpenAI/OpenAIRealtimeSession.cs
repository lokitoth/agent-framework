// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// <see cref="RealtimeSession"/> for OpenAI Realtime. This phase carries no
/// OpenAI-specific projection beyond what the underlying
/// <see cref="IRealtimeClientSession"/> provides; the type exists so consumers
/// can identify and decorate the session by concrete type, and so future
/// OpenAI-specific message shapes (e.g. function-call streaming) can be
/// added without an API break.
/// </summary>
[Experimental("MEAIREALTIME001")]
public sealed class OpenAIRealtimeSession : RealtimeSession
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIRealtimeSession"/>
    /// class.
    /// </summary>
    /// <param name="innerSession">The wrapped client session.</param>
    public OpenAIRealtimeSession(IRealtimeClientSession innerSession)
        : base(innerSession)
    {
    }
}
