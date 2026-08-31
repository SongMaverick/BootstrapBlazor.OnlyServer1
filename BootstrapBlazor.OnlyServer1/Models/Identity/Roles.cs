using Microsoft.AspNetCore.Identity;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models.Identity;

/// <summary>
/// 角色实体,继承 ASP.NET Core IdentityRole&lt;int&gt;。
/// 为未来 RBAC 权限系统使用,UI 暂不实现。
/// </summary>
public class Roles : IdentityRole<int>
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public override int Id { get; set; }
}