using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models;

/// <summary>
/// 用户实体,继承 ASP.NET Core IdentityUser&lt;int&gt;。
/// UserName 字段由 UsersService 在创建/修改时同步为 WorkerNumber.ToString(),
/// 保证 Identity 的 FindByNameAsync 能查到用户。
///
/// 邮箱 / 手机号 / 双因素认证等功能不用(内部系统),但 IdentityUser 的字段仍然存在
/// (Code First 会建列),统一标注 IsNullable = true 允许 NULL,避免插入时报错。
/// </summary>
public class Users : IdentityUser<int>
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public override int Id { get; set; }

    // ===== 业务字段 =====
    public string Name { get; set; } = "";
    public int WorkerNumber { get; set; }
    /// <summary>员工在公司 OA 系统中的唯一标识(原 GID,2026-07-08 Review #L-8 重命名为 Gid)</summary>
    public string Gid { get; set; } = "";
    public DateTime CreatedTime { get; set; }
    public bool IsDeleted { get; set; }

    // ===== 部门(FK:Departments.Id,可空) =====
    [SugarColumn(IsNullable = true)]
    public int? DepartmentId { get; set; }

    // 不映射到 DB,仅用于 join 显示部门名
    [SugarColumn(IsIgnore = true)]
    public string? DepartmentName { get; set; }

    // 不映射到 DB,仅用于 join 显示角色名(逗号分隔)
    [SugarColumn(IsIgnore = true)]
    public string? RoleNames { get; set; }

    // ===== Identity 字段(全部显式标注可空性,绕过 SqlSugar 默认 NOT NULL 问题) =====

    // 必须非空:UserName 是 Identity 关键字段
    // (2026-08-06)基类 IdentityUser<int> 把 UserName 声明成 string?,
    // 我们坚持非空(DB 列 IsNullable=false 也保证非空),用 [property: AllowNull]
    // 告诉编译器"基类签名是 string?,但我从不让其为 null,所以类型保留非空即可"。
    [SugarColumn(IsNullable = false, Length = 256)]
    [property: AllowNull]
    public override string UserName { get; set; } = "";

    [SugarColumn(IsNullable = false, Length = 256)]
    [property: AllowNull]
    public override string NormalizedUserName { get; set; } = "";

    // 邮箱:不用,允许 NULL
    [SugarColumn(IsNullable = true, Length = 256)]
    public override string? Email { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public override string? NormalizedEmail { get; set; }

    // 手机号:不用,允许 NULL
    [SugarColumn(IsNullable = true, Length = 50)]
    public override string? PhoneNumber { get; set; }

    // 密码哈希 / 安全戳 / 并发戳:Identity 内部使用,允许 NULL
    [SugarColumn(IsNullable = true, Length = 500)]
    public override string? PasswordHash { get; set; }

    [SugarColumn(IsNullable = true, Length = 50)]
    public override string? SecurityStamp { get; set; }

    [SugarColumn(IsNullable = true, Length = 50)]
    public override string? ConcurrencyStamp { get; set; }

    // 锁定时间
    [SugarColumn(IsNullable = true)]
    public override DateTimeOffset? LockoutEnd { get; set; }

    // 其余 bool 字段(默认值即可)
    public override bool EmailConfirmed { get; set; }
    public override bool PhoneNumberConfirmed { get; set; }
    public override bool TwoFactorEnabled { get; set; }
    public override bool LockoutEnabled { get; set; }
    public override int AccessFailedCount { get; set; }
}