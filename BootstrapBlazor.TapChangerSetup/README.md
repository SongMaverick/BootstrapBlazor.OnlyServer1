# 部署手册 · 分接开关(Tap-Changer)字典

> 适用版本:2026-09-01 新增功能,2026-09-01 扩展型号表
> 安装程序:`BootstrapBlazor.TapChangerSetup`
> 页面路由:
> - `/tap-changer-manufacturers` —— 分接开关厂家字典
> - `/tap-changer-models`        —— 分接开关型号字典

## ⚡ 核心两条命令

跑测试库装表只需要这两条(装前看一眼,装,不用 --production 也不要 YES 确认):

```bash
dotnet run --project BootstrapBlazor.TapChangerSetup -- --check
dotnet run --project BootstrapBlazor.TapChangerSetup -- --install
```

`cd` 到 sln 根目录 `D:\D7_codeBase\BootstrapBlazor.OnlyServer1\`(这条 README 的所有命令都在这个目录)再跑。

跑前无需停机,无需 `appsettings.json` 切换(默认 `sqlServerConnectionTest` = 生产测试库)。

**期望输出**(`--check` 在全新环境):
```
  表 TapChangerManufacturers: 不存在
  表 TapChangerModels: 不存在
  权限码: 0/8 已存在
```

**期望输出**(`--check` 在装完):
```
  表 TapChangerManufacturers: 存在
    行数: 0
    索引 IX_TapChangerManufacturers_NormalizedName: 存在
  表 TapChangerModels: 存在
    行数: 0
    索引 IX_TapChangerModels_Name: 存在
  权限码: 8/8 已存在
    ADMIN: 已绑定 8/8
    OPERATOR: 已绑定 2/2
    VIEWER: 已绑定 2/2
```

打开 `/tap-changer-manufacturers` 和 `/tap-changer-models` 就能用了。**生产环境部署请看下面完整手册**。

---

## 背景

分接开关(Tap-Changer)是有载调压变压器里的独立组件,涉及厂家字典 + 型号字典两张表,后续会扩规格/选型等表。

为什么单独做一个安装项目(脱离 `BootstrapBlazor.OrderManagementSetup`):
- 订单管理 和 分接开关 是两个**独立业务域**,没有任何表/字段相互引用
- 两个安装项目独立安装、独立回滚,互不影响

## 范围

**DB 改动(全部为新增,不删除、不修改任何已有对象)**

| 对象 | 说明 |
|---|---|
| `TapChangerManufacturers` 表 | 9 列(Id + Name + NormalizedName + IsActive + 5 审计字段) |
| `IX_TapChangerManufacturers_NormalizedName` | 过滤唯一索引 `WHERE IsDeleted = 0`(大小写无关) |
| `TapChangerModels` 表 | 19 列(Id + ManufacturerId + Name + 11 分量字段 + IsActive + 5 审计字段) |
| `IX_TapChangerModels_Name` | 过滤唯一索引 `WHERE IsDeleted = 0`(大小写敏感,因型号含 Roman 数字) |
| `Permissions` 8 行 | `TapChangerManufacturer.Create/Read/Edit/Delete` + `TapChangerModel.Create/Read/Edit/Delete` |
| `RolePermissions` 12 行 | ADMIN 8 + OPERATOR 2 + VIEWER 2 |

**代码改动**

| 文件 | 类型 |
|---|---|
| `BootstrapBlazor.OnlyServer1/Models/TapChanger/TapChangerManufacturer.cs` | 新增 |
| `BootstrapBlazor.OnlyServer1/Models/TapChanger/TapChangerModel.cs` | 新增 |
| `BootstrapBlazor.OnlyServer1/Services/TapChanger/TapChangerManufacturerService.cs` | 新增 |
| `BootstrapBlazor.OnlyServer1/Services/TapChanger/TapChangerModelService.cs` | 新增 |
| `BootstrapBlazor.OnlyServer1/Components/Pages/TapChanger/TapChangerManufacturers.razor` | 新增 |
| `BootstrapBlazor.OnlyServer1/Components/Pages/TapChanger/TapChangerModels.razor` | 新增 |
| `BootstrapBlazor.TapChangerSetup/` | 新增项目(本目录) |
| `BootstrapBlazor.OnlyServer1/Models/Identity/PermissionCodes.cs` | 追加 8 个常量 |
| `BootstrapBlazor.OnlyServer1/Program.cs` | 追加 2 行 DI 注册 + 2 行 using |
| `BootstrapBlazor.OnlyServer1/Components/_Imports.razor` | 追加 2 行 using |

**`BootstrapBlazor.DbMigrator` 和 `BootstrapBlazor.OrderManagementSetup` 一个字节都没改**(可以独立部署)。

---

## 前置条件

1. **数据库备份**(虽然只做新增,仍建议)
2. 新代码已编译:`dotnet build BootstrapBlazor.OnlyServer1.sln` 通过
3. **确认安装程序连的是哪个库**(见下,这是最容易出错的一步)

### ⚠ 连接串是硬编码在代码里的

`BootstrapBlazor.TapChangerSetup/Program.cs` 中段:

```csharp
//生产环境
//var connStr = config.GetConnectionString("sqlServerConnection");
//生产测试环境
var connStr = config.GetConnectionString("sqlServerConnectionTest");
//测试环境
//var connStr = config.GetConnectionString("sqlServerConnectionStringOffice");
```

**当前是生产测试库。** 三个值(生产/测试/办公)在 `appsettings.json` 里:
- `sqlServerConnection`           —— 生产库 `stcl_md`
- `sqlServerConnectionTest`      —— 生产测试库 `stcl_md_test` (默认)
- `sqlServerConnectionStringOffice` —— 办公本机测试库

**上生产前必须把注释切回 `sqlServerConnection` 并重新编译。**

同时确认主程序 `BootstrapBlazor.OnlyServer1/Data/SqlSugarSetup.cs` 指向**同一个库**。

## 部署流程

```bat
REM === 步骤 1 · DBA 备份 ===
sqlcmd -S JINWAD5A.AD101.SIEMENS-ENERGY.NET -d stcl_md -Q "BACKUP DATABASE [stcl_md] TO DISK = 'D:\Backup\pre_tapchanger_<YYYYMMDD_HHMMSS>.bak' WITH INIT"

REM === 步骤 2 · 切换连接串到生产并编译 ===
REM 编辑 BootstrapBlazor.TapChangerSetup/Program.cs,把 connStr 切回 sqlServerConnection
cd /d D:\D7_codeBase\BootstrapBlazor.OnlyServer1
dotnet build BootstrapBlazor.OnlyServer1.sln

REM === 步骤 3 · 装前只读检查 ===
dotnet run --project BootstrapBlazor.TapChangerSetup -- --check
REM 确认输出里的连接串是生产库

REM === 步骤 4 · 执行安装(要求输入 YES) ===
dotnet run --project BootstrapBlazor.TapChangerSetup -- --install --production

REM === 步骤 5 · 装后复查 ===
dotnet run --project BootstrapBlazor.TapChangerSetup -- --check
REM 任何一项对不上,停止
```

安装程序退出码:`0` 成功 / `1` 参数或配置错误 / `2` 数据库连接失败 / `3` 执行失败。

## SQL 侧核对(步骤 5 之后)

```sql
-- TapChangerManufacturers 表结构:9 列
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TapChangerManufacturers'
ORDER BY ORDINAL_POSITION;

-- TapChangerManufacturers 过滤唯一索引:必须有 filter_definition
SELECT name, is_unique, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('TapChangerManufacturers')
  AND name = 'IX_TapChangerManufacturers_NormalizedName';
-- 期望 is_unique = 1, filter_definition = ([IsDeleted]=(0))

-- TapChangerModels 表结构:19 列
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TapChangerModels'
ORDER BY ORDINAL_POSITION;

-- TapChangerModels 外键:ManufacturerId → TapChangerManufacturers.Id
SELECT
    fk.name                AS FK_name,
    OBJECT_NAME(fk.parent_object_id)  AS TableName,
    OBJECT_NAME(fk.referenced_object_id) AS RefTableName
FROM sys.foreign_keys fk
WHERE fk.parent_object_id = OBJECT_ID('TapChangerModels');

-- TapChangerModels 过滤唯一索引:必须有 filter_definition
SELECT name, is_unique, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('TapChangerModels')
  AND name = 'IX_TapChangerModels_Name';
-- 期望 is_unique = 1, filter_definition = ([IsDeleted]=(0))

-- 权限绑定:期望 12 行(8 ADMIN + 2 OPERATOR + 2 VIEWER)
SELECT r.Name AS 角色, p.Code
FROM RolePermissions rp
JOIN Roles r ON r.Id = rp.RoleId
JOIN Permissions p ON p.Id = rp.PermissionId
WHERE p.Code LIKE 'TapChangerManufacturer%' OR p.Code LIKE 'TapChangerModel%'
ORDER BY r.Name, p.Code;
```

## ⚠ 烟测前必读:权限 Claim 有 5 分钟缓存

权限码写进 DB 后,**已登录用户的 cookie 里没有新 Claim**,页面按钮不会出现。`SecurityStampValidator` 每 5 分钟才续期一次(`Program.cs`)。

> 烟测前务必**退出登录 → 清 cookie → 重新登录**。
> 否则会误判功能失效。历史上踩过同样的坑,见 code review 记录中 `orders-page-buttons-missing-prod`。

## 端到端验证清单

用 **Admin** 账号,按顺序执行。

### A. 厂家字典(`/tap-changer-manufacturers`)

| # | 操作 | 期望结果 | 验证点 |
|---|---|---|---|
| 1 | 打开 `/tap-changer-manufacturers` | 进入页面,数据为空 | 页面路由 + Admin 权限 |
| 2 | 新增「常州西电」 | 提示新增成功,列表出现 | Create |
| 3 | 再新增「常州西电」(大小写或空格变体) | 提示「已存在」 | NormalizedName 去重 |
| 4 | 编辑「常州西电」改名为「西门子」 | 提示保存成功 | Edit |
| 5 | 停用「西门子」(IsActive=false) | 列表显示但下拉项无该项 | IsActive 独立于 IsDeleted |
| 6 | 删除「西门子」 | 提示已删除,列表消失 | 软删 |
| 7 | 把「常州西电」也删了,再新建「常州西电」 | 可以再建同名 | WHERE IsDeleted=0 过滤索引生效 |

### B. 型号字典(`/tap-changer-models`)

| # | 操作 | 期望结果 | 验证点 |
|---|---|---|---|
| 1 | 打开 `/tap-changer-models` | 进入页面,数据为空 | 页面路由 + Admin 权限 |
| 2 | 新增型号时未选厂家 | 提示「请选择关联的分接开关厂家」 | 厂家必填 |
| 3 | 选「常州西电」+ 列数 1 + Type DU + 相数 I + 额定电流 3204 + Um 245 + 分接选择器 245 + BD(06/05/0/D),保存 | 提示成功,Name 自动显示「1XDU I 3204-245-245-06 05 0 D」 | Name 自动拼装 |
| 4 | 再新建同名(各分量一致),点保存 | 提示「已存在」 | Name 唯一 |
| 5 | 编辑该型号,改 TypeCode 为 "DU2" | Name 重算为「1XDU2 I 3204-...」 | 编辑也重算 Name |
| 6 | 停用(IsActive=false) | 列表显示但下拉项无该项 | IsActive |
| 7 | 删除 | 提示已删除,列表消失 | 软删 |
| 8 | 在型号编辑表单里点厂家下拉,看到停用的「西门子」排在启用项之后 | 下拉包含全部厂商 | GetAllIncludingInactiveAsync |

### 权限矩阵

| 角色 | 进入页面 | 新增 | 编辑 | 删除 |
|---|---|---|---|---|
| Admin | ✓ | ✓ | ✓ | ✓ |
| Operator | ✓(只读) | ✗ 按钮不显示 | ✗ 按钮不显示 | ✗ 按钮不显示 |
| Viewer | ✓(只读) | ✗ 按钮不显示 | ✗ 按钮不显示 | ✗ 按钮不显示 |

每次换账号都要重新登录(Claim 缓存)。

## 失败回滚

### 场景 A:步骤 3/4 安装失败

安装程序只做新增且幂等,失败时不会留下半成品。修正问题后重跑 `--install` 即可。

### 场景 B:步骤 5 复查不通过

执行下面的 SQL 回滚:

```sql
-- 先删型号表(FK 引用厂家表),再删厂家表
DROP TABLE TapChangerModels;
DROP TABLE TapChangerManufacturers;

DELETE FROM RolePermissions
WHERE PermissionId IN (SELECT Id FROM Permissions
                       WHERE Code LIKE 'TapChangerManufacturer%'
                          OR Code LIKE 'TapChangerModel%');
DELETE FROM Permissions
WHERE Code LIKE 'TapChangerManufacturer%'
   OR Code LIKE 'TapChangerModel%';
```

## 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 连接串切错库,装到测试库 | **高** | 步骤 3 `--check` 输出里核对连接串;步骤 5 复查 |
| 型号表 FK 引用厂家表,删厂家会失败 | 中 | DB 默认 NO ACTION,删厂家前先清型号;Service 也只允许软删,不触发 |
| 权限 Claim 缓存导致误判 | 中 | 烟测前强制重新登录 |
| Name 自动拼装格式不符业务预期 | 低 | 改 `TapChangerModelService.BuildName` 即可,后续可考虑自动 + 手填切换 |

## 关键文件参考

| 关注点 | 文件 |
|---|---|
| 安装程序 | `BootstrapBlazor.TapChangerSetup/Program.cs` |
| 厂家实体 | `BootstrapBlazor.OnlyServer1/Models/TapChanger/TapChangerManufacturer.cs` |
| 厂家 Service | `BootstrapBlazor.OnlyServer1/Services/TapChanger/TapChangerManufacturerService.cs` |
| 厂家页面 | `BootstrapBlazor.OnlyServer1/Components/Pages/TapChanger/TapChangerManufacturers.razor` |
| 型号实体 | `BootstrapBlazor.OnlyServer1/Models/TapChanger/TapChangerModel.cs` |
| 型号 Service(含 BuildName) | `BootstrapBlazor.OnlyServer1/Services/TapChanger/TapChangerModelService.cs` |
| 型号页面 | `BootstrapBlazor.OnlyServer1/Components/Pages/TapChanger/TapChangerModels.razor` |
| 权限码常量 | `BootstrapBlazor.OnlyServer1/Models/Identity/PermissionCodes.cs` |

## 待验证事项(截至 2026-09-01)

本手册的步骤基于代码走读编写。已实际验证的只有:
- ✅ `dotnet build` 整个解决方案通过
- ✅ 安装程序 `--check` 可执行到 DB 查询(可能因网络仅在办公网段跑通)

**尚未验证**:`--install` 从未在生产/测试库执行过,两个页面从未在浏览器中打开过。首次执行请在测试库完整走一遍本手册,把与预期不符的地方补充回来。
