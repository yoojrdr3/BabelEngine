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
2. **쌍방 전투 + 종족 군단** — 진행 중(절반). 아래 "2단계 작업 지시" 참고.
3. **종족 선택 화면** — 시작할 때 플레이할 종족 고르기.
4. **외형** — 일러스트 기반 캐릭터, 이펙트, 정식 HUD. (지금은 임시 도형 + IMGUI)
5. **3파전·목표물** — 전장 규칙 확장.

웹 프로토타입에 있고 아직 안 옮긴 것: 화면 가장자리 분대 방향 화살표, 분대 카드, 튜닝 패널(대신 `BattleConfig.asset` 인스펙터 사용), 타격 이펙트.

## 2단계 작업 지시 (여기서부터 이어서)

목표: 종족 3개가 각자 군단을 이끌고 한 전장에서 싸운다. 플레이어 1 + AI 2, 지휘관이 죽으면 그 군단 붕괴.

### 이미 들어간 것 (컴파일됨)
- `RaceDefinition.cs` — 종족 에셋(프리팹 3종, 배율 hp/speed/tether/range/melee/ranged, 성향 `noRetreat`·`avoidMelee`).
- `ArmySpawn.cs` — 씬에 놓는 군단 배치점(race, team, playerControlled, headingDeg).
- `BattleConfig` — `commanderHp/leaderHp/memberHp`, `squadRespawn`, `reinforceInterval`, `aiFireCooldown`, `aiPreferredRange`, `aiReorderInterval`, `BattleMath.Rotate` 추가.
- `Unit.squad`, `Unit.army`, `Squad.wiped/respawn/reinforce` 필드 추가(아직 아무도 안 씀).
- `AICommander.cs` — 초안. `#if BABEL_STAGE2` 로 감싸 컴파일에서 빠져 있다. 아래 API 가 생기면 `#if`/`#endif` 두 줄을 지운다.

### 남은 일

**1. `Army.cs` 개편**
- 필드 추가: `RaceDefinition race`, `bool isPlayer`, `bool defeated`, `AICommander ai`.
- `D => config.D * race.tetherMul`, `Range => config.D * config.rangeMul * race.rangeMul`. `Tick`/`OrderMove`/`LimitCenter`/`InLimitCircle` 안의 `C.D`·`config.D` 를 전부 `D` 로 바꾼다.
- 속도(`commanderMaxSpeed`, `steerMax`, `memberMaxSpeed`)에 `race.speedMul` 을 곱한다.
- `Build(world, race, team, isPlayer, origin, heading)`: 프리팹은 `race.commanderPrefab/leaderPrefab/memberPrefab`. 분대 시작 위치는 `BattleMath.Rotate(config.squadStart[i], heading - PI/2)`. 스폰할 때 `u.maxHp = 기준체력 * race.hpMul` 을 `Setup` 전에 넣고, `u.squad`, `u.army` 를 채운다.
- 근접 피해 `world.Damage(e, 피해 * race.meleeDamageMul, this)`, 사격 `world.SpawnProjectile(kind, this, pos, angle)`.
- 성향: `avoidMelee` 면 Melee 상태로 들어가지 않는다. `noRetreat` 면 "제한 원 이탈 → Retreat" 대신, 대상이 지휘관에서 `D + meleeReach + 60` 보다 멀어졌을 때만 대상을 버리고 Return.
- `Tick` 맨 앞에 손실 처리:
  - 지휘관이 죽으면 `defeated = true`, 군단의 모든 유닛 `dead = true`, 이후 Tick 안 함.
  - 분대장이 죽었고 살아 있는 분대원이 있으면 승계: 분대장을 그 분대원 위치에서 `hp = maxHp * 0.6` 으로 되살리고 그 분대원을 죽인다.
  - 분대장도 분대원도 없으면 `wiped = true`, `respawn = config.squadRespawn`. 시간이 지나면 지휘관 곁에서 전원 체력 가득 재편성(state = Return).
  - 분대원이 하나라도 죽어 있으면 `reinforceInterval` 마다 1명을 분대장 위치에서 되살린다.
- 분대 루프에서 `wiped` 이거나 분대장이 죽은 분대는 건너뛴다. `UpdateMembers` 에서 죽은 분대원은 건너뛴다. 집결 도착 판정도 wiped 분대는 제외.
- `StatusOf`: wiped 면 `"재편성 중 (n초)"`.

**2. `BattleWorld.cs` 개편**
- `playerPrefabs` 필드 삭제. `List<Army> armies`, `string outcome`.
- `Awake`: `FindObjectsByType<ArmySpawn>` 을 team 순으로 돌며 군단 생성. 위치는 `transform.position / unitScale`. `playerControlled` 인 첫 군단이 `playerArmy`, 나머지는 `AddComponent<AICommander>()` 해서 `army.ai` 에 넣는다.
- `Update`: `player.HandleInput()` → 각 군단 `ai?.Tick(dt)` 후 `Tick(dt)` → `SeparateMembers` → 카메라 → 투사체 → 승패 판정 → 모든 유닛 `flash` 감쇠(dt*6) → `SyncView`.
- `NearestHostileCommander(Army self)`: 패배하지 않은 다른 팀 군단의 지휘관 중 가장 가까운 것.
- `Damage(Unit e, float dmg, Army from)`: 피해 숫자 색은 `from.race.color`, `e.squad.hitAt = time`, 피해 숫자 목록은 60개로 제한.
- `SpawnProjectile(kind, Army a, from, angle)`: 피해 `Shot.damage * a.race.rangedDamageMul`, 비행 거리 `a.Range - 20`, 색은 종족 색(렌더러 `_BaseColor` 는 MaterialPropertyBlock, 트레일은 start/endColor). 팀 판정은 `a.team`.
- 승패: 플레이어 군단 패배 → `"패배"`, 다른 군단 전부 패배 → `"승리"`.

**3. `PlayerController.cs`**: `C.D` → `army.D`. 군단 패배 시 입력 무시. 선택 분대가 wiped 면 발사 안 함. `outcome` 이 있을 때 R 키로 `SceneManager.LoadScene(현재 씬 이름)`.

**4. `BattleOverlay.cs`**: `C.D` → `army.D`. wiped 분대의 표시는 끈다.

**5. `BattleHud.cs`**: 체력 막대를 모든 군단의 지휘관·분대장에 표시(아군 초록, 적 빨강, 지휘관은 더 크게 + 종족 이름). 요약에 지휘관 체력, 남은 적 군단 수. `outcome` 을 화면 가운데 크게 + "R: 다시 시작".

**6. `BabelBuilder.cs`**
- 종족별 머티리얼·프리팹(`{종족}_Commander/Leader/Member`). 임시 외형은 도형으로 구분(메타포스 = 각진 블록, 타나르크 = 길쭉한 캡슐, 악마 = 캡슐 + 뿔). 지휘관은 머리 위 금색 구.
- `Data/Race_Metaphos.asset`, `Race_Tanarc.asset`, `Race_Demon.asset` 생성(있으면 수치 보존, 프리팹만 다시 연결).
- 씬에 `ArmySpawn` 3개: 플레이어 메타포스 (0, -1900) heading 90 / 타나르크 (-1500, 900) / 악마 (1500, 900). AI 둘이 서로 더 가까워 먼저 맞붙는다.
- 더미 적은 씬에 놓지 않는다(프리팹과 코드는 유지).
- `EditorBuildSettings.scenes` 에 Battle 씬 등록(R 재시작에 필요).
- 예전 프리팹(`Commander`, `CommanderMember`, `SquadLeader`, `SquadMember`)과 머티리얼(`Army_Red`, `Army_Blue`)은 삭제.

종족 초기 수치(첫 생성 시에만 적용):

| 종족 | 색 | hp | speed | tether | range | melee | ranged | 성향 |
|---|---|---|---|---|---|---|---|---|
| 메타포스 | (0.50, 0.45, 0.36) | 1.6 | 0.8 | 0.7 | 0.6 | 1.5 | 0.8 | — |
| 타나르크 | (0.20, 0.50, 0.95) | 0.8 | 1.1 | 1.25 | 1.2 | 0.7 | 1.4 | avoidMelee |
| 악마 | (0.75, 0.12, 0.10) | 1.0 | 1.25 | 1.0 | 0.7 | 1.6 | 0.6 | noRetreat |

### 확인 방법
컴파일(`refresh_unity` → `read_console` 오류 0) → 메뉴 `Babel/전장 빌드 ...` 실행 → 플레이 모드 진입 → `execute_code` 로 군단별 지휘관 체력·분대 상태를 읽어 싸움이 벌어지는지 확인 → 스크린샷 1장 → 플레이 모드 종료 → 커밋·푸시.

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
