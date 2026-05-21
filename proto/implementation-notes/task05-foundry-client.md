# Task 05 — Microsoft.Agents.AI.Realtime.Foundry

Plan reference: `/proto/implementation-plan.md` §4.3.

## Goals

- `FoundryRealtimeAgent : RealtimeAgent` + `FoundryRealtimeAgentOptions`
  (Endpoint, AgentName/ProjectName, Credential / ApiKey, SessionOptions,
  Name/Description).
- Conversation mode only this phase.
- Internal `IWebSocketTransport` seam; production code throws
  `NotSupportedException` (the prototype does not perform a real WS
  handshake — the live transport is a follow-up). Tests inject a
  `FakeWebSocketTransport` via `InternalsVisibleTo`.
- `FoundryRealtimeClientSession : IRealtimeClientSession` projecting
  raw VoiceLive JSON to AF normalized `RealtimeServerMessage` (and
  serializing client messages outbound via `FoundryClientMessageEncoder`).
- `FoundryRealtimeSession : RealtimeSession`.
- Azure-only knobs ride on `RealtimeSessionOptions.RawRepresentationFactory`
  (not typed on the AF surface).

## Files

- `src/Microsoft.Agents.AI.Realtime.Foundry/`
  - `FoundryRealtimeAgent.cs`
  - `FoundryRealtimeAgentOptions.cs`
  - `FoundryRealtimeSession.cs`
  - `FoundryRealtimeClientSession.cs`
  - `FoundryEventProjector.cs` — VoiceLive → AF normalized events,
    including `output_audio_buffer.cleared` → `InterruptedRealtimeServerMessage`
    (normalized-events §6 G1).
  - `FoundryClientMessageEncoder.cs` — AF/MEAI client message → VoiceLive
    wire JSON. Maps `session.update`, `input_audio_buffer.append/.commit`,
    `conversation.item.create`, `response.create`, `response.cancel`.
  - `IWebSocketTransport.cs` — internal interface.

- `tests/Microsoft.Agents.AI.Realtime.Foundry.UnitTests/`
  - `FakeWebSocketTransport.cs`
  - `FoundryEventProjectorTests.cs` — 7 inbound projection tests.
  - `FoundryRealtimeAgentTests.cs` — 8 tests covering ctor guards,
    production NotSupported, transport open + session.update,
    transport disposal on session dispose, streaming projection,
    outbound JSON serialization.

## Log

- Initial JsonSerializerOptions failed under net10 reflection mode
  because `MakeReadOnly()` requires an explicit
  `TypeInfoResolver`. Set `DefaultJsonTypeInfoResolver` on the
  abstractions' DefaultOptions; this fixed Foundry tests.
- MEAI's `RealtimeClientMessage` carries no wire `type` discriminator,
  so each provider must encode it. Added `FoundryClientMessageEncoder`
  to map AF subtype → VoiceLive event name.

## Outcome

- Build clean.
- `Microsoft.Agents.AI.Realtime.Foundry.UnitTests`: 15/15 tests passing.
