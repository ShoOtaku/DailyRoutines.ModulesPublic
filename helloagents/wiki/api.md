# API 说明

## 范围说明
- 本仓库为插件模块代码仓库，不提供独立 HTTP/REST API
- 对外行为主要通过插件命令、游戏 UI 交互和事件钩子体现

## 交互接口类型
- 命令型接口: 位于 `Assist/` 目录中的命令类模块
- UI 操作接口: 位于 `UIOperation/`、`UIOptimization/` 的界面行为模块
- 系统增强接口: 位于 `System/`、`General/` 的行为拦截与增强模块

## 文档维护规则
- 若后续新增可编程外部 API（HTTP、IPC、消息总线），需在本文件新增正式接口定义
- 若仅为内部方法签名变化，优先记录在对应 `wiki/modules/<module>.md`

