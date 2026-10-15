# 2026-10-15 — 유닛 애니메이션 고도화와 애니메이션 검증 샷

- Date: 2026-10-15
- GitHub Issue: #165
- Status: Implemented, awaiting review

## Goal

필드의 몸이 죽고, 쏘고, 태어나고, 숨 쉬는 것이 화면에서 읽히게 한다. 그리고
그 동작을 플레이어 없이 PNG 로 확인하는 샷을 `ViewShots` 에 더한다. 샷이 먼저고,
애니메이션은 그 샷을 보면서 다듬는다.

## Non-goals

- 새 유닛 모델. 사운드. HUD.
- 애니메이션 클립, 본, 프리팹, FBX, 머티리얼 애셋 (#159 의 규칙).
- 시뮬레이션 변경. 뷰는 `Entity` 를 읽기만 한다.

## Context / Constraints

- `BodyRig` 는 `Time.unscaledTime` 으로 움직이고 `Update` 에서 돈다. 에디터 batch
  모드에서는 `Update` 가 불리지 않으므로, 샷 하네스는 시계를 직접 밀고 rig 를
  직접 한 프레임씩 돌려야 한다.
- `Fog.Show` 는 죽은 entity 에 Hidden 을 돌려준다. 그래서 지금 죽음은 그 프레임에
  사라지는 것이다.
- 색 literal 은 `UiStyle`, `FieldPalette`, `BodyPalette` 에만 둔다.
- 200기 전투에서 프레임당 추가 할당 0. 투사체와 impact 는 pool.
- `ViewSelfCheck.ViewReadsDoNotMutate` 가 계속 통과해야 한다.

## 설계

### 시계

`ViewClock.cs` (새 파일). `Now`, `Delta` 두 값. 평소에는 `Time.unscaledTime` /
`Time.unscaledDeltaTime` 을 돌려주고, 하네스가 `Hold(now, delta)` 로 고정하면 그
값을 돌려준다. `BodyRig` 와 새 effect 코드는 전부 이 시계를 읽는다.
`BodyRig.Animate()` 를 public 으로 열고 `Update` 는 그것만 부른다.

### 샷

- `lineup-<faction>.png` 6장: 빈 `World` 에 그 진영의 로스터 항목 전부를 `Role`
  오름차순, 같은 역할은 slot 오름차순으로 x 축 한 줄에 세운다. 건물은 완공.
- `strip-<faction>-<motion>.png`: `walk`, `attack`, `hit`, `death`, `idle`. 걷는
  유닛 네 종(전투 유닛 먼저, 모자라면 일꾼)을 세우고 동작을 시작시킨 뒤 0.9초를
  0.9/7초 간격 8프레임으로 찍어 가로로 붙인다. 프레임 사이는 1/60초씩 rig 를
  돌린다. `walk` 는 로스터 속도로 오른쪽(+x)으로 걷는다.
- 확인용으로 `spawn` strip 과 방어 건물의 `tower` strip 을 더한다. 이슈의
  다섯 동작 밖이므로 PR 에 따로 적는다.

### 애니메이션 (`BodyRig`)

| 동작 | 길이 | 내용 |
|---|---|---|
| 죽음 | 0.6초 | 걷는 몸: 기울며 y 스케일 감소, 땅 아래로 침강, 알파 페이드. 나는 몸: 0.3초 낙하 후 침강. 건물: 흔들리며 침강. 돌 골렘 전투 유닛: 잔해 색으로 wash 되며 침강. 끝나면 renderer 와 component 를 끈다 |
| 스폰 | 0.3초 | root 스케일 0 → 1, overshoot 약 1.12 |
| 성장 완료 | 0.15초 + 0.3초 | 옛 몸이 1.25배로 부풀고, 새 몸이 스폰 동작으로 나온다 |
| 유휴 | 반복 | `Walk` 몸통 1.5% 호흡, `Hop` 주기적 눌림, `Float` 부유 + 좌우 기울기, `Fly` 날개 유지 + 부유. 위상과 속도를 시드로 가른다 |
| 걷기 | 반복 | 다리 진폭과 stride 를 다리 길이에서 계산해 발이 미끄러지지 않게 한다. 방향 전환 때 안쪽으로 기울고, 가속 때 앞으로 숙인다. `Hop` 은 포물선 점프와 착지 squash |
| 공격 | 0.36초 | `Lunge` 뒤로 당겼다 앞으로 찌름, `Recoil` 뒤로 튕김과 팔 들림, `Pulse` 부풀었다 눌림. 포탑은 목표로 돌고 반동 |
| 피격 | 0.3초 | 뒤로 밀림, squash, 첫 0.13초 동안 종이 흰색으로 45% wash |

죽음의 기울기는 hull 이 아니라 root 를 돌린다. 다리는 root 에 매달려 있어서,
hull 만 돌리면 다리가 선 자리에 남는다 (strip 에서 확인한 문제).

wash 는 `Body.shader` / `BodyGhost.shader` 의 새 property `_Wash` (rgb 목표 색,
a 비율) 로 한다. 곱하기 tint 로는 잔해 색에 닿지 않는다.

### 투사체와 impact (`Effects.cs`, 새 파일)

- `MatchView.Pose` 가 쿨다운 리셋을 볼 때 사거리가 2칸을 넘으면 tracer 를 쏜다.
  공격자의 wind-up 뒤에 목표 가슴 높이까지 0.24~0.28초 (거리에 비례). 길이 0.75셀.
  처음 계획은 0.12~0.2초와 0.4셀이었고, 2차 라운드에서 바꿨다 (아래).
- 착탄 순간 impact: shard 7개가 퍼지며 0.25초에 작아져 사라지고 땅에 ring 이 퍼진다.
  근접은 tracer 없이 impact 만.
- 색은 `BodyPalette.Shot(faction)`. tracer 와 shard 는 미리 만든 pool 에서 꺼내고,
  모자랄 때만 늘린다.

### MatchView

- 엔티티별 `alive` 목록을 더해 참에서 거짓으로 바뀐 프레임을 본다. 그 프레임에
  몸이 보이고 있었으면 `Die` 를 부르고, 죽는 동안에는 fog 와 무관하게 보이게 둔다.
- 첫 프레임 이후에 생긴 id 는 `Spawn`. skin 이 바뀌는 살아 있는 몸은 옛 몸을
  `retiring` 목록에 넘겨 부풀게 하고, 새 몸은 0.15초 뒤에 스폰한다.

## Approach (Checklist)

- [x] **Step 1** — `ViewClock`, `BodyRig.Animate`
- [x] **Step 2** — lineup 샷
- [x] **Step 3** — strip 샷
- [x] **Step 4** — 죽음
- [x] **Step 5** — 투사체와 impact
- [x] **Step 6** — 스폰과 성장
- [x] **Step 7** — 유휴
- [x] **Step 8** — 걷기와 공격 다듬기, strip 으로 세 번 이상 반복
- [x] **Step 9** — 2차 라운드: tracer, wind-up, 불투명 죽음, 유휴 진폭, growth strip
- [x] **Step 10** — 3차 라운드: 착탄 시점 flinch 와 죽음, fog 처리, 규칙 공유, match strip

## Validation

- **Commands to run:**
  - `dotnet build`
  - `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewSelfCheck.Run`
  - `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewShots.Run -shotsOutput shots -shotsTicks 900`
- **Expected output:** `OK: all view determinism checks passed`, 기존 5장 + lineup 6장
  + strip.

## 반복 기록

strip 을 다섯 번 렌더하며 고쳤다.

1. 첫 렌더: 카메라가 멀고 몸이 위로 쏠렸다. frame 0 이 동작 시작 전 포즈였다
   (Begin 뒤에 Animate 를 한 번 더 돌려 고침).
2. tracer 와 shard 가 64px 에서 보이지 않았다. 길이 0.5, 굵기 0.17, shard 0.24 로
   키웠다. 스폰이 0.13초 프레임 간격에서 이미 overshoot 라 smoothstep 상승으로 바꿨다.
3. frame 을 640x360 으로 넓히고 간격 2.6셀로 당겼다. 돌 골렘 잔해 wash 0.9 는 평면
   색처럼 보여 0.7 로 낮췄다.
4. Recoil 의 팔 반동 -40도가 포신을 수직으로 세워 -24도로 줄였다. 피격 wash 0.55 는
   잎 색을 지워 0.45 로 낮췄다.
5. 다리가 root 에 매달린 몸(차원 유랑종 갑각류)이 죽을 때 다리가 남았다. root 를
   돌리도록 고쳤다.

## 2차 라운드 (coordinator 검토 후)

1차 결과에서 tracer, 공격 준비 동작, 죽음이 64px 에서 싸게 보인다는 검토를 받고
다섯 가지를 고쳤다.

| 항목 | 1차 | 2차 |
|---|---|---|
| tracer | 길이 0.5셀, 굵기 0.17, 비행 0.12~0.2초. strip 에 1프레임 | 길이 0.75셀, 굵기 0.34, 반투명 trail (1.3셀, alpha 0.35), 비행 0.24~0.28초, wind-up 뒤 발사. strip 에 2프레임 |
| impact | shard 5개, 0.24셀 | shard 7개, 0.42셀, 셋 중 하나는 종이 흰색 쪽으로 60%, 땅에 퍼지는 ring decal |
| 피격 시점 | 피해 순간 | 원거리는 tracer 도착 순간, 근접은 즉시 (3차에서 구현 방식 변경) |
| 공격 준비 | 없음 | 0.0875초 wind-up (뒤로 당김과 웅크림). `Lunge` 팔 160도, `Recoil` 팔 70도. 전체 0.35초 |
| 죽음 | 0.18초부터 페이드 | 처음 60% 불투명. 걷는 몸은 쓰러지며 다리가 벌어짐, 나는 몸은 떨어져 한 번 튐, 팔이 있는 몸은 팔 반대쪽으로 쓰러짐. 돌 골렘 전투 유닛은 잔해 덩어리 3개 (`Effects.Crumble`) |
| 유휴 | 호흡 1.8%, 슬라임 눌림 펄스, 부유 크기 비례 | 호흡 3.5%, 슬라임 눌림 연속 11%, 부유 ±0.06 world unit |
| growth strip | 없음 | `strip-treespirits-growth.png`: 일꾼이 부풀고 덩굴 정령과 고목 수호자가 솟음 |

attack strip 은 Attack 을 frame 0 보다 0.05초 먼저 불러 frame 0 에 wind-up 이 잡힌다.
유휴 strip 의 인접 프레임 차이(밝기 차 12 초과 픽셀 수)는 1,100~4,800 이다.

## 3차 라운드 (critic 검토 후)

| 항목 | 고친 것 |
|---|---|
| 집중 사격 때 flinch 없음 | `Hit(delay)` 는 매번 시작을 미래로 밀었다. `Hit()` 는 이제 즉시만 받고, `Effects` 가 tracer 마다 목표 id 를 들고 있다가 도착한 프레임에 `Landed` 로 알려 MatchView 가 그때 `Hit()` 을 부른다 |
| 원거리 처치의 순서 | 죽음을 프레임 끝의 `Consequences` 에서 모든 사격 뒤에 준다. `Die(rubble, delay)` 의 delay 는 그 몸으로 날아가는 마지막 tracer 의 남은 시간 (`Effects.Incoming`) 이고, 나중 호출은 쓰러짐을 앞당길 수만 있다 |
| 안개에서 나온 순간의 가짜 사격 | `cooldowns[i]` 를 fog 판단 전에 모든 entity 에 대해 갱신하고, `Pose` 는 비교 결과만 받는다 |
| tracer 가 숨은 목표를 드러냄 | 목표가 `Fog.Seen` 이 아니면 tracer 는 거리의 60% 에서 사라지고 burst, ring, landing 이 없다 |
| harness 와 규칙 중복 | `MatchView.TracerReach`, `Throws(UnitStats)`, `Rubble(Faction, EntityKind)` 를 공개하고 `ViewShots` 가 부른다 |
| 붉은 hit flash ring | `HitDetection` 이 고른 몸이라도 tracer 가 날아오는 중이면 ring 을 착탄 순간에 켠다 |
| 검증 공백 | `strip-match-fight`, `-kill`, `-spawn`: 스크립트 매치를 tick 하나에 held-clock 3프레임씩 진행하며 `MatchView.Draw` (LateUpdate 그대로) 로 그린다. `MatchRunner` 상태는 reflection 으로 넣는다 (`MatchRunner` 는 쓰기 범위 밖이고 setter 가 private). peer 0 의 fog 가 켜져 있다 |

match strip 이 찾은 순간: 첫 원거리 사격 tick 150, 첫 유닛 죽음 tick 348 (entity 10, 지옥불 원거리), 첫 생산 유닛 tick 340.
`strip-match-kill.png` 에서 tick 348 에 죽은 지옥불 원거리 유닛은 뇌우 정령의 tracer 가 닿는 frame 4 까지 서 있다가 frame 5 에 쓰러진다.

이슈 문구에서 달라진 숫자: tracer 0.75셀 (이슈 0.3~0.5), 비행 0.24~0.28초 (이슈 0.12~0.2),
shard 7개 (이슈 4~6), 걷는 몸 호흡 3.5% (이슈 1~2%). `BodyPalette.cs` 는 이슈의 쓰기 범위 목록에
없지만, 색 literal 이 들어갈 수 있는 파일이라 `HitFlash` 와 `Shot(faction)` 을 거기에 추가했다.

## Risks & Rollback

- **Risks:** 컴파일은 Unity 에서만 확인된다. 죽는 몸을 fog 보다 먼저 보이게 하는
  분기를 잘못 두면 안개 속 적의 죽음이 보이거나, 기억된 건물이 사라진다. 원거리
  피격 반응과 그 사격으로 인한 죽음은 시뮬레이션의 즉시 피해보다 0.33~0.37초
  (wind-up 0.0875초 + 비행 0.24~0.28초) 늦게 보인다. 체력바는 피해 순간에 줄어든다.
  match strip 은 `MatchRunner` 의 private 멤버 이름 (`previous`, `current`,
  `lastStepMs`, `World`, `LocalPeer`, `Phase`, `Instance`) 에 기대므로, 그쪽 이름이
  바뀌면 `ViewShots` 가 `MissingFieldException` 으로 멈춘다.
- **Rollback steps:** `git revert`. 뷰 파일만 바뀌고 시뮬레이션과 저장 형식은 그대로다.

## Open Questions

- 없음.
