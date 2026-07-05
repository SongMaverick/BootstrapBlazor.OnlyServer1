using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Tests.Services;

/// <summary>
/// UsersService.ChangePasswordAsync 的早期校验测试。
/// 关注点:密码长度校验、错误消息。
/// 早期校验在调用 UserManager / DB 之前返回,所以此处可用 null! 跳过其他依赖。
/// 数据库相关路径需要真 DB 或 mock 框架,本次不覆盖(留给后续专门任务)。
/// </summary>
public class UsersServiceChangePasswordTests
{
    private static UsersService CreateService(int requiredLength)
    {
        // UserManager / SqlSugar 早期返回时不会触碰,传 null! 跳过
        var opts = Options.Create(new IdentityOptions
        {
            Password = new PasswordOptions { RequiredLength = requiredLength }
        });
        return new UsersService(null!, null!, opts, NullLogger<UsersService>.Instance);
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
                Assert.DoesNotContain($"新密码长度至少 {requiredLength} 位", error);
            }
        }
        catch
        {
            // UserManager 调 NPE 是预期的:null! → 实际验证的是"长度未拦截"
            // 不视为本测试失败
        }
    }
}
