# 2026-10-16 — 필드 표시가 연구된 수치를 읽게 적용

- Date: 2026-10-16
- GitHub Issue: #182
- Status: Implemented, awaiting review

## Goal

`MatchView.cs` 에서 아직 기본값을 읽는 네 곳이 연구된 값을 읽게 한다.

1. 선택 유닛의 사거리 링: `FactionData.Stats(...).Range` 대신 `World.RangeOf(entity)`.
2. 포획 막대: `CaptureOrder.Progress(Entity)`(60틱 고정) 대신 `Progress(World, Entity)`.
3. 도착 반경 링: `ArrivalOrder.Radius` 대신 `ArrivalOrder.RadiusFor(World, peer)`.
4. 잔해 fade: `World.RemnantTicks`(400) 대신 그 월드에서 가능한 최대 지속으로 나눔.

옛 overload `Progress(Entity)` 와 `Radius` 는 지우려 했으나 지우지 못했다(Open Questions).
`CLAUDE.md` 의 "See it" 절을 지금 샷 수와 `-shotsOutput` 사용법에 맞게 고친다.

## Non-goals

- 새 애니메이션, HUD 변경, `Sim/` 변경.

## Context / Constraints

- 이 브랜치는 `feat/174`(PR #176, 안에 #173 과 #171)에 `feat/165`(PR #175)를 합친
  통합 브랜치다. 셋 다 draft 이므로 이 PR 은 그 위에 쌓인다.
- 뷰는 시뮬레이션을 읽기만 한다.
- 잔해 셀에는 지속 틱이 저장되지 않고 `RemnantTicksFor(owner)` 는 주인 없는 잔해에
  쓸 수 없다. 그래서 `0..World.MaxPeers-1` 의 `RemnantTicksFor` 최댓값으로 나눈다.
  연구 전에는 400 이라 지금과 같고, 연구 뒤에는 600 으로 나눠 600틱 잔해가 처음부터
  옅어진다. 400틱 잔해(연구 전에 떨어진 것)는 600 으로 나누면 처음 값이 2/3 가 되어
  조금 더 옅게 시작하는데, 완전 불투명으로 200틱 머무는 것보다 낫다.
- 쓰기 범위: `Client/Assets/Scripts/MatchView.cs`, `CaptureOrder.cs`, `ArrivalOrder.cs`,
  `Replay/CaptureOrderChecks.cs`, `Replay/ArrivalOrderChecks.cs`, `CLAUDE.md`, 이 문서.

## Approach (Checklist)

- [x] **Step 0: Recon** — 호출자 전부 찾기(`MatchView.cs` 와 두 harness 뿐).
- [x] **Step 1: 사거리 링** — `world.RangeOf(e)`.
- [x] **Step 2: 포획 막대와 도착 반경** — 새 overload 로 바꾸고 harness 호출도 새 overload 로. 옛 overload 는 남김.
- [x] **Step 3: 잔해 fade** — 최대 지속 helper 와 주석.
- [x] **Step 4: 문서** — `CLAUDE.md` "See it".

## Validation

- **Commands to run:**
  - `dotnet build`
  - `dotnet run --project Replay`
  - Unity: `sync.sh <worktree> 182`, `run.sh 182 WordCraft.View.ViewSelfCheck.Run selfcheck`,
    `run.sh 182 WordCraft.View.ViewShots.Run shots -shotsOutput ... -shotsTicks 900`
- **Expected output:** `0 Warning(s)`, `OK: all determinism checks passed`,
  `OK: all view determinism checks passed`, match strip 3장이 여전히 나옴.

## Risks & Rollback

- **Risks:** 연구 전에 떨어진 400틱 잔해는 연구 뒤에 처음부터 2/3 의 밝기로 시작한다.
  필드 위 표시는 배치 실행으로 연구 상태를 보지 못한다(샷은 연구 없는 스크립트 매치).
- **Rollback steps:** `git revert`. `Sim/` 과 world hash 에 닿지 않는다.

## Open Questions

- 옛 `CaptureOrder.Progress(Entity)` 와 `ArrivalOrder.Radius` 는 지우지 않았다. 쓰기 범위
  밖인 `Replay/ResearchMenuChecks.cs`(#174)가 연구 안 한 peer 의 막대와 연구한 반경을
  기본값과 견주는 기준으로 둘을 아직 부른다(255, 283, 288행). 지우면 Replay 가 컴파일되지
  않는다. `MatchView.cs` 호출자는 사라졌고 두 주석이 그 사실과 지울 조건을 적는다.
  `ResearchMenuChecks.cs` 가 기본값 기준을 `World.CaptureTicks` 와 `World.ArrivalRadius`
  로 바꾸는 후속에서 지우면 된다.
