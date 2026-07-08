using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// RolesService 的纵深防御测试(2026-07-07 Review #H1)。
/// 5 个 mutation 方法都限 Admin:
///   SaveAsync / DeleteAsync /
///   AddUserToRoleAsync / RemoveUserFromRoleAsync / SetUserRolesAsync
/// </summary>
public class RolesServiceAuthTests
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

    private static RolesService CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        // _db=null!,_userManager=null! — 校验分支早返回,不会触碰依赖
        return new RolesService(null!, null!, accessor, NullLogger<RolesService>.Instance);
    }

    // ============================================================
    // SaveAsync
    // ============================================================

    [Fact]
    public async Task SaveAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.SaveAsync(
            new Roles { Name = "X" }, addPermissionIds: null, removePermissionIds: null, ItemChangedType.Add);

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task SaveAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var (ok, error) = await service.SaveAsync(
            new Roles { Name = "X" }, addPermissionIds: null, removePermissionIds: null, ItemChangedType.Add);

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    [Fact]
    public async Task SaveAsync_Admin_PassesAuth()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 校验通过后会落到 _db=null! → catch 吞 → 返回 (false, "保存失败,请重试")
        // 关键:不是"无权"路径
        var (ok, error) = await service.SaveAsync(
            new Roles { Name = "X" }, addPermissionIds: null, removePermissionIds: null, ItemChangedType.Add);
        Assert.False(ok);
        Assert.DoesNotContain("未登录", error);
        Assert.DoesNotContain("无权限", error);
    }

    // ============================================================
    // DeleteAsync
    // ============================================================

    [Fact]
    public async Task DeleteAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.DeleteAsync(new[] { new Roles { Id = 1 } });

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task DeleteAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var (ok, error) = await service.DeleteAsync(new[] { new Roles { Id = 1 } });

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    // ============================================================
    // AddUserToRoleAsync
    // ============================================================

    [Fact]
    public async Task AddUserToRoleAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.AddUserToRoleAsync(1, 1);

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task AddUserToRoleAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var (ok, error) = await service.AddUserToRoleAsync(1, 1);

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    // ============================================================
    // RemoveUserFromRoleAsync
    // ============================================================

    [Fact]
    public async Task RemoveUserFromRoleAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.RemoveUserFromRoleAsync(1, 1);

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task RemoveUserFromRoleAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var (ok, error) = await service.RemoveUserFromRoleAsync(1, 1);

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    // ============================================================
    // SetUserRolesAsync
    // ============================================================

    [Fact]
    public async Task SetUserRolesAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.SetUserRolesAsync(1, new[] { 1 });

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task SetUserRolesAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var (ok, error) = await service.SetUserRolesAsync(1, new[] { 1 });

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    [Fact]
    public async Task SetUserRolesAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // catch 吞 _db=null! 异常 → return (false, ex.Message)
        var (ok, error) = await service.SetUserRolesAsync(1, new[] { 1 });
        Assert.False(ok);
        Assert.DoesNotContain("未登录", error);
        Assert.DoesNotContain("无权限", error);
    }
}
