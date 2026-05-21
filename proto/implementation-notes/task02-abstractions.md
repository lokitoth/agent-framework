# Task 02 — `Microsoft.Agents.AI.Realtime.Abstractions`

Plan source: `/proto/implementation-plan.md` §4.1 + §3.1 / §3.2 / §3.6.

This task creates the abstractions assembly only. Tests land alongside
`test-support` in task 03 so we can commit "abstractions + abstractions
tests" as one wip phase per the user's commit cadence.

## Scope (one source of truth)

Add the following public types in namespace `Microsoft.Agents.AI`:

| Type | Notes |
| --- | --- |
| `RealtimeAgent` | abstract, Id/Name/Description/IdCore/Metadata/GetService, `RealtimeAgent.CurrentRunContext` AsyncLocal, `ConnectSessionAsync(ct) → ValueTask<RealtimeSession>`, abstract `ConnectSessionCoreAsync`. |
| `DelegatingRealtimeAgent` | decorator base; forwards Id/Name/Description/GetService/ConnectSession. |
| `RealtimeAgentMetadata` | provider id, model id, `RealtimeModality SupportedModalities`, `SupportsInterruption`, `SupportsVideo`. |
| `RealtimeModality` | `[Flags]` None=0, Text=1, Audio=2, Video=4. |
| `RealtimeAgentRunContext` | sibling of `AgentRunContext`; carries the agent + session. |
| `RealtimeSession` | abstract, wraps `IRealtimeClientSession`. Already-connected, reuses `AgentSessionStateBag`, exposes `History` + `ConversationId`, convenience helpers `Append/Commit/SendMessage/RequestResponse/CancelResponseAsync`, `protected void AddHistoryItem(...)` / `protected internal void ReplaceHistoryAt(...)` mutator surface for the core projection (ADR-004). |
| `InterruptedRealtimeServerMessage : RealtimeServerMessage` | gap-fill (ADR-005), `Type` defaults to `new("Interrupted")`. |
| `CancelResponseRealtimeClientMessage : RealtimeClientMessage` | **3rd AF-side addition** — see "Deviation note" below. |
| `RealtimeFunctionInvocationContext` | re-uses MEAI `AIFunction`; carries `Session`, `FunctionCallContent`, per-response `CancellationToken`. |
| `RealtimeAgentJsonUtilities` | static class exposing `DefaultOptions` (shared `JsonSerializerOptions`). |

All public types are annotated `[Experimental("MEAI-REALTIME-001")]`.

## Deviation note — `CancelResponseRealtimeClientMessage`

Plan §3.1 lists only two AF-layer additions
(`RealtimeAgentInterruptedEvent` + convenience helpers). Plan §4.1 +
`realtime-agent.md` §51 require `RealtimeSession.CancelResponseAsync()` as a
non-virtual helper implemented via `SendAsync`. The MEAI 10.5 surface does
not ship a typed "cancel response" client message
(`normalized-events.md` §117 explicitly treats `response.cancel` as a
`RawRepresentation` passthrough). Provider implementations need a typed
seam to recognize the cancel intent.

We add **one** AF-side marker subtype, `CancelResponseRealtimeClientMessage
: RealtimeClientMessage`, so providers' `SendAsync` overrides can pattern
match on it and emit the right wire op (`response.cancel`, `clear`, etc.).
This is the smallest possible extension that keeps `CancelResponseAsync`
non-virtual per plan, and it follows MEAI's own convention of one subtype
per client op. It does **not** introduce a parallel taxonomy — every other
inbound/outbound type stays MEAI's. The plan §3.1 list is updated as a
follow-up.

## Cross-project dependency choices

- `PackageReference Microsoft.Extensions.AI.Abstractions` — for the
  realtime types we build on.
- `ProjectReference ../../../../dotnet/src/Microsoft.Agents.AI.Abstractions`
  — for `AgentSessionStateBag` reuse (plan §3.2). This reaches into the
  existing AF abstractions package without copying state-bag types.
- `InternalsVisibleTo` for sibling proto packages (`...Realtime`,
  `.Foundry`, `.OpenAI`, `Hosting`, `Foundry.Hosting.Realtime`),
  `TestSupport`, the UnitTests project, and `DynamicProxyGenAssembly2`
  (Moq castle proxy).

## Open question deferred

`RealtimeFunctionInvocationContext`'s exact shape isn't load-bearing in
this task — only used by the core package's
`FunctionInvocationRealtimeAgent` (task 05). I'll define the minimum
shape now (session + FunctionCallContent + token) and refine in task 05.

## Build validation

- `dotnet build src/Microsoft.Agents.AI.Realtime.Abstractions` should
  succeed with zero warnings (TreatWarningsAsErrors=true).
- Cross-solution `ProjectReference` into `/dotnet/src` resolves the
  multi-target package and picks net10.0 (per plan §3.6 / review §A).
