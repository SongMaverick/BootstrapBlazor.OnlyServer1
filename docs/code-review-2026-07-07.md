# 代码审查报告 — BootstrapBlazor.OnlyServer1

| 项目 | 值 |
|---|---|
| 审查日期 | 2026-07-07 |
| 项目根 | `D:\D7_codeBase\BootstrapBlazor.OnlyServer1` |
| 范围 | `BootstrapBlazor.OnlyServer1/`、`BootstrapBlazor.DbMigrator/`、`BootstrapBlazor.OnlyServer1.Tests/` |
| 审查者 | Claude Code |
| 引发的修复 | C2 + 登录/登出/锁审计 |

> **状态标记约定**:每条发现标注
> ✅ = 本报告中已亲自读源码验证
> 🟦 = 由辅助 agent 报告、待复核
> 🔧 = 已在 2026-07-07 的修复中处理

---

## 摘要

| 级别 | 总数 | 已验证 | 已修复 |
|---|---|---|---|
| 🔴 Critical | 3 | 3 | 1 (C2) |
| 🟠 High | 7 | 6 | 0 |
| 🟡 Medium | 8 | 2 | 0 |
| 🟢 Low | 9 | 1 | 0 |

仅已验证的级别为最高优先级。其余 🟦 待复核条目按"看着办"对待 — 多数是代码风格/防御性改进,不影响上线决策。

---

## 🔴 Critical

### C1 · DataProtection 主密钥提交到代码仓库 ✅

- **文件**: `BootstrapBlazor.OnlyServer1/keys/key-a75fcc96-9083-4303-a00f-34b925d07d51.xml`
- **实质**: `masterKey` 是 base64 明文 `<value>Wg3VmmQ4...`,可用于伪造所有 auth cookie / antiforgery token
- **修复**:
  ```bash
  git filter-repo --invert-paths --path-glob '**/key-*.xml'
  echo '**/key-*.xml' >> .gitignore
  ```
  生产环境: `AddDataProtection().ProtectKeysWithDpapi()` (或 Azure Key Vault / 证书)
- **影响**: 立即 — 持有 repo 的人能登录任意账户

### C2 · `UsersService.ChangePasswordAsync` 越权 ✅ 🔧

- **文件**: `Services/UsersService.cs:217` (修复前)
- **实质**: 仅接收 `userId + oldPassword`,完全未校验调用者 == userId 或 Admin;配合默认 `123456` 弱密码即可提权
- **修复内容** (已实施):
  - 注入 `CurrentUserAccessor`
  - 方法入口先校验 `isSelf || isInRole(Admin)`,否则返回 `"无权修改此用户的密码"` + 越权日志
  - 新增 6 个审计日志点(授权/输入校验/用户不存在/Identity 拒绝/成功),均不打印密码明文
- **测试**: `BootstrapBlazor.OnlyServer1.Tests/Services/UsersServiceChangePasswordTests.cs` — 13/13 通过(含 3 个新增授权场景)

### C3 · DbMigrator 也有明文密码 + 硬编码路径 ✅

- **文件**: `BootstrapBlazor.DbMigrator/appsettings.json:4-5`
- **实质**: 又一份明文 `Designer2026!@#` / `SongCheng2026!@#`;且 `Migration.SourceDirectory = "D:\D7_codeBase\BootstrapBlazor.OnlyServer1"`(本机绝对路径,CI 必败)
- **修复**: 迁到 env var / User Secrets;**轮换以上三组明文密码**;SourceDirectory 默认改为 `./sample-data`,`--source` CLI 强制覆盖

---

## 🟠 High

### H1 · Service 层普遍缺失"纵深防御"权限校验 ✅

- **位置**: `OrdersService.SaveAsync`、`RolesService.SaveAsync/DeleteAsync`、`DrawingNameService.DeleteDrawingNameAsync`、`UsersService.ChangePasswordAsync`(已修)
- **实质**: Service 仅依赖调用方 Razor 的 `[Authorize]`;任何能注入 Service 的代码都绕过
- **模板**: 注入 `CurrentUserAccessor`,在方法开头 `HasPermission("...")`,否则抛 `UnauthorizedAccessException`
- **参考实现**: 已修的 `UsersService.ChangePasswordAsync`

### H2 · `AddDrawingNumbers` / `OrderDrawingNumbers` 的 IDOR ✅

- **文件**: `Components/Pages/AddDrawingNumbers.razor:3`、`OrderDrawingNumbers.razor:3`
- **实质**: `[Authorize(Roles=Admin,Operator)]` 是唯一门控;`{orderId:int}` 让任何 Operator 通过改 URL 改动/查看任意订单
- **修复**: Razor 层加 `[Authorize(Roles=Admin)]`,Service 层加 `Order.Edit` 权限 + 部门/创建人归属校验

### H3 · SqlSugar `OrderBy(string)` 字符串拼接 — SQL 注入风险 ✅

- **文件**: `Services/OrdersService.cs:91-95`、`UsersService.cs:48-51`、`RolesService.cs:48-52`、`DepartmentsService.cs:44-48`、`Queries/BaseQueryService.cs:60-66`
- **实质**: `query.OrderBy($"{options.SortName} asc")`,`SortName` 来自表格列头点击(客户端可传);可注入 `Id; DROP TABLE Users;--`
- **修复**: 白名单 `new[]{"Id","OrderNumber"}.Contains(options.SortName)`,或改强类型 `query.OrderBy(x => x.OrderNumber)`

### H4 · 弱密码策略 + 默认账号 ✅

- **位置**: `appsettings.json:61-67` (`RequiredLength:6`, 复杂度全 false) + `BootstrapBlazor.DbMigrator/Program.cs:176` (硬编码 `Admin@123`) + `:1008` (用户密码 `123456`)
- **实质**: `123456` 即过;默认账号一猜即中
- **修复**:
  - `RequiredLength ≥ 10`,`Require{Upper,Lower,Digit,NonAlphanumeric}: true`
  - 生产 `--admin-password` 必填;User 迁移每用户随机一次性密码

### H5 · 未启用 HTTPS / HSTS / Cookie SecurePolicy ✅

- **位置**: `Program.cs`、`appsettings.json:9`(`"Urls": "http://localhost:8373"`)
- **实质**: HTTP-only;`AddIdentityCookies` 用默认 `CookieSecurePolicy.SameAsRequest`
- **修复**:
  ```csharp
  app.UseHttpsRedirection();
  app.UseHsts();   // 生产
  services.ConfigureApplicationCookie(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
  services.ConfigureExternalCookie(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
  ```

### H6 · 仓库根目录 SQL 文件含真实生产 PII ✅

- **文件**: `Order.sql`、`Users.sql`、`C04.sql ~ C08.sql`、`DrawingNumber.sql`、`DrawingNames.sql`(根目录)
- **实质**: 例如 `Order.sql` 含 `(1, 123456, 220, 123, '宋程', '2024-06-03 ...')` — 真实员工姓名/工号/时间
- **修复**: 移到 `BootstrapBlazor.DbMigrator/sample-data/`(.gitignore 排除根目录 `*.sql`);或写入前脚本脱敏

### H7 · `SqlSugarUserStore.Users` getter 每次全表扫描 🟦

- **文件**: `Services/Identity/SqlSugarUserStore.cs:44-51`
- **实质**: `Queryable<Users>().ToList()` 每次调用都扫表
- **修复**: 改造为查询翻译器,或基于 store 生命周期缓存;真数据库压测后再决定

---

## 🟡 Medium

### M1 · 异常吞噬掩盖真实失败 ✅

所有 `Services/*.cs` 的 catch 块均返回空集合 / `false`,UI 无法区分"无数据" vs "出错"
**建议**: 关键路径 throw,全局异常中间件统一处理

### M2 · 数据库错误信息直接暴露给用户 ✅

`SqlSugarUserStore.cs` / `RolesService.cs:336` 把 `ex.Message` 写入 UI
**建议**: 已知 SqlException 错误码映射;其它返回 "操作失败,请查看日志"

### M3 · 权限写入 cookie,撤销需重新登录 🟦

`DisplayNameClaimsFactory.cs:47-52` 把 Permission 嵌进 cookie claim;业务页面 (`Roles.razor:290`、`UserRoles.razor:173`) 自己提示 "重新登录生效"
**建议**: 每次请求查 DB 的 AuthorizationHandler;或缩短 cookie 寿命

### M4 · 日志级别 `Trace` + PII 入库 🟦

`Program.cs:23` 设 `SetMinimumLevel(LogLevel.Trace)` + `_logger.LogInformation("登录成功: WorkerNumber=…")` 永久入 SQL `Logs` 表
**已部分修复** (2026-07-07): 登录日志改为 `workerNumber + name + ip`,可读性更高但仍 PII
**仍待**: 生产建议改哈希 / 截取关联键

### M5 · 登录表单未启用 antiforgery token 🟦

`AccountController.Login` 没有 `[ValidateAntiForgeryToken]`,`UseAntiforgery()` 注册了却没生效
**建议**: 加 `[ValidateAntiForgeryToken]`,Blazor form 加 `<AntiforgeryToken />`

### M6 · Role 删除与权限替换无事务 🟦

`RolesService.SaveAsync:282-289` 先 DELETE 再 INSERT;插入失败 → 权限永久丢失
`RolesService.DeleteAsync` 部分失败不回滚已删数据
**建议**: 包入 `db.Ado.BeginTran()` / `CommitTran()`

### M7 · 备份配置不当 🟦

`appsettings.Development.json` 启 `DetailedErrors: true`;`DbMigrator/Program.cs:443-453` 含旧 schema 的硬编码 DROP
**建议**: 生产强制 env=Production;旧 schema 名迁出 docs

### M8 · CSP / Strict-Transport-Security 缺失 🟦

`web.config:25-32` 只配了 X-Frame / X-Content / Referrer
**建议**: 加 CSP(`script-src 'self' 'nonce-...'`)与 HSTS

---

## 🟢 Low

按优先级不到的话可推迟:

- DbMigrator 多处 `$\"... '{table}'\"` 拼接 SQL 标识符 (C2.1–2.5, 🟦)
- `BootstrapBlazor.OnlyServer1/run3.log.err` 空文件入仓 (.gitignore + 删, 🟦)
- `OrdersService.InvalidateDrawingNumberCache()` 全局失效所有用户缓存 (🟦)
- `OrderLockProvider._locks` 字典无界增长 (✅;现已有时间审计)
- `DrawingNameService.GetDistinctPartsAsync` 双重 Distinct 依赖 DB collation (🟦)
- 不再使用的方法 (H1 修复后会剔除若干)

---

## 已修复项详情(2026-07-07)

### ✅ C2 · `UsersService.ChangePasswordAsync` 授权 + 审计

| 文件 | 改动 |
|---|---|
| `Services/UsersService.cs` | 注入 `CurrentUserAccessor`,`ChangePasswordAsync` 入口先校验 |
| `Tests/Services/UsersServiceChangePasswordTests.cs` | 新增 3 授权测试 + 调整 helper 支持注入 stub accessor |

**6 个新增审计日志点**:
1. 越权拒绝(Warning)
2. 输入为空(Warning)
3. 新密码过短(Warning,含长度值)
4. 目标用户不存在(Warning)
5. Identity 拒绝(Warning,含 `isPasswordIssue` 撞库信号)
6. 成功(Information,含 `isSelf` / `isAdminReset`)

**测试**: 13/13 passed(`filter "FullyQualifiedName~UsersServiceChangePasswordTests"`)

### ✅ 登录/登出审计增强

| 文件 | 改动 |
|---|---|
| `Controllers/AccountController.cs` | 5 个登录事件 + 1 个登出事件加 `name` / `ip` / `userId`;split `IsNotAllowed` 分支;新增 `GetClientIp()` (支持 X-Forwarded-For)、`GetUserInfoByWorkerNumberAsync()` helper |

**新日志格式举例**:
```
登录成功: userId=5 workerNumber=123456 name=张三 ip=192.168.1.10 persistent=True
登录失败(密码错): userId=5 workerNumber=123456 name=张三 ip=10.0.0.5 requiresTwoFactor=False
用户登出: userId=5 workerNumber=123456 name=张三 ip=192.168.1.10
```

### ✅ OrderLockProvider 锁审计

| 文件 | 改动 |
|---|---|
| `Services/OrderLockProvider.cs` | 注入 `ILogger<OrderLockProvider>`;Stopwatch 测等待/持有时间;4 阈值常量(`WaitWarnMs=500, WaitErrorMs=2_000, HoldWarnMs=2_000, HoldErrorMs=10_000`);取消异常单独捕获并日志 |
| `Services/DrawingNumbers/C0XWriteService.cs` | 调用方层 stopwatch + callerId;空输入跳过分支 |

---

## 修复顺序建议

| 顺序 | 任务 | 截止 |
|---|---|---|
| 今天 | C1 `git filter-repo` 删 keys,轮换 SQL 密码 | 上线前 |
| 今天 | C3 DbMigrator 密码外移 | 同上 |
| 本周 | H1 Service 层统一加 `CurrentUserAccessor` 权限校验 | 上线前 |
| 本周 | H2 IDOR 修复(给 `AddDrawingNumbers` 加 `[Authorize(Roles=Admin)]` + 部门过滤) | 同上 |
| 本周 | H3 `OrderBy(string)` 改白名单(小幅 PR,5 个文件) | 同上 |
| 本月 | H4 密码策略强化 | — |
| 本月 | H5 HSTS + HTTPS + Cookie Secure | — |
| 本月 | H6 SQL 文件脱敏/移出 | — |
| 紧后 | M1–M8 视时间排 | 下迭代 |

---

## 快速定位表

| 主题 | 主要文件 |
|---|---|
| Identity 配置 | `Models/Identity/PermissionCodes.cs`、`Models/Identity/RoleNames.cs` |
| SqlSugar 注入点 | `Data/SqlSugarSetup.cs` |
| 全局 DI / 中间件 | `Program.cs` |
| Razor 授权声明 | `Components/Pages/*.razor` 第 3 行(`@attribute [Authorize(...)]`) |
| 业务权限校验 | `Services/**.cs` — 缺位集中 |
| 日志输出配置 | `NLog.config`(未在本次审查范围) |
| 数据库迁移 | `BootstrapBlazor.DbMigrator/Program.cs` |

---

## 给下次审查的提示

- 本报告的 🟦 项需先复核;`M4` 已部分修复但 PII 持久化问题仍在
- 建议下次优先复核 H1(Service 层授权覆盖到所有 mutation 路径)
- 跑一次 `dotnet test` 全量回归(当前 60/60 通过)
- C1 修复后立即重发内部部署指南,通知开发者清本机 `keys/` 目录
