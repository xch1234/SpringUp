# 独立场景流程

## 运行入口

Unity 6000.5.11f1 打开 Assets/Scenes/Flow/Bootstrap.unity，或菜单 Tools > SpringUp > Flow > Open Bootstrap，然后 Play。
Bootstrap 是常规 Build Settings 中的新构建首场景；既有 SpringUpBuild.WindowsBatch 仍专门构建旧 Demo。它始终加载，MainMenu / Battle / Rest 用 Additive 装载；稳定状态恰好一个内容场景。
不要单独 Play 内容场景，其 World 默认关闭，必须由入口注入依赖后启用。
场景和 Window.prefab 随仓库提供；Generate Missing Scenes 仅生成缺失文件，不覆盖现有场景。

新局 → 战斗 → 休整 → 下一波；第七波胜利或玩家死亡进入结算窗口。Esc 打开暂停。
WASD / 方向键移动，Shift 疾跑，F 攻击（设置中可重绑定）。
休整有黄色木桩、背包、固定测试商品商店、重置木桩、下一波入口。
战斗角落有提前完成波次、模拟死亡、验证换装锁定的开发按钮。

## 职责与生命周期

- RunConfiguration：从序列化 TextAsset 和 OrganCatalog 装配并校验配置，供新流程和旧 SpringUpRunDriver 共用。沿用现有 Demo 的蜘蛛巢奖励替换 TNT 规则。
- RunController / Inventory：唯一波次、装备、背包、货币真相源。新代码使用 RunPhase 和明确参数事件；旧 Phase / Emit 接口为 Demo 保留。
- RunSession：纯 C# 会话，持有 RunController、生命与阶段间充能快照，不持有场景对象。新局创建新会话，放弃时释放；没有存档。
- FlowApplication：Bootstrap 的流程入口、受保护的异步加载、全局窗口协调。场景通过根节点的 FlowContent 定位，不全局搜索玩家/管道。
- FlowContent：序列化地图、玩家、波次、攻击管道引用。Initialize 接收依赖，Begin 开始玩法，Stop 保存生命/充能并解绑事件。新流程没有实验室预览依赖。
- PauseService：可幂等释放的暂停凭证。暂停与游戏阶段分离，窗口和加载分别持有凭证；最后一个凭证释放后才恢复时间。
- FlowWindow / Window.prefab：简单布局和控件。UI 调用 RunController 交易/换装接口，不直接扣钱或写库存。窗口栈只允许最上层交互，关闭时恢复焦点。

Bootstrap 只持有当前内容引用；退出场景先清理窗口与场景订阅、取消攻击，再清敌人/池、重置 PlayerLocator，卸载旧场景，加载并初始化新场景。
加载期间禁用世界输入，协程使用异步操作，不依赖 scaled time。失败关闭遮罩，提供重试或返回主菜单。
新场景成功设为 active scene 后才创建运行对象，弹丸/池/攻击效果归属内容场景。
仅 Bootstrap 有 EventSystem；菜单/加载相机与地图相机互斥。

## 状态规则

生命跨地图保存；休整禁用自动生命恢复，不隐式回血。充能以正常速率恢复，攻击正常扣费；每波战斗进入时充满。
切换、换装、重置木桩时取消待执行攻击，按原 PlayerChargeAttack 规则退还尚未执行部分的费用；清理状态效果、黑洞、攻击表现和弹丸。
进入新地图不复制冷却/临时效果/执行进度。换装成功立即更新休整管道并重置进度，下一波从同一库存装配。
战斗拒绝换装、出售、丢弃及购买。奖励可以造成背包超限；超限不得进入下一波，购买必须有空位且货币充足。
木桩复用 EnemyRuntime / BattleOrganCombat，默认 TrainingTarget 保底 1 HP，不发死亡事件，不奖励、不推进波次。
未来可击杀测试目标可以关闭 TrainingTarget，并使用独立测试目标生成/重置入口；不要把目标击杀接到 RunController 的波次奖励上。

Esc 顺序：取消重绑定 → 关闭最上层设置/提示/确认 → 关闭背包/商店 → 关闭或打开暂停。
结算/加载失败窗口不被 Esc 隐藏；使用其明确出口。返回主菜单需放弃确认。
暂停既冻结时间，也通过 PlayerMotor.InputAllowed 和 PlayerChargeAttack 的权限委托屏蔽直接键盘操作，UI 不受影响。

## 扩展

新增窗口：增加 WindowKind 和 OpenWindow 内容，使用 Push / CloseTop 管理暂停与焦点；不要自己写 Time.timeScale。
业务写操作放 RunController（校验阶段、容量和货币后事务提交），窗口只请求并显示结果。
新增地图：独立内容场景，添加 FlowContent，序列化依赖并保持 World 初始 inactive，加入 Build Settings，再通过 Switch 加载。
替换地图资源无需复制战斗实现。新增身体部位玩法应先扩展装备到管道/属性的适配，再扩展背包 UI。

## 占位与限制

背包复用 SpringUpDemo 的实验室布局、物品卡片和部位槽位，支持选择物品与槽位后安装/替换、拆卸、出售及丢弃；背包溢出后可以滚动。不含拖拽。新窗口不携带实验室战斗预览或演示库存，关闭背包后在休整地图测试真实构筑。
商店只有固定 rust_knife 商品，TryPurchase 是正式业务边界；旧 TryBuy 保留旧 Demo 的 not_open 兼容行为。
设置仅攻击重绑定；无音量、存档、继续游戏、随机商品或刷新。
休整沿用灰盒场地，是可移动可攻击地图。结算是窗口。
键盘焦点使用基础 Unity 导航；滚动列表键盘焦点自动滚入视区尚未实现。
失败重试会重建目标地图，不回滚已经合法结算的波次/奖励，也不再次结算奖励。

## 旧 Demo

Assets/Scenes/SpringUpDemo.unity、LaboratoryDemo.unity、Battle_Greybox.unity 仍可直接打开并 Play。
旧统一 Demo 继续使用其原实验室与暂停预览；新 Bootstrap 不接管旧场景。
不要在同一播放会话中手动 Additive 加载旧 Demo 和新流程，二者是独立入口。

## 验证入口

- SceneFlowCoreTests：嵌套暂停、奖励幂等、容量与购买事务（EditMode）。
- SpringUp.EditorChecks.SceneFlowChecks.RunBatch：独立副本 batchmode，不加 -quit；运行真实 Play Mode 多场景七波检查。
- SpringUp.EditorChecks.IntegratedFlowChecks.RunBatch：旧统一 Demo Play Mode 回归。
- SpringUp.LaboratoryEditor.LaboratoryPlayChecks.Run：旧实验室 Play Mode 回归。
- SceneFlowChecks 中故意请求 Missing.unity，验证加载错误可恢复；该异常日志是预期故障注入。

手工验收：从 Bootstrap 开局，移动/攻击，Esc→设置→重绑定→Esc 逐层退出；提前结算，打开背包换刀，关闭后攻击木桩，再进入下一波核对构筑。
暂停状态点返回主菜单，先取消再确认，重新开局核对生命、金币、波次。测试长背包列表滚动、鼠标点击、不同窗口尺寸与焦点表现。
另打开两个旧 Demo 核对可视化表现。自动 batchmode 检查不能替代画面、音频及最终交互手感验收。

实际运行结果与全量测试的已知失败见 [GameFlowValidation.md](GameFlowValidation.md)。

## 地图平铺与编辑视图

Battle_Greybox、新流程 Battle 和 Rest 共用 `Assets/Prefabs/Flow/ArenaTiles.prefab`。
地图仍为 72×72 世界单位，40×40 格，每格 1.8×1.8：4 个角各使用一次，四条边各重复 38 次，中心重复 38×38 次。
480×480 源图保持方形像素比例，统一缩放；没有将中心或边缘拉伸成大块。使用与原战场一致的 URP Unlit 材质。
旧 SpringUpDemo 原有纯色地图和 LaboratoryDemo 场景布局不变。

屏幕空间 Canvas 在 Scene 视图中的边框不代表世界碰撞。无需移动战场或改刷怪、相机坐标：
`Tools > SpringUp > Scene View > Hide UI for Map Editing` 隐藏 Scene 视图中的 Canvas 和 EventSystem，
`Show UI` 恢复。此操作仅影响编辑视图，Game UI 仍正常显示与交互。
新流程常驻 UI 和实验室 Prefab 使用 UI 图层；该工具也处理旧 Demo 的 Canvas。
EventSystem 只负责 UI 输入，不绘制游戏画面，不参与物理碰撞。Scene 中看到的是编辑器辅助显示；也可在 Hierarchy 左侧用眼睛图标隐藏它。不要取消对象或组件的启用勾选，否则 UI 输入会失效。

玩家保留运动学刚体。`PlayerMotor` 在设置速度前用实际碰撞形状向墙层扫掠，限制朝墙方向的速度分量，同时保留沿墙移动；测试覆盖四边、角落、疾跑与极高速度。无需移动场景原点或修改用户的输入引用。
新流程标题、状态、按钮和业务提示使用简体中文；业务错误码保持稳定，在显示层翻译。通用窗口按宽度排版并自动计算文本换行高度。

`LaboratoryWindow.prefab` 是从 SpringUpDemo 实验室提取的独立窗口，
`FlowLaboratoryWindow` 显式绑定 `RunLaboratoryEquipment`，订阅装备变更并随窗口销毁取消订阅。
全局窗口栈负责暂停、Esc 和焦点恢复，`LaboratoryPresenter.Bind` 支持脱离演示入口复用；旧 Demo 的 `source` 初始化兼容保留。
`Tools > SpringUp > Flow > Repair Terrain and Import Laboratory` 是显式迁移工具，会替换上述三个地图的 MapBackground 和重建新实验室 Prefab；日常编辑直接修改 Prefab，无需重复运行迁移。
