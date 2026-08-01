> WordOnline에서 2026-08-01에 복제한 사본이다. 이후로는 WordCraft가 자체적으로
> 관리하며, 원본 저장소가 없어도 이 문서만으로 작업이 성립해야 한다.
> WordCraft 고유 설정은 `docs/FACTIONS.md`와 `docs/FACTION-MECHANICS.md`에 쓴다.

# 마법·소환수 아트 카탈로그

아트 제작 전 대상과 세계관 귀속을 정리한 문서다.

game 서버 동작까지 결합한 개별 페이지는
[`magic/README.md`](magic/README.md)에서 찾는다. 각 서버 마법은
`magic/pages/<server-key>.md` 파일 하나를 가진다.

## 데이터 기준

- 실제 사용 가능한 마법과 조합식은 서버가 정본이다.
- 클라이언트는 서버 이름을 `PascalCase`로 바꿔
  `Client/Assets/Resources/Art/Sprites/<Name>.png`를 불러온다.
- 아래 목록은 클라이언트 한국어 테이블 89행과 현재 스프라이트를 합친 아트
  인벤토리다. 서버 운영 목록과 완전히 같다는 뜻은 아니다.
- `소환수`는 살아 움직이는 개체, `소환물`은 둥지·탑·토템·룬 같은 설치물,
  `마법 효과`는 투사체·폭발·범위 현상으로 구분한다.

## 1. 소환수

### 세계수 정령

워드가 죽은 자리에 자란 세계수에서 태어난 전기·풀·바람 정령. 친근하지만
슬라임과는 다른 종족이다.

| 계열 | 소환수 | 리소스 이름 | 아트 메모 |
|---|---|---|---|
| 전기 | 번개 정령 | `ThunderSpirit` | 전기 정령 기본형 |
| 전기 | 천둥 새 무리 | `ThunderBirdSwarm` | 소형 비행 정령 무리 |
| 전기 | 찌릿쥐 | `ZapMouse` | 전기 짐승형 |
| 풀 | 나무 골렘 | `TreeGolem` | 식물로 엮인 대형 정령 |
| 풀 | 덩굴 정령 | `VineSpirit` | 덩굴 신체 |
| 풀 | 덩굴 군집 | `VineColony` | 군체형 |
| 풀 | 씨앗 정령 무리 | `SeedSpiritSwarm` | 소형 정령 무리 |
| 풀 | 도깨비불 | `WillOWisp` | 세계수 마력의 작은 빛 |
| 풀 | 술이파리 | `Leafair` | 잎사귀 비행 정령. 이름 재검토 필요 |
| 바람 | 폭풍 기수 | `StormRider` | 바람을 타는 기수형 |
| 바람 | 운룡 | `CloudDragon` | 구름·바람 용형 |
| 바람 | 바람 정령 | `WindSpirit` | 바람 정령 기본형 |

### 물 슬라임 생존자

정령이 아니라 물질적 생명체. 전쟁 이후 남은 온건파다. 도구와 장비를 사용할 수
있다.

| 소환수 | 리소스 이름 | 아트 메모 |
|---|---|---|
| 물 슬라임 무리 | `WaterSlimeSwarm` | 기본 군집 |
| 물의 궁수 | `AquaArcher` | 장비를 사용하는 전투원 |
| 거품 정령 | `BubbleSpirit` | 현재 명칭은 정령이지만 물 슬라임 계열로 재해석 필요 |

### 돌 골렘 부족

MiniRock에서 장로까지 이어지는 돌 생명체 부족. 인간의 석조 기계와 분리한다.

| 소환수 | 리소스 이름 | 아트 메모 |
|---|---|---|
| 바위 골렘 | `RockGolem`, `RockGolem2` | 대형 전사 |
| 돌 마법사 | `RockMage` | 부족 주문 시전자 |
| 꼬마 돌 무리 | `MiniRockSwarm` | MiniRock 군집 |
| 포탑등이 | `Towerback` | 등에 포탑을 진 골렘으로 확정할지 미결 |

### 지옥불 군단

정령이 아니라 지옥불 차원의 악마 군단. 기존 불 정령 이름과 귀여운 외형은
세계관에 맞지 않아 전면 재해석 대상이다.

| 현재 이름 | 리소스 이름 | 재해석 방향 |
|---|---|---|
| 불의 정령 | `FireSpirit` | 하급 뿔 악마 |
| 불씨 정령 무리 | `EmberSpiritSwarm` | 작은 악마병 무리 |
| 용암 갑각 악마 | `MagmaSpirit`, `MagmaSpiritAttacking` | 갑각이 갈라진 중갑 악마 |
| 불 대왕 정령 | `FireLordSpirit` | 군단 지휘관 |
| 로컬 이름 없음 | `FireChildSpirit` | 어린 정령이 아닌 하급 악마 |
| 로컬 이름 없음 | `FireTadpole` | 악마 유충·사냥개 계열 |

### 기타·세계관 귀속 미정

| 소환수 | 리소스 이름 | 결정 필요 |
|---|---|---|
| 치킨 특공대 | `ChickenCommando` | 인간 용병인지 독립 종족인지 |
| 차원 두꺼비 | `DimensionToad` | 세계수 생물인지 차원 생물인지 |

## 2. 소환물·건축물

### 인간 마법 문명

얼굴 없는 조립 구조물. 차가운 회색 석재, 스틸블루 금속, 청동 부품을 쓴다.

| 소환물 | 리소스 이름 | 기능 이미지 |
|---|---|---|
| 대포 | `Cannon` | 물리 투사체 방어 시설 |
| 타워 | `Tower` | 기본 마법 탑 |
| 전기 타워 | `ElectricTower` | 전기 증폭 시설 |
| 돌 포탑 | `RockTurret` | 인간제 석조 포탑. 골렘 계열과 분리 필요 |
| 거품 생성기 | `BubbleGenerator` | 물 슬라임 지원 장치 |
| 마나 샘 | `ManaWell` | 마나 공급 시설 |

### 자연·정령 설치물

| 소환물 | 리소스 이름 |
|---|---|
| 생명의 나무 | `LifeTree` |
| 회복 토템 | `HealingTotem` |
| 광란의 토템 | `FrenzyTotem` |
| 바람 토템 | `WindTotem` |
| 비 구름 | `RainCloud` |
| 집결의 횃불 | `RallyingTorch` |
| 풀씨 둥지 | `SeedNest` |

### 슬라임 둥지

현재 로컬 테이블에는 화염·물·번개·자연·바위·바람 둥지 6종이 있다.
세계관 기준으로는 물 슬라임만 현존하므로 나머지 다섯 종류는 다음 중 하나로
재정의해야 한다.

1. 과거에 멸종한 슬라임의 기록
2. 세계수 정령의 둥지로 명칭 변경
3. 지옥불 군단의 악마 산란장으로 교체

현재 리소스에서 확인되는 PVE 둥지는 `PveWaterSlimeNest`,
`PveNatureSlimeNest`다.

같은 문제를 가진 슬라임 무리도 로컬 테이블에는 화염·물·번개·자연·바위·바람
6종이 등록돼 있다. 현재 세계관에서 그대로 유지할 소환수는
`WaterSlimeSwarm`뿐이다. 나머지는 멸종 기록으로 남기거나 해당 진영 생물로
교체해야 한다.

### 원소 룬

화염·물·전기·자연·바위·바람 룬 6종. 생명체가 아니라 플레이어가 전개하는
마법 장치로 통일한다.

리소스 이름: `FireRune`, `WaterRune`, `LightningRune`, `NatureRune`,
`RockRune`, `WindRune`.

## 3. 직접 마법 효과

### 기본 원소 마법

| 유형 | 6원소 변형 |
|---|---|
| 탄환 | 화염탄, 물 탄환, 전격탄, 자연의 탄환, 암석탄, 바람 탄환 |
| 폭발 | 화염 폭발, 물 폭발, 번개 폭발, 덩굴 폭발, 바위 폭발, 돌풍 |
| 투하 | 화염 투하, 물 투하, 번개 투하, 자연 투하, 바위 투하, 바람 투하 |

6원소는 같은 효과군 실루엣을 공유하되, 물질과 움직임만 원소별로 다르게 만든다.

### 전기

- 연쇄 번개 — `ChainLightning`
- 전격 과부하 — `ShockOverload`
- 번개 투하 — `LightningDrop`
- 번개 폭발 — `LightningExplosion`

### 풀·세계수

- 덩굴 투척 — `VineToss`
- 과생장 — `Overgrowth`
- 덩굴 세계 — `VineWorld`
- 거대 덩굴 — `GiantVine` — 현재 로컬 이름 없음

### 바람

- 모래 폭풍 — `SandStorm`
- 토네이도 — `TornadoStrike`
- 바람 칼날 — `WindBlade`
- 칼바람 회오리 — `RazorGale`

### 물

- 파도 소환 — `TideCall`
- 간헐천 폭발 — `WaterExplosion`

### 돌

- 돌 굴리기 — `RockRolling`
- 분화구 — `Crater`
- 바위 투하 — `RockDrop`

### 지옥불

- 유성우 — `MeteorShower`
- 마그마 폭발 — `MagmaExplosion`
- 화염탄 — `FireShot`

지옥불 효과도 악마 군단의 마법이어야 한다. 둥근 정령형 불꽃보다 검붉은
갑각 파편, 뿔 모티프, 내부 용암광을 쓴다.

## 4. 로컬 데이터 문제

아트 생성 전에 이름부터 정리해야 할 항목:

- `Magic Shared Data.asset`에 잘못된 키 `'emberSpiritSwarm`,
  `'waterExplosion`이 별도 행으로 남아 있다.
- `emberSpiritSwarm`이 중복 등록돼 있다.
- 룬 6종은 키와 한국어가 서로 어긋나 있다.
- `vineToss`와 `rockRolling`의 한국어 이름이 서로 뒤바뀌어 있다.
- `windSpirit` 한국어가 `돌풀 정령`으로 저장돼 있다.
- `BubbleSpirit`은 세계관상 물 슬라임 계열인데 이름은 정령이다.
- `FireSpirit`, `MagmaSpirit`, `FireLordSpirit`은 세계관상 악마인데 이름은
  정령이다.
- 스프라이트에는 있지만 로컬 테이블에는 없는 항목:
  `FireChildSpirit`, `FireTadpole`, `GiantVine`, 일부 `*2` 변형.

## 5. 아트 제작 우선순위

1. 지옥불 소환수 6종을 악마 군단 계보로 재설계
2. 세계수 정령 12종의 공통 신체 문법 확정
3. 물 슬라임 3종을 정령과 시각적으로 분리
4. 돌 골렘 4종과 인간 구조물 6종의 재질 분리
5. 직접 마법 효과를 원소별 가족으로 묶어 재생성
6. 세계관 귀속 미정 2종과 둥지 6종의 설정 확정
