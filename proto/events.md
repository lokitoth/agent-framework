# Realtime / Speech Agent — I/O Event Comparison

This document compares the wire-level event surfaces of the major realtime / speech agent
APIs as background for designing a `RealtimeAgent` abstraction in `Microsoft.Agents.AI`.

**Foundry is treated as the neutral baseline.** Other providers are described relative
to it. Differences are called out per-row and in the "Provider Notes" section.

Sources:

- **Foundry (Azure AI Voice Live)** —
  [voice-live-how-to](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-how-to)
  and
  [Azure OpenAI Realtime audio reference](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/realtime-audio-reference).
  Voice Live uses the same event shapes as the Azure OpenAI Realtime API, plus a small
  number of Azure-specific extensions for TTS, viseme/animation, audio timestamps, and
  the avatar pipeline.
- **OpenAI Realtime API** —
  [realtime client events](https://developers.openai.com/api/docs/api-reference/realtime_client_events)
  /
  [server events](https://developers.openai.com/api/docs/api-reference/realtime_server_events).
  GA event names (`response.output_audio.delta`, etc.) are noted where they have been
  renamed; legacy names (`response.audio.delta`) still appear in many SDKs and in
  Foundry's reference page.
- **Gemini Live API** —
  [BidiGenerateContent reference](https://ai.google.dev/api/live).
- **Amazon Nova Sonic** (speech-to-speech) —
  [input events](https://docs.aws.amazon.com/nova/latest/userguide/input-events.html)
  /
  [output events](https://docs.aws.amazon.com/nova/latest/userguide/output-events.html).
  Included for completeness; not in-box for the equivalent text agent and likely
  out of scope for the initial `RealtimeAgent`.

> Legend
>
> - `—` = no direct equivalent (functionality is either implicit, folded into another
>   event, or unsupported).
> - *italics* = behavior differs materially from the Foundry baseline; see notes.

---

## 1. Transport & session shape (high level)

| Aspect                   | Foundry (baseline)                                                                               | OpenAI                                                  | Gemini                                                            | Nova Sonic                                                       |
| ------------------------ | ------------------------------------------------------------------------------------------------ | ------------------------------------------------------- | ----------------------------------------------------------------- | ---------------------------------------------------------------- |
| Wire envelope            | JSON message with `type` discriminator (e.g. `"type": "session.update"`) plus payload fields     | Same — Foundry mirrors OpenAI                           | JSON message with a **single populated union field** (no `type`)  | JSON `{ "event": { "<eventName>": { ... } } }` envelope          |
| Discriminator            | `type` string                                                                                    | `type` string                                           | Field presence (`setup`, `clientContent`, `realtimeInput`, …)     | Inner key under `event` (`sessionStart`, `audioInput`, …)        |
| Audio payload encoding   | base64 string in event body                                                                      | base64 string in event body                             | base64 `Blob` (`data`, `mimeType`) in event body                  | base64 string in event body                                      |
| Transport                | WebSocket (also WebRTC offered via separate endpoint)                                            | WebSocket / WebRTC / SIP                                | WebSocket                                                         | Bedrock bidirectional stream (HTTP/2)                            |
| Multi-turn conversation  | Server-managed; items addressable by `item_id`                                                   | Same                                                    | Server-managed; *no explicit per-item IDs for audio/text deltas*  | Hierarchical: `promptName` ⊃ `contentName`/`contentId`           |
| Session resume           | Not in baseline                                                                                  | Not in baseline                                         | Yes — `sessionResumption` + `SessionResumptionUpdate` / `GoAway`  | No                                                               |

---

## 2. Client → Server events

| Event Description                          | Foundry (baseline)                                       | OpenAI                                                                 | Gemini                                              | Nova Sonic                                                  |
| ------------------------------------------ | -------------------------------------------------------- | ---------------------------------------------------------------------- | --------------------------------------------------- | ----------------------------------------------------------- |
| Configure session                          | `session.update`                                         | `session.update`                                                       | `setup` (`BidiGenerateContentSetup`) — *first only* | `sessionStart` + `promptStart`                              |
| Append streaming audio (user mic)          | `input_audio_buffer.append`                              | `input_audio_buffer.append`                                            | `realtimeInput.audio`                               | `audioInput` (inside an open `contentStart`/`contentEnd`)   |
| Commit audio buffer (manual VAD)           | `input_audio_buffer.commit`                              | `input_audio_buffer.commit`                                            | — *implicit via VAD / `activityEnd`*                | — *implicit; closed by `contentEnd`*                        |
| Clear audio buffer                         | `input_audio_buffer.clear`                               | `input_audio_buffer.clear`                                             | —                                                   | —                                                           |
| Signal end of audio stream                 | — *handled by VAD/commit*                                | —                                                                      | `realtimeInput.audioStreamEnd`                      | `contentEnd` for the audio content block                    |
| Manual start-of-user-activity (VAD off)    | — *(server-side VAD only)*                               | —                                                                      | `realtimeInput.activityStart`                       | `contentStart` with `role: USER`                            |
| Manual end-of-user-activity (VAD off)      | —                                                        | —                                                                      | `realtimeInput.activityEnd`                         | `contentEnd`                                                |
| Send text input                            | `conversation.item.create` (item w/ `input_text` part)   | `conversation.item.create`                                             | `clientContent` (`BidiGenerateContentClientContent`)| `contentStart`/`textInput`/`contentEnd`                     |
| Insert prior history / context             | `conversation.item.create` (any role)                    | `conversation.item.create`                                             | `clientContent` with `turns[]` (full history)       | History block sent between `promptStart` and audio          |
| Truncate / interrupt an in-flight item     | `conversation.item.truncate`                             | `conversation.item.truncate`                                           | — *barge-in is signaled by server-side `interrupted`* | — *barge-in is signaled implicitly*                         |
| Delete a prior conversation item           | `conversation.item.delete`                               | `conversation.item.delete`                                             | —                                                   | —                                                           |
| Request a response                         | `response.create`                                        | `response.create`                                                      | — *auto on `turnComplete=true`*                     | — *auto after user `contentEnd`*                            |
| Cancel an in-flight response               | `response.cancel`                                        | `response.cancel`                                                      | — *via `clientContent` interrupt*                   | — *via new user audio (barge-in)*                           |
| Return a tool/function-call result         | `conversation.item.create` (`function_call_output` item) | `conversation.item.create` (`function_call_output` item)               | `toolResponse` (`BidiGenerateContentToolResponse`)  | `contentStart` (TOOL) + `toolResult` + `contentEnd`         |
| End the session / disconnect               | Close socket                                             | Close socket                                                           | Close socket                                        | `contentEnd` → `promptEnd` → `sessionEnd`                   |
| **Foundry-only — Avatar SDP exchange** ¹   | `session.avatar.connect`                                 | n/a                                                                    | n/a                                                 | n/a                                                         |

¹ Avatar rows are documented for completeness — video I/O (including the Foundry
avatar pipeline) is **not** part of the initial `RealtimeAgent` implementation.
See `misc-notes.md`.

---

## 3. Server → Client events

### 3a. Session / connection lifecycle

| Event Description           | Foundry (baseline)        | OpenAI                | Gemini                       | Nova Sonic                                                   |
| --------------------------- | ------------------------- | --------------------- | ---------------------------- | ------------------------------------------------------------ |
| Session created             | `session.created`         | `session.created`     | — *(folded into setupComplete)* | — *(implicit on stream open)*                              |
| Session configured / ready  | `session.updated`         | `session.updated`     | `setupComplete`              | — *(implicit)*                                               |
| Conversation created        | `conversation.created`    | `conversation.created`| —                            | `completionStart` (per response, not per session)            |
| Server going to disconnect  | —                         | —                     | `goAway` (with `timeLeft`)   | —                                                            |
| Session resumption token    | —                         | —                     | `sessionResumptionUpdate`    | —                                                            |
| Error                       | `error`                   | `error`               | — *(transport-level)*        | dedicated error events (`speech-errors`)                     |
| Rate-limit info             | `rate_limits.updated`     | `rate_limits.updated` | (inside `usageMetadata`)     | `usageEvent` (per response)                                  |
| **Foundry-only — Avatar** ¹ | `session.avatar.connecting` | n/a                 | n/a                          | n/a                                                          |

¹ Avatar / video output is documented for completeness — **not** part of the
initial `RealtimeAgent` implementation. See `misc-notes.md`.

### 3b. Input-audio / VAD / transcription

| Event Description                          | Foundry (baseline)                                          | OpenAI                                                                         | Gemini                                       | Nova Sonic                                                                                  |
| ------------------------------------------ | ----------------------------------------------------------- | ------------------------------------------------------------------------------ | -------------------------------------------- | ------------------------------------------------------------------------------------------- |
| Input audio buffer committed               | `input_audio_buffer.committed`                              | `input_audio_buffer.committed`                                                 | —                                            | —                                                                                           |
| Input audio buffer cleared                 | `input_audio_buffer.cleared`                                | `input_audio_buffer.cleared`                                                   | —                                            | —                                                                                           |
| VAD: user started speaking                 | `input_audio_buffer.speech_started`                         | `input_audio_buffer.speech_started`                                            | — *(inferred from `interrupted`)*            | — *(inferred from new USER `contentStart`)*                                                 |
| VAD: user stopped speaking                 | `input_audio_buffer.speech_stopped`                         | `input_audio_buffer.speech_stopped`                                            | —                                            | —                                                                                           |
| Input audio transcription (streaming delta)| `conversation.item.input_audio_transcription.delta`         | `conversation.item.input_audio_transcription.delta`                            | `serverContent.inputTranscription` (delta)   | `textOutput` with `role: USER`, `generationStage=FINAL` (per chunk; not pre-streamed)       |
| Input audio transcription complete         | `conversation.item.input_audio_transcription.completed`     | `conversation.item.input_audio_transcription.completed`                        | — *(implied by `turnComplete`)*              | `contentEnd` (TEXT, USER)                                                                   |
| Input audio transcription failed           | `conversation.item.input_audio_transcription.failed`        | `conversation.item.input_audio_transcription.failed`                           | —                                            | error event                                                                                 |

### 3c. Response structure / items

| Event Description                       | Foundry (baseline)                | OpenAI                         | Gemini                                                | Nova Sonic                                                |
| --------------------------------------- | --------------------------------- | ------------------------------ | ----------------------------------------------------- | --------------------------------------------------------- |
| Conversation item created               | `conversation.item.created`       | `conversation.item.created`    | — *(items not surfaced)*                              | `contentStart` (per item)                                 |
| Response started                        | `response.created`                | `response.created`             | — *(implicit on first `serverContent`)*               | `completionStart`                                         |
| Response output item added              | `response.output_item.added`      | `response.output_item.added`   | —                                                     | `contentStart`                                            |
| Response output item done               | `response.output_item.done`       | `response.output_item.done`    | —                                                     | `contentEnd` (with `stopReason`)                          |
| Response content part added             | `response.content_part.added`     | `response.content_part.added`  | —                                                     | (same `contentStart`)                                     |
| Response content part done              | `response.content_part.done`      | `response.content_part.done`   | —                                                     | (same `contentEnd`)                                       |
| Model is done generating (pre-playback) | — *(implicit in `response.done`)* | —                              | `serverContent.generationComplete`                    | —                                                         |
| Model turn complete                     | `response.done`                   | `response.done`                | `serverContent.turnComplete`                          | `completionEnd` (with `stopReason`)                       |
| User barged-in / response interrupted   | *signaled via* `input_audio_buffer.speech_started` + truncation | same                  | `serverContent.interrupted`                           | `contentEnd` with `stopReason: INTERRUPTED`               |
| Conversation item truncated             | `conversation.item.truncated`     | `conversation.item.truncated`  | —                                                     | —                                                         |
| Conversation item deleted               | `conversation.item.deleted`       | `conversation.item.deleted`    | —                                                     | —                                                         |

### 3d. Streaming response content (text, audio, transcript)

OpenAI/Foundry split each response into separate `*.delta` / `*.done` streams per
modality. Gemini multiplexes all of them through `serverContent.modelTurn` plus the
sibling `outputTranscription`. Nova Sonic separates them into successive content
blocks (SPECULATIVE text → audio → FINAL text).

| Event Description                | Foundry (baseline)                       | OpenAI                                                              | Gemini                                            | Nova Sonic                                                                  |
| -------------------------------- | ---------------------------------------- | ------------------------------------------------------------------- | ------------------------------------------------- | --------------------------------------------------------------------------- |
| Streaming text delta             | `response.text.delta`                    | `response.text.delta` (legacy) / `response.output_text.delta` (GA)  | `serverContent.modelTurn` (Content with text)     | `textOutput` (`role: ASSISTANT`, `generationStage=SPECULATIVE`)             |
| Streaming text done              | `response.text.done`                     | `response.text.done` / `response.output_text.done`                  | — *(implied by `turnComplete`)*                   | `contentEnd` (TEXT)                                                         |
| Streaming audio delta            | `response.audio.delta`                   | `response.audio.delta` / `response.output_audio.delta`              | `serverContent.modelTurn` (Content with inlineData audio) | `audioOutput`                                                       |
| Streaming audio done             | `response.audio.done`                    | `response.audio.done` / `response.output_audio.done`                | — *(implied by `turnComplete`)*                   | `contentEnd` (AUDIO)                                                        |
| Audio transcript delta           | `response.audio_transcript.delta`        | `response.audio_transcript.delta` / `response.output_audio_transcript.delta` | `serverContent.outputTranscription` (delta) | `textOutput` (`role: ASSISTANT`, `generationStage=FINAL`, sent post-audio)  |
| Audio transcript done            | `response.audio_transcript.done`         | `response.audio_transcript.done`                                    | —                                                 | `contentEnd` (TEXT, FINAL)                                                  |
| **Foundry-only — TTS word timestamp delta** | `response.audio_timestamp.delta`        | n/a                                                                 | n/a                                               | n/a                                                                         |
| **Foundry-only — TTS word timestamp done**  | `response.audio_timestamp.done`         | n/a                                                                 | n/a                                               | n/a                                                                         |
| **Foundry-only — Viseme delta**             | `response.animation_viseme.delta`       | n/a                                                                 | n/a                                               | n/a                                                                         |
| **Foundry-only — Viseme done**              | `response.animation_viseme.done`        | n/a                                                                 | n/a                                               | n/a                                                                         |

### 3e. Tool / function calling

| Event Description                  | Foundry (baseline)                        | OpenAI                                    | Gemini                                                | Nova Sonic                                  |
| ---------------------------------- | ----------------------------------------- | ----------------------------------------- | ----------------------------------------------------- | ------------------------------------------- |
| Tool call arguments delta          | `response.function_call_arguments.delta`  | `response.function_call_arguments.delta`  | — *(arguments delivered whole)*                       | — *(delivered whole in `toolUse`)*          |
| Tool call arguments done           | `response.function_call_arguments.done`   | `response.function_call_arguments.done`   | `toolCall` (`BidiGenerateContentToolCall`)            | `toolUse` (inside TOOL `contentStart`/End)  |
| Tool call cancellation             | — *(no equivalent)*                       | —                                         | `toolCallCancellation`                                | — *(implied by INTERRUPTED `stopReason`)*   |

### 3f. Usage / metering

| Event Description                  | Foundry (baseline)                        | OpenAI                  | Gemini                              | Nova Sonic   |
| ---------------------------------- | ----------------------------------------- | ----------------------- | ----------------------------------- | ------------ |
| Per-response token usage           | inside `response.done.response.usage`     | inside `response.done`  | `usageMetadata` (on most server msgs) | `usageEvent` |

---

## 4. Provider notes (deltas vs. Foundry baseline)

### OpenAI Realtime

- Effectively identical event surface to Foundry. The GA naming refactor renamed
  several events under the `response.output_*` prefix
  (`response.output_audio.delta`, `response.output_text.delta`,
  `response.output_audio_transcript.delta`); legacy names still appear in older SDKs
  and remain accepted. Foundry's published reference still uses the legacy names.
- Foundry's `input_audio_transcription.model` field must be the **deployment name**,
  not the model name — this is the only documented Azure deviation in the shared event
  schema.
- Foundry adds Azure-only features that have no OpenAI counterpart:
  - Azure VAD variants (`azure_semantic_vad`, `azure_semantic_vad_multilingual`) with
    `remove_filler_words`, `languages`, and `interrupt_response`.
  - Azure-managed STT/TTS (`model: "azure-speech"`, named neural voices, HD voices,
    `rate`, `temperature`).
  - **Audio word-level timestamps** (`response.audio_timestamp.delta` / `.done`),
    opted into via `output_audio_timestamp_types: ["word"]`.
  - **Viseme stream** (`response.animation_viseme.delta` / `.done`), opted into via
    `animation.outputs: ["viseme_id"]`.
  - **Avatar pipeline** (`session.avatar.connect` ↔ `session.avatar.connecting`) for
    text-to-speech avatar video over WebRTC. *Documented for completeness — video
    I/O is **not** part of the initial `RealtimeAgent` implementation (see
    `misc-notes.md`).*

### Gemini Live (BidiGenerateContent)

- **No `type` discriminator.** Each message is a union; clients dispatch on which
  field is populated (`setup`, `clientContent`, `realtimeInput`, `toolResponse` from
  the client; `setupComplete`, `serverContent`, `toolCall`, `toolCallCancellation`,
  `goAway`, `sessionResumptionUpdate` from the server). `usageMetadata` is a sibling
  field that can ride along with most server messages.
- **`clientContent` vs `realtimeInput` distinction.** `clientContent` appends to
  conversation history (and may interrupt generation); `realtimeInput` is a
  best-effort live stream (audio/video/text) whose turn boundaries are derived from
  VAD or explicit `activityStart` / `activityEnd` / `audioStreamEnd` signals. This is
  a meaningful split that Foundry/OpenAI do not have — both of those treat all input
  as appended buffer content with VAD-driven turn boundaries.
- **No per-modality response streams.** Audio, text, and tool calls all arrive as
  `serverContent.modelTurn` (Content with parts) — the client demultiplexes by part
  type. Transcripts are a separate, unordered sibling
  (`serverContent.inputTranscription` / `outputTranscription`).
- **Distinct `generationComplete` vs `turnComplete`.** The model finishes generating
  (`generationComplete`) before playback finishes (`turnComplete`). Foundry/OpenAI
  emit only the "done" signal.
- **Native barge-in event** (`serverContent.interrupted`) where Foundry/OpenAI rely
  on `input_audio_buffer.speech_started` + a manual `conversation.item.truncate`.
- **First-class video input** (`realtimeInput.video`). Foundry has no video input.
  *Documented for completeness — video I/O is **not** part of the initial
  `RealtimeAgent` implementation (see `misc-notes.md`).*
- **Session resumption** (`sessionResumption` config + `sessionResumptionUpdate` /
  `goAway`) — Foundry/OpenAI have no equivalent; sessions are tied to the socket.
- **No granular item/part events** (`response.output_item.*`,
  `response.content_part.*`, `conversation.item.created`, `*.truncated`, `*.deleted`).
  History is opaque on the server.

### Amazon Nova Sonic

- **Hierarchical event model** rather than flat events: `sessionStart` → `promptStart`
  → repeated `contentStart` / `<content>` / `contentEnd` blocks → `promptEnd` →
  `sessionEnd`. A `promptName` plus per-block `contentName` / `contentId` are required
  on every event and tie things together.
- **Explicit lifecycle events on the client side** (`promptStart`, `promptEnd`,
  `sessionEnd`) where Foundry only has `session.update` and connection close.
- **No `response.create` analog** — once the user content block is closed, the model
  responds. Likewise no client-side `response.cancel`; cancellation is implicit by
  starting a new user audio block (barge-in).
- **Three-phase response content**: SPECULATIVE assistant text (model's plan),
  AUDIO output, then FINAL assistant text (sentence-level transcript of what was
  actually spoken — useful when audio was interrupted). Foundry instead emits
  `response.audio_transcript.delta/done` alongside the audio stream.
- **Tool calls delivered whole** in a `toolUse` event (not streamed token-by-token).
  Foundry streams them via `response.function_call_arguments.delta`.
- **No streaming transcript of user input** during recognition — `textOutput` for
  USER appears after the user content block closes.
- **Stop reasons surfaced explicitly** on every `contentEnd` (`PARTIAL_TURN`,
  `END_TURN`, `INTERRUPTED`, `TOOL_USE`).

---

## 5. Implications for `RealtimeAgent`

Translating the differences above into design pressure on the abstraction:

1. **Discriminator normalization.** Foundry/OpenAI use a `type` string; Gemini uses
   field presence; Nova uses a nested key. The transport layer must surface a normalized
   event-kind enum upward so the agent core never sees raw wire shapes.

2. **Modality multiplexing.** Output audio + output text + output transcript come as
   three parallel streams in Foundry/OpenAI, one mixed stream in Gemini, and three
   sequential blocks in Nova. The abstraction's `Receive()` channel should emit a
   tagged union (`AudioContent`, `TextContent`, transcript text, tool calls) without
   committing to interleave order.

3. **Turn control.** Three different turn-completion stories:
   - Foundry/OpenAI: explicit `response.create` + `response.done`.
   - Gemini: implicit on `turnComplete`, with a separate `generationComplete` for
     playback-aware UX.
   - Nova: implicit on `contentEnd` of the user block, with explicit `stopReason` on
     every block end.

   A neutral surface likely needs (a) a "turn complete" event the consumer can await,
   (b) an optional "model done generating" hint (Gemini-only today), and (c) a stop
   reason.

4. **Interruption / barge-in.** Foundry/OpenAI require the client to detect
   `speech_started` and call `conversation.item.truncate`. Gemini and Nova fire a
   dedicated interruption signal. The abstraction should expose a single
   `OnInterrupted` event and handle the Foundry-side truncation under the hood.

5. **Tool calls.** Streamed-argument deltas (Foundry/OpenAI) vs whole-call delivery
   (Gemini/Nova) means the abstraction should surface both `ToolCallDelta` and
   `ToolCallComplete` events, with Gemini/Nova firing only the latter.

6. **Foundry-only features (timestamps, visemes, avatar) are additive.** They can be
   surfaced as provider-specific extensions without polluting the cross-provider
   shape, and the existing `Microsoft.Extensions.AI` raw-passthrough conventions
   already cover both directions:

   - **Inbound** (provider event → consumer): set the underlying provider event
     object on a `RawRepresentation` property on whatever realtime event/content
     type the abstraction emits, mirroring `ChatResponse.RawRepresentation`,
     `ChatResponseUpdate.RawRepresentation`, `ChatMessage.RawRepresentation`, and
     the existing `AgentResponse.RawRepresentation` /
     `AgentResponseUpdate.RawRepresentation` slots in
     `Microsoft.Agents.AI.Abstractions`. Consumers who want Foundry-only events
     (`response.audio_timestamp.delta`, `response.animation_viseme.delta`,
     `session.avatar.connecting`, …) downcast the `RawRepresentation` to the
     provider's native event type.

   - **Outbound** (consumer → provider request configuration): use the
     `RawRepresentationFactory` pattern from `ChatOptions.RawRepresentationFactory`
     in `Microsoft.Extensions.AI`. A realtime "options" type (e.g.
     `RealtimeSessionOptions`) should expose a
     `Func<IRealtimeClient, object?> RawRepresentationFactory` that the
     provider-specific client invokes to materialize the native session-config
     payload (Voice Live `session.update` body with Azure VAD / voice / avatar /
     `animation.outputs`, OpenAI `session.update` body, Gemini
     `BidiGenerateContentSetup`, Nova `promptStart`). This is already the
     documented path for opting into provider-specific tools and request fields
     in `docs/decisions/0002-agent-tools.md`, so applying it here keeps the
     realtime story consistent with `ChatClientAgent`.

   No new escape-hatch concept is needed: the SK ADR-0065
   `service_event` / `service_event_type` idea collapses into "set
   `RawRepresentation` on the emitted event"; the per-provider request knobs
   collapse into `RawRepresentationFactory`.

7. **History / item model is not portable.** Foundry/OpenAI expose addressable items
   (`conversation.item.*`) and let the client delete / truncate them. Gemini and Nova
   do not. A portable in-memory `RealtimeAgentSession` should treat the server side as
   authoritative for history and only cache locally what the provider does not retain
   (consistent with the existing `InMemorySession` direction in `notes.md`).

8. **Session resumption.** Only Gemini supports it. Treat as an optional capability
   flag; do not bake it into the base interface.
