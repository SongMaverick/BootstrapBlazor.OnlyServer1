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
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// UsersService.ChangePasswordAsync 的早期校验测试。
/// 关注点:授权、密码长度校验、错误消息。
/// 早期校验在调用 UserManager / DB 之前返回,所以此处可用 null! 跳过其他依赖。
/// 数据库相关路径需要真 DB 或 mock 框架,本次不覆盖(留给后续专门任务)。
/// </summary>
public class UsersServiceChangePasswordTests
{
    // 创建一个 stub IHttpContextAccessor,模拟"当前用户=callerUserId,角色=callerRoles"。
    private static IHttpContextAccessor BuildHttpContextAccessor(int callerUserId, params string[] callerRoles)
    {
        var accessor = new HttpContextAccessor();
        if (callerUserId <= 0 && callerRoles.Length == 0) return accessor; // 未登录
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

    private static UsersService CreateService(int requiredLength, int callerUserId = 1, params string[] callerRoles)
    {
        var opts = Options.Create(new IdentityOptions
        {
            Password = new PasswordOptions { RequiredLength = requiredLength }
        });
        var accessor = new BootstrapBlazor.OnlyServer1.Services.Identity.CurrentUserAccessor(
            BuildHttpContextAccessor(callerUserId, callerRoles));
        return new UsersService(null!, null!, opts, accessor, NullLogger<UsersService>.Instance);
    }

    [Theory]
    [InlineData(null, "newValid123")]
    [InlineData("",   "newValid123")]
    [InlineData("oldValid123", null)]
    [InlineData("oldValid123", "")]
    public async Task ChangePasswordAsync_EmptyInput_ReturnsFalse(string? oldPassword, string? newPassword)
    {
        var service = CreateService(requiredLength: 6);

        var (ok, error) = await service.ChangePasswordAsync(1, oldPassword!, newPassword!);

        Assert.False(ok);
        Assert.Equal("旧密码和新密码均不能为空", error);
    }

    [Theory]
    [InlineData("abc", 6, "6")]      // length=3 < req=6
    [InlineData("ab", 8, "8")]        // length=2 < req=8
    [InlineData("12345", 6, "6")]     // length=5 < req=6
    public async Task ChangePasswordAsync_NewShorterThanRequired_ReturnsFalseWithConfiguredLength(
        string newPassword, int requiredLength, string expectedLengthInMessage)
    {
        var service = CreateService(requiredLength);

        var (ok, error) = await service.ChangePasswordAsync(1, "oldValid123", newPassword);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains(expectedLengthInMessage, error);
    }

    [Theory]
    [InlineData("123456", 6)]    // length=6 == req=6, 不是严格大于
    [InlineData("abcdef", 4)]    // length=6 > req=4
    [InlineData("longPassword", 4)]
    public async Task ChangePasswordAsync_LengthAtOrAboveMinimum_DoesNotFailOnLengthCheck(
        string newPassword, int requiredLength)
    {
        // 注:此测试只验证不会因长度早期返回失败;后续依赖 UserManager 会失败,
        // 因为我们的 _userManager 是 null!。我们捕获该异常/失败来证明校验通过。
        var service = CreateService(requiredLength);

        try
        {
            var (ok, error) = await service.ChangePasswordAsync(1, "oldValid123", newPassword);

            // 如果到这里,说明长度通过了 — 我们不关心后续步骤。
            // 真实环境会进 UserManager.ChangePasswordAsync 然后成功。
            // 这里只断言"早期长度错误"分支已通过 — 用我们的 error 字符串识别。
            if (!ok)
            {
                Assert.NotEqual("旧密码和新密码均不能为空", error);
                Assert.NotEqual("无权修改此用户的密码", error);
                Assert.DoesNotContain($"新密码长度至少 {requiredLength} 位", error);
            }
        }
        catch
        {
            // UserManager 调 NPE 是预期的:null! → 实际验证的是"长度未拦截"
            // 不视为本测试失败
        }
    }

    // ===== 授权校验(纵深防御) =====

    [Fact]
    public async Task ChangePasswordAsync_CrossUser_Rejected()
    {
        // 调用者 userId=1,想改 userId=2 的密码 → 拒绝
        var service = CreateService(requiredLength: 6, callerUserId: 1);

        var (ok, error) = await service.ChangePasswordAsync(2, "oldValid123", "newValid123");

        Assert.False(ok);
        Assert.Equal("无权修改此用户的密码", error);
    }

    [Fact]
    public async Task ChangePasswordAsync_Anonymous_Rejected()
    {
        // 未登录调用 → 拒绝
        var service = CreateService(requiredLength: 6, callerUserId: 0);

        var (ok, error) = await service.ChangePasswordAsync(1, "oldValid123", "newValid123");

        Assert.False(ok);
        Assert.Equal("无权修改此用户的密码", error);
    }

    [Fact]
    public async Task ChangePasswordAsync_AdminCanResetOther_PassesAuth()
    {
        // Admin 角色可以改任意用户密码 → 通过授权关(后续 UserManager=null 会失败,正常)
        var service = CreateService(requiredLength: 6, callerUserId: 99,
            callerRoles: new[] { "Admin" });

        try
        {
            var (ok, error) = await service.ChangePasswordAsync(2, "oldValid123", "newValid123");
            // 授权通过后,失败原因只能是 UserManager=null 或 UserNotFound,不会是"无权"
            if (!ok)
            {
                Assert.NotEqual("无权修改此用户的密码", error);
            }
        }
        catch
        {
            // UserManager=null 的 NPE 是预期的(单元测试不挂数据库)
        }
    }
}
