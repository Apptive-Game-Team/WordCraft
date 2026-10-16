# 2026-10-16 — 종족 밸런스와 실전 AI 완성 검증

- Date: 2026-10-16
- GitHub Issue: #221 (depends on #211 and PR #225)
- Status: In progress

## Goal
실제 시작 자원과 맵에서 AI 생산·성장·도착·능력·인구 조건을 충족하고,
144경기에서 각 진영 40–60%, 자리 편향 10%p 이내, 무승부 5% 이하 및
6000틱 내 종료율 95% 이상을 확인한다. 실제 플레이어 조작과 화면도 검증한다.

## Non-goals
계정·랭킹 또는 공개 호스팅 추가. 한정된 AI 대전 결과를 모든 인간 대전의
완벽한 밸런스나 무결함의 증명으로 해석하지 않는다.

## Context / Constraints
PR #225의 온라인/AI/비주얼 구현을 기반으로 한다. 추가 worktree 없이 파일
소유를 나눠 native subagent를 사용한다. AI를 먼저 고정하고 이후 수치를 조정한다.
규칙 결함이 확인되면 수치로 숨기지 않고 별도 원인과 회귀검사를 기록한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 144경기 기준선, 실전 AI 실패 원인, 개막 가격과 자리 편향 분석.
- [x] **Step 1: Implementation** AI 조건 개선 후 6개 유닛의 9개 수치를 조정. 전후 표는 FACTION-MECHANICS에 기록.
- [ ] **Step 2: Tests** BalanceChecks, Replay, Host, 144경기, Unity 및 실제 플레이.
- [ ] **Step 3: Rollout / Rollback** 문서 수치·버전 갱신, PR과 검증 증거 제공.

## Validation
- **Commands to run:** dotnet build; Replay; Host selfcheck; Host arena -games 4;
  Unity ViewSelfCheck, BuildPlayer 및 실제 게임 조작.
- **Expected output:** 이슈 수용 조건 통과, 결정론 해시 일치, 실제 조작에서 오류 없음.

## Risks & Rollback
- **Risks:** 결정론적 단일 맵에 과적합, AI 변경에 따른 수치 결과 변동, 자리 편향.
- **Rollback steps:** 각 수치 조정과 버전/골든/문서 변경을 함께 revert.

## Open Questions
- 초기 배치·명령 순서가 자리 편향의 원인인지 측정으로 확인한다.
- 인간 진영은 근접 로스터가 없으므로 AI 역할과 실제 유닛 역할을 구분해 검증한다.

## Baseline

`Host arena -games 4`, 144경기, 모두 종료·복제 hash 일치. seed는 게임 결과의
다양성을 만들지 않으므로 같은 자리를 반복한 결과를 독립 표본으로 해석하지 않는다.

| 진영 | 승 / 경기 | 승률 | 개막 전투유닛 가격 합 |
|---|---:|---:|---:|
| TreeSpirits | 32 / 48 | 66.67% | 40 |
| Hellfire | 44 / 48 | 91.67% | 90 |
| WaterSlimes | 16 / 48 | 33.33% | 90 |
| RockGolems | 24 / 48 | 50.00% | 150 |
| Driftworlds | 12 / 48 | 25.00% | 80 |
| Humans | 16 / 48 | 33.33% | 130 |

피어 0/1 승수 44/100, 격차 38.89%p. 개막 가격 최대/최소 3.75배.
원본 로그는 `Client/Logs/balance-baseline-144.log`.
새 `arena -verify 1`은 전체 6000틱·4판 이상 조건과 승률/종료/무승부/자리
조건을 검사하며, 1틱 미완료 실행에서 예상대로 exit 1을 확인했다.

## Integrated v42 Results

- Release build: zero errors/warnings. Host selfcheck passed including lossy link, all 36 negotiated faction pairs, desync diagnostics and spectators.
- Full arena: 144 games, 140 finished (97.22%), 4 draws (2.78%), seats 72/68 (2.78pp), every twin hash matches. `-verify 1` exit 0.
- Wins out of 48: Tree 20, Hellfire 24, Water 28, Rock 28, Drift 20, Humans 20. Opening army prices 80/90/100/120/80/120, ratio 1.5.
- Actual AI conditions pass after tuning: all six simultaneous compositions; Tree growth 2, Drift arrivals 6; actual casts 2/1/13/1/0/3; longest cap stall 0.
- CoreCLR and Unity Mono independently produced `FAEC9B582915E10D` before golden update. Final Unity selfcheck passed with that golden.
- Updated old price/HP test expectations without weakening tier-gate or desync diagnostics. Mirror combat window is 2000 ticks; Drift's first death occurs at tick 865, beyond the old 800-tick window.
- Some individual matchups remain one-sided. The 144 games repeat 36 deterministic seat configurations, not independent randomized matches.
- Changed UI actual-play validation and full final regression remain in progress.
