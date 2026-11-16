using System.Text;
using UnityEngine;

namespace Babel
{
    // 임시 HUD(IMGUI): 상태 요약, 분대 번호·상태, 적 체력 막대, 피해 숫자, 안내 문구.
    public class BattleHud : MonoBehaviour
    {
        public BattleWorld world;
        public bool showHelp = true;

        GUIStyle center, left;
        readonly StringBuilder sb = new StringBuilder();
        static readonly Color Blue = new Color(0.11f, 0.07f, 0.92f);

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

            // 분대 번호와 상태
            for (int i = 0; i < army.squads.Count; i++)
            {
                Squad s = army.squads[i];
                if (s.wiped) continue;
                Vector2 head = cam.SimToGui(s.leader.pos, C.U(s.leader.radius) * 4.8f);
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

            // 상태 요약
            Squad sel = pc.selected >= 0 ? army.squads[pc.selected] : null;
            sb.Length = 0;
            sb.Append("<b>바벨 엔진 · ").Append(army.race.raceName).Append("</b>\n");
            int foes = 0;
            foreach (Army a in world.armies) if (a != army && !a.defeated) foes++;
            sb.Append("지휘관 체력: ").Append(Mathf.Max(0f, army.Commander.hp).ToString("0")).Append(" / ").Append(army.Commander.maxHp.ToString("0")).Append(" · 남은 적 군단: ").Append(foes).Append('\n');
            sb.Append("조작 모드: <b>").Append(sel != null ? "분대 지휘" : "아바타").Append("</b>\n");
            sb.Append("선택된 분대: ").Append(sel != null ? sel.id + "번" : "—").Append('\n');
            sb.Append("거리 (지휘관 → 선택 분대): ").Append(sel != null ? sel.dist.ToString("0") + " u" : "—").Append('\n');
            sb.Append("최대 거리 D: ").Append(army.D.ToString("0")).Append(" u · 사거리: ").Append(army.Range.ToString("0")).Append(" u\n");
            sb.Append("선택 분대 상태: ").Append(sel != null ? army.StatusOf(pc.selected) : "—").Append('\n');
            sb.Append("카메라: ").Append(pc.camFocus >= 0 ? (pc.camFocus + 1) + "번 분대 쪽" : "지휘관").Append(" · 줌 ").Append(pc.zoom.ToString("0.00")).Append("×\n");
            if (army.ceasefire) sb.Append("<color=#ff5555><b>전투 행동 금지 (X X 로 해제)</b></color>\n");
            if (showHelp)
            {
                sb.Append("\nWASD 이동 · 좌클릭 발사 · 1–4 분대 선택(한 번 더: 카메라)\n");
                sb.Append("우클릭 지휘/커서 추적 · 한 번 더: 전투 허용 · 더블 클릭: 고정\n");
                sb.Append("Space/Esc 아바타 · 휠 줌 · X 전투 금지");
            }
            Rect box = new Rect(12f * k, 12f * k, 430f * k, (showHelp ? 270f : 195f) * k);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 10f * k, box.y + 8f * k, box.width - 20f * k, box.height - 16f * k), sb.ToString(), left);

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
                Rect r = new Rect((Screen.width - w) / 2f, Screen.height - 90f * k, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, world.toast, center);
            }
        }

        static void HpBar(Unit u, BattleCamera cam, BattleConfig C, float k, bool ally, float size, string label)
        {
            if (u.dead) return;
            Vector2 g = cam.SimToGui(u.pos, C.U(u.radius) * 2.2f);
            float w = 44f * k * size, h = 5f * k * (size > 1f ? 1.4f : 1f), y = g.y - 12f * k * (size > 1f ? 1.6f : 1f);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(g.x - w / 2f, y, w, h), Texture2D.whiteTexture);
            GUI.color = ally ? new Color(0.25f, 0.8f, 0.3f) : new Color(0.85f, 0.27f, 0.27f);
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
