# 贡献指南

## 开发流程

1. **建分支**:`feat/<short-desc>` / `fix/<short-desc>` / `refactor/<short-desc>`
2. **本地验证**:`dotnet build` + `dotnet test` 都需 0 错误
3. **提 PR**:至少 1 个 reviewer;PR 描述写清楚"为什么改 + 改了什么"
4. **合入后**:删除远程分支

## Commit 规范

采用 Conventional Commits:

```
<type>(<scope>): <subject>

<body>

<footer>
```

**type**:
- `feat` — 新功能
- `fix` — 修 bug
- `refactor` — 重构(不改行为)
- `docs` — 文档
- `test` — 测试
- `chore` — 构建/工具/依赖

**scope**(可选):`orders` / `users` / `identity` / `db-migrator` / `docs` ...

**示例**:
```
feat(orders): 用 filtered unique index 替代 SERIALIZABLE 事务

新增 Orders.OrderNumber 唯一约束,SERIALIZABLE 范围锁导致的 1-2 分钟
卡顿问题彻底解决。详见 ADR 0005。

Closes #42
```

## 代码规范

详细见代码本身的命名/规范约束(项目根 `.editorconfig`、各项目 `.csproj`)。要点:

- **命名空间**:统一 file-scoped(`namespace Foo;`,不用 `namespace Foo { ... }`)
- **可空性**:`<Nullable>enable</Nullable>` 已开,引用类型必须显式标注 `?`
- **错误处理**:Service 层返回 `(bool ok, string? error)` 元组;不要在 Service 内部吞异常
- **日志**:用 `ILogger<T>`,严禁 `Console.WriteLine`
- **不写大块注释**:WHAT 写在代码里,WHY 写在注释里,WHY 的决策背景写在 ADR

## 数据库变更

任何 schema 改动必须:

1. 改 Models
2. 在 `BootstrapBlazor.DbMigrator/Program.cs`:
   - `CreateAllTables` 加新表
   - `EvolveSchemaAsync` 加演进逻辑(给已存在库加列)
   - `EnsureAllIndexes` 加索引
3. 在 PR 描述里附带新表/新字段说明(暂时没有独立的 ER 图文档)
4. 在 PR 描述里写明:"需要跑 `--schema` 升级现有库"

## 架构变更

任何技术选型/架构调整必须先写 ADR:

1. 在项目根新建 `0006-xxx.md`,按 ADR 模板(标题/背景/决策/后果)填写
2. 在主 [README.md](README.md) 的文档节里登记一行链接
3. PR 合并前 reviewer 必须确认 ADR 完整

## 不要做的事

- ❌ 提交明文密码 / 连接字符串到 git(用 Secret Manager 或环境变量)
- ❌ 在 Service 里 `catch { return null }`(吞掉 bug)
- ❌ 用 `DateTime.Now` 跨时区场景(用 `DateTime.UtcNow`)
- ❌ 改业务逻辑不更新 ADR 和文档
- ❌ 把 bin/ obj/ 自定义输出提交到仓库