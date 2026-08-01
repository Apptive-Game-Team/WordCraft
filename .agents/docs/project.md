# Project Context

Fill this document during project initialization. Agents must verify commands against repository configuration before running them.

## Overview

- Product: WordCraft, a desktop RTS with deterministic lockstep multiplayer over
  peer-to-peer UDP. No gameplay server exists.
- Primary users: players on Windows and macOS, on a LAN in the first milestones.
- Core domain: deterministic simulation, resource gathering, base building, unit
  production, pathfinding, combat, and peer input exchange.
- Runtime environment: .NET 7 SDK for headless simulation work today; a Unity
  2022 LTS desktop project from Milestone 4 onward.

## Architecture

- Entry points: `Replay/Program.cs` (headless determinism self-check). The Unity
  player entry point does not exist yet.
- Main modules:
  - `Sim/` — pure C# simulation, `netstandard2.1`, `WordCraft.Sim` namespace.
  - `Replay/` — headless harness that runs input logs and compares state hashes.
- Dependency direction: `Replay` depends on `Sim`. `Sim` depends on nothing.
  The future Unity view layer will depend on `Sim`; `Sim` must never depend on it.
- External systems: none. No server, no database, no network service.
- Persistent data: none yet. Input logs and replays are files, not a store.

## Commands

| Purpose | Command |
|---|---|
| Install dependencies | none; the .NET SDK is the only requirement |
| Run locally | `dotnet run --project Replay` |
| Format | TODO |
| Lint | TODO |
| Type-check | `dotnet build` |
| Unit tests | `dotnet run --project Replay` (assert-based self-check, no test framework) |
| Integration tests | TODO |
| Build | `dotnet build` |

## Constraints

- Supported platforms: Windows and macOS desktop. WebGL and mobile are out of scope.
- Compatibility requirements: `Sim/` targets `netstandard2.1` so it imports into
  Unity 2022 LTS unchanged.
- Determinism rules for `Sim/`, all mandatory:
  - No `float` or `double`. Use `Fix` and `FixVec2`.
  - No `UnityEngine`, `System.DateTime`, `System.Random`, or wall-clock time.
  - No `Dictionary`/`HashSet` iteration in simulation order. Iterate by entity id.
  - Every random draw goes through `World.Random`; the draw count is state.
  - Entity ids are never reused.
  - New state fields must be added to `World.Hash()` or desyncs go undetected.
- Performance constraints: the simulation runs at 20 ticks per second; every peer
  must finish a tick inside its budget or the whole match stalls.
- Security or privacy requirements: lockstep peers see full game state by design.
  Cheat prevention is an explicit non-goal.

## Ownership

- Maintainers: Apptive-Game-Team.
- Sensitive modules: `Sim/` determinism contracts and, once it exists, the peer
  input exchange and hash comparison code.
- Changes requiring explicit review: anything touching fixed-point math, RNG,
  entity id allocation, command ordering, the state hash, or the wire protocol.

## Reused Material

Art and faction concepts come from the WordOnline project. Assets must pass a
per-file ownership check before being copied; most are team-made but early
assets may not be. The implementation plan lives in the WordOnline repository at
`.plan/general/2026-08-01-p2p-lockstep-rts.md`.
