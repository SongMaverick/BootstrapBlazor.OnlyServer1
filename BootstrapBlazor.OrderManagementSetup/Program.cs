using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using System.Configuration;

namespace BootstrapBlazor.OrderManagementSetup;

/// <summary>
/// 「订单管理」功能的一次性安装程序(2026-08-10)。
///
/// 为什么不放进 BootstrapBlazor.DbMigrator:
///   DbMigrator 是已上线系统的建表 + 历史数据迁移工具,它的 --schema / --seed / --migrate
///   会遍历全部业务表、读 Order.sql / C0X.sql 等历史 SQL 文件重灌数据。
///   在生产库上为了一张新表去跑它,风险面远大于收益。
///   所以订单管理的建表/索引/权限码单独成一个进程,只碰它自己的东西:
///     - OrderMDDetail 表(不存在才建)
///     - IX_OrderMDDetail_OrderId 过滤唯一索引
///     - Permissions 表里 3 个 OrderMDDetail.* 权限码
///     - RolePermissions 里这 3 个码到 ADMIN / OPERATOR / VIEWER 的关联
///   绝不 DROP、不改任何已有表的结构、不动任何业务数据。
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
    private const string TableName = "OrderMDDetail";
    private const string IndexName = "IX_OrderMDDetail_OrderId";

    /// <summary>
    /// 本功能引入的权限码。明细行随订单存在,不单独新增,故没有 OrderMDDetail.Create。
    /// 与 BootstrapBlazor.OnlyServer1/Models/Identity/PermissionCodes.cs 一一对应。
    /// </summary>
    private static readonly Permissions[] _permissionSeeds =
    {
        new() { Code = PermissionCodes.OrderMDDetailRead,   Name = "查看订单明细", Category = "订单管理", SortOrder = 1, IsActive = true },
        new() { Code = PermissionCodes.OrderMDDetailEdit,   Name = "编辑订单明细", Category = "订单管理", SortOrder = 2, IsActive = true },
        new() { Code = PermissionCodes.OrderMDDetailDelete, Name = "清空订单明细", Category = "订单管理", SortOrder = 3, IsActive = true },
    };

    /// <summary>
    /// 角色 → 权限码。Operator 与 Admin 一样可完整操作本页(用户 2026-08-10 明确要求),
    /// Viewer 只读。
    /// </summary>
    private static readonly Dictionary<string, string[]> _roleMapping = new()
    {
        ["ADMIN"] = new[] { PermissionCodes.OrderMDDetailRead, PermissionCodes.OrderMDDetailEdit, PermissionCodes.OrderMDDetailDelete },
        ["OPERATOR"] = new[] { PermissionCodes.OrderMDDetailRead, PermissionCodes.OrderMDDetailEdit, PermissionCodes.OrderMDDetailDelete },
        ["VIEWER"] = new[] { PermissionCodes.OrderMDDetailRead },
    };

    public static async Task<int> Main(string[] args)
    {
        bool doCheck = args.Contains("--check");
        bool doInstall = args.Contains("--install");
        bool doProduction = args.Contains("--production");

        Console.WriteLine("============================================================");
        Console.WriteLine(" 订单管理(结构设计订单明细)安装程序");
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
        //测试环境
        var connStr = config.GetConnectionString("sqlServerConnectionStringOffice");
        if (string.IsNullOrWhiteSpace(connStr))
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
            Console.WriteLine($"  - 创建表 {TableName}(已存在则跳过)");
            Console.WriteLine($"  - 创建过滤唯一索引 {IndexName}(已存在则跳过)");
            Console.WriteLine("  - 灌入 3 个 OrderMDDetail.* 权限码(已存在则跳过)");
            Console.WriteLine("  - 把这 3 个权限码绑给 ADMIN / OPERATOR / VIEWER(已绑定则跳过)");
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
            Console.WriteLine(">>> 1/3 建表");
            await CreateTableAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 2/3 建索引");
            await CreateIndexAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 3/3 权限码 + 角色绑定");
            await SeedPermissionsAsync(db);
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
    // 步骤实现(全部幂等)
    // ============================================================

    private static async Task CreateTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, TableName))
        {
            Console.WriteLine($"  - {TableName}:已存在,跳过");
            return;
        }

        // CodeFirst 只建这一张表,不会碰其它实体
        await Task.Run(() => db.CodeFirst.InitTables<OrderMDDetail>());
        Console.WriteLine($"  ✓ {TableName}:已创建");
    }

    /// <summary>
    /// OrderMDDetail.OrderId 用 filtered unique index(仅约束 IsDeleted=0 的活跃行):
    /// 保证一个订单最多只有一条"有效"明细;清空明细(软删)后可以重新填写。
    /// 做法与 DbMigrator 里 IX_Orders_OrderNumber 一致。
    /// </summary>
    private static async Task CreateIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{IndexName}' AND object_id = OBJECT_ID('{TableName}'))
BEGIN
    CREATE UNIQUE INDEX {IndexName} ON {TableName} (OrderId) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 过滤唯一索引 {IndexName} (WHERE IsDeleted = 0) 已就绪");
    }

    /// <summary>
    /// 灌入 3 个 OrderMDDetail.* 权限码。
    ///
    /// 流程:
    ///   1. 清理老的 OrderDetail.* 码(2026-08-12 重命名为 OrderMDDetail.*)
    ///      —— 关联 RolePermissions 一起删;若有自定义角色分配老码,也会一并清理。
    ///      仅匹配我们列出的 3 个老 Code,绝不 LIKE 模糊匹配,防误删其它模块权限。
    ///   2. 插入 Code 不存在的新码(已存在的不动,不覆盖 Name/Category 的人工调整)。
    /// </summary>
    private static async Task SeedPermissionsAsync(ISqlSugarClient db)
    {
        // 1. 清理老的 OrderDetail.* 码
        var oldCodes = new[] { "OrderDetail.Read", "OrderDetail.Edit", "OrderDetail.Delete" };
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
            Console.WriteLine($"  ! 清理老的 OrderDetail.* 权限码:Permissions {deletedPerms} 个 + RolePermissions {deletedRolePerms} 条关联");
        }

        // 2. 插入 Code 不存在的新码
        var existingCodes = (await db.Queryable<Permissions>().ToListAsync())
            .Select(p => p.Code)
            .ToHashSet();

        var toInsert = _permissionSeeds.Where(p => !existingCodes.Contains(p.Code)).ToList();
        if (toInsert.Count == 0)
        {
            Console.WriteLine("  - 3 个 OrderMDDetail.* 权限码均已存在,跳过");
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
        var tableExists = await TableExistsAsync(db, TableName);
        Console.WriteLine($"  表 {TableName}: {(tableExists ? "存在" : "不存在")}");

        if (tableExists)
        {
            var rowCount = await db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {TableName}");
            Console.WriteLine($"    行数: {rowCount}");

            var indexExists = await db.Ado.GetIntAsync(
                $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{IndexName}' AND object_id = OBJECT_ID('{TableName}')") > 0;
            Console.WriteLine($"    索引 {IndexName}: {(indexExists ? "存在" : "不存在")}");
        }

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

    /// <summary>日志里隐藏密码。</summary>
    private static string MaskConnectionString(string connStr)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            connStr, @"(password|pwd)\s*=\s*[^;]*", "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
