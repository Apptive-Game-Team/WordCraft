# 2026-10-15 — 업그레이드 연구: 공격·방어 단계와 진영 고유 연구

- Date: 2026-10-15
- GitHub Issue: #164
- Status: Implemented, awaiting review

## Goal

기술 건물(`Role.Tech`)이 업그레이드를 연구한다. 여섯 진영 모두 `공격력` 3단계,
`방어력` 3단계, 진영 고유 연구 2종을 가진다. 연구 상태는 결정론 state 이고 전부
hash 된다. 효과는 state 가 아니라 단계에서 계산하는 조회다.

## Non-goals

- 명령 카드·HUD 의 연구 버튼(후속 이슈). `Client/Assets/Scripts/` 는 건드리지 않는다.
- 유닛 로스터 확장, active ability.
- 밸런스 조정. 수치는 문서화한 출발값이다.

## Context / Constraints

- `Sim/` 결정론 규칙 전부. float 금지, id 순회, 새 state 는 `World.Hash()`.
- `Command` 는 Net·Replay 에서 필드 단위로 직렬화된다. 새 필드 없이 `EntityId`·`Arg`
  만 쓴다. `CommandType` 은 1바이트라 값 14 가 그대로 건너간다.
- 완공된 기술 건물이 있으면 `TierOf` 가 곧 3 이다. 그래서 행의 `Tier` 검사는
  완공 검사 뒤에서 독립적으로 거부할 경우가 오늘은 없다. 하네스는 짓는 중인 기술
  건물(아직 2단계)로 거부를 확인한다.
- 규칙이 바뀌므로 `FactionData.ContentVersion` 29 → 30.

## 계약

```csharp
// Sim/FactionData.cs
public struct UnitStats { public int Armor; }            // 기본 0, 모든 행 0
public enum UpgradeEffect { Attack, Armor, GrowthTime, GrowthHpRamp, OffspringCap,
    OffspringPeriod, VolleyCap, VolleyRadius, RemnantTicks, GolemMeleeRange,
    ArrivalRadius, ArrivalTicks, CaptureTicks, CannonRange }
public struct UpgradeRow { string Name; int Resources; int Ticks; int MaxLevel;
    int Tier; UpgradeEffect Effect; }
public const int MaxUpgrades = 4;
public static int UpgradeCount(Faction faction);
public static UpgradeRow Upgrade(Faction faction, int index);   // 범위 밖은 MaxLevel 0

// Sim/Command.cs
CommandType.Research = 14;   // EntityId = 기술 건물, Arg = 업그레이드 index

// Sim/Entity (World.cs)
public int ResearchIndex;      // -1 이 없음
public int ResearchTicksLeft;

// Sim/Research.cs (World)
public int UpgradeLevel(int peer, int index);
public int UpgradeLevel(int peer, UpgradeEffect effect);
public int ResearchCost(int peer, int index);            // Resources * (현재 단계 + 1)
public bool CanResearch(int peer, int buildingId, int index);
public int AttackBonus(int peer);   public int ArmorBonus(int peer);
public static int DamageAfterArmor(int damage, int attackBonus, int armor, int armorBonus);
public int GrowthTicksFor(int peer, Role role, int slot); public int GrowthRampFor(int peer);
public int WarlordOffspringCap(int peer); public int WarlordSpawnTicksFor(int peer);
public int VolleyMaxBonusFor(int peer);   public Fix VolleyRadiusFor(int peer);
public int RemnantTicksFor(int peer);     public Fix RangeOf(Entity e);
public Fix ArrivalRadiusFor(int peer);    public int ArrivalTicksFor(int peer);
public int CaptureTicksFor(int peer);
```

규칙:

- 피해: `max(1, Damage + AttackBonus(공격자) + VolleyBonus - (Armor + ArmorBonus(피격자)))`.
  공격·방어 단계당 +1. 방어 보너스는 피격자 소유 피어의 것이고, 중립(-1)은 0.
- `Research` 거부 순서: 소유·생존 → `Role.Tech` 완공 → 이 건물이 이미 연구 중 →
  index 범위·최대 단계 → 같은 피어의 다른 건물이 같은 index 를 연구 중 → 행의
  `Tier` → 자원. 통과하면 `Resources * (단계 + 1)` 을 즉시 전액 차감.
- `ResearchSystem` 은 `ProductionSystem` 바로 뒤, 엔티티 id 오름차순. 끝나면
  `upgradeLevels[peer, index]++`.
- 건물이 죽으면 `Kill` 이 연구를 지운다. 환불 없음.
- `CancelProduction`: 큐가 있으면 기존대로 큐 꼬리를 취소하고, 큐가 비어 있고 연구
  중이면 연구를 취소하고 전액 환불한다.
- hash: 엔티티별 `ResearchIndex`·`ResearchTicksLeft`, 피어별 `upgradeLevels[p, i]`
  (p 0..7, i 0..3). 새 hash 필드는 이 셋뿐이다.

출발값 (모든 진영 공통 형식):

| index | 이름 | 자원 | 틱 | 최대 | Tier |
|---|---|---|---|---|---|
| 0 | 공격력 | 50 (2단계 100, 3단계 150) | 160 | 3 | 3 |
| 1 | 방어력 | 50 (2단계 100, 3단계 150) | 160 | 3 | 3 |
| 2 | 고유 연구 1 | 100 | 200 | 1 | 3 |
| 3 | 고유 연구 2 | 100 | 200 | 1 | 3 |

처음 계획은 100·150 이었다. 솔로 매치 AI 가 기술 건물을 세운 뒤 보유 자원이 90 을
넘지 않아서 100 에서는 연구를 한 번도 못 했고, 그래서 50·100 으로 내렸다.

고유 연구는 이슈 표를 따른다. 세계수 정령 성장 시간 25% 단축(40→30, 60→45),
성장 중 체력 상승 폭 2배(절반 시점에 목표 도달), 지옥불 자손 상한 5→7, 방출 주기
100→70틱, 물 슬라임 밀집 상한 4→6, 반경 3→4칸, 돌 골렘 잔해 400→600틱, 근접
사거리 +1, 차원 유랑종 도착 반경 6→8칸, 통로 도착 시간 60→40틱(통로 시계
`PassageArrivalTicks` 로 값이 매겨진 항목, 오늘은 틈새 사수), 인간 포획 60→40틱,
대포 사거리 +1.

## Approach (Checklist)

- [x] **Step 0: Recon** — Combat, Economy, World, 진영 파일, Ai, Replay 하네스, ScriptedLog.
- [x] **Step 1: Armor 와 피해 공식** — `UnitStats.Armor`, `DamageAfterArmor`.
- [x] **Step 2: 업그레이드 표** — `UpgradeRow`, `UpgradeEffect`, 조회 두 개.
- [x] **Step 3: Research 명령과 시스템** — 명령, 엔티티 필드, `upgradeLevels`, hash, Kill.
- [x] **Step 4: 고유 효과** — 각 메커니즘이 읽는 상수를 `World` 메서드로.
- [x] **Step 5: 취소 환불**.
- [x] **Step 6: AI** — 기술 건물 건설과 index 오름차순 연구. 연구는 생산보다 먼저
      건다. 생산 뒤에 두면 매 think 마다 보유 자원이 생산에 다 쓰여서 연구비를 넘는
      순간이 없다.
- [x] **Step 7: 하네스** — `Replay/UpgradeChecks.cs`, Program.cs 한 줄 등록.
- [x] **Step 8: 골든 해시** — ScriptedLog 에 연구 명령 한 번(tick 505, 엔티티 24,
      index 0), CoreCLR 와 Mono 모두 `0xC9E3B6CFC16E4BA9`.
- [x] **Step 9: 문서** — `docs/FACTION-MECHANICS.md` 의 `## 업그레이드` 절.

## Validation

- **Commands to run:** `dotnet build`, `dotnet run --project Replay`,
  `dotnet run --project Host -- selfcheck`, Unity `ViewSelfCheck.Run`.
- **Expected output:** 0 경고, `OK: all determinism checks passed`, selfcheck 통과,
  `OK: all view determinism checks passed` 와 CoreCLR 와 같은 golden hash.

## Risks & Rollback

- **Risks:** AI 가 기술 건물과 연구에 자원을 써서 솔로 매치 승리가 늦어질 수 있다.
  클라이언트의 포획 진행 막대는 `World.CaptureTicks` 로 나누므로 포획 연구 뒤에는
  막대가 40틱 중 60틱 기준으로 그려진다(HUD 후속 이슈).
- **Rollback steps:** `git revert`. 되돌리면 ContentVersion 과 golden hash 가 이전
  값으로 돌아간다.

## Open Questions

- 없음.
