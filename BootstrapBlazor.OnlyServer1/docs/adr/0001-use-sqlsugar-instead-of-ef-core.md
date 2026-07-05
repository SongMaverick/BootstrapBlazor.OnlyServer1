# 0001 - 用 SqlSugar 替代 EF Core

## 状态

已实施 (项目立项初期)

## 背景

项目早期使用 EF Core + SqlServer,后切换到 SqlSugar 5.x。触发原因:

- EF Core 的 `ChangeTracker` 在大批量插入(迁移 600+ 条订单)时内存占用大,慢
- Code First + 自定义 Identity Store 实现复杂(需重写 `IUserStore` 的 EF 实现)
- 团队熟悉 Dapper/SqlSugar,EF Core 的 LINQ-to-SQL 翻译在某些场景不可控

## 评估过的方案

### 方案 A: 继续 EF Core
- 优点: 微软官方,文档全
- 缺点: Identity 自定义 Store 复杂;大批量性能差;LINQ-to-SQL 不可控

### 方案 B: SqlSugar 5.x ✅
- 优点: 国产 ORM,中文文档;Code First 简单;支持原始 SQL 混合;Identity Store 实现直接
- 缺点: 英文文档少;生态不如 EF Core 大

### 方案 C: Dapper
- 优点: 极致性能
- 缺点: 无 Code First,迁移全靠 SQL 脚本;Identity Store 需完全手写

## 决策

选 **SqlSugar**。

主要理由:
1. 业务规模不大(单办公网),SqlSugar 性能足够
2. 中文文档 + 中文社区,团队维护成本低
3. Code First + AOP + 自定义 Identity Store 都比 EF Core 简单

## 后果

### 正面
- Identity Store 实现简化(见 ADR 0002)
- 启动期 SQL 日志通过 AOP 一行配置接入 NLog
- 大批量插入性能比 EF Core 快约 3-5 倍

### 负面 / 风险
- 升级 SqlSugar 版本需仔细测试(`5.1.4.214` 之后接口可能变)
- 部分高级 LINQ 写法不被支持,需降级为 SQL 字符串

### 中和
- 关键 SQL 写入 `Logs` 表(AOP 自动),便于排查
- 复杂查询走 `_db.Ado.GetIntAsync(sql)` 而非 LINQ

## 相关

- `Data/SqlSugarSetup.cs` — SqlSugar 客户端 + AOP
- 所有 `Services/*.cs` — ORM 用法
- 迁移工具: `BootstrapBlazor.DbMigrator/`