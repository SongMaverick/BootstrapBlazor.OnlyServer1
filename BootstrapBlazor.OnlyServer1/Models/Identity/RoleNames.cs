namespace BootstrapBlazor.OnlyServer1.Models.Identity;

/// <summary>
/// 系统内置角色名常量。
/// 改角色名只改这里一处 ——
/// Razor 的 [Authorize(Roles = ...)]、MainLayout 的菜单过滤、Services 的角色检查
/// 全部从此类读取。
/// 数据库 Roles 表 Name 列由 DbMigrator 灌种时也用此值(保持一致)。
/// </summary>
public static class RoleNames
{
    public const string Admin    = "Admin";
    public const string Operator = "Operator";
    public const string Viewer   = "Viewer";

    public static readonly string[] All = { Admin, Operator, Viewer };
}
