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
        private const string BackgroundTileFolder = "Assets/Art/bk1440-s";
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
        /// 11 → 贴真图：用 Assets/Art/bk1440-s 的九宫格底图替换纯色地面（边框八块贴地图边界、
        ///      中间一块铺满内部），边界墙压暗到接近黑以免在深海底图上显脏
        /// 12 → 地图改正方形 72×72，让九宫格「3 格 × 24 米」与「480 像素 ÷ 20 PPU」
        ///      严格对上，边框不再有拉伸；底图导入参数也由代码强制对齐
        /// 13 → **修九宫格全部重叠**：12 版按「精灵 = 1 世界单位」算缩放，
        ///      而底图实际是 480 px ÷ PPU 个单位，于是每块铺开 48 米、格心只挪 24 米。
        ///      改为按精灵 <c>bounds</c> 反算缩放（与 PPU 解耦），PPU 也改成
        ///      从图的实际宽度推导，换分辨率不用改代码
        /// 14 → 相机视野长边拉到 36 米（= 地图 72 米的一半，可视 36 × 20.25 米）；
        ///      角色与敌人同倍放大 1.6875（玩家 0.8 → 1.35 米），弹体一并放大，
        ///      尺寸统一收进 GreyboxAssets 以免两处各改一遍
        /// 15 → 玩家移速 5 → 10 米/秒（策划要求两倍）。这个值是**烘进场景**的
        ///      （`PlayerStatsBase` 的序列化字段），不改版本号的话场景不会重建、值不会生效
        /// 16 → 相机加地图边界限制：角色接近边缘时镜头停住、保证画面不越界，
        ///      回到中间再继续跟随。边界值由构建器写给 CameraFollow，不手填
        /// 17 → 场景里摆一个木桩（TrainingDummy，位置 12,0）供试伤害；
        ///      木桩是不动不攻击、血量 99999 的第 7 种原型
        /// 18 → 战场分层与碰撞关系：角色层穿过敌人层（不再被敌人卡住、也不推木桩），
        ///      但仍被墙层挡住；敌人之间保留碰撞体积。玩家刚体改 Kinematic
        /// </summary>
        private const int GreyboxSceneVersion = 18;

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
            ConfigureBattleLayers();
            BuildMapBackground(sprite);
            BuildBoundaryWalls(sprite);
            var player = BuildPlayer(sprite);
            BuildCameraFollow(player);
            BuildWaveSystem(sprite);
            BuildTrainingDummy();

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

        /// <summary>
        /// 建立三个战场层，并把碰撞关系写成：
        ///
        /// ```
        /// 玩家 ↔ 敌人   关   （角色穿过敌人和木桩，且不会推动它们）
        /// 玩家 ↔ 墙     开   （角色被地图边界挡住）
        /// 敌人 ↔ 敌人   开   （保留敌人之间的碰撞体积）
        /// 敌人 ↔ 墙     开
        /// ```
        ///
        /// **为什么不改 ProjectSettings**：`ProjectSettings/` 是多人共用仓库里
        /// 两边都在改的文件（远端 main 与本地 main 有 29 个文件双侧修改）。
        /// 层名与碰撞矩阵都在这里用代码写一遍，效果一样，但不动共享配置、没有合并风险。
        ///
        /// **为什么用 <c>IgnoreLayerCollision</c> 而不是手改 <c>m_LayerCollisionMatrix</c>**：
        /// 那是 32×32 位掩码，手算位偏移几乎必错；API 是语义化的，写错了也看得懂。
        ///
        /// 顺序很重要：**先把层名写进 TagManager 再取层号**，否则第一次生成时
        /// `NameToLayer` 会返回 -1（层还没建），于是所有物体都落在 Default 上、
        /// 三者又会互相撞。
        /// </summary>
        private static void ConfigureBattleLayers()
        {
            var playerLayer = EnsureLayer(BattleLayers.Player);
            var enemyLayer = EnsureLayer(BattleLayers.Enemy);
            var wallLayer = EnsureLayer(BattleLayers.Wall);

            if (playerLayer < 0 || enemyLayer < 0 || wallLayer < 0)
            {
                Debug.LogError("[BattleGreybox] 层不足，无法建立战场碰撞关系。" +
                               "角色会被敌人卡住、木桩会被推走。");
                return;
            }

            // 先全关，再显式打开需要的——比"只关一对、其余靠默认"更不容易出意外。
            Physics2D.IgnoreLayerCollision(playerLayer, enemyLayer, true);
            Physics2D.IgnoreLayerCollision(playerLayer, wallLayer, false);
            Physics2D.IgnoreLayerCollision(enemyLayer, enemyLayer, false);
            Physics2D.IgnoreLayerCollision(enemyLayer, wallLayer, false);

            Debug.Log("[BattleGreybox] 战场碰撞关系已确立：玩家(" + playerLayer + ") 穿过 敌人(" +
                      enemyLayer + ")，两者都被 墙(" + wallLayer + ") 挡住；敌人之间保留碰撞。");
        }

        /// <summary>
        /// 保证某个层名存在于 <c>TagManager.asset</c> 里，返回层号；找不到空位返回 -1。
        ///
        /// 用 <c>SerializedObject</c> 写而不是直接改 YAML：这是 Unity 的正式入口，
        /// 写坏的后果比自己拼 YAML 小得多。**只往空的用户层槽位填**（0–7 是内置层，
        /// 不能占用），已存在同名层则原样返回，所以可以反复调用。
        /// </summary>
        private static int EnsureLayer(string layerName)
        {
            var existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0)
                return existing;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[BattleGreybox] 读不到 TagManager.asset，无法建立层 \"" + layerName + "\"。");
                return -1;
            }

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                Debug.LogWarning("[BattleGreybox] TagManager 里找不到 layers 数组。");
                return -1;
            }

            // 0–7 是 Unity 内置层（Default / TransparentFX / Ignore Raycast / Water / UI…），
            // 占用它们会改变物理默认行为，所以从 8 开始找。
            for (var i = 8; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                if (element != null && !string.IsNullOrEmpty(element.stringValue))
                    continue;

                element.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();

                var created = LayerMask.NameToLayer(layerName);
                Debug.Log("[BattleGreybox] 已建立层 " + created + " = \"" + layerName + "\"。");
                return created;
            }

            Debug.LogWarning("[BattleGreybox] 用户层槽位（8–31）已满，无法建立 \"" + layerName + "\"。");
            return -1;
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            TryAssignTag(cameraObject, "MainCamera");
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            // 正交相机的 orthographicSize 是**半高**，不是长边。按 16:9 换算：
            //   长边 = orthographicSize × 2 × (16/9) = 10.125 × 2 × 1.7778 = 36 米
            // 也就是可视范围 36 × 20.25 米，正好是 72 × 72 地图的一半。
            // 换成别的宽高比时短边仍是 20.25 米，长边会变（见验证清单里的说明）。
            camera.orthographicSize = CameraHalfHeight;

            var serialized = new SerializedObject(camera);
            serialized.FindProperty("m_ClearFlags").intValue = (int)CameraClearFlags.SolidColor;
            serialized.FindProperty("m_BackGroundColor").colorValue = new Color(0.06f, 0.09f, 0.13f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            cameraObject.AddComponent<AudioListener>();
        }

        /// <summary>
        /// 相机可视长边，米。策划要求长边 **36 米 = 地图边长 72 米的一半**。
        ///
        /// 为什么不用 <c>camera.aspect</c> 现算：那会把 Game 视图窗口当时的尺寸写进场景，
        /// 别人打开工程看到的就不是同一张地图。这里按 16:9 定死，保证**生成结果可复现**。
        /// </summary>
        private const float CameraVisibleLongEdge = 36f;

        /// <summary>16:9 下把长边折成半高：<c>半高 = 长边 ÷ 2 ÷ (16/9)</c>。</summary>
        private const float CameraHalfHeight = CameraVisibleLongEdge * 9f / 16f / 2f;

        /// <summary>
        /// 底图：用 <c>Assets/Art/bk1440-s</c> 的九宫格替换原来的纯色地面。
        ///
        /// **图的分法**（1 左上 / 2 上中 / 3 右上 / 4 左中 / 5 中心 / 6 右中 / 7 左下 / 8 下中 / 9 右下）：
        /// 除 5 以外的八块都是**边框**，只贴地图边界；5 是内部底纹。
        ///
        /// **布局**：三行三列的"环"直接对齐可通行区域——上边一行 1-2-3、中间一行 4-5-6、
        /// 下边一行 7-8-9，整圈的外沿正好落在 <see cref="PlayAreaHalfWidth"/> /
        /// <see cref="PlayAreaHalfHeight"/> 上。于是"边框图贴在地图边界"是字面成立的，
        /// 玩家跑到墙边时看到的就是这一圈图。
        ///
        /// **为什么地图必须是正方形**：九张原图都是 480×480。地图取 72 × 72 时
        /// 每格 = 72 ÷ 3 = 24 米，而 480 像素 ÷ 20 PPU = 24 米——**两者相等**，
        /// 于是每块底图都按 1:1 落在自己的格子里，**一点拉伸都没有**。
        /// 地图若是 64 × 36 这种长方形，格子与图的比例就对不上，边框必然被拉扁
        /// （改之前实测横向要拉约 1.19:1）。所以尺寸和贴图是绑在一起的：
        /// **要动 <see cref="PlayAreaHalfWidth"/> 就得让地图保持正方形**，
        /// 否则回到拉伸状态。导入参数由 <see cref="FixBackgroundTileImports"/> 强制对齐，
        /// 不依赖 .meta 有没有被改坏。
        ///
        /// **原本的纯色地面已删除**：深海底图自己就是地面，再盖一层 <c>Ground</c> 会把图挡住。
        /// 那个对象还留着踩坑记录（地面碰撞体把玩家弹飞），见 <see cref="BuildBoundaryWalls"/> 的注释。
        /// </summary>
        /// <param name="fallbackSprite">底图缺失时退回的纯色方块，保证场景仍然能看。</param>
        private static void BuildMapBackground(Sprite fallbackSprite)
        {
            // 每格米数先定下来：九宫格 = 地图边长 ÷ 3。底图的 PPU 是从它反推的。
            var cellMeters = PlayAreaHalfWidth * 2f * InnerColumnWidthRatio;
            var pixelsPerUnit = ResolveBackgroundPixelsPerUnit(cellMeters);
            FixBackgroundTileImports(pixelsPerUnit);

            var root = new GameObject("MapBackground");
            root.transform.position = Vector3.zero;

            // 底图整体压到最底：地面 0、玩家 1、波次 HUD 之类的都在它上面。
            const int BackgroundSortingOrder = -100;

            var tiles = LoadBackgroundTiles();

            // ---- 内部：中心块 5 铺满「九宫格中心格」的范围 ----
            var centerWidth = PlayAreaHalfWidth * 2f * InnerColumnWidthRatio;
            var centerHeight = PlayAreaHalfHeight * 2f * InnerRowHeightRatio;
            CreateBackgroundTile(
                root.transform,
                "Tile_5_Center",
                tiles[5] != null ? tiles[5] : fallbackSprite,
                Vector2.zero,
                new Vector2(centerWidth, centerHeight),
                BackgroundSortingOrder);

            // ---- 边框：八块贴地图边界 ----
            // outer = 整个九宫格外沿；inner = 中心格。两者之差就是边框带的厚度。
            var outerWidth = PlayAreaHalfWidth * 2f * BackgroundFrameScale;
            var outerHeight = PlayAreaHalfHeight * 2f * BackgroundFrameScale;
            var leftColumnWidth = (outerWidth - centerWidth) * 0.5f;
            var rightColumnWidth = leftColumnWidth;
            var topRowHeight = (outerHeight - centerHeight) * 0.5f;
            var bottomRowHeight = topRowHeight;

            var xLeft = -(centerWidth * 0.5f + leftColumnWidth * 0.5f);
            var xCenter = 0f;
            var xRight = centerWidth * 0.5f + rightColumnWidth * 0.5f;
            var yTop = centerHeight * 0.5f + topRowHeight * 0.5f;
            var yMiddle = 0f;
            var yBottom = -(centerHeight * 0.5f + bottomRowHeight * 0.5f);

            var specs = new[]
            {
                // 上排
                new { index = 1, name = "Tile_1_TopLeft",     pos = new Vector2(xLeft,   yTop),    size = new Vector2(leftColumnWidth,  topRowHeight) },
                new { index = 2, name = "Tile_2_TopCenter",   pos = new Vector2(xCenter, yTop),    size = new Vector2(centerWidth,      topRowHeight) },
                new { index = 3, name = "Tile_3_TopRight",    pos = new Vector2(xRight,  yTop),    size = new Vector2(rightColumnWidth, topRowHeight) },
                // 中排（中心格已单独建，这里只放左右两块）
                new { index = 4, name = "Tile_4_MiddleLeft",  pos = new Vector2(xLeft,   yMiddle), size = new Vector2(leftColumnWidth,  centerHeight) },
                new { index = 6, name = "Tile_6_MiddleRight", pos = new Vector2(xRight,  yMiddle), size = new Vector2(rightColumnWidth, centerHeight) },
                // 下排
                new { index = 7, name = "Tile_7_BottomLeft",  pos = new Vector2(xLeft,   yBottom), size = new Vector2(leftColumnWidth,  bottomRowHeight) },
                new { index = 8, name = "Tile_8_BottomCenter",pos = new Vector2(xCenter, yBottom), size = new Vector2(centerWidth,      bottomRowHeight) },
                new { index = 9, name = "Tile_9_BottomRight", pos = new Vector2(xRight,  yBottom), size = new Vector2(rightColumnWidth, bottomRowHeight) }
            };

            var placed = 1; // 中心块已经放过
            foreach (var spec in specs)
            {
                var sprite = tiles[spec.index];
                if (sprite == null)
                {
                    Debug.LogWarning("[BattleGreybox] 底图缺 " + spec.index + ".png，该块用纯色方块代替。");
                    sprite = fallbackSprite;
                }
                else
                {
                    placed++;
                }

                CreateBackgroundTile(root.transform, spec.name, sprite, spec.pos, spec.size, BackgroundSortingOrder);
            }

            // 把"格心间距 / 目标边长 / 精灵自身世界尺寸 / 实际渲染边长"一起打出来。
            // **这是为了防住已经发生过一次的重叠事故**：那次九块图的分母（精灵世界尺寸）
            // 与格心间距用了两套假设，结果每块铺开 48 米而只挪了 24 米，九张全叠住。
            // 现在这四个数只要彼此自洽就不会重叠，对不上时也能一眼看出是哪一项错了。
            var sample = tiles[1] != null ? tiles[1] : fallbackSprite;
            var sampleSize = GetSpriteWorldSize(sample);
            var spacing = Mathf.Abs(xRight - xCenter);
            Debug.Log("[BattleGreybox] 地图底图：九宫格 " + placed + "/9 块就位" +
                      "｜地图 " + (PlayAreaHalfWidth * 2f).ToString("0.#") + " × " + (PlayAreaHalfHeight * 2f).ToString("0.#") + " 米" +
                      "｜每格 " + leftColumnWidth.ToString("0.###") + " × " + topRowHeight.ToString("0.###") + " 米" +
                      "｜格心间距 " + spacing.ToString("0.###") + " 米（须 = 每格，小于每格就会重叠）" +
                      "｜PPU " + pixelsPerUnit.ToString("0.###") + " → 单块精灵 " +
                      sampleSize.x.ToString("0.###") + " × " + sampleSize.y.ToString("0.###") + " 单位" +
                      "｜实际渲染 " + (sampleSize.x * (leftColumnWidth / sampleSize.x)).ToString("0.###") + " × " +
                      (sampleSize.y * (topRowHeight / sampleSize.y)).ToString("0.###") + " 米。");
        }

        /// <summary>
        /// 精灵在 1 倍缩放下的世界尺寸。用 <c>bounds</c> 而不是拿 <c>pixelsPerUnit</c> 现算：
        /// bounds 已经把 PPU 和 pivot 都算进去了，是 Unity 自己的口径。
        /// 尺寸异常时退回 <c>1×1</c> 并告警——宁可日志难看，也不要静默算出个 0 去当除数。
        /// </summary>
        private static Vector2 GetSpriteWorldSize(Sprite sprite)
        {
            if (sprite == null)
                return Vector2.one;

            var size = (Vector2)sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                Debug.LogWarning("[BattleGreybox] 精灵 \"" + sprite.name + "\" 的尺寸异常（" +
                                 size.x + " × " + size.y + "），按 1 单位处理。");
                return Vector2.one;
            }
            return size;
        }

        /// <summary>
        /// 读九宫格底图。<c>[i]</c> 对应 <c>i.png</c>，缺图或导入类型不对时为 <c>null</c>。
        ///
        /// 用 <c>LoadAssetAtPath&lt;Sprite&gt;</c> 而不是先拿 Texture2D：拿 Sprite 能直接暴露
        /// "贴图没设成 Sprite (2D and UI)" 这个最常见的导入错误——那时这里会返回 null，
        /// 而不是等到场景里出现一块空白才发现。
        /// </summary>
        private static Sprite[] LoadBackgroundTiles()
        {
            var tiles = new Sprite[10]; // 1..9，0 号不用
            for (var i = 1; i <= 9; i++)
            {
                var path = BackgroundTileFolder + "/" + i + ".png";
                tiles[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (tiles[i] == null)
                    Debug.LogWarning("[BattleGreybox] 读不到底图 Sprite：" + path +
                                     "（检查文件是否存在、Texture Type 是否为 Sprite (2D and UI)）");
            }
            return tiles;
        }

        /// <summary>
        /// 把九张底图的导入参数调成"一格一图、像素 1:1"。**PPU 是从图的实际宽度推出来的，
        /// 不是写死的常数**——这样美术换分辨率（480、960、…）都不用改代码。
        ///
        /// 推导只有一步：
        /// <code>
        /// 每格米数 = 地图边长 ÷ 3          （九宫格，三行三列）
        /// 想要 1:1  →  PPU = 图宽像素 ÷ 每格米数
        /// </code>
        /// 72 米地图 → 每格 24 米；480 px 的图 → PPU 20，960 px 的图 → PPU 40，以此类推。
        ///
        /// **为什么必须由代码设**：PPU 是"像素 ÷ PPU = 世界米数"里的分母。
        /// 沿用灰盒方块的 16 PPU 时，480 px 的图会变成 30 世界单位，
        /// 塞进 24 米的格子里就是被压扁 25%。导入设置一旦漂移，画面就悄悄错，
        /// 而错的方向取决于 .meta 里写了什么——所以这里不看 .meta，直接按公式设一遍。
        ///
        /// 只在真的不一致时才 <c>SaveAndReimport</c>：那个调用会触发一次资源重导，
        /// 每次打开工程都跑一遍会白白拖慢启动。
        /// </summary>
        private static void FixBackgroundTileImports(float pixelsPerUnit)
        {
            for (var i = 1; i <= 9; i++)
            {
                var path = BackgroundTileFolder + "/" + i + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("[BattleGreybox] 拿不到贴图导入器：" + path);
                    continue;
                }

                var changed = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }
                if (!Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit))
                {
                    importer.spritePixelsPerUnit = pixelsPerUnit;
                    changed = true;
                }
                // 像素画要 Point + 关 mipmap，否则缩小时会糊。
                if (importer.filterMode != FilterMode.Point)
                {
                    importer.filterMode = FilterMode.Point;
                    changed = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }

                if (!changed)
                    continue;

                importer.SaveAndReimport();
                Debug.Log("[BattleGreybox] 已把底图导入参数改成 1:1 不拉伸：" + path +
                          "（PPU = " + pixelsPerUnit.ToString("0.###") + "）");
            }
        }

        /// <summary>
        /// 让每张底图恰好铺满自己那一格所需的 PPU，**从图的实际像素宽度推出来**。
        ///
        /// <code>PPU = 图宽像素 ÷ 每格米数</code>
        ///
        /// 所以换分辨率不用改代码：480 px 的图 → 20，960 px 的图 → 40。
        /// 前提是九张图**同宽**——不同宽的话它们的像素密度不一致，
        /// 网格即使铺满也会看起来"有的糊有的清"，这里会告警但不阻断。
        /// </summary>
        /// <param name="cellSizeInMeters">每格边长，米（= 地图边长 ÷ 3）。</param>
        private static float ResolveBackgroundPixelsPerUnit(float cellSizeInMeters)
        {
            for (var i = 1; i <= 9; i++)
            {
                var path = BackgroundTileFolder + "/" + i + ".png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    Debug.LogWarning("[BattleGreybox] 读不到底图贴图：" + path);
                    continue;
                }

                if (texture.width != texture.height)
                {
                    // 非要算的话，格子和格子之间就至少有一个方向塞不满，
                    // 想让"像素 1:1"成立就必须是正方形图。
                    Debug.LogWarning("[BattleGreybox] 底图不是正方形：" + path + "（" +
                                     texture.width + " × " + texture.height +
                                     "）。九宫格要 1:1 铺满，每张图必须是正方形。");
                }

                var ppu = texture.width / cellSizeInMeters;
                if (ppu > 0f)
                    return ppu;
            }

            // 一张都读不到时退回一个"能让 480 图对上 24 米格"的值，至少让场景能生成。
            Debug.LogWarning("[BattleGreybox] 九张底图都读不到，PPU 退回 20（480 px ÷ 24 米）。");
            return 20f;
        }

        /// <summary>
        /// 放一块底图。沿用边界墙同一套尺寸规矩：精灵按 1×1 世界单位缩放，
        /// 于是「transform.localScale = 目标米数」就等于「看得见的米数」，不会出现
        /// 碰撞体和外观不一致那种偏差（早期障碍物踩过这个坑）。
        /// </summary>
        /// <summary>
        /// 放一块底图，**按精灵自己的世界尺寸反算缩放**，因此与 PPU 无关。
        ///
        /// **这里出过一次实测事故，记下来**：初版照抄了边界墙那套
        /// 「<c>localScale</c> = 目标米数」。那个等式只在**精灵恰好是 1×1 世界单位**时成立——
        /// 白方块是 16 px ÷ 16 PPU = 1 单位，所以对墙成立；**底图不是**：
        /// 一块底图 = <c>480 px ÷ PPU</c>，PPU 写 16 时就是 30 单位。
        /// 我却按 <c>scale=(24,24)</c> 摆、格心间距也是 24，
        /// 于是每块实际铺开 48 米、只挪了 24 米 → **九张重叠在一起**。
        ///
        /// 现在的算法与 PPU 完全解耦：
        /// <code>scale = 目标米数 ÷ 精灵自身的世界尺寸</code>
        /// 于是不管导入设置写成 16、20 还是别的，方块都恰好铺满它那一格。
        /// 代价是每块图的像素密度会随 PPU 变（PPU 越小越糊），所以
        /// <see cref="FixBackgroundTileImports(float)"/> 仍然会去把导入设置写对，
        /// 这里只是保证**即使它没写对也不会错位**。
        /// </summary>
        private static void CreateBackgroundTile(
            Transform parent,
            string name,
            Sprite sprite,
            Vector2 center,
            Vector2 size,
            int sortingOrder)
        {
            var tile = new GameObject(name);
            tile.transform.SetParent(parent, false);
            tile.transform.position = new Vector3(center.x, center.y, 0f);

            // 精灵在 1 倍缩放下的世界尺寸（含 PPU 与 pivot 口径），按它反算缩放。
            var spriteSize = GetSpriteWorldSize(sprite);
            tile.transform.localScale = new Vector3(size.x / spriteSize.x, size.y / spriteSize.y, 1f);

            var renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// 九宫格边框环的整体缩放。1 = 边框外沿正好落在可通行区域的边界上。
        /// 调大它会把边框推到墙外（地图看起来更大，但边框与墙错位）；
        /// 调小则边框缩进墙内。
        /// </summary>
        private const float BackgroundFrameScale = 1f;

        /// <summary>中心格占可通行区域宽度的比例。1/3 是九宫格的标准分法（三列等宽）。</summary>
        private const float InnerColumnWidthRatio = 1f / 3f;

        /// <summary>中心格占可通行区域高度的比例。含义见 <see cref="InnerColumnWidthRatio"/>。</summary>
        private const float InnerRowHeightRatio = 1f / 3f;

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
            walls.layer = BattleLayers.Resolve(BattleLayers.Wall);

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
                // 子物体各自也要设层：Unity 不会从父物体继承 layer。
                wall.layer = walls.layer;
                // 可见尺寸 = 世界尺寸；collider.size 保持 1，随 transform 一起缩。
                wall.transform.localScale = new Vector3(spec.size.x, spec.size.y, 1f);

                var renderer = wall.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                // 加底图之前墙是"贴近地面的暗紫"，在纯色地面上看得出是墙。
                // 换成深海底图后那点亮度反而成了脏边——底图本身已经足够深，
                // 于是压到接近黑：仍能看见一条边界，但不抢底图。
                renderer.color = new Color(0.05f, 0.06f, 0.09f, 1f);
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
        /// 可通行区域半宽，米。策划要求：地图约为屏幕的 3 倍。
        ///
        /// 相机可见范围 **36 × 20.25 米**（长边 36 米 = 地图 72 米的一半，见
        /// <see cref="CameraVisibleLongEdge"/>），地图是它的 2 × 3.56 倍。
        /// **2026-10-13 改成正方形 72 × 72**（原为 64 × 36）：美术的九宫格底图是
        /// 正方形图，正方形地图才能让「3 行 3 列 = 72/3 = 24 米一格」和
        /// 「480 像素 ÷ 20 PPU = 24 米」严格对上，**边框一格都不用拉伸**。
        /// 见 <see cref="BuildMapBackground"/>。
        /// 早期版本是 30 × 18，只有可见范围的 1.4 倍——角色跑几步就到头，
        /// 而且"敌人在屏幕外生成"这件事根本放不下。
        /// </summary>
        private const float PlayAreaHalfWidth = 36f;

        /// <summary>可通行区域半高，米。含义见 <see cref="PlayAreaHalfWidth"/>。</summary>
        private const float PlayAreaHalfHeight = 36f;

        private static GameObject BuildPlayer(Sprite sprite)
        {
            var player = new GameObject("Player");
            TryAssignTag(player, "Player");
            player.transform.position = Vector3.zero;

            // 设在「角色层」上：与敌人层互不相碰（穿过敌人与木桩），与墙层相碰（被挡住）。
            // 碰撞矩阵由 ConfigureBattleLayerCollisions() 在代码里确立。
            player.layer = BattleLayers.Resolve(BattleLayers.Player);

            // 用 localScale 缩方块会让碰撞体也跟着缩，且非均匀缩放下圆形碰撞体容易抖。
            // 所以这里 scale 保持 1，尺寸全部交给碰撞体去写——视觉和碰撞盒因此可以各自明确。
            // 边长取自 GreyboxAssets.PlayerWorldSize（当前 1.35 米，见那里的说明：
            // 相机视野拉大到 36 米长边后，角色同倍放大以维持原来的观感占比）。
            player.transform.localScale = Vector3.one;

            var renderer = player.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0.35f, 0.85f, 0.75f, 1f);
            renderer.sortingOrder = 1;

            // 方形角色用方碰撞体，不用圆形：圆形套在方块上会在斜向接触时产生侧向滑移。
            var collider = player.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(GreyboxAssets.PlayerColliderSize, GreyboxAssets.PlayerColliderSize);

            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            // **Kinematic 而不是 Dynamic**：即使碰撞矩阵已经关掉了玩家↔敌人，
            // Dynamic 刚体仍会去推别的刚体（木桩就是这么被推着走的）。
            // Kinematic 不参与动力学求解，谁也推不动它，它也不推别人；
            // 但它**照常与静态碰撞体（墙）产生阻挡**，所以"被墙挡住"仍然成立。
            body.bodyType = RigidbodyType2D.Kinematic;

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

            // 地图边界限制：把可通行区域写给相机，避免"相机以为的地图"和"实际地图"两处各写一遍。
            // 不接这一步的话相机不会越界（字段有默认值），但地图尺寸一改就会对不上。
            var clampProperty = serialized.FindProperty("clampToMapBounds");
            if (clampProperty != null)
                clampProperty.boolValue = true;

            var centerProperty = serialized.FindProperty("mapCenter");
            if (centerProperty != null)
                centerProperty.vector2Value = Vector2.zero;

            var extentsProperty = serialized.FindProperty("mapHalfExtents");
            if (extentsProperty != null)
                extentsProperty.vector2Value = PlayableHalfExtents;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 写回后立刻读一次，确认真的序列化进去了——"设了"和"设上了"是两回事。
            if (follow.Target == null)
            {
                Debug.LogError("[BattleGreybox] CameraFollow.target 写入后读回仍为 null，镜头不会跟随。");
            }

            Debug.Log("[BattleGreybox] 相机边界限制已接入：地图半尺寸 " +
                      PlayableHalfExtents.x + " × " + PlayableHalfExtents.y +
                      " 米，开启状态 " + follow.ClampEnabled +
                      "（画面半宽/半高由相机 aspect 运行时决定，" +
                      "所以窗口比例变了也不会露黑边）。");
        }

        /// <summary>
        /// 摆一个木桩（<see cref="EnemyArchetype.Dummy"/>）在场景里，供试伤害用。
        ///
        /// **为什么由构建器直接摆一个**：木桩唯一的用途就是"被角色打"，如果每次都要人手
        /// 从 Project 拖进场景，它基本不会被用上。摆一个固定的、每次重建都在的靶子，
        /// 试伤害才是一个零成本动作。
        ///
        /// **位置 (12, 0)**：出生点旁边，走几步就到，且不挡刷怪。
        ///
        /// **prefab 不存在时自己生成一次**：`Resources` 下的资产是
        /// `EnemyPrefabBuilder` 生成的，而两个构建器的 `InitializeOnLoad` 先后顺序不保证
        /// （也可能这次加载时只有场景那个跑了）。与其留个空靶子让人以为"木桩坏了"，
        /// 不如就地补上——`BuildOne(overwrite: false)` 只在缺失时创建，不会冲掉手工调整。
        /// </summary>
        private static void BuildTrainingDummy()
        {
            var config = EnemyArchetypeConfig.Get(EnemyArchetype.Dummy);
            var path = EnemyPrefabBuilder.OutputFolder + "/" + config.displayName + ".prefab";

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var generated = EnemyPrefabBuilder.BuildOne(
                    EnemyArchetype.Dummy, GreyboxAssets.SquareSprite, overwrite: false);

                if (string.IsNullOrEmpty(generated))
                {
                    Debug.LogWarning("[BattleGreybox] 木桩 prefab 缺失且现场生成失败：" + path +
                                     "\n跑一次菜单 Tools → 程序C → 生成全部敌人 prefab 之后再重建场景。");
                    return;
                }

                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(generated);
                if (prefab == null)
                {
                    Debug.LogWarning("[BattleGreybox] 木桩 prefab 生成后仍读不到：" + generated);
                    return;
                }
            }

            // 用 InstantiatePrefab 而不是 Instantiate：保留 prefab 关联，
            // 这样在场景里改木桩的位置不会被当成"改了资产"。
            var dummy = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (dummy == null)
            {
                Debug.LogWarning("[BattleGreybox] 木桩 prefab 实例化失败：" + path);
                return;
            }

            dummy.name = "TrainingDummy";
            dummy.transform.position = new Vector3(TrainingDummyX, TrainingDummyY, 0f);
            // prefab 里已经设了层，但实例化后显式再设一次：
            // 场景里的对象层若与预期不符，症状（被角色推着走）极难反推到"层"上。
            dummy.layer = BattleLayers.Resolve(BattleLayers.Enemy);

            // 读回真实血量，确认写进去的是不是 99999——"设了"和"设上了"是两回事。
            var data = dummy.GetComponent<EnemyData>();
            var health = data != null ? data.MaxHealth : -1f;

            Debug.Log("[BattleGreybox] 木桩已摆放：位置 (" + TrainingDummyX + ", " + TrainingDummyY +
                      ")，血量 " + health.ToString("0") +
                      (Mathf.Approximately(health, EnemyArchetypeConfig.DummyHealth)
                          ? " ✅"
                          : " ⚠ 与配置里的 " + EnemyArchetypeConfig.DummyHealth.ToString("0") + " 不一致"));
        }

        /// <summary>木桩在战场场景里的摆放位置（世界坐标，米）。</summary>
        private const float TrainingDummyX = 12f;

        /// <summary>木桩在战场场景里的摆放位置。见 <see cref="TrainingDummyX"/>。</summary>
        private const float TrainingDummyY = 0f;

        /// <summary>
        /// 刷怪器 + 波次计时。挂在同一个名为 BattleSystem 的空物体上。
        ///
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
                sprite, GreyboxAssets.ProjectileWorldSize, new Color(1f, 0.75f, 0.3f, 1f));
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
