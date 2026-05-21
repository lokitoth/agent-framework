# RealtimeAgent — Client-side Layer Outline

This document outlines the **types and packages** required to add a `RealtimeAgent` story to Agent Framework, mirroring the existing `AIAgent` layering. The focus is on the **client-side abstractions** that sit in front of provider realtime APIs (OpenAI Realtime, Azure OpenAI Realtime, Google Gemini Live, AWS Nova Sonic, etc.).

The core difference vs `AIAgent`:

| `AIAgent` (request/response)                           | `RealtimeAgent` (bidirectional, duplex)                                  |
| ------------------------------------------------------ | ------------------------------------------------------------------------ |
| Caller sends a message, awaits a response (or stream). | Caller opens a long-lived session; both sides push events asynchronously. |
| Turns are explicit and serialized.                     | Turns are negotiated (server VAD / client commit), can be interrupted.   |
| Content is primarily text + structured data.           | Content includes streaming audio frames, partial transcripts, video.    |
| Tool calls block the turn.                             | Tool calls race with ongoing audio output and may be cancelled.          |

The package shape intentionally parallels `Microsoft.Agents.AI.*` so the same patterns (builder, delegating decorators, OTel/logging, function invocation, session state) carry over.

---

## 1. `Microsoft.Agents.AI.Realtime.Abstractions`

Equivalent of `Microsoft.Agents.AI.Abstractions`. Pure abstractions, no provider deps, no transport deps. Depends on `Microsoft.Extensions.AI.Abstractions` for shared content types (`AIContent`, `DataContent`, `FunctionCallContent`, `FunctionResultContent`, `ChatRole`).

### 1.1 Core agent type

- **`RealtimeAgent`** — abstract base, sibling of `AIAgent`.
  - `Id`, `Name`, `Description`, `IdCore` — same shape as `AIAgent`.
  - `GetService(Type, object?)` / `GetService<T>()` — service-locator pattern, identical to `AIAgent`.
  - `CurrentRunContext` (AsyncLocal) — same flow pattern.
  - **No** `RunAsync` / `RunStreamingAsync`. Instead:
    - `ValueTask<RealtimeSession> ConnectAsync(RealtimeConnectOptions?, CancellationToken)` — opens the duplex session.
    - `ValueTask<RealtimeSession> CreateSessionAsync(CancellationToken)` — creates a *disconnected* session object (for serialization scenarios / pre-warming).
    - `SerializeSessionAsync` / `DeserializeSessionAsync` — same shape as `AIAgent`, persists logical session state (history, tool registry, instructions) but not the live socket.
  - `Metadata` → `RealtimeAgentMetadata` (provider name, model id, supported modalities, supported audio formats).

- **`DelegatingRealtimeAgent`** — decorator base, mirrors `DelegatingAIAgent`. Enables OTel, logging, function invocation, policy decorators.

- **`RealtimeAgentMetadata`** — provider id, model id, supported `RealtimeModality` flags, supported audio codecs, supports server VAD?, supports interruption?, supports video?

### 1.2 Session — the duplex channel

- **`RealtimeSession`** — abstract, sibling of `AgentSession`. Represents one *live* conversation.
  - Inherits the `StateBag` pattern from `AgentSession` for serializable session state.
  - **Lifecycle**: `ConnectionState State { get; }` ∈ { `Connecting`, `Open`, `Closing`, `Closed`, `Faulted` }, `event EventHandler<RealtimeSessionStateChangedEventArgs>` (or expose via the event stream below).
  - **Inbound (server→client) stream**: `IAsyncEnumerable<RealtimeSessionUpdate> ReceiveUpdatesAsync(CancellationToken)`. Single consumer; the channel is the wire-level view of what the provider is emitting. This is the realtime analog of `AgentResponseUpdate`.
  - **Outbound (client→server) operations**:
    - `ValueTask SendAsync(RealtimeClientEvent, CancellationToken)` — low-level escape hatch.
    - Convenience helpers (non-virtual on the base, implemented in terms of `SendAsync`):
      - `AppendInputAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken)`
      - `CommitInputAudioAsync(CancellationToken)` — for client-side turn detection.
      - `SendMessageAsync(ChatMessage, CancellationToken)` — text injection.
      - `RequestResponseAsync(RealtimeResponseOptions?, CancellationToken)` — explicit response trigger.
      - `CancelResponseAsync(CancellationToken)` — interrupt in-flight audio.
      - `UpdateSessionAsync(RealtimeSessionUpdateOptions, CancellationToken)` — change instructions / tools / VAD / voice mid-session.
      - `AddItemAsync(RealtimeItem, CancellationToken)`, `DeleteItemAsync(string itemId, CancellationToken)` — conversation history mutation.
      - `SendToolResultAsync(string callId, AIContent result, CancellationToken)`.
  - **Conversation view**: `IReadOnlyList<RealtimeItem> History { get; }` — the locally-reconstructed conversation, kept in sync from inbound updates.
  - `DisposeAsync()` / `CloseAsync(CancellationToken)`.
  - `GetService` (same locator as `AgentSession`).

- **`RealtimeConnectOptions`** — initial instructions, modalities to enable, voice, audio formats, tools, server-VAD config, max output tokens, response format.

- **`RealtimeSessionUpdateOptions`** — same fields as connect options, all nullable, used to partially update an open session.

### 1.3 Event model

The wire model is a **discriminated union of updates**. The shape:

- **`RealtimeSessionUpdate`** — abstract base, with `Kind`, `EventId`, `Timestamp`, `RawProviderEvent` (string id + `JsonElement` for fidelity).
- Concrete update types (each carries strongly typed content):
  - `SessionCreatedUpdate`, `SessionUpdatedUpdate`
  - `ConnectionStateChangedUpdate`
  - `InputAudioBufferAppendedUpdate`, `InputAudioBufferCommittedUpdate`, `InputAudioBufferClearedUpdate`
  - `SpeechStartedUpdate`, `SpeechStoppedUpdate` (server VAD signals)
  - `InputTranscriptionDeltaUpdate`, `InputTranscriptionCompletedUpdate`, `InputTranscriptionFailedUpdate`
  - `ItemCreatedUpdate`, `ItemDeletedUpdate`, `ItemTruncatedUpdate`
  - `ResponseStartedUpdate`, `ResponseCompletedUpdate`, `ResponseCancelledUpdate`
  - `OutputAudioDeltaUpdate` (carries `ReadOnlyMemory<byte>` + format), `OutputAudioDoneUpdate`
  - `OutputTextDeltaUpdate`, `OutputTextDoneUpdate`
  - `OutputTranscriptDeltaUpdate`, `OutputTranscriptDoneUpdate`
  - `FunctionCallArgumentsDeltaUpdate`, `FunctionCallInvokedUpdate` (carries `FunctionCallContent`), `FunctionCallCancelledUpdate`
  - `RateLimitsUpdate`
  - `RealtimeErrorUpdate` (carries `RealtimeError` — non-fatal; transport faults manifest as `ConnectionStateChangedUpdate(Faulted)` + termination of the enumeration).

- **`RealtimeClientEvent`** — abstract base for outbound events; concrete subtypes mirror the inbound set (`InputAudioAppendEvent`, `ResponseCreateEvent`, `SessionUpdateEvent`, `ItemCreateEvent`, `ResponseCancelEvent`, …). Public so providers can be driven directly when needed.

### 1.4 Content model

Reuse `Microsoft.Extensions.AI` content where possible; add realtime-only shapes:

- **`RealtimeItem`** — conversation item (message, function_call, function_call_output). Has `Id`, `Role`, `IReadOnlyList<AIContent> Contents`, `Status` (in_progress / completed / incomplete).
- **`RealtimeAudioFormat`** — codec (`Pcm16`, `G711Ulaw`, `G711Alaw`, `Opus`), sample rate, channels.
- **`RealtimeAudioContent : DataContent`** — typed audio chunk with `Format`. Considered for promotion into M.E.AI later.
- **`RealtimeModality`** flags: `Text`, `Audio`, `Video` (`[Flags]`).
- **`RealtimeVoice`** — well-known voice id wrapper (string + provider-known set).

### 1.5 Turn detection

- **`TurnDetectionOptions`** — abstract.
  - `ServerVoiceActivityDetection` (threshold, prefix padding, silence duration, create_response, interrupt_response).
  - `SemanticVoiceActivityDetection` (eagerness).
  - `NoneTurnDetection` (client commits manually).
  - `ClientPushToTalk` (purely a flag for client UX; transport-level it's `None`).

### 1.6 Tools

- Realtime tools reuse `AITool` / `AIFunction` from `Microsoft.Extensions.AI` so the same `[Description]` / `KernelFunction`-style definitions used by `AIAgent` work unchanged.
- **`RealtimeFunctionInvocationContext`** — same role as `FunctionInvocationContext` in M.E.AI; adds `SessionCancellationToken` (cancelled when the response/turn is cancelled by the model or the user), and a handle back to the `RealtimeSession` so a tool can stream partial results or push intermediate audio (e.g., "let me check…").

### 1.7 Run context & telemetry primitives

- **`RealtimeAgentRunContext`** — AsyncLocal context (sibling of `AgentRunContext`) carrying the active `RealtimeSession`, a logical correlation id, and request-scoped `Activity`.
- **`RealtimeAgentLogMessages` / `RealtimeAgentTelemetryConsts`** — strings only, no implementation. The actual diagnostics live in `Microsoft.Agents.AI.Realtime`.

### 1.8 Serialization

- `RealtimeAgentJsonUtilities`, `[JsonSerializable]` types for `RealtimeSession.StateBag`, `RealtimeItem`, and update/event payloads to support source-gen-friendly serialization.

---

## 2. `Microsoft.Agents.AI.Realtime`

Equivalent of `Microsoft.Agents.AI`. Concrete, non-provider building blocks. Depends on `…Realtime.Abstractions` + `Microsoft.Extensions.AI` + `Microsoft.Extensions.Logging.Abstractions`.

### 2.1 Builder

- **`RealtimeAgentBuilder`** — mirrors `AIAgentBuilder`. Composes a pipeline of `DelegatingRealtimeAgent` decorators around an inner factory. Same `Use(...)` / `Build(IServiceProvider?)` ergonomics.
- Extension methods: `UseLogging`, `UseOpenTelemetry`, `UseFunctionInvocation`, `UseAudioRecording` (debug), `UseInterruptionPolicy`, `UseSessionPersistence`.

### 2.2 Built-in decorators

- **`LoggingRealtimeAgent`** — structured logs for connect/disconnect/send/receive (with PII redaction hooks; audio bytes are logged as length only).
- **`OpenTelemetryRealtimeAgent`** — spans for `realtime.connect`, `realtime.response`, `realtime.tool_call`; metrics for audio bytes in/out, latency to first audio token, interruption count. Follows the conventions in `OpenTelemetryAgent` / `OpenTelemetryConsts`.
- **`FunctionInvocationRealtimeAgent`** — listens for `FunctionCallInvokedUpdate`, dispatches to `AIFunction.InvokeAsync`, marshals the result back via `SendToolResultAsync`. Honors cancellation when the response is cancelled.
- **`AnonymousDelegatingRealtimeAgent`** — inline decorator factory (parallels `AnonymousDelegatingAIAgent`).

### 2.3 AIAgent bridge

- **`RealtimeAgentAsAIAgent`** — adapts a `RealtimeAgent` to the request/response `AIAgent` contract by opening a transient session, sending the input, collecting `Output*Update`s until `ResponseCompletedUpdate`, and returning an `AgentResponse`. Lets a realtime model be consumed anywhere an `AIAgent` is expected (workflows, orchestration, eval harness).
- **`AIAgentAsRealtimeAgent`** *(optional)* — adapts a text `AIAgent` to the realtime surface for testing/local-loop scenarios (no real audio; uses a TTS/STT pair injected via DI if present).

### 2.4 Session utilities

- **`InMemoryRealtimeHistoryProvider`** — stores `RealtimeItem`s for replay/handoff.
- **`SessionCompactionRealtimeAgent`** — periodically summarizes old items via an injected `IChatClient` and replaces them with a synthetic summary item (parallels the `Compaction` namespace under `Microsoft.Agents.AI`).
- **`PerSessionStateBag` helpers** matching the `AgentSession` patterns.

### 2.5 Audio helpers (transport-agnostic)

- `RealtimeAudioPipe` — bridges any `Stream`/`PipeReader` of PCM frames into `AppendInputAudioAsync` with framing, resampling hooks, and back-pressure.
- `RealtimeAudioWriter` — fan-out for `OutputAudioDeltaUpdate` into a `PipeWriter` / `Stream`.
- Format converters (PCM16 ↔ G.711) are **not** included by default — providers ship their own only if required.

### 2.6 Diagnostics

- `RealtimeDiagnosticIds` — `[Experimental]` ids for the new surface (`MEAI-REALTIME-001`, …).

---

## 3. Provider packages

Each provider package adds **one** concrete `RealtimeAgent` (or builder extension that returns one) plus provider-specific options. They live in separate NuGet packages so consumers pay only for what they use, matching the existing `Microsoft.Agents.AI.OpenAI` / `Microsoft.Agents.AI.Foundry` split.

The notes below capture **gotchas to keep in mind when implementing** — not the full type list.

### 3.1 `Microsoft.Agents.AI.Realtime.OpenAI`

Wraps the OpenAI Realtime API (`gpt-realtime`, `gpt-4o-realtime-preview`, …).

Type sketch: `OpenAIRealtimeAgent : RealtimeAgent`, `OpenAIRealtimeAgentOptions`, `OpenAIRealtimeSession : RealtimeSession`, builder extensions `AsRealtimeAgent(this RealtimeConversationClient client, …)`.

Implementation notes:

- **Two transports**: WebSocket (server-to-server) and WebRTC (browser/edge with ephemeral tokens). The abstraction must let the session pick; expose `OpenAIRealtimeTransport.WebSocket | WebRtc`.
- **Ephemeral token minting** is a *server-only* concern — surface an `IEphemeralTokenProvider` and a default REST impl, but do not bake API keys into anything that could ship in a browser.
- **Event taxonomy** is closed and versioned; map 1:1 to `RealtimeSessionUpdate` subtypes and keep the raw event in `RawProviderEvent` for forward-compat.
- **Audio formats**: `pcm16` (24 kHz, mono, little-endian) is the default; `g711_ulaw` / `g711_alaw` (8 kHz) are required for telephony (Twilio/ACS). Surface this on `RealtimeConnectOptions.InputAudioFormat` / `OutputAudioFormat`.
- **Server VAD** is the default; map `TurnDetectionOptions` directly to `session.turn_detection`. `semantic_vad` is supported on newer models — gate via `RealtimeAgentMetadata`.
- **Interruption semantics**: when the user starts speaking during model output, the server emits `input_audio_buffer.speech_started` and (if configured) cancels the in-flight response. The session must surface this as a single `ResponseCancelledUpdate` + truncate the assistant item locally so `History` stays consistent.
- **Function calling**: arguments stream as `response.function_call_arguments.delta`; only invoke after `…done`. Submit results via `conversation.item.create` (type `function_call_output`) followed by `response.create`.
- **Reconnect**: sessions are not resumable by id. On disconnect, the client must replay state (instructions, tools, history) via `session.update` + `conversation.item.create`. Build this into `OpenAIRealtimeSession` and expose `ReconnectAsync`.
- **Rate limits** are pushed as `rate_limits.updated`; expose via `RateLimitsUpdate`.
- **Image input** (when supported) goes via `input_image` content parts — keep the door open in `RealtimeItem`.

### 3.2 `Microsoft.Agents.AI.Realtime.AzureOpenAI`

Same wire protocol as OpenAI; reuse `OpenAIRealtimeSession` internals via a `internal` shared assembly (`Shared` folder pattern, like `Microsoft.Agents.AI.OpenAI` / `Microsoft.Agents.AI.Foundry` today).

Implementation notes:

- **Auth**: AAD (`TokenCredential`) is first-class and required for production; API-key auth is for dev only. Mirror `AzureOpenAIClient`'s credential model.
- **Endpoint shape**: deployment-based (`/openai/realtime?deployment=…&api-version=…`), not model-based. `OpenAIRealtimeAgentOptions.Model` becomes `AzureOpenAIRealtimeAgentOptions.DeploymentName`.
- **Region/availability**: not all regions/models support realtime — surface clear errors and let `RealtimeAgentMetadata` advertise capabilities discovered at connect.
- **Data residency / content filtering**: Azure may inject `content_filter_results` into events; expose via `RawProviderEvent` and a typed `AzureContentFilterUpdate`.
- **Private endpoints / custom DNS**: connection options must accept a custom `Uri` and `HttpClient`/`WebSocket` factory.

### 3.3 `Microsoft.Agents.AI.Realtime.Google.Gemini`

Wraps the Gemini **Live API** (`BidiGenerateContent`).

Implementation notes:

- **Different protocol**: WebSocket with a *handshake* (`setup` message must be first, replies with `setupComplete`) before any data. Encode in `ConnectAsync`.
- **Different content shape**: `BidiGenerateContentClientMessage` / `…ServerMessage`; map to/from `RealtimeSessionUpdate`. Audio is base64-encoded `Blob` parts with `audio/pcm;rate=16000` (input) and `audio/pcm;rate=24000` (output).
- **Tool calling**: Gemini supports both *server* tools (Google Search, code execution) and *client* tools (functions); model them as `RealtimeServerTool` (opaque, declared on connect) vs `AIFunction` (invoked locally).
- **Turn detection**: server VAD only (no "semantic VAD" equivalent); `none` not supported the same way — manual turn boundaries use `clientContent.turnComplete`.
- **Interruption**: server sends `serverContent.interrupted = true`; map to `ResponseCancelledUpdate`.
- **Sessions are short-lived** (≈15 min audio / 2 min video by default); implement an auto-reconnect-with-replay strategy like OpenAI and surface a `SessionWillExpireUpdate` warning.
- **Auth**: API key for AI Studio, OAuth/ADC for Vertex AI; expose both via `IGeminiCredentialProvider`.

### 3.4 `Microsoft.Agents.AI.Realtime.AwsNovaSonic` *(optional, lower priority)*

Wraps Amazon Nova Sonic bidirectional streaming via Bedrock.

Implementation notes:

- **Transport**: HTTP/2 event streams via the AWS SDK (`InvokeModelWithBidirectionalStream`), not WebSocket. The session implementation must hide this difference.
- **Event framing**: AWS uses its own event-stream framing (`:event-type` headers); a dedicated codec is needed.
- **Auth**: SigV4 via the AWS SDK credential chain; do not invent a parallel credential model — accept `AWSCredentials`.
- **Tooling model**: Nova Sonic uses a `toolUse` / `toolResult` event pair similar to OpenAI; reuse `AIFunction`-based invocation.

### 3.5 Other potential providers

Stubs to keep on the radar (not part of v1):

- `Microsoft.Agents.AI.Realtime.AzureCommunicationServices` — bridges ACS Call Automation audio streams into a `RealtimeSession` (telephony front-end).
- `Microsoft.Agents.AI.Realtime.Twilio` — same idea over Twilio Media Streams.
- `Microsoft.Agents.AI.Realtime.LiveKit` / `WebRtcBridge` — generic WebRTC transport providers.

---

## 4. Cross-cutting decisions to lock down before coding

These are open questions that should be settled in an ADR alongside the first PR:

1. **Single inbound channel vs events.** `IAsyncEnumerable<RealtimeSessionUpdate>` is proposed; alternative is C# events. The enumerable composes better with LINQ/`await foreach` and matches `AgentResponseUpdate`. Decision needed: how many concurrent enumerators are allowed? (Proposal: one; subsequent calls throw.)
2. **Where audio bytes live.** New `RealtimeAudioContent : DataContent` in this repo vs propose upstream to `Microsoft.Extensions.AI`. Proposal: define here as `[Experimental]`, plan upstream contribution.
3. **History reconstruction ownership.** Should `RealtimeSession.History` be authoritative (client tracks) or a thin view over server state? Proposal: client-tracked, kept in sync from updates, exposed read-only — needed for reconnect/replay.
4. **Tool invocation default.** Auto-invoke functions (like `FunctionInvocationDelegatingAgent` does for `AIAgent`) or require explicit opt-in? Proposal: opt-in via `UseFunctionInvocation()` to match the existing `AIAgent` story.
5. **`AIAgent` bridge fidelity.** What does `RealtimeAgent` → `AIAgent` do with audio output when consumed in a non-realtime context? Proposal: surface transcript only; raw audio is exposed via `AdditionalProperties` on the `AgentResponse`.
6. **Session serialization scope.** Confirm we serialize *logical* state (history, instructions, tool registry, state bag) but never live transport state (sockets, ephemeral tokens). Document explicitly.
7. **Python parity.** Mirror package names in `python/packages/` (`agent-framework-realtime`, `agent-framework-realtime-openai`, …) and align the event taxonomy 1:1 so docs/samples translate cleanly.

---

## 5. Suggested package dependency graph

```
Microsoft.Extensions.AI.Abstractions
        ▲
        │
Microsoft.Agents.AI.Realtime.Abstractions
        ▲
        │
Microsoft.Agents.AI.Realtime ─────────┐
        ▲                             │
        │                             │
        ├── Microsoft.Agents.AI.Realtime.OpenAI
        ├── Microsoft.Agents.AI.Realtime.AzureOpenAI   (shares internals with .OpenAI via Shared/)
        ├── Microsoft.Agents.AI.Realtime.Google.Gemini
        └── Microsoft.Agents.AI.Realtime.AwsNovaSonic
```

`Microsoft.Agents.AI` and `Microsoft.Agents.AI.Realtime` are **siblings** — neither references the other. Bridging happens through `RealtimeAgentAsAIAgent` in `Microsoft.Agents.AI.Realtime`, which has a one-way dependency on `Microsoft.Agents.AI.Abstractions` (for `AIAgent` / `AgentResponse`).
