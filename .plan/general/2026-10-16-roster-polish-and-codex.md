# 2026-10-16 — 유닛 구별성 개선과 게임 내 도감

- Date: 2026-10-16
- GitHub Issue: #221 (PR #227 follow-up)
- Status: Complete

## Goal
비슷하게 보이는 유닛의 원작 특징과 역할별 실루엣을 강조하고, 게임 내에서 종족별 유닛·건물을 찾아볼 수 있게 한다.

## Non-goals
새 아트 스타일 확정, 시뮬레이션 밸런스 변경, 원작에 없는 설정 추가.

## Context / Constraints
theevilent.com 도감의 정체성 및 master-v2 재질을 유지한다. 도감은 읽기 전용이며 온라인 경기를 멈추거나 명령을 내보내지 않는다. 기존 UI와 동일한 데이터/이미지를 사용한다.

## Approach (Checklist)
- [x] **Step 0: Recon** 이전 모델 렌더, CardDetails 및 HUD 조사.
- [x] **Step 1: Implementation** 세계수/물/돌/불 조형 보강, 종족·검색·역할 필터 도감, 카메라 경계 보정.
- [x] **Step 2: Tests** 모델·애니메이션 렌더, 도감 UI와 입력 차단·데이터 검사, Unity selfcheck 및 Windows 빌드.
- [x] **Step 3: Rollout / Rollback** 별도 빌드와 비교판 제공. 모델/UI를 독립적으로 복구 가능.

## Validation
- **Commands to run:** ViewSelfCheck, ViewShots, 도감 캡처 fixture, BuildPlayer.
- **Expected output:** 개별 유닛 형태가 구별되고 모든 도감 항목이 조회됨. 도감 뒤로 명령 입력이 통과하지 않음. 게임 해시 불변.

## Risks & Rollback
- **Risks:** 원형에서 벗어난 과도한 장식, 작은 화면 글자 잘림, 메뉴 입력 전파.
- **Rollback steps:** 모델 및 도감 커밋 단위로 복구.

## Open Questions
- None. 도감은 게임 내 메뉴로 제공한다.

## Results
- Unity ViewSelfCheck 통과, 600틱 해시 D7F7252F1ECBEC6D 유지. 도감 128개 항목과 검색·필터·조건·월드 불변성 검사 통과.
- ViewShots 라인업·대기·이동·공격 렌더 성공. 수정 4종족 상세판과 대표 동작 스트립 육안 확인.
- Windows 빌드 Client/Builds/codex/WordCraft.exe 성공. 실제 별도 게임 창에서 1024×576/1920×1080 각 3장 도감 캡처 확인.
- 캡처: Client/Logs/art-polish-after 및 Client/Logs/codex-visible-*.png (생성 결과는 Git 제외). 이번 검증은 렌더 fixture이며 전체 경기 수동 재검증은 아님.
