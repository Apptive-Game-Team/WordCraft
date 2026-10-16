# 2026-10-16 — 본진 일꾼 생산과 집결지 조작 복구

- Date: 2026-10-16
- GitHub Issue: Pending publication approval
- Status: In progress

## Goal
본진에서 일꾼을 생산하고 우클릭 집결지로 새 유닛을 보내는 기본 RTS 조작을 실제 플레이에서 사용할 수 있게 한다.

## Non-goals
경제 밸런스 재설계, 종족별 새 유닛 추가.

## Context / Constraints
사용자가 세계수 일꾼 소환 불가를 보고했다. 시뮬레이션은 생산을 지원하지만 ProductionMenu가 Worker를 제외하며 CommandCard에도 별도 버튼이 없었다. 기존 튜토리얼 변경을 보존하고 추가 worktree 없이 파일 소유를 분리한다. 원격 게시 승인은 완성된 내용으로 요청한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 본진 생산 데이터, UI 카드, 우클릭과 rally 시뮬레이션 조사.
- [x] **Step 1: Implementation** 합법 생산 건물에 일꾼 U 버튼 추가, 집결지 결함 수정, 튜토리얼 안내 갱신.
- [ ] **Step 2: Tests** 모든 종족 본진 생산 자동 검사 및 실제 세계수 생산/집결지 플레이.
- [ ] **Step 3: Rollout / Rollback** 튜토리얼 포함 새 빌드와 게시 초안, 변경 단위 revert.

## Validation
- **Commands to run:** Unity ViewSelfCheck, Windows BuildPlayer, 실제 본진 일꾼 생산과 우클릭 집결지 확인. Sim 변경이 필요하면 Replay/Host 회귀 추가.
- **Expected output:** 일꾼 생산 버튼 표시, 합법 명령으로 새 Worker 생성, 새 유닛이 설정된 집결지로 이동.

## Risks & Rollback
- **Risks:** 기존 생산 단축키 변경, 생산 불가능 건물 노출, rally가 다른 명령에 덮임.
- **Rollback steps:** 일꾼 카드 및 연결 검사/안내 변경 단위 revert.

## Open Questions
- None. 광맥 집결지는 새 일꾼이 자동 채집하고, 일반 땅 재지정 시 채집 대상이 해제된다.

## Investigation / Decisions
- Worker button occupies unused U cell without shifting military production pages.
- Production-capable buildings rally before enemy attack dispatch; defensive towers retain attack orders.
- Resource rally stores explicit node ID through existing command Arg, hashes new simulation state and advances content version. A dead or depleted node falls back to movement. Ground rally clears the node. Replay/Host and Unity checks are required after integration.

## Results
- CoreCLR Replay full regression passed, including six-faction production/deposit, per-tick repeated-log hashes, target clearing, invalid/depleted/dead nodes and foreign-order rejection.
- Host selfcheck passed including handshake coverage. Release build with -m:1 passed with zero warnings/errors.
- ContentVersion 43; CoreCLR golden B7BAA82575E2386D. Existing Command.Arg carries resource ID plus one; wire structure unchanged. New entity state participates in World.Hash.
- Independent review found no P1/P2 defects. Unity and actual player input remain in progress.

- Unity ViewSelfCheck passed with Mono golden B7BAA82575E2386D. New Windows build: Client/Builds/tutorial-worker-rally/WordCraft.exe.
- Actual input: base U button visible, mana 500 to 460, population 4 to 5, new worker reached ground rally. Resource-rally manual verification paused after user input was detected; automated resource-gather/deposit checks passed. Awaiting user preference before further app input.

## Follow-up: A attack command
- User requested standard A attack targeting. Attack cards use A while production cells retain their existing keys; A on hostile entities attacks that target, and A on ground/friendly/resource points issues attack-move.
- Update HUD key display and tutorial instructions together. Add input-boundary regression, rerun Unity and build a separate player. No simulation change or further version bump required.
- Keep live input stopped while the user is playing; build and automated checks can continue independently.


- A-target input regression passed in Unity; golden remains B7BAA82575E2386D. Removed A from Horizontal camera axis to prevent unintended camera movement while aiming. Arrow keys retain camera movement. Final separate controls build includes this input setting.

