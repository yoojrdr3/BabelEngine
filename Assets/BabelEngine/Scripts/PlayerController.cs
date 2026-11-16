using UnityEngine;
using UnityEngine.InputSystem;

namespace Babel
{
    // 플레이어 입력: 지휘관 이동, 분대 선택, 우클릭 지휘, 좌클릭 공격, 카메라 모드·줌.
    // BattleWorld 가 매 프레임 HandleInput → (군단 갱신) → TickCamera 순서로 부른다.
    public class PlayerController : MonoBehaviour
    {
        [Header("실행 중 상태")]
        [Tooltip("-1 = 아바타 모드, 0~3 = 분대 지휘 모드")] public int selected = -1;
        [Tooltip("카메라가 따라가는 분대(-1 = 지휘관)")] public int camFocus = -1;
        public Vector2 cursor;
        public Vector2 camTarget;
        public Vector2 camPos;
        public float zoom = 1.75f;
        public float zoomGoal = 1.75f;
        public float zoomAvatar, zoomSquadNear, zoomSquadFar;
        public bool squadFar;

        BattleWorld world;
        Army army;
        BattleCamera cam;
        BattleConfig C;
        float zoomEase;
        float lastRightT = -1e9f;
        Vector2 lastRightP;
        float lastXT = -1e9f;
        bool lastXForbade;

        public void Init(BattleWorld w, Army a, BattleCamera c)
        {
            world = w; army = a; cam = c; C = w.config;
            zoomAvatar = C.zoomAvatar;
            zoomSquadNear = C.zoomSquadNear;
            zoomSquadFar = C.zoomSquadFar;
            zoom = zoomGoal = zoomAvatar;
            zoomEase = C.modeZoomFollow;
            camPos = camTarget = a.Commander.pos;
            cam.Apply(camPos, zoom);
        }

        // ------------------------------------------------------------ 입력
        public void HandleInput()
        {
            Keyboard kb = Keyboard.current;
            Mouse ms = Mouse.current;
            if (kb == null || ms == null) return;

            army.moveInput = new Vector2(
                (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));

            // 커서는 지난 프레임에 그려진 카메라 기준으로 푼다(클릭과 같은 기준).
            Vector2 sp = ms.position.ReadValue();
            cursor = cam.ScreenToSim(sp);
            army.cursor = cursor;

            if (kb.xKey.wasPressedThisFrame) XPressed();
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SelectSquad(0);
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SelectSquad(1);
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SelectSquad(2);
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) SelectSquad(3);
            if (kb.spaceKey.wasPressedThisFrame) SpaceToAvatar();
            if (kb.escapeKey.wasPressedThisFrame) Deselect();

            if (ms.leftButton.wasPressedThisFrame) Fire();
            if (ms.rightButton.wasPressedThisFrame) RightClick(sp);

            float sc = ms.scroll.ReadValue().y;
            if (Mathf.Abs(sc) > 0.01f)
            {
                // 휠은 현재 모드의 줌 값을 바꾸고 그 값은 기억된다.
                Preset = Mathf.Clamp(Preset * Mathf.Exp(Mathf.Sign(sc) * 0.15f), C.zoomMin, C.zoomMax);
                zoomGoal = Preset;
                zoomEase = C.wheelZoomFollow;
            }

            if (selected < 0) army.Commander.facing = BattleMath.Angle(cursor - army.Commander.pos);
        }

        // 숫자 키: 선택만 한다. 같은 숫자를 한 번 더 누르면 카메라가 그 분대 쪽으로 간다.
        public void SelectSquad(int i)
        {
            if (i >= army.squads.Count) return;
            if (selected == i)
            {
                if (camFocus != i)
                {
                    camFocus = i;
                    UpdateZoomBand(true);
                    world.Toast($"카메라 → {i + 1}번 분대 쪽 ({Mathf.RoundToInt(C.camSquadBias * 100f)}% 지점)");
                }
                return;
            }
            selected = i;
            army.StopFollow();
            world.Toast($"{i + 1}번 분대 선택 — 한 번 더 누르면 카메라 이동");
        }

        // Space: 첫 번째는 아바타 선택만, 두 번째는 카메라도 지휘관으로.
        public void SpaceToAvatar()
        {
            if (selected >= 0)
            {
                selected = -1;
                army.StopFollow();
                world.Toast(camFocus >= 0 ? "아바타 선택 — 한 번 더 누르면 카메라 이동" : "아바타 모드");
            }
            else if (camFocus >= 0)
            {
                camFocus = -1;
                ApplyModeZoom();
                world.Toast("카메라 → 지휘관");
            }
        }

        // Esc: 카메라까지 한 번에 아바타 모드로.
        public void Deselect()
        {
            if (army.followAll) { army.StopFollow(); world.Toast("모든 분대: 커서 따라가기 해제"); }
            if (selected < 0 && camFocus < 0) return;
            bool wasFocused = camFocus >= 0;
            selected = -1;
            camFocus = -1;
            army.StopFollow();
            if (wasFocused) ApplyModeZoom();
            world.Toast("아바타 모드 — 카메라 → 지휘관");
        }

        // X: 전투 행동 금지. 금지 상태에서 0.3초 안에 두 번 누르면 해제.
        void XPressed()
        {
            float now = Time.unscaledTime;
            bool isDouble = now - lastXT <= C.doubleClickTime;
            if (!army.ceasefire)
            {
                army.ceasefire = true;
                lastXT = now; lastXForbade = true;
                world.Toast("전투 행동 금지 — X 더블 입력으로 해제");
            }
            else if (isDouble && !lastXForbade)
            {
                army.ceasefire = false;
                lastXT = -1e9f; lastXForbade = false;
                world.Toast("전투 행동 금지 해제");
            }
            else { lastXT = now; lastXForbade = false; }
        }

        // 좌클릭: 지휘관(아바타 모드) 또는 선택 분대(지휘 모드)가 커서 방향으로 1발.
        void Fire()
        {
            if (selected < 0)
            {
                Unit red = army.Commander;
                float a = BattleMath.Angle(cursor - red.pos);
                red.facing = a;
                world.SpawnProjectile(ShotKind.Bolt, army.team, red.pos, a);
            }
            else
            {
                Squad s = army.squads[selected];
                float a = BattleMath.Angle(cursor - s.leader.pos);
                s.leader.facing = a;
                s.leader.facingHold = 0.7f;
                s.attackedAt = world.time;
                world.SpawnProjectile(ShotKind.Skill, army.team, s.leader.pos, a);
            }
        }

        // 우클릭 1번: 비전투로 커서 추적 · 추적 중 한 번 더: 전투 허용 · 더블 클릭: 그 자리에 고정.
        void RightClick(Vector2 sp)
        {
            float now = Time.unscaledTime;
            bool isDouble = now - lastRightT <= C.doubleClickTime && (sp - lastRightP).magnitude <= C.doubleClickPixels;
            lastRightT = isDouble ? -1e9f : now;
            lastRightP = sp;

            if (isDouble && (army.followIdx >= 0 || army.followAll))
            {
                if (army.followAll)
                {
                    army.GroupOrder(cursor, true);
                    army.StopFollow();
                    world.Toast("모든 분대: 커서 따라가기 해제 — 이 위치에 고정");
                }
                else
                {
                    int idx = army.followIdx;
                    army.OrderMove(idx, cursor, true);
                    army.StopFollow();
                    world.Toast($"{idx + 1}번 분대: 커서 따라가기 해제 — 이 위치에 고정");
                }
                return;
            }

            if (selected < 0)
            {
                if (army.followAll)
                {
                    if (army.followPassive)
                    {
                        army.followPassive = false;
                        world.Toast("모든 분대: 전투 허용하며 커서 따라가기 (우클릭 더블 클릭으로 해제)");
                    }
                }
                else
                {
                    army.StartRally(cursor);
                    world.Toast("모든 분대: 비전투로 집결 → 커서 추적 — 한 번 더: 전투 허용, 더블 클릭: 해제");
                }
            }
            else
            {
                if (army.followIdx == selected)
                {
                    if (army.followPassive)
                    {
                        army.followPassive = false;
                        world.Toast($"{selected + 1}번 분대: 전투 허용하며 커서 따라가기 (우클릭 더블 클릭으로 해제)");
                    }
                }
                else
                {
                    bool clamped = army.OrderMove(selected, cursor, true);
                    army.followIdx = selected;
                    army.followPassive = true;
                    world.Toast(clamped
                        ? $"{selected + 1}번 분대: 명령 지점이 D 밖이라 최대 거리로 제한됨"
                        : $"{selected + 1}번 분대: 비전투로 커서 따라가기 — 한 번 더: 전투 허용, 더블 클릭: 해제");
                }
            }
        }

        // ------------------------------------------------------------ 카메라·줌
        // 줌 프리셋: 지휘관 1.75× / 분대(지휘관에서 D/2 이내) 1.0× / 분대(그보다 멀리) 0.6×
        float Preset
        {
            get => camFocus < 0 ? zoomAvatar : squadFar ? zoomSquadFar : zoomSquadNear;
            set { if (camFocus < 0) zoomAvatar = value; else if (squadFar) zoomSquadFar = value; else zoomSquadNear = value; }
        }

        void ApplyModeZoom() { zoomGoal = Preset; zoomEase = C.modeZoomFollow; }

        void UpdateZoomBand(bool force)
        {
            if (camFocus < 0) return;
            float d = army.squads[camFocus].dist, r = C.D / 2f, h = force ? 0f : C.zoomBandHysteresis;
            bool far = squadFar ? d > r - h : d > r + h;
            if (far != squadFar || force) { squadFar = far; ApplyModeZoom(); }
        }

        public void TickCamera(float dt)
        {
            Unit red = army.Commander;
            camTarget = camFocus >= 0
                ? red.pos + (army.squads[camFocus].leader.pos - red.pos) * C.camSquadBias
                : red.pos;
            camPos += (camTarget - camPos) * BattleMath.Damp(C.camFollow, dt);
            UpdateZoomBand(false);
            // 로그 공간에서 감쇠: 확대와 축소의 느낌이 같다.
            float lg = Mathf.Log(zoomGoal / zoom);
            zoom = Mathf.Abs(lg) < 1e-4f ? zoomGoal : zoom * Mathf.Exp(lg * BattleMath.Damp(zoomEase, dt));
            cam.Apply(camPos, zoom);
        }
    }
}
