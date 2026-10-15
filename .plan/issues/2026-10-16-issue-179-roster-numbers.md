# 2026-10-16 — 기존 로스터 행의 문서 수치와 공중·지상 전용 플래그 적용

- Date: 2026-10-16
- GitHub Issue: #179
- Status: Implemented (draft PR)

## Goal

`docs/TECH-TREE.md` `## 구현 순서` 묶음 2를 구현한다. 기존 로스터 행이 문서가 준 수치와
플래그를 그대로 가진다.

- `†` 수치: 풀씨 정령, 용암 갑각 악마, 거품 정령, 물결 궁수, 꼬마돌, 이끼바위 골렘, 바위
  술사, 수습 마법생, 공수 특공대의 자원·틱·체력·피해·사거리·속도, 마법 탑(생산) 체력 220.
- 바람 정령(세계수 `Signature` 0)과 구름 용(물 슬라임 `Signature` 0)이 `Air = true`.
- 용암 아가리(지옥불 `Defense` 0)와 굴림바위 언덕(돌 골렘 `Defense` 0)이 `HitsAir = false`.
- 마도 정찰기(인간 `Signature` 1)가 `Damage = 0`.
- 하네스 예외 목록 갱신, 새 검사 `Replay/RosterNumbersChecks.cs`.
- `ContentVersion` 31 → 32, 골든 해시 갱신.
- 문서: `docs/FACTIONS.md` 아트 현황, `docs/FACTION-MECHANICS.md` 수치·공중 절,
  `docs/TECH-TREE.md` 불일치 #3~#7, #12~#14에 "반영됨".

## Non-goals

- 공수 특공대·돌 포탑 지상 전용(문서의 열린 결정, 채택하지 않는다).
- 마도 정찰기 공중(표에 `제안`으로 적힌 값이라 이 묶음에 넣지 않는다).
- `방어` 열(`Armor`) 값. 문서가 "방어력 열은 전부 제안"이라고 적었다.
- 새 행(묶음 3), 능력, 밸런스 재조정, AI 사다리 변경(묶음 9).
- `Host/`, `Net/`, `Client/Assets/Scripts/` 수정.

## Context / Constraints

- `Sim/` 결정론 규칙. 새 hash 필드 없음. 전부 정적 표.
- `docs/TECH-TREE.md`와 `docs/FACTION-MECHANICS.md`가 다르면 TECH-TREE.md를 따른다.
- `#178`(`feat/178`, PR #181) 위에 쌓고 `feat/170`(arena, PR #172)을 merge한 상태에서 측정한다.
- 골든 로그가 용암 갑각 악마를 뽑으므로 골든 해시가 움직인다. CoreCLR과 Unity Mono의 값을
  확인한 뒤 `ScriptedLog.cs`를 고친다.

## Approach (Checklist)

- [x] **Step 0: Recon.** `Sim/FactionData.cs` override 표, `Sim/Combat.cs` `CanHit`·`CanAttack`,
  `Replay/Program.cs` 예외 목록, `ScriptedLog.cs`.
- [x] **Step 1: `†` 수치.** `statOverrides`·`productionOverrides`에 행 추가.
- [x] **Step 2: 공중.** 바람 정령·구름 용 행 `Air = true`.
- [x] **Step 3: 지상 전용.** 용암 아가리·굴림바위 언덕 행 `HitsAir = false`.
- [x] **Step 4: 마도 정찰기 무장 해제.**
- [x] **Step 5: 하네스.** `EveryWeaponReachesSomething`, `OnlyNonCombatantsAreUnarmed`,
  `EveryUnarmedSlotIsWorthSomethingElse` 갱신. 수치 변경으로 깨지는 fixture 수정.
  `Replay/RosterNumbersChecks.cs` 추가.
- [x] **Step 6: `ContentVersion` 32, 골든 해시.**
- [x] **Step 7: 문서.**

## Implementation Notes

- 스크립트 로그에서 피어 1의 두 번째 `Produce`(틱 420)를 뺐다. 용암 갑각 악마가 90자원이라
  두 기를 사면 틱 480에 35자원만 남아 방어 건물(100자원)을 세우지 못하고,
  `ScriptedLogPlacesEveryBuilding`이 실패한다.
- 기존 fixture 넷이 공유 가격·시간·피해를 가정했다. 풀씨 정령 가격(인구 상한, 취소 환불),
  용암 갑각 악마 80틱(통로 도착 대기), 용암 갑각 악마 피해 14(군단장 대조 유닛이 먹이를 죽임).
  각각 표에서 값을 읽게 고쳤다.
- `IsNonCombatant`에 마도 정찰기를 더하고 `EveryUnarmedSlotIsWorthSomethingElse`는 묶음 5의
  표식이 들어올 때까지 이 행을 이름으로 통과시킨다.
- `docs/FACTION-MECHANICS.md`는 한 줄 외에 공중 절의 공중 유닛 목록·지상 전용 목록과 인간 절
  아트 문장(불일치 #12)도 고쳤다. 고치지 않으면 #4·#5·#12를 반영됨으로 적을 수 없다.
- `Sim/FactionData.cs`의 `sprites` 주석 둘(불일치 #12, #14)을 고쳤다.

## Validation

- **Commands to run:** `dotnet build`, `dotnet run --project Replay`,
  `dotnet run --project Host -- selfcheck`, `dotnet run -c Release --project Host -- arena -games 2`,
  Unity `ViewSelfCheck` (unity-tools `run.sh`).
- **Expected output:** 0 warnings, `OK: all determinism checks passed`, selfcheck OK,
  `OK: all view determinism checks passed`, CoreCLR와 Mono의 final hash가 같다.

## Risks & Rollback

- **Risks:** 근접만 가진 AI 부대는 바람 정령·구름 용을 때리지 못한다(묶음 9의 몫).
  AI가 T3에서 무기 없는 마도 정찰기만 사던 문제는 `AiFighterAt`이 무기 없는 항목을
  자원 검사 전에 건너뛰게 고쳤다. 그래도 arena 인간 승률은
  8.33%다. 인간이 사는 유일한 무기 유닛 공수 특공대가 65자원 55틱이 된 것이 원인이다.
  이 수정에 공수 특공대를 임시로 20자원 40틱으로 되돌리면 다섯 진영 상대 10판 중 6승이다.
  같은 규칙 때문에 지옥불 AI는 군단장을, 차원 유랑종 AI는 경계 운반자를 사지 않는다.
  `Host/SelfCheck.cs`의 `ArenaTicks`를 1200에서 2000으로 올렸다(용암 갑각 악마 90자원 80틱).
  기록된 리플레이는 `ContentVersion`이 달라 재생되지 않는다.
- **Rollback steps:** `git revert`로 이 PR의 커밋을 되돌린다. 해시 필드가 없어 데이터 이전은 없다.

## Open Questions

- 마도 정찰기의 가치(표식)는 묶음 5에서 들어온다. 그때까지 `EveryUnarmedSlotIsWorthSomethingElse`가
  이 행을 이름으로 예외 처리한다.
