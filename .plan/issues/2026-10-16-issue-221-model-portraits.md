# 2026-10-16 — 도감과 명령 카드에 실제 전장 모델 표시

- Date: 2026-10-16
- GitHub Issue: #221 (PR #227)
- Status: Complete

## Goal
기존 PNG와 전장 외형의 불일치를 없애고 도감·생산·건설·성장 카드가 실제 Models 형상을 표시한다.

## Non-goals
새 모델 디자인, 시뮬레이션 변경, 도감 회전 조작 추가.

## Context / Constraints
사용자가 실제 제작 모델로 표시하도록 명시했다. 기존 모델과 재질을 그대로 사용하고 모델 미리보기는 월드·카메라·조명을 변경하지 않는다.

## Approach (Checklist)
- [x] **Step 0: Recon** CardPortrait의 PNG 로드와 Models 공유 메시 구조 확인.
- [x] **Step 1: Implementation** 격리된 모델 렌더 및 faction/role/slot 캐시, 도감 설명 정정.
- [x] **Step 2: Tests** 전체 128개 항목, 캐시·리소스·월드 불변성 검사 및 실제 UI 렌더.
- [x] **Step 3: Rollout / Rollback** Windows 빌드 및 기존 PR 갱신. 변경 커밋으로 복구.

## Validation
- **Commands to run:** ViewSelfCheck, Windows BuildPlayer, CodexUiPreview 및 카드 UI 캡처.
- **Expected output:** PNG 대신 전장과 동일한 3D 모델 형태가 보이고, 화면 경계 안에 들어오며, 본 경기 상태는 유지된다.

## Risks & Rollback
- **Risks:** 최초 모델 생성 비용, 렌더 자원 누수, 전장 조명/카메라 간섭.
- **Rollback steps:** 모델 초상 커밋을 복구한다.

## Open Questions
- None.

## Results
- GPU ViewSelfCheck 통과: 128개 실제 픽셀·캐시·자원 정리·렌더 상태 복구 검사, golden D7F7252F1ECBEC6D 유지.
- Windows codex-models 빌드 성공. 실제 도감 1024/1920과 카드 UI 캡처에서 전장 모델 표시 확인.
- 전체 경기 수동 재검증은 하지 않았으며 이번 변경은 보기 전용이다.
