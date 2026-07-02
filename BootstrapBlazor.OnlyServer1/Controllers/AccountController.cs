using BootstrapBlazor.OnlyServer1.Models;
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
        public async Task<IActionResult> Login(
            [FromForm] string WorkerNumber,
            [FromForm] string Password,
            [FromForm] string? ReturnUrl)
        {
            if (string.IsNullOrEmpty(ReturnUrl) || !Url.IsLocalUrl(ReturnUrl))
                ReturnUrl = "~/";

            if (string.IsNullOrWhiteSpace(WorkerNumber) || string.IsNullOrWhiteSpace(Password))
            {
                return RedirectToLoginWithError("员工号和密码不能为空", ReturnUrl);
            }

            var result = await _signInManager.PasswordSignInAsync(
                WorkerNumber, Password, isPersistent: true, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("登录成功: WorkerNumber={WorkerNumber}", WorkerNumber);
                return LocalRedirect(ReturnUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("登录锁定: WorkerNumber={WorkerNumber}", WorkerNumber);
                return RedirectToLoginWithError("账号已锁定,请 5 分钟后重试", ReturnUrl);
            }

            _logger.LogWarning("登录失败: WorkerNumber={WorkerNumber}, Reason={Reason}",
                WorkerNumber, result.ToString());
            return RedirectToLoginWithError("员工号或密码错误", ReturnUrl);
        }

        private IActionResult RedirectToLoginWithError(string error, string returnUrl)
        {
            var qs = $"?error={Uri.EscapeDataString(error)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return LocalRedirect("~/login" + qs);
        }

        [HttpPost("Logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("用户登出");
            return LocalRedirect("/login");
        }
    }
}
