# Review v2 — `proto/implementation-plan.md`

Follow-up to `implementation-plan-review.md`, taking into account:

- The plan revision that incorporates the v1 review.
- The clarification that this is a **prototype**: refactors of existing
  packages should be noted as follow-ups, not performed. Where the
  proto needs primitives from existing `Microsoft.Agents.AI.*` packages,
  take a package/project reference and — when the needed primitive is
  `internal` — extend it via `[InternalsVisibleTo]` rather than
  reimplementing it in the proto solution.

---

## What v2 resolved cleanly

The new §3 ("Type-surface decisions resolved up-front") closes every
substantive item the v1 review flagged:

| v1 issue                                                  | v2 resolution                                                |
| --------------------------------------------------------- | ------------------------------------------------------------ |
| §1 — parallel event taxonomy vs M.E.AI                    | §3.1: M.E.AI is the public surface; no parallel hierarchy. |
| §2 — `RealtimeSession` shape vs `session.md` §4.1         | §3.2: already-connected; wraps `IRealtimeClientSession`; no Serialize/Deserialize; reuses `AgentSessionStateBag`. |
| §3 — `IWebSocketTransport` invented without checking M.E.AI | §3.3: providers compose `IRealtimeClient`; `IWebSocketTransport` is internal-only and only the fallback. |
| §4 — hosting package naming                               | §3.5: picks `Microsoft.Agents.AI.Foundry.Hosting.Realtime`. |
| §5 — `Foundry.Hosting.Common` refactor under-specified    | §3.6: explicit no-refactor; proto-local minimal versions; extraction tracked as follow-up. (See §A below for a tighter alternative.) |
| §6 — Invocations transport fused into Foundry             | §3.7: explicitly acknowledged; split flagged as follow-up. |
| §S1 (smaller) — OTel/InMemoryHistory tests                | §4.2 + §4.6 add them.                                        |
| §S2 (smaller) — `RealtimeSessionStateBag` reuse           | §3.2 explicit: no new state-bag type.                        |
| §S4 (smaller) — missing ADRs                              | §3.8 captures ADR-003 (tool-invocation default) and ADR-004 (history ownership). |

The §3.4 simplification (no persistence store this phase; reduce
`RealtimeSessionStore` to a `RegisterAsync` / `LookupAsync` registry)
is a good prototype-scope call — it falls out naturally from §3.2's
"no Serialize/Deserialize."

---

## A. Prototype scoping: prefer references + `[InternalsVisibleTo]` over reimplementation

§3.6 currently says, of the new `Microsoft.Agents.AI.Foundry.Hosting.Realtime`:

> Where it needs primitives such as `HostedSessionIsolationKeyProvider`,
> it implements minimal proto-local versions and tracks the extraction
> as a follow-up.

This is more work — and more divergence risk — than necessary for a
prototype. A spot-check of the existing
`dotnet/src/Microsoft.Agents.AI.Foundry.Hosting` shows that **most of
what `realtime-hosting.md` §5.1 calls out as the "Common" extraction
candidates are already `public`**:

| Type                                       | Visibility today | Source                                                    |
| ------------------------------------------ | ---------------- | --------------------------------------------------------- |
| `HostedSessionContext`                     | `public sealed`  | `HostedSessionContext.cs`                                 |
| `HostedSessionContextExtensions`           | `public static`  | `HostedSessionContextExtensions.cs`                       |
| `HostedSessionIsolationKeyProvider`        | `public abstract`| `HostedSessionIsolationKeyProvider.cs`                    |
| `FileSystemAgentSessionStore`              | `public sealed`  | `FileSystemAgentSessionStore.cs`                          |
| `InMemoryAgentSessionStore`                | `public sealed`  | `InMemoryAgentSessionStore.cs`                            |
| `AgentSessionStore` (Foundry-flavor base)  | `public abstract`| `AgentSessionStore.cs`                                    |
| `HostedAgentUserAgentPolicy`               | `internal sealed`| `HostedAgentUserAgentPolicy.cs`                           |
| `PlatformHostedSessionIsolationKeyProvider`| `internal sealed`| `PlatformHostedSessionIsolationKeyProvider.cs`            |
| `HostedSessionJsonUtilities`               | `internal static`| `HostedSessionJsonUtilities.cs`                           |

Likewise in `Microsoft.Agents.AI.Hosting`: `AIHostAgent`,
`AgentSessionStore`, `IHostedAgentBuilder`, `NoopAgentSessionStore`,
`InMemoryAgentSessionStore`, and the `HostApplicationBuilder…`
extensions are all `public`. Only `HostedAgentBuilder` /
`HostedWorkflowBuilder` are `internal`.

**Suggested rewording of §3.6**:

> `Microsoft.Agents.AI.Foundry.Hosting.Realtime` takes a project (or
> package) reference to the existing
> `Microsoft.Agents.AI.Foundry.Hosting` and reuses its public types
> directly (`HostedSessionContext`, `HostedSessionContextExtensions`,
> `HostedSessionIsolationKeyProvider`, `AgentSessionStore`, etc.). For
> the small set of internals the proto needs
> (`HostedAgentUserAgentPolicy`,
> `PlatformHostedSessionIsolationKeyProvider`,
> `HostedSessionJsonUtilities`), add a one-line
> `[assembly: InternalsVisibleTo("Microsoft.Agents.AI.Foundry.Hosting.Realtime")]`
> to the existing package — that is the smallest possible touch and
> avoids the "minimal proto-local versions" duplication. The `.Common`
> extraction stays a tracked follow-up per `realtime-hosting.md` §5.1
> but is **not** performed in this phase.

Same pattern applies to anywhere else the proto reaches into existing
packages:

- **`Microsoft.Agents.AI.Realtime.Foundry` ↔ `Microsoft.Agents.AI.Foundry`**:
  if a future need pulls in something internal (e.g., shared
  user-agent / client-headers policy from `ClientHeadersPolicy.cs`),
  add `[InternalsVisibleTo]` rather than copy-pasting the policy.
- **`Microsoft.Agents.AI.Realtime.Hosting` ↔ `Microsoft.Agents.AI.Hosting`**:
  reuse `IHostedAgentBuilder` patterns; the existing `HostedAgentBuilder`
  internal concrete can be reached the same way if construction
  symmetry matters.
- **Tests across packages**: the proto's `TestSupport` project should
  use the same `[InternalsVisibleTo]` pattern the existing
  `*UnitTests` projects already do (see references in the existing
  `Microsoft.Agents.AI.Foundry.Hosting.UnitTests`).

This also nudges §3.4: the registry-shaped `RealtimeSessionStore` may
not even need to exist in the proto. If the only consumer is
`HostedRealtimeAgent` and the only impl is `Noop`, drop the
abstraction entirely until §6/cascade or persistence work resurrects
the need. A YAGNI'd hosting wrapper is fine.

---

## B. Small drift items introduced by v2

These are minor, but worth fixing while the plan is still in flight:

1. **§3.1 — `RealtimeAgentInterruptedEvent` projection mechanism is
   ambiguous.** v2 says it "is **not** an alternate base for
   `RealtimeServerMessage`; it is its own type carried alongside via
   `RawRepresentation` and surfaced as an inert
   `RealtimeServerMessageType.RawContentOnly` server message that the
   AF layer recognizes specifically." A consumer reading the
   `IAsyncEnumerable<RealtimeServerMessage>` needs a concrete way to
   pull it out. Either:
   - document an extension helper (e.g.,
     `bool TryGetInterruptedEvent(this RealtimeServerMessage, out RealtimeAgentInterruptedEvent)`), or
   - have the AF layer subclass `RealtimeServerMessage`
     (`InterruptedRealtimeServerMessage : RealtimeServerMessage`)
     and emit *that* instead of a marker-payload `RawContentOnly`.
   The latter is closer to how `normalized-events.md` §6 G1 sketches
   the event ("introduce a single high-level event on the
   `RealtimeAgent` surface"). Pick one explicitly.

2. **§2 solution layout still lists `Microsoft.Agents.AI.Foundry.Hosting.Realtime/`
   under the proto `src/`.** Consistent with §3.5; just confirm the
   `.UnitTests` sibling under `tests/` follows the same casing
   (`Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests/`). It
   does in the listing; good.

3. **§3.3 OpenAI fallback decision.** "First task: validate whether
   `Microsoft.Extensions.AI.OpenAI`'s `IRealtimeClient` covers the
   proto's needs." Add a one-liner about what "validate" means
   concretely (smoke test: open a session against the fake transport,
   send `session.update` + `response.create`, receive
   `response.done`) so the decision criterion isn't subjective.

4. **§3.4 + §5.1 — `RealtimeSessionStore` shape.** §3.4 reduces it to
   `RegisterAsync` / `LookupAsync`. §5.1 echoes that. If you keep it,
   document what `LookupAsync` returns when nothing was registered
   (null vs throw) — the v1 plan didn't have to answer this because
   the store actually persisted things.

5. **§4.1 — `History` ownership.** §3.2 says
   `History` is a "read-only projection over emitted
   `RealtimeConversationItem`s (client-tracked)." Worth being explicit
   about *where* the projection lives: is it on the
   `Microsoft.Agents.AI.Realtime` core (composing M.E.AI), or on
   `RealtimeSession` in `Abstractions`? Putting projection logic in
   `Abstractions` means it has to know about every
   `RealtimeServerMessage` subtype; pushing it into the core package
   keeps `Abstractions` thin. The decision is implied but not stated.

6. **§4.3 — Foundry "session-config flavor" in `FoundryRealtimeAgentOptions`.**
   Be explicit whether transcription-only vs conversation-mode sessions
   are in scope for the proto. `session.md` Conceptual mapping table
   lists Foundry as "single conversational session type; specialization
   via session config (e.g., transcription mode, BYOM agent)" — so
   most likely YAGNI for the proto, but worth a one-liner non-goal.

7. **§3.8 ADR list — missing one.** v1 review §S4 also flagged
   "authoritative `History` ownership for the **hosted** path."
   ADR-004 covers history ownership in general; consider noting
   explicitly that the hosted layer does **not** own history (the
   wrapper just composes — store removal in §3.4 already implies it,
   but the ADR text should say so).

---

## C. Things v2 does that are worth keeping as-is

- **Standalone proto solution** (`/proto/impl/realtime.slnx`) opening
  independently. Right call.
- **`[Experimental("MEAI-REALTIME-001")]`** on the entire public
  surface. Matches the existing `DiagnosticIds.Experiments.*`
  pattern.
- **Parallelizable provider work** (Foundry + OpenAI in §7 step 3).
- **Cascade explicitly unscheduled** with the forward-compat hooks
  noted (§4.1 surface chosen so `AppendInputTextAsync` /
  `CommitInputTextAsync` can be added additively).
- **Fake `IRealtimeClient` / `IRealtimeClientSession`** in
  `TestSupport` as the primary unit-test surface (§4.5). This is the
  right test seam now that M.E.AI is the public surface.

---

## Summary

v2 cleanly resolves every substantive item from v1. The remaining
items are tightening, not blockers:

- **Bigger:** rewrite §3.6 to use existing-package references +
  `[InternalsVisibleTo]` instead of "minimal proto-local versions" —
  the relevant types are already `public` or one attribute away. This
  is in the spirit of "prototype, defer refactors."
- **Smaller:** pin down the `RealtimeAgentInterruptedEvent` projection
  mechanism (§B1), spell out the `History` projection's home (§B5),
  and tidy up the `Register/Lookup` registry contract (§B4) or drop
  it entirely.

With those tweaks, the plan and the rest of `/proto` are in
agreement and the implementation can proceed.
