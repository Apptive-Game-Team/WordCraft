# 2026-10-16 — 플레이어 건설·성장 명령 완성

- Date: 2026-10-16
- GitHub Issue: #226
- Status: In progress

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
- [ ] **Step 3: Rollout / Rollback** Build only to Client/Builds/balanced after integration approval; retain the user's existing process.

## Validation
- **Commands to run:** Unity batch WordCraft.View.ViewSelfCheck.Run after coordinated build.
- **Expected output:** All view determinism and roster control checks pass.
- Manual: building pages, placement/cancel, growth selection/cancel, building double click, production and research.
- Actual automated result: `Client/Logs/issue226-selfcheck.log`, final hash `0x89303B33ED3EA28D`; all view checks passed. Initial test-fixture compile/lifecycle failures were corrected before the passing run.
- Manual gameplay of the changed controls is still pending final player build and exclusive input access; user's older game window remained untouched.

## Risks & Rollback
- **Risks:** stale submenu state and placement slot mismatch; verify selection changes and escape cancellation.
- **Rollback steps:** revert only issue #226 view changes, leaving simulation and other agents' changes intact.

## Open Questions
- None.
