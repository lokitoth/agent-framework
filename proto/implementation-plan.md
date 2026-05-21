# RealtimeAgent Prototype — Implementation Plan

Prototype `RealtimeAgent` support under `/proto/impl` as a standalone
`realtime.slnx` solution that mirrors the package layering described in
`/proto/realtime-agent.md`, `/proto/realtime-hosting.md`,
`/proto/clientside-cascade-agent.md`, and — authoritatively for the type
shapes — `/proto/session.md` §4 and `/proto/normalized-events.md`.

Client-side first, hosting second, cascading agent **specified but not
scheduled**. All tests in this phase are **unit tests**; no real provider
endpoints.

This revision incorporates the feedback in
`/proto/implementation-plan-review.md`.

---

## 1. Goals and non-goals

**Goals**

- Stand up `realtime.slnx` under `/proto/impl` with the package skeletons
  and minimum-viable types needed to validate the client- and hosting-side
  surface.
- **Build directly on the M.E.AI 10.5.x realtime primitives** (`IRealtimeClient`,
  `IRealtimeClientSession`, `RealtimeClientMessage`, `RealtimeServerMessage`,
  `RealtimeConversationItem`, `RealtimeSessionOptions`, `RealtimeAudioFormat`,
  `VoiceActivityDetectionOptions`, …). The AF layer adds only what is
  genuinely missing (per `normalized-events.md` §6 + `misc-notes.md`).
- Validate the surface with two providers — Foundry VoiceLive and OpenAI
  Realtime — via fake `IRealtimeClient` / `IRealtimeClientSession` fixtures.
- Validate the hosting-side abstractions with a Foundry-flavored hosting
  package, again via in-process fakes.

**Non-goals (this phase)**

- No integration tests against real OpenAI / Azure / Foundry endpoints.
- No WebRTC / SIP transports; no WebSocket *hosting* transport package.
- No Gemini, Anthropic, Nova Sonic.
- No DevUI, samples, or docs-site changes.
- No production-quality logging / OTel — telemetry hooks are stubbed.
- No cascading agent **implementation** — design captured in §6, no todos.
- No `Microsoft.Agents.AI.Foundry.Hosting.Common` refactor of the existing
  text-agent package (see §5.2 for what we do instead).

---

## 2. Solution layout

```
/proto/impl/
  realtime.slnx
  Directory.Build.props                 (mirrors /dotnet conventions, scoped)
  Directory.Packages.props              (M.E.AI 10.5.x, M.E.AI.OpenAI 10.5.x, xUnit, Moq)
  src/
    Microsoft.Agents.AI.Realtime.Abstractions/
    Microsoft.Agents.AI.Realtime/
    Microsoft.Agents.AI.Realtime.OpenAI/
    Microsoft.Agents.AI.Realtime.Foundry/
    Microsoft.Agents.AI.Realtime.Hosting/
    Microsoft.Agents.AI.Foundry.Hosting.Realtime/        (see §5.0 naming)
  tests/
    Microsoft.Agents.AI.Realtime.Abstractions.UnitTests/
    Microsoft.Agents.AI.Realtime.UnitTests/
    Microsoft.Agents.AI.Realtime.OpenAI.UnitTests/
    Microsoft.Agents.AI.Realtime.Foundry.UnitTests/
    Microsoft.Agents.AI.Realtime.Hosting.UnitTests/
    Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests/
    TestSupport/
      Microsoft.Agents.AI.Realtime.TestSupport/
```

---

## 3. Type-surface decisions resolved up-front

These resolve the substantive disagreements flagged by the review (items
§§1–4) so §4 doesn't carry them as open questions.

### 3.1 M.E.AI is the public realtime surface — no parallel taxonomy

Per `normalized-events.md` §1 + §6, `misc-notes.md`, and `session.md` §4.1:

- **Inbound:** the public stream is
  `IAsyncEnumerable<RealtimeServerMessage>` (M.E.AI). The AF layer does
  **not** introduce a parallel `RealtimeSessionUpdate` hierarchy.
- **Outbound:** clients call `SendAsync(RealtimeClientMessage, ct)` with
  the existing M.E.AI message subtypes (`SessionUpdateRealtimeClientMessage`,
  `InputAudioBufferAppendRealtimeClientMessage`,
  `InputAudioBufferCommitRealtimeClientMessage`,
  `CreateConversationItemRealtimeClientMessage`,
  `CreateResponseRealtimeClientMessage`). Provider-specific ops not in the
  normalized set ride on `RawRepresentation` of a pass-through
  `RealtimeClientMessage` (`normalized-events.md` §1).
- **Audio format / VAD / transcription options:** the M.E.AI types are
  used directly. We do **not** redefine `RealtimeAudioFormat` /
  `VoiceActivityDetectionOptions` / `TranscriptionOptions`.
- **AF-layer additions** (the only ones):
  1. `RealtimeAgentInterruptedEvent` (gap-filler called out in
     `normalized-events.md` §6, G1).
  2. A small set of **convenience helpers** on `RealtimeSession`
     (`AppendInputAudioAsync`, `CommitInputAudioAsync`,
     `SendMessageAsync`, `RequestResponseAsync`, `CancelResponseAsync`).
     These are non-virtual on the base and implemented in terms of
     `SendAsync`. They are ergonomic sugar over `RealtimeClientMessage`,
     not a new type system.
- **Gemini `generationComplete`** is intentionally **not** modeled
  (`normalized-events.md` §6, G3).

If the proto reveals further genuine gaps, capture them as M.E.AI
follow-ups — do not invent escape hatches (`misc-notes.md`).

### 3.2 `RealtimeSession` shape (per `session.md` §4.1)

- **Already-connected on construction.** No separate `ConnectAsync` /
  `CreateSessionAsync` split. The agent's `ConnectSessionAsync(ct)`
  opens the socket, completes the provider handshake, and returns a live
  session.
- **Wraps `IRealtimeClientSession`.** `RealtimeSession` *is*-a (or
  composes) `IRealtimeClientSession`. The wire-level
  `SendAsync(RealtimeClientMessage, ct)` and
  `GetStreamingResponseAsync(...)` are the primary I/O surface.
- **No `Serialize` / `Deserialize` on the base type.** Persistence /
  resumption is a Gemini-only concept today and is *not* introduced now.
  A nullable `string? ConversationId { get; }` is the forward-compat slot
  (`session.md` §4.2).
- **Configuration lives on the agent**, not per-connect call
  (`session.md` §4.3.1). `ConnectSessionAsync` takes only
  cancellation (and eventually `ConversationId` for resumable providers).
- **`StateBag` reuse.** `RealtimeSession` inherits the existing
  `AgentSessionStateBag` pattern from `Microsoft.Agents.AI.Abstractions`;
  no new `RealtimeSessionStateBag` type is introduced (review §S2).

### 3.3 Provider clients build on M.E.AI's `IRealtimeClient`

- The Foundry and OpenAI provider packages wrap a `IRealtimeClient`
  obtained from M.E.AI (or, for OpenAI, from
  `Microsoft.Extensions.AI.OpenAI` which already ships one).
- **Foundry:** until/unless M.E.AI ships a Foundry `IRealtimeClient`,
  the proto includes a thin `FoundryRealtimeClient : IRealtimeClient`
  inside `Microsoft.Agents.AI.Realtime.Foundry`. It is driven by an
  injectable WebSocket abstraction (so unit tests can swap in a fake),
  but that abstraction is **internal** — it is not part of any public
  AF surface. The public AF surface is the M.E.AI `IRealtimeClient`.
- **OpenAI:** compose `Microsoft.Extensions.AI.OpenAI`'s
  `IRealtimeClient` rather than re-implementing the wire mapping. If
  the M.E.AI.OpenAI client is unavailable or insufficient (validated as
  the first task of the OpenAI work), drop down to the same
  internal-WebSocket pattern Foundry uses and flag a follow-up. Decision
  goes into the OpenAI ADR.
- **Auto tool invocation** is provided by composing M.E.AI's
  `FunctionInvokingRealtimeClientSession` (`session.md` §4.1) — we do not
  reimplement it. The AF-side decorator (§4.2) becomes a thin
  configure-and-wrap of that.

### 3.4 No persistence in hosted realtime (this phase)

- Because `RealtimeSession` does **not** expose serialize/deserialize
  (§3.2), hosted realtime does **not** round-trip session state through a
  store. **The `RealtimeSessionStore` abstraction is dropped from scope
  entirely** (review v2 §A): until persistable session state exists
  (`session.md` §4.5), `HostedRealtimeAgent` is a thin
  `DelegatingRealtimeAgent` with no store dependency. `InMemoryRealtimeSessionStore`
  and `FileSystemRealtimeSessionStore` are tracked as follow-ups, not
  introduced as YAGNI'd registry shells.
- Logical conversation continuity across reconnects relies on M.E.AI's
  `ConversationId` slot. Hosted realtime threads this through; nothing
  more.

### 3.5 Hosting package naming

- We follow `realtime-hosting.md` §5.2 / `hosting.md` §3.2:
  **`Microsoft.Agents.AI.Foundry.Hosting.Realtime`** (Foundry-tier
  parallel of `Microsoft.Agents.AI.Foundry.Hosting`), not
  `Microsoft.Agents.AI.Realtime.Foundry.Hosting`.

### 3.6 Reusing existing-package primitives (no refactors)

- The proto is a **prototype**: refactors of existing packages are
  follow-ups, not work items. The Foundry hosting realtime package takes
  a project reference to the existing
  `dotnet/src/Microsoft.Agents.AI.Foundry.Hosting` and reuses its **public
  types directly** — `HostedSessionContext`,
  `HostedSessionContextExtensions`, `HostedSessionIsolationKeyProvider`,
  `AgentSessionStore`, `InMemoryAgentSessionStore`,
  `FileSystemAgentSessionStore`. No duplication, no proto-local clones.
- For the small set of `internal` primitives the proto needs
  (`HostedAgentUserAgentPolicy`,
  `PlatformHostedSessionIsolationKeyProvider`,
  `HostedSessionJsonUtilities`), we add a one-line
  `[assembly: InternalsVisibleTo("Microsoft.Agents.AI.Foundry.Hosting.Realtime")]`
  to the existing package. That is the smallest possible touch and
  preserves the no-refactor stance.
- Same pattern applies to any cross-package reach by the realtime
  packages (`…Realtime.Foundry` ↔ `…Foundry`,
  `…Realtime.Hosting` ↔ `…Hosting`, test projects across packages).
  Never copy-paste types the proto can reach via reference +
  `[InternalsVisibleTo]`.
- The `.Common` extraction in `realtime-hosting.md` §5.1 stays a
  tracked follow-up; this phase does not perform it.

### 3.7 Invocations transport packaging

- `realtime-hosting.md` §3.3 calls for an independent
  `Microsoft.Agents.AI.Realtime.Hosting.Invocations` transport package
  reusable by non-Foundry hosts. **For the proto, we fuse the Invocations
  handler + neutral + VoiceLive encoders into
  `Microsoft.Agents.AI.Foundry.Hosting.Realtime`.** A later split is
  flagged as a follow-up (matching the same pattern §3.3 establishes for
  client-side OpenAI/Foundry duplication).

### 3.8 Open ADR items captured for this phase

- ADR-001: M.E.AI-as-public-surface vs parallel taxonomy. *Decision: §3.1
  above.*
- ADR-002: Single-consumer enumeration of
  `GetStreamingResponseAsync`. *Decision: single-consumer, subsequent
  call throws.*
- ADR-003: Auto tool invocation default
  (`realtime-agent.md` §4 / review §S4): **opt-in** via
  `UseFunctionInvocation()`, matching `AIAgent`.
- ADR-004: Authoritative history ownership.
  **Client-tracked** projection over `RealtimeServerMessage` history. The
  projection logic lives in `Microsoft.Agents.AI.Realtime` (the core
  package) and not in `Abstractions`, so `Abstractions` stays thin and
  does not need to know about every `RealtimeServerMessage` subtype. The
  hosted layer does **not** own history — `HostedRealtimeAgent` is a
  thin wrapper; no store dependency in this phase (§3.4).
- ADR-005: `RealtimeAgentInterruptedEvent` projection mechanism.
  **Subclass `RealtimeServerMessage`** as
  `InterruptedRealtimeServerMessage : RealtimeServerMessage` and emit
  that in the inbound stream. Closer to the
  `normalized-events.md` §6 G1 sketch than the marker-payload approach;
  gives consumers an `is`-check / pattern-match seam without an
  extension helper.

---

## 4. Client-side phase

### 4.1 `Microsoft.Agents.AI.Realtime.Abstractions`

Pure abstractions; depends on `Microsoft.Extensions.AI.Abstractions` 10.5.x
for `IRealtimeClient`, `IRealtimeClientSession`, the
`RealtimeClient/ServerMessage` hierarchies, `RealtimeConversationItem`,
`RealtimeSessionOptions`, `RealtimeAudioFormat`,
`VoiceActivityDetectionOptions`, `TranscriptionOptions`, etc.

Types introduced (intentionally small per §3):

- `RealtimeAgent` — abstract base; `Id`, `Name`, `Description`, `IdCore`,
  `Metadata`, `GetService`, `CurrentRunContext` (AsyncLocal),
  `ConnectSessionAsync(ct)` returning `RealtimeSession`.
- `DelegatingRealtimeAgent` — decorator base.
- `RealtimeAgentMetadata` — provider id, model id, supported
  `RealtimeModality` flags, supports interruption, supports video.
- `RealtimeSession` — `IRealtimeClientSession`-shaped (§3.2). Members:
  - `SendAsync(RealtimeClientMessage, ct)` (delegates to inner
    `IRealtimeClientSession`).
  - `GetStreamingResponseAsync(ct)` →
    `IAsyncEnumerable<RealtimeServerMessage>`.
  - Convenience helpers: `AppendInputAudioAsync`, `CommitInputAudioAsync`,
    `SendMessageAsync`, `RequestResponseAsync`, `CancelResponseAsync` —
    non-virtual, implemented via `SendAsync`.
  - `ConversationId` (forward-compat, often null).
  - `History` — read-only `IReadOnlyList<RealtimeConversationItem>`. The
    projection logic that populates it lives in
    `Microsoft.Agents.AI.Realtime` (per ADR-004), not in this assembly;
    the base class exposes the property and a `protected` mutator
    surface consumed by the core projection.
  - `StateBag` — reuses `AgentSessionStateBag` from
    `Microsoft.Agents.AI.Abstractions`.
  - `DisposeAsync`.
- `InterruptedRealtimeServerMessage : RealtimeServerMessage` — the only
  AF-defined inbound event (gap-fill, `normalized-events.md` §6 G1).
  Subclassing per ADR-005. Lives in `Abstractions` so providers can
  emit it directly; the `History` projection in core (ADR-004)
  pattern-matches on it.
- `RealtimeModality` (`[Flags]`: Text, Audio, Video).
- `RealtimeAgentRunContext` — AsyncLocal, sibling of `AgentRunContext`.
- `RealtimeFunctionInvocationContext` — re-uses `AIFunction`; carries the
  session and per-response cancellation.
- `RealtimeAgentJsonUtilities` + `[JsonSerializable]` set.
- All public types: `[Experimental("MEAI-REALTIME-001")]`.

**Not introduced** (review §1 / §S2): a parallel
`RealtimeSessionUpdate*`/`RealtimeClientEvent*` hierarchy; redefined
`RealtimeAudioFormat`; a new `RealtimeSessionStateBag`.

### 4.2 `Microsoft.Agents.AI.Realtime` — concrete, non-provider

- `RealtimeAgentBuilder` + `Use(...)` plumbing (parallels
  `AIAgentBuilder`).
- `LoggingRealtimeAgent` + `LoggingRealtimeAgentBuilderExtensions` (bytes
  redacted as length).
- `OpenTelemetryRealtimeAgent` +
  `OpenTelemetryRealtimeAgentBuilderExtensions` — span/meter names
  populated, no exporter wiring (review §S1: ship the builder extension
  shape and a test stub).
- `FunctionInvocationRealtimeAgent` — composes M.E.AI's
  `FunctionInvokingRealtimeClientSession` (§3.3). Opt-in (ADR-003).
- `AnonymousDelegatingRealtimeAgent`.
- `RealtimeAgentAsAIAgent` — bridge to `AIAgent`. Transcript-only response
  collection; audio surfaced via `AdditionalProperties`.
- `InMemoryRealtimeHistoryProvider`.
- `RealtimeAudioPipe` / `RealtimeAudioWriter`.

**Deferred to cascade (§6):** `AIAgentAsRealtimeAgent`,
`CascadingRealtimeAgent`, `UseCascade`, `AppendInputTextAsync` /
`CommitInputTextAsync` on `RealtimeSession`.

### 4.3 `Microsoft.Agents.AI.Realtime.Foundry`

- `FoundryRealtimeAgent : RealtimeAgent` +
  `FoundryRealtimeAgentOptions` (instructions, voice, audio formats,
  VAD). **Conversation mode only this phase** — transcription-only
  sessions and BYOM-agent specialization are out of scope and tracked as
  follow-ups (`session.md` §1 conceptual table).
- `FoundryRealtimeAgent` constructs an internal
  `FoundryRealtimeClient : IRealtimeClient` (§3.3). The
  `IWebSocketTransport` is **internal**; tests reach it via
  `InternalsVisibleTo`.
- `FoundryRealtimeSession : RealtimeSession` wraps the
  `IRealtimeClientSession` and adds Foundry-specific projection (e.g.,
  recognizing `output_audio_buffer.cleared` as
  `RealtimeAgentInterruptedEvent`, per `normalized-events.md` §6 G1).
- Azure-only knobs (`azure_semantic_vad`,
  `azure_deep_noise_suppression`, HD/custom voices, `rate`) ride on
  `RealtimeSessionOptions.RawRepresentationFactory` per `session.md`
  §4.4 — **not** as typed AF surface.
- Auth: accept `TokenCredential` or `AzureKeyCredential`.

### 4.4 `Microsoft.Agents.AI.Realtime.OpenAI`

- First task: validate whether `Microsoft.Extensions.AI.OpenAI`'s
  `IRealtimeClient` covers the proto's needs.
  **Concrete validation criterion** (so the decision isn't subjective):
  open a session against the `FakeRealtimeClient` test transport, send a
  `SessionUpdateRealtimeClientMessage` + `CreateResponseRealtimeClientMessage`,
  receive a `ResponseCreatedRealtimeServerMessage` with status
  `ResponseDone`, and round-trip one `OutputTextAudioRealtimeServerMessage`
  text delta. If that flow exercises the M.E.AI.OpenAI client end-to-end
  cleanly, **compose it.** Otherwise, the package replicates the
  Foundry pattern (internal `IWebSocketTransport`, internal
  `OpenAIRealtimeClient : IRealtimeClient`) and flags shared-internals
  extraction with Foundry as a follow-up.
- `OpenAIRealtimeAgent : RealtimeAgent` +
  `OpenAIRealtimeAgentOptions`.
- `OpenAIRealtimeSession : RealtimeSession`.
- WebSocket only (no WebRTC / SIP / ephemeral tokens this phase).

### 4.5 Shared test support
(`tests/TestSupport/Microsoft.Agents.AI.Realtime.TestSupport`)

- `FakeRealtimeClient : IRealtimeClient` and
  `FakeRealtimeClientSession : IRealtimeClientSession` — push canned
  `RealtimeServerMessage`s, capture `RealtimeClientMessage`s.
- `FakeWebSocketTransport` — only used by Foundry tests (and OpenAI tests
  if §4.4 takes the fallback path).
- `RealtimeServerMessageRecorder` — drain helpers + per-type assertions.
- `JsonEventScript` — small "send these events, expect these messages"
  DSL.

### 4.6 Client-side unit tests

For each `…UnitTests` project, in addition to the type-level tests
already implied:

- **Abstractions** — `RealtimeAgentTests`, `DelegatingRealtimeAgentTests`,
  `RealtimeAgentMetadataTests`, `RealtimeSessionTests`
  (already-connected invariant, single-consumer enumeration,
  `DisposeAsync` idempotency, `History` read-only),
  `RealtimeAgentInterruptedEventTests`, convenience-helper tests
  (`Append/Commit/Send/Request/Cancel` translate to the right
  `RealtimeClientMessage` subtype).
- **Realtime core** — Builder composition + decorator order; logging
  redaction; function-invocation opt-in via the M.E.AI
  `FunctionInvokingRealtimeClientSession` composition; OTel decorator
  emits spans (no exporter); `InMemoryRealtimeHistoryProvider` tests
  (parallel of `InMemoryChatHistoryProviderTests`);
  `RealtimeAgentAsAIAgent` happy path.
- **Foundry** — Use `FakeWebSocketTransport`:
  - Inbound projection for the VoiceLive event vocabulary.
  - Outbound: snapshot JSON for `Append/Commit/SendMessage/Request/Cancel`.
  - Turn-detection translation matrix incl. `azure_semantic_vad`
    (via `RawRepresentationFactory`).
  - Interruption: `speech_started` mid-response ⇒ exactly one
    `RealtimeAgentInterruptedEvent` projected.
  - Reconnect-with-replay: out of scope this phase (no
    Serialize/Deserialize); tracked as follow-up.
- **OpenAI** — Same matrix as Foundry; plus function-call streaming
  (`response.function_call_arguments.delta` → invoke on `…done` →
  `conversation.item.create(function_call_output)` + `response.create`)
  and `rate_limits.updated` projection.

---

## 5. Hosting-side phase

### 5.1 `Microsoft.Agents.AI.Realtime.Hosting`

Shared, transport-neutral. Depends on `Microsoft.Agents.AI.Realtime` +
`Microsoft.Agents.AI.Hosting`.

- `IHostedRealtimeAgentBuilder` / `HostedRealtimeAgentBuilder`.
- `HostApplicationBuilderRealtimeExtensions.AddRealtimeAgent(name, …)` —
  keyed-DI registration (parallels `AddAIAgent`).
- `HostedRealtimeAgent : DelegatingRealtimeAgent` — wraps an inner
  `RealtimeAgent`. *No* session store, *no* registry (§3.4); the
  wrapper's job is per-connection logging/OTel + isolation-context
  propagation.
- `IRealtimeAgentTransport`, `IRealtimeAgentTransportContext`,
  `IRealtimeEventEncoder`, `RealtimeAgentTransportHandler` base.
- `HostedRealtimeSessionContext` — minimal isolation/identity surface
  (header → key extraction interface only; no policy this phase).

### 5.2 `Microsoft.Agents.AI.Foundry.Hosting.Realtime`

Foundry-tier (§3.5), with the Invocations transport fused in (§3.7).
**Takes a project reference to `Microsoft.Agents.AI.Foundry.Hosting`**
(§3.6) and reuses `HostedSessionContext`,
`HostedSessionIsolationKeyProvider`, etc. directly. For the small set
of `internal` primitives needed
(`HostedAgentUserAgentPolicy`,
`PlatformHostedSessionIsolationKeyProvider`,
`HostedSessionJsonUtilities`), the existing package gains a one-line
`[assembly: InternalsVisibleTo("Microsoft.Agents.AI.Foundry.Hosting.Realtime")]`.

- `InvocationsRealtimeAgentTransportHandler` — `POST /invocations` + SSE
  behind an `IInvocationsRequestSink` abstraction so unit tests don't
  need a TestServer.
- `VoiceLiveInvocationsEventEncoder` — emits the
  `output_audio_transcription.delta` / `.done` / `done` shape the
  Python sample hand-codes.
- `NeutralRealtimeInvocationsEventEncoder` — control comparator
  (`text.delta` / `text.done` / `done`).
- `AddFoundryRealtime` / `MapFoundryRealtime` extensions.
- Standalone (§3.6): no dep on `dotnet/src/Microsoft.Agents.AI.Foundry.Hosting`.
  Reuses anything it needs by composition with §5.1.

### 5.3 Hosting unit tests

- **Shared** — DI keyed resolution + lifetimes;
  `RealtimeAgentTransportHandler` happy path with a fake transport + fake
  encoder (connect/pump/close); cancellation propagation;
  `HostedRealtimeSessionContext` header extraction.
- **Foundry hosting** — Golden-file SSE JSON for representative
  `RealtimeServerMessage` sequences (text-only response, audio transcript
  response, cancelled response, error).
  `InvocationsRealtimeAgentTransportHandler` end-to-end with canned
  server-message streams. `AddFoundryRealtime` registers expected services
  and the VoiceLive encoder as default.

---

## 6. Cascade — specified, not scheduled

Fully specified in `/proto/clientside-cascade-agent.md` and
`/proto/clientside-cascade-agent-test.md`. **No todo is added in this
phase.** When it lands later:

- `CascadingRealtimeAgent : RealtimeAgent` and
  `CascadingRealtimeAgentOptions` in `Microsoft.Agents.AI.Realtime`.
- `CascadingRealtimeSession` wiring STT / inner `AIAgent` / TTS per
  cascade doc §1.
- `TextChunkingStrategy`, `VoiceActivityDetector` plug-point.
- `UseCascade` builder extension + `AsCascadingRealtimeAgent` fluent
  helper on `AIAgent`.
- `AppendInputTextAsync` / `CommitInputTextAsync` additions on
  `RealtimeSession` (cascade-doc §3.1 stretch). The current §4.1 surface
  has been chosen so these can be added additively (review §S1, cascade
  forward-compat).

---

## 7. Sequencing

1. Solution skeleton + `TestSupport` + Abstractions + Abstractions unit tests.
2. Realtime core + tests.
3. Foundry client + tests **and** OpenAI client + tests *(parallelizable)*.
4. Hosting shared + tests.
5. Foundry hosting (Invocations + VoiceLive encoder) + tests.

Cascade (§6) is not in the sequence.
