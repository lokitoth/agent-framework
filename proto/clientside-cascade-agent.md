# Client-side Cascading Realtime Agent — Design Notes

This document explores what a **client-side speech→text→speech ("cascading")
realtime agent** looks like under the `RealtimeAgent` story laid out in
[`realtime-agent.md`](./realtime-agent.md). The "sandwich" idea was first
sketched in [`notes.md`](./notes.md):

```text
audio in ──▶ STT ──▶ text ──▶ AIAgent ──▶ text ──▶ TTS ──▶ audio out
```

The goal here is to think through the design as if the STT and TTS endpoints
were themselves accessed through MEAI's `IRealtimeClient` /
`IRealtimeClientSession` abstraction (e.g., Azure Speech, OpenAI realtime
transcription sessions, Foundry Voice Live in transcription/TTS mode). That
framing is *deliberately the hard case*: HTTP-based one-shot STT/TTS is
trivially a special case of the same design.

Three things to nail down:

1. How the pipeline works end-to-end (events, buffering, turn boundaries,
   interruption, errors).
2. What the user-facing API to configure and construct it looks like — i.e.
   the `CascadingRealtimeAgent` (concrete `RealtimeAgent`) and its options.
3. Stretch goal: how/whether **streaming text input** to the realtime surface
   falls out of this design.

---

## 1. End-to-end flow

### 1.1 Components

A cascading agent composes **four** ingredients behind a single
`RealtimeAgent` façade:

| # | Role | Concrete shape |
|---|---|---|
| 1 | **STT session** | `IRealtimeClient` opened with `RealtimeSessionKind.Transcription`, fed user PCM via `InputAudioBufferAppend*` client messages, emitting `InputAudioTranscription*` server messages. |
| 2 | **Inner text agent** | Any `AIAgent` (typically `ChatClientAgent` over a streaming `IChatClient`). Owns the *conversation* — instructions, history, tools, function invocation. |
| 3 | **TTS session** | `IRealtimeClient` opened in a synthesis-oriented configuration (provider-specific; e.g. Voice Live with `output_modalities=["audio"]` and no LLM behind it, or a dedicated speech-synthesis realtime endpoint). Accepts text input and emits `OutputAudio*` server messages. |
| 4 | **VAD / endpointing** | Either (a) **server-side**, surfaced by the STT session (`SpeechStarted/Stopped` updates), or (b) a pluggable **client-side** `VoiceActivityDetector` for STT endpoints that don't do it themselves. |

The cascading agent's job is purely to **wire these four together** and
project them onto the `RealtimeSession` event taxonomy from
`realtime-agent.md` §1.3 so that callers see a single, coherent realtime
session.

### 1.2 Happy-path walkthrough

```
User mic frames                          (PCM ReadOnlyMemory<byte>)
   │
   ▼
CascadingRealtimeSession.AppendInputAudioAsync(bytes)
   │
   ├──▶ STT IRealtimeClientSession.SendAsync(InputAudioBufferAppend)
   │           │
   │           ▼
   │      (server VAD or client VAD fires)
   │      InputTranscriptionDelta…           ─────▶ surfaced as InputTranscriptionDeltaUpdate
   │      InputTranscriptionCompleted("...")
   │
   ▼
On Completed:
   ├─ Append a ChatMessage(User, "...") to inner agent thread
   ├─ Emit ItemCreatedUpdate(user message)
   └─ Call innerAgent.RunStreamingAsync(...)  (or reuse open AgentRun)
            │
            ▼
       AgentResponseUpdate stream (TextContent deltas, FunctionCallContent, …)
            │
            ├──▶ OutputTextDeltaUpdate / OutputTranscriptDeltaUpdate           (text fan-out)
            ├──▶ Per delta: TTS session.SendAsync(text chunk)                  (see §1.4)
            │              │
            │              ▼
            │         OutputAudioDelta… frames ──▶ OutputAudioDeltaUpdate     (audio fan-out)
            │
            └──▶ on FunctionCallContent: FunctionCallInvokedUpdate
                       │ (FunctionInvocationRealtimeAgent decorator handles
                       │  invocation if UseFunctionInvocation() is on)
                       ▼
                   SendToolResultAsync → fed back into inner agent's next turn
On agent completion:
   ├─ Flush TTS (commit / "input_text_done")
   ├─ Emit OutputTextDoneUpdate, OutputAudioDoneUpdate, ResponseCompletedUpdate
   └─ Update local History with the final assistant RealtimeItem
```

Key invariants the cascading session must enforce:

- **Single response-in-flight at a time.** The inner agent's streaming run,
  the TTS session, and the outbound audio fan-out share one logical
  *response*. `CancelResponseAsync` must cancel all three coherently.
- **History is authoritative on the inner agent.** The cascading session's
  `History` is a projection of the inner `AgentThread` plus any synthetic
  `RealtimeItem`s needed to satisfy the realtime contract (e.g., to expose
  function calls that the inner agent treated as internal).
- **Audio bytes are zero-copy where possible.** PCM frames flow through as
  `ReadOnlyMemory<byte>`; the only mandatory copy is when the underlying
  transport requires base64 framing.

### 1.3 Turn detection & interruption

Three modes worth supporting, chosen via `CascadingRealtimeAgentOptions.TurnDetection`:

| Mode | Who decides turn end? | Cancellation on barge-in |
|---|---|---|
| `Server` (default if STT supports it) | STT session emits `SpeechStopped` / `InputTranscriptionCompleted` | Subsequent `SpeechStarted` while a response is in flight ⇒ session calls `CancelResponseAsync` internally (cancel inner agent run, truncate TTS, emit `ResponseCancelledUpdate`). |
| `Client` (`VoiceActivityDetector` plugin) | A client-side detector consumes the same PCM that's forwarded to STT; raises `SpeechStarted/Stopped` callbacks. | Same cancel pathway; detector also drives `CommitInputAudioAsync` on the STT session. |
| `None` (push-to-talk) | Caller invokes `CommitInputAudioAsync()` explicitly. | Caller invokes `CancelResponseAsync()` explicitly. |

Barge-in cancellation is the most subtle part of the cascade. The session
must:

1. Stop pumping further text deltas into TTS.
2. Issue a TTS-side cancel (provider-specific: `response.cancel`, `clear`,
   or simply closing+reopening the synthesis stream).
3. Truncate the assistant `RealtimeItem` in `History` at the last byte the
   client *actually played* — the cascading session needs the caller to
   report playback position back via an explicit
   `NotifyPlaybackPositionAsync(itemId, sampleOffset)` (mirroring OpenAI
   Realtime's `conversation.item.truncate`).
4. Cancel the inner agent's `RunStreamingAsync` `CancellationToken`. If the
   inner agent has already produced text past the cut point, that text is
   kept in the *logical* history but marked truncated so the model sees a
   faithful record of what the user heard.

### 1.4 Streaming text into TTS

The interesting glue is **inner-agent → TTS**. Two strategies:

- **Sentence-buffered (default).** Buffer text deltas until a sentence-ish
  boundary (`. ! ? \n`, or N chars), then flush one chunk into the TTS
  session. Lowest implementation risk; works against any TTS that accepts
  text input. Trade-off: prosody is sentence-bounded.
- **Token-streamed.** Forward every text delta directly into the TTS as it
  arrives, using whatever streaming-text input the TTS session exposes
  (`response.input_text.delta` style). Best prosody / lowest latency-to-
  first-audio, but **requires the TTS endpoint to accept partial text**. If
  the TTS realtime client surface doesn't model partial input text natively,
  see the stretch goal in §3.

The choice is a `CascadingRealtimeAgentOptions.TextChunking` enum
(`Sentence | Token | Custom(Func<…>)`). Detection of TTS capability happens
at connect time (the TTS `IRealtimeClient`'s metadata advertises whether it
supports streamed text input); if `Token` is asked for and unsupported, the
agent falls back to `Sentence` with a single warning log.

### 1.5 Errors & lifecycle

- **STT or TTS faults** surface as `RealtimeErrorUpdate` (recoverable) or
  `ConnectionStateChangedUpdate(Faulted)` + enumeration termination (fatal).
  A fatal fault in either sub-session faults the whole cascading session;
  the cascading session does **not** silently swallow one half.
- **Inner-agent exceptions** propagate as a synthetic `RealtimeErrorUpdate`
  with the exception detail and abort the current response, but the session
  stays open and accepts the next user turn — same contract as a tool that
  throws.
- **Connect ordering:** open STT first, then inner-agent thread (cheap),
  then TTS. Tear down in reverse order on dispose. If TTS connect fails,
  the agent surfaces a typed `CascadeConnectException` instead of leaving a
  half-open STT session.

---

## 2. User-facing API

### 2.1 Package placement

- **Type:** `CascadingRealtimeAgent : RealtimeAgent`
- **Package:** `Microsoft.Agents.AI.Realtime` (non-provider). Cascading has
  no provider dependency of its own — its STT and TTS sub-clients are
  injected as `IRealtimeClient` instances by the caller, and the inner
  text model is an `AIAgent`. This keeps it in the same "concrete but
  provider-neutral" tier as `RealtimeAgentBuilder` and the built-in
  decorators (§2.1–§2.2 in `realtime-agent.md`).

### 2.2 Options

```csharp
public sealed class CascadingRealtimeAgentOptions
{
    // Required ingredients
    public required AIAgent InnerAgent { get; init; }
    public required IRealtimeClient SpeechToText { get; init; }
    public required IRealtimeClient TextToSpeech { get; init; }

    // Sub-session configuration (flowed into the respective IRealtimeClient
    // at ConnectAsync time; mirror the MEAI RealtimeSessionOptions surface).
    public RealtimeSessionOptions? SpeechToTextSessionOptions { get; init; }
    public RealtimeSessionOptions? TextToSpeechSessionOptions { get; init; }

    // Turn detection (see §1.3). Defaults to Server when STT advertises it,
    // otherwise None.
    public CascadingTurnDetection TurnDetection { get; init; }
        = CascadingTurnDetection.Auto;
    public VoiceActivityDetector? ClientVoiceActivityDetector { get; init; }

    // Inner-agent → TTS text chunking strategy (see §1.4).
    public TextChunkingStrategy TextChunking { get; init; }
        = TextChunkingStrategy.Sentence;

    // Inner-agent thread management. If null, a new thread is created per
    // ConnectAsync; otherwise this thread is reused (and its history shows
    // up in CascadingRealtimeSession.History).
    public AgentThread? InnerAgentThread { get; init; }

    // Voice / audio format passthroughs that the agent will use to
    // populate TTS session options when caller didn't supply them.
    public RealtimeVoice? Voice { get; init; }
    public RealtimeAudioFormat? OutputAudioFormat { get; init; }
    public RealtimeAudioFormat? InputAudioFormat { get; init; }
}
```

### 2.3 Construction

Three idiomatic shapes, picking up the patterns already used elsewhere in
Agent Framework:

**(a) Direct ctor — power user.**

```csharp
var cascade = new CascadingRealtimeAgent(new CascadingRealtimeAgentOptions
{
    InnerAgent  = chatAgent,                        // ChatClientAgent or anything
    SpeechToText = azureSpeechRealtimeClient,       // IRealtimeClient
    TextToSpeech = azureSpeechSynthesisRealtimeClient,
    Voice = new RealtimeVoice("en-US-AvaNeural"),
    TurnDetection = CascadingTurnDetection.Server,
});

await using var session = await cascade.ConnectAsync(ct);
```

**(b) Builder extension — composes with the decorator stack.**

```csharp
RealtimeAgent agent = new RealtimeAgentBuilder()
    .UseLogging()
    .UseOpenTelemetry()
    .UseFunctionInvocation()
    .UseCascade(opts =>
    {
        opts.InnerAgent   = chatAgent;
        opts.SpeechToText = sttClient;
        opts.TextToSpeech = ttsClient;
    })
    .Build();
```

`UseCascade` is the terminal factory (it produces the inner-most
`RealtimeAgent`); the decorator chain wraps it just like
`UseFunctionInvocation` etc. wrap a provider's `OpenAIRealtimeAgent`.

**(c) Fluent extension on `AIAgent`** — the most discoverable for the
common "I already have a `ChatClientAgent` and want voice on top" path:

```csharp
RealtimeAgent voice = chatAgent.AsCascadingRealtimeAgent(
    speechToText: sttClient,
    textToSpeech: ttsClient,
    configure: o => o.Voice = new RealtimeVoice("alloy"));
```

This is the analog of `RealtimeAgentAsAIAgent` in the opposite direction —
making an existing `AIAgent` consumable on the realtime surface.

### 2.4 What the `RealtimeSession` looks like

`CascadingRealtimeSession : RealtimeSession` implements the full surface
from `realtime-agent.md` §1.2. Notable mapping points:

| Member | Cascading implementation |
|---|---|
| `AppendInputAudioAsync(pcm)` | Forwarded to STT; also fed to the client VAD if configured. |
| `CommitInputAudioAsync()` | Forwarded to STT only. |
| `SendMessageAsync(ChatMessage)` | Adds directly to inner agent's thread, skipping STT; triggers a response (so text-only chat works in the same session). |
| `RequestResponseAsync()` | Runs the inner agent against the current thread, then runs TTS over the output. |
| `CancelResponseAsync()` | Cancels inner-agent CT; cancels TTS; emits `ResponseCancelledUpdate` (see §1.3). |
| `UpdateSessionAsync(opts)` | Splits opts: instructions/tools → inner agent's `ChatOptions`; voice/audio format → TTS sub-session; VAD → STT sub-session. |
| `SendToolResultAsync(...)` | Routed to the inner agent's function-result pathway (not to either speech sub-client). |
| `History` | Projection over the inner `AgentThread.Messages` plus any synthetic items needed for parity. |
| `ReceiveUpdatesAsync()` | Merged stream of mapped STT updates, mapped inner-agent updates, mapped TTS updates, and synthetic cascade-level updates (response start/complete, errors, state changes). |
| `GetService<T>()` | Returns the inner agent, sub-clients, and the cascading agent itself for power-user access. |

### 2.5 Metadata

`CascadingRealtimeAgent.Metadata` advertises:

- `Provider = "cascade"`,
- `Model = innerAgent.Metadata.Model` (or the inner agent's name),
- `SupportedModalities = Text | Audio`,
- `SupportsServerVad = sttClient.Metadata.SupportsServerVad`,
- `SupportsInterruption = true` (the cascade implements it regardless of
  whether the underlying speech endpoints do),
- `SupportsVideo = false`.

This lets `RealtimeAgentAsAIAgent`, OTel, and capability-gating decorators
treat it identically to a native realtime agent.

---

## 3. Stretch goal — streaming text into the realtime surface

There are two distinct "stream text in" use cases worth disentangling:

### 3.1 Streaming text from an upstream component into a realtime agent

E.g., a workflow node produces tokens from some other LLM and wants to push
them into a realtime agent for speech synthesis without round-tripping
through full messages. This is **already supported out of the box** by the
design in `realtime-agent.md` if we add one helper:

```csharp
ValueTask AppendInputTextAsync(string textDelta, CancellationToken ct);
ValueTask CommitInputTextAsync(CancellationToken ct);
```

…as siblings of `AppendInputAudioAsync` / `CommitInputAudioAsync` on
`RealtimeSession`. Semantics:

- For a **native** realtime agent (OpenAI, Foundry, Gemini) these map to
  the provider's existing partial-user-text events
  (`conversation.item.create` with incremental `input_text` parts, or
  `clientContent` chunks for Gemini), or — for providers that don't have
  partial text input — buffer locally until commit, then send one message.
- For the **cascading** agent these:
  1. Buffer the text into the inner agent's *next user message*,
     short-circuiting STT entirely.
  2. On commit, run the inner agent + TTS pipeline exactly as in §1.2.

This is purely additive to the abstraction in `realtime-agent.md` — no
breaking changes. It collapses the "text-only chat over a voice session"
case into a single operation rather than requiring the caller to construct
a full `ChatMessage`.

### 3.2 Streaming text from the inner agent into the TTS sub-session

This is the cascading-only case covered in §1.4. The "stretch" question is
whether MEAI's `IRealtimeClient` abstraction can faithfully model a TTS
endpoint that accepts **partial text input** without inventing a parallel
type system.

What's needed at the MEAI layer:

- A `RealtimeClientMessage` subtype for *partial text input* — analogous
  to the existing `InputAudioBufferAppend` for audio. The OpenAI Realtime
  taxonomy already implies this shape via `conversation.item.create` +
  `response.create` with streamed text parts; the equivalent for Voice
  Live's TTS-only sessions and for Azure Speech streaming TTS is
  `synthesis.text.append` / `synthesis.text.commit` style messages.
- A `RealtimeSessionKind.Synthesis` (or a third value) so the TTS
  sub-client can be opened in a synthesis-only mode that the cascading
  agent can reliably target. Today MEAI exposes `Conversation` and
  `Transcription` only.

If those two additions are accepted, **everything else falls out for
free**: the cascading agent already drives the TTS via `SendAsync`, so a
token-streamed mode is a one-line change in the text-chunking strategy —
"flush immediately" instead of "buffer until sentence boundary".

If the MEAI surface can't grow that shape near-term, the fallback is a
narrow internal abstraction inside `Microsoft.Agents.AI.Realtime`:

```csharp
internal interface IStreamingTextToSpeechClient
{
    ValueTask AppendTextAsync(string delta, CancellationToken ct);
    ValueTask CommitAsync(CancellationToken ct);
    IAsyncEnumerable<RealtimeAudioFrame> ReadAudioAsync(CancellationToken ct);
}
```

…with adapters from the provider-specific TTS SDKs. This keeps the public
`CascadingRealtimeAgentOptions.TextToSpeech` typed as `IRealtimeClient`
for the common case, while providing a richer fast path for providers
that natively stream text input. Users opt in via a second optional
property (`StreamingTextToSpeech`) — when set, the agent prefers it and
ignores `TextToSpeech`.

### 3.3 Recommendation

- **Ship `AppendInputTextAsync` / `CommitInputTextAsync` on the
  `RealtimeSession` base** as part of v1. Cheap; useful beyond cascading.
- **Default `CascadingRealtimeAgent.TextChunking` to `Sentence`** for v1.
- **Pursue MEAI additions** (partial text input message + `Synthesis`
  session kind) in parallel; once landed, flip the default to `Token` for
  TTS clients that advertise support, with `Sentence` as the
  compatibility fallback.

---

## 4. Open questions

1. **Where does the inner agent's `AgentThread` live?** Owned by the
   cascading session and disposed with it, or owned by the caller and
   passed in (per §2.2)? Proposal: **caller-owned if provided, else
   session-scoped**, mirroring how `ChatClientAgent` treats `AgentThread`.
2. **Function-call audio interjection.** Should tools be able to push a
   "let me check…" audio prompt mid-execution? The
   `RealtimeFunctionInvocationContext` already exposes the session, so this
   is mechanically possible — but it needs a deliberate API
   (`Context.SpeakAsync(text)`) and a documented contract around how it
   interacts with the assistant's own ongoing TTS.
3. **History fidelity for serialization.** When `SerializeSessionAsync`
   runs, do we persist the audio bytes the user heard, the transcripts, or
   both? Proposal: transcripts only (matching the non-cascading
   `RealtimeAgent` default per `realtime-agent.md` §4 Q5), with raw audio
   available only through an opt-in `UseAudioRecording()` decorator.
4. **Inner-agent streaming requirement.** Does the cascade *require* a
   streaming-capable inner agent (`AIAgent.RunStreamingAsync` that emits
   token-level deltas)? Practically, yes, to keep latency-to-first-audio
   competitive. Non-streaming agents work but only with `Sentence`
   chunking and add a full inner-agent-response of latency.
5. **VAD plug-in surface.** `VoiceActivityDetector` is currently
   hand-waved. Likely a small interface
   (`Process(ReadOnlySpan<byte> pcm) → VadEvent?`) with a default Silero
   or WebRTC-VAD implementation behind a separate optional package.
6. **Translation-style "cascade".** The same chassis could host an
   inner *translation* step (STT → MT → TTS, no chat agent). Out of scope
   for v1; revisit alongside the OpenAI translation session work mentioned
   in `session.md` §4.3.
