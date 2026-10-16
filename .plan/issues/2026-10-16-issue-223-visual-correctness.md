# 2026-10-16 — 전투 비주얼 정확성과 시각 검증

- Date: 2026-10-16
- GitHub Issue: #223
- Status: In progress

## Goal

최신 통합본의 실제 전투 결과와 화면을 일치시킨다. 전체 완성 목표는 기존
`../general/2026-10-15-starcraft-parity-roadmap.md`와 `docs/TECH-TREE.md`를 이어간다.
기존 여섯 진영의 콘셉트와 절차적 3D 렌더링을 유지한다.

## Non-goals

이 수정 단위에서 시뮬레이션 규칙이나 정본 아트 스타일을 변경하지 않는다.
정확한 착탄 위치와 착탄 틱 사망을 관측하기 위해 Sim에는 읽기 전용 per-step
BurstEvent 출력 버퍼만 추가한다. 시스템은 이 버퍼를 읽지 않으며 해시·명령·피해는 불변이다.
명령 수락 여부와 사망 순서를 뷰가 재구현하지 않기 위한 범위 확장이다.
AI #211과 밸런스 #221은 후속 의존 작업이며 완료로 간주하지 않는다.

## Context / Constraints

main bd18f11 갱신 완료. .NET 빌드, Replay, Host selfcheck 통과.
Hindsight 서버는 DNS 오류로 사용 불가. Unity 최초 import 중 일시적 컴파일 오류가
기록되어 깨끗한 재실행으로 실제 성공을 확인해야 한다.

## Approach (Checklist)
- [x] **Step 0: Recon**: 코드, 기존 로드맵, #223 수용 조건 확인.
- [x] **Step 1: Implementation**: 미니맵 시야, 능력 위치와 착탄, 수명 바,
  효과 풀, 물 지대 캐시, 전개 동작을 수정한다.
- [x] **Step 2: Tests**: 회귀 검사와 Unity 뷰 검사, cast/field 샷 반복 확인.
- [ ] **Step 3: Rollout / Rollback**: 검증 근거와 잔여 작업을 PR로 기록.

## Validation
- **Commands to run:** `dotnet build`, `dotnet run --project Replay`,
  `dotnet run --project Host -- selfcheck`, Unity `ViewSelfCheck`, `ViewShots`.
- **Expected output:** 오류 없는 빌드와 동일 결정론 해시. 안개 밖 동적 지형 숨김,
  실제 착탄 시점에만 폭발, 유닛 중심에 정렬한 수명 표시.

## Risks & Rollback
- **Risks:** 프레임 사이 여러 시뮬레이션 틱과 거부된 능력 명령의 처리.
- **Rollback steps:** 기능별 커밋 revert. 시뮬레이션과 프로토콜은 불변.

## Open Questions
- 없음. 전체 출시 완료 판단에는 AI, 밸런스, 실제 플레이 검증이 추가로 필요하다.

## Visual Verification — 2026-10-16

- `dotnet build --no-restore`: 0 warnings, 0 errors.
- Unity `ViewSelfCheck`: exit 0 (`Client/Logs/visual-selfcheck-final.log`), golden hash `0x89303B33ED3EA28D` unchanged.
- Added minimap Hidden/Memory/Seen checks, ring-only pool allocation/reuse checks, cancelled approach and stale-handle reuse checks.
- `ViewShots -shotsOnly cast,field` ran three times, each exit 0 and 12 images in `Client/Logs/visual-pass-1`, `visual-pass-2`, `visual-pass-3`.
- Pass 1: confirmed visible geyser and impact timing; sand still read as confetti.
- Pass 2: aligned sand chips tangentially, flattened their shape, limited height to 0.18–0.36 cells; reviewed concentric motion, meteor impact, cannon crown and fire-coloured death burst.
- Pass 3: checked final field fade, tree-spirit zone and base rendering. Final shot evidence lives in `visual-pass-3`.
- Replay cast checks now cover exact Tide points over longer-lived fields, two simultaneous tides, cancelled meteor and caster death on its landing tick. Whole Replay validation is coordinated by the AI workstream.
- Remaining: online-menu interaction and live multiplayer playtest belong to the parent integration work; batch shots intentionally exclude IMGUI.
