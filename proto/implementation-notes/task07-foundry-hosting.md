# Task 07 — Microsoft.Agents.AI.Foundry.Hosting.Realtime

Plan reference: `/proto/implementation-plan.md` §5.2.

## Goals

- `InvocationsRealtimeAgentTransportHandler` — `POST /invocations` + SSE.
  Behind an `IInvocationsRequestSink` abstraction so unit tests don't
  need a TestServer (per plan §5.2).
- `VoiceLiveInvocationsEventEncoder` — emits the
  `output_audio_transcription.delta/.done/done` SSE shape the Python
  sample hand-codes (see `vl_sample/hello-world-invocations-voicelive/main.py`).
- `NeutralRealtimeInvocationsEventEncoder` — control comparator
  (`text.delta` / `text.done` / `done`).
- `AddFoundryRealtime` extension — registers VoiceLive encoder as the
  default and the transport handler.

This package is **standalone** per §3.6: it does not take a project
reference on `dotnet/src/Microsoft.Agents.AI.Foundry.Hosting`. The full
shared-internals integration (`HostedAgentUserAgentPolicy`,
`PlatformHostedSessionIsolationKeyProvider`, etc.) is tracked as a
follow-up.

## Files

- `src/Microsoft.Agents.AI.Foundry.Hosting.Realtime/`
  - `IInvocationsRequestSink.cs`
  - `InvocationsRealtimeAgentTransportHandler.cs`
  - `VoiceLiveInvocationsEventEncoder.cs`
  - `NeutralRealtimeInvocationsEventEncoder.cs`
  - `FoundryRealtimeServiceCollectionExtensions.cs`

- `tests/Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests/`
  - `EncoderTests.cs` — VoiceLive + Neutral SSE frame snapshots.
  - `InvocationsRealtimeAgentTransportHandlerTests.cs` — end-to-end with
    canned server-message streams, inbound client message pump,
    AddFoundryRealtime DI verification.

## Outcome

- Build clean across realtime.slnx.
- `Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests`: 12/12 passing.
- **Full solution: 137/137 tests** (Abstractions 46, Realtime 43,
  OpenAI 9, Foundry 15, Hosting 12, Foundry Hosting 12).
