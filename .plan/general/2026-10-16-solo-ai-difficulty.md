# 2026-10-16 — 실력별 솔로 AI 난이도

- Date: 2026-10-16
- GitHub Issue: Pending publication approval
- Status: Implemented and locally validated; publication pending

## Goal
초급·보통·고급을 선택하여 AI의 초반 압박과 조작 속도를 실력에 맞춘다. 초급을 새 솔로 기본값으로 사용한다.

## Non-goals
플레이 실력 자동 추정, 온라인 랭크/매칭 변경, 유닛 능력치나 무료 자원 보정.

## Context / Constraints
현재 AI는 모든 솔로에서 같은 10틱 판단 주기와 공격 조건을 사용한다. 사용자 피드백은 초반 속도가 너무 빠르다는 것이다. 기존 튜토리얼·일꾼·집결지·A 공격 변경을 보존한다. 시뮬레이션 기본 프로필은 기존 고급 행동을 보존하여 기존 arena와 회귀를 유지하고 클라이언트 기본 선택은 초급으로 한다.

## Approach (Checklist)
- [x] **Step 0: Recon** AI 판단/경제/공격 및 솔로 진입 경로 확인.
- [x] **Step 1: Implementation** 해시된 per-peer 난이도/공격 시계, 세 단계 프로필, 솔로 선택 UI와 현재 난이도 표시.
- [x] **Step 2: Tests** 여섯 종족 난이도별 선제공격 유예·공격 간격·결정론 검사, Replay/Host 및 Unity 회귀와 별도 빌드.
- [ ] **Step 3: Rollout / Rollback** 별도 실행 파일, 게시 초안, 버전/해시를 포함한 변경 단위 revert.

## Validation
- **Commands to run:** dotnet Release build, Replay, Host selfcheck, Unity ViewSelfCheck, BuildPlayer.
- **Expected output:** 초급은 120초 전 선제 출격하지 않고 보통은 60초, 고급은 기존 행동. 경제와 자위는 계속하며 무료 자원 없이 같은 합법 명령을 사용한다. 난이도 변경 시 상태 해시에 반영된다.

## Risks & Rollback
- **Risks:** 긴 준비시간 뒤 누적 병력 일괄 출격, 방어 마비, 공격 시계 누락으로 desync, 튜토리얼/일반 솔로 상태 혼입.
- **Rollback steps:** 프로필/UI/콘텐츠 버전/golden을 함께 되돌리고 온라인 양쪽 클라이언트 버전을 맞춘다.

## Open Questions
- None. 자동 실력 추정 대신 사용자가 난이도를 직접 고른다.

## Results
- Release build passed. All six factions at three difficulty levels passed deterministic pacing, first-attack grace, wave caps and intervals, worker targets and economy tests.
- Legacy Hard AI regression passed (composition, abilities, growth, arrival).
- Independent review found two P2 issues: low-difficulty waves skipped building-path recovery and could strand support. Both fixed with focused regressions.
- ContentVersion44; CoreCLR golden D7F7252F1ECBEC6D. Full Replay/Host and Unity checks in progress.

- Unity ViewSelfCheck passed, matching CoreCLR golden D7F7252F1ECBEC6D; includes difficulty defaults, selected profile, tutorial/menu isolation and earlier input regressions.


- Windows difficulty build passed (88,190,663 bytes). Hidden menu screenshot attempt did not complete within40seconds; only the capture process was stopped. Menu visual layout remains unverified in live play; existing player window was preserved.

- Full Replay and Host selfcheck passed. Extra arena run was stopped as redundant and is not claimed as passed. Live menu visual QA is still limited as noted above.
