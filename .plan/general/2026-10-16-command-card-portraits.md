# 2026-10-16 — 소환·건설 그림 버튼과 설명

- Date: 2026-10-16
- GitHub Issue: None (local work, publication pending)
- Status: Local implementation and validation complete

## Goal
하단 소환·건설·성장 버튼에 해당 개체의 그림을 표시하고 hover로 이름·역할·비용·조건을 설명한다.

## Non-goals
명령 규칙이나 기존 단축키 변경, 원본 아트 덮어쓰기.

## Context / Constraints
54px 버튼과 4×3 배치를 유지한다. 잠긴 버튼도 그림과 설명이 보여야 한다. 등록된 도감 에셋을 재사용하고 누락은 글자 fallback 처리한다.

## Approach (Checklist)
- [x] **Step 0: Recon** CommandCard/Hud/UiStyle 및 FactionData.Sprite 매핑 조사.
- [x] **Step 1: Implementation** CardPortrait 캐시, UiStyle 아이콘 버튼/동적 tooltip, CardDetails 정보 구성 및 Hud 연결.
- [x] **Step 2: Tests** 6종족 매핑·상태 검증, Unity 컴파일, 1024×576/1920×1080 UI 캡처 6장 및 대표 이미지 육안 확인.
- [x] **Step 3: Rollout / Rollback** Client/Builds/card-portraits/WordCraft.exe 별도 Windows 빌드 성공. UI 파일 단위로 복구 가능.

## Validation
- **Commands to run:** Unity ViewSelfCheck, UI capture, BuildPlayer.
- **Expected output:** 그림/단축키/비용이 겹치지 않고 tooltip 전체가 화면 안에 표시됨. 명령·게임 해시 유지.
- **Results:** ViewSelfCheck PASS, 최종 해시 D7F7252F1ECBEC6D 유지. 6종족 등록 에셋 로딩/설명 및 world 비변경 검사 통과. 1024×576 실제 IMGUI 캡처 3장 육안 확인(활성·잠금·선택, 건설 2페이지, 생산, 일꾼·구름용·공방 설명). 숨김 캡처는 검정이라 증거에서 제외.
- **Artifacts:** Client/Logs/card-selfcheck.log, Client/Logs/card-build.log, Client/Logs/cards-visible-1024-{0,1,2}.png. 별도 QA 실행은 자동 캡처 후 종료하며 기존 사용자 게임을 조작하지 않음.

## Risks & Rollback
- **Risks:** 작은 그림 가독성, 긴 한국어 설명 잘림, 누락 에셋.
- **Rollback steps:** 카드 렌더 변경과 새 helper만 복구.

## Open Questions
- None.
