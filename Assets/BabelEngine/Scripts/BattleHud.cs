using UnityEngine;
using UnityEngine.InputSystem;

namespace Babel
{
    // HUD(IMGUI): 왼쪽 위 지휘관 패널, 아래 분대 카드 4장, 유닛 위 체력 막대·번호·피해 숫자, 안내 문구, 전투 결과.
    // H 키로 조작 안내를 켜고 끈다.
    public class BattleHud : MonoBehaviour
    {
        public BattleWorld world;
        public bool showHelp = true;

        GUIStyle center, left;
        static readonly Color Ally = new Color(0.25f, 0.8f, 0.3f);
        static readonly Color Foe = new Color(0.85f, 0.27f, 0.27f);
        static readonly Color Panel = new Color(0.07f, 0.08f, 0.1f, 0.78f);

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (world == null || world.playerArmy == null || world.battleCamera == null) return;
            Army army = world.playerArmy;
            PlayerController pc = world.player;
            BattleConfig C = world.config;
            BattleCamera cam = world.battleCamera;
            float k = Screen.height / 900f;
            if (center == null)
            {
                center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                left = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = true };
            }
            center.fontSize = Mathf.RoundToInt(13f * k);
            left.fontSize = Mathf.RoundToInt(13f * k);

            // 체력 막대: 모든 군단의 지휘관·분대장 (아군 초록, 적 빨강, 지휘관은 더 크게 + 종족 이름), 더미 적
            foreach (Army a in world.armies)
            {
                if (a.defeated) continue;
                bool ally = a.team == army.team;
                HpBar(a.Commander, cam, C, k, ally, 2f, a.race.raceName);
                foreach (Squad s in a.squads)
                    if (!s.wiped) HpBar(s.leader, cam, C, k, ally, 1f, null);
            }
            foreach (Unit u in world.units)
                if (u.army == null && u.team != army.team) HpBar(u, cam, C, k, false, 1f, null);
            GUI.color = Color.white;

            // 분대 번호와 상태 (유닛 위)
            for (int i = 0; i < army.squads.Count; i++)
            {
                Squad s = army.squads[i];
                if (s.wiped) continue;
                Vector2 head = cam.SimToGui(s.leader.pos, C.U(s.leader.radius) * 7.6f);
                Text(head, (i + 1).ToString(), i == pc.selected ? Color.yellow : Color.white, 1.4f * k);
                string st = army.StatusOf(i);
                if (st != "대기") Text(cam.SimToGui(s.leader.pos, 0f) + new Vector2(0f, 14f * k), st, Color.white, k);
            }

            // 피해 숫자
            foreach (BattleWorld.FloatText f in world.texts)
            {
                float t = f.t / f.dur;
                Vector2 g = cam.SimToGui(f.pos, 2.5f) - new Vector2(0f, 28f * k * t);
                Color c = f.color; c.a = 1f - t;
                Text(g, f.text, c, 1.15f * k);
            }

            DrawCommanderPanel(army, pc, k);
            DrawSquadCards(army, pc, k);

            // 전투 결과
            if (!string.IsNullOrEmpty(world.outcome))
            {
                bool win = world.outcome == "승리";
                Text(new Vector2(Screen.width / 2f, Screen.height * 0.4f), world.outcome, win ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.3f, 0.3f), 5f * k);
                Text(new Vector2(Screen.width / 2f, Screen.height * 0.4f + 60f * k), "R: 다시 시작", Color.white, 1.6f * k);
            }

            // 안내 문구
            if (!string.IsNullOrEmpty(world.toast) && Time.unscaledTime < world.toastUntil)
            {
                float w = 760f * k, h = 34f * k;
                Rect r = new Rect((Screen.width - w) / 2f, Screen.height - 190f * k, w, h);
                Fill(r, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(r, world.toast, center);
            }
        }

        // 왼쪽 위: 종족 · 지휘관 체력 · 남은 적 군단 · 모드
        void DrawCommanderPanel(Army army, PlayerController pc, float k)
        {
            Unit cmd = army.Commander;
            int foes = 0;
            foreach (Army a in world.armies) if (a != army && !a.defeated) foes++;
            Squad sel = pc.selected >= 0 ? army.squads[pc.selected] : null;

            float x = 14f * k, y = 14f * k, w = 330f * k;
            float h = (showHelp ? 150f : 104f) * k;
            Fill(new Rect(x, y, w, h), Panel);
            Fill(new Rect(x, y, 5f * k, h), army.race.color);

            GUI.color = army.race.color;
            left.fontStyle = FontStyle.Bold;
            left.fontSize = Mathf.RoundToInt(18f * k);
            GUI.Label(new Rect(x + 16f * k, y + 6f * k, w - 24f * k, 26f * k), army.race.raceName + " 군단", left);
            left.fontStyle = FontStyle.Normal;
            left.fontSize = Mathf.RoundToInt(13f * k);
            GUI.color = Color.white;

            float bx = x + 16f * k, by = y + 38f * k, bw = w - 32f * k, bh = 14f * k;
            Fill(new Rect(bx, by, bw, bh), new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(bx, by, bw * Mathf.Clamp01(cmd.hp / cmd.maxHp), bh), Ally);
            GUI.Label(new Rect(bx, by - 2f * k, bw, bh + 4f * k), $"지휘관 {Mathf.Max(0f, cmd.hp):0} / {cmd.maxHp:0}", center);

            string mode = sel != null ? $"분대 지휘 — {sel.id}번" : "아바타";
            string line = $"조작: <b>{mode}</b>   남은 적 군단: <b>{foes}</b>\n" +
                          $"사거리 {army.Range:0} · 분대 거리 D {army.D:0} · 줌 {pc.zoom:0.00}×";
            if (army.ceasefire) line += "\n<color=#ff5555><b>전투 행동 금지 (X X 로 해제)</b></color>";
            GUI.Label(new Rect(bx, by + 22f * k, bw, 54f * k), line, left);

            if (showHelp)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.75f);
                GUI.Label(new Rect(bx, y + h - 38f * k, bw, 36f * k),
                    "WASD 이동 · 클릭 발사 · 1–4 분대 · 우클릭 지휘 · X 전투 금지 · H 안내", left);
                GUI.color = Color.white;
            }
        }

        // 아래 가운데: 분대 카드 4장 (번호, 상태, 분대장 체력, 분대원 생존 점)
        void DrawSquadCards(Army army, PlayerController pc, float k)
        {
            int n = army.squads.Count;
            float cw = 168f * k, ch = 82f * k, gap = 10f * k;
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) / 2f, y0 = Screen.height - ch - 16f * k;
            for (int i = 0; i < n; i++)
            {
                Squad s = army.squads[i];
                Rect r = new Rect(x0 + i * (cw + gap), y0, cw, ch);
                bool isSel = i == pc.selected;
                Fill(r, isSel ? new Color(0.16f, 0.15f, 0.06f, 0.88f) : Panel);
                Fill(new Rect(r.x, r.y, r.width, 4f * k), isSel ? Color.yellow : army.race.color);

                GUI.color = isSel ? Color.yellow : Color.white;
                left.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(r.x + 10f * k, r.y + 8f * k, 40f * k, 24f * k), (i + 1).ToString(), left);
                left.fontStyle = FontStyle.Normal;
                GUI.color = s.wiped ? new Color(1f, 0.6f, 0.4f) : Color.white;
                GUI.Label(new Rect(r.x + 34f * k, r.y + 8f * k, r.width - 40f * k, 24f * k), army.StatusOf(i), left);
                GUI.color = Color.white;

                float bx = r.x + 10f * k, bw = r.width - 20f * k;
                float frac = s.wiped ? 0f : Mathf.Clamp01(s.leader.hp / s.leader.maxHp);
                Fill(new Rect(bx, r.y + 36f * k, bw, 9f * k), new Color(0f, 0f, 0f, 0.55f));
                Fill(new Rect(bx, r.y + 36f * k, bw * frac, 9f * k), Ally);

                for (int m = 0; m < s.members.Count; m++)
                {
                    bool alive = !s.wiped && !s.members[m].dead;
                    Fill(new Rect(bx + m * 20f * k, r.y + 52f * k, 14f * k, 14f * k), alive ? army.race.color : new Color(0.25f, 0.25f, 0.28f, 0.8f));
                }
                if (isSel && !s.wiped)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.8f);
                    GUI.Label(new Rect(bx + 4 * 20f * k + 4f * k, r.y + 50f * k, bw, 20f * k), $"거리 {s.dist:0}", left);
                    GUI.color = Color.white;
                }
            }
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void HpBar(Unit u, BattleCamera cam, BattleConfig C, float k, bool ally, float size, string label)
        {
            if (u.dead) return;
            Vector2 g = cam.SimToGui(u.pos, C.U(u.radius) * 7.0f);
            float w = 44f * k * size, h = 5f * k * (size > 1f ? 1.4f : 1f), y = g.y - 8f * k * (size > 1f ? 1.6f : 1f);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(g.x - w / 2f, y, w, h), Texture2D.whiteTexture);
            GUI.color = ally ? Ally : Foe;
            GUI.DrawTexture(new Rect(g.x - w / 2f, y, w * Mathf.Clamp01(u.hp / u.maxHp), h), Texture2D.whiteTexture);
            if (label != null)
            {
                var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(12f * k) };
                GUI.color = Color.white;
                GUI.Label(new Rect(g.x - 80f, y - 18f * k, 160f, 18f * k), label, st);
            }
        }

        // 가운데 정렬 글자(검은 그림자 포함)
        void Text(Vector2 at, string text, Color color, float sizeK)
        {
            int keep = center.fontSize;
            center.fontSize = Mathf.RoundToInt(13f * sizeK);
            Rect r = new Rect(at.x - 150f, at.y - 14f, 300f, 28f);
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, center);
            GUI.color = color;
            GUI.Label(r, text, center);
            GUI.color = Color.white;
            center.fontSize = keep;
        }
    }
}
