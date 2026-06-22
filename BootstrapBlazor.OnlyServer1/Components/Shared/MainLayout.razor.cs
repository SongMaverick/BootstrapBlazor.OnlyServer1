using BootstrapBlazor.Components;
using Microsoft.AspNetCore.Components.Routing;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Helpers;
using Microsoft.AspNetCore.Components;
using SqlSugar;

namespace BootstrapBlazor.OnlyServer1.Components.Shared
{
    public sealed partial class MainLayout
    {
        [Inject]
        private AuthService? AuthService { get; set; }

        [Inject]
        private NavigationManager? NavigationManager { get; set; }

        [Inject]
        private ToastService? ToastService { get; set; }

        [Inject]
        private ISqlSugarClient? Db { get; set; }

        private bool UseTabSet { get; set; } = true;

        private string Theme { get; set; } = "";

        private bool IsOpen { get; set; }

        private bool IsFixedHeader { get; set; } = true;

        private bool IsFixedFooter { get; set; } = true;

        private bool IsFullSide { get; set; } = true;

        private bool ShowFooter { get; set; } = true;

        private List<MenuItem>? Menus { get; set; }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            if (AuthService != null)
            {
                AuthService.OnChange += HandleAuthStateChanged;
            }

            if (AuthService != null && !AuthService.IsLoggedIn && NavigationManager != null)
            {
                // forceLoad: true 会重建 SignalR circuit,确保 scoped AuthService 拿到干净状态
                NavigationManager.NavigateTo("/login", forceLoad: true);
                return;
            }

            Menus = GetIconSideMenuItems();
        }

        private async void HandleAuthStateChanged()
        {
            // 登录/登出/自动登录成功后,CurrentUser 已变化,
            // 强制刷新 layout 让 header 里的用户名立即更新(不再需要 F5)
            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch
            {
                // 组件已销毁,忽略
            }
        }

        private static List<MenuItem> GetIconSideMenuItems()
        {
            var menus = new List<MenuItem>
            {
                new() { Text = "首页", Icon = "fa-solid fa-fw fa-flag", Url = "/" , Match = NavLinkMatch.All},
                new() { Text = "人员列表", Icon = "fa-solid fa-fw fa-users", Url = "/users" },
                new() { Text = "订单列表", Icon = "fa-solid fa-fw fa-file-invoice", Url = "/orders" },
                new() { Text = "查询图号", Icon = "fa-solid fa-fw fa-magnifying-glass", Url = "/find-drawing-number" },
                new() { Text = "图名列表", Icon = "fa-solid fa-fw fa-list", Url = "/drawing-names" }
            };

            return menus;
        }

        private async Task HandleLogout()
        {
            if (AuthService != null)
            {
                AuthService.Logout();
            }

            if (ToastService != null)
            {
                await ToastService.Show(new ToastOption()
                {
                    Category = ToastCategory.Success,
                    Content = "退出登录成功！",
                    Delay = 2000
                });
            }

            if (NavigationManager != null)
            {
                NavigationManager.NavigateTo("/login");
            }
        }

        public void Dispose()
        {
            // 取消订阅,避免 component 销毁后事件 handler 还在被调用的内存泄漏
            if (AuthService != null)
            {
                AuthService.OnChange -= HandleAuthStateChanged;
            }
        }

        // ===== 修改密码弹窗 =====

        private bool _changePasswordModalVisible = false;
        private string _oldPassword = "";
        private string _newPassword = "";
        private string _confirmPassword = "";
        private bool _changePasswordSaving = false;

        private void ShowChangePasswordModal()
        {
            // 重置字段 + 打开
            _oldPassword = "";
            _newPassword = "";
            _confirmPassword = "";
            _changePasswordModalVisible = true;
        }

        private void CloseChangePasswordModal()
        {
            if (_changePasswordSaving) return;  // 提交中不允许关
            _changePasswordModalVisible = false;
        }

        private async Task HandleChangePasswordAsync()
        {
            // 前端基本校验
            if (AuthService?.CurrentUser == null)
            {
                await ShowToast("登录状态已失效,请重新登录", ToastCategory.Error);
                return;
            }
            if (string.IsNullOrEmpty(_oldPassword) || string.IsNullOrEmpty(_newPassword))
            {
                await ShowToast("旧密码和新密码均不能为空", ToastCategory.Warning);
                return;
            }
            if (_newPassword.Length < 6)
            {
                await ShowToast("新密码长度至少 6 位", ToastCategory.Warning);
                return;
            }
            if (_newPassword != _confirmPassword)
            {
                await ShowToast("两次输入的新密码不一致", ToastCategory.Warning);
                return;
            }

            if (Db == null)
            {
                await ShowToast("数据库未就绪,请稍后再试", ToastCategory.Error);
                return;
            }

            _changePasswordSaving = true;
            try
            {
                var currentUserId = AuthService.CurrentUser.Id;
                var oldEncrypted = MD5Helper.Encrypt(_oldPassword);

                // 用旧密码的 MD5 从 DB 拉这条记录,匹配才算验证通过
                var user = await Db.Queryable<Models.Users>()
                    .Where(u => u.Id == currentUserId && !u.IsDelete)
                    .FirstAsync();

                if (user == null || user.Password != oldEncrypted)
                {
                    await ShowToast("旧密码不正确", ToastCategory.Error);
                    return;
                }

                // 写入新密码的 MD5
                var newEncrypted = MD5Helper.Encrypt(_newPassword);
                var rows = await Db.Updateable<Models.Users>()
                    .SetColumns(u => u.Password == newEncrypted)
                    .Where(u => u.Id == currentUserId)
                    .ExecuteCommandAsync();

                if (rows > 0)
                {
                    await ShowToast("密码修改成功", ToastCategory.Success);
                    _changePasswordModalVisible = false;
                }
                else
                {
                    await ShowToast("密码修改失败,请重试", ToastCategory.Error);
                }
            }
            catch (Exception ex)
            {
                // 错误日志会走 NLog(Program.cs 已经接了)
                await ShowToast($"密码修改出错:{ex.Message}", ToastCategory.Error);
            }
            finally
            {
                _changePasswordSaving = false;
            }
        }

        private async Task ShowToast(string message, ToastCategory category)
        {
            if (ToastService == null) return;
            await ToastService.Show(new ToastOption()
            {
                Category = category,
                Content = message,
                Delay = 2000
            });
        }
    }
}