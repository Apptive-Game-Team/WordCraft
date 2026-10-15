# 2026-10-16 — 지형·물가·소품·가장자리 품질 2차

- Date: 2026-10-16
- GitHub Issue: #187
- Status: In progress

## Goal

#159 의 1차 지형을 두 번째로 다듬는다. 절벽면에 층리와 jitter, 비탈에 길 표시,
물가에 흔들리는 포말과 움직이는 수면, 열린 땅에 큰 색 패치, 소품 8종 이상,
받침대 대신 Backdrop 으로 사라지는 맵 가장자리, 조명 재확인.

## Non-goals

- 맵 레이아웃(`MatchScenario`), 미니맵, 안개 규칙.
- `Field.cs` 의 높이 계약(`Corner`, `Cliff`, `At`, `Height`) 변경.
- 애셋 파일. 메시와 머티리얼은 전부 런타임에 만든다.

## Context / Constraints

- 이 브랜치는 `feat/182`(PR #184, 안에 #175·#176·#173·#171) 위에 쌓는다. 전부
  draft 다.
- 쓰기 범위: `FieldMesh.cs`, `FieldProps.cs`, `FieldPalette.cs`, `Sky.cs`,
  `Field.cs`(도우미만), `Ground.shader`, `Water.shader`, `Paper.cginc`,
  `docs/UI-STYLE.md` 지형 절, 이 문서. `MatchView.cs`·`BodyRig.cs`·`Models.cs`·
  `Effects.cs`·`ViewShots.cs` 는 #186 이 쓰므로 건드리지 않는다.
- 걸을 수 있는 땅의 표면은 `Field.Height` 그대로다. 메시가 바꾸는 것은 벽,
  호수 바닥, 맵 밖뿐이고, 셀 윗면의 꼭짓점 높이는 바꾸지 않는다.
- 색 리터럴은 `FieldPalette.cs` 에만 쓴다. 셰이더가 쓰는 색(포말 등)은
  머티리얼 속성이나 전역으로 넘긴다.
- 배치는 `FieldMesh.Hash` 로 정한다. `World.Random` 은 쓰지 않는다.
- chunk 당 draw call 은 그대로 두 개(Ground, Water). 소품과 가장자리는 chunk 의
  Ground 메시에 굽는다. 받침대의 `Base` 메시 한 개는 없어진다.

## Approach (Checklist)

- [ ] **절벽면.** 1레벨을 넘는 벽은 절대 높이 0.55 마다 가로 띠로 나누고, 띠마다
      명도를 번갈아 바꾼다. 윗가장자리에 0.09 높이의 밝은 테두리 띠, 안쪽 행의
      정점은 격자점과 행 번호의 해시로 최대 0.12 밀어낸다. 같은 격자점을 쓰는 두
      벽은 같은 값을 얻으므로 모서리에 틈이 없다. 절벽 아래 셀에 낙석 더미.
- [ ] **비탈.** 기울기의 주축을 따라 흙빛 줄 2~3개(열마다 해시로 개수 고정, 셀
      경계에서 이어진다)와 비탈 옆 가장자리의 낮은 돌.
- [ ] **물가.** 수면을 셀당 3x3 으로 나누고 정점마다 기슭까지의 거리를 vertex
      alpha 로 싣는다. `Water.shader` 가 그 거리에 시간과 위치의 sin 을 더해
      포말 띠를 흔든다. 두 방향의 느린 물결 띠. 기슭 벽은 수면 높이에서 물
      쪽으로 들어가는 흙 비탈. 호수 바닥은 기슭 셀과 안쪽 셀 두 단계 색, 안쪽은
      0.25 더 깊다. 기슭 물 셀에 갈대.
- [ ] **지면 변화.** 6셀 블록마다 해시로 패치(마른 풀, 이끼, 흙)를 두고 반경
      3~6셀, 가장자리를 각도 sin 으로 흔든다. 격자점 단위 색이라 셀 경계가 없다.
      기지 둘레 반경 10 은 비운다. 셀 단위 grain 도 격자점 단위로 바꾼다.
- [ ] **소품.** 바위 2종(돌, 큰 바위), 풀 2종(풀포기, 고사리), 나무 2종(침엽수,
      활엽수), 꽃, 버섯, 쓰러진 통나무, 낙석, 갈대, 비탈 돌. 능선 위에는 바위와
      작은 침엽수, 절벽 아래에는 낙석·고사리·버섯, 고지에는 침엽수와 마른 풀.
- [ ] **맵 가장자리.** 받침대와 판을 지우고, 맵 밖으로 0.4 비탈, 그 아래 층리
      절벽이 -3.6 까지 내려간다. `Paper.cginc` 의 `WcEdgeFade` 가 맵 밖의 깊이에
      따라 색을 `_WcHaze` 색(Backdrop)으로 끌어간다.
- [ ] **조명.** 샷을 보고 그림자 해상도와 하늘빛/땅빛을 다시 잰다.
- [ ] **문서.** `docs/UI-STYLE.md` 지형 절의 표와 소품 문단.

## Validation

- `dotnet build`
- Unity `ViewSelfCheck`: `OK: all view determinism checks passed`
  (`FieldAgreesWithSim` 포함)
- Unity `ViewShots`: field 다섯 장을 #165 기준 샷과 비교, 세 번 이상 반복.

## Risks & Rollback

- **Risks:** 벽 정점 수가 늘어 chunk 메시가 커진다(여전히 65535 아래). 수면
  분할로 water 정점이 9배. 가장자리 페이드가 호수 바닥을 건드리지 않도록 맵
  안쪽에서는 0 이어야 한다.
- **Rollback steps:** `git revert`. 뷰 전용이라 시뮬레이션과 해시는 영향이 없다.

## Open Questions

- 없음.
