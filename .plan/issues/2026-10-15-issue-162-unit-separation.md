# 2026-10-15 — 유닛 간 분리와 간단한 지역 회피

- Date: 2026-10-15
- GitHub Issue: #162
- Status: Implemented, awaiting review

## Goal

같은 셀 근방의 지상 유닛끼리 서로 밀어내서, 한 점에 겹쳐 서지 않고 좁은 램프에서
한 줄로 밀리지 않게 한다. 밀림은 길찾기의 결론을 뒤집지 않는다: 절벽·암반·물·
잔해·건물 셀로는 들어가지 않는다.

## Non-goals

- flow field, 집단 길찾기, 대열.
- 건물·노드와의 분리(이미 길찾기 장애물).
- 공중 유닛 간 분리.
- 밀려서 막힌 바디의 재탐색.

## Context / Constraints

- `Sim/World.cs` MoveSystem 은 id 순으로 한 걸음씩 걷고 `WalkableStep` 으로
  절벽 규칙을 묻는다. 분리는 그 뒤, 같은 시스템 안의 두 번째 pass 다.
- 로스터(`Sim/FactionData.cs` UnitStats)에 footprint 가 없다. 새 필드를 넣고
  Speed 가 있는 모든 행에 쓴다.
- 이동 규칙이 바뀌므로 `FactionData.ContentVersion` 28 → 29.
- 새 상태 필드는 없다. 버킷과 push 는 tick 안에서만 쓰는 scratch 고, Position 과
  Target 은 이미 hash 에 있다.

## Approach (Checklist)

- [x] **Step 0: Recon** — MoveSystem, WalkableStep, 로스터 행, TerrainChecks.
- [x] **Step 1: 로스터** — `UnitStats.Footprint`, `FactionData.BodyRadius`,
      ContentVersion 29.
- [x] **Step 2: 분리** — `Sim/Separation.cs` 의 `SeparationSystem`, MoveSystem 끝에서 호출.
- [x] **Step 3: 체크** — `Replay/SeparationChecks.cs`, Program.cs 등록, golden hash.

## 계약

```csharp
// Sim/FactionData.cs
public struct UnitStats { public Fix Footprint; }   // 바디 반경, 셀 단위
public static readonly Fix BodyRadius = Fix.Ratio(3, 8);  // 걷는 행의 기본값

// Sim/World.cs
public Fix Footprint(Entity e);   // 로스터 행의 Footprint
```

규칙:

- 참가자: Alive, Kind Unit 또는 Worker, Speed > 0, 공중 아님.
- 밀리는 쪽: 참가자이면서 Hold 아님, 성장 중 아님. Hold·성장 중인 바디는 남을
  밀어내되 자기는 서 있다.
- 이웃: 자기 셀과 8이웃 셀의 버킷. 버킷은 `bucketHead[GridCells]` 와
  `bucketNext[entity]` 배열이고, id 내림차순으로 push 하므로 순회가 id 오름차순.
- 밀림: 두 바디 거리 d 가 footprint 합 m 보다 짧으면 각자 `(m - d) / 2` 만큼
  서로 반대로. 같은 점이면 `(i + j) & 7` 번째 8방향으로, 낮은 id 가 +방향.
- 한 tick 의 총 밀림은 Speed / 2 로 clamp.
- 적용: `WalkableStep(pos, pos + push)`. 거부되면 X 성분만, 그 다음 Y 성분만.
  셋 다 거부면 그대로. 건물·노드가 선 셀로는 들어가지 않는다(이미 그 셀이면 허용).
- 떨림 방지: pass 시작 시점에 `Position == Target` 이고 경로가 끝난 바디는
  밀린 뒤 `Target = Position`. 걷던 바디의 Target 은 그대로.

## Validation

- **Commands to run:** `dotnet build` 후 `dotnet run --project Replay`,
  `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewSelfCheck.Run`
- **Expected output:** `OK: all determinism checks passed`, Mono 도 같은 golden hash.
- 새 체크(`Replay/SeparationChecks.cs`): 겹친 두 유닛이 분리되고 그 뒤 멈춘다,
  열 기 묶음이 점을 떠난다, Hold 바디는 밀리지 않는다, 절벽 가장자리의 분리가
  절벽을 넘기지 않고 암반·물에 서지 않는다, 건물·노드 셀로 들어가지 않는다,
  두 World 의 hash 가 같다, Speed 가 있는 모든 로스터 행에 footprint 가 있다.
- 비용 체크: 200 유닛을 열 셀에 몰아넣은 월드와 두 셀 간격으로 펼친 월드의
  tick 시간 차이(세 번 중 최소)가 1ms 미만. Debug 빌드에서 0.34ms, Release 에서
  약 0.15ms. 펼친 월드의 tick 자체가 Debug 에서 0.8ms 인 것은 전투 조준 탐색의
  기존 비용이라 이 이슈 밖이다.

## Risks & Rollback

- **Risks:** 걷는 바디가 옆으로 밀린 뒤 절벽 모서리에 걸려 멈출 수 있다(기존
  ponytail: 미끄러지지 않고 서는 규칙). 서 있는 바디는 걷는 바디에 tick 당
  Speed 절반씩 밀려난다 — 한 칸 틈에 선 바디는 지나가는 바디에 밀려 틈 밖까지
  간다(`RemnantsBlockTheOwnersOwnUnits` 의 control run 이 그 경우라 도착 판정을
  한 셀 이내로 바꿨다). 비용 체크는 벽시계 시간이라 아주 느린 CI 에서 흔들릴 수
  있다.
- **Rollback steps:** `git revert`. 새 상태 필드가 없으므로 되돌리면 hash 규칙은
  그대로고 golden hash 만 이전 값으로 돌아간다.

## Open Questions

- 없음.
