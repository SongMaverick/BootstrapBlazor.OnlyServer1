using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;

namespace BootstrapBlazor.OnlyServer1.Services.Identity
{
    /// <summary>
    /// 业务字段(姓名/Gid)的 Claim 类型常量。
    /// 由 <see cref="DisplayNameClaimsFactory"/> 在签发 Cookie 时写入。
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        public const string DisplayNameClaim = "DisplayName";
        /// <summary>员工 GID 的 Claim 类型(2026-07-08 Review #L-8 改名 GIDClaim→GidClaim,值 "GID"→"Gid")</summary>
        public const string GidClaim = "Gid";
        /// <summary>细粒度操作权限码的 Claim 类型(每条权限码一个 Claim)。</summary>
        public const string PermissionClaim = "Permission";
    }
}
