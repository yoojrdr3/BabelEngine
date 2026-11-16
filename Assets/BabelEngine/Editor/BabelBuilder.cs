using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Babel.EditorTools
{
    // 에셋 생성 방식: 메뉴를 실행하면 설정·머티리얼·프리팹·씬이 프로젝트에 저장된다.
    // BattleConfig.asset 은 이미 있으면 건드리지 않는다(튜닝 값 보존). 나머지는 다시 만든다.
    public static class BabelBuilder
    {
        const string Root = "Assets/BabelEngine";
        const string Lit = "Universal Render Pipeline/Lit";
        const string Unlit = "Universal Render Pipeline/Unlit";

        struct EnemyDef { public Vector2 pos, axis; public float amp, spd; }

        // 웹 프로토타입 ENEMY_DEFS (y 는 Unity 쪽 방향에 맞춰 뒤집음)
        static readonly EnemyDef[] Enemies =
        {
            new EnemyDef { pos = new Vector2(1120, 520) },
            new EnemyDef { pos = new Vector2(1440, -400), axis = new Vector2(0, -1), amp = 300, spd = 0.9f },
            new EnemyDef { pos = new Vector2(-1160, 760) },
            new EnemyDef { pos = new Vector2(-880, -1000), axis = new Vector2(1, 0), amp = 340, spd = 0.7f },
            new EnemyDef { pos = new Vector2(320, -1320) },
            new EnemyDef { pos = new Vector2(2000, 1160), axis = new Vector2(1, 0), amp = 240, spd = 1.1f },
            new EnemyDef { pos = new Vector2(-2040, -240) },
            new EnemyDef { pos = new Vector2(120, 1520), axis = new Vector2(1, 0), amp = 440, spd = 0.5f },
        };

        [MenuItem("Babel/전장 빌드 (군단 + 더미 적)")]
        public static void BuildBattle()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Babel] 플레이 모드를 끄고 실행하세요."); return; }
            foreach (var d in new[] { "Data", "Materials", "Prefabs", "Scenes", "Textures" })
                Directory.CreateDirectory($"{Root}/{d}");
            AssetDatabase.Refresh();
            AssetDatabase.DeleteAsset($"{Root}/Materials/Commander_Red.mat"); // 이전 빌드의 잔재

            // 씬을 먼저 연다. 나중에 열면 그 순간 방금 만든 프리팹 참조가 메모리에서 내려간다.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cfg = LoadOrCreate<BattleConfig>($"{Root}/Data/BattleConfig.asset");

            // --- 머티리얼
            var groundMat = Mat("Ground", Lit, Color.white);
            groundMat.SetTexture("_BaseMap", GridTexture());
            float tiles = cfg.worldHalf * 2f / 250f;
            groundMat.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            groundMat.SetFloat("_Smoothness", 0f);
            var redMat = Mat("Army_Red", Lit, new Color(0.91f, 0.09f, 0.09f));
            var blueMat = Mat("Army_Blue", Lit, new Color(0.11f, 0.07f, 0.92f));
            var enemyMat = Mat("Enemy", Lit, new Color(0.29f, 0.29f, 0.29f));
            var noseMat = Mat("Nose", Lit, Color.white);
            var lineMat = Mat("Line", "Sprites/Default", Color.white);
            Color boltC = new Color(1f, 0.42f, 0f), skillC = new Color(0f, 0.66f, 1f), autoC = new Color(0.36f, 0.49f, 1f);

            // --- 프리팹
            float ms = cfg.memberScale;
            var prefabs = new ArmyPrefabs
            {
                commander = UnitPrefab(cfg, "Commander", cfg.commanderRadius, redMat, noseMat, false, 0f, 100f),
                commanderMember = UnitPrefab(cfg, "CommanderMember", cfg.commanderRadius * ms, redMat, noseMat, false, 6f, 30f),
                squadLeader = UnitPrefab(cfg, "SquadLeader", cfg.squadLeaderRadius, blueMat, noseMat, false, 9f, 60f),
                squadMember = UnitPrefab(cfg, "SquadMember", cfg.squadLeaderRadius * ms, blueMat, noseMat, false, 6f, 30f),
            };
            Unit enemyPrefab = UnitPrefab(cfg, "EnemyDummy", cfg.enemyRadius, enemyMat, null, true, 0f, cfg.enemyHp);
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
            bc.Apply(Vector2.zero, cfg.zoomAvatar);

            var worldGo = new GameObject("BattleWorld");
            var world = worldGo.AddComponent<BattleWorld>();
            var player = worldGo.AddComponent<PlayerController>();
            var overlay = worldGo.AddComponent<BattleOverlay>();
            var hud = worldGo.AddComponent<BattleHud>();
            world.config = cfg;
            world.playerPrefabs = prefabs;
            world.boltPrefab = bolt;
            world.skillPrefab = skill;
            world.autoPrefab = auto;
            world.player = player;
            world.battleCamera = bc;
            overlay.world = world;
            overlay.lineMaterial = lineMat;
            hud.world = world;

            var enemyRoot = new GameObject("더미 적").transform;
            for (int i = 0; i < Enemies.Length; i++)
            {
                EnemyDef def = Enemies[i];
                var e = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab.gameObject);
                e.name = $"더미 적 {i + 1}";
                e.transform.SetParent(enemyRoot, false);
                e.transform.position = new Vector3(cfg.U(def.pos.x), 0f, cfg.U(def.pos.y));
                var de = e.GetComponent<DummyEnemy>();
                de.patrolAxis = def.axis;
                de.patrolAmp = def.amp;
                de.patrolSpeed = def.spd;
                de.phase = i * 1.3f;
            }

            EditorSceneManager.SaveScene(scene, $"{Root}/Scenes/Battle.unity");
            AssetDatabase.SaveAssets();
            Debug.Log("[Babel] 전장 빌드 완료: 프리팹 8개, Battle.unity");
        }

        // 유닛 프리팹: 루트(Unit) > Visual > Body(+Nose). 루트는 바닥에 있고 +Z 가 정면.
        static Unit UnitPrefab(BattleConfig cfg, string name, float radius, Material mat, Material noseMat, bool cube, float lunge, float hp)
        {
            float r = cfg.U(radius);
            var root = new GameObject(name);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var body = GameObject.CreatePrimitive(cube ? PrimitiveType.Cube : PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(visual.transform, false);
            if (cube)
            {
                body.transform.localScale = Vector3.one * (r * 1.64f);
                body.transform.localPosition = new Vector3(0f, r * 0.82f, 0f);
                body.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            }
            else
            {
                body.transform.localScale = Vector3.one * (r * 2f);
                body.transform.localPosition = new Vector3(0f, r * 2f, 0f);
                var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nose.name = "Nose";
                Object.DestroyImmediate(nose.GetComponent<Collider>());
                nose.transform.SetParent(visual.transform, false);
                nose.transform.localScale = Vector3.one * (r * 0.7f);
                nose.transform.localPosition = new Vector3(0f, r * 2.7f, r * 0.9f);
                nose.GetComponent<Renderer>().sharedMaterial = noseMat;
            }
            var rend = body.GetComponent<Renderer>();
            rend.sharedMaterial = mat;

            var unit = root.AddComponent<Unit>();
            unit.radius = radius;
            unit.maxHp = hp;
            unit.hp = hp;
            unit.lungeDistance = lunge;
            unit.visual = visual.transform;
            unit.bodyRenderer = rend;
            if (cube) root.AddComponent<DummyEnemy>();

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
