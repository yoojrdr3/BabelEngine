using UnityEngine;

namespace Babel
{
    // 종족 하나의 정의. 기준 수치는 BattleConfig 에 있고, 여기서는 배율과 전투 성향만 정한다.
    [CreateAssetMenu(menuName = "Babel/Race Definition")]
    public class RaceDefinition : ScriptableObject
    {
        public string raceName = "종족";
        public Color color = Color.white;
        [TextArea(2, 4)] public string description;

        [Header("프리팹")]
        public Unit commanderPrefab;
        public Unit leaderPrefab;
        public Unit memberPrefab;

        [Header("배율 (기준값 × 이 값)")]
        public float hpMul = 1f;
        public float speedMul = 1f;
        [Tooltip("지휘관↔분대 최대 거리 D")] public float tetherMul = 1f;
        [Tooltip("원거리 사거리")] public float rangeMul = 1f;
        public float meleeDamageMul = 1f;
        public float rangedDamageMul = 1f;

        [Header("전투 성향")]
        [Tooltip("근접 중 적이 제한 원을 벗어나도 물러나지 않고 계속 쫓는다")] public bool noRetreat;
        [Tooltip("근접 돌격을 하지 않고 사격만 한다")] public bool avoidMelee;
    }
}
