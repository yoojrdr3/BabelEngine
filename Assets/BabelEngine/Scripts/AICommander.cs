using UnityEngine;

namespace Babel
{
    // AI 지휘관: 플레이어 대신 군단에 입력을 넣는다.
    // 가장 가까운 적 지휘관에게 사거리의 일정 비율까지 접근하고, 대형을 적 쪽으로 돌리고, 사거리 안의 적을 쏜다.
    // 분대의 사격·근접은 Army 의 자동 전투가 처리한다.
    public class AICommander : MonoBehaviour
    {
        public Army army;
        [Tooltip("지금 노리는 적 지휘관")] public Unit foe;

        float fireCd, orderCd, strafeT, strafeSign = 1f;

        public void Tick(float dt)
        {
            BattleWorld w = army.world;
            BattleConfig C = army.config;
            Unit me = army.Commander;

            foe = w.NearestHostileCommander(army);
            if (foe == null) { army.moveInput = Vector2.zero; return; }

            Vector2 to = foe.pos - me.pos;
            float d = to.magnitude;
            Vector2 n = d > 1e-3f ? to / d : Vector2.up;
            float want = army.Range * C.aiPreferredRange;

            // 멀면 접근, 너무 가까우면 후퇴, 적당하면 옆으로 천천히 움직인다.
            strafeT -= dt;
            if (strafeT <= 0f) { strafeT = Random.Range(2f, 4f); strafeSign = -strafeSign; }
            if (d > want) army.moveInput = n;
            else if (d < want * 0.6f)
            {
                // 물러나되 벽에 몰리지 않도록 전장 중앙 쪽으로 휜다.
                Vector2 inward = -me.pos / C.worldHalf;
                army.moveInput = BattleMath.Norm(-n + inward * 1.5f, -n);
            }
            else army.moveInput = new Vector2(-n.y, n.x) * (strafeSign * 0.35f);

            // 기본 대형을 적 쪽으로 돌려 둔다(명령이 아니라 지정 위치만 바꾸므로 자동 전투를 끊지 않는다).
            orderCd -= dt;
            if (orderCd <= 0f)
            {
                orderCd = C.aiReorderInterval;
                float rot = BattleMath.Angle(n) - Mathf.PI / 2f;
                for (int i = 0; i < army.squads.Count && i < C.squadStart.Length; i++)
                    army.squads[i].offset = BattleMath.Rotate(C.squadStart[i], rot);
            }

            fireCd -= dt;
            float range = army.Range;
            // 사거리 안에 적 지휘관이 있으면 그쪽을 집중 사격, 없으면 가장 가까운 적.
            Unit t = (foe.pos - me.pos).magnitude <= range ? foe : w.NearestEnemy(army.team, me.pos, e => (e.pos - me.pos).magnitude <= range);
            if (t != null)
            {
                me.facing = BattleMath.Angle(t.pos - me.pos);
                if (fireCd <= 0f)
                {
                    fireCd = C.aiFireCooldown;
                    w.SpawnProjectile(ShotKind.Bolt, army, me.pos, me.facing);
                }
            }
            else me.facing = BattleMath.Angle(n);
        }
    }
}
