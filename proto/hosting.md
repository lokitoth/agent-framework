# Hosting Abstractions for Invocations + Foundry Realtime

Design notes contrasting the raw Invocations-protocol wrapper in
[`vl_sample/hello-world-invocations-voicelive`](./vl_sample/hello-world-invocations-voicelive)
with the more mature hosting abstractions in `dotnet/src`, and proposing the
set of abstractions we would need to (a) host a raw Invocations API endpoint
analogous to `Microsoft.Agents.AI.Hosting.OpenAI`, and (b) layer a
Foundry-specific "Realtime" hosting package on top (working name:
`Microsoft.Agents.AI.Foundry.Hosting.Realtime`).

This is exploratory; nothing here has been committed to as a delivery plan.

---

## 1. What the VoiceLive sample actually is

`hello-world-invocations-voicelive/main.py` is a **raw Invocations protocol
wrapper**:

- The protocol SDK (`azure-ai-agentserver-invocations`,
  `InvocationAgentServerHost`) owns:
  - the HTTP contract,
  - `POST /invocations`,
  - optional `GET /invocations/{id}` and `DELETE /invocations/{id}` for LRO,
  - `agent_session_id` / `invocation_id` header / query-string parsing,
  - OpenTelemetry instrumentation,
  - serving the OpenAPI spec at `GET /invocations/docs/openapi.json`.
- The author owns:
  - the handler body,
  - the model call,
  - history management (in-process `list`, lossy on restart),
  - **and the SSE event vocabulary** — note the manually-emitted
    `output_audio_transcription.delta` / `.done` / `done` events. These are
    *VoiceLive-compatible* event shapes the author hand-rolls because
    VoiceLive on the other side of the Invocations endpoint expects them.

So Invocations is the *transport contract* (single POST in, SSE stream out,
session id in the query string), and "speaks VoiceLive" is an *event-
vocabulary contract* layered on top of that transport.

The manifest (`agent.manifest.yaml`) declares both pieces explicitly:

```yaml
template:
  kind: hosted
  protocols:
    - protocol: invocations
      version: 1.0.0
metadata:
  voiceLiveCompatible: "true"
```

---

## 2. What the .NET tree already has

Three layers, increasing in opinionation:

| Layer | Package | What it gives you |
|---|---|---|
| **Core hosting** | `Microsoft.Agents.AI.Hosting` | `AddAIAgent(name, …)` → `IHostedAgentBuilder`; `AIHostAgent` (a `DelegatingAIAgent` that adds `AgentSessionStore` round-tripping); name-keyed DI registration; `AgentSessionStore` with `Noop` / `InMemory` impls. Transport-agnostic. |
| **Protocol bridge** | `Microsoft.Agents.AI.Hosting.OpenAI` | Implements OpenAI ChatCompletions / Responses / Conversations over ASP.NET routing. `MapOpenAIResponses(agentBuilder, path?)`, `IResponsesService` + `InMemoryResponsesService`, `IResponseExecutor` (`HostedAgentResponseExecutor` resolves by name from DI; `AIAgentResponseExecutor` binds a specific instance). Converts OpenAI request shapes ↔ `AIAgent.RunStreamingAsync`. |
| **Foundry hosted-agent runtime** | `Microsoft.Agents.AI.Foundry.Hosting` | Bridges to the **Azure AI AgentServer Responses SDK** (`Azure.AI.AgentServer.Responses`). `AddFoundryResponses` / `MapFoundryResponses` registers `AgentFrameworkResponseHandler : ResponseHandler` that the SDK invokes. Adds Foundry-only concerns: `HostedSessionIsolationKeyProvider` (cross-user isolation via `x-agent-user-isolation-key` / `x-agent-chat-isolation-key`), `FoundryToolboxService` (MCP proxies via `FOUNDRY_AGENT_TOOLSET_ENDPOINT`), `-32006` MCP consent flow, OpenTelemetry wrapping, hosted-agent User-Agent policy, `FileSystemAgentSessionStore` rooted at `/.checkpoints` in-container. |

**Key observation:** there is a clean three-layer story for the **Responses**
protocol family:

1. transport-neutral agent + session store,
2. OpenAI Responses HTTP shape (`Hosting.OpenAI`),
3. Foundry Responses hosting glue (`Foundry.Hosting`), which uses an upstream
   Azure SDK (`Azure.AI.AgentServer.Responses`) to handle the wire and only
   requires you to plug a `ResponseHandler`.

There is **no equivalent today for the Invocations protocol** in .NET, and
**no Realtime hosting** at all.

---

## 3. Proposed abstractions

### 3.1 `Microsoft.Agents.AI.Hosting.Invocations` — the OpenAI-tier analog

This is the layer that does **not** know about Foundry — equivalent to what
`Hosting.OpenAI` is to `Foundry.Hosting`. It should host the Invocations
protocol over ASP.NET regardless of whether the surrounding deployment is
Foundry, Aspire, plain Kestrel, or Azure Functions.

Symmetry with `Hosting.OpenAI`:

```csharp
builder.Services.AddInvocationsAgentServer();            // protocol-level services
app.MapInvocations(agentBuilder, path: "/invocations");  // single agent
app.MapInvocations();                                    // multiplexed default
```

Key abstractions to introduce:

| Type | Role | Mirrors |
|---|---|---|
| `IInvocationsService` | "given an `InvocationRequest`, produce `IAsyncEnumerable<InvocationStreamEvent>`" | `IResponsesService` |
| `IInvocationExecutor` | Per-agent execution strategy; default impl wraps an `AIAgent` and runs `RunStreamingAsync` | `IResponseExecutor` / `AIAgentResponseExecutor` |
| `AIAgentInvocationExecutor` / `HostedAgentInvocationExecutor` | Bind to a specific agent instance / resolve by name from keyed DI | `AIAgentResponseExecutor` / `HostedAgentResponseExecutor` |
| `InvocationRequestContext` | Parses `agent_session_id`, `invocation_id`, headers; surfaces them to the executor | the Python sample's `request.state.session_id` / `invocation_id` |
| `IInvocationStore` for LRO (`get_invocation`, `cancel_invocation`) | Stores in-progress / completed invocation state; default in-memory; pluggable | (new — Responses has an analog via response storage) |
| `InvocationsHttpHandler` | The ASP.NET handler implementing the three endpoints (`POST /invocations`, `GET /invocations/{id}`, `DELETE /invocations/{id}`) plus `GET /invocations/docs/openapi.json` | `ResponsesHttpHandler` |
| `InvocationsEventWriter` (SSE) | Writes `data: { … }\n\n` framed events; abstracts framing from event vocabulary | `SseJsonResult` |
| `IInvocationEventEncoder` | **The pluggable piece.** Maps `AgentRunResponseUpdate` → SSE event JSON. Default encoder = "neutral" (text deltas + done). VoiceLive-shape encoder = the one hand-written in the Python sample. | (new — no analog needed in Responses because the Responses wire shape *is* the contract) |

The `IInvocationEventEncoder` is the architecturally important addition.
Unlike Responses, Invocations does not standardize a single event vocabulary
on the wire — the protocol gives you the envelope (SSE over POST with
session/invocation correlation) and the consumer (VoiceLive, a custom CLI,
an evaluator harness, etc.) picks the event shape. So the AF layer must let
you pick or implement one.

**History.** Invocations does not provide server-side history (the sample
explicitly maintains its own). The natural answer is to reuse
`AgentSessionStore` + `AIHostAgent` — the same primitive `Hosting.OpenAI`
and `Foundry.Hosting` already lean on. The default executor would:

1. Look up `agent_session_id` in `AgentSessionStore` → `AgentSession`,
2. `agent.RunStreamingAsync(messages, session, …)`,
3. Save on completion.

That replaces the in-memory `history = []` in the Python sample with the
framework's session story.

### 3.2 `Microsoft.Agents.AI.Foundry.Hosting.Realtime` — the Foundry-tier wrapper

This is the layer that knows about Foundry. By analogy with
`Foundry.Hosting` (which uses `Azure.AI.AgentServer.Responses` + adds
Foundry-specific glue), `Foundry.Hosting.Realtime` would **use
`Hosting.Invocations` as its transport substrate** and add Foundry- and
Realtime-specific concerns on top:

```csharp
builder.Services.AddFoundryRealtime(realtimeAgent);   // RealtimeAgent, not AIAgent
app.MapFoundryRealtime();                             // mounts /invocations
```

The interesting design questions / required abstractions:

1. **What does the executor consume?** This is the Foundry-Realtime story's
   central question.
   - **Option A**: hosted thing is a `RealtimeAgent` (from
     [`proto/session.md`](./session.md)). The executor opens a
     `RealtimeSession` per invocation, feeds the request body into
     `SendAsync(...)`, and forwards `GetStreamingResponseAsync(...)` events
     through an encoder. This is the "true" realtime mapping.
   - **Option B**: hosted thing is just an `AIAgent`, and the executor emits
     VoiceLive-shaped SSE events from text deltas. **This is what the Python
     sample actually does.** Cheap; lets a text agent appear to VoiceLive as
     a realtime agent because VoiceLive on the *other* side is doing
     TTS/STT.
   - We probably need **both**, with the same encoder pluggability story.
     The sample is option B; we want option A available when an actual
     realtime-capable backend (Foundry Voice Live model, OpenAI Realtime,
     Gemini Live) is in play.
   - This argues for `IInvocationExecutor` having two ready-made
     implementations: `AIAgentInvocationExecutor` (the sample's case) and
     `RealtimeAgentInvocationExecutor` (true realtime).

2. **`IInvocationEventEncoder` — VoiceLive variant.** A
   `VoiceLiveInvocationEventEncoder` lives here and is the default when
   `AddFoundryRealtime` is used. It produces the
   `output_audio_transcription.delta` / `.done` / `done` shape the sample
   hand-codes — driven from either `AgentRunResponseUpdate` (option B) or
   the normalized `RealtimeServerMessage` stream from `IRealtimeClientSession`
   (option A, mapping `OutputAudioTranscriptionDelta` etc. defined in
   [`proto/normalized-events.md`](./normalized-events.md) §1 onto the wire
   shape).

3. **Foundry-specific concerns to reuse from `Foundry.Hosting`:**
   - `HostedSessionIsolationKeyProvider` + `HostedSessionContext` — these
     are protocol-agnostic (read headers, stamp + validate the session).
     They should be lifted into a shared `Foundry.Hosting.Common` (new) or
     `Foundry.Hosting` itself and consumed by both `Foundry.Hosting` and
     `Foundry.Hosting.Realtime`.
   - `HostedAgentUserAgentPolicy`, `ApplyOpenTelemetry`,
     `FileSystemAgentSessionStore` rooted at `/.checkpoints`.
   - `FoundryToolboxService` — applies only when the hosted thing is an
     `AIAgent` with a chat-tool surface. For a pure `RealtimeAgent` whose
     underlying transport is the provider's realtime socket, toolbox
     injection happens at *agent construction*, not per-request. So this
     stays in `Foundry.Hosting` (Responses path) and does not get pulled
     forward.

4. **Manifest-level signal.** `agent.manifest.yaml` has
   `protocols: [invocations]` and `metadata.voiceLiveCompatible: "true"`.
   The .NET host registration is the analog: `AddFoundryRealtime` implies
   VoiceLive event encoding and Invocations transport; a separate
   `AddFoundryInvocations` (option B without the Realtime opinion) gives
   you the bare Invocations transport with a neutral encoder, no VoiceLive
   presumption.

5. **Reconnect / resumption — explicitly out of scope at the hosting
   layer.** Per [`proto/session.md`](./session.md) §4.2, resumption is a
   `RealtimeSession` concern (Gemini handles, etc.), not an Invocations
   concern. Invocations is a synchronous request/response with SSE — each
   call is its own invocation. The `agent_session_id` provides logical
   conversation continuity across separate invocations, which is exactly
   what `AgentSessionStore` is for.

### 3.3 Suggested package layout

```
Microsoft.Agents.AI.Hosting.Invocations            (new, peer of Hosting.OpenAI)
    IInvocationsService / IInvocationExecutor / IInvocationEventEncoder
    InvocationsHttpHandler, default executors, neutral encoder
    EndpointRouteBuilderExtensions.MapInvocations(...)

Microsoft.Agents.AI.Foundry.Hosting.Common         (new — extracted from Foundry.Hosting)
    HostedSessionIsolationKeyProvider, HostedSessionContext,
    isolation key middleware, HostedAgentUserAgentPolicy,
    FileSystemAgentSessionStore, ApplyOpenTelemetry helpers

Microsoft.Agents.AI.Foundry.Hosting                (existing — Responses path; takes a dep on .Common)

Microsoft.Agents.AI.Foundry.Hosting.Realtime       (new — takes deps on .Invocations + .Common)
    VoiceLiveInvocationEventEncoder
    RealtimeAgentInvocationExecutor (consumes RealtimeAgent from proto/session.md)
    AddFoundryRealtime / MapFoundryRealtime
```

The `.Common` extraction is the only refactor of existing code; it falls out
naturally because the same isolation / identity / telemetry glue is needed
by both the Responses path and the Realtime/Invocations path.

---

## 4. Open questions worth deciding before any of this gets written

1. **Should Invocations be its own package or fold into `Hosting`?** I'd
   keep it separate. It mirrors the `Hosting.OpenAI` split and lets
   non-Foundry consumers adopt Invocations standalone.
2. **Is there a Foundry-provided upstream SDK** (analog to
   `Azure.AI.AgentServer.Responses`) we should bridge to instead of
   implementing the HTTP shape ourselves? The Python sample uses
   `azure-ai-agentserver-invocations`. If a .NET counterpart exists or is
   planned, `Foundry.Hosting.Realtime` should bridge to it the same way
   `Foundry.Hosting` bridges to `Azure.AI.AgentServer.Responses`, and the
   `Hosting.Invocations` package becomes a thinner abstraction (or
   unnecessary if the SDK is itself transport-only). Worth confirming.
3. **`IInvocationEventEncoder` extensibility scope.** Just VoiceLive, or do
   we also model the `evaluator-harness` / generic-text shapes as
   first-class? Probably define the interface + ship `Neutral` + `VoiceLive`
   and let users implement others.
4. **`RealtimeAgent` is still a proto.** `Foundry.Hosting.Realtime` is
   downstream of that work landing in `Microsoft.Agents.AI.Abstractions`
   (and `Microsoft.Extensions.AI` realtime types being available).
   Sequencing matters.
