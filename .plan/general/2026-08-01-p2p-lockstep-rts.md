# 2026-08-01 — P2P Deterministic Lockstep RTS (New Project)

- Date: 2026-08-01
- GitHub Issue: none yet
- Owning repository: new standalone Unity project (not a WordOnline submodule)
- Status: Draft

## Goal

Build a new desktop RTS (Windows/macOS) that reuses WordOnline's art assets and
faction/summon concepts, with resource gathering, base building, and unit
production. Multiplayer runs as deterministic lockstep over direct peer-to-peer
UDP, with no gameplay server.

## Acceptance Criteria

- Two peers running the same build, same map seed, and same confirmed input log
  produce identical state hashes at every checkpoint tick through match end.
- Simulation assembly compiles and runs without any reference to `UnityEngine`;
  Unity code only reads simulation state and renders it.
- A match starts from direct IP entry on LAN with no external service running.
- Resource gathering, building placement/construction, unit production, and unit
  move/attack commands all execute inside the deterministic simulation.
- A desync is detected within one checkpoint interval, halts the match, and dumps
  the first divergent entity/field for diagnosis.
- Reused art keeps the faction identity defined in the WordOnline summon concept
  manifest; no asset is copied without its license/ownership confirmed.

## Non-goals

- WebGL/browser support, mobile support.
- Matchmaking, accounts, ranking, persistence, or any WordOnline backend service.
- NAT traversal, STUN/TURN relay, or internet play in the first milestone.
- Cheat prevention. Lockstep peers see full state by design.
- Rollback/GGPO-style prediction. Input-delay lockstep only.
- The WordOnline word/card casting layer. Units come from conventional
  production buildings only; element+form card combination is dropped.
- Changes to any existing WordOnline module.

## Context / Constraints

- Source concepts live in WordOnline: card elements `FIRE/WATER/NATURE/LIGHTNING/
  ROCK/WIND` (`game/.../magic/ElementType.java`), and the frozen faction/summon
  concepts in `.plan/issues/2026-07-31-issue-9-summon-concept-manifest.md`
  (지옥불 군단, 물 슬라임, 돌 골렘 부족, 차원 유랑종).
- Reusable art in `client/`: 96 prefabs in `Assets/Resources/Prefabs`, 110 sprites
  in `Assets/Resources/Game`, 21 sound files, 56 images in `Assets/Art/Images`.
  `Assets/Art` is 131 MB, `Assets/Resources` is 11 MB.
- Existing prefabs are wired to the server-authoritative renderer path
  (`ObjectSpawner`/`ObjectUpdater`/`ObjectSyncer`). Copy sprites, animations, and
  materials; rebuild prefabs against the new view layer instead of porting them.
- `game/src/main/java/com/wordonline/server/game/domain/object/` is a usable
  reference implementation for porting: `component/mob` (state machine, detector,
  pathfinder, directive), `component/physic` (`CircleCollider`, `RigidBody`,
  `StaticObstacle`, `WallCollision`), and `util/CollisionSystem`. It is Java and
  float-based; port the structure, not the arithmetic.
- Determinism contract requirements are already enumerated in
  `.plan/general/2026-07-12-deterministic-client-lockstep.md`. Reuse that list
  (fixed timestep, fixed-point math, RNG draw order, entity/component IDs,
  collection traversal order, A* tie-breaking, serialization, hash algorithm).
  That plan keeps a server relay; this project does not.
- Desktop-only removes the WebGL IL2CPP/AOT determinism risk and allows raw UDP,
  so `System.Net.Sockets` covers transport with no networking dependency.
- Unity version: pin to the same `2022.3.34f1` as `client/` unless a newer LTS is
  chosen before scaffolding, so copied assets import without upgrade churn.

## Affected Repositories and Contracts

| Repository | Role |
|---|---|
| new RTS project | All new code. Sole deliverable. |
| `client/` | Read-only asset and concept source. No edits. |
| `game/` | Read-only reference for mob/physics/collision structure. No edits. |
| root `WordOnline` | Holds this plan only. No submodule pointer change. |

Internal contracts inside the new project:

- `Sim` assembly: pure C#, no `UnityEngine`. Exposes tick(inputs), state read
  API, and `Hash()`.
- `Net` assembly: UDP transport, peer session, input exchange, hash compare.
- `View` assembly (Unity): renders `Sim` state, converts player actions to
  `Command` values. Never mutates simulation state.
- Wire messages: `Hello{protocolVersion, simVersion, contentVersion, seed, map}`,
  `Input{tick, peerId, commands[], ackTick}`, `Hash{tick, hash}`.
- Canonical order: `tick -> peerId -> command sequence -> entityId -> system order`.

## Approach

- [ ] Recon
  - Confirm asset licensing/ownership for every sprite, sound, and font to be
    copied. Exclude anything third-party with a non-transferable license
    (`Assets/Resources/Cainos`, `TextMesh Pro`, purchased packs) until verified.
  - Inventory which of the 96 prefabs have reusable sprites/animations versus
    which are server-DTO-only shells.
  - Decide project name, repository host, and Unity LTS version.
  - Pick fixed-point representation (Q32.32 `long` recommended) and verify sqrt,
    trig, and normalize behavior needed by movement and collision.
- [ ] Implementation
  - Milestone 1 — deterministic core: `Sim` assembly with fixed-point math,
    deterministic RNG, integer tick loop, entity store with stable IDs, state
    hash, and a headless replay harness that runs the same input log twice.
  - Milestone 2 — RTS gameplay in `Sim`: resource nodes and gathering, worker
    units, building placement and construction, production queues, unit
    move/attack commands, grid pathfinding with explicit tie-breaking, combat and
    death. Single-player against nothing, no netcode yet.
  - Milestone 3 — P2P lockstep `Net`: UDP socket, direct-IP connect, handshake
    with version/seed agreement, fixed input delay (default 3 ticks at 20 Hz),
    input send/ack/resend, tick barrier, periodic hash exchange and desync halt.
  - Milestone 4 — Unity `View`: camera, selection box, command issuing, unit and
    building rendering from copied sprites, resource/production HUD, faction
    visual identity from the summon concept manifest.
  - Milestone 5 — content pass: two playable factions, one map, win condition.
- [ ] Focused validation
  - Run the replay harness twice on the same input log and diff every checkpoint
    hash; on mismatch, dump the first divergent entity and field.
  - Cross-machine replay: same log on Windows and macOS builds, compare hashes.
  - Unit tests for fixed-point overflow boundaries, RNG draw count stability,
    A* equal-cost tie order, collision pair ordering, and entity ID reuse.
- [ ] Compatibility and regression validation
  - Reject a peer whose `protocolVersion`/`simVersion`/`contentVersion` differs,
    at handshake, before any tick runs.
  - Test packet loss, duplication, reordering, and delay injection on the input
    channel; confirm the barrier stalls and recovers without executing a tick
    twice.
  - Test peer disconnect mid-match: remaining peer ends the match deterministically
    rather than continuing with invented input.
- [ ] Release order and rollback check
  - Not applicable in the usual sense: no deployed service and no existing users.
    Each milestone must keep the replay harness green before the next starts.

## Validation

- Commands:
  - `dotnet test` on the `Sim` test project (headless, no Unity).
  - Unity Edit Mode tests for `View`-to-`Sim` command mapping.
  - Replay harness CLI over recorded input logs (exact invocation recorded once
    the harness exists).
- Manual checks:
  - Two builds on one LAN: play a full match, confirm no hash mismatch and
    identical end state on both screens.
  - Force a desync by mutating one peer's state deliberately; confirm detection at
    the next checkpoint and a first-divergence dump.
  - Play a match with 200 ms artificial latency; confirm input delay absorbs it.
- Expected results:
  - No hash drift across Windows and macOS for the same input log.
  - No tick executed twice, no non-canonical input ordering.
  - No `UnityEngine` symbol reachable from the `Sim` assembly.

## Risks & Rollback

- Determinism breakage through floating point, unordered collections, `Dictionary`
  traversal, `DateTime`, `UnityEngine.Random`, or physics engine use. Mitigation:
  `Sim` assembly forbids `UnityEngine` and `float`/`double` by convention and by a
  test that scans the compiled assembly for them.
- Lockstep pacing is bounded by the slowest peer. Mitigation: explicit input delay
  and a stall timeout with a stated forfeit policy.
- Asset licensing: copied art may include third-party packs that cannot move to a
  new project. Mitigation: recon gate before any copy; exclude unverified assets.
- Scope: full RTS (economy, building, production, pathfinding, AI) is much larger
  than the existing card game. Mitigation: milestones are independently playable;
  stop after Milestone 3 yields a working networked prototype even if content is thin.
- Rollback: the new project is standalone, so rollback is per-milestone git revert.
  No WordOnline module is affected at any point.

## Release Order

1. Deterministic core plus replay harness.
2. Single-player RTS simulation.
3. P2P lockstep over LAN direct IP.
4. Unity view and HUD.
5. Faction content and win condition.

Internet play, NAT traversal, and matchmaking are a separate future plan.

## Decisions

- Five factions: 지옥불 군단, 물 슬라임, 돌 골렘 부족, 차원 유랑종, 세계수 정령.
  The first four come from the summon concept manifest; 세계수 정령 is new and
  needs its own concept entry. Existing nature art covers it (`LifeTree`,
  `TreeGolem`, `VineSpirit`, `SeedSpirit`, `GiantVine`, `Overgrowth`,
  `LeafSlime`, `Vine`, `VineColony`, `SeedNest`).
- No word/card casting. Conventional RTS unit production only.
- Art ownership: most assets are team-made, but early-period assets may not be.
  Every asset needs an explicit per-file ownership check before it is copied.
- Project name `WordCraft`. Location `~/development/wordcraft`, root namespace
  `WordCraft`.
- Fixed-point format Q16.16 in a `long` backing field, not Q32.32. Simpler
  multiply with no 128-bit intermediate, and 1/65536 precision is enough for RTS
  positions.
- Milestone 1 is plain .NET (`dotnet` 7 present), no Unity project yet. The Unity
  project appears at Milestone 4, so the simulation cannot accidentally depend on
  `UnityEngine`.

## Open Questions

- Project and repository name?
- Unity `2022.3.34f1` to match `client/`, or a newer LTS?
- Tick rate: reuse 20 Hz from `GameLoop.FPS`, or raise to 30 Hz for RTS feel?
- Player count target: 1v1 only, or up to 4 peers in a full mesh?
- 세계수 정령 is not a faction defined in the summon manifest; it appears only as
  a contrast reference. Does it replace 물 슬라임 as the fourth faction, or are
  there five factions? Nature art exists either way (`LifeTree`, `TreeGolem`,
  `VineSpirit`, `SeedSpirit`, `GiantVine`, `Overgrowth`, `LeafSlime`,
  `VineColony`).
- Which early-period assets are not team-made? Producing that list is a recon
  gate before any file is copied.
