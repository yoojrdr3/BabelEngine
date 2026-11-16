using System.Collections.Generic;
using UnityEngine;

namespace Babel
{
    // Hold: 지정 위치 유지 + 자동 사격 / Melee: 제한 원 안의 적에게 근접 / Retreat: 지휘관 쪽으로 퇴각 / Return: 지정 위치로 복귀
    public enum SquadState { Hold, Melee, Retreat, Return }

    // 분대장 1 + 분대원 N. 지휘관 분대도 같은 구조를 쓴다(상태 기계만 안 쓴다).
    public class Squad : MonoBehaviour
    {
        public int id;
        public Unit leader;
        public List<Unit> members = new List<Unit>();

        [Header("상태 (실행 중)")]
        public SquadState state;
        public float stateTime;
        [Tooltip("지휘관 기준 지정 위치")] public Vector2 offset;
        public Vector2 dir = Vector2.up;
        public float dist;
        [Tooltip("대형이 향하는 방향(라디안)")] public float heading;
        public Unit target;
        public Unit rangedTarget;
        [Tooltip("0 이하일 때만 근접 돌격. Infinity = 지정 위치 도착 전까지 금지")] public float engageLock;

        [HideInInspector] public float firedAt = -9f, attackedAt = -9f, hitAt = -9f;

        public void SetState(SquadState s)
        {
            if (state == s) return;
            state = s;
            stateTime = 0f;
        }
    }
}
