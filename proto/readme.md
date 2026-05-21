# `/proto` — RealtimeAgent prototype workspace

This folder holds the design notes, implementation, sample, and analysis
artifacts for the `RealtimeAgent` prototype. It is intentionally
self-contained: design docs live next to the standalone `realtime.slnx`
solution they describe, and there are no cross-cutting build or test
hooks into the rest of the repository.

For a quick "what's done / what's next" view, start with
[`prototype-summary.md`](./prototype-summary.md).

---

## Folder structure

```
/proto/
├── readme.md                          ← this file
├── prototype-summary.md               ← what landed, next steps, open gaps
│
├── notes.md                           ← scratch notes / scope sketches
├── misc-notes.md                      ← small decisions and reminders
│
├── realtime-agent.md                  ← client-side type/package outline
├── realtime-agent-test.md             ← client-side test plan
├── realtime-hosting.md                ← hosting-side outline
├── realtime-hosting-test.md           ← hosting-side test plan
├── hosting.md                         ← Invocations + Foundry hosting design
├── session.md                         ← RealtimeSession shape (authoritative)
├── events.md                          ← raw provider event reference
├── normalized-events.md               ← normalized event taxonomy (authoritative)
│
├── clientside-cascade-agent.md        ← cascade (STT → AIAgent → TTS) design
├── clientside-cascade-agent-test.md   ← cascade test plan
│
├── implementation-plan.md             ← phase-1 build plan (drives /impl)
├── implementation-plan-review.md      ← v1 plan review
├── implementation-plan-review-2.md    ← v2 plan review
│
├── implementation-notes/              ← per-task diaries
│   ├── task01-soln-skeleton.md
│   ├── task02-abstractions.md
│   ├── task03-realtime-core.md
│   ├── task04-openai-client.md
│   ├── task05-foundry-client.md
│   ├── task06-hosting-shared.md
│   └── task07-foundry-hosting.md
│
├── client-implementation-gaps.md      ← post-impl gap analysis (provider wire)
├── audio-pipe-performance.md          ← RealtimeAudioPipe performance review
│
├── impl/                              ← the prototype solution
│   ├── realtime.slnx
│   ├── global.json
│   ├── Directory.Build.props
│   ├── Directory.Packages.props
│   ├── src/
│   │   ├── Microsoft.Agents.AI.Realtime.Abstractions/
│   │   ├── Microsoft.Agents.AI.Realtime/
│   │   ├── Microsoft.Agents.AI.Realtime.OpenAI/
│   │   ├── Microsoft.Agents.AI.Realtime.Foundry/
│   │   ├── Microsoft.Agents.AI.Realtime.Hosting/
│   │   └── Microsoft.Agents.AI.Foundry.Hosting.Realtime/
│   └── tests/
│       ├── TestSupport/
│       │   └── Microsoft.Agents.AI.Realtime.TestSupport/
│       ├── Microsoft.Agents.AI.Realtime.Abstractions.UnitTests/
│       ├── Microsoft.Agents.AI.Realtime.UnitTests/
│       ├── Microsoft.Agents.AI.Realtime.OpenAI.UnitTests/
│       ├── Microsoft.Agents.AI.Realtime.Foundry.UnitTests/
│       ├── Microsoft.Agents.AI.Realtime.Hosting.UnitTests/
│       └── Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests/
│
└── vl_sample/                         ← reference Python sample
    ├── hello-world-invocations-voicelive/   ← BYO Invocations + VoiceLive
    └── client/                              ← voicelive_client.py reference
```

---

## Document map by purpose

### "I want to understand what was built"

- [`prototype-summary.md`](./prototype-summary.md) — current state, what
  passes, next steps, important gaps.
- [`implementation-notes/`](./implementation-notes/) — per-task diaries
  (decisions, deviations, build/test outcomes).
- [`impl/`](./impl/) — the solution itself.

### "I want to understand the design"

Authoritative type / event shapes:

- [`session.md`](./session.md) — `RealtimeSession` (already-connected,
  wraps `IRealtimeClientSession`, no Serialize/Deserialize this phase).
- [`normalized-events.md`](./normalized-events.md) — the cross-provider
  event taxonomy; defines the small set of AF additions on top of M.E.AI.

Package / layer outlines:

- [`realtime-agent.md`](./realtime-agent.md) — client-side packages,
  types, and dependency graph.
- [`realtime-hosting.md`](./realtime-hosting.md) — hosting-side packages
  (shared `…Realtime.Hosting` and the Foundry-tier
  `…Foundry.Hosting.Realtime`).
- [`hosting.md`](./hosting.md) — Invocations transport contract and the
  VoiceLive event vocabulary that layers on top of it.
- [`clientside-cascade-agent.md`](./clientside-cascade-agent.md) —
  STT → `AIAgent` → TTS cascade (specified, not scheduled this phase).

Test plans (mirror the design docs):

- [`realtime-agent-test.md`](./realtime-agent-test.md)
- [`realtime-hosting-test.md`](./realtime-hosting-test.md)
- [`clientside-cascade-agent-test.md`](./clientside-cascade-agent-test.md)

Provider-event reference / scope notes:

- [`events.md`](./events.md), [`notes.md`](./notes.md),
  [`misc-notes.md`](./misc-notes.md).

### "I want to understand the plan that drove the build"

- [`implementation-plan.md`](./implementation-plan.md) — phase-1 plan
  (after both reviews). Sections §3 (resolved type-surface decisions),
  §4 (client-side), §5 (hosting-side), §7 (sequencing).
- [`implementation-plan-review.md`](./implementation-plan-review.md) +
  [`implementation-plan-review-2.md`](./implementation-plan-review-2.md)
  — review traces that produced §3.

### "I want to understand the gaps and follow-ups"

- [`client-implementation-gaps.md`](./client-implementation-gaps.md) —
  post-implementation gap analysis for the provider packages (most
  notably the Foundry FND-1..FND-16 list, plus the
  `Azure.AI.VoiceLive` recommendation).
- [`audio-pipe-performance.md`](./audio-pipe-performance.md) —
  `RealtimeAudioPipe` performance and tuning notes.

---

## The `impl/` solution

`impl/realtime.slnx` is a standalone .NET solution. It targets
`net10.0` only, treats warnings as errors, and pins packages centrally.
It references back into `/dotnet/src/Microsoft.Agents.AI*` projects
(via `$(DotnetSrcRoot)` in `Directory.Build.props`) for the existing AF
abstractions it composes (`AgentSessionStateBag`, `AIAgent`,
`AIAgentBuilder`).

### Source projects (`impl/src/`)

| Project | Purpose |
| --- | --- |
| `Microsoft.Agents.AI.Realtime.Abstractions` | Public surface: `RealtimeAgent`, `RealtimeSession`, metadata, the two AF-side message subtypes, JSON utilities. Built on M.E.AI 10.5's `IRealtimeClient` / `IRealtimeClientSession`. |
| `Microsoft.Agents.AI.Realtime` | Concrete, non-provider: builder, decorators (`Logging`, `OpenTelemetry`, `FunctionInvocation`, `AnonymousDelegating`), history projection, in-memory history provider, audio pipe, `RealtimeAgentAsAIAgent` bridge. |
| `Microsoft.Agents.AI.Realtime.OpenAI` | Composes M.E.AI.OpenAI's `OpenAIRealtimeClient`. No bespoke transport. |
| `Microsoft.Agents.AI.Realtime.Foundry` | Hand-rolled prototype VoiceLive client behind an internal `IWebSocketTransport`. Tests inject a fake. |
| `Microsoft.Agents.AI.Realtime.Hosting` | Transport-neutral hosted-side primitives: `HostedRealtimeAgent`, `AddRealtimeAgent` keyed-DI, transport / encoder abstractions, `RealtimeAgentTransportHandler`. |
| `Microsoft.Agents.AI.Foundry.Hosting.Realtime` | Foundry-tier hosting with the Invocations transport fused in: `InvocationsRealtimeAgentTransportHandler`, `VoiceLiveInvocationsEventEncoder`, `NeutralRealtimeInvocationsEventEncoder`, `AddFoundryRealtime`. |

### Test projects (`impl/tests/`)

One `*.UnitTests` project per source project, plus
`TestSupport/Microsoft.Agents.AI.Realtime.TestSupport` which provides
`FakeRealtimeClient`, `FakeRealtimeClientSession`,
`FakeWebSocketTransport`, recorders, and JSON DSL helpers shared by the
provider and hosting test projects.

All tests are unit tests against in-process fakes; no real provider
endpoints are exercised. Current totals: **137 / 137 passing**.

### Building / testing

```powershell
dotnet build proto/impl/realtime.slnx
dotnet test  proto/impl/realtime.slnx
```

---

## The `vl_sample/` sample

`vl_sample/hello-world-invocations-voicelive` is a Python "hello world"
hosted agent that demonstrates the **Invocations protocol** + VoiceLive
event vocabulary that the .NET `Microsoft.Agents.AI.Foundry.Hosting.Realtime`
package mirrors. The encoder in
`VoiceLiveInvocationsEventEncoder.cs` emits the same
`output_audio_transcription.delta` / `.done` / `done` SSE shape this
sample hand-rolls. `vl_sample/client/voicelive_client.py` is a reference
WebSocket client used to interact with the sample.

The sample is reference material only — it has its own `requirements.txt`
and `Dockerfile`, and it does not participate in the .NET build.
