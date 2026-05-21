# RealtimeAgent Prototype — Summary

Snapshot of where the `RealtimeAgent` prototype under `/proto/impl` currently
stands: what landed, what passes, and what intentionally did not.

Authoritative design references: `/proto/realtime-agent.md`,
`/proto/realtime-hosting.md`, `/proto/session.md` §4 (type shapes),
`/proto/normalized-events.md` (event taxonomy), and `/proto/misc-notes.md`.

The plan that drove the work is `/proto/implementation-plan.md`
(after `implementation-plan-review.md` / `…-review-2.md`). Per-task notes
live under `/proto/implementation-notes/task01..task07`. Post-implementation
analysis: `/proto/client-implementation-gaps.md` and
`/proto/audio-pipe-performance.md`.

---

## What was done

### Solution + tooling (task 01)

- Standalone `proto/impl/realtime.slnx`, isolated from `/dotnet` by a
  per-proto `Directory.Build.props` / `Directory.Packages.props` and
  `global.json` (SDK pin `10.0.200`, rollForward `minor`).
- Single TFM: **`net10.0`**. `TreatWarningsAsErrors=true`.
  `MEAI001` / `OPENAI001` (M.E.AI / OpenAI experimental ids) globally
  suppressed; AF-side surface ships under `[Experimental("MEAI-REALTIME-001")]`.
- Centrally pinned packages: `Microsoft.Extensions.AI{,.Abstractions,.OpenAI}` 10.5.1,
  `Azure.Core` 1.55.0, `Azure.Identity` 1.21.0, `OpenAI` 2.10.0, plus the
  Microsoft.Extensions.Hosting / DI / Logging / System.* dependencies the
  realtime pipeline actually uses, and the xUnit v3 / Moq test stack.

### Type-surface decisions resolved (plan §3)

- **M.E.AI 10.5 is the public realtime surface.** AF does not invent a
  parallel `RealtimeSessionUpdate*` / `RealtimeClientEvent*` taxonomy. Only
  three additions ship on the AF side:
  - `InterruptedRealtimeServerMessage : RealtimeServerMessage` (gap-fill,
    `normalized-events.md` §6 G1; ADR-005).
  - `CancelResponseRealtimeClientMessage : RealtimeClientMessage` (typed
    seam so providers can pattern-match the cancel intent — M.E.AI 10.5 has
    no `response.cancel` client message; see task 02 deviation note).
  - A small set of **non-virtual convenience helpers** on `RealtimeSession`
    (`AppendInputAudioAsync`, `CommitInputAudioAsync`, `SendMessageAsync`,
    `RequestResponseAsync`, `CancelResponseAsync`).
- **`RealtimeSession` is already-connected** on construction; wraps
  `IRealtimeClientSession`; no `Serialize`/`Deserialize`; reuses
  `AgentSessionStateBag` (no `RealtimeSessionStateBag`).
- **No persistence in hosted realtime.** `HostedRealtimeAgent` is a thin
  `DelegatingRealtimeAgent`; no session store / registry (plan §3.4).
- **Auto tool invocation is opt-in** (`UseFunctionInvocation`), composing
  M.E.AI's `FunctionInvokingRealtimeClientSession` (ADR-003).
- **History is client-tracked** via a projection in the
  `Microsoft.Agents.AI.Realtime` core package, not `Abstractions` (ADR-004).
  Hosted layer does **not** own history.

### Packages built

| Package | Role | Notes |
| --- | --- | --- |
| `Microsoft.Agents.AI.Realtime.Abstractions` | Public surface | `RealtimeAgent`, `DelegatingRealtimeAgent`, `RealtimeAgentMetadata`, `RealtimeModality`, `RealtimeAgentRunContext`, `RealtimeFunctionInvocationContext`, `RealtimeAgentJsonUtilities`, the two AF-side message subtypes (above). 10 .cs files. |
| `Microsoft.Agents.AI.Realtime` | Concrete, non-provider | Builder + `Use(...)` plumbing; `Logging`, `OpenTelemetry`, `FunctionInvocation`, `AnonymousDelegating` decorators; `HistoryProjectingRealtimeSession`; `InMemoryRealtimeHistoryProvider`; `RealtimeAudioPipe` / `RealtimeAudioWriter`; `RealtimeAgentAsAIAgent` (transcript-only bridge). 19 .cs files. |
| `Microsoft.Agents.AI.Realtime.OpenAI` | OpenAI provider | Composes M.E.AI.OpenAI's `OpenAIRealtimeClient`; no bespoke transport. `OpenAIRealtimeAgent`, `OpenAIRealtimeAgentOptions`, `OpenAIRealtimeSession`. 3 .cs files. |
| `Microsoft.Agents.AI.Realtime.Foundry` | Foundry / VoiceLive provider | Hand-rolled prototype `IRealtimeClient` + `IRealtimeClientSession` + JSON encoder/projector behind an internal `IWebSocketTransport` seam (no real socket; tests inject a fake). 7 .cs files. |
| `Microsoft.Agents.AI.Realtime.Hosting` | Shared hosted-side primitives | `HostedRealtimeAgent`, `HostedRealtimeAgentBuilder`, `IRealtimeAgentTransport` / `IRealtimeAgentTransportContext` / `IRealtimeEventEncoder`, `RealtimeAgentTransportHandler`, `HostedRealtimeSessionContext`, `AddRealtimeAgent` keyed-DI extension. 6 .cs files. |
| `Microsoft.Agents.AI.Foundry.Hosting.Realtime` | Foundry-tier hosting + Invocations transport | `InvocationsRealtimeAgentTransportHandler` (POST + SSE behind `IInvocationsRequestSink`), `VoiceLiveInvocationsEventEncoder` (mirrors `vl_sample/hello-world-invocations-voicelive/main.py`), `NeutralRealtimeInvocationsEventEncoder` (control comparator), `AddFoundryRealtime` extension. Standalone — does **not** reference `Microsoft.Agents.AI.Foundry.Hosting`; the Common-extraction sharing is a tracked follow-up (plan §3.6 / §3.7). 5 .cs files. |

Cross-solution `ProjectReference` into `/dotnet/src` is used by
`Realtime.Abstractions` (for `AgentSessionStateBag`) and `Realtime`
(for `AIAgent` / `AIAgentBuilder`).

### Tests

| Project | Tests |
| --- | --- |
| `Microsoft.Agents.AI.Realtime.Abstractions.UnitTests` | 46 |
| `Microsoft.Agents.AI.Realtime.UnitTests` | 43 |
| `Microsoft.Agents.AI.Realtime.OpenAI.UnitTests` | 9 |
| `Microsoft.Agents.AI.Realtime.Foundry.UnitTests` | 15 |
| `Microsoft.Agents.AI.Realtime.Hosting.UnitTests` | 12 |
| `Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests` | 12 |
| **Total** | **137 / 137 passing** |

Shared `TestSupport/Microsoft.Agents.AI.Realtime.TestSupport` provides
`FakeRealtimeClient`, `FakeRealtimeClientSession`, `FakeWebSocketTransport`,
recorders, and DSL helpers used across the provider/hosting test projects.

### ADRs captured

- **ADR-001** — M.E.AI-as-public-surface vs parallel taxonomy → M.E.AI.
- **ADR-002** — Single-consumer enumeration of `GetStreamingResponseAsync`.
- **ADR-003** — Auto tool invocation default → **opt-in**.
- **ADR-004** — History projection owned by `Microsoft.Agents.AI.Realtime`
  (not `Abstractions`, not hosted).
- **ADR-005** — `RealtimeAgentInterruptedEvent` projection mechanism →
  subclass `RealtimeServerMessage` (`InterruptedRealtimeServerMessage`).

---

## What is intentionally out of scope this phase

Verbatim from the plan's non-goals (and the per-task scope notes):

- No integration tests against real OpenAI / Azure / Foundry endpoints.
- No WebRTC, SIP, ephemeral-token, or WebSocket *hosting* transport package.
- No Gemini, Anthropic, Nova Sonic providers.
- No DevUI, samples, or docs-site changes.
- No production-quality logging / OTel — exporter wiring is stubbed; the
  decorator emits spans and meters but no pipeline.
- No Foundry persistence; no session store / registry abstractions.
- No cascading agent **implementation** (only specified — see
  `clientside-cascade-agent.md`); the §4.1 type surface is chosen so
  `AppendInputTextAsync` / `CommitInputTextAsync` can be added additively.
- No `Microsoft.Agents.AI.Foundry.Hosting.Common` extraction (tracked
  follow-up; the proto duplicates the small bits it needs by composition
  rather than refactoring the existing package).
- No video I/O.

---

## Next steps

Ordered roughly by what unblocks the most downstream work.

### 1. Replace the bespoke Foundry transport with `Azure.AI.VoiceLive`

`client-implementation-gaps.md` recommends pivoting the Foundry provider
to the `Azure.AI.VoiceLive` SDK (`1.1.0-beta.4`) instead of growing the
current hand-rolled WebSocket / JSON encoder / projector stack. The SDK
already owns transport, auth, typed session options, typed server-event
models for the whole VoiceLive vocabulary, and a raw escape hatch
(`SendCommandAsync`). The AF work becomes:

- `FoundryRealtimeClient : IRealtimeClient` over `VoiceLiveClient`.
- `FoundryRealtimeClientSession : IRealtimeClientSession` over
  `VoiceLiveSession`.
- Adapter mapping from M.E.AI `RealtimeSessionOptions` /
  `RealtimeClientMessage` to `VoiceLiveSessionOptions` and typed
  `VoiceLiveSession` methods.
- Adapter mapping from `SessionUpdate` subclasses back to M.E.AI
  `RealtimeServerMessage` subclasses (preserving correlation fields).

This subsumes FND-1 through FND-13 in the gap doc.

### 2. Production OpenAI transport validation

The current OpenAI tests only exercise the AF wrapper, not the
M.E.AI.OpenAI realtime mapping. Add either (a) protocol-delegation tests
that pin the M.E.AI version contract, or (b) an explicit doc note that
this package relies on upstream M.E.AI coverage for OpenAI wire
compatibility (OAI-1 .. OAI-3).

### 3. Cascade agent

Specified in `clientside-cascade-agent.md` and
`clientside-cascade-agent-test.md`; not scheduled in this phase.
When it lands:

- `CascadingRealtimeAgent : RealtimeAgent` + `CascadingRealtimeAgentOptions`.
- `CascadingRealtimeSession` wiring STT → inner `AIAgent` → TTS.
- `TextChunkingStrategy`, `VoiceActivityDetector` plug-points.
- `UseCascade` builder extension + `AsCascadingRealtimeAgent` fluent
  helper on `AIAgent`.
- Additive `AppendInputTextAsync` / `CommitInputTextAsync` on
  `RealtimeSession`.

### 4. Hosting follow-ups

- Split the Invocations transport out of
  `Microsoft.Agents.AI.Foundry.Hosting.Realtime` into a reusable
  `Microsoft.Agents.AI.Realtime.Hosting.Invocations` package
  (plan §3.7 / `realtime-hosting.md` §3.3).
- Land the `Microsoft.Agents.AI.Foundry.Hosting.Common` extraction so
  the realtime variant can drop its proto-local duplicates and reuse
  `HostedSessionContext`, `HostedSessionIsolationKeyProvider`,
  `HostedAgentUserAgentPolicy`, `PlatformHostedSessionIsolationKeyProvider`,
  `HostedSessionJsonUtilities` directly (plan §3.6).
- Add a real WebSocket hosting transport package (today's hosting work
  is transport-neutral; only Invocations/SSE has an encoder).
- Per-connection isolation policy: `HostedRealtimeSessionContext` is
  intentionally minimal (key + caller identity); a real header-to-key
  policy needs to ship before multi-tenant hosting is meaningful.

### 5. Cross-cutting validation that was deferred

- Integration tests against real provider endpoints (the plan is
  explicit that this phase is unit-only).
- Session-handshake readiness contract: `ConnectSessionAsync` should
  arguably block until `session.updated` per `session.md`; today it
  returns once `session.update` is sent (FND-15).
- Send-concurrency serialization on the provider sessions per the
  M.E.AI `IRealtimeClientSession.SendAsync` remarks (FND-16).
- Reconnect-with-replay flow (out of scope until persistable session
  state exists — see `session.md` §4.5).

---

## Important gaps to address

Pulled from `client-implementation-gaps.md`, `audio-pipe-performance.md`,
and the per-task notes. Grouped by where the work has to happen.

### Foundry provider (highest-risk area)

These all assume the bespoke implementation stays. Most disappear once
the `Azure.AI.VoiceLive` adapter (Next-Steps §1) lands.

- **FND-1 — No production transport.** The internal `IWebSocketTransport`
  factory throws `NotSupportedException` in non-test code. There is no
  real WebSocket handshake, no URL/query-string construction
  (`wss://.../voice-live/realtime?api-version=…`), and no auth wiring
  for `TokenCredential` / `AzureKeyCredential`.
- **FND-2 — `session.update` payload is wrong on the wire.** The encoder
  serializes M.E.AI `RealtimeSessionOptions` directly (camelCase like
  `sessionKind`, `inputAudioFormat`, `voiceActivityDetection`). VoiceLive
  expects snake_case (`modalities`, `input_audio_format`, `turn_detection`,
  `voice` as an object, `input_audio_noise_reduction`,
  `input_audio_echo_cancellation`, `temperature`,
  `max_response_output_tokens`, etc.).
- **FND-3 — `RawRepresentationFactory` is documented as the Azure
  extension path but never invoked.** Azure-only knobs
  (`azure_semantic_vad`, `azure_deep_noise_suppression`,
  `server_echo_cancellation`, HD/custom voices, `rate`, timestamps,
  animation, avatar) cannot reach the wire today.
- **FND-4 — `conversation.item.create` doesn't match VoiceLive shape.**
  Missing `previous_item_id`, no per-content-part `type` mapping
  (`input_text`, `input_audio`, `function_call_output`, MCP approvals),
  no `RawRepresentation` honoring.
- **FND-5 — `response.create` doesn't match VoiceLive shape.** Emits
  M.E.AI property names; the spec expects `modalities`, `instructions`,
  `voice`, `output_audio_format`, `temperature`,
  `max_response_output_tokens`, `tools`, `tool_choice`.
- **FND-6 — `response.cancel` drops `ResponseId`.** AF carries it on
  `CancelResponseRealtimeClientMessage`; the encoder emits a bare
  `{ "type": "response.cancel" }`.
- **FND-7 — No `RawRepresentation` passthrough on outbound.** Provider
  events the typed set doesn't cover (`input_audio_buffer.clear`,
  `conversation.item.truncate/.delete`, Foundry `session.avatar.connect`)
  cannot be sent.
- **FND-8 — Audio append only handles in-memory `DataContent.Data`.**
  Data-URI / URI variants supported by M.E.AI are ignored.
- **FND-9 — Inbound projector covers ~5 of 30+ documented VoiceLive
  events.** Everything else is downgraded to a bare base
  `RealtimeServerMessage`, which loses the type signal middleware needs
  (`ResponseCreated`, `ResponseDone`, `ResponseOutputItemAdded`/`Done`,
  `ContentPart*`, transcription `delta`/`done`, function-call argument
  events, MCP events, `rate_limits.updated`, `error`, `warning`,
  Voice-Live-specific timestamp/viseme/blendshape/avatar events).
- **FND-10 — Projections drop correlation metadata.** `response_id`,
  `item_id`, `output_index`, `content_index`, `event_id`, raw
  representation — none of it survives projection.
- **FND-11 — No `*.done` finalization events** for text/audio/transcript.
- **FND-12 — `response.done` projection loses everything but the type.**
  No status, output items, usage, or error details. Tool-call detection
  and telemetry cannot work from this.
- **FND-13 — `error` / `warning` server events are not normalized to
  `ErrorRealtimeServerMessage` / raw warnings.**
- **FND-14 — Interruption only recognizes `output_audio_buffer.cleared`.**
  The primary documented WebSocket signal —
  `input_audio_buffer.speech_started` — is not mapped, and the matching
  outbound `conversation.item.truncate` raw event has no support.
- **FND-15 — No handshake-readiness wait.** `ConnectSessionAsync` returns
  immediately after the `session.update` write; `session.md` says
  construction should imply the provider handshake completed
  (server `session.created`/`session.updated`).
- **FND-16 — No send-concurrency serialization** on the Foundry session
  per the M.E.AI contract.

### OpenAI provider

- **OAI-1 — Tests verify wrapper behavior only.** Round-tripping is done
  via `FakeRealtimeClient`. The OpenAI Realtime wire requirements
  (`session.update`, `input_audio_buffer.append`,
  `conversation.item.create`, `response.create`/`response.cancel`,
  response-lifecycle and delta/done events) are exercised only by
  M.E.AI.OpenAI's own tests. AF has no regression guard if the
  dependency version, package wiring, or event mapping changes.
- **OAI-2 — No coverage of GA event-name expectations**
  (`response.output_text.delta`, `response.output_audio.delta`,
  `response.output_audio_transcript.delta`).
- **OAI-3 — `RealtimeSessionKind.Conversation` vs `Transcription`** is
  not exercised; translation is not represented at all.

### Audio pipeline

From `audio-pipe-performance.md` §"Gaps to address before production":

1. Document that `RealtimeAudioPipe` accepts already-framed `DataContent`
   — it doesn't read `Stream` / `PipeReader`.
2. Add queue-depth / back-pressure instrumentation so callers can tune
   capacity against real data.
3. Add tests for cancellation while blocked on a full channel and writes
   after completion/disposal.
4. Decide on an overflow strategy (today: `Wait` only) for callers whose
   capture API cannot block.
5. Source-generated JSON for realtime messages if Foundry JSON text
   frames stay in the hot path (today the JSON path is reflection-based,
   per `RealtimeAgentJsonUtilities` using `DefaultJsonTypeInfoResolver`).
6. Revisit the `RealtimeAudioWriter` name — currently an input-side view,
   not an output-audio writer.

### Hosting

- `HostedRealtimeSessionContext` is a key + caller-identity record only.
  No header-to-key extraction policy ships in this phase.
- `Microsoft.Agents.AI.Foundry.Hosting.Realtime` is standalone —
  duplicates nothing today because it doesn't need much, but the
  follow-up extraction of `Microsoft.Agents.AI.Foundry.Hosting.Common`
  is the right home for `HostedAgentUserAgentPolicy`,
  `PlatformHostedSessionIsolationKeyProvider`, and
  `HostedSessionJsonUtilities`.
- Invocations + VoiceLive encoder are currently fused into the Foundry
  hosting package; non-Foundry hosts that want the Invocations transport
  need it split out (plan §3.7).
- No real-WebSocket hosting transport; only Invocations / SSE is
  encoded.

### Documentation / process

- Several decisions land in implementation notes rather than ADRs proper
  (e.g. the `CancelResponseRealtimeClientMessage` deviation in task 02).
  Promote to ADRs if they outlive the prototype.
