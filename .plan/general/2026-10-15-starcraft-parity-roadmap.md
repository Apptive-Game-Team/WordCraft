# 2026-10-15 — StarCraft 수준 로드맵

- Date: 2026-10-15
- GitHub Issue: None (트랙별 이슈는 아래 표)
- Owning repository: WordCraft
- Status: Active
- Supersedes: `.plan/general/2026-08-11-parallel-milestones.md`의 M4 이후 순서. 트랙 정의(A 시뮬레이션 직렬, B 뷰, C 하네스, D 콘텐츠)는 그대로 쓴다.

## Goal

WordCraft를 StarCraft와 나란히 놓아도 밀리지 않는 RTS로 만든다. 기준은 셋이다.

1. **콘텐츠 깊이.** 여섯 진영 각각이 StarCraft 한 종족만큼의 유닛(12종 이상), 건물(9종 이상), 업그레이드·기술(12종 이상), 능력(6종 이상), 선행 관계가 있는 3단계 테크트리를 가진다. 단, 기존 컨셉(세계관, 진영당 고유 메커니즘 하나, 형태 언어와 팔레트)을 깨지 않고 그 위에 쌓는다.
2. **시각 품질.** 모든 유닛·건물이 걷기·공격·피격·죽음·유휴·스폰 애니메이션을 가지고, 스크린샷으로 직접 보고 판단했을 때 상용 게임으로 읽힌다.
3. **조작감과 흐름.** 업그레이드·능력이 HUD에서 쓸 수 있고, AI가 확장 로스터를 실제로 쓰며, 사람이 한 판을 끝까지 돌릴 수 있다.

## 현재 상태 (2026-10-15)

- 검증됨: 결정론 시뮬레이션, P2P 락스텝, 여섯 진영 고유 메커니즘 전부, 지형 고도·8방향 길찾기·분리, 3D 필드와 절차적 저폴리 모델, HUD·미니맵·안개·사운드. `dotnet build` 0경고, `Replay`·`Host selfcheck`·Unity `ViewSelfCheck` 전부 통과.
- 없음: 업그레이드(0개), 능력(0개), 방어력 스탯, 슬롯 단위 기술 단계와 선행 건물, 진영당 전투 유닛 4~5종뿐, 죽음·투사체·스폰·유휴 애니메이션, 애니메이션을 검증할 샷.
- 도구: .NET 8 SDK는 `~/.dotnet`에 있고, Unity 2022.3.34f1은 Windows 쪽(`C:\Program Files\Unity\Hub\Editor\2022.3.34f1`)에 있다. `.worktrees/unity-tools/sync.sh`와 `run.sh`가 WSL checkout의 `Client/`를 `C:\Users\jys09\Projects\WordCraft-<name>`으로 mirror하고 `ViewSelfCheck`·`ViewShots`를 돌린다.

## 트랙과 쓰기 범위

| 트랙 | 쓰기 범위 | 동시성 |
|---|---|---|
| A 시뮬레이션 | `Sim/`, `Replay/`, `Client/Assets/Editor/ScriptedLog.cs`, `docs/FACTION-MECHANICS.md` | 직렬. 한 번에 하나 |
| B 뷰·애니메이션 | `Client/Assets/Scripts/{BodyRig,Models,Solids,MatchView,Decals,BodyPalette}.cs`, 새 효과 파일, `Client/Assets/Editor/ViewShots.cs`, `Resources/Shaders/` | 병렬 가능 |
| B2 HUD | `Client/Assets/Scripts/{Hud,CommandCard,Orders,ProductionMenu,UiStyle}.cs`, `docs/UI-STYLE.md` | B와 병렬 가능. A의 API가 먼저 머지돼야 한다 |
| C 하네스 | `Replay/`의 새 파일, `Host/` | A와 같은 PR에 실리는 것이 보통 |
| D 콘텐츠 | `docs/TECH-TREE.md`, `docs/FACTIONS.md`, `.art/`, `.plan/` | 병렬 가능 |

## 단계

### P1 — 그릇 (진행 중)

| 트랙 | 이슈 | 내용 |
|---|---|---|
| A | #164 | 업그레이드 연구: `Armor` 스탯, 진영별 업그레이드 표, `Research` 명령, `ResearchSystem`, 진영 고유 연구 12종, AI 연구, `ContentVersion` 30 |
| B | #165 | 죽음·투사체·impact·스폰·유휴·걷기·공격 애니메이션과 `ViewShots`의 lineup·strip 샷 |
| D | #166 | `docs/TECH-TREE.md`: 진영당 유닛 12+, 건물 9+, 업그레이드 12+, 능력 6+, 테크트리, 구현 순서 |

동기화 조건: 셋 다 draft PR이 열리고 CI가 초록. 사람이 리뷰해 머지한다.

### P2 — 테크트리 구조 (A 직렬, B2 병렬)

- **A-2 슬롯 단위 기술 단계와 선행 건물.** `FactionData.Tier(role)`를 `Tier(faction, role, slot)`으로, 슬롯마다 선행 건물(`Role`+슬롯) 하나. `Economy.TierOf`가 건물 목록을 읽어 답한다. 해시 필드 없음(파생). `ContentVersion` 31.
- **A-3 연구 건물 분리.** 업그레이드 행마다 연구 건물 `Role`. 생산 건물·기술 건물·상위 건물.
- **B2-1 연구 카드.** 기술 건물을 선택하면 명령 카드에 연구 버튼, 진행 막대, 단계 표시. #164가 머지된 뒤.
- **D-2 `docs/FACTIONS.md`를 `TECH-TREE.md`에 맞춰 갱신.**

### P3 — 로스터 확장 (A 직렬, B 병렬)

`docs/TECH-TREE.md`의 구현 순서를 따른다. 묶음 하나가 PR 하나.

- **A-4 로스터 행 1차.** 진영마다 유닛 3~4종, 건물 2~3종 추가(스탯·가격·단계·선행만, 능력 없음). 아트 있는 것 우선.
- **B-3 바디 1차.** A-4의 항목마다 `Models.cs` 절차 모델. lineup 샷으로 검증.
- **A-5 능력 1차: 패시브.** 광역 피해, 치유, 감속, 화상 오라. 각각 해시 필드 명시.
- **A-6 능력 2차: 한시 소환·돌진·탐지.**
- **A-7 로스터 행 2차.** 나머지 유닛·건물.
- **B-4 바디 2차, 능력 효과 비주얼.**
- **B2-2 능력 버튼과 오토캐스트 표시.**
- **A-8 AI가 확장 로스터와 능력을 쓴다.** 사다리를 계획으로.

### P4 — 품질 (B 중심)

- **B-5 건물 애니메이션**: 건설 중 조립, 생산 중 맥동, 파괴.
- **B-6 지형·소품·조명 품질**: 절벽면 질감, 물가, 안개 가장자리, 그림자 품질.
- **B-7 전투 가독성 2차**: 피해 숫자 없이 읽히는 타격, 광역 표시, 치유 표시.
- **사람 검증**: 솔로 한 판, 두 클라이언트 한 판. `docs/UI-STYLE.md`의 "사람이 봐야 하는 것" 목록.

### P5 — 밸런스 (M4)

`.plan/general/2026-08-11-parallel-milestones.md`의 M4. 동족전이 아닌 대전을 AI 대 AI로 반복해 승률을 재고 `statOverrides`를 조정한다. 하네스에 자동 대전 통계 명령(`Host -- arena`)을 넣는 것이 첫 줄이다.

## Non-goals

- 인터넷 대전, NAT, 매치메이킹, 캠페인, 3인 이상.
- 애셋 파일(FBX·프리팹·애니메이션 클립). 전부 런타임 코드로 만든다(#159의 규칙).
- Sim 안의 병렬화. A 트랙은 끝까지 직렬이다.

## Validation

- 모든 PR: `dotnet build` 0경고, `dotnet run --project Replay`, `dotnet run --project Host -- selfcheck`, Unity `ViewSelfCheck`. CI가 둘 다 초록.
- A 트랙 PR: 새 상태 필드가 `World.Hash()`에 있고, 골든 해시는 CoreCLR과 Mono가 같은 값을 낸 뒤에만 고친다. 하네스 검사 파일이 새로 붙는다.
- B 트랙 PR: `ViewShots`의 lineup·strip PNG를 PR에 첨부하고 사람이 본다.
- D 트랙 PR: `Role`·슬롯이 `Sim/FactionData.cs`와 충돌하지 않는다.

## Risks & Rollback

- **A가 병목이다.** 직렬이라 로스터 확장 속도가 A의 속도다. 묶음을 작게 자르고 PR마다 해시 필드를 적는 것으로 대응한다.
- **골든 해시가 PR마다 움직인다.** A 트랙 PR이 두 개 이상 열려 있으면 뒤의 것은 앞의 것 위에 rebase해야 한다. 한 번에 하나만 연다.
- **뷰 품질 판단이 주관이다.** strip 샷을 기준으로 삼고, 64px 축소에서 동작이 읽히는지를 통과 조건으로 둔다.
- 롤백은 PR 단위 revert.

## Open Questions

- 업그레이드 연구 건물을 기술 건물 하나로 둘지(#164의 출발값), 상위 건물로 나눌지(P2 A-3). `TECH-TREE.md`가 답한다.
- 능력 중 액티브(목표 지정)를 언제 넣을지. 패시브·오토캐스트로 먼저 깊이를 만들고, 액티브는 HUD의 조준 UI(`ArrivalOrder`가 선례)가 준비된 뒤.
