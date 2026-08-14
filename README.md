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
| [`Net/`](Net/) | P2P 락스텝 세션과 UDP 전송 | .NET Standard 2.1 |
| [`Replay/`](Replay/) | 헤드리스 결정론 자체 검증과 리플레이 하네스 | .NET 7 |
| [`Host/`](Host/) | 두 피어의 매치를 콘솔에서 돌리는 러너. `host`, `join`, `solo`, `selfcheck`, `watch`, `replay`, `compare` | .NET 7 |
| [`Client/`](Client/) | Unity 뷰와 HUD. `Sim`과 `Net`을 컴파일된 어셈블리로 참조 | Unity 2022 LTS |

`Sim`과 `Net`은 의도적으로 늦게 Unity와 만났습니다. 초기부터 Unity 프로젝트가
있었다면 `UnityEngine` 타입과 부동소수점이 시뮬레이션으로 새어들기 쉬웠고, 그렇게
생긴 desync는 원인에서 멀리 떨어진 곳에서 드러났을 것입니다.

## 시작하기

빌드해보지 않고 바로 플레이하려면
[Releases](https://github.com/Apptive-Game-Team/WordCraft/releases)에서 Windows·macOS
빌드를 받습니다. 서버가 없는 LAN 프로토타입이므로 한쪽이 host, 다른 쪽이 그
IP로 join합니다.

소스에서 돌리려면 .NET 7 SDK만 있으면 됩니다.

```bash
git clone https://github.com/Apptive-Game-Team/WordCraft.git
cd WordCraft
dotnet run --project Replay
```

`OK: all determinism checks passed`가 출력되면 정상입니다.

## Unity 클라이언트를 열기 전에

`Client/`는 `Sim`과 `Net`을 컴파일된 어셈블리로 참조합니다. 두 파일은 커밋되지
않고 빌드 산출물이므로, 프로젝트를 열기 전에 한 번 빌드해야 합니다.

```bash
dotnet build
```

이 빌드가 `Client/Assets/Plugins/`에 어셈블리를 놓습니다. 어셈블리를 커밋하지 않는
이유는 컴파일할 때마다 내용이 달라져 작업 트리가 상시 더러워지기 때문이고, 빌드마다
복사하는 이유는 클라이언트가 헤드리스 검사와 다른 규칙으로 시뮬레이션하는 드리프트를
막기 위해서입니다.

## 결정론 자체 검증

`Replay`는 테스트 프레임워크 없이 assert 기반으로 다음을 검증합니다.

- 고정소수점 사칙연산, 정수 뉴턴 sqrt, 3-4-5 삼각형 벡터 크기가 정확히 5인지
- RNG 스트림과 드로우 횟수가 같은 시드에서 완전히 일치하는지
- 같은 입력 로그를 두 번 실행했을 때 600틱 전 구간 해시가 동일한지
- 명령 도착 순서를 뒤집어도 정규 순서 정렬 후 결과가 같은지
- 한쪽에 명령 하나를 몰래 끼워 넣었을 때 해당 틱에서 발산이 감지되는지

`Sim/` 아래를 수정하면 반드시 다시 실행합니다.

CI(`.github/workflows/ci.yml`)가 모든 push와 PR에서 이 검사를 CoreCLR로 돌리고,
같은 시나리오를 Unity Mono에서 한 번 더 돌리는 뷰 결정론 검사를 뒤에 붙입니다.
둘이 같은 해시를 내는지가 게이트라서, 골든 해시가 움직이면 두 런타임이 독립적으로
같은 값을 낸 것을 CI에서 확인한 뒤에만 상수를 고칩니다.

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

1. 결정론 코어와 리플레이 하네스 — 완료
2. 자원 채집, 건설, 유닛 생산, 패스파인딩 — 완료
3. P2P 락스텝 네트워크 (LAN 직접 IP) — 완료
4. Unity 뷰와 HUD — 대부분 완료. 전투 가독성 일부와 경보가 남았다
5. 진영 메커니즘과 밸런스 — 진행 중. 여섯 중 셋(물 슬라임 일제 사격, 돌 골렘
   잔해, 지옥불 군단장)이 들어갔다. 나머지 셋과 진영별 밸런스는 남았다

이 목록은 큰 상태만 남깁니다. 남은 일의 세부 순서와 트랙(쓰기 범위)별 동시
진행 방식은 [`.plan/general/2026-08-11-parallel-milestones.md`](.plan/general/2026-08-11-parallel-milestones.md)가,
각 항목이 왜 이 순서이고 무엇이 아직 검증 안 됐는지는
[`.plan/general/2026-08-02-advancement-roadmap.md`](.plan/general/2026-08-02-advancement-roadmap.md)가
갖고 있습니다.

인터넷 대전, NAT 통과, 매치메이킹은 별도 계획입니다.

## 개발 지침

에이전트와 기여자 지침은 [AGENTS.md](AGENTS.md)와 [`.agents/docs/`](.agents/docs/)를
참고합니다. 프로젝트 맥락은 [.agents/docs/project.md](.agents/docs/project.md)에
있습니다.
