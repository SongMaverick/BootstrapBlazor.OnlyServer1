using System.Security.Claims;
using BootstrapBlazor.Components;
using BootstrapBlazor.OnlyServer1.Models.Identity;
using BootstrapBlazor.OnlyServer1.Services;
using BootstrapBlazor.OnlyServer1.Services.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.OnlyServer1.Components.Shared
{
    public sealed partial class MainLayout
    {
        [Inject]
        private AuthenticationStateProvider? AuthStateProvider { get; set; }

        [Inject]
        private UsersService? UsersService { get; set; }

        [Inject]
        private CurrentUserAccessor? CurrentUser { get; set; }

        [Inject]
        private ToastService? ToastService { get; set; }

        [Inject]
        private IOptions<IdentityOptions>? IdentityOptions { get; set; }

        private bool UseTabSet { get; set; } = true;

        private string Theme { get; set; } = "";

        private bool IsOpen { get; set; }

        private bool IsFixedHeader { get; set; } = true;

        private bool IsFixedFooter { get; set; } = true;

        private bool IsFullSide { get; set; } = true;

        private bool ShowFooter { get; set; } = true;

        private List<MenuItem>? Menus { get; set; }

        // 从 ClaimsPrincipal 取出的用户展示信息
        private string _displayName = "";
        private string _userName = "";

        // 公共菜单:所有已登录用户可见
        private static readonly List<MenuItem> PublicMenus = new()
        {
            new() { Text = "首页", Icon = "fa-solid fa-fw fa-flag", Url = "/", Match = NavLinkMatch.All },
            new() { Text = "查询图号", Icon = "fa-solid fa-fw fa-magnifying-glass", Url = "/find-drawing-number" },
        };

        // 业务菜单:按角色过滤
        //   Admin    —— 系统管理员,全权
        //   Operator —— 业务操作员(图号分配 / 订单处理 / 图名维护)
        //   Viewer   —— 只读(可看订单)
        // 调整后必须让用户重新登录才能看到新菜单(Cookie Claims 缓存)
        private static readonly List<(MenuItem item, string[] requiredRoles)> BusinessMenus = new()
        {
            (new() { Text = "订单列表", Icon = "fa-solid fa-fw fa-file-invoice", Url = "/orders" },
                RoleNames.All),
            (new() { Text = "图名列表", Icon = "fa-solid fa-fw fa-list", Url = "/drawing-names" },
                new[] { RoleNames.Admin, RoleNames.Operator }),
            (new() { Text = "人员列表", Icon = "fa-solid fa-fw fa-users", Url = "/user-management" },
                new[] { RoleNames.Admin }),
            (new() { Text = "部门管理", Icon = "fa-solid fa-fw fa-building", Url = "/department-management" },
                new[] { RoleNames.Admin }),
            (new() { Text = "角色权限", Icon = "fa-solid fa-fw fa-shield-halved", Url = "/roles" },
                new[] { RoleNames.Admin }),
            (new() { Text = "用户角色", Icon = "fa-solid fa-fw fa-user-tag", Url = "/user-roles" },
                new[] { RoleNames.Admin }),
            // 注:"添加图号"(/add-drawing-numbers/{orderId:int})和"修改图号"
            // (/update-drawing-number/{id:int}/{drawingType})的路由都需要 URL 参数,
            // 菜单项无法带参数 → 点击直接 404。入口改由业务页内嵌按钮触发:
            //   - 添加图号:Orders 表格工具栏 / OrderDrawingNumbers 列表页顶部按钮
            //   - 修改图号:OrderDrawingNumbers 表格内 "修改" 按钮(弹窗编辑)
        };

        protected override async Task OnInitializedAsync()
        {
            // 走 AuthStateProvider 而不是 IHttpContextAccessor:
            // Blazor Server 交互式渲染下,MainLayout.OnInitializedAsync 触发时
            // IHttpContextAccessor.HttpContext 经常是 null/空,导致 IsAuthenticated
            // 永远返回 false,菜单被错误地只显示公共项。
            // AuthStateProvider 在任何生命周期都可靠。
            if (AuthStateProvider == null)
            {
                Menus = PublicMenus;
                return;
            }

            var state = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = state.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                _displayName = user.FindFirst(ClaimsPrincipalExtensions.DisplayNameClaim)?.Value
                                ?? user.Identity.Name
                                ?? "";
                _userName = user.FindFirst(ClaimsPrincipalExtensions.GIDClaim)?.Value
                            ?? user.Identity.Name
                            ?? "";
                Menus = GetIconSideMenuItems(user);
            }
            else
            {
                Menus = PublicMenus;
            }
        }

        /// <summary>
        /// 根据 ClaimsPrincipal 过滤业务菜单(角色匹配则加入)。
        /// 公共菜单始终包含。
        /// </summary>
        private static List<MenuItem> GetIconSideMenuItems(ClaimsPrincipal user)
        {
            var all = new List<MenuItem>(PublicMenus);
            foreach (var (item, roles) in BusinessMenus)
            {
                if (roles.Any(r => user.IsInRole(r)))
                {
                    all.Add(item);
                }
            }
            return all;
        }

        // ===== 修改密码弹窗 =====

        private bool _changePasswordModalVisible = false;
        private string _oldPassword = "";
        private string _newPassword = "";
        private string _confirmPassword = "";
        private bool _changePasswordSaving = false;

        private void ShowChangePasswordModal()
        {
            _oldPassword = "";
            _newPassword = "";
            _confirmPassword = "";
            _changePasswordModalVisible = true;
        }

        private void CloseChangePasswordModal()
        {
            if (_changePasswordSaving) return;
            _changePasswordModalVisible = false;
        }

        private async Task HandleChangePasswordAsync()
        {
            if (AuthStateProvider == null || UsersService == null)
            {
                await ShowToast("服务未就绪,请稍后再试", ToastCategory.Error);
                return;
            }

            var state = await AuthStateProvider.GetAuthenticationStateAsync();
            var currentUserIdClaim = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(currentUserIdClaim, out var currentUserId) || currentUserId <= 0)
            {
                await ShowToast("登录状态已失效,请重新登录", ToastCategory.Error);
                return;
            }

            if (string.IsNullOrEmpty(_oldPassword) || string.IsNullOrEmpty(_newPassword))
            {
                await ShowToast("旧密码和新密码均不能为空", ToastCategory.Warning);
                return;
            }
            var minLen = IdentityOptions?.Value.Password.RequiredLength ?? 6;
            if (_newPassword.Length < minLen)
            {
                await ShowToast($"新密码长度至少 {minLen} 位", ToastCategory.Warning);
                return;
            }
            if (_newPassword != _confirmPassword)
            {
                await ShowToast("两次输入的新密码不一致", ToastCategory.Warning);
                return;
            }

            _changePasswordSaving = true;
            try
            {
                var (ok, error) = await UsersService.ChangePasswordAsync(currentUserId, _oldPassword, _newPassword);

                if (ok)
                {
                    await ShowToast("密码修改成功", ToastCategory.Success);
                    _changePasswordModalVisible = false;
                }
                else
                {
                    await ShowToast(error ?? "密码修改失败,请重试",
                        error?.Contains("旧密码") == true ? ToastCategory.Error : ToastCategory.Warning);
                }
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
