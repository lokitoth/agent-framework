# RealtimeAudioPipe performance analysis

## Summary

The current `RealtimeAudioPipe` implementation is a bounded, single-producer/single-consumer `Channel<DataContent>` that decouples audio capture from realtime-session sends. It is a good minimal handoff primitive for already-framed audio chunks: it preserves ordering, applies back-pressure, avoids unbounded memory growth, and keeps the capture side independent from provider transport details.

The main performance cost is not the channel itself. The larger costs are chunk sizing, JSON/base64 serialization, WebSocket frame frequency, and the fact that every chunk is sent sequentially through `RealtimeSession.AppendInputAudioAsync`. The default capacity of 32 is safe as a guardrail, but its real latency and memory behavior depend entirely on chunk duration and byte size.

## Implementation being analyzed

Code path:

1. `RealtimeAudioPipe.WriteAsync(DataContent)` writes a `DataContent` reference into a bounded channel.
2. `RealtimeAudioPipe.PumpToAsync(RealtimeSession)` drains the channel with `ReadAllAsync`.
3. Each chunk is forwarded with `RealtimeSession.AppendInputAudioAsync`.
4. `AppendInputAudioAsync` wraps the chunk in `InputAudioBufferAppendRealtimeClientMessage` and calls `SendAsync`.
5. The Foundry prototype serializes the message to a JSON text frame before sending it over `IWebSocketTransport.SendTextAsync`. The OpenAI package delegates the transport to the underlying M.E.AI/OpenAI realtime client.

Important current-state caveats:

- The implementation does not frame a `Stream` or `PipeReader`; callers must provide already-framed `DataContent` chunks.
- The implementation does not resample, transcode, normalize, or validate audio format.
- The `RealtimeAudioWriter` type in this implementation is a lightweight input-side writer view over `RealtimeAudioPipe`, not an output-audio `Stream`/`PipeWriter` fan-out helper.

## Fast path costs

The pipe adds a small amount of overhead per chunk:

- One channel enqueue/dequeue.
- One `DataContent` object reference stored in the channel buffer.
- A `ValueTask` for writes, which can complete synchronously when the channel has room.
- One async send per chunk in the pump loop.

The pipe does not copy the audio bytes itself. If the producer constructs `DataContent` from a new byte array per frame, those arrays dominate allocation pressure. If the provider path serializes bytes to JSON, the send path also allocates a base64 representation and a JSON string or buffer for each frame.

The current JSON path is also reflection-based (`DefaultJsonTypeInfoResolver`), with source generation explicitly deferred. That is acceptable for a prototype, but it is not the lowest-overhead path for high-rate audio appends.

## Chunk size tradeoffs

Chunk duration is the most important tuning input.

| Chunk duration | Typical appends/sec | Benefits | Costs |
| --- | ---: | --- | --- |
| 10 ms | 100 | Lowest capture-to-send latency; fine-grained VAD/interruption behavior | Highest per-message overhead, more scheduler and serialization pressure |
| 20 ms | 50 | Common realtime-audio compromise; responsive without excessive frame rate | Still many JSON/WebSocket sends per second |
| 40 ms | 25 | Lower CPU and transport overhead | Adds latency and coarser VAD/interruption granularity |
| 100 ms | 10 | Efficient for non-interactive streaming | Noticeable latency; poor fit for low-latency voice interaction |

For interactive voice, 20 ms frames are usually the best starting point. Larger chunks reduce overhead, but they also delay server VAD, barge-in detection, and first-token response timing. Smaller chunks improve responsiveness, but they amplify fixed costs in JSON serialization, WebSocket framing, logging/telemetry, and async scheduling.

## Capacity tradeoffs

`RealtimeAudioPipe` capacity is measured in chunks, not bytes or milliseconds. With the default capacity of 32:

| Format and chunk size | Bytes/chunk | Raw bytes buffered at capacity 32 | Approximate audio time buffered |
| --- | ---: | ---: | ---: |
| PCM16 mono, 16 kHz, 20 ms | 640 | 20 KB | 640 ms |
| PCM16 mono, 24 kHz, 20 ms | 960 | 30 KB | 640 ms |
| PCM16 mono, 24 kHz, 40 ms | 1,920 | 60 KB | 1,280 ms |
| G.711, 8 kHz, 20 ms | 160 | 5 KB | 640 ms |

The memory numbers above count raw audio arrays only. They do not include `DataContent` objects, channel bookkeeping, JSON/base64 send buffers, or provider SDK buffers.

Capacity therefore controls the latency/jitter budget as much as it controls memory:

- Higher capacity absorbs transient network or serialization stalls, but it can hide backlog and increase end-to-end latency.
- Lower capacity exposes slow consumers quickly and keeps latency bounded, but it increases the chance that capture has to wait.
- Because `BoundedChannelFullMode.Wait` is used, overload applies back-pressure rather than dropping audio. That preserves fidelity but can block the producer's write path.

For microphone-driven interactive sessions, a capacity representing roughly 250-750 ms of audio is a reasonable starting point. For 20 ms frames, that means about 12-38 chunks, so the default of 32 is defensible. For larger chunks, the same default can become a multi-second backlog and should be reduced.

## Back-pressure behavior

The current implementation chooses correctness over loss:

- Full channel: `WriteAsync` waits.
- Completed channel: further writes fail through channel semantics.
- Pump cancellation: `ReadAllAsync` and `AppendInputAudioAsync` observe the provided cancellation token.
- Send failure: the pump task faults instead of silently dropping audio.

This is the right default for an application-level helper because it avoids silent data loss. It does mean the producer must not run blocking waits on a time-sensitive capture callback. If the audio capture API requires a non-blocking callback, the callback should hand off to a dedicated producer task or use a policy outside `RealtimeAudioPipe` to drop, coalesce, or mark overflow explicitly.

## Sequential send tradeoff

`PumpToAsync` awaits each `AppendInputAudioAsync` before reading and sending the next chunk. This has useful properties:

- Preserves audio order.
- Lets provider transport back-pressure propagate to the pipe.
- Avoids unbounded in-flight sends.
- Keeps error behavior simple.

The tradeoff is throughput coupling: serialization or transport delay for one chunk delays every later chunk. Parallel sends are not appropriate for ordered realtime audio unless the transport or provider offers an ordered batching primitive. A better optimization path is batching adjacent chunks when backlog exists, or provider-specific binary/audio-frame writes, not concurrent appends.

## Serialization and wire overhead

For providers that send `InputAudioBufferAppendRealtimeClientMessage` as JSON text frames, raw audio bytes become base64. Base64 increases payload size by about 33% before JSON property overhead. Approximate steady-state payload rates:

| Format | Raw audio rate | Base64 payload rate before JSON overhead |
| --- | ---: | ---: |
| PCM16 mono, 16 kHz | 32 KB/s | 43 KB/s |
| PCM16 mono, 24 kHz | 48 KB/s | 64 KB/s |
| G.711, 8 kHz | 8 KB/s | 11 KB/s |

At 20 ms chunks, PCM16 24 kHz sends 50 messages per second, each carrying about 960 raw bytes or about 1,280 base64 bytes plus JSON envelope overhead. That is still modest bandwidth, but the fixed per-message CPU/allocation overhead can matter on constrained clients, high-concurrency servers, or sessions with multiple simultaneous audio streams.

Potential optimizations if this path becomes hot:

- Add source-generated JSON metadata for realtime message types.
- Prefer provider SDK paths that avoid reserializing already-normalized messages when possible.
- Use larger chunks only where added latency is acceptable.
- Add an explicit batching helper that combines queued adjacent chunks up to a latency or byte threshold.
- Consider provider-specific binary audio frames if supported by the underlying service.

## Logging and telemetry considerations

The logging layer correctly redacts audio payloads as lengths rather than raw bytes. That avoids large log entries and PII leakage. The performance tradeoff is that every send can still execute message-type checks and logging level checks. This is minor compared with serialization, but at 50-100 appends per second it is worth keeping hot-path logging at Debug/Trace disabled in production.

Telemetry should prefer aggregate counters such as bytes sent, chunks sent, queue depth, back-pressure wait time, and send latency. Per-chunk spans would be too noisy and expensive for normal realtime audio rates.

## Recommended usage guidance

Use `RealtimeAudioPipe` when the application needs a simple, safe boundary between an audio producer and a realtime session. It is especially useful when capture, encoding, session lifetime, and provider transport live in different components.

Avoid treating it as a complete audio pipeline. The caller should still own:

- Capturing audio on a non-blocking path.
- Choosing frame duration.
- Resampling/transcoding to the provider's configured input format.
- Constructing `DataContent` with correctly sized byte chunks.
- Deciding whether overload should block, drop, coalesce, or fail.

Recommended defaults for interactive voice prototypes:

- 20 ms chunks.
- Capacity near 16-32 chunks, adjusted by measured queue wait and latency.
- Dedicated producer/pump tasks rather than blocking capture callbacks.
- Debug/trace logging disabled during performance runs.
- Metrics for chunk rate, byte rate, channel backlog, write wait time, send latency, and pump faults.

## Gaps to address before production

1. Add docs or API comments making clear that the current pipe accepts already-framed `DataContent`; it does not read `Stream`/`PipeReader` input.
2. Add optional queue-depth/back-pressure instrumentation so applications can tune capacity with real data.
3. Add tests for cancellation while blocked on a full channel and writes after completion/disposal.
4. Consider an explicit overflow strategy if the intended target includes real-time capture callbacks that cannot wait.
5. Add source-generated JSON serialization for realtime messages if Foundry JSON text frames remain in the hot path.
6. Revisit whether an output-side audio writer helper is still planned, because the current `RealtimeAudioWriter` name now describes an input writer view.
