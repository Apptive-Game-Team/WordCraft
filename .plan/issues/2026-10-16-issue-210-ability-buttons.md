# 2026-10-16: 명령 카드의 능력 버튼과 지점 조준, 쿨다운 표시

- Date: 2026-10-16
- GitHub Issue: #210
- Status: In progress (draft PR, #214 와 #207 위에 쌓음)

## Goal

묶음 8 의 `Cast` 명령(#209)을 사람이 쓸 수 있게 한다. 유닛 카드에 대표 몸의 액티브
능력 버튼을 놓고, 지점 목표 능력은 조준 흐름으로 보낸다. 시뮬레이션은 건드리지 않는다.
뷰는 읽기만 하고 모든 주문은 `Produce`, `Research` 와 같이 명령으로 나간다.

## 설계

### AbilityMenu (UnityEngine 없음)

`ProductionMenu`, `ResearchMenu` 와 같은 자리. `Replay` 가 컴파일해서 돌린다.

- `For(faction, role, slot)`: 그 몸이 가진 액티브 행(`FactionData.IsCast`)을 진영 능력
  표 index 순으로. `Entry` 는 표 index, 이름, 행.
- 칸 배치: 전사 카드 `3, 7, 11, 8, 9, 10`, 일꾼 카드 `3, 7, 11, 9, 10`(8번은 `Build`).
  열 하나(`U J M`)가 먼저 차고 그다음 2행이 찬다. 넘치면 마지막 칸이 `More` 가 되고
  page 당 칸 수 - 1 개를 놓는다(`ProductionMenu` 와 같은 규칙).
- 칸 상태: `Locked`(해금 연구 전, `World.AbilityUnlocked`), 쿨다운 남은 비율
  (`AbilityCooldown / Cooldown`, 전개는 전환 시계 `AbilityTicksLeft / Ticks`).
- 라벨: 이름, 비용이 있으면 둘째 줄에 `World.AbilityCost`(깨우기 비용 연구 반영).
- 오토캐스트 표시: 질풍, 굴러 들이받기, 잔해 수리, 수리. 문서 능력 표의 종류 열이
  `오토캐스트` 인 네 행이다. 정보 패널에 `이름  autocast` 줄로만 나간다.

### 카드와 주문

- `CardSlot` 에 `Locked`, `Cooldown`(남은 비율) 을 더한다. 능력 칸은 `Type = Cast`,
  `Slot = 진영 능력 표 index`.
- `CommandCard.Of` 에 대표 id 를 받는다(쿨다운은 몸의 상태라서).
- `Orders.Run`: 목표 없음은 바로 `Cast`, 지점 목표는 `Pending = Cast` 와 `CastIndex`.
  다음 땅 클릭이 `Cast` 를 보낸다. ESC 와 오른쪽 클릭은 기존 `Cancel` 이 푼다.
- 같은 능력(`Role`, `Slot` 이 행과 같은 몸)을 가진 선택 유닛 모두에게 보낸다.
  문서 `docs/TECH-TREE.md` 는 어느 능력도 대표 하나에게만 보내라고 적지 않았다.
  그래서 예외 목록은 비어 있고, 이 사실을 PR 에 적는다.
- 해금 전 칸은 키도 막는다(버튼이 죽은 면이라서). 쿨다운 중인 칸은 막지 않는다:
  대표만 쿨다운이고 나머지는 준비된 선택도 있고, 거부는 시뮬레이션이 한다.
- 조준 ring: `MatchView` 한 곳에서 `Pending == Cast` 이고 지점 목표인 동안 대표
  유닛 위치에 행의 `CastRange` 로 `RangeRing` 을 `Accent` 색으로 그린다.

### Hud

- 능력 칸: 해금 전 `Void` 비활성 셀, 쿨다운 중 셀 아래 `Meter`.
- 정보 패널: 대표가 가진 오토캐스트 능력을 `autocast` 상태 줄로.
- 조준 중 안내 줄에 능력 이름.

## 하네스

`Replay/AbilityMenuChecks.cs`(등록 한 줄): 모든 진영의 모든 몸이 카드 칸 안에 들어가는지
(넘치면 page 수가 `MaxPages` 안인지), page 경계, 해금 전과 쿨다운 중 칸 상태, 라벨의 비용이
시뮬레이션이 받는 값과 같은지. 검사마다 한 번씩 일부러 깨서 실패를 확인한다.

## 문서

`docs/UI-STYLE.md` 에 능력 셀 규칙 한 절.

## 검증

`dotnet build`(경고 0), `dotnet run --project Replay`, Unity `ViewSelfCheck`.
