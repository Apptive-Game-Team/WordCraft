# 2026-10-16 — 분리 pass 시간 검사가 기계 부하에 따라 실패하지 않게 적용

- Date: 2026-10-16
- GitHub Issue: #216
- Status: In progress

## Goal

`dotnet run --project Replay` 의 통과 여부가 기계 부하에 따라 바뀌지 않게 한다.
`SeparationChecks.TwoHundredBodiesStepInsideTheBudget` 는 packed 200기와 spread
200기의 틱당 wall-clock 시간 차이가 1.0 ms 를 넘으면 실패한다. 한가한 기계에서는
0.3 ms 안팎이지만, 다른 dotnet 실행이나 Unity batch 가 돌면 1.0 ms 에서 1.7 ms 까지
벌어진다.

## Non-goals

- 분리 pass 자체의 최적화.
- `Sim/` 변경.

## Approach (Checklist)

- [ ] **Step 1: 측정** — best-of-three 를 best-of-five 로 바꾼다. 비교는 차이의
      절대값이 아니라 비율로 한다: packed 틱 시간이 spread 틱 시간의 2 배에
      0.2 ms 바닥을 더한 값보다 작아야 한다. 부하는 두 측정을 같이 늘리므로 비율이
      덜 흔들린다.
- [ ] **Step 2: opt-in gate** — 수치는 매 실행에 출력한다. 상한을 넘으면 기본 실행은
      경고 한 줄만 출력하고, `dotnet run --project Replay -- --perf` 일 때만 실패한다.
      `Program.Main` 이 인자를 받아 `SeparationChecks.EnforcePerformance` 에 넣는다.
- [ ] **Step 3: 문서** — `CLAUDE.md` Check 절에 한 줄.
- [ ] **Step 4: 검증** — 부하 상태 10회, `--perf` 3회.

## Risks

- 기본 실행이 분리 pass 의 성능 회귀를 막지 못한다. 수치와 경고는 남고, 회귀 확인은
  `--perf` 로 한다.

## Rollback

이 PR 을 revert 한다. `Sim/` 과 golden hash 는 건드리지 않는다.
