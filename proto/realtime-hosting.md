# RealtimeAgent — Hosting-side Layer Outline

This document outlines the **types and packages** required to host a
`RealtimeAgent` (as defined in [`realtime-agent.md`](../realtime-agent.md))
behind a network-facing protocol, mirroring the layering already established
for `AIAgent` hosting:

- `Microsoft.Agents.AI.Hosting` — transport-neutral hosting primitives.
- `Microsoft.Agents.AI.Hosting.OpenAI` — protocol bridge for the OpenAI
  Responses / ChatCompletions / Conversations wire shapes.
- `Microsoft.Agents.AI.Foundry.Hosting` — Foundry-specific hosted-agent
  runtime, bridging to `Azure.AI.AgentServer.Responses`.

See [`hosting.md`](./hosting.md) for the contrast between that mature stack
and the raw Invocations-protocol wrapper used by the Python VoiceLive sample,
and for the `Hosting.Invocations` + `Foundry.Hosting.Realtime` package shape
this document expands on.

The core difference vs `AIAgent` hosting:

| `AIAgent` hosting (request/response)                         | `RealtimeAgent` hosting (bidirectional, duplex)                                  |
| ------------------------------------------------------------ | -------------------------------------------------------------------------------- |
| One HTTP request → one (possibly streamed) response.         | One client connection → long-lived duplex channel; both sides push events.       |
| Per-request executor invocation; no persistent transport.    | Persistent transport (WebSocket/WebRTC/HTTP/2 stream); executor owns its lifetime. |
| Session state is round-tripped through `AgentSessionStore`.  | Session state lives in the active `RealtimeSession` for the life of the connection; `AgentSessionStore` handles only cross-connection logical continuity. |
| Tool calls are part of the request/response body.            | Tool calls race with ongoing audio; cancellation and barge-in matter.            |
| One inbound shape (JSON request body).                       | Many inbound shapes: text, audio frames, control events, tool results.          |

The package shape intentionally parallels `Microsoft.Agents.AI.Hosting.*` so
the same patterns (DI registration, name-keyed agent resolution, builder
endpoints, ASP.NET integration, `AgentSessionStore`, isolation/identity glue)
carry over.

---

## 1. `Microsoft.Agents.AI.Realtime.Hosting.Abstractions`

Equivalent of the abstraction-only slice of `Microsoft.Agents.AI.Hosting`.
Pure abstractions — no ASP.NET deps, no transport deps, no provider deps.
Depends on `Microsoft.Agents.AI.Realtime.Abstractions` and on the existing
`Microsoft.Agents.AI.Hosting` `AgentSessionStore` / `IHostedAgentBuilder`
contracts.

> **Why split this out?** `Microsoft.Agents.AI.Hosting` today does not have a
> separate `.Abstractions` assembly — its abstraction surface (`IHostedAgentBuilder`,
> `AgentSessionStore`) already lives there alongside concrete builders and
> extension methods. We keep the same shape for realtime: a single
> `Microsoft.Agents.AI.Realtime.Hosting` package. This §1/§2 split is purely
> editorial in this document; in code there's one assembly. The split exists
> in `realtime-agent.md` because the *client*-side surface ships pure
> abstractions independently of any concrete agent implementation.

(Folded into §2 in actual delivery; described separately here only to mirror
the `realtime-agent.md` outline shape.)

---

## 2. `Microsoft.Agents.AI.Realtime.Hosting`

Equivalent of `Microsoft.Agents.AI.Hosting`. Transport-neutral hosting
primitives for `RealtimeAgent`. Depends on `Microsoft.Agents.AI.Realtime` +
`Microsoft.Agents.AI.Hosting` + `Microsoft.Extensions.Hosting.Abstractions`
+ `Microsoft.Extensions.DependencyInjection.Abstractions`.

### 2.1 Builder + DI registration

Mirrors `HostApplicationBuilderAgentExtensions` / `IHostedAgentBuilder`:

- **`IHostedRealtimeAgentBuilder`** — sibling of `IHostedAgentBuilder`. Has
  `string Name`, `IServiceCollection ServiceCollection`, `ServiceLifetime Lifetime`.
- **`HostedRealtimeAgentBuilder`** — concrete implementation, mirrors
  `HostedAgentBuilder`.
- **`HostApplicationBuilderRealtimeExtensions`** — extension methods on
  `IHostApplicationBuilder`:
  - `AddRealtimeAgent(name, configureOptions, lifetime)` — register by
    options + factory.
  - `AddRealtimeAgent(name, Func<IServiceProvider, string, RealtimeAgent>, lifetime)`
    — custom factory delegate.
- **`ServiceCollectionRealtimeExtensions`** — same overloads on
  `IServiceCollection` for non-host scenarios.

`RealtimeAgent` instances are registered as keyed services by name, exactly
the way `AIAgent` is today. Resolution from a hosting handler reads the agent
name from the protocol-specific request envelope (Invocations
`agent.name` / metadata `entity_id`, etc.).

### 2.2 Hosted wrapper

Mirrors `AIHostAgent`:

- **`HostedRealtimeAgent : DelegatingRealtimeAgent`** — wraps an inner
  `RealtimeAgent` and adds:
  - `RealtimeSessionStore SessionStore { get; }` — cross-connection logical
    session continuity (history / state-bag round-trip), not the live socket.
  - `GetOrCreateSessionAsync(conversationId, ct)` — looks up persisted state,
    constructs a *disconnected* `RealtimeSession` (via
    `RealtimeAgent.CreateSessionAsync`), then `ConnectAsync` is called by the
    transport handler.
  - `SaveSessionAsync(conversationId, session, ct)` — persists the
    serializable slice (`StateBag`, `History`, instructions/tools snapshot)
    after the live connection closes.
  - **Lifetime caveat:** unlike `AIHostAgent`, persistence happens at
    *connection close*, not after each turn. The handler decides the cadence
    (per-turn vs end-of-connection vs periodic), driven by the underlying
    transport's natural checkpointing points.

### 2.3 Session persistence

Mirrors `AgentSessionStore` / `Noop` / `InMemory`:

- **`RealtimeSessionStore`** — abstract; sibling of `AgentSessionStore`. Same
  three primitives: `GetSessionAsync`, `SaveSessionAsync`, `DeleteSessionAsync`.
  Operates on the serializable slice of `RealtimeSession`, not the live one.
- **`NoopRealtimeSessionStore`** — default; no persistence, every connection
  starts fresh.
- **`InMemoryRealtimeSessionStore`** — process-local; mirrors
  `InMemoryAgentSessionStore`.
- **`FileSystemRealtimeSessionStore`** — disk-backed, `JsonSerializable`-driven;
  parallels the Foundry `FileSystemAgentSessionStore` but lives here so
  non-Foundry hosts can use it.
- **`HostedRealtimeSessionContext`** — request-scoped identity / isolation
  wrapper. Same idea as `HostedSessionContext` in `Foundry.Hosting`; lifted
  into hosting common so both `Foundry.Hosting` and `Foundry.Hosting.Realtime`
  consume one definition.

### 2.4 Transport-neutral handler primitives

These are the hosting-layer parallels of `IResponseExecutor` /
`HostedAgentResponseExecutor` — but adapted for duplex transports:

- **`IRealtimeAgentTransport`** — abstract per-connection transport. Methods:
  - `ValueTask AcceptAsync(IRealtimeAgentTransportContext, CancellationToken)`
    — invoked by the ASP.NET endpoint when a new connection is accepted;
    returns when the connection terminates.
  - The implementation is responsible for: reading inbound frames from the
    wire, translating them to `RealtimeClientEvent`s, calling
    `RealtimeSession.SendAsync`, reading the inbound `IAsyncEnumerable<RealtimeSessionUpdate>`,
    and writing each update out to the wire in the protocol's encoding.
- **`IRealtimeAgentTransportContext`** — surfaces:
  - The resolved `RealtimeAgent` (from name-keyed DI).
  - The resolved `RealtimeSessionStore` and `HostedRealtimeSessionContext`.
  - Authentication principal, request headers, query string.
  - The underlying duplex stream (`WebSocket`, `IDuplexPipe`, etc.) — typed
    via a transport-specific adapter so the handler stays transport-neutral.
  - A `CancellationToken` linked to connection-close.
- **`IRealtimeEventEncoder`** — pluggable, plays the same role
  `IInvocationEventEncoder` plays for the Invocations transport: maps
  between provider-neutral `RealtimeSessionUpdate` / `RealtimeClientEvent`
  and the wire vocabulary (raw OpenAI Realtime events, Foundry VoiceLive
  shape, Gemini bidi, custom protocol). Default encoders ship in transport
  packages (§3) and protocol packages (§4).
- **`RealtimeAgentTransportHandler`** — base class that wires the three above
  together: resolves agent + store, opens session, runs both directions of
  the pump, persists on close. Subclasses fill in the transport-specific
  framing (`WebSocketRealtimeAgentTransportHandler`, `WebRtcRealtimeAgentTransportHandler`,
  `InvocationsRealtimeAgentTransportHandler`).

### 2.5 Diagnostics & telemetry

Mirrors the existing logging/OTel patterns:

- **`RealtimeHostingTelemetry`** — `ActivitySource` and `Meter` names used by
  every hosting handler.
- **`RealtimeHostingLogMessages`** — `LoggerMessage` strings for connect,
  send, receive, error, close.
- **`RealtimeHostingDiagnosticIds`** — `[Experimental]` ids for the new
  surface.

### 2.6 Abstractions reused as-is from `Microsoft.Agents.AI.Hosting`

These do not need a realtime-specific equivalent:

- `IHostedAgentBuilder` *kind* — we add a sibling, not a replacement, because
  `AIAgent` and `RealtimeAgent` are siblings.
- The DI keying convention (keyed by agent name) carries over verbatim.
- `WorkflowCatalog` — out of scope. Workflow integration for realtime agents
  is a separate question (TBD).

---

## 3. Transport packages

Each package implements `IRealtimeAgentTransport` for one wire transport.
They are protocol-neutral within a transport family — i.e. the WebSocket
package can serve OpenAI Realtime *or* Foundry VoiceLive *or* a custom
encoder, depending on which `IRealtimeEventEncoder` is registered.

### 3.1 `Microsoft.Agents.AI.Realtime.Hosting.WebSockets`

ASP.NET-Core-integrated WebSocket transport. Depends on
`Microsoft.AspNetCore.WebSockets`.

Types:

- **`WebSocketRealtimeAgentTransportHandler : RealtimeAgentTransportHandler`**
  — drives a `System.Net.WebSockets.WebSocket`. Reads/writes binary or text
  frames according to the encoder.
- **`MapRealtimeWebSocketEndpoint(this IEndpointRouteBuilder, IHostedRealtimeAgentBuilder, path)`**
  — endpoint route builder extension; mirrors `MapOpenAIResponses`.
- **`WebSocketRealtimeOptions`** — keepalive interval, max message size,
  subprotocol negotiation, close timeout.

### 3.2 `Microsoft.Agents.AI.Realtime.Hosting.WebRTC`

WebRTC transport. Depends on a WebRTC stack (likely SIPSorcery initially;
abstract enough that LiveKit/MediaSoup integrations can plug in later).

Types:

- **`WebRtcRealtimeAgentTransportHandler`** — owns the SDP offer/answer
  exchange + DTLS handshake; surfaces audio tracks as
  `OutputAudioDeltaUpdate` and `InputAudioBufferAppendEvent`.
- **`WebRtcSignalingEndpoint`** — HTTP endpoint that brokers offer/answer.
- **`IEphemeralTokenProvider`** — lifted from
  `Microsoft.Agents.AI.Realtime.OpenAI` so other transports can mint tokens
  for browser clients.

Implementation notes:

- Audio frames need transcoding (Opus ↔ PCM16) at the transport boundary —
  most realtime models speak PCM but WebRTC speaks Opus. Encoder is in
  the transport, not the agent.
- Out of scope for v1; document the slot now so we don't accidentally model
  it incompatibly in `IRealtimeAgentTransport`.

### 3.3 `Microsoft.Agents.AI.Realtime.Hosting.Invocations`

The Invocations-protocol transport from
[`hosting.md`](./hosting.md). Reused here because the VoiceLive sample
demonstrates that **Invocations is a viable hosting surface for realtime
agents**, even though it is request/response on the wire. It works because
VoiceLive on the *other* side handles the duplex audio rail; the agent only
needs to stream text/transcript events back through SSE.

Types:

- **`InvocationsRealtimeAgentTransportHandler : RealtimeAgentTransportHandler`**
  — implements `IRealtimeAgentTransport` over `POST /invocations` + SSE.
  Internally:
  - Each POST opens a transient `RealtimeSession`.
  - Request body is converted to `RealtimeClientEvent`s and sent.
  - `ReceiveUpdatesAsync` is consumed; updates are passed to the encoder and
    written out as SSE events until `ResponseCompletedUpdate` (or the
    connection closes).
  - On stream end, `RealtimeSessionStore.SaveSessionAsync` is called keyed by
    `agent_session_id`.
- **`MapRealtimeInvocations(this IEndpointRouteBuilder, IHostedRealtimeAgentBuilder, path)`**
  — same pattern as `MapInvocations` in §3.1 of [`hosting.md`](./hosting.md),
  with a realtime-aware default encoder.
- **Built-in encoders:**
  - **`NeutralRealtimeInvocationsEventEncoder`** — emits `text.delta`,
    `text.done`, `done` shapes; useful for non-VoiceLive consumers.
  - **`VoiceLiveInvocationsEventEncoder`** — emits the
    `output_audio_transcription.delta` / `.done` / `done` vocabulary the
    VoiceLive sample hand-codes. Lives here (not in `Foundry.Hosting.Realtime`)
    because VoiceLive compatibility is a wire-format concern, not a
    Foundry-deployment concern.

---

## 4. Protocol-vocabulary packages

These packages provide `IRealtimeEventEncoder` implementations for specific
provider event taxonomies. Optional; they exist when consumers want their
hosted agent to *speak* a particular provider's wire format directly (so a
client written against e.g. the OpenAI Realtime SDK can talk to an AF-hosted
realtime agent unchanged).

- **`Microsoft.Agents.AI.Realtime.Hosting.OpenAI`** — encoder/decoder for the
  OpenAI Realtime event taxonomy (`session.update`, `input_audio_buffer.*`,
  `response.*`, etc.). Reuses the wire-format types from
  `Microsoft.Agents.AI.Realtime.OpenAI` via shared internals.
- **`Microsoft.Agents.AI.Realtime.Hosting.Gemini`** — encoder/decoder for
  `BidiGenerateContent*` messages.
- **`Microsoft.Agents.AI.Realtime.Hosting.AzureVoiceLive`** — strict encoder
  for the Foundry Voice Live event taxonomy (a superset of OpenAI Realtime
  with Azure-specific extensions: `azure_semantic_vad`, HD voices, etc.).
  This is the "speaks VoiceLive natively over WebSocket" option, distinct
  from the SSE/Invocations VoiceLive shape in §3.3.

Each protocol package combines with any transport package (§3) — e.g.
WebSocket + OpenAI encoder, Invocations + VoiceLive-SSE encoder, WebSocket +
VoiceLive encoder.

---

## 5. Foundry-hosted layer

### 5.1 `Microsoft.Agents.AI.Foundry.Hosting.Common` *(refactor)*

Lifted out of the existing `Microsoft.Agents.AI.Foundry.Hosting` so it can be
shared by both the Responses path and the Realtime path. Contents:

- `HostedSessionIsolationKeyProvider`, `PlatformHostedSessionIsolationKeyProvider`
- `HostedSessionContext`, `HostedSessionContextExtensions`
- `HostedAgentUserAgentPolicy`
- `FileSystemAgentSessionStore` (today) → may be promoted alongside
  `FileSystemRealtimeSessionStore` from §2.3
- `ApplyOpenTelemetry` helpers
- `FOUNDRY_PROJECT_ENDPOINT` / `FOUNDRY_AGENT_TOOLSET_ENDPOINT` env conventions

This is the only refactor of existing code required by the realtime hosting
work — it falls out naturally because the same isolation/identity/telemetry
glue is needed by both protocols.

### 5.2 `Microsoft.Agents.AI.Foundry.Hosting.Realtime`

The Foundry-tier package — the realtime sibling of `Foundry.Hosting`.
Depends on `Microsoft.Agents.AI.Realtime.Hosting`,
`Microsoft.Agents.AI.Realtime.Hosting.Invocations` (default transport per
the VoiceLive sample), and `Microsoft.Agents.AI.Foundry.Hosting.Common`.

Public surface:

- **`AddFoundryRealtime(this IServiceCollection)`** — multiplexed registration
  by agent name (mirrors `AddFoundryResponses()` no-arg overload).
- **`AddFoundryRealtime(this IServiceCollection, RealtimeAgent agent, RealtimeSessionStore? store = null)`**
  — single-agent shorthand.
- **`MapFoundryRealtime(this IEndpointRouteBuilder, string prefix = "")`** —
  mounts the Invocations endpoint with the `VoiceLiveInvocationsEventEncoder`
  pre-wired.

Internals:

- **`FoundryRealtimeInvocationsHandler`** — sibling of
  `AgentFrameworkResponseHandler`. Resolves a `RealtimeAgent` (by
  `agent.name` / `metadata["entity_id"]`), applies `HostedSessionIsolationKeyProvider`
  validation, applies `ApplyOpenTelemetry` and `TryApplyUserAgent`, then
  delegates to the transport handler.
- **Per-request executor strategies** (sibling of the
  `IResponseExecutor` / `AIAgentResponseExecutor` split):
  - `RealtimeAgentInvocationExecutor` — true realtime: opens a
    `RealtimeSession`, pumps audio + text both ways. Used when the underlying
    `RealtimeAgent` is provider-realtime (Foundry Voice Live, OpenAI
    Realtime, Gemini Live).
  - `AIAgentInvocationExecutor` — the "VoiceLive-over-text-agent" case from
    the Python sample. Wraps an `AIAgent` (not a `RealtimeAgent`) and
    synthesizes `output_audio_transcription.*` events from text deltas.
    Lets a plain text agent appear to VoiceLive as a realtime agent because
    VoiceLive itself does the TTS/STT.

  Both executors ride through the same `IRealtimeAgentTransport` +
  `IRealtimeEventEncoder` plumbing — the choice is a DI registration
  decision, not a code-path divergence.

- **`FoundryToolboxService` does not get pulled forward** into the Realtime
  path. Toolbox / MCP injection for a `RealtimeAgent` happens at
  *agent construction time* (because the realtime socket is the tool surface
  for the model), not per-request. Stays in `Foundry.Hosting`.

- **`-32006` MCP consent flow** likewise stays in `Foundry.Hosting` — it is
  Responses-pipeline-specific.

### 5.3 What the Python VoiceLive sample maps to

Concrete mapping, for sanity-checking the abstraction:

| Sample concern                                       | .NET hosting equivalent                                                |
| ---------------------------------------------------- | ---------------------------------------------------------------------- |
| `InvocationAgentServerHost()`                        | `app.MapFoundryRealtime()` (Foundry-tier) or `app.MapRealtimeInvocations(builder)` (transport-tier) |
| `@app.invoke_handler` body                           | The user does not write this — `FoundryRealtimeInvocationsHandler` + an executor handles it. The user supplies a `RealtimeAgent` (or an `AIAgent` for option B). |
| `request.state.session_id` / `invocation_id`         | `IRealtimeAgentTransportContext.RequestHeaders` + `HostedRealtimeSessionContext` |
| `history = []` (in-process list)                     | `RealtimeSessionStore` (`InMemory` / `FileSystem` / pluggable)         |
| Manual `output_audio_transcription.delta` SSE events | `VoiceLiveInvocationsEventEncoder`                                     |
| Manual `_stream_reply` (Responses API + thread bridge)| `RealtimeAgent.ConnectAsync` + `ReceiveUpdatesAsync` (option A) *or* `AIAgent.RunStreamingAsync` adapter (option B) |
| `agent.manifest.yaml` `voiceLiveCompatible: "true"`  | `AddFoundryRealtime` registration implies VoiceLive encoder            |
| OpenTelemetry / health / OpenAPI doc serving         | Lifted from `Foundry.Hosting.Common` + ASP.NET defaults                |

---

## 6. Cross-cutting decisions to lock down before coding

Open questions, to be settled in an ADR alongside the first PR:

1. **One transport-neutral handler base, or per-transport handlers?**
   `RealtimeAgentTransportHandler` is proposed as a small abstract base; the
   alternative is to ship transport-specific handlers with no shared base
   and accept some duplication. Decision needed once the second transport
   (WebRTC or Invocations) is being implemented in earnest.
2. **Encoder ↔ transport binding.** Today's proposal keeps them orthogonal
   (any encoder × any transport). Some combinations don't make sense (OpenAI
   Realtime encoder over Invocations SSE). Should we model compatibility
   explicitly via marker interfaces, or document allowed pairings?
3. **Authoritative `History`.** Must the hosted-side `RealtimeSession.History`
   exactly mirror what each connected client sees? Probably yes for replay
   on reconnect; the encoder is responsible for projecting to whatever the
   wire protocol's history view looks like.
4. **Connection-close persistence cadence.** Save-on-close is simple but loses
   data on crash. Should `HostedRealtimeAgent` expose a periodic checkpoint
   hook? Proposal: yes, optional; default off; configured per-store.
5. **Multi-tenant isolation for raw realtime transports.** `Foundry.Hosting`
   gets `x-agent-user-isolation-key` / `x-agent-chat-isolation-key` from the
   Foundry platform. For non-Foundry hosts using
   `Microsoft.Agents.AI.Realtime.Hosting.WebSockets` directly, the equivalent
   must come from the auth layer — define a pluggable
   `IRealtimeIsolationKeyProvider` with sensible defaults (claims-based).
6. **WebRTC scope for v1.** Defer? Proposal: design the slot; do not ship the
   package. The Invocations + WebSocket pair is sufficient for the VoiceLive
   sample and for OpenAI Realtime parity.
7. **DevUI / Aspire integration.** `Aspire.Hosting.AgentFramework.DevUI`
   already exists for `AIAgent`. Decide whether to extend it or ship a new
   `Aspire.Hosting.AgentFramework.RealtimeDevUI`.

---

## 7. Suggested package dependency graph

```
Microsoft.Extensions.AI.Abstractions
        ▲
        │
Microsoft.Agents.AI.Realtime.Abstractions          ◀── (from realtime-agent.md §1)
        ▲
        │
Microsoft.Agents.AI.Realtime                       ◀── (from realtime-agent.md §2)
        ▲
        │
Microsoft.Agents.AI.Realtime.Hosting               ◀── this doc §2
        ▲
        ├── Microsoft.Agents.AI.Realtime.Hosting.WebSockets        (§3.1)
        ├── Microsoft.Agents.AI.Realtime.Hosting.WebRTC            (§3.2, deferred)
        ├── Microsoft.Agents.AI.Realtime.Hosting.Invocations       (§3.3)
        │
        ├── Microsoft.Agents.AI.Realtime.Hosting.OpenAI            (§4)
        ├── Microsoft.Agents.AI.Realtime.Hosting.Gemini            (§4)
        └── Microsoft.Agents.AI.Realtime.Hosting.AzureVoiceLive    (§4)

Microsoft.Agents.AI.Foundry.Hosting.Common          (§5.1, refactor)
        ▲
        ├── Microsoft.Agents.AI.Foundry.Hosting              (existing — Responses path)
        └── Microsoft.Agents.AI.Foundry.Hosting.Realtime     (§5.2)
              └─ depends on Microsoft.Agents.AI.Realtime.Hosting
              └─ depends on Microsoft.Agents.AI.Realtime.Hosting.Invocations
```

`Microsoft.Agents.AI.Hosting` and `Microsoft.Agents.AI.Realtime.Hosting` are
**siblings** — neither references the other. Bridging happens at the agent
layer (via `RealtimeAgentAsAIAgent` / `AIAgentAsRealtimeAgent` from
`Microsoft.Agents.AI.Realtime`), so a deployment that wants to expose the
same logical agent over both Responses and Invocations can do so by
registering one underlying agent and mapping both endpoints.
