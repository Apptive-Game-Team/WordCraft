# 2026-10-16 — 공사 중 피해 누적과 로스터를 따르는 개막 병력

- Date: 2026-10-16
- GitHub Issue: #191, #154
- Status: Implemented (draft PR, #194 위에 쌓음)

## Goal

- #191: `ConstructionSystem`이 체력을 진행도에서 덮어쓰지 않고 틱마다 정수 증가분을 더한다.
  받은 피해가 남고, 0 이하가 된 공사장은 `Kill`로 죽는다. 새 상태 필드는 없다.
- #154: `MatchScenario.Build`의 개막 유닛 둘이 `FactionData.OpeningFighter(faction, index)`가
  답하는 로스터 항목이 된다. 인간은 이름 없는 근접 0번을 놓지 않는다.
- `ContentVersion` 33 → 34.

## Decisions

- 증가분은 `(MaxHp - 1)`을 건설 시계에 나눠 틱 번호로 계산한다. 공사장이 체력 1로 세워지므로
  `MaxHp`를 나누면 피해 없이 `MaxHp + 1`이 되어 clamp가 피해 d를 `MaxHp - d + 1`로 읽게 한다.
- `OpeningFighter`는 이름이 있고 무장한 첫 항목을 고른다. 근접 우선은 근접 → 원거리 →
  시그니처, 원거리 우선은 원거리 → 근접 → 시그니처, 슬롯은 낮은 쪽부터.
  "생산되는 항목만"이라는 조건은 걸지 않는다. 지옥불 원거리 0번(자손)은 생산되지 않지만 지금도
  개막에 서는 몸이라, 조건을 걸면 세계수 정령·지옥불이 예전 행과 같아야 한다는 요구와 부딪힌다.
- 대칭 검사는 거울 맞은편을 같은 진영끼리는 Kind·Role·Slot으로, 진영이 다르면 Kind와 위치로만
  비교한다. 진영마다 개막 항목이 다를 수 있어서다.

## Validation

`dotnet build`, `dotnet run --project Replay`, `Host selfcheck`, Unity `ViewSelfCheck`,
`arena -games 2`. 새 검사는 `Replay/ConstructionChecks.cs`, `Replay/OpeningArmyChecks.cs`.
