# Realtime Session Management — Comparison

This document compares the **session-management** surface area of the three realtime/speech provider APIs that are in scope for an `Microsoft.Agents.AI.RealtimeAgent` abstraction:

- **Azure AI Foundry — Voice Live API** *(treated as the neutral / default reference here)*
  - <https://learn.microsoft.com/azure/ai-services/speech-service/voice-live-how-to>
  - Event schema docs: <https://learn.microsoft.com/azure/ai-foundry/openai/realtime-audio-reference>
- **OpenAI — Realtime API**
  - <https://developers.openai.com/api/docs/guides/realtime>
  - <https://developers.openai.com/api/docs/guides/realtime-conversations>
  - <https://developers.openai.com/api/docs/guides/realtime-websocket>
- **Google Gemini — Live API**
  - <https://ai.google.dev/gemini-api/docs/live-api>
  - <https://ai.google.dev/gemini-api/docs/live-api/session-management>
  - <https://ai.google.dev/gemini-api/docs/live-api/capabilities>

> *Anthropic does not currently expose a realtime session API. Amazon Nova Sonic and xAI Grok are out of scope here (no first-class text-agent integration yet).*

---

## 1. Conceptual mapping

| Concept | Foundry (Voice Live) | OpenAI (Realtime) | Gemini (Live) |
|---|---|---|---|
| Transport | WebSocket (WSS); WebRTC variant available | WebSocket, WebRTC, or SIP | WebSocket (WSS); WebRTC via partner integrations |
| Endpoint | `wss://{resource}.services.ai.azure.com/voice-live/realtime?api-version=2025-10-01&model=…` (or `&agent_id=…&project_id=…`) | `wss://api.openai.com/v1/realtime?model=…` (also `/v1/realtime/translations` for translation sessions) | `wss://…/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent` via SDK `client.aio.live.connect(model=…)` |
| Auth | Microsoft Entra `Bearer` token (recommended) **or** `api-key` header / query param | `Authorization: Bearer <api_key>` header; ephemeral tokens for browsers | API key or OAuth; **ephemeral tokens** for client-to-server use |
| Session "type" selector | Single conversational session type; specialization via session config (e.g., transcription mode, BYOM agent) | Three distinct session kinds: **voice-agent** (`/v1/realtime`), **translation** (`/v1/realtime/translations`), **transcription** (no model responses) | Single session type; behavior selected via `responseModalities` and tool config |
| Initial config message | `session.update` (first client event) | `session.update` (first client event) | `BidiGenerateContentSetup` (first message after handshake) |
| Session created ack | `session.created` (server event) | `session.created` (server event) | `BidiGenerateContentSetupComplete` |
| Config update ack | `session.updated` | `session.updated` | (no equivalent — most fields are set-once at setup) |
| Conversation state object | `conversation` (server-managed) with `conversation.item.*` events | `conversation` with `conversation.item.*` events | Implicit; server maintains turn state, surfaced via `serverContent` messages |
| Turn / response object | `response` + `response.*` events | `response` + `response.*` events | `serverContent` stream + `turnComplete` / `generationComplete` flags |
| Voice activity detection | `turn_detection` (`server_vad`, `semantic_vad`, **`azure_semantic_vad`**, **`azure_semantic_vad_multilingual`**) | `turn_detection` (`server_vad`, `semantic_vad`) | Server-side VAD (configurable via `realtimeInputConfig`) |
| Server-side audio enhancements | `input_audio_noise_reduction` (incl. `azure_deep_noise_suppression`), `input_audio_echo_cancellation` (`server_echo_cancellation`) | `input_audio_noise_reduction` (`near_field` / `far_field`) — no server echo cancellation | None exposed |
| TTS voice selection | Rich `voice` object: `azure-standard`, `azure-custom`, HD voices, `temperature`, `rate` | `voice` string (e.g., `marin`, `alloy`) | `speechConfig.voiceConfig` (prebuilt voice names) |
| Pre-termination warning | None published | None published | **`GoAway`** server message with `timeLeft` |
| Session resumption across reconnects | Not supported (new connection = new session) | Not supported (new connection = new session) | **`sessionResumption`** config + server `SessionResumptionUpdate` with a `handle` (valid 2h) |
| Context-window compression | Not exposed | Not exposed | **`contextWindowCompression`** (sliding window + trigger token count) |
| Max session duration | Per-model; tied to underlying gpt-realtime limits | **60 minutes** | **15 min** audio-only / **2 min** audio+video uncompressed; **~10 min** per *connection*; unbounded with compression + resumption |
| Closing the session | Close WebSocket (or send no further input) | Close WebSocket | Close WebSocket; SDK `session.close()` |

---

## 2. Lifecycle, side-by-side

### Foundry (neutral baseline)

```
client ──▶ open WSS (with model or agent_id)
client ──▶ session.update         { instructions, turn_detection, voice, input_audio_*, … }
server ──▶ session.created
server ──▶ session.updated
        ◀──▶ input_audio_buffer.* / conversation.item.* / response.* events
client ──▶ (close socket)
```

### OpenAI

Effectively identical event names and shapes (Foundry's Voice Live "uses the same events as the Azure OpenAI Realtime API"). Differences:

- **Three session shapes** chosen by endpoint (`/v1/realtime` vs `/v1/realtime/translations` vs transcription). Translation sessions are *continuous* — no `response.create`, no per-turn commit.
- **Hard 60-minute session cap.**
- No Azure-specific VAD / noise / echo / voice extensions.
- Supports **SIP** transport in addition to WS/WebRTC.

### Gemini

Schema is structurally different — protobuf-style rather than typed event envelopes:

```
client ──▶ open WSS
client ──▶ BidiGenerateContentSetup { model, generationConfig, responseModalities,
                                      sessionResumption?, contextWindowCompression?, … }
server ──▶ BidiGenerateContentSetupComplete
        ◀──▶ BidiGenerateContentClientContent / RealtimeInput / ToolResponse
        ◀──▶ serverContent (audio / text / turnComplete / generationComplete)
        ◀──▶ SessionResumptionUpdate { newHandle, resumable }   [periodic]
server ──▶ GoAway { timeLeft }                                  [before disconnect]
client ──▶ session.close()
```

Notable: setup is essentially **set-once**; you don't get a free-form `session.update` you can resend mid-session the way you do on Foundry/OpenAI.

---

## 3. Notable per-provider deltas vs Foundry

### OpenAI vs Foundry
- **No Azure VAD types** (`azure_semantic_vad`, `azure_semantic_vad_multilingual`) and no multilingual filler-word removal.
- **No server-side echo cancellation** and no `azure_deep_noise_suppression` mode.
- **Simpler `voice` field** (string, not an object); no `rate`, no HD voice tiers, no custom voices.
- **Different `model` semantics** for `input_audio_transcription`: OpenAI uses a model name, Azure requires a *deployment* name.
- **Explicitly typed session "modes"** (voice-agent / translation / transcription) selected by endpoint, vs Foundry where transcription is just a session-config flavor.
- **SIP transport** option for telephony.
- **Hard 60-min cap** is documented; Foundry's effective ceiling is model-dependent and not surfaced as a single number.

### Gemini vs Foundry
- **Different event taxonomy.** No `session.update` / `session.created` / `response.*` family — instead `BidiGenerateContentSetup`, `serverContent`, `turnComplete`, `generationComplete`.
- **Setup is largely immutable.** Most knobs are fixed at setup time; Foundry allows mid-session `session.update`.
- **First-class session continuation primitives** that Foundry lacks:
  - `sessionResumption` + `SessionResumptionUpdate { handle }` → resume across new connections (handles valid 2 h).
  - `contextWindowCompression` (sliding window) → effectively unbounded sessions.
  - `GoAway { timeLeft }` → graceful handoff before the server cuts the connection.
- **Two-tier duration model:** connection lifetime (~10 min) is separate from logical session lifetime; Foundry/OpenAI conflate them.
- **No server-side audio enhancement knobs** (noise suppression, echo cancellation, filler-word removal) at the session level.
- **Stricter media constraints baked into setup:** input audio is fixed at 16 kHz PCM, output at 24 kHz PCM; Foundry exposes `input_audio_sampling_rate` (16k or 24k) and richer codec choices.
- **Ephemeral tokens** are a first-class concept for client-to-server use; Foundry uses Entra tokens and OpenAI uses an ephemeral-token endpoint for the WebRTC browser case.

---

## 4. Implications for a `RealtimeAgent` abstraction

### 4.1 `RealtimeSession` — one type, not two

`Microsoft.Extensions.AI` already ships a unified realtime primitive — `IRealtimeClientSession` — that combines the live transport and the logical session into a single type. `Microsoft.Agents.AI`'s `RealtimeAgent` builds directly on top of that:

- The MEAI underlayer exposes `IRealtimeClient` (provider client) and `IRealtimeClientSession` (live session: `SendAsync` + `GetStreamingResponseAsync`), configured with `RealtimeSessionOptions` and `RealtimeSessionKind` (`Conversation` / `Transcription`). See [`normalized-events.md`](./normalized-events.md) §1.
- `RealtimeAgent` produces a `RealtimeSession` (the AF-layer wrapper) from `ConnectSessionAsync(ct)`. That `RealtimeSession` *is* an `IRealtimeClientSession` (or wraps one) with the agent-layer behaviors layered on — most notably auto tool invocation via MEAI's `FunctionInvokingRealtimeClientSession`.

`RealtimeSession`:

- Is **created already-connected**. Construction implies the WebSocket is open and the provider's setup handshake (`session.update`/`session.created` or `BidiGenerateContentSetup`/`SetupComplete`) has been completed.
- Owns the live transport for its entire lifetime.
- Exposes the MEAI session surface:
  - `SendAsync(RealtimeClientMessage, ct)` for client → server traffic (`SessionUpdateRealtimeClientMessage`, `InputAudioBufferAppendRealtimeClientMessage`, `InputAudioBufferCommitRealtimeClientMessage`, `CreateConversationItemRealtimeClientMessage`, `CreateResponseRealtimeClientMessage`).
  - `GetStreamingResponseAsync(...)` returning `IAsyncEnumerable<RealtimeServerMessage>` for the server → client stream.
  - `DisposeAsync()` — closes the socket and ends the logical session.
- Does **not** expose `Serialize`/`Deserialize`. Persistence across process restarts is a Gemini-only concept today; we'll add it additively once a second provider needs it.

We use **"Session"** terminology (not "Connection") in `Microsoft.Agents.AI`, matching what MEAI already ships in `Microsoft.Extensions.AI`. See §4.5 for the resulting naming-overlap caveat vs. `AgentSession`.

### 4.2 Forward-compatibility slot for resumption

To avoid breaking changes when Gemini-style resumption is wired up later:

- `RealtimeSession` exposes a nullable **`string? ConversationId { get; }`**. Semantics mirror `ChatClientAgentSession.ConversationId`: an opaque server-side handle that may be `null`, may rotate over the session's lifetime, and should be treated as sensitive.
  - Foundry / OpenAI today: always `null`.
  - Gemini (stretch): populated and updated as the service issues `SessionResumptionUpdate` messages.
- `RealtimeSessionOptions` (MEAI) is the configured surface; on the AF layer we add an optional `ConversationId` (likely via a thin `RealtimeAgentRunOptions` or an overload on `ConnectSessionAsync`) so a future resumable provider can rehydrate. On providers that don't support resumption this is ignored (or rejected if non-null — TBD).
- Reconnect-on-disconnect, `GoAway` handling, and context-window compression stay **internal** to the Gemini `IRealtimeClient`/`IRealtimeClientSession` implementation. They are not part of the public AF contract. Power-user hooks can be added later via `GetService<T>()` without affecting the baseline API.

### 4.3 Session "modes" (OpenAI)

OpenAI's split between voice-agent / translation / transcription endpoints is modeled with MEAI's `RealtimeSessionKind` (`Conversation`, `Transcription`) on `RealtimeSessionOptions`. Translation does not have a `RealtimeSessionKind` value yet — if/when we cover it, it will be added at the MEAI layer rather than parallel to it. On Foundry this maps to session-config flavors; on Gemini, Translation is unsupported and surfaces as a clear error at connect time.

### 4.3.1 Where configuration lives

Session configuration is supplied **when constructing the `RealtimeAgent`**, not on each `ConnectSessionAsync` call. That is:

```csharp
var agent = new FoundryRealtimeAgent(
    endpoint, credential,
    new FoundryRealtimeAgentOptions { Instructions = "...", Voice = ..., TurnDetection = ... });

await using var session = await agent.ConnectSessionAsync(ct);
```

Rationale:

- For now a given `RealtimeAgent` produces **a single fixed kind of session** — model, instructions, voice, turn detection, input/output formats, kind (`Conversation` vs `Transcription`) — all bound at agent-construction time. Internally these flow into a `RealtimeSessionOptions` (and a provider-specific `RawRepresentationFactory` where needed).
- `ConnectSessionAsync` takes only per-connection inputs (cancellation token; eventually a `ConversationId` for resumption).
- This matches how `ChatClientAgent` works today: agent-level options are immutable; `RunAsync` accepts only invocation-scoped inputs.

This is a *for-now* constraint, pending the introduction of serializable, persistable sessions:

- Once `RealtimeSession`-as-persistable-state exists (the `RealtimeAgentSession : AgentSession` evolution alluded to in §4.5), the agent can stay fixed-config while persisted sessions carry whatever per-conversation state is portable across reconnects/processes.
- Per-session configuration *overrides* (e.g., switching voice or instructions for one connection) are deliberately deferred until we have a concrete need; mid-session reconfiguration via a `SessionUpdateRealtimeClientMessage` on the live session covers the in-session-mutation case for Foundry/OpenAI.

### 4.4 Provider extension surface

The neutral `RealtimeSessionOptions` (MEAI) covers the Foundry baseline: instructions, voice, audio formats (`RealtimeAudioFormat`), VAD (`VoiceActivityDetectionOptions`), and transcription (`TranscriptionOptions`). Provider-specific knobs ride on `RealtimeSessionOptions.RawRepresentationFactory`:

- **Azure-only:** `azure_semantic_vad`, `azure_semantic_vad_multilingual`, `azure_deep_noise_suppression`, `server_echo_cancellation`, HD voices, custom voices, speaking `rate`, filler-word removal.
- **Gemini-only:** `sessionResumption`, `contextWindowCompression`, ephemeral-token auth.
- **OpenAI-only:** SIP transport, dedicated translation/transcription endpoints.

### 4.5 ⚠️ Naming caveat vs. `AgentSession`

`RealtimeSession` is intentionally **not** an `AgentSession`, and the similar name is a known source of potential confusion. We accept this overlap because it matches MEAI's existing `IRealtimeClientSession` terminology, and inventing a different word at the AF layer would create a worse mismatch with the underlayer. The two abstractions differ in important ways:

| | `AgentSession` (e.g., `ChatClientAgentSession`) | `RealtimeSession` |
|---|---|---|
| What it is | A serializable, transport-agnostic *handle* to a logical conversation | The *live* bidirectional connection itself (an `IRealtimeClientSession`) |
| Lifetime | Long-lived; spans many `RunAsync` calls; survives process restarts via serialization | Bounded by the WebSocket; ends when disposed |
| Construction | `agent.CreateSessionAsync()` — no I/O; pure state | `agent.ConnectSessionAsync()` — opens a socket and completes a handshake |
| I/O surface | None — passed *into* `RunAsync` | `SendAsync` / `GetStreamingResponseAsync` directly on the type |
| `ConversationId` | Server-side chat-history pointer (often non-null for hosted agents) | Opaque resumption handle, **null today** on every provider except Gemini |
| Serialization | First-class (`AIAgent.SerializeSessionAsync`) | Not modeled on the base type yet (forward-compat slot only) |

If/when realtime providers grow durable, cross-process session identity, we can introduce a separate `RealtimeAgentSession : AgentSession` that *holds* a `ConversationId` and from which a `RealtimeSession` is opened — mirroring the existing `ChatClientAgent` ⇄ `ChatClientAgentSession` shape. Splitting the live `RealtimeSession` into a separate `RealtimeConnection` type is **not** planned: MEAI has already unified those concepts under `IRealtimeClientSession`, and re-splitting them at the AF layer would diverge from the underlayer for no concrete benefit. Until persistable session state lands, "Session" carries both meanings here, and we live with the documentation caveat above.
