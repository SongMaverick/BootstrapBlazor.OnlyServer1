# 主项目 BootstrapBlazor.OnlyServer1 第 3 次权限代码审查(2026-07-09)

> **审查范围**:仅权限相关代码(Accounts / ClaimsPrincipal / Service 层权限校验 / UI 层 Authorize)。
> **对照基线**:07-08 报告(`code-review-onlyserver1-2026-07-08.md`)的 4 个新必做项 + 仍未修项。
> **审查方式**:全量精读 12 个文件,确认上次审查项的修复状态 + 找出新缺陷。

---

## 一、上次 07-08 报告 4 项 — 状态复核

| # | 07-08 项 | 状态 | 证据 |
|---|---|---|---|
| 1 | **H1 漏修** — `UsersService.SaveAsync:155` 和 `SoftDeleteAsync:206` 无 `RequireRole(Admin)` | ✅ **已修** | `UsersService.cs:168` + `:238` 都有 `RequireRole(RoleNames.Admin)` + 日志 + 注释 |
| 2 | **M9 逻辑 bug** — `Roles.razor:213` 编辑角色时 `Array.Empty<int>()` 清空所有 `RolePermissions` | ✅ **已修** | `RolesService.SaveAsync:248-355` 改 delta 签名,新增 `ApplyPermissionDeltaAsync:363-400` 私有 helper;`Roles.razor:215` 传 `(null, null)` |
| 3 | **M10 注入风险** — `BaseQueryService:78` `OrderBy($"{DefaultSortField()} desc")` 字符串拼接 | ⚠️ **部分** | `OrdersService:71` override `AllowedSortCols` 后,继承 QueryAsync 自动套用白名单;但 **直接调用 `OrderBy(string)` 仍存在** — 既然子类都走 `QueryAsync(PagedQuery)`,**底层的 `OrderBy(string)` 应改为 `protected` 或加 `[Obsolete]` 警告** |
| 4 | **L9 命名** — `LogoutPage.razor` 不做 logout | ❌ **未修** | 文件名仍是 `LogoutPage.razor` |

---

## 二、🆕 本次新发现的缺陷

### 🔴 H1-NEW: 权限 Claim 缓存陈旧,角色/权限变更不立即生效(**P0 安全隐患**)

**位置**:`DisplayNameClaimsFactory.cs:47-51` + `Program.cs:49-60` 缺 `SecurityStampValidator`

**现象**:
- `DisplayNameClaimsFactory.GenerateClaimsAsync` 只在用户 **SignIn 时** 把 Permission claim 写进 cookie
- 当 Admin 修改了某用户的角色,或某角色的权限码,被修改用户的 cookie 仍然带着旧 claim
- 该用户重新登录前都能继续按旧权限操作

**攻击/危害场景**:
1. Admin 把员工 X 从 Operator 降为 Viewer(假设 X 正在浏览器保持登录)
2. X 刷新页面,菜单仍显示"订单列表 / 添加图号"等 Operator 项
3. X 调任何走 `CurrentUser.HasPermission(...)` 按钮级判断的代码,仍返回 true
4. X 调 `UsersService.SaveAsync` 等 **Service 层硬编码的 `RequireRole(Admin)`** 会被拒;但 **订单、图号等没走 Service 硬编码、只走 Claim 检查** 的入口会被绕过

**复现路径**:
- Roles.razor 的"权限"按钮弹窗(`SavePermsAsync:286-303`)只显示"已保存 N 个权限,相关用户需重新登录生效"
- 但 **系统没有强制相关用户的 session 失效**,也没有缩短 cookie 有效期
- 默认 SecurityStamp 轮换是 30 分钟(Identity 默认 `SecurityStampTimeSpan` = 30 days 实际)

**修复方案**(择一):
1. **启用 SecurityStampValidator**(推荐):在 `Program.cs` 中
   ```csharp
   services.Configure<SecurityStampValidatorOptions>(o => {
       o.ValidationInterval = TimeSpan.FromMinutes(5); // 5 分钟一查
   });
   ```
   这样 cookie 每 5 分钟回服务端校验一次 `users.SecurityStamp`,若 DB 改了 Stamp 就强制重登。

2. **权限变更后主动签出**:在 `RolesService.SaveAsync` / `SetUserRolesAsync` / `PermissionsService.SetRolePermissionsAsync` 末尾遍历受影响 userId 调 `IAuthenticationService.SignOutAsync`。
   - 优点:精确
   - 缺点:多 session 维护成本高

3. **HasPermission 走 DB 不走 Claim**:把 `CurrentUser.HasPermission` 改成走 `PermissionsService.GetCodesByUserIdAsync` 实时查。
   - 优点:实时准确
   - 缺点:每次 HasPermission 都查 DB,量大;要本地缓存 + 短 TTL

**优先级**:🔴 **高** — 这是 07-08 报告未识别的真正"权限吊销失效"问题,比 IDOR 更难发现。

---

### 🟠 M3-NEW: `OrdersService` IDOR 用 `DisplayName` 字符串比对,可被重名绕过

**位置**:`OrdersService.cs:115-127`

```csharp
if (changedType == ItemChangedType.Update)
{
    var isAdmin = _currentUser.IsInAnyRole(RoleNames.Admin);
    var currentName = _currentUser.GetDisplayName() ?? "";
    var isOwner = !string.IsNullOrEmpty(order.CreatedBy) && order.CreatedBy == currentName;
    if (!isAdmin && !isOwner) { return (false, "..."); }
}
```

**问题**:`order.CreatedBy` 存的是 `DisplayName`(用户姓名),不是 userId。两个 "张三" 之间可以互相改对方创建的订单;或者员工 A 改名后,新来的同名人 B 能改 A 的历史订单。

**修复**:
1. `Orders` 模型加 `CreatedByUserId` 字段(int),比较走 `Id`
2. 或者在 `Orders` 表已有 `CreatedBy` 列存 userId(WrokerNumber),改比对为 `order.CreatedBy == _currentUser.GetUserName()`
3. **不要用 DisplayName 字符串做权限判定**

**优先级**:🟠 **中** — 现实触达需要重名/改名场景,概率不高但可被定向利用。

---

### 🟠 M4-NEW: `PermissionsService.SetRolePermissionsAsync` 授权失败时 **静默 return**

**位置**:`PermissionsService.cs:73-95`

```csharp
public async Task SetRolePermissionsAsync(int roleId, IEnumerable<int> permissionIds)
{
    var (ok, err) = _currentUser.RequireRole(Models.Identity.RoleNames.Admin);
    if (!ok) { _logger.LogWarning(...); return; }  // ← 静默 return
    // ... 真正干活
}
```

**问题**:方法签名是 `Task` 不是 `Task<bool>`,授权失败时 **不抛异常也不返回 false**,调用方无法区分"成功"和"被拒绝"。

**实际影响**:`Roles.razor:286-303` 的 `SavePermsAsync` 没 try/catch,直接走完会显示"已保存 N 个权限"。如果有任何路径(后台任务/Controller)直接调 `SetRolePermissionsAsync` 而非 Admin,会得到"成功"的假象。

**修复**:
```csharp
public async Task<bool> SetRolePermissionsAsync(int roleId, IEnumerable<int> permissionIds)
{
    var (ok, err) = _currentUser.RequireRole(RoleNames.Admin);
    if (!ok) { _logger.LogWarning(...); return false; }
    // ...
    return true;
}
```
对应 `Roles.razor:291` 改成 `if (await PermissionsService.SetRolePermissionsAsync(...)) { ... }`

**优先级**:🟠 **中** — UI 层 `[Authorize(Roles=Admin)]` 已挡,实际触达需要"非 Admin 调 Service"的场景,目前不存在这种路径,但纵深防御需要修。

---

### 🟠 M5-NEW: Logout 表单 **没有 AntiforgeryToken**

**位置**:`MainLayout.razor:19-23` + `AccountController.cs:90-92`

```razor
<form method="post" action="/Account/Logout" class="d-inline">
    <button type="submit" class="dropdown-item">...退出登录</button>
</form>
```

```csharp
[HttpPost("Logout")]
[Authorize]
public async Task<IActionResult> Logout() { ... }
```

**问题**:
- 登录 (`Login`) 已加 `[ValidateAntiForgeryToken]`(07-08 修)
- **登出没加** — form 里也没有 `<AntiforgeryToken />`
- 任何能"诱骗用户加载图片/链接"的攻击者,都能让用户被强制登出
- 严重性看似低(登出不算敏感),但可与"会话固定 / 重放"组合攻击

**修复**:
1. form 内加 `<AntiforgeryToken />`(`MainLayout.razor:19`)
2. Controller 加 `[ValidateAntiForgeryToken]`

**优先级**:🟠 **中** — M5 报告只盯 Login,登出被遗漏。

---

### 🟡 L2-NEW: `OrderLockProvider` 无界字典仍存续(L1 已知未修)

**位置**:`OrderLockProvider.cs:17, 33`

```csharp
private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
var sem = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
```

**问题**:
- `_locks` 永远增长,被 Acquire 的 orderId 不会清理
- 单实例运行 1 年后,处理 10 万个订单就会留 10 万个 SemaphoreSlim
- 攻击者构造大量 orderId 触发 Acquire 即可 OOM
- 实际中 `OrderLockProvider` 只在受控的 Service 层调用,但纵深防御角度仍需修

**修复**:
- 在 Acquire 成功后启动一个延迟释放 timer,空闲 X 分钟后清理
- 或改用 `ConditionalWeakTable` / `LruCache<int, SemaphoreSlim>`

**优先级**:🟡 **低**(07-08 报告 L1 已记,本次复核仍未修)。

---

### 🟡 L3-NEW: `LogoutPage.razor` 命名误导(L9 已知未修)

**位置**:`Components/Pages/LogoutPage.razor`

**问题**:文件叫 LogoutPage 但只重定向到 /login,不执行 SignOut。

**修复**:改名 `LogoutRedirect.razor`。

**优先级**:🟡 **低** — 命名问题,功能正确。

---

### 🟡 L4-NEW: `RolesService.SetUserRolesAsync` 唯一 admin 保护存在 TOCTOU 窗口

**位置**:`RolesService.cs:574-586`

```csharp
var currentRoleNames = await _userManager.GetRolesAsync(user);
if (currentRoleNames.Contains(AdminRoleName) && !targetNames.Contains(AdminRoleName))
{
    var adminCount = await _db.Queryable<UserRolesRow>()...CountAsync();
    if (adminCount <= 1) { return (false, "..."); }  // ← 校验
}
// 之后:RemoveFromRolesAsync / AddToRolesAsync
```

**问题**:
- "判断 adminCount <= 1"和"RemoveFromRolesAsync"是两次独立操作
- READ COMMITTED 隔离级下,两个并发请求都通过"剩余 admin > 1"校验
- 都执行 RemoveFromRolesAsync → 系统锁死(无 admin)
- 极端并发下可触发

**修复**:
- 把"判断 + 移除"包到 SERIALIZABLE 事务中
- 或加 `users.SecurityStamp` 乐观锁比对

**优先级**:🟡 **低** — 极低概率,但生产环境一旦发生无法恢复。

---

### 🟡 L5-NEW: `SqlSugarUserStore.Users` 全表扫描(H7 已知未修)

**位置**:`SqlSugarUserStore.cs:45-52`

```csharp
public IQueryable<Users> Users
{
    get { var list = _db.Queryable<Users>().Where(u => !u.IsDeleted).ToList(); return list.AsQueryable(); }
}
```

**问题**:
- 注释承认是"全表拉内存" — 用户上千即成隐患
- Identity 框架很少走 `Users` 属性,实际影响有限
- 但有可被反射/扩展触发的风险

**修复**:
- 删掉 IQueryableUserStore 接口实现,只暴露精确查询方法
- 或实现 IQueryable<TUser> 适配器把 Expression 翻译给 SqlSugar

**优先级**:🟡 **低**(07-08 报告 H7 已记)。

---

### 🟢 其他细节

| 位置 | 描述 | 优先级 |
|---|---|---|
| `SqlSugarUserStore.CreateAsync:120` | catch (Exception ex) 后只返回 `Failed` 描述,异常被吞,无日志 — 失败时无排查线索 | 🟢 极低 |
| `Orders.razor:151-165` `OnAddAsync` 用 `_currentUser.GetDisplayName()` 预填 `CreatedBy` — 重名员工的 `CreatedBy` 同样会重名,加剧 M3-NEW 影响 | 🟢 极低(合并到 M3 修复) |
| `MainLayout.razor:15-18` | "个人中心 / 设置 / 通知" 三个菜单项是 `<a href="#">` 占位,点击不做事;不算安全缺陷但 UX 误导 | 🟢 UX |

---

## 三、按优先级汇总

### 🔴 上线前必做(P0 / P1)

1. **H1-NEW: 启用 SecurityStampValidator**,缩短权限变更生效延迟 → `Program.cs:60` 之后加
   ```csharp
   services.Configure<SecurityStampValidatorOptions>(o =>
       o.ValidationInterval = TimeSpan.FromMinutes(5));
   services.AddScoped<SecurityStampValidator<Users>>();
   ```
   (否则 1 次"降权"操作的生效延迟最长 = cookie 过期时间 = 默认 14 天)

2. **M3-NEW: 改 `OrdersService` IDOR 为 userId 比对** — `CreatedBy` 改存 userId(WrokerNumber / Id)而非 DisplayName

3. **M5-NEW: 补 Logout 表单 + Controller 的 antiforgery** — `MainLayout.razor:19` 加 `<AntiforgeryToken />`,`AccountController.cs:90` 加 `[ValidateAntiForgeryToken]`

### 🟠 本周内建议(P2)

4. **M4-NEW: `PermissionsService.SetRolePermissionsAsync` 改返回 `Task<bool>`**,失败时不静默
5. **L2-NEW: `OrderLockProvider` 加超时清理**,防字典无限增长
6. **L3-NEW: `LogoutPage.razor` 改名 `LogoutRedirect.razor`**

### 🟡 后续可优化(P3)

7. **L4-NEW: 唯一 admin 保护改为 SERIALIZABLE 事务**
8. **L5-NEW: `SqlSugarUserStore.Users` 改为精确查询** / 拆 IQueryableUserStore 接口
9. **M10 跟进: `BaseQueryService.OrderBy(string)` 改为 `protected` 或 `[Obsolete]`**,让子类只走 AllowedSortCols 路径

---

## 四、对比 07-08 报告待办状态

| 07-08 待办 | 07-09 状态 |
|---|---|
| H1 漏修 2 处 | ✅ 已修 |
| M9 编辑角色清空权限 | ✅ 已修 |
| M10 注入风险弱化 | ⚠️ 部分(子查询已修,基类签名未限制) |
| L9 命名误导 | ❌ 未修 |
| C-1a 明文 DB 密码 | ❌ 未修(本次未涉及) |
| C-1b DataProtection 密钥入库 | ❌ 未修 |
| C-3 弱密码策略 | ❌ 未修 |
| H2 IDOR | ⚠️ 部分(Orders 改用 DisplayName 字符串,仍不严) |
| H5 HTTPS/HSTS/Cookie Secure | ❌ 未修 |
| H6 DrawingName.sql PII | ❌ 未修 |
| H7 SqlSugarUserStore 全表扫描 | ❌ 未修 |
| L1 OrderLockProvider 无界字典 | ❌ 未修 |

---

## 五、本次新发现的"已知未修"追加

- 🆕 **H1-NEW 权限 Claim 缓存陈旧** — 07-08 未识别,本次最严重发现
- 🆕 **M3-NEW IDOR 字符串比对**
- 🆕 **M4-NEW 静默 return**
- 🆕 **M5-NEW Logout CSRF 漏修**
- 🆕 **L2/L3/L4/L5-NEW 已知未修项的复核确认**

---

**审查人**:Claude
**审查日期**:2026-07-09
**下次审查建议**:H1-NEW(权限吊销)和 M3-NEW(IDOR)修复后做一次回归。
