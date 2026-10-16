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
The separation timing check only prints its numbers and a warning by default; `dotnet run --project Replay -- --perf` makes it fail the run.

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

## See it

```bash
Unity -batchmode -quit -projectPath Client       -executeMethod WordCraft.View.ViewShots.Run       -shotsOutput shots -shotsTicks 900
```

Writes PNGs into the `-shotsOutput` directory (created if missing; a relative
path is taken from the working directory, not from `Client/`). How many depends
on the roster, so no total is written here: the `OK: N shots written to ...`
line of the `ViewShots.Run` log counts them, per kind. The kinds:

- `field`: 5 shots of the scripted match in progress: the base, the lane and its
  high ground, the whole map, a close-up, the flank, from the camera poses the
  player gets;
- `lineup`: 3 shots per faction, 18 in all. `lineup-<faction>` has every roster
  entry of the faction in a row; `lineup-<faction>-units` and
  `lineup-<faction>-buildings` are the same row split in two, so each body is
  twice the size;
- strips, one per faction and motion: `strip-<faction>-<motion>`, eight frames
  across 0.9 seconds side by side, so an animation reads from one still. A strip
  holds four bodies (three buildings for a building motion). A faction with more
  rows than one page holds gets further pages, `strip-<faction>-<motion>-2`,
  `-3` and so on, so every roster row is seen. The unit motions are `walk`,
  `attack`, `hit`, `death`, `idle`, `spawn`, `tower` and `growth`; the building
  motions are `build`, `work`, `destroy` and `sitedestroy` (a half-built site
  that stands 0.25 s, then falls). `tower` takes the defenses that have a weapon
  (`UnitStats.HasWeapon`); `growth` takes a worker and the body it grows into;
- `match`: 5 shots, `strip-match-<fight|kill|spawn|build|raze>`, the real
  `MatchView` driven over the scripted log, so tracers, flinches, deaths and a
  site finishing are seen as the player sees them. The scripted log razes no
  building, so `raze` drives it over a small siege of its own: a finished
  풀씨 둥지 shot down by four 지옥불 ranged units;
- `status`: `strip-<faction>-status`, one per faction: a slowed walker behind a
  normal one, a burning ranged unit, and the faction's healing aura source
  between two wounded allies, with the aura rings drawn because the sources are
  selected;
- `chain`: `strip-<faction>-chain` for the two factions with a chain-on-hit row
  (세계수 정령, 인간 마법 문명): the shot jumping across three buildings;
- `cast`: `strip-<faction>-cast`, one per faction: one active ability used on
  workers that never fight back (칼바람 회오리, 유성우, 간헐천 with 해일 부름,
  모래폭풍 with a debris throw, 낙뢰 with a 견인 대포 deploying; 차원 유랑종 has
  no active, so its strip shows a summon running out). `field` also writes
  `strip-waterslimes-field`: water fields laid, lapsing and left behind a
  walking 샘물 운반자.

`-shotsTicks` is how many ticks of the scripted log run before the field shots
(900 by default). `-shotsOnly lineup,build` writes only the kinds it names:
`field`, `lineup`, `match`, `growth`, `status`, `chain`, `cast`, or any strip
motion above, so one kind can
be looked at again in a minute instead of rendering everything. A name that is
no kind fails the run. No `-nographics`: it needs a GPU. This is how a change to
the field, a model or the light is looked at without launching a player.

```bash
Unity -batchmode -quit -projectPath Client       -executeMethod WordCraft.View.ViewSelfCheck.Run
```

Prints `OK: all view determinism checks passed`. Run it after any change under
`Client/` that reads the world, and after any change to `Sim/` that moves the
golden hash in `ScriptedLog.cs`.

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
- `Host/` — console runner: `host`, `join`, `solo`, `selfcheck`, `watch`,
  `replay`, `compare`.
- `Client/` — the Unity 2022 LTS view. Consumes `Sim` and `Net` as compiled
  assemblies that `dotnet build` vendors into `Client/Assets/Plugins/`; they are
  gitignored, so build once before opening the project. The field is 3D and
  built at runtime from code: `Field.cs` maps sim `(x, y)` to world
  `(x, height, y)` and is the only place that does, `FieldMesh.cs` is the
  terrain, `Models.cs` builds every body, `Resources/Shaders/Paper.cginc` is the
  one lighting model. No scene, prefab, mesh or material asset is ever edited.

The Unity project was built late on purpose, so that Unity types and floating
point could not leak into the simulation while the determinism core was being
laid down. That direction is now a standing rule rather than a schedule: `Sim`
and `Net` depend on nothing above them, and the view may read the simulation but
never write to it.
