using UnityEngine;
using UnityEngine.Rendering;

namespace Babel
{
    // 바닥에 그리는 선 표시: 사거리, 제한 원(지름 D), 지정 위치, 선택 고리, 조준선, 카메라 목표, 전투 금지 X.
    public class BattleOverlay : MonoBehaviour
    {
        public BattleWorld world;
        public Material lineMaterial;

        const float Y = 0.05f;
        static readonly Color Red = new Color(0.91f, 0.09f, 0.09f);
        static readonly Color Blue = new Color(0.11f, 0.07f, 0.92f);
        static readonly Color Skill = new Color(0f, 0.66f, 1f);
        static readonly Color Green = new Color(0.12f, 0.68f, 0.13f);

        LineRenderer rangeRing, limitRing, selectRing, previewRing, aimLine, camDot, xA, xB;
        LineRenderer[] destRing, destLine, meleeLine, pingRing;
        float pixel, scale;

        bool ready;

        void Init()
        {
            ready = true;
            int n = world.playerArmy.squads.Count;
            rangeRing = Make("사거리", true);
            limitRing = Make("제한 원 (지름 D)", true);
            selectRing = Make("선택 고리", true);
            previewRing = Make("명령 미리보기", true);
            aimLine = Make("조준선", false);
            camDot = Make("카메라 목표", true);
            xA = Make("전투 금지 X-1", false);
            xB = Make("전투 금지 X-2", false);
            destRing = new LineRenderer[n]; destLine = new LineRenderer[n]; meleeLine = new LineRenderer[n];
            for (int i = 0; i < n; i++)
            {
                destRing[i] = Make($"지정 위치 {i + 1}", true);
                destLine[i] = Make($"지정 위치 선 {i + 1}", false);
                meleeLine[i] = Make($"근접 연결선 {i + 1}", false);
            }
            pingRing = new LineRenderer[4];
            for (int i = 0; i < pingRing.Length; i++) pingRing[i] = Make($"명령 표시 {i + 1}", true);
        }

        LineRenderer Make(string lineName, bool loop)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        Vector3 W(Vector2 p) => new Vector3(p.x * scale, Y, p.y * scale);

        void Circle(LineRenderer lr, Vector2 c, float r, Color col, float px)
        {
            int n = r > 300f ? 128 : 40;
            if (lr.positionCount != n) lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                lr.SetPosition(i, W(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
            }
            Style(lr, col, px);
        }

        void Line(LineRenderer lr, Vector2 a, Vector2 b, Color col, float px)
        {
            if (lr.positionCount != 2) lr.positionCount = 2;
            lr.SetPosition(0, W(a));
            lr.SetPosition(1, W(b));
            Style(lr, col, px);
        }

        void Style(LineRenderer lr, Color col, float px)
        {
            lr.startColor = lr.endColor = col;
            lr.widthMultiplier = px * pixel;
            lr.enabled = true;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void LateUpdate()
        {
            if (world.playerArmy == null) return;
            if (!ready) Init();
            Army army = world.playerArmy;
            PlayerController pc = world.player;
            BattleConfig C = world.config;
            Camera cam = world.battleCamera.Cam;
            scale = C.unitScale;
            pixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height); // 화면 1픽셀의 월드 길이
            Unit red = army.Commander;
            int sel = pc.selected;
            Unit src = sel < 0 || army.squads[sel].wiped ? red : army.squads[sel].leader;
            bool selOk = sel >= 0 && !army.squads[sel].wiped;

            // 사거리: 지금 좌클릭으로 쏘는 쪽
            Circle(rangeRing, src.pos, army.Range, !selOk ? A(Red, 0.45f) : A(Blue, 0.5f), 1.5f);
            Line(aimLine, src.pos, pc.cursor, !selOk ? A(Red, 0.4f) : A(Skill, 0.75f), 1.5f);

            if (selOk)
            {
                Squad s = army.squads[sel];
                Circle(limitRing, army.LimitCenter(s), army.D / 2f, new Color(0.07f, 0.07f, 0.07f, 0.9f), 1.8f);
                Circle(selectRing, s.leader.pos, 25f + Mathf.Sin(world.time * 6f) * 2f, Blue, 2.5f);
                Vector2 o = pc.cursor - red.pos;
                if (o.magnitude > army.D) o *= army.D / o.magnitude;
                Circle(previewRing, red.pos + o, 16f, A(Blue, 0.35f), 1.2f);
            }
            else limitRing.enabled = selectRing.enabled = previewRing.enabled = false;

            for (int i = 0; i < army.squads.Count; i++)
            {
                Squad s = army.squads[i];
                if (s.wiped) { destRing[i].enabled = destLine[i].enabled = meleeLine[i].enabled = false; continue; }
                Vector2 dest = red.pos + s.offset;
                float gap = (dest - s.leader.pos).magnitude;
                bool isSel = i == sel;
                if (!isSel && gap < 6f) destRing[i].enabled = destLine[i].enabled = false;
                else
                {
                    Color c = A(Blue, isSel ? 0.85f : 0.3f);
                    Circle(destRing[i], dest, 22f, c, 1.5f);
                    if (gap >= 6f) Line(destLine[i], s.leader.pos, dest, c, 1.2f); else destLine[i].enabled = false;
                }
                if (s.state == SquadState.Melee && s.target != null) Line(meleeLine[i], s.leader.pos, s.target.pos, A(Blue, 0.5f), 1.5f);
                else meleeLine[i].enabled = false;
            }

            // 카메라 목표(초록) / 전투 금지(빨강 X)
            Circle(camDot, pc.camTarget, 6f, Green, 3f);
            if (army.ceasefire)
            {
                Vector2 t = pc.camTarget;
                Line(xA, t + new Vector2(-25, -25), t + new Vector2(25, 25), Red, 6f);
                Line(xB, t + new Vector2(25, -25), t + new Vector2(-25, 25), Red, 6f);
            }
            else xA.enabled = xB.enabled = false;

            for (int i = 0; i < pingRing.Length; i++)
            {
                if (i >= world.pings.Count) { pingRing[i].enabled = false; continue; }
                BattleWorld.Ping p = world.pings[world.pings.Count - 1 - i];
                float k = p.t / p.dur;
                Circle(pingRing[i], p.pos, 8f + 26f * (1f - k), A(p.clamped ? Red : Blue, 1f - k), 2f);
            }
        }
    }
}
