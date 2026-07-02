using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// 业务字段(姓名/GID)的 Claim 类型常量。
    /// 由 <see cref="DisplayNameClaimsFactory"/> 在签发 Cookie 时写入。
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        public const string DisplayNameClaim = "DisplayName";
        public const string GIDClaim = "GID";
        /// <summary>细粒度操作权限码的 Claim 类型(每条权限码一个 Claim)。</summary>
        public const string PermissionClaim = "Permission";
    }
}
