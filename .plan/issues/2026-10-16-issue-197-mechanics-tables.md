# 2026-10-16 — 메커니즘 일반화: 유닛 성장, spawner 표, 도착 제공자 표, 씨앗 요람, 잔해

- Date: 2026-10-16
- GitHub Issue: #197
- Status: In progress (draft PR, #196 위에 쌓음)

## Goal

`docs/TECH-TREE.md` 구현 순서 묶음 4. 여섯 메커니즘 중 넷이 0번 슬롯 상수 대신 표를 읽는다.
새 해시 필드는 없다. `ContentVersion` 34 → 35.

## API contract (`Sim/FactionData.cs`)

| 표 | 행 모양 | 행 |
|---|---|---|
| `growthTargets` | `(Faction, Role, Slot, GrowthCost)`, `GrowthCost`에 `From (Role, Slot)` 추가 | 기존 다섯 행은 `From = (Worker, 0)`. 잎날 정령(Melee 0) → 폭풍 기수 60/60, 번개 정령(Ranged 0) → 번개새 무리 60/50, 덩쿨 정령(Ranged 1) → 거대 덩쿨 100/120 |
| `spawners` | `SpawnerRow { Faction, Role, Slot, ChildRole, ChildSlot, Ticks, Cap, TicksEffect, TicksResearched, CapEffect, CapResearched }` | 군단장(Signature 0) → 자손(Ranged 0) 100틱·5기, 연구 `OffspringPeriod` 70·`OffspringCap` 7. 산란 악마(Signature 1) → 잿불 새끼(Melee 3) 120틱·3기, 연구 `BroodCap` 4 |
| `arrivals` | `ArrivalRow { Faction, Role, Slot, Passage, Radius, RadiusEffect, RadiusResearched }` | 통로·깊은 통로 `Passage`. 굴절 기둥 6·경계 운반자 6(연구 `ArrivalRadius` 8), 위상 닻 3(연구 `PhaseAnchorRadius` 4), 떠도는 섬 4, 틈새 포대 0 |
| `freeQueues` | `FreeQueueRow { Faction, Role, Slot, GiveRole, GiveSlot, Radius, RadiusEffect, RadiusResearched }` | 씨앗 요람(Production 1) → 풀씨 정령(Worker 0), 반경 8, 연구 `SeedCradleRadius` 12 |
| `remnantShapes` | `(Faction, Role, Slot, (Dx, Dy)[])` | 고대 골렘(Signature 0): `(-1,0) (0,0) (1,0)`. 그 밖의 돌 골렘 유닛은 `(0,0)` |

연구 행 셋(묶음 4 배치 표): 씨앗 요람 반경(뿌리 회당, T3, 선행 씨앗 요람, 100/160),
산란 상한(재 분화구, T2, 100/160), 위상 닻 반경(외피 연마장, T2, 100/160).
세계수 정령과 차원 유랑종이 9행이 되어 `MaxUpgrades` 8 → 9.

## Decisions

- 고대 골렘 잔해는 문서의 3칸(자기 셀과 x−1, x+1)을 따른다. 이슈 체크 목록은 "상하좌우 4셀"(5칸)이라고
  적었지만 문서 네 곳(정체성, 잔해 규칙, 능력 표, 잔해 흐름)과 이슈 Problem 절이 모두 3칸이고,
  5칸 모양은 굳기의 것이다. 모양은 표 한 줄이라 바꾸기 쉽다. PR에 적는다.
- 도착 반경 연구(6 → 8)는 문서대로 반경 6인 두 행에만 걸린다. 위상 닻은 자기 연구(3 → 4)를 갖는다.
- 씨앗 귀환은 세계수 정령의 `Kind == Unit` 몸이 죽을 때 `Kill` 안에서 대기열에 넣는다. 사망 몸의
  인구를 먼저 돌려준 뒤 상한을 본다. 요람은 같은 주인, 살아 있고 완공, 반경은 `WithinRange`,
  가장 가까운 것(같으면 낮은 id). 대기열 5 또는 인구 상한이면 버린다. `CancelProduction` 거부.
- 도착 셀 검사는 `ArrivalValid(peer, point, air)` 안에 둔다. 지상 몸은 `IsPassable(cell, false)`이고
  그 셀에 건물이나 자원 노드가 없어야 한다. 두 인자 판은 지상 판정이다.
- 성장 중인 유닛은 공격하지 않는다(`CanAttack`에 `!IsGrowing`). 일꾼은 원래 공격하지 않았다.
- 잔해 쓰기는 모두 `max`, 열린 지형 셀만, 맵 밖은 건너뛴다.
- `MaxUpgrades`가 9가 되어 해시하는 `upgradeLevels` 칸이 피어마다 하나 늘어 골든 해시가 움직인다.

## Validation

`dotnet build`, `dotnet run --project Replay`(새 `Replay/MechanicsChecks.cs`), `Host selfcheck`,
`Host arena -games 2`, Unity `ViewSelfCheck`.
