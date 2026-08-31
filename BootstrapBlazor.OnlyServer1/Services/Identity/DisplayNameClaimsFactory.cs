using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.Admin;
using BootstrapBlazor.OnlyServer1.Services.Common;
using BootstrapBlazor.OnlyServer1.Services.Drawing;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using BootstrapBlazor.OnlyServer1.Services.OrderManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// ClaimsPrincipal 工厂:在 Identity 标准 Claim 之外追加:
    ///   - 角色 claim(ClaimTypes.Role)—— 由基类 UserClaimsPrincipalFactory&lt;TUser, TRole&gt; 自动写入
    ///   - DisplayName / Gid(业务字段)
    ///   - Permission(用户所有角色 × 权限码的并集,供 HasPermission 按钮级判断)
    ///
    /// 注意:基类必须用带角色类型参数的重载
    ///   UserClaimsPrincipalFactory&lt;Users&gt;            —— 不会写 ClaimTypes.Role
    ///   UserClaimsPrincipalFactory&lt;Users, Roles&gt;    —— 会自动写 ClaimTypes.Role
    /// 否则 IsInRole("Admin") / [Authorize(Roles = "Admin")] 全部失效。
    /// </summary>
    public class DisplayNameClaimsFactory : UserClaimsPrincipalFactory<Users, Roles>
    {
        private readonly PermissionsService _permissionsService;

        public DisplayNameClaimsFactory(
            UserManager<Users> userManager,
            RoleManager<Roles> roleManager,
            IOptions<IdentityOptions> options,
            PermissionsService permissionsService)
            : base(userManager, roleManager, options)
        {
            _permissionsService = permissionsService;
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Users user)
        {
            var identity = await base.GenerateClaimsAsync(user);

            if (!string.IsNullOrEmpty(user.Name))
                identity.AddClaim(new Claim(ClaimsPrincipalExtensions.DisplayNameClaim, user.Name));
            if (!string.IsNullOrEmpty(user.Gid))
                identity.AddClaim(new Claim(ClaimsPrincipalExtensions.GidClaim, user.Gid));

            // 权限码(从 UserRoles × RolePermissions × Permissions 联合查)。
            // 失败时直接抛回 SignInManager — 登录整体失败,避免"幽灵登录"产生
            // 「已认证但无任何 Permission claim」的状态(前端按钮全灰但仍
            // 能进业务页 + 跑非权限校验的 SQL)。
            var codes = await _permissionsService.GetCodesByUserIdAsync(user.Id);
            foreach (var code in codes.Distinct())
            {
                identity.AddClaim(new Claim(ClaimsPrincipalExtensions.PermissionClaim, code));
            }

            return identity;
        }
    }
}
