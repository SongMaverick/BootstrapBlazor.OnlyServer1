using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// 从当前 HttpContext 的 ClaimsPrincipal 提取当前用户信息。
    /// 用于业务 Service 中拿到"当前登录用户"的姓名 / Id / GID 等字段。
    ///
    /// 依赖注入:IHttpContextAccessor。
    /// 用法:
    ///   var currentUserName = accessor.GetUserName() ?? "System";
    ///   var currentUserId   = accessor.GetUserId();   // 0 表示未登录
    /// </summary>
    public class CurrentUserAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

        public int GetUserId()
        {
            var id = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var v) ? v : 0;
        }

        public string? GetUserName()
            => Principal?.Identity?.Name;

        /// <summary>业务字段 Name(姓名),通过 UserManager 创建 SignIn 时注入。</summary>
        public string? GetDisplayName()
            => Principal?.FindFirstValue("DisplayName");

        /// <summary>业务字段 GID,通过 UserManager 创建 SignIn 时注入。</summary>
        public string? GetGID()
            => Principal?.FindFirstValue("GID");

        /// <summary>当前用户拥有的所有权限码(从 Claims 读取,由 DisplayNameClaimsFactory 写入)。</summary>
        public IEnumerable<string> GetPermissions()
            => Principal?.FindAll(ClaimsPrincipalExtensions.PermissionClaim).Select(c => c.Value)
               ?? Enumerable.Empty<string>();

        /// <summary>判断当前用户是否拥有指定权限码。</summary>
        public bool HasPermission(string code)
            => Principal?.HasClaim(ClaimsPrincipalExtensions.PermissionClaim, code) == true;

        /// <summary>判断当前用户是否拥有任意一个权限码。</summary>
        public bool HasAnyPermission(params string[] codes)
            => codes.Any(HasPermission);

        /// <summary>判断当前用户是否在任一指定角色中。空数组视为 false(必须显式指定)。</summary>
        public bool IsInAnyRole(params string[] roles)
        {
            if (roles == null || roles.Length == 0) return false;
            return roles.Any(r => Principal?.IsInRole(r) == true);
        }
    }
}