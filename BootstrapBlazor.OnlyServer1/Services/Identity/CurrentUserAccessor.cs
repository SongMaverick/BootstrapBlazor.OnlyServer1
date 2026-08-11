using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// 从当前 HttpContext 的 ClaimsPrincipal 提取当前用户信息。
    /// 用于业务 Service 中拿到"当前登录用户"的姓名 / Id / Gid 等字段。
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

        /// <summary>业务字段 Gid,通过 UserManager 创建 SignIn 时注入。
        /// (2026-07-08 Review #L-8) 重命名 GetGID→GetGid,改用 GidClaim 常量(原用字面量 "GID")。</summary>
        public string? GetGid()
            => Principal?.FindFirstValue(ClaimsPrincipalExtensions.GidClaim);

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

        // ====================================================================
        // 纵深防御:Service mutation 方法的权限校验入口
        // (2026-07-07 Review #H1 增补)
        //
        // 为什么需要(背景):
        // Razor 层 [Authorize] 只挡"用户访问页面"这一层。Service 是真正
        // 改数据的地方。一旦出现以下场景,Razor 层即被绕过,Service 直接
        // 被穿:
        //   - 新增 Controller / Web API 时漏 [Authorize]
        //   - 增加健康检查 / 定时任务 / 单元测试代码直接调 Service
        //   - 任何未来绕过 UI 的调用路径
        //
        // 用法(每个 Service mutation 方法入口):
        //     var (ok, err) = _currentUser.RequireRole(RoleNames.Admin);
        //     if (!ok) {
        //         _logger.LogWarning(...);
        //         return (false, err);
        //     }
        //
        // **不要删这段注释,不要删这个方法,不要在 Service mutation 里
        //   旁路调用而不调本方法。** 漏调的 Service 方法视为"已知未审",
        //   上线前需对照 2026-07-07 代码审查报告 "H1" 段
        //   段对照检查。
        // ====================================================================
        public (bool ok, string? error) RequireRole(params string[] roles)
        {
            if (roles == null || roles.Length == 0)
                return (false, "权限配置错误:未指定允许的角色");

            if (!IsAuthenticated)
                return (false, "未登录或会话已失效,请重新登录");

            if (IsInAnyRole(roles))
                return (true, null);

            return (false, $"无权限(需要角色: {string.Join("/", roles)})");
        }
    }
}