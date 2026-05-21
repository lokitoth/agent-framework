# Task 01 — Solution skeleton

Stand up `/proto/impl/realtime.slnx` with the minimum tooling needed for
subsequent tasks to add projects without further config churn.

## Decisions

- **Target framework: `net10.0` only.** The proto does not target
  `netstandard2.0` / `net472` (unlike `/dotnet/src`). All consumers will be
  on `net10.0`. Multi-targeting adds analyzer / `using` noise we don't need
  for a prototype.
- **SDK pin: `10.0.200` rollForward `minor`.** Same as
  `/dotnet/global.json`. Local SDK is 10.0.300 which rolls forward fine.
- **Single `Directory.Build.props` / `Directory.Packages.props`** at
  `/proto/impl`. The repo root (`/`) does not have any MSBuild files, so
  upward search stops at this level — clean isolation.
- **NuGet pins copy only what the proto needs.** Don't carry the full
  `/dotnet/Directory.Packages.props` matrix.
- **Cross-solution `ProjectReference` to `/dotnet/src/*.csproj`** is allowed
  per plan §3.6 (review v2 §A). We won't exercise it in this task; the
  hosting packages will use it later.
- **`TreatWarningsAsErrors=true`** — match `/dotnet` discipline.
- **Suppress** `MEAI001` (M.E.AI experimental) and `OPENAI001` (OpenAI
  SDK experimental) globally so realtime types are usable without per-call
  pragmas. The AF-side `[Experimental("MEAI-REALTIME-001")]` attribute is
  emitted as `MEAIREALTIME001` once the analyzer normalizes the id; we will
  add that suppression once a consumer trips it (tests).
- **Single-arg `Experimental` attribute keys.** MEAI uses an internal
  `DiagnosticIds.Experiments.AIRealTime` constant which resolves to a
  particular id; we just use a stable string `"MEAI-REALTIME-001"` on the
  AF-side per the plan ADR.

## Files created

- `proto/impl/.gitignore` — `.NET` ignores (bin/obj/.vs/...).
- `proto/impl/global.json` — SDK pin.
- `proto/impl/Directory.Build.props` — common build settings.
- `proto/impl/Directory.Packages.props` — central package pins.
- `proto/impl/realtime.slnx` — empty solution.
- `proto/impl/src/.gitkeep`, `proto/impl/tests/.gitkeep` — folder anchors.

## Validation

- `dotnet build proto/impl/realtime.slnx` against an empty `.slnx` should
  succeed (no projects to build, so it returns immediately).
- Verify `dotnet --list-sdks` reports a compatible SDK
  (`10.0.x` with feature band ≥ 200).

## Follow-ups deferred

- Source link / analyzers configuration — out of scope; can be lifted from
  `/dotnet` later.
- `.editorconfig` — defer until first real code lands and a style choice is
  needed.
- Git repo init — `/proto` itself is not a git repo. We init one at
  `/proto/impl` so we can commit per the user request without polluting
  outside the proto solution.
