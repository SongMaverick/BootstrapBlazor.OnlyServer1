using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Identity;

/// <summary>
/// 权限码主表。每个权限码是一个"操作权限点",由代码静态定义并通过 DbMigrator 灌入。
/// 角色通过 RolePermissions 与之关联,用户的最终权限集 = 用户所属角色权限的并集。
///
/// 典型 Code 命名:
///   - User.Create / User.Edit / User.Delete
///   - Order.Approve / Order.Cancel
///   - Department.Read / Department.Edit
///   - Role.AssignUser
/// </summary>
[SugarTable("Permissions")]
public class Permissions
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>权限码(如 "User.Create"),应用层常量</summary>
    [SugarColumn(IsNullable = false, Length = 100)]
    public string Code { get; set; } = "";

    /// <summary>显示名(如 "新增用户")</summary>
    [SugarColumn(IsNullable = false, Length = 100)]
    public string Name { get; set; } = "";

    /// <summary>所属模块(如 "人员管理"、"部门管理")</summary>
    [SugarColumn(IsNullable = false, Length = 50)]
    public string Category { get; set; } = "";

    /// <summary>分类内排序,小者靠前</summary>
    public int SortOrder { get; set; }

    [SugarColumn(IsNullable = true, Length = 500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}