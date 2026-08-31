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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// DepartmentsService.SaveAsync / SoftDeleteAsync 的纵深防御测试(2026-07-07 Review #H1)。
/// 部门管理仅 Admin。早期校验在 _db 调用前返回。
/// </summary>
public class DepartmentsServiceAuthTests
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

    private static DepartmentsService CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        return new DepartmentsService(null!, accessor, NullLogger<DepartmentsService>.Instance);
    }

    // ===== SaveAsync =====

    [Fact]
    public async Task SaveAsync_Anonymous_ReturnsFalseWithNotLoggedIn()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.SaveAsync(new Departments { Name = "X" });

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task SaveAsync_Operator_ReturnsFalseWithNoPermission()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var (ok, error) = await service.SaveAsync(new Departments { Name = "X" });

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    [Fact]
    public async Task SaveAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 校验通过后会落到 _db=null! → 内部 catch 吞 → 返回 (false, "保存失败,请重试")
        // 关键:不是"无权"错误
        var (ok, error) = await service.SaveAsync(new Departments { Name = "X" });
        Assert.False(ok);
        Assert.DoesNotContain("未登录", error);
        Assert.DoesNotContain("无权限", error);
    }

    // ===== SoftDeleteAsync =====

    [Fact]
    public async Task SoftDeleteAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var ok = await service.SoftDeleteAsync(new[] { new Departments { Id = 1 } });

        Assert.False(ok);
    }

    [Fact]
    public async Task SoftDeleteAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var ok = await service.SoftDeleteAsync(new[] { new Departments { Id = 1 } });

        Assert.False(ok);
    }

    [Fact]
    public async Task SoftDeleteAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 校验通过后会落到 _db=null! → catch 吞 → return false
        var ok = await service.SoftDeleteAsync(new[] { new Departments { Id = 1 } });
        Assert.False(ok);
    }
}
