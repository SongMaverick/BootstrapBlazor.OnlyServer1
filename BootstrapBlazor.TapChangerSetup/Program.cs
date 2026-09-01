using System.Text.RegularExpressions;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.TapChanger;
using Microsoft.Extensions.Configuration;
using SqlSugar;

namespace BootstrapBlazor.TapChangerSetup;

/// <summary>
/// 「分接开关(Tap-Changer)」功能的一次性安装程序(2026-09-01)。
///
/// 为什么单做一个项目(不再挂在 BootstrapBlazor.OrderManagementSetup 下):
///   分接开关(Tap-Changer)是有载调压变压器里的一个独立业务域,
///   跟"订单管理"没有耦合关系,后续还会加 TapChangerModels / TapChangerSpecs 等表。
///   单独一个安装项目,职责清晰:
///     BootstrapBlazor.OrderManagementSetup —— 订单业务(订单明细/问题汇总)
///     BootstrapBlazor.TapChangerSetup     —— 分接开关业务(厂家/规格/选型等)
///   两者互不依赖,可独立安装、独立回滚。
///
/// 全部操作幂等,可反复执行。
///
/// 用法:
///   # 只读检查当前状态(建议先跑这个)
///   dotnet run --project BootstrapBlazor.TapChangerSetup -- --check
///
///   # 执行安装
///   dotnet run --project BootstrapBlazor.TapChangerSetup -- --install
///
///   # 生产库安装(要求输入 YES 确认)
///   dotnet run --project BootstrapBlazor.TapChangerSetup -- --install --production
///
/// 退出码:0 成功 / 1 参数或配置错误 / 2 数据库连接失败 / 3 执行失败
/// </summary>
public static class Program
{
    private const string TapChangerManufacturersTable = "TapChangerManufacturers";
    private const string TapChangerManufacturersIndex = "IX_TapChangerManufacturers_NormalizedName";

    // (2026-09-01) 有载分接开关型号表。FK ManufacturerId → TapChangerManufacturers.Id。
    private const string TapChangerModelsTable = "TapChangerModels";
    private const string TapChangerModelsIndex = "IX_TapChangerModels_Name";

    /// <summary>
    /// 本功能引入的权限码:
    ///   厂家字典 Create / Read / Edit / Delete(2026-09-01)
    /// + 型号字典 Create / Read / Edit / Delete(2026-09-01,FK ManufacturerId)
    /// 与 BootstrapBlazor.OnlyServer1/Models/Identity/PermissionCodes.cs 一一对应。
    /// </summary>
    private static readonly Permissions[] _permissionSeeds =
    {
        new() { Code = PermissionCodes.TapChangerManufacturerCreate, Name = "新增分接开关厂家", Category = "分接开关管理", SortOrder = 10, IsActive = true },
        new() { Code = PermissionCodes.TapChangerManufacturerRead,   Name = "查看分接开关厂家", Category = "分接开关管理", SortOrder = 11, IsActive = true },
        new() { Code = PermissionCodes.TapChangerManufacturerEdit,   Name = "编辑分接开关厂家", Category = "分接开关管理", SortOrder = 12, IsActive = true },
        new() { Code = PermissionCodes.TapChangerManufacturerDelete, Name = "删除分接开关厂家", Category = "分接开关管理", SortOrder = 13, IsActive = true },
        new() { Code = PermissionCodes.TapChangerModelCreate,        Name = "新增分接开关型号", Category = "分接开关管理", SortOrder = 14, IsActive = true },
        new() { Code = PermissionCodes.TapChangerModelRead,          Name = "查看分接开关型号", Category = "分接开关管理", SortOrder = 15, IsActive = true },
        new() { Code = PermissionCodes.TapChangerModelEdit,          Name = "编辑分接开关型号", Category = "分接开关管理", SortOrder = 16, IsActive = true },
        new() { Code = PermissionCodes.TapChangerModelDelete,        Name = "删除分接开关型号", Category = "分接开关管理", SortOrder = 17, IsActive = true },
    };

    /// <summary>
    /// 角色 → 权限码:
    ///   - ADMIN     —— 厂家 + 型号各 4 个(字典的增删改查都由 Admin 接管)
    ///   - OPERATOR  —— 仅 Read(给其它业务表单的下拉项用;Service 端还会强制 Admin 才能写入)
    ///   - VIEWER    —— 仅 Read
    /// </summary>
    private static readonly Dictionary<string, string[]> _roleMapping = new()
    {
        ["ADMIN"] = new[] {
            PermissionCodes.TapChangerManufacturerCreate,
            PermissionCodes.TapChangerManufacturerRead,
            PermissionCodes.TapChangerManufacturerEdit,
            PermissionCodes.TapChangerManufacturerDelete,
            PermissionCodes.TapChangerModelCreate,
            PermissionCodes.TapChangerModelRead,
            PermissionCodes.TapChangerModelEdit,
            PermissionCodes.TapChangerModelDelete,
        },
        ["OPERATOR"] = new[] {
            PermissionCodes.TapChangerManufacturerRead,
            PermissionCodes.TapChangerModelRead,
        },
        ["VIEWER"] = new[] {
            PermissionCodes.TapChangerManufacturerRead,
            PermissionCodes.TapChangerModelRead,
        },
    };

    public static async Task<int> Main(string[] args)
    {
        bool doCheck = args.Contains("--check");
        bool doInstall = args.Contains("--install");
        bool doProduction = args.Contains("--production");

        Console.WriteLine("============================================================");
        Console.WriteLine(" 分接开关(厂家字典)安装程序");
        Console.WriteLine("============================================================");

        if (!doCheck && !doInstall)
        {
            Console.Error.WriteLine("错误:必须指定 --check(只读检查)或 --install(执行安装)");
            Console.Error.WriteLine("用法:");
            Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.TapChangerSetup -- --check");
            Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.TapChangerSetup -- --install               # 测试环境直接建表");
            Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.TapChangerSetup -- --install --production   # 生产环境(需输 YES)");
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
            Console.WriteLine($"  - 创建表 {TapChangerManufacturersTable}(已存在则跳过)");
            Console.WriteLine($"  - (2026-09-01) 创建表 {TapChangerModelsTable}(FK ManufacturerId → Manufacturers,已存在则跳过)");
            Console.WriteLine($"  - 创建过滤唯一索引 {TapChangerManufacturersIndex} / {TapChangerModelsIndex}(已存在则跳过)");
            Console.WriteLine($"  - 灌入 {_permissionSeeds.Length} 个分接开关权限码(厂家 4 + 型号 4,已存在则跳过)");
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
            await CreateTapChangerManufacturersTableAsync(db);
            await CreateTapChangerModelsTableAsync(db);

            Console.WriteLine();
            Console.WriteLine(">>> 2/4 建索引");
            await CreateTapChangerManufacturersIndexAsync(db);
            await CreateTapChangerModelsIndexAsync(db);

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
            Console.WriteLine("✓ 分接开关厂家安装完成。");
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
    // 建表 / 索引(全部幂等)
    // ============================================================

    private static async Task CreateTapChangerManufacturersTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, TapChangerManufacturersTable))
        {
            Console.WriteLine($"  - {TapChangerManufacturersTable}:已存在,跳过");
            return;
        }
        await Task.Run(() => db.CodeFirst.InitTables<TapChangerManufacturer>());
        Console.WriteLine($"  ✓ {TapChangerManufacturersTable}:已创建");
    }

    /// <summary>
    /// (NormalizedName, IsDeleted=0) 过滤唯一索引。
    /// 软删的条目释放 NormalizedName,允许重建同名厂家(沿用 Orders 3 张表 WHERE IsDeleted=0 的同样套路)。
    /// </summary>
    private static async Task CreateTapChangerManufacturersIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{TapChangerManufacturersIndex}' AND object_id = OBJECT_ID('{TapChangerManufacturersTable}'))
BEGIN
    CREATE UNIQUE INDEX {TapChangerManufacturersIndex} ON {TapChangerManufacturersTable} (NormalizedName) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 过滤唯一索引 {TapChangerManufacturersIndex} (WHERE IsDeleted = 0) 已就绪");
    }

    /// <summary>
    /// (2026-09-01) TapChangerModels 表。CodeFirst 幂等建表。
    /// SqlSugar CodeFirst 自动处理 FK ManufacturerId → TapChangerManufacters.Id。
    /// </summary>
    private static async Task CreateTapChangerModelsTableAsync(ISqlSugarClient db)
    {
        if (await TableExistsAsync(db, TapChangerModelsTable))
        {
            Console.WriteLine($"  - {TapChangerModelsTable}:已存在,跳过");
            return;
        }
        await Task.Run(() => db.CodeFirst.InitTables<TapChangerModel>());
        Console.WriteLine($"  ✓ {TapChangerModelsTable}:已创建(FK ManufacturerId → {TapChangerManufacturersTable})");
    }

    /// <summary>
    /// (2026-09-01) TapChangerModels(Name, IsDeleted=0) 过滤唯一索引。
    /// Name 由 Service 在 Save 前按 11 个分量自动拼装,大小写敏感(不做 NormalizedName,
    /// 因为产品型号常用 "II" / "III" / "D" 之类大小写有意义的字符串)。
    /// 软删释放 Name 允许重建。
    /// </summary>
    private static async Task CreateTapChangerModelsIndexAsync(ISqlSugarClient db)
    {
        var sql = $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{TapChangerModelsIndex}' AND object_id = OBJECT_ID('{TapChangerModelsTable}'))
BEGIN
    CREATE UNIQUE INDEX {TapChangerModelsIndex} ON {TapChangerModelsTable} (Name) WHERE IsDeleted = 0;
END";
        await db.Ado.ExecuteCommandAsync(sql);
        Console.WriteLine($"  ✓ 过滤唯一索引 {TapChangerModelsIndex} (WHERE IsDeleted = 0) 已就绪");
    }

    // ============================================================
    // 灌权限码 + 角色绑定
    // ============================================================

    /// <summary>
    /// 插入 Code 不存在的新码(已存在的不动,不覆盖 Name/Category 的人工调整)。
    /// 不做老码清理(本项目是初次引入,不存在历史遗留码)。
    /// </summary>
    private static async Task SeedPermissionsAsync(ISqlSugarClient db)
    {
        var existingCodes = (await db.Queryable<Permissions>().ToListAsync())
            .Select(p => p.Code)
            .ToHashSet();

        var toInsert = _permissionSeeds.Where(p => !existingCodes.Contains(p.Code)).ToList();
        if (toInsert.Count == 0)
        {
            Console.WriteLine($"  - {_permissionSeeds.Length} 个分接开关厂家权限码均已存在,跳过");
            return;
        }

        await db.Insertable(toInsert).ExecuteCommandAsync();
        Console.WriteLine($"  ✓ 灌入 {toInsert.Count} 个权限码:{string.Join(", ", toInsert.Select(p => p.Code))}");
    }

    /// <summary>
    /// 增量补绑:逐个 code 检查 RolePermissions 里有没有,没有才插。
    /// 特意不照抄 DbMigrator 里"该角色已有权限关联 → 整体跳过"的守卫 —— 在已上线的库上
    /// 那意味着新权限码永远绑不上去。
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
                Console.WriteLine($"  - {normalizedName}:分接开关厂家权限已齐全,无需补绑");
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
        await ReportTableStateAsync(db, TapChangerManufacturersTable, TapChangerManufacturersIndex);
        await ReportTableStateAsync(db, TapChangerModelsTable, TapChangerModelsIndex);

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

    /// <summary>单表的状态报告:存在性 + 行数 + 索引存在性。</summary>
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
        }
    }

    /// <summary>日志里隐藏密码。</summary>
    private static string MaskConnectionString(string connStr)
    {
        return Regex.Replace(
            connStr, @"(password|pwd)\s*=\s*[^;]*", "$1=***",
            RegexOptions.IgnoreCase);
    }
}
