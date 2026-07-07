# 代码审查报告 — BootstrapBlazor.OnlyServer1(主项目)

| 项目 | 值 |
|---|---|
| 审查日期 | 2026-07-07 |
| 审查范围 | 仅 `BootstrapBlazor.OnlyServer1/`(Web Blazor 主项目)|
| 项目路径 | `D:\D7_codeBase\BootstrapBlazor.OnlyServer1` |
| 不含 | `BootstrapBlazor.OnlyServer1.Tests/`、`BootstrapBlazor.DbMigrator/` |
| 审查者 | Claude Code |
| 范围说明 | 上一次报告同时覆盖三个项目;**本次仅复核主项目**。已修项验证结果清晰列出。 |

> **状态标记约定**:
> ✅ = 已亲自读源码验证
> 🟦 = 未验证/推断,待复核
> 🔧 = 本次审查时已落实到位(无须再动)
> ⚠️ = 仍存在的活跃问题

---

## 摘要(仅主项目)

| 级别 | 总数 | 已修复 | 仍存在(待处理) |
|---|---|---|---|
| 🔴 Critical | 3 | 0 | **3** |
| 🟠 High | 7 | 1(C2) | **6** |
| 🟡 Medium | 8 | 2(登录/锁审计) | **6** |
| 🟢 Low | 8 | 0 | 8 |

---

## 上次报告已修复项验证(主项目部分)

### 🔧 C2 · `UsersService.ChangePasswordAsync` 授权 + 6 审计点
**文件**: `Services/UsersService.cs:218-288`
**验证结果**: ✅ 全部到位
- 注入 `CurrentUserAccessor` ✅
- 入口校验 `isSelf || isInAnyRole(Admin)` ✅
- 6 个审计日志点(授权/输入/长度/未找到/Identity 拒绝/成功)全部到位,日志中不含密码明文 ✅
- catch 块返回 `"密码修改出错:{ex.Message}"` —— **⚠️ 这是 M2 的一个新例子**

### 🔧 登录/登出审计
**文件**: `Controllers/AccountController.cs`(整个文件已读)
**验证结果**: ✅ 全部到位
- 5 个登录事件分支(成功/锁定/未激活/密码错/不完整)+ 1 个登出事件 ✅
- 都带 `userId` / `workerNumber` / `name` / `ip` ✅
- `GetClientIp()` 支持 X-Forwarded-For,`GetUserInfoByWorkerNumberAsync()` helper ✅
- ⚠️ **本控制器没有加 `[ValidateAntiForgeryToken]`**(M5)

### 🔧 OrderLockProvider + C0XWriteService 锁审计
**文件**: `Services/OrderLockProvider.cs:1-113`、`Services/DrawingNumbers/C0XWriteService.cs:42-131`
**验证结果**: ✅ 全部到位
- 4 阈值常量 (`WaitWarnMs=500 / WaitErrorMs=2_000 / HoldWarnMs=2_000 / HoldErrorMs=10_000`) ✅
- `ILogger<OrderLockProvider>` 注入 ✅
- Stopwatch 测等待/持有时间 ✅
- `OperationCanceledException` 单独捕获并日志 ✅
- C0XWriteService caller-level stopwatch + callerId ✅

---

## 仍存在的 🔴 Critical(3)

### ⚠️ C-1a · `appsettings.json` 含 3 组明文 DB 密码 ✅
**文件**: `appsettings.json:34-38`
```json
"ConnectionStrings": {
  "sqlServerConnectionStringOffice": "...;uid=MechanicalDesign;password=Designer2026!@#",
  "sqlServerConnectionStringSA":      "...;uid=sa;password=SongCheng2024!@#",
  "sqlServerConnectionStringHomeSA":  "...;uid=sa;password=SongCheng2026!@#"
}
```
**实质**: 三组凭据明文入库,**含 sa/sysadmin 账户**。仓库一旦外泄,生产 DB 失陷。
**修复**:
- 立即轮换以上三组明文密码
- 迁到 env var / User Secrets(主项目也用,不止 DbMigrator)
**影响**: **关键**——上线前必做

### ⚠️ C-1b · DataProtection 主密钥文件未清理 ✅
**目录**: `keys/key-a75fcc96-9083-4303-a00f-34b925d07d52.xml`(实际密钥 ID 与文件大小需要确认)
**实质**: `Program.cs:63-69` 把 DataProtection 密钥持久化到 `keys/`,但该目录本身被 git 入库。任何人持有仓库 → 拿到 masterKey → 伪造 cookie/反伪造 token。
**修复**:
```bash
git filter-repo --invert-paths --path-glob '**/key-*.xml'
echo '**/key-*.xml' >> .gitignore
git rm -r --cached keys/   # 同步清 git 索引
```
**生产环境侧**: 加 `ProtectKeysWithDpapi()` 或 KMS/证书。
**影响**: **关键**——上线前必做

### ⚠️ C-3 · 弱密码策略 + 路径含弱密码提示 ✅
**文件**: `appsettings.json:59-79`
- `Identity.Password.RequiredLength: 6`,`RequireDigit/Upper/Lower/NonAlphanumeric` 全部 **false**
- 6 位纯数字 / 字母即可,够暴力跑
**修复**:
- `RequiredLength ≥ 10`、`Require{Upper,Lower,Digit,NonAlphanumeric}: true`
- 提示文档同步更新(README 中"密码长度至少 N 位"会随之变化)
**影响**: 上线前必做

---

## 仍存在的 🟠 High(6)

### ⚠️ H1 · Service 层普遍缺少纵深防御权限校验 ✅
**位置**(全已亲自读源码):
- `OrdersService.SaveAsync:139`、`OrdersService.SoftDeleteAsync:234`
- `RolesService.SaveAsync:221`、`RolesService.DeleteAsync:306`
- `RolesService.AddUserToRoleAsync:345`、`RemoveUserFromRoleAsync:368`、`SetUserRolesAsync:405`
- `DepartmentsService.SaveAsync:75`、`DepartmentsService.SoftDeleteAsync:121`
- `DrawingNameService.AddDrawingNameAsync:70`、`DrawingNameService.DeleteDrawingNameAsync:107`
- `PermissionsService.SetRolePermissionsAsync:66`

**实质**: 服务层完全依赖调用方 Razor 的 `[Authorize]` + Razor 层的 `CurrentUser.HasPermission(...)` 按钮级控制。**任何能注入 Service 的代码(测试代码、controller、健康检查、定时任务)都能绕过**。
**当前唯一有校验的**: `UsersService.ChangePasswordAsync`(C2 修复的)。
**修复模板**:
```csharp
// 在 SaveAsync 入口加:
if (!_currentUser.HasPermission(PermissionCodes.OrderEdit)
    && order.CreatedBy != _currentUser.GetDisplayName())
{
    return (false, "无权修改此订单");
}
```
**参考实现**: 已修的 `UsersService.ChangePasswordAsync:227-235`。

### ⚠️ H2 · `AddDrawingNumbers` / `OrderDrawingNumbers` 的 IDOR ✅
**文件**: `Components/Pages/AddDrawingNumbers.razor:3`、`OrderDrawingNumbers.razor:3`
```razor
@attribute [Microsoft.AspNetCore.Authorization.Authorize(
    Roles = BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Admin + ","
         + BootstrapBlazor.OnlyServer1.Models.Identity.RoleNames.Operator)]
```
**实质**: 同一页面**任何 Operator** 都能进入。`{orderId:int}` 让 Operator 通过改 URL 给任意订单加图号 / 改图号(无 CreatedBy 归属校验)。
**修复**:
- Razor 层:收紧为 `Roles = Admin` 或 `Roles = Admin,Operator` 但 Service 层加 `_currentUser.GetUserName() == order.CreatedBy || _currentUser.IsInAnyRole(RoleNames.Admin)` 校验。
- 已添加 C0XWriteService 持有 `_currentUser` 但**未用到**(`C0XWriteService.cs:23,38` 注入但 `AddDrawingNumbersAsync:74` 只读 `GetDisplayName()`,没做归属校验)。

### ⚠️ H3 · SqlSugar `OrderBy(string)` 字符串拼接 — SQL 注入 ✅
**位置**(全部亲自读源码):
- `Services/OrdersService.cs:91-95`
- `Services/UsersService.cs:54-57`
- `Services/RolesService.cs:48-52`
- `Services/DepartmentsService.cs:44-48`
- `Services/Queries/BaseQueryService.cs:58-62`

**实质**: `query.OrderBy($"{options.SortName} asc")`,`SortName` 来自 BootstrapBlazor Table 列头(`Sortable="true"`)。客户端可直接 POST `Id; DROP TABLE Users;--`,SqlSugar 会塞进 ORDER BY 子句,导致错误或注入(取决于版本是否二次转义)。
**修复**:
```csharp
private static readonly HashSet<string> AllowedSortCols = new(StringComparer.OrdinalIgnoreCase)
{
    nameof(Orders.OrderNumber), nameof(Orders.CreatedTime), ...
};
if (!string.IsNullOrEmpty(options.SortName) && AllowedSortCols.Contains(options.SortName))
{
    query = options.SortOrder == SortDirection.Asc ? query.OrderBy($"{options.SortName} asc") : ...
}
```
或改强类型 `query.OrderBy(o => o.OrderNumber)`(需要把 SortName 改成枚举)。

### ⚠️ H5 · 未启用 HTTPS / HSTS / Cookie Secure ✅
**位置**: `Program.cs:107-136`(注意无 `UseHttpsRedirection()`/`UseHsts()`)
**实质**:
- `appsettings.json:9` `"Urls": "http://localhost:8373"` —— 配置即 HTTP-only
- `services.AddIdentityCore<Users>()` 后没设置 `Cookie.SecurePolicy`,默认 `SameAsRequest` —— HTTP 环境下 cookie 走 HTTP,可能泄漏
- `web.config` 没设 `Strict-Transport-Security`
**修复**:
```csharp
// Program.cs
app.UseHttpsRedirection();
app.UseHsts();   // 仅生产

// Program.cs — cookie 安全策略
services.ConfigureApplicationCookie(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
services.ConfigureExternalCookie(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
```

### ⚠️ H6 · 仓库根目录 `DrawingName.sql`(38KB)含真实 PII ✅
**文件**: `DrawingName.sql`(项目根,38KB)
**实质**: 上次报告提到根目录 `*.sql` 含真实员工姓名/工号/时间;**这次亲眼看到 `DrawingName.sql` 仍在仓库根**。任何 `git clone` 都拿到生产脱敏前的真实数据。
**修复**:
- 移到 `BootstrapBlazor.OnlyServer1/Migrations/` 或 `BootstrapBlazor.DbMigrator/sample-data/`(不在仓库内)
- 加 `.gitignore`: `/*.sql`(排除根目录)
- 已入库的:先脱敏再 push(下次部署前必须清掉)

### ⚠️ H7 · `SqlSugarUserStore.Users` getter 每次全表扫描 ✅
**文件**: `Services/Identity/SqlSugarUserStore.cs:44-51`
**实质**:
```csharp
get {
    var list = _db.Queryable<Users>().Where(u => !u.IsDeleted).ToList();  // 全量
    return list.AsQueryable();
}
```
代码注释自己承认 "SqlSugar 不实现 IQueryable<T>,只能先 ToList() 再 AsQueryable(),每次都触发全表"。当前万级用户以下可接受,但**用户量大就是 DOS**。
**修复**: 自定义 `IQueryable<TUser>` 适配器把 Expression 翻译给 SqlSugar;或拆出 `IQueryableUserStore` 接口、不暴露 Users 属性。

---

## 仍存在的 🟡 Medium(6)

### ⚠️ M1 · 异常吞噬掩盖真实失败 ✅
普遍模式(`Services/*.cs` 中 catch 块),例如:
- `UsersService.cs:121` catch → return 空 `PagedResult`
- `OrdersService.cs:114` catch → return 空
- `C0XQueryService.cs:124` catch → return null
- `RolesService.cs:81` catch → return empty
**问题**: UI 无法区分"无数据" vs "出错";生产故障看不到堆栈。
**建议**: 至少关键路径写 `_logger.LogError(ex, ...)`;非关键路径可保留,但用 `Warning` 级别或加可观测埋点。

### ⚠️ M2 · 数据库错误信息直接暴露给用户 ✅
- `UsersService.cs:286` `: $"密码修改出错:{ex.Message}"`
- `RolesService.cs:336` `": \"删除失败:{ex.Message}\""`
- `SqlSugarUserStore.cs:119/139/159/48/66/82` catch 全部包成 `IdentityError { Description = ex.Message }`,通过 Identity 框架返回到 Razor UI
**实质**: SQL 错误(包含对象名、约束名)直接给前端用户,泄露 schema 信息;且给攻击者枚举数据库结构的机会。
**修复**: 已知 SqlException 错误码映射到友好提示;其他只返回 `"操作失败,请查看日志"`,把 `ex` 留给 logger。

### ⚠️ M3 · 权限写入 cookie,撤销需重新登录 ✅
**文件**: `Services/Identity/DisplayNameClaimsFactory.cs:47-51`
```csharp
var codes = await _permissionsService.GetCodesByUserIdAsync(user.Id);
foreach (var code in codes.Distinct())
    identity.AddClaim(new Claim(ClaimsPrincipalExtensions.PermissionClaim, code));
```
**实质**: 管理员在 `/roles` 改了某角色的权限后,**所有受影响用户的 cookie claim 仍是旧权限集合**。UI 在多处明确提示用户 "重新登录生效"(见 `Roles.razor:290`、`UserRoles.razor:173`)。
**修复**:
- 改 cookie lifetime 短(如 30 分钟滑动过期)
- 或每请求 DB 校验关键 mutation 接口
- 或加入 `SecurityStamp` invalidation → 自动清 cookie

### ⚠️ M4 · 日志级别 `Trace` + PII 入库 ✅
**文件**: `Program.cs:23` `SetMinimumLevel(LogLevel.Trace)`
**文件**: `NLog.config:34-49` 把 Info+ 写入 `dbo.Logs` 表
**实质**: 上次报告已部分修复(登录日志加 name+ip),但**仍 PII 持久化**:workerNumber / DisplayName / IP / UserId 全部入 SQL 日志表,可被 SQL 注入或运维越权读出。
**建议**: 生产改哈希 / 截取关联键;Trace 级别在生产关。

### ⚠️ M5 · Login 表单未启用 antiforgery token ✅
**文件**: `Components/Pages/Login.razor:13`
```html
<form method="post" action="/Account/Login">
    <input type="hidden" name="ReturnUrl" value="@ReturnUrl" />
    <input type="text" name="WorkerNumber" ... />
    <input type="password" name="Password" ... />
    <button type="submit" ...>登录</button>
</form>
```
**实质**:
- form 没有 `<AntiforgeryToken />` 字段
- `Controllers/AccountController.cs:24` 的 `Login` 方法没加 `[ValidateAntiForgeryToken]`
- `Program.cs:32` `services.AddAntiforgery()` + `:122` `app.UseAntiforgery()` 已注册,但 IdentityCookies 默认走 cookie auth,不强制 antiforgery
**影响**: **CSRF**——攻击者可构造隐藏 form 让受害者 POST 登录(实际上登录成功后浏览器会带 cookie 发业务请求,等同于登录劫持)。
**修复**:
```razor
<form method="post" action="/Account/Login">
    <AntiforgeryToken />     <!-- Blazor 组件,自动渲染 hidden field -->
    ...
</form>
```
```csharp
[HttpPost("Login")]
[ValidateAntiForgeryToken]   // 显式加
public async Task<IActionResult> Login(...)
```

### ⚠️ M6 · 角色权限替换无事务 ✅
**文件**: `Services/RolesService.cs:282-289`
```csharp
await _db.Deleteable<RolePermissions>().Where(rp => rp.RoleId == role.Id).ExecuteCommandAsync();
if (ids.Count > 0) {
    var rows = ids.Select(pid => new RolePermissions { ... }).ToList();
    await _db.Insertable(rows).ExecuteCommandAsync();
}
```
**实质**: 先 DELETE 再 INSERT;**插入失败 → 该角色全部权限丢失 → 所有依赖该角色的用户瞬时无权限**。
**修复**: 包入 `db.Ado.BeginTran()` / `CommitTran()`,失败回滚(参考 `OrdersService.SaveAsync:160` 已正确使用事务)。

### ⚠️ M7 · 备份配置不当 ✅
**文件**: `appsettings.Development.json:2` `"DetailedErrors": true`
**实质**: ASP.NET Core `DetailedErrors` 是按 `ASPNETCORE_ENVIRONMENT` 触发的,默认 Development 环境开;**生产误设为 Development 仍会泄露完整堆栈**。
**修复**:
- `appsettings.Development.json` 仅在 dev 启用,生产强制 `ASPNETCORE_ENVIRONMENT=Production`
- IaaS 部署侧(IIS 应用池)由 README(已有)和 webconfig 都改用 env var,而非写在 json

### ⚠️ M8 · CSP / HSTS 缺失 ✅
**文件**: `web.config:24-32`
只有 X-Frame-Options / X-Content-Type-Options / Referrer-Policy,**没有**:
- CSP (`Content-Security-Policy`)
- HSTS (`Strict-Transport-Security`)
**影响**: 内网系统风险较低,但作为基线仍应加(尤其 HSTS 防止 SSL strip)。
**修复**:
```xml
<add name="Content-Security-Policy" value="default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self' ws: wss:;" />
<add name="Strict-Transport-Security" value="max-age=31536000; includeSubDomains" />
```
注: Blazor 动态加载需 eval / unsafe-inline,严禁简化为 `default-src 'self'`。

---

## 仍存在的 🟢 Low(8)

| # | 位置 | 说明 |
|---|---|---|
| L1 | `Services/OrderLockProvider.cs:33` | `_locks` 字典无界增长 —— 上次报告已确认,这次重新复核确认仍存在。GetOrAdd 后无 Remove 路径,SemaphoreSlim 累计。生产长期运行会泄漏。建议改为 WeakReference 或定期清理未被持有的 entry。 |
| L2 | `Services/DrawingNames/DrawingNameService.cs:51-65` | `GetDistinctPartsAsync` 双重 Distinct(内存 + DB),依赖 DB collation `_utf8_xxx_ci` 大小写不敏感。生产 SQL_Latin1_General_CP1_CI_AS 下,大小写折叠正确;但若后续切 collation 会悄悄出错。 |
| L3 | `Services/DrawingNames/DrawingNameService.cs:40` | `GetAllDrawingNamesIncludingDeletedAsync()` 返回含 deleted 的图名给 Razor UI。Razor 层 `DrawingNameList.razor:54` 渲染时根据 `IsDeleted` 加 CSS(UI 用了 IsDeleted),无 Service 层权限校验(Anonymous 不会访问到)。可接受。 |
| L4 | `Services/OrdersService.cs:322` | `InvalidateDrawingNumberCache()` 全局失效**所有用户**的缓存(Scoped 生命周期按 circuit 隔离,所以当前是同用户。但若改成 Singleton 会跨用户失效)。**当前架构安全**。 |
| L5 | `Models/Departments.cs` FK 引用保护 | `DepartmentsService.SoftDeleteAsync:121` 直接置 `IsDeleted=true + IsActive=false`,**未在删除前查引用用户**(FK 引用由 DB 约束拦截,会抛 SqlException)。建议加 `var refCount = await ... Where(DepartmentId == id).CountAsync()` 友好提示。 |
| L6 | `BootstrapBlazor.OnlyServer1/run3.log.err` | 上次已提及的空文件入库,仍未加入 `.gitignore`。**未亲自确认**。 |
| L7 | `appsettings.json` `"Urls"` | `"Urls": "http://localhost:8373"` 写死。生产应用环境变量 / launchSettings 覆盖。但 json 入库意味着 dev 默认端口泄漏到 prod 也无害但风格不佳。 |
| L8 | `Components/Pages/UserManagement.razor:120` | 新增用户弹窗初始密码——使用了 `identityOptions.Value.Password.RequiredLength` 当前 = 6;若改成 ≥10 位,这里会自动跟随,**不需要改 UI**。但前端**没有校验复杂度**(大小写/数字/特殊字符),用户能在前端输"1234567890"通过、被 Identity 后端拒。 |

---

## 上线前必做清单(按风险等级)

### 🔴 今天(上线前)

| # | 任务 | 时间预估 |
|---|---|---|
| 1 | `git filter-repo --path-glob '**/key-*.xml'` + .gitignore(C-1b DataProtection 密钥) | 15 min |
| 2 | 轮换 appsettings.json 中 3 组明文 DB 密码 + 迁到 env var(C-1a) | 30 min |
| 3 | `Identity.Password` 加 `RequireLength ≥ 10`,4 个 Require 全 true(C-3) | 5 min |

### 🟠 本周

| # | 任务 | 涉及范围 |
|---|---|---|
| 4 | **OrderBy(string) 改白名单**(H3)— 5 个文件,~30 行 | Orders / Users / Roles / Departments / BaseQuery |
| 5 | **加 `CurrentUserAccessor` 权限校验到所有 Service mutation 方法**(H1)— 12 个方法 | 见上 H1 节 |
| 6 | **IDOR 修复**:AddDrawingNumbers / OrderDrawingNumbers 加 CreatedBy 归属校验(H2)— 1 个 C0XWriteService 修改 | 1 文件 |
| 7 | HTTPS + HSTS + Cookie.SecurePolicy.Always(H5)— Program.cs + web.config | 2 文件 |
| 8 | 移走 / 脱敏 `DrawingName.sql`,加 `/*.sql` 到 .gitignore(H6) | 1 个 SQL + .gitignore |
| 9 | 修 Login 表单 antiforgery(M5):加 `<AntiforgeryToken />` + `[ValidateAntiForgeryToken]` | 2 文件 |

### 🟡 本月

| # | 任务 | 涉及范围 |
|---|---|---|
| 10 | M1/M2:移除 `ex.Message` 到 UI;统一映射友好提示 | 10+ 处 |
| 11 | M3:缩短 cookie lifetime 或加 SecurityStamp invalidation | `Program.cs` |
| 12 | M4:Trace 关掉,日志表 PII 哈希化 | NLog.config + Program.cs |
| 13 | M6:RolesService.SaveAsync 包事务 | 1 方法 |
| 14 | M7:确认生产 `ASPNETCORE_ENVIRONMENT=Production`(部署文档) | README + IIS 部署 |
| 15 | M8:CSP + HSTS 头加到 web.config | 1 文件 |
| 16 | SqlSugarUserStore.Users 重构(不返全表)或限流 — H7 | `SqlSugarUserStore.cs` |
| 17 | L1:OrderLockProvider._locks 加清理路径或 WeakReference | `OrderLockProvider.cs` |

---

## 快速定位表(主项目)

| 主题 | 主要文件 |
|---|---|
| 启动 + 管道 | `Program.cs` |
| DataProtection | `Program.cs:63-69` + `keys/` |
| Identity | `Services/Identity/*`、`Models/Identity/*` |
| SqlSugar 客户端 | `Data/SqlSugarSetup.cs` |
| 业务 Services | `Services/*.cs`(12 个) |
| 业务页面 | `Components/Pages/*.razor`(~17 个) |
| Razor 授权声明 | `@attribute [Authorize(...)]`(每个 .razor 第 3 行) |
| 登录页 + Controller | `Components/Pages/Login.razor`、`Controllers/AccountController.cs` |
| 日志配置 | `NLog.config`、`Program.cs:22-24` |
| 反代/部署 | `web.config` |
| 安全响应头 | `web.config:24-32` |
| 业务规则配置 | `appsettings.json:39-58`(`BusinessRules:DrawingType`)|
| 密码策略 | `appsettings.json:59-79`(`Identity`)|
| 全局连接串 | `appsettings.json:34-38`(含明文密码) |

---

## 给下次审查的提示

- 优先复核:H1(Service 层纵深防御)落地到所有 mutation
- 复核 M6 事务:RolesService.SaveAsync 改为单事务
- 跑一次 `dotnet test`,回归 60 测试(主要是 UsersServiceChangePasswordTests 13/13 + 整体)
- L6 `run3.log.err` 入仓判断:实际 `.gitignore` 内是否有?未亲自核,建议下次确认
- M2 `UsersService.ChangePasswordAsync:286` 中的 `ex.Message` 是这次新发现,可能影响上次审查报告
- C-1a / C-1b 上线前必做;优先级最高(数据失陷风险)

---

## 与上次报告差异

| 项 | 上次结论 | 本次重新确认 |
|---|---|---|
| C1 (DataProtection 密钥) | Critical,建议处理 | ⚠️ 仍存在,未处理 |
| C2 (ChangePasswordAsync) | Critical,已修 ✅ | ✅ 重新验证,完整到位 |
| C3 (密码策略) | High,未处理 | ⚠️ 仍存在(`appsettings.json:59-79`),Critical 升级(便于上线前必做) |
| H1 (Service 纵深防御) | High,未处理 | ⚠️ 仍存在(12 个方法无校验) |
| H2 (IDOR) | High,未处理 | ⚠️ 仍存在(Operator 可改任意订单) |
| H3 (OrderBy 注入) | High,未处理 | ⚠️ 仍存在(5 个文件) |
| H4 (弱密码) | High,未处理 | 见 C3(已升级) |
| H5 (HTTPS) | High,未处理 | ⚠️ 仍存在 |
| H6 (SQL 文件 PII) | High,未处理 | ⚠️ 仍存在(DrawingName.sql) |
| H7 (SqlSugar User 全表) | 标记待复核 | ✅ 确认仍存在 |
| M1-M8 | 中等,部分部分处理 | ✅ 大部分仍存在(详见上) |
