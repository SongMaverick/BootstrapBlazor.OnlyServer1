# 代码审查报告 — BootstrapBlazor.OnlyServer1(主项目)

| 项目 | 值 |
|---|---|
| 审查日期 | 2026-07-08 |
| 审查范围 | 仅 `BootstrapBlazor.OnlyServer1/`(Web Blazor 主项目)|
| 项目路径 | `D:\D7_codeBase\BootstrapBlazor.OnlyServer1` |
| 不含 | `BootstrapBlazor.OnlyServer1.Tests/`、`BootstrapBlazor.DbMigrator/` |
| 审查者 | Claude Code |
| 上次审查 | `docs/code-review-onlyserver1-2026-07-07.md` |

> **状态标记**:
> ✅ = 已亲自读源码验证
> 🔧 = 上次报告后已修复
> ⚠️ = 仍存在
> 🆕 = 这次新发现

---

## 摘要

| 级别 | 上次总数 | 上次仍存 | 本次仍存 | 本次新增 | 本次已修 |
|---|---|---|---|---|---|
| 🔴 Critical | 3 | 3 | **3** | 0 | 0 |
| 🟠 High | 7 | 6 | **5** | 🆕 1 | 1(M5 完整修复) |
| 🟡 Medium | 8 | 6 | **6** | 🆕 1 | 0 |
| 🟢 Low | 8 | 8 | **8** | 🆕 1 | 0 |

> ⚠️ **本次关键变化**:
> - ✅ M5(CSRF/Antiforgery)完整修复
> - 🆕 **重大发现:H1 部分回归** — `UsersService.SaveAsync` 和 `SoftDeleteAsync` 没有加 `RequireRole` 校验,上次修的 12 处遗漏了 2 处
> - 🆕 **逻辑 Bug**:`Roles.razor` 编辑角色时把所有权限清空
> - 🆕 **未察觉的 SQL 注入风险弱化**:`BaseQueryService.DefaultSortField` 仍是字符串拼接

---

## 上次报告已修复项验证(本次变更仅文档)

### 🔧 M5 · Login 表单 antiforgery 完整修复 ✅
**文件**: `Components/Pages/Login.razor:14`、`Controllers/AccountController.cs:24`
```razor
<form method="post" action="/Account/Login">
    <AntiforgeryToken />            <!-- ✅ 已加 -->
    <input type="hidden" name="ReturnUrl" value="@ReturnUrl" />
    ...
```
```csharp
[HttpPost("Login")]
[ValidateAntiForgeryToken]         // ✅ 已加
public async Task<IActionResult> Login(...)
```
**结论**: ✅ CSRF 防线已闭合。`M5` 从清单中删除。

### 🔧 M4 · Trace 级别默认关 ✅
**文件**: `Program.cs:25`
```csharp
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
```
**结论**: ✅ 生产默认 Information(原 Trace)。仍存在的问题是 NLog 写入 `dbo.Logs` 表的仍是 Info+ PII(workerNumber/IP/userId)。

---

## 🔴 Critical(3,全部仍存在)

### ⚠️ C-1a · `appsettings.json` 含明文 DB 密码(含 sa 账户)
**文件**: `appsettings.json:34-38`(本次亲自复核)
```json
"ConnectionStrings": {
  "sqlServerConnectionStringOffice": "...;uid=MechanicalDesign;password=Designer2026!@#",
  "sqlServerConnectionStringSA":      "...;uid=sa;password=SongCheng2024!@#",
  "sqlServerConnectionStringHomeSA":  "...;uid=sa;password=SongCheng2026!@#"
}
```
**影响**: 上线前必做。

### ⚠️ C-1b · `keys/key-*.xml` 仍在仓库
**目录**: `keys/key-a75fcc96-9083-4303-a00f-34b925d07d51.xml`(本次亲自复核存在)
**修复**: `git filter-repo --invert-paths --path-glob '**/key-*.xml'` + `.gitignore`

### ⚠️ C-3 · 弱密码策略(`RequiredLength=6`)
**文件**: `appsettings.json:61-67`
```json
"Password": {
  "RequiredLength": 6,
  "RequireDigit": false,
  "RequireUppercase": false,
  "RequireLowercase": false,
  "RequireNonAlphanumeric": false
}
```

---

## 🟠 High(本次剩 5 项,新增 1 项,已修 1 项)

### ⚠️ H1(部分修复,**仍存在 2 处**)· Service 层 `RequireRole` 校验遗漏 `UsersService`
**上次报告列出的 12 个方法,本次亲自全部复核**:

| # | 位置 | 已加 RequireRole? | 状态 |
|---|---|---|---|
| 1 | `OrdersService.SaveAsync:158-264` | ✅(Update=Admin 或 CreatedBy;Add=Admin/Operator) | 🔧 已修 |
| 2 | `OrdersService.SoftDeleteAsync:285` | ✅(Admin) | 🔧 已修 |
| 3 | `RolesService.SaveAsync:234` | ✅(Admin) | 🔧 已修 |
| 4 | `RolesService.DeleteAsync:352` | ✅(Admin) | 🔧 已修 |
| 5 | `RolesService.AddUserToRoleAsync:405` | ✅(Admin) | 🔧 已修 |
| 6 | `RolesService.RemoveUserFromRoleAsync:442` | ✅(Admin) | 🔧 已修 |
| 7 | `RolesService.SetUserRolesAsync:493` | ✅(Admin) | 🔧 已修 |
| 8 | `DepartmentsService.SaveAsync:89` | ✅(Admin) | 🔧 已修 |
| 9 | `DepartmentsService.SoftDeleteAsync:149` | ✅(Admin) | 🔧 已修 |
| 10 | `DrawingNameService.AddDrawingNameAsync:70` | ✅(Admin/Operator) | 🔧 已修 |
| 11 | `DrawingNameService.DeleteDrawingNameAsync:122` | ✅(Admin/Operator) | 🔧 已修 |
| 12 | `PermissionsService.SetRolePermissionsAsync:72` | ✅(Admin) | 🔧 已修 |
| 13 | **`UsersService.SaveAsync:155`** | ❌ **完全没有** | 🆕 **遗漏** |
| 14 | **`UsersService.SoftDeleteAsync:206`** | ❌ **完全没有** | 🆕 **遗漏** |
| 15 | `C0XWriteService.AddDrawingNumbersAsync:42` | ✅(Admin/Operator) | 🔧 已修 |
| 16 | `C0XWriteService.UpdateCX0DrawingNumberByIdAsync:151` | ✅(Admin/Operator) | 🔧 已修 |
| 17 | `DrawingNumberAllocator.AllocateAsync:40` | ✅(Admin/Operator) | 🔧 已修 |

**🆕 NEW 关键回归 — `UsersService.SaveAsync`**:
```csharp
// Services/UsersService.cs:155-204
public async Task<(bool ok, string? error)> SaveAsync(Users user, string? rawPassword, ItemChangedType changedType)
{
    // ⚠️ 没有 _currentUser.RequireRole(...) 校验!
    if (string.IsNullOrEmpty(user.GID)) { return (false, "GID不能为空"); }
    // 直接进 _userManager.CreateAsync / UpdateAsync
    ...
}
```
- 上次报告 9 项必做清单中第 5 项 "Service 层纵深防御到所有 mutation" 仍然未完成
- **任何人能注入 `UsersService`(单元测试 / 未来 controller / 健康检查 / 定时任务)都能绕过 Razor 层 `[Authorize(Roles = Admin)]`**
- 包括创建 admin 用户(虽然 `_userManager.CreateAsync` 后 UserManager 不会自动加角色,但可以新用户登入后由管理员加 admin)
- 用户更新也无校验 — 攻击者把任一用户的 `Name` 改成自己的姓名冒充审计行
- 软删任意用户无校验

**修复模板**:
```csharp
public async Task<(bool ok, string? error)> SaveAsync(Users user, ...)
{
    var (ok, err) = _currentUser.RequireRole(RoleNames.Admin);
    if (!ok) {
        _logger.LogWarning("UsersService.SaveAsync denied: callerId={Id} reason={Reason}",
            _currentUser.GetUserId(), err);
        return (false, err);
    }
    ...
}
```

**影响**:🆕 **新必做项**。上次报告把 H1 列为 12 项,实际漏了 2 项。

### ⚠️ H2 · IDOR:`OrderDrawingNumbers` / `AddDrawingNumbers` 仍允许 Operator 改任意订单
**文件**: `Components/Pages/AddDrawingNumbers.razor:3`、`OrderDrawingNumbers.razor:3`
```razor
@attribute [Microsoft.AspNetCore.Authorization.Authorize(Roles = Admin + "," + Operator)]
```
- `C0XWriteService` 已加 Admin/Operator 校验
- 但**没有"CreatedBy 归属校验"** —— Operator A 可改 Operator B 创建的订单图号

**修复**(在 `C0XWriteService.AddDrawingNumbersAsync:74` 之后加):
```csharp
var order = await _db.Queryable<Orders>().InSingleAsync(orderId);
if (order == null) { ... }
if (!_currentUser.IsInAnyRole(RoleNames.Admin)
    && order.CreatedBy != _currentUser.GetDisplayName())
{
    _logger.LogWarning("...IDOR denied: caller={C} orderCreatedBy={O}", ...);
    return deniedResult;
}
```

### ⚠️ H3 · OrderBy(string) 字符串拼接已加白名单(已修)
**本次亲自复核 5 处**:OrdersService:30、UsersService:23、RolesService:25、DepartmentsService:19、BaseQueryService:48
- 每个 Service 有独立的 `HashSet<string>` 白名单
- 不在白名单的回退默认排序
- ✅ **已全部修复**

### ⚠️ H5 · HTTPS / HSTS / Cookie Secure
**文件**: `Program.cs`(无 `UseHttpsRedirection()`/`UseHsts()`)
- `appsettings.json:9` `"Urls": "http://localhost:8373"` —— 仍是 HTTP-only
- `services.AddIdentityCore<Users>()` 后没设置 `Cookie.SecurePolicy`
- web.config 已加 `Strict-Transport-Security`(HTTP 下浏览器忽略)
- ⚠️ 仍未启用 `UseHttpsRedirection()` + `UseHsts()`

### ⚠️ H6 · `DrawingName.sql`(38KB)含真实 PII 仍在仓库根
**文件**: `DrawingName.sql`(38KB,根目录,真实员工姓名/工号/时间)
- 加 `.gitignore`: `/*.sql`
- 已入库的:先脱敏再 push

### ⚠️ H7 · `SqlSugarUserStore.Users` getter 每次全表扫描
**文件**: `Services/Identity/SqlSugarUserStore.cs:44-51`
```csharp
public IQueryable<Users> Users {
    get {
        var list = _db.Queryable<Users>().Where(u => !u.IsDeleted).ToList();
        return list.AsQueryable();
    }
}
```
- 注释自己承认 "用户量大时是 DOS"
- Identity 框架偶尔会走 `Users`(如 UserManager.Users / RoleManager.Users)
- 当前未到量级,隐患存在

---

## 🟡 Medium(本次剩 6 项)

### ⚠️ M1 · 异常吞噬掩盖真实失败(全部仍在)
`Services/*.cs` 中普遍 catch → 空 `PagedResult`,如:
- `UsersService.cs:135` `"Error querying users"`
- `OrdersService.cs:135` `"Error searching orders"`
- `RolesService.cs:95` `"Error querying roles"`
- `DepartmentsService.cs:82` `"Error querying departments"`
- `DrawingNameService.cs:114` 返回 0(无日志)
- 等等

**至少关键路径已写 `_logger.LogError(ex, ...)`,做埋点的占多数。但 query 路径太普遍,建议抽一个 ErrorOr 结构让 Service 层无法选择"忽略"。**

### ⚠️ M2 · 数据库错误信息直接暴露给用户
**🆕 复盘仍存在**:
| 文件:行 | 暴露形式 |
|---|---|
| `Roles.razor:203` | `await ShowToast("查询失败:" + ex.Message, ToastCategory.Error);` |
| `Roles.razor:295` | `await ShowToast($"保存失败:{ex.Message}", ToastCategory.Error);` |
| `SqlSugarUserStore.cs:119/139/159` | IdentityError `Description = "操作失败,请查看日志"`(已修,谢谢 — 但要看从 Identity 流到 Razor 是否原样透传) |

**建议**:
```csharp
// 改 catch 内统一:
await ShowToast("查询失败,请重试", ToastCategory.Error);
_logger.LogError(ex, "...");
```

### ⚠️ M3 · 权限写入 cookie,撤销需重新登录
**文件**: `Services/Identity/DisplayNameClaimsFactory.cs`(未亲自读)
- `_permissionsService.GetCodesByUserIdAsync` 的结果在登录时序列化到 cookie claim
- 管理员改了 `/roles` 后所有用户必须重新登录才生效
- 当前架构如此设计,但要让最终用户知道(M3 之前已识别)

### ⚠️ M4 · 日志入 SQL 仍含 PII(部分修复)
- `Program.cs:25` SetMinimumLevel=Information ✅ 已修
- `NLog.config:74` `<logger name="*" minlevel="Info" writeTo="dbTarget,asyncFallback" />` —— Info+ 仍把 workerNumber / DisplayName / IP / UserId 写 `dbo.Logs`
- 没哈希 / 截断关联键

### ⚠️ M6 · 事务边界
- ✅ `RolesService.SaveAsync` 已加 `_db.Ado.BeginTran()` (`RolesService.cs:279-335`)
- ✅ `OrdersService.SaveAsync` 已加 `_db.Ado.BeginTran()` (`OrdersService.cs:211-253`)
- ✅ `PermissionsService.SetRolePermissionsAsync:72` —— **但这次确认只删 + 插两步独立,不是事务!**(RolesService 包事务后这个方法基本被淘汰,但还在)

### ⚠️ M8 · 已加 CSP + HSTS(web.config 已修)
**文件**: `web.config:38-40`
```xml
<add name="Content-Security-Policy" value="default-src 'self'; ... connect-src 'self' ws: wss:; frame-ancestors 'self';" />
<add name="Strict-Transport-Security" value="max-age=31536000; includeSubDomains" />
```
✅ CSP + HSTS 已加。HSTS 在 HTTP 下浏览器忽略,HTTPS 后生效。

### 🆕 M9 · `Roles.razor` 编辑角色时把所有权限清空(逻辑 bug)
**文件**: `Components/Pages/Roles.razor:211-221`
```csharp
private async Task<bool> OnSaveAsync(RoleRowViewModel row, ItemChangedType changedType)
{
    // ⚠️ Bug:任何"编辑保存"都传 Array.Empty<int>(),等于全清空 RolePermissions
    var (ok, error) = await RolesService.SaveAsync(
        row.Role,
        Array.Empty<int>(),          // ← 这里
        changedType.ToDomain());

    if (!ok) { ... }
    await ShowToast("已保存。点击「权限」按钮可分配权限码", ToastCategory.Success);
    return true;
}
```
**实质**:
- 角色改名保存时,Razor 只把角色名塞进去,权限 ID 数组是空
- `RolesService.SaveAsync` 走 `M6 已修的 BeginTran` 路径:`DELETE RolePermissions WHERE RoleId = ...` 然后 INSERT 空集合 → **该角色全部权限丢失**
- 角色权限丢失 → 所有依赖该角色的用户瞬时无权限
- 提示文案 "点击「权限」按钮可分配权限码" 暗示用户去手工恢复,但**没显式警告"会清空"**

**重现**:
1. 打开 `/roles`,选任意有权限的角色(非 ADMIN)
2. 不打开"权限"弹窗,直接改名称保存
3. 该角色所有用户失去所有权限;正常业务流程被静默打破

**修复方案**(任选):
- **方案 A(Razor 改)**:在 `RolesService.GetUsersInRoleAsync`/`GetCodesByRoleIdAsync` 之外,先查权限 ID 传过去:
```csharp
private async Task<bool> OnSaveAsync(RoleRowViewModel row, ItemChangedType changedType)
{
    // 编辑时先拉权限并保留,新增才用空
    var permIds = changedType == ItemChangedType.Update
        ? (await PermissionsService.GetCodesByRoleIdAsync(row.Role.Id))
            .Select(...)   // 转回 Id
            .ToList()
        : new List<int>();
    var (ok, error) = await RolesService.SaveAsync(row.Role, permIds, changedType.ToDomain());
    ...
}
```
- **方案 B(Service 防御性改)**:`RolesService.SaveAsync` 只接收"追加或删除"的 delta,不接收全替换
- **方案 C(短期 UI)**:把"角色名编辑"和"权限管理"分两步,改名前必须先 lock 提示

**影响**: 🆕 上线后用户改名的操作就会断权限,属业务可用性 + 安全(用户缺权限后强制走权限审计路径)双重问题。

### ⚠️ M7 · 详细错误配置(未亲自复核 appsettings.Development)

### 🆕 M10 · `BaseQueryService.DefaultSortField` 仍是字符串拼接(防御性弱)
**文件**: `Services/Queries/BaseQueryService.cs:78`
```csharp
// H3 白名单:不在 AllowedSortCols 的 SortName 一律忽略,回退默认排序
var allowed = AllowedSortCols;
var isSortAllowed = !string.IsNullOrEmpty(options.SortName)
    && allowed.Contains(options.SortName);
if (isSortAllowed) { ... }
else {
    query = query.OrderBy($"{DefaultSortField()} desc");   // ← 这里
}
```
**实质**:
- `DefaultSortField()` 由子类重写(如 `OrdersService.DefaultSortField() => "CreatedTime"`)
- 当前所有子类返回的字符串都是白名单的 column 名(写死),所以**实际不可注入**
- 但 `OrderBy(string)` 路径仍然存在,只是这次碰巧安全
- 防御性写:应当也走白名单校验

**修复**:
```csharp
protected virtual HashSet<string> AllowedDefaultSortCols => AllowedSortCols;
// 或在基类再校验一次 DefaultSortField() 返回值
```

---

## 🟢 Low(8 项,新增 1 项)

### ⚠️ L1 · `OrderLockProvider._locks` 字典无界增长 — 仍未修复
**文件**: `Services/OrderLockProvider.cs:17`
```csharp
private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
```
- 上次报告已识别,本次亲自复核仍未改
- 进程一旦长期运行,每个被访问过的 OrderId 都会留下 SemaphoreSlim
- `SemaphoreSlim` 本身不会被 GC
- **多实例水平扩展时**:`_locks` 不跨进程,**OrderLockProvider 注释也自承"仅在单实例进程内生效"** —— 上线必须保持单实例或改 SERIALIZABLE

**修复**:
- `WeakReference<int>` 包装,或定时清理 30 分钟未被持有的 entry
- 或者直接走 SQL SERIALIZABLE + 唯一约束

### ⚠️ L2-L8 · 同上次报告

### 🆕 L9 · `LogoutPage.razor` 没有 `[Authorize]` 但只重定向(无影响)
**文件**: `Components/Pages/LogoutPage.razor`
- 仅靠 `Nav.NavigateTo("/login", forceLoad: true)` 跳转,不做登出操作
- 真实登出在 AccountController.Logout
- 命名误导(叫 "LogoutPage" 但不做 logout),**应改名为 `LogoutRedirect.razor` 或合并到 MainLayout**

---

## 上线前必做清单(本次更新)

### 🔴 今天必做(3 项,与上次同)

| # | 任务 | 状态 |
|---|---|---|
| 1 | `git filter-repo --path-glob '**/key-*.xml'` + .gitignore | 未做 ⚠️ |
| 2 | 轮换 appsettings.json 中 3 组明文 DB 密码 + 迁到 env var | 未做 ⚠️ |
| 3 | `Identity.Password` 加 `RequireLength ≥ 10`,4 个 Require 全 true | 未做 ⚠️ |

### 🆕 本次新加的"今天"必做

| # | 任务 | 时间预估 |
|---|---|---|
| **4** | **🆕 修复 H1 `UsersService`:SaveAsync + SoftDeleteAsync 加 `RequireRole(Admin)`** | 5 min |
| **5** | **🆕 修复 M9:Roles.razor 编辑保存前先拉权限 ID(Razor)或 SaveAsync 接收 delta(Service)** | 30 min |

### 🟠 本周(与上次同)

| # | 任务 |
|---|---|
| 6 | OrderBy 全部 5 处加白名单(已修 ✅)|
| 7 | H2 IDOR 修复(`C0XWriteService.AddDrawingNumbersAsync` 加 CreatedBy 校验)|
| 8 | HTTPS / HSTS / Cookie.SecurePolicy |
| 9 | 移走 `DrawingName.sql`,加 `/*.sql` 到 .gitignore |
| 10 | L1 `OrderLockProvider._locks` 加清理路径或 WeakReference |

### 🟡 本月(本次微调)

| # | 任务 |
|---|---|
| 11 | M2:移除 `ex.Message` 到 UI — **本次新发现 `Roles.razor:203、295` 还有** |
| 12 | M4:NLog 配置 PII 哈希化 |
| 13 | M7:确认生产 ASPNETCORE_ENVIRONMENT=Production |
| 14 | H7:SqlSugarUserStore.Users 重构或限流 |
| 15 | M10:`BaseQueryService` DefaultSortField 加白名单校验 |
| 16 | L9:`LogoutPage.razor` 改名为 `LogoutRedirect.razor` |

---

## 已修复回顾(从上次到本次)

| 项 | 位置 |
|---|---|
| H1(部分) | OrdersService.SaveAsync/SoftDeleteAsync、RolesService 全部、DepartmentsService 全部、DrawingNameService 全部、PermissionsService.SetRolePermissionsAsync、C0XWriteService、DrawingNumberAllocator(共 12 + 3 = 15 处)**已加 RequireRole**。**仍漏:UsersService.SaveAsync、UsersService.SoftDeleteAsync**。 |
| H3 | 全部 5 处 OrderBy 都已加白名单 |
| M5 | Login 表单加 AntiforgeryToken + AccountController 加 ValidateAntiForgeryToken |
| M6 | RolesService.SaveAsync 加 BeginTran;OrdersService.SaveAsync 加 BeginTran |
| M8 | web.config 加 CSP + HSTS |

---

## 快速定位表(主项目,本次更新)

| 主题 | 主要文件 |
|---|---|
| 启动 + 管道 | `Program.cs` |
| DataProtection | `Program.cs:69-71` + `keys/`(仍含明文密钥 ⚠️) |
| Identity | `Services/Identity/*`、`Models/Identity/*` |
| SqlSugar 客户端 | `Data/SqlSugarSetup.cs` |
| 业务 Services | `Services/*.cs`(共 11 个) |
| 🆕 **登录页 + Controller** | `Components/Pages/Login.razor`、`Controllers/AccountController.cs`(**已修复 CSRF**) |
| Razor 授权声明 | `@attribute [Authorize(...)]`(共 10 个 .razor 第 3 行) |
| 🆕 **逻辑 bug** | **`Roles.razor:213` —— 编辑角色时清空权限** |
| 🆕 **H1 漏修** | **`UsersService.SaveAsync:155`、`UsersService.SoftDeleteAsync:206`** |
| 🆕 **白名单未覆盖** | **`BaseQueryService:78` DefaultSortField 仍走字符串拼接** |

---

## 给下次审查的提示

- 优先复核:🆕 本次新发现的 4 项(`UsersService` H1 + Roles.razor 逻辑 bug + BaseQueryService 弱化 + LogoutPage 命名)
- 复核 L1 上 OrderLockProvider 清理策略
- 跑 `dotnet test`,本轮审查未跑测试 —— 看新增 15 处 `RequireRole` 有没有破坏 UsersServiceChangePasswordTests 等回归
- 待 IWebHostEnvironment / web.config 部署到 IIS 后真跑一遍,验证 CSP 头实际下发(HTTP 下浏览器忽略 HSTS,但 CSP 应该下发)
