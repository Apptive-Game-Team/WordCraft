# WordCraft

WordCraft는 결정론적 락스텝(lockstep) 시뮬레이션을 P2P UDP로 동기화하는 데스크톱
RTS입니다. 게임 로직을 처리하는 서버가 없습니다. 각 피어가 동일한 시뮬레이션을
독립적으로 실행하고, 주고받는 것은 플레이어 입력과 상태 해시뿐입니다.

아트와 진영 컨셉은 [WordOnline](https://github.com/Apptive-Game-Team/WordOnline)
프로젝트에서 가져옵니다.

## 저장소 구조

| 경로 | 역할 | 기술 |
| --- | --- | --- |
| [`Sim/`](Sim/) | 순수 C# 시뮬레이션. 고정소수점 연산, 결정론 RNG, 엔티티 상태, 상태 해시 | .NET Standard 2.1 |
| [`Replay/`](Replay/) | 헤드리스 결정론 자체 검증과 리플레이 하네스 | .NET 7 |

Unity 프로젝트는 마일스톤 4에서 추가합니다. 의도적으로 늦게 만듭니다. 초기부터
Unity 프로젝트가 있으면 `UnityEngine` 타입과 부동소수점이 시뮬레이션으로 새어들기
쉽고, 그렇게 생긴 desync는 원인에서 멀리 떨어진 곳에서 드러납니다.

## 시작하기

.NET 7 SDK만 있으면 됩니다.

```bash
git clone https://github.com/Apptive-Game-Team/WordCraft.git
cd WordCraft
dotnet run --project Replay
```

`OK: all determinism checks passed`가 출력되면 정상입니다.

## 결정론 자체 검증

`Replay`는 테스트 프레임워크 없이 assert 기반으로 다음을 검증합니다.

- 고정소수점 사칙연산, 정수 뉴턴 sqrt, 3-4-5 삼각형 벡터 크기가 정확히 5인지
- RNG 스트림과 드로우 횟수가 같은 시드에서 완전히 일치하는지
- 같은 입력 로그를 두 번 실행했을 때 600틱 전 구간 해시가 동일한지
- 명령 도착 순서를 뒤집어도 정규 순서 정렬 후 결과가 같은지
- 한쪽에 명령 하나를 몰래 끼워 넣었을 때 해당 틱에서 발산이 감지되는지

`Sim/` 아래를 수정하면 반드시 다시 실행합니다.

## `Sim/` 규칙

시뮬레이션은 모든 피어에서 바이트 단위로 동일한 상태를 만들어야 합니다. 아래 규칙을
어기면 조용히 desync가 나고, 실패는 원인에서 한참 떨어진 곳에서 드러납니다.

- `float`, `double` 금지. `Fix`(Q16.16)와 `FixVec2`를 씁니다.
- `UnityEngine`, `System.DateTime`, `System.Random`, 실시간 시계 금지.
- 시뮬레이션 순서에 `Dictionary`/`HashSet` 순회 금지. 엔티티 ID 순으로 순회합니다.
- 모든 난수는 `World.Random`을 거칩니다. 드로우 횟수 자체가 상태입니다.
- 엔티티 ID는 재사용하지 않습니다.
- 상태 필드를 추가하면 `World.Hash()`에도 추가합니다. 빠뜨리면 desync가 감지되지
  않습니다.
- 명령은 도착 순서와 무관하게 `PeerId`, `Seq` 정규 순서로 실행합니다.

## 마일스톤

1. 결정론 코어와 리플레이 하네스 (완료)
2. 자원 채집, 건설, 유닛 생산, 패스파인딩
3. P2P 락스텝 네트워크 (LAN 직접 IP)
4. Unity 뷰와 HUD
5. 진영 콘텐츠와 승리 조건

인터넷 대전, NAT 통과, 매치메이킹은 별도 계획입니다.

## 개발 지침

에이전트와 기여자 지침은 [AGENTS.md](AGENTS.md)와 [`.agents/docs/`](.agents/docs/)를
참고합니다. 프로젝트 맥락은 [.agents/docs/project.md](.agents/docs/project.md)에
있습니다.
