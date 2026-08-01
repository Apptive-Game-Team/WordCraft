# CLAUDE.md

Repository guidance lives in [AGENTS.md](AGENTS.md) and `.agents/docs/`.
Read [.agents/docs/project.md](.agents/docs/project.md) before non-trivial work.

## Hard rules for `Sim/`

The simulation must produce byte-identical state on every peer. Breaking any of
these silently desyncs matches, and the failure surfaces far from its cause.

- No `float`, no `double`. Use `Fix` (Q16.16) and `FixVec2`.
- No `UnityEngine`, `System.DateTime`, `System.Random`, no wall-clock time.
- No `Dictionary`/`HashSet` iteration in simulation order. Iterate by entity id.
- Every random draw goes through `World.Random`. The draw count is state.
- Entity ids are never reused.
- Any new state field must be added to `World.Hash()`, or desyncs go undetected.
- Commands execute in canonical order `PeerId, Seq` regardless of arrival order.

## Check

```bash
dotnet run --project Replay
```

Prints `OK: all determinism checks passed`, or the first failing invariant.
Run this after any change under `Sim/`.

## Layout

- `Sim/` — pure C# simulation, `netstandard2.1`, no dependencies.
- `Replay/` — headless determinism self-check and replay harness.
- The Unity project does not exist yet. It arrives at Milestone 4, deliberately
  late, so Unity types and floating point cannot leak into the simulation.
