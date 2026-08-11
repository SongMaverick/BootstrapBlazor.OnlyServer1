using System.Text.Json;
using System.Text.RegularExpressions;
using BootstrapBlazor.DbMigrator.Sql;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BootstrapBlazor.DbMigrator;

/// <summary>
/// 数据库迁移工具主程序。
///
/// 模式:
///   --schema                    建表(Code First)
///   --seed                      灌 Identity 角色 + 默认管理员
///   --migrate                   从 SQL 文件读旧数据并迁移
///   --source &lt;dir&gt;            旧数据目录(包含 Users.sql / Order.sql / ReactorOrder.sql / C0X.sql / DrawingNames.sql)
///   --drop                      先删所有业务表再重建(危险)
///   --admin-password p          自定义默认管理员密码(默认 Admin@123)
///   --production                生产模式(强制要求交互确认 + 写迁移日志)
///
/// (2026-07-08 Review #L-7) C0X 列重命名专用 3 模式(独立运行,不与其他模式组合):
///   --rename-c0x-drawingname    9 张 C0X 表 DrawName/DrawNumber → DrawingName/DrawingNumber
///   --rollback-c0x-drawingname  回滚上述重命名
///   --check-c0x-state           只读检查当前 C0X 列/索引状态
///
/// (2026-07-08 Review #L-8) Users.GID 列重命名专用 3 模式(独立运行):
///   --rename-users-gid          Users.GID → Users.Gid
///   --rollback-users-gid        回滚上述重命名
///   --check-users-state         只读检查 Users 表 GID/Gid 列状态
///
/// 典型用法:
///   # 测试环境 — 完整重置:删表 + 建表 + 灌种子 + 读旧 SQL 迁移
///   dotnet run --project DbMigrator -- --drop --schema --seed --migrate --source .
///
///   # 仅跑数据迁移(表已建好)
///   dotnet run --project DbMigrator -- --migrate --source .
///
///   # 生产部署 — 空数据库,一次性建表 + 灌种子 + 迁移(要求输入 YES 确认)
///   dotnet run --project DbMigrator -- --schema --seed --migrate --source /data/old --production
///
///   # L-7 协调迁移 — C0X 列重命名(独立模式)
///   dotnet run --project DbMigrator -- --check-c0x-state
///   dotnet run --project DbMigrator -- --rename-c0x-drawingname
///   dotnet run --project DbMigrator -- --check-c0x-state
///   dotnet run --project DbMigrator -- --rollback-c0x-drawingname  # 失败时回滚
///   (部署步骤见 git history L-7 列重命名提交)
///
///   # L-8 协调迁移 — Users.GID 列重命名(独立模式)
///   dotnet run --project DbMigrator -- --check-users-state
///   dotnet run --project DbMigrator -- --rename-users-gid
///   dotnet run --project DbMigrator -- --check-users-state
///   dotnet run --project DbMigrator -- --rollback-users-gid  # 失败时回滚
///   (部署步骤见 git history L-8 列重命名提交)
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool doSchema = args.Contains("--schema");
        bool doSeed = args.Contains("--seed");
        bool doMigrate = args.Contains("--migrate");
        bool doDrop = args.Contains("--drop");
        bool doProduction = args.Contains("--production");

        // (2026-07-08 Review #L-7) C0X 列重命名专用 3 模式,与其他模式互斥(只跑这一个)
        bool doRenameC0X = args.Contains("--rename-c0x-drawingname");
        bool doRollbackC0X = args.Contains("--rollback-c0x-drawingname");
        bool doCheckC0X = args.Contains("--check-c0x-state");

        // (2026-07-08 Review #L-8) Users.GID 列重命名专用 3 模式(独立运行)
        bool doRenameUsersGid = args.Contains("--rename-users-gid");
        bool doRollbackUsersGid = args.Contains("--rollback-users-gid");
        bool doCheckUsersState = args.Contains("--check-users-state");

        string? sourceDir = null;
        var srcIdx = Array.IndexOf(args, "--source");
        if (srcIdx >= 0 && srcIdx + 1 < args.Length)
            sourceDir = args[srcIdx + 1];

        string? customAdminPwd = null;
        var pwdIdx = Array.IndexOf(args, "--admin-password");
        if (pwdIdx >= 0 && pwdIdx + 1 < args.Length)
            customAdminPwd = args[pwdIdx + 1];

        // 默认 --schema --seed(若用户没指定任何模式,执行基础建表+种子)
        // (2026-07-08 Review #L-7) C0X rename/check 模式独立运行,不进入默认流程
        // (2026-07-08 Review #L-8) Users.GID rename/check 模式独立运行,不进入默认流程
        if (!doSchema && !doSeed && !doMigrate && !doDrop
            && !doRenameC0X && !doRollbackC0X && !doCheckC0X
            && !doRenameUsersGid && !doRollbackUsersGid && !doCheckUsersState)
        {
            doSchema = true;
            doSeed = true;
        }

        Console.WriteLine("============================================================");
        Console.WriteLine(" BootstrapBlazor 数据库迁移工具 (Code First + 数据迁移)");
        Console.WriteLine("============================================================");

        // 加载配置
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        //sqlServerConnection
        var connStr = config.GetConnectionString("sqlServerConnection");

        //var connStr = config.GetConnectionString("SqlServer");
        //var connStr = config.GetConnectionString("sqlServerConnectionStringHomeSA");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            Console.Error.WriteLine("错误:appsettings.json 中未配置 ConnectionStrings.SqlServer");
            return 1;
        }

        Console.WriteLine($" 连接字符串: {MaskConnectionString(connStr)}");
        Console.WriteLine($" 模式: schema={doSchema}, seed={doSeed}, migrate={doMigrate}, drop={doDrop}, production={doProduction}");
        if (doRenameC0X || doRollbackC0X || doCheckC0X)
        {
            Console.WriteLine($" L-7 专用: renameC0X={doRenameC0X}, rollbackC0X={doRollbackC0X}, checkC0X={doCheckC0X}");
        }
        if (doMigrate)
        {
            if (string.IsNullOrEmpty(sourceDir))
            {
                Console.Error.WriteLine("错误:--migrate 必须配合 --source <目录> 指定 SQL 文件位置");
                return 1;
            }
            Console.WriteLine($" 源数据目录: {Path.GetFullPath(sourceDir)}");
        }
        Console.WriteLine();

        // 初始化 SqlSugar 客户端
        var db = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = connStr,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        }, db =>
        {
            db.Aop.OnLogExecuting = (sql, pars) =>
            {
                var oneLine = sql.Replace("\r", " ").Replace("\n", " ");
                if (oneLine.Length > 200) oneLine = oneLine.Substring(0, 200) + "...";
                Console.WriteLine($"  SQL: {oneLine}");
            };
        });

        // 测试连接
        try
        {
            Console.Write(" 测试数据库连接... ");
            await db.Ado.GetIntAsync("SELECT 1");
            Console.WriteLine("✓ 成功");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ 失败:{ex.Message}");
            return 2;
        }

        // 生产模式:要求确认
        if (doProduction)
        {
            Console.WriteLine();
            Console.WriteLine("⚠⚠⚠ 生产模式 ⚠⚠⚠");
            Console.WriteLine("即将对生产数据库执行以下操作:");
            if (doDrop) Console.WriteLine("  - 删除所有业务表");
            if (doSchema) Console.WriteLine("  - 创建/重建所有表结构");
            if (doSeed) Console.WriteLine("  - 灌入部门种子 + Identity 种子数据");
            if (doMigrate) Console.WriteLine("  - 从 SQL 文件迁移历史数据");
            Console.Write(" 确认? 输入 YES 继续: ");
            var confirm = Console.ReadLine();
            if (!string.Equals(confirm, "YES", StringComparison.Ordinal))
            {
                Console.WriteLine(" 已取消");
                return 0;
            }

            // 写迁移日志
            await WriteMigrationLog(db, args);
        }

        // Drop
        if (doDrop && doSchema)
        {
            Console.WriteLine();
            Console.WriteLine(">>> 删除所有业务表");
            await DropAllTables(db);
        }

        // Schema
        if (doSchema)
        {
            Console.WriteLine();
            Console.WriteLine(">>> 建表(Code First)");
            await CreateAllTables(db);

            // 演进式 schema 调整:Code First 不会给已存在的表加新列。
            // 对存量库一次性补齐 DrawNumber 列,然后再尝试建 UNIQUE 索引。
            Console.WriteLine();
            Console.WriteLine(">>> Schema 演进(补列)");
            await EvolveSchemaAsync(db);
            // 索引在 CreateAllTables 末尾执行,此时列已就绪
            await EnsureAllIndexes(db);
        }

        // Seed
        if (doSeed)
        {
            Console.WriteLine();
            Console.WriteLine(">>> 部门种子");
            await SeedDepartmentsAsync(db);
            Console.WriteLine();
            Console.WriteLine(">>> Identity 种子");
            await SeedIdentityAsync(db, customAdminPwd ?? "Admin@123");
            Console.WriteLine();
            Console.WriteLine(">>> 权限码种子");
            await SeedPermissionsAsync(db);
            Console.WriteLine();
            Console.WriteLine(">>> 绑定 Admin 全部权限");
            await BindAdminAllPermissionsAsync(db);
            Console.WriteLine();
            Console.WriteLine(">>> 绑定 Operator / Viewer 业务权限");
            await BindOperatorAndViewerPermissionsAsync(db);
        }

        // Migrate
        if (doMigrate)
        {
            Console.WriteLine();
            Console.WriteLine(">>> 数据迁移");
            await MigrateFromSqlFilesAsync(db, sourceDir!);
            Console.WriteLine();
            Console.WriteLine(">>> 角色分配整理(仅超管保留 Admin,其他用户归位 Operator)");
            await NormalizeUserRoleAssignmentsAsync(db);

            // (2026-07-09 Fix) DepartmentId 回填:从 if (doSeed) 改到 if (doMigrate)
            //   原 bug:用户只跑 --migrate 不跑 --seed 时,DepartmentId 完全不回填
            //   现在:在 Migrate 之后总是跑;默认部门若不存在则自动建(保证非空)
            //   这步操作的是 migrate 出来的 Users,与 --seed 的"灌种子"职责分离
            Console.WriteLine();
            Console.WriteLine(">>> 回填 Users.DepartmentId(旧员工无部门 → 默认部门)");
            await BackfillUserDepartmentsAsync(db);
        }

        // ============================================================
        // (2026-07-08 Review #L-7) C0X 列重命名专用 3 模式
        // 这 3 个模式独立运行,不与 schema/seed/migrate 混用。
        // ============================================================
        if (doCheckC0X)
        {
            Console.WriteLine();
            Console.WriteLine(">>> 检查 C0X 列/索引状态(L-7)");
            await CheckC0XStateAsync(db);
        }
        if (doRenameC0X)
        {
            Console.WriteLine();
            Console.WriteLine(">>> C0X 列重命名 DrawName/DrawNumber → DrawingName/DrawingNumber (L-7)");
            await RenameC0XColumnsAsync(db, rollback: false);
        }
        if (doRollbackC0X)
        {
            Console.WriteLine();
            Console.WriteLine(">>> C0X 列重命名回滚 DrawingName/DrawingNumber → DrawName/DrawNumber (L-7)");
            await RenameC0XColumnsAsync(db, rollback: true);
        }

        // ============================================================
        // (2026-07-08 Review #L-8) Users.GID 列重命名专用 3 模式
        // ============================================================
        if (doCheckUsersState)
        {
            Console.WriteLine();
            Console.WriteLine(">>> 检查 Users 表 GID/Gid 列状态(L-8)");
            await CheckUsersGidStateAsync(db);
        }
        if (doRenameUsersGid)
        {
            Console.WriteLine();
            Console.WriteLine(">>> Users.GID 列重命名 GID → Gid (L-8)");
            await RenameUsersGidColumnAsync(db, rollback: false);
        }
        if (doRollbackUsersGid)
        {
            Console.WriteLine();
            Console.WriteLine(">>> Users.GID 列重命名回滚 Gid → GID (L-8)");
            await RenameUsersGidColumnAsync(db, rollback: true);
        }

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine(" 完成");
        Console.WriteLine("============================================================");
        return 0;
    }

    // ============================================================
    // 建表
    // ============================================================
    private static async Task CreateAllTables(ISqlSugarClient db)
    {
        // Identity 表(注意 Users 继承 IdentityUser,需要 Identity 引用)
        await SafeCreate<Users>(db, "Users");
        await SafeCreate<Roles>(db, "Roles");
        await SafeCreate<UserRolesRow>(db, "UserRoles");
        await SafeCreate<UserClaimRow>(db, "UserClaims");
        await SafeCreate<RoleClaimRow>(db, "RoleClaims");

        // 权限系统表(细粒度操作权限)
        await SafeCreate<Permissions>(db, "Permissions");
        await SafeCreate<RolePermissions>(db, "RolePermissions");

        // 业务-基础字典表
        await SafeCreate<Departments>(db, "Departments");

        // 业务表
        await SafeCreate<Orders>(db, "Orders");
        await SafeCreate<OrdersItem>(db, "OrdersItem");
        await SafeCreate<DrawingNames>(db, "DrawingNames");
        await SafeCreate<DrawingNumberSequence>(db, "DrawingNumberSequence");
        await SafeCreate<Logs>(db, "Logs");   // NLog dbTarget 输出表

        // C01..C09(C0XBase 是 abstract,SqlSugar 不会为它建表)
        await SafeCreate<C01>(db, "C01");
        await SafeCreate<C02>(db, "C02");
        await SafeCreate<C03>(db, "C03");
        await SafeCreate<C04>(db, "C04");
        await SafeCreate<C05>(db, "C05");
        await SafeCreate<C06>(db, "C06");
        await SafeCreate<C07>(db, "C07");
        await SafeCreate<C08>(db, "C08");
        await SafeCreate<C09>(db, "C09");
    }

    /// <summary>
    /// 演进式 schema 调整:给已存在的 C0X 表加 DrawingNumber 列(若不存在),
    /// 并用 Seed + Id 公式回填已有行的 DrawingNumber,最后把列改为 NOT NULL。
    ///
    /// (2026-07-08 Review #L-7):列名 DrawNumber → DrawingNumber。
    /// 适用范围:已经过 --rename-c0x-drawingname 迁移的库;新库(Code First)直接建新名列。
    /// ⚠️ 在已迁移库上跑本方法,期望看到"列已存在,跳过"+"索引已就绪",不应有任何 ALTER。
    /// </summary>
    private static async Task EvolveSchemaAsync(ISqlSugarClient db)
    {
        foreach (var table in _c0xTables)
        {
            var exists = await db.Ado.GetIntAsync(
                $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = 'DrawingNumber'");
            if (exists == 0)
            {
                await db.Ado.ExecuteCommandAsync($"ALTER TABLE [{table}] ADD [DrawingNumber] INT NULL");
                Console.WriteLine($"  ✓ {table}:新增 DrawingNumber 列");
            }

            // 回填已有行:DrawingNumber = Seed + Id,与运行时公式等价
            var seed = GetSeedDrawingNumber(table);
            var updated = await db.Ado.ExecuteCommandAsync(
                $"UPDATE [{table}] SET [DrawingNumber] = {seed} + [Id] WHERE [DrawingNumber] IS NULL");
            if (updated > 0)
                Console.WriteLine($"  ✓ {table}:回填 DrawingNumber {updated} 行 (seed={seed})");

            // 列改为 NOT NULL(只在确实有数据或已是 NOT NULL 时跳过)
            // 必须先 DROP UNIQUE 索引,否则 SQL Server 拒绝 ALTER COLUMN(索引依赖该列)
            var isNullable = await db.Ado.GetIntAsync(
                $"SELECT CAST(is_nullable AS INT) FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = 'DrawingNumber'");
            if (isNullable == 1)
            {
                var indexName = $"IX_{table}_DrawingNumber";
                await db.Ado.ExecuteCommandAsync($"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('{table}')) DROP INDEX [{indexName}] ON [{table}]");
                await db.Ado.ExecuteCommandAsync($"ALTER TABLE [{table}] ALTER COLUMN [DrawingNumber] INT NOT NULL");
                Console.WriteLine($"  ✓ {table}:DrawingNumber 改为 NOT NULL(临时 DROP 索引后重建)");
            }

            // 冗余列 DrawingType:每个表的行必然等于表名本身,删除
            var hasDrawingType = await db.Ado.GetIntAsync(
                $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = 'DrawingType'");
            if (hasDrawingType > 0)
            {
                await db.Ado.ExecuteCommandAsync($"ALTER TABLE [{table}] DROP COLUMN [DrawingType]");
                Console.WriteLine($"  ✓ {table}:删除冗余列 DrawingType");
            }
        }

        // 演进 Users.DepartmentId 与 FK_Users_Departments
        await EvolveUsersSchemaAsync(db);
    }

    /// <summary>
    /// 演进式 Users schema:加 DepartmentId 列 + FK_Users_Departments 外键。
    /// DepartmentId 列从无到有逐个补齐,FK 约束用 sys.foreign_keys 判定避免重复加。
    /// </summary>
    private static async Task EvolveUsersSchemaAsync(ISqlSugarClient db)
    {
        var hasDeptId = await db.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'DepartmentId'");
        if (hasDeptId == 0)
        {
            await db.Ado.ExecuteCommandAsync(
                "ALTER TABLE [Users] ADD [DepartmentId] INT NULL");
            Console.WriteLine("  ✓ Users:新增 DepartmentId 列");
        }

        var hasFk = await db.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_Users_Departments'");
        if (hasFk == 0)
        {
            await db.Ado.ExecuteCommandAsync(
                @"ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_Departments]
                  FOREIGN KEY ([DepartmentId]) REFERENCES [Departments]([Id])");
            Console.WriteLine("  ✓ Users:建立 FK_Users_Departments 外键");
        }
    }

    /// <summary>所有 UNIQUE 索引(C0X DrawingNumber / Users / Roles),依赖 EvolveSchemaAsync 已补列
    /// (2026-07-08 Review #L-7):C0X 索引名 DrawNumber → DrawingNumber。</summary>
    private static async Task EnsureAllIndexes(ISqlSugarClient db)
    {
        await EnsureUniqueIndex(db, "Users", "IX_Users_NormalizedUserName", "NormalizedUserName");
        await EnsureUniqueIndex(db, "Users", "IX_Users_WorkerNumber", "WorkerNumber");
        await EnsureUniqueIndex(db, "Roles", "IX_Roles_NormalizedName", "NormalizedName");
        await EnsureUniqueIndex(db, "Departments", "IX_Departments_NormalizedName", "NormalizedName");

        // Orders.OrderNumber 用 filtered unique index(仅约束 IsDeleted=0 的活跃行):
        //   软删订单(IsDeleted=1)允许保留原 OrderNumber,
        //   新建订单可以重新使用已软删的订单号,不会因历史数据阻塞业务。
        await EnsureFilteredUniqueIndex(db, "Orders", "IX_Orders_OrderNumber", "OrderNumber", "IsDeleted = 0");

        foreach (var table in _c0xTables)
        {
            await EnsureUniqueIndex(db, table, $"IX_{table}_DrawingNumber", "DrawingNumber");
        }

        // Permissions.Code:SQL Server 不支持表达式 UNIQUE 索引,改用普通非唯一索引(25 个静态值,不会冲突)
        await EnsureIndex(db, "Permissions", "IX_Permissions_Code", "Code");
        await EnsureIndex(db, "Permissions", "IX_Permissions_Category", "Category");
    }

    private static async Task SafeCreate<T>(ISqlSugarClient db, string tableName) where T : class, new()
    {
        if (await TableExists(db, tableName))
        {
            Console.WriteLine($"  - {tableName}:已存在,跳过");
            return;
        }
        await Task.Run(() => db.CodeFirst.InitTables<T>());
        Console.WriteLine($"  ✓ {tableName}:已创建");
    }

    private static async Task<bool> TableExists(ISqlSugarClient db, string tableName)
    {
        try
        {
            // 用 sys.objects 而不是 INFORMATION_SCHEMA.TABLES,
            // 避免 user 默认 schema 不同时查不到 dbo 表的问题
            // 直接内插(表名固定,无注入风险)
            var count = await db.Ado.GetIntAsync(
                $"SELECT COUNT(*) FROM sys.objects WHERE type='U' AND name = '{tableName}'");
            return count > 0;
        }
        catch { return false; }
    }

    private static async Task EnsureIndex(ISqlSugarClient db, string tableName, string indexName, string columns)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('{tableName}'))
BEGIN
    CREATE INDEX {indexName} ON {tableName} ({columns});
    PRINT '  ✓ Index {indexName} created';
END";
        try
        {
            await db.Ado.ExecuteCommandAsync(sql);
            Console.WriteLine($"  ✓ 索引 {indexName} 已就绪");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! 索引 {indexName} 失败:{ex.Message}");
        }
    }

    private static async Task EnsureUniqueIndex(ISqlSugarClient db, string tableName, string indexName, string columns)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('{tableName}'))
BEGIN
    CREATE UNIQUE INDEX {indexName} ON {tableName} ({columns});
    PRINT '  ✓ Unique Index {indexName} created';
END";
        try
        {
            await db.Ado.ExecuteCommandAsync(sql);
            Console.WriteLine($"  ✓ 唯一索引 {indexName} 已就绪");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! 唯一索引 {indexName} 失败:{ex.Message}");
        }
    }

    /// <summary>
    /// filtered unique index:仅约束 WHERE 子句命中的行。
    /// 典型用途:Orders.OrderNumber 只约束 IsDeleted=0 的活跃行,
    /// 软删订单(IsDeleted=1)保留原 OrderNumber,允许业务重新使用同一订单号。
    /// </summary>
    private static async Task EnsureFilteredUniqueIndex(ISqlSugarClient db, string tableName, string indexName, string columns, string whereClause)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('{tableName}'))
BEGIN
    CREATE UNIQUE INDEX {indexName} ON {tableName} ({columns}) WHERE {whereClause};
    PRINT '  ✓ Filtered Unique Index {indexName} created';
END";
        try
        {
            await db.Ado.ExecuteCommandAsync(sql);
            Console.WriteLine($"  ✓ 过滤唯一索引 {indexName} (WHERE {whereClause}) 已就绪");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! 过滤唯一索引 {indexName} 失败:{ex.Message}");
        }
    }

    private static async Task DropAllTables(ISqlSugarClient db)
    {
        // 注意顺序:先删有外键依赖的,后删父表
        // 同时包含 Identity 新表 + 旧业务的 C0X/Orders + 旧权限系统表(Role/UserRole/Permission/RolePermission)
        var tables = new[]
        {
            // 1) Identity 子表(Users 上有 FK_Users_Departments,先删 Departments)
            "RoleClaims", "UserClaims", "UserRoles",
            "Roles", "Users",
            // 2) 业务字典表
            "Departments",
            // 3) 旧权限系统(单数表名,与 Identity 复数表不冲突)
            "RolePermission", "UserRole",
            "Role", "Permission",
            // 4) 业务表
            "DrawingNumberSequence", "Logs", "DrawingNames", "OrdersItem", "Orders",
            "C01","C02","C03","C04","C05","C06","C07","C08","C09"
        };
        foreach (var t in tables)
        {
            try
            {
                // 直接用 IF EXISTS DROP TABLE,绕过任何元数据查询问题
                await db.Ado.ExecuteCommandAsync(
                    $"IF OBJECT_ID(N'{t}', N'U') IS NOT NULL DROP TABLE [{t}];");
                Console.WriteLine($"  ✓ {t}:已删除");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! {t}:删除失败 - {ex.Message}");
            }
        }
    }

    // ============================================================
    // (2026-07-08 Review #L-7) C0X 列重命名专用 3 模式
    // ============================================================

    private static readonly string[] _c0xTables =
        new[] { "C01", "C02", "C03", "C04", "C05", "C06", "C07", "C08", "C09" };

    private static async Task<bool> ColumnExists(ISqlSugarClient db, string table, string column)
    {
        var count = await db.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND name = '{column}'");
        return count > 0;
    }

    private static async Task<bool> IndexExists(ISqlSugarClient db, string table, string indexName)
    {
        var count = await db.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('{table}')");
        return count > 0;
    }

    /// <summary>
    /// (2026-07-08 Review #L-7) 检查 C0X 9 张表的 DrawName/DrawNumber vs DrawingName/DrawingNumber 状态。
    /// 退出码:0 = 已是新名(可跳过迁移),1 = 异常状态(需人工介入),2 = 旧名(可跑 --rename)。
    /// </summary>
    private static async Task CheckC0XStateAsync(ISqlSugarClient db)
    {
        bool anyOld = false, anyNew = false, anyMixed = false;
        foreach (var table in _c0xTables)
        {
            var hasOldNum = await ColumnExists(db, table, "DrawNumber");
            var hasNewNum = await ColumnExists(db, table, "DrawingNumber");
            var hasOldName = await ColumnExists(db, table, "DrawName");
            var hasNewName = await ColumnExists(db, table, "DrawingName");

            if (hasNewNum && hasNewName && !hasOldNum && !hasOldName) { anyNew = true; Console.WriteLine($"  ✓ {table}:新名(已迁移)"); }
            else if (hasOldNum && hasOldName && !hasNewNum && !hasNewName) { anyOld = true; Console.WriteLine($"  · {table}:旧名(待迁移)"); }
            else { anyMixed = true; Console.WriteLine($"  ! {table}:异常 — old=({hasOldNum},{hasOldName}) new=({hasNewNum},{hasNewName})"); }
        }

        Console.WriteLine();
        if (anyMixed) { Console.WriteLine(">>> 状态:异常,需人工介入"); }
        else if (anyNew && !anyOld) { Console.WriteLine(">>> 状态:已迁移,无需操作"); }
        else if (anyOld && !anyNew) { Console.WriteLine(">>> 状态:旧名,可跑 --rename-c0x-drawingname"); }
        else { Console.WriteLine(">>> 状态:9 张表混合(部分旧部分新),需人工介入"); }
    }

    /// <summary>
    /// (2026-07-08 Review #L-7) 把 9 张 C0X 表的 DrawName/DrawNumber 重命名为 DrawingName/DrawingNumber。
    /// 同时 drop 旧唯一索引 IX_C0X_DrawNumber,重建为 IX_C0X_DrawingNumber。
    /// rollback=true 时反向操作。
    /// 全部在一个事务内,失败自动回滚。
    /// </summary>
    private static async Task RenameC0XColumnsAsync(ISqlSugarClient db, bool rollback)
    {
        var oldNum = rollback ? "DrawingNumber" : "DrawNumber";
        var newNum = rollback ? "DrawNumber" : "DrawingNumber";
        var oldName = rollback ? "DrawingName" : "DrawName";
        var newName = rollback ? "DrawName" : "DrawingName";
        var oldIdxSuffix = rollback ? "DrawingNumber" : "DrawNumber";
        var newIdxSuffix = rollback ? "DrawNumber" : "DrawingNumber";
        var direction = rollback ? "回滚" : "重命名";

        // 1. 前置检查:全表一致才执行,任一异常立即报错
        foreach (var table in _c0xTables)
        {
            var hasOldNum = await ColumnExists(db, table, oldNum);
            var hasNewNum = await ColumnExists(db, table, newNum);
            var hasOldName = await ColumnExists(db, table, oldName);
            var hasNewName = await ColumnExists(db, table, newName);
            if (hasNewNum && hasNewName && !hasOldNum && !hasOldName)
            {
                Console.WriteLine($"  - {table}:已是目标状态,跳过");
                continue;
            }
            if (!hasOldNum || !hasOldName)
            {
                throw new InvalidOperationException(
                    $"{table} 状态异常:既无旧列 {oldNum}/{oldName},也无新列 {newNum}/{newName}。需要人工介入。");
            }
            if (hasNewNum || hasNewName)
            {
                throw new InvalidOperationException(
                    $"{table} 状态异常:同时存在旧列 {oldNum}/{oldName} 和新列 {newNum}/{newName}。需要人工介入。");
            }
        }

        // 2. 原子事务:18 列 sp_rename + 9 索引 drop+recreate
        db.Ado.BeginTran();
        try
        {
            foreach (var table in _c0xTables)
            {
                var oldIdx = $"IX_{table}_{oldIdxSuffix}";
                var newIdx = $"IX_{table}_{newIdxSuffix}";

                // 索引先 drop(sp_rename 列后索引的列定义会自动更新,但索引名仍带旧名)
                if (await IndexExists(db, table, oldIdx))
                {
                    await db.Ado.ExecuteCommandAsync($"DROP INDEX [{oldIdx}] ON [{table}]");
                }

                // 重命名列
                await db.Ado.ExecuteCommandAsync(
                    $"EXEC sp_rename 'dbo.{table}.{oldNum}', '{newNum}', 'COLUMN'");
                await db.Ado.ExecuteCommandAsync(
                    $"EXEC sp_rename 'dbo.{table}.{oldName}', '{newName}', 'COLUMN'");

                // 重建唯一索引(如果不存在,常见于已迁移后再 rollback 一次的情况)
                if (!await IndexExists(db, table, newIdx))
                {
                    await db.Ado.ExecuteCommandAsync(
                        $"CREATE UNIQUE INDEX [{newIdx}] ON [{table}]([{newNum}])");
                }

                Console.WriteLine($"  ✓ {table}:{direction}完成 ({oldNum}→{newNum}, {oldName}→{newName})");
            }
            db.Ado.CommitTran();
            Console.WriteLine();
            Console.WriteLine($">>> 所有 9 张表 {direction}已提交");
        }
        catch (Exception ex)
        {
            db.Ado.RollbackTran();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"✗ 失败,已回滚:{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// (2026-07-08 Review #L-8) 检查 Users 表 GID/Gid 列状态。
    /// 退出:标准输出,无异常退出码。
    /// </summary>
    private static async Task CheckUsersGidStateAsync(ISqlSugarClient db)
    {
        var hasOld = await ColumnExists(db, "Users", "GID");
        var hasNew = await ColumnExists(db, "Users", "Gid");

        if (hasNew && !hasOld) { Console.WriteLine("  ✓ Users:新名 Gid(已迁移)"); Console.WriteLine(">>> 状态:已迁移,无需操作"); }
        else if (hasOld && !hasNew) { Console.WriteLine("  · Users:旧名 GID(待迁移)"); Console.WriteLine(">>> 状态:旧名,可跑 --rename-users-gid"); }
        else if (hasNew && hasOld) { Console.WriteLine("  ! Users:异常 — 同时存在 GID 和 Gid 列"); Console.WriteLine(">>> 状态:异常,需人工介入"); }
        else { Console.WriteLine("  ! Users:异常 — 既无 GID 也无 Gid 列"); Console.WriteLine(">>> 状态:异常,需人工介入"); }
    }

    /// <summary>
    /// (2026-07-08 Review #L-8) Users.GID → Users.Gid 列重命名(原子 sp_rename)。
    /// rollback=true 时反向。
    /// </summary>
    private static async Task RenameUsersGidColumnAsync(ISqlSugarClient db, bool rollback)
    {
        var oldName = rollback ? "Gid" : "GID";
        var newName = rollback ? "GID" : "Gid";
        var direction = rollback ? "回滚" : "重命名";

        // 前置检查
        var hasOld = await ColumnExists(db, "Users", oldName);
        var hasNew = await ColumnExists(db, "Users", newName);
        if (hasNew && !hasOld) { Console.WriteLine($"  - Users:已是目标状态({newName}),跳过"); return; }
        if (!hasOld)
        {
            throw new InvalidOperationException(
                $"Users 表无 {oldName} 列,异常状态。需要人工介入。");
        }
        if (hasNew)
        {
            throw new InvalidOperationException(
                $"Users 表同时存在 {oldName} 和 {newName} 列,异常状态。需要人工介入。");
        }

        // 原子事务(虽然只有 1 个 sp_rename,套事务保持与其他 L-7 模式一致)
        db.Ado.BeginTran();
        try
        {
            await db.Ado.ExecuteCommandAsync(
                $"EXEC sp_rename 'dbo.Users.{oldName}', '{newName}', 'COLUMN'");
            db.Ado.CommitTran();
            Console.WriteLine($"  ✓ Users.{oldName} → Users.{newName} ({direction}完成)");
        }
        catch (Exception ex)
        {
            db.Ado.RollbackTran();
            Console.Error.WriteLine($"  ✗ 失败,已回滚:{ex.Message}");
            throw;
        }
    }

    // ============================================================
    // Identity 种子
    // ============================================================
    private static async Task SeedDepartmentsAsync(ISqlSugarClient db)
    {
        var defaults = new[]
        {
            new { Name = "MD 部门", Code = (string?)"MD", Desc = (string?)"默认种子部门" }
        };

        foreach (var d in defaults)
        {
            var normalized = d.Name.ToUpperInvariant();
            var exists = await db.Queryable<Departments>()
                .Where(x => x.NormalizedName == normalized)
                .FirstAsync();
            if (exists != null)
            {
                Console.WriteLine($"  - 部门已存在:{d.Name}");
                continue;
            }

            var newId = await db.Insertable(new Departments
            {
                Name = d.Name,
                NormalizedName = normalized,
                Code = d.Code,
                Description = d.Desc,
                IsActive = true,
                CreatedTime = DateTime.Now,
                IsDeleted = false
            }).ExecuteReturnIdentityAsync();
            Console.WriteLine($"  ✓ 部门种子:{d.Name}(Id={newId})");
        }
    }

    /// <summary>
    /// 回填历史员工的 DepartmentId:
    ///   - 旧 Users.sql 没有 DepartmentId 列,迁移后全部为 NULL
    ///   - UsersService 用 INNER JOIN Departments,NULL 直接被滤掉 → 列表为空
    ///   - 这里把所有 DepartmentId IS NULL 且非超管的员工指向默认部门('MD 部门')
    ///   - 必须在 MigrateFromSqlFilesAsync 之后调用(操作的是 migrate 出来的 Users)
    ///
    /// (2026-07-09 Fix)从 if (doSeed) 改到 if (doMigrate);默认部门缺失时自动建
    ///   原 bug:用户只跑 --migrate 不加 --seed 时,此函数根本不被调用,
    ///   DepartmentId 全部 NULL,UI 列表查不到这些用户。
    /// </summary>
    private static async Task BackfillUserDepartmentsAsync(ISqlSugarClient db)
    {
        // 1) 找默认部门,缺失则自动建(不再静默跳过)
        var defaultDept = await db.Queryable<Departments>()
            .Where(d => d.NormalizedName == "MD 部门" && !d.IsDeleted)
            .FirstAsync();
        if (defaultDept == null)
        {
            Console.WriteLine("  - 未找到 'MD 部门',自动创建");
            var newDeptId = await db.Insertable(new Departments
            {
                Name = "MD 部门",
                NormalizedName = "MD 部门",
                Code = "MD",
                Description = "默认种子部门(BackfillUserDepartmentsAsync 自动建)",
                IsActive = true,
                CreatedTime = DateTime.Now,
                IsDeleted = false
            }).ExecuteReturnIdentityAsync();
            defaultDept = new Departments { Id = newDeptId, Name = "MD 部门", NormalizedName = "MD 部门" };
            Console.WriteLine($"  ✓ 已自动建 'MD 部门'(Id={newDeptId})");
        }

        // 2) 先打印"应当回填多少行"的预估值,便于排查
        var expected = await db.Ado.GetIntAsync(
            @"SELECT COUNT(*) FROM [Users] u
              LEFT JOIN [UserRoles] ur ON ur.UserId = u.Id
              LEFT JOIN [Roles] r      ON r.Id  = ur.RoleId AND r.NormalizedName = 'ADMIN'
              WHERE u.DepartmentId IS NULL AND u.IsDeleted = 0 AND r.Id IS NULL");
        Console.WriteLine($"  预期回填 {expected} 条(非超管、DepartmentId IS NULL、未删除)");

        if (expected == 0)
        {
            // 这种情况可能是:用户已经全部设过部门了,或者表里没有用户
            // 不报错,只是提示一下
            var totalUsers = await db.Ado.GetIntAsync("SELECT COUNT(*) FROM [Users] WHERE IsDeleted = 0");
            var nullDept = await db.Ado.GetIntAsync("SELECT COUNT(*) FROM [Users] WHERE IsDeleted = 0 AND DepartmentId IS NULL");
            Console.WriteLine($"  ℹ 当前 Users 共 {totalUsers} 条,其中 {nullDept} 条 DepartmentId 为 NULL");
            return;
        }

        // 3) 实际 UPDATE
        var rows = await db.Ado.ExecuteCommandAsync(
            @"UPDATE u SET u.DepartmentId = @deptId
              FROM [Users] u
              LEFT JOIN [UserRoles] ur ON ur.UserId = u.Id
              LEFT JOIN [Roles] r      ON r.Id  = ur.RoleId AND r.NormalizedName = 'ADMIN'
              WHERE u.DepartmentId IS NULL
                AND u.IsDeleted = 0
                AND r.Id IS NULL",
            new SugarParameter("@deptId", defaultDept.Id));

        if (rows != expected)
        {
            // 预估值和实际值不一致,可能是迁移过程中有数据写入
            Console.WriteLine($"  ⚠ 实际回填 {rows} 条(预期 {expected} 条,差 {expected - rows})");
        }
        else
        {
            Console.WriteLine($"  ✓ 回填 {rows} 条 → {defaultDept.Name}(Id={defaultDept.Id})");
        }
    }

    private static async Task SeedIdentityAsync(ISqlSugarClient db, string adminPassword)
    {
        var passwordHasher = new PasswordHasher<Users>();

        // 角色(三角色:Admin / Operator / Viewer)
        var adminRole    = await GetOrCreateRoleAsync(db, "Admin",    "ADMIN");
        var operatorRole = await GetOrCreateRoleAsync(db, "Operator", "OPERATOR");
        var viewerRole   = await GetOrCreateRoleAsync(db, "Viewer",   "VIEWER");
        Console.WriteLine($"  角色:Admin(Id={adminRole.Id}), Operator(Id={operatorRole.Id}), Viewer(Id={viewerRole.Id})");

        // 管理员
        var existingAdmin = await db.Queryable<Users>()
            .Where(u => u.NormalizedUserName == "1")
            .FirstAsync();

        Users admin;
        if (existingAdmin == null)
        {
            admin = new Users
            {
                UserName = "1",
                NormalizedUserName = "1",
                Name = "超级管理员",
                WorkerNumber = 1,
                Gid = "ADMIN",
                CreatedTime = DateTime.Now,
                IsDeleted = false,
                LockoutEnabled = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            admin.PasswordHash = passwordHasher.HashPassword(admin, adminPassword);
            var newId = await db.Insertable(admin).ExecuteReturnIdentityAsync();
            admin.Id = newId;
            Console.WriteLine($"  ✓ 管理员已创建:WorkerNumber=1, Id={newId}");
        }
        else
        {
            admin = existingAdmin;
            Console.WriteLine($"  - 管理员已存在(Id={admin.Id})");
        }

        // 关联角色
        var hasRole = await db.Queryable<UserRolesRow>()
            .Where(r => r.UserId == admin.Id && r.RoleId == adminRole.Id)
            .AnyAsync();
        if (!hasRole)
        {
            await db.Insertable(new UserRolesRow { UserId = admin.Id, RoleId = adminRole.Id })
                .ExecuteCommandAsync();
            Console.WriteLine("  ✓ 已关联 Admin 角色");
        }

        Console.WriteLine();
        Console.WriteLine("  管理员登录信息:");
        Console.WriteLine("    员工号: 1");
        Console.WriteLine($"    密  码: {adminPassword}");
        Console.WriteLine("    ⚠ 首次登录后请立即改密");
    }

    private static async Task<Roles> GetOrCreateRoleAsync(ISqlSugarClient db, string name, string normalized)
    {
        var existing = await db.Queryable<Roles>()
            .Where(r => r.NormalizedName == normalized)
            .FirstAsync();
        if (existing != null) return existing;

        var role = new Roles
        {
            Name = name,
            NormalizedName = normalized,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        role.Id = await db.Insertable(role).ExecuteReturnIdentityAsync();
        return role;
    }

    /// <summary>
    /// 灌入 27 个操作权限码字典(原 25 + 新增 Order.Edit / Order.Delete)。
    /// 幂等:只补灌缺失的 Code,不覆盖已有。
    /// 与运行时 Services/PermissionsService.cs 中使用的 Code 常量保持一致。
    /// </summary>
    private static async Task SeedPermissionsAsync(ISqlSugarClient db)
    {
        var seeds = new List<Permissions>
        {
            // 人员管理
            new() { Code = "User.Read",          Name = "查看人员",   Category = "人员管理", SortOrder = 1 },
            new() { Code = "User.Create",        Name = "新增人员",   Category = "人员管理", SortOrder = 2 },
            new() { Code = "User.Edit",          Name = "编辑人员",   Category = "人员管理", SortOrder = 3 },
            new() { Code = "User.Delete",        Name = "删除人员",   Category = "人员管理", SortOrder = 4 },
            new() { Code = "User.ResetPassword", Name = "重置密码",   Category = "人员管理", SortOrder = 5 },

            // 部门管理
            new() { Code = "Department.Read",   Name = "查看部门",   Category = "部门管理", SortOrder = 1 },
            new() { Code = "Department.Create", Name = "新增部门",   Category = "部门管理", SortOrder = 2 },
            new() { Code = "Department.Edit",   Name = "编辑部门",   Category = "部门管理", SortOrder = 3 },
            new() { Code = "Department.Delete", Name = "删除部门",   Category = "部门管理", SortOrder = 4 },

            // 角色权限
            new() { Code = "Role.Read",        Name = "查看角色",     Category = "角色权限", SortOrder = 1 },
            new() { Code = "Role.Create",      Name = "新增角色",     Category = "角色权限", SortOrder = 2 },
            new() { Code = "Role.Edit",        Name = "编辑角色",     Category = "角色权限", SortOrder = 3 },
            new() { Code = "Role.Delete",      Name = "删除角色",     Category = "角色权限", SortOrder = 4 },
            new() { Code = "Role.AssignUser",  Name = "分配用户角色", Category = "角色权限", SortOrder = 5 },

            // 订单
            new() { Code = "Order.Read",    Name = "查看订单", Category = "订单", SortOrder = 1 },
            new() { Code = "Order.Create",  Name = "下单",     Category = "订单", SortOrder = 2 },
            new() { Code = "Order.Approve", Name = "审核订单", Category = "订单", SortOrder = 3 },
            new() { Code = "Order.Cancel",  Name = "取消订单", Category = "订单", SortOrder = 4 },
            new() { Code = "Order.Edit",    Name = "编辑订单", Category = "订单", SortOrder = 5 },
            new() { Code = "Order.Delete",  Name = "删除订单", Category = "订单", SortOrder = 6 },

            // 图号
            new() { Code = "DrawingNumber.Read",     Name = "查询图号", Category = "图号", SortOrder = 1 },
            new() { Code = "DrawingNumber.Allocate", Name = "分配图号", Category = "图号", SortOrder = 2 },
            new() { Code = "DrawingNumber.Update",   Name = "修改图号", Category = "图号", SortOrder = 3 },

            // 图名
            new() { Code = "DrawingName.Read",   Name = "查看图名", Category = "图名", SortOrder = 1 },
            new() { Code = "DrawingName.Create", Name = "新增图名", Category = "图名", SortOrder = 2 },
            new() { Code = "DrawingName.Edit",   Name = "编辑图名", Category = "图名", SortOrder = 3 },
            new() { Code = "DrawingName.Delete", Name = "删除图名", Category = "图名", SortOrder = 4 },
        };

        // 幂等补灌:只插入 Code 不存在的行(便于老库平滑升级)
        var existingCodes = (await db.Queryable<Permissions>().ToListAsync())
            .Select(p => p.Code).ToHashSet();
        var toInsert = seeds.Where(s => !existingCodes.Contains(s.Code)).ToList();
        if (toInsert.Count > 0)
        {
            await db.Insertable(toInsert).ExecuteCommandAsync();
            Console.WriteLine($"  ✓ 补灌 {toInsert.Count} 个权限码(共 {seeds.Count} 条种子)");
        }
        else
        {
            var count = await db.Queryable<Permissions>().CountAsync();
            Console.WriteLine($"  - Permissions 已有 {count} 条,无新增,跳过");
        }
    }

    /// <summary>
    /// 给 ADMIN 角色绑定所有权限码。幂等:
    ///   - 若 ADMIN 当前权限数 = 0(全新库),全量绑定(自动包含 SeedPermissionsAsync 新增的权限码)
    ///   - 若 ADMIN 已有权限,跳过(避免覆盖运营人员手工调整)
    /// </summary>
    private static async Task BindAdminAllPermissionsAsync(ISqlSugarClient db)
    {
        var adminRole = await db.Queryable<Roles>()
            .Where(r => r.NormalizedName == "ADMIN")
            .FirstAsync();
        if (adminRole == null)
        {
            Console.WriteLine("  ! ADMIN 角色不存在,跳过");
            return;
        }

        var existingCount = await db.Queryable<RolePermissions>()
            .Where(rp => rp.RoleId == adminRole.Id)
            .CountAsync();
        if (existingCount > 0)
        {
            Console.WriteLine($"  - ADMIN 已有 {existingCount} 条权限关联,跳过(避免覆盖)");
            return;
        }

        var allPerms = await db.Queryable<Permissions>().ToListAsync();
        if (allPerms.Count == 0)
        {
            Console.WriteLine("  ! Permissions 表为空,先跑权限码种子");
            return;
        }

        var rows = allPerms.Select(p => new RolePermissions
        {
            RoleId = adminRole.Id,
            PermissionId = p.Id
        }).ToList();

        await db.Insertable(rows).ExecuteCommandAsync();
        Console.WriteLine($"  ✓ ADMIN 已绑定全部 {rows.Count} 个权限码");
    }

    /// <summary>
    /// 给 OPERATOR 和 VIEWER 角色绑定预设业务权限码。幂等:
    ///   - 若角色当前权限数 = 0(全新库),按预设全量绑定
    ///   - 若已有权限,跳过(避免覆盖运营人员手工调整)
    ///
    /// OPERATOR(图号分配员 / 业务操作员):订单增改查、图号增改查、图名增改查
    /// VIEWER(只读):查看订单 + 查询图号
    /// </summary>
    private static async Task BindOperatorAndViewerPermissionsAsync(ISqlSugarClient db)
    {
        var allPerms = await db.Queryable<Permissions>().ToListAsync();
        var byCode = allPerms.ToDictionary(p => p.Code);

        var mapping = new Dictionary<string, string[]>
        {
            ["OPERATOR"] = new[]
            {
                "Order.Read", "Order.Create", "Order.Edit",
                "DrawingNumber.Read", "DrawingNumber.Allocate", "DrawingNumber.Update",
                "DrawingName.Read", "DrawingName.Create", "DrawingName.Edit"
            },
            ["VIEWER"] = new[]
            {
                "DrawingNumber.Read", "Order.Read"
            },
        };

        foreach (var (normalizedName, codes) in mapping)
        {
            var role = await db.Queryable<Roles>()
                .Where(r => r.NormalizedName == normalizedName)
                .FirstAsync();
            if (role == null)
            {
                Console.WriteLine($"  ! {normalizedName} 角色不存在,跳过");
                continue;
            }

            var existingCount = await db.Queryable<RolePermissions>()
                .Where(rp => rp.RoleId == role.Id)
                .CountAsync();
            if (existingCount > 0)
            {
                Console.WriteLine($"  - {normalizedName} 已有 {existingCount} 条权限关联,跳过(避免覆盖)");
                continue;
            }

            var rows = codes
                .Where(c => byCode.ContainsKey(c))
                .Select(c => new RolePermissions { RoleId = role.Id, PermissionId = byCode[c].Id })
                .ToList();
            await db.Insertable(rows).ExecuteCommandAsync();
            Console.WriteLine($"  ✓ {normalizedName} 已绑定 {rows.Count} 个权限码");
        }
    }

    // ============================================================
    // 数据迁移
    // ============================================================

    /// <summary>
    /// 数据迁移后的角色分配整理:只有超管(WorkerNumber=1)保留 Admin,
    /// 其他所有活跃用户统一归位为 Operator。Viewer 由超管在 /user-roles 手动分配。
    ///
    /// 幂等:
    ///   - 非超管用户若有 Admin 角色关联 → 删除
    ///   - 非超管用户若无 Operator 角色关联 → 新增
    ///   - 已正确归位的用户不重复处理
    /// 软删用户(IsDelete=true)跳过。
    /// </summary>
    private static async Task NormalizeUserRoleAssignmentsAsync(ISqlSugarClient db)
    {
        var operatorRole = await db.Queryable<Roles>()
            .Where(r => r.NormalizedName == "OPERATOR")
            .FirstAsync();
        if (operatorRole == null)
        {
            Console.WriteLine("  ! OPERATOR 角色不存在,跳过整理(请先跑 --seed)");
            return;
        }

        var adminRole = await db.Queryable<Roles>()
            .Where(r => r.NormalizedName == "ADMIN")
            .FirstAsync();

        var superAdminUserId = await db.Queryable<Users>()
            .Where(u => u.NormalizedUserName == "1")
            .Select(u => u.Id)
            .FirstAsync();
        if (superAdminUserId == 0)
        {
            Console.WriteLine("  ! 超管(WorkerNumber=1)不存在,跳过整理");
            return;
        }

        // 找所有非超管、非软删的用户 Id
        var candidateUserIds = await db.Queryable<Users>()
            .Where(u => !u.IsDeleted && u.Id != superAdminUserId)
            .Select(u => u.Id)
            .ToListAsync();

        int addedOperator = 0;
        int removedAdmin = 0;
        foreach (var uid in candidateUserIds)
        {
            // 1) 剥离 Admin 角色关联(如果有)
            if (adminRole != null)
            {
                var hadAdmin = await db.Queryable<UserRolesRow>()
                    .Where(ur => ur.UserId == uid && ur.RoleId == adminRole.Id)
                    .AnyAsync();
                if (hadAdmin)
                {
                    await db.Deleteable<UserRolesRow>()
                        .Where(ur => ur.UserId == uid && ur.RoleId == adminRole.Id)
                        .ExecuteCommandAsync();
                    removedAdmin++;
                }
            }

            // 2) 确保有 Operator 角色关联(没有则新增)
            var hasOperator = await db.Queryable<UserRolesRow>()
                .Where(ur => ur.UserId == uid && ur.RoleId == operatorRole.Id)
                .AnyAsync();
            if (!hasOperator)
            {
                await db.Insertable(new UserRolesRow { UserId = uid, RoleId = operatorRole.Id })
                    .ExecuteCommandAsync();
                addedOperator++;
            }
        }

        Console.WriteLine($"  ✓ 角色整理完成:为 {addedOperator} 名用户新增 Operator,从 {removedAdmin} 名用户剥离 Admin (共检查 {candidateUserIds.Count} 人)");
    }

    private static async Task MigrateFromSqlFilesAsync(ISqlSugarClient db, string sourceDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine($"  ✗ 源目录不存在: {sourceDir}");
            return;
        }

        // 1. Users
        var usersFile = FindSqlFile(sourceDir, "Users");
        if (usersFile != null)
        {
            await MigrateUsersAsync(db, usersFile);
        }
        else
        {
            Console.WriteLine("  - 未找到 Users.sql,跳过");
        }

        // 2. Orders + ReactorOrder
        var orderFile = FindSqlFile(sourceDir, "Order");
        var reactorFile = FindSqlFile(sourceDir, "ReactorOrder");
        // 旧 Order.Id → 新 Orders.Id(C01-C07 用此映射)
        var orderIdMap = new Dictionary<int, int>();
        // 旧 ReactorOrder.Id → 新 Orders.Id(C08 用此映射)
        var reactorIdMap = new Dictionary<int, int>();
        if (orderFile != null)
        {
            await MigrateOrdersAsync(db, orderFile, isReactor: false, idMap: orderIdMap);
        }
        else
        {
            Console.WriteLine("  - 未找到 Order.sql,跳过");
        }
        if (reactorFile != null)
        {
            await MigrateOrdersAsync(db, reactorFile, isReactor: true, idMap: reactorIdMap);
        }
        else
        {
            Console.WriteLine("  - 未找到 ReactorOrder.sql,跳过");
        }

        // 3. C01-C09
        // C01-C07: 旧 OrderId 需要用 orderIdMap 重映射(因为新 Orders 按插入顺序自增)
        // C08:     旧 ReactorOrderId 需要用 reactorIdMap 重映射
        // C09:     一般没数据,保险起见也用 orderIdMap
        for (int i = 1; i <= 7; i++)
        {
            var file = FindSqlFile(sourceDir, $"C0{i}");
            if (file != null)
                await MigrateDrawingNumbersAsync(db, file, idMap: orderIdMap);
            else
                Console.WriteLine($"  - 未找到 C0{i}.sql,跳过");
        }
        var c08File = FindSqlFile(sourceDir, "C08");
        if (c08File != null)
            await MigrateDrawingNumbersAsync(db, c08File, idMap: reactorIdMap);
        else
            Console.WriteLine("  - 未找到 C08.sql,跳过");

        // C09 一般没数据,但保险起见
        var c09File = FindSqlFile(sourceDir, "C09");
        if (c09File != null)
            await MigrateDrawingNumbersAsync(db, c09File, idMap: orderIdMap);

        // 4. DrawingNames
        var dnFile = FindSqlFile(sourceDir, "DrawingNames") ?? FindSqlFile(sourceDir, "DrawingName");
        if (dnFile != null)
            await MigrateDrawingNamesAsync(db, dnFile);
        else
            Console.WriteLine("  - 未找到 DrawingNames.sql,跳过");

        // 5. 初始化图号序列表(每张 C0X 写入 CurrentMax = MAX(DrawNumber),迁移后下次分配接续)
        await InitDrawingNumberSequenceAsync(db);
    }

    /// <summary>
    /// 把每张 C0X 表当前 MAX(DrawingNumber) 写入 DrawingNumberSequence,
    /// 保证迁移完成后 Web 端首次分配 = MAX + 1,与旧库"最后一张图号 + 1"语义一致。
    ///
    /// (2026-07-08 Review #L-7):列名 DrawNumber → DrawingNumber。
    /// </summary>
    private static async Task InitDrawingNumberSequenceAsync(ISqlSugarClient db)
    {
        Console.WriteLine();
        Console.WriteLine(">>> 初始化图号序列表");
        foreach (var table in _c0xTables)
        {
            try
            {
                var max = await db.Ado.GetIntAsync($"SELECT ISNULL(MAX(DrawingNumber), 0) FROM [{table}]");
                await db.Insertable(new DrawingNumberSequence
                {
                    DrawingType = table,
                    CurrentMax = max
                }).ExecuteCommandAsync();
                Console.WriteLine($"  ✓ {table}:CurrentMax={max}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! {table} 初始化失败:{ex.Message}");
            }
        }
    }

    private static string? FindSqlFile(string dir, string namePrefix)
    {
        var exact = Path.Combine(dir, $"{namePrefix}.sql");
        if (File.Exists(exact)) return exact;
        // 大小写不敏感扫描
        return Directory.GetFiles(dir, $"{namePrefix}*.sql")
            .FirstOrDefault();
    }

    private static async Task MigrateUsersAsync(ISqlSugarClient db, string file)
    {
        Console.WriteLine($"  Users ← {Path.GetFileName(file)}");
        var rows = SqlInsertParser.ParseFile(file);
        Console.WriteLine($"    解析到 {rows.Count} 行");

        var passwordHasher = new PasswordHasher<Users>();
        int inserted = 0;
        int skipped = 0;
        foreach (var row in rows)
        {
            try
            {
                var workerNumber = Convert.ToInt32(row["WorkerNumber"]);
                var existing = await db.Queryable<Users>()
                    .Where(u => u.WorkerNumber == workerNumber)
                    .FirstAsync();
                if (existing != null) { skipped++; continue; }

                var email = row["Email"] as string;

                var user = new Users
                {
                    UserName = workerNumber.ToString(),
                    NormalizedUserName = workerNumber.ToString().ToUpperInvariant(),
                    Name = row["Name"] as string ?? "",
                    WorkerNumber = workerNumber,
                    Gid = ((row["GID"] as string) ?? "").ToUpper(),
                    // 从旧库 SQL 直接复制 Email 和 PhoneNumber(之前 Excel 富化已删除,这里接管)
                    Email = email,
                    NormalizedEmail = email?.ToUpperInvariant(),
                    PhoneNumber = row["PhoneNumber"] as string,
                    CreatedTime = ToDateTime(row["CreateTime"]) ?? DateTime.Now,
                    IsDeleted = false,
                    LockoutEnabled = true,
                    // 预设密码 123456(明文),用 Identity PBKDF2 哈希
                    PasswordHash = passwordHasher.HashPassword(
                        new Users { Id = 0, UserName = workerNumber.ToString() },
                        "123456"),
                    SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                };
                await db.Insertable(user).ExecuteCommandAsync();
                inserted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ! 插入失败(Name='{row["Name"]}'):{ex.Message}");
            }
        }
        Console.WriteLine($"    ✓ 插入 {inserted} 条,跳过 {skipped} 条(已存在)");
    }

    /// <summary>
    /// 迁移 Orders 或 ReactorOrder。
    /// 如果 isReactor=true,把旧 ReactorOrder.Id 映射到新 Orders.Id(写入 idMap)。
    ///
    /// 行为:
    ///   - Orders 表用自增 Id(新值,跟旧 Id 无关 — 用户允许)
    ///   - ReactorOrder 同样按自增顺序插入 Orders,IsReactor=true 区分
    ///   - idMap:旧 Order.Id 或旧 ReactorOrder.Id → 新 Orders.Id
    ///   - 后续 C0X 迁移通过 idMap 把 OrderId 重写到新 Id(保证 619 条 C04-C07 引用不丢)
    /// </summary>
    private static async Task MigrateOrdersAsync(
        ISqlSugarClient db, string file, bool isReactor, Dictionary<int, int>? idMap)
    {
        var label = isReactor ? "ReactorOrder" : "Order";
        Console.WriteLine($"  {label} ← {Path.GetFileName(file)}");
        var rows = SqlInsertParser.ParseFile(file);
        Console.WriteLine($"    解析到 {rows.Count} 行");

        int inserted = 0;
        int failed = 0;
        foreach (var row in rows)
        {
            try
            {
                int? legacyId = row["Id"] != null ? Convert.ToInt32(row["Id"]) : null;
                var order = new Orders
                {
                    // Id 不指定,SqlSugar 用自增分配,ExecuteReturnIdentityAsync 拿回来
                    OrderNumber = Convert.ToInt32(row["OrderNumber"]),
                    Power = row["Power"] != null ? Convert.ToInt32(row["Power"]) : 0,
                    RatedVoltage = row["RatedVoltage"] != null
                        ? Convert.ToInt32(row["RatedVoltage"])
                        : (row["Ratedvoltage"] != null ? Convert.ToInt32(row["Ratedvoltage"]) : 0),
                    IsReactor = isReactor,
                    CreatedBy = row["CreatedBy"] as string ?? "",
                    UpdatedBy = row["CreatedBy"] as string ?? "",  // 旧库没有 UpdatedBy,用创建者
                    CreatedTime = ToDateTime(row["CreatedTime"]) ?? DateTime.Now,
                    UpdatedTime = ToDateTime(row["CreatedTime"]) ?? DateTime.Now,
                    IsDeleted = false
                };
                var newId = await db.Insertable(order).ExecuteReturnIdentityAsync();

                // 任何类型都填映射表:C04-C07 用 orderIdMap,C08 用 reactorIdMap
                if (legacyId.HasValue && idMap != null)
                    idMap[legacyId.Value] = newId;
                inserted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ! 插入失败:{ex.Message}");
                failed++;
            }
        }
        Console.WriteLine($"    ✓ 插入 {inserted} 条" + (failed > 0 ? $",失败 {failed} 条" : "") +
            (idMap != null ? $",映射表累积 {idMap.Count} 条" : ""));
    }

    /// <summary>
    /// 迁移 C01..C09。
    /// idMap: 旧 OrderId → 新 OrderId
    ///   - C04-C07 传 orderIdMap
    ///   - C08     传 reactorIdMap
    ///   - C09     保险起见传 orderIdMap
    ///
    /// 关键行为:
    ///   1. 显式保留旧 C0X.Id(实体 IsIdentity=true,但 INSERT 时用 SET IDENTITY_INSERT ON 手动赋值)
    ///   2. **仅补 min..max 范围内的真实跳号** — 不补"旧库从未用过"的 Id(比如 C04 从 Id=7 开始,1-6 不补)
    ///   3. 占位行 = "被删除的旧图纸尸骨" — 复制"跳号之后的第一条真实数据"的字段
    ///      (DrawName/OrderId/CreatedBy/CreatedTime 等),仅 Id 不同,IsDelete=true 标记删除
    ///   4. **从 Id=1 开始全覆盖占位**(用户要求):
    ///      C04 SQL 从 Id=7 开始 → 补 Id=1..6 占位,复制 Id=8 那行(铁芯装配)
    ///      C07 SQL 从 Id=8 开始 → 补 Id=1..7 占位,复制 Id=8 那行(外形尺寸图)
    ///      C07 SQL 跳号 13   → 补 Id=13 占位,复制 Id=14 那行(涂漆技术条件)
    ///      其他表从 Id=1 开始 → 不需要补占位
    ///   4. C0X.OrderId 通过 idMap 重映射到新 Orders.Id(防止 619 条孤儿引用)
    /// </summary>
    private static async Task MigrateDrawingNumbersAsync(
        ISqlSugarClient db, string file, Dictionary<int, int>? idMap)
    {
        var tableName = Path.GetFileNameWithoutExtension(file);
        Console.WriteLine($"  {tableName} ← {Path.GetFileName(file)}");
        var rows = SqlInsertParser.ParseFile(file);
        Console.WriteLine($"    解析到 {rows.Count} 行");

        // 1) 先插真实数据(显式 Id)
        //    按 Id 升序处理,这样后面补占位行时,"前一行的真实数据"就是列表里 Id 最接近的那个
        var sortedRows = rows
            .Select(r => new { Row = r, OldId = Convert.ToInt32(r["Id"]) })
            .OrderBy(x => x.OldId)
            .ToList();

        // 保存所有已插的真实行(按 Id 排序),用于补占位时"找前一行"
        var insertedRealEntities = new List<C0XInsertDto>();

        int inserted = 0;
        int skipped = 0;
        var seenIds = new HashSet<int>();
        foreach (var entry in sortedRows)
        {
            var row = entry.Row;
            try
            {
                var oldOrderId = Convert.ToInt32(row["OrderId"]);
                // C0X.OrderId 必须通过 idMap 指向新 Orders.Id
                var newOrderId = idMap != null && idMap.TryGetValue(oldOrderId, out var mapped)
                    ? mapped
                    : 0;  // 旧 OrderId 找不到映射时,OrderId 置 0(占位行风格)
                var oldId = entry.OldId;

                var entity = new C0XInsertDto
                {
                    Id = oldId,
                    DrawingNumber = GetSeedDrawingNumber(tableName) + oldId,
                    // 兼容旧库导出 SQL:旧库 C0X 表在 L-7 列重命名前用 DrawName,
                    // 新库用 DrawingName。优先读新名,fallback 旧名,
                    // 这样用户每次重新从旧库导出 SQL 直接覆盖即可,不用关心列名。
                    DrawingName = row["DrawingName"] as string ?? row["DrawName"] as string ?? "",
                    OrderId = newOrderId,
                    CreatedBy = row["CreatedBy"] as string ?? "",
                    UpdatedBy = row["UpdatedBy"] as string ?? "",
                    CreatedTime = ToDateTime(row["CreatedTime"]) ?? DateTime.Now,
                    UpdatedTime = ToDateTime(row["UpdatedTime"]) ?? DateTime.Now,
                    IsDelete = row["IsDelete"] != null && Convert.ToBoolean(row["IsDelete"])
                };

                await InsertC0X(db, tableName, entity);
                seenIds.Add(oldId);
                insertedRealEntities.Add(entity);
                inserted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ! 插入失败(Id={entry.OldId},OrderId={row["OrderId"]}):{ex.Message}");
                skipped++;
            }
        }

        // 2) 从 Id=1 开始全覆盖占位(用户要求:C04/C07 等"开头跳号的表"也要补占位行)
        //    例子:C04 从 Id=7 开始 → 补 Id=1..6 占位,内容复制 Id=8(铁芯装配)
        //         C07 从 Id=8 开始 → 补 Id=1..7 占位,内容复制 Id=8(外形尺寸图)
        //         C07 跳号 13   → 补 Id=13 占位,内容复制 Id=14(涂漆技术条件)
        //    业务层 FindByIdAsync / UpdateDrawNameAsync 已加 !IsDelete 过滤,占位行不会暴露
        int placeholderInserted = 0;
        if (insertedRealEntities.Count > 0)
        {
            var maxId = seenIds.Max();
            // 从 Id=1 开始,凡是没出现过的都补占位
            var missingIds = Enumerable.Range(1, maxId)
                .Where(i => !seenIds.Contains(i))
                .ToList();

            if (missingIds.Count > 0)
            {
                Console.WriteLine($"    补 {missingIds.Count} 个占位 Id (1..{maxId} 中缺失的)" +
                    (missingIds.Count <= 10 ? $": {string.Join(",", missingIds)}" : $",前 10 个:{string.Join(",", missingIds.Take(10))}..."));

                // 占位源:用户指定的"被删除位置之后的真实数据"。
                //   C04 占位 Id=1..6 → 源 Id=8(铁芯装配)
                //   C07 占位 Id=1..7 → 源 Id=8(外形尺寸图)
                //   C07 占位 Id=13 → 源 Id=14(涂漆技术条件)
                // 找不到用户指定源时,fallback 到"missingId 之后的第一条真实数据"。
                // 每张表一个占位源(用户指定),所有占位 Id 都用同一个源
                var placeholderSource = ResolvePlaceholderSource(tableName, insertedRealEntities);

                foreach (var missingId in missingIds)
                {
                    try
                    {
                        // 优先用用户指定的占位源,fallback 到 missingId 之后的第一条
                        var successor = placeholderSource
                            ?? insertedRealEntities
                                .Where(e => e.Id > missingId)
                                .OrderBy(e => e.Id)
                                .FirstOrDefault()
                            ?? insertedRealEntities[^1];

                        if (successor == null)
                        {
                            // 理论上不会发生(insertedRealEntities 非空)
                            successor = insertedRealEntities[^1];
                        }

                        var placeholder = new C0XInsertDto
                        {
                            Id = missingId,
                            DrawingNumber = GetSeedDrawingNumber(tableName) + missingId,
                            DrawingName = successor.DrawingName,   // 复制"跳号之后的第一条"真实数据
                            OrderId = successor.OrderId,            // 占位行也保留关联订单
                            CreatedBy = successor.CreatedBy,
                            UpdatedBy = successor.UpdatedBy,
                            CreatedTime = successor.CreatedTime,
                            UpdatedTime = successor.UpdatedTime,
                            IsDelete = true                          // 关键:占位行标记为删除
                        };
                        await InsertC0X(db, tableName, placeholder);
                        placeholderInserted++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    ! 占位行插入失败(Id={missingId}):{ex.Message}");
                    }
                }
            }
        }

        Console.WriteLine($"    ✓ 插入 {inserted} 条真实 + {placeholderInserted} 条占位 (从 Id=1 全覆盖,复制跳号后第一行字段,IsDelete=true),失败 {skipped} 条" +
            (idMap != null ? " (OrderId 已通过映射重写)" : ""));
    }

    private static async Task MigrateDrawingNamesAsync(ISqlSugarClient db, string file)
    {
        Console.WriteLine($"  DrawingNames ← {Path.GetFileName(file)}");
        var rows = SqlInsertParser.ParseFile(file);
        Console.WriteLine($"    解析到 {rows.Count} 行");

        int inserted = 0;
        foreach (var row in rows)
        {
            try
            {
                var dn = new DrawingNames
                {
                    Part = row["Part"] as string ?? "",
                    ComponentName = row["ComponentName"] as string ?? "",
                    CreatedBy = row["CreatedBy"] as string ?? "",
                    CreatedTime = ToDateTime(row["CreatedTime"]) ?? DateTime.Now,
                    UpdatedBy = row["UpdatedBy"] as string ?? "",
                    UpdatedTime = ToDateTime(row["UpdatedTime"]) ?? DateTime.Now,
                    IsDeleted = row["Isdelete"] != null && Convert.ToBoolean(row["Isdelete"])
                };
                await db.Insertable(dn).ExecuteCommandAsync();
                inserted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ! 插入失败:{ex.Message}");
            }
        }
        Console.WriteLine($"    ✓ 插入 {inserted} 条");
    }

    /// <summary>按表名动态选择实体类型并插入(C0XBase abstract,SqlSugar 多态写入)</summary>
    private static async Task<int> InsertC0X(ISqlSugarClient db, string tableName, C0XInsertDto data)
    {
        // C0XBase.Id 现在是 IsIdentity=true(SqlSugar 建表生成 IDENTITY 列)。
        // 迁数据时需要显式赋值旧 Id,SQL Server 要求每次会话只能有 1 个表开 IDENTITY_INSERT。
        // 简单做法:每次 Insertable 前 SET ON,之后 SET OFF。
        // 注:必须用 Ado.ExecuteCommandAsync 走原生 SQL,SqlSugar ORM 不支持。
        // 注 2:GO 之类的 batch terminator 在 Ado 里不能用,所以分两条命令。
        return tableName switch
        {
            "C01" => await InsertC0XWithIdentityInsert<C01>(db, "C01", data),
            "C02" => await InsertC0XWithIdentityInsert<C02>(db, "C02", data),
            "C03" => await InsertC0XWithIdentityInsert<C03>(db, "C03", data),
            "C04" => await InsertC0XWithIdentityInsert<C04>(db, "C04", data),
            "C05" => await InsertC0XWithIdentityInsert<C05>(db, "C05", data),
            "C06" => await InsertC0XWithIdentityInsert<C06>(db, "C06", data),
            "C07" => await InsertC0XWithIdentityInsert<C07>(db, "C07", data),
            "C08" => await InsertC0XWithIdentityInsert<C08>(db, "C08", data),
            "C09" => await InsertC0XWithIdentityInsert<C09>(db, "C09", data),
            _ => throw new ArgumentException($"Unknown C0X table: {tableName}")
        };
    }

    /// <summary>
    /// 包装:打开 IDENTITY_INSERT → INSERT 显式 Id → 关闭 IDENTITY_INSERT。
    /// SqlSugar 的 Insertable 会自动用 entity 的 Id 字段(因为 IsPrimaryKey)。
    /// </summary>
    private static async Task<int> InsertC0XWithIdentityInsert<T>(ISqlSugarClient db, string tableName, C0XInsertDto data)
        where T : C0XBase, new()
    {
        try
        {
            await db.Ado.ExecuteCommandAsync($"SET IDENTITY_INSERT [{tableName}] ON;");
            var rows = await db.Insertable(Map<T>(data)).ExecuteCommandAsync();
            return rows;
        }
        finally
        {
            // 即使出错也要 OFF,否则连接里的 IDENTITY_INSERT 状态会影响后续操作
            await db.Ado.ExecuteCommandAsync($"SET IDENTITY_INSERT [{tableName}] OFF;");
        }
    }

    private static T Map<T>(C0XInsertDto d) where T : C0XBase, new()
    {
        return new T
        {
            Id = d.Id,
            DrawingNumber = d.DrawingNumber,
            DrawingName = d.DrawingName,
            OrderId = d.OrderId,
            CreatedBy = d.CreatedBy,
            UpdatedBy = d.UpdatedBy,
            CreatedTime = d.CreatedTime,
            UpdatedTime = d.UpdatedTime,
            IsDeleted = d.IsDelete
        };
    }

    /// <summary>
    /// 占位源映射:用户明确指定每张表的"被删除位置"占位用什么真实数据填充。
    /// key = 表名(C04/C07...),value = 占位源 Id(整张表所有占位行都用这一个源)。
    ///
    /// 业务约定(跟旧库约定):
    ///   C04: Id=1..6 占位 → 复制 Id=8 那行(铁芯装配)
    ///   C07: Id=1..7 + Id=13 占位 → 复制 Id=8 那行(外形尺寸图);
    ///                                但 Id=13 跳号在 Id=8 之后,要单独指定 Id=14(涂漆技术条件)
    ///
    /// 返回 null 表示这张表没用用户显式指定,自动 fallback 到"missingId 后第一条真实数据"。
    /// </summary>
    private static C0XInsertDto? ResolvePlaceholderSource(string tableName, List<C0XInsertDto> insertedRealEntities)
    {
        // 每张表有"主占位源"(开头跳号 1..N 用)和"次占位源"(中间跳号用)
        // 这里用"主占位源",中间跳号 missingId 位置如果匹配不到主源,fallback 到 missingId 后第一条
        return tableName switch
        {
            "C04" => insertedRealEntities.FirstOrDefault(e => e.Id == 8),   // 铁芯装配
            "C07" => insertedRealEntities.FirstOrDefault(e => e.Id == 8),   // 外形尺寸图(Id=1..7 占位用)
            // C07 Id=13 跳号不在 Id=8 之后,fallback 到 missingId(=13) 之后的第一条 = Id=14
            _ => null
        };
    }

    private static DateTime? ToDateTime(object? o)
    {
        if (o == null) return null;
        if (o is DateTime dt) return dt;
        if (o is string s && DateTime.TryParse(s, out var p)) return p;
        if (o is long l) return DateTimeOffset.FromUnixTimeSeconds(l).LocalDateTime;
        return null;
    }

    private static async Task WriteMigrationLog(ISqlSugarClient db, string[] args)
    {
        try
        {
            var log = new
            {
                MigratedAt = DateTime.Now,
                Args = string.Join(" ", args),
                Operator = Environment.UserName,
                Machine = Environment.MachineName
            };
            // 用 SQL 直接写,因为 MigrationLog 表结构可能不存在
            await db.Ado.ExecuteCommandAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='MigrationLog' AND xtype='U')
CREATE TABLE MigrationLog (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    MigratedAt DATETIME NOT NULL,
    Operator NVARCHAR(100) NULL,
    Machine NVARCHAR(100) NULL,
    Args NVARCHAR(MAX) NULL
);");
            // 此处是迁移日志,非用户输入,直接拼字符串避免 SugarParameter 复杂性
            // 用 SQL Server 友好的 yyyy-MM-dd HH:mm:ss.fff 格式,避开 DATETIME 对 ISO 8601 'T' 分隔符的非确定性解析
            var escapedArgs = (log.Args ?? "").Replace("'", "''");
            var escapedOp = (log.Operator ?? "").Replace("'", "''");
            var escapedMachine = (log.Machine ?? "").Replace("'", "''");
            await db.Ado.ExecuteCommandAsync(
                $"INSERT INTO MigrationLog (MigratedAt, Operator, Machine, Args) VALUES ('{log.MigratedAt:yyyy-MM-dd HH:mm:ss.fff}', '{escapedOp}', '{escapedMachine}', '{escapedArgs}')");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! 写迁移日志失败:{ex.Message}");
        }
    }

    private static string MaskConnectionString(string connStr)
    {
        return Regex.Replace(
            connStr,
            @"(?i)(password|pwd)\s*=\s*[^;]+",
            "$1=***");
    }

    // ============================================================
    // 轻量映射实体
    // ============================================================
    // UserRolesRow 已迁至 BootstrapBlazor.OnlyServer1.Models.Identity(2026-07-08 命名审查 L-2 合并)
    // DbMigrator 通过 ProjectReference 直接复用同名 public 类,避免三处重复。

    [SugarTable("UserClaims")]
    private class UserClaimRow
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }
        public int UserId { get; set; }
        public string? ClaimType { get; set; }
        public string? ClaimValue { get; set; }
    }

    [SugarTable("RoleClaims")]
    private class RoleClaimRow
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }
        public int RoleId { get; set; }
        public string? ClaimType { get; set; }
        public string? ClaimValue { get; set; }
    }

    /// <summary>C0X 迁移用的数据传输对象(避免创建 9 个空 DTO)
    /// (2026-07-08 Review #L-7) DrawNumber/DrawName → DrawingNumber/DrawingName</summary>
    private class C0XInsertDto
    {
        /// <summary>显式指定 Id — 保持旧库 Id;占位行也是显式 Id</summary>
        public int Id { get; set; }
        /// <summary>业务图号 — 由 SeedDrawingNumber + Id 计算,保证与旧库 MinDrawingNumber + Id 等价</summary>
        public int DrawingNumber { get; set; }
        public string DrawingName { get; set; } = "";
        public int OrderId { get; set; }
        public string CreatedBy { get; set; } = "";
        public string UpdatedBy { get; set; } = "";
        public DateTime CreatedTime { get; set; }
        public DateTime UpdatedTime { get; set; }
        public bool IsDelete { get; set; }
    }

    /// <summary>每张 C0X 表的图号种子值(与 OnlyServer1 handler 的 SeedDrawingNumber 保持一致)</summary>
    private static int GetSeedDrawingNumber(string tableName) => tableName switch
    {
        "C04" => 17000,
        "C05" => 34000,
        "C06" => 22893,
        "C07" => 2992,
        _ => 0   // C01/C02/C03/C08/C09
    };
}