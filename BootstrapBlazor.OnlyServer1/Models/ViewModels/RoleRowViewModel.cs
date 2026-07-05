using BootstrapBlazor.OnlyServer1.Models;
using ModelRoles = BootstrapBlazor.OnlyServer1.Models.Roles;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels
{
    /// <summary>
    /// 角色列表行视图:在 ModelRoles 基础上附加用户数和权限数(供 TableColumn @bind-Field 用)。
    /// 名称空间别名 —— 避免与同名 Razor 页面类冲突。
    /// </summary>
    public class RoleRowViewModel
    {
        public BootstrapBlazor.OnlyServer1.Models.Roles Role { get; set; } = new();
        public int UserCount { get; set; }
        public int PermissionCount { get; set; }
    }
}
