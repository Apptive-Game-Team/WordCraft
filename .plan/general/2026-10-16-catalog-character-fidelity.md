# 2026-10-16 — 원작 도감 기반 전장 캐릭터 외형 복원

- Date: 2026-10-16
- GitHub Issue: Pending publication approval
- Status: Local implementation and validation complete

## Goal
https://theevilent.com/arcane-casters/magic 에서 확인한 캐릭터 정체성·실루엣·얼굴 유무를 실제 전장 3D 모델에 반영한다.

## Non-goals
렌더링 스타일 재선정, 시뮬레이션 밸런스 변경, 원작에 없는 설정 확정.

## Context / Constraints
도감은 브라우저로 직접 열어 확인했다. 로컬 PNG는 HUD 아이콘이고 전장은 Models.cs의 절차적 mesh라 아이콘만 바꿔서는 문제를 해결하지 못한다. 도감의 Leafair는 얼굴 없는 긴 잎다발, VineSpirit은 넓은 잎 얼굴과 감긴 덩굴이다. 기존 모델에 임의 눈·무기 및 구슬 사슬이 들어갔다. 현 master-v2 렌더링과 애니메이션은 유지하고 확인된 형태의 불일치만 복구한다. 도감에 없는 RTS 확장 항목은 원작이라고 주장하지 않는다.

## Approach (Checklist)
- [x] **Step 0: Recon** 사이트 도감/로컬 매핑/스타일 문서 및 기존 6종족 모델 라인업.
- [x] **Step 1: Implementation** 세계수 형상 복원, 물/바위/지옥불 대표 카탈로그 모델 복원, docs/CATALOG-VISUAL-FIDELITY.md에 출처·차이 기록.
- [x] **Step 2: Tests** 수정 전후 각 72장 생성, 수정 4종족 lineup 및 대표 idle/walk/attack strip 육안 비교, ViewSelfCheck PASS(D7F7252F1ECBEC6D), 별도 Windows 빌드 성공.
- [x] **Step 3: Rollout / Rollback** Client/Builds/catalog/WordCraft.exe 독립 빌드로 제공, 원본 PNG/anchor 보존, 모델 변경 단위 revert 가능. 이번 실행 파일의 직접 입력 플레이 검증은 수행하지 않음.

## Validation
- **Commands to run:** Unity ViewShots, ViewSelfCheck, BuildPlayer.
- **Expected output:** 도감의 핵심 실루엣이 작게도 구별되고 정지·이동·공격에서 부품이 분리되거나 바닥으로 뚫리지 않음. 시뮬레이션 golden은 변경 없음.

## Risks & Rollback
- **Risks:** 3D번역 시 도감 특징 과장, 부품 증가, 기존 애니메이션 pivot 손상.
- **Rollback steps:** 모델/시각 기록 변경을 되돌리며 게임 규칙과 기존 기능은 유지한다.

## Open Questions
- None. 사용자 지정 도감이 형태 기준이며 기존 master 렌더링 자체는 변경하지 않는다.
