using UnityEngine;

namespace Babel
{
    // 맞기만 하는 표적. 씬에 배치된 위치가 기준점이고, 축을 따라 왕복한다.
    [RequireComponent(typeof(Unit))]
    public class DummyEnemy : MonoBehaviour
    {
        [Tooltip("왕복 방향 (x, y) = Unity 의 (X, Z)")] public Vector2 patrolAxis;
        [Tooltip("왕복 폭(웹 단위). 0 이면 제자리")] public float patrolAmp;
        public float patrolSpeed;
        public float phase;

        [HideInInspector] public Unit unit;
        [HideInInspector] public Vector2 basePos;
        [HideInInspector] public float respawn;
        [HideInInspector] public bool counting;
    }
}
