# 开发规范

## 命名约定

### 命名空间

统一 **file-scoped namespace**(C# 10+):

```csharp
// ✅ 推荐
namespace BootstrapBlazor.OnlyServer1.Models;

public class C01 : C0XBase
{
}

// ❌ 不推荐
namespace BootstrapBlazor.OnlyServer1.Models
{
    public class C01 : C0XBase
    {
    }
}
```

每个文件**只属于一个命名空间**,不需要嵌套。

### 文件 / 类 / 接口

| 元素 | 命名 | 示例 |
|------|------|------|
| 类 | PascalCase | `UsersService` |
| 接口 | `I` + PascalCase | `IDrawingTypeHandler` |
| 方法 | PascalCase | `SaveAsync` |
| 公共属性 | PascalCase | `OrderNumber` |
| 私有字段 | `_` + camelCase | `_db` / `_logger` |
| 局部变量 | camelCase | `orderId` |
| 常量 | PascalCase | `Admin` |
| 私有常量 | `_` + PascalCase | `_defaultPartOptions` |

### Razor 文件

| 元素 | 命名 | 示例 |
|------|------|------|
| 页面文件 | `<Name>.razor` | `Orders.razor` |
| 路由 | kebab-case | `/orders`、`/user-management` |
| 路由参数 | camelCase | `{orderId:int}` |
| 代码后置 | `<Name>.razor.cs` | `MainLayout.razor.cs` |
| CSS 后置 | `<Name>.razor.css` | `UserManagement.razor.css` |

### 文件夹

- 一个文件夹一类职责,不要"杂物间"
- 嵌套不超过 3 层
- 文件夹名复数(`Models`、`Services`、`Pages`)

## 可空性

`<Nullable>enable</Nullable>` 已开。

```csharp
// ✅ 引用类型必须显式标注
public string Name { get; set; } = "";      // 不允许 null
public string? Description { get; set; }     // 允许 null

// ❌ 不要为了"省事"全标 ?
public string? Name { get; set; }            // null 没意义,业务不允许
```

新建项目时:
- EF/SqlSugar 实体:string 默认 `= ""`,引用类型 `?`
- DTO:同上
- ViewModel:同上
- Service 方法签名:`Task<(bool ok, string? error)>`

## 错误处理

### Service 层

返回 `(bool ok, string? error)` 元组,**不要吞异常**:

```csharp
// ✅ 推荐
public async Task<(bool ok, string? error)> SaveAsync(Orders order, ItemChangedType ct)
{
    try
    {
        // ... 业务逻辑
        return (true, null);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error saving order: {OrderNumber}", order.OrderNumber);
        return (false, "保存失败,请重试");
    }
}

// ❌ 反面教材
public async Task<Orders?> GetByIdAsync(int id)
{
    try { return await _db.Queryable<Orders>().InSingleAsync(id); }
    catch { return null; }   // 吞掉所有 bug,真错时只看到"空"
}
```

### Razor 层

统一用 `ToastService` 给用户反馈:

```csharp
private async Task<bool> OnSaveAsync(Models.Orders order, ItemChangedType ct)
{
    var (ok, error) = await OrdersService.SaveAsync(order, ct.ToDomain());
    if (!ok && !string.IsNullOrEmpty(error))
    {
        await ToastService.Error(error);
    }
    return ok;
}
```

### 异常分类

| 异常类型 | 处理方式 |
|---------|---------|
| **业务规则违反**(用户名重复、订单号冲突) | 返回 `(false, "用户可读的中文消息")` |
| **未知异常**(NRE、SqlException 等) | 记录日志,返回通用错误,**不要翻译成业务术语** |
| **死锁 / 超时** | 返回"系统繁忙,请稍后重试",NLog 记录详细堆栈 |

## 日志规范

```csharp
// ✅ 结构化日志
_logger.LogInformation("Adding new order: {OrderNumber}", order.OrderNumber);
_logger.LogWarning(ex, "Duplicate OrderNumber: {OrderNumber}", order.OrderNumber);
_logger.LogError(ex, "Error saving order: OrderNumber={N}", order.OrderNumber);

// ❌ 不要
_logger.LogInformation($"Order {order.OrderNumber} added");  // 不结构化,grep 难
Console.WriteLine("order added");                            // 走 NLog 别用 Console
_logger.LogDebug(ex.ToString());                              // 不要整串堆栈,LogXxx 自动加
```

**日志级别**:
| 级别 | 何时用 |
|------|-------|
| `Trace` | 详细调试(默认不开) |
| `Debug` | SQL/参数序列化(默认不开) |
| `Information` | 业务事件(用户登录、新增订单) |
| `Warning` | 业务规则违反、可恢复异常(订单号重复、死锁重试) |
| `Error` | 未知异常、不可恢复错误 |
| `Critical` | 系统级故障(DB 连接失败、应用启动失败) |

## 注释规范

```csharp
// ✅ 注释"为什么"(WHY),不是"做什么"(WHAT)
/// <summary>
/// 用 SQL Server 单条 UPDATE...OUTPUT 语句原子自增,
/// 无应用层锁、无显式事务。早期版本曾用 SERIALIZABLE + 应用层预检,
/// 实测范围锁会导致 1-2 分钟无响应,详见 ADR 0005。
/// </summary>
public async Task<int> AllocateAsync(string drawingType, CancellationToken ct = default)

// ❌ 不写"做什么",代码本身已经说了
/// <summary>
/// 分配图号
/// </summary>
public async Task<int> AllocateAsync(string drawingType)
```

**注释三原则**:
1. **WHY 写注释**(隐藏约束、决策原因、踩过的坑)
2. **WHAT 不写注释**(好的命名已经说清楚了)
3. **ADR 写决策背景**(WHY 涉及多文件/多决策时写 ADR)

## 异步编程

```csharp
// ✅ async Task + CancellationToken
public async Task<PagedResult<Users>> QueryAsync(PagedQuery options, CancellationToken ct = default)

// ❌ async void(除 Blazor EventCallback)
public async void Save()    // 异常无法捕获

// ❌ 异步方法没 await
public async Task<X> Get()   // 警告 CS1998
{
    return Task.FromResult(...);   // 直接同步返回,不标 async
}
```

## Razor 组件

### 参数注入

```razor
@* ✅ 用 [Inject] 属性注入 *@
@inject OrdersService OrdersService
@inject ToastService ToastService

@code {
    // code-behind 类里用 [Inject] + private set
    [Inject] private CurrentUserAccessor? CurrentUser { get; set; }
}

@* ❌ 在 @code 里手动 builder.Service *@
```

### 状态管理

```razor
@* ✅ 私有字段(不暴露给外部) *@
@code {
    private bool _isLoading;
    private List<DrawingNames> _drawingNames = new();
}

@* ❌ public 属性 *@
@code {
    public bool IsLoading { get; set; }   // 让组件可以外部改状态,破坏封装
}
```

### 路由参数

```razor
@page "/add-drawing-numbers/{orderId:int}"

@code {
    [Parameter]
    public int OrderId { get; set; }   // 必须 public 属性 + [Parameter]
}
```

### 不要在 Razor 里写业务逻辑

```razor
@* ❌ 在 Razor 里写 SQL *@
@code {
    private async Task<List<Users>> LoadUsers()
    {
        return await Db.Queryable<Users>().ToListAsync();   // 绕过 Service 层
    }
}

@* ✅ 调 Service *@
@code {
    private async Task LoadUsers()
    {
        var result = await UsersService.QueryAsync(options);
        _users = result.Items;
    }
}
```

## 数据库查询

### 避免 N+1

```csharp
// ❌ 每行一次查询
foreach (var r in roles)
{
    vm.UserCount = await RolesService.CountUsersInRoleAsync(r.Id);   // N 次往返
}

// ✅ 批量查询 + 内存聚合
var userCounts = await RolesService.GetUserCountsByRoleIdsAsync(roleIds);  // 1 次
foreach (var r in roles)
{
    vm.UserCount = userCounts.TryGetValue(r.Id, out var c) ? c : 0;
}
```

### 显式 Select 投影

```csharp
// ❌ 拉所有字段(包括 PasswordHash / SecurityStamp 等大字段)
var items = query.ToList();

// ✅ 只查需要的字段
var items = query.Select<Users>((u, d) => new Users
{
    Id = u.Id, Name = u.Name, ...
}).ToList();
```

### 事务隔离级别

- 默认 `READ COMMITTED` 即可
- 不要用 `SERIALIZABLE`(范围锁导致等锁,见 ADR 0005)
- 唯一性靠 `UNIQUE INDEX` + catch SqlException 2627/2601

## Git 操作

### 不要提交到 git

- `bin/ obj/`(已在 `.gitignore`)
- `appsettings.Development.json`(本地配置)
- 明文密码 / API Key
- 个人调试代码

### Commit 前自查

```powershell
dotnet build
dotnet test
git diff --stat
```

## 相关 ADR

- [ADR-0003](adr/0003-file-scoped-namespace-convention.md) — 命名空间风格
- [ADR-0005](adr/0005-replace-serializable-with-unique-index.md) — 事务隔离级别