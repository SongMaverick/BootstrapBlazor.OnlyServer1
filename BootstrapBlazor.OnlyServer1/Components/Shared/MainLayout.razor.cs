using BootstrapBlazor.Components;
using Microsoft.AspNetCore.Components.Routing;
using BootstrapBlazor.OnlyServer1.Services;
using Microsoft.AspNetCore.Components;

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
    }
}