# 바벨 엔진 — 인수인계

웹 프로토타입(`WebPrototype/`)의 분대 전투를 Unity로 옮겨 **종족전쟁 게임**으로 키우는 프로젝트.
여러 종족이 각자 군단(지휘관 분대 + 분대 4개)을 이끌고 한 전장에서 싸운다.

## 환경

- Unity **6000.3.19f1** (URP, Input System 패키지)
- 저장소: https://github.com/yoojrdr3/BabelEngine (`main`)
- Unity MCP: CoplayDev "MCP for Unity" (`Packages/manifest.json` 에 포함)

## 다른 PC에서 이어서 하기

1. `git clone https://github.com/yoojrdr3/BabelEngine.git`
2. Unity Hub에서 폴더를 6000.3.19f1 로 연다.
3. `Assets/BabelEngine/Scenes/Battle.unity` 를 열고 플레이.
4. Claude로 Unity를 조작하려면:
   - Unity 메뉴 `Window > MCP for Unity` → Transport `HTTP Local`, URL `http://127.0.0.1:8080` → **Start Server** (초록불 "Session Active" 확인)
   - 이 폴더에서 Claude 세션을 연다. 루트의 `.mcp.json` 이 `unityMCP` 서버를 등록한다. 승인 창이 뜨면 허용.
   - 목록에 안 뜨면 `~/.claude.json` 의 `mcpServers` 에 `"unityMCP": {"type":"http","url":"http://127.0.0.1:8080/mcp"}` 를 넣고 Claude 앱을 완전히 종료 후 재시작.

## 폴더 구조 (`Assets/BabelEngine/`)

| 폴더 | 내용 |
|---|---|
| `Scripts/` | 게임 로직 |
| `Editor/BabelBuilder.cs` | 에셋 생성 빌더 (메뉴 `Babel > ...`) |
| `Data/` | `BattleConfig.asset`(공통 수치), 종족 정의 에셋 |
| `Prefabs/`, `Materials/`, `Textures/` | 빌더가 생성 |
| `Scenes/Battle.unity` | 전장 씬 (빌더가 생성) |

`WebPrototype/` 는 원본 웹 프로토타입(game.js, README.md)과 캐릭터 일러스트 6장. 규칙의 기준 문서는 `WebPrototype/README.md`.

## 핵심 설계

- **에셋 생성 방식**: 씬·프리팹·머티리얼은 손으로 만들지 않고 `BabelBuilder` 메뉴로 생성한다. 빌더를 다시 돌리면 씬과 프리팹은 덮어쓴다. `Data/` 의 설정 에셋은 이미 있으면 보존한다(튜닝 값 유지).
- **좌표**: 로직은 웹 단위(u)로 계산한다. `(x, y)` = Unity `(X, Z)`, `unitScale`(0.05) 로 미터 변환. 웹과 달리 y 가 화면 위쪽.
- **한 곳에서 순서대로 갱신**: `BattleWorld.Update` 가 입력 → 군단 → 겹침 해소 → 카메라 → 투사체 → 화면 반영 순으로 부른다. 개별 컴포넌트는 `Update` 를 갖지 않는다.
- **Unit**: 지휘관·분대장·분대원·적 공통 몸체(위치, 체력, 팀).
- **Squad**: 분대장 1 + 분대원 4, 상태 기계(대기/근접/퇴각/복귀).
- **Army**: 군단. 웹 프로토타입 `update()` 의 분대 로직을 그대로 옮겼다. 조작 주체가 `moveInput`/`cursor` 를 넣고 명령 함수를 부른다.
- **PlayerController**: 플레이어 입력과 카메라 모드·줌.
- **주의**: MonoBehaviour 클래스는 반드시 같은 이름의 파일에 둔다(한 파일에 둘 넣으면 AddComponent 가 null).
- **주의**: 빌더에서 `NewScene` 은 프리팹을 만들기 전에 부른다(나중에 부르면 프리팹 참조가 파괴된다).

## 조작법

WASD 이동 · 좌클릭 발사 · 1–4 분대 선택(같은 숫자 한 번 더: 카메라 이동) · 우클릭 지휘/커서 추적(한 번 더: 전투 허용, 더블 클릭: 고정) · Space/Esc 아바타 모드 · 휠 줌 · X 전투 금지(X X 해제)

## 진행 단계

1. **조작·카메라 이식** — 완료. 군단 1개, 대형, 거리 제한 D, 지휘, 자동 전투, 카메라 모드.
2. **쌍방 전투** — 진행 중. 체력·사망, AI 지휘관, 승패.
3. **종족 데이터** — 종족별 수치·성향을 에셋으로 분리, 종족 선택.
4. **외형** — 일러스트 기반 캐릭터, 이펙트, 정식 HUD. (지금은 임시 도형 + IMGUI)
5. **3파전·목표물** — 전장 규칙 확장.

웹 프로토타입에 있고 아직 안 옮긴 것: 화면 가장자리 분대 방향 화살표, 분대 카드, 튜닝 패널(대신 `BattleConfig.asset` 인스펙터 사용), 타격 이펙트.

## 종족 구상 (일러스트 기준)

| 종족 | 일러스트 | 성격 |
|---|---|---|
| 메타포스 | 석판 중장갑 | 뭉쳐서 밀고 가는 벽: 체력 높음, 근접 강함, 느림, D 짧음 |
| 타나르크 | 흰 장갑 + 푸른 회로 | 넓게 펼쳐 사격: 사거리·사격 피해 높음, 체력 낮음, 근접 회피 |
| 악마 | 혼돈(지휘관), 파괴·종말(분대) | 물면 안 놓는 돌격: 근접·속도 높음, 퇴각 없음 |

악마 3장을 한 종족의 병종으로 묶은 것은 추정이다. 사용자 확인이 필요하다.

## 작업 규칙 (사용자 요청)

- 한국어로 소통한다.
- 만드는 것은 에셋 생성 방식으로 남겨 항상 검수할 수 있게 한다.
- 작업 중 무엇을 하고 있는지 짧게 자주 알린다.
- MCP 연결이 안 되면 이것저것 시도하지 말고 바로 멈추고 알린다.
- 작업 단계가 끝날 때마다 커밋하고 푸시한다.
- 3D 생성(Blender MCP `generate_3d`)은 비용이 들 수 있으니 실행 전에 확인받는다.

## 미해결

- 캐릭터 표현 방식(2D 빌보드 vs 3D 모델) 미정. 지금은 임시 도형.
- Blender MCP 는 Blender 를 켜고 애드온을 실행해야 연결된다. 아직 시도하지 못했다.
- 키보드·마우스 조작감은 사람이 직접 플레이해서 확인해야 한다(MCP 로는 값만 확인 가능).
