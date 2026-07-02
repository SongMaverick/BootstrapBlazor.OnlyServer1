using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// ClaimsPrincipal 工厂:在 Identity 标准 Claim 之外追加:
    ///   - 角色 claim(ClaimTypes.Role)—— 由基类 UserClaimsPrincipalFactory&lt;TUser, TRole&gt; 自动写入
    ///   - DisplayName / GID(业务字段)
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
            if (!string.IsNullOrEmpty(user.GID))
                identity.AddClaim(new Claim(ClaimsPrincipalExtensions.GIDClaim, user.GID));

            // 权限码(从 UserRoles × RolePermissions × Permissions 联合查)
            // 失败时静默忽略(用户没任何权限码即可,不会阻塞登录)
            try
            {
                var codes = await _permissionsService.GetCodesByUserIdAsync(user.Id);
                foreach (var code in codes.Distinct())
                {
                    identity.AddClaim(new Claim(ClaimsPrincipalExtensions.PermissionClaim, code));
                }
            }
            catch
            {
                // 启动早期或 DB 异常时,登录依然要成功
            }

            return identity;
        }
    }
}