# Bootstrap 输入、背包与商店验证

> 这是 main 合并前的历史验证记录。2026-10-17 整合已保留 A 的自动攻击，旧充能/改键功能及其测试已删除；下文的旧失败和操作说明不代表当前版本。最新结果见 [main 与 A 整合记录](Docs/2026-10-17-main与A整合记录.md)。

日期：2026-10-15。Unity 6000.5.11f1，主工程 SpringUp，本地分支 feat/bootstrap-input-shop。

## 结果

- 最终 Unity EditMode 全量测试：26 项，25 通过，1 项既有测试失败。本次攻击专项 4/4 与流程核心测试均通过。
- 唯一失败：EnemyFlashChecks.DamageFlashRestoresColorAndSurvivesReuse，第 32 行固定断言敌人预制体为 6 个，当前 Resources/Enemies 中实际为 7 个。本次没有修改该测试或敌人资源。
- SceneFlowChecks.RunBatch：从 Bootstrap 进入真实 Play Mode，带图形渲染完整通过。验证实际 B 打开、Tab 关闭/打开、Esc 返回游戏、战斗背包只读、休整换装、无出售按钮、商人距离、购买/出售和重复出售保护、库存及骨髓刷新、容量限制、嵌套暂停、取消换绑、七波胜利、战败、重开和加载失败恢复。
- IntegratedFlowChecks.RunBatch：旧 SpringUpDemo 的主动攻击、实验室自动预览、共享库存、容量、下一波、七波和失败重开回归通过；其节奏、稀疏槽位等 EDIT 回归亦通过。
- ChargeAttackChecks：零消耗连续攻击及最后出手间隔、快速重试冷却、多器官松键完成当前轮、充能不足持续等待恢复、无目标等待和后续目标丢失退款、跨波释放门槛、键盘换绑/冲突、旧配置回退、鼠标左右键/持久化、捕获启动输入释放、鼠标 UI 点击后持续按住不穿透、纯展示图像不阻止攻击、焦点恢复释放门槛均已自动验证。
- 检查了生成的中文主菜单、背包和商店截图。背包无出售按钮，休整角落为快捷键提示；背包/商店布局可用。

## 复现

在没有其他编辑器占用工程时执行 Unity；若工程被占用，将 Assets、Packages、ProjectSettings 同步到独立验证副本后执行。

```text
Unity.exe -batchmode -nographics -projectPath <SpringUp> -runTests -testPlatform EditMode -testResults <results.xml> -logFile <tests.log>
Unity.exe -batchmode -projectPath <SpringUp> -executeMethod SpringUp.EditorChecks.SceneFlowChecks.RunBatch -logFile <bootstrap.log>
Unity.exe -batchmode -nographics -projectPath <SpringUp> -executeMethod SpringUp.EditorChecks.IntegratedFlowChecks.RunBatch -logFile <legacy.log>
```

SceneFlowChecks 包含故意加载 Missing.unity 的失败恢复用例，其预期异常不代表回归失败。判定以最后的 CORE PASS / PLAY PASS 为准。

本次结果位于工作区外层 input-delivery.xml、input-delivery.log、input-bootstrap-final2.log、input-legacy.log；UI 截图位于 SpringUp/Logs/Presentation。日志和缓存不提交。

## 实现边界与未验证项

- 商人为青绿色占位物，交互半径 2；正式 NPC 可替换 FlowContent 的 merchant 引用并复用 FlowApplication.TryOpenShop。
- 出售仅列出未装备库存，不自动拆卸；价格由 Inventory.SalePrice 与既有 Sell 共用。背包仍可在休整丢弃，包括原有允许丢弃的已装备物品。
- 轮次复用最后一个器官的 StepInterval，最低为 BodyRuntime.MinimumStepInterval；没有另加平衡参数。
- 键鼠测试采用 Input System 合成输入；焦点恢复通过 Unity 回调模拟。未进行真实设备热插拔、人工 Alt-Tab 长时间游玩或独立发布构建验证。
- 本次主工程启动时 Git 工作树干净，未发现适用 AGENTS.md。没有占用中的用户 Unity 编辑器，因此验证直接使用主工程。Unity 生成的无关 ProjectSettings 序列化变化已排除。
