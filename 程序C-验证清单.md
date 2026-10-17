# 程序 C：战场验证清单

更新：2026-10-16。日常先打开 `Assets/Scenes/SpringUpDemo.unity`；独立排查 C 战场用 `Battle_Greybox.unity`。

## 两个场景的关系

Battle_Greybox 是 C 的源场景。Demo 已同步九宫格地图、72×72 边界、相机基础 Size 10.125、玩家移速 10、移动输入、刷怪范围及训练木桩。

之后修改 C 战场，可用 `Tools → SpringUp → 同步C战场到统一Demo` 再同步；该操作会覆盖 Demo 对应战场参数。同步前保存场景，只微调 Demo 时不必再同步。工具保留实验室、库存、器官与统一流程。

## 试玩检查

- [ ] WASD / 方向键与 Shift 疾跑正常，松开停止。
- [ ] 四面墙和墙角能挡住玩家，贴墙斜走能沿墙滑动。
- [ ] 玩家能穿过木桩/敌人而不会把它们推走；接触伤害仍正常。
- [ ] 相机跟随角色，在地图边缘受边界约束；躯干视野倍率正常。
- [ ] 在 Demo 靠近有效目标后自动攻击，实际受伤时敌人闪色；无需按 F。
- [ ] 近战敌人自然追近并造成接触伤害；远程、医疗、生产类按各自规则行动。
- [ ] 60 秒结束会清场，但清场不算击杀、不触发击杀奖励或连锁。
- [ ] 实验室、下一波、死亡和重开没有遗留弹体或状态。

独立 Battle_Greybox 中的 F 是开发伤害快捷键。它未复制到正式 Demo，不要把它当成当前攻击操作。

## 调参与代码

- 玩家：场景 Player 上的 `PlayerStatsBase`；移动由 `PlayerMotor`，实际属性与生命由 `PlayerRuntime` 管理。
- 相机：`CameraFollow` 与主场景 `SpringUpRunDriver` 的视口/躯干倍率适配。
- 敌人基础参数：`Assets/Scripts/Battle/EnemyArchetypeConfig.cs`，不是只改 Prefab 的数值副本。
- 动态敌人资源：`Assets/Resources/Enemies/`；由代码加载，不能因为没有场景 GUID 引用就删掉。
- 波次：`WaveDirector`。Demo 由 SpringUpRunDriver 控制开波；独立灰盒保留自己的调试入口。
- 碰撞：`BattleLayers` 初始化层关系，`PlayerMotor` 用扫掠实现 Kinematic 玩家碰墙。

## 自动化与剩余范围

`BattleSceneSyncChecks` 验证 Demo 接线、四墙/墙角、滑动、镜头和木桩；完整整合回归见 [工程配置操作单](工程配置操作单.md)。源场景的 `Battle_Greybox.generated.txt` 是生成器版本标记，必须保留。

正式 Boss、平衡、表现与目标设备验收未由这些检查代替。当前待办统一见 [试玩验收清单](Docs/待确认与试玩验收清单.md)，旧缺口盘点已删除。
