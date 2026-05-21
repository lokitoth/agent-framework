# Client implementation protocol gaps

Analysis date: 2026-05-21

## Scope and references checked

- Local implementations and unit tests:
  - `proto/impl/src/Microsoft.Agents.AI.Realtime.OpenAI`
  - `proto/impl/tests/Microsoft.Agents.AI.Realtime.OpenAI.UnitTests`
  - `proto/impl/src/Microsoft.Agents.AI.Realtime.Foundry`
  - `proto/impl/tests/Microsoft.Agents.AI.Realtime.Foundry.UnitTests`
- MEAI realtime types under `proto/external/MEAI`, especially:
  - `IRealtimeClient`, `IRealtimeClientSession`
  - `RealtimeSessionOptions`
  - `RealtimeClientMessage` / `RealtimeServerMessage`
  - `OpenAIRealtimeClient` / `OpenAIRealtimeClientSession`
- Reference path chased from `proto/notes.md`:
  - Foundry Voice Live how-to: `voice-live-how-to`
  - Foundry Voice Live API reference: `voice-live-api-reference-2025-10-01`
  - Azure OpenAI realtime-audio reference, which points through to OpenAI Realtime
  - OpenAI Realtime overview, WebSocket/conversation guides, sessions reference, and realtime resource reference
- Azure VoiceLive .NET SDK checked:
  - NuGet `Azure.AI.VoiceLive` `1.1.0-beta.4`
  - API docs: `https://learn.microsoft.com/en-us/dotnet/api/azure.ai.voicelive?view=azure-dotnet`
  - Package contents: `netstandard2.0`, `net8.0`, and `net10.0` assemblies; dependency on `Azure.Core` `1.55.0`
- Existing proto design notes used for intended normalized behavior:
  - `proto/events.md`
  - `proto/normalized-events.md`
  - `proto/session.md`

## High-level summary

OpenAI is mostly a wrapper around MEAI's `OpenAIRealtimeClient`, so the local AF implementation has little protocol code to validate. The gap is primarily test coverage: the OpenAI unit tests prove wrapper behavior with `FakeRealtimeClient`, not OpenAI Realtime wire compatibility.

Foundry is currently a direct protocol implementation. The current implementation is prototype-minimal and unit-test-shaped: it can send a small set of JSON events and project a few inbound events, but it does not yet encode/decode the full Voice Live/OpenAI realtime wire contract required by the reference docs.

`Azure.AI.VoiceLive` `1.1.0-beta.4` materially changes the Foundry recommendation: prefer an SDK-backed Foundry adapter over a custom WebSocket/JSON protocol implementation. The AF package would still need an adapter from MEAI `IRealtimeClient` / `IRealtimeClientSession` to the VoiceLive SDK, but the SDK should own VoiceLive transport, auth, wire serialization, and server-event deserialization.

## Azure.AI.VoiceLive SDK assessment

### Recommendation

Use `Azure.AI.VoiceLive` as the base for the Foundry provider instead of continuing the bespoke `IWebSocketTransport` + `FoundryClientMessageEncoder` + `FoundryEventProjector` stack.

The SDK does **not** implement MEAI's `IRealtimeClient`, so AF still needs a thin adapter:

- `FoundryRealtimeClient : IRealtimeClient` backed by `VoiceLiveClient`.
- `FoundryRealtimeClientSession : IRealtimeClientSession` backed by `VoiceLiveSession`.
- Mapping from MEAI `RealtimeSessionOptions` / `RealtimeClientMessage` into `VoiceLiveSessionOptions` and typed `VoiceLiveSession` methods.
- Mapping from `SessionUpdate` subclasses into MEAI `RealtimeServerMessage` subclasses.

That adapter is much smaller and safer than owning the service protocol directly.

### SDK coverage that addresses current Foundry gaps

`VoiceLiveClient` covers client construction and session creation:

- constructors for `Uri + TokenCredential` and `Uri + AzureKeyCredential`
- `VoiceLiveClientOptions.ServiceVersion.V2025_10_01`
- `StartSessionAsync(...)` and `CreateSession(...)`
- `SessionTarget.FromModel(...)` and `SessionTarget.FromAgent(AgentSessionConfig)`

`VoiceLiveSession` covers most client events listed in the Voice Live reference:

- audio: `SendInputAudioAsync`, `ClearInputAudioAsync`, `CommitInputAudioAsync`
- explicit streaming turns: `StartAudioTurnAsync`, `AppendAudioToTurnAsync`, `EndAudioTurnAsync`, `CancelAudioTurnAsync`
- session update: `ConfigureSessionAsync(VoiceLiveSessionOptions)`
- conversation items: `AddItemAsync`, `RequestItemRetrievalAsync`, `DeleteItemAsync`, `TruncateConversationAsync`
- response control: `StartResponseAsync`, `CancelResponseAsync`
- avatar: `ConnectAvatarAsync`
- raw escape hatch: `SendCommandAsync(BinaryData)` and `SendCommandAsync(RequestContent)`
- receive path: `GetUpdatesAsync`, `GetUpdatesAsync<T>`, `WaitForUpdateAsync<T>`, `ReceiveUpdatesAsync`

`VoiceLiveSessionOptions` covers the session shape the current encoder gets wrong:

- `Modalities`
- `Model`
- `Instructions`
- `Voice`
- `InputAudioFormat`
- `OutputAudioFormat`
- `InputAudioSamplingRate`
- `TurnDetection`
- `InputAudioNoiseReduction`
- `InputAudioEchoCancellation`
- `InputAudioTranscription`
- `OutputAudioTimestampTypes`
- `Animation`
- `Avatar`
- `Tools`
- `ToolChoice`
- `Temperature`
- `MaxResponseOutputTokens`
- `ReasoningEffort`
- `InterimResponse`
- `AdditionalProperties` for serialized request-session extensions

The SDK has typed server-event models for essentially the whole Voice Live event list that the current projector only partially handles, including:

- `SessionUpdateSessionCreated` / `SessionUpdateSessionUpdated`
- `SessionUpdateInputAudioBufferCommitted` / `Cleared` / `SpeechStarted` / `SpeechStopped`
- `SessionUpdateConversationItemCreated` / `Retrieved` / `Truncated` / `Deleted`
- `SessionUpdateConversationItemInputAudioTranscriptionDelta` / `Completed` / `Failed`
- `SessionUpdateResponseCreated` / `ResponseDone`
- `SessionUpdateResponseOutputItemAdded` / `Done`
- `SessionUpdateResponseContentPartAdded` / `Done`
- `SessionUpdateResponseTextDelta` / `Done`
- `SessionUpdateResponseAudioDelta` / `Done`
- `SessionUpdateResponseAudioTranscriptDelta` / `Done`
- `SessionUpdateResponseFunctionCallArgumentsDelta` / `Done`
- MCP list/call events
- audio timestamp, animation blendshape, animation viseme events
- `SessionUpdateError` and `ServerEventWarning`

The docs show these event models preserve important correlation fields. For example, `SessionUpdateResponseAudioDelta` exposes `EventId`, `ResponseId`, `ItemId`, `OutputIndex`, `ContentIndex`, and `Delta`.

### Remaining caveats if using the SDK

- The package is prerelease; docs explicitly warn the API may change before GA.
- It is an Azure SDK surface, not a MEAI surface. The mapping from MEAI abstractions to VoiceLive SDK types remains AF-owned and must be unit tested.
- The SDK's `CancelResponseAsync` has no documented `response_id` parameter. If AF wants to preserve `CancelResponseRealtimeClientMessage.ResponseId`, use `SendCommandAsync(BinaryData)` for targeted raw cancel or document that Voice Live only supports/uses "current response" cancellation through the typed method.
- The SDK has a raw outbound escape hatch (`SendCommandAsync`), and `SessionUpdate` is an `IPersistableModel`, but AF should explicitly verify how unknown inbound events are represented/preserved before relying on raw `RawRepresentation` for future server events.
- Session readiness semantics still need an adapter decision: `StartSessionAsync` connects, and `ConfigureSessionAsync` sends the update, but AF must decide whether `ConnectSessionAsync` waits for `SessionUpdateSessionUpdated` before returning to satisfy `proto/session.md`.
- Client thread safety is documented for Azure SDK client methods, but concurrent sends on a single `VoiceLiveSession` should still be validated under the adapter's intended usage.

### Impact on the Foundry gap list below

With an SDK-backed adapter, several existing Foundry gaps become "adapter mapping/test gaps" rather than "implement the protocol ourselves" gaps:

- FND-1 is largely addressed by `VoiceLiveClient` and `VoiceLiveSession`.
- FND-2 through FND-5 should be replaced by MEAI-to-`VoiceLiveSessionOptions` / item / response mapping tests.
- FND-7 is largely addressed by `VoiceLiveSession.SendCommandAsync(...)`.
- FND-8 is largely addressed by `SendInputAudioAsync` overloads, plus AF adapter handling for MEAI `DataContent` variants.
- FND-9 through FND-13 should be replaced by SDK `SessionUpdate`-to-MEAI projection tests.
- FND-14 remains a normalized interruption policy decision: map `SessionUpdateInputAudioBufferSpeechStarted` and/or `SessionUpdateConversationItemTruncated` to `InterruptedRealtimeServerMessage`.
- FND-15 remains a session lifecycle policy decision.
- FND-16 remains a concurrency validation item for the adapter.

## OpenAI implementation gaps

### OAI-1: Unit tests do not validate OpenAI Realtime protocol mapping

`OpenAIRealtimeAgent` composes any `IRealtimeClient` and returns `OpenAIRealtimeSession`. The tests use `FakeRealtimeClient`, so they verify wrapper behavior only:

- constructor guards
- `Name` / `Description`
- `SessionOptions` passed to `CreateSessionAsync`
- pass-through send/receive using fake MEAI messages
- `GetService`

They do not validate any of the OpenAI Realtime wire requirements documented in the reference path: `session.update`, `input_audio_buffer.append`, `conversation.item.create`, `response.create`, `response.cancel`, response lifecycle events, text/audio/audio-transcript deltas, error events, or GA event-name mapping.

This is not necessarily an implementation bug in AF because protocol work is delegated to MEAI's `OpenAIRealtimeClientSession`, which already maps many OpenAI SDK events to MEAI messages. It is a test-validation gap for this package.

### OAI-2: No local coverage of OpenAI GA event-name expectations

OpenAI current docs call out GA names such as:

- `response.output_text.delta`
- `response.output_audio.delta`
- `response.output_audio_transcript.delta`

MEAI's OpenAI session maps SDK types such as `RealtimeServerUpdateResponseOutputAudioDelta` into MEAI `OutputAudioDelta`, but the AF OpenAI tests never exercise that path. A regression in package wiring, MEAI dependency version, or event mapping would not be caught here.

### OAI-3: OpenAI wrapper does not validate session-kind behavior

OpenAI docs distinguish conversation, transcription, and translation flows. MEAI exposes `RealtimeSessionKind.Conversation` and `RealtimeSessionKind.Transcription`; translation is not represented. The local OpenAI tests do not cover:

- conversation vs transcription session creation
- transcription-only constraints
- unsupported translation behavior

Given the local wrapper delegates to MEAI, this should be validated by tests that use a controllable MEAI/OpenAI realtime client seam or by documenting that the package intentionally relies on MEAI's own tests for these protocol semantics.

## Foundry implementation gaps

### FND-1: Production transport and authentication are not implemented

`FoundryRealtimeAgent.ConnectSessionCoreAsync` calls an internal transport factory. The public constructor always installs a factory that throws `NotSupportedException`.

The Voice Live docs require a real WebSocket connection to:

- `wss://<resource>.services.ai.azure.com/voice-live/realtime?api-version=2025-10-01`
- plus either `model`, or agent-service parameters such as `agent_id` and `project_id`
- authentication via either Bearer token or API key header/query parameter

Current options include `Endpoint`, `ProjectName`, `AgentName`, `Credential`, and `ApiKey`, but production code does not use the credential/key, construct the required URL/query parameters, or perform a real WebSocket handshake.

### FND-2: `session.update` payload serializes MEAI options, not Voice Live wire shape

`FoundryClientMessageEncoder` encodes:

```json
{
  "type": "session.update",
  "session": { ... serialized RealtimeSessionOptions ... }
}
```

With `JsonSerializerDefaults.Web`, this produces MEAI property names such as `sessionKind`, `inputAudioFormat`, `outputAudioFormat`, `outputModalities`, `maxOutputTokens`, `voice`, and `voiceActivityDetection`.

The Voice Live reference expects fields such as:

- `modalities`
- `voice` as an object with `type`, `name`, and provider-specific fields
- `input_audio_format`
- `output_audio_format`
- `input_audio_sampling_rate`
- `turn_detection`
- `input_audio_noise_reduction`
- `input_audio_echo_cancellation`
- `temperature`
- `max_response_output_tokens`

The current unit test only asserts `"type": "session.update"`, so this mismatch is not detected.

### FND-3: `RawRepresentationFactory` is documented as the Azure extension path but ignored

`FoundryRealtimeAgentOptions` comments and `proto/session.md` say Azure-only knobs should ride through `RealtimeSessionOptions.RawRepresentationFactory`, including:

- `azure_semantic_vad`
- `azure_deep_noise_suppression`
- `server_echo_cancellation`
- HD/custom voices
- `rate`
- timestamps/animation/avatar options

`FoundryClientMessageEncoder` never invokes `RawRepresentationFactory`. It serializes `RealtimeSessionOptions` directly, so provider-specific Voice Live session fields cannot be emitted through the documented escape hatch.

### FND-4: Conversation item encoding is not Voice Live compatible

`CreateConversationItemRealtimeClientMessage` is serialized directly as MEAI `RealtimeConversationItem`, which has:

- `id`
- `role`
- `contents`
- `rawRepresentation`

Voice Live/OpenAI `conversation.item.create` expects:

```json
{
  "type": "conversation.item.create",
  "previous_item_id": "...",
  "item": {
    "type": "message",
    "role": "user",
    "content": [
      { "type": "input_text", "text": "..." }
    ]
  }
}
```

or provider wire shapes for `input_audio`, `function_call_output`, MCP approval responses, etc. The current tests do not send a text item, audio item, tool result, `previous_item_id`, or raw item representation, so they would not catch this.

### FND-5: `response.create` encoding is not Voice Live compatible

`CreateResponseRealtimeClientMessage` is serialized directly. That produces MEAI names such as:

- `items`
- `outputAudioOptions`
- `outputVoice`
- `excludeFromConversation`
- `maxOutputTokens`
- `outputModalities`
- `toolMode`

Voice Live/OpenAI `response.create` expects a `response` object using wire fields such as:

- `modalities`
- `instructions`
- `voice`
- `output_audio_format`
- `temperature`
- `max_response_output_tokens`
- `tools`
- `tool_choice`

The tests only send an `InputAudioBufferAppendRealtimeClientMessage`, so response creation with overrides is unvalidated.

### FND-6: `response.cancel` drops AF's `ResponseId`

AF adds `CancelResponseRealtimeClientMessage.ResponseId`. The OpenAI realtime reference supports an optional `response_id` on `response.cancel`; the Voice Live API reference currently shows a bare `response.cancel`. The implementation should either map `ResponseId` for OpenAI-compatible cancel semantics or explicitly document/test that Voice Live ignores it.

Foundry encodes `CancelResponseRealtimeClientMessage` as only:

```json
{ "type": "response.cancel" }
```

It never emits `response_id`, and no unit test covers cancellation or pins the provider-specific choice.

### FND-7: Raw client-event passthrough is missing

`normalized-events.md` explicitly says provider-specific client events should ride on `RealtimeClientMessage.RawRepresentation`, including:

- `input_audio_buffer.clear`
- `conversation.item.truncate`
- `conversation.item.delete`
- `response.cancel`
- Foundry `session.avatar.connect`

The Foundry encoder's fallback is:

```csharp
_ => message.GetType().Name
```

It ignores `RawRepresentation`, so a caller cannot send documented Voice Live events that are not represented by the small typed set. Tests do not cover raw passthrough.

### FND-8: Audio append handles only in-memory `DataContent.Data`

The realtime docs require `input_audio_buffer.append.audio` to be base64 audio bytes in the configured input format. MEAI `DataContent` can carry in-memory data or URI/data-URI content; MEAI's OpenAI implementation explicitly handles data URIs before falling back to bytes.

Foundry always uses:

```csharp
Convert.ToBase64String(append.Content.Data.Span)
```

This means URI/data-URI audio content is not supported or validated. The unit test uses only `new DataContent(new byte[] { 1, 2, 3 }, "audio/pcm")`.

### FND-9: Inbound projector covers only a small subset of server events

The Voice Live reference lists many server events required for normal realtime operation:

- `session.created`
- `session.updated`
- `conversation.item.created`
- `conversation.item.retrieved`
- `conversation.item.truncated`
- `conversation.item.deleted`
- `input_audio_buffer.committed`
- `input_audio_buffer.cleared`
- `input_audio_buffer.speech_started`
- `input_audio_buffer.speech_stopped`
- `conversation.item.input_audio_transcription.*`
- `response.created`
- `response.done`
- `response.output_item.added`
- `response.output_item.done`
- `response.content_part.added`
- `response.content_part.done`
- `response.text.delta` / `response.text.done`
- `response.audio.delta` / `response.audio.done`
- `response.audio_transcript.delta` / `response.audio_transcript.done`
- function-call argument events
- MCP events
- `rate_limits.updated`
- `error`
- `warning`
- Voice Live-specific timestamp, viseme, blendshape, and avatar events

`FoundryEventProjector` only special-cases:

- `response.text.delta`
- `response.audio_transcript.delta`
- `response.audio.delta`
- `response.done`
- `output_audio_buffer.cleared`

Everything else becomes a base `RealtimeServerMessage` with `Type = new RealtimeServerMessageType(typeName)`. That does not satisfy MEAI's normalized message requirements for middleware: `ResponseCreated`, `ResponseDone`, `ResponseOutputItemAdded`, and `ResponseOutputItemDone` are the event types downstream middleware needs for response/tool-call processing.

### FND-10: Inbound projections drop correlation metadata

For text/audio/audio-transcript deltas, the reference event shape includes:

- `response_id`
- `item_id`
- `output_index`
- `content_index`
- `delta`

OpenAI server events also carry `event_id`, and Voice Live examples include event ids on several server events. The Foundry projector maps only the delta text/audio. It does not populate:

- `MessageId`
- `ResponseId`
- `ItemId`
- `OutputIndex`
- `ContentIndex`
- `RawRepresentation`

This makes it impossible for consumers to correlate deltas with response/items/content parts. Tests currently assert only type plus delta content.

### FND-11: Done events are not projected for text/audio/transcripts

Voice Live has finalization events:

- `response.text.done`
- `response.audio.done`
- `response.audio_transcript.done`

MEAI has corresponding normalized types:

- `OutputTextDone`
- `OutputAudioDone`
- `OutputAudioTranscriptionDone`

Foundry does not project these events. Tests do not cover them.

### FND-12: `response.done` projection loses response status, items, usage, and errors

Voice Live `response.done` includes the final response object with status, output items, usage, and status details. MEAI has `ResponseCreatedRealtimeServerMessage` for `ResponseDone`.

Foundry currently returns only:

```csharp
new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone }
```

This loses response id, status, output items, usage, error information, and raw representation. Tool-call detection and telemetry cannot work from this projection.

### FND-13: Error and warning events are not normalized

The Voice Live reference defines `error` and `warning` server events. MEAI has `ErrorRealtimeServerMessage`.

Foundry does not map `error` to `ErrorRealtimeServerMessage`, does not preserve `event_id` correlation, and does not surface `warning` with raw details. Tests do not cover either.

### FND-14: Interruption handling does not match the primary documented WebSocket signal

Existing proto notes say Foundry can emit `output_audio_buffer.cleared`, and the implementation maps that to `InterruptedRealtimeServerMessage`.

The reference docs also describe `input_audio_buffer.speech_started` as the server-side VAD signal clients should use to interrupt playback and, on WebSocket clients, follow with `conversation.item.truncate` to synchronize server conversation state. Foundry does not map `input_audio_buffer.speech_started` to the AF interruption message, and it has no support for sending the matching `conversation.item.truncate` raw event.

The current unit test validates only `output_audio_buffer.cleared`, so it misses the main documented WebSocket interruption flow.

### FND-15: Session handshake readiness is not validated

`ConnectSessionAsync` returns immediately after connecting and optionally sending `session.update`. The docs describe server confirmation via `session.created` / `session.updated`, and `proto/session.md` says construction should imply the provider handshake has completed.

Current Foundry code does not wait for or validate a `session.updated` acknowledgement before returning the live `RealtimeSession`. Tests assert that a `session.update` was sent, not that the handshake completes.

### FND-16: Send concurrency requirement is not implemented or tested

MEAI's `IRealtimeClientSession.SendAsync` remarks state that provider implementations must serialize access if the underlying transport cannot handle concurrent sends. `FoundryRealtimeClientSession.SendAsync` writes directly to `_transport.SendTextAsync` with no send lock. The fake transport also does not expose concurrency problems.

This can lead to protocol violations with a real WebSocket transport when audio streaming, tool-result sends, cancellation, or session updates happen concurrently.

## Unit test gaps to add

### OpenAI tests

- Add a protocol-delegation test or documented test seam proving the AF OpenAI package composes MEAI's `OpenAIRealtimeClient` and relies on MEAI for OpenAI wire mapping.
- Add coverage for `RealtimeSessionKind.Conversation` vs `Transcription` behavior, or explicitly document that this is covered upstream in MEAI.
- Add dependency/version-sensitive tests for MEAI OpenAI event mapping if the AF package needs to guarantee GA OpenAI behavior independently.

### Foundry tests

If the current custom protocol implementation remains:

- Assert full `session.update` JSON, including `modalities`, audio format fields, turn detection, voice object, and Azure-only raw fields.
- Assert `RawRepresentationFactory` is invoked and merged/preserved for provider-specific session options.
- Assert `conversation.item.create` for text, audio, function-call output, and `previous_item_id`.
- Assert `response.create` with instructions, modalities, voice/audio overrides, tools/tool choice, max tokens, and out-of-band conversation behavior.
- Assert `response.cancel` includes optional `response_id`.
- Assert raw passthrough events, especially `input_audio_buffer.clear`, `conversation.item.truncate`, `conversation.item.delete`, and `session.avatar.connect`.
- Assert audio append handles both in-memory bytes and data URI content.
- Add inbound projection fixtures for the documented lifecycle: `session.*`, `input_audio_buffer.*`, `conversation.item.*`, `response.created`, `response.output_item.*`, `response.content_part.*`, text/audio/transcript delta and done, function-call argument events, errors, and rate limits.
- Assert projected messages include `MessageId`, `ResponseId`, `ItemId`, `OutputIndex`, `ContentIndex`, `RawRepresentation`, status, usage, and output items where applicable.
- Add a handshake test that `ConnectSessionAsync` does not return before the expected session-ready acknowledgement, if that remains the intended contract from `proto/session.md`.
- Add a send-concurrency test or transport contract test once production WebSocket transport exists.

If switching to an `Azure.AI.VoiceLive`-backed adapter:

- Replace wire-JSON encoder tests with MEAI-to-SDK mapping tests for `VoiceLiveSessionOptions`, conversation items, response options, tools, audio formats, VAD, transcription, voice, and Azure-specific options.
- Replace fake WebSocket tests with a fake/mock `VoiceLiveSession` seam or adapter-level abstraction that verifies the correct typed SDK methods are called.
- Add SDK `SessionUpdate`-to-MEAI projection tests for each normalized event type and selected provider-specific raw events.
- Add raw passthrough tests using `VoiceLiveSession.SendCommandAsync(BinaryData)` for events that remain outside the typed SDK surface.
- Add explicit tests for `CancelResponseRealtimeClientMessage.ResponseId`: either targeted raw cancel is sent or the value is rejected/documented as unsupported.
- Add a session lifecycle test that verifies whether `ConnectSessionAsync` waits for `SessionUpdateSessionUpdated`.
- Pin a prerelease-version compatibility test or documentation note because `Azure.AI.VoiceLive` is still beta.
