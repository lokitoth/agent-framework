# RealtimeAgent — Client-side Test Plan

This document defines the **test projects** required to cover the
client-side realtime packages outlined in
[`realtime-agent.md`](./realtime-agent.md), modeled after the way
`/dotnet/tests` covers the corresponding text-agent packages
(`Microsoft.Agents.AI.Abstractions`, `Microsoft.Agents.AI`,
`Microsoft.Agents.AI.OpenAI`, …).

The mapping is 1:1 with the production packages — for every
`Microsoft.Agents.AI.Realtime.*` package there is a sibling
`Microsoft.Agents.AI.Realtime.*.UnitTests` project, and for every
provider package there is also a `*.IntegrationTests` project that hits
the real service. Naming, folder conventions, and `csproj` layout follow
the existing `/dotnet/tests` convention exactly.

Conventions reused from the existing dotnet test stack:

- Unit projects: `xUnit` + `Moq` + `Microsoft.Extensions.AI.Abstractions`
  test helpers; `TestJsonSerializerContext.cs` per project for source-gen
  serializer coverage; one `XTests.cs` file per production type.
- Integration projects: secrets via `dotnet user-secrets` + env, fixtures
  pattern (`OpenAIChatCompletionFixture`-style), `Support/` folder for
  shared helpers and cleanup hooks.
- Conformance: a shared `*.Conformance.IntegrationTests` project for
  cross-provider behavior (parallels `AgentConformance.IntegrationTests`).

---

## 1. `Microsoft.Agents.AI.Realtime.Abstractions.UnitTests`

Mirrors `Microsoft.Agents.AI.Abstractions.UnitTests`. Pure abstractions —
no transport, no provider, no I/O. Validates contracts, equality,
serialization, and AsyncLocal flow.

Files:

- `RealtimeAgentTests.cs` — abstract base contract: `Id`/`Name`/
  `Description` defaults, `IdCore` generation, `GetService<T>()` locator
  (parallels `AIAgentTests.cs`).
- `RealtimeAgentMetadataTests.cs` — metadata round-trip (provider,
  model, modalities, audio codecs, VAD/interruption/video capability
  flags).
- `DelegatingRealtimeAgentTests.cs` — decorator forwards every member
  to inner (parallels `DelegatingAIAgentTests.cs`); guards against
  missing overrides via reflection.
- `RealtimeSessionTests.cs` — abstract base: `State` transitions
  (`Connecting`→`Open`→`Closing`→`Closed`/`Faulted`), single-consumer
  enforcement of `ReceiveUpdatesAsync`, `DisposeAsync`/`CloseAsync`
  idempotency, `History` read-only contract.
- `RealtimeSessionStateBagTests.cs` — `StateBag` semantics
  (set/get/remove, JSON round-trip via source-gen) — parallels
  `AgentSessionStateBagTests.cs`.
- `RealtimeConnectOptionsTests.cs` / `RealtimeSessionUpdateOptionsTests.cs`
  — defaults, null-vs-default semantics, deep-clone behavior used by
  decorators.
- `RealtimeSessionUpdateTests.cs` — discriminator dispatch for every
  concrete `…Update` subtype; ensures `Kind` + `RawProviderEvent`
  preserved across serialization.
  - One `[Theory]` per family: input-audio, speech-VAD, item lifecycle,
    response lifecycle, output-audio, output-text/transcript,
    function-call, rate-limit, error.
- `RealtimeClientEventTests.cs` — mirror coverage for outbound events
  (`InputAudioAppendEvent`, `ResponseCreateEvent`, `SessionUpdateEvent`,
  `ItemCreateEvent`, `ResponseCancelEvent`, …).
- `RealtimeItemTests.cs` — `Id`/`Role`/`Contents`/`Status` round-trip;
  rejects invalid status transitions when the model enforces them.
- `RealtimeAudioContentTests.cs` — `DataContent` derivation, format
  preservation, `[Experimental]` attribute presence.
- `RealtimeAudioFormatTests.cs` — equality, PCM/G.711/Opus default
  rates, serialization keys.
- `RealtimeModalityTests.cs` — `[Flags]` arithmetic
  (`Text|Audio`/`Audio|Video`), JSON serialization of flag values.
- `RealtimeVoiceTests.cs` — well-known voice id equality + custom voice
  pass-through.
- `TurnDetectionOptionsTests.cs` — polymorphism between
  `ServerVoiceActivityDetection` / `SemanticVoiceActivityDetection` /
  `NoneTurnDetection` / `ClientPushToTalk`; default-VAD fallbacks; JSON
  source-gen round-trip.
- `RealtimeFunctionInvocationContextTests.cs` — `SessionCancellationToken`
  fires on response cancel; back-reference to session is non-null.
- `RealtimeAgentRunContextTests.cs` — AsyncLocal scoping across awaits,
  nested context disposal (parallels `AgentRunContextTests.cs`).
- `RealtimeAgentJsonUtilitiesTests.cs` — `[JsonSerializable]` context
  covers every public type; round-trips `RealtimeSession.StateBag`,
  `RealtimeItem`, and every update/event subtype (parallels
  `AgentAbstractionsJsonUtilitiesTests.cs`).
- `Models/` — small POCO fixtures shared across tests (mirrors
  `Microsoft.Agents.AI.Abstractions.UnitTests/Models`).
- `TestJsonSerializerContext.cs` — source-gen context for the test
  payloads.

---

## 2. `Microsoft.Agents.AI.Realtime.UnitTests`

Mirrors `Microsoft.Agents.AI.UnitTests`. Concrete building blocks that
do not require a provider — builder, decorators, bridge, session
utilities, audio helpers. Uses a `TestRealtimeAgent` / `TestRealtimeSession`
in-memory fake (parallels `TestAIAgent.cs`) so no socket I/O occurs.

Files:

- `TestRealtimeAgent.cs` — in-memory `RealtimeAgent` with a scripted
  update stream; sibling of `TestAIAgent.cs`.
- `TestRealtimeSession.cs` — in-memory session with a `Channel<RealtimeSessionUpdate>`
  exposed for the test to feed inbound updates, and a list of received
  `RealtimeClientEvent`s for assertions.
- `RealtimeAgentBuilderTests.cs` — `Use(...)` ordering, `Build(IServiceProvider?)`
  resolution, double-build idempotency, factory delegation (parallels
  `AIAgentBuilderTests.cs`).
- `LoggingRealtimeAgentTests.cs` — log scope per session, audio bytes
  logged as length, PII-redaction hook fires (parallels
  `LoggingAgentTests.cs`).
- `LoggingRealtimeAgentBuilderExtensionsTests.cs` — `UseLogging`
  registers the decorator once and respects `ILoggerFactory` resolution
  order.
- `OpenTelemetryRealtimeAgentTests.cs` — spans emitted for
  `realtime.connect` / `realtime.response` / `realtime.tool_call`;
  metrics for audio bytes in/out, time-to-first-audio, interruption
  count (parallels `OpenTelemetryAgentTests.cs`). Uses
  `TestActivityListener` + `MeterListener`.
- `OpenTelemetryRealtimeAgentBuilderExtensionsTests.cs` — `UseOpenTelemetry`
  registration semantics.
- `FunctionInvocationRealtimeAgentTests.cs` — listens for
  `FunctionCallInvokedUpdate`, invokes the `AIFunction`, sends
  `function_call_output` back; cancels on response-cancel; surfaces
  exceptions as `FunctionCallCancelledUpdate` + log (parallels
  `FunctionInvocationDelegatingAgentTests.cs`).
- `AnonymousDelegatingRealtimeAgentTests.cs` — inline factory wires up
  correctly (parallels `AnonymousDelegatingAIAgentTests.cs`).
- `RealtimeAgentAsAIAgentTests.cs` — bridge to `AIAgent`: text input →
  open transient session → collect `OutputTextDeltaUpdate`/
  `OutputTranscriptDeltaUpdate` → produce `AgentResponse`. Asserts:
  - Audio output surfaced via `AgentResponse.AdditionalProperties`
    (decision §4.5 from the design doc).
  - Streaming variant returns `AgentResponseUpdate` chunks.
  - Tool calls flow through if `UseFunctionInvocation` is registered.
  - Cancellation propagates.
- `AIAgentAsRealtimeAgentTests.cs` — *(optional)* reverse bridge for
  local-loop testing; only built when a fake TTS/STT pair is injected.
- `SessionCompactionRealtimeAgentTests.cs` — periodic summarization
  replaces old `RealtimeItem`s with a summary item; honors compaction
  triggers (parallels the `Compaction/` folder for the text agent).
- `InMemoryRealtimeHistoryProviderTests.cs` — append, replay, snapshot,
  enumerate ordering (parallels `InMemoryChatHistoryProviderTests.cs`).
- `RealtimeAudioPipeTests.cs` — frames `Stream`/`PipeReader` of PCM into
  `AppendInputAudioAsync` with the correct chunk size; back-pressure
  honored; cancellation tears down both ends.
- `RealtimeAudioWriterTests.cs` — fans `OutputAudioDeltaUpdate` into a
  `PipeWriter`/`Stream`; preserves frame boundaries; flushes on
  `OutputAudioDoneUpdate`.
- `RealtimeDiagnosticIdsTests.cs` — every public type with
  `[Experimental(MEAI-REALTIME-…)]` matches the documented id list.
- `TestJsonSerializerContext.cs` — source-gen context for the test
  payloads.

---

## 3. Provider unit-test projects

One unit-test project per provider package. Each focuses on
**provider-event → `RealtimeSessionUpdate`** mapping and
**`RealtimeClientEvent` → wire-message** mapping, using captured event
fixtures rather than live sockets. Mirrors the way
`Microsoft.Agents.AI.OpenAI.UnitTests` covers `Microsoft.Agents.AI.OpenAI`.

### 3.1 `Microsoft.Agents.AI.Realtime.OpenAI.UnitTests`

Files:

- `OpenAIRealtimeAgentTests.cs` — builder extension
  `AsRealtimeAgent(this RealtimeConversationClient, …)`, metadata
  surface, transport selection (`WebSocket` vs `WebRtc`).
- `OpenAIRealtimeAgentOptionsTests.cs` — option defaults, validation.
- `OpenAIRealtimeSessionTests.cs` — happy-path connect, send/receive
  pump, close; uses a fake `WebSocket`/`IDuplexPipe`.
- `OpenAIRealtimeEventMappingTests.cs` — **golden-file** tests:
  - Inbound: every `session.*`, `input_audio_buffer.*`,
    `conversation.item.*`, `response.*`, `response.function_call_arguments.*`,
    `rate_limits.updated`, `error` event from a captured fixture maps
    to the correct `RealtimeSessionUpdate` subtype and preserves
    `RawProviderEvent`.
  - Outbound: every `RealtimeClientEvent` serializes to the documented
    OpenAI shape (`session.update`, `input_audio_buffer.append`,
    `conversation.item.create`, `response.create`, `response.cancel`).
- `OpenAIRealtimeReconnectTests.cs` — `ReconnectAsync` replays
  instructions, tools, and history via `session.update` +
  `conversation.item.create`; ordering is deterministic.
- `OpenAIRealtimeInterruptionTests.cs` — `speech_started` mid-response
  surfaces a single `ResponseCancelledUpdate` and truncates the local
  assistant item so `History` stays consistent (design §3.1).
- `OpenAIRealtimeFunctionCallingTests.cs` — `…arguments.delta` buffer
  flushes only after `…done`; result submitted via
  `conversation.item.create` + `response.create`.
- `OpenAIEphemeralTokenProviderTests.cs` — REST default impl: token
  request payload shape, error mapping, key-never-logged invariant.
- `Fixtures/` — captured JSON event fixtures (one file per event
  family); also used as the wire-format spec.

### 3.2 `Microsoft.Agents.AI.Realtime.AzureOpenAI.UnitTests`

Files:

- `AzureOpenAIRealtimeAgentTests.cs` — builder extension; deployment-
  vs-model name handling.
- `AzureOpenAIRealtimeAgentOptionsTests.cs` — option validation,
  endpoint URI shape (`/openai/realtime?deployment=…&api-version=…`).
- `AzureOpenAIAuthTests.cs` — `TokenCredential` flow; API-key path
  marked dev-only; never emitted into logs/headers in error paths.
- `AzureContentFilterUpdateTests.cs` — `content_filter_results` mapped
  to typed update; raw preserved.
- `AzureCustomTransportTests.cs` — custom `Uri`, `HttpClient`/`WebSocket`
  factory accepted and used.
- (Shared-internals coverage for `OpenAIRealtimeSession` lives in
  `Microsoft.Agents.AI.Realtime.OpenAI.UnitTests`; this project asserts
  only Azure-specific deltas.)

### 3.3 `Microsoft.Agents.AI.Realtime.Google.Gemini.UnitTests`

Files:

- `GeminiRealtimeAgentTests.cs` — builder, metadata.
- `GeminiRealtimeSessionTests.cs` — `setup`/`setupComplete` handshake
  must precede any data; failure paths.
- `GeminiBidiMessageMappingTests.cs` — `BidiGenerateContentClientMessage`
  / `…ServerMessage` ↔ `RealtimeClientEvent` / `RealtimeSessionUpdate`
  with golden fixtures; base64 audio decode for `audio/pcm;rate=16000`
  in, `…rate=24000` out.
- `GeminiServerToolsTests.cs` — `RealtimeServerTool` (Google Search,
  code execution) declared on connect; surfaced as opaque
  capability — not invoked locally.
- `GeminiTurnDetectionTests.cs` — `clientContent.turnComplete` for the
  `None` boundary; `semantic_vad` mapped to default server VAD.
- `GeminiInterruptionTests.cs` — `serverContent.interrupted=true` →
  `ResponseCancelledUpdate`.
- `GeminiSessionExpirationTests.cs` — auto-reconnect-with-replay;
  `SessionWillExpireUpdate` surfaced before the deadline.
- `GeminiCredentialProviderTests.cs` — API-key vs ADC selection.

### 3.4 `Microsoft.Agents.AI.Realtime.AwsNovaSonic.UnitTests` *(deferred until package ships)*

Files (skeleton only for v1):

- `NovaSonicRealtimeAgentTests.cs`
- `NovaSonicEventStreamCodecTests.cs` — `:event-type` framing,
  bidirectional decode/encode against captured fixtures.
- `NovaSonicToolUseTests.cs` — `toolUse`/`toolResult` mapped to
  `AIFunction` invocation.
- `NovaSonicSigV4Tests.cs` — credentials flow uses AWS SDK chain only.

---

## 4. Provider integration-test projects

Mirror the existing `OpenAIChatCompletion.IntegrationTests` /
`AnthropicChatCompletion.IntegrationTests` shape: they hit the real
service, use `user-secrets` for credentials, are gated by
`[Trait("Category","Integration")]`, and use a per-suite fixture for
session teardown.

### 4.1 `OpenAIRealtime.IntegrationTests`

Files:

- `OpenAIRealtimeFixture.cs` — secrets, client construction, audio
  fixture loader (a short PCM-16 clip in `Assets/`).
- `OpenAIRealtimeConnectTests.cs` — connect → receive `session.created`
  → close; metadata advertises model + modalities discovered.
- `OpenAIRealtimeAudioInOutTests.cs` — send a 1–2 s PCM clip with server
  VAD enabled, await `OutputAudioDoneUpdate`, decode transcript, assert
  non-empty transcript and well-formed audio (sample rate / channels).
- `OpenAIRealtimeTextInOutTests.cs` — text-only conversation: send
  `ChatMessage`, await response, assert transcript ordering.
- `OpenAIRealtimeFunctionCallingTests.cs` — `MenuPlugin`-style tool
  (reused from `AgentConformance.IntegrationTests`); model invokes
  function, result submitted, follow-up response generated.
- `OpenAIRealtimeInterruptionTests.cs` — start a response, send audio
  mid-flight, assert single `ResponseCancelledUpdate` and truncated
  assistant item.
- `OpenAIRealtimeReconnectTests.cs` — force-close the underlying socket
  mid-session, call `ReconnectAsync`, verify replayed state preserved
  across the gap.
- `OpenAIRealtimeRateLimitsTests.cs` — drive enough load to surface a
  `RateLimitsUpdate`; assert the payload is parsable.
- `OpenAIRealtimeAgentAsAIAgentTests.cs` — bridge surface: drive the
  same MenuPlugin scenario via `AIAgent.RunAsync`/`RunStreamingAsync`
  through `RealtimeAgentAsAIAgent`.
- `Assets/` — small PCM-16 clip(s) and expected-transcript text.

### 4.2 `AzureOpenAIRealtime.IntegrationTests`

Same suites as 4.1, plus:

- `AzureOpenAIRealtimeAuthTests.cs` — both `TokenCredential` and
  API-key paths against a configured Azure deployment.
- `AzureOpenAIRealtimeContentFilterTests.cs` — driving a prompt that
  trips the filter surfaces an `AzureContentFilterUpdate`.
- `AzureOpenAIRealtimePrivateEndpointTests.cs` — *(gated)* custom Uri /
  `HttpClient` factory wires through correctly.

### 4.3 `GoogleGeminiRealtime.IntegrationTests`

Files:

- `GeminiRealtimeFixture.cs`
- `GeminiRealtimeHandshakeTests.cs` — `setup`/`setupComplete`.
- `GeminiRealtimeAudioInOutTests.cs` — 16 kHz in / 24 kHz out.
- `GeminiRealtimeFunctionCallingTests.cs` — client `AIFunction`.
- `GeminiRealtimeServerToolsTests.cs` — Google Search opt-in.
- `GeminiRealtimeInterruptionTests.cs`.
- `GeminiRealtimeSessionExpirationTests.cs` — long-running session;
  `SessionWillExpireUpdate` arrives before the 15-min cliff.

### 4.4 `AwsNovaSonicRealtime.IntegrationTests` *(deferred)*

Skeleton matching 4.1.

---

## 5. `RealtimeAgentConformance.IntegrationTests`

Sibling of `AgentConformance.IntegrationTests`. One shared test class
per behavior, parameterized by an `IRealtimeAgentFixture` so every
provider runs the same suite. Ensures cross-provider parity.

Files:

- `IRealtimeAgentFixture.cs` — fixture contract: `Task<RealtimeAgent>
  CreateAgentAsync()`, supported-capability flags, sample-rate hints,
  optional `MenuPlugin` registration.
- `ConnectAndCloseTests.cs` — open, observe `SessionCreatedUpdate`,
  close cleanly across providers.
- `AudioRoundTripTests.cs` — send PCM, receive transcript + audio.
- `TextRoundTripTests.cs` — text-only conversations.
- `FunctionCallingTests.cs` — `MenuPlugin` (reused), assert tool call →
  result → follow-up.
- `InterruptionTests.cs` — mid-response barge-in.
- `HistoryReconstructionTests.cs` — local `History` matches the
  server's view after a fixed scripted turn sequence.
- `SessionUpdateMidConnectionTests.cs` — change instructions/voice/VAD
  via `UpdateSessionAsync` and assert the next response uses the new
  settings.
- `CancelResponseTests.cs` — client-driven `CancelResponseAsync` works
  on every provider.
- `Support/` — `AgentCleanup`, `SessionCleanup`, `Constants`,
  `TestConfiguration` (mirrors the existing folder in
  `AgentConformance.IntegrationTests`).
- `ConformanceTraces/` — golden activity traces per provider for the
  audio-round-trip scenario (parallels the existing folder in
  `Microsoft.Agents.AI.Hosting.OpenAI.UnitTests`).

Each provider integration project listed in §4 exposes an
`IRealtimeAgentFixture` implementation registered into this conformance
suite — so the same scenarios run unmodified against OpenAI,
AzureOpenAI, Gemini, and (eventually) Nova Sonic.

---

## 6. Cross-cutting test concerns

These apply across all unit and integration projects above:

1. **Source-gen serialization.** Every public `[JsonSerializable]` type
   in `Realtime.Abstractions` is exercised by both reflection-based and
   source-gen contexts in `RealtimeAgentJsonUtilitiesTests` and provider
   `…EventMappingTests`. Matches `AgentJsonUtilitiesTests` pattern.
2. **`RawProviderEvent` fidelity.** Every inbound mapping test asserts
   the raw `JsonElement` is preserved verbatim so consumers can rely on
   forward-compat. Outbound encoders are property-tested where feasible
   (`FsCheck`-style round-trips).
3. **Cancellation.** Every async API in `RealtimeSession` is asserted
   to honor `CancellationToken` *both* at the public surface and inside
   the inbound pump (single-consumer enforcement). Covered in
   `RealtimeSessionTests` and each provider session test.
4. **Single-consumer invariant.** `ReceiveUpdatesAsync` rejects a
   second concurrent enumerator with a defined exception type
   (`InvalidOperationException` per design §4.1 decision); covered in
   the abstractions project and re-asserted per provider.
5. **No-secret-leak.** Logging + OTel decorator tests assert that audio
   bytes are length-only and that API keys / bearer tokens never
   appear in any emitted log scope or span attribute.
6. **AOT/trim safety.** `Microsoft.Agents.AI.Realtime.Abstractions` and
   `Microsoft.Agents.AI.Realtime` are exercised under
   `PublishTrimmed=true` in a small smoke-test project (sibling of any
   existing trim tests in `/dotnet/tests`) — gated by a CI lane, not
   per-PR.
7. **`[Experimental]` discipline.** A single reflection-based test in
   `RealtimeDiagnosticIdsTests` asserts every public type/member in the
   realtime surface carries the documented experimental id and that no
   new id appears without a matching doc entry.

---

## 7. Test-project dependency graph

```
Microsoft.Agents.AI.Realtime.Abstractions.UnitTests
        ▲
        │
Microsoft.Agents.AI.Realtime.UnitTests
        ▲
        ├── Microsoft.Agents.AI.Realtime.OpenAI.UnitTests
        ├── Microsoft.Agents.AI.Realtime.AzureOpenAI.UnitTests
        ├── Microsoft.Agents.AI.Realtime.Google.Gemini.UnitTests
        └── Microsoft.Agents.AI.Realtime.AwsNovaSonic.UnitTests   (deferred)

OpenAIRealtime.IntegrationTests          ─┐
AzureOpenAIRealtime.IntegrationTests      ├──▶ RealtimeAgentConformance.IntegrationTests
GoogleGeminiRealtime.IntegrationTests     │
AwsNovaSonicRealtime.IntegrationTests     ─┘   (deferred)
```

The conformance project does **not** reference the provider
integration projects directly; each provider project registers its
`IRealtimeAgentFixture` and lights up the shared suite via xUnit
collection fixtures (same pattern used by `AgentConformance.IntegrationTests`
today).
