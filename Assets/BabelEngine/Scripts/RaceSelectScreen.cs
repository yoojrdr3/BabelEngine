using UnityEngine;
using UnityEngine.InputSystem;

namespace Babel
{
    // 시작 화면: 플레이할 종족을 고른다(임시 IMGUI). 카드를 누르거나 1~3 키.
    // 고르면 BattleWorld.StartBattle 이 그 종족을 플레이어 자리에, 나머지를 AI 자리에 세운다.
    public class RaceSelectScreen : MonoBehaviour
    {
        public BattleWorld world;

        GUIStyle title, nameStyle, body, hint, tagStyle;
        int hover = -1;

        void Update()
        {
            if (world == null || world.started || world.races == null) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Pick(0);
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Pick(1);
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Pick(2);
        }

        void Pick(int i)
        {
            if (i < 0 || i >= world.races.Length || world.races[i] == null) return;
            world.StartBattle(world.races[i]);
        }

        static string Mul(float v) => "×" + v.ToString("0.##");

        void OnGUI()
        {
            if (world == null || world.started || world.races == null || world.races.Length == 0) return;
            float k = Screen.height / 900f;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                body = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = true, wordWrap = true };
                hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            title.fontSize = Mathf.RoundToInt(34f * k);
            nameStyle.fontSize = Mathf.RoundToInt(28f * k);
            body.fontSize = Mathf.RoundToInt(15f * k);
            hint.fontSize = Mathf.RoundToInt(16f * k);
            tagStyle.fontSize = Mathf.RoundToInt(15f * k);

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 50f * k, Screen.width, 60f * k), "플레이할 종족을 고르세요", title);

            int n = world.races.Length;
            float cw = 340f * k, ch = 520f * k, gap = 30f * k;
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) / 2f, y0 = 150f * k;
            Vector2 mouse = Event.current.mousePosition;
            int newHover = -1;
            for (int i = 0; i < n; i++)
            {
                RaceDefinition r = world.races[i];
                if (r == null) continue;
                Rect card = new Rect(x0 + i * (cw + gap), y0, cw, ch);
                bool over = card.Contains(mouse);
                if (over) newHover = i;

                GUI.color = over ? new Color(0.20f, 0.20f, 0.24f, 0.98f) : new Color(0.12f, 0.12f, 0.15f, 0.96f);
                GUI.DrawTexture(card, Texture2D.whiteTexture);
                GUI.color = r.color;
                GUI.DrawTexture(new Rect(card.x, card.y, card.width, 8f * k), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(card.x + 40f * k, card.y + 40f * k, card.width - 80f * k, 96f * k), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(card.x, card.y + 150f * k, card.width, 44f * k), r.raceName, nameStyle);

                string traits = "";
                if (r.noRetreat) traits += "물면 안 놓는다 (퇴각 없음)\n";
                if (r.avoidMelee) traits += "근접을 피하고 사격만 한다\n";
                string text =
                    (string.IsNullOrEmpty(r.description) ? "" : r.description + "\n\n") +
                    $"체력 {Mul(r.hpMul)}   속도 {Mul(r.speedMul)}\n" +
                    $"분대 거리 D {Mul(r.tetherMul)}   사거리 {Mul(r.rangeMul)}\n" +
                    $"근접 피해 {Mul(r.meleeDamageMul)}   사격 피해 {Mul(r.rangedDamageMul)}\n" +
                    (traits.Length > 0 ? "\n<b>" + traits.TrimEnd('\n') + "</b>" : "");
                GUI.Label(new Rect(card.x + 22f * k, card.y + 205f * k, card.width - 44f * k, card.height - 270f * k), text, body);

                GUI.color = r.color;
                GUI.Label(new Rect(card.x, card.yMax - 52f * k, card.width, 36f * k), $"[{i + 1}] 선택", tagStyle);
                GUI.color = Color.white;

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && over)
                {
                    Event.current.Use();
                    Pick(i);
                    return;
                }
            }
            hover = newHover;
            GUI.Label(new Rect(0, y0 + ch + 20f * k, Screen.width, 30f * k), "카드를 누르거나 1·2·3 키 — 나머지 종족은 AI 가 맡습니다", hint);
        }
    }
}
