# 2026-10-16 — 상태 이상과 오라: 감속, 화상, 연쇄, 피해·주기 오라, 치유 오라, 화상 오라, 과생장, 덩굴 지대

- Date: 2026-10-16
- GitHub Issue: #200
- Status: Implemented (draft PR, #199 위에 쌓음)

## Goal

`docs/TECH-TREE.md` 구현 순서 묶음 5. 능력 표 M11과 해금 연구 행 12개, 새 시스템
`StatusSystem`, 새 해시 필드 둘(`Entity.SlowTicksLeft`, `Entity.BurnTicksLeft`).
`ContentVersion` 35 → 36. `MaxUpgrades` 9 → 12(지옥불 군단이 12행).

## API contract (`Sim/FactionData.cs`)

```csharp
public enum AbilityKind { None, SlowOnHit, BurnOnHit, ChainOnHit, WoundedRate, ArmorAura,
    ArmorPerAlly, ArmorWhileStill, MarkAura, RateAura, HealAura, BurnAura, GrowthAura, SlowAura }
public enum AbilityTargets { None, Bodies, BodiesAndBuildings, RangedBodies, DefenseBuildings, GroundBodies }

public struct AbilityRow
{
    public string Name;
    public Faction Faction; public Role Role; public int Slot;   // 보유 항목
    public AbilityKind Kind;
    public AbilityTargets Targets;
    public int Unlock;      // 해금 연구 index, -1이면 기본
    public int Cooldown;    // 이 묶음에서는 전부 0 (묶음 7)
    public int Radius, Amount, Ticks, Period, Limit;
    public int Boost;       // 수치 연구 index, -1이면 없음
    public int BoostedRadius, BoostedAmount, BoostedTicks; // 0이면 그 수치는 그대로
}

FactionData.AbilityCount(faction); FactionData.Ability(faction, index);
FactionData.AbilityRowCount; FactionData.AbilityRowAt(i);
```

`Unlock`·`Boost`는 표에 `UpgradeEffect`로 적고 정적 생성자가 그 진영 업그레이드 표의
index로 바꾼다. 보유 항목이 로스터에 없거나 연구 행이 없으면 type load에서 실패한다.

| 진영 | 능력 | 보유 | 종류 | 해금 | 수치 | 수치 연구 |
|---|---|---|---|---|---|---|
| 세계수 | 휘감기 | Ranged 1 | SlowOnHit | 기본 | 40틱 | 휘감기 지속 60 |
| 세계수 | 연쇄 번개 | Ranged 0 | ChainOnHit | 연쇄 번개 | 2칸, 2번 | |
| 세계수 | 과생장 | Signature 1 | GrowthAura | 기본 | 3칸, 짝수 틱 | |
| 세계수 | 덩굴 지대 | Signature 2 | SlowAura | 기본 | 3칸 | |
| 세계수 | 회복 | Defense 1 | HealAura | 기본 | 4칸, +2, 20틱 | |
| 지옥불 | 화상 오라 | Melee 0 | BurnAura | 기본 | 2칸, 2, 20틱 | 화상 오라 강화 3 |
| 지옥불 | 화염탄 | Ranged 1 | BurnOnHit | 화염탄 | 60틱 | |
| 지옥불 | 피 냄새 | Melee 2 | WoundedRate | 피 냄새 | 주기 8 | |
| 지옥불 | 집결 | Defense 1 | RateAura | 기본 | 5칸, −3, 하한 5 | 집결 반경 7 |
| 물 | 물막이 | Melee 1 | ArmorAura | 기본 | 2칸, +1 | |
| 물 | 거품 벽 | Melee 0 | ArmorPerAlly | 거품 벽 | 2칸, 1기당 +1, 최대 2 | |
| 물 | 회복의 비 | Signature 1 | HealAura | 기본 | 3칸, +2, 20틱 | 회복의 비 강화 3 |
| 돌 골렘 | 이끼 재생 | Signature 2 | HealAura | 기본 | 3칸, +3, 20틱 | 이끼 재생 강화 5 |
| 유랑종 | 굳은 몸 | Melee 2 | ArmorWhileStill | 기본 | +3 | |
| 유랑종 | 생명 기록 | Signature 3 | HealAura | 기본 | 3칸, +3, 20틱 | |
| 인간 | 표식 | Signature 1 | MarkAura | 기본 | 5칸, +1 | 표식 강화 2 |
| 인간 | 증폭 | Signature 3 | RateAura | 기본 | 4칸, −5, 하한 8 | 증폭 반경 6 |
| 인간 | 연쇄 방전 | Defense 1 | ChainOnHit | 연쇄 방전 | 2칸, 1번 | |

## 시스템 자리

- `CombatSystem`: 쿨다운을 세울 때 `AttackTicksAgainst(a, t)`(피 냄새, 집결, 증폭). 피해는
  `DamageAfterArmor(피해 + VolleyBonus + 표식, 공격력, ArmorFor + 오라 방어(물막이, 거품 벽, 굳은 몸), 방어력)`.
  살아남은 대상에 휘감기·화염탄. 그 뒤 연쇄(지역 `stackalloc` 배열).
- `StatusSystem`(새, `CombatSystem` 뒤·`FleeSystem` 앞): id 오름차순으로 감속 1 감소, 화상 1 감소 뒤
  10의 배수면 고정 1. 그다음 주기가 맞는 틱에 화상 오라(원천마다), 마지막으로 치유 오라.
- `MoveSystem`: `SpeedFor(e)`에 감속(휘감기 또는 덩굴 지대, 합쳐도 × 1/2 한 번)을 곱하고 3/4칸/틱에서 자른다.
- `GrowthSystem`: 짝수 틱에 도깨비불 3칸 안의 성장 중인 아군 몸은 `GrowStep`을 한 번 더 부른다.
- `Kill`이 두 필드를 0으로 지운다.

## Decisions

- "유닛"은 `EntityKind.Unit`과 `Worker`를 함께 뜻한다(일꾼도 치유·화상 오라·감속을 받는다).
- 오라 원천은 살아 있고, 성장 중이 아니고, 건물이면 완공된 몸이다. 원천 자신도 오라 안에 있다
  (거품 벽만 "다른" 거품 정령을 센다).
- 감속은 `StatusSystem`이 먼저 1을 줄이고 남은 값이 0보다 크면 그 틱 걸음이 느리다. 40을 세우면
  39번 걸음이 느려진다(화상의 "먼저 줄인다"와 같은 규칙).
- 화상 피해 1과 주기 10은 몸이 불을 붙인 쪽을 기억하지 않으므로 `World` 상수다.
- 주기 오라의 하한은 원래 주기보다 길게 만들지 않는다: `min(주기, max(하한, 주기 − 감소))`.
- 연쇄는 1차 명중의 최종 피해를 절반씩 줄인다. 튄 대상의 방어와 오라는 다시 묻지 않는다.
- 하네스가 두 월드를 상태 시계 하나만 다르게 만들 수 있도록 `World.SetStatusForScenario`를 더했다
  (`GrantResources`처럼 시나리오 전용). 몸을 다치게 하는 것도 이 화상으로 한다.
- 주기 오라의 하한은 오늘 로스터에서 걸리는 조합이 없다(피 냄새 8 − 집결 3 = 하한 5). 하한 줄을
  지워도 하네스가 통과하는 것을 확인했고, PR에 적는다.

## 결과

- 골든 해시는 두 번 움직였다: `MaxUpgrades` 9 → 12로 `0x42E9F28F9B63D88D` → `0x2C507449587F068D`,
  새 필드 둘로 `0xC7F3354AEC08548D`. 두 값 모두 CoreCLR와 Unity Mono가 같다. 로그의 용암 갑각 악마는
  20의 배수 틱에 적 지상 몸 2칸 안에 서지 않아 화상 오라로는 움직이지 않았다(탐침으로 0회 확인).

## Validation

`dotnet build`, `dotnet run --project Replay`, `dotnet run --project Host -- selfcheck`,
`dotnet run -c Release --project Host -- arena -games 2`, Unity `ViewSelfCheck`.

## 리뷰 반영

- 오라 원천의 위치를 패스 시작에 원천 id·행과 함께 담고 거기서 잰다. 덩굴 군집이 `MoveSystem`
  안에서 걸어서, 그때그때 읽으면 id가 낮은 몸과 높은 몸이 서로 다른 위치를 쟀다. 거울처럼 선 두
  일꾼 검사가 고치기 전 9번째 틱에 실패했다.
- 연쇄는 건물로도 튄다(`AcquireTarget`과 같은 범위). 문서를 "적(유닛, 일꾼, 건물)"으로 고쳤다.
- type load가 수치 없는 행(과생장, 덩굴 지대, 연쇄, 명중 시 감속·화상 제외)과 하한·상한 없는
  주기 오라·거품 벽 행을 거부한다. `IsAura`는 `FactionData`로 옮겼다.
- 분리 밀기의 상한은 해시된 `e.Speed / 2` 그대로 둔다. 밀기는 두 몸이 겹친 만큼을 푸는 것이고
  걷는 빠르기와 관계없으며, 어떤 행 속도의 절반도 반 칸 미만이다. 규칙 변경 없음.
- `Kill`이 두 시계를 지우는 검사, 섞인 월드에서 감속·화상·연쇄가 실제로 일어났는지 세는 검사,
  나머지 네 진영을 피어 0~3에 둔 월드의 두 번 실행 검사를 더했다.
