# BootstrapBlazor.DbMigrator

数据库迁移工具 — 用 SqlSugar Code First 建表 + 从 SQL 文件迁移旧数据 + 灌入 RBAC 权限码种子。

## 完整命令速查(收藏夹)

> ⚠ **shell 差异**:
> - **PowerShell**(提示符 `PS C:\>`):用 `` ` `` 换行
> - **CMD**(提示符 `C:\>`):用 `^` 换行,**或整条命令写一行**(推荐,零歧义)
> - **bash / zsh**:用 `\` 换行
> - 一行命令在三种 shell 里都能跑

### 命令 1 — 测试环境完整重置(最常用,带删表)

```bash
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.DbMigrator --drop --schema --seed --migrate --source "D:\D7_codeBase\BootstrapBlazor.OnlyServer1"
```

执行顺序:
1. **`--drop`** 删除所有业务表(22+ 张,含新增的 `Permissions` / `RolePermissions`)
2. **`--schema`** 重建所有表 + 演进列 + 建索引
3. **`--seed`** 灌入种子(详见下文,4 个子步骤)
4. **`--migrate`** 从旧 SQL 文件读历史数据并迁移

### 命令 2 — 已建库的生产首次部署(不删表,要确认)

```bash
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.DbMigrator --schema --seed --migrate --source "D:\D7_codeBase\BootstrapBlazor.OnlyServer1" --production --admin-password "YourSecurePwd!2026"
```

`--production` 会要求输入 `YES` 二次确认,并写 `MigrationLog` 表。

### 命令 3 — 仅跑数据迁移(表已建好,只想灌数据)

```bash
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.DbMigrator --migrate --source "D:\D7_codeBase\BootstrapBlazor.OnlyServer1"
```

### 命令 4 — 只跑 RBAC 种子(已有库,想补 25 个权限码 + 清理 USER 角色 + ADMIN 自动全勾)

```bash
dotnet run --project D:\D7_codeBase\BootstrapBlazor.OnlyServer1\BootstrapBlazor.DbMigrator --seed
```

> 幂等:Permissions 表非空则跳过;ADMIN 已有权限关联也跳过。

### 命令 5 — 默认(不传任何参数)

等价于 `--schema --seed`(建表 + 灌种子,无删表、无数据迁移)。

---

## `--seed` 阶段详细分解(4 个子步骤)

按顺序执行,任一失败会中断后续:

| # | 子步骤 | 作用 | 幂等? |
|---|---|---|---|
| 1 | `SeedDepartmentsAsync` | 灌入部门种子("MD 部门") | ✅ |
| 2 | `SeedIdentityAsync` | 创建 ADMIN 角色 + WorkerNumber=1 超管(密码 `Admin@123` 或 `--admin-password` 指定) | ✅ |
| 3 | `CleanupLegacyRolesAsync` | 删除 USER 角色及其 `UserRoles` / `RolePermissions` 关联 | ✅ |
| 4 | `SeedPermissionsAsync` | 灌入 25 个操作权限码(见下表) | ✅ |
| 5 | `BindAdminAllPermissionsAsync` | 自动给 ADMIN 角色关联全部 25 个权限码 | ✅ |

> 新装的库:跑完 5 步后 ADMIN 用户登录即可访问全部功能,无需任何额外配置。
> 已有库:4 步会自动跳过已存在的项;5 步只在 ADMIN 当前 0 权限时全量绑定,不会覆盖手工调整。

### 25 个权限码字典

按 Category 分组:

| Category | Code | Name |
|---|---|---|
| 人员管理 | `User.Read` | 查看人员 |
| 人员管理 | `User.Create` | 新增人员 |
| 人员管理 | `User.Edit` | 编辑人员 |
| 人员管理 | `User.Delete` | 删除人员 |
| 人员管理 | `User.ResetPassword` | 重置密码 |
| 部门管理 | `Department.Read` | 查看部门 |
| 部门管理 | `Department.Create` | 新增部门 |
| 部门管理 | `Department.Edit` | 编辑部门 |
| 部门管理 | `Department.Delete` | 删除部门 |
| 角色权限 | `Role.Read` | 查看角色 |
| 角色权限 | `Role.Create` | 新增角色 |
| 角色权限 | `Role.Edit` | 编辑角色 |
| 角色权限 | `Role.Delete` | 删除角色 |
| 角色权限 | `Role.AssignUser` | 分配用户角色 |
| 订单 | `Order.Read` | 查看订单 |
| 订单 | `Order.Create` | 下单 |
| 订单 | `Order.Approve` | 审核订单 |
| 订单 | `Order.Cancel` | 取消订单 |
| 图号 | `DrawingNumber.Read` | 查询图号 |
| 图号 | `DrawingNumber.Allocate` | 分配图号 |
| 图号 | `DrawingNumber.Update` | 修改图号 |
| 图名 | `DrawingName.Read` | 查看图名 |
| 图名 | `DrawingName.Create` | 新增图名 |
| 图名 | `DrawingName.Edit` | 编辑图名 |
| 图名 | `DrawingName.Delete` | 删除图名 |

---

## 命令参数

| 参数 | 说明 |
|------|------|
| `--schema` | 建表(Code First,含 RBAC 新表) |
| `--seed` | 灌入 4 个子步骤种子(部门 / Identity / 清理 USER / 权限码 / ADMIN 全勾) |
| `--migrate` | 从 SQL 文件迁移旧数据 |
| `--source <dir>` | 旧数据 SQL 文件目录(配合 `--migrate`) |
| `--drop` | 先删所有业务表(**不可逆**,生产禁用,需配合 `--schema`) |
| `--admin-password <pwd>` | 自定义默认管理员密码(默认 `Admin@123`) |
| `--production` | 生产模式(需 `YES` 确认 + 写 `MigrationLog` 审计) |

不传任何模式 = `--schema --seed`

---

## 自动创建的表(共 24+ 张)

**Identity(5 张)**:`Users` / `Roles` / `UserRoles` / `UserClaims` / `RoleClaims`

**RBAC 权限(2 张)**:
- `Permissions` — 权限码主表
- `RolePermissions` — 角色-权限多对多关联(复合主键 `RoleId+PermissionId`)

**业务-字典表(1 张)**:`Departments`

**业务表(15+ 张)**:`Orders` / `OrdersItem` / `DrawingNames` / `DrawingNumberSequence` / `Logs` / `C01`–`C09`

**索引**:
- `IX_Users_NormalizedUserName` (UNIQUE) / `IX_Users_WorkerNumber` (UNIQUE)
- `IX_Roles_NormalizedName` (UNIQUE)
- `IX_Departments_NormalizedName` (UNIQUE)
- `IX_Permissions_Code` / `IX_Permissions_Category`
- `IX_C01_DrawNumber` ~ `IX_C09_DrawNumber` (每张 UNIQUE)

**外键**:
- `FK_Users_Departments`:`Users.DepartmentId` → `Departments.Id`(`ON DELETE NO ACTION`)
- `RolePermissions.RoleId` → `Roles.Id` (`ON DELETE CASCADE`)
- `RolePermissions.PermissionId` → `Permissions.Id` (`ON DELETE CASCADE`)

---

## RBAC 权限管理流程

迁移完成后,在 Web 端:

1. **超管登录**(WorkerNumber=1,密码 `Admin@123` 或 `--admin-password` 指定)
2. 侧边栏出现两个新菜单:
   - **角色权限**(`/roles`)— 创建业务角色(如"订单审核员"),给角色勾选权限码
   - **用户角色**(`/user-roles`)— 给员工分配角色(单选 / 多选)
3. 业务角色变更后,**该员工需重新登录**才能看到新的菜单和按钮(Cookie Claims 缓存)

> ADMIN 角色不能改名、不能删除;唯一一名 ADMIN 不能解除关联(防锁死系统)。

---

## 数据迁移规则(不变)

### Users
- `UserName = WorkerNumber.ToString()`
- `NormalizedUserName = UPPER(WorkerNumber)`
- `GID` 转大写
- `DepartmentId` 可空;若旧库 `Users.sql` 没有此列,迁移后会保持 `NULL`,`--seed` 后会自动回填到 `MD 部门`

### Orders + ReactorOrder
- 合并到 `Orders` 表
- `ReactorOrder.IsReactor = true`,普通 `Orders` 为 `false`
- 建立 `reactorIdMap`:旧 ReactorOrder.Id → 新 Orders.Id(用于 C08)

### C01-C09
- 显式保留旧 Id(`SET IDENTITY_INSERT`)
- 跳号位置补占位行(`IsDelete=true`),保证 `Id` 从 1 全覆盖
- C04/C07 有用户指定的占位源(详见代码注释)
- C0X.OrderId 通过 `idMap` 重映射到新 Orders.Id

---

## 编码自动检测

迁移器读取 SQL 文件时按以下顺序检测编码:
1. UTF-8 BOM
2. 合法 UTF-8 字节序列
3. **GB18030**(兼容 GBK / GB2312,中文环境老导出最常用)

解决老数据库导出文件中文乱码问题。

---

## 工作流程

```
┌─────────────────────────────────────────────────────────┐
│ 1. 准备阶段                                             │
│    - 从老数据库导出 SQL 文件(Users/Order/ReactorOrder/ │
│      C01-C09/DrawingNames)到本地目录                     │
│    - 编码不限,工具会自动检测                             │
│    - 修改 appsettings.json 中的连接字符串               │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│ 2. 测试环境(反复演练)                                   │
│    命令 1:完整重置(删表 + 建表 + 种子 + 迁移)           │
│                                                         │
│    验证:                                                │
│    - 5 个子步骤都跑通(--seed 阶段 4 个新步骤)           │
│    - SELECT COUNT(*) FROM Permissions = 25              │
│    - ADMIN 用户登录看到侧边栏 /roles 和 /user-roles     │
│    - 旧数据条数对得上                                   │
│    - C08 指向正确电抗器订单                              │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│ 3. 生产环境                                             │
│    - 备份目标数据库                                     │
│    - 命令 2:--production 模式(会要求输入 YES 确认)      │
│    - 首次部署后立刻登录 Web 端改默认密码                 │
│    - MigrationLog 表自动记录操作者/时间/参数             │
└─────────────────────────────────────────────────────────┘
```

---

## 已知限制

1. SQL 解析器**不支持多行 VALUES**(单条 INSERT 跨行 OK,但每条必须以 `;` 结尾)
2. SQL 解析器**不支持 SELECT INTO / BULK INSERT**
3. **不主动为 `Orders` / `OrdersItem` 等老业务表加 FK**;**仅** `FK_Users_Departments` 这一个新 FK 会被自动加上
4. 迁移日志写到独立的 `MigrationLog` 表
5. 改了角色/权限后,**该用户必须重新登录**才能看到效果(Cookie Claims 缓存)
6. `Permissions` 表的 Code 字段是普通索引(非 UNIQUE) — SQL Server 不支持表达式列 UNIQUE 索引,且 25 个静态值不会冲突,无需 UNIQUE 约束

---

## 注意事项

- **生产部署务必加 `--production`**,会有确认环节
- **生产部署前先备份目标库**,工具对已存在数据不会二次确认
- **老密码全部清空**,迁移后所有人需要重置密码(管理员账号默认 `Admin@123`,其他用户由管理员在 Web 端设置)
- **ID 重映射仅发生在 C08**,C01-C07 不受影响
- **多行命令在 CMD 里用 `^` 换行**(不是 PowerShell 的 `` ` ``),否则参数会丢失
- **不确定用哪个 shell 时,直接写一行命令**(三种 shell 都能跑)
- **改了用户角色后,通知该用户重新登录**才能看到新的菜单/按钮
- **ADMIN 是系统保留角色**:不能改名字、不能删除、唯一 ADMIN 不能解除关联

---

## 故障排查

| 现象 | 可能原因 |
|------|---------|
| 中文乱码 | 工具会自动检测 GB18030,如仍乱码请确认源文件未被 BOM 损坏 |
| "列数与值数不匹配" | SQL 文件含 CREATE TABLE 或 USE 等非 INSERT 语句,正常跳过 |
| "Foreign Key 冲突" | 本工具不加 FK,通常是手动建表时遗留 |
| "表已存在" | 工具自动跳过,如需重建用 `--drop --schema` |
| "找不到 --source" | 检查路径是否支持(支持绝对和相对) |
| **参数没生效(如 `migrate=False`)** | **CMD 多行命令换行符写错**(应该是 `^` 不是 `` ` ``);整条命令写一行解决 |
| 跑完种子 `Permissions` 表为空 | 旧库可能在早期跑过,Permissions 表有遗留空数据但触发不到幂等检查;手动 `DELETE FROM Permissions` 再跑 `--seed` |
| 用户登录后菜单没变 | Cookie Claims 缓存,需重新登录(`Account/Logout` 再 `Account/Login`) |
| 唯一 ADMIN 无法解除关联 | 设计如此,需先创建第二个 ADMIN 角色用户,再解除旧的 |

---

## 默认登录信息

| 字段 | 值 |
|---|---|
| 员工号 | 1 |
| 用户名 | 超级管理员 |
| 默认密码 | `Admin@123`(可用 `--admin-password` 自定义) |
| 角色 | Admin(自动绑定全部 25 个权限码) |
| GID | ADMIN |

> **首次部署完成后,请立即登录改密。**
