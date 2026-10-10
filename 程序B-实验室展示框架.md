# 程序 B：真实配置装配与结算预览

更新：2026-10-10；当前集成分支 `main_temp`。入口为 `Assets/Scenes/LaboratoryDemo.unity`，或 `Tools > SpringUp > Open Laboratory Scene`，然后 Play。

## 操作

左侧保留头、手、腿三条六槽和躯干槽。选择背包物品及匹配槽位后安装／替换；只选已装备槽位可拆下。再次点击取消选择。卡片显示库存实例短编号，选中后显示名称、类型、实例短编号和来自真实配置的数值说明。同种器官的完整库存 InstanceId 独立，DefinitionId 相同。

初始手部第 1 槽拳头、第 4 槽多肢触手。临时库存每种手部器官共两件（含初始装备），另有头、腿、躯干各三件明确的装配占位，不产生战斗效果。退出 Play 后恢复，未实现正式库存与存档。

成功安装、替换、拆下、躯干缩容或手动“重新开始”会清空整套预览并按当前装备重建。仅选择／取消选择不重置、不暂停。替换退回、拆下及缩容退回继续使用原装备事务。

## 唯一配置来源

`Assets/Resources/OrganCatalog.asset` 显式引用 `Assets/Data/Organs/Hand/` 的七份原资源；没有第二份伤害、概率、范围或时间配置。`OrganCatalog.CreateDefinitions()` 调用 A 的 `OrganConfig.TryCreateDefinition()`，启动时校验七份引用、重复 ID 和状态关联。缺失或错配在 Console 和右侧显示错误，停止预览，不回退假数据。

`LaboratoryDemoBootstrap` 将同一份只读 OrganDefinition 快照交给卡片和战斗。停止 Play 后修改 A 的资源并保存，再 Play，显示与结算同时更新；运行中的旧快照不热更新。目录可供其他模块查询，运行时不使用 AssetDatabase。状态仍引用 `Assets/Data/Statuses/` 的 Stun、Bleed、Slow。

| 器官 | 预览方式 |
| --- | --- |
| 拳头 | 按配置单目标伤害 |
| 钢管 | 直接伤害；按真实概率施加眩晕，文字显示剩余时间 |
| 锈刀 | 直接伤害；按真实概率施加流血，按间隔真实扣血并显示剩余时间 |
| 粘液腺 | 直接伤害；按真实概率叠加减速，显示层数、实际减速比例和下次减层时间 |
| 多肢触手 | 击杀时追加执行本部位，日志显示来源槽位、追加执行及命中；同链每实例最多一次，深度上限仍为 A 的规则 |
| 坍缩体 | 创建真实 BlackHoleBehaviour，按配置到期；显示剩余／总持续时间、半径、范围内存活目标；不展示位移 |
| TNT | 用 AttackResolver 根据真实世界坐标及爆炸中心判定；整批命中结算后才处理击杀追加 |

概率没有为展示调高。仅装触手不会造成伤害；只有黑洞也不扣血。状态可能需要多次攻击才成功施加。

## 两个固定目标

默认值集中在 `LaboratoryCombatDemo`（场景 Combat 物体）Inspector：

| 参数 | 默认值 |
| --- | --- |
| Target Health | A、B 均为 30；与装备无关 |
| First Position / Second Position | 世界坐标 (0.75, 0)、(1.75, 0)，间距 1 |
| Explosion Center | (0, 0)，同时为黑洞生成中心 |
| Replenish Delay | 死亡后 1.5 秒 |

画面方块是两个世界目标的 UI 投影，不以 Canvas 像素坐标结算。目标实际 Transform 固定在以上位置。默认 TNT 半径可覆盖两者；将 A 的 TNT 半径改为 0.75 时只命中边界上的 A，B 不受伤。目标血量、位置、存活／死亡和补充倒计时可见。

每次死亡只通知一次，清除状态及延迟伤害来源；延迟后以新 TargetId 满血补充，不广播击杀、不发奖励。存活目标、管道节拍与正常游标不重置。全部死亡时允许短暂空场，单目标攻击不伪造命中，范围攻击允许命中 0 个。新身份拒绝命中旧目标编号的攻击。

下方“当前目标状态”独立常驻；“最近事件”最多六条。黑洞可以按实例并存，同实例再次生成替换旧黑洞。减速比例由 A 的 SlowMultiplier 读取，不混入眩晕的零速度。

## 接入与边界

- `TemporaryEquipment` 保持库存身份和守恒；`LaboratoryItem.Definition` 保存共享快照。
- `LaboratoryCombatDemo.InventoryOrigins` 将独立战斗实例映射回原库存 InstanceId；事件来源同时标识部位和槽位。预览重建不修改库存身份。
- 复用 A 的 OrganPipelineController、AttackResolver、DemoTarget、StatusEffectRuntime、BlackHoleBehaviour 和 CastContext。B 只负责接线、目标生命周期及 UI，不复制伤害或状态算法，不调用 OrganDemoSetup。
- Play 中目标放入独立临时 Unity 场景，关闭时清理并卸载；只对两个私有目标结算，不订阅／发布 C 的全局战场事件，不写正式库存或奖励。
- 流血保留原上下文；范围命中先完成整批扣血，再发布原攻击的死亡信号；状态批次也先结算，再处理追加。原链防重复与深度限制有效。
- 不展示普通移动、眩晕移动变化、黑洞位移、范围圈或粒子。这是简化装备效果和结算预览，不是完整战场模拟，也不代表 Event → Event 已实现。
- 本次不接正式掉落、商店、存档、下一波、C 战场或休整区木桩。

## 验证

检查入口：`Tools > SpringUp > Check Laboratory UI`（先停止 Play、关闭实验室场景），含 `LaboratorySettlementChecks` 与原 `TemporaryEquipmentChecks`；A 完整入口为 `Tools > SpringUp > Run All Organ Checks`。真实 Play 自动入口为 `SpringUp.LaboratoryEditor.LaboratoryPlayChecks.Run`，批处理不加 `-quit`，结束自动退出。

本轮使用 Unity 6000.5.11f1，在 `D:/gamejam/2026TapTap/LaboratoryStage2Validation` 的源码快照中，以本地 uGUI/Input System 和所需内建模块运行。未修改主工程依赖。这不等于完整包环境、URP、正式项目打包或物理鼠标人工验收通过。

- A 完整 51 组通过，含 49 对有序器官组合、延迟流血原链、深度上限、范围多目标击杀与黑洞生命周期。
- B 行为检查覆盖配置伤害／半径修改同步、七种装卸、重复实例映射、真实范围边界、状态扣血与到期／减层、黑洞文字到期、死亡一次、补充新身份及无旧状态、空场恢复、补充不触发链、流血链防环、选择不重置、手动重启、重复初始化、占位隔离和数量守恒。原 500 次随机事务检查保留。
- 多分辨率截图、文字高度检查及真实 Play 最终结果见交付清单。

A 的 `OrganConsolidationChecks.CheckAssets` 原来断言 Scenes 根目录只有一个场景，本轮缩小为检查 A 的 OrganDemo 主场景存在，保留 A 归档检查；B/C 场景均保留，没有通过删场景绕过检查。

正式中文字体、热重载、跨平台、完整依赖打包及正式流程接线仍待后续验收。
