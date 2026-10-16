# 2026-10-16 — 물 지대·한시 소환·폭발·광역·전개의 필드 비주얼

- Date: 2026-10-16
- GitHub Issue: #212
- Base: `feat/212` = `feat/209`(PR #214, 묶음 8 `Cast`) + `feat/206`(PR #207, 상태 이상 비주얼). 이 PR 은 두 PR 위에 쌓인다.
- Status: In progress

## Goal

묶음 6·7·8 이 시뮬레이션에 넣은 효과를 필드에서 보이게 한다. 뷰는 `ExpireTick`,
`AbilityTicksLeft`, `AbilityPoint`, `Deployed`, `AbilityCooldown`, `World.WaterFieldExpiry(cell)`,
`World.RemnantExpiry(cell)`, 체력 변화, `AttackCooldown` 리셋, 능력 표만 읽는다. `Sim/` 은 건드리지 않는다.
풀만 쓰고 프레임당 할당이 없으며, 색은 `UiStyle` 토큰과 `BodyPalette` 견본에서만 온다.

## 방법

| 효과 | 읽는 것 | 그리는 것 |
|---|---|---|
| 물 지대 | `HasWaterField(cell)`, `WaterFieldExpiry(cell)` | 셀마다 사각 `Decal`(새 square 모양, 지형을 따라 4×4 로 나눈다) 하나를 풀에서 꺼낸다. 색은 `WaterSlimes.Pale`, alpha 0.38, 남은 틱이 40 아래면 0.12 까지 선형으로 옅어진다. 안개가 `Seen` 인 셀만. 미니맵은 새 토큰 `MinimapWaterField` 로 셀을 칠한다(잔해 다음, 점 아래) |
| 한시 소환 | `ExpireTick > 0`, `World.LifetimeFor` | 체력바 오른쪽 옆의 작은 미터(바탕 `MeterBack`, 채움 `InkDim`), 늘 보인다. 남은 틱이 60 이하이면 `BodyRig.Expiring` 이 `_Wash` 를 흰색 쪽으로 0.3 진폭으로 깜빡인다 |
| 사망·도착 폭발 | 죽은 몸 또는 이번 프레임에 생긴 몸의 `DeathBurst`·`ArrivalBurst` 행 | 반경까지 퍼지는 ring 하나(`Effects.Blast`)와 `Impact` 1.5배, 색은 `BodyPalette.Shot(faction)` |
| 용암 폭발·폭격 | 해금된 `SplashOnHit` 행의 몸이 쏨 | 표적 위에서 떨어지는 낙하 tracer, 닿을 때 반경 ring |
| 유성우·낙뢰·간헐천 | `DelayedStrike` 행, `AbilityTicksLeft`, `AbilityPoint` | 시계가 도는 동안 지점에 반경 ring(예고). 남은 틱이 6 이하가 되는 프레임에 낙하 tracer 를 떨어뜨려 시계가 0 이 되는 틱에 닿게 한다(유성우는 셋, 낙뢰는 위에서 내려오는 bolt). 닿으면 ring 과 burst. 간헐천은 떨어지지 않고 솟는다 |
| 해일 부름 | 시전자의 `AbilityCooldown` 이 뛰고, 시전 틱 + 300 을 만료로 가진 셀 | 그 셀들의 중심에 ring 과 burst |
| 칼바람 회오리·모래폭풍 | `StrikeZone`·`SandZone` 행, `AbilityTicksLeft > 0`, `AbilityPoint` | 지점을 도는 입자 고리(프레임마다 다시 그리는 chip 풀)와 옅은 반경 ring. 칼바람 회오리는 10틱마다 치는 순간 작은 ring |
| 잔해 투척 | 해금된 `RemnantThrow` 행의 몸이 쏘는 프레임에 0 으로 바뀐 잔해 셀 | 그 셀에서 표적까지 날아가는 chunk tracer(일반 tracer 대신), 닿을 때 반경 1칸 ring |
| 잔해 수리 | `RemnantRepair` 행 몸의 `AbilityCooldown` 이 뜀, 0 으로 바뀐 잔해 셀, 체력이 오른 아군 | 그 셀에서 치유된 몸까지 chunk tracer. 치유 표시는 #206 의 것을 그대로 쓴다 |
| 수리(공방 기술자) | 체력이 오른 건물·장치 | #206 의 치유 표시가 이미 그린다 |
| 전개 | `Deploy` 행, `AbilityTicksLeft`, `Deployed` | `BodyRig.Deploy(settle)`: 0 에서 1 로 hull 이 낮아지고 넓어지며, 맨 위 부품(crown, 없으면 arm)이 내려앉는다. 전환이 끝나 전개되는 순간 먼지 ring. 선택된 몸의 사거리 ring 은 전개 전 사거리에서 전개 사거리(10)까지 같은 비율로 커진다 |

- 지난 프레임 값: `Vital` 에 `Clock`(`AbilityTicksLeft`), `Cooldown`(`AbilityCooldown`) 을 더한다. 잔해 격자는
  프레임마다 "잔해가 있었다" bool 4096 칸을 갖는다. 할당은 처음 한 번.
- 표시는 시전자가 로컬 피어이거나 지점 셀이 `Seen` 일 때만. 안개 속 폭발이 위치를 알려주면 안 된다.
- 새 풀: 물 지대 decal, 예고·구역 ring decal, 수명 미터 billboard 두 장(엔티티마다 처음 쓸 때), 궤도 chip.
  chunk tracer 는 tracer 슬롯마다 chunk renderer 를 하나 더 두어 `Incoming`·`Landed` 를 그대로 쓴다.
- `strip-<faction>-cast`(6개): 세계수 칼바람 회오리, 지옥불 유성우, 물 슬라임 간헐천, 돌 골렘 모래폭풍과
  잔해 투척, 차원 유랑종은 액티브가 없어 한시 소환 만료와 화염 분출, 인간 낙뢰와 견인 대포 전개.
  `strip-waterslimes-field`: 물방울 생존자의 물 지대 깔기와 만료가 다른 지대 셀들. `-shotsOnly` 에 `cast`
  종류를 더하고, 물 지대 strip 은 `field` 에 든다.

## Risks

- 낙하 tracer 와 예고 ring 은 틱 시계를 초로 바꿔 맞춘다. 프레임이 밀리면 닿는 순간이 피해 틱과 조금 어긋난다.
- 해일 부름 지점은 만료 값으로 추정한다. 같은 틱에 같은 만료의 물 지대 깔기가 7칸 안에서 겹치면 중심이 밀린다.
- 견인 대포에는 crown 이 없다(`Models.cs` 는 범위 밖). 포신인 arm 이 crown 역할을 한다.

## Non-goals

HUD 버튼(#210), 사운드, `Sim/`, `Models.cs`, `CommandCard.cs`, `Hud.cs`, `Orders.cs`.

## Validation

`dotnet build`, `dotnet run --project Replay`, Unity `ViewSelfCheck`, Unity `ViewShots -shotsOnly cast,field`
(세 번 이상 보고 다듬는다).
