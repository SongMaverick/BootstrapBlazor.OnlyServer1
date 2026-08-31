using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using ModelRoles = BootstrapBlazor.OnlyServer1.Models.Identity.Roles;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels
{
    /// <summary>
    /// 角色列表行视图:在 ModelRoles 基础上附加用户数和权限数(供 TableColumn @bind-Field 用)。
    /// 名称空间别名 —— 避免与同名 Razor 页面类冲突。
    /// </summary>
    public class RoleRowViewModel
    {
        public BootstrapBlazor.OnlyServer1.Models.Identity.Roles Role { get; set; } = new();
        public int UserCount { get; set; }
        public int PermissionCount { get; set; }
    }
}
