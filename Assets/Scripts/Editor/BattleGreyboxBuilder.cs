using System.Collections.Generic;
using TideBorne.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TideBorne.EditorTools
{
    /// <summary>
    /// 程序 C 的最小可跑战场：一张灰盒地图 + 玩家 + 跟随相机。
    ///
    /// 用代码造场景而不是手写 <c>.unity</c> YAML：YAML 里的 fileID 与 GUID 一旦写错，
    /// Unity 打开时是静默丢组件，极难排查；走 Unity 自己的 API 由它生成，结果一定自洽。
    /// </summary>
    public static class BattleGreyboxBuilder
    {
        private const string MenuPath = "Tools/程序C/生成战场灰盒场景";
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/Battle_Greybox.unity";
        private const string MarkerPath = SceneFolder + "/Battle_Greybox.generated.txt";
        private const string SquareSpriteAssetPath = "Assets/Art/Textures/white_16.png";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string MoveActionName = "Move";
        private const string SprintActionName = "Sprint";

        /// <summary>
        /// 生成器版本。**每次改动场景结构都要加一**——否则自动生成不会重建，
        /// 已经生成的旧场景会一直留着（初版的「玩家出生在地面碰撞体内部」就是这么漏出去的）。
        ///
        /// 版本历史：
        /// 1 → 初版（玩家埋在地面碰撞体里，会飞走）
        /// 2 → 地面去掉碰撞体、加边界墙、改方碰撞体
        /// 3 → 共用灰盒精灵、接上刷怪器精灵引用、加开火闪烁与刷怪诊断
        /// 4 → 精灵落成资产（不再嵌运行时对象）、障碍物提亮并抬 sortingOrder、敌人发射可见弹道
        /// 5 → 弹体改为出射定轨直飞、敌人停在射程上开火、加屏幕右上角波次倒计时 HUD
        /// 6 → 加左上角调试 HUD（镜头偏移判定 / 敌人最近距离 / 疑似卡住）
        /// 7 → 碰撞体尺寸与可见尺寸对齐（障碍物原来碰撞是视觉的两倍）、边界墙改为可见、
        ///     刷怪夹进可通行区域（原来生成半径 14 米大于地图半高 9.5 米，敌人生成在墙外进不来）
        /// 8 → 地图放大到屏幕的 3 倍（64×36）、移除障碍物、刷怪改为对象池复用、
        ///     新增「不许生成在玩家屏幕 1/4 范围内」规则、墙色改暗
        /// 9 → 灰盒敌人落成 prefab 资产并接线给刷怪器（不再只在内存里造原型）
        /// 10 → 修对象池「新建实例继承了原型的非激活状态」导致弹体不可见；
        ///      弹体 prefab 改放 Resources 并在运行时按名加载
        /// </summary>
        private const int GreyboxSceneVersion = 10;

        private static bool s_AutoGenerationAttempted;

        /// <summary>
        /// 编辑器加载后检查灰盒场景是否需要（重新）生成。
        ///
        /// **为什么要有这个**：菜单项需要人记得点，实测漏过一次——进了播放模式但场景不存在，
        /// 结果「没有地图可看」。更糟的是旧场景一旦生成就会一直留着，
        /// 结构改对了也不会重建。所以用 <see cref="MarkerPath"/> 记下生成器版本：
        /// 版本不符就重建。
        ///
        /// **但不会覆盖手工改动**：若场景文件比标记新，说明有人在生成后编辑过它，
        /// 此时只警告、不重建，避免把人的工作冲掉。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ScheduleAutoGeneration()
        {
            EditorApplication.delayCall += TryAutoGenerateGreyboxScene;
        }

        private static void TryAutoGenerateGreyboxScene()
        {
            if (s_AutoGenerationAttempted)
                return;

            s_AutoGenerationAttempted = true;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            if (!ShouldRegenerate())
                return;

            Debug.Log("[BattleGreybox] 灰盒场景缺失或结构版本过旧，自动生成一次。");
            if (!BuildScene())
                Debug.LogWarning("[BattleGreybox] 自动生成失败，可手动跑菜单 " + MenuPath + "。");
        }

        /// <summary>
        /// 判断是否需要生成：场景不存在、标记缺失、或标记里的版本号与当前生成器不符。
        ///
        /// **刻意不比较"场景是否比标记新"**：那个启发式会把"版本已更新但场景刚生成过"
        /// 误判成"有人手工改过"，于是版本号形同虚设——旧场景永远不重建。
        /// 代价是升级版本号时会覆盖对灰盒场景的手工改动；这个取舍是有意的：
        /// 它是生成物，标记文件里也写明了。想保住手工改动就先把它另存为别的场景。
        /// </summary>
        private static bool ShouldRegenerate()
        {
            if (!System.IO.File.Exists(ScenePath))
                return true;

            if (!System.IO.File.Exists(MarkerPath))
                return true;

            return ReadMarkerVersion() != GreyboxSceneVersion;
        }

        private static int ReadMarkerVersion()
        {
            try
            {
                var lines = System.IO.File.ReadAllLines(MarkerPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("version="))
                        continue;

                    int parsed;
                    if (int.TryParse(trimmed.Substring("version=".Length).Trim(), out parsed))
                        return parsed;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[BattleGreybox] 读取生成标记失败：" + exception.Message);
            }

            return -1;
        }

        private static void WriteMarker()
        {
            // 用 Environment.NewLine：只写 \n 在 Windows 记事本里会显示成一行。
            var lines = new[]
            {
                "# 本文件由 BattleGreyboxBuilder 生成，用于记录灰盒场景的结构版本。",
                "# 场景结构改动时请同步提高 BattleGreyboxBuilder.GreyboxSceneVersion。",
                "# 本场景是**生成物**：版本号一升就会被重建，会覆盖对它的手工改动。",
                "# 想保住手工改动，请先把场景另存为别的名字。",
                "# 删掉本文件可强制下次打开工程时重建场景。",
                "version=" + GreyboxSceneVersion,
                "scene=" + ScenePath,
                "generated=" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            System.IO.File.WriteAllText(MarkerPath, string.Join(System.Environment.NewLine, lines) + System.Environment.NewLine);
            AssetDatabase.ImportAsset(MarkerPath, ImportAssetOptions.ForceUpdate);
        }

        [MenuItem(MenuPath, false, 10)]
        public static void BuildGreyboxScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("正在运行", "请先退出播放模式再生成场景。", "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (System.IO.File.Exists(ScenePath))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "场景已存在",
                    ScenePath + " 已存在，将覆盖它。",
                    "覆盖",
                    "取消");
                if (!overwrite)
                    return;
            }

            BuildScene();
        }

        /// <summary>
        /// 真正建场景。菜单与自动生成共用。
        /// 不含任何交互询问——那属于调用方的职责。
        /// </summary>
        /// <returns>是否成功保存。</returns>
        private static bool BuildScene()
        {
            EnsureFolder("Assets", "Scenes");

            // 先把精灵落成资产，再建场景。
            // 关键：场景里引用的精灵**不能是运行时临时对象**——那种引用会被嵌进场景文件，
            // 既不可靠也无法给队友复用。这里保证有一份真正的资产，各处只引用它。
            var sprite = EnsureSquareSpriteAsset();
            if (sprite == null)
            {
                Debug.LogError("[BattleGreybox] 无法创建灰盒方块精灵资产，场景里将看不到方块。");
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildCamera();
            BuildGround(sprite);
            BuildBoundaryWalls(sprite);
            var player = BuildPlayer(sprite);
            BuildCameraFollow(player);
            BuildWaveSystem(sprite);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("[BattleGreybox] 场景保存失败：" + ScenePath);
                return false;
            }

            Debug.Log("[BattleGreybox] 已生成 " + ScenePath +
                      "\n下一步：进播放模式，用 WASD / 方向键移动，按 F 对敌人造成伤害。");

            WriteMarker();
            return true;
        }

        /// <summary>
        /// 保证灰盒方块精灵以资产形式存在（`Assets/Art/Textures/white_16.png`），并返回它。
        /// 已经存在就直接用，不重复生成。
        /// </summary>
        public static Sprite EnsureSquareSpriteAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpriteAssetPath);
            if (existing != null)
                return existing;

            return BuildSquareSpriteAsset();
        }

        [MenuItem("Tools/程序C/生成 Sprite 方块资源", false, 11)]
        public static Sprite BuildSquareSpriteAsset()
        {
            EnsureFolder("Assets", "Art");
            EnsureFolder("Assets/Art", "Textures");

            var texture = CreateWhiteTexture(GreyboxAssets.SquareTextureSize);
            System.IO.File.WriteAllBytes(SquareSpriteAssetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(SquareSpriteAssetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(SquareSpriteAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = GreyboxAssets.SquarePixelsPerUnit;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpriteAssetPath);
            if (sprite == null)
            {
                Debug.LogError("[BattleGreybox] " + SquareSpriteAssetPath +
                               " 导入后读不到 Sprite，请检查该贴图的 Texture Type 是否为 Sprite (2D and UI)。");
                return null;
            }

            Debug.Log("[BattleGreybox] 灰盒方块精灵已就绪：" + SquareSpriteAssetPath +
                      "（16×16，1 单位 = " + GreyboxAssets.SquarePixelsPerUnit + " 像素）。");
            return sprite;
        }

        /// <summary>纯白纹理。像素尺寸与 PPU 由 <see cref="GreyboxAssets"/> 统一规定。</summary>
        private static Texture2D CreateWhiteTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            TryAssignTag(cameraObject, "MainCamera");
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;

            var serialized = new SerializedObject(camera);
            serialized.FindProperty("m_ClearFlags").intValue = (int)CameraClearFlags.SolidColor;
            serialized.FindProperty("m_BackGroundColor").colorValue = new Color(0.06f, 0.09f, 0.13f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            cameraObject.AddComponent<AudioListener>();
        }

        /// <summary>
        /// 地面：只有视觉，**不加碰撞体**。
        ///
        /// 这里踩过一次实测坑：初版给地面加了 <c>BoxCollider2D</c>（30×18 的实心矩形），
        /// 而玩家出生在 (0,0)——**正好在实心碰撞体内部**。
        /// Box2D 检测到深度重叠后沿最小平移方向把玩家弹出去（表现为"方块一出现就飞走"），
        /// 弹出地面范围后贴着边界卡住（表现为"只能左右移动"）。
        ///
        /// 幸存者类要的是「无阻挡地面 + 边界墙」，所以碰撞交给
        /// <see cref="BuildBoundaryWalls"/>，地面只负责画。
        /// </summary>
        private static void BuildGround(Sprite sprite)
        {
            var ground = new GameObject("Ground");
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(PlayAreaHalfWidth * 2f, PlayAreaHalfHeight * 2f, 1f);

            var renderer = ground.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0.16f, 0.20f, 0.24f, 1f);
            renderer.sortingOrder = 0;
        }

        /// <summary>
        /// 四面边界墙，把玩家关在地面范围内。
        ///
        /// **尺寸必须与可见范围严格一致**：灰盒方块精灵是 1×1 世界单位，
        /// 所以「transform.localScale = 目标尺寸」且「BoxCollider2D.size = 1」时，
        /// 可见大小与阻挡范围才相等。
        /// 早期版本把尺寸直接写进 collider.size（视觉仍是 1×1），
        /// 结果碰撞体比看得见的大一圈——实测表现为"障碍物的实际阻挡范围明显更大"。
        ///
        /// 墙**带渲染**：不可见的墙会造成"走到某处莫名被挡住"的困惑，
        /// 看得见才能判断自己是不是撞墙了。
        /// </summary>
        private static void BuildBoundaryWalls(Sprite sprite)
        {
            var halfWidth = PlayAreaHalfWidth;
            var halfHeight = PlayAreaHalfHeight;
            const float Thickness = 1f;

            var walls = new GameObject("BoundaryWalls");
            walls.transform.position = Vector3.zero;

            // 四条墙：中心位置、世界尺寸
            var specs = new[]
            {
                new { name = "Wall_Top", pos = new Vector2(0f, halfHeight + Thickness * 0.5f), size = new Vector2(halfWidth * 2f + Thickness * 2f, Thickness) },
                new { name = "Wall_Bottom", pos = new Vector2(0f, -halfHeight - Thickness * 0.5f), size = new Vector2(halfWidth * 2f + Thickness * 2f, Thickness) },
                new { name = "Wall_Left", pos = new Vector2(-halfWidth - Thickness * 0.5f, 0f), size = new Vector2(Thickness, halfHeight * 2f) },
                new { name = "Wall_Right", pos = new Vector2(halfWidth + Thickness * 0.5f, 0f), size = new Vector2(Thickness, halfHeight * 2f) }
            };

            foreach (var spec in specs)
            {
                var wall = new GameObject(spec.name);
                wall.transform.SetParent(walls.transform, false);
                wall.transform.position = new Vector3(spec.pos.x, spec.pos.y, 0f);
                // 可见尺寸 = 世界尺寸；collider.size 保持 1，随 transform 一起缩。
                wall.transform.localScale = new Vector3(spec.size.x, spec.size.y, 1f);

                var renderer = wall.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                // 刻意用贴近地面的暗色：墙是边界提示，不该比场景本身更抢眼。
                // 早期版本用 (0.55,0.35,0.40) 的暗红，实测被误认为"地图外多了一圈红色的东西"。
                renderer.color = new Color(0.24f, 0.22f, 0.26f, 1f);
                renderer.sortingOrder = 0;

                var collider = wall.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
            }
        }

        /// <summary>可通行区域（边界墙内侧）的半宽与半高，单位米。刷怪与其它系统共用。</summary>
        public static Vector2 PlayableHalfExtents
        {
            get { return new Vector2(PlayAreaHalfWidth, PlayAreaHalfHeight); }
        }

        /// <summary>
        /// 可通行区域半宽，米。策划要求：地图 x/y 约为屏幕 x/y 的 3 倍。
        ///
        /// 相机 orthographicSize 为 6、按 16:9 算，可见范围约 21.3 × 12 米，
        /// 故取 32 × 18 的半尺寸 = 64 × 36 的地图（约为可见范围的 3 倍 × 3 倍）。
        /// 早期版本是 30 × 18，只有可见范围的 1.4 倍——角色跑几步就到头，
        /// 而且"敌人在屏幕外生成"这件事根本放不下。
        /// </summary>
        private const float PlayAreaHalfWidth = 32f;

        /// <summary>可通行区域半高，米。含义见 <see cref="PlayAreaHalfWidth"/>。</summary>
        private const float PlayAreaHalfHeight = 18f;

        private static GameObject BuildPlayer(Sprite sprite)
        {
            var player = new GameObject("Player");
            TryAssignTag(player, "Player");
            player.transform.position = Vector3.zero;

            // 用 localScale 缩方块会让碰撞体也跟着缩，且非均匀缩放下圆形碰撞体容易抖。
            // 改成一条 0.8 单位的白方块：用 scale 只缩视觉，碰撞体单独定尺寸。
            player.transform.localScale = Vector3.one;

            var renderer = player.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0.35f, 0.85f, 0.75f, 1f);
            renderer.sortingOrder = 1;

            // 方形角色用方碰撞体，不用圆形：圆形套在方块上会在斜向接触时产生侧向滑移。
            var collider = player.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.8f, 0.8f);

            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            player.AddComponent<PlayerStatsBase>();
            player.AddComponent<PlayerRuntime>();

            var motor = player.AddComponent<PlayerMotor>();
            AssignInputActions(motor);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 开发期伤害工具：让「敌人会死」这条验收不必等程序 A 的器官管道。
            // 与 DevDamageField.cs 自身用同一组条件编译符号，两边必须一致，
            // 否则正式发行版构建会因为找不到这个类型而失败。
            player.AddComponent<DevDamageField>();
#endif

            return player;
        }

        private static void BuildCameraFollow(GameObject player)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[BattleGreybox] 找不到 Main Camera（Camera.main 为 null），无法挂跟随脚本。" +
                               "请检查相机是否带 MainCamera 标签。");
                return;
            }

            var follow = camera.gameObject.AddComponent<CameraFollow>();

            var serialized = new SerializedObject(follow);
            var targetProperty = serialized.FindProperty("target");
            if (targetProperty == null)
            {
                Debug.LogError("[BattleGreybox] CameraFollow 上找不到 target 字段，镜头不会跟随。");
                return;
            }

            targetProperty.objectReferenceValue = player.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 写回后立刻读一次，确认真的序列化进去了——"设了"和"设上了"是两回事。
            if (follow.Target == null)
            {
                Debug.LogError("[BattleGreybox] CameraFollow.target 写入后读回仍为 null，镜头不会跟随。");
            }
        }

        /// <summary>
        /// 刷怪器 + 波次计时。挂在同一个名为 BattleSystem 的空物体上。
        /// 敌人 prefab 不指定——<see cref="EnemySpawner"/> 会退回 <see cref="EnemyFactory"/>
        /// 现场造灰盒敌人。**但精灵必须显式接上**：初版这里漏了，
        /// 结果刷怪器一路走 EnemyFactory 里那份 1 PPU 的兜底图，敌人小到看不见。
        /// </summary>
        private static void BuildWaveSystem(Sprite sprite)
        {
            var system = new GameObject("BattleSystem");
            system.transform.position = Vector3.zero;

            var spawner = system.AddComponent<EnemySpawner>();
            system.AddComponent<WaveDirector>();
            system.AddComponent<EnemyCountLogger>();
            // 灰盒诊断 HUD：波次倒计时摆右上角，镜头/敌人数字摆左上角。
            // 没有它们，"一波 60 秒"和"镜头有没有跟随"都只能靠肉眼描述去猜。
            system.AddComponent<WaveHud>();
            system.AddComponent<BattleDebugHud>();

            // 弹体 prefab 落成资产。用资产而非运行时原型：
            // 运行时原型里的精灵引用在实例化后不保证可靠，实测出现"弹体存在但不可见"。
            // 放在 Resources 下，运行时用 Resources.Load 绑定——静态字段跨不过域重载，
            // 需要一个不依赖场景序列化的途径。
            var projectilePrefab = Projectile.EnsureProjectilePrefab(
                sprite, 0.25f, new Color(1f, 0.75f, 0.3f, 1f));
            if (projectilePrefab == null)
                Debug.LogError("[BattleGreybox] 弹体 prefab 生成失败，运行时会退回不可靠的运行时原型。");
            else
                Debug.Log("[BattleGreybox] 弹体 prefab = " + Projectile.GreyboxProjectilePrefabPath);

            var serialized = new SerializedObject(spawner);
            var spriteProperty = serialized.FindProperty("enemySprite");
            if (spriteProperty != null)
            {
                spriteProperty.objectReferenceValue = sprite;
            }

            // 把灰盒敌人落成 prefab 并接线给刷怪器。
            // 走 prefab 而不是内存原型：资产可在 Inspector 里查看、可被美术替换、
            // 也能被别的场景引用；内存原型只在本次会话有效。
            var prefabProperty = serialized.FindProperty("enemyPrefab");
            if (prefabProperty != null)
            {
                var enemyPrefab = EnemyFactory.EnsureGreyboxEnemyPrefab(
                    sprite, new Color(0.85f, 0.30f, 0.35f, 1f));
                if (enemyPrefab != null)
                    prefabProperty.objectReferenceValue = enemyPrefab;
            }
            else
            {
                Debug.LogWarning("[BattleGreybox] EnemySpawner 上找不到 enemyPrefab 字段。");
            }

            // 把可通行区域接线给刷怪器：边界墙尺寸与刷怪夹取范围必须一致，
            // 各写一份数字必然漂移（这一轮已经因为"两处各写一份"踩过两次）。
            var halfExtents = PlayableHalfExtents;
            SetFloatProperty(serialized, "playAreaHalfWidth", halfExtents.x);
            SetFloatProperty(serialized, "playAreaHalfHeight", halfExtents.y);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (spriteProperty == null)
                Debug.LogWarning("[BattleGreybox] EnemySpawner 上找不到 enemySprite 字段，敌人会退回兜底精灵。");

            // 把弹体精灵接线到敌人 prefab 上的 EnemyAttack。
            // **必须改 prefab 资产本身**：该组件在 prefab 上，场景里的实例是运行时从 prefab 生成的，
            // 改场景实例不会影响后续刷出来的敌人。
            WireProjectileSprite(EnemyFactory.GreyboxEnemyPrefabPath, sprite);
        }

        /// <summary>
        /// 给 prefab 上的 <c>EnemyAttack</c> 接上弹体精灵。
        /// 用 <c>LoadPrefabContents</c> / <c>SaveAsPrefabAsset</c> 是 Unity 官方的
        /// "以可编辑方式打开 prefab"的入口，避免直接改场景实例造成改动丢失。
        /// </summary>
        private static void WireProjectileSprite(string prefabPath, Sprite sprite)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogWarning("[BattleGreybox] 打不开 prefab：" + prefabPath + "，弹体精灵不会被接线。");
                return;
            }

            try
            {
                var attack = root.GetComponent<EnemyAttack>();
                if (attack == null)
                {
                    Debug.LogWarning("[BattleGreybox] " + prefabPath + " 上没有 EnemyAttack。");
                    return;
                }

                var serialized = new SerializedObject(attack);
                var spriteProperty = serialized.FindProperty("projectileSprite");
                if (spriteProperty != null && sprite != null)
                    spriteProperty.objectReferenceValue = sprite;

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                if (spriteProperty == null)
                {
                    Debug.LogWarning("[BattleGreybox] EnemyAttack 上没有 projectileSprite 字段，" +
                                     "弹体可能不可见。");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetFloatProperty(SerializedObject serialized, string fieldName, float value)
        {
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning("[BattleGreybox] 找不到字段 " + fieldName + "，该值不会被接线。");
                return;
            }

            property.floatValue = value;
        }

        /// <summary>把 InputSystem_Actions 里的 Move / Sprint 动作引用填进 PlayerMotor。</summary>
        private static void AssignInputActions(PlayerMotor motor)
        {
            var references = LoadInputActionReferences();
            if (references.Count == 0)
            {
                Debug.LogWarning("[BattleGreybox] 读不到 " + InputActionsPath +
                                 " 里的动作引用，PlayerMotor 的输入字段为空，需要手工拖入。");
                return;
            }

            var serialized = new SerializedObject(motor);
            var move = FindActionReference(references, MoveActionName);
            var sprint = FindActionReference(references, SprintActionName);

            if (move != null)
                serialized.FindProperty("moveAction").objectReferenceValue = move;
            if (sprint != null)
                serialized.FindProperty("sprintAction").objectReferenceValue = sprint;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (move == null)
                Debug.LogWarning("[BattleGreybox] 没找到名为 " + MoveActionName + " 的动作。");
            if (sprint == null)
                Debug.LogWarning("[BattleGreybox] 没找到名为 " + SprintActionName + " 的动作。");
        }

        /// <summary>
        /// 从 .inputactions 资产里取出所有 InputActionReference 子资产。
        /// 动作引用是资产的子对象，只能用 LoadAllAssetsAtPath 取。
        /// </summary>
        private static List<InputActionReference> LoadInputActionReferences()
        {
            var result = new List<InputActionReference>();
            var assets = AssetDatabase.LoadAllAssetsAtPath(InputActionsPath);

            foreach (var asset in assets)
            {
                var reference = asset as InputActionReference;
                if (reference != null)
                    result.Add(reference);
            }

            return result;
        }

        private static InputActionReference FindActionReference(List<InputActionReference> references, string actionName)
        {
            foreach (var reference in references)
            {
                if (reference == null || reference.action == null)
                    continue;

                if (reference.action.name == actionName)
                    return reference;
            }

            return null;
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        /// <summary>
        /// 设置 Tag，但先确认 Tag 存在。
        /// 直接写 <c>go.tag = "不存在的Tag"</c> 会在运行时抛 <c>UnityException</c>；
        /// 本工程 TagManager 的 tags 列表是空的（MainCamera / Player 属 Unity 内置，通常可用，
        /// 但内置列表被改过就会失效），所以这里显式兜底。
        /// </summary>
        private static void TryAssignTag(GameObject target, string tag)
        {
            try
            {
                target.tag = tag;
            }
            catch (UnityException)
            {
                Debug.LogWarning("[BattleGreybox] Tag \"" + tag +
                                 "\" 在当前工程不存在，已跳过设置。可在 Project Settings → Tags and Layers 里补上。");
            }
        }
    }
}
