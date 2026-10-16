# 2026-10-16: 한시 소환과 사망 폭발: Lifetime, ExpirySystem, BurstSystem

- Date: 2026-10-16
- GitHub Issue: #202
- Status: Implemented (draft PR, #205 위에 쌓음)

## Goal

`docs/TECH-TREE.md` 구현 순서 묶음 6. 새 해시 필드는 `Entity.ExpireTick` 하나다(0이면 영구).
`ContentVersion` 36 → 37. `MaxUpgrades`는 12 그대로다(세계수 정령 11 → 12, 차원 유랑종 9 → 12).

## 정적 데이터 (`Sim/FactionData.cs`)

- `UnitStats.Lifetime`(틱, 0이면 영구). `lifetimes` 표 다섯 행이 정적 생성자에서 채운다:
  차원 유랑종 Melee 1·Melee 2·Ranged 1·Ranged 3·Signature 3, 전부 600.
- `AbilityKind` 셋 추가: `ExpiryAura = 14`, `DeathBurst = 15`, `ArrivalBurst = 16`.

| 진영 | 능력 | 보유 | 종류 | 대상 | 수치 | 수치 연구 |
|---|---|---|---|---|---|---|
| 세계수 | 전격 과부하 | Melee 2 (찌릿쥐) | DeathBurst | Bodies | 2칸, 고정 12 | 과부하 강화 18 |
| 유랑종 | 화염 분출 | Melee 1 | DeathBurst | GroundBodies | 2칸, 고정 12 | 화염 분출 강화 18 |
| 유랑종 | 도착 방전 | Ranged 1 | ArrivalBurst | Bodies | 2칸, 고정 10 | |
| 유랑종 | 층위 고정 | Signature 2 | ExpiryAura | Bodies | 4칸, 짝수 틱 | 층위 고정 반경 6 |

연구 행 넷: 과부하 강화(광란의 토템 Tech 2, T2, 75, 120), 소환 수명(층위 관측대 Tech 0,
T3, 150, 200, 600 → 800), 층위 고정 반경(Tech 0, T3, 100, 160), 화염 분출 강화(외피 연마장
Tech 1, T2, 75, 120).

## 시스템 (`Sim/`)

- `SpawnUnit`이 `Lifetime > 0`인 행에 `ExpireTick = Tick + LifetimeFor(owner)`를 쓴다.
  `Kill`은 `ExpireTick`을 0으로 지운다.
- `ExpirySystem`: `SpawnerSystem` 뒤, `CombatSystem` 앞. 짝수 틱이면 층위 고정이 닿는
  아군 한시 몸의 `ExpireTick`을 1 늘리고(몸마다 한 번), 그다음 id 오름차순으로
  `Tick >= ExpireTick`인 몸을 `Kill`한다.
- `pendingBursts`: 해시하지 않는 `List` 대기열 (원천 id, 능력 행, 위치). `Kill`은
  사망 폭발 행이 있는 몸이면 넣기만 한다. `ProductionSystem`은 도착 방전 행의 몸이 나오면
  선 자리로 넣는다.
- `BurstSystem`: `StatusSystem` 뒤, `FleeSystem` 앞. 넣은 순서대로 비우고, 원천 진영의 적
  중 대상에 맞고 반경 안인 몸에 `DamageAfterArmor(수치, 0, 0, 0)`(고정 피해, 방어 무시)을
  준다. 폭발로 죽은 몸은 같은 대기열 끝에 붙어 같은 순회에서 처리된다. 끝나면 비운다.
  `World.PendingBurstCount`는 하네스용이다.

## 하네스 (`Replay/SummonChecks.cs`)

능력 표 행, 600틱 만료, 영구 행 `ExpireTick` 0, 층위 고정 지연, 사망 폭발이 `BurstSystem`
에서 피해를 주고 연쇄 사망이 같은 틱, 도착 방전, 연구 수치, 틱 끝 대기열 비어 있음,
두 월드 해시 일치, 해시가 `ExpireTick`에 민감함. 검사 14개를 하나씩 일부러 깨서 모두 실패하는 것을 확인했다.

## 결과

- 골든 해시 `0xC7F3354AEC08548D` → `0x39F72E92D095408D`. CoreCLR과 Unity Mono가 같은 값을 냈다.
- #205 리뷰 수정(`IsAura`가 `FactionData`로 이동, 더 엄격한 능력 `Require`)을 병합하면서 `ExpiryAura`를
  `IsAura`에, 폭발 두 종류를 반경·대상 `Require`에 넣었다.

## 문서

`docs/FACTION-MECHANICS.md` `## 능력`에 묶음 6 행과 폭발·만료 규칙, `docs/TECH-TREE.md`
묶음 6과 연구 행 배치 6에 "반영됨(#202)".
