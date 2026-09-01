using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using System.Configuration;

namespace BootstrapBlazor.OrderManagementSetup;

/// <summary>
/// 「订单管理」功能的一次性安装程序(2026-08-10)。
/// (2026-08-13) 扩展:订单问题汇总(OrderIssues)。
/// (2026-08-17) 重构:从原 OrderMDDetail 单表拆成 3 张业务表
///   - OrderInfo              —— 订单项目信息
///   - OrderDesignPhases      —— 一次二次人员 + 时间
///   - OrderImportantComponents —— 重要组件
/// + OrderIssues(问题汇总,已存在)
/// + 6 个新权限码(3 张表各 Read/Edit)
///
/// 为什么不放进 BootstrapBlazor.DbMigrator:
///   DbMigrator 是已上线系统的建表 + 历史数据迁移工具,它的 --schema / --seed / --migrate
///   会遍历全部业务表、读 Order.sql / C0X.sql 等历史 SQL 文件重灌数据。
///   在生产库上为新表去跑它,风险面远大于收益。
///   所以订单管理的建表/索引/权限码单独成一个进程,只碰它自己的东西。
///
/// 全部操作幂等,可反复执行。
///
/// 用法:
///   # 只读检查当前状态(建议先跑这个)
///   dotnet run --project BootstrapBlazor.OrderManagementSetup -- --check
///
///   # 执行安装
///   dotnet run --project BootstrapBlazor.OrderManagementSetup -- --install
///
///   # 生产库安装(要求输入 YES 确认)
///   dotnet run --project BootstrapBlazor.OrderManagementSetup -- --install --production
///
/// 退出码:0 成功 / 1 参数或配置错误 / 2 数据库连接失败 / 3 执行失败
/// </summary>
public static class Program
{
    private const string OrderInfoTable                = "OrderInfo";
    private const string OrderInfoIndex                = "IX_OrderInfo_OrderId";
    private const string OrderDesignPhasesTable        = "OrderDesignPhases";
    private const string OrderDesignPhasesIndex        = "IX_OrderDesignPhases_OrderId";
    private const string OrderImportantComponentsTable = "OrderImportantComponents";
    private const string OrderImportantComponentsIndex = "IX_OrderImportantComponents_OrderId";

    private const string OrderIssuesTable = "OrderIssues";
    private const string OrderIssuesIndex = "IX_OrderIssues_OrderId_Active";

    /// <summary>
    /// (2026-08-17 重构)本功能引入的权限码:
    ///   3 张业务表 × {Read, Edit} = 6 个。
    ///   - Edit 已足够"清空内容"语义,故没有 Delete。
    ///   - OrderIssue.* 3 个保留(早期版本引入)。
    /// 与 BootstrapBlazor.OnlyServer1/Models/Identity/PermissionCodes.cs 一一对应。
    /// (2026-09-01) 分接开关(TapChanger)厂家权限码已迁到 BootstrapBlazor.TapChangerSetup 项目负责。
    /// </summary>
    private static readonly Permissions[] _permissionSeeds =
    {
        new() { Code = PermissionCodes.OrderInfoRead,                    Name = "查看订单项目信息",     Category = "订单管理", SortOrder = 1, IsActive = true },
        new() { Code = PermissionCodes.OrderInfoEdit,                    Name = "编辑订单项目信息",     Category = "订单管理", SortOrder = 2, IsActive = true },
        new() { Code = PermissionCodes.OrderDesignPhasesRead,            Name = "查看一次二次人员时间", Category = "订单管理", SortOrder = 3, IsActive = true },
        new() { Code = PermissionCodes.OrderDesignPhasesEdit,            Name = "编辑一次二次人员时间", Category = "订单管理", SortOrder = 4, IsActive = true },
        new() { Code = PermissionCodes.OrderImportantComponentsRead,     Name = "查看重要组件",         Category = "订单管理", SortOrder = 5, IsActive = true },
        new() { Code = PermissionCodes.OrderImportantComponentsEdit,     Name = "编辑重要组件",         Category = "订单管理", SortOrder = 6, IsActive = true },
        new() { Code = PermissionCodes.OrderIssueRead,                   Name = "查看订单问题汇总",     Category = "订单管理", SortOrder = 7, IsActive = true },
        new() { Code = PermissionCodes.OrderIssueAdd,                    Name = "添加订单问题",         Category = "订单管理", SortOrder = 8, IsActive = true },
        new() { Code = PermissionCodes.OrderIssueEditStatus,             Name = "修改订单问题状态",     Category = "订单管理", SortOrder = 9, IsActive = true },
    };

    /// <summary>
    /// 角色 → 权限码。Operator 与 Admin 一样可完整操作本页(用户 2026-08-10 明确要求),
    /// Viewer 只读。
    /// (2026-09-01) 分接开关厂家(TapChangerManufacturer.*)已迁到 BootstrapBlazor.TapChangerSetup 项目负责。
    /// </summary>
    private static readonly Dictionary<string, string[]> _roleMapping = new()
    {
        ["ADMIN"] = new[] {
            PermissionCodes.OrderInfoRead, PermissionCodes.OrderInfoEdit,
            PermissionCodes.OrderDesignPhasesRead, PermissionCodes.OrderDesignPhasesEdit,
            PermissionCodes.OrderImportantComponentsRead, PermissionCodes.OrderImportantComponentsEdit,
            PermissionCodes.OrderIssueRead, PermissionCodes.OrderIssueAdd, PermissionCodes.OrderIssueEditStatus,
        },
        ["OPERATOR"] = new[] {
            PermissionCodes.OrderInfoRead, PermissionCodes.OrderInfoEdit,
            PermissionCodes.OrderDesignPhasesRead, PermissionCodes.OrderDesignPhasesEdit,
            PermissionCodes.OrderImportantComponentsRead, PermissionCodes.OrderImportantComponentsEdit,
            PermissionCodes.OrderIssueRead, PermissionCodes.OrderIssueAdd, PermissionCodes.OrderIssueEditStatus,
        },
        ["VIEWER"] = new[] {
            PermissionCodes.OrderInfoRead,
            PermissionCodes.OrderDesignPhasesRead,
            PermissionCodes.OrderImportantComponentsRead,
            PermissionCodes.OrderIssueRead,
        },
    };

    public static async Task<int> Main(string[] args)
    {
        bool doCheck = args.Contains("--check");
        bool doInstall = args.Contains("--install");
        bool doProduction = args.Contains("--production");

        Console.WriteLine("============================================================");
        Console.WriteLine(" 订单管理(项目信息 / 一次二次人员时间 / 重要组件 / 问题汇总)安装程序");
        Console.WriteLine("============================================================");

        if (!doCheck && !doInstall)
        {
            Console.Error.WriteLine("错误:必须指定 --check(只读检查)或 --install(执行安装)");
            Console.Error.WriteLine("用法:");
            Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.OrderManagementSetup -- --check");
            Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.OrderManagementSetup -- --install               # 测试环境直接建表");
            Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.OrderManagementSetup -- --install --production   # 生产环境(需输 YES)");
            return 1;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        //生产环境
        //var connStr = config.GetConnectionString("sqlServerConnection");
        //生产测试环境
        var connStr = config.GetConnectionString("sqlServerConnectionTest");
        //测试环境
        //var connStr = config.GetConnectionString("sqlServerConnectionStringOffice");

        if(string.IsNullOrWhiteSpace(connStr))
        {
            Console.Error.WriteLine("错误:appsettings.json 中未配置 ConnectionStrings.sqlServerConnection");
            return 1;
        }

        Console.WriteLine($" 连接字符串: {MaskConnectionString(connStr)}");
        Console.WriteLine($" 模式: check={doCheck}, install={doInstall}, production={doProduction}");
        Console.WriteLine();

        var db = new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = connStr,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

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

        Console.WriteLine();
        Console.WriteLine(">>> 当前状态");
        await ReportStateAsync(db);

        if (!doInstall)
        {
            Console.WriteLine();
            Console.WriteLine(" 只读检查完成,未做任何修改。");
            return 0;
        }

        if (doProduction)
        {
            Console.WriteLine();
            Console.WriteLine("⚠⚠⚠ 生产模式 ⚠⚠⚠");
            Console.WriteLine("即将对生产数据库执行以下操作(均为新增,不删除、不修改已有数据):");
            Console.WriteLine($"  - 创建表 {OrderInfoTable}(已存在则跳过)");
            Console.WriteLine($"  - 创建表 {OrderDesignPhasesTable}(已存在则跳过)");
            Console.WriteLine($"  - 创建表 {OrderImportantComponentsTable}(已存在则跳过)");
            Console.WriteLine($"  - (2026-08-13) 创建表 {OrderIssuesTable}(已存在则跳过)");
            Console.WriteLine($"  - (2026-08-14) 给 {OrderIssuesTable} 增量补列 ClosedBy / ClosedTime / Resolution(已存在则跳过)");
            Console.WriteLine($"  - 创建 4 个过滤唯一索引 {OrderInfoIndex} / {OrderDesignPhasesIndex} / {OrderImportantComponentsIndex} / {OrderIssuesIndex}(已存在则跳过)");
            Console.WriteLine($"  - 灌入 {_permissionSeeds.Length} 个订单管理权限码(已存在则跳过)");
            Console.WriteLine("  - 把权限码绑给 ADMIN / OPERATOR / VIEWER(已绑定则跳过)");
            Console.Write(" 确认? 输入 YES 继续: ");
            var confirm = Console.ReadLine();
            if (!string.Equals(confirm, "YES", StringComparison.Ordinal))
            {
                Console.WriteLine(" 已取消");
                return 0;
            }
        }

        try
        {
            Console.WriteLine();
            Console.WriteLine(">>> 1/4 建表");
            await CreateOrderInfoTableAsync(db);
            await CreateOrderDesignPhasesTableAsync(db);
            await CreateOrderImportantComponentsTableAsync(db);
            await CreateOrderIssuesTableAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 2/4 建索引");
            await CreateOrderInfoIndexAsync(db);
            await CreateOrderDesignPhasesIndexAsync(db);
            await CreateOrderImportantComponentsIndexAsync(db);
            await CreateOrderIssuesIndexAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 3/4 权限码");
            await SeedPermissionsAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 4/4 角色绑定");
            await BindRolePermissionsAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 安装后状态");
            await ReportStateAsync(db);

            Console.WriteLine();
            Console.WriteLine("✓ 订单管理安装完成。");
            Console.WriteLine();
            Console.WriteLine("  注意:登录态里的权限 Claim 有 5 分钟缓存(SecurityStampValidator),");
            Console.WriteLine("        已登录用户需要等约 5 分钟或重新登录,页面按钮才会出现。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"✗ 安装失败:{ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            return 3;
        }
    }

    // ============================================================
    // 建表(全部幂等)
    // ============================================================

    private static async Task CreateOrderInfoTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, OrderInfoTable))
        {
            Console.WriteLine($"  - {OrderInfoTable}:已存在,跳过");
            return;
        }
        await Task.Run(() => db.CodeFirst.InitTables<OrderInfo>());
        Console.WriteLine($"  ✓ {OrderInfoTable}:已创建");
    }

    private static async Task CreateOrderDesignPhasesTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, OrderDesignPhasesTable))
        {
            Console.WriteLine($"  - {OrderDesignPhasesTable}:已存在,跳过");
            return;
        }
        await Task.Run(() => db.CodeFirst.InitTables<OrderDesignPhases>());
        Console.WriteLine($"  ✓ {OrderDesignPhasesTable}:已创建");
    }

    private static async Task CreateOrderImportantComponentsTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, OrderImportantComponentsTable))
        {
            Console.WriteLine($"  - {OrderImportantComponentsTable}:已存在,跳过");
            return;
        }
        await Task.Run(() => db.CodeFirst.InitTables<OrderImportantComponents>());
        Console.WriteLine($"  ✓ {OrderImportantComponentsTable}:已创建");
    }

    private static async Task CreateOrderIssuesTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, OrderIssuesTable))
        {
            Console.WriteLine($"  - {OrderIssuesTable}:已存在,跳过");
        }
        else
        {
            await Task.Run(() => db.CodeFirst.InitTables<OrderIssues>());
            Console.WriteLine($"  ✓ {OrderIssuesTable}:已创建");
        }

        // (2026-08-14) 已存在的表也补一遍新列(ClosedBy / ClosedTime)。
        // SqlSugar 的 CodeFirst.InitTables 不会给已有表加列,这里用 raw SQL 增量添加,幂等可重跑。
        await EnsureOrderIssuesColumnsAsync(db);
    }

    /// <summary>
    /// (2026-08-14) 给 OrderIssues 表增量补 ClosedBy / ClosedTime / Resolution 列(可空)。
    /// 已存在则跳过。适用于"代码已升级、但旧表还没 ALTER"的场景。
    /// </summary>
    private static async Task EnsureOrderIssuesColumnsAsync(ISqlSugarClient db)
    {
        await AddColumnIfNotExistsAsync(db, OrderIssuesTable, "ClosedBy",  "NVARCHAR(100) NULL");
        await AddColumnIfNotExistsAsync(db, OrderIssuesTable, "ClosedTime", "DATETIME NULL");
        await AddColumnIfNotExistsAsync(db, OrderIssuesTable, "Resolution", "NVARCHAR(MAX) NULL");
    }

    private static async Task AddColumnIfNotExistsAsync(ISqlSugarClient db, string tableName, string columnName, string columnDef)
    {
        var exists = await db.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[{tableName}]') AND name = '{columnName}'");
        if (exists > 0)
        {
            Console.WriteLine($"    - 列 {columnName}:已存在,跳过");
            return;
        }
        await db.Ado.ExecuteCommandAsync($"ALTER TABLE {tableName} ADD {columnName} {columnDef}");
        Console.WriteLine($"    + 列 {columnName}:已添加");
    }

    // ============================================================
    // 索引(全部幂等,3 张业务表 + OrderIssues)
    // ============================================================

    private static async Task CreateOrderInfoIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{OrderInfoIndex}' AND object_id = OBJECT_ID('{OrderInfoTable}'))
BEGIN
    CREATE UNIQUE INDEX {OrderInfoIndex} ON {OrderInfoTable} (OrderId) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 过滤唯一索引 {OrderInfoIndex} (WHERE IsDeleted = 0) 已就绪");
    }

    private static async Task CreateOrderDesignPhasesIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{OrderDesignPhasesIndex}' AND object_id = OBJECT_ID('{OrderDesignPhasesTable}'))
BEGIN
    CREATE UNIQUE INDEX {OrderDesignPhasesIndex} ON {OrderDesignPhasesTable} (OrderId) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 过滤唯一索引 {OrderDesignPhasesIndex} (WHERE IsDeleted = 0) 已就绪");
    }

    private static async Task CreateOrderImportantComponentsIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{OrderImportantComponentsIndex}' AND object_id = OBJECT_ID('{OrderImportantComponentsTable}'))
BEGIN
    CREATE UNIQUE INDEX {OrderImportantComponentsIndex} ON {OrderImportantComponentsTable} (OrderId) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 过滤唯一索引 {OrderImportantComponentsIndex} (WHERE IsDeleted = 0) 已就绪");
    }

    /// <summary>
    /// (2026-08-13) OrderIssues(OrderId, IsDeleted=0) 普通索引(非唯一):同一订单可有多条问题。
    /// 加速"按订单查问题列表"的常见查询。
    /// </summary>
    private static async Task CreateOrderIssuesIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{OrderIssuesIndex}' AND object_id = OBJECT_ID('{OrderIssuesTable}'))
BEGIN
    CREATE INDEX {OrderIssuesIndex} ON {OrderIssuesTable} (OrderId) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 索引 {OrderIssuesIndex} (WHERE IsDeleted = 0) 已就绪");
    }

    // ============================================================
    // 灌权限码 + 角色绑定
    // ============================================================

    /// <summary>
    /// 灌入 9 个权限码。
    ///
    /// 流程:
    ///   1. 清理老的 OrderMDDetail.* 码(2026-08-17 重构前使用,本次清理)
    ///      —— 关联 RolePermissions 一起删;若有自定义角色分配老码,也会一并清理。
    ///      仅匹配我们列出的 3 个老 Code,绝不 LIKE 模糊匹配,防误删其它模块权限。
    ///      仅匹配 OrderDetail.* 的 3 个老权限码(2026-08-12 第一次重命名时遗留)。
    ///   2. 插入 Code 不存在的新码(已存在的不动,不覆盖 Name/Category 的人工调整)。
    /// </summary>
    private static async Task SeedPermissionsAsync(ISqlSugarClient db)
    {
        // 1. 清理老的 OrderMDDetail.* + OrderDetail.* 码
        var oldCodes = new[] {
            "OrderMDDetail.Read", "OrderMDDetail.Edit", "OrderMDDetail.Delete",
            "OrderDetail.Read", "OrderDetail.Edit", "OrderDetail.Delete",
        };
        var oldPerms = await db.Queryable<Permissions>()
            .Where(p => oldCodes.Contains(p.Code))
            .ToListAsync();
        if (oldPerms.Count > 0)
        {
            var oldIds = oldPerms.Select(p => p.Id).ToList();
            var deletedRolePerms = await db.Deleteable<RolePermissions>()
                .Where(rp => oldIds.Contains(rp.PermissionId))
                .ExecuteCommandAsync();
            var deletedPerms = await db.Deleteable<Permissions>()
                .Where(p => oldIds.Contains(p.Id))
                .ExecuteCommandAsync();
            Console.WriteLine($"  ! 清理老的 OrderMDDetail.* / OrderDetail.* 权限码:Permissions {deletedPerms} 个 + RolePermissions {deletedRolePerms} 条关联");
        }

        // 2. 插入 Code 不存在的新码
        var existingCodes = (await db.Queryable<Permissions>().ToListAsync())
            .Select(p => p.Code)
            .ToHashSet();

        var toInsert = _permissionSeeds.Where(p => !existingCodes.Contains(p.Code)).ToList();
        if (toInsert.Count == 0)
        {
            Console.WriteLine($"  - {_permissionSeeds.Length} 个订单管理权限码均已存在,跳过");
            return;
        }

        await db.Insertable(toInsert).ExecuteCommandAsync();
        Console.WriteLine($"  ✓ 灌入 {toInsert.Count} 个权限码:{string.Join(", ", toInsert.Select(p => p.Code))}");
    }

    /// <summary>
    /// 增量补绑:逐个 code 检查 RolePermissions 里有没有,没有才插。
    ///
    /// 特意不照抄 DbMigrator 里 Bind*PermissionsAsync 的"该角色已有权限关联 → 整体跳过"守卫。
    /// 那个守卫是为了不覆盖运营在 /roles 页的手工调整,但在**已上线的库**上它意味着
    /// 新权限码永远绑不上去 —— 所有人的按钮都是灰的。
    /// 这里只增不删,对手工调整同样安全。
    /// </summary>
    private static async Task BindRolePermissionsAsync(ISqlSugarClient db)
    {
        var byCode = (await db.Queryable<Permissions>().ToListAsync())
            .GroupBy(p => p.Code)
            .ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var (normalizedName, codes) in _roleMapping)
        {
            var role = await db.Queryable<Roles>()
                .Where(r => r.NormalizedName == normalizedName)
                .FirstAsync();
            if (role == null)
            {
                Console.WriteLine($"  ! {normalizedName} 角色不存在,跳过");
                continue;
            }

            var existingPermIds = (await db.Queryable<RolePermissions>()
                .Where(rp => rp.RoleId == role.Id)
                .ToListAsync())
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            var rows = codes
                .Where(c => byCode.ContainsKey(c))
                .Select(c => byCode[c])
                .Where(pid => !existingPermIds.Contains(pid))
                .Select(pid => new RolePermissions { RoleId = role.Id, PermissionId = pid })
                .ToList();

            if (rows.Count == 0)
            {
                Console.WriteLine($"  - {normalizedName}:订单管理权限已齐全,无需补绑");
                continue;
            }

            await db.Insertable(rows).ExecuteCommandAsync();
            Console.WriteLine($"  ✓ {normalizedName}:补绑 {rows.Count} 个权限码");
        }
    }

    // ============================================================
    // 状态报告(只读)
    // ============================================================

    private static async Task ReportStateAsync(ISqlSugarClient db)
    {
        await ReportTableStateAsync(db, OrderInfoTable, OrderInfoIndex);
        await ReportTableStateAsync(db, OrderDesignPhasesTable, OrderDesignPhasesIndex);
        await ReportTableStateAsync(db, OrderImportantComponentsTable, OrderImportantComponentsIndex);
        await ReportTableStateAsync(db, OrderIssuesTable, OrderIssuesIndex);

        var codes = _permissionSeeds.Select(p => p.Code).ToList();
        var perms = (await db.Queryable<Permissions>().ToListAsync())
            .Where(p => codes.Contains(p.Code))
            .ToList();
        Console.WriteLine($"  权限码: {perms.Count}/{codes.Count} 已存在");

        if (perms.Count > 0)
        {
            var permIds = perms.Select(p => p.Id).ToHashSet();
            var roles = await db.Queryable<Roles>().ToListAsync();
            var allRolePerms = await db.Queryable<RolePermissions>().ToListAsync();

            foreach (var (normalizedName, expectedCodes) in _roleMapping)
            {
                var role = roles.FirstOrDefault(r => r.NormalizedName == normalizedName);
                if (role == null)
                {
                    Console.WriteLine($"    {normalizedName}: 角色不存在");
                    continue;
                }
                var bound = allRolePerms.Count(rp => rp.RoleId == role.Id && permIds.Contains(rp.PermissionId));
                Console.WriteLine($"    {normalizedName}: 已绑定 {bound}/{expectedCodes.Length}");
            }
        }
    }

    // ============================================================
    // 工具
    // ============================================================

    /// <summary>用 sys.objects 判断表是否存在,与 DbMigrator.TableExists 保持一致。</summary>
    private static async Task<bool> TableExistsAsync(ISqlSugarClient db, string tableName)
    {
        var count = await db.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[{tableName}]') AND type in (N'U')");
        return count > 0;
    }

    /// <summary>(2026-08-13) 单表的状态报告:存在性 + 行数 + 索引存在性 + (OrderIssues) 关键列存在性。</summary>
    private static async Task ReportTableStateAsync(ISqlSugarClient db, string tableName, string indexName)
    {
        var tableExists = await TableExistsAsync(db, tableName);
        Console.WriteLine($"  表 {tableName}: {(tableExists ? "存在" : "不存在")}");

        if (tableExists)
        {
            var rowCount = await db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {tableName}");
            Console.WriteLine($"    行数: {rowCount}");

            var indexExists = await db.Ado.GetIntAsync(
                $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('{tableName}')") > 0;
            Console.WriteLine($"    索引 {indexName}: {(indexExists ? "存在" : "不存在")}");

            // (2026-08-14) 仅 OrderIssues 表报 ClosedBy / ClosedTime / Resolution 关键列,
            // 这几列缺失会导致 EF/SqlSugar 读这些字段时抛 "无效列名"。
            if (tableName == OrderIssuesTable)
            {
                var closedBy   = await ColumnExistsAsync(db, tableName, "ClosedBy");
                var closedTime = await ColumnExistsAsync(db, tableName, "ClosedTime");
                var resolution = await ColumnExistsAsync(db, tableName, "Resolution");
                Console.WriteLine($"    列 ClosedBy:  {(closedBy   ? "存在" : "不存在")}");
                Console.WriteLine($"    列 ClosedTime:{(closedTime ? "存在" : "不存在")}");
                Console.WriteLine($"    列 Resolution:{(resolution ? "存在" : "不存在")}");
            }
        }
    }

    /// <summary>(2026-08-14) 判断列是否存在。</summary>
    private static async Task<bool> ColumnExistsAsync(ISqlSugarClient db, string tableName, string columnName)
    {
        var count = await db.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[{tableName}]') AND name = '{columnName}'");
        return count > 0;
    }

    /// <summary>日志里隐藏密码。</summary>
    private static string MaskConnectionString(string connStr)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            connStr, @"(password|pwd)\s*=\s*[^;]*", "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}