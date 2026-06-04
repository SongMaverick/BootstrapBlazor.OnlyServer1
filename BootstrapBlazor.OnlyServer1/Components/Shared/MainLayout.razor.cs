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

            if (AuthService != null && !AuthService.IsLoggedIn && NavigationManager != null)
            {
                NavigationManager.NavigateTo("/login");
                return;
            }

            Menus = GetIconSideMenuItems();
        }

        private static List<MenuItem> GetIconSideMenuItems()
        {
            var menus = new List<MenuItem>
            {
                new() { Text = "Index", Icon = "fa-solid fa-fw fa-flag", Url = "/" , Match = NavLinkMatch.All},
                new() { Text = "人员列表", Icon = "fa-solid fa-fw fa-users", Url = "/users" },
                new() { Text = "订单列表", Icon = "fa-solid fa-fw fa-file-invoice", Url = "/orders" }
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
    }
}
