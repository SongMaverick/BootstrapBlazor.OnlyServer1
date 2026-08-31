using System.Security.Claims;
using BootstrapBlazor.OnlyServer1.Models;
using BootstrapBlazor.OnlyServer1.Models.Admin;
using BootstrapBlazor.OnlyServer1.Models.Common;
using BootstrapBlazor.OnlyServer1.Models.Drawing;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Models.OrderManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BootstrapBlazor.OnlyServer1.Controllers
{
    [Route("[controller]")]
    public class AccountController : Controller
    {
        private readonly SignInManager<Users> _signInManager;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            SignInManager<Users> signInManager,
            ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        [HttpPost("Login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            [FromForm] string WorkerNumber,
            [FromForm] string Password,
            [FromForm] bool RememberMe,
            [FromForm] string? ReturnUrl)
        {
            if (string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl))
                ReturnUrl = "~/";

            var ip = GetClientIp();

            if (string.IsNullOrWhiteSpace(WorkerNumber) || string.IsNullOrWhiteSpace(Password))
            {
                _logger.LogWarning("登录表单不完整: ip={Ip}", ip);
                return RedirectToLoginWithError("员工号和密码不能为空", ReturnUrl);
            }

            // 提前按 WorkerNumber 拉一次基础信息(姓名 + userId),后续所有分支都能拿到姓名
            // 用于审计关联。注意:错误 WorkerNumber 时返回 null,不影响密码校验失败原因。
            var (userId, userName) = await GetUserInfoByWorkerNumberAsync(WorkerNumber);

            // (2026-08-14) RememberMe 控制 ASP.NET Identity cookie 的生命周期:
            //   true  → auth cookie 持久化(浏览器关闭也不丢)
            //   false → session cookie(关闭浏览器即失效)
            // 客户端「员工号+密码」cookie 由 wwwroot/js/rememberMe.js 接管,与本参数独立。
            var result = await _signInManager.PasswordSignInAsync(
                WorkerNumber, Password, isPersistent: RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation(
                    "登录成功: userId={UserId} workerNumber={WorkerNumber} name={Name} ip={Ip} rememberMe={RememberMe}",
                    userId, WorkerNumber, userName ?? "(未知)", ip, RememberMe);
                return LocalRedirect(ReturnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning(
                    "登录锁定(超过 5 次失败): userId={UserId} workerNumber={WorkerNumber} name={Name} ip={Ip}",
                    userId, WorkerNumber, userName ?? "(未知)", ip);
                return RedirectToLoginWithError("账号已锁定,请 5 分钟后重试", ReturnUrl);
            }

            if (result.IsNotAllowed)
            {
                _logger.LogWarning(
                    "登录被拒绝(账户未激活/未验证): userId={UserId} workerNumber={WorkerNumber} name={Name} ip={Ip}",
                    userId, WorkerNumber, userName ?? "(未知)", ip);
                return RedirectToLoginWithError("账号未激活,请联系管理员", ReturnUrl);
            }

            _logger.LogWarning(
                "登录失败(密码错): userId={UserId} workerNumber={WorkerNumber} name={Name} ip={Ip} requiresTwoFactor={Req2fa}",
                userId, WorkerNumber, userName ?? "(未知)", ip, result.RequiresTwoFactor);
            return RedirectToLoginWithError("员工号或密码错误", ReturnUrl);
        }

        private async Task<(int? userId, string? name)> GetUserInfoByWorkerNumberAsync(string workerNumber)
        {
            var user = await _signInManager.UserManager.FindByNameAsync(workerNumber);
            return (user?.Id, user?.Name);
        }

        private IActionResult RedirectToLoginWithError(string error, string returnUrl)
        {
            var qs = $"?error={Uri.EscapeDataString(error)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return LocalRedirect("~/login" + qs);
        }

        [HttpPost("Logout")]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            // 注意:Identity 体系中 User.Identity.Name = 用户名(本系统=WorkerNumber);
            // 真实姓名走单独的 "DisplayName" claim(由 DisplayNameClaimsFactory 写入)
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var workerNumber = User.Identity?.Name;
            var displayName = User.FindFirstValue(BootstrapBlazor.OnlyServer1.Services.Identity.ClaimsPrincipalExtensions.DisplayNameClaim);
            var ip = GetClientIp();

            await _signInManager.SignOutAsync();

            // (2026-08-14) 清掉「记住我」写入的两个非 HttpOnly cookie。
            // 设 Max-Age=-1 让浏览器立即过期。路径必须跟写入时一致(/),否则删不掉。
            Response.Cookies.Delete("remember_user", new CookieOptions { Path = "/" });
            Response.Cookies.Delete("remember_pass", new CookieOptions { Path = "/" });

            _logger.LogInformation(
                "用户登出: userId={UserId} workerNumber={WorkerNumber} name={Name} ip={Ip}",
                userId, workerNumber, displayName ?? "(未知)", ip);
            return LocalRedirect("/login");
        }

        private string GetClientIp()
        {
            // X-Forwarded-For 兼容反代场景(若前端有 nginx/cdn);本机直接拿到 RemoteIpAddress
            var fwd = HttpContext.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(first)) return first;
            }
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}

