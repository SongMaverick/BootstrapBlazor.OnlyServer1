# 业务术语表

本表统一项目内的核心业务概念命名,**写代码前先查这里**。同一概念在不同上下文有不同写法,但**数据库列名 / 类名 / UI 文本三者必须一致**。

## 核心概念

| 中文 | C# 字段 / 类 | 数据库列 | UI 显示 | 含义 |
|------|-------------|---------|--------|------|
| 订单 | `Orders` (类) | `Orders` (表) | 订单列表 | 一台变压器/电抗器的生产任务 |
| 订单号 | `Orders.OrderNumber` (int) | `OrderNumber` | 订单号 | 业务唯一编号(如 12345) |
| 容量 | `Orders.Power` (int, MW) | `Power` | 容量 | 决定图号归到哪张 C0X 表 |
| 图号 | `C0XBase.DrawNumber` (int) | `DrawNumber` | 图号 | 一张图纸的业务编号(如 17001) |
| 图号(显示) | `DrawingNumberViewModel.DrawingTypeNumber` (string) | — | 图号 | "C04 17 001" 这种带前缀的显示 |
| 图号类型 | `C0XBase` 子类 / `DrawingType` 常量 | 表名 (C01..C09) | — | C01~C09,根据 Power 区间归类 |
| 图名 | `DrawingNames` (类) | `DrawingNames` (表) | 图名 | 一类图纸的模板描述(如"油箱") |
| 图纸名称 | `DrawingNames.ComponentName` | `ComponentName` | 图纸名称 | 同"图名 Component",具体名称 |
| 图名类别 | `DrawingNames.Part` | `Part` | 图名类别 / 所属部分 | 图名分组(ESL-01 / ESL-02 等) |
| 部门 | `Departments` (类) | `Departments` | 部门 | 员工所属组织单位 |
| 员工 | `Users` (类) | `Users` | 人员 / 员工 | 系统用户 |
| 员工号 | `Users.WorkerNumber` (int) | `WorkerNumber` | 员工号 | 也是 `UserName`,Identity 登录凭证 |
| 角色 | `Roles` (类) | `Roles` | 角色 | Admin/Operator/Viewer |
| 权限码 | `Permissions` (类) | `Permissions` | 权限 | 细粒度操作点(如 `Order.Create`) |
| 用户-角色 | `UserRoles` (表) | `UserRoles` | — | 多对多关联 |
| 角色-权限 | `RolePermissions` (类) | `RolePermissions` | — | 多对多关联 |

## 容易混淆的"近义词"

### "图号" 的不同含义

| 上下文 | 具体指 |
|--------|-------|
| **业务图号** = `DrawNumber` (int) | 如 17001,数据库列,UNIQUE |
| **显示图号** = `DrawingTypeNumber` (string) | "C04 17 001",UI 展示用 |
| **图号类型** = `DrawingType` (string) | "C04",决定图号归到哪张表 |
| **图号前缀** = `DrawingType` 前 2 字符 | DrawingTypeNumber 拼接时用 |

**典型错误**: 在 `Orders.razor` 列表里显示 `OrderNumber` 时,不要写成"图号:12345"。`OrderNumber` 是订单号,不是图号。

### "图名" vs "图纸名称"

| 名词 | 数据库列 | 含义 |
|------|---------|------|
| 图名(集合) | `DrawingNames` 表 | 一组模板 |
| 图纸名称(单条) | `ComponentName` | "油箱"、"箱盖"这种具体名 |
| 图名类别 | `Part` | ESL-01 / ESL-02 这种分组 |

UI 文本统一:
- 列标题写"图名"(指 ComponentName 单条)
- 类别写"图名类别"或"所属部分"
- 不写"图纸名"(歧义,无法判断是 ComponentName 还是 DrawName)

### "图名" vs "图号"

```
图名(DrawingName) = "油箱"           ← 描述图纸"是什么"
图号(DrawingNumber) = "C04 17 001"  ← 标识"是哪一张"
```

一对多关系:
- 1 个图名(如"油箱")对应多张图纸(不同订单下的"油箱"图)
- 1 张图号 = 1 张图纸 = 属于 1 个订单

## 角色名固定常量

```csharp
// Models/Identity/RoleNames.cs
public const string Admin    = "Admin";    // 系统管理员
public const string Operator = "Operator"; // 业务操作员
public const string Viewer   = "Viewer";   // 只读用户
```

**改角色名只改这里一处**,所有 [Authorize(Roles=...)]、菜单过滤、RolesService 全部从此读取。数据库 `Roles.Name` 由 DbMigrator 灌种时用此值。

## 权限码命名约定

格式:`<模块>.<动作>`

| 前缀 | 模块 |
|------|------|
| `User.` | 人员 |
| `Department.` | 部门 |
| `Role.` | 角色权限 |
| `Order.` | 订单 |
| `DrawingNumber.` | 图号 |
| `DrawingName.` | 图名 |

具体值见 `Models/Identity/PermissionCodes.cs`。改值只改这里,数据库 `Permissions.Code` 由 DbMigrator 灌种。