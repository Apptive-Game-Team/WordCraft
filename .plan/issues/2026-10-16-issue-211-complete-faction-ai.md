# 2026-10-16 — 종족 AI 완성

- Date: 2026-10-16
- GitHub Issue: #211
- Status: Live acceptance verified in v42; integrated 144-game arena passed in #221

## Goal
Complete the existing AI ladder with faction composition, growth, arrival targeting and active abilities; verify real combat and deterministic matches.

## Non-goals
Changing unit balance or replacing lockstep; competitive human-level strategy.

## Context / Constraints
Reuse reviewed portions of origin/feat/211. Command-only AI, fixed-point math, stable entity order. No additional persistent state.

## Approach (Checklist)
- [x] **Step 0: Recon** Inspect WIP, existing AI checks, production and ability gates.
- [x] **Step 1: Implementation** Fix economy reservations and actual match stalls; integrate static composition and ability decisions.
- [x] **Step 2: Tests** Add observable command/behavior checks, run Replay and arena.
- [x] **Step 3: Rollout / Rollback** Increment content version and document policy; revert AI changes as a unit if required.

## Validation
- **Commands to run:** dotnet run --project Replay; dotnet run --project Host -- arena
- **Expected output:** deterministic checks pass; report arena completion honestly.

## Risks & Rollback
- **Risks:** new spending priorities can delay armies and create matchup stalls.
- **Rollback steps:** Revert AI, table, content-version and accompanying tests together.

## Open Questions
- Arena completion threshold depends on remaining simulation movement behavior; measure before claiming completion.

## Initial Results (v40)
- Final arena: 36/36 matches finish within 6000 ticks; every twin hash agrees. Peer 0: 11 wins, peer 1: 25 wins.
- Faction win counts (12 appearances each): Tree 8, Hellfire 11, Water 4, Rock 6, Drift 3, Humans 4. No unit balance values changed.
- Existing armed-only and highest-tier-only AI test assumptions replaced by opening-budget/support-cap and funded tech-progression behavior checks. Live mirror producer legality remains covered.
- Remaining acceptance gap: RockGolems may not field support within an unfunded 2000-tick mirror. Three-role production passes in a separate funded/full-tech scenario; this is not a claim that the live-game requirement passes.
- No scouting, retreat or expansion strategy; competitive balance remains open.

- Final full regression: dotnet run --no-restore --project Replay → OK: all determinism checks passed (including all 10 active AI decisions, funded composition, air response and existing checks).
- git diff --check for AI-owned files passes.

## Live Acceptance Follow-up (v41)
- Fixed cross-building active-unlock research starvation, first support investment,
  late reinforcement joining, affordable replacement of a missing combat role,
  anti-air budget reservation and rotated building search.
- `dotnet run --no-restore --project Replay -- --ai`: PASS.
- Normal-resource scenario with a legally gathering/producing/holding defender:
  all six factions witness three different living unit entries and their target
  composition roles together within 2000 ticks. Humans have no melee roster and
  are explicitly checked for three entries spanning ranged and support.
- Growth: Tree 2; arrival: Drift 6; actual casts: Tree 2, Hell 1, Water 17,
  Rock 1, Humans 3; longest supply-cap stall: 0 ticks for every faction.
- Full AI-vs-AI balance and completion are validated separately by #221.
- v42 recheck: all live conditions still pass; Water actual casts 13, other witnesses unchanged.
- #221 arena: 140/144 finish, 4 draws, faction win rates 41.67–58.33%, seat gap 2.78pp; every twin hash matches.
