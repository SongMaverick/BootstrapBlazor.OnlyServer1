using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;

namespace BootstrapBlazor.OnlyServer1.Models.ViewModels
{
    /// <summary>
    /// 用户-角色管理页的列表行视图:在 Users 基础上附加 RoleNames(逗号分隔)和 PrimaryRoleId(主角色 Id,供下拉回显)。
    /// 避免 TableColumn Template 没有 @bind-Field 时 Razor 类型推断失败的问题。
    /// </summary>
    public class UserWithRolesViewModel
    {
        public Users User { get; set; } = new();
        public string RoleNames { get; set; } = "";
        public int? PrimaryRoleId { get; set; }
    }
}
