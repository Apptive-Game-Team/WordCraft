# 2026-10-16 — 테크트리 신규 행 65개의 바디 모델

- Date: 2026-10-16
- GitHub Issue: #195
- Base: `feat/195` = `feat/180`(PR #194) + `feat/186`(PR #188) + `feat/187`(PR #189) 통합. 이 PR 은 세 PR 위에 쌓인다.
- Status: In progress

## Goal

묶음 3 이 늘린 로스터 행 65개(유닛 41, 건물 24)에 `Models.cs` 의 전용 builder 를 붙인다.
지금 유닛 행은 같은 역할의 슬롯 0 몸이나 `GenericUnit` 으로, 건물 행은 같은 역할의
슬롯 0 몸으로 떨어진다. 여섯 lineup 이 진영마다 유닛 12종, 건물 9종 이상으로 읽히고
같은 몸이 둘 없어야 한다.

## Non-goals

- 기존 행의 몸 변경, 능력 효과 비주얼, HUD.
- `Sim/`, `BodyRig.cs`, `Effects.cs`, `MatchView.cs`.

## 행 목록 (진영, 역할, 슬롯)

| 진영 | 유닛 | 건물 |
|---|---|---|
| 세계수 정령 | Melee 2 찌릿쥐, Melee 3 폭풍 기수, Ranged 2 번개새 무리(공중), Ranged 3 거대 덩쿨, Signature 1 도깨비불(공중), Signature 2 덩굴 군집 | Production 1 씨앗 요람, Defense 1 회복 토템, Tech 1 바람 토템, Tech 2 광란의 토템 |
| 지옥불 군단 | Melee 1 화염 악마, Melee 2 균열 사냥개, Melee 3 잿불 새끼, Ranged 2 용암 투석 악마, Ranged 3 재날개 악마(공중), Signature 1 산란 악마, Signature 2 운석 술사 | Production 1 큰 균열, Defense 1 집결의 횃불, Defense 2 화염 첨탑, Tech 1 재 분화구 |
| 물 슬라임 | Melee 1 물막이 슬라임, Melee 2 급류 슬라임, Ranged 2 침 뱉는 슬라임, Ranged 3 물대포 슬라임, Signature 1 비구름(공중), Signature 2 간헐천 술사, Signature 3 샘물 운반자 | Production 1 구름 둥지, Defense 1 솟는 샘, Defense 2 거품 장벽, Tech 1 물결 수련장 |
| 돌 골렘 부족 | Melee 1 구르는 바위, Melee 2 성벽 골렘, Melee 3 깨어난 바위, Ranged 1 돌팔매 꼬마돌, Ranged 2 부유석 파수(공중), Ranged 3 모래 술사, Signature 1 이끼 현자, Signature 2 이끼 포자 무리(공중) | Production 1 각성의 원, Defense 1 부유석 첨탑, Defense 2 잠듦의 고리, Tech 1 이끼 정원 |
| 차원 유랑종 | Ranged 2 덩쿨편, Ranged 3 멸종한 바람 슬라임(공중), Signature 1 떠도는 섬(공중), Signature 2 층위 관측자, Signature 3 멸종한 자연 슬라임 | Production 1 깊은 통로, Defense 1 위상 닻, Defense 2 틈새 포대, Tech 1 외피 연마장 |
| 인간 마법 문명 | Ranged 1 석궁 사수, Ranged 2 마도사, Ranged 3 석조 보행 포대, Ranged 4 견인 대포, Ranged 5 마도 폭격선(공중), Ranged 6 마도 기수, Signature 2 공방 기술자, Signature 3 마나 증폭기 | Production 1 석조 병영, Production 2 마도 공창, Tech 1 마도 학당, Tech 2 결속대 |

## 설계

- 유닛 dispatch 는 지금처럼 `(역할, 슬롯)` switch. 건물 dispatch 에 슬롯을 넘겨
  `*Building(b, role, slot)` 으로 바꾸고, 슬롯 0 은 지금 몸 그대로 둔다.
- 걸음: 공중은 `Fly` + `Flap` 날개, 슬라임은 `Hop`, 정령은 `Float`, 장치와 골렘은
  `Walk` + `Swing`/`Step`, 비전투 시전자는 `Strike.Pulse`.
- 건물: 연구 건물은 `CrownSpin`, 무장 방어는 `CrownAims` + `CrownKick`, 성벽과 닻은
  crown 없음. 부품은 `b.Tier` 로 나눠 각 진영의 `Assembly` 로 올라온다.
- 소유자 accent(`Band`/`Strap`/`Shard`/`Pennant`)를 모든 몸에 넣는다. 색은 `BodyPalette`
  에서만 가져온다.
- `ViewShots`: walk·attack·hit·death·idle·spawn strip 이 걷는 행 전부를 돌도록 4기씩
  여러 장(`strip-<faction>-<motion>-2.png` 등)으로 나눈다. lineup 은 진영 한 장에 더해
  유닛 줄과 건물 줄을 따로 찍는다(`lineup-<faction>-units`, `lineup-<faction>-buildings`).

## Validation

- `dotnet build`
- Unity `ViewShots -shotsOnly lineup` 세 번 이상, walk/attack/idle/death strip
- Unity `ViewSelfCheck`
