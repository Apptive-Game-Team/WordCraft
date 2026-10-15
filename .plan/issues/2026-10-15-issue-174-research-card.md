# 2026-10-15 — 기술 건물 연구 카드와 연구 수치의 뷰 반영

- Date: 2026-10-15
- GitHub Issue: #174
- Status: Draft

## Goal

#164(PR #173)가 시뮬레이션에 넣은 `CommandType.Research` 를 사람이 내릴 수 있게
한다. 기술 건물(`Role.Tech`)을 고르면 카드 1·2행에 그 진영의 업그레이드가 놓이고,
연구 중인 건물은 정보 패널에 진행도를 보이며, 선택한 유닛 이름 옆에 현재 공격·방어
보너스(`+1/+2`)가 보인다. 뷰가 읽는 수치 세 곳(포획 진행 막대, 도착 반경, 시야
반경)도 연구된 값을 읽게 한다.

## Non-goals

- 능력 버튼, 아이콘.
- `MatchView.cs` 의 사거리 링, 필드 위 건물 연구 미터, 필드 위 포획 막대 호출
  (#165 가 그 파일을 쓰는 중이라 후속).
- `Sim/` 변경.

## Context / Constraints

- 이 브랜치는 `main` 에 `feat/164`(PR #173)와 `feat/169`(PR #171)를 합친 통합
  브랜치다. 두 PR 이 먼저 merge 되어야 이 PR 이 의미를 갖는다.
- 쓰기 범위: `CommandCard.cs`, `Hud.cs`, `Orders.cs`, `CaptureOrder.cs`,
  `ArrivalOrder.cs`, `Fog.cs`, `ResearchMenu.cs`(신규), `Replay/ResearchMenuChecks.cs`
  (신규), `Replay/Program.cs`(한 줄), `docs/UI-STYLE.md`, 이 문서.
  `Replay/Replay.csproj` 에 `ResearchMenu.cs` 컴파일 한 줄이 더 필요하다(범위 밖이지만
  `ProductionMenu.cs` 와 같은 방식으로 Replay 가 컴파일해야 하므로).
- 뷰는 시뮬레이션을 읽기만 한다. 가격과 가능 여부는 `World.ResearchCost`,
  `World.UpgradeLevel` 에서 읽고 다시 계산하지 않는다.
- 페이징은 #169 가 만든 `ProductionMenu.Pages/PageRange/NextPage` 를 그대로 쓴다.
- `MatchView.cs` 가 `CaptureOrder.Progress(Entity)` 와 `ArrivalOrder.Radius` 를
  호출하므로 이 둘의 시그니처는 바꾸지 않고 `Progress(World, Entity)` 와
  `RadiusFor(World, int)` 를 추가한다. `MatchView` 를 새 것으로 바꾸는 일은 후속이다.

## Approach (Checklist)

- [ ] **Step 0: Recon** — `Sim/Research.cs`, `FactionData` 연구 표, `CommandCard`,
      `Orders`, `Hud`, `ProductionMenu` 와 그 harness.
- [ ] **Step 1: `ResearchMenu` 와 harness** — Unity 를 import 하지 않는 `ResearchMenu`
      (목록, 라벨, 최대 단계, 진행도, 보너스 문자열). `Replay/ResearchMenuChecks.cs` 가
      여섯 진영 목록 길이와 page 상한, 라벨과 단계 변화, 진행도를 검사한다.
- [ ] **Step 2: 카드 칸과 Research 명령** — `CommandCard.Building` 이 기술 건물이면
      연구 칸을 놓는다. 최대 단계는 `Maxed` 칸(`Void` 면). `Orders.Run` 이 대표 하나에
      `Research(EntityId, Arg = index)` 를 보낸다. `More` 의 page 수가 기술 건물이면
      연구 목록 기준이다.
- [ ] **Step 3: 진행 미터와 보너스** — 정보 패널에 `researching <이름>` 과 `Queue` 색
      미터, 유닛 이름 옆 `+공격/+방어`.
- [ ] **Step 4: Cancel** — 기술 건물 카드의 1번 칸 `Cancel` 이 연구 중에는 연구 취소임을
      카드 주석과 harness 에 적는다(시뮬레이션은 이미 처리한다).
- [ ] **Step 5: 뷰가 연구 수치를 읽음** — `CaptureOrder.Progress(World, Entity)`,
      `ArrivalOrder.RadiusFor(World, int)`, `Fog` 시야 반경이 `World.RangeOf`.
- [ ] **Step 6: 문서** — `docs/UI-STYLE.md`.

## Validation

- **Commands to run:**
  - `dotnet build`
  - `dotnet run --project Replay`
  - Unity: `sync.sh <worktree> 174`, `run.sh 174 WordCraft.View.ViewSelfCheck.Run selfcheck`
- **Expected output:** `0 Warning(s)`, `OK: all determinism checks passed`,
  `OK: all view determinism checks passed`, `error CS` 없음.
- 사람이 볼 것: 기술 건물 선택 시 카드가 바뀌는가, 연구 후 단계 표시가 오르는가,
  취소가 환불되는가(상단 자원 숫자), 진행 미터와 `+1/+2` 가 정보 패널에 맞게 놓이는가.

## Risks & Rollback

- **Risks:** 화면 배치는 배치 실행으로 볼 수 없다. 카드 라벨 두 줄이 54px 칸에 들어가는지,
  정보 패널 높이(184px)에 줄이 더 들어가는지는 사람이 본다. `MatchView` 의 포획 막대와
  사거리 링은 이 PR 이 아직 연구 수치로 바꾸지 않는다.
- **Rollback steps:** `git revert`. `Sim/` 과 world hash 에 닿지 않는다.

## Open Questions

- 없음.
