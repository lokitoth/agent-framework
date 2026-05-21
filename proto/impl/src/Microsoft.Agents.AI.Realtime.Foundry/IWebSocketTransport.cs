// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Realtime.Foundry;

/// <summary>
/// Minimal WebSocket transport abstraction used by the Foundry realtime
/// client session. Internal so we can swap an in-memory fake into unit
/// tests via <c>InternalsVisibleTo</c>.
/// </summary>
[Experimental("MEAIREALTIME001")]
internal interface IWebSocketTransport : IAsyncDisposable
{
    /// <summary>Opens the connection.</summary>
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    /// <summary>Sends a JSON text frame.</summary>
    Task SendTextAsync(string payload, CancellationToken cancellationToken);

    /// <summary>
    /// Receives the next text frame; returns <see langword="null"/> if the
    /// peer has closed the connection.
    /// </summary>
    Task<string?> ReceiveTextAsync(CancellationToken cancellationToken);
}
