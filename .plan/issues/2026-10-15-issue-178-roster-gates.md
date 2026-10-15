# 2026-10-15 — 슬롯 단위 기술 단계·선행 건물·생산 건물 목록 게이트

- Date: 2026-10-15
- GitHub Issue: #178
- Status: Implemented, awaiting review

## Goal

`docs/TECH-TREE.md` `## 데이터 모델 확장`의 M1·M2·M3·M4·M5·M7과 `## 구현 순서` 묶음 1을
구현한다. 기술 단계, 선행 건물, 단계를 여는 건물, 생산 건물 목록, 연구 건물을 역할이
아니라 `(Faction, Role, Slot)` 항목이 답한다. 전부 정적 표이고 새 hash 필드는 없다.

## Non-goals

- 로스터 행 추가(묶음 3), 기존 행 수치 정리(묶음 2), M6·M8·M9·M10·M11.
- HUD에서 잠긴 버튼을 회색으로 그리는 것.
- `docs/TECH-TREE.md`, `Net/` 수정.

## Context / Constraints

- `Sim/` 결정론 규칙 전부. 새 표는 static이고 선언 순서로 훑는다.
- 기존 항목의 단계·선행·생산 건물은 TECH-TREE.md 각 진영 표를 따른다. 표가 아직 없는
  항목(바람 토템, 큰 균열, 구름 둥지, 각성의 원, 석조 병영, 마도 공창)을 가리키는 값은
  그 행이 묶음 3에서 들어올 때 함께 적는다. 지금 적으면 type load에서 실패한다.
- 표가 그 진영에 없는 항목을 가리키면 type load에서 실패한다(`statOverrides`와 같은 방식).
- 규칙이 바뀌므로 `ContentVersion` 30 → 31.
- 골든 해시가 움직인다(스크립트 로그가 본진에서 근접 유닛을 뽑는다). 두 런타임에서
  같은 값을 확인한 뒤 `ScriptedLog.cs`를 고친다.

## 계약

```csharp
// Sim/FactionData.cs
public static int Tier(Faction faction, Role role, int slot);            // Tier(Role) 삭제
public static (Role Role, int Slot) Prerequisite(Faction faction, Role role, int slot); // Role.None = 없음
public static int GrantsTier(Faction faction, Role role, int slot);      // 0, 2, 3
public const int MaxProducers = 2;
public static (Role Role, int Slot) ProducedAt(Faction faction, Role role, int slot, int index); // Role.None = 없음
public struct UpgradeRow { ...; (Role Role, int Slot) ResearchAt; (Role Role, int Slot) Requires; }

// Sim/World (Economy.cs)
public bool HasStanding(int peer, Role role, int slot);  // 완공·생존 건물
public bool CanProduce(int peer, int buildingId, Role role, int slot); // 자원·인구·대기열 제외 게이트

// Sim/Hellfire.cs, Sim/Combat.cs
public const int WarlordSlot = 0;   // IsWarlord가 슬롯을 묻는다
public const Role ArcherRole = Role.Ranged; public const int ArcherSlot = 0; // VolleyBonus
```

## Approach (Checklist)

- [x] **Step 0: Recon.** `Sim/` 전부, `Replay/Program.cs`의 본진 생산 fixture, `ScriptedLog.cs`.
- [x] **Step 1: M1.** `tierOverrides` 12행, `Tier(Faction, Role, int)`. `CanBuild`·`TryQueueUnit`이 슬롯으로 묻는다.
- [x] **Step 2: M2.** `prerequisites` 표(기술 건물 6행 + 유닛 12행), `HasStanding`, 생산·건설·성장·연구 게이트.
- [x] **Step 3: M3.** `GrantsTier`, `TierOf`가 읽는다.
- [x] **Step 4: M4.** `producerOverrides`(본진·생산 건물 둘 다인 4행), `ProducedAt`, `TryQueueUnit` 거부.
- [x] **Step 5: M5.** `UpgradeRow.ResearchAt`·`Requires`, `CanResearch`.
- [x] **Step 6: M7.** `IsWarlord`·`VolleyBonus`가 0번만.
- [x] **Step 7: 성장.** `TryGrow`가 대상의 단계와 선행을 묻는다.
- [x] **Step 8: AI.** 건물마다 그 건물이 지금 뽑을 수 있는 항목 중 보유 자원으로 살 수 있는 가장 높은 단계를 고른다. 일꾼은 일꾼 생산 건물에서.
- [x] **Step 9: 하네스.** 본진에서 전투 유닛을 뽑던 fixture에 생산 건물. `Replay/RosterGateChecks.cs`.
- [x] **Step 10: 골든 해시, `ContentVersion` 31, `docs/FACTION-MECHANICS.md` `## 테크트리`.**

## Validation

- **Commands to run:** `dotnet build`, `dotnet run --project Replay`,
  `dotnet run --project Host -- selfcheck`, Unity `ViewSelfCheck` (unity-tools `run.sh`).
- **Expected output:** 0 warnings, `OK: all determinism checks passed`,
  `OK: all view determinism checks passed`, CoreCLR와 Mono의 final hash가 같다.
- **결과:** 골든 해시 `0x20B573A2BAE79B6F` → `0x45FA012D9A76F9AB`. CoreCLR(Replay)와
  Unity Mono(`ViewSelfCheck`)가 같은 값을 냈다. 스크립트 로그의 `Produce` 네 개를 본진에서
  생산 건물(entity 18, 19)로 옮겼다.

## Risks & Rollback

- **Risks:** AI 생산 구성이 바뀐다(T3가 서면 상위 유닛). 솔로 매치 하네스가 그 결과를 본다.
  기록된 리플레이는 `ContentVersion`이 달라 재생되지 않는다.
- **Rollback steps:** `git revert`로 이 PR의 커밋을 되돌린다. 해시 필드가 없어 데이터 이전은 없다.

## 묶음 1 placeholder

- 바람 정령(세계수 `Signature` 0)의 선행은 문서상 바람 토템(`Tech` 1)이고, 마도 정찰기(인간
  `Signature` 1)의 생산 건물은 마도 공창(`Production` 2)이다. 둘 다 묶음 3 전에는 없는
  건물이라, 묶음 1은 바람 정령 선행 `Tech` 0(뿌리 회당), 마도 정찰기 생산 `Production` 0(마법 탑)
  ·선행 `Tech` 0(공방)으로 적는다. 묶음 3이 건물 행을 넣을 때 바꾼다.
- 군단장·구름 용·고대 골렘·공수 특공대의 둘째 생산 건물(큰 균열, 구름 둥지, 각성의 원,
  석조 병영)도 묶음 3에서 적는다. 그때까지 `Production` 0 하나다.

## 리뷰 후속 (PR #181)

- 선행 검사를 `return true`로 바꿔도 하네스가 통과했다. `APrerequisiteRefusesWhereTheTierWouldAllow`를
  더했다. 뿌리 회당이 서 있고 풀씨 둥지가 없는(처음부터 없거나 무너진) 세계수 정령은 T3인데도
  둘째 뿌리 회당 건설과 고목 수호자 성장을 거부당하고, 풀씨 둥지가 서면 둘 다 받는다.
  같은 변이를 다시 넣으면 이 check가 실패한다. 단계와 선행은 게임 안에서도 따로 거부된다.
- `CanResearch`의 `Role.Tech` 검사를 지웠다. 어느 건물이 연구하는지는 `ResearchAt` 한 줄이
  정한다. 그 줄을 지우면 하네스가 실패한다.
- `FactionData.Tier`가 로스터에 없는 항목(빈 0번 포함)에 `MaxTier + 1`을 답한다.
- `docs/FACTION-MECHANICS.md` 연구 거부 순서의 5번과 6번을 코드 순서대로 바꿨다.
- 골든 해시는 움직이지 않았다(`0x45FA012D9A76F9AB`, CoreCLR과 Mono 같음).
