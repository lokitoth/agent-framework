# Task 06 — Microsoft.Agents.AI.Realtime.Hosting (shared)

Plan reference: `/proto/implementation-plan.md` §5.1.

## Goals

- Transport-neutral hosted-side primitives.
- `IHostedRealtimeAgentBuilder` / `HostedRealtimeAgentBuilder`.
- `AddRealtimeAgent` (keyed DI, parallels `AddAIAgent`).
- `HostedRealtimeAgent : DelegatingRealtimeAgent` — per-connection
  wrapper carrying `HostedRealtimeSessionContext`. **No** session store,
  **no** registry (§3.4).
- `IRealtimeAgentTransport`, `IRealtimeAgentTransportContext`,
  `IRealtimeEventEncoder`, `RealtimeAgentTransportHandler` base — the
  connect/pump/close lifecycle.
- `HostedRealtimeSessionContext` — minimal isolation surface (key +
  caller identity strings; no policy this phase).

## Files

- `src/Microsoft.Agents.AI.Realtime.Hosting/`
  - `HostedRealtimeAgent.cs`
  - `HostedRealtimeAgentBuilder.cs` (+ `IHostedRealtimeAgentBuilder`)
  - `HostedRealtimeSessionContext.cs`
  - `IRealtimeAgentTransport.cs` (+ `IRealtimeAgentTransportContext` + `IRealtimeEventEncoder`)
  - `RealtimeAgentTransportHandler.cs`
  - `RealtimeAgentServiceCollectionExtensions.cs`

- `tests/Microsoft.Agents.AI.Realtime.Hosting.UnitTests/`
  - `HostedRealtimeAgentTests` — ctor guard, delegation, context resolved via GetService.
  - `RealtimeAgentServiceCollectionExtensionsTests` — keyed-DI registration, null guards, key isolation, scoped lifetime.
  - `RealtimeAgentTransportHandlerTests` — outbound encoder pump, inbound client message pump, cancellation, encoder null guard.
  - `HostedRealtimeSessionContextTests` — defaults.

## Outcome

- Build clean.
- `Microsoft.Agents.AI.Realtime.Hosting.UnitTests`: 12/12 tests passing.
