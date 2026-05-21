# Misc Notes

Small decisions and reminders that don't yet warrant their own document. Add
to this file as small items come up.

## Scope

- **Video I/O is out of scope for the initial `RealtimeAgent` implementation.**
  Keep video references in design docs and tables for completeness (e.g.
  Gemini's `realtimeInput.video`, the avatar pipeline, "Avatar/video output"
  rows in event matrices), but explicitly call out each occurrence as
  "documented for completeness — not in the initial implementation." Video
  provider capabilities can be surfaced later as video-specific outputs without
  changing the core audio/text/tool-call surface.

## Extensibility conventions

- Provider-specific raw events and request knobs reuse the existing
  `Microsoft.Extensions.AI` patterns: `RawRepresentation` on emitted types and
  `ChatOptions.RawRepresentationFactory`-style factories on options types.
  Do **not** invent new escape hatches. See `events.md` §5 for details.

- `Microsoft.Extensions.AI` already ships a realtime API surface
  (`IRealtimeClient`, `IRealtimeClientSession`, `RealtimeClientMessage` /
  `RealtimeServerMessage` hierarchies, `RealtimeConversationItem`,
  `RealtimeSessionOptions`, `RealtimeAudioFormat`,
  `VoiceActivityDetectionOptions`, etc., namespace `Microsoft.Extensions.AI`).
  Treat these as the normalized cross-provider surface — don't invent a parallel
  taxonomy. See `normalized-events.md` for the full mapping and the small list
  of additions we layer on top in `Microsoft.Agents.AI.Abstractions`.
