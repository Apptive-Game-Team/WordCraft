# 2026-10-15 — 유닛 애니메이션 고도화와 애니메이션 검증 샷

- Date: 2026-10-15
- GitHub Issue: #165
- Status: Draft

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
| 피격 | 0.3초 | 뒤로 밀림, squash, 짧은 밝은 wash |

wash 는 `Body.shader` / `BodyGhost.shader` 의 새 property `_Wash` (rgb 목표 색,
a 비율) 로 한다. 곱하기 tint 로는 잔해 색에 닿지 않는다.

### 투사체와 impact (`Effects.cs`, 새 파일)

- `MatchView.Pose` 가 쿨다운 리셋을 볼 때 사거리가 2칸을 넘으면 tracer 를 쏜다.
  공격자 가슴 높이에서 목표 가슴 높이까지 0.12~0.2초 (거리에 비례). 길이 0.4셀.
- 착탄 순간 impact: shard 5개가 퍼지며 0.25초에 작아져 사라진다. 근접은 tracer
  없이 impact 만.
- 색은 `BodyPalette.Shot(faction)`. tracer 와 shard 는 미리 만든 pool 에서 꺼내고,
  모자랄 때만 늘린다.

### MatchView

- 엔티티별 `alive` 목록을 더해 참에서 거짓으로 바뀐 프레임을 본다. 그 프레임에
  몸이 보이고 있었으면 `Die` 를 부르고, 죽는 동안에는 fog 와 무관하게 보이게 둔다.
- 첫 프레임 이후에 생긴 id 는 `Spawn`. skin 이 바뀌는 살아 있는 몸은 옛 몸을
  `retiring` 목록에 넘겨 부풀게 하고, 새 몸은 0.15초 뒤에 스폰한다.

## Approach (Checklist)

- [ ] **Step 1** — `ViewClock`, `BodyRig.Animate`
- [ ] **Step 2** — lineup 샷
- [ ] **Step 3** — strip 샷
- [ ] **Step 4** — 죽음
- [ ] **Step 5** — 투사체와 impact
- [ ] **Step 6** — 스폰과 성장
- [ ] **Step 7** — 유휴
- [ ] **Step 8** — 걷기와 공격 다듬기, strip 으로 세 번 이상 반복

## Validation

- **Commands to run:**
  - `dotnet build`
  - `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewSelfCheck.Run`
  - `Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewShots.Run -shotsOutput shots -shotsTicks 900`
- **Expected output:** `OK: all view determinism checks passed`, 기존 5장 + lineup 6장
  + strip.

## Risks & Rollback

- **Risks:** 컴파일은 Unity 에서만 확인된다. 죽는 몸을 fog 보다 먼저 보이게 하는
  분기를 잘못 두면 안개 속 적의 죽음이 보이거나, 기억된 건물이 사라진다. 투사체는
  시뮬레이션의 hitscan 피해보다 0.12~0.2초 늦게 닿는다.
- **Rollback steps:** `git revert`. 뷰 파일만 바뀌고 시뮬레이션과 저장 형식은 그대로다.

## Open Questions

- 없음.
