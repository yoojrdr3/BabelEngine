using System;
using UnityEngine;

namespace Babel
{
    public enum ShotKind { Bolt = 0, Skill = 1, Auto = 2 }

    [Serializable]
    public class ShotDef
    {
        public float speed, radius, damage;
        public ShotDef() { }
        public ShotDef(float speed, float radius, float damage) { this.speed = speed; this.radius = radius; this.damage = damage; }
    }

    // 웹 프로토타입 game.js 의 CFG 에 해당. 값은 웹 단위(u) 그대로 두고 unitScale 로 미터 변환.
    // 좌표는 (x, y) = Unity 의 (X, Z). 웹과 달리 y 가 화면 위쪽이다.
    [CreateAssetMenu(menuName = "Babel/Battle Config")]
    public class BattleConfig : ScriptableObject
    {
        [Header("단위")]
        [Tooltip("웹 단위 1u = 몇 미터인지")] public float unitScale = 0.05f;
        public float worldHalf = 4000f;

        [Header("지휘관 (웹의 빨강 분대)")]
        public float commanderMaxSpeed = 260f;
        public float commanderAccel = 14f;
        public float commanderRadius = 18f;

        [Header("분대")]
        [Tooltip("지휘관↔분대 최대 거리. 제한 원의 지름이기도 하다")] public float D = 1440f;
        public float squadLeaderRadius = 16f;
        [Tooltip("지정 위치로 돌아가는 반응성 (1/s)")] public float steer = 3f;
        [Tooltip("분대가 혼자 움직일 때 최대 속도 (u/s)")] public float steerMax = 480f;
        public float returnTimeout = 6f;
        public float reengageDelay = 1f;
        public Vector2[] squadStart = { new Vector2(-340, 240), new Vector2(340, 240), new Vector2(-340, -260), new Vector2(340, -260) };

        [Header("분대원")]
        public float memberScale = 0.7f;
        [Tooltip("평시 대형 간격 배율")] public float memberSpacing = 3f;
        public float memberMaxSpeed = 760f;
        public float memberGain = 4.5f;
        public float memberAccel = 14f;
        public float memberFireCooldown = 1.3f;
        public float memberMeleeDamage = 1f;
        public float memberMeleeCooldown = 0.75f;
        [Tooltip("분대장 기준 V자 대형 (+x = 진행 방향)")]
        public Vector2[] formation = { new Vector2(-12, -40), new Vector2(-12, 40), new Vector2(-46, -18), new Vector2(-46, 18) };
        public float[] meleeRingDeg = { 72f, -72f, 144f, -144f };

        [Header("전투")]
        [Tooltip("원거리 사거리 = D × 이 값")] public float rangeMul = 1f;
        public ShotDef bolt = new ShotDef(950, 5, 1);
        public ShotDef skill = new ShotDef(720, 9, 3);
        public ShotDef auto = new ShotDef(900, 4, 1);
        public float autoCooldown = 0.9f;
        public float meleeReach = 44f;
        public float meleeDamage = 2f;
        public float meleeCooldown = 0.55f;
        public float meleeLeaveMargin = 16f;

        [Header("더미 적")]
        public float enemyRadius = 20f;
        public float enemyHp = 30f;
        public float enemyRespawn = 2.5f;

        [Header("집결 (아바타 모드 우클릭)")]
        public float rallySlot = 70f;
        public float rallyArrive = 40f;
        public float rallyTimeout = 8f;

        [Header("카메라")]
        public float camFollow = 5f;
        public float camPitch = 55f;
        [Tooltip("분대 카메라: 지휘관→분대장 선 위의 이 비율 지점")] public float camSquadBias = 0.75f;
        public float zoomAvatar = 1.75f;
        public float zoomSquadNear = 1f;
        public float zoomSquadFar = 0.6f;
        public float zoomBandHysteresis = 20f;
        public float modeZoomFollow = 5f;
        public float wheelZoomFollow = 14f;
        public float zoomMin = 0.3f;
        public float zoomMax = 2f;
        [Tooltip("줌 1.0 일 때 화면 세로 절반에 보이는 웹 단위")] public float refHalfHeight = 450f;

        [Header("입력")]
        public float doubleClickTime = 0.3f;
        public float doubleClickPixels = 8f;

        public float Range => D * rangeMul;
        public float U(float webUnits) => webUnits * unitScale;
        public ShotDef Shot(ShotKind k) => k == ShotKind.Bolt ? bolt : k == ShotKind.Skill ? skill : auto;
    }

    public static class BattleMath
    {
        public static float Damp(float k, float dt) => 1f - Mathf.Exp(-k * dt);
        public static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        public static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);
        public static Vector2 Norm(Vector2 v, Vector2 fallback) { float l = v.magnitude; return l > 1e-6f ? v / l : fallback; }
        public static float LerpAngle(float a, float b, float t) => a + Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg) * Mathf.Deg2Rad * t;

        // 점 p 에서 선분 a→b 까지의 거리 (투사체 관통 판정).
        public static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float l2 = d.sqrMagnitude;
            float t = l2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / l2) : 0f;
            return (p - (a + d * t)).magnitude;
        }
    }
}
