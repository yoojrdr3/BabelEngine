using UnityEngine;

namespace Babel
{
    // 씬에 놓는 군단 배치점. 이 오브젝트의 위치에 해당 종족의 군단이 생긴다.
    // 오브젝트를 끄면 그 군단은 전투에 참가하지 않는다.
    public class ArmySpawn : MonoBehaviour
    {
        public RaceDefinition race;
        [Tooltip("팀 번호. 번호가 다르면 서로 적")] public int team;
        [Tooltip("플레이어가 조작하는 군단 (하나만)")] public bool playerControlled;
        [Tooltip("시작할 때 바라보는 방향(도). 0 = +X, 90 = +Z")] public float headingDeg = 90f;

        void OnDrawGizmos()
        {
            Gizmos.color = race != null ? race.color : Color.white;
            Gizmos.DrawWireSphere(transform.position, 4f);
            float a = headingDeg * Mathf.Deg2Rad;
            Gizmos.DrawLine(transform.position, transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 10f);
        }
    }
}
