# 程序 B：实验室使用与接线

更新：2026-10-16。

## 两种入口

**SpringUpDemo 的休息期实验室**使用本局正式库存。装卸、替换、出售、丢弃会影响下一波；背包容量符合要求后才能出战。战斗中不能改装备。

**LaboratoryDemo**使用临时样例库存，便于随时预览所有 36 种配置。`LaboratoryDemoBootstrap` 为定义创建独立实例，并提供装配占位用于 UI 检查；退出 Play 不保存。这里不是正式掉落或经济流程。

## 怎样操作

1. 选择背包器官，再选择头、手、腿任一普通槽位；躯干器官选躯干槽。
2. 点击安装或替换；替换出的器官回到背包。选择已装备槽位可拆下。
3. 查看右侧说明、两个目标与效果预览。改变装备或点击重新开始会重置预览；仅改变选择不会重置。
4. 新触发器可以借助“受伤 -5”“回复 +5”“移动：开/关”“暴击：必定/关”模拟条件；这些按钮不影响实战人物。

预览在休息暂停时仍继续运行；它使用独立目标和时间，不会误伤真实战场敌人。概率效果不保证每次出现。

## 参数与真实数据来源

`OrganCatalog.asset` 提供共享战斗定义，背包记录独立 InstanceId。同一种器官的多件实例共用 DefinitionId；修改装备不会复制第二套伤害参数。

在 `LaboratoryCombatDemo` Inspector 可调目标血量、两个目标的位置、玩家模拟位置和死亡补充延迟。目标死亡后以新身份补充，不额外发奖励。器官参数修改位置见 [调参手册](Docs/开发改动总览与调参手册.md)。

## 脚本入口

- `LaboratoryPresenter.cs`：选择、按钮、卡片和预览刷新。
- `OrganCardView.cs`：单张器官卡片。
- `TemporaryEquipment.cs`：独立预览的样例库存和事务。
- `RunLaboratoryEquipment.cs`：把同一界面接到正式 RunController 库存。
- `LaboratoryCombatDemo.cs` / `EffectPreviewGraphic.cs`：预览逻辑和二维效果。

## 效果预览示例

![多种执行器的二维预览](Docs/Images/all_actuator_preview.png)

## 检查边界

`Tools → SpringUp → Check Laboratory UI` 检查资源接线、装配和显示；`LaboratoryPlayChecks` 检查实际预览更新。完整整合回归入口见 [工程配置操作单](工程配置操作单.md)。

预览不是完整战场模拟；墙体、移动、受击与相机仍需在 SpringUpDemo 试玩。最新剩余工作以 [试玩验收清单](Docs/待确认与试玩验收清单.md) 为准。旧的第二阶段交付清单已删除。
