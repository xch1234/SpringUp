# 潮涌之躯（SpringUp）

Unity 2D URP 器官组合项目。策划目标是三条六槽管道、躯干节拍和七个波次，当前已完成最小管道与七种手部器官的独立演示。

## 从这里开始

1. 用 Unity 打开项目，等待编译完成。
2. 打开 `Assets/Scenes/OrganDemo.unity`。
3. 选中 OrganDemo 物体，在 Organ Demo Setup 的 Preset 中选择演示模式，然后 Play。

操作、数值修改、自动检查、脚本职责及未完成事项，统一阅读 [程序 A 器官系统使用说明](程序A-器官系统使用说明.md)。程序和美术队友都可以从这份说明开始。

## 当前目录

- `Assets/Scenes/OrganDemo.unity`：主要入口，支持综合组合、双触手、状态、TNT、黑洞、拳头及手动模式。
- `Assets/Scenes/Tests/`：归档的七个旧场景。
- `Assets/Data/Organs/Hand/`：唯一一套七种手部器官配置。
- `Assets/Data/Statuses/`：独立的眩晕、流血、减速配置。
- `Assets/Scripts/`：运行代码；`Assets/Tests/Editor/`：自动检查。
- `Docs/Archive/程序A-历史开发记录.md`：原分步记录与旧项目介绍，保留追溯，旧路径不作为当前使用方法。

完整检查入口：`Tools > SpringUp > Run All Organ Checks`，共 51 组。旧分步菜单在 Checks By Stage 中。

## 文档与协作

- [策划案](策划案.md)：玩法与内容依据。
- [接口草案](接口草案.md)：程序模块接入约定和实际实现范围。
- [器官模块代码架构](器官模块-代码架构.md)：架构提案，包含尚未实现的设计。
- [历史记录](Docs/Archive/程序A-历史开发记录.md)：原阶段成果、分步记录和计划。

现有实现是每个槽位独立发动效果。完整 Event → Event 事件流、正式敌人及整局流程仍需实现与联调。分工见总使用说明末尾。

提交 Unity 资源时请带上 `.meta`。`Library/`、`Temp/`、`Logs/`、`UserSettings/` 和 `.utmp/` 是缓存、日志或本机检查数据，不上传。
