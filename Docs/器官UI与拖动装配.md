# 器官 UI 与拖动装配

## 在哪里调整

- `Assets/Resources/EquipmentHud.prefab`：战场左上角 HUD。根节点控制位置和整体尺寸，子节点控制各槽位与伤害记录面板。
- `Assets/Prefabs/Flow/LaboratoryWindow.prefab`：正式流程背包。可调整背景、装备格、库存区、右侧详情和底部按钮的 RectTransform。运行时不会重新生成这些位置。
- `Assets/Resources/OrganArtCatalog.asset`：器官配置 ID 与 Sprite 对应表。现有 7 张图已经绑定。补齐美术时将图片作为 Sprite 导入，添加或更换对应条目即可。JPG 白底来自原图，建议后续使用透明 PNG。
- 每个槽位的 `OrganIconView`：可调整 `pulseDuration`、`pulseColor`、合法/非法放置颜色，以及图标、选中层、拖放层、闪亮层的位置。没有图时显示器官名称。

背景中的文字和格框是图片的一部分。移动交互组件不会移动图片上的格框；若要大幅改变视觉布局，需要更换背景或拆分美术。

`Tools > SpringUp > UI > Build Organ UI Assets` 是显式重建命令，会用代码中的默认布局重新保存上述 Prefab 和图标映射。日常微调直接编辑 Prefab，不必运行这个命令。原有“Repair Terrain and Import Laboratory”也会调用此重建。

## 玩家操作

- 点击库存或装备器官：查看详情。长说明可以滚动。
- 库存拖到装备槽：装配；目标已有器官时，原器官回到库存。
- 装备拖到背包区域（含已有物品的格子）：卸下；界面自动切到卸下物品所在页。
- 装备拖到其他装备槽：移动或交换。两个器官都必须符合目标部位规则。
- 松开在非法位置、切换库存页、关闭窗口：取消拖动。丢弃只能通过明确按钮执行。
- 库存每页显示 14 格，没有容量限制；分页不改变库存顺序。
- 战斗中只能选中查看，休整期间可以修改装备。背包打开时世界暂停。

普通器官可以放入头、手、腿；特殊器官只允许躯干。槽位从左向右排列，换位会改变触发关系。交换在 `Inventory.MoveOrSwap` 中先校验双方再提交，由 `RunController` 检查阶段并统一发出装备更新事件。

## 高亮复用与后续预览

`OrganIconView.Pulse()` 只负责一次视觉闪亮，使用未缩放时间，不判断战斗规则。选中、拖放提示、闪亮互不覆盖状态。

`OrganPulseBinding` 将独立 `OrganPipelineController` 的实际攻击、执行器产出和触发成功事件转换为 `(BodyPart, slotIndex)` 回调。HUD 使用该适配器，伤害记录仍由 `OrganCombatReport` 负责。适配器使用结束时调用 `Dispose()` 解除订阅。

后续实验室模拟可创建自己的管道和器官运行实例，绑定相同适配器，再调用对应格子的 `Pulse()`。不要把正式战斗管道用于模拟。当前提供 `BackpackPresenter` 组件右键菜单 `Preview selected icon pulse`，可在运行模式选择器官后演示共用闪亮；本轮未加入完整条件模拟或伤害预览。

## 定向验证

- `SpringUp.EditorChecks.OrganUiChecks`：原子移动/交换、战斗只读、拖放装卸、取消、实例选择、分页、缺图显示、真实触发链高亮，以及进入运行模式后的 UI 射线和暂停动画测试。
- `SpringUp.EditorChecks.OrganUiChecks.RunBatch`：上述编辑模式逻辑检查、已有战斗报告回归，以及 1440×900、1280×720、1024×768 的界面边界与截图检查。截图输出到 `Logs/OrganUI`。
- `BuildAndCheck`：先重建资源再执行 `RunBatch`，仅用于需要重建默认布局的验证副本。

原有实验室演示场景仍使用旧 `LaboratoryPresenter`；正式游戏背包使用新的 `BackpackPresenter`。旧场景不会自动迁移。
