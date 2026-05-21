# Review — `proto/implementation-plan.md`

Cross-check of `proto/implementation-plan.md` against the rest of `/proto`
and the corresponding text-agent packages under
`/dotnet/src/Microsoft.Agents.AI.*` (+ unit tests). Focus is on broad
agreement with decisions, not pedantry.

---

## Where the plan is solidly on the rails

- **Scope discipline.** Client-first, hosting-second, cascade-deferred
  matches the design docs' stated maturity (`session.md` §4.1,
  `clientside-cascade-agent.md`). Explicit non-goals (WebRTC/SIP, Gemini,
  Nova, real-network tests, OTel exporter wiring, DevUI) match
  `misc-notes.md` and `realtime-hosting.md` §6.
- **Sequencing.** Abstractions → core → providers (parallelizable) →
  hosting tracks `realtime-agent.md` §5 dependency graph and
  `realtime-hosting.md` §7 cleanly.
- **DI / keyed-by-name pattern.** §4.1's `AddRealtimeAgent` +
  `HostedRealtimeAgent : DelegatingRealtimeAgent` is a faithful mirror
  of the existing `AddAIAgent` / `AIHostAgent` shape verified in
  `dotnet/src/Microsoft.Agents.AI.Hosting/{HostApplicationBuilderAgentExtensions,AIHostAgent,IHostedAgentBuilder,AgentSessionStore}.cs`.
- **Diagnostic ID convention.** `[Experimental("MEAI-REALTIME-001")]`
  is consistent with the existing `DiagnosticIds.Experiments.AIOpenAIResponses`
  pattern in `Microsoft.Agents.AI.Foundry.Hosting`.
- **Test infrastructure.** `FakeWebSocketTransport` +
  `RealtimeUpdateRecorder` + `JsonEventScript` shared in `TestSupport/`
  mirrors patterns the test docs call out (`TestAIAgent.cs`-style fakes)
  and the existing `dotnet/tests` conventions
  (`FakeAuthenticationTokenProvider.cs`, `WorkflowTestAgents.cs`).
- **Cascade is correctly out of band.** §5 captures the API shape from
  `clientside-cascade-agent.md` and flags the `AppendInputTextAsync` /
  `CommitInputTextAsync` forward-compat concern.

---

## Substantive disagreements with the other proto docs

These are the real "are we sure about this?" items, not bikesheds.

### 1. §3.1 recreates a parallel event taxonomy that `normalized-events.md` / `misc-notes.md` / `session.md` §4.1 explicitly say not to build

`normalized-events.md` §1/§6 and `misc-notes.md` are emphatic: reuse
`IRealtimeClient`, `IRealtimeClientSession`, `RealtimeClientMessage`,
`RealtimeServerMessage`, `RealtimeConversationItem`,
`RealtimeSessionOptions`, `RealtimeAudioFormat` from M.E.AI (10.5.1 is
already pinned in `Directory.Packages.props`). The only AF-side
additions called out are `RealtimeAgentInterruptedEvent` (G1) and the
intentional non-modelling of Gemini `generationComplete` (G3).

The implementation plan §3.1 still enumerates a full first-class
`RealtimeSessionUpdate` hierarchy (`SessionCreatedUpdate`,
`InputAudioBufferAppendedUpdate`, `SpeechStartedUpdate`,
`OutputAudioDeltaUpdate`, …) plus a parallel `RealtimeClientEvent`
hierarchy (`InputAudioAppendEvent`, `ResponseCreateEvent`, …) plus a
redefined `RealtimeAudioFormat` (same name as the M.E.AI type —
guaranteed collision). It hedges by calling these "thin projections,"
but the enumerated list is the OpenAI Realtime taxonomy with AF naming,
not a thin projection.

**Recommendation.** Either commit to the M.E.AI surface as the public
AF API (per `session.md` §4.1, "`RealtimeSession` *is* an
`IRealtimeClientSession` (or wraps one)") and add only the two
targeted gap-fillers, *or* explicitly resolve the conflict in an ADR
before §3.1 ships. The plan picks neither path.

### 2. `RealtimeSession` shape contradicts `session.md` §4.1

`session.md` §4.1 says explicitly:

- The session is **created already-connected**.
- It exposes `SendAsync(RealtimeClientMessage, ct)` and
  `GetStreamingResponseAsync(...)` returning
  `IAsyncEnumerable<RealtimeServerMessage>`.
- It **does not** expose `Serialize` / `Deserialize` (resumption is
  Gemini-only today).

The plan §3.1 instead keeps the older `realtime-agent.md` §1 shape:
`ConnectAsync` separate from `CreateSessionAsync` (disconnected for
serialization), `SerializeSessionAsync` / `DeserializeSessionAsync`,
`ReceiveUpdatesAsync` returning AF-typed `RealtimeSessionUpdate`. That
older shape predates the M.E.AI-alignment pass; the plan picks up that
older shape verbatim without acknowledging the §4.1 refinement.

This also propagates into hosting §4.1:
`HostedRealtimeAgent.GetOrCreateSessionAsync` + `RealtimeSessionStore`
round-trip presumes the serialize/deserialize story `session.md` §4.1
rejects. If serialize-on-AIAgent is the intended fallback, that needs
to be said.

### 3. `IWebSocketTransport` is invented without sanity-checking M.E.AI

§3.3 / §3.4 specify "`FoundryRealtimeSession : RealtimeSession` driven
by an injectable WebSocket abstraction (`IWebSocketTransport`)." There
is no mention of `IRealtimeClient` even though M.E.AI 10.5.1 ships it
and `misc-notes.md` / `normalized-events.md` direct us to build on top
of it. For OpenAI specifically, `Microsoft.Extensions.AI.OpenAI` (also
10.5.1, also pinned) may already provide an `IRealtimeClient`
implementation that the AF provider package could compose on rather
than re-implementing the wire mapping. Worth at least confirming that
and either layering on top or documenting why we are going one level
deeper.

### 4. Hosting package naming silently flips the convention

`realtime-hosting.md` §5.2 and `hosting.md` §3.2 use
`Microsoft.Agents.AI.Foundry.Hosting.Realtime` (Foundry-tier, parallels
existing `Microsoft.Agents.AI.Foundry.Hosting`). The plan §4.2 uses
`Microsoft.Agents.AI.Realtime.Foundry.Hosting` (Realtime-tier). Both
are defensible, but the divergence is silent. One short sentence
picking a winner avoids a later rename.

### 5. `Foundry.Hosting.Common` refactor is referenced but not committed to

§4.2 says the new Foundry hosting "reuse[s] the shared isolation-key /
identity primitives from §4.1 via composition." But §4.1 only
introduces a *new* `HostedRealtimeSessionContext`; it does not lift
`HostedSessionIsolationKeyProvider` / `HostedSessionContext` /
`HostedAgentUserAgentPolicy` out of the existing
`Microsoft.Agents.AI.Foundry.Hosting` (which is what
`realtime-hosting.md` §5.1 calls for as the only required refactor of
existing code). So as written, §4.2 will either duplicate those types
or take an unstated dep on the existing
`Microsoft.Agents.AI.Foundry.Hosting` package. Worth a one-liner:
either "duplicate for the proto, defer the `.Common` extraction" or
"do the extraction first."

### 6. Invocations transport is fused into the Foundry hosting package

`realtime-hosting.md` §3.3 keeps `InvocationsRealtimeAgentTransportHandler`
+ `NeutralRealtimeInvocationsEventEncoder` +
`VoiceLiveInvocationsEventEncoder` in a transport-tier
`Microsoft.Agents.AI.Realtime.Hosting.Invocations` (reusable by
non-Foundry hosts). The plan §4.2 puts them all in
`Microsoft.Agents.AI.Realtime.Foundry.Hosting`. Fine for the proto,
but it means a later split is required — flag it as a known follow-up
the way §3.4 already does for OpenAI/Foundry client-side duplication.

---

## Smaller items worth fixing in place

- §3.2 lists `OpenTelemetryRealtimeAgent` as a stub but has no test
  entry; the dotnet `OpenTelemetryAgent` pattern always ships with
  `*BuilderExtensions.cs` + a tests file (per
  `OpenTelemetryAgentTests.cs`). Same for
  `InMemoryRealtimeHistoryProvider` — no tests listed despite
  `InMemoryChatHistoryProviderTests.cs` being the obvious template.
  The detailed test plan (`realtime-agent-test.md` §2) already
  enumerates these; the implementation plan should either point at
  that list or copy it.
- §3.1's `RealtimeSessionStateBag` test entry is correct, but the plan
  does not say where it lives. The existing `AgentSessionStateBag`
  lives in `Microsoft.Agents.AI.Abstractions`; the plan should be
  explicit that it is reused (not re-invented) — there is no need for
  a `RealtimeSessionStateBag` type at all if `RealtimeSession` inherits
  the `AgentSession` `StateBag` pattern (which `realtime-agent.md` §1.2
  says it does).
- §3.3 mentions Foundry-Voice-Live-specific `azure_semantic_vad` as
  `RawProviderEvent` only — consistent with `normalized-events.md` §4.
  Good.
- §6 ADR list is short; missing two that `realtime-agent.md` §4 and
  `realtime-hosting.md` §6 flag as needing a decision before code lands:
  (a) tool-invocation default (opt-in via `UseFunctionInvocation` vs
  auto), and (b) authoritative `History` ownership for the hosted path.
  Add or explicitly defer.

---

## Summary

The plan's structure, sequencing, package layering, and DI/test
conventions match the rest of `/proto` and the existing `dotnet/src`
patterns. The substantive issue is that **§3.1 reuses the original
(pre-M.E.AI-alignment) `realtime-agent.md` §1 type list, while the rest
of the proto folder (`normalized-events.md`, `misc-notes.md`,
`session.md` §4.1) has moved on to "build on top of M.E.AI's
`IRealtimeClient` / `RealtimeClientMessage` / `RealtimeServerMessage`
and add only what's genuinely missing."** That conflict — plus the
silent flip in hosting package naming and the under-specified
`Foundry.Hosting.Common` story — are the things to resolve before §3.1
ships. Everything else is bikeshed-grade.
