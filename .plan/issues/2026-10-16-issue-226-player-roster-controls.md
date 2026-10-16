# 2026-10-16 — 플레이어 건설·성장 명령 완성

- Date: 2026-10-16
- GitHub Issue: #226
- Status: Complete (local validation)

## Goal
Expose every building entry and growth target through the real command card; preserve building selection on double click.

## Non-goals
Simulation rules, balance, networking, and the user's running player are unchanged.

## Context / Constraints
Build and Grow already accept packed roster arguments. Current UI only builds slot zero and never sends Grow. Use existing paging keys and authoritative simulation validation.

## Approach (Checklist)
- [x] **Step 0: Recon** Inspect card, orders, selection and placement ghost.
- [x] **Step 1: Implementation** Page all building entries; add growth submenu with price, duration and requirements; carry slots through placement and commands; preserve building double click. Hover details keep full names and prerequisites readable while card labels stay compact.
- [x] **Step 2: Tests** Editor checks cover all faction building entries, growth source filtering, page coverage, locked prerequisites, packed arguments, double-click selection, cancellation and world-reset state. ViewSelfCheck passed exit 0 on 2026-10-16.
- [x] **Step 3: Rollout / Rollback** Built separate `Client/Builds/balanced-final/WordCraft.exe`; existing player folders/processes were not replaced or terminated. Final QA window remains paused at tick 777.

## Validation
- **Commands to run:** Unity batch WordCraft.View.ViewSelfCheck.Run after coordinated build.
- **Expected output:** All view determinism and roster control checks pass.
- Manual: building pages, placement/cancel, growth selection/cancel, building double click, production and research.
- Actual automated result: `Client/Logs/issue226-selfcheck.log`, final hash `0x89303B33ED3EA28D`; all view checks passed. Initial test-fixture compile/lifecycle failures were corrected before the passing run.
- Final automated result after balance version 42 and camera/solo-pause integration: `Client/Logs/balance42-final-selfcheck.log`, exit 0, hash `0xFAEC9B582915E10D`, independently matched with CoreCLR. The integrated checks cover camera base snap on new worlds, preservation of player pan, restart homing, solo pause clock/queued orders/resume, and refusal to pause network matches.
- Final build: `Client/Logs/balance42-final-build.log`, exit 0; `Client/Builds/balanced-final/WordCraft.exe`, 88,179,421 bytes including player data.
- Actual Windows input validation, with explicit exclusive-input permission: Solo starts with the local base visible; P pauses at tick 128 across multiple UI actions. Worker selected, V/G production building placed and completed at 400 HP; resource-node right click gathers mana (350 to 380). Building double click retains selection. F production completes (population 4 to 5). Build menu M changes to page 2; G builds Tech slot 2, 광란의 토템, completed at 250 HP. Its F research spends 100 mana, shows remaining ticks, then reaches `1/1 max`. Worker H/F growth changes the selected body to 덩쿨 정령, observed at 86/90 HP. Repeated P resume/pause works; final tick 777 remains paused.
- Evidence: `Client/Logs/balance42-final-research-complete.png` shows the slot-2 technology building, completed research, and paused state. Growth completion was visually inspected in the computer-use screenshot stream. Earlier balanced build also verified defeat at tick 876 (43.8 seconds) and the play-again menu flow; this is not a victory-path test.
- Scope limits: not every faction/ability was manually played. Automated roster coverage exercises all factions; the actual advanced construction/growth/research flow above used TreeSpirits. Previous user-input guards were respected; final authorized QA had no guard conflict. Generated Android icon settings were restored after validation.
- Build freshness fix: `BuildPlayer.SourceFiles` excludes only complete `bin`/`obj` directory segments (case insensitive); other nested real `.cs` inputs retain the existing timestamp rejection. This removed the false stale-DLL failure from another configuration's generated AssemblyInfo file.

## Risks & Rollback
- **Risks:** stale submenu state and placement slot mismatch; verify selection changes and escape cancellation.
- **Rollback steps:** revert only issue #226 view changes, leaving simulation and other agents' changes intact.

## Open Questions
- None.
