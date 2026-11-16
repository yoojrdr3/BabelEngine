using UnityEngine;

namespace Babel
{
    // 전장의 모든 개체(지휘관, 분대장, 분대원, 적)의 공통 몸체.
    // 위치·속도는 웹 단위로 들고 있고, SyncView 가 Unity 좌표로 옮긴다.
    public class Unit : MonoBehaviour
    {
        [Header("설정 (프리팹)")]
        public float radius = 18f;
        public float maxHp = 30f;
        [Tooltip("근접 공격 때 앞으로 튀는 거리")] public float lungeDistance = 6f;
        public Transform visual;
        public Renderer bodyRenderer;

        [Header("상태 (실행 중)")]
        public int team;
        public float hp;
        public bool dead;
        public Vector2 pos;
        public Vector2 vel;
        public float facing;

        [HideInInspector] public float facingHold, lunge, flash, fireCd, meleeCd;
        [HideInInspector] public bool isMember;
        [HideInInspector] public Squad squad;
        [HideInInspector] public Army army;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpb;
        Color baseColor = Color.white;
        bool flashed;

        public void Setup(int team, Vector2 p, float facing)
        {
            this.team = team;
            pos = p;
            vel = Vector2.zero;
            this.facing = facing;
            hp = maxHp;
            dead = false;
            if (bodyRenderer != null && bodyRenderer.sharedMaterial != null && bodyRenderer.sharedMaterial.HasProperty(BaseColorId))
                baseColor = bodyRenderer.sharedMaterial.GetColor(BaseColorId);
        }

        public void SyncView(float scale)
        {
            Vector2 d = BattleMath.Dir(facing);
            Vector2 p = pos + d * (lunge * lungeDistance);
            transform.SetPositionAndRotation(
                new Vector3(p.x * scale, 0f, p.y * scale),
                Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up));

            if (visual != null && visual.gameObject.activeSelf == dead) visual.gameObject.SetActive(!dead);

            bool f = flash > 0.4f;
            if (f != flashed && bodyRenderer != null)
            {
                flashed = f;
                if (mpb == null) mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, f ? Color.white : baseColor);
                bodyRenderer.SetPropertyBlock(mpb);
            }
        }
    }
}
