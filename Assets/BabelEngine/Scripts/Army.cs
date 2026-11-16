using System.Collections.Generic;
using UnityEngine;

namespace Babel
{
    [System.Serializable]
    public class ArmyPrefabs
    {
        public Unit commander, commanderMember, squadLeader, squadMember;
    }

    // 군단 = 지휘관 분대 + 따라다니는 분대들. 웹 프로토타입 update() 의 빨강/파랑 분대 로직.
    // 조작 주체(플레이어, 나중엔 AI)는 moveInput / cursor 를 넣고 명령 함수를 부른다.
    public class Army : MonoBehaviour
    {
        public BattleConfig config;
        public BattleWorld world;
        public int team;
        public Squad commanderSquad;
        public List<Squad> squads = new List<Squad>();

        [Header("명령 상태")]
        [Tooltip("커서를 따라가는 분대 번호(-1 = 없음)")] public int followIdx = -1;
        public bool followAll;
        public bool gathering;
        [Tooltip("따라가는 동안 전투 금지")] public bool followPassive;
        public bool ceasefire;
        public Vector2 gatherAt;
        public float gatherTime;

        [Header("입력 (조작 주체가 매 프레임 넣는다)")]
        public Vector2 moveInput;
        public Vector2 cursor;

        public Unit Commander => commanderSquad.leader;
        public float Range => config.Range;

        static readonly Color MeleeColor = new Color(0.11f, 0.07f, 0.92f);

        // ------------------------------------------------------------ 생성
        public void Build(BattleWorld w, int armyTeam, ArmyPrefabs prefabs, Vector2 origin)
        {
            world = w;
            config = w.config;
            team = armyTeam;
            commanderSquad = MakeSquad("지휘관 분대", 0, prefabs.commander, prefabs.commanderMember, origin, Mathf.PI / 2f);
            for (int i = 0; i < config.squadStart.Length; i++)
            {
                Vector2 o = config.squadStart[i];
                Squad s = MakeSquad($"분대 {i + 1}", i + 1, prefabs.squadLeader, prefabs.squadMember, origin + o, BattleMath.Angle(o));
                s.offset = o;
                s.dir = BattleMath.Norm(o, Vector2.up);
                s.dist = o.magnitude;
                s.leader.fireCd = 0.15f * i;
                squads.Add(s);
            }
        }

        Squad MakeSquad(string squadName, int id, Unit leaderPrefab, Unit memberPrefab, Vector2 pos, float heading)
        {
            var go = new GameObject(squadName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<Squad>();
            s.id = id;
            s.heading = heading;
            s.leader = Spawn(leaderPrefab, go.transform, id == 0 ? "지휘관" : "분대장", pos, heading, false);
            for (int k = 0; k < config.formation.Length; k++)
            {
                Unit m = Spawn(memberPrefab, go.transform, $"분대원 {k + 1}", SlotPos(s, k), heading, true);
                m.fireCd = 0.3f + k * 0.3f;
                m.meleeCd = k * 0.15f;
                s.members.Add(m);
            }
            return s;
        }

        Unit Spawn(Unit prefab, Transform parent, string unitName, Vector2 pos, float facing, bool member)
        {
            Unit u = Instantiate(prefab, parent);
            u.name = unitName;
            u.isMember = member;
            u.Setup(team, pos, facing);
            world.Register(u);
            return u;
        }

        // ------------------------------------------------------------ 기하
        Vector2 SlotPos(Squad s, int k)
        {
            Vector2 o = config.formation[k];
            float c = Mathf.Cos(s.heading), sn = Mathf.Sin(s.heading), sp = config.memberSpacing;
            return s.leader.pos + new Vector2(o.x * c - o.y * sn, o.x * sn + o.y * c) * sp;
        }

        // 제한 원: 지름 D, 한쪽 끝이 지휘관에 닿고 분대 방향으로 뻗는다.
        public Vector2 LimitCenter(Squad s) => Commander.pos + s.dir * (config.D / 2f);
        public bool InLimitCircle(Squad s, Vector2 p, float margin) => (p - LimitCenter(s)).magnitude <= config.D / 2f + margin;

        public bool IsPassive(Squad s)
        {
            if (ceasefire) return true;
            if (!followPassive) return false;
            return followAll || (followIdx >= 0 && squads[followIdx] == s);
        }

        public string StatusOf(int i)
        {
            Squad s = squads[i];
            bool following = followAll || followIdx == i;
            if (following && gathering) return followPassive ? "집결 중 (비전투)" : "집결 중";
            if (following) return followPassive ? "커서 추적 (비전투)" : "커서 추적";
            if (s.state == SquadState.Hold && world.time - s.firedAt < 0.6f) return "사격";
            switch (s.state)
            {
                case SquadState.Melee: return "근접 전투";
                case SquadState.Retreat: return "퇴각";
                case SquadState.Return: return "복귀";
                default: return "대기";
            }
        }

        // ------------------------------------------------------------ 명령
        // 분대의 지정 위치를 지휘관 기준 상대 위치로 저장. D 보다 멀면 같은 방향의 최대 거리로 제한.
        // 직접 명령은 자동 전투보다 우선: 근접을 끊고, 도착 전까지 다시 돌격하지 않는다.
        public bool OrderMove(int idx, Vector2 w, bool feedback)
        {
            Squad s = squads[idx];
            Vector2 o = w - Commander.pos;
            float d = o.magnitude;
            bool clamped = d > config.D;
            if (clamped) o *= config.D / d;
            s.offset = o;
            s.target = null;
            s.engageLock = float.PositiveInfinity;
            s.SetState(SquadState.Return);
            if (feedback) world.AddPing(Commander.pos + o, clamped);
            return clamped;
        }

        // 모든 분대를 한 점 주변 2×2 로 모은다.
        public void GroupOrder(Vector2 w, bool feedback)
        {
            float sl = config.rallySlot;
            for (int i = 0; i < squads.Count; i++)
            {
                Vector2 spread = new Vector2((i % 2 == 0 ? -1 : 1) * sl, (i < 2 ? 1 : -1) * sl);
                OrderMove(i, w + spread, feedback);
            }
        }

        public void StartRally(Vector2 w)
        {
            followAll = true;
            followPassive = true;
            gathering = true;
            gatherAt = w;
            gatherTime = 0f;
            GroupOrder(w, false);
            world.AddPing(w, false);
        }

        public void StopFollow()
        {
            followIdx = -1;
            followAll = false;
            gathering = false;
            followPassive = false;
        }

        // ------------------------------------------------------------ 매 프레임
        public void Tick(float dt)
        {
            BattleConfig C = config;
            float H = C.worldHalf, range = Range, time = world.time;
            Unit red = Commander;

            // 지휘관 이동
            Vector2 inp = Vector2.ClampMagnitude(moveInput, 1f);
            red.vel += (inp * C.commanderMaxSpeed - red.vel) * BattleMath.Damp(C.commanderAccel, dt);
            if (inp == Vector2.zero && red.vel.magnitude < 2f) red.vel = Vector2.zero;
            Vector2 prev = red.pos;
            red.pos = new Vector2(Mathf.Clamp(red.pos.x + red.vel.x * dt, -H, H), Mathf.Clamp(red.pos.y + red.vel.y * dt, -H, H));
            Vector2 rv = (red.pos - prev) / dt;

            // 커서 따라가기 / 집결
            if (followIdx >= 0) OrderMove(followIdx, cursor, false);
            else if (followAll && gathering)
            {
                GroupOrder(gatherAt, false);
                gatherTime += dt;
                bool arrived = true;
                foreach (Squad s in squads)
                    if ((red.pos + s.offset - s.leader.pos).magnitude >= C.rallyArrive) { arrived = false; break; }
                if (arrived || gatherTime > C.rallyTimeout)
                {
                    gathering = false;
                    world.Toast("모든 분대: 집결 완료 — 커서 추적 시작");
                }
            }
            else if (followAll) GroupOrder(cursor, false);

            // 지휘관 분대원: 대형 유지 + 사거리 안 적 자동 사격 (지휘관 본인은 좌클릭으로만 쏜다)
            UpdateHeading(commanderSquad, rv, dt);
            commanderSquad.rangedTarget = world.NearestEnemy(team, red.pos, e => (e.pos - red.pos).magnitude <= range);
            UpdateMembers(commanderSquad, rv, null, commanderSquad.rangedTarget, ShotKind.Bolt, !ceasefire, dt);

            foreach (Squad s in squads)
            {
                Unit L = s.leader;
                s.stateTime += dt;
                L.fireCd -= dt;
                L.meleeCd -= dt;
                s.engageLock -= dt; // Infinity 는 도착할 때까지 Infinity
                L.lunge = Mathf.Max(0f, L.lunge - dt * 6f);
                bool passive = IsPassive(s);

                // --- 상태 전이
                if (s.state == SquadState.Melee)
                {
                    Unit e = s.target;
                    if (passive || e == null || e.dead)
                    {
                        s.target = null;
                        s.SetState(SquadState.Return);
                    }
                    else if (!InLimitCircle(s, e.pos, C.meleeLeaveMargin))
                    {
                        // 싸움이 제한 원 밖으로 나감: 지휘관 쪽으로 물러난 뒤 복귀
                        s.target = null;
                        s.engageLock = float.PositiveInfinity;
                        s.SetState(SquadState.Retreat);
                    }
                }
                if (!passive && (s.state == SquadState.Hold || s.state == SquadState.Return) && s.engageLock <= 0f)
                {
                    Unit e = world.NearestEnemy(team, L.pos, en => InLimitCircle(s, en.pos, 0f));
                    if (e != null) { s.target = e; s.SetState(SquadState.Melee); }
                }

                // --- 이동 목표
                Vector2 t;
                bool carry = true;
                if (s.state == SquadState.Melee)
                {
                    Vector2 n = BattleMath.Norm(L.pos - s.target.pos, -s.dir);
                    t = s.target.pos + n * C.meleeReach;
                    carry = false;
                }
                else if (s.state == SquadState.Retreat)
                {
                    t = red.pos + BattleMath.Norm(L.pos - red.pos, s.dir) * 50f;
                }
                else t = red.pos + s.offset;

                // 지휘관의 움직임을 그대로 실어 주고(상대 위치 유지), 목표로 부드럽게 보정한 뒤 D 제한을 강제한다.
                Vector2 old = L.pos;
                if (carry) L.pos += rv * dt;
                Vector2 c = Vector2.ClampMagnitude((t - L.pos) * BattleMath.Damp(C.steer, dt), C.steerMax * dt);
                L.pos += c;

                Vector2 d = L.pos - red.pos;
                if (d.magnitude > C.D) L.pos = red.pos + d.normalized * C.D;
                L.pos = new Vector2(Mathf.Clamp(L.pos.x, -H, H), Mathf.Clamp(L.pos.y, -H, H));
                d = L.pos - red.pos;
                s.dist = d.magnitude;
                if (s.dist > 0.5f) s.dir = d / s.dist;

                L.vel = (L.pos - old) / dt;
                if (L.facingHold > 0f) L.facingHold -= dt;
                else if (L.vel.magnitude > 25f) L.facing = BattleMath.Angle(L.vel);

                // --- 도착
                if (s.state == SquadState.Retreat && s.dist < 70f) s.SetState(SquadState.Return);
                if (s.state == SquadState.Return &&
                    ((red.pos + s.offset - L.pos).magnitude < 8f || s.stateTime > C.returnTimeout))
                {
                    s.SetState(SquadState.Hold);
                    if (float.IsPositiveInfinity(s.engageLock)) s.engageLock = C.reengageDelay;
                }

                // --- 공격
                if (s.state == SquadState.Melee)
                {
                    Unit e = s.target;
                    if ((e.pos - L.pos).magnitude <= C.meleeReach + 10f)
                    {
                        L.facing = BattleMath.Angle(e.pos - L.pos);
                        L.facingHold = 0.3f;
                        if (L.meleeCd <= 0f)
                        {
                            L.meleeCd = C.meleeCooldown;
                            L.lunge = 1f;
                            s.attackedAt = time;
                            world.Damage(e, C.meleeDamage, MeleeColor);
                        }
                    }
                }
                // 분대의 사격 대상(분대장·분대원 공유). 근접·퇴각 중에는 없음.
                s.rangedTarget = !passive && (s.state == SquadState.Hold || s.state == SquadState.Return)
                    ? world.NearestEnemy(team, L.pos, en => (en.pos - L.pos).magnitude <= range)
                    : null;
                if (s.rangedTarget != null && L.fireCd <= 0f)
                {
                    float a = BattleMath.Angle(s.rangedTarget.pos - L.pos);
                    world.SpawnProjectile(ShotKind.Auto, team, L.pos, a);
                    L.fireCd = C.autoCooldown;
                    L.facing = a;
                    L.facingHold = 0.4f;
                    s.firedAt = time;
                    s.attackedAt = time;
                }

                UpdateHeading(s, L.vel, dt);
                UpdateMembers(s, L.vel, s.state == SquadState.Melee ? s.target : null, s.rangedTarget,
                    ShotKind.Auto, s.state != SquadState.Retreat && !passive, dt);
            }
        }

        // 대형은 분대장의 진행 방향을 본다(멈춰 있으면 유지).
        void UpdateHeading(Squad s, Vector2 v, float dt)
        {
            if (v.magnitude > 30f) s.heading = BattleMath.LerpAngle(s.heading, BattleMath.Angle(v), BattleMath.Damp(6f, dt));
        }

        void UpdateMembers(Squad S, Vector2 lv, Unit meleeTarget, Unit rangedTarget, ShotKind kind, bool canFire, float dt)
        {
            BattleConfig C = config;
            Unit L = S.leader;
            float range = Range;
            for (int k = 0; k < S.members.Count; k++)
            {
                Unit m = S.members[k];
                m.fireCd -= dt;
                m.meleeCd -= dt;
                if (m.facingHold > 0f) m.facingHold -= dt;
                m.lunge = Mathf.Max(0f, m.lunge - dt * 6f);

                // 목표 지점: 대형 자리(분대장 속도를 실어 줌) 또는 근접 대상 둘레의 고리
                Unit e = meleeTarget;
                Vector2 t, fv = lv;
                if (e != null)
                {
                    float a = BattleMath.Angle(L.pos - e.pos) + C.meleeRingDeg[k % C.meleeRingDeg.Length] * Mathf.Deg2Rad;
                    t = e.pos + BattleMath.Dir(a) * (e.radius + m.radius + 6f);
                    fv = Vector2.zero;
                }
                else t = SlotPos(S, k);

                Vector2 dv = Vector2.ClampMagnitude(fv + (t - m.pos) * C.memberGain, C.memberMaxSpeed);
                m.vel += (dv - m.vel) * BattleMath.Damp(C.memberAccel, dt);
                m.pos += m.vel * dt;
                if (m.facingHold <= 0f && m.vel.magnitude > 30f) m.facing = BattleMath.Angle(m.vel);

                if (e != null)
                {
                    if (!e.dead && (e.pos - m.pos).magnitude <= e.radius + m.radius + 16f)
                    {
                        m.facing = BattleMath.Angle(e.pos - m.pos);
                        m.facingHold = 0.3f;
                        if (m.meleeCd <= 0f)
                        {
                            m.meleeCd = C.memberMeleeCooldown * (0.85f + Random.value * 0.3f);
                            m.lunge = 1f;
                            S.attackedAt = world.time;
                            world.Damage(e, C.memberMeleeDamage, MeleeColor);
                        }
                    }
                }
                else if (canFire && rangedTarget != null && !rangedTarget.dead && m.fireCd <= 0f &&
                         (rangedTarget.pos - m.pos).magnitude <= range)
                {
                    float ang = BattleMath.Angle(rangedTarget.pos - m.pos);
                    world.SpawnProjectile(kind, team, m.pos, ang);
                    S.attackedAt = world.time;
                    m.fireCd = C.memberFireCooldown * (0.85f + Random.value * 0.3f);
                    m.facing = ang;
                    m.facingHold = 0.4f;
                }
            }
        }
    }
}
