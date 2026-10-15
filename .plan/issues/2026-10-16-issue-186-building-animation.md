# 2026-10-16 — 건물 건설·활동·파괴 애니메이션과 검증 strip

- Date: 2026-10-16
- GitHub Issue: #186
- Base: `feat/182` (PR #184, 안에 #175·#176·#173·#171). 이 PR 은 #184 위에 쌓인다.
- Status: Implemented, awaiting review

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
- `Design` 에 `Tiers`(mesh 배열), `TierPivots`, `TierHeights`, `Assembled`, `Assembly`(진영별
  방식), `Debris`(덩어리와 먼지 색), `Mote`(입자 색), `Glow`(파괴 때 번쩍이는 안쪽 빛,
  없으면 alpha 0) 를 더한다. 색에는 기본값이 없고, `Models.Dress` 가 `BodyPalette` 에서
  채운다.
- `Assembled` 는 플레이어가 짓는 건물만 참이다. 자원 노드와 잔해 더미는 `Building` 이지만
  부품이 없고, 지금까지처럼 흔들리며 가라앉는다.
- tier 는 hull 의 자식이라 피격 flinch 와 발사 반동을 그대로 탄다.
- 본진, 생산, 방어, 인구, 기술 건물 30종의 hull 을 부품 1~4개로 나눈다. 지옥불·물
  슬라임·돌 골렘·유랑종의 방어 건물은 hull 하나와 crown 이다.
- 인간 본진의 깃발은 기초 부품에 있다. 기둥 부품에 두면 짓는 동안 떠 있다.

### 건설 (`BodyRig.Progress`)

진행도 0~0.9 를 hull tier 수로 나눠 tier 마다 한 구간씩, 0.9~1.0 은 crown (없으면
마지막 tier) 이 쓴다. 구간 안의 진행 u 로 tier 의 위치·회전·크기를 정한다.

| 진영 | 방식 | 부품 하나의 움직임 |
|---|---|---|
| 세계수 정령 | 자람 | 가는 상태에서 y 0 → 1, xz 0.3 → 1, 끝에 8% 넘침 |
| 지옥불 군단 | 찢고 솟음 | 자기 높이의 1.05배 땅 아래에서 떨리며 올라옴 |
| 물 슬라임 | 부풂 | 크기 0 → 1.16 → 0.94 → 1 탄성 |
| 돌 골렘 부족 | 쌓임 | 기초는 y 로 쌓이고, 나머지는 1.2 + 0.4 x 높이 위에서 떨어져 착지 squash |
| 차원 유랑종 | 외피판 닫힘 | 기초는 y 로 쌓인다. 옆에 선 판은 바깥으로 80도 열렸다 닫히고, 가운데를 두른 판은 70도 비틀리고 1.7배 펼쳐졌다 닫힌다 |
| 인간 마법 문명 | 층이 쌓임 | xz 그대로, y 0 → 1 로 한 층씩 올라감, 넘침 없음 |

부품 k 는 진행도 k x step 에서 시작해 1.4 step 동안 올라간다. 첫 부품은 진행도 0 에서도
자기 상승의 20% 에 있다.

완공 순간 (진행도가 1 에 닿는 프레임, 직전 관측이 0.5초 안일 때만) hull 이 0.35초
눌렸다 펴지고 `Effects.Dust` 가 땅에 ring 을 한 번 퍼뜨린다. 지어지는 동안의 반투명
(SiteAlpha 0.75) 은 그대로 둔다. root 를 y 로 늘이던 `siteHeight` 는 없앤다.

### 활동 (`BodyRig.Work`, `Deliver`)

- `MatchView.Pose`: 완공된 건물이 `QueueCount > 0` 또는 `ResearchIndex >= 0` 이면
  `Work(true)`.
- crown 이 스스로 도는 건물 (`CrownSpin != 0`) 은 회전이 0.4초에 걸쳐 3.5배로 빨라진다.
  각도를 시간에 곱하지 않고 누적해서 속도가 바뀌어도 튀지 않는다.
- 나머지는 hull 이 1.5% 맥동한다.
- 둘 다 0.33초마다 진영색 입자 하나 (`Effects.Mote`, 0.3~0.5셀) 가 지붕 높이 85% 에서
  1초 동안 1.3셀 떠오르며 작아진다. 동시에 3개가 보인다. 이슈는 crown 이 도는 건물에
  입자를 두지 않았지만, 3.5배 빠른 회전은 정지 화면에서 보이지 않아서 모두에게 준다.
- 생산 완료: `QueueCount` 가 줄어든 프레임에 같은 주인의 새 유닛이 건물 문 앞 (중심에서
  `Cells(role) x 0.5 + 2` 셀 안. 생산된 유닛은 `Position + RallyOffset`, 2셀 밖에 선다)
  에 생겼으면, 가장 가까운 그 유닛 쪽으로 `Deliver(yaw)`. hull 이 0.45초 동안 10% 부풀고
  그쪽으로 10도 기운다. 취소로 줄어든 queue 는 문 앞에 새 유닛이 없으므로 반응하지 않는다.
  유랑종 통로가 먼 도착 지점으로 보낸 유닛에는 기울지 않는다.

### 파괴 (`BodyRig.Raze`, `Fall` 이 부품 있는 건물에서 넘긴다)

0.8초 (아래 비율은 0.8초에 대한 비율). 0~30% 흔들림, 15% 에 `Effects.Crumble` 이
진영색 덩어리 2~4개 (footprint 반올림: 본진 4, 생산 3, 방어 2) 를 던지고, crown 부터
위쪽 tier 순서로 서로 다른 방향으로 기울며 떨어지고, 32% 에 먼지 ring (footprint 의
45% 반지름), 45% 부터 남은 기초가 자기 높이만큼 가라앉고 마지막 30% 에서 흐려진다.
지옥불과 유랑종은 3~27% 에 `_Wash` 로 안쪽 빛 색이 80% 까지 번쩍이고, 그 뒤 어두운
잔해 색 55% 로 꺼진다. 다른 진영은 30% 만 어두워진다.

### Effects

- `Crumble(foot, size, colour, yaw, delay, count)`: 덩어리 수를 받는다 (기본 3).
- `Dust(at, radius, colour, delay)`: impact ring pool 을 shard 없이 쓴다. 반지름과 길이를
  burst 마다 들고 있다.
- `Mote(at, colour)`: 떠오르는 입자 pool, 처음 12개.

### strip (`ViewShots`)

- `strip-<faction>-build`: 본진, 생산, 방어. 진행도를 0.77초에 0 → 1 로 밀고, 마지막
  프레임에 완공 ring 과 settle 이 보인다.
- `strip-match-build`: 실제 `MatchView` 로 스크립트 매치에서 처음 완공되는 건물 (tick
  250).
- `strip-match-raze`, `strip-match-razesite`: 스크립트 매치에는 건물이 무너지는 tick 이
  없어서 따로 만든 공성 world 를 `MatchView` 로 그린다. 빈 맵에 peer 1 의 풀씨 둥지
  (완성, 또는 건설 시간 절반이 지난 site) 와 그 양옆 4.5셀에 peer 0 의 지옥불 원거리 4기.
  peer 0 의 fog 가 켜져 있다. 프레임 사이 4 tick, 8프레임이 1.4초다. 무너지는 tick 은
  완성 346, site 450.
- `-shotsOnly build,work,...`: 한 종류만 다시 찍는 필터. 없는 이름은 `FAIL` 로 끝난다.
- `CLAUDE.md` 의 "See it" 절을 78장과 `-shotsOnly` 에 맞춘다 (이슈 쓰기 범위 밖, 검토
  요청으로 넓힘).
- `strip-<faction>-work`: 본진, 생산, 기술. 방어 건물은 생산도 연구도 하지 않아서
  기술 건물로 바꾼다. 0.36초 (`DeliverAt`) 에 본진과 생산 건물이 유닛을 내보낸다
  (`Deliver` 와 유닛 `Spawn`).
- `strip-<faction>-destroy`: 본진, 생산, 방어.
- 6진영 x 3 = 18장.

## Approach (Checklist)

- [x] **Step 1** — `ViewShots` 에 build, work, destroy strip (지금 코드 그대로 찍어 기준 확보)
- [x] **Step 2** — `Models` tier 와 `Design` 필드, `BodyRig` 조립
- [x] **Step 3** — `Effects.Dust`, 완공 ring 과 settle
- [x] **Step 4** — 활동: `Work`, `Deliver`, `Effects.Mote`, `MatchView` 연결
- [x] **Step 5** — 파괴: tier 붕괴, `Crumble` 확장, 안쪽 빛
- [x] **Step 6** — strip 을 보며 세 번 이상 다듬기 (64px 축소본으로 판단)

## 반복 기록

strip 18장을 0.4배로 줄여 진영 6개를 한 장에 쌓은 montage 로 판단했다. 이 크기에서
본진이 약 55px 이다.

0. 기준: 지금 코드의 build strip 은 건물이 y 로 늘어나며 반투명하게 솟을 뿐이다.
1. 첫 렌더: 여섯 방식이 모두 구별된다. 카메라가 멀고 건물이 프레임 아래쪽에 몰려
   있었다. 돌 골렘의 떨어지는 돌이 프레임 위로 나갔다 (낙하 높이 1.6 + 0.6h → 1.2 + 0.4h).
2. 파괴: 기초가 낮은 건물 (지옥불 본진의 0.3 판) 이 0.5초에 이미 땅속에 있었다. 가라앉는
   깊이를 건물 높이에서 기초 높이로 바꾸고 시작을 0.4 → 0.45 로 늦췄다. 안쪽 빛은 frame 1
   에 34% 만 잡혀 정점을 0.07 → 0.15 로 옮겼다. 먼지 ring 이 본진 둘레 3셀까지 퍼져 45% 로
   줄이고 색을 더 밝게 (0.35 → 0.55) 했다. 유랑종 본진의 첫 부품이 1.7배로 펼쳐져 보여
   돌 골렘처럼 기초는 y 로만 쌓이게 했다.
3. 활동: 입자가 64px 에서 점 하나였다. 0.3~0.5셀로 키우고 0.4 → 0.33초 간격, 지붕 높이
   70% → 85% 에서 시작하게 했다. 도는 crown 의 가속은 정지 화면에서 보이지 않아 입자를
   모든 일하는 건물에 줬다. 생산 완료의 부풂과 기울기를 8%, 7도에서 10%, 10도로 올렸다.
4. 실제 경로 (아래 5 참고): `strip-match-spawn` 에서 생명의 나무가 일꾼을 내보내며 입자를 내고 그쪽으로
   기울고, `strip-match-build` 에서 풀씨 둥지가 frame 1 에 불투명해지고 frame 3~5 에 먼지
   ring 이 퍼진다.
5. 검토 반영: 자원 노드와 잔해 더미가 건물 파괴 경로 (회색 덩어리와 먼지) 를 타던 것을
   `Assembled` 로 막았다. 무너지는 건물을 실제 경로로 보려고 공성 strip 두 장을 더했다.
   근접 6기는 건물 위로 몰려 가렸고, 뒤쪽 원거리는 화면에서 건물 위에 떠 보여서 양옆
   원거리 4기로 바꿨다. 원거리 처치는 tracer 가 닿을 때까지 쓰러짐을 미루므로 프레임
   간격을 4 tick 으로 늘렸다.

## Validation

- `dotnet build`
- `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewSelfCheck.Run`
  → `OK: all view determinism checks passed`
- `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewShots.Run -shotsOutput shots -shotsTicks 900`
  → 기존 57장 + strip 18장 + match strip 3장 = 78장

## Risks & Rollback

- **Risks:** 생산 취소와 이웃 건물의 생산 완료가 같은 프레임이고 그 유닛이 취소한 건물의
  문 앞 반경 안에 서면, 취소한 건물도 기운다. builder 를 tier 로 나누면서 primitive 순서를 바꾸지 않아도, `Solids` 의 grain
  난수열이 tier 마다 새로 시작해 면의 명암이 조금 바뀐다. 건설 중인 건물의 체력바 높이는
  완성 높이(`Top`) 를 그대로 쓴다. 완공 ring 은 직전 관측이 0.5초 안일 때만 나므로 안개
  밖에서 완공된 건물은 조용히 완성 모습으로 나온다.
- **Rollback:** `git revert`. 뷰 코드와 editor 하네스만 바뀐다.
