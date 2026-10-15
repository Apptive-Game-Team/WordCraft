# 2026-10-15 — 명령 카드 3행 4열 확장과 생산 목록 페이지

- Date: 2026-10-15
- GitHub Issue: #169
- Status: Implemented, awaiting review

## Goal

명령 카드를 3×3 아홉 칸에서 3행 4열 열두 칸으로 넓히고, 생산 목록이 카드보다
길어지면 `More` 버튼으로 page 를 넘긴다. 로스터가 진영당 열 개 넘게 늘어도
(#166) 모든 항목에 버튼이 있고, 연구 버튼(#164) 자리가 카드에 미리 잡혀 있다.

## Non-goals

- 연구 버튼 자체(#164 뒤의 후속 이슈).
- 명령 카드의 툴팁, 아이콘 교체.
- `Sim/`, `Replay/Program.cs`, `CameraRig.cs` 는 건드리지 않는다.

## Context / Constraints

- 쓰기 범위: `UiStyle.cs`, `CommandCard.cs`, `Orders.cs`, `ProductionMenu.cs`,
  `Hud.cs`, `Replay/ProductionMenuChecks.cs`, `docs/UI-STYLE.md`.
- 행 수가 3 으로 그대로라 `UiStyle.BottomPanel`(200)과 `MinimapSize`는 변하지 않는다.
  `CameraRig` 가 `BottomPanel` 과 `TopBar` 만 읽으므로 카메라는 그대로다.
- 카드 폭은 170 에서 228 로 늘어 선택 목록 폭이 58px 줄어든다. `TopRightWidth`
  는 상단 바의 값이라 카드와 무관하고 260 그대로다.
- `ProductionMenu` 는 Unity 를 import 하지 않는다(Replay 가 컴파일한다). page
  계산은 거기 두고 `CommandCard` 는 그 결과를 `CardSlot` 으로 바꾸기만 한다.
- 기존 3×3 의 칸은 같은 키를 유지한다. `R T Y / F G H / V B N` 은 그대로 있고
  네 번째 열 `U J M` 이 새로 생긴다. 그래서 Attack 은 계속 `F` 다.

## Approach (Checklist)

- [x] **Step 0: Recon** — `CommandCard`, `Hud.Card`, `Orders`, `ProductionMenu`,
      `ProductionMenuChecks`, `UI-STYLE.md` 의 레이아웃 수식.
- [x] **Step 1: 레이아웃 상수와 키** — `CommandCard.Cols = 4`, 키 열두 개,
      `UiStyle.CardWidth` 를 `Cols` 에서 계산, 카드 표를 열두 칸으로,
      `UI-STYLE.md` 수식.
- [x] **Step 2: 생산 목록 page** — `ProductionMenu.Cells = 8`, `PageSize = 7`,
      `Pages(count)`, `CardSlot.More`, `Orders.Page`(선택이 바뀌면 0),
      `Orders.Card()` 한 곳에서 카드를 만들고 `Hud` 는 그것을 그린다.
- [x] **Step 3: 건설 서브메뉴** — 이미 `Cols` 로 도는 반복문이 4열 두 행에
      다섯 건물을 놓는지 확인하고 주석을 갱신.
- [x] **Step 4: 연구 제목** — `Building` 카드에서 `Role.Tech` 면 제목 `research`,
      1·2행 비움. `CardKind` 는 늘리지 않는다.
- [x] **Step 5: 하니스** — `ProductionMenuChecks` 를 page 규칙으로 다시 쓴다.

## 계약

```csharp
// ProductionMenu.cs
public const int Cells = 8;       // 1·2행 전체
public const int PageSize = 7;    // 넘칠 때 마지막 칸이 More 라서
public const int MaxPages = 4;
public static int Pages(int count);                  // count <= 8 이면 1, 아니면 ceil(count / 7)
public static void PageRange(int count, int page, out int first, out int length);

// CommandCard.cs
public const int Cols = 4, Rows = 3;                 // Cells = 12
public static CardSlot[] Of(CardKind kind, Faction faction, Role lead, int page);
public static string Title(CardKind kind, Role lead);

// Orders.cs
public int Page { get; }          // 뷰 상태. 시뮬레이션에 보내지 않는다
public CardSlot[] Card();         // 키가 누르는 카드 = HUD 가 그리는 카드
```

규칙:

- 생산 항목이 8개 이하면 1·2행 여덟 칸에 전부 놓고 `More` 는 없다.
- 9개 이상이면 page 당 7개 + 11번 칸(키 `M`)이 `More`. 마지막 page 에서 누르면
  첫 page 로 돌아간다. `More` 의 라벨에 `2/3` 처럼 현재 page 를 쓴다.
- `Role.Tech` 건물은 0행(Rally, Cancel, Arrive)만 두고 1·2행은 빈 칸, 제목 `research`.
- 선택 목록(id 순서까지)이 바뀌면 `Page = 0`.

## Validation

- **Commands to run:** `dotnet build`, `dotnet run --project Replay`,
  `unity-tools` 의 `sync.sh` 후 `run.sh 169 WordCraft.View.ViewSelfCheck.Run selfcheck`.
- **Expected output:** 경고 0 개, `OK: all determinism checks passed`,
  `OK: all view determinism checks passed`.
- 새 체크(`ProductionMenuChecks`): 모든 진영의 page 수가 4 이하, 차원 유랑종이
  여섯 항목으로 1 page, `Pages` 와 `PageRange` 의 경계값(0, 8, 9, 14, 15, 28, 29),
  모든 항목이 정확히 한 page 에 한 번씩 나온다.
- 화면은 배치 실행으로 볼 수 없다. 사람이 볼 것은 PR 의 Risks 에 적는다.

## Risks & Rollback

- **Risks:** 선택 목록 폭이 58px 줄어 창 폭 868px 미만에서 목록이 사라진다
  (`List` 가 `ChipWidth` 미만이면 그리지 않는다). 네 번째 열이 미니맵·선택
  목록과 겹치지 않는지, `M` 키가 More 를 누르는지는 눈으로 봐야 한다.
- **Rollback steps:** `git revert`. 시뮬레이션과 hash 에 닿지 않는다.

## Open Questions

- 없음.
