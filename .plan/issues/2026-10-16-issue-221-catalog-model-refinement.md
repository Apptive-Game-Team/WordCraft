# 2026-10-16 — 도감 고유 형태와 모델 완성도 보강

- Date: 2026-10-16
- GitHub Issue: #221 (PR #227)
- Status: Complete

## Goal
공식 도감의 몸 비율과 고유 부위가 전장 및 도감 모델에서 구별되도록 조형을 다듬는다.

## Non-goals
아트 스타일 교체, 밸런스 변경, PNG로 모델 대체.

## Context / Constraints
사용자가 실제 모델이 지나치게 단순하고 비슷하다고 지적했다. theevilent.com/arcane-casters/magic을 브라우저에서 재확인했다. 고정된 master-v2 재질 및 애니메이션 리그를 유지한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 원작 도감과 최신 실제 모델 이미지 비교.
- [x] **Step 1: Implementation** 자연·번개·바람, 물·돌, 지옥불 조형 병렬 보강. 인간 대포·포탑돌이 구조 보강 및 건물 초상 방향 수정.
- [x] **Step 2: Tests** GPU ViewSelfCheck, 전종족 라인업·동작 렌더, 실제 모델 도감 및 카드 UI 확인.
- [x] **Step 3: Rollout / Rollback** 별도 Windows 빌드 및 기존 PR 업데이트, 조형 변경 커밋으로 복구.

## Validation
- **Commands to run:** ViewSelfCheck.Run, ViewShots.Run, Windows BuildPlayer, CodexUiPreview/CardUiPreview.
- **Expected output:** 몸 비율과 고유 형태가 보이고 움직임 중 부품 분리가 없으며 시뮬레이션 해시가 동일하다.

## Risks & Rollback
- **Risks:** 작은 장식의 잡음, 몸 부위 겹침, 초상에서 앞면 가림.
- **Rollback steps:** 해당 외형 수정 커밋을 복구한다.

## Open Questions
- None.

## Results
- 자연·번개·바람 12종, 물·돌 7종, 지옥불 2종, 대포·포탑돌이 2종의 실제 모델 보강.
- GPU ViewSelfCheck 통과, 600틱 해시 D7F7252F1ECBEC6D 유지. 84장 lineup/detail/idle/walk/attack 렌더 성공.
- 수정 5종족 상세판, 대표 이동·공격 스트립 및 실제 1024 도감 7장 확인. 별도 Windows catalog-refined 빌드 성공.
- 렌더 fixture 검증이며 전체 경기 수동 재검증은 하지 않음.
