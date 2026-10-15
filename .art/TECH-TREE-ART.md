# 테크트리 컨셉 아트

이 문서는 신규 필요 항목의 첫 번째 sprite 시안과 형태 요구, 참조 이미지를 기록한다.

## 세계수 정령

| 한국어 이름 | 영어 리소스 이름 | 파일 | 형태 요구 1~3 | 참조 (anchor, sibling) | 비고 | Unity 경로 |
|---|---|---|---|---|---|---|
| 씨앗 요람 | SeedCradle | `.art/concept/SeedCradle-v1.png` | 1. 64 px에서 위로 열린 큰 뿌리 요람. 2. 가운데 크림색 씨앗 하나와 받치는 넓은 잎. 3. 위쪽 잎 세 장과 몸체에 연결된 둥근 뿌리. | `MasterStyleKey.png`, `WorldTreeSpirit.png`, `SeedNest.png` | 별도 fallback 없음. 신규 필요 항목 누락 없음. | `Client/Assets/Resources/Art/Sprites/SeedCradle.png` (1차 시안, 승인 전 반입) |

## 물 슬라임

| 한국어 이름 | 영어 리소스 이름 | 파일 | 형태 요구 1~3 | 참조 (anchor, sibling) | 비고 | Unity 경로 |
|---|---|---|---|---|---|---|
| 물막이 슬라임 | WaterScreenSlime | `.art/concept/WaterScreenSlime-v1.png` | 1) 64픽셀에서 넓고 낮은 보호 자세가 보인다.<br>2) 오른쪽 어깨의 둥근 물결이 뒤쪽 아군을 가린다.<br>3) 작은 타원형 눈과 몸 안쪽 밝은 면을 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 원거리 아군을 지키는 근접 유닛. | `Client/Assets/Resources/Art/Sprites/WaterScreenSlime.png` (1차 시안, 승인 전 반입) |
| 급류 슬라임 | RiptideSlime | `.art/concept/RiptideSlime-v1.png` | 1) 64픽셀에서 오른쪽으로 빠르게 나아가는 낮은 몸이 보인다.<br>2) 뒤쪽 물결 모양이 몸과 이어진다.<br>3) 눈과 세 단계 명암을 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 물 지대에서 빨라지는 측면 기동 유닛. | `Client/Assets/Resources/Art/Sprites/RiptideSlime.png` (1차 시안, 승인 전 반입) |
| 물대포 슬라임 | WaterCannonSlime | `.art/concept/WaterCannonSlime-v1.png` | 1) 64픽셀에서 큰 몸과 오른쪽으로 나온 굵은 물기둥이 보인다.<br>2) 물기둥이 몸에서 직접 이어진다.<br>3) 몸 안쪽 밝은 면과 작은 눈을 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 문서의 대체 형태 적용. 포신과 노즐 없이 몸에서 물기둥을 뿜는다. | `Client/Assets/Resources/Art/Sprites/WaterCannonSlime.png` (1차 시안, 승인 전 반입) |
| 간헐천 술사 | GeyserMage | `.art/concept/GeyserMage-v1.png` | 1) 64픽셀에서 머리 위의 둥근 물기둥과 선 몸이 보인다.<br>2) 물기둥이 몸에서 이어져 오른쪽으로 굽는다.<br>3) 넓은 밑면과 차분한 작은 눈을 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 물 지대에서 간헐천을 사용하는 비전투 유닛. | `Client/Assets/Resources/Art/Sprites/GeyserMage.png` (1차 시안, 승인 전 반입) |
| 샘물 운반자 | SpringCarrier | `.art/concept/SpringCarrier-v1.png` | 1) 64픽셀에서 등 위에 물을 담은 낮고 넓은 몸이 보인다.<br>2) 물그릇은 장비가 아니라 몸의 움푹한 부분이다.<br>3) 오른쪽을 보는 작은 눈과 둥근 앞쪽 가장자리를 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 이동식 물 지대를 만드는 비전투 유닛. | `Client/Assets/Resources/Art/Sprites/SpringCarrier.png` (1차 시안, 승인 전 반입) |
| 구름 둥지 | CloudNest | `.art/concept/CloudNest-v1.png` | 1) 64픽셀에서 구름을 받치는 넓은 둥지가 보인다.<br>2) 둥근 세 덩이가 가운데의 얕은 공간을 받친다.<br>3) 덩이와 밑면이 물질층으로 이어진다. | MasterStyleKey, WaterSlime, SpringheadPool | 비구름과 구름 용의 생산 건물. | `Client/Assets/Resources/Art/Sprites/CloudNest.png` (1차 시안, 승인 전 반입) |
| 솟는 샘 | RisingSpring | `.art/concept/RisingSpring-v1.png` | 1) 64픽셀에서 낮은 물 고리와 높은 물기둥 하나가 보인다.<br>2) 둥근 물기둥이 아래 물 고리에서 솟는다.<br>3) 몸 안쪽의 넓은 밝은 면을 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 고정 물 지대를 유지하는 비무장 건물. | `Client/Assets/Resources/Art/Sprites/RisingSpring.png` (1차 시안, 승인 전 반입) |
| 거품 장벽 | BubbleMound | `.art/concept/BubbleMound-v1.png` | 1) 64픽셀에서 낮고 넓은 거품 둔덕이 보인다.<br>2) 큰 거품 세 개가 오른쪽으로 낮게 이어진다.<br>3) 밝은 면으로 거품을 구별하면서 한 덩어리를 유지한다. | MasterStyleKey, WaterSlime, SpringheadPool | 문서의 대체 형태 적용. 성벽 대신 낮고 둥근 거품 둔덕. 이름과 기능은 아트 승인 전 제안 상태. | `Client/Assets/Resources/Art/Sprites/BubbleMound.png` (1차 시안, 승인 전 반입) |
| 물결 수련장 | WaveTrainingPool | `.art/concept/WaveTrainingPool-v1.png` | 1) 64픽셀에서 낮은 원형 수조와 가운데 말린 물결이 보인다.<br>2) 물결 끝이 오른쪽을 향하고 안쪽은 열린다.<br>3) 낮은 물질층이 수조의 둘레를 만든다. | MasterStyleKey, WaterSlime, SpringheadPool | 연구 5종을 담당하는 건물. | `Client/Assets/Resources/Art/Sprites/WaveTrainingPool.png` (1차 시안, 승인 전 반입) |

## 지옥불 군단

| 한국어 이름 | 영어 리소스 이름 | 파일 | 형태 요구 1~3 | 참조 (anchor, sibling) | 비고 | Unity 경로 |
|---|---|---|---|---|---|---|
| 균열 사냥개 | RiftHound | `.art/concept/RiftHound-v1.png` | 1) 64 px에서 낮게 달리는 네 발 사냥개와 긴 주둥이.<br>2) 굵은 뒷다리와 갈고리 발톱, 비대칭 뿔.<br>3) 좁은 콧구멍과 작은 눈빛으로 피 냄새 추적 표현. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 빠른 지상 추격 유닛. | `Client/Assets/Resources/Art/Sprites/RiftHound.png` (1차 시안, 승인 전 반입) |
| 잿불 새끼 | EmberWhelp | `.art/concept/EmberWhelp-v1.png` | 1) 64 px에서 작은 머리와 짧은 꼬리를 가진 두 발 악마.<br>2) 짧은 뿔 두 개와 갈고리 앞발 두 개.<br>3) 갈라진 가슴 갑각 안쪽에 작은 빛. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 산란 악마가 낳는 무료 근접 유닛. | `Client/Assets/Resources/Art/Sprites/EmberWhelp.png` (1차 시안, 승인 전 반입) |
| 용암 투석 악마 | LavaCatapultDemon | `.art/concept/LavaCatapultDemon-v1.png` | 1) 64 px에서 낮은 네 발 몸과 등 위의 큰 굽은 발사 갑각.<br>2) 작은 뿔머리가 발사 갑각 아래에서 오른쪽을 본다.<br>3) 등 안쪽의 작은 용암 저장부로 광역 공격 표현. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 투사체 `MagmaExplosion`은 별도 기존 자산. | `Client/Assets/Resources/Art/Sprites/LavaCatapultDemon.png` (1차 시안, 승인 전 반입) |
| 재날개 악마 | AshWingDemon | `.art/concept/AshWingDemon-v1.png` | 1) 64 px에서 양옆으로 뻗은 길고 각진 두 날개.<br>2) 오른쪽을 향한 뾰족한 머리와 갈고리 발톱.<br>3) 잿빛 날개막과 작은 눈빛, 불꽃 자국 없음. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 공중 유닛만 공격한다. | `Client/Assets/Resources/Art/Sprites/AshWingDemon.png` (1차 시안, 승인 전 반입) |
| 산란 악마 | BroodDemon | `.art/concept/BroodDemon-v1.png` | 1) 64 px에서 넓은 배 갑각과 짧고 굵은 다리.<br>2) 배 아래의 갈라진 부분에서 잿불 새끼가 나오는 구조.<br>3) 어두운 갑각 안에 작은 빛 하나. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 지상에서 느리게 이동하며 잿불 새끼를 낳는다. | `Client/Assets/Resources/Art/Sprites/BroodDemon.png` (1차 시안, 승인 전 반입) |
| 운석 술사 | MeteorInvoker | `.art/concept/MeteorInvoker-v1.png` | 1) 64 px에서 길고 마른 몸과 들어 올린 팔 하나.<br>2) 삼각형 갑각 망토와 땅을 딛는 두 발.<br>3) 무기 없이 가슴 안쪽의 작은 빛으로 시전자 표현. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 효과 `MeteorShower`는 별도 기존 자산. | `Client/Assets/Resources/Art/Sprites/MeteorInvoker.png` (1차 시안, 승인 전 반입) |
| 큰 균열 | GrandRift | `.art/concept/GrandRift-v1.png` | 1) 64 px에서 가운데가 크게 열린 비대칭 갑각 아치.<br>2) 두 개의 갈라진 첨탑과 두꺼운 아래쪽 갑각.<br>3) 안쪽 가장자리의 작은 빛으로 차원 통로 표현. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | `RiftAltar`보다 크고 높은 T3 생산 건물. 문과 계단 없음. | `Client/Assets/Resources/Art/Sprites/GrandRift.png` (1차 시안, 승인 전 반입) |
| 화염 첨탑 | FlameSpire | `.art/concept/FlameSpire-v1.png` | 1) 64 px에서 하늘로 뻗은 좁고 높은 갑각 첨탑.<br>2) 세 갈래 아래쪽 갑각과 위를 향한 작은 발사 틈.<br>3) 발사 틈 안쪽에만 작은 빛. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | 공중 전용 방어 건물. 포신과 기계 부품 없음. | `Client/Assets/Resources/Art/Sprites/FlameSpire.png` (1차 시안, 승인 전 반입) |
| 재 분화구 | AshCaldera | `.art/concept/AshCaldera-v1.png` | 1) 64 px에서 두꺼운 둥근 갑각과 가운데 솟은 재 굴뚝.<br>2) 뒤쪽 가장자리에서 솟은 세 개의 각진 갑각.<br>3) 가운데 작은 잿불 통풍구, 폭발과 흩어진 잔해 없음. | `MasterStyleKey.png`, `HellfireDemon.png`, `RiftAltar.png` | `Crater`는 두 진영이 공유하는 직접 마법 효과여서 재사용하지 않음. 상설 연구 건물 형태를 적용. | `Client/Assets/Resources/Art/Sprites/AshCaldera.png` (1차 시안, 승인 전 반입) |

## 차원 유랑종

| 한국어 이름 | 영어 리소스 이름 | 파일 | 형태 요구 1~3 | 참조 (anchor, sibling) | 비고 | Unity 경로 |
|---|---|---|---|---|---|---|
| 덩쿨편 | VineShard | `.art/concept/VineShard-v1.png` | 1. 64 px에서 오른쪽을 향한 여섯 다리 외피 생물과 원거리용 돌출부가 보인다.<br>2. 무광 외피판 사이에 깊이가 있는 작은 청록색 세계가 보인다.<br>3. 다리 여섯 개와 몸체가 하나의 윤곽으로 읽힌다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 세계수 정령의 덩굴 효과인 `VineWorld`를 사용하지 않았다. | `Client/Assets/Resources/Art/Sprites/VineShard.png` (1차 시안, 승인 전 반입) |
| 멸종한 바람 슬라임 | ExtinctWindSlime | `.art/concept/ExtinctWindSlime-v1.png` | 1. 64 px에서 뒤로 흐르는 두 덩어리가 달린 공중 슬라임이 보인다.<br>2. 갈색 외피판 안의 청록색 몸체와 작은 세계가 구분된다.<br>3. 눈 두 개는 작은 타원이며 별도 aura가 없다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 한시 소환수의 첫 시안. | `Client/Assets/Resources/Art/Sprites/ExtinctWindSlime.png` (1차 시안, 승인 전 반입) |
| 떠도는 섬 | WanderingIsland | `.art/concept/WanderingIsland-v1.png` | 1. 64 px에서 넓은 외피와 좁아지는 아랫부분을 가진 공중 개체가 보인다.<br>2. 외피 틈 안에 깊이가 있는 작은 세계가 보인다.<br>3. 아래쪽 돌기가 움직이는 개체임을 드러내며 무기는 없다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 공중 도착 반경 4칸의 비전투 유닛. | `Client/Assets/Resources/Art/Sprites/WanderingIsland.png` (1차 시안, 승인 전 반입) |
| 층위 관측자 | StrataObserver | `.art/concept/StrataObserver-v1.png` | 1. 64 px에서 높은 머리 외피판을 지닌 여섯 다리 생물이 보인다.<br>2. 세로 틈에 여러 거리의 지형 층이 보인다.<br>3. 작은 눈과 닫힌 외피판으로 비전투 시전자를 구분한다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 주변 한시 소환수의 수명을 늘리는 유닛. | `Client/Assets/Resources/Art/Sprites/StrataObserver.png` (1차 시안, 승인 전 반입) |
| 멸종한 자연 슬라임 | ExtinctNatureSlime | `.art/concept/ExtinctNatureSlime-v1.png` | 1. 64 px에서 둥근 몸과 넓은 잎 모양 윗부분이 보인다.<br>2. 갈색 외피판의 좁은 틈에 청록색 지형이 보인다.<br>3. 작은 타원형 눈 두 개가 있는 단일 몸체다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 한시 치유 유닛의 첫 시안. | `Client/Assets/Resources/Art/Sprites/ExtinctNatureSlime.png` (1차 시안, 승인 전 반입) |
| 깊은 통로 | DeepPassage | `.art/concept/DeepPassage-v1.png` | 1. 64 px에서 두꺼운 외피판 둘이 만든 높은 세로 통로가 보인다.<br>2. 안쪽에 한 쌍의 작은 외피판이 추가로 보인다.<br>3. 틈 안의 지형이 뒤로 갈수록 작아진다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 기존 `Passage`와 안쪽 외피판으로 구분한다. | `Client/Assets/Resources/Art/Sprites/DeepPassage.png` (1차 시안, 승인 전 반입) |
| 위상 닻 | PhaseAnchor | `.art/concept/PhaseAnchor-v1.png` | 1. 64 px에서 좁고 높은 외피판 세 갈래가 보인다.<br>2. 갈래 사이 작은 틈 안에 청록색 지형이 보인다.<br>3. 무기나 받침대 없이 세 끝이 같은 높이에 닿는다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 첫 결과가 `AnchoredWorld`와 비슷해 다시 생성한 시안. | `Client/Assets/Resources/Art/Sprites/PhaseAnchor.png` (1차 시안, 승인 전 반입) |
| 틈새 포대 | RiftBattery | `.art/concept/RiftBattery-v1.png` | 1. 64 px에서 오른쪽으로 벌어진 외피판 세 장이 공격 방향을 보여 준다.<br>2. 열린 부분에는 총구 대신 깊이가 있는 작은 세계가 보인다.<br>3. 외피판의 겹침으로 원거리 방어 건물임을 읽을 수 있다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 형태 충돌의 fallback 적용: 포신과 기계 받침대 없이 외피판이 겹쳐 열린다. 아트 승인 전에는 확정하지 않는다. | `Client/Assets/Resources/Art/Sprites/RiftBattery.png` (1차 시안, 승인 전 반입) |
| 외피 연마장 | ShellPolishingYard | `.art/concept/ShellPolishingYard-v1.png` | 1. 64 px에서 낮고 넓은 외피판 네 장의 열린 배치가 보인다.<br>2. 판 사이에 청록색 지형과 먼 보라색 하늘이 보인다.<br>3. 오른쪽의 올라간 외피판으로 다른 건물과 구분한다. | `MasterStyleKey`, `ArcaneImpact`, `AnchoredWorld` | 연구 6종을 여는 기술 건물의 첫 시안. | `Client/Assets/Resources/Art/Sprites/ShellPolishingYard.png` (1차 시안, 승인 전 반입) |


## 돌 골렘 부족

`docs/TECH-TREE.md`의 신규 필요 유닛 6종과 건물 4종에 대한 첫 시안이다. 각 이미지는 `MasterStyleKey`, `RockGolem`, `MossrockSanctum`을 참조해 만들었고, 부유석 2종에는 낮게 뜨는 무거운 돌 형태를 적용했다.

| 한국어 이름 | 영어 리소스 이름 | 파일 | 형태 요구 1~3 | 참조 (anchor, sibling) | 비고 | Unity 경로 |
|---|---|---|---|---|---|---|
| 성벽 골렘 | WallGolem | `.art/concept/WallGolem-v1.png` | 1. 64 px에서 넓은 몸통과 두 발로 선 돌벽이 보인다.<br>2. 큰 바위 세 덩이가 붙은 몸통과 짧은 팔이 잔해 장벽의 역할을 보여 준다.<br>3. 큰 이음매에 이끼가 자라고 작은 눈 두 개가 보인다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 첫 결과는 일반 골렘과 실루엣 차이가 작다. 승인 전 몸통 폭을 재검토한다. | `Client/Assets/Resources/Art/Sprites/WallGolem.png` (1차 시안, 승인 전 반입) |
| 깨어난 바위 | AwakenedStone | `.art/concept/AwakenedStone-v1.png` | 1. 64 px에서 잔해 바위에서 일어선 작은 골렘이 보인다.<br>2. 큰 하체 바위가 두 발로 나뉘고 작은 머리와 짧은 팔이 붙는다.<br>3. 이끼가 이음매 하나를 따라 이어지고 작은 눈 두 개가 보인다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 첫 결과는 일어나는 동작보다 서 있는 골렘에 가깝다. | `Client/Assets/Resources/Art/Sprites/AwakenedStone.png` (1차 시안, 승인 전 반입) |
| 돌팔매 꼬마돌 | PebbleSlinger | `.art/concept/PebbleSlinger-v1.png` | 1. 64 px에서 큰 둥근 돌 하나를 든 작은 투척 골렘이 보인다.<br>2. 작은 머리, 짧은 몸통, 땅에 붙은 두 발과 돌을 든 팔이 구분된다.<br>3. 등과 팔의 이끼, 작은 눈 두 개가 보인다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 돌이 머리보다 커서 저렴한 원거리 유닛의 크기감은 재검토가 필요하다. | `Client/Assets/Resources/Art/Sprites/PebbleSlinger.png` (1차 시안, 승인 전 반입) |
| 부유석 파수 | FloatingStoneSentinel | `.art/concept/FloatingStoneSentinel-v1.png` | 1. 64 px에서 땅 가까이 뜬 무거운 돌 파수가 보인다.<br>2. 넓은 어깨 바위와 작은 머리, 오른쪽을 향한 두꺼운 팔이 보인다.<br>3. 위쪽 이음매에 이끼가 자라며 하나의 단단한 몸체를 유지한다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 형태 충돌의 fallback으로 낮게 뜨는 무거운 돌을 선택했다. 첫 결과에는 바닥과 몸 사이 틈이 약해 공중 유닛으로 읽기 어렵다. 아트 승인 전에는 확정하지 않는다. | `Client/Assets/Resources/Art/Sprites/FloatingStoneSentinel.png` (1차 시안, 승인 전 반입) |
| 모래 술사 | SandShaper | `.art/concept/SandShaper-v1.png` | 1. 64 px에서 오른팔을 뻗어 모래 덩어리를 다루는 돌 술사가 보인다.<br>2. 작은 머리와 넓은 두 발, 굵은 종이층 모양 모래가 구분된다.<br>3. 어깨 이끼와 작은 눈 두 개가 보인다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | `SandStorm` 효과와 별도로 쓰는 유닛 시안이다. | `Client/Assets/Resources/Art/Sprites/SandShaper.png` (1차 시안, 승인 전 반입) |
| 이끼 포자 무리 | MossSporeCluster | `.art/concept/MossSporeCluster-v1.png` | 1. 64 px에서 이끼를 덮은 돌 세 덩이가 하나의 작은 무리로 보인다.<br>2. 둥근 황갈색 돌 중심과 넓은 이끼 윗부분이 겹친다.<br>3. 큰 이끼 잎 한 장이 재생 지원 역할을 암시한다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 형태 충돌의 fallback으로 땅 가까이에 모인 무거운 돌을 선택했다. 첫 결과는 공중 이동이 잘 드러나지 않는다. 아트 승인 전에는 확정하지 않는다. | `Client/Assets/Resources/Art/Sprites/MossSporeCluster.png` (1차 시안, 승인 전 반입) |
| 각성의 원 | AwakeningCircle | `.art/concept/AwakeningCircle-v1.png` | 1. 64 px에서 넓고 낮은 바위 고리와 가운데 단단한 돌판이 보인다.<br>2. 네 개의 큰 자연석이 안쪽으로 기울어 생산 장소를 이룬다.<br>3. 큰 이음매를 따라 이끼가 자라고 뒤쪽 바위 하나가 높다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 첫 결과는 원보다 바위 더미에 가까워 승인 전 재작업이 필요하다. | `Client/Assets/Resources/Art/Sprites/AwakeningCircle.png` (1차 시안, 승인 전 반입) |
| 부유석 첨탑 | HoverstoneSpire | `.art/concept/HoverstoneSpire-v1.png` | 1. 64 px에서 낮은 돌기둥 위에 큰 바위 하나가 떠 있는 방어 건물이 보인다.<br>2. 큰 윗돌과 넓은 받침돌이 공중 표적에 돌을 떨어뜨리는 형태를 이룬다.<br>3. 자연석 이음매와 받침의 이끼가 보인다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 형태 충돌의 fallback으로 낮게 뜬 큰 바위를 선택했다. 포신, 조준부, 발사구는 없다. 아트 승인 전에는 확정하지 않는다. | `Client/Assets/Resources/Art/Sprites/HoverstoneSpire.png` (1차 시안, 승인 전 반입) |
| 잠듦의 고리 | DormancyRing | `.art/concept/DormancyRing-v1.png` | 1. 64 px에서 낮은 바위 다섯 개가 가운데 잠든 돌을 감싼 형태가 보인다.<br>2. 바깥 바위가 안으로 기울고 이끼가 아랫부분을 잇는다.<br>3. 뒤쪽 돌이 앞쪽 돌보다 높아 깊이가 보인다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 첫 결과는 고리보다 바위 더미에 가깝다. 잔해 만료 연장 기능도 형태만으로 드러나지 않아 승인 전 재작업이 필요하다. | `Client/Assets/Resources/Art/Sprites/DormancyRing.png` (1차 시안, 승인 전 반입) |
| 이끼 정원 | MossGarden | `.art/concept/MossGarden-v1.png` | 1. 64 px에서 이끼가 자라는 낮고 넓은 돌 단이 보인다.<br>2. 큰 돌 세 층 사이에 넓은 이끼와 잎이 자란다.<br>3. 작은 꽃이나 도구 없이 식생과 따뜻한 돌의 비율이 유지된다. | `MasterStyleKey`, `RockGolem`, `MossrockSanctum` | 연구 건물의 첫 시안이다. | `Client/Assets/Resources/Art/Sprites/MossGarden.png` (1차 시안, 승인 전 반입) |

## 인간 마법 문명

| 한국어 이름 | 영어 리소스 이름 | 파일 | 형태 요구 1~3 (한 줄씩) | 참조 (anchor, sibling) | 비고 | Unity 경로 |
|---|---|---|---|---|---|---|
| 석궁 사수 | `CrossbowMarksman` | `.art/concept/CrossbowMarksman-v1.png` | 1) 사람과 오른쪽을 겨눈 넓은 석궁<br>2) 두꺼운 후드와 회색 갑옷<br>3) 나무 활과 금속 받침 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/CrossbowMarksman.png` (1차 시안, 승인 전 반입) |
| 마도사 | `ArcaneMage` | `.art/concept/ArcaneMage-v1.png` | 1) 지팡이를 든 사람<br>2) 회색 옷과 스틸블루 어깨 보호대<br>3) 지팡이 끝의 작은 결정 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 낙뢰 효과는 별도 자산 | `Client/Assets/Resources/Art/Sprites/ArcaneMage.png` (1차 시안, 승인 전 반입) |
| 석조 보행 포대 | `StoneWalkerBattery` | `.art/concept/StoneWalkerBattery-v1.png` | 1) 네 다리의 낮은 포대와 오른쪽 포신<br>2) 이음매가 보이는 석조 외피<br>3) 짧은 포신과 두꺼운 전면 장갑 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/StoneWalkerBattery.png` (1차 시안, 승인 전 반입) |
| 견인 대포 | `TowedCannon` | `.art/concept/TowedCannon-v1.png` | 1) 긴 포신과 두 바퀴<br>2) 견인 막대와 전개용 차축<br>3) 석조 약실과 스틸블루 고정 장치 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/TowedCannon.png` (1차 시안, 승인 전 반입) |
| 마도 폭격선 | `ArcaneBomber` | `.art/concept/ArcaneBomber-v1.png` | 1) 넓은 날개와 선체 아래 폭탄칸<br>2) 각진 금속 선체와 석조 장갑<br>3) 닫힌 폭탄칸 한 개 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/ArcaneBomber.png` (1차 시안, 승인 전 반입) |
| 마도 기수 | `ArcaneRider` | `.art/concept/ArcaneRider-v1.png` | 1) 네 다리 석조 장치에 탄 사람<br>2) 기계 관절과 스틸블루 결합부<br>3) 오른쪽을 겨눈 짧은 원거리 무기 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 문서의 형태 충돌에 따라 생물 탈것 대신 다리 달린 석조 장치를 적용. 아트 승인 전 제안 | `Client/Assets/Resources/Art/Sprites/ArcaneRider.png` (1차 시안, 승인 전 반입) |
| 마도 정찰기 | `ArcaneScout` | `.art/concept/ArcaneScout-v1.png` | 1) 직선 날개 두 개를 단 작은 정찰기<br>2) 석조 외피와 금속 선체<br>3) 무기 없이 표식 장치 한 개 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 공중 이동은 문서의 제안이며 현재 코드와 다름 | `Client/Assets/Resources/Art/Sprites/ArcaneScout.png` (1차 시안, 승인 전 반입) |
| 공방 기술자 | `WorkshopEngineer` | `.art/concept/WorkshopEngineer-v1.png` | 1) 큰 렌치를 든 사람<br>2) 회색 작업복과 스틸블루 보호대<br>3) 허리 공구함 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/WorkshopEngineer.png` (1차 시안, 승인 전 반입) |
| 마나 증폭기 | `ManaAmplifier` | `.art/concept/ManaAmplifier-v1.png` | 1) 중앙 기둥과 대칭 안테나 두 개<br>2) 바퀴가 달린 석조 본체<br>3) 보호된 결정 하나, 포신 없음 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/ManaAmplifier.png` (1차 시안, 승인 전 반입) |
| 석조 병영 | `StoneBarracks` | `.art/concept/StoneBarracks-v1.png` | 1) 넓고 낮은 병영과 중앙 출입구<br>2) 석재 벽돌과 스틸블루 지붕<br>3) 양쪽 훈련 공간 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/StoneBarracks.png` (1차 시안, 승인 전 반입) |
| 마도 공창 | `ArcaneFoundry` | `.art/concept/ArcaneFoundry-v1.png` | 1) 양쪽 작업장과 중앙 기중기<br>2) 석조 벽과 금속 제작 장치<br>3) 열린 작업장 안의 대포 차체 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/ArcaneFoundry.png` (1차 시안, 승인 전 반입) |
| 마도 학당 | `ArcaneAcademy` | `.art/concept/ArcaneAcademy-v1.png` | 1) 중앙 지붕과 양쪽 학습 공간<br>2) 석조 벽과 스틸블루 지붕<br>3) 정면 책 표식과 작은 결정 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 없음 | `Client/Assets/Resources/Art/Sprites/ArcaneAcademy.png` (1차 시안, 승인 전 반입) |
| 결속대 | `BindingPlatform` | `.art/concept/BindingPlatform-v1.png` | 1) 낮은 받침대와 양쪽 고정 팔<br>2) 청동 사슬과 스틸블루 버팀대<br>3) 비어 있는 중앙 석조 받침 | `MasterStyleKey.png`, `HumanMagicTower.png`, `Cannon.png` | 포획된 몸은 그리지 않음 | `Client/Assets/Resources/Art/Sprites/BindingPlatform.png` (1차 시안, 승인 전 반입) |
