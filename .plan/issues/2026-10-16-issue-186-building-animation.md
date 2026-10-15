# 2026-10-16 — 건물 건설·활동·파괴 애니메이션과 검증 strip

- Date: 2026-10-16
- GitHub Issue: #186
- Base: `feat/182` (PR #184, 안에 #175·#176·#173·#171). 이 PR 은 #184 위에 쌓인다.
- Status: In progress

## Goal

기지가 정물로 읽히지 않게 한다. 건물이 짓는 동안 부품 단위로 조립되고, 생산·연구
중에는 움직이고, 죽을 때 부서져 내려앉는다. 세 동작을 `ViewShots` strip 으로 먼저
찍고, 그 strip 을 보면서 다듬는다.

## Non-goals

- 유닛 애니메이션, HUD, 사운드.
- 셰이더, 애셋 파일, 시뮬레이션 변경. 뷰는 `Entity` 를 읽기만 한다.
- `FieldMesh.cs`, `FieldProps.cs`, `FieldPalette.cs`, `Sky.cs`, `Field.cs`,
  Ground/Water 셰이더 (#187 이 같은 시간에 고친다).

## Context / Constraints

- 건물 hull 은 지금 mesh 하나다. 부품이 순서대로 올라오려면 hull 이 여러 조각으로
  나뉘어야 한다. `Solids` 는 쓰기 범위 밖이므로 `Models` 의 builder 가 primitive 를
  여러 `Solids` 에 나눠 쓰게 한다 (`b.Tier(origin)`).
- 색 literal 은 `BodyPalette`, `UiStyle` 에만 있다. 이 PR 은 거기서 읽기만 한다.
- 프레임당 할당 0. 입자, 먼지 ring, 덩어리는 `Effects` 의 pool 에서 꺼낸다.
- 시계는 `ViewClock`. strip 은 시계를 잡고 rig 를 직접 돌린다.

## 설계

### 부품 (`Models`)

- `Blueprint.Tier(origin)`: 새 `Solids` 를 만들어 tier 목록에 넣고 돌려준다. builder 는
  `hull = b.Tier(...)` 로 다음 부품을 쓰기 시작한다. tier 0 은 기존 `Hull` 이다.
- `Design` 에 `Tiers`(mesh 배열), `TierPivots`, `TierHeights`, `Assembly`(진영별 방식),
  `Debris`(덩어리와 먼지 색), `Glow`(파괴 때 번쩍이는 안쪽 빛, 없으면 alpha 0) 를 더한다.
- tier 는 hull 의 자식이라 피격 flinch 와 발사 반동을 그대로 탄다.
- 본진, 생산, 방어, 인구, 기술 건물 30종에 tier 를 2~4개 둔다.

### 건설 (`BodyRig.Progress`)

진행도 0~0.9 를 hull tier 수로 나눠 tier 마다 한 구간씩, 0.9~1.0 은 crown (없으면
마지막 tier) 이 쓴다. 구간 안의 진행 u 로 tier 의 위치·회전·크기를 정한다.

| 진영 | 방식 | 부품 하나의 움직임 |
|---|---|---|
| 세계수 정령 | 자람 | 가는 상태에서 y 0 → 1, xz 0.35 → 1, 끝에 6% 넘침 |
| 지옥불 군단 | 찢고 솟음 | 자기 높이만큼 땅 아래에서 떨리며 올라옴 |
| 물 슬라임 | 부풂 | 크기 0 → 1.15 → 0.95 → 1 탄성 |
| 돌 골렘 부족 | 쌓임 | 위에서 떨어져 내려와 착지 squash |
| 차원 유랑종 | 외피판 닫힘 | 바깥으로 70도 열린 판이 좌우 번갈아 닫힘 |
| 인간 마법 문명 | 층이 쌓임 | xz 그대로, y 0 → 1 로 한 층씩 올라감, 넘침 없음 |

완공 순간 (진행도가 1 에 닿는 프레임, 직전 관측이 0.5초 안일 때만) hull 이 0.3초
눌렸다 펴지고 `Effects.Dust` 가 땅에 ring 을 한 번 퍼뜨린다. 지어지는 동안의 반투명
(SiteAlpha 0.75) 은 그대로 둔다. root 를 y 로 늘이던 `siteHeight` 는 없앤다.

### 활동 (`BodyRig.Work`, `Deliver`)

- `MatchView.Pose`: 완공된 건물이 `QueueCount > 0` 또는 `ResearchIndex >= 0` 이면
  `Work(true)`.
- crown 이 스스로 도는 건물 (`CrownSpin != 0`) 은 회전이 0.4초에 걸쳐 3.5배로 빨라진다.
  각도를 시간에 곱하지 않고 누적해서 속도가 바뀌어도 튀지 않는다.
- 나머지는 hull 이 1.5% 맥동하고 0.4초마다 진영색 입자 하나 (`Effects.Mote`) 가 지붕
  근처에서 1초 동안 떠오르며 작아진다. 동시에 2~3개가 보인다.
- 생산 완료: `QueueCount` 가 줄어든 프레임에 같은 주인의 새 유닛이 생겼으면, 가장
  가까운 새 유닛 쪽으로 `Deliver(yaw)`. hull 이 0.4초 동안 8% 부풀고 그쪽으로 7도
  기운다. 취소로 줄어든 queue 는 새 유닛이 없으므로 반응하지 않는다.

### 파괴 (`BodyRig.Fall`, 건물 경로)

0.8초. 0~0.2초 흔들림, 0.15초에 `Effects.Crumble` 이 진영색 덩어리 2~4개를 던지고,
위쪽 tier 와 crown 이 서로 다른 방향으로 기울며 떨어지고, 0.3초에 먼지 ring, 0.45초
부터 남은 것이 가라앉고 마지막 30% 에서 흐려진다. 지옥불과 유랑종은 첫 0.12초에
`_Wash` 로 안쪽 빛 색이 번쩍이고, 그 뒤 어두운 색으로 꺼진다.

### Effects

- `Crumble(foot, size, colour, yaw, delay, count)`: 덩어리 수를 받는다 (기본 3).
- `Dust(at, radius, colour, delay)`: impact ring pool 을 shard 없이 쓴다. 반지름과 길이를
  burst 마다 들고 있다.
- `Mote(at, colour)`: 떠오르는 입자 pool, 처음 12개.

### strip (`ViewShots`)

- `strip-<faction>-build`: 본진, 생산, 방어. 진행도를 0.77초에 0 → 1 로 밀고, 마지막
  프레임에 완공 ring 과 settle 이 보인다.
- `strip-<faction>-work`: 본진, 생산, 기술. 방어 건물은 생산도 연구도 하지 않아서
  기술 건물로 바꾼다. 0.45초에 본진과 생산 건물이 유닛을 내보낸다 (`Deliver` 와
  유닛 `Spawn`).
- `strip-<faction>-destroy`: 본진, 생산, 방어.
- 6진영 x 3 = 18장.

## Approach (Checklist)

- [ ] **Step 1** — `ViewShots` 에 build, work, destroy strip (지금 코드 그대로 찍어 기준 확보)
- [ ] **Step 2** — `Models` tier 와 `Design` 필드, `BodyRig` 조립
- [ ] **Step 3** — `Effects.Dust`, 완공 ring 과 settle
- [ ] **Step 4** — 활동: `Work`, `Deliver`, `Effects.Mote`, `MatchView` 연결
- [ ] **Step 5** — 파괴: tier 붕괴, `Crumble` 확장, 안쪽 빛
- [ ] **Step 6** — strip 을 보며 세 번 이상 다듬기 (64px 축소본으로 판단)

## Validation

- `dotnet build`
- `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewSelfCheck.Run`
  → `OK: all view determinism checks passed`
- `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewShots.Run -shotsOutput shots -shotsTicks 900`
  → 기존 57장 + strip 18장 = 75장

## Risks & Rollback

- **Risks:** builder 를 tier 로 나누면서 primitive 순서를 바꾸지 않아도, `Solids` 의 grain
  난수열이 tier 마다 새로 시작해 면의 명암이 조금 바뀐다. 건설 중인 건물의 체력바 높이는
  완성 높이(`Top`) 를 그대로 쓴다. 완공 ring 은 직전 관측이 0.5초 안일 때만 나므로 안개
  밖에서 완공된 건물은 조용히 완성 모습으로 나온다.
- **Rollback:** `git revert`. 뷰 코드와 editor 하네스만 바뀐다.
