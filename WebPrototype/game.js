// Babel Engine — squad control & camera R&D prototype.
// Plain canvas, no dependencies. World units == CSS pixels (zoom is fixed at 1).
(() => {
  'use strict';

  const TAU = Math.PI * 2;

  // ---------------------------------------------------------------- config
  const CFG = {
    D: 1440,             // max red↔blue distance; also the limit circle's DIAMETER
    redMaxSpeed: 260,     // u/s
    redAccel: 14,         // red velocity smoothing (1/s)
    steer: 3,            // companion correction gain toward assigned spot (1/s)
    steerMax: 480,       // companion max correction speed (u/s), on top of red's motion
    camFollow: 5,         // camera smoothing (1/s)
    camSquadBias: 0.75,   // camera on a squad: this far along avatar→leader (0.5 = midpoint)
    zoom: 1.75,           // current zoom (starts in avatar mode)
    zoomAvatar: 1.75,     // avatar-mode zoom (wheel in avatar mode adjusts this)
    zoomSquadNear: 1,     // camera on a squad that is within D/2 of the avatar
    zoomSquadFar: 0.6,    // camera on a squad that is farther than D/2
    zoomBandHysteresis: 20, // u, avoids flicker right at the D/2 boundary
    modeZoomFollow: 5,    // zoom easing on mode change (1/s), matches the camera pan
    wheelZoomFollow: 14,  // zoom easing for wheel input (1/s), snappier
    zoomMin: 0.3,
    zoomMax: 2,
    worldHalf: 4000,
    rangeMul: 1,          // ranged attack range = D * rangeMul, i.e. the limit circle's diameter
    bolt: { speed: 950, r: 5, dmg: 1 },              // red, manual
    skill: { speed: 720, r: 9, dmg: 3 },             // blue, manual
    auto: { speed: 900, r: 4, dmg: 1, cooldown: 0.9 }, // blue, automatic
    melee: { reach: 44, dmg: 2, cooldown: 0.55, leaveMargin: 16 },
    // Squad members (4 per squad): follow the leader and fight alongside it.
    member: { scale: 0.7, spacing: 3, maxSpeed: 760, gain: 4.5, accel: 14, fireCooldown: 1.3, meleeDmg: 1, meleeCooldown: 0.75 },
    returnTimeout: 6,     // safety: give up 'return' if the spot is unreachable
    reengageDelay: 1,     // after a retreat, wait this long at the post before melee again
    showInactiveCircles: true,
    showAllMidpoints: true,
    showRanges: true,
  };

  const COL = {
    outside: '#dcdcdc',
    bg: '#f4f4f4',
    gridMinor: 'rgba(0,0,0,0.055)',
    gridMajor: 'rgba(0,0,0,0.13)',
    axis: 'rgba(0,0,0,0.22)',
    red: '#e81616',
    redDark: '#9e0d0d',
    blue: '#1c12ea',
    blueDark: '#0e0a8a',
    green: '#1fae22',
    enemy: '#4a4a4a',
    enemyEdge: '#1e1e1e',
    bolt: '#ff6a00',
    skill: '#00a8ff',
    auto: '#5b7cff',
    melee: '#1c12ea',
  };

  const STATE_LABEL = { hold: '대기', melee: '근접 전투', retreat: '퇴각', return: '복귀' };

  // ---------------------------------------------------------------- canvas
  const canvas = document.getElementById('game');
  const ctx = canvas.getContext('2d');
  let VW = 0, VH = 0, DPR = 1;

  function resize() {
    DPR = Math.max(1, window.devicePixelRatio || 1);
    VW = canvas.clientWidth;
    VH = canvas.clientHeight;
    canvas.width = Math.round(VW * DPR);
    canvas.height = Math.round(VH * DPR);
  }
  window.addEventListener('resize', resize);
  resize();

  // ---------------------------------------------------------------- helpers
  const hyp = Math.hypot;
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const damp = (k, dt) => 1 - Math.exp(-k * dt);
  function norm(x, y, fallback) {
    const l = hyp(x, y);
    return l > 1e-6 ? { x: x / l, y: y / l } : (fallback || { x: 0, y: -1 });
  }
  // Distance from point p to segment a→b (projectile swept hit test).
  function segDist(px, py, ax, ay, bx, by) {
    const dx = bx - ax, dy = by - ay;
    const l2 = dx * dx + dy * dy;
    const t = l2 > 0 ? clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1) : 0;
    return hyp(px - (ax + dx * t), py - (ay + dy * t));
  }

  function lerpAngle(a, b, t) {
    const d = ((b - a + Math.PI) % TAU + TAU) % TAU - Math.PI;
    return a + d * t;
  }

  // ---------------------------------------------------------------- squad members
  const LEADER_R = { red: 18, blue: 16 };
  // V formation in the leader's local frame (+x = heading): two wings, two rear.
  // Base (1×) spacing; the peacetime spacing is this times CFG.member.spacing.
  const FORMATION = [{ x: -12, y: -40 }, { x: -12, y: 40 }, { x: -46, y: -18 }, { x: -46, y: 18 }];
  // In melee the leader takes one side of the enemy, members fill the rest of a 5-way ring.
  const MELEE_RING = [72, -72, 144, -144].map(d => d * Math.PI / 180);

  function slotPos(L, o) {
    const c = Math.cos(L.heading), s = Math.sin(L.heading), k = CFG.member.spacing;
    return { x: L.x + (o.x * c - o.y * s) * k, y: L.y + (o.x * s + o.y * c) * k };
  }
  function makeMembers(L, leaderR) {
    return FORMATION.map((o, k) => {
      const p = slotPos(L, o);
      return {
        x: p.x, y: p.y, vx: 0, vy: 0, r: leaderR * CFG.member.scale,
        facing: L.heading, facingHold: 0, lunge: 0,
        cd: 0.3 + k * 0.3, mcd: k * 0.15,
      };
    });
  }
  // Formation faces the leader's direction of travel (kept while standing still).
  function updateHeading(L, vx, vy, dt) {
    if (hyp(vx, vy) > 30) L.heading = lerpAngle(L.heading, Math.atan2(vy, vx), damp(6, dt));
  }

  // opts: { meleeTarget, rangedTarget, shotKind, canFire }
  function updateMembers(L, lvx, lvy, opts, dt) {
    const M = CFG.member, range = rangedRange();
    L.members.forEach((m, k) => {
      m.cd -= dt;
      m.mcd -= dt;
      if (m.facingHold > 0) m.facingHold -= dt;
      m.lunge = Math.max(0, m.lunge - dt * 6);

      // Target spot: formation slot (carrying the leader's velocity), or a ring around the melee target.
      const e = opts.meleeTarget;
      let tx, ty, fvx = lvx, fvy = lvy;
      if (e) {
        const a = Math.atan2(L.y - e.y, L.x - e.x) + MELEE_RING[k];
        const rr = e.r + m.r + 6;
        tx = e.x + Math.cos(a) * rr;
        ty = e.y + Math.sin(a) * rr;
        fvx = fvy = 0;
      } else {
        const p = slotPos(L, FORMATION[k]);
        tx = p.x; ty = p.y;
      }
      let dvx = fvx + (tx - m.x) * M.gain, dvy = fvy + (ty - m.y) * M.gain;
      const dl = hyp(dvx, dvy);
      if (dl > M.maxSpeed) { dvx *= M.maxSpeed / dl; dvy *= M.maxSpeed / dl; }
      const a = damp(M.accel, dt);
      m.vx += (dvx - m.vx) * a;
      m.vy += (dvy - m.vy) * a;
      m.x += m.vx * dt;
      m.y += m.vy * dt;
      if (m.facingHold <= 0 && hyp(m.vx, m.vy) > 30) m.facing = Math.atan2(m.vy, m.vx);

      // Attacks
      if (e) {
        if (!e.dead && hyp(e.x - m.x, e.y - m.y) <= e.r + m.r + 16) {
          m.facing = Math.atan2(e.y - m.y, e.x - m.x);
          m.facingHold = 0.3;
          if (m.mcd <= 0) {
            m.mcd = M.meleeCooldown * (0.85 + Math.random() * 0.3);
            m.lunge = 1;
            L.attackedAt = time;
            effects.push({ type: 'slash', x: e.x, y: e.y, r: e.r + 6, a: Math.atan2(m.y - e.y, m.x - e.x), small: true, t: 0, dur: 0.2 });
            damage(e, M.meleeDmg, 'melee', m.x, m.y);
          }
        }
      } else {
        const t = opts.rangedTarget;
        if (opts.canFire && t && !t.dead && m.cd <= 0 && hyp(t.x - m.x, t.y - m.y) <= range) {
          const ang = Math.atan2(t.y - m.y, t.x - m.x);
          spawnProjectile(opts.shotKind, m.x, m.y, ang);
          L.attackedAt = time;
          m.cd = M.fireCooldown * (0.85 + Math.random() * 0.3);
          m.facing = ang;
          m.facingHold = 0.4;
        }
      }
    });
  }

  // Soft separation: members never overlap each other, any leader, or a live enemy.
  function separateMembers() {
    const all = red.members.concat(...squads.map(s => s.members));
    for (let i = 0; i < all.length; i++) {
      for (let j = i + 1; j < all.length; j++) {
        const a = all[i], b = all[j];
        const dx = b.x - a.x, dy = b.y - a.y, d = hyp(dx, dy), min = a.r + b.r + 2;
        if (d < min) {
          const n = d > 1e-6 ? { x: dx / d, y: dy / d } : { x: 1, y: 0 };
          const push = (min - d) / 2;
          a.x -= n.x * push; a.y -= n.y * push;
          b.x += n.x * push; b.y += n.y * push;
        }
      }
    }
    const solids = [{ x: red.x, y: red.y, r: LEADER_R.red }]
      .concat(squads.map(s => ({ x: s.x, y: s.y, r: LEADER_R.blue })))
      .concat(enemies.filter(e => !e.dead));
    for (const m of all) {
      for (const o of solids) {
        const dx = m.x - o.x, dy = m.y - o.y, d = hyp(dx, dy), min = m.r + o.r + 2;
        if (d < min) {
          const n = d > 1e-6 ? { x: dx / d, y: dy / d } : { x: 0, y: 1 };
          m.x = o.x + n.x * min;
          m.y = o.y + n.y * min;
        }
      }
    }
  }

  // ---------------------------------------------------------------- state
  const SQUAD_START = [[-340, -240], [340, -240], [-340, 260], [340, 260]];

  const red = { x: 0, y: 0, vx: 0, vy: 0, aim: -Math.PI / 2, recoil: 0, heading: -Math.PI / 2, rangedTarget: null };
  red.members = makeMembers(red, LEADER_R.red);

  const squads = SQUAD_START.map(([ox, oy], i) => {
    const s = {
      id: i + 1,
      x: ox, y: oy, vx: 0, vy: 0,
      offset: { x: ox, y: oy },   // assigned position RELATIVE to red (world axes)
      dir: norm(ox, oy),          // red → blue direction (drives limit circle placement)
      dist: hyp(ox, oy),
      facing: Math.atan2(oy, ox),
      heading: Math.atan2(oy, ox), // formation direction
      facingHold: 0,
      flash: 0,
      ...freshBehaviour(i),
    };
    s.members = makeMembers(s, LEADER_R.blue);
    return s;
  });

  // hold: keep assigned spot, auto-fire at enemies in range
  // melee: an enemy entered this squad's limit circle → close in and strike
  // retreat: melee target left the limit circle → fall back to the avatar
  // return: head back to the assigned spot
  function freshBehaviour(i) {
    return {
      state: 'hold', stateTime: 0, target: null, rangedTarget: null,
      fireCd: 0.15 * i, meleeCd: 0, engageLock: 0, lunge: 0, firedAt: -9,
      attackedAt: -9,   // last time the leader or a member shot / struck (arrow vibration)
      hitAt: -9,        // last time the squad was hit (arrow blink) — see squadHit()
    };
  }

  let selected = null;          // null = avatar mode, 0..3 = squad command mode
  let camFocus = null;          // index of the squad whose red↔squad midpoint the camera follows (null = avatar)
  const cam = { x: 0, y: 0 };
  let camTarget = { x: 0, y: 0 };
  const mouse = { sx: 0, sy: 0, inside: false, wx: 0, wy: 0 };
  const keys = new Set();
  const projectiles = [];
  const effects = [];
  let time = 0;

  const ENEMY_DEFS = [
    { x: 1120, y: -520 },
    { x: 1440, y: 400, patrol: { ax: 0, ay: 1, amp: 300, spd: 0.9 } },
    { x: -1160, y: -760 },
    { x: -880, y: 1000, patrol: { ax: 1, ay: 0, amp: 340, spd: 0.7 } },
    { x: 320, y: 1320 },
    { x: 2000, y: -1160, patrol: { ax: 1, ay: 0, amp: 240, spd: 1.1 } },
    { x: -2040, y: 240 },
    { x: 120, y: -1520, patrol: { ax: 1, ay: 0, amp: 440, spd: 0.5 } },
  ];
  const enemies = ENEMY_DEFS.map((d, i) => ({
    bx: d.x, by: d.y, x: d.x, y: d.y, patrol: d.patrol || null, phase: i * 1.3,
    r: 20, hp: 30, maxHp: 30, flash: 0, dead: false, respawn: 0,
  }));

  // ---------------------------------------------------------------- combat geometry
  const rangedRange = () => CFG.D * CFG.rangeMul;

  // The squad's limit circle: diameter D, edge on red, extending toward the squad.
  function limitCenter(s) {
    return { x: red.x + s.dir.x * CFG.D / 2, y: red.y + s.dir.y * CFG.D / 2 };
  }
  function inLimitCircle(s, x, y, margin) {
    const c = limitCenter(s);
    return hyp(x - c.x, y - c.y) <= CFG.D / 2 + margin;
  }
  function nearestEnemy(x, y, accept) {
    let best = null, bd = Infinity;
    for (const e of enemies) {
      if (e.dead || !accept(e)) continue;
      const d = hyp(e.x - x, e.y - y);
      if (d < bd) { bd = d; best = e; }
    }
    return best;
  }
  // Camera point when the camera is on a squad: on the avatar→leader line, CFG.camSquadBias of the
  // way toward the leader (0.5 = midpoint, 0.75 = closer to the squad).
  function camPointFor(s) {
    const k = CFG.camSquadBias;
    return { x: red.x + (s.x - red.x) * k, y: red.y + (s.y - red.y) * k };
  }
  function setState(s, state) {
    if (s.state !== state) { s.state = state; s.stateTime = 0; }
  }

  // ---------------------------------------------------------------- coordinates
  function screenToWorld(sx, sy) {
    return { x: (sx - VW / 2) / CFG.zoom + cam.x, y: (sy - VH / 2) / CFG.zoom + cam.y };
  }
  function worldToScreen(wx, wy) {
    return { x: (wx - cam.x) * CFG.zoom + VW / 2, y: (wy - cam.y) * CFG.zoom + VH / 2 };
  }
  function eventPos(e) {
    const r = canvas.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }

  // ---------------------------------------------------------------- toast
  const toastEl = document.getElementById('toast');
  let toastTimer = 0;
  function toast(msg) {
    toastEl.textContent = msg;
    toastEl.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toastEl.classList.remove('show'), 1400);
  }

  // ---------------------------------------------------------------- commands
  // Number key: select only — the camera keeps doing whatever it was doing (avatar, or the
  // previous squad's midpoint). Same number again: camera moves to this squad's midpoint.
  function selectSquad(i) {
    if (selected === i) {
      if (camFocus !== i) {
        camFocus = i;
        updateZoomBand(true);
        toast(`카메라 → ${i + 1}번 분대 쪽 (${Math.round(CFG.camSquadBias * 100)}% 지점)`);
      }
      return;
    }
    selected = i;
    stopFollow(); // the previous squad keeps the spot it had reached
    toast(`${i + 1}번 분대 선택 — 한 번 더 누르면 카메라 이동`);
  }

  // Space, mirroring the number keys: first press selects the avatar only (camera stays put),
  // a second press (camera still on a squad) brings the camera back to the avatar.
  function spaceToAvatar() {
    if (selected !== null) {
      selected = null;
      stopFollow();
      toast(camFocus !== null ? '아바타 선택 — 한 번 더 누르면 카메라 이동' : '아바타 모드');
    } else if (camFocus !== null) {
      camFocus = null;
      applyModeZoom();
      toast('카메라 → 빨강 분대');
    }
  }

  // Esc: straight back to avatar mode, camera included.
  function deselect() {
    if (followAll) { stopFollow(); toast('모든 분대: 커서 따라가기 해제'); }
    if (selected === null && camFocus === null) return;
    const wasFocused = camFocus !== null;
    selected = null;
    camFocus = null;
    stopFollow();
    if (wasFocused) applyModeZoom();
    toast('아바타 모드 — 카메라 → 빨강 분대');
  }

  // Right click (command mode only): set the squad's position relative to red,
  // clamped to the farthest reachable point within D along the same ray.
  // Follow toggle: the first right click makes the squad's order follow the cursor every frame,
  // the next right click pins it where the cursor is (feedback = false while following).
  let followIdx = null;          // squad whose order is following the cursor, or null
  function orderMove(w, feedback = true, idx = selected) {
    if (idx === null) return;
    const s = squads[idx];
    let ox = w.x - red.x, oy = w.y - red.y;
    const d = hyp(ox, oy);
    const clamped = d > CFG.D;
    if (clamped) { ox *= CFG.D / d; oy *= CFG.D / d; }
    s.offset = { x: ox, y: oy };
    // A direct order overrides auto-combat: drop any melee/retreat and hold off
    // engaging again until the squad reaches its new spot.
    s.target = null;
    s.engageLock = Infinity;
    setState(s, 'return');
    if (!feedback) return;
    effects.push({ type: 'order', x: red.x + ox, y: red.y + oy, cx: w.x, cy: w.y, clamped, t: 0, dur: clamped ? 1.3 : 0.7 });
    toast(clamped
      ? `${s.id}번 분대: 명령 지점이 D 밖이라 최대 거리로 제한됨 (${Math.round(d)} → ${CFG.D})`
      : `${s.id}번 분대: 새 상대 위치 지정`);
  }

  // Left click: one projectile from red (avatar) or from the selected squad (skill).
  function fire(w) {
    if (selected === null) {
      const a = Math.atan2(w.y - red.y, w.x - red.x);
      red.aim = a;
      red.recoil = 1;
      spawnProjectile('bolt', red.x, red.y, a, w);
    } else {
      const s = squads[selected];
      const a = Math.atan2(w.y - s.y, w.x - s.x);
      s.facing = a;
      s.facingHold = 0.7;
      s.flash = 1;
      s.attackedAt = time;
      spawnProjectile('skill', s.x, s.y, a, w);
    }
  }

  // All ranged attacks share one range, measured from the shooter's center.
  function spawnProjectile(kind, sx, sy, a, w) {
    const P = CFG[kind];
    const dx = Math.cos(a), dy = Math.sin(a);
    const x = sx + dx * 20, y = sy + dy * 20;
    const left = Math.max(1, rangedRange() - 20); // distance still allowed to travel
    projectiles.push({ kind, x, y, vx: dx * P.speed, vy: dy * P.speed, r: P.r, dmg: P.dmg, left, trail: [] });
    if (w) effects.push({ type: 'aim', x: w.x, y: w.y, kind, t: 0, dur: 0.9 });
    effects.push({ type: 'muzzle', x, y, kind, t: 0, dur: 0.18 });
  }

  // Call when a squad takes damage. Enemies don't attack yet, so for now only the test button uses it.
  function squadHit(i) {
    squads[i].hitAt = time;
  }

  function resetPositions() {
    red.x = red.y = red.vx = red.vy = 0;
    red.heading = -Math.PI / 2;
    red.members = makeMembers(red, LEADER_R.red);
    squads.forEach((s, i) => {
      const [ox, oy] = SQUAD_START[i];
      Object.assign(s, { x: ox, y: oy, vx: 0, vy: 0, offset: { x: ox, y: oy }, dir: norm(ox, oy), heading: Math.atan2(oy, ox) }, freshBehaviour(i));
      s.members = makeMembers(s, LEADER_R.blue);
    });
    selected = null;
    camFocus = null;
    stopFollow();
    applyModeZoom();
    cam.x = cam.y = 0;
    projectiles.length = 0;
    effects.length = 0;
    toast('위치 초기화됨');
  }

  // ---------------------------------------------------------------- input
  // Hide/show every overlay panel (readout, tuning + squad cards, help). The toggle button stays.
  const uiToggleBtn = document.getElementById('ui-toggle');
  function toggleUI() {
    const hidden = document.body.classList.toggle('ui-hidden');
    uiToggleBtn.textContent = hidden ? 'UI 보이기 (U)' : 'UI 숨기기 (U)';
  }
  uiToggleBtn.addEventListener('click', e => { toggleUI(); e.currentTarget.blur(); });

  // Cease-fire (X): one press forbids all automatic combat (auto-fire, melee charge) for every
  // squad incl. the avatar's members; a double press (≤ 300 ms) while it is on lifts it.
  // The double press only lifts a cease-fire that was already on before the pair started.
  let ceasefire = false;
  let lastX = { t: -1e9, forbade: false };
  function xPressed() {
    const now = performance.now();
    const isDouble = now - lastX.t <= 300;
    if (!ceasefire) {
      ceasefire = true;
      lastX = { t: now, forbade: true };
      toast('전투 행동 금지 — X 더블 입력으로 해제');
    } else if (isDouble && !lastX.forbade) {
      ceasefire = false;
      lastX = { t: -1e9, forbade: false };
      toast('전투 행동 금지 해제');
    } else {
      lastX = { t: now, forbade: false };
    }
  }

  const MOVE_KEYS = ['KeyW', 'KeyA', 'KeyS', 'KeyD'];
  function squadKey(code) {
    const m = /^(?:Digit|Numpad)([1-4])$/.exec(code);
    return m ? Number(m[1]) - 1 : -1;
  }

  window.addEventListener('keydown', e => {
    if (e.code === 'KeyH' && !e.repeat) {
      document.querySelector('.help').classList.toggle('hidden');
      return;
    }
    if (e.code === 'KeyU' && !e.repeat) {
      toggleUI();
      return;
    }
    if (e.code === 'KeyX' && !e.repeat) {
      xPressed();
      return;
    }
    const n = squadKey(e.code);
    if (!MOVE_KEYS.includes(e.code) && n < 0 && e.code !== 'Escape' && e.code !== 'Space') return;
    // Make sure tuning widgets never swallow game keys.
    if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
    e.preventDefault();
    keys.add(e.code);
    if (e.repeat) return;
    if (n >= 0) selectSquad(n);
    else if (e.code === 'Space') spaceToAvatar();
    else if (e.code === 'Escape') deselect();
  });
  window.addEventListener('keyup', e => keys.delete(e.code));
  window.addEventListener('blur', () => keys.clear());

  canvas.addEventListener('mousemove', e => {
    const p = eventPos(e);
    mouse.sx = p.x; mouse.sy = p.y; mouse.inside = true;
  });
  canvas.addEventListener('mouseleave', () => { mouse.inside = false; });
  canvas.addEventListener('mousedown', e => {
    e.preventDefault();
    if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
    const p = eventPos(e);
    mouse.sx = p.x; mouse.sy = p.y; mouse.inside = true;
    // Resolve against the camera exactly as it was drawn this frame.
    const w = screenToWorld(p.x, p.y);

    // Left button = attack skill only: every left click fires, it never touches follow / rally.
    if (e.button === 0) { fire(w); return; }
    if (e.button !== 2) return;

    // Right double click (≤ 300 ms, ≤ 8 px apart) while squads follow the cursor (or the first
    // click of the pair just released it): release / keep released — the second click does not
    // restart the follow.
    const now = performance.now();
    const isDouble = now - lastDown.t <= 300 && hyp(p.x - lastDown.x, p.y - lastDown.y) <= 8;
    const releasedByFirst = lastDown.released;
    lastDown = { button: 2, t: isDouble ? -1e9 : now, x: p.x, y: p.y, released: false };
    if (isDouble && (followIdx !== null || followAll || releasedByFirst)) {
      if (followAll) {
        groupOrder(w, true);
        stopFollow();
        toast('모든 분대: 커서 따라가기 해제 (우클릭 더블 클릭) — 이 위치에 고정');
      } else if (followIdx !== null) {
        const s = squads[followIdx];
        orderMove(w, true, followIdx);
        stopFollow();
        toast(`${s.id}번 분대: 커서 따라가기 해제 (우클릭 더블 클릭) — 이 위치에 고정`);
      }
      return;
    }

    // Right click 1: follow the cursor WITHOUT combat · right click 2 (while following): follow
    // with combat allowed as usual · right double click: release (handled above).
    if (selected === null) {
      // Avatar mode: all squads rally at the clicked spot, then follow the cursor.
      if (followAll) {
        if (followPassive) {
          followPassive = false;
          toast('모든 분대: 전투 허용하며 커서 따라가기 (우클릭 더블 클릭으로 해제)');
        }
      } else {
        // Rally: every squad gathers at the clicked spot (a tight 2×2 cluster), then the cluster
        // follows the cursor.
        followAll = true;
        followPassive = true;
        gathering = true;
        gatherAt = { x: w.x, y: w.y };
        gatherTime = 0;
        groupOrder(gatherAt, false);
        effects.push({ type: 'order', x: w.x, y: w.y, cx: w.x, cy: w.y, clamped: false, t: 0, dur: 0.9 });
        toast('모든 분대: 비전투로 집결 → 커서 추적 — 우클릭 한 번 더: 전투 허용, 더블 클릭: 해제');
      }
    }
    else {
      // Squad selected: that squad follows the cursor.
      if (followIdx === selected) {
        if (followPassive) {
          followPassive = false;
          toast(`${selected + 1}번 분대: 전투 허용하며 커서 따라가기 (우클릭 더블 클릭으로 해제)`);
        }
      } else {
        orderMove(w);
        followIdx = selected;
        followPassive = true;
        toast(`${selected + 1}번 분대: 비전투로 커서 따라가기 — 우클릭 한 번 더: 전투 허용, 더블 클릭: 해제`);
      }
    }
  });
  // Follow without combat: the following squads neither shoot nor charge until released.
  let followPassive = false;
  function isPassive(s) {
    if (ceasefire) return true;
    if (!followPassive) return false;
    return followAll || (followIdx !== null && squads[followIdx] === s);
  }
  let lastDown = { button: -1, t: -1e9, x: 0, y: 0, released: false };
  // Group follow (avatar mode): rally at the clicked spot, then follow the cursor. Each squad's
  // order = point + its slot in a tight 2×2 cluster (each one still clamped to D by orderMove).
  let followAll = false;
  let gathering = false;          // true until every squad has reached the rally point
  let gatherAt = { x: 0, y: 0 };  // rally point (world)
  let gatherTime = 0;
  const RALLY = { slot: 70, arrive: 40, timeout: 8 };
  const groupSpread = [
    { x: -RALLY.slot, y: -RALLY.slot }, { x: RALLY.slot, y: -RALLY.slot },
    { x: -RALLY.slot, y: RALLY.slot }, { x: RALLY.slot, y: RALLY.slot },
  ];
  function groupOrder(w, feedback) {
    squads.forEach((s, i) => orderMove({ x: w.x + groupSpread.at(i).x, y: w.y + groupSpread.at(i).y }, feedback, i));
  }
  function stopFollow() { followIdx = null; followAll = false; gathering = false; followPassive = false; }
  // Keep tracking the cursor while following, even over the HUD panels.
  window.addEventListener('mousemove', e => {
    if (followIdx === null && !followAll) return;
    const p = eventPos(e);
    mouse.sx = p.x; mouse.sy = p.y;
  });
  // No browser context menu anywhere on the playfield.
  window.addEventListener('contextmenu', e => e.preventDefault());

  // ---------------------------------------------------------------- zoom
  // Zoom presets: avatar 1.75×; camera on a squad: 1.0× while that squad is within the limit
  // circle's radius (D/2) of the avatar, 0.6× beyond it. Changing preset eases together with the
  // camera pan. The mouse wheel adjusts the active preset (remembered). Zoom is about the screen center.
  let zoomGoal = CFG.zoom;
  let zoomEase = CFG.modeZoomFollow;
  let squadFar = false; // focused squad is beyond D/2 from the avatar (with hysteresis)
  const zoomKey = () => (camFocus === null ? 'zoomAvatar' : squadFar ? 'zoomSquadFar' : 'zoomSquadNear');
  const zoomUI = {
    zoomAvatar: { input: document.getElementById('t-zoom-avatar'), out: document.getElementById('o-zoom-avatar') },
    zoomSquadNear: { input: document.getElementById('t-zoom-near'), out: document.getElementById('o-zoom-near') },
    zoomSquadFar: { input: document.getElementById('t-zoom-far'), out: document.getElementById('o-zoom-far') },
  };
  // Re-evaluate near/far for the focused squad; switch preset when the band changes.
  function updateZoomBand(force) {
    if (camFocus === null) return;
    const d = squads[camFocus].dist, r = CFG.D / 2, h = force ? 0 : CFG.zoomBandHysteresis;
    const far = squadFar ? d > r - h : d > r + h;
    if (far !== squadFar || force) {
      squadFar = far;
      applyModeZoom();
    }
  }
  function syncZoomUI() {
    for (const k in zoomUI) {
      zoomUI[k].input.value = String(CFG[k]);
      zoomUI[k].out.textContent = `${CFG[k].toFixed(2)}×`;
    }
  }
  function applyModeZoom() {
    zoomGoal = CFG[zoomKey()];
    zoomEase = CFG.modeZoomFollow;
  }
  function setModeZoom(k, z, ease) {
    CFG[k] = clamp(z, CFG.zoomMin, CFG.zoomMax);
    if (k === zoomKey()) { zoomGoal = CFG[k]; zoomEase = ease; }
    syncZoomUI();
  }
  syncZoomUI(); // don't trust slider values the browser restored on reload
  canvas.addEventListener('wheel', e => {
    e.preventDefault(); // also blocks ctrl+wheel / pinch browser zoom over the playfield
    const dy = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaMode === 2 ? e.deltaY * VH : e.deltaY;
    const k = zoomKey();
    setModeZoom(k, CFG[k] * Math.exp(-dy * 0.0015), CFG.wheelZoomFollow); // ~14% per notch
  }, { passive: false });

  // ---------------------------------------------------------------- update
  function update(dt) {
    time += dt;
    const H = CFG.worldHalf;

    // Red squad: WASD always drives it, in both modes.
    let ix = (keys.has('KeyD') ? 1 : 0) - (keys.has('KeyA') ? 1 : 0);
    let iy = (keys.has('KeyS') ? 1 : 0) - (keys.has('KeyW') ? 1 : 0);
    const il = hyp(ix, iy);
    if (il > 0) { ix /= il; iy /= il; }
    const ra = damp(CFG.redAccel, dt);
    red.vx += (ix * CFG.redMaxSpeed - red.vx) * ra;
    red.vy += (iy * CFG.redMaxSpeed - red.vy) * ra;
    if (il === 0 && hyp(red.vx, red.vy) < 2) red.vx = red.vy = 0;
    const px = red.x, py = red.y;
    red.x = clamp(red.x + red.vx * dt, -H, H);
    red.y = clamp(red.y + red.vy * dt, -H, H);
    const rvx = (red.x - px) / dt, rvy = (red.y - py) / dt; // actual red velocity
    red.recoil = Math.max(0, red.recoil - dt * 6);
    const range = rangedRange();

    // Follow toggle on: the squad's order follows the cursor (resolved against the camera as
    // last drawn, same as a click).
    if (followIdx !== null) orderMove(screenToWorld(mouse.sx, mouse.sy), false, followIdx);
    else if (followAll && gathering) {
      // Rally phase: hold the rally point (in the world) until every squad has arrived.
      groupOrder(gatherAt, false);
      gatherTime += dt;
      const arrived = squads.every(s => hyp(red.x + s.offset.x - s.x, red.y + s.offset.y - s.y) < RALLY.arrive);
      if (arrived || gatherTime > RALLY.timeout) {
        gathering = false;
        toast('모든 분대: 집결 완료 — 커서 추적 시작');
      }
    } else if (followAll) groupOrder(screenToWorld(mouse.sx, mouse.sy), false);

    // Red members: follow the avatar in formation and auto-fire at enemies in range.
    // (The avatar itself only fires on left click.)
    updateHeading(red, rvx, rvy, dt);
    red.rangedTarget = nearestEnemy(red.x, red.y, en => hyp(en.x - red.x, en.y - red.y) <= range);
    updateMembers(red, rvx, rvy, { meleeTarget: null, rangedTarget: red.rangedTarget, shotKind: 'bolt', canFire: !ceasefire }, dt);

    // Blue squads: behaviour transitions, movement (always under the hard D limit), attacks.
    for (const s of squads) {
      s.stateTime += dt;
      s.fireCd -= dt;
      s.meleeCd -= dt;
      s.engageLock -= dt; // Infinity stays Infinity until the squad reaches its post
      s.lunge = Math.max(0, s.lunge - dt * 6);

      // --- transitions
      if (s.state === 'melee') {
        const e = s.target;
        if (isPassive(s)) {
          // Cease-fire / passive follow: break off and go back to the post.
          s.target = null;
          setState(s, 'return');
        } else if (!e || e.dead) {
          // Target down: re-engage another enemy in the circle, otherwise go back to the post.
          s.target = null;
          setState(s, 'return');
        } else if (!inLimitCircle(s, e.x, e.y, CFG.melee.leaveMargin)) {
          // Fight left the limit circle: fall back to the avatar, then return to the post.
          s.target = null;
          s.engageLock = Infinity;
          setState(s, 'retreat');
        }
      }
      if (!isPassive(s) && (s.state === 'hold' || s.state === 'return') && s.engageLock <= 0) {
        const e = nearestEnemy(s.x, s.y, en => inLimitCircle(s, en.x, en.y, 0));
        if (e) { s.target = e; setState(s, 'melee'); }
      }

      // --- movement target
      let tx, ty, carry = true;
      if (s.state === 'melee') {
        const e = s.target;
        const n = norm(s.x - e.x, s.y - e.y, { x: -s.dir.x, y: -s.dir.y });
        tx = e.x + n.x * CFG.melee.reach;
        ty = e.y + n.y * CFG.melee.reach;
        carry = false;
      } else if (s.state === 'retreat') {
        const n = norm(s.x - red.x, s.y - red.y, s.dir);
        tx = red.x + n.x * 50;
        ty = red.y + n.y * 50;
      } else {
        tx = red.x + s.offset.x;
        ty = red.y + s.offset.y;
      }

      // Carry red's motion (so the relative offset holds while red moves),
      // then smoothly correct toward the target. Then hard-enforce the D limit.
      const ox = s.x, oy = s.y;
      if (carry) {
        s.x += rvx * dt;
        s.y += rvy * dt;
      }
      const k = damp(CFG.steer, dt);
      let cx = (tx - s.x) * k, cy = (ty - s.y) * k;
      const cl = hyp(cx, cy), maxStep = CFG.steerMax * dt;
      if (cl > maxStep) { cx *= maxStep / cl; cy *= maxStep / cl; }
      s.x += cx;
      s.y += cy;

      let dx = s.x - red.x, dy = s.y - red.y, d = hyp(dx, dy);
      if (d > CFG.D) {
        s.x = red.x + (dx / d) * CFG.D;
        s.y = red.y + (dy / d) * CFG.D;
      }
      // World bounds (projection onto a box containing red never increases distance to red).
      s.x = clamp(s.x, -H, H);
      s.y = clamp(s.y, -H, H);
      dx = s.x - red.x; dy = s.y - red.y; d = hyp(dx, dy);
      s.dist = d;
      if (d > 0.5) s.dir = { x: dx / d, y: dy / d };

      s.vx = (s.x - ox) / dt;
      s.vy = (s.y - oy) / dt;
      if (s.facingHold > 0) s.facingHold -= dt;
      else if (hyp(s.vx, s.vy) > 25) s.facing = Math.atan2(s.vy, s.vx);
      s.flash = Math.max(0, s.flash - dt * 3);

      // --- arrival
      if (s.state === 'retreat' && d < 70) setState(s, 'return');
      if (s.state === 'return' &&
          (hyp(red.x + s.offset.x - s.x, red.y + s.offset.y - s.y) < 8 || s.stateTime > CFG.returnTimeout)) {
        setState(s, 'hold');
        if (s.engageLock === Infinity) s.engageLock = CFG.reengageDelay;
      }

      // --- attacks
      if (s.state === 'melee') {
        const e = s.target;
        if (hyp(e.x - s.x, e.y - s.y) <= CFG.melee.reach + 10) {
          s.facing = Math.atan2(e.y - s.y, e.x - s.x);
          s.facingHold = 0.3;
          if (s.meleeCd <= 0) {
            s.meleeCd = CFG.melee.cooldown;
            s.lunge = 1;
            s.attackedAt = time;
            effects.push({ type: 'slash', x: e.x, y: e.y, r: e.r + 10, a: Math.atan2(s.y - e.y, s.x - e.x), t: 0, dur: 0.22 });
            damage(e, CFG.melee.dmg, 'melee', s.x, s.y);
          }
        }
      }
      // Squad's ranged target (shared by leader and members); none while meleeing or retreating.
      s.rangedTarget = !isPassive(s) && (s.state === 'hold' || s.state === 'return')
        ? nearestEnemy(s.x, s.y, en => hyp(en.x - s.x, en.y - s.y) <= range)
        : null;
      if (s.rangedTarget && s.fireCd <= 0) {
        const e = s.rangedTarget;
        const a = Math.atan2(e.y - s.y, e.x - s.x);
        spawnProjectile('auto', s.x, s.y, a);
        s.fireCd = CFG.auto.cooldown;
        s.facing = a;
        s.facingHold = 0.4;
        s.firedAt = time;
        s.attackedAt = time;
      }

      // --- members act with the squad
      updateHeading(s, s.vx, s.vy, dt);
      updateMembers(s, s.vx, s.vy, {
        meleeTarget: s.state === 'melee' ? s.target : null,
        rangedTarget: s.rangedTarget,
        shotKind: 'auto',
        canFire: s.state !== 'retreat' && !isPassive(s),
      }, dt);
    }
    separateMembers();

    // Camera: target = red (avatar) or the live midpoint red↔selected (command).
    const sel = selected !== null ? squads[selected] : null;
    const fs = camFocus !== null ? squads[camFocus] : null;
    camTarget = fs ? camPointFor(fs) : { x: red.x, y: red.y };
    const ca = damp(CFG.camFollow, dt);
    cam.x += (camTarget.x - cam.x) * ca;
    cam.y += (camTarget.y - cam.y) * ca;
    updateZoomBand(false);
    // Mode / wheel zoom, eased in log space so in and out feel the same.
    if (Math.abs(Math.log(zoomGoal / CFG.zoom)) < 1e-4) CFG.zoom = zoomGoal;
    else CFG.zoom = Math.exp(Math.log(CFG.zoom) + Math.log(zoomGoal / CFG.zoom) * damp(zoomEase, dt));

    // Cursor in world space, re-resolved every frame against the moving camera.
    const mw = screenToWorld(mouse.sx, mouse.sy);
    mouse.wx = mw.x; mouse.wy = mw.y;
    if (selected === null && mouse.inside) red.aim = Math.atan2(mw.y - red.y, mw.x - red.x);

    // Enemies
    for (const e of enemies) {
      if (e.dead) {
        e.respawn -= dt;
        if (e.respawn <= 0) {
          e.dead = false; e.hp = e.maxHp;
          effects.push({ type: 'pop', x: e.x, y: e.y, t: 0, dur: 0.4 });
        }
        continue;
      }
      if (e.patrol) {
        const k = Math.sin(time * e.patrol.spd + e.phase) * e.patrol.amp;
        e.x = e.bx + e.patrol.ax * k;
        e.y = e.by + e.patrol.ay * k;
      }
      e.flash = Math.max(0, e.flash - dt * 6);
    }

    // Projectiles (not limited by D)
    for (let i = projectiles.length - 1; i >= 0; i--) {
      const p = projectiles[i];
      p.trail.push({ x: p.x, y: p.y });
      if (p.trail.length > (p.kind === 'skill' ? 12 : 6)) p.trail.shift();
      const ax = p.x, ay = p.y;
      const sp = hyp(p.vx, p.vy), stepLen = Math.min(sp * dt, p.left);
      p.x += (p.vx / sp) * stepLen; p.y += (p.vy / sp) * stepLen; p.left -= stepLen;
      let hit = null;
      for (const e of enemies) {
        if (!e.dead && segDist(e.x, e.y, ax, ay, p.x, p.y) < e.r + p.r) { hit = e; break; }
      }
      if (hit) { damage(hit, p.dmg, p.kind, ax - p.vx, ay - p.vy); projectiles.splice(i, 1); continue; }
      if (p.left <= 0) {
        effects.push({ type: 'fizzle', x: p.x, y: p.y, kind: p.kind, t: 0, dur: 0.3 });
        projectiles.splice(i, 1);
      }
    }

    for (let i = effects.length - 1; i >= 0; i--) {
      effects[i].t += dt;
      if (effects[i].t >= effects[i].dur) effects.splice(i, 1);
    }
  }

  // (fromX, fromY): where the hit came from — sparks fly back toward the attacker.
  function damage(e, dmg, kind, fromX, fromY) {
    e.hp -= dmg;
    e.flash = 1;
    const color = COL[kind];
    const back = Math.atan2(fromY - e.y, fromX - e.x);
    const hx = e.x + Math.cos(back) * e.r, hy = e.y + Math.sin(back) * e.r;
    const parts = [];
    for (let k = 0; k < 9; k++) {
      const a = back + (Math.random() - 0.5) * 2.2;
      const sp = 80 + Math.random() * 180;
      parts.push({ vx: Math.cos(a) * sp, vy: Math.sin(a) * sp });
    }
    effects.push({ type: 'spark', x: hx, y: hy, parts, color, t: 0, dur: 0.35 });
    effects.push({ type: 'text', x: e.x + (Math.random() - 0.5) * 16, y: e.y - 30, text: `-${dmg}`, color, t: 0, dur: 0.8 });
    if (e.hp <= 0) {
      e.dead = true;
      e.respawn = 2.5;
      effects.push({ type: 'burst', x: e.x, y: e.y, t: 0, dur: 0.5 });
    }
  }

  // ---------------------------------------------------------------- render
  function render() {
    const Z = CFG.zoom;
    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    ctx.fillStyle = COL.outside;
    ctx.fillRect(0, 0, VW, VH);

    // World space
    ctx.setTransform(DPR * Z, 0, 0, DPR * Z, DPR * (VW / 2 - cam.x * Z), DPR * (VH / 2 - cam.y * Z));
    drawGrid();
    drawEnemies();
    drawRanges();
    drawLimitCircles();
    drawDestinations();
    drawOrderPreview();
    drawSquads();
    drawRed();
    drawSquadArrows();
    drawProjectiles();
    drawCameraMarkers();
    drawAim();
    drawEffects();

    // Screen space
    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    drawCameraCenter();
  }

  function circle(x, y, r) {
    ctx.beginPath();
    ctx.arc(x, y, r, 0, TAU);
  }
  function line(ax, ay, bx, by) {
    ctx.beginPath();
    ctx.moveTo(ax, ay);
    ctx.lineTo(bx, by);
  }
  function cross(x, y, s) {
    ctx.beginPath();
    ctx.moveTo(x - s, y - s); ctx.lineTo(x + s, y + s);
    ctx.moveTo(x + s, y - s); ctx.lineTo(x - s, y + s);
  }

  function drawGrid() {
    const Z = CFG.zoom, H = CFG.worldHalf;
    const l = Math.max(-H, cam.x - VW / 2 / Z), r = Math.min(H, cam.x + VW / 2 / Z);
    const t = Math.max(-H, cam.y - VH / 2 / Z), b = Math.min(H, cam.y + VH / 2 / Z);
    ctx.fillStyle = COL.bg;
    ctx.fillRect(-H, -H, 2 * H, 2 * H);

    const lines = (step, color) => {
      ctx.beginPath();
      for (let x = Math.ceil(l / step) * step; x <= r; x += step) { ctx.moveTo(x, t); ctx.lineTo(x, b); }
      for (let y = Math.ceil(t / step) * step; y <= b; y += step) { ctx.moveTo(l, y); ctx.lineTo(r, y); }
      ctx.strokeStyle = color;
      ctx.lineWidth = 1;
      ctx.stroke();
    };
    lines(50, COL.gridMinor);
    lines(250, COL.gridMajor);
    ctx.beginPath();
    ctx.moveTo(0, t); ctx.lineTo(0, b);
    ctx.moveTo(l, 0); ctx.lineTo(r, 0);
    ctx.strokeStyle = COL.axis;
    ctx.stroke();

    // Coordinate labels on major lines for judging camera motion.
    ctx.fillStyle = 'rgba(0,0,0,0.28)';
    ctx.font = '10px ui-monospace, Consolas, monospace';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'top';
    for (let x = Math.ceil(l / 500) * 500; x <= r; x += 500) {
      for (let y = Math.ceil(t / 500) * 500; y <= b; y += 500) ctx.fillText(`${x},${y}`, x + 3, y + 3);
    }

    ctx.strokeStyle = 'rgba(0,0,0,0.45)';
    ctx.lineWidth = 3;
    ctx.strokeRect(-H, -H, 2 * H, 2 * H);
  }

  function drawEnemies() {
    for (const e of enemies) {
      if (e.dead) {
        ctx.setLineDash([4, 4]);
        circle(e.x, e.y, e.r);
        ctx.strokeStyle = 'rgba(0,0,0,0.25)';
        ctx.lineWidth = 1;
        ctx.stroke();
        ctx.setLineDash([]);
        continue;
      }
      ctx.save();
      ctx.translate(e.x, e.y);
      ctx.rotate(Math.PI / 4);
      const s = e.r * 0.82;
      ctx.fillStyle = e.flash > 0.4 ? '#ffffff' : COL.enemy;
      ctx.strokeStyle = COL.enemyEdge;
      ctx.lineWidth = 2;
      ctx.fillRect(-s, -s, 2 * s, 2 * s);
      ctx.strokeRect(-s, -s, 2 * s, 2 * s);
      ctx.restore();
      // target mark
      circle(e.x, e.y, 5);
      ctx.fillStyle = e.flash > 0.4 ? COL.enemy : '#f4f4f4';
      ctx.fill();
      // hp bar
      const w = 40, hpw = w * (e.hp / e.maxHp);
      ctx.fillStyle = 'rgba(0,0,0,0.15)';
      ctx.fillRect(e.x - w / 2, e.y - e.r - 14, w, 5);
      ctx.fillStyle = '#d44';
      ctx.fillRect(e.x - w / 2, e.y - e.r - 14, hpw, 5);
    }
  }

  // Ranged attack range: prominent for whoever the left click fires from, faint for other squads.
  function drawRanges() {
    const range = rangedRange();
    ctx.setLineDash([3, 6]);
    ctx.lineWidth = 1;
    if (CFG.showRanges) {
      squads.forEach((s, i) => {
        if (i === selected) return;
        circle(s.x, s.y, range);
        ctx.strokeStyle = 'rgba(28,18,234,0.16)';
        ctx.stroke();
      });
    }
    const src = selected === null ? red : squads[selected];
    const color = selected === null ? 'rgba(232,22,22,0.5)' : 'rgba(28,18,234,0.55)';
    circle(src.x, src.y, range);
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.3;
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.fillStyle = color;
    ctx.font = '10px system-ui, "Malgun Gothic", sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillText('사거리', src.x, src.y - range - 3);
  }

  // Limit circle: diameter = D, one edge on the red squad, extending toward the blue squad.
  // A blue squad on the red→blue ray is inside it exactly when its distance ≤ D.
  function drawLimitCircles() {
    // Selected: thin green ring through both squads (as in the concept sketch). Drawn first:
    // at distance == D it coincides with the black limit circle, which must stay readable.
    if (selected !== null) {
      const s = squads[selected];
      circle((red.x + s.x) / 2, (red.y + s.y) / 2, s.dist / 2);
      ctx.setLineDash([6, 4]);
      ctx.strokeStyle = 'rgba(31,174,34,0.75)';
      ctx.lineWidth = 1;
      ctx.stroke();
      ctx.setLineDash([]);
    }

    // Avatar mode: no limit circles (too busy) — squads are shown by ring arrows (drawSquadArrows).
    if (selected === null) return;

    const r = CFG.D / 2;
    const order = [0, 1, 2, 3].sort((a, b) => (a === selected) - (b === selected)); // selected drawn last
    for (const i of order) {
      const s = squads[i];
      const sel = i === selected;
      if (!sel && selected !== null && !CFG.showInactiveCircles) continue;
      const cx = red.x + s.dir.x * r, cy = red.y + s.dir.y * r;
      circle(cx, cy, r);
      if (sel) {
        ctx.fillStyle = 'rgba(0,0,0,0.035)';
        ctx.fill();
        ctx.strokeStyle = '#111';
        ctx.lineWidth = 1.6;
        ctx.stroke();

        // Dimension line: red → far edge = D
        const fx = red.x + s.dir.x * CFG.D, fy = red.y + s.dir.y * CFG.D;
        ctx.setLineDash([2, 5]);
        line(red.x, red.y, fx, fy);
        ctx.strokeStyle = 'rgba(0,0,0,0.35)';
        ctx.lineWidth = 1;
        ctx.stroke();
        ctx.setLineDash([]);
        const nx = -s.dir.y, ny = s.dir.x;
        line(fx - nx * 7, fy - ny * 7, fx + nx * 7, fy + ny * 7);
        ctx.strokeStyle = '#111';
        ctx.lineWidth = 1.6;
        ctx.stroke();
        ctx.fillStyle = '#111';
        ctx.font = '600 11px ui-monospace, Consolas, monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText('D', fx + s.dir.x * 12, fy + s.dir.y * 12);
        // circle center (intentionally distinct from the green camera target)
        ctx.beginPath();
        ctx.moveTo(cx - 4, cy); ctx.lineTo(cx + 4, cy);
        ctx.moveTo(cx, cy - 4); ctx.lineTo(cx, cy + 4);
        ctx.strokeStyle = 'rgba(0,0,0,0.4)';
        ctx.lineWidth = 1;
        ctx.stroke();
      } else {
        ctx.strokeStyle = selected === null ? 'rgba(0,0,0,0.32)' : 'rgba(0,0,0,0.12)';
        ctx.lineWidth = 1;
        ctx.stroke();
      }
    }
  }

  // Assigned relative position (moves with red) + path from squad to it.
  function drawDestinations() {
    squads.forEach((s, i) => {
      const sel = i === selected;
      const dx = red.x + s.offset.x, dy = red.y + s.offset.y;
      const gap = hyp(dx - s.x, dy - s.y);
      if (!sel && gap < 6) return;
      const a = sel ? 0.85 : 0.3;
      ctx.strokeStyle = `rgba(28,18,234,${a})`;
      ctx.lineWidth = 1.5;
      if (gap >= 6) {
        ctx.setLineDash([5, 5]);
        line(s.x, s.y, dx, dy);
        ctx.stroke();
      }
      ctx.setLineDash([3, 3]);
      circle(dx, dy, 22);
      ctx.stroke();
      ctx.setLineDash([]);
      cross(dx, dy, 3.5);
      ctx.stroke();
    });
  }

  // Ghost of where a right click would place the selected squad.
  function drawOrderPreview() {
    if (selected === null || !mouse.inside) return;
    let ox = mouse.wx - red.x, oy = mouse.wy - red.y;
    const d = hyp(ox, oy);
    const over = d > CFG.D;
    if (over) { ox *= CFG.D / d; oy *= CFG.D / d; }
    const gx = red.x + ox, gy = red.y + oy;
    circle(gx, gy, 16);
    ctx.fillStyle = 'rgba(28,18,234,0.08)';
    ctx.fill();
    ctx.strokeStyle = 'rgba(28,18,234,0.3)';
    ctx.lineWidth = 1;
    ctx.stroke();
    if (over) {
      ctx.setLineDash([3, 4]);
      line(gx, gy, mouse.wx, mouse.wy);
      ctx.strokeStyle = 'rgba(200,20,20,0.35)';
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = 'rgba(160,20,20,0.7)';
      ctx.font = '11px system-ui, "Malgun Gothic", sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'bottom';
      ctx.fillText('최대 D', gx, gy - 20);
    }
  }

  function drawMembers(list, fill, edge) {
    for (const m of list) {
      const x = m.x + Math.cos(m.facing) * m.lunge * 6, y = m.y + Math.sin(m.facing) * m.lunge * 6;
      circle(x, y, m.r);
      ctx.fillStyle = fill;
      ctx.fill();
      ctx.strokeStyle = edge;
      ctx.lineWidth = 1.5;
      ctx.stroke();
      // facing dot
      circle(x + Math.cos(m.facing) * m.r * 0.5, y + Math.sin(m.facing) * m.r * 0.5, 2.2);
      ctx.fillStyle = '#fff';
      ctx.fill();
    }
  }

  function drawSquads() {
    // Melee links first, so they sit under the squads
    for (const s of squads) {
      if (s.state !== 'melee' || !s.target) continue;
      const e = s.target;
      ctx.setLineDash([4, 4]);
      line(s.x, s.y, e.x, e.y);
      ctx.strokeStyle = 'rgba(28,18,234,0.5)';
      ctx.lineWidth = 1.5;
      ctx.stroke();
      ctx.setLineDash([]);
      circle(e.x, e.y, e.r + 7);
      ctx.strokeStyle = 'rgba(28,18,234,0.6)';
      ctx.lineWidth = 2;
      ctx.stroke();
    }

    squads.forEach((s, i) => {
      const sel = i === selected;
      // short lunge toward the facing direction on each melee strike
      const lx = Math.cos(s.facing) * s.lunge * 9, ly = Math.sin(s.facing) * s.lunge * 9;
      const x = s.x + lx, y = s.y + ly;
      drawMembers(s.members, COL.blue, COL.blueDark);
      if (sel) {
        const pr = 25 + Math.sin(time * 6) * 2;
        circle(x, y, pr);
        ctx.fillStyle = 'rgba(28,18,234,0.1)';
        ctx.fill();
        ctx.strokeStyle = COL.blue;
        ctx.lineWidth = 2.5;
        ctx.stroke();
      }

      // facing chevron
      ctx.save();
      ctx.translate(x, y);
      ctx.rotate(s.facing);
      ctx.beginPath();
      ctx.moveTo(25, 0); ctx.lineTo(17, -6); ctx.lineTo(17, 6); ctx.closePath();
      ctx.fillStyle = COL.blueDark;
      ctx.fill();
      ctx.restore();

      circle(x, y, 16);
      ctx.fillStyle = COL.blue;
      ctx.fill();
      ctx.strokeStyle = COL.blueDark;
      ctx.lineWidth = 2;
      ctx.stroke();
      ctx.fillStyle = '#fff';
      ctx.font = 'bold 15px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(String(s.id), x, y + 1);

      if (s.flash > 0) {
        circle(s.x, s.y, 16 + (1 - s.flash) * 28);
        ctx.strokeStyle = `rgba(0,168,255,${s.flash})`;
        ctx.lineWidth = 3;
        ctx.stroke();
      }

      // behaviour label
      const label = squadStatus(s);
      if (label !== '대기') {
        const lc = s.state === 'melee' ? '#1c12ea' : s.state === 'retreat' ? '#c0392b' : s.state === 'return' ? '#555' : '#3d5bd9';
        ctx.font = '600 11px system-ui, "Malgun Gothic", sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(255,255,255,0.9)';
        ctx.strokeText(label, s.x, s.y + 22);
        ctx.fillStyle = lc;
        ctx.fillText(label, s.x, s.y + 22);
      }
    });
  }

  function squadStatus(s) {
    const following = followAll || (followIdx !== null && squads[followIdx] === s);
    if (following && gathering) return followPassive ? '집결 중 (비전투)' : '집결 중';
    if (following) return followPassive ? '커서 추적 (비전투)' : '커서 추적';
    if (s.state === 'hold' && time - s.firedAt < 0.6) return '사격';
    return STATE_LABEL[s.state];
  }

  function drawRed() {
    drawMembers(red.members, COL.red, COL.redDark);
    const bl = 32 - red.recoil * 8;
    line(red.x, red.y, red.x + Math.cos(red.aim) * bl, red.y + Math.sin(red.aim) * bl);
    ctx.strokeStyle = COL.redDark;
    ctx.lineWidth = 6;
    ctx.lineCap = 'round';
    ctx.stroke();
    ctx.lineCap = 'butt';
    circle(red.x, red.y, 18);
    ctx.fillStyle = COL.red;
    ctx.fill();
    ctx.strokeStyle = COL.redDark;
    ctx.lineWidth = 2;
    ctx.stroke();
    // avatar marker
    circle(red.x, red.y, 9);
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 2;
    ctx.stroke();
  }

  // Avatar mode: one numbered arrow per squad on a ring around the avatar, pointing at its leader.
  //   - faint while the leader is on screen, solid when off screen
  //   - while the squad is attacking, one short out-and-back pulse every 2 s (staggered per squad)
  //   - blinks red/white while the squad is being hit
  //   - arrows that would overlap are spread apart along the ring (the head still points at the leader)
  // Sizes are screen pixels (constant at any zoom).
  const ARROW = { ring: 80, len: 20, half: 9, badge: 10, font: 12,attackWindow: 1.2, hitWindow: 1.5, pulse: 0.22, pulsePeriod: 2, minGap: 0.42 };

  // Spread angles (radians) so neighbours on the ring are at least `gap` apart.
  function spreadAngles(items, gap) {
    if (items.length < 2) return;
    for (let iter = 0; iter < 20; iter++) {
      items.sort((p, q) => p.pos - q.pos);
      let moved = false;
      for (let k = 0; k < items.length; k++) {
        const p = items[k], q = items[(k + 1) % items.length];
        let diff = q.pos - p.pos;
        if (k === items.length - 1) diff += TAU; // wrap-around pair
        if (diff < gap) {
          const push = (gap - diff) / 2;
          p.pos -= push; q.pos += push;
          moved = true;
        }
      }
      for (const p of items) p.pos = Math.atan2(Math.sin(p.pos), Math.cos(p.pos));
      if (!moved) break;
    }
  }

  // Always drawn (both modes), centered on the current camera target (green dot).
  // Screen-space UI: drawn in screen pixels around the camera point, so the ring radius, arrow
  // and badge keep the same on-screen size at any zoom (only the directions come from the world).
  function drawSquadArrows() {
    const c = camTarget, Z = CFG.zoom;
    ctx.save();
    ctx.translate(c.x, c.y);
    ctx.scale(1 / Z, 1 / Z); // 1 unit = 1 screen pixel from here on, origin at the camera point
    {
      const items = [];
      for (const s of squads) {
        if (camFocus !== null && squads[camFocus] === s) continue; // drawn big at the centre instead
        const dx = s.x - c.x, dy = s.y - c.y, d = hyp(dx, dy) * Z; // on-screen distance
        if (d < ARROW.ring + 30) continue; // squad is right next to the center
        const a = Math.atan2(dy, dx);
        items.push({ s, a, pos: a });
      }
      spreadAngles(items, ARROW.minGap);
      for (const { s, a, pos } of items) {
        const ux = Math.cos(pos), uy = Math.sin(pos);
        const sp = worldToScreen(s.x, s.y);
        const onScreen = sp.x > 0 && sp.x < VW && sp.y > 0 && sp.y < VH;
        const attacking = time - s.attackedAt < ARROW.attackWindow;
        const hit = time - s.hitAt < ARROW.hitWindow;
        const blinkOn = hit && Math.floor(time * 8) % 2 === 0;
        const phase = (time + s.id * 0.5) % ARROW.pulsePeriod; // once per period, staggered per squad
        const pulse = attacking && phase < ARROW.pulse ? Math.sin((phase / ARROW.pulse) * Math.PI) * 7 : 0;
        const r0 = ARROW.ring + pulse;

        const isSel = squads.indexOf(s) === selected;
        ctx.globalAlpha = hit || isSel || !onScreen ? 1 : 0.4;
        const fill = hit ? (blinkOn ? COL.red : '#ffffff') : COL.blue;
        const edge = hit ? COL.red : isSel ? COL.green : COL.blueDark;
        // arrow head
        ctx.save();
        ctx.translate(ux * r0, uy * r0);
        ctx.rotate(a);
        ctx.beginPath();
        ctx.moveTo(ARROW.len, 0);
        ctx.lineTo(0, -ARROW.half);
        ctx.lineTo(4, 0);
        ctx.lineTo(0, ARROW.half);
        ctx.closePath();
        ctx.fillStyle = fill;
        ctx.fill();
        ctx.strokeStyle = edge;
        ctx.lineWidth = isSel ? 2.5 : 1.5;
        ctx.stroke();
        ctx.restore();
        // number badge just inside the arrow
        const bx = ux * (r0 - ARROW.badge - 1), by = uy * (r0 - ARROW.badge - 1);
        circle(bx, by, ARROW.badge);
        ctx.fillStyle = fill;
        ctx.fill();
        ctx.strokeStyle = edge;
        ctx.stroke();
        ctx.fillStyle = fill === '#ffffff' ? COL.red : '#fff';
        ctx.font = `bold ${ARROW.font}px system-ui, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(String(s.id), bx, by + 0.5);
        ctx.globalAlpha = 1;
      }
    }

    // Camera on a squad: that squad's arrow, 1.5× the ring arrows, replaces the green dot at the
    // camera point — badge centred on the point, head pointing at the leader.
    if (camFocus !== null) {
      const s = squads[camFocus], k = 1.5;
      const a = Math.atan2(s.y - c.y, s.x - c.x);
      const attacking = time - s.attackedAt < ARROW.attackWindow;
      const hit = time - s.hitAt < ARROW.hitWindow;
      const blinkOn = hit && Math.floor(time * 8) % 2 === 0;
      const phase = (time + s.id * 0.5) % ARROW.pulsePeriod;
      const pulse = attacking && phase < ARROW.pulse ? Math.sin((phase / ARROW.pulse) * Math.PI) * 7 / k : 0;
      const fill = hit ? (blinkOn ? COL.red : '#ffffff') : COL.blue;
      const edge = hit ? COL.red : COL.green;
      ctx.save();
      ctx.scale(k, k);
      // head
      ctx.save();
      ctx.rotate(a);
      ctx.translate(ARROW.badge + 1 + pulse, 0);
      ctx.beginPath();
      ctx.moveTo(ARROW.len, 0);
      ctx.lineTo(0, -ARROW.half);
      ctx.lineTo(4, 0);
      ctx.lineTo(0, ARROW.half);
      ctx.closePath();
      ctx.fillStyle = fill;
      ctx.fill();
      ctx.strokeStyle = edge;
      ctx.lineWidth = 1.6;
      ctx.stroke();
      ctx.restore();
      // badge (kept upright)
      circle(0, 0, ARROW.badge);
      ctx.fillStyle = fill;
      ctx.fill();
      ctx.strokeStyle = edge;
      ctx.lineWidth = 1.6;
      ctx.stroke();
      ctx.fillStyle = fill === '#ffffff' ? COL.red : '#fff';
      ctx.font = `bold ${ARROW.font}px system-ui, sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(String(s.id), 0, 0.5);
      ctx.restore();
    }
    ctx.restore();
  }

  function drawProjectiles() {
    for (const p of projectiles) {
      const color = COL[p.kind];
      if (p.trail.length > 1) {
        for (let k = 1; k < p.trail.length; k++) {
          const a = k / p.trail.length;
          line(p.trail[k - 1].x, p.trail[k - 1].y, p.trail[k].x, p.trail[k].y);
          ctx.strokeStyle = color;
          ctx.globalAlpha = a * 0.6;
          ctx.lineWidth = p.r * 1.4 * a;
          ctx.stroke();
        }
        ctx.globalAlpha = 1;
      }
      if (p.kind === 'skill') {
        ctx.shadowColor = color;
        ctx.shadowBlur = 14;
      }
      circle(p.x, p.y, p.r);
      ctx.fillStyle = color;
      ctx.fill();
      ctx.shadowBlur = 0;
      circle(p.x, p.y, p.r * 0.45);
      ctx.fillStyle = '#fff';
      ctx.fill();
    }
  }

  function drawCameraMarkers() {
    // Midpoints of unselected squads (camera targets they would produce)
    if (CFG.showAllMidpoints) {
      squads.forEach((s, i) => {
        if (i === selected) return;
        const cp = camPointFor(s);
        circle(cp.x, cp.y, 3.5);
        ctx.strokeStyle = 'rgba(31,174,34,0.45)';
        ctx.lineWidth = 1.5;
        ctx.stroke();
      });
    }
    // Active camera target: green dot on the avatar; when the camera is on a squad, that squad's
    // big arrow marks the camera point instead (drawSquadArrows).
    if (camFocus === null) {
      circle(camTarget.x, camTarget.y, 6);
      ctx.fillStyle = COL.green;
      ctx.fill();
      ctx.strokeStyle = '#fff';
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }
    // Cease-fire: a red X, 50u across, on the camera point
    if (ceasefire) {
      cross(camTarget.x, camTarget.y, 25);
      ctx.strokeStyle = 'rgba(255,255,255,0.9)';
      ctx.lineWidth = 9;
      ctx.lineCap = 'round';
      ctx.stroke();
      ctx.strokeStyle = COL.red;
      ctx.lineWidth = 6;
      ctx.stroke();
      ctx.lineCap = 'butt';
    }
  }

  function drawAim() {
    if (!mouse.inside) return;
    const src = selected === null ? red : squads[selected];
    const color = selected === null ? 'rgba(232,22,22,0.4)' : 'rgba(0,168,255,0.75)';
    ctx.setLineDash([6, 6]);
    line(src.x, src.y, mouse.wx, mouse.wy);
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.stroke();
    ctx.setLineDash([]);
    // reticle
    const x = mouse.wx, y = mouse.wy;
    circle(x, y, 11);
    ctx.strokeStyle = selected === null ? COL.red : COL.skill;
    ctx.lineWidth = 1.5;
    ctx.stroke();
    ctx.beginPath();
    ctx.moveTo(x - 16, y); ctx.lineTo(x - 7, y);
    ctx.moveTo(x + 7, y); ctx.lineTo(x + 16, y);
    ctx.moveTo(x, y - 16); ctx.lineTo(x, y - 7);
    ctx.moveTo(x, y + 7); ctx.lineTo(x, y + 16);
    ctx.stroke();
  }

  function drawEffects() {
    for (const f of effects) {
      const k = f.t / f.dur, a = 1 - k;
      switch (f.type) {
        case 'order': {
          circle(f.x, f.y, 8 + 26 * (1 - k));
          ctx.strokeStyle = `rgba(28,18,234,${a})`;
          ctx.lineWidth = 2;
          ctx.stroke();
          if (f.clamped) {
            ctx.setLineDash([4, 4]);
            line(f.x, f.y, f.cx, f.cy);
            ctx.strokeStyle = `rgba(200,20,20,${a * 0.8})`;
            ctx.lineWidth = 1.5;
            ctx.stroke();
            ctx.setLineDash([]);
            cross(f.cx, f.cy, 7);
            ctx.lineWidth = 2.5;
            ctx.stroke();
            ctx.fillStyle = `rgba(200,20,20,${a})`;
            ctx.font = '600 11px system-ui, "Malgun Gothic", sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'bottom';
            ctx.fillText('D로 제한됨', f.x, f.y - 30);
          }
          break;
        }
        case 'aim': {
          cross(f.x, f.y, f.kind === 'skill' ? 7 : 5);
          ctx.strokeStyle = f.kind === 'skill' ? `rgba(0,140,220,${a})` : `rgba(230,90,0,${a})`;
          ctx.lineWidth = 2;
          ctx.stroke();
          break;
        }
        case 'muzzle': {
          circle(f.x, f.y, 5 + 14 * k);
          ctx.fillStyle = COL[f.kind];
          ctx.globalAlpha = a * 0.7;
          ctx.fill();
          ctx.globalAlpha = 1;
          break;
        }
        case 'spark': {
          ctx.strokeStyle = f.color;
          ctx.lineWidth = 2;
          ctx.globalAlpha = a;
          for (const p of f.parts) {
            const d = f.t * (1 - k * 0.5);
            line(f.x + p.vx * d, f.y + p.vy * d, f.x + p.vx * (d + 0.03), f.y + p.vy * (d + 0.03));
            ctx.stroke();
          }
          ctx.globalAlpha = 1;
          break;
        }
        case 'text': {
          ctx.globalAlpha = a;
          ctx.fillStyle = f.color;
          ctx.font = 'bold 15px system-ui, sans-serif';
          ctx.textAlign = 'center';
          ctx.textBaseline = 'middle';
          ctx.fillText(f.text, f.x, f.y - 28 * k);
          ctx.globalAlpha = 1;
          break;
        }
        case 'slash': {
          const w = f.small ? 0.6 : 1.0;
          ctx.beginPath();
          ctx.arc(f.x, f.y, f.r + 6 * k, f.a - w + k * 0.5 * w, f.a + w - k * 0.5 * w);
          ctx.strokeStyle = `rgba(28,18,234,${a})`;
          ctx.lineWidth = f.small ? 2.5 : 4;
          ctx.lineCap = 'round';
          ctx.stroke();
          ctx.lineCap = 'butt';
          break;
        }
        case 'burst': {
          circle(f.x, f.y, 20 + 45 * k);
          ctx.strokeStyle = `rgba(40,40,40,${a})`;
          ctx.lineWidth = 3;
          ctx.stroke();
          break;
        }
        case 'pop':
        case 'fizzle': {
          circle(f.x, f.y, (f.type === 'pop' ? 30 : 4) + 10 * k);
          ctx.strokeStyle = f.type === 'pop' ? `rgba(40,40,40,${a * 0.6})` : `rgba(120,120,120,${a})`;
          ctx.lineWidth = 1.5;
          ctx.stroke();
          break;
        }
      }
    }
  }

  // Actual camera center (screen center). Converges onto the camera point. While the camera is on a
  // squad, its ticks open up around the big squad badge so they never cover the number.
  function drawCameraCenter() {
    const x = VW / 2, y = VH / 2;
    const g = camFocus !== null ? ARROW.badge * 1.5 + 4 : 4, L = 8; // inner gap, tick length
    ctx.beginPath();
    ctx.moveTo(x - g - L, y); ctx.lineTo(x - g, y);
    ctx.moveTo(x + g, y); ctx.lineTo(x + g + L, y);
    ctx.moveTo(x, y - g - L); ctx.lineTo(x, y - g);
    ctx.moveTo(x, y + g); ctx.lineTo(x, y + g + L);
    ctx.strokeStyle = 'rgba(0,0,0,0.45)';
    ctx.lineWidth = 1.5;
    ctx.stroke();
  }

  // ---------------------------------------------------------------- HUD
  const $ = id => document.getElementById(id);
  const hud = {
    mode: $('r-mode'), sel: $('r-sel'), dist: $('r-dist'), d: $('r-d'),
    cam: $('r-cam'), lag: $('r-lag'), cursor: $('r-cursor'),
    range: $('r-range'), state: $('r-state'), zoom: $('r-zoom'),
    hAvatar: $('h-avatar'), hCmd: $('h-cmd'),
  };
  const setText = (el, v) => { if (el.textContent !== v) el.textContent = v; };

  const cardsEl = $('cards');
  const cards = squads.map((s, i) => {
    const el = document.createElement('div');
    el.className = 'card';
    el.innerHTML = `<div class="top"><span class="badge">${s.id}</span><span class="cd"></span></div><div class="bar"><i></i></div><div class="st"></div>`;
    el.addEventListener('mousedown', e => { e.preventDefault(); selectSquad(i); });
    cardsEl.appendChild(el);
    return { el, cd: el.querySelector('.cd'), st: el.querySelector('.st'), bar: el.querySelector('.bar i') };
  });

  function updateHud() {
    const s = selected !== null ? squads[selected] : null;
    setText(hud.mode, s ? '분대 지휘' : '아바타');
    hud.mode.className = s ? 'cmd' : 'avatar';
    setText(hud.sel, s ? `${s.id}번` : '—');
    setText(hud.dist, s ? `${s.dist.toFixed(1)} u` : '—');
    setText(hud.d, `${CFG.D} u`);
    setText(hud.range, `${rangedRange().toFixed(0)} u`);
    setText(hud.state, s ? squadStatus(s) : '—');
    setText(hud.cam, camFocus !== null ? `빨강→${camFocus + 1}번 ${Math.round(CFG.camSquadBias * 100)}% 지점` : '빨강 분대');
    setText(hud.lag, `${hyp(cam.x - camTarget.x, cam.y - camTarget.y).toFixed(1)} u`);
    setText(hud.zoom, `${CFG.zoom.toFixed(2)}×`);    setText(hud.cursor, mouse.inside ? `${mouse.wx.toFixed(0)}, ${mouse.wy.toFixed(0)}` : '—');
    hud.hAvatar.classList.toggle('active', !s);
    hud.hCmd.classList.toggle('active', !!s);
    squads.forEach((sq, i) => {
      const c = cards[i];
      c.el.classList.toggle('sel', i === selected);
      setText(c.cd, `${Math.round(sq.dist)}/${CFG.D}`);
      const st = squadStatus(sq);
      setText(c.st, st);
      c.st.dataset.state = st === '사격' ? 'fire' : sq.state;
      const f = Math.min(1, sq.dist / CFG.D);
      c.bar.style.width = `${(f * 100).toFixed(1)}%`;
      c.bar.classList.toggle('max', f > 0.995);
    });
  }

  // ---------------------------------------------------------------- tuning
  function bindRange(id, outId, fmt, apply) {
    const input = $(id), out = $(outId);
    input.addEventListener('input', () => { const v = Number(input.value); out.textContent = fmt(v); apply(v); });
    input.addEventListener('change', () => input.blur());
  }
  bindRange('t-d', 'o-d', v => String(v), v => {
    CFG.D = v;
    for (const s of squads) {
      const l = hyp(s.offset.x, s.offset.y);
      if (l > v) { s.offset.x *= v / l; s.offset.y *= v / l; }
    }
  });
  bindRange('t-range', 'o-range', v => `${v.toFixed(2)}×`, v => { CFG.rangeMul = v; });
  bindRange('t-spacing', 'o-spacing', v => `${v.toFixed(1)}×`, v => { CFG.member.spacing = v; });
  bindRange('t-bias', 'o-bias', v => `${Math.round(v * 100)}%`, v => { CFG.camSquadBias = v; });
  bindRange('t-steermax', 'o-steermax', v => String(v), v => { CFG.steerMax = v; });
  bindRange('t-zoom-avatar', 'o-zoom-avatar', v => `${v.toFixed(2)}×`, v => setModeZoom('zoomAvatar', v, CFG.wheelZoomFollow));
  bindRange('t-zoom-near', 'o-zoom-near', v => `${v.toFixed(2)}×`, v => setModeZoom('zoomSquadNear', v, CFG.wheelZoomFollow));
  bindRange('t-zoom-far', 'o-zoom-far', v => `${v.toFixed(2)}×`, v => setModeZoom('zoomSquadFar', v, CFG.wheelZoomFollow));
  bindRange('t-cam', 'o-cam', v => v.toFixed(1), v => { CFG.camFollow = v; });
  bindRange('t-steer', 'o-steer', v => v.toFixed(1), v => { CFG.steer = v; });
  $('t-circles').addEventListener('change', e => { CFG.showInactiveCircles = e.target.checked; e.target.blur(); });
  $('t-mids').addEventListener('change', e => { CFG.showAllMidpoints = e.target.checked; e.target.blur(); });  $('t-ranges').addEventListener('change', e => { CFG.showRanges = e.target.checked; e.target.blur(); });
  // Hit test: marks one random squad as hit every 0.4 s for 3 s (arrow blink preview).
  $('t-hit').addEventListener('click', e => {
    e.currentTarget.blur();
    const i = Math.floor(Math.random() * squads.length);
    let n = 0;
    const tick = () => { squadHit(i); if (++n < 8) setTimeout(tick, 400); };
    tick();
    toast(`피격 테스트: ${i + 1}번 분대`);
  });
  $('t-reset').addEventListener('click', e => { resetPositions(); e.target.blur(); });

  // ---------------------------------------------------------------- loop
  let last = performance.now();
  function frame(now) {
    const dt = Math.min(0.05, Math.max(0.001, (now - last) / 1000));
    last = now;
    update(dt);
    render();
    updateHud();
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  // Debug handle for inspection from the console.
  window.__babel = {
    CFG, red, squads, cam, enemies, projectiles, mouse,
    get selected() { return selected; },
    get camTarget() { return camTarget; },
    get zoomGoal() { return zoomGoal; },
    screenToWorld, worldToScreen, rangedRange, inLimitCircle, squadHit,
    get time() { return time; },
    get ceasefire() { return ceasefire; },
    // Advance the simulation manually (useful when the tab is hidden and rAF is paused).
    step(seconds, dt = 1 / 60) {
      for (let t = 0; t < seconds - 1e-9; t += dt) update(dt);
      render();
      updateHud();
    },
  };
})();
