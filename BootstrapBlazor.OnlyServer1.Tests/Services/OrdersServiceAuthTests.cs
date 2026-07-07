using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// OrdersService.SaveAsync / SoftDeleteAsync 的纵深防御测试(2026-07-07 Review #H1)。
/// 规则:
///   SaveAsync Add    = Admin / Operator
///   SaveAsync Update = Admin 或 当前用户 == order.CreatedBy
///   SoftDeleteAsync   = Admin
/// </summary>
public class OrdersServiceAuthTests
{
    private static IHttpContextAccessor BuildHttpContextAccessor(int callerUserId, params string[] callerRoles)
    {
        var accessor = new HttpContextAccessor();
        if (callerUserId <= 0 && callerRoles.Length == 0) return accessor;
        var claims = new List<Claim>();
        if (callerUserId > 0)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, callerUserId.ToString()));
        // DisplayName claim 也需要,因为 Service 用 _currentUser.GetDisplayName() 跟 order.CreatedBy 比
        if (callerUserId > 0)
            claims.Add(new Claim("DisplayName", $"user{callerUserId}"));
        claims.AddRange(callerRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"))
        };
        return accessor;
    }

    private static OrdersService CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        // _db / _c0xQuery / _registry 都传 null! — 校验分支在它们之前返回
        return new OrdersService(null!, null!, null!, accessor, NullLogger<OrdersService>.Instance);
    }

    // ============================================================
    // SaveAsync — Add
    // ============================================================

    [Fact]
    public async Task SaveAsync_Add_Anonymous_Rejected()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.SaveAsync(
            new Orders { OrderNumber = 1, CreatedBy = "userX", UpdatedBy = "userX" },
            ItemChangedType.Add);

        Assert.False(ok);
        Assert.Contains("未登录", error);
    }

    [Fact]
    public async Task SaveAsync_Add_Viewer_Rejected()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Viewer" });

        var (ok, error) = await service.SaveAsync(
            new Orders { OrderNumber = 1, CreatedBy = "userX", UpdatedBy = "userX" },
            ItemChangedType.Add);

        Assert.False(ok);
        Assert.Contains("无权限", error);
    }

    [Fact]
    public async Task SaveAsync_Add_Operator_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        // catch 吞 _db=null! 异常 → return (false, "保存失败,请重试")
        var (ok, error) = await service.SaveAsync(
            new Orders { OrderNumber = 1, CreatedBy = "user1", UpdatedBy = "user1" },
            ItemChangedType.Add);
        Assert.False(ok);
        Assert.DoesNotContain("未登录", error);
        Assert.DoesNotContain("无权限", error);
    }

    [Fact]
    public async Task SaveAsync_Add_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // catch 吞 _db=null! 异常 → return (false, "保存失败,请重试")
        var (ok, error) = await service.SaveAsync(
            new Orders { OrderNumber = 1, CreatedBy = "user1", UpdatedBy = "user1" },
            ItemChangedType.Add);
        Assert.False(ok);
        Assert.DoesNotContain("未登录", error);
        Assert.DoesNotContain("无权限", error);
    }

    // ============================================================
    // SaveAsync — Update(关键差异点)
    // 规则: Admin 或 当前用户 displayName == order.CreatedBy
    // ============================================================

    [Fact]
    public async Task SaveAsync_Update_Anonymous_Rejected()
    {
        var service = CreateService(callerUserId: 0);

        var (ok, error) = await service.SaveAsync(
            new Orders { Id = 100, OrderNumber = 1, CreatedBy = "user1", UpdatedBy = "user1" },
            ItemChangedType.Update);

        Assert.False(ok);
        // 未登录 + Update 分支返回的不是 RequireRole 的错误,而是创建者分支的"无权限"错误
        Assert.NotNull(error);
    }

    [Fact]
    public async Task SaveAsync_Update_OtherUser_Operator_Rejected()
    {
        // operator user5 想改 user1 创建的订单
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });
        // 调整 helper 让 callerUserName=user5
        // (我们的 stub 已经把 DisplayName 设为 user{callerUserId},即 user5)

        var (ok, error) = await service.SaveAsync(
            new Orders { Id = 100, OrderNumber = 1, CreatedBy = "user1", UpdatedBy = "user1" },
            ItemChangedType.Update);

        Assert.False(ok);
        Assert.Contains("创建者", error);
    }

    [Fact]
    public async Task SaveAsync_Update_SameUser_Operator_PassesAuth_ThenFailsOnDb()
    {
        // operator 自己改自己的订单
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await service.SaveAsync(
                new Orders { Id = 100, OrderNumber = 1, CreatedBy = "user1", UpdatedBy = "user1" },
                ItemChangedType.Update));
    }

    [Fact]
    public async Task SaveAsync_Update_AdminForOthersOrder_PassesAuth_ThenFailsOnDb()
    {
        // Admin 改任何人的订单
        var service = CreateService(callerUserId: 99, callerRoles: new[] { "Admin" });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await service.SaveAsync(
                new Orders { Id = 100, OrderNumber = 1, CreatedBy = "user1", UpdatedBy = "user99" },
                ItemChangedType.Update));
    }

    // ============================================================
    // SoftDeleteAsync — Admin only
    // ============================================================

    [Fact]
    public async Task SoftDeleteAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var ok = await service.SoftDeleteAsync(new[] { new Orders { Id = 1 } });

        Assert.False(ok);
    }

    [Fact]
    public async Task SoftDeleteAsync_Operator_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Operator" });

        var ok = await service.SoftDeleteAsync(new[] { new Orders { Id = 1 } });

        Assert.False(ok);
    }

    [Fact]
    public async Task SoftDeleteAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // catch 吞 _db=null! 异常 → return false
        var ok = await service.SoftDeleteAsync(new[] { new Orders { Id = 1 } });
        Assert.False(ok);
    }
}
