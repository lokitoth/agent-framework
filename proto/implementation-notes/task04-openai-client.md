# Task 04 — Microsoft.Agents.AI.Realtime.OpenAI

Plan reference: `/proto/implementation-plan.md` §4.4.

## Goals

- Provide `OpenAIRealtimeAgent : RealtimeAgent` + options.
- Compose M.E.AI.OpenAI's `OpenAIRealtimeClient` directly (plan §4.4 calls this
  the success path: the type already satisfies `IRealtimeClient`, applies
  `RealtimeSessionOptions` as the initial `session.update`, and exposes the
  session enumeration single-consumer per ADR-002).
- `OpenAIRealtimeSession : RealtimeSession` so consumers can identify and
  decorate the session by concrete type.
- WebSocket-only this phase (no WebRTC/SIP/ephemeral tokens).

## Validation (§4.4 criterion)

Round-trip a session via `FakeRealtimeClient` (TestSupport): apply session
options on connect → send a client message → receive a server message →
disposes cleanly. Covered as `OpenAIRealtimeAgentTests.RoundTrip_Via_Fake`.

## Files

- `src/Microsoft.Agents.AI.Realtime.OpenAI/Microsoft.Agents.AI.Realtime.OpenAI.csproj`
- `src/Microsoft.Agents.AI.Realtime.OpenAI/OpenAIRealtimeAgent.cs`
- `src/Microsoft.Agents.AI.Realtime.OpenAI/OpenAIRealtimeAgentOptions.cs`
- `src/Microsoft.Agents.AI.Realtime.OpenAI/OpenAIRealtimeSession.cs`
- `tests/Microsoft.Agents.AI.Realtime.OpenAI.UnitTests/…`

## Log

- Confirmed via `/proto/external/MEAI/.../OpenAIRealtimeClient.cs` that the
  M.E.AI client owns transport and applies `SessionUpdate` on connect — no
  reason to reimplement; we compose.
- Built agent + session + options. Builder extension `UseOpenAIRealtime` is
  intentionally **not** added in this phase — the standard
  `RealtimeAgentBuilder(agent)` pattern already lets callers drop in an
  `OpenAIRealtimeAgent` instance.

## Outcome

- Build clean.
- `Microsoft.Agents.AI.Realtime.OpenAI.UnitTests`: 9/9 tests passing.
- §4.4 validation criterion satisfied: round-trip via FakeRealtimeClient (SessionUpdate sent, server message received, clean dispose).
