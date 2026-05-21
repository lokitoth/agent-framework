# RealtimeAgent — Hosting-side Test Plan

This document defines the **test projects** required to cover the
hosting-side realtime packages outlined in
[`realtime-hosting.md`](./realtime-hosting.md), modeled after the way
`/dotnet/tests` covers the corresponding text-agent hosting packages
(`Microsoft.Agents.AI.Hosting`, `Microsoft.Agents.AI.Hosting.OpenAI`,
`Microsoft.Agents.AI.Foundry.Hosting`, …).

Each hosting package has a sibling `*.UnitTests` project; transports
and the Foundry tier additionally have `*.IntegrationTests` projects
that boot an ASP.NET host (`WebApplicationFactory<>` /
`TestServer`-style) and exchange real wire frames over loopback. The
conformance project parallels `AgentConformance.IntegrationTests` and
exercises every encoder × transport pairing the design allows.

Conventions reused from the existing `/dotnet/tests` stack:

- Unit projects: `xUnit` + `Moq` + `Microsoft.Extensions.Hosting`
  helpers; one `XTests.cs` per production type; `Fixtures/` folder for
  multi-test scaffolding.
- Integration projects: ASP.NET `WebApplicationFactory<>`, a fake or
  recorded `RealtimeAgent` (no live provider needed) plus a small live
  test against OpenAI Realtime for end-to-end coverage; `Properties/`
  + `user-secrets` for credentials; conformance traces in
  `ConformanceTraces/`.

---

## 1. `Microsoft.Agents.AI.Realtime.Hosting.UnitTests`

Mirrors `Microsoft.Agents.AI.Hosting.UnitTests`. Covers transport-
neutral hosting primitives — DI registration, the hosted wrapper,
session stores, the transport handler base, and diagnostics.

Files:

- `HostApplicationBuilderRealtimeExtensionsTests.cs` — `AddRealtimeAgent`
  with options + factory overloads; lifetime honored
  (`Singleton`/`Scoped`/`Transient`); name keying matches the existing
  `AIAgent` pattern (parallels `HostApplicationBuilderAgentExtensionsTests.cs`).
- `ServiceCollectionRealtimeExtensionsTests.cs` — non-host overloads
  on `IServiceCollection`; idempotent registration; double-register
  fails fast.
- `IHostedRealtimeAgentBuilderTests.cs` — `Name` / `ServiceCollection`
  / `Lifetime` surface; chained `Configure` calls compose.
- `HostedRealtimeAgentBuilderTests.cs` — concrete builder semantics
  (mirrors `HostedAgentBuilderToolsExtensionsTests.cs`).
- `HostedRealtimeAgentTests.cs` — decorator wraps inner agent;
  `GetOrCreateSessionAsync` consults the store, falls back to
  `CreateSessionAsync`; `SaveSessionAsync` persists only the
  serializable slice (not the live socket); persistence cadence runs
  at *connection close* (design §2.2).
- `RealtimeSessionStoreTests.cs` — abstract contract assertions
  (parallels `AgentSessionStore` testing approach).
- `NoopRealtimeSessionStoreTests.cs` — every operation is a no-op;
  returns `null`/empty as documented.
- `InMemoryRealtimeSessionStoreTests.cs` — concurrent get/save/delete,
  isolation across keys.
- `FileSystemRealtimeSessionStoreTests.cs` — JSON file round-trip;
  atomic write; corruption recovery; case-insensitive key handling on
  Windows (parallels `FileSystemAgentSessionStoreTests.cs` in
  `Microsoft.Agents.AI.Foundry.Hosting.UnitTests`).
- `HostedRealtimeSessionContextTests.cs` — request-scoped identity /
  isolation propagation; shared with the Foundry tier (design §2.3).
- `IRealtimeAgentTransportTests.cs` — contract: `AcceptAsync` returns
  when the connection terminates; cancellation linked to
  connection-close fires when the host shuts down.
- `RealtimeAgentTransportHandlerTests.cs` — base class wires
  agent + store + encoder; pumps both directions; persists on close;
  surfaces failures as a `Faulted` state transition + log. Uses a fake
  duplex `IDuplexPipe` and a fake `IRealtimeEventEncoder`.
- `IRealtimeEventEncoderTests.cs` — abstract contract: round-trip
  invariants between `RealtimeSessionUpdate`/`RealtimeClientEvent` and
  the encoder's wire vocabulary.
- `RealtimeHostingTelemetryTests.cs` — `ActivitySource`/`Meter` names
  match documented strings; spans/metrics emitted by the base handler.
- `RealtimeHostingLogMessagesTests.cs` — `LoggerMessage` source-gen
  strings load; arguments propagate; redaction hook respected.
- `RealtimeHostingDiagnosticIdsTests.cs` — every public type carries
  the documented `[Experimental]` id.
- `Fixtures/` — `FakeRealtimeAgent`, `FakeRealtimeSession`,
  `RecordingEventEncoder`, `LoopbackDuplexPipe` reused across tests.

---

## 2. Transport unit-test projects

One unit-test project per `IRealtimeAgentTransport` implementation,
mirroring the transport-vs-protocol split in `realtime-hosting.md` §3.

### 2.1 `Microsoft.Agents.AI.Realtime.Hosting.WebSockets.UnitTests`

Files:

- `WebSocketRealtimeAgentTransportHandlerTests.cs` — drives a fake
  `WebSocket`; binary vs text frame routing per encoder; subprotocol
  negotiation; close-status codes mapped to `ConnectionStateChanged`.
- `MapRealtimeWebSocketEndpointTests.cs` — endpoint registration adds
  the WS middleware; route conflicts surface a clear error; mirrors
  `MapOpenAIResponses` test coverage in
  `Microsoft.Agents.AI.Hosting.OpenAI.UnitTests/EndpointRouteBuilderExtensionsTests.cs`.
- `WebSocketRealtimeOptionsTests.cs` — keepalive, max message size,
  close timeout defaults + validation.
- `WebSocketFramingTests.cs` — large frames split correctly; partial
  reads aggregate; cancellation tears down both directions.

### 2.2 `Microsoft.Agents.AI.Realtime.Hosting.WebRTC.UnitTests` *(deferred per §6.6)*

Skeleton only:

- `WebRtcRealtimeAgentTransportHandlerTests.cs`
- `WebRtcSignalingEndpointTests.cs` — SDP offer/answer round-trip
  against a fake peer.
- `EphemeralTokenProviderTests.cs` — default REST impl payload shape;
  scope/audience validation.
- `OpusPcm16TranscoderTests.cs` — frame-level round-trip parity.

### 2.3 `Microsoft.Agents.AI.Realtime.Hosting.Invocations.UnitTests`

Files:

- `InvocationsRealtimeAgentTransportHandlerTests.cs` — `POST /invocations`
  body → `RealtimeClientEvent`; SSE response written until
  `ResponseCompletedUpdate`; `agent_session_id` extracted from the
  envelope and used as the store key.
- `MapRealtimeInvocationsTests.cs` — route mapping (parallels
  `EndpointRouteBuilderExtensionsTests.cs` in
  `Microsoft.Agents.AI.Hosting.OpenAI.UnitTests`).
- `NeutralRealtimeInvocationsEventEncoderTests.cs` — `text.delta` /
  `text.done` / `done` shapes; matches the documented schema.
- `VoiceLiveInvocationsEventEncoderTests.cs` —
  `output_audio_transcription.delta` / `.done` / `done` vocabulary
  matches the Python VoiceLive sample byte-for-byte (golden files in
  `Fixtures/VoiceLive/`).
- `Fixtures/` — recorded request bodies and expected SSE byte streams
  for the VoiceLive happy path, tool-calling path, and cancellation
  path.

---

## 3. Protocol-vocabulary encoder unit-test projects

One unit-test project per encoder package in `realtime-hosting.md` §4.
Each is a pure mapping test suite — no transport, no host —
mirroring the structure of provider unit-test projects in
[`realtime-agent-test.md`](./realtime-agent-test.md) §3.

### 3.1 `Microsoft.Agents.AI.Realtime.Hosting.OpenAI.UnitTests`

Files:

- `OpenAIRealtimeEventEncoderTests.cs` — inbound and outbound mapping
  for every event in the OpenAI Realtime taxonomy, against captured
  fixtures shared with `Microsoft.Agents.AI.Realtime.OpenAI.UnitTests`
  (see `realtime-agent-test.md` §3.1) — but here exercised from the
  *server's* point of view: the encoder receives a
  `RealtimeSessionUpdate` and emits the OpenAI wire shape that an
  OpenAI-SDK client would expect.
- `OpenAIRealtimeEventEncoderRoundTripTests.cs` — every supported
  client-event JSON round-trips through `Decode` → `Encode` without
  loss.
- `Fixtures/` — shared with the client-side OpenAI test fixtures via a
  `Shared.Fixtures` project reference.

### 3.2 `Microsoft.Agents.AI.Realtime.Hosting.Gemini.UnitTests`

Files:

- `GeminiBidiEventEncoderTests.cs` — server-side mapping for
  `BidiGenerateContent*` messages.
- `GeminiBidiEventEncoderRoundTripTests.cs`.
- `Fixtures/` — recorded `setup`/`setupComplete`, audio chunks,
  function-call exchanges.

### 3.3 `Microsoft.Agents.AI.Realtime.Hosting.AzureVoiceLive.UnitTests`

Files:

- `AzureVoiceLiveEventEncoderTests.cs` — superset of OpenAI Realtime
  with Azure-specific extensions (`azure_semantic_vad`, HD voices,
  content-filter events).
- `AzureVoiceLiveCompatibilityTests.cs` — encoder output validates
  against the VoiceLive schema bundle pinned in `Fixtures/Schema/`.

---

## 4. `Microsoft.Agents.AI.Foundry.Hosting.Common.UnitTests` *(new, refactor)*

Sibling of the new `Microsoft.Agents.AI.Foundry.Hosting.Common` package
lifted from `Foundry.Hosting` (design §5.1). Picks up the existing
tests in `Microsoft.Agents.AI.Foundry.Hosting.UnitTests` that cover
the lifted types — they move with the code, not duplicated.

Tests that move:

- `HostedSessionIdentityContextTests.cs` (was in `Foundry.Hosting.UnitTests`).
- `FakeHostedSessionIsolationKeyProvider.cs` (test helper).
- `FileSystemAgentSessionStoreTests.cs`.
- `HostedOutboundUserAgentTests.cs`.

New tests added with the refactor:

- `HostedSessionContextTests.cs` — request-scoped propagation.
- `ApplyOpenTelemetryTests.cs` — both Responses and Realtime callers
  produce comparable activity baggage.
- `EnvConventionsTests.cs` — `FOUNDRY_PROJECT_ENDPOINT` /
  `FOUNDRY_AGENT_TOOLSET_ENDPOINT` parsing + precedence rules.

The existing `Microsoft.Agents.AI.Foundry.Hosting.UnitTests` project
remains for the Responses-specific tests
(`AgentFrameworkResponseHandler*`, `FoundryAIToolExtensionsTests`,
`FoundryToolboxServiceTests`, `OutputConverter*`, `InputConverterTests`,
`HostedFoundryMemoryProvider*`, MCP-consent flow).

---

## 5. `Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests`

Mirrors `Microsoft.Agents.AI.Foundry.Hosting.UnitTests`, but covers
the realtime sibling. No live Foundry service — handler tests use a
fake `RealtimeAgent` and a fake transport context.

Files:

- `ServiceCollectionExtensionsTests.cs` — `AddFoundryRealtime`
  registration (no-arg multiplex + single-agent shorthand); mirrors
  the existing `ServiceCollectionExtensionsTests.cs`.
- `MapFoundryRealtimeTests.cs` — `MapFoundryRealtime` pre-wires the
  VoiceLive encoder + Invocations transport at the documented path.
- `FoundryRealtimeInvocationsHandlerTests.cs` — agent resolution by
  `agent.name` / `metadata["entity_id"]`; isolation-key validation;
  user-agent policy applied; telemetry baggage attached (parallels
  `AgentFrameworkResponseHandlerTests.cs`).
- `FoundryRealtimeInvocationsHandlerTelemetryTests.cs` — span +
  metric shape (parallels `AgentFrameworkResponseHandlerTelemetryTests.cs`).
- `RealtimeAgentInvocationExecutorTests.cs` — true-realtime path:
  opens a `RealtimeSession`, pumps audio + text both ways, terminates
  on `ResponseCompletedUpdate`.
- `AIAgentInvocationExecutorTests.cs` — "VoiceLive-over-text-agent":
  wraps an `AIAgent`, synthesizes `output_audio_transcription.*`
  events from text deltas; verifies the Python sample's exact event
  ordering.
- `ExecutorSelectionTests.cs` — DI registration drives executor
  selection; no code-path divergence at runtime.
- `WorkflowTestAgents.cs` — *(absent here)*; workflow integration for
  realtime is out of scope per design §2.6.

---

## 6. `Microsoft.Agents.AI.Realtime.Hosting.IntegrationTests` *(transport-tier)*

End-to-end loopback tests using `WebApplicationFactory<>`. No external
provider — backed by a `FakeRealtimeAgent` that drives scripted update
streams. Mirrors `Foundry.Hosting.IntegrationTests` shape, minus the
Foundry-specific scenarios.

Files:

- `Fixtures/RealtimeHostingFixture.cs` — boots an ASP.NET host with a
  configurable transport + encoder; exposes a `WebSocket` /
  `HttpClient` for the test to drive.
- `WebSocketHappyPathTests.cs` — connect, send audio, receive audio +
  transcript, close cleanly across all three default encoders
  (Neutral, OpenAI, AzureVoiceLive).
- `InvocationsHappyPathTests.cs` — `POST /invocations` + SSE; reads
  every documented event until `done`; matches Python sample bytes
  when using `VoiceLiveInvocationsEventEncoder`.
- `FunctionCallingTests.cs` — model-side function call routed through
  the encoder + transport without loss (uses `MenuPlugin` from
  `AgentConformance.IntegrationTests`).
- `InterruptionTests.cs` — mid-response barge-in: client sends speech,
  server emits a single `ResponseCancelledUpdate` over the wire.
- `SessionPersistenceTests.cs` — connect, exchange some turns,
  disconnect, reconnect with the same `agent_session_id`, verify
  history replay via `RealtimeSessionStore`.
- `IsolationTests.cs` — pluggable `IRealtimeIsolationKeyProvider`
  enforces separation across simulated tenants (design §6.5).
- `BackPressureTests.cs` — client-slow scenario: server respects the
  encoder's flow-control hooks and does not buffer unboundedly.
- `ConformanceTraces/` — golden activity traces per transport ×
  encoder pairing for the happy-path scenario (parallels the existing
  folder in `Microsoft.Agents.AI.Hosting.OpenAI.UnitTests`).

---

## 7. `Microsoft.Agents.AI.Foundry.Hosting.Realtime.IntegrationTests`

Sibling of `Foundry.Hosting.IntegrationTests`. Boots a full Foundry-
tier host (`AddFoundryRealtime` + `MapFoundryRealtime`) and exercises
the Invocations + VoiceLive pairing end-to-end.

Files:

- `Fixtures/FoundryRealtimeFixture.cs` — boots the host with a fake
  isolation-key provider, an in-memory session store, and either a
  fake `RealtimeAgent` (offline) or a real OpenAI Realtime agent
  (gated by secrets, like `OpenAIRealtime.IntegrationTests`).
- `HappyPathHostedAgentTests.cs` — full VoiceLive event sequence
  matches the Python sample (parallels `HappyPathHostedAgentTests.cs`
  in `Foundry.Hosting.IntegrationTests`).
- `ToolCallingHostedAgentTests.cs` — `MenuPlugin` invoked via the
  hosted realtime path; client-side `AIFunction` invocation runs in
  the executor (parallels the existing same-named file).
- `AIAgentBackedHostedAgentTests.cs` — option-B path: hosted agent is
  a text `AIAgent`, transcripts are synthesized by
  `AIAgentInvocationExecutor`, VoiceLive client cannot tell the
  difference.
- `SessionFilesHostedAgentTests.cs` — session-bound file references
  flow through the realtime envelope identically to the Responses
  path (parallels the existing file).
- `CustomStorageHostedAgentTests.cs` — pluggable
  `RealtimeSessionStore` (file system, mocked Cosmos, etc.) — parallels
  the existing same-named test.
- `MemoryHostedAgentTests.cs` — when an `IChatHistoryProvider` /
  memory provider is registered, it sees the realtime turn boundaries
  emitted by the executor.
- `ToolCallingApprovalHostedAgentTests.cs` — approval flow over the
  realtime envelope, where supported.
- `IsolationKeyEnforcementTests.cs` — `HostedSessionIsolationKeyProvider`
  enforced on every realtime connect.
- `ConformanceTraces/` — pinned activity baggage.

---

## 8. `Microsoft.Agents.AI.Realtime.Hosting.Conformance.IntegrationTests`

Sibling of `AgentConformance.IntegrationTests`. Cross-cuts the encoder
× transport matrix from `realtime-hosting.md` §3/§4 with a single shared
test suite, parameterized by an `IRealtimeHostingFixture`. Verifies that
the same hosted behavior is reachable over every supported combination.

Files:

- `IRealtimeHostingFixture.cs` — fixture contract: encoder, transport,
  ASP.NET host builder, client factory (WebSocket / HttpClient).
- `ConnectAndCloseTests.cs` — every pairing.
- `AudioRoundTripTests.cs` — PCM in → transcript out for each pairing.
- `FunctionCallingTests.cs` — shared `MenuPlugin` across every pairing.
- `SessionPersistenceTests.cs` — reconnect + replay across pairings
  that support it; pairings that don't (e.g., Invocations one-shot)
  surface a clear capability flag.
- `InterruptionTests.cs` — pairings with VAD support.
- `Support/` — `Constants`, `SessionCleanup`, `TestConfiguration`,
  matching the existing folder layout.

Pairings exercised by default:

| Transport         | Encoder                              | In v1?   |
| ----------------- | ------------------------------------ | -------- |
| WebSockets        | Neutral                              | yes      |
| WebSockets        | OpenAI Realtime                      | yes      |
| WebSockets        | AzureVoiceLive                       | yes      |
| WebSockets        | Gemini Bidi                          | yes      |
| Invocations (SSE) | NeutralRealtimeInvocationsEventEncoder | yes    |
| Invocations (SSE) | VoiceLiveInvocationsEventEncoder       | yes    |
| WebRTC            | OpenAI Realtime                      | deferred |

Disallowed pairings (per design §6.2) are asserted to fail at
registration with a clear diagnostic — covered in a single
`DisallowedPairingsTests.cs`.

---

## 9. Cross-cutting test concerns

1. **ASP.NET correctness.** Every transport + encoder pairing runs
   under `WebApplicationFactory<>` so middleware ordering, dependency
   scopes, and disposal are exercised as in production. Mirrors the
   ASP.NET tests in `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore.IntegrationTests`.
2. **DI keying parity.** Realtime keyed registrations follow the same
   convention as `AIAgent`; `HostApplicationBuilderRealtimeExtensionsTests`
   asserts the same `IKeyedService<>` lookup keys are produced.
3. **`RealtimeSessionStore` cadence.** Tests assert persistence
   happens at connection close by default and additionally at the
   periodic checkpoint cadence when configured (design §6.4).
4. **Isolation.** `IRealtimeIsolationKeyProvider` is exercised across
   non-Foundry hosts (transport-tier integration) and the Foundry
   tier (header-driven) to ensure both surfaces enforce the same
   contract.
5. **Telemetry.** Every transport + encoder pairing emits the
   documented `ActivitySource` / `Meter` names; conformance traces
   pinned in `ConformanceTraces/` catch regressions byte-for-byte.
6. **AOT/trim.** `Microsoft.Agents.AI.Realtime.Hosting` and the
   default encoders are exercised under `PublishTrimmed=true` in a
   smoke-test lane; matches the existing convention for the text
   hosting stack.
7. **No-secret-leak.** Hosting telemetry and log scopes are asserted
   never to include auth headers, ephemeral tokens, or raw audio
   bytes (audio is logged length-only).

---

## 10. Test-project dependency graph

```
Microsoft.Agents.AI.Realtime.Hosting.UnitTests
        ▲
        ├── Microsoft.Agents.AI.Realtime.Hosting.WebSockets.UnitTests
        ├── Microsoft.Agents.AI.Realtime.Hosting.WebRTC.UnitTests        (deferred)
        ├── Microsoft.Agents.AI.Realtime.Hosting.Invocations.UnitTests
        ├── Microsoft.Agents.AI.Realtime.Hosting.OpenAI.UnitTests
        ├── Microsoft.Agents.AI.Realtime.Hosting.Gemini.UnitTests
        └── Microsoft.Agents.AI.Realtime.Hosting.AzureVoiceLive.UnitTests

Microsoft.Agents.AI.Foundry.Hosting.Common.UnitTests       (refactor)
        ▲
        └── Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests

Microsoft.Agents.AI.Realtime.Hosting.IntegrationTests          ──┐
Microsoft.Agents.AI.Foundry.Hosting.Realtime.IntegrationTests   ─┼──▶ Microsoft.Agents.AI.Realtime.Hosting.Conformance.IntegrationTests
                                                                 ─┘
```

The conformance project does **not** reference the integration
projects directly; each integration project registers its
`IRealtimeHostingFixture` implementation and lights up the shared
suite via xUnit collection fixtures — same pattern as
`AgentConformance.IntegrationTests` today.
