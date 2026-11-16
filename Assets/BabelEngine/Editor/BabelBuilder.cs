using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Babel.EditorTools
{
    // 에셋 생성 방식: 메뉴를 실행하면 설정·종족·머티리얼·프리팹·씬이 프로젝트에 저장된다.
    // Data/ 의 에셋(BattleConfig, 종족)은 이미 있으면 수치를 보존한다(튜닝 값 유지). 프리팹 연결만 다시 건다.
    public static class BabelBuilder
    {
        const string Root = "Assets/BabelEngine";
        const string Lit = "Universal Render Pipeline/Lit";
        const string Unlit = "Universal Render Pipeline/Unlit";

        enum Look { Block, Tall, Horned, Dummy }

        class RaceSpec
        {
            public string key, displayName, description;
            public Color color;
            public Look look;
            public float hp, speed, tether, range, melee, ranged;
            public bool noRetreat, avoidMelee;
            public int team;
            public bool player;
            public Vector2 pos;
            public float heading;
        }

        static readonly RaceSpec[] Races =
        {
            new RaceSpec { key = "Metaphos", displayName = "메타포스", color = new Color(0.50f, 0.45f, 0.36f), look = Look.Block,
                description = "석판 중장갑. 뭉쳐서 밀고 가는 벽: 체력이 높고 근접이 강하지만 느리고 분대가 멀리 못 간다.",
                hp = 1.6f, speed = 0.8f, tether = 0.7f, range = 0.6f, melee = 1.5f, ranged = 0.8f,
                team = 0, player = true, pos = new Vector2(0, -1900), heading = 90f },
            new RaceSpec { key = "Tanarc", displayName = "타나르크", color = new Color(0.20f, 0.50f, 0.95f), look = Look.Tall,
                description = "흰 장갑과 푸른 회로. 넓게 펼쳐 쏜다: 사거리와 사격이 강하지만 체력이 낮고 근접을 피한다.",
                hp = 0.8f, speed = 1.1f, tether = 1.25f, range = 1.2f, melee = 0.7f, ranged = 1.4f, avoidMelee = true,
                team = 1, pos = new Vector2(-1500, 900), heading = 0f },
            new RaceSpec { key = "Demon", displayName = "악마", color = new Color(0.75f, 0.12f, 0.10f), look = Look.Horned,
                description = "혼돈·파괴·종말의 악마. 물면 안 놓는 돌격: 근접과 속도가 높고 물러나지 않는다.",
                hp = 1.0f, speed = 1.25f, tether = 1.0f, range = 0.7f, melee = 1.6f, ranged = 0.6f, noRetreat = true,
                team = 2, pos = new Vector2(1500, 900), heading = 180f },
        };

        [MenuItem("Babel/전장 빌드 (종족 군단)")]
        public static void BuildBattle()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Babel] 플레이 모드를 끄고 실행하세요."); return; }
            foreach (var d in new[] { "Data", "Materials", "Prefabs", "Scenes", "Textures" })
                Directory.CreateDirectory($"{Root}/{d}");
            AssetDatabase.Refresh();

            // 이전 단계의 잔재
            foreach (var old in new[]
            {
                "Materials/Commander_Red.mat", "Materials/Army_Red.mat", "Materials/Army_Blue.mat",
                "Prefabs/Commander.prefab", "Prefabs/CommanderMember.prefab", "Prefabs/SquadLeader.prefab", "Prefabs/SquadMember.prefab",
            })
                AssetDatabase.DeleteAsset($"{Root}/{old}");

            // 씬을 먼저 연다. 나중에 열면 그 순간 방금 만든 프리팹 참조가 메모리에서 내려간다.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cfg = LoadOrCreate<BattleConfig>($"{Root}/Data/BattleConfig.asset");

            // --- 공통 머티리얼
            var groundMat = Mat("Ground", Lit, Color.white);
            groundMat.SetTexture("_BaseMap", GridTexture());
            float tiles = cfg.worldHalf * 2f / 250f;
            groundMat.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            groundMat.SetFloat("_Smoothness", 0f);
            var enemyMat = Mat("Enemy", Lit, new Color(0.29f, 0.29f, 0.29f));
            var noseMat = Mat("Nose", Lit, Color.white);
            var goldMat = Mat("Commander_Gold", Lit, new Color(1f, 0.82f, 0.15f));
            var lineMat = Mat("Line", "Sprites/Default", Color.white);
            Color boltC = new Color(1f, 0.42f, 0f), skillC = new Color(0f, 0.66f, 1f), autoC = new Color(0.36f, 0.49f, 1f);

            // --- 종족별 프리팹과 종족 에셋
            float ms = cfg.memberScale;
            var raceAssets = new RaceDefinition[Races.Length];
            for (int i = 0; i < Races.Length; i++)
            {
                RaceSpec sp = Races[i];
                var mat = Mat($"Race_{sp.key}", Lit, sp.color);
                var dark = Mat($"Race_{sp.key}_Dark", Lit, new Color(sp.color.r * 0.45f, sp.color.g * 0.45f, sp.color.b * 0.45f));
                string path = $"{Root}/Data/Race_{sp.key}.asset";
                bool fresh = AssetDatabase.LoadAssetAtPath<RaceDefinition>(path) == null;
                var race = LoadOrCreate<RaceDefinition>(path);
                race.commanderPrefab = UnitPrefab($"{sp.key}_Commander", cfg.commanderRadius, mat, dark, goldMat, sp.look, 0f, cfg.commanderHp, true);
                race.leaderPrefab = UnitPrefab($"{sp.key}_Leader", cfg.squadLeaderRadius, mat, dark, null, sp.look, 9f, cfg.leaderHp, false);
                race.memberPrefab = UnitPrefab($"{sp.key}_Member", cfg.squadLeaderRadius * ms, mat, dark, null, sp.look, 6f, cfg.memberHp, false);
                race.color = sp.color; // 색은 머티리얼과 항상 맞춘다
                if (string.IsNullOrEmpty(race.description)) race.description = sp.description;
                if (fresh)
                {
                    race.raceName = sp.displayName;
                    race.hpMul = sp.hp; race.speedMul = sp.speed; race.tetherMul = sp.tether; race.rangeMul = sp.range;
                    race.meleeDamageMul = sp.melee; race.rangedDamageMul = sp.ranged;
                    race.noRetreat = sp.noRetreat; race.avoidMelee = sp.avoidMelee;
                }
                EditorUtility.SetDirty(race);
                raceAssets[i] = race;
            }

            Unit enemyPrefab = UnitPrefab("EnemyDummy", cfg.enemyRadius, enemyMat, null, null, Look.Dummy, 0f, cfg.enemyHp, false);
            GameObject bolt = ShotPrefab(cfg, "Shot_Bolt", cfg.bolt.radius, Mat("Shot_Bolt", Unlit, boltC), lineMat, boltC);
            GameObject skill = ShotPrefab(cfg, "Shot_Skill", cfg.skill.radius, Mat("Shot_Skill", Unlit, skillC), lineMat, skillC);
            GameObject auto = ShotPrefab(cfg, "Shot_Auto", cfg.auto.radius, Mat("Shot_Auto", Unlit, autoC), lineMat, autoC);

            // --- 씬 내용
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            float size = cfg.U(cfg.worldHalf) * 2f / 10f;
            ground.transform.localScale = new Vector3(size, 1f, size);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            var cam = Camera.main;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.86f, 0.86f);
            cam.farClipPlane = 500f;
            var bc = cam.gameObject.AddComponent<BattleCamera>();
            bc.config = cfg;
            bc.Apply(Races[0].pos, cfg.zoomAvatar);

            var worldGo = new GameObject("BattleWorld");
            var world = worldGo.AddComponent<BattleWorld>();
            var player = worldGo.AddComponent<PlayerController>();
            var overlay = worldGo.AddComponent<BattleOverlay>();
            var hud = worldGo.AddComponent<BattleHud>();
            var select = worldGo.AddComponent<RaceSelectScreen>();
            world.config = cfg;
            world.boltPrefab = bolt;
            world.skillPrefab = skill;
            world.autoPrefab = auto;
            world.player = player;
            world.races = raceAssets;
            select.world = world;
            world.battleCamera = bc;
            overlay.world = world;
            overlay.lineMaterial = lineMat;
            hud.world = world;

            // 군단 배치점: 플레이어 메타포스 + AI 둘(서로 더 가까워 먼저 맞붙는다). 더미 적은 놓지 않는다.
            var spawnRoot = new GameObject("군단 배치").transform;
            for (int i = 0; i < Races.Length; i++)
            {
                RaceSpec sp = Races[i];
                var go = new GameObject($"배치 · {sp.displayName}");
                go.transform.SetParent(spawnRoot, false);
                go.transform.position = new Vector3(cfg.U(sp.pos.x), 0f, cfg.U(sp.pos.y));
                var spawn = go.AddComponent<ArmySpawn>();
                spawn.race = raceAssets[i];
                spawn.team = sp.team;
                spawn.playerControlled = sp.player;
                spawn.headingDeg = sp.heading;
            }

            string scenePath = $"{Root}/Scenes/Battle.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) }; // R 재시작에 필요
            AssetDatabase.SaveAssets();
            Debug.Log("[Babel] 전장 빌드 완료: 종족 3개(프리팹 9 + 에셋 3), 군단 배치 3개, Battle.unity");
        }

        // 유닛 프리팹: 루트(Unit) > Visual > Body(+Nose 등). 루트는 바닥에 있고 +Z 가 정면.
        // 임시 외형: 메타포스 = 각진 블록, 타나르크 = 길쭉한 캡슐, 악마 = 캡슐 + 뿔. 지휘관은 머리 위 금색 구.
        static Unit UnitPrefab(string name, float radius, Material mat, Material dark, Material gold, Look look, float lunge, float hp, bool commander)
        {
            float r = radius * 0.05f; // BattleConfig.unitScale 기본값. 프리팹 크기용이라 정확히 같을 필요는 없다.
            var cfg = AssetDatabase.LoadAssetAtPath<BattleConfig>($"{Root}/Data/BattleConfig.asset");
            if (cfg != null) r = cfg.U(radius);

            var root = new GameObject(name);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            PrimitiveType shape = look == Look.Dummy || look == Look.Block ? PrimitiveType.Cube : PrimitiveType.Capsule;
            var body = GameObject.CreatePrimitive(shape);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(visual.transform, false);
            float top; // 몸체 꼭대기 높이
            switch (look)
            {
                case Look.Dummy:
                    body.transform.localScale = Vector3.one * (r * 1.64f);
                    body.transform.localPosition = new Vector3(0f, r * 0.82f, 0f);
                    body.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    top = r * 1.64f;
                    break;
                case Look.Block:
                    body.transform.localScale = new Vector3(r * 2.1f, r * 2.8f, r * 1.7f);
                    body.transform.localPosition = new Vector3(0f, r * 1.4f, 0f);
                    top = r * 2.8f;
                    break;
                case Look.Tall:
                    body.transform.localScale = new Vector3(r * 1.3f, r * 2.4f, r * 1.3f);
                    body.transform.localPosition = new Vector3(0f, r * 2.4f, 0f);
                    top = r * 4.8f;
                    break;
                default: // Horned
                    body.transform.localScale = Vector3.one * (r * 2f);
                    body.transform.localPosition = new Vector3(0f, r * 2f, 0f);
                    top = r * 4f;
                    break;
            }
            var rend = body.GetComponent<Renderer>();
            rend.sharedMaterial = mat;

            if (look == Look.Horned)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var horn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    horn.name = "Horn";
                    Object.DestroyImmediate(horn.GetComponent<Collider>());
                    horn.transform.SetParent(visual.transform, false);
                    horn.transform.localScale = new Vector3(r * 0.35f, r * 0.7f, r * 0.35f);
                    horn.transform.localPosition = new Vector3(s * r * 0.65f, top + r * 0.1f, 0f);
                    horn.transform.localRotation = Quaternion.Euler(0f, 0f, -s * 28f);
                    horn.GetComponent<Renderer>().sharedMaterial = dark != null ? dark : mat;
                }
            }
            if (look != Look.Dummy)
            {
                // 정면 표식(코): 어느 쪽을 보는지 알 수 있게 한다.
                var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nose.name = "Nose";
                Object.DestroyImmediate(nose.GetComponent<Collider>());
                nose.transform.SetParent(visual.transform, false);
                nose.transform.localScale = Vector3.one * (r * 0.7f);
                nose.transform.localPosition = new Vector3(0f, top * 0.65f, r * 0.9f);
                nose.GetComponent<Renderer>().sharedMaterial = dark != null ? dark : mat;
            }
            if (commander && gold != null)
            {
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Crown";
                Object.DestroyImmediate(crown.GetComponent<Collider>());
                crown.transform.SetParent(visual.transform, false);
                crown.transform.localScale = Vector3.one * (r * 0.9f);
                crown.transform.localPosition = new Vector3(0f, top + r * (look == Look.Horned ? 1.1f : 0.8f), 0f);
                crown.GetComponent<Renderer>().sharedMaterial = gold;
            }

            var unit = root.AddComponent<Unit>();
            unit.radius = radius;
            unit.maxHp = hp;
            unit.hp = hp;
            unit.lungeDistance = lunge;
            unit.visual = visual.transform;
            unit.bodyRenderer = rend;
            if (look == Look.Dummy) root.AddComponent<DummyEnemy>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/Prefabs/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Unit>();
        }

        static GameObject ShotPrefab(BattleConfig cfg, string name, float radius, Material mat, Material lineMat, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            float d = cfg.U(radius) * 2f * 1.6f;
            go.transform.localScale = Vector3.one * d;
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.12f;
            tr.startWidth = d;
            tr.endWidth = 0f;
            tr.minVertexDistance = 0.1f;
            tr.sharedMaterial = lineMat;
            tr.startColor = c;
            tr.endColor = new Color(c.r, c.g, c.b, 0f);
            tr.shadowCastingMode = ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // 바닥 격자: 한 장이 250u, 50u 마다 옅은 선.
        static Texture2D GridTexture()
        {
            string path = $"{Root}/Textures/Grid.png";
            if (!File.Exists(path))
            {
                const int n = 250;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                Color bg = new Color(0.957f, 0.957f, 0.957f), minor = new Color(0.88f, 0.88f, 0.88f), major = new Color(0.72f, 0.72f, 0.72f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        t.SetPixel(x, y, x < 2 || y < 2 ? major : (x % 50 == 0 || y % 50 == 0) ? minor : bg);
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.anisoLevel = 8;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static Material Mat(string name, string shader, Color c)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
