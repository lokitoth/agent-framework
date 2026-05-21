# Proposed Normalized Event & Content Model for `RealtimeAgent`

This proposal layers the cross-provider event surface from
[`events.md`](./events.md) onto the realtime primitives that
**already ship in `Microsoft.Extensions.AI`** (assembly
`Microsoft.Extensions.AI.Abstractions`, namespace `Microsoft.Extensions.AI`,
v10.x). The intent is **not** to introduce a parallel taxonomy, but to call
out which M.E.AI types serve as the normalized surface and where small,
purposeful additions are needed.

Foundry is treated as the neutral baseline (consistent with `events.md`).

---

## 1. Existing M.E.AI primitives we adopt as-is

### Transport / session

| Concern                  | M.E.AI type                                            |
| ------------------------ | ------------------------------------------------------ |
| Client                   | `IRealtimeClient`, `DelegatingRealtimeClient`          |
| Session                  | `IRealtimeClientSession` (`SendAsync` + `GetStreamingResponseAsync`) |
| Session options          | `RealtimeSessionOptions`                               |
| Session kind             | `RealtimeSessionKind` (`Conversation`, `Transcription`)|
| Audio format             | `RealtimeAudioFormat(MediaType, SampleRate)`           |
| VAD config               | `VoiceActivityDetectionOptions`                        |
| Transcription config     | `TranscriptionOptions`                                 |
| Provider escape hatch    | `RealtimeSessionOptions.RawRepresentationFactory`      |

### Client → server messages (extend `RealtimeClientMessage`)

| Wire intent                                  | M.E.AI message                                  |
| -------------------------------------------- | ----------------------------------------------- |
| Configure / update session                   | `SessionUpdateRealtimeClientMessage`            |
| Append audio chunk                           | `InputAudioBufferAppendRealtimeClientMessage` (carries `DataContent`) |
| Commit audio buffer                          | `InputAudioBufferCommitRealtimeClientMessage`   |
| Add a conversation item (text/history/tool result) | `CreateConversationItemRealtimeClientMessage` (carries `RealtimeConversationItem`) |
| Request a response                           | `CreateResponseRealtimeClientMessage`           |

Every message carries `MessageId` and `RawRepresentation`. Provider-specific
client events that don't fit the normalized set (e.g.
`input_audio_buffer.clear`, `conversation.item.truncate`,
`conversation.item.delete`, `response.cancel`, Foundry
`session.avatar.connect`) are sent by setting `RawRepresentation` on a
"pass-through" `RealtimeClientMessage` and routed to the provider transport.

### Server → client messages (extend `RealtimeServerMessage`)

| Wire intent                                | M.E.AI message                                   | `RealtimeServerMessageType`                              |
| ------------------------------------------ | ------------------------------------------------ | -------------------------------------------------------- |
| Response started                           | `ResponseCreatedRealtimeServerMessage`           | `ResponseCreated`                                        |
| Response done / cancelled / failed         | `ResponseCreatedRealtimeServerMessage`           | `ResponseDone` (with `Status` ∈ `RealtimeResponseStatus`)|
| Output item added                          | `ResponseOutputItemRealtimeServerMessage`        | `ResponseOutputItemAdded`                                |
| Output item done                           | `ResponseOutputItemRealtimeServerMessage`        | `ResponseOutputItemDone`                                 |
| Streaming text delta                       | `OutputTextAudioRealtimeServerMessage` (`Text`)  | `OutputTextDelta`                                        |
| Streaming text done                        | `OutputTextAudioRealtimeServerMessage` (`Text`)  | `OutputTextDone`                                         |
| Streaming output audio delta               | `OutputTextAudioRealtimeServerMessage` (`Audio`) | `OutputAudioDelta`                                       |
| Streaming output audio done                | `OutputTextAudioRealtimeServerMessage`           | `OutputAudioDone`                                        |
| Output audio transcript delta              | `OutputTextAudioRealtimeServerMessage` (`Text`)  | `OutputAudioTranscriptionDelta`                          |
| Output audio transcript done               | `OutputTextAudioRealtimeServerMessage` (`Text`)  | `OutputAudioTranscriptionDone`                           |
| User audio transcription delta             | `InputAudioTranscriptionRealtimeServerMessage`   | `InputAudioTranscriptionDelta`                           |
| User audio transcription completed         | `InputAudioTranscriptionRealtimeServerMessage`   | `InputAudioTranscriptionCompleted`                       |
| User audio transcription failed            | `InputAudioTranscriptionRealtimeServerMessage`   | `InputAudioTranscriptionFailed`                          |
| Conversation item added / done             | `ResponseOutputItemRealtimeServerMessage` (or `RawContentOnly`) | `ConversationItemAdded` / `ConversationItemDone` |
| Error                                      | `ErrorRealtimeServerMessage`                     | `Error`                                                  |
| Provider-specific / unmapped               | base `RealtimeServerMessage` with `RawRepresentation` only | `RawContentOnly`                              |

### Content (via `RealtimeConversationItem.Contents: IList<AIContent>`)

`RealtimeConversationItem` is the M.E.AI "item" carrier: an `Id`, an optional
`ChatRole`, and a list of `AIContent`. The full existing `AIContent` zoo
applies — we do not invent message-level content shapes:

| Realtime semantic                       | `AIContent` subtype                                              |
| --------------------------------------- | ---------------------------------------------------------------- |
| Text (user input, assistant message, transcript) | `TextContent`                                            |
| Audio chunk (input or output, in-memory)| `DataContent` (with `MediaType` from `RealtimeAudioFormat`)      |
| Audio reference (URI/CDN)               | `UriContent`                                                     |
| Function/tool call                      | `FunctionCallContent`                                            |
| Function/tool result                    | `FunctionResultContent`                                          |
| Tool call requiring approval            | `ToolApprovalRequestContent` / `ToolApprovalResponseContent`     |
| Reasoning (Gemini "thinking", o-series) | `TextReasoningContent`                                           |
| Token / billing metering                | `UsageContent` (+ `UsageDetails` on response-level messages)     |
| Non-fatal error attached to an item     | `ErrorContent`                                                   |

---

## 2. Provider → M.E.AI mapping (one-row summary)

A condensed mapping of the per-provider events in `events.md` onto the
normalized M.E.AI surface above. Rows omitted when they collapse trivially
into a row already listed.

| Foundry / OpenAI event                                  | Gemini field                            | Nova event                                            | Normalized M.E.AI                                           |
| ------------------------------------------------------- | --------------------------------------- | ----------------------------------------------------- | ----------------------------------------------------------- |
| `session.update` (client)                               | `setup`                                 | `sessionStart` + `promptStart`                        | `SessionUpdateRealtimeClientMessage`                        |
| `input_audio_buffer.append`                             | `realtimeInput.audio`                   | `audioInput` (inside open audio block)                | `InputAudioBufferAppendRealtimeClientMessage(DataContent)`  |
| `input_audio_buffer.commit`                             | `realtimeInput.audioStreamEnd` / `activityEnd` | `contentEnd` (audio block)                     | `InputAudioBufferCommitRealtimeClientMessage`               |
| `conversation.item.create` (text / tool result / history) | `clientContent` / `toolResponse`      | `contentStart`+`textInput`/`toolResult`+`contentEnd`  | `CreateConversationItemRealtimeClientMessage`               |
| `response.create`                                       | — *(implicit on `turnComplete`)*        | — *(implicit on user `contentEnd`)*                   | `CreateResponseRealtimeClientMessage`                       |
| `session.created` / `session.updated`                   | `setupComplete`                         | — *(implicit on stream open)*                         | `RealtimeServerMessage` (`RawContentOnly` for now)          |
| `response.created`                                      | — *(implicit)*                          | `completionStart`                                     | `ResponseCreatedRealtimeServerMessage` (`ResponseCreated`)  |
| `response.done`                                         | `serverContent.turnComplete`            | `completionEnd`                                       | `ResponseCreatedRealtimeServerMessage` (`ResponseDone`, `Status`) |
| `response.output_item.added`                            | (folded into `serverContent.modelTurn`) | `contentStart`                                        | `ResponseOutputItemRealtimeServerMessage` (`Added`)         |
| `response.output_item.done`                             | —                                       | `contentEnd` (with `stopReason`)                      | `ResponseOutputItemRealtimeServerMessage` (`Done`)          |
| `response.text.delta` / `.done`                         | `serverContent.modelTurn` text part     | `textOutput` SPECULATIVE                              | `OutputTextAudioRealtimeServerMessage` (`OutputText*`)      |
| `response.audio.delta` / `.done`                        | `serverContent.modelTurn` audio part    | `audioOutput`                                         | `OutputTextAudioRealtimeServerMessage` (`OutputAudio*`)     |
| `response.audio_transcript.delta` / `.done`             | `serverContent.outputTranscription`     | `textOutput` FINAL                                    | `OutputTextAudioRealtimeServerMessage` (`OutputAudioTranscription*`) |
| `conversation.item.input_audio_transcription.delta`     | `serverContent.inputTranscription`      | `textOutput` USER                                     | `InputAudioTranscriptionRealtimeServerMessage` (`Delta`)    |
| `conversation.item.input_audio_transcription.completed` | — *(implicit on `turnComplete`)*        | `contentEnd` (USER, TEXT)                             | `InputAudioTranscriptionRealtimeServerMessage` (`Completed`)|
| `response.function_call_arguments.delta`                | — *(delivered whole)*                   | — *(delivered whole)*                                 | `ResponseOutputItemRealtimeServerMessage` carrying `FunctionCallContent` (with streaming-arg accumulation in `ResponseOutputItemAdded` ⇒ `Done`) |
| `response.function_call_arguments.done`                 | `toolCall`                              | `toolUse`                                             | `ResponseOutputItemRealtimeServerMessage` (`Done`)          |
| `error`                                                 | — *(transport-level)*                   | error events                                          | `ErrorRealtimeServerMessage`                                |
| `rate_limits.updated`                                   | `usageMetadata`                         | `usageEvent`                                          | `UsageContent` (on response-level messages)                 |
| `input_audio_buffer.speech_started` / `_stopped`        | — *(inferred from `interrupted`)*       | — *(inferred from new USER block)*                    | `RealtimeServerMessage` (`RawContentOnly`) — *see Gap G1*   |
| `conversation.item.truncate` (client)                   | *(server)* `interrupted`                | *(server)* `INTERRUPTED` `stopReason`                 | — *see Gap G1*                                              |
| `response.cancel` (client)                              | — *(via interrupt)*                     | — *(via barge-in)*                                    | `RawRepresentation` passthrough on `RealtimeClientMessage`  |
| Gemini `toolCallCancellation` (server)                  | `toolCallCancellation`                  | (via `INTERRUPTED` stopReason)                        | Handled by `FunctionInvokingRealtimeClientSession` (auto-invoke); `RawContentOnly` for manual handling — *see Gap G2* |
| Foundry word timestamps, viseme, avatar                 | n/a                                     | n/a                                                   | `RawContentOnly` w/ `RawRepresentation` — *see §4 / §5*     |

---

## 3. Gaps in the existing M.E.AI surface

Three gaps are worth filling at the **`Microsoft.Agents.AI.Abstractions`**
layer (i.e. above M.E.AI), so providers that have richer signals can surface
them portably without modifying the M.E.AI primitives:

### Gap G1 — explicit barge-in / interruption signal

The current path is `RealtimeResponseStatus.Cancelled` on `ResponseDone`. That
is sufficient *post hoc*, but the consumer typically needs an early "stop
playback now" hint, which the providers expose as:

- Foundry / OpenAI: `input_audio_buffer.speech_started` (client must then
  call `conversation.item.truncate`).
- Gemini: `serverContent.interrupted`.
- Nova: `contentEnd` with `stopReason: INTERRUPTED`.

**Proposal:** introduce a single high-level event on the `RealtimeAgent`
surface (*not* in M.E.AI):

```text
RealtimeAgentInterruptedEvent {
    InterruptedResponseId : string?         // best-effort
    InterruptedItemId     : string?
    AudioSamplesPlayed    : long?           // for client-side truncation
    RawRepresentation     : object?         // provider-native trigger event
}
```

The Foundry/OpenAI adapter raises this on `speech_started` and is responsible
for sending the matching truncate message internally. Gemini and Nova
adapters raise it on their native signal.

### Gap G2 — tool-call cancellation

Gemini's `toolCallCancellation` and Nova's `INTERRUPTED` stop-reason on a
TOOL block have no representation in M.E.AI today. **However**, most of this
collapses into the existing middleware story rather than the public
abstraction:

- **Auto-invoked tools** (the common path) are handled by M.E.AI's
  `FunctionInvokingRealtimeClientSession`. That middleware already owns the
  in-flight tool task and the `CancellationToken` passed to the
  `AIFunction`. Cancellation is a middleware-internal concern: when an
  interruption is observed (Gemini `toolCallCancellation`, Nova `INTERRUPTED`
  stop-reason, Foundry/OpenAI barge-in propagated via G1), the middleware
  cancels the matching in-flight invocation's token and drops the (now-stale)
  result instead of sending it back to the model. **No new content type or
  message type is needed** — the existing `CancellationToken` plumbing plus
  the per-provider adapter's interpretation of native cancel signals is
  sufficient.

- **Manually handled tool calls** (consumer opts out of auto-invocation):
  the consumer needs a discrete signal so it can roll back side effects.
  Only Gemini emits a dedicated event for this. The pragmatic surface here
  is **provider-specific**: emit it as a `RealtimeServerMessage` with
  `Type = RawContentOnly` and the native `BidiGenerateContentToolCallCancellation`
  attached as `RawRepresentation`. Consumers that need it on Gemini downcast;
  consumers on other providers conflate it with the G1 interruption event
  (which is what the providers themselves do).

**Net:** drop the proposed `FunctionCallCancellationContent` from the
normalized layer. Cancellation lives on
`FunctionInvokingRealtimeClientSession` for the auto-invoke case, and on
`RawRepresentation` for the rare manual-handling case. We can revisit and
promote a portable content type only if a real cross-provider need emerges.

### Gap G3 — "model done generating" (pre-playback)

Only Gemini distinguishes `generationComplete` from `turnComplete`. This is
mostly useful as a playback-UX hint and is not present on Foundry/OpenAI/Nova
at all.

**Proposal:** **do not** model this at the normalized layer. Consumers that
care can pull it out of `RawRepresentation` on the Gemini-side
`RealtimeServerMessage`. Document this explicitly so it doesn't become a
recurring "where is generationComplete?" question.

---

## 4. Foundry-only additive features

Foundry's audio word-level timestamps (`response.audio_timestamp.delta`/`.done`)
and viseme stream (`response.animation_viseme.delta`/`.done`) are emitted as
`RealtimeServerMessage` with `Type = RawContentOnly` and the native event
attached as `RawRepresentation`. Consumers that opt into them (via the
session-options `RawRepresentationFactory` setting
`output_audio_timestamp_types` / `animation.outputs`) downcast
`RawRepresentation`.

We deliberately do **not** add `AudioTimestampContent` /
`VisemeContent` AIContent types in the initial implementation: they are
provider-specific, the cross-provider need is unproven, and the existing
`RawRepresentation` story covers them cleanly. If a portable need
materializes, they can be added later as additive `AIContent` subclasses
without breaking the normalized surface.

The Foundry avatar SDP handshake (`session.avatar.connect` ↔
`session.avatar.connecting`) is video — *documented for completeness but **not**
part of the initial `RealtimeAgent` implementation* (see `misc-notes.md`).

---

## 5. Video / Gemini `realtimeInput.video`

Out of scope for the initial implementation (see `misc-notes.md`). When
adopted later, the natural fit is:

- Inbound video frames from the user → a `DataContent` on
  `CreateConversationItemRealtimeClientMessage`, with a video media type.
- Outbound video (avatar / Gemini video) → a new `VideoContent : AIContent`
  in `Microsoft.Agents.AI.Abstractions` (or M.E.AI if proposed upstream),
  surfaced through `OutputTextAudioRealtimeServerMessage` extension or a new
  `OutputVideoRealtimeServerMessage`. Defer that design until we actually
  build it.

---

## 6. Summary

- **Reuse, don't replace.** The entire normalized surface for the initial
  `RealtimeAgent` is the existing M.E.AI realtime API
  (`IRealtimeClient`, `RealtimeClientMessage`/`RealtimeServerMessage`
  hierarchies, `RealtimeConversationItem`, `RealtimeSessionOptions`) plus
  the existing `AIContent` zoo.
- **Two targeted additions** in `Microsoft.Agents.AI.Abstractions`:
  `RealtimeAgentInterruptedEvent` (G1) — the explicit barge-in hint —
  and explicit non-modelling of Gemini `generationComplete` (G3).
  Tool-call cancellation (formerly G2) collapses into existing M.E.AI
  middleware (`FunctionInvokingRealtimeClientSession` cancels its own
  in-flight `CancellationToken`) plus `RawRepresentation` for the rare
  manual-handling case — no new content type.
- **Provider-specific events** (Foundry timestamps/visemes/avatar, Gemini
  resumption tokens / `goAway`, Nova `usageEvent`, etc.) ride
  `RawRepresentation` on the existing M.E.AI base types. No new escape hatch
  is introduced.
- **Video stays deferred**, with a sketched extension path.
