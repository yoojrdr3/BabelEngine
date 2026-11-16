using System.Collections.Generic;
using UnityEngine;

namespace Babel
{
    // 전장 전체를 한 곳에서 순서대로 굴린다: 입력 → 군단 → 겹침 해소 → 카메라 → 적 → 투사체 → 화면 반영.
    public class BattleWorld : MonoBehaviour
    {
        public BattleConfig config;
        public GameObject boltPrefab, skillPrefab, autoPrefab;
        public PlayerController player;
        [Tooltip("종족 선택 화면에 나오는 종족들")] public RaceDefinition[] races;
        public BattleCamera battleCamera;

        [Header("실행 중")]
        public float time;
        public Army playerArmy;
        [Tooltip("종족을 골라 전투가 시작되었는가")] public bool started;
        public readonly List<Army> armies = new List<Army>();
        [Tooltip("전투 결과: 비어 있으면 진행 중, \"승리\" 또는 \"패배\"")] public string outcome;

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
            public ShotKind kind; public Army army; public int team; public Vector2 pos, vel; public float radius, damage, left;
            public GameObject view;
        }
        readonly List<Projectile> projectiles = new List<Projectile>();
        readonly Stack<GameObject>[] pool = { new Stack<GameObject>(), new Stack<GameObject>(), new Stack<GameObject>() };
        Transform projectileRoot;
        readonly List<ArmySpawn> slots = new List<ArmySpawn>();

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock shotBlock;

        void Awake()
        {
            shotBlock = new MaterialPropertyBlock();
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

            // 군단은 종족을 고른 뒤(StartBattle) 만든다. 씬의 ArmySpawn 은 자리(위치·방향·팀)만 정한다.
            slots.AddRange(FindObjectsByType<ArmySpawn>(FindObjectsSortMode.None));
            slots.Sort((a, b) => a.team != b.team ? a.team.CompareTo(b.team) : string.CompareOrdinal(a.name, b.name));
            foreach (Unit u in units) u.SyncView(s);
        }

        // 플레이어가 고른 종족은 플레이어 자리에, 나머지 종족은 AI 자리에 차례로 들어간다.
        public void StartBattle(RaceDefinition choice)
        {
            if (started) return;
            var rest = new List<RaceDefinition>();
            foreach (RaceDefinition r in races) if (r != null && r != choice) rest.Add(r);
            float s = config.unitScale;
            foreach (ArmySpawn sp in slots)
            {
                bool isPlayer = sp.playerControlled && playerArmy == null;
                RaceDefinition race = isPlayer ? choice : rest.Count > 0 ? rest[0] : sp.race;
                if (!isPlayer && rest.Count > 0) rest.RemoveAt(0);
                if (race == null) continue;
                var go = new GameObject($"군단 · {race.raceName}{(isPlayer ? " (플레이어)" : "")}");
                go.transform.SetParent(transform, false);
                var army = go.AddComponent<Army>();
                Vector3 wp = sp.transform.position;
                army.Build(this, race, sp.team, isPlayer, new Vector2(wp.x, wp.z) / s, sp.headingDeg * Mathf.Deg2Rad);
                armies.Add(army);
                if (isPlayer) playerArmy = army;
                else
                {
                    army.ai = go.AddComponent<AICommander>();
                    army.ai.army = army;
                }
            }
            if (playerArmy == null) { Debug.LogError("[Babel] 플레이어 ArmySpawn(playerControlled)이 없습니다."); enabled = false; return; }
            player.Init(this, playerArmy, battleCamera);
            foreach (Unit u in units) u.SyncView(s);
            started = true;
        }

        void Update()
        {
            if (!started) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            time += dt;
            player.HandleInput();
            foreach (Army a in armies)
            {
                if (a.defeated) continue;
                if (a.ai != null) a.ai.Tick(dt);
                a.Tick(dt);
            }
            SeparateMembers();
            player.TickCamera(dt);
            TickDummies(dt);
            TickProjectiles(dt);
            UpdateOutcome();
            for (int i = texts.Count - 1; i >= 0; i--) { texts[i].t += dt; if (texts[i].t >= texts[i].dur) texts.RemoveAt(i); }
            for (int i = pings.Count - 1; i >= 0; i--) { pings[i].t += dt; if (pings[i].t >= pings[i].dur) pings.RemoveAt(i); }
            float s = config.unitScale;
            foreach (Unit u in units) { u.flash = Mathf.Max(0f, u.flash - dt * 6f); u.SyncView(s); }
        }

        void UpdateOutcome()
        {
            if (!string.IsNullOrEmpty(outcome)) return;
            if (playerArmy.defeated) { outcome = "패배"; return; }
            foreach (Army a in armies)
                if (a != playerArmy && !a.defeated) return;
            outcome = "승리";
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

        // 패배하지 않은 다른 팀 군단의 지휘관 중 가장 가까운 것.
        public Unit NearestHostileCommander(Army self)
        {
            Unit best = null;
            float bd = float.PositiveInfinity;
            Vector2 p = self.Commander.pos;
            foreach (Army a in armies)
            {
                if (a.defeated || a.team == self.team || a.Commander.dead) continue;
                float d = (a.Commander.pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = a.Commander; }
            }
            return best;
        }

        public void Damage(Unit e, float dmg, Army from)
        {
            if (e.dead) return;
            e.hp -= dmg;
            e.flash = 1f;
            if (e.squad != null) e.squad.hitAt = time;
            if (texts.Count >= 60) texts.RemoveAt(0);
            Color color = from != null ? from.race.color : Color.white;
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
            }
        }

        // ------------------------------------------------------------ 투사체
        // 모든 원거리 공격은 같은 사거리를 쓴다. 쏜 위치에서 정확히 그 거리까지만 난다.
        public void SpawnProjectile(ShotKind kind, Army a, Vector2 from, float angle)
        {
            ShotDef P = config.Shot(kind);
            Vector2 d = BattleMath.Dir(angle);
            var p = new Projectile
            {
                kind = kind, army = a, team = a.team, pos = from + d * 20f, vel = d * P.speed,
                radius = P.radius, damage = P.damage * a.race.rangedDamageMul, left = Mathf.Max(1f, a.Range - 20f),
            };
            var stack = pool[(int)kind];
            GameObject prefab = kind == ShotKind.Bolt ? boltPrefab : kind == ShotKind.Skill ? skillPrefab : autoPrefab;
            p.view = stack.Count > 0 ? stack.Pop() : Instantiate(prefab, projectileRoot);
            p.view.transform.position = ViewPos(p.pos);
            p.view.SetActive(true);
            Color col = a.race.color;
            var rend = p.view.GetComponent<Renderer>();
            if (rend != null)
            {
                shotBlock.SetColor(BaseColorId, col);
                rend.SetPropertyBlock(shotBlock);
            }
            var trail = p.view.GetComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.Clear();
                trail.startColor = col;
                trail.endColor = new Color(col.r, col.g, col.b, 0f);
            }
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
                if (hit != null) Damage(hit, p.damage, p.army);
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
