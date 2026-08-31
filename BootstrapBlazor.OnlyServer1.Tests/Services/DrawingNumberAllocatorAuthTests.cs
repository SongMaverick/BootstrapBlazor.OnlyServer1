using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.Admin;
using BootstrapBlazor.OnlyServer1.Services.Common;
using BootstrapBlazor.OnlyServer1.Services.Drawing;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using BootstrapBlazor.OnlyServer1.Services.OrderManagement;
using BootstrapBlazor.OnlyServer1.Services.Drawing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// DrawingNumberAllocator.AllocateAsync 的纵深防御测试(2026-07-07 Review #H1)。
///
/// AllocateAsync 是"修改关键资源"操作,仅 Admin/Operator。
/// 测试只覆盖权限校验早返回分支;后续 _db=null! 会抛 NPE,这里是预期。
/// </summary>
public class DrawingNumberAllocatorAuthTests
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

    private static DrawingNumberAllocator CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        // _db=null! 跳过 DB,_registry=null! 跳过注册表
        return new DrawingNumberAllocator(null!, null!, accessor, NullLogger<DrawingNumberAllocator>.Instance);
    }

    [Fact]
    public async Task AllocateAsync_Anonymous_ThrowsUnauthorized()
    {
        var service = CreateService(callerUserId: 0);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.AllocateAsync("C04"));
        Assert.Contains("未登录", ex.Message);
    }

    [Fact]
    public async Task AllocateAsync_Viewer_ThrowsUnauthorized()
    {
        // Viewer 不在 Admin/Operator 列表 → 拒绝
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Viewer" });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.AllocateAsync("C04"));
        Assert.Contains("无权限", ex.Message);
    }

    [Fact]
    public async Task AllocateAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 授权通过后会落到 _db=null! → NPE 是预期
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await service.AllocateAsync("C04"));
    }

    [Fact]
    public async Task AllocateAsync_Operator_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await service.AllocateAsync("C04"));
    }
}
