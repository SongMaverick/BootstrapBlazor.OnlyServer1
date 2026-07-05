using BootstrapBlazor.Components;

namespace BootstrapBlazor.OnlyServer1.Helpers;

/// <summary>
/// 统一样式的 Toast 扩展,避免 Razor 页面里反复写 new ToastOption {...}。
/// 默认 Delay 2500ms。
/// 注意:方法名避开 BootstrapBlazor.Components.ToastServiceExtensions 的同名方法
/// (那个用 (message, title, autoHide) 签名),用 Show*/Print* 前缀。
/// </summary>
public static class AppToastExtensions
{
    public static Task ShowSuccess(this ToastService svc, string content, int delay = 2500)
        => svc.Show(new ToastOption { Category = ToastCategory.Success, Content = content, Delay = delay });

    public static Task ShowWarning(this ToastService svc, string content, int delay = 2500)
        => svc.Show(new ToastOption { Category = ToastCategory.Warning, Content = content, Delay = delay });

    public static Task ShowError(this ToastService svc, string content, int delay = 2500)
        => svc.Show(new ToastOption { Category = ToastCategory.Error, Content = content, Delay = delay });

    public static Task ShowInfo(this ToastService svc, string content, int delay = 2500)
        => svc.Show(new ToastOption { Category = ToastCategory.Information, Content = content, Delay = delay });
}
