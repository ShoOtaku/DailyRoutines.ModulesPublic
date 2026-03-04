# 代码风格与代码规范

> 本文档是 `DailyRoutines.ModulesPublic` 的代码风格与静态检查规范 SSOT。  
> 若与临时口头约定冲突，以本文档与仓库配置文件（`.editorconfig`、`.csproj`）为准。

## 1. 基础格式规范
- 编码: UTF-8
- 换行: LF
- 文件末尾: 必须保留结尾换行
- 缩进: 4 个空格，禁止 Tab

配置来源:
- `.editorconfig`

## 2. C# 代码风格

### 2.1 换行与大括号
- `catch` / `else` / `finally` 前必须换行
- 大括号遵循 `csharp_new_line_before_open_brace = all`
- 对象初始化器成员不强制在新行

### 2.2 var 与类型声明
- `var` 在内置类型、类型明显和一般场景均允许（建议级别）
- 可读性优先: 当右值类型不明显时允许显式类型

### 2.3 括号可读性
- 算术、关系、其他二元表达式建议加括号以提升清晰度
- 不因为“能省略”而主动移除有助阅读的括号

### 2.4 空格与标点
- 方法调用/声明参数括号内侧不加空格
- 方括号内不加空格，逗号前无空格、后有空格
- 二元运算符前后保留空格
- 控制流关键字后保留空格（如 `if (...)`）

### 2.5 表达式体成员
- 单行场景下可使用表达式体（方法/属性/构造函数）
- 多行逻辑优先使用块体，避免压缩可读性

## 3. 静态分析与告警策略

### 3.1 分析器
- 启用 `DailyRoutines.CodeAnalysis`（`PrivateAssets=all`）
- 分析器资产包含 `analyzers` 与 `buildtransitive`

### 3.2 编译级约束
- `Nullable = enable`
- `LangVersion = latest`
- `AllowUnsafeBlocks = true`

### 3.3 告警处理原则
- 项目存在 `NoWarn` 白名单（见 `.csproj`）
- 新增忽略项必须满足:
  - 说明业务必要性
  - 记录影响范围
  - 可回溯到提交或任务背景

## 4. ReSharper 约定
- 多行参数、调用链、LINQ 查询等保持对齐
- `if/else` 多行场景必须使用大括号
- 禁止将简单 `try` 块压缩为单行
- 若 ReSharper 与 `.editorconfig` 冲突，以仓库文件当前配置为准

## 5. 目录与模块组织规范
- 按功能目录组织模块（如 `Action/`、`System/`、`UIOptimization/`）
- 通常一个 `.cs` 文件对应一个独立功能模块
- 公共 `using` 集中在 `GlobalUsings/`

## 6. 提交前检查清单
- 代码格式符合 `.editorconfig`
- 新增/修改逻辑通过本地构建
- 不引入新的高严重度分析器问题
- 已同步更新对应知识库文档:
  - `helloagents/wiki/modules/<module>.md`
  - 必要时更新 `helloagents/project.md` 与本文件
  - 在 `helloagents/CHANGELOG.md` 记录规范变更

## 7. 维护规则
- 规范变更必须先改知识库，再改代码实现
- 若代码现实行为与文档冲突，应以代码为准修正文档
- 每次引入新分析器或大规模风格调整时，必须更新本文件

## 8. 相关清单
- 模块开发执行清单: `helloagents/wiki/module-dev-checklist.md`
