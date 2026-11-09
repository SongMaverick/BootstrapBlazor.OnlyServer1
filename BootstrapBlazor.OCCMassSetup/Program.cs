using BootstrapBlazor.OnlyServer1.Models.Identity;
using Microsoft.Extensions.Configuration;
using SqlSugar;

const string permissionCode = PermissionCodes.OCCMassUse;
const string permissionName = "使用 OCC 重量复制";
const string permissionCategory = "OCC 重量复制";
const string roleName = RoleNames.Accounting;

var doCheck = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
var doInstall = args.Contains("--install", StringComparer.OrdinalIgnoreCase);
var doProduction = args.Contains("--production", StringComparer.OrdinalIgnoreCase);

Console.WriteLine("============================================================");
Console.WriteLine(" OCC 重量复制角色安装程序");
Console.WriteLine("============================================================");

if (doCheck == doInstall)
{
    Console.Error.WriteLine("错误:必须指定 --check 或 --install，且不能同时指定。");
    Console.Error.WriteLine("用法:");
    Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.OCCMassSetup -- --check");
    Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.OCCMassSetup -- --install");
    Console.Error.WriteLine("  dotnet run --project BootstrapBlazor.OCCMassSetup -- --install --production");
    return 1;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();


// 生产环境
//var connStr = config.GetConnectionString("sqlServerConnection");
//生产测试环境
var connStr = config.GetConnectionString("sqlServerConnectionTest");
//测试环境
//var connStr = config.GetConnectionString("sqlServerConnectionStringOffice");

if(string.IsNullOrWhiteSpace(connStr) ||
   connStr.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("错误:请先在 appsettings.json 配置有效的 ConnectionStrings:sqlServerConnection。");
    return 1;
}

Console.WriteLine($"连接字符串: {MaskConnectionString(connStr)}");
Console.WriteLine($"模式: check={doCheck}, install={doInstall}, production={doProduction}");

var db = new SqlSugarScope(new ConnectionConfig
{
    ConnectionString = connStr,
    DbType = DbType.SqlServer,
    IsAutoCloseConnection = true,
    InitKeyType = InitKeyType.Attribute
});

try
{
    Console.Write("测试数据库连接... ");
    await db.Ado.GetIntAsync("SELECT 1");
    Console.WriteLine("成功");

    await ReportStateAsync(db);

    if (doCheck)
    {
        Console.WriteLine("只读检查完成，未做任何修改。");
        return 0;
    }

    if (doProduction)
    {
        Console.WriteLine();
        Console.WriteLine("生产模式只执行新增操作，不删除已有角色或权限。");
        Console.Write("确认? 输入 YES 继续: ");
        if (!string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal))
        {
            Console.WriteLine("已取消。");
            return 0;
        }
    }

    var role = await GetOrCreateRoleAsync(db);
    var permission = await GetOrCreatePermissionAsync(db);
    await BindPermissionAsync(db, role.Id, permission.Id);

    Console.WriteLine();
    Console.WriteLine($"安装完成:角色={role.Name}(Id={role.Id})，权限={permission.Code}(Id={permission.Id})");
    Console.WriteLine("请在系统的“用户角色”页面将 accounting 分配给需要使用该页面的用户。");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"执行失败: {ex.Message}");
    return 2;
}
finally
{
    db.Dispose();
}

static async Task<Roles> GetOrCreateRoleAsync(ISqlSugarClient db)
{
    var normalized = roleName.ToUpperInvariant();
    var role = await db.Queryable<Roles>()
        .Where(item => item.NormalizedName == normalized)
        .FirstAsync();
    if (role is not null)
    {
        Console.WriteLine($"角色已存在: {role.Name}(Id={role.Id})");
        return role;
    }

    role = new Roles
    {
        Name = roleName,
        NormalizedName = normalized,
        ConcurrencyStamp = Guid.NewGuid().ToString()
    };
    role.Id = await db.Insertable(role).ExecuteReturnIdentityAsync();
    Console.WriteLine($"已创建角色: {roleName}(Id={role.Id})");
    return role;
}

static async Task<Permissions> GetOrCreatePermissionAsync(ISqlSugarClient db)
{
    var permission = await db.Queryable<Permissions>()
        .Where(item => item.Code == permissionCode)
        .FirstAsync();
    if (permission is not null)
    {
        Console.WriteLine($"权限已存在: {permission.Code}(Id={permission.Id})");
        return permission;
    }

    permission = new Permissions
    {
        Code = permissionCode,
        Name = permissionName,
        Category = permissionCategory,
        SortOrder = 1,
        IsActive = true
    };
    permission.Id = await db.Insertable(permission).ExecuteReturnIdentityAsync();
    Console.WriteLine($"已创建权限: {permissionCode}(Id={permission.Id})");
    return permission;
}

static async Task BindPermissionAsync(ISqlSugarClient db, int roleId, int permissionId)
{
    var exists = await db.Queryable<RolePermissions>()
        .Where(item => item.RoleId == roleId && item.PermissionId == permissionId)
        .AnyAsync();
    if (exists)
    {
        Console.WriteLine("角色权限关联已存在，跳过。");
        return;
    }

    await db.Insertable(new RolePermissions
    {
        RoleId = roleId,
        PermissionId = permissionId
    }).ExecuteCommandAsync();
    Console.WriteLine("已将 OCCMass.Use 绑定到 accounting。");
}

static async Task ReportStateAsync(ISqlSugarClient db)
{
    var role = await db.Queryable<Roles>()
        .Where(item => item.NormalizedName == roleName.ToUpperInvariant())
        .FirstAsync();
    var permission = await db.Queryable<Permissions>()
        .Where(item => item.Code == permissionCode)
        .FirstAsync();

    Console.WriteLine();
    Console.WriteLine("当前状态:");
    Console.WriteLine($"  角色: {(role is null ? "不存在" : $"已存在(Id={role.Id})")}");
    Console.WriteLine($"  权限: {(permission is null ? "不存在" : $"已存在(Id={permission.Id})")}");
}

static string MaskConnectionString(string value)
{
    var parts = value.Split(';');
    for (var index = 0; index < parts.Length; index++)
    {
        if (parts[index].StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
            parts[index].StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
        {
            var separator = parts[index].IndexOf('=');
            parts[index] = separator >= 0 ? parts[index][..(separator + 1)] + "******" : "******";
        }
    }

    return string.Join(';', parts);
}
