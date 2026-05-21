// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating <see cref="RealtimeAgent"/> that enables automatic tool
/// invocation by composing M.E.AI's
/// <see cref="FunctionInvokingRealtimeClient"/> over the underlying
/// <see cref="IRealtimeClientSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in per ADR-003: callers must explicitly attach this decorator via
/// <see cref="FunctionInvocationRealtimeAgentBuilderExtensions.UseFunctionInvocation(RealtimeAgentBuilder, ILoggerFactory?, Action{FunctionInvokingRealtimeClient}?, IServiceProvider?)"/>.
/// </para>
/// <para>
/// The decorator opens a session against the wrapped agent, extracts the
/// underlying <see cref="IRealtimeClientSession"/> via
/// <see cref="RealtimeSession.GetService{TService}(object?)"/>, wraps it in a
/// short-lived adapter <see cref="IRealtimeClient"/> that returns the open
/// session, and then re-opens the session via
/// <see cref="FunctionInvokingRealtimeClient.CreateSessionAsync"/>. The
/// returned session participates in MEAI's function-call loop.
/// </para>
/// <para>
/// AF-side projection state (history, ConversationId) on the original
/// <see cref="RealtimeSession"/> is preserved by routing the returned session
/// through a <see cref="DelegatingRealtimeSession"/>-shaped wrapper that
/// delegates to the function-invoking session for wire ops but reuses the
/// original session's <see cref="AgentSessionStateBag"/>.
/// </para>
/// </remarks>
[Experimental("MEAIREALTIME001")]
public sealed class FunctionInvocationRealtimeAgent : DelegatingRealtimeAgent
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Action<FunctionInvokingRealtimeClient>? _configure;
    private readonly IServiceProvider? _functionInvocationServices;

    /// <summary>Initializes a new instance of the <see cref="FunctionInvocationRealtimeAgent"/> class.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="loggerFactory">An optional logger factory.</param>
    /// <param name="configure">An optional configuration callback applied to the wrapped <see cref="FunctionInvokingRealtimeClient"/>.</param>
    /// <param name="functionInvocationServices">An optional service provider for resolving services to tools.</param>
    public FunctionInvocationRealtimeAgent(
        RealtimeAgent innerAgent,
        ILoggerFactory? loggerFactory = null,
        Action<FunctionInvokingRealtimeClient>? configure = null,
        IServiceProvider? functionInvocationServices = null)
        : base(innerAgent)
    {
        this._loggerFactory = loggerFactory;
        this._configure = configure;
        this._functionInvocationServices = functionInvocationServices;
    }

    /// <inheritdoc/>
    protected override async ValueTask<RealtimeSession> ConnectSessionCoreAsync(CancellationToken cancellationToken)
    {
        RealtimeSession providerSession = await base.ConnectSessionCoreAsync(cancellationToken).ConfigureAwait(false);

        IRealtimeClientSession? providerInner = providerSession.GetService<IRealtimeClientSession>();
        if (providerInner is null)
        {
            return providerSession;
        }

        SingleSessionRealtimeClient adapter = new(providerInner);
        FunctionInvokingRealtimeClient invokingClient = new(adapter, this._loggerFactory, this._functionInvocationServices);
        this._configure?.Invoke(invokingClient);

        IRealtimeClientSession invokingSession = await invokingClient
            .CreateSessionAsync(providerSession.Options, cancellationToken)
            .ConfigureAwait(false);

        return new FunctionInvocationRealtimeSession(invokingSession, providerSession);
    }

    /// <summary>
    /// Minimal <see cref="IRealtimeClient"/> that returns a single, already-open
    /// <see cref="IRealtimeClientSession"/>. Lifetime ends with the wrapped session.
    /// </summary>
    private sealed class SingleSessionRealtimeClient : IRealtimeClient
    {
        private readonly IRealtimeClientSession _session;

        public SingleSessionRealtimeClient(IRealtimeClientSession session)
        {
            this._session = session;
        }

        public Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(this._session);

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return serviceKey is null && serviceType.IsInstanceOfType(this)
                  ? this
                  : this._session.GetService(serviceType, serviceKey);
        }

        public void Dispose()
        {
            // The underlying session is owned by the caller; do not dispose here.
        }
    }
}
