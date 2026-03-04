# Changelog

本文件记录 `helloagents/` 知识库的重要变更，格式参考 Keep a Changelog。

## [Unreleased]

### Added
- 新增 `helloagents/wiki/coding-style.md`，沉淀项目代码风格与代码规范
- 新增 `helloagents/wiki/module-dev-checklist.md`，沉淀模块开发执行清单
- 新增 `Combat/AutoRespawnTeleport.cs`，实现复活过图后自动坐标传送（固定坐标/死亡坐标模式）

### Changed
- 更新 `helloagents/project.md`，增加规范入口与执行要求
- 更新 `helloagents/wiki/overview.md`，增加“代码风格与代码规范”快速入口
- 更新 `helloagents/wiki/coding-style.md`，增加 Checklist 关联入口
- 更新 `helloagents/wiki/modules/combat.md`，补充 `AutoRespawnTeleport` 行为说明与维护记录
- 精简 `AutoRespawnTeleport` 配置界面，移除重试参数与通知开关设置项（改为内置默认值）
- 进一步精简 `AutoRespawnTeleport`，移除聊天与通知输出逻辑
- 精简 `AutoRespawnTeleport` 安全检查逻辑，仅保留必要的目标坐标与执行状态判断
- 将 `AutoRespawnTeleport` 的重试调度由时间戳轮询改为 `TaskHelper` 队列重试，实现更简洁的重试流程
- 调整 `AutoRespawnTeleport` 配置 UI：移除死亡坐标显示，收窄固定坐标输入框宽度
- 调整 `AutoRespawnTeleport` 配置 UI：`UseCurrentPosition` 按钮改为固定宽度
- 调整 `AutoRespawnTeleport` 代码风格：重命名状态变量与重试流程方法，补充场景注释，使实现更贴近仓库手写风格
- 调整 `AutoRespawnTeleport` 枚举比较写法，移除不必要的 `(int)` 类型转换
- 重构 `AutoRespawnTeleport` 重试机制：移除 `TaskHelper` 套娃式重试，改为 `Framework.Update` 下的状态机 + 时间戳重试

## [2026-03-04] - 知识库初始化

### Added
- 初始化 `helloagents/` 目录结构
- 新增 `project.md` 技术约定文档
- 新增 `wiki/overview.md`、`wiki/arch.md`、`wiki/api.md`、`wiki/data.md`
- 新增模块文档索引 `wiki/modules/*.md`
- 新增 `history/index.md` 变更历史索引
