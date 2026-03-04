# 项目技术约定

## 项目标识
- 项目名称: Daily Routines Modules In Public
- 维护者: AtmoOmen
- 当前版本: 1.0.0.0
- 项目描述: Help with some boring daily tasks

## 技术栈
- 语言与平台: C# / .NET（由 `Dalamud.CN.NET.Sdk` 管理）
- 插件 SDK: `Dalamud.CN.NET.Sdk/14.0.1`
- 主要依赖:
  - `DailyRoutines.CodeAnalysis`（分析器）
  - `TimeAgo.Core`

## 编译与运行约定
- 常用配置: `Debug`、`Release`、`ReleaseTest`
- 构建命令: `dotnet build DailyRoutines.ModulesPublic.csproj -c Debug`
- 代码约束:
  - `Nullable=enable`
  - `LangVersion=latest`
  - `AllowUnsafeBlocks=true`

## 代码组织约定
- 按功能目录拆分模块（如 `Action/`、`System/`、`UIOptimization/`）
- 单个 `.cs` 文件通常对应一个独立功能模块
- 公共 `using` 约定集中在 `GlobalUsings/`

## 质量与流程约定
- 静态分析: 通过 `DailyRoutines.CodeAnalysis` 与编辑器规则协同
- 变更后要求:
  - 同步更新 `helloagents/wiki/modules/<module>.md`
  - 在 `helloagents/CHANGELOG.md` 记录文档或行为变更

## 代码风格与规范入口
- 统一规范文档: `helloagents/wiki/coding-style.md`
- 配置事实来源:
  - `.editorconfig`
  - `DailyRoutines.ModulesPublic.csproj`
- 执行要求:
  - 代码风格以配置与规范文档为准
  - 新增风格约束或告警策略时，先更新知识库再落地代码
