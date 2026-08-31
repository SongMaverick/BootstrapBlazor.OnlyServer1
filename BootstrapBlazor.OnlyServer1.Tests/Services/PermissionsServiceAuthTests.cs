using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.Admin;
using BootstrapBlazor.OnlyServer1.Services.Common;
using BootstrapBlazor.OnlyServer1.Services.Drawing;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using BootstrapBlazor.OnlyServer1.Services.OrderManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// PermissionsService.SetRolePermissionsAsync 的纵深防御测试(2026-07-07 Review #H1)。
/// 覆盖角色权限是 Admin-only 操作。
/// 注:匿名/越权时早 return,无 error message(签名是 void Task),
/// 只能断言 "DB 没被调到" — 这里只断无异常 + 不进入业务路径。
/// </summary>
public class PermissionsServiceAuthTests
{
    private static IHttpContextAccessor BuildHttpContextAccessor(int callerUserId, params string[] callerRoles)
    {
        var accessor = new HttpContextAccessor();
        if (callerUserId <= 0 && callerRoles.Length == 0) return accessor;
        var claims = new List<Claim>();
        if (callerUserId > 0)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, callerUserId.ToString()));
        claims.AddRange(callerRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"))
        };
        return accessor;
    }

    private static PermissionsService CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        return new PermissionsService(null!, accessor, NullLogger<PermissionsService>.Instance);
    }

    [Fact]
    public async Task SetRolePermissionsAsync_Anonymous_NoEffect()
    {
        var service = CreateService(callerUserId: 0);

        // 早返回:不抛异常,DB 没被调
        await service.SetRolePermissionsAsync(1, new[] { 1, 2 });
        // 通过:不抛 + 早 return 即视为权限被挡
    }

    [Fact]
    public async Task SetRolePermissionsAsync_Operator_NoEffect()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        await service.SetRolePermissionsAsync(1, new[] { 1, 2 });
    }

    [Fact]
    public async Task SetRolePermissionsAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 校验通过后会落到 _db=null! → NPE
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await service.SetRolePermissionsAsync(1, new[] { 1, 2 }));
    }
}
