# 2026-10-15 — 지형 고도와 경사 길찾기

- Date: 2026-10-15
- GitHub Issue: #158
- Status: Implemented, awaiting review

## Goal

셀마다 높이를 주고, 그 높이를 길찾기가 읽게 한다. 높이차 1은 걸어 올라가는
경사이고 2 이상은 못 넘는 절벽이다. 동시에 길찾기를 8방향 binary heap A\* 로
올려서 유닛이 격자 계단이 아니라 비스듬히 걷는다.

## Non-goals

- 고도에 따른 사거리·시야·명중 보정. 이동과 통행만.
- local avoidance, flow field, 집단 길찾기.
- 3D 렌더링(#159).

## Context / Constraints

- `Sim/World.cs` 의 `TileKind` ponytail 주석이 이미 지적한 자리다. 균일 비용과
  Manhattan 휴리스틱은 모든 열린 칸이 같은 비용일 때만 admissible 하므로, 고도
  비용과 휴리스틱을 같이 고쳐야 한다.
- 이웃 순서는 네트워크 계약이다. 8방향으로 늘리면서 순서를 문서에 남긴다.
- 높이는 맵 생성 시점에 정해지고 매치 중 변하지 않는다. `World.Hash()` 에 들어
  가야 desync 가 탐지된다.
- `Client/Assets/Scripts/MatchScenario.cs` 가 맵을 칠한다. 180도 회전 대칭이
  구조적으로 보장되는 좌표계(sum, difference)로 쓴다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — `Sim/World.cs`(terrain, IsPassable, 경로 추종),
      `Sim/Pathfinder.cs`, `Replay/Program.cs` 의 지형·경로 체크.
- [ ] **Step 1: 고도 상태** — `World.MaxHeight`, `byte[] heights`,
      `HeightAt(cell)`, `SetHeight(x, y, level)`, `StepPassable(from, to, air)`,
      `Hash()` 반영.
- [ ] **Step 2: Pathfinder** — 8방향, binary heap, octile 휴리스틱, 경사 비용,
      코너 컷 금지, 경로 다듬기.
- [ ] **Step 3: 맵** — `MatchScenario.PaintHeights`, 램프, 능선.
- [ ] **Step 4: 체크** — `Replay/TerrainChecks.cs`, golden hash 갱신.

## 계약 (다른 작업이 의존하는 공개 API)

```csharp
// Sim/World.cs
public const int MaxHeight = 3;                       // 레벨 0..3
public int HeightAt(int cell);                        // 0..MaxHeight
public void SetHeight(int x, int y, int level);        // 시나리오 전용, 첫 tick 전
public bool StepPassable(int from, int to, bool air);  // IsPassable(to) + 경사 규칙
```

- 높이차 2 이상 = 절벽. 지상 통행 거부. 공중은 전부 무시.
- 걸음 비용: 직선 10, 대각 14, 한 레벨 오르막 +6(내려가는 쪽은 가산 없음).
- 휴리스틱: octile 거리 × 10. 한 걸음 최소 비용이 10이므로 admissible 하다.
- 이웃 순서: N, E, S, W, NE, SE, SW, NW. 직교가 먼저라 같은 비용이면 직교가
  이긴다.

## 구현에서 계약과 달라진 점

- 대각 규칙이 더 엄격하다. 대각 한 걸음은 출발 셀에서 양옆 두 셀로 갈 수 있어야
  할 뿐 아니라, 그 양옆 셀에서 목표 셀로도 갈 수 있어야 한다. 출발 1, 옆 0,
  목표 2 같은 배치에서 느슨한 규칙은 길을 내주지만 걸어가는 바디는 옆 셀을
  돌다가 거부당해 멈춘다.
- 다듬기는 레벨을 바꾸지 않는다. 한 걸음보다 긴 직선은 출발점과 같은 레벨의
  셀만 지난다. 비용을 모르는 string-pulling 이 탐색이 일부러 돌아간 비탈을
  가로지르는 것을 막는다. 첫 중간점은 항상 탐색의 첫 걸음이다.
- "레벨 1 고지와 램프 하나"는 계약상 성립하지 않는다. 1레벨 차이는 모든 변이
  걸어 올라가는 비탈이다. 그래서 모든 고지는 레벨 2이고, 레벨 1 램프가 유일한
  오르막이다.
- `FactionData.ContentVersion` 27 → 28. 맵과 이동 규칙이 함께 바뀌었다. replay
  파일의 `FormatVersion` 은 바이트 배치가 같아 1 그대로다.
- 맵: 능선(spine ≤ 1, lane 10..17 밖) 레벨 3. 두 lane 입구 양옆(spine 2..8,
  lane 19..24) 레벨 2 고지 넷, 각각 자기 기지 쪽을 보는 램프(spine 9..10,
  lane 20..23) 하나. 측면 메사 x 45..50, y 7..11 레벨 2, 램프 (51,8)·(51,9) 가
  중립 꼬마돌 (54,6) 을 본다. 전부 180도 회전 대칭.

## Validation

- **Commands to run:** `dotnet build` 후 `dotnet run --project Replay`
- **Expected output:** `OK: all determinism checks passed`
- 새 체크: 절벽 거부, 경사 허용과 비용, 코너 컷 거부, 다듬은 경로 전 구간 통행
  가능, 맵 고도 회전 대칭, 공중 유닛의 고도 무시, 두 시작 지점의 경로 길이 동일.

## Risks & Rollback

- **Risks:** 이웃 순서와 비용이 바뀌어 기존 replay 재생 결과가 달라진다. golden
  hash 갱신이 필요하고, 그 갱신이 맞는지는 Replay 전체 통과로만 보장된다.
  경로 다듬기가 통행 불가 구간을 가로지르면 유닛이 벽을 통과한다 — 다듬은 뒤
  전 구간 재검사가 그 방어선이다.
- **Rollback steps:** `git revert`. 고도는 더해지는 상태이므로 `SetHeight` 를
  부르지 않으면 전 맵이 레벨 0이고 동작이 이전과 같다(단 비용 체계는 다르다).

## Open Questions

- 없음.
