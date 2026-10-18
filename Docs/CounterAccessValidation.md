# 商店、实验室交互与装配限制（2026-10-18）

正式入口：`Assets/Scenes/Flow/Bootstrap.unity`。

- STORE / LAB 的交互点分别放在对应柜台左侧甲板，随船体背景定位；靠近时显示 E 提示，移除原商店占位方块的显示。
- B / Tab 始终打开只读背包。休整时靠近 LAB 按 E 才能装配、替换、拆卸或交换器官。
- 关闭实验室、离开范围、进入战斗或打开上层窗口后，装配权限立即失效；拖拽提交时也校验权限。
- 丢弃器官及出售已装备器官同样受限制；商店仍支持购买和出售未装备器官。
- 背包下方显示当前权限和返回游戏的按键。

## 验证

Unity 6000.5.11f1 独立副本执行 `SpringUp.EditorChecks.CounterAccessChecks.RunBatch`，最终 PASS。测试从 Bootstrap 进入战斗与休整，注入 B/E 按键并调用真实背包拖拽接口，覆盖交互点位置、不重叠范围、只读背包、装配/替换/移动/交换/拆卸、过期拖拽、关闭撤权、嵌套窗口、商店交易及装备带入下一波。拒绝操作同时检查库存、金钱和装配事件均未改变。

运行时代码、编辑器代码及测试通过 warnings-as-errors 编译。已审查实际渲染截图。未执行全量历史测试或独立 Player 构建。

副本为绕过 AI 编辑器包的导入失败，排除了非必要编辑器包；正式项目 Packages 未改。日志仍有 UnityEditor.Search.SearchDatabase 启动索引异常，发生在业务测试开始前，未影响本次定向检查。

本地日志：`Logs/CounterAccessChecks-final.log`。截图：`Logs/CounterAccessValidation/counter_positions.png`、`counter_read_only_bag.png`、`counter_laboratory.png`（Logs 不入版本控制）。

如编辑器当前仍在运行旧一轮 Play，停止后从 Bootstrap 重新运行；若 Rest 已打开，重新加载场景以读取保存后的柜台对象。
