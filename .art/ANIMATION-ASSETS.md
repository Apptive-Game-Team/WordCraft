> WordOnline에서 2026-08-01에 복제한 사본이다. 이후로는 WordCraft가 자체적으로
> 관리하며, 원본 저장소가 없어도 이 문서만으로 작업이 성립해야 한다.
> WordCraft 고유 설정은 `docs/FACTIONS.md`와 `docs/FACTION-MECHANICS.md`에 쓴다.

# 애니메이션 프레임과 오라 에셋

Unity 런타임에서 하나의 소환수 외형을 구성하는 기본 프레임, 공격 프레임, 오라를
구분하는 정본 문서다. 숫자 접미사는 별도 캐릭터가 아니라 상태 프레임일 수 있다.

## 의미 이름

| 실제 파일 | 의미 이름 | 런타임 연결 |
|---|---|---|
| `MagmaSpirit.png` | 용암 갑각 악마 · 선 기본 자세 | `MagmaSpirit.prefab` 기본 SpriteRenderer |
| `MagmaSpiritAttacking.png` | 용암 갑각 악마 · 내려찍기 자세 | `OnAttackSpriteSwapper.onAttackSprite` |
| `MagmaSpiritSpawn.png` | 용암 갑각 악마 · 지면 돌파 소환 자세 | 생성 시 0.28초 표시 |
| `RockGolem.png` | 이끼바위 골렘 · 기본 자세 | `RockGolem.prefab` 기본 SpriteRenderer |
| `RockGolem2.png` | 이끼바위 골렘 · 공격 자세 | `OnAttackSpriteSwapper.onAttackSprite` |
| `TreeGolem.png` | 고목 수호자 · 기본 자세 | `TreeGolem.prefab` 기본 SpriteRenderer |
| `TreeGolem2.png` | 고목 수호자 · 공격 자세 | `OnAttackSpriteSwapper.onAttackSprite` |
| `AquaArcher.png` | 물결 궁수 · 활시위를 당긴 기본 자세 | `AquaArcherAttackPresenter` 기본 Sprite |
| `AquaArcherAttack.png` | 물결 궁수 · 시위를 놓은 공격 자세 | 공격 이벤트에서 0.08초 표시 |

실제 파일명과 `.meta` GUID는 런타임 참조 때문에 유지한다. 문서와 홈페이지에서는
숫자 접미사 대신 의미 이름을 표시한다.

## 공격 프레임 규칙

`OnAttackSpriteSwapper`는 공격 이벤트에서 공격 프레임으로 바꾸고 기본값 기준
0.2초 뒤 원래 프레임으로 복귀한다. Transform 보정 없이 Sprite만 바꾸므로 두
이미지가 아래 조건을 만족해야 한다.

`AquaArcherAttackPresenter`는 평상시에 당긴 프레임을 유지하다 공격 이벤트에서
놓은 프레임으로 즉시 바꾸고 0.08초 뒤 복귀한다. 위치 튐을 막기 위해 별도
Transform 스케일 변형은 적용하지 않는다.

`MagmaSpiritSpawnPresenter`는 일반 소환 연출을 재생하는 생성에서만 지면 돌파
프레임을 0.28초 표시한 뒤 선 기본 자세로 복귀한다. 동기화 복구처럼
`playSpawnPresentation`이 꺼진 생성에서는 소환 프레임을 건너뛴다.

- 동일 PPU
- 동일 `Bottom Center` 피벗
- 동일 캐릭터 몸 배율
- 발 또는 지면 접점의 동일한 픽셀 기준점
- 동일 카메라 각도와 우측 방향
- 공격 동작에 필요한 팔·머리·효과만 변화
- 캔버스가 달라도 기준점에서 몸통 핵심점까지의 거리는 동일

검증할 때 두 PNG를 같은 PPU로 겹쳐 발점, 골반, 머리 중심을 대조한다.
`RockGolem`과 `RockGolem2`는 256x244, PPU 100, Bottom Center로 교정했다. 현재
`TreeGolem`과 `TreeGolem2`는 PPU가 서로 달라 재작업 전에 교정이 필요하다.

## 오라 의미

| 실제 파일 | 의미 이름 | 사용 방식 |
|---|---|---|
| `fire_aura.png` | 지옥불 · 대기 오라 / 공격 파동 | `FireIdleAura`, `FireAttackAura` |
| `wind_aura.png` | 바람 · 대기 오라 / 공격 파동 | `WindIdleAura`, `WindAttackAura` |
| `ligtning_aura.png` | 전기 · 대기 오라 / 공격 파동 | `LightningIdleAura`, `LightningAttackAura` |
| `nature_aura.png` | 자연 · 대기 오라 / 공격 파동 | `NatureIdleAura`, `NatureAttackAura` |
| `rock_aura.png` | 바위 · 대기 오라 / 공격 파동 | `RockIdleAura`, `RockAttackAura` |
| `water_aura.png` | 물 · 대기 오라 / 공격 파동 | `WaterIdleAura`, `WaterAttackAura` |
| `cloud.png` | 운룡 · 구형 물 아우라 | `CloudDragon.prefab` 전용 자식 SpriteRenderer |

오라는 본체에 합성하지 않는다. 공용 투명 Sprite로 별도 생성한다.

- 대기 오라: 반복 맥동, 알파 70%까지 변화, 약한 ±3도 회전
- 공격 파동: 0.3초 동안 0.86배 수축 후 1.35배 확장하며 페이드아웃
- 중심 정렬, 방사형 또는 타원형 실루엣
- 캐릭터 얼굴·팔다리·무기와 경쟁하는 고밀도 디테일 금지
- 불·운룡처럼 오라가 정체성에 필요한 개체도 본체와 오라를 독립 검증
- 운룡의 `cloud.png`는 바람 오라가 아니라 몸 전체를 감싸는 구형 물 아우라다.
  중앙은 본체가 읽히도록 저밀도로 유지하고, 공용 `wind_aura.png`와 혼용하지 않는다.

## 홈페이지 표시

- 기본·공격 프레임이 실제로 다른 포즈면 둘 다 표시한다.
- 컨셉과 축소 인게임 이미지가 같은 포즈면 컨셉만 표시한다.
- 오라는 소환수 상세의 `부속 에셋`으로 표시한다.
- 마법 상세와 소환수 상세은 양방향 링크를 제공한다.
