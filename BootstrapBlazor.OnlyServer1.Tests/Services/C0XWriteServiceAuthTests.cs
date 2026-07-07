using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services.DrawingNumbers;
using BootstrapBlazor.OnlyServer1.Services.DrawingTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// C0XWriteService.AddDrawingNumbersAsync / UpdateCX0DrawingNumberByIdAsync
/// 的纵深防御测试(2026-07-07 Review #H1)。
/// 图号加/改:Admin/Operator。
/// </summary>
public class C0XWriteServiceAuthTests
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

    private static C0XWriteService CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        // _db=null!,_registry=null!,_allocator=null! — 校验后才会撞到 null
        return new C0XWriteService(
            null!, null!, null!, null!, accessor,
            NullLogger<C0XWriteService>.Instance);
    }

    // ============================================================
    // AddDrawingNumbersAsync
    // 失败时返回的 DrawingNumberInsertResult 里的 Failures[].Reason 是 "无权操作"
    // ============================================================

    [Fact]
    public async Task AddDrawingNumbersAsync_Anonymous_AllFailures()
    {
        var service = CreateService(callerUserId: 0);

        var result = await service.AddDrawingNumbersAsync(1, new[] { "X", "Y" });

        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, f => Assert.Equal("无权操作", f.Reason));
        Assert.Empty(result.Success);
    }

    [Fact]
    public async Task AddDrawingNumbersAsync_Viewer_AllFailures()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Viewer" });

        var result = await service.AddDrawingNumbersAsync(1, new[] { "X" });

        Assert.Single(result.Failures);
        Assert.Equal("无权操作", result.Failures[0].Reason);
    }

    [Fact]
    public async Task AddDrawingNumbersAsync_Admin_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 授权通过后会落到 _db=null! → NPE
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await service.AddDrawingNumbersAsync(1, new[] { "X" }));
    }

    [Fact]
    public async Task AddDrawingNumbersAsync_Operator_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await service.AddDrawingNumbersAsync(1, new[] { "X" }));
    }

    // ============================================================
    // UpdateCX0DrawingNumberByIdAsync — 失败时返回 0
    // ============================================================

    [Fact]
    public async Task UpdateCX0DrawingNumberByIdAsync_Anonymous_ReturnsZero()
    {
        var service = CreateService(callerUserId: 0);

        var count = await service.UpdateCX0DrawingNumberByIdAsync(
            new C01 { Id = 1, DrawName = "X" }, "C01");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateCX0DrawingNumberByIdAsync_Viewer_ReturnsZero()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Viewer" });

        var count = await service.UpdateCX0DrawingNumberByIdAsync(
            new C01 { Id = 1, DrawName = "X" }, "C01");

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateCX0DrawingNumberByIdAsync_Operator_PassesAuth_ThenFailsOnRegistry()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        // 权限通过后,_registry=null → handler.Resolve 抛 NPE 直接冒上来
        // (此方法外面没有 try-catch 包裹)
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await service.UpdateCX0DrawingNumberByIdAsync(
                new C01 { Id = 1, DrawName = "X" }, "C01"));
    }
}
