# Task 03 — Microsoft.Agents.AI.Realtime (concrete, non-provider)

Plan reference: `/proto/implementation-plan.md` §4.2.

## Goals

Build the concrete, non-provider realtime package:

- `RealtimeAgentBuilder` + `Use(...)` (parallels `AIAgentBuilder`).
- `LoggingRealtimeAgent` + `LoggingRealtimeAgentBuilderExtensions`
  (audio byte payloads redacted as length per review §S1).
- `OpenTelemetryRealtimeAgent` + `OpenTelemetryRealtimeAgentBuilderExtensions`
  (span/meter names; no exporter wiring).
- `FunctionInvocationRealtimeAgent` (composes M.E.AI's
  `FunctionInvokingRealtimeClientSession`).
- `AnonymousDelegatingRealtimeAgent`.
- `RealtimeAgentAsAIAgent` (transcript-only bridge per plan §4.2).
- `InMemoryRealtimeHistoryProvider` + `HistoryProjectingRealtimeSession`
  per ADR-004.
- `RealtimeAudioPipe` / `RealtimeAudioWriter`.
- Internal helpers: `DelegatingRealtimeSession`, `WrappingRealtimeSession`.

## Design notes

### Cross-session wrapping pattern

`RealtimeSession.InnerSession` is `protected`. To layer middleware
(`Logging`, `OTel`, `FunctionInvocation`), the core package introduces a
`DelegatingRealtimeSession` base that pulls the underlying
`IRealtimeClientSession` out of the wrapped session via
`GetService<IRealtimeClientSession>()` and reuses its `StateBag`. All
operations then forward to the wrapped `RealtimeSession` so AF-side
projection state (history, ConversationId, Options) is preserved.

### FunctionInvocation composition

Use M.E.AI's `FunctionInvokingRealtimeClientSession` directly by wrapping
the underlying `IRealtimeClientSession` from the provider's session. The
returned `RealtimeSession` is a `WrappingRealtimeSession` over the
function-invoking session so the response loop runs through MEAI's
implementation.

### Logging redaction

`LoggingRealtimeSession.SendAsync` inspects `RealtimeClientMessage`
subtypes; for `InputAudioBufferAppendRealtimeClientMessage` the content
payload is rendered as `Audio(length=N)` rather than as raw bytes.

### History projection (ADR-004)

`HistoryProjectingRealtimeSession` wraps a session and intercepts
`GetStreamingResponseAsync` to append `RealtimeConversationItem` entries
to the projected `History` collection when
`ResponseOutputItemRealtimeServerMessage` arrives with status
`ResponseOutputItemDone`.

### Bridge: `RealtimeAgentAsAIAgent`

Transcript-only: opens a session per `RunAsync` call, sends the user
message via `SendMessageAsync`, fires `RequestResponseAsync`, drains the
stream collecting `OutputTextAudioRealtimeServerMessage` text deltas
until `ResponseDone`. Audio data surfaces via `AdditionalProperties` on
the response (key `"realtime.audio"`, value: list of base64 audio
chunks). Out of scope: multi-turn within a single session; cascade
patterns.

## Implementation log

- 2026-05-20 — Plan + diary stub created; project skeleton next.
- 2026-05-20 — Source files written: csproj (+ MEAI/AF refs +
  InternalsVisibleTo), RealtimeAgentBuilder (+ Use overloads),
  WrappingRealtimeSession, DelegatingRealtimeSession, LoggingRealtimeAgent
  + LoggingRealtimeSession + extensions (audio redacted as `Audio(length=N)`),
  OpenTelemetryRealtimeAgent + Session + extensions +
  RealtimeOpenTelemetryConsts, FunctionInvocationRealtimeAgent + Session +
  extensions (composes MEAI's FunctionInvokingRealtimeClient via a
  SingleSessionRealtimeClient adapter), AnonymousDelegatingRealtimeAgent
  + extensions, HistoryProjectingRealtimeSession,
  InMemoryRealtimeHistoryProvider, RealtimeAudioPipe / Writer,
  RealtimeAgentAsAIAgent. Added `Microsoft.Agents.AI.Realtime` to
  `realtime.slnx`. First build failed:
  (a) RealtimeAgentAsAIAgent missing 3 AIAgent abstracts
  (Create/Serialize/DeserializeSessionCoreAsync); added throwing
  overrides (bridge is transcript-only). (b) DelegatingRealtimeSession
  remarks used `paramref` on a class-level tag; reworded. Build clean.
  Grep confirms no `var` usage in the package.
- 2026-05-20 — Wrote unit tests `Microsoft.Agents.AI.Realtime.UnitTests`
  covering: Builder composition + decorator ordering, Logging audio
  redaction, OpenTelemetry activity emission, AnonymousDelegating
  happy path, HistoryProjecting projection of `ConversationItemDone`
  + `ResponseOutputItemDone`, InMemoryRealtimeHistoryProvider
  Append/GetHistory/Clear, RealtimeAgentAsAIAgent transcript drain.

## Outcome

- Test project added to realtime.slnx.
- Build clean.
- `Microsoft.Agents.AI.Realtime.UnitTests`: 43/43 tests passing (RealtimeAgentBuilder, LoggingRealtimeAgent, OpenTelemetryRealtimeAgent, AnonymousDelegatingRealtimeAgent, HistoryProjectingRealtimeSession, InMemoryRealtimeHistoryProvider, RealtimeAgentAsAIAgent, RealtimeAudioPipe, FunctionInvocationRealtimeAgent).
- Fixed xUnit2021: awaited `Assert.ThrowsAsync` in `SessionApis_Throw_NotSupported`.
