# Client-side Cascading RealtimeAgent — Test Plan

This document defines the **test coverage** required for the
`CascadingRealtimeAgent` design outlined in
[`clientside-cascade-agent.md`](./clientside-cascade-agent.md), in the
same shape as the test plans for the
[client-side](./realtime-agent-test.md) and
[hosting-side](./realtime-hosting-test.md) realtime stack.

The cascading agent is a **single concrete `RealtimeAgent`** that lives
in `Microsoft.Agents.AI.Realtime` (per design §2.1). Its tests therefore
live inside the existing `Microsoft.Agents.AI.Realtime.UnitTests`
project (see [`realtime-agent-test.md`](./realtime-agent-test.md) §2),
organized under a dedicated `Cascade/` subfolder — mirroring how the
text agent's `Compaction/` and `ChatClient/` folders sit inside
`Microsoft.Agents.AI.UnitTests`.

A separate `CascadingRealtimeAgent.IntegrationTests` project covers the
end-to-end pairing against real STT + chat + TTS endpoints, parallel to
the per-provider integration projects under
[`realtime-agent-test.md`](./realtime-agent-test.md) §4.

Conventions reused from the existing `/dotnet/tests` stack are
identical to those documented in the sibling test plans: xUnit + Moq;
one `XTests.cs` per production type; `Fixtures/`, `Support/`,
`ConformanceTraces/`, `TestJsonSerializerContext.cs`; secrets via
`dotnet user-secrets`; ASP.NET integration via `WebApplicationFactory<>`
where needed.

---

## 1. `Microsoft.Agents.AI.Realtime.UnitTests/Cascade/` *(new folder)*

Lives alongside the existing realtime client-side unit tests
(see [`realtime-agent-test.md`](./realtime-agent-test.md) §2). All
tests in this folder use fake `IRealtimeClient` / `IRealtimeClientSession`
implementations for STT and TTS — no real socket, no real provider —
and a `TestAIAgent` (already present in
`Microsoft.Agents.AI.UnitTests`) for the inner agent.

### 1.1 Fakes & test helpers

Files:

- `Cascade/Fakes/FakeRealtimeClient.cs` — opens scripted
  `FakeRealtimeClientSession` instances; advertises configurable
  metadata (`SupportsServerVad`, `SupportsStreamedTextInput`,
  supported audio formats).
- `Cascade/Fakes/FakeRealtimeClientSession.cs` — exposes a
  `Channel<RealtimeServerMessage>` the test feeds inbound messages
  through and a list of received `RealtimeClientMessage`s for
  assertions; configurable connect/close/fault behavior.
- `Cascade/Fakes/RecordingVoiceActivityDetector.cs` — programmable
  client-side VAD that emits `SpeechStarted`/`SpeechStopped` on
  demand.
- `Cascade/Fakes/FakeStreamingTextToSpeechClient.cs` — implements the
  internal `IStreamingTextToSpeechClient` fast-path from design §3.2.
- `Cascade/Fakes/RecordingInnerAgent.cs` — `AIAgent` wrapper that
  records every `RunStreamingAsync` call and lets the test drive the
  emitted `AgentResponseUpdate` stream.

### 1.2 Construction & options

Files:

- `Cascade/CascadingRealtimeAgentTests.cs` — `Metadata` shape
  (`Provider="cascade"`, model name inherited from inner agent,
  modalities, capability flags per design §2.5); `GetService<T>()`
  returns inner agent, STT/TTS clients, and the cascade itself.
- `Cascade/CascadingRealtimeAgentOptionsTests.cs` — required-property
  validation (`InnerAgent` / `SpeechToText` / `TextToSpeech` non-null);
  optional defaults; `TurnDetection = Auto` resolves to `Server` when
  STT advertises VAD, otherwise `None`; `TextChunking = Token`
  downgrades to `Sentence` with a single warning when TTS doesn't
  advertise streamed-text support (design §1.4).
- `Cascade/CascadingRealtimeAgentBuilderExtensionsTests.cs` —
  `UseCascade(...)` is a terminal factory; composes correctly under
  `UseLogging` / `UseOpenTelemetry` / `UseFunctionInvocation` (design
  §2.3 (b)).
- `Cascade/AsCascadingRealtimeAgentExtensionsTests.cs` — fluent
  `AIAgent.AsCascadingRealtimeAgent(...)` (design §2.3 (c)); returned
  agent's `GetService<AIAgent>()` is the same instance that was wrapped.

### 1.3 Session surface mapping

One file per row in design §2.4 table:

- `Cascade/CascadingRealtimeSessionTests.cs` — connect ordering (STT →
  inner thread → TTS, per design §1.5); reverse-order disposal;
  half-open recovery on TTS-connect failure surfaces a typed
  `CascadeConnectException`; `State` transitions
  (`Connecting`→`Open`→`Closing`→`Closed`/`Faulted`); single-consumer
  `ReceiveUpdatesAsync` invariant.
- `Cascade/CascadingSession_AppendInputAudioTests.cs` — PCM forwarded
  to STT *and* to client VAD when configured; zero-copy where transport
  allows (no defensive `.ToArray()` on the hot path — asserted via
  `ReadOnlyMemory<byte>` identity check on the fake).
- `Cascade/CascadingSession_CommitInputAudioTests.cs` — forwarded to
  STT only; idempotent when called before any audio appended.
- `Cascade/CascadingSession_SendMessageTests.cs` — text injection
  bypasses STT, is appended to the inner agent's thread, triggers a
  response (so text-only chat works in the same session); STT receives
  no messages.
- `Cascade/CascadingSession_RequestResponseTests.cs` — runs the inner
  agent against the current thread, then runs TTS over the output;
  emits `ResponseStartedUpdate` before any output delta.
- `Cascade/CascadingSession_UpdateSessionTests.cs` — `UpdateSessionOptions`
  is split correctly: instructions/tools → inner agent's `ChatOptions`,
  voice/audio format → TTS sub-session, VAD config → STT sub-session;
  partial updates leave non-mentioned fields untouched.
- `Cascade/CascadingSession_SendToolResultTests.cs` — routed to the
  inner agent's function-result pathway; neither STT nor TTS receives
  the result.
- `Cascade/CascadingSession_HistoryTests.cs` — projection over
  `AgentThread.Messages` plus synthetic `RealtimeItem`s for
  function-call parity; read-only contract honored; reflects truncation
  after barge-in (see §1.4 below).
- `Cascade/CascadingSession_ReceiveUpdatesTests.cs` — merged stream
  carries: mapped STT updates (`InputTranscription*`,
  `Speech*`), mapped inner-agent updates (`OutputText*`,
  `ItemCreatedUpdate`, `FunctionCall*`), mapped TTS updates
  (`OutputAudio*`), and synthetic cascade-level updates
  (`ResponseStartedUpdate`, `ResponseCompletedUpdate`,
  `ResponseCancelledUpdate`, `ConnectionStateChangedUpdate`); deterministic
  ordering when sub-streams interleave (assertion uses a virtual clock
  on the fakes).

### 1.4 Turn detection & barge-in

Files:

- `Cascade/CascadeTurnDetection_ServerTests.cs` — STT-emitted
  `SpeechStopped` / `InputTranscriptionCompleted` triggers an inner-agent
  run; new `SpeechStarted` mid-response triggers internal
  `CancelResponseAsync`. Asserts the full barge-in sequence from design
  §1.3:
  1. text deltas to TTS stop,
  2. TTS-side cancel issued,
  3. assistant `RealtimeItem` truncated at the byte the client reported
     played via `NotifyPlaybackPositionAsync(itemId, sampleOffset)`,
  4. inner agent's `RunStreamingAsync` `CancellationToken` fires.
- `Cascade/CascadeTurnDetection_ClientVadTests.cs` — pluggable
  `VoiceActivityDetector` sees the same PCM forwarded to STT; client
  VAD's `SpeechStopped` drives `CommitInputAudioAsync` on STT; barge-in
  flow identical to the server-VAD case.
- `Cascade/CascadeTurnDetection_NoneTests.cs` — push-to-talk:
  no automatic commit; explicit caller-driven `CommitInputAudioAsync`
  and `CancelResponseAsync` required and respected.
- `Cascade/CascadeTurnDetection_AutoSelectionTests.cs` — `Auto` resolves
  per design §1.3 (server when STT supports it, else none); decision is
  logged once at connect.
- `Cascade/NotifyPlaybackPositionTests.cs` — without a reported
  playback position, truncation falls back to the last delta written
  to TTS (documented best-effort).

### 1.5 Inner-agent → TTS text chunking

Files:

- `Cascade/TextChunking_SentenceTests.cs` — buffers text deltas;
  flushes on `.`, `!`, `?`, `\n`, and on max-chars threshold; flushes
  remaining buffer at response end; respects multi-byte / surrogate
  pair boundaries.
- `Cascade/TextChunking_TokenTests.cs` — forwards every delta to TTS
  immediately; surfaces an exception if TTS doesn't advertise
  streamed-text support unless the auto-downgrade kicked in at connect.
- `Cascade/TextChunking_CustomTests.cs` — user-supplied
  `Func<...>` is invoked for every delta; arbitrary boundary logic
  honored.
- `Cascade/TextChunking_FlushOnCancelTests.cs` — on
  `CancelResponseAsync` the buffered (un-flushed) text is **not**
  shipped to TTS.
- `Cascade/TextChunking_StreamingTtsFastPathTests.cs` — when
  `StreamingTextToSpeech` (design §3.2) is supplied, deltas flow
  through `IStreamingTextToSpeechClient.AppendTextAsync`; the
  `IRealtimeClient`-typed `TextToSpeech` is **not** used.

### 1.6 Streaming text input (`AppendInputTextAsync` /
`CommitInputTextAsync`)

Per design §3.1, these helpers also exist on the cascading session.

Files:

- `Cascade/CascadingSession_AppendInputTextTests.cs` — text deltas
  buffer into the inner agent's *next user message*, bypassing STT;
  `CommitInputTextAsync` triggers an inner-agent run + TTS exactly
  like §1.2 of the design.
- `Cascade/AppendInputText_TextOnlyOverVoiceTests.cs` — single-call
  shortcut (`AppendInputTextAsync("hello") + CommitInputTextAsync`)
  reaches a complete response without ever touching STT.

> Coverage of the same helpers on **native** realtime agents is in
> per-provider unit tests
> ([`realtime-agent-test.md`](./realtime-agent-test.md) §3). Those tests
> assert the OpenAI / Azure / Gemini mapping; the cascade tests above
> assert the inner-agent + TTS routing.

### 1.7 Errors & lifecycle

Files:

- `Cascade/CascadeErrorPropagation_SttFaultTests.cs` — STT recoverable
  error → `RealtimeErrorUpdate`; session stays open. Fatal STT
  fault → `ConnectionStateChangedUpdate(Faulted)` + the cascade is
  faulted as a whole; the TTS sub-session is closed cleanly during
  teardown (design §1.5).
- `Cascade/CascadeErrorPropagation_TtsFaultTests.cs` — symmetrical
  for the TTS side.
- `Cascade/CascadeErrorPropagation_InnerAgentExceptionTests.cs` —
  inner-agent throws → synthetic `RealtimeErrorUpdate` with the
  exception detail; current response aborted; session still accepts
  the next user turn (same contract as a throwing tool, per design
  §1.5).
- `Cascade/CascadeConnect_OrderingTests.cs` — STT-then-TTS connect
  ordering; TTS-connect failure surfaces `CascadeConnectException` and
  the partially-open STT session is closed.
- `Cascade/CascadeDispose_OrderingTests.cs` — `DisposeAsync` /
  `CloseAsync` tear down TTS first, then inner agent, then STT;
  exceptions on one teardown step do not prevent the others.
- `Cascade/CascadeCancellation_PropagationTests.cs` — caller's
  `CancellationToken` on any `RealtimeSession` API cancels in-flight
  STT/TTS operations and any active inner-agent run.

### 1.8 Function calling

Files:

- `Cascade/CascadeFunctionInvocation_DecoratorTests.cs` —
  `UseFunctionInvocation()` decorator listens for
  `FunctionCallInvokedUpdate` from the inner-agent side, invokes the
  `AIFunction`, and routes the result through
  `SendToolResultAsync`; the result is fed into the inner agent's
  next turn (not into the TTS sub-session).
- `Cascade/CascadeFunctionInvocation_AudioInterjectionTests.cs` —
  *(per open question §2 in the design)* when a tool calls
  `Context.SpeakAsync(text)` mid-execution, the text is routed through
  the TTS sub-session and produces an `OutputAudio*` delta sequence;
  ordering against the assistant's own ongoing TTS is documented and
  asserted. Marked `[Trait("Status","Provisional")]` until the API is
  finalized.

### 1.9 Serialization

Files:

- `Cascade/CascadeSerialization_TranscriptsOnlyTests.cs` —
  `SerializeSessionAsync` round-trips transcripts and inner-agent
  history but **not** raw audio (design §4 Q3 default).
- `Cascade/CascadeSerialization_WithAudioRecordingTests.cs` —
  `UseAudioRecording()` opt-in decorator captures both directions and
  the captured audio survives a round-trip.
- `Cascade/CascadeSerialization_JsonContextTests.cs` — all cascade-
  specific options/state types are reachable from
  `RealtimeAgentJsonUtilities` source-gen context (asserted via
  reflection like
  `RealtimeAgentJsonUtilitiesTests` does for the base surface).

### 1.10 Telemetry

Files:

- `Cascade/CascadeOpenTelemetryTests.cs` — `realtime.connect` span
  carries `cascade.stt.provider` / `cascade.tts.provider` /
  `cascade.inner_agent` baggage; per-response spans tag
  `cascade.text_chunking`, `cascade.turn_detection`, and emit metrics
  for "STT bytes in", "TTS bytes out", "time-to-first-audio",
  "interruption count".
- `Cascade/CascadeLoggingTests.cs` — connect/disconnect/send/receive
  logged with structured scopes; STT-PCM and TTS-PCM logged as length
  only; transcripts redactable via the same PII hook used by
  `LoggingRealtimeAgentTests`.

### 1.11 Bridge interplay

Files:

- `Cascade/CascadingAgent_AsAIAgentBridgeTests.cs` — wrapping a
  `CascadingRealtimeAgent` in `RealtimeAgentAsAIAgent` works
  unchanged: a text input drives the cascade end-to-end and
  collects the inner-agent text response into an `AgentResponse`;
  audio bytes surfaced in `AgentResponse.AdditionalProperties` per
  the existing bridge contract (design §2.5 promises capability
  parity with native realtime agents).

---

## 2. `CascadingRealtimeAgent.IntegrationTests` *(new project)*

Sibling of the per-provider integration projects in
[`realtime-agent-test.md`](./realtime-agent-test.md) §4. Hits real
endpoints and verifies the cascade composes correctly against
production STT, chat, and TTS implementations.

The fixture is parameterized so the same test suite can run against
different STT/TTS pairings: Azure Speech (transcription) + Azure
Speech (synthesis), OpenAI Realtime (transcription session) + Azure
Speech TTS, Foundry Voice Live transcription mode + Foundry Voice Live
synthesis mode, etc.

Files:

- `Fixtures/CascadingRealtimeFixture.cs` — secrets, sub-client
  construction, the configured STT/TTS pairing, audio fixture loader
  (small PCM-16 clip in `Assets/`), inner-agent factory (default
  `ChatClientAgent` over Azure OpenAI).
- `Fixtures/ICascadeFixture.cs` — fixture contract enumerating the
  pairing's capabilities (server VAD, streamed text input,
  interruption support).
- `CascadeHappyPathTests.cs` — open, send a short PCM clip, await
  `OutputAudioDoneUpdate`, assert non-empty transcript both ways,
  audio out has expected sample rate/channels.
- `CascadeTextOnlyOverVoiceTests.cs` — text injection
  (`SendMessageAsync` and `AppendInputTextAsync`/`CommitInputTextAsync`)
  produces a coherent audio response without ever touching STT.
- `CascadeFunctionCallingTests.cs` — `MenuPlugin` (reused from
  `AgentConformance.IntegrationTests`) registered on the inner agent;
  spoken request triggers tool call → result → audio follow-up.
- `CascadeBargeInTests.cs` — start a response, send mid-flight
  speech, assert a single `ResponseCancelledUpdate` reaches the caller
  and the assistant `RealtimeItem` is truncated; `NotifyPlaybackPositionAsync`
  drives the truncation offset.
- `CascadeServerVadTests.cs` — applicable only when the STT pairing
  supports it.
- `CascadeClientVadTests.cs` — runs against a STT pairing that does
  *not* advertise VAD; pluggable `VoiceActivityDetector` (default
  WebRTC-VAD / Silero) drives the boundaries.
- `CascadePushToTalkTests.cs` — explicit `CommitInputAudioAsync` /
  `CancelResponseAsync` only.
- `CascadeReconnectTests.cs` — kill the STT socket mid-conversation;
  cascading session surfaces a recoverable error, reconnects, and
  the next user turn succeeds.
- `CascadeMetadataAdvertisementTests.cs` — `Metadata` flags match
  what the pairing actually supports at runtime.
- `Support/AgentCleanup.cs`, `Support/Constants.cs`,
  `Support/TestConfiguration.cs` — matches existing convention.
- `Assets/` — short PCM clip(s) + expected-transcript text shared
  with the per-provider realtime integration projects.

---

## 3. Conformance hookup

The cascading agent registers an `IRealtimeAgentFixture` implementation
into the existing
[`RealtimeAgentConformance.IntegrationTests`](./realtime-agent-test.md#5-realtimeagentconformanceintegrationtests)
project so every cross-provider conformance scenario
(connect/close, audio round trip, text round trip, function calling,
interruption, history reconstruction, session update mid-connection,
cancel response) also runs against the cascade.

No new conformance project — the cascade is just another `RealtimeAgent`
implementation and slots into the existing suite. The conformance
scenarios that depend on a capability the cascade can't satisfy with a
given STT/TTS pairing are skipped via `IRealtimeAgentFixture`'s
capability flags, matching the pattern already used for
native-provider fixtures.

---

## 4. Cross-cutting test concerns specific to the cascade

In addition to the cross-cutting concerns listed in
[`realtime-agent-test.md`](./realtime-agent-test.md) §6:

1. **Ordering determinism.** The merged
   `ReceiveUpdatesAsync` stream pulls from three independent
   async sources (STT, inner agent, TTS). Tests use a virtual clock on
   the fakes so the order is deterministic and golden assertions are
   stable across runs.
2. **Audio zero-copy.** Tests assert no defensive `.ToArray()` on the
   audio hot path. `FakeRealtimeClientSession` records the
   `ReadOnlyMemory<byte>` identity it was handed; the test asserts
   the same backing buffer reaches both STT and the optional client
   VAD.
3. **Capability negotiation.** When `TextChunking = Token` is requested
   but the TTS pairing doesn't advertise streamed-text support, the
   single-warning downgrade is logged exactly once per connect (not
   per delta).
4. **Inner-agent isolation.** Tests assert the cascade never calls
   anything on the inner agent that a non-streaming `AIAgent` can't
   handle — `RunStreamingAsync` is the only required method. Verified
   via a `[Mock(VerifyAll = true)]`-style assertion on
   `RecordingInnerAgent`.
5. **Bridge composability.** `CascadingRealtimeAgent` and
   `RealtimeAgentAsAIAgent` compose cleanly in either direction
   (cascade-of-bridge and bridge-of-cascade), with no cyclic
   dependencies or duplicate function-invocation decorator
   registrations.

---

## 5. Test-project dependency graph

```
Microsoft.Agents.AI.Realtime.UnitTests
        │
        └── Cascade/   (folder; no separate project)
              ▲
              │  shares fakes/utilities
              │
CascadingRealtimeAgent.IntegrationTests
              │
              └─▶ registers IRealtimeAgentFixture into
                  RealtimeAgentConformance.IntegrationTests
                  (see realtime-agent-test.md §5)
```

No new unit-test *project* is introduced — the cascade is a concrete
type inside `Microsoft.Agents.AI.Realtime`, so its unit tests live
inside that package's existing test project, exactly the way the
text-agent compaction tests live inside
`Microsoft.Agents.AI.UnitTests/Compaction/` rather than in a separate
`Microsoft.Agents.AI.Compaction.UnitTests` assembly.
