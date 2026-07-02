using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Models
{
    /// <summary>
    /// 角色-权限 多对多关联。
    /// 复合主键 (RoleId, PermissionId)。
    /// CASCADE:删除角色 / 权限码时自动清理关联。
    /// </summary>
    [SugarTable("RolePermissions")]
    public class RolePermissions
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int RoleId { get; set; }

        [SugarColumn(IsPrimaryKey = true)]
        public int PermissionId { get; set; }
    }
}
