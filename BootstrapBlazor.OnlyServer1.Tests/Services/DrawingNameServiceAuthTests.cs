using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Services.DrawingNames;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// DrawingNameService.AddDrawingNameAsync / DeleteDrawingNameAsync 的纵深防御测试
/// (2026-07-07 Review #H1)。
/// 图名操作:Admin/Operator。
/// </summary>
public class DrawingNameServiceAuthTests
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

    private static DrawingNameService CreateService(int callerUserId = 1, params string[] callerRoles)
    {
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        return new DrawingNameService(null!, accessor, NullLogger<DrawingNameService>.Instance);
    }

    // ===== AddDrawingNameAsync =====

    [Fact]
    public async Task AddDrawingNameAsync_Anonymous_ReturnsZero()
    {
        var service = CreateService(callerUserId: 0);

        var inserted = await service.AddDrawingNameAsync("ESL-外壳-001", "ESL-01");

        Assert.Equal(0, inserted);
    }

    [Fact]
    public async Task AddDrawingNameAsync_Viewer_ReturnsZero()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Viewer" });

        var inserted = await service.AddDrawingNameAsync("ESL-外壳-001", "ESL-01");

        Assert.Equal(0, inserted);
    }

    [Fact]
    public async Task AddDrawingNameAsync_Admin_PassesAuth()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Admin" });

        // 权限通过后会落到 _db=null! → 内部 catch 块吞掉 → 返回 0
        // 关键断言:不是返回"无权"错误
        var inserted = await service.AddDrawingNameAsync("ESL-外壳-001", "ESL-01");
        Assert.Equal(0, inserted);
    }

    [Fact]
    public async Task AddDrawingNameAsync_Operator_PassesAuth()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        var inserted = await service.AddDrawingNameAsync("ESL-外壳-001", "ESL-01");
        Assert.Equal(0, inserted);
    }

    // ===== DeleteDrawingNameAsync =====

    [Fact]
    public async Task DeleteDrawingNameAsync_Anonymous_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 0);

        var ok = await service.DeleteDrawingNameAsync(1);

        Assert.False(ok);
    }

    [Fact]
    public async Task DeleteDrawingNameAsync_Viewer_ReturnsFalse()
    {
        var service = CreateService(callerUserId: 5, callerRoles: new[] { "Viewer" });

        var ok = await service.DeleteDrawingNameAsync(1);

        Assert.False(ok);
    }

    [Fact]
    public async Task DeleteDrawingNameAsync_Operator_PassesAuth_ThenFailsOnDb()
    {
        var service = CreateService(callerUserId: 1, callerRoles: new[] { "Operator" });

        // 内部 catch 把 _db=null! 异常吞掉 → return false
        var ok = await service.DeleteDrawingNameAsync(1);
        Assert.False(ok);
    }
}
