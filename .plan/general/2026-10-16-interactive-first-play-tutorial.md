# 2026-10-16 — 실습형 첫 플레이 튜토리얼

- Date: 2026-10-16
- GitHub Issue: Pending publication approval
- Status: In progress

## Goal
한국어 단계별 실습으로 선택, 채집, 건설, 생산, 성장, 연구, 전투를 익힌다. 실제 행동 결과를 보고 다음 목표로 진행한다.

## Non-goals
여섯 종족 개별 캠페인, 음성 더빙, 기존 게임 규칙 변경.

## Context / Constraints
검증된 feat/221을 기반으로 추가 worktree 없이 파일소유를 분리한다. 원격 이슈 생성은 자동승인검토가 새 payload 게시 승인을 요구해 거절하여 로컬 구현부터 진행한다. 세계수 대 비활성 AI의 기본 맵을 사용하며 새 모드가 일반 솔로/온라인에 섞이지 않게 한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 메뉴, 솔로, HUD 입력 차단, 기존 회귀 검사를 확인.
- [x] **Step 1: Implementation** 읽기 전용 진행 추적기, 한국어 안내 패널, 시작 메뉴/모드 분리.
- [x] **Step 2: Tests** 완료 조건·먼저 수행한 행동·재시작·모드전환 자동 검사, Unity 빌드 및 실제 입력.
- [ ] **Step 3: Rollout / Rollback** 별도 tutorial 빌드, 게시 내용 준비 후 승인, 커밋 단위 되돌리기.

## Validation
- **Commands to run:** dotnet build; Unity ViewSelfCheck; BuildPlayer; 실제 튜토리얼 플레이.
- **Expected output:** 행동 없이 자동 통과하지 않으며, 정상 명령으로 끝까지 완료. 일반 솔로는 AI 유지, 온라인은 튜토리얼 상태 없음.

## Risks & Rollback
- **Risks:** 먼저 수행한 행동 누락, 자원 고갈, 패널 입력 통과, 다른 매치에 상태 잔존.
- **Rollback steps:** 튜토리얼 관련 뷰와 메뉴 연결을 함께 revert. 시뮬레이션 데이터 변경 없음.

## Open Questions
- None. 첫 교육은 세계수로 고정하고 완료 후 자유 솔로로 이동한다.

## Results
- Release dotnet build passed. Sim and Net rules, content version and protocol remain unchanged.
- Final Unity ViewSelfCheck passed, including all 8 tutorial objectives through legal commands, early-action retention, stale selection rejection, fresh-world isolation, passive opponent, restored solo AI, pause and panel hit regions.
- Golden hash remains `FAEC9B582915E10D`.
- Windows build `Client/Builds/tutorial/WordCraft.exe` passed, 88,186,735 bytes including player data.
- Fixed two editor test fixtures: edit-mode singletons must be initialized explicitly; a worker killed during combat cannot count as a fresh selection. Progress conditions were not weakened.
- Review fixes: tutorial hides while sound mixer is open, camera wheel/middle-drag ignore HUD regions, narrow windows place the panel beneath alert text.
- Corrected previous QA documentation: the observed 광란의 토템 is Tech slot 2, not slot 1. The building and research observations remain valid.
- Actual Windows input completed all eight lessons, including a five-unit attack and an enemy kill. Selection, restart, panel click blocking, focus controls and research completion were observed. Existing user game windows were preserved.

- Actual-play feedback: two melee units were insufficient against the defending Hellfire frontline. The final lesson now explicitly asks for at least five leafblade units and double-click selection before attack-move; no balance values were changed.

- Manual QA exposed a completion race: combat continued and the victory screen replaced the completion panel within six ticks. The runner now freezes immediately on completion, preserving the panel until an explicit next action. A session regression holds overdue ticks across twelve update frames and checks normal solo recovery.
- Final read-only review found no P1/P2 issues. The completion HUD now reports Tutorial complete instead of suggesting P can resume.

- Final actual play completed again at tick 4688; tick remained fixed for more than ten seconds. AI와 실전 시작 opened a fresh solo match, removed the tutorial panel and advanced tick 33 to 146. User follow-up worker production/resource rally changes are being integrated into the next build.
