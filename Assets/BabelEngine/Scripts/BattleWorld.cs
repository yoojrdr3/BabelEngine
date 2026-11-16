using System.Collections.Generic;
using UnityEngine;

namespace Babel
{
    // 전장 전체를 한 곳에서 순서대로 굴린다: 입력 → 군단 → 겹침 해소 → 카메라 → 적 → 투사체 → 화면 반영.
    public class BattleWorld : MonoBehaviour
    {
        public BattleConfig config;
        public ArmyPrefabs playerPrefabs;
        public GameObject boltPrefab, skillPrefab, autoPrefab;
        public PlayerController player;
        public BattleCamera battleCamera;

        [Header("실행 중")]
        public float time;
        public Army playerArmy;

        public readonly List<Unit> units = new List<Unit>();
        public readonly List<DummyEnemy> dummies = new List<DummyEnemy>();
        public readonly List<FloatText> texts = new List<FloatText>();
        public readonly List<Ping> pings = new List<Ping>();
        [HideInInspector] public string toast;
        [HideInInspector] public float toastUntil;

        public class FloatText { public Vector2 pos; public string text; public Color color; public float t, dur; }
        public class Ping { public Vector2 pos; public bool clamped; public float t, dur; }

        class Projectile
        {
            public ShotKind kind; public int team; public Vector2 pos, vel; public float radius, damage, left;
            public GameObject view;
        }
        readonly List<Projectile> projectiles = new List<Projectile>();
        readonly Stack<GameObject>[] pool = { new Stack<GameObject>(), new Stack<GameObject>(), new Stack<GameObject>() };
        Transform projectileRoot;

        static readonly Color[] ShotColor =
        {
            new Color(1f, 0.42f, 0f), new Color(0f, 0.66f, 1f), new Color(0.36f, 0.49f, 1f),
        };

        void Awake()
        {
            projectileRoot = new GameObject("투사체").transform;
            projectileRoot.SetParent(transform, false);

            float s = config.unitScale;
            foreach (DummyEnemy d in FindObjectsByType<DummyEnemy>(FindObjectsSortMode.None))
            {
                d.unit = d.GetComponent<Unit>();
                d.basePos = new Vector2(d.transform.position.x, d.transform.position.z) / s;
                d.unit.Setup(-1, d.basePos, 0f);
                dummies.Add(d);
                Register(d.unit);
            }

            var go = new GameObject("플레이어 군단");
            playerArmy = go.AddComponent<Army>();
            playerArmy.Build(this, 0, playerPrefabs, Vector2.zero);
            player.Init(this, playerArmy, battleCamera);
            foreach (Unit u in units) u.SyncView(s);
        }

        void Update()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            time += dt;
            player.HandleInput();
            playerArmy.Tick(dt);
            SeparateMembers();
            player.TickCamera(dt);
            TickDummies(dt);
            TickProjectiles(dt);
            for (int i = texts.Count - 1; i >= 0; i--) { texts[i].t += dt; if (texts[i].t >= texts[i].dur) texts.RemoveAt(i); }
            for (int i = pings.Count - 1; i >= 0; i--) { pings[i].t += dt; if (pings[i].t >= pings[i].dur) pings.RemoveAt(i); }
            float s = config.unitScale;
            foreach (Unit u in units) u.SyncView(s);
        }

        public void Register(Unit u) { units.Add(u); }

        public void Toast(string msg) { toast = msg; toastUntil = Time.unscaledTime + 1.6f; }
        public void AddPing(Vector2 p, bool clamped) { pings.Add(new Ping { pos = p, clamped = clamped, dur = clamped ? 1.3f : 0.7f }); }

        public Unit NearestEnemy(int team, Vector2 p, System.Func<Unit, bool> accept)
        {
            Unit best = null;
            float bd = float.PositiveInfinity;
            foreach (Unit u in units)
            {
                if (u.dead || u.team == team || !accept(u)) continue;
                float d = (u.pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        public void Damage(Unit e, float dmg, Color color)
        {
            if (e.dead) return;
            e.hp -= dmg;
            e.flash = 1f;
            texts.Add(new FloatText { pos = e.pos + new Vector2((Random.value - 0.5f) * 16f, 0f), text = "-" + dmg.ToString("0.#"), color = color, dur = 0.8f });
            if (e.hp <= 0f) e.dead = true;
        }

        // 겹침 해소: 분대원끼리, 그리고 분대원이 분대장·살아 있는 적과 겹치지 않게 밀어낸다.
        readonly List<Unit> tmpMembers = new List<Unit>();
        readonly List<Unit> tmpSolids = new List<Unit>();
        void SeparateMembers()
        {
            tmpMembers.Clear();
            tmpSolids.Clear();
            foreach (Unit u in units)
            {
                if (u.dead) continue;
                if (u.isMember) tmpMembers.Add(u); else tmpSolids.Add(u);
            }
            for (int i = 0; i < tmpMembers.Count; i++)
            {
                Unit a = tmpMembers[i];
                for (int j = i + 1; j < tmpMembers.Count; j++)
                {
                    Unit b = tmpMembers[j];
                    Vector2 dv = b.pos - a.pos;
                    float d = dv.magnitude, min = a.radius + b.radius + 2f;
                    if (d >= min) continue;
                    Vector2 n = d > 1e-6f ? dv / d : Vector2.right;
                    float push = (min - d) / 2f;
                    a.pos -= n * push;
                    b.pos += n * push;
                }
            }
            foreach (Unit m in tmpMembers)
            {
                foreach (Unit o in tmpSolids)
                {
                    Vector2 dv = m.pos - o.pos;
                    float d = dv.magnitude, min = m.radius + o.radius + 2f;
                    if (d >= min) continue;
                    Vector2 n = d > 1e-6f ? dv / d : Vector2.up;
                    m.pos = o.pos + n * min;
                }
            }
        }

        void TickDummies(float dt)
        {
            foreach (DummyEnemy d in dummies)
            {
                Unit u = d.unit;
                if (u.dead)
                {
                    if (!d.counting) { d.counting = true; d.respawn = config.enemyRespawn; }
                    d.respawn -= dt;
                    if (d.respawn <= 0f) { d.counting = false; u.dead = false; u.hp = u.maxHp; u.flash = 0f; }
                    continue;
                }
                if (d.patrolAmp > 0f)
                    u.pos = d.basePos + d.patrolAxis * (Mathf.Sin(time * d.patrolSpeed + d.phase) * d.patrolAmp);
                u.flash = Mathf.Max(0f, u.flash - dt * 6f);
            }
        }

        // ------------------------------------------------------------ 투사체
        // 모든 원거리 공격은 같은 사거리를 쓴다. 쏜 위치에서 정확히 그 거리까지만 난다.
        public void SpawnProjectile(ShotKind kind, int team, Vector2 from, float angle)
        {
            ShotDef P = config.Shot(kind);
            Vector2 d = BattleMath.Dir(angle);
            var p = new Projectile
            {
                kind = kind, team = team, pos = from + d * 20f, vel = d * P.speed,
                radius = P.radius, damage = P.damage, left = Mathf.Max(1f, config.Range - 20f),
            };
            var stack = pool[(int)kind];
            GameObject prefab = kind == ShotKind.Bolt ? boltPrefab : kind == ShotKind.Skill ? skillPrefab : autoPrefab;
            p.view = stack.Count > 0 ? stack.Pop() : Instantiate(prefab, projectileRoot);
            p.view.transform.position = ViewPos(p.pos);
            p.view.SetActive(true);
            var trail = p.view.GetComponent<TrailRenderer>();
            if (trail != null) trail.Clear();
            projectiles.Add(p);
        }

        Vector3 ViewPos(Vector2 p) => new Vector3(p.x * config.unitScale, 1.5f, p.y * config.unitScale);

        void TickProjectiles(float dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                Projectile p = projectiles[i];
                Vector2 a = p.pos;
                float sp = p.vel.magnitude, step = Mathf.Min(sp * dt, p.left);
                p.pos += p.vel / sp * step;
                p.left -= step;

                Unit hit = null;
                foreach (Unit u in units)
                {
                    if (u.dead || u.team == p.team) continue;
                    if (BattleMath.SegDist(u.pos, a, p.pos) < u.radius + p.radius) { hit = u; break; }
                }
                if (hit != null) Damage(hit, p.damage, ShotColor[(int)p.kind]);
                if (hit != null || p.left <= 0f)
                {
                    p.view.SetActive(false);
                    pool[(int)p.kind].Push(p.view);
                    projectiles.RemoveAt(i);
                    continue;
                }
                p.view.transform.position = ViewPos(p.pos);
            }
        }
    }
}
