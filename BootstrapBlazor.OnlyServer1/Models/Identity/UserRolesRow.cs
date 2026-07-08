using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Identity;

/// <summary>
/// UserRoles 表的轻量映射实体(Identity 标准 UserRole 关联表)。
///
/// 3 处使用方:
///   - Services/PermissionsService:多角色权限码查询的 INNER JOIN
///   - Services/Identity/SqlSugarUserStore:UserManager.AddToRoleAsync 等
///   - BootstrapBlazor.DbMigrator:Code First 建表 + 灌种子管理员角色
///
/// 之前的版本在三处各声明一遍(命名还差一个 's'):
///   - PermissionsService    → public UserRolesRow
///   - SqlSugarUserStore     → private UserRoleRow  (改名:无 's')
///   - DbMigrator/Program.cs → private UserRolesRow
/// 2026-07-08 命名审查 L-2 合并到本文件,避免后续 SqlSugar 多态写入时被两套类误导。
/// 复合主键 (UserId, RoleId),由 Identity 框架按约定读写。
/// </summary>
[SugarTable("UserRoles")]
public class UserRolesRow
{
    [SugarColumn(IsPrimaryKey = true)]
    public int UserId { get; set; }

    [SugarColumn(IsPrimaryKey = true)]
    public int RoleId { get; set; }
}
