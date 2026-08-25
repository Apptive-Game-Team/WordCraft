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

## Build

```bash
dotnet build
```

Vendors `WordCraft.Sim.dll` and `WordCraft.Net.dll` into `Client/Assets/Plugins/`.
To build a player without opening the editor or pushing a tag:

```bash
Unity -batchmode -nographics -quit -projectPath Client \
      -executeMethod WordCraft.View.BuildPlayer.Run \
      -buildOutput play -buildTarget Win64
```

The binary lands in the folder `-buildOutput` names, created if it does not
exist. `-buildTarget` takes `Win64`, `OSXUniversal` or `Linux64`, and defaults
to whatever platform the editor is already set to. Development builds only: the
release workflow does not call this entry point — game-ci's `unity-builder`
brings its own build step.

**The stale-plugin trap.** Unity has no idea the vendored assemblies came from
source, so forgetting the `dotnet build` does not fail. It produces a perfectly
working player that plays by last week's rules, with nothing anywhere saying so.
One such build was actually made: a player with no 통로 in it.
`BuildPlayer.Fresh()` now refuses to build when either vendored assembly is
older than any `.cs` under `Sim/` or `Net/`, and names the file that is newer. A
timestamp rather than a hash, because that is exactly the question `dotnet build`
itself asks and so cannot answer differently. Nothing gates simply opening the
editor; that order is on you.

## Layout

- `Sim/` — pure C# simulation, `netstandard2.1`, no dependencies.
- `Net/` — P2P lockstep session and UDP transport, `netstandard2.1`.
- `Replay/` — headless determinism self-check, replay harness, replay file format.
- `Host/` — console runner: `host`, `join`, `solo`, `selfcheck`, `replay`.
- `Client/` — the Unity 2022 LTS view. Consumes `Sim` and `Net` as compiled
  assemblies that `dotnet build` vendors into `Client/Assets/Plugins/`; they are
  gitignored, so build once before opening the project.

The Unity project was built late on purpose, so that Unity types and floating
point could not leak into the simulation while the determinism core was being
laid down. That direction is now a standing rule rather than a schedule: `Sim`
and `Net` depend on nothing above them, and the view may read the simulation but
never write to it.
