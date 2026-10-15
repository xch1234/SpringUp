# 场景重构验证记录（2026-10-15）

Unity 6000.5.11f1，在工作区旁的 FlowValidation 独立副本中运行 batchmode / nographics；没有占用用户打开的 Unity 会话。隐藏批处理验证副本启用了 runInBackground，项目本身设置未改。

## 已执行

- 场景生成器：编译成功，生成四个场景和窗口 Prefab；旧场景文件未修改。
- SceneFlowChecks 核心检查：暂停凭证嵌套/重复释放/旧凭证失效、容量限制、购买事务通过。
- SceneFlowChecks Play Mode：七波流程（开发接口提前结束波次）、胜利、战斗/休整死亡、生命和装备保留、充能消耗与每波补满、休整换装和真实木桩伤害、容量与重复请求保护通过。
- 同一流程检查：真实键盘移动和攻击、嵌套窗口、Esc 取消重绑定、鼠标关闭设置取消捕获、暂停返回主菜单再开新局、暂停期间已就绪敌人及重叠弹丸不结算、加载错误恢复通过。
- 每次稳定切换断言 Bootstrap + 一个内容场景、唯一 EventSystem、唯一生效 AudioListener、无旧自动流程驱动；休整只登记一个木桩。
- 故意请求 Missing.unity 触发加载失败，遮罩关闭后可返回主菜单；这是日志中的预期异常。
- 退出 Play 时窗口先销毁的失效引用已修复；后续流程日志不再出现该 MissingReferenceException。
- IntegratedFlowChecks.RunBatch：旧 SpringUpDemo 原流程、实验室共享配置、暂停预览、七波、重开、死亡通过。
- LaboratoryPlayChecks.Run：旧 LaboratoryDemo 通过。

## 全量 EditMode 结果与限制

全量 NUnit 首轮 23/24，后续复跑 22/24；新增 SceneFlowCoreTests 两项通过。以下旧测试保持原样，未为了全绿削弱断言：

1. EnemyFlashChecks.DamageFlashRestoresColorAndSurvivesReuse：断言敌人资源数为 6，但当前 main 原本已有 7 个 Prefab（含 Dummy）。移除数量差异后的临时诊断还发现部分现有战斗 Prefab 未附加闪白组件；诊断改动已撤回。该问题不属于本次流程重构。
2. ChargeAttackChecks.ManualInputChargeRoundsAndRebinding：首轮通过，隐藏批处理复跑曾超时；启用后台运行后出现未收到模拟 F 键的断言失败。新入口的独立 Play Mode 检查已实际注入攻击按键并验证扣血/扣充能通过；仍不能把全量测试标为通过，需在可见 Unity Test Runner 中复核该旧输入测试。

批处理还偶发 UnityEditor.Search.SearchDatabase 启动索引异常，位于编辑器包内部；独立流程检查仍完成并以 0 退出。
未执行有画面的人工游玩、屏幕截图审查、音频或构建成独立 Player 的验收。

## 人工复核

打开 Bootstrap，验证真实键盘攻击、移动与窗口焦点；休整换装后攻击木桩，再进下一波。
尝试长背包滚动、不同 Game View 尺寸、鼠标和键盘切换、暂停/商店/确认框嵌套。
让一波自然计时结束（自动流程检查使用提前完成接口加速），观察敌人和弹丸表现。
打开 SpringUpDemo、LaboratoryDemo 观察原 UI 和预览。
在可见 Test Runner 复核上述两个旧测试；不用修改业务代码来绕过断言。

## 本地日志（不入 Git）

FlowValidation/flow-keyboard-final.log（最终新流程）、legacy-flow-release.log、legacy-lab-release.log；
全量结果 edit-verified-results.xml，早期对照 edit-results.xml / edit-final-results.xml。

## 2026-10-15：地图平铺与实验室移植复验

在独立 `FlowValidation` 工程、Unity 6000.5.11f1 中执行；未接管用户打开的编辑器。

- `FlowPresentationRepair.RepairAndVerify`：通过。三个地图改为 20×20；四角各 1、四边各 18、中心 324 个等比例格子。
- `FlowPresentationChecks.Run`：通过。实际渲染地图及实验室 PNG；检查 1440×900、1280×720、1024×768 的按钮/背包视口边界，以及超容量背包滚动到底部。已查看地图、1280 和 1024 实验室渲染图。发现并修复共用默认材质导致贴图串用的问题，现为九种地形各自显式绑定纹理的 Unlit 材质。
- `SceneFlowChecks.RunBatch`：通过。换装检查现在实际调用移植界面的物品选择、槽位选择和安装按钮；无演示库存、无内部战斗预览。七波胜负、跨地图生命/装备、木桩伤害、暂停/Esc、重复请求及故障恢复检查通过。
- `IntegratedFlowChecks.RunBatch`、`LaboratoryPlayChecks.Run`：均退出码 0，旧 Demo 回归通过。
- 日志：`FlowValidation/presentation-final.log`、`presentation-legacy-flow.log`、`presentation-legacy-lab.log`。渲染图：`FlowValidation/Logs/Presentation/`。

测试中的 Missing.unity 是预期故障注入；仍可见 UnityEditor.Search 的启动异常，与此前相同。此次未重跑全量 NUnit，此前记录中的已知失败不视为修复。
尚需人工确认 Scene 视图隔离菜单的编辑体验、真实鼠标/键盘导航手感；未做独立 Player 构建。第 1 项按 Scene 编辑边框重叠处理，未将战场整体平移，也未据此改动 Game 视图布局。

## 2026-10-15：边界、40×40 与简体中文

- `FlowPresentationRepair.UpdateTerrainAndVerify`：生成 1600 格共用地图；验证四角各 1、四边各 38、中心 1444，每格 1.8×1.8。已查看实际渲染图。
- `ArenaBoundaryChecks`：从 Battle/Rest 复制真实墙体与玩家碰撞形状到独立物理场景，使用真实 PlayerMotor 扫掠和物理模拟，验证四边、四角、15/2000 单位每秒速度、沿墙移动；两场景均通过。
- `SceneFlowChecks.RunBatch`：最终中文排版版本完整七波流程通过；生成并查看中文主菜单和背包截图，确认长提示不再被 100 宽的旧布局截断。
- Scene 视图隐藏 EventSystem 后检查编辑器隐藏标记成立，同时 EventSystem 和 Canvas 仍启用；恢复显示后继续完整流程通过。
- 旧 IntegratedFlowChecks、LaboratoryPlayChecks 回归均退出码 0。未重跑全量 NUnit，也未做 Player 构建。
- 日志：`FlowValidation/boundaries-chinese.log`、`boundaries-chinese-layout.log`、`boundaries-legacy-flow.log`、`boundaries-legacy-lab.log`。截图在 `FlowValidation/Logs/Presentation/`。
- 原有 `Battle_Greybox.unity` 未提交的输入引用修改保持原样，不包含在本次提交中；地图通过共用 Prefab 更新。仍需人工试玩确认碰墙手感。
