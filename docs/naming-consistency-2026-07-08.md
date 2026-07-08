# 命名一致性审查报告 — BootstrapBlazor.OnlyServer1

| 项目 | 值 |
|---|---|
| 审查日期 | 2026-07-08 |
| 审查范围 | `BootstrapBlazor.OnlyServer1/`(主项目命名一致性)|
| 项目路径 | `D:\D7_codeBase\BootstrapBlazor.OnlyServer1` |
| 审查者 | Claude Code |

> **状态**:
> 🆕 = 此次发现
> 🔁 = 之前可能存在但需要再次强调
> 建议优先级 **🔴 高 / 🟠 中 / 🟡 低**

---

## 摘要

| 主题 | 数量 | 严重项 |
|---|---|---|
| 文件名 vs 类名不一致 | 1 | 🔴 |
| 同一 DB 表的多个类名 | 1 | 🟠 |
| 缩略词不规范(全大写) | 1 | 🟡 |
| 方法命名风格不一致 | 4 组 | 🟠 |
| 领域概念命名混淆 | 2 | 🟠 |
| DTO / VM 放置位置不一致 | 3 个类 | 🟡 |
| 注释中术语不一致 | 3 处 | 🟢 |

---

## 🔴 文件名 vs 类名不一致

### L-1 · `Models/SortOrder.cs` 的文件名与类名不一致
**文件**: `Models/SortOrder.cs:1-12`
```csharp
namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 排序方向。独立于 BootstrapBlazor 的同名枚举...
/// 命名取 "SortDirection" 是为与 UI 框架的 SortOrder 区分。
/// </summary>
public enum SortDirection
{
    Asc = 0,
    Desc = 1,
}
```
- 文件名:`SortOrder.cs`
- 类名:`SortDirection`
- **不一致** — 文件名/类名应当一致
- 注释解释为什么类名取 `SortDirection`(避开 BootstrapBlazor 的 SortOrder),但**没改文件名**,违反 C# 一文件一类型惯例

**修复**:
- 选项 A:`SortOrder.cs` → `SortDirection.cs`(推荐,跟随类名)
- 选项 B:类名改回 `SortOrder`,改用 alias 避开 UI 框架冲突

---

## 🟠 同一 DB 表对应多个类名

### ✅ L-2 · `UserRoles` 表有两个类:`UserRolesRow` 和 `UserRoleRow` —— **已修复 (2026-07-08)**

| 类 | 位置 | 修饰符 |
|---|---|---|
| `UserRolesRow` | `Services/PermissionsService.cs:112` | `public` |
| `UserRoleRow` | `Services/Identity/SqlSugarUserStore.cs:543-550` | `private` |

```csharp
// Services/Identity/SqlSugarUserStore.cs:543
[SugarTable("UserRoles")]
private class UserRoleRow     // ← 注意:无 's'
{
    [SugarColumn(IsPrimaryKey = true)]
    public int UserId { get; set; }
    [SugarColumn(IsPrimaryKey = true)]
    public int RoleId { get; set; }
}

// Services/PermissionsService.cs:112-116
[SugarTable("UserRoles")]
public class UserRolesRow      // ← 注意:有 's'
{
    [SugarColumn(IsPrimaryKey = true)] public int UserId { get; set; }
    [SugarColumn(IsPrimaryKey = true)] public int RoleId { get; set; }
}
```
- 两个类都是 `[SugarTable("UserRoles")]`,映射同一张表
- 命名差一个 `s`(`UserRolesRow` vs `UserRoleRow`)
- 跨文件复制/粘贴时容易写错

**修复**:
- 统一为 `UserRolesRow`(放在 Models/ 或 Models/Identity/ 公共位置)
- `SqlSugarUserStore` 改用 `using` 别名引用公共类
- 命名向真实表名对齐

**修复结果(2026-07-08)**:已统一为 `UserRolesRow`,canonical 位置 `Models/Identity/UserRolesRow.cs`(public)。原 `SqlSugarUserStore.UserRoleRow` 私有类已删除,改为引用公共类。所有调用点已更新(PermissionsService、RolesService、UsersService、SqlSugarUserStore、DbMigrator)。L-10 也随之完成。

---

## 🟠 方法命名风格不一致

### ✅ L-3 · `Get` vs `Find` 混用表示 "按 Id 查单个" —— **已修复 (2026-07-08)**

| 服务 | 方法名 | 模式 |
|---|---|---|
| `OrdersService` | `FindByIdAsync(int orderId)` | **Find** + ById | ← 原 `GetByIdAsync` 已改名 |
| `C0XQueryService` | `FindDrawingNumberByIdAsync(int id, string drawingType)` | **Find** + ById |
| `C0XQueryService` | `FindByDrawNumberAsync(int drawNumber)` | **Find** + ByDrawNumber |
| `C0XQueryService` | `FindDrawingNumberByTypeAndNumberAsync` | **Find** |
| `SqlSugarUserStore` | `FindByIdAsync`、`FindByNameAsync`、`FindByEmailAsync`、`FindByLoginAsync` | **Find** |
| `SqlSugarRoleStore` | `FindByIdAsync`、`FindByNameAsync` | **Find** |

**问题**:
- Repository / Service 通用惯例:**`Get` 返回已确定存在的实体(可能 throw)**,**`Find` 返回可能 null**
- `OrdersService.GetByIdAsync:68` 实际是 catch 异常返回 null — 行为上是 Find
- C# / .NET BCL 几乎没有 `GetByIdAsync`(常用 `Find` 或 LINQ `FirstOrDefault`)

**建议**(任一):
- 方案 A:**全部统一为 `FindByXxxAsync`**(返 null 表示不存在)
- 方案 B:**保留 Get 但严格规定 Get 抛异常 / Find 返 null**,并在异常时 throw `KeyNotFoundException`

**修复结果(2026-07-08)**:选择方案 A。`OrdersService.GetByIdAsync` 已改名为 `OrdersService.FindByIdAsync`,返回 `Task<Orders?>`(null 表示不存在或异常)。2 个 Razor 调用点(AddDrawingNumbers、OrderDrawingNumbers)已同步更新。

### ✅ L-4 · `Query` vs `Search` 混用表示 "分页列表查询" —— **已修复 (2026-07-08)**

| 服务 | 方法名 | 模式 |
|---|---|---|
| `RolesService` | `QueryAsync(PagedQuery options)` | **Query** |
| `UsersService` | `QueryAsync(PagedQuery options)` | **Query** |
| `DepartmentsService` | `QueryAsync(PagedQuery options)` | **Query** |
| `OrdersService` | `QueryAsync(PagedQuery, ...)`(继承自 BaseQueryService) | **Query** | ← 原 `SearchAsync` 已删除 |
| `BaseQueryService.QueryAsync` | 默认 | **Query** |

**问题**:
- `OrdersService.SearchAsync` 跟其他 3 个 `QueryAsync` 行为完全一致(都是 PagedQuery 多字段过滤+排序+分页)
- 方法签名还多带了 3 个 string? 参数(orderNumberKeyword/createdByKeyword/reactorFilter),看起来"更复杂"
- 但完全可改名为 `QueryAsync(PagedQuery options, ...)` 对齐其他

**建议**:改 `OrdersService.SearchAsync` → `OrdersService.QueryAsync`,签名保持;或新增 shared `SearchOrdersQuery` DTO 把 keyword 收纳进 `PagedQuery.SearchText`。

**修复结果(2026-07-08)**:`OrdersService.SearchAsync` 已删除,改为继承 `BaseQueryService.QueryAsync`(通过 `AllowedSortCols` 等虚属性定制 Orders 专属逻辑)。Orders.razor 调用点同步更新。`PagedQuery.SearchText` 统一过滤,keyword 参数收纳完成。

### L-5 · `GetActive` / `GetAll` / `GetAllActive` 不一致

```csharp
// Services/DepartmentsService.cs:37
public Task<List<Departments>> GetActiveAsync()                  // 仅 IsActive=true 且未软删

// Services/RolesService.cs:102
public Task<List<Roles>> GetAllAsync()                          // 全部(含软删)

// Services/PermissionsService.cs:28
public Task<List<Permissions>> GetAllActiveAsync()              // IsActive=true
```

`GetAll*` 与 `GetActive*` 语义不同,看命名不易区分:
- `GetActiveAsync`(Departments)— 仅 active
- `GetAllActiveAsync`(Permissions)— 仅 active
- `GetAllAsync`(Roles)— 全部

**建议**:加注释或统一命名(让"active" 在 `DepartmentsService` 也是 `GetAllActiveAsync`,虽然形式冗长但语义清晰)。

### L-6 · `GetAllDrawingNames` vs `GetAllDrawingNamesIncludingDeleted` 长名称不优雅

```csharp
// Services/DrawingNames/DrawingNameService.cs:31-45
public async Task<List<...>> GetAllDrawingNamesAsync()
public async Task<List<...>> GetAllDrawingNamesIncludingDeletedAsync()
```

两方法只是差 `IncludeDeleted` 布尔。**建议**:
```csharp
public Task<List<DrawingNames>> GetAllAsync(bool includeDeleted = false);
```
节省长名 + 调用方一眼明白可选参数。

---

## 🟠 领域概念命名混淆

### L-7 · `DrawingNumber` / `DrawNumber` / `DrawingName` / `DrawName` 四词搅在一起

| 概念 | C# 实体 | DB 表 | 类型 |
|---|---|---|---|
| 图号(整数,如 17001) | `C0XBase.DrawNumber` | C01..C09 表的 `DrawNumber` 列 | int |
| 图号序列表 | `DrawingNumberSequence` | `DrawingNumberSequence` | class |
| 图号视图模型 | `DrawingNumberViewModel` | (无表) | VM |
| 图名(文本,如 "油箱") | `C0XBase.DrawName` | C01..C09 表的 `DrawName` 列 | string |
| 图名主数据 | `Models.DrawingNames` | `DrawingName` 表(单数表名,反约定) | class |
| 图名添加服务 | `DrawingNameService` | (无表) | service |

**问题**:
- `DrawingNumber`(类/服务) vs `DrawNumber`(属性/列) — **同一概念两个名字**,只是截断位置不同
- `DrawingNames`(类) vs `DrawingName`(DB 表名 `DrawingName` 单数)— **类用复数,表用单数**,违反"类名跟表名同形"的默认约定
- `DrawName` vs `DrawingName` 又再次混淆:`DrawName` 是"图号行的图纸名",`DrawingName` 是"图名主数据"

**建议**:
| 建议名 | 现在 |
|---|---|
| `C0XBase.Number` 或 `C0XBase.DrawingNumber` | `C0XBase.DrawNumber` |
| `Models.DrawingName`(单数) | `Models.DrawingNames`(复数) |
| **DB 表 `DrawingNumbers`(复数)** | DB 表 `DrawingName`(单数) |

**实际迁移成本** 较高(改字段会触发 SqlSugar CodeFirst 重命名,可能影响生产 DB)。建议:
- 短期:在 `DrawName`、`DrawNumber` 上加 `// TODO` 注释解释命名来历
- 中期:`DrawNumber` → `DrawingNumber`(属性改,DB 列加 ColumnName 别名保持兼容)

### L-8 · `GID` 是公司缩写还是字段含义不明

`Users.GID`:
- 全大写 GID 看上去像 SQL 表风格的 "主键 ID"
- 实际语义:员工在公司 OA 系统中的唯一标识(Global ID?)
- .NET 约定:缩略词仅 2 字母全大写,3 字母及以上的用 PascalCase → 应是 `Gid`
- 所有引用都用 "GID" 字符串:`ClaimsPrincipalExtensions.GIDClaim = "GID"`、`Principal?.FindFirstValue("GID")`
- DB 列名应该是 `GID`(若 legacy 数据库已固定,保持现状)

**建议**:
- 选项 A:重命名为 `Gid`(C# 习惯)且 Claim 字符串用 `gid`(小写)
- 选项 B:保持现状,因为 DB 列已用 `GID`,改了反而要再加 ColumnName map
- **如果未来要做 ETL / 跨系统对接,统一为 `Gid` 更标准**

---

## 🟡 DTO / VM 放置位置不一致

### ✅ L-9 · 三个 DTO 散落在不同目录 —— **已修复 (2026-07-08)**

| 类 | 位置 | 实际角色 |
|---|---|---|
| `DrawingNumberViewModel` | `Models/`(直接放) | 视图模型(VM)|
| `RoleRowViewModel` | `Models/ViewModels/` | 视图模型(VM)|
| `UserWithRolesViewModel` | `Models/ViewModels/` | 视图模型(VM)|
| `DrawingNumberInsertResult` | `Services/`(混进服务目录) | DTO(成功列表 + 失败列表)|

**建议**:
- 所有 VM 集中到 `Models/ViewModels/`
- `DrawingNumberInsertResult` 应移到 `Models/`(它是数据载体,不是业务行为)

**修复结果(2026-07-08)**:
- `DrawingNumberViewModel.cs` 从 `Models/` 移到 `Models/ViewModels/`
- `DrawingNumberInsertResult.cs` 从 `Services/` 移到 `Models/`
- 5 个 page-local DTO 提取到 `Models/ViewModels/`: `CheckboxItem` / `CardStyle`(from AddDrawingNumbers.razor)、`DrawingNameAddModel`(from DrawingNameList.razor)、`FindDrawingNumberQueryModel`(from FindDrawingNumber.razor)、`OrderDrawingNumberEditModel`(from OrderDrawingNumbers.razor)
- 全局 `_Imports.razor` 已包含 `Models.ViewModels`,Razor 页面无需改 `@using`

### ✅ L-10 · `UserRolesRow`(PermissionsService)放在 Services/ 下违反分层 —— **已修复 (随 L-2 一并)**

---

## 🟡 缩略词/术语不规范

### L-11 · 中文注释里写"ID"但 C# 命名用 `Id`
- `Models/OrdersItem.cs:12` 注释"主键ID"
- `Models/OrdersItem.cs:18` 注释"旧订单ID"
- `Models/OrdersItem.cs:24` 注释"新订单ID"
- `Components/Pages/OrderDrawingNumbers.razor:130` UI 文字 "主键 ID:@xxx"
- 但实际字段名都是 `Id`、`OldOrderId`、`NewOrderId`(用 PascalCase,正确)

**建议**:中英混合的注释/UI 文档里保持用 "ID" 也 OK,但代码示例或类成员说明尽量用 `Id` 与代码对齐。

### L-12 · `IsDeleted` vs `Isdelete`(DB 列大写规则不一)
- `Models/DrawingNames.cs:47`: `[SugarColumn(ColumnName = "Isdelete")] public bool IsDeleted`
- DB 列名 `Isdelete` 是 PascalCase 不规范:`IsDelete` 或全大写 `ISDELETE` 都不对,小写开头的 `Isdelete` 是 SQL Server 历史数据迁移遗留
- 其余表 `IsDeleted`(PascalCase 正确)
- **建议**:DbMigrator 重命名 SQL Server 列 `Isdelete` → `IsDeleted`,跟模型 + 同库其他表对齐

---

## 🟢 注释/UI 文本术语不一致

| 位置 | 现状 | 备注 |
|---|---|---|
| `AccountController.cs:111` 注释 "X-Forwarded-For 兼容反代场景" | OK | — |
| `OrderDrawingNumbers.razor:130` UI "主键 ID" | 看个人习惯 | — |
| `Model.DrawingNames.cs:45` 注释 "DB 列名沿用旧库 Isdelete" | OK(已解释) | — |
| `DrawingNumberViewModel.cs:3` 没有 XML 注释(全是赤裸属性) | 改进点 | 类/字段加一行 `<summary>`|

`DrawingNumberViewModel` 全字段无任何注释 —— 这是 UI 用的传输对象,影响不大,但同仓 `DrawingTypeOptions.cs` 等都加了注释,风格不统一。

---

## 已检查确认无问题

✅ **类继承/接口**:每个 Service 都有注释解释角色边界
✅ **命名空间**:`BootstrapBlazor.OnlyServer1.{Models,Services,Helpers,...}` 分层清晰
✅ **SQL 关键字 / 类型**:`ISqlSugarClient`、`CancellationToken`、`IServiceCollection` 全统一
✅ **泛型后缀**:`PagedResult<T>`、`PagedQuery`、`SelectItem<TKey>` 一致
✅ **`Async` 后缀**:所有 IO 方法都带 `Async` 后缀 ✓

---

## 修复优先级建议

### 🔴 立即(可批量一起改,无破坏)

| # | 项 | 改动 |
|---|---|---|
| 1 | L-1 | `SortOrder.cs` → `SortDirection.cs`(如果方案 A) |

### 🟠 本月(影响范围小,改一处带全局搜索)

| # | 项 | 改动 |
|---|---|---|
| 2 | L-2 | 统一 `UserRolesRow` 到 `Models/Identity/`,删除重复 |
| 3 | L-3 | `OrdersService.GetByIdAsync` → `OrdersService.FindByIdAsync`(实际已返 null) |
| 4 | L-4 | `OrdersService.SearchAsync` → `OrdersService.QueryAsync` |

### 🟡 本季度(需要求稳 + 测试回归)

| # | 项 | 改动 |
|---|---|---|
| 5 | L-5、L-6 | `GetActive` → `GetAllActive`;`GetAllDrawingNamesIncludingDeletedAsync` 改为带 `bool` 参数 |
| 6 | L-7 | `DrawName`/`DrawNumber` → `DrawingName`/`DrawingNumber`(含 DB ColumnName 别名保兼容) |
| 7 | L-9、L-10 | VM/DTO 集中到 `Models/ViewModels/` 或 `Models/` |
| 8 | L-12 | DB 列 `Isdelete` 重命名为 `IsDeleted` |

### 🟢 长期(量大、不阻塞)

| # | 项 | 改动 |
|---|---|---|
| 9 | L-8 | `GID` → `Gid` 全栈改名(若不接外部系统,可重命名) |
| 10 | L-11、L-12 | 注释 / 数据库大写规则文档化 |

---

## 给下次重构的提示

- 优先合并 `UserRolesRow` 重复类 —— 一改即破两层,且会暴露 SqlSugar mapping 漏洞
- `DrawingNumber` vs `DrawNumber` 命名分歧是**长期技术债**,改之前先列 DB 兼容性方案
- Razor 命名误导(`LogoutPage`、`DrawingNames` 复数单数不一致)是低风险但累积技术债
- `DrawingNumberViewModel` 全无注释可一并补上,顺便验证其字段类型
- 中期建议加 `[EditorConfig](.editorconfig)` 锁死 .NET naming convention(CA1707、CA1709、CA1716 等)
