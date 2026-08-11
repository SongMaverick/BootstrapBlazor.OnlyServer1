# 部署手册 · 订单管理(结构设计订单明细)

> 适用版本:2026-08-10 新增功能
> 安装程序:`BootstrapBlazor.OrderManagementSetup`
> 页面路由:`/order-management`

## 背景

用 `2026 订单管理.xlsx` 的「2026结构设计订单」sheet 手工维护的排产信息(客户、国内/出口、台数、一次/二次项目负责人、四个图纸日期、设计工具、分体大架子、试验套管、开关数量、焊接箱沿)搬进系统。

现有 `Orders` 表只有 4 个业务字段,且 `/orders` 页与图号分配深度耦合,所以**不改动 Orders 表和 /orders 页**,新建一对一扩展表 `OrderDetails` + 独立页面 `/order-management`。

本期不做:「问题汇总」「详细设计完成状态」两列;Excel 那 200 行历史数据也不导入,上线后手工录。

## 范围

**DB 改动(全部为新增,不删除、不修改任何已有对象)**

| 对象 | 说明 |
|---|---|
| `OrderDetails` 表 | 21 列,`OrderId` 关联 `Orders.Id` |
| `IX_OrderDetails_OrderId` | 过滤唯一索引 `WHERE IsDeleted = 0` |
| `Permissions` 3 行 | `OrderDetail.Read` / `.Edit` / `.Delete` |
| `RolePermissions` 7 行 | ADMIN 3 + OPERATOR 3 + VIEWER 1 |

**代码改动**

| 文件 | 类型 |
|---|---|
| `Models/OrderDetails.cs` | 新增 |
| `Models/ViewModels/OrderDetailRow.cs` | 新增 |
| `Services/OrderDetailsService.cs` | 新增 |
| `Components/Pages/OrderManagement.razor` | 新增 |
| `BootstrapBlazor.OrderManagementSetup/` | 新增项目 |
| `Models/Identity/PermissionCodes.cs` | 追加 3 个常量 |
| `Services/UsersService.cs` | 追加 `GetSelectItemsAsync()` |
| `Program.cs` | 追加 1 行 DI 注册 |
| `Components/Shared/MainLayout.razor.cs` | 追加 1 条菜单 |
| **`Services/OrdersService.cs`** | **改动了已上线的下单主流程,见下** |

**`BootstrapBlazor.DbMigrator` 一个字节都没改**(`git diff` 为空)。旧的建表/数据迁移程序完全不参与本次部署。

### OrdersService.SaveAsync 的改动(唯一碰到既有主流程的地方)

只有两处:

1. `order.Id = await _db.Insertable(order).ExecuteReturnIdentityAsync();` —— 接住原本被丢弃的自增 Id
2. 事务提交后追加 `if (changedType == Add) await TryCreateEmptyOrderDetailAsync(...)` —— 新建订单时自动建一条空明细行

`TryCreateEmptyOrderDetailAsync` 是 best-effort:**放在事务之外 + 捕获所有异常只记日志**。所以即使 `OrderDetails` 表不存在,下单也不会失败。Update 分支、权限校验、事务结构、唯一索引冲突处理全部原样未动。

## 部署顺序:无需停机

与 L-7 / L-8 不同,本次**不需要维护窗口**。因为两个方向都是安全的:

| 顺序 | 结果 |
|---|---|
| **先装 DB,再发 web**(推荐) | 完全正常 |
| 先发 web,再装 DB | 降级但安全:新订单的自动建明细失败(日志记 Error),用户首次编辑明细时由兜底逻辑补建,不丢数据 |

## 前置条件

1. **数据库备份**(虽然只做新增,仍建议)
2. 新代码已编译:`dotnet build BootstrapBlazor.OnlyServer1.sln` 通过
3. **确认安装程序连的是哪个库**(见下,这是最容易出错的一步)

### ⚠ 连接串是硬编码在代码里的

`BootstrapBlazor.OrderManagementSetup/Program.cs` 约第 86-89 行:

```csharp
//生产环境
//var connStr = config.GetConnectionString("sqlServerConnection");
//测试环境
var connStr = config.GetConnectionString("sqlServerConnectionStringOffice");
```

**当前是测试环境(Office)。上生产前必须把注释切回来并重新编译。**

同时确认主程序 `BootstrapBlazor.OnlyServer1/Data/SqlSugarSetup.cs` 指向**同一个库**。两边不一致会出现「表明明建了,程序却报表不存在」。

## 执行环境

**所有步骤都可以在 cmd 中执行。** 本手册只用到了 `dotnet` 和 `sqlcmd`,两者在 cmd / PowerShell / bash 下都能跑,选你顺手的就行。

| 步骤 | 权限要求 |
|---|---|
| 1 备份 | 需要 DB 备份权限 |
| 2 编译 | 普通用户 |
| 3-5 安装程序 | 普通用户 + DB 写权限 |
| SQL 核对 | 普通查询权限 |

## 部署流程

```bat
REM === 步骤 1 · DBA 备份 ===
sqlcmd -S JINWAD5A.AD101.SIEMENS-ENERGY.NET -d stcl_md -Q "BACKUP DATABASE [stcl_md] TO DISK = 'D:\Backup\pre_ordermgmt_<YYYYMMDD_HHMMSS>.bak' WITH INIT"

REM === 步骤 2 · 切换连接串到生产并编译 ===
REM 编辑 BootstrapBlazor.OrderManagementSetup/Program.cs,把 connStr 切回 sqlServerConnection
cd /d D:\D7_codeBase\BootstrapBlazor.OnlyServer1
dotnet build BootstrapBlazor.OnlyServer1.sln

REM === 步骤 3 · 装前只读检查 ===
dotnet run --project BootstrapBlazor.OrderManagementSetup -- --check
REM 确认输出里的连接串是生产库
REM 全新环境期望:
REM   表 OrderDetails: 不存在
REM   权限码: 0/3 已存在

REM === 步骤 4 · 执行安装(要求输入 YES) ===
dotnet run --project BootstrapBlazor.OrderManagementSetup -- --install --production

REM === 步骤 5 · 装后复查 ===
dotnet run --project BootstrapBlazor.OrderManagementSetup -- --check
REM 期望输出:
REM   表 OrderDetails: 存在
REM     行数: 0
REM     索引 IX_OrderDetails_OrderId: 存在
REM   权限码: 3/3 已存在
REM     ADMIN: 已绑定 3/3
REM     OPERATOR: 已绑定 3/3
REM     VIEWER: 已绑定 1/1
REM 任何一项对不上,停止
```

安装程序退出码:`0` 成功 / `1` 参数或配置错误 / `2` 数据库连接失败 / `3` 执行失败。

## SQL 侧核对(步骤 5 之后)

```sql
-- 表结构:期望 21 列,4 个日期列 IS_NULLABLE = YES
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OrderDetails'
ORDER BY ORDINAL_POSITION;

-- 过滤唯一索引:必须有 filter_definition,否则清空后无法重填
SELECT name, is_unique, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('OrderDetails') AND name = 'IX_OrderDetails_OrderId';
-- 期望 is_unique = 1, filter_definition = ([IsDeleted]=(0))

-- 权限绑定:期望 7 行
SELECT r.Name AS 角色, p.Code
FROM RolePermissions rp
JOIN Roles r ON r.Id = rp.RoleId
JOIN Permissions p ON p.Id = rp.PermissionId
WHERE p.Code LIKE 'OrderDetail%'
ORDER BY r.Name, p.Code;
```

## ⚠ 烟测前必读:权限 Claim 有 5 分钟缓存

权限码写进 DB 后,**已登录用户的 cookie 里没有新 Claim**,页面按钮不会出现。`SecurityStampValidator` 每 5 分钟才续期一次(`Program.cs`)。

> 烟测前务必**退出登录 → 清 cookie → 重新登录**。
> 否则会误判功能失效。历史上踩过同样的坑,见 code review 记录中 `orders-page-buttons-missing-prod`。

## 端到端验证清单

用 **Admin** 账号,按顺序执行:

| # | 操作 | 期望结果 | 验证点 |
|---|---|---|---|
| 1 | 看侧边栏 | 出现「订单管理」 | 菜单注册 |
| 2 | 打开 `/order-management` | **存量订单全部列出**,明细列空白 | LEFT JOIN(最关键) |
| 3 | 选一个存量订单点「编辑」 | 弹窗打开,明细字段为空 | — |
| 4 | 填客户 + 两个负责人 + 4 个日期,保存 | 提示保存成功,列表显示出来 | 兜底 INSERT 分支 |
| 5 | 再编辑同一行改客户,保存 | 生效 | UPDATE 分支 |
| 6 | 点「清空内容」 | 字段清空,**行仍在列表里** | `ClearAsync` |
| 7 | 去 `/orders` 新建一个测试订单 | 下单成功 | **回归:主流程没坏** |
| 8 | 回 `/order-management` | 新订单出现 | 自动建明细 |

第 8 步用 SQL 确认自动建真的生效(而不是靠 LEFT JOIN 显示的空行):

```sql
SELECT o.OrderNumber, d.Id AS DetailId, d.CreatedBy, d.CreatedTime
FROM Orders o
LEFT JOIN OrderDetails d ON o.Id = d.OrderId AND d.IsDeleted = 0
WHERE o.OrderNumber = <刚建的订单号>;
```

`DetailId` 有值 = 自动建成功;`NULL` = 自动建失败(不影响使用),去 `Services/logs/` 搜 `TryCreateEmptyOrderDetailAsync failed`。

### 日期控件专项(重点)

4 个 `DateTime?` 字段是**本项目首例**,之前没有 `DateTimePicker` 使用先例,最可能出问题:

- 能否弹出日历、选中日期
- **能否清空**(选了之后改回空值)
- 列表显示 `yyyy-MM-dd`,未填显示 `-`
- 重开编辑弹窗,已选日期能否正确回显

### 校验规则

| 输入 | 期望提示 |
|---|---|
| 台数填 0 | 台数必须大于 0 |
| 开关数量填 -1 | 开关数量不能为负数 |
| 一次确认日期早于提交日期 | 一次图纸确认日期不能早于提交日期 |
| 二次确认日期早于提交日期 | 二次图纸确认日期不能早于提交日期 |

### 权限矩阵

| 角色 | 进入页面 | 编辑 | 清空内容 |
|---|---|---|---|
| Admin | ✓ | ✓ | ✓ |
| Operator | ✓ | ✓ | ✓ |
| Viewer | ✓ | ✗ 按钮不显示 | ✗ 按钮不显示 |

每次换账号都要重新登录(Claim 缓存)。

### 回归测试(不可省略)

因为改了 `OrdersService.SaveAsync`:

- [ ] `/orders` 新建订单 → 成功
- [ ] `/orders` 编辑订单 → 成功
- [ ] 重复订单号 → 仍提示「订单号已存在」
- [ ] 给订单分配图号 → 正常
- [ ] 已有图号的订单,编辑按钮仍禁用

## 失败回滚

### 场景 A:步骤 3/4 安装失败

安装程序只做新增且幂等,失败时不会留下半成品。修正问题后重跑 `--install` 即可。

### 场景 B:步骤 5 复查不通过

执行下面的 SQL 回滚,恢复到安装前状态:

```sql
DROP TABLE OrderDetails;   -- 索引随表一起删除
DELETE FROM RolePermissions
WHERE PermissionId IN (SELECT Id FROM Permissions WHERE Code LIKE 'OrderDetail%');
DELETE FROM Permissions WHERE Code LIKE 'OrderDetail%';
```

## 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 连接串切错库,装到测试库 | **高** | 步骤 3 `--check` 输出里核对连接串;步骤 5 复查 |
| 改动了已上线的下单流程 | 中 | 新代码在事务外 + 全异常捕获;回归清单第 7 项必测 |
| `DateTimePicker` 首次使用,行为未验证 | 中 | 日期控件专项测试;失败不影响其他字段 |
| 权限 Claim 缓存导致误判 | 中 | 烟测前强制重新登录 |
| 存量订单没有明细行 | 低 | 按设计如此(用户决定不 backfill),靠 LEFT JOIN + 兜底 INSERT |

## 关键文件参考

| 关注点 | 文件 |
|---|---|
| 安装程序 | `BootstrapBlazor.OrderManagementSetup/Program.cs` |
| 实体定义 | `BootstrapBlazor.OnlyServer1/Models/OrderDetails.cs` |
| 查询/保存/清空逻辑 | `BootstrapBlazor.OnlyServer1/Services/OrderDetailsService.cs` |
| 自动建明细 | `BootstrapBlazor.OnlyServer1/Services/OrdersService.cs` → `TryCreateEmptyOrderDetailAsync` |
| 页面 | `BootstrapBlazor.OnlyServer1/Components/Pages/OrderManagement.razor` |
| 权限码常量 | `BootstrapBlazor.OnlyServer1/Models/Identity/PermissionCodes.cs` |

## 待验证事项(截至 2026-08-10)

本手册的步骤基于代码走读编写。已实际验证的只有:

- ✅ `dotnet build` 整个解决方案通过
- ✅ 现有 120 个单元测试全部通过,无回归
- ✅ 安装程序 `--check` 对生产库执行成功(只读,零写入)

**尚未验证**:`--install` 从未执行过,页面从未在浏览器中打开过。首次执行请在测试库完整走一遍本手册,把与预期不符的地方补充回来。
