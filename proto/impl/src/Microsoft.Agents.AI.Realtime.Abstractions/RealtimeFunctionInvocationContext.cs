// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Carries the per-invocation context handed to an <see cref="AIFunction"/>
/// that is invoked in response to a model-issued tool call on a
/// <see cref="RealtimeSession"/>.
/// </summary>
/// <remarks>
/// Created by the core package's function-invocation decorator (which composes
/// MEAI's <c>FunctionInvokingRealtimeClientSession</c>). Tools can inspect
/// <see cref="Session"/> to send follow-up messages and use
/// <see cref="CancellationToken"/> to cooperate with response cancellation.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class RealtimeFunctionInvocationContext
{
    /// <summary>Initializes a new instance of the <see cref="RealtimeFunctionInvocationContext"/> class.</summary>
    /// <param name="session">The session that issued the tool call.</param>
    /// <param name="function">The function being invoked.</param>
    /// <param name="callContent">The function-call content from the provider.</param>
    /// <param name="cancellationToken">A token that is cancelled when the in-flight response is cancelled.</param>
    public RealtimeFunctionInvocationContext(
        RealtimeSession session,
        AIFunction function,
        FunctionCallContent callContent,
        CancellationToken cancellationToken = default)
    {
        this.Session = session ?? throw new ArgumentNullException(nameof(session));
        this.Function = function ?? throw new ArgumentNullException(nameof(function));
        this.CallContent = callContent ?? throw new ArgumentNullException(nameof(callContent));
        this.CancellationToken = cancellationToken;
    }

    /// <summary>Gets the <see cref="RealtimeSession"/> that issued the tool call.</summary>
    public RealtimeSession Session { get; }

    /// <summary>Gets the <see cref="AIFunction"/> being invoked.</summary>
    public AIFunction Function { get; }

    /// <summary>Gets the <see cref="FunctionCallContent"/> from the provider.</summary>
    public FunctionCallContent CallContent { get; }

    /// <summary>Gets the cancellation token to honour during the invocation.</summary>
    public CancellationToken CancellationToken { get; }
}
