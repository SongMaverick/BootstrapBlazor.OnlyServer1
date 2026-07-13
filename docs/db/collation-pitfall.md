# SQL Server Collation 陷阱(中文显示为 `?`)

> **2026-07-09 事故复盘 + 修复指引**
> 修复路径:**drop 表 → DbMigrator 用 SqlSugar CodeFirst 重建(自动 NVARCHAR)→ re-seed → re-migrate**

---

## 现象

- 测试环境:用户姓名正常显示(`张三`、`李四`)
- 生产环境:用户姓名显示为 `?`
- 同源代码、同 `Users.sql` 脚本,行为完全不同

## 根因(30 秒版)

**`varchar` 列按列 collation 编码存储,不同 collation 下"能不能存中文"是完全不同的两件事。**

| DB 默认 collation | varchar 能否存中文 | 测试 / 生产 |
|------------------|-------------------|-----------|
| `Chinese_PRC_CI_AS` | ✅(走 GBK/CP936 编码) | 测试 ✓ |
| `SQL_Latin1_General_CP1_CI_AS` | ❌(codepage 不含中文,字节替换为 `?`) | 生产 ❌ |

DB 默认 collation 跟着 **SQL Server 安装时的 Windows 系统语言** 走。

- 测试机:中文系统 / 中文 SQL Server 安装包 → 默认 `Chinese_PRC_CI_AS`
- 生产机:IT 用标准英文镜像装的 → 默认 `SQL_Latin1_General_CP1_CI_AS`

## 损坏链路(完整版)

DbMigrator 跑 `Users.sql` 时,SQL Server 在 **INSERT 时机** 做隐式转换,规则是:

```
INSERT ... VALUES (..., N'张三', ...)
                    ^^^^^^^^^^^ NVARCHAR 字面量(Unicode,完整保留中文)
    ↓ SQL Server 按目标列 collation 把 NVARCHAR 转换为 VARCHAR
    ↓
    ┌─ 列 collation = Chinese_PRC_CI_AS(测试)
    │   → NVARCHAR → VARCHAR 用 GBK 编码 → 字节落盘 → 读回 → "张三" ✅
    │
    └─ 列 collation = SQL_Latin1_General_CP1_CI_AS(生产)
        → NVARCHAR → VARCHAR 时 codepage 不含中文字符 → 替换为 '?'
        → 字节落盘 → 读回 → "?" ❌
```

**`N'...'` 前缀救不了你**——前缀只保证字面量是 Unicode,转换到 `varchar` 时仍按目标列 collation 编码,所以**列 collation 是最终决定因素**。

## 验证(30 秒出真相)

测试 + 生产分别执行:

```sql
-- 1. DB 默认 collation
SELECT DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS DbCollation;

-- 2. Users 表关键列的实际 collation
SELECT
    c.name AS ColumnName,
    c.collation_name AS ColumnCollation
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Users')
  AND c.name IN ('Name', 'GID', 'Email', 'PhoneNumber', 'UserName', 'NormalizedUserName');
```

预期:

| Column | Test | Prod |
|--------|------|------|
| Name | `Chinese_PRC_CI_AS` | `SQL_Latin1_General_CP1_CI_AS` |
| Email | 同上 | 同上 |

## 修复方案:**drop + DbMigrator 重建**(推荐)

如果生产数据可以丢弃(本项目当前阶段,数据尚未正式上线,可以直接重置),最干净的修复就是 drop 表,让 DbMigrator 重新建——SqlSugar CodeFirst 默认把所有 `string` 列建为 **NVARCHAR**,彻底绕开 collation 陷阱。

### 步骤

**1) 备份(可选,如果生产数据没价值可跳过)**

```sql
BACKUP DATABASE [stcl_md] TO DISK = N'C:\Backup\stcl_md_pre_collation_fix.bak'
WITH INIT, COMPRESSION;
```

**2) 跑 DbMigrator 一把搞定**

```bash
# 在生产服务器上,启动 VS / dotnet CLI
dotnet run --project BootstrapBlazor.DbMigrator -- \
    --drop --schema --seed --migrate --source . --production
```

参数说明:

| 参数 | 作用 |
|------|------|
| `--drop` | 先 `DROP TABLE` 所有业务表(Users/C0X/Orders/...)+ 旧权限表 |
| `--schema` | 用 SqlSugar CodeFirst 重建,所有 `string` 列 → **NVARCHAR**(根因修复点) |
| `--seed` | 灌 Identity 角色 / 默认管理员 / 27 个权限码 / 部门种子 |
| `--migrate --source .` | 从当前目录读 `Users.sql` / `Order.sql` / C0X.sql / `DrawingNames.sql`,用 `SqlInsertParser` 解析 INSERT,经 SqlSugar ORM 写入新表(列是 NVARCHAR,不再有 collation 问题) |
| `--production` | 强制要求输入 `YES` 二次确认 + 写 MigrationLog |

**3) 验证**

```sql
-- 重跑之前的验证 SQL,这次 Users.Name 的 collation 应当 = DB 默认 collation
-- 或列定义已经直接是 NVARCHAR(无 collation 概念也行,看 SQL Server 版本)
SELECT
    c.name AS ColumnName,
    c.collation_name AS ColumnCollation
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Users')
  AND c.name = 'Name';
```

应用端登录 → 姓名正常显示中文。

## 为什么 drop + 重建 > ALTER COLUMN

| 维度 | ALTER COLUMN | drop + 重建(本方案) |
|------|-------------|-------------------|
| 改动范围 | 只动 Users / DrawingNames 两张表 | 全库 schema 重新对齐 |
| 历史数据 | 保留(但已损坏的 `?` 救不回) | 重新从 SQL 文件迁移(数据源可靠) |
| 索引 / FK / 约束 | 手动 DROP / 重建,容易遗漏 | SqlSugar CodeFirst 自动重建 |
| 其他表潜在隐患 | 不动 — 如果其他表也 varchar 同样会出问题 | 一起重置,所有表统一 NVARCHAR |
| 实施时间 | 5-10 分钟(含备份) | 5-10 分钟(同) |
| 风险 | 中(列类型转换可能锁表) | 低(只是删表+重建) |

**只要数据可重建(本项目当前阶段,正式上线前),drop + 重建就是唯一正解。**

## 源文件:Users.sql / DrawingNames.sql 仍然要修

虽然 DbMigrator 的 `--schema` 走 CodeFirst(自动 NVARCHAR),不读 Users.sql 的 CREATE TABLE,但 **`Users.sql` / `DrawingNames.sql` 仍然改 nvarchar**,原因:

1. **作为 schema 文档** — 给接手的人看,知道这表应该长什么样
2. **防止以后有人手动 SSMS 跑这个文件** — 比如 DBA 排查问题时,可能直接 SSMS → 执行 Users.sql,会重蹈覆辙
3. **跟 `Migrations/001_IdentitySchema.sql`(全 NVARCHAR)对齐** — 风格统一,无歧义

修改对照:

| 文件 | 列 | 原 | 新 |
|------|---|---|---|
| Users.sql | Name | varchar(255) | **nvarchar(255)** |
| Users.sql | GID | varchar(255) | **nvarchar(255)** |
| Users.sql | UserName | varchar(256) | **nvarchar(256)** |
| Users.sql | NormalizedUserName | varchar(256) | **nvarchar(256)** |
| Users.sql | Email | varchar(256) | **nvarchar(256)** |
| Users.sql | NormalizedEmail | varchar(256) | **nvarchar(256)** |
| Users.sql | PhoneNumber | varchar(50) | **nvarchar(50)** |
| Users.sql | PasswordHash / SecurityStamp / ConcurrencyStamp | varchar | **保持 varchar**(base64 / GUID) |
| DrawingNames.sql | Part | varchar(255) | **nvarchar(255)** |
| DrawingNames.sql | ComponentName | varchar(255) | **nvarchar(255)** |
| DrawingNames.sql | CreatedBy | varchar(255) | **nvarchar(255)** |
| DrawingNames.sql | UpdatedBy | varchar(255) | **nvarchar(255)** |

文件编码也从 **UTF-16LE 改为 UTF-8 BOM**——`EncodingHelper` 自动检测,Bash/VS 直接可读,不用再像之前那样看着 `N'�s�_R'` 这种乱码猜。

## 如果以后真要保留数据(已上线后)

万一生产数据已经是有价值的(已经跑了一段时间,不能 drop),就只能 ALTER COLUMN + 数据回填:

```sql
-- 单表备份
SELECT * INTO Users_Backup_20260709 FROM Users;

-- 改列(处理索引依赖:先 DROP,改完重建)
DROP INDEX IX_Users_NormalizedUserName ON Users;
ALTER TABLE Users ALTER COLUMN Name NVARCHAR(255) NOT NULL;
ALTER TABLE Users ALTER COLUMN GID NVARCHAR(255) NOT NULL;
ALTER TABLE Users ALTER COLUMN UserName NVARCHAR(256) NOT NULL;
ALTER TABLE Users ALTER COLUMN NormalizedUserName NVARCHAR(256) NOT NULL;
ALTER TABLE Users ALTER COLUMN Email NVARCHAR(256) NULL;
ALTER TABLE Users ALTER COLUMN NormalizedEmail NVARCHAR(256) NULL;
ALTER TABLE Users ALTER COLUMN PhoneNumber NVARCHAR(50) NULL;
CREATE UNIQUE INDEX IX_Users_NormalizedUserName ON Users(NormalizedUserName);

-- ⚠ 已损坏的 '?' 数据无法靠 schema 修复救回,需从外部源(OA / HR)回填
-- UPDATE u SET u.Name = N'新姓名' FROM Users u JOIN <外部源> src ON u.WorkerNumber = src.WorkerNumber;
```

**本项目当前阶段不需要这套——drop + 重建更干净。**

## 验收清单

- [ ] 测试/生产 collation 验证 SQL 跑通,确认 Prod 是 Latin1
- [ ] 已备份生产 DB(可选,数据可丢)
- [ ] 跑 `dotnet run --project DbMigrator -- --drop --schema --seed --migrate --source . --production`
- [ ] 输入 `YES` 二次确认
- [ ] 验证脚本:SSMS 查 Users.Name collation,确认是 NVARCHAR
- [ ] 应用端登录,姓名显示正常
- [ ] 新写入一条用户,确认 NVARCHAR 路径正常

## 防止再犯

- [ ] **永远不要在含中文的列上用 `varchar`**——直接 `nvarchar`,别想着"反正测试能存"
- [ ] **新表 schema 模板用 `Migrations/001_IdentitySchema.sql`**(全 NVARCHAR,已经做对)
- [ ] **部署清单记录目标 DB 的 collation**——如果生产 DB collation 是 `SQL_Latin1_General_CP1_CI_AS`,建库时手动改成 `Chinese_PRC_CI_AS`(虽然 CodeFirst 不依赖,但万一未来手动建表就有备无患)
- [ ] 团队 Code Review 看到 SQL 文件里的 `varchar(` 字段名含中文相关(姓名/地址/描述/...),直接打回

## 延伸阅读

- [Microsoft Docs: Collation and Unicode Support](https://learn.microsoft.com/en-us/sql/relational-databases/collations/collation-and-unicode-support)
- [Stack Overflow: nvarchar vs varchar — the difference](https://stackoverflow.com/q/612430)
- 项目内:`Migrations/001_IdentitySchema.sql`(正确做法模板)
- 项目内:`Users.sql` / `DrawingNames.sql`(已修,本 commit)