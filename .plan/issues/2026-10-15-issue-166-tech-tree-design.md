# 2026-10-15 — 여섯 진영 테크트리·업그레이드·로스터 확장 설계

- Date: 2026-10-15
- GitHub Issue: #166
- Status: Implemented, awaiting review

## Goal

`docs/TECH-TREE.md` 한 파일에 여섯 진영의 확장 로스터(유닛 12종 이상, 건물 9종 이상,
업그레이드 12종 이상, 능력 6종 이상)와 테크트리, 구현 순서를 적는다. 시뮬레이션 트랙이
다음에 구현할 정본이므로 모든 행이 오늘의 `Role`·슬롯 모델로 주소를 가진다.

## Non-goals

- 코드, 아트, 다른 문서 수정. 불일치는 문서 끝 절에만 적는다.
- 밸런스 검증. 수치는 출발값이다.

## Context / Constraints

- 정본 순서: `.art/WORLD.md`(세계관) → `docs/FACTIONS.md`·`docs/FACTION-MECHANICS.md`
  (설계) → `Sim/FactionData.cs`(현재 로스터). 원문이 정하지 않은 설정은 `제안`.
- 기존 슬롯은 그대로, 새 항목은 `extras` 뒤에 붙인다. 기존 수치는 바꾸지 않고 인용한다.
- 업그레이드 틀은 #164(공격력·방어력 3단계, 고유 연구 2종, `Research` = 14)를 그대로 쓴다.
- 결정론: 정수와 틱, id 오름차순, 새 상태는 `World.Hash()`.

## Approach (Checklist)
- [x] **Step 0: Recon** — 세계관·로스터·메커니즘 문서, `Sim/` 로스터와 시스템 순서, 스프라이트 목록, #164·#166.
- [x] **Step 1: 문서 작성** — 공통 규칙, 데이터 모델 확장, 진영별 표 여섯 벌, 테크트리 그림, 구현 순서, 불일치.
- [x] **Step 2: 대조** — 표의 `Role`·슬롯을 `FactionData.names`·`extras`와 하나씩 맞춰 충돌이 없는지 확인.
- [x] **Step 3: PR** — draft PR, `Closes #166`.

## Validation
- **Commands to run:** 없음(문서). `git diff --stat main` 이 `docs/TECH-TREE.md` 와 이 계획 파일만 보여야 한다.
- **Expected output:** 두 파일 추가, 그 밖의 변경 없음.

## Risks & Rollback
- **Risks:** 문서와 코드가 이미 어긋난 곳(문서 수치 미반영, 공중·지상 전용 플래그, 단계)이 많아 어느 값을 인용했는지 헷갈릴 수 있다. 표에 표식(`†`, `(코드 …)`)으로 구분하고 끝 절에 모은다.
- **Rollback steps:** `git revert` 한 번. 다른 파일을 건드리지 않는다.

## Open Questions
- 유닛당 인구 비용(지금은 몸 하나에 1), 은신·탐지, 수송, 일꾼이 건물로 자라는 세계수 정령 건설은 문서의 열린 결정 절로 넘긴다.
