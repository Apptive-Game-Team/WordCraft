# 2026-10-16 — 테크트리 로스터 행 추가

- Date: 2026-10-16
- GitHub Issue: #180
- Status: In progress

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

- `feat/179`(PR #190) 위에 쌓고 `feat/183`(PR #185)을 merge한 상태다.
- `Sim/` 결정론 규칙. 새 엔티티 필드 없음. `upgradeLevels`는 이미 해시되며 행 수 상한
  `MaxUpgrades`가 4에서 8로 늘어 해시하는 칸이 늘어난다.
- 스프라이트 열은 `.art/TECH-TREE-ART.md`의 영어 리소스 이름이다. PNG는 아직 `Client/Assets`에 없다.
- 명령 카드 생산 칸은 이 base에서 6칸이고 페이지가 없다(#169, PR #171이 아직 merge되지 않았다).

## Approach (Checklist)

- [ ] Step 1: 조회 함수와 연구 행 구조(`UpgradeEffect` 추가, 항목별 연구 표, `MaxUpgrades` 8).
- [ ] Step 2: 진영별 로스터 행과 연구 행(진영마다 커밋 하나).
- [ ] Step 3: `ContentVersion` 33, 골든 해시 확인.
- [ ] Step 4: 하네스. `Replay/RosterChecks.cs`, 기존 검사 갱신.
- [ ] Step 5: `ViewSelfCheck.RosterArtResolves`가 컨셉 시안만 있는 이름을 받게 한다.
- [ ] Step 6: 문서. `docs/FACTIONS.md` 표, `docs/TECH-TREE.md` 상태 표기.
- [ ] Step 7: 검증. build, Replay, selfcheck, arena, Unity ViewSelfCheck.

## Validation

- `dotnet build` (경고 0)
- `dotnet run --project Replay`
- `dotnet run --project Host -- selfcheck`
- `dotnet run -c Release --project Host -- arena -games 2`
- Unity `WordCraft.View.ViewSelfCheck.Run`

## Implementation Notes

## Follow-ups
