# 2026-10-16 — 테크트리 로스터 행 추가

- Date: 2026-10-16
- GitHub Issue: #180
- Status: Implemented (draft PR)

## Goal

`docs/TECH-TREE.md` `## 구현 순서` 묶음 3을 구현한다. 여섯 진영이 유닛 12종, 건물 9종 이상을
`FactionData.Has`로 가진다.

- `## 슬롯 대조`의 추가 열 전부를 `extras` 끝에 진영·역할 순서로 붙인다. 슬롯 번호는 문서와 같다.
- 각 행의 스탯(체력, 방어력, 피해, 사거리, 주기, 속도, `Footprint`, `Air`, `HitsAir`, `HitsGround`),
  생산 가격·틱, 건설 가격·틱, 단계, 선행, 생산 건물을 문서 표에서 옮긴다.
- 세계수 정령의 성장 행 중 출발점이 풀씨 정령인 셋(찌릿쥐, 도깨비불, 덩굴 군집)을 `growthTargets`에 넣는다.
- 잿불 새끼, 깨어난 바위는 `Produced = false`이고 `Has`는 참이다.
- 묶음 1의 임시 지정 둘(바람 정령 선행, 마도 정찰기 생산 건물·선행)과 미뤄 둔 둘째 생산 건물 넷
  (지옥불 군단장 큰 균열, 구름 용 구름 둥지, 고대 골렘 각성의 원, 공수 특공대 석조 병영)을 바꾼다.
- `연구 행 배치` 묶음 3의 연구 행 20개와 조회 함수 `RangeFor`, `AttackTicksFor`, `SpeedFor`, `ArmorFor`.
- `Replay/RosterChecks.cs`, `ContentVersion` 32 → 33, `docs/FACTIONS.md` 표 갱신.

## Non-goals

- 능력과 능력 해금 연구(묶음 5~8). 성장 출발점이 유닛인 행(폭풍 기수, 번개새 무리, 거대 덩쿨)의
  성장 표(묶음 4, M6). spawner 표, 도착 표(묶음 4).
- 기존 행의 `방어` 열과 건물 방어력 1. 기존 행 전체에 걸리는 값이라 이 묶음의 범위 밖이다.
- `Models.cs` 바디, HUD 아이콘 PNG, `Host/`, `Net/`.

## Context / Constraints

- `feat/179`(PR #190) 위에 쌓고 `feat/183`(PR #185), `feat/174`(PR #171, #176), `feat/192`(PR #193)를 merge한 상태다.
- `Sim/` 결정론 규칙. 새 엔티티 필드 없음. `upgradeLevels`는 이미 해시되며 행 수 상한
  `MaxUpgrades`가 4에서 8로 늘어 해시하는 칸이 늘어난다.
- 스프라이트 열은 `.art/TECH-TREE-ART.md`의 영어 리소스 이름이다. PNG는 PR #193이 들여왔다.
- 명령 카드는 #169의 3×4 카드다. 생산·연구 칸 8개, 넘치면 `More`로 쪽을 넘긴다.

## Approach (Checklist)

- [x] Step 1: 조회 함수와 연구 행 구조(`UpgradeEffect` 추가, 항목별 연구 표, `MaxUpgrades` 8).
- [x] Step 2: 진영별 로스터 행과 연구 행(진영마다 커밋 하나).
- [x] Step 3: `ContentVersion` 33, 골든 해시 확인.
- [x] Step 4: 하네스. `Replay/RosterChecks.cs`, 기존 검사 갱신.
- [x] Step 5: 명령 카드와 아트. `feat/174`, `feat/192` merge, 건물별 목록과 쪽 넘김, 엄격한 아트 검사.
- [x] Step 6: 문서. `docs/FACTIONS.md` 표, `docs/TECH-TREE.md` 상태 표기.
- [x] Step 7: 검증. build, Replay, selfcheck, arena, Unity ViewSelfCheck.

## Validation

- `dotnet build` (경고 0)
- `dotnet run --project Replay`
- `dotnet run --project Host -- selfcheck`
- `dotnet run -c Release --project Host -- arena -games 2`
- Unity `WordCraft.View.ViewSelfCheck.Run`

## Implementation Notes

- 새 행 65개(유닛 41, 건물 24)를 `extras` 끝에 진영·역할 순서로 붙였다. `## 슬롯 대조`와 맞는지
  스크립트로 111칸을 대조했고 어긋난 칸은 0개였다.
- 연구 행 20개는 진영 표의 4번 이후에 붙는다. 진영별 행 수는 8, 7, 7, 6, 8, 8이다.
  `MaxUpgrades`가 4에서 8로 늘어 해시하는 `upgradeLevels` 칸이 늘었고, 골든 해시가
  `0xE77E7D309B69368D`에서 `0xF3DE07A6B0241E8D`로 움직였다. CoreCLR(`Replay`)과
  Unity Mono(`ViewSelfCheck`)가 같은 값을 냈다. 두 merge 뒤에도 값은 그대로다.
- 조회 함수 넷은 항목별 연구 표 `entryUpgrades`를 읽는다. `RangeOf`는 `RangeFor`로 이름을 바꿨고
  merge된 `Fog.cs`도 따라 고쳤다. `MoveSystem`이 `SpeedFor`를 읽고 `e.Speed`는 해시되는 값으로 남는다.
- 속도 0인 몸(거대 덩쿨)은 추격 분기에서 `Target`을 고쳐 쓰지 않는다.
- AI 사다리: 인구 건물을 연구와 생산보다 먼저 정하고, 인구 건물이 필요한데 짓는 중인 것이 없으면
  그 생각에서는 연구도 생산도 하지 않는다. 그 전에는 차원 유랑종 AI가 인구 상한에서 192틱,
  돌 골렘 AI가 115틱을 서 있었다. `RosterChecks`가 1500틱 안에 100틱 넘게 연속으로 서 있지
  않다고 고정한다. 지금은 여섯 진영 모두 0틱이다.
- 명령 카드: 생산 목록과 연구 목록이 선택한 건물 항목 기준이다(`ProductionMenu.For(faction, role, slot)`,
  `ResearchMenu.For(faction, role, slot)`, `Orders.LeadSlot()`). 8칸을 넘으면 `More`로 넘긴다.
  거품 생성기 10과 통로 9는 2쪽, 각성 바위 7은 1쪽이다.
- 아트: 52개 PNG가 들어와 `RosterArtResolves`는 엄격한 검사로 돌아갔다. 이끼 현자 `MossSage`,
  마도 정찰기 `ArcaneScout`.
- `Host/SelfCheck.cs` arena 쌍은 6000틱까지 두 판을 끝까지 두고, 승자가 아니라 두 판이 모두 끝나
  세어졌는지를 본다.
- 기존 검사 중 한 항목짜리 목록, 마법 탑 정찰기 임시 지정, 연구 행 4개를 전제하던 것을 새 로스터에
  맞게 고쳤다.

- 리뷰 반영: `Fog.cs`가 공중 여부를 몸의 항목으로 읽는다(`World.Flies`). 사거리가 `AcquireRange`(10)보다
  긴 몸은 그 사거리에서 적을 잡고 유지한다(`World.AcquisitionRange`). 연구한 거대 덩쿨은 11칸에서 잡는다.
  AI는 인구 건물 가격보다 보유 자원이 적고 일꾼이 있을 때만 모은다. arena selfcheck는 각 판을 좌석을
  적어 다시 두고, 승리가 좌석이 아니라 진영에 매겨졌는지 본다.

## Follow-ups

- 기존 행의 `방어` 열과 건물 방어력 1은 아직 코드에 없다.
- 폭풍 기수, 번개새 무리, 거대 덩쿨 성장 표(묶음 4), 잿불 새끼 산란(묶음 4), 깨어난 바위 깨우기(묶음 8).
- AI가 세계수 정령과 차원 유랑종의 새 행을 뽑지 않고, `Tech` 1번 이후를 짓지 않아 그 연구 행을
  쓰지 않는다(묶음 9).
