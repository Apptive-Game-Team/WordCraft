# 2026-10-16 — 기존 건물 정면 방향, tower strip 무장 필터, See it 갱신

- Date: 2026-10-16
- GitHub Issue: #201
- Base: `feat/195`(PR #198). 이 PR 은 #198 위에 쌓인다.
- Status: In progress

## Goal

1. 기존 건물 30종(여섯 진영의 `*Building` builder, 역할 다섯)에서 문, 창, 문장, 깃발, 균열 같은
   정면 요소가 +z 에 있다. 기본 카메라(yaw 0)는 -z 쪽에서 보므로 이를 -z 로 옮긴다.
   새 건물 24종은 이미 -z 다.
2. `ViewShots.cs` 의 `tower` strip 은 `UnitStats.HasWeapon` 이 거짓인 방어 건물을 건너뛴다.
3. `CLAUDE.md` 의 "See it" 절을 지금의 샷 구성으로 고치고, 고정된 장수 대신
   `ViewShots.Run` 로그의 OK 줄이 센다고 적는다.

## Non-goals

새 모델, 애니메이션 변경, `Sim/`, `BodyRig.cs`.

## 방법 (1)

- 정면에만 있는 부품은 z 좌표를 뒤집는다. 회전은 x 축 기울기와 y 축 회전의 부호를 바꾼다
  (z 축 회전은 그대로). 좌우 대칭 부품과 뒤쪽 부품은 그대로 둔다.
- 방향이 있는 구조(돌 골렘 Base 의 움푹한 자리와 바위들, 지옥불 Defense 의 아가리,
  물 슬라임 Tech 의 파도, 유랑종 Supply 의 갈고리)는 한 builder 안에서 z 를 통째로 뒤집는다.
- `CrownAims` 가 참인 crown(물 Defense, 돌 Defense, 인간 Defense 대포)은 조준하며 돌기 때문에 건드리지 않는다.
- `Top`, footprint, `Design` 숫자는 z 부호와 무관하다.

## 검증

- `ViewSelfCheck`.
- `ViewShots -shotsOnly lineup,build,tower` 를 전후로 찍어 lineup 의 실루엣이 같은지,
  build strip 에서 tier 가 제 자리에 올라오는지, tower strip 에 무장 없는 방어 건물이 빠졌는지 본다.
