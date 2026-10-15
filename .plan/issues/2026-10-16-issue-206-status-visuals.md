# 2026-10-16 — 상태 이상·오라·연쇄의 필드 비주얼

- Date: 2026-10-16
- GitHub Issue: #206
- Base: `feat/206` = `fix/201`(PR #204) + `feat/200`(PR #205). 이 PR 은 두 PR 위에 쌓인다.
- Status: Implemented (draft PR, #204 과 #205 위에 쌓음)

## Goal

#200 이 시뮬레이션에 넣은 감속, 화상, 치유 오라, 연쇄, 피해·공격 주기 오라, 화상 오라를
필드에서 보이게 한다. 뷰는 `SlowTicksLeft`, `BurnTicksLeft`, 체력 변화, `AttackCooldown` 리셋,
능력 표만 읽는다. `Sim/` 은 건드리지 않는다.

## 방법

| 효과 | 읽는 것 | 그리는 것 |
|---|---|---|
| 감속 | `SlowTicksLeft > 0` | 발밑 ring decal(세계수 `Leaf`), alpha 는 남은 틱 / 40. hull 1.5% 축소 |
| 화상 | `BurnTicksLeft > 0` | 머리 위로 떠오르는 `Ember` 조각 2~3개(풀) |
| 화상 피해 틱 | `BurnTicksLeft` 가 10의 배수를 지남 | 작은 impact(shard 재사용, `Ember`, 크기 0.55배) |
| 치유 | 체력이 오르고 `MaxHp` 그대로, 공사 중이 아님 | 연녹색(`Good`) mote 2개와 짧은 ring |
| 치유 원천 선택 | 능력 표의 `HealAura` 행 | 반경 ring, `Good` |
| 다른 오라 원천 선택 | `ArmorAura`, `MarkAura`, `RateAura`, `GrowthAura`, `SlowAura` 행 | 반경 ring, `Accent`, 얇게 |
| 화상 오라 | `BurnAura` 행 원천 | 선택과 무관하게 희미한 `Ember` ring, 반경 2칸 |
| 연쇄 | 공격자가 해금된 `ChainOnHit` 행, 같은 프레임에 체력이 준 적 몸 | 1차 대상에서 가장 가까운 순으로 지그재그 tracer(`Bolt`). 근사이며 주석에 적는다 |

- 상태와 체력은 엔티티마다 지난 프레임 값을 하나의 병렬 리스트에 둔다. 안개 속에서도 갱신한다.
- 새 풀: 지그재그 bolt 와 불씨. 둘 다 처음 모자랄 때만 만든다.
- `strip-<faction>-status`: 시나리오 월드를 실제 `MatchView` 로 그린다. 감속된 몸, 불타는 몸,
  치유 원천과 다친 아군(`hpOverride`), 진영의 오라 원천(선택), 연쇄를 가진 진영은 연구를 끝낸
  연쇄 공격자와 적 몸 셋. 선택은 `Selection` 을 harness 에서 만들어 넣는다.
- `strip-match-status`: 스크립트 로그에서 묶음 5 효과가 발동하는 틱을 찾고, 없으면 쓰지 않는다.
- `strip-<faction>-chain`: 연쇄 연구를 끝낸 공격자와 2칸 안쪽 간격의 적 건물 셋.

## 결과

- 치유 확인용 아군은 `hpOverride` 로 만들 수 없다. `World.Add` 가 `MaxHp` 도 같은 값으로 두므로
  덜 다친 몸이 아니라 약한 몸이 된다. Replay 의 `Wound` 처럼 시나리오 화상으로 체력의 절반을 깎고
  나서 치유 원천을 세운다.
- 첫 strip 에서 선택된 몸과 거의 가득 찬 체력바의 채움이 검은 바탕 뒤로 숨는 것을 봤다. 둘 다 깊이
  검사가 없는 overlay 이고 같은 자리에 있어서 카메라 거리 정렬이 순서를 정했다. 채움 billboard 에
  `sortingOrder` 1 을 주어 고쳤다(별도 commit).
- 연쇄 strip 의 표적 간격이 처음에 2.02칸이라 시뮬레이션이 튀지 않았다. 1.87칸과 1.79칸으로 줄였다.
- 스크립트 로그는 감속, 화상, 치유, 연쇄를 하나도 일으키지 않는다. 그래서 `strip-match-status` 는
  쓰지 않는다. 첫 탐색은 공사가 끝나는 틱의 체력 상승을 치유로 읽었고, 직전 틱의 공사 여부를 같이
  보도록 고쳤다.
- 연쇄를 가진 진영(세계수 정령, 인간)은 `strip-<faction>-chain` 을 따로 쓴다. 지옥불 군단과 인간은
  치유 오라 행이 없어 status strip 에 치유가 없다.

## Non-goals

광역·물 지대·소환 만료 비주얼, HUD 능력 버튼, 사운드, `Sim/`, `CLAUDE.md`.

## Validation

`dotnet build`, Unity `ViewShots -shotsOnly status,match`, Unity `ViewSelfCheck`.
