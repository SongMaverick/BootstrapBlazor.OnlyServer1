# 0002 - 自定义 SqlSugar Identity Store

## 状态

已实施 (v1.0)

## 背景

ASP.NET Core Identity 默认依赖 EF Core(`IdentityDbContext`)。本项目用 SqlSugar(ADR 0001),必须实现 `IUserStore<>` / `IRoleStore<>` 接口的 SqlSugar 版本,才能让 Identity 走 SqlSugar。

## 评估过的方案

### 方案 A: 用 EF Core 做 Identity,SqlSugar 做业务
- 优点: Identity 部分零代码
- 缺点: 两个 ORM 并存,维护负担;事务难跨 ORM

### 方案 B: 自定义 SqlSugarUserStore + SqlSugarRoleStore ✅
- 优点: 单一 ORM;Identity 与业务一致
- 缺点: 需实现 11 个 `IUserStore<>` 接口 + 1 个 `IRoleStore<>`,代码量大

### 方案 C: 不使用 Identity,自己写认证
- 优点: 完全可控
- 缺点: 放弃 [Authorize(Roles=...)] / SignInManager / ClaimsPrincipal 等标准管道

## 决策

选 **方案 B**,自定义 SqlSugar Store。

实现:
- `Services/Identity/SqlSugarUserStore.cs` — 实现 11 个 IUserStore 接口
- `Services/Identity/SqlSugarRoleStore.cs` — 实现 IRoleStore + IRoleClaimStore
- `Program.cs` 注册: `.AddUserStore<SqlSugarUserStore>().AddRoleStore<SqlSugarRoleStore>()`

## 后果

### 正面
- Identity 与业务走同一 ORM,事务/连接管理一致
- 业务字段(DisplayName / Gid / Permission Claim)通过 `DisplayNameClaimsFactory` 一次写入 Cookie
- 实现了完整的 Identity 管道:`UserManager<Users>` / `SignInManager<Users>` / `RoleManager<Roles>` 全部可用

### 负面 / 风险
- 自定义 Store 需自己处理事务、并发、软删等边角(见 SqlSugarUserStore.cs 注释)
- `IQueryableUserStore<Users>.Users` 属性每次访问触发全表 ToList(性能隐患,见注释警告)
- Identity 框架版本升级时,接口签名可能变,需同步更新

### 中和
- 关键路径加 `ILogger` 日志(任何异常)
- `Users.IsDeleted = 0` 过滤软删用户写在所有查询条件里
- `ConcurrencyStamp` 每次更新刷新,防脏写

## 相关

- `Services/Identity/SqlSugarUserStore.cs` (552 行)
- `Services/Identity/SqlSugarRoleStore.cs` (177 行)
- `Services/Identity/DisplayNameClaimsFactory.cs` — 业务 Claim 写入
- `Program.cs` 注册